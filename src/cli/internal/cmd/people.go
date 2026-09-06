package cmd

import (
	"context"
	"fmt"
	"strings"
	"text/tabwriter"

	"github.com/spf13/cobra"

	openapi_types "github.com/oapi-codegen/runtime/types"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/api"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/client"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/config"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/render"
)

// The people of the organization, from the console. Every command here is an
// administrator's under their own session (Specification §6.4): who may be in
// this organization is a decision about people, and an agent that could invite
// one could invite itself a second identity.
//
// This is the half of `advice` in root.go that used to have no command at all —
// `administer-organization` was deliberately absent from the table, because
// suggesting a command that does not exist is the failure ADR 0010 is about.
// It exists now, so the entry does too.

func newUsers(g *globals) *cobra.Command {
	command := &cobra.Command{
		Use:     "users",
		Aliases: []string{"user"},
		Short:   "The people of this organization. An administrator only.",
		Long: "Everything here needs a session token and an administrator behind it. The\n" +
			"instance sends no email, which is the price of having no external dependency\n" +
			"to operate: an invitation is a link to hand over, and a password reset is an\n" +
			"administrator setting one.",
		Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			people, _, err := g.people(command.Context())
			if err != nil {
				return err
			}
			if g.json {
				return render.JSON(g.out(), people)
			}

			table := tabwriter.NewWriter(g.out(), 0, 0, 2, ' ', 0)
			for _, person := range people {
				fmt.Fprintf(table, "%s\t%s\t%s\t%s\n",
					person.Email, person.Name, role(person), standingOf(person))
			}
			return table.Flush()
		},
	}
	command.AddCommand(
		newUsersInvite(g),
		newUsersPassword(g),
		newUsersDeactivate(g),
		newUsersReactivate(g))
	return command
}

func newUsersInvite(g *globals) *cobra.Command {
	var name string
	var administrator bool

	command := &cobra.Command{
		Use:   "invite <email>",
		Short: "Write out an invitation, and print the link once.",
		Long: "The instance sends no email. What comes back is a link an administrator\n" +
			"hands over, and the code in it lives in the fragment so that a working\n" +
			"invitation never reaches an access log.\n\n" +
			"**The link appears here and nowhere else, ever.** It is a credential, so it\n" +
			"goes to stdout on its own line and everything a person reads goes to stderr.\n" +
			"An invitation that was lost is withdrawn and written again.",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			if strings.TrimSpace(name) == "" {
				return &config.UsageError{Message: "--name says what to call them; the invitation carries it."}
			}

			resolved, c, err := g.instance()
			if err != nil {
				return err
			}

			resp, err := c.InviteUserWithResponse(command.Context(), api.InviteUserRequest{
				Email:           args[0],
				Name:            name,
				IsAdministrator: &administrator,
			})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("an invitation")
			}

			written := *resp.JSON200

			// Relative, as everything the instance hands a client is: it does not
			// know what address a browser reached it at, and this CLI has one
			// (`docs/api.md`). Joining the halves is the client's, here for the
			// same reason as in `login`.
			link := at(resolved.Address, written.Link)

			fmt.Fprintf(g.msg(), "%s is invited as %s, and the invitation runs out %s.\n",
				written.Invitation.Email, role(api.User{IsAdministrator: written.Invitation.IsAdministrator}),
				when(&written.Invitation.ExpiresAt))
			fmt.Fprintln(g.msg(), "The link is below and this is the only time it is shown. Hand it over.")

			if g.json {
				return render.JSON(g.out(), map[string]any{
					"invitation": written.Invitation,
					"link":       link,
				})
			}
			fmt.Fprintln(g.out(), link)
			return nil
		},
	}
	command.Flags().StringVar(&name, "name", "", "what to call them")
	command.Flags().BoolVar(&administrator, "administrator", false, "they may administer this organization")
	return command
}

func newUsersPassword(g *globals) *cobra.Command {
	var raw bool

	command := &cobra.Command{
		Use:   "password <email>",
		Short: "Set somebody's password, and end their sessions. An administrator only.",
		Long: "There is no reset by email, because the instance sends none. An\n" +
			"administrator sets a password and hands it over, and every session and\n" +
			"token of that person stops working at once.\n\n" +
			"The password comes from stdin and never from an argument, for the reason\n" +
			"every value in this CLI does: an argument is in the shell history and in\n" +
			"`ps`.\n\n" +
			"    pwgen -s 24 1 | vaultaffe users password somebody@example.com",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			password, err := readValue(g.env.Stdin, raw)
			if err != nil {
				return err
			}

			people, c, err := g.people(command.Context())
			if err != nil {
				return err
			}
			id, err := whoIs(people, args[0])
			if err != nil {
				return err
			}

			resp, err := c.ResetPasswordWithResponse(command.Context(), id,
				api.ResetPasswordRequest{Password: password})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a person")
			}
			fmt.Fprintf(g.msg(),
				"%s has that password now, and every session and token of theirs has ended.\n",
				resp.JSON200.Email)
			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}
			return nil
		},
	}
	command.Flags().BoolVar(&raw, "raw", false, "keep the bytes exactly as they came, trailing newline included")
	return command
}

func newUsersDeactivate(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "deactivate <email>",
		Short: "Take somebody out of the organization. Every token of theirs stops working.",
		Long: "The row stays, deactivated rather than deleted, so that everything they\n" +
			"ever signed in the change log keeps an author.",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			return g.turn(command.Context(), args[0], false)
		},
	}
}

func newUsersReactivate(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "reactivate <email>",
		Short: "Put them back. Their tokens work again, the revoked ones excepted.",
		Args:  cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			return g.turn(command.Context(), args[0], true)
		},
	}
}

func (g *globals) turn(ctx context.Context, who string, back bool) error {
	people, c, err := g.people(ctx)
	if err != nil {
		return err
	}
	id, err := whoIs(people, who)
	if err != nil {
		return err
	}

	var person *api.User
	if back {
		resp, err := c.ReactivateUserWithResponse(ctx, id)
		if err != nil {
			return client.Transport(err)
		}
		if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
			return err
		}
		person = resp.JSON200
	} else {
		resp, err := c.DeactivateUserWithResponse(ctx, id)
		if err != nil {
			return client.Transport(err)
		}
		if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
			return err
		}
		person = resp.JSON200
	}
	if person == nil {
		return unreadable("a person")
	}

	fmt.Fprintf(g.msg(), "%s is %s.\n", person.Email,
		verb(back, "back in this organization", "out of this organization, and so is every token of theirs"))
	if g.json {
		return render.JSON(g.out(), person)
	}
	return nil
}

func newInvitations(g *globals) *cobra.Command {
	command := &cobra.Command{
		Use:     "invitations",
		Aliases: []string{"invitation"},
		Short:   "The invitations of this organization, in whatever state. An administrator only.",
		Long: "The code is in none of this: a listing says who was invited, what accepting\n" +
			"makes them and where the invitation stands, and the link was shown once,\n" +
			"when it was written.",
		Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			_, c, err := g.instance()
			if err != nil {
				return err
			}

			resp, err := c.ReadInvitationsWithResponse(command.Context())
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("an invitation listing")
			}
			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}

			table := tabwriter.NewWriter(g.out(), 0, 0, 2, ' ', 0)
			for _, invitation := range *resp.JSON200 {
				fmt.Fprintf(table, "%s\t%s\t%s\t%s\truns out %s\n",
					invitation.Id, invitation.Email, invitation.Name,
					invitation.State, when(&invitation.ExpiresAt))
			}
			return table.Flush()
		},
	}
	command.AddCommand(newInvitationsWithdraw(g))
	return command
}

func newInvitationsWithdraw(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "withdraw <id>",
		Short: "Take an invitation back before anybody used it. The link stops working.",
		Args:  cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			id, err := parseID(args[0])
			if err != nil {
				return err
			}

			_, c, err := g.instance()
			if err != nil {
				return err
			}

			resp, err := c.WithdrawInvitationWithResponse(command.Context(), id)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("an invitation")
			}
			fmt.Fprintf(g.msg(), "The invitation to %s is withdrawn, and its link works nowhere.\n",
				resp.JSON200.Email)
			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}
			return nil
		},
	}
}

func newOrganization(g *globals) *cobra.Command {
	command := &cobra.Command{
		Use:     "organization",
		Aliases: []string{"org"},
		Short:   "What this organization is called.",
		Long: "Reading it is anybody's; renaming it is an administrator's under their own\n" +
			"session. There is exactly one organization in the MVP and it is called\n" +
			"Default until somebody says otherwise.",
		Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			_, c, err := g.instance()
			if err != nil {
				return err
			}

			resp, err := c.ReadOrganizationWithResponse(command.Context())
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("an organization")
			}
			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}
			fmt.Fprintln(g.out(), resp.JSON200.Name)
			return nil
		},
	}
	command.AddCommand(newOrganizationRename(g))
	return command
}

func newOrganizationRename(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "rename <name>",
		Short: "Rename this organization. An administrator only.",
		Args:  cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			_, c, err := g.instance()
			if err != nil {
				return err
			}

			resp, err := c.RenameOrganizationWithResponse(command.Context(),
				api.NameRequest{Name: args[0]})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("an organization")
			}
			fmt.Fprintf(g.msg(), "This organization is called %s.\n", resp.JSON200.Name)
			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}
			return nil
		},
	}
}

// people reads the organization's people once, so that the commands below can
// take an address where the API takes an id. Everything a person types in this
// CLI is a name — a project, an environment, a key — and an address is what a
// person knows somebody here by; a UUID on the command line would be this CLI
// asking somebody to read the API's mind.
func (g *globals) people(ctx context.Context) ([]api.User, *client.Client, error) {
	_, c, err := g.instance()
	if err != nil {
		return nil, nil, err
	}

	resp, err := c.ReadUsersWithResponse(ctx)
	if err != nil {
		return nil, nil, client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return nil, nil, err
	}
	if resp.JSON200 == nil {
		return nil, nil, unreadable("a listing of people")
	}
	return *resp.JSON200, c, nil
}

// whoIs turns what somebody typed into the id the API takes: an address, or an
// id for whoever already has one. An address that is not here is a usage error
// rather than a round trip, because the listing that would have answered it has
// already been read.
func whoIs(people []api.User, who string) (openapi_types.UUID, error) {
	wanted := strings.TrimSpace(who)

	for _, person := range people {
		if strings.EqualFold(person.Email, wanted) || person.Id.String() == wanted {
			return person.Id, nil
		}
	}

	addresses := make([]string, 0, len(people))
	for _, person := range people {
		addresses = append(addresses, person.Email)
	}
	return openapi_types.UUID{}, &config.UsageError{Message: fmt.Sprintf(
		"nobody here is %q. This organization has %s.", who, orNone(strings.Join(addresses, ", ")))}
}

func role(person api.User) string {
	return verb(person.IsAdministrator, "administrator", "member")
}

func standingOf(person api.User) string {
	if person.DeactivatedAt != nil {
		return "deactivated " + when(person.DeactivatedAt)
	}
	return "active"
}

package cmd

import (
	"context"
	"fmt"
	"strings"
	"text/tabwriter"
	"time"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/api"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/client"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/config"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/render"
)

func newTokens(g *globals) *cobra.Command {
	command := &cobra.Command{
		Use:     "tokens",
		Aliases: []string{"token"},
		Short:   "The tokens of this organization. Never their values.",
		Long: "Listing is not human-only: a revocation list an agent cannot read is not\n" +
			"one. Creating and revoking are, because a token is itself a secret and one\n" +
			"created through an agent's CLI would be printed into that agent's context.",
		Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			_, c, err := g.instance()
			if err != nil {
				return err
			}

			resp, err := c.ReadTokensWithResponse(command.Context())
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a token listing")
			}
			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}

			table := tabwriter.NewWriter(g.out(), 0, 0, 2, ' ', 0)
			for _, token := range *resp.JSON200 {
				fmt.Fprintf(table, "%s\t%s\t%s\t%s\t%s\n",
					token.Id, token.Kind, valueOr(token.Name, "—"),
					strings.Join(token.Scopes, ","), standing(token))
			}
			return table.Flush()
		},
	}
	command.AddCommand(newTokensCreate(g), newTokensRevoke(g))
	return command
}

func newTokensCreate(g *globals) *cobra.Command {
	var kind, expires string
	var scopes []string

	command := &cobra.Command{
		Use:   "create <name>",
		Short: "Create a service or an agent token. A person only.",
		Long: "This is the way an agent gets a token at all: a person creates one under\n" +
			"their own session and hands it over in VAULTAFFE_TOKEN. It is human-only\n" +
			"because the answer is itself a secret — one created here under an agent's\n" +
			"token would be printed straight into that agent's context.\n\n" +
			"**The value is printed exactly once**, by this command, and appears in no\n" +
			"listing ever again.\n\n" +
			"Scopes omitted mean the default of the kind: everything for an agent token,\n" +
			"because the point is attribution and not restriction, and `names` and\n" +
			"`read` for a service token. --project and --environment narrow the binding\n" +
			"and have to be given here explicitly: the directory you happen to stand in\n" +
			"does not silently narrow a token.",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			if kind != "agent" && kind != "service" {
				return &config.UsageError{Message: fmt.Sprintf(
					"--kind %q: a token created here is `agent` or `service`. A session comes from signing in.", kind)}
			}

			resolved, c, err := g.instance()
			if err != nil {
				return err
			}

			request := api.CreateTokenRequest{Kind: kind, Name: args[0]}
			if len(scopes) > 0 {
				request.Scopes = &scopes
			}
			if strings.TrimSpace(expires) != "" {
				moment, err := time.Parse(time.RFC3339, expires)
				if err != nil {
					return &config.UsageError{Message: fmt.Sprintf(
						"--expires %q is not a moment: 2026-12-31T00:00:00Z.", expires)}
				}
				request.ExpiresAt = &moment
			}

			// The binding is by id and the CLI speaks names, so the names are
			// looked up here. Only what was passed on the command line counts:
			// the binding of the directory somebody happens to stand in must not
			// quietly become the binding of a token.
			if strings.TrimSpace(g.project) != "" {
				binding, err := bindingFor(command.Context(), c, g.project, g.environment)
				if err != nil {
					return err
				}
				request.Bindings = &[]api.Binding{binding}
			} else if strings.TrimSpace(g.environment) != "" {
				return &config.UsageError{Message: "--environment narrows a binding inside a project: pass --project too."}
			}

			resp, err := c.CreateTokenWithResponse(command.Context(), request)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a token")
			}

			issued := *resp.JSON200
			fmt.Fprintf(g.msg(), "%s is a %s token of %s, with %s, reaching %s.\n",
				valueOr(issued.Token.Name, issued.Token.Id.String()), issued.Token.Kind, resolved.Address,
				strings.Join(issued.Token.Scopes, ","), reach(issued.Token))
			fmt.Fprintln(g.msg(), "The value is below and this is the only time it is shown. Hand it over as VAULTAFFE_TOKEN.")

			if g.json {
				return render.JSON(g.out(), issued)
			}
			fmt.Fprintln(g.out(), issued.Value)
			return nil
		},
	}
	command.Flags().StringVar(&kind, "kind", "agent", "`agent` or `service`")
	command.Flags().StringSliceVar(&scopes, "scopes", nil, "names, read, write, delete; the kind's default when omitted")
	command.Flags().StringVar(&expires, "expires", "", "when it stops working, as 2026-12-31T00:00:00Z")
	return command
}

func newTokensRevoke(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "revoke <id>",
		Short: "Revoke a token. A person only.",
		Long: "The row stays, revoked rather than deleted, so that everything it ever\n" +
			"signed in the change log keeps an author.",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			id, err := parseID(args[0])
			if err != nil {
				return err
			}

			_, c, err := g.instance()
			if err != nil {
				return err
			}

			resp, err := c.RevokeTokenWithResponse(command.Context(), id)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a token")
			}
			fmt.Fprintf(g.msg(), "%s is revoked and works nowhere from now on.\n",
				valueOr(resp.JSON200.Name, resp.JSON200.Id.String()))
			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}
			return nil
		},
	}
}

// bindingFor turns the names a person types into the ids a binding is made of.
// A project that is not there is found out here rather than as a validation
// failure about a field nobody typed.
func bindingFor(ctx context.Context, c *client.Client, project, environment string) (api.Binding, error) {
	resp, err := c.ReadProjectWithResponse(ctx, project)
	if err != nil {
		return api.Binding{}, client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return api.Binding{}, err
	}
	if resp.JSON200 == nil {
		return api.Binding{}, unreadable("a project")
	}

	binding := api.Binding{ProjectId: resp.JSON200.Id}
	if strings.TrimSpace(environment) == "" {
		return binding, nil
	}

	for _, candidate := range resp.JSON200.Environments {
		if candidate.Name == environment {
			id := candidate.Id
			binding.EnvironmentId = &id
			return binding, nil
		}
	}
	return api.Binding{}, &config.UsageError{Message: fmt.Sprintf(
		"%s has no environment %q: it has %s.", project, environment, strings.Join(names(resp.JSON200.Environments), ", "))}
}

func standing(token api.Token) string {
	switch {
	case token.RevokedAt != nil:
		return "revoked " + when(token.RevokedAt)
	case token.ExpiresAt != nil:
		return "expires " + when(token.ExpiresAt)
	default:
		return "active"
	}
}

func reach(token api.Token) string {
	if token.ReachesTheWholeOrganization || len(token.Bindings) == 0 {
		return "the whole organization"
	}
	return fmt.Sprintf("%d bound place(s)", len(token.Bindings))
}

package cmd

import (
	"context"
	"errors"
	"fmt"
	"strings"
	"text/tabwriter"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/api"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/client"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/render"
)

// The catalogue commands: create, list, delete, restore — and, since the stage
// after the MVP, purge. They were as much administration as Stage 1 needed to
// bring an instance up from the console, and complete CLI parity for human
// administration was explicitly not an MVP gate (Specification §6.6, §12); it is
// what §6.6 stage 3 asked for and what `people.go`, `purge.go` and `notice.go`
// finish. The rule that held while it was missing holds anyway: no refusal ever
// offers a command that does not exist.

func newProjects(g *globals) *cobra.Command {
	var deleted bool

	command := &cobra.Command{
		Use:     "projects",
		Aliases: []string{"project"},
		Short:   "The projects this token reaches, and their environments.",
		Args:    cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			return g.listProjects(command.Context(), deleted)
		},
	}
	command.Flags().BoolVar(&deleted, "deleted", false, "what is deleted and still recoverable, instead")
	command.AddCommand(newProjectsCreate(g), newProjectsDelete(g), newProjectsRestore(g), newProjectsPurge(g))
	return command
}

func (g *globals) listProjects(ctx context.Context, deleted bool) error {
	_, c, err := g.instance()
	if err != nil {
		return err
	}

	resp, err := c.ReadProjectsWithResponse(ctx, &api.ReadProjectsParams{Deleted: &deleted})
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}
	if resp.JSON200 == nil {
		return unreadable("a catalogue")
	}

	if g.json {
		return render.JSON(g.out(), resp.JSON200)
	}

	table := tabwriter.NewWriter(g.out(), 0, 0, 2, ' ', 0)
	for _, project := range *resp.JSON200 {
		fmt.Fprintf(table, "%s\t%s\n", project.Name, strings.Join(names(project.Environments), " "))
	}
	return table.Flush()
}

func newProjectsCreate(g *globals) *cobra.Command {
	var environments []string
	var none bool

	command := &cobra.Command{
		Use:   "create <name>",
		Short: "Create a project, with its environments.",
		Long: "A project arrives with `dev`, `staging` and `prod` unless others are named.\n" +
			"--no-environments creates none, which is the explicit empty list rather than\n" +
			"a forgotten flag.\n\n" +
			"Creating a project needs a token that reaches the whole organization, and\n" +
			"it is not human-only: creating projects and environments is something an\n" +
			"agent may do.",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			_, c, err := g.instance()
			if err != nil {
				return err
			}

			request := api.CreateProjectRequest{Name: args[0]}
			switch {
			case none:
				request.Environments = &[]string{}
			case len(environments) > 0:
				request.Environments = &environments
			}

			resp, err := c.CreateProjectWithResponse(command.Context(), request)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return sayWhatTookTheName(err)
			}
			if resp.JSON200 == nil {
				return unreadable("a project")
			}

			fmt.Fprintf(g.msg(), "%s exists, with %s.\n", resp.JSON200.Name,
				orNone(strings.Join(names(resp.JSON200.Environments), ", ")))
			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}
			return nil
		},
	}
	command.Flags().StringSliceVar(&environments, "environments", nil, "the environments to create; dev, staging and prod by default")
	command.Flags().BoolVar(&none, "no-environments", false, "create the project with no environments at all")
	command.MarkFlagsMutuallyExclusive("environments", "no-environments")
	return command
}

func newProjectsDelete(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "delete <name>",
		Short: "Delete a project, recoverably, with the subtree it has.",
		Long: "Nothing cascades: the project's active environments and their values are\n" +
			"retained as one recoverable subtree, and an environment deleted before the\n" +
			"project stays deleted when the project comes back. The name stays reserved\n" +
			"for the whole window.",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			_, c, err := g.instance()
			if err != nil {
				return err
			}
			resp, err := c.DeleteProjectWithResponse(command.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			fmt.Fprintf(g.msg(), "%s is deleted, and recoverable until the window closes.\n", args[0])
			return nil
		},
	}
}

func newProjectsRestore(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "restore <name>",
		Short: "Bring a deleted project back, with the subtree it had.",
		Args:  cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			_, c, err := g.instance()
			if err != nil {
				return err
			}
			resp, err := c.RestoreProjectWithResponse(command.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a project")
			}
			fmt.Fprintf(g.msg(), "%s is back, with %s.\n", resp.JSON200.Name,
				orNone(strings.Join(names(resp.JSON200.Environments), ", ")))
			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}
			return nil
		},
	}
}

func newEnvironments(g *globals) *cobra.Command {
	var deleted bool

	command := &cobra.Command{
		Use:     "environments",
		Aliases: []string{"environment"},
		Short:   "The environments of a project this token reaches.",
		Args:    cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			resolved, c, err := g.inProject()
			if err != nil {
				return err
			}

			resp, err := c.ReadEnvironmentsWithResponse(command.Context(), resolved.Project,
				&api.ReadEnvironmentsParams{Deleted: &deleted})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a listing")
			}
			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}
			for _, environment := range *resp.JSON200 {
				fmt.Fprintln(g.out(), environment.Name)
			}
			return nil
		},
	}
	command.Flags().BoolVar(&deleted, "deleted", false, "what is deleted and still recoverable, instead")
	command.AddCommand(newEnvironmentsCreate(g), newEnvironmentsDelete(g), newEnvironmentsRestore(g), newEnvironmentsPurge(g))
	return command
}

func newEnvironmentsCreate(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "create <name>",
		Short: "Add an environment to this project.",
		Long: "Environment names are free, and an extra `dev-someone` is an ordinary\n" +
			"environment offering no privacy: everybody in the organization sees it.",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			resolved, c, err := g.inProject()
			if err != nil {
				return err
			}
			resp, err := c.CreateEnvironmentWithResponse(command.Context(), resolved.Project,
				api.CreateEnvironmentRequest{Name: args[0]})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return sayWhatTookTheName(err)
			}
			if resp.JSON200 == nil {
				return unreadable("an environment")
			}
			fmt.Fprintf(g.msg(), "%s/%s exists.\n", resolved.Project, resp.JSON200.Name)
			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}
			return nil
		},
	}
}

func newEnvironmentsDelete(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "delete <name>",
		Short: "Delete an environment, recoverably, with its secrets.",
		Args:  cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			resolved, c, err := g.inProject()
			if err != nil {
				return err
			}
			resp, err := c.DeleteEnvironmentWithResponse(command.Context(), resolved.Project, args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			fmt.Fprintf(g.msg(), "%s/%s is deleted, and recoverable until the window closes.\n", resolved.Project, args[0])
			return nil
		},
	}
}

func newEnvironmentsRestore(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "restore <name>",
		Short: "Bring a deleted environment back, with what it held.",
		Args:  cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			resolved, c, err := g.inProject()
			if err != nil {
				return err
			}
			resp, err := c.RestoreEnvironmentWithResponse(command.Context(), resolved.Project, args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("an environment")
			}
			fmt.Fprintf(g.msg(), "%s/%s is back.\n", resolved.Project, resp.JSON200.Name)
			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}
			return nil
		},
	}
}

// sayWhatTookTheName turns the one refusal that is regularly misread into the
// sentence that answers it: "I deleted it, why can I not recreate it".
func sayWhatTookTheName(err error) error {
	var failure *client.Failure
	if errors.As(err, &failure) && failure.Problem.Code() == "name-taken" {
		if deleted, found := failure.Problem.Extra["takenBySomethingDeleted"]; found && string(deleted) == "true" {
			failure.Message += " It is deleted and still inside its recovery window, which is what keeps the name reserved: `restore` brings that one back."
		}
	}
	return err
}

func orNone(list string) string {
	if list == "" {
		return "no environments"
	}
	return list
}

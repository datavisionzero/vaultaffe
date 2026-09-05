package cmd

import (
	"context"
	"fmt"
	"path/filepath"
	"strings"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/client"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/config"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/render"
)

func newSetup(g *globals) *cobra.Command {
	var list, forget bool

	command := &cobra.Command{
		Use:   "setup",
		Short: "Bind this directory to a project and an environment.",
		Long: "The binding is a prefix table in your own configuration and not a file in\n" +
			"the repository: everything at or below a bound directory means that project\n" +
			"and that environment, and the longest bound directory wins.\n\n" +
			"A checked-in " + config.ProjectFileName + " file (project and environment, and no value) is\n" +
			"optional and does exactly one thing: `setup` with no flags reads it — from\n" +
			"here upwards, so a monorepo needs one at its root — and writes the entry.\n" +
			"It is never read behind your back at run time, because a repository that\n" +
			"could rebind your directories by being cloned could point a `run` at\n" +
			"production.\n\n" +
			config.EnvProject + " and " + config.EnvEnvironment + " override the table, which is how CI and\n" +
			"containers say it without a table at all.",
		Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			switch {
			case list:
				return g.listBindings()
			case forget:
				return g.forgetBinding()
			default:
				return g.setup(command.Context())
			}
		},
	}
	command.Flags().BoolVar(&list, "list", false, "print the whole table instead of changing it")
	command.Flags().BoolVar(&forget, "forget", false, "remove the entry for exactly this directory")
	return command
}

func (g *globals) setup(ctx context.Context) error {
	directory := g.dir()
	project, environment := strings.TrimSpace(g.project), strings.TrimSpace(g.environment)
	source := "--project and --environment"

	if project == "" || environment == "" {
		found, ok, err := config.FindProjectFile(directory)
		if err != nil {
			return err
		}
		if !ok {
			return &config.UsageError{Message: fmt.Sprintf(
				"nothing to bind %s to: pass --project <name> --environment <name>, or put a %s file with `project` and `environment` in the repository.",
				directory, config.ProjectFileName)}
		}
		// The file binds the directory it is in rather than this one, so that a
		// monorepo is bound once at its root and every package under it inherits.
		directory = filepath.Dir(found.Path)
		if project == "" {
			project = found.Project
		}
		if environment == "" {
			environment = found.Environment
		}
		source = found.Path
	}

	g.confirm(ctx, project, environment)

	file, err := g.readConfig()
	if err != nil {
		return err
	}
	file.Bind(config.Binding{Directory: directory, Project: project, Environment: environment})
	if err := g.writeConfig(file); err != nil {
		return err
	}

	fmt.Fprintf(g.msg(), "%s means %s/%s, from %s.\n", directory, project, environment, source)
	if g.json {
		return render.JSON(g.out(), config.Binding{Directory: directory, Project: project, Environment: environment})
	}
	return nil
}

// confirm asks the instance whether that environment is really there. A typo in
// a binding is otherwise found by the first command that uses it, which may be a
// `run` in front of a person who is already waiting. It is not a condition of
// binding: a machine with no token yet, or an instance that is down, is not a
// reason to refuse to write a line into a local file — so this says what it
// found and never stops the binding.
func (g *globals) confirm(ctx context.Context, project, environment string) {
	_, c, err := g.instance()
	if err != nil {
		fmt.Fprintf(g.msg(), "Not checked against the instance: %v\n", err)
		return
	}

	resp, err := c.ReadProjectWithResponse(ctx, project)
	if err != nil {
		fmt.Fprintf(g.msg(), "Not checked against the instance: %v\n", client.Transport(err))
		return
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		fmt.Fprintf(g.msg(), "The instance does not agree yet: %v\n", err)
		return
	}
	if resp.JSON200 == nil {
		return
	}

	for _, candidate := range resp.JSON200.Environments {
		if candidate.Name == environment {
			return
		}
	}
	fmt.Fprintf(g.msg(), "Bound anyway, but %s has no environment %q today. Its environments: %s.\n",
		project, environment, strings.Join(names(resp.JSON200.Environments), ", "))
}

func (g *globals) listBindings() error {
	file, err := g.readConfig()
	if err != nil {
		return err
	}
	if g.json {
		return render.JSON(g.out(), file.Bindings)
	}
	for _, binding := range file.Bindings {
		fmt.Fprintf(g.out(), "%s\t%s/%s\n", binding.Directory, binding.Project, binding.Environment)
	}
	return nil
}

func (g *globals) forgetBinding() error {
	file, err := g.readConfig()
	if err != nil {
		return err
	}
	if !file.Unbind(g.dir()) {
		return &config.UsageError{Message: fmt.Sprintf(
			"%s has no entry of its own. `vaultaffe setup --list` says which directory the binding here comes from.", g.dir())}
	}
	if err := g.writeConfig(file); err != nil {
		return err
	}
	fmt.Fprintf(g.msg(), "%s is no longer bound.\n", g.dir())
	return nil
}

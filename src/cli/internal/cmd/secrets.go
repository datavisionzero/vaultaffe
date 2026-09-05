package cmd

import (
	"context"
	"errors"
	"fmt"
	"io"
	"strings"
	"text/tabwriter"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/api"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/client"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/config"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/exit"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/render"
)

func newSecrets(g *globals) *cobra.Command {
	var deleted bool

	command := &cobra.Command{
		Use:   "secrets",
		Short: "The keys of this environment, their status, and one value at a time.",
		Long: "Bare, this lists names and status and never a value — the normal case for an\n" +
			"agent, and a different endpoint and a different scope from reading one\n" +
			"(docs/api.md). A value is a second request for one key that was named.",
		Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			return g.listSecrets(command.Context(), deleted)
		},
	}
	command.Flags().BoolVar(&deleted, "deleted", false, "what is deleted and still recoverable, instead")

	command.AddCommand(
		newSecretsList(g),
		newSecretsGet(g),
		newSecretsSet(g),
		newSecretsDelete(g),
		newSecretsRestore(g),
		newSecretsImport(g),
		newSecretsExport(g),
		newSecretsVersions(g),
		newSecretsRollback(g),
	)
	return command
}

func newSecretsList(g *globals) *cobra.Command {
	var deleted bool

	command := &cobra.Command{
		Use:   "list",
		Short: "Names and status. Never a value.",
		Args:  cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			return g.listSecrets(command.Context(), deleted)
		},
	}
	command.Flags().BoolVar(&deleted, "deleted", false, "what is deleted and still recoverable, instead")
	return command
}

func (g *globals) listSecrets(ctx context.Context, deleted bool) error {
	resolved, c, err := g.bound()
	if err != nil {
		return err
	}

	resp, err := c.ReadSecretNamesWithResponse(ctx, resolved.Project, resolved.Environment,
		&api.ReadSecretNamesParams{Deleted: &deleted})
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

	table := tabwriter.NewWriter(g.out(), 0, 0, 2, ' ', 0)
	for _, secret := range *resp.JSON200 {
		fmt.Fprintf(table, "%s\t%s\t%s\n", secret.Name, secret.Status, when(secret.ValueWrittenAt))
	}
	return table.Flush()
}

func newSecretsGet(g *globals) *cobra.Command {
	var raw bool

	command := &cobra.Command{
		Use:   "get <KEY>",
		Short: "Print exactly one value, because it was explicitly asked for.",
		Long: "One key, named. This is the one command whose whole purpose is to put a\n" +
			"value where somebody can see it, which is why it takes no listing, no\n" +
			"pattern and no `--all`.\n\n" +
			"Exactly one trailing newline is added, so that the output reads like every\n" +
			"other command's; --raw prints the bytes as they are stored.",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			return g.getSecret(command.Context(), args[0], raw)
		},
	}
	command.Flags().BoolVar(&raw, "raw", false, "print the stored bytes exactly, without a trailing newline")
	return command
}

func (g *globals) getSecret(ctx context.Context, name string, raw bool) error {
	resolved, c, err := g.bound()
	if err != nil {
		return err
	}

	resp, err := c.ReadSecretWithResponse(ctx, resolved.Project, resolved.Environment, name)
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}
	if resp.JSON200 == nil {
		return unreadable("a value")
	}

	if resp.JSON200.Value == nil {
		return &client.Failure{Code: exit.Placeholder, Message: fmt.Sprintf(
			"%s is an empty placeholder in %s/%s: a person still has to fill it.",
			name, resolved.Project, resolved.Environment)}
	}

	if g.json {
		return render.JSON(g.out(), resp.JSON200)
	}
	if raw {
		_, err := io.WriteString(g.out(), *resp.JSON200.Value)
		return err
	}
	_, err = fmt.Fprintln(g.out(), *resp.JSON200.Value)
	return err
}

func newSecretsSet(g *globals) *cobra.Command {
	var replace, empty, raw bool

	command := &cobra.Command{
		Use:   "set <KEY>",
		Short: "Write a value that arrives on stdin and is never seen.",
		Long: "The value comes exclusively from stdin: never as an argument, where the\n" +
			"shell history and `ps` would keep it, and never as a file. An agent can pipe\n" +
			"a `gcloud` or `stripe` command straight through without reading it, and the\n" +
			"confirmation does not give it back.\n\n" +
			"    gcloud … | vaultaffe secrets set STRIPE_KEY\n" +
			"    vaultaffe secrets set SMTP_PASSWORD --empty\n\n" +
			"Exactly one trailing newline is removed, because practically every command\n" +
			"ends its output with one and a key carrying an invisible \\n is the bug that\n" +
			"costs an afternoon; --raw keeps the bytes as they came. A multi-line value —\n" +
			"a PEM key — passes through as it is.\n\n" +
			"Overwriting is explicit: a key that already holds a value needs --replace,\n" +
			"and filling a placeholder needs nothing. Overwriting is as destructive as\n" +
			"deleting, and the flag is also what makes a rotation legible in the change\n" +
			"log.",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			return g.setSecret(command.Context(), args[0], replace, empty, raw)
		},
	}
	command.Flags().BoolVar(&replace, "replace", false, "overwrite a key that already holds a value")
	command.Flags().BoolVar(&empty, "empty", false, "create a placeholder for a person to fill, and read no stdin")
	command.Flags().BoolVar(&raw, "raw", false, "keep the bytes as they came, trailing newline and all")
	command.MarkFlagsMutuallyExclusive("empty", "raw")
	return command
}

func (g *globals) setSecret(ctx context.Context, name string, replace, empty, raw bool) error {
	resolved, c, err := g.bound()
	if err != nil {
		return err
	}

	// The unsafe form is not offered at all: `set KEY=value` would put the value
	// in the shell history and in `ps`, and Doppler's answer to that is five
	// lines of HISTIGNORE configuration. Somebody who types it gets a sentence
	// rather than a key with an odd name.
	if strings.Contains(name, "=") {
		return &config.UsageError{Message: fmt.Sprintf(
			"%q looks like a key and a value. The value arrives on stdin and never as an argument, where the shell history and `ps` would keep it: `… | vaultaffe secrets set %s`.",
			name, strings.SplitN(name, "=", 2)[0])}
	}

	request := api.SetSecretRequest{}
	if replace {
		request.Replace = &replace
	}
	if !empty {
		value, err := readValue(g.env.Stdin, raw)
		if err != nil {
			return err
		}
		if value == "" {
			// An empty string standing in for a placeholder is the bug the
			// placeholder exists to prevent, so it is named rather than guessed.
			return &config.UsageError{Message: fmt.Sprintf(
				"nothing arrived on stdin for %s. Pipe the value in, or pass --empty to make a placeholder for a person to fill.", name)}
		}
		request.Value = &value
	}

	resp, err := c.SetSecretWithResponse(ctx, resolved.Project, resolved.Environment, name, request)
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return sayHowToOverwrite(err)
	}
	if resp.JSON200 == nil {
		return unreadable("a confirmation")
	}

	return g.confirmSecret(resolved, *resp.JSON200, verb(empty, "is a placeholder in", "is set in"))
}

// sayHowToOverwrite adds the flag to the instance's refusal. The server says
// that the request did not ask to overwrite and never which flag says so, which
// is the client's half of that sentence (ADR 0010).
func sayHowToOverwrite(err error) error {
	var failure *client.Failure
	if errors.As(err, &failure) && failure.Problem.Code() == "replace-required" {
		failure.Message += " Pass --replace to overwrite it."
	}
	return err
}

func newSecretsDelete(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "delete <KEY>",
		Short: "Delete a key, recoverably.",
		Long: "It leaves every listing at once and keeps its last state for the recovery\n" +
			"window; `restore` brings it back inside that window, and the name stays\n" +
			"reserved for the whole of it.",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			resolved, c, err := g.bound()
			if err != nil {
				return err
			}
			resp, err := c.DeleteSecretWithResponse(command.Context(), resolved.Project, resolved.Environment, args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a confirmation")
			}
			return g.confirmSecret(resolved, *resp.JSON200, "is deleted from")
		},
	}
}

func newSecretsRestore(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "restore <KEY>",
		Short: "Bring a deleted key back, inside its window.",
		Args:  cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			resolved, c, err := g.bound()
			if err != nil {
				return err
			}
			resp, err := c.RestoreSecretWithResponse(command.Context(), resolved.Project, resolved.Environment, args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a confirmation")
			}
			return g.confirmSecret(resolved, *resp.JSON200, "is back in")
		},
	}
}

func newSecretsImport(g *globals) *cobra.Command {
	var replace bool

	command := &cobra.Command{
		Use:   "import",
		Short: "Read a .env from stdin and apply all of it or none of it.",
		Long: "    vaultaffe secrets import < .env\n\n" +
			"The file arrives on stdin so that an agent can migrate one it never\n" +
			"displays. The answer names keys — created, filled, replaced, unchanged,\n" +
			"skipped and unreadable — and never a value. `KEY=` makes a placeholder, and\n" +
			"a key that already holds a value is skipped unless --replace.",
		Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			return g.importSecrets(command.Context(), replace)
		},
	}
	command.Flags().BoolVar(&replace, "replace", false, "overwrite keys that already hold a value")
	return command
}

func (g *globals) importSecrets(ctx context.Context, replace bool) error {
	resolved, c, err := g.bound()
	if err != nil {
		return err
	}

	// The whole file, bytes as they are: what a `.env` means is the API's
	// decision and not this CLI's (docs/api.md).
	content, err := readValue(g.env.Stdin, true)
	if err != nil {
		return err
	}
	if strings.TrimSpace(content) == "" {
		return &config.UsageError{Message: "nothing arrived on stdin: `vaultaffe secrets import < .env`."}
	}

	resp, err := c.ImportSecretsWithResponse(ctx, resolved.Project, resolved.Environment,
		api.ImportRequest{Content: content, Replace: &replace})
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}
	if resp.JSON200 == nil {
		return unreadable("an import")
	}

	if g.json {
		return render.JSON(g.out(), resp.JSON200)
	}

	report := *resp.JSON200
	out := g.out()
	group(out, "created", report.Created)
	group(out, "filled", report.Filled)
	group(out, "replaced", report.Replaced)
	group(out, "unchanged", report.Unchanged)
	for _, skipped := range report.Skipped {
		fmt.Fprintf(out, "skipped    %s (%s)\n", skipped.Name, skipped.Reason)
	}
	for _, unreadable := range report.Unreadable {
		fmt.Fprintf(out, "unreadable line %d (%s)\n", unreadable.Line, unreadable.Reason)
	}
	return nil
}

func newSecretsExport(g *globals) *cobra.Command {
	var format string

	command := &cobra.Command{
		Use:   "export",
		Short: "Every value of this environment, as a .env. A person only.",
		Long: "This writes every value in plaintext, which is precisely the contradiction\n" +
			"`inject` was rejected for. It stays because a way back out is part of being\n" +
			"trustworthy, and it works under a session token only: an agent or a service\n" +
			"token is refused however many scopes it carries.",
		Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			if format != "env" {
				return &config.UsageError{Message: fmt.Sprintf(
					"--format %q: the one format this writes is `env`.", format)}
			}
			return g.exportEnvironment(command.Context())
		},
	}
	command.Flags().StringVar(&format, "format", "env", "the format to write; `env` is the one there is")
	return command
}

func (g *globals) exportEnvironment(ctx context.Context) error {
	resolved, c, err := g.bound()
	if err != nil {
		return err
	}

	resp, err := c.ExportEnvironmentWithResponse(ctx, resolved.Project, resolved.Environment)
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}

	_, err = g.out().Write(resp.Body)
	return err
}

func newSecretsVersions(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "versions <KEY>",
		Short: "When this key was written, and when each version goes. Never what.",
		Long: "Id, written, replaced and expires — and no value, because five old\n" +
			"credentials in one answer would be the bulk disclosure the rest of this\n" +
			"product spends every decision avoiding.",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			resolved, c, err := g.bound()
			if err != nil {
				return err
			}
			resp, err := c.ReadSecretVersionsWithResponse(command.Context(), resolved.Project, resolved.Environment, args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a history")
			}
			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}

			table := tabwriter.NewWriter(g.out(), 0, 0, 2, ' ', 0)
			for _, version := range *resp.JSON200 {
				fmt.Fprintf(table, "%s\twritten %s\treplaced %s\texpires %s\n",
					version.Id, when(&version.WrittenAt), when(&version.ReplacedAt), when(&version.ExpiresAt))
			}
			return table.Flush()
		},
	}
}

func newSecretsRollback(g *globals) *cobra.Command {
	var versionID string

	command := &cobra.Command{
		Use:   "rollback <KEY>",
		Short: "Put an earlier value back without anybody reading it.",
		Long: "An agent that wrecked a value overnight is exactly who needs an undo button,\n" +
			"so this is an ordinary write and not a human-only action. Nothing is opened\n" +
			"to do it: every version is sealed under the key's own data key, and a\n" +
			"rollback moves ciphertext.\n\n" +
			"Without --version it is the value before this one.",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			resolved, c, err := g.bound()
			if err != nil {
				return err
			}

			request := api.RollBackRequest{}
			if strings.TrimSpace(versionID) != "" {
				parsed, err := parseID(versionID)
				if err != nil {
					return err
				}
				request.VersionId = &parsed
			}

			resp, err := c.RollBackSecretWithResponse(command.Context(), resolved.Project, resolved.Environment, args[0], request)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a confirmation")
			}
			return g.confirmSecret(resolved, *resp.JSON200, "is rolled back in")
		},
	}
	command.Flags().StringVar(&versionID, "version", "", "the version to put back; the one before this by default")
	return command
}

// confirmSecret is the one place a write is acknowledged, and it says the name,
// the place and what happened — never the value. A confirmation that echoed it
// would put into a transcript exactly what piping a vendor's command straight in
// kept out of one.
func (g *globals) confirmSecret(resolved config.Resolved, secret api.Secret, what string) error {
	fmt.Fprintf(g.msg(), "%s %s %s/%s.\n", secret.Name, what, resolved.Project, resolved.Environment)
	if g.json {
		return render.JSON(g.out(), secret)
	}
	return nil
}

func group(out io.Writer, label string, names []string) {
	for _, name := range names {
		fmt.Fprintf(out, "%-10s %s\n", label, name)
	}
}

func unreadable(what string) error {
	return &client.Failure{Code: exit.Unexpected, Message: "the instance answered " + what + " this CLI cannot read"}
}

func verb(condition bool, yes, no string) string {
	if condition {
		return yes
	}
	return no
}

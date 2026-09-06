package cmd

import (
	"fmt"
	"strings"
	"text/tabwriter"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/client"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/render"
)

// The two things a key's screen says that the console had no word for: which
// keys this environment has not got and its siblings have, and who has read one.
//
// Both are the second half of the sentence in Specification §6.2 that this
// stage is finishing — complete CLI coverage of human administration *and the
// missing-key notice* follow after the MVP. Neither reads a value, and neither
// could: one is arithmetic over names, the other is two moments per identity.

func newSecretsMissing(g *globals) *cobra.Command {
	var dismissed bool

	command := &cobra.Command{
		Use:   "missing",
		Short: "Keys most of this project's other environments have and this one has not.",
		Long: "A display, and nothing that acts. Nothing here creates a key: what it offers\n" +
			"is the list and, with `dismiss`, the way to say \"not here\" and have it stay\n" +
			"said.\n\n" +
			"A key is reported when **more than half** of this project's other\n" +
			"environments hold it. Held against any single one of them the notice would\n" +
			"report every key production legitimately has alone, and would hold three\n" +
			"shared environments against one personal `dev-alex` — and a notice that\n" +
			"cries wolf is one nobody reads on the day it is right.\n\n" +
			"The comparison runs only over environments this token reaches, so a token\n" +
			"bound to one environment gets nothing here rather than a filtered answer.",
		Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			resolved, c, err := g.bound()
			if err != nil {
				return err
			}

			resp, err := c.ReadMissingKeysWithResponse(command.Context(),
				resolved.Project, resolved.Environment)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a notice")
			}
			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}

			table := tabwriter.NewWriter(g.out(), 0, 0, 2, ' ', 0)
			for _, key := range *resp.JSON200 {
				if (key.DismissedAt != nil) != dismissed {
					continue
				}
				fmt.Fprintf(table, "%s\tin %s", key.Name, strings.Join(key.PresentIn, ", "))
				if key.DismissedAt != nil {
					fmt.Fprintf(table, "\tdismissed %s", when(key.DismissedAt))
				}
				fmt.Fprintln(table)
			}
			return table.Flush()
		},
	}
	command.Flags().BoolVar(&dismissed, "dismissed", false, "the keys somebody silenced here, instead")
	command.AddCommand(newSecretsDismiss(g))
	return command
}

func newSecretsDismiss(g *globals) *cobra.Command {
	var undo bool

	command := &cobra.Command{
		Use:   "dismiss <KEY>",
		Short: "Stop the notice mentioning that key in this environment. --undo says it again.",
		Long: "One key and one environment: \"this environment does not need that key\" is a\n" +
			"statement about one place, and the same key may well be missing somewhere it\n" +
			"is wanted. It creates nothing and changes nothing about the vault, which is\n" +
			"why it is in no change log.\n\n" +
			"Dismissing twice is dismissing once.",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			resolved, c, err := g.bound()
			if err != nil {
				return err
			}

			if undo {
				resp, err := c.WithdrawDismissalWithResponse(command.Context(),
					resolved.Project, resolved.Environment, args[0])
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
					return err
				}
				fmt.Fprintf(g.msg(), "The notice for %s/%s mentions %s again.\n",
					resolved.Project, resolved.Environment, args[0])
				if g.json && resp.JSON200 != nil {
					return render.JSON(g.out(), resp.JSON200)
				}
				return nil
			}

			resp, err := c.DismissMissingKeyWithResponse(command.Context(),
				resolved.Project, resolved.Environment, args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			fmt.Fprintf(g.msg(), "The notice for %s/%s says nothing about %s from now on.\n",
				resolved.Project, resolved.Environment, args[0])
			if g.json && resp.JSON200 != nil {
				return render.JSON(g.out(), resp.JSON200)
			}
			return nil
		},
	}
	command.Flags().BoolVar(&undo, "undo", false, "take the dismissal back and mention the key here again")
	return command
}

func newSecretsAccess(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "access <KEY>",
		Short: "Who has read this key, and when they first and last did. Never what.",
		Long: "A summary and not a record of every run: a value read does not prove that\n" +
			"anything started with it. So it is two moments per identity and no count —\n" +
			"reading a key a thousand times is still one line here.\n\n" +
			"The identity's kind is on the line for the same reason it is in the change\n" +
			"log: with writing agents, what kind of thing acted is the question worth\n" +
			"asking.",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			resolved, c, err := g.bound()
			if err != nil {
				return err
			}

			resp, err := c.ReadSecretAccessWithResponse(command.Context(),
				resolved.Project, resolved.Environment, args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("an access summary")
			}
			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}

			table := tabwriter.NewWriter(g.out(), 0, 0, 2, ' ', 0)
			for _, one := range *resp.JSON200 {
				fmt.Fprintf(table, "%s\t%s\tfirst %s\tlast %s\n",
					one.Identity.Name, one.Identity.Type,
					when(&one.FirstAt), when(&one.LastAt))
			}
			return table.Flush()
		},
	}
}

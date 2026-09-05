package cmd

import (
	"fmt"
	"strings"
	"text/tabwriter"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/api"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/client"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/render"
)

func newChanges(g *globals) *cobra.Command {
	var secret string
	var limit, offset int32
	var everywhere bool

	command := &cobra.Command{
		Use:   "changes",
		Short: "What was changed, by whom, and by what kind of thing.",
		Long: "The change log holds no value at all, not even as a diff: a moment, an\n" +
			"action, the names it happened to, and the acting identity with its type —\n" +
			"human-session, service-token or agent-token. That last field is what the log\n" +
			"exists for, because with writing agents the interesting question is what\n" +
			"kind of thing acted.\n\n" +
			"Reads are not in it. A successful read does not prove an application\n" +
			"started, and an export is a read too.\n\n" +
			"By default this asks about the project and environment this directory is\n" +
			"bound to; --everywhere asks about the whole organization, which needs a\n" +
			"token that reaches it.",
		Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			params := &api.ReadChangesParams{Limit: &limit, Offset: &offset}

			var c *client.Client
			if everywhere {
				var err error
				if _, c, err = g.instance(); err != nil {
					return err
				}
			} else {
				resolved, bound, err := g.bound()
				if err != nil {
					return err
				}
				c = bound
				params.Project, params.Environment = &resolved.Project, &resolved.Environment
				if strings.TrimSpace(secret) != "" {
					params.Secret = &secret
				}
			}

			resp, err := c.ReadChangesWithResponse(command.Context(), params)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a change log")
			}

			if g.json {
				return render.JSON(g.out(), resp.JSON200)
			}

			table := tabwriter.NewWriter(g.out(), 0, 0, 2, ' ', 0)
			for _, change := range resp.JSON200.Entries {
				fmt.Fprintf(table, "%s\t%s\t%s\t%s (%s)\n",
					when(&change.OccurredAt), change.Action, where(change), change.Identity.Name, change.Identity.Type)
			}
			return table.Flush()
		},
	}
	command.Flags().StringVar(&secret, "secret", "", "one key of this environment")
	command.Flags().BoolVar(&everywhere, "everywhere", false, "the whole organization instead of this directory's binding")
	command.Flags().Int32Var(&limit, "limit", 50, "how many entries, at most 200")
	command.Flags().Int32Var(&offset, "offset", 0, "how many to skip, newest first")
	return command
}

// where names what an entry happened to. The log records names and not ids, so
// that it can still say what happened to something that no longer exists.
func where(change api.Change) string {
	parts := make([]string, 0, 3)
	for _, name := range []*string{change.Project, change.Environment, change.Secret} {
		if name != nil && *name != "" {
			parts = append(parts, *name)
		}
	}
	if len(parts) == 0 {
		return "the organization"
	}
	return strings.Join(parts, "/")
}

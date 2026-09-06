package cmd

import (
	"fmt"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/api"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/client"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/render"
)

// Purge, at each of the three levels and once more for a key's history.
//
// It is the one deletion this product cannot undo, and it is a person's alone
// (Specification §6.5): in an agent's hands it would be anti-forensics. The
// server refuses an agent token with `human-only` and `humanAction: "purge"`,
// and this CLI's `advice` table now has a command to name for it — which is the
// whole of ADR 0010 and the reason the entry could not be written until now.
//
// Nothing here asks for a confirmation. A prompt in an agent's terminal is a
// command that hangs (§6.2), and this CLI is never interactive; what makes a
// purge deliberate here is that it is typed, and what makes it safe is that
// only a session can do it.

const purgeWhat = "It cannot be undone, and it is a person's alone. Last night's backup still has\n" +
	"what this removes — a purge in the database does not reach into one.\n"

func newProjectsPurge(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "purge <name>",
		Short: "Remove a deleted project and everything retained under it, now. A person only.",
		Long: "The project has to be deleted already: purging is the early end of a\n" +
			"recovery window, not a second way to delete. Everything retained under it\n" +
			"goes with it, and the name is free afterwards.\n\n" + purgeWhat,
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			_, c, err := g.instance()
			if err != nil {
				return err
			}
			resp, err := c.PurgeProjectWithResponse(command.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a purge")
			}
			return g.sayPurged(*resp.JSON200, fmt.Sprintf("%s is gone from this instance", args[0]))
		},
	}
}

func newEnvironmentsPurge(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "purge <name>",
		Short: "Remove a deleted environment and its keys, now. A person only.",
		Long: "The environment has to be deleted already. Its keys and everything they\n" +
			"held go with it, and the name is free inside the project afterwards.\n\n" + purgeWhat,
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			resolved, c, err := g.inProject()
			if err != nil {
				return err
			}
			resp, err := c.PurgeEnvironmentWithResponse(command.Context(), resolved.Project, args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a purge")
			}
			return g.sayPurged(*resp.JSON200,
				fmt.Sprintf("%s/%s is gone from this instance", resolved.Project, args[0]))
		},
	}
}

func newSecretsPurge(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "purge <KEY>",
		Short: "Remove a deleted key and everything it held, now. A person only.",
		Long: "The key has to be deleted already. What it held and everything it used to\n" +
			"hold go with it, and the name is free inside the environment afterwards.\n\n" + purgeWhat,
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			resolved, c, err := g.bound()
			if err != nil {
				return err
			}
			resp, err := c.PurgeSecretWithResponse(command.Context(),
				resolved.Project, resolved.Environment, args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a purge")
			}
			return g.sayPurged(*resp.JSON200, fmt.Sprintf("%s is gone from %s/%s",
				args[0], resolved.Project, resolved.Environment))
		},
	}
}

func newSecretsPurgeHistory(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "purge-history <KEY>",
		Short: "Remove what a key used to hold, keeping the key. A person only.",
		Long: "The headline case of §6.5: after a suspected compromise, a key's history\n" +
			"genuinely disappears. The key stays and so does the value it holds now — it\n" +
			"is the undo button that goes, which is exactly why this is a person's.\n\n" + purgeWhat,
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			resolved, c, err := g.bound()
			if err != nil {
				return err
			}
			resp, err := c.PurgeSecretVersionsWithResponse(command.Context(),
				resolved.Project, resolved.Environment, args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200 == nil {
				return unreadable("a purge")
			}
			return g.sayPurged(*resp.JSON200, fmt.Sprintf("%s in %s/%s holds what it holds and nothing else",
				args[0], resolved.Project, resolved.Environment))
		},
	}
}

// sayPurged reports a purge the way the answer describes it — by counting the
// values that are gone, which is the number somebody wants after a compromise.
// A purge that removed none is not dressed up as one that did: "0 retained
// values" reads like a failure, and it is not one.
//
// It also says the thing no product we looked at says out loud: a purge in the
// database does not reach into last night's backup.
func (g *globals) sayPurged(purged api.Purged, what string) error {
	if purged.Versions > 0 {
		fmt.Fprintf(g.msg(), "%s, with %s.\n", what, plural(int(purged.Versions),
			"one retained value", fmt.Sprintf("%d retained values", purged.Versions)))
	} else {
		fmt.Fprintf(g.msg(), "%s.\n", what)
	}
	fmt.Fprintln(g.msg(), "Last night's backup still has what this removed.")
	if g.json {
		return render.JSON(g.out(), purged)
	}
	return nil
}

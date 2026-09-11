package cmd

import (
	"context"
	"fmt"
	"os"
	"path/filepath"
	"strings"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/client"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/config"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/render"
)

func newRenew(g *globals) *cobra.Command {
	var tokenFile string

	command := &cobra.Command{
		Use:   "renew",
		Short: "Replace the token this machine is holding with the next one, without printing it.",
		Long: "`enroll` asks a person for a token of this machine's own; this replaces it\n" +
			"with the next one, and asks nobody. It is the one act on a credential an\n" +
			"agent may do, and it is bounded on every side: the token it replaces is the\n" +
			"one it is already holding, the successor carries the same name, the same\n" +
			"scopes and the same reach, and the expiry is the length a person agreed to\n" +
			"when they issued it, begun again. Nothing here widens anything, which is\n" +
			"why an agent may do it at all.\n\n" +
			"**The value is never printed.** It goes into a file only you can read, and\n" +
			"this command says where — the same rule `enroll` follows, for the same\n" +
			"reason: a value that has been printed has been in a terminal, in a\n" +
			"scrollback and, where an agent ran the command, in a transcript.\n\n" +
			"**The value it replaces is dead the moment this returns.** If this token\n" +
			"came from " + config.EnvToken + " then that variable, in this process's\n" +
			"environment and in every process already started from it, now holds a\n" +
			"string that authenticates nothing — this CLI cannot reach in and change it.\n" +
			"What runs next takes the value from the file this command names.\n\n" +
			"A session is not renewed this way: signing in again is how a person gets\n" +
			"another one. A service token is not either — its value lives in a\n" +
			"pipeline's secret store or a deployment's environment, and an instance that\n" +
			"cannot write to those would be replacing a working credential with one\n" +
			"nothing is holding.",
		Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			return g.renew(command.Context(), strings.TrimSpace(tokenFile))
		},
	}
	command.Flags().StringVar(&tokenFile, "token-file", "",
		"where to write it; the default is the file it came from, or one beside the configuration under agents/")
	return command
}

func (g *globals) renew(ctx context.Context, tokenFile string) error {
	resolved, c, err := g.instance()
	if err != nil {
		return err
	}

	// Which token this is, asked before it is replaced: the id to rotate, the
	// name the successor will carry, and the kind — this being a session is the
	// one mistake worth catching here rather than as a refusal about an id
	// nobody typed.
	me, err := c.ReadMeWithResponse(ctx)
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(me.HTTPResponse, me.Body); err != nil {
		return err
	}
	if me.JSON200 == nil {
		return unreadable("who this token belongs to")
	}

	if me.JSON200.TokenKind == "session" {
		return &config.UsageError{Message: fmt.Sprintf(
			"the token in use is a session of %s, and a session is not renewed: run `vaultaffe login` to sign in again.",
			resolved.Address)}
	}

	// Where the value will go, worked out before the old one is killed. A
	// rotation that succeeded and then found nowhere to put the answer would
	// leave this machine holding a value that stopped working and no way to the
	// one that replaced it.
	path, err := g.renewalFileFor(resolved, valueOr(me.JSON200.TokenName, "agent"), tokenFile)
	if err != nil {
		return err
	}
	if err := keptFree(path, resolved.Token); err != nil {
		return err
	}

	resp, err := c.RotateTokenWithResponse(ctx, me.JSON200.TokenId)
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

	if err := os.MkdirAll(filepath.Dir(path), 0o700); err != nil {
		return &config.UsageError{Message: fmt.Sprintf("%s could not be made: %v", filepath.Dir(path), err)}
	}
	if err := config.WriteTokenFile(path, issued.Value); err != nil {
		return &config.UsageError{Message: fmt.Sprintf("%s could not be written: %v", path, err)}
	}

	fmt.Fprintf(g.msg(), "%s is renewed. It is the same %s token of %s, with %s, reaching %s.\n",
		valueOr(issued.Token.Name, issued.Token.Id.String()), issued.Token.Kind, resolved.Address,
		strings.Join(issued.Token.Scopes, ","), reach(issued.Token))
	fmt.Fprintf(g.msg(), "Its value is in %s, readable only by you, and was not printed.\n", path)
	fmt.Fprintln(g.msg(), "The value it replaces is revoked: whatever still holds it fails on its next request.")

	if resolved.TokenFrom == config.EnvToken {
		fmt.Fprintf(g.msg(), "\nThis one came from %s, which this CLI cannot change from in here. What you start next takes it from the file:\n\n    %s=\"$(cat %s)\" <the command that starts it>\n",
			config.EnvToken, config.EnvToken, path)
	}

	if g.json {
		// Everything about the token except the one thing this command exists to
		// keep out of a terminal.
		return render.JSON(g.out(), map[string]any{
			"instance":  resolved.Address,
			"tokenFile": path,
			"token":     issued.Token,
		})
	}
	return nil
}

// renewalFileFor answers where the successor goes: what was asked for, then the
// file this token was read out of, and failing both the place `enroll` would
// have put a token of this name.
//
// Writing back to the file it came from is the case worth having — the next
// invocation reads the same path and finds the value that works, with nothing
// to carry across. A token that came out of the environment has no such file,
// and the answer there is the one `enroll` gives, named after the token rather
// than after what an enrollment asked to be called.
func (g *globals) renewalFileFor(resolved config.Resolved, name, chosen string) (string, error) {
	if chosen != "" {
		return chosen, nil
	}
	if resolved.TokenFrom != config.EnvToken && resolved.TokenFrom != config.FromKeychain {
		return resolved.TokenFrom, nil
	}
	return g.tokenFileFor(name, "")
}

// keptFree refuses to write over a file holding some other credential. The path
// is worked out from a name, and two tokens may be called the same thing; a
// secrets manager that quietly replaced the wrong one would be the failure this
// whole command is here to avoid, in miniature.
//
// An unreadable file is not an objection: `enroll` writes mode 0600 and this
// process may not be the one that ran it. What is refused is a file that is
// plainly somebody else's token.
func keptFree(path, holding string) error {
	content, err := os.ReadFile(path)
	if err != nil {
		return nil
	}

	held := strings.TrimSpace(string(content))
	if held == "" || held == strings.TrimSpace(holding) {
		return nil
	}

	return &config.UsageError{Message: fmt.Sprintf(
		"%s holds a token that is not the one in use, and this would write over it: pass --token-file to say where the new value goes.",
		path)}
}

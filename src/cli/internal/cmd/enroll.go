package cmd

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/api"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/client"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/config"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/exit"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/render"
)

func newEnroll(g *globals) *cobra.Command {
	var tokenFile string

	command := &cobra.Command{
		Use:   "enroll <name>",
		Short: "Ask a person for a token of this machine's own, without printing it.",
		Long: "`login` signs a person in; this asks for a credential of the asking\n" +
			"machine's own. It is the device-code flow either way — a short code here, a\n" +
			"person deciding in a browser somewhere else — and what is different is the\n" +
			"far end: an agent token, with the scopes and the reach that person chose.\n\n" +
			"**The value is never printed.** It goes straight into a file only you can\n" +
			"read, and this command says where. That is the whole point: a token that\n" +
			"has been printed has been in a terminal, in a scrollback and — where an\n" +
			"agent ran the command — in a transcript. Nothing this command puts on a\n" +
			"screen is worth anything to somebody reading over your shoulder.\n\n" +
			"The name is what the person deciding will see. They can change it, and what\n" +
			"they settle on is what the token is called from then on.\n\n" +
			"Nothing about this machine's own configuration changes. The token is for\n" +
			"whatever you are about to start, and it reaches it the way an agent's token\n" +
			"always does — through " + config.EnvToken + " in that process's environment.",
		Args: cobra.ExactArgs(1),
		RunE: func(command *cobra.Command, args []string) error {
			return g.enroll(command.Context(), args[0], strings.TrimSpace(tokenFile))
		},
	}
	command.Flags().StringVar(&tokenFile, "token-file", "",
		"where to write it; the default is beside the configuration, under agents/")
	return command
}

func (g *globals) enroll(ctx context.Context, name, tokenFile string) error {
	address, c, err := g.anonymous()
	if err != nil {
		return err
	}

	// Before a code is printed, let alone a token collected: is this a vaultaffe
	// instance, and do the two of us speak the same contract.
	if _, err := c.Handshake(ctx); err != nil {
		return err
	}

	// Where it will go, worked out before anybody is asked to decide: a person
	// confirming in a browser and then being told this machine has nowhere to
	// put the answer would have agreed to a token nobody can use.
	path, err := g.tokenFileFor(name, tokenFile)
	if err != nil {
		return err
	}

	begun, err := c.BeginEnrollmentWithResponse(ctx, api.BeginEnrollmentRequest{Name: name})
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(begun.HTTPResponse, begun.Body); err != nil {
		return err
	}
	if begun.JSON200 == nil {
		return &client.Failure{Code: exit.Unexpected, Message: "the instance began an enrollment this CLI cannot read"}
	}
	asked := *begun.JSON200

	// Both addresses arrive relative to the instance, and this CLI is the one
	// that has to join the two halves: what it prints is read by a person who
	// has to open it.
	fmt.Fprintf(g.msg(), "Ask a person to open %s and enter this code:\n\n    %s\n\n", at(address, asked.VerificationUri), asked.UserCode)
	if asked.VerificationUriComplete != "" {
		fmt.Fprintf(g.msg(), "Or the code's own address: %s\n\n", at(address, asked.VerificationUriComplete))
	}
	fmt.Fprintf(g.msg(), "Waiting. The code is good for %s.\n", minutes(asked.ExpiresInSeconds))

	issued, err := g.collect(ctx, c, asked)
	if err != nil {
		return err
	}

	if err := os.MkdirAll(filepath.Dir(path), 0o700); err != nil {
		return &config.UsageError{Message: fmt.Sprintf("%s could not be made: %v", filepath.Dir(path), err)}
	}
	if err := config.WriteTokenFile(path, issued.Value); err != nil {
		return &config.UsageError{Message: fmt.Sprintf("%s could not be written: %v", path, err)}
	}

	fmt.Fprintf(g.msg(), "%s is an agent token of %s, with %s, reaching %s.\n",
		valueOr(issued.Token.Name, issued.Token.Id.String()), address,
		strings.Join(issued.Token.Scopes, ","), reach(issued.Token))
	fmt.Fprintf(g.msg(), "Its value is in %s, readable only by you, and was not printed.\n\n", path)
	fmt.Fprintf(g.msg(), "Hand it to whatever you are starting, in its environment:\n\n    %s=\"$(cat %s)\" <the command that starts it>\n\n", config.EnvToken, path)
	fmt.Fprintln(g.msg(), "This machine's own configuration is untouched: your session is still yours.")

	if g.json {
		// Everything about the token except the one thing this command exists
		// to keep out of a terminal.
		return render.JSON(g.out(), map[string]any{
			"instance":  address,
			"tokenFile": path,
			"token":     issued.Token,
		})
	}
	return nil
}

// collect polls until a person has decided. Which refusal comes back is the
// protocol, exactly as it is for a login: `device-pending` means keep asking and
// the other three mean stop (docs/api.md).
func (g *globals) collect(ctx context.Context, c *client.Client, asked api.Enrollment) (api.TokenIssued, error) {
	interval := time.Duration(asked.IntervalSeconds) * time.Second
	if interval <= 0 {
		interval = 5 * time.Second
	}
	deadline := time.Now().Add(time.Duration(asked.ExpiresInSeconds) * time.Second)

	for {
		resp, err := c.CollectEnrollmentWithResponse(ctx, api.CollectEnrollmentRequest{DeviceCode: asked.DeviceCode})
		if err != nil {
			return api.TokenIssued{}, client.Transport(err)
		}

		checked := client.Check(resp.HTTPResponse, resp.Body)
		if checked == nil {
			if resp.JSON200 == nil {
				return api.TokenIssued{}, &client.Failure{Code: exit.Unexpected, Message: "the instance answered a token this CLI cannot read"}
			}
			return *resp.JSON200, nil
		}

		var failure *client.Failure
		if !errors.As(checked, &failure) || failure.Problem.Code() != "device-pending" {
			return api.TokenIssued{}, checked
		}

		if time.Now().After(deadline) {
			return api.TokenIssued{}, &client.Failure{
				Code:    exit.Denied,
				Message: "nobody decided that enrollment in time.",
			}
		}

		select {
		case <-ctx.Done():
			return api.TokenIssued{}, ctx.Err()
		case <-time.After(interval):
		}
	}
}

// tokenFileFor answers where the value goes: what was asked for, or a file named
// after the enrollment beside the configuration.
//
// It is deliberately **not** recorded in the configuration the way `login
// --token-file` is. That entry is a rung of this CLI's own token ladder, and an
// agent's token on it would make every command a person runs on this machine act
// as the agent — which is the mistake `docs/agents.md` exists to prevent, in the
// other direction. An agent's token reaches the agent through its environment
// and through nothing else (Specification §6.4).
func (g *globals) tokenFileFor(name, chosen string) (string, error) {
	if chosen != "" {
		return chosen, nil
	}

	path, err := g.configPath()
	if err != nil {
		return "", err
	}

	return filepath.Join(filepath.Dir(path), "agents", slug(name)+".token"), nil
}

// slug turns what a person called an enrollment into something that is a file
// name on every system this CLI runs on. It is for the operator's own benefit —
// the file is found by reading what this command printed — so the rule is only
// that two different names rarely collide and that the result is readable.
func slug(name string) string {
	var out strings.Builder
	dash := false

	for _, r := range strings.ToLower(strings.TrimSpace(name)) {
		switch {
		case (r >= 'a' && r <= 'z') || (r >= '0' && r <= '9'):
			out.WriteRune(r)
			dash = false
		case !dash && out.Len() > 0:
			out.WriteByte('-')
			dash = true
		}
	}

	trimmed := strings.Trim(out.String(), "-")
	if trimmed == "" {
		return "agent"
	}
	if len(trimmed) > 60 {
		trimmed = strings.Trim(trimmed[:60], "-")
	}
	return trimmed
}

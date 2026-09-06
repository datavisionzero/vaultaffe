package cmd

import (
	"context"
	"errors"
	"fmt"
	"net/http"
	"os"
	"strings"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/api"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/client"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/config"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/exit"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/render"
)

func newInstance(g *globals) *cobra.Command {
	command := &cobra.Command{
		Use:   "instance",
		Short: "Whether this instance has been started, and starting it.",
		Args:  cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			return g.readInstance(command.Context())
		},
	}
	command.AddCommand(newInstanceStart(g))
	return command
}

func (g *globals) readInstance(ctx context.Context) error {
	address, c, err := g.anonymous()
	if err != nil {
		return err
	}

	resp, err := c.ReadInstanceWithResponse(ctx)
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}
	if resp.JSON200 == nil {
		return &client.Failure{Code: exit.Unexpected, Message: "the instance answered something this CLI cannot read"}
	}

	if g.json {
		return render.JSON(g.out(), resp.JSON200)
	}
	if !resp.JSON200.Started {
		fmt.Fprintf(g.out(), "%s has not been started: `vaultaffe instance start --email <address> --name <name>`.\n", address)
		return nil
	}
	fmt.Fprintf(g.out(), "%s is the instance of %s.\n", address, valueOr(resp.JSON200.OrganizationName, "an organization it did not name"))
	return nil
}

func newInstanceStart(g *globals) *cobra.Command {
	var email, name, tokenFile, claimFile string

	command := &cobra.Command{
		Use:   "start",
		Short: "The first run: the organization, the first person, and a session.",
		Long: "The one request that authenticates nobody, because there is nobody yet, and\n" +
			"the one that refuses the second time. The first person becomes the\n" +
			"administrator of the default organization.\n\n" +
			"It does need **this instance's claim secret**, which the instance writes to\n" +
			"its own log at every start until somebody claims it (ADR 0019). Whoever can\n" +
			"read that log can start the instance; whoever merely reaches the port\n" +
			"cannot.\n\n" +
			"The password arrives on stdin, like every other secret this CLI takes:\n" +
			"never as an argument, where the shell history and `ps` would keep it. The\n" +
			"claim secret cannot share that stdin, so it comes from --claim-file or from\n" +
			"VAULTAFFE_CLAIM. Prefer the file, or export the variable: an assignment\n" +
			"written in front of the command is in the shell history like any argument.\n\n" +
			"    docker compose logs vaultaffe            # the secret is in there\n" +
			"    printf '%s' \"$PASSWORD\" | vaultaffe instance start \\\n" +
			"        --email you@example.com --name 'Your Name' --claim-file ./claim",
		Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			claim, err := g.claimSecret(strings.TrimSpace(claimFile))
			if err != nil {
				return err
			}
			return g.startInstance(command.Context(), email, name, strings.TrimSpace(tokenFile), claim)
		},
	}
	command.Flags().StringVar(&email, "email", "", "the first person's address")
	command.Flags().StringVar(&name, "name", "", "the first person's name")
	command.Flags().StringVar(&tokenFile, "token-file", "",
		"write the session to this file instead of the keychain, readable only by you")
	command.Flags().StringVar(&claimFile, "claim-file", "",
		"read this instance's claim secret from this file; "+config.EnvClaim+" is the other way")
	_ = command.MarkFlagRequired("email")
	_ = command.MarkFlagRequired("name")
	return command
}

// claimSecret answers with this instance's claim secret, from the file that was
// named or from the environment. Never from an argument: it is a credential, and
// an argument is in the shell history and in `ps` for as long as the command runs.
//
// An empty answer is not an error here. An instance older than ADR 0019 does not
// ask for one, and finding that out is the instance's business rather than a
// guess this end makes — what this CLI does is name its own way of passing one
// when the instance says it wanted it.
func (g *globals) claimSecret(file string) (string, error) {
	if file == "" {
		return strings.TrimSpace(g.getenv(config.EnvClaim)), nil
	}

	content, err := os.ReadFile(file)
	if err != nil {
		return "", &config.UsageError{Message: fmt.Sprintf(
			"--claim-file %s could not be read: %v", file, err)}
	}
	return strings.TrimSpace(string(content)), nil
}

func (g *globals) startInstance(ctx context.Context, email, name, tokenFile, claim string) error {
	address, c, err := g.anonymous()
	if err != nil {
		return err
	}
	if _, err := c.Handshake(ctx); err != nil {
		return err
	}

	password, err := readValue(g.env.Stdin, false)
	if err != nil {
		return err
	}
	if password == "" {
		return &config.UsageError{Message: "no password on stdin: pipe one in, so that it is not in the shell history."}
	}

	resp, err := c.StartInstanceWithResponse(ctx, api.StartInstanceRequest{
		Email:    email,
		Name:     name,
		Password: password,
	}, func(_ context.Context, req *http.Request) error {
		// Absent rather than empty when there is none: a header carrying "" is
		// a presented secret that is wrong, and this has presented nothing.
		if claim != "" {
			req.Header.Set(client.ClaimHeader, claim)
		}
		return nil
	})
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return claimAdvice(err, claim)
	}
	if resp.JSON200 == nil {
		return &client.Failure{Code: exit.Unexpected, Message: "the instance started and answered something this CLI cannot read"}
	}

	started := *resp.JSON200
	if err := g.keep(address, started.Session.Token, tokenFile); err != nil {
		return err
	}

	fmt.Fprintf(g.msg(), "%s is started: %s, and you are its administrator.\n", address, started.OrganizationName)
	if g.json {
		return render.JSON(g.out(), map[string]any{
			"instance":         address,
			"organizationId":   started.OrganizationId,
			"organizationName": started.OrganizationName,
			"userId":           started.Session.UserId,
			"email":            started.Session.Email,
		})
	}
	return nil
}

// claimAdvice adds this CLI's own way of passing a claim secret to the refusal
// that says one was needed. The instance names the log and the compose command,
// because it does not know who is asking; this end knows it is a terminal, and
// ADR 0010 puts naming the command at this end for exactly that reason.
func claimAdvice(err error, claim string) error {
	var failure *client.Failure
	if !errors.As(err, &failure) || failure.Problem.Code() != "claim-refused" {
		return err
	}

	if claim == "" {
		failure.Message += fmt.Sprintf(
			"\nPass it with --claim-file, or put it in %s.", config.EnvClaim)
		return failure
	}

	failure.Message += "\nThat is not this instance's claim secret. It is printed at every start " +
		"until somebody claims the instance, so the newest one in its log is the one that works."
	return failure
}

func valueOr(value *string, fallback string) string {
	if value == nil || *value == "" {
		return fallback
	}
	return *value
}

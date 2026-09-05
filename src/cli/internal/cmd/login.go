package cmd

import (
	"context"
	"errors"
	"fmt"
	"strings"
	"time"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/api"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/client"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/config"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/exit"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/keychain"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/render"
)

func newLogin(g *globals) *cobra.Command {
	var tokenFile string

	command := &cobra.Command{
		Use:   "login",
		Short: "Sign in through a browser on any machine, and keep the session in the keychain.",
		Long: "The device-code flow: this CLI prints a short code and a person confirms it\n" +
			"in a browser — on this machine or on another one. That is the only login\n" +
			"that works over SSH, in CI, in a container and in an agent's sandbox, where\n" +
			"there is no browser to open.\n\n" +
			"The session token goes into the operating system's keychain. Where there is\n" +
			"none, this CLI says so and names the two ways on rather than quietly writing\n" +
			"it to a file.\n\n" +
			"An agent's token never comes through here: it arrives in " + config.EnvToken + ".",
		Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			return g.login(command.Context(), strings.TrimSpace(tokenFile))
		},
	}
	command.Flags().StringVar(&tokenFile, "token-file", "",
		"write the session to this file instead of the keychain, readable only by you")
	return command
}

func (g *globals) login(ctx context.Context, tokenFile string) error {
	address, c, err := g.anonymous()
	if err != nil {
		return err
	}

	// Before a code is printed, let alone a token collected: is this a vaultaffe
	// instance, and do the two of us speak the same contract.
	if _, err := c.Handshake(ctx); err != nil {
		return err
	}

	begun, err := c.BeginDeviceLoginWithResponse(ctx)
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(begun.HTTPResponse, begun.Body); err != nil {
		return err
	}
	if begun.JSON200 == nil {
		return &client.Failure{Code: exit.Unexpected, Message: "the instance began a login this CLI cannot read"}
	}
	login := *begun.JSON200

	fmt.Fprintf(g.msg(), "Open %s and enter this code:\n\n    %s\n\n", login.VerificationUri, login.UserCode)
	if login.VerificationUriComplete != "" {
		fmt.Fprintf(g.msg(), "Or open the code's own address: %s\n\n", login.VerificationUriComplete)
	}
	fmt.Fprintf(g.msg(), "Waiting. The code is good for %s.\n", minutes(login.ExpiresInSeconds))

	session, err := g.poll(ctx, c, login)
	if err != nil {
		return err
	}

	if err := g.keep(address, session.Token, tokenFile); err != nil {
		return err
	}

	fmt.Fprintf(g.msg(), "Signed in to %s as %s.\n", address, session.Email)
	if g.json {
		// The session token is what this command just put in the keychain, and
		// printing it here would put it in the terminal it was kept out of.
		return render.JSON(g.out(), map[string]any{
			"instance":        address,
			"userId":          session.UserId,
			"name":            session.Name,
			"email":           session.Email,
			"isAdministrator": session.IsAdministrator,
			"expiresAt":       session.ExpiresAt,
		})
	}
	return nil
}

// poll collects the session once a person has confirmed. Which refusal comes
// back is the whole protocol: `device-pending` means keep asking, and the other
// three mean stop (docs/api.md).
func (g *globals) poll(ctx context.Context, c *client.Client, login api.DeviceLogin) (api.Session, error) {
	interval := time.Duration(login.IntervalSeconds) * time.Second
	if interval <= 0 {
		interval = 5 * time.Second
	}
	deadline := time.Now().Add(time.Duration(login.ExpiresInSeconds) * time.Second)

	for {
		resp, err := c.RedeemDeviceLoginWithResponse(ctx, api.RedeemDeviceRequest{DeviceCode: login.DeviceCode})
		if err != nil {
			return api.Session{}, client.Transport(err)
		}

		checked := client.Check(resp.HTTPResponse, resp.Body)
		if checked == nil {
			if resp.JSON200 == nil {
				return api.Session{}, &client.Failure{Code: exit.Unexpected, Message: "the instance answered a session this CLI cannot read"}
			}
			return *resp.JSON200, nil
		}

		var failure *client.Failure
		if !errors.As(checked, &failure) || failure.Problem.Code() != "device-pending" {
			return api.Session{}, checked
		}

		if time.Now().After(deadline) {
			return api.Session{}, &client.Failure{
				Code:    exit.Denied,
				Message: "nobody confirmed that login in time.",
			}
		}

		select {
		case <-ctx.Done():
			return api.Session{}, ctx.Err()
		case <-time.After(interval):
		}
	}
}

// keep puts the session where it belongs and records where that was, so that
// the next command finds it without being told again.
func (g *globals) keep(address, token, tokenFile string) error {
	file, err := g.readConfig()
	if err != nil {
		return err
	}
	file.Instance = address

	if tokenFile != "" {
		if err := config.WriteTokenFile(tokenFile, token); err != nil {
			return &config.UsageError{Message: fmt.Sprintf("%s could not be written: %v", tokenFile, err)}
		}
		file.TokenFile = tokenFile
		fmt.Fprintf(g.msg(), "The session is in %s, readable only by you.\n", tokenFile)
		return g.writeConfig(file)
	}

	if err := g.keychain().Store(address, token); err != nil {
		if errors.Is(err, keychain.ErrUnavailable) {
			return &config.UsageError{Message: keychain.Advice}
		}
		return err
	}
	file.TokenFile = ""
	return g.writeConfig(file)
}

func newLogout(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "logout",
		Short: "Revoke this machine's session and remove it from the keychain.",
		Args:  cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			return g.logout(command.Context())
		},
	}
}

func (g *globals) logout(ctx context.Context) error {
	resolved, c, err := g.instance()
	if err != nil {
		return err
	}

	// A token that came out of the environment is the agent's or CI's, and this
	// CLI did not put it there. Revoking it from here would revoke something the
	// person at this terminal may not even know they are holding.
	if resolved.TokenFrom == config.EnvToken {
		return &config.UsageError{Message: fmt.Sprintf(
			"the token in use came from %s, and this CLI did not put it there: unset the variable, or revoke that token where it was created.",
			config.EnvToken)}
	}

	resp, err := c.SignOutWithResponse(ctx)
	if err != nil {
		return client.Transport(err)
	}
	// A session the instance no longer knows is one this machine should stop
	// holding either: the local half of a logout is worth doing regardless.
	revoked := client.Check(resp.HTTPResponse, resp.Body)

	file, err := g.readConfig()
	if err != nil {
		return err
	}
	if file.TokenFile != "" {
		if err := config.WriteTokenFile(file.TokenFile, ""); err != nil {
			return err
		}
		file.TokenFile = ""
	}
	if err := g.keychain().Forget(resolved.Address); err != nil && !errors.Is(err, keychain.ErrUnavailable) {
		return err
	}
	if err := g.writeConfig(file); err != nil {
		return err
	}

	if revoked != nil {
		fmt.Fprintf(g.msg(), "The session is gone from this machine. The instance answered: %v\n", revoked)
		return nil
	}
	fmt.Fprintf(g.msg(), "Signed out of %s.\n", resolved.Address)
	return nil
}

func minutes(seconds int32) string {
	if seconds < 120 {
		return fmt.Sprintf("%d seconds", seconds)
	}
	return fmt.Sprintf("%d minutes", seconds/60)
}

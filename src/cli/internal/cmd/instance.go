package cmd

import (
	"context"
	"fmt"
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
	var email, name, tokenFile string

	command := &cobra.Command{
		Use:   "start",
		Short: "The first run: the organization, the first person, and a session.",
		Long: "The one request that needs no credential, because there is none yet, and\n" +
			"the one that refuses the second time. The first person becomes the\n" +
			"administrator of the default organization.\n\n" +
			"The password arrives on stdin, like every other secret this CLI takes:\n" +
			"never as an argument, where the shell history and `ps` would keep it.\n\n" +
			"    printf '%s' \"$PASSWORD\" | vaultaffe instance start --email you@example.com --name 'Your Name'",
		Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			return g.startInstance(command.Context(), email, name, strings.TrimSpace(tokenFile))
		},
	}
	command.Flags().StringVar(&email, "email", "", "the first person's address")
	command.Flags().StringVar(&name, "name", "", "the first person's name")
	command.Flags().StringVar(&tokenFile, "token-file", "",
		"write the session to this file instead of the keychain, readable only by you")
	_ = command.MarkFlagRequired("email")
	_ = command.MarkFlagRequired("name")
	return command
}

func (g *globals) startInstance(ctx context.Context, email, name, tokenFile string) error {
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
	})
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
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

func valueOr(value *string, fallback string) string {
	if value == nil || *value == "" {
		return fallback
	}
	return *value
}

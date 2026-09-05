package cmd

import (
	"context"
	"fmt"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/api"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/client"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/render"
)

func newStatus(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "status",
		Short: "Which instance, as whom, and what this directory means.",
		Long: "The three questions worth asking before a write: which instance is this,\n" +
			"what kind of thing is my token, and which project and environment does this\n" +
			"directory mean — with, for each of them, where the answer came from.",
		Args: cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			return g.status(command.Context())
		},
	}
}

func (g *globals) status(ctx context.Context) error {
	in, err := g.input()
	if err != nil {
		return err
	}

	address, err := in.ResolveAddress()
	if err != nil {
		return err
	}

	token, tokenFrom, tokenErr := in.ResolveToken(address)
	project, environment, projectFrom, envFrom, bindingErr := in.ResolveBinding()

	c, err := client.New(address, token, g.httpClient())
	if err != nil {
		return err
	}

	// Nothing here aborts on a failure: `status` is the command somebody runs
	// *because* something is wrong, and one broken answer must not take the
	// other two down with it.
	shake, shakeErr := c.Handshake(ctx)
	var me *api.Me
	var meErr error
	if tokenErr == nil {
		resp, err := c.ReadMeWithResponse(ctx)
		switch {
		case err != nil:
			meErr = client.Transport(err)
		default:
			meErr = client.Check(resp.HTTPResponse, resp.Body)
			me = resp.JSON200
		}
	}

	if g.json {
		return render.JSON(g.out(), map[string]any{
			"instance":    address,
			"handshake":   shake,
			"token":       map[string]any{"from": tokenFrom, "identity": me},
			"project":     project,
			"environment": environment,
		})
	}

	out := g.out()
	fmt.Fprintf(out, "instance     %s\n", address)
	if shakeErr != nil {
		fmt.Fprintf(out, "             %v\n", shakeErr)
	} else if shake != nil {
		fmt.Fprintf(out, "release      %s, serving %v\n", shake.Release, shake.ApiVersions)
	}

	switch {
	case tokenErr != nil:
		fmt.Fprintf(out, "token        none: %v\n", tokenErr)
	case meErr != nil:
		fmt.Fprintf(out, "token        from %s, and the instance did not accept it: %v\n", tokenFrom, meErr)
	case me != nil:
		fmt.Fprintf(out, "token        %s, from %s\n", me.TokenKind, tokenFrom)
		fmt.Fprintf(out, "acting as    %s\n", me.Name)
		fmt.Fprintf(out, "scopes       %v\n", me.Scopes)
	default:
		fmt.Fprintf(out, "token        from %s\n", tokenFrom)
	}

	if bindingErr != nil {
		fmt.Fprintf(out, "directory    %s, unbound\n", g.dir())
		fmt.Fprintf(out, "             %v\n", bindingErr)
		return nil
	}
	fmt.Fprintf(out, "directory    %s\n", g.dir())
	fmt.Fprintf(out, "project      %s, from %s\n", project, projectFrom)
	fmt.Fprintf(out, "environment  %s, from %s\n", environment, envFrom)
	return nil
}

// Package cmd is the command tree: `vaultaffe <object> <verb>`, with the two
// commands that come before every other one — `login` and `setup`.
//
// Data goes to stdout, everything a person reads goes to stderr, and the exit
// code says what happened (docs/cli.md). Nothing here is ever interactive:
// stdin is read where a flag or a pipe says so and never to ask a question,
// because a prompt in an agent's terminal is a command that hangs
// (Specification §6.2).
package cmd

import (
	"context"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"strings"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/client"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/config"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/exit"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/keychain"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/version"
)

// Keychain is the store a session token goes into, as a set of functions so
// that a test never touches the machine's own (ADR 0012).
type Keychain struct {
	Read   func(instance string) (string, error)
	Store  func(instance, token string) error
	Forget func(instance string) error
}

// Env is what a command runs in, so that a test supplies all of it.
type Env struct {
	Getenv   func(string) string
	Dir      string
	Stdin    io.Reader
	Stdout   io.Writer
	Stderr   io.Writer
	HTTP     *http.Client
	Keychain Keychain
	// Environ is the environment `run` starts from and takes things out of.
	Environ func() []string
	// Exec replaces this process with another (Specification §6.2). It is here
	// rather than called directly so that a test of `run` can see what would
	// have been executed instead of ceasing to exist.
	Exec func(path string, argv []string, env []string) error
}

// Run executes args and answers the exit code.
func Run(ctx context.Context, args []string, env Env) int {
	root := newRoot(env)
	root.SetArgs(args)
	root.SetIn(env.Stdin)
	root.SetOut(env.Stdout)
	root.SetErr(env.Stderr)

	if err := root.ExecuteContext(ctx); err != nil {
		return report(root, env.Stderr, err)
	}
	return exit.OK
}

func report(root *cobra.Command, stderr io.Writer, err error) int {
	var failure *client.Failure
	var usage *config.UsageError
	switch {
	case errors.As(err, &failure):
		fmt.Fprintln(stderr, "vaultaffe:", failure.Message)
		if failure.Problem.Code() == "human-only" {
			fmt.Fprintln(stderr, "vaultaffe:", advice(root, failure.Problem.String("humanAction")))
		}
		return failure.Code
	case errors.As(err, &usage):
		fmt.Fprintln(stderr, "vaultaffe:", usage.Message)
		return exit.Usage
	default:
		fmt.Fprintln(stderr, "vaultaffe:", err)
		return exit.Unexpected
	}
}

// humanCommands maps the action a refusal names — `humanAction`, docs/api.md —
// to the command of *this* CLI that a person does it with. An entry is added by
// the commit that adds the command, and `advice` asks the tree whether it is
// really there, so that an entry outliving its command cannot become a
// suggestion to run something that does not exist
// ([ADR 0010](../../../../docs/adr/0010-a-refusal-names-the-action-and-the-client-names-the-command.md)).
//
// `administer-organization` was deliberately absent while there was no command
// for it, because inventing one would have been exactly the failure the ADR is
// about. It is here now, and `advice` still asks the tree rather than trusting
// this table.
//
// `purge` maps to one of four commands, and the table names the one an agent is
// most likely to have been refused: purging a key's history is the case §6.5 is
// written for. `advice` says "a person does this with …" rather than "the only
// way is …", so naming one of a family is a signpost and not a claim.
var humanCommands = map[string]string{
	"export":                  "secrets export",
	"create-token":            "tokens create",
	"revoke-token":            "tokens revoke",
	"purge":                   "secrets purge-history",
	"administer-organization": "users",
}

func advice(root *cobra.Command, action string) string {
	if path, known := humanCommands[action]; known {
		if found, _, err := root.Find(strings.Fields(path)); err == nil && found.CommandPath() == root.Name()+" "+path {
			return fmt.Sprintf("A person does this with `%s` under their own session.", found.CommandPath())
		}
	}
	return "This CLI has no command for it: a person does it in the management interface."
}

type globals struct {
	env          Env
	json         bool
	project      string
	environment  string
	address      string
	insecureHTTP bool
	root         *cobra.Command
}

func newRoot(env Env) *cobra.Command {
	g := &globals{env: env}
	root := &cobra.Command{
		Use:   "vaultaffe",
		Short: "Secrets for teams that work with agents.",
		Long: "vaultaffe from the console: the interface an agent uses, and the one a\n" +
			"console-minded person uses. A value reaches this terminal only where a\n" +
			"command was explicitly asked for one.",
		Version:       version.Value,
		SilenceUsage:  true,
		SilenceErrors: true,
	}
	root.SetVersionTemplate("vaultaffe {{.Version}}\n")
	root.PersistentFlags().BoolVar(&g.json, "json", false, "print the answer as the API gave it")
	root.PersistentFlags().StringVar(&g.project, "project", "", "the project; defaults to the binding of this directory")
	root.PersistentFlags().StringVar(&g.environment, "environment", "", "the environment; defaults to the binding of this directory")
	root.PersistentFlags().StringVar(&g.address, "url", "", "the instance; defaults to "+config.EnvURL+" or the one logged in to")
	root.PersistentFlags().BoolVar(&g.insecureHTTP, "insecure-http", false, "allow plain HTTP to a host that is not loopback")

	// A usage mistake leaves with 2 and one sentence, rather than with a wall of
	// help nobody reads and an exit code that says nothing.
	root.SetFlagErrorFunc(func(_ *cobra.Command, err error) error {
		return &config.UsageError{Message: err.Error()}
	})

	root.AddCommand(newLogin(g), newLogout(g), newSetup(g), newStatus(g), newInstance(g), newRun(g), newSecrets(g), newChanges(g), newProjects(g), newEnvironments(g), newTokens(g), newUsers(g), newInvitations(g), newOrganization(g))
	g.root = root
	return root
}

// dir is the directory this invocation was run in.
func (g *globals) dir() string {
	if g.env.Dir != "" {
		return g.env.Dir
	}
	dir, _ := os.Getwd()
	return dir
}

func (g *globals) getenv(name string) string {
	if g.env.Getenv == nil {
		return os.Getenv(name)
	}
	return g.env.Getenv(name)
}

func (g *globals) configPath() (string, error) {
	return config.Path(g.getenv)
}

func (g *globals) readConfig() (config.File, error) {
	path, err := g.configPath()
	if err != nil {
		return config.File{}, err
	}
	return config.Load(path)
}

func (g *globals) writeConfig(file config.File) error {
	path, err := g.configPath()
	if err != nil {
		return err
	}
	return config.Save(path, file)
}

func (g *globals) keychain() Keychain {
	kc := g.env.Keychain
	if kc.Read == nil {
		kc.Read = keychain.Read
	}
	if kc.Store == nil {
		kc.Store = keychain.Store
	}
	if kc.Forget == nil {
		kc.Forget = keychain.Forget
	}
	return kc
}

func (g *globals) input() (config.Input, error) {
	file, err := g.readConfig()
	if err != nil {
		return config.Input{}, err
	}
	return config.Input{
		Getenv:         g.getenv,
		Dir:            g.dir(),
		File:           file,
		Address:        g.address,
		Project:        g.project,
		Environment:    g.environment,
		AllowPlainHTTP: g.insecureHTTP,
		ReadKeychain:   g.keychain().Read,
	}, nil
}

func (g *globals) httpClient() *http.Client {
	if g.env.HTTP != nil {
		return g.env.HTTP
	}
	return client.Default()
}

// instance is what a command that talks to the API but names no project starts
// with: the address and the token, and no binding.
func (g *globals) instance() (config.Resolved, *client.Client, error) {
	in, err := g.input()
	if err != nil {
		return config.Resolved{}, nil, err
	}

	address, err := in.ResolveAddress()
	if err != nil {
		return config.Resolved{}, nil, err
	}
	token, from, err := in.ResolveToken(address)
	if err != nil {
		return config.Resolved{}, nil, err
	}

	c, err := client.New(address, token, g.httpClient())
	return config.Resolved{Address: address, Token: token, TokenFrom: from}, c, err
}

// bound is what a command about a secret starts with: the address, the token,
// and which project and environment this directory means.
func (g *globals) bound() (config.Resolved, *client.Client, error) {
	in, err := g.input()
	if err != nil {
		return config.Resolved{}, nil, err
	}

	resolved, err := config.Resolve(in)
	if err != nil {
		return resolved, nil, err
	}

	c, err := client.New(resolved.Address, resolved.Token, g.httpClient())
	return resolved, c, err
}

// inProject is what a command about a project starts with: the address, the
// token, and which project — and no environment, because it does not need one.
func (g *globals) inProject() (config.Resolved, *client.Client, error) {
	in, err := g.input()
	if err != nil {
		return config.Resolved{}, nil, err
	}

	address, err := in.ResolveAddress()
	if err != nil {
		return config.Resolved{}, nil, err
	}
	token, from, err := in.ResolveToken(address)
	if err != nil {
		return config.Resolved{}, nil, err
	}
	project, projectFrom, err := in.ResolveProject()
	if err != nil {
		return config.Resolved{}, nil, err
	}

	c, err := client.New(address, token, g.httpClient())
	return config.Resolved{
		Address: address, Token: token, TokenFrom: from,
		Project: project, ProjectFrom: projectFrom,
	}, c, err
}

// anonymous is what `login` and the first run start with: an address and no
// token, because there is not one yet.
func (g *globals) anonymous() (string, *client.Client, error) {
	in, err := g.input()
	if err != nil {
		return "", nil, err
	}

	address, err := in.ResolveAddress()
	if err != nil {
		return "", nil, err
	}

	c, err := client.New(address, "", g.httpClient())
	return address, c, err
}

func (g *globals) out() io.Writer {
	if g.env.Stdout == nil {
		return os.Stdout
	}
	return g.env.Stdout
}

func (g *globals) msg() io.Writer {
	if g.env.Stderr == nil {
		return os.Stderr
	}
	return g.env.Stderr
}

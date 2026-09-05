package cmd

import (
	"context"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"sort"
	"strings"
	"sync"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/api"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/client"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/config"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/exit"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/render"
)

// protected is the fixed, documented list of Specification §6.2. "And friends"
// is not a specification, so it is spelled out here and nowhere else.
//
// A value from the store never overwrites one of these. `VAULTAFFE_` goes
// further and is removed from the child altogether: the token an agent runs
// under must not reach what it starts, and after `exec()` there is no second
// chance. The loader variables are here so that a token which may write cannot
// inject code into every `run` on the machine.
var protectedNames = []string{"PATH", "HOME", "USER", "SHELL", "TMPDIR"}

var protectedPrefixes = []string{"LD_", "DYLD_", "VAULTAFFE_"}

// strippedPrefix is the one that is not merely protected but taken out.
const strippedPrefix = "VAULTAFFE_"

func isProtected(name string) bool {
	for _, protected := range protectedNames {
		if name == protected {
			return true
		}
	}
	for _, prefix := range protectedPrefixes {
		if strings.HasPrefix(name, prefix) {
			return true
		}
	}
	return false
}

func newRun(g *globals) *cobra.Command {
	var allowEmpty bool

	command := &cobra.Command{
		Use:   "run -- <command> [arguments...]",
		Short: "Start a process with this environment's secrets in its environment.",
		Long: "The command that sits in front of every other command, and therefore the\n" +
			"one where correctness is worth more than anything else.\n\n" +
			"This CLI does not stay as a parent: it builds the environment, says\n" +
			"everything it has to say, and then replaces itself with the process through\n" +
			"exec(). Signal forwarding, exit codes and process groups then take care of\n" +
			"themselves — a command killed by a signal reports 128+N, and `kill` on this\n" +
			"pid reaches the real process rather than a wrapper standing in front of it\n" +
			"(ADR 0009). Windows has no exec() and is not a target; WSL runs the Linux\n" +
			"binary.\n\n" +
			"Values reach the process as environment variables. There is no temporary\n" +
			"file and no plaintext on disk. Nothing of the process's own output is\n" +
			"masked: if it prints its own secret, it printed it.",
		Args:                  cobra.MinimumNArgs(1),
		DisableFlagsInUseLine: true,
		RunE: func(command *cobra.Command, args []string) error {
			return g.run(command.Context(), args, allowEmpty)
		},
	}
	command.Flags().BoolVar(&allowEmpty, "allow-empty", false,
		"start even though a key is an empty placeholder a person has still to fill")
	// Flags stop at the first thing that is not one, so that `run -- npm run
	// dev --json` passes --json to npm rather than to this CLI.
	command.Flags().SetInterspersed(false)
	return command
}

// report is what `run` says before it ceases to exist. It names keys and never
// values — the JSON form no more than the sentences.
type account struct {
	Project      string   `json:"project"`
	Environment  string   `json:"environment"`
	Injected     []string `json:"injected"`
	Replaced     []string `json:"replaced"`
	Kept         []string `json:"kept"`
	Stripped     []string `json:"stripped"`
	Placeholders []string `json:"placeholders"`
	Command      []string `json:"command"`
}

func (g *globals) run(ctx context.Context, args []string, allowEmpty bool) error {
	resolved, c, err := g.bound()
	if err != nil {
		return err
	}

	secrets, err := listSecrets(ctx, c, resolved.Project, resolved.Environment)
	if err != nil {
		return err
	}

	// An empty placeholder means a person still has to do something, and
	// injecting an empty string would hide exactly that (Specification §6.2).
	// It is decided here, before a single value is fetched: there is no reason
	// to read secrets for a process that is not going to start.
	placeholders := make([]string, 0)
	wanted := make([]string, 0, len(secrets))
	for _, secret := range secrets {
		if secret.Status == "empty" {
			placeholders = append(placeholders, secret.Name)
			continue
		}
		wanted = append(wanted, secret.Name)
	}
	sort.Strings(placeholders)
	sort.Strings(wanted)

	if len(placeholders) > 0 && !allowEmpty {
		return &client.Failure{Code: exit.Placeholder, Message: fmt.Sprintf(
			"%s/%s has %s a person has still to fill: %s. Pass --allow-empty to start anyway.",
			resolved.Project, resolved.Environment, plural(len(placeholders), "an empty placeholder", "empty placeholders"),
			strings.Join(placeholders, ", "))}
	}

	values, err := readValues(ctx, c, resolved.Project, resolved.Environment, wanted)
	if err != nil {
		return err
	}
	if allowEmpty {
		for _, name := range placeholders {
			values[name] = ""
		}
	}

	environment, said := build(g.environ(), values, resolved)
	said.Project, said.Environment = resolved.Project, resolved.Environment
	said.Placeholders, said.Command = placeholders, args

	// Everything is said before the call, because after it there is nobody left
	// to say anything (ADR 0009).
	g.say(said, resolved)

	path, err := exec.LookPath(args[0])
	if err != nil {
		return notExecutable(args[0], err)
	}

	execute := g.env.Exec
	if execute == nil {
		return &client.Failure{Code: exit.Unexpected, Message: "this build has no way to replace itself with a process"}
	}
	if err := execute(path, args, environment); err != nil {
		return &client.Failure{Code: exit.Unexpected, Message: fmt.Sprintf("%s could not be started: %v", path, err)}
	}
	// Reached only where Exec was substituted, which is a test.
	return nil
}

// build assembles the environment handed to exec() and the account of what
// happened to it.
func build(inherited []string, values map[string]string, resolved config.Resolved) ([]string, account) {
	said := account{Injected: []string{}, Replaced: []string{}, Kept: []string{}, Stripped: []string{}}

	kept := make([]string, 0, len(inherited))
	present := make(map[string]bool, len(inherited))
	for _, entry := range inherited {
		name, _, found := strings.Cut(entry, "=")
		if !found {
			continue
		}
		if strings.HasPrefix(name, strippedPrefix) {
			said.Stripped = append(said.Stripped, name)
			continue
		}
		present[name] = true
		kept = append(kept, entry)
	}

	names := make([]string, 0, len(values))
	for name := range values {
		names = append(names, name)
	}
	sort.Strings(names)

	for _, name := range names {
		if isProtected(name) {
			said.Kept = append(said.Kept, name)
			continue
		}
		if present[name] {
			said.Replaced = append(said.Replaced, name)
			kept = replace(kept, name, values[name])
			continue
		}
		said.Injected = append(said.Injected, name)
		kept = append(kept, name+"="+values[name])
	}

	sort.Strings(said.Stripped)
	return kept, said
}

func replace(environment []string, name, value string) []string {
	for i, entry := range environment {
		if existing, _, _ := strings.Cut(entry, "="); existing == name {
			environment[i] = name + "=" + value
			return environment
		}
	}
	return append(environment, name+"="+value)
}

// say reports every collision, because a collision usually means that
// machine-specific configuration has leaked into the store
// (Specification §5, §6.2).
func (g *globals) say(said account, resolved config.Resolved) {
	where := resolved.Project + "/" + resolved.Environment

	for _, name := range said.Replaced {
		fmt.Fprintf(g.msg(), "vaultaffe: %s was already in this environment; the value from %s replaces it.\n", name, where)
	}
	for _, name := range said.Kept {
		fmt.Fprintf(g.msg(), "vaultaffe: %s is on the protected list; what was already here stays, and %s was not injected into it.\n", name, where)
	}
	if len(said.Placeholders) > 0 {
		fmt.Fprintf(g.msg(), "vaultaffe: started with %s empty: %s.\n",
			plural(len(said.Placeholders), "a placeholder", "placeholders"), strings.Join(said.Placeholders, ", "))
	}

	if g.json {
		_ = render.JSON(g.out(), said)
	}
}

// listSecrets asks for names and status, which is the endpoint that carries no
// value at all.
func listSecrets(ctx context.Context, c *client.Client, project, environment string) ([]api.Secret, error) {
	resp, err := c.ReadSecretNamesWithResponse(ctx, project, environment, &api.ReadSecretNamesParams{})
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return nil, err
	}
	if resp.JSON200 == nil {
		return nil, &client.Failure{Code: exit.Unexpected, Message: "the instance answered a listing this CLI cannot read"}
	}
	return *resp.JSON200, nil
}

// readValues fetches one value per key, because reading a value is a request of
// its own and there is no bulk read that is not the human-only export
// (docs/api.md). A few at a time: a hundred keys serially would be a hundred
// round trips in front of a process somebody is waiting for, and a hundred at
// once would be a small denial of service against the instance.
func readValues(ctx context.Context, c *client.Client, project, environment string, names []string) (map[string]string, error) {
	const atOnce = 8

	values := make(map[string]string, len(names))
	var guard sync.Mutex
	var wait sync.WaitGroup
	var failure error

	ctx, stop := context.WithCancel(ctx)
	defer stop()

	tickets := make(chan struct{}, atOnce)
	for _, name := range names {
		wait.Add(1)
		go func(name string) {
			defer wait.Done()
			tickets <- struct{}{}
			defer func() { <-tickets }()

			value, err := readOne(ctx, c, project, environment, name)
			guard.Lock()
			defer guard.Unlock()
			if err != nil {
				if failure == nil {
					failure = err
					stop()
				}
				return
			}
			values[name] = value
		}(name)
	}
	wait.Wait()

	if failure != nil {
		return nil, failure
	}
	return values, nil
}

func readOne(ctx context.Context, c *client.Client, project, environment, name string) (string, error) {
	resp, err := c.ReadSecretWithResponse(ctx, project, environment, name)
	if err != nil {
		return "", client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return "", err
	}
	if resp.JSON200 == nil || resp.JSON200.Value == nil {
		return "", nil
	}
	return *resp.JSON200.Value, nil
}

// notExecutable tells the two failures of the lookup apart, because a shell
// does: 127 is a command that is not there, 126 one that is there and cannot be
// run (ADR 0009).
func notExecutable(name string, err error) error {
	if errors.Is(err, exec.ErrNotFound) {
		return &client.Failure{Code: 127, Message: fmt.Sprintf("%s: no such command on the PATH.", name)}
	}
	return &client.Failure{Code: 126, Message: fmt.Sprintf("%s: found and not executable: %v", name, err)}
}

func (g *globals) environ() []string {
	if g.env.Environ != nil {
		return g.env.Environ()
	}
	return os.Environ()
}

func plural(count int, one, many string) string {
	if count == 1 {
		return one
	}
	return many
}

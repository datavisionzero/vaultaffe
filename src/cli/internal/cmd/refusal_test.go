package cmd

import (
	"strings"
	"testing"
)

// The server names the action and never a command; the client names the command
// it has (ADR 0010). This is the half that keeps the client honest: every entry
// in the table has to resolve to a command that is really in the tree, so that
// an entry outliving its command cannot become a suggestion to run something
// that does not exist.
func TestEveryHumanActionThisCLINamesResolvesToACommandItHas(t *testing.T) {
	root := newRoot(Env{})

	for action, path := range humanCommands {
		found, _, err := root.Find(strings.Fields(path))
		if err != nil || found.CommandPath() != root.Name()+" "+path {
			t.Fatalf("%s points at `vaultaffe %s`, which this CLI does not have", action, path)
		}
	}
}

// Administering the organization is a screen and not a command. The sentence
// says so rather than inventing one.
func TestAnActionWithNoCommandSaysSoRatherThanInventingOne(t *testing.T) {
	root := newRoot(Env{})

	said := advice(root, "administer-organization")

	if !strings.Contains(said, "no command") {
		t.Fatalf("it offered something instead: %q", said)
	}
	if strings.Contains(said, "vaultaffe ") {
		t.Fatalf("it named a command anyway: %q", said)
	}
}

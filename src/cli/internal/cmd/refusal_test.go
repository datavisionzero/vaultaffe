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

// The other half, and the one the stage after the MVP made true: every action
// the server can refuse with now has a command here. While one of them did not,
// the sentence said so rather than inventing one — which is the behaviour the
// test below still holds.
func TestEveryActionTheServerCanNameHasACommandHere(t *testing.T) {
	root := newRoot(Env{})

	// The wire spellings of Specification §6.4, as docs/api.md lists them.
	for _, action := range []string{
		"purge", "create-token", "revoke-token", "administer-organization", "export",
	} {
		said := advice(root, action)

		if !strings.Contains(said, "vaultaffe ") {
			t.Fatalf("%s has a command in this CLI, and the advice did not name it: %q", action, said)
		}
	}
}

// An action this CLI has no command for says so rather than inventing one. It
// is the case a newer instance produces — an action this build has never heard
// of — and the one an entry outliving its command produces.
func TestAnActionWithNoCommandSaysSoRatherThanInventingOne(t *testing.T) {
	root := newRoot(Env{})

	for _, action := range []string{"rotate-the-master-key", ""} {
		said := advice(root, action)

		if !strings.Contains(said, "no command") {
			t.Fatalf("it offered something instead: %q", said)
		}
		if strings.Contains(said, "vaultaffe ") {
			t.Fatalf("it named a command anyway: %q", said)
		}
	}
}

// And an entry that outlived its command is the same answer, which is the point
// of asking the tree instead of trusting the table.
func TestAnEntryPointingAtNothingIsNotASuggestion(t *testing.T) {
	humanCommands["purge"] = "secrets incinerate"
	defer func() { humanCommands["purge"] = "secrets purge-history" }()

	said := advice(newRoot(Env{}), "purge")

	if strings.Contains(said, "vaultaffe ") {
		t.Fatalf("it named a command this CLI does not have: %q", said)
	}
}

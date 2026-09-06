package cmd

import (
	"net/http"
	"strings"
	"testing"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/exit"
)

// The one deletion this product cannot undo, and the one thing no product we
// looked at says out loud: a purge in the database does not reach into last
// night's backup. The command says it every time.
func TestAPurgeSaysWhatItRemovedAndWhatStillHasIt(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("DELETE /api/v1/projects/billing/environments/dev/secrets/STRIPE_KEY/versions",
		http.StatusOK, map[string]any{
			"project": "billing", "environment": "dev", "secret": "STRIPE_KEY", "versions": 3,
		})

	got := bound(newSession(t, in)).run(t, "secrets", "purge-history", "STRIPE_KEY")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "3 retained values") {
		t.Fatalf("it did not say how much is gone:\n%s", got.Stderr)
	}
	if !strings.Contains(got.Stderr, "backup") {
		t.Fatalf("it did not say what still has them:\n%s", got.Stderr)
	}
}

// Nothing here prompts. A prompt in an agent's terminal is a command that hangs
// (Specification §6.2); what makes a purge deliberate is that it was typed, and
// what makes it safe is that only a session can do it.
func TestAPurgeAsksNothingAndIsRefusedForATokenByTheInstance(t *testing.T) {
	in := startInstanceStub(t)
	in.refuse("POST /api/v1/projects/billing/purge", http.StatusForbidden, "human-only",
		"Purging is reserved for a person: it is the one deletion this product cannot undo.",
		map[string]any{"humanAction": "purge"})

	got := bound(newSession(t, in)).run(t, "projects", "purge", "billing")

	if got.Code != exit.HumanOnly {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "vaultaffe secrets purge-history") {
		t.Fatalf("it did not name a command a person has:\n%s", got.Stderr)
	}
}

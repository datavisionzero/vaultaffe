package cmd

import (
	"net/http"
	"strings"
	"testing"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/exit"
)

const missingPath = "/api/v1/projects/billing/environments/dev/missing"

// A display, and it shows the reason rather than asking anybody to take it: the
// environments that have the key are on the line.
func TestTheNoticeNamesTheEnvironmentsThatHaveTheKey(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET "+missingPath, http.StatusOK, []map[string]any{
		{"name": "STRIPE_KEY", "presentIn": []string{"prod", "staging"}, "dismissedAt": nil},
	})

	got := bound(newSession(t, in)).run(t, "secrets", "missing")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if !strings.Contains(got.Stdout, "STRIPE_KEY") || !strings.Contains(got.Stdout, "prod, staging") {
		t.Fatalf("the notice did not say what it is about:\n%s", got.Stdout)
	}
}

// A dismissed line is in the same answer, and this is the switch that shows it
// instead — the same shape as `--deleted` everywhere else in this CLI.
func TestDismissedLinesAreBehindTheSwitchAndNotInTheNotice(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET "+missingPath, http.StatusOK, []map[string]any{
		{"name": "STRIPE_KEY", "presentIn": []string{"prod"}, "dismissedAt": "2026-01-02T00:00:00Z"},
	})

	s := bound(newSession(t, in))

	if said := s.run(t, "secrets", "missing"); strings.Contains(said.Stdout, "STRIPE_KEY") {
		t.Fatalf("a dismissed key was in the notice:\n%s", said.Stdout)
	}
	if said := s.run(t, "secrets", "missing", "--dismissed"); !strings.Contains(said.Stdout, "STRIPE_KEY") {
		t.Fatalf("the dismissed key was nowhere:\n%s", said.Stdout)
	}
}

// Saying "not here" creates nothing, and taking it back is the same command
// with a flag rather than a second verb nobody would find.
func TestDismissingAndTakingItBackAreOneCommand(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("POST "+missingPath+"/STRIPE_KEY/dismissal", http.StatusOK, map[string]any{
		"name": "STRIPE_KEY", "presentIn": []string{"prod"}, "dismissedAt": "2026-01-02T00:00:00Z",
	})
	in.answer("DELETE "+missingPath+"/STRIPE_KEY/dismissal", http.StatusOK, map[string]any{
		"name": "STRIPE_KEY", "presentIn": []string{"prod"}, "dismissedAt": nil,
	})

	s := bound(newSession(t, in))

	if said := s.run(t, "secrets", "missing", "dismiss", "STRIPE_KEY"); said.Code != exit.OK {
		t.Fatalf("exit %d\n%s", said.Code, said.Stderr)
	}
	in.sent("POST", missingPath+"/STRIPE_KEY/dismissal")

	if said := s.run(t, "secrets", "missing", "dismiss", "STRIPE_KEY", "--undo"); said.Code != exit.OK {
		t.Fatalf("exit %d\n%s", said.Code, said.Stderr)
	}
	in.sent("DELETE", missingPath+"/STRIPE_KEY/dismissal")
}

// Two moments per identity, its kind beside it, and no count — the line that
// keeps this from reading as a record of every run.
func TestTheAccessSummaryIsTwoMomentsAndAKind(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/projects/billing/environments/dev/secrets/STRIPE_KEY/access",
		http.StatusOK, []map[string]any{
			{
				"identity": map[string]any{
					"id": maintainerID, "type": "agent-token", "name": "the deploy agent",
				},
				"firstAt": "2026-01-01T00:00:00Z", "lastAt": "2026-01-05T00:00:00Z",
			},
		})

	got := bound(newSession(t, in)).run(t, "secrets", "access", "STRIPE_KEY")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	for _, wanted := range []string{"the deploy agent", "agent-token", "first ", "last "} {
		if !strings.Contains(got.Stdout, wanted) {
			t.Fatalf("%q was not in the summary:\n%s", wanted, got.Stdout)
		}
	}
}

package cmd

import (
	"encoding/json"
	"net/http"
	"strings"
	"testing"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/exit"
)

// Ids a test can read: what is in each row matters more here than which id it
// carries, so they are named after what they are.
const (
	deployAgentID = "6f1f1a2e-0000-4000-8000-000000000020"
	ciServiceID   = "6f1f1a2e-0000-4000-8000-000000000021"
	revokedCIID   = "6f1f1a2e-0000-4000-8000-000000000022"
	laptopID      = "6f1f1a2e-0000-4000-8000-000000000023"
	oldLaptopID   = "6f1f1a2e-0000-4000-8000-000000000024"
	revokedTabID  = "6f1f1a2e-0000-4000-8000-000000000025"
)

// The instance answers one undivided listing, in no order this client may rely
// on — which is the point: the grouping is the client's, and the order the
// instance happens to use is deliberately not the one a person wants.
func tokens() []map[string]any {
	return []map[string]any{
		{
			"id": laptopID, "kind": "session", "name": nil,
			"scopes":   []string{"names", "read", "write", "delete"},
			"bindings": []any{}, "reachesTheWholeOrganization": true,
			"createdAt": "2026-03-01T00:00:00Z", "expiresAt": nil, "revokedAt": nil,
		},
		{
			"id": revokedCIID, "kind": "service", "name": "an old ci",
			"scopes":   []string{"names", "read"},
			"bindings": []any{}, "reachesTheWholeOrganization": true,
			"createdAt": "2026-01-01T00:00:00Z", "expiresAt": nil,
			"revokedAt": "2026-02-01T00:00:00Z",
		},
		{
			"id": revokedTabID, "kind": "session", "name": nil,
			"scopes":   []string{"names", "read", "write", "delete"},
			"bindings": []any{}, "reachesTheWholeOrganization": true,
			"createdAt": "2026-04-01T00:00:00Z", "expiresAt": nil,
			"revokedAt": "2026-04-02T00:00:00Z",
		},
		{
			"id": ciServiceID, "kind": "service", "name": "ci",
			"scopes":   []string{"names", "read"},
			"bindings": []any{}, "reachesTheWholeOrganization": true,
			"createdAt": "2026-02-01T00:00:00Z", "expiresAt": nil, "revokedAt": nil,
		},
		{
			"id": oldLaptopID, "kind": "session", "name": nil,
			"scopes":   []string{"names", "read", "write", "delete"},
			"bindings": []any{}, "reachesTheWholeOrganization": true,
			"createdAt": "2026-01-15T00:00:00Z", "expiresAt": nil, "revokedAt": nil,
		},
		{
			"id": deployAgentID, "kind": "agent", "name": "a deploy agent",
			"scopes":   []string{"names", "read", "write", "delete"},
			"bindings": []any{}, "reachesTheWholeOrganization": true,
			"createdAt": "2026-02-15T00:00:00Z", "expiresAt": nil, "revokedAt": nil,
		},
	}
}

func listed(t *testing.T) console {
	t.Helper()

	in := startInstanceStub(t)
	in.answer("GET /api/v1/tokens", http.StatusOK, tokens())

	got := bound(newSession(t, in)).run(t, "tokens")
	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	return got
}

// The acceptance criterion this command exists to keep: a table is greppable
// and an interface is read, so the two headings are on stderr and every row is
// on stdout. `vaultaffe tokens | grep …` receives exactly what it received
// before the listing was ever divided.
func TestTheHeadingsAreOnStderrAndStdoutIsNothingButRows(t *testing.T) {
	got := listed(t)

	for _, heading := range []string{"Tokens —", "Sessions —"} {
		if !strings.Contains(got.Stderr, heading) {
			t.Fatalf("stderr does not head the sections:\n%s", got.Stderr)
		}
		if strings.Contains(got.Stdout, heading) {
			t.Fatalf("a heading reached stdout:\n%s", got.Stdout)
		}
	}

	rows := lines(got.Stdout)
	if len(rows) != len(tokens()) {
		t.Fatalf("stdout holds %d lines, not the %d rows:\n%s", len(rows), len(tokens()), got.Stdout)
	}
	for _, row := range rows {
		// Every line is a row, and a row starts with the id it is about.
		if !strings.HasPrefix(row, "6f1f1a2e-") {
			t.Fatalf("stdout holds something that is not a row: %q", row)
		}
	}
}

// The two lists answer two different questions, so they are ordered by two
// different things: an inventory reads by name, a list of devices reads newest
// first. What is revoked stays below what still works in both, because a
// revocation list that hides revocations is not one.
func TestTheTwoListsAreOrderedTheWayTheConsoleOrdersThem(t *testing.T) {
	got := listed(t)

	want := []string{
		// Tokens: in use first and then by name, so "a deploy agent" before
		// "ci"; the revoked one below both.
		deployAgentID, ciServiceID, revokedCIID,
		// Sessions: in use first and then newest first; the revoked one below.
		laptopID, oldLaptopID, revokedTabID,
	}

	rows := lines(got.Stdout)
	for i, id := range want {
		if !strings.HasPrefix(rows[i], id) {
			t.Fatalf("row %d is %q, and %s was expected there:\n%s", i, rows[i], id, got.Stdout)
		}
	}
}

// The grouping is a thing this client does for a person reading a terminal.
// Whoever reads JSON reads the instance's own answer and groups it themselves.
func TestJSONIsTheInstancesAnswerAndIsNotGrouped(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/tokens", http.StatusOK, tokens())

	got := bound(newSession(t, in)).run(t, "--json", "tokens")
	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}

	var answered []map[string]any
	if err := json.Unmarshal([]byte(got.Stdout), &answered); err != nil {
		t.Fatalf("stdout is not the listing: %v\n%s", err, got.Stdout)
	}
	if len(answered) != len(tokens()) {
		t.Fatalf("json holds %d and the instance answered %d", len(answered), len(tokens()))
	}
	for i, token := range tokens() {
		if answered[i]["id"] != token["id"] {
			t.Fatalf("json is reordered: %v is at %d, not %v", answered[i]["id"], i, token["id"])
		}
	}
	if strings.Contains(got.Stderr, "Sessions —") {
		t.Fatalf("json mode still printed a heading:\n%s", got.Stderr)
	}
}

// An empty half is said rather than left blank: a person who reads "no session
// on record" knows the list was asked for. It is a sentence, so it is on
// stderr, and stdout stays empty for whatever is reading it.
func TestAnEmptyHalfIsSaidOnStderrAndLeavesStdoutEmpty(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/tokens", http.StatusOK, []map[string]any{})

	got := bound(newSession(t, in)).run(t, "tokens")
	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if strings.TrimSpace(got.Stdout) != "" {
		t.Fatalf("stdout is not empty:\n%s", got.Stdout)
	}
	if !strings.Contains(got.Stderr, "No agent or service token yet.") ||
		!strings.Contains(got.Stderr, "No session on record.") {
		t.Fatalf("it did not say the halves are empty:\n%s", got.Stderr)
	}
}

// lines is what a pipe would see: the rows, without the blank ones a terminal
// gets for spacing.
func lines(out string) []string {
	var rows []string
	for _, line := range strings.Split(out, "\n") {
		if strings.TrimSpace(line) != "" {
			rows = append(rows, line)
		}
	}
	return rows
}

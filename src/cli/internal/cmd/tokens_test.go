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
	// And it says which of the two kinds of empty it is. "There is nothing" to
	// somebody whose tokens are all revoked is a sentence they have no way to
	// see through.
	if !strings.Contains(got.Stderr, "No agent or service token that works. Revoked ones are not shown: pass --revoked.") ||
		!strings.Contains(got.Stderr, "No session that works. Revoked ones are not shown: pass --revoked.") {
		t.Fatalf("it did not say the halves are empty:\n%s", got.Stderr)
	}
}

// What still authenticates, unless the revoked ones were asked for. The row of
// a revoked token stays for good — everything it signed in the change log keeps
// an author that way — and that is a reason to keep it, not a reason to keep it
// in front of the credentials somebody came here to read.
func TestTheListingAsksForWhatWorksUnlessTheRevokedAreWanted(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/tokens", http.StatusOK, tokens())

	s := bound(newSession(t, in))

	if got := s.run(t, "tokens"); got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if query := in.sent("GET", "/api/v1/tokens").Query; query != "" {
		t.Fatalf("the default listing asked for %q, and the address should say what was typed", query)
	}

	in.Requests = nil

	if got := s.run(t, "tokens", "--revoked"); got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if query := in.sent("GET", "/api/v1/tokens").Query; query != "revoked=true" {
		t.Fatalf("--revoked asked for %q", query)
	}
}

// Rotating prints the value once, the way creating does, and says in as many
// words that the one it replaced is dead — a run still holding it fails on its
// next request, and somebody has to be told that before they walk away.
func TestRotatingPrintsTheValueOnceAndSaysTheOldOneIsGone(t *testing.T) {
	const next = "vaultaffe_agent_theonethatreplacedit"

	in := startInstanceStub(t)
	in.answer("POST /api/v1/tokens/"+deployAgentID+"/rotate", http.StatusOK, map[string]any{
		"token": map[string]any{
			"id": ciServiceID, "kind": "agent", "name": "a deploy agent",
			"scopes":   []string{"names", "read", "write", "delete"},
			"bindings": []any{}, "reachesTheWholeOrganization": true,
			"createdAt": "2026-05-01T00:00:00Z", "expiresAt": nil, "revokedAt": nil,
		},
		"value": next,
	})

	got := bound(newSession(t, in)).run(t, "tokens", "rotate", deployAgentID)
	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}

	// The value is data and goes to stdout; everything a person reads is on
	// stderr, so `… | pbcopy` receives the value and nothing else.
	if strings.TrimSpace(got.Stdout) != next {
		t.Fatalf("stdout is not the value alone:\n%s", got.Stdout)
	}
	if strings.Contains(got.Stderr, next) {
		t.Fatalf("the value reached the prose as well:\n%s", got.Stderr)
	}
	if !strings.Contains(got.Stderr, "revoked") || !strings.Contains(got.Stderr, "fails on its next request") {
		t.Fatalf("it did not say the old value is dead:\n%s", got.Stderr)
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

// Changing sends what was asked for and nothing else: a flag nobody passed is a
// part of the token nobody meant to touch, and a request that carried it anyway
// would rewrite the reach of every token somebody only wanted to rename.
func TestChangingATokenSendsOnlyWhatWasAsked(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("PATCH /api/v1/tokens/"+deployAgentID, http.StatusOK, map[string]any{
		"id": deployAgentID, "kind": "agent", "name": "the agent on billing",
		"scopes":   []string{"names", "read", "write", "delete"},
		"bindings": []any{}, "reachesTheWholeOrganization": true,
		"createdAt": "2026-02-15T00:00:00Z", "expiresAt": nil, "revokedAt": nil,
	})

	got := bound(newSession(t, in)).run(t, "tokens", "change", deployAgentID, "--name", "the agent on billing")
	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}

	var sent map[string]any
	if err := json.Unmarshal(in.sent("PATCH", "/api/v1/tokens/"+deployAgentID).Body, &sent); err != nil {
		t.Fatal(err)
	}
	if sent["name"] != "the agent on billing" {
		t.Fatalf("the new name did not reach the instance: %v", sent)
	}
	for _, untouched := range []string{"scopes", "bindings"} {
		if _, there := sent[untouched]; there {
			t.Fatalf("%q was sent for a rename: %v", untouched, sent)
		}
	}

	// The one sentence a person needs afterwards, because it is the thing that
	// makes this command worth having.
	if !strings.Contains(got.Stderr, "value is unchanged") {
		t.Fatalf("it does not say the value is untouched:\n%s", got.Stderr)
	}
}

// The widest a token gets, and the only way to say it: no binding at all. An
// empty list rather than an omitted one, because omitted means unchanged here.
func TestChangingATokenToTheWholeOrganizationSendsNoBinding(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("PATCH /api/v1/tokens/"+ciServiceID, http.StatusOK, map[string]any{
		"id": ciServiceID, "kind": "service", "name": "ci",
		"scopes":   []string{"names", "read"},
		"bindings": []any{}, "reachesTheWholeOrganization": true,
		"createdAt": "2026-02-01T00:00:00Z", "expiresAt": nil, "revokedAt": nil,
	})

	got := bound(newSession(t, in)).run(t, "tokens", "change", ciServiceID, "--organization")
	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}

	var sent map[string]any
	if err := json.Unmarshal(in.sent("PATCH", "/api/v1/tokens/"+ciServiceID).Body, &sent); err != nil {
		t.Fatal(err)
	}
	bindings, ok := sent["bindings"].([]any)
	if !ok || len(bindings) != 0 {
		t.Fatalf("the whole organization did not travel as an empty binding: %v", sent)
	}
}

// The directory somebody stands in does not narrow a token. `bound` puts a
// project and an environment in the environment of this session, exactly as a
// working directory would, and a change that carried them would be a widening
// nobody typed.
func TestChangingATokenIgnoresTheBindingOfTheDirectory(t *testing.T) {
	in := startInstanceStub(t)

	got := bound(newSession(t, in)).run(t, "tokens", "change", deployAgentID)
	if got.Code != exit.Usage {
		t.Fatalf("exit %d, not the usage error:\n%s", got.Code, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "nothing to change") {
		t.Fatalf("it does not say what was missing:\n%s", got.Stderr)
	}
}

// Purging is offered on what is revoked, and the instance is the one that says
// so — but what the command prints afterwards is the half a person cannot see:
// the row is gone and the history is not.
func TestPurgingATokenSaysTheLogKeepsWhatItDid(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("POST /api/v1/tokens/"+revokedCIID+"/purge", http.StatusOK, map[string]any{
		"id": revokedCIID, "kind": "service", "name": "an old ci",
		"scopes":   []string{"names", "read"},
		"bindings": []any{}, "reachesTheWholeOrganization": true,
		"createdAt": "2026-01-01T00:00:00Z", "expiresAt": nil,
		"revokedAt": "2026-02-01T00:00:00Z",
	})

	got := bound(newSession(t, in)).run(t, "tokens", "purge", revokedCIID)
	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	in.sent("POST", "/api/v1/tokens/"+revokedCIID+"/purge")

	if !strings.Contains(got.Stderr, "an old ci") || !strings.Contains(got.Stderr, "keeps its name in the log") {
		t.Fatalf("it does not say what went and what stayed:\n%s", got.Stderr)
	}
}

// ADR 0010: the instance names the action and this client names its own command.
// An agent refused a widening is told the command a person actually has.
func TestARefusedChangeNamesTheCommandAPersonHas(t *testing.T) {
	in := startInstanceStub(t)
	in.refuse("PATCH /api/v1/tokens/"+deployAgentID, http.StatusForbidden, "human-only",
		"Changing a token is reserved for a person.", map[string]any{"humanAction": "change-token"})

	got := bound(newSession(t, in)).run(t, "tokens", "change", deployAgentID, "--scopes", "names,read")
	if got.Code != exit.HumanOnly {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "vaultaffe tokens change") {
		t.Fatalf("the refusal does not name this CLI's own command:\n%s", got.Stderr)
	}
}

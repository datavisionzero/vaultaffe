package cmd

import (
	"encoding/json"
	"net/http"
	"strings"
	"testing"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/exit"
)

const projectID = "6f1f1a2e-0000-4000-8000-000000000002"
const environmentID = "6f1f1a2e-0000-4000-8000-000000000003"

func aProject(name string, environments ...string) map[string]any {
	listed := make([]map[string]any, 0, len(environments))
	for _, environment := range environments {
		listed = append(listed, map[string]any{
			"id": environmentID, "projectId": projectID, "name": environment,
			"createdAt": "2026-01-01T00:00:00Z", "deletedAt": nil,
		})
	}
	return map[string]any{
		"id": projectID, "name": name, "createdAt": "2026-01-01T00:00:00Z",
		"deletedAt": nil, "environments": listed,
	}
}

// A project arrives with its three environments unless others are named. That
// is the server's rule, and the CLI's job is not to send a list nobody asked
// for.
func TestCreatingAProjectSendsNoEnvironmentListByDefault(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("POST /api/v1/projects", http.StatusOK, aProject("billing", "dev", "staging", "prod"))

	s := bound(newSession(t, in))
	got := s.run(t, "projects", "create", "billing")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	var sent map[string]any
	if err := json.Unmarshal(in.sent("POST", "/api/v1/projects").Body, &sent); err != nil {
		t.Fatal(err)
	}
	if _, named := sent["environments"]; named {
		t.Fatalf("an environment list was sent anyway: %v", sent)
	}
	if !strings.Contains(got.Stderr, "staging") {
		t.Fatalf("what was created was not said:\n%s", got.Stderr)
	}
}

// The explicit empty list is a flag of its own, so that it cannot be confused
// with a forgotten one.
func TestNoEnvironmentsIsAnExplicitEmptyList(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("POST /api/v1/projects", http.StatusOK, aProject("billing"))

	got := bound(newSession(t, in)).run(t, "projects", "create", "billing", "--no-environments")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	var sent map[string]any
	if err := json.Unmarshal(in.sent("POST", "/api/v1/projects").Body, &sent); err != nil {
		t.Fatal(err)
	}
	listed, named := sent["environments"].([]any)
	if !named || len(listed) != 0 {
		t.Fatalf("the empty list did not arrive: %v", sent)
	}
}

// "I deleted it, why can I not recreate it" is the question this refusal is
// regularly misread as not answering.
func TestARecreatedNameSaysWhatIsHoldingIt(t *testing.T) {
	in := startInstanceStub(t)
	in.refuse("POST /api/v1/projects", http.StatusConflict, "name-taken",
		"Something of that name is here.", map[string]any{"takenBySomethingDeleted": true})

	got := bound(newSession(t, in)).run(t, "projects", "create", "billing")

	if got.Code != exit.Conflict {
		t.Fatalf("exit %d, want %d\n%s", got.Code, exit.Conflict, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "restore") {
		t.Fatalf("the way back was not named:\n%s", got.Stderr)
	}
}

// Environments belong to a project, and which project is the binding of this
// directory unless one is named.
func TestEnvironmentsFollowTheBindingOfThisDirectory(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/projects/billing/environments", http.StatusOK, []map[string]any{
		{"id": environmentID, "projectId": projectID, "name": "dev", "createdAt": "2026-01-01T00:00:00Z", "deletedAt": nil},
	})

	got := bound(newSession(t, in)).run(t, "environments")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if strings.TrimSpace(got.Stdout) != "dev" {
		t.Fatalf("printed %q", got.Stdout)
	}
}

func TestDeletingAProjectSaysItIsRecoverable(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("DELETE /api/v1/projects/billing", http.StatusOK, aProject("billing"))

	got := bound(newSession(t, in)).run(t, "projects", "delete", "billing")

	if got.Code != exit.OK || !strings.Contains(got.Stderr, "recoverable") {
		t.Fatalf("exit %d:\n%s", got.Code, got.Stderr)
	}
}

// Past the window it is a refusal rather than a missing row, and it has an exit
// code of its own so that a script can tell the two apart.
func TestRestoringPastTheWindowIsItsOwnOutcome(t *testing.T) {
	in := startInstanceStub(t)
	in.refuse("POST /api/v1/projects/billing/restore", http.StatusGone, "not-recoverable",
		"Deleted longer ago than the recovery window.")

	got := bound(newSession(t, in)).run(t, "projects", "restore", "billing")

	if got.Code != exit.Gone {
		t.Fatalf("exit %d, want %d\n%s", got.Code, exit.Gone, got.Stderr)
	}
}

// The value is printed exactly once, by the command that created it, and the
// person is told that this was the once.
func TestCreatingATokenPrintsTheValueOnceAndSaysSo(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("POST /api/v1/tokens", http.StatusOK, map[string]any{
		"token": map[string]any{
			"id": "6f1f1a2e-0000-4000-8000-000000000004", "kind": "agent", "name": "the deploy agent",
			"scopes": []string{"names", "read", "write", "delete"}, "bindings": []any{},
			"reachesTheWholeOrganization": true, "createdAt": "2026-01-01T00:00:00Z",
			"expiresAt": nil, "revokedAt": nil,
		},
		"value": "vaultaffe_agent_theonlytimeitisshown",
	})

	got := bound(newSession(t, in)).run(t, "tokens", "create", "the deploy agent", "--kind", "agent")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if strings.TrimSpace(got.Stdout) != "vaultaffe_agent_theonlytimeitisshown" {
		t.Fatalf("the value is not what was printed: %q", got.Stdout)
	}
	if !strings.Contains(got.Stderr, "only time") || !strings.Contains(got.Stderr, "VAULTAFFE_TOKEN") {
		t.Fatalf("what to do with it was not said:\n%s", got.Stderr)
	}
}

// A binding is by id and this CLI speaks names, so the names are looked up —
// and only the ones passed here count. The directory somebody happens to stand
// in must not quietly narrow a token.
func TestATokenIsBoundByWhatWasPassedAndNotByTheDirectory(t *testing.T) {
	in := startInstanceStub(t)
	aCatalogue(in)
	in.answer("POST /api/v1/tokens", http.StatusOK, map[string]any{
		"token": map[string]any{
			"id": "6f1f1a2e-0000-4000-8000-000000000004", "kind": "agent", "name": "bound",
			"scopes":                      []string{"names", "read"},
			"bindings":                    []map[string]any{{"projectId": projectID, "environmentId": environmentID}},
			"reachesTheWholeOrganization": false, "createdAt": "2026-01-01T00:00:00Z",
			"expiresAt": nil, "revokedAt": nil,
		},
		"value": "vaultaffe_agent_bound",
	})

	s := bound(newSession(t, in))

	// Standing in a bound directory and saying nothing binds nothing.
	if got := s.run(t, "tokens", "create", "bound", "--scopes", "names,read"); got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	var sent map[string]any
	if err := json.Unmarshal(in.sent("POST", "/api/v1/tokens").Body, &sent); err != nil {
		t.Fatal(err)
	}
	if _, narrowed := sent["bindings"]; narrowed {
		t.Fatalf("the directory's binding became the token's: %v", sent)
	}
	if scopes, _ := sent["scopes"].([]any); len(scopes) != 2 {
		t.Fatalf("the scopes did not arrive: %v", sent)
	}

	// Saying it does.
	in.Requests = nil
	if got := s.run(t, "tokens", "create", "bound", "--project", "billing", "--environment", "dev"); got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if err := json.Unmarshal(in.sent("POST", "/api/v1/tokens").Body, &sent); err != nil {
		t.Fatal(err)
	}
	bindings, narrowed := sent["bindings"].([]any)
	if !narrowed || len(bindings) != 1 {
		t.Fatalf("the binding did not arrive: %v", sent)
	}
	first := bindings[0].(map[string]any)
	if first["projectId"] != projectID || first["environmentId"] != environmentID {
		t.Fatalf("the names were not turned into ids: %v", first)
	}
}

// An environment that is not there is found out at this end, against what is,
// rather than as a validation failure about a field nobody typed.
func TestAnEnvironmentThatIsNotThereIsNamedAgainstWhatIs(t *testing.T) {
	in := startInstanceStub(t)
	aCatalogue(in)

	got := bound(newSession(t, in)).run(t, "tokens", "create", "bound", "--project", "billing", "--environment", "prud")

	if got.Code != exit.Usage {
		t.Fatalf("exit %d, want %d\n%s", got.Code, exit.Usage, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "dev") {
		t.Fatalf("what is there was not said:\n%s", got.Stderr)
	}
}

// Creating a token is human-only, and the refusal names the command a person
// runs — which this CLI now really has.
func TestATokenAnAgentAsksForNamesTheCommandAPersonRuns(t *testing.T) {
	in := startInstanceStub(t)
	in.refuse("POST /api/v1/tokens", http.StatusForbidden, "human-only",
		"Creating a token is reserved for a person: the answer is itself a secret.",
		map[string]any{"humanAction": "create-token"})

	got := bound(newSession(t, in)).run(t, "tokens", "create", "the deploy agent")

	if got.Code != exit.HumanOnly {
		t.Fatalf("exit %d, want %d\n%s", got.Code, exit.HumanOnly, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "vaultaffe tokens create") {
		t.Fatalf("the command was not named:\n%s", got.Stderr)
	}
}

// A listing an agent cannot read is not a revocation list, so listing is not
// human-only — and it carries no value.
func TestTheTokenListingCarriesNoValue(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/tokens", http.StatusOK, []map[string]any{{
		"id": "6f1f1a2e-0000-4000-8000-000000000004", "kind": "agent", "name": "the deploy agent",
		"scopes": []string{"names", "read"}, "bindings": []any{},
		"reachesTheWholeOrganization": true, "createdAt": "2026-01-01T00:00:00Z",
		"expiresAt": nil, "revokedAt": "2026-03-01T00:00:00Z",
	}})

	got := bound(newSession(t, in)).run(t, "tokens", "--json")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if strings.Contains(got.Stdout, `"value"`) {
		t.Fatalf("a value is in the listing:\n%s", got.Stdout)
	}
}

// A session comes from signing in and is not something this creates.
func TestOnlyTwoKindsAreCreatable(t *testing.T) {
	in := startInstanceStub(t)

	got := bound(newSession(t, in)).run(t, "tokens", "create", "mine", "--kind", "session")

	if got.Code != exit.Usage || !strings.Contains(got.Stderr, "signing in") {
		t.Fatalf("exit %d:\n%s", got.Code, got.Stderr)
	}
}

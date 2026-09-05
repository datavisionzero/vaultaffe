package cmd

import (
	"encoding/json"
	"net/http"
	"strings"
	"testing"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/exit"
)

const aMultiLineValue = "-----BEGIN NOT A KEY-----\na fixture, and what matters about it is that it has lines\n-----END NOT A KEY-----\n"

func aSecret(name, status string) map[string]any {
	written := any("2026-01-01T00:00:00Z")
	if status == "empty" {
		written = nil
	}
	return map[string]any{
		"id": "6f1f1a2e-0000-4000-8000-00000000000a", "name": name, "status": status,
		"createdAt": "2026-01-01T00:00:00Z", "valueWrittenAt": written, "deletedAt": nil,
	}
}

// The listing is the normal case for an agent, and it is a different endpoint
// and a different scope from reading a value. Nothing in it is a value — not in
// the table, and not in the JSON either.
func TestTheListingCarriesNamesAndStatusAndNoValue(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/projects/billing/environments/dev/secrets", http.StatusOK, []map[string]any{
		aSecret("DATABASE_URL", "set"), aSecret("SMTP_PASSWORD", "empty"),
	})

	s := bound(newSession(t, in))
	for _, args := range [][]string{{"secrets"}, {"secrets", "list"}, {"secrets", "--json"}} {
		got := s.run(t, args...)
		if got.Code != exit.OK {
			t.Fatalf("%v: exit %d\n%s", args, got.Code, got.Stderr)
		}
		if !strings.Contains(got.Stdout, "DATABASE_URL") || !strings.Contains(got.Stdout, "empty") {
			t.Fatalf("%v printed:\n%s", args, got.Stdout)
		}
		// `valueWrittenAt` says when and not what; a member called `value` is
		// the one thing this endpoint must never grow.
		if strings.Contains(got.Stdout, `"value"`) {
			t.Fatalf("%v carries a value member:\n%s", args, got.Stdout)
		}
	}
}

// One key, named. Exactly one trailing newline is added so the output reads like
// every other command's; --raw prints the bytes as they are stored.
func TestGetPrintsOneValueAndRawPrintsTheBytes(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/projects/billing/environments/dev/secrets/DATABASE_URL", http.StatusOK, map[string]any{
		"secret": aSecret("DATABASE_URL", "set"), "value": aValue,
	})

	s := bound(newSession(t, in))

	if got := s.run(t, "secrets", "get", "DATABASE_URL"); got.Stdout != aValue+"\n" {
		t.Fatalf("printed %q", got.Stdout)
	}
	if got := s.run(t, "secrets", "get", "DATABASE_URL", "--raw"); got.Stdout != aValue {
		t.Fatalf("--raw printed %q", got.Stdout)
	}
}

// A placeholder is not a value, and asking for one is not an error of the API:
// a person still has to do something, and it is the same outcome `run` stops on.
func TestGettingAPlaceholderSaysAPersonHasToFillIt(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/projects/billing/environments/dev/secrets/SMTP_PASSWORD", http.StatusOK, map[string]any{
		"secret": aSecret("SMTP_PASSWORD", "empty"), "value": nil,
	})

	got := bound(newSession(t, in)).run(t, "secrets", "get", "SMTP_PASSWORD")

	if got.Code != exit.Placeholder {
		t.Fatalf("exit %d, want %d\n%s", got.Code, exit.Placeholder, got.Stderr)
	}
	if got.Stdout != "" {
		t.Fatalf("something was printed anyway: %q", got.Stdout)
	}
}

// The value arrives on stdin and nowhere else, exactly one trailing newline is
// removed, and the confirmation does not give it back.
func TestSetTakesStdinStripsOneNewlineAndEchoesNothing(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("PUT /api/v1/projects/billing/environments/dev/secrets/STRIPE_KEY", http.StatusOK, aSecret("STRIPE_KEY", "set"))

	s := bound(newSession(t, in))
	s.stdin = "not-a-stripe-key-just-a-fixture\n"
	got := s.run(t, "secrets", "set", "STRIPE_KEY")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	var sent map[string]any
	if err := json.Unmarshal(in.sent("PUT", "/api/v1/projects/billing/environments/dev/secrets/STRIPE_KEY").Body, &sent); err != nil {
		t.Fatal(err)
	}
	if sent["value"] != "not-a-stripe-key-just-a-fixture" {
		t.Fatalf("the value arrived as %q", sent["value"])
	}
	if got.mentions("not-a-stripe-key-just-a-fixture") {
		t.Fatalf("the confirmation gave the value back:\n%s%s", got.Stdout, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "STRIPE_KEY") {
		t.Fatalf("nothing was confirmed:\n%s", got.Stderr)
	}
}

// A PEM key is several lines and two of them are blank on purpose. Exactly one
// byte comes off, and --raw takes none.
func TestAMultiLineValuePassesThroughIntact(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("PUT /api/v1/projects/billing/environments/dev/secrets/TLS_KEY", http.StatusOK, aSecret("TLS_KEY", "set"))

	s := bound(newSession(t, in))
	s.stdin = aMultiLineValue
	s.run(t, "secrets", "set", "TLS_KEY")

	var sent map[string]any
	if err := json.Unmarshal(in.sent("PUT", "/api/v1/projects/billing/environments/dev/secrets/TLS_KEY").Body, &sent); err != nil {
		t.Fatal(err)
	}
	if sent["value"] != strings.TrimSuffix(aMultiLineValue, "\n") {
		t.Fatalf("the key arrived as %q", sent["value"])
	}

	in.Requests = nil
	s.stdin = aMultiLineValue
	s.run(t, "secrets", "set", "TLS_KEY", "--raw")
	if err := json.Unmarshal(in.sent("PUT", "/api/v1/projects/billing/environments/dev/secrets/TLS_KEY").Body, &sent); err != nil {
		t.Fatal(err)
	}
	if sent["value"] != aMultiLineValue {
		t.Fatalf("--raw changed the bytes: %q", sent["value"])
	}
}

// The unsafe form is not offered at all, and somebody who types it anyway gets a
// sentence rather than a key with an odd name (Specification §6.2).
func TestAValueAsAnArgumentIsRefusedWithTheReason(t *testing.T) {
	in := startInstanceStub(t)

	got := bound(newSession(t, in)).run(t, "secrets", "set", "STRIPE_KEY=not-a-stripe-key-just-a-fixture")

	if got.Code != exit.Usage {
		t.Fatalf("exit %d, want %d\n%s", got.Code, exit.Usage, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "stdin") {
		t.Fatalf("the reason was not given:\n%s", got.Stderr)
	}
	for _, request := range in.Requests {
		if request.Method == http.MethodPut {
			t.Fatal("it was sent to the instance anyway")
		}
	}
}

// Nothing on stdin is a mistake with two ways out, and neither of them is an
// empty string standing in for a placeholder.
func TestSetWithNothingOnStdinNamesTheEmptyFlag(t *testing.T) {
	in := startInstanceStub(t)

	got := bound(newSession(t, in)).run(t, "secrets", "set", "STRIPE_KEY")

	if got.Code != exit.Usage || !strings.Contains(got.Stderr, "--empty") {
		t.Fatalf("exit %d:\n%s", got.Code, got.Stderr)
	}
}

// A placeholder is asked for by name and reads no stdin at all.
func TestEmptyMakesAPlaceholderAndSendsNoValue(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("PUT /api/v1/projects/billing/environments/dev/secrets/SMTP_PASSWORD", http.StatusOK, aSecret("SMTP_PASSWORD", "empty"))

	s := bound(newSession(t, in))
	s.stdin = "this should be ignored"
	got := s.run(t, "secrets", "set", "SMTP_PASSWORD", "--empty")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	var sent map[string]any
	if err := json.Unmarshal(in.sent("PUT", "/api/v1/projects/billing/environments/dev/secrets/SMTP_PASSWORD").Body, &sent); err != nil {
		t.Fatal(err)
	}
	if _, carried := sent["value"]; carried {
		t.Fatalf("a value was sent for a placeholder: %v", sent)
	}
}

// Overwriting is as destructive as deleting. The instance says the request did
// not ask to; this CLI says which flag asks.
func TestOverwritingWithoutTheFlagNamesTheFlag(t *testing.T) {
	in := startInstanceStub(t)
	in.refuse("PUT /api/v1/projects/billing/environments/dev/secrets/STRIPE_KEY", http.StatusConflict,
		"replace-required", "That key holds a value and the request did not say to overwrite it.",
		map[string]any{"secretName": "STRIPE_KEY"})

	s := bound(newSession(t, in))
	s.stdin = "not-a-stripe-key-either\n"
	got := s.run(t, "secrets", "set", "STRIPE_KEY")

	if got.Code != exit.Conflict {
		t.Fatalf("exit %d, want %d\n%s", got.Code, exit.Conflict, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "--replace") {
		t.Fatalf("the flag was not named:\n%s", got.Stderr)
	}
}

// The file arrives on stdin so that an agent can migrate one it never displays,
// and the answer names keys and no values.
func TestImportReadsStdinAndReportsNamesOnly(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("POST /api/v1/projects/billing/environments/dev/import", http.StatusOK, map[string]any{
		"created": []string{"DATABASE_URL"}, "filled": []string{}, "replaced": []string{},
		"unchanged": []string{}, "skipped": []map[string]any{{"name": "STRIPE_KEY", "reason": "holds a value"}},
		"unreadable": []map[string]any{{"line": 7, "reason": "no ="}},
	})

	s := bound(newSession(t, in))
	s.stdin = "DATABASE_URL=" + aValue + "\nSTRIPE_KEY=not-a-stripe-key-just-a-fixture\n"
	got := s.run(t, "secrets", "import")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	var sent map[string]any
	if err := json.Unmarshal(in.sent("POST", "/api/v1/projects/billing/environments/dev/import").Body, &sent); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(sent["content"].(string), "DATABASE_URL=") {
		t.Fatalf("the file did not arrive whole: %q", sent["content"])
	}
	if !strings.Contains(got.Stdout, "DATABASE_URL") || !strings.Contains(got.Stdout, "holds a value") {
		t.Fatalf("the report says too little:\n%s", got.Stdout)
	}
	if got.mentions(aValue) || got.mentions("not-a-stripe-key-just-a-fixture") {
		t.Fatalf("a value was reported back:\n%s%s", got.Stdout, got.Stderr)
	}
}

// Export writes every value in plaintext, which is why it is a person's alone.
// The refusal names the command this CLI has for it and nothing it does not.
func TestExportRefusedToATokenNamesThisCLIsOwnCommand(t *testing.T) {
	in := startInstanceStub(t)
	in.refuse("GET /api/v1/projects/billing/environments/dev/export", http.StatusForbidden, "human-only",
		"Exporting an environment is reserved for a person: the output is every value of it at once.",
		map[string]any{"humanAction": "export"})

	got := bound(newSession(t, in)).run(t, "secrets", "export")

	if got.Code != exit.HumanOnly {
		t.Fatalf("exit %d, want %d\n%s", got.Code, exit.HumanOnly, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "vaultaffe secrets export") {
		t.Fatalf("the command a person runs was not named:\n%s", got.Stderr)
	}
}

func TestExportWritesWhatTheInstanceWrote(t *testing.T) {
	in := startInstanceStub(t)
	in.route("GET /api/v1/projects/billing/environments/dev/export", func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "text/plain")
		_, _ = w.Write([]byte("DATABASE_URL=\"" + aValue + "\"\n"))
	})

	got := bound(newSession(t, in)).run(t, "secrets", "export")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if !strings.Contains(got.Stdout, aValue) {
		t.Fatalf("the export did not arrive:\n%s", got.Stdout)
	}
}

func TestOnlyOneFormatIsOffered(t *testing.T) {
	in := startInstanceStub(t)

	got := bound(newSession(t, in)).run(t, "secrets", "export", "--format", "json")

	if got.Code != exit.Usage {
		t.Fatalf("exit %d, want %d\n%s", got.Code, exit.Usage, got.Stderr)
	}
}

// The version listing says when and never what: five old credentials in one
// answer would be the bulk disclosure everything else here avoids.
func TestTheVersionListingSaysWhenAndNeverWhat(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/projects/billing/environments/dev/secrets/STRIPE_KEY/versions", http.StatusOK, []map[string]any{
		{"id": "6f1f1a2e-0000-4000-8000-00000000000c", "writtenAt": "2026-01-01T00:00:00Z",
			"replacedAt": "2026-02-01T00:00:00Z", "expiresAt": "2026-02-04T00:00:00Z"},
	})

	got := bound(newSession(t, in)).run(t, "secrets", "versions", "STRIPE_KEY")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if !strings.Contains(got.Stdout, "2026-01-01") {
		t.Fatalf("the listing says nothing:\n%s", got.Stdout)
	}
	if strings.Contains(got.Stdout, `"value"`) || strings.Contains(got.Stdout, aValue) {
		t.Fatalf("the listing carries a value:\n%s", got.Stdout)
	}
}

// An agent that wrecked a value overnight is exactly who needs an undo button,
// and putting one back reads nothing.
func TestRollbackIsAnOrdinaryWriteAndReadsNothing(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("POST /api/v1/projects/billing/environments/dev/secrets/STRIPE_KEY/rollback", http.StatusOK, aSecret("STRIPE_KEY", "set"))

	got := bound(newSession(t, in)).run(t, "secrets", "rollback", "STRIPE_KEY")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "STRIPE_KEY") {
		t.Fatalf("nothing was confirmed:\n%s", got.Stderr)
	}
	if got.Stdout != "" {
		t.Fatalf("a rollback printed something:\n%s", got.Stdout)
	}
}

// What the log exists for: not who acted, but what kind of thing did.
func TestTheChangeLogNamesTheIdentityAndItsType(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/changes", http.StatusOK, map[string]any{
		"entries": []map[string]any{{
			"id": "6f1f1a2e-0000-4000-8000-00000000000d", "occurredAt": "2026-02-01T09:00:00Z",
			"action":   "value-set",
			"identity": map[string]any{"id": "6f1f1a2e-0000-4000-8000-00000000000e", "type": "agent-token", "name": "the deploy agent"},
			"project":  "billing", "environment": "dev", "secret": "STRIPE_KEY",
		}},
		"total": 1,
	})

	got := bound(newSession(t, in)).run(t, "changes")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	for _, wanted := range []string{"value-set", "billing/dev/STRIPE_KEY", "the deploy agent", "agent-token"} {
		if !strings.Contains(got.Stdout, wanted) {
			t.Fatalf("%q is not in the log:\n%s", wanted, got.Stdout)
		}
	}
}

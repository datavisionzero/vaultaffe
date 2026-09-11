package cmd

import (
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/config"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/exit"
)

const (
	heldValue    = "vaultaffe_agent_theoneitisholding"
	renewedValue = "vaultaffe_agent_theonethatreplacesit"
)

// renewing stands an instance in front of the flow: who this token belongs to,
// and the successor it is answered when it asks.
func renewing(t *testing.T, in *instance, kind string) {
	t.Helper()

	in.answer("GET /api/v1/me", http.StatusOK, map[string]any{
		"organizationId": "6f1f1a2e-0000-4000-8000-000000000001",
		"userId":         "6f1f1a2e-0000-4000-8000-000000000002",
		"name":           "A Maintainer", "email": "maintainer@example.com",
		"isAdministrator": false,
		"tokenId":         deployAgentID, "tokenName": "claude in ~/webshop", "tokenKind": kind,
		"scopes": []string{"names", "read", "write", "delete"},
	})
	in.answer("POST /api/v1/tokens/"+deployAgentID+"/rotate", http.StatusOK, map[string]any{
		"token": map[string]any{
			"id": ciServiceID, "kind": "agent", "name": "claude in ~/webshop",
			"scopes":   []string{"names", "read", "write", "delete"},
			"bindings": []any{}, "reachesTheWholeOrganization": true,
			"createdAt": "2026-05-01T00:00:00Z", "expiresAt": nil, "revokedAt": nil,
		},
		"value": renewedValue,
	})
}

// holding is an agent as its harness starts one: the token in the environment,
// which is how an agent token reaches an agent and the only way it does
// (Specification §6.4).
func holding(s *session) *session {
	s.env[config.EnvToken] = heldValue
	return s
}

// The property this command shares with `enroll`, and the reason it exists at
// all rather than the agent being told to run `tokens rotate`: the value
// reaches a file and nothing else. A value that has been printed has been in a
// terminal, in a scrollback and — this command being one an agent runs itself —
// in a transcript.
func TestRenewingNeverPrintsTheValueAndWritesItWhereOnlyYouCanRead(t *testing.T) {
	in := startInstanceStub(t)
	renewing(t, in, "agent")

	s := holding(newSession(t, in))
	got := s.run(t, "renew")
	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}

	if got.mentions(renewedValue) {
		t.Fatalf("the value reached the terminal:\n%s\n%s", got.Stdout, got.Stderr)
	}

	path := filepath.Join(s.dir, "config", "agents", "claude-in-webshop.token")

	written, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("the value did not reach %s: %v", path, err)
	}
	if strings.TrimSpace(string(written)) != renewedValue {
		t.Fatalf("%s holds something else: %q", path, string(written))
	}

	info, err := os.Stat(path)
	if err != nil {
		t.Fatal(err)
	}
	if mode := info.Mode().Perm(); mode&0o077 != 0 {
		t.Fatalf("the token file is mode %04o", mode)
	}

	// The value it replaced came out of the environment, and no process can
	// change the environment of the one that started it. Saying so is the
	// difference between a machine that renews itself and one that has quietly
	// stopped working.
	if !strings.Contains(got.Stderr, config.EnvToken) || !strings.Contains(got.Stderr, path) {
		t.Fatalf("it did not say where the new value is and why the old place is stale:\n%s", got.Stderr)
	}
}

// Where the token came from a file, the successor goes back into that file: the
// next invocation reads the same path and finds the value that works, with
// nothing for anybody to carry across.
func TestRenewingWritesBackToTheFileTheTokenCameFrom(t *testing.T) {
	in := startInstanceStub(t)
	renewing(t, in, "agent")

	s := newSession(t, in)
	path := filepath.Join(s.dir, "the.token")

	if err := config.WriteTokenFile(path, heldValue); err != nil {
		t.Fatal(err)
	}
	if err := config.Save(s.env["VAULTAFFE_CONFIG"], config.File{
		Instance: in.URL, TokenFile: path,
	}); err != nil {
		t.Fatal(err)
	}

	got := s.run(t, "renew")
	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if got.mentions(renewedValue) {
		t.Fatalf("the value reached the terminal:\n%s\n%s", got.Stdout, got.Stderr)
	}

	written, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	if strings.TrimSpace(string(written)) != renewedValue {
		t.Fatalf("%s was not written back: %q", path, string(written))
	}
}

// A session is not renewed this way. Signing in again is how a person gets
// another one, and the sentence says that rather than letting the instance
// refuse an id nobody typed.
func TestASessionIsNotRenewed(t *testing.T) {
	in := startInstanceStub(t)
	renewing(t, in, "session")

	got := bound(newSession(t, in)).run(t, "renew")

	if got.Code != exit.Usage {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "login") {
		t.Fatalf("it did not say what to do instead:\n%s", got.Stderr)
	}
	for _, seen := range in.Requests {
		if seen.Method == "POST" {
			t.Fatalf("it asked the instance to rotate a session: %s %s", seen.Method, seen.Path)
		}
	}
}

// The path is worked out from a name, and two tokens may be called the same
// thing. Writing over a file holding some other credential is the failure this
// command exists to avoid, in miniature — so it is refused before anything is
// rotated, while the token in use still works.
func TestRenewingRefusesToWriteOverAnotherTokensFile(t *testing.T) {
	in := startInstanceStub(t)
	renewing(t, in, "agent")

	s := holding(newSession(t, in))
	path := filepath.Join(s.dir, "config", "agents", "claude-in-webshop.token")

	if err := os.MkdirAll(filepath.Dir(path), 0o700); err != nil {
		t.Fatal(err)
	}
	if err := config.WriteTokenFile(path, "vaultaffe_agent_somebodyelses"); err != nil {
		t.Fatal(err)
	}

	got := s.run(t, "renew")

	if got.Code != exit.Usage {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "--token-file") {
		t.Fatalf("it did not say how to say where the value goes:\n%s", got.Stderr)
	}
	for _, seen := range in.Requests {
		if seen.Method == "POST" {
			t.Fatalf("it rotated before it knew where the answer would go: %s %s", seen.Method, seen.Path)
		}
	}

	held, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	if strings.TrimSpace(string(held)) != "vaultaffe_agent_somebodyelses" {
		t.Fatalf("the other token's file was written over: %q", string(held))
	}
}

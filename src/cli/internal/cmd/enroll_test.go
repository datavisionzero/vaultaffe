package cmd

import (
	"encoding/json"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/config"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/exit"
)

const enrolledValue = "vaultaffe_agent_thisistheonlycopyofit"

// enrolling stands an instance in front of the whole flow: the ask, and a poll
// that answers as though a person had already agreed.
func enrolling(t *testing.T, in *instance) {
	t.Helper()

	in.answer("POST /api/v1/enrollments", http.StatusOK, map[string]any{
		"deviceCode":              "the-long-one",
		"userCode":                "BCDF-GHJK",
		"verificationUri":         "/enroll",
		"verificationUriComplete": "/enroll?code=BCDF-GHJK",
		"expiresInSeconds":        600,
		"intervalSeconds":         5,
	})
	in.answer("POST /api/v1/enrollments/tokens", http.StatusOK, map[string]any{
		"token": map[string]any{
			"id": deployAgentID, "kind": "agent", "name": "claude in ~/webshop",
			"scopes":   []string{"names", "read"},
			"bindings": []any{}, "reachesTheWholeOrganization": true,
			"createdAt": "2026-03-01T00:00:00Z", "expiresAt": nil, "revokedAt": nil,
		},
		"value": enrolledValue,
	})
}

// The property the whole command exists for: the value reaches a file and
// nothing else. A token that has been printed has been in a terminal, in a
// scrollback and — where an agent ran the command — in a transcript.
func TestEnrollingNeverPrintsTheValueAndWritesItWhereOnlyYouCanRead(t *testing.T) {
	in := startInstanceStub(t)
	enrolling(t, in)

	s := newSession(t, in)
	got := s.run(t, "enroll", "claude in ~/webshop")
	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}

	if got.mentions(enrolledValue) {
		t.Fatalf("the value reached the terminal:\n%s\n%s", got.Stdout, got.Stderr)
	}

	path := filepath.Join(s.dir, "config", "agents", "claude-in-webshop.token")

	written, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("the value did not reach %s: %v", path, err)
	}
	if strings.TrimSpace(string(written)) != enrolledValue {
		t.Fatalf("%s holds something else: %q", path, string(written))
	}

	info, err := os.Stat(path)
	if err != nil {
		t.Fatal(err)
	}
	if mode := info.Mode().Perm(); mode != 0o600 {
		t.Fatalf("%s is mode %04o, not 0600: a token in a file is protected by nothing else", path, mode)
	}

	// And the person is told the two things they need: where it went, and the
	// one line that hands it to what they are about to start.
	for _, said := range []string{path, config.EnvToken, "was not printed"} {
		if !strings.Contains(got.Stderr, said) {
			t.Fatalf("it does not say %q:\n%s", said, got.Stderr)
		}
	}
}

// The code and the address are what a person needs, and they are on stderr with
// everything else a person reads.
func TestEnrollingPrintsTheCodeAndWhereToDecideIt(t *testing.T) {
	in := startInstanceStub(t)
	enrolling(t, in)

	got := newSession(t, in).run(t, "enroll", "claude in ~/webshop")
	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}

	if !strings.Contains(got.Stderr, "BCDF-GHJK") {
		t.Fatalf("the code was not printed:\n%s", got.Stderr)
	}
	if !strings.Contains(got.Stderr, in.URL+"/enroll") {
		t.Fatalf("the address a person opens was not printed whole:\n%s", got.Stderr)
	}

	// The name the person will read is what reached the instance.
	var asked map[string]any
	if err := json.Unmarshal(in.sent("POST", "/api/v1/enrollments").Body, &asked); err != nil {
		t.Fatal(err)
	}
	if asked["name"] != "claude in ~/webshop" {
		t.Fatalf("the instance was told something else: %v", asked)
	}
}

// The mistake this must not make, in the other direction from the one
// `docs/agents.md` warns about: an agent's token on this machine's own token
// ladder would make every command a person runs here act as the agent.
func TestEnrollingLeavesThisMachinesOwnConfigurationAlone(t *testing.T) {
	in := startInstanceStub(t)
	enrolling(t, in)

	s := newSession(t, in)
	s.keychain[in.URL] = aSession

	got := s.run(t, "enroll", "claude in ~/webshop")
	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}

	if got.Config.TokenFile != "" {
		t.Fatalf("the agent's token was put on this machine's ladder: %q", got.Config.TokenFile)
	}
	if got.Keychain[in.URL] != aSession {
		t.Fatalf("the person's own session was touched: %q", got.Keychain[in.URL])
	}
}

func TestEnrollingWritesWhereItWasTold(t *testing.T) {
	in := startInstanceStub(t)
	enrolling(t, in)

	s := newSession(t, in)
	path := filepath.Join(s.dir, "somewhere", "else.token")

	got := s.run(t, "enroll", "an agent", "--token-file", path)
	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}

	written, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("the value did not reach %s: %v", path, err)
	}
	if strings.TrimSpace(string(written)) != enrolledValue {
		t.Fatalf("%s holds something else", path)
	}
}

// --json is for whatever reads this command's answer, and it is the same answer
// minus the one thing the command exists to keep out of a terminal.
func TestEnrollingInJSONStillDoesNotPrintTheValue(t *testing.T) {
	in := startInstanceStub(t)
	enrolling(t, in)

	got := newSession(t, in).run(t, "enroll", "an agent", "--json")
	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if got.mentions(enrolledValue) {
		t.Fatalf("the value reached the terminal:\n%s", got.Stdout)
	}

	var answer map[string]any
	if err := json.Unmarshal([]byte(got.Stdout), &answer); err != nil {
		t.Fatalf("stdout is not the answer: %v\n%s", err, got.Stdout)
	}
	if answer["tokenFile"] == nil || answer["token"] == nil {
		t.Fatalf("the answer says neither where it went nor what it is: %v", answer)
	}
}

// A person who never decides is not a failure of this CLI, and the exit code
// says which of the four it was.
func TestEnrollingStopsWhenAPersonRefuses(t *testing.T) {
	in := startInstanceStub(t)
	enrolling(t, in)
	in.refuse("POST /api/v1/enrollments/tokens", http.StatusBadRequest,
		"device-denied", "A person refused this enrollment.")

	got := newSession(t, in).run(t, "enroll", "an agent")
	if got.Code == exit.OK {
		t.Fatalf("it carried on after a refusal:\n%s", got.Stderr)
	}
	if !strings.Contains(got.Stderr, "refused") {
		t.Fatalf("it does not say what happened:\n%s", got.Stderr)
	}
}

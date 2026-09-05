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

const aValue = "postgres://user:s3cr3t@db/billing"

// anEnvironment gives the instance a listing and one value per key, the way the
// API splits them: names and status are one endpoint and one scope, a value is
// a second request for one key at a time (docs/api.md).
func anEnvironment(in *instance, secrets map[string]string, placeholders ...string) {
	listing := make([]map[string]any, 0, len(secrets)+len(placeholders))
	for name, value := range secrets {
		listing = append(listing, map[string]any{
			"id": "6f1f1a2e-0000-4000-8000-00000000000a", "name": name, "status": "set",
			"createdAt": "2026-01-01T00:00:00Z", "valueWrittenAt": "2026-01-01T00:00:00Z", "deletedAt": nil,
		})
		in.answer("GET /api/v1/projects/billing/environments/dev/secrets/"+name, http.StatusOK, map[string]any{
			"secret": map[string]any{
				"id": "6f1f1a2e-0000-4000-8000-00000000000a", "name": name, "status": "set",
				"createdAt": "2026-01-01T00:00:00Z", "valueWrittenAt": "2026-01-01T00:00:00Z", "deletedAt": nil,
			},
			"value": value,
		})
	}
	for _, name := range placeholders {
		listing = append(listing, map[string]any{
			"id": "6f1f1a2e-0000-4000-8000-00000000000b", "name": name, "status": "empty",
			"createdAt": "2026-01-01T00:00:00Z", "valueWrittenAt": nil, "deletedAt": nil,
		})
	}
	in.answer("GET /api/v1/projects/billing/environments/dev/secrets", http.StatusOK, listing)
}

func bound(s *session) *session {
	s.keychain[s.env[config.EnvURL]] = aSession
	s.env[config.EnvProject] = "billing"
	s.env[config.EnvEnvironment] = "dev"
	return s
}

// Values reach the process as environment variables. No temporary file, no
// plaintext on disk (Specification §6.2) — and nothing of the value in either
// stream of the terminal that started it.
func TestRunPutsValuesInTheProcessAndNotInTheTerminal(t *testing.T) {
	in := startInstanceStub(t)
	anEnvironment(in, map[string]string{"DATABASE_URL": aValue})

	s := bound(newSession(t, in))
	s.environ = []string{"PATH=/usr/bin:/bin", "HOME=/home/somebody"}

	got := s.run(t, "run", "--", "true")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if got.Exec == nil {
		t.Fatal("nothing was started")
	}
	if value, found := got.Exec.Value("DATABASE_URL"); !found || value != aValue {
		t.Fatalf("the process would have found %q (%v)", value, found)
	}
	if got.mentions(aValue) {
		t.Fatalf("a value reached the terminal:\n%s%s", got.Stdout, got.Stderr)
	}
	if got.Exec.Argv[0] != "true" {
		t.Fatalf("the command was %v", got.Exec.Argv)
	}
}

// The token an agent runs under must not reach what it starts, and after
// exec() there is no second chance (ADR 0009). Everything with the prefix goes,
// not only the token.
func TestNothingOfThisCLIsOwnEnvironmentReachesTheChild(t *testing.T) {
	in := startInstanceStub(t)
	anEnvironment(in, map[string]string{"DATABASE_URL": aValue})

	s := bound(newSession(t, in))
	s.environ = []string{
		"PATH=/usr/bin:/bin",
		"VAULTAFFE_TOKEN=vaultaffe_agent_thetokenitself",
		"VAULTAFFE_URL=" + in.URL,
		"VAULTAFFE_PROJECT=billing",
	}

	got := s.run(t, "run", "--", "true")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	for _, name := range []string{"VAULTAFFE_TOKEN", "VAULTAFFE_URL", "VAULTAFFE_PROJECT"} {
		if _, found := got.Exec.Value(name); found {
			t.Fatalf("%s reached the child process", name)
		}
	}
	if got.mentions("vaultaffe_agent_thetokenitself") {
		t.Fatalf("the token was printed:\n%s%s", got.Stdout, got.Stderr)
	}
}

// A value from the store beats what was already there, and every collision is
// said out loud: a collision usually means machine-specific configuration has
// leaked into the store (Specification §5).
func TestAValueFromTheStoreWinsAndTheCollisionIsReported(t *testing.T) {
	in := startInstanceStub(t)
	anEnvironment(in, map[string]string{"DATABASE_URL": aValue})

	s := bound(newSession(t, in))
	s.environ = []string{"PATH=/usr/bin:/bin", "DATABASE_URL=postgres://localhost/scratch"}

	got := s.run(t, "run", "--", "true")

	if value, _ := got.Exec.Value("DATABASE_URL"); value != aValue {
		t.Fatalf("the process would have found %q", value)
	}
	if !strings.Contains(got.Stderr, "DATABASE_URL") {
		t.Fatalf("the collision was not reported:\n%s", got.Stderr)
	}
	if got.mentions("postgres://localhost/scratch") || got.mentions(aValue) {
		t.Fatalf("a value was printed with the collision:\n%s%s", got.Stdout, got.Stderr)
	}
}

// The protected list is fixed and documented, and a write token must not be
// able to inject code into every `run` on the machine through it.
func TestTheProtectedListIsNeverOverwritten(t *testing.T) {
	in := startInstanceStub(t)
	anEnvironment(in, map[string]string{
		"PATH":                  "/tmp/evil",
		"LD_PRELOAD":            "/tmp/evil.so",
		"DYLD_INSERT_LIBRARIES": "/tmp/evil.dylib",
		"HOME":                  "/tmp/elsewhere",
		"DATABASE_URL":          aValue,
	})

	s := bound(newSession(t, in))
	s.environ = []string{"PATH=/usr/bin:/bin", "HOME=/home/somebody"}

	got := s.run(t, "run", "--", "true")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if value, _ := got.Exec.Value("PATH"); value != "/usr/bin:/bin" {
		t.Fatalf("PATH became %q", value)
	}
	if value, _ := got.Exec.Value("HOME"); value != "/home/somebody" {
		t.Fatalf("HOME became %q", value)
	}
	for _, name := range []string{"LD_PRELOAD", "DYLD_INSERT_LIBRARIES"} {
		if _, found := got.Exec.Value(name); found {
			t.Fatalf("%s was injected into the child", name)
		}
	}
	if value, _ := got.Exec.Value("DATABASE_URL"); value != aValue {
		t.Fatal("the ordinary key was dropped along with the protected ones")
	}
	if !strings.Contains(got.Stderr, "PATH") {
		t.Fatalf("keeping the existing PATH was not said:\n%s", got.Stderr)
	}
}

// An empty placeholder means a person still has to do something, and silently
// injecting an empty string would hide exactly that. It has an exit code of its
// own, because it is not a failure of the API.
func TestAnEmptyPlaceholderStopsRunAndIsNamed(t *testing.T) {
	in := startInstanceStub(t)
	anEnvironment(in, map[string]string{"DATABASE_URL": aValue}, "SMTP_PASSWORD")

	s := bound(newSession(t, in))
	got := s.run(t, "run", "--", "true")

	if got.Code != exit.Placeholder {
		t.Fatalf("exit %d, want %d\n%s", got.Code, exit.Placeholder, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "SMTP_PASSWORD") || !strings.Contains(got.Stderr, "--allow-empty") {
		t.Fatalf("the key or the way past it was not named:\n%s", got.Stderr)
	}
	if got.Exec != nil {
		t.Fatal("something was started anyway")
	}
	// Nothing is read for a process that is not going to start.
	for _, request := range in.Requests {
		if strings.Contains(request.Path, "/secrets/") {
			t.Fatalf("a value was fetched anyway: %s", request.Path)
		}
	}
}

func TestAllowEmptyStartsAnywayAndSaysWhichWereEmpty(t *testing.T) {
	in := startInstanceStub(t)
	anEnvironment(in, map[string]string{"DATABASE_URL": aValue}, "SMTP_PASSWORD")

	s := bound(newSession(t, in))
	got := s.run(t, "run", "--allow-empty", "--", "true")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	value, found := got.Exec.Value("SMTP_PASSWORD")
	if !found || value != "" {
		t.Fatalf("the placeholder arrived as %q (%v)", value, found)
	}
	if !strings.Contains(got.Stderr, "SMTP_PASSWORD") {
		t.Fatalf("it did not say what was empty:\n%s", got.Stderr)
	}
}

// exec() takes a path and not a command, so `run` resolves it — and has to tell
// the two failures apart itself, the way a shell does (ADR 0009).
func TestTheTwoFailuresOfTheLookupAreTwoExitCodes(t *testing.T) {
	in := startInstanceStub(t)
	anEnvironment(in, map[string]string{"DATABASE_URL": aValue})

	notExecutable := filepath.Join(t.TempDir(), "unrunnable")
	if err := os.WriteFile(notExecutable, []byte("#!/bin/sh\n"), 0o600); err != nil {
		t.Fatal(err)
	}

	s := bound(newSession(t, in))
	if missing := s.run(t, "run", "--", "no-such-command-anywhere"); missing.Code != 127 {
		t.Fatalf("a missing command left with %d, want 127\n%s", missing.Code, missing.Stderr)
	}
	if unrunnable := s.run(t, "run", "--", notExecutable); unrunnable.Code != 126 {
		t.Fatalf("a file that is not executable left with %d, want 126\n%s", unrunnable.Code, unrunnable.Stderr)
	}
}

// The JSON is for a machine and carries no more than the sentences do: names
// and status, and never a value.
func TestTheJSONAccountCarriesNamesAndNoValues(t *testing.T) {
	in := startInstanceStub(t)
	anEnvironment(in, map[string]string{"DATABASE_URL": aValue}, "SMTP_PASSWORD")

	s := bound(newSession(t, in))
	s.environ = []string{"PATH=/usr/bin:/bin", "DATABASE_URL=postgres://localhost/scratch"}

	got := s.run(t, "run", "--json", "--allow-empty", "--", "true")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	var said map[string]any
	if err := json.Unmarshal([]byte(got.Stdout), &said); err != nil {
		t.Fatalf("the account is not JSON: %v\n%s", err, got.Stdout)
	}
	if strings.Contains(got.Stdout, "s3cr3t") || strings.Contains(got.Stdout, "scratch") {
		t.Fatalf("a value is in the account:\n%s", got.Stdout)
	}
	if said["project"] != "billing" || said["environment"] != "dev" {
		t.Fatalf("the account does not say where it read from: %v", said)
	}
}

// Flags stop at the command, so that the process's own flags are its own.
func TestTheProcessKeepsItsOwnFlags(t *testing.T) {
	in := startInstanceStub(t)
	anEnvironment(in, map[string]string{"DATABASE_URL": aValue})

	s := bound(newSession(t, in))
	got := s.run(t, "run", "--", "true", "--json", "--allow-empty")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if strings.Join(got.Exec.Argv, " ") != "true --json --allow-empty" {
		t.Fatalf("the command was %v", got.Exec.Argv)
	}
	if got.Stdout != "" {
		t.Fatalf("--json was taken as this CLI's own:\n%s", got.Stdout)
	}
}

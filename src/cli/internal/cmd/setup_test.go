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

func aCatalogue(in *instance) {
	in.answer("GET /api/v1/projects/billing", http.StatusOK, map[string]any{
		"id": "6f1f1a2e-0000-4000-8000-000000000002", "name": "billing",
		"createdAt": "2026-01-01T00:00:00Z", "deletedAt": nil,
		"environments": []map[string]any{
			{"id": "6f1f1a2e-0000-4000-8000-000000000003", "projectId": "6f1f1a2e-0000-4000-8000-000000000002",
				"name": "dev", "createdAt": "2026-01-01T00:00:00Z", "deletedAt": nil},
		},
	})
}

// The checked-in file does one thing: `setup` reads it and writes the entry. It
// binds the directory it is in, so that a monorepo is bound once at its root.
func TestSetupBindsFromTheProjectFileUpwards(t *testing.T) {
	in := startInstanceStub(t)
	aCatalogue(in)

	s := newSession(t, in)
	s.keychain[in.URL] = aSession
	root := s.dir
	deep := filepath.Join(root, "services", "billing")
	if err := os.MkdirAll(deep, 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(root, config.ProjectFileName), []byte("project = billing\nenvironment = dev\n"), 0o600); err != nil {
		t.Fatal(err)
	}
	s.dir = deep

	got := s.run(t, "setup")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if len(got.Config.Bindings) != 1 {
		t.Fatalf("the table holds %+v", got.Config.Bindings)
	}
	binding := got.Config.Bindings[0]
	if binding.Directory != root || binding.Project != "billing" || binding.Environment != "dev" {
		t.Fatalf("bound %+v, want the root of the repository", binding)
	}
}

// The file is not read at run time. A repository that could rebind a person's
// directories by being cloned could point a `run` at production.
func TestTheProjectFileIsNotABindingByItself(t *testing.T) {
	in := startInstanceStub(t)
	s := newSession(t, in)
	s.keychain[in.URL] = aSession
	if err := os.WriteFile(filepath.Join(s.dir, config.ProjectFileName), []byte("project = billing\nenvironment = prod\n"), 0o600); err != nil {
		t.Fatal(err)
	}

	got := s.run(t, "status")

	if !strings.Contains(got.Stdout+got.Stderr, "unbound") {
		t.Fatalf("the directory was taken to be bound by the file alone:\n%s%s", got.Stdout, got.Stderr)
	}
}

// A typo in a binding is otherwise found by the first command that uses it,
// which may be a `run` in front of somebody who is already waiting. It is a
// remark and not a condition: a local file is still written.
func TestSetupSaysWhenTheEnvironmentIsNotThereAndBindsAnyway(t *testing.T) {
	in := startInstanceStub(t)
	aCatalogue(in)

	s := newSession(t, in)
	s.keychain[in.URL] = aSession
	got := s.run(t, "setup", "--project", "billing", "--environment", "prud")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "prud") || !strings.Contains(got.Stderr, "dev") {
		t.Fatalf("the mistake was not shown against what is there:\n%s", got.Stderr)
	}
	if len(got.Config.Bindings) != 1 {
		t.Fatalf("nothing was bound: %+v", got.Config.Bindings)
	}
}

// A binding is a local file. A machine with no token yet, or an instance that is
// down, is no reason to refuse to write one — but it is a reason to say that
// nothing was checked.
func TestSetupBindsWithoutATokenAndSaysItCheckedNothing(t *testing.T) {
	in := startInstanceStub(t)
	s := newSession(t, in)

	got := s.run(t, "setup", "--project", "billing", "--environment", "dev")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "Not checked") {
		t.Fatalf("it did not say what it could not do:\n%s", got.Stderr)
	}
	if len(got.Config.Bindings) != 1 {
		t.Fatalf("nothing was bound: %+v", got.Config.Bindings)
	}
}

// Nothing to bind to is a usage error naming the command that binds, and never
// a command this CLI does not have.
func TestSetupWithNothingToBindToSaysWhatToPass(t *testing.T) {
	in := startInstanceStub(t)

	got := newSession(t, in).run(t, "setup")

	if got.Code != exit.Usage {
		t.Fatalf("exit %d, want %d\n%s", got.Code, exit.Usage, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "--project") {
		t.Fatalf("what to pass was not named:\n%s", got.Stderr)
	}
}

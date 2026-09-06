package cmd

import (
	"encoding/json"
	"net/http"
	"strings"
	"testing"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/exit"
)

const maintainerID = "6f1f1a2e-0000-4000-8000-000000000010"
const invitationID = "6f1f1a2e-0000-4000-8000-000000000011"

func people() []map[string]any {
	return []map[string]any{
		{
			"id": maintainerID, "email": "maintainer@example.test", "name": "Maintainer",
			"isAdministrator": true, "createdAt": "2026-01-01T00:00:00Z", "deactivatedAt": nil,
		},
	}
}

// Everything a person types in this CLI is a name, and an address is what a
// person knows somebody here by. The API takes an id; turning one into the
// other is the client's, exactly as it is for a token's binding.
func TestAPersonIsNamedByTheirAddressAndNotByAnID(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/users", http.StatusOK, people())
	in.answer("POST /api/v1/users/"+maintainerID+"/deactivate", http.StatusOK, people()[0])

	got := bound(newSession(t, in)).run(t, "users", "deactivate", "maintainer@example.test")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	in.sent("POST", "/api/v1/users/"+maintainerID+"/deactivate")
}

// An address nobody here has is a mistake at this end: the listing that would
// have answered it has already been read, so there is nothing to ask.
func TestAnAddressNobodyHereHasIsAUsageErrorAndNamesTheOnesThereAre(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/users", http.StatusOK, people())

	got := bound(newSession(t, in)).run(t, "users", "deactivate", "somebody@example.test")

	if got.Code != exit.Usage {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "maintainer@example.test") {
		t.Fatalf("it did not say who is here:\n%s", got.Stderr)
	}
}

// An address is a name and not a value, so both of them are arguments — and the
// person is still named by the address they sign in with today.
func TestAnAddressChangesByNamingTheOldOneAndTheNewOne(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/users", http.StatusOK, people())

	changed := people()[0]
	changed["email"] = "maintainer@elsewhere.test"
	in.answer("POST /api/v1/users/"+maintainerID+"/email", http.StatusOK, changed)

	got := bound(newSession(t, in)).run(t,
		"users", "email", "maintainer@example.test", "maintainer@elsewhere.test")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}

	var sent map[string]any
	if err := json.Unmarshal(in.sent("POST", "/api/v1/users/"+maintainerID+"/email").Body, &sent); err != nil {
		t.Fatal(err)
	}
	if sent["email"] != "maintainer@elsewhere.test" {
		t.Fatalf("the new address did not arrive: %v", sent)
	}

	// What a person needs to hear is that the sign-in moved and nothing else did.
	if !strings.Contains(got.Stderr, "maintainer@elsewhere.test") ||
		!strings.Contains(got.Stderr, "sessions are unchanged") {
		t.Fatalf("it did not say what changed:\n%s", got.Stderr)
	}
}

// A password is never an argument, for the reason every value in this CLI is
// never one: an argument is in the shell history and in `ps`.
func TestAPasswordResetTakesThePasswordFromStdin(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/users", http.StatusOK, people())
	in.answer("POST /api/v1/users/"+maintainerID+"/password", http.StatusOK, people()[0])

	s := bound(newSession(t, in))
	s.stdin = "a-password-of-real-length\n"
	got := s.run(t, "users", "password", "maintainer@example.test")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}

	var sent map[string]any
	if err := json.Unmarshal(in.sent("POST", "/api/v1/users/"+maintainerID+"/password").Body, &sent); err != nil {
		t.Fatal(err)
	}
	// One trailing newline off, as everywhere a value comes off a pipe.
	if sent["password"] != "a-password-of-real-length" {
		t.Fatalf("the password did not arrive as it was piped: %v", sent)
	}
	// And it is not in the terminal it was piped through.
	if got.mentions("a-password-of-real-length") {
		t.Fatalf("the password was printed:\n%s\n%s", got.Stdout, got.Stderr)
	}
}

// The link is a credential, so it goes to stdout on its own and everything a
// person reads goes to stderr — and the two halves of the address are joined
// here, because the instance does not know what address a browser reached it at.
func TestAnInvitationLinkIsWholeAndOnStdoutAlone(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("POST /api/v1/invitations", http.StatusOK, map[string]any{
		"invitation": map[string]any{
			"id": invitationID, "email": "somebody@example.test", "name": "Somebody",
			"isAdministrator": false, "state": "open", "invitedByUserId": maintainerID,
			"createdAt": "2026-01-01T00:00:00Z", "expiresAt": "2026-01-08T00:00:00Z",
		},
		"link": "/invite#anopeninvitationcode",
	})

	s := bound(newSession(t, in))
	got := s.run(t, "users", "invite", "somebody@example.test", "--name", "Somebody")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if strings.TrimSpace(got.Stdout) != s.env["VAULTAFFE_URL"]+"/invite#anopeninvitationcode" {
		t.Fatalf("the link was not the whole address, alone on stdout: %q", got.Stdout)
	}
}

// An agent that reaches an administrative action is told the action is a
// person's, and this CLI names the command it has for it (ADR 0010).
func TestAnAgentIsPointedAtTheCommandAPersonUses(t *testing.T) {
	in := startInstanceStub(t)
	in.refuse("GET /api/v1/users", http.StatusForbidden, "human-only",
		"Administering the organization and its users is reserved for a person.",
		map[string]any{"humanAction": "administer-organization"})

	got := bound(newSession(t, in)).run(t, "users")

	if got.Code != exit.HumanOnly {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "vaultaffe users") {
		t.Fatalf("it did not name the command a person uses:\n%s", got.Stderr)
	}
}

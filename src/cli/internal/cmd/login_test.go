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
	"github.com/datavisionzero/vaultaffe/src/cli/internal/keychain"
)

const aSession = "vaultaffe_session_0123456789abcdef"

// Relative, because that is what an instance actually answers: the two
// addresses are given relative to it and the CLI has the host already
// (`BeginDeviceLogin.VerificationPath`). This stub said `in.URL + "/device"`
// once, which is why nothing here noticed that a person was being told to open
// `/device`.
func aDeviceLogin(in *instance) {
	in.answer("POST /api/v1/device/authorizations", http.StatusOK, map[string]any{
		"deviceCode": "d-1", "userCode": "BCDF-GHJK",
		"verificationUri": "/device", "verificationUriComplete": "/device?code=BCDF-GHJK",
		"expiresInSeconds": 600, "intervalSeconds": 1,
	})
}

// What a person is told to open has to be openable. Both lines, because the
// second one is the one somebody clicks.
func TestALoginPrintsAnAddressAPersonCanOpen(t *testing.T) {
	in := startInstanceStub(t)
	aDeviceLogin(in)
	in.answer("POST /api/v1/device/tokens", http.StatusOK, map[string]any{
		"userId": "6f1f1a2e-0000-4000-8000-000000000001", "name": "A Maintainer",
		"email": "maintainer@example.com", "isAdministrator": true,
		"token": aSession, "expiresAt": "2026-12-31T00:00:00Z",
	})

	s := newSession(t, in)
	got := s.run(t, "login")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	said := got.Stdout + got.Stderr
	for _, want := range []string{in.URL + "/device", in.URL + "/device?code=BCDF-GHJK"} {
		if !strings.Contains(said, want) {
			t.Fatalf("it did not print %q:\n%s", want, said)
		}
	}
	// And not the bare path on its own, which is what the bug looked like.
	if strings.Contains(said, "Open /device") {
		t.Fatalf("it told a person to open a path with no host:\n%s", said)
	}
}

// The point of the whole flow: the session ends up in the keychain, and the
// value never appears in the terminal it was kept out of.
func TestALoginPutsTheSessionInTheKeychainAndPrintsNoValue(t *testing.T) {
	in := startInstanceStub(t)
	aDeviceLogin(in)

	pending := true
	in.route("POST /api/v1/device/tokens", func(w http.ResponseWriter, _ *http.Request) {
		if pending {
			pending = false
			refuse(w, http.StatusBadRequest, "device-pending", "Nobody has confirmed that login yet.")
			return
		}
		w.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(w).Encode(map[string]any{
			"userId": "6f1f1a2e-0000-4000-8000-000000000001", "name": "A Maintainer",
			"email": "maintainer@example.com", "isAdministrator": true,
			"token": aSession, "expiresAt": "2026-12-31T00:00:00Z",
		})
	})

	s := newSession(t, in)
	got := s.run(t, "login")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if got.Keychain[in.URL] != aSession {
		t.Fatalf("the keychain holds %q", got.Keychain[in.URL])
	}
	if got.Config.Instance != in.URL {
		t.Fatalf("the configuration points at %q", got.Config.Instance)
	}
	if got.mentions(aSession) {
		t.Fatalf("the session token was printed:\n%s%s", got.Stdout, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "BCDF-GHJK") {
		t.Fatalf("the user code was not shown:\n%s", got.Stderr)
	}
}

// Where there is no keychain, Doppler falls back to plaintext without a word.
// This is the one thing we do differently: nothing is written, and the two ways
// on are named (Specification §6.2).
func TestALoginWithNoKeychainSaysSoAndWritesNothing(t *testing.T) {
	in := startInstanceStub(t)
	aDeviceLogin(in)
	in.answer("POST /api/v1/device/tokens", http.StatusOK, map[string]any{
		"userId": "6f1f1a2e-0000-4000-8000-000000000001", "name": "A Maintainer",
		"email": "maintainer@example.com", "isAdministrator": false,
		"token": aSession, "expiresAt": "2026-12-31T00:00:00Z",
	})

	s := newSession(t, in)
	s.store = func(string, string) error { return keychain.ErrUnavailable }

	got := s.run(t, "login")

	if got.Code != exit.Usage {
		t.Fatalf("exit %d, want %d\n%s", got.Code, exit.Usage, got.Stderr)
	}
	for _, wanted := range []string{config.EnvToken, "--token-file"} {
		if !strings.Contains(got.Stderr, wanted) {
			t.Fatalf("%q was not named:\n%s", wanted, got.Stderr)
		}
	}
	if got.mentions(aSession) {
		t.Fatalf("the session token was printed:\n%s%s", got.Stdout, got.Stderr)
	}
	if got.Config.TokenFile != "" {
		t.Fatalf("a token file was chosen for the user: %q", got.Config.TokenFile)
	}
}

// The file is the alternative a person chooses out loud, and it is written the
// way a file holding a token has to be.
func TestATokenFileIsWrittenReadableByItsOwnerOnly(t *testing.T) {
	in := startInstanceStub(t)
	aDeviceLogin(in)
	in.answer("POST /api/v1/device/tokens", http.StatusOK, map[string]any{
		"userId": "6f1f1a2e-0000-4000-8000-000000000001", "name": "A Maintainer",
		"email": "maintainer@example.com", "isAdministrator": false,
		"token": aSession, "expiresAt": "2026-12-31T00:00:00Z",
	})

	s := newSession(t, in)
	path := filepath.Join(s.dir, "token")
	got := s.run(t, "login", "--token-file", path)

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	info, err := os.Stat(path)
	if err != nil {
		t.Fatal(err)
	}
	if mode := info.Mode().Perm(); mode&0o077 != 0 {
		t.Fatalf("the token file is mode %04o", mode)
	}
	if got.Config.TokenFile != path {
		t.Fatalf("the configuration does not remember the file: %q", got.Config.TokenFile)
	}
	if len(got.Keychain) != 0 {
		t.Fatal("the keychain was written to as well")
	}
}

// Nothing is sent to a plain-HTTP host that is not loopback, and the refusal
// arrives before the first request rather than after the token was on the wire.
func TestPlainHTTPOffLoopbackIsRefusedBeforeAnythingIsSent(t *testing.T) {
	in := startInstanceStub(t)
	s := newSession(t, in)
	s.env[config.EnvURL] = "http://vault.example.com"

	got := s.run(t, "login")

	if got.Code != exit.Usage {
		t.Fatalf("exit %d, want %d\n%s", got.Code, exit.Usage, got.Stderr)
	}
	if len(in.Requests) != 0 {
		t.Fatalf("%d requests were made anyway", len(in.Requests))
	}
	if !strings.Contains(got.Stderr, "--insecure-http") {
		t.Fatalf("the override was not named:\n%s", got.Stderr)
	}
}

// A client the instance will not serve has to arrive as a sentence rather than
// as a field that failed to parse, and with an exit code of its own
// (Specification §6.3).
func TestATooOldClientIsASentenceAndItsOwnExitCode(t *testing.T) {
	in := startInstanceStub(t)
	in.refuse("GET /api/handshake", http.StatusUpgradeRequired, "client-too-old",
		"This instance serves clients from 9.0.0 onwards.", map[string]any{"minimumClient": "9.0.0"})

	got := newSession(t, in).run(t, "login")

	if got.Code != exit.Skew {
		t.Fatalf("exit %d, want %d\n%s", got.Code, exit.Skew, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "9.0.0") {
		t.Fatalf("the instance's own sentence was not printed:\n%s", got.Stderr)
	}
}

// An instance that does not serve the contract this build speaks is the other
// half of the same problem, and only this build knows which contract that is.
func TestAnInstanceThatDoesNotServeThisContractSaysWhichItDoes(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/handshake", http.StatusOK, map[string]any{
		"product": "vaultaffe", "release": "9.0.0", "apiVersions": []string{"v2"}, "minimumClient": "0.1.0",
	})

	got := newSession(t, in).run(t, "login")

	if got.Code != exit.Skew {
		t.Fatalf("exit %d, want %d\n%s", got.Code, exit.Skew, got.Stderr)
	}
	if !strings.Contains(got.Stderr, "v2") {
		t.Fatalf("what the instance serves was not named:\n%s", got.Stderr)
	}
}

// A host that answers but is something else entirely is found out at the
// handshake rather than three requests later.
func TestAnotherProductAtThatAddressIsFoundOutAtTheHandshake(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/handshake", http.StatusOK, map[string]any{
		"product": "planaffe", "release": "1.0.0", "apiVersions": []string{"v1"}, "minimumClient": "0.1.0",
	})

	got := newSession(t, in).run(t, "login")

	if got.Code != exit.Skew || !strings.Contains(got.Stderr, "planaffe") {
		t.Fatalf("exit %d:\n%s", got.Code, got.Stderr)
	}
}

// Every request says which build made it, which is what the instance refuses a
// too-old one by.
func TestEveryRequestNamesTheRelease(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("GET /api/v1/instance", http.StatusOK, map[string]any{"started": false})

	got := newSession(t, in).run(t, "instance")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}
	if in.sent("GET", "/api/v1/instance").Header.Get("Vaultaffe-Client") == "" {
		t.Fatal("no Vaultaffe-Client header was sent")
	}
}

// The password is a secret like any other: it comes off stdin, is never an
// argument, and is not echoed anywhere.
func TestTheFirstRunTakesThePasswordFromStdinOnly(t *testing.T) {
	in := startInstanceStub(t)
	in.answer("POST /api/v1/instance", http.StatusOK, map[string]any{
		"organizationId": "6f1f1a2e-0000-4000-8000-000000000009", "organizationName": "Default",
		"session": map[string]any{
			"userId": "6f1f1a2e-0000-4000-8000-000000000001", "name": "A Maintainer",
			"email": "maintainer@example.com", "isAdministrator": true,
			"token": aSession, "expiresAt": "2026-12-31T00:00:00Z",
		},
	})

	s := newSession(t, in)
	s.stdin = "correct horse battery staple\n"
	got := s.run(t, "instance", "start", "--email", "maintainer@example.com", "--name", "A Maintainer")

	if got.Code != exit.OK {
		t.Fatalf("exit %d\n%s", got.Code, got.Stderr)
	}

	var sent map[string]any
	if err := json.Unmarshal(in.sent("POST", "/api/v1/instance").Body, &sent); err != nil {
		t.Fatal(err)
	}
	// The one trailing newline every pipe adds is not part of the password.
	if sent["password"] != "correct horse battery staple" {
		t.Fatalf("the password arrived as %q", sent["password"])
	}
	if got.mentions("correct horse") || got.mentions(aSession) {
		t.Fatalf("a secret was printed:\n%s%s", got.Stdout, got.Stderr)
	}
	if got.Keychain[in.URL] != aSession {
		t.Fatal("the first run did not leave a session behind")
	}
}

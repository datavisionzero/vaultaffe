// Package keychain is where a person's session token lives — the operating
// system's own store, and nowhere else quietly (ADR 0012).
//
// An agent's token never comes through here. It arrives in the environment as
// VAULTAFFE_TOKEN, which is how the harness that started the agent hands it
// over ([Specification §6.4](../../../../Specification.md#64-permissions-in-the-mvp)).
package keychain

import (
	"errors"
	"fmt"

	"github.com/zalando/go-keyring"
)

// Service is the name this CLI's entries carry in the store. One entry per
// instance: the account is the address, so two instances on one machine do not
// overwrite each other's session.
const Service = "vaultaffe"

// ErrUnavailable is no keychain on this machine — a headless Linux with no
// Secret Service, most often. It is not a failure to be worked around silently:
// falling back to plaintext without saying so is the behaviour this product
// exists to be the opposite of (Specification §6.2).
var ErrUnavailable = errors.New("no keychain")

// ErrNotFound is a keychain that works and holds nothing for this instance.
var ErrNotFound = errors.New("no session for this instance")

// Store keeps the token for instance under the operating system's store.
func Store(instance, token string) error {
	if err := keyring.Set(Service, instance, token); err != nil {
		return translate(err)
	}
	return nil
}

// Read answers the token stored for instance.
func Read(instance string) (string, error) {
	token, err := keyring.Get(Service, instance)
	if err != nil {
		return "", translate(err)
	}
	return token, nil
}

// Forget removes the entry for instance. Removing one that is not there is not
// an error: `logout` twice is not a failure.
func Forget(instance string) error {
	err := keyring.Delete(Service, instance)
	switch {
	case err == nil, errors.Is(err, keyring.ErrNotFound):
		return nil
	default:
		return translate(err)
	}
}

// Advice is what the CLI says when there is no keychain: the two ways to hold a
// token on a machine that has no store of its own, named rather than chosen for
// the user. Doppler falls back to plaintext here without a word; that is
// precisely what this sentence exists to not do.
const Advice = `This machine has no keychain the CLI can use (a headless Linux without a Secret Service, most often).
Nothing was written: a session token belongs in a store, and writing one to a file you did not ask for is not a decision this CLI makes for you.
Two ways on:
  • put a token in the environment as VAULTAFFE_TOKEN — how an agent receives one anyway, and how CI holds one;
  • or choose a file yourself: vaultaffe login --token-file ~/.config/vaultaffe/token, which writes it readable only by you.`

func translate(err error) error {
	switch {
	case err == nil:
		return nil
	case errors.Is(err, keyring.ErrNotFound):
		return ErrNotFound
	case errors.Is(err, keyring.ErrUnsupportedPlatform):
		return ErrUnavailable
	default:
		// go-keyring answers a missing Secret Service as a D-Bus error rather
		// than as a kind of its own, and there is nothing else it means here.
		return fmt.Errorf("%w: %v", ErrUnavailable, err)
	}
}

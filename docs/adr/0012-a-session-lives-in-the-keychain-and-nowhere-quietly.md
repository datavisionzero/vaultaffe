# A Session Lives in the Keychain, and Nowhere Quietly

`vaultaffe login` puts the session token into the **operating system's own
store** — Keychain on macOS, the Secret Service over D-Bus on Linux, through
`go-keyring`, which shells out to `security` on the one and speaks D-Bus on the
other and therefore needs no cgo. Where there is no such store — a headless Linux
most often — the CLI **says so, writes nothing, and names the two alternatives**:
a token in `VAULTAFFE_TOKEN`, or a file the person chooses out loud with
`login --token-file`.

## The decision is the failure case, not the happy one

Everybody puts the token in the keychain. Doppler does too, and then falls back
to a plaintext file when there is no keychain, without saying so and without
documenting it. That fallback is the decision this ADR is about, and we make the
opposite one: a secrets manager that quietly writes a credential to disk on the
machines where security is *hardest* has inverted its own promise on exactly the
machines that need it.

Refusing silently would be the other bad answer. A CLI that just fails on a
headless box is a CLI somebody works around with a shell alias, and the
workaround will be worse than what we would have offered. So the refusal is a
sentence with two named ways on, both of which the person has to choose:

- `VAULTAFFE_TOKEN`, which is how an agent receives its token anyway
  ([Specification §6.4](../../Specification.md#64-permissions-in-the-mvp)) and how
  CI holds one;
- `login --token-file <path>`, written `0600`, its path recorded in the user's
  configuration. A file that others can read is refused on the way back in, with
  the `chmod` that fixes it — a file mode is the only protection a token in a
  file has, and shrugging at `0644` would be the quiet fallback by another route.

## What never comes through here

**An agent's token.** It arrives in the agent's environment and lives there; that
is plaintext on that machine and it is inside the threat model
([§4](../../Specification.md#4-guiding-principles)). `run` strips every
`VAULTAFFE_` variable from the child process so it goes no further
([§6.2](../../Specification.md#62-cli)), and `logout` refuses a token that came
from the environment rather than revoking something the person at the terminal
may not know they are holding.

## Consequences

**One entry per instance**, keyed by address, so two instances on one machine do
not overwrite each other's session — and `logout` removes exactly one of them.

**The keychain is reached through three functions**, injected into the command
tree, so that a test never touches the machine's own store and CI needs no
Secret Service to run the CLI's tests.

**The token is not in the configuration file.** That file holds the instance, the
prefix table and — where one was chosen — the *path* of the token file. Nothing in
it is a credential.

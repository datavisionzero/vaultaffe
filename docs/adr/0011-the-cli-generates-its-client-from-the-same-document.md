# The CLI Generates Its Client From the Same Document

[ADR 0006](./0006-the-contract-is-checked-in-and-the-web-client-is-generated-from-it.md)
left this one open on purpose: the document is checked in either way, and whether
`vaultaffe` generates a Go client from it or writes the few structs it reads by
hand was to be answered with the code in front of whoever answered it. The code
is here now, and the answer is **generated** — `oapi-codegen`, from
`docs/api/openapi.json`, into `src/cli/internal/api`, not committed and produced
by `go generate ./...` before vet, test and build.

## Why not hand-written

The argument for hand-writing was real: this CLI must not let a value reach an
agent's context (Specification §4), and a hand-written client names only the
fields it means to have. That argument does not survive contact with the surface.
The CLI reads projects, environments, secret names and status, one value, the
change log, the value history, tokens, the handshake, three device-login steps
and the import report — thirty-odd shapes, and the ones with the most fields are
the ones nobody would enjoy transcribing. Every one of them is a field name that
can be got wrong silently, in a language where a missing JSON key is a zero value
rather than an error.

And the value-blindness the hand-written version was supposed to buy is not
bought there. `SecretValue.Value` exists in the contract because one endpoint
returns one value on purpose; what keeps it out of a transcript is which
*commands* print it — `secrets get`, and `run` into a child process's environment
— and that is a property of the command tree, which is ours either way. A
generated type does not print itself.

**What generating buys instead is a compiler between the server and both its
clients.** The web application already generates from this document; a renamed
field that reaches production is exactly what the arrangement exists to prevent,
and the CLI is the client an agent runs unattended.

## Consequences

**The generated file is not committed, and CI generates it.** A working tree is
therefore never a state where the client agrees with a contract that has moved,
and a reviewer reads the contract's diff rather than a machine's.

**The module now has dependencies**, and therefore a `go.sum` that the CI cache
keys on: `oapi-codegen` as a tool, its runtime, `cobra` for the command tree, and
`go-keyring` ([ADR 0012](./0012-a-session-lives-in-the-keychain-and-nowhere-quietly.md)).
The binary stays static and cgo-free, which is the property that matters for
something that runs inside an agent's container.

**A response shape changing is now a three-part commit** — the endpoint, the
regenerated document, and whichever client stops compiling. That is the point.

**The generated client is wrapped, never used raw.** `internal/client` is where
the bearer token, the release header and the turning of a problem document into
an exit code live, so that no command reaches around them.

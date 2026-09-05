# The Contract Is Checked In, and the Web Client Is Generated From It

`docs/api/openapi.json` is the HTTP contract, it is a file in this repository,
and the web application does not write request or response types by hand: its API
layer is generated from that document with `openapi-typescript` and
`openapi-fetch`. The generated output is not committed — the document is the
artifact, its output is not.

The alternative is what most projects do: hand-written `fetch` wrappers, with an
OpenAPI document published for outsiders as a description of what the server
happens to do. That is the arrangement that lets a renamed field reach
production, and this repository carries three languages that share no types by
design (`docs/codebase.md`). Generating from one document is what puts a compiler
back between the server and its clients.

**The document is captured from a running instance, not written by hand.** The
endpoint definitions produce it; `ContractTests` starts the whole application
against a Postgres, fetches what it serves and fails if it differs from the file
checked in; CI makes the same comparison on a second path — it starts the
installation and diffs the captured file — so that neither the test nor the
capture can be the only thing that was right. Hand-maintaining the document would
make it a wish; generating it without checking it in would make a breaking change
invisible in review. Checked in and verified, the diff of a pull request shows the
contract changing.

Regenerating is one command:

```sh
VAULTAFFE_CAPTURE_CONTRACT=1 dotnet test tests/Vaultaffe.IntegrationTests
```

## The CLI's own client is not decided here

A document good enough to generate one client from is good enough to generate
two. Whether `vaultaffe` does that or writes the few structs it reads by hand is
the CLI skeleton's decision, not this one: the CLI does not exist yet, and its own
constraint — that no command an agent runs may let a value reach its context
([§4](../../Specification.md#4-guiding-principles)) — is an argument about which
fields it names, which is exactly the kind of question that should be answered
with the code in front of whoever answers it.

What this ADR does fix for it either way: the document is checked in, so a Go
client that is generated has something to generate from without a running
server, and one that is hand-written has something to be reviewed against.

## Consequences

**A change to a response shape is a two-part commit** — the endpoint and the
regenerated document — and a third part whenever the web client no longer
compiles against it. That is the point, and it is the cheapest moment to find
out.

**The document is also the API documentation** for anyone scripting against an
instance. One artifact serves the generator, the test and the reader; `docs/api.md`
explains the parts a schema cannot say.

**`info.version` is the contract's version, not the instance's.** The API carries
its version in the path
([ADR 0005](./0005-the-api-carries-its-version-in-the-path.md)), so the document
says `v1` and does not change when a release is cut. Which release an instance is,
is the handshake's answer.

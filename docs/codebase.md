# The Codebase

[`Vision.md`](../Vision.md) says what Vaultaffe is for and
[`Specification.md`](../Specification.md) how it behaves. This one says where
that lives: how the repository is laid out, which project holds what, which way
the dependencies point, and what is built by which toolchain.

Most of it does not exist yet. **The skeleton was raised before there was
anything to break, so that CI could be green from the first commit** (ADR 0001);
what has landed in it so far is the data model — the eight tables, the
migrations that apply themselves on startup, and the one place two organizations
are kept apart ([`storage.md`](./storage.md)) — the two formats nothing can
change afterwards: the token value and the envelope a secret rests in
([ADR 0004](./adr/0004-the-token-format-and-the-envelope.md)) — and the contract
the rest of the API arrives inside: the version in the path, the handshake, the
shape of a refusal, and the document all of it is captured into
([`api.md`](./api.md)). Beside it the Go module with one command that prints its
version, and the workflow that builds and tests both. This document is kept accurate from here on: a file that lands
somewhere it does not describe means one of the two is wrong.

Three decisions shape the layout. Two of them the specification already made —
the CLI is **Go** and the frontend is **React**, both for the reasons in
[Specification §9](../Specification.md#9-technical-guardrails). The third is made
here: the backend is **four layers**
([ADR 0002](./adr/0002-the-backend-is-four-layers-not-one-project.md)). The
repository therefore carries three languages, and the artifact an operator runs
carries two of them.

## The shape of the repository

```
vaultaffe/
├─ .github/workflows/          ci on every push
├─ docs/                       the decisions, and this
│  ├─ adr/
│  ├─ api.md                   the HTTP surface: versions, headers, the shape of a refusal
│  ├─ api/openapi.json         the contract, captured from a running instance and checked in
│  └─ storage.md               the data model: tables, constraints, what is enforced where
├─ src/
│  ├─ Vaultaffe.Domain/         the rules
│  ├─ Vaultaffe.Application/    the use cases and their ports
│  ├─ Vaultaffe.Infrastructure/ Postgres, the encryption, the schema
│  ├─ Vaultaffe.Api/            HTTP and the composition root
│  ├─ cli/                      the Go CLI — `vaultaffe`
│  └─ web/                      the single-page application (not here yet)
├─ tests/
│  ├─ Vaultaffe.UnitTests/
│  └─ Vaultaffe.IntegrationTests/
└─ Vaultaffe.slnx              plus global.json and the Directory.* properties
```

`src/` and `tests/` is the convention a .NET contributor arrives expecting, and
the open-source intent of the vision is reason enough to meet it rather than
invent something more descriptive.

Documents that describe a surface — the data model, the HTTP API, the CLI, the
screens, running an instance — get their own file under `docs/` as that surface
arrives. Writing the empty files now would only invite them to drift. Two are
here so far: [`storage.md`](./storage.md) and [`api.md`](./api.md).

## The four layers

Dependencies point inward and only inward: Domain depends on nothing,
Application on Domain, Infrastructure on Application, and Api on both of the
outer two as the composition root. Domain carries no package references at all,
which is the cheapest possible check that nothing has leaked into it —
`LayeringTests` reads the four project files and fails the build on a reference
pointing outward.

**`Vaultaffe.Domain` holds the rules.** Organization, project, environment and
secret with the name rule `^[A-Z_][A-Z0-9_]*$`; the three token kinds with their
prefixes, bindings and scope sets, and the shape of a token value itself; what
an empty placeholder is and what it stops; the bounds on value history — five
versions, 72 hours, whichever is hit first — and the recovery window on a
deletion. The test of whether something
belongs here: **anything the specification already states as a rule.** A token
that can be constructed without a binding, or a value version that can outlive
both of its bounds, is a rule that escaped.

**`Vaultaffe.Application` holds the acts and the ports.** An act is one thing a
caller does, a port is one thing the acts need answered. Setting a secret,
reading one, listing names without values, importing an environment, rolling
back, deleting recoverably and restoring, creating tokens under a human session.
Beside them the ports: the stores, the identity of the caller, the key ring that
seals a value and opens it again, and the clock — which is `TimeProvider` from
the base class libraries rather than a port of ours.

**`Vaultaffe.Infrastructure` answers those ports.** `Persistence/` is the one
place that declares schema: the context, one configuration per table, the
migrations and the migrator that applies them before anything is served.
`Encryption/` is the envelope of
[Specification §6.3](../Specification.md#63-operations) — a data key per secret
under the instance master key, in the columns that model already carries — and
the only place in this product that holds a value in the clear on purpose. The
organization filter of
[§9](../Specification.md#9-technical-guardrails) sits here too, in
`VaultaffeDbContext.OnModelCreating` and nowhere else:
[`storage.md`](./storage.md) has the whole of it.

**`Vaultaffe.Api` is the adapters and the composition root.** The endpoints,
the token and session authentication, the version handshake, the one place a
refusal becomes a problem document — and the browser confirmation page the
device-code login needs. The HTTP API is the only read and write interface
(§9): the web application is a client of it exactly as the CLI is, and a later
MCP server would be a third adapter over the same acts rather than a second way
into the data.

## The CLI is a client, not a layer

`src/cli/` is an ordinary Go module — `cmd/vaultaffe` the binary, `internal/`
the packages. It references nothing in `src/` and knows an instance only through
its public HTTP API, which is what lets the same static binary run on a laptop,
a CI runner or inside an agent's container. It ships as its own release artifact,
one binary per platform, and is versioned with the server it was cut from: the
tag sets `-ldflags -X …/internal/version.Value` here and `-p:Version=` on the
.NET side, so the two halves of a release cannot disagree about which release
they are.

Windows is not a target ([Specification §6.2](../Specification.md#62-cli)):
`run` replaces itself with the child process through `exec()`, and Windows has
no such call. WSL runs the Linux binary.

## Tests are split by what they need

**`Vaultaffe.UnitTests`** runs in seconds and needs nothing installed: the rules
of Domain and the use cases of Application against substituted ports.
**`Vaultaffe.IntegrationTests`** brings up Postgres with Testcontainers, because
the parts no substitute can vouch for are the ones this product is about — that
a value survives the round trip through the envelope encryption, that the
organization filter cannot be stepped around, that the migrations apply, that a
version falling out of the history window is deleted rather than tombstoned. The
split is by what a test needs rather than by what it covers, because that is the
distinction CI has to act on.

Beside them the contract, which needs a third thing: the whole application in
the test process, on a database of its own. `AnInstance` is that — a
`WebApplicationFactory` with the two values an operator configures — so that what
a test asks about the API is asked of a running installation over HTTP,
migrations and all, rather than of a handler somebody called directly.

What is in it today is the schema, the tenancy and the envelope: that the
migrations apply and that applying them twice is uneventful, that a name outside
its rule is refused by the database and not only by the domain, that a deleted
object keeps its name reserved, that a half-sealed value cannot be written, that
one organization never sees another's rows, that a value written under a
secret's own data key comes back out of Postgres unchanged and a superseded one
with it — and that the change log has no column a value could live in. Then the
contract: that a client too old is told so rather than left to fail at a field,
that a refusal is a problem document whose `code` a client can switch on, and
that the document a running instance serves is the one checked in.

The frontend will carry its own tests inside `src/web/`, and the CLI carries its
own inside `src/cli/`, each run by the CI job that builds it.

## One workflow, and it is the gate

`.github/workflows/ci.yml` runs on every push to `main`, every pull request and
on demand. There is no review step between a commit and the trunk (ADR 0001), so
that workflow is the only thing standing between a mistake and `main`: the
format check, the unit tests, the integration tests on Testcontainers, and the
Go job that formats, vets, tests and builds the CLI.

The contract job is the fifth: it starts the installation against a Postgres,
captures the document it serves and fails on a diff against the one checked in
([ADR 0006](./adr/0006-the-contract-is-checked-in-and-the-web-client-is-generated-from-it.md)).
`ContractTests` makes the same comparison from the other side, which is
deliberate — the capture and the test check each other.

The web build and the image are not in it yet. Each of them would have to settle
a question another ticket owns, and each arrives in the commit that creates its
subject.

## What is deliberately not here

- **No second read path and no second write path.** Every adapter — HTTP today,
  MCP later — calls the same use cases. The HTTP API is the only interface;
  there are no privileged side doors for the web UI (Specification §9).
- **No shared types across the three languages.** The server shares none with
  the CLI or the frontend, and the HTTP contract is what holds them together.
- **No context split.** This is a single-context repository, and `src/` is laid
  out by layer rather than by bounded context.
- **No `ee/` directory.** MIT throughout, with no feature behind a second
  license — that is a product promise (Vision), and the layout is where it
  either holds or does not.

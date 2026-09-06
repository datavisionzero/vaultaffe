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
([ADR 0004](./adr/0004-the-token-format-and-the-envelope.md)) — the contract the
rest of the API arrives inside: the version in the path, the handshake, the shape
of a refusal, and the document all of it is captured into
([`api.md`](./api.md)) — identity: the first run, signing in, the device-code
login with the one page it needs, and token management — the authorization
every endpoint after it is held to: the scope set, the binding, and the short
list only a person may do — the catalogue: projects and environments, with
the change log that every write path since has been in — and the secrets surface
itself: names without values, one value at a time, a file in and a file out, and
the change log and value history read back and rolled back, and what deletion
means at the end: purge, and the sweep that enforces the deadline. Beside it the
CLI's own skeleton — the command tree, the client generated from that contract,
the device-code login, and the prefix table that says what a directory means
([`cli.md`](./cli.md)) — and the web application: the frame, the way in, the
people of the organization, the catalogue screens the product is used on, and
what makes a change legible and reversible — the log, the value history with its
rollback, and the purge only a person may ask for
([`human-interface.md`](./human-interface.md)) — and, under
[`deploy/`](../deploy/), the way all of it is actually run: one image, Caddy in
front of it, and the first run that turns an installation into an organization —
and the workflow that builds, tests and then starts all three. This
document is kept accurate from here on: a file that lands somewhere it does not
describe means one of the two is wrong.

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
├─ deploy/                     how an instance is actually run
│  ├─ Dockerfile               one image: the API and the SPA it serves
│  ├─ docker-compose.yml       the instance, Postgres, and Caddy in front
│  ├─ Caddyfile                the site address, and the whole TLS decision
│  ├─ .env.example             the two values an operator fills in
│  ├─ backup.sh                one file holding the dump and the key together
│  └─ restore.sh               and the way back in
├─ docs/                       the decisions, and this
│  ├─ adr/
│  ├─ agents.md                handing an agent its token, per harness
│  ├─ api.md                   the HTTP surface: versions, headers, the shape of a refusal
│  ├─ api/openapi.json         the contract, captured from a running instance and checked in
│  ├─ cli.md                   the CLI surface: the token, the binding, the exit codes
│  ├─ human-interface.md       the screens, their actions, and who may do what
│  ├─ operations.md            keeping an instance: the key, upgrades, backup and restore
│  └─ storage.md               the data model: tables, constraints, what is enforced where
├─ src/
│  ├─ Vaultaffe.Domain/         the rules
│  ├─ Vaultaffe.Application/    the use cases and their ports
│  ├─ Vaultaffe.Infrastructure/ Postgres, the encryption, the schema
│  ├─ Vaultaffe.Api/            HTTP and the composition root
│  ├─ cli/                      the Go CLI — `vaultaffe`
│  └─ web/                      the single-page application — React on Vite
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
arrives. Writing the empty files now would only invite them to drift. Four are
here so far: [`storage.md`](./storage.md), [`api.md`](./api.md),
[`cli.md`](./cli.md) and [`human-interface.md`](./human-interface.md) — the last
of them written *before* its surface exists, which is deliberate: the screens are
held together by that document rather than by their components.

The fifth is [`operations.md`](./operations.md), which arrived with backup and
restore because it needed them to be worth reading and now covers the whole of an
instance's life: bringing one up, what is configured and what deliberately is
not, the key, upgrading, and what to look at when something is wrong. It does not
repeat the comments in `docker-compose.yml` and `.env.example` — those are where
somebody setting an instance up is actually looking, and what they have to *know*
before they need it is here.

The sixth, [`agents.md`](./agents.md), is the only one that documents somebody
else's software, and it exists because the agent-token model is one sentence of
ours (`VAULTAFFE_TOKEN` in the agent's environment,
[§6.4](../Specification.md#64-permissions-in-the-mvp)) and three different
answers in the harnesses people actually run — one of which drops the variable
without a word, leaving the change log to credit a human for everything the agent
did.

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
deletion. `Secrets/DotEnv` is here for the same reason the name rules are: there
is no standard for that format, every tool differs at its edges, and what this one
does at them is a decision this product makes rather than a library's habit.
`Secrets/MissingKeys` is here for a reason of its own: it is arithmetic over key
names — a key is missing here when more than half of the project's other
environments hold it — and putting the rule in the domain is what lets it be
read, tested and argued about with no database anywhere near it, which for the
one feature that is *only* a rule is the whole point. The test of whether
something belongs here: **anything the specification already states as a rule.** A token
that can be constructed without a binding, or a value version that can outlive
both of its bounds, is a rule that escaped. `Authorization/` is the one to look
at twice: the short list of §6.4 that only a person may do, and the sentences a
refusal says about them — which name the action and never a command
([ADR 0010](./adr/0010-a-refusal-names-the-action-and-the-client-names-the-command.md)).

**`Vaultaffe.Application` holds the acts and the ports.** An act is one thing a
caller does, a port is one thing the acts need answered. Starting the instance,
signing in, the three steps of a device login, creating and revoking tokens,
creating and listing and renaming and deleting and restoring a project, an
environment or a secret, writing a value, reading one, importing a file and
exporting one, reading the change log and the value history, rolling back to an
earlier value, purging one early, reading the missing-key notice and dismissing a
line of it, and reading the access summary of a key. Beside the ports one is
worth naming for what it is not: `ISecretAccessStore` is the only thing here
written on a **read**, which is why it is a port of its own rather than two more
methods among the ones that seal and delete values. Beside them the sweep that removes what the
deadlines have passed, which is the one thing here that is not an act because
nobody asked for it. Beside them the ports: the stores, the identity of the caller, the thing that hashes a
password, the key ring that seals a value and opens it again, and the clock —
which is `TimeProvider` from the base class libraries rather than a port of ours.

`ChangeLog` is one of the acts and belongs to **every** write path, not to a
later ticket: it takes an action and the names it happened to, has no parameter a
value could be passed through, and enlists its entry so the same `SaveAsync` that
commits the change commits the record of it
([§6.5](../Specification.md#65-logging-and-history)). A log written in a second
transaction is one that can disagree with what happened.

`Caller` is the one to know: the organization, the person, the token, its scopes
and what it reaches, as a value rather than the rows it came from. The adapter
that authenticated the request hands the same one to every act in it, and it is
what the change log's identity type is read from.

`Authorization/Authority` is the other: **the one place that says no.** Every rule
of [§6.4](../Specification.md#64-permissions-in-the-mvp) — the scope set, the
binding, the human-only list — is decided there, and an act or an endpoint says
what it needs rather than checking anything. A rule spelled out in twenty handlers
is nineteen chances to spell it differently, and in a secrets manager the one that
is spelled wrong is the one nobody notices.

**`Vaultaffe.Infrastructure` answers those ports.** `Identity/` hashes passwords
with Argon2id in the encoding that carries its own parameters, so raising the cost
is a re-hash rather than a migration. `Persistence/` is the one
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

**`Vaultaffe.Api` is the adapters and the composition root.** The endpoints, the
token authentication, the version handshake, the authorization middleware, the one
place a refusal becomes a problem document — and the browser confirmation page the
device-code login needs,
which is the whole of the browser surface until the management application
arrives. `CallerContext` is worth knowing about: it answers both the caller port
the acts ask and the organization scope the query filter reads, so a caller can
never be inside a different organization from the one their queries are filtered
by. Beside it `EndpointAuthorization`: an endpoint declares what it needs —
`HumanOnly(…)`, `Needing(…)` — as metadata, and one middleware after routing
enforces every such declaration through `Authority`. A handler therefore carries
no authorization code at all, and an endpoint that forgot its requirement is a
missing line one can grep for rather than a check hidden in a method. The HTTP API is the only read and write interface
(§9): the web application is a client of it exactly as the CLI is, and a later
MCP server would be a third adapter over the same acts rather than a second way
into the data.

## The CLI is a client, not a layer

`src/cli/` is an ordinary Go module — `cmd/vaultaffe` the binary, `internal/`
the packages. It references nothing in `src/` and knows an instance only through
its public HTTP API, which is what lets the same static binary run on a laptop,
a CI runner or inside an agent's container.

`internal/api` is that API, generated from `docs/api/openapi.json` and **not
committed**: `go generate ./...` produces it before vet, test and build, so a
working tree is never a state where the client agrees with a contract that has
moved ([ADR 0011](./adr/0011-the-cli-generates-its-client-from-the-same-document.md)).
Nothing calls it raw. `internal/client` wraps it with what every request carries
and every answer is checked for — the bearer token, the release this build is,
and the turning of a problem document into a sentence and an exit code — and
`internal/exit` is that code, one per kind of outcome, so that a script branches
on a number rather than on wording. `internal/problem` keeps the extension
members of a refusal, which is how a command prints what its own code carries
without this CLI having to know every code there will ever be.

`internal/config` is the one to read: it holds no token and decides three
things — which instance, as whom, and what this directory means. The binding is a
prefix table in the user's own configuration, and the optional checked-in
`.vaultaffe` file is an input to `setup` rather than a second source of truth, so
that a repository cannot rebind somebody's directories by being cloned. The
refusal of plain HTTP off loopback lives here too, because it is a property of an
address rather than of a request. `internal/keychain` is where a person's session
goes, and the interesting part of it is the failure case: no store means a
sentence and two named alternatives, never a quiet file
([ADR 0012](./adr/0012-a-session-lives-in-the-keychain-and-nowhere-quietly.md)).

`internal/cmd` is the tree, and it carries the client half of
[ADR 0010](./adr/0010-a-refusal-names-the-action-and-the-client-names-the-command.md):
a table from the action a refusal names to the command *this* CLI has for it,
checked against the real command tree, so that an entry outliving its command
cannot become a suggestion to run something that does not exist. Its `Env` is
what makes the surface testable — the environment, the directory, the three
streams, the HTTP client, the keychain, and the call that replaces this process.
That last one is what makes `run` testable at all: a test sees the path, the
argument vector and the environment that would have been handed to `exec()`
instead of the process ceasing to exist, which is how the protected list, the
stripped `VAULTAFFE_` variables and the two lookup exit codes are held.

The secrets surface is where the CLI's own half of value-blindness lives, and it
is mostly a set of decisions about what a command does **not** do: `set` reads a
value from stdin and refuses `KEY=value` with the reason, no confirmation echoes
what was written, the listing and the version history carry names and moments,
and `get` is the one command whose purpose is to put a value where somebody can
see it — which is why it takes one key and no pattern. Its tests are the same
list from the other side.

Beside it is as much of the catalogue as bringing an instance up from the console
takes, and the one command that turns names into ids: a token binding is by id
and everything else in this CLI is by name, so `tokens create` looks the project
up rather than asking anybody for a UUID. What it deliberately does **not** do is
read the directory's own binding — a token narrowed by where somebody happened to
be standing would be a surprise nobody could see in the command they typed.

It ships as its own release artifact,
one binary per platform, and is versioned with the server it was cut from: the
tag sets `-ldflags -X …/internal/version.Value` here and `-p:Version=` on the
.NET side, so the two halves of a release cannot disagree about which release
they are.

Windows is not a target ([Specification §6.2](../Specification.md#62-cli)):
`run` replaces itself with the child process through `exec()`, and Windows has
no such call. WSL runs the Linux binary.

What `exec()` actually does — and the four things it leaves for `run` to do
before the call — was measured on both target platforms rather than inferred:
[ADR 0009](./adr/0009-run-replaces-itself-and-does-everything-else-first.md).

## The frontend is a client too

`src/web/` is an ordinary Vite project — React 19, TypeScript, Tailwind 4,
shadcn on Base UI, `react-router`, Vitest — and it reaches an instance only
through the same public API the CLI does. The stack and the frame are the sister
project's, adopted rather than decided again
([ADR 0013](./adr/0013-the-web-application-is-the-sister-projects-frame.md)).

`src/api/schema.d.ts` is generated from `docs/api/openapi.json` and **not
committed**, exactly as the CLI's client is: `generate` runs before `dev`,
`build`, `typecheck` and `test`, so a route that changed shape stops the build
here rather than failing on a screen
([ADR 0006](./adr/0006-the-contract-is-checked-in-and-the-web-client-is-generated-from-it.md)).
`src/api/client.ts` is the only thing that wraps it: the bearer token on every
request, the one place a `401` from anywhere else means the session ended, and
the turning of a problem document into the sentence a screen shows — which is
the instance's own, because a refusal names the action and the client names the
command ([ADR 0010](./adr/0010-a-refusal-names-the-action-and-the-client-names-the-command.md))
and a paraphrase loses both.

`src/api/useAsk.ts` is how every screen reads. A question is **named** —
`"users"`, `"secrets:landing-page/prod"` — rather than watched through a
dependency array, so an answer belongs to the question it was asked for and the
last one never shows under the next; and `answered()` beside it turns a refusal
into something to catch, so an act shows the instance's own sentence where it was
asked for.

`src/session/` is where that token lives, and the interesting part is the same
as the CLI's: the store, not the happy path. It is `sessionStorage`, so the tab
is the session's lifetime, and a browser that refuses storage gets a token in
memory rather than a broken page
([ADR 0014](./adr/0014-the-browser-holds-its-session-for-as-long-as-the-tab.md)).

`src/shell/` is the frame: the sidebar, the header, the palette, the account
menu and the overview of the keys. `views.ts` is the route table and the place
every address inside the product is built — a project and an environment in
lower case, a key in upper, each escaped
([ADR 0003](./adr/0003-a-name-inside-a-reference-is-narrow-and-lower-case.md)).
`shortcuts.ts` is the only list of bound keys: the handlers ask it what was
pressed and the `?` overview draws what it holds, so a key that is bound is a
key that is advertised, and one that no screen answers yet is not in it at all.
The frame asks the instance for nothing — it renders before the organization's
data and is not remounted by navigation, which is what makes a loading state a
skeleton inside a frame rather than a blank page.

The screens sit beside the frame, one directory per part of the matrix.
`src/entry/` is what a tab that is nobody can reach — the first run, signing in,
and accepting an invitation — and none of them is inside the shell, because there
is no session behind them yet. `Doorway` is the one that asks which: an
installation nobody has started has **only** `entry/FirstRun.tsx`, at `/start`,
and every other address leads there. `src/catalogue/` is the product itself: the
projects, one project, the environment screen and one key, with that key's own
history beside its value. `src/changes/` is the change log: the screen, and the entries it
shares with the key that is read on its own. `src/settings/` is the area list
beside the area: the tokens, the people, the organization's one word, and the
two things a person changes about themselves. `src/shared/` holds what more than one of them needs — the
page header, the two confirmation dialogs, a labelled field that knows where a
refusal goes, the two spellings of a moment, and the confirmation a purge gets.

**Every route of the matrix now leads to the screen it names**, so the scaffold
that said "not built yet" is gone and `shell/Nowhere.tsx` is what is left: the
answer to an address this application has no screen for.

The change log and the value history are two files rather than one because they
are two different things
([Specification §6.5](../Specification.md#65-logging-and-history)). `changes/`
reads a log that holds **no value at all** and puts the acting identity's *type*
on every row, because with writing agents that is the interesting half of "who";
its filter lives in the address, and a narrower field stays closed until the
wider one is named, so the combination the instance refuses is unreachable from
here. `catalogue/History.tsx` reads a listing of moments — id, written,
replaced, expires and never a value — and offers the rollback, which is a write
like any other and is therefore an agent's too.

`shared/Purge.tsx` is the one confirmation four different acts share, and it is
its own file because of what it says rather than what it does: a purge destroys
the undo button, **and it does not reach last night's backup**. Both sentences
are in the dialog. The second is the one no product we looked at says out loud.

The masking rule is visible in the code rather than described by it: the
environment screen renders a listing that **has no values in it** — the endpoint
does not carry them — and `catalogue/Secret.tsx` is the only file that reads one,
one key at a time, holding it in component state that a reload, the back button
and leaving the screen all take away. The other two values this product hands
over exactly once — an invitation link and a token — appear in the dialog that
made them and in no listing afterwards, which is the instance's rule showing
through rather than a decision of these screens.

Two things this application deliberately does not have. There is no route for
`/device`: that page is rendered by the instance, holds no session and asks for
a password every time
([ADR 0008](./adr/0008-a-session-is-a-token-and-the-only-page-asks-for-a-password.md)),
so it is reached by leaving rather than by routing. And the palette searches the
names of screens and the names of the catalogue — projects and their
environments, asked for when it opens — and **never a value**, because the
listing endpoints do not carry one and a palette that turned one up would be a
reveal nobody asked for. Key names are deliberately not in it: finding one would
mean a listing per environment on every open.

`npm run build` lands in `src/Vaultaffe.Api/wwwroot`, which the API serves as
static files with everything unclaimed falling back to `index.html`. One
`dotnet run` is therefore the whole product, and the browser reaches the API at
its own origin; in development Vite serves the SPA and forwards `/api`,
`/openapi`, `/problems` and `/device` to the instance so that stays true there
too.

## The stack is three services, and one of them is the product

`deploy/` is how an instance is run, and the shape of it is
[ADR 0016](./adr/0016-one-image-and-caddy-in-front-of-it.md): **one image
carrying the API and the built SPA**, Postgres behind it, and Caddy in front as
the TLS terminator. There is no frontend service. The application is served from
the `wwwroot` the Vite build lands in, so the browser reaches the API at its own
origin and there is no address to configure, no origin to allow, and no way for
the two halves of a release to be different builds.

`Dockerfile` is three stages and the two toolchains meet in it and nowhere else:
Node builds the SPA — generating its API layer from the checked-in contract
first, exactly as a contributor's `npm run build` does (ADR 0006) — the .NET SDK
publishes the API, and the runtime image carries neither of them. It runs as the
non-root user the base image provides and writes nothing: the master key is held
in memory, and everything durable is Postgres's volume.

`docker-compose.yml` is the whole installation. Two values are the operator's —
the database password and the master key — and both live in `deploy/.env`, which
is ignored by git and is the one file in this product that is *expected* to hold
key material. The instance publishes no port: Caddy is the only way in, which is
what makes "the backend speaks only HTTP on the internal network"
([§6.3](../Specification.md#63-operations)) a property of the file rather than a
sentence in a guide.

`Caddyfile` holds one variable and therefore one decision. A domain in
`VAULTAFFE_SITE_ADDRESS` and Caddy obtains and renews a certificate for it; left
at `:80` it is plain HTTP for a trial on a laptop, which is safe because the CLI
refuses plain HTTP off loopback unless it is told otherwise
([`cli.md`](./cli.md)) — so `localhost` works out of the box and nothing else
quietly does.

What an operator does after `up -d` is the **first run**, and it is a screen
rather than a variable: no bootstrap administrator in the environment, because
the first user is created by the one unauthenticated request there is and the
second such request is refused
([ADR 0007](./adr/0007-the-first-run-is-unauthenticated-and-happens-once.md)).
The window between the two is real, and `entry/FirstRun.tsx` says so on the
screen where it can still be closed.

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
that the document a running instance serves is the one checked in. Then identity,
end to end over HTTP: that an instance cannot be started twice, that a wrong
password and an unknown address are one answer, that a device code hands over one
token and never a second, and that no token listing carries a value. Then
authorization: that a machine token is refused a human-only action with the
action's name in the document and no command in it, that a refused request wrote
nothing, and — in the unit tests, where the rules are — that a scope set is
carried whole or not at all, that a token bound to one environment does not reach
its neighbour, and that no refusal this product can make spells a command. Then
the secrets surface, which is mostly a set of assertions about what an answer does
**not** contain: that a write is not echoed back, that no listing carries a value
or even the word, that a token with `names` and not `read` sees the keys and is
refused one of them, that a token that may write and not read can still rotate a
credential without ever seeing either half of it, that a multi-line value survives
a round trip through the export format, that the history stays inside both of its
bounds, and that an agent with every scope there is still cannot export. Then the two
histories: that the log names the identity and its type — an agent as an agent —
that no value reaches it however often one is written, that a read is not in it,
that pages do not overlap, that a bound token has to name what it is asking about,
and that a rollback puts a value back without anybody having read it. Then the end
of a deletion: that a purge is human-only however many scopes a token carries,
that it takes the retained subtree and frees the name, that a key's history can be
made to genuinely disappear while the key stays, that the deadline removes what
nobody purged, that a descendant deleted first expires on its own clock — and that
the change log outlives every one of those. Then
the catalogue: that a project arrives with its three environments, that a deleted
one keeps its name reserved and comes back with the subtree it had rather than
the one it would have had, that past the window there is a refusal and not a
missing row, that a bound token's listing is narrowed while its writes are
refused — and that every one of those changes is in the change log under the
identity and the type that made it, an agent's own token included.

`AnInstance` carries the one service a test replaces: a clock it can move. A
window measured in days has no other way of being asked about, and everything
else in that host is the installation an operator gets.

The frontend carries its own tests inside `src/web/` and the CLI its own inside
`src/cli/`, each run by the CI job that builds it. The frontend's are Vitest and
Testing Library against a fetch the test stands in front of the generated
client — a route table of `METHOD /path` — and they are written against roles
and accessible names rather than markup, because that is the same floor
[`human-interface.md`](./human-interface.md) sets. The CLI's are Go tests
against an `httptest` instance and an injected keychain, and much of what they
assert is what an invocation did **not** print: that the session token
`login` collected is in neither stream, that a password piped into the first run
is not echoed, and that nothing is sent at all to a plain-HTTP host that is not
loopback.

## One workflow is the gate, and a second one is the release

`.github/workflows/ci.yml` runs on every push to `main`, every pull request and
on demand. There is no review step between a commit and the trunk (ADR 0001), so
that workflow is the only thing standing between a mistake and `main`: the
format check, the unit tests, the integration tests on Testcontainers, the web
job that typechecks, lints, tests and builds the frontend, and the Go job that
generates the client from the checked-in contract and then formats, vets, tests
and builds the CLI. Neither client job commits its generated layer, which makes
both of them second readers of the same document the contract job verifies
against a running instance.

The contract job is the last of them: it starts the installation against a
Postgres, captures the document it serves and fails on a diff against the one
checked in
([ADR 0006](./adr/0006-the-contract-is-checked-in-and-the-web-client-is-generated-from-it.md)).
`ContractTests` makes the same comparison from the other side, which is
deliberate — the capture and the test check each other.

The last job is not a toolchain at all. It builds the image and brings
`deploy/docker-compose.yml` up — Caddy, the instance, Postgres — and then asks
the running stack the three questions no test inside any of the three languages
can answer: that the handshake replies through the proxy, that what is served at
`/` is the application rather than only the API, and that the first run happens
once and the second is refused. That is the success criterion of `git clone` to a
running instance ([§11](../Specification.md#11-success-criteria-for-the-mvp)),
checked on every commit rather than remembered before a release. It publishes
nothing itself: the image it builds is loaded into the runner's own daemon,
because what it asks is whether the stack runs.

Two jobs follow it that check nothing at all. Once everything above them is
green they build the image again on two native runners — `amd64` and `arm64` —
push both by digest and merge them into one manifest index under `:main` and
`:sha-<commit>`, which are the names
[`deploy/.env.example`](../deploy/.env.example) gives an installation that wants
to follow the trunk. They run last because `:main` means "the trunk, green", and
it would mean nothing said before the rest of that file had finished. No
`VERSION` is passed: `Directory.Build.props` keeps `0.0.0-dev` for a build nobody
tagged, and a trunk build is not a release.

**A release is a tag, and `.github/workflows/release.yml` is the only place a
version number comes from.** `v1.2.3` — or `v1.2.3-rc.1` — is checked for being
a release tag at all, then checked for naming a commit CI has a green run for,
because a tag can be put on any commit and ADR 0001's trunk discipline is worth
nothing if a release can step around it. What comes out is the same image with
`VERSION` from the tag, named `:1.2.3` and — for a stable release only —
`:latest`, and the CLI cross-compiled from one runner for macOS and Linux on both
architectures with a `checksums.txt` beside it. No Windows binary
([§7](../Specification.md#7-explicitly-not-in-the-mvp)); WSL runs the Linux one.
Both halves take the same string, the .NET side through `-p:Version=` and the Go
side through a linker flag, so the version exchange between a CLI and an instance
cannot report skew between two halves of one release.

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

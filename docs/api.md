# The HTTP API

The HTTP API is the only way into this product and the only way out of it
([Specification §9](../Specification.md#9-technical-guardrails)). The web
application is a client of it exactly as the CLI is; there is no privileged side
door for either.

This file says what a schema cannot: how a client finds out what it is talking
to, what a refusal looks like, and which rules hold across every endpoint. What
each endpoint takes and returns is
[`api/openapi.json`](./api/openapi.json), which is generated from the endpoints
themselves and checked in
([ADR 0006](./adr/0006-the-contract-is-checked-in-and-the-web-client-is-generated-from-it.md)).

**Most of it does not exist yet.** What has landed is the contract itself — the
version in the path, the handshake, the error shape and the document — identity:
the first run, signing in, the device-code login and token management — the
authorization every endpoint after it is enforced by — the catalogue: projects
and environments, with the change log that every write path since has been in —
the secrets surface itself — the change log and the value history, read and
rolled back — what deletion means at the end: purge, and the deadline that
arrives on its own — and the people of the organization, who arrived with the
screens that needed them. The CLI, the rest of the web application and the
deployment have their own tickets, and this file is kept accurate as each lands.

## Where the endpoints are

```
/api/v1/instance                 whether this instance has been started, and starting it
/api/v1/sessions                 signing in; /sessions/current signs out
/api/v1/me                       who the caller turned out to be
/api/v1/device/authorizations    beginning a device-code login
/api/v1/device/tokens            polling one
/api/v1/tokens                   creating, listing and revoking tokens
/api/v1/users                    the people of the organization
/api/v1/invitations              the links that invite them, and accepting one
/api/v1/organization             what this organization is called
/api/v1/projects                 the catalogue, by name
/api/v1/projects/…/secrets       names and status; one value at a time
/api/v1/projects/…/missing       keys this environment has not got and its siblings have
/api/v1/changes                  what was changed, by whom, and of what kind
/api/v1/projects/…/access        who has read a key, first and last
/device                          the page a human confirms a login on

/api/handshake       what this instance is and what it serves
/problems            every refusal this instance can make
/problems/<code>     one of them
/openapi/v1.json     the contract
```

The version is the second segment of every path a client calls
([ADR 0005](./adr/0005-the-api-carries-its-version-in-the-path.md)). Four paths
sit outside it because they are what a client reads *before* it knows whether it
and the instance agree on anything, and one — `/device` — because it is a page for
a person rather than part of the contract.

## The version exchange

Two versions are in play and they are not the same thing:

| | What it is | Where it comes from |
| --- | --- | --- |
| **Release** | Which build this instance is — `0.4.1`, or `0.0.0-dev` for an untagged one | The tag a release was cut from |
| **Contract version** | Which shape the API has — `v1` | The path, and `info.version` in the document |

`GET /api/handshake` answers both, and needs no token:

```json
{
  "product": "vaultaffe",
  "release": "0.0.0-dev",
  "apiVersions": ["v1"],
  "minimumClient": "0.1.0"
}
```

- `product` is always `vaultaffe`. A client pointed at the wrong host finds that
  out here rather than three requests later.
- `apiVersions` is what this instance serves. A client that needs one not in the
  list is **too new** for this instance and says so — it does not try the request
  and interpret the failure.
- `minimumClient` is the oldest client release this instance still answers. A
  client below it is **too old** and says so.

Beyond the handshake, every request and every response carries a version:

| Header | Direction | What it means |
| --- | --- | --- |
| `Vaultaffe-Client` | request | The client's release, e.g. `0.4.1`. Optional; a browser sends none. |
| `Vaultaffe-Version` | response | This instance's release. On every answer, including refusals and failures. |
| `Vaultaffe-Claim` | request | An unclaimed instance's claim secret. On `POST /api/v1/instance` and nowhere else. |

A `Vaultaffe-Client` below `minimumClient` is refused with `client-too-old`
before it reaches an endpoint, and one that is not a version at all with
`client-version-unreadable`. That is the whole point of the exchange: being too
old has to arrive as a sentence, not as a field that failed to parse
([§6.3](../Specification.md#63-operations)).

`0.0.0` is never refused for being old. It is what an untagged build calls itself,
and a working copy has nothing to upgrade to.

## Getting in

Every request but the handshake, the problem catalogue, the document, the first
run, a sign-in and the two device-login endpoints carries a token:

```
Authorization: Bearer vaultaffe_session_…
```

All three token kinds arrive that way, and which one it is, is read from the
prefix before anything is looked up ([ADR 0004](./adr/0004-the-token-format-and-the-envelope.md)).
A request with **no** token is nobody, and an endpoint that needs a caller refuses
it. A request with a token that does not authenticate — unknown, revoked, expired,
malformed — is refused before it reaches an endpoint, with one sentence for all
four: which of them it is, is information about a credential the caller does not
hold.

### The first run

`GET /api/v1/instance` says whether this instance has been started, without a
token, and whether the first run wants a claim secret — `needsClaim`, which is
what lets a client put the field on the screen. It says that one is required and
nothing whatsoever about it.

`POST /api/v1/instance` starts it: the default organization is created, the first
user becomes its administrator, and the answer carries a session token. It
authenticates nobody, because there is nobody yet, and it refuses the second
time.

**It does require this instance's claim secret**, in `Vaultaffe-Claim`
([ADR 0019](./adr/0019-an-unclaimed-instance-holds-its-own-claim-secret.md)). The
instance generates one for itself before it serves anything and writes it to its
own log at every start until somebody claims it, so that the window between
`docker compose up` and the first sign-in belongs to whoever can read that log
rather than to whoever reaches the port first. It is a header rather than a field
of the body because it is a credential and not a property of the organization
being made — and it is described here rather than in the contract, like
`Authorization` and `Vaultaffe-Client`.

The two refusals are ordered, and the order is the point. An instance that has
already been started answers `already-started` **before** the claim secret is
looked at, so a running instance never says whether a guess was right. An
unstarted one answers `claim-refused` for a secret that is missing and for one
that is wrong, in constant time and without ever echoing what was presented.

The first run **deletes** the claim secret in the same transaction that consumes
it: a started instance holds no working credential nobody knows about.

### Signing in

`POST /api/v1/sessions` takes an email address and a password and answers a
session token. **The token appears in that answer and nowhere else, ever.** A
wrong password and an address nobody here has are the same refusal, and take the
same time.

`DELETE /api/v1/sessions/current` revokes the token the request came in under. The
row stays — revoked rather than deleted, so that everything it ever signed in the
change log keeps an author.

`GET /api/v1/me` answers who the caller is: the organization, the person — their
name and the address they sign in with — the token and its scopes. `PATCH
/api/v1/me` changes that person's **own name**, `POST /api/v1/me/email` the
**address they sign in with**, and `POST /api/v1/me/password` their own password;
the last two take the password the caller already has.

All three are refused for anything but a session, and refused inside the act
rather than declared on the endpoint: changing your own name is not one of the
short list of §6.4, it is nobody's administration but your own. What is refused is
a service or agent token acting for the person accountable for it, exactly as
signing out is — taking somebody's sign-in away from inside a process they handed
a credential to is not what the credential was for.

**Changing your own password ends every other session of yours** and keeps the
one that asked. Somebody changing a password because they think it is known
elsewhere expects exactly that, and somebody who has just proved they know the
current one is not who it is protecting them from. Their service and agent tokens
are untouched. The current password is asked for so that a session left open on a
borrowed machine is not enough to lock its owner out; the wrong one is
`unauthenticated` and says nothing else.

`GET /api/v1/organization` answers what this organization is called, to any
caller — a token knows which organization it is in the moment it authenticates,
and a screen needs the name for its header. `PATCH /api/v1/organization` renames
it, and that is an administrator's.

### The device-code login

The only login that works where there is no browser on the machine asking — an SSH
session, a CI job, a container, an agent's sandbox
([§6.2](../Specification.md#62-cli)).

1. `POST /api/v1/device/authorizations` answers a **device code** the client keeps
   and a **user code** it prints, with `verificationUri`, `expiresInSeconds` and
   `intervalSeconds`. The user code is eight consonants as `XXXX-XXXX`: no vowel,
   so it is never a word, and no digit, so none of `0/O`, `1/I`, `5/S` or `2/Z`
   has a second half to be confused with.

   **`verificationUri` and `verificationUriComplete` are relative to the
   instance** — `/device` and `/device?code=XXXX-XXXX`. A client resolves them
   against the address it just called, which it has; the instance does not,
   because it stands behind a proxy and would have to be told its own public
   name to build one. A client that prints one of them to a person has to join
   the two halves first: a path with no host is not something anybody can open.
2. A human opens `/device` on any machine, types the code and their password, and
   confirms. That page asks for a password every time and holds no session
   ([ADR 0008](./adr/0008-a-session-is-a-token-and-the-only-page-asks-for-a-password.md)).
3. `POST /api/v1/device/tokens` with the device code answers the session once a
   human has confirmed. Until then it refuses, and **which refusal it is, is the
   whole protocol**: `device-pending` means keep polling; `device-denied`,
   `device-expired` and `not-found` mean stop.

A device code hands over one token and never a second: the poll that collects it
marks the login redeemed, so one left behind in a CI log is worth nothing to
whoever finds it.

### What a caller may do

[Specification §6.4](../Specification.md#64-permissions-in-the-mvp) is
deliberately trivial for people and precise for tokens, and this is the whole of
it.

**Every user of an organization sees and changes everything in it.** There is no
role model beyond who may administer the organization. A session token therefore
reaches the whole organization with every scope, and narrowing it would be
narrowing a person.

**The granularity is the tokens'.** A service or an agent token carries a
**binding** — projects and environments — and a **scope set**: `names`, `read`,
`write`, `delete`. Both are enforced, and each has its own refusal because each
has its own remedy:

- `insufficient-scope` — the token is pointed at the right place and is missing a
  scope. It carries `requiredScopes` and `grantedScopes`, so that a caller that
  cannot see its own token still learns what it would have needed. A human issues
  a wider one; scopes are set when a token is created.
- `out-of-reach` — the token is bound elsewhere. It carries `projectId` and
  `environmentId`, which are not secret, so a caller can tell being pointed at
  the wrong project from being pointed at the wrong environment of the right one.

Naming a project without an environment asks whether the token reaches into that
project at all, and a token bound to one environment of it does. A token with no
binding reaches the whole organization, which is what an agent token gets unless
the human narrows it: **the point is attribution, not restriction.**

**Human-only actions** are the short list where an agent's mistake could not be
undone or the output is itself a secret: purging value history or deleted
objects, creating and revoking tokens, and administering the organization and its
users. **Exporting an environment is on the list too.** §6.4 does not enumerate it and
§6.2 states it directly, and it belongs there for the list's own second reason:
the output is itself a secret — every value of an environment at once.

They are refused with `human-only` for every token that is not a session,
whatever its scopes — being human-only is not a permission a token can be given.

The document carries `humanAction`: one of `purge`, `create-token`,
`revoke-token`, `administer-organization`, `export`.

```json
{
  "type": "/problems/human-only",
  "title": "This action is reserved for a person",
  "status": 403,
  "detail": "Creating a token is reserved for a person: the answer is itself a secret, and one created here would be printed into this context. Hand it to a human, who creates it under their own session and gives you the value.",
  "code": "human-only",
  "humanAction": "create-token"
}
```

**The refusal names the action and never a command** — the CLI renders
`humanAction` as the command it has, the web UI as the screen it has
([ADR 0010](./adr/0010-a-refusal-names-the-action-and-the-client-names-the-command.md)).
A server that spelled out a command would be spelling one from a release it
cannot see.

An endpoint says what it needs as a declaration on the endpoint, and one
middleware enforces every such declaration after routing and before the handler —
so a refused request never reaches the act, and nothing it would have written is
written. What the binding is checked against depends on which project a request
names, so that half is asked by the act, through the same object.

### Tokens

`POST /api/v1/tokens` creates a `service` or an `agent` token — a session comes
from signing in, not from being created. `GET /api/v1/tokens` lists every token of
the organization, newest first, revoked ones included and sessions among them.
`DELETE /api/v1/tokens/{id}` revokes one.

**The value is in the answer that created it and in no listing, ever.** Creating
and revoking are human-only: a token is itself a secret, and one an agent created
through the CLI would be printed to stdout and thus into its own context
([§6.1](../Specification.md#61-web-ui)). Listing is not — a revocation list an
agent cannot read is not one.

Scopes travel as words — `["names", "read", "write", "delete"]` — and not as the
integer of flags the column holds, so that a client never has to know which bits
those are. Omitting them means the default of that kind: **everything** for an
agent token, because the point is attribution and not restriction
([§6.4](../Specification.md#64-permissions-in-the-mvp)), and `names` plus `read`
for a service token. Bindings omitted or empty mean the whole organization.

## The people of the organization

The instance sends **no email** ([§6.1](../Specification.md#61-web-ui)), so
everything here is something a person does and hands over. That is the price of
having no external dependency to operate ([§4](../Specification.md#4-guiding-principles)),
and for teams of this size the right one.

```
GET    /api/v1/users                        who is here, oldest first
POST   /api/v1/users/{id}/password          an administrator sets one
POST   /api/v1/users/{id}/email             an administrator changes a login name
POST   /api/v1/users/{id}/deactivate        take somebody out of the organization
POST   /api/v1/users/{id}/reactivate        put them back

POST   /api/v1/invitations                  write one out — its link appears once
GET    /api/v1/invitations                  every invitation, in whatever state
DELETE /api/v1/invitations/{id}             withdraw one nobody has used
POST   /api/v1/invitations/offer            what a link is for
POST   /api/v1/invitations/acceptance       accept it: a password, and a session
```

**An invitation is a credential, not a message**
([ADR 0015](./adr/0015-an-invitation-is-a-credential-in-a-link.md)). The answer
that created it carries a **relative link with the code in the fragment** —
`/invite#…` — and no listing carries one afterwards, exactly as a token's value
appears once and never again. It is good for 72 hours, this product's one window,
and it is spendable once.

The code travels in the **fragment** of the link and in the **body** of the two
requests that use it, never in a path or a query string: a fragment does not leave
the browser, and this instance serves the web application itself — a code in the
request line would land in its own access log. `POST /invitations/offer` and
`POST /invitations/acceptance` are therefore both `POST`, and both are
unauthenticated, because the person holding a link has no identity here until
accepting gives them one. Accepting takes a password and a name; the **address and
the administrator flag are the invitation's** and not the request's.

For a code this instance wrote, the answer carries the state — `open`, `accepted`,
`withdrawn`, `expired` — and a code nothing here issued is `not-found` and learns
nothing. That is deliberately unlike the one sentence a bad token gets: whoever
presents an invitation code is holding it, and "this was already used" is what
tells them to ask for a new one.

**A password reset is an administrator's**, and it **ends every session that
person had** — a reset that leaves the sessions opened with the old password
working is not one. Their service and agent tokens are untouched: those never
depended on the password.

**An address is a login name, and changing one is an administrator's too.** It is
not somewhere this instance sends anything — it sends nothing — it is what
somebody types to sign in, and people marry, change their name, or move to another
address at the same company. `POST /users/{id}/email` moves it, and moves nothing
else: the password hash carries its own salt and never depended on the address,
and every token names its person by id, so **their sessions stay**. That is the
one line between this and a reset, which ends them because a password somebody
else may know is worth nothing while the sessions opened with it still work.

There are **two ways to that column, and one rule behind them**. `POST
/users/{id}/email` is an administrator's, and it is what repairs a lock-out —
somebody has to be able to, because a mistyped address cannot correct itself: the
correction needs the sign-in that the mistyped address just took away, and there
is no mail to send a way back through. `POST /me/email` is the way that needs
nobody, and it **takes the password the caller already has**, exactly as changing
a password does and for the same reason: a session left open on a borrowed machine
should not be enough to take its owner's sign-in away. A wrong one is
`unauthenticated` and says nothing else.

The address is stored in **one spelling**, lower-case and trimmed, so a change to
another spelling of what somebody already has is not a conflict. An address
somebody here already signs in with is `name-taken`; an address that is not one is
a validation failure naming `email`. Both ways answer the person as every listing
shows them, and `GET /me` carries the address too — a screen that offers to change
it has to be able to show it.

**Deactivating somebody takes every token of theirs with it**, their sessions and
the agent tokens they are accountable for included — a person who is out of the
organization does not go on acting in it through something they left running. The
row stays, so everything they ever changed keeps an author, and reactivating is
the undo. **Nobody deactivates themselves**: it is the one way to leave an
instance with nothing that can administer it.

Everything on this surface is human-only with `humanAction: administer-organization`,
and everything that changes something also needs the person to **be an
administrator** — the one line §6.4 draws between two people. The two halves are
refused differently on purpose: a token is told the action is a person's, because
the remedy is to hand it to one; a person who is not an administrator is told they
are not, because the remedy is to ask one. Reading the list of people is
human-only and not an administrator's — everybody in an organization sees
everybody in it.

## Projects and environments

Addressed **by name**, at both levels:

```
GET    /api/v1/projects                                        every project this token reaches
POST   /api/v1/projects                                        create one
GET    /api/v1/projects/{project}                              one, with its environments
PATCH  /api/v1/projects/{project}                              rename it
DELETE /api/v1/projects/{project}                              delete it, recoverably
POST   /api/v1/projects/{project}/restore                      bring it back

GET    /api/v1/projects/{project}/environments                 every environment this token reaches
POST   /api/v1/projects/{project}/environments                 add one
PATCH  /api/v1/projects/{project}/environments/{environment}   rename it
DELETE /api/v1/projects/{project}/environments/{environment}   delete it, recoverably
POST   /api/v1/projects/{project}/environments/{environment}/restore
```

A name is what a person types, what a `vaultaffe://` reference carries and what
the CLI's directory binding holds
([§5](../Specification.md#5-core-concepts)), so it is what the path carries too.
The id is in every answer, because a token binding is by id and so is the
`out-of-reach` refusal.

**Creating a project creates its environments.** `dev`, `staging` and `prod`
unless the request names others; an explicit empty list creates none. Environment
names are free, and an extra `dev-someone` is an ordinary environment offering
**no** privacy — everybody in the organization sees it
([§6.4](../Specification.md#64-permissions-in-the-mvp)). Both names are lower-case
and narrow, because both appear inside a reference
([ADR 0003](./adr/0003-a-name-inside-a-reference-is-narrow-and-lower-case.md)).

**Deleting is recoverable and nothing cascades.** `DELETE` sets the moment and the
object leaves every listing; `restore` brings it back inside 72 hours, and
`not-recoverable` is the answer after that — the row is still there, so this is a
refusal rather than a `not-found`. Deleting a project does **not** delete its
environments: the subtree is retained and restored *as one*, so an environment
deleted before its project stays deleted when the project comes back.

**A deleted object keeps its name.** Creating something of that name is
`name-taken` with `takenBySomethingDeleted: true` — which is the answer to "I
deleted it, why can I not recreate it".

**A binding narrows a listing and refuses a change.** A token bound to one project
sees that project in `GET /projects` and `out-of-reach` on any other; a token
bound to one environment sees that environment and cannot create the one beside
it, or rename or delete the project its binding was supposed to narrow it to.
Creating a project needs a token that reaches the whole organization.

Scopes: `names` to read the catalogue, `write` to create or rename, `delete` to
delete **and to restore** — restoring is the undo of a deletion and travels with
it ([§5](../Specification.md#5-core-concepts)). None of it is human-only:
creating projects and environments is explicitly something an agent may do.

**Every one of these changes is in the change log**, with the acting identity and
its type ([§6.5](../Specification.md#65-logging-and-history)) — the log belongs to
the first write path rather than to a later ticket, and an entry is committed by
the same transaction as the change it describes. Reading it is `GET`-able with its
own ticket; writing it starts here.

## Secrets

The product itself, and the two endpoints that are not the same endpoint:

```
GET    …/environments/{environment}/secrets           names and status. Never a value.
GET    …/environments/{environment}/secrets/{KEY}     one value, by name
PUT    …/environments/{environment}/secrets/{KEY}     write one, or make a placeholder
DELETE …/environments/{environment}/secrets/{KEY}     delete it, recoverably
POST   …/environments/{environment}/secrets/{KEY}/restore
POST   …/environments/{environment}/import            a .env in
GET    …/environments/{environment}/export            every value out, for a person
```

All of them under `/api/v1/projects/{project}`. Import and export hang off the
environment rather than off `secrets/` because a literal route beats a parameter
and matching ignores case — `secrets/export` beside `secrets/{name}` would shadow
a secret somebody called `EXPORT`.

**Names and values are two endpoints and two scopes.** The listing answers
`[{name, status}]` where status is `set` or `empty`, and needs `names`. Reading a
value is a second request, one key at a time, and needs `read`. That split is what
lets an agent be issued a token that can see which keys exist and which a human
still has to fill, and cannot read one — the normal case
([§4](../Specification.md#4-guiding-principles),
[§8](../Specification.md#8-agentic-use--the-guiding-scenarios)) — and it is the
shape a later MCP server needs, where a tool returning a value must not exist.

**Writing.** `PUT` takes `{"value": …}`. `{"value": null}` asks for an **empty
placeholder** — the state that means a human still has to do something, which
stops `run` and which the listing reports. An empty *string* is refused: one
standing in for the other is the bug the placeholder exists to prevent.

**Overwriting is explicit.** A `PUT` over a key that already holds a value is
`replace-required` unless the request says `"replace": true`. Filling a
placeholder needs nothing. **Nothing is trimmed here**: §6.2 strips exactly one
trailing newline and does it in the CLI, where the bytes came off a pipe and
`--raw` can say not to.

**The answer never carries the value back.** A confirmation that echoed it would
put into a transcript exactly what piping a vendor's command straight in kept out
of one.

**Superseded values are kept, briefly.** Five versions or 72 hours, whichever is
reached first, and what falls out is deleted rather than tombstoned — every
retained old value is usually a still-valid credential
([§6.5](../Specification.md#65-logging-and-history)). The bounds are applied on
the write that creates a version, so a history is never over its limit waiting for
a sweep.

**Import** takes a whole `.env` and applies all of it or none of it. The answer
names keys — `created`, `filled`, `replaced`, `unchanged`, `skipped` with a
reason, `unreadable` with a line number — and never values, so an agent can
migrate a file it never displays. Migration is a success criterion
([§11](../Specification.md#11-success-criteria-for-the-mvp)) and must not be
UI-only. `KEY=` creates an empty placeholder; a key already holding a value is
skipped unless `"replace": true`.

The format is written down rather than inferred, because there is no standard for
it: `#` comments and blank lines are skipped, a leading `export` is allowed,
single quotes are literal, double quotes may span lines and understand `
`,
`
`, `	`, `\` and `"`, and an unquoted value keeps its `#` — a trailing
comment cannot be told from a password containing one. Export always quotes and
escapes, so what it writes is something import reads.

**Export is for a person.** It writes every value of an environment in plaintext,
which is precisely the contradiction `inject` was rejected for
([§7](../Specification.md#7-explicitly-not-in-the-mvp)). It stays because a way
back out is part of being trustworthy, and it is `human-only` with
`humanAction: "export"` — an agent or service token is refused however many scopes
it carries. Nothing is recorded for it: the change log holds mutations, not reads.

### The missing-key notice

Keys this environment has not got and its siblings have
([§6.1](../Specification.md#61-web-ui)). **It displays; nothing here writes a
secret.**

```
GET    …/environments/{environment}/missing
POST   …/environments/{environment}/missing/{KEY}/dismissal    stop saying it here
DELETE …/environments/{environment}/missing/{KEY}/dismissal    say it again
```

**The rule is a majority, and that is the whole design.** A key is missing here
when **more than half** of this project's other environments hold it. Held
pairwise — "present in any other environment" — the notice would report every key
`prod` legitimately has alone, and would hold three shared environments against a
personal `dev-alex`; environment names are free
([§5](../Specification.md#5-core-concepts)) and nothing in the model says which
one is complete, so a notice built on a single comparison partner is one somebody
clicks away, and then it is worthless on the day it is right. A majority needs no
new field and no declared reference environment: a personal environment is one
voice among the others, and with two environments a majority of one is the other
one. A tie is not a majority.

Each line answers `{name, presentIn, dismissedAt}` — `presentIn` names the
environments that have the key, so a reader can see the reason rather than take
it. A placeholder counts as present; a deleted secret counts as absent.

**It reaches only where the caller does.** The comparison runs over the
environments the token's binding covers, so a notice never tells a token which
keys exist somewhere it may not touch — and a token bound to one environment gets
an empty notice rather than a filtered one, because there is nothing left to
compare with.

**Dismissal is per key and per environment**, needs `write`, and is the one thing
this notice offers besides showing. Dismissing twice is dismissing once. Reading
needs `names`, because names is all this ever says. A dismissal appears in no
change log: it changes what a screen says, not what the vault holds.

## The change log, the value history and the access summary

Three things that get lumped together and have completely different risk profiles
([§6.5](../Specification.md#65-logging-and-history)). The log holds no value at
all; the history holds a few under tight bounds and hands none of them out; the
summary holds two moments per identity and is not an execution history.

```
GET  /api/v1/changes                                     what was changed, by whom
GET  …/environments/{environment}/secrets/{KEY}/versions  what it used to hold, and when
POST …/environments/{environment}/secrets/{KEY}/rollback  put one back
GET  …/environments/{environment}/secrets/{KEY}/access    who has read it, first and last
```

**The log never contains a value**, not even as a diff. An entry is a moment, an
action, the project, environment and secret name it happened to, and the acting
identity **with its type** — `human-session`, `service-token` or `agent-token`.
That last field is what the log exists for: with writing agents, what kind of
thing acted is the interesting question, and it is why an agent gets a token of
its own on day one. Doppler puts the old and new value in the log and had to bolt
on a redaction that does not truly delete; we follow AWS.

Actions are words: `created`, `renamed`, `value-set`, `value-rolled-back`,
`placeholder-created`, `deleted`, `restored`, `purged`.

**Reads are not in it.** `run` reads values, but a successful read does not prove
an application started, and the log records mutations rather than every
invocation. What is known about reads is the access summary below, and it is
deliberately less than a log.

**A reader names what they are asking about.** The entries carry names and no
ids, so a binding cannot narrow the answer afterwards — instead `project`,
`environment` and `secret` are the filter, and the same authority as everywhere
else says whether the caller reaches it. No `project` needs a token that reaches
the whole organization; a bound token names its project, and a token bound to one
environment names that too. `environment` without `project`, or `secret` without
`environment`, is a validation failure rather than a silently wider search: a
filter matching every project's `prod` is not what anybody meant.

`limit` (default 50, at most 200) and `offset` page it, newest first, over a
total order of moment and then id — one act writes several entries at the same
instant, and a page boundary in the middle of them has to fall in the same place
twice. Entries arriving while a reader pages will shift it; that is what an
append-only log does, and it is not worth a cursor here.

**The version listing says when, and never what.** Id, `writtenAt`, `replacedAt`
and `expiresAt` — no value, because five old credentials in one answer would be
the bulk disclosure the rest of this product spends every decision avoiding, and
nothing needs it.

**The access summary is two moments per identity, and that is the whole
promise.** Each line is an identity with its type, `firstAt` and `lastAt` — and
no count, because a count would be the beginning of the execution history this
product does not have. A successful read does not prove that an application
started; two moments are what can be said honestly, so two moments are what is
stored. Reading a key a thousand times is still one line, and the table is
bounded by how many identities an organization has rather than by how often
anything runs.

Reading the summary needs `names`, like the change log beside it: it says who
read a key and when, never what. A read of an **empty placeholder** counts —
somebody asked for the key, which is what this is about — and an **export**
counts as a read of every key in the environment, because leaving it out would
make the one read that takes everything the one read nothing knows about.

The summary is written on the read itself, in one `on conflict … do update`
statement rather than a read followed by a write: two processes starting at the
same moment under the same token must not race each other into a duplicate. It
commits by itself and is best effort — a summary that could not be written does
not take a value read down with it. Purging a secret takes its summary with it,
the way it takes the values it used to hold.

**Rollback is a write like any other and available to agents.** An agent that
wrecks a value overnight is exactly who needs an undo button. It costs `write`
and nothing else — there is no `replace` to give, because asking to undo already
says what is meant. `{"versionId": …}` names one; omitted means the value before
this one.

Nothing is opened to do it: every version of a secret is sealed under that
secret's own data key, so a rollback moves ciphertext and never asks the key ring
for anything. The version rolled back to leaves the history, because it is the
current value again, and what it replaced takes its place — one credential is not
counted twice against a bound that exists to hold few of them.

## Deleting, restoring, purging, expiring

Deleting takes something out of listings and use immediately and keeps its last
active state for **72 hours**
([§6.5](../Specification.md#65-logging-and-history)). Three things can end that:
a restore, a purge, or the deadline.

```
DELETE …/projects/{project}                      delete, recoverably
POST   …/projects/{project}/restore              bring it back
POST   …/projects/{project}/purge                remove it now — human sessions only
DELETE …/secrets/{KEY}/versions                  remove what it used to hold — human sessions only
GET    …?deleted=true                            what is recoverable, at any of the three levels
```

`purge` exists at all three levels and `?deleted=true` on all three listings.

**A container keeps its subtree, and gets it back.** Deleting a project or an
environment retains its active descendants and their current values as one
recoverable subtree and never cascades into permanent deletion. Descendants
deleted **before** it keep their original deadlines and stay deleted when it comes
back: the subtree returns as it was, not as it would have been. Value versions
keep their own five-version and 72-hour bounds; a deletion does not restart those
clocks. Repeating a deletion does not extend the window.

**Names stay reserved for the whole window**, so recreating something cannot
silently replace its recoverable predecessor — `name-taken` with
`takenBySomethingDeleted: true` says which case it is. The name frees itself when
the row goes, whether that is a purge or the deadline.

**Purge is a visible feature, not a support ticket.** After a suspected
compromise the normal expectation is that a key's history genuinely disappears,
and neither Doppler nor AWS offers that cleanly. `DELETE …/secrets/{KEY}/versions`
is that request: it removes everything the key used to hold and keeps the key and
its current value. The other three remove a **deleted** object and everything
retained under it — a purge is the second half of a deletion, not a faster one, so
asking for it on something in use is refused.

**Every purge is human-only**, with `humanAction: "purge"`. It is the one action
that destroys the undo button, and in an agent's hands it would be anti-forensics.
An agent carrying every scope there is is still refused; being human-only is not a
permission a token can be given.

**The deadline removes what nobody purged.** An expiry sweep runs inside the
instance every fifteen minutes — a window nothing enforces is a promise rather
than a window, and a self-hosted product whose safety property depends on somebody
having set up a cron job does not have that property. It removes superseded values
past their window and deleted objects past theirs, subtree included, and writes
nothing to the change log: an entry names the identity that acted, and no identity
acted — a deadline did.

**Neither purge nor expiry removes change-log entries.** That is what keeps the
log able to say what happened to something that no longer exists, and it is why
the log records names rather than keys.

**And the honest part.** A purge reaches this database and nothing else. It does
not reach last night's backup, and this product promises backups
([§6.3](../Specification.md#63-operations)). If a value has to be gone
everywhere, the backups holding it are part of that job and no API call here can
do it for you. No product we looked at says this out loud; it is true of all of
them.

## Refusals

Every error is `application/problem+json`
([RFC 9457](https://www.rfc-editor.org/rfc/rfc9457)):

```json
{
  "type": "/problems/client-too-old",
  "title": "This client is older than this instance accepts",
  "status": 426,
  "detail": "This instance serves clients from 0.1.0 onwards.",
  "code": "client-too-old",
  "minimumClient": "0.1.0"
}
```

**`code` is what a client switches on.** It is the last segment of `type` and
repeats as a member of its own, so that nothing has to take a URI apart to learn
which refusal it is looking at. `title` and `detail` are for a person and are
free to improve; matching on either is a bug waiting for the next wording change.

`type` is relative, which RFC 9457 resolves against the request — so it points at
`/problems/<code>` on the instance that answered, and that path is served. `GET
/problems` lists every code this build can raise, with its status and its title;
an instance is the authority on what it itself refuses.

Some codes carry extra members, as `client-too-old` carries `minimumClient`
above. They are documented with the code.

**A problem document never carries a secret value.** Neither does a log line, and
for the same reason: Specification §6.5 makes a value in either a bug rather than
an untidiness. `detail` says what was refused, not what it was refused about.

### The codes

| Code | Status | When |
| --- | --- | --- |
| `validation` | 400 | A field is missing, malformed or over its limit. Carries `errors`, mapping field to messages. |
| `not-found` | 404 | Nothing by that name. |
| `unauthenticated` | 401 | No token, an unknown token, a revoked one — or the wrong password. |
| `forbidden` | 403 | The caller is authenticated and still may not do this — a person who is not an administrator, or an invitation that cannot be accepted any more. |
| `human-only` | 403 | One of the short list only a person may do. Carries `humanAction`. |
| `insufficient-scope` | 403 | The token is missing a scope. Carries `requiredScopes` and `grantedScopes`. |
| `out-of-reach` | 403 | The token is bound elsewhere. Carries `projectId` and `environmentId`. |
| `already-started` | 409 | The instance already has its first user. |
| `claim-refused` | 403 | The first run presented no claim secret, or not this instance's. Never carries the secret. |
| `name-taken` | 409 | Something of that name is here. Carries `takenBySomethingDeleted`. |
| `not-recoverable` | 410 | Deleted longer ago than the 72-hour window. |
| `replace-required` | 409 | That key holds a value and the request did not say to overwrite. Carries `secretName`. |
| `device-pending` | 400 | Nobody has confirmed that login yet. Keep polling. |
| `device-denied` | 400 | A human refused it. |
| `device-expired` | 400 | Nobody confirmed it in time, or its token was already collected. |
| `unsupported-api-version` | 404 | The path named a contract version this instance does not serve. Carries `apiVersions`. |
| `client-too-old` | 426 | `Vaultaffe-Client` is below `minimumClient`. Carries `client` and `minimumClient`. |
| `client-version-unreadable` | 400 | `Vaultaffe-Client` is not a version. |
| `master-key-mismatch` | 500 | This instance was started with a different master key than the one the value was sealed under — a dump restored without the `.env` beside it. Nothing is damaged and no caller can do anything about it. |
| `sealed-value-damaged` | 500 | The key is right and the stored bytes are not what was written: a row changed underneath the instance, or a layout this build does not know. |
| `internal` | 500 | Something went wrong here. The reason is in this instance's log and deliberately not in the document. |

Only what something already refuses is in that table, and the refusals of the
secrets surface arrive with the endpoints that make them — a code nothing raises
is a promise to a client that nothing keeps.

The three five-hundreds are worth reading together. `internal` says nothing
beyond its title on purpose: an exception message is a place a value could leak
into (§6.5), so it goes to the log and not into the document. The other two carry
a sentence because they are not exceptions about a request at all — they are the
instance saying which half of an installation is wrong, and the person who has to
act on that is an operator who is not reading the log yet. The three authorization codes are in
it because the enforcement is: `human-only` is what token management answers an
agent with today, and `insufficient-scope` and `out-of-reach` are raised by the
one place that decides both, which every endpoint that names a project will ask.

## The document

`docs/api/openapi.json` is generated from the endpoint definitions and checked
in. Regenerate it with the change that moved it:

```sh
VAULTAFFE_CAPTURE_CONTRACT=1 dotnet test tests/Vaultaffe.IntegrationTests
```

`ContractTests` compares what a running instance serves against the file, and CI
does the same from the other side — it starts the installation, captures the
document and fails on a diff. A response shape that changed without the document
changing with it is red twice.

`info.version` in the document is `v1`, the contract's version, not the
instance's release: the API carries its version in the path, so a release does
not change the document.

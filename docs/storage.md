# The Data Model

Twelve tables, and the rules the database itself holds. What each concept *means*
is [Specification §5](../Specification.md#5-core-concepts); this says how it is
stored and which of the promises are constraints rather than intentions.

`Vaultaffe.Infrastructure/Persistence` is the one place that declares schema —
one configuration per table, and migrations that apply themselves when an
instance starts ([Specification §6.3](../Specification.md#63-operations)). A
migration is added with the pinned tool in `.config/dotnet-tools.json`:

```sh
dotnet tool restore
dotnet ef migrations add <Name> --project src/Vaultaffe.Infrastructure
```

No running instance is needed for that, and none should be: the factory in
`Persistence/DesignTimeDbContextFactory.cs` builds the model with the Npgsql
provider and never opens a connection. A configuration changed without a
migration to carry it is caught by `SchemaTests`, not by somebody noticing.

**Run `dotnet format` after generating one.** EF writes migrations with a byte
order mark and a block namespace, and the format job in CI is not going to make
an exception for generated code that is checked in like any other file.

## The tables

```
organization
├─ app_user                 (organization_id, email unique across the instance)
├─ invitation               (organization_id, the hash of the code in a link)
└─ project                  (organization_id, name unique per organization)
   └─ environment           (project_id, name unique per project)
      ├─ dismissed_key      (environment_id, name unique per environment)
      └─ secret             (environment_id, name unique per environment)
         ├─ secret_value_version
         └─ secret_access    (one row per identity, two moments)

token                       (organization_id, user_id)
└─ token_binding            (a project, or one environment of it)

device_authorization        (one handover in progress: a login, or an enrollment)
└─ enrollment_binding       (the reach a person agreed to, before there is a token)

change_log_entry            (points at nothing)

instance_claim              (no organization: there is none yet)
```

| Table | What it holds |
| --- | --- |
| `organization` | The tenancy boundary. Exactly one row in the MVP. |
| `app_user` | A person: the address they sign in with, their password hash, whether they administer the organization, and since when they are out of it. |
| `invitation` | Somebody an administrator asked to join: an address, what accepting makes them, and the hash of the code in the link. |
| `project` | An application or service. |
| `environment` | `dev`, `staging`, `prod` — one level, no branch or personal configs. |
| `secret` | A key, and its current value sealed under the secret's data key. |
| `secret_value_version` | Values this secret used to hold, tightly bounded. |
| `secret_access` | First and last use of this secret by one identity. Two moments, never a count. |
| `dismissed_key` | A key the missing-key notice was told not to mention in this environment again. |
| `token` | A credential: kind, scope set, and the hash of a value shown once. A rotation adds the successor beside the row it replaces rather than writing over it, so the day the value in circulation changed is a fact this table holds ([ADR 0022](./adr/0022-an-agent-renews-its-own-token.md)). |
| `token_binding` | What a token may touch. No rows means the whole organization. |
| `device_authorization` | One handover in progress — a `vaultaffe login` or a `vaultaffe enroll`: two codes, what it produces, and what has happened to it. |
| `enrollment_binding` | What a person agreed an enrollment may reach, while there is still no token to hang it on. |
| `change_log_entry` | What was done, by whom, and of what type — never a value. Administration is in it too, with no place and an `about_name`. |
| `instance_claim` | The claim secret an unstarted instance is claimed with. At most one row, and none once the first run has consumed it. |

The one row that holds a preference rather than a fact about the vault is
`dismissed_key`, and it is deliberately the thinnest table here: an environment,
a key, and when somebody said "not here". It has no `deleted_at`, because
withdrawing a dismissal deletes the row and nothing about that needs to be
recoverable — the way back is to say it again. Purging an environment takes its
dismissals with it, explicitly and in the same act, the way everything else
between the containers goes.

## Every table carries the organization

[Specification §9](../Specification.md#9-technical-guardrails): every domain
table has an `organization_id`, and all queries are organization-filtered in one
central place rather than in every handler. That place is
`VaultaffeDbContext.OnModelCreating`, which walks the model and puts the same
filter on every entity implementing `IBelongToAnOrganization` — so a table added
tomorrow is filtered by existing, not by someone remembering to say so.

The column is carried even where the parent could be walked to: a secret's
environment knows its project and the project knows its organization. A join the
filter had to walk would be a filter each query could get wrong on its own.

There are two exceptions, and `TenancyTests` lists both by name so that a third
cannot arrive by somebody forgetting the rule. It reads the model and fails if
any other table is unfiltered, and puts a second organization in the database to
prove the filter holds against real rows.

`organization` is an exception of **shape**: a row of that table *is* an
organization and cannot carry a key to one, so it is filtered by its own.

`instance_claim` is an exception of **time**. It holds the claim secret the first
run has to present ([ADR 0019](./adr/0019-an-unclaimed-instance-holds-its-own-claim-secret.md)),
and it exists precisely in the window when there is no organization to belong to.
It is the one table whose row is stored as it is read rather than as a hash,
because the instance has to print it again at every start until somebody claims
it — and the first run deletes it in the same transaction that consumes it, so a
started instance never carries one.

**A caller inside no organization sees nothing.** The filter compares against a
`Guid?`, and null never equals a column. That is the safe end of the comparison;
the first run reaches past it with an explicit `IgnoreQueryFilters()`.

## A value never rests unsealed, and never half-sealed

Envelope encryption from day one
([Specification §6.3](../Specification.md#63-operations)):

- `secret.wrapped_data_key` — **one data key per secret**, wrapped by the
  instance master key. It is kept for the secret's lifetime, across every write.
- `secret.nonce`, `secret.ciphertext` — the current value.
- `secret_value_version.nonce`, `.ciphertext` — a superseded value, sealed under
  the **same** data key.

That shape is what makes master-key rotation a rewrap of `wrapped_data_key`
rather than a re-encryption of every value in the instance. A data key that
changed per value would strand the retained versions and undo the whole point.
Rotation is not an MVP feature (§7); the structure that makes it cheap is.

`ck_secret_value_sealed_whole` refuses a row that has a ciphertext without the
nonce and data key it was sealed under — a value nobody could ever open again.

**A secret with no value at all is an empty placeholder**, not a broken row: the
state a human still has to fill, that stops `run` and that the names listing
reports ([§6.2](../Specification.md#62-cli)). It is a state, never an empty
string pretending to be a value.

Both levels are AES-256-GCM, and both blobs start with a format byte so that a
later algorithm is a new byte rather than a guess about what an old row means
([ADR 0004](./adr/0004-the-token-format-and-the-envelope.md)):

```
ciphertext        [format][ciphertext][tag]
wrapped_data_key  [format][master key id][nonce][data key][tag]
```

The master key itself is not in the database and never will be: it is
`Vaultaffe__MasterKey` in the instance environment, 32 bytes in base64, read
once at startup — an instance without a usable one does not start. The four
bytes of `master key id` are derived from that key with HKDF and say which key a
row is under, which is how a half-restored backup produces the sentence *"this
value was sealed under a different master key"* instead of a database that reads
as damaged.

## Deleting keeps the row, and the name

Deleting sets `deleted_at` and nothing else
([Specification §6.5](../Specification.md#65-logging-and-history)). The row stays
out of listings and use for 72 hours, and can be restored. Two consequences are
in the schema rather than in code:

- **The unique indexes do not exclude deleted rows.** A deleted project keeps its
  name reserved for exactly as long as it is recoverable, so recreating it cannot
  silently replace its recoverable predecessor. When the window ends the row is
  removed and the name frees itself — no sweep has to remember to release it.
- **No foreign key cascades between the containers.** Deleting a project never
  cascades into immediate permanent deletion of what hangs underneath; the
  subtree is retained and restored as one. A database cascade would be the
  opposite of that promise.

The cascades are the two things that hang directly off a secret —
`secret_value_version` and `secret_access` — and neither contradicts the window: a
deleted secret keeps its row, so they fire only when that row is genuinely
removed, at a human purge or the end of the window. When a secret is gone, what
it used to hold and what was recorded about reading it have to be gone with it.
That is what a purge means.

The acts do the same thing the schema does: deleting a project sets `deleted_at`
on the project row and touches nothing underneath. The subtree is retained and
restored **as one**, so an environment deleted before its project stays deleted
when the project comes back — which is the state that was there, rather than the
state that would have been. Marking the children too would lose exactly that.

**Still here and still recoverable are two questions.** Nothing removes the row
when the window ends: what removes it is a purge, or the sweep that reads the
deadline. So an object past its window is found and then refused —
`not-recoverable`, not `not-found` — and its name stays reserved until the row
itself is gone.

`ExpirySweep` is that sweep, and it is the one query in this product that steps
past the organization filter for a reason that is not a login: it is the instance
acting rather than a caller, so it is inside no organization and the filter would
answer it with nothing at all. It runs every fifteen minutes inside the
installation rather than as an operator's cron job, because a window nothing
enforces is a promise rather than a window. It removes the containers in the order
the schema demands — there is no cascade between them, on purpose — and leaves
the two that hang off a secret, its versions and its access summary, to the
database that declares them.

A purge is the same removal asked for early by a person, and it goes through the
tracked change tracker rather than `ExecuteDelete` so that the change-log entry
recorded about it commits in the same transaction as the rows it describes.

## The access summary is two moments, and is written on a read

`secret_access` is the one table in this product written by a **read**, and the
one written by hand-rolled SQL. Both follow from what it is
([§6.5](../Specification.md#65-logging-and-history)): first and last use per
identity and secret, and never a row per read.

```sql
insert into secret_access (…) values (…)
on conflict (secret_id, identity_id) do update set
    identity_name = excluded.identity_name,
    last_at = greatest(secret_access.last_at, excluded.last_at)
```

One statement rather than a lookup followed by a write, because two `run`s
starting at the same moment under the same token would otherwise race each other
into a duplicate, and the loser of that race would turn a value read into a 500.
`greatest` is the rule the domain type declares by shape: `first_at` is written
once and never again, `last_at` only ever moves forward.

It commits by itself. A read is not part of a transaction that changes anything,
and a summary that could not be written must not take the value read down with
it — which is the one place in this schema where best effort is the right answer
and is written down as such.

The table is bounded by how many identities an organization has, not by how often
anything runs, and there is **no count column**. A count would be the beginning of
the execution history §6.5 declines to promise: a successful read does not prove
that an application started, and two moments are what can be said honestly. Like
the change log, the identity is an id with its type and name beside it rather
than a foreign key, and there is no column a value could go in.

## Value history is bounded, and the clock is `replaced_at`

Five versions, at most 72 hours, whichever is reached first; what falls out is
deleted rather than tombstoned. Both numbers live in `Domain/Secrets/ValueHistory`
— Specification §6.5 says they are a decision rather than a placeholder, and that
if they turn out wrong they change in one line. That is the line.

The retention clock is `replaced_at`, not `written_at`: a value that stood for a
year and was replaced this morning is this morning's undo. The index
`ix_secret_value_version_secret_replaced_at` is what both the rollback and the
retention sweep read.

**Both bounds are applied on the write that creates a version**, in the same
transaction. A history is therefore never over its limit waiting for a job to run,
and there is nothing for a sweep to catch up on except versions that aged out
without anybody writing again — which is the only case a sweep is actually for.

## The change log has no value column, and no foreign keys

Timestamp, project, environment, secret name, action — and identity along with
**identity type**, because with writing agents that is the interesting question.
No value, not even as a diff. Doppler puts the old and new value into the log and
consequently had to bolt on a redaction which does not truly delete; we follow
AWS instead.

`ChangeLogTests` lists the table's columns and fails when one appears that is not
in that list, and asserts that none of them is `bytea`. Adding a column means
editing that list, which is the point.

The subject is recorded **by name**. Neither a purge nor an expiry removes
change-log entries, so this table outlives the project, environment and secret
it talks about, and a foreign key pointing at a row that is gone would take the
entry with it. `identity_name` is kept for the same reason: the entries of a
token that was revoked — or purged, and whose row is genuinely gone — should
still read as something other than a bare id.

**`about_name` is the fourth name, and it is what makes this one log rather than
two** ([ADR 0020](./adr/0020-one-change-log-and-not-two.md)). An entry with no
`project_name` was done to a person, a token or the organization, and this is
who or what: an address, a token's name, the organization's new name. One column
and not one per kind, because `action` already says which kind of thing the name
is — nothing reads an entry without knowing that.

It holds an **identifier and never a credential**: no password, no token value,
no invitation code. That is the same rule as the missing value column, pointed at
people, and the column list above is what enforces it.

There is no foreign key to `app_user` or to `token` either. The reason is the
one above and one more: the log must not depend on a row staying. A deactivated
person keeps theirs, and a revoked token keeps its own until somebody purges it
— which is exactly the case this column was written for, and the reason a token
can be removed at all.

**What deserves honest documentation** (Specification §6.5): a purge in the
database does not reach into last night's backup, and this product promises
backups (§6.3). If a value has to be gone everywhere, the backups holding it are
part of that job and no API call can do it for you. `docs/api.md` says so where
purge is described; it belongs in the operations guide too, when there is one. No
product we looked at says it out loud, and it is true of all of them.

## A person is `app_user`, and the name is the one compromise

`user` is a reserved word in Postgres, and a table named that would need quoting
in every query for the rest of this product's life. Every other table here is
named after its concept; this is the one that pays a prefix for it.

The address is stored **normalized and lower-case**, because two spellings of one
address are one person to everybody except an index — and a login that depended on
how somebody's keyboard felt that morning is not a login. Surrounding whitespace
goes the same way: it is what a form collected, not what anybody meant. The rule
itself is deliberately loose — one `@`, something on either side, no whitespace —
because this instance sends no mail
([Specification §6.1](../Specification.md#61-web-ui)), so an address here is an
identifier rather than a delivery target, and a strict rule would only refuse
addresses that are perfectly valid. `ck_user_email` holds exactly that much.

`ux_user_email` is unique **across the instance** rather than per organization.
With one organization the two are the same thing; with several they are not, and a
sign-in has only an address to go on — an address belonging to two people would be
a login nobody could resolve.

`password_hash` holds Argon2id in its PHC encoding, which carries the algorithm
and its parameters with the value. Raising the cost later is therefore a new hash
on the next sign-in rather than a migration, and nothing in the schema has to know
which parameters a given row was written under.

`deactivated_at` is null while somebody is in the organization. A moment rather
than a flag, because "since when" is what anybody asks about a person who is no
longer here — and the row stays either way, so everything they ever changed keeps
an author, exactly as a revoked token's entries do
([§6.5](../Specification.md#65-logging-and-history)). Authentication reads it: a
token belonging to somebody who is out authenticates nobody, their sessions and
the agent tokens they are accountable for included.

## An invitation is a credential, so the row holds a hash

`invitation` is the second table that stores the hash of something it can never
read back, and for the same reason the first two do. This instance sends no mail,
so an invitation is a **link an administrator copies and hands over**
([ADR 0015](./adr/0015-an-invitation-is-a-credential-in-a-link.md)) — which makes
it a credential rather than a message, good for 72 hours and spendable once.

`ux_invitation_code_hash` is unique, because that column is how one is looked up —
by whoever is holding the link and is not inside an organization yet.
`ix_invitation_email` is not unique: an address may be invited again after the
first invitation was withdrawn or ran out, and the earlier row stays, because it
is what says who invited whom and when. What is refused is a second *open* one,
which is a rule about state rather than about a column.

Everything else on the row is a moment — accepted, withdrawn, expires — and the
state is read from them rather than stored as one, exactly as a device login's
is. `accepted_by_user_id` is the person it turned into, so an administrator
reading the list can see which of the people below arrived through which link.

## A handover in progress is a row, and it holds no code

`device_authorization` is the device-code flow of
[§6.2](../Specification.md#62-cli) between the CLI asking and a human confirming.
It carries `device_code_hash` and never the device code — the same rule the token
table follows, for a credential that is only worth ten minutes. The short
`user_code` *is* stored in the clear, because it is not a credential: it names a
pending request to the human confirming it, and confirming still takes their
password ([ADR 0008](./adr/0008-a-session-is-a-token-and-the-only-page-asks-for-a-password.md)).

Both codes are unique. Everything else on the row is a moment — approved, denied,
redeemed — and the state is read from them rather than stored as one, so there is
no status column that could disagree with its own timestamps. `ix_device_authorization_expires_at`
is what a sweep will read when this instance has one; until then a collected login
is a row that answers `redeemed` and hands nothing over twice.

It carries an `organization_id` like every other domain table, and at the moment
it is created nobody has authenticated. The MVP has exactly one organization (§5)
and that is the one it gets; the day there are several, what resolves it is the
human who confirms, and this is the column that answer already goes into.

**`produces` says what is at the far end**, and it is why this is one table
rather than two ([ADR 0021](./adr/0021-an-agent-asks-for-its-own-token.md)). A
login hands the person who confirmed a session; an enrollment hands the machine
that asked an agent token of its own. Everything before that last step — the two
codes, the ten minutes, the states, the rule that a code works once — is the
same protocol, and a second table would have been that protocol written out
twice. `ck_device_authorization_produces` keeps the column to the two that
exist, the way `ck_token_kind` keeps a token to the three kinds.

An enrollment carries three more columns and a small table. `requested_name` is
what the asking client called itself, capped and believed by nobody: an instance
cannot check that a machine calling itself an agent on somebody's laptop is one.
`approved_name` is what the person settled on, kept **beside** it rather than
over it, because a claim and a decision are two different facts.
`approved_scopes` is the same integer of flags a token's scopes are, null until
somebody has agreed anything.

`enrollment_binding` is the reach they agreed to, and it is not `token_binding`
for a reason of order: a token's value exists exactly once, in the answer that
creates it, so the token cannot be made until the machine that asked comes back
for it. Between the agreement and that collection there is a reach with nothing
to attach it to — and a `token_binding` row pointing at no token is a row its
own foreign key could not hold. These rows are a decision in flight and never a
record of anything: they go with the enrollment, and what the token ended up
reaching is written into `token_binding` when it is finally issued.

## Tokens store a hash, and nothing else

`token.value_hash` is unique, because authentication looks a token up by exactly
that column. It holds `sha256` of the token value — plain, because the input is
256 bits of randomness rather than a password, and because a per-row salt would
turn every request into a table scan. The value itself is shown once at creation
and never stored: a token this instance could read back would be a credential it
holds in the clear.

A value reads `vaultaffe_session_…`, `vaultaffe_service_…` or `vaultaffe_agent_…`
([ADR 0004](./adr/0004-the-token-format-and-the-envelope.md)). Nothing in the
schema knows that — the prefix is read before a lookup happens, and `kind` is
what the row carries.

The scope set is one integer of flags (`names`, `read`, `write`, `delete`), so
that "may write but never read" is expressible and adding a scope later is a new
flag rather than a migration. All three kinds exist from day one even though at
first only attribution tells them apart: you do not add a token kind later
without one.

A token with no `token_binding` rows reaches its whole organization — the default
for an agent token, because the point is attribution, not restriction
([§6.4](../Specification.md#64-permissions-in-the-mvp)). The unique index over
`(token_id, project_id, environment_id)` is declared `nulls not distinct`, or
Postgres would allow "this whole project" twice.

**`name`, `scopes` and the binding rows are the mutable part of a token, and
`value_hash` is not.** Changing what a credential is called, may do and reaches
is `update token` and a rewrite of its bindings; the column authentication looks
it up by never moves, which is what lets the same string keep working while the
instance changes what it lets it through for. A binding this act creates is
added to the context explicitly rather than being discovered on the aggregate,
because a store left to tell a row it just made from a row it loaded a moment
ago will sooner or later update where it should have inserted.

`user_id` is whose token it is: for a session token the person it authenticates,
for a service or agent token the person who created it and is accountable for what
it does. A human creates an agent token and hands it over
([§6.4](../Specification.md#64-permissions-in-the-mvp)), and the trail from a
machine back to a person is exactly what the change log's identity type is for
(§6.5). The foreign key is `restrict` rather than `cascade`, for the reason
revoking beats deleting: nothing here disappears quietly.

**A row does go, eventually, and only after a revocation.** Purging a revoked
token deletes it — `fk_token_binding_token` takes the bindings with it, because
a binding records nothing on its own — and nothing else in the schema points
here. That is deliberate rather than lucky: the change log names its identities
instead of keying them (below), precisely so that a credential's row can be
removed without the entries it signed going with it. A token still in use is
refused: a row that vanished while its value worked would be a credential nobody
could find and nobody could take back.

## What is deliberately not here

- **No grouping level between organization and project.** The organization is the
  grouping mechanism (Specification §5).
- **No `parent_environment_id`.** There is no inheritance, no branch config and
  no personal config, and therefore not a single exception to "everyone in the
  organization sees everything". The retrofit path, if that turns out wrong, is
  an additive column — a note, not a plan.
- **No roles table, and no permissions table.** Every user of an organization sees
  and changes everything in it, and the only distinction is
  `app_user.is_administrator` — who may invite users and administer the
  organization (§6.4). The granularity is the token's, and it is already two
  columns away.
- **No password reset table.** A reset is an administrator setting a password and
  handing it over (§6.1), so there is no second link kind to store — the one row
  type that carries a code is `invitation`
  ([ADR 0015](./adr/0015-an-invitation-is-a-credential-in-a-link.md)).
- **No session table.** A session *is* a token
  ([ADR 0008](./adr/0008-a-session-is-a-token-and-the-only-page-asks-for-a-password.md)),
  which is why signing out is a revocation and a session appears in the token
  listing beside the rest.
- **No `Down` migrations that anybody is told to run.** Migrations only run
  forward: a self-hosted instance that rolls a schema back rolls a value's
  ciphertext back with it, and the honest recovery from a bad upgrade is the
  documented backup.

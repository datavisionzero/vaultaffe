# The Data Model

Eight tables, and the rules the database itself holds. What each concept *means*
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
└─ project                  (organization_id, name unique per organization)
   └─ environment           (project_id, name unique per project)
      └─ secret             (environment_id, name unique per environment)
         └─ secret_value_version

token
└─ token_binding            (a project, or one environment of it)

change_log_entry            (points at nothing)
```

| Table | What it holds |
| --- | --- |
| `organization` | The tenancy boundary. Exactly one row in the MVP. |
| `project` | An application or service. |
| `environment` | `dev`, `staging`, `prod` — one level, no branch or personal configs. |
| `secret` | A key, and its current value sealed under the secret's data key. |
| `secret_value_version` | Values this secret used to hold, tightly bounded. |
| `token` | A credential: kind, scope set, and the hash of a value shown once. |
| `token_binding` | What a token may touch. No rows means the whole organization. |
| `change_log_entry` | What was done, by whom, and of what type — never a value. |

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

`organization` is the one exception, and it is an exception of shape rather than
of rule: a row of that table *is* an organization and cannot carry a key to one,
so it is filtered by its own. `TenancyTests` reads the model and fails if any
table is unfiltered, and puts a second organization in the database to prove the
filter holds against real rows.

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

*What the algorithm is, and how the master key is read, is not decided here.*
These are the columns those decisions land in.

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

The one cascade is `secret_value_version` → `secret`, and it does not contradict
the window: a deleted secret keeps its row, so it fires only when that row is
genuinely removed — a human purge, or the end of the window. When a secret is
gone its history has to be gone with it. That is what a purge means.

## Value history is bounded, and the clock is `replaced_at`

Five versions, at most 72 hours, whichever is reached first; what falls out is
deleted rather than tombstoned. Both numbers live in `Domain/Secrets/ValueHistory`
— Specification §6.5 says they are a decision rather than a placeholder, and that
if they turn out wrong they change in one line. That is the line.

The retention clock is `replaced_at`, not `written_at`: a value that stood for a
year and was replaced this morning is this morning's undo. The index
`ix_secret_value_version_secret_replaced_at` is what both the rollback and the
retention sweep read.

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
change-log entries, so this table outlives the project, environment and secret it
talks about, and a foreign key pointing at a row that is gone would take the
entry with it. `identity_name` is kept for the same reason: a revoked token's
entries should still read as something other than a bare id.

**What deserves honest documentation** (Specification §6.5): a purge in the
database does not reach into last night's backup. That belongs in the operations
guide when there is one, not in a footnote.

## Tokens store a hash, and nothing else

`token.value_hash` is unique, because authentication looks a token up by exactly
that column. The value itself is shown once at creation and never stored — a
token this instance could read back would be a credential it holds in the clear.

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

*Who owns a token is not in this table.* That arrives with the identity it
belongs to.

## What is deliberately not here

- **No grouping level between organization and project.** The organization is the
  grouping mechanism (Specification §5).
- **No `parent_environment_id`.** There is no inheritance, no branch config and
  no personal config, and therefore not a single exception to "everyone in the
  organization sees everything". The retrofit path, if that turns out wrong, is
  an additive column — a note, not a plan.
- **No users table yet.** Identity, its password hash and its sessions arrive
  with the login that needs them.
- **No `Down` migrations that anybody is told to run.** Migrations only run
  forward: a self-hosted instance that rolls a schema back rolls a value's
  ciphertext back with it, and the honest recovery from a bad upgrade is the
  documented backup.

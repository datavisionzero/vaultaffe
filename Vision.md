# Vaultaffe — Product Vision

> Working title. A self-hostable, open-source secrets manager in the spirit of
> [Doppler](https://www.doppler.com) — deliberately lean, deliberately
> opinionated, built for working alongside AI agents.

**License:** MIT
**Status:** Vision / pre-MVP — groundwork research complete (§10)

---

## 1. Elevator pitch

Vaultaffe is a secrets manager a team self-hosts in minutes with
`docker compose up`. It organizes secrets by project and environment, offers a
clean web UI for managing them — and a CLI that injects secrets into any process
as environment variables **without the values ever landing in plaintext in a
file, in shell history, or in an AI agent's context**.

## 2. Why

Doppler solves the secrets problem well and, above all, *simply*. But it is SaaS:
the secrets live with a third party, pricing scales with headcount, and the
feature set keeps growing in directions — integrations, compliance, fine-grained
RBAC — that small teams and solo developers do not need.

At the same time, the way software gets built is shifting. AI agents run
commands, start dev servers, deploy, provision new services — and they need
credentials to do it. An agent holding an API token in plaintext in its context
potentially leaks it into logs, transcripts, and model providers. The right
arrangement is: **the agent knows the secret's name, not its value.**

That is the gap Vaultaffe fills: Doppler's simplicity, self-hosted, open source,
with agents as a first-class audience.

## 3. Audience

1. **Solo developers and small teams** who don't want secrets scattered across
   `.env` files and password managers, but don't want to run an enterprise vault
   either.
2. **Teams working heavily with AI agents** (Claude Code, Cursor, Codex, homegrown
   agents) who want to give those agents access to systems without giving them
   secrets.
3. **Self-hosting-minded organizations** for whom data sovereignty is a
   requirement.

Explicitly *not* the audience for the MVP: large organizations with compliance
obligations, complex role models, and audit requirements.

## 4. Guiding principles

| Principle | What it means |
| --- | --- |
| **Simple beats complete** | If a feature needs explaining, it is probably cut wrong. Doppler is our benchmark for simplicity, not for scope. |
| **Opinionated** | We build one good path, not ten configurable ones. Fewer switches, less documentation, less misuse. |
| **Self-hosting first** | One `docker-compose.yml`, one Postgres, one backup command. No Kubernetes requirement, no external dependencies to operate. |
| **Agent-first** | The CLI is not a byproduct of the UI but an equal primary interface. Anything the UI can do, the CLI and API can do. |
| **Value-blind toward the model** | The structured agent interface never returns a secret value — not as an option, not behind a flag. Values reach processes, not model contexts. Of six vendors we examined, exactly one holds that line (1Password); this is our niche, not merely a precaution. |
| **Convenience over hardening** | We are not building a zero-knowledge system or an HSM integration. Realistic threat model: secrets should not be lying around in repos, logs, and agent contexts — not: defense against an attacker with root on your own server. |
| **Multi-tenant from day 1 (in the data model)** | The organization is the dividing line. It is in the model from the start, even though the MVP's UI only ever shows one. |

## 5. Core concepts

Deliberately close to Doppler, because the model has proven itself:

- **Organization** — the tenancy boundary. Every record belongs to exactly one
  organization. Organizations never see each other.
- **Project** — an application or service (`webshop-api`, `landing-page`).
  Projects can be **grouped** (by customer or product line, say); this matters
  more to us than to Doppler, because self-hosters tend to put everything in one
  instance.
- **Environment** — `dev`, `staging`, `prod` within a project. Freely nameable,
  with sensible defaults when a project is created.
- **Secret** — a key/value pair within an environment. Values are encrypted at
  rest. Names follow `^[A-Z_][A-Z0-9_]*$` — the same rule Doppler uses, because
  secrets end up as environment variables and we would rather not have the
  special-character discussion.
- **Token** — an access key for CLI and automation, typically bound to a project
  and environment and limited in scope (read / read-write). Tokens carry a
  **recognizable prefix** per kind so that secret scanners find them in repos and
  logs. Doppler does this on purpose, and it has to be settled in the first
  design pass — you don't change a token format later.

For the same reason we are fixing the **reference scheme**
`vaultaffe://<project>/<environment>/<KEY>` now, even though nothing in the MVP
resolves it. It costs nothing today and will be as immutable as the token format
later. Practical consequence for naming: project and environment names stay
narrow — no spaces. 1Password allows them and trips over its own template syntax
as a result.

**One "environment" instead of Doppler's environment + config.** Doppler splits
the two: the environment creates a root config and is the anchor for access
control, with branch configs hanging underneath — one per developer (a "personal
config", enabled by default on `dev`), one per feature branch or preview
deployment. We collapse both into a single concept. The reason is not an
assumption about team size but a boundary:

**Doppler is a config store; we are a secret store.** The product says so
everywhere: the level is literally called "config", there are 16 value types such
as `PORT`, `URL`, and `boolean`, there are `${KEY}` references for composing
values, and the docs consistently talk about "secrets **and app configuration**".
But once `PORT`, `LOG_LEVEL`, and `DATABASE_URL=postgres://localhost:5432/app`
live in the vault, a per-person level becomes *necessary* — those are exactly the
values that differ on every machine. Personal configs are a consequence of
non-secrets living in a secret store. We don't want that, and with it the need
largely disappears: **confidential values almost always belong to the team, not
to a person.**

**What varies per machine usually isn't confidential — and doesn't belong in
here.** The replacement costs us no model level, because it already exists:
`vaultaffe run` passes the existing environment through and layers the secrets on
top. A value that isn't in the vault arrives unchanged from your shell, your
`.envrc`, or Docker Compose — there is no conflict to resolve. Where the rule
chafes, you should know it: `DATABASE_URL` is harmless in `dev` and confidential
in `prod`, so realistically the same key lands in both environments; and some
vendors hand out a separate sandbox per developer. For both, the escape hatch is
an ordinary additional environment, `dev-alex`. As a rare case that is
acceptable; as the normal path it would not be.

What this buys us is more than a saved level: **not a single exception** to
"everyone in the organization sees everything" (personal configs are precisely
that in Doppler — the one exception to permission inheritance), no second
visibility model in every query, no underscore naming convention, and we never
have to answer the inheritance question Doppler's own documentation fails to
answer: if the root config changes a key that a branch config has overridden, who
wins? No wording there resolves it. Retrofit path if we turn out to be wrong: an
additive `parent_environment_id` column. Not a plan — just a note that the door
stays open.

**In the MVP: report, don't act.** Doppler does *not* automatically create a new
secret across all environments. It offers opt-in mirroring on save, plus a
"missing secret detection" that merely *shows* keys missing from an environment,
with a dismiss option. For the MVP we adopt only the notice: no silent automation.
**Propagation across environments is therefore not rejected**, only deferred — as
an opt-in action the user triggers, it is a plausible next step (§12).

## 6. MVP scope

### 6.1 Web UI

- Login (email + password), user management within the organization.
- Overview of project groups, projects, environments.
- View, create, edit, delete secrets — values masked by default, revealable.
- Import/export of a `.env` per environment (the pragmatic migration path).
- A notice for keys missing in one environment but present in another — as a
  display with a "dismiss" option, not as automation.
- Change log and bounded value history with rollback (§6.5).
- Token management: create, name, revoke. The value is shown exactly once.
- Per-secret change history: who changed what, when.
- The organization name is editable; exactly one organization ("Default") in the
  MVP.

### 6.2 CLI

The centerpiece. The target state:

```bash
vaultaffe login                       # auth-code flow, token into the OS keychain
vaultaffe setup                       # binds the current directory to project + environment
vaultaffe run -- npm run dev          # replaces itself with the process, secrets in the env
vaultaffe secrets                     # lists names only — never values
vaultaffe secrets get DB_URL          # prints one value — deliberately explicit, one key
cmd | vaultaffe secrets set STRIPE_KEY   # value exclusively via stdin
vaultaffe secrets set SMTP_PASSWORD --empty   # empty placeholder for a human to fill
vaultaffe secrets delete STRIPE_KEY
vaultaffe secrets download --format env
```

Properties that matter to us:

- **`exec()` instead of a wrapper process.** Doppler stays the parent of the
  command and forwards signals by hand — necessary only because of
  `--mount`/`--watch`, which are non-goals for us. Without them our CLI replaces
  itself with the target process via `exec()`. Signal forwarding, exit codes, and
  process groups then resolve themselves instead of us reimplementing four
  pitfalls Doppler has here (Ctrl-Z gets caught too, no separate process group, a
  signal-terminated child reports `255` instead of `128+N`, forwarding is off by
  default at a TTY). On Windows, which has no `exec()`, the wrapper approach
  remains.
- **Injection, not a file.** Secrets reach the process as environment variables.
  No temporary `.env`, no plaintext on disk. Store values win over pre-existing
  variables; `PATH`, `HOME`, and friends are reserved.
- **No masking of process output.** If you print your own secret, you printed it.
  We don't promise otherwise and we filter nothing — neither does Doppler.
- **Directory binding lives in the user's configuration.** Doppler's most
  underrated idea: the directory → project+environment mapping is a prefix table
  in the user's config, not a file in the repo. An optional checked-in project
  file (project + environment, *no* secrets) merely produces an entry in it.
  Unlike Doppler we search for it up the directory tree, so monorepos work.
- **Agent-friendly:** machine-readable JSON on request, clear exit codes, no
  interactive prompts in non-interactive mode.
- **Setting a secret without seeing it — and not offering the unsafe form at
  all.** Doppler allows `secrets set KEY=value` as an argument and counters with
  five lines of `HISTIGNORE` configuration; a design that thereby declares its own
  failure. With us the value arrives **exclusively via stdin**, never as an
  argument (shell history, `ps`) and never as a file. The confirmation does not
  echo the value back. An agent can pipe the output of a `gcloud` or `stripe`
  command straight through without reading it.
- **Empty placeholders behind a named flag.** `--empty` creates a key without a
  value — the agent prepares, the human fills in. (In Doppler this only works as
  an undocumented side effect of `KEY=`.)
- **Fetching names without values.** A dedicated endpoint returns secret names
  only. That is the normal case for agents — and exactly the shape a later MCP
  server needs.

### 6.3 Operations

- One `docker-compose.yml`: backend, frontend, Postgres. One `.env` for instance
  configuration (including the master encryption key).
- A documented **backup and restore path** for the whole instance (database dump
  plus key material), including the note that a backup without the key is
  worthless.
- Migrations run automatically at startup.
- First-run wizard: the first user becomes administrator, the default
  organization is created.

### 6.4 Permissions in the MVP

Deliberately trivial: **every user in an organization sees and changes everything
in that organization.** No role model beyond the distinction of who may invite
users and administer the organization. Tokens, by contrast, are tightly bound
(project + environment, read or write), because that is where the real risk sits.

### 6.5 Logging and history

Two things that often get lumped together despite having completely different
risk profiles:

**The change log never contains values.** We record timestamp, project,
environment, secret name, action — and **identity along with identity type**
(human, service token, agent), because with writing agents that is the
interesting question. No value, not even as a diff. We follow AWS here, not
Doppler: Doppler puts the old and new value into the log and consequently had to
bolt on a redaction feature, which doesn't even truly delete the value.

**Value history is tightly bounded.** Rollback is a real need and writing agents
make it more common, not less — an agent that wrecks a value overnight needs an
undo button. But the useful window is hours, not months, and every retained old
value is usually a still-valid credential. So: a small number of previous
versions, additionally time-limited, and when a version falls out it is deleted
rather than tombstoned. For comparison: Vault defaults to 10 versions, AWS
guarantees exactly one — Doppler alone keeps them indefinitely.

**Purge is a visible feature**, not a support ticket. After a suspected
compromise the normal expectation is that a key's history genuinely disappears.
Neither Doppler nor AWS offers this cleanly; here we can do better with little
effort.

**Reads are not logged exhaustively.** Every `run` is a read; a complete trail
would be the largest table in the system. Instead: first and last access per
identity and secret — enough to find unused secrets and unexpected access.
(Doppler uses the same model.)

**What deserves honest documentation:** a purge in the database does not reach
into last night's backup. We promise backups (§6.3); no product we examined says
anything about this contradiction. We should.

## 7. Explicitly not in the MVP

- **Integrations and synchronization** to Vercel, AWS, GitHub Actions, Kubernetes
  and so on. The classic core of Doppler — a non-goal for us. Envisaged long-term
  as a **plugin point** so the community can contribute them; the architecture
  should not preclude it, but we build none of it in the MVP.
- Fine-grained RBAC, groups, permission inheritance.
- **Personal configs / per-user values.** Rejected, not deferred — reasoning in
  §5. They mostly solve machine-dependent *configuration*, which does not belong
  in the vault in the first place, and they would be the single exception to
  "everyone sees everything".
- **Branch configs and preview environments.** A throwaway environment per pull
  request is a real pattern, but the value share of it is small — two or three
  differing keys, such as the database URL and your own hostname in the OAuth
  redirect. The large share is CI: create it, provision a database, tear it down
  after the merge. That sits beyond our boundary to integrations. Whoever needs it
  can create an environment via the CLI and delete it again; we build no more than
  that.
- Secret rotation, expiry dates, automatic renewal.
- Dynamic secrets / short-lived credentials (that is Vault's field, not ours).
- Compliance features: SOC 2 reports, tamper-evident audit trails, SSO/SAML/SCIM.
- Log export, SIEM integration, alerts and webhooks on log events, retention
  tiers. If log forwarding ever exists, it carries no values.
- A "read-only mode" that lets secret values through. AWS's MCP server classifies
  `GetSecretValue` as a read operation and thereby permits it in read-only mode —
  but in a secrets manager, reading *is* the dangerous operation. We would rather
  build no such mode than one that makes a false promise.
- Multiple organizations in the UI (data model yes, interface no).
- Automatic mirroring or propagation of secrets across environments — deferred,
  not rejected (§5, §12).
- An `inject`/`substitute` command that replaces references in configuration files
  with values. It solves only "my config format doesn't read environment
  variables" and costs escaping rules per format, a second addressing model, and a
  contradiction with "no plaintext on disk". None of the guiding scenarios in §8
  needs it. If it ever ships: brace syntax only, stdout by default, and quoting
  from day one.
- Mobile app, browser extension.
- Zero-knowledge / end-to-end encryption where the server can never see values.

This list is not an excuse; it is the product decision. Anyone who needs items
from it is better served by **[Infisical](https://infisical.com)** — and our
README should say so. A project that hides its alternatives is selling itself as
something it isn't.

## 8. Agentic use — the guiding scenarios

These scenarios decide whether the product succeeds:

1. **The agent starts the application.** The agent runs
   `vaultaffe run -- npm run dev`. The application has its credentials; the agent
   never saw them. Its transcript contains the command name, not the token.
2. **The agent provisions a new service.** The agent creates an API key at a
   vendor and pipes it straight into `vaultaffe secrets set` — the value travels
   from the vendor into the store without a detour through the model context.
3. **The agent prepares, the human completes.** While implementing, the agent
   notices that `SMTP_PASSWORD` is needed, creates the key as an empty
   placeholder, and tells the human. No half-finished code with
   `TODO: put token here`.
4. **The agent rotates a credential.** It creates a new key at the vendor, pipes
   it into `secrets set`, and revokes the old one. Rotation is therefore a
   **workflow, not a product feature** for us — real rotation otherwise requires
   provider integrations, and those are a non-goal. The product supplies only what
   the workflow needs: write a new value, keep the old one briefly, optionally a
   "check by" date.

For all of these: whatever the agent can do through the CLI it must also be able
to do through the HTTP API — so that an **MCP server** can later be layered on top
as a thin shell, without touching the backend.

**Where value-blindness applies, and where it doesn't.** `run` has to fetch values;
that is its purpose. `secrets get` deliberately prints exactly one value,
explicitly requested. The line does not run through the CLI but through the
*structured* agent interface: a later MCP server gets **no** tool that returns a
value. Names without values are the normal case there, not the exception.

**Writing yes, deleting sparingly.** 1Password's MCP server gives agents write
access but not a single delete tool. We adopt that asymmetry: an agent that
creates a secret is useful; one that deletes a secret is mostly dangerous. The
empirical finding from the CVE research matches — there are documented cases of
agents *exfiltrating* credentials, but not one of an agent overwriting or deleting
a secret value. The priority is exfiltration, not destruction.

## 9. Technical guardrails

- **Backend:** .NET 10, with the HTTP API as the only read/write interface. The
  web UI is a client of that API just like the CLI — no privileged side doors.
- **Database:** PostgreSQL. Secret values encrypted at rest with an instance key
  from the environment configuration.
- **Frontend:** React.
- **CLI: Go.** Deliberately not .NET — the CLI does not have to match the backend,
  and one technical argument decides it: `exec()` requires a system call that Go
  (`syscall.Exec`) and Rust expose directly, while .NET offers it only through
  P/Invoke. Add to that a static single-file binary with no runtime,
  cross-compilation for every target in one step, mature keyring libraries, and
  millisecond startup — the last one counts, because `vaultaffe run` sits in front
  of *every* command. Rust would be the equivalent alternative. Doppler's own CLI
  is written in Go; we read it as a reference but adopt no code (Apache-2.0 cannot
  be relicensed to MIT). Rust's edge in this field (`secrecy`/`zeroize`, sandboxing
  via Landlock/seatbelt — the reason the Codex CLI was rewritten in Rust) only
  applies once the CLI not only starts the child process but also isolates it.
  That is a non-goal; the decision gets revisited only if it stops being one.
- **Deployment:** Docker Compose as the first-class path.
- **Local token storage:** the OS keychain. If it isn't available we say so
  loudly and let the user decide — Doppler silently falls back to plaintext in
  that case, undocumented. Precisely not that.
- **Multi-tenancy:** every domain table carries the organization. All queries are
  organization-filtered by default, enforced in one central place rather than in
  every handler. That makes opening up to multiple organizations later a UI and
  onboarding question, not a migration.
- **Extensibility:** authentication methods, the encryption backend, and (later)
  integrations sit behind narrow interfaces — prepared, but not built out.

## 10. What the research found

The groundwork research is complete: the open-source landscape and the
build-vs-fork question, the mechanics of the Doppler CLI verified against its
source, the agent write path and the MCP servers of six vendors, audit logging and
value history across products, 1Password's `op://` reference pattern, and
commercial pricing. The raw notes are deliberately **not** part of this
repository — they are working material, heavy on vendor-specific detail with a
short shelf life. What survived of them is in this document.

One finding worth stating explicitly, because it is easy to get backwards: the
`op://` reference pattern is **not** what makes 1Password value-blind. That comes
from `op run`, the MCP server, and the FIFO mount; `op inject` writes plaintext,
either into a file in the agent's working directory or to stdout and thus straight
into the model context. 1Password markets the pattern as repository hygiene — it
"can safely be checked into Git" — not as agent protection, and no reference
appears in its MCP server's tool list. For us that means: no `inject` in the MVP
(§7), only the scheme decision pulled forward (§5).

### 10.1 Build, don't fork

**An open-source Doppler does not exist.** Doppler has open-sourced only the CLI
(Apache-2.0); there is no server repository. The existing clone attempts are
one-person projects without a viable licensing position.

The obvious alternatives all rule themselves out as the base for an MIT fork:

| Project | License | Why not a fork |
| --- | --- | --- |
| Infisical | MIT Expat **with an `ee/` exception** | 68 directories under a proprietary enterprise license, including the permission layer itself. An MIT fork would be "Infisical without its permission system". On top of that, ~12,800 commits in 12 months — you don't fork in order to remove code. |
| Phase | the same `ee/` construction | CLI separately GPL-3.0, seven containers to operate. |
| HashiCorp Vault | BUSL-1.1 (licensor now IBM) | Last MPL releases: `v1.14.8` / `v1.13.12`. |
| OpenBao | MPL-2.0 | File-level copyleft, not relicensable to MIT — and a different product with no notion of projects and environments. |
| Bitwarden Secrets Manager | AGPL-3.0 | Self-hosting only with an enterprise subscription, and no environment concept. |

**The cautionary tale:** EnvKey had MIT, the right model, and even
`envkey-source -- cmd`. The cloud was shut down in February 2025 and the
repository has been dormant since August 2024. A secrets manager whose operator
disappears takes its users with it — that is the argument for self-hosting as the
*only* mode of operation, not as a concession.

### 10.2 The honest justification

Against "why not just use Infisical", our answer is **not** the feature set and
**not** the tech stack — .NET is an author preference, not a product advantage,
and it should never be sold as one. Three points remain, and they hold:

1. **What is left out.** A product you can explain in one screenful. Plus a
   concrete finding from the pricing comparison: at the competition, the paywall
   trigger is almost never the number of secrets but **audit log retention and
   SSO** — Doppler's free tier keeps logs for three days, Infisical's for none.
   Exactly what we put at the core in §6.5. And both Doppler and Bitwarden put the
   right to self-host in their most expensive tier.
2. **MIT without an asterisk.** No `ee/` directory, no feature behind a second
   license.
3. **Agent ergonomics.** Not one candidate we examined knows "set a value without
   seeing it" or "create an empty placeholder". That is the gap.

### 10.3 What is still open

- Process group and signal behavior needs to be verified in practice, not just
  inferred from someone else's source.
- Doppler's API reference was consistently blocked by Cloudflare; the HTTP field
  names are evidenced only from the Go structs in the CLI.
- A fallback cache for offline operation: whether we want one in the MVP at all.
  Doppler's caching apparatus is explained retroactively by its API limits —
  secret reads are rate-limited separately (120–480/min) and every `run` is a read.
  As self-hosters we simply do not have that problem; for us it would purely be
  about working offline. If we do build it, then deliberately unlike Doppler:
  its file derives the key from `token:project:config`, has no expiry, and a
  revoked token does not lock the machine out.
- The rule "what varies per machine doesn't belong in the vault" (§5) is a
  positioning claim we will have to defend in the documentation. It is the reason
  we leave out personal configs, even though in Doppler they are the normal path
  for local development rather than an edge case. The early warning signal, should
  we be wrong, is easy to watch for: environments named `dev-<firstname>` start
  appearing in numbers. The retrofit path is then described in §5.
- What exactly the change history looks like: full value history, or rollback
  only.

## 11. Success criteria for the MVP

- From `git clone` to a running instance with the first secret in **under 10
  minutes**.
- `vaultaffe run -- <cmd>` works reliably on macOS and Linux, including correct
  exit codes and signal forwarding.
- An AI agent can start an application and create a new secret without a single
  secret value appearing in its transcript.
- A team migrates its `.env` files and deletes them afterwards.
- The README explains the entire product in one screenful.

## 12. Afterwards (outlook, not MVP)

Roughly in this order:

1. Multiple organizations in the UI, and switching between them.
2. An MCP server as the official agent interface alongside the CLI — value-blind
   (§8) and on the same HTTP API, with no special path in the backend. Open
   question: **elicitation** would be the clean way to accept a value from a human
   without the agent seeing it, but the URL mode required for that is so far
   documented for only a single client. It needs a runtime capability check and a
   defined failure path. Tool annotations (`readOnlyHint` and relatives) are no
   foundation to build on; the MCP project itself considers them a failure.
3. Roles and access restrictions at project/environment level (at minimum:
   restricting production environments). Doppler's `restricted` visibility is
   worth a second look here: it separates not by role but by **identity type** — a
   value a machine may read and a human never gets to see in the UI. That is
   exactly the direction of thinking our agent focus needs, only approached from
   the other side.
4. A plugin mechanism for integrations — with the goal that the community
   contributes them, not us.
5. Opt-in propagation of secrets across environments: a value the user
   deliberately writes into several environments, and building on that, the
   question of whether and how an agent may trigger it.
6. Webhooks / change notifications.
7. SSO for organizations that need it.

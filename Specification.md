# Vaultaffe — Product Specification

> Working title. A self-hostable, open-source secrets manager in the spirit of
> [Doppler](https://www.doppler.com) — deliberately lean, deliberately
> opinionated, built for working alongside AI agents.

**License:** MIT
**Status:** Specification / pre-MVP — groundwork research complete (§10)

The [product vision](Vision.md) defines the audience, promise, and boundaries.
This document contains the detailed behavior, implementation decisions, build
order, and research rationale. Deferred features are explicitly marked.

---

## 1. Elevator pitch

Vaultaffe is a secrets manager a team self-hosts in minutes with
`docker compose up`. It organizes secrets by project and environment, offers a
clean web UI for managing them — and a CLI that injects secrets into any process
as environment variables. **Normal development workflows do not require copying
secret values into project files, shell history, or an AI agent's context.** This
is a workflow property, not a barrier preventing an authorized agent from reading
values or a child process from printing them.

## 2. Why

Doppler solves the secrets problem well and, above all, *simply*. But it is SaaS:
the secrets live with a third party, pricing scales with headcount, and the
feature set keeps growing in directions — integrations, compliance, fine-grained
RBAC — that small teams and solo developers do not need.

At the same time, the way software gets built is shifting. AI agents run
commands, start dev servers, deploy, provision new services — and they need
credentials to do it. An agent holding an API token in plaintext in its context
potentially leaks it into logs, transcripts, and model providers. The right
arrangement is: **the agent can work with a secret's name without needing to see
its value.**

That is the gap Vaultaffe fills: Doppler's simplicity, self-hosted, open source,
with agents as a first-class audience.

## 3. Audience

The primary audience is **solo developers and small, technically experienced
teams that work daily with CLI-based AI agents and operate their own development
infrastructure**. They want shared credentials out of project files and routine
agent conversations without operating an enterprise vault.

Agencies and other small organizations are possible later audiences, not the
initial target: customer separation and different human access requirements may
not fit the MVP's organization-wide visibility.

Explicitly *not* the audience for the MVP: large organizations with compliance
obligations, complex role models, and audit requirements.

## 4. Guiding principles

| Principle | What it means |
| --- | --- |
| **Simple beats complete** | If a feature needs explaining, it is probably cut wrong. Doppler is our benchmark for simplicity, not for scope. |
| **Opinionated** | We build one good path, not ten configurable ones. Fewer switches, less documentation, less misuse. |
| **Self-hosting first** | One `docker-compose.yml`, one Postgres, one documented backup path. No Kubernetes requirement, no external dependencies to operate — no mail server either (§6.1). |
| **Agent-first** | The CLI is an equal primary interface for development workflows. The UI and CLI use the same API. Complete CLI coverage of human administration is a follow-up, not an MVP gate; human-only actions still require a human session (§6.4). |
| **Value-blind by default** | The *structured* agent interface (the later MCP server, §12) never returns a secret value — not as an option, not behind a flag. The CLI cannot promise that: `run` is a value read by definition, and `run -- env` prints everything. So for the CLI the claim is narrower and still worth making: **every command an agent needs in the normal course of work completes without a value passing through its context.** Values reach processes, not model contexts. Of six vendors we examined, exactly one holds that line (1Password); this is our niche, not merely a precaution. |
| **Agents act under their own identity** | An agent never uses a human's session. It gets an **agent token** (§6.4), so every log line says who acted — a person or an agent. This is about attribution, not restriction: an agent token is as capable as its human by default, and only a short list of actions is human-only. |
| **Convenience over hardening** | We are not building a zero-knowledge system or an HSM integration. Realistic threat model: secrets should not be lying around in repos, logs, and agent contexts — not: defense against an attacker with root on your own server. **The agent is assumed cooperative.** We protect against a careless agent leaking values into transcripts and logs, not against one hijacked by prompt injection that deliberately exfiltrates. Against the latter a secrets manager with a `run` command has no defense worth promising, and we don't pretend otherwise. |
| **Multi-tenant from day 1 (in the data model)** | The organization is the dividing line. It is in the model from the start, even though the MVP's UI only ever shows one. |

## 5. Core concepts

Deliberately close to Doppler, because the model has proven itself:

- **Organization** — the tenancy boundary. Every record belongs to exactly one
  organization. Organizations never see each other.
- **Project** — an application or service (`webshop-api`, `landing-page`).
  Project names are unique within an organization. There is deliberately **no
  grouping level** between organization and project: self-hosters do tend to put
  everything in one instance, but the organization already is the grouping
  mechanism in the data model, and a second one in the MVP would be exactly the
  kind of feature §4 warns about. If one instance genuinely needs several groups,
  that is the multi-organization UI of §12, not a new level.
- **Environment** — `dev`, `staging`, `prod` within a project. Freely nameable,
  with sensible defaults when a project is created.
- **Secret** — a key/value pair within an environment. Values are encrypted at
  rest. Names follow `^[A-Z_][A-Z0-9_]*$` — the same rule Doppler uses, because
  secrets end up as environment variables and we would rather not have the
  special-character discussion.
- **Token** — an access key for CLI and automation. There are **three kinds**,
  each with a **recognizable prefix**, so that secret scanners find them in repos
  and logs and so that the server always knows which kind it is talking to: the
  *session token* a human receives from `vaultaffe login`, the *service token*
  for CI and deployments, and the *agent token* (§6.4). Every token carries a
  **binding** (which projects and environments it may touch) and a **scope set**
  — `names`, `read`, `write`, `delete` — rather than a read/read-write switch, so
  that "may write but never read" is expressible. Doppler does the prefix on
  purpose, and it has to be settled in the first design pass — you don't change a
  token format later. Nor do you add a token kind later without a migration;
  that is why the agent kind exists from day one even though nothing but
  attribution uses it at first.

For the same reason we are fixing the **reference scheme**
`vaultaffe://<project>/<environment>/<KEY>` now, even though nothing in the MVP
resolves it. It costs nothing today and will be as immutable as the token format
later. The organization is not part of the reference; it is implied by the token
that resolves it, which is why project names are unique per organization.
Practical consequence for naming: project and environment names stay narrow — no
spaces. 1Password allows them and trips over its own template syntax as a result.

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
values that differ on every machine. Personal configs can serve that use case,
but also genuinely personal credentials such as individual sandbox tokens.
**The MVP covers shared team secrets, not personal credentials or private
development environments.** Omitting personal configs is a scope choice, not a
claim that personal secrets are uncommon.

**Non-secret local configuration stays local.** That needs no new model level:
`vaultaffe run` passes the existing environment through and layers the secrets on
top. A value that isn't in the vault arrives unchanged from your shell, your
`.envrc`, or Docker Compose — there is no conflict to resolve. Where the rule
chafes, you should know it: `DATABASE_URL` is harmless in `dev` and confidential
in `prod`, so realistically the same key lands in both environments — and then
the store wins over your shell (§6.2), so the "machine-specific values come from
the machine" argument only holds for keys that are *absent* from the vault. `run`
therefore reports every collision, because a collision is usually configuration
that leaked into the vault. And some vendors hand out a separate sandbox per
developer. An ordinary additional environment, `dev-alex`, can separate values
only when those values may be shared with the whole team. It provides no privacy:
everyone in the organization can access it (§6.4). Genuinely personal credentials
are outside the MVP. The later missing-key notice must also account for such
additional environments (§12).

What this buys us is more than a saved level: **not a single exception** to
"everyone in the organization sees everything" (personal configs are precisely
that in Doppler — the one exception to permission inheritance), no second
visibility model in every query, no underscore naming convention, and we never
have to answer the inheritance question Doppler's own documentation fails to
answer: if the root config changes a key that a branch config has overridden, who
wins? No wording there resolves it. Retrofit path if we turn out to be wrong: an
additive `parent_environment_id` column. Not a plan — just a note that the door
stays open.

**After MVP: report, don't act.** Doppler does *not* automatically create a new
secret across all environments. It offers opt-in mirroring on save, plus a
"missing secret detection" that merely *shows* keys missing from an environment,
with a dismiss option. Our first follow-up adopts only the notice: no silent automation.
**Propagation across environments is therefore not rejected**, only deferred — as
an opt-in action the user triggers, it is a plausible next step (§12).

## 6. MVP scope

### 6.1 Web UI

- Login (email + password), user management within the organization. The
  instance sends **no email**: an invitation is a link the administrator copies
  and hands over, a password reset is done by an administrator. That is the price
  of "no external dependencies to operate" (§4), and for teams of this size it is
  the right one.
- Overview of projects and environments.
- View, create, edit, delete secrets — values masked by default, revealable.
- Import/export of a `.env` per environment (the pragmatic migration path). The
  export is the human path to a plaintext file; the CLI equivalent exists but is
  restricted to human sessions (§6.2).
- **After MVP:** a notice for keys missing in one environment but present in another — as a
  display with a "dismiss" option, not as automation.
- Change log and bounded value history with rollback and purge; restore deleted
  secrets, environments, and projects within their recovery window (§6.5).
- Token management: create, name, revoke; for agent tokens also the binding and
  scope set (§6.4). The value is shown exactly once. Token management is
  **human-only**: a token is itself a secret, and one created by an agent through
  the CLI would land on stdout — and thus in its context.
- Per-secret change history: who changed what, when — and whether "who" was a
  person or an agent.
- The organization name is editable; exactly one organization ("Default") in the
  MVP.

### 6.2 CLI

The centerpiece. The target state for the secrets surface:

```bash
vaultaffe login                       # device-code flow, session token into the OS keychain
vaultaffe setup                       # binds the current directory to project + environment
vaultaffe run -- npm run dev          # replaces itself with the process, secrets in the env
vaultaffe secrets                     # lists names and status (set / empty) — never values
vaultaffe secrets get DB_URL          # prints one value — deliberately explicit, one key
cmd | vaultaffe secrets set STRIPE_KEY            # value exclusively via stdin
cmd | vaultaffe secrets set STRIPE_KEY --replace  # overwriting a non-empty value is explicit
vaultaffe secrets set SMTP_PASSWORD --empty       # empty placeholder for a human to fill
vaultaffe secrets delete STRIPE_KEY
vaultaffe secrets import < .env       # migration: the CLI reads the file, the agent never does
vaultaffe secrets export --format env # human sessions only — see below
```

That is the secrets surface, not the whole CLI. The MVP also supports projects
and environments (create, list, delete, restore), secret restore and rollback,
and token creation for initial setup under a human session. Complete CLI coverage
of human administration and the missing-key notice follow after MVP (§12).
All implemented human-only commands require a session token, with a clear error
otherwise; an unavailable CLI command must not be suggested as a recovery step.

Properties that matter to us:

- **`exec()` instead of a wrapper process.** Doppler stays the parent of the
  command and forwards signals by hand — necessary only because of
  `--mount`/`--watch`, which are non-goals for us. Without them our CLI replaces
  itself with the target process via `exec()`. Signal forwarding, exit codes, and
  process groups then resolve themselves instead of us reimplementing four
  pitfalls Doppler has here (Ctrl-Z gets caught too, no separate process group, a
  signal-terminated child reports `255` instead of `128+N`, forwarding is off by
  default at a TTY). Windows has no `exec()` and is **not an MVP target** (§11);
  WSL runs the Linux binary. A native build with the wrapper approach follows the
  MVP (§12).
- **Injection, not a file.** Secrets reach the process as environment variables.
  No temporary `.env`, no plaintext on disk. Store values win over pre-existing
  variables, and every collision is reported on stderr (and in the JSON output),
  because a collision usually means machine-specific configuration has leaked
  into the vault (§5). A **fixed, documented list** of variables is never
  overwritten: `PATH`, `HOME`, `USER`, `SHELL`, `TMPDIR`, everything beginning
  with `LD_` or `DYLD_`, and everything beginning with `VAULTAFFE_`. The last so
  that the token an agent runs under never reaches the child process; the loader
  variables so that a write token cannot inject code into every `run`. "And
  friends" is not a specification.
- **An empty placeholder stops `run`.** If a key in the environment is an empty
  placeholder, `run` refuses to start and names the key; `--allow-empty` overrides
  it. An empty placeholder means "a human still has to do something", and
  silently injecting an empty string would hide exactly that. The names listing
  carries the same status, so an agent sees what is still missing without seeing
  anything else.
- **No masking of process output.** If you print your own secret, you printed it.
  We don't promise otherwise and we filter nothing — neither does Doppler.
- **Directory binding lives in the user's configuration.** Doppler's most
  underrated idea: the directory → project+environment mapping is a prefix table
  in the user's config, not a file in the repo. An optional checked-in project
  file (project + environment, *no* secrets) merely produces an entry in it.
  Unlike Doppler we search for it up the directory tree, so monorepos work. In
  CI and containers, `VAULTAFFE_PROJECT` and `VAULTAFFE_ENVIRONMENT` override the
  binding.
- **Headless authentication.** `login` uses the **device-code flow** — the CLI
  shows a short code, the human confirms it in a browser on any machine — because
  SSH sessions, CI, containers, and agent sandboxes have no browser of their own.
  Where there is no human in the loop at all, the token arrives through
  `VAULTAFFE_TOKEN`. That variable is also how an agent receives its agent token
  (§6.4).
- **Agent-friendly:** machine-readable JSON on request, clear exit codes, no
  interactive prompts in non-interactive mode. An action an agent may not perform
  (§6.4) fails with an error that says so and names the human CLI command or UI
  action available for it — the agent proposes, the human executes.
- **Setting a secret without seeing it — and not offering the unsafe form at
  all.** Doppler allows `secrets set KEY=value` as an argument and counters with
  five lines of `HISTIGNORE` configuration; a design that thereby declares its own
  failure. With us the value arrives **exclusively via stdin**, never as an
  argument (shell history, `ps`) and never as a file. The confirmation does not
  echo the value back. An agent can pipe the output of a `gcloud` or `stripe`
  command straight through without reading it. **Exactly one trailing newline is
  stripped**, because practically every command ends its output with one and a
  key with an invisible `\n` at the end is the kind of bug that costs an
  afternoon; `--raw` keeps the bytes as they are. Multi-line values such as PEM
  keys pass through stdin unchanged.
- **Overwriting is explicit.** `set` on a key that already holds a non-empty value
  fails unless `--replace` is given; filling an empty placeholder needs no flag.
  Overwriting is as destructive as deleting, and an agent that means to rotate a
  key should say so — the flag is also what makes the rotation in §8 legible in
  the change log.
- **Empty placeholders behind a named flag.** `--empty` creates a key without a
  value — the agent prepares, the human fills in. (In Doppler this only works as
  an undocumented side effect of `KEY=`.)
- **Fetching names without values.** A dedicated endpoint returns secret names
  and their status only. That is the normal case for agents — and exactly the
  shape a later MCP server needs.
- **Import via stdin, export for humans only.** `import` reads a `.env` from
  stdin, so an agent can migrate a file it never displays — migration is a
  success criterion (§11) and must not be UI-only. `export` writes every value in
  plaintext, which is precisely the contradiction we used to reject `inject`
  (§7). It stays because a way back out is part of being trustworthy, but it
  works only under a session token; agent and service tokens are refused.

### 6.3 Operations

- One `docker-compose.yml`: the instance, Postgres, and **Caddy** as the TLS
  terminator. The frontend is not a service of its own — it is built into the
  instance's image and served by it, so the browser reaches the API at its own
  origin ([ADR 0016](docs/adr/0016-one-image-and-caddy-in-front-of-it.md)). One
  `.env` for instance configuration (including the master encryption key).
- **TLS is part of "in minutes".** A token over plain HTTP is a token in the
  network log. Caddy obtains certificates automatically for a domain; the backend
  itself speaks only HTTP on the compose-internal network. The CLI refuses plain
  HTTP to any non-loopback host unless explicitly overridden, so `localhost`
  works out of the box and nothing else quietly does.
- **Envelope encryption from day one.** Each value is encrypted with a per-secret
  data key, which is in turn wrapped by the instance master key. Rotating the
  master key is not an MVP feature (§7), but the structure makes it a rewrap of
  data keys rather than a re-encryption of every value — like the token format,
  cheap now and painful later.
- A documented **backup and restore path** for the whole instance (database dump
  plus key material), including the note that a backup without the key is
  worthless.
- Migrations run automatically at startup. The HTTP API is versioned from the
  first release; CLI and server exchange versions, and the CLI says clearly when
  it is too old or too new.
- First-run wizard: the first user becomes administrator, the default
  organization is created.

### 6.4 Permissions in the MVP

Deliberately trivial for humans: **every user in an organization sees and changes
everything in that organization.** No role model beyond the distinction of who
may invite users and administer the organization.

**Tokens carry the granularity.** Every token has a binding (projects and
environments) and a scope set — `names`, `read`, `write`, `delete` (§5). A
service token defaults to one project, one environment, `names` and `read`.

**Agent tokens.** An agent never acts under a human's session. Without this, the
server cannot tell an agent from the person whose terminal it sits in: the change
log would say "alex" for everything, and the identity type in §6.5 would have no
source. So a human creates an agent token — in the UI or from their own session —
and hands it to the agent through its environment as `VAULTAFFE_TOKEN`, which the
agent harness sets for the agent's process and `run` strips from every child
(§6.2). The point is **attribution, not restriction**. Accordingly the default
binding is the whole organization with every scope; the human narrows it at
creation, and excluding production environments is the first switch offered.
Whether an agent may delete is a scope, not a doctrine.

**Human-only actions** are the short list where an agent's mistake cannot be
undone or where the output is itself a secret: **purging value history or deleted
objects, creating and revoking tokens, and administering the organization and
its users.** These
require a session token. The agent gets an error naming the human command or UI
action. Everything else — reading, running, setting, recoverable deletion,
restoring, creating projects and environments, rolling back — an agent
may do by default.

The honest caveat: an agent token in the agent's process environment is plaintext
on that machine. That is inside the threat model (§4). The human's session token,
by contrast, never leaves the keychain.

### 6.5 Logging and history

Two things that often get lumped together despite having completely different
risk profiles:

**The change log never contains values.** We record timestamp, project,
environment, secret name, action — and **identity along with identity type**
(human session, service token, agent token — the token kind tells us, §5),
because with writing agents that is the interesting question. No value, not even
as a diff. We follow AWS here, not
Doppler: Doppler puts the old and new value into the log and consequently had to
bolt on a redaction feature, which doesn't even truly delete the value.

**Value history is tightly bounded.** Rollback is a real need and writing agents
make it more common, not less — an agent that wrecks a value overnight needs an
undo button. But the useful window is hours, not months, and every retained old
value is usually a still-valid credential. So: **the last five versions, kept for
at most 72 hours**, whichever bound is hit first, and when a version falls out it
is deleted rather than tombstoned. The numbers are a decision, not a placeholder;
if they turn out wrong they change in one config line. Rollback is a write like
any other and available to agents. For comparison: Vault defaults to 10 versions,
AWS guarantees exactly one — Doppler alone keeps them indefinitely.

**Purge is a visible feature**, not a support ticket. After a suspected
compromise the normal expectation is that a key's history genuinely disappears.
Neither Doppler nor AWS offers this cleanly; here we can do better with little
effort. Purge is **human-only** (§6.4): it is the one action that destroys the
undo button, and in an agent's hands it would be anti-forensics.

**Deletion is recoverable, including container deletion.** Deleting a secret,
environment, or project removes it from active listings and use immediately, but
retains its last active state for 72 hours. Deleting a container retains its
active descendants and their current values as one recoverable subtree; it never
cascades into immediate permanent deletion. Previously deleted descendants keep
their original deadlines. Older value versions keep their existing five-version
and 72-hour limits; deletion does not restart those clocks. Repeating deletion
does not extend the recovery deadline.

Container deletion requires delete scope over the entire affected subtree.
Restore is a write operation within the token's binding. Restoring a container
requires access to the whole retained subtree and restores it atomically. Names
remain reserved during the recovery window, so recreating an object cannot
silently replace its recoverable predecessor. Tokens are checked against their
current scopes and bindings; restore neither recreates nor reactivates revoked
tokens. Delete and restore are recorded without values in the change log.

Only a human session can purge retained versions or a deleted object and its
subtree before their deadlines. Scheduled expiry permanently removes retained
data when its window ends. Neither purge nor expiry removes change-log entries.
Thus agent deletion preserves the undo window; permanent early removal is the
same human-only capability regardless of the object's level in the hierarchy.

**Reads are not logged exhaustively.** `run` reads values, but a successful read
does not prove that an application started. The MVP change log records mutations,
not every invocation or application start. After MVP, first and last access per
identity and secret provide an access summary, not an execution history. Agent
identity attribution applies to recorded changes and, when available, these
access summaries.

**What deserves honest documentation:** a purge in the database does not reach
into last night's backup. We promise backups (§6.3); no product we examined says
anything about this contradiction. We should.

### 6.6 Build order

The first release completes the core workflow. Build it in two stages, followed
by an optional post-MVP stage:

1. **API + CLI.** Organization, projects, environments, secrets, the three token
   kinds, `login`, `setup`, `run`, `secrets` with `set`/`get`/`delete`/`import`.
   Bootstrap and human-session token creation are available from the command
   line. Implement the browser confirmation page required by device-code login
   in this stage, before the full management UI. Include the change log and
   recoverable mutations from the first write path.
2. **Complete the MVP journey.** Add the web UI in §6.1 except the missing-key
   notice, including token and user management, import/export, bounded value
   history, rollback, restore, and human-only purge. Deliver Compose with HTTPS
   and documented backup/restore. The MVP ships when this core journey and §11
   work; complete administrative CLI parity is not required.
3. **After MVP.** Missing-key notices with dismissal, first/last access summaries,
   and full CLI parity for human administration. These do not gate release.

## 7. Explicitly not in the MVP

- **Integrations and synchronization** to Vercel, AWS, GitHub Actions, Kubernetes
  and so on. The classic core of Doppler — a non-goal for us. Envisaged long-term
  as a **plugin point** so the community can contribute them; the architecture
  should not preclude it, but we build none of it in the MVP.
- Fine-grained RBAC for humans, user groups, permission inheritance. (Tokens
  already have binding and scope, §6.4 — that is not RBAC, it is the minimum a
  machine credential needs.)
- **Project groups.** Deferred: the organization is the grouping (§5); if the
  multi-organization UI of §12 does not cover the need, we revisit.
- **A native Windows CLI.** WSL runs the Linux binary; a wrapper-based native
  build follows the MVP (§6.2, §12).
- **Email of any kind.** Invitations are links, password resets are done by an
  administrator (§6.1).
- **Master key rotation.** The encryption structure supports it (§6.3); the
  feature comes later.
- **Personal configs / per-user values.** Outside the shared-team-secret scope
  (§5). Personal credentials and private environments require a visibility model
  the MVP does not provide; this is not a claim that those needs are rare.
- Missing-key notices, first/last access summaries, and complete administrative
  CLI parity. These follow the first useful release (§6.6, §12).
- **Branch configs and preview environments.** A throwaway environment per pull
  request is a real pattern, but the value share of it is small — two or three
  differing keys, such as the database URL and your own hostname in the OAuth
  redirect. The large share is CI: create it, provision a database, tear it down
  after the merge. That sits beyond our boundary to integrations. Whoever needs it
  can create an environment via the CLI and delete it again; we build no more than
  that.
- Secret rotation, expiry and reminder dates, automatic renewal. Rotation is a
  workflow the agent runs (§8), not a feature we build.
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
  from day one. (`export` has the same problem and survives only as the
  migration path out, human sessions only — §6.2.)
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
   the workflow needs: write the new value with `--replace`, keep the old one
   briefly in the bounded history (§6.5).
5. **The agent hits a human-only action.** It notices the CI pipeline needs a
   service token, tries to create one, and gets a clear refusal naming the
   command. It hands that command to the human, who runs it in their own console.
   The agent did everything it could; the one step that produces a secret as
   output stayed with a person (§6.4).

For all of these the agent acts under an **agent token** (§6.4), so the change
log shows the agent, not the human whose terminal it sits in. And whatever the
agent can do through the CLI it must also be able to do through the HTTP API — so
that an **MCP server** can later be layered on top as a thin shell, without
touching the backend.

**Where value-blindness applies, and where it doesn't.** `run` has to fetch values;
that is its purpose. `secrets get` deliberately prints exactly one value,
explicitly requested. `run -- env` prints all of them, and `/proc` shows the
child's environment to anyone running as the same user. We fence none of that: a
cooperative agent does not do it, and against a non-cooperative one a secrets
manager with a `run` command has no defense worth promising (§4). The hard line
runs through the *structured* agent interface: a later MCP server gets **no** tool
that returns a value. Names without values are the normal case there, not the
exception.

**Writing yes, deleting by scope.** 1Password's MCP server gives agents write
access but not a single delete tool. We adopt that asymmetry where it costs
nothing: the MCP server gets no delete tool, and removing `delete` from an agent
token is one switch at creation. We do **not** adopt it as the CLI default: an
agent that cannot clean up what it created leaves the mess to a human, and
overwriting — which nobody proposes to forbid — is just as destructive. The real
safeguards are the bounded history (§6.5) and the explicit `--replace` (§6.2).
The empirical finding from the CVE research supports the priorities — there are
documented cases of agents *exfiltrating* credentials, but not one of an agent
overwriting or deleting a secret value. The priority is exfiltration, not
destruction.

## 9. Technical guardrails

- **Backend:** .NET 10, with the HTTP API as the only read/write interface. The
  web UI is a client of that API just like the CLI — no privileged side doors.
- **Database:** PostgreSQL. Secret values encrypted at rest — envelope
  encryption with per-secret data keys under an instance master key from the
  environment configuration (§6.3).
- **Frontend:** React.
- **CLI: Go.** Deliberately not .NET — the CLI does not have to match the backend,
  and one technical argument decides it: `exec()` requires a system call that Go
  (`syscall.Exec`) and Rust expose directly, while .NET offers it only through
  P/Invoke. Add to that a static single-file binary with no runtime,
  cross-compilation for every target in one step, mature keyring libraries, and
  millisecond startup — the last one counts, because `vaultaffe run` sits in front
  of *every* command. Rust would be the equivalent alternative. Doppler's own CLI
  is written in Go; we read it as a reference but adopt no code. Apache-2.0 code
  could legally live in an MIT project with its notices intact, but the result
  would be "MIT with an asterisk" — the thing §10.2 promises not to be. Rust's
  edge in this field (`secrecy`/`zeroize`, sandboxing
  via Landlock/seatbelt — the reason the Codex CLI was rewritten in Rust) only
  applies once the CLI not only starts the child process but also isolates it.
  That is a non-goal; the decision gets revisited only if it stops being one.
- **Deployment:** Docker Compose as the first-class path, Caddy in front (§6.3).
- **Local token storage:** the OS keychain for session tokens. If it isn't
  available (a headless Linux box without a Secret Service, say) we say so loudly
  and name the two alternatives: `VAULTAFFE_TOKEN` from the environment, or a
  file the user explicitly opts into. Doppler silently falls back to plaintext in
  that case, undocumented. Precisely not that. Agent tokens never touch the
  keychain; they live in the agent's environment by design (§6.4).
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
   seeing it", "create an empty placeholder", or "this change was made by an
   agent, not by the person whose terminal it ran in". That is the gap.

### 10.3 What is still open

- ~~Process group and signal behavior needs to be verified in practice, not just
  inferred from someone else's source.~~ **Answered.** Measured on macOS and Linux
  under a real terminal:
  [ADR 0009](docs/adr/0009-run-replaces-itself-and-does-everything-else-first.md).
  `exec()` holds — the process is the same one, a signalled command reports
  `128+N`, Ctrl-C and Ctrl-Z including `fg` behave as if the shell had started it,
  and a `kill` aimed at the pid reaches the command rather than orphaning it. What
  it does not solve is `run`'s to do before the call: the reporting, the `PATH`
  lookup with 126 and 127, and the environment it hands over.
- Doppler's API reference was consistently blocked by Cloudflare; the HTTP field
  names are evidenced only from the Go structs in the CLI.
- A fallback cache for offline operation: whether we want one in the MVP at all.
  Doppler's caching apparatus is explained retroactively by its API limits —
  secret reads are rate-limited separately (120–480/min) and every `run` is a read.
  As self-hosters we simply do not have that problem; for us it would purely be
  about working offline. If we do build it, then deliberately unlike Doppler:
  its file derives the key from `token:project:config`, has no expiry, and a
  revoked token does not lock the machine out.
- Whether shared team secrets alone cover the initial audience's needs (§5).
  Frequent `dev-<firstname>` environments can indicate demand for individual
  values. Environment inheritance alone would not provide privacy; any later
  support for personal credentials also needs an explicit visibility model.
- How an agent harness gets `VAULTAFFE_TOKEN` into the agent's process in
  practice, per tool (Claude Code, Cursor, Codex): a settings file, a hook, a
  wrapper script. The mechanism is the harness's, not ours, but the documentation
  has to show it for each, or the agent-token model stays theoretical.

## 11. Success criteria for the MVP

- From `git clone` to a running instance **with HTTPS** and the first secret in
  **under 10 minutes**.
- `vaultaffe run -- <cmd>` works reliably on macOS and Linux, including correct
  exit codes and signal forwarding. Windows is not a criterion (§7).
- An AI agent can start an application and create a new secret without a single
  secret value appearing in its transcript. The change log attributes secret
  creation to the agent, not to the human; it does not promise an event for each
  application start.
- A team migrates its `.env` secrets and removes the secret-bearing files
  afterwards; non-secret local configuration can stay local.
- The README explains the entire product in one screenful.

## 12. Afterwards (outlook, not MVP)

First complete the deferred convenience features from §6.6: missing-key notices
with dismissal, first/last access summaries, and administrative CLI parity.
Then, roughly in this order:

1. Multiple organizations in the UI, and switching between them.
2. An MCP server as the official agent interface alongside the CLI — value-blind
   (§8) and on the same HTTP API, with no special path in the backend. Open
   question: **elicitation** would be the clean way to accept a value from a human
   without the agent seeing it, but the URL mode required for that is so far
   documented for only a single client. It needs a runtime capability check and a
   defined failure path. Tool annotations (`readOnlyHint` and relatives) are no
   foundation to build on; the MCP project itself considers them a failure.
3. Roles and access restrictions **for humans** at project/environment level (at
   minimum: restricting production environments). Tokens already have this
   granularity (§6.4); this is about bringing people to the same level. Doppler's
   `restricted` visibility is worth a second look here: it separates not by role
   but by **identity type** — a value a machine may read and a human never gets to
   see in the UI. That is exactly the direction of thinking our agent focus needs,
   only approached from the other side — and our token kinds already carry the
   identity type it needs.
4. A plugin mechanism for integrations — with the goal that the community
   contributes them, not us.
5. Opt-in propagation of secrets across environments: a value the user
   deliberately writes into several environments, and building on that, the
   question of whether and how an agent may trigger it.
6. A native Windows CLI with the wrapper approach (§6.2), and master key rotation
   on top of the envelope structure (§6.3).
7. Webhooks / change notifications.
8. SSO for organizations that need it.
9. Project groups — only if multiple organizations turn out not to cover the need
   (§5).

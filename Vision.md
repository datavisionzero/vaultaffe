# Vaultaffe — Product Vision

> Working title. An open-source, self-hostable secrets manager for developers
> who work with AI agents.

**License:** MIT · **Status:** pre-MVP — no implementation yet.

## The promise

Vaultaffe helps solo developers and small teams work on applications with AI
agents without having to copy credentials into prompts, transcripts, or project
files for normal development workflows. Secrets stay centrally managed on the
team's own infrastructure and reach the processes that need them through a
simple CLI.

**An agent can use a secret by name without needing to see its value.** This is
workflow convenience, not an access barrier: an authorized agent can still read
values, and a process can print its environment. We assume a cooperative agent;
preventing deliberate exfiltration by a compromised agent is outside our scope.

## Who it is for

Our first users are solo developers and small, technically experienced teams
that work daily with CLI-based AI agents and operate their own development
infrastructure. They want shared credentials out of scattered `.env` files and
agent conversations, without operating an enterprise vault.

The MVP manages a team's shared secrets. Personal credentials and private
development environments are not covered: all human members of an organization
can access its secrets. Agencies with customer-separation requirements and
organizations needing granular human permissions are not the initial target.

## The experience

Secrets belong to a project and an environment. A small web UI handles human
management; the CLI supports everyday development and agent workflows.

- **Start an application:** `vaultaffe run -- npm run dev` gives the process its
  credentials without requiring the agent to read or copy their values.
- **Store a new credential:** a command pipes a generated value directly into
  `vaultaffe secrets set KEY`. The confirmation does not expose the value.
- **Prepare work for a human:** the agent creates an empty placeholder and names
  what is missing. The human fills it in; the agent can continue without seeing it.

Agents act under their own tokens, so recorded actions identify the agent rather
than impersonating the person at the terminal. Changes can be recovered within
a bounded history window. Import and export provide a practical way in and out.

## What we choose

- **Simple operation:** Docker Compose, PostgreSQL, HTTPS, and a documented
  backup and restore path. No external mail service is required.
- **Useful defaults:** names and status without values, input through stdin,
  explicit overwrites, and clear errors when a human must act.
- **A focused secret store:** projects and environments, without configuration
  inheritance, personal overrides, or an integration platform.
- **Open source throughout:** MIT, with no separately licensed feature tier.
- **A realistic security boundary:** encrypted storage and fewer routine copies
  of credentials; no promise of zero knowledge or protection from host root access.

The MVP excludes fine-grained human permissions, provider integrations, automatic
rotation, dynamic credentials, SSO, and native Windows support. Linux and macOS
are the CLI targets; Windows users can use WSL.

## The first useful release

The MVP completes the core journey: self-host, sign in, import existing secrets,
give an agent its own access, start an application, change credentials, and
recover mistakes. It includes the web UI, essential CLI workflows, a value-free
change log, bounded recovery, and backup/restore.

Missing-key notices, access summaries, and full CLI parity for administrative
screens can follow. They do not gate the first release.

Success means:

- From `git clone` to a running instance with HTTPS and the first secret in
  under ten minutes.
- `vaultaffe run` works reliably on macOS and Linux, including exit codes and
  signal behavior.
- An agent can start an application and create a secret without a secret value
  appearing in its transcript. The creation is attributed to its agent identity;
  exhaustive logging of application starts is not promised.
- A team migrates its `.env` secrets into Vaultaffe and removes the secret-bearing
  files afterwards; non-secret local configuration can stay local.
- The README explains the product in one screenful.

Detailed behavior, recovery rules, build order, architecture, and research
rationale live in [Specification.md](Specification.md).

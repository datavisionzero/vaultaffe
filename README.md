# Vaultaffe

A self-hostable secrets manager for solo developers and small teams who work
daily with CLI-based AI agents and run their own infrastructure. Secrets by
project and environment, a web UI to manage them, and a CLI that puts them into
any process as environment variables — no temporary `.env` on disk:

```sh
vaultaffe run -- npm run dev
```

Hosting it is one `docker compose up`, with HTTPS, one Postgres, and one
documented backup that holds the key beside the data.

## The one thing that is different

Agents run commands, start dev servers and provision services, and they need
credentials to do it — and an agent holding a token in plaintext leaks it into
transcripts, logs and model providers. Vaultaffe's default workflows let **an
agent use a secret by name without seeing its value**:

```sh
vaultaffe secrets                                          # names and status — never values
stripe api-keys create | vaultaffe secrets set STRIPE_KEY  # a value arrives only on stdin
vaultaffe secrets set SMTP_PASSWORD --empty                # a placeholder for a human to fill
```

The agent acts under [its own token](docs/agents.md), so the change log says who
did what. This is a workflow property, not an access barrier: an authorized agent
can read values, `run` hands them to the process it starts, and an agent hijacked
into exfiltrating them is beyond what a secrets manager with a run command can
prevent. We don't pretend otherwise.

## What it deliberately is not

No integrations with Vercel, AWS or Kubernetes. No fine-grained RBAC — everyone
in an organization sees everything, and tokens carry the binding and the scopes.
No rotation, no dynamic secrets, no compliance tooling, no SSO, no zero-knowledge
encryption. No native Windows CLI; WSL runs the Linux binary. Shared team
secrets, not personal ones. That list is the product decision, not a roadmap of
regrets — if you need things from it, [Infisical](https://infisical.com) is the
better tool and we would rather say so.

## Running one

```sh
cp deploy/.env.example deploy/.env    # and make the two values it asks for
docker compose -f deploy/docker-compose.yml up -d
open http://localhost                 # the first person here is the administrator
```

> **Status: pre-MVP.** It runs and it is usable, but nothing is released yet: the
> first `up` builds the image from the checkout, and there are no CLI binaries to
> download.

.NET 10, React, PostgreSQL, Caddy for TLS. The CLI is Go, because `exec()` wants
a system call Go exposes directly and a static binary with millisecond startup
sits in front of every command.

| | |
| --- | --- |
| [`Vision.md`](Vision.md) · [`Specification.md`](Specification.md) | what this is, and how it behaves |
| [`docs/operations.md`](docs/operations.md) | running an instance: the key, upgrades, backup and restore |
| [`docs/agents.md`](docs/agents.md) | handing an agent its token, per harness |
| [`docs/cli.md`](docs/cli.md) · [`docs/api.md`](docs/api.md) · [`docs/human-interface.md`](docs/human-interface.md) | the three surfaces |
| [`docs/codebase.md`](docs/codebase.md) · [`docs/storage.md`](docs/storage.md) | the layout, and the data model |

MIT. All of it — no `ee/` directory, no feature behind a second license.

# Vaultaffe

A self-hostable, open-source secrets manager for solo developers and small teams
that work daily with CLI-based AI agents and operate their own infrastructure.

> **Status: pre-MVP. Vision, specification, and the data model.**
> Nothing here installs or runs yet — there is a schema and no way in. Read
> [`Vision.md`](Vision.md) for the product direction,
> [`Specification.md`](Specification.md) for detailed behavior and architecture,
> [`docs/codebase.md`](docs/codebase.md) for the layout and
> [`docs/storage.md`](docs/storage.md) for the data model. If you need a working
> self-hosted secrets manager today, use [Infisical](https://infisical.com).

## What it will be

Secrets organized by project and environment, a web UI to manage them, and a CLI
that injects them into any process as environment variables:

```bash
vaultaffe run -- npm run dev
```

The CLI injects values without writing a temporary `.env` file. One `docker compose up` to host
it yourself — with HTTPS included — one Postgres, one documented backup path.

## The one thing that is different

AI agents run commands, start dev servers, and provision services — and they need
credentials to do it. An agent holding a token in plaintext leaks it into
transcripts, logs, and model providers.

Vaultaffe's default workflows let **the agent use a secret by name without
needing to see its value.**

```bash
vaultaffe secrets                          # lists names and status — never values
stripe api-keys create | vaultaffe secrets set STRIPE_KEY   # value only via stdin
vaultaffe secrets set SMTP_PASSWORD --empty                 # a placeholder for a human to fill
```

An agent can pipe a freshly created API key straight into the store without ever
reading it, and can prepare a key for a human to fill. These workflows deliver
values to processes without requiring them in the model context. The agent acts under its own token, so
the change log says who did what — a person or an agent.

This is a workflow property, not an access barrier, and assumes a cooperative
agent. An authorized agent can read values. `run` hands values to the process it
starts, and an agent hijacked into exfiltrating them is beyond what a secrets
manager with a run command can prevent. We don't pretend otherwise.

## What it deliberately is not

No integrations to Vercel, AWS, or Kubernetes. No fine-grained RBAC — everyone in
an organization sees everything, while tokens carry binding and scope. No secret
rotation, no dynamic secrets, no compliance tooling, no SSO. No zero-knowledge
encryption. No native Windows CLI in the MVP; WSL works.

The MVP covers shared team secrets; personal credentials and private development
environments are outside its scope.

That list is the product decision, not a roadmap of regrets. If you need items
from it, [Infisical](https://infisical.com) is the better tool and we would rather
say so than pretend otherwise.

## Stack

.NET 10 backend, React frontend, PostgreSQL, Docker Compose with Caddy for TLS.
The CLI is written in Go, because `exec()` needs a system call Go exposes directly
and because a static binary with millisecond startup sits in front of every
command.

## License

MIT. All of it — no `ee/` directory, no feature behind a second license.

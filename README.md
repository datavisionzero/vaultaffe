# Vaultaffe

A self-hostable, open-source secrets manager for teams that work with AI agents.

> **Status: pre-MVP. There is no code yet — only a vision.**
> Nothing here installs or runs. If you want to know where this is going, read
> [`Vision.md`](Vision.md). If you need a working self-hosted secrets manager
> today, use [Infisical](https://infisical.com).

## What it will be

Secrets organized by project and environment, a web UI to manage them, and a CLI
that injects them into any process as environment variables:

```bash
vaultaffe run -- npm run dev
```

No temporary `.env` file, no plaintext on disk. One `docker compose up` to host
it yourself, one Postgres, one documented backup path.

## The one thing that is different

AI agents run commands, start dev servers, and provision services — and they need
credentials to do it. An agent holding a token in plaintext leaks it into
transcripts, logs, and model providers.

Vaultaffe's rule: **the agent knows the secret's name, not its value.**

```bash
vaultaffe secrets                          # lists names — never values
stripe api-keys create | vaultaffe secrets set STRIPE_KEY   # value only via stdin
vaultaffe secrets set SMTP_PASSWORD --empty                 # a placeholder for a human to fill
```

An agent can pipe a freshly created API key straight into the store without ever
reading it, and can prepare a key it knows is needed but must not see. Values
reach processes, not model contexts.

## What it deliberately is not

No integrations to Vercel, AWS, or Kubernetes. No fine-grained RBAC — everyone in
an organization sees everything, while tokens are tightly scoped. No secret
rotation, no dynamic secrets, no compliance tooling, no SSO. No zero-knowledge
encryption.

That list is the product decision, not a roadmap of regrets. If you need items
from it, [Infisical](https://infisical.com) is the better tool and we would rather
say so than pretend otherwise.

## Stack

.NET 10 backend, React frontend, PostgreSQL, Docker Compose. The CLI is written in
Go, because `exec()` needs a system call Go exposes directly and because a static
binary with millisecond startup sits in front of every command.

## License

MIT. All of it — no `ee/` directory, no feature behind a second license.

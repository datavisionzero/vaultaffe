# An Unclaimed Instance Holds Its Own Claim Secret

`POST /api/v1/instance` — the first run — requires a **claim secret** that the
instance generates for itself before it serves anything, keeps for as long as it
is unclaimed, and writes to its own log at every start. Whoever can read that log
can start the instance; whoever merely reaches the port cannot.

This **supersedes
[ADR 0007](./0007-the-first-run-is-unauthenticated-and-happens-once.md)**, which
decided the opposite and named the window it left open. Everything else 0007
settled still stands: the first run happens exactly once, it authenticates
nobody, `GET /api/v1/instance` answers without a token, and adding people later
is a different act with a different door.

## What changed, given that 0007 saw this coming

0007 turned down a bootstrap secret and gave three reasons. Two of them are about
a mechanism this is not, and the third was put too strongly.

- *"It is a second secret in the operator's `.env`, and one they only ever use
  once."* — This is not that. **The instance makes it**, so `deploy/.env.example`
  gains nothing, there is no second `openssl rand` before the first screen, and
  there is nothing to skip by copying an example file. The
  [§11](../../Specification.md#11-success-criteria-for-the-mvp) budget takes one
  command, not one more required value: `docker compose logs vaultaffe`.
- *"The window is already there and this does not close it — a bootstrap secret
  protects against somebody who can reach the port and not the disk."* — That is
  the attacker. Somebody who reaches the disk already has `deploy/.env` and with
  it the master key; they were never inside the threat model of
  [§4](../../Specification.md#4-guiding-principles). Somebody who reaches only
  the port is the internet, and between `docker compose up -d` and the operator
  opening a browser that is exactly who the first run was open to.
- *"The honest fix is not a secret, it is the sentence."* — The sentence is still
  true and still in [`operations.md`](../operations.md): do the first run now
  rather than tomorrow. It is now advice about tidiness rather than the only
  thing standing between an instance and a stranger.

## What it costs, and what was not built

**It is stored as it is read.** Every other credential here is kept as a hash,
because the instance never has to say it again; this one it does, at every start,
so that a closed terminal is not a lost instance. Hashing it would force a new
secret at each start and fill the log history with values that no longer work —
a worse trap than the row this keeps. What the row guards is an **empty**
instance, and the first run deletes it in the same transaction that consumes it.

**No expiry and no lockout.** 256 bits are guessed or they are not. A lockout
would be a way to make an unstarted instance unusable to its own operator, which
is a denial of service dressed as a hardening.

**No loopback exception.** Behind Caddy every request arrives from the proxy, so
"from localhost it does not matter" cannot be told from "through the proxy it
does" without trusting a forwarded header. An exception that can be spoofed is
the whole hole again.

**Not in the OpenAPI document.** The secret travels in `Vaultaffe-Claim`, beside
the other credentials this API takes, and headers are described in
[`api.md`](../api.md) rather than in the contract — the same place `Authorization`
and `Vaultaffe-Client` are described, and for the same reason.

## Consequences

**One value reaches a log, and it is named as the exception.**
[§6.5](../../Specification.md#65-logging-and-history) makes a value in a log line
a bug rather than an untidiness, and that rule is not softened: this is nobody's
secret, it is the key to an empty instance, and it stops existing the moment the
instance holds anything worth keeping. The exception is written into §6.5 so that
the rule and the code do not disagree.

**A table with no organization on it.** Every other table carries one and is
filtered by it ([§9](../../Specification.md#9-technical-guardrails)); this row
exists precisely in the window when there is no organization to carry.
`TenancyTests` lists it as an exception by name rather than letting one appear by
omission.

**Existing installations need nothing.** A started instance never reaches the
first run again, so the change is additive: the migration adds an empty table
that such an instance never writes to.

**A lost secret is a restart.** That is what makes this safe to adopt, and it is
the sentence the operations guide has to keep.

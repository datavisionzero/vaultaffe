# There Is No Offline Cache

`vaultaffe run` reaches the instance every time. No value is ever kept on a
developer's machine, there is no flag that would keep one, and there is no
follow-up ticket for building one.

[Specification §10.3](../../Specification.md#103-what-is-still-open) recorded a
fallback cache for offline operation as open — "whether we want one in the MVP
at all" — with the note that Doppler's caching apparatus is explained
retroactively by its own API limits. This is the spike that answers it, and the
answer is **no**.

## What was measured

An instance from this repository at `0.0.0-dev` against Postgres 18, one
project, one environment, **50 secrets**, with `vaultaffe run -- /usr/bin/true`
timed from a shell. Round-trip latency was injected by a proxy in front of the
instance that holds every chunk for half the stated time in each direction, so
that a link and not a sleep is what the CLI sees. Measured on **macOS 26.6
(arm64)** with **Go 1.27.1**.

| | one request | `run`, 50 keys |
| --- | --- | --- |
| loopback | 0.01 s | **0.05 – 0.09 s** |
| 40 ms round trip | 0.07 s | **0.42 – 0.48 s** |

The shape behind the second column is in `run.go`: one listing, then one read
per key, eight at a time. Fifty keys is therefore one round trip plus seven
waves, and eight round trips at 40 ms is 0.32 s — which is what the measurement
says, so the number is understood rather than merely observed.

What happens with nothing there was measured too, because it is the case the
cache would be for:

```
$ vaultaffe run -- npm run dev
vaultaffe: the instance could not be reached: … connect: connection refused
$ echo $?
10
```

Exit **10** is `unreachable` and it already exists (`docs/cli.md`). Being
offline is a named outcome today, not a hang and not a stack trace.

## Why the answer is no

**The cost a cache would buy off is not one we pay.** Doppler rate-limits
secret reads separately and every `run` is a read, so a cache there is what
keeps a team under a quota. We are the quota. Half a second in front of
`npm run dev` — over a link slower than most teams' instance, which usually sits
on their own network — is not a problem anybody would trade a plaintext file for.

**What is left is offline work alone, and that is a smaller case than it
sounds.** The developer who cannot reach the instance usually cannot reach the
staging database, the registry or the API the application talks to either. A
cache would let `npm run dev` start; it would not make the thing it started work.

**It puts back the file the product exists to remove.** The success criterion is
that a team migrates its `.env` secrets and *removes the secret-bearing files*
([Vision](../../Vision.md), [Specification §11](../../Specification.md#11-success-criteria-for-the-mvp)).
A cache is a `.env` with worse properties: nobody put it in `.gitignore`, nobody
remembers it is there, and it is written by us rather than by the person whose
disk it is on. Having argued a team out of one file, we would be writing them
another.

**Three decisions would each have to be got right where the reference
implementation got them wrong** (§10.3): a key not derived from
`token:project:config`, an expiry where there is none today, and a revoked token
that actually locks the machine out. The first two are work. The third is not
solvable on the client at all — a cache a revoked token can still open is a
credential that outlives its revocation, and a cache that asks the instance
whether the token still stands is not offline. Any honest version of this
feature ends at "revocation is not immediate on machines that hold a copy",
which is a sentence a secrets manager should not have to write.

## What the answer is instead

**Unreachable stays a refusal, and a named one.** `run` says what it could not
reach and leaves with 10.

**The offline path out is `secrets export`, and it is deliberately a person's.**
It is human-only (§6.2), it produces a file the person asked for, and they know
it exists because they typed the command that made it. That is the whole
difference from a cache: an explicit act with a file somebody is responsible
for, rather than an invisible copy kept fresh behind them.

**Reaching the instance is an operations question**, and it is answered where
operations questions are: the instance is theirs, `docs/operations.md` says how
to keep it up, and [ADR 0017](./0017-a-backup-is-one-file-and-it-holds-the-key.md)
says how to get it back.

## Consequences

**No ticket follows this one.** The spike was allowed to answer no, and it did;
the epic it sits in explicitly does not gate anything, so nothing waits on it.

**A pull request adding a cache, an `--offline` flag, or "reuse the last
successful read" is adding the file this decision exists not to have**, and
revisits this ADR before the first line of it.

**What would reopen it** is evidence rather than anticipation: members of a team
regularly working somewhere the instance cannot be reached, with the work they
are doing there being work a cache would actually unblock. If that arrives, the
three points above are the design brief and not the starting position — and the
third one is still unsolved, so the honest reopening starts by deciding what
revocation is allowed to mean.

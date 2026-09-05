# vaultaffe

Instructions for coding agents working in this repository. See
[Vision.md](Vision.md) for what Vaultaffe is and what it deliberately is not,
and [Specification.md](Specification.md) for how it behaves.

## Language

Everything in the repository is written in **English**, regardless of the
language a contributor speaks: source code, identifiers, comments, docs, ADRs,
commit messages, PR titles and bodies, and issues.

Working notes and drafts under `scratchpad/` are exempt — they are local and
never pushed (see below).

## Repository and contributions

- **Host**: GitHub — `datavisionzero/vaultaffe`. Public, MIT — all of it, with
  no `ee/` directory and no feature behind a second license.
- **Layout**: where the code lives, which project holds what, and which way the
  dependencies point is [`docs/codebase.md`](docs/codebase.md). Read it before
  adding a file. The data model and the rules the database itself holds are
  [`docs/storage.md`](docs/storage.md); the HTTP surface — versions, headers, the
  shape of a refusal — is [`docs/api.md`](docs/api.md), and the contract it
  describes is checked in beside it.
- **Language of the domain**:
  [Specification §5](Specification.md#5-core-concepts) is the glossary —
  organization, project, environment, secret, token. Code, identifiers, the HTTP
  contract and the CLI use those names without exception, and a concept that
  needs a name the specification does not have gets settled there first.
- **Decisions**: [`docs/adr/`](docs/adr/) holds decisions the specification does
  not already make. Read the ones that touch the area you are about to work in,
  and say so explicitly when your work contradicts one instead of silently
  overriding it.
- Contributions arrive as pull requests from forks. Maintainers may push to
  `main` directly.
- Commit and push only when asked to.

## Branching

The repository is a **trunk**: `main` is the only long-lived branch, and it is
always in a state that could be released
([ADR 0001](docs/adr/0001-the-repository-is-a-trunk.md)).

- Committing straight to `main` is the normal path for maintainers.
- A short-lived branch is optional — take one when the work is large, risky, or
  wants review, and merge it back within days, not weeks.
- Whatever the path, CI has to be green on `main`. A red trunk is fixed or
  reverted before anything else is pushed on top of it.

## Before pushing

Three checks, every time, because a public repository does not forget.

1. **No personal information.** No real names, private e-mail addresses, home
   or IP addresses, hostnames of private machines, absolute paths carrying a
   user name, or anything else that identifies a person. Commit authorship and
   `datavisionzero` are the exception — that is the account this is published
   under. Check the diff, not just the files you meant to change:
   `git diff --staged` and `git log -p @{u}..` before the push.
2. **No secrets.** No tokens, connection strings, private keys or `.env`
   contents, not even expired or example ones that look real.
3. **No secret values in anything this product writes.** That is the same check
   pointed inward, and in this repository it is a product rule rather than
   hygiene: the change log never contains values, listings return names and
   status, and a log line, an error message or a test fixture that carries one
   is a bug ([Specification §6.5](Specification.md#65-logging-and-history)).

When something has to be written down that fails any of them, it belongs in
`scratchpad/`, which is ignored by git.

## The scratchpad

`scratchpad/` is the local working area — notes, drafts, throwaway experiments.
It is in `.gitignore` and never reaches the remote. If the directory does not
exist, proceed silently; it is not part of the published repository.

The scratchpad keeps the working level out of the public repository. Where a
maintainer's own working items live is their business and is not described here.
**GitHub issues are where things are reported and discussed in the open**, and
they are English like everything else that reaches the remote.

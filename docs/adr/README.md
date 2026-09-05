# Architecture Decisions

This directory contains decisions that are difficult to reverse, would be
surprising without context, and resulted from a genuine trade-off. Product scope
and promises belong in the [product vision](../../Vision.md), and behavior,
build order and the technical guardrails in the
[specification](../../Specification.md) — an entry there names what was chosen,
an ADR here explains why the obvious alternative was not.

Most of the technical direction is therefore *not* here: .NET, PostgreSQL,
React, a Go CLI, envelope encryption and the three token kinds are settled in
[Specification §9](../../Specification.md#9-technical-guardrails) with their
reasoning attached, and copying them into ADRs would create a second place to
keep current.

## Naming

ADRs are numbered sequentially as `NNNN-short-slug.md`. The next number follows
the highest existing number.

## Short form

```md
# Short decision title

One to three sentences describe the context, decision, and rationale.
```

Status, considered options, and consequences are included only when they add
material value to understanding the decision.

## Decisions

- [0001 – The repository is a trunk](./0001-the-repository-is-a-trunk.md)
- [0002 – The backend is four layers, not one project](./0002-the-backend-is-four-layers-not-one-project.md)
- [0003 – A name inside a reference is narrow and lower-case](./0003-a-name-inside-a-reference-is-narrow-and-lower-case.md)
- [0004 – The token format and the envelope](./0004-the-token-format-and-the-envelope.md)
- [0005 – The API carries its version in the path](./0005-the-api-carries-its-version-in-the-path.md)
- [0006 – The contract is checked in, and the web client is generated from it](./0006-the-contract-is-checked-in-and-the-web-client-is-generated-from-it.md)
- [0007 – The first run is unauthenticated, and happens once](./0007-the-first-run-is-unauthenticated-and-happens-once.md)
- [0008 – A session is a token, and the only page asks for a password](./0008-a-session-is-a-token-and-the-only-page-asks-for-a-password.md)
- [0009 – `run` replaces itself, and does everything else first](./0009-run-replaces-itself-and-does-everything-else-first.md)
- [0010 – A refusal names the action, and the client names the command](./0010-a-refusal-names-the-action-and-the-client-names-the-command.md)

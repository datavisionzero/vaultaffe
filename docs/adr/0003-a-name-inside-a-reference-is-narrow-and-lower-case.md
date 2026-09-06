# A Name Inside a Reference Is Narrow and Lower-Case

A project's and an environment's name match
`^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$`, at most 64 characters. A secret's name
matches `^[A-Z_][A-Z0-9_]*$`, at most 128 — that one the specification already
states, and it is here only because the same reasoning holds it in place.

[Specification §5](../../Specification.md#5-core-concepts) fixes
`vaultaffe://<project>/<environment>/<KEY>` now, although nothing in the MVP
resolves one, and says the two names therefore "stay narrow — no spaces". This
decides what narrow is. It has to be decided in the first design pass for the
same reason the token format does: by the time anything resolves a reference,
people have written names into scripts, `.env` files and each other's
documentation, and a rule can only ever be loosened afterwards, never tightened.

The obvious alternative is to let a team call a project what it likes and escape
the name where it appears. That is the expensive one, and 1Password is the
worked example: it allows spaces in the names its `op://` references address, and
its own template syntax trips over them. Escaping is not one decision but one per
context — a URL, a shell line, a YAML file, a log message — and every one of them
is a place a reference can be read back wrong.

**Lower-case is the part that is a genuine restriction**, and the reason is the
unique index rather than taste. Project names are unique per organization; if
case were significant, `Prod` and `prod` would be two different environments that
no human reading a deployment log could tell apart, and if it were not, the
database would have to be told so in every index and every lookup. Making the
name lower-case settles it once. Every name in the specification's own examples —
`webshop-api`, `landing-page`, `dev`, `staging`, `prod`, `dev-robin` — is already
of that shape.

## Consequences

**The rule is enforced twice, and the second one is the database.** The domain
type refuses a bad name before a row is built, and a check constraint refuses it
if a future act forgets to ask. The pattern is written once, unanchored, and each
side adds its own anchors — `^…$` for Postgres, `\A…\z` for .NET, because .NET's
`$` also matches before a trailing newline and would let `DATABASE_URL\n` pass a
rule that reads as if it could not. A key with an invisible newline on the end is
the kind of bug Specification §6.2 spends a paragraph on; it should not enter
through the name as well.

**Nothing in a reference needs escaping, and that is the whole return.** None of
the three parts can contain a `/` or a space, so parsing one is a `Split` and
formatting one is interpolation. `SecretReferenceTests` asserts exactly that.

**A team importing from elsewhere may have to rename.** Doppler allows names this
rule does not. The migration path is `.env` import per environment
([§6.2](../../Specification.md#62-cli)), which carries secret names — those follow
the same rule in both products — and not project names, so the cost falls on
whoever sets the projects up once, not on the values.

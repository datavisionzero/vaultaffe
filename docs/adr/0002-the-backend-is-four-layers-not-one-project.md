# The Backend Is Four Layers, Not One Project

The backend is `Vaultaffe.Domain`, `Vaultaffe.Application`,
`Vaultaffe.Infrastructure` and `Vaultaffe.Api` on .NET 10, with dependencies
pointing inward and the compiler holding them there. The obvious alternative is
one project with folders named after features, and for a product whose entire
case is being small it is a serious one: four projects for a secret store a
solo developer runs is a great deal of structure, and every feature touches
three or four of them.

Two sentences in the specification decided it.

**"The HTTP API is the only read/write interface. The web UI is a client of that
API just like the CLI — no privileged side doors"**
([§9](../../Specification.md#9-technical-guardrails)). That is a promise about
where the use cases live, made before a line of code existed. There are two
adapters over one set of acts from the first day, an MCP server is already named
as a plausible third ([§6.2](../../Specification.md#62-cli)), and layering turns
"they cannot drift" from a discipline into a compile unit: setting a secret is
one type in Application, and no adapter can reach the database on its own to
grow a capability the others lack.

**"All queries are organization-filtered by default, enforced in one central
place rather than in every handler"** (§9). A rule that has to hold in exactly
one place needs a place that is exactly one — and the specification says why in
the same breath: getting this right now makes several organizations later a UI
question rather than a migration. The same is true of the two other things §6.6
calls unreversible. The envelope encryption is a port Application asks and
Infrastructure answers, so what is wrapped under what is decided once. The token
format and its scope set are rules, not plumbing, and they belong in a project
that references nothing.

## Consequences

**The risk taken on is an anemic domain** — four projects in which
`Vaultaffe.Domain` holds nothing but data classes and every rule lives a layer
up. The guard is a rule that can be checked by reading: **anything the
specification already states as a rule belongs in Domain.** The secret name
pattern, the three token kinds and their scope sets, what an empty placeholder
stops, the five-version and 72-hour bounds on value history, the 72-hour
recovery window on a deletion — all of that is of that kind. If those end up in
Application, the innermost project is ballast and this decision was not worth
its cost.

**A value never travels in plaintext through a layer that has no business
holding it.** Encryption is Infrastructure's, and the acts above it deal in
"the value of this secret" rather than in ciphertext and data keys. The
practical test is the one the product is built on: an act that only needs names
and status must be expressible without a path that decrypts anything
([§6.2](../../Specification.md#62-cli)).

**Atomicity is Infrastructure's problem, and it is why the ports are coarse.**
"Set this value unless one is already there" is one act and one round trip, a
conditional write in one transaction — the explicit-overwrite rule of §6.2 is
not a check a caller performs and then a write it follows with.

**A new field touches every layer.** That is the recurring price, and this
product pays it rarely: the model is four nouns and a token, and the whole case
of the vision is that the list stops there.

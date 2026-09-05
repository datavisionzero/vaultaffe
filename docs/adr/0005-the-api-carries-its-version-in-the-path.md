# The API Carries Its Version in the Path

Every endpoint of this API lives under `/api/v1/`, and the two that describe the
API itself — `GET /api/handshake` and the problem catalogue at `/problems` — do
not. A client asks the handshake which versions an instance serves and picks one;
a client that guesses and picks one this instance does not have is told
`unsupported-api-version` rather than handed a 404 it has to interpret.

[Specification §6.3](../../Specification.md#63-operations) settles *that* the API
is versioned from the first release. This settles *where* the version is, which
is the part that cannot be changed afterwards for the same reason the token
format cannot: by the time it matters, the string is in a CI configuration, a
`curl` line in somebody's runbook and a generated client nobody regenerates.

## Why the path and not a header

The obvious alternative is a version header, and it is the one an API purist
picks — one resource, many representations. It loses on the three things that
actually happen to this product:

- **A path is visible in a log, a proxy rule and a `curl` line.** Which version
  broke is readable in a Caddy access log without anyone having thought to log a
  header. This is a product that self-hosters operate themselves
  ([§4](../../Specification.md#4-guiding-principles)), and what an operator can
  see without instrumentation is worth more here than in a product with a support
  team.
- **Two versions can be served side by side without a single conditional.** They
  are two route groups. With a header they are one route with a branch inside it,
  and the branch is the thing that gets forgotten in the endpoint added next.
- **A generated client takes it for free.** The path is in the OpenAPI document
  ([ADR 0006](./0006-the-contract-is-checked-in-and-the-web-client-is-generated-from-it.md));
  a header would be a default every generated client has to be configured to
  send, in two languages.

The cost is the one the purist names: the URL of a resource changes when the
contract does, and a link stored under `/api/v1/...` is a link to a version. This
product stores no such links — its references are
`vaultaffe://<project>/<environment>/<KEY>` and carry no host at all
([§5](../../Specification.md#5-core-concepts)) — so the cost is not paid here.

Note that planaffe — the sibling project this repository takes its house style
from — decided the opposite for itself, and for a reason that does not hold here:
its CLI and its instance are cut from one release and upgraded together. Vaultaffe's CLI is a static binary that lives on a laptop, in
a CI image and inside an agent's container, and is upgraded by whoever remembers
to. Skew is the normal state, not the incident.

## Consequences

**The version exchange is a handshake plus a floor.** The handshake names the
product, the release, the contract versions served and the oldest client release
still answered. Every response carries `Vaultaffe-Version`, the refused ones
included, so a client can report skew from whatever answer it got. A client
announcing `Vaultaffe-Client` below the floor is refused with `client-too-old`
before it reaches an endpoint — which is the point: too old has to be a sentence,
not a field that failed to parse
([§6.3](../../Specification.md#63-operations)).

**`0.0.0` is not a release and is never refused.** That is the CLI's default when
no tag named it, and a developer's working copy has nothing to upgrade to. The
CLI's own `Released()` is where that gets said out loud.

**The floor starts at the first release and moves rarely.** It is a line in
`ClientVersion`, and moving it is a deliberate act that says which release was
left behind. Nothing is left behind yet.

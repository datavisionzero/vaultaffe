# A Session Is a Token, and the Only Page Asks for a Password

There are no cookies in this product and no CSRF machinery. A human signing in
receives a **session token** — the `vaultaffe_session_…` of
[ADR 0004](./0004-the-token-format-and-the-envelope.md) — and every caller,
browser or CLI, presents it as `Authorization: Bearer <token>`. The one browser
page this stage has, the confirmation page the device-code login needs
([§6.6](../../Specification.md#66-build-order)), holds no session at all: it asks
for the email address and password each time.

## Why one credential rather than two

[Specification §5](../../Specification.md#5-core-concepts) already fixes three
token kinds, one of which is "the *session token* a human receives from
`vaultaffe login`". A browser session would be a fourth thing to authenticate,
with its own storage, its own expiry and its own place in the change log — and
[§6.5](../../Specification.md#65-logging-and-history) records an identity *type*
that has exactly three values, taken from the token kind. A cookie session would
have to map onto one of them anyway.

The cost is the one every bearer-token SPA pays: the token lives in the browser,
where a cookie marked `HttpOnly` would not be. That is a real difference and it is
inside the threat model of [§4](../../Specification.md#4-guiding-principles) —
this product defends against secrets lying around in repositories, logs and agent
contexts, not against script running inside its own page. Against that attacker a
`HttpOnly` cookie buys little in a single-page application that must read the API
anyway, and it costs a CSRF story on every write.

## Why the confirmation page asks for a password every time

The page a human lands on to confirm `vaultaffe login` could have signed them in
and kept a session. It does not, and that is what makes it safe to be the only
page in this stage:

- **There is nothing to forge.** No cookie, no session, no state a cross-site
  request could reuse. Every confirmation carries the credentials with it, so a
  request the browser was tricked into making carries nothing.
- **A guessed user code is still worth nothing.** The short code is eight
  characters and lives ten minutes, which is not a token's kind of unguessable —
  and does not have to be, because confirming also takes somebody's password. The
  long device code, which *is* the credential, is 256 bits and never leaves the
  client that asked for it.
- **It is thrown away once**, not twice. The management application of the next
  stage is what a browser surface worth building looks like; a half-built session
  here would be replaced by it.

The page is one file, server-rendered, with no stylesheet, no script and nothing
loaded from anywhere — it renders on a machine that is not the one being signed
in, possibly with no network left. It is deliberately absent from the OpenAPI
document ([ADR 0006](./0006-the-contract-is-checked-in-and-the-web-client-is-generated-from-it.md)):
that document is what a client is generated from, and this is a page for a person.

## Consequences

**Signing out is revoking.** `DELETE /api/v1/sessions/current` revokes the token
the request came in under, and the row stays — revoked rather than deleted, so
that everything it ever signed in the change log keeps an author
([§6.5](../../Specification.md#65-logging-and-history)).

**A session appears in the token listing** beside service and agent tokens. What
somebody needs after losing a laptop is to see their sessions and revoke one, and
that falls out of there being one kind of thing to list.

**A session expires, and thirty days is the number.** It is a line in
`Application/Acts/Sessions`. A short life would mean a device-code login every
morning, and the honest outcome of that is people reaching for a service token
instead — the credential that neither expires nor says who acted.

**When the web application arrives it stores a token, not a cookie.** Whether
that is `sessionStorage`, memory plus a refresh, or something else is its
decision; what this one fixes is that there is nothing else for it to present.

# The Browser Holds Its Session for as Long as the Tab

The web application keeps its session token in **`sessionStorage`**, under one
key, and sends it as `Authorization: Bearer …` like every other client. Closing
the tab is signing out. There is no cookie, no `localStorage`, and no "remember
me".

## There was no cookie to be handed

`POST /api/v1/sessions` answers with a token, in that answer and nowhere else
ever ([`api.md`](../api.md)), because the same endpoint serves the CLI, an agent
and a browser — one credential shape for all three, and no privileged side door
for the one that happens to be a browser
([Specification §9](../../Specification.md#9-technical-guardrails)). So the
question was never "cookie or storage"; it was where a browser puts a bearer
token it has been given.

A `HttpOnly` cookie would genuinely be safer against a cross-site script,
because script cannot read one. Buying that means the instance issuing a second
credential kind for one client, a CSRF defence to go with it, and a sign-in path
that behaves differently depending on who is asking — three things on the
authentication surface of a secrets manager, added for a client the product does
not treat as special. It is a good trade for a product whose browser is its
front door; it is not one here, and the day it becomes one it is a change to the
instance rather than to this file.

## Then the tab, not the disk

Between the two stores a script can read, the difference is how long the token
outlives the reader:

- **`localStorage`** survives the browser being closed, is shared by every tab
  of the origin, and stays on the disk of the machine until something removes
  it. On a shared or borrowed machine that is a credential left behind.
- **`sessionStorage`** is one tab's, and it is gone when that tab is. A second
  tab signs in on its own; a reload keeps working, which is the one thing
  holding it in memory alone would not.

The CLI made the same call against a different store: the keychain, and **nowhere
quietly** ([ADR 0012](./0012-a-session-lives-in-the-keychain-and-nowhere-quietly.md)).
A browser has no keychain to reach, so the honest equivalent is the store that
forgets.

## Consequences

**Opening a link in a new tab asks for a password.** That is the visible cost,
and it is paid every day. It is accepted for the same reason the CLI refuses to
write a token to a plaintext file on a headless box: the store a secrets manager
picks is part of what it is promising.

**A `401` from anywhere but the three session endpoints drops the token and
returns to the sign-in screen.** Which of the four reasons it was — unknown,
revoked, expired, malformed — is information about a credential the caller does
not hold, so the client does not guess and the reader gets one sentence.

**Signing out revokes the token at the instance and forgets it here**, even when
the request itself does not come back: an instance that did not answer is not a
reason to keep a credential lying around. The row stays revoked rather than
deleted, so everything the session ever changed keeps an author.

**Storage a browser refuses is not an error.** A private window or a policy that
blocks site data throws on the property itself; the token then lives in memory
for the life of the page, and the only thing lost is surviving a reload.

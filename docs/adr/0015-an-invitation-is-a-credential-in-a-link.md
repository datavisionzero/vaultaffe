# An Invitation Is a Credential in a Link

An invitation is not a message this instance sends. It is a **one-time code an
administrator copies out of one screen and hands over**, stored as a hash, good
for 72 hours, spendable once. It rides in the **fragment** of the link and
reaches the instance in a **request body** — never in a path and never in a query
string.

## Why there is a link at all

[Specification §6.1](../../Specification.md#61-web-ui) settles the shape and
§4 says why: **the instance sends no email**, because an installation that needs
an SMTP server to add the second person is an external dependency to operate. So
an invitation cannot be a message. What is left is something the administrator
carries over — a chat window, a room, a phone.

That makes an invitation a credential rather than a notification, and it is held
to what every other credential here is held to
([ADR 0004](./0004-the-token-format-and-the-envelope.md)): the row holds the hash
and never the code, the answer that created it is the only place the code
appears, it works exactly once, and it stops working on its own. Seventy-two
hours, which is this product's one window — the same one a deletion is
recoverable inside ([§6.5](../../Specification.md#65-logging-and-history)). A
second number would be a second thing to explain.

## Why the fragment, and why a body

The obvious link is `/invite/<code>`, and it is the one nearly every product
ships. It puts a working credential into the request line, and this instance
serves the web application itself — so the code lands in its own access log, in
whatever reverse proxy is in front of it, and in a `Referer` header on the way
out. This repository's rule is that **no secret value ends up in anything this
product writes** ([§6.5](../../Specification.md#65-logging-and-history)), and a
log full of live invitation links is exactly that.

A fragment never leaves the browser. The application reads it, and the two
endpoints an invited person calls — reading what the link is for, and accepting
it — take the code in a body, which is where the sign-in endpoint already takes a
password. A query string would have been half a fix: out of the path, still in
every log line.

What this does not fix is the browser's own history and whatever the
administrator pasted the link into. Nothing here can; the 72 hours and the
single use are what bound it.

## Why the state comes back, and one sentence does not

A token that does not authenticate is refused with **one sentence for four
cases**, because which of them it is, is information about a credential the
caller does not hold ([`api.md`](../api.md)). An invitation answers differently:
a code nothing here issued is `not-found` and learns nothing, but for a code this
instance did write, the state — open, accepted, withdrawn, expired — comes back.

Whoever presents it is holding it. They are the person it was handed to, and
"this invitation was already used" is the sentence that tells them to ask for a
new one instead of retyping a password. Telling a stranger nothing costs nothing
here, because a stranger does not have the code.

## Consequences

**A password reset is not this.** §6.1 makes it an administrator's act: they set
a password and hand it over the way they handed over the link. It ends every
session that person had — a password reset that leaves the sessions opened with
the old password working is not one — and their service and agent tokens are
untouched, because those never depended on it.

**Deactivating somebody takes every token of theirs with it**, their sessions and
the agent tokens they are accountable for included. It is what makes
deactivation mean something the same minute; the alternative is a person who is
out of the organization still acting in it through something they left running.
It is reversible for the reason a deletion is: the second most likely thing after
making a decision about a person is having made it about the wrong one.

**Nobody deactivates themselves.** Not because it would be wrong, but because it
is the one way to end up with an instance nothing can administer, and this
product has no support desk to undo that.

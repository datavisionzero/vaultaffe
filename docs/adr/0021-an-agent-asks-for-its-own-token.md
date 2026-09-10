# An Agent Asks for Its Own Token

`vaultaffe enroll "claude in ~/webshop"` prints a short code and waits. A person
opens `/enroll` in the browser they are already signed in to, sees what asked,
chooses what it may do and how far it reaches, and agrees. The asking machine's
next poll receives the token and writes it straight into a file at mode `0600`.

**The value is never printed, and no person ever sees it.** That is the whole of
the decision; everything below is why it is worth a flow of its own.

## The problem, in the words it was reported in

A person creates an agent token on the tokens screen. The value is shown exactly
once, correctly. Now it has to reach the agent, and the ways to do that are:

- Copy it out of the browser and paste it into a shell that starts the agent.
  That is the documented path ([`agents.md`](../agents.md)), and it works.
- Paste it into the conversation with the agent. **This burns the token.** A
  token in a transcript is a token in whatever stores that transcript, and
  [§6.1](../../Specification.md#61-web-ui) spends every other decision keeping
  values out of exactly those places.

The second is what people reach for, because the first means leaving the
conversation, finding a shell, writing a file, and restarting the thing they
were talking to. Nothing about that is hard; all of it is friction at the exact
moment somebody is trying to get on with something else — and the alternative
sitting right there is one that quietly ruins the credential.

**A path that is safe and tedious, next to a path that is easy and wrong, is a
design problem and not a discipline problem.**

## What this is, and what was already here

It is the device-code flow of [§6.2](../../Specification.md#62-cli), pointed at
a different far end. The whole protocol was already built for `vaultaffe login`:
two codes, one of them a credential and one of them readable out loud; ten
minutes; a person deciding on some other machine; a collection that works
exactly once. The only thing that differs is what the collection hands over — a
session for the person who confirmed, or an agent token of the asking machine's
own.

**So there is one table and one flow, not two.** `device_authorization` gains a
`produces` column, and `Handover` says which of the two it is. A second table
would have been that protocol written out twice, and the second copy is the one
that misses a state, forgets that a code works once, or lets an approval nobody
collected in time still be worth something an hour later. `ChangeLogEntry` made
the same call for the same reason ([ADR
0020](./0020-one-change-log-and-not-two.md)).

## Why the value never crosses a person

Three things go over the wire, and none of them is worth anything to somebody
reading over the agent's shoulder:

- **The short code** is eight consonants that identify a request. Acting on it
  takes a person's session, and the person decides what — if anything — it gets.
- **The long code** never leaves the process that asked. It is not printed, not
  shown to anybody, and hashed before it is stored, exactly as it is for a
  login.
- **The value** is answered to the machine that asked and written to a file. It
  is not printed by the CLI, not carried by `--json`, and never shown in the
  browser where somebody agreed.

The result is that the exchange is safe to have **in front of** an agent. The
code can be read out in a conversation, because a conversation is not a session.
That is the property the reported problem needed and the copy-and-paste path
could not have: it removes the tempting wrong move by making the right one the
short one.

## What a person is deciding, and what they are told

`/enroll` shows the name the client sent and says, in as many words, that the
instance has no way to check it. An installation cannot tell whether a machine
calling itself an agent on somebody's laptop is one; what it can do is refuse to
dress a claim up as a fact. The person confirms or replaces the name — what they
settle on is what the token is called from then on, and `requested_name` and
`approved_name` are kept apart because they are two different facts.

They then choose scopes and reach in **the same form the tokens screen uses**.
It is one component for both, so that what a person may agree to and what they
may issue cannot come apart: a scope offered at creation and withheld at
approval would be a permission model nobody wrote down.

The default is the whole organization with every scope, because the point of an
agent token is attribution and not restriction
([§6.4](../../Specification.md#64-permissions-in-the-mvp)).

## Agreeing is human-only, and it is the existing rule

The approval declares `HumanOnly(HumanAction.CreateToken)` — the same action,
not a new one. What comes out of an approval is a token, and an agent that could
agree to one for itself would be a credential nobody issued. Beginning and
collecting are unauthenticated by definition: the whole flow exists to turn no
credential into one.

Reading an enrollment and refusing it need only a session. Somebody who cannot
decide what a machine may do should still be able to see what is being asked and
say no to it.

## The change log says a person did it

The entry is `token-created`, and it is recorded **under the person who
agreed**, through `ChangeLog.RecordByPerson`. The alternative was to record it
under the token being collected, which is what an accepted invitation does with
the session it creates — but here that would read as a machine handing itself a
key, which is the one sentence the identity type exists to keep out of this log
([§6.5](../../Specification.md#65-logging-and-history)). A person handed a
machine a key; the collection is the machine picking it up.

## What the reach costs, and why it is a table

An approval records a shape and not a token, because a token's value exists
exactly once, in the answer that creates it — and the machine that will receive
it is not on the approval request. So between "a person agreed" and "the machine
collected" there is a reach nobody can hang on anything yet, and
`enrollment_binding` is where it waits. It is not `token_binding`: a row there
pointing at no token is a row its own foreign key could not hold.

Two alternatives were weighed and turned down:

- **Let the client mint the value and send only its hash.** The value would
  never cross the network at all, which is stronger still. It also puts the
  token format in two languages and, worse, ends the instance's ability to
  enforce it: a client that minted a value without the prefix would produce a
  token no secret scanner can find, and
  [ADR 0004](./0004-the-token-format-and-the-envelope.md) calls that format
  immutable. Not worth it for a wire we already trust with the same value at
  creation.
- **Create the token at approval with no usable value, and set its hash at
  collection.** One place for the shape, and a nullable `value_hash` on the
  sharpest table in the schema plus a token standing that means "authenticates
  nothing yet". A new and slightly alarming state on the credential table, to
  save a small table that lives for ten minutes.

## Consequences

**`docs/agents.md` leads with this.** Handing the string over by hand still
works and is still documented — it is what CI does, and what an operator who
already has a token does. It is no longer the first thing the page says.

**The CLI does not put the token on this machine's ladder.** `enroll` writes the
file and records nothing in `config.json`, deliberately: `tokenFile` is a rung
of this CLI's own token resolution, and an agent's token on it would make every
command a person runs on that machine act as the agent — the failure `agents.md`
exists to prevent, in the other direction. The token reaches the agent through
`VAULTAFFE_TOKEN` in the process that starts it, and through nothing else.

**A token that turns out too narrow is changed, not re-enrolled.** The tokens
screen can rename a token, rescope it and rebind it without touching its value,
so the person agreeing here does not have to get the reach exactly right the
first time.

**An enrollment nobody collects is rubbish within the hour**, like a login
nobody confirms, and the same sweep will take both when there is one.

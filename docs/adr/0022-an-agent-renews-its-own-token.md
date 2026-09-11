# An Agent Renews Its Own Token

A token's value can be replaced. `POST /api/v1/tokens/{id}/rotate` revokes the
row the token had and issues a successor beside it, carrying the same name, the
same scopes and the same reach, with the value shown once the way the first one
was.

It is human-only like everything else a token's life is made of — **except that
an agent may rotate the token it is itself holding, and no other.** That
exception is the decision here; the rotation itself is the obvious half.

## Why a value has to be replaceable at all

[ADR 0021](./0021-an-agent-asks-for-its-own-token.md) got a value to a machine
without printing it. Nothing until now got a *second* value to that machine, and
a credential that cannot be replaced is one that is either kept for good or
thrown away whole. The two ways out without this act are both worse than the
problem.

- **Revoke it and create another.** The new token is a new row with a new name
  in every listing, and the change log now carries two names for one worker —
  one before the incident and one after. Whoever reads that history later has to
  know they are the same thing, from nothing the instance told them.
- **Leave the value in circulation.** Which is what actually happens when the
  alternative is that much work, and it is the failure this decision exists to
  prevent.

`PATCH /api/v1/tokens/{id}` was written for the same family of problem and
deliberately cannot help here: it changes everything about a credential *except*
the value, and says so as its whole point. When the value is what went wrong,
nothing short of another value is an answer.

## Why the row is not overwritten

The cheap implementation writes a new hash into the row that is already there.
It also erases the only record that anything happened: `created_at` would go on
claiming this credential dates from the day it was first issued, and nothing at
all would say when the value before it stopped working.

A rotation is usually the answer to something having gone wrong, and the date it
happened is exactly the fact somebody wants afterwards. So the old row is
revoked and the successor added beside it, and the day the value in circulation
changed is something the database holds rather than something nobody wrote down.
It costs one row per rotation, which is the cheapest record of an incident this
product keeps.

## Why the old value dies at once

There is no overlap and no grace period. A run still holding the old value fails
on its next request.

The argument for an overlap is real — a deployment mid-flight, an agent mid-task
— and it is outweighed by what the act is usually for. A rotation is what
somebody reaches for when a value has gone somewhere it should not be, and five
minutes of grace is five minutes granted to whoever has it, indistinguishably
from the five minutes granted to the legitimate holder. The instance cannot tell
those two apart; that is the entire situation.

The failure mode of dying at once is loud and immediately diagnosable: something
gets `401` and says so. The failure mode of an overlap is that a revocation
quietly did not mean what it said.

## Why the expiry is the length it was given, begun again

A token issued to run for thirty days is rotated into one that runs for thirty
days from now. A token issued without an expiry keeps having none.

Neither of the obvious alternatives survives contact with what the act is for.
Carrying the old moment across hands back a credential that expires this
afternoon, which makes rotation useless as renewal. Taking a fresh expiry as an
argument lets a caller — including the agent itself — turn a credential somebody
deliberately time-boxed into one that never runs out.

The length is the thing a person actually decided when they issued the token.
Any number of rotations reproduces that decision and none of them extends it, so
the same act is a renewal for a token that is running out and a replacement for
one that leaked, without being two acts.

## Why an agent may do it to its own token

[§6.4](../../Specification.md#64-permissions-in-the-mvp) puts a token's life on
the human-only list for two reasons, and this act has to answer both.

**"The output is itself a secret."** True, and the reason `create` is
human-only: a value printed into an agent's terminal is a value in that agent's
context and in whatever stores its transcript. This act does not print it. The
CLI writes it to a file at mode `0600` and says where — the same channel ADR
0021 already built, for the same reason. Nothing the agent's own transcript
records is worth anything to a reader.

**"An agent that could widen a token could widen its own."** Not true here, and
this is the part worth being precise about. The successor carries the same
scopes and the same reach; nothing about the request says otherwise, because the
request carries nothing but an id. The holder of a value exchanging it for
another value of identical authority has gained nothing it did not already have
— it *is* the credential, and it could already do everything the successor can
do.

So the rule is: **a person, or the agent holding that very token.** Not any
agent, and not a service token.

- **Another agent's token is refused**, with the same `human-only` refusal every
  other act on somebody else's credential gets. An agent that could rotate a
  token it does not hold could take a working credential away from whoever does
  hold it and read the replacement — which is theft with extra steps and a
  denial of service besides.
- **A service token is refused even for itself.** Not because it is more
  dangerous, but because it has nowhere to put the answer: a service token's
  value lives in a pipeline's secret store or a deployment's environment, and
  this instance can write to neither. It would be replacing a credential that
  works with one nothing is holding. A person rotates it and puts the value
  where it belongs.
- **A session is refused as validation**, not as a permission question. A
  session is what signing in left behind, and signing in again is how another
  one is got.

## Where the rule lives

Every other human-only endpoint declares itself and one middleware enforces the
declaration, so that no handler contains authorization code. This one cannot be
declared: whether it is a person's act depends on which token the request named,
and the endpoint does not know that before it runs.

So it is asked the way the binding is asked — by the act, through `Authority`,
which is still the only place that says no. The refusal is the one every other
human-only endpoint gives, naming `rotate-token` so that a client can name the
command it has ([ADR
0010](./0010-a-refusal-names-the-action-and-the-client-names-the-command.md)).
What moved is where the question is asked from, not where it is answered.

## Consequences

- **`vaultaffe renew` is an agent's own command**, beside `enroll`: it asks who
  it is, rotates that token, and writes the value into the file the token came
  from — or, where the token came from `VAULTAFFE_TOKEN`, into the file `enroll`
  would have used, saying so. A process cannot change the environment of the one
  that started it, and the command says that rather than leaving a machine
  quietly holding a string that stopped working.
- **`vaultaffe tokens rotate <id>` is the person's**, and prints the value once
  the way `tokens create` does.
- **The console offers it on every token that still works and is not a
  session**, asks first, and says in the question that whatever holds the old
  value fails on its next request.
- **The change log gets one entry, `token-rotated`**, under the name that
  continues. One act, one entry: a revocation and a creation would read as two
  decisions, and the fact somebody is looking for is that the value changed on
  this day.
- **A revoked token is not rotated.** Putting a withdrawn credential back is
  creating one, and that is its own act, under a name the revocation list is
  still showing. This differs from what a rotation does to an *expired* token,
  which a person may rotate: running out is a clock, not a decision anybody
  made.
- **The person accountable for the token does not change.** The successor
  belongs to whoever the original belonged to, whoever asked for the rotation.
  Moving it would quietly rewrite who answers for what an agent does
  ([§6.5](../../Specification.md#65-logging-and-history)).

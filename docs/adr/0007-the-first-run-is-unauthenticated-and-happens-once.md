# The First Run Is Unauthenticated, and Happens Once

`POST /api/v1/instance` takes an email address, a name and a password, creates the
default organization and makes that person its administrator. It needs no
credential, because there is none yet, and it refuses the second time it is
called. Whoever reaches a fresh instance first owns it.

[Specification §6.3](../../Specification.md#63-operations) asks for a first-run
wizard and says the first user becomes administrator. This decides what stands in
front of it, and the answer is nothing.

## The alternative, and why not

The obvious alternative — and the one the sibling project planaffe chose for
itself — is a bootstrap secret in the instance configuration: the operator puts a
long random string in the `.env` beside the master key, and the first run has to
present it. It closes the window between `docker compose up` and the first
sign-in, during which anybody who can reach the port can become the
administrator.

It is not chosen here, for three reasons, and the third is the one that decides
it:

- **The window is already there and this does not close it.** Whoever can reach
  an unstarted instance can also read its `.env` in most of the ways a
  self-hosted instance is actually stood up — the same directory, the same
  Compose project, the same host. A bootstrap secret protects against somebody
  who can reach the port and not the disk, which is a narrower attacker than the
  threat model of [§4](../../Specification.md#4-guiding-principles) commits to.
- **It is a second secret in the operator's `.env`, and it is one they only ever
  use once.** [§4](../../Specification.md#4-guiding-principles) is explicit that
  a feature which needs explaining is probably cut wrong, and
  [§11](../../Specification.md#11-success-criteria-for-the-mvp) puts a ten-minute
  budget on `git clone` to a running instance with a secret in it. Two required
  random strings before the first screen is a step somebody skips by copying an
  example.
- **The honest fix is not a secret, it is the sentence.** The exposure lasts
  until the first user exists, and the operations guide says so: start the
  instance and complete the first run in the same sitting, before it is reachable
  from anywhere but the machine it runs on. That is a true statement an operator
  can act on. A bootstrap secret would let us not say it, without making it less
  true.

## Consequences

**The operations guide owns a sentence it must not lose**, and this ADR is what
that sentence points at. When the Compose file arrives, "do the first run now"
belongs in it.

**`GET /api/v1/instance` says whether the instance has been started**, without a
token. That is what lets a client tell "you need to start this" from "you need to
sign in", and it is not a leak: an instance nobody has started has nothing in it.

**Adding users later is a different act with a different door.** An invitation is
a link an administrator hands over
([§6.1](../../Specification.md#61-web-ui)) and it arrives with user management in
the next stage. The first run stays what it is: the one act that has no caller.

**If the trade-off turns out wrong, the retrofit is additive.** An optional
`Vaultaffe__BootstrapToken` that the first run requires *when it is set* would
change no schema and no client — the endpoint would gain one refusal. It is not
built, because an unused switch is the other thing §4 warns about.

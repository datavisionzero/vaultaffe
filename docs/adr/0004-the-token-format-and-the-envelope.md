# The Token Format and the Envelope

A token value is `vaultaffe_<kind>_<43 characters>`: the words `session`,
`service` and `agent`, then 256 bits of randomness in base64url without padding.
The instance stores `sha256(value)` and nothing else. A secret's value is sealed
with AES-256-GCM under a data key of that secret's own, and the data key is
sealed with AES-256-GCM under the instance master key — 32 bytes an operator
puts in the environment as base64.

[Specification §5](../../Specification.md#5-core-concepts) and
[§6.3](../../Specification.md#63-operations) already decided the *shape*: three
kinds with a recognizable prefix, a hash rather than a value, an envelope rather
than a single key. This decides the bytes, because those are what nobody can
change afterwards. A token that is already in a keychain, a CI variable and an
agent's environment cannot be reissued in a new format by anyone but the person
holding it, and a value sealed under one algorithm has to stay openable for as
long as the instance keeps it.

## The prefix is three whole words, not three letters

GitHub's `ghp_`/`gho_`/`ghs_` and Doppler's `dp.st.` are the established form,
and initials are the obvious choice. We spell the kind out instead, for a reason
that is specific to this product: these strings end up in the change log, in an
error message and in whatever an agent pasted where it should not have. **A
person reading `vaultaffe_agent_…` in a log knows what leaked; a person reading
`vfa_…` has to look it up.** The cost is fourteen characters in a string nobody
types, and the benefit lands exactly where §6.5 says the interesting question is
— which kind of identity acted.

`vaultaffe_` in front of all three is what a secret scanner keys on. The whole
format, `vaultaffe_(?:session|service|agent)_[A-Za-z0-9_-]{43}`, is a constant on
`TokenValue` so that whoever teaches a scanner about this product copies it from
the code that issues them, and a test holds the two together.

**No checksum.** GitHub appends a base62 CRC32 so a scanner can reject false
positives cheaply. Ours would buy nothing worth the format: a corrupted token and
an unknown token both end as "this token authenticates nothing", the prefix
already carries the scanner, and every extra rule in a format is a rule that has
to be right on the first day.

**base64url without padding**, so that the value survives a URL, a shell line, an
`.env` and an environment variable without one escaping rule — the same reasoning
[ADR 0003](./0003-a-name-inside-a-reference-is-narrow-and-lower-case.md) applies
to names. Parsing is exact: nothing is trimmed and nothing is case-folded, so a
token that arrives out of a file with a newline on the end is refused rather than
quietly repaired.

**Plain SHA-256, not a password hash.** Argon2 or bcrypt would be the reflex, and
they are the wrong tool: their cost exists to make a dictionary of human-chosen
passwords expensive, and the input here is 256 bits of uniform randomness with no
dictionary to search. Authentication also has to find the row by exactly this
column — `ux_token_value_hash` — and a per-row salt would turn every request into
a table scan.

## The envelope is AES-256-GCM twice, and the wrap names its key

GCM authenticates as well as encrypts, so a row somebody edited fails to open
instead of opening to something else, and it is the one AEAD that every machine
this product runs on accelerates in hardware. ChaCha20-Poly1305 is the
alternative and would be the better choice on hardware without AES-NI; there is
no such hardware in this product's audience, and .NET only offers it where the
platform's OpenSSL does.

Both layouts start with a format byte, so that a later algorithm is a new byte
rather than a guess about what an old row means:

```
secret.ciphertext        [format][ciphertext][tag]        nonce in its own column
secret.wrapped_data_key  [format][master key id][nonce][data key][tag]
```

The nonce is random per write rather than a counter. The birthday bound on a
96-bit nonce is far away from a key used by one secret that a human rewrites, and
a counter would need state that a restore from backup could hand out twice.

**The wrapped data key names the master key it is under**, as four bytes derived
from the key itself with HKDF — not a version number an operator maintains. It
pays for itself twice. An instance started with the wrong key says *"this value
was sealed under a different master key"* instead of reporting every value in the
database as damaged, which is the failure an operator actually hits after a
half-restored backup. And the master-key rotation that §7 defers can tell a
rewrapped row from one still waiting, without a second column and without trying
every key it has.

**No associated data.** Binding a ciphertext to its secret's id would stop rows
being swapped by somebody who can already write to the database, and
[§4](../../Specification.md#4-guiding-principles) is explicit that such a person
is outside the threat model. What it would add is a way for a value to become
unopenable — a restore that rebuilds a row under a new id — in exchange for
hardening this product does not claim.

## Consequences

**The master key is read at startup and the instance refuses to start without
it.** The alternative is one that comes up, accepts values for an hour and cannot
open a single one of them after the next restart. The key is
`Vaultaffe__MasterKey` in the environment, beside `ConnectionStrings__Postgres`,
and the error names `openssl rand -base64 32` and says that a database dump
without the key is worthless.

**A data key is created once per secret and never again.** `Secret.Seal` refuses
a different one, the key ring hands the existing wrapped form back untouched, and
`ck_secret_value_sealed_whole` refuses a row that has a ciphertext without it.
Three places, because this is the one that makes the deferred rotation a rewrap.

**The agent kind exists and issues, though nothing yet tells it from the others.**
That is the point of deciding the format now: §5 says a token kind is not added
later without a migration, and `TokenKind` and its prefix are what would need
one.

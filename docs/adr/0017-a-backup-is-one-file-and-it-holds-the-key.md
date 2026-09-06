# A Backup Is One File, and It Holds the Key

[Specification §6.3](../../Specification.md#63-operations) asks for a documented
backup and restore path for the whole instance — the database dump *plus* the key
material. It is delivered as two scripts,
[`deploy/backup.sh`](../../deploy/backup.sh) and
[`deploy/restore.sh`](../../deploy/restore.sh), and the artifact they write is
one file holding both halves.

Prose would have been the cheaper reading of that sentence, and it is the wrong
one. Envelope encryption ([ADR 0004](./0004-the-token-format-and-the-envelope.md))
means a `pg_dump` of this product is not a backup of it: restored without the
master key it is an instance that starts, signs people in, lists every project
and environment, and cannot open a single value. Two documented steps fail by
somebody performing one of them, and this pair fails in the direction where the
missing half is discovered at the moment it is needed most. A script that cannot
take one without the other removes the opportunity to get it wrong, and it takes
away none of the operator's responsibility: nothing here is scheduled, nothing is
sent anywhere, and when a backup runs, where the file goes and how long it is
kept remain theirs.

## The consequence that has to be said out loud

**The artifact is as sensitive as the installation.** It carries the master key
in the clear, so a copy of it in an unencrypted bucket is a copy of every secret
the team has. [`docs/operations.md`](../operations.md) says that in those words
rather than leaving it to be inferred from the file listing.

Splitting the halves to make backups less sensitive is the alternative that
sounds prudent and is not: it reintroduces exactly the failure this decision
exists to remove, and it moves the key to wherever the operator finds convenient
— which is, in practice, a note beside the dump.

The other half of the same coin is that **a purge does not reach last night's
backup**. `DELETE …/secrets/{KEY}/versions` destroys the recoverable history in
the instance ([§6.5](../../Specification.md#65-logging-and-history)) and does
nothing whatsoever to a file already written. Somebody who purges because a value
must be out of the world has to think about their backups, and the operations
guide says so where it is read rather than in small print.

## Why the dump is `pg_dump` inside the database container

The tool is run with `docker compose exec db`, not from the host and not from the
instance's image. `pg_dump` refuses a server newer than itself, so a client
pinned anywhere else would make upgrading Postgres a question about the version
of something else — the runtime image, or whatever the operator's machine
happens to carry. Inside the database container the tool is by construction the
server's own, and the format stays one every operator already knows how to read
with tools this project did not write.

The restore **drops and creates the database** rather than restoring over it with
`--clean`. Restoring over leaves behind whatever the artifact does not mention,
and a table from a newer schema surviving into an older instance is the kind of
half-restore that is found much later and believed to be something else. It also
sets the role's password from the restored `.env`: a cluster keeps the password
it was created with, so a restore onto a machine that has already run an
installation would otherwise leave the instance holding a credential its database
does not have.

## What is deliberately not offered

**Verifying an artifact without restoring it.** It is the question that matters
most about any backup, and answering it honestly means performing most of a
restore — a second mechanism carrying most of the risk of the first. This product
would rather ask for a test restore, and CI performs one on every commit: take a
backup, destroy the installation, restore it, and read back a value that was
written before any of that happened.

**Certificates.** Caddy's `/data` volume is not in the artifact. It is fetched
again in seconds from the certificate authority, and a backup is for what cannot
be.

**Anything resembling a schedule, a destination or a retention policy.** Those
are the operator's, they differ per installation, and every one of them would be
a knob this product would then have to be right about.

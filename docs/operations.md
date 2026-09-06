# Operating an Instance

What somebody running vaultaffe has to know that the files in
[`deploy/`](../deploy) do not already say in their own comments. Bringing an
instance up is three lines and they are at the top of
[`docker-compose.yml`](../deploy/docker-compose.yml); this is about keeping it
up, and about the one operation nobody performs until they need it.

**This file is backup and restore so far.** Configuration, upgrading and what to
look at when something is wrong arrive with the rest of the operator
documentation; until they do, what an operator has to decide is in the comments
of the two files they edit — `docker-compose.yml` and `.env`.

## An installation is two things, and they are not interchangeable

| Where | What is there | Worth on its own |
| --- | --- | --- |
| The `vaultaffe-db` volume | Every secret, sealed. Users, tokens, the change log. | Nothing. |
| `deploy/.env` | The master key every value is sealed under. | Nothing. |

That is envelope encryption
([Specification §6.3](../Specification.md#63-operations),
[ADR 0004](./adr/0004-the-token-format-and-the-envelope.md)): each value has its
own data key, and the data keys are wrapped under the master key from `.env`.
A database dump restored without that key is an instance that starts, signs
people in, lists every project and environment, and cannot open a single value.

**So a backup of this product is both halves or it is not a backup.** That is why
it is a script rather than a paragraph
([ADR 0017](./adr/0017-a-backup-is-one-file-and-it-holds-the-key.md)): two
documented steps fail by somebody performing one of them.

Caddy's volume is not in that table. Certificates are fetched again in seconds
and are not worth backing up.

## Taking one

```sh
deploy/backup.sh /somewhere            # or no argument for the current directory
```

The database has to be running; nothing else does. It writes one file —
`vaultaffe-backup-<utc>.tar.gz`, mode `0600` — and prints its path on stdout, so
it can be handed straight to whatever moves it off the machine:

```sh
scp "$(deploy/backup.sh /var/backups)" backup-host:/vaultaffe/
```

Inside are the dump, `.env`, and a manifest naming the release and the schema the
artifact was taken at. Nothing is scheduled and nothing is sent anywhere: when
this runs, where the file goes and how long it is kept are yours. `cron` and a
retention rule are the usual answer and this product deliberately does not have
an opinion about yours.

### The artifact is as sensitive as the instance

It carries the master key in the clear. A copy of it is a copy of every secret
the team has, so it belongs where the instance's own disk would belong — an
encrypted volume, a bucket with encryption at rest and no public read, a machine
that is not the one being backed up. **Not** in the repository, not in a shared
drive "for now", and not on a laptop that leaves the building.

If that reads as a reason to keep the key somewhere else and the dump somewhere
convenient: that is exactly the split this product refuses to make, and
[ADR 0017](./adr/0017-a-backup-is-one-file-and-it-holds-the-key.md) says why.

## Putting one back

```sh
deploy/restore.sh /var/backups/vaultaffe-backup-20260905T184043Z.tar.gz
```

It prints what the artifact is, says what it is about to replace, and asks — type
`restore`, or pass `--yes` where nothing can be typed. Then it stops the
instance, puts back both halves, drops and recreates the database, replays the
dump, and brings the stack up. On a machine that has never run this product, that
command and nothing before it is the whole restore: there is no `up` to do first.

Afterwards, sign in and look at a value you know. That is the check — that the
key and the data arrived together — and it is the only one worth performing.

Three things worth knowing before you need them:

- **Whatever the installation held is gone.** A restore is not a merge. Anything
  written after the backup was taken is not there afterwards, and there is no
  undo.
- **A `.env` that was already there is moved aside**, to `.env.replaced-<utc>`,
  and it still holds the old master key. Delete it when you are certain, not
  before: it is the only thing that can open a dump taken under it.
- **The site address comes back with the artifact.** Restoring onto a different
  host means `VAULTAFFE_SITE_ADDRESS` in the restored `.env` is the old one —
  the one thing to look at before pointing DNS at the new machine.

## When the key does not match the data

Putting a database back under a different master key is the mistake this whole
arrangement exists to prevent, so it is worth knowing what it looks like. The
instance starts. The handshake answers. Signing in works, projects and
environment names and the change log are all there — because none of that is
sealed under the master key. Reading a value fails, and the instance's log says
why in one line:

```
This value was sealed under a different master key than the one this instance
was started with. Restoring a backup restores the key with it.
```

The caller sees a plain server error; the sentence above is in
`docker compose logs vaultaffe`, and that is the first place to look when a
restored instance behaves this way. **Nothing is damaged.** Put the matching key
back in `.env`, `docker compose up -d`, and every value opens again — the data
was never touched.

## What a restore is not, and what a purge does not reach

A purge — `DELETE …/secrets/{KEY}/versions`, or purging a deleted project — is
the one destruction this product offers that has no recovery window
([§6.5](../Specification.md#65-logging-and-history)). It destroys the undo inside
the instance, and it does **nothing at all** to a backup that was already
written. Last night's artifact still has the value.

So when a value must be out of the world — a credential in the wrong place, a
person asking for something to be gone — the purge is one of two acts, and the
other is deciding what happens to the backups that predate it. That is a decision
about your retention, and no product command can make it for you. It is written
here because it is the part that is easy not to think of.

## Testing it

A backup nobody has restored is a belief. This one is exercised on every commit:
CI brings the stack up the way an operator does, writes a secret, takes a
backup, destroys the installation with `docker compose down -v`, restores from
the artifact alone and reads the value back
([`.github/workflows/ci.yml`](../.github/workflows/ci.yml)).

That checks the mechanism, not your copy of it. Restoring your own artifact onto
a scratch machine once — before anything depends on it — is the only way to find
out whether what you have been keeping is what you thought.

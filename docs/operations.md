# Operating an Instance

What somebody running vaultaffe has to know that the files in
[`deploy/`](../deploy) do not already say in their own comments. Bringing an
instance up is three lines and they are at the top of
[`docker-compose.yml`](../deploy/docker-compose.yml); this is about the decisions
behind them, about keeping the instance up, and about the one operation nobody
performs until they need it.

An instance is three containers — the product, Postgres, and Caddy in front of
it holding the certificate — one image
([ADR 0016](./adr/0016-one-image-and-caddy-in-front-of-it.md)), one volume that
matters and one file that matters. Everything below is one of those.

## Bringing one up

```sh
cp deploy/.env.example deploy/.env       # and make the two values it asks for
docker compose -f deploy/docker-compose.yml up -d
open http://localhost                    # or the domain in VAULTAFFE_SITE_ADDRESS
```

The first `up` builds the image from the checkout, because `:latest` moves only
on a stable release and there has not been one yet; naming a released tag in
`VAULTAFFE_IMAGE` — a prerelease included — fetches it and never builds. `up -d` returns when the instance *answers* and not when a port
opened — Caddy waits for the health check, which is the handshake, which is
served after the migrations have run.

That last address is the **first run**, and it happens exactly once
([ADR 0007](./adr/0007-the-first-run-is-unauthenticated-and-happens-once.md)): the
first person to reach an unstarted instance names the organization and becomes its
administrator. Nothing authenticates that page, because there is nobody to
authenticate against yet. **So do it now rather than tomorrow** — until somebody
has, whoever reaches the instance first is the one it happens for. From a console
with no browser, `vaultaffe instance start` is the same act
([`cli.md`](./cli.md)).

`VAULTAFFE_SITE_ADDRESS` is the whole of the TLS decision. Unset it is `:80`,
plain HTTP for whatever host asked — the trial on a laptop, and nothing beyond
one: the CLI refuses plain HTTP off loopback unless it is explicitly told
otherwise. Name a domain that resolves to the machine and Caddy obtains and
renews a certificate for it on its own, which is what keeps HTTPS inside the ten
minutes rather than being the step everybody postpones
([Specification §11](../Specification.md#11-success-criteria-for-the-mvp)).

## Behind a proxy that is already there

Everything above assumes this instance owns ports 80 and 443, because that is
what lets Caddy fetch a certificate and what makes HTTPS part of the ten minutes
rather than the step after them. On a machine that already terminates TLS for
something else — another Caddy, an nginx, a Traefik — it does not own them, and
`up -d` fails on the ports rather than on anything interesting.

The answer is the same override the ports were made movable for. Nothing is
edited; three values in `deploy/.env` and a block in the proxy that is already
there:

```sh
# deploy/.env — no VAULTAFFE_SITE_ADDRESS at all
VAULTAFFE_HTTP_PORT=127.0.0.1:8081
VAULTAFFE_HTTPS_PORT=127.0.0.1:8443
```

The left half of a published port is the whole of it, so `127.0.0.1:` binds
these to loopback and nothing off the machine reaches them. With
`VAULTAFFE_SITE_ADDRESS` unset the bundled Caddy serves plain HTTP, which is
correct here: the hop it is on does not leave the host. The proxy in front does
the certificate and passes on:

```caddyfile
vault.example.org {
	reverse_proxy 127.0.0.1:8081
}
```

**Two things follow, and both are worth saying out loud.**

`VAULTAFFE_SITE_ADDRESS` stops being the TLS decision. In this shape it decides
nothing; the certificate, its renewal and the redirect from port 80 belong to
the proxy in front, and so does the answer when one of them goes wrong. The
section above about a missing certificate is then about that proxy's log, not
this stack's.

And there are two Caddys in a row. That is the price of not editing the compose
file, and it is a small one — the extra hop is a loopback connection on the same
host. Running the instance without the bundled proxy would mean publishing its
own port, and it deliberately publishes none.

## What is configured, and what is not

Everything an operator sets is in `deploy/.env`, and the file's own comments say
it at more length than a table can. The two without a default are the two the
stack refuses to start without.

| In `deploy/.env` | What it decides |
| --- | --- |
| `POSTGRES_PASSWORD` | The database password, shared by the two services. No default: `openssl rand -base64 24`. |
| `VAULTAFFE_MASTER_KEY` | The key every value is sealed under. No default, and 32 random bytes in base64 exactly: `openssl rand -base64 32`. |
| `VAULTAFFE_SITE_ADDRESS` | What the instance answers as, and whether there is a certificate. `:80` unset. |
| `VAULTAFFE_HTTP_PORT`, `VAULTAFFE_HTTPS_PORT` | The published ports, `80` and `443` unset. The whole left half, so `127.0.0.1:8080` binds to loopback and nowhere else — which is how this instance runs behind a proxy that is already there (above). |
| `VAULTAFFE_IMAGE` | Which build this installation runs. `:latest` is the newest stable release, `:main` follows the trunk, a version or a `sha-` tag pins it. |

Inside the container the product reads two things and nothing else:
`ConnectionStrings__Postgres` and `Vaultaffe__MasterKey`, both composed for it by
`docker-compose.yml`. The rest of what it answers to is ASP.NET's own —
`Logging__LogLevel__Default=Debug` turns the log up, and there is no vaultaffe
setting hiding behind a prefix.

**What is deliberately not configurable** is the arithmetic of §6.5, because the
numbers are a product decision rather than a placeholder: a secret keeps **5**
superseded values for at most **72 hours**, and something deleted stays
recoverable for **72 hours**. They live in one class in the domain
(`ValueHistory`) and change by being changed there, not by an installation
setting one of them to a year and quietly keeping every credential it ever held.

## The key, and the one thing to do with it

Make it once, with the command above, and put it where the rest of this file
says. Three things follow from what it is:

- **It is not a password and cannot be typed.** 32 bytes in base64, produced by
  the machine. An instance started without a usable one does not start — the
  alternative being an instance that accepts values happily and cannot open a
  single one after the next restart.
- **There is no rotation in the MVP.** Re-wrapping every data key under a new
  master key is a migration, not a setting, and nothing here pretends to offer
  it. Choosing the key is therefore a one-time act, and the thing that protects
  it is where the file lives, not how often it changes.
- **It belongs in the backup, beside the dump**, which is the whole of
  [ADR 0017](./adr/0017-a-backup-is-one-file-and-it-holds-the-key.md) and the
  next section.

## Upgrading

```sh
deploy/backup.sh /somewhere              # first, and not optionally
docker compose -f deploy/docker-compose.yml pull
docker compose -f deploy/docker-compose.yml up -d
```

**Migrations apply themselves at startup**, so an upgrade is a pull and a restart
and there is no second command that creates schema. They run **forward only**:
there is no `Down` path an operator can be told to take, because a schema rolled
back rolls a value's ciphertext back with it, and the honest recovery from a bad
upgrade is the backup taken a minute before it (§6.3). That is why the first line
above is first.

Coming off a release that turned out badly is `VAULTAFFE_IMAGE` pinned to the
previous version — *if* the newer one applied no migration. Where it did, the way
back is the restore, and that is not a workaround for the missing one: it is the
same operation, with the data as it was.

The instance logs which migrations it applied, or says the schema is up to date,
in the first lines after a start. `docker compose logs vaultaffe` is where.

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

## When something is wrong

One log and one endpoint answer most of it.

```sh
docker compose -f deploy/docker-compose.yml logs -f vaultaffe
curl -s http://localhost/api/handshake
```

The handshake is outside the versioned prefix and needs no credential: what this
instance is and which versions of the contract it serves
([`api.md`](./api.md)). An instance that answers it is up, has its key, and has
its schema — which turns most questions into one of these:

- **`up -d` hangs, or Caddy never starts.** It waits for the health check on
  purpose. The first start is the migrations; after that, a container that stays
  unhealthy has said why in its own log, and it is nearly always the key or the
  database below.
- **The instance exits at once.** A missing or malformed `VAULTAFFE_MASTER_KEY`,
  and it says so rather than starting without one. 32 bytes, base64, no line
  break.
- **Values do not open, everything else works.** The key does not match the data;
  that is its own section above, and nothing is damaged.
- **No certificate.** Caddy needs the domain in `VAULTAFFE_SITE_ADDRESS` to
  resolve to this machine and needs ports 80 and 443 reachable from the internet
  — the challenge arrives on them. A moved published port is the usual cause, and
  a rate limit from repeated failures is the usual second one. Its log says which.
- **A restart cost a certificate.** The `caddy-data` volume holds it and the
  account key. Restarts without it are new certificates, and a few of those are a
  rate limit.
- **The CLI refuses before it connects.** A token over plain HTTP is a token in a
  network log: `http://` is accepted only to loopback ([`cli.md`](./cli.md)). The
  answer is the certificate, not the override.
- **The CLI and the instance disagree on the contract.** Exit code `9`, and it
  says which side is behind. The two halves of a release are built together, so
  this means one of them was not upgraded.
- **The first run is gone.** It happens once and cannot be repeated
  ([ADR 0007](./adr/0007-the-first-run-is-unauthenticated-and-happens-once.md)).
  An administrator who has lost their password is reset by another
  administrator; an instance with no reachable administrator at all is a restore
  from a backup taken before that was true.

Nothing here is read from a value: the log says which secret was touched and by
whom, never what it held (§6.5). If a line in an instance's log ever carries a
value, that is a bug worth an issue and not an operational curiosity.

## Testing it

A backup nobody has restored is a belief. This one is exercised on every commit:
CI brings the stack up the way an operator does, writes a secret, takes a
backup, destroys the installation with `docker compose down -v`, restores from
the artifact alone and reads the value back
([`.github/workflows/ci.yml`](../.github/workflows/ci.yml)).

That checks the mechanism, not your copy of it. Restoring your own artifact onto
a scratch machine once — before anything depends on it — is the only way to find
out whether what you have been keeping is what you thought.

**The certificate is the one thing CI cannot ask.** An ACME issuance needs a name
that resolves from the public internet and ports 80 and 443 reachable on it, and
a rate limit from a workflow that runs on every commit would be a red trunk for a
reason that has nothing to do with the code. So it is checked by hand, and this
is the last time it was: on 2026-09-06, on a host that owned ports 80 and 443
with `VAULTAFFE_SITE_ADDRESS` set to a name resolving to it and nothing else
changed, the bundled Caddy obtained a Let's Encrypt certificate **twelve
seconds** after `up -d`, for that name and no other, with port 80 answering
`308` to the `https://` of the same address. The certificate was in the
stack's own `caddy-data` volume, which is what says it was this Caddy that
fetched it. Worth repeating whenever the proxy or its configuration changes.

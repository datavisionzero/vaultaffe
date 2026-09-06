# The CLI

`vaultaffe` is the centerpiece of the product
([Specification §6.2](../Specification.md#62-cli)) and a client of the public
HTTP API like any other ([`api.md`](./api.md)): one static binary that knows an
instance only through that API, so the same file runs on a laptop, on a CI runner
and inside an agent's container.

This file says what the command help cannot: which rules hold across every
command, where a token and a binding come from, and what an exit code means.

**All of it is here.** The two commands that come before all the others —
`login`, `setup`, and the first run — `run` itself, the secrets surface, the
catalogue, and, since the stage after the MVP, the administration that used to be
the browser's alone: the people of the organization, its name, and purge at each
of the levels ([§6.6](../Specification.md#66-build-order) stage 3). Parity was
explicitly **not** an MVP gate, and while it was missing the rule was that no
refusal ever offers a command that does not exist. That rule did not change when
the commands arrived: the table mapping a refused action to a command is still
checked against the real command tree, so an entry outliving its command says
"this CLI has no command for it" rather than naming something that is gone
([ADR 0010](./adr/0010-a-refusal-names-the-action-and-the-client-names-the-command.md)).

## The two rules that hold everywhere

**A value reaches this terminal only where a command was explicitly asked for
one.** `run` puts values into a child process's environment — that is its
purpose; `secrets get` prints exactly one key that was named. Everything else
knows names and status
([§4](../Specification.md#4-guiding-principles),
[§8](../Specification.md#8-agentic-use--the-guiding-scenarios)). A confirmation
never echoes back what was written, and no `--json` anywhere carries a value that
the plain output would not have.

**Nothing is interactive.** Stdin is read where a flag or a pipe says so and
never to ask a question. A prompt in an agent's terminal is a command that hangs,
and this CLI is meant to be run unattended.

## Where the commands are

```
vaultaffe login                  sign in through a browser on any machine
vaultaffe logout                 revoke this machine's session and forget it
vaultaffe setup                  bind this directory to a project and an environment
vaultaffe status                 which instance, as whom, and what this directory means
vaultaffe instance               whether this instance has been started
vaultaffe instance start         the first run: the organization, the first person, a session
vaultaffe run -- <command>       start a process with this environment's secrets in it

vaultaffe secrets                names and status — never values
vaultaffe secrets get            print exactly one value, explicitly asked for
vaultaffe secrets set            write one, from stdin and nowhere else
vaultaffe secrets delete         delete one, recoverably
vaultaffe secrets restore        bring a deleted one back
vaultaffe secrets import         read a .env from stdin
vaultaffe secrets export         write one out — a person only
vaultaffe secrets versions       when a key was written, never what it held
vaultaffe secrets rollback       put an earlier value back without reading it
vaultaffe secrets access         who has read a key, first and last — never what
vaultaffe secrets missing        keys the sibling environments have and this one has not
vaultaffe secrets missing dismiss  say "not here", and --undo takes it back
vaultaffe secrets purge          remove a deleted key for good — a person only
vaultaffe secrets purge-history  remove what a key used to hold — a person only

vaultaffe projects               the catalogue: list, create, delete, restore, purge
vaultaffe environments           the environments of a project: the same five
vaultaffe tokens                 list; create and revoke, which are a person's alone

vaultaffe users                  the people of the organization — an administrator only
vaultaffe users invite           write an invitation and print its link, once
vaultaffe users password         set somebody's password, from stdin
vaultaffe users email            change the address somebody signs in with
vaultaffe users deactivate       take somebody out; reactivate puts them back
vaultaffe invitations            the ones written, and withdraw
vaultaffe organization           what this organization is called; rename it

vaultaffe changes                what was changed, by whom, and by what kind of thing
```

Data goes to **stdout**, everything a person reads goes to **stderr**, and the
exit code says what happened. `--json` prints the answer as the API gave it.

## Which instance, and as whom

Three things are resolved before every command that talks to an instance, each
through a ladder, and `vaultaffe status` prints all three with the rung each one
came from.

**The instance** — `--url`, then `VAULTAFFE_URL`, then the instance this machine
logged in to.

**The token** — `VAULTAFFE_TOKEN`, then the token file if one was chosen, then
the keychain. The environment wins because that is how an agent receives its own
token and how CI holds one.

**The project and the environment** — see the binding, below.

### Plain HTTP is refused off loopback

A token over plain HTTP is a token in somebody's network log
([§6.3](../Specification.md#63-operations)). `https://` is always fine and
`http://` to a loopback host — `localhost`, `127.0.0.1`, `::1` — is always fine,
so a development instance works out of the box. Anything else is refused before
the first request, and the override is explicit: `--insecure-http`, or
`VAULTAFFE_INSECURE_HTTP=1`.

### The session goes into the keychain

`login` is the device-code flow: the CLI prints a short code, a person confirms
it in a browser on any machine, and the CLI collects the session. That is the
only login that works over SSH, in CI, in a container and in an agent's sandbox.

The session token goes into the operating system's own store. Where there is
none, the CLI says so, writes nothing, and names the two ways on — a token in
`VAULTAFFE_TOKEN`, or `login --token-file <path>`, written `0600`
([ADR 0012](./adr/0012-a-session-lives-in-the-keychain-and-nowhere-quietly.md)).
A token file that others can read is refused with the `chmod` that fixes it.

An agent's token never comes through `login`. It arrives in `VAULTAFFE_TOKEN`,
set by whatever harness starts the agent
([§6.4](../Specification.md#64-permissions-in-the-mvp)), and `logout` refuses to
revoke one that came from there.

## The binding

The mapping from a directory to a project and an environment is a **prefix table
in the user's configuration**, not a file in the repository — Doppler's most
underrated idea. Everything at or below a bound directory means that project and
that environment, and the longest bound directory wins, so a monorepo is bound
once at its root and a package inside it can bind itself.

```sh
vaultaffe setup --project billing --environment dev   # bind this directory
vaultaffe setup --list                                # the whole table
vaultaffe setup --forget                              # remove this directory's entry
```

An optional checked-in `.vaultaffe` file holds a project and an environment and
**no value**:

```ini
project = billing
environment = dev
```

It does exactly one thing: `vaultaffe setup` with no flags reads it — searching
from here **upwards**, the way git finds its own directory, so a monorepo needs
one file at its root — and writes the entry for the directory the file is in. It
is never consulted behind your back at run time, because a repository that could
rebind your directories by being cloned could point a `run` at production. A key
the file does not know is a mistake rather than something ignored: a misspelt
`enviroment` that silently did nothing would be the one mistake this file must
not be able to make quietly.

In CI and in containers, `VAULTAFFE_PROJECT` and `VAULTAFFE_ENVIRONMENT` are the
binding and there is no table at all. Each half resolves on its own — `--project`
on the command line does not throw away the environment this directory is bound
to.

## `run`

```sh
vaultaffe run -- npm run dev
```

The command in front of every other command. This CLI does not stay as a parent:
it builds the environment, says everything it has to say, and then **replaces
itself** with the process through `exec()`
([ADR 0009](./adr/0009-run-replaces-itself-and-does-everything-else-first.md)).
Signal forwarding, exit codes and process groups then take care of themselves — a
command killed by a signal reports `128+N` rather than `255`, and a `kill` aimed
at this pid reaches the real process instead of a wrapper standing in front of
it. Windows has no `exec()` and is not an MVP target; WSL runs the Linux binary.

Everything is therefore said **before** the call, because after it there is
nobody left to say anything.

**Values reach the process as environment variables.** No temporary `.env`, no
plaintext on disk. Nothing of the process's own output is masked: if it prints
its own secret, it printed it, and we promise nothing else.

**Every collision is reported on stderr**, and in `--json`. A value from the
store beats one that was already in the environment, and a collision usually
means machine-specific configuration has leaked into the store
([§5](../Specification.md#5-core-concepts)) — which is worth a line each time.

**The protected list is fixed and documented.** Never overwritten by a value
from the store:

```
PATH  HOME  USER  SHELL  TMPDIR      anything starting with LD_ or DYLD_      anything starting with VAULTAFFE_
```

The loader variables are on it so that a token which may write cannot inject
code into every `run` on the machine. `VAULTAFFE_` goes further than the others:
those variables are **removed from the child altogether**, so that the token an
agent runs under never reaches what it starts.

**An empty placeholder stops `run`** and is named. A placeholder means a person
still has to do something, and injecting an empty string would hide exactly
that; `--allow-empty` starts anyway and says which keys were empty. Nothing is
read for a process that is not going to start.

**`--` separates the two command lines.** Flags stop at the command, so
`run -- npm run dev --json` passes `--json` to npm.

**There is no offline mode, and no cached copy of anything.** Every `run` asks
the instance, and an instance that cannot be reached is exit code `10` naming
the address. That is a decision rather than an omission
([ADR 0018](./adr/0018-there-is-no-offline-cache.md)): the reading costs about
half a second for fifty keys across a slow link, and a cache would put a
plaintext file back on the disk this product exists to clear. When a file is
genuinely what is wanted, `secrets export` makes one on purpose.

Two exit codes are the shell's rather than this table's, because `exec()` takes a
path and `run` resolves it: **127** for a command that is not on the `PATH`, and
**126** for one that is there and not executable. Everything after the call is
the process's own, `128+N` included.

## The secrets surface

```sh
vaultaffe secrets                                  # names and status — never values
vaultaffe secrets get DB_URL                       # exactly one value, explicitly
cmd | vaultaffe secrets set STRIPE_KEY             # the value arrives on stdin
cmd | vaultaffe secrets set STRIPE_KEY --replace   # overwriting is explicit
vaultaffe secrets set SMTP_PASSWORD --empty        # a placeholder for a person
vaultaffe secrets delete STRIPE_KEY
vaultaffe secrets import < .env
vaultaffe secrets export --format env              # session tokens only
```

**Names and values are two commands, because they are two endpoints and two
scopes.** The bare listing answers a name and a status — `set` or `empty` — and
needs `names`; reading a value is a second request for one key that was named and
needs `read`. That split is what lets an agent hold a token that sees which keys
exist and which a person still has to fill, and cannot read one.

**Setting a secret without seeing it.** The value arrives **exclusively on
stdin** — never as an argument, where the shell history and `ps` would keep it,
and never as a file. `vaultaffe secrets set KEY=value` is refused with the reason
rather than quietly making a key of that name. The confirmation does not give the
value back, so an agent can pipe a vendor's command straight through:

```sh
gcloud secrets versions access latest --secret=stripe | vaultaffe secrets set STRIPE_KEY
```

**Exactly one trailing newline is removed**, because practically every command
ends its output with one and a key carrying an invisible `\n` is the bug that
costs an afternoon. `--raw` keeps the bytes as they came. A multi-line value —
a PEM key — passes through as it is. `get` is the mirror image: it adds exactly
one newline, and `--raw` prints the stored bytes.

**Overwriting is explicit.** `set` over a key that already holds a value is
refused unless `--replace`; filling a placeholder needs no flag. Overwriting is as
destructive as deleting, and the flag is also what makes a rotation legible in the
change log.

**Import reads stdin, export is for a person.** `import` applies all of a `.env`
or none of it and answers with keys — created, filled, replaced, unchanged,
skipped with a reason, unreadable with a line number — and never values, so an
agent can migrate a file it never displays. `export` writes every value in
plaintext and is therefore `human-only`: a session token, or a refusal that names
`vaultaffe secrets export` as the command a person runs.

**The value history says when and never what.** `secrets versions` lists ids and
moments; `secrets rollback` puts one back and is an ordinary write available to an
agent — an agent that wrecked a value overnight is exactly who needs an undo
button — and nothing is opened to do it.

**`vaultaffe secrets missing`** is the notice: keys **more than half** of this
project's other environments hold and this one has not, with the environments
that have them on the line so the reason is visible rather than taken. It
displays and does not act — no command here creates a key. `secrets missing
dismiss KEY` says "not here" and it stays said, per key and per environment;
`--undo` takes it back, and `--dismissed` lists what was silenced. The comparison
runs only over environments this token reaches, so a token bound to one gets
nothing here rather than a filtered answer.

**`vaultaffe secrets access KEY`** is who has read it: an identity with its kind,
and the first and last time it did. Two moments and **no count** — a value read
does not prove that anything started with it, and a number here would read as
though it did.

**`vaultaffe changes` is the log**, which holds no value at all. An entry is a
moment, an action, the names it happened to, and the acting identity **with its
type**: `human-session`, `service-token` or `agent-token`. Reads are not in it. By
default it asks about this directory's binding; `--everywhere` asks about the
whole organization and needs a token that reaches it.

## The catalogue, and getting an agent its token

```sh
vaultaffe projects create billing                    # dev, staging and prod come with it
vaultaffe projects create billing --no-environments  # the explicit empty list
vaultaffe environments create review --project billing
vaultaffe projects delete billing                    # recoverable, with its subtree
vaultaffe projects restore billing
```

Creating a project needs a token that reaches the whole organization, and none
of it is human-only: creating projects and environments is something an agent
may do. Deleting is recoverable and nothing cascades — the subtree is retained
and comes back as it was, not as it would have been — and the name stays
reserved for the whole window, which is the answer to "I deleted it, why can I
not recreate it".

```sh
vaultaffe tokens                                       # id, kind, name, scopes, standing
vaultaffe tokens create "the deploy agent" --kind agent
vaultaffe tokens create "ci" --kind service --project billing --environment prod --scopes names,read
vaultaffe tokens revoke <id>
```

**This is how an agent gets a token at all**: a person creates one under their
own session and hands it over in `VAULTAFFE_TOKEN`. Creating and revoking are
human-only, because a token is itself a secret and one created under an agent's
own token would be printed straight into that agent's context. Listing is not:
a revocation list an agent cannot read is not one.

**The value is printed exactly once**, by the command that created it, and the
sentence beside it says so. Scopes omitted mean the default of the kind —
everything for an agent token, because the point is attribution and not
restriction, and `names` plus `read` for a service token.

`--project` and `--environment` narrow the binding and have to be passed here
**explicitly**. The directory you happen to be standing in does not silently
narrow a token, and the names are turned into ids by a lookup, so a mistyped
environment is answered against the ones that exist.

## The people of the organization, and its name

```sh
vaultaffe users                                        # address, name, role, standing
vaultaffe users invite somebody@example.test --name "Somebody"
vaultaffe users password somebody@example.test < /dev/stdin
vaultaffe users email somebody@example.test somebody-else@example.test
vaultaffe users deactivate somebody@example.test       # reactivate puts them back
vaultaffe invitations                                  # and `invitations withdraw <id>`
vaultaffe organization rename "Datavision Zero"
```

Everything here needs a **session** and an administrator behind it: who may be in
this organization is a decision about people
([§6.4](../Specification.md#64-permissions-in-the-mvp)), and an agent that could
invite one could invite itself a second identity. An agent token is refused with
`human-only`, and the sentence it gets names `vaultaffe users`.

**A person is named by their address**, not by an id. Everything a person types
in this CLI is a name — a project, an environment, a key — and an address is what
somebody here is known by; the id the API takes is looked up, the way a token's
binding looks up a project. An address nobody here has is a usage error that
names the ones there are, because the listing that would have answered it has
already been read.

**The instance sends no email**, which is the price of having no external
dependency to operate. So an invitation is a link to hand over: it is printed
**once**, alone on stdout because it is a credential, with the code in the
fragment so that a working invitation never reaches an access log. An invitation
that was lost is withdrawn and written again. A password reset is an
administrator setting one, and the password comes off **stdin** for the reason
every value in this CLI does:

```sh
pwgen -s 24 1 | vaultaffe users password somebody@example.test
```

**An address is a name, so both of them are arguments.** `users email` takes the
address somebody signs in with today and the one they will sign in with from now
on — it is not a value and there is nothing to keep out of a shell history. What
moves is the sign-in and nothing else: their password is the one they had, and
every session and token of theirs goes on working, because a token names its
person by id. It is an administrator's for the reason a reset is one — a mistyped
address cannot correct itself, since the correction needs the sign-in it just took
away.

## Purge

```sh
vaultaffe secrets purge-history STRIPE_KEY   # the key stays; what it used to hold goes
vaultaffe secrets purge STRIPE_KEY           # a deleted key, and everything it held
vaultaffe environments purge review
vaultaffe projects purge billing
```

The one deletion this product cannot undo, and a person's alone
([§6.5](../Specification.md#65-logging-and-history)): in an agent's hands it
would be anti-forensics. The first line is the headline case — after a suspected
compromise a key's history genuinely disappears, while the key and the value it
holds now stay.

The other three end a recovery window early rather than deleting anything: the
object has to be deleted already. **Nothing prompts.** A prompt in an agent's
terminal is a command that hangs, and this CLI is never interactive; what makes a
purge deliberate here is that it was typed, and what makes it safe is that only a
session can do it.

Each of them says how many retained values are gone — the number somebody wants
after a compromise — and then the sentence no product we looked at says out loud:
**last night's backup still has what this removed.**

## The version exchange

Every request carries `Vaultaffe-Client`, this build's release, and the instance
refuses one below its `minimumClient` with a sentence rather than a field that
failed to parse ([`api.md`](./api.md)). The other half — an instance that does not
serve the contract this build speaks — is what the CLI says itself, because only
this build knows which contract that is.

Neither is a check on every answer: paying a round trip per command to learn what
a refusal would have said is a cost every `run` would carry. `login`, the first
run and `status` shake hands explicitly; every other command lets the instance
refuse it.

## Exit codes

A script branches on the code, never on the sentence.

| Code | Meaning |
| --- | --- |
| `0` | It worked. |
| `1` | Something went wrong at the instance, or an answer this CLI cannot read. |
| `2` | Usage: bad arguments, no instance, no token, no binding for this directory. |
| `3` | Nothing by that name. |
| `4` | Refused: a field is missing, malformed or over its limit. |
| `5` | Conflict: the name is taken, the key holds a value, the instance is already started. |
| `6` | Deleted longer ago than the recovery window. |
| `7` | Denied: no token, the wrong token, a missing scope, or a token bound elsewhere. |
| `8` | Reserved for a person: one of the short list no token can be given ([§6.4](../Specification.md#64-permissions-in-the-mvp)). |
| `9` | This CLI and this instance do not agree on a contract. |
| `10` | Nothing answered: DNS, a refused connection, a timeout, TLS. |
| `11` | `run` stopped on an empty placeholder: a person still has to do something. |

`8` is its own code on purpose. It is the one refusal an agent acts on
differently: it does not retry with a wider token, it asks a person — and the
sentence it gets names the command **this** CLI has for it, or says plainly that
there is none
([ADR 0010](./adr/0010-a-refusal-names-the-action-and-the-client-names-the-command.md)).

## Getting it

A release carries one binary per platform, and the name has no version in it so
that the URL can be typed:

```sh
os=$(uname -s | tr '[:upper:]' '[:lower:]')     # darwin or linux
arch=$(uname -m | sed 's/x86_64/amd64/;s/aarch64/arm64/')
curl -fL -o vaultaffe \
  "https://github.com/datavisionzero/vaultaffe/releases/latest/download/vaultaffe_${os}_${arch}"
chmod +x vaultaffe && sudo mv vaultaffe /usr/local/bin/
```

`checksums.txt` is beside them on the same release, and a download nobody checks
is a download nobody should run. `/latest/` is the newest **stable** release and
a prerelease is never behind it; a particular release is that URL with its own
tag in place of `latest`.

The macOS binaries are not signed and do not need to be: the quarantine flag
comes from the program that downloaded a file, and `curl` sets none. One taken
through a browser is a different matter, and Gatekeeper will say so.

There is no Windows binary and there will not be one: `run` replaces itself with
the process it starts, Windows has no `exec()` for that, and WSL runs the Linux
one ([§7](../Specification.md#7-explicitly-not-in-the-mvp)).

**Upgrade it with the instance.** Both halves of a release are built from one
tag and carry the same version, which is what the exchange above compares — so a
disagreement means one of them was not upgraded, and not that the release is
inconsistent with itself.

## Building it

```sh
cd src/cli
go generate ./...   # the API client, from docs/api/openapi.json (ADR 0011)
go test ./...
go build ./cmd/vaultaffe
```

The generated client is not committed. `go generate` before vet, test and build
is what keeps a working tree from being a state where the client agrees with a
contract that has moved.

A binary built this way calls itself `0.0.0-dev` and means it: the version is
stamped in from the tag by the release workflow and there is nowhere else it
comes from (`internal/version`).

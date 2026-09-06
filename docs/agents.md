# Handing an Agent its Token

An agent acts under its own token, or the change log says the person whose
terminal it sits in did everything
([Specification §6.4](../Specification.md#64-permissions-in-the-mvp)). The token
is a string, and the whole of getting it to the agent is putting that string into
the agent's process environment as `VAULTAFFE_TOKEN`.

That sentence is the mechanism and it is one line. This file exists because
"set an environment variable" means something different in every harness — and
because in one of them the obvious thing fails silently in exactly the way that
matters: everything works, and every change is attributed to a human who did not
make it.

## The token first

```sh
vaultaffe tokens create "claude on the billing repo" --kind agent
```

A person's own act, under their own session — creating a token is human-only,
because a token created under an agent's token would be printed straight into
that agent's context ([`cli.md`](./cli.md)). **The value is printed exactly
once.**

An agent token's default binding is the whole organization with every scope: the
point is attribution, not restriction (§6.4). Narrowing it is flags at creation —
`--project billing --environment dev`, `--scopes names,read,write` — and leaving
production out is the first one worth reaching for.

**The one thing you do not do is give it to the agent.** It goes to the *harness*
that starts the agent, never into a prompt or a chat: a token pasted into a
conversation is a token in a transcript, and everything else in this product is
arranged so that does not happen.

Where the string then lives on that machine is the operator's decision, and it is
plaintext wherever it is — that caveat is inside the threat model and is not
worked around here (§6.4). A file at mode `0600` outside the repository, or the
machine's own keychain read at launch. **Not** a file the repository carries: a
token in a committed settings file is the `.env` this product exists to remove,
with a shorter name.

## What every harness has to end up doing

Three facts. Everything below is one of them, per tool.

- **The CLI reads `VAULTAFFE_TOKEN` from its own process environment**, the first
  rung of the ladder, ahead of a token file and ahead of the keychain
  ([`cli.md`](./cli.md)). The environment wins precisely because that is how an
  agent receives a token.
- **What the harness starts commands with is what the CLI sees.** The environment
  of the editor or agent process is what counts, not the terminal you happen to
  have open beside it.
- **`run` strips it back out.** Every `VAULTAFFE_` variable is removed from the
  child `vaultaffe run` starts (§6.2), so the token the agent runs under never
  reaches the application it starts.

And one check that settles all of it, in any harness, run from the agent's own
terminal:

```sh
vaultaffe status
```

It prints which instance, as whom, and what this directory means — each with the
rung it came from. `VAULTAFFE_TOKEN` there is the agent's own token. **"the
keychain" is the human's session**, and that is the failure this file exists to
prevent. The second half of the check is `vaultaffe changes`: every entry carries
the identity type, and what it should say is `agent-token`.

## Claude Code

Two ways, and the difference between them is only where the string sits.

**The shell that starts it.** Claude Code passes its own environment on to the
commands it runs, so exporting the variable before launching keeps the token out
of every file:

```sh
VAULTAFFE_TOKEN="$(cat ~/.config/vaultaffe/agent.token)" claude
```

**A settings file**, for a machine where that is one step too many every morning.
The `env` block sets variables for every session and the subprocesses it starts:

```json
{
  "env": {
    "VAULTAFFE_TOKEN": "..."
  }
}
```

*Which* file is the whole of the decision. `.claude/settings.json` is the shared
project file and is meant to be committed — **never that one**.
`.claude/settings.local.json` is the project-local file that git ignores, and
`~/.claude/settings.json` is the user's own, outside every repository. Values are
literal strings, with no `$VAR` expansion, so both of them hold the token in the
clear; that is the trade this route is, and it is a fair one only outside version
control.

**The trap:** an `env` entry overrides a variable of the same name exported in
the shell. A token forgotten in `~/.claude/settings.json` quietly wins over the
one you just exported — including a revoked one, whose refusals will look like an
instance problem. `vaultaffe status` names the rung, not the file, so when the
rung is right and the token is wrong, that block is the first place to look.

Claude Code's own reference for both:
[settings](https://code.claude.com/docs/en/settings),
[environment variables](https://code.claude.com/docs/en/env-vars).

## Cursor

The agent runs its commands in your shell environment — what Cursor itself was
started with, plus what a login shell sets. So the mechanism is the shell
profile, and the useful part is that Cursor says when it is the one asking:
`CURSOR_AGENT` is set in the shell it drives.

```sh
# ~/.zshrc — the agent gets the agent's token; your terminal keeps its session.
if [ -n "$CURSOR_AGENT" ]; then
  export VAULTAFFE_TOKEN="$(cat ~/.config/vaultaffe/agent.token)"
fi
```

That split is the point rather than a nicety: your own shell goes on resolving to
the keychain, so what you do is attributed to you and what the agent does is
attributed to it — from one machine, one checkout and one profile.

On macOS, an editor started from the Dock has the launch environment and not
whatever your terminal exports, which is why the lines belong in the profile a
login shell reads rather than in a shell you source by hand.

**Cloud and background agents do not run on your machine** and never see that
file. Their secrets are configured in Cursor's dashboard, are scoped to the
workspace, and are injected when an agent starts — so an agent that was already
running does not pick a new one up, and starting a fresh one is part of the
change.

## Codex

Codex forwards a *filtered* environment to the commands it spawns, and the
default filter removes names containing `KEY`, `SECRET` or `TOKEN`.
`VAULTAFFE_TOKEN` is exactly that shape.

**This is the failure worth knowing about**, because nothing errors. The variable
is simply not there, the CLI takes the next rung down the ladder, the keychain
answers with the human's session, every command works — and the change log says a
person made every change the agent made. Attribution is the one thing this whole
arrangement is for, and this is how it is lost without a single red line.

The knob is `shell_environment_policy` in `~/.codex/config.toml`. Its exact
spelling belongs to Codex and has moved — a `filters` table now, `exclude` and
`include_only` arrays before it — so read
[their config reference](https://learn.chatgpt.com/docs/config-file/config-advanced)
for the shape your version takes. Two of them:

```toml
# Undo the default exclusion for the one variable, in the filter table.
[shell_environment_policy.filters]
"VAULTAFFE_TOKEN" = "include"
```

```toml
# Or set it outright — applied after the exclusions, and therefore not filtered.
# This holds the token in config.toml in the clear: the same trade as a settings
# file, and it belongs to the machine's user rather than to a repository.
[shell_environment_policy]
set = { VAULTAFFE_TOKEN = "..." }
```

Then check. `vaultaffe status` from inside a Codex session is the only answer
that is worth anything here, and it takes a second.

## Any other harness

The rule is short enough to apply to a tool this file has never heard of: find
where that harness sets environment variables for the commands it runs, put
`VAULTAFFE_TOKEN` there, and then have the agent run `vaultaffe status` and read
which rung the token came from. If a harness filters what it forwards, it will do
it to this variable — the name contains `TOKEN`, which is what such filters look
for, and rightly.

## When it is over

```sh
vaultaffe tokens revoke <id>
```

A token that reached a transcript, a screenshot or a pasted log is revoked and
replaced, not tidied up. Revocation is human-only for the same reason creation
is, listing is not — a revocation list an agent cannot read is not one — and
nothing else about the agent has to change: the harness gets the new string in
the same place the old one sat.

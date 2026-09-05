# A Refusal Names the Action, and the Client Names the Command

[Specification §8, scenario 5](../../Specification.md#8-agentic-use--the-guiding-scenarios)
is one of the five the product is measured by: an agent hits a human-only action,
gets "a clear refusal naming the command", hands that command to a person, and
the person runs it. The refusal is therefore not an error message — it is the
hand-off, and it is the only part of that scenario the server controls.

**The server names the action. It does not name the command.** A `human-only`
refusal carries `humanAction` — `purge`, `create-token`, `revoke-token`,
`administer-organization` — and a sentence saying what was refused and that a
person does it. The CLI turns that into `vaultaffe tokens create …`; the web UI
turns it into the screen it has; a later MCP server turns it into whatever it
tells its model.

## Why not put the command in the sentence

It is the obvious thing to do, and it is wrong for three reasons, in order of how
badly each one bites.

**A command that does not exist is worse than no command at all.** An agent that
is handed `vaultaffe tokens create --agent` passes it to its human, the human
runs it, and the shell says `unknown command`. The agent has now cost a person
more time than the refusal saved, and it has done it while sounding certain. The
CLI's surface arrives ticket by ticket; a server that hardcodes commands is
writing down what some other release of some other artifact happens to spell
today.

**The client the server is talking to is not always the CLI.** The same refusal
reaches a browser, where the answer is a screen and not a command, and a server
that names a command there is describing a tool that particular caller may not
have installed.

**The version skew runs the wrong way.** The instance and the CLI are versioned
together but deployed apart — an operator upgrades the instance and the CLI on a
CI runner keeps its release for weeks (`docs/api.md`, the version exchange). Of
the two, the client is the one that knows which commands it has.

## What this costs

An agent talking to the API directly — no CLI, no UI — gets an action name and a
sentence rather than something it can run. That is the right end of the trade:
it can say "creating a token is a person's to do" to its human, truthfully,
instead of inventing a command. And the API is not the surface that scenario is
about; the CLI is.

## Where it is held

`HumanActions.RefusalOf` in the domain writes those sentences, and a unit test
reads every one of them and fails if a command appears in it. The rule is easier
to keep than to remember: no refusal text in this product spells a command.

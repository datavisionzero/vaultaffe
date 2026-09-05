# `run` Replaces Itself, and Does Everything Else First

`vaultaffe run -- <cmd>` builds the environment, says everything it has to say,
and then calls `syscall.Exec`. It does not stay as a parent, does not forward
signals, does not make a process group of its own, and has nothing to say
afterwards, because afterwards it does not exist.

[Specification §6.2](../../Specification.md#62-cli) already chooses `exec()` over
a wrapper process, and [§10.3](../../Specification.md#103-what-is-still-open)
recorded that the signal and process-group behaviour "needs to be verified in
practice, not just inferred from someone else's source". This is that
verification, and the list of things `exec()` turns out **not** to solve — which
is the part the `run` ticket is built from.

## How it was measured

A throwaway probe that calls `syscall.Exec` and reports its own `pid`, `ppid` and
`pgid` on both sides of it, run under a real pseudo-terminal with a real
interactive `bash` in it. The control characters were written **into the pty**, so
the line discipline delivered the signals exactly as it does when somebody presses
the key; nothing here was simulated by sending a signal to a process. Ctrl-Z in
particular cannot be tested any other way — a process group orphaned by the test
harness has its stop signals discarded by the kernel, which looks exactly like
"Ctrl-Z did nothing".

Measured on **macOS 26.6 (arm64)** and **Debian 13 (arm64)** with **Go 1.27.1**.
Every result below was identical on both, and every number is one that was
printed rather than expected. The probe stays in the working area and is not part
of this repository; what survived of it is this file.

## What `exec()` does

**It is the same process.** `pid`, `ppid` and `pgid` are unchanged across the
call, and the shell's job is still the shell's job. There is nothing between the
terminal and the command afterwards, which is why the rest of this list is
uneventful.

**Exit codes are the shell's own.** A child killed by a signal reports `128+N`,
because the process the shell waited for *is* the child:

| | `exec()` | a wrapper that passes on the exit code |
| --- | --- | --- |
| `exit 42` | 42 | 42 |
| SIGINT | 130 | 130 |
| SIGTERM | 143 | **255** |
| SIGQUIT | 131 | 255 |
| SIGHUP | 129 | 255 |
| SIGKILL | 137 | 255 |

The 255 is not a straw man: it is what `exec.ExitError.ExitCode()` answers for a
process that was signalled rather than exited, and passing that to `os.Exit`
produces exactly the `255` Specification §6.2 records Doppler reporting.

**Ctrl-C and Ctrl-Z work, including `fg`.** Ctrl-C ends the command with 130.
Ctrl-Z gives `[1]+ Stopped`, the job appears in `jobs`, `fg` resumes it and it
finishes with its own status. Job control has nothing to notice, because there is
one process and it is the one bash started.

**The process group is the terminal's foreground group.** `pgid` equals `tpgid`
after the call, which is the property everything above rests on.

**A signal aimed at one pid reaches the real process.** This is the difference a
terminal never shows and a CI runner always does: `kill -TERM <pid>` on an
`exec()`ed `run` ends the command with 143; on a wrapper it kills the wrapper,
answers 143 to whoever asked — and leaves the actual command running. The probe
prints `SOMETHING SURVIVED`, which is what `docker stop` and a cancelled CI job
would produce.

**A child's own children are covered, and only because the group is.** A command
that spawns its own processes leaves them in the same process group, so Ctrl-C
reaches all of them and nothing is left behind. Nothing about `exec()` guarantees
that — a command that deliberately makes its own process group escapes it, exactly
as it would if the shell had started it directly. That is the right answer:
`run` is not a supervisor and should not behave like one.

**Nothing of ours is still open on the other side.** A file and a listening socket
opened before the call are gone in the child; `/proc/self/fd` shows only 0, 1 and
2. Go opens descriptors with `O_CLOEXEC`, and for a product whose whole claim is
about what does *not* reach a child process, that is worth having measured rather
than assumed.

### The honest nuance

A naive wrapper — start the child, wait, pass on the status — also survives Ctrl-C
and Ctrl-Z, because the child shares its process group and the terminal signals
the whole group. Three of the four pitfalls §6.2 lists are therefore not
consequences of *being* a parent; they are consequences of the signal forwarding a
parent needs once it wants `--mount` and `--watch`, which is where the handlers, the
separate process group and the "off by default at a TTY" come from. `exec()` does
not fix that machinery. It removes the reason to have any.

What is intrinsic to being a parent is the other two: the exit code of a signalled
child, and the orphaning above.

## What `exec()` does not solve

Four things, and they are `run`'s to do:

1. **Everything is said before the call.** Collisions with the existing
   environment, an empty placeholder that stops `run`, a warning about anything —
   all of it goes to stderr *before* `exec()`, because after it there is nobody to
   say it. Nothing can be reported, retried or cleaned up afterwards.
2. **The `PATH` lookup, and the two exit codes that go with it.** `exec()` takes a
   path, not a command, so `run` resolves it — and has to tell the two failures
   apart itself: **127** for a command that is not there, **126** for one that is
   there and not executable. Go's `exec.LookPath` returns `exec.ErrNotFound` and a
   permission error respectively; a probe that treated both the same answered 127
   for both, which is the shape of the bug.
3. **Building the environment, including what is taken out of it.** The fixed list
   of §6.2 — `PATH`, `HOME`, `USER`, `SHELL`, `TMPDIR`, anything starting with
   `LD_` or `DYLD_`, and anything starting with `VAULTAFFE_` — is applied to the
   slice handed to `exec()`. The last one is the one that matters: the token an
   agent runs under must not reach the child, and after the call there is no
   second chance.
4. **Windows has no `exec()`.** Not an MVP target (§6.2), and the wrapper build
   that would be needed there is a post-MVP question (§12) — not a fallback hiding
   inside `run`.

## Consequences

**`run` has no signal handling, no process-group management and no supervision
code**, and a pull request adding any of it is adding back the machinery this
decision exists to avoid. If one of `--mount` or `--watch` is ever wanted, that is
where this ADR gets revisited, because that is the feature that forces a parent.

**The `run` ticket is built around one ordering**: resolve the binding, fetch the
values, build the environment, report, resolve the path, `exec`. Everything that
can fail has to fail before the last step.

**The exit codes are testable without a terminal.** `128+N`, 126 and 127 are
observable from a plain shell, so the CLI's own tests can hold them. Ctrl-C and
Ctrl-Z need a pty and an interactive shell, which is what this spike was for;
they are not re-run in CI.

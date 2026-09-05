# The Repository Is a Trunk

`main` is the only long-lived branch, it is always in a state that could be
released, and maintainers commit straight to it. A short-lived branch is
available for work that is large, risky, or wants review, and merges back within
days; nothing else branches.

The obvious alternative is a branch and a pull request per change, which is what
an open-source repository usually shows a visitor. It was not taken because of
who writes here: a single maintainer and a set of agents working ticket by
ticket, where every pull request is ceremony one of them has to open, wait for,
and merge — and an agent waiting on its own review is waiting on nobody. Release
branches were never in question; this product ships as a container image from a
tag, and a tag can be cut from a trunk that is always green.

## Consequences

**The trunk has to stay green, and that is the whole of the discipline.** CI
runs on every push to `main` and on every pull request, and a red trunk is fixed
or reverted before anything is pushed on top of it. There is no gate to hide
behind, so the test suite is the gate, and it has to be fast enough that nobody
is tempted to push past it.

**The skeleton comes before the code.** The six empty projects, the Go module
and the workflow were raised in one commit precisely so that "green" is a state
this repository has been in since before it did anything. A gate assembled after
the code it was meant to hold back is a gate that was missing while that code
was written.

**Pull requests keep working for the people this is written for.**
Contributions from forks arrive as pull requests and run the same CI; what this
decision drops is the requirement that maintainers use the same path for their
own work.

**A ticket that cannot be finished in one sitting takes a branch.** That is the
one case the optional branch exists for — half a rewrite on the trunk is exactly
the state this decision claims `main` is never in.

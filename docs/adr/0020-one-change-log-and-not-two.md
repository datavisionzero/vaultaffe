# One Change Log, and Not Two

What is done to **people and to credentials** — an invitation, a password set, an
address changed, somebody deactivated, a token issued or revoked, the
organization renamed — goes into the same `change_log_entry` table as what is
done to the vault. It carries no project, environment or secret, and names its
subject in one further column, `about_name`.

The alternative was a second journal with its own table, endpoint and screen.
It is not chosen, because the question a person actually asks is "what happened
in this instance, and who did it" — and they should not have to ask it twice, in
two places, either of which somebody can forget to write to.

## Why the price of one table turned out to be nothing

The obvious cost of putting placeless rows in a log that filters by place is the
filtering. It was already paid:

- `ChangeLogStore.Matching` adds a `WHERE` only for a filter that is set, so
  administrative rows drop out of a project filter by themselves and stand in the
  unfiltered log. No query changed.
- `changes/Entries.tsx` already read an entry with no place as the
  organization's own — "a token created, a person invited". The screen was
  written for this before there was anything to show.

What did have to be decided is who may read them, and the existing authority
answers it more sharply than a new rule would have.
[§6.4](../../Specification.md#64-permissions-in-the-mvp) is unchanged for
people: everybody in an organization sees everything in it, these entries
included — a small team in which only administrators can see what administrators
did is a team nobody audits. For **tokens** it comes out stricter for free:
`ReadChangeLog` requires a token that reaches the whole organization before it
will answer an unfiltered read, and an unfiltered read is the only place these
rows appear. A bound service token therefore never sees "X reset Y's password",
and no rule had to be written to make that true.

## What goes in the new column, and what never does

`about_name` holds an **identifier** — an email address, a token's name, the
organization's new name. It never holds a credential: not a password, not a
token value, not the code in an invitation. That is
[§6.5](../../Specification.md#65-logging-and-history) applied to people rather
than a new rule, and `ChangeLogTests` keeps it honest the way it always has — the
table's columns are a written-out list, so adding one means editing the list, and
editing the list means reading the sentence above it.

One column and not two, because `action` already says which kind of thing the
name is: nothing reads an entry without knowing whether it is looking at a person
or a token.

**A change of identifier records the new one.** `RenameProject` already decided
this and said why: it is the name everything after this entry is about, and the
old one is in the entry before it. An address follows the same rule — which is
what `joined` is for. The first run and an accepted invitation each write one, so
that there is always an entry before the first change, and an address that
appears from nowhere is impossible rather than merely unlikely. Both are recorded
under the identity the act itself just created, because neither has a caller.

## What is deliberately not recorded

**Sign-ins.** A sign-in changes nothing, and §6.5 keeps this log to mutations.
What is known about use is the access summary, which is deliberately less than a
log.

**Anything retroactive.** What was not written cannot be invented. This log knows
about administration from the migration onwards, and a journal with made-up rows
is worse than one with a beginning.

## Consequences

**Every instance now opens with an entry.** The first run records the person it
made, so the log is never empty and a test that counted entries counts one more.

**Tokens arrive with this rather than later.** `TokenActs` recorded nothing at
all, while `docs/api.md` and the change-log screen both already claimed a created
token appeared in the log. A key to the vault handed to a machine is the entry a
change log most needs; it was the quietest hole in it.

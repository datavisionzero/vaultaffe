# The Human Interface

This document fixes the screens of the web application, the actions available on
each of them, and the permission boundary they are drawn against. It is written
**before** a screen is built, because that is the document planaffe's interface
holds together by — not the components, the document in front of them.

The product intent is [`Vision.md`](../Vision.md) and the behaviour
[`Specification.md`](../Specification.md); the HTTP operations stay in
[`api.md`](./api.md), and the CLI's own surface in [`cli.md`](./cli.md). The web
application is a client of the same API as the CLI and has no privileged side
door ([§9](../Specification.md#9-technical-guardrails)).

**What is not here.** No dashboard, no charts, no graphs of anything. No
approval workflow, no per-secret permissions, no environment inheritance, no
rotation UI, no key diffing between environments beyond the post-MVP missing-key
notice ([§7](../Specification.md#7-explicitly-not-in-the-mvp)). One organization,
called Default, with the name editable
([§6.1](../Specification.md#61-web-ui)).

## The rule this whole interface is drawn around

**A value is masked until a person reveals it, one key at a time.**

It is not a display convention. Names and values are two endpoints and two
scopes, and the listing screens genuinely **do not have the values in them**
([`api.md`](./api.md)) — so revealing one is a request, not a toggle over data
already on the page. That is what makes the rule true rather than decorative: a
screenshot of an environment cannot leak a value that was never sent, and a
browser extension reading the DOM finds names and status.

Four things follow, and they hold on every screen below.

- **Reveal is per key and never per screen.** There is no "show all", no
  "unmask" on a list, and no export button dressed up as one. Exporting exists
  and is its own deliberate act, described below.
- **Copying is revealing.** A "copy" control fetches the value exactly as an eye
  icon does. It puts it on the clipboard instead of on the screen, which is
  better for the shoulder behind the reader and identical for everything else,
  and it is labelled as what it is.
- **A revealed value hides itself again** when the reader leaves the screen, and
  a reveal is not restored by the back button or a reload. Nothing about a
  reveal goes into `sessionStorage` or the URL.
- **A token value appears exactly once**, on the screen that created it, and in
  no listing ever again ([§6.1](../Specification.md#61-web-ui)). The screen says
  that this is the once.

**Reads are not in the change log** ([§6.5](../Specification.md#65-logging-and-history)),
revealing included, and the interface does not pretend otherwise: there is no
"who looked at this" tab, because the MVP records mutations and a screen implying
more would be a promise the product does not keep.

## Screen matrix

| route | screen | primary content | narrow-screen behaviour |
|---|---|---|---|
| `/start` | First run | organization name, the first person, their password | centred single column; the only screen reachable before an instance is started |
| `/login` | Sign in | email and password, and the sentence that a reset is done by an administrator | centred single column; it also stands at **any** address a signed-out reader asks for, so signing in lands where they were going |
| `/invite` | Join | what the link is for, the address it was written to, a name and a password — or the sentence saying it was used, withdrawn or ran out | centred single column; the code is in the fragment and never in a request line |
| `/device` | Confirm a login | the eight-consonant code, the password, confirm or deny | centred single column; the code field is the first focus |
| `/projects` | Projects | every project this session reaches, each with its environments, and a switch for what is deleted and recoverable | one card per project, environments as chips that wrap |
| `/projects/:project` | Project | the project's environments, each with a key count, how many keys are still waiting for a value, and when a value in it was last written; and the project's own acts | environments stack; the acts move into the row's menu |
| `/projects/:project/:environment` | Environment | **the main screen**: the keys of this environment, their status and when each was last written; import, export and the deleted switch | two-line rows — name and status above, the moment below; no horizontal scroll |
| `/projects/:project/:environment/:KEY` | Secret | the masked value with its reveal, the bounded version history, and this key's own change log | one column; the value block stays above the history |
| `/changes` | Change log | what was changed, by whom and **by what kind of thing**, filtered by project, environment and key | the identity and its type stay; the filters open as a dismissible sheet |
| `/settings/tokens` | Settings · Tokens | the organization's tokens with kind, name, scopes, binding and standing; creating one; the value of a new one, once | area list folds above the area |
| `/settings/users` | Settings · Users | the people of the organization, the invitation link to copy, an administrator's password reset, and the invitations that are still open | area list folds above the area |
| `/settings/organization` | Settings · Organization | the organization's name | area list folds above the area |
| `/settings/profile` | Settings · Profile | own name, own password | area list folds above the area |

`/` leads to `/projects`. A project and an environment are addressed **by name**,
exactly as the API and the CLI's binding address them
([§5](../Specification.md#5-core-concepts),
[ADR 0003](./adr/0003-a-name-inside-a-reference-is-narrow-and-lower-case.md)), so
a link to an environment is a link somebody can read and retype. A key is
addressed by its name too, which is upper-case where the other two are lower —
that difference is the domain's and the URL keeps it.

The shell persists around every screen inside the organization: the project
switcher, the change log, settings, and the account menu. A non-administrator
sees no fewer screens than an administrator, because every person of the
organization sees and changes everything in it
([§6.4](../Specification.md#64-permissions-in-the-mvp)) — the only line is the
short list of administrative acts, and hiding a control is never the
authorization check.

**`/login` and `/invite` are not inside the shell either**, and for the same
reason: there is no session behind them yet. `/invite` is its own address because
the link an administrator hands over has to lead somewhere on its own; the code
in it lives in the **fragment**, so it never reaches this instance's access log
([ADR 0015](./adr/0015-an-invitation-is-a-credential-in-a-link.md)), and
accepting an invitation signs the new person in rather than sending them to a
form for the password they have just chosen.

**`/device` is not inside the shell.** It holds no session, asks for a password
every time, and is reachable while signed in or not
([ADR 0008](./adr/0008-a-session-is-a-token-and-the-only-page-asks-for-a-password.md)).
It is the one screen that already exists before this document's own screens do.

## The environment screen

This is the screen the product is used on, so its details are here rather than
in a component's source.

A row is a key: its name, its status — **set** or **empty** — and when its value
was last written. An empty placeholder is not an error state and is not styled as
one: it means a person still has to do something, it is what stops `run`
([§6.2](../Specification.md#62-cli)), and it is the state an agent deliberately
creates when it prepares work for a human. It is marked by a word and a shape,
not by colour alone.

There is no value column, and there is no width at which one appears.

Creating a key and writing a value are the same form: a name, and a value the
form does not echo back after it is saved. Writing over a key that already holds
a value asks first, in those words — overwriting is as destructive as deleting,
and it is what makes a rotation legible in the change log. Filling an empty
placeholder asks nothing.

**Import and export live on this screen** because they are per environment.
Import takes a `.env` — pasted or dropped — and answers with names: created,
filled, replaced, unchanged, skipped with a reason, unreadable with a line
number, and never a value. Export is the plainest screen in the application and
the one that asks the hardest question: it writes every value of the environment
in plaintext, it is a person's alone, and the dialog says both of those before it
does it.

## Action matrix

Where an answer appears is a rule and not a per-screen decision: **what the
instance answered stands at the act that asked**, never at the foot of the page,
and a row's acts live in that row's own menu.

| area | read actions | write actions | asks first |
|---|---|---|---|
| Project | list, open, see environments, see what is deleted and recoverable | create with its environments, rename, delete, restore | delete, restore past nothing; **purge** |
| Environment | list, open, see what is deleted | add, rename, delete, restore | delete; **purge** |
| Secret | list names and status, open one, **reveal one value**, copy one value, read the version history, read this key's change log | create, write a value, make a placeholder, delete, restore, roll back to a version | overwrite a value that is set; delete; roll back; **purge the history** |
| Environment file | — | import a `.env`, export one | import with replace; **export**, always |
| Change log | read, filter by project, environment and key, page | — | — |
| Token | list with kind, name, scopes, binding and standing | create, revoke | revoke |
| User | list the people of the organization, see who is deactivated | invite by link, withdraw an invitation, reset a password as administrator, deactivate, put back | withdraw; reset a password; deactivate |
| Organization | read the name | rename | — |
| Profile | read own name and email | change own name, change own password | — |

**Every purge asks, and says the two things nobody else says.** A purge destroys
the undo button — it is the second half of a deletion, not a faster one — and it
**does not reach last night's backup**. Both sentences are in the dialog, not in
the small print, because a value that has to be gone everywhere is a job the
backups are part of and no button here can finish
([§6.5](../Specification.md#65-logging-and-history), [`api.md`](./api.md)).

**Export always asks**, even to an administrator who does it weekly. The dialog
says what it is about to produce, and it is the one confirmation in this
application that does not offer "don't ask again".

## Permission matrix

[§6.4](../Specification.md#64-permissions-in-the-mvp) is deliberately trivial for
people and precise for tokens, and this is the whole of it in a table. A **person
of the organization** is signed in with a session; the two token columns are
what an agent or a service reaches the same API with, and they are here because
the same rules draw the screens and refuse the requests.

| capability | person of the organization | agent token | service token |
|---|---:|---:|---:|
| See projects, environments and key names | yes | with `names`, inside its binding | with `names`, inside its binding |
| Reveal one value | yes | with `read` | with `read` |
| Write a value, create a placeholder | yes | with `write` | with `write` |
| Delete a secret, environment or project, and restore one | yes | with `delete` | with `delete` |
| Create a project or an environment | yes | with `write`, reaching the organization | with `write`, reaching the organization |
| Roll back to an earlier version | yes | with `write` | with `write` |
| Read the change log | yes | yes, narrowed by its binding | yes, narrowed by its binding |
| **Purge** a history or a deleted object | yes | **no** | **no** |
| **Create or revoke** a token | yes | **no** | **no** |
| **Export** an environment | yes | **no** | **no** |
| **Administer** the organization and its users | yes, administrator | **no** | **no** |

The four rows in bold are the short list, and **being human-only is not a
permission a token can be given**: an agent carrying every scope there is is
still refused. The refusal names the action and never a command; this
application turns it into the screen it has, exactly as the CLI turns it into the
command it has
([ADR 0010](./adr/0010-a-refusal-names-the-action-and-the-client-names-the-command.md)).
Where an action is one a person may do and this reader may not — administering
the organization without being an administrator — the control is disabled with
the reason beside it rather than hidden, because a control that vanishes is a
question nobody can ask.

There is **no role model beyond the administrator line**, and no per-secret,
per-project or per-environment permission for people. An environment called
`dev-someone` is an ordinary environment offering no privacy: everybody in the
organization sees it, and the interface never implies otherwise.

## Accessibility and performance floor

Every action is reachable by keyboard, focus is visible, and a dialog traps focus
and returns it to what opened it — or, where the act removed that control, to
what the screen offers next. Controls have accessible names. Status is never
carried by colour alone: **set** and **empty** are words, a revoked token says
"revoked", and a deleted object says so in text. Asynchronous changes are
announced, and a reveal announces that a value is now on screen rather than
silently changing it.

**The masking rule has an accessibility half.** A masked value is not a field
full of bullet characters that a screen reader spells out; it is a control that
says what it is — "value, hidden; reveal" — and the revealed value is a region a
reader can be taken to. Nothing about revealing depends on hovering, because a
reveal that only works with a pointer is a reveal a keyboard cannot take back.

The phone layout performs the same actions as the desktop one. Nothing is
desktop-only, because the person confirming a device login on their phone is the
guiding case ([§6.2](../Specification.md#62-cli)).

Loading, empty, error and refusal states are **designed states rather than blank
screens**, and each of the four says something specific:

- **Loading**: the shell renders before the data, and navigation does not remount
  it. A list is a skeleton of rows, not a spinner in the middle of a page.
- **Empty**: an environment with no keys says what a first key is for and offers
  import beside creating one, because the guiding case for an empty environment
  is a team arriving from `.env` files
  ([§11](../Specification.md#11-success-criteria-for-the-mvp)).
- **Error**: the instance's own sentence, at the act that asked, with the code
  where a person could look it up.
- **Refused**: `insufficient-scope`, `out-of-reach` and `human-only` are three
  different sentences and are never flattened into "forbidden", because each has
  its own remedy and only one of them is "ask a person".

## What arrives with which screen

The API this document draws on exists for everything above the settings area.
**The people of the organization arrived with the screens that need them** —
`/api/v1/users` and `/api/v1/invitations` ([`api.md`](./api.md)) — built to the
invitation-is-a-link and reset-by-an-administrator shape, because this instance
sends no email and that is the price of having no external dependency to operate
([§4](../Specification.md#4-guiding-principles)). The organization's name arrived
the same way — `/api/v1/organization`, read by anybody and renamed by an
administrator — together with the two things a person changes about themselves.

The **missing-key notice** — a key present in one environment and absent from a
neighbouring one — is explicitly post-MVP and is a display with a dismissal, not
automation ([§6.1](../Specification.md#61-web-ui),
[§12](../Specification.md#12-afterwards-outlook-not-mvp)). It belongs on the
environment screen when it comes, and it changes nothing in this document but a
row.

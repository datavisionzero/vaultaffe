# The Web Application Is the Sister Project's Frame

The frontend takes its whole stack and its whole shell from
[planaffe](https://github.com/datavisionzero/planaffe): React 19 on Vite with
TypeScript, Tailwind 4, shadcn in the `base-nova` style on Base UI,
`lucide-react`, `react-router`, IBM Plex Sans and Mono, Vitest with Testing
Library — and, above them, the frame itself: a sidebar, a command palette on
`⌘K`, keyboard shortcuts with an overview on `?`, an account menu, a theme
provider, `PageHeader` and `ActionDialog`. It is adopted rather than chosen
again, and the components under `components/ui/` are generated once and then
**owned here** rather than tracked upstream.

## Why not decide it fresh

The stack is already settled by
[Specification §9](../../Specification.md#9-technical-guardrails) — React, no
framework beyond it — so what was left to decide was the fifteen smaller
questions under it, and the sister project has answered every one of them
against a real interface. Answering them a second time would cost weeks and
produce a *different* set of answers, which is the worse outcome: two of the
same person's products that feel like two products.

What that buys is specific and mostly invisible. Base UI gives dialogs and menus
that trap focus and return it, which the accessibility floor of
[`human-interface.md`](../human-interface.md) requires and which nobody writes
correctly by hand. The token layer in `index.css` drops Tailwind's own palette
with `--color-*: initial`, so `bg-red-500` is not a class this project has and a
screen either speaks the vocabulary or does not build. The shortcut table is
read by the handlers *and* by the overview the `?` opens, so a key that is bound
is a key that is advertised.

## What is not adopted

**The domain.** No claims, no releases, no blockers, no issue keys. The route
table is [`human-interface.md`](../human-interface.md)'s screen matrix and
nothing else.

**Anything looser than this product's own rule.** Where vaultaffe is stricter,
vaultaffe wins: a value is masked by default and revealed one key at a time,
never as a state of a list; a token value appears exactly once. The palette is
the clearest case — the sister project's searches the instance for issues and
pages, and this one searches the names of screens, because the listing endpoints
carry no values at all and a palette that turned one up would be a reveal nobody
asked for.

**The generated client is not a choice made here.** It is
[ADR 0006](./0006-the-contract-is-checked-in-and-the-web-client-is-generated-from-it.md),
and it is why `generate` runs before `dev`, `build`, `typecheck` and `test`.

## Consequences

A fix made in one project does not arrive in the other. That is the price of
owning the components instead of depending on them, and it is the same price
shadcn charges everybody; the alternative — a shared package between two
unrelated products — would be a third thing to release.

The frame renders before any of the organization's data does, and navigation
does not remount it. That is what makes a loading state a skeleton inside a
frame rather than a blank page, and it is the reason the frame asks the instance
for nothing at all.

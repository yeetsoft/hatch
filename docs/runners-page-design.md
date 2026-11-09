# Runners Page Design

## Summary

The Runners page is the master-detail replacement for the Runners and API keys
pages — **a runner is a key, with its sessions beneath it** (epic HA-1934). The
list of runners is on the left, the selected runner's sessions and settings are
on the right. It serves three people: **admins** who create and revoke runners,
**owners** who steer the runners they control, and **viewers** who watch.

It is built from **`@hatch/ui` components and `tokens.css` tokens only**. A
colour, radius, size or space value written as a literal for this page is a bug,
for the reason [Design System Architecture](design-system-architecture.md)
gives; the token is already named and already has an answer in both themes.

The sections, in order:

- **Routes and layout** — the nested routes, the desk's two panes, the phone's
  two pages.
- **Who sees what** — the three variants, the one rule that picks between them,
  and the wall-off case.
- **Empty states** — what each "nothing" says, and which words it uses.

## Routes and layout

**The routes are nested in `App.tsx`**: `<Route path="/runners"
element={<RunnersLayout/>}>` with an `index` child and a `:name` child. The
layout owns the list — one `useRunners` poll, `POLL_MS` unchanged at 20 s — and
the one-time secret state, and renders `<Outlet/>` as the detail. The secret
lives in the layout so it survives moving between runners. `/api-keys` and its
nav row are removed, with no redirect. `AttentionPanel`'s link to `/runners`
stays; the board's robot head links to `/runners/:name`.

**`:name` is `encodeURIComponent(name)`.** The detail reads its runner out of
the list's data, with no second fetch; revoked runners stay in the list, so they
resolve. An unknown name draws `<EmptyState message="No runner called Quill.">`
(the name as typed) with `<Button as={Link} to="/runners">` back to the list.

### Desk

Wider than `PHONE_QUERY` (`40rem`, see "The phone" in
[Design System Architecture](design-system-architecture.md#the-phone)), the
layout draws `.hatch-page` with a modifier, `.hatch-page--split`. The modifier
exists because `.hatch-page` is itself the desk scroller, and `.hatch-page > *`
caps every child at `--measure` with `flex: none`. A grid inside it therefore
needs the modifier to lift the cap (`max-width: none`), stop the page scrolling
(`overflow-y: hidden`) and let the grid fill the rest (`flex: 1; min-height: 0`).

- **The grid is `grid-template-columns: 1fr 2fr`** — ratios, no pixel widths —
  with a gap of `--sp-5`.
- **Each pane is `overflow-y: auto; min-height: 0`**, so the list scrolls on its
  own and the detail on its own.
- **The page `<h1>` "Runners" is `PageHeader`** above the grid; the detail's
  `PageHeader` is `level={2}`.
- **The `index` route's right pane is** `<EmptyState message="Pick a runner to
  see its sessions and settings.">`.
- **The selected row has `aria-current="page"`** and a `--primary-bg` ground.

### Phone

`usePhone()` from
[`lib/viewport.ts`](../src/Hatch.Web/apps/hatch/src/lib/viewport.ts) picks two
pages in place of the grid.

- **`/runners` draws** the h1 `PageHeader` "Runners" and the list.
- **`/runners/:name` draws no layout heading.** The detail is headed by
  `<Button as={Link} to="/runners">` reading "Runners" with a back chevron — a
  text glyph `‹` in `<span aria-hidden="true">`, as `OmniBar` does its `❯`,
  since `@hatch/ui` has no icon set to import. The detail's `PageHeader` is
  `level={1}`, so there is never more than one h1.
- **The document scrolls** (`.hatch-page` is `overflow-y: visible` below
  `40rem`), and nothing is sticky.
- **Session controls wrap** (`hatch-reorder--wrap`); `Table` scrolls by itself
  below `40rem`.

## Who sees what

Three variants, picked by one rule, stated once:
`isAdmin ? admin : runner.canControl ? owner : viewer`.

| Variant | Who | Sees |
| --- | --- | --- |
| Admin | `useMe().isAdmin` | Everything: Create runner, owner select, rotate, revoke. |
| Owner | `runner.canControl`, not admin | Edits tags, `under` and bounds; pause, resume, stop, orders, pause all, stop all. |
| Viewer | `runner.canControl` false | Everything read-only. |

- **Admin** sees every control on the page, and is the only variant that creates
  a runner, picks its owner, rotates its secret or revokes it.
- **Owner** reads identity facts as text: there is no owner select, rotate,
  revoke or create.
- **Viewer** sees values drawn as `Text`, never as disabled inputs, which read
  as bugs. Where controls would be, one line at the top of Live sessions says
  `{runner.for ?? 'its owner'}'s to control` — the existing phrase from
  [`RunnersPage.tsx`](../src/Hatch.Web/apps/hatch/src/pages/RunnersPage.tsx), in
  `Text tone="muted"`.
- **Wall off** means `useMe().me.kind` is `local` or `none` (role null), so
  nobody is admin and `canControl` is true: it draws the owner variant. Create,
  rotate, revoke and owner select do not appear, because the admin routes are
  dormant there and `hatch config` bootstraps the runner. This is also the
  signal the empty states below read.
- **A revoked runner has no actions for anyone**, and its config draws as
  read-only values.
- **A `once` session draws no controls**, only `Text tone="muted"` "one
  increment", as `RunnersPage.tsx` does today.

## Empty states

All are `EmptyState` unless noted. Loading, failed and empty are three kinds of
"nothing", and each keeps its own word.

- **List, no runners, admin** — "No runners yet. A runner is a key: create one,
  then hand its secret to a checkout." with Create runner as the `action`.
- **List, no runners, not admin** — "No runners yet. Ask an admin to create
  one."
- **List, no runners, wall off** — "No runners yet. Run `hatch config` in a
  checkout and it makes its own."
- **Loading** (`runners === null`) draws nothing.
- **A failed read** is the existing `<p className="text-danger">` line above the
  stale list, never an `EmptyState`.
- **Detail, no live sessions** — "No live sessions. Start one with `hatch
  go-to-work` in a checkout set up with this key.", drawn muted.
- **Detail, no recent sessions** — that section is omitted, by the
  renders-nothing discipline.

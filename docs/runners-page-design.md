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
- **The list** — the glyph and its ranking, the row's three lines, the sort,
  and where Create runner sits.

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

## The list

The list is the master pane on the desk and the whole page on the phone. It
reads each runner's sessions from the list's own data — `runner.sessions`, the
live sessions and the last 20 of history (HA-1936, HA-1937) — and draws one row
per runner. The field is named here and its shape is not restated.

### The glyph

**`RunnerGlyph` is a new app component** in `apps/hatch/src/components`, not a
`Badge`. `Badge` has no warn tone and none is added.

- **One runner, one glyph.** The states rank gone > out of usage > throttled >
  paused > working > idle, and the runner shows the highest-ranked state among
  its *live* sessions.
- **A session is live** when it is not ended and `now - lastSeenAt <=
  goneAfterSeconds`. `goneAfterSeconds` stays on the runner, as today.
- **A session's own state is today's `runnerActivity`** in
  [`lib/runners.ts`](../src/Hatch.Web/apps/hatch/src/lib/runners.ts), applied
  to that session instead of to the runner.
- **`stopping` shares paused's rank and glyph**, with the word "Stopping".
- **No live session, but at least one ever seen** — live or in the last-20
  history — is `gone`.
- **Never any session is `none`**: the words are "Not started", it ranks below
  idle, and it never reads as `gone`. A runner that has not started is not a
  runner that went missing.

| State | Shape | Token | Light / dark |
| --- | --- | --- | --- |
| gone | ring, diagonal slash | `--danger` | #d32f2f / #ff6b6b |
| out of usage | solid disc, bar cut out | `--danger` | same |
| throttled | half-filled disc | `--warn` | #b26a00 / #ffb74d |
| paused, stopping | two vertical bars | `--primary` | #0066cc / #66b3ff |
| working | solid disc | `--success` | #388e3c / #66bb6a |
| idle | hollow ring | `--muted` | #666666 / #999999 |
| none | dashed ring | `--muted` | same |

- **Shape carries the state, so colour is never alone.**
- **Contrast against `--card`** (#ffffff light, #2d2d2d dark), light / dark:
  danger 4.98 / 4.96, warn 4.24 / 7.96, primary 5.57 / 6.2, success 4.12 /
  5.82, muted 5.74 / 4.83. Every figure clears the 3:1 non-text floor.
- **The glyph is `role="img"`** with `aria-label` the `activityWords` text of
  the session that speaks (see the row's second line), or "Not started".
- **Its box is an ornament dimension**, set in the component's own CSS — the
  one allowed exception to "tokens only".
- **Throttled moves from `danger` to `warn`**, because it heals itself at a
  known time: the cap resets on the clock, and nobody has to act.
- **`ACTIVITY_TONES` is not changed.** It types a `BadgeTone`, which has no
  warn; `runnerStrip` and `goToWork.ts` keep their badges and their danger
  throttled. `RunnerGlyph` owns its own state-to-token map.

### The row

**`RunnerRow` is one `<Link>` to `/runners/:name`**, inside a `<ul>` in `<Card
flush>`. It carries `aria-current="page"` when selected (see
[Desk](#desk)).

- **Line 1** is the glyph, the name (`hatch-runner-name`), and then, right
  aligned, a muted `Badge` `x2` when more than one session is live and a `Badge`
  `revoked` when the runner is revoked.
- **Line 2** is `--t-label`, muted, one line with an ellipsis, and the full text
  in `title`. By state:
  - working draws the claim key as plain text — the row is already a link —
    then " - " and the last line, with "+1 more" when several sessions work;
  - idle: "Idle - seen {agoPhrase}";
  - paused, throttled and out of usage draw the `activityWords` text;
  - gone: "Gone - last seen {agoPhrase}";
  - none: "Not started".
- **Line 3** is `--t-micro`, muted: the owner (`for`, or "no owner"), then a
  " · " and the host — the one distinct host of its live sessions, or "{n}
  machines". With no sessions the host is omitted.
- **Which session speaks on line 2** is, among the sessions at the winning rank,
  the most recently heard. `activityWords` is that session's. Working draws its
  claim key and `line` (the key alone when `line` is null), and "+{n-1} more"
  counts the *other working* sessions.
- **`agoPhrase`** is the one in
  [`lib/claim.ts`](../src/Hatch.Web/apps/hatch/src/lib/claim.ts), and "seen"
  reads the runner's `lastSeenAt`: the newest `lastSeenAt` over all its
  sessions, null with none.
- **Drift badges (`Behind`, `Rebuild`) are not on the list.** They belong to the
  detail.
- **A revoked row** uses the same glyph rule and no special case.

### Sort

**The sort is done at render**, not trusting the server's order.

- **Runners with a live session come first**, then the rest.
- **Inside each group**, `lastSeenAt` descending (null last), then name
  ascending.
- **Revoked runners come last**, in a collapsed `<details>` "Revoked (n)", the
  same row, muted. When `:name` is a revoked runner the `<details>` renders
  `open`, so the `aria-current` row is never hidden. With no revoked runners
  there is no `<details>`.
- **Host grouping and `HostTotal` leave the page.** The host is a fact on line
  3 of the row, not a heading over it.

### Create runner

- **It sits in `PageHeader`'s `actions` slot on the list header**, as `Button
  variant="primary"`. It is admin only, and absent for everyone else (see [Who
  sees what](#who-sees-what)).
- **On the phone `actions` wraps under the title.** `PageHeader.css` today is
  `display: flex` with no wrap and `actions` at `flex: none`, so nothing wraps.
  The requirement: below `PHONE_QUERY` the header wraps and the actions drop
  beneath the title. That is a `@hatch/ui` change, for HA-1942, and HA-1983
  lists it among the components.
- **The empty state repeats it** as `action` (see [Empty
  states](#empty-states)).
- **It is on the list header only.** On the phone `/runners/:name` draws no
  layout heading (see [Phone](#phone)), so the button is absent there.

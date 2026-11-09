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
- **The detail** — the five sections, grouped by who writes each field, what
  each saves, and where its refusal lands.

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

## The detail

The detail is the right pane on the desk and `/runners/:name` on the phone —
five `<Card>` sections, each headed `<h2 className="hatch-section-title">`, in
this order: Identity and key, What it may work, Bounds, Live sessions, Recent
sessions. Each section owns its own save and its own refusal line; nothing
here repeats [Who sees what](#who-sees-what).

### Identity and key

Name, prefix (`<code>`), `createdAt`, and last used —
`lastUsedLabel(lastUsedAt)`, today in
[`lib/apiKeys.ts`](../src/Hatch.Web/apps/hatch/src/lib/apiKeys.ts), folded into
[`lib/runners.ts`](../src/Hatch.Web/apps/hatch/src/lib/runners.ts) — are muted
text here, never inputs; `rotatedAt` and `revokedAt` draw only when set. **The
header line beside the name carries `Behind`** when any live session's
`boardDrift === 'Behind'` and **`Rebuild`** when any live session's
`sourceDrift === 'Behind'` — both move from the runner to the session (HA-1937's
`revision`/`sequence`/`builtAt`/`sourceRevision`/`sourceSequence`).

- **Owner** is a `<select>` of people and "nobody", admin only, saved on
  change and disabled while saving —
  [`ApiKeysPage.tsx`](../src/Hatch.Web/apps/hatch/src/pages/ApiKeysPage.tsx)'s
  `changeOwner`/`setApiKeyOwner` pattern, becoming `PUT
  /api/hatch/runners/{name}/owner` (HA-1936). Everyone else reads it as text:
  `for`, or "no owner".
- **`Rotate key`** (`Button`, default `secondary`) and **`Revoke`**
  (`variant="danger"`) are admin only and absent on a revoked runner.
  Confirmations belong to HA-1982 ("Creating, rotating and revoking"); this
  section only places the buttons.
- **A failed save draws `text-danger` inside this `Card`**, never at the
  page's top: `RunnersPage.tsx`'s `topRefusal` line is dropped entirely, and
  every section below states its own refusal once, where its own control is.

### What it may work

Two editable fields in one `.hatch-field-grid`, saved together: Tags
(`RunnerTagsField`, one per line, empty is any runner) and Under (plain input,
hint "An epic to stay inside, like AER-930. Empty is the whole board.", with a
link to `/issues/{under}` beside it when set — `BoundsSummary`'s under-link,
lifted here). `Button variant="primary"` "Save settings" and "Revert" —
`BoundsForm`/`boundsOf`/`boundsChanged`'s pattern, narrowed to `{ tags, under
}` and its own PATCH.

- **Below, read-only "Declared by its sessions"**: Projects is the union of
  every live session's declared `projects` ("every project" when none; hint
  "Does not change the merge train", today's `RunnerProjectsField` hint —
  projects stop being PATCHable, HA-1938). Dangerous is Yes/No per live
  session, one line per `where` (Yes when that session's `refusesDangerous` is
  false, i.e. `--dangerous` is `on`). Closing sentence: "From `--project` and
  `--dangerous` on each loop. Not editable here."
- **`under` is drawn once, here** — Bounds opens with "Also held under
  {under}", linked, instead of redrawing it (HA-1941).

### Bounds

The five ceilings in `.hatch-field-grid`: Max runs, Max spend, Max tokens this
run, Max tokens per hour, Until (`MomentField`) — `BoundsForm`'s fields, with
`tags`/`projects` dropped (moved to [What it may
work](#what-it-may-work)) and `under` reduced to an opening echo: "Also held
under {under}", linked, when set.

- Intro line: "Ceilings. A session's own flags narrow these and never raise
  them."
- **A sum line above the fields**, over live sessions: runs,
  `moneyPhrase(spend)`, `compactTokens(tokens)`, `compactTokens(tokens this
  hour)` — `groupByHost`'s `totalRuns`/`totalSpent` reduce, widened to tokens.
- **`(default)` beside the label itself**, not just the hint, when
  `maxTokensSource`/`maxTokensPerHourSource` is `'default'`: `<Field
  label={<>Max tokens this run{isDefault && <Text tone="muted"> (default)</Text>}</>}
  hint={tokenFieldHint(...)}>`; `tokenFieldHint` is unchanged.
- Saved on a button only — "Save bounds"/"Revert", `BoundsForm`'s pattern
  narrowed to the five.
- **Refusal routing**: a `400` naming a token flag (`tokenFieldOf`) draws as
  that field's `Field error` (today's `formRefusal`); every other refusal is
  `text-danger` above this section's save row, never at the page's top.

### Live sessions

- **Heading row**: `Pause all`/`Stop all` draw when `canControl` and at least
  one live loop session. `Stop all` opens a `Modal` "Stop {n} sessions after
  their current increment?" with `Leave it`/`Stop all` —
  `ApiKeysPage.tsx`'s revoke-modal shape. `Pause all` opens no modal.
- **Multi-host note**: sessions spanning more than one distinct `host` draw a
  new `.hatch-runner-note` (`--warn-bg` ground, `--warn-ink` text) listing the
  hosts: "This key is in use from {n} machines. Tags and bounds are per
  runner, so a key is meant to be one machine."
- **One `<li>` per live session**, ordered `startedAt` ascending then `where`
  (blocks never reorder as they beat): a head (glyph, `where` in `--mono`,
  state words, `agoPhrase(lastSeenAt)`); a claim line (the claim key as a
  link, its last `line`, or muted "nothing yet" — today's `Row` third cell);
  controls (Pause/Resume, Stop after this one, order controls — lifted
  verbatim from `Row`, acting on the session's id rather than the runner's
  name); runs, spend, and `TokenBudget` (widened from `Runner` to a session's
  `maxTokens`/`maxTokensPerHour`/`tokensUsed`/`tokensLastHour`/sources);
  effective bounds (the session's narrowed
  `under`/`maxRuns`/`maxSpend`/`maxTokens`/`maxTokensPerHour`/`untilAt`,
  `BoundsSummary`'s phrasing minus tags/projects); and a closed `<details>`
  "Machine": `host`, `os`/`arch`, `agent`/`agentVersion`, CLI build
  (`runnerBuildLabel`), `username`, `ip` ("as the server saw it"), `container`,
  `startedAt`, declared `projects` and dangerous Yes/No, `repositories`
  (`repositoriesLabel`), `drives` (`drivesLabel`) — null fields omitted, for an
  older CLI (HA-1940).

### Recent sessions

Omitted entirely when empty, by the renders-nothing discipline. A `<Table
scroll>`: Where, Kind, Started, Ended, Runs, Spent, Tokens; newest first, at
most 20 — the server's own retention (HA-1937); no controls.

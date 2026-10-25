---
title: Watching the loop
section: Running agents
order: 50
summary: Where to see the loop working, what it landed, and what it is waiting on you for.
---

The loop runs unwatched - nobody has to sit and read a terminal while it
works. This page is where to see it working, what it did, and what it is
waiting on you for.

## Runners

**Agents → Runners** lists every runner that has spoken to this Hatch lately:
a row appears within a minute of it starting and drops off two hours after it
stops. This page lists every runner, because a person is looking; a key
reading the same list gets its owner's runners, an admin's key gets every one,
and a key with no owner gets only the runners that belong to nobody. `hatch
runners` is the same read at a terminal, one line per row. Each row
has:

- **Runner** - its name, and `one increment` where it is not a `loop` (a loop
  says nothing here, since that is the default); a **Behind** badge when its
  own build has fallen behind the board's (restart it against a newer
  deploy), and a **Rebuild** badge when its own build has fallen behind its
  own checkout's source (run `make build-hatch` in that checkout). The two
  never mean the same thing, and either, both or neither can be true of one
  row; a runner started outside a checkout holding Hatch's source never shows
  **Rebuild**.
- **Doing** - `Gone`, `Out of usage until …`, `Paused`, `Stopping`, `Working
  <KEY>` or `Idle`. Silence beats everything else: a runner that has stopped
  answering reads `Gone` whatever it was last asked to do. Silence is ninety
  seconds by default, which `Hatch:RunnerGoneAfterSeconds` changes; a value of
  zero or less is not believed, and ninety stands. The key in `Working <KEY>`
  is a link to the ticket.
- **Last said** - the last line it reported.
- **Heard from** - how long ago its last heartbeat was.
- **Bounds** - only the ones actually set; a row with none says so rather
  than showing four empty caps. The `under` epic, where set, is a link to its
  ticket too.

There is no cost column here - spend lives on the **Leaderboard**. Own-tickets-only
still shows on the row, inside Bounds: the repositories a runner serves no
longer do, and show once the row is unfolded instead - neither is a control
on this page.

A loop's row, and only a loop's, carries controls: **Pause** (or **Resume**
on a paused one), **Stop after this one**, and a **Details** panel - where it
runs, the agent it runs, its build - a short sha and how long ago, or
`dev build` for one that hasn't been stamped - what it drives, and the
repositories it serves, then a Bounds form - **Under**, **Max runs**, **Max spend**, **Until** (an hour,
not just a day), **Tags**, **Projects** (one key per line; empty is every project, and the merge train is unaffected), with **Save bounds** and **Revert** - and an Order
control: a box for a ticket
key with **Work this ticket**, and **Run one pass**. The loop takes an order at
its next heartbeat, paused or running, then returns to what its state says.
A press only records what the board wants:
the runner reads it between increments, at its next heartbeat, never in the
middle of one. Nothing on the server starts or stops a process, which is why
the same buttons work for a runner in a container or on a laptop behind a
router: the loop does the reading, and the loop is what exits. Only a person
can press them. A row that belongs to somebody can be pressed by its owner or
an admin only; to everyone else it says whose it is. A key may read the
runners and heartbeat as one, but never set a bound or a control, since an
agent that could raise its own **Max spend**
could raise its own budget. [One increment and the loop](one-increment-and-the-loop#bounds-timeouts-and-stopping)
covers what each bound actually does.

**Tags** seed from the loop's first heartbeat only; after that the row is
the board's, and editing them here - or a `PATCH` on the row - is what
moves them from here on. The loop adopts a changed list on its very next
heartbeat, saying so once - `hatch: the board set --tag ...`, or `--tag
off` for none.

The bounds are not seeded once the way tags are. Each fresh start of a loop -
one started by hand, or the supervisor's first start - writes the bounds it
was started with onto its row on its first heartbeat, overwriting an edit made
here and clearing any bound the row held for a flag the loop was not given.
A supervisor restart does not: it keeps what the row holds, so an edit made
here survives it. A row made by `hatch work` or `go-to-work --once` never
takes bounds from that heartbeat.

An order not yet read is drawn on the row as `ordered to work <KEY> by <name>,
<how long ago>`, with **Withdraw**, until the loop reads it; a loop holds one
at a time, so the Order control is replaced by that line while it stands.

A runner holding a ticket shows on that ticket too. On the board, its card
carries a small robot head - blue while the runner is being heard from, amber
once it has gone quiet on a lease it still holds. The issue page says which
checkout holds the claim, since when, and the last line the runner printed.
Clearing a claim by hand makes the ticket claimable again at once; the runner
notices and stops at its next heartbeat, within a minute by default, so until
then its session may still push or move the ticket.

## Overnight

**Agents → Overnight** is the newest night: the run of landings nearest the
most recent one, split off by any gap over four hours - a landing is any
issue reaching a terminal column, by a person's hand or by a hop, not only
what a loop did. Epics, bugs, stories and tasks are listed in that order, and
within a type the ones with the most beneath them come first; each row shows
the issue, its type, its title and how much is beneath it.

The egg-shaped control beside **Overnight** in the nav bar carries a count of
landings this reader has not seen. It is rechecked about once a minute and
whenever the tab comes back into focus, and it goes quiet the moment this
reader opens the Overnight page - for that reader alone, not board-wide.

## Leaderboard

**Agents → Leaderboard** is every session's work-log row, board-wide. Pick a
range - **Last 24 hours**, **Today**, **Last 7 days**, **Last 14 days**
(the default), **Last 30 days**, or a pasted window - and narrow it to one
issue and everything beneath it. Below the controls sit a spend-over-time
graph, a **Top billing** list of the costliest sessions in the window, and a
table of every session in it, sortable by tokens, requests, peak context,
notional USD or when it ran. Nothing on this page writes - there is no
control that could, and the server refuses a browser outright.
[What a session is told](what-a-session-is-told#the-work-log-block) says what
goes into one of these rows in the first place.

## Reports

**Reports** is a link in the nav bar, beside Board and Plan, and opens on a
card per report. **Timeline** answers when each top-level epic was in flight,
and how long its work sat in each column, drawn in the board's own column
colours; **Cumulative flow** shows how much work sat in each column over time,
and where it is piling up. A ticket counts as in flight while it stands in any
column after the first that is neither done nor parked - the first column is
where work waits, a done column is where it ended, and a parked one is a
siding, so a stretch in any of the three is a gap in the drawing.

The Timeline has three controls. The window - **Earlier**, **Today**, **Later**
and a **Length**, ninety days to begin with - moves what is charted. **Project**
(drawn only where there is more than one) and **Creator** narrow it to one
project or one person's work. And an epic's row can be hidden: a **Hidden**
list appears beside the controls naming each one, with **Show** beside it and **Show
all** beneath, and what is hidden is remembered for this browser only, never
for anybody else looking at the same board.

## History and the work log

These are two different things. **Manage → History** is every event across
every project, newest first, a hundred to a page, each one naming when it
happened, the issue, what happened, who did it and what changed.
[People, roles and keys](people-roles-and-keys) says who a key or a person
can be.

An issue's own **Work log** section is different: one row per session run
against that issue, its title and summary - or *A session that did not say
what it did*, when it left neither - then its tokens, its notional cost, how
long it took, how many turns, and a command to resume it.

## The battery

In the nav bar, with no page of its own: your own Claude usage, as your own
runners last reported it (a reading of a Claude account, so absent for a
runner under another agent) - not a reading of the board's work, and drawn only
once one of your runners has reported a reading. The fill is how much of the
current session window has been spent; the ring around it is how much of
that window is left before it resets, drawn as a dashed track while the reset
time is unknown. Its colour runs through five bands, from allowance to spare
up to spending ahead of the reset, coloured by whichever of your windows is
under the most pressure. Pressing it opens every window your account
reports, each with its own percent used and how old the reading is, and a
button to refresh it. [Flow, WIP and the battery](flow-wip-and-the-battery)
covers what the board does with this reading.

## What is waiting on you

The control at the right of the bar is board-wide, not per person. At rest it
reads **Clear**; the moment anything needs a person or the loop is stuck, it
lights and opens a panel in two groups.

**Human** - things only a person can clear, in the order to work through
them: **Deadlocked**, **Trunk builds that fail**, **Pull requests to
review**, **Approved, not merging**, **Landed, not done**, **Questions to
answer**. **Agent** - things the loop is already on: **Branches that
conflict**, **Branch builds that fail**. A separate **Out of usage** section
sits above both, for any of your own runners that have run out, and clears
itself once the usage comes back.

Stalled and held issues are not in this panel: a card wears a lock or an
hourglass for them instead, and the issue's own stall panel offers
**Resume now** and **Hold for me**.
[Questions and decisions](questions-and-decisions) covers answering a
question; [One increment and the loop](one-increment-and-the-loop#stalls)
covers what a stall is and when the board resumes one on its own.

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
stops. Each row has:

- **Runner** - its name, whether it is a `loop` or `one increment`, where it
  runs, the agent it runs, and what it drives.
- **Doing** - `Gone`, `Out of usage until …`, `Paused`, `Stopping`, `Working
  <KEY>` or `Idle`. Silence beats everything else: a runner that has stopped
  answering reads `Gone` whatever it was last asked to do.
- **Last said** - the last line it reported.
- **Heard from** - how long ago its last heartbeat was.
- **Bounds** - only the ones actually set; a row with none says so rather
  than showing four empty caps.

There is no cost column here - spend lives on the **Leaderboard**. Own-tickets-only
and the repositories a runner serves show on the row too, but neither is a
control on this page.

A loop's row, and only a loop's, carries controls: **Pause** (or **Resume**
on a paused one), **Stop after this one**, and a **Bounds** form - **Under**,
**Max runs**, **Max spend**, **Until** (an hour, not just a day), with
**Save bounds** and **Revert**. A press only records what the board wants:
the runner reads it between increments, at its next heartbeat, never in the
middle of one. [One increment and the loop](one-increment-and-the-loop#bounds-timeouts-and-stopping)
covers what each bound actually does.

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
runners last reported it - not a reading of the board's work, and drawn only
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

## Coming

Pages this one links to that are not written yet:

- `flow-wip-and-the-battery`
- `questions-and-decisions`

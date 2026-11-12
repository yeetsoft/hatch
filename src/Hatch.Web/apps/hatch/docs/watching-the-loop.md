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

**Agents → Runners** is a list of runners on the left and the selected runner's
detail on the right. On a phone they are two pages: the list, and the detail
behind a **Runners** link back to it. A runner is a key with its sessions
beneath it: the key is what gets created and revoked, and each checkout using it
is a session. `hatch runners` is the same read at a terminal, one line per
runner with its sessions indented beneath.

To make a runner, see [Create runner](people-roles-and-keys#runners-and-their-keys).

### The list

One row per runner, headed **Runners**. A page shows every runner, because a
person is looking; a key reading the same list gets its owner's runners, an
admin's key gets every one, and a key with no owner gets only the runners that
belong to nobody.

Each row opens that runner's detail, and has three lines:

- The glyph, the runner's name, and on the right an `x2` badge (or `x3`, and so
  on) when more than one session is live, and a `revoked` badge when it is.
- What it is doing: `Working HA-12 - <its last line>` (with `+1 more` when
  several sessions work), `Idle - seen 4 minutes ago`, `Paused`, `Stopping`,
  `Over 200k tokens/hour until 3:40am`, `Out of usage until …`, `Agent not
  starting until … - <what it said>`, `Agent needs signing in - <what it
  said>`, `Gone - last seen …`, or `Not started`. The full line is its tooltip.
- Its owner, or `no owner`, then the machine it runs on - or `{n} machines` when
  its live sessions are on several.

**The glyph** is the runner's most urgent state among its live sessions, ranked
gone, agent down, out of usage, throttled, paused or stopping, working, idle,
none. Its shape carries the state, so colour is never the only signal:

| State | Shape |
| --- | --- |
| Gone | ring with a diagonal slash, red |
| Agent down | ring with an exclamation mark, red |
| Out of usage | solid disc with a bar cut out, red |
| Throttled | half-filled disc, amber |
| Paused, stopping | two vertical bars, blue |
| Working | solid disc, green |
| Idle | hollow ring, grey |
| Not started | dashed ring, grey |

Throttled is amber, not red, because it heals itself when the hour turns and
nobody has to act. **Not started** is a runner that has never had a session, and
it is never drawn as **Gone**: a runner that has not started has not gone
missing. **Gone** is a runner that has had sessions and has none live now.

A session is live when it has not ended and has been heard from within the
runner's silence limit. Silence beats everything else: a runner that has stopped
answering reads `Gone` whatever it was last asked to do. Silence is ninety
seconds by default, which `Hatch:RunnerGoneAfterSeconds` changes; a value of
zero or less is not believed, and ninety stands. `Agent not starting until …`
and `Agent needs signing in` rank below `Gone` and above `Out of usage`: the
agent itself, not the account, is the problem, and a runner stuck here is left
out of the board's peer set and is never elected driver until the check passes.
`Over 200k tokens/hour until 3:40am` ranks below `Gone`, `Agent down` and `Out
of usage` and above `Paused`. It means the loop is waiting out its hourly token
cap and resumes by itself when the time passes. A runner that holds no hourly
cap reads `Over its hourly token cap until …`.

Runners with a live session come first, then the rest, each group by the most
recent time it was heard from and then by name. Revoked runners sit last, in a
collapsed **Revoked (n)** that opens by itself when you are looking at one of
them. There is no such group when none is revoked.

### The detail

Five cards, in this order.

**Identity and key.** Its name, the key's prefix, when it was created, when it
was last used, and when it was rotated or revoked, where either has happened. The
owner is an admin's to change, from a list of people and *nobody*; everyone else
reads it as text. **Rotate key** and **Revoke** are an admin's too, and neither
appears on a revoked runner. A **Behind** badge beside the name means a live
session's own build has fallen behind the board's (restart it against a newer
deploy), and a **Rebuild** badge means one has fallen behind its own checkout's
source (run `make build-hatch` in that checkout, or start its supervised loop again, which builds first). The two never mean the same
thing, and either, both or neither can be true of one runner; a session started
outside a checkout holding Hatch's source never shows **Rebuild**. A failed save
in this card says so inside it.

**What it may work.** **Tags** (one per line; empty is any runner), **Under**
(an epic to stay inside, like `AER-930`; empty is the whole board, and a link
beside it opens the ticket) and **Refuses models** (one per line, like `fable`
or `claude-opus-5`; empty refuses none), saved together with **Save settings**,
or undone with **Revert**. The loop adopts a changed tag list on its very next
heartbeat, saying so once - `hatch: the board set --tag ...`, or `--tag off`
for none. A refused model binds sooner: on the board's very next dispatch read,
not the next heartbeat. An entry that is neither an alias nor a pinned id of
an agent this runner can run is refused in the card, before anything is sent.
Tags are set only here, or by a `PATCH` on the runner; the CLI never sends
them. The refused models are the exception: the CLI has its own door to the
same route, in the [CLI reference](cli-reference). A runner that refuses
models says so on its row in the list, as `refuses fable`, and a caller who
cannot edit reads `Refuses:` beside `Tags:` and `Under:`.

Beneath them, **Declared by its sessions** is read-only: **Projects** is every
project any live session was started with (every project when none), and whether
each session **refuses dangerous work**, one line per session. Both are the
running process's own report, carried on every heartbeat and overwritten each
time, like its remotes; only a new process started with `--project` or a
different `HATCH_DANGEROUS` changes them. Projects narrows which tickets a
session picks up and nothing else: the merge train is unaffected, and an approved
pull request in any project still merges on its own clock. **Refuse dangerous
work** stops the board handing that session an issue flagged dangerous (see
[`hatch dangerous`](cli-reference#dangerous) for the flag).

**Bounds.** Five ceilings - **Max runs**, **Max spend**, **Max tokens this run**,
**Max tokens per hour** and **Until** (an hour, not just a day) - with **Save
bounds** and **Revert**. A session's own flags narrow these and never raise them.
When the runner is held under an epic, the card opens with `Also held under
HA-12`, linked. A line above the fields sums what the live sessions have used:
runs, spend, tokens this run and tokens this hour. [One increment and the
loop](one-increment-and-the-loop#bounds-timeouts-and-stopping) covers what each
bound actually does.

The two token boxes take a token count, like `500k` or `2M`, and show it back as
`500,000` when you leave the box. Empty clears the cap. A value that is not a
token count blocks **Save bounds**. When the board refuses a value, the refusal
appears under the field it names; any other refusal appears above the buttons.

**The two defaults a runner inherits when it holds no cap of its own** are set
on Settings, by an admin only - with the wall off, anyone at the keyboard. A
runner with none of its own runs under them, which is the `(default)` beside the
label of a token box. A box reading the site default shows empty, not the number
it inherits - saving with no edit leaves it inheriting, rather than locking that
figure in as the runner's own. Its hint names the figure it is inheriting instead
of the usual token-count wording, so the empty box is never mistaken for an unset
cap.

Each fresh start of a loop - one started by hand, or the supervisor's first
start - writes the bounds it was started with onto its runner on its first
heartbeat, overwriting an edit made here and clearing any bound the runner held
for a flag the loop was not given. A supervisor restart does not: it keeps what
the runner holds, so an edit made here survives it. A runner made by `hatch work`
or `go-to-work --once` never takes bounds from that heartbeat.

**Live sessions.** One block per live session, oldest first, so a block never
moves as it beats: its checkout, its state and how long ago it was heard from;
the ticket it holds, as a link, and the last line it printed (or `nothing yet`);
its controls; its runs, spend and token budget; and its own bounds, narrowed by
its flags. A `go-to-work` loop that stops at one of its own bounds ends on
`Stopped: <why>`, for example `Stopped: --max-runs 3 reached`. The token budget
reads `used / cap tokens` for the night and `used / cap per hour` for the last
hour, each with a bar: green under 80% of the cap, amber from 80%, red at the
cap. A window with no cap reads `… tokens, no cap`, with no bar, and a cap taken
from the site default carries `(default)`.

A loop session's controls are **Pause** (or **Resume** on a paused one), **Stop
after this one**, and an order: a box for a ticket key with **Work this ticket**,
and **Run one pass**. An order not yet read is drawn as `ordered to work <KEY> by
<name>, <how long ago>`, with **Withdraw**, until the loop reads it; a loop holds
one at a time, so the order box is replaced by that line while it stands. The loop
takes an order at its next heartbeat, paused or running, then returns to what its
state says. A `once` session has no controls and says `one increment`.

**Pause all** and **Stop all** sit in the card's heading when at least one loop
session is live. **Pause all** acts at once. **Stop all** asks first - `Stop 3
sessions after their current increment?` - and each session finishes its current
increment, then stops; none is cut off partway.

A press only records what the board wants: the runner reads it between
increments, at its next heartbeat, never in the middle of one. Nothing on the
server starts or stops a process, which is why the same buttons work for a runner
in a container or on a laptop behind a router: the loop does the reading, and the
loop is what exits.

When live sessions span more than one machine, a note at the top says so: a key
is meant to be one machine, and tags and bounds are per runner. With none live,
the card says how to start one.

Each block ends in a closed **Machine** panel, showing what the session's last
heartbeat said about itself. It carries six machine fields - `os`, `arch`,
`agentVersion`, `container`, `username`, `processStartedAt` - plus `ip`, seven in
all. The six come from the CLI itself and are missing for a session run by a CLI
old enough to predate them, and the panel leaves a missing field out. `ip` is
never one of the six - it is the server's own reading of the connection, taken
after the forwarded header from the proxy in front of it is applied, so it is
set on every session regardless of CLI version, including one that sends none of
the other six. The panel also names the session's host, its agent, its build (a
short sha and how long ago, or `dev build` for one that hasn't been stamped), the
repositories it serves and what it drives. Nothing else is collected: no
environment variable, no process id, no MAC address.

**Recent sessions.** The runner's last twenty sessions,
newest first: where, kind, when it started and ended, runs, spend and tokens. It
has no controls, and the card is left out when there are none. The
**Leaderboard** stays the place for spend over time and by session.

A runner's name is recorded for a (profile, checkout) pair, so one checkout run
under two profiles speaks to two boards under two names, and each board's page
shows it under the name recorded for that pair. See [the runner's
name](the-runner#the-runners-name).

### Who can do what

- **An admin** creates runners, changes their owner, rotates and revokes keys,
  and can do everything an owner can.
- **An owner** edits the runner's settings and bounds, controls its sessions, and
  uses **Pause all** and **Stop all**. The owner reads the key's facts as text.
- **Anyone else** sees everything read-only, and a line at the top of **Live
  sessions** reading `{owner}'s to control`.
- **With the wall off** the page draws the owner's view for whoever is at the
  keyboard, with no **Create runner**, **Rotate key** or **Revoke**: a runner
  makes itself when `hatch config` runs in its checkout.
- **A revoked runner** has no actions for anybody, and its settings read as text.

A key may read the runners and heartbeat as one, but never set a bound or a
control, since an agent that could raise its own **Max spend** could raise its own
budget.

### On the board

A runner holding a ticket shows on that ticket too. On the board, its card
carries a small robot head - blue while the runner is live, amber once it has gone
quiet on a lease it still holds. Live is judged per session: the key's own
heartbeat counts, and so does any one of its sessions still inside the gone
horizon, so a key running several checkouts at once draws the head lit while any
one of them is still being heard from, even if the others have gone quiet. The
issue page names the claim as `<runner> at <where>` - who holds it, and from which
checkout - since when, and the last line that checkout printed. Clearing a claim
by hand makes the ticket claimable again at once; the runner notices and stops at
its next heartbeat, within a minute by default, so until then its session may
still push or move the ticket. Clicking the robot head itself, rather than the
rest of the card, opens that runner's own page at `/runners/<name>` instead of
the issue.

The board's own **Go to Work** / **Pause** button sets the same state Pause and
Resume do here.

### How a loop ends its session

A `go-to-work` loop ends its own session when it exits, whichever way it
exits - a bound, `--stop-file`, a stop from the board, Ctrl-C - so a loop
started straight afterwards in the same checkout starts at once. The one
exception is the hand-over to a restarted build, which keeps the session for
its successor. A loop that crashed never ended its session, and it stays live
until it has been quiet for the silence limit. A fresh start that finds it says
so once - how long it will wait at most - and starts as soon as the session
goes. If that session beats again while the new loop waits, it is a real second
loop in the checkout, and the new one is refused with the usual "is already the
runner" sentence and exits 1.

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

**Agents → Leaderboard** is every session's work-log row, board-wide unless
scoped. Pick a range - **Last 24 hours**, **Today**, **Last 7 days**, **Last
14 days** (the default), **Last 30 days**, or a pasted window - and narrow it
to one issue and everything beneath it. Below the controls sit a
spend-over-time graph, a **Top billing** list of the costliest sessions in the
window, and a table of every session in it, sortable by tokens, requests,
peak context, notional USD or when it ran. Nothing on this page writes - there
is no control that could, and the server refuses a browser outright. Under a
scoped tab the graph, the list and the table all cover only sessions run
against issues of the projects in scope, and the issue filter offers only
those issues.
[What a session is told](what-a-session-is-told#the-work-log-block) says what
goes into one of these rows in the first place.

## Reports

**Reports** is a row of the **Track** menu in the nav bar, beside Plan and
History, and opens on a card per report. **Timeline** answers when each top-level epic was in flight,
and how long its work sat in each column, drawn in the board's own column
colours; **Cumulative flow** shows how much work sat in each column over time,
and where it is piling up; **Changelog** lists what landed, bucketed by the
hour, day, week or month, at the level a stakeholder reads - epics, or the
stories under them. Cumulative flow carries its own **Projects**
control (drawn only where there is more than one project) - a checkbox per
project rather than the Timeline's one at a time - so the scope can be
narrowed to any of the projects. **All** and **None** sit at the foot of the
list, **All** greyed out while every project is ticked and **None** while
nothing is; **None** draws "No project is chosen." in place of the chart.
Beside **Projects**, a
**Colour by** control (also drawn only where there is more than one project)
cuts the same stack by project instead of by column. Hovering a row of
**Projects** shows **Only**, which keeps that project alone (on a touch screen
it is always shown). Dragging across the ruler above the Cumulative flow frames
the window: a highlight follows the pointer, and releasing sets the window to
the two instants dragged, whichever way the drag went, and **Length** reads
**Custom** (or the matching preset). A drag too short to mean anything does
nothing, and **Escape**, or releasing off the chart, cancels. **Earlier**,
**Today**, **Later** and **Length** still work on the result. A ticket counts as in
flight while it stands in any
column after the first that is neither done nor parked - the first column is
where work waits, a done column is where it ended, and a parked one is a
siding, so a stretch in any of the three is a gap in the drawing.

The Timeline has three controls. The window - **Earlier**, **Today**, **Later**
and a **Length**, ninety days to begin with - moves what is charted. **Length**
offers 1 hour, 2 hours, 6 hours, 12 hours and 24 hours before 1 week and up; the
five short ones end at now, where a week and longer end at the next local
midnight, and **Today** brings a window under two days long to now rather than
to that midnight. **Project** (drawn only where there is more than one) and
**Creator** narrow it to one project or one person's work. And an epic's row
can be hidden: a **Hidden** list appears beside the controls naming each one,
with **Show** beside it and **Show all** beneath, and what is hidden is
remembered for this browser only, never for anybody else looking at the same
board.

The Changelog opens on the last two weeks, not ninety days, with the same
window controls - **Earlier**, **Today**, **Later** and a **Length**. **Projects**
(drawn only where there is more than one) is a checkbox per project, as in
Cumulative flow, with the same **All** and **None**; **None** draws the same
sentence, "No project is chosen.", in place of the list. **Bucket** - hour, day, week or month, day to begin
with - sets how finely the window is cut: each bucket is a section headed by its own
date, and a stretch with nothing in it between two buckets is a line, "Quiet
from" one date "to" another. **Level** picks what a bucket holds, and the page
opens on story: each epic that had stories close in the bucket is a heading,
marked **Completed** where the epic itself closed there, with its stories
beneath; at epic, each epic closed is a card. **Loose** is off to begin with;
on, an **Also landed** list under each bucket adds the bugs and the stories
with no epic that closed, at either level, with an emergency-priority one
tinted. A window in which nothing landed says so. Every control writes itself
to the address bar, leaving out what is still its default, so the address can
be sent to somebody and opens on the same answer, and Back is not walked
through each change.

**Copy as Markdown**, beside the controls, puts the whole loaded answer on the
clipboard - a `##` heading per bucket; at epic level a bullet per epic, its key
a link, its title and how much sat beneath; at story level a bullet per epic,
`(completed)` where it closed there, with its stories nested beneath. With
**Loose** on, an `Also landed` heading, a `###` under its bucket, and a bullet
per entry, an emergency one marked **Emergency**. Each key links to the issue
on the Hatch the reader was on, and a quiet gap is left out. A window in which
nothing landed pastes the same words the page shows. The button is not drawn
where the browser has no clipboard access, and a copy the browser refuses is
said so on the button itself.

**Consumption** lists what each epic consumed in a window - tokens, notional
dollars and runner time, the epic's own and everything beneath it - one row to
an epic. It opens on the last three months, with the same window controls -
**Earlier**, **Today**, **Later** and a **Length**. **Projects** (drawn only
where there is more than one) is the same checkbox per project, with **All** and
**None**. **Type** picks what a row is, epic to begin with, or reflection, story,
task, bug, or **All**; **Status** keeps the issues now standing in one column,
**Any** to begin with. The table is sorted by clicking a column head - **Tokens**,
**Notional USD**, **Runner time** or **Last session**, the most recent first to
begin with - and the head of the sort in use says so. A row whose sessions
reported no price draws a dash rather than `$0`, and one with some unpriced
sessions draws what was priced and how many were not. A line over the table
gives the totals for every issue that matched, and under it **Spend over time**
draws the same filter as one bar per bucket, oldest at the left - an hour or a
day, as the server chooses - with a bucket in which nothing ran drawn as a zero
you can still hover; a bucket's hover names how many of its sessions reported
no price beside its total. The **Tokens** and **Notional USD** buttons over the
graph pick what it measures, one at a time and never on a shared axis, and the
choice is in the address bar as `measure`, left out at tokens. The axis and the
span are labelled from what the server answered, so a window it widened onto
its grid reads as the wider window. The table holds at most a
hundred rows: where more matched it says "The first 100 of 240 issues. The totals
cover all 240." above the table, so the larger total does not read as a bug. A
window in which nothing was consumed says so, and says when the log does run,
and still draws the graph as a row of zero bars; a filter under which nothing
was ever logged says that instead and draws no graph. A **Download CSV** link
beside the totals line hands the whole filter out as a file, uncapped where
the table reads only the first page. Every control
writes itself to the address bar, leaving out what is still its default.

## History and the work log

These are two different things. **Track → History** is every event across
every project, newest first, a hundred to a page, each one naming when it
happened, the issue, what happened, who did it and what changed. Under a
scoped tab it lists only the events of the projects in scope, and paging walks
those rows.
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
time is unknown. Its colour is one of three bands by percent used, green under
50%, yellow to 79% and red from 80%, coloured by whichever of your windows has
the highest percentage. Pressing it opens every window your account
reports, each with its own percent used and how old the reading is, and a
button to refresh it. [Flow, WIP and the battery](flow-wip-and-the-battery)
covers what the board does with this reading.

## What is waiting on you

The control at the right of the bar is board-wide, not per person. At rest it
reads **Clear**; the moment anything needs a person or the loop is stuck, it
lights and opens a panel in two groups.

**Human** - things only a person can clear, in the order to work through
them: **Trunk builds that fail**, **Backups**, **Stalled merge trains**,
**Deadlocked**, **Questions to answer**, **Stalled issues**,
**Pull requests to review**,
**Approved, not merging**, **Landed, not done**, **Finished with no code**,
**Contributions runners will not work**, **Runner capacity**,
**Waiting on a runner**. **Contributions runners will not work** lists each
contribution in review whose branch conflicts with the trunk or whose build
failed, in a project that has not turned on **Runners work contributions**:
the key links to the issue, the pull request opens on the forge, and a
contribution mirrored from another instance names where it came from.
**Waiting on a runner** lists every issue whose next move no live runner can
take for want of a model or a tool, and links to the Runners page, where each runner shows what it offers
([the runner](the-runner#what-a-runner-advertises) covers what that is); like
**Backups**, it lights the control and adds no pill to the bar. **Backups**
appears only when the configured backup is failing or stale, and links to
Settings, where the folder is fixed. **Stalled merge trains** appears only when
a project's train has passed a driver over; each row links to the issue at the
head of the train and names the runner passed over and the one it went to. It
clears when that runner next checks in, and it does not light the control, since
a runner that died never will.

Each row under **Stalled issues** is the issue's key, a link, its title, the
reason the stall gave when it gave one, and then one line for who has it: `resumes
in N minutes` while the loop will resume it on its own, `any moment now` once it
is past its window, `held by a person` when somebody pressed **Hold for me**, or
the board's own sentence when the board itself holds it, such as `stalled 3
times in a row, at this board's limit of 3 - held for a person now, not resumed
again`. Only a board-held row lights the control
and is counted; a loop-owned row or a person-held one is listed and does
neither, since one is already on its way and the other is somebody's choice. With
nothing to list, the section opens to *Nothing is stalled.* [One increment and the
loop](one-increment-and-the-loop#stalls) covers what a stall is.

Each row under **Waiting on a runner** is the issue's key, a link, its title,
and `in <column> - <the gap>`. The gap reads `no live runner offers fable`, or
`no live runner has pwsh 7 or newer and playwright-browsers`; a move that
needs both joins them with `; `. A pinned id reads `pinned to <id> - no live
runner's agent is <agent>`. A board with no live runner at all lists nothing -
that is the *no runner* condition, not this one - and neither does a split
fleet, one runner lacking the model and another the tool, since no single
sentence is true of all of them. With nothing to list, the section opens to
*Nothing is waiting on a runner.* [hatch queue](cli-reference#queue) prints
the same list at the terminal.

**Runner capacity** lists every project whose clear implementation steps no
live runner, or only one, can take - a narrower question than **Waiting on a
runner**'s: a runner capable of the move can still fold a step on a claim, a
full WIP slice, a question, a person or a dependency edge, and those are a
runner's own business, not a capacity gap. Each row names the project, how
many clear steps it has, links to the first ten, and reads either `N clear
steps: only <runner> can take them` or `no live runner can take them:
<runner>: <reason>; ...`. A project two or more live runners can take is not
listed at all. With nothing to list, the section opens to *Every project's next
steps have two or more live runners.* Unlike **Waiting on a runner**, only a
no-taker row lights the control - a one-taker row is information, not
breakage, and is not counted. The web's work queue - the button beside Go to
Work, next to the battery - shows the same two sentences next to a row's own
words, the way [hatch queue](cli-reference#queue) does at the terminal.

**Agent** - things the loop is already on: **Branches that
conflict**, **Branch builds that fail**. A separate **Out of usage** section
sits above both, for any of your own runners that have run out, and clears
itself once the usage comes back.

A section with nothing in it shows only its heading, which is a button:
pressing it opens the section to its empty sentence, and pressing it again folds
it. A group with nothing in any of its sections folds the same way, to its group
heading alone, and opens to its section headings. A section or group with
anything in it is open and has no button, since there is nothing to hide.
**Backups**, **Stalled merge trains** and **Out of usage** are not folded but
simply absent when empty. **Pull requests to review** stays open while it has a
count to say - issues in review with no pull request recorded, or held back by
the loop - because that is the only place it is said. The panel opens folded
every time, and nothing is remembered between openings. Pressing a heading never
closes the panel; Tab reaches every heading, and Escape closes it.

A card still wears a lock or an hourglass for a stalled or held issue, and the
issue's own stall panel is still where **Resume now** and **Hold for me** are
reached.
[Questions and decisions](questions-and-decisions) covers answering a
question; [One increment and the loop](one-increment-and-the-loop#stalls)
covers what a stall is and when the board resumes one on its own.

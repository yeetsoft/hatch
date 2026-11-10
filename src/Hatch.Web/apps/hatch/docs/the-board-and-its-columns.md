---
title: The board and its columns
section: Concepts
order: 10
summary: The seven columns a stock board ships with, and who moves work out of each.
---

This is the stock board, which is what the **Hatch** logo at the left of the bar opens: the board is the home page, and the bar has no Board link of its own. An
operator may rename its columns, add more and reorder them, so nothing below
depends on a name: each column is described by what happens in it, and the
names are only what a fresh install calls them.
[The lifecycle of a ticket](the-lifecycle-of-a-ticket) follows a ticket across them.

## The seven stock columns

In order, left to right:

- **Draft** — an idea being written; nothing reads it. A person moves it on.
- **Breakdown** — turn the draft into a specification, with acceptance
  criteria on the issue and children under it. A session moves it on.
- **Backlog** — specified work awaiting selection by a person. It is the gate
  between "somebody wrote a paragraph" and "a pull request is open". A person
  moves it on, except where the board carries it (see the hops below).
- **To Do** — analyse the work until implementing it is mechanical. A session
  moves it on.
- **In Progress** — write the code, get it green, push it, and put it up for
  review. A session moves it on.
- **In Review** — read the pull request, wait for green, merge. A person moves
  it on. A session steps in only for a pull request that has stopped merging
  cleanly or whose build has failed, and that increment starts and ends in
  the same column.
- **Done** — shipped, and terminal. Only a person moves work into it, or the
  close hop below, which carries a pre-approved parent, or a parent in a project
  whose **A passing check approves the merge** setting is on, and a declared
  no-code ticket under the same rule.

## Who moves work

There are three answers to who moves a ticket out of a column:

- **A person.** Nothing else will.
- **A session.** Some playbook names the column as its `from` for that
  issue's type, so the loop dispatches a session to work the ticket there and
  move it on.
- **The board, with no session.** This is a hop: the board carries the ticket
  one step on by itself.

Which of these a column is comes from the playbook matrix, never from the
column's name. A column no playbook names as its `from` is a person's. One
column can be a session's for a story and a person's for an epic, because a
playbook names the issue types it applies to. Rename **Breakdown** to
anything you like and it is still a session's; add a column nobody writes a
playbook for and it is a person's.

The hops the board makes are:

- **Express skips** — an express issue is carried past a column ticked
  **Express skips**.
- **Epics** — an epic is carried toward the column where code gets written
  once it has children.
- **Under** — a story or bug in a column ticked **Express skips** is carried
  on, with no session, while its parent epic stands in the column where code
  gets written.
- **Parent pulls** — a child is carried on, out of a column ticked **Parent
  pulls**, while its parent stands in the column where code gets written. Out
  of the column that feeds it, only a child its parent already planned is
  carried: a task under a story, not a story under an epic (whose analysis cuts
  it into tasks), and not a child too long to stay in the column where code
  gets written. Those are analysed as usual.
- **Sendback** — a childless issue whose brief is too long for its playbook is
  carried back to the column that feeds the column where code gets written. It
  is the one hop that moves a ticket left.
- **Approve** — a pre-approved issue in review, whose pull request is current
  and would merge cleanly, goes into the column ticked **Merges PR**.
- **Close** — a parent in review with every child done, when it is pre-approved
  or its project has **A passing check approves the merge** on, or a ticket
  that declared it was finished with no code, when it is pre-approved or its
  project has **A no-code declaration closes the ticket** on, goes into the
  first Done column to its right. The merge setting alone does not carry a
  declared ticket. Without either, a declared ticket waits in review for a
  person.

[The lifecycle of a ticket](the-lifecycle-of-a-ticket) follows one ticket
across every column and hop above, start to finish.

## Narrowing the board

On a desktop window the header is one row, left to right: **Search**,
**Project**, **Types**, **Needs me**, **…** (More filters), the count
(*N of M*) and **×** (Reset filters) while a filter is set, then Go to Work,
the work queue, the flow view and **+** (New issue). **Search** rests narrow
and widens while you type in it. **Project** shows only its key (or **All**)
when closed, and its open list keeps the full names. **Types** opens a checkbox per
type, each with the number of cards it holds; hovering a row shows **Only**,
which keeps that row alone (on a touch screen it is always shown). **All** and
**None** sit at its foot: **None** leaves the board blank on purpose, with the
count reading *0 of N*, and **×** brings every type back. The choice, None
included, is remembered in this browser. **Needs me** shows the
cards holding a question nobody has answered. **…** opens the filters kept
out of the row, today the **Assignee** choice; the button is lit while one of
them is set, and the count still includes it. **×** returns every filter to
its default and is drawn only when one is set. On a phone the row folds
everything past the search box behind **Filters**, where **Assignee** stays
beside the others.

The **Project** picker is drawn only when Hatch has more than one project,
and **All projects** is a choice like any other. It and the type filter are
remembered per browser, in `localStorage`, across pages, reloads and
restarts. A `?project=KEY` link opens that project whatever this browser
remembers, and the browser remembers it afterwards. A second control in the
bar scopes the whole tab to projects: see
[Scoping a tab to projects](#scoping-a-tab-to-projects). Under a scope the
picker lists only the scope's projects, its empty choice reads **All in
scope**, it is not drawn when the scope holds one project, and a
`?project=` or remembered project outside the scope is dropped.

A column that shows no card is drawn at half the width of one that does, so
empty columns stop pushing the busy ones off screen. That covers a column with
no cards at all and one whose cards the current filters all hide (it still says
*N hidden by the filter*). It widens again, with no reload, the moment a card
shows in it: a filter cleared, a card filed, moved or dropped in, a live update.
A column whose only cards are folded behind **+ N waiting** keeps its full
width, since the fold is work one click away. A drag does not widen the narrow
columns; each is still a full-height drop target. The phone's stacked layout
is unchanged.

## Scoping a tab to projects

Above the board, the bar carries a second control: which projects *this
browser tab* is looking at. Scoped, it shows each project's mark and key,
filled; unscoped, it reads **All projects**. Opening it shows every
project as a checkbox — ticking or unticking one writes the scope
immediately, and unticking the last one means every project again, same
as never scoping at all.

The scope lives in the address bar, as `?scope=KEY,KEY` — project keys,
comma-separated. A link you click carries the tab's own scope forward; a
link you paste or are sent means exactly what it says, with no
browser memory behind it, so two tabs can hold two different scopes at
once. Projects, Statuses, Playbooks, Runners, Overnight, Get the runner,
Users, API keys, Settings and Docs ignore it — none of them is an issue
list. Later sections on this page, and this app's other pages, say where
else it reaches.

In a Hatch with three projects, `AER`, `FIL` and `GEO`, three tabs can each
look at one of them:

1. Open the board. The control reads **All projects**, with every box ticked.
2. Untick `FIL` and `GEO`. The address gains `?scope=AER` and the tab's title
   becomes `AER - Hatch`.
3. Duplicate the tab. The copy opens on the same address, so it starts on the
   same scope, `AER`.
4. In the copy, tick `FIL` and untick `AER`. Its address becomes
   `?scope=FIL` and its title `FIL - Hatch`; the first tab is untouched.
5. Duplicate once more and, in that copy, untick `FIL` and tick `GEO`. Its
   address becomes `?scope=GEO` and its title `GEO - Hatch`. Three tabs,
   three scopes, three titles, side by side.

**A link you click keeps the scope; a link you paste means what it says.**

On the board, cards of projects outside the scope are not drawn, and are not
counted in *N hidden by the filter* (nor in *N of M*), because the scope sits
upstream of the filter row. **New issue** offers only the scope's projects
and opens on the first of them when the filter and the remembered project are
outside it. **Go to Work** starts your runners; the project a runner works in
is its own `--project`, set where it is started, not here.

An issue outside the tab's scope still opens from a link, says so under its
header, and offers to add its project to the tab in one press.

**Manage → Bulk edit** finds only the scope's issues: its Project filter lists
the scope's projects and its results are theirs. Its *move to project* target
is deliberately not narrowed, because moving an issue out of a scope is an
edit, and the result names the issues that left. **Manage → Import** offers
only the scope's projects in its Project chooser, and has the project already
chosen when the scope holds one. See
[Import and bulk edit](issues#import-and-bulk-edit).

**Track → History** and **Agents → Leaderboard** follow the scope too.
[History](watching-the-loop#history-and-the-work-log) lists only the events
of the projects in scope, and the
[Leaderboard](watching-the-loop#leaderboard) covers only the sessions run
against their issues.

**Plan** follows the scope too. Its project picker lists only the scope's
projects, reads **All in scope**, and is not drawn for a one-project scope;
picking none draws the epics of every project in scope. A `?project=` outside
the scope is dropped from the address.

**Search** follows the scope too. Its Project filter lists only the scope's
projects, reads **All in scope**, and is not drawn for a one-project scope;
with none chosen the results are those of every project in scope. A
`?projectId=` outside the scope is dropped from the address.

**Track → Dreams** follows the scope too. Its list holds the dreams of the
projects in scope and every dream that leans toward no project, which always
shows. Its Project filter, the **Lean** chooser under the composer and the
**Project** chooser under **Weave an epic** list only the scope's projects,
**None** stays on **Lean**, and the filter is not drawn for a one-project
scope. A `?projectId=` outside the scope is dropped from the address. See
[Dreams](dreams#finding-one).

**Reports → Timeline, Cumulative flow and Changelog** follow the scope too.
Each one's project control lists only the scope's projects, and is not drawn
for a scope of one project. See [Reports](flow-wip-and-the-battery#reports).

## Go to Work and Pause

The green **Go to Work** / **Pause** button sits leftmost in the header. It
drives only your own loop runners — a loop started by hand or on standby
(see [Starting it at login](the-runner#starting-it-at-login)) — never
another person's, whatever your role. **Go to Work** sets each of yours to
running; **Pause** pauses the running ones. A press lands at the runner's next heartbeat: at once
if it is napping between passes, after the increment in flight otherwise.
With none of yours on, the button is disabled and the status says "No
runner"; press it to find the link to start one.

A one-word **status** sits right after the button — **Working**, **Pausing**,
**Stopping**, **Out of usage**, **Idle**, **Paused**, **Stopped**, **Off** or
**No runner** — coloured by its tone, so a fault shows. On a desktop window,
hover it for the whole line or press it to open the line with the ticket's
link and column, the project an idle loop is bound to, and, with none of yours
on, the **Start one at login** link. On a phone the whole line is a row under
the bar instead. It says what your loops are doing: working on a ticket;
pausing after or stopping after the ticket in hand; out of usage; idle;
paused; stopping; stopped unexpectedly; off; or no runner. When several of your loops are on it names
the one most worth saying and adds "and N more". It is blank while the runners
load, and it follows the runners poll (every 20 seconds) and every press. A
"Stopped unexpectedly" line becomes "No runner" two hours after the loop was
last heard.

## Column flags

A column's behaviour is a set of flags, ticked on the **Statuses** screen.
There are nine, and a column carries whichever of them it needs.

- **Done column** — landing here means the work shipped. Nothing is dispatched
  into one, it stamps the issue's closed time, and a dependency waits on it.
  The stock Done column ships with it ticked. A person or a key may tick it,
  and it is refused while **Protected** or **Merges PR** is ticked on the
  column.
- **Deferred** — parked work. A Deferred column is not drawn on the board and
  has no drop target; the issue page's status bar is the only way in. No
  session starts work there, and the rollup leaves it out of every total. No
  stock column ships with it, any number of columns may carry it, and a person
  or a key may tick it. It is refused while **Protected** or **Merges PR** is
  ticked.
- **WIP** — whether a story, bug or epic standing in the column counts toward
  the limits in the WIP limits card above the table. In Progress and In Review
  ship with it ticked, and only a person may tick it. A tick left on a
  Deferred or Done column is ignored. An upgrade ticks the two only where the
  board still has columns of those names. [Flow, WIP and the battery](flow-wip-and-the-battery#wip-limits) covers
  what the limit is for and how to read the card above the table.
- **Express skips** — an express issue standing here with no unanswered
  question is carried to the next column with no session. The Done column is
  never entered, and the flag says nothing about whose column anything is.
  Backlog ships with it ticked, on a board that is still exactly the stock
  seven. Only a person may tick it.
- **Parent pulls** — a child standing here is carried to the next column with
  no session while its parent stands in the column where code gets written.
  Backlog ships with it ticked. Only a person may tick it.
- **Agent files here** — an issue filed by a program is born in this column
  instead of the leftmost one. Backlog ships with it ticked. Only a person may
  tick it, one column at a time: ticking it unticks the old one. A Deferred or
  Done column is refused. With none ticked, agent-filed issues are born
  leftmost.
- **Code written here** — the column where code gets written. A dependency
  holds the move into it, a story's open tasks hold the move out of it, and
  **Parent pulls** reads the parent's position against it. No stock column is
  seeded with it ticked. Only a person may tick it, one column at a time, and
  a Deferred or Done column is refused. With none ticked, Hatch uses the
  column just left of review, which is In Progress on the stock board.
- **Protected** — only an owner or approver of the issue's project may move
  work into this column by hand: never a key, never a hop, never a dispatch.
  No stock column ships with it. Only a person may tick it, never a key. A
  Deferred or Done column is refused, and it cannot be unticked while **Merges
  PR** is ticked. Ticking it moves `Columns.AwaitingReview` one column left
  for every project on the board, not only the one adding the column, since
  that landmark steps back over any protected column it meets walking left
  from the first terminal one — see
  [a passing check approves the merge](letting-the-loop-merge-for-you#a-passing-check-approves-the-merge).
- **Merges PR** — the runner merges an issue's pull request once it stands in
  this column, current and green, with no session. No stock column ships with
  it. Only a person may tick it, never a key. It needs **Protected** ticked
  first, and a Deferred or Done column is refused.

**Runner tags** — a field, not a tenth flag: a list of [runner
tags](the-runner#what-a-runner-advertises) the column requires, the same way a
project or an issue can. They union with the project's, the issue's ancestors'
and the issue's own, the same union that gates every move a runner is
handed, in every column and on every hop — see the dispatcher's tag fold
(`docs/hatch.md`, "The dispatcher") for the exact rule. A column with none set
adds nothing to it; one with tags of its own gates every move out of it, hops
included, for a runner that does not advertise them all, and names the column
by name in the sentence it folds with. Set it in the **Runner tags** column on the Statuses screen, one tag per
line, saved the moment you leave the box; a refusal shows under the box
rather than at the top of the page. A column with tags draws them small under
its name on the board; one with none draws nothing.

## Example: an Approved column

Add an eighth column, between In Review and Done, and call it `Approved` —
every column name on this page is a placeholder, and nothing hangs on it
being this one. Tick **Protected** on it, then **Merges PR**; the second
checkbox refuses to tick until the first already has.

What that changes: a person moving a ticket into the new column by hand *is*
the approval, and the runner then merges the ticket's pull request once its
branch is current and its build is green, with no session reading the diff
and nothing spent. A ticket already pre-approved while it stood in review is
carried into the column by the approve hop above, with no person moving it at
all. [Review, pull requests and the merge train](review-pull-requests-and-the-merge-train) covers reading the pull
request itself, and the train an approved ticket joins behind whatever else
in its project is also approved.

The stock board still ships with exactly the seven columns above, and
`Approved` is never one of them. Adding it — naming an approver, and choosing
which of the forge's three shapes the repository is — is covered end to end
on its own page, not repeated here:
[letting the loop merge for you](letting-the-loop-merge-for-you).

Columns and their flags are the shape of the board. [Issues](issues) is what
moves across them: dependencies, deferring, and closing a subtree.

## Stories on epics

Stories on epics is a per-project setting that only an owner of the project
can change. An owner sets it in the project dialog, in its Board section, with
the "Show stories on epics, not the board" checkbox, and it is saved with the
rest of the dialog; anyone else sees a sentence saying what it is set to. It is
also written through the API ([API reference](api-reference)).

With the setting on, a project's stories are drawn on their epic's card
instead of as cards of their own. An epic with stories carries one dot per
story, in rank order, each coloured and titled from the column the story
stands in, and a count reading "N of M done". Only a story in the terminal
column counts as done: one in a protected column, such as a Merges PR column,
or in a deferred one does not, and a story past the done horizon is still
counted.

A story that is waiting on a person, with an unanswered question or held for
a person, draws a dark "!" in place of its dot. The count then reads
"N of M done · K needs me" (nothing is added at zero), a story's line shows
"!" and **Needs me** where the column name would be, and the strip's
screen-reader sentence adds "K needs you". A story stalled at the resume limit
is not marked.

A story is carried off the board exactly when the setting is on, its parent is
an epic standing in a column the board draws, and it is not itself an epic, a
task, a bug or a reflection. Everything else keeps its own card, including a
story whose epic is missing, shelved or past the horizon. A carried story is
never a card, at any column or fold stage, and the "hidden by the filter" count
on a column never counts one. With the setting off, the board draws exactly as
it always has.

The filter bar finds a carried story through its epic. Searching a carried
story's key or words from its title, the **Needs me** switch and the Assignee filter
each draw the epic whose carried story matches. The Types and Project filters
judge the epic itself. The story is never drawn as a card.

An epic's card shows its stories two ways. Expanded, which is how every epic
starts, it draws one line under the dots for each story: the status icon in
the colour of the column the story stands in, its title cut to one line, and
the name of that column. Clicking a line opens that story's summary on the
desk without leaving the board; at phone width, where the board has no desk
behind it, it goes to the story instead.

The strip of dots and the count is a button. Pressing it collapses the card to
that strip alone and pressing it again brings the lines back. It can be
reached with Tab and toggled with Enter or Space, and it reports whether it is
expanded to a screen reader. Dragging the epic by its face still moves it; a
press on the strip is never a drag.

Which epics are collapsed is remembered per browser, under the key
`hatch.board.collapsedEpics` in its local storage. Only collapsed epics are
kept, so expanded is the default and a browser that has never collapsed one
holds nothing. A fresh browser, or one whose storage was cleared, sees every
epic expanded.

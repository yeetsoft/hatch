---
title: The board and its columns
section: Concepts
order: 10
summary: The seven columns a stock board ships with, and who moves work out of each.
---

This is the stock board, which is what the **Hatch** logo at the left of the
bar opens, unless the person chose Ambitions in the Home screen card on the
Settings page: the board is the home page otherwise, and the bar has no
Board link of its own. An operator may rename its columns, add more and
reorder them, so nothing below depends on a name: each column is described
by what happens in it, and the names are only what a fresh install calls
them. [The lifecycle of a ticket](the-lifecycle-of-a-ticket) follows a
ticket across them.

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

A card's place within a column is its rank, floated first by its own
priority tier (see [Rank and ordering](issues#rank-and-ordering) and
[Priority](issues#priority)) — and, before that, by the level of the project
it sits in: an expedited project's cards float above every other project's in
each column, and a paused project's sink below them.

A card's priority and pre-approval marks say where the level came from and
whether it is honoured, on hover: "expedited on AER-12", or "pre-approved on
AER-12, not honoured in OPS" for a level set on a parent in another project
that stopped at the line. A level that was not honoured reads as none or normal
on the card itself, so it wears a dim, dashed mark of its own. The parent row
says the same of a pull: "AER-12 is in progress, but its pull is not honoured
in OPS". Where the parent is outside your scope the sentence reads "from an
issue outside your scope" and names nothing more.

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
**Project**, **Types**, **Needs me**, **Blocked**, **Contributions**, **…** (More filters), the count
(*N of M*) and **×** (Reset filters) while a filter is set, then Go to Work,
the work queue, the flow view and **+** (New issue). The work queue also opens
from [the console](#the-console) with `>queue`. **Search** rests narrow
and widens while you type in it. **Project** shows only its key (or **All**)
when closed, and its open list keeps the full names. **Types** opens a checkbox per
type, each with the number of cards it holds; hovering a row shows **Only**,
which keeps that row alone (on a touch screen it is always shown). **All** and
**None** sit at its foot: **None** leaves the board blank on purpose, with the
count reading *0 of N*, and **×** brings every type back. The choice, None
included, is remembered in this browser; a choice saved as exactly the five types that came before `ambition` reads as all six. **Needs me** shows the
cards holding a question nobody has answered. **Blocked** shows only the cards
the work queue says no pass and no live runner will move: a
deadlock, a step with no playbook, a stall, or a ready step no live runner can
take. It does not show a card waiting on a dependency, a full WIP section, a
family or its turn, a running session, or a person, nor one the queue does not
read. Pressing it reads the queue, so the board draws nothing until the queue
lands, and a failed read shows its sentence beside the board; it works with the
flow view on or off, and is not remembered between visits. **Contributions** has three
choices: **All** (the default, a contribution drawn with everything else),
**Only** (just the cards with a contribution record) and **Hidden** (every card
but those); the count and **×** treat it like any other filter, and the choice
is remembered in this browser as the types are. A contribution's card wears a
mark beside its type glyph and names where it came from; see
[Contribution](issues#contribution). Dropping one into a column that merges pull
requests asks first. **…** opens the filters kept
out of the row: **Assignee** and **Filed by**. The button is lit while either
is set, and the count and **×** include them. **Filed by** lists everyone who
filed a card on the board, hidden cards included, each with the number of cards
they filed, sorted A-Z. **Only**, **All** and **None** read as they do in
**Types**, and **None** leaves the board blank with the count reading *0 of N*.
A name nobody has a card for any more drops out of the choice. Neither is
remembered between visits. **×** returns every filter to its default and is
drawn only when one is set. On a phone the row folds everything past the search
box behind **Filters**, where **Assignee** and **Filed by** stay inline beside
the others.

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

On a touch screen, press and hold a card until it lifts, then drag it to a column. A quick tap opens the issue and a swipe scrolls the board. Hold a lifted card near the top or bottom edge to scroll.

## Scoping a tab to projects

Above the board, the bar carries a second control: which projects *this
browser tab* is looking at. Scoped, it shows each project's mark and key,
filled; unscoped, it reads **All projects** in an outlined pill, its caret
turning over while the panel is open. Opening it shows every
project as a checkbox — ticking or unticking one writes the scope
immediately, and unticking the last one means every project again, same
as never scoping at all.

Each row also carries that project's level, next to its name. An owner of
the project sees two buttons: a green up arrow that expedites it, and a
pause mark that pauses it. A glyph is outlined when the project is not at
its level and filled solid when it is; it half-fills while the pointer or
the keyboard focus sits on it, before anything has changed. Pressing the
arrow on a normal or paused project expedites it; pressing it again on an
expedited one sets it back to normal. Pressing pause on a normal or
expedited project pauses it; pressing it again on a paused one sets it
back to normal. Only one level holds at a time, so expediting a paused
project or pausing an expedited one moves it straight to the new level,
with no stop at normal in between. The row redraws at once on a press, and
if the write is refused the row goes back and the panel shows the
refusal's own sentence. Anyone who is not an owner sees a plain mark
instead — one filled glyph, for a project already expedited or
paused — and nothing for a project at its normal level.

Beside the trigger, never inside it, a small cluster says which projects are
expedited or paused without opening the panel. A green up arrow leads a run
of pills, one for each expedited project; a pause mark leads a second run for
the paused ones. Each pill is in its project's own colour and carries its key,
and the pills overlap slightly, the first on top. Hovering a run names it, as
*Expedited projects: …* or *Paused projects: …*. Nothing is drawn when no
project is expedited or paused. The same glyphs also sit inside the trigger,
before its caret, so pressing one opens the panel like the rest of the
trigger. An owner sees an **×** on a pill when the pointer or focus is on it
(always, on a touch screen, which has no hover); pressing it sets that
project back to normal and the pill goes at once, with no reload. Anyone who
is not an owner sees plain pills, each titled with the project's name, and no
×. On the phone the cluster wraps onto its own line under the trigger, and
each pill is at least 44px tall to press.

The scope lives in the address bar, as `?scope=KEY,KEY` — project keys,
comma-separated. A link you click carries the tab's own scope forward; a
link you paste or are sent means exactly what it says, with no
browser memory behind it, so two tabs can hold two different scopes at
once. Projects, Statuses, Playbooks, Runners, Get the runner,
Users, Settings and Docs ignore it — none of them is an issue
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

**Search** also finds an issue by a title it used to have, not only its
current one. A result found that way lists the old titles under the current
one, newest first, and reads "previous title" under Matched.

**Track → Dreams** follows the scope too. Its list holds the dreams of the
projects in scope and every dream that leans toward no project, which always
shows. Its one **Project** picker, above the composer, lists only the scope's
projects after **No project**. A `?projectId=` outside the scope is dropped
from the address. See
[Dreams](dreams#finding-one).

**Reports → Timeline, Cumulative flow, Changelog and Consumption** follow the scope too.
Each one's project control lists only the scope's projects, and is not drawn
for a scope of one project. See [Reports](flow-wip-and-the-battery#reports).

## The console

Press `/` on any page and a prompt opens over it. Type an issue's key
(`HA-3`, `ha-3` or `ha3`), its number (`3`), or words from its title, and the
list narrows as you type. A title word matches from the start of a word in
the title - `dash` finds "Dashboard filters", but `ash` does not. **↑↓**
move, **↵** opens the highlighted row and **esc** puts it away. Paste a whole
key (`HA-455`, any case, with the hyphen) and press **↵**, and you land on that
issue at once, with no lookup first, even before the list has filled: on the
board, a card the board holds is peeked, and any other key opens its issue
page, which says so when the key names nothing. Move the highlight with
**↑↓** or the mouse first, and **↵** takes that row instead. The last row,
**Search everything**, carries what you typed to the Search page - the way to
find an issue by a title it used to have, since the console itself matches
current titles only. A key or a number finds closed issues too, not only the
ones on the board: each project knows the highest number it has handed out,
so typing `455` lists issue 455 of every project that got that far, at once.
The rows for keys the page already holds show their titles right away; the
rest show the key with a dim `…` where the title goes, and fill in a moment
after you stop typing, when the console asks the server once. More than fifty
keys say `50 of 112 — keep typing`. It is not a
shortcut while you are typing:
`/` in a text box, a description editor, or the board's own search box is a
slash.

Press `c` on the board and the New issue dialog opens, with its title box
empty and ready to type in. It is the same dialog the **+** button opens, and
the button's tooltip says `c`. Like `/`, it is a letter while you are typing:
in a text box, a description editor or the board's search box, `c` types `c`.
It opens nothing with Ctrl, Cmd or Alt held, so Ctrl+C and Cmd+C still copy,
and nothing while a dialog, the peek or the console is open, or while a card
is being dragged. It works on the board only. The dialog opens on *epic*; a
type you pick and file carries to the next filing on the same page.

Press Escape on the board and the flow view's banding goes off, and the band
button reads unpressed. Escape goes to the smaller thing first: with a dialog
open (a card's peek, the work queue, New issue) the first Escape closes the
dialog and the banding stays, and the second turns it off; an open filter
menu folds, and the board's search box clears, before Escape reaches the
banding. It does nothing with Ctrl, Cmd, Alt or Shift held.

The first character of the prompt says what it lists:

| Prefix | Lists |
| --- | --- |
| none, or `/` | issues, as above (`/ha-3` is `ha-3`) |
| `>` | commands |
| `?` | this list of prefixes; **↵** does nothing on it |

The prompt's verb (**go**, **run** or **help**) and the status line say which
is on. A search for an issue cannot start with `>` or `?`.

A command is a sentence you can find by any words in it, in any order, ignoring
case and punctuation: `>board`, `>go board` and `>goto board` all find **Go to
Board**. Names that start with what you typed come first, then names with a part after
an arrow that does (`>todo` finds **Status → To Do** before **Go to Docs**),
then any other match. A bare `>` shows the
first eight commands, with "8 of N" in the status line, until you type.
**↑↓** move, **↵** runs the highlighted one and **esc** puts the prompt away.
The commands are:

- **Go to** each page the primary nav lists, plus **Board** and **Settings**.
  The tab's scope is kept. **Go to Users** is there for an Admin only, as it is
  in the nav.
- **New issue** opens the same dialog the board's **+** does. On the board it
  is the board's own, on the project the board's filter names. On any other
  page it opens over that page, on the tab's first project and offering only
  the tab's projects, and filing opens the new issue's page.
- **Open the work queue**, found by `>queue` or `>work`. On the board it opens
  the same queue the header button does. On any other page it lands on the
  board, with the tab's scope kept, and the queue open; reloading the board or
  coming back to it with Back does not open it again.
- **Theme → Auto**, **Theme → Light** and **Theme → Dark**, the three choices
  of the theme switch. You stay on the page you were on.
- On the board only, the filter rows. **Filter → Blocked** and **Filter → Needs me**
  each switch the same button in the filter row, and say **on** or **off** at
  their right according to what the board shows now. When the board offers more
  than one project there is a **Filter →** row per project (`>filter ha`), named as
  the Project picker names it; taking one sets the project and writes
  `?project=`. **Clear filters** is there only while a filter is set, and
  returns every filter to its default as the **×** in the filter row does. The
  console closes when you take one, and the board changes under it. Off the
  board none of these rows is offered.
- **Status** and **Status → <Column>**, offered only where an issue is in
  view. See below.
- **Priority**, **Pre-approval** and a **Priority → <Level>** and
  **Pre-approval → <Level>** for each level, offered where an issue is in view
  and you are a person, not an API key. See below.

The console follows the tab's scope. In a tab scoped to projects, an issue
outside the scope is not listed for a key prefix or for words from its title.
Type its whole key (`FIL-1`, not `FIL-`) and it is listed first, marked
**[out of scope]**, because a whole key can only mean that issue.

### Setting an issue's status

With an issue in view, `>status` lists the board's columns, deferred ones
included, with the issue's own column marked **current**. Taking one moves the
issue there, exactly as the status bar on its page or the status picker in the
board's peek would. A bare `>` offers **Status** first, and every column is
also a command of its own, **Status → <Column>**, so `>done` and `>todo` go
straight there. **esc** in the column list goes back to `>`; a second **esc**
puts the console away.

The issue in view is, in this order:

1. the issue whose page you are on;
2. the card the board's peek is open on;
3. the card that had keyboard focus when you pressed `/` on the board. Tab to
   a card, press `/`, type `>todo` and **↵**, and the card moves and the
   **Undo** chicklet appears.

On any other page, or on a board with no card focused or peeked, there is no
issue in view and no Status row.

A column you cannot move the issue to is listed dimmed and marked **blocked**,
and the status line says why when you land on it: only an owner or approver may
move work into a protected column, and a column that merges the pull request
needs the issue to have one. **↵** on it does nothing. The current column says
**Already in <Column>**. These are the same reasons the status bar gives.

When the move closes or shelves an epic or story that has open work under it,
the console asks, in place of the dialog the page would open:
**Close its 3 open children too?** (**Shelve** for a deferred column), with
**Yes** and **No**. **Yes** moves them too and the page's progress shows it;
**No** and **esc** leave them where they are. A refusal for some of them is
said in the status line and the question stays, and **Yes** again is safe.
Either answer puts the console away.

A refusal from the server shows in the status line in its own words and the
console stays open. Two confirmations keep their own dialogs: a move into a
column whose work-in-progress limit is full, and a contribution moving into a
column that merges pull requests. The console closes and the dialog opens, as
it does from the status bar.

What opening a row does depends on the page. On the board the card is ringed
and peeked, or peeked alone when the board's filter is hiding it, and on the
board `/` also opens the console over a peek that is already open. On every
other page the row opens the issue's own page, with the tab's scope kept on
the address. Off the board the console reads the board once as it opens, so
its status line says **Loading…** until that lands; it does not poll.

### Setting an issue's priority and pre-approval

With an issue in view, `>priority` lists the six priority levels and
`>pre-approval` lists the three pre-approval levels, in the order the
controls on the issue's page and in the board's peek draw them. Taking one
sets it, the same call those controls make, and the page then reloads, so the
console and the mouse always agree. `>expedited` and **↵** on an issue page
sets it Expedited and the status bar's pill shows it; on the board with a
card peeked, `>paused` and **↵** sets it Paused, the card redraws, and the
peek's pill agrees, even if you set a level in the peek by hand a moment
before. Every level is also a command of its own, **Priority → <Level>** and
**Pre-approval → <Level>**, so `>expedited`, `>paused`, `>express` and `>none`
go straight there. **esc** in a level list goes back to `>`; a second **esc**
puts the console away.

The issue's own level is listed dimmed and marked **current**, and says
**Already <Level>**; **↵** on it does nothing. An inherited level is not the
issue's own, so it is not marked.

Setting **Pre-approved**, or lowering out of it, is for an owner or approver of
the issue's project. For anyone else those rows are dimmed and marked
**blocked**, and the status line gives the sentence the pre-approval control
gives: only an owner or approver of the project may set pre-approval. **↵** on
one does nothing.

Only a person is offered these rows. The controls draw a word instead of a
button for an API key, since the server refuses the write, and the console
follows. They are also absent where no issue is in view (see above).

A refusal from the server shows in the status line in its own words and the
console stays open. A set that works puts the console away.

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
  has no drop target of its own; the issue page's status bar and the peek's
  status picker are the way in, and the one drag that reaches it is the dream
  cloud. Both lists also draw a second press, **to a dream**, beside each
  Deferred column, which opens the same dialog as the cloud with that column
  chosen: a remand without a drag. A board with no Deferred column draws
  neither the column's press nor this one. While a card is held,
  and only when the board has a Deferred column, a cloud reading **Remand to a
  dream** is drawn above the columns. Dropping on it moves nothing: it opens a
  dialog naming the column, the open work under the card and the dream's first
  line, and nothing is written until **Remand** is pressed. That is why it is
  safe where a column drop is not — a drag can overshoot a lane, but a
  slipped drop on the cloud only asks a question, and **Leave it on the
  board**, escape or the scrim sends nothing. A remand cannot be undone: the
  chicklet that follows names the card and what went with it, links to the
  dream, and has no **Undo**. No
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
story's key or words from its title, the **Needs me** and **Blocked** switches and the Assignee filter
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

## The health strip

Above the columns, under the filter bar, a strip of five tiles shows in one look
whether work is moving, piling up, stuck or waiting on a person, and which
runners were up and doing what. On a phone it is one row of the five numbers
that opens into the tiles when tapped; see
[Hatch on your phone](hatch-on-your-phone).

The strip has one window control: **2h**, **4h**, **8h**, **12h** or **24h**.
It starts at 8h, and one choice sets every tile. Beside it a line says where the
window ends and how wide a bar is, as "to 15:40 · 20 min bars". Every
sparkline is cut into 24 bars on that one time axis, so the same bar is the
same moment on all four.

- **Moved** counts forward moves: the issues that went one column or more to
  the right. The big number is the window's count, and under it is the change
  from the window before, with an arrow: "16 vs prior 8h". More moves is green;
  fewer is amber, never red.
- **In flight** counts the issues in columns that hold work being done now. Its
  sparkline is three stacked bands: waiting, in flight and done, each read from
  the columns' flags rather than their names. The big number is the in-flight
  count, with the other two beside it. The tile's label opens the [cumulative
  flow](flow-wip-and-the-battery) report on the same window and the same projects;
  pressing the sparkline opens the dialog described below.
- **Oldest in flight** is the longest stay of an issue that has no children and
  is in flight, as a time such as `14h 20m`, or "none". Under it is how many
  in-flight cards are past normal for their column, in amber with a warning
  sign, or "none past normal". The warning sign and the "N past normal" line
  are pressable, and say where they lead, as "Open the 3 issues past normal".
  With exactly one issue past normal, pressing either opens that issue. With
  more, pressing either opens the Oldest in flight dialog with its list
  narrowed to the issues past normal and scrolled into view, and does not open
  the dialog a second time. "none past normal" is plain text, and so is the line on a marked bar,
  where it is a time. On a phone the sign and the line are at least 44px tall,
  so the tile's line grows a little to hold them. The line under that names the oldest issue by key
  and column, as "AER-12 · In Review", and is a link that opens it. Point at the
  number or at that line, or focus either, and a note shows the issue's key and
  title, how long it has stood in its column, and that column's normal.

  The sparkline is stacked bands of the in-flight issues by age, across the 24
  bars: **past normal** at the bottom, **aging** above it and **healthy** above
  that, in red, amber and green. An issue is healthy for under half of its
  column's normal, aging from half up to the normal, and past normal beyond it.
  Past normal sits on the baseline because it is the band that matters. A fourth,
  grey band tops the stack, and only when an issue stands in a column that has no
  normal yet. The bands are scaled to the tallest bar, and a bar with nothing in
  flight draws nothing. On a bar the number becomes the oldest stay then, the
  second line says the time, and the line under it says the split, as "2 past
  normal · 3 aging · 9 healthy". The split is also in the tile's spoken label,
  so no band is told by colour alone.
- **Waiting on you** counts pull requests and questions waiting on a person. The
  sparkline is steps. The big number is the count now, and under it is how long
  the oldest has waited. When nothing waits it takes the good tone, with a
  check mark that is said as "clear", and it says how long the board has been
  clear, as "clear for 3h 20m", or "clear for over 30d" when nothing has
  waited in as long as the read looks back. The line under it says how many
  moves were made in the window without a person, runners and the board
  together, as "47 moves without you". When no bar in the window had anything
  waiting, the sparkline is bars of those moves in the good colour instead of
  a flat line, and pointing at one says its count and its time. If something
  waited earlier in the window, the steps stay, so you can see when it
  cleared. When the read has no clear time to give, it says "nothing waiting".
- **Runners** draws one thin lane per runner that was up at any time in the
  window, at most eight, across the whole tile and without names: the hover
(and each cell's own tooltip) names each runner. Up now first, then most recently heard from, and a
  line under the lanes says how many are not drawn. The big number is how many
  are up of how many have a lane, with how many of those are paused underneath.
  Each cell in a lane is the most telling thing the runner did in that bar: a
  full bar is **moved a ticket** or **working**, told apart by shade; a
  dotted full bar is **let go or errored** - a stall, a drop or a failed
  session; a striped full bar is **out of usage or agent down**, from the
  state record; a short bar is **idle** (up, nothing it could take) or **off**
  (no session open); a hollow bar is **paused**, from the state record, worded
  "paused by the board" when the board itself imposed it. No cell is told by
  colour alone - every one is also a word in the hover, the key and the
  tile's own spoken label. Lanes ignore the project scope the rest of the
  board follows, but not the caller's own visibility: a runner's work in a
  project the caller cannot see reads as idle. A **Key** button at the tile's
  top right opens a legend naming all seven cells; press it, or hover or focus
  the tile. Press the lanes, or anywhere else on the tile that is not the Key,
  and its dialog opens, which draws every lane rather than eight.

Press the sparkline of **Moved**, **In flight**, **Oldest in flight**,
**Waiting on you** or **Runners**, or anywhere else on the tile that is not a link or a button,
and a dialog opens with that tile's window drawn large. On Runners, **Key** still
opens its own key, not the dialog. From the keyboard, focus
the sparkline and press Enter or Space; the arrow keys still mark a bar and do
not open it. Escape or a click outside closes the dialog and puts focus back on
the sparkline. Pointing at a tile only marks a moment, as before. Every dialog
is titled with the tile and its window, as "Moved, last 8h", and carries the
same window control: changing it there changes the strip's window too. Under it
is the chart on the same 24 bars, with times along the bottom and the scale's
top value at the side, then a legend naming every mark, then one sentence for
the marked bar - point at a bar, or step with the arrow keys, and it says that
bar's figures in words. The marks are told apart by pattern as well as shade.
The dialog draws from the same read as the strip and redraws as it refreshes.

- **Moved** stacks each bar by who moved: runners at the bottom, then a person,
  then the board. Under the chart are each one's total for the window and the
  window's total, with the total for the window before. The read has no
  per-source figure for that window, so it is one number.
- **In flight** draws the three bands, with each band's count at the marked bar
  (the last bar, now, at rest), and a link, "Open the cumulative flow report",
  to the report on the same window and projects.
- **Oldest in flight** draws the tile's age bands large: past normal at the
  bottom, then aging and healthy, and a grey band on top only when an issue
  stands in a column with no normal yet. Each band is a different pattern as
  well as a different shade, and the legend names every band drawn. The side
  scale is a count of issues, and a bar's sentence gives the oldest stay and the
  split by age.

  Under the legend is a list of every in-flight issue that has no children,
  longest stay first. Each row is one link that opens the issue, and shows its
  key and title, its column, and its time in that column. The time is also drawn
  as a bar against the column's normal. Every row shares one scale, and a mark on
  each bar shows where the normal falls, so an issue at twice its normal reads
  that way at a glance. A row whose column has no normal yet draws no bar and
  says "no normal yet". A row past normal carries the warning sign and the words
  "past normal"; the others say "aging" or "healthy". A two-way control above the
  list, **All** and **Past normal**, switches between every issue and those past
  normal alone, each side with its count. The past normal count is the tile's own,
  because both come from one answer. A list longer than the dialog scrolls inside
  it, and the dialog keeps its size.
- **Waiting on you** draws pull requests and questions apart, as two stacked
  areas, and says how long the oldest has waited now. When nothing waits it
  says instead how long the board has been clear and since when, as a date and
  time ("since at least" when that is the read's own limit), the window's moves
  by runners, by the board and by a person, the share made without a person as
  "47 of 49", and how many issues were done in the window.
- **Runners** draws every lane, not eight: one row for each runner the read
  answers, with its name, whether it is up, paused or not up now, and when it
  was last heard from ("last heard 3h 10m before 15:40"), then its 24 cells,
  larger than the tile's, each with its own tooltip. The legend's seven lines show
  in the dialog with no button press, each beside a swatch of its cell. A side
  note says how many lanes there are. Point at a bar, or step with the arrow
  keys, and a line marks it across every row and one sentence names each runner's
  state there. With many lanes the rows scroll inside a box while the time axis
  stays in view; Up and Down scroll it when it has focus.

At rest the last three tiles show now, the bar still filling, Moved shows
the window's total, and Runners shows how many are up now. Point at any bar,
on any tile, and every tile marks that same moment: each number becomes its
value then, and the line under it says the time. On Moved the hovered bar's
count is split by who moved: a runner, a person, or the board. On Runners a
popover under the tile names every drawn runner's state at that moment. Move
off and the window's own numbers come back. From the keyboard, Tab to a
sparkline and use the left and right arrows to step a bar, Home and End to
jump, and Escape to clear. Every tile also reads sensibly from its text
alone, with its arrow or warning said in words.

Colour follows meaning, not direction, and no tile says anything by colour
alone. Light and dark each have their own values.

The strip refreshes with the board, every 30 seconds. A read that fails leaves
the last answer drawn; it never blocks the board or puts an error over it.
A board with no history shows no strip at all, with no placeholder and no error.

The button at the right of the filter bar, beside the flow view, hides and
shows the strip; on a phone it is inside **Filters** instead. While it is hidden nothing is read. Both choices, shown or
hidden and the window, are remembered per browser, under `hatch.board.strip.shown`
and `hatch.board.strip.window` in its local storage. A browser whose storage
cannot be read opens with the strip shown at 8h.

The strip follows the tab's [project scope](#scoping-a-tab-to-projects), as the
cumulative flow report does. It does not follow the filter bar: narrowing the
cards by assignee or type leaves the numbers alone.

The bars are rebuilt from the status trail, and a few early ones are
approximate:

- A check approving a merge, and a pull request held back by a conflict or a
  failed build, count as waiting on a person in an earlier bar. Only the
  attention panel can tell the difference today.
- Pre-approval is walked up the parents an issue has now, and ignores the
  project line a level may not cross.
- A question that went to the merge level, or one awaiting its look back, ends
  at a terminal column in an earlier bar yet counts now.
- Whether an issue has children is today's answer, not the answer then.
- A parent in a project you cannot see adds no level to an earlier bar.

The last bar is exact: it is the attention control's own count.

## Cards show their age

A card that has sat in its column longer than is normal for that column wears a
chip and a faint tint. The chip is a clock and the time in the column, such as
`14h 20m`; the tint is a pale indigo over the whole card, with no change to its
border. The chip carries the words, so the mark never rests on colour alone.
Point at the chip for the sentence: "In In Progress for 14h 20m. Normal here is
under 6h (p90, last 30 days)." It looks the same on the desk and on the phone,
in the light and the dark theme, and stays readable under a flow band when the
flow view is on.

Only a column in the middle of the board ages its cards. The first column does
not, a terminal column does not, and a deferred column does not: a card waiting
in the inbox, or finished, or set aside, is not late.

Normal is the 90th percentile of the stays that finished in that column over
the last 30 days, taken over work with no children. A column shows it once it
has ten such stays; before that its cards show nothing. A card is aged when its
stay so far is longer than that.

A parent waiting on its children shows nothing, since the time it sits in a
column is the time its children take.

A card that is also waiting on an answer keeps its own warm wash and border and
still shows its chip. A card moved by hand loses its chip until the board is
read again, which happens on the next poll or action.

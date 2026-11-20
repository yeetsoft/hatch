---
title: Issues
section: Concepts
order: 20
summary: What hangs under what, and what the board does with each field on a ticket.
---

A ticket crosses the columns [the lifecycle of a ticket](the-lifecycle-of-a-ticket) describes. This
page is about the fields that travel with it along the way.

## Types, and what may hang under what

Six types: ambition, epic, reflection, story, task, bug. On the issue page the type is a pill in the Details card's edit mode; pressing it opens the six types under it, each as its tile and its word. The tile is a coloured outline with a picture on it, a rounded square for ambition, epic, story and task and a disc for reflection and bug, so the type reads without the colour. The ambition wears the Hatch egg on the brand gradient, where the other five wear a picture on a colour of their own. A list that mixes columns - an issue's children, a search, the bulk page - draws the tile as an outline before work starts, solid while it is under way, and with a small tick once it has landed; the board keeps it solid, since its column already says so. The type badge, the pill and every step wear the page's own plain face, and only the tile carries the colour. Picking one sets it and folds the band, as the Priority and Pre-approval sections say of theirs. The New issue dialog's Type field is the same pill. The Progress card's composer, where a child is filed under an ambition, epic, reflection, story or bug, picks the child's type the same way, and its band offers only what that parent may take: under an ambition it offers `epic` alone. Any type but an ambition may have no parent at all —
the ordinary state of a freshly captured bug, or an epic with nothing broken
out of it yet. Where one does have a parent, the model allows exactly this:
an epic hangs under an epic, a reflection or an ambition, a story hangs under an epic, a task hangs under
a story, a bug or an epic, and a bug hangs under an epic or a story.

An ambition is the largest aim, broken out into epics. It takes no parent, so the issue page and the New issue dialog draw no Parent field for one, and it takes only epics: an epic's Parent picker lists ambitions beside epics and reflections. An epic of epics becomes an ambition by retyping it on its page. The server refuses the retype while the issue has a parent, or a child that is not an epic, and it refuses retyping an epic under an ambition to anything but `epic`. The Plan page draws every epic and ambition, an ambition with its tile and its epics beneath it.

That is wider than the planning habit of an epic taking stories and a story
taking tasks. The habit is a way of breaking work down, not a rule the model
enforces — a task may hang directly under an epic, and a bug under either an
epic or a story.

## Capabilities

A capability is one thing an ambition says has to be true, written as a
statement and a line saying how anyone would tell. An ambition holds an
ordered list of them. Each is a row of its own, not text in the description, so
it can be counted and, later, judged.

Only an ambition takes capabilities. Any other type refuses with a sentence
naming it. Both lines are one line each, up to 300 characters, and neither may
be blank. Adding one appends it, or puts it at the position given and moves the
rest down. Editing changes either line, reordering names every capability in
the wanted order, and removing one closes the gap. The positions always run 1 to
n.

A person can change an ambition's capabilities anywhere on the board. A session
can read them anywhere, but can write them only while the ambition is left of
the implementation column. Once the ambition is in that column, past it, done
or deferred, a session is refused: what an ambition asks for is settled before
the work starts, and changing it after is a person's call.

Each change shows on the ambition's History as its own line: added, edited,
moved or removed. An ambition that holds capabilities cannot be retyped to
anything else until they are removed, and deleting the ambition deletes them.

A session records whether a capability holds, with what was looked at and a
sha, as one of three words: holds, does not hold, or cannot tell. A capability
carries every verdict it has ever been given, and its state - what anyone
reading the ambition sees - is always the newest one, or "not checked" with
none. A key may record a verdict in any column, including past the
implementation line that gates the rest of a capability's row: settling what
an ambition asks for is a person's call, but checking whether it holds is not.
Each verdict shows on the ambition's History too. This is a different thing
from a check session's pass or fail against a live claim, which the board
still calls a verdict elsewhere - the word is already taken, so a capability's
own is never called that on its own.

An epic may serve some of an ambition's capabilities. A person or a session
links an epic to the capabilities it serves, and only to those of the ambition
the epic hangs beneath, however many epics and reflections lie between. The
link reads from both ends: the epic lists what it serves, and each capability
lists the epics that serve it. A session may change the links in any column.
An epic cannot be moved out from under an ambition while it still serves one of
its capabilities; unlink it first. Each change shows on the epic's History as
"serves changed". Deleting the epic or the capability removes the link and
nothing else.

Both show on the issue page, read-only. An ambition's page draws a Capabilities
section above Progress: one row per capability, in order, with its statement and
how to tell, its state as one of "Holds", "Does not hold", "Cannot tell" or "Not
checked", what the newest verdict looked at, its sha, its author and its date, and the
epics that serve it. An ambition with none says so and shows the `hatch capability add`
command to start one. An epic's page draws a Serves section naming each
capability it serves, with the ambition it belongs to and that capability's
state. The section is left out when the epic serves nothing, and no other type
draws either one.

An ambition's page also draws Epics in flight, between Capabilities and
Progress. It lists each direct child in a column that is neither terminal nor
deferred: the key as a link, the title, the column's name, and the capabilities
the epic serves as numbered badges. One line says how many are open and how many
the ambition runs at once, in the shape "2 open, runs 2 at once"; an ambition
with no limit set runs one. With no open epic the section says so. On a phone the
column and badges stack under the title. No other type draws it.

## Passes

A pass is a session on an ambition standing in the implementation column, aimed
at that column itself rather than the next one. A person starts the first one
by moving the ambition into the implementation column; after that, a pass runs
again on its own whenever the ambition has room.

It reads each capability against the trunk and records a verdict — holds, does
not hold, cannot tell — with what it looked at and a sha, the same way any
session records one; see [Capabilities](#capabilities) above.

It carves at most as many epics under the ambition as its own limit allows
(`wipLimit`, read as one where unset), less however many are already open,
each epic saying which capabilities it serves, and leaves the ambition
standing where it is — an ambition is never carried on by a pass, only by the
operator.

An ambition's issue page has a Passes section, after Epics in flight, reading
`GET /issues/{key}/passes` on the same tolerant terms as Capabilities: a
failure is a sentence in the section, not the page's error. Every pass is an
entry, newest first, headed by its work-log title (or the word Pass, where
the row has none), its date and the sha it judged against. Each capability it
judged is a line with the position, the state word and, where that word
changed from the pass before, a mark saying so. A pass whose verdicts could
not be recorded as data says that instead of a line; a verdict recorded with
no pass around it — no work-log row at all — shows as its own entry, saying
it was recorded outside a pass. Every entry's own comments follow, rendered
the way the Comments section renders a body. The verdict lines sit in a grid
on a desktop and stack one per line on a phone.

## How a ticket reads

A ticket is read by a person first. These rules say how it should read.

- A title is a handful of words. Name the thing, not the ticket about the thing. Five words is plenty; ten is too many.
- An epic opens with a summary: two or three short paragraphs, before any heading.
- A story opens with one sentence: who gets what once it lands.
- A task may be technical, but its title still reads as a sentence.
- Sentences are short and say who does what. No stacked clauses. No coined names for things the repository already names. Spell out an acronym once.
- A comment leads with the outcome; the sha and branch follow.
- A work-log title says what the session did, like a commit subject.

A title that passes: `Tickets in a human voice`. It names the thing, in four words.

A title that does not: `Weave: more human voice`. `Weave` is a coined name, and the title says nothing a reader could picture.

A story summary that passes: `A person reading a ticket can tell in one sentence what they will get.`

A story summary that does not: `Weave unifies the title, summary and comment surfaces under a shared voice contract, so that every ticket, however filed, reads alike.` It names a coined thing, and the clauses stack.

The API asks for these rules and never enforces them. A ticket that breaks one is still a ticket.

## Keys and numbering

The key shown everywhere, `AER-12`, is **computed** — the project's own key
and the issue's number, joined by a dash — and never stored, so there is
exactly one fact about a key anywhere and no chance of two copies
disagreeing after a rename. The number is minted from the project's own
counter, never from counting what is already there: a deleted issue's
number is never handed to the next one, so an old link becomes a dead link,
never a different ticket. Moving an issue to another project mints it a new
number there, and its old key goes dead the same way. Its parent and, by
default, its children keep their links to it across the move.

## Parent and children

Parentage is optional, and checked on every write: the parent has to exist,
be a legal type for this one (above), and form no cycle. It may stand in
another project. The one exception is a task under a story or bug that shares
[one pull request](#one-pull-request): the task has to stay in that story's
project, since one pull request is one branch in one repository, and the
refusal names the story. The web picker - the issue page's Details card and
the New issue dialog - lists candidates from every project you can see, the
issue's own project first. Moving an issue to another project carries only the
children that are in its own project, and the parent stays: the moved issue
keeps its own parent, and a child in another project, or one left behind with
`moveDescendants: false`, stays where it is and keeps its link to the moved
parent. A parent's own progress is read from its children, not from its own
column — the totals count **leaves**, issues with no children of their own,
so a story standing in review with every task still in To Do reads as To Do,
because the tasks are the work and not the story's own column.

Where a parent is in progress but its pull is not honoured across a project
line, the Parent field, the board card's parent row and the peek say so in one
sentence: "AER-12 is in progress, but its pull is not honoured in OPS". When
the parent is outside your scope the sentence reads "from an issue outside your
scope, not honoured in OPS", and names nothing more.

## Filed from

An issue also records which issue's session filed it, and the issue it was
filed from lists what was filed from it. That is provenance and nothing more:
it holds nothing, orders nothing and blocks nothing, and an issue filed from
one that is still open is picked up exactly as any other. It can point at an
issue in another project, and it clears itself if the source is deleted. It is
set when an issue is filed, or changed later with an edit, and every change
leaves a line in the issue's history. The rework task filed when a person
[overturns a look-back question](questions-and-decisions#looking-back) is filed
from the issue the question was on.

The field sits on the issue page too, in the Details card's edit view, right
after Parent. What it holds is read on the same page in its own section,
alongside the issues filed from this one — omitted when there is neither a
source nor anything filed.

**Plan** draws its own meter from this same rollup, one row per epic in the
house. An epic under an epic of another project is drawn inside it, with its
project key beside its own. Narrowed to one project, an epic whose parent is
elsewhere is drawn as a root of its own, with a line saying what it is part
of, or only that it is part of work outside your scope. Where a reader's
scope hides some of an epic's tree, the total is over what they can see and
a line says the tree goes on. In a tab scoped to projects its own Project picker follows the same rule
as the board's: see
[Scoping a tab to projects](the-board-and-its-columns#scoping-a-tab-to-projects).

## The changelog task

When a top-level issue closes in a project with a repository that keeps a
changelog file, the board files a task under it, `Record <KEY> in the
changelog`, and the closed issue's history shows a `changelog_task_filed` event
naming the task. The task never appears in the file itself. See
[the lifecycle of a ticket](the-lifecycle-of-a-ticket#the-changelog-task).

## Rank and ordering

A card's place in its column is a rank picked by the server, never the
client. A drop says only *before this card* or *after this card*, and the
number in between is computed on the board's own side, so a browser and a
script can never disagree about where a card landed. Dropping at the very
top or bottom, or between two cards with no room left between their ranks,
is handled the same way — the server either reaches past the end or
renumbers the column first, without the client ever seeing a rank.

Within a column, the dispatcher reads cards in rank order, so dragging a card
up in its column is what gets it worked sooner. The float that reads a card's
own level ahead of rank — see [Priority](#priority) — carries its project's
level too, in the order that section already names.

## Ready and due dates

A pair with different jobs. `ready` is a gate: an issue whose ready date has
not arrived yet is folded off the board entirely, which lets a ticket be
filed the moment it is thought of rather than the moment it can start — buy
a certificate in September, and the renewal appears on the board next August
on its own. `due` is a deadline, drawn as a chip that warms starting three
days out; a past due date is accepted without comment, since recording that
something was owed last Tuesday is half of what a tracker is for. Both are
optional, and both are written as either a bare date or an instant.

## Priority

Six levels, lowest to highest: paused, economy, low, normal, expedited,
emergency. `emergency` goes to the top of the walk, before anything else;
`expedited` goes first, after emergency; `normal` is the default, and a
ticket at it says nothing about its priority at all; `low` is reached only
after every normal ticket, and only while an unattended pass is on pace;
`economy` is worked only when an unattended pass finds usage that would
otherwise go spare; `paused` is set aside, and nothing picks it up again
until a person sets it back.

For every level but the bottom three it is purely a sort key, not a gate —
an emergency ticket that is blocked is still blocked. Paused, economy and
low are gates too: paused always, economy and low only for an unattended
pass, since naming the ticket by hand gets it regardless. A level is
inherited live from the nearest ancestor that sets one, so raising an epic's
level is a single press that reaches every story and task under it that has
not set its own — read back as, for instance, "expedited (inherited from
AER-12)". Raising a ticket's level this way is what the house calls
*expediting* it. A level set on a parent in another project, and stopped at
the line, reads "expedited on AER-12, not honoured in OPS" beside the pill; one
from an issue outside your scope reads "expedited from an issue outside your
scope", with "not honoured" added where it was stopped. Both are said on a
board card too, in the tile's hover text or on a dashed mark.

On a board card the level reads off the type's tile and the card's left edge.
Emergency is a flame behind the tile and an amber stripe; expedited is a
smaller ember and a blue stripe; economy is a faded tile and a grey stripe;
low is a `↓` in the row beneath and a grey stripe; paused is a `⏸` there and
a faded card. Red on a card means a bug and nothing else, so emergency is
amber, not red. Hovering the tile gives the type, then what its priority and
pre-approval say, in words.

A project has a level too: paused, normal or expedited. An expedited project's
clear work is picked up before any other project's, whatever level its tickets
sit at, though an emergency ticket in any project still goes first; the other
projects' clear tickets read as parked, naming it. A paused project is set
aside whole: nothing in it is picked up, an emergency ticket included, and its
tickets read "project AER is paused". A runner that cannot work the expedited
project is handed the next project's work instead.

The level is a pill in the issue page's status band, beside Status and
Pre-approval, and the same pill sits in the board's peek. Pressing it opens
the six levels under the row; picking one sets it and folds the band. A
key-holder sees the word, with where it was inherited from, and nothing to
press.

## Dependencies

An edge, shown as `depends` on the ticket that waits and `blocks` on the one
waited on, gates two things and two only: being picked up and written — the
move into [the column where code gets written](the-board-and-its-columns) —
and a parent pulling a child into that same column while the parent is
already there. Everything left of that still moves: a ticket waiting on
another still goes through breakdown, still lands in backlog, and is still
analysed. An edge clears only once the issue it names reaches a terminal
column, merged rather than merely in review, and a dependency an ancestor
holds reaches everything below it — a task under a blocked story is blocked
too.

## One pull request

Whether a story's tasks share the story's own branch and pull request,
instead of a branch and pull request each, merged one after another. A
project holds a default, off unless an owner turns it on, and a story may
override it — on, off, or back to inherit — through `hatch one-pr`. The
setting locks once any task under the story has started, since work begun on
one branch cannot be moved onto the other. A project that enforces its default
takes no override at all: `on` and `off` are refused there, and only `inherit`
is accepted. Moving a story or bug into such a project returns an override with
nothing started under it to inherit; one with started work keeps its override.

The project's own policy is set on **Manage → Projects**, **Edit**, **Landing**
— an owner's alone, beside the project's one-pull-request default — between
*Stories may set their own, either way* (the default) and *Every story follows
the project*. Turning on the latter returns every override that exists on a
story or bug with nothing started under it to the project's default; one with
started work keeps its override. Anyone who is not an owner sees the choice
read out, but cannot change it.

Under such a project, the issue page draws the three steps disabled, with the
reason — that the project enforces its default, and that an owner can make it
optional on the Projects page — as their title. The line beneath still says
what is in effect: `On · the project's default, enforced` or
`Off · the project's default, enforced` for a story or bug with nothing of its
own to keep; `On · set on this story before the project enforced its default;
locked` or the `Off` and `bug` forms of it for one whose override predates the
policy and was kept because its work had started. A task's line is unchanged
by any of this — `inherited from` the story it is under, whatever the story's
own line reads. The New Issue form, which has no story yet to lock, shows one
muted line in its place instead of the steps, saying that the chosen project
enforces its own default, and files no setting of its own for the one-PR field.

## Pre-approval

Three levels: none, express, preapproved — read live from the nearest
ancestor that sets one, the same way priority is. `express` carries a ticket
past a column ticked [Express skips](the-board-and-its-columns), with no
session, as long as it has no [unanswered question](questions-and-decisions).
`preapproved` carries it further: once its pull request is green and
current, or once every child is done, the board carries it on by itself,
with nobody reading the diff first — a move [the lifecycle of a
ticket](the-lifecycle-of-a-ticket) calls the hop.

On a board card the level reads off the type's tile: express is one line
behind it, and pre-approved is three lines behind it and a tile that leans
forward. A card that is both pre-approved and emergency streams its flame back
off the leaning tile.

The level is a pill in the issue page's status band, beside Status and
Priority, and the same pill sits in the board's peek. Pressing it opens the
three levels under the row. Only an owner or approver of the project may set
or lower `preapproved`, and the band says so on the steps it locks. A
key-holder sees the word, with where it was inherited from, and nothing to
press. A level that was not honoured reads beside the pill, "pre-approved on
AER-12, not honoured in OPS", and one from an issue outside your scope reads
"express from an issue outside your scope". The remedy is the same band: an
approver sets the level on the issue itself.

Inside one project a level reaches every descendant, whoever set it. From a
parent in another project it reaches a descendant only while the person who set
it could set the same level on that descendant themselves: an owner or approver
of its project for `preapproved`, someone who may move work into the column
that feeds implementation for `express`, and a live person for a priority, with
the project in that person's scope. Take their role away and the level stops
reaching the descendant on the next read. A level with nobody recorded as its
setter, as every level set before setters were recorded, stays inside its own
project. Where a level did not reach, the ticket's `preApprovalPassedOver` and
`priorityPassedOver` name the ancestor that carries it, and the queue says
"pre-approved on AER-1, not honoured in OPS" where it would otherwise have
moved the ticket on.

A running parent pulls a child in another project the same way: only while the
person who started the parent (`startedBy`) could have moved that child into the
next column themselves - a live person with the child's project in scope, and an
owner or approver where the column is protected. Inside one project nothing
changes. Where the pull is not honoured, the child's `pullPassedOver` names the
parent, and the queue says "OPS-1 is in progress, but the person who started it
has no say in AER, so a person moves it". A parent started by nobody pulls
nothing across a line.

## Local model

Three levels: none, prefer, require — read live from the nearest ancestor
that sets one, the same way priority is. `none` runs on the cloud model, as
every ticket always has. `prefer` uses a local model where one can do the
work and falls back to the cloud model where none can. `require` runs only
on a local model and never falls back quietly. The level reaches everything
beneath the ticket that sets it, so an epic set to `prefer` has every story
and task under it preferring too, until one of them sets its own.

The level is a pill in the issue page's status band, after Pre-approval.
Pressing it opens the three levels under the row, and a press saves. The
Details card names the level and the ticket it was inherited from, once it
is not none, and a board card at `prefer` or `require` wears a small mark.
Any person may set it. A key-holder sees the word, with where it was
inherited from, and nothing to press.

At `require`, every move under the ticket whose playbook row names a local
model runs on that model, and only on a runner that lists it; the move waits
until one does, and the ticket stalls as any other would if that model cannot
finish it. A row with no local model runs as it does today, and the issue's own
model override still wins. See [the local model](playbooks#the-local-model) on
a playbook row.

At `prefer`, a move whose row names a local model runs on it when a local
runner is free to take the move, and on the row's ordinary model when none is.
A local runner is free when it is live, runs the `ollama` agent, is running
rather than paused, holds no ticket, lists the model, and nothing on the ticket
rules it out - its tags, an omitted runner, a refused model or a missing
`has`. A runner that is working or paused is not free, so the cloud never waits
on a GPU. While one is free, a cloud runner is told the ticket is left for it,
and `hatch queue` prints the sentence. The issue's own model override still
wins, and a row with no local model runs as it does today. The work answer
says which model the session runs on, and `modelFellBack` where it is the
ordinary one.

## Human work

A three-state override saying who works a ticket: `inherit` (the default),
`human` or `agent`. The live value is read at request time from the nearest
ticket — itself first, then each ancestor — whose own value is not `inherit`;
with nothing set anywhere it is `agent`. Nothing is copied, so moving a ticket
to a new parent changes the live value at once. Unlike priority and dangerous,
the walk stops at an explicit `agent`, not only at an explicit `human`, so a
child can opt back out from under a parent a person has claimed.

A `human` ticket — its own value or the nearest ancestor's — is refused by
`work/next`, `work/queue`, `work/{key}` and a hop with a kind sentence: "human
work - a person does this one, and no runner picks it up", or "human work
from `HA-12` - ..." when the value is inherited. Moving it by hand, on the
board or with `hatch move`, is unaffected — the override is dispatch's own
reading, not a lock on the column.

Set with `PUT /issues/{key}/human-work`, `{ "value": "human"|"agent"|"inherit" }` -
a person's call, never a key's, the same reach as Local model. Setting the
value a ticket already holds writes nothing. The issue page shows the live
value, its own value and the ticket it was inherited from, beside Local
model; a board card marked human carries the same source.

## Finished with no code

A one-line reason, at most 200 characters and trimmed, saying that a ticket
finished with no code: an investigation, say. `hatch show` prints it as
`no code:`, and the issue page's facts panel shows a `No code` row reading
`Finished with no code: <reason>`. A childless ticket wears the 😎 glyph on
its board card and in the peek, in whatever column it stands in, with the
reason in the glyph's title. It goes when the declaration is cleared or set
aside.

A ticket with a child never wears the glyph, whatever it declared and
whenever: a story, a bug, or an epic is carried by its children's own code,
so a declaration on it says nothing, and the card answer follows the
children as they stand. The declaration itself is unchanged — it still shows
on the issue page and in `hatch show`, and still stands in review for the
close hop and the attention panel.

A session sets it with `hatch pr KEY --none "reason"`. A person sets it from
the issue page: the edit view's **Finished with no code** field, with **Save**
and **Clear**. Both call `PUT /api/issues/{key}/no-code`, and the
history shows a `no_code_changed` event whose actor is whoever made the call.

The route refuses while the ticket has a pull request recorded, or a branch on
origin in a repository its project counts: it answers 409 with a sentence
naming the ticket and what it has, and `hatch pr --none` exits `1` with the
same sentence. A reason over 200 characters is a 400.

Clear it on the issue page, or send `""` to the route; a whitespace-only
reason also clears, and an absent reason is a 400, so clearing is always said.
Moving the ticket into a column left of review clears it too, and writes a
`no_code_changed` event naming the column. A terminal column or a deferred one
does not clear it. A session claiming the
ticket while it stands left of review clears it as well, and so does that
session's claim ending, whether it let go or an operator cleared it: the
declaration belongs to review, and a ticket that is being worked again is not
finished with no code. Each writes a `no_code_changed` event naming the column
it stood in (`standing`). A ticket in review or to its right, in a terminal
column or in a deferred one keeps it through a claim. Left alone, a stale
declaration would reach review and wait there for a person to close it. A branch turning up on origin, or a pull request being
recorded, sets it aside instead, and writes a `no_code_set_aside` event
carrying the old reason.

A declared ticket in review waits for a person to close it. It closes with no
session when it is pre-approved or its project has **A no-code declaration
closes the ticket** on, and only once any children are done. The project's *A
passing check approves the merge* setting does not carry it, since it is not a
person's word that nothing was built. [The lifecycle of a
ticket](the-lifecycle-of-a-ticket) and [the board and its
columns](the-board-and-its-columns#who-moves-work) already say this.

## Contribution

An issue can say it came from outside: a pull request somebody else opened, or
the mirror of an issue on another Hatch. That is its contribution record, one
per issue, and it is written by a runner through its own route, never by editing
the issue. It holds the pull request URL, the head branch, the author's login
and the forge's own word for how they relate to the repository (`CONTRIBUTOR`,
`NONE` and so on, kept as given), where it originated (the other Hatch's
address, project key and issue key, if there is one), and when and by which
runner it was filed. A mirrored ancestor has an origin and no pull request, so
its URL and branch are empty.

The record is write-once. Writing it again with the same pull request changes
nothing, and a different one is refused. One pull request is one issue: a URL
another issue's record already holds is refused, naming that issue. Where the
issue has no pull request of its own, the record's becomes it; where it has a
different one, the write is refused and nothing is written. The first write adds
a `contribution_filed` event carrying the URL, branch, author and origin.

How a contribution is filed, approved at the epic level and merged is told in order on [the review page](review-pull-requests-and-the-merge-train#contributions); `docs/hatch.md` carries the record and the envelope under "Contribution", and the approval path under "The approval column".

### The mirrored tree

A pull request a Hatch instance opens for its own issue carries that issue's
whole chain in its body, own ticket first and root last. The outside filer
reads it before filing: for each ancestor, root first, it looks for an issue
this project already mirrored from the same instance and key
(`originInstance`/`originKey`) and leaves it exactly as it is where it finds
one, or else creates a real issue of the ancestor's own type, titled and
described from it (`Mirrored from <instance> <key>`, then the ancestor's own
description), gives it a contribution record naming the origin with no pull
request, and moves it into review - so a person reads and approves the epic
or the story, not the diff. The pull request's own ticket hangs under the
nearest of these, and carries the pull request itself on its own record.

Mirroring never refuses a pairing: `reflection` folds to `epic` since a
reflection is a board's own cadence artefact and has no standing as anyone
else's ancestor, and a type this board does not know folds to `task`. Where
the type a chain gives still cannot legally hang under its natural parent
here, the issue hangs under the nearest ancestor it can instead, and its
description says so. A pull request whose body carries none of this - no
envelope, or one that cannot be read - is filed as a plain, parentless
contribution, as it always was; a reader that found an envelope but could not
make sense of it says why in the record's own note instead of failing the
filing.

`hatch show` prints a `contribution:` line for an issue with a record, the issue
and its board card carry it as `contribution`, and `GET /issues` can ask whether
a pull request or an originating key is already here with `pullRequestUrl`, or
`originInstance` and `originKey`.

### How it reads on the board and the issue page

An issue with a record wears a **contribution mark** on its board card and in
the issue page's header: an arrow pointing in at a bracket, drawn as an open
stroke, so it still reads in a greyscale render. It sits beside the type glyph
and never replaces it, so a mirrored epic is an epic and a contribution at once,
on any type. A card with no record wears nothing new.

Beside the mark is the **origin badge**: the originating key and the instance's
host where the record has them (`AER-12 · hatch.example.org`), the key alone
where it has no instance, otherwise `@login`. Where the record's instance is an
`http` or `https` address the host is a link to it; any other value is shown as
text. The link is only an address, and Hatch never fetches it. The record keeps
no origin issue URL, so the link goes to the instance and not to the issue.

The issue page's **Origin** section lists the issue's mirrored ancestors, root
first, each with its key, type, title and origin badge, read from the parents'
own records. It is left out where no ancestor has a record, and an ancestor past
the board's done horizon is not listed.

A move of a contribution into a column that merges pull requests asks first, in
the browser: dropping its card, pressing that column on the peek's status
picker, or pressing it on the issue page's status bar shows a confirm naming the
origin and saying that merging it merges someone else's branch. **Leave it**
sends nothing; **Move it** moves it. Any other move, and any move of an issue
with no record, asks nothing new. [`hatch move`](cli-reference#move) refuses the
same move unless it is given `--contribution`. The Bulk edit page does not ask.

## Runner tags

A ticket's own say over which runner may work it — a project may
require tags too, the same shape. What a runner is actually held to is the
union of all three: the project's own, every ancestor's own, and the
ticket's own, rather than whichever is nearest. By default nothing is
required anywhere, so most tickets say nothing about tags at all; a runner
that advertises none of what is required is skipped, the same way a
dependency or a hold would be.

The field sits on the issue page, in the Details card's edit view, right
after Assignee, and opening it takes nothing beyond being able to open
the ticket at all — the same as its title and description. Every tag it
inherits is read out underneath, one sentence per source in the order the
union itself is built: the project first, then each ancestor in turn, naming
where each one came from.

## Dangerous

A one-line reason, at most 200 characters, saying why running a ticket's work
could destabilise a machine: security testing, performance or load testing,
or crashing or hanging processes on purpose. It is read live from the
ticket's own row, else the nearest flagged ancestor's — down the subtree and
never up — and the issue page names the ancestor it came from when the reason
is not the ticket's own.

It is not a tag: every runner takes flagged work unless its own operator has
turned it away (see [the runner](the-runner#what-a-runner-advertises)), so no
board stalls on a ticket for carrying the flag. The field sits on the issue
page, in the Details card's edit view, right after Runner tags; emptying it
clears the flag.

## Omitted runners

A runner named against a ticket that is then never handed that ticket. Each
omission carries the runner's name, an optional reason of at most 200
characters, who recorded it and when. A runner can recuse itself from a
ticket it ought not to take, and a person can omit one from the issue page.

It is inherited by everything under the ticket, a union down the tree: a
child cannot widen what its parent said. An omission a ticket only inherits is
therefore lifted on the ancestor that holds it, never on the child, and the
issue page names that ancestor beside each inherited name.

An omission stays until somebody lifts it; moving the ticket between columns
does not remove it. A name that is no longer a live runner (see [the
runner](the-runner#the-runners-name)) stays listed, marked *not a runner on
this board*, so a runner that comes back under the same name finds the
omission still standing.

The list sits on the issue page, in the Details card's edit view, right after
Dangerous: every omission the ticket carries, with **Lift** on each of its
own, and below it **Omit a runner** — a picker over the runners you can see
that also takes a typed name, a reason with a 200-character counter, and a
button. A refusal appears beside the field and what you typed stays. The
read-only card and the peek show the same list as a fact. Opening it takes
nothing beyond being able to open the ticket at all, the same as Runner tags
and Dangerous.

## Held

A person's own mark, set from the ticket's **Hold for me** button and read
back as **Held** — never a key's, and never set by a session. A held ticket
is folded out of dispatch unconditionally, and stays folded until a person
clears it. That is different from a stall, which a session leaves behind and
which the board resumes on its own after a wait — see [the lifecycle of a
ticket](the-lifecycle-of-a-ticket).

## Deferring an issue

Reached from the ticket's own status bar, or from the board's peek — never
from a drag onto a column, since a drag can overshoot a lane and work should
not be shelved by accident. The one drag that reaches the shelf is the dream
cloud, a door that asks first: dropping a card on it moves nothing until the
dialog it opens is answered, and answering it remands the card, and the open
work under it, to a dream. Deferring a ticket with open work under it shelves that
work too, in one step, the same way closing a subtree does below.

Each deferred column in the status bar and in the peek's status picker has a
second press beside it, **to a dream**. It opens the same dialog the cloud does,
with that column chosen, and the same **Remand** confirms it. The issue page
reloads and shows where the dream went. A press the move into that column
would refuse, for a protected column, is refused here too.

Two things follow: a [deferred](the-board-and-its-columns) ticket does not
clear a dependency — only work that lands does — so everything waiting on it
gets a comment saying the gate still holds; and a deferred ticket drops out
of its parent's rollup entirely, counted neither done nor outstanding.

## Closing a subtree

Moving a ticket with open work under it into a terminal column asks a
question naming exactly that work, rather than closing it silently — the
single biggest way a board used to disagree with reality, an epic reading
"shipped" while eleven tasks sat open underneath it. Taking the cascade
lands every one of them in whichever [Done column](the-board-and-its-columns)
the parent itself landed in; a ticket that has already stopped, terminal or
deferred, is left exactly where it is, never re-closed in a different
flavour.

## Import and bulk edit

**Manage → Import** reads a directory of `.md` files, previews what each one
would become against the board's own columns, and copies it in - retiring
the source file stays a deliberate manual act, never this page's. In a tab
scoped to projects the Project chooser lists only those, and a tab scoped to
a single project has it already chosen.

**Manage → Bulk edit** runs the same server-side filters the board does -
project, type, status, parent, ancestor, text - then applies one edit to
every result still ticked when the button is pressed, not to whatever the
filter would return if run again later. In a scoped tab its Project filter and
results are the scope's, while *move to project* still offers every project; an
issue moved out of scope leaves the table and is named in the result.

## Attachments

A file dropped onto the **New issue** dialog's body, or onto an issue's
**Description** section on the issue page or the board's peek, is uploaded
and linked into the description, in Edit or in Preview alike. While a file
is in the air over either area it reads `Drop to attach`. Several files
dropped at once are uploaded in order and inserted one per line. A file
dropped or pasted belongs to the project the issue, or the New issue dialog,
is in; one dropped before this landed, or dropped anywhere else, stays
unlinked.

A PNG, JPEG, GIF or WebP becomes an inline image; anything else becomes a
download link named for the file itself, routed through
[the API reference](api-reference). An image over 2 MB is redrawn smaller
in the browser before it is sent, and a shrunk GIF or PNG comes out as a
still PNG.

The upload lands at the caret when the editor is open and has one;
otherwise it lands at the end of the draft after a blank line, and Preview
switches to Edit to show it. Either way the draft is then unsaved until
**Save**, the same as typing is. A pasted image is uploaded the same way
and always lands at the caret.

A file over 25 MB is refused by the server, and nothing is inserted - the
server's own sentence appears under the editor instead. Dropping several
files at once stops at the first refusal: nothing from that drop is
inserted, and the sentence names the file that was refused.

An issue holds whatever its description links. Under the description -
on the issue page, on the board's peek, and in the New issue dialog,
directly after its own description box - a list draws one row per link,
each a name, a size and a download. The list reads the text as it is
being edited, not the saved description, so a drop or a paste shows a row
before **Save**, and removing a link from the text drops its row before
Save too. A pasted image is in the list like any other link, and one
pasted before this list existed still reads `image.png` - the paste
handler's own fallback name, unrelated to this feature. A link naming a
file that is no longer stored reads `(deleted)`, with no download. See
[the API reference](api-reference#attachments-images-and-import) for the
routes behind each row.

Each row also carries a **Delete**, which asks `Delete <file>? The links
to it in this description are removed too.` Saying no leaves everything
exactly as it was. Saying yes removes every link to that file from the
description, and - on the issue page and the board's peek - saves the
description at once if nothing else was being edited, so the stored
description never links a file that is gone; a description already
mid-edit keeps those edits unsaved, with the row and its link already
gone and Save still offered for the rest. Deleting an attachment is
person-only, the same line the upload already draws - see
[the API reference](api-reference#attachments-images-and-import).

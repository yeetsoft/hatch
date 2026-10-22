---
title: Issues
section: Concepts
order: 20
summary: What hangs under what, and what the board does with each field on a ticket.
---

A ticket crosses the columns [the lifecycle of a ticket](the-lifecycle-of-a-ticket) describes. This
page is about the fields that travel with it along the way.

## Types, and what may hang under what

Four types: epic, story, task, bug. Any of them may have no parent at all —
the ordinary state of a freshly captured bug, or an epic with nothing broken
out of it yet. Where one does have a parent, the model allows exactly this:
an epic hangs under an epic, a story hangs under an epic, a task hangs under
a story, a bug or an epic, and a bug hangs under an epic or a story.

That is wider than the planning habit of an epic taking stories and a story
taking tasks. The habit is a way of breaking work down, not a rule the model
enforces — a task may hang directly under an epic, and a bug under either an
epic or a story.

## Keys and numbering

The key shown everywhere, `AER-12`, is **computed** — the project's own key
and the issue's number, joined by a dash — and never stored, so there is
exactly one fact about a key anywhere and no chance of two copies
disagreeing after a rename. The number is minted from the project's own
counter, never from counting what is already there: a deleted issue's
number is never handed to the next one, so an old link becomes a dead link,
never a different ticket. Moving an issue to another project mints it a new
number there, and its old key goes dead the same way.

## Parent and children

Parentage is optional, and checked on every write: the parent has to exist,
sit in the same project, be a legal type for this one (above), and form no
cycle. A parent's own progress is read from its children, not from its own
column — the totals count **leaves**, issues with no children of their own,
so a story standing in review with every task still in To Do reads as To Do,
because the tasks are the work and not the story's own column.

**Plan** draws its own meter from this same rollup, one row per epic in the
house.

## Rank and ordering

A card's place in its column is a rank picked by the server, never the
client. A drop says only *before this card* or *after this card*, and the
number in between is computed on the board's own side, so a browser and a
script can never disagree about where a card landed. Dropping at the very
top or bottom, or between two cards with no room left between their ranks,
is handled the same way — the server either reaches past the end or
renumbers the column first, without the client ever seeing a rank.

Within a column, the dispatcher reads cards in rank order, so dragging a card
up in its column is what gets it worked sooner.

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
*expediting* it.

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
one branch cannot be moved onto the other.

## Pre-approval

Three levels: none, express, preapproved — read live from the nearest
ancestor that sets one, the same way priority is. `express` carries a ticket
past a column ticked [Express skips](the-board-and-its-columns), with no
session, as long as it has no [unanswered question](questions-and-decisions).
`preapproved` carries it further: once its pull request is green and
current, or once every child is done, the board carries it on by itself,
with nobody reading the diff first — a move [the lifecycle of a
ticket](the-lifecycle-of-a-ticket) calls the hop.

## Runner tags

A ticket's own say over which runner may implement it — a project may
require tags too, the same shape. What a runner is actually held to is the
union of all three: the project's own, every ancestor's own, and the
ticket's own, rather than whichever is nearest. By default nothing is
required anywhere, so most tickets say nothing about tags at all; a runner
that advertises none of what is required is skipped, the same way a
dependency or a hold would be.

The field sits on the issue page, in the Details card's edit view, right
after Pre-approval, and opening it takes nothing beyond being able to open
the ticket at all — the same as its title and description. Every tag it
inherits is read out underneath, one sentence per source in the order the
union itself is built: the project first, then each ancestor in turn, naming
where each one came from.

## Held

A person's own mark, set from the ticket's **Hold for me** button and read
back as **Held** — never a key's, and never set by a session. A held ticket
is folded out of dispatch unconditionally, and stays folded until a person
clears it. That is different from a stall, which a session leaves behind and
which the board resumes on its own after a wait — see [the lifecycle of a
ticket](the-lifecycle-of-a-ticket).

## Deferring an issue

Reached from the ticket's own status bar, or from the board's peek — never
from a drag, since a drag can overshoot a lane and work should not be
shelved by accident. Deferring a ticket with open work under it shelves that
work too, in one step, the same way closing a subtree does below.

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
the source file stays a deliberate manual act, never this page's.

**Manage → Bulk edit** runs the same server-side filters the board does -
project, type, status, parent, ancestor, text - then applies one edit to
every result still ticked when the button is pressed, not to whatever the
filter would return if run again later.

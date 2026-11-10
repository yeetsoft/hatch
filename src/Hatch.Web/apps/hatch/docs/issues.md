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
house. In a tab scoped to projects its own Project picker follows the same rule
as the board's: see
[Scoping a tab to projects](the-board-and-its-columns#scoping-a-tab-to-projects).

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

## Finished with no code

A ticket whose session declared it finished with no code (`hatch pr --none`)
wears the 😎 glyph on its board card and in the peek, in whatever column it
stands in, with the reason in the glyph's title. It goes when the declaration
is cleared or set aside. [The lifecycle of a
ticket](the-lifecycle-of-a-ticket) has how the declaration is made and set
aside. In review, it is carried to Done with no person when it is pre-approved
or its project has **A no-code declaration closes the ticket** on; otherwise it
waits for a person.

## Runner tags

A ticket's own say over which runner may work it — a project may
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
dropped at once are uploaded in order and inserted one per line.

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

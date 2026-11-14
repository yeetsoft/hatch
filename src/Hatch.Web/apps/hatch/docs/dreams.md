---
title: Dreams
section: Concepts
order: 25
summary: A line worth keeping before it is work: where it is captured, how it is found, and what it means for one to be woven into an issue.
---

The **Dreams** screen, at `/dreams` and first in the Track group of the primary nav, keeps the
lines somebody wanted said before they are work. An [issue](issues) is work with a status; a dream
is what comes before one.

## What a dream is

A dream is a line somebody wanted said - an idea, a complaint, a want - captured with no ceremony.
It may lean toward a project, or be left loose, leaning toward none.

A dream is not an issue. It carries no status, sits in no column and has no assignee, so the board
never dispatches it and the loop never picks it up. It is a place to put a thought down until it is
worth turning into a ticket, or never is.

## Writing one

The box at the top of the screen takes a dream. Type it in the box, which reads markdown like any
other description, and press **Post** - or press Ctrl+Enter (Cmd+Enter on a Mac) without leaving the
box. The **lean** is the project it points toward, and **None** leaves it loose. The **source** is
where it came from, and may be a web address.

A posted dream appears at the top of the list at once and the box, the lean and the source empty
themselves. If the search or the filters would hide the new dream, they are cleared so it can be
seen. If the server refuses the dream, its reason shows beside the box and what you typed stays as
you left it, ready to post again.

## From a Claude session

A session posts a dream through `hatch dream`, and the dream carries the session as its source, so
the list says where it came from. A claude.ai conversation has no command to call: paste it into
the box above as one dream, and note where it came from in the source. The command's flags are in
the [CLI reference](cli-reference).

## Editing and deleting one

Each dream in the list has an **Edit** button, which works as it does on an issue's description:
Edit and Preview switch between the box and the rendered words, and **Save** appears once the words
differ from what is stored. The new text shows in place, with no reload. If the server refuses the
change, its reason shows on that row and your draft stays as you typed it. Only the words are
editable; the lean and the source are set when the dream is posted.

**Delete** asks first. Cancelling the question, or a refusal from the server, leaves the list as it
was, and a refusal shows its reason on that row. Deleting a dream also removes it from the "Drew on"
of any issue it was woven into, and the question names those issues. Each row keeps its own draft
and its own reason, so editing or deleting one never touches another.

An edit does not run the search again: a dream whose new words no longer match the search stays in
the list until the next time it loads.

## Finding one

The search box matches any word, not every word: a query of two words finds a dream that has either.
That is unlike the Search page, which wants all of them. The results are narrowed by project, and by
a loose-only toggle that keeps only the dreams no issue has drawn on.

In a tab [scoped](the-board-and-its-columns#scoping-a-tab-to-projects) to some projects, the list
shows those projects' dreams and, always, the dreams that lean toward no project. The Project
filter, the **Lean** chooser and the **Project** chooser under **Weave an epic** list only those
projects, and **None** stays on **Lean**; the filter is not drawn for a one-project scope. A
`projectId` in the address that the scope does not hold is dropped, and the list reads as if no
project were chosen.

The URL carries the search, so a link to a query is a real link: paste it to somebody and they see
the same dreams.

## Woven into an issue

An issue can be recorded as having drawn on one or more dreams. That is what **woven into** means,
and it is a record, not a move: the dream stays where it was, and the issue keeps the list. A dream
woven into an issue shows a chip linking to that issue; a dream with no issue woven into it is a
loose one. The call that writes the record is `PUT /issues/{key}/dreams`, described in the
[API reference](api-reference).

## Weaving an epic

**Weave an epic** sits closed, under the composer box, open by pressing its
summary. The composer stays first, even on the phone - nothing goes above it.

Inside is a **Mode**, one of four: a seed sentence's own thread; the thread
the dreams return to with the most urgency; the thread the most dreams touch;
or the next step toward the vision, given what has already shipped. Choosing
**seed** shows a **Seed** box, which follows the search box above until you
touch it - the moment you have typed in it, even to clear it, it keeps its
own text and stops following search.

A **Project** chooser defaults to the Project facet's own choice, or to the
one project when the board runs only one; with more than one project and no
facet set, nothing is checked, and the project must be chosen by hand - an
epic needs a home. Below it, **Use the dreams on screen** hands the weave the
dreams currently listed, in the order they are shown, as the ones to start
reading from; left unticked, the weave names none and the session finds its
own.

Pressing **Weave** files an **epic** - in the breakdown column, briefed to be
woven from the dreams - and takes you straight to it. A refusal shows beside
the button and leaves every control as it was, ready to press again.

What happens next happens off this page: the loop's epic-breakdown session
reads the dreams the brief points at, gives the epic a title of its own in
place of `Weave: <seed>`, rewrites its description to open with a summary and
quote the dreams after it, files its stories, and records which dreams it drew
on - which
is what puts a **woven into** chip on each of them, the same chip a dream
gets from any other issue drawing on it (see [above](#woven-into-an-issue)).
Nothing on the Dream Board itself is edited by that session.

It is a person's press, not a key's: weaving spends a breakdown session at
the largest model, which is a person's call to spend, so an agent calling the
route is refused. Pressing also does nothing if nobody has told the board
which column an epic is born in - a weave is refused outright where no
playbook row covers the `epic` type. See the
[API reference](api-reference) for the route and its refusals.

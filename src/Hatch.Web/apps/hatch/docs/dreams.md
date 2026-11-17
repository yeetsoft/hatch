---
title: Dreams
section: Concepts
order: 25
summary: A line worth keeping before it is work: where it is captured, how it is found, and what it means for one to be woven into an issue.
---

The **Dreams** screen, at `/dreams` and first in the Track group of the primary nav, keeps the
lines somebody wanted said before they are work. An [issue](issues) is work with a status; a dream
is what comes before one.

Under the title, the page says in three lines what it is for:

1. **Drop in** snippets of ideas that are not epics yet.
2. **Recycle** epics that are not ready.
3. **Weave** new work out of the cloud of the undefined.

## What a dream is

A dream is a line somebody wanted said - an idea, a complaint, a want - captured with no ceremony.
It may lean toward a project, or be left loose, leaning toward none.

A dream is not an issue. It carries no status, sits in no column and has no assignee, so the board
never dispatches it and the loop never picks it up. It is a place to put a thought down until it is
worth turning into a ticket, or never is.

## Writing one

Above the box sits the page's one **Project** picker, with a first tile, **No project**, for none.
What it holds is the project the dream leans toward: a dream posted with a project picked leans
toward it, and one posted with none picked leans toward none. A line beside the picker says what the
choice means. The picker is drawn whenever the board has a project.

The composer is an open box with no card around it, asking **What are you dreaming about?** in large
type. Under it runs a thin rule, faint while the box rests and full while it has focus. Type the dream
in the box, which reads markdown like any other description, and press **Post** just beneath it. On a
desktop the key hint beside Post says the shortcut: Ctrl+Enter (Cmd+Enter on a Mac) posts without
leaving the box. On a phone Post spans the width. Post stays dimmed while the box is empty.

The **source** is where it came from, and may be a web address. It is folded away behind the small
**Source** control on Post's row; press it and the field opens with the caret in it. A source with
text in it stays open, and is saved with the dream.

A posted dream appears at the top of the list at once when the list is open. The text falls into the rule as it fades while
the box empties, and the source folds again; with reduced motion on, nothing moves and the box just
empties. The picker keeps its choice, so a run of dreams for one project is a run of posts. If the
search or the filters would hide the new dream, they are cleared so it can be seen - all but the
picked project, which stays, and the list stays open. With the list folded (see
[Finding one](#finding-one)) there is no list to put it in: the box empties, the count on **Past
dreams** goes up by one, and a line under it says **Posted.** with a **Show it** button. Pressing
that opens the list with the new dream first. If the server refuses the dream, its reason shows under the box and what
you typed stays as you left it, ready to post again.

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

The history is folded when you arrive: a bare `/dreams` draws neither the search row nor the list.
One control under **Weave an epic**, **Past dreams**, opens both, and pressing it again folds them.
It carries a magnifier glyph and a count in the mono face, which is the picked project's total - the
same dreams the list would hold with that project and no search - so it holds while you search. The
list fades in when it opens; with reduced motion on it just appears.

Open lives in the address, as `open=true`, so Back undoes an open or a close, a reload keeps it and a
pasted link reproduces it. The list is also open whenever the address carries `q`, `loose`, `issue`
or `remandedFrom`, so the links the issue page and the board's remand notice make
(`/dreams?issue=KEY`, `/dreams?remandedFrom=KEY`) arrive open and filtered. Folding the list removes
`open` and clears those four, and leaves `projectId` alone.

The search box matches any word, not every word: a query of two words finds a dream that has either.
That is unlike the Search page, which wants all of them. The results are narrowed by the project
picked above the box, and by a loose-only toggle that keeps only the dreams no issue has drawn on.

With a project picked, the list holds that project's dreams and the dreams that lean toward no
project, and none of another project's. With none picked it covers every project. The pick is
written to the address as `projectId`, so a reload keeps it and Back undoes it.

In a tab [scoped](the-board-and-its-columns#scoping-a-tab-to-projects) to some projects, the list
shows those projects' dreams and, always, the dreams that lean toward no project. The picker lists
only those projects, after **No project**. A `projectId` in the address that the scope does not hold
is dropped, and the list reads as if no project were picked.

The URL carries the search, so a link to a query is a real link: paste it to somebody and they see
the same dreams.

## Woven into an issue

An issue can be recorded as having drawn on one or more dreams. That is what **woven into** means,
and it is a record, not a move: the dream stays where it was, and the issue keeps the list. A dream
woven into an issue shows a chip linking to that issue; a dream with no issue woven into it is a
loose one. The call that writes the record is `PUT /issues/{key}/dreams`, described in the
[API reference](api-reference).

## Remanded from an issue

Shelving an issue and its open tree ([the board and its columns](the-board-and-its-columns#column-flags)) writes a
dream of its own: the issue's title and description, so the thought is not lost once the tickets are
parked. A dream remanded from an issue shows a chip reading "remanded from" the root issue, linking
to it, with "and N more" after it when the remand carried descendants along. That chip is a record,
the same as a woven-into chip: the issues it names stay exactly where the remand left them, in their
deferred column, whether or not they are ever picked back up. Deleting a remanded dream moves none of
them - the question asks first and says so, naming the root the way the chip does.

The issue page carries the other half of the record. An issue a dream was remanded from shows a
**Remanded to a dream** section with the dream's first line, linking to the Dreams page filtered to
it, and when and by whom it was remanded. The section stays once the issue is moved back onto a lane
- it says "It is back on the board" in place of "It is still in a deferred column" - and only
disappears if no dream was ever remanded from that issue.

`remandedFrom=KEY` on the Dreams page's own URL is the filter behind that link - paste it to somebody
and they see the same list, the way every other filter on this page does (see
[above](#finding-one)); the page also offers a button to clear it.

## Weaving an epic

**Weave an epic** sits closed, under the composer box, open by pressing its
summary. It has no project control of its own: it uses the picker above the
composer.

Inside is a **Mode**, one of four: a seed sentence's own thread; the thread
the dreams return to with the most urgency; the thread the most dreams touch;
or the next step toward the vision, given what has already shipped. Choosing
**seed** shows a **Seed** box, which follows the search box above until you
touch it - the moment you have typed in it, even to clear it, it keeps its
own text and stops following search.

The epic is filed into the picked project, or into the one project when the
board runs only one, even with none picked. With more than one project and
none picked, **Weave** is disabled and a line beside it says to pick a project
- an epic needs a home. Below the mode, **Use the dreams on screen** hands the weave the
dreams currently listed, in the order they are shown, as the ones to start
reading from; left unticked, the weave names none and the session finds its
own. While the history is folded no dreams are on screen, so the box names none.

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

A remanded dream brings its tree with it. The session reads the shelved work
through the issue routes, names that tree in the epic's description, and moves
nothing out of its deferred column.

It is a person's press, not a key's: weaving spends a breakdown session at
the largest model, which is a person's call to spend, so an agent calling the
route is refused. Pressing also does nothing if nobody has told the board
which column an epic is born in - a weave is refused outright where no
playbook row covers the `epic` type. See the
[API reference](api-reference) for the route and its refusals.

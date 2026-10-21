---
title: The board and its columns
section: Concepts
order: 10
summary: The seven columns a stock board ships with, and who moves work out of each.
---

This is the stock board. An operator may rename its columns, add more and
reorder them, so nothing below depends on a name: each column is described by
what happens in it, and the names are only what a fresh install calls them.
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
  close hop below.

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
  pulls**, while its parent stands in the column where code gets written.
- **Sendback** — a childless issue whose brief is too long for its playbook is
  carried back to the column that feeds the column where code gets written. It
  is the one hop that moves a ticket left.
- **Approve** — a pre-approved issue in review, whose pull request is current
  and would merge cleanly, goes into the column ticked **Merges PR**.
- **Close** — a pre-approved parent in review with every child done goes into
  the first Done column to its right.

[The lifecycle of a ticket](the-lifecycle-of-a-ticket) follows one ticket
across every column and hop above, start to finish.

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
  board still has columns of those names. `flow-wip-and-the-battery` covers
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
  PR** is ticked.
- **Merges PR** — the runner merges an issue's pull request once it stands in
  this column, current and green, with no session. No stock column ships with
  it. Only a person may tick it, never a key. It needs **Protected** ticked
  first, and a Deferred or Done column is refused.

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
all. `review-pull-requests-and-the-merge-train` covers reading the pull
request itself, and the train an approved ticket joins behind whatever else
in its project is also approved.

The stock board still ships with exactly the seven columns above, and
`Approved` is never one of them. Adding it — naming an approver, and choosing
which of the forge's three shapes the repository is — is covered end to end
on its own page, not repeated here:
[letting the loop merge for you](letting-the-loop-merge-for-you).

Columns and their flags are the shape of the board. [Issues](issues) is what
moves across them: dependencies, deferring, and closing a subtree.

## Coming

Pages this one links to that are not written yet:

- `flow-wip-and-the-battery`
- `review-pull-requests-and-the-merge-train`

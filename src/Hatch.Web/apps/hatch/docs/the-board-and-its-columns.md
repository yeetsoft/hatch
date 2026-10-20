---
title: The board and its columns
section: Concepts
order: 10
summary: The seven columns a stock board ships with, and who moves work out of each.
---

This is the stock board. An operator may rename its columns, add more and
reorder them, so nothing below depends on a name: each column is described by
what happens in it, and the names are only what a fresh install calls them.
[How work flows](how-work-flows) follows a ticket across them.

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
- **Parent pulls** — a child is carried on, out of a column ticked **Parent
  pulls**, while its parent stands in the column where code gets written.
- **Epics** — an epic is carried toward the column where code gets written
  once it has children.
- **Approve** — a pre-approved issue in review, whose pull request is current
  and would merge cleanly, goes into the column ticked **Merges PR**.
- **Close** — a pre-approved parent in review with every child done goes into
  the first Done column to its right.

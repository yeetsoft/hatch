---
title: Flow, WIP and the battery
section: Concepts
order: 80
summary: How the WIP limits and the fold keep a loop that can work any ticket from working all of them at once.
---

A loop that can work any ticket will work all of them at once. Nothing about a
ticket being ready stops a second one being ready too, and a third, until a
board that was meant to move one thing forward at a time has a dozen half-done.
Every mechanism on this page exists to stop that: a limit on how much may be in
progress, and a rule that an issue the board will not dispatch says so.

## WIP limits

The WIP section is one set of columns, the ones ticked **WIP** — see [the board
and its columns](the-board-and-its-columns) for the flag. A limit is a number
on the section, and the section counts work in two slices, always both and
always in this order:

- `story,bug` — a story or a bug counts here.
- `epic` — an epic counts here.

A task counts toward neither, so tasks are never held by a WIP limit. Each
slice has its own limit, and a slice with none still exists and still reports
its load; it just has nothing to hold anyone back with.

A limit is measured as that slice's load across the whole WIP section, not per
column: five stories and bugs spread over three WIP columns are five against
the one limit.

An epic also carries a limit of its own, over its direct stories and bugs — how
many of them may be in the WIP section at once. It is held beside the
section-wide limit, not instead of it, and it is off by default: an epic with no
number set holds none of its stories back.

When a slice is full, the move into the section is held, and the sentence says
which slice or epic is full and at what count:

- *the WIP section is full — 5 of 5 stories and bugs are in it*
- *an epic is at its limit — 1 of 1 of its stories and bugs is in the WIP
  section*, naming the epic

Where both are full, the section's sentence is the one said. Either way,
nothing more is pulled in until something leaves.

## The fold

An issue the board will not dispatch is *folded*, and a fold always carries a
sentence saying why. These are the reasons, in the order the dispatcher asks
them — the first that applies is the one said:

1. **A ready date that has not arrived.** The issue is dated for later; see
   [issues](issues) for ready dates.
2. **An open question.** The issue is waiting on a person, not on an agent; see
   [questions and decisions](questions-and-decisions).
3. **An unmet dependency.** Something it waits on has not landed; dependencies
   are also on [issues](issues).
4. **An epic with nothing filed under it.** *Nothing is filed under it — an epic
   runs its stories, and it has none.*
5. **A full WIP section, or an epic at its own limit.** The two sentences above,
   each with a tail saying nothing more is pulled in until something leaves.
6. **No playbook for the move.** Asked last, because it is only worth saying
   about an issue that is otherwise a candidate; see [playbooks](playbooks) for
   how a move is matched.

A fold is free and never permanent. It is asked again on every
pass, so nothing is ever un-folded by hand: when the date arrives, the question
is answered, the dependency lands, a story is filed under the epic, a slot
opens or a playbook is written, the next pass finds the issue clear and
dispatches it.

`hatch queue` prints every issue a pass would look at and why — each row with
its sentence, or nothing where the issue is clear. It is `GET
/api/hatch/work/queue` underneath, and it is where to look first when a ticket
you expected to move has not.

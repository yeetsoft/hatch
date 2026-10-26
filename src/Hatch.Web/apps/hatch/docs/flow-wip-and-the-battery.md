---
title: Flow, WIP and the battery
section: Concepts
order: 80
summary: How the WIP limits, the fold, stalls, the battery and preemption keep a loop that can work any ticket from working all of them at once.
---

A loop that can work any ticket will work all of them at once. Nothing about a
ticket being ready stops a second one being ready too, and a third, until a
board that was meant to move one thing forward at a time has a dozen half-done.
Every mechanism on this page exists to stop that: a limit on how much may be in
progress, a rule that an issue the board will not dispatch says so, a rule for
a session that moved nothing, a reading of the account the loop is spending,
and a way for urgent work to take a runner that is busy with something less.

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

## When an increment does nothing

A **stall** is a session that ends with the ticket in the column it found it
in; a plain advance with no `work-log` counts as not moved. It matters because
nothing about the board changed, so the next pass would pick the same ticket
and spend the same money, all night. Stalls escalate, and each is written on
the ticket:

1. **The first in a row is let go.** One comment says why, with the command to
   resume the session. There is no mark, and the ticket is free for the very
   next pass.
2. **The second is marked.** The comment is joined by a stall mark, which
   records when and why and blocks the next dispatch. The queue then says the
   ticket was *stalled N minutes ago* and *resumes itself after 15 minutes*.
   The wait is `Hatch:StallResumeMinutes`, and `0` never resumes a ticket
   unattended.
3. **The third in a row is held for a person.** `Hatch:StallResumeLimit`
   defaults to 3. After its window the queue's sentence becomes *stalled 3 times
   in a row … held for a person now*, and the board stops resuming it. A
   person's **Resume now** or **Hold for me** clears the mark.

The count is of consecutive releases that dropped the ticket. It starts again
after a release that worked, a change of status, or a person's answer.

A ticket an increment let go is also left to a different live runner: while
another runner could take it, the runner that dropped it is told to leave it,
and with only one runner nothing changes.

Some endings that look like a stall are not one, and count toward nothing:

- Filing issues under the ticket. The ticket is measured against the children
  it had at dispatch, and a new key is progress.
- A session that asked a question. The open question is the flag.
- Running out of usage; the runner waits for the reset.
- Being preempted, below.
- Losing the lease.
- A hop, where no session ran.
- A hard limit, which published what the session left.

A separate counter ends the night: three increments in a row that exit
non-zero. The endings above do not count toward it either. See [One increment
and the loop](one-increment-and-the-loop#stalls) for the escalation as the
operator of a loop meets it.

## The battery

The battery is how much of the account an unattended pass would spend is left,
and when it comes back. It is optional by design: a person no runner has ever
reported for gets no battery at all, with no placeholder and no error — the
endpoint answers `204`. That is a supported state, and it is the default.

**Where the reading comes from.** The runner reports the account's usage on the
heartbeat it already sends, and on the claim heartbeat while an increment
runs. During a session the reading comes from the session's own stream; between sessions it comes from a `/usage` probe at the top
of idle passes, and the same probe runs every two minutes through a running
increment, so the reading never ages past what the board trusts. Hatch keeps it per person and answers only the caller's own. A
person with two runners has one account, so the freshest whole reading wins
rather than a merge of the two.

**What it shows.** The colour is decided on the client from the percentage
used: green under 50%, yellow from 50% to 79%, red from 80%. The battery takes
the worst window, the one with the highest percentage, and never the `extra`
credit-line row. Opened, each window is two bars, spent and time gone; a dashed time bar
means no reset is known, and a window that has already reset reads as not
started. A reading is `ok` within five minutes of when it was read and `stale`
after, still shown with its age; there is no other state. Refresh re-reads
Hatch's own row only. [Watching the loop](watching-the-loop#the-battery) has the
panel itself.

**What the loop does as it runs down.** An unattended pass reads the same
reading before it touches work at the bottom of the [priority](issues#priority)
scale:

- **Economy** work is taken only if every window of the account is ahead of its
  own pace with 10 points to spare.
- **Low** work is taken only if the session window alone is on pace.

Naming the ticket by hand, or a hop, skips both gates. With no reading yet, or
a key owned by nobody, both gates fold. A runner whose account is spent waits
for the reset, and none of that is a stall. [One increment and the
loop](one-increment-and-the-loop#one-pass-in-order) has where the gates sit in a
pass.

**Not a budget.** Nothing is capped or metered by the battery. It reads the
account's own limits and decides nothing except those two gates. The cap on
what a run spends is `--max-spend`; see [bounds, timeouts and
stopping](one-increment-and-the-loop#bounds-timeouts-and-stopping).

## Preemption

A runner heartbeating on ordinary work can be told it has been **preempted**
when emergency-level work is actionable and nobody holds it. Nothing is
nominated or reserved in advance. It is decided at each heartbeat from the board
as it stands, which is what lets an urgent ticket walk the board on one runner.

The runner is told only when all five of these hold:

1. An emergency ticket is actionable and unclaimed.
2. The runner's own ticket is below emergency. Emergency work is never
   preempted, and that is judged by the ticket's *effective* level: a story or
   task under an emergency epic is as safe as one marked emergency itself (see
   [priority](issues#priority), which is inherited).
3. Its ticket is last in the dispatcher's order among the live claims not yet
   told: economy before low, and low before normal and expedited.
4. Fewer runners have already been told than there are emergency tickets
   waiting.
5. No live runner is free. A free one takes the emergency ticket on its own next
   pass, so preempting would spend a session to gain nothing.

When it is told, the session is cancelled, the tree is committed and pushed to
the ticket's branch, one comment names the emergency ticket, and the claim is
released as `preempted`. That is not a stall and not a failure: the night's tally
lists it separately.

## Reports

**Reports** turns this page's own mechanics into pictures: the Timeline
report draws when each epic was in flight and how long its work sat in each
column; Cumulative flow draws how much work sat in each column over time,
and where it is piling up.

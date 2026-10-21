---
title: Deterministic work and agent work
section: Concepts
order: 40
summary: The line between what the board decides from its own shape, for nothing, and what needs a session, bought by the increment.
---

Everything the board can decide from its own shape — a column, a flag, a date, a limit, a live claim — it decides for itself, for nothing. Everything that needs judgement — code written, a diff read, a decision nobody wrote a rule for — is one increment of one session, bought.

## What the board does by rule

- **Dispatch** — one pass over the board, built the same way for "what's next, anywhere" as for a request naming one ticket: the rightmost column with something to advance, read right to left, with anything marked off normal considered first or last wherever it stands. Run on every pass of the loop, and on every request naming a ticket by key.
- **The fold** — a fixed, ordered list of reasons a move is not an agent's to make right now — a terminal column, a live claim, a pause, an unanswered question, a repository nobody has a checkout of, something it depends on, a full WIP section, a branch that still conflicts, and more — asked in the same order whichever way the ticket was reached, so the loop and a person asking about one ticket by name hear the same sentence for the same reason.
- **Claims** — a lease on one ticket, taken before a session starts and let go, or timed out, once it ends, so a second runner reading the same board never starts a second session on it. A live claim on a relative — an ancestor or a descendant — folds the move too, because a parent's session is building what its children describe.
- **The hops** — seven conditions under which the board carries a ticket across the column it stands in with no session and nothing spent: [express, epic, under, parent, sendback, approve and close](the-lifecycle-of-a-ticket#the-hops-the-board-makes).
- **Express** — [pre-approval](issues#pre-approval), read live from the nearest ancestor that sets one, that lets a ticket skip a column marked to let it, as long as nothing on it is an open question.
- **The merge train** — one order per project's merge column, head to tail, that lets an approved pull request merge, get brought current with the trunk, or get carried back to review for a conflict or a failed build — each on the board's own say, with no session reading the diff. `review-pull-requests-and-the-merge-train` covers riding it.
- **Stalls and let-go** — an increment that ends with its ticket exactly where it found it is let go once, quietly; a second one in a row marks it and holds the next dispatch for fifteen minutes before the board resumes it on its own; a third holds it for a person. [One increment and the loop](one-increment-and-the-loop#stalls) has the counts.
- **The WIP gates** — a limit on how many stories, bugs and epics may stand in the columns marked WIP at once, read off the board's own load and held until something leaves; an epic can carry a limit of its own on top of the section's. `flow-wip-and-the-battery` has the numbers.
- **The battery** — the account an unattended pass would spend, read before it will touch anything at economy or low priority, so a quiet night never spends past what is actually left. `flow-wip-and-the-battery` covers the reading.
- **The build and merge checks** — what a runner found when it looked at a branch: whether it merges cleanly with the trunk, and whether the build on its tip passed. A session is dispatched into review for one of the two gone wrong, and for nothing else. `review-pull-requests-and-the-merge-train` covers reading them.

## What a session does

Where none of that clears a ticket, the board spawns one increment: one ticket, one process, one agent session, run under whichever playbook [the board and its columns](the-board-and-its-columns#who-moves-work) matches to that column and that type — the same matrix that, read from the board's own side, says which columns are a session's at all. [One increment and the loop](one-increment-and-the-loop) follows the process itself end to end.

A session is judged by what it leaves behind, never by what it said about itself: the ticket moved to a new column, a comment landed on it, a branch was pushed, a pull request was opened, a work-log row was written down. [The work-log block](what-a-session-is-told#the-work-log-block) is read for its own title and its own summary, but reading well is not the test — a session that wrote a convincing one and left the ticket exactly where it found it is a [stall](one-increment-and-the-loop#stalls), the same as a session that wrote nothing at all.

## Every mechanism, and what it costs

| Mechanism | Who | When | Costs |
|---|---|---|---|
| Dispatch | the board | every pass of the loop, and every request naming a ticket by key | nothing |
| The fold | the board | inside dispatch, once per candidate, in a fixed order | nothing |
| Claims | the board | taken before a session starts, released or timed out after | nothing |
| The hops | the board | the moment dispatch would otherwise spawn a session | nothing |
| Express | the board | a column marked to skip it, read on every pass | nothing |
| The merge train | the board | its own clock, running independently of the loop's own pass interval | nothing |
| Stalls and let-go | the board | the moment an increment ends having moved nothing | nothing |
| The WIP gates | the board | every move into a column marked WIP | nothing |
| The battery | the board | before an unattended pass will touch economy or low work | nothing |
| The build and merge checks | the board | continuously, as a runner reports what it found | nothing |

## Coming

Pages this one links to that are not written yet:

- `flow-wip-and-the-battery`
- `review-pull-requests-and-the-merge-train`

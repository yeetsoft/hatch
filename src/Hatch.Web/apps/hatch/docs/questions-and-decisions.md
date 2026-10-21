---
title: Questions and decisions
section: Concepts
order: 60
summary: How a session asks for a decision it should not make, what an open question holds up, and where the answer is kept.
---

Some things are not a session's to choose: a product call, a name that will be
lived with for years, a tradeoff with no technically correct side. A session
that reaches one does not guess and does not quietly take whichever option is
easiest to build. It asks, and a person answers. The question is a record on the
ticket rather than a line in a chat, so the board can count it, show it and hold
the ticket until it is settled.

## Asking

A session asks with `hatch ask`, one question to a call, so each can be answered
on its own and a day apart from the others. The body is the question alone. The
tradeoffs go inside the options they belong to, each `--option` becoming
something the operator presses, and `--recommend` marks the one the session
would take — at most one.

A label is short enough to press and reads as a decision on its own:
`child-weighted`, not "weight each child equally". It becomes the answer's own
text, so it is worth getting right. Prose is for the answer that is genuinely
open-ended, where no short list of choices covers it.

A question the repository can answer is not one to ask. The session answers it
by reading, because a question the code already settles is a round trip through
a person for nothing. [What a session is
told](what-a-session-is-told#when-you-cannot-decide) has the instruction as the
session reads it, and [the CLI reference](cli-reference#ask) has the flags.

## What an open question blocks

A question is open when no comment answers it. That is worked out on each read
and never stored, so a question cannot be open and answered at once.

While a ticket holds one, the board folds it out of dispatch with *"N unanswered
question(s) - it is waiting on a person, not on an agent"*. That is the same for
the queue pass, for a dispatch aimed at that one ticket by name and for a hop,
and it lasts until the question is answered. Nothing is spawned at the ticket
and nothing is built past the question on a guess. A fix for a conflict or a red
build in a review column is held too, since it is a dispatch like any other.

One column is the exception. A column ticked **Merges PR** is judged on its
pull request's checks before any question is read, so a pull request there merges
whatever is open on its ticket.

What an open question does not block:

- Another ticket. The hold is on the one ticket.
- A person moving the ticket by hand. Only dispatch reads the question.
- Commenting on the ticket, filing under it or reading it.
- The merge column, as above.

A question on a ticket in a terminal column stops counting. The attention panel
drops it and the issue page titles the section *Asked (n)* instead of *Waiting
on you (n)*, though the question stays open on the record.

The board asks too. A check that fails twice in a row posts a question in prose
and holds the ticket, and that one blocks exactly as an agent's does.

## How the operator answers

An unanswered question shows in three places:

- A `?` badge with a count, on the ticket's card on the board.
- **Questions to answer**, in the **What is waiting on you** panel
  ([watching the loop](watching-the-loop#what-is-waiting-on-you)). It has one row
  per question, oldest first. A row opens the issue; it does not answer in place.
- The **Waiting on you** card on the issue page, where each question shows its
  options with the recommended one marked.

Choosing an option makes its label the answer's text. It does not send it:
**Answer**, or Enter, does. The box stays editable, so an answer the options did
not anticipate is typed over the label. An answer is one line. A longer reply,
or an answer with a caveat under it, is a comment.

From a terminal, `hatch questions` lists what is open and `hatch answer` settles
one, taking an option by its number ([the CLI reference](cli-reference#answer)
has the rest).

## Where the answer lives afterwards

On the issue, as an answer comment bound to the question it settles, with who
wrote it and when. The question stays in the thread with its options, so the
alternatives that were not taken remain on the record. Which option was taken is
not stored separately: the answer's text is the option's label, and a second
record saying it in numbers could only come to disagree with the first.

A later increment on the ticket is handed each answered question with its
answer and who gave it, as settled under *Decisions already made* ([what a
session is told](what-a-session-is-told#decisions-already-made)), and is told
not to ask again. Answering a question once is what stops the next session from
reopening it.

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

## The three levels

Every question carries a level saying how far work may go before it is answered.
`GET /api/questions`, `GET /api/issues/{key}/questions` and the ticket's comments
all read it, and `hatch questions` shows it beside who asked.

- `hold` - nothing moves. This is every question's default, and what a question
  asked before levels existed reads as.
- `build` - work is dispatched and built and stands in review, but is not
  merged.
- `merge` - the work merges, and the answer is looked over after.

Every question reads `hold` until a person sets another level. Whoever may answer
a question may set its level, with `PUT /api/issues/{key}/questions/{id}/level`;
no owner or approver role is needed, because it only loosens what that same person
could end by answering. A key cannot: asking with a level is refused, and so is the
route, because a session cannot decide how far its own question lets it go.

`build` and `merge` are refused on a question with no recommended option, since a
session would have nothing to build on. A person may always lower an open question
back to `hold`, and an answered question's level cannot be changed at all. A change is on the
ticket's trail as `question_level_changed`, with both levels and the person's name.

## How the operator answers

An unanswered question shows in four places:

- The **Ballot**, at `/ballot`, the first place a question shows. It is a link
  beside **Search** in the nav bar, a row straight after Search in the phone
  menu, and **Go to Ballot** in the console. It lists every
  waiting question under the issues that enclose it, each issue drawn with the
  first lines of its description above its questions, and a child issue nested
  inside its parent. It answers in place: each question is drawn as the issue
  page draws it, with who asked and when, its options as buttons with the
  recommended one marked, and the answer box and **Answer** button. The
  recommended option is one press and Enter away. Once the answer lands the
  question leaves the Ballot, an issue left with no question beneath it goes
  too, and the count in the bar drops without a reload. A refused answer shows
  its error beside the row, which stays with what was typed. Each issue on it
  also has **Preview**, which opens the issue in a dialog where its questions
  are answered the same way, and **Open in new tab**; the key opens the issue.
  The dialog is the board's peek, without the move, remand and delete
  controls: the status shows as a plain pill. Answering leaves it open and the
  Ballot behind it refreshes. It follows the tab's project scope, and a
  question answered elsewhere leaves it.
- A `?` badge with a count, on the ticket's card on the board.
- **Questions to answer**, in the **What is waiting on you** panel
  ([watching the loop](watching-the-loop#what-is-waiting-on-you)). It opens with
  a row, **Answer on the Ballot**, with the count, that goes to the Ballot. One
  smaller entry per question follows, oldest first, and opens the issue. The
  panel does not answer in place.
- The **Waiting on you** card on the issue page, where each question shows its
  options with the recommended one marked.
- Clicking the card behind the badge opens the peek with the same card drawn
  above the Description, answerable the same way. The badge clears on the
  board behind the dialog once it is.

Under each question's options, on the Ballot, the issue page's card and the
peek alike, a line says its level in words - *nothing moves until this is
answered*, *built but not merged until this is answered* or *merges now; the
answer is looked over after* - above three buttons, **hold**, **build** and
**merge**, with the current level marked and pressed. Pressing another sets it
([the three levels](#the-three-levels)) and the page reads again. A refused
change shows the server's sentence beside the buttons, and the level stays
where it was. A question that offers no recommendation says so and offers only
**hold**, since `build` and `merge` would be refused. An issue that shipped
with questions open shows no level control, because the level no longer
matters. The card's first line follows the strictest level among its
questions: at `hold` the ticket is not picked up again until they are answered;
at `build` it is built on the recommendation and waits in review; at `merge` it
is built and merged on the recommendation, and the questions wait for a look
back.

Choosing an option makes its label the answer's text. It does not send it:
**Answer**, or Enter, does. The box stays editable, so an answer the options did
not anticipate is typed over the label. An answer is one line. A longer reply,
or an answer with a caveat under it, is a comment.

From a terminal, `hatch questions` lists what is open and `hatch answer` settles
one, taking an option by its number ([the CLI reference](cli-reference#answer)
has the rest).

A question that has waited longer than `Hatch__GooseQuestionHours` (a day, by
default) opens one finding from [the goose](the-goose#questions-waiting), which
lists every such question, oldest first. It only says they are waiting; it
suggests no answer and answers none.

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

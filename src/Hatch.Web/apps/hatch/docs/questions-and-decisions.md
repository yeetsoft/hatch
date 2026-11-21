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

How far the board holds a ticket depends on the question's level (see [the three
levels](#the-three-levels)). The ticket's strictest open question counts, and so
does the strictest one on any ticket above it, so a question on an epic reaches
every story and task beneath it.

- At `hold`, every move of the ticket is folded with *"N unanswered question(s) -
  it is waiting on a person, not on an agent"*. Where the question is an
  ancestor's the sentence names that ticket: *"... on HA-12 - ..."*. That is the
  same for the queue pass, for a dispatch aimed at that one ticket by name and
  for a hop, and it lasts until the question is answered. Nothing is spawned at
  the ticket and nothing is built past the question on a guess. A fix for a
  conflict or a red build in a review column is held too, since it is a dispatch
  like any other. A passing check in the review column leaves the ticket where
  it is rather than carrying it into the merge column, and says so in a comment.
- At `build`, work is dispatched and built as usual, and only what would merge or
  close it is folded: the move out of review into the merge column or a done
  column (the approve hop and the close hop), and a passing check's advance
  there. The sentence reads *"N unanswered question(s) at build on HA-12 - it is
  built, and a person answers before it merges"*. A fix for a conflict or a red
  build still goes ahead. In a project that lands on the trunk there is no merge
  to hold, since building is landing, so the move into the implementation column
  is folded instead and the moves before it go ahead.
- At `merge`, nothing is folded: not a move, a hop or an advance, the approve
  and close hops included. The question does not go away when the ticket closes.
  It stays on the ballot, in the attention panel and in `hatch questions` until
  it is answered, as a look back.

Every one of these counts as waiting on a person, so the attention panel and
`hatch queue` read them the same way. A ticket flagged dangerous is the one case
that ignores the level: with any open question on it or above it, it is folded
with *"N unanswered question(s) on HA-12, and it is flagged dangerous (reason) -
a person answers before any of it moves"*.

One column is the exception. A column ticked **Merges PR** is judged on its
pull request's checks before any question is read, so a pull request there merges
whatever is open on its ticket.

What an open question does not block:

- Another ticket. The hold is on the one ticket.
- A person moving the ticket by hand. Only dispatch reads the question.
- Commenting on the ticket, filing under it or reading it.
- The merge column, as above.

A `hold` or `build` question on a ticket in a terminal column stops counting.
The attention panel drops it and the issue page titles the section *Asked (n)*
instead of *Waiting on you (n)*, though the question stays open on the record. A
`merge` question does not stop: it keeps counting after the ticket closes, and
reads as a look back until it is answered. An answer given after the ticket
closed is marked as given after the fact.

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

## What a session may answer

An ambition standing in the implementation column can be sent to answer one open
question beneath it, instead of running its pass. The board decides, and only
when all of this holds:

- The question is at `build` or `merge`. A `hold` question is never answered by a
  session, whatever the context says: it waits for a person
  ([the three levels](#the-three-levels)).
- It has no answer yet, and its issue has not closed.
- It is on an issue beneath the ambition, at any depth. A question on the
  ambition itself is never answered this way.
- Nothing from the question's issue up to the ambition is flagged dangerous, and
  the ambition is not either.
- No finished session has answered it already. A question is dispatched once: a
  session that ended `worked` and named the question takes it off the list, and
  one that was dropped does not.

Where two questions qualify, the newer one goes first. This comes before the
ambition's pass, so with both available the question is answered first. A board
with no playbook row marked *answers questions* runs the pass as it always has.

The work is dispatched on the ambition, not on the issue the question is on, and
`hatch queue` names both, as `answers the question on HA-12: <first line>`. The
dispatch carries the question itself as `answering`; see
[the API reference](api-reference#work-what-a-runner-asks-the-board-for).

The answer is a session's own, so it reads as a look back: the person looks it
over on the Ballot afterwards and keeps or overturns it
([looking back](#looking-back)).

## How the operator answers

An unanswered question shows in four places, and a hold-level one on an
ambition's tree in a fifth:

- The **Ballot**, at `/ballot`. It has no place in the nav bar or the phone
  menu. You reach it from the attention panel's **Answer on the Ballot** row,
  from the waiting strip on the Ambitions page, and with **Go to Ballot** in
  the console. It shows one question at a time,
  the one holding the most work first, as a card: the issue it is on, what it
  holds and its level, its options with the recommended one first, and **Say
  something else** for the rest. A question the loop decided without you is
  drawn differently: see [looking back](#looking-back). **Skip** moves to the next without writing
  anything, and a skipped question comes round again after the last. A switch
  under the header, **By issue**, shows the list instead, which lists every
  waiting question under the issues that enclose it, each issue drawn with the
  first lines of its description above its questions, and a child issue nested
  inside its parent. It answers in place: each question is drawn as the issue
  page draws it, a look-back question with the same **Keep** and **Overturn**
  as the card, with who asked and when, its options as buttons with the
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
- A hold-level question anywhere on an ambition's tree also shows atop [the
  Ambitions page](issues#the-ambitions-page), in the strip above the rows,
  each line linking to the Ballot.

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

## Looking back

A look-back question is one of two kinds, each already decided without you:

- Asked at `merge` on an issue that has since closed, with nobody answering:
  the work went ahead on the recommended option, and the question waited for
  your say ([what an open question blocks](#what-an-open-question-blocks)).
- Asked at `build` or `merge`, answered only by a session - an API key, never a
  person - whatever column its issue stands in, open or closed. A session's
  own answer to a question of its own kind reads the same way, by design: the
  mark is the author's kind, not how the answer was given.

The Ballot draws either apart from an open one. Where a session answered,
*Answered by a session* names it, then the decision - the first line of its
answer - then *Found in*, with the rest of that answer rendered as markdown.
Where nobody answered at all, *Decided without you* names the recommended
option instead. Either way, a line under it says what has happened since: the
column the issue stands in, the day it closed, and whether its pull request
merged or its work landed on the trunk. A clause the board has nothing for is
left out. The pill still reads what it holds, as any question's does - usually
*Holds nothing* once an issue has closed, but not where a session answered one
still open, and still holding live work beneath it.

Two presses, on both Ballot views: the card, and the list by issue.

- **Keep** agrees with the loop. It records the decision - the session's own
  answer where one gave it, else the recommended option - as your own answer,
  exactly as pressing that option on an open question would, and the card
  moves on. It is disabled only where neither a session answered nor an option
  was recommended, since there is then nothing to keep.
- **Overturn** disagrees. It shows the other options, where the question
  offered any, and the box. Choosing one, or typing your own, and pressing
  **Overturn** records that as your answer and files one task to bring the
  work in line. The card names the task as a link, and **Next** moves on. A
  refused overturn shows the server's sentence on the card and keeps what you
  chose and typed.

The task goes under the issue the question was on, or under its nearest
ancestor that can take a task where that issue cannot, since a task cannot
hang under a task. It lands in the leftmost column when a person overturns and
in the agents' column when an API key does. It is [filed from](issues#filed-from)
the issue the question was on.

## Making an answer a principle

Where a question has an ambition above it, at any depth, its answer can become one of that ambition's principles with a press. It shows in two places:

- On the issue page, under an answered question's own answer.
- On the Ballot's look-back card, as a checkbox beside **Keep** and **Overturn**. Ticking it adds the sentence box; pressing either button answers the question first and promotes right after.

Both start with the sentence prefilled from the answer, collapsed to one line, and it is the person's to edit before pressing **Promote** - an answer like `per-node` is not a rule anyone could hold to as written. It is a person's press alone: a key is refused, the way it is refused setting a question's level ([the three levels](#the-three-levels)).

The press appends the sentence as a bullet under the ambition's `## Principles` heading, adding the heading itself where the ambition had none. Success names the ambition - *Now a principle of* and a link; a refusal shows the server's own sentence and keeps what was typed. The ambition's History shows it as `principle_promoted`, beside the `redescribed` row the same edit writes - the two land in the one save, so they read together.

## Where the answer lives afterwards

On the issue, as an answer comment bound to the question it settles, with who
wrote it and when. The question stays in the thread with its options, so the
alternatives that were not taken remain on the record. Which option was taken is
not stored separately: the answer's text is the option's label, and a second
record saying it in numbers could only come to disagree with the first.

A program's answer carries its kind: an answer an API key wrote is marked
*a session* beside its author, on the issue page's own comment thread and
wherever the Ballot draws it. An answer written before the column existed, or
one the board wrote in its own voice, carries no kind and draws no mark.

A later increment on the ticket is handed each answered question with its
answer and who gave it, as settled under *Decisions already made* ([what a
session is told](what-a-session-is-told#decisions-already-made)), and is told
not to ask again. Answering a question once is what stops the next session from
reopening it.

An overturn leaves the same answer comment and nothing more on the question
itself. The rework it files is a separate ticket, [filed
from](issues#filed-from) the issue the question was on — that task, not the
question, is where to look for what an overturn set in motion.

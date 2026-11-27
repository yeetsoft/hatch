---
title: Ambitions
section: Concepts
order: 22
summary: Take an idea to a running ambition in the browser, then every part of how an ambition works.
---

An ambition is the largest aim Hatch tracks: what you are driving at, written as a few capabilities that each say how anyone would tell it is true. Sessions carve epics under it, a pass at a time, and check every capability against the trunk until all of them hold. Take an ambition when the aim is bigger than one epic and you cannot yet say which epics it needs; take an epic when you already know the work.

## From an idea to a running ambition

Every step below is a press in the browser. Each ends with a link to the part further down that holds the detail. A person who works from a terminal has `hatch capability` and `hatch serves` too, and [the CLI reference](cli-reference) lists them.

### Say what you want

Open **Ambitions** on the nav bar, or in the phone menu, and press **New ambition**. Pick the project if the dialog has not already, write what you want in plain words, and add images or videos if they help. Say what you are driving at, not how to build it.

Press **Shape**. Hatch files a draft ambition and opens its page. Only a person can press it, because shaping spends a session. It needs a project with a row on the Playbooks page that shapes ambitions, and says so by name where there is none. The form is described in [Shaping an ambition](dreams#shaping-an-ambition), and [A draft ambition](#a-draft-ambition) says what happens next.

### Read what the session wrote

A session picks the draft up on its own. While it works, the row on the Ambitions page reads Shaping. When it ends, the draft has a title, a description and capabilities, and stands one column on, in the column after the one that shapes it (Backlog on the stock board). The row now reads Ready, and nothing runs yet.

Read all three. Does the title say what you meant? Does the description? Do the capabilities cover it? Edit the description by hand where it is wrong. To have the session try again, move the draft back to the column that shapes it (Breakdown on the stock board).

If every session should keep to some rule, add it under a `## Principles` heading in the description, one bullet each. [Principles](#principles) says how a session reads them.

### Make each capability checkable

On the ambition's page, the Capabilities section lists each capability as two lines: what has to be true, and how anyone would tell. Press **Add a capability** to add one, and use the controls on a row to edit, reorder or remove it.

A good capability is one a stranger could settle by looking at the trunk and saying yes or no. "Search is fast" cannot be settled. "A search over ten thousand issues returns in under a second, and the benchmark test in the repository shows it" can. If the second line reads like a hope, change it until it reads like a check. [Capabilities](#capabilities) has the limits and the rules.

### Give it a budget and a pace

Both sit on the same page. **Epics at once**, in the header card, is how many epics a pass may have open under the ambition. It reads as one where you leave it unset. The **Budget** card takes a dollar amount, which is how much the ambition may cost. A budget is optional, and with none nothing ever counts as spent.

Start small on both. You can change either while the ambition runs. [Budget](#budget) says what happens when it is spent.

### Start it

Press **Start it**, on the Ready row or in the ambition's page header. It asks first, and names how many capabilities a pass will check, how many epics run at once, the budget or that none is set, and the pre-approval level. Confirm, and the ambition moves into the implementation column and reads Running. From then on a pass runs by itself whenever the ambition has room. [A draft ambition](#a-draft-ambition) has the detail of starting it, and [Passes](#passes) says what a pass does.

### Answer the Ballot

A session that cannot decide asks you. A question that holds work on an ambition's tree appears in two places on the Ambitions page: the **Waiting on you** tile at the top, and the **On you** part of the ambition's row. Either opens the Ballot, which shows one question at a time. Press an option, or write your own answer with **Say something else**. [How the operator answers](questions-and-decisions#how-the-operator-answers) says all of what the Ballot offers.

One question to expect is about the budget. When it is spent, a pass asks whether to raise it or stop. Answering "raise the budget" raises nothing: you set the higher amount on the Budget card, and that is what lets the next pass carve.

### Watch it run

The Ambitions page lists every ambition with its stage as a pill and one sentence saying what it needs next. The stages mean this:

- **Shaping**: a session is writing the title, description and capabilities.
- **Draft**: it has no capabilities yet.
- **Ready**: it has capabilities, and waits for you to start it.
- **Running**: it stands in the implementation column, and a pass runs as soon as a runner takes it.
- **Budget spent**: its budget has run out, so it carves nothing until you raise or clear it.
- **Complete**: the newest verdict on every capability is that it holds, so a pass has nothing left to do.
- **Stopped**: it stands past the implementation column, where no pass runs.
- **Done** and **Shelved**: it stands in a closing or a deferred column, and sits under the fold at the foot of the list.

[Stages](#stages) holds the exact rules. Each row also shows its capability bar, the epics open under it and what has been spent, and the ambition's own page lists every pass with what it judged. [Since you last looked](#since-you-last-looked) says how the page tells you what changed while you were away.

### Stop it, or change it while it runs

Four things stop an ambition from carving more work:

- Its budget is spent. The next pass carves nothing and asks you what to do.
- Every capability holds. Carving stops, and a pass still runs to judge again, in case the trunk stops holding.
- You move it out of the implementation column, to a later one. No pass runs there. Move it back in to start passes again.
- It reaches a closing column, or a deferred one.

A spent budget refuses only a new epic: an epic already open, and the work under it, finishes.

To change an ambition that is running, edit it where it stands. Add, edit or remove a capability on its page: a session can only change them before the ambition starts, so this is yours. A session then works out what the change does to each epic under it, and an epic that now serves nothing waits for you to shelve it or keep it running. Raise or clear the budget on the Budget card, and change **Epics at once** in the header card. Either gives the next pass room. [Changing an ambition](#changing-an-ambition) has the detail.

## The parts, in full

What follows is the whole of how an ambition works, one part at a time: its capabilities, the passes that check them, how a draft becomes a running ambition, what a change to one does, its budget, the Ambitions page and its principles. Each part is written to be looked up, so read the walk-through above first.

## Capabilities

A capability is one thing an ambition says has to be true, written as a
statement and a line saying how anyone would tell. An ambition holds an
ordered list of them. Each is a row of its own, not text in the description, so
it can be counted and, later, judged.

Only an ambition takes capabilities. Any other type refuses with a sentence
naming it. Both lines are one line each, up to 300 characters, and neither may
be blank. Adding one appends it, or puts it at the position given and moves the
rest down. Editing changes either line, reordering names every capability in
the wanted order, and removing one closes the gap. The positions always run 1 to
n.

A person can change an ambition's capabilities anywhere on the board. A session
can read them anywhere, but can write them only while the ambition is left of
the implementation column. Once the ambition is in that column, past it, done
or deferred, a session is refused: what an ambition asks for is settled before
the work starts, and changing it after is a person's call.

Each change shows on the ambition's History as its own line: added, edited,
moved or removed. An ambition that holds capabilities cannot be retyped to
anything else until they are removed, and deleting the ambition deletes them.

A session records whether a capability holds, with what was looked at and a
sha, as one of three words: holds, does not hold, or cannot tell. A capability
carries every verdict it has ever been given, and its state - what anyone
reading the ambition sees - is always the newest one, or "not checked" with
none. A key may record a verdict in any column, including past the
implementation line that gates the rest of a capability's row: settling what
an ambition asks for is a person's call, but checking whether it holds is not.
Each verdict shows on the ambition's History too. This is a different thing
from a check session's pass or fail against a live claim, which the board
still calls a verdict elsewhere - the word is already taken, so a capability's
own is never called that on its own.

An epic may serve some of an ambition's capabilities. A person or a session
links an epic to the capabilities it serves, and only to those of the ambition
the epic hangs beneath, however many epics and reflections lie between. The
link reads from both ends: the epic lists what it serves, and each capability
lists the epics that serve it. A session may change the links in any column.
An epic cannot be moved out from under an ambition while it still serves one of
its capabilities; unlink it first. Each change shows on the epic's History as
"serves changed". Deleting the epic or the capability removes the link and
nothing else.

An ambition's own page opens with a line reading "Ambitions / " and its
title, where "Ambitions" links back to [the Ambitions page](#the-ambitions-page)
under the tab's own scope - the one way back to it from an ambition's own
page. Under that, right after Details, is a header card: the stage as a pill
and the sentence under it, the capability bar and its legend, and a row of
facts - the project, **Epics at once** (editable right there, the same field
Details' edit form and read view show under that name for an ambition), how
many passes have run, and the pre-approval level. The stage, the sentence,
the bar and the limit are read narrowed to this one key
(`GET /ambitions?key=`, see [the Ambitions page](#the-ambitions-page)), so
this card and the ambition's row there can never disagree about where it
stands. An ambition's own words - its Description, where "Principles" and
"Out of scope" are headings rather than cards of their own - come straight
after the header card and before Capabilities; no other type moves its
Description. Every other section below keeps the order it always has.

Both show on the issue page. An ambition's page draws a Capabilities
section above Progress: one row per capability, in order, with its statement and
how to tell, its state as one of "Holds", "Does not hold", "Cannot tell" or "Not
checked", what the newest verdict looked at, its sha, its author and its date, and the
epics that serve it. An ambition with none says so and shows the `hatch capability add`
command to start one. An epic's page draws a Serves section. For a person, an
epic that hangs beneath an ambition, at any depth, always draws it, even when
the epic serves nothing: it lists every capability of that ambition with a tick
box, its number, its statement and its state, the ones the epic serves ticked.
Change the ticks and press **Save**, and the whole set is sent at once and
replaces what the epic served; the section then shows the saved set, and the
ambition's Capabilities section and the Ambitions page show it too. A refusal
appears as a sentence in the section and leaves the ticks as they were. Anybody
who is not a person, such as a runner's key, reads the list as before: each
capability the epic serves, with the ambition it belongs to and that
capability's state, with no tick boxes and no Save, and no section at all when
the epic serves nothing. An epic beneath no ambition draws nothing when it
serves nothing, and no other type draws either section. Each state is a pill, green, red, amber or grey, and looks the
same on Serves, on each verdict in Passes and for the outcome in Latest change.

A person signed in, or working at the console with the wall off, can add, edit,
reorder and remove a capability from the Capabilities section itself — a key,
or a browser nobody has signed into, sees the section exactly as above, with no
control on it. **Add a capability** takes a statement and a how-to-tell line and
puts the new row last. **Edit**, on a row, opens both lines in place; **Save**
writes them and **Cancel**, or Escape, restores the row untouched. **Remove**
asks first, naming the epics that serve the capability — removing it unlinks
them — and removes it once confirmed. **Move up** and **Move down** write the
new order at once; the first row has no Move up and the last no Move down.
Each control names its own capability for a screen reader and works from the
keyboard. A refusal from the server shows as its own sentence beside the
control that caused it, and what was typed stays in the field. Reordering
writes no event an ambition's History shows as a change — only an add, an edit
or a remove does — so moving a row is never something a running ambition's
session has to work out the consequences of. On an ambition that has reached
the implementation column, the section says once that a session will work out
what a change does to the epics in flight; the Latest change section below it
shows that change the moment the write lands, with no reload needed. The CLI
door — `hatch capability add` — stays, and is what an ambition with no
capabilities yet shows in place of the form to a key or a signed-out browser.

An ambition's page also draws Epics in flight, between Capabilities and
Progress. It lists each direct child in a column that is neither terminal nor
deferred: the key as a link, the title, the column's name, and the capabilities
the epic serves as numbered badges. One line says how many are open and how many
the ambition runs at once, in the shape "2 open, runs 2 at once"; an ambition
with no limit set runs one. With no open epic the section says so. On a phone the
column and badges stack under the title. No other type draws it.

## Passes

A pass is a session on an ambition standing in the implementation column, aimed
at that column itself rather than the next one. A person starts the first one
by moving the ambition into the implementation column; after that, a pass runs
again on its own whenever the ambition has room. An ambition that has not been
started yet is a [draft](#a-draft-ambition), and a pass never runs on one.

It reads each capability against the trunk and records a verdict — holds, does
not hold, cannot tell — with what it looked at and a sha, the same way any
session records one; see [Capabilities](#capabilities) above.

It carves at most as many epics under the ambition as its own limit allows
(`wipLimit`, read as one where unset), less however many are already open,
each epic saying which capabilities it serves, and leaves the ambition
standing where it is — an ambition is never carried on by a pass, only by the
operator.

Carving stops on its own once every capability's newest verdict is holds: a
key filing or moving an epic under that ambition is refused, with a sentence
saying every capability holds and nothing is left to carve. A person is never
refused this way, and an ambition with no capabilities yet is never refused
either. The pass still runs and judges again each time, since only a pass
finds out that the trunk has stopped holding — it carves nothing and comments
on the ambition that nothing was carved and why.

An ambition's issue page has a Passes section, after Epics in flight, reading
`GET /issues/{key}/passes` on the same tolerant terms as Capabilities: a
failure is a sentence in the section, not the page's error. Every pass is an
entry, newest first. Each is read from one work-log row that ran from the
implementation column and ended worked or with no outcome; its verdicts and
comments are the ones timed between the row's start and end. An entry is
headed by its work-log title (or the word Pass, where the row has none), its
date and the sha it judged against. Each capability it judged is a line with
the position, the state word and, where that word changed from the pass before,
a mark saying so, followed by what was looked at, its sha, its author and its
date.

A row from before the ambition had its type was an epic's pass, so its verdicts
were never recorded as data. Its entry says "Verdicts not recorded as data"
in place of the lines. A verdict timed inside no row's window is listed as its
own entry, "Recorded outside a pass", dated at the verdict's own time. The page
works that out when it reads, and nothing is stored for it. Every entry's own
comments follow, rendered the way the Comments section renders a body. The
verdict lines sit in a grid on a desktop and stack one per line on a phone.

## A draft ambition

A draft ambition is one that stands left of the implementation column and has
not been started. It is sent to the row that shapes it once, and then it waits
for a person to start it.

**Where it stands.** A draft stands in the from-column of the row that shapes
ambitions, Breakdown on a stock board. A person files it into that column, or
presses **Shape it**, drawn on the Ambitions row and on the ambition's own
page header for a draft whose project has such a row. The press is a plain
move into that column - no session and no confirmation - and the row then
reads `shaping`. Where the project has no such row, the press is absent, and
the draft's stage sentence says the two ways on: add capabilities by hand, or
add a row that shapes ambitions on the Playbooks page.

**The row that shapes it.** Hatch sends one session at a draft standing in that
column, to the [row flagged as shaping an
ambition](playbooks#rows-that-shape-ambitions). The session is an ordinary
advance, so it ends with the draft in the row's to-column, Backlog on a stock
board. Only a flagged row on that exact move is matched, and only for an
ambition: an epic, a story, a task or a bug on the same move gets its own row,
never this one. Where the project and the board each have one, the project's wins.

**Where it stands after.** Once the draft stands in the to-column, nothing is
aimed at it. It is not shaped again, and it is never sent to a pass. A draft
with a capability already on it - written by hand, with no session - reads
`ready` the same way, whether or not the project has a shaping row.

**What the queue says.** A draft with no row for its move reads
`waits for a person to start it - move it into "<implementation column>"`,
where the quoted name is the board's implementation column. It no longer asks
for a playbook. Every other type with no row keeps the sentence it had.
An open question on the draft holds it the way it holds any ticket, with the
question's own sentence.

**Starting it.** Starting an ambition is a person's move into the implementation
column, offered as **Start it** on a `ready` row's Ambitions line and on its own
page header. Unlike Shape it, the press asks first: it names how many
capabilities a pass will check, how many epics run at once, the budget or that
none is set, and the pre-approval level, each read fresh when the press is made.
Confirming is the same plain move a drag already makes, and the row then reads
`running`. From there [a pass](#passes) runs exactly as it does for any ambition.
A person who moves a shaped draft back into the from-column gets it shaped
again.

**Both presses are a person's.** Neither is a new route or a new permission -
each is the ordinary move route, gated the way a drag into that column already
is, and a refusal (a protected column, a full one) shows as the server's own
sentence beside the press. A key is offered neither: each is a move a person
may already make by hand, and a key gains nothing from a button standing in
for a drag it was never offered either.

## Changing an ambition

A change is what happened to an ambition's capabilities since the last change
was worked out: every capability added, edited or removed, read as a group
rather than stored on its own. Reordering is never part of a change - it does
not say what anything has to be, only what order the rows sit in.

An epic beneath the ambition can carry an outcome on a change: carries on (the
epic's work still stands), not touched (the change did not reach what this
epic serves) or serves nothing (the change means this epic no longer serves
anything, usually because what it served was removed). Each comes with a line
saying why. An outcome is a record of a session's judgment, not a move - the
epic's own column stays the board's to change, same as a capability's verdict
is a record and never a move either.

This is a different thing from the word outcome on a work log row, which says
whether a session's increment moved its ticket (worked, dropped and the rest) -
the two are never the same thing, the way a capability's verdict and a check
session's are not.

Recording again for the same epic on the same change replaces the outcome and
the why; once a person has decided one, it cannot be recorded over. Each
recording shows on the epic's own History, naming the ambition, the outcome
and the why - not on the ambition's, since the judgment is about the epic.

An epic whose outcome is serves nothing waits for a person, and there are two
presses. Both are a person's only: a key cannot make either, and each answers
once per outcome.

- **Shelve it** moves the epic and the open work under it into the first
  deferred column, the way deferring them by hand would, and records the
  decision. Work already closed or already deferred stays where it is. Whoever
  waits on a shelved issue is told on their own ticket. It follows the same
  access rule as any move into that column, and it needs an epic that is still
  open.
- **Keep it running** records the decision, who made it and when, and moves
  nothing. The epic stays on the board and the outcome is closed.

Each shows on the epic's own History as `change to AER-12: shelved` or `change
to AER-12: kept running`. A shelved epic can still become a dream: the remand
door on the epic's own page is how.

**When the session is sent.** A running ambition, one standing in the
implementation column, is sent to the row marked *works out changes* when its
newest change has not been worked out and at least one open epic beneath it was
filed before that change's newest event. An open epic is a direct child in a
column that is neither done nor shelved. An epic filed after the change was
written with it in view, so it is not weighed, and on its own it sends no
session. A board with no such row sends nothing, and the ambition runs as it
always did.

The session goes ahead of an open question and ahead of the pass. With a change
and a question both waiting, the change is worked out first and the question is
answered on a later look. A full room and a pass that has already run do not
hold it, since working a change out opens no epic.

It is sent once for each change. A session that ends `worked` and names the
change on its work log row closes it, and the ambition reads as it did before.
A row that ended any other way leaves the change waiting, so it is sent again.
A capability added, edited or removed after that is a new change, and sends a
new session. A session that works a change out never counts as a pass, so it
neither settles nor holds the next one. `hatch queue` shows the row as `works
out what a change does to 3 epics`.

**On the ambition's page.** An ambition's page has a Latest change section,
after the Budget card and before Epics in flight, reading
`GET /issues/{key}/changes` on the same tolerant terms Capabilities and
Passes do: a failure is a sentence in the section, not the page's error. An
ambition that was never changed draws no section, and no other type draws
it. Only the newest change is drawn - an older one's own outcomes are never
shown under it.

It opens with what was changed, one line per event: a badge saying Added,
Changed or Removed, then the words - struck through on a removal, the old
ones struck through beside the new ones on a change, plain on an add. A
change not yet worked out says so in one sentence, "This change has not been
worked out yet. A session will work out what it does to each epic.", and
draws no epic lines and no presses even where outcomes already exist, since a
session may still replace them.

Once worked out, one line follows for each epic with an outcome: the key as a
link, the title, and the outcome - carries on, not touched or serves nothing
now - with its why. An outcome of serves nothing, on an epic that is still
open and has no decision yet, carries the two presses the epic's own page
describes: Shelve it, which asks first naming how many open issues move with
it, and Keep it running, which decides at once. Either calls the decision
route and the section reloads. Once decided, the line reads who decided it
and when, in place of the presses; an epic that has since closed or been
deferred by another door shows its column instead. A refusal from the route
is shown as its own sentence beside the presses. On a phone the outcome and
the why stack under the epic's title, and the two presses wrap to fit the
width.

## Budget

An ambition can carry a dollar budget: how much it may cost. It is optional. An ambition has none until a person sets one, and nothing asks for one.

**What it counts.** Spent is the summed cost of every session logged on the ambition and on everything beneath it, at any depth and in
any project, since the ambition began. A session that carried no price adds nothing; the read says how many there were. The budget is
spent when that sum reaches it. An ambition with no budget is never spent, whatever it has cost.

**Who sets it.** Only a person. A key can read the budget but not write it, and no other route, the edit or the bulk edit, takes one.
`PATCH /issues/{key}/budget` sets, changes or clears it: a positive number of dollars to the cent, or an empty string to clear. Only an
ambition takes one; any other type is refused with a sentence naming its type. Each change is on the ambition's trail with the old amount,
the new one and who made it.

**How spent is read.** `GET /issues/{key}/budget` adds the work log up on each read; nothing stores the figure, and the issue itself
carries neither the budget nor the spend, because the issue is read once for every card on the board. The sum is the same whoever reads it,
including a person whose scope hides a project that holds one of the ambition's epics. A session reads it with `hatch api GET`;
there is no `hatch` command for it.

**What happens when it is spent.** Once the budget is spent, a key that files an epic under the ambition, or moves one under it, is
refused. The sentence says the budget is spent and names both amounts: `the budget of AER-12 is spent - $52.00 of $50.00 - so no epic is
filed under it until a person raises it`. A person is never refused, and an ambition with no budget is never refused, whatever it has cost.
An epic already under the ambition may re-send its own parent with no refusal.

The ambition's pass reads the budget before it carves. With the budget spent it carves nothing, asks one question on the ambition and
comments the pass. The question names the budget and the amount spent, and offers two answers: raise the budget, or stop here. The
ambition's pass waits in the queue until a person answers. Answering "raise the budget" raises nothing: the person sets the higher amount
on the ambition, and that change is what lets the next pass carve. Where an answered question already says to stop at this amount, the
pass does not ask again; it still carves nothing and still records its verdicts, so the page shows what holds once the last work lands.
A pass still runs when an epic under the ambition closes, and after "stop here" it files nothing.

**Work in flight finishes.** Only a new epic is refused. An epic already open under the ambition, and the stories and tasks beneath it,
are worked as before. The budget on its own does not fold the pass: one pass has to run to ask the question. A change to the budget opens
room for the next pass, the way a change to Epics at once does.

**On the ambition's page.** An ambition's page has a Budget card, after Capabilities and before Epics in flight, and no other type draws
it. With a budget set it draws a bar, then a bold line such as "41% spent, for 1 of 6 capabilities", the dollars spent and the budget, and
a sentence that at 100% the ambition stops carving and asks, and that work already in flight finishes. The capabilities that hold come from
the Capabilities read the page already makes. Once the budget is spent the card says so in words as well as by the bar's colour. With no
budget it shows the dollars spent so far and says no budget is set; it does not warn. Where some sessions carried no price, it says how
many, so the figure reads as at least that much. A person sets, changes and clears the budget from the same card, and a refusal from the
API is shown beside the field as its own sentence. If the budget cannot be read, the card says so and the rest of the page is untouched.

## The Ambitions page

The Ambitions page, at `/ambitions`, is a top-level link after the magnifying
glass on the nav bar, and in the phone menu, except for a person who made it
their home screen (below): the logo is theirs to Ambitions, and a **Board** link
stands in its place on the bar and in the phone menu. It lists every ambition across every
project, in one read, each with the stage it is at and what it needs next. It
follows the tab's project scope the same way Plan and Ballot do, and nothing is
stored for it.

A person may make this page their home screen, in the **Home screen** card
on the Settings page. The root address (`/`) then opens Ambitions, and the
**Hatch** logo leads here too; the board is the default, and what a person
who has chosen nothing gets. The board is never lost to the choice: it has
its own address, `/board`, which the **Board** link, the console's
**Go to Board** and the queue command all open. A key reads the choice (`home` on `GET /me`)
but cannot change it - `PUT /me/home` takes a person only. See [the board
and its columns](the-board-and-its-columns) and
[the API reference](api-reference).

The heading has a primary press, **New ambition**, on both profiles. It opens a
dialog of the same name, holding a project picker, a box for the words and
**Add images or videos**; the project is already picked where the tab's scope
names exactly one, or where the board has only one. **Shape** files the draft
ambition and opens its page, as on the Dreams page, which is where the form
comes from ([Shaping an ambition](dreams)); a refusal shows as the server's own
sentence and the words stay. The dialog offers no dreams. The press files into
the column where a session shapes the ambition, which the New issue dialog does
not, and it is not drawn for a key: shaping is a person's press.

Above the rows are two tiles, side by side on a desk and one above the
other on a phone. They are the board's own tiles: the same card, the same
small heading and the same large line. Together they start and end where the
rows do. With no ambition in the tab's scope, neither is drawn.

The first tile, **Waiting on you**, is what waits on the reader. Its large
line counts the questions at the hold level anywhere on an ambition's tree,
the ambition itself included: "3 decisions", or "1 decision". Under it, a
sentence says how many open issues those questions hold and how long the
oldest has waited: "Holding 4 open issues. The oldest has waited 9 hours."
An issue held by two questions counts once, and so does a story beneath an
epic whose own question already holds it. Under a minute it reads "The
oldest was just asked." Then comes one line for every question: the
ambition, the key of the issue the question is on, the first words of the
question, and how long it has waited. The four sit on four columns, so two
lines read as a table; the whole line links to the Ballot, under the tab's
scope, and so does the **Open the ballot** link below them. With none
waiting, the tile is one sentence, "Nothing is waiting on you.", with no
large line and no link. Where the read behind the tile fails, the tile says
so in a sentence of its own, and the second tile and the rows still draw.

The second tile, **Capabilities that hold**, counts them: "N of M" is the
capabilities whose newest verdict holds, over every capability of every
ambition in the tab's scope. An ambition in a finished or a shelved column
is left out of both numbers, so a closed ambition does not read as parts done
for good; a scope holding only such ambitions reads "0 of 0". Under it, "2
more in flight. 1 newly holding this week." counts the capabilities not yet
holding that an open epic is working on, and those that hold now whose unbroken run of holds began in the
last seven days. A "does not hold" or "cannot tell" verdict between two
"holds" ends the run, so the later one starts it again.

A row is the ambition's title, linking to the ambition's own page, its
project key and its stage as a pill, and under them one sentence saying what
the ambition needs or waits for. Under that comes the summary: the first
sentence of the ambition's description as plain text, in ink, cut at 200
characters with an ellipsis. It skips what is not prose: comments, fenced
code, headings, tables and rules. A list marker or a `>` is dropped, a link
keeps its words and an image goes. An ambition with no description, or none
that holds a sentence, has no such line. Beside that, "N capabilities" with a bar and a
legend: one segment and one legend entry per count that is not zero, each in
its own colour, the segments in proportion to each other. An ambition with no
capabilities says so instead: "This ambition has no capabilities yet." Last,
"In flight, N of M" (M its own limit, read as one where it has none), then,
with at least one epic open, "N open, runs M at once" and each open epic by
its key, title and column, with the numbers of the capabilities it serves as
badges, or a dash where it serves none. With no epic open the third part
says so instead: "No epic is open under this ambition." With no ambition in the
tab's scope, the page says in two sentences what an ambition is - "An ambition is what you are
driving at, broken into capabilities that are each checked against the trunk
on every pass. A person starts one by saying what they want, and a session
shapes it." - and offers the same press as the heading, in place of a bare
"No ambition in scope." Last comes a fourth part, **On you**, which says what
waits on the reader and what the ambition has cost. It reads "1 decision" or
"N decisions": the open hold-level questions on the ambition and on everything
beneath it that the reader may see. Questions at the build or merge level,
answered ones and ones on closed issues are not counted. Pressing the count
opens the Ballot under the tab's scope; the row does not name the issue a
question holds, and the Ballot does. With none, it says "No decision waits on
you." in muted words. The count ignores the tab's project filter, as the
row's open epics do, so it can count a question in another project that the
tile above and a narrowed Ballot leave out; the same holds for a question
beneath an issue the reader cannot see. Below it is one budget line: "41%
spent" against a budget, "$12.50 spent" with none, and "Budget used up: 104%
spent" once the budget is spent. It leaves out the note about sessions that
carried no price, which the ambition's own page gives.

Each row is a card, and its four parts sit on four columns set once for the
whole list, so the head, the capabilities, the epics and what is on the reader
start at the same place on every row, with their first lines level; in the
epics, the column and the capability numbers also line up from one epic to the
next. On a phone the four parts of a row, and the words of a line in the first
tile, stack one above the other instead of running side by side, and nothing
scrolls sideways.

An ambition's own page draws the same stage pill and sentence, and the same
capability bar and legend, in the header card right after Details - narrowed
to that one key (`GET /ambitions?key=`), so a row here and that card can
never disagree about where the ambition stands. See
[the issue page](#capabilities) above.

### Stages

An ambition's stage is the first of these that is true of it. The server works
it out on each read, from the same rules [the queue](watching-the-loop) uses,
and finds each column by what it is ticked or where it stands, never by its
name.

| Stage | When | The sentence under it |
|---|---|---|
| Done | It stands in a terminal column. | It stands in "*column*", which closes work. |
| Shelved | It stands in a deferred column. | It stands in "*column*" and is set aside until a person moves it. |
| Shaping | It stands in the from-column of the playbook row that shapes ambitions for its project. | It is being shaped into a title, a description and capabilities. |
| Draft | It stands left of the implementation column and has no capability. | It has no capabilities yet. |
| Ready | It stands left of the implementation column and has at least one capability. | Nothing runs until a person starts it - move it into "*implementation column*". |
| Budget spent | It stands in the implementation column and its budget is spent. | Its budget is spent - raise it or clear it before the next pass. |
| Complete | It stands in the implementation column, has capabilities, and the newest verdict on each is "holds". | Every capability holds, so a pass has nothing left to do. |
| Running | It stands in the implementation column. | What the queue says where it is holding the pass for room, or because the last pass took what it could; otherwise: A pass runs as soon as a runner takes it. |
| Stopped | Anything else: it stands past the implementation column, where no pass runs. | It stands in "*column*", and a pass runs only in "*implementation column*". |

A board with no implementation column has no column a pass runs in, so every
ambition there that is not done, shelved or shaping is Stopped, and its
sentence says that. The shaping row is read from the Playbooks page, so a row
that only a repository's own playbook file holds does not make an ambition read
as Shaping.

A running ambition's sentence speaks for the pass's room alone.
[The queue](watching-the-loop) says everything else that can hold a pass, and a
question at the hold level also shows in the strip above the rows.

Rows are grouped by stage under a heading with the stage and its count, such as
"Running, 2", in this order: Budget spent, Running, Stopped, Complete, Ready,
Shaping, Draft. A stage with no ambition has no heading. Done and Shelved
ambitions are folded under one closed line at the foot of the list, "N
finished", which opens to show them. Inside a group, and inside the fold, rows
keep board order: by column, then by rank, then by id.

### The capability bar

The bar's four counts are read afresh on every visit, never stored, and a
capability is counted once, in the first of these that is true of it:

1. **Holds.** Its newest verdict is "holds".
2. **Waiting on a person.** At least one open epic serves it, and every open
   epic that serves it is waiting on a person.
3. **In flight.** At least one open epic serves it (and did not already
   count as waiting on a person above).
4. **Not started.** None of the above - including a capability whose newest
   verdict is "does not hold" or "cannot tell", or one never checked at all,
   as long as no open epic serves it.

An open epic is a direct child of the ambition standing in a column that is
neither terminal nor deferred, the same reckoning [Epics in
flight](#capabilities) above uses. An epic is waiting on a person when any of
three things is true of it: a question at the hold level is open on it or on
an ancestor, the ambition itself included; it is flagged dangerous and some
question, at any level, is open on it or an ancestor; or it is itself held.
A question on a story beneath the epic does not make the epic wait - other
work under it can still move - though that question still shows in the
**Waiting on you** tile above the rows. A column with no playbook is not read as waiting on a person
here; [the queue](watching-the-loop) is where that is said.

For example, an ambition with four capabilities and two open epics might
count: one capability held (its newest verdict is "holds"); one capability
in flight (an open epic serves it, and that epic has no question or hold
against it); one capability waiting on a person (an open epic serves it, and
a hold-level question is open on that epic); and one not started (no open
epic serves it, whatever its own verdict happens to be). The bar draws one
segment each for holds, in flight, waiting and not started, and the legend
beneath it reads "1 hold, 1 in flight, 1 waiting on a person, 1 not
started."

### Since you last looked

Below the rows, **Since you last looked** says what changed under the
ambitions in the tab's scope since your last visit, and how long ago that
was. Each line is its kind, then its words, newest first, and the whole line
is a link:

- **Now holds.** A capability's newest verdict became "holds": its statement,
  the ambition, and what the verdict looked at. It links to the ambition. A
  capability that came to hold and stopped holding in between draws no line.
- **Carved.** An epic was filed under an ambition: its key and title, and
  "serves #1, #2" or "serves no capability yet". It links to the epic. An
  epic that already existed and was moved under an ambition later is not a
  line.
- **Decided without you.** A [question](questions-and-decisions) beneath an
  ambition was answered by a session, or went ahead on its recommendation: the
  key and what the work went ahead on. It links to the Ballot, under the
  tab's scope.
- **Asked you.** A question at the hold level was asked beneath an ambition
  and is still open: the key and the question's first line. Several on one
  issue make one line, "3 questions are waiting". It links to the issue.

The section shows at most 30 lines, and a last line, "and N more", counts the
rest. With nothing changed it is one sentence, "Nothing has changed since you
last looked." On your first visit it covers the last seven days and says so.
On a phone the kind stacks above the words. With no ambition in the tab's
scope it is not drawn, though opening the page still counts as looking. Where
what changed cannot be read, the section says so in a sentence and the rest of
the page still draws.

Opening the page is what counts as looking: there is no press to mark the
lines as read. The visit is recorded once the section has been read and drawn,
and only once for each time you open the page, so changing the scope or coming
back to the tab keeps the lines running from the same instant. A failed read
is not counted as a look. The last visit is kept for you, the person, on the
server and not on the device, so a phone and a desk agree. It is one visit for
the whole page: a look under a narrow scope counts for every project. A key
reads the changes from an instant it names, or the last seven days, and never
records a visit - see [the API reference](api-reference).

## Principles

An ambition's description can hold a `## Principles` heading, with one bullet per principle beneath it. That heading, on an ambition's own description, is the only place anything reads a principle from - an epic that is not an ambition carries none, whatever a heading of the same name on its own description might say.

Every session dispatched on an issue under an ambition, at any depth, is told each principle in its prompt, straight after the ticket's own description - see [the prompt, section by section](what-a-session-is-told#principles). A person can edit the bullets by hand, the way any description is edited, and the next session dispatched reads whatever is there.

A person can also grow the list with a press rather than typing a bullet: promoting an answered question appends its sentence as one, under the heading, adding the heading itself where the ambition had none yet. [Questions and decisions](questions-and-decisions#making-an-answer-a-principle) says where the press sits and what it does.


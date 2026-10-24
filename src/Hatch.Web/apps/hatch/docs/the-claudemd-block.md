---
title: The agent block
section: Reference
order: 50
summary: The block to paste into a repository's `CLAUDE.md` or `AGENTS.md` so its agents know how to reach the board.
---

## What it is and where it goes

The runner spawns an agent session per increment, inside your checkout —
`claude`, `codex`, `grok` or `ollama`, whichever `HATCH_AGENT` names. What that
session knows about the board is whatever your repository's instructions file
says: Claude Code reads `CLAUDE.md`, and Codex and Grok Build read `AGENTS.md`.
Paste the same block into whichever file your agent reads. It is the workflow
contract: which calls to make, what an agent may do to a ticket, what only you
may do, and when to stop and ask rather than guess.

If you use more than one agent, keep the block in `AGENTS.md` and have
`CLAUDE.md` import it with a line containing `@AGENTS.md` (Claude Code's import
syntax), rather than two copies that drift.

Paste it as its own section. The commands in it are the runner's own, so they
work from any directory once `hatch config` has run; nothing in it names a path
in your repository, and nothing in it needs editing to fit one.

That is deliberate. The block says `hatch` and never a path to a script, because
the commands in it are the installed binary's: they work from any directory once
`hatch config` has run, whichever repository the session was spawned in. A path
that exists in only one checkout would be true of exactly one installation, and
the block is for every one of them.

This page is the block's home. Copy it from here; the repository's own
`CLAUDE.md` or `AGENTS.md` is the copy that gets read, and this is the copy
that is kept current.

## The block

Everything inside the fence below is the block, and everything between the
contract marks is what to copy. The first line of it is the heading to paste
under.

<!-- claude-contract:start -->
````markdown
## The operator and agent contract

These rules come in two halves, because this file is loaded on every increment
and most increments only write code. This half carries what every session
needs; `hatch-planning.md` — on your Hatch's own Docs page — carries the shapes
only a session that files or reshapes work does: `parentKey`, the two dates,
the bulk endpoint and the plan reads. Read it on demand.

### Reaching Hatch

`hatch` is the calls a working session actually makes — `board`, `next`,
`queue`, `changelog`, `runners`, `show`, `comments`, `start`, `move`, `comment`, `pr`, `landed`, `verdict`, `depends`, `one-pr`, `ask`,
`questions`, `answer`, `config`, `use`, `work`, `go-to-work`, `do-my-work`, and `api`
for everything else. It finds a column by name rather than by id — on the
letters and digits alone, so `todo` at a terminal reaches the column the board
calls `To Do` — and folds off cards whose ready date has not arrived, exactly
as the board does. `hatch --help` lists the surface and every subcommand
takes `-h` for its own.

Only `work`, `go-to-work`, `do-my-work` and `landed` need a checkout, because
only those four are about a codebase — the checkout you are standing in, or one
named with `--repo` or `HATCH_REPOS` (`landed` reads that checkout's origin
remote, to record the sha a ticket's work reached the trunk at). Every other
call is one request and a sentence about the answer, and `hatch board` from a directory that has never
been a repository is the ordinary case.

`--mine` on `work`, `go-to-work` and `queue` narrows to the caller's own
tickets — assigned to the person the calling key belongs to, or to the key
itself. `hatch do-my-work` is exactly `go-to-work --mine`, for a runner that
should spend its budget on one person's tickets and nobody else's; plain
`go-to-work` still skips every person's own tickets, including the caller's,
exactly as it always has.

Its settings are read in three layers, highest first: an exported `HATCH_BASE`
or `HATCH_KEY`, then `scripts/.env` in the checkout you happen to be
standing in, then the per-user file `hatch config` writes — mode 600, under the
platform's application-data directory, outside every repository. An operator
configures once and every checkout on that machine is reached; a repository that
wants to pin its own origin still can. **The key lives outside the artifact** —
never a tracked file, never a value in a commit, never pasted into an issue.
That is not ordinary secret hygiene: Hatch ships to other operators, and a
credential in the artifact is one operator's credential inherited by everyone
who clones it (`ethos.md`, on your Hatch's own Docs page).

The key is optional. Against a Hatch started with its wall off
(`auth-architecture.md` on your Hatch's own Docs page, "Local mode") there is no
credential to present, and every call names itself with an `X-Hatch-Runner`
header instead — which is a name and not a proof, and only that Hatch reads one.
So a `401` is two different sentences, and says which happened: a key that was
sent and refused is a key to go and look at, and no key at all is a Hatch with
its wall on and nothing to look at yet.

`hatch work` reads `work/next`, then spawns a headless session with the
playbook's prompt, model and effort. It prints the session id first and last
with the agent's resume command beside it, and streams what the run is doing
as it happens — every tool call, a thinking-token pulse, and a heartbeat naming
what it is still waiting on. That last part is not a nicety: the CLI's default
output prints nothing until the run ends, so a four-minute increment was four
minutes of blank terminal indistinguishable from a hang, and the fix for "is it
working" is showing the work, not a spinner.

`hatch go-to-work` is `work` in a circle, and is
`hatch.md`'s "The unattended loop" on your Hatch's own
Docs page — what it may pick up, what it does
about a ticket that did not move, and what it stops for.

`hatch queue` reads the scan and prints it, one issue a line — key, type,
column, and either the reason the pass would fold past it or the transition it
is clear for, in the dispatcher's order: every emergency row first whatever
column it sits in, then every expedited row, then the rest, then every
economy row, then every paused row last, and inside each tier the rightmost
column first and the order the board itself draws that column in. A row
marked `!!` is emergency, one marked `! ` is expedited, one marked `~ ` is
economy — worked only once the account an unattended pass would spend has
usage to spare — and one marked `||` is paused — its own level or one
inherited from an ancestor, the walk does not care which. `hatch queue AER-1`
scopes it to one epic's subtree. It spawns nothing and writes nothing, and an
empty board prints a sentence saying so rather than a blank line: "there is
nothing" and "something went wrong and printed nothing" look identical
otherwise, which is the one thing a run nobody watched cannot afford to be
unsure about.

This is the long form, and it is no longer the only way to see the folds.
`work` and `go-to-work` group the same sentences and print them with a count
each — worst first, one line per distinct reason, so a column of two hundred
cards folded for four reasons is four lines — whenever a pass has an increment
to run or finds nothing at all. A board with nothing on the dispatcher's path
says *that* instead, which is the difference between a finished board and a
jammed one, said without anybody having to run a second command. The idle loop
reprints the digest when it changes and otherwise says it is still alive every
ten minutes; `queue` is where the counts turn back into tickets.

### What an agent does with a ticket

- **Read it.** The description is the brief; the comments, the answered
  questions, and the event trail are the context.
- **Move it to *in progress* before starting.** The board saying what is being
  worked on right now is the board's whole job.
- **Comment the commit sha and the branch, and record the pull request.** The
  ticket is where somebody looks in six months, and a comment naming a commit is
  what makes that search short. The pull request is a field rather than a
  sentence — `hatch pr AER-12 <url>` puts it there, and the issue page draws
  it as something to click. On a project that lands on the trunk there is no
  pull request: the ticket records the sha with `hatch landed AER-12`, not
  `hatch pr`.
- **Name the ticket in the pull request, in two places.** The title is the key,
  one space, then the subject in house style — `AER-12 Auth: the first Admin`,
  with no brackets and no second colon. The first line of the description is
  `[AER-12](<origin>/apps/hatch/issues/AER-12)` and nothing else, then a blank
  line, then the summary. `<origin>` is the address Hatch was reached at
  (`HATCH_BASE`), or the install's public address where it sets one; `hatch work
  AER-12 --dry-run` prints the exact line. Never write a relative link.
- **Plan on the ticket, not in a chat log.** A planning session `PATCH`es
  acceptance criteria into the description and `POST`s the stories or tasks the
  work breaks into. An epic takes stories; a story takes tasks.
- **File the wait, don't write it down.** Something that cannot start until a
  soak test finishes or a renewal window opens gets a `readyAt`, not a sentence
  in a description saying "not until March".

Every one of those calls writes an event carrying the key's name as the actor,
so the trail says who did what without anybody being asked to record it.

### What only the operator does

- **Move work into a terminal column.** Implementation ends in *review*, with a
  comment saying what landed and what did not. Only the operator decides that
  something shipped, and the board enforces it: the dispatcher refuses a
  transition into a terminal column outright.
- **Move work into a protected column.** Only an owner or approver can, by
  hand. A `403` there is that gate working, not a mistake to work around.
- **Edit a playbook.** The API refuses it, and the refusal is deliberate; if a
  playbook is wrong, say so on the ticket and stop.
- **Mint and revoke keys**, and everything else behind a plain `[RequireAdmin]`.
- **Answer a question**, which is the next section.

### When a decision is not the implementer's

Some things a ticket needs are not an implementer's to choose: a product call, a
name that will be lived with for years, a tradeoff with no technically correct
side. The rule is: do not guess, and do not quietly take whichever branch is
cheapest to build. Ask on the ticket, **name the choices**, and stop.

```
hatch ask AER-12 "How should drain retries be scoped?" \
    --recommend "Per-node: one budget each, so a slow node cannot starve the rest" \
    --option   "Global: one budget for the drain, simpler to reason about"
```

The body is the question alone, in a sentence; the tradeoffs go inside the
options they belong to; and `--recommend` marks the one the asker would take, of
which there may be one. A label is short enough to press and reads as a decision
on its own — `per-node`, not `we should scope them per node` — because the label
becomes the answer's own text. One call per question, so each can be answered on
its own. Prose (`ask` with no options) is for the answers that are genuinely
open-ended: a name, a description, a direction.

Then **stop**. An open question blocks the ticket from being dispatched at all,
so nothing further is spawned at it until somebody answers, and anything built
past the question is built on a guess.

The operator answers at a terminal (`hatch answer` walks the open ones one at
a time, serially — a list of six printed at once gets answered in aggregate,
which is how a wrong assumption gets in) or on the issue page. The answer is a
comment bound to its question, so `work` carries the decisions already made into
the next session's prompt under **Decisions already made**. Those are settled;
build on them, and do not reopen them.

What the repository can answer, answer by reading the repository. A question the
code already settles is a round trip through a person for nothing.
````
<!-- claude-contract:end -->

Two things in it point at documentation rather than restating it —
`hatch-planning.md` and `auth-architecture.md` — and both are on your Hatch's
own **Docs** page, under the gear beside this one. Every Hatch image carries the
whole of this documentation, so the sessions your board dispatches can read it
without leaving the machine.

## What each part is for

**Reaching Hatch** is the part that stops a session inventing `curl`. It lists
the commands a working session makes, says which of them need a checkout and
which are one request, and gives the three layers the settings come from: an
exported `HATCH_BASE` or `HATCH_KEY`, then `scripts/.env` in the checkout, then
the per-user file `hatch config` writes. The key lives outside the artifact, so
it is never a tracked file or a value in a commit. It also tells a session how
to read a refusal: a `403` names its own cause, printed from the API's own
answer, so ask the operator; a `401` is either a key that was sent and refused
or no key against a Hatch that wants one. [CLI reference](cli-reference) has
every command, [Environment variables](environment-variables) has the settings,
and [People, roles and keys](people-roles-and-keys) has what a key may reach.

**What an agent does with a ticket** is the routine a session follows once it
has one: move it to in progress before starting, comment the commit sha and the
branch, and record the pull request with `hatch pr`, or the sha it reached the
trunk at with `hatch landed` in a project that lands there. Without those
comments a ticket changes under nobody's eye, and the person who looks six
months later finds code and no account of it. [The lifecycle of a ticket](the-lifecycle-of-a-ticket) is the long form.

**What only the operator does** is the list of things a session must not do,
and why a refusal there is not a fault. A terminal or deferred column, a
protected column, and an edit to a playbook are decisions that belong to a
person, or to the board carrying a pre-approved ticket on with no session. A
`403` on one of them is the gate working, not a misconfiguration to route
around. [The board and its columns](the-board-and-its-columns) explains which
columns are which.

**When a decision is not the implementer's** is what a session does at a choice
that is not its to make: `hatch ask`, one call per question, with the choices
named as options and one of them marked with `--recommend`, and then stop. An
open question blocks the ticket from being dispatched at all, so nothing more is
spawned at it until somebody answers. The answer comes back as a decision in
the next prompt, settled. The `ask` section of [CLI reference](cli-reference)
has the flags.

---
title: What a session is told
section: Running agents
order: 40
summary: The prompt a session is handed, section by section - what each part is for and when it appears - and how the agent CLI is started around it.
---

Every increment's prompt is the **playbook** matched for that column move and
issue type ([playbooks](playbooks)), then a run of sections the runner writes
from the board: the ticket, where the tree stands, what has already been
decided, how to reach Hatch, and where to stop. A session needs nothing it was
not told, because it starts with no memory of the board and nobody to ask. The
playbook text comes first, above a `---` rule; everything under the rule is
the runner's.

To read a real one, run `hatch work AER-12 --dry-run`: it composes the prompt
for that ticket, prints it and exits, claiming nothing and spawning nothing
([one increment and the loop](one-increment-and-the-loop)).

## The prompt, section by section

Each heading below is the heading the prompt itself uses, in the order the
runner writes them. Only some appear on any one prompt; each says when.

### The ticket

Always. The key, the type and the title; the move, which reads as an advance
from one column to another, as resolving conflicts with the trunk, or as fixing
a failing build; the parent and the ready and due dates, where the ticket has
them; and then the description, which is the brief.

### Repositories

When the dispatch has any. A line for each repository the ticket's project
binds: the checkout's path, its remote, and `(primary)` on the one the session
starts in. A repository with no checkout on this machine says so instead of a
path.

### The conflict

On a conflict dispatch, where the ticket's pull request no longer merges with
the trunk. The branch and the trunk with both shas, and the files that
conflict. It says what the conflict is and leaves what to do about it to the
playbook.

### The failing build

On a build dispatch, where the pull request's checks failed. For each
repository, the branch and the sha, then each failing check with its link and
the lines of its log that lead up to the failure. That is what makes a failure
fixable when it does not reproduce on this machine. The logs share a cap of
40 KB; past it a check's log is left out, with the command that prints it.

### The last check

When a check in the ticket's history failed and this dispatch is the
implementation column picking it back up. The check's findings, fenced, under
the same 40 KB cap.

### The branch

When the runner has entered, or decided not to enter, a branch. It opens by
saying it **overrides** whatever the playbook says about which branch to start
from or whether to cut one: a stock playbook still tells a session to cut a
branch from the trunk, and this is the correction. Then one line per
repository, in whichever of these states the tree is in:

- **One branch, entered.** The session is on origin's branch for this ticket,
  with how far ahead of the trunk it is. The trunk has been merged into it, and
  the section says what came of that: nothing to merge, a clean merge that is
  committed and not pushed, or a merge **left in progress with conflicts**,
  with the files named, to be resolved and committed before anything else. If
  this machine's copy was ahead of origin's, or had diverged, that is said too.
  Continue on it, and push to it.
- **No branch.** The tree is on the trunk at origin's tip, with a name to cut.
  Cut it, push as the work goes, and open a pull request before the increment
  ends.
- **Already merged.** The branch for this ticket has already merged into the
  trunk. The tree is on the trunk, with a new name to cut.
- **Several.** Origin holds more than one unmerged branch for the ticket. The
  tree is on the trunk, and the answer under *Decisions already made* says
  which to use.
- **A shared story's branch.** A task of a one-pull-request story works on the
  story's branch, not one named for itself. It pushes there and opens no pull
  request; the story's closeout does.
- **A project that lands on the trunk.** The tree is on the trunk at origin's
  tip: commit there and cut nothing.
- **The runner could not enter it.** The tree is on the trunk and the section
  says what went wrong.

What the runner does to a checkout, and why, is
[one increment and the loop](one-increment-and-the-loop#what-it-does-to-your-checkout).

### Its children

When the ticket has any. Each child's key, type, title and the column it is in
now, so a session working an epic or a story sees where its parts stand
without asking the board.

### This ticket was let go

When one or more increments in a row ended on this ticket without moving it,
and were let go quietly rather than flagged. The comments say why and what each
left behind. A session is told to read them and continue that work, not start
over.

### This ticket stalled

When a person, or the board itself, marked the ticket stalled directly. It
carries the reason that was given, and says once whether it has stalled once or
several times in a row. It stands in for a question and an answer nobody typed
into the comment thread.

### This ticket was sent back

When the brief was over the playbook's size limit for a move and the ticket was
sent back unanalysed. A ticket that can take children is told to cut the work
into child tasks, each under the limit. One that cannot is told to trim its
description to the slice it keeps and file the excess as new tasks under the
same parent.

These three sections are independent: each appears under its own trigger, and a
ticket can carry any of them, or none.

### Decisions already made

When questions on the ticket have been answered ([questions and
decisions](questions-and-decisions)). Each question with each answer and who
gave it, introduced as settled. The one thing a session must not do is reopen a
question somebody has already answered, so the answers are carried here rather
than left for it to find in the thread.

### Said to you since the last session

When a person left the agent a message while no session was running on the
ticket. A message sent mid-run reaches that run another way; one nothing read
is carried here instead, so it is neither lost nor delivered twice. Each says
who sent it and when, and asks the session to say on the ticket if it changes
its plan.

### Reaching Hatch

Always. The `hatch` commands a session makes, to be run from the repository
root: `comments`, `start`, `move`, `comment`, `ask`, and `api` for everything
else. The key is already in the environment, and the brief above is already
current, so there is no `show`. Two things change it:

- On a check's column, `hatch verdict` stands where `start` and `move` would,
  because a check ends with a verdict, not a move.
- With several repositories in play, it names which checkout is the root.

### Landing on the trunk

For a project that lands work straight on the trunk, in place of a pull request
section ([review, pull requests and the merge
train](review-pull-requests-and-the-merge-train)). There is no pull request:
merge the trunk in, never rebase, push, never force, and record the landing
with `hatch landed`. The increment is not done until it is recorded.

### Opening a pull request

Where the work goes to review on a branch. It gives the title form (the key,
then the subject as an area and a sentence) and the exact first line of the
description, a link to the ticket, and ends with `hatch pr` to record the pull
request on the ticket. In a one-pull-request story it adds that the closeout
increment, once the tasks are all pushed, opens the one pull request for all of
them.

### No pull request from this ticket

For a task on a shared story branch, in place of the pull request section.
Push to the branch and move the ticket on: it waits, with nothing of its own to
merge, until the story's pull request does.

Exactly one of *Landing on the trunk*, *Opening a pull request* and *No pull
request from this ticket* appears, always.

### When you cannot decide

Always. Some things are not the session's to choose, and it is told not to
guess. It asks with `hatch ask`, naming the choices: `--recommend` for the one
it would take, `--option` for each alternative, one call for each question.
Then it stops. An open question blocks the ticket from being dispatched at all,
so nothing more is spawned at it until someone answers ([questions and
decisions](questions-and-decisions)). What the repository can answer, the
session answers by reading it.

### This ticket is express

Only on an express ticket ([issues](issues) has the levels). The loop carries
it on past any column marked *Express skips* with no session and no spend, and
an unanswered question is what stops that.

### This ticket is pre-approved

Only on a pre-approved ticket. Past what express does, once the ticket is in
review with a green, current pull request recorded, the loop merges it with
nobody reading the diff first. So the session is told to read its own diff
before it opens the pull request: that is the only reading it gets.

### Where this increment ends

Always. The column to stop in, or on a check the pass column and the fail
column, since a check ends with a verdict. No moves into a terminal column,
which only the operator makes. An increment that leaves the ticket where it
found it is recorded as having done nothing. No playbook edits: the API
refuses them on purpose, and a wrong playbook is said on the ticket instead.

The prompt then ends with two things that are plain paragraphs, not sections.
A ticket moving into review may be asked for a release note, and every prompt
asks for a `work-log` block saying what the session did. Both have their own
pages, beside the hooks and the inbox.

## How the session runs

The prompt is one half of it; the other is how the agent CLI is started. For
`claude`, the default:

- It runs **headless**, with permission prompts bypassed, since nobody is there
  to answer one and an unanticipated prompt would become a silent denial in the
  middle of a run no one is watching. That is a deliberate grant, which is why
  `hatch work` is something you type rather than something a scheduler does.
- The **model and effort** are the playbook's, with the issue's own overrides
  already folded in.
- Each repository other than the primary is added to the session as a
  directory, so it can read and edit the sibling checkouts.
- Each tool the playbook denies is passed as a denied tool, one per entry.
- `hatch` is first on its `PATH` and is the same binary that spawned it, already
  pointed at the same board and named as the runner that dispatched it.

The playbook's **budget** is never in the prompt. A session that crosses it is
told once, mid-run, to wrap up.

`codex` and `grok` differ. Codex has no bypass flag of its own: a denied `Edit`,
`Write` or `NotebookEdit` downgrades it to a read-only sandbox instead, and any
other denied tool it refuses outright, having no finer control. It carries no
hooks. Grok likewise carries no hooks, since the CLI loads them only from
fixed, trust-gated paths the runner cannot safely redirect.

Beyond the prompt, what the session knows is the repository's own `CLAUDE.md`
([the CLAUDE.md block](the-claudemd-block)).

## Coming

Pages this one names that are not written yet:

- `playbooks`
- `questions-and-decisions`
- `review-pull-requests-and-the-merge-train`

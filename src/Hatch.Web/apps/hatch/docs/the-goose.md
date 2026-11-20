---
title: The goose
section: Running agents
order: 55
summary: The server's own sweep of the board, every minute, and how to read the record it keeps.
---

The goose is Hatch's watcher. Every minute the server looks over the board by
itself, with no runner working and no browser open, and writes down that it
did. When the goose is running, the record grows or its newest row's last
sweep stays recent. When it is not, nothing else on the board will say so.

## What a sweep is

A sweep asks each of the goose's checks two things: what did you look at, and
what did you conclude? Each answer is a line of plain text. The checks run in a
fixed order, one after another.

A check that fails does not stop the sweep. Its entry says it failed and
carries the error text, and the checks after it still run.

A sweep with no checks at all - were every one ever removed - still records
that it ran and nothing more. That is still the proof the watcher is alive.

Only one sweep runs at a time, even with several servers on one database. A
sweep that starts while another is running writes nothing. A sweep still
running when the next is due means the next one is skipped, not queued.

## The page

The **Goose** page, at `/goose` and in the **Agents** group of the nav, is
where all of this is read. You do not need the API for it. From top to bottom:

- **A line saying whether the goose is running** and when it last swept. A
  stopped goose says so in the attention panel's own words.
- **Open findings**, longest-standing first. A row says what is wrong, which
  check found it, its project, and how long ago the condition began.
- **Runs**, newest first. A row says when the run first and last ran, how many
  sweeps it holds, how long the slowest took, how many checks ran (and how many
  of them failed), and how many findings were open at its last sweep. The last
  sweep, because a run's entries are the latest sweep's.
- **Closed findings**, the one that closed last first. A row says how long the
  finding lasted and when it closed.

Press a run to open it in place: each check, with what it looked at and
concluded, or the error it failed with. Press a finding to open it: its
evidence, one label and value to a line.

An open or closed finding has the same **File a bug** button the attention
panel has, and shows the bug's key once one is filed. Opened, a finding that
has a bug also shows the bug's key and column, the brief it was filed with, and
what each session on it reported, newest first: the title, the summary, when,
and which runner. Nothing is copied to the finding. The brief is the bug's own
description, the reports are its [work log](watching-the-loop), and the runner
is read from the event that released each session. A session from before a
release named its runner shows none.

The page reads again every minute, the attention control's own interval. A
caller who reaches only some of the projects sees the findings of their
projects and no runs, with one line saying that runs cover the whole board.
Each empty list says so: no runs yet, nothing open, nothing closed in the days
kept.

## How to read a run

The record is a list of **runs**, not a list of sweeps. A sweep that concluded
exactly what the one before it did is folded into that run instead of adding a
row, so a quiet board is one row however long it stays quiet. Each run shows:

- **First ran** and **last ran**: when its first and latest sweeps started.
  A run whose last sweep is recent is the goose still running now.
- **Sweeps**: how many sweeps it holds. Sixty in an hour is the goose keeping
  its pace.
- **Longest**: the slowest of those sweeps, in milliseconds. The page writes it
  as a duration, such as `340 ms` or `1.2 s`.

Open a run to see its entries: one per check, in the order they ran, with what
the check looked at, what it concluded, and whether it failed. The entries are
the latest sweep's. What a check looked at can change between folded sweeps
(three issues, then four); only a different conclusion, or a check that starts
or stops failing, starts a new run. How long a sweep took never does.

A new row, then, means something changed. The newest row's last-ran time is
how fresh the watcher is.

## Settings

All seven are server settings, set in the environment like any other
([Deployment overview](deployment-overview)):

| Variable | Default | Meaning |
|---|---|---|
| `Hatch__GooseIntervalSeconds` | `60` | Seconds between sweeps. Zero or less falls back to `60`. |
| `Hatch__GooseKeepDays` | `7` | Days a run is kept after its last sweep. `0` keeps everything. Less than that falls back to `7`. |
| `Hatch__GooseTrainReadyMinutes` | `30` | How long a merge train's head may be judged mergeable before the `train-ready` check below opens a finding. `0` turns the check off; less than that falls back to `30`. |
| `Hatch__GooseUnclaimedMinutes` | `10` | How long a project may have ready work and a live, idle runner able to take some of it before the `ready-unclaimed` check below opens a finding. `0` turns the check off; a negative value falls back to `10`. |
| `Hatch__GooseNoRunnerMinutes` | `15` | How long a project may have ready work that no live runner can take before the `ready-no-runner` check below opens a finding. `0` turns the check off; a negative value falls back to `15`. |
| `Hatch__GooseRepeatClaims` | `5` | How many claims by one runner on one ticket, with the ticket not moving, make the `claim-repeating` check below open a finding. `0` turns the check off; less than that falls back to `5`. |
| `Hatch__GooseQuestionHours` | `24` | How long a question may wait before the `questions-waiting` check below opens a finding. `0` turns the check off; less than that falls back to `24`. |

Each sweep also deletes the runs whose last sweep is older than the keep
window.

## Findings

Some checks do more than conclude a sentence: they watch for a condition that
has stood too long, and open a **finding** - a stored row with its own
sentence and evidence, kept until a sweep stops seeing the condition.

A finding carries which check opened it, what it is about (a project, usually),
when the condition it reports began, when the finding itself opened (once the
condition had stood the check's own bound), when it closed, and the bug filed
from it, if any has been. At most one finding is open at a time for a given
check and subject. A check that opens one may still refresh its sentence and
evidence on every later sweep the condition is still true; the two times it
began and opened never change once set. A closed finding is kept for the same
`Hatch__GooseKeepDays` window a run is, and only once its bug, if it has one,
has landed in a terminal column - an open finding is never deleted.

The goose never files that bug. A person does, by handing the finding to an
agent with `POST /api/goose/findings/{id}/bug`: it files one bug, in the
finding's own project, and stores it on the finding. The bug's title names the
problem, and its description is the brief - the sentence, when the condition
began, the evidence written out one line each, the check and its bound, and one
line asking for the cause and then either a fix or what a person has to do. A
finding that has already cleared can still be handed over, and its brief says
so. A second call answers the bug already filed. A finding that names no project
needs a `projectKey` in the call. No check counts a bug filed this way, so a
finding never watches its own fix.

### `train-ready`

Watches every project's merge train for a head - the row the train is acting
on next - that has been ready to merge, or needed its branch brought current,
for longer than `Hatch__GooseTrainReadyMinutes`. This is a second clock beside
the board's own stall watch, which only ever passes the driver over to another
runner: this one opens a finding a person can read even when there is no other
runner to hand the head to. The finding's sentence names the project and the
head, for example *"AER's merge train is standing: AER-12 is ready to merge
and has not"*; the evidence beside it names the head, the driver (or that none
is eligible), when the driver last acted in the project's merges column, how
many pull requests wait behind the head, and how many times a driver has
already been passed over on it.

### `ready-unclaimed` and `ready-no-runner`

A person is told once a project's ready work has sat unclaimed - once while a
runner that could take it sits idle, and once while no live runner can take it
at all. Both read the same board the dispatcher does and add no rule of their
own about what is ready: a row the board's own scan calls clear, or parked
only behind higher-ranking work, that is not a hop and is not the merge
train's. Ready work filed as the bug for another finding is left out of both,
so a finding does not watch its own fix.

The two never both watch one project at once: a project with an idle taker is
`ready-unclaimed`'s to watch, and one with none at all is `ready-no-runner`'s.
Unlike the **Runner capacity** and **Waiting on a runner** sections of the
attention panel, which only ever say what is true right now, these say a
project has stood that way for a while - each has its own bound, below, and
neither opens until a project has stood past it.

`ready-unclaimed` opens once a project has ready work, a live runner able to
take some of it is idle, and nothing in the project has been claimed since the
condition began, for longer than `Hatch__GooseUnclaimedMinutes`. A claim taken
anywhere in the project restarts its clock, even one taken and released again
between two sweeps. Its sentence names the project and the count, for example
*"AER has 3 steps ready and no runner has taken one"*; the evidence beside it
names the steps (the first ten keys, and the count), each idle runner able to
take one and when it was last heard from, and the project's newest claim, or
that it has none.

`ready-no-runner` opens once a project has ready work that no live runner can
take at all - including a project with no live runner whatsoever - for longer
than `Hatch__GooseNoRunnerMinutes`. Its sentence reads *"AER has 3 steps ready
and no live runner can take them"*; the evidence names the steps the same way,
then, for each live runner, why it cannot take them, in the same words the
queue uses. With no live runner at all it says so, and lists when each known
runner - live or not - was last heard from.

### `claim-repeating`

Watches for one runner claiming one ticket again and again while the ticket
does not move. The board already counts the releases it can read as a runner
letting go, but it skips several ways a release can end - the agent never
started, the loop died, the ticket could not be served - so a runner can take
the same ticket many times and nothing else says so. This check counts the
claims instead, whatever outcome each release carried.

A **streak** is one runner's claims on one ticket, newest first, back to the
first thing that shows the ticket moved: a column change, a new commit seen on
its pull request or its build, or a claim by another runner. A finding opens
when a streak holds at least `Hatch__GooseRepeatClaims` claims and its newest
claim is under two hours old. It closes when the streak breaks, or when two
hours pass with no further claim. A ticket in a done or deferred column, and
one filed from a finding, is left alone.

The sentence names the runner, the ticket and the count, for example *"here:/one
has claimed AER-12 7 times and it has not moved"*. The evidence names the
runner, the number of claims, the first and the newest, how many releases
carried each outcome, the ticket's column, and whether it carries a stall mark
or a hold. Its first-claim time is when the finding says the condition began.

A new commit shows only once a pull request or a build verdict exists for it. A
push made before either leaves no mark on the ticket, so the check cannot see
it, and a runner that pushes every time without opening a pull request can
still read as a streak.

The check only says what it saw. It does not pause the runner, stall-mark the
ticket or change what is counted against it. If you see one, see [a runner
keeps claiming one ticket](troubleshooting#a-runner-keeps-claiming-one-ticket).

### `questions-waiting`

Watches the questions still owed a person - the same ones the Ballot and the
attention panel list, so a question on a done issue is never counted - and
opens one finding when at least one was asked `Hatch__GooseQuestionHours` ago
or longer. It is about the board and names no project, so there is at most one
such finding open, and only a caller who may see everything reads it. A
question on an issue filed from a finding is left out.

The sentence gives the count and the bound, for example *"3 questions have
waited more than 24 hours"*, and no duration. The evidence lists every question
past the bound, oldest first: the issue's key and title, who asked, when, and
the first line of the question. As questions are answered and others pass the
bound, it stays one finding with fresh evidence; it closes when none is past
the bound. It judges by age alone: it suggests no answer and answers nothing.
The condition begins at the first sweep that sees a question past the bound,
so its start can trail the question by up to one sweep interval.

### How a finding reaches the panel

An open finding is listed first under **Findings** in the Human group of the
[attention panel](watching-the-loop#what-is-waiting-on-you), longest-standing
first. Each row is the sentence and how long the condition has been true. It
links to the issue the finding is about when its subject is an issue in the
finding's own project - for `train-ready`, the head - and to the project's board
otherwise. The bug filed from it, if any, is a link of its own, and so is **Goose**,
which opens [the page](#the-page) where the finding is read in full. Every open
finding is counted by the bar's control, which lights for it.

### When the goose stops

The panel also tells you when the goose itself has stopped, since a goose that
is not sweeping opens and closes nothing, and the list under it can go stale.
The goose has stopped when the newest run's last sweep is older than three
intervals, or, if no run is stored, when the server has been up for three
intervals. The page reading it judges this by its own clock; a sweep never
writes it, so a sweep that is wedged cannot say it is fine. The first row under
**Findings** then reads *The goose has stopped. It last ran 12 minutes ago.*, or
*It has not run since the server started.*, and counts as one thing waiting. The
row links to [the page](#the-page). It is shown to everyone, whatever their scope, because a person who cannot see a
run can still be hurt by there being none.

## Reading it from outside

The page reads three routes. `GET /api/goose/runs` lists runs, newest first,
without the per-check detail but with how many checks each holds
(`checks`) and how many of them failed (`checksFailed`), and takes a `limit`. `GET /api/goose/runs/{id}` answers one run with its
entries. `GET /api/goose/findings` lists findings, open ones first
(longest-standing first), then closed ones (newest first); `?open=true`
narrows it to the open ones. An API key may call all three; filing a bug from a finding is a person's press, and a key is refused it. A run holds what
was seen in every project, so a caller who may see only some projects gets an
empty list and reads every run as not found; a finding instead follows the
same scoping as the rest of the board - one naming a project is left out for a
caller who cannot see it, and one naming none is only in an unbounded caller's
list. See the [API reference](api-reference). `GET /api/attention` carries the open
findings and the goose's own state as well, so the panel needs no second
request.

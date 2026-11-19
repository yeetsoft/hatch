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

## How to read a run

The record is a list of **runs**, not a list of sweeps. A sweep that concluded
exactly what the one before it did is folded into that run instead of adding a
row, so a quiet board is one row however long it stays quiet. Each run shows:

- **First ran** and **last ran**: when its first and latest sweeps started.
  A run whose last sweep is recent is the goose still running now.
- **Sweeps**: how many sweeps it holds. Sixty in an hour is the goose keeping
  its pace.
- **Longest**: the slowest of those sweeps, in milliseconds.

Open a run to see its entries: one per check, in the order they ran, with what
the check looked at, what it concluded, and whether it failed. The entries are
the latest sweep's. What a check looked at can change between folded sweeps
(three issues, then four); only a different conclusion, or a check that starts
or stops failing, starts a new run. How long a sweep took never does.

A new row, then, means something changed. The newest row's last-ran time is
how fresh the watcher is.

## Settings

All four are server settings, set in the environment like any other
([Deployment overview](deployment-overview)):

| Variable | Default | Meaning |
|---|---|---|
| `Hatch__GooseIntervalSeconds` | `60` | Seconds between sweeps. Zero or less falls back to `60`. |
| `Hatch__GooseKeepDays` | `7` | Days a run is kept after its last sweep. `0` keeps everything. Less than that falls back to `7`. |
| `Hatch__GooseTrainReadyMinutes` | `30` | How long a merge train's head may be judged mergeable before the `train-ready` check below opens a finding. `0` turns the check off; less than that falls back to `30`. |
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

## Reading it from outside

`GET /api/goose/runs` lists runs, newest first, without the per-check detail,
and takes a `limit`. `GET /api/goose/runs/{id}` answers one run with its
entries. `GET /api/goose/findings` lists findings, open ones first
(longest-standing first), then closed ones (newest first); `?open=true`
narrows it to the open ones. An API key may call all three. A run holds what
was seen in every project, so a caller who may see only some projects gets an
empty list and reads every run as not found; a finding instead follows the
same scoping as the rest of the board - one naming a project is left out for a
caller who cannot see it, and one naming none is only in an unbounded caller's
list. See the [API reference](api-reference).

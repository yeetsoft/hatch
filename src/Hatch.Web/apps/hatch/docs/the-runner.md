---
title: The runner
section: Running agents
order: 10
summary: Getting the runner onto your machine, pointing it at a board, where its settings and its name live, and the commands that read and drive the board by hand.
---

The board is a board; the **runner** is what works the tickets. It is one
binary, `hatch`, it runs on your machine, and it talks to this Hatch over HTTP.
It works tickets, one increment or a loop of them ([one increment and the
loop](one-increment-and-the-loop)), and it reads and drives the board from a
terminal. The same runner as a container is *The container runner*.

## Getting it

**Agents → Get the runner** ([`/apps/hatch/runner`](/apps/hatch/runner)) offers
this platform's download first, with the others underneath for the machine you
are not sitting at, and prints the revision it was built from. That is the same
build as the page serving it, so the two are never a version apart.

Put the file on your `PATH` as `hatch` (`hatch.exe` on Windows). On macOS and
Linux it needs marking executable first:

```
chmod +x hatch
```

It needs `git`, and the agent CLI it spawns, logged in: `claude` by default,
or `codex` or `grok` when `HATCH_AGENT` or `--agent` says so. In a checkout of
Hatch itself, `scripts/hatch.sh` (`scripts\hatch.ps1` in PowerShell) finds or
builds the binary and hands over, so nothing needs installing first.

## Pointing it at a board

Bare `hatch config` asks, in order, for the origin, the key (read without
echo), the agent, the path of that agent's CLI, and, inside a checkout, the
name the board should call this runner. Enter keeps what is shown in brackets.
It writes the answers to a per-user file, so it follows you between checkouts
and you do it once.

Each flag writes one thing and asks nothing:

- `--origin <origin>` writes the origin alone, leaving the key and everything
  else as it was. It is what the Runner page's first line pastes.
- `--key <key>` writes the key alone, and needs an origin already set. A key on
  a command line stays in shell history; the prompt does not, so prefer the
  prompt on a machine other people can read.
- `--runner <name>` sets what this checkout calls itself on the board; see
  *The runner's name* below.
- `--repo <path>` also serves that checkout, for a loop with no checkout of its
  own. It is repeatable; each path must exist and carry a `.git`, or nothing is
  written. Bare `--repo` clears them.
- `--workspace <dir>` names a directory this runner owns entirely, where it
  clones every repository the board binds that it has no checkout of.
  `--workspace off` never clones. A workspace may not sit inside a named
  checkout, nor a named checkout inside it: a clone under a tree being reset is
  two owners of one path.

An origin or key write is then tried against the board. If that call does not
go through, the file is still written, so there is something to fix, and the
command exits 1. With the wall off, the key may be blank; with the wall up,
the runner needs one (*People, roles and keys*).

## Three layers

Every setting is read from three places, highest first:

1. an exported variable;
2. `scripts/.env` in the checkout you are standing in;
3. the per-user file `hatch config` writes.

`hatch config --show` prints where that file is, so no path is written here.
It is mode 600 wherever the platform has modes; on Windows it inherits the
directory's ACL. A blank value is the same as unset. A flag beats every layer
for its run: `--repo` on `work` or `go-to-work` replaces `HATCH_REPOS` rather
than adding to it.

Five settings are read from the environment only: `HATCH_HEARTBEAT`,
`HATCH_RETRY_SECONDS`, `HATCH_ROOT`, `HATCH_NIGHT_STATE` and `HATCH_RTK`. A
file line naming one of them is ignored, with a warning that says so.
`HATCH_BASE_BRANCH` has no `config` flag: export it, or put it in
`scripts/.env`.

These are the settings `hatch --help` lists, with their defaults:

```
HATCH_BASE           the origin of your Hatch
HATCH_KEY            hatch_ak_...; optional against a Hatch with its wall off
HATCH_CLAUDE_BIN     the claude CLI, if it is not on PATH
HATCH_CODEX_BIN      the codex CLI, if it is not on PATH
HATCH_GROK_BIN       the grok CLI, if it is not on PATH
HATCH_AGENT          claude, codex or grok (default: claude)
HATCH_BASE_BRANCH    the trunk go-to-work resets to between increments
HATCH_RUNNER         what the board calls this runner (default: a cast-list
                     character, chosen once per checkout)
HATCH_ROOT           the checkout to work in (default: upwards from here)
HATCH_REPOS          checkouts a loop with no checkout of its own serves,
                     joined on : (Unix) or ; (Windows)
HATCH_WORKSPACE      where it clones what the board binds (unset: a per-user
                     default; off: never clone)
HATCH_HEARTBEAT      seconds of silence before the renderer says what it is
                     waiting on (default: 20)
HATCH_RETRY_SECONDS  seconds a call keeps retrying Hatch (default: 90; 0 for
                     one try)
HATCH_NIGHT_STATE    where a night's totals are handed to the loop that
                     restarts into
HATCH_RTK            off keeps RTK's Bash hook out of a session where rtk is
                     on PATH
```

*Environment variables* has the full table.

## What --show prints

`hatch config --show` is the way to see all of that at once. It prints:

- the per-user file's path, and whether it exists yet, and the path of this
  checkout's `scripts/.env`;
- the runner's name, and where it came from;
- `HATCH_RETRY_SECONDS`, with its layer;
- each setting that can live in a file, with the layer it came from: exported,
  the `.env` path, or the per-user file;
- the key masked, never in full;
- `HATCH_REPOS` as a path a line;
- for an unset `HATCH_WORKSPACE`, the directory clones would go to.

## The runner's name

The board shows each runner by name, under **Agents → Runners** (*Watching the
loop*). The name is `HATCH_RUNNER` if that is set; otherwise the name recorded
for this checkout; otherwise a cast-list character, picked once from the host
and the path, skipping names already recorded here or live elsewhere, and
kept from then on.

`hatch config --runner <name>` refuses a name that is empty, too long, not
plain ASCII, or already a live runner's elsewhere, and writes nothing. It also
needs a checkout: a name belongs to a checkout, not to a person.

## Where the key never goes

The key stays out of the artifact: never a tracked file, a commit, a plan, an
issue or a paste. `scripts/.env` is git-ignored for that reason, and the
`hatch_ak_` prefix exists so that one which does leak is recognisable on
sight. The per-user file sits outside every repository. The
[CLAUDE.md block](the-claudemd-block) tells an agent the same.

## Reading the board, spending nothing

Only `work`, `go-to-work` and `do-my-work` need a checkout or a workspace to
clone into. Everything here is one request and a sentence about the answer, so
`hatch board` from anywhere is the ordinary case.

```
hatch board                       the columns, and how many cards in each
hatch queue                       every card a pass would look at, in the order it looks
hatch queue AER-1                 ...under one epic
hatch queue --mine                ...only your own
hatch next                        top workable card of "todo"
hatch next "in progress"          ...or of any column
hatch show AER-12                 the brief, plus its comments
hatch comments AER-12             the comments, without the brief
hatch questions                   everything waiting on an answer
hatch questions AER-12            ...or just this ticket's
hatch changelog                   what shipped, as markdown
hatch changelog --days 30 --before 2026-09-01 --project AER --json
```

`hatch queue` is the dry run for the loop, and the answer to "why did it not
pick up the ticket I meant": each line is a key, a type, a column, and either
the reason the pass would fold past it or the transition it is clear for. It
spawns nothing and writes nothing. With `--mine` it shows what a `--mine` pass
would take, and folds past everything else, naming whose it is. An empty board
says so in a sentence rather than printing a blank line: "there is nothing" and
"something went wrong and printed nothing" look identical otherwise.

`hatch changelog` prints a heading per day and a bullet per feature, with an
*Also* list of that day's fixes. `--days <n>` is the newest *n* days that
shipped something, not the last *n* calendar days; seven by default. `--before`
moves the window back to end before a date, `--project` narrows it to one key
and refuses a key nobody has, and `--json` prints the endpoint's own answer and
nothing else. A window with nothing in it prints one sentence saying so.

Columns are found by name rather than by id, on the letters and digits alone,
so `todo` reaches the column the board calls `To Do`. `hatch next` takes the
same.

## Driving a ticket by hand

```
hatch start AER-12                move it to "in progress"
hatch move AER-12 todo            ...or to any non-terminal column
hatch comment AER-12 "sha abc123 on branch aer-12-thing"
hatch pr AER-12                   where it is being reviewed
hatch pr AER-12 https://...       ...or say where, having opened one
hatch pr AER-12 --clear           ...or take it off the one it has
hatch landed AER-12 [<sha>] [--repo <path>]    on a project that lands on the trunk, the sha it reached
hatch depends AER-12              what it waits on, and what waits on it
hatch depends AER-13 AER-12       AER-13 waits on AER-12
hatch depends AER-13 --remove AER-12        ...no longer
hatch one-pr AER-5                whether the story's tasks share its branch and one PR
hatch one-pr AER-5 on             ...turn it on for this story (or off, or inherit)
hatch ask AER-12 "how should retries be scoped?"     --recommend "Per-node: one budget per node, so a slow node cannot starve"     --option "Global: one budget for the drain, simpler to reason about"
hatch verdict AER-12 pass|fail ["findings"]
hatch answer                      answer the open questions, one at a time, here
hatch api GET /api/hatch/issues?statusId=2
hatch api PATCH /api/hatch/issues/AER-12 '{"dueAt":"2026-10-01"}'
```

`hatch api` is the escape hatch for a route with no dedicated verb; see
[`api-reference`](api-reference) for the full table.

`hatch ask` is for a choice between things you can name: each `--option`
becomes something the operator presses, and `--recommend` is the one you would
take. One question a call, so each can be answered on its own, and then stop:
an open question blocks the ticket from being dispatched at all (see
`questions-and-decisions`).

`hatch verdict` records a check session's own verdict, and a person rarely
types it. Anything but `pass` or `fail` is refused before a request is made.

`hatch landed` is for a project that lands on the trunk, where there is no pull
request to record. It records the sha the work reached the trunk at, in this
checkout's origin remote, at `<sha>` or `HEAD`; a sha not on the trunk is
refused before anything is sent.

`hatch depends` with one key reads: what that issue waits on, and what waits on
it. With two it writes an edge, and an edge gates one move, the one into the
column where the code gets written. It clears when the issue it names is
merged. Filing stories in order is not the same as saying they are ordered.

`hatch answer` walks the open questions serially on purpose: six printed at once
get answered in aggregate, which is how a wrong assumption gets in.

Every command takes `-h` for what it accepts, and [CLI
reference](cli-reference) has each one in full.

## Coming

Pages this one names that are not written yet:

- `environment-variables`
- `questions-and-decisions`

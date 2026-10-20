---
title: CLI reference
section: Reference
order: 20
summary: Every hatch command, what it takes, what it prints, and what it leaves on the board.
---

This is the whole surface of one binary, `hatch`, published for macOS, Windows
and Linux. `hatch --help` lists it, and every command takes `-h` for its own
usage block. Which origin it talks to, and which key it names itself with, come
from three layers of settings, highest first: [the runner](the-runner) has how
to get the binary and what those layers are, and `environment-variables` has
every variable.

## Every command

| Command | Needs a checkout | Writes to the board |
|---|---|---|
| `config` | No | No: it writes local files only |
| `board` | No | No |
| `next` | No | No |
| `queue` | No | No |
| `changelog` | No | No |
| `show` | No | No |
| `comments` | No | No |
| `start` | No | Yes: moves the ticket |
| `move` | No | Yes: moves the ticket |
| `comment` | No | Yes: adds a comment |
| `pr` | No | Only with a `<url>` or `--clear` |
| `landed` | No: reads git where it stands, or where `--repo` says | Yes: records a sha |
| `verdict` | No | Yes: records a verdict |
| `depends` | No | Only with a second key |
| `one-pr` | No | Only with `on`, `off` or `inherit` |
| `ask` | No | Yes: adds a question |
| `questions` | No | No |
| `answer` | No | Yes: answers a question |
| `api` | No | Whatever the route does |
| `work` | Yes | Yes |
| `go-to-work` | Yes | Yes |
| `do-my-work` | Yes | Yes |

The order is the order the binary knows them in. `hatch --help` prints the same
list with one exception: it does not print `landed`, so do not take it as the
complete one. Two more commands exist and are left off the table because nobody
types them: one is called by the container runner's entrypoint, and one by a
session's hooks.

Only `work`, `go-to-work` and `do-my-work` need a checkout, or a workspace to
clone into. Everything else is one request and a sentence about the answer, so
it runs from anywhere. Every write carries the key's name as its actor, so the
ticket's history says who did what without anybody being asked to record it.

## The commands

### config

```
hatch config [--show | --origin <origin> | --key <key> | --runner <name>
              | --repo <path>... | --workspace <dir>]
```

Asks for the origin and the key, and writes them to a per-user file where they
follow you between checkouts, so you do it once. Bare `hatch config` also asks
for the name the board should call this runner. The key may be left blank
against a Hatch with its wall off, where calls name themselves with a runner
header instead. Never put it in a repository, either way.

- `--show` says what is set and which layer each value came from. What it
  prints is in [the runner](the-runner#what---show-prints).
- `--origin <origin>` writes the origin alone, asking nothing, and leaves the
  key and everything else as they were.
- `--key <key>` writes the key alone, and needs an origin already set. A key on
  a command line stays in shell history; the prompt does not, so prefer the
  prompt on a machine other people can read.
- `--runner <name>` sets what the board calls this checkout. It is the one
  `config` flag that needs a checkout, since a runner's name is a fact about
  one of them.
- `--repo <path>` also serves that checkout, for a loop with no checkout of its
  own. It is repeatable, each path must exist and carry a `.git`, and bare
  `--repo` clears them.
- `--workspace <dir>` names the directory where the runner clones what the
  board binds and it has no checkout of. `--workspace off` never clones.

It touches no ticket and no column, so it writes nothing to the board.

### board

```
hatch board
```

Prints the columns and how many cards each holds, then how full each WIP slice
is, where a limit is set. Takes nothing and writes nothing.

### next

```
hatch next [<column>]
```

Prints the top workable card of a column, `todo` by default. The column is
found by its letters and digits alone, so `todo` reaches `To Do`. Cards whose
ready date has not arrived are folded past, the way the board folds them.

### queue

```
hatch queue [--mine] [<ancestor key>]
```

Every issue a dispatch pass would look at, in the order it looks, each with the
reason it would be folded past or the transition it is clear for. It is the
dry run for the loop and the answer to "why did it not pick up the ticket I
meant". It spawns nothing and writes nothing, and a board with nothing to look
at says so in a sentence.

- `<ancestor key>` narrows it to one epic's tree.
- `--mine` shows what a `--mine` pass would take and folds past everything
  else, naming whose it is. *Mine* means the caller's own: tickets assigned to
  the person the calling key belongs to, or to the key itself. Whose tickets a
  key's `--mine` reaches is set by an admin on the API Keys page, never by the
  key ([People, roles and keys](people-roles-and-keys)).

A row's marker says where the pass puts it:

- `!!` is emergency and `! ` is expedited. Every emergency row is considered
  before every expedited row, before anything else, whatever column each sits
  in.
- `- ` is low, considered after every normal row, and `~ ` is economy,
  considered last of all.
- `||` is paused. A person set it aside, and nothing picks it up until they
  set it back.
- A note such as `(express, no session)`, `(parent pulled, no session)`,
  `(epic, no session)`, `(under <epic>, no session)` or `(pre-approved, no
  session)` marks a hop: the loop carries the ticket on itself, with no session,
  rather than spawning one.

### changelog

```
hatch changelog [--days <n>] [--before <date>] [--project <KEY>] [--json]
```

What shipped, as markdown: a heading per day, a bullet per feature carrying how
much shipped beneath it, and an *Also* list of that day's fixes. A window with
nothing in it prints one sentence saying so. It writes nothing.

- `--days <n>` is the newest *n* days that shipped something, not the last *n*
  calendar days. Seven by default.
- `--before <date>` moves the window back to end before that date.
- `--project <KEY>` narrows it to one project, and refuses a key nobody has.
- `--json` prints the endpoint's own answer and nothing else.

### show

```
hatch show AER-12
```

The brief, its edges and where it is, followed by every comment on it. The
description is markdown and goes to the terminal as it was written. A ticket
that is expedited, emergency, low, economy, paused, express or pre-approved
says so, and a level it holds only because an ancestor carries it names that
ancestor. None of those can be set from here: the key is a key, and each is a
person's write. Reads only.

### comments

```
hatch comments AER-12
```

The thread alone, with no header, status or description. A session that
already has the brief in its prompt reads the thread with this rather than
reading the ticket again. Reads only.

### start

```
hatch start AER-12
```

Moves the ticket to *In Progress*, so the board says what is being worked on
right now. That is the board's whole job. The column is found the way
`move` finds one, and the move is an event under the key's name.

### move

```
hatch move AER-12 <column>
```

Moves the ticket to any column that is neither terminal nor protected. The
column is found by its name on the letters and digits alone, so `todo` reaches
`To Do`. A terminal column refuses, because only the operator decides that
something shipped; a deferred column refuses for the same reason; and a
protected column refuses, because only its approvers move work there, in the
browser. [The board and its columns](the-board-and-its-columns) has which is
which. A move is an event under the key's name.

### comment

```
hatch comment AER-12 "the body"
```

Writes a comment on the ticket, under the key's name as its author. The ticket
is where somebody looks in six months, and a comment naming a commit and a
branch is what makes that search short.

### pr

```
hatch pr AER-12 [<url>|--clear]
```

With one argument it reads: it prints the URL where the ticket is being
reviewed, and nothing else, so `open "$(hatch pr AER-12)"` is the whole of
"show me the review". With two it writes.

- `<url>` records where the review is, having opened one.
- `--clear` takes it off the one it has.

### landed

```
hatch landed AER-12 [<sha>] [--repo <path>]
```

For a project that lands on the trunk, where there is no pull request to
record: records that the ticket's work reached the trunk, in this checkout's
origin remote, at `<sha>` or `HEAD`. It is the one command beside the three
that spawn an agent that reads git, and it needs no checkout to run.

- `<sha>` is the commit, or `HEAD`, which is the default.
- `--repo <path>` reads git where the path says instead of in the working
  directory.

A sha that is not on the trunk there is refused before anything is sent. The
write is an event under the key's name.

### verdict

```
hatch verdict AER-12 pass|fail ["findings"]
```

A check session's own verdict, with its findings as the optional third
argument. A person rarely types it. Anything but `pass` or `fail` is refused
before a request is made.

### depends

```
hatch depends AER-12 [<key>|--remove <key>]
```

With one key it reads: what the ticket waits on, and what waits on it, both
printed even when empty. With two it writes an edge, and the answer is the
ticket's edges again.

- `<key>` says the ticket waits on that one. `hatch depends AER-12 AER-11`
  means AER-12 waits on AER-11.
- `--remove <key>` says it waits on it no longer.

An edge gates one move: the one into the column where the code gets written. It
clears when the issue it names is merged. Filing stories in order is not the
same as saying they are ordered; this is how to say it.

### one-pr

```
hatch one-pr AER-5 [on|off|inherit]
```

Whether a story's tasks share the story's branch and one pull request. With
only the key it reads. With a second argument it writes:

- `on` shares them, whatever the project's default is.
- `off` does not, whatever the project's default is.
- `inherit` follows the project's default.

A story only, and it is locked once any task under it has started: set it when
the story is filed, before its tasks are worked.

### ask

```
hatch ask AER-12 "the question" [--option "Label: what it means"]...
                                [--recommend "Label: what it means"]
```

Asks for a decision that is not yours to make. A question is a comment on the
ticket with options attached, so it is written under the key's name exactly as
`comment` is, and the two share one endpoint. One question a call, so each can
be answered on its own; then stop, because an open question blocks the ticket
from being dispatched at all.

- `--option "Label: what it means"` is a choice, and the flag is repeatable.
  Each one becomes something the operator presses, in the web UI and in
  `answer`. The label before the colon is short enough to press, and becomes
  the answer's own text.
- `--recommend "Label: what it means"` is the one you would take, in the same
  form. There may be one of these.

Name the choices wherever you can: a menu is answered in a press, and a
paragraph has to be read twice and replied to. With no options the question is
open-ended and is answered in prose. `questions-and-decisions` has what happens
to the ticket from there.

### questions

```
hatch questions [AER-12]
```

Everything waiting on an answer, or just that ticket's. Reads only.

### answer

```
hatch answer [AER-12]
```

Walks every open question, or one ticket's, one at a time on this terminal. A
number takes that option, Enter alone leaves the question open, a trailing `\`
keeps typing, and `^D` stops. It is one at a time on purpose: six printed at
once get answered in aggregate, which is how a wrong assumption gets in. Each
answer is written under the key's name.

### api

```
hatch api <METHOD> <path> [<json body>]
```

The escape hatch: any route under `/api/hatch/`, for what no other command
reaches. It takes the method, the path and an optional JSON body, and prints the
answer as it arrives.

```
hatch api GET /api/hatch/issues?statusId=2
hatch api PATCH /api/hatch/issues/AER-12 '{"dueAt":"2026-10-01"}'
```

- The method is upper-cased, and a path missing its leading `/` has one added.
- The body is sent exactly as typed, so quote it for your shell.
- A 2xx prints the body untouched, so it pipes to `jq`. A non-2xx exits 1 and
  prints the refusal, since here the refusal is the answer someone asked for.
  A 403 means the key is working and the route is not one a key may take.

What a call writes is whatever the route does, and the actor is the key's name
as always. `api-reference` has the routes.

## Coming

Pages this one names that are not written yet:

- `api-reference`
- `environment-variables`
- `questions-and-decisions`

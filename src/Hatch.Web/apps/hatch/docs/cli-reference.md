---
title: CLI reference
section: Reference
order: 20
summary: Every hatch command, what it takes, what it prints, and what it leaves on the board.
---

This is the whole surface of one binary, `hatch`, published for macOS, Windows
and Linux. `hatch --help` lists it, and every command takes `-h` for its own
usage block. `-h` and `--help` count only as the first argument: four commands
take prose, so a comment that reads `--help` is a body, not a request for help.
A usage mistake is different from a failed call (see [When a call
fails](#when-a-call-fails)): it prints `hatch: ` and what was wrong, a blank
line, then that command's usage block, all on standard error, and exits `1`.
Which origin it talks to, and which key it names itself with, come
from three layers of settings, highest first: [the runner](the-runner) has how
to get the binary and what those layers are, and [`environment-variables`](environment-variables) has
every variable.

## Every command

| Command | Needs a checkout | Writes to the board |
|---|---|---|
| `config` | No | No: it writes local files only |
| `use` | No | No: it writes local files only |
| `board` | No | No |
| `next` | No | No |
| `queue` | No | No |
| `changelog` | No | No |
| `runners` | No | No |
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

The order is the order the binary knows them in, and `hatch --help` prints the
same list. Two more commands exist and are left off the table because nobody
types them: one is called by the container runner's entrypoint, and one by a
session's hooks. They are in `--help`, in their own groups.

Only `work`, `go-to-work` and `do-my-work` need a checkout, or a workspace to
clone into. Everything else is one request and a sentence about the answer, so
it runs from anywhere. Every write carries the key's name as its actor, so the
ticket's history says who did what without anybody being asked to record it.

## The commands

### config

```
hatch config [--show | --origin <origin> | --key <key> | --runner <name>
              | --repo <path>... | --tag <tag>... | --workspace <dir>] [--here]
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
- `--runner <name>` sets what the board calls this checkout. It needs a
  checkout, since a runner's name is a fact about one of them.
- `--repo <path>` also serves that checkout, for a loop with no checkout of its
  own. It is repeatable, each path must exist and carry a `.git`, and bare
  `--repo` clears them.
- `--tag <tag>` sets the tags this runner advertises
  ([the runner](the-runner#what-a-runner-advertises)). It is repeatable, bare
  `--tag` clears them, a tag that breaks the rule is refused with the rule's
  sentence and nothing is written. Once this checkout's runner has a row,
  `--tag` seeds nothing further: the row's tags are the board's, and the loop
  follows them ([the runner](the-runner#what-a-runner-advertises)).
- `--workspace <dir>` names the directory where the runner clones what the
  board binds and it has no checkout of. `--workspace off` never clones.
- `--here` writes this checkout's `scripts/.env` instead of the per-user file,
  for every write above. The checkout's git must already ignore the file.
  Refused outside a checkout, combined with `--runner` (a runner's name is
  already the checkout's own), or combined with `--show`.

It touches no ticket and no column, so it writes nothing to the board.

### use

```
hatch use [<name> | --none]
```

Switches, shows or clears the active profile: the one-line pointer file that
names which profile file is layered in. It reads and writes local files only,
so it works with nothing configured.

- `hatch use` alone prints the active profile's name, or `<none>`.
- `hatch use <name>` makes `<name>` the active profile and prints it. It is
  refused, naming the profiles directory, unless a profile file by that name
  already exists there.
- `hatch use --none` clears the active profile.

It touches no ticket and no column, so it writes nothing to the board.

### board

```
hatch board
```

Prints the columns and how many cards each holds, then how full each WIP slice
is, where a limit is set. Takes nothing and writes nothing.

Each column is one line, `<Name>: <count>`, with ` (terminal)` or ` (deferred)`
after the name on a column that is one. Past the count it marks the cards that
are not ordinary, and only the marks that have any: `(n emergency)`,
`(n expedited)`, `(n low)`, `(n economy)` and `(n paused)` for priority, then
`(n express)` and `(n pre-approved)` for a card that carries its approval in
advance. A stock board has none of these, so its lines are a name and a number.

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

### runners

```
hatch runners [--json]
```

One line per runner that has spoken to this Hatch lately, most recently
heard from first: its name, what it is doing, the ticket it holds (or
*none*), and how long ago it last heartbeat. A caller with nothing to see
prints one sentence saying so. It writes nothing.

- `--json` prints the endpoint's own answer and nothing else.

### show

```
hatch show AER-12
hatch show AER-12 --brief
```

The brief, its edges and where it is, followed by every comment on it. The
description is markdown and goes to the terminal as it was written. A ticket
that is expedited, emergency, low, economy, paused, express or pre-approved
says so, and a level it holds only because an ancestor carries it names that
ancestor. A ticket that requires a runner tag says so too, naming the project or
the ancestor issue each tag beyond its own came from. None of those can be set from here: the key is a key, and each is a
person's write. A ticket a live runner holds says so on a `runner:` line - who
holds it, since when, and how long ago it was last heard from, with what it
is doing - drawn from the ticket itself, so it shows for any holder and is
absent once the lease has lapsed. Reads only. `--brief`, which may come before or after the key,
prints the header alone - everything above the blank line that precedes the
description - and leaves out the description and every comment, with no
request made for the comments at all.

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

Four things are refused before anything is sent, each as one `hatch: ` line:
the checkout has no `origin` remote, so a project's binding could never match
it; no branch can be named as the trunk there, which `HATCH_BASE_BRANCH`
settles; the sha does not resolve to a commit; or it resolves and is not on the
trunk, in which case land it first. The write is an event under the key's name.

### verdict

```
hatch verdict AER-12 pass|fail ["findings"]
```

A check session's own verdict, with its findings as the optional third
argument. A person rarely types it. Anything but `pass` or `fail` is refused
before a request is made.

It is recorded against the issue's live claim, one verdict to a claim, so a
second call replaces the first. Nothing moves when it is recorded: the runner
applies it after the session ends. Findings over 100,000 characters are
refused, and so is a verdict on a claim that is not under a check playbook,
with `a verdict needs a check playbook covering <key> where it stands, and none
does`. A key may call it, and so may a person.

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
clears when the issue it names reaches a terminal column. Filing stories in
order is not the same as saying they are ordered; this is how to say it.

Saying an edge that already stands is not an error and writes nothing. Four
edges are refused, each by name: an issue on itself, on one of its ancestors,
on one of its descendants (both of those "could never be satisfied", since a
parent is not done until its work is), and one that would close a cycle, which
the refusal puts as the other issue "is already waiting on this issue".

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
open-ended and is answered in prose. [Questions and
decisions](questions-and-decisions) has what happens to the ticket from there.

It prints `asked question #<n> on <key> as <author>`, then says the ticket will
not be dispatched again until it is answered, with the `hatch answer` line to
do it and the ticket's URL.

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

It ends with `answered <n> of <total>` and, if any were answered, a `hatch work`
line for the tickets that are now clear, since answering is only half of it.
With nothing open it prints `hatch: nothing is waiting on an answer` instead.

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
  A 403 prints the API's own reason - a role-gate code, or a plain sentence -
  except `key_not_accepted` and `scope_mismatch`, where it means the key is
  working and the route is not one a key may take.

What a call writes is whatever the route does, and the actor is the key's name
as always. [`api-reference`](api-reference) has the routes.

## The three that spawn an agent

`work`, `go-to-work` and `do-my-work` are the only commands that start an agent
session, and so the only ones that need a checkout, or a workspace to clone
into. Outside one they refuse and exit 1:

```
hatch: this is not a git repository, and a ticket is about a codebase.
hatch:   run it inside a checkout, name one with --repo, set HATCH_REPOS, or give a --workspace to clone into.
```

### work

```
hatch work [<issue key>] [--mine] [--under <epic key>] [-i] [--quiet]
           [--model <model>] [--effort <effort>] [--agent <id>] [--dry-run]
           [--no-keys] [--repo <path>]... [--workspace <dir>]
```

One increment: claims a ticket, spawns one headless agent session with the
prompt, model and effort its column and type call for, and exits when that
session ends. With no key it takes the next actionable issue; a key names one
outright. [One increment: `hatch work`](one-increment-and-the-loop#one-increment-hatch-work)
has what it prints and every exit code.

- `--mine` picks only the caller's own tickets, as `do-my-work` would. It is
  refused together with a key, since one names the ticket and the other narrows
  which ticket is picked:
  ```
  hatch: work takes a key or --mine, not both - one names the ticket, the other narrows which ticket is picked
  ```
- `--under <epic key>` looks only inside that epic's subtree. Not with a key
  either: one says which ticket, the other says where to look for one.
- `-i` runs a session you sit in, rather than a headless one.
- `--quiet` says nothing until the increment is finished.
- `--model <model>` beats the playbook's model, for this run only.
- `--effort <effort>` does the same for the effort.
- `--agent <id>` beats `HATCH_AGENT`, for this run only.
- `--dry-run` prints the prompt and exits: it claims nothing, spawns nothing,
  and clones nothing even where a workspace is configured.
- `--no-keys` reads no keys from this terminal. Ctrl-C and `--stop-file` still
  work.
- `--repo <path>` also serves that checkout. It is repeatable, and the whole
  list is for this run; `HATCH_REPOS` is not consulted when it is given.
- `--workspace <dir>` clones into `<dir>` every repository the board binds that
  this runner has no checkout of. `HATCH_WORKSPACE` is not consulted when it is
  given, and `--workspace off` never clones.

### go-to-work

```
hatch go-to-work [--mine] [--under <epic key>] [--once] [--quiet]
                 [--interval <seconds>] [--merge-interval <seconds>]
                 [--agent <id>] [--max-runs <n>] [--max-spend <dollars>]
                 [--until <HH:MM>] [--stop-file <path>] [--no-keys]
                 [--restart-after <minutes> | --no-restart]
                 [--repo <path>]... [--workspace <dir>]
```

`work` in a circle: the next actionable issue, one increment, ask again, until
nothing on the board is an agent's to move, and then wait and ask again. It
takes no ticket key. [The loop: `hatch go-to-work`](one-increment-and-the-loop#the-loop-hatch-go-to-work)
has the pass in order, the bounds and the restarts.

- `--mine` takes only the caller's own tickets. Plain `go-to-work` skips every
  person's tickets, including yours.
- `--under <epic key>` stays inside one epic's subtree.
- `--once` makes one pass, and out.
- `--quiet` prints no per-increment stream, only what each one ended as.
- `--interval <seconds>` is how long to wait when there was nothing to do.
  The default is 60.
- `--merge-interval <seconds>` is the merge train's own clock, separate from
  `--interval`: approved pull requests merge on it without waiting for an idle
  pass. The default is 5.
- `--agent <id>` beats `HATCH_AGENT`. It is resolved once before the loop starts
  and carried the whole night, unlike `--model` and `--effort`, which `work`
  reads fresh each run.
- `--no-keys` reads no keys from this terminal. Ctrl-C and `--stop-file` still
  work.
- `--repo <path>` also serves that checkout, and is repeatable.
- `--workspace <dir>` is where to clone the repositories this runner has no
  checkout of.
- `--max-runs <n>` stops after that many increments.
- `--max-spend <dollars>` stops once the run has cost that much.
- `--until <HH:MM>` stops at that wall-clock hour, tomorrow's if it has gone by.
- `--stop-file <path>` stops once that path exists, so `touch` it from anywhere.
- `--restart-after <minutes>` comes back as a newer build at least that often.
  `0` means only when the loop's own source actually changed.
- `--no-restart` never comes back as a newer build.

None of the bounds is set by default. Each is read between increments, so the
increment in flight always finishes. The two restart flags do nothing in a
`hatch` started by hand, which has no supervisor to rebuild and run it again.

### do-my-work

```
hatch do-my-work [every go-to-work flag]
```

Exactly `go-to-work --mine`, every flag included, and `hatch do-my-work -h`
prints `go-to-work`'s own usage block. It exists for a smaller budget spent on
one person's own tickets: on a board shared with other people, plain
`go-to-work` works the shared pool and skips every person's own, so one loop
never has to work both. Run `go-to-work` for the pool and `do-my-work`
separately for yours. [Sharing a board](one-increment-and-the-loop#sharing-a-board)
has the rest, including what happens to a key that belongs to nobody.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | It worked, or usage was asked for with `-h`, `--help` or `help`. For `work`, an increment ran, whatever its session exited with. For `go-to-work` and `do-my-work`, the night ended: a bound was reached, a pass found nothing to do, it was interrupted, or the `--mine` key belongs to nobody. |
| `1` | A refusal: bad arguments, an unknown command, `hatch` run bare, settings that are not configured, a directory that is not a repository, a bad `HATCH_REPOS`, a bad `HATCH_TAGS`, or an HTTP fault from the board. `work --mine` exits `1` for a key that belongs to nobody. `go-to-work` and `do-my-work` exit `1` when their runner name is already the live runner somewhere else. |
| `2` | Nothing to do, as an answer and not a fault. `next` found nothing workable, `pr` was read with no URL set, and `work` found the board idle, the ticket blocked, or another runner holding it. `runner-credential` also exits `2` while no credential has been saved yet, which is the ordinary state between starting the container runner and pasting one in. |
| `75` | `go-to-work` or `do-my-work` is asking its supervisor to rebuild it and run it again. It never happens in a loop started by hand, nor under `--no-restart` or `--once`. |
| `130` | Interrupted, for `work` and every command except `go-to-work` and `do-my-work`. The first signal prints that it is letting go of the ticket and releases the claim; a second is immediate. |

`go-to-work` and `do-my-work` never exit `130`. They catch their own
interrupt, print the night's tally with `interrupted` as the stop reason, and
exit `0`. A `--mine` key that belongs to nobody ends their night the same way,
with `0`, where `work --mine` refuses with `1`. [One increment and the
loop](one-increment-and-the-loop#exit-codes) has `work`'s codes and the loop's
own in prose; this table is the whole binary's.

`work -i` exits with the attached session's own exit code.

## When a call fails

A refused call is one line on standard error, starting `hatch: `, and exits
`1` ([above](#exit-codes)). A handful of the refusals have a sentence of
their own:

```
hatch: could not reach <origin> - <detail>
hatch: 401 - no key was sent and this Hatch has its wall on. Run `hatch config`.
hatch: 401 - the key was not accepted. Minted, not revoked, copied whole?
hatch: 403 - the key is good and this route is not one it may take (CLAUDE.md).
hatch: 403 - no key was sent, and this route is refused anyway (CLAUDE.md).
hatch: 404 - no such issue or route: <path>
```

A `403` prints the API's own reason where it has one - a role-gate code such
as `hatch: 403 - not_admin`, or a plain sentence such as `hatch: 403 - only an
owner or approver of AER may move work into "approved"`. The fixed sentence
above is kept for the two codes that really are a scope refusal,
`key_not_accepted` and `scope_mismatch`, where it replaces the code outright,
and for a `403` with an empty body, where there is no reason in the answer to
print - worded by whether a key was sent at all.

The first has a second shape once a retry happened: `hatch: could not reach
<origin> for <n>s - <detail>`. A call Hatch has not answered is tried again
until `HATCH_RETRY_SECONDS` runs out, ninety by default and `0` for a single
try, and `for 90s` is the only visible sign that any retrying happened at all.
Without it, the call was refused on the spot.

Retrying is about a Hatch that cannot be reached; a Hatch that answers too
slowly is a different case. Every call gives up after two minutes, whatever
`HATCH_RETRY_SECONDS` says.

The two `401`s tell apart a key that was sent and refused from no key sent at
all to a Hatch that wants one: the first sends you to check the key, the second
to `hatch config`. A `403` is the gate and not a fault - it names its own
cause, which is the API's own reason rather than always a scope. [401 and
403](people-roles-and-keys#401-and-403) has both from the other side, and
[People, roles and keys](people-roles-and-keys) has what a key's scopes reach.
Anything else is `hatch: <code> - <body>`, with the body's own sentence where
it has one.

Normal output goes to standard output and complaints to standard error, and
both take one lock, so a session's stream and the runner's own lines never
interleave.

## The two nobody types

Two of the commands are not a person's. `runner-credential <agent>` is the
container entrypoint's own plumbing: it asks for the credential of `claude`,
`codex` or `grok` in a retry loop, waiting while it exits `2` ([the container
runner](the-container-runner) has the rest). `inbox` is what a session's hooks
call to be handed a message, and it marks that message read ([the
inbox](what-a-session-is-told#the-inbox)). Both are real commands in the
binary and not hidden verbs, and both are left out of what a session is told on
purpose: the first prints a credential, for no work a session could do with it,
and a session that knew of the second could mark its operator's messages read
without reading them.

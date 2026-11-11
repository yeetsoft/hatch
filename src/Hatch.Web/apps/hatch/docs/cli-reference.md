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
Which origin it talks to, and which key it calls with, come
from four layers of settings, highest first: [the runner](the-runner) has how
to get the binary and what those layers are, and [`environment-variables`](environment-variables) has
every variable.

`--profile <name>` is taken out of the arguments of any command, wherever it
appears, and uses that profile for the one call; it also pins the run, so
`hatch use` never moves it. Given twice, the last wins; given with no name it
is refused with `hatch: --profile takes a name`; a name with no profile file
behind it is refused with `no such profile`. `HATCH_PROFILE` does the same
without the flag.

## Every command

| Command | Needs a checkout | Writes to the board |
|---|---|---|
| `config` | No | No: it writes local files only |
| `use` | No | No: it writes local files only |
| `image` | Yes | No |
| `standby` | Yes | No: it registers or forgets a login item on this machine |
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
| `pr` | No | Only with a `<url>`, `--clear` or `--none "reason"` |
| `landed` | No: reads git where it stands, or where `--repo` says | Yes: records a sha |
| `verdict` | No | Yes: records a verdict |
| `depends` | No | Only with a second key |
| `one-pr` | No | Only with `on`, `off` or `inherit` |
| `dangerous` | No | Only with a reason or `--clear` |
| `omit` | No | Only with a runner and reason, or `--lift` |
| `recuse` | No | Yes: omits this runner's own name |
| `dream` | No | Yes: adds a dream |
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

`work`, `go-to-work` and `do-my-work` need a key, and a checkout or a workspace
to clone into, and `standby` needs the checkout it registers a login item for.
Everything else is one request and a sentence about the answer, so
it runs from anywhere. Every write carries the key's name as its actor, so the
ticket's history says who did what without anybody being asked to record it.

## The commands

### config

```
hatch config [--show | --origin <origin> | --key <key>
              | --repo <path>... | --workspace <dir> | --models <model>...
              | --has <entry>... | --runner-base-image <image>
              | --log on|<dir>|off | --dangerous on|off]
             [--here | --profile <name>]
```

Asks for the origin and the key, and writes them to a per-user file where they
follow you between checkouts, so you do it once. A runner's name is its key's,
so there is no name to give. Left blank, the key is probed with no key: against
a Hatch with its wall off, `hatch config` offers `Create a runner on this
board? [Y/n]`, asks `Runner name [<suggestion>]`, creates the runner, writes
its secret as the key and prints `created runner <name>`; a taken name is
refused and nothing is written, and the runner has no owner until an admin sets
one. Against a Hatch with its wall on, it says there is no key, and an admin
creates the runner on the Runners page; then `hatch config --key <key>`. A loop
needs a key either way. Never put it in a repository.

- `--show` says what is set and which layer each value came from, including an
  `identity:` line naming who `/api/hatch/me` says the caller is - printed as
  `<unreachable>` rather than failing when the origin is unset or does not
  answer - and `has:`/`missing:`, plus an `<id> via:` line for each tool the
  shell or `PATH` answered, detected quietly and merged with `HATCH_HAS`.
  What it prints is in [the runner](the-runner#what---show-prints).
- `--origin <origin>` writes the origin alone, asking nothing, and leaves the
  key and everything else as they were.
- `--key <key>` writes the key alone, and needs an origin already set. A key on
  a command line stays in shell history; the prompt does not, so prefer the
  prompt on a machine other people can read.
- `--repo <path>` also serves that checkout, for a loop with no checkout of its
  own. It is repeatable, each path must exist and carry a `.git`, and bare
  `--repo` clears them.
- `--workspace <dir>` names the directory where the runner clones what the
  board binds and it has no checkout of. `--workspace off` never clones.
- `--log on|<dir>|off` writes `HATCH_LOG`, where this runner's log goes: `on` is
  the `logs` folder beside the per-user file, a directory is made if it is not
  there (and refused, with nothing written, if it cannot be), and `off` keeps
  none. The setting is only recorded here.
- `--dangerous on|off` writes `HATCH_DANGEROUS`: `off` makes this runner refuse
  work flagged dangerous, and `on` (the default) takes it. Written on the
  runner's row on every beat ([the runner](the-runner#what-a-runner-refuses)).
  Any other value is refused, with nothing written.
- `--models <model>...` writes `HATCH_MODELS`, the models this runner
  advertises. Give each its own `--models` (`--models haiku --models sonnet`);
  a bare `--models` clears them and the agent's default stands. Each is an alias
  (`haiku`, `sonnet`, `opus`) or a pinned id of `HATCH_AGENT`'s agent, at most
  100, trimmed and deduplicated. One that is neither, one of another agent, or a
  comma inside one (`--models haiku,sonnet`) is refused, naming it, with nothing
  written ([the runner](the-runner#models)).
- `--has <entry>...` writes `HATCH_HAS`, what this machine has that detection
  cannot see. Give each its own `--has` (`--has dotnet@10 --has node`); a bare
  `--has` clears them. Each is an `id` or `id@version` the master list knows,
  trimmed, lowercased and deduplicated. One it does not know, or a comma inside
  one (`--has dotnet,node`), is refused, naming it, with nothing written
  ([the runner](the-runner#has)).
- `--runner-base-image <image>` writes `HATCH_RUNNER_BASE_IMAGE`, the image
  [`hatch image dockerfile`](#image) starts from when `--base` is not given.
  Written alone, everything else as it was. An image with a space or line break
  in it is refused with nothing written.
- `--here` writes this checkout's `scripts/.env` instead of the per-user file,
  for every write above. The checkout's git must already ignore the file.
  Refused outside a checkout or combined with `--show`.
- `--profile <name>` writes that profile's own file instead of the per-user
  file, for every write above. The file need not exist yet. Refused combined
  with `--here` (two different places to write) or an invalid name. `--show
  --profile <name>` reads the named profile as if it were active, without
  switching to it.

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

A running `go-to-work` that follows the active profile restarts, or stops
saying why, when this moves the pointer; see
[Restarts](one-increment-and-the-loop#restarts).

### image

```
hatch image dockerfile [--base <image>]
hatch image build [--base <image>] [--image <name>] [--platform <list>]
```

`dockerfile` prints the Dockerfile this checkout's `.hatch/dependencies.json`
renders to. It reads the checkout and the machine-bound settings and makes no
board call, so it works with nothing configured; it needs no Docker either.
[`build`](#build) builds that text.

The text, in order:

1. `FROM <base>`.
2. Each declared dependency's install recipe, in id order. What a recipe requires
   is installed too, so `playwright-browsers` brings `node` in bare.
3. `.hatch/runner.Dockerfile`, byte for byte, only when the repository has one. A
   file with no final newline is followed by one `\n`, which is not part of it.
4. The resolved set, in the declaration's own shape (an id string, or an object
   with `id` and `version`), written to `/etc/hatch/dependencies.json` and
   carried as the image label `hatch.dependencies`.

The same declaration renders the same bytes, however its entries are ordered.

- `--base <image>` is the image to start from. It wins over
  `HATCH_RUNNER_BASE_IMAGE`, which `hatch config --runner-base-image <image>`
  writes; with neither, the command says so, naming both. An image is one word.

The baked block runs after `runner.Dockerfile` and writes under `/etc`, so a
`runner.Dockerfile` must leave the user as root. The heredoc needs BuildKit.

It refuses, in one sentence, when the checkout has no `.hatch/dependencies.json`
(naming the path), when the file is not a valid declaration, and when a declared
dependency ships no install recipe - that repository stays on host runners.

It touches no ticket and no column, so it writes nothing to the board.

#### build

Builds and tags the image the Dockerfile above describes. It takes `--base` and
refuses exactly as `dockerfile` does, because it renders the same text in the
same process, and gives the tool exactly those bytes on standard input:
`build -f - --platform <platforms> -t <image> .`, run from the repository root,
so `.hatch/runner.Dockerfile`'s own `COPY` lines resolve against the checkout as
they would for `docker build` by hand. The tool's own output is shown as it
comes. Nothing is pushed.

- It runs `docker`, or `podman` where there is no `docker`. With neither on
  `PATH` it says so in one sentence naming both.
- `--image <name>` is the name to tag, one word. Without it the name comes from
  the checkout's `origin` remote, whichever of `https://`, `ssh://` or
  `git@host:` form it is written in (credentials in the URL are ignored): a
  `github.com` origin gives `ghcr.io/<owner>/<repo>/hatch-runner`, lowercased,
  because a registry refuses uppercase. Any other host has no registry Hatch
  can name, and so does no `origin` at all; both are refused, naming
  `--image`.
- `--platform <list>` is a comma list of `os/arch`, such as `linux/arm64`.
  Without it the platforms are read off the base image itself - never a fixed
  pair, since `--base` can point anywhere. The registry's manifest list is
  read first (`docker buildx imagetools inspect`, then `docker manifest
  inspect`; podman's `manifest inspect`), skipping the attestation entries; a
  base only this machine holds, such as one built a step earlier, has no
  registry manifest, so its own platform is read from the local image instead.
  Where none of that reads, the build is refused, naming `--platform`.

A list of more than one platform cannot be loaded into a classic local image
store (Docker's containerd store can). Where the tool refuses one, it says so
itself and the build fails; the line after it names `--platform` as the way round
it, and the exit code is 1.

It touches no ticket and no column, so it writes nothing to the board.

### standby

```
hatch standby [install | show | remove]
```

Registers, shows or forgets the login item that starts this checkout's loop
(`hatch go-to-work --standby`) whenever its operator signs in. It makes no
board call, but it needs a checkout: the item is registered for the one you
are standing in, and under the profile you name with `--profile`, if any.

- `hatch standby install` registers the item and prints the checkout, the
  command it runs and the log it writes. It starts at the next sign-in;
  `hatch go-to-work --standby` starts it now. Run again, it says it is already
  installed and changes nothing.
- `hatch standby show`, or `hatch standby` alone, prints whether the item is
  installed, the command and the log, and then the loop lock: the pid of the
  loop holding this checkout right now, or that no loop does. A lock left by a
  process that has died counts as no loop.
- `hatch standby remove` forgets the login item. A loop that is already
  running is not stopped by this - only the login item goes away. With nothing
  installed it says so.

`show`, a repeated `install` and a `remove` with nothing installed exit 0. A
refusal by the platform's own tool, no checkout, or a hatch that cannot be
started from a login item (the bare `dotnet` host) exits 1 with one sentence on
standard error.

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
at says so in a sentence. Runner tags are judged on the calling key's own tags
from the Runners page, the same ones its loop carries, so a tagged project is
not reported as "advertises none" to a key that has them.
It reads as this checkout's runner, so a row can say a ticket is left for another
runner.

- `<ancestor key>` narrows it to one epic's tree.
- `--mine` shows what a `--mine` pass would take and folds past everything
  else, naming whose it is. *Mine* means the caller's own: tickets assigned to
  the person the calling key belongs to, or to the key itself. Whose tickets a
  key's `--mine` reaches is set by an admin on the Runners page, never by the
  key ([People, roles and keys](people-roles-and-keys)).

A row's marker says where the pass puts it:

- `!!` is emergency and `! ` is expedited. Every emergency row is considered
  before every expedited row, before anything else, whatever column each sits
  in.
- `- ` is low, considered after every normal row, and `~ ` is economy,
  considered last of all.
- `||` is paused. A person set it aside, and nothing picks it up until they
  set it back.
- A runner bound to projects folds past an issue in any other project, in a
  sentence such as `AER-12 is in project AER - this runner is bound to FIL and
  HA`. Hops are folded too: a runner does not carry a ticket on in a project it
  does not drive.
- A note such as `(express, no session)`, `(parent pulled, no session)`,
  `(epic, no session)`, `(under <epic>, no session)` or `(pre-approved, no
  session)` marks a hop: the loop carries the ticket on itself, with no session,
  rather than spawning one.

After those rows, `queue` closes with one line per distinct thing no live
runner currently offers or has, read from the same `waitingOnARunner` list
the attention panel draws, so the terminal and the panel cannot disagree.
Narrowed to `--mine` or an ancestor key, the footer counts only the issues the
rows above show; unnarrowed, it matches the panel. For example: `3 issues
wait for a runner that offers fable` or `1 issue waits for a runner that has
pwsh 7 or newer and playwright-browsers`. A server too old to send the list
prints nothing extra.

`queue` asks the board which live runners could take each clear row right
now - `next` and `work` do not - so a row's own line can end with a second
clause in parentheses: `only host:/a can take this` when just one runner
can, or `no live runner can take this: host:/a: busy; host:/b: omitted` when
none can, naming every live runner ruled out and the fold's own reason for
each. Silent when two or more runners can take it, or when the board did not
judge.

### changelog

```
hatch changelog [--days <n>] [--before <date>] [--project <KEY>] [--json]
```

What shipped, as markdown: a heading per day, a bullet per feature carrying how
much shipped beneath it, and an *Also* list of that day's fixes. A window with
nothing in it prints one sentence saying so. It writes nothing. This is the
agent-facing feed, unchanged; the web view is the Changelog report under
Reports.

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

A runner's row is followed by one indented line per session of its own that is
still live: `  where  agent  OS  state`, two spaces between columns, aligned over
that runner's own live sessions alone. The OS is what the session itself
reported (for example `Darwin 23.6.0`), not something the CLI looks up. A
session missing its `where`, agent or OS prints `-` in its place. A runner with
no live session - a gone runner included - prints no line beneath it.

`--json` carries more than this plain-text line shows: the full set the
endpoint answers under each session - `os`, `arch`, `agentVersion`,
`container`, `username`, `processStartedAt`, and `ip` (the server's own
reading of the connection, never sent by the CLI) - where the table above
prints only `os`.

A runner that has reported its own process adds `  on HOST`, `N runs` and
`$X.XX` (the machine, the increments run and the dollars spent), then the
tokens, before any projects, each only where it has said so. The tokens read
`2.4M tokens (180k last hour)`: the total, and the last hour in brackets only
where the runner reported one. A runner that reported only an hour prints no
tokens at all.

A runner that reports dependencies closes that part of its line with
`  has dotnet 10.0.100, node 22.11.0, pwsh`, comma-separated, before any
projects; one with none prints nothing extra.

A runner bound to some projects (`go-to-work --project FIL --project AER`)
closes its line with `  projects FIL AER`,
the keys space-separated. A runner bound to none ends its line as it always did.
The Runners page shows the same list comma-separated, `projects HA, AER`,
read-only, drawn from the runner's own flags.

A runner that advertises models closes its line with them after any projects,
`  models haiku sonnet opus fable`, the ids space-separated; one that advertises
none prints nothing extra ([the runner](the-runner#models)).

### show

```
hatch show AER-12
hatch show AER-12 --brief
```

The brief, its edges and where it is, followed by every comment on it. A ticket
that was filed from another says so on a `filed from:` line, and one with
tickets filed from it lists them on a `filed:` line; both are provenance only,
and absent where they do not apply. The
description is markdown and goes to the terminal as it was written. A ticket
that is expedited, emergency, low, economy, paused, express or pre-approved
says so, and a level it holds only because an ancestor carries it names that
ancestor. A ticket that requires a runner tag says so too, naming the project or
the ancestor issue each tag beyond its own came from. A ticket with omitted runners lists each one with its reason, who omitted it and how long ago, in the shape `hatch omit AER-12` reads, with `(from AER-3)` on one an ancestor carries, and says nothing of them when there are none. None of those can be set from here: the key is a key, and each is a
person's write. A ticket a live runner holds says so on a `runner:` line - who
holds it, since when, and how long ago it was last heard from, with what it
is doing - drawn from the ticket itself, so it shows for any holder and is
absent once the lease has lapsed. A story or bug of a project that enforces its
one-PR default gets the `one-pr:` line too, as `hatch one-pr` reads it. Reads only. `--brief`, which may come before or after the key,
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
hatch pr AER-12 [<url>|--clear|--none "reason"]
```

With one argument it reads: it prints the URL where the ticket is being
reviewed, and nothing else, so `open "$(hatch pr AER-12)"` is the whole of
"show me the review". A ticket that declared it finished with no code prints
that declaration instead. With two it writes.

- `<url>` records where the review is, having opened one.
- `--clear` takes it off the one it has.
- `--none "reason"` declares the ticket finished with no code, and prints
  `AER-12: finished with no code - reason`. The reason is required and at
  most 200 characters; `hatch show` prints it as `no code:`.

A declaration is for work that produced nothing to review, such as an
investigation. It is refused, with the server's sentence and exit `1`, once the
ticket has a pull request recorded or a branch on origin. A branch that later
appears on origin sets the declaration aside, and so does moving the ticket
left of review. A ticket that declared no code closes with no person reading
anything when it is pre-approved, or when its project's *A no-code declaration
closes the ticket* setting is on; otherwise it waits in review for a person.

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

Whether a story's or a bug's tasks share its branch and one pull request. With
only the key it reads. With a second argument it writes:

- `on` shares them, whatever the project's default is.
- `off` does not, whatever the project's default is.
- `inherit` follows the project's default.

A story or a bug only, and it is locked once any task under it has started:
set it when it is filed, before its tasks are worked. Under a project that
enforces its one-PR default, `on` and `off` are refused with a `409`; `inherit`
is still accepted. The read under such a project says `(the project's default,
enforced)`, or `(set on this story before the project enforced it; locked)` for
a story or bug that carries an override from before.

### dangerous

```
hatch dangerous AER-5 ["reason" | --clear]
```

Whether running an issue's work could destabilise the machine that runs it:
security testing, performance or load testing, or crashing or hanging
processes on purpose. With only the key it reads. With a second argument it
writes:

- a reason flags the issue, for the operator of a runner to read.
- `--clear` clears the issue's own flag.

The reason is at most 200 characters, measured after trimming. A blank reason
is refused and calls nothing, so clearing is always said as `--clear`. Open to
a key. A task inherits the flag of the story or epic above it, and `hatch show`
prints the reason with `(from AER-5)` after it when it is not the issue's own;
it prints nothing for an issue that is not flagged.

### omit

```
hatch omit AER-12 [<runner> "reason" | --lift <runner>]
```

The runners kept off an issue's work. With only the key it reads: each
runner omitted, its reason, who omitted it and how long ago, or
`omitted runners: none`. With more it writes:

- `<runner> "reason"` omits that runner from the issue.
- `--lift <runner>` lifts the issue's own omission of that runner.

The reason is required, and is at most 200 characters, measured after
trimming; a missing or blank one is refused and calls nothing. A task inherits
the omissions of the story or epic above it, and the read names each one with
`(from AER-3)` after it. An inherited omission is lifted on the ticket that
carries it: asked of the task, `--lift` prints the refusal naming that ticket.
Open to a key.

### recuse

```
hatch recuse AER-12 "reason"
```

What a session calls on its way out: `omit` for this runner's own name, which
is its key's. It makes the same write `omit`
does, and says what it recorded: `recused <runner> from AER-12: <reason>`.

The reason is required, and is at most 200 characters, measured after
trimming; a missing, blank or overlong one is refused and calls nothing, and
so is a call with no key (`recuse needs a key - a runner's name is its key's`).
The dispatcher never hands the issue to
this runner again, and neither does it hand it any task below the issue. Lift
it with `hatch omit AER-12 --lift <runner>`. Open to a key.

### dream

```
hatch dream "<words>" [--source <ref>] [--project AER]
```

Puts a half-thought on the Dream Board, the one a ticket is not yet the right
shape for. The words are markdown, quoted as one argument, and are posted
exactly as written after trimming. `--source` is free text saying where the
dream came from: a session id, a URL. `--project` is a lean, not a home: the
project key the dream points toward. A key nobody has is refused before any
call is made, naming the key it could not find.

The author is the key's name, set by the server and not by the command. It
says what it posted: `posted dream <id> as <author>`. The dream appears on the
Dreams screen at once, with its source beside it. A missing or blank set of
words, a second bare argument and a flag `dream` does not take are refused and
call nothing; the server's own refusal, a length cap for one, reaches the
terminal as it is. Open to a key.

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
  except `key_not_accepted`, where it means the key is
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
           [--project <key>]...
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
- `--project <key>` looks only in that project (`AER`, say). It is repeatable,
  and the whole list is for this run. It narrows the scan, so it does nothing
  with a key, and it never touches the runner's row: a one-off `work` does not
  change what the board says this runner is bound to. A value that is not the
  shape of a project key is refused with the usage block.
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
hatch go-to-work [--mine] [--under <epic key>] [--project <key>]... [--once] [--standby] [--quiet]
                 [--interval <seconds>] [--merge-interval <seconds>]
                 [--agent <id>] [--max-runs <n>] [--max-spend <dollars>]
                 [--max-tokens <count>] [--until <HH:MM>] [--stop-file <path>] [--no-keys]
                 [--max-tokens-per-hour <count>]
                 [--restart-after <minutes> | --no-restart]
                 [--repo <path>]... [--workspace <dir>] [--runners <n>]
```

`work` in a circle: the next actionable issue, one increment, ask again, until
nothing on the board is an agent's to move, and then wait and ask again. It
takes no ticket key. [The loop: `hatch go-to-work`](one-increment-and-the-loop#the-loop-hatch-go-to-work)
has the pass in order, the bounds and the restarts.

With `HATCH_LOG` set, the run keeps a plain-text copy of what it says in a file
under that folder, and its first line names it (`hatch: logging to <path>`). A
restart appends to the same file and the next night starts a new one.

- `--mine` takes only the caller's own tickets. Plain `go-to-work` skips every
  person's tickets, including yours.
- `--under <epic key>` stays inside one epic's subtree.
- `--project <key>` takes only that project's tickets. It is repeatable, and
  the whole list is for this run: sent on every beat, and the loop never
  takes it back. With none given the loop works every project, as it always has.
- `--once` makes one pass, and out.
- `--standby` starts the runner paused: it says so once, reports in, and takes
  nothing until the board's Go to Work sets it running. A restarted incarnation
  ignores it, and it is refused with `--once`.
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
- `--runners <n>` starts `n` runners on this machine, from 1 to 16; the default
  is 1. The first is this one as it runs today, and the others clone into
  `<workspace>.n`. Output is prefixed `[n]` and no footer or keys are drawn.
  Ctrl-C, `--stop-file` and a restart apply to all of them, and each finishes
  its increment first. It needs a workspace, so `--workspace off` is refused. A
  pinned `--profile` is handed to every member.
- `--max-runs <n>` stops after that many increments. With `--runners` it bounds
  the set's combined total, and every runner stops once they have made that many
  between them.
- `--max-spend <dollars>` stops once the run has cost that much. With
  `--runners` it is the set's combined spend, and a restart of the set carries it
  forward.
- `--max-tokens <count>` stops once the increments have reported that many
  tokens - `500k`, `2M`, `1.5M`. It counts reported tokens only: an increment
  that reported none is not in the total. With `--runners` it is the set's
  combined total, and a restart carries it forward. A value that is not a token
  count is refused at startup, before any lock is taken.
- `--max-tokens-per-hour <count>` waits, rather than stops, while the tokens
  reported by increments that ended in the last sixty minutes are at or over
  that count - `500k`, `2M`. The
  loop takes no ticket, spawns nothing and keeps heartbeating, and says so once
  (`hatch: this runner is over 200k tokens/hour - taking no tickets until 3:40am`,
  then `hatch: still over 200k tokens/hour` as a reminder). The beat's line and
  the runner row read `over 200k tokens/hour until 3:40am`, and the beat carries
  `throttledUntil`, the instant the window is expected to be under the count.
  The hour's own entries age out oldest first, so the loop resumes by itself the
  moment the total is under, saying `hatch: back under 200k tokens/hour - taking
  tickets again`. The wait records nothing: no increment, no failure and no
  token is counted, so it never moves the three-failures stop or any other
  bound. An order from the Runners page during the wait is spent with
  `order spent - over 200k tokens/hour until 3:40am`, and nothing is claimed.
  Usage limits win when both hold, a paused runner stays paused, and
  `--max-tokens` and `--until` still end the night. With `--runners` the window
  is the set's combined last hour, and a restart carries it forward. One
  increment bigger than the count puts the loop over on its own, until that
  increment is an hour old, so size the count above the biggest increment; the
  closing tally's `hatch: peak hour 340k tokens` line is the number to size it
  from. A value that is not a token count, `0` included, is refused at startup,
  before any lock is taken.
- `--until <HH:MM>` stops at that wall-clock hour, tomorrow's if it has gone by.
- `--stop-file <path>` stops once that path exists, so `touch` it from anywhere.
- `--restart-after <minutes>` comes back as a newer build at least that often.
  `0` means only when the loop's own source actually changed.
- `--no-restart` never comes back as a newer build.

None of the bounds, `--project` included, is set by default. Each is read
between increments, so the increment in flight always finishes. The two restart flags do nothing in a
`hatch` started by hand, which has no supervisor to rebuild and run it again.

### do-my-work

```
hatch do-my-work [every go-to-work flag]
```

Exactly `go-to-work --mine`, every flag included, and `hatch do-my-work -h`
prints `go-to-work`'s own usage block, so it takes `--runners` too. It exists for a smaller budget spent on
one person's own tickets: on a board shared with other people, plain
`go-to-work` works the shared pool and skips every person's own, so one loop
never has to work both. Run `go-to-work` for the pool and `do-my-work`
separately for yours. [Sharing a board](one-increment-and-the-loop#sharing-a-board)
has the rest, including what happens to a key that belongs to nobody.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | It worked, or usage was asked for with `-h`, `--help` or `help`. For `work`, an increment ran, whatever its session exited with. For `go-to-work` and `do-my-work`, the night ended: a bound was reached, a pass found nothing to do, it was interrupted, or the `--mine` key belongs to nobody. |
| `1` | A refusal: bad arguments, an unknown command, `hatch` run bare, settings that are not configured, a directory that is not a repository, a bad `HATCH_REPOS`, a bad `HATCH_MODELS`, a bad `HATCH_HAS`, or an HTTP fault from the board. `work --mine` exits `1` for a key that belongs to nobody. `go-to-work` and `do-my-work` exit `1` when their runner name is already the live runner somewhere else. |
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
above is kept for the one code that really is a key refusal,
`key_not_accepted` (a key on a route that takes no key), where it replaces the code outright,
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
cause, which is the API's own reason. [401 and
403](people-roles-and-keys#401-and-403) has both from the other side, and
[People, roles and keys](people-roles-and-keys) has what a key may and may not do.
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

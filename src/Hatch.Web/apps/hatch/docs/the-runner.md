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
build as the page serving it, so the two are never a version apart. Four
platforms are built: `win-x64`, `osx-arm64`, `osx-x64` and `linux-x64`.

Put the file on your `PATH` as `hatch` (`hatch.exe` on Windows). On macOS and
Linux it needs marking executable first:

```
chmod +x hatch
```

The binary is self-contained: nothing else needs installing to run it, not
.NET, not Node, not `jq`. It needs `git`, and the agent CLI it spawns, logged
in: `claude` by default, or `codex`, `grok` or `ollama` when `HATCH_AGENT` or
`--agent` says so. In a checkout of Hatch itself, `scripts/hatch.sh` (`scripts\hatch.ps1`
in PowerShell) finds or builds the binary and hands over, so nothing needs
installing first — the wrapper itself needs the .NET SDK, and bash 3.2+ or
PowerShell 5.1+. `make build-hatch` builds the binary once so every command
after is fast, and `make publish-hatch` produces the four platform binaries
the Runner page serves.

That page carries a command reference beneath the download, so a person who
has just downloaded `hatch` has it in front of them. The whole reference is
the [CLI reference](cli-reference).

## Which agent it spawns

Install and sign in to the one the runner will spawn, then name it with
`HATCH_AGENT` or `--agent`. Vendor install lines move; the vendor's page wins
over the line here.

| id | install | signing in | needs |
| --- | --- | --- | --- |
| `claude` | [Claude Code setup](https://code.claude.com/docs/en/setup) | `claude auth login` | A Claude Pro, Max, Team or Enterprise plan, or a Console account; the free claude.ai plan has no Claude Code |
| `codex` | [Codex CLI](https://learn.chatgpt.com/docs/codex/cli) | The first run of `codex` offers *Sign in with ChatGPT*, or set `CODEX_API_KEY` | See the vendor's page for which plans include it |
| `grok` | [Grok Build](https://docs.x.ai/build/overview) | Run `grok` once for the browser sign-in, or set `XAI_API_KEY` | No plan is named here: see the vendor's page |
| `ollama` | Runs through the `codex` CLI: install `codex` | None: there is no account | An Ollama server reachable at `HATCH_OLLAMA_HOST` ([Reaching an Ollama](the-container-runner#reaching-an-ollama-outside-the-container)) |

The install lines, as the vendors print them today:

```
curl -fsSL https://claude.ai/install.sh | bash          # claude
irm https://claude.ai/install.ps1 | iex                 # claude, Windows
curl -fsSL https://chatgpt.com/codex/install.sh | sh    # codex
curl -fsSL https://x.ai/cli/install.sh | bash           # grok
irm https://x.ai/cli/install.ps1 | iex                  # grok, Windows
```

## Pointing it at a board

Bare `hatch config` asks, in order, for the origin, the key (read without
echo), the agent, the path of that agent's CLI, and, inside a checkout, the
name the board should call this runner. Enter keeps what is shown in brackets.
It writes the answers to a per-user file, so it follows you between checkouts
and you do it once. The asking needs a terminal: redirected or scripted, bare
`hatch config` refuses and exits 1 rather than hang on a prompt nobody can
answer.

An empty origin is refused, and so is one without `http(s)://` in front of it.
Leaving the key blank prints a warning, and so does a key that does not start
`hatch_ak_` — both are carried on anyway, since the call that follows is the
one that actually knows whether the board accepts it.

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
- `--here` writes this checkout's `scripts/.env` instead of the per-user file,
  for every write above. The checkout's git must already ignore the file.
  Refused outside a checkout, combined with `--runner` (a runner's name is
  already the checkout's own), or combined with `--show`.

An origin or key write is then tried against the board. If that call does not
go through, the file is still written, so there is something to fix, and the
command exits 1. With the wall off, the key may be blank; with the wall up,
the runner needs one (*People, roles and keys*). A call that goes through
prints `reached <origin> - columns: …`, naming the board's own columns, so the
address and the key are both known good before anything is claimed.

## Four layers

Every setting is read from four places, highest first:

1. an exported variable;
2. `scripts/.env` in the checkout you are standing in;
3. the named profile file, when one resolved (`--profile`, `HATCH_PROFILE`, or the active-profile pointer file);
4. the per-user file `hatch config` writes.

`hatch config --here` writes the second layer directly - every other write
mode writes the fourth ([`config`](cli-reference#config)).

`hatch config --show` prints where that file is, so no path is written here.
It is mode 600 wherever the platform has modes; on Windows it inherits the
directory's ACL. A blank value is the same as unset. A flag beats every layer
for its run: `--repo` on `work` or `go-to-work` replaces `HATCH_REPOS` rather
than adding to it.

Both files are parsed, not sourced: a `KEY=value` line, blanks and `#`
comments skipped, one layer of matching quotes stripped off the value. An
empty value never wins over a layer beneath it that is set, and a name not on
the allowed list is ignored with a line saying so, rather than silently.

Six settings are read from the environment only: `HATCH_HEARTBEAT`,
`HATCH_RETRY_SECONDS`, `HATCH_ROOT`, `HATCH_NIGHT_STATE`, `HATCH_RTK` and
`HATCH_PROFILE`. A file line naming one of them is ignored, with a warning
that says so.
`HATCH_BASE_BRANCH` has no `config` flag: export it, or put it in
`scripts/.env`.

**Warn:** `HATCH_RUNNER` is read only from the first two layers - an exported
variable, or this checkout's own `scripts/.env` - never the profile file or
the per-user file, whether or not a profile is active. A name belongs to a
checkout, not to a person carried between them; a value found in the profile
or the per-user file is ignored, with a line saying which held it.

A profile with no `HATCH_WORKSPACE` of its own does not share the no-profile
default: it gets its own `hatch/workspaces/<profile>` directory, beside -
never inside - the no-profile `hatch/workspace`, so a clone in the one can
never collide with a profile subdirectory in the other.

These are the settings `hatch --help` lists, with their defaults:

```
HATCH_BASE           the origin of your Hatch
HATCH_KEY            hatch_ak_...; optional against a Hatch with its wall off
HATCH_CLAUDE_BIN     the claude CLI, if it is not on PATH
HATCH_CODEX_BIN      the codex CLI, if it is not on PATH
HATCH_GROK_BIN       the grok CLI, if it is not on PATH
HATCH_AGENT          claude, codex, grok or ollama (default: claude)
HATCH_BASE_BRANCH    the trunk go-to-work resets to between increments
HATCH_RUNNER         what the board calls this runner (default: a cast-list
                     character, chosen once per checkout)
HATCH_ROOT           the checkout to work in (default: upwards from here)
HATCH_REPOS          checkouts a loop with no checkout of its own serves,
                     joined on : (Unix) or ; (Windows)
HATCH_TAGS           the tags this runner advertises, comma-separated (default:
                     none)
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

[Environment variables](environment-variables) has the full table.

## What --show prints

`hatch config --show` is the way to see all of that at once. It prints:

- the per-user file's path, and whether it exists yet, and the path of this
  checkout's `scripts/.env`;
- the runner's name, and where it came from;
- who `/api/hatch/me` says the caller is - the key, its owner and its scopes,
  or that calls go out as a named runner - or `<unreachable>` when there is
  no origin to ask or the call fails;
- `HATCH_RETRY_SECONDS`, with its layer;
- each setting that can live in a file, with the layer it came from: exported,
  the `.env` path, or the per-user file;
- the key masked, never in full;
- `HATCH_REPOS` as a path a line;
- `HATCH_TAGS` as a comma-separated list, or `<unset>`;
- for an unset `HATCH_WORKSPACE`, the directory clones would go to.

## What a runner advertises

A tag is a word a project's, a ticket's, or one of its ancestors' runner tags
require. A project that requires `gpu` is only worked by a runner that carries
`gpu`, and a ticket or an ancestor that requires a tag of its own adds to the
project's, never away from it - a runner must carry the whole union. A column
may require tags of its own too, union'd in the same way, and gating every
move out of it rather than only the move into the column where code gets
written. **The tags seed the row once.** The first heartbeat to name any
writes them onto the row, which is what makes them show on the page
immediately; every heartbeat after that leaves them alone, whether or not it
names any of its own. The consequence is worth knowing: once a runner's row
carries tags, **the row is where they live**, and `hatch config --tag` typed
at a checkout that already has one changes the board no more than
`--max-runs` does from the command line - only a `PATCH` on the row itself
moves them from here on. The board reads them
again on every dispatch, and a running loop adopts the row's tags on its next
heartbeat and says so once (`hatch: the board set --tag ...`).

A runner carries at most 8 tags. Each is at most 32 characters, with no space
or comma inside, and is trimmed and lowercased. A tag that breaks the rule
refuses every command, with the rule's sentence.

```
HATCH_TAGS=gpu,linux
hatch config --tag gpu --tag linux
```

`--tag` is repeatable, and bare `--tag` clears them. An empty `HATCH_TAGS`
reads as unset, so a runner that sets nothing advertises nothing.

A runner that falls short of what a project requires is not refused outright:
`hatch queue` folds the row and says what is missing, by name — *"AER-12 is
implemented only by a runner advertising macos and xcode - this runner
advertises linux"* (*"... advertises none"* for a runner with no tags at all).

## The runner's name

The board shows each runner by name, under **Agents → Runners** (*Watching the
loop*). The name is `HATCH_RUNNER` if that is set; otherwise the name recorded
for this checkout; otherwise a cast-list character, picked once from the host
and the path, skipping names already recorded here or live on the board (read
from `GET /api/hatch/runners/names`, names alone), and kept from then on. An explicit `HATCH_RUNNER` over 240 characters is clipped
from the front, since the server refuses one any longer and the end of a name
is the part that still identifies the checkout.

`hatch config --runner <name>` refuses a name that is empty, too long, not
plain ASCII, or already a live runner's - apart from this checkout's own
current name - and writes nothing. It also needs a checkout: a name belongs to a checkout, not to a person.

## Where the key never goes

The key stays out of the artifact: never a tracked file, a commit, a plan, an
issue or a paste. `scripts/.env` is git-ignored for that reason, and the
`hatch_ak_` prefix exists so that one which does leak is recognisable on
sight. The per-user file sits outside every repository. The
[agent block](the-claudemd-block) tells an agent the same.

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
[questions and decisions](questions-and-decisions)).

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

## Two runners on one machine

One machine, two checkouts of the one repository a project binds, two loops -
`<checkout-a>` and `<checkout-b>` below, standing for whatever two paths you
actually clone to. Nothing here needs a second machine, a second person, or a
second Hatch, though the last of those is covered too.

### The layout

Two clones of the same repository, side by side, with one `go-to-work` (or
`work`) loop standing in each. The two checkouts can serve the same board and
split its work by tag (*Splitting the work by column*, below), or each point
at a Hatch of its own.

### What is already per checkout

Two things already belong to one checkout and no other, with nothing to set:

- **The runner's name.** Recorded in the `runners` file beside the per-user
  config file, keyed on the checkout's canonical path, so `<checkout-a>` and
  `<checkout-b>` each get a name of their own the first time either speaks to
  a board - see [the runner's name](#the-runners-name).
- **The loop lock.** One `go-to-work` or `work` at a time per checkout; a
  second naming the same checkout is refused, with the pid of the one already
  holding it ([one loop per served checkout](one-increment-and-the-loop#one-loop-per-served-checkout)).

What is **not** per checkout is everything else. The per-user file - its
directory is the OS application-data folder, `%APPDATA%\hatch` on Windows and
`~/.config/hatch` on macOS and Linux; `hatch config --show` prints the exact
path - holds the origin, the key, the agent, the tags and the workspace for
every checkout on the machine, until a checkout's own `scripts/.env`
overrides a line of it. Two checkouts left on the one `hatch config` are the
same runner wearing two names: same board, same key, same tags, same
workspace - rarely the point of running two.

### Setting up the second checkout

In `<checkout-b>`:

```
hatch config --here --tag breakdown
hatch config --here --workspace <dir>
```

`<dir>` is a directory of its own, outside both checkouts - a workspace may
not sit inside a named checkout, nor a named checkout inside it. `--here`
writes this checkout's `scripts/.env` instead of the per-user file, and only
the lines the flag given it names: it never copies a per-user value down,
and it is refused outside a checkout, refused combined with `--show`, and
refused unless this checkout's git already ignores `scripts/.env`.

For a second Hatch - `<checkout-b>` pointed at a board of its own rather than
sharing `<checkout-a>`'s - add the origin and the key the same way:

```
hatch config --here --origin <origin>
hatch config --here --key <key>
```

A key on a command line stays in shell history; the bare `hatch config
--here` prompt asks for each in turn without echoing the key, and Enter on
any prompt keeps only what this checkout's own file already carries - never a
value read in from the per-user file or an export, even though the bracket
shown is whatever is actually active.

**Warn:** a key is read only from the file that carries the origin it pairs
with, so `--here --origin` alone leaves `<checkout-b>` keyless - the per-user
key is ignored, with a line saying which file held it - until `--here --key`
gives this checkout a key of its own.

### What --show prints in each checkout

Run from `<checkout-a>`, which carries no overrides of its own:

```
file:                 <per-user config>/config
checkout:             <checkout-a>/scripts/.env  (written by --here)
runner:               <checkout-a>  (<per-user config>/runners)
identity:         key "<name>", owned by <owner>, scopes: hatch
HATCH_RETRY_SECONDS:  90  (default)
HATCH_BASE:        <origin>  (<per-user config>/config)
HATCH_KEY:         ●●●●●●●●  (<per-user config>/config)
HATCH_CLAUDE_BIN:  <unset>
HATCH_CODEX_BIN:   <unset>
HATCH_GROK_BIN:    <unset>
HATCH_OLLAMA_HOST: <unset>
HATCH_OLLAMA_TIERS: <unset>
HATCH_AGENT:       claude  (<per-user config>/config)
HATCH_BASE_BRANCH: <unset>
HATCH_RUNNER:      <unset>
HATCH_REPOS:       <unset>
HATCH_WORKSPACE:   <unset - clones go to <per-user config>/workspace>
HATCH_TAGS:        <unset>
```

Run from `<checkout-b>`, after the previous subsection's two `--here` writes -
only `HATCH_TAGS` and `HATCH_WORKSPACE` now come from this checkout's own
file; everything else still folds through to the same per-user file:

```
file:                 <per-user config>/config
checkout:             <checkout-b>/scripts/.env  (written by --here)
runner:               <checkout-b>  (<per-user config>/runners)
identity:         key "<name>", owned by <owner>, scopes: hatch
HATCH_RETRY_SECONDS:  90  (default)
HATCH_BASE:        <origin>  (<per-user config>/config)
HATCH_KEY:         ●●●●●●●●  (<per-user config>/config)
HATCH_CLAUDE_BIN:  <unset>
HATCH_CODEX_BIN:   <unset>
HATCH_GROK_BIN:    <unset>
HATCH_OLLAMA_HOST: <unset>
HATCH_OLLAMA_TIERS: <unset>
HATCH_AGENT:       claude  (<per-user config>/config)
HATCH_BASE_BRANCH: <unset>
HATCH_RUNNER:      <unset>
HATCH_REPOS:       <unset>
HATCH_WORKSPACE:   <dir>  (<checkout-b>/scripts/.env)
HATCH_TAGS:        breakdown  (<checkout-b>/scripts/.env)
```

Each setting line is the name padded out, the value, then two spaces and the
layer it came from in brackets: `exported`, the checkout's `scripts/.env`
path, or the per-user file's path. An unset line carries no layer. The key
prints masked, never in full.

The `identity:` line calls `/api/hatch/me` with whatever origin and key are
folded above, and names the key, its owner and its scopes, or - for a runner
header instead of a key - that calls go out as its name. It prints
`<unreachable>` without calling out at all when no origin is set, and the
same when the call itself fails, so `--show` keeps working fully offline.

### The workspace lock

Two loops serving the same board but sharing a workspace would reset each
other's clones, so a workspace is held like a checkout is. Naming one that is
already held refuses the run outright, before it claims anything:

```
hatch: the workspace <dir> is already served by pid <n>, from <checkout>.
hatch: two loops would reset the same clones - give this checkout a workspace of its own with  hatch config --here --workspace <dir>
```

Leaving the default workspace to the other loop is not a fault - it means
this run serves only the checkouts it already has, and the other loop does
whatever cloning either needs:

```
hatch: another run holds the default workspace <dir> - pid <n>, from <checkout>; this run will not clone
```

`go-to-work` holds whichever workspace it uses for the night; `work` holds it
for the one increment. [When it will not run](#when-it-will-not-run) has both
sentences in the context the renderer prints them in, and [one loop per
served checkout](one-increment-and-the-loop#one-loop-per-served-checkout) has
the lock's detail.

### Splitting the work by column

A tagged column gates every session spawned *out of* it for a runner that
does not advertise the union of the project's, its ancestors', the issue's
and that column's own tags - never the move into an untagged column ahead of
it. That cuts the other way too: a *project* tag the light runner lacks
blocks it from a tagged Breakdown column just as it would from the
implementation column - a project's own tags cannot be used to keep
implementation off a runner that only does breakdown.

The working recipe:

1. On the Statuses page - a person only, a key never - give the Breakdown
   column the runner tag `breakdown`.
2. Give the implementation column (In Progress, on a stock board) the runner
   tag `build`.
3. The light runner in `<checkout-b>` advertises `breakdown` alone; the
   primary runner in `<checkout-a>` advertises `build` alone.

Tags reserve columns; they do not partition the board. An untagged column (To
Do, In Review on a stock board) is open to both runners, a hop is never
gated, and a runner advertising a superset of what a column requires
qualifies same as one advertising exactly that. **Tags seed a runner's row
once** - the first heartbeat naming any writes them onto the board, and
`--here --tag` typed again after that changes nothing until a `PATCH` on the
row itself moves them (see [what a runner
advertises](#what-a-runner-advertises)). `hatch queue`, run in either
checkout, names the missing tag by name when it passes a card over for want
of one. See also [Runner tags](the-board-and-its-columns) for the column-side
flag this recipe sets.

### Starting them

One terminal per checkout, each left running its own loop:

```
hatch go-to-work
```

with `hatch` on `PATH`; in a checkout of Hatch itself, `scripts/hatch.sh
go-to-work` on macOS and Linux, `scripts\hatch.ps1 go-to-work` on Windows (or,
where the execution policy refuses scripts, `powershell -NoProfile
-ExecutionPolicy Bypass -File .\scripts\hatch.ps1 go-to-work`). Each session a
loop spawns reaches that loop's own origin and key, from its standing checkout
or a clone in its own workspace. **Agents → Runners** shows both runners at
once, each with its tags and where it is working from.

A checkout, profile or export that names an origin and no key is keyless on
purpose, whatever the per-user file carries.

## When it will not run

- **`hatch: no claude CLI on PATH.`** The runner looks on `PATH` only — never
  an editor extension's bundled copy, which moves with that extension's own
  updates and would break on somebody else's release schedule. Install the
  CLI, or name it in `HATCH_CLAUDE_BIN`, which must point at a file that
  exists. Under another agent the message names that agent's CLI instead
  (`hatch: no codex CLI on PATH.`) and the setting is `HATCH_CODEX_BIN` or
  `HATCH_GROK_BIN`; `ollama` runs through the `codex` CLI, so it is
  `HATCH_CODEX_BIN` there too.
- **`hatch: cannot tell which branch is the trunk here`.** `origin/HEAD` is
  not set in this clone. `git remote set-head origin -a` fixes it, or name
  the branch in `HATCH_BASE_BRANCH`.
- **`hatch: the tree has changes in it that would not stash`.** A merge in
  progress, or a conflicted file. Clear it by hand; the loop will not guess.
- **`hatch: a go-to-work is already running in <path>, pid <n>`.** One loop
  per checkout. Join that one, stop it, or use another checkout.
- **`hatch: the workspace <dir> is already served by pid <n>, from
  <checkout>.`** Two loops would reset the same clones. Give this checkout a
  workspace of its own with `hatch config --here --workspace <dir>`, or stop
  the other loop. `go-to-work` holds the workspace for the night and `work`
  for the one increment.
- **`hatch: another run holds the default workspace <dir> - ...; this run will
  not clone`.** Nothing is wrong: another loop holds the default workspace, so
  this run serves its own checkouts and the other loop does the cloning.
- **It picked up nothing.** `hatch queue` says why, per card: no playbook
  leads out of that column for that type, an open question, a person's name
  on it, a ready date not yet arrived, another runner's claim, or a project
  bound to repositories this runner has no checkout of.
- **`hatch: could not reach <origin> for 90s - ...`** See [when a call
  fails](cli-reference#when-a-call-fails).

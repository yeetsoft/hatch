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

**Agents → Get the runner** ([`/runner`](/runner)) offers
this platform's download first, with the others underneath for the machine you
are not sitting at, and prints the revision it was built from. That is the same
build as the page serving it, so the two are never a version apart. Four
platforms are built: `win-x64`, `osx-arm64`, `osx-x64` and `linux-x64`.

On a Hatch with the wall up, an admin makes the runner's key from **Agents →
Runners → Create runner**. The secret is shown once, with the `hatch config
--origin` and `hatch config --key` commands that take it (see [Runners and their
keys](people-roles-and-keys#runners-and-their-keys)).

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
echo), the agent, and the path of that agent's CLI. Enter keeps what is shown
in brackets. It writes the answers to a per-user file, so it follows you
between checkouts and you do it once. The asking needs a terminal: redirected
or scripted, bare `hatch config` refuses and exits 1 rather than hang on a
prompt nobody can answer.

An empty origin is refused, and so is one without `http(s)://` in front of it.
Leaving the key blank probes that origin with no key. Against a wall-off
board it offers `Create a runner on this board? [Y/n]`, then a suggested
`Runner name`, and mints a fresh key under that name - written here as this
checkout's key, with no owner until an admin sets one; a name already taken
refuses and writes nothing. Against a wall that is on, the probe is refused
outright and `hatch config` says there is no key - an admin creates the
runner on the Runners page, then `hatch config --key <key>` picks it up. A
key that does not start `hatch_ak_` prints a warning but is carried on
anyway, since the call that follows is the one that actually knows whether
the board accepts it.

Each flag writes one thing and asks nothing:

- `--origin <origin>` writes the origin alone, leaving the key and everything
  else as it was. It is what the Runner page's first line pastes.
- `--key <key>` writes the key alone, and needs an origin already set. A key on
  a command line stays in shell history; the prompt does not, so prefer the
  prompt on a machine other people can read.
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
  Refused outside a checkout, or combined with `--show`.

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
3. the named profile file, when one resolved (`--profile`, `HATCH_PROFILE`, or the active-profile pointer file), at `<per-user config>/profiles/<name>`;
4. the per-user file `hatch config` writes.

`hatch config --here` writes the second layer directly - every other write
mode writes the fourth ([`config`](cli-reference#config)).

`hatch config --show` prints where that file is, so no path is written here.
It is mode 600 wherever the platform has modes; on Windows it inherits the
directory's ACL. A blank value is the same as unset. A flag beats every layer
for its run: `--repo` on `work` or `go-to-work` replaces `HATCH_REPOS` rather
than adding to it.

**Where the profile layer lives.** A profile is the file
`<per-user config>/profiles/<profile-a>`; its name is letters, digits and
hyphens, nothing else. The active profile is the one-line file
`<per-user config>/profile`, and that pointer is what `hatch use` moves
([`use`](cli-reference#use)). `--profile <profile-a>` or an exported
`HATCH_PROFILE` pins one run to a profile instead, and a pinned run ignores the
pointer. A name that resolves to no file refuses the run
(`no such profile "<profile-a>" - looked in <per-user config>/profiles`); a name
given nowhere is not a refusal, it is the no-profile fold of the other three
layers.

**The pairing rule.** `HATCH_KEY` answers only from the layer `HATCH_BASE` came
from. A key in any other layer is ignored, with a line saying which file held
it:

```
hatch: HATCH_KEY in <checkout-a>/scripts/.env is ignored - HATCH_BASE comes from <per-user config>/profiles/<profile-a>, and a key goes with its origin. Set both in <checkout-a>/scripts/.env, or neither.
```

An exported key gets `Export both, or neither.` in the same place. So a layer
that names an origin and no key is keyless on purpose, and `hatch config --here
--origin <origin>` alone leaves that checkout keyless - give it a key in the
same call, or none. *Several runners on one machine* has this in a recipe.

**Board-bound names.** While a profile is active, the per-user file is not
consulted for `HATCH_BASE`, `HATCH_KEY`, `HATCH_WORKSPACE` and `HATCH_REPOS`:
they belong to one board, so the profile answers for them or nothing does,
and `--show` marks each of them
`(profile active: the per-user file was not consulted)`. The machine-bound
names - `HATCH_AGENT`, the three CLI paths, `HATCH_OLLAMA_*` and
`HATCH_BASE_BRANCH` - still fall through to the per-user file, since they
describe the machine rather than the board.

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

A profile with no `HATCH_WORKSPACE` of its own does not share the no-profile
default: it gets its own `hatch/workspaces/<profile>` directory, beside -
never inside - the no-profile `hatch/workspace`, so a clone in the one can
never collide with a profile subdirectory in the other.

These are the settings `hatch --help` lists, with their defaults:

```
HATCH_BASE           the origin of your Hatch
HATCH_KEY            hatch_ak_...; optional against a Hatch with its wall off
HATCH_CLAUDE_BIN     the claude CLI, if it is not on PATH
HATCH_CLAUDE_TOOLS   the tools an unattended claude session starts with (default: Bash,Read,
                     Edit,Write,Grep,Glob,Monitor,TaskStop; `default` is claude's full set)
HATCH_CODEX_BIN      the codex CLI, if it is not on PATH
HATCH_GROK_BIN       the grok CLI, if it is not on PATH
HATCH_AGENT          claude, codex, grok or ollama (default: claude)
HATCH_BASE_BRANCH    the trunk go-to-work resets to between increments
HATCH_ROOT           the checkout to work in (default: upwards from here)
HATCH_REPOS          checkouts a loop with no checkout of its own serves,
                     joined on : (Unix) or ; (Windows)
HATCH_WORKSPACE      where it clones what the board binds (unset: a per-user
                     default; off: never clone)
HATCH_LOG            where its log goes (on: the logs folder beside the
                     per-user file; a directory; unset or off: no log). A
                     work or go-to-work run keeps one file there, named on
                     its first line
HATCH_DANGEROUS      off: refuse work flagged dangerous (on or unset: take it)
HATCH_MODELS         the models this runner advertises, comma-separated
                     (default: the agent's)
HATCH_HAS            what this machine has that detection cannot see,
                     comma-separated (default: nothing)
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
- the active profile, and where it came from - an explicit `--profile`, the
  resolved active one, or `<none>`; the `runner:` line below and an unset
  `HATCH_WORKSPACE:` line name it in turn;
- the runner's name, from the key (`<none - no key>` with no key,
  `<unreachable>` when `/api/hatch/me` cannot be reached);
- who `/api/hatch/me` says the caller is - the key and its owner - or
  `<unreachable>` when there is no origin to ask or the call fails;
- `HATCH_RETRY_SECONDS`, with its layer;
- each setting that can live in a file, with the layer it came from: exported,
  the `.env` path, the profile file `<per-user config>/profiles/<profile-a>`,
  or the per-user file;
- the key masked, never in full;
- `HATCH_REPOS` as a path a line;
- for an unset `HATCH_WORKSPACE`, the directory clones would go to;
- `HATCH_LOG:` as `<unset - no log>`, `off - no log`, or the folder the log
  goes to (the `logs` folder beside the per-user file, for `on`);
- `HATCH_DANGEROUS:` as `<unset - takes dangerous work>`, `on - takes dangerous work`,
  `off - this runner refuses dangerous work`, or a bad value with the sentence that refuses it;
- `HATCH_MODELS:` as a comma-separated list, `<unset - the agent's default>`, or a bad
  value with the sentence that refuses it;
- `has:` - what detection found merged with what `HATCH_HAS` declares, `id` or
  `id version`, comma-separated, or `none`;
- `missing:` - everything neither detection nor `HATCH_HAS` found, comma-separated,
  or `none`. Detected here quietly: an absent id prints no line on stderr the
  way it does for a beat;
- `<id> via:` - one line for each detected id that was asked of the shell or
  fell back to `PATH`: the shell's own invocation, as `zsh -l -i +m`, or
  `PATH - <why>` with the reason the shell did not answer. An id that was not
  found gets none, since `missing:` has it. No line at all on Windows, or with
  no usable `SHELL`;
- a note after the line it explains, when a value was passed over or never
  looked for. `<layer>` is `exported`, a file path, or the profile path:

  ```
  HATCH_KEY: …  (ignored: <layer> holds a key, but HATCH_BASE comes from <layer> - set both there, or neither)
  ```

  That is the pairing rule.

## What a runner advertises

A runner tells the board three facts, each owned differently. *Tags* are a
person's decision: set only on the Runners page, by a `PATCH` on the row,
never by the CLI - the row is where they live. *Models* -
what its agent can spawn - and *has* - what its machine has - are
detections instead: every beat overwrites the row, and no page or `PATCH`
edits them, so a value can never say a machine has a tool it does not. Each
of the three is judged on every dispatch read (`hatch queue`, `next`, `work
KEY`, the loop's own pass) and never on a hop, and each is opt-in on the
wire: a caller sending no `model=`/`agent=` or `has=` is folded by nothing for
it, which is why an older CLI and the issue page are unaffected. Tags are the
exception: a caller sending no `tag=` is judged on the tags of the API key it
called with, so `hatch queue` and `hatch work` run by hand see what a loop
under the same key sees (a `tag=` sent replaces them, never joins them; a key
with none set still advertises none). A call with no key and neither `tag=`
nor `dangerous=` sent is the board's own scan and is judged on no tags at
all, so a person's board-wide read folds nothing on tags.
[Models](#models) and [Has](#has) below have each detection in full. A
container `hatch image build` built advertises two more facts with no
detection of its own, read once at start the same way the six [machine
facts](#what---show-prints) are: the dependency set it was baked with, which
[Has](#has) below says joins `HATCH_HAS`, and the image reference itself -
[Image](#image) below.

A tag is a word a project's, a ticket's, or one of its ancestors' runner tags
require. A project that requires `gpu` is only worked by a runner that carries
`gpu`, and a ticket or an ancestor that requires a tag of its own adds to the
project's, never away from it - a runner must carry the whole union. A column
may require tags of its own too, union'd in the same way, and gating every
move out of it. The union gates every move a runner is handed, in every
column and on every hop - a tag says which runner may work a ticket, not only
which may write its code. **Tags are set only on the Runners page** - a
`PATCH` on the row, never the CLI - and that is where they live. The loop
reads them back on every heartbeat and adopts them, saying so once
(`hatch: the board set --tag ...`).

A runner carries at most 8 tags. Each is at most 32 characters, with no space
or comma inside, and is trimmed and lowercased. A tag that breaks the rule
is refused by the Runners page with the rule's sentence.

A runner that falls short of what a project requires is not refused outright:
`hatch queue` folds the row and says what is missing, by name — *"AER-12 is
worked only by a runner advertising macos and xcode - this runner
advertises linux"* (*"... advertises none"* for a runner with no tags at
all; a call with no key and no `tag=`/`dangerous=` folds nothing here at
all, rather than reading as advertising none).

### Models

A runner can say which models it offers. `HATCH_MODELS` is a comma-separated
list - aliases (`haiku`, `sonnet`, `opus`, lowercased) or pinned ids - trimmed
and deduplicated, at most 100. It is read from the same four layers as any
other setting, and unset means the agent's default.

```
HATCH_MODELS=haiku,sonnet
hatch config --models haiku --models sonnet
```

`--models` is repeatable, and bare `--models` clears them. A comma inside one
value is refused (`--models haiku,sonnet` is one unknown id, not two), with
nothing written. So is a pinned id of a different agent than `HATCH_AGENT`
(blank is `claude`): `"qwen3-coder:30b" is an ollama model - this runner's
agent is claude`. A bad `HATCH_MODELS` in a file or the environment refuses
every command with the same sentence, after `hatch: HATCH_MODELS -`. It is
machine-bound, so a profile still reads it from the per-user file.

**What the default is.** Unset, a runner advertises what its agent can spawn:
for claude, codex and grok the four aliases, `haiku sonnet opus fable`; for
ollama the ids its aliases are mapped to (`HATCH_OLLAMA_TIERS`), in alias order, then
the models its server has pulled, sorted, up to 100. An ollama runner with
nothing mapped and nothing pulled advertises none. A pulled name that is not a
valid Ollama id (`name:tag`) is left out rather than refusing the beat.

**`HATCH_MODELS` overrides the default only for its own agent.** It was
checked against `HATCH_AGENT`, so a run whose `--agent` flag chose another
agent advertises that agent's default instead of a list that would be refused.

**A runner can stop offering a model for a while.** Until a time, or for the
rest of the run, a narrowed model is left off the list the beat sends, whatever
`HATCH_MODELS` or the agent's default says, and it is back once the time
passes. A per-model weekly window the `/usage` probe finds spent narrows that
model until the window resets, and the terminal says so once - see [When it
will not run](#when-it-will-not-run). A supervisor restart keeps what is
narrowed; a fresh start forgets it.
Where every model is narrowed the beat sends an empty list, which clears the
row's models, rather than none, which would leave the last list standing.

**Every heartbeat writes the list.** Unlike tags, the board does not own it:
the row carries whatever the last beat said, so changing `HATCH_MODELS` or
pulling a model on an ollama server shows on the next heartbeat. A runner that
has nothing to advertise sends none and leaves the row's list as it was.

**The models and the agent ride on every dispatch read too.** `hatch queue`,
`hatch next`, `hatch work KEY` and the loop's own pass send one `model=` per
model the runner offers - less any narrowed, read afresh each time - and
`agent=` for its agent; a hop sends neither. A move whose model the runner
does not offer is folded for it, with the board's own sentence: *"HA-12 in
Breakdown runs on fable - this runner's claude does not offer it"*. So a runner
never claims a ticket only to refuse it. A playbook or an issue pinned to a
model id rather than an alias is choosing an agent, not a list ([model and
effort](playbooks#model-and-effort)); a move pinned to another agent's id is
folded the same way, with the board's own clause after the ticket key, as
`queue` shows it: *"pinned to gpt-6-luna, a codex model - this runner's
agent is claude"*. A runner that offers no list, or has narrowed every
model, sends no `model=` and the board judges the agent alone.
`hatch runners` closes a row with `  models haiku sonnet` and then `  has
dotnet 10.0.100, node 22.11.0`. The Runners page draws the dependencies a
runner has the same way, as `has dotnet 10.0.100, node 22.11.0`, under the
repositories line in a row's details.

### Has

A machine can declare what it has that detection cannot see. `HATCH_HAS` is a
comma-separated list of dependencies - each an `id` or `id@version` from the
master list - trimmed, lowercased and deduplicated. It adds to what detection
finds and never removes. It is read from the same four layers as any other
setting, and unset means nothing declared. A machine that would rather not
install a toolchain at all can run a project image instead ([project runner
images](project-runner-images)).

```
HATCH_HAS=dotnet@10.0.100,pwsh
hatch config --has dotnet@10.0.100 --has pwsh
```

`--has` is repeatable, and bare `--has` clears them. A comma inside one value
is refused (`--has dotnet,pwsh` is one unknown id, not two), with nothing
written. So is an id the master list does not know: `` `cobol` is not a
dependency Hatch knows - the list is docker, dotnet, make, node,
playwright-browsers, postgres, pwsh ``. A bad `HATCH_HAS` in a file or the
environment refuses every command with the same sentence, after `hatch: HATCH_HAS -`. It is
machine-bound, so a profile still reads it from the per-user file.

**Detection runs once**, at the start of `go-to-work`, `do-my-work`, `work`
and `queue`, concurrently across all seven dependencies: `make`, `dotnet`,
`node`, `pwsh` and `docker` (and which version each answers), whether a
Playwright browsers cache exists, and whether Postgres answers. `docker` is
present only when `docker info` answers, so the daemon is running, and
`docker compose version` succeeds; its version is the client's. `postgres` is
present when a `postgres`, `pg_ctl` or `pg_isready` binary on `PATH` answers
`--version`; a Docker daemon no longer counts for it. The five tools are asked
of `$SHELL` first, run as `-l -i +m -c` so a version manager loaded from
the profile is seen (`+m` turns job control off), with a 10 second limit, and
of `PATH` (5 seconds) only if the shell fails to answer. That is macOS and
Linux: Windows, or no usable `SHELL`, reads `PATH` alone. So `make`, `dotnet`,
`node` and `pwsh` can take 15 seconds (10 and 5), `docker` up to 20 (10, then 5
for `docker info` and 5 for `docker compose version`), and Postgres up to 15,
since it tries three binaries at 5 seconds each. Because detection runs once,
a Docker daemon that was not running at start is absent until Docker is
started and the loop restarted; stderr says so as
`hatch: docker not detected - the Docker daemon did not answer ...`. An
operator who set `HATCH_HAS=postgres` to cover a machine that only had Docker
needs `HATCH_HAS=docker` now. What
detection finds is merged with what `HATCH_HAS` declares - a declared entry
adds only what detection missed, never removing what detection found - and
the merged set is what a beat advertises: the same one `hatch runners` and the
Runners page draw, above. A supervisor restart is a new process, so it
detects again with no code of its own; an id neither detection nor `HATCH_HAS`
covers says why once on stderr, as `hatch: <id> not detected - <why>`, and an
id read from `PATH` after the shell failed says so once, as `hatch: <id> read
from PATH, not the shell - <why>`.

**A container `hatch image build` built declares a set of its own too** -
baked to `/etc/hatch/dependencies.json` at build time ([`hatch
image`](cli-reference#image) says how). It joins
`HATCH_HAS` as a second declared source, on the same terms: detection still
runs and still wins, so an id the image was built with but the base no longer
carries is not silently believed. Neither declares anything for a runner from
the generic image, or outside one.

The merged set is also sent on every dispatch read - `queue`, `work <key>`
and the loop's own pass - one `has=` per entry, and never on a hop. A move
whose project needs a dependency this runner's set does not satisfy is folded
for it with the board's own sentence, printed unchanged: `AER-1 needs dotnet
10 or newer (acme/repo's .hatch/dependencies.json) - this runner has dotnet
8`. So a runner never claims a ticket only to find its machine cannot run it.
[What a repository needs](#what-a-repository-needs) says where that need is
written.

### Image

A container `hatch image build` built advertises the image it was started
from, with no detection of its own: `HATCH_IMAGE` is baked into the image at
build time and read once at start, the same way the six [machine
facts](#what---show-prints) are. A runner from the generic image, or run
outside one, sends and shows none.

It rides on every heartbeat beside the six machine facts, never on a dispatch
read - a column or a project cannot require one, the way a tag or a
dependency can. `hatch runners` closes a row with `image <ref>` right after
`on <host>`, and the Runners page names it beside the host and the build, in
the row and in its detail. A fresh `loop` beat that names none clears
whatever an older incarnation advertised - a restart onto the generic image
must not keep looking like the one that was built - while any other beat
naming none leaves the row's own value alone, the way `Os` and the other five
machine facts do.

## What a repository needs

**The file.** A repository says what a build of it needs in
`.hatch/dependencies.json` on the trunk of each repository a project binds. The
shape is `{"dependencies": [ ... ]}`, and an entry is an id string or
`{"id": ..., "version": ...}`. The ids are the master list - `make`, `dotnet`,
`node`, `playwright-browsers`, `postgres`, `pwsh`, `docker` - each declared once. A
version is one to three dotted whole numbers, and it is a minimum, met by that
version and anything newer: `22` is met by `22.0.1` and `24.22.0` and not by
`21.9.9`; `10.0.100` is met by `10.0.302` and not by `10.0.99`; `22.1` is not
met by a bare `22`. A need the list lacks is a runner tag instead ([What a
runner advertises](#what-a-runner-advertises)).

**Where it is read.** The runner's poll reads the blob off the trunk through
git, beside `.hatch/playbooks.yaml`, and reports it to `PUT
/api/hatch/dependency-files` ([API reference](api-reference#dependency-files));
the board parses the bytes itself. A project's need is the union over every
repository it binds.

**What it gates.** Development steps only: out of the implementation column, and
the review self-move while it is conflict or build work. Never a hop, never the
move into the implementation column, and never breakdown or analysis, which read
a repository and do not build it. A runner that falls short is folded with the
sentence quoted under [Has](#has).

**An invalid file.** An invalid file folds the project's development steps with
*"acme/repo's .hatch/dependencies.json is invalid at `<sha>` (`<problem>`) - fix it
on the trunk"*. An absent file, or one no runner has read yet, folds nothing, so
a repository that has not opted in is exactly as it was.

**On the Projects page.** In Manage, Projects, editing a project shows what the
board knows beside each bound repository: *needs dotnet 10 or newer, node 22
or newer, ...*, *declares no dependencies*, or *no runner has read its
declaration yet*. An invalid file shows its problem and *last good sha* with
the short sha where one parsed, and a **File** chip to the blob.

**This repository's own file** is the worked example:

```json
{
  "dependencies": [
    "make",
    { "id": "dotnet", "version": "10" },
    { "id": "node", "version": "22" },
    { "id": "pwsh", "version": "7" },
    "playwright-browsers",
    "docker"
  ]
}
```

The Makefile's header comment points at it, so the two move together.

The same file is what a project's runner image is built from: [project runner
images](project-runner-images) follows it from here to a running container.

## What a runner refuses

A ticket can be flagged dangerous: security testing, load testing, anything
that destabilises a process or a machine. **A runner takes dangerous work by
default.** A runner that should not - a shared machine, a laptop somebody is
using - says so:

```
hatch config --dangerous off
HATCH_DANGEROUS=off
```

With `off`, every dispatch read the runner makes (`next`, `queue`, and the read
for a named ticket) carries `dangerous=false`, so the board hands it nothing
flagged, and its heartbeat says it refuses. `on`, or nothing written, takes
dangerous work. Any other value refuses every command with a sentence.
`--here` and `--profile` write the setting where they write anything else, and
it is machine-bound: a profile still reads it from the per-user file.

**The setting is the CLI's own.** It rides every heartbeat the way `remotes`
and `models` do, re-sent afresh each beat rather than seeded once, and the
Runners page draws it from there - it is never a `PATCH` field, and it is
never answered back. The loop's own flag is the only thing that sets it, for
the whole run: `hatch config --dangerous` takes effect on the next start of
the runner it was written for, and there is no row for a running loop to
adopt it from.

A runner can also refuse one ticket rather than every dangerous one: a session
that decides the work is not one this runner can do runs `hatch recuse AER-12
"reason"` and stops there. The board stops handing that runner this ticket,
and the increment ends with `hatch: AER-12 - <runner> recused itself
(<reason>), going on to the next` - no stall comment, no mark, no question.
`hatch omit AER-12 --lift <runner>` undoes it.

### Refused models

A key can also refuse models rather than tickets: a list of models its runner
is never to be told to run, narrower than what it advertises
([Models](#models)). It lives on the board, on the key's own config row -
not `hatch config` and not a `HATCH_*` variable, since a setting here would
be a second store for the same fact.

Two doors reach it. From a terminal, `hatch runners refuse` reads, adds to
and lifts from the calling key's own list -
[CLI reference](cli-reference#runners) has the forms. The Runners page has
its own control over the same row.

The board folds a refused model out of the dispatch read itself, so a loop
never advertises one it has refused and never has to subtract it from its own
`model=` list. A running loop reads the list back on every heartbeat the way
it reads tags, and says once when the board has changed it: `hatch: the board
set refuses fable` (or `refuses none` when the list is cleared). A Hatch too
old to answer with the list leaves the loop's own alone.

## The runner's name

A runner's name is its key's. The board reads it from `GET /api/hatch/me`
and shows it under **Agents → Runners** (*Watching the loop*); `hatch config
--show` marks its own `runner:` line `(from the key)`. Nothing on this side
chooses, records or sends another name - a key and a profile each carry one
name, so two profiles are two runners, and a checkout with no key has none.

A loop with no key refuses before it takes a lock, a claim or makes a request
([`work`, `go-to-work`, `do-my-work`](cli-reference)); a read command still
works, keyless, as the local person.

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
hatch dangerous AER-12 "why"      flag it: running its work could destabilise a machine
hatch dangerous AER-12 --clear    ...clear the flag
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

## Several runners on one machine

One machine, more than one loop - `<checkout-a>` and `<checkout-b>` below
stand for whatever paths you actually clone to, and `<profile-a>` and
`<profile-b>` for whatever you name your profiles. Nothing here needs a second
machine or a second person, though a second Hatch is covered too.

### The shapes

Four ways to arrange it, and which one you want decides the rest:

- **Shape a: two checkouts, one board.** Two clones of the one repository a project
  binds, a `go-to-work` loop standing in each, both reading the same board and
  split by tag (*Splitting the work by column*, below).
- **Shape b: one checkout, two boards, one at a time.** One clone and two
  profiles; `hatch use` moves the loop from one board to the other.
- **Shape c: two checkouts, two boards at once.** Each checkout pinned to a
  profile of its own, each loop reading its own board.
- **Shape d: a set of runners from one command.** `hatch go-to-work --runners <n>`
  (*A set*, below).

**One checkout runs one loop at a time, whatever its profile.** The loop lock
keys on the checkout alone, so shape b is a switch and not concurrency: a
second `go-to-work` in a checkout that already has one running is refused,
under the same profile or another. Running two boards together takes two
checkouts, which is shape c.

### Profiles

A profile is a named file holding the settings that belong to one board - its
origin, its key, its workspace. [Four layers](#four-layers) says
where it sits among the other layers, and what falls through to the per-user
file while one is active; [What --show prints](#what---show-prints) says how
`--show` marks each of those lines. This section is only the commands.

Create one. The file need not exist yet, and `--profile` is refused combined
with `--here`:

```
hatch config --profile <profile-a> --origin <origin>
hatch config --profile <profile-a> --key <key>
```

Origin and key go in the same file, since a key answers only to the origin it
was written beside. Read one back without switching to it:

```
hatch config --show --profile <profile-a>
```

Switch the active profile - the one-line pointer file every unpinned command
reads:

```
hatch use <profile-a>
hatch use
hatch use --none
```

Bare `hatch use` prints the active name, or `<none>`; `--none` clears the
pointer, which puts the loop back on the per-user file alone. `use` is refused
for a name with no profile file.

Pin one terminal to a profile without touching the pointer: put
`--profile <profile-b>` anywhere after the command name, or export
`HATCH_PROFILE`:

```
hatch go-to-work --profile <profile-b>
```

A pinned run ignores the pointer, and so is not moved by a later `hatch use`.

### Per checkout, per board

#### What is already per checkout

One thing already belongs to one checkout and no other, with nothing to set:

- **The loop lock.** One `go-to-work` or `work` at a time per checkout; a
  second naming the same checkout is refused, with the pid of the one already
  holding it ([one loop per served checkout](one-increment-and-the-loop#one-loop-per-served-checkout)).

The runner's name is not one of these - it is the key's, wherever that key is
read from ([the runner's name](#the-runners-name)), so `<checkout-a>` and
`<checkout-b>` on the same key are the same runner.

What is **not** per checkout is everything else. The per-user file - its
directory is the OS application-data folder, `%APPDATA%\hatch` on Windows,
`~/Library/Application Support/hatch` on macOS and `$XDG_CONFIG_HOME/hatch`
(default `~/.config/hatch`) on Linux; `hatch config --show` prints the exact
path, and is the one answer that is always right - holds the origin, the key, the agent and the workspace for
every checkout on the machine, until a profile or a checkout's own
`scripts/.env` overrides a line of it. Two checkouts left on the one `hatch
config` are the same runner wearing two names: same board, same key, same
workspace - rarely the point of running two.

#### Setting up the second checkout

In `<checkout-b>`:

```
hatch config --here --workspace <dir>
```

The light runner's tags are set on the Runners page, not here - `breakdown`
for the light runner, `build` for the primary - and the loop adopts them on
its next beat.

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

Or give the board a profile, as in *Profiles* above, and pin `<checkout-b>`
to it: the better home when the board is one you will want to switch to
later.

A key on a command line stays in shell history; the bare `hatch config
--here` prompt asks for each in turn without echoing the key, and Enter on
any prompt keeps only what this checkout's own file already carries - never a
value read in from the per-user file or an export, even though the bracket
shown is whatever is actually active.

**Warn:** a key is read only from the file that carries the origin it pairs
with, so `--here --origin` alone leaves `<checkout-b>` keyless - the per-user
key is ignored, with a line saying which file held it - until `--here --key`
gives this checkout a key of its own. A checkout, profile or export that names
an origin and no key is keyless on purpose, whatever the per-user file
carries.

#### What --show prints in each checkout

Run from `<checkout-a>`, which carries no overrides of its own and follows
the per-user file:

```
file:             <per-user config>/config
checkout:         <checkout-a>/scripts/.env  (written by --here)
profile:          <none>
runner:           <name>  (from the key)
identity:         key "<name>", owned by <owner>
HATCH_RETRY_SECONDS:  90  (default)
HATCH_BASE:        <origin>  (<per-user config>/config)
HATCH_KEY:         hatch_ak_xxx...xxxx  (<per-user config>/config)
HATCH_CLAUDE_BIN:  <unset>
HATCH_CLAUDE_TOOLS:  <unset>
HATCH_CODEX_BIN:   <unset>
HATCH_GROK_BIN:    <unset>
HATCH_OLLAMA_HOST: <unset>
HATCH_OLLAMA_TIERS:  <unset>
HATCH_AGENT:       claude  (<per-user config>/config)
HATCH_BASE_BRANCH: <unset>
HATCH_REPOS:       <unset>
HATCH_WORKSPACE:   <unset - clones go to <per-user config>/workspace>  (profile: <none>)
```

Run from `<checkout-b>`, pinned to `<profile-b>` - shape c - which was
created with `hatch config --profile <profile-b> --origin <origin>`, then
`--key <key>`. Every board-bound line now answers from
the profile or from nothing, and says so; `HATCH_AGENT` describes the machine,
so it still folds through from the per-user file:

```
file:             <per-user config>/config
checkout:         <checkout-b>/scripts/.env  (written by --here)
profile:          <profile-b>  (<per-user config>/profiles/<profile-b>)
runner:           <name>  (from the key)
identity:         key "<name>", owned by <owner>
HATCH_RETRY_SECONDS:  90  (default)
HATCH_BASE:        <origin>  (<per-user config>/profiles/<profile-b>)  (profile active: the per-user file was not consulted)
HATCH_KEY:         hatch_ak_xxx...xxxx  (<per-user config>/profiles/<profile-b>)  (profile active: the per-user file was not consulted)
HATCH_CLAUDE_BIN:  <unset>
HATCH_CLAUDE_TOOLS:  <unset>
HATCH_CODEX_BIN:   <unset>
HATCH_GROK_BIN:    <unset>
HATCH_OLLAMA_HOST: <unset>
HATCH_OLLAMA_TIERS:  <unset>
HATCH_AGENT:       claude  (<per-user config>/config)
HATCH_BASE_BRANCH: <unset>
HATCH_REPOS:       <unset>  (profile active: the per-user file was not consulted)
HATCH_WORKSPACE:   <unset - clones go to <per-user config>/workspaces/<profile-b>>  (profile: <profile-b>)  (profile active: the per-user file was not consulted)
```

A profile with no workspace of its own gets one beside the no-profile default,
never inside it, so the two loops here do not meet in the workspace lock
below. A line written with `--here` instead would show
`<checkout-b>/scripts/.env` as its layer, and keep the note.

Each setting line is the name padded out, the value, then two spaces and the
layer it came from in brackets: `exported`, the checkout's `scripts/.env`
path, the profile file's path, or the per-user file's path. An unset line
carries no layer. The key prints as its first twelve and last four
characters, or `(set)` when it is short, never in full.

The `identity:` line calls `/api/hatch/me` with whatever origin and key are
folded above, and names the key and its owner. It prints `<unreachable>`
without calling out at all when no origin is set, and the same when the call
itself fails, so `--show` keeps working fully offline.

#### The workspace lock

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

#### Splitting the work by column

A tagged column gates every move *out of* it, hops included, for a runner
that does not advertise the union of the project's, its ancestors', the
issue's and that column's own tags. A column's own tags never reach the
column ahead of it. That cuts the other way too: a *project* tag the light
runner lacks blocks it from every column - a tagged Breakdown column, an
untagged one and the implementation column alike - so a project's own tags
cannot be used to keep implementation off a runner that only does breakdown,
and a runner that lacks one is not handed a ticket of that project at all.

The working recipe:

1. On the Statuses page - a person only, a key never - give the Breakdown
   column the runner tag `breakdown`.
2. Give the implementation column (In Progress, on a stock board) the runner
   tag `build`.
3. The light runner in `<checkout-b>` advertises `breakdown` alone; the
   primary runner in `<checkout-a>` advertises `build` alone.

Tags reserve columns; they do not partition the board. An untagged column (To
Do, In Review on a stock board) is open to both runners, as long as the
project, the ticket and its ancestors require nothing the runner lacks - a
hop is gated exactly as any other move is - and a runner advertising a
superset of what a column requires qualifies same as one advertising exactly
that. **Tags are set on the Runners page**, and the loop adopts them on its
next beat (see [what a runner
advertises](#what-a-runner-advertises)). `hatch queue`, run in either
checkout, names the missing tag by name when it passes a card over for want
of one. See also [Runner tags](the-board-and-its-columns) for the column-side
flag this recipe sets.

### A set

One terminal per checkout, each left running its own loop:

```
hatch go-to-work
```

with `hatch` on `PATH`; in a checkout of Hatch itself, `scripts/hatch.sh
go-to-work` on macOS and Linux, `scripts\hatch.ps1 go-to-work` on Windows (or,
where the execution policy refuses scripts, `powershell -NoProfile
-ExecutionPolicy Bypass -File .\scripts\hatch.ps1 go-to-work`). Each session a
loop spawns reaches that loop's own origin and key, from its standing checkout
or a clone in its own workspace. **Agents → Runners** shows what each loop
reports. Loops that report under two names are two rows; two checkouts that
report under one name are one row with a quieter line beneath it for each
checkout, giving where it is working from, its agent and OS, and what it is
doing - the same list `hatch runners` indents under a runner.

One terminal, several runners: `--runners <n>` (1 to 16) starts `n` of them
from the one command, instead of a checkout and a terminal for each:

```
hatch go-to-work --runners 3
```

Runner 1 is this checkout, served as a lone `go-to-work` would. Each of the
others gets:

- **No name of its own.** It calls `/api/hatch/me` with the same key as
  runner 1, so it reports under the same name (see [the runner's
  name](#the-runners-name)); **Agents → Runners** tells the members of a set
  apart by workspace, not by name.
- **A workspace of its own.** That is `<workspace>.n` - a sibling of the one
  being served, never a directory inside it - where it clones the repositories
  the project binds.
- **A lock of its own.** It holds that workspace like any loop does (see [the
  workspace lock](#the-workspace-lock)).

Ctrl-C and `--stop-file` stop all of them, each after the increment it is in,
and the output of each line is prefixed `[n]`. `--max-runs`, `--max-spend`
and `--max-tokens` bound the set's combined total, not each runner's own: each
runner checks the sum of what the set has done, between its increments, and a
restart of the set carries that sum forward. `--max-tokens-per-hour` is a wait
and not a stop, and it judges the set's combined last hour: each runner reads
the others' entries from the set's ledger and waits on its own. A second
`--runners` set started over the same workspaces is refused: runner 1 meets the checkout lock (`a
go-to-work is already running in <path>`) and the others meet the workspace
lock, the same refusals as in [the workspace lock](#the-workspace-lock) above.
`--runners` needs a workspace, so `--workspace off` is refused, and
`do-my-work` takes it too; [cli-reference](cli-reference#go-to-work) has the
flag.

With `HATCH_LOG` set, the host writes the set's one log
(`go-to-work-set-<yyyyMMdd-HHmmss>.log`), each member's lines prefixed `[n]` as
on screen, and the members write none. A restart of the set appends to the same
file.

### Switching the set

`hatch use <profile-b>` moves a running set to another board. Every member
that follows the active profile - one started without `--profile` or
`HATCH_PROFILE` - looks at the pointer between increments and never inside
one. The first to see it change exits `75` (`RestartExitCode`), and the set
host writes the set's restart file, so every other member finishes the
increment it is in and stops; then the whole set comes back on the new
profile's board, with the night's totals carried forward. A member pinned with
`--profile` or `HATCH_PROFILE` never looks.

Unsupervised, or run with `--no-restart`, there is nothing to bring the set
back, so it stops instead and says why. [Restarts](one-increment-and-the-loop#restarts)
has the rule in full, including a pointer that names a profile with no file.

## Starting it at login

`hatch standby install` registers a login item that starts this checkout's
loop at every sign-in, with no terminal and no signed-in session required:

- **Windows** - a Task Scheduler logon task, registered with `schtasks
  /Create /XML` under the signed-in user (no elevation needed), run through
  `conhost.exe --headless` so no window opens.
- **macOS** - a launchd agent (`~/Library/LaunchAgents/<id>.plist`) with
  `RunAtLoad`; nothing ever calls `launchctl`.
- **Linux** - a systemd user unit, `enable`d but never started with `--now`,
  where `systemctl --user show-environment` succeeds; otherwise an XDG
  autostart entry (`~/.config/autostart/<id>.desktop`), for a login that
  never starts the user session systemd would run the unit under.

It starts the loop **paused**: the loop heartbeats but takes no ticket until
something sets it running - the Runners page's **Resume**, or a `go-to-work
--standby` run by hand reaching the same row. Everything the loop prints
lands in its log, at the per-user hatch folder's
`logs/standby-<fingerprint>.log` (`%APPDATA%\hatch\logs\...` on Windows),
which a crash leaves readable.

`hatch standby remove` forgets the login item alone - a loop already running
keeps running. `hatch standby show` (or bare `hatch standby`) names whether
the item is installed, the command and log it uses, and the pid of the loop
holding this checkout's lock right now, if any.

In Hatch's own checkout - where `scripts/hatch.sh` and `src/Hatch.Cli` both
exist - the registered command runs the supervisor (`hatch.ps1` on Windows,
`hatch.sh` elsewhere) rather than the installed binary directly, so a
`src/Hatch.Cli` change landed on the trunk restarts the standby loop as the
new build, the same as any other supervised run.

Running `scripts/hatch.ps1` at all needs the execution policy to allow it:
`Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`, or run it through
`powershell -NoProfile -ExecutionPolicy Bypass -File`.

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
- **The agent never started** (the CLI wrote something and ended before its
  first stream event):
  - the increment ends released `unstarted`, which the board's let-go count
    steps over;
  - the terminal's tally lists it as
    `hatch:   unstarted  HA-12  the agent never started - the CLI said "..."`
    (the quoted last line is left off where the CLI wrote nothing);
  - the ticket gets one comment, *"An unattended increment could not start its
    agent on this runner and left HA-12 untouched."*, then what the CLI wrote in
    a fenced block and *"It exited N."*;
  - it is **not a stall**: no let-go comment, no mark, no question, and not
    one of the three failed increments - it neither resets nor advances them.
    A `--quiet` run is never read this way.
- **The agent is signed out** (an expired OAuth session, a rejected key, a
  prompt to log in again, however far the session got): one increment is
  enough - the ticket goes back with one comment saying the agent needs
  signing in again, and the runner pauses itself at once rather than waiting
  for a second run to say the same thing twice. See
  [troubleshooting](troubleshooting#the-runner-says-its-agent-needs-signing-in).
- **`hatch: {model} is off this runner's list until {clock}`** (or `for the
  rest of this run`):
  - the increment ends released `unservable` - the model is one the account
    cannot spawn, learned from the one refusal;
  - the terminal's tally lists it as
    `hatch:   unservable  HA-12  fable is not one this runner can spawn`
    (three spaces after `hatch:`, two after `unservable` and the key, as
    `GoToWork.cs:420` writes it);
  - the ticket gets one comment, *"This runner's Claude no longer offers
    fable, until 3:40am."* (or *"..., for the rest of this run."*), then the
    branch, sha and resume command (`Lifecycle.cs:340`; `Claude` is the
    adapter's display name);
  - it is **not a stall**: no let-go comment, no mark, no question, not one
    of the three failed increments - the next pass does not take that
    ticket, because the beat drops the model and the board folds it with
    the model sentence in `### Models`;
  - a restart keeps the narrowing, a fresh start forgets it
    ([one increment and the loop](one-increment-and-the-loop#stalls)).

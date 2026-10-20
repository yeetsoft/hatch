---
title: The runner
section: Running agents
order: 10
summary: Getting the runner onto your machine, pointing it at a board, and where its settings and its name live.
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

## Coming

Pages this one names that are not written yet:

- `the-container-runner`
- `watching-the-loop`
- `people-roles-and-keys`
- `environment-variables`

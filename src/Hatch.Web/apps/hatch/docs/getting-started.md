---
title: Getting started
section: Start here
order: 20
summary: From a clean machine to a first `hatch work`, in the order you do it.
---

**The two sentences to send a friend:** *Clone this repository, install Docker
Desktop, then run `docker compose up -d` from the repository root and open
http://localhost:8080/apps/hatch/. That is a project board of your own, with a
page inside it that hands you the runner that works your tickets for you.*

The rest of this page is those two sentences, in order.

## Prerequisites

- **A checkout of this repository**, and **Docker Desktop**, running. `docker
  compose` builds the board's own image from source, so there is nothing else
  to install to bring it up.
- **On Windows, either PowerShell** — 5.1, the blue one that ships with the OS,
  or 7 (`pwsh`). Where the two shells differ, the line is shown for each.
  Backing up needs 7: see
  [restoring](backup-restore-and-replication#restoring).
- **git**, and the **`claude` CLI, logged in**. Neither is needed to run the
  board; both are needed by the runner, which branches, commits and pushes from
  a checkout and spends each increment inside a `claude` session. The Runner
  page inside Hatch links all three.
- **`gh`, signed in — optional** ([cli.github.com](https://cli.github.com/);
  `gh auth login`, or `HATCH_GH_TOKEN` for the container runner). It turns on
  build checks, noticing merged pull requests, and merging approved pull
  requests. It also reads the build on the trunk's tip, which landing on the
  trunk needs: a repository with no CI reads as *no checks*, which a project
  with *Checks are required* unticked lands on after a settle window. Without
  it the runner still works tickets; it just never reads a build, never notices
  a merge and never merges a pull request. A ticket that lands on the trunk is
  still recorded and confirmed in the trunk, but waits as pending, since no
  build is ever read for it.

## The one line

From the repository root:

```
docker compose up -d
```

Then open **http://localhost:8080/apps/hatch/**. Compose says nothing about the
address once it has gone to the background, so that part is on this page rather
than in your terminal. The line is the same in both PowerShells and in a macOS
terminal, character for character.

The first run takes a few minutes — it builds the image, runs the migration to
completion, and only then starts the API.

Port 8080 is only the default. If something on your machine already has it,
`HATCH_PORT` moves it:

macOS / Linux:

```
HATCH_PORT=9090 docker compose up -d
```

PowerShell:

```
$env:HATCH_PORT=9090; docker compose up -d
```

Wherever this page writes `<your-hatch-origin>`, it means whatever that address
turned out to be — `http://localhost:8080` unless you moved it.

## Opening the board and naming yourself

Open that address. That is the board, and it is empty in a particular way: the
seven columns are there — Draft through Done, the ones every Hatch ships with —
and there is no project yet, so there is nothing for an issue key to be made of.
(On a phone it opens differently: [Hatch on your phone](hatch-on-your-phone).)

A card that reaches Done does not sit there forever: by default it leaves the
board two days after it closes, so the column's count stays a true read of what
just shipped rather than a growing pile of everything that ever has. The card is
not gone — it can still be found by searching: press `/` on the board and take
the trailing **Search everything** row, or follow the column's own `last 2 days`
note, which opens Search already filtered to what closed there.

Unless you put up the wall ([the wall, off, on and
required](the-wall-off-on-and-required)), there is no sign-in. A Hatch started this way does not ask who you are and has
nowhere to look it up, so it signs what you do with whatever your shell already
called you (`USER` on macOS and Linux, `USERNAME` on Windows), and calls you
`friend` when neither is set. The nav bar says which it used.

**Settings → Your name** changes it, and is worth doing before you file
anything. Every comment, every move and every event on this board is signed with
that name forever afterwards, and a board where half the trail says `friend` is
a board that cannot answer "who did this." The field appears only on a Hatch
with its wall off.

## Your first project

Open **Manage → Projects** ([`/apps/hatch/projects`](/apps/hatch/projects)). The
**New project** card asks for two things:

- **Key** — two to six characters, e.g. `HOME`. It is what every issue number is
  made of: `HOME-1`, `HOME-2`, and so on.
- **Name** — what the project is called to a person.

Press **Add project**. A project is a key namespace, not a container: one board
holds every project, and a project only says which numbers its tickets carry.

Then find the project's row and press **Edit**. The dialog's **Repositories**
part is where you bind it to a git repository: add a remote and, if you want
one, a base branch; use the arrows to reorder them (the first is the primary);
**Remove** takes one off. Nothing is stored until **Save**. Binding is how the
board knows which repository a project's tickets are about.

## Your first epic

Press **New issue** on the board, set **Type** to *epic* — it opens on *task* —
and **Project** to the one you just made, give it a **Title**, and press **File
it**. The server puts it in the leftmost column, **Draft**, and it crosses three
columns before anything is chosen to be built:

- **Draft** is yours, and nothing reads it. Write what you want in the
  description, then drag the epic to **Breakdown**.
- **Breakdown** is a session's. The stock *breakdown (epic)* playbook rewrites
  the description as the epic's overview, files stories under it and moves it
  on to **Backlog**. That session is the first `hatch work`, in the next part.
- **Backlog** is yours again, and it is the gate: specified work waiting to be
  chosen. The stories a session files are born here too.

That is where this page stops. Choosing what to start — Backlog to the column
after it — is the first decision that is yours;
[the board and its columns](the-board-and-its-columns) and
[the lifecycle of a ticket](the-lifecycle-of-a-ticket) take it from there.

## Getting the runner

The board is a board; the **runner** is what works the tickets. It is one
binary, and it runs on your machine.

**Agents → Get the runner** ([`/apps/hatch/runner`](/apps/hatch/runner)) detects
your platform, offers the matching download, and prints the revision it was
built from. Put the file on your `PATH` as `hatch` (`hatch.exe` on Windows). On
macOS and Linux it needs marking executable first:

```
chmod +x hatch
```

Then point it at the board, and ask it for work. These two lines are the same in
every shell:

```
hatch config --origin <your-hatch-origin>
hatch work
```

`hatch config --show` says what is set, and which layer it came from. A Hatch
with its wall off needs no key, and `hatch config` takes a blank when it asks.
With the wall up, the runner needs an API key — an admin mints one under
**Manage → API keys**, and
[putting a front door on it](the-wall-off-on-and-required#putting-a-front-door-on-it) (step 5) has the line
that hands it over.

`hatch work` is one increment: it claims the next actionable ticket, runs one
session on it, and exits. Exit code 2 means there was nothing to do. Run it
inside a checkout of the repository the project is about. To see what it would
do first, `hatch work --dry-run` prints the prompt and claims and spawns
nothing.

The rest is in [one increment and the loop](one-increment-and-the-loop):
[one increment, `hatch work`](one-increment-and-the-loop#one-increment-hatch-work),
and [the loop, `hatch go-to-work`](one-increment-and-the-loop#the-loop-hatch-go-to-work),
which runs increments back to back. How the binary gets onto a machine and finds
its board is in [the runner](the-runner).

## What to read next

- [Welcome](welcome), for what Hatch is and the one rule behind it, if you want that first.
- [The lifecycle of a ticket](the-lifecycle-of-a-ticket), for a ticket's path across the board, end to end.
- [The board and its columns](the-board-and-its-columns), for what happens in each column and who moves work out of it.
- [One increment and the loop](one-increment-and-the-loop), for what `hatch work` and `hatch go-to-work` do, every flag they take, and how a night ends.
- [The wall, off, on and required](the-wall-off-on-and-required), for signing in and running this somewhere more than one person reaches.
- [Letting the loop merge for you](letting-the-loop-merge-for-you), for what has to be true before an approved pull request merges with nobody watching.
- [Hatch on your phone](hatch-on-your-phone), for installing it as an app, and what it can and cannot do there.
- [The CLAUDE.md block](the-claudemd-block), for the block to paste into this repository's own `CLAUDE.md` so an agent can reach the board.

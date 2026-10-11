# Working on Hatch

Notes for Claude. Read [`README.md`](README.md) for what Hatch is,
[`docs/ethos.md`](docs/ethos.md) for the one rule that constrains every commit
(*nothing in this repo may be true of exactly one installation*), and
[`docs/hatch.md`](docs/hatch.md) for the tracker every ticket below comes off.

## Hatch: the ticket is the unit of work

[Hatch](docs/hatch.md) is the house project tracker, at `hatch.${DOMAIN}`. It is
where work is described, and it is reachable programmatically — so a link to a
ticket is a complete instruction, and the ticket is where the answer goes back.

`hatch` is the calls a working session actually makes — `board`, `next`,
`queue`, `show`, `comments`, `start`, `move`, `comment`, `pr`, `depends`, `ask`,
`questions`, `answer`, `config`, `work`, `go-to-work`, `do-my-work`, and `api`
for everything else. It finds a column by name rather than by id, on the
letters and digits alone, so `todo` reaches the column the board calls `To Do`.
Prefer it to raw `curl`; the raw calls below are what it is doing. `hatch
--help` lists the surface, and every subcommand takes `-h` for its own.

`--mine` on `work`, `go-to-work` and `queue` narrows to the caller's own
tickets — assigned to the person the calling key belongs to, or to the key
itself — never to a person's tickets generally: plain `go-to-work` still skips
every person's own, exactly as it always has. `hatch do-my-work` is exactly
`go-to-work --mine`, for a friend on a smaller budget who wants to spend it on
their own tickets and nobody else's. Whose tickets a key's `--mine` reaches is
set by an admin on the API Keys page, never by the key itself.

It is one program — [`src/Hatch.Cli`](src/Hatch.Cli), published as a single
binary for macOS, Windows and Linux — because an operator who clones Hatch has
no copy of this repository's scripts on their `PATH`. Only `work`,
`go-to-work` and `do-my-work` need to be run inside a git checkout.

**In this checkout, [`scripts/hatch.sh`](scripts/hatch.sh) reaches every one of
those commands**, finding or building the binary and handing over. Use it where
`hatch` is not installed; the two are the same commands and the same arguments,
and this file writes `./scripts/hatch.sh` below for exactly that reason. In
Windows PowerShell or PowerShell 7 it is [`scripts\hatch.ps1`](scripts/hatch.ps1),
the same door and the same supervisor; where the execution policy refuses
scripts (the Windows client default), run
`powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\hatch.ps1 <command>`,
or allow the account once with `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`.
A session on Windows runs its commands in Git Bash, where `hatch.sh` runs the
built `hatch.exe`.

Access is an API key — `Authorization: Bearer hatch_ak_…` — read from three
layers, highest first: an exported `HATCH_BASE`/`HATCH_KEY`, then
`scripts/.env` in this checkout (ignored by git), then the per-user file
`hatch config` writes. **The key lives outside the artifact**: never a tracked
file, never a value in a commit, never pasted into a plan or an issue. Hatch is
headed for release to other operators, and a credential in the artifact is one
operator's credential inherited by everyone who clones it. The key carries the
`hatch` scope and reaches `/api/hatch/*` and nothing else, so a `403` means the
key is working and the route is not one a key may take — ask the operator.

The key is optional against a Hatch running with its wall off, where calls name
themselves with a runner header instead. So a `401` says which of two things
happened: a key that was sent and refused, or no key against a Hatch that wants
one.

### If the loop spawned you

**This is most likely how you got here.** `go-to-work` (or `do-my-work`, the
same loop narrowed to one person's own tickets) runs increments back to
back — the next actionable issue, one increment, ask again — so assume nobody
is reading the terminal, and that the next increment starts the moment yours
ends. Most of what used to be said here travels in the prompt itself now,
since an operator who clones Hatch has no copy of this file — what is left
below is what the prompt does not carry.

- **The tree's state is the prompt's `## The branch` section**, not this
  one — it overrides any branching instruction in the playbook, including
  cutting a branch it did not name.
- **A hop may have carried the ticket here unread** — express, a sub-epic or
  child whose epic is already running (`docs/hatch.md#the-hop`). A top-level
  epic is never carried this way.
- **What you leave is put right, but never pushed, for you.** The loop aborts
  a half-done merge and stashes the rest, recoverably. A tree it cannot
  reset ends the night, so clear a conflict before you stop.
- **A ticket that does not move is a stall.** The first in a row is let go
  quietly, with a comment saying why; the second is marked stalled and sits
  fifteen minutes before the board resumes it on its own, until it has
  stalled enough times in a row to hold for a person instead. Filing issues
  under it counts as progress either way.
- **Say what has to land in order**, which column order does not:

  ```
  ./scripts/hatch.sh depends AER-13 AER-12   # AER-13 waits on AER-12
  ```

  Gates the one move into the column where code gets written; everything
  left of that still files and still lands in the backlog.
- **`./scripts/hatch.sh queue`** prints every issue a pass would look at and
  why, or that it is clear. A column with no playbook looks finished from
  outside and may not be.

A playbook edit is refused by the API on purpose — say so on the ticket
instead. Work on the loop itself (`src/Hatch.Cli`, `scripts/hatch.sh`,
`scripts/hatch.ps1`, `src/Hatch.Contracts`) restarts the loop rather than
waiting for the next night.
[`docs/hatch.md`](docs/hatch.md#what-makes-an-issue-actionable) has what
makes an issue actionable at all.

### When a decision is not yours to make

The prompt's `## When you cannot decide` section carries this in full —
name the choices, one call per question, then stop. Nothing here repeats
it; `./scripts/hatch.sh ask AER-12 "..."` is the call it describes.

### Given a ticket

A `hatch.${DOMAIN}/issues/AER-12` link, or a bare `AER-12`:

```
GET  ${HATCH_BASE}/api/hatch/issues/AER-12          # title, description, status, parent, children
GET  ${HATCH_BASE}/api/hatch/issues/AER-12/comments
GET  ${HATCH_BASE}/api/hatch/issues/AER-12/questions?open=false   # decisions asked for, and given
GET  ${HATCH_BASE}/api/hatch/issues/AER-12/events   # what has happened to it, newest first
```

Read it, then get to work. The description is markdown and is the brief.

### Filing work: see `docs/hatch-planning.md`

An increment that **files or reshapes work** — an epic into stories, a story
into tasks, dating something that cannot start yet, or moving a batch — reads
[`docs/hatch-planning.md`](docs/hatch-planning.md) first: `POST
/api/hatch/issues` with a `parentKey`, the `readyAt`/`dueAt` dates, the
`issues` filters, the bulk endpoint, and the `/api/hatch/plan` reads that say
how far along a project is.

A separate file, since an implementation increment never needs it and this
one loads on every increment. Two rules from it matter enough to repeat:

- **An epic takes stories; a story takes tasks.** Plan on the ticket, not in a
  chat log — acceptance criteria go in the description, in a form somebody else
  could check.
- **Filing stories in order is not saying they are ordered.** Work that must
  land in sequence says so with `./scripts/hatch.sh depends`, one call per edge.

### Implementing a ticket

- Move it to **in progress** before starting, so the board says what is being
  worked on right now. That is the board's whole job.
- `POST /api/hatch/issues/AER-12/comments` with the commit sha and the branch —
  the ticket is where somebody looks in six months, and a comment naming a
  commit is what makes that search short.
- `./scripts/hatch.sh pr AER-12 <url>` if you opened a pull request. `pr AER-12`
  with no URL reads back the one that is set, and `--clear` takes it off. That
  pull request's title begins `AER-12 ` and its description opens with the line
  `[AER-12](<origin>/apps/hatch/issues/AER-12)`, then a blank line; `<origin>`
  is `HATCH_BASE` unless the install sets a public address, and the link is
  never relative.
- **Never move a ticket to a terminal or a deferred status.** Only the operator
  decides that something shipped, and only the operator decides that something
  is not worth doing now. An increment ends in the column its prompt's "Where
  this increment ends" section names — not a fixed column, since a story or
  task's target varies — with a comment saying what landed and what did not;
  work that should be shelved is said on the ticket.
- **End by saying what you did**, in a fenced block:

  ~~~
  ```work-log
  A title naming what this session did

  The summary, under 100 words.
  ```
  ~~~

  `hatch` lifts that out of the last thing the session says and posts it,
  with what the increment cost, as one row of the **work log** on the ticket —
  the only record anywhere that knows *which* ticket the money went on. Write it
  last, and write it once: whichever block comes last wins.

Every one of those calls writes an event carrying the key's name as the actor,
so the trail says who did what without anybody being asked to record it.

## House rules

- Build with `make` (`make build`, `make test-api`, `make test-web`), never a
  bare `dotnet` — the npm step needs the shell profile.
- Never commit unless asked.
- No hardcoded domains, addresses, hostnames, or people, anywhere — including
  in plans and comments. See [`docs/ethos.md`](docs/ethos.md).
- The operator does all browser and UI verification. The implementer's
  definition of done is lint, build and tests green.

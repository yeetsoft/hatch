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
`queue`, `show`, `start`, `move`, `comment`, `pr`, `depends`, `ask`,
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
back — the next actionable issue, one increment, ask again — so assume nobody is
reading the terminal, and that the next increment starts the moment yours ends.

- **The tree is on the issue's branch, or on the trunk with a name to cut, and
  it is current.** The loop fetches and resets every checkout the ticket's
  project is bound to onto its default branch, then looks on origin for the
  one branch named for the key (`<key-lowercased>` or `<key-lowercased>-…`). If
  there is one, you start on it, at origin's tip, with the trunk already merged
  in; if there is none, you start on the trunk and are told the name to cut. Do
  not go looking for a base, and do not cut a branch the prompt did not tell
  you to: the prompt's `## The branch` section is the state you are in, and it
  overrides any branching instruction in the playbook. A merge that conflicted
  is left *in progress* — resolve it and commit it before anything else. Two
  unmerged branches for one key stop the loop from spawning at all: it asks on
  the ticket which to use, and you will find the answer under **Decisions
  already made**.
- **A hop may have carried the ticket here, with nobody reading it first.**
  Express, a sub-epic entering the WIP section because the epic above it is
  already running there, or a story or bug under an epic already running
  there — the trail on the ticket names which; see "the hop" in
  [`docs/hatch.md`](docs/hatch.md#the-hop). A top-level epic is never carried
  this way: its own column is the signal, and only a person moves it in. It is
  not a session and not a stall: nothing was spent, and the increment you are
  running now is the first one to actually look at it.
- **What you leave is put right for you, and said on the ticket.** When you
  stop, and before the claim is let go, the loop aborts a merge, rebase or
  cherry-pick you left in progress, stashes what is uncommitted (under a message
  naming the ticket — recoverable with `git stash pop`, but not where you left
  it), names any commits on the issue's branch that origin does not have, moves
  commits left on the local trunk to `<key-lowercased>-rescued-<sha>`, puts every
  checkout back on the trunk, and merges the trunk into your pull request's
  branch on origin when it has fallen behind — without your tree, and without a
  force-push. It never pushes your work for you: *green before pushed* is still
  yours, so work that matters is committed **and pushed**. A tree that cannot be
  reset ends the night, so clear a half-finished merge or a conflicted file
  before you stop rather than leaving it for whoever runs next. A ticket for a
  repository this loop does not have is folded past, with the reason, rather
  than dispatched — so nothing here is ever spawned in the wrong checkout.
  **The one exception is running out of Claude usage mid-session**: you do not
  get to say whether what you left is fit to publish, so the loop commits it —
  untracked files included — onto your issue's branch and pushes it for you,
  then says so on the ticket along with when it expects you back.
- **Leave the ticket somewhere new.** An increment that ends with the ticket in
  the column it started in is a *stall* — unless it filed issues under the
  ticket, which is progress whether or not the ticket moved — and the first
  one in a row is let go quietly: the loop comments saying why, and moves on —
  the ticket is free for
  the very next pass, since most of the time whatever happened is weather and
  a retry a few minutes later just works. The second stall in a row on the
  same ticket is flagged the way every stall used to be: the loop comments,
  opens a question against the issue, and nothing further is dispatched there
  until a person answers it. That guard exists so a ticket that is genuinely
  stuck costs two increments and not a night, but a sentence you wrote about
  why you stopped is worth more than the one it writes for you.
- **Asking is a full stop, not a pause.** An open question blocks the ticket
  from being dispatched at all. Ask and stop — do not ask and keep building.
- **Record the pull request**: `./scripts/hatch.sh pr AER-12 <url>`. It is a
  field on the issue, not a URL somebody has to find in a comment.
- **Name the ticket in the pull request, in two places.** The title is the key,
  one space, then the subject in house style — `AER-12 Auth: the first Admin`,
  with no brackets and no second colon. The first line of the description is
  `[AER-12](<origin>/apps/hatch/issues/AER-12)` and nothing else, then a blank
  line, then the summary. `<origin>` is the address Hatch was reached at
  (`HATCH_BASE`), or the install's public address where it sets one; `hatch work
  AER-12 --dry-run` prints the exact line. Never write a relative link.
- **Say what has to land in order.** The loop takes siblings in whatever order
  the board puts them in, so work that must land in order says so:

  ```
  ./scripts/hatch.sh depends AER-13 AER-12   # AER-13 waits on AER-12
  ```

  It gates one move — the one into the column where the code gets written — and
  clears only when the issue it names is merged. Everything left of that keeps
  moving: a blocked issue is still broken down, still lands in the backlog, and
  is still analysed.
- **Read the board before assuming it is empty.** `./scripts/hatch.sh queue`
  prints every issue a pass would look at, in the order it looks, each with the
  reason it would be folded past — or the transition it is clear for. It spawns
  nothing and writes nothing. A column and type nobody has written a playbook
  for reads as a finished board from outside and is not one.

What a session is told, which model it runs on and how much effort it spends are
a **playbook**: a row per (status transition, issue types), readable by a key and
writable only by a person — as is a model or effort pinned on a single ticket. If
a playbook is wrong, say so on the ticket. The API refuses to let you route
around it, and it refuses on purpose.

Work on the loop itself — `src/Hatch.Cli`, `scripts/hatch.sh`,
`scripts/hatch.ps1`, `src/Hatch.Contracts` — reaches the next increment rather than the next
night: when its own source changes on the trunk, the loop rebuilds and comes
back as the new version.

The conditions that make an issue actionable live on the server, and
[`docs/hatch.md`](docs/hatch.md#what-makes-an-issue-actionable) lists them with
the reasoning behind each.

### When a decision is not yours to make

A product call, a name that will be lived with for years, a tradeoff with no
technically correct side: do not guess, and do not quietly take whichever branch
is cheapest to build. Ask on the ticket, and **name the choices**:

```
./scripts/hatch.sh ask AER-12 "How should drain retries be scoped?" \
    --recommend "Per-node: one budget each, so a slow node cannot starve the rest" \
    --option   "Global: one budget for the drain, simpler to reason about"
```

Each `--option` becomes something the operator presses, so the body is the
question alone and the tradeoffs go inside the options they belong to;
`--recommend` marks the one you would take, of which there may be one. A label
reads as a decision on its own — `per-node`, not `we should scope them per
node` — because the label becomes the answer's own text. Ask in prose
(`ask AER-12 "…"`, no options) only when the answer is genuinely open-ended: a
name, a description, a direction.

One call per question, so each can be answered on its own. Then **stop**.
Answers arrive in the next session's prompt under **Decisions already made**.
Those are settled: build on them, and do not reopen them.

What the repository can answer, answer by reading the repository.

### Given a ticket

A `hatch.${DOMAIN}/issues/AER-12` link, or a bare `AER-12`, means:

```
GET  ${HATCH_BASE}/api/hatch/issues/AER-12          # title, description, status, parent, children
GET  ${HATCH_BASE}/api/hatch/issues/AER-12/comments
GET  ${HATCH_BASE}/api/hatch/issues/AER-12/questions?open=false   # decisions asked for, and given
GET  ${HATCH_BASE}/api/hatch/issues/AER-12/events   # what has happened to it, newest first
```

Read it, then get to work. The description is markdown and is the brief.

### Filing work: see `docs/hatch-planning.md`

An increment that **files or reshapes work** — turning a draft into an epic with
stories under it, breaking a story into tasks, dating something that cannot
start yet, or moving a batch at once — reads
[`docs/hatch-planning.md`](docs/hatch-planning.md) first. It has the shapes:
`POST /api/hatch/issues` with a `parentKey`, the `readyAt` and `dueAt` dates,
the `issues` filters and the bulk endpoint, and the `/api/hatch/plan` reads that
say how far along a project is.

It is a separate file because an implementation increment never needs any of it,
and this one is loaded on every increment. Two rules from it are worth carrying
here, because getting them wrong is not recoverable by reading further:

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

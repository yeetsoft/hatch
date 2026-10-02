---
title: Running the agent
lede: The runner on your own machine. One increment, the loop, what it does to your checkout, and how to stop it.
permalink: /running-the-agent/
---

The board is a board; the **runner** is what works the tickets. It is one
binary called `hatch`, it runs on your machine, and it talks to your Hatch
over HTTP. Each increment it spawns one headless `claude` session inside your
checkout with the prompt, model and effort the ticket's column and type call
for, streams what that session does, and hands the ticket back.

This page is the working knowledge. The [Agent manual](agent-manual.md) is the
exhaustive reference for every flag and behaviour.

## What it needs

- **git** on the `PATH`. The loop fetches, resets and prunes with it, and the
  session branches, commits and pushes with it.
- **The `claude` CLI**, logged in, on the `PATH` or named in
  `HATCH_CLAUDE_BIN`. The runner refuses to take a ticket if it cannot find
  one, before claiming anything.
- **A git checkout** of the repository the board is about, with a remote
  called `origin` — the one you are standing in, or one named with `--repo`
  or `HATCH_REPOS`, so a loop with none of its own still works the two or
  three repositories already cloned on the machine. Only `work`, `go-to-work`
  and `do-my-work` need one; every other command is one request and a
  sentence about the answer. `--workspace` or `HATCH_WORKSPACE` is a third
  way in: a directory the loop owns entirely, where it clones what the board
  binds that it has no checkout of — for a machine with nothing cloned on it
  at all.

Nothing else. The binary is self-contained: no .NET, no Node, no `gh`, no
`jq`.

## Getting the binary

**Agents → Get the runner** in your board's nav offers the download for your
platform (`win-x64`, `osx-arm64`, `osx-x64`, `linux-x64`) and prints the
revision it was built from, which is the same commit as the image serving the
page. Put it on your `PATH` as `hatch` (`hatch.exe` on Windows); `chmod +x
hatch` on macOS and Linux.

Inside a checkout of Hatch itself, `./scripts/hatch.sh` (or
`.\scripts\hatch.ps1` in PowerShell on Windows) reaches the same
commands and builds the CLI on demand with the .NET SDK. `make build-hatch`
builds it once so every command after is fast, and `make publish-hatch`
produces the four self-contained binaries the Runner page serves.

## Pointing it at a board

```
hatch config --origin <your-hatch-origin>     the origin alone, asking nothing
hatch config                                  asks for origin, key and claude path
hatch config --show                           what is set, and which layer it came from
```

Settings are read in three layers, highest first:

1. An exported environment variable.
2. `scripts/.env` in the checkout you are standing in.
3. The per-user file `hatch config` writes: `~/.config/hatch/config` on
   macOS and Linux, `%APPDATA%\hatch\config` on Windows, mode 600, outside
   every repository.

So you configure once and every checkout on the machine is reached, and a
repository that wants to pin its own origin still can. Both files are parsed,
not sourced: `KEY=value` lines, and only these names:

| Variable | Meaning |
|---|---|
| `HATCH_BASE` | The origin of your Hatch |
| `HATCH_KEY` | `hatch_ak_…`, optional against a Hatch with its wall off |
| `HATCH_CLAUDE_BIN` | The `claude` CLI, if it is not on `PATH` |
| `HATCH_BASE_BRANCH` | The trunk `go-to-work` resets to, if `origin/HEAD` does not say |
| `HATCH_RUNNER` | What the board calls this runner. Default: a character from the cast list, chosen once per checkout - see `hatch config` |
| `HATCH_REPOS` | Checkouts a loop with no checkout of its own serves, joined on the platform's path separator (`:` on Unix, `;` on Windows) |
| `HATCH_WORKSPACE` | A directory this runner owns entirely, where it clones every repository the board binds that it has no checkout of |
| `HATCH_HEARTBEAT` | Seconds of silence before the renderer says what it is still waiting on. `0` turns it off |
| `HATCH_RETRY_SECONDS` | Seconds a call keeps retrying while Hatch does not answer, backing off between tries. `90` by default, `0` for exactly one attempt |

**The key lives outside the artifact**: never a tracked file, never a value in
a commit, never pasted into a ticket. Hatch is built to be cloned by other
operators, and a credential in the repository is one operator's credential
inherited by everyone who clones it.

**With no key** the runner names itself with an `X-Hatch-Runner` header, which
is a name and not a proof, and only a Hatch started with its wall off reads
one. That is every Hatch started with `docker compose up -d`. So a `401` says
one of two things, and says which: a key that was sent and refused, or no key
against a Hatch that wants one. A `403` means the key is working and the route
is not one a key may take.

## One increment: `hatch work`

```
hatch work                        the next actionable issue anywhere on the board
hatch work AER-12                 ...or this one, whatever else is above it
hatch work --under AER-1          ...or the next one under that epic
hatch work --dry-run              print the prompt and exit: claims nothing, spawns nothing
hatch work -i AER-12              a session you sit in, rather than a headless one
hatch work --quiet                say nothing until it is finished
hatch work --model opus --effort xhigh AER-12
hatch work --repo /path/to/a/checkout    also serve that checkout, repeatable
hatch work --workspace /clones           clone what the board binds, into /clones
```

What it does, in order: checks that the `claude` CLI can be found; sends one
heartbeat so a hand-run increment shows on the Runners page; takes a claim on
the ticket, so no other runner is sent at it; spawns the session with the
prompt on stdin; posts the work-log row; reads back where the ticket ended
up; prints any question the session asked; and releases the claim, every way
out including Ctrl-C.

Two things worth knowing that the loop does differently:

- **`work` does not touch git.** It runs against the tree exactly as it finds
  it. Only `go-to-work` fetches, stashes and resets before an increment.
- **`--model` and `--effort` beat the playbook for this run only.** They are
  one opinion about one ticket, which is why `go-to-work` has neither.

What it prints: the session id first and last with the `claude --resume`
command beside it, every tool call as it happens, a thinking-token pulse, and
a heartbeat naming what it is still waiting on. The `claude` CLI's own default
output prints nothing until the run ends, and four minutes of blank terminal
is indistinguishable from a hang.

Exit codes:

```
0    an increment ran, whatever the session itself exited with
1    a refusal: no claude CLI, an unreadable board, a flag it does not take
2    nothing to do: the board is idle, the ticket is blocked, or somebody else has it
130  interrupted
```

## The loop: `hatch go-to-work`

`work` in a circle: next actionable issue, one increment, ask again, until the
board has nothing an agent may move, and then wait and ask again every
interval. A process per increment rather than one long session, because a
session that ran all night would carry six hours of context into its last
ticket, and the earliest decisions in that context are the ones nobody can
audit afterwards.

```
hatch go-to-work                  until told to stop
hatch go-to-work --once           one pass, and out
hatch go-to-work --under AER-1    only inside that epic's subtree
hatch go-to-work --mine           only your own tickets - see below
hatch go-to-work --interval 300   seconds to wait when there was nothing to do (default 60)
hatch go-to-work --quiet          no per-increment stream, only what each one ended as
hatch go-to-work --repo /path/to/a/checkout    also serve that checkout, repeatable
hatch go-to-work --workspace /clones           clone what the board binds, into /clones
```

A ticket key is refused here. This command's question is "what is next", and
one ticket cannot be the answer twice. One increment on a named ticket is
`hatch work AER-12`.

### Sharing a board: `--mine` and `hatch do-my-work`

If you share one board with other people, each running their own agent on
their own budget, plain `go-to-work` still takes anything on the board an
agent may move and skips every ticket assigned to a person — including your
own. `--mine` narrows it further: it takes only a ticket assigned to the
person your key belongs to, or to your key itself, and folds every other one.
`hatch do-my-work` is exactly `hatch go-to-work --mine`, and takes every flag
above.

There is no single loop that works both the shared pool and your own tickets
— run `go-to-work` for the pool and `do-my-work` separately for your own.
Whose tickets your key's `--mine` reaches is set by an admin on the API Keys
page, never by the key itself.

### One pass, in order

1. **Heartbeat.** Says this runner is here, and reads back what the Runners
   page would like it to do: paused, stopping, a different scope, a different
   cap. Read at the top of a pass, the one moment no claim is held.
2. **Stop conditions**, below.
3. **Pick.** The first issue the dispatcher clears, folding past everything it
   does not, and a claim on it. No claim, no spawn. With a workspace
   configured, a repository the ticket binds that this loop has no checkout of
   is cloned here, with the lease already held.
4. **Reset every checkout the increment will use.** Fetch each, and put its
   tree back on its default branch at the tip the remote has right now — a
   binding's own base branch beats `origin/HEAD` for that checkout, and
   `HATCH_BASE_BRANCH` still only ever overrides the checkout the loop is
   standing in. A ticket for a repository this loop has no checkout of and no
   workspace to clone into is folded past, with the reason, before this step
   is ever reached.
5. **Check its own source.** If the loop was rebuilt on the trunk under it, it
   stops here and asks to come back as the new build, holding no ticket.
6. **Spawn the increment**, and record what it cost and whether the ticket
   moved.

An increment that ran is followed by the next one immediately. The interval is
only what to do when there was nothing to do. While idle it prints the reasons
the board is folded, grouped and counted, once, then again only when they
change, with a one-line "still nothing" every ten minutes.

### What it does to your checkout

Before every increment, on every checkout the ticket's project is bound to —
so a session never has to work out its own base:

- It fetches with prune, and puts the checkout on the default branch at the
  tip `origin` has it at. The trunk is whatever the binding's own base branch
  names, then `origin/HEAD`; `HATCH_BASE_BRANCH` overrides it, but only for
  the checkout the loop is standing in.
- **Uncommitted and untracked changes go into a stash**, named for the hour it
  was taken. Nothing is discarded, `git stash pop` is how you get it back, and
  ignored files are never touched, so a dependency directory or a local `.env`
  is safe.
- **Committed work is never at risk.** A checkout moves `HEAD`; the branch the
  last increment pushed is still under its own name. If the local trunk was
  ahead of origin, the loop says so and `git reflog` has the commits.
- A local branch whose upstream on origin has been deleted is deleted too,
  named on the terminal with the short sha it pointed at, so
  `git branch <name> <sha>` puts it back.
- **Then it goes onto the ticket's branch.** If origin has one branch named for
  the key — the lowercased key, or that and a hyphen and anything — the checkout
  goes onto it at origin's tip and the trunk is merged in; a conflict is left in
  progress for the session, and the prompt says so. With no branch, or only one
  already merged, the checkout stays on the trunk and the prompt names a branch
  to cut. With two unmerged branches the loop asks on the ticket which to use and
  spawns nothing. Never a rebase, never a force-push.
- **When the increment ends, the tree is put right before the ticket is let go**:
  a merge or rebase left half-done is aborted, uncommitted changes are stashed
  under a message naming the ticket, unpushed commits and commits left on the
  local trunk are named on the ticket (the latter moved to a `-rescued-` branch),
  and a pull request whose branch has fallen behind the trunk gets the trunk
  merged into it on origin, when that merge is clean. Everything found is one
  comment on the ticket. The checkout is back on the trunk afterwards.

The practical consequence: work that matters is work that is committed. Do
not leave something half-finished in the tree and then start the loop in the
same checkout.

### Stalls

A ticket that did not move is a **stall**: the session ended with the ticket
in the column it found it in. The loop comments on it, with the
`claude --resume` command for that session, and opens a question against it
with two options, *leave it* and *try again*, neither recommended. Nothing
further is dispatched there until somebody answers. One bad ticket costs one
increment instead of a night.

**Running out of Claude usage is not a stall.** When a session ends because its
account hit its usage limit, the runner commits and pushes whatever it left
onto the issue's branch, writes one comment saying it ran out of usage, when it
expects to resume, and the `claude --resume` command, and lets the ticket go
with no question asked and no flag blocking the next dispatch. The loop then
stops taking tickets until the account resets — see
[Bounds, timeouts and stopping](#bounds-timeouts-and-stopping) — without
counting toward the three-failures stop either.

**Being preempted is not a stall either.** The board can ask the runner
holding the least important ticket to stand down for an emergency one. That
runner still owns the ticket it was working, so it commits and pushes what it
has — the same as running out of usage — says so on the ticket, naming the
emergency issue, and goes straight on to pick that issue up next. No question
is asked and nothing blocks the next dispatch. A runner is protected from
being asked to stand down the same way whether its own ticket is emergency on
its own row or only by inheritance from an ancestor — the check reads the
effective level, not the stored one, so a story under an emergency epic is as
safe as a story marked emergency itself.

### Bounds, timeouts and stopping

None are set by default. An unattended run that stopped for a reason nobody
asked for is a run somebody has to go and check on.

```
hatch go-to-work --max-runs 5             stop after five increments
hatch go-to-work --max-spend 20           stop once the night has cost $20
hatch go-to-work --until 08:00            stop at that wall-clock hour (tomorrow, if it has gone by today)
hatch go-to-work --stop-file /tmp/stop    stop once that path exists
```

They are checked between increments and every five seconds through a wait,
so the increment in flight always finishes, is committed and is pushed.
`--stop-file` is the one to reach for from another terminal or another
machine: `touch /tmp/stop` ends the loop after the increment in flight, with
no pid to find and no signal that could land mid-push. A stop file that
already exists when the loop starts is refused, because otherwise an empty
board and a stale stop file would look identical.

The same bounds are controls on the **Runners** page, and a value set there
is folded in on the next heartbeat, including being cleared. Pause, resume and
"stop after this one" live there too. Nothing on the server starts or stops a
process; the loop picks each of these up itself, between increments, which is
what makes them work for a runner behind a router nothing can reach. The cost
of that: a press takes effect at the top of the next pass, after whatever
increment is in flight has finished.

**A runner waiting out a usage limit obeys every one of these too** - stopping,
`--until`, `--max-runs`, `--max-spend` and `--stop-file`, and Ctrl-C - the same
as a paused one. An `--until` earlier than the reset ends the night at
`--until`. The one thing it does that a pause does not: every ten minutes it
fetches its own checkout's trunk, and restarts as the new version if its own
source changed there, the same restart a claimed pass makes - see
[Stalls](#stalls) above.

**Ctrl-C** lets go of the claim on the way out and exits 130. A second one is
immediate, and leaves the ticket claimed until the lease ages out, five
minutes by default. **Closing the terminal a loop runs in** lets go of the
claim the same way - the hang-up a closed window or a dropped session sends is
caught exactly like Ctrl-C is. A loop that can no longer write to that
terminal at all carries on regardless, rather than going down with the claim
still held.

Four things end a run without a bound having been reached:

```
three increments in a row failed        whatever is broken is broken for every ticket
the workspace could not be reset        every ticket after it would be built on the wrong tree
the board asked this runner to stop     the Runners page, mid-night
there is no claude CLI to spawn         nothing was ever going to run
```

It finishes by printing the night: why it stopped, how many increments and
what they cost, what moved, and what stalled.

### Restarts

A loop whose own source changed on the trunk asks to be restarted as the new
build, because a process cannot exec itself into one. It exits 75 and
something standing over it has to rebuild and run it again.

```
hatch go-to-work --restart-after 60   come back as a newer build at least that often (default 30)
hatch go-to-work --restart-after 0    ...only when its own source actually changed
hatch go-to-work --no-restart         ...never
```

**A `hatch` you started by hand has nothing standing over it**, so it is the
loop it started as and these flags do nothing. This only matters when the
board is about Hatch's own repository: there, `./scripts/hatch.sh go-to-work`
(or `.\scripts\hatch.ps1 go-to-work` in PowerShell) is the supervisor. It
catches the 75, runs `make build-hatch` (`dotnet build` in PowerShell), and
starts the loop again with the night's totals carried forward, so a restart
cannot outspend `--max-spend`.

Windows client editions refuse scripts by default (execution policy
`Restricted`). `powershell -NoProfile -ExecutionPolicy Bypass -File
.\scripts\hatch.ps1 go-to-work` runs it anyway, and `Set-ExecutionPolicy -Scope
CurrentUser RemoteSigned` allows it for the account once; a clone carries no
mark of the web, so `RemoteSigned` is enough. Group Policy outranks both: if it
is what refuses, it is the thing to change, and no flag will get round it. The container runner is a third case: it carries the
binary its image was built with, and a rebuild of the image is how it becomes
a newer one.

### One loop per served checkout

A loop is not limited to the checkout it is standing in: `--repo` (repeatable)
or `HATCH_REPOS` names others it serves too, so one loop on a laptop can work
the two or three repositories already cloned there with no checkout of its
own. Each named path is resolved and checked at startup — it must exist,
carry a `.git`, and have an `origin` a project's binding can match — before
anything is locked or claimed.

A second `go-to-work` naming any checkout this loop already serves is refused,
naming the pid of the one that has it. Two loops do not collide over the
board, the claim divides it, but they would collide over a tree they share.
Two checkouts, two loops, and both are welcome. They appear on the Runners
page under their own names.

A loop with none of its own checkouts of what a board binds can still serve
it: `--workspace <dir>` (or `HATCH_WORKSPACE`) names a directory it owns
entirely, and it clones whatever a project binds that it has no checkout of
into `<dir>/<host>/<owner>/<repo>` the first time a ticket needs it — a port in
a host, if there is one, spelled with `_` in place of `:`, since Windows will
not take a `:` in a directory name. A remote two projects both bind is cloned
once and served to both. A clone that fails releases the ticket, comments on
it with git's own line, and counts as a failed increment — three of them in a
row end the night the same way three failed spawns do. A directory already
holding clones from an earlier night is served as it is found, nothing
re-cloned.

## Reading the board, spending nothing

```
hatch board                       the columns, and how many cards in each
hatch queue                       every card a pass would look at, in the order it looks
hatch queue AER-1                 ...under one epic
hatch next                        top workable card of "todo"
hatch next "in progress"          ...or of any column
hatch show AER-12                 the brief, its edges, and its comments
hatch questions                   everything waiting on an answer
hatch questions AER-12            ...or just this ticket's
```

`hatch queue` is the dry run for the loop and the answer to "why did it not
pick up the ticket I meant". A row marked `!!` is emergency, one marked `! `
is expedited, one marked `~ ` is economy - worked only once the account an
unattended pass would spend has usage to spare - and one marked `||` is
paused: a person set it aside, and nothing picks it up until they set it
back. Columns are found by name on the letters and digits alone, so `todo`
reaches `To Do`.

## Driving a ticket by hand

```
hatch start AER-12                move it to "in progress"
hatch move AER-12 todo            ...or to any non-terminal column
hatch comment AER-12 "sha abc123 on branch aer-12-thing"
hatch pr AER-12                   where it is being reviewed
hatch pr AER-12 https://...       ...or say where, having opened one
hatch pr AER-12 --clear           ...or take it off the one it has
hatch depends AER-13 AER-12       AER-13 waits on AER-12
hatch depends AER-13 --remove AER-12
hatch ask AER-12 "the question" --recommend "Label: why" --option "Label: why"
hatch answer                      answer the open questions, one at a time, here
hatch api GET /api/hatch/issues?statusId=2
hatch api PATCH /api/hatch/issues/AER-12 '{"dueAt":"2026-10-01"}'
```

`hatch answer` walks the open questions serially on purpose: six printed at
once get answered in aggregate, which is how a wrong assumption gets in. A
number takes that option and the ticket records the option's label as the
answer, not the number.

`move` refuses a terminal column and a deferred column. Only the operator
decides that something shipped or shelved, and the board enforces it on the
server as well.

## What the session is told

Every increment's prompt is assembled from the playbook row for that column
transition and issue type, then the ticket (key, type, title, the move it is
making, dates, description), its children, the questions already answered
under **Decisions already made**, and a fixed tail: how to reach Hatch from
inside the session, how to ask when it cannot decide, where the increment
ends, and the instruction to finish with a `work-log` block. `hatch work
--dry-run` prints the whole thing.

The session runs the `claude` CLI in print mode with permission prompts
bypassed, because nothing is there to answer one. The runner puts its own
directory at the front of the session's `PATH`, so `hatch` inside the session
is the same binary that spawned it, already pointed at the same board.

What the session knows about the board beyond that is your repository's
`CLAUDE.md`. Your Hatch ships the block to paste under **Docs**, behind the gear.

## The `work-log` row

When the session ends, the runner lifts the last fenced `work-log` block out
of what it said, a title line and a summary, and posts it with the session
id, the duration, the four token counts and the notional cost as one row on
the ticket's work log. That row is the only record anywhere that knows which
ticket the money went on, and the **Leaderboard** page adds them up.

## Troubleshooting

**`hatch: no claude CLI on PATH.`** Install it, or `export
HATCH_CLAUDE_BIN=/path/to/claude`. The runner looks on `PATH` only; it does
not find an editor extension's bundled copy.

**`hatch: 401 - no key was sent and this Hatch has its wall on.`** This
board wants a key. Mint one on its API keys page and run `hatch config`.

**`hatch: 403 - the key is good and this route is not one it may take.`**
A key is never an administrator. Playbooks, priority, assignees, settings and
the Runners page controls are a person's to change, in the browser. Whose
tickets a key's `--mine` reaches is the same kind of change — an admin sets it
on the API Keys page when minting a key, or afterwards, and a key can never
set its own.

**`hatch: cannot tell which branch is the trunk here`.** `origin/HEAD` is
not set in this clone. `git remote set-head origin -a` fixes it, or name the
branch in `HATCH_BASE_BRANCH`.

**`hatch: the tree has changes in it that would not stash`.** A merge in
progress or a conflicted file. Clear it by hand; the loop will not guess.

**`hatch: a go-to-work is already running in <path>, pid <n>`.** One loop
per checkout. Join that one, stop it, or use another checkout. A lock left by
a dead process is cleared automatically.

**It picked up nothing.** `hatch queue` says why, per card. The common
reasons: no playbook leads out of that column for that type, an open
question, a person's name on it, a ready date not yet arrived, another
runner's claim, or — once the runner declares what it has — a project bound
to repositories it has no checkout of.

**`hatch: could not reach <origin> for 90s - ...`** Hatch was unreachable for
longer than `HATCH_RETRY_SECONDS` (a deploy, usually, or a longer outage) — a
brief gap of no more than that many seconds costs a call nothing, since it
retries underneath this message rather than failing on the first blip. If a
claim's heartbeat keeps failing for longer than that ticket's
`StallLapseSeconds` altogether, the session gives up on its own and stops
rather than keep working a ticket the board may already have handed to
somebody else — the next pass finds out on its own next attempt.

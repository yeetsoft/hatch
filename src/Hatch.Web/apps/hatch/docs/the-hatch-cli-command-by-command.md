---
title: The hatch CLI, command by command
section: Running agents
order: 50
summary: Every `hatch` command, what it does, and what it costs to run.
---

Everything below runs against the board you pointed it at with `hatch config`.
Only `work` and `go-to-work` need a checkout or a workspace to clone into — the
one you are standing in, one named with `--repo` or `HATCH_REPOS`, or
`--workspace`/`HATCH_WORKSPACE` to clone what is missing — and the rest are one
request and a sentence about the answer, so `hatch board` from anywhere is the
ordinary case.

```
hatch --help                 every command, from the program itself
hatch <command> -h           ...and what one of them takes
```

## Pointing it at a board

```
hatch config                              ask for the origin and the key, and write them
hatch config --origin <your-hatch-origin> ...without being asked for the origin
hatch config --show                       what is set, and which layer it came from
```

Written to a per-user file — mode 600, under the platform's application-data
directory, outside every repository — so it follows you between checkouts and
you do it once. A Hatch with its wall off needs no key; leave it blank.

## One increment: `hatch work`

Asks the board for a ticket, claims it, spawns one headless `claude` session
with the prompt, model and effort that ticket's column and type call for,
streams what the session does, and exits when the session ends. One ticket, one
process.

```
hatch work                        the next actionable issue anywhere on the board
hatch work AER-12                 ...or this one, whatever else is above it
hatch work --under AER-1          ...or the next one under that epic's subtree
```

A key and `--under` together are refused: one names the ticket, the other names
where to look for it.

```
hatch work --dry-run              print the prompt and exit - claims nothing, spawns nothing
hatch work -i AER-12              a session you sit in, rather than a headless one
hatch work --quiet                say nothing until it is finished
hatch work --model opus --effort xhigh AER-12
hatch work --repo /path/to/a/checkout    also serve that checkout, repeatable
hatch work --workspace /clones           clone what the board binds, into /clones
```

`--model` and `--effort` beat the playbook for this run only. They are one
opinion about one ticket, which is why `go-to-work` has neither.

What it prints, and why: the session id first and last with the
`claude --resume` line beside it, then every tool call, a thinking-token pulse,
and a heartbeat naming what it is still waiting on. The `claude` CLI's own
default output prints nothing until the run ends, and four minutes of blank
terminal is indistinguishable from a hang.

It also beats once against the Runners page, so an increment run by hand shows
up beside the loops rather than being a session nobody can see.

Exit codes:

```
0    an increment ran - whatever the session itself exited with
1    a refusal: no claude CLI, an unreadable board, a flag it does not take
2    nothing to do: the board is idle, the ticket is blocked, or somebody else has it
130  interrupted
```

## The loop: `hatch go-to-work`

`work` in a circle: next actionable issue, one increment, ask again — until the
board has nothing an agent may move, and then it waits and asks again every
interval. A process per increment rather than one long session, because a
session that ran all night would carry six hours of context into its last
ticket, and the earliest decisions in that context are the ones nobody can
audit afterwards.

```
hatch go-to-work                  until told to stop
hatch go-to-work --once           one pass, and out
hatch go-to-work --under AER-1    only inside that epic's subtree
hatch go-to-work --interval 300   seconds to wait when there was nothing to do (default 60)
hatch go-to-work --quiet          no per-increment stream, only what each one ended as
hatch go-to-work --repo /path/to/a/checkout    also serve that checkout, repeatable
hatch go-to-work --workspace /clones           clone what the board binds, into /clones
```

A ticket key is refused here: this command's question is "what is next", asked
again and again, and one ticket cannot be the answer to it twice. One increment
on a named ticket is `hatch work AER-12`.

### One pass, in order

1. **Heartbeat.** Says this runner is here, and reads back what the Runners
   page would like it to do — paused, stopping, a different scope, a different
   cap. Read at the top of a pass, which is the one moment no claim is held.
2. **Stop conditions**, below.
3. **Pick.** The first issue the dispatcher clears, folding past everything it
   does not, and takes a claim on it. No claim, no spawn — that is what keeps
   two runners off one ticket. With a workspace configured, a repository the
   ticket binds that this loop has no checkout of is cloned here, with the
   lease already held.
4. **Reset every checkout the increment will use.** Fetch each, and put its
   tree back on its default branch at the tip the remote has right now — a
   binding's own base branch beats `origin/HEAD` for that checkout, and
   `HATCH_BASE_BRANCH` still only ever overrides the standing one. A ticket for
   a repository this loop has no checkout of and no workspace to clone into is
   folded past, with the reason, before this step is ever reached.
5. **Check its own source.** If `go-to-work` was rebuilt on the trunk under it,
   it stops here and asks to come back as the new build, holding no ticket.
6. **Spawn the increment**, and record what it cost and whether the ticket
   moved.

An increment that ran is followed by the next one immediately; the interval is
only what to do when there was nothing to do.

**A ticket that did not move is a stall, and the first one in a row is let go
quietly.** The loop comments saying why, and moves on — the ticket is free for
the very next pass, since most of the time whatever happened is weather and a
retry a few minutes later just works. Only the **second** stall in a row on the
same ticket is flagged: the loop comments, opens a question against it, and
nothing further is dispatched at that ticket until somebody answers. A ticket
that is genuinely stuck costs two increments instead of a night.

### Bounds, timeouts and stopping

None of these are set by default. An unattended run that stopped for a reason
nobody asked for is a run somebody has to go and check on.

```
hatch go-to-work --max-runs 5             stop after five increments
hatch go-to-work --max-spend 20           stop once the night has cost $20
hatch go-to-work --until 08:00            stop at that wall-clock hour (tomorrow, if it has gone by today)
hatch go-to-work --stop-file /tmp/stop    stop once that path exists
hatch go-to-work --max-runs 5 --max-spend 20 --until 08:00
```

They are checked between increments and through every wait, so the increment in
flight always finishes, is committed and is pushed. `--stop-file` is the one to
reach for from another terminal or another machine — stopping a loop then needs
nothing but a shell and a path, no pid to find and no signal that could land
mid-push:

```
touch /tmp/stop                           # the loop ends after the increment in flight
```

The same three bounds are controls on the **Runners** page, and a value set
there is folded in on the next heartbeat — including being cleared. Pause,
resume and "stop after this one" live there too.

**Ctrl-C** lets go of the claim on the way out and exits 130. A second one is
immediate, and leaves the ticket claimed until the lease ages out.

And four things end a run without a bound having been reached:

```
three increments in a row failed        whatever is broken is broken for every ticket
the workspace could not be reset        every ticket after it would be built on the wrong tree
the board asked this runner to stop     the Runners page, mid-night
there is no claude CLI to spawn         nothing was ever going to run
```

It finishes by printing the night: what moved, what stalled, how many
increments and what they cost.

### Restarts

A loop whose own source changed on the trunk asks to be restarted as the new
build, because a process cannot exec itself into one.

```
hatch go-to-work --restart-after 60   come back as a newer build at least that often (default 30)
hatch go-to-work --restart-after 0    ...only when its own source actually changed
hatch go-to-work --no-restart         ...never
```

**This needs something standing over the process**, because a process cannot
rebuild itself. A `hatch` you started by hand has nothing standing over it, so
it is the loop it started as and these three flags do nothing there. The
supervisor is Hatch's own `scripts/hatch.sh`, or `scripts\hatch.ps1` in
PowerShell on Windows, which catches the 75, compiles the new source and runs it
again. The container runner is a third case: it
carries the binary its image was built with, so it becomes a newer one when
the stack is brought up again after a `git pull` (see *Upgrading*).

Windows client editions refuse scripts by default (execution policy
`Restricted`). `powershell -NoProfile -ExecutionPolicy Bypass -File
.\scripts\hatch.ps1 go-to-work` runs the supervisor anyway, and
`Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` allows it for the account
once; a clone carries no mark of the web, so `RemoteSigned` is enough. Group
Policy outranks both: if it is what refuses, it is the thing to change, and no
flag will get round it.

Exit codes:

```
0    the night ended, for one of the reasons above
1    a refusal: a flag it does not take, a stop file that already exists, a lock it could not take
75   asking the supervisor to rebuild and run it again
130  interrupted
```

### One loop per served checkout

A loop is not limited to the checkout it is standing in: `--repo /path/to/a/checkout`
(repeatable) or `HATCH_REPOS` (checkout paths joined on `Path.PathSeparator`)
names others it serves too — which is what lets one loop on a laptop work the
two or three repositories already cloned there, with no checkout of its own.
Every named path is resolved and validated at startup — it must exist and
carry a `.git`, and a project's binding can only ever match it once it has an
`origin` — before anything is locked or claimed.

A second `go-to-work` naming any checkout this loop already serves is refused,
naming the pid of the one that has it. Two loops no longer collide over the
board — the claim divides it — but they would still collide over a tree they
share, one increment's reset landing in the middle of another's branch. Two
checkouts, two loops, and both are welcome — whether each loop found its
checkout by standing in it or by naming it.

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

### What it does to your checkout

Before every increment, on every checkout the ticket's project is bound to —
so that a session never has to work out its own base:

- It fetches, and puts the checkout back on the default branch at the tip the
  remote has it at right now — a binding's own base branch beats
  `origin/HEAD` for that checkout, and `HATCH_BASE_BRANCH` still only ever
  overrides the checkout the loop is standing in.
- **Uncommitted and untracked changes go into a stash**, named for the hour it
  was taken. Nothing is discarded — `git stash pop` is how you get it back —
  and ignored files are never touched, so a dependency directory or a local
  `.env` is safe.
- **Committed work is never at risk.** A checkout moves `HEAD`; the branch the
  last increment pushed is still under its own name.
- A branch whose upstream on origin has been deleted is deleted locally too,
  named on the terminal with the short sha it pointed at, so
  `git branch <name> <sha>` puts it back.
- **Then it goes onto the ticket's branch.** If origin has one branch named for
  the key — the lowercased key, or that and a hyphen and anything — the checkout
  goes onto it at origin's tip and the trunk is merged in; a conflict is left in
  progress for the session, and the prompt says so. With no branch, or only one
  already merged, the checkout stays on the trunk and the prompt names a branch
  to cut. With two unmerged branches the loop asks on the ticket which to use and
  spawns nothing. In a project that lands on the trunk, the checkout stays on
  `<trunk>` when origin has no branch for the ticket, and the session pushes
  `<trunk>` itself; the runner never does. Never a rebase, never a force-push.
- **When the increment ends, the tree is put right before the ticket is let go**:
  a merge or rebase left half-done is aborted, uncommitted changes are stashed
  under a message naming the ticket, unpushed commits and commits left on the
  local trunk are named on the ticket (the latter moved to a `-rescued-` branch),
  and a pull request whose branch has fallen behind the trunk gets the trunk
  merged into it on origin, when that merge is clean. Everything found is one
  comment on the ticket. The checkout is back on the trunk afterwards.

The practical consequence: work that matters is work that is committed. Do not
leave something half-finished in the tree and then start the loop in the same
checkout.

## Reading the board, spending nothing

```
hatch board                       the columns, and how many cards in each
hatch queue                       every card a pass would look at, in the order it looks
hatch queue AER-1                 ...under one epic
hatch next                        top workable card of "todo"
hatch next "in progress"          ...or of any column
hatch show AER-12                 the brief, plus its comments
hatch comments AER-12             the comments, without the brief
hatch questions                   everything waiting on an answer
hatch questions AER-12            ...or just this ticket's
```

`hatch queue` is the dry run for the loop, and the answer to "why did it not
pick up the ticket I meant": each line is a key, a type, a column, and either
the reason the pass would fold past it or the transition it is clear for. It
spawns nothing and writes nothing. An empty board says so in a sentence rather
than printing a blank line — "there is nothing" and "something went wrong and
printed nothing" look identical otherwise.

Columns are found by name rather than by id, on the letters and digits alone,
so `todo` reaches the column the board calls `To Do`.

## Driving a ticket by hand

```
hatch start AER-12                move it to "in progress"
hatch move AER-12 todo            ...or to any non-terminal column
hatch comment AER-12 "sha abc123 on branch aer-12-thing"
hatch pr AER-12                   where it is being reviewed
hatch pr AER-12 https://...       ...or say where, having opened one
hatch pr AER-12 --clear           ...or take it off the one it has
hatch landed AER-12 [<sha>] [--repo <path>]    ...or, on a project that lands on the trunk, the sha it reached
hatch depends AER-13 AER-12       AER-13 waits on AER-12
hatch depends AER-13 --remove AER-12        ...no longer
hatch one-pr AER-5                whether the story's tasks share its branch and one PR
hatch one-pr AER-5 on             ...turn it on for this story (or off, or inherit)
hatch answer                      answer the open questions, one at a time, here
hatch api GET /api/hatch/issues?statusId=2
hatch api PATCH /api/hatch/issues/AER-12 '{"dueAt":"2026-10-01"}'
```

`hatch answer` walks the open questions serially on purpose: six printed at once
get answered in aggregate, which is how a wrong assumption gets in.

## Settings

Read in three layers, highest first: an exported variable, then `scripts/.env`
in the checkout you are standing in, then the per-user file `hatch config`
writes.

```
HATCH_BASE         the origin of your Hatch
HATCH_KEY          hatch_ak_... Optional against a Hatch with its wall off
HATCH_CLAUDE_BIN   the claude CLI, if it is not on PATH
HATCH_BASE_BRANCH  the trunk go-to-work resets to between increments
HATCH_RUNNER       what the board calls this runner (default: a character from
                   the cast list, chosen once per checkout - see `hatch config`)
HATCH_ROOT         the checkout to work in (default: upwards from here)
HATCH_REPOS        checkouts a loop with no checkout of its own serves, joined on
                   the platform's path separator (: on Unix, ; on Windows)
HATCH_WORKSPACE    a directory this runner owns entirely, where it clones every
                   repository the board binds that it has no checkout of
HATCH_HEARTBEAT    seconds of silence before the renderer says what it is waiting on
HATCH_RTK          off to keep RTK's Bash hook out of a session where rtk is on PATH
                   (exported only: scripts/.env and `hatch config` are not read for it)
```

`--repo` on `go-to-work` or `work` (repeatable) beats `HATCH_REPOS` outright for
that run rather than adding to it, the same way any other flag beats a setting.
`hatch config --repo /path/to/a/checkout` (repeatable) writes `HATCH_REPOS` to
the per-user file; `--repo` alone clears it. `--workspace <dir>` works the same
way for `HATCH_WORKSPACE` — beats it for that run — and `hatch config
--workspace <dir>` writes it to the per-user file. A workspace refuses to sit
inside a named checkout, or a named checkout inside it, in either direction: a
clone under a tree being reset, or a tree under a directory of clones, is two
owners of one path.

## Where the prompt comes from

The prompt, model and effort of every increment come from a **playbook** — a row
per (column transition, issue type). The board ships with a working set, and the
Playbooks page is where you change what your sessions are told. They are yours
to edit and not your agents': the API refuses the write from a key, on purpose,
because an agent that could widen its own instructions and its own budget is a
loop with no end.

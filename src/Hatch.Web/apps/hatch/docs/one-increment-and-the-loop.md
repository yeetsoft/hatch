---
title: One increment and the loop
section: Running agents
order: 20
summary: What `hatch work` and `hatch go-to-work` do, every flag they take, and how a night ends.
---

An **increment** is one ticket, one process, one agent session: it claims a
ticket, runs the session, writes up what happened and lets the ticket go. The
**loop** is increments back to back, a fresh process for each, so no session
carries a night's context into its last ticket, and the earliest decisions in
that context are the ones nobody can audit afterwards. Both need a checkout, or
a workspace to clone into ([the runner](the-runner)); `hatch --help` lists every
command, and `hatch work -h` and `hatch go-to-work -h` say what these two take.

## One increment: `hatch work`

```
hatch work                        the next actionable issue anywhere on the board
hatch work AER-12                 ...or this one, whatever else is above it
hatch work --under AER-1          ...or the next one under that epic's subtree
hatch work --mine                 ...or the next one assigned to you
```

A key names the ticket, and `--under` and `--mine` each narrow where to look
for one, so a key with either is refused.

```
hatch work --dry-run              print the prompt and exit
hatch work -i AER-12              a session you sit in, rather than a headless one
hatch work --quiet                say nothing until it is finished
hatch work --model opus --effort xhigh AER-12
hatch work --agent codex          which agent CLI spawns the session
hatch work --no-keys              read no keys from this terminal
hatch work --repo /path/to/a/checkout    also serve that checkout, repeatable
hatch work --workspace /clones           clone what the board binds, into /clones
```

- `--dry-run` claims nothing, spawns nothing and clones nothing, even where a
  workspace is set, and prints the prompt a session would be handed ([what a
  session is told](what-a-session-is-told)). It beats `-i`.
- `-i` runs the session in your terminal instead of headless. There is no
  readout then, since the terminal is the session's.
- `--quiet` prints nothing until the increment is finished.
- `--model` and `--effort` beat the playbook for this run only. They are one
  opinion about one ticket, which is why the loop has neither.
- `--agent` beats `HATCH_AGENT` for this run: `claude`, `codex` or `grok`.
- `--no-keys` reads no keys from this terminal; [what it prints](#what-it-prints) has the keys.
- `--repo` and `--workspace` each replace their setting (`HATCH_REPOS`,
  `HATCH_WORKSPACE`) for the run rather than adding to it. `--workspace off`
  never clones.

`work` does touch git. It fetches and resets each checkout the ticket uses,
goes onto the ticket's branch, and puts the tree right afterwards, exactly as
the loop does. The one difference is a dirty tree: `work` refuses one ("commit
or stash them first"), where the loop stashes it, because somebody is sitting
there and the changes are theirs.

It beats once against **Agents → Runners**, so an increment run by hand shows
beside the loops rather than being a session nobody can see.

### What it prints

The opening banner, `🥚🥚🥚🥚🥚 STARTING WORK ON <KEY>`, names the ticket, what
the increment is for (a move, a conflict or a failing build), the model and
effort, and the runner's name with which increment of the night this is. The
session's tool calls stream under it, with a thinking-token pulse and a
heartbeat naming what it is still waiting on: the agent CLI's own default
output prints nothing until the run ends, and four minutes of blank terminal is
indistinguishable from a hang. A glyph row closes it:

```
🐣   the ticket moved, a conflict was resolved, or a fix was pushed
🥚   it did not move
🍳   the session exited badly, was interrupted, or lost its lease
```

`🍳` outranks the other two. A pass that spawns no session prints no banner.

On an interactive terminal (not redirected, not `TERM=dumb`, and never
`work -i`) a readout is pinned to the bottom. Its rows are the issue; whether
the agent is alive (elapsed, tokens, how long it has been quiet, and what it
was last inside of); one bar per usage window the agent CLI reports; and the
runner's own row. Under it is a legend of keys, read only where the readout
draws and stdin is a terminal. Anywhere else one line says so and names Ctrl-C
and `--stop-file`. `--no-keys` keeps the readout and drops the keys and the
legend.

```
n   check now, skipping the wait: ends the idle nap early (the loop only)
s   stop after this increment; press again to undo
c   cancel: asks, then ends the run as Ctrl-C does
p   pause and resume: still heartbeating, takes nothing
k   skip this increment: asks, then ends only this one, touching the ticket no further
?   swap the legend for a longer one
```

A skip counts neither as a failure nor as a success, so it leaves the
failure streak where it was. Every key that changes what the runner does writes
a line to the log.

### Exit codes

```
0    an increment ran, whatever the session exited with; or the ticket was
     carried across a column with no session, or a conflict or failing build
     had already cleared
1    a refusal: no agent CLI, an unreadable board, a flag it does not take, a
     tree it could not reset or that is dirty, a --mine key that belongs to
     nobody, a refused runner name, or the runner itself failing
2    nothing to do: the board is idle, the ticket is blocked or somebody else
     has it, or it needs a person to say which of two branches
130  interrupted
```

With `-i`, the exit code is the session's own.

## The loop: `hatch go-to-work`

`work` in a circle: the next actionable issue, one increment, ask again, until
the board has nothing an agent may move, and then wait and ask again every
interval.

```
hatch go-to-work                  until told to stop
hatch go-to-work --once           one pass, and out
hatch go-to-work --under AER-1    only inside that epic's subtree
hatch go-to-work --interval 300   seconds to wait when there was nothing to do (default 60)
hatch go-to-work --merge-interval 10     seconds between the merge train's checks (default 5)
hatch go-to-work --quiet          no per-increment stream, only what each one ended as
hatch go-to-work --agent codex    which agent CLI, once for the night
hatch go-to-work --no-keys        read no keys from this terminal
hatch go-to-work --repo /path/to/a/checkout    also serve that checkout, repeatable
hatch go-to-work --workspace /clones           clone what the board binds, into /clones
```

- `--once` is one pass and out. It heartbeats as a one-off and reads no
  instruction back, so a pause, a stop or a bound set on the board does not
  reach it, and it never restarts.
- `--interval` and `--merge-interval` take at least one second; anything else
  is refused with exit 1. The interval is only what to do when a pass found
  nothing. The merge train keeps its own clock, so approved pull requests merge
  without waiting for it ([letting the loop merge for you](letting-the-loop-merge-for-you)).
- `--quiet`, `--no-keys`, `--repo` and `--workspace` are as for `work`.
- `--agent` is resolved once, before the loop starts, and carried the whole
  night, unlike the model and effort, which the playbook settles fresh each
  increment.

A ticket key is refused here. This command's question is "what is next", asked
again and again, and one ticket cannot be the answer twice. One increment on a
named ticket is `hatch work AER-12`.

### Sharing a board

If you share one board with other people, each running their own agent on their
own budget, plain `go-to-work` takes anything on the board an agent may move
and skips every ticket assigned to a person, including your own.

```
hatch go-to-work --mine           only tickets assigned to you
hatch do-my-work                  exactly the same, every flag included
```

`--mine` takes only a ticket assigned to the person the key belongs to, or to
the key itself, and folds every other one. No one loop works both the shared
pool and your own tickets: run `go-to-work` for the pool and `do-my-work`
separately for your own.

A key that belongs to nobody has no tickets of its own, and is refused: the
loop ends its night (exit 0) saying so, and `work --mine` exits 1. Who sets a
key's owner is in [people, roles and keys](people-roles-and-keys).

### One pass, in order

1. **Usage probe.** Where the agent CLI can report its own usage (only
   `claude` can), the loop asks it, at most every two minutes and after an
   increment, so the bars are the account's and not the last session's.
2. **Heartbeat and stop conditions.** The heartbeat says this runner is here
   and reads back what **Agents → Runners** would like it to do: paused, stopping, a
   different scope, different bounds. Anything the board changed is printed
   (`the board set --max-runs 3`). Then the bounds, [below](#bounds-timeouts-and-stopping).
   This is the one moment no claim is held.
3. **Age restart.** Under Hatch's own supervisor a loop that has run long
   enough asks to come back as a newer build ([Restarts](#restarts)).
4. **Pause.** Paused from the board or from the keyboard, it keeps
   heartbeating and takes nothing. The board wins if both are true.
5. **Usage-limit wait.** A runner whose account ran out of usage takes
   nothing until it resets, obeying every bound and Ctrl-C meanwhile.
6. **Review poll.** On the interval, pull requests in review are read for a
   failing build or a conflict, which become work of their own.
7. **Pick and claim.** The first issue the dispatcher clears, folding past
   everything it does not, and a claim on it. No claim, no spawn, which is what
   keeps two runners off one ticket. If the ticket binds a repository this loop
   has no checkout of, it is cloned here, with the lease already held. A ticket
   that a hop carries across a column with no session is not an increment:
   `--max-runs` and `--once` do not count it, and the next pass starts at once.
8. **Reset.** Fetch each checkout the increment will use and put its tree on
   its default branch at the tip the remote has right now.
9. **Source check.** If the loop was rebuilt on the trunk under it, it stops
   here and asks to come back as the new build, holding no ticket.
10. **Conflict or build recheck, and branch entry.** A conflict or failing
    build is read again against the refs just fetched, and if it has cleared,
    nothing is spawned. The checkout then goes onto the ticket's branch.
11. **Spawn the increment**, and record what it cost and whether the ticket
    moved.

An increment that ran is followed by the next one immediately. While idle the
loop prints the reasons the board is folded once, again only when they change,
and `still nothing` every ten minutes.

### Stalls

A ticket that did not move is a **stall**: the session ended with the ticket in
the column it found it in, or with a plain advance and no `work-log`, which
counts as not moved. Stalls escalate:

- **The first in a row is let go.** The loop comments why, with the command to
  resume that session, and the ticket is free for the very next pass, since most
  of the time whatever happened is weather.
- **The second marks it stalled.** The loop comments and sets the mark, which
  blocks the next dispatch and badges the card. The board resumes the ticket
  by itself after 15 minutes (`0` never resumes it).
- **Three in a row hold it for a person.** The third stall is marked like the
  second, and after it the board stops resuming the ticket.

An open question on the ticket is itself the flag, so the second stall on a
ticket that already has one only comments. A session that asked a question is
no stall. Nor is running out of usage, being preempted, losing the lease, or a
skip with `k`: none counts toward a stall or toward the three failed increments
below, and a session stopped by a hard limit published what it left.
[Flow, WIP and the battery](flow-wip-and-the-battery) has the numbers.

### Bounds, timeouts and stopping

None is set by default. An unattended run that stopped for a reason nobody
asked for is a run somebody has to go and check on.

```
hatch go-to-work --max-runs 5             stop after five increments
hatch go-to-work --max-spend 20           stop once the night has cost $20
hatch go-to-work --until 08:00            stop at that wall-clock hour (tomorrow, if it has gone by today)
hatch go-to-work --stop-file /tmp/stop    stop once that path exists
```

`--max-runs` and `--max-spend` take at least 0, so `--max-runs 0` stops before
the first increment. `--max-spend` counts priced increments only, and the
night's closing tally says how many were unpriced. `--until` takes `HH:MM`.
`--stop-file` is refused if the path already exists, because otherwise an empty
board and a stale stop file would look identical.

They are checked between increments and through every wait, so the increment
in flight always finishes, is committed and is pushed. `--stop-file` is the one
to reach for from another terminal or another machine: with no pid to find and
no signal that could land mid-push, `touch /tmp/stop` ends the loop after the
increment in flight.

The same bounds are controls on **Agents → Runners**, and a value set there is folded
in on the next heartbeat, including being cleared (*Watching the loop*).

**Ctrl-C** lets go of the claim on the way out, prints the night, and exits 0
from `go-to-work` (`work` exits 130). SIGTERM and a closed terminal do the
same, and so do `c` then `y`. A second Ctrl-C is not handled, so the process
is killed at once and the ticket stays claimed until the lease ages out, five
minutes by default.

Other things end a run without a bound having been reached:

```
three increments in a row failed        whatever is broken is broken for every ticket
the workspace could not be reset        every ticket after it would be built on the wrong tree
the board asked this runner to stop     the Agents → Runners page, mid-night
there is no agent CLI to spawn          nothing was ever going to run
the keyboard asked (s)                  after the increment in flight
a --mine pass the board refused         the key belongs to nobody
the night's totals could not be written for a restart
```

An increment fails when its session exits non-zero, or a clone fails; one clean
exit resets the streak. The run finishes by printing the night: why it
stopped, how many increments and what they cost, what moved, and what stalled.

### Restarts

A loop whose own source changed on the trunk asks to come back as the new
build — exit 75, because a process cannot rebuild itself.

```
hatch go-to-work --restart-after 60   come back as a newer build at least that often (default 30)
hatch go-to-work --restart-after 0    ...only when its own source actually changed
hatch go-to-work --no-restart         ...never
```

`0` is not "off" — it still asks, only on an actual change rather than a
clock running out. `--no-restart` is the only one of the three that is
really off.

Two more things turn restarts off before any of those flags is even read:
`--once`, since it heartbeats as a one-off and never comes back regardless;
and an unset `HATCH_NIGHT_STATE`, which only Hatch's own supervisor sets. A
`hatch` started by hand has nothing standing over it, so it is the loop it
started as, and all three flags do nothing there.

The supervisor is `scripts/hatch.sh`, or `scripts\hatch.ps1` in PowerShell on
Windows: it catches the 75, rebuilds and runs the loop again, carrying the
night's totals forward, so a restart cannot outspend `--max-spend`.

Windows client editions refuse scripts by default (execution policy
`Restricted`). `powershell -NoProfile -ExecutionPolicy Bypass -File
.\scripts\hatch.ps1 go-to-work` runs the supervisor anyway, and
`Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` allows it for the
account once; a clone carries no mark of the web, so `RemoteSigned` is
enough. Group Policy outranks both: if it is what refuses, it is the thing
to change, and no flag will get round it.

The container runner never asks.

Exit codes:

```
0    the night ended, for one of the reasons above - Ctrl-C, SIGTERM and a
     closed terminal included
1    a refusal: a flag it does not take, a stop file that already exists, a
     lock it could not take
75   asking the supervisor to rebuild and run it again
```

### One loop per served checkout

A loop is not limited to the checkout it is standing in: `--repo
/path/to/a/checkout` (repeatable) or `HATCH_REPOS` (checkout paths joined on
the platform's path separator) names others it serves too — which is what
lets one loop on a laptop work the two or three repositories already cloned
there, with no checkout of its own. Every named path is checked at startup:
it must exist and carry a `.git`, and a project's binding can only ever
match it once it has an `origin` — before anything is locked or claimed.

A second `go-to-work` naming any checkout this loop already serves is
refused, naming the pid of the one that already holds it. Two loops no
longer collide over the board — the claim divides it — but they would still
collide over a tree they share, one increment's reset landing in the middle
of another's branch. Two checkouts, two loops, and both are welcome, whether
each loop found its checkout by standing in it or by naming it.

A loop with none of its own checkouts of what a board binds can still serve
it. **Cloning is on by default**: an unset `HATCH_WORKSPACE` is a per-user
directory, and only `--workspace off` (or `HATCH_WORKSPACE=off`) never
clones — `hatch config --show` prints which this runner would do. Named or
default, the workspace is a directory the loop owns entirely, and it clones
whatever a project binds that it has no checkout of into
`<dir>/<host>/<owner>/<repo>` the first time a ticket needs it — a port in a
host, if there is one, spelled with `_` in place of `:`, since Windows will
not take a `:` in a directory name. A remote two projects both bind is
cloned once and served to both. A clone that fails releases the ticket,
comments on it with git's own line, and counts as a failed increment — three
of them in a row end the night the same way three failed spawns do. A
directory already holding clones from an earlier night is served as it is
found, nothing re-cloned.

A second loop that never named its own workspace shares the default one
with the first rather than being refused: it serves only the checkouts it
already has and clones nothing, since the first loop already owns whatever
directory the default's clones land in. A workspace may not sit inside a
named checkout, nor a named checkout inside it — a clone under a tree being
reset, or a tree under a directory of clones, is two owners of one path.

### What it does to your checkout

Before every increment, on every checkout the ticket's project is bound to —
so a session never has to work out its own base:

- It fetches, and puts the checkout back on the default branch at the tip
  the remote has it at right now — a binding's own base branch beats
  `origin/HEAD` for that checkout, and `HATCH_BASE_BRANCH` only ever
  overrides the checkout the loop is standing in.
- **Uncommitted and untracked changes go into a stash**, named for the hour
  it was taken. Nothing is discarded — `git stash pop` is how you get it
  back — and ignored files are never touched, so a dependency directory or a
  local `.env` is safe.
- A local trunk ahead of origin is reset to match it too — only `git
  reflog` keeps what it had, since this is the one case where committed
  work is not safe from the reset.
- A branch whose upstream on origin has been deleted is deleted locally too,
  named on the terminal with the short sha it pointed at, so `git branch
  <name> <sha>` puts it back.
- **Then it goes onto the ticket's branch.** If origin has one branch named
  for the key — the lowercased key, or that and a hyphen and anything — the
  checkout goes onto it at origin's tip and the trunk is merged in; a
  conflict is left in progress for the session, and the prompt says so
  ([what a session is told](what-a-session-is-told#the-branch)). With no
  branch, or only one already merged, the checkout stays on the trunk and
  the prompt names a branch to cut. With two unmerged branches the loop
  asks on the ticket which to use and spawns nothing. In a project that
  lands on the trunk, the checkout stays on `<trunk>` when origin has no
  branch for the ticket, and the session pushes `<trunk>` itself; the
  runner never does. Never a rebase, never a force-push.
- **When the increment ends, the tree is put right before the ticket is let
  go**: a merge or rebase left half-done is aborted, uncommitted changes are
  stashed under a message naming the ticket, unpushed commits and commits
  left on the local trunk are named on the ticket (the latter moved to a
  `-rescued-` branch), and a pull request whose branch has fallen behind the
  trunk gets the trunk merged into it on origin, when that merge is clean.
  Everything found is one comment on the ticket. The checkout is back on the
  trunk afterwards.

The practical consequence: work that matters is work that is committed. Do
not leave something half-finished in the tree and then start the loop in the
same checkout.

## Where the prompt comes from

Each increment's prompt, model and effort come from [a
playbook](/apps/hatch/playbooks) — a row per column transition and issue
type, yours to change and never your agents': the API refuses the write
from a key, on purpose. [What a session is told](what-a-session-is-told) has
the prompt section by section.

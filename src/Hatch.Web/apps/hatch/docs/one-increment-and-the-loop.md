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
  workspace is set. It beats `-i`.
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
   and reads back what **Runners** would like it to do: paused, stopping, a
   different scope, different bounds. Anything the board changed is printed
   (`the board set --max-runs 3`). Then the bounds, [below](#bounds-timeouts-and-stopping).
   This is the one moment no claim is held.
3. **Age restart.** Under Hatch's own supervisor a loop that has run long
   enough asks to come back as a newer build (*Restarts*, below).
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

The same bounds are controls on **Runners**, and a value set there is folded
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
the board asked this runner to stop     the Runners page, mid-night
there is no agent CLI to spawn          nothing was ever going to run
the keyboard asked (s)                  after the increment in flight
a --mine pass the board refused         the key belongs to nobody
the night's totals could not be written for a restart
```

An increment fails when its session exits non-zero, or a clone fails; one clean
exit resets the streak. The run finishes by printing the night: why it
stopped, how many increments and what they cost, what moved, and what stalled.

## Coming

Pages this one names that are not written yet:

- `watching-the-loop`
- `flow-wip-and-the-battery`

---
title: The hatch CLI, command by command
section: Running agents
order: 90
summary: Every `hatch` command, what it does, and what it costs to run.
---

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

## Where the prompt comes from

The prompt, model and effort of every increment come from a **playbook** — a row
per (column transition, issue type). The board ships with a working set, and the
Playbooks page is where you change what your sessions are told. They are yours
to edit and not your agents': the API refuses the write from a key, on purpose,
because an agent that could widen its own instructions and its own budget is a
loop with no end.

---
title: Running the runner as a container instead
section: Running agents
order: 40
summary: Running the runner as a container instead of a binary on your own machine, and what it can't build.
---

The section above is the primary path, and this one is the shortcut. Rather
than putting a binary on your `PATH` and keeping a terminal open, the same
stack can start a **container** that carries the runner, `git` and the `claude`
CLI, mounts one of your checkouts, and works tickets under the control of the
Runners page like any other runner.

It is off unless you ask for it. Nothing about the stack changes if you never
type the word `runner`.

**What it can and cannot do, first, because it decides whether this section is
for you.** The container carries `git` and the `claude` CLI and no language
runtimes at all — no .NET, no Node, no Python, no compilers. So it can plan,
break work down, analyse a repository and write code in any repository at all,
and it can commit and push what it wrote. It *cannot build or test* a
repository whose toolchain it does not have, and no general image has
everybody's. For a repository where "done" means a green build — which is most
of them — the base-OS runner above is the one to use, because it works with
whatever you already have installed. This one is for the planning, breaking
down and analysing that make up a good part of a board's work, and for
repositories whose tooling is `git` and a text editor.

## Starting it

Two things it has to be told: which checkout to work in — or a workspace to
clone into, if you have none of your own already checked out — and whose name
goes on the commits it makes.

macOS / Linux:

```
HATCH_CHECKOUT=/path/to/your/checkout \
HATCH_GIT_NAME="Your Name" \
HATCH_GIT_EMAIL=you@example.org \
docker compose --profile runner up -d
```

PowerShell:

```
$env:HATCH_CHECKOUT="C:\path\to\your\checkout"
$env:HATCH_GIT_NAME="Your Name"
$env:HATCH_GIT_EMAIL="you@example.org"
docker compose --profile runner up -d
```

It appears on the **Runners** page within a minute, called `hatch-runner`
(`HATCH_RUNNER_NAME` calls it something else), and the controls there — pause,
stop after this one, a spend cap, an hour to stop at — work on it exactly as
they do on a runner you started in a terminal. Stopping it from that page stops
the container too, rather than Docker restarting it behind your back.

`docker compose ... logs -f runner` is what it is saying while it works.

**Its Claude credential comes from the Settings page**, and from nowhere else.
Paste a token from `claude setup-token` into **Settings → Claude subscription
token** and the container picks it up on its next look. If none is saved when
it starts, it says so once and then waits, looking again every minute — so the
order you do these two things in does not matter.

**One container works one workspace.** A workspace clones what the board
binds, so one container can serve a whole board rather than one repository:
whatever a project binds that this runner has no checkout of, it clones into
`/workspace` the first time a ticket needs it, and reuses that clone for every
ticket after. If you already have a checkout of one of those repositories and
would rather this container work in it than clone its own, mount it at
`/checkout` with `HATCH_CHECKOUT` as above — that one is used in place of a
clone, and everything else the board binds is still cloned into the workspace
as needed.

## Letting it push

The container starts with no credential of its own, so pushing needs one of
these three. All three are the ones you already have — none of them is a new
account or a new token unless you want one.

**A token in the environment.** The simplest, and the only one that needs no
edit to the compose file. `HATCH_GIT_TOKEN` is handed to `git` when a push over
HTTPS asks for a password:

macOS / Linux:

```
HATCH_GIT_TOKEN=ghp_... HATCH_CHECKOUT=... docker compose --profile runner up -d
```

PowerShell:

```
$env:HATCH_GIT_TOKEN="ghp_..."
docker compose --profile runner up -d
```

**The credential helper you already have.** If `git push` works from your own
terminal over HTTPS without asking, something is already holding that
credential, and it can be mounted read-only. Edit the checked-out
`compose.yaml` and uncomment these two lines under the runner's `volumes:`:

```
      - "${HOME}/.gitconfig:/root/.gitconfig:ro"
      - "${HOME}/.git-credentials:/root/.git-credentials:ro"
```

On Windows the same two files live under `%USERPROFILE%`, and `${HOME}` there
is `${USERPROFILE}`:

```
      - "${USERPROFILE}/.gitconfig:/root/.gitconfig:ro"
      - "${USERPROFILE}/.git-credentials:/root/.git-credentials:ro"
```

This shape only carries what a *file* holds. A helper that keeps the
credential somewhere else — macOS's Keychain (`credential.helper = osxkeychain`),
Windows' Credential Manager (`manager`) — has nothing in `.git-credentials` to
mount, and there a token in the environment is the answer.

These same three shapes are what answers a clone's own prompt for a
credential, not only a push's — a repository the board binds that the
container has to clone into its workspace asks git for one exactly the way a
push does.

**An SSH key.** If your remote is `git@…` rather than `https://…`, the
container needs a key. On macOS, Docker Desktop bridges your own running
`ssh-agent` into a container at a fixed path, so no key ever leaves the host —
uncomment under `volumes:`, and add the matching line under `environment:`:

```
      - "/run/host-services/ssh-auth.sock:/ssh-agent"
```

```
      SSH_AUTH_SOCK: /ssh-agent
```

On Windows that bridge is not available to a Linux container, so the key itself
is mounted instead — read-only, and pointed at with `GIT_SSH_COMMAND`:

```
      - "${USERPROFILE}/.ssh/id_ed25519:/root/.ssh/id_ed25519:ro"
```

```
      GIT_SSH_COMMAND: "ssh -i /root/.ssh/id_ed25519 -o IdentitiesOnly=yes -o StrictHostKeyChecking=accept-new"
```

The same two lines work on macOS and Linux for a key that has no agent holding
it, with `${HOME}` in place of `${USERPROFILE}`.

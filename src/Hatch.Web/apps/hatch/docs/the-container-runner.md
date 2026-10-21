---
title: The container runner
section: Running agents
order: 30
summary: The runner packaged as a container beside the stack, for a Hatch with its wall off - what it can and cannot do, where its credential comes from, and how to let it push.
---

The same runner as [the runner](the-runner), started by Compose instead of by
you. Rather than putting a binary on your `PATH` and keeping a terminal open,
the stack can start a **container** that carries `hatch`, `git` and an agent
CLI, mounts one of your checkouts, and works tickets under the controls of the
board like any other runner.

It is off unless you ask for it. Nothing about the stack changes if you never
type the word `runner`.

## Whether it is for you

**What the image carries:** `git`, `openssh-client`, `curl`, `gh`, the
`claude`, `codex` and `grok` CLIs, and `hatch`. It carries **no language
runtime**: no .NET, no Node, no Python, no compilers. So it can plan, break
work down, analyse a repository and write code in any repository at all, and
it can commit and push what it wrote. It **cannot build or test** a repository
whose toolchain it does not have, and no general image has everybody's. For a
repository where "done" means a green build, which is most of them, use
[the runner](the-runner): it works with whatever you already have installed.

**And it is for a Hatch with its wall off.** The container gets its agent
credential by asking Hatch for it, and Hatch answers that request with a
`403` whenever the wall is on. A Hatch with its wall on
([the wall, off, on and required](the-wall-off-on-and-required)) is a Hatch
this container cannot take a credential from; run the runner on your own
machine there.

## Starting it

The profile is `runner`. Two things it has to be told: which checkout to work
in (or nothing, to work in a workspace of its own clones), and whose name goes
on the commits it makes.

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

A missing name or email is a refusal, not a default: the container prints what
to set and exits 1, because the default would be `root@` and a container's
hostname on a commit in your repository, forever.

To see what it is saying while it works:

```
docker compose --profile runner logs -f runner
```

It appears on **Agents → Runners** within a minute, under `HATCH_RUNNER_NAME`
(`hatch-runner` unless you set it), and takes its controls there (pause, stop
after this one, a spend cap, an hour to stop at) exactly as a runner you
started in a terminal does; see *Watching the loop*. Anything after `up -d` on
the command line is not passed through to the loop. To hand it bounds such as
`--max-runs` or `--under`, add a `command:` to the `runner:` service in
`compose.yaml`; the controls on the page are usually the easier route.

## Its credential

`HATCH_AGENT` picks the agent the container's loop spawns: `claude` (the
default), `codex` or `grok`. Anything else exits 1, naming the three.

Its credential comes from the gear's **Settings** page of your Hatch, and from
nowhere else; there is nothing to log in to inside the container, and nothing
mounts `~/.claude`. Which field depends on the agent:

- **Claude subscription token.** On a machine where `claude` is logged in, run
  `claude setup-token` and authorise it in the browser prompt that opens. It
  prints a token starting with `sk-ant-oat-`; paste it in and save.
- **Codex API key.** An API key from the OpenAI platform. A ChatGPT login does
  not travel to a container, since it is a file and not one value.
- **Grok Build API key.** An API key. `grok login` is for interactive machines
  and does not travel to a container either.

At start the entrypoint runs `hatch runner-credential <agent>` (an internal
command, not one for you to type), which prints one `NAME=value` line for the
agent (`CLAUDE_CODE_OAUTH_TOKEN`, `CODEX_API_KEY` or `XAI_API_KEY`) that the
entrypoint exports before it starts the loop. If none is saved, the container
says so once and waits, asking again every 60 seconds, so the order you do
these two things in does not matter.

It reads the credential **once, at start**. Save a new one on the Settings page
and the container keeps the old one until it is restarted:

```
docker compose --profile runner restart runner
```

## Letting it push

The container starts with no credential of its own, so pushing needs one of
these three. They answer a clone's prompt for a credential the same way they
answer a push's, so a repository the board binds that the container has to
clone into its workspace needs nothing more.

**A token in the environment.** The simplest, and the only one that needs no
edit to `compose.yaml`. `HATCH_GIT_TOKEN` is answered to `git` through
`GIT_ASKPASS`, whenever a push or clone over HTTPS asks for a username or a
password:

macOS / Linux:

```
HATCH_GIT_TOKEN=<a token your forge issued> \
HATCH_CHECKOUT=/path/to/your/checkout \
HATCH_GIT_NAME="Your Name" \
HATCH_GIT_EMAIL=you@example.org \
docker compose --profile runner up -d
```

PowerShell:

```
$env:HATCH_GIT_TOKEN="<a token your forge issued>"
docker compose --profile runner up -d
```

(with the three variables from *Starting it* still set in that session.)

**The credential helper you already have.** If `git push` works from your own
terminal over HTTPS without asking, something is holding that credential, and a
file-based one can be mounted read-only. In `compose.yaml`, uncomment these two
lines under the runner's `volumes:`:

```
      - "${HOME}/.gitconfig:/root/.gitconfig:ro"
      - "${HOME}/.git-credentials:/root/.git-credentials:ro"
```

On Windows `${HOME}` is `${USERPROFILE}`. This shape carries only what a
*file* holds. A helper that keeps the credential somewhere else, such as a
system keychain or Windows' Credential Manager, has nothing in
`.git-credentials` to mount, and there a token in the environment is the
answer.

**SSH.** If your remote is `git@…` rather than `https://…`, the container
needs a key. With an `ssh-agent` running that Docker can mount the socket of,
the key never leaves the host: uncomment this line under `volumes:`, and add
the matching one under `environment:` (both are needed):

```
      - "${SSH_AUTH_SOCK}:/ssh-agent"
```

```
      SSH_AUTH_SOCK: /ssh-agent
```

Without an agent, mount the key itself read-only with a `volumes:` line of
your own, and point `GIT_SSH_COMMAND` at it in `environment:`. With no
`known_hosts` and no `GIT_SSH_COMMAND` of your own, the entrypoint sets
`GIT_SSH_COMMAND` to `ssh -o StrictHostKeyChecking=accept-new`, because
nobody is there to answer "are you sure you want to continue connecting?".
Mount your own `known_hosts`, or set your own `GIT_SSH_COMMAND`, and that is
left alone.

Commits made in the container carry `HATCH_GIT_NAME` and `HATCH_GIT_EMAIL` as
both author and committer.

**`gh`.** Separately from pushing, `HATCH_GH_TOKEN` (arriving as `GH_TOKEN`)
lets `gh` read build checks and merge pull requests. The entrypoint says once,
at the top of its log, whether `gh` is signed in; without it the container
still works, and build checks and merges are off.

## The variables

Everything the `runner` service and its entrypoint read:

| Variable | Meaning | Default |
|---|---|---|
| `HATCH_CHECKOUT` | The repository to mount at `/checkout` | The directory `compose.yaml` was reached from, which is right only inside Hatch's own checkout. Optional once the container has a workspace to clone into instead |
| `HATCH_GIT_NAME` | The name on the commits it makes (`GIT_AUTHOR_NAME`) | Required |
| `HATCH_GIT_EMAIL` | The email on the commits it makes (`GIT_AUTHOR_EMAIL`) | Required |
| `HATCH_GIT_TOKEN` | An HTTPS token, answered to git through `GIT_ASKPASS` | Empty |
| `HATCH_GH_TOKEN` | The token `gh` reads, as `GH_TOKEN`, to check builds and merge | Empty |
| `HATCH_RUNNER_NAME` | What **Agents → Runners** calls it | `hatch-runner` |
| `HATCH_AGENT` | `claude`, `codex` or `grok` | `claude` |
| `HATCH_TAGS` | The tags it advertises, comma-separated | Empty |
| `HATCH_KEY` | An API key, for a Hatch with its wall on | Empty. Not required, and not useful here: against a wall the container cannot fetch its credential |

Five more are set for the container, and are not for changing: `HATCH_BASE`
(`http://api:8080`, the API over Compose's own network), `HATCH_ROOT`
(`/checkout`), `HATCH_WORKSPACE` (`/workspace`), `HATCH_RUNNER` (the name
above, as it arrives inside) and `IS_SANDBOX` (set to `1` by the image, because
the `claude` CLI refuses to bypass permission prompts as root unless it is told
it is in a sandbox, and an unattended increment has nobody to answer one).
[Environment variables](environment-variables) has the full table for the whole
of Hatch.

## One container, one workspace

`/checkout` is a bind mount of `HATCH_CHECKOUT`. `/workspace` is the named
volume `workspace`, which outlives the container being recreated. With both,
the checkout is served in place, and whatever else the board binds that this
runner has no checkout of is cloned into the workspace the first time a ticket
needs it, and reused after. So one container can serve a whole board rather
than one repository.

A mounted checkout is added to git's `safe.directory`, because the bind mount
arrives owned by the host's user. With neither a checkout at `/checkout` nor a
workspace to clone into, the entrypoint exits 1, and `restart: on-failure`
brings it back the moment you fix the mount.

Its loop's locks live inside the container, so nothing stops two containers
sharing one volume and working the same clone at once. Run **one container per
workspace**.

## Upgrading it

The service builds rather than pulls (`build:` with `pull_policy: build`), so
`up` rebuilds it from the source you pulled, every time:

```
git pull
docker compose --profile runner up -d
```

It never asks to restart itself the way the runner on your own machine does
(see *One increment and the loop*, under restarts), so a new build arrives
only this way; [upgrading](upgrading) covers the rest of the stack.

`restart: on-failure` is deliberate. The loop exits 0 when it was told to stop,
from **Agents → Runners** or by a bound it was given, and non-zero when
something went wrong; `unless-stopped` would restart the container after a
deliberate stop and undo the button that stopped it. A stop the board asked for
stays stopped, and a crash is still recovered from.

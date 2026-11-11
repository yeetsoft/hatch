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
runtime**: no .NET, no Node, no Python, no compilers.

The container advertises what it has like any runner, and its Dockerfile
installs none of `make`, `dotnet`, `node`, `pwsh`, Playwright's browsers, Docker or a
Postgres. So for a repository that declares a toolchain in
`.hatch/dependencies.json`, the board folds it from that project's development
steps ([what a repository needs](the-runner#what-a-repository-needs)), and it
still takes breakdown, analysis and review. The runner that *can* build such a
repository is the one built from the project's own image
([HA-1874](/apps/hatch/issues/HA-1874)), which Compose starts with the
`runner-image` profile ([starting it from a named image](#starting-it-from-a-named-image)). A container whose operator bakes in its own
toolchain declares it with `HATCH_HAS` ([the runner](the-runner#has)).

So it can plan, break
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

It appears on **Agents → Runners** within a minute, under its key's name, and
takes its controls there (pause, stop after this one, a spend cap, an hour to
stop at) exactly as a runner you started in a terminal does; see *Watching the
loop*. Anything after `up -d` on
the command line is not passed through to the loop. To hand it bounds such as
`--max-runs` or `--under`, add a `command:` to the `runner:` service in
`compose.yaml`; the controls on the page are usually the easier route.

## Starting it from a named image

The `runner` profile builds `containers/hatch-runner/Dockerfile`. A project's
own image, which carries that repository's toolchain on top of this one, is
already built somewhere: pulled from a registry, or left on this machine by a
build. The `runner-image` profile starts a container from it by name, with the
same environment, checkout and workspace as `runner`, and builds nothing.

Two variables:

- `HATCH_RUNNER_IMAGE` is the image to start, such as
  `ghcr.io/<owner>/<project>-runner:latest`. Required under this profile.
- `HATCH_RUNNER_PULL_POLICY` is `missing` (the default), `always` or `never`.
  `missing` pulls only an image this machine does not already have, so an
  image that exists only locally still starts. `always` pulls on every `up`,
  for the registry's current copy, and refuses an image that was never pushed.
  `never` pulls nothing.

macOS / Linux:

```
HATCH_RUNNER_IMAGE=ghcr.io/<owner>/<project>-runner:latest \
HATCH_CHECKOUT=/path/to/your/checkout \
HATCH_GIT_NAME="Your Name" \
HATCH_GIT_EMAIL=you@example.org \
docker compose --profile runner-image up -d
```

PowerShell:

```
$env:HATCH_RUNNER_IMAGE="ghcr.io/<owner>/<project>-runner:latest"
$env:HATCH_CHECKOUT="C:\path\to\your\checkout"
$env:HATCH_GIT_NAME="Your Name"
$env:HATCH_GIT_EMAIL="you@example.org"
docker compose --profile runner-image up -d
```

The service is `runner-image`, so `docker compose --profile runner-image logs -f
runner-image` is its log. `--profile runner` is still what builds the generic
image, and neither variable changes it.

**Without `docker compose`.** The same container is a `docker run`, with the
same variables the stack sets. The entrypoint is the image's own, so name no
command:

```
docker run -d --restart on-failure \
  -e HATCH_BASE=<where this Hatch is reached from the container> \
  -e HATCH_KEY=<the runner's key> \
  -e GIT_AUTHOR_NAME="Your Name" \
  -e GIT_AUTHOR_EMAIL=you@example.org \
  -v /path/to/your/checkout:/checkout \
  -v hatch-workspace:/workspace \
  ghcr.io/<owner>/<project>-runner:latest
```

**No image named.** The profile with no `HATCH_RUNNER_IMAGE` falls back to the
generic image that `runner` builds, and the entrypoint inside it refuses,
naming the variable and exiting 1. Build that image first (`--profile runner`);
without it Docker refuses with a pull error for `hatch-local-runner` instead,
which does not name the variable. The check cannot be Compose's
own required-variable form, since Compose reads every service's variables
before it applies a profile, and a required one would stop a plain `docker
compose up -d` as well.

**An image that is not a runner.** The service starts `/entrypoint.sh`
explicitly, so an image without one is refused straight away by Docker, naming
the path, rather than running whatever it defaults to and exiting without a
word.

### Pulling a private image

`docker` pulls the image before the container exists, so a private image needs
the host to be logged in to its registry (`docker login <registry>`), or, on a
cluster, an image pull secret. The container's own `gh` and git tokens
(`HATCH_GH_TOKEN`, `HATCH_GIT_TOKEN`) play no part in the pull.

## Its credential

`HATCH_AGENT` picks the agent the container's loop spawns: `claude` (the
default), `codex`, `grok` or `ollama`. Anything else exits 1, naming the four.

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
- **Ollama needs no credential.** It needs only `HATCH_OLLAMA_HOST` set to a
  server the container can reach: see
  [Reaching an Ollama outside the container](#reaching-an-ollama-outside-the-container).

At start the entrypoint runs `hatch runner-credential <agent>` (an internal
command, not one for you to type), which prints one `NAME=value` line for the
agent (`CLAUDE_CODE_OAUTH_TOKEN`, `CODEX_API_KEY` or `XAI_API_KEY`) that the
entrypoint exports before it starts the loop. If none is saved, the container
says so once and waits, asking again every 60 seconds, so the order you do
these two things in does not matter.

Hatch stores that credential obfuscated, not encrypted, and the route that
hands it out is refused to every caller when the wall is up, which is why
`HATCH_KEY` is no help here. `hatch` carries its own decoder for it, and that
is why the image has no `jq`, `python` or `openssl`.

It reads the credential **once, at start**. Save a new one on the Settings page
and the container keeps the old one until it is restarted:

```
docker compose --profile runner restart runner
```

A restart, or a `docker stop`, is safe in the middle of a ticket. The
entrypoint ends by `exec`ing `hatch go-to-work`, so the loop is the container's
first process and receives the signal itself; an increment interrupted
mid-ticket lets go of its claim on the way out.

## Reaching an Ollama outside the container

`ollama` needs no credential, so the container does not wait on the Settings
page for it. It does need a host, since the image never carries an Ollama of its
own, and `HATCH_OLLAMA_HOST` is where you say where one is:

```
HATCH_AGENT=ollama \
HATCH_OLLAMA_HOST=http://host.docker.internal:11434 \
docker compose --profile runner up -d
```

**An Ollama on the machine running the container.** On Docker Desktop,
`host.docker.internal` already means that machine. On Linux it does not, until
you add `extra_hosts: ["host.docker.internal:host-gateway"]` to the `runner:`
service in `compose.yaml`.

**An Ollama in its own container on the same Compose network.** Use the
service's name as the host, such as `http://ollama:11434`. Nothing more is
needed, since Compose's network resolves service names for its members.

**Making it reachable at all.** Ollama listens on loopback only by default, and
a container, whichever of the two ways above it comes, is not on that machine's
loopback. Setting `OLLAMA_HOST=0.0.0.0` on the Ollama side is yours to do; this
repository sets nothing there on your behalf.

`HATCH_OLLAMA_TIERS` is the other half: it says which model each alias (`haiku`,
`sonnet`, `opus`, `fable`) means to an `ollama` agent. Both are in
*The variables* below.

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
*file* holds. A helper that keeps the credential somewhere else, such as
`osxkeychain` on macOS (`credential.helper = osxkeychain`) or `manager` on
Windows, has nothing in `.git-credentials` to mount, and there a token in the
environment is the answer.

**SSH.** If your remote is `git@…` rather than `https://…`, the container
needs a key. On Linux, with an `ssh-agent` running that Docker can mount the
socket of, the key never leaves the host: uncomment this line under
`volumes:`, and add
the matching one under `environment:` (both are needed):

```
      - "${SSH_AUTH_SOCK}:/ssh-agent"
```

```
      SSH_AUTH_SOCK: /ssh-agent
```

On macOS, Docker Desktop bridges your own `ssh-agent` at a fixed path, so use
this line under `volumes:` instead (the `SSH_AUTH_SOCK: /ssh-agent` line under
`environment:` stays the same):

```
      - "/run/host-services/ssh-auth.sock:/ssh-agent"
```

On Windows, where no agent is bridged into a Linux container, and for any key
no agent holds, mount the key itself read-only under `volumes:` (`${HOME}` in
place of `${USERPROFILE}` on macOS and Linux):

```
      - "${USERPROFILE}/.ssh/id_ed25519:/root/.ssh/id_ed25519:ro"
```

and point `GIT_SSH_COMMAND` at it under `environment:`:

```
      GIT_SSH_COMMAND: "ssh -i /root/.ssh/id_ed25519 -o IdentitiesOnly=yes -o StrictHostKeyChecking=accept-new"
```

With no
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

Everything the `runner` and `runner-image` services and the entrypoint read:

| Variable | Meaning | Default |
|---|---|---|
| `HATCH_CHECKOUT` | The repository to mount at `/checkout` | The directory `compose.yaml` was reached from, which is right only inside Hatch's own checkout. Optional once the container has a workspace to clone into instead |
| `HATCH_GIT_NAME` | The name on the commits it makes (`GIT_AUTHOR_NAME`) | Required |
| `HATCH_GIT_EMAIL` | The email on the commits it makes (`GIT_AUTHOR_EMAIL`) | Required |
| `HATCH_GIT_TOKEN` | An HTTPS token, answered to git through `GIT_ASKPASS` | Empty |
| `HATCH_GH_TOKEN` | The token `gh` reads, as `GH_TOKEN`, to check builds and merge | Empty |
| `HATCH_AGENT` | `claude`, `codex`, `grok` or `ollama` | `claude` |
| `HATCH_OLLAMA_HOST` | Where the Ollama server is, for an `ollama` agent (see *Reaching an Ollama outside the container*) | Empty; required under `ollama` |
| `HATCH_OLLAMA_TIERS` | Which model id each alias (`haiku`, `sonnet`, `opus`, `fable`) means to an `ollama` agent | Empty: the CLI's own default model |
| `HATCH_KEY` | The API key the container calls with; the runner's name is the key's | Required: the runner is the key |
| `HATCH_RUNNER_IMAGE` | The image the `runner-image` profile starts (see *Starting it from a named image*) | Required under `runner-image`; unused under `runner` |
| `HATCH_RUNNER_PULL_POLICY` | Whether `runner-image` pulls: `missing`, `always` or `never` | `missing` |

Four more are set for the container, and are not for changing: `HATCH_BASE`
(`http://api:8080`, the API over Compose's own network), `HATCH_ROOT`
(`/checkout`), `HATCH_WORKSPACE` (`/workspace`) and `IS_SANDBOX` (set to `1` by the image, because
the `claude` CLI refuses to bypass permission prompts as root unless it is told
it is in a sandbox, and an unattended increment has nobody to answer one).
`IS_SANDBOX` is `claude`'s alone. `codex` needs nothing like it: its sandbox
starts as root in the unprivileged container through `bubblewrap`, which the
image installs, with no `security_opt` or `cap_add`. `grok` needs nothing
either, since it is started with `--always-approve --trust` and so has no OS
sandbox to satisfy.
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

## Building on it

A project image extends this image rather than reproducing it
([HA-1874](/apps/hatch/issues/HA-1874)). What it can count on staying put:

- **A Debian-family base.** The `hatch` binary is a glibc build, so an Alpine
  (musl) base would leave an image whose binary cannot start.
- **`hatch` at `/usr/local/bin/hatch`**, because the entrypoint, the loop and
  every script that shells out to it look it up there.
- **`/entrypoint.sh` as the `ENTRYPOINT`.** It turns the container's
  environment into a running loop, and an image that replaces it has stopped
  being a runner.
- **Root as the user**, with no `USER` instruction. A bind-mounted checkout is
  owned by the host's uid, and a container user whose uid does not match cannot
  write to the branch it is meant to push.
- **`IS_SANDBOX=1`**, which lets the `claude` CLI run in bypass mode as uid 0
  in an unattended increment.
- **`HATCH_ROOT=/checkout` and `HATCH_WORKSPACE=/workspace`**, where the loop
  looks for a mounted checkout and where it clones what it has no checkout of.
- **`/root/.local/bin` and `/root/.grok/bin` on `PATH`**, where the `claude` and
  `grok` installers put their binaries.
- **`/etc/hatch/` reserved** for what an image bakes in about itself. It exists
  and is empty here; a derived image may put files in it and may not use it for
  anything else.

The whole of a derived image can be two lines. The base is
`ghcr.io/<owner>/hatch-runner`, where `<owner>` is whoever publishes the image
you pull, never a literal owner:

```
FROM ghcr.io/<owner>/hatch-runner:latest
RUN apt-get update && apt-get install -y --no-install-recommends jq && rm -rf /var/lib/apt/lists/*
```

The base removes apt's lists at the end of each layer to keep the image small,
so the `apt-get update` here is the derived image's own.

A derived image changes none of the contract above and only adds packages.
It is also not the same thing as a project image `hatch image build` bakes
([HA-1874](/apps/hatch/issues/HA-1874)): neither this image nor a plain `FROM`
of it sets `HATCH_IMAGE`, so a runner inside either advertises no image -
[the runner](the-runner#image) says what one does once it is built that way.

The published base carries the label `org.opencontainers.image.revision`,
naming the commit it was built from, and a derived image inherits it unless its
own build sets one. It is published under two tags, `latest` and the commit sha;
[building it or pulling it](deployment-overview#building-it-or-pulling-it) has
which is which.

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

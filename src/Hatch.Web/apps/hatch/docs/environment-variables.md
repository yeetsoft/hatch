---
title: Environment variables
section: Reference
order: 40
summary: Every variable Hatch reads - the stack's, the runner's and the container's - with its default and what reads it.
---

Three programs read the variables here, and they do not read the same ones: the
API and its migration (through `compose.yaml`), the `hatch` binary, and the
container runner's entrypoint. So look at the **Read by** column first.

Nothing here is secret-bearing by default: every default below is empty, a
public value or a plain setting. The few variables that carry a secret are
named as such, and the value belongs in your shell or a `.env` beside
`compose.yaml`, never in a file you commit. The configuration keys these
variables land on inside the API (`Auth__*`, `Apps__*`, `Hatch__*` and
`ConnectionStrings__*`) belong to *Deployment overview*, not to this page; it
is under *Coming* below.

## Every variable

One table, grouped by the bold label in the first column. "Settings file" says
whether the `hatch` binary will take the variable from `scripts/.env` or from
the per-user file `hatch config` writes, as well as from the environment; those
three layers are explained in [the runner](the-runner#three-layers). A file
line naming a variable that says **no** is skipped, with a line on stderr
saying so; those are read from the environment alone.

| Variable | Read by | Default | What it does |
|---|---|---|---|
| **The stack's, which you set** | | | |
| `HATCH_PORT` | `compose.yaml`, for the `api` service | `8080` | The port on this machine the API is published at. |
| `HATCH_AUTH` | `compose.yaml`, for `migrate` and `api` (as `Auth__Enabled`) | `false` | Whether the wall is up. Turning it on is [putting a front door on it](putting-a-front-door-on-it). |
| `HATCH_GOOGLE_CLIENT_ID` | `compose.yaml`, for `migrate` and `api` | empty | The Google sign-in client's id, for a Hatch with its wall up. |
| `HATCH_GOOGLE_CLIENT_SECRET` | `compose.yaml`, for `migrate` and `api` | empty | **A secret.** The Google sign-in client's secret. |
| `HATCH_GOOGLE_REDIRECT_URI` | `compose.yaml`, for `migrate` and `api` | empty: derived from the request | Where Google sends a person back to. Set it behind a TLS proxy, where the scheme and host the API sees are not the ones Google has registered. |
| `HATCH_PUBLIC_URL` | `compose.yaml`, for `api` (as `Apps__PublicBaseUrl`) | empty: the address the runner itself reaches Hatch at | The address a browser opens this Hatch at, so a pull request's link back to its ticket is one a reviewer can open. |
| `HATCH_ANDROID_PACKAGE` | `compose.yaml`, for `api` | empty: no statements are served | The package name of one phone's installed app, read off the phone ([Hatch on your phone](hatch-on-your-phone)). |
| `HATCH_ANDROID_CERT_FINGERPRINT` | `compose.yaml`, for `api` | empty: no statements are served | The signing certificate's fingerprint for that same app. |
| **The stack's own, which nobody changes** | | | |
| `HATCH_MIGRATE` | the API (`migrate` sets it to `1`) | unset | Runs the API image as the migration, which exits when it is done, instead of serving. |
| **The runner service in the stack, from your shell or `.env`** | | | |
| `HATCH_CHECKOUT` | `compose.yaml`, for `runner` | `.`: the directory `compose.yaml` was reached from | The repository mounted at `/checkout`. Right by default only inside Hatch's own checkout. |
| `HATCH_RUNNER_NAME` | `compose.yaml`, for `runner` (becomes `HATCH_RUNNER`) | `hatch-runner` | What **Agents → Runners** calls the container. |
| `HATCH_TAGS` | `compose.yaml`, for `runner`; then `hatch` | empty: the container advertises no tags | The tags this container advertises, comma-separated ([the runner](the-runner#what-a-runner-advertises)). |
| `HATCH_GIT_NAME` | `compose.yaml`, for `runner` (becomes `GIT_AUTHOR_NAME`) | empty; the container refuses to start without it | The name on the commits it makes. |
| `HATCH_GIT_EMAIL` | `compose.yaml`, for `runner` (becomes `GIT_AUTHOR_EMAIL`) | empty; the container refuses to start without it | The email on the commits it makes. |
| `HATCH_GIT_TOKEN` | `compose.yaml`, for `runner`; then the entrypoint | empty | **A secret.** An HTTPS token, answered to `git` through `GIT_ASKPASS`. |
| `HATCH_GH_TOKEN` | `compose.yaml`, for `runner` (becomes `GH_TOKEN`) | empty: build checks and merges are off | **A secret.** The token `gh` reads to check builds and merge pull requests. |
| **Set for the container by `compose.yaml` or the image** | | | |
| `HATCH_BASE` | `compose.yaml`, for `runner`; then `hatch` | `http://api:8080` | The API over Compose's own network. Nothing about this crosses the machine. |
| `HATCH_ROOT` | the image; `compose.yaml`; then `hatch` and the entrypoint | `/checkout` | Where the checkout is mounted. The entrypoint unsets it when nothing is there and a workspace is. |
| `HATCH_WORKSPACE` | the image; `compose.yaml`; then `hatch` and the entrypoint | `/workspace` | Where the container clones what the board binds that it has no checkout of. |
| **The `hatch` binary** | | | |
| `HATCH_BASE` | `hatch` | none; `hatch` refuses to run without it | The origin of your Hatch, as `<your-hatch-origin>`. A trailing `/` is trimmed. Settings file: yes. |
| `HATCH_KEY` | `hatch` | empty | **A secret.** The API key, shaped `hatch_ak_...`. Optional against a Hatch with its wall off. Settings file: yes. |
| `HATCH_AGENT` | `hatch`; the container's entrypoint | `claude`; blank also means `claude` | Which agent a session spawns: `claude`, `codex` or `grok`. An unknown id is refused. Settings file: yes. |
| `HATCH_CLAUDE_BIN` | `hatch` | unset: found on `PATH` | The `claude` CLI, if it is not on `PATH`. Settings file: yes. |
| `HATCH_CODEX_BIN` | `hatch` | unset: found on `PATH` | The `codex` CLI, if it is not on `PATH`. Settings file: yes. |
| `HATCH_GROK_BIN` | `hatch` | unset: found on `PATH` | The `grok` CLI, if it is not on `PATH`. Settings file: yes. |
| `HATCH_BASE_BRANCH` | `hatch` | unset: the branch origin calls its default | The trunk `go-to-work` resets to between increments. It has no `hatch config` flag. Settings file: yes. |
| `HATCH_RUNNER` | `hatch` | unset: a cast-list name, chosen once per checkout | What the board calls this runner ([the runner](the-runner#the-runners-name)). Settings file: yes. |
| `HATCH_REPOS` | `hatch` | empty | The checkouts a loop with no checkout of its own serves, joined on the platform's path separator (`:` on Unix, `;` on Windows). Blanks are dropped. Settings file: yes. |
| `HATCH_TAGS` | `hatch` | empty | The tags this runner advertises, comma-separated; trimmed, lowercased. A tag that breaks the rule ([the runner](the-runner#what-a-runner-advertises)) refuses every command. Settings file: yes. |
| `HATCH_WORKSPACE` | `hatch` | unset: a per-user `hatch/workspace` under the OS application-data directory, created only when a clone needs it | A directory the runner owns entirely, where it clones what the board binds that it has no checkout of. `off`, in any case, means never clone. Settings file: yes. |
| `HATCH_HEARTBEAT` | `hatch` | `20` | Seconds of silence before the readout says what it is waiting on. `0` turns the pulse off; a value that is not a whole number, or is negative, falls back to `20`. Settings file: **no**. |
| `HATCH_RETRY_SECONDS` | `hatch` | `90` | Seconds a call keeps retrying Hatch. `0` means exactly one attempt; a value that is not a whole number, or is negative, falls back to `90`. Settings file: **no**. |
| `HATCH_ROOT` | `hatch` | unset: the nearest ancestor of the working directory that has a `.git` | The checkout to work in. Set to a directory that does not exist, there is no fallback: `hatch` has no checkout. Settings file: **no**. |
| `HATCH_NIGHT_STATE` | `hatch` | unset | Where a night's totals are handed to the loop that restarts into. Set only by `scripts/hatch.sh` and `scripts/hatch.ps1`, which is how a hand-started `hatch` knows nothing will restart it. Settings file: **no**. |
| `HATCH_RTK` | `hatch` | unset | `off`, in any case, keeps RTK's Bash hook out of a session. Anything else uses it when `rtk` is on `PATH`. Settings file: **no**. |
| **The wrapper scripts** | | | |
| `HATCH_RUNNER_BIN` | `scripts/hatch.sh`, `scripts/hatch.ps1` | unset: the script finds or builds the binary | The path of a `hatch` binary you already have, for a machine with no SDK. It must be an executable file. With it set there is nothing to rebuild. |
| **The container's entrypoint** | | | |
| `HATCH_ROOT` | the entrypoint | `/checkout`, from the image | If it holds a `.git`, it is added to git's `safe.directory`. If not, and `HATCH_WORKSPACE` is set, `HATCH_ROOT` is unset and the loop clones into the workspace. If neither, the entrypoint exits 1. |
| `HATCH_WORKSPACE` | the entrypoint | `/workspace`, from the image | The fallback above. |
| `HATCH_GIT_TOKEN` | the entrypoint | empty | When set, the entrypoint exports `GIT_ASKPASS`. |
| `HATCH_AGENT` | the entrypoint | `claude` | Exits 1 for anything but `claude`, `codex` or `grok`. |
| `HATCH_GIT_NAME` | the entrypoint, in its refusal only | | It reads `GIT_AUTHOR_NAME`, which `compose.yaml` fills from this; its message names this variable. |
| `HATCH_GIT_EMAIL` | the entrypoint, in its refusal only | | The same, for `GIT_AUTHOR_EMAIL`. |
| `HATCH_GH_TOKEN` | the entrypoint, in its message only | | It checks whether `gh` is signed in, and names this variable when it is not. `gh` reads `GH_TOKEN`. |
| `HATCH_CHECKOUT` | the entrypoint, in its refusal only | | It names this variable when there is no checkout to work in. |

The container's variables, with their defaults and what to put in them, are
[the container runner](the-container-runner#the-variables)'s table; the rows
above are here so that every name has one home.

### What the container sets from them

When you are debugging a container you will look for these, and they come from
three different places.

| Variable | Set by | Value |
|---|---|---|
| `GIT_AUTHOR_NAME`, `GIT_AUTHOR_EMAIL` | `compose.yaml` | From `HATCH_GIT_NAME` and `HATCH_GIT_EMAIL`. |
| `GH_TOKEN` | `compose.yaml` | From `HATCH_GH_TOKEN`. |
| `GIT_COMMITTER_NAME`, `GIT_COMMITTER_EMAIL` | the entrypoint | Copies of the author's. |
| `GIT_ASKPASS` | the entrypoint | `/usr/local/bin/git-askpass.sh`, only when `HATCH_GIT_TOKEN` is set. |
| `GIT_SSH_COMMAND` | the entrypoint | `ssh -o StrictHostKeyChecking=accept-new`, only when neither it nor `~/.ssh/known_hosts` already exists. |
| `IS_SANDBOX` | the image | `1`, because the `claude` CLI refuses to bypass permission prompts as root unless it is told it is in a sandbox. |

The agent's own credential is not a variable you set. The entrypoint fetches it
from your Hatch with `hatch runner-credential` and exports it before the loop
starts ([the container runner](the-container-runner#its-credential)).

`HATCH_REVISION` is a build argument in `containers/hatch-runner/Dockerfile`,
not a runtime variable: it stamps the revision into the binary when the image
is built, and nothing reads it once the image exists.

## Not Hatch's own

The `hatch` binary reads these and does not own them.

| Variable | What the binary does with it |
|---|---|
| `NO_COLOR` | Set to anything, even empty, it turns the readout's colour off. |
| `TERM` | `dumb` turns the readout off. |
| `PATH` | Where the agent CLIs and `rtk` are found when their `HATCH_*_BIN` variable is not set. |
| `CODEX_HOME` | Read to find the operator's Codex home. A `codex` session is spawned with its own overlay of it. |

## Coming

Pages this one names that are not written yet:

- `deployment-overview`

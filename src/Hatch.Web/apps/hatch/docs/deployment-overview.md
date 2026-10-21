---
title: Deployment overview
section: Deploying and operating
order: 10
summary: What a running Hatch is made of - the five Compose services and which of them exits on purpose, how to start it, move its port and stop it, and how to run from the published images instead of building.
---

Hatch is a Postgres and one API image, and that image also serves every app.
`compose.yaml` at the repository root runs both on one computer, and it is the
default: everything on this page is about that file. A cluster is the other
way to run it (`kubernetes`, below), and no manifests ship with Hatch.

## What runs

| Service | What it is | Lifecycle |
|---|---|---|
| `db` | Postgres, built from `containers/hatch-db/Dockerfile.db`: `postgres:18.4` plus `pginit.sql`, which on a fresh volume creates the `hatch` and `quartz` databases and Quartz's tables. Its healthcheck is `pg_isready`. Port 5432 is published, because this is the developer's stack and its tests reach Postgres from the host | Runs until stopped |
| `migrate` | The API image with `HATCH_MIGRATE=1`, which makes it apply the schema and nothing else | Runs to completion on every start, then **exits, and exited is correct**: that is the state `api` waits for |
| `api` | The API, and every app it serves, on 8080 inside the container and `HATCH_PORT` outside it | Starts only once `migrate` has completed successfully, and runs until stopped |
| `runner` | [The container runner](the-container-runner), under the profile `runner` | Off unless that profile is named |
| `design` | The `@hatch/ui` gallery on 5173, a Vite dev server, under the profile `design` | Off unless that profile is named |

`migrate` and `api` are the same image. Because `migrate` is a separate
service, a schema change is applied before the new API is started rather than
raced by it, on every start and not only the first. A stack whose `migrate`
shows `Exited (0)` is a healthy stack.

Two volumes hold what outlives the containers. `pgdata` (`hatch-local_pgdata`)
is the data: the board, the comments, the keys. `workspace` holds the container
runner's clones and is not data; nothing in it cannot be cloned again. The line
`name: hatch-local` at the top of `compose.yaml` fixes the project, the network
and the volume names, so they are the same wherever the command is run from.

## Starting it

```
docker compose up -d
```

Then open `<your-hatch-origin>/apps/hatch/`, which on one computer is
`http://localhost:8080/apps/hatch/`.

Port 8080 is only the default. `HATCH_PORT` moves it:

macOS / Linux:

```
HATCH_PORT=9090 docker compose up -d
```

PowerShell:

```
$env:HATCH_PORT=9090; docker compose up -d
```

Or put a `HATCH_PORT=9090` line in the `.env` beside `compose.yaml`, which
Compose reads on its own, and the port stays moved on every later command.

A profile is named on each command, because Compose starts nothing under a
profile nobody named:

```
docker compose --profile runner up -d
```

## Building it or pulling it

Compose builds every image from the checkout: `db`, `migrate`, `api` and
`runner` each have a `build:` and `pull_policy: build`, so every `up` rebuilds
them. That is why `git pull` and then `up -d` is an upgrade
([upgrading](upgrading)). The build is all cache when nothing changed.

The same images are also published. `.github/workflows/publish.yml` pushes them
to the GitHub container registry after every green build of the trunk:

| Image | Tags |
|---|---|
| `ghcr.io/<owner>/hatch-api` | `latest`; the commit sha; and `<YYYYMMDDHHmmss>-<sha>`, which is there for image automation to order builds by |
| `ghcr.io/<owner>/hatch-db` | `latest` and the commit sha |
| `ghcr.io/<owner>/hatch-runner` | `latest` and the commit sha, for `linux/amd64` and `linux/arm64` |

`<owner>` is the account the repository lives under. `migrate` and `api` are
both `hatch-api`.

**To run from the images instead of building,** put a `compose.override.yaml`
beside `compose.yaml`. Compose reads it on its own and lays it over the
file, so nothing in `compose.yaml` is edited:

```yaml
services:
  db:
    image: ghcr.io/<owner>/hatch-db:latest
    pull_policy: always
  migrate:
    image: ghcr.io/<owner>/hatch-api:latest
    pull_policy: always
  api:
    image: ghcr.io/<owner>/hatch-api:latest
    pull_policy: always
  runner:
    image: ghcr.io/<owner>/hatch-runner:latest
    pull_policy: always
```

Leave out `runner` if you do not use it. Then fetch the images and start them:

```
docker compose pull
docker compose up -d
```

A sha tag pins a version; `latest` follows the trunk. If the packages are
private, `docker login ghcr.io` first, with a token that can read packages.

## The variables

`compose.yaml` is set from the shell or from a `.env` beside it, and
[Environment variables](environment-variables) has every one. The operator's
eight: `HATCH_PORT`; `HATCH_AUTH` → `Auth__Enabled`; the three
`HATCH_GOOGLE_*` variables → `Auth__Google__*`; `HATCH_PUBLIC_URL` →
`Apps__PublicBaseUrl`; and the two `HATCH_ANDROID_*` variables →
`Apps__AssetLinks__0__…`. The runner's own variables are on
[the container runner](the-container-runner#the-variables). `api` passes the
shell's own `USERNAME`/`USER` as `Auth__LocalPerson__Name`, so there is
nothing to set for that one.

## Where secrets go

- **The `.env` beside `compose.yaml`.** Git-ignored (`.gitignore:13-17`).
- **`src/Hatch.Api/.env.json`.** Bind-mounted at `/App/.env.json`
  (`compose.yaml:80`) and git-ignored (`.gitignore:12`). It loads after the
  environment, so it wins (`Program.cs:56-62`). It holds `ha_host`, `ha_port`
  and `ha_token`, imported once into site settings
  (`Services/DeviceMapping/DeviceMappingSeeder.cs:131-146`), and
  `vm_log_shipper_token` (`Controllers/VmConsoleLogsController.cs:29`). A board
  needs none of them.
- **Never the repository**, a commit, an issue or a plan (`docs/ethos.md`).
- **In a cluster, a Secret:** `kubernetes`, below.

## What the API reads

Each key below is the environment spelling (`__` per level) of an
`appsettings.json` section. Defaults are the initialisers; the only hosts
named anywhere here are `localhost`, `db` and compose's own service names.

| Key | Default | What it does |
|---|---|---|
| **`Auth__*`, bound by `Services/Auth/AuthOptions.cs`** | | |
| `Auth__Enabled` | `false` | Whether the wall refuses anything at all; `false` leaves the app behaving exactly as it did before auth existed. |
| `Auth__EnforceInProcess` | `true` | Whether this pod also refuses on its own, rather than only answering `/api/auth/verify` on Traefik's behalf. |
| `Auth__CookieName` | `__Secure-hatch_grant` | The cookie the grant token rides in. |
| `Auth__CookieDomain` | empty | The cookie's `Domain` attribute; empty makes the cookie host-only. |
| `Auth__ExemptHosts__0` | empty | A hostname the gate never challenges, whatever the path. |
| `Auth__InviteTtlMinutes` | `15` | How long an invite code is worth typing. |
| `Auth__BootstrapInviteTtlMinutes` | `60` | TTL for the invite the migration mints into an empty install. |
| `Auth__GrantRenewAfterDays` | `30` | Re-issues the cookie once it is this old. |
| `Auth__LastSeenThrottleSeconds` | `60` | Floor on how often a grant's last-seen time is written. |
| `Auth__SignInPath` | `/apps/auth/` | Where a document request with no grant is sent. |
| `Auth__RedeemAttemptsPerWindow` | `10` | Redemption attempts one client IP may spend per window. |
| `Auth__RedeemWindowSeconds` | `60` | The window `RedeemAttemptsPerWindow` is counted over. |
| `Auth__LocalPerson__Name` | empty | Who local mode says is sitting at the machine when there is no wall to ask. `api` passes the shell's own `USERNAME`/`USER` here, so there is nothing to set. |
| `Auth__Google__ClientId` | empty | The Google sign-in client's id. |
| `Auth__Google__ClientSecret` | empty | **A secret.** The Google sign-in client's secret. |
| `Auth__Google__RedirectUri` | empty | Where Google sends a person back to; set it behind a TLS proxy, where the scheme and host the API sees are not the ones Google has registered. |
| **`Apps__*`, bound by `Modules/AppsOptions.cs`** | | |
| `Apps__PublicBaseUrl` | empty | The address a browser opens this Hatch at, printed into a QR label or a pull request's link back to its ticket. |
| `Apps__AssetLinks__0__PackageName` | empty | One phone's installed app, trusted to open this install's links without the user granting it by hand. |
| `Apps__AssetLinks__0__Sha256CertFingerprints__0` | empty | That app's signing certificate fingerprint. |
| **`Hatch__*`, bound by `Modules/Hatch/HatchOptions.cs`** | | |
| `Hatch__ClaimTtlSeconds` | `300` | How long a claim survives without a heartbeat. |
| `Hatch__RunnerGoneAfterSeconds` | `90` | How long a runner may go without a heartbeat before its row reads *gone*. |
| `Hatch__StallLapseMinutes` | `5` | How long a claim may go untouched before it is considered quiet. |
| `Hatch__StallResumeMinutes` | `15` | How long a marked issue is folded before the loop picks it back up on its own. |
| `Hatch__StallResumeLimit` | `3` | How many consecutive stalls an issue may resume through before it holds for a person instead. |
| `Hatch__DoneHorizonHours` | `48` | How long a card sits in a terminal column before the board stops handing it over. |
| **The database** | | |
| `ConnectionStrings__Hatch` | `Host=localhost` | Where `hatch`'s own schema lives. Under `DOTNET_ENVIRONMENT=Docker`, which `compose.yaml` sets, the default is `Host=db` instead. |
| `ConnectionStrings__Quartz` | `Host=localhost` | Where Quartz's tables live - created only by `containers/hatch-db/pginit.sql`. Also `Host=db` under `DOTNET_ENVIRONMENT=Docker`. |
| **Everything else** | | |
| `HATCH_MIGRATE` | unset | `1` makes the image run as the migration, which applies the schema and exits, instead of serving. |
| `KioskFiles__BaseAddress` | `http://files/` | Where the kiosk app's signature checksum comes from. A board never calls it. |
| `DOMAIN` | unset | The host the kiosk app's download link is built against. A board never calls it. |
| `DOTNET_ENVIRONMENT` | | Any value but `Development` writes the log as JSON lines instead of the human-formatted default. |

`Auth__SignInPath` cannot usefully move: the wall exempts the literal
`/apps/auth` (`Services/Auth/AuthGate.cs:180`), so pointing it elsewhere opens
a redirect loop. The wall's own off/on/required states are covered on
[the wall, off, on and required](the-wall-off-on-and-required).

A malformed `Apps__AssetLinks` entry stops the API at start
(`Modules/AssetLinksValidation.cs`), unlike `Apps__PublicBaseUrl`, which
degrades quietly.

The migration creates `hatch`'s schema on every start; see
[backup, restore and replication](backup-restore-and-replication)
for backing it up, restoring it and running it on a replica.

## How to stop

```
docker compose down
```

**`down` keeps your data.** The containers go; the volume outlives them, so
`down` and then `up -d` again — even after an upgrade has rebuilt the images —
brings back the same board. `down` takes the runner container with it whether or not you
name its profile, because it removes everything in the project.

```
docker compose down -v
```

**`down -v` throws your data away.** The `-v` removes the volume, and there is
no undo. Use it when you want to start over from an empty board, and not
otherwise.

## Coming

Pages this one links to that are not written yet:

- `kubernetes`

---
title: Deployment overview
section: Deploying and operating
order: 10
summary: What a running Hatch is made of - the five Compose services and which of them exits on purpose, how to start it, move its port and stop it, and how to run from the published images instead of building.
---

Hatch is a Postgres and one API image, and that image also serves the board
and the sign-in shell.
`compose.yaml` at the repository root runs both on one computer, and it is the
default: everything on this page is about that file. A cluster is the other
way to run it ([Kubernetes](kubernetes)), and no manifests ship with Hatch.

## What runs

| Service | What it is | Lifecycle |
|---|---|---|
| `db` | Postgres, built from `containers/hatch-db/Dockerfile.db`: `postgres:18.4` plus `pginit.sql`, which on a fresh volume creates the `hatch` and `quartz` databases and Quartz's tables. Its healthcheck is `pg_isready`. Port 5432 is published, because this is the developer's stack and its tests reach Postgres from the host | Runs until stopped |
| `migrate` | The API image with `HATCH_MIGRATE=1`, which makes it apply the schema and nothing else | Runs to completion on every start, then **exits, and exited is correct**: that is the state `api` waits for |
| `api` | The API, and the board and sign-in shell it serves, on 8080 inside the container and `HATCH_PORT` outside it | Starts only once `migrate` has completed successfully, and runs until stopped |
| `runner` | [The container runner](the-container-runner), under the profile `runner` | Off unless that profile is named |

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

Then open `<your-hatch-origin>/`, which on one computer is
`http://localhost:8080/`.

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
ten: `HATCH_PORT`; `HATCH_AUTH` → `Auth__Enabled`; the three
`HATCH_GOOGLE_*` variables → `Auth__Google__*`; `HATCH_PUBLIC_URL` →
`Hatch__PublicBaseUrl`; the two `HATCH_ANDROID_*` variables →
`Hatch__AssetLinks__0__…`; and `HATCH_SECRET_KEY` → `Hatch__SecretKey` with
`HATCH_PREVIOUS_SECRET_KEY` → `Hatch__PreviousSecretKey`. The runner's own variables are on
[the container runner](the-container-runner#the-variables). `api` passes the
shell's own `USERNAME`/`USER` as `Auth__LocalPerson__Name`, so there is
nothing to set for that one.

## Where secrets go

- **The `.env` beside `compose.yaml`.** Git-ignored (`.gitignore:13-17`). This is where
  `HATCH_SECRET_KEY` goes: [the secret key](the-secret-key) says how to make one.
- **`src/Hatch.Api/.env.json`.** Bind-mounted at `/App/.env.json`
  (`compose.yaml:83`) and git-ignored (`.gitignore:12`). It loads after the
  environment, so it wins (`Program.cs:48-52`), for `Hatch:SecretKey` as for
  everything else. A board needs nothing
  from it: it is a place for operator secrets in local development.
  `make run` and `make up` make a development secret key there, at
  `Hatch:SecretKey`, if it holds none, and never replace one. An install made
  any other way sets `HATCH_SECRET_KEY` itself.
- **Never the repository**, a commit, an issue or a plan (`docs/ethos.md`).
- **In a cluster, a Secret:** see [Kubernetes](kubernetes).

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
| `Auth__SignInPath` | `/auth/` | Where a document request with no grant is sent. |
| `Auth__RedeemAttemptsPerWindow` | `10` | Redemption attempts one client IP may spend per window. |
| `Auth__RedeemWindowSeconds` | `60` | The window `RedeemAttemptsPerWindow` is counted over. |
| `Auth__LocalPerson__Name` | empty | Who local mode says is sitting at the machine when there is no wall to ask. `api` passes the shell's own `USERNAME`/`USER` here, so there is nothing to set. |
| `Auth__Google__ClientId` | empty | The Google sign-in client's id. |
| `Auth__Google__ClientSecret` | empty | **A secret.** The Google sign-in client's secret. |
| `Auth__Google__RedirectUri` | empty | Where Google sends a person back to; set it behind a TLS proxy, where the scheme and host the API sees are not the ones Google has registered. |
| **`Hatch__*`, bound by `Modules/Hatch/HatchOptions.cs`** | | |
| `Hatch__PublicBaseUrl` | empty | The address a browser opens this Hatch at, printed into a pull request's link back to its ticket. |
| `Hatch__AssetLinks__0__PackageName` | empty | One phone's installed app, trusted to open this install's links without the user granting it by hand. |
| `Hatch__AssetLinks__0__Sha256CertFingerprints__0` | empty | That app's signing certificate fingerprint. |
| `Hatch__SecretKey` | empty | **A secret.** The key secrets are sealed under: base64 of exactly 32 bytes (`openssl rand -base64 32`). Empty means none: the API starts, warns once, and cannot save a secret. See [the secret key](the-secret-key). |
| `Hatch__PreviousSecretKey` | empty | **A secret.** The key before the current one, kept while a change of key is under way. It opens what it sealed and seals nothing. Same form as `Hatch__SecretKey`. See [changing the key](the-secret-key#changing-the-key). |
| `Hatch__ClaimTtlSeconds` | `300` | How long a claim survives without a heartbeat. |
| `Hatch__RunnerGoneAfterSeconds` | `90` | How long a runner may go without a heartbeat before its row reads *gone*. |
| `Hatch__StallLapseMinutes` | `5` | How long a claim may go untouched before it is considered quiet. |
| `Hatch__StallResumeMinutes` | `15` | How long a marked issue is folded before the loop picks it back up on its own. |
| `Hatch__StallResumeLimit` | `3` | How many consecutive stalls an issue may resume through before it holds for a person instead. |
| `Hatch__RunnerPauseAfter` | `3` | How many releases in a row, each marked as having spent no tokens, pause a runner session. `0` turns the pause off; a negative value falls back to `3`. |
| `Hatch__RunnerPauseMinutes` | `60` | How long a paused session stands down, in minutes. Zero or less falls back to `60`. |
| `Hatch__TrainStallMinutes` | `10` | How long a merge train's head may be judged continuously mergeable before its driver is passed over to another live, eligible runner. |
| `Hatch__DoneHorizonHours` | `48` | How long a card sits in a terminal column before the board stops handing it over. |
| `Hatch__BackupPath` | empty | The folder inside the container the daily `pg_dump` copy is written to; empty means backups are off. |
| `Hatch__BackupKeepDays` | `14` | How many days of copies are kept; a copy goes only past this age and only while that many newer copies remain; `0` keeps everything. |
| `Hatch__GooseIntervalSeconds` | `60` | Seconds between the goose's sweeps of the board. Zero or less falls back to `60`. |
| `Hatch__GooseKeepDays` | `7` | How many days a goose run is kept after its last sweep; `0` keeps everything, and less than that falls back to `7`. |
| `Hatch__GooseTrainReadyMinutes` | `30` | How long a merge train's head may be judged mergeable before the goose's `train-ready` check opens a finding. `0` turns the check off; a negative value falls back to `30`. |
| `Hatch__GooseUnclaimedMinutes` | `10` | How long a project may have ready work and a live, idle runner able to take some of it before the goose's `ready-unclaimed` check opens a finding. `0` turns the check off; a negative value falls back to `10`. |
| `Hatch__GooseNoRunnerMinutes` | `15` | How long a project may have ready work that no live runner can take before the goose's `ready-no-runner` check opens a finding. `0` turns the check off; a negative value falls back to `15`. |
| `Hatch__GooseRepeatClaims` | `5` | How many claims by one runner on one ticket, with nothing about the ticket moving, make the goose's `claim-repeating` check open a finding. `0` turns the check off; a negative value falls back to `5`. |
| `Hatch__GooseQuestionHours` | `24` | How many hours a question may wait on a person before the goose's `questions-waiting` check opens a finding. `0` turns the check off; a negative value falls back to `24`. |
| `Hatch__GooseChatSendMinutes` | `60` | How many minutes sends to a chat workspace or room may keep failing before the goose's `chat-sends-failing` check opens a finding. `0` turns the check off; a negative value falls back to `60`. |
| `Hatch__GooseChatListenMinutes` | `60` | How many minutes a chat workspace's connection may keep failing to open before the goose's `chat-listen-failing` check opens a finding. `0` turns the check off; a negative value falls back to `60`. |
| `Hatch__GooseStandingHours` | `4` | How many hours a ticket may stand in one column, with no runner on it and nobody it is waiting for, before the goose's `standing-still` check opens a finding. `0` turns the check off; a negative value falls back to `4`. |
| **The database** | | |
| `ConnectionStrings__Hatch` | `Host=localhost` | Where `hatch`'s own schema lives. Under `DOTNET_ENVIRONMENT=Docker`, which `compose.yaml` sets, the default is `Host=db` instead. |
| `ConnectionStrings__Quartz` | `Host=localhost` | Where Quartz's tables live - created only by `containers/hatch-db/pginit.sql`. Also `Host=db` under `DOTNET_ENVIRONMENT=Docker`. |
| `Database__HatchPoolSize` | `30` | The most connections one `api` process opens to `hatch`. Empty, zero or less falls back to the default. Left alone if the configured string already sets `Maximum Pool Size` (or `MaxPoolSize`) itself. |
| `Database__QuartzPoolSize` | `10` | The same, for the `quartz` string. |
| `Database__CommandTimeoutSeconds` | `30`; `0` (none) in migrate mode | How long a statement may run before Npgsql cancels it. Empty, zero or less falls back to the default - which is itself `0` in migrate mode, since a migration is expected to take longer than a request. Left alone if the string already sets `Command Timeout` (or `CommandTimeout`). |
| `Database__PoolWaitSeconds` | `5` | How long a request waits for a pooled connection before it is answered `503` ([sizing Postgres](#sizing-postgres)). Empty, zero or less falls back to the default. Left alone if the string already sets `Timeout`. |
| **Everything else** | | |
| `HATCH_MIGRATE` | unset | `1` makes the image run as the migration, which applies the schema and exits, instead of serving. |
| `DOTNET_ENVIRONMENT` | | Any value but `Development` writes the log as JSON lines instead of the human-formatted default. |

`Auth__SignInPath` cannot usefully move: the wall exempts the literal
`/auth` and everything under it (`Services/Auth/AuthGate.cs:166`), so pointing
it elsewhere opens a redirect loop. The wall's own off/on/required states are covered on
[the wall, off, on and required](the-wall-off-on-and-required).

A malformed `Hatch__AssetLinks` entry stops the API at start
(`Modules/AssetLinksValidation.cs`), unlike `Hatch__PublicBaseUrl`, which
degrades quietly. So does a `Hatch__SecretKey` or `Hatch__PreviousSecretKey`
that is set and is not base64 of exactly 32 bytes
(`Modules/SecretKeyValidation.cs`); the message names `HATCH_SECRET_KEY` or
`HATCH_PREVIOUS_SECRET_KEY` and never prints the value. A key left empty only
warns.

The migration creates `hatch`'s schema on every start; see
[backup, restore and replication](backup-restore-and-replication)
for backing it up, restoring it and running it on a replica.

## Sizing Postgres

The API opens two pools, `hatch` and `quartz`, each capped at
`Database__HatchPoolSize`/`Database__QuartzPoolSize` connections
(`Database__*` above). A request that cannot get one within
`Database__PoolWaitSeconds` is answered `503` rather than left to queue
forever, and a statement that runs longer than `Database__CommandTimeoutSeconds`
is cancelled on the server. None of this is enough on its own: the pools still
have to fit the Postgres they are opening against, and that is arithmetic an
operator does once, against their own numbers, not a figure this page can fix
for every install (`docs/ethos.md`).

**The connection sum.** Every replica opens its own pair of pools, so:

```
replicas x (Database__HatchPoolSize + Database__QuartzPoolSize) + reserve <= max_connections - superuser_reserved_connections
```

`max_connections` and `superuser_reserved_connections` are Postgres's own
settings (`100` and `3` on the shipped `hatch-db` image, which sets neither).
The `migrate` container never belongs in the reserve - it finishes before
`api` starts (`compose.yaml`'s `depends_on: condition:
service_completed_successfully`) and so never shares the server with it. The
reserve is whatever else does run alongside `api`: a `pg_dump` backup, and a
person's own `psql`. [Kubernetes](kubernetes) is one replica today, so the
shipped defaults (`30 + 10 = 40`) leave `97 - 40 = 57` of a default
`max_connections=100` for that reserve. A second replica would need `80`
instead, leaving `17` - at which point the pool sizes, not the reserve, are
what to turn down.

**Memory per backend.** Each connection is a Postgres backend process, and
each one holds working memory beyond Postgres's own `shared_buffers` - more
once it has carried a wide row or a big result set. Budget
`(replicas x (HatchPoolSize + QuartzPoolSize)) x the widest backend you have measured`,
within whatever limits the container or the pod. HA-2765 is the board this
story's figures came from: about 4,200 issues, 20,000 comments and 130,000
events (230 MB in all), where an ordinary backend held 7 to 14 MB and a wide
one up to 34 MB - one board's own numbers, not a figure to carry to another.

**Shared buffers against the size of the database.** `shared_buffers` (`128MB`
on the shipped image) is Postgres's own cache of table and index pages; a
database much larger than it will read more from disk, and a database that
fits inside it mostly will not. There is no formula here either - it is a
number to raise once the database has grown enough to measure the difference,
in `postgresql.conf` or a `command:` override on the `db` service.

**Measure it, on the running install:**

| Question | How |
|---|---|
| What is `max_connections`/`shared_buffers`/`superuser_reserved_connections` set to? | `SHOW max_connections;` and the same for the other two, in `psql`. |
| How many backends are open, and in what state? | `SELECT state, count(*) FROM pg_stat_activity GROUP BY state;` |
| How much memory does one backend hold? | The process's own PSS: `grep Pss /proc/<pid>/smaps_rollup` inside the `db` container, where `<pid>` is a backend's OS process id (`SELECT pid FROM pg_stat_activity`); or `SELECT * FROM pg_log_backend_memory_contexts(<pid>)` and read the next line it writes to the Postgres log. |
| How big is the database, and is `shared_buffers` keeping up? | `SELECT pg_size_pretty(pg_database_size(current_database()));` for the first; `SELECT blks_hit, blks_read FROM pg_stat_database WHERE datname = current_database();` for the second - a `blks_hit` far above `blks_read` is `shared_buffers` doing its job. |

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

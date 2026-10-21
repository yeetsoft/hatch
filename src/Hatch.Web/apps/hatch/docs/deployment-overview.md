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

---
title: Troubleshooting
section: Deploying and operating
order: 60
summary: Find the symptom, then the service log that shows it.
---

Find the symptom below, then read the log it names.

## If something is wrong

Start with what is running:

```
docker compose ps -a
```

Healthy is `db` healthy, `migrate` exited with 0, and `api` up. `migrate`
showing as exited is correct: that is a finished migration, not a crash.

Then read the log of the service the symptom points at:

```
docker compose logs <service>
```

`<service>` is `db`, `migrate`, `api` or `runner`. The `runner` service exists
only under its profile, so it is listed only when you started it with one. Add
`--since 10m` to see only the last ten minutes, and `-f` to follow the log as
it is written.

Outside `Development` the API logs JSON, one object per line, and Compose sets
`DOTNET_ENVIRONMENT=Docker` on `migrate` and `api`. So their logs read as JSON
lines, not as the plain text of a `dotnet run`.

## Docker is not running

- **`the system cannot find the file specified`, naming a pipe or a socket.**
  In full it is `open //./pipe/dockerDesktopLinuxEngine` on Windows, or
  `/var/run/docker.sock` on macOS.
- **`Cannot connect to the Docker daemon`, or `error during connect`.** The
  same cause, as another build of Docker words it.

Nothing about the stack failed: Docker Desktop is installed and not running.
Start it and wait for its dashboard to say **Engine running** — a fresh
install does not start itself, and the first start brings WSL2 up with it and
can take a couple of minutes.

```
docker version
```

It printing a **Server** block as well as a **Client** one is how you know
Docker is ready; then run the command that failed again. If the installer asked
for a sign-out or a reboot and did not get one, that is the other half of this:
the group membership it added is not in effect until then.

There is no service log to read here. The stack never started, so there is
nothing yet for `docker compose logs` to show.

## The board does not come up

- **`migrate` is exited.** Correct, and not the symptom. It runs once, applies
  the migrations and stops.
- **`api` is in `Created`.** `migrate` failed, and `api` is waiting for a
  `migrate` that never completed successfully. Read its log, fix what it names,
  and start the stack again:

  ```
  docker compose logs migrate
  docker compose up -d
  ```

  [Upgrading](upgrading#reading-its-log) says what a good `migrate` log looks
  like.
- **`api` is restarting.** It exits at start, for example because the schema
  is not there; it does not wait for one. Read why:

  ```
  docker compose logs api
  ```
- **`db` is not healthy.** Nothing after it starts. Read:

  ```
  docker compose logs db
  ```
- **`no matching manifest`, or `exec format error`.** The images do not suit
  this machine's processor.

The first start builds from source and takes several minutes, so a `docker
compose ps -a` that shows nothing up yet is not by itself a failure.
`docker compose up -d` is safe to repeat.

## The port is taken

Two ports are published.

- **The board, 8080 by default.** Move it with `HATCH_PORT`, in a `.env` file
  beside `compose.yaml`:

  ```
  HATCH_PORT=9090
  ```

  or in front of the command:

  ```
  HATCH_PORT=9090 docker compose up -d
  ```

  In PowerShell:

  ```
  $env:HATCH_PORT=9090; docker compose up -d
  ```

  Then point the runner at the new address:

  ```
  hatch config --origin <your-hatch-origin>
  ```
- **The database, 5432.** `port is already allocated`, or `bind` naming
  `5432`, means another Postgres on this computer already holds it. Stop that
  one. Unlike the board's port, this one cannot be moved: `compose.yaml` has no
  setting for it.

The log to read for either is the one that failed to start: `docker compose logs
api` for the board, `docker compose logs db` for the database.

## Signing in

- **The `migrate` log says "Google sign-in is not configured: set
  Auth__Google__ClientId and Auth__Google__ClientSecret"** (`FirstAdminNotice.cs:16`):
  nobody can sign in until Google is set up. Set `HATCH_GOOGLE_CLIENT_ID` and
  `HATCH_GOOGLE_CLIENT_SECRET`, then read `docker compose logs migrate`.
- **Google shows `redirect_uri_mismatch`:** `HATCH_GOOGLE_REDIRECT_URI` must equal
  the address registered with Google, character for character. See
  [Behind a reverse proxy](the-wall-off-on-and-required#behind-a-reverse-proxy).
- **Signed in, but nothing changes on a LAN address or in Safari:** the sign-in
  cookie is `Secure`, and a browser holds it back on a plain `http` address. Same
  link: [Behind a reverse proxy](the-wall-off-on-and-required#behind-a-reverse-proxy).
- **"An administrator has to approve your account"** (`auth/src/App.tsx:62`): the
  sign-in worked and the role is `pending`. An admin has to change it; see
  [People, roles and keys](people-roles-and-keys#roles).
- **Locked out:** see [Locked out](the-wall-off-on-and-required#locked-out).
- **Any refusal:** `docker compose logs api` shows a Warning `Auth refused
  {Reason} for {Host}{Path} from {ClientIp}` (`AuthGate.cs:304`). The reasons are
  `no_credential`, `unknown_grant` and `unknown_key` (`AuthGate.cs:28-32`).

## A 401 or a 403

- **A `401`.** The wall is on and no key was sent, or a key was sent and
  refused. Run `hatch config`.
- **A `403`.** A route or a rule behind it refuses — a key is never an
  administrator. `hatch` prints the API's own reason: a role-gate code, or a
  plain sentence naming the rule. Only `key_not_accepted` and
  `scope_mismatch` print as "the key is good and this route is not one it
  may take" - those two really are about a scope.

[401 and 403](people-roles-and-keys#401-and-403) has the full list, from the
other side.

The runner's own troubleshooting — no `claude` CLI on `PATH`, which branch is
the trunk, a tree that will not stash, one loop per checkout, a pass that
picks up nothing, losing the connection to origin — belongs to running the
loop, not to the board: see [When it will not
run](the-runner#when-it-will-not-run).

## The Runner page offers no downloads

- **This build published no runner binaries.** An image built before the
  publish step, or `dotnet run` from a checkout, has none. Build them
  yourself with `make publish-hatch` from the repository root, or rebuild the
  image.

## A psql shell

For looking at the database by hand:

```
docker compose exec db psql -U user -d hatch
```

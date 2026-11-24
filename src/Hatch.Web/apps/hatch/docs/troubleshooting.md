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

A runner started while the daemon was down prints
`hatch: docker not detected - the Docker daemon did not answer ...` and
takes no work that needs Docker: start Docker, then restart the loop, since
detection runs once.

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
  plain sentence naming the rule. Only `key_not_accepted` prints as "the key
  is good and this route is not one it may take" - it is about a key
  reaching a route that takes no key.

[401 and 403](people-roles-and-keys#401-and-403) has the full list, from the
other side.

The runner's own troubleshooting — no `claude` CLI on `PATH`, which branch is
the trunk, a tree that will not stash, one loop per checkout, a pass that
picks up nothing, losing the connection to origin — belongs to running the
loop, not to the board: see [When it will not
run](the-runner#when-it-will-not-run).

## Two loops on one machine

- **`the workspace <dir> is already served`.** Another loop holds that
  workspace: [When it will not run](the-runner#when-it-will-not-run). Give
  this checkout a workspace of its own with `hatch config --here --workspace
  <dir>`, or stop the other loop.
- **`this run will not clone`.** Not a fault, the default workspace is
  another loop's: [When it will not run](the-runner#when-it-will-not-run).
- **`hatch: --here writes this checkout's scripts/.env - run it inside
  one`.** The command ran outside a git checkout; `cd` into one. See [several
  runners on one machine](the-runner#several-runners-on-one-machine).
- **`hatch: <path>/scripts/.env is not ignored by this checkout's git - add
  .env to .gitignore before --here can write it`.** Add `.env` to the
  checkout's `.gitignore`; it is refused on purpose so a key cannot reach a
  tracked tree. See [several runners on one machine](the-runner#several-runners-on-one-machine).
- **`hatch: the workspace <dir> is already served by pid <n>, from
  <checkout>.`** Another live loop holds a named workspace; give this
  checkout its own with `hatch config --here --workspace <dir>`, or stop
  that loop. See [several runners on one machine](the-runner#several-runners-on-one-machine).

## After upgrading, my runner's tags are gone

- **The runner's tags or bounds read empty after an upgrade.** A runner's
  settings (tags, bounds, omissions) used to live on a row per checkout and
  now live on its key; the old rows are kept, marked adopted once carried
  over. Two things carry them. Reading the Runners page adopts an old row
  onto a key where its owner has exactly one live key and exactly one
  unadopted old row. And a loop's first heartbeat after the upgrade sends
  its old name, which adopts the old row of that name onto that key, unless
  the row belongs to a different person - so two old rows for one person are
  each carried by name once their loops upgrade.
- **Nothing was carried.** A person with two old rows and no loop sending
  its name, no row, no key or two keys is left alone, now and later, until
  the count on both sides is one. Set the tags and bounds by hand on the
  Runners page - tags are set only there: [the
  runner](the-runner#what-a-runner-advertises). A key that already carries
  settings keeps them; adoption fills only a key that has none.

## A ticket sits in Breakdown and no runner picks it up

- **The column runs on a model no live runner offers.** `fable` is the
  example here and the Breakdown column's default; any alias or pinned id
  behaves the same. `hatch queue` names the ticket and the model, in the
  board's own wording ([Models](the-runner#models)), and closes with *3
  issues wait for a runner that offers fable*. The attention panel's
  **Waiting on a runner** says the same, board-wide: [watching the
  loop](watching-the-loop#what-is-waiting-on-you).
- **Fix it from either end.** Start a runner whose account has the model, or
  put the playbook on a tier somebody offers - by a proposal, never an edit:
  [playbooks](playbooks#model-and-effort). A runner that finds out the hard
  way narrows the model itself, and the increment ends released `unservable`,
  which is not a stall: [when it will not
  run](the-runner#when-it-will-not-run).

## The runner says its agent is not starting

- **Two runs in a row ended before the agent printed anything.** The runner
  stops claiming, says *the agent is not starting* with the last line the CLI
  wrote and the time it will check again, and keeps heartbeating. Nothing is
  wrong with the tickets: each was released untouched.
- **Read what the CLI said.** The line is usually the cause - a login that
  expired, a binary that moved, a config it will not read. Run the agent by
  hand in the checkout and fix that; the next check passes by itself.
- **It checks on a back-off.** First after five minutes, then fifteen, then
  sixty, and sixty from there on. A check is one headless session on the
  agent's cheapest model asked for a single word, claiming no ticket; its
  cost is said on the terminal and joins the night's spend. When it starts
  and answers the terminal says *the agent starts again - taking tickets*.
- **Want it sooner.** Stop the loop and start it again by hand once the
  agent is fixed: a fresh start carries no pause. A restart the loop asks for
  itself carries the pause across to the same instant, so it does not skip
  the wait. How the wait works: [stalls](one-increment-and-the-loop#stalls).
- **How much it cost.** The closing tally says how many runs never started
  and how long the runner was paused for it.
- **It shows in the attention panel.** The runner's owner is told: read what the agent said, on the runner's page. A **Failing runners** row on the
  control names the runner and links to its page, and goes by itself when the
  runner is well again or has gone quiet.

## The runner says its agent needs signing in

- **One increment ended on a sign-in failure.** An expired OAuth session, a
  rejected key, a prompt to log in again. Unlike the agent not starting, one
  is enough - the runner pauses at once, rather than waiting for a second
  run to say the same thing twice. The ticket it was holding goes back to
  the board with one comment naming the sentence; nothing is wrong with the
  ticket, and it is neither a let-go nor a stall.
- **Sign in on this machine.** Run the agent by hand in the checkout and sign
  in again; the next check passes by itself.
- **It checks on the same back-off** as the agent not starting - five
  minutes, then fifteen, then sixty - with the same cheap headless session,
  and the terminal says *the agent starts again - taking tickets* once a
  check starts and answers. A later run that starts clears the pause too.
- **Want it sooner.** The same as the agent not starting: stop the loop and
  start it again by hand once signed in.
- **It shows in the attention panel.** Sign the agent in on that machine. A **Failing runners** row on the
  control names the runner and links to its page, and goes by itself when the
  runner is well again or has gone quiet.

## The runner says it cannot reach its forge

- **The runner's `gh` sign-in fails.** The runner reports it on every beat, with
  what `gh` said, and the board stops handing it a step that would push a branch
  and then be unable to open the pull request. Breakdown, analysis, conflict work
  and a project that lands on its trunk are still handed to it.
- **Sign `gh` in on that machine.** Run `gh auth login`, or give it a token the
  forge accepts. The runner reports again within a pass and the failure ends
  with no restart.
- **It shows in the attention panel.** A **Failing runners** row names the runner,
  says it cannot reach its forge since when, repeats what `gh` said and links to
  the runner's page. It is sent to the runner's owner, and to an admin where the
  runner belongs to nobody. The row goes by itself once `gh` works; nothing is
  pressed. If a ticket is listed under **Waiting on a runner** as *no live runner
  can open a pull request*, every live runner has this failure.

## A session asked whether it may commit unsigned

- **The machine could not sign, and the session was not told what to do.** The
  runner asks git whether it can sign before each increment, and a session that
  was started by a build older than that finds out at its first commit. Update
  the runner: from then on the answer is in the prompt, and the session does
  not ask.
- **See what git says here.** The runner prints one line at the start for any
  checkout where git is set to sign and cannot: what git said, and that commits
  will be unsigned where a project allows it. Fix it by hand - unlock the key,
  start the agent, point `gpg.program` at one that is there - and the check
  passes again within thirty minutes, with no restart.
- **A ticket was let go with a comment saying this runner cannot sign.** The
  project allows only signed commits, so the runner will not make an unsigned
  one. Fix the signing on this machine, or leave the ticket for a runner that
  can; a second one in a row is flagged stalled and waits like any other.
- **A ticket reads "allows only signed commits" in the queue, and the runner
  passes over it.** The project allows only signed commits, and the board
  folds the move for any runner that has not said its signing works. The
  sentence says which of three it is:
  - *"... build-box does not sign its commits"* - git is not set to sign on
    that machine. Set up signing there, or leave the ticket for a runner that
    signs.
  - *"... signing fails on build-box (gpg: signing failed: No pinentry)"* -
    git is set to sign and could not; the reason is what git said. Unlock the
    key, start the agent or point `gpg.program` at one that is there. The check
    passes again within thirty minutes, with no restart.
  - *"... build-box has not said whether it signs"* - the runner has not
    reported an answer, usually because it is an older build. Update it. A
    runner that is only heartbeating by hand never says.
  If **Waiting on a runner** lists the ticket as *no live runner signs its commits*,
  no runner anywhere can take it: fix one, or an owner can allow unsigned
  commits in the project dialog.
- **A runner whose signing fails is in the attention panel.** A **Failing
  runners** row says it cannot sign its commits, repeats what git said and
  links to the runner's page; it tells the owner to fix signing on that
  machine, or allow unsigned commits on the project. It goes by itself once
  signing works.
- **A ticket reads "ends in a pull request" in the queue, and the runner passes
  over it.** The runner's forge sign-in fails, so the board does not hand it a
  step that would push a branch and then not be able to open the pull request.
  The sentence ends with what `gh` said:
  *"HA-12 ends in a pull request - build-box cannot reach its forge (gh said: ...)"*.
  Sign `gh` in on that machine (`gh auth login`, or a token the forge accepts);
  the loop asks again within a pass and the row clears. Or leave the ticket for a
  runner that can. Breakdown, analysis, conflict work and a project that lands on
  its trunk are still handed to it.
- **A ticket in review reads "its failing build cannot be read".** The same fault,
  met on the review self-move: the runner reads the failing check's log through
  `gh`, and cannot. *"HA-12's failing build cannot be read by build-box - it
  cannot reach its forge (gh said: ...)"*. Fix `gh` there, or leave it for a
  runner that can. If **Waiting on a runner** lists a step as *no live runner can
  open a pull request*, every live runner has said its forge fails; a runner that
  has said nothing, such as an older build, is not counted as failing.
- **The session still asked.** The paragraph at the end of its branch section
  tells it what to do in each case, and none of them is to ask. A session that
  asked anyway is worth a note on the ticket, with the prompt it was given -
  `hatch work AER-12 --dry-run` prints it. See [what a session is
  told](what-a-session-is-told#the-branch) and [signing
  commits](the-runner#signing-commits).

## The runner exited 78

- **The log says git cannot sign commits here, and every project it serves
  allows only signed commits.** The runner could never commit anything it is
  allowed to work, so it stopped at once with nothing claimed. Exit code `78`
  is that and nothing else. The log names the projects (the first five), what
  git said in each checkout, and the two ways out.
- **Fix signing on this machine.** Unlock the key, start the agent, or point
  `gpg.program` at one that is there, then start the runner again. A container
  that restarts on failure keeps restarting until this is fixed.
- **Or have an owner allow unsigned commits.** It is a setting on each project.
  Where any project the runner serves allows them, the runner starts and works
  only that project's tickets.
- **It exited 78 after working for a while.** Signing worked at the start and
  stopped working, and the runner met a ticket it had to let go for that. The
  closing tally says so and no claim is held. The same two ways out apply.
- **It never runs again by itself.** A wrapper script and a downloaded build's
  launcher pass `78` through. See [signing
  commits](the-runner#signing-commits).

## The runner says the board has it paused

- **Three tickets in a row ended in error without spending a token.** The
  board paused that session for an hour. The runner prints *the board has this
  runner paused - nothing will be picked up until it is set running*, keeps
  heartbeating and takes nothing; a claim sent from that checkout is refused
  with a sentence saying the board paused it, until when, and what it saw.
  Nothing is wrong with the tickets.
- **Read why first.** The sentence names the last ticket. Fix what made the
  sessions fail: usually the agent, the checkout or the account.
- **Wait it out or lift it.** The pause ends by itself at the time named. A
  person can lift it on the Runners page.
- **A fresh start does not clear it.** Unlike the agent not starting, a loop
  started again in the same checkout inherits the board's pause until it
  passes. A session under the same key at another checkout is not paused.
  A ticket the session already held keeps its claim and is released as usual.
- **It shows in the attention panel.** Read why on the runner's page, where the pause can be lifted. A **Failing runners** row on the
  control names the runner and links to its page, and goes by itself when the
  runner is well again or has gone quiet.

## A session is still running in the checkout after the loop died

- **The loop was killed or its window closed mid-increment.** The agent it
  spawned does not go with it. Start `hatch go-to-work` in the same checkout:
  once it holds the lock it ends the leftover session and its process tree,
  releases the ticket's claim as `abandoned`, comments once on the ticket
  (with the resume command, when the session had said its id) and says so on
  the terminal: [a loop that died](one-increment-and-the-loop#a-loop-that-died).
  Nothing needs clearing by hand.
- **It said the record belongs to another board.** The checkout was last
  served by a loop pointed at a different Hatch. The record is left as it is;
  start the loop against that board, or delete the `hatch-session-*.json` file
  in the system's temporary directory yourself.
- **The session is still there afterwards.** Only the agent's own process
  tree is ended, and only where the pid still starts when the record says. A
  tool the agent had already orphaned, after the agent itself had exited, has
  no record to find it by - stop it yourself.
- **`hatch work` does not clean up.** It writes and clears its own record but
  never reads another's; use `go-to-work`.

## A ticket was not stall-marked because the session spent nothing

- **The session never got as far as the ticket.** The runner's work-log row for
  the claim shows no tokens and ended in error - an agent that failed to start
  properly, a login that lapsed, a model that refused. The board leaves the
  ticket's record alone: no stall mark, and the release is not counted
  against it, so it is never held for a person on that account.
- **The ticket is picked up again at once.** Nothing waits out a resume
  window. A runner that keeps failing this way will keep taking and dropping
  it, so read the runner's own comment on the ticket and fix that end: the
  runner's terminal and its work-log rows say what the session saw.
- **Two sessions on one claim count together.** The mark is left off only when
  every row since the claim was taken spent nothing. One row that spent tokens,
  or that did not end in error, and the stall is marked as usual.
- **How the stall mark works otherwise:** [when an increment does
  nothing](flow-wip-and-the-battery#when-an-increment-does-nothing).

## A session keeps getting paused by the board

- **Three releases in a row spent nothing.** The board stands the session
  down for an hour rather than let it keep taking and dropping tickets:
  [when the board pauses a
  session](the-runner#when-the-board-pauses-a-session).
- **Fix the cause first, not just the pause.** The usual causes are the same
  three as a session paused on its own agent - a signed-out agent, a full
  disk, or a bad or expired key - plus a model or binary that stopped
  resolving. Read the runner's terminal and its work-log rows for the
  session's claims; the board's own line on the paused session names the
  last ticket it dropped.
- **Lifting it without fixing the cause re-pauses it.** The Runners page's
  **Lift pause** button ends the hour early, but it does nothing about why
  three releases spent nothing - the same cause will fail the next three
  the same way, and the board pauses it again.
- **Want it sooner, once the cause is fixed.** Press **Lift pause** on the
  session's row. Its next heartbeat and its next claim run as normal.

## A runner keeps claiming one ticket

- **The goose opened a `claim-repeating` finding.** Its sentence names the
  runner, the ticket and how many times it was claimed, for example *"here:/one
  has claimed AER-12 7 times and it has not moved"*. At least five claims
  (`Hatch__GooseRepeatClaims`) in a row, none of them followed by a column
  change, a new commit on the pull request or its build, or a claim by another
  runner: [the check](the-goose#claim-repeating).
- **Read the releases first.** The evidence counts how many releases carried
  each outcome. `unstarted` or `abandoned` over and over points at the runner,
  not the ticket - an agent that does not start, or a loop that keeps dying;
  see [a session keeps getting paused by the
  board](#a-session-keeps-getting-paused-by-the-board). `dropped` or no outcome
  points at the ticket, which the session reads and cannot finish.
- **The evidence also says whether the ticket is held or stall-marked.** A
  held ticket is one the loop is meant to keep off; a stall mark says why the
  last increment stopped. Neither is set by this check, which changes nothing.
- **Then hold the ticket or answer it.** Hold it to stop the loop returning
  to it, or answer the question it is waiting on if it has one. A column
  change, or a new commit on its pull request or build, ends the streak and
  closes the finding by itself.

## A ticket stalls on a missing toolchain

- **The repository builds with tools the runner's machine lacks, and nothing
  said so.** Declare them in `.hatch/dependencies.json`: [what a repository
  needs](the-runner#what-a-repository-needs). From the next poll the board
  folds the project's development steps for every runner that lacks them, by
  name - *HA-12 needs dotnet 10 or newer (acme/repo's
  .hatch/dependencies.json) - this runner has dotnet 8* - and stops sending
  the ticket there.
- **A need the list cannot say is a runner tag.** See [what a runner
  advertises](the-runner#what-a-runner-advertises). A runner with a tool
  detection cannot see declares it with `HATCH_HAS`: [has](the-runner#has).

## The Runner page offers no downloads

- **This build published no runner binaries.** An image built before the
  publish step, or `dotnet run` from a checkout, has none. Build them
  yourself with `make publish-hatch` from the repository root, or rebuild the
  image.

## The runner says it could not move to a build

A downloaded `hatch` running a loop can be moved to a newer build. When it
cannot, it carries on with the build it has, says one line at the start of the
next restart, and asks again later: a minute, then two, doubling to an hour. Each
line reads `hatch: could not move to build <sha> - <why>; carrying on with the
build it has, asking again in <time>`. The night is never lost over it.

- **`the board answered from another build (mid-deploy?)`** The board was being
  upgraded while the build was fetched. Nothing to do: the next ask finds it done.
- **`the download's sha256 is not the one the board gave`** The bytes were damaged
  on the way, or a proxy changed them. It asks again on its own. If it keeps
  happening, look at what stands between this machine and the board.
- **`the new build did not report revision`** The fetched file did not say it is the
  build it was meant to be, or would not run on this machine. Check that the
  platform matches, and that nothing (antivirus, a read-only mount) is stopping it
  from starting.
- **`could not start` or `did not answer "version" within 60s`** The same, seen
  from the other side: the file arrived whole but would not run here.
- **`403 downloading`, `401 downloading`, a timeout, or a connection message** The
  board could not be reached with this runner's key. See [a 401 or a
  403](#a-401-or-a-403).
- **`installing <sha> - <message>`** The disk, not the board: it is full, or the
  per-user folder is not writable. Free space or fix the folder's permissions; the
  builds live in `versions/` there.
- **`kept <n> old build(s) it could not remove; tried again at the next change of
  build`** The launcher removes the builds it no longer needs, and this one was in
  use, held open by a scanner or another process, or on a disk that refused. It
  says so once and tries again by itself; nothing is lost and no night is delayed.
  If it never clears, delete the folders under `versions/` that no process is
  running by hand.

## The runner went back to the build it had

When a new build ends before the board has answered its first beat, the launcher
puts the night's totals back as they were, starts the build it had, and never asks
for that build again until the launcher itself is started again. The line reads
`hatch: build <sha> ended with exit <code> before the board answered it; carrying
on with the build it had` (or `could not be started`). A build that ended after the
board answered it is a normal end, and ends the loop as it always has.

- Read the log for what the new build said as it ended. It is a build that does not
  work, and an upgrade is the way out: the board's next build is asked for as usual.
- Deleting `versions/` in the per-user folder is safe at any time. The next start
  runs the downloaded file itself.

## A secret will not save

Saving a Claude token, Codex API key or Grok Build API key on the **Settings**
page fails. The page says *Secrets cannot be saved until HATCH_SECRET_KEY is
set.* Called from the API, the refusal is `secrets cannot be saved until
HATCH_SECRET_KEY is set; set it and restart, or remove the secret from this
request`, and `GET /api/settings` says `canSaveSecrets: false`.

Hatch seals every secret it stores under a key, and this Hatch has none. The API
said so once when it started, in a line you can still read:

```
docker compose logs api
```

Look for `HATCH_SECRET_KEY is not set, so secrets cannot be saved until one is
set.` Two causes are usual:

- **No key was ever set.** Make one and set it: [Set the secret
  key](getting-started#set-the-secret-key).
- **The `.env` is not beside `compose.yaml`,** or the variable is spelled some
  other way, so Compose never read it. `HATCH_SECRET_KEY` is the whole name.

To fix it, put the key in the `.env` and run `docker compose up -d`. A `restart`
does not do it, because it starts the same container with the environment it
already had. Reload the Settings page and save again. The line in the log stops
once a key is set. Nothing stored is lost while there is no key.

If the API will not start at all after you set one, the value is not base64 of
exactly 32 bytes. The message names `HATCH_SECRET_KEY` and never prints it. Make
a fresh one with `openssl rand -base64 32`. [The secret key](the-secret-key) is
the whole page about it.

## The Settings page says a secret cannot be opened

Beside the Claude token, Codex API key or Grok Build API key on the
**Settings** page, you see: *This is saved, but it cannot be opened. Enter it
again, or remove it.* Something is stored under that setting, and this Hatch
cannot read it. Everything that uses the secret treats it as not set, so a runner
that needs it will say it has no credential.

It comes about three ways:

- **The key is wrong.** `HATCH_SECRET_KEY` is set, but it is not the key the
  value was saved under. This is usual after the key was changed without
  `HATCH_PREVIOUS_SECRET_KEY` keeping the old one for the change-over: see
  [changing the key](the-secret-key#changing-the-key). The log line for each
  secret names the key it was sealed under (`It names key <id>`).
- **The key is lost.** `HATCH_SECRET_KEY` is not set at all now, for instance in
  a `.env` that was not carried over. Set it back to the key the value was saved
  under and the sentence goes. See [Environment variables](environment-variables).
- **The stored text is damaged.** The value was edited in the database, cut short,
  copied from another setting's row, or written by a newer Hatch under a scheme
  this one does not know.

The API log says which setting, once until the stored value changes. It never
prints the value:

```
docker compose logs api
```

Look for `Site setting ClaudeSubscriptionToken is stored but cannot be opened`.

To fix it, either restore the right key and reload the page, or give up on the old
value: type the secret again and press Save, which replaces it, or press Remove to
clear it. Saving needs `HATCH_SECRET_KEY` to be set.

## A busy database

Every dispatch scan reads the whole board, so a database that is busier than
the board's size explains is usually being scanned too often. Hatch counts its
scans and says who asked for each: one log line for a request that ran a scan,
and a meter for anything that reads numbers.

- **Find the line.** The API writes one Information line for each request, and
  each goose sweep, that ran a scan or built a priority tree. A request that did
  neither writes nothing.

  ```
  docker compose logs api | grep "Dispatch reads on"
  ```

  For example: *Dispatch reads on api/work/next: 1 scans, 2 tree builds, 30
  statements, 33 ms in scans, 49 ms in all, by caller next=1*.

- **Read the fields.**
  - `Route` is the route pattern of the request, never its path, so an issue key
    does not appear. A goose sweep has no route and says `goose`.
  - `Scans` is how many scans the request started, scans that a scan started
    for its peers included.
  - `TreeBuilds` is how many times the request built the priority tree. Each
    scan builds one, and some routes build one of their own beside it.
  - `Statements` is every database command the request sent, scan or not. It
    counts commands sent by Hatch's own contexts; it is the driver's count, not
    the server's.
  - `ScanMs` is the time spent in scans. A scan that runs peer scans is timed
    once, so nesting is not counted twice.
  - `RequestMs` is the time of the whole request or sweep, for comparison.
  - `Callers` is the scans started by each caller, as `name=count`.

- **Name the callers.** A scan is always asked for by one of these:
  - `next`: a runner asking for its next step, or a named step.
  - `queue`: the queue, and the takers it lists for each step.
  - `attention`: the Attention page.
  - `claim-heartbeat`: a runner's heartbeat asking whether its claim should
    yield to emergency work. It scans only while an open emergency issue
    exists, so on a board with none this counter stays at zero.
  - `goose`: the goose's sweep.

  One caller far above the rest is where to look. A queue asked for with takers, on a
  board with two live runners, starts four: its own scan, the takers' scan of the
  whole board, and one scan for each live runner.

- **Read the meter.** The same numbers are published on the `Hatch.Dispatch`
  meter, for whatever exporter the installation runs. Hatch ships no exporter and
  no `/metrics` route. To look at it by hand, install
  [`dotnet-counters`](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-counters)
  where the API runs and point it at the process:

  ```
  dotnet-counters monitor --counters Hatch.Dispatch --process-id <pid>
  ```

  The instruments are `hatch.dispatch.scans`, `hatch.dispatch.scan.duration`
  (milliseconds), `hatch.dispatch.scan.candidates`,
  `hatch.dispatch.scan.statements` and `hatch.dispatch.tree_builds`. Each is
  tagged with `caller`; a tree built outside a scan carries the route pattern
  instead. The `api` image does not carry `dotnet-counters`, so reading the meter
  from inside the container needs the tool added to it.

- **Expect no scan from the merge-train poll.** Every runner polls
  `GET /work/review` every few seconds, so it is built to cost the same on a
  board of fifty issues and on one of two thousand. It runs no scan and builds no
  priority tree: with nothing in review or merging for the caller it sends two
  statements, and with rows there it reads only those rows and the issues above
  them. A `Dispatch reads on api/work/review` line, or any `TreeBuilds` count on
  that route, means the poll has started reading the whole board again.

## A psql shell

For looking at the database by hand:

```
docker compose exec db psql -U user -d hatch
```

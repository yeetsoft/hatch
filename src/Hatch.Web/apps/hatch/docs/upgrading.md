---
title: Upgrading
section: Deploying and operating
order: 40
summary: Pulling new source or new images, what the migration does on the way up, how to tell from its log that it worked, and how to roll back.
---

An upgrade is new images plus a migration. The images are replaced; the data
stays where it was, in its volume, because a volume is not part of an image.
Take a backup first — [backup, restore and replication](backup-restore-and-replication)
has the line — since the one thing an upgrade can do that a restart cannot is
change the shape of the data.

## Building from source

```
git pull
docker compose up -d
```

`git pull` brings the source up to date, and `up -d` rebuilds the images from
it: `db`, `migrate`, `api` and `runner` each say `pull_policy: build` in
`compose.yaml`, so `up` never reuses an image built from older source. With
nothing changed the rebuild is all cache and takes seconds. A `down` between
the two is not needed and does no harm.

If you run [the container runner](the-container-runner), add `--profile runner`
to the second line. Why, and what it does and does not restart, is under
[Upgrading it](the-container-runner#upgrading-it).

## From the published images

With the `compose.override.yaml` from
[Building it or pulling it](deployment-overview#building-it-or-pulling-it),
fetch the new images and start them:

```
docker compose pull
docker compose up -d
```

A sha tag is a version you chose: it names one build and stays that build.
`latest` is whatever the trunk last built, so it moves under you each time you
`pull`.

## What migrate does

`migrate` is the `hatch-api` image started with `HATCH_MIGRATE=1`. Instead of
serving, it does the following on every start (`Program.cs`), in this order:

1. Applies the core schema's migrations.
2. Applies each module context's migrations, one after another, logging
   `Migrating module context {Context}` for each.
3. Seeds the device mappings.
4. Applies the Home Assistant connection, if one is configured.
5. Says what the wall needs: whether an Administrator has to be made, and how.

Then it exits. `api` is gated on it: `compose.yaml` makes `api` depend on
`migrate` with `condition: service_completed_successfully`, so `api` starts
only after `migrate` exits 0. With nothing to apply, `migrate` changes nothing
and says the same things.

## Reading its log

```
docker compose logs migrate
```

These lines mean it worked:

- `Migrating module context …`, once for each module context.
- `No Home Assistant connection is configured; none applied.` or
  `Applying the Home Assistant connection at …`, whichever is true of your
  install.
- One line about the wall, and which one depends on how the wall is set:
  - Wall off: `Auth is disabled, so there is no wall to get through and no Administrator is needed.`
  - Wall on and nobody is an Administrator yet, a warning: `No Administrator exists yet. The next person to sign in with Google becomes the Administrator.`
    If Google sign-in is not configured it goes on: ` Google sign-in is not configured: set Auth__Google__ClientId and Auth__Google__ClientSecret.`
  - Wall on and an Administrator already exists: **no line at all.** That
    silence is the all-clear, not a failure.

Migrations themselves are applied silently. Entity Framework's own log is set
to `Warning` in `appsettings.json` and the Docker environment does not lower
it, so there is no line for each migration applied, and a page of output with
none in it is normal. The `Migrating module context` lines and the exit code
are the evidence.

Healthy means `docker compose ps -a` shows `migrate` as exited with 0. That
`migrate` is exited at all is correct — see
[the board does not come up](troubleshooting#the-board-does-not-come-up).

Anything else, an exception in the log or a non-zero exit, means `api` never
starts. It does not restart in a loop; it sits in `Created`, because it is
waiting for a `migrate` that never completed. Fix what the log names and run
`docker compose up -d` again. The [troubleshooting](troubleshooting) page has the
failures worth knowing by name.

## Rolling back

Images roll back; data does not.

To roll the images back, pin the previous sha tag in the override (published
images) or `git checkout` the previous commit (source), then:

```
docker compose up -d
```

`migrate` only ever applies migrations; it never reverses one. So the older API
meets the newer schema. That is usually fine when the upgrade added a column
the older code never reads, and not fine when it renamed or dropped one the
older code still does.

If the older API fails against the newer schema, put the data back too:
restore the backup taken before the upgrade, as described under
[Restoring](backup-restore-and-replication#restoring).

## The runner after an upgrade

Download the runner again from **Agents → Get the runner**. That page prints
`Built from` and the revision, which tells you whether the one on your `PATH` is
the one this board expects.

A loop running under Hatch's own supervisor restarts itself as a newer build
when its source changes ([Restarts](one-increment-and-the-loop#restarts)). The
container runner never does; a new build reaches it only through the commands
above.

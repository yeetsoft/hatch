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
2. Applies the module context's migrations (Hatch's own, in the `hatch`
   schema), logging `Migrating module context {Context}`.
3. Says what the wall needs: whether an Administrator has to be made, and how.

Then it exits. `api` is gated on it: `compose.yaml` makes `api` depend on
`migrate` with `condition: service_completed_successfully`, so `api` starts
only after `migrate` exits 0. With nothing to apply, `migrate` changes nothing
and says the same things.

## Reading its log

```
docker compose logs migrate
```

These lines mean it worked:

- `Migrating module context …`, once.
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

**Sealed secrets do not roll back.** An older build does not know the sealed
form. It reads a sealed value as not set, so a runner that needs it says it has
no credential. The way back is to enter the
secrets again on the older build, or to restore the backup taken before the
upgrade, which still holds them as the older build wrote them. See [Secrets are
sealed from this version](#secrets-are-sealed-from-this-version).

## What an upgrade drops

Hatch no longer carries the Gather, Photos, Quill and Storage apps, or the
house-automation half it started as. The migrations that remove them drop,
with everything in them:

- the four Postgres schemas `gather`, `photos`, `quill` and `storage`;
- the `ImmichBaseUrl` and `ImmichApiKey` rows in the site settings, which only
  Photos read;
- 21 tables: devices, their channels and camera connections (`Devices`,
  `DeviceChannels`, `CameraConnections`); zones (`Zones`, `ZoneConfigs`);
  readings and state history (`EnvironmentReadings`, `Measurements`,
  `StateChanges`); routines (`Routines`, `RoutineActions`); panels (`Panels`,
  `PanelItems`, `PanelControlBindings`); the family calendar (
  `CalendarAccounts`, `Calendars`, `CalendarEvents`); weather and air quality
  (`WeatherAlerts`, `AirQualitySamples`); and the command ledger (`Commands`,
  `ControlDecisions`, `ControlOverrides`);
- the site settings only those tables' features ever read: `Latitude`,
  `Longitude`, `WeatherEntity`, `ComfortToleranceF`, `DefaultComfortLowF`,
  `DefaultComfortHighF`, `OverrideBackoffMinutes`, `HomeAssistantHost`,
  `HomeAssistantPort`, `HomeAssistantToken`, `MediaLibraryBaseUrl`,
  `KioskWifiSsid`, `KioskWifiPassword`, `KioskWifiSecurityType`,
  `CalendarAgendaDays`, `WeatherAlertProvider`, `AirQualityProvider`,
  `WeatherAlertContact`, `AirQualityAlertThresholdAqi` and
  `HazardMaxSeverityAgeHours`.

An install that never ran those apps, or never used the home-automation half, has none
of this, and the migrations find nothing to drop. One that did loses their
data the moment `migrate` runs, and rolling the images back does not bring it
back: the older migration history is still there, but the tables are not. If
anything in them is worth keeping, dump it first with
[the by-hand `pg_dump`](backup-restore-and-replication#doing-it-by-hand-instead),
before you pull.

## Secrets are sealed from this version

Hatch seals the secrets it stores, under a key you set: `HATCH_SECRET_KEY`. An
install that already holds a Claude token, a Codex API key or a Grok Build API
key meets this on the way up. [The secret key](the-secret-key) is the page about
the key itself.

1. **Take the backup first.** It is the one copy that still holds your secrets
   as the older build wrote them, which is what [Rolling
   back](#rolling-back) needs.
2. **Upgrade as usual.** With no key set, nothing breaks. The stored secrets
   keep opening, a runner keeps getting its credential, and the API warns once
   at start: `HATCH_SECRET_KEY is not set, so secrets cannot be saved until one
   is set.`
3. **Saving a new secret is refused until a key is set.** The Settings page says
   *Secrets cannot be saved until HATCH_SECRET_KEY is set.* Making a key and
   where it goes is [Set the secret key](getting-started#set-the-secret-key).
4. **The first start with a key seals everything already stored.** Set the key
   and run `docker compose up -d`, then read the api's log:

   ```
   docker compose logs api
   ```

   It says, once:

   ```
   Resealed 2 stored secrets under key <id>; 0 could not be opened.
   ```

   `<id>` names the key and is not the key. A secret it could not open is left
   as it is, and has a line of its own naming its row. That is
   [a secret that cannot be opened](troubleshooting#the-settings-page-says-a-secret-cannot-be-opened),
   not a failed upgrade.

The reseal runs in the api when it serves, never in `migrate`, and runs on every
start: with everything already sealed it has nothing to do. If it fails, the
failure is logged, the API still starts, and the next start tries again.

**Take a backup after that start,** too. A dump taken before it holds the
secrets in their older form, which anyone with the file can read.

## Settings renamed from `Apps__*`

The install's public address and its Android asset links are Hatch's own
settings now. The `HATCH_PUBLIC_URL`, `HATCH_ANDROID_PACKAGE` and
`HATCH_ANDROID_CERT_FINGERPRINT` variables are unchanged, and `compose.yaml`
passes them on to the new names. If you set the settings by hand rather than
through those variables, rename them:

| Was | Is |
| --- | --- |
| `Apps__PublicBaseUrl` | `Hatch__PublicBaseUrl` |
| `Apps__AssetLinks__<n>__PackageName` | `Hatch__AssetLinks__<n>__PackageName` |
| `Apps__AssetLinks__<n>__Sha256CertFingerprints__<m>` | `Hatch__AssetLinks__<n>__Sha256CertFingerprints__<m>` |

The old names are read by nothing, so a value left under one is ignored.

The kiosk-era `/api/app-version` and `/api/apps/config` routes are gone; a
board never called either.

## The runner after an upgrade

Download the runner again from **Agents → Get the runner**. That page prints
`Built from` and the revision, which tells you whether the one on your `PATH` is
the one this board expects.

A loop running under Hatch's own supervisor restarts itself as a newer build
when its source changes ([Restarts](one-increment-and-the-loop#restarts)). The
container runner never does; a new build reaches it only through the commands
above.

A downloaded runner follows its board by itself. When the board is deployed on
a new build, the runner hears it on its next heartbeat, fetches that build from
the board, checks it, and comes back as it at the top of a pass, never while it
holds a ticket. The **Behind** badge goes with the old process. A runner that
was downloaded before this existed cannot do that yet: it needs one last manual
download, after which it keeps up on its own. A launcher at version 3 or more
also keeps its own file current, from the builds it keeps
([Getting it](the-runner#getting-it)), so the one on your `PATH` is not downloaded
again; a launcher at version 2 or less needs one last download to get that. A runner started with
`--no-restart` or `--once`, or one run from a checkout by `scripts/hatch.sh` or
`scripts\hatch.ps1`, never changes build this way.

## Links an older `hatch` wrote

A pull request, a preemption comment or a `hatch work` read-out names an issue
as `<origin>/issues/AER-12`, with no `/apps/hatch` in front. A `hatch` binary
built before that change keeps writing the old `<origin>/apps/hatch/issues/AER-12`
form until its operator upgrades it. Nothing breaks meanwhile: the root answers
the old path with a permanent redirect to the same path at the root, and a pull
request whose first line still carries the old form is read the same way as the
new one, for the same key and instance.

## The old `/api/hatch` prefix

Every route moved from `/api/hatch/<name>` to `/api/<name>`. The old prefix
is still served, in place: a call to `/api/hatch/<name>` is answered exactly as
`/api/<name>` would answer it - same method, body, headers and query string, and
no redirect in between, so an API key travels with it. An API key, a script, or
a `hatch` binary built before the change keeps working through it; upgrade the
binary when convenient, and call `/api/<name>` from anything new.

The other direction does not work: a **new** `hatch` against a server that has
not been upgraded asks for `/api/<name>` and gets a `404`. Upgrade the server
first, then the binaries. A proxy that routes by the `/api/hatch` prefix needs
the same move.

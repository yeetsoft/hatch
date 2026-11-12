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

## Links an older `hatch` wrote

A pull request, a preemption comment or a `hatch work` read-out names an issue
as `<origin>/issues/AER-12`, with no `/apps/hatch` in front. A `hatch` binary
built before that change keeps writing the old `<origin>/apps/hatch/issues/AER-12`
form until its operator upgrades it. Nothing breaks meanwhile: the root answers
the old path with a permanent redirect to the same path at the root, and a pull
request whose first line still carries the old form is read the same way as the
new one, for the same key and instance.

---
title: Backup, restore and replication
section: Deploying and operating
order: 50
summary: Where a Hatch keeps its data, how to dump it every night and restore it, and what replication means for it.
---

Everything Hatch knows is in Postgres, so a dump is the backup and restoring is
reading it back. Replication is a question for Postgres, not for Hatch; the
last sections say what to tell Hatch once you have answered it.

## Where the data is, and how to back it up

Everything Hatch knows — every issue, comment, event and playbook — is in
Postgres, inside a named volume called `hatch-local_pgdata`. Nothing lives in
the images and nothing lives in a file on your desktop, which is the whole
reason `down` is safe and `down -v` is not.

There are two databases in that volume. **`hatch`** holds the data: issues,
comments, events, playbooks, people and keys. **`quartz`** is the scheduler's
store. The db image creates it (`containers/hatch-db/pginit.sql`) and the API
re-registers every job at each start (`Jobs/_Init.cs`, `WireUpJobs`), so it is
rebuilt rather than backed up, and a backup that leaves it out has left out
nothing you would miss.

The container runner's `workspace` volume is not data and is not part of this:
it is clones of public history, reproducible from the remotes the board
already names, and losing it costs a re-clone rather than anything a backup
needs to answer for.

**Do not back it up by copying the volume.** A file-level copy of a database
directory that is being written to has no consistency guarantee, so what you
would be keeping is a file that restores *sometimes*. Ask Postgres for the dump
instead — it is one command, the stack stays up, and what comes back is a text
file you can read.

```
docker compose exec -T db pg_dump -U user --clean --if-exists hatch > hatch-backup.sql
```

That is a board's worth of history in well under a megabyte. Put it wherever
you already put things you would be sad to lose.

## Restoring

Restoring is the same in reverse, with the API stopped for the length of it —
the dump drops every table before it recreates them, and an API holding
connections to those tables is an API answering a page out of a schema that is
being replaced underneath it.

macOS / Linux:

```
docker compose stop api
docker compose exec -T db psql -U user -d hatch -v ON_ERROR_STOP=1 < hatch-backup.sql
docker compose start api
```

PowerShell, which has no `<` input redirection at all:

```
docker compose stop api
Get-Content hatch-backup.sql | docker compose exec -T db psql -U user -d hatch -v ON_ERROR_STOP=1
docker compose start api
```

`ON_ERROR_STOP=1` is what turns a half-restore into a refusal. Without it
`psql` reports each failure and carries on to the next statement, and you find
out which half arrived by using the board.

The backup line's `>` works in either shell, as long as the PowerShell is 7 or
newer — 5.1 writes UTF-16 there, which `psql` will not read back.

## Every night

A backup you have to remember to take is one you will not have. The line is the
same dump, run from the directory that holds `compose.yaml` and writing a file
named for the day. These keep the files in `hatch-backups` in your home
directory, outside the checkout, so no commit can pick them up; create that
directory first.

macOS / Linux — `crontab -e`, and add this line, with the path to your checkout
in place of `<the checkout>`. Cron treats a bare `%` as a newline, which is why
the date format escapes it:

```
0 3 * * * cd <the checkout> && docker compose exec -T db pg_dump -U user --clean --if-exists hatch > $HOME/hatch-backups/hatch-$(date +\%F).sql
```

Windows — from a PowerShell 7 prompt, register a task that runs `pwsh` in the
checkout. It has to be 7, for the reason given under [Restoring](#restoring):
the task's own `pwsh` is the one that writes the file. The single quotes keep
the date from being worked out now, when the task is registered, instead of
each night:

```
$run = '-NoProfile -Command "docker compose exec -T db pg_dump -U user --clean --if-exists hatch > $HOME\hatch-backups\hatch-$(Get-Date -Format yyyy-MM-dd).sql"'
$action = New-ScheduledTaskAction -Execute pwsh -Argument $run -WorkingDirectory '<the checkout>'
Register-ScheduledTask -TaskName 'Hatch backup' -Action $action -Trigger (New-ScheduledTaskTrigger -Daily -At 3am)
```

Run each line once by hand before you trust it — the `docker compose` line on
its own from the checkout, then the task itself (`Start-ScheduledTask -TaskName
'Hatch backup'` on Windows) — and look for the file. A schedule that has never
run has not been shown to work.

How many nights to keep, and a copy somewhere other than this machine, are
yours to decide; Hatch ships no backup container and does not prune anything.

## Replication

A **streaming replica** is a second Postgres that follows the first. It is a
failover target, not a backup: a delete replicates as faithfully as everything
else, so the night a bad query empties a table is the night both copies are
empty. Keep the dump either way.

A **managed Postgres with point-in-time recovery** covers what the dump cannot:
rewinding to a minute before the mistake rather than to last night. Which
provider, and how it is billed, is not something Hatch has a view on.

Hatch need not know it is replicated. To point it at either, set
`ConnectionStrings__Hatch` and `ConnectionStrings__Quartz` on **both** `migrate`
and `api` — in a `compose.override.yaml` `environment:` block, or from a cluster
Secret ([what the API reads](deployment-overview#what-the-api-reads) has the
variables, and [Kubernetes](kubernetes), the cluster side):

```
services:
  migrate:
    environment:
      ConnectionStrings__Hatch: <connection string for the hatch database>
      ConnectionStrings__Quartz: <connection string for the quartz database>
  api:
    environment:
      ConnectionStrings__Hatch: <connection string for the hatch database>
      ConnectionStrings__Quartz: <connection string for the quartz database>
```

Create the `quartz` database and its tables there first: the db image does it
for the bundled Postgres, and nothing does it for yours. The part of
`pginit.sql` after `\c quartz` is the script, and it is safe to run twice. The
compose `db` service still starts, because `migrate` waits on it, but holds
nothing you care about once the connection strings point elsewhere.

## A restore drill

A dump you have never restored is a hope. Restore it into a scratch database in
the same db container, count the same table in both, and throw the scratch one
away. The live `hatch` database is not touched.

macOS / Linux:

```
docker compose exec db createdb -U user hatch_drill
docker compose exec -T db psql -U user -d hatch_drill -v ON_ERROR_STOP=1 < hatch-backup.sql
docker compose exec db psql -U user -d hatch_drill -c 'SELECT count(*) FROM "hatch"."Issues"'
docker compose exec db psql -U user -d hatch -c 'SELECT count(*) FROM "hatch"."Issues"'
docker compose exec db dropdb -U user hatch_drill
```

PowerShell, where the restore needs `Get-Content` and the query goes in on
standard input, so no shell has to pass its double quotes through to a native
command:

```
docker compose exec db createdb -U user hatch_drill
Get-Content hatch-backup.sql | docker compose exec -T db psql -U user -d hatch_drill -v ON_ERROR_STOP=1
'SELECT count(*) FROM "hatch"."Issues"' | docker compose exec -T db psql -U user -d hatch_drill
'SELECT count(*) FROM "hatch"."Issues"' | docker compose exec -T db psql -U user -d hatch
docker compose exec db dropdb -U user hatch_drill
```

The table is quoted because the schema and the table are both case-sensitive
names: `"hatch"."Issues"`. The two counts should match, give or take whatever was
written since the dump. Run the drill once the nightly line is set up, and
again after any change to the stack.

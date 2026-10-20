---
title: Where the data is, and how to back it up
section: Deploying and operating
order: 30
summary: Where a Hatch keeps its data, and how to back it up and restore it safely.
---

Everything Hatch knows — every issue, comment, event and playbook — is in one
Postgres database called `hatch`, inside a named volume called
`hatch-local_pgdata`. Nothing lives in the images and nothing lives in a file
on your desktop, which is the whole reason `down` is safe and `down -v` is not.

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

Restoring is the same in reverse, with the API stopped for the length of it —
the dump drops every table before it recreates them, and an API holding
connections to those tables is an API answering a page out of a schema that is
being replaced underneath it:

```
docker compose stop api
docker compose exec -T db psql -U user -d hatch -v ON_ERROR_STOP=1 < hatch-backup.sql
docker compose start api
```

`ON_ERROR_STOP=1` is what turns a half-restore into a refusal. Without it
`psql` reports each failure and carries on to the next statement, and you find
out which half arrived by using the board.

**In PowerShell, the restore line is different**, because PowerShell has no
`<` input redirection at all:

```
Get-Content hatch-backup.sql | docker compose exec -T db psql -U user -d hatch -v ON_ERROR_STOP=1
```

The backup line's `>` works in either shell, as long as the PowerShell is 7 or
newer — 5.1 writes UTF-16 there, which `psql` will not read back.

---
title: The one line
section: Start here
order: 30
summary: The one command that starts a Hatch, and how to move it off its default port.
---

From the repository root:

```
docker compose up -d
```

Then open **http://localhost:8080/apps/hatch/**. Compose says nothing about the
address once it has gone to the background, so that part is on this page rather
than in your terminal.

The same line in both PowerShells and in a macOS terminal, character for
character.

The first run takes a few minutes — it builds the image, runs the migration to
completion, and only then starts the API.

Port 8080 is only the default. If something on your machine already has it,
`HATCH_PORT` moves it — and that is one of the two places on this page
where the two shells differ (the other is the backup and restore pair, further
down):

macOS / Linux:

```
HATCH_PORT=9090 docker compose up -d
```

PowerShell:

```
$env:HATCH_PORT=9090; docker compose up -d
```

Wherever this page writes `<your-hatch-origin>` below, it means whatever that
address turned out to be — `http://localhost:8080` unless you moved it.

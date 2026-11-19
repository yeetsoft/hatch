---
title: The goose
section: Running agents
order: 55
summary: The server's own sweep of the board, every minute, and how to read the record it keeps.
---

The goose is Hatch's watcher. Every minute the server looks over the board by
itself, with no runner working and no browser open, and writes down that it
did. When the goose is running, the record grows or its newest row's last
sweep stays recent. When it is not, nothing else on the board will say so.

## What a sweep is

A sweep asks each of the goose's checks two things: what did you look at, and
what did you conclude? Each answer is a line of plain text. The checks run in a
fixed order, one after another.

A check that fails does not stop the sweep. Its entry says it failed and
carries the error text, and the checks after it still run.

A fresh install has no checks yet, so a sweep records that it ran and nothing
more. That is still the proof the watcher is alive.

Only one sweep runs at a time, even with several servers on one database. A
sweep that starts while another is running writes nothing. A sweep still
running when the next is due means the next one is skipped, not queued.

## How to read a run

The record is a list of **runs**, not a list of sweeps. A sweep that concluded
exactly what the one before it did is folded into that run instead of adding a
row, so a quiet board is one row however long it stays quiet. Each run shows:

- **First ran** and **last ran**: when its first and latest sweeps started.
  A run whose last sweep is recent is the goose still running now.
- **Sweeps**: how many sweeps it holds. Sixty in an hour is the goose keeping
  its pace.
- **Longest**: the slowest of those sweeps, in milliseconds.

Open a run to see its entries: one per check, in the order they ran, with what
the check looked at, what it concluded, and whether it failed. The entries are
the latest sweep's. What a check looked at can change between folded sweeps
(three issues, then four); only a different conclusion, or a check that starts
or stops failing, starts a new run. How long a sweep took never does.

A new row, then, means something changed. The newest row's last-ran time is
how fresh the watcher is.

## Settings

Both are server settings, set in the environment like any other
([Deployment overview](deployment-overview)):

| Variable | Default | Meaning |
|---|---|---|
| `Hatch__GooseIntervalSeconds` | `60` | Seconds between sweeps. Zero or less falls back to `60`. |
| `Hatch__GooseKeepDays` | `7` | Days a run is kept after its last sweep. `0` keeps everything. Less than that falls back to `7`. |

Each sweep also deletes the runs whose last sweep is older than the keep
window.

## Reading it from outside

`GET /api/goose/runs` lists runs, newest first, without the per-check detail,
and takes a `limit`. `GET /api/goose/runs/{id}` answers one run with its
entries. An API key may call both. A run holds what was seen in every project,
so a caller who may see only some projects gets an empty list and reads every
run as not found. See the [API reference](api-reference).

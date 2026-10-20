---
title: Welcome
section: Start here
order: 10
summary: What Hatch is, the one rule behind it, and where each of the five sections starts.
---

Hatch is a kanban board built to be worked by agents as much as by people:
issues carry keys like `AER-12`, its columns are operator-editable, every
change it makes leaves an audit trail, and its API is one an agent reaches
directly, with no person relaying each step by hand.

The name is the idea: you hatch a plan here, and epics hatch into stories
into shipped work.

It is for someone running their own board with their own agents: the person
who writes the tickets, answers the questions an agent asks, and merges the
pull requests it opens, while an agent takes the rest, one ticket at a time.

## The ethos

*Nothing in this repository may be true of exactly one installation.* Every
host, domain, address, credential and person is a parameter supplied at
deploy time; the repository holds the shape, the operator holds the values.
That is why these pages write `<your-hatch-origin>` rather than a real
address — there isn't one the repository can know.

## Where to go

- **Start here** — [Getting started](getting-started) walks a clean machine
  to a first `hatch work` against your own board.
- **Concepts** — [The board and its columns](the-board-and-its-columns)
  names what happens in each column and who moves work out of it.
- **Running agents** — [Getting the runner and pointing it at the
  board](getting-the-runner-and-pointing-it-at-the-board) gets the binary
  onto your machine and aimed at this board.
- **Deploying and operating** — [Putting a front door on
  it](putting-a-front-door-on-it) covers signing in and running this
  somewhere more than one person reaches.
- **Reference** — [The CLAUDE.md block](the-claudemd-block) is the block to
  paste into a repository's own `CLAUDE.md` so an agent can reach the board.

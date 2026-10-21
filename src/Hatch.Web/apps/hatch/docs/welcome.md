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

**Docs** in the primary nav always opens here.

## Where to go

- **Start here** — [Getting started](getting-started) walks a clean machine
  to a first `hatch work` against your own board.
- **Concepts** — [The board and its columns](the-board-and-its-columns)
  names what happens in each column and who moves work out of it.
- **Running agents** — [The runner](the-runner) gets the binary onto your
  machine and aimed at this board.
- **Deploying and operating** — [Deployment overview](deployment-overview)
  covers running this somewhere more than one person reaches, and [the
  wall](the-wall-off-on-and-required) covers signing in.
- **Reference** — [The CLAUDE.md block](the-claudemd-block) is the block to
  paste into a repository's own `CLAUDE.md` so an agent can reach the board.

---
title: Hatch at home
section: Start here
order: 10
summary: The quick version: the two sentences that get a Hatch running, and the links to the rest of this page.
---

**The two sentences to send a friend:** *Clone this repository, install Docker
Desktop, then run `docker compose up -d` from the repository root and open
http://localhost:8080/apps/hatch/. That is a project board of your own, with a
page inside it that hands you the runner that works your tickets for you.*

Everything below is the long version of those two sentences — what to have
first, the one line, where the runner comes from, what the loop does to a
checkout, and the block to paste into your own repository's `CLAUDE.md` so your
agents know how to reach the board. It is written for a person and for a
Claude, in that order.

**Quick links.** The `hatch` CLI — the runner that works your tickets — is
handed out by your own Hatch, from **Agents → Get the runner**:

- [**Get the runner → download `hatch`**](/apps/hatch/runner) · or
  `<your-hatch-origin>/apps/hatch/runner` in a browser
- [What to do with it once it is on your `PATH`](the-hatch-cli-command-by-command)
- [`hatch work` — one increment](the-hatch-cli-command-by-command#one-increment-hatch-work) ·
  [`hatch go-to-work` — the loop](the-hatch-cli-command-by-command#the-loop-hatch-go-to-work)

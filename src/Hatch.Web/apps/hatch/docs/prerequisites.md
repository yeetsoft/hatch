---
title: Prerequisites
section: Start here
order: 20
summary: What to have installed and signed in to before bringing a Hatch up.
---

- **A checkout of this repository**, and **Docker Desktop**, running. `docker
  compose` builds the board's own image from source, so there is nothing else
  to install to bring it up.
- **On Windows, either PowerShell** — 5.1, the blue one that ships with the OS,
  or 7 (`pwsh`). Every line on this page runs in both, with two exceptions that
  say so where they appear: the restore line, and the backup line beside it,
  which needs 7 because 5.1 writes UTF-16 through `>` and `psql` will not read
  that back.
- **git**, and the **`claude` CLI, logged in**. Neither is needed to run the
  board; both are needed by the runner, which branches, commits and pushes from
  a checkout and spends each increment inside a `claude` session. The Runner
  page inside Hatch links all three.
- **`gh`, signed in — optional** ([cli.github.com](https://cli.github.com/);
  `gh auth login`, or `HATCH_GH_TOKEN` for the container runner, below). It
  turns on build checks, noticing merged pull requests, and merging approved
  pull requests. It also reads the build on the trunk's tip, which landing on
  the trunk needs: a repository with no CI reads as *no checks*, which a project
  with *Checks are required* unticked lands on after a settle window. Without
  it the runner still works tickets; it just never reads a build, never notices
  a merge and never merges a pull request. A ticket that lands on the trunk is
  still recorded and confirmed in the trunk, but waits as pending, since no
  build is ever read for it.

---
title: Project runner images
section: Running agents
order: 35
summary: A repository's own runner image, from the declaration to a running container - why it exists, how it is built, started and kept current, and what stays on host runners.
---

A **project image** is the [container runner](the-container-runner)'s generic
image plus whatever one repository needs to build itself. This page is the map
of the chain, from the file a repository commits to a container working its
tickets. Each step is specified once, on the page that owns it, and linked
rather than repeated here.

## Why an image of its own

The generic image carries `hatch`, `git`, the agent CLIs and nothing of any
toolchain, so it can serve any project and builds none of them. A project image
is `FROM` it plus the repository's declared dependencies
([HA-1874](/apps/hatch/issues/HA-1874)), so a machine needs Docker and nothing
else installed to work that repository's tickets.

It also changes who can be dispatched. The board folds a runner whose machine
lacks what a repository needs ([what a repository
needs](the-runner#what-a-repository-needs)), and a runner in a project image
finds that toolchain by detection ([has](the-runner#has)) and advertises the
image it was started from ([the image](the-runner#image)).

## The declaration

A repository says what a build of it needs in `.hatch/dependencies.json` on its
trunk. The shape, the master list of ids and what the file gates are in [what a
repository needs](the-runner#what-a-repository-needs); the image is built from
the same file, so the declaration and the image cannot disagree.

## Recipes, and the escape hatch

Each id on the master list has an install recipe: a Dockerfile fragment shipped
inside Hatch, one per id. The recipes of the declared set are concatenated in
id order. A recipe can require another, so `playwright-browsers` brings `node`
in bare even when `node` is not declared.

A need no recipe covers goes in `.hatch/runner.Dockerfile`, which is read byte
for byte after the recipes. It is the escape hatch, and it belongs to exactly
one repository, so an image built for several never reads one. Neither file is
ever committed in rendered form: the Dockerfile is rendered fresh at every
build. The full text, in order, is under [`hatch image`](cli-reference#image).

## Building it

`hatch image` has three subcommands, and [the CLI reference](cli-reference#image)
has the flags of each:

- `dockerfile` prints the Dockerfile the declaration renders to. It needs no
  Docker and no board.
- `build` builds and tags it, and with `--push` pushes it. The base comes from
  `--base` or `HATCH_RUNNER_BASE_IMAGE`, and the name from `--image` or the
  repository's `origin`; neither has a default written into Hatch.
- `workflow` prints a GitHub Actions workflow that does the same from the
  repository's own CI.

### In CI

`hatch image workflow` prints the text of
`.github/workflows/hatch-runner-image.yml`. Committing it is a change made by a
ticket in that repository; Hatch never pushes into a project repository. The
workflow reads two **GitHub Actions repository variables**,
`HATCH_RUNNER_BASE_IMAGE` and optionally `HATCH_RUNNER_IMAGE_NAME`. The first
is not the `hatch config --runner-base-image` setting of the near-identical
name, which the workflow never reads ([the CLI
reference](cli-reference#workflow) says where to set them). It publishes to
`ghcr.io/<owner>/<repo>/hatch-runner`, where `<owner>` and `<repo>` are the
repository's own.

`hatch image build --push` makes the same artifact from anywhere Docker runs: a
laptop, a runner host, or a CI that is not GitHub's.

## Starting a runner from it

Compose's `runner-image` profile starts a container from a named image, with the
same environment, checkout and workspace as the `runner` profile and nothing
built. `HATCH_RUNNER_IMAGE` names the image and `HATCH_RUNNER_PULL_POLICY`
says when to pull it ([the variables](environment-variables) list both). The
Compose command, the equivalent `docker run`, and what a private registry needs
- the host logged in, never the container's own git or `gh` tokens - are in
[starting it from a named
image](the-container-runner#starting-it-from-a-named-image).

Once it is running, [`hatch runners`](cli-reference#runners) and the Runners page show
the image it was started from beside the host and the build.

## One image for several repositories

`--repo` (repeatable) or `--all` renders one Dockerfile for several
repositories: the **union** of their declared sets, in the same id order. It is
worth it when the repositories' toolchains overlap, because they then share
layers and one runner can work all of them. Two repositories pinning one id to
different versions are refused by name, since a recipe installs one version,
and `--image` is required past one repository because there is no single
repository to name it after. [`hatch image`](cli-reference#image) has the exact
sentences.

## Keeping it current

The workflow rebuilds on a push to the trunk touching `.hatch/**`, on manual
dispatch, and weekly. That is how a moved base image or a changed recipe reaches
the image without anyone remembering.

Nothing in Hatch detects a stale project image on its own. Between rebuilds, a
container running an old image is caught the way any old runner is: the
`hatch` baked into it is only as new as the image's own build, so a runner
behind the board's revision carries the **Behind** badge ([the
detail](watching-the-loop#the-detail)).

## What cannot be containerised

A need the master list does not know has no recipe, by construction: Xcode and
the Windows SDKs have no Linux install at all, and every id on the list does.
Such a need is not a declared dependency; it is a runner tag, met by a host
runner that carries the tag and by `HATCH_HAS` detection, as [what a
repository needs](the-runner#what-a-repository-needs) says. A repository with
only that kind of need stays on host runners.

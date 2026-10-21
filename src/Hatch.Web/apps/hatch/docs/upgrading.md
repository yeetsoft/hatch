---
title: Upgrading
section: Deploying and operating
order: 40
summary: Pulling and rebuilding a Hatch without losing anything.
---

```
git pull
docker compose up -d
```

`git pull` brings the source up to date, and `up -d` rebuilds the images from
it: every service Hatch builds says `pull_policy: build` in `compose.yaml`, so
`up` never reuses an image built from older source. With nothing changed the
rebuild is all cache and takes seconds. A `down` between the two is not needed
and does no harm. **Your data is untouched** — it is in a volume, and a volume is not part of an image.
The migration re-runs on every start and does nothing when there is nothing to
do.

Download the runner again from the Runner page after an upgrade. The page
prints the revision it was built from, which is how you can tell whether the
one on your `PATH` is the one this board expects.

If you run [the container runner](the-container-runner), add `--profile runner` to the line above:
Compose only rebuilds and only restarts the services the profile it was given
turns on, and without it the runner container would sit on the version you
started with.

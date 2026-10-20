---
title: How to stop
section: Deploying and operating
order: 40
summary: Stopping a Hatch, with and without throwing its data away.
---

```
docker compose down
```

**`down` keeps your data.** The containers go; the volume outlives them, so
`down` and then `up -d` again — even after an upgrade has rebuilt the images —
brings back the same board. `down` takes the runner container with it whether or not you
name its profile, because it removes everything in the project.

```
docker compose down -v
```

**`down -v` throws your data away.** The `-v` removes the volume, and there is
no undo. Use it when you want to start over from an empty board, and not
otherwise.

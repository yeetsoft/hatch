---
title: If something is wrong
section: Deploying and operating
order: 90
summary: What a Docker-not-running error looks like, and where to check when a container won't start.
---

**`the system cannot find the file specified`, naming a pipe or a socket.** In
full it is `open //./pipe/dockerDesktopLinuxEngine` on Windows, or
`/var/run/docker.sock` on macOS. Nothing about the stack failed: Docker Desktop
is installed and not running. Start it and wait for its dashboard to say
**Engine running** — a fresh install does not start itself, and the first start
brings WSL2 up with it and can take a couple of minutes. `docker version`
printing a **Server** block as well as a **Client** one is how you know it is
ready. If the installer asked for a sign-out or a reboot and did not get one,
that is the other half of this: the group membership it added is not in effect
until then.

```
docker compose ps
docker compose logs api
docker compose logs migrate
```

`migrate` showing as exited is correct — that is a finished migration, not a
crash. `api` restarting in a loop usually means the migration did not finish,
and its log says why.

---
title: Kubernetes
section: Deploying and operating
order: 30
summary: What the API expects of a cluster, since no manifests, chart or Helm values ship with Hatch.
---

No manifests, chart or Helm values ship with Hatch. This page is what the
API expects of a cluster, and what an operator writes.

## What the API expects

- **A Postgres with two databases,** reached by `ConnectionStrings__Hatch`
  and `ConnectionStrings__Quartz` ([What the API
  reads](deployment-overview#what-the-api-reads) carries both rows).
  - The migration creates `hatch`'s schema. Nothing in the API creates
    Quartz's tables.
  - So on any Postgres but the `hatch-db` image, create the `quartz`
    database and run the part of `containers/hatch-db/pginit.sql` after its
    `\c quartz` line (line 5).
  - For a managed Postgres instead of one you run yourself, see [Backup,
    restore and replication](backup-restore-and-replication).
- **The migration before every rollout.** The same image with
  `HATCH_MIGRATE=1`, run to completion before any `api` pod starts, on every
  deploy (`Program.cs:368-422`). It migrates, logs, and exits `0`.
  - It needs the same `Auth__*` settings as the pods, because it is where
    the first-admin line is logged.
  - The API exits rather than wait for a schema that is not there
    (`compose.yaml:73-75`). A pod started before the migration finishes
    crash-loops until the Job completes.
- **Probes.**
  - Liveness is `GET /health/live`. It has no checks, so it is up whenever
    the process is.
  - Readiness is `GET /health/ready`: the `hatch` database is reachable
    (`Common/DatabaseHealthCheck.cs`, registered at `Program.cs:342` with
    the `ready` tag). Quartz's database is not checked.
  - Both stay open with the wall up (`Services/Auth/AuthGate.cs:120-121`).
  - They are split so a database blip takes a pod out of the Service
    without restarting it (`Program.cs:587-591`).
  - The image has no `HEALTHCHECK`.
- **Port 8080, plain http,** with TLS at the Ingress. See [Behind a reverse
  proxy](the-wall-off-on-and-required#behind-a-reverse-proxy).
- **Configuration through environment.**
  - Every key on [What the API reads](deployment-overview#what-the-api-reads).
  - `DOTNET_ENVIRONMENT` set to anything but `Development`, which writes
    JSON log lines to stdout.
  - The secrets from a Secret.
  - `.env.json` is optional, and a board needs nothing in it.
- **One origin** serving `/apps/*`, `/api/*`,
  `/.well-known/assetlinks.json` and `/health/*`: one Ingress host to one
  Service.
- **One replica.** Quartz's scheduler instance id is never configured, so
  every replica's check-in collapses onto one cluster row
  (`Jobs/SchedulerCheckout.cs:21-25`). The sign-in rate limiter is per
  process (`Program.cs:284-288`).

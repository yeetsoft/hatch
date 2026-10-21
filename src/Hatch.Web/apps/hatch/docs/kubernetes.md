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

## Reading Flux, optionally

Only a cluster that runs Flux has anything to read here, and the API runs
without it.

- **What it reads.** In a cluster (`KUBERNETES_SERVICE_HOST` set), the API
  lists Flux `GitRepository` (`source.toolkit.fluxcd.io/v1`) and
  `Kustomization` (`kustomize.toolkit.fluxcd.io/v1`) objects in
  `flux-system` (`Services/FluxRevisionReader.cs:41,144-145`).
  - It uses its own service account's token.
  - It reads at most every 30 seconds (`FluxRevisionReader.cs:42`).
- **The shape.** Each GitRepository carries its name, branch and sha. Under
  it sit the Kustomizations that point at it by `spec.sourceRef.name`, each
  with its name, the sha it last applied, and Ready: true, false, or null
  when Flux does not say (`Models/HatchRevision/ClusterRevisions.cs`).
- **Where it goes.** What each object last applied comes back in the
  `cluster` field of `GET /api/hatch-revision`. That field goes to signed-in
  callers, or to everyone with the wall off
  (`Controllers/HatchRevisionController.cs:51-52`).
- **No screen in the Hatch app draws it today.** The `cluster` field is
  typed `unknown` (`src/Hatch.Web/apps/hatch/src/types.ts:2061-2070`). Read
  it from the API.
- **The RBAC it wants.** A Role in `flux-system` with `get` and `list` on
  `gitrepositories` and `kustomizations`, bound to the API's service
  account. The code only lists; the comment at
  `FluxRevisionReader.cs:30-32` asks for both.
- **Without it,** the field says the cluster's Flux resources could not be
  read, and the API logs a Warning (`FluxRevisionReader.cs:154-155`). Two
  other cases say so in the same field:
  - Outside a cluster it says it is not running in a Kubernetes cluster,
    with no log (`FluxRevisionReader.cs:64-70`).
  - When the client cannot be built from the service-account files, it says
    it could not read the cluster's service-account credentials, with a
    Warning (`FluxRevisionReader.cs:76-79`).

  Nothing else breaks.

## The objects you would write

One line each: what it is for, and which part above it carries.

- **A Secret:** the Google client secret and the connection strings. See
  [What the API expects](#what-the-api-expects), configuration through
  environment.
- **A ServiceAccount.** The Pod runs as it. Add the Role and RoleBinding
  from [Reading Flux, optionally](#reading-flux-optionally) only if you want
  that read.
- **The migration Job,** or whatever your deploy tool runs before a
  rollout: a Helm pre-upgrade hook, or a Flux Kustomization the app depends
  on. See the migration before every rollout, above.
- **A Deployment:** one replica, the two probes, and the environment from
  the Secret. See one replica, probes and configuration through
  environment, above.
- **A Service** on port 8080. See port 8080, above.
- **An Ingress:** one host, TLS, every path to the one Service. Optionally a
  forward-auth middleware: see [Forward
  auth](the-wall-off-on-and-required#forward-auth). See one origin, above.
- **The Postgres,** or a managed one. See a Postgres with two databases,
  above.

The image is `ghcr.io/<owner>/hatch-api`, pinned by a sha tag. See
[Building it or pulling it](deployment-overview#building-it-or-pulling-it).

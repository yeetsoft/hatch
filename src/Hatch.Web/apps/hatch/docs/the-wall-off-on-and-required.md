---
title: The wall: off, on and required
section: Deploying and operating
order: 20
summary: What each of the wall's three postures - off, on and required - changes about who may reach a Hatch, and how.
---

One switch, `HATCH_AUTH` (`Auth__Enabled` underneath it), and three
postures.

- **Off** — the default, and right for one computer: nobody is asked who
  they are.
- **On** — sign in with Google: nobody reaches the board until they do.
- **Required** — On, behind a TLS proxy: how to run a Hatch more than one
  computer reaches.

A Hatch reachable from a network with the wall Off is open to everyone on
that network, not only to whoever is sitting at it — Off asks nothing of
anybody, wherever the request comes from. [People, roles and
keys](people-roles-and-keys#the-wall) names the same three states from the
other side, in terms of who may do what.

## Off

Nothing asks who anybody is, and everyone is one local person. That person
is named from **Settings → Your name**; failing that, the account that
started Hatch; failing that, `friend` (`Services/Auth/LocalCaller.cs`).
Every request is allowed — there is no gate to pass
(`Services/Auth/AuthGate.cs:268`).

A program fares the same way. A runner with no key names itself with a
header, so the trail still says which runner did what, though a name is all
that header proves; it is dropped only once the wall goes up
(`Common/AuthMiddleware.cs:51`). No key is needed here, and `hatch config`
takes a blank. [The container runner](the-container-runner) works only in
this posture: it fetches its credential by asking Hatch for it, and a Hatch
with its wall up refuses that ask outright.

## On

A request carrying no cookie and no key is refused, and a page load with
neither is sent to sign in at `/apps/auth/`. The runner header means
nothing here — On listens only to a cookie or a key. **Manage → Users** and
**Manage → API keys** appear in the nav, because there is now a role to
hold and a key to mint. `migrate` and `api` have to be given the same
`Auth__*` lines, which Compose already does (`compose.yaml:56-61`).

## Putting a front door on it

Turning the wall on, from nothing, in six steps.

**1. Make a Google OAuth client.** In the Google Cloud console, create an
OAuth client of type *Web application*. Add this as an authorized redirect
URI:

```
<your-hatch-origin>/api/auth/google/callback
```

Configure the consent screen too, and while it is unpublished, add as test
users the Google accounts that may sign in.

**2. Write a `.env` beside `compose.yaml`.** Compose reads it on its own,
and git ignores it:

```
HATCH_AUTH=true
HATCH_GOOGLE_CLIENT_ID=<your client id>
HATCH_GOOGLE_CLIENT_SECRET=<your client secret>
```

On `http://localhost:8080` that is all: the redirect address is worked out
from the request. Behind TLS, where the address people type is not the one
the api sees, add
`HATCH_GOOGLE_REDIRECT_URI=<your-hatch-origin>/api/auth/google/callback`
([Behind a reverse proxy](#behind-a-reverse-proxy) says why).

**3. Run `docker compose up -d` again,** then check `docker compose logs
migrate`. With no Administrator yet, it says:

> No Administrator exists yet. The next person to sign in with Google
> becomes the Administrator.

and, if a Google variable above did not arrive, it also says:

> Google sign-in is not configured: set Auth__Google__ClientId and
> Auth__Google__ClientSecret.

Both are written once, by the one-shot `migrate` step, and only when the
wall is on (`Program.cs:416-419`).

**4. The first new Google identity to sign in while no admin exists becomes
one,** so sign in yourself before you share the address.
[People, roles and keys](people-roles-and-keys#roles) has the rest: how
somebody after that is approved, or added ahead of time.

**5. The runner now needs a key.** Open **Manage → API keys**, mint one,
and give it to the runner:

```
hatch config --origin <your-hatch-origin> --key <the key>
```

[The runner](the-runner#pointing-it-at-a-board) has the rest of what that
line does.

**6. If you are locked out,** see [Locked out](#locked-out), below.

## Locked out

Set `HATCH_AUTH=false`, or delete the line, and run `docker compose up -d`.
That is the whole rollback, and nothing is lost: your projects, issues,
people and keys are all there when the wall goes back up.

## Behind a reverse proxy

TLS ends at the proxy. The API itself serves plain http on 8080, and
redirects to https only when an https port is configured — which neither
Compose nor the image does (`Program.cs:468-482`). Compose passes the `Auth__*`
lines it already names (`compose.yaml:59-64` and `86-91`) and none of the
ones below; to set one, add it under `environment:` for `api` in a
`compose.override.yaml` beside `compose.yaml`.

- **The cookie.** `__Secure-hatch_grant` (`Auth__CookieName`), `Secure`,
  `HttpOnly`, `SameSite=Lax`, path `/` (`Services/Auth/AuthCookie.cs:116-129`).
  It is kept 400 days (`AuthCookie.cs:26`) and issued again once it is
  `Auth__GrantRenewAfterDays` old, 30 unless you say otherwise
  (`Services/Auth/AuthOptions.cs:104`). Because it is `Secure`, Chromium and
  Firefox accept it over http only from `http://localhost`, not from any
  other http address (a LAN IP, say), and Safari does not accept it even on
  localhost. That is why the proxy has to speak https.
- **The redirect address.** The API honours only `X-Forwarded-For` and
  `X-Forwarded-Proto`, from any proxy, and never `X-Forwarded-Host`
  (`Program.cs:457-466`), so the host it sees is the proxy's own, not the one
  people type. Behind a proxy, set both in `.env`:

  ```
  HATCH_GOOGLE_REDIRECT_URI=<your-hatch-origin>/api/auth/google/callback
  HATCH_PUBLIC_URL=<your-hatch-origin>
  ```

  The first is the address Google sends a person back to
  (`compose.yaml:91`); the second is the address a pull request's link back
  to its ticket is built from (`compose.yaml:101`).
- **One origin.** The proxy sends `/apps/*`, `/api/*`,
  `/.well-known/assetlinks.json` and `/health/*` to the API from one host.
  The cookie, and the installed app's scope (`/`,
  `src/Hatch.Web/apps/hatch/public/manifest.webmanifest:6`), belong to that
  origin.
- **One sign-in across subdomains.** `Auth__CookieDomain`, for example
  `.${DOMAIN}`, makes one sign-in cover every host under it. Empty, the
  default, keeps the cookie host-only (`AuthOptions.cs:74-79`).
- **Hosts never challenged.** `Auth__ExemptHosts__0`, `Auth__ExemptHosts__1`,
  and so on. The port is dropped and case ignored, and the host is checked
  before any path (`Services/Auth/AuthGate.cs:248`, `309-323`).
- **What stays open with the wall up.** Every path `AuthGate.cs` exempts,
  matched by whole path segment and case-insensitively
  (`exemptPaths`, `AuthGate.cs:115-189`; `exemptExactPaths`, `204-214`):
  - `/health/live` and `/health/ready`, because a probe presents no cookie,
    and gating them fails readiness on every instance.
  - `/apps/auth`, `/auth` and `/auth/` (the last two exactly, not as
    prefixes), `/api/auth/verify`, `/api/auth/redeem` and `/api/auth/google`
    (sign-in start and callback): the way in. Gating the way in is a redirect
    loop.
  - `/.well-known/assetlinks.json`, which Android's own link verifier fetches
    with no cookie.
  - `/api/hatch-revision`, which commit this replica runs. The caller who
    most needs it is a stale tab about to be refused, so it is answered to
    everyone.
  - `/api/app-version`, the content-hashed asset filenames of each app, which
    the kiosk shell fetches with no cookie to tell a dashboard from the
    proxy's error page.
  - `/api/ui-logs`, `/api/vm-console-logs`, `/api/kiosk/provisioning-info`
    and `/api/sun-events`, which callers with no cookie reach.
  - The media library's configured `RequestPath`, `/media` unless changed
    (`Services/Media/MediaLibraryOptions.cs:30`).

  Every refusal logs the Warning `Auth refused {Reason} for {Host}{Path} from
  {ClientIp}` (`AuthGate.cs:303-305`). Read it with `docker compose logs api`.

## Forward auth

For a proxy that asks before it forwards, the question goes to Hatch.
Traefik's `forwardAuth` is the example; no proxy configuration ships with
Hatch.

- **The endpoint.** `GET /api/auth/verify` (`Controllers/AuthController.cs:59-124`).
  It reads `X-Forwarded-Method`, `X-Forwarded-Host`, `X-Forwarded-Uri` and
  `X-Forwarded-Proto` to learn what the proxy was asked, and the cookie or an
  `Authorization: Bearer` key to learn who is asking.
- **What it answers,** always with `Cache-Control: no-store` on a refusal:
  - `204` when the caller is allowed — with `X-Hatch-Grant` and
    `X-Hatch-Label` when a cookie let them in, for the proxy to copy onto the
    request it forwards.
  - `302` to the sign-in page for a browser loading a page
    (`Services/Auth/AuthChallenge.cs:31-50`). The address is absolute only
    for a host inside `Auth__CookieDomain`; anywhere else, or with that
    domain empty, it is relative (`AuthChallenge.cs:82-110`).
  - `401` for anything else, and always for a caller that sent a key.
  - `204` for everybody, with the wall off.
- **The proxy as the only enforcer.** `Auth__EnforceInProcess=false` leaves
  refusing to the proxy. Anything that reaches the API directly is then as
  open as it was before the wall existed (`AuthOptions.cs:41-44`). Compose
  never sets it.

## The installed app

- **The manifest is behind the wall,** which is why
  `src/Hatch.Web/apps/hatch/index.html:6` links it with
  `crossorigin="use-credentials"`: a fetch with no cookie gets a 401, and an
  app whose manifest 401s never offers to install (`docs/hatch.md:160-172`).
- **Android's asset links.** `HATCH_ANDROID_PACKAGE` and
  `HATCH_ANDROID_CERT_FINGERPRINT` (`Apps__AssetLinks__0__PackageName` and
  `Apps__AssetLinks__0__Sha256CertFingerprints__0`, `compose.yaml:111-112`)
  are served at `/.well-known/assetlinks.json`
  (`Modules/AssetLinksController.cs:25-44`). It is open with the wall up. It
  answers `404` while both are unset. A malformed value stops the API at
  start (`Modules/AssetLinksValidation.cs`). A second phone is
  `Apps__AssetLinks__1__PackageName` and
  `Apps__AssetLinks__1__Sha256CertFingerprints__0` (`compose.yaml:105-110`).
  [Hatch on your
  phone](hatch-on-your-phone#letting-it-own-links-on-android) shows how to
  read the two values off the phone.

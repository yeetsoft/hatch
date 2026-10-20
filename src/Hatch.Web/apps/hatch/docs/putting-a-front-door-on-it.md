---
title: Putting a front door on it
section: Deploying and operating
order: 10
summary: Turning on sign-in with Google, step by step.
---

Optional, and off until you turn it on. With the wall up Hatch asks who you
are, and people sign in with Google. It takes one file and six steps.

**1. Make a Google OAuth client.** In the Google Cloud console, create an OAuth
client of type *Web application*. Add this as an authorized redirect URI:

```
<your-hatch-origin>/api/auth/google/callback
```

Configure the consent screen too, and while it is unpublished, add as test
users the Google accounts that may sign in.

**2. Write a `.env` beside `compose.yaml`.** Compose reads it on its own, and
git ignores it:

```
HATCH_AUTH=true
HATCH_GOOGLE_CLIENT_ID=<your client id>
HATCH_GOOGLE_CLIENT_SECRET=<your client secret>
```

On `http://localhost:8080` that is all: the redirect address is worked out from
the request. Behind TLS, where the address people type is not the one the api
sees, add `HATCH_GOOGLE_REDIRECT_URI=<your-hatch-origin>/api/auth/google/callback`.

**3. Run `docker compose up -d` again,** then open `<your-hatch-origin>/apps/hatch/`
and sign in. Confirm the wall arrived with `docker compose logs migrate`; if it
says Google sign-in is not configured, a variable above did not arrive.

**4. The first Admin, then everyone else.** The first person to sign in becomes
the Administrator — the migrate log says so ("No Administrator exists yet. The
next person to sign in with Google becomes the Administrator."). So sign in
yourself, straight away, before you share the address. Everyone after arrives
Pending, unless an Administrator added them first with **Add user**, and can
do nothing until an Administrator approves them on **Manage → Users**, by
changing their **Role**. Signing in again never changes anybody's role.

**5. The runner now needs a key.** Open **Manage → API keys**, mint one, and
give it to the runner:

```
hatch config --origin <your-hatch-origin> --key <the key>
```

A runner in a container takes it as `HATCH_KEY=<the key>` in the same `.env`.

**6. If you are locked out,** set `HATCH_AUTH=false` (or delete the line) and
run `docker compose up -d`. That is the whole rollback, and nothing is lost:
your projects, issues and people are all still there when you turn it back on.

A note on http: the sign-in cookie is `Secure`. Chromium and Firefox accept that
from `http://localhost`, but not from any other http address (a LAN IP, say),
and Safari does not accept it even on localhost. For those, put TLS in front of
Hatch and set `HATCH_GOOGLE_REDIRECT_URI` to the https address.

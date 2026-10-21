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
`HATCH_GOOGLE_REDIRECT_URI=<your-hatch-origin>/api/auth/google/callback`.

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

A note on http: the sign-in cookie is `Secure`. Chromium and Firefox accept
that from `http://localhost`, but not from any other http address (a LAN
IP, say), and Safari does not accept it even on localhost. For those, put
TLS in front of Hatch and set `HATCH_GOOGLE_REDIRECT_URI` to the https
address.

## Locked out

Set `HATCH_AUTH=false`, or delete the line, and run `docker compose up -d`.
That is the whole rollback, and nothing is lost: your projects, issues,
people and keys are all there when the wall goes back up.

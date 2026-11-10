---
title: People, roles and keys
section: Concepts
order: 90
summary: Who may do what in Hatch: the wall, the three roles, project owners and approvers, and runners' keys.
---

Who may do what in Hatch turns on four things: the wall, the role a person
holds, the projects they own or approve, and, for a program, its key. This
page takes them from the outside in, starting with whether Hatch asks who you
are at all.

## The wall

The wall is the sign-in in front of Hatch, and the operator decides whether it
is up with `HATCH_AUTH`. It is off until they turn it on.

- **Off.** Nothing asks who you are, and everyone is the one local person. That
  person is named from **Settings → Your name**, shown only where the wall is
  off. Until it is set, Hatch uses the account name of whoever started it, and
  failing that, `friend`. A caller with no key is that local person, never a
  program - a runner is named by its key or not at all. A program is still
  refused the writes only a person may make. **Users** and **API keys** are
  not in the nav, because with no wall nobody holds a role.
- **On.** People sign in with Google. A request that carries no credential is
  refused.

[The wall, off, on and required](the-wall-off-on-and-required) says what
each state changes, and how to turn it on.

## Roles

Behind the wall every person holds one role, shown in lowercase on **Manage →
Users**: `pending`, `user` or `admin`.

- **`pending`** reaches nothing behind the wall. They see the sign-in screen,
  which tells them "An administrator has to approve your account before you can
  see the board."
- **`user`** reaches every route on the board.
- **`admin`** reaches what a user does and adds the operator's own controls:
  people, sessions, API keys and the core settings. With the wall up, an
  admin also controls every runner, not only the ones that work for them.

The first `admin` is the first new Google identity to sign in while no admin
exists. The `migrate` log says so, so that you sign in yourself before you
share the address. Signing in again never changes anybody's role.

An admin lets someone in two ways, both on **Manage → Users**:

- **Approve them after they sign in** by changing their **Role** from
  `pending`.
- **Let them in ahead of time** with **Add user**. It takes an email, a role
  (`user` unless you choose otherwise) and an optional name. The first Google
  sign-in with that address claims it and gets the chosen role, even when no
  admin exists yet.

The last `admin` cannot be demoted or deleted. The refusal is a `409`, and the
**Role** select and **Delete** button on that row are disabled. Make someone
else an `admin` first.

## Owners and approvers

A project carries a second permission beside the three roles above, held per
project rather than globally.

- **Where it is set.** Per project, on **Manage → Projects**, **Edit**,
  **Members**. Each member there holds `owner` or `approver`.
- **Who becomes an owner.** Whoever creates a project owns it. A project
  with no owner at all may be claimed by any user with **Claim**. Only an
  owner adds, removes or changes the role of a member, and the last owner
  cannot be dropped or demoted — the write is refused with a `409`.
- **An admin is not an owner.** The check a project runs reads only its own
  membership, never the signed-in person's role, so an `admin` who is not a
  member of a project cannot move its work into a Protected column either,
  and has to be added by one of the project's owners like anybody else.
- **Owner or approver.** The one permission membership grants: moving an
  issue into a [Protected](the-board-and-its-columns#column-flags) column by
  hand, and setting [pre-approval](issues#pre-approval) to or from
  *pre-approved*. Every other change to pre-approval follows whichever rule
  governs moving into the board's send-back column — the same owner-or-
  approver test, but only where that column is itself Protected.
- **Owner alone.** A project's landing and auto-heal settings, whether its
  playbooks come from its repository or the board, and its one-pull-request
  policy, are an owner's call; an approver has no say in either.
- **Keys and runners never count.** A program — an API key, or a runner
  naming itself with a header — is refused a Protected move regardless of
  membership. With the wall off, the one local person is a real person
  rather than a program, and may move work into a Protected column from the
  browser the same as anyone else; a key or a runner still may not.
- **The approve hop** carries a pre-approved issue into a Protected column
  with no session and nobody pressing anything by hand: the pre-approval was
  that owner's or approver's own permission, given in advance.

## Runners and their keys

A runner is a key: the credential a program carries instead of a person,
`Authorization: Bearer hatch_ak_…`, plus its config - the board's settings for
it (state, caps, tags, projects), kept in `RunnerConfigs` against the key's id.
The heartbeat table a running loop writes is a bridge, joined to the key by
name, that HA-1937 retires.

- **Where it is minted.** **Manage → API keys**, admin only. A key is minted by
  *creating a runner* (`POST /api/hatch/runners`): no key may mint, rotate,
  revoke or re-own a key, only a person at that screen. The form takes **Name**
  ("What the audit trail will call it") and **Belongs to** ("Whose tickets
  this key's `--mine` will reach"), then **Mint key**. The secret is shown
  once, on the page, and the response is sent `no-store`, so it is never shown
  again. A name already held, by a key revoked or not, is refused.
- **Replacing a secret.** An admin rotates a runner with
  `POST /api/hatch/runners/{name}/rotate`; a new secret is made at once and
  shown once, sent `no-store`; the old secret is refused from the next call (a
  `401`), so every loop using it needs the new one; the name, owner, tags,
  bounds, sessions, omissions and attribution carry over, and the key's prefix
  and a `rotatedAt` time are updated; a revoked runner cannot be rotated
  (`409`) — rotate to replace a leaked secret and keep the runner working,
  revoke to stop it. [`api-reference`](api-reference) has the route.
- **What a key reaches.** Everything under `/api/hatch`, less the routes that
  take a person only. Those refuse a key outright — on a walled Hatch with
  `key_not_accepted`, and with the wall off they refuse any program the same
  way. Among them: a ticket's assignee, priority, hold and pre-approval; its
  WIP limit and the WIP section; playbooks, per project and per ticket — the
  one entry here that also refuses a call with no key and no browser's
  `Sec-Fetch-Mode` mark, so "person" means a person at the
  browser specifically, not merely a signed-in session with no UI; a
  column's settings; a project's repositories, logo, landing and auto-heal
  settings, playbook source and members; Hatch's own settings; a runner's
  record; attachment upload and delete; marking the
  overnight view seen; running a backup.
  [`api-reference`](api-reference) has the route-by-route version.
- **Which runners a key sees.** A key reads the runners of its owner, an
  admin's key reads every runner, and a key with no owner reads only the
  runners that belong to nobody. A person at a browser, and a Hatch with its
  wall off, read every runner.
  See [`api-reference`](api-reference).
- **Whose name an event carries.** The signed-in person's, if there is one;
  else the key's **Name**; else, with the wall off, the local name. A key's
  request carries no person, so on a walled Hatch an event it causes is
  attributed to that **Name** — which is why the mint form's hint calls it
  what the audit trail will call the key. A revoked key's row stays, so an
  old event still has a name to point to.
- **A revoked runner stays listed.** Revoking stops the key working and leaves
  the runner, and its config, on the list.
- **Revoking takes effect from the next call.** It stamps the moment
  revoked, and every call after is checked against that fresh — nothing
  about a key is cached — so the next call the key makes is a `401`.
  Revoking an already-revoked key changes nothing.
- **Where the key goes:** never into the artifact — [the runner](the-runner#where-the-key-never-goes)
  says where it stays instead.

## 401 and 403

Two refusals a program meets at `/api`, and they say different things. Browser
navigation to `/apps/hatch` on a walled Hatch does not meet either: it is sent
to sign-in instead.

- **A `401` is the wall.** Either a key was sent and refused — never minted,
  mistyped, or revoked — or no key was sent to a Hatch that has its wall on.
  The CLI tells the two apart:
  - No key sent: ``hatch: 401 - no key was sent and this Hatch has its wall on. Run `hatch config`.``
  - Key sent: `hatch: 401 - the key was not accepted. Minted, not revoked, copied whole?`
- **A `403` is a gate.** The wall let the caller in and a rule behind it said
  no. There are two kinds.
  - **The role gate** answers `{ "error": "<code>" }`, with one of:
    - `pending_approval` — the person has not been let in yet, and is refused
      at every level.
    - `not_admin` — the person's role is below what the route asks for.
    - `no_person` — an enrolled device nobody has claimed.
    - `key_not_accepted` — a key reached a route that takes no key.
  - **A project or person-only refusal** is a bare sentence rather than a
    code:
    - `only an owner or approver of <KEY> may move work into "<column>"`
    - `only an owner of <KEY> may add or remove its members`
    - `only an owner of <KEY> may change how its work lands`
    - `only an owner of <KEY> may change whether its playbooks are sourced from its repository`
    - `only an owner or approver of <KEY> may set pre-approval - it is the approve press given in advance`
    - `setting an issue's pre-approval level is a person's call, not an agent's`
    - `moving work into a protected column is an owner's or approver's call, not an agent's`
    - `only <name>, or an admin, may control <runner>`
- **What the CLI prints for a `403`** is the API's own reason: a role-gate
  code prints as itself (`hatch: 403 - not_admin`), and a plain-sentence
  refusal prints that sentence (`hatch: 403 - only an owner or approver of
  AER may move work into "approved"`). The one exception is `key_not_accepted`,
  where the fixed sentence `hatch: 403 - the key is good and this route is
  not one it may take (CLAUDE.md).` replaces the code outright. An empty body still
  gets a fixed sentence, worded by whether a key was sent at all.

## Whose tickets `--mine` reaches

- **Who sets it.** An admin, on **Manage → API keys** — a key may never set
  its own. **Belongs to**, on the mint form, lists people only and is
  pre-selected to the admin at the screen; left as is, that admin becomes the
  key's owner. The **Owner** select on an existing, unrevoked row also offers
  `nobody`; a revoked row shows its owner as text only.
- **Whose tickets it reaches.** The signed-in person; failing that, the key's
  owner; failing that, the local person where the wall is off. `--mine` takes
  only a ticket assigned to that person, or to the key itself, and folds
  every other one.
- **A key that belongs to nobody has no tickets of its own**, and is refused
  before a `--mine` pass scans anything: an admin sets it on **Manage → API
  keys**.

[One increment and the loop](one-increment-and-the-loop#sharing-a-board) says
more about sharing one board between people and keys.

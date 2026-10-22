---
title: People, roles and keys
section: Concepts
order: 90
summary: Who may do what in Hatch: the wall, the three roles, project owners and approvers, and API keys.
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
  failing that, `friend`. A runner with no key names itself with a header, so
  the trail still says which runner did what, but a name is all that header is:
  it proves nothing. A program is still refused the writes only a person may
  make. **Users** and **API keys** are not in the nav, because with no wall
  nobody holds a role.
- **On.** People sign in with Google. The runner header is ignored, and a
  request that carries no credential is refused.

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
  people, sessions, API keys and the core settings.

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
- **Owner alone.** A project's landing and auto-heal settings, and whether
  its playbooks come from its repository or the board, are an owner's call;
  an approver has no say in either.
- **Keys and runners never count.** A program — an API key, or a runner
  naming itself with a header — is refused a Protected move regardless of
  membership. With the wall off, the one local person is a real person
  rather than a program, and may move work into a Protected column from the
  browser the same as anyone else; a key or a runner still may not.
- **The approve hop** carries a pre-approved issue into a Protected column
  with no session and nobody pressing anything by hand: the pre-approval was
  that owner's or approver's own permission, given in advance.

## API keys

The credential a program carries instead of a person: `Authorization: Bearer
hatch_ak_…`.

- **Where it is minted.** **Manage → API keys**, admin only — no key may
  mint, revoke or re-own a key, only a person at that screen. The form takes
  **Name** ("What the audit trail will call it"), **Scopes** ("What the key
  may reach. A key with none reaches nothing.") and **Belongs to** ("Whose
  tickets this key's `--mine` will reach"), then **Mint key**. The secret is
  shown once, on the page, and never again.
- **One scope, `hatch`.** Everything under `/api/hatch`, less the routes that
  take a person only. Those refuse a key outright — on a walled Hatch with
  `key_not_accepted`, and with the wall off they refuse any program the same
  way. Among them: a ticket's assignee, priority, hold and pre-approval; its
  WIP limit and the WIP section; playbooks, per project and per ticket; a
  column's settings; a project's repositories, logo, landing and auto-heal
  settings, playbook source and members; Hatch's own settings; a runner's
  record; image upload; marking the overnight view seen.
  [`api-reference`](api-reference) has the route-by-route version.
- **Whose name an event carries.** The signed-in person's, if there is one;
  else the key's **Name**; else, with the wall off, the local name. A key's
  request carries no person, so on a walled Hatch an event it causes is
  attributed to that **Name** — which is why the mint form's hint calls it
  what the audit trail will call the key. A revoked key's row stays, so an
  old event still has a name to point to.
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
    - `key_not_accepted` — a key reached a route that takes no key at all.
    - `scope_mismatch` — a key reached a route that takes a scope this key does
      not carry.
  - **A project or person-only refusal** is a bare sentence rather than a
    code:
    - `only an owner or approver of <KEY> may move work into "<column>"`
    - `only an owner of <KEY> may add or remove its members`
    - `only an owner of <KEY> may change how its work lands`
    - `only an owner of <KEY> may change whether its playbooks are sourced from its repository`
    - `only an owner or approver of <KEY> may set pre-approval - it is the approve press given in advance`
    - `setting an issue's pre-approval level is a person's call, not an agent's`
    - `moving work into a protected column is an owner's or approver's call, not an agent's`
- **What the CLI prints for a `403`** is the API's own reason: a role-gate
  code prints as itself (`hatch: 403 - not_admin`), and a plain-sentence
  refusal prints that sentence (`hatch: 403 - only an owner or approver of
  AER may move work into "approved"`). The one exception is `key_not_accepted`
  and `scope_mismatch`, where the fixed sentence `hatch: 403 - the key is
  good and this route is not one it may take (CLAUDE.md).` replaces the code
  outright, since those two really are a scope refusal. An empty body still
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

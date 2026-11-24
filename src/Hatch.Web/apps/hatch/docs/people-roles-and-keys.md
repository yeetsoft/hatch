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
  refused the writes only a person may make. **Users** is not
  in the nav, because with no wall nobody holds a role.
- **On.** People sign in with Google. A request that carries no credential is
  refused.

[The wall, off, on and required](the-wall-off-on-and-required) says what
each state changes, and how to turn it on.

## Roles

Behind the wall every person holds one role, shown in lowercase on **Manage →
Users**: `pending`, `user` or `admin`.

- **`pending`** reaches nothing behind the wall. They see the sign-in screen,
  which tells them "An administrator has to approve your account before you can
  see the board." A key whose owner is `pending` is refused too, on every route
  that takes a key, with `403 pending_approval`. The key is not revoked and
  nothing about it changes, so it works again when the owner is `user` or
  `admin`.
- **`user`** reaches every route on the board.
- **`admin`** reaches what a user does and adds the operator's own controls:
  people, sessions and the core settings. With the wall up, an admin also
  controls every runner, not only the ones that work for them, may create a
  runner for any person or for nobody, and may change a runner's owner. A user
  creates, rotates and revokes only their own runners.

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

Deleting a person revokes every key they own, in the same press. Each key stops
at the moment of the delete, and a key that was already revoked keeps the time
it had. The keys stay listed on **Runners** as revoked, with no owner, so an old
event still has a name to point to. A delete that is refused, such as the last
`admin`, revokes nothing.

Once anybody has linked a Slack account, **Manage → Users** also has a **Slack**
column, showing how many Slack links each person holds. Press the count to open
that row's links: the workspace, the Slack member, and whether the link is
`linked` or `waiting`. **Remove** takes one away, after asking. Nothing is sent
to it afterwards, and the person can link again from Settings. There is no
button to make or confirm a link for somebody else: a link is its person's own
choice. Deleting a person removes their links too, and a second person can then
confirm a link to the same Slack member at once. The column is admin-only, like
the rest of the page, and a key is refused.

An admin also sees **Manage → Chat**, where a Slack workspace is connected. [Hatch in Slack](hatch-in-slack)
has the steps.

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
  policy, and its level (paused, normal or expedited), are an owner's call; an
  approver has no say in either.
- **Keys never count.** A program — an API key — is refused a Protected move
  regardless of membership. With the wall off, the one local person is a real
  person rather than a program, and may move work into a Protected column
  from the browser the same as anyone else; a key still may not.
- **A key files no bug from a finding.** `POST /goose/findings/{id}/bug` is a
  person's press, refused to a key with `403 key_not_accepted`, and no project
  setting changes that: a key filing work for itself is the loop the goose must
  never become.
- **The approve hop** carries a pre-approved issue into a Protected column
  with no session and nobody pressing anything by hand: the pre-approval was
  that owner's or approver's own permission, given in advance.

## Scope

A person's scope is which projects they may see. It is a third thing beside the
role and project membership, and it grants nothing: it only narrows.

- **Three modes.** `all` sees every project. `none` sees no project. `some`
  sees the projects an admin chose.
- **No row is `all`.** Someone who has never been given a scope sees every
  project.
- **Who sets it.** An admin, through `GET /scopes`, `GET /scopes/{personId}`
  and `PUT /scopes/{personId}`. An admin may set their own. A key is refused all
  three, and so is any non-admin.
- **Where it is set.** On **Manage → Users**, in the Scope column of a person's
  row, and in the Add user form. Choose `none`, `all` or `some`. `all` and
  `none` save at once. `some` opens the row with every project listed, with its
  key, name, colour and icon; ticking or unticking one saves at once, and the
  row reads `some (N)`, N being the ticked count. A `some` with nothing ticked
  reads `some (0)` and acts as `none`. Choosing `some` starts from what the
  person sees now: all of the projects ticked from `all`, none from `none`.
  The row's Projects button opens the list again. An admin who narrows their
  own scope is asked first and can leave it as it was; widening it, and anyone
  else's change, never asks. The Add user form saves the scope in a second call
  once the person exists, and says so if that call fails.
- **Setting `all` or `none` clears the list.** No chosen projects are kept
  behind for later. A project that is deleted drops out of every list.
- **A key follows its owner.** A key sees what the person who owns it sees. A
  key with no live owner sees every project. So do the wall being off, and
  work the server starts itself.
- **A person sees their own scope.** `GET /api/me` answers it as `scope`, for a
  person and for a key (its owner's). When it reaches no project, the board,
  Plan, Projects and Dreams pages draw one sentence in place of the page: *No
  project is in your scope; an admin sets it on Manage → Users.* It is not a
  blank board and not an error. A fresh install with no projects yet is not
  this: its scope is `all`, and it says "No projects yet." The new-issue
  default, the `?scope=` tab filter and every project picker offer only the
  projects `GET /api/projects` returns, which is already the caller's own.
  `hatch board`, `next`, `queue`, `go-to-work` and `do-my-work` under a key
  that reaches no project print the same news in one sentence and exit `0`,
  where they would otherwise read as a board with nothing on it.
- **It is enforced where the data is read.** A person on `some` sees only the
  rows of those projects, on every board, list, search and issue page. A person
  on `none` sees no project, issue, comment or event. A project outside the
  scope reads as one that does not exist, so its existence is not told, and a
  bulk change never touches a row the person cannot see. A few house-wide
  numbers, such as how many cards are in progress, still count every project:
  the limit is the house's, and they show a number, never a card.

## What a scoped person sees

The [scope](#scope) above says what a person is allowed. This is what that is
like from the other side: the same pages and the same calls, with some
projects not there.

- **`all` sees what it always did.** Every project, every issue, every list.
  So does a person with no scope row.
- **`none` sees nothing of any project.** The board has its columns and no
  cards. Search finds nothing. The Projects page is empty. A list answers
  empty, not with an error.
- **`some` sees the chosen projects, and only those.** Their issues, comments,
  events, dreams, playbooks and reports read as they do for `all`. Everything
  of any other project is left out.
- **A hidden project reads as one that does not exist.** A call that names it
  answers `404`, or, where a filter or a bulk change names it, the usual `400`
  saying there is no such project. It never answers `403`, which would say it
  is there, and it never answers differently from a project nobody made. The
  call to create an issue in a hidden project is a `404`, and in a visible one
  a `201`.
- **A link to a hidden issue opens a `404`.** Someone sent a link to `BBB-1`
  and cannot see project BBB: they get the same `404` they would for an issue
  nobody filed.
- **A key reads through its owner.** It sees what the person who owns it sees,
  on the same routes and with the same answers. A key with no owner is
  unbounded: it sees every project, as the wall being off does. A route a key
  may not call at all, such as every playbook edit, answers `403 key_not_accepted`
  to it whatever the project, before scope is read.
- **A parent link with a hidden side is told as a flag.** A reader who can see
  `AAA-2` and not its parent in project BBB is told the parent is there, and
  nothing more: the card carries no parent key and no title, and a flag says
  the parent is outside the scope. The same holds the other way round: a parent
  whose child is in a hidden project says some child is, with no key and no
  count. A level, priority, danger reason, runner tag, omitted runner, local
  model or pull that came from a hidden ancestor still applies to what the
  reader sees, because the loop acts on it. It shows no ancestor key, and a
  flag says it came from outside the scope. `hatch show` says it in a
  sentence.
- **A key is dispatched what an unbounded key is.** The loop reads the whole
  tree, whatever the owner's scope: a level on a hidden ancestor counts, and a
  parent is never closed over a child the key cannot see. The issue it is
  dispatched is still one it can see.
- **The Runners page leaves out what the caller cannot see.** A runner may hold
  a claim on an issue of a project the caller is scoped out of. That claim is
  dropped from the caller's view of the runner, so the page does not name an
  issue the caller cannot open.

One test holds this for every route. `ScopeRouteMatrixTests` boots the real
server with the wall up and asks every Hatch route as a person and as a key, for
a scope of `none` and a scope of `some`. A route added without being listed in
it fails the build.

## Runners and their keys

A runner is a key: the credential a program carries instead of a person,
`Authorization: Bearer hatch_ak_…`, plus its config - the board's settings for
it (state, caps, tags, projects), kept in `RunnerConfigs` against the key's id.
The heartbeat table a running loop writes is a bridge, joined to the key by
name, that HA-1937 retires.

- **Where it is minted.** A key is minted only by *creating a runner*
  (`POST /api/runners`), by a signed-in person - a user for themselves, an
  admin for anyone: no key may mint, rotate, revoke or re-own a key, only a
  person. The Runners page's **Create runner** button is shown to a user as
  well as an admin, and not with the wall off. The dialog takes **Name** ("What the audit trail will call it") and,
  for an admin, **Owner** (hint: "Whose tickets this key's --mine reaches. The key sees only
  the projects its owner sees; with nobody, it sees every project."), then
  **Create**. A user's dialog has no Owner: it says the runner will be theirs, and the
  runner is. The secret is shown once, on the page, beside the two `hatch config`
  commands it needs next, and the response is sent `no-store`, so it is never
  shown again. A name already held, by a key revoked or not, is refused. An
  admin can change the owner later, on the runner's Identity card, which
  carries the same hint under the select.
- **A key stops with its owner.** Deleting the owner revokes the key. Setting
  the owner to `pending` refuses the key with `403 pending_approval` until the
  owner is `user` or `admin` again, and revokes nothing. A key an admin made
  with no owner is neither: it has no owner to lose.
- **Inside its owner's scope.** A key reads through its owner's
  [scope](#scope): its `next`, `queue`, `review`, hops, verdicts, merges,
  landings and closes touch only the projects that person can see, and an
  issue outside them answers `404`. An owner scoped to `none` leaves the key
  with nothing to claim. A key with no owner, or a Hatch with the wall off,
  is unbounded. The merge train follows the same rule: a project is driven
  only by a runner whose owner can see it, so a runner scoped out of a
  project is never elected its driver, however long it has run, and a peer
  scoped out of a project is not counted as able to take its tickets.
- **Replacing a secret.** The runner's owner, or an admin, rotates a runner with
  `POST /api/runners/{name}/rotate`; a new secret is made at once and
  shown once, sent `no-store`; the old secret is refused from the next call (a
  `401`), so every loop using it needs the new one; the name, owner, tags,
  bounds, sessions, omissions and attribution carry over, and the key's prefix
  and a `rotatedAt` time are updated; a revoked runner cannot be rotated
  (`409`) — rotate to replace a leaked secret and keep the runner working,
  revoke to stop it. [`api-reference`](api-reference) has the route. On the
  Runners page these are Rotate key and Revoke on a runner's own Identity
  card, for the runner's owner or an admin.
- **What a key reaches.** Every route of the board's own API (not sign-in, `people` or `ui-logs`), less the routes that
  take a person only. Those refuse a key outright — on a walled Hatch with
  `key_not_accepted`, and with the wall off they refuse any program the same
  way. Among them: a ticket's assignee, priority, hold and pre-approval; its
  WIP limit and the WIP section; playbooks, per project and per ticket — the
  one entry here that also refuses a call with no key and no browser's
  `Sec-Fetch-Mode` mark, so "person" means a person at the
  browser specifically, not merely a signed-in session with no UI; a
  column's settings; a project's repositories, logo, landing and auto-heal
  settings, playbook source and members; Hatch's own settings; a runner's
  record; attachment delete; choosing a home screen; linking and unlinking a
  Slack account, which only the person it speaks for may do; running a backup.
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
- **Who may revoke or rotate.** The runner's owner, or an admin. A user gets
  `403` on a runner that belongs to someone else, with a sentence naming the
  owner, and on a runner that belongs to nobody: such a key sees every
  project, so only an admin may press it. A key is refused both routes.
- **Where the key goes:** never into the artifact — [the runner](the-runner#where-the-key-never-goes)
  says where it stays instead.

## 401 and 403

Two refusals a program meets at `/api`, and they say different things. Browser
navigation to the board on a walled Hatch does not meet either: it is sent
to sign-in instead.

- **A `401` is the wall.** Either a key was sent and refused — never minted,
  mistyped, revoked, or its owner was deleted — or no key was sent to a Hatch that has its wall on.
  The CLI tells the two apart:
  - No key sent: ``hatch: 401 - no key was sent and this Hatch has its wall on. Run `hatch config`.``
  - Key sent: `hatch: 401 - the key was not accepted. Minted, not revoked, copied whole?`
- **A `403` is a gate.** The wall let the caller in and a rule behind it said
  no. There are two kinds.
  - **The role gate** answers `{ "error": "<code>" }`, with one of:
    - `pending_approval` — the person, or the owner of the key, has not been
      let in yet, and is refused at every level.
    - `not_admin` — the person's role is below what the route asks for.
    - `no_person` — an enrolled device nobody has claimed.
    - `key_not_accepted` — a key reached a route that takes no key.
  - **A project or person-only refusal** is a bare sentence rather than a
    code:
    - `only an owner or approver of <KEY> may move work into "<column>"`
    - `only an owner of <KEY> may add or remove its members`
    - `only an owner of <KEY> may change how its work lands`
    - `only an owner of <KEY> may change whether its playbooks are sourced from its repository`
    - `only an owner of <KEY> may set its level`
    - `only an owner or approver of <KEY> may set pre-approval - it is the approve press given in advance`
    - `setting an issue's pre-approval level is a person's call, not an agent's`
    - `moving work into a protected column is an owner's or approver's call, not an agent's`
    - `only <name>, or an admin, may control <runner>`
    - `only an admin may create a runner for somebody else`
- **What the CLI prints for a `403`** is the API's own reason: a role-gate
  code prints as itself (`hatch: 403 - not_admin`), and a plain-sentence
  refusal prints that sentence (`hatch: 403 - only an owner or approver of
  AER may move work into "approved"`). The one exception is `key_not_accepted`,
  where the fixed sentence `hatch: 403 - the key is good and this route is
  not one it may take (CLAUDE.md).` replaces the code outright. An empty body still
  gets a fixed sentence, worded by whether a key was sent at all.

## Whose tickets `--mine` reaches

- **Who sets it.** A user's own runner is theirs from the moment they create
  it: with the wall up, a user who creates a runner owns it, and may name only
  themselves as its owner. Only an admin makes a runner for somebody else or
  for nobody, and only an admin changes an owner later, from a runner's
  Identity card on the Runners page — a key may never set its own. **Owner**, on the create dialog and the
  Identity card, lists people only and is pre-selected to the admin at the
  screen; left as is, that admin becomes the key's owner. The **Owner** select
  on an existing, unrevoked row also offers `nobody`; a revoked row shows its
  owner as text only.
- **The owner also sets what projects the key sees.** The key reads through
  its owner's scope, as above. `nobody` is unbounded: a key with no owner sees
  every project, though it has no tickets of its own for `--mine`.
- **Whose tickets it reaches.** The signed-in person; failing that, the key's
  owner; failing that, the local person where the wall is off. `--mine` takes
  only a ticket assigned to that person, or to the key itself, and folds
  every other one.
- **A key that belongs to nobody has no tickets of its own**, and is refused
  before a `--mine` pass scans anything: an admin sets it on the Runners
  page, from the runner's Identity card.

[One increment and the loop](one-increment-and-the-loop#sharing-a-board) says
more about sharing one board between people and keys.

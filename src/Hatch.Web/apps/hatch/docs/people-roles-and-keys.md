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

[Putting a front door on it](putting-a-front-door-on-it) turns the wall on;
`the-wall-off-on-and-required` will say what each state changes.

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

## Coming

Pages this one links to that are not written yet:

- `the-wall-off-on-and-required`
- `review-pull-requests-and-the-merge-train`

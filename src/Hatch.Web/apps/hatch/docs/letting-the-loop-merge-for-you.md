---
title: Letting the loop merge for you
section: Running agents
order: 70
summary: Turning on a column the runner merges approved pull requests from, with no session.
---

The runner above opens a pull request and leaves it in review — it never
merges one itself. The next thing worth doing once it's running is this:
teach the board a column where moving a ticket by hand *is* the approval,
and the runner merges its pull request from there with no session and
nothing spent.

A project chooses how its work lands: by pull request, which this section is
about, or straight onto the trunk. It is set on **Manage → Projects**, edit,
**Landing** — an owner's alone — as *By pull request* or *On the trunk*, beside
a *Checks are required* box. Step 3 says which to pick.

**1. Add the column.** On **Manage → Statuses**, add a column — call it
`Approved`, or anything else; every name here is a placeholder, and nothing
hangs on it being this one — positioned between `In Review` and `Done`.
Tick **Protected** first, then **Merges PR**: the second checkbox refuses to
tick until the first already has ("is not protected - tick that first"). The
page's own legend says what each one does:

- **Protected** — "Only an owner or approver of the issue's project may move
  work into this column by hand - never a key, never a hop, never a
  dispatch."
- **Merges PR** — "The runner merges an issue's pull request once it stands
  here, current and green, with no session. Needs Protected ticked first."

**2. Add an approver.** **Manage → Projects**, edit the project, and open
its **Members** section. If nobody has claimed the project yet, a **Claim**
button sits above the member list — press it first. Then add whoever should
approve, with a role of `owner` or `approver`, either one, from the same
dialog. An approver here is always a *person* — a key can never move work
into a protected column, by the gate's own design: "moving work into a
protected column is an owner's or approver's call, not an agent's".

**3. On the forge: which shape is the repository?** Three shapes of
repository are common, and which one you have decides what, if anything, to
configure. To tell: look at the rules on `<trunk>` — the repository's default
branch — in the forge's settings. No rules at all is the first shape; required
status checks and nothing else is the second; a required approving review is
the third. They are offered simplest first, and each says what it costs in
trust.

- **A trunk nobody protects.** Either set the project to land on the trunk
  (**Landing → On the trunk**, above), or leave it
  on pull requests and untick **Checks are required** when the repository has no
  CI. There is nothing to configure on the forge; the account the session's git
  pushes as needs push access, and that is all. *The cost in trust:* a landed
  commit reaches `<trunk>` before anyone reads it, and the only check is the
  trunk's build after the fact (landing on the trunk) or Hatch's own approval
  (pull requests).
- **Pull requests with checks and no review.** Leave the defaults. A person
  still approves in Hatch — the move into the column from step 1, or a
  pre-approval — and the train merges once the checks pass. No bypass is needed:
  the runner's merge is still requested with `--admin`, which a forge honours as
  a bypass only where something required is unmet, and here nothing is (the flag
  is always sent; the forge decides). *The cost in trust:* CI is the only reader
  of the code.
- **Pull requests with a required review.** The runner merges a pull request
  with `gh pr merge --merge --admin --match-head-commit <sha>` — exactly what it
  runs once Hatch's own gate has approved the move. `--admin` is what lets that
  succeed even though nobody clicked **Approve** on the forge's own pull request
  review: the approval already happened on the board, in the move into the
  protected column, not on the forge. So, on `<your-org>/<your-repo>`:
  - Create a ruleset on `<trunk>` requiring one approving review and the
    required status checks — the ordinary protection everybody else's push still
    has to clear.
  - Add the account `HATCH_GH_TOKEN` signs `gh` in as to the ruleset's
    **bypass list**. Without it, the same rule that protects everyone else's
    pushes would also refuse the runner's merge, and the approval that already
    happened on the board would have nowhere to land.

  *The cost in trust:* the bypass means anyone who may move a ticket into the
  merge column gets a merge past the review rule — which is why the column is
  protected, and why its approvers are owners or approvers, never a key.

Hatch asks for its own approval in every shape that opens pull requests,
whatever the forge requires.

A ticket that finishes with no code has no pull request to merge. Beside **A
passing check approves the merge**, an owner can turn on **A no-code
declaration closes the ticket** on **Manage → Projects**, edit - it carries a
ticket that declared (`hatch pr --none`) from review to Done on the next pass,
with no person and no session. Off, only a pre-approved declared ticket is
carried, and the rest wait for a person. The merge setting does not do this.

A third flag beside those two, **Runners work contributions**, decides whether
a runner may push to a contribution's branch - resolving its conflicts, fixing
its failing build - or leaves a contribution in review for a person. It is off
by default, so no runner pushes to somebody else's branch unless an owner opted
in; while it is off, such a contribution is listed under **Contributions
runners will not work** in the attention panel.

**4. `HATCH_GH_TOKEN`.** The same `.env` beside `compose.yaml` as *Putting a
front door on it*, above it on this page. Compose turns it into `GH_TOKEN`
for `gh`, and without it the runner's own entrypoint already says so once:
"gh is not signed in - build checks and merges are off. Set HATCH_GH_TOKEN
to turn them on."

**5. What a merge looks like on the ticket.** Once a pull request is
approved — moved into the column from step 1 — and its branch holds the
trunk with a passing build, the merge train notices and merges it with no
session, no agent and nothing spent, on its own clock — a few seconds apart
by default, separate from the poll that rereads the build and conflict
checks in the first place. It leaves one comment on the ticket, `Merged its
pull request at <sha>, as <runner>.`, and the terminal where the runner is
running shows its own line for the same event: `<key> pull request merged
-> <column>`.

**6. More than one approved ticket in the same project.** Approve two or
more at once and they do not race each other through: they queue as a
**train**, one per project, ordered the same way the board already orders
everything else — expedited before normal, then by rank. Only the train's
*head* is the one being acted on; everything behind it is waiting its turn,
not held up by anything of its own.

See it in two places. The web's work queue groups a project's approved
tickets together, labels the head, and names the runner driving that
project's train beside it. `hatch queue`'s own plain text says the same
thing on a waiting row's own line, in full: `"... - waiting its turn behind
HA-12, position 2 in the train"`.

Only one runner acts on a given project's train at a time — not every
runner you have pointed at the board, but the one the control plane elected
to drive it. See which on the **Agents → Runners** page, beside that
runner's own row: `driving HA, AER`, or `driving nothing` if nothing of yours
has a driver right now. You do not configure this yourself, even running two
or more runners against the same board — it is the election's own call to make.

Whose runner may drive a project depends on the project. With sign-in on, a
project that has an owner is driven only by a runner working for one of its
owners or approvers, since theirs are the forge credentials that can make the
merge; a runner on anyone else's key, or on a key nobody owns, never drives it,
however long it has run. When none of a project's owners' or approvers' runners
is up, the project reads `driving nothing` on the **Agents → Runners** page
and the work queue says "no runner is driving it", and its approved pull
requests wait for one rather than pass to a runner that cannot merge them.
Only the train's own acting waits, though: a pull request somebody merged by
hand closes its ticket with no driver, on any runner holding a checkout of the
repository, whether or not the branch is still on origin.
Make somebody an owner or approver, or give a runner's key a different owner,
and the change takes effect on the runners' next heartbeat; nothing restarts.
A project with no owner, or a Hatch with sign-in off, lets any runner holding
a checkout drive.

**7. What to do when a row holds.** Some things stop an approved pull
request from merging, and they show up rather than sit silent: the nav
bar's attention panel grows a section, **Approved, not merging**, between
*Pull requests to review* and *Questions to answer* — each row names the
issue and says why, in its own sentence: a closed pull request, more than
one branch on origin named for it, no branch on origin named for it, a
pull request that changes a playbook file or `.hatch/dependencies.json` —
a person merges either on the forge after reading it, whatever the
ticket's pre-approval — or — rarer — a branch that holds the trunk but
never got a check run before a ten-minute settle window closed. A
dependency-file row that asks for something no live runner has says so on
the end of its sentence. The issue page draws the same sentence as a chip
beside the pull request link, for that ticket alone.

A branch that starts conflicting with the trunk, or whose build fails, is
not one of these — the loop already pushes it straight back to review on
its own, and it never reaches this panel at all. For everything that does
land here, the fix happens on the ticket or the branch, never by anything
typed into GitHub's own UI.

## A passing check approves the merge

Step 1 above already has a person drag a ticket into the merge column to
approve it. This is the same approval, given by a playbook instead of a
hand, once a project turns a setting on. A check row judges the branch — the
same thing a person reads before dragging it — and its own pass carries the
ticket into the merge column with nobody reading the diff first. The trade
is the same column's: ticking **Protected** on the merge column moves every
project's review landmark one column left — the dispatcher steps back over
any protected column it meets walking left from the first terminal one — so
a project that leaves this setting off still waits in review for a person's
drag, exactly as it always has.

**1. The merge column.** Reuse the column from step 1 above — Protected,
then Merges PR. Nothing further is added here; the check lands a ticket in
the same column a person's drag would.

**2. Two rows on the Playbooks page.** **Agents → Playbooks**, not
**Manage**. Both rows name the review column — call it `In Review`, or
anything else; nothing hangs on it being this one — as `From`:

- The stock row, `In Review → In Review`, still has to cover the type being
  checked. It is the seeded fixup that only fixes a conflict or a failing
  build on the branch, and nothing else — leave it alone.
- A second, new row, `In Review → Approved` — `Approved` standing for the
  merge column from step 1 — with **Is check** ticked. `Is check` is what
  tells the apply to land a pass in the merge column rather than back in
  review.

**3. The project's own setting.** **Manage → Projects**, edit the project,
**Landing** section — the same one the by-pull-request/on-the-trunk choice
and the repo-sourced-playbooks checkbox sit under. Tick *A passing check
approves the merge*. Its own hint, verbatim:

> A check playbook's pass out of the review column lands the ticket in the
> protected Merges PR column, and the runner merges it from there. Off, a
> person drags it there.

While no column on the board is yet both Protected and Merges PR, a warning
sits beneath it, verbatim:

> No column on the board is both Protected and Merges PR, so this does
> nothing until one is.

**What a ticket's trail shows afterwards.** The History tab's line for the
move:

```
In Review → Approved, approved by a passing check
```

and, beside the findings comment every check apply posts, the approval
comment:

```
Approved by In Review → Approved's passing verdict.
```

**Turning it off.** Nothing above fires: a clean branch waits in review
exactly as it does today, for a person's drag; the check row stops matching
the move, and the review column's own self-move — the conflict-and-build
row — is all that is left to match.

---
title: Review, pull requests and the merge train
section: Concepts
order: 70
summary: Where a branch stops being one session's and becomes the project's, and what the pull request recorded on an issue is for.
---

Review is where a branch stops being the session's and starts being the
project's. Most of what happens here is asked of git and the forge by the
runner, never of a session: whether the branch merges with the trunk, whether
its build passed, and what the forge says of its pull request are all read
without anyone spending an increment on them.

## The pull request on the issue

An issue holds one pull request address, and it does three jobs.

- **It is the one place the ticket says where the work is being reviewed.** It
  is a single URL rather than a list: it answers where the review is *now*, and
  every earlier value is already in the issue's event log. It is what a person
  opens six months later, and what [`hatch pr`](cli-reference#pr) prints for a
  key.
- **Recording it is part of finishing.** An increment that ends in review
  having pushed a branch with no pull request recorded is refused as
  unpublished: a comment on the ticket says so, and the second time in a row
  the board asks whether one has been opened.
- **The forge's own account of it is kept beside it.** Its merge state, its
  review decision and the head commit it was read at are stored with the
  address, and the merge train reads that.

The address does not drive the checks on the branch. Neither the merge check
nor the build is read from it — the merge check is made on the issue's
branch, and the build is asked of the forge for the repository — so a pull
request nobody recorded still has its build read.

## The build check and the merge check

Code decides here, not a prompt. The runner asks git whether the branch merges with the trunk, asks the forge how the build on its tip came out, and writes each answer onto the issue; the board reads what the issue holds and applies one rule. Both answers are kept per repository, because a project may bind several and a passing one must not hide a failing one.

The merge check says whether the branch merges with the trunk. It has four verdicts:

| Merge check | Means |
|---|---|
| `clean` | the branch merges with the trunk; nothing for a session to do |
| `conflicted` | it does not, in the files named |
| `none` | no unmerged branch on origin is named for the issue |
| `ambiguous` | two or more are, and the board will not guess which is the issue's |

The build check says how the checks on the branch's tip came out. It is about one sha, since a verdict about any other sha says nothing about the branch as it stands. It also has four verdicts:

| Build check, about one sha | Means |
|---|---|
| `passed` | every check concluded, none failed |
| `failed` | every check concluded and at least one failed; each failing check is named, with a link |
| `pending` | something is still queued or running; nothing is fixed until every check has concluded, so one increment sees every failure at once |
| `none` | no check ran on the sha; a push's checks take a moment to appear, so the runner keeps asking for ten minutes before it believes it |

An issue in review is dispatched on these two and nothing else. The board reads them in this order:

- Any `conflicted` is conflict work, and it outranks the build. The merge commit gives the branch a new sha, so the build's answer is about to be stale, and a conflicting pull request often gets no CI run at all.
- `none` and `ambiguous` are folds that say why, not work.
- Only a `failed` build, on the sha the merge check read, in a repository that merges cleanly, is build work.
- `pending`, a verdict about a different sha, no verdict at all, `passed` and `none` are all nothing for an agent to do.

A runner that cannot read builds - no forge CLI, not signed in, a host it cannot reach - costs nothing: the issue is explained as it was before builds were read.

Neither check costs an increment. The runner takes both on a clock between increments, asking `ls-remote` first and fetching only where a branch moved, and only a conflict or a failed build is ever handed to a session. A flaky check on a branch is a failure, and the board does not re-run it. A build that fails again on the tip an increment itself pushed is not sent to another agent: the board marks the ticket stalled and resumes it after a pause, and holds it for a person after several in a row. [One increment and the loop](one-increment-and-the-loop#stalls) has the counts.

## When review is a session's

The review row is the one row whose two ends are the same column — [the ten rows Hatch seeds](playbooks#the-ten-rows-hatch-seeds), the last row of that table, an *edits* row. One playbook, at its own model and effort, answers both the conflict dispatch and the build dispatch.

The runner adds one of two sections to the prompt depending on which; [what a session is told](what-a-session-is-told) has the fuller treatment of each. A conflict dispatch gets a `## The conflict` section — the branch, the trunk, both shas and the files that conflict — and the session starts on the branch with the trunk merge already in progress. A build dispatch gets a `## The failing build` section — the branch and its sha, each failing check named with its link, and up to 150 lines of its log ending at the last `##[error]` line, the lines leading up to the failure and not the cleanup that follows it — and the session starts with the trunk already merged in and no merge in progress.

The playbook tells the session to read whichever section is there and find the failure, then fix that and whatever it broke — and nothing else, since this is not the increment to improve the change. It builds and tests with the repository's own commands until they are green, commits and pushes to the same branch, and never rebases, force-pushes, or pushes to the trunk. It comments the new sha on the ticket and leaves the ticket in the column it is in. A check that turns out flaky rather than broken is still reported on the ticket, not papered over.

It is not a check. A check playbook is the one kind of row that cannot move its own ticket at all; the review row can move its ticket, and is told not to. See [playbooks](playbooks) and [the lifecycle of a ticket](the-lifecycle-of-a-ticket).

Starting and ending in the same column, such an increment cannot be judged by whether it moved the ticket the way an advance is. A conflict increment is judged by the verdict git gives the branch on origin afterwards: nothing conflicts is resolved, not a stall; still conflicts is a stall, naming the files. A build increment is judged first by whether it pushed, since CI has not run by the time it ends: a new tip reads as "fix pushed, build pending"; a tip that did not move is a stall. Before either is spawned, the runner re-reads the branch, or the build on its tip, and spawns nothing if the problem it was dispatched for has already gone.

## Protected columns, and who may move into them

A protected column is one of the flags [Column flags](the-board-and-its-columns#column-flags) defines, alongside **Merges PR**, which always carries it too — this section is about who may act once a column carries it, not about the flag itself.

Only an owner or an approver of the issue's project may move work into a protected column by hand — never a key, never a dispatch. A key attempting it is refused outright, whatever column: moving work into a protected column is an owner's or approver's call, not an agent's. A person without the role is named instead: only an owner or approver of the project may move work into it.

Of the board's own hops, only the approve hop crosses a protected column's gate. A pre-approval is that owner's or approver's own permission, already given on the ticket in advance; the hop carries it out the moment the board can see it is true, and decides nothing for itself. Every other hop that would land in a protected column is refused the same way a person without the role is.

Trying the move any other way answers with a 403 — the gate working, not a scope being wrong.

## The merge train

Every ticket standing in a project's merges-PR column takes a place in that project's own train — one queue per project, never a board-wide one, so two projects' pull requests never wait on each other. The order is the same priority tiering the rest of the board uses: [priority](issues#priority) first, then rank. An expedited ticket therefore goes ahead of a normal one even when it sits behind it in rank.

The head of the train is the first ticket in that order that is neither held nor being sent back to review. A ticket is held, and stepped over, when its pull request is closed or still a draft, when its branch is missing or ambiguous, when its pull request changes a playbook file — a person merges that one — or when its tip has had no checks past the settle window, on a project that requires them, and the forge does not call it clean. A ticket is sent back to review, and stepped over, when its branch conflicts with the trunk or its build failed on the sha just judged clean. A build that is still running is neither: that ticket stays the head, and the train waits on it until it settles. Every other ticket in the column shows a sentence naming the head and its own position behind it — `hatch queue` prints it too — and is not acted on until its turn comes, however actionable its own ticket would otherwise read.

Only the head is touched on a tick. A clean branch whose build passed merges; a clean branch that is behind the trunk is brought current with it first, and waits for the next tick. Nothing behind the head moves, whatever its own branch and build would otherwise allow.

None of this spends an increment or a session. The runner acts on its own clock, separate from the clock that reads the build and merge checks above — [`--merge-interval`](cli-reference#go-to-work) against `--interval`. A ticket can therefore be merged under a session that is still working it: if that session's own earlier push already left the branch clean and green, and its turn in the train had come by the time the train ran, the train merges it. That is the design, not a race to fix.

Adding the column, naming an approver and choosing the forge's own shape are covered start to finish elsewhere and not repeated here: [letting the loop merge for you](letting-the-loop-merge-for-you).

## Landing on the trunk

A project's landing setting — an owner's choice, on **Manage → Projects** — can read *on the trunk* instead of *by pull request*. A ticket in such a project never carries a pull request: review is still where its branch stops being a session's own, but there is no column to merge it into and nothing for the train to read. If a pull request is recorded on one anyway, that ticket goes back under the pull-request rules above.

A session in such a project pushes its branch's commit onto the trunk directly, and then records where that landed in place of opening a pull request: `hatch landed <key> [<sha>] [--repo <path>]` writes the sha, the trunk ref and the repository the work reached it in. [`hatch landed`](cli-reference#landed) has the command in full.

The two checks above are about a branch, and a landing has none, so the question changes: it is about the trunk itself. Once a runner has confirmed the commit's sha is on the trunk, what is judged is whether the trunk's own build passes there — read off whatever commit currently holds the trunk's tip, not the sha that first landed. A commit that landed clean and was later followed by a broken one therefore reads as held until the trunk is green again, and nothing says which commit broke it. A trunk build that read no checks at all settles after the same settle window the merge section names: it holds where the project requires checks, and lands where it does not.

There is no pull request to merge, so nothing merges it. The ticket is carried straight into its project's first terminal column the moment the trunk holds the commit and the trunk's build there passes, with nobody pressing anything — the same kind of automatic step a merged pull request takes elsewhere. A comment on the ticket says so, naming the trunk and the sha.

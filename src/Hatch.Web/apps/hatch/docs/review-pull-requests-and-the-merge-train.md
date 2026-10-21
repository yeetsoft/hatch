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

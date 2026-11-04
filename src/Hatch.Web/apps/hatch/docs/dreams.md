---
title: Dreams
section: Concepts
order: 25
summary: A line worth keeping before it is work: where it is captured, how it is found, and what it means for one to be woven into an issue.
---

The **Dreams** screen, at `/dreams` and first in the Track group of the primary nav, keeps the
lines somebody wanted said before they are work. An [issue](issues) is work with a status; a dream
is what comes before one.

## What a dream is

A dream is a line somebody wanted said - an idea, a complaint, a want - captured with no ceremony.
It may lean toward a project, or be left loose, leaning toward none.

A dream is not an issue. It carries no status, sits in no column and has no assignee, so the board
never dispatches it and the loop never picks it up. It is a place to put a thought down until it is
worth turning into a ticket, or never is.

## Finding one

The search box matches any word, not every word: a query of two words finds a dream that has either.
That is unlike the Search page, which wants all of them. The results are narrowed by project, and by
a loose-only toggle that keeps only the dreams no issue has drawn on.

The URL carries the search, so a link to a query is a real link: paste it to somebody and they see
the same dreams.

## Woven into an issue

An issue can be recorded as having drawn on one or more dreams. That is what **woven into** means,
and it is a record, not a move: the dream stays where it was, and the issue keeps the list. A dream
woven into an issue shows a chip linking to that issue; a dream with no issue woven into it is a
loose one. The call that writes the record is `PUT /issues/{key}/dreams`, described in the
[API reference](api-reference).

## The weave press

The weave press turns a handful of dreams into a filed epic.

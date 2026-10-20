---
title: API reference
section: Reference
order: 30
summary: Every route under /api/hatch, grouped by resource, with who may call it and what it answers.
---

Every route Hatch answers lives under `/api/hatch`, JSON in and JSON out. This
page groups them by resource: the board's own resources first, then the
loop's (`cli-reference` is the easier way to make most of these calls; this
page is for the ones it has no verb for).

## How to call it

A person's browser carries a session cookie; a program sends `Authorization:
Bearer hatch_ak_…`. [People, roles and keys](people-roles-and-keys) covers
minting a key and what its one scope reaches - this page only says, route by
route, who may call it.

**Issue routes take the display key** - `/issues/{key}`, case-insensitively,
never a database id. Project and column routes take an int id -
`/projects/{id}`, `/statuses/{id}`. A person id is a guid -
`/projects/{id}/members/{personId}`.

```
hatch api GET /api/hatch/issues?statusId=2
```

`cli-reference` has `hatch api` in full.

`docs/hatch-planning.md` ships inside every Hatch image, so a session can read
it on the machine it is working on. It carries the filing shapes in full -
`parentKey`, `readyAt`/`dueAt`, the `issues` filters, `/issues/bulk` and the
`/plan` reads - none of which are repeated here.

## Projects

| Method | Path | Who | What it does |
|---|---|---|---|
| GET | `/projects` | key | Every project, with issue count, repositories, logo timestamp, members, `canApprove`/`canAdminister` for the caller |
| POST | `/projects` | key | Creates one; the creating person (if any) becomes owner |
| PATCH | `/projects/{id}` | key | Renames and/or rekeys; both optional, null leaves each alone |
| DELETE | `/projects/{id}` | key | Deletes an empty project |
| GET | `/projects/{id}/repositories` | key | The bound remotes, in order, primary first |
| PUT | `/projects/{id}/repositories` | **person** | Replaces the whole ordered list |
| GET | `/projects/{id}/logo` | key | The image bytes |
| PUT | `/projects/{id}/logo` | **person** | Sets it (raw body, detected content-type) |
| DELETE | `/projects/{id}/logo` | **person** | Clears it |
| GET | `/projects/{id}/members` | key | Who owns/approves |
| PUT | `/projects/{id}/members/{personId}` | **person** | Sets one member's role, adding them if new |
| DELETE | `/projects/{id}/members/{personId}` | **person** | Removes a member |
| POST | `/projects/{id}/claim` | **person** | Takes ownership of a project with none |
| PUT | `/projects/{id}/landing` | **person** | Sets `landing`, `checksRequired`, `onePullRequest` default, and the four `autoHealBug*` fields |
| PUT | `/projects/{id}/playbooks-source` | **person** | Toggles `repoSourced` |

`ProjectCreateRequest` is `Key`, `Name`, an optional `Color` and `Icon`.
`ProjectPatchRequest` is the same four fields, all optional - null leaves a
field alone on the patch, `""` clears `Color`/`Icon`. The repositories PUT
body is a list of `ProjectRepositoryWriteRequest(Remote, BaseBranch?)` - the
whole ordered set, not a delta. A members PUT body is
`ProjectMemberWriteRequest(Role)`, `"owner"` or `"approver"`. The landing PUT
body is `ProjectLandingWriteRequest`: `Landing?`, `ChecksRequired?`,
`OnePullRequest?`, `AutoHealBugEnabled?`, `AutoHealBugPriority?`,
`AutoHealBugPreApproval?`, `AutoHealBugStatusId?` - `AutoHealBugStatusId` is
the one field with a clear sentinel, `""`. The playbooks-source PUT body is
`ProjectRepoSourcedWriteRequest(RepoSourced)`. Answers are a `ProjectDto`
throughout, and a list of `ProjectMemberDto` from the member and claim
routes.

**Refusals worth naming.** DELETE a project 409s if it still holds issues,
and separately 409s if a playbook still names it. A members PUT 403s if the
caller is not a live owner, 400s on an unknown role or person, and 409s if
the edit would leave the project with no owner at all - the one state only
`/claim` escapes. POST `/claim` 409s if the project already has an owner. The
landing PUT 403s if the caller is not an owner; `landing` is `"pull-request"`
or `"trunk"`, anything else 400s; a story that has already started work keeps
its own `onePullRequest` fixed rather than inheriting a new default. The
playbooks-source PUT 403s if the caller is not an owner, and 400s if the
project binds no repository or any playbook row still names it.

## Columns

| Method | Path | Who | What it does |
|---|---|---|---|
| GET | `/statuses` | key | Every column |
| POST | `/statuses` | key | Creates one |
| PATCH | `/statuses/{id}` | key | Name, sort order, color, `isTerminal`, `isDeferred` |
| DELETE | `/statuses/{id}` | key | Deletes an unused column |
| PUT | `/statuses/{id}/express-skips` | **person** | Ticks/unticks whether an express issue skips this column with no session |
| PUT | `/statuses/{id}/parent-pulls` | **person** | Ticks/unticks whether a child is pulled through this column while its parent is in implementation |
| PUT | `/statuses/{id}/agent-files` | **person** | Ticks/unticks the one column a program's issues are born in |
| PUT | `/statuses/{id}/implementation` | **person** | Ticks/unticks the one column where code is written |
| PUT | `/statuses/{id}/protected` | **person** | Ticks/unticks who may move work into this column |
| PUT | `/statuses/{id}/merges-pull-request` | **person** | Ticks/unticks whether the loop merges the pull request once a card stands here, current and green |

[The board and its columns](the-board-and-its-columns) says what each flag
means; this page says which route sets it.

`StatusDto` carries `Id`, `Name`, `SortOrder`, `IsTerminal`, `IsDeferred`,
`IsWip`, `Color`, `ExpressSkips`, `ParentPulls`, `AgentFiles`,
`IsImplementation`, `IsProtected`, `MergesPullRequest`. `StatusCreateRequest`
and `StatusPatchRequest` share a shape - `Name`, optional `SortOrder`,
`IsTerminal`, `Color`, `IsDeferred`. The six flag writes each take one
required boolean: `ExpressSkipsRequest(ExpressSkips)`,
`ParentPullsRequest(ParentPulls)`, `AgentFilesRequest(AgentFiles)`,
`ImplementationRequest(IsImplementation)`, `ProtectedRequest(On)`,
`MergesPullRequestRequest(On)` - the last two take a field named `On`, not a
field named after the flag.

**Refusals worth naming.** A PATCH 409s on a duplicate name, and 400s setting
`isTerminal: true` while `MergesPullRequest` or `IsProtected` still holds -
untick those first. `agent-files` and `implementation` 400 turning on over a
deferred or terminal column, and ticking one column auto-clears every other -
at most one of each ever holds. `protected` 400s turning on over a deferred
or terminal column, and 400s turning off while `MergesPullRequest` still
holds. `merges-pull-request` 400s turning on over a deferred or terminal
column, or one not yet `protected`. DELETE 409s if the column still holds
issues, a playbook names it (from or to), a project's auto-heal points at it,
a verdict was recorded against it, or it is the board's last column.

## WIP

| Method | Path | Who | What it does |
|---|---|---|---|
| GET | `/wip` | key | The section: flagged columns, and the two slices (`story,bug` then `epic`) with their limits |
| PUT | `/wip` | **person** | Sets the limit, the epic limit, and/or the whole section's columns |
| PATCH | `/issues/{key}/wip` | **person** | Sets an epic's own limit on how many of its stories run at once |

[The board and its columns](the-board-and-its-columns) covers what the
section and its limits mean.

`WipSectionDto` is `StatusIds` and `Slices`, each a `WipSliceDto(Types,
Limit?)`. The section PUT body, `WipSectionRequest(Limit?, StatusIds?,
EpicLimit?)`, leaves a field alone when it is absent; `""` or whitespace
clears a limit, a positive whole number sets it; `StatusIds`, when sent, is
the whole section, not a delta, so `[]` clears it. The per-issue PATCH body,
`IssueWipLimitRequest(Limit?)`, follows the same clear rule.

**Refusals.** The section PUT 400s a limit that is not blank or a positive
whole number, and 400s naming a deferred or terminal column in `StatusIds`.
The per-issue PATCH 400s if the issue is not an `epic`.

## The board

| Method | Path | Who | What it does |
|---|---|---|---|
| GET | `/board` | key | Every column and every card in the house, in one request - the WIP meter alongside it, read from the same instant |

The answer is a `BoardDto`: `Statuses`, `Cards` (each an `IssueCardDto`), an
optional `Wip`, an optional `DoneHorizonHours`. There is no write here, and no
refusal beyond the standard ones.

## Issues

| Method | Path | What it does |
|---|---|---|
| GET | `/issues/{key}` | One issue, the full `IssueDto` |
| GET | `/issues` | Cards matching a filter - no parameters at all is the whole board |
| POST | `/issues` | Files one; lands leftmost/bottom unless a program's agent-files column exists |
| PATCH | `/issues/{key}` | Edits whichever fields the body names; null leaves a field alone |
| POST | `/issues/bulk` | One edit applied to many issues by key |
| DELETE | `/issues/{key}` | Deletes it; children are outdented, not deleted |
| POST | `/issues/{key}/move` | The drag: a column and the neighbours it landed between |

All seven are a key's. `GET /issues` takes `projectId`, `type`, `statusId`,
`parentKey` (`""` means "no parent at all" - the one filter with no other way
to write it), `ancestorKey` (anything below at any depth, not the issue
itself) and `text` (a substring of the title, plus an exact key match), all
optional and ANDed together. `docs/hatch-planning.md` has these filters in
full, the bulk endpoint's own filters, and the `/plan` reads' arithmetic -
none of that is re-derived here.

`IssueCreateRequest` is `ProjectId`, `Type`, `Title`, an optional
`Description`, `ParentKey`, `ReadyAt`, `DueAt`, and `OnePr` - `OnePr` only on
a `story`, refused on anything else, `inherit`/`on`/`off`. `IssuePatchRequest`
is `Title?`, `Description?`, `Type?`, `StatusId?`, `ParentKey?`, `ReadyAt?`,
`DueAt?`, `PullRequestUrl?`, `ProjectId?`, `MoveDescendants?`, `WipOverride` -
`""` clears `ParentKey`, `ReadyAt`, `DueAt` or `PullRequestUrl` (no issue key,
date or URL is ever legitimately empty, so the sentinel is unambiguous);
`ProjectId` has no clear sentinel, since an issue always belongs to some
project; a null `MoveDescendants` reads as `true`. `IssueBulkEditRequest`
shares those fields, minus `Title`, `Description` and `PullRequestUrl`, which
only make sense one issue at a time, under `Keys`. Its answer,
`IssueBulkResultDto`, is `Changed`, `Unchanged`, `Failures` (each
`{key, reason}`) and `Rekeyed` (each `{from, to}`, for every issue a project
move actually carried, named or not). `IssueMoveRequest` is `StatusId`,
`AfterKey?`, `BeforeKey?`, `FromStatusId?`, `WipOverride` - the client names
neighbours, never a rank, and `FromStatusId` is a precondition: a card moved
since is refused rather than relocated.

**Refusals worth naming.** Create and patch 400 on an invalid title, type,
date or pull-request-URL shape, and on an illegal parent - wrong project, a
cycle, or a type pairing the issue hierarchy refuses. A move into a protected
column 403s unless the caller is a live owner or approver, never a program,
and into a merges-pull-request column 400s if the issue carries no pull
request URL. A column change that would overflow the WIP section 409s with a
`{error, load, limit}` body unless `wipOverride: true` - and `wipOverride`,
and a cross-project `ProjectId` on a patch, are person-only even on this
key-open controller: a program sending either is 403'd before the issue is
even loaded. A project move 409s if any issue in the moving set - the root,
or the whole subtree unless `moveDescendants: false` - holds a live claim.
The move route additionally 409s if `FromStatusId` does not match where the
card actually is, and 409s, as a plain string rather than the WIP body, if
the card's live claim belongs to a check playbook - a check session judges,
it does not move the ticket. Both create and a project-moving patch or bulk
retry up to five times on a number-minting race before answering 409 "could
not mint an issue number - try again".

## A ticket's own fields

| Field | Method | Path | Who | What it does |
|---|---|---|---|---|
| assignee | PUT | `/issues/{key}/assignee` | **person** | Sets/clears who it belongs to |
| priority | PUT | `/issues/{key}/priority` | **person** | One of six named levels |
| expedite | PUT | `/issues/{key}/expedite` | **person** | Legacy two-level alias for priority (`expedited`/`normal`) |
| pre-approval | PUT | `/issues/{key}/pre-approval` | **person** | One of three named levels |
| express | PUT | `/issues/{key}/express` | **person** | Legacy two-level alias for pre-approval (`express`/`none`) |
| one-pr | PUT | `/issues/{key}/one-pr` | key | `on`/`off`/`inherit`, stories only |
| release-note | PUT | `/issues/{key}/release-note` | key | The changelog's sentence for this issue |
| stall | PUT | `/issues/{key}/stall` | open to anything the wall admits | A runner's own mark that it left the ticket where it found it, and why |
| hold | PUT | `/issues/{key}/hold` | **person** | Parks/resumes a ticket; clears any stall mark |
| playbook | PATCH | `/issues/{key}/playbook` | **person** | Per-ticket model/effort override, beating every playbook |
| landing | PUT | `/issues/{key}/landing` | key | What a repository holds for this issue's branch, per remote |

Several of these are a person's alone; checked one at a time rather than
assumed uniform. `one-pr` and `landing` are key-open despite sitting beside
five person-only fields: one-pr is the story's own say over its project's
default, which gives a key nothing it could not already do by filing the
story that way; landing is a fact a session or a poll reports about a
repository and a sha, not a power over the board. `stall` carries no
`[RequireRole]` at all, on the method or the class - it is reachable by
anything the wall admits, person, key or a keyless runner that named itself,
which is deliberate: a dispatcher is exactly who leaves this mark.
`release-note` is key-open but claim-gated: a person may write it any time, a
program caller only while holding the issue's live claim, sent as
`?heldToken=`, and is refused with a 409 otherwise.

`AssigneeRequest` is `Kind?` and `Id?` - both or neither, and neither clears.
`PriorityRequest(Priority)` takes one of `paused`, `economy`, `low`,
`normal`, `expedited`, `emergency`; `ExpediteRequest(Expedited)` is the
two-level alias. `PreApprovalRequest(Level)` takes one of `none`, `express`,
`preapproved`; `ExpressRequest(Express)` is the two-level alias.
`OnePrRequest(Value)` takes `on`, `off` or `inherit`. `ReleaseNoteRequest`
is an optional `Note` plus the query parameter `heldToken` (a guid, program
callers only) - null or blank clears it. `StallRequest(Why)` is one line,
truncated. `HoldRequest(Held)` is a boolean. `IssuePlaybookRequest(Model?,
Effort?)` leaves a field alone when null, and hands it back to the playbook
on `""`. `LandingWriteRequest` is `Remote`, `Sha`, an optional `Trunk`,
`ContainedBy` and `Runner`.

**Refusals worth naming.** `assignee` 400s on a kind with no id, or an
unknown actor. `priority`/`expedite` 400 an unparseable level name - the
alias is just a two-level write of the same field and the same trail.
`one-pr` 400s on anything but a story, and 409s once any descendant has
started - standing in the implementation column or to its right, since a
task begun on one branch cannot be moved onto the other. `pre-approval` and
`express` gate by what is being set: raising to or lowering from
`preapproved` needs an owner or approver, 403 otherwise, since that is the
approve press given in advance; every other transition is gated like an
ordinary move into the board's send-back column, where one exists.
`playbook` 400s an unknown model or effort - the same validation a playbook
file is held to. `landing` 400s a malformed remote, sha or trunk, and the
first write for a repository 400s if it omits `trunk`. Every field here
follows the house no-op rule: setting the value already held writes nothing
and moves no `UpdatedAt`.

## The thread

| Method | Path | What it does |
|---|---|---|
| GET | `/issues/{key}/comments` | The thread, oldest first |
| POST | `/issues/{key}/comments` | A note, a question, an answer, or a message |
| GET | `/issues/{key}/events` | The audit trail, newest first |
| GET | `/questions` | Every question in the house, oldest first |
| GET | `/issues/{key}/questions` | The same, for one issue |
| POST | `/issues/{key}/messages/deliver` | Marks messages read; answers with the ones this call marked |

All six are a key's. Both question routes take `?open=` (default `true`):
open is unanswered and not terminal, `open=false` is every question,
answered and obviated ones included.

`CommentDto` is `Id`, `Author`, `Body`, `Kind` (`""`, `question`, `answer` or
`message`), an optional `AnswersId`, an optional `Options`, `CreatedAt`, an
optional `DeliveredAt`, an optional `DeliveredTo`. `CommentCreateRequest` is
`Body`, an optional `Kind` (omitted means an ordinary note), an optional
`AnswersId`, an optional `Options`. `QuestionOptionDto` is `Label`, an
optional `Detail`, `Recommended` - at most eight options per question, at
most one recommended, labels unique case-insensitively. `QuestionDto` is
`Id`, `IssueKey`, `IssueTitle`, `Body`, `AskedBy`, `AskedAt`, an optional
`Options`, `Answers` (oldest first - an empty list is what "open" means, no
second flag), `Obviated`. `IssueEventDto` is `Id`, `Actor`, `Kind`, an
optional `Payload`, `At`. `MessageDeliverRequest` is an optional `Ids` - exact
ids, or null for every unread one; ids that are not unread messages on the
issue are ignored rather than refused.

Every write here leaves an event carrying the caller's name as the actor, so
the trail says who did what without anybody being asked to record it.

**Refusals worth naming.** `POST comments` 400s an empty or over-length body,
an unknown `kind`, an `answersId` on anything but an `answer`, `options` on
anything but a `question`, and a batch of refusals on bad options: one option
alone, more than eight, duplicate labels, an over-length label or detail,
more than one recommended. An `answer`'s `answersId` must name an actual open
`question` on the same issue, or it is 400'd. Nothing here checks whether the
caller may answer its own question - that gap is deliberate, since what
holds the loop shut is that an open question blocks dispatch entirely, not a
check on who answered.

## Dependencies

| Method | Path | What it does |
|---|---|---|
| POST | `/issues/{key}/dependencies` | Makes this issue wait on another |
| DELETE | `/issues/{key}/dependencies/{dependsOnKey}` | Frees it from one |

Both a key's. There is no GET: both routes answer the whole `IssueDto`, so a
page repaints from one response. The POST body is
`IssueDependencyRequest(DependsOnKey)`.

**Refusals.** 400s self-dependency, a blocker above or below the issue in the
parent tree (it could never be satisfied), and a blocker that already -
directly or through a chain - waits on this issue, the cycle the new edge
would close. Adding an edge that is already there, or deleting one that is
not, writes nothing and answers the issue unchanged - no error either way.

## Search, the plan and assignees

| Method | Path | What it does |
|---|---|---|
| GET | `/search` | Full-text search across title, description, comments and work log, paged |
| GET | `/plan` | Every epic's rollup, the Plan page's one request |
| GET | `/plan/{key}` | One issue's rollup and its children's |
| GET | `/assignees` | Everybody an issue could belong to, plus who the caller is |

All four a key's. `/search` takes `q`, `projectId`, `type`, `statusId`,
`createdFrom`/`createdTo`, `closedFrom`/`closedTo`, `limit` (1-200, default
50) and `offset`; with `q`, best match first and closed-ahead-of-open among
equals, without it newest first (closed, else created). `/plan` takes an
optional `projectId`. `/assignees` takes nothing, and answers an
`AssigneeDirectoryDto(Me?, Assignees)`. `docs/hatch-planning.md` has the
plan's arithmetic in full.

**Refusals.** `/search` 400s an invalid `type`, a `limit` outside 1-200, a
negative `offset`, or an unparseable date bound. `/plan/{key}` 404s an
unknown key; `/plan` with an unknown `projectId` answers an empty plan rather
than a 404, since an empty or just-deleted project is a legal thing to look
at.

## Images and import

| Method | Path | Who | What it does |
|---|---|---|---|
| POST | `/images` | **person** | Uploads one (raw body, detected content-type) |
| GET | `/images/{id}` | key | Fetches it |
| POST | `/import/preview` | key | What uploaded `.md` files would become; reads/writes nothing |
| POST | `/import/preview-text` | key | The same, for a pasted title and body |
| POST | `/import` | key | Files the approved tree: epic per doc, story per phase, task per checkbox |

`ImageDto` is just `Id`. A preview's answer is the parsed tree, one doc per
file - the shape is internal to the importer and not worth repeating here.
`preview-text`'s body is `PastedPlan(Title, Body)`. The import write's body
is `ImportRequest(ProjectId, Docs)`, and its answer is
`ImportResultDto(Epics, IssueCount)`, each epic an
`ImportedEpicDto(Filename, Key, Title, StoryCount, TaskCount)`.

**Refusals.** Image upload 403s a program caller outright - images are
pasted by a person, not uploaded by an agent - and 400s an undetectable
content type. `preview` 400s a non-`.md` filename, a file over 1 MB, or more
than 25 files. `preview-text` 400s an empty title or body, a title over the
issue title cap, or a body over 1 MB. `import` 400s an empty or over-25
`Docs` list, 404s an unknown `ProjectId`, 409s a board with no columns, and -
like create and a project move - retries a numbering race up to five times
before 409ing.

## Coming

Pages this one names that are not written yet:

- `cli-reference`

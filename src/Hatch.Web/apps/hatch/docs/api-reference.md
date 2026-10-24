---
title: API reference
section: Reference
order: 30
summary: Every route under /api/hatch, grouped by resource, with who may call it and what it answers.
---

Every route Hatch answers lives under `/api/hatch`, JSON in and JSON out. This
page groups them by resource: the board's own resources first, then the
loop's ([`cli-reference`](cli-reference) is the easier way to make most of
these calls; this page is for the ones it has no verb for).

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

[`cli-reference`](cli-reference) has `hatch api` in full.

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
| PUT | `/projects/{id}/reflection` | **person** | Sets `reflectionCadenceDays` |
| POST | `/projects/{id}/reflect` | **person** | Files one reflection on the spot - the reflect-now control |
| PUT | `/projects/{id}/runner-tags` | **person** | Replaces the whole list of runner tags |
| PUT | `/projects/{id}/binds-tracker` | **person** | Sets `bindsTracker`, clearing it elsewhere |
| PUT | `/projects/{id}/stories-on-epics` | **person** | Sets `storiesOnEpics` |

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
`ProjectRepoSourcedWriteRequest(RepoSourced)`. The reflection PUT body is
`ProjectReflectionWriteRequest(CadenceDays)` - a positive number of days, or
null to turn reflection off; it never writes `reflectionLastPeriodStart`,
only the reflection job does. `POST .../reflect` takes no body, and answers
an `IssueDto` - the reflection it just filed, or, pressed again while that
one is still open, the same issue with nothing filed; it never writes
`reflectionLastPeriodStart` either, only the job does. The runner-tags PUT body is
`ProjectRunnerTagsWriteRequest(Tags)` - the whole list, not a delta. The binds-tracker PUT body is
`ProjectBindsTrackerWriteRequest(BindsTracker)`; at most one project on a board binds the tracker's
own repository, so turning it on takes it from whichever project had it, and the answer is a
`ProjectBindsTrackerDto(Project, ClearedKey)` naming that project's key, or null. The
stories-on-epics PUT body is `ProjectStoriesOnEpicsWriteRequest(StoriesOnEpics)`, and resending the
value a project already holds writes nothing. Every other answer is a `ProjectDto`
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
project binds no repository or any playbook row still names it. The
reflection PUT 403s if the caller is not an owner, 400s on a cadence that is
zero or negative, and 400s naming the Playbooks page if no playbook row would
run the reflection a non-null cadence files. `POST .../reflect` 403s if the
caller is not an owner, and 400s naming the Playbooks page the same way if no
playbook row covers it. The
runner-tags PUT 403s if the caller is not an owner, and 400s on a tag that
breaks its format rule or a list over the count limit. The binds-tracker PUT 403s if the caller is not
an owner, and 400s if the project binds no repository.  The stories-on-epics PUT 403s if the caller is not an owner.

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
| PUT | `/statuses/{id}/runner-tags` | **person** | Replaces the whole list of runner tags a column requires |

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

The answer is a `BoardDto`: `Statuses`, `Issues` (each an `IssueCardDto`), an
optional `Wip`, an optional `DoneHorizonHours`, and `PastHorizon` - the
stories the horizon withheld, handed over separately so a client can count
them against their still-open epic. There is no write here, and no refusal
beyond the standard ones.

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

All eight are a key's. `GET /issues` takes `projectId`, `type`, `statusId`,
`parentKey` (`""` means "no parent at all" - the one filter with no other way
to write it), `ancestorKey` (anything below at any depth, not the issue
itself) and `text` (a substring of the title, plus an exact key match), all
optional and ANDed together. `docs/hatch-planning.md` has these filters in
full, the bulk endpoint's own filters, and the `/plan` reads' arithmetic -
none of that is re-derived here.

`IssueCreateRequest` is `ProjectId`, `Type`, `Title`, an optional
`Description`, `ParentKey`, `ReadyAt`, `DueAt`, `OnePr`, `Priority` and
`PreApproval` - `OnePr` only on a `story`, refused on anything else,
`inherit`/`on`/`off`; `Priority` and `PreApproval` take the level names the two
`PUT` routes below take, absent meaning `normal` and `none`, so an issue can be
born at a level in the one save, with no second event. `IssuePatchRequest`
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
even loaded. Create has person-only fields of its own the same way: a program
that names `priority` or `preApproval`, even as `normal` or `none`, is 403'd
before the project is looked up or a number is minted. A person who names
`preapproved` must be an owner or approver of the project, and one who names
`express` must be allowed to move work into the board's send-back column - the
two questions `PUT /issues/{key}/pre-approval` asks - and a refusal files
nothing. An unknown level name 400s, naming the levels. A project move 409s if any issue in the moving set - the root,
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
| runner-tags | PUT | `/issues/{key}/runner-tags` | key | Replaces the ticket's own runner tags whole |
| release-note | PUT | `/issues/{key}/release-note` | key | The changelog's sentence for this issue |
| stall | PUT | `/issues/{key}/stall` | open to anything the wall admits | A runner's own mark that it left the ticket where it found it, and why |
| hold | PUT | `/issues/{key}/hold` | **person** | Parks/resumes a ticket; clears any stall mark |
| playbook | PATCH | `/issues/{key}/playbook` | **person** | Per-ticket model/effort override, beating every playbook |
| landing | PUT | `/issues/{key}/landing` | key | What a repository holds for this issue's branch, per remote |

Several of these are a person's alone; checked one at a time rather than
assumed uniform. `one-pr`, `runner-tags` and `landing` are key-open despite
sitting beside five person-only fields: one-pr is the story's own say over its
project's default, which gives a key nothing it could not already do by
filing the story that way; runner-tags is a statement about which runner may
pick the ticket up, not a power over the board, for the same reason; landing
is a fact a session or a poll reports about a repository and a sha, not a
power over the board. `stall` carries no
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
`OnePrRequest(Value)` takes `on`, `off` or `inherit`.
`IssueRunnerTagsWriteRequest(Tags)` is the whole list, replaced; trimmed,
lower-cased and deduplicated, at most 8 tags of at most 32 characters, none
with a space or comma. `ReleaseNoteRequest`
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
task begun on one branch cannot be moved onto the other. `runner-tags` 400s
a blank tag, one with a space or comma inside, one over 32 characters, or a
list over 8; `404` for no such key; re-sending the list it already holds
writes nothing. `pre-approval` and
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

## The loop's own routes

Everything above is a resource the board itself owns. What follows is what a
runner calls to do its own work, and what the operator reads to watch it -
the second half of this page, after the board's own.

### Work: what a runner asks the board for

| Method | Path | What it does |
|---|---|---|
| GET | `/work/next` | The next clear issue an unattended pass would pick up |
| GET | `/work/queue` | What a whole pass would do, blocked issues included, each with the sentence saying why |
| GET | `/work/review` | Every issue in review or merges this caller holds a checkout of, and what the board holds about its branch |
| GET | `/work/{key}` | The same dispatch answer for one named issue |
| POST | `/work/{key}/hop` | Carries an express issue, or a child its parent pulls, across its column with no session |
| POST | `/work/{key}/verdict` | Applies a check session's recorded verdict - passes it on, sends it back, or holds it for a person on a second fail in a row |
| POST | `/work/{key}/merged` | Advances a reviewed issue whose pull request the forge says merged |
| POST | `/work/{key}/pushback` | Carries a merge-column issue back to review once its branch conflicts or its build fails |
| POST | `/work/{key}/landed` | Advances a trunk-landing issue once its commit is confirmed landed and the trunk is green |
| POST | `/work/{key}/closed` | Records that the pull request recorded on an issue closed without merging |

All ten are a key's. This is the group a reader comes to the page for:
`hatch work` calls `GET /work/{key}` for a named ticket and `GET /work/next`
for an unattended one; `hatch go-to-work` calls `GET /work/queue` once to log
what it sees, then `GET /work/next` in a loop - see
[one increment and the loop](one-increment-and-the-loop) for what the
increment does with the answer, rather than retold here. `/work/next` and
`/work/{key}` answer a `WorkDto`: the issue, the move it would make, and the
playbook row and the prompt the increment is to run where nothing blocks it,
or the sentence saying what does - see
[what a session is told](what-a-session-is-told) for what the prompt itself
holds.

`next`, `queue` and `{key}` take `remote`, `standing`, `clones` to declare a
caller's checkouts (any one opts into the repository fold), plus
`ancestorKey` to narrow to one corner of the board and `mine` to narrow to
the caller's own tickets; `{key}` and `next` also take `heldToken`, a claim
the caller already holds, so re-reading a dispatch for a ticket it is already
working is not folded as a conflict. `queue` instead takes `asRunner`, which
answers off that named runner's own row - its repositories, `mine` and
`ancestorKey` - rather than the query string, and may not be combined with
any of them. The four write routes (`merged`, `pushback`, `closed`) take
`PullRequestMergedRequest`/`PullRequestPushbackRequest`/`PullRequestClosedRequest(Url,
Runner)`, comparing `Url` against what is recorded on the issue; `verdict`
takes only `heldToken`, naming the claim the verdict was recorded under;
`landed` takes no body at all and trusts nothing from its caller, re-reading
the landing and the trunk build fresh.

**Refusals worth naming.** `hop` 409s when the move it would make is
blocked, and 409s again naming the sentence "is not a hop" when nothing
about the issue is a hop at all. `merged`/`pushback`/`closed` 409 when no
pull request is recorded, or when the given `Url` is not the one recorded;
`pushback` separately 409s with the judge's own sentence when the branch is
not actually a pushback. `merged`/`pushback`/`landed` 409 when the issue
holds a live claim - a session is running on it, so nothing here moves the
row out from under it. `landed` 409s off the review column and 409s again
naming whether the project lands by pull request at all or already carries
one of its own.

### Claims

| Method | Path | What it does |
|---|---|---|
| POST | `/issues/{key}/claim` | Takes the lease, or says who already has it |
| POST | `/issues/{key}/claim/heartbeat` | Refreshes the lease, and answers whether the board has preempted it for an emergency |
| DELETE | `/issues/{key}/claim` | Lets go of it |

All three are a key's, with one narrowing below. A claim is a lease with a
clock, not a dispatch of its own - `WorkController` is what decides whether
to send anything at an issue, and a claim that second-guessed that would be
the same rule in two places. The lease's length is the
`Hatch__ClaimTtlSeconds` setting (default 300 seconds); taking a claim
carries it back as `TtlSeconds` alongside `StallLapseSeconds`, so a caller
never has to know the setting's name to honor it. A heartbeat renews the
lease and may replace the playbook it is running under; it writes no event on
an ordinary beat, only on a preemption, which is the board's own decision to
make room for an emergency ticket and is the one case a heartbeat answers a
`ClaimPreemptedDto` instead of `204`.

A live claim refuses a second `POST`, including the same runner's own -
re-claiming is not a thing a runner does, it heartbeats - and a claim on an
ancestor or descendant issue refuses it too: one runner per line of the tree.
The `DELETE` carries an optional `token` (the one the claim was taken with)
and an optional `outcome` - one of `dropped`, `worked`, `preempted` - read
only alongside a token. Releasing a claim that holds nothing, or releasing
twice, is a `204` that writes nothing either way.

**The one narrowing.** A tokenless `DELETE` is the operator's own clobber -
clearing another runner's claim by hand - and it refuses a program caller
outright even though the route itself is key-open: an agent that could take
a ticket off another agent mid-increment is the exact failure the claim
exists to prevent, reintroduced through its own back door. A runner lets go
of its own lease by presenting the token it was given, which every honest
runner has. This is deliberately not on the person-only list in
[Who may call what](#who-may-call-what) below - the attribute itself does
not narrow the route, the action body does.

### Checks, pull requests and landing

| Method | Path | What it does |
|---|---|---|
| POST | `/issues/{key}/verdicts` | Records a check session's pass/fail about the issue, under its live claim |
| PUT | `/issues/{key}/merge-check` | Replaces the stored verdict for the issue's branch against one repository's trunk |
| PUT | `/issues/{key}/build-check` | Replaces the stored verdict for the build on the tip of the issue's branch in one repository |
| PUT | `/issues/{key}/pull-request-state` | Replaces the stored mergeability, review decision and head sha for the issue's recorded pull request |
| PUT | `/issues/{key}/brought-current` | Marks the issue as the one the train last brought current, and at which trunk sha |
| GET | `/trunk-builds` | Every repository trunk's stored build verdict |
| PUT | `/trunk-builds` | Replaces the verdict for one repository's trunk |
| POST | `/trunk-builds/{id}/bug` | Files the bug the attention panel's *File a bug* button asks for, from a failing trunk |

All seven are a key's. None of this group has a `GET` beside the issue
routes - a verdict, a merge check and a build check all ride an `IssueDto`/
`ReviewCheckDto`, which is where every client needs them, rather than a
second route to keep in step. `verdicts` is the one route here that is a
session's and not a poll's: it names no remote, is matched to the issue's
live claim token, and only takes effect once `/work/{key}/verdict` is called
with that same token - a check session records what it found, and the loop
decides what to do about it. The other six are what a runner's poll writes
with no session at all, the same background process
[letting the loop merge for you](letting-the-loop-merge-for-you) describes
driving the merge train from: a merge check and a build check are facts
about two refs on origin any runner computes the same way, a pull-request
state is what the forge itself already says, a brought-current mark is the
train saying which row it just updated, and a trunk build is nobody's
issue, so its one route answers to no claim and no column at all.
`/trunk-builds/{id}/bug` is the one write in this group a person can also
take, from the attention panel's own button; a key filing one gets none of
the three optional fields (express, priority, the column) a person may
choose - they come off the project's own auto-heal settings, and only where
a project has turned auto-filing on.

**Refusals worth naming.** `verdicts` 400s a verdict that is not `pass`/
`fail`, and refuses where the issue holds no live check-playbook claim.
`merge-check`/`build-check`/`pull-request-state`/`trunk-builds` 400 an
unparseable remote, verdict, merge state or review decision, and (for a
`failed` or `pending` build or trunk verdict) a missing list of failing
checks. `pull-request-state` 409s when no pull request is recorded on the
issue, or when the given url is not the one recorded. `brought-current` 409s
where the issue's column does not merge pull requests. A verdict, build or
merge check that repeats what is already stored refreshes the shas, the
runner and the time, and writes no event - a poll that looks every interval
leaves one entry, not a hundred.

### Playbooks

| Method | Path | Who | What it does |
|---|---|---|---|
| GET | `/playbooks` | key | Every row, in board order |
| POST | `/playbooks` | **person** | Creates one |
| PATCH | `/playbooks/{id}` | **person** | Edits one |
| DELETE | `/playbooks/{id}` | **person** | Deletes one |
| GET | `/playbook-files` | key | Every stored verdict of what a repository's trunk actually carries at `.hatch/playbooks.yaml` |
| PUT | `/playbook-files` | key | Replaces the stored row for one repository's trunk, from a runner's poll of the file |

**A key may read a playbook and may not write one, and the refusal is
deliberate.** An agent that could widen its own prompt, or raise its own
model or effort, has no fixed point to settle at, and the failure that
follows is unbounded spend rather than a wrong answer - the same shape of
loop the claim's tokenless `DELETE` above and a runner's own `--max-spend`
are cut to prevent. Reading stays open because an agent has to be told what
it was dispatched with.

`/playbook-files` is not that same edge, despite sitting beside it: it is
not the matrix above, it is a cache of what a runner's poll actually found
on disk at a repository-sourced project's trunk - a fact a key reports about
a file, not an edit to Hatch's own instructions - so both its `GET` and its
`PUT` stay key-open. Check each verb against its own attribute rather than
assume the group is uniform.

`PlaybookDto` carries the transition (`FromStatusId`/`FromStatusName`,
`ToStatusId`/`ToStatusName`), `Types`, `Shape`, the effective `Prompt`
beside the row's own `DefaultPrompt`, `Model`, `Effort`, an optional `Budget`
(millions of tokens) and `BriefLimit` (characters), `DeniedTools`, `IsCheck`,
and `Source` - `"board"` or `"project"`, by whether `ProjectId` names one.
The create and patch bodies share that shape with the three-state convention
every optional field in Hatch takes: absent leaves a field alone (patch) or
takes its default (create), `""` clears it, a value sets it.

**Refusals worth naming.** Create and patch 400 a column that does not
exist, a transition that goes nowhere - unless it is the review column
naming itself, which is the one review playbook a board may have - a
duplicate of a row already filed for that transition, those types, that
shape and that project, an unknown model or effort, and a project that reads
its playbooks from its own repository rather than from rows here at all.
`playbook-files`' `PUT` 400s an empty remote, trunk, sha or runner, or
content over the stored cap, and writes no event - there is no issue to
write one on, and no question, since a bad playbooks file is this row's own
state rather than a stall.

### Runners and settings

| Method | Path | Who | What it does |
|---|---|---|---|
| GET | `/runners` | key | Every runner heard from lately that the caller may see, most recent first |
| GET | `/runners/names` | key | The name of every runner heard from lately, most recent first, and nothing else |
| POST | `/runners/{name}` | key | A runner's own heartbeat: what it is, and what the board would like it to do next |
| PATCH | `/runners/{name}` | **person** | Sets its state, under, max-runs, max-spend, until, and tags |
| POST | `/runners/{name}/order` | **person** | Orders this runner to work a named ticket, or to pass on its next pick |
| DELETE | `/runners/{name}/order` | **person** | Withdraws whatever standing order it holds |
| GET | `/runner` | **person** | What platforms this image can hand out, and the commit they and it were built from |
| GET | `/runner/download/{rid}` | **person** | One platform's `hatch` binary |
| GET | `/settings` | **person** | Hatch's own settings: the Claude subscription token, the agents' API keys, the local person's name |
| PUT | `/settings` | **person** | Edits them |
| GET | `/settings/credentials/{agent}` | key | One agent's credential, unprotected, for the container runner to start it with |
| GET | `/settings/claude-token` | key | The same, kept for an older container; `credentials/claude` is its successor |
| GET | `/me` | key | Who, or what, is calling - a browser or a key or a runner - for the nav strip |

`/runners/{name}` is the one call a running loop makes about itself, and the
whole round trip: it reports what it is - its repositories, whether it
clones, `mine`, `standing`, where it runs, which agent spawned it - and
reads back a `RunnerInstructionDto` saying whether to keep going, pause or
stop, and the four bounds (`Under`, `MaxRuns`, `MaxSpend`, `UntilAt`) it is
running within. Those four bounds are written by a fresh loop's first beat
(`Fresh: true`, a `loop` beat only), which also clears any the row held for a
flag the loop was not given, and otherwise only by a person's `PATCH` -
a supervisor restart sending its original `--max-runs` again never undoes an
operator's midnight edit, a `once` beat never writes them, and a running agent
cannot raise its own budget: only a new process started with flags can, which
is the failure the bounds exist to prevent. There is no
`DELETE` on the row itself: it ages out of `GET /runners` on its own once
nothing has heard from it in a while.

`/runners/{name}/order` is a standing order a person places from the Runners
page - work a named ticket next, or pass on the loop's own pick - read and
cleared the moment this runner's next `loop` heartbeat answers it, never
accumulating past one. `POST` places it (`RunnerOrderRequest { kind, key? }`,
`key` required for `work` and absent for `pass`); `DELETE` withdraws it
unread, 204 either way. Ordering `work` also writes an `ordered` event
(`{ runner, by }`) on the ticket named. A `once` heartbeat (`hatch work`,
sharing a loop's own name) leaves an order exactly as it found it - the
instruction is for the loop's own next pass, not for a single increment that
will never read it back.

`GET /runners` answers with the rows the caller may see:

| Caller | Sees |
|---|---|
| A person at a browser, or any caller on a Hatch with its wall off | Every runner |
| A key whose owner is an admin | Every runner |
| A key whose owner is anyone else | Only that owner's runners |
| A key with no owner | Only the runners that belong to nobody |

`/runners/names` is the same list whoever asks, and carries no `where`, owner
or claim, which is why a new runner can read it to pick a free name.

`/runner` and `/runner/download/{rid}` carry no scope at all, unlike every
other Hatch-scoped read on this page - the audience is a person at a browser
setting up a machine, and nobody's dispatcher needs to download the program
it is already running as.

`/settings` is Hatch's own configuration, carrying nothing an agent reads:
it is person-only on both verbs by design, beside `/runners/{name}` and
`/playbooks`' writes above - an API key that could write here could set the
name every event in the house is signed with, or swap the credential the
account's headroom is read through. `credentials/{agent}` and
`claude-token` are the one deliberate exception beside it: **the answer is a
live credential, and it is never logged** - the one place in Hatch a secret
is handed back out of the API on purpose, for the one caller that has
nowhere else to read it from, the container entrypoint's own
`hatch runner-credential` - see [the container runner](the-container-runner)
and [cli-reference](cli-reference). Both refuse outright wherever the wall
is up - a credential that crosses a network Hatch does not own is a
different question from one handed to a container on the same laptop.

`/me` answers a `MeDto(Kind, Name, Role, Configured, CanSignOut, CanSignIn,
Owner, Scopes)` - `kind: key` for an API key, naming its own name, its
owner's name (`Owner`, null with no owner), its scopes (`Scopes`, null for
every other kind) and its owner's role lowercased (`Role`, null with no
owner); `kind: runner` for a wall-off runner header, naming its own name with
`Owner` and `Scopes` both null; `kind: person` when a grant holds a person;
`kind: local` with the wall off and the machine's own person speaking;
`kind: none` with the wall off and nobody local either (an unidentified
browser, still able to sign in); and `204` only for an unidentified browser
against a walled Hatch - no grant, no key, no local actor.

**Refusals worth naming.** `PATCH /runners/{name}` 404s a name no runner has
ever used - it edits, it never creates. With the wall up, it also 403s a
caller who is neither the runner's own owner nor an admin, naming the owner
and the runner in the sentence. A tag that breaks the rule - more than 8, one
over 32 characters, or one with a space or comma inside - 400s with the
rule's own sentence, the same refusal a heartbeat's own tags answer to.
`POST /runners/{name}` 409s when two live runners would otherwise share one
name. `POST /runners/{name}/order` 400s a kind that is not `work` or `pass`,
and `work` with no key or one that is not an issue key; 404s a `work` key
naming an issue that does not exist; 409s a `once` runner, a `stopping`
runner, and a runner that already holds an order, naming the kind and key
held and saying to withdraw it first. `credentials/{agent}` 404s an agent it
does not know and 403s wherever the wall is up; a credential that is unset or
blank answers `204` rather than either refusal.

### What a night left behind

| Method | Path | Who | What it does |
|---|---|---|---|
| POST | `/issues/{key}/work-log` | key | Records what one session cost on this issue |
| GET | `/issues/{key}/work-log` | key | This issue's own sessions, and what its whole subtree has cost |
| GET | `/work-log/history` | key | Spend over time, in equal buckets |
| GET | `/work-log/sessions` | key | The sessions in a range, ranked, and what the range cost in total |
| GET | `/work-log/by-playbook` | key | The sessions in a range, folded by the playbook, transition, issue type, model and effort they ran under |
| GET | `/overnight` | key | What landed in the newest night |
| POST | `/overnight/seen` | **person** | Marks the night read, for the caller |
| GET | `/activity` | key | The event trail across every issue, newest first, a page at a time |
| GET | `/changelog` | key | What shipped, a window of days at a time |

[Watching the loop](watching-the-loop) is what draws these on screen; this
page is their shapes. `work-log`'s `POST` is the one write on this whole page
that is a key's and nothing else's, the opposite cut from everything above
it: it refuses a person - a work log entry is written by the dispatcher,
with an API key - there is no control anywhere in the browser for writing
one and none is meant to exist. Its `GET`, on `issues/{key}/work-log`,
answers a `WorkLogDto(Key, Totals, Own, Entries)` where `Totals` covers the
whole subtree and `Own`/`Entries` cover only this issue - an epic showing a
handful of its own sessions beside a much larger `Totals` is the feature,
not a bug. `/work-log/history` buckets by `hour` or `day`, chosen from the
range's length unless named, and answers the bucket size it actually used;
`/work-log/sessions` ranks by `tokens` (the default), `cost`, `ended`,
`requests` or `peakContext`, each descending. Both take an optional
`ancestorKey`, read inclusive of the issue itself - deliberately unlike the
same parameter on `GET /issues` - so the graph, the table and the issue
page's own meter can never describe different populations.
`/work-log/by-playbook` folds the same range by the playbook, transition,
issue type, model and effort each session ran under, and takes `projectId`
in place of `ancestorKey`; it answers every group, heaviest first by total
tokens, with no sort or limit to choose, and an unknown `projectId` answers
no rows rather than a refusal. Sessions from an older runner, which did not
record those, fold into one row of nulls, and a status name is the board's
current one - null where the column has since gone.

`/overnight` answers an `OvernightDto(From, To, Count, Rows, Unseen)`,
grouping and gap-splitting the newest night's terminal landings; `seen` is a
person's own mark of having looked, and refuses a program for the same
reason `work-log`'s write refuses a person, in reverse - a keyless runner
that named itself is refused here too, since it is an agent whether or not
it carries a credential. `/activity` pages the same event trail
`/issues/{key}/events` reads for one issue, across every issue instead,
ordered the same way so one event never sorts differently between the two
reads; it takes `limit` (1 to 500, default 100) and `offset`, and answers
`hasMore` rather than a count. `/changelog` takes `before`, `projectId` and
`days` (default 14), answering the newest window of days that shipped
something, with every closed month's own count beside it for the jump
control - this is the same route the Changelog entry at the top of the
app's nav draws from, not a page of its own.

**Refusals worth naming.** `work-log`'s `POST` 400s a missing session id.
`history` 400s an offset outside +-1440 minutes, an unrecognised bucket
name, or a request whose buckets would exceed 1000. `sessions` 400s an
unrecognised sort or a limit outside 1-500. `activity` 400s a limit outside
1-500 or a negative offset. `changelog` 400s a window under one day or a
project that does not exist. `history`/`sessions`/`by-playbook`/`changelog` all 400 an
unparseable range bound, in the order `to`, then `from`, then the inversion.

### Reports and the battery

| Method | Path | What it does |
|---|---|---|
| GET | `/reports/timeline` | The top-level epics active in a range, and the work beneath each as stretches across the board's columns |
| GET | `/reports/cumulative-flow` | How many issues sat in each column over time, as a grid; a terminal column baselines at zero at the window start and counts only what closed inside it |
| GET | `/reports/flow` | How work moved through the board in a range: per column what entered, left and stands, and how long it dwelled, plus the window's drops, hops, send-backs, questions and sessions - counts and durations only |
| GET | `/utilization` | The caller's own Claude headroom - the battery in the nav strip |
| GET | `/attention` | What the loop is waiting on a person for, in one request |

All five a key's. `timeline` takes `from`, `to` (default the ninety days
ending now), `projectId` and `createdBy`; `cumulative-flow` takes only
`from`/`to`, the same ninety-day default; `flow` takes `from`/`to` the same
way, plus `projectId` and `type`, and 400s an unknown `type` or `projectId`.
None of the three queries is itself bounded by the range - the fold needs an
issue's first move even where it falls outside it - but the ranges' bounds
are. `utilization` answers a
`UtilizationReading(State, ReadAt, Limits)` off whatever this caller's own
runners last reported on their own heartbeat, never the account's endpoint
directly, and `204`s for a caller with no runner ever reporting one, which
is the ordinary state for a fresh install rather than an error.

`attention` is the one request the nav strip's badge and panel both draw
from, since two polls a poll interval apart would show a lit control beside
an empty list. It answers an `AttentionDto`: open questions waiting on
somebody, pull requests in review split into plain reviews, conflicts, and
failing builds, how many sit in review with no pull request at all, the
merge column's own held-back and still-progressing rows, trunk-landing rows
the judge holds, failing trunk builds, exhausted runners, and the
deadlocked rows the dispatcher's own scan flags - an issue waiting, directly
or through a chain, on something itself waiting on it.

**Refusals.** None beyond the ordinary ones - every route in this group is
a read with no body to reject.

## Who may call what

One scope, `hatch`, and it reaches everything under `/api/hatch` **less the
routes that take a person only**. A key that calls one of those gets a
`403`, and that is the gate working as designed, not a misconfiguration -
[People, roles and keys](people-roles-and-keys#api-keys) says the same thing
in prose, grouped by what the route does; this list is the route-by-route
version it already points to, and should not repeat or contradict it.

Recounted directly from the `[RequireRole]` attributes - the method's where
it carries one, the class's otherwise (`AssigneeController` is the one file
on this page with both) - there are **38** person-only paths on this page,
across both halves together:

```
PUT    /projects/{id}/repositories
PUT    /projects/{id}/logo
DELETE /projects/{id}/logo
PUT    /projects/{id}/members/{personId}
DELETE /projects/{id}/members/{personId}
POST   /projects/{id}/claim
PUT    /projects/{id}/landing
PUT    /projects/{id}/playbooks-source
PUT    /projects/{id}/binds-tracker
PUT    /projects/{id}/stories-on-epics
PUT    /projects/{id}/reflection
POST   /projects/{id}/reflect
PUT    /statuses/{id}/express-skips
PUT    /statuses/{id}/parent-pulls
PUT    /statuses/{id}/agent-files
PUT    /statuses/{id}/implementation
PUT    /statuses/{id}/protected
PUT    /statuses/{id}/merges-pull-request
PUT    /statuses/{id}/runner-tags
PUT    /wip
PATCH  /issues/{key}/wip
PUT    /issues/{key}/assignee
PUT    /issues/{key}/priority
PUT    /issues/{key}/expedite
PUT    /issues/{key}/pre-approval
PUT    /issues/{key}/express
PUT    /issues/{key}/hold
PATCH  /issues/{key}/playbook
POST   /images
PATCH  /runners/{name}
POST   /runners/{name}/order
DELETE /runners/{name}/order
GET    /runner
GET    /runner/download/{rid}
GET    /settings
PUT    /settings
POST   /overnight/seen
POST   /playbooks
PATCH  /playbooks/{id}
DELETE /playbooks/{id}
```

Twenty-seven belong to a project, a column, the WIP section, a ticket's own
fields, or image upload; the other eleven are a runner's own record (now
three - the heartbeat's `PATCH` and the order's `POST`/`DELETE`), the
`/runner` pair, Hatch's settings on both verbs, marking the overnight seen,
and the three playbook writes.

Two writes that look like the same shape are deliberately **not** on this
list, because the attribute itself does not narrow them - the narrowing is
in the action body, against whether the caller is a program at all, the
same guard `work-log`'s own refusal (above, inverted) uses: the tokenless
`DELETE /issues/{key}/claim` (Claims, above) and `wipOverride`/a
cross-project `ProjectId` on `PATCH /issues/{key}` (the Issues table,
earlier on this page). A reader scanning only `[RequireRole]` attributes
would miss both; this page does not, by calling each out in its own group's
prose instead of padding this list with routes that are not, by the
attribute, person-only at all.

## Refusals

- **`400`** - a field or value the body or the query sent is wrong, named
  plainly: `"a verdict is one of pass, fail"`, `"an offset is not negative -
  not -4"`.
- **`401`** - one of two different things behind one code: a key that was
  sent and refused (revoked, or malformed), or no key at all against a
  Hatch whose wall is up and wants one. Neither ever reaches an action - the
  check runs as an authorization filter, before model binding.
- **`403`** - either the route is not one this key may take at all (the
  person-only list above), or the caller is not a live owner or approver of
  the project the move or the edit concerns - a key or a runner never counts
  as either, however it is scoped. The error names the reason in both cases,
  since the caller is already authenticated and the sentence a page shows
  for "this device is not linked to anyone" is not the sentence it shows for
  "you are not an administrator".
- **`404`** - no such issue, no such row, or no such route. An issue key
  nobody filed is a `404`, never an empty issue - none of the shapes on this
  page answer "nothing here" by handing back zeroed fields.
- **`409`** - the state itself refuses the write: a move into a terminal or
  protected column, a live claim already standing, a project that still
  holds issues or still has a playbook naming it, a last owner about to be
  removed, two runners about to share one name.

[`cli-reference`](cli-reference) turns every one of these into one of five
named sentences rather than a raw status code, for the loop and for a
terminal both.

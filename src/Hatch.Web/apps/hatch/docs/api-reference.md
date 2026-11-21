---
title: API reference
section: Reference
order: 30
summary: Every route under /api, grouped by resource, with who may call it and what it answers.
---

Every route Hatch answers lives under `/api`, JSON in and JSON out. This
page groups them by resource: the board's own resources first, then the
loop's ([`cli-reference`](cli-reference) is the easier way to make most of
these calls; this page is for the ones it has no verb for).

## How to call it

A person's browser carries a session cookie; a program sends `Authorization:
Bearer hatch_ak_…`. [People, roles and keys](people-roles-and-keys) covers
minting a key and what a key reaches - this page only says, route by
route, who may call it.

**Issue routes take the display key** - `/issues/{key}`, case-insensitively,
never a database id. Project and column routes take an int id -
`/projects/{id}`, `/statuses/{id}`. A person id is a guid -
`/projects/{id}/members/{personId}`.

```
hatch api GET /api/issues?statusId=2
```

[`cli-reference`](cli-reference) has `hatch api` in full.

**The old prefix.** Before this page's routes lived at `/api/<name>` they lived
at `/api/hatch/<name>`. That prefix is still served in place - the same answer
`/api/<name>` gives, with no redirect, so the key and a write's body arrive
intact - and `/api/hatch-revision` is not part of it. [Upgrading](upgrading#the-old-apihatch-prefix) has what that means
for an older `hatch` binary.

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
| PUT | `/projects/{id}/repositories` | **person** | Replaces the whole ordered list, each remote's `changelog` flag included |
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
| PUT | `/projects/{id}/check-approves-merge` | **person** | Sets `checkApprovesMerge` |
| PUT | `/projects/{id}/no-code-closes` | **person** | Sets `noCodeCloses` |
| PUT | `/projects/{id}/runners-work-contributions` | **person** | Sets `runnersWorkContributions` |
| PUT | `/projects/{id}/files-contributions` | **person** | Sets `filesContributions` |
| PUT | `/projects/{id}/one-pr-policy` | **person** | Sets `onePullRequestPolicy`, returning overrides nothing has started under to inherit on a tightening |
| PUT | `/projects/{id}/priority` | **person** | Sets `priority` to `paused`, `normal` or `expedited` |

`ProjectCreateRequest` is `Key`, `Name`, an optional `Color` and `Icon`.
`ProjectPatchRequest` is the same four fields, all optional - null leaves a
field alone on the patch, `""` clears `Color`/`Icon`. The repositories PUT
body is a list of
`ProjectRepositoryWriteRequest(Remote, BaseBranch?, Changelog)` - the whole ordered set, not a delta, so leaving out `changelog` writes false. Each
entry reads back with `changelog`, true where the repository takes a changelog
file. A members PUT body is
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
value a project already holds writes nothing. The check-approves-merge PUT body is
`ProjectCheckApprovesMergeWriteRequest(CheckApprovesMerge)`, and resending the value a project
already holds writes nothing, the audit fields included. The no-code-closes PUT body is
`ProjectNoCodeClosesWriteRequest(NoCodeCloses)`, and resending the value a project
already holds writes nothing, the audit fields included. The runners-work-contributions PUT body is
`ProjectRunnersWorkContributionsWriteRequest(RunnersWorkContributions)`; off, the dispatcher holds a
contribution's conflicted or failed-build branch for a person instead of handing it to a runner, and
resending the value a project already holds writes nothing, the audit fields included
(`runnersWorkContributionsBy`, `runnersWorkContributionsAt` on the `ProjectDto`). The files-contributions PUT body is
`ProjectFilesContributionsWriteRequest(FilesContributions)`; nothing reads the flag yet, and
resending the value a project already holds writes nothing, the audit fields included
(`filesContributionsBy`, `filesContributionsAt` on the `ProjectDto`). The one-pr-policy PUT body is
`ProjectOnePrPolicyWriteRequest(Policy)`, `optional` or `enforced` and nothing else (a `400` names
the bad value), owner only, and resending the value a project already holds writes nothing, the audit
fields included. The priority PUT body is
`ProjectPriorityWriteRequest(Priority)`, `paused`, `normal` or `expedited` and nothing else (a `400`
names the bad value), owner only, and resending the level a project already holds writes nothing, the
audit fields included (`priorityBy`, `priorityAt` on the `ProjectDto`). Every other answer is a `ProjectDto`
throughout, and a list of `ProjectMemberDto` from the member and claim
routes.

**Refusals worth naming.** DELETE a project 409s if it still holds issues,
and separately 409s if a playbook still names it. A members PUT 403s if the
caller is not a live owner, 400s on an unknown role or person, and 409s if
the edit would leave the project with no owner at all - the one state only
`/claim` escapes. POST `/claim` 409s if the project already has an owner. The
landing PUT 403s if the caller is not an owner; `landing` is `"pull-request"`
or `"trunk"`, anything else 400s; a story or bug that has already started work keeps
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
an owner, and 400s if the project binds no repository.  The stories-on-epics PUT 403s if the caller is not an owner.  The check-approves-merge PUT 403s if the caller is not an owner.  The no-code-closes PUT 403s if the caller is not an owner. The runners-work-contributions PUT 403s if the caller is not an owner. The files-contributions PUT 403s if the caller is not an owner. The one-pr-policy PUT 403s if the caller is not an owner, and 400s on anything but `optional` or `enforced`. The priority PUT 403s if the caller is not an owner, and 400s on anything but `paused`, `normal` or `expedited`.

## Scopes

What each person may see of the projects. All three are an admin's and a
person's alone: a key is refused with `403`. A scope narrows every board,
list, search and issue route for the person it belongs to, and for the keys
that person owns.
See [People, roles and keys](people-roles-and-keys#scope).

| Method | Path | Who | What it does |
|---|---|---|---|
| GET | `/scopes` | **person** | Every person's scope, `[{ personId, mode, projectIds }]`; a person with no row reads as `all` with every project id |
| GET | `/scopes/{personId}` | **person** | One person's scope; `404` for a person who does not exist |
| PUT | `/scopes/{personId}` | **person** | Sets `{ mode, projectIds }`, `mode` one of `none`, `all`, `some`; `projectIds` is required under `some` and ignored otherwise; `400` for an unknown mode or project, `404` for an unknown person |

**A project outside the caller's scope answers as one that does not exist, on
every route that takes one.** That is `404`, or the usual `400` with `there is
no project N` where a filter or a body names the project: a report's
`projectId`, a bulk edit that moves issues, a new playbook's `projectId`. It is
never `403`, and never a status that differs from the one an id nothing has
gets. A list answers without the project's rows, and a search finds nothing of
it. A key reads through its owner, and a key with no owner is unbounded.
A runner's `Under` is checked the same way: it must name an issue the caller
can see, so a hidden issue and a missing one both answer `404`.
`ScopeRouteMatrixTests` asks every Hatch route this, as a person and as a key.

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
| PATCH | `/issues/{key}/wip` | **person** | Sets an epic's, reflection's or ambition's own limit on how many of its stories run at once |

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

Every card names who filed it, as `createdBy` - a person's or an API key's
name, so a runner's own filings are filterable too. Every route that serves
an `IssueCardDto` carries it, not just this one.

## Issues

| Method | Path | What it does |
|---|---|---|
| GET | `/issues/{key}` | One issue, the full `IssueDto` |
| GET | `/issues` | Cards matching a filter - no parameters at all is the whole board |
| POST | `/issues` | Files one; lands leftmost/bottom unless a program's agent-files column exists |
| PATCH | `/issues/{key}` | Edits whichever fields the body names; null leaves a field alone. A `projectId` move keeps the issue's parent unless `parentKey` names another or is `""` |
| POST | `/issues/bulk` | One edit applied to many issues by key |
| DELETE | `/issues/{key}` | Deletes it; children are outdented, not deleted |
| POST | `/issues/{key}/move` | The drag: a column and the neighbours it landed between |

All eight are a key's. `GET /issues` takes a repeatable `projectId`, `type`, `statusId`,
`parentKey` (`""` means "no parent at all" - the one filter with no other way
to write it), `ancestorKey` (anything below at any depth, not the issue
itself), `text` (a substring of the title, plus an exact key match) and
`filedFromKey` (the issues filed from one issue; `""` means "filed from
nothing"), `pullRequestUrl` (exact match on the pull request an issue holds,
whether recorded with `hatch pr` or by a contribution record), `originInstance`
and `originKey` (exact match on a contribution record's origin; a trailing `/`
on the instance is ignored, and an issue with no record matches neither), all
optional and ANDed together. Every card, and `GET /issues/{key}`, carries
`contribution` - the record, or null - and a review row from `GET /work/review`
carries `branch`, the head branch a contribution recorded, null on the board's
own rows. `docs/hatch-planning.md` has these filters in
full, the bulk endpoint's own filters, and the `/plan` reads' arithmetic -
none of that is re-derived here.

`IssueCreateRequest` is `ProjectId`, `Type`, `Title`, an optional
`Description`, `ParentKey`, `ReadyAt`, `DueAt`, `OnePr`, `Priority`,
`PreApproval`, `FiledFromKey` and `LocalModel` - `OnePr` only on a `story` or a `bug`, refused on anything else,
`inherit`/`on`/`off` (`on` and `off` 409 under a project whose one-PR policy is
`enforced`, before anything is written); `Priority` and `PreApproval` take the level names the two
`PUT` routes below take, absent meaning `normal` and `none`, so an issue can be
born at a level in the one save, with no second event; `LocalModel` takes `none`, `prefer` or `require`
the same way, absent meaning `none`. `IssuePatchRequest`
is `Title?`, `Description?`, `Type?`, `StatusId?`, `ParentKey?`, `ReadyAt?`,
`DueAt?`, `PullRequestUrl?`, `ProjectId?`, `MoveDescendants?`, `WipOverride`,
`FiledFromKey?` - `""` clears `ParentKey`, `FiledFromKey`, `ReadyAt`, `DueAt` or `PullRequestUrl` (no issue key,
date or URL is ever legitimately empty, so the sentinel is unambiguous);
`ProjectId` has no clear sentinel, since an issue always belongs to some
project; a null `MoveDescendants` reads as `true`, and `false` leaves the direct children where they are, still under the moved issue. A move never reads an absent `ParentKey` as the clear. `IssueBulkEditRequest`
shares those fields, minus `Title`, `Description` and `PullRequestUrl`, which
only make sense one issue at a time, under `Keys`. Its answer,
`IssueBulkResultDto`, is `Changed`, `Unchanged`, `Failures` (each
`{key, reason}`) and `Rekeyed` (each `{from, to}`, for every issue a project
move actually carried, named or not). `IssueMoveRequest` is `StatusId`,
`AfterKey?`, `BeforeKey?`, `FromStatusId?`, `WipOverride` - the client names
neighbours, never a rank, and `FromStatusId` is a precondition: a card moved
since is refused rather than relocated.

**Refusals worth naming.** Create and patch 400 on an invalid title, type,
date or pull-request-URL shape, and on an illegal parent - a
cycle, or a type pairing the issue hierarchy refuses - or a task filed or retyped
under a story or bug in another project whose tasks share one pull request (the
sentence names the story). An ambition takes no parent (*an ambition takes no
parent*), and only an epic hangs under one. A key - never a person - filing or
moving an epic under an ambition whose every capability's newest verdict is
holds 400s, naming the ambition and saying nothing is left to carve; one
already hanging under that ambition may re-send its own `parentKey` with no
refusal, and an ambition with no capabilities, or with a capability still
without a verdict, is never refused this way. A key filing or moving an
epic under an ambition whose budget is spent 400s the same way, saying the budget
is spent and naming both amounts; an ambition with no budget is never refused
this way, and neither is a person. A patch that retypes an issue to
`ambition` 400s while the issue has a parent (unless the same patch clears
`ParentKey`) or a child that is not an epic, naming the first such child by key;
one that retypes an ambition 400s while a child could not hang under the new
type, and one that retypes an epic under an ambition to anything but `epic`
400s. No other retype looks at the children. A parent may otherwise stand in another project. A move into a protected
column 403s unless the caller is a live owner or approver, never a program,
and into a merges-pull-request column 400s if the issue carries no pull
request URL. A column change that would overflow the WIP section 409s with a
`{error, load, limit}` body unless `wipOverride: true` - and `wipOverride`,
and a cross-project `ProjectId` on a patch, are person-only even on this
key-open controller: a program sending either is 403'd before the issue is
even loaded. Create has person-only fields of its own the same way: a program
that names `priority`, `preApproval` or `localModel`, even as `normal` or `none`, is 403'd
before the project is looked up or a number is minted. The caller is recorded as the setter of a level named at create, the same way the two routes below record it. A person who names
`preapproved` must be an owner or approver of the project, and one who names
`express` must be allowed to move work into the board's send-back column - the
two questions `PUT /issues/{key}/pre-approval` asks - and a refusal files
nothing; `localModel` needs no approver. An unknown level name 400s, naming the levels. A project move 409s if any issue in the moving set - the root,
or the whole subtree unless `moveDescendants: false` - holds a live claim. It also 409s when
it would leave a one-PR story's tasks in another project, and 400s a task moving away from
such a story; a bulk batch reports that key as a failure and moves the rest.
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
| priority | PUT | `/issues/{key}/priority` | **person** | One of six named levels. Records the caller as `PrioritySetBy` (cleared at `normal`); a priority crosses a project line only while that person is live and has the descendant's project in scope |
| expedite | PUT | `/issues/{key}/expedite` | **person** | Legacy two-level alias for priority (`expedited`/`normal`) |
| pre-approval | PUT | `/issues/{key}/pre-approval` | **person** | One of three named levels. Records the caller as `PreApprovalSetBy` (cleared at `none`); a level crosses a project line only while that person could set it on the descendant - an owner or approver for `preapproved`, someone who may move work into the send-back column for `express` |
| express | PUT | `/issues/{key}/express` | **person** | Legacy two-level alias for pre-approval (`express`/`none`) |
| local-model | PUT | `/issues/{key}/local-model` | **person** | One of three named levels |
| human-work | PUT | `/issues/{key}/human-work` | **person** | `human`/`agent`/`inherit` - who works this ticket, walked live to the nearest ancestor that is not `inherit` |
| one-pr | PUT | `/issues/{key}/one-pr` | key | `on`/`off`/`inherit`, stories and bugs; `on`/`off` 409 under an `enforced` project |
| runner-tags | PUT | `/issues/{key}/runner-tags` | key | Replaces the ticket's own runner tags whole |
| dangerous | PUT | `/issues/{key}/dangerous` | key | Why running this ticket and its subtree is dangerous; `""` clears |
| omitted-runners | PUT | `/issues/{key}/omitted-runners/{runner}` | key | Omits a runner from this ticket and its subtree, with an optional reason; rewrites the reason if already omitted |
| omitted-runners | DELETE | `/issues/{key}/omitted-runners/{runner}` | key | Lifts the ticket's own omission of a runner; 404 for one only an ancestor carries |
| no-code | PUT | `/issues/{key}/no-code` | key | Finished with no code, and why; `""` clears; refused once a branch or pull request exists |
| contribution | PUT | `/issues/{key}/contribution` | key | That the issue came from outside: pull request URL, head branch, author login and association, origin. Write-once, unique per pull request URL; answers the `IssueDto` |
| release-note | PUT | `/issues/{key}/release-note` | key | The changelog's sentence for this issue. A key may also write it under a changelog task's live claim |
| stall | PUT | `/issues/{key}/stall` | open to anything the wall admits | A runner's own mark that it left the ticket where it found it, and why. On a claim whose session spent nothing (no tokens, ended in error) the board leaves the mark off, still answers `200` with the issue, and writes a `stall_declined` event instead |
| hold | PUT | `/issues/{key}/hold` | **person** | Parks/resumes a ticket; clears any stall mark |
| playbook | PATCH | `/issues/{key}/playbook` | **person, at the browser** | Per-ticket model/effort override, beating every playbook |
| landing | PUT | `/issues/{key}/landing` | key | What a repository holds for this issue's branch, per remote |

Several of these are a person's alone; checked one at a time rather than
assumed uniform. `one-pr`, `runner-tags`, `dangerous`, `omitted-runners`, `no-code`, `contribution` and `landing` are key-open despite
sitting beside five person-only fields: one-pr is the story's or bug's own say over its
project's default, which gives a key nothing it could not already do by
filing it that way; runner-tags is a statement about which runner may
pick the ticket up, not a power over the board, for the same reason, and
dangerous is the same kind of statement, and so is omitted-runners; no-code
is a statement of fact about the work, with the same reach as recording a pull
request through `PATCH`, which is also key-open; landing
is a fact a session or a poll reports about a repository and a sha, not a
power over the board. `stall` carries no
`[RequireRole]` at all, on the method or the class - it is reachable by
anything the wall admits, person or key, which is deliberate: a dispatcher
is exactly who leaves this mark.
`release-note` is key-open but claim-gated: a person may write it any time, a
program caller only while holding the issue's live claim, sent as
`?heldToken=`, and is refused with a 409 otherwise. One second claim is
accepted: the live claim of the changelog task the board recorded for that
issue, so the task's session may write a note nobody wrote earlier. The
`release_note_changed` event then carries `via`, the task's key. No other
issue's claim is accepted.

`AssigneeRequest` is `Kind?` and `Id?` - both or neither, and neither clears.
`PriorityRequest(Priority)` takes one of `paused`, `economy`, `low`,
`normal`, `expedited`, `emergency`; `ExpediteRequest(Expedited)` is the
two-level alias. `PreApprovalRequest(Level)` takes one of `none`, `express`,
`preapproved`; `ExpressRequest(Express)` is the two-level alias.
`OnePrRequest(Value)` takes `on`, `off` or `inherit`.
`IssueRunnerTagsWriteRequest(Tags)` is the whole list, replaced; trimmed,
lower-cased and deduplicated, at most 8 tags of at most 32 characters, none
with a space or comma. `OmitRunnerRequest(Reason)` is an optional reason,
trimmed, at most 200 characters; null or blank stores none. `ReleaseNoteRequest`
is an optional `Note` plus the query parameter `heldToken` (a guid, program
callers only) - null or blank clears it. `StallRequest(Why)` is one line,
truncated. `HoldRequest(Held)` is a boolean. `IssuePlaybookRequest(Model?,
Effort?)` leaves a field alone when null, and hands it back to the playbook
on `""`. `LandingWriteRequest` is `Remote`, `Sha`, an optional `Trunk`,
`ContainedBy` and `Runner`.

**Refusals worth naming.** `assignee` 400s on a kind with no id, or an
unknown actor. `priority`/`expedite` 400 an unparseable level name - the
alias is just a two-level write of the same field and the same trail.
`one-pr` 400s on anything but a story or a bug, 409s `on` or `off` under a project
whose one-PR policy is `enforced` (the project enforces its default, so the story
may carry no setting of its own - `inherit` is never refused by the policy, and a
value the story already holds is not refused and writes nothing), ahead of the
next refusal, and 409s once any descendant has
started - standing in the implementation column or to its right, since a
task begun on one branch cannot be moved onto the other. A move into an `enforced`
project (`PATCH` or `POST /issues/bulk` with `projectId`) returns an un-started override to inherit.
`one-pr` also 409s turning it on, or inherit from a default that is on, for a story or
bug with a task in another project, and a move 409s the same way when the setting would
read on in the target over a task in a third project; the sentence carries a count and
never the other project's task key. `PUT /projects/{id}/landing` 409s turning the one-PR
default on, and `PUT /projects/{id}/one-pr-policy` 409s enforcing an on default, while
a story that would read it has such a task, writing nothing. A move carries only the
descendants reached through links inside the moved issue's own project. `runner-tags` 400s
a blank tag, one with a space or comma inside, one over 32 characters, or a
list over 8; `404` for no such key; re-sending the list it already holds
writes nothing. `dangerous` 400s an absent reason (send `""` to clear) and one
over 200 characters after trimming; `404` for no such key; re-sending the reason
it already holds writes nothing. `omitted-runners` 400s a blank runner name,
one over the runner-name limit, and a reason over 200 characters after
trimming; `404` for no such key, and on DELETE for an omission the ticket
does not carry itself, naming the ancestor when one does; re-sending the
reason it already holds writes nothing. `no-code` 400s an absent reason (send `""` to
clear) and one over 200 characters after trimming, 409s while a pull request is
recorded or a counted merge check found a branch, and `404` for no such key;
re-sending the reason it already holds writes nothing. A later branch-finding
merge check or a recorded pull request clears the declaration with a
`no_code_set_aside` event, and a move into a column left of review clears it
with a `no_code_changed` one. `pre-approval` and
`express` gate by what is being set: raising to or lowering from
`preapproved` needs an owner or approver, 403 otherwise, since that is the
approve press given in advance; every other transition is gated like an
ordinary move into the board's send-back column, where one exists.
`local-model` takes `none`, `prefer` or `require`, writes a `local_model_changed`
event naming both sides, and needs no approver - any person may set it, and a key is
403'd. `IssueDto` and `IssueCardDto` carry `localModel`, `localModelOwn` and
`localModelFrom`, read live from the nearest ancestor that is not `none`. An unknown
name 400s, naming the levels. Nothing in dispatch reads the level yet.
`human-work` takes `human`, `agent` or `inherit` (`HumanWorkRequest(Value)`),
writes a `human_work_changed` event naming both sides, and needs no approver -
any person may set it, and a key is 403'd with "marking an issue as human work
is a person's call, not an agent's". `IssueDto` and `IssueCardDto` carry
`humanWork`, `humanWorkOwn` and `humanWorkFrom`, read live from the nearest
ancestor - including the issue itself - whose own value is not `inherit`;
`agent` with nothing set. Unlike `localModel`'s walk, an explicit `agent`
stops it the same as an explicit `human` does, so a descendant can opt back
out from under a human ancestor. An unknown value 400s, naming the three. A
human issue - its own or the nearest ancestor's - is refused by
`work/next`, `work/queue`, `work/{key}` and a hop with "human work - a
person does this one, and no runner picks it up", or "human work from
`HA-12` - ..." when inherited; moving it by hand (the board, `PATCH`) is
unaffected, since dispatch is the only reader of the override.
`IssueDto` and `IssueCardDto` also carry `preApprovalPassedOver` and `priorityPassedOver`: the key of the
nearest ancestor whose level was passed over at a project line because its setter has no say in this
ticket's project (or none was recorded), or null. Read live, beside `preApprovalFrom` and `priorityFrom`.
Beside each sits its own level name - `preApprovalPassedOverLevel` and `priorityPassedOverLevel` - the
level the passed-over ancestor carried, or null where nothing was passed over; never disclosed, since the
value already applies with no ancestor named.
`IssueDto` and `IssueCardDto` also say what a reader is told of a link with a hidden side,
where the project at the other end is outside the reader's scope. `parentBeyondScope` is true when the
parent is there and `parentKey` is null; `childBeyondScope` is true when some child is there and
`childKeys` holds none of it, with no count. `fromBeyondScope` lists the values whose from-key was blanked
because the ancestor it names is outside the scope - any of `priority`, `preApproval`, `localModel`,
`onePr`, `dangerous`, `preApprovalPassedOver`, `priorityPassedOver` and `pullPassedOver` - and the value
itself still applies. A `runnerTagsFrom` source or an `omittedRunners` entry from such an ancestor has a
blank key and `beyondScope: true`; a null omission `fromKey` still means the issue's own. All default to
false or empty, and a reader whose scope holds everything never sees one set.
`IssueDto` also carries `startedBy` (a name, or null for a key's move, an issue started before this was
recorded, or one never started) and `startedAt` (null only where it never crossed): written the first time
a move, a PATCH, a bulk edit or a hop carries the issue rightward across the board's send-back column, and
overwritten by the next crossing, including back to nobody for a key's.
`IssueDto` and `IssueCardDto` also carry `pullPassedOver`: the key of the issue's parent where that parent
is in another project and in progress, yet may not pull it - the person who started it (`startedBy`) has
no say in this ticket's project, or nobody started it - or null. Read live, beside `preApprovalPassedOver`.
`playbook` 400s an unknown model or effort - the same validation a playbook
file is held to. `landing` 400s a malformed remote, sha or trunk, and the
first write for a repository 400s if it omits `trunk`. Every field here
follows the house no-op rule: setting the value already held writes nothing
and moves no `UpdatedAt`.

`contribution` takes the record minus `filedAt` and `filedBy`, which the server
stamps from its clock and the caller. `authorLogin` and `authorAssociation` are
required; `pullRequestUrl` is optional (a mirrored ancestor has none) and is
read as `PATCH` reads one; `originInstance` and `originKey` come together or not
at all, and the instance is stored without a trailing `/`. Each field has a
length cap, and a body over one is a `400`. The first write records the issue's
contribution, a `contribution_filed` event naming the caller, and, where the
issue has no pull request of its own, sets it to the record's with a
`pull_request_changed` event. A repeat with the same `pullRequestUrl` is a `200`
that writes nothing, whatever else the body says. `409` for a different URL on
an issue that already has a record, for a URL another issue's record holds
(naming that issue), and for an issue whose own pull request is a different
one; `404` for no such key.

## The thread

| Method | Path | Who | What it does |
|---|---|---|---|
| GET | `/issues/{key}/comments` | key | The thread, oldest first |
| POST | `/issues/{key}/comments` | key | A note, a question, an answer, or a message |
| GET | `/issues/{key}/events` | key | The audit trail, newest first |
| GET | `/questions` | key | Every question in the house, oldest first |
| GET | `/issues/{key}/questions` | key | The same, for one issue |
| GET | `/ballot` | key | Every waiting question, each under the chain of issues that encloses it |
| POST | `/issues/{key}/messages/deliver` | key | Marks messages read; answers with the ones this call marked |
| PUT | `/issues/{key}/questions/{id}/level` | **person** | Sets an open question's level, `{ "level": "build" }` |
| POST | `/issues/{key}/questions/{id}/overturn` | key | Answers a look-back question against the loop and files the rework, `{ "answer": "..." }` |
| POST | `/issues/{key}/questions/{id}/promote` | **person** | Turns an answered question's answer into a principle of the ambition above it, `{ "principle": "..." }` |

Eight of the ten are a key's; the level and the promote are a person's. Both question routes take `?open=` (default `true`):
open is unanswered and either not terminal or asked at `merge`, which stays
open on a closed issue until it is answered; `open=false` is every question,
answered and obviated ones included.

`GET /ballot` answers the waiting questions of `/questions` as a tree, in one
read: `{ roots: [...], questions: [...] }`, each root the topmost issue of a chain with the
questions waiting beneath it. `?projectId=` narrows it to the projects named,
and repeats as `/plan`'s does. An issue appears once however many questions
sit under it; roots, and each node's children, run oldest waiting question
beneath first, and a node's own `questions` oldest first. `BallotDto` is
`Roots` and `Questions`. `BallotNodeDto` is `Key`, `ProjectKey`, `Type`, `Title`, `Excerpt`
(the first three non-blank lines of the description, markdown as written, cut
to 300 characters with a `.` appended when cut, empty when there is none),
`IsTerminal` (an ancestor can stand in a terminal column, and so can the issue a
waiting `merge` question is on; any other waiting question's issue never does), `Children` (more `BallotNodeDto`) and `Questions`
(`QuestionDto`s, as below).

`questions` is the same waiting questions as one list, in the order to answer
them, narrowed by `?projectId=` as `roots` is. Each `BallotQuestionDto` is
`Question` (a `QuestionDto`), `Holds`, and five look-back fields, null on a
question that is not a look-back. `Holds` is the issue the question is on and
every issue beneath it outside a terminal or deferred column, so a question on
an open leaf holds one and a look-back on a closed issue with nothing open
beneath it holds none. The order is the questions that are not look-backs
first, by `Holds` descending, then by level with `hold` before `build` before
`merge`, then oldest asked first; look-backs follow, oldest first. A look-back
also carries `WentAheadOn` (the recommended option's label, null when none is
marked), `IssueStatus` (the issue's column name), `ClosedAt`, `Merged` (whether
a status change marked `merged` is on its trail) and `Landings` (each a
`BallotLandingDto`: `Canonical` and `Sha`, empty when none is recorded).

`CommentDto` is `Id`, `Author`, `Body`, `Kind` (`""`, `question`, `answer` or
`message`), an optional `AnswersId`, an optional `Options`, `CreatedAt`, an
optional `DeliveredAt`, an optional `DeliveredTo`, and a `Level` that is set on
a question (`hold`, `build` or `merge`) and null on everything else, and an
`AnsweredAfter` that is true only on an answer given after its issue closed - set on the
answers inside a `QuestionDto`, false on every other comment, and derived when
read. A person sets the level with
`PUT /issues/{key}/questions/{id}/level`; a key cannot. It also carries an
optional `AuthorKind` (`person` or `key`; null on a comment written before
the column existed, or by the board itself).
`CommentCreateRequest` is `Body`, an optional `Kind` (omitted means an ordinary
note), an optional `AnswersId`, an optional `Options`, and a `Level` that it
only carries so it can be refused: a comment sent with one is a `400`, since a
level is set by a person after the question is asked. `QuestionOptionDto` is `Label`, an
optional `Detail`, `Recommended` - at most eight options per question, at
most one recommended, labels unique case-insensitively. `QuestionDto` is
`Id`, `IssueKey`, `IssueTitle`, `Body`, `AskedBy`, `AskedAt`, an optional
`Options`, `Answers` (oldest first - an empty list is what "open" means, no
second flag), `Obviated`, `Level` (never null: `hold` unless a person set another), and
`LookBack` - true for an open `merge` question whose issue has reached a
terminal column, derived when read - and an optional `AmbitionKey`, the key of the
ambition above the question's issue at any depth, null where none stands above it,
derived when read. `IssueEventDto` is `Id`, `Actor`, `Kind`, an
optional `Payload`, `At`. `MessageDeliverRequest` is an optional `Ids` - exact
ids, or null for every unread one; ids that are not unread messages on the
issue are ignored rather than refused.

`PUT /issues/{key}/questions/{id}/level` takes a `QuestionLevelRequest`, one `Level`
named `hold`, `build` or `merge`, and answers with that question's `QuestionDto`. It is
a person's, as every person-only route is: a key gets `403` with `key_not_accepted`, and
nothing is written. It needs no owner or approver role, since the person is whoever may
answer the question. It `400`s with a sentence for a level that is not one of the three,
for a comment that is not a question on that issue, for a question that is already
answered, and for `build` or `merge` on a question with no recommended option, since a
session would have nothing to build on. `hold` is always allowed on an open question.
Sending the level the question already holds is a `200` that writes nothing. A change
writes a `question_level_changed` event, payload `{ questionId, from, to }`, with the
person as actor.

`POST /issues/{key}/questions/{id}/overturn` takes a `QuestionOverturnRequest`, one `Answer`:
an option's label, or a sentence in prose. It is for a look back, accepted in either of two
cases: an unanswered `merge` question whose issue has reached a terminal column, or a
`build` or `merge` question every one of whose answers is a key's - whatever column its
issue stands in, open or closed. It answers with a `QuestionOverturnDto`: `Question`, that
question's `QuestionDto` now answered, and `TaskKey`, the key of the task filed. Whoever may
answer may overturn, so a key may, as it may post a comment. In one save it writes the
answer as a comment of kind `answer` under the caller's name, with the body as sent, and an
`answered` event as any answer writes, and files one task. The task is filed from the issue
(`filedFromKey`) and hangs under it; where the issue is a task and cannot take one, it hangs
under the nearest ancestor that can, and under nothing where none can. It lands where a
filing lands: the leftmost column for a person, the agents' column for a key. Its title is
`Bring {key} in line with the answer`. Its description says what was decided - a session's
own answer where one answered, else the recommended option's label - what the person
decided instead, who overturned it and when, that the work is to be brought in line with
the answer, and names the issue key and question id, with the question's body quoted.
Keeping the decision needs no route of its own: the page answers with it through
`POST /issues/{key}/comments`, as for any option. It `400`s with a sentence for an empty or
over-length answer, for a comment that is not a question on that issue, for a question a
person has answered, for a question a key answered at `hold`, for a question not asked at
`merge` with no answers at all, and for one whose issue has not reached a terminal column
with no answers at all; nothing is written. An unknown key is a `404`.

`POST /issues/{key}/questions/{id}/promote` takes a `QuestionPromoteRequest`, one
optional `Principle`, and answers with a `QuestionPromoteDto`: `AmbitionKey`, the key of
the ambition the sentence landed on, and `Principle`, the sentence as written there. It
is a person's, as every person-only route is: a key gets `403` with `key_not_accepted`,
and nothing is written. The sentence is `Principle`, trimmed, or the question's newest
answer when none is sent, then collapsed - every run of whitespace, newlines included, to
one space - before it is checked or compared. It `400`s with a sentence for an empty
sentence, one over 500 characters, a comment that is not a question on the issue, a
question with no answer, an issue with no ambition above it at any depth, and a sentence
already a bullet under the ambition's `## Principles` heading, compared trimmed and
without case. It appends `- <sentence>` after the last bullet under that heading, adding
the heading itself when the ambition's description has none, and writes two events on the
ambition in the one save: `redescribed`, payload `{ from, to }` as any description edit
writes, and `principle_promoted`, payload `{ questionId, issueKey, principle }`. An
unknown key is a `404`.

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
| GET | `/search` | Full-text search across title, previous titles, description, comments and work log, paged |
| GET | `/plan` | Every epic's and ambition's rollup, the Plan page's one request; a tree may cross projects |
| GET | `/plan/{key}` | One issue's rollup and its children's, a child in another project included |
| GET | `/ambitions` | Every ambition with its capabilities counted four ways and the epics open under it; takes a repeatable `projectId` |
| GET | `/assignees` | Everybody an issue could belong to, plus who the caller is |

All five a key's. `/search` takes `q`, a repeatable `projectId`, `type`, `statusId`,
`createdFrom`/`createdTo`, `closedFrom`/`closedTo`, `limit` (1-200, default
50) and `offset`; with `q`, best match first and closed-ahead-of-open among
equals, without it newest first (closed, else created). Each hit's `matched`
lists which of `title`, `previous-title`, `description`, `comment`,
`work-log` found it, in that order; the hit also carries `previousTitles`,
the issue's own previous titles newest first, whether or not
`previous-title` matched - empty on an issue never retitled. `/plan` takes an
optional, repeatable `projectId`; with none, an epic under an epic of another
project is nested under it, and with one, an epic whose parent is outside the
answer is a root, its card carrying `parentKey` or `parentBeyondScope`, and
`childBeyondScope` where its tree goes on past the reader's scope. `/plan`
roots on parentless epics and ambitions; an ambition comes back among the
roots with its epics as entries beneath it, and never under `loose`.
`/assignees` takes nothing, and answers an
`AssigneeDirectoryDto(Me?, Assignees)`. `docs/hatch-planning.md` has the
plan's arithmetic in full.

`/ambitions` takes an optional, repeatable `projectId`, the same filter
`/plan` takes, and answers one `AmbitionRowDto` per ambition the caller may
see, in board order: column sort order, then rank, then id. Its fields are
`Key`, `ProjectKey`, `Title`, `StatusId`, `StatusName`, `Limit` (the
ambition's `wipLimit`, read as one where it is unset), `Capabilities`,
`Holds`, `InFlight`, `WaitsOnPerson` and `NotStarted` (the four counts,
adding up to `Capabilities`), `Open` (its open epics, in rank order), and
`ChildBeyondScope` (as `/plan` reads it: some child sits in a project the
caller's scope does not hold). `Open` carries one `AmbitionOpenEpicDto` per
open epic: `Key`, `ProjectKey`, `Title`, `StatusId`, `StatusName`, `Serves`
(the positions, ascending, of the capabilities of its own ambition that it
serves), `WaitsOnPerson`, and `Why` - one sentence saying so, naming no key,
null unless `WaitsOnPerson` is true. Every number on the page is derived on
read; nothing behind it is stored.

**Refusals.** `/search` 400s an invalid `type`, a `limit` outside 1-200, a
negative `offset`, or an unparseable date bound. `/plan/{key}` 404s an
unknown key; `/plan` with a `projectId` naming no project answers an empty
plan rather than a 404, since an empty or just-deleted project is a legal
thing to look at - true of each id when several are given. `/ambitions`
answers the same way: a `projectId` naming no project contributes nothing to
the list rather than 404ing, true of each id when several are given.

## Attachments, images and import

| Method | Path | Who | What it does |
|---|---|---|---|
| GET | `/attachments` | key | Describes the files named in `ids`, in the order asked |
| GET | `/attachments/{id}` | key | Downloads one, always as a file under its stored name |
| POST | `/attachments` | **person** | Uploads one file (multipart `file`, up to 25 MB), linked to a project with `projectId` |
| DELETE | `/attachments/{id}` | **person** | Removes one |
| GET | `/images/{id}` | key | The inline view of an attachment that is an image |
| POST | `/import/preview` | key | What uploaded `.md` files would become; reads/writes nothing |
| POST | `/import/preview-text` | key | The same, for a pasted title and body |
| POST | `/import` | key | Files the approved tree: epic per doc, story per phase, task per checkbox |

`AttachmentDto` is `Id`, `FileName`, `ContentType`, `Size` and `CreatedAt`; the
bytes are only ever served by `GET /attachments/{id}`. `GET /images/{id}` is the inline view of an attachment, and answers 404 for a row that is not one of the four image types.
A preview's answer is the parsed tree, one doc per
file - the shape is internal to the importer and not worth repeating here.
`preview-text`'s body is `PastedPlan(Title, Body)`. The import write's body
is `ImportRequest(ProjectId, Docs)`, and its answer is
`ImportResultDto(Epics, IssueCount)`, each epic an
`ImportedEpicDto(Filename, Key, Title, StoryCount, TaskCount)`.

**Refusals.** Attachment upload and delete 403 a program caller outright -
files are attached by a person, not by an agent. Upload's `projectId` is
optional, checked against the caller's scope, and 404s when it names no
project the caller can see, before the file is read. Upload 400s a request with
no file, an empty file, and a file over 25 MB, and stores nothing; an image is
stored under its detected type whatever its size, anything else under the
type it was sent with, or `application/octet-stream` when that is missing or
not a valid type. The name is the last path segment of the one sent, cut to
255 characters, or `attachment` when nothing is left. `GET /attachments/{id}`
404s an unknown id and answers `Content-Disposition: attachment` with
`nosniff`, so a file labelled `text/html` is saved and never rendered.
`GET /attachments` takes `ids` as a comma-separated list: it leaves out an id
that is unknown, repeated or not a Guid, and 400s more than 100. `DELETE`
404s an unknown id and writes no issue event. `preview` 400s a non-`.md` filename, a file over 1 MB, or more
than 25 files. `preview-text` 400s an empty title or body, a title over the
issue title cap, or a body over 1 MB. `import` 400s an empty or over-25
`Docs` list, 404s an unknown `ProjectId`, 409s a board with no columns, and -
like create and a project move - retries a numbering race up to five times
before 409ing.

## Dreams

| Method | Path | What it does |
|---|---|---|
| GET | `/dreams` | Dreams matching a search and/or a project, paged |
| POST | `/dreams` | Adds one |
| PATCH | `/dreams/{id}` | Edits one |
| DELETE | `/dreams/{id}` | Removes one |
| PUT | `/issues/{key}/dreams` | Replaces the set of dreams this issue drew on |
| GET | `/issues/{key}/dreams` | The dreams this issue drew on, newest first |
| POST | `/issues/{key}/remand` | Shelves this issue and the open work under it into a deferred column, and posts a dream from it |
| POST | `/dreams/weave` | Files an epic in the breakdown column, briefed to be woven from the dreams (a person's, not a key's) |

A dream is a line somebody wanted said - leaning toward a project, or loose.
`/dreams` takes `q` (any word, not every word - unlike `/search`), a repeatable
`projectId` (omitted or empty matches every dream, leaning and loose alike), `includeNoProject` (`true` adds every dream with no project to a non-empty `projectId` list, in the same ordered, counted, paged answer; with no `projectId` it changes nothing; default `false`), `issueKey` (only
dreams woven into that issue), `remandedFromKey` (only dreams
remanded from that issue), `loose` (`true` for only dreams woven into no
issue at all), `limit` (1-200, default 50) and `offset`; with `q`, best match
first, else newest first. `issueKey` and `loose` each AND with `q` and
`projectId` as one more filter on the same query - each is a single
`EXISTS`/`NOT EXISTS` against `DreamThreads`, not a lookup per dream;
`remandedFromKey` is the same `EXISTS`, against `DreamRemands`. Its
answer is `DreamsPageDto(Dreams, Total)`, each a
`DreamDto(Id, Body, Author, Source, ProjectId, ProjectKey, CreatedAt, UpdatedAt, WovenInto, RemandedFrom)` -
`WovenInto` the keys of the issues that drew on this dream, ordered by
project key then issue number, empty where nothing has. `RemandedFrom` the
keys of the issues the dream was remanded from, the root first and then by
project key and issue number, empty for a dream no remand made.
POST's body is `DreamCreateRequest(Body, Source?, ProjectId?)`; `Author` is
the caller. PATCH takes a raw JSON object rather than
`DreamCreateRequest` - `{"body"?, "source"?, "projectId"?}` - so that a key
left out leaves that field alone, where a key present with a JSON `null`
clears it (`source` and `projectId` only; `body` takes a string, never
`null`). A PATCH that changes nothing writes nothing and leaves `UpdatedAt`
alone.

`PUT /issues/{key}/dreams` takes `IssueDreamsWriteRequest(DreamIds)` - the
whole set this issue should carry afterwards, duplicates collapsed. A request
naming the set already there writes nothing, logs nothing and leaves
`UpdatedAt` alone; otherwise it writes a `dreams_woven` issue event (see
docs/hatch.md) and answers the resulting `DreamDto[]`, newest first. `GET
/issues/{key}/dreams` is the same read, with no write.

`POST /issues/{key}/remand` takes `DreamRemandRequest(StatusId, Keys?)` and
answers `201` with `DreamRemandResultDto(Dream, Changed, Unchanged)`. `{key}`
is the root, read the way `POST /issues/{key}/move` reads one. `StatusId` is a
deferred column; `Keys` are the root's descendants to shelve with it (none
shelves the root alone, duplicates collapse, at most 500). It writes one dream -
the root's title, a blank line, then its description, cut to a comment's
length, with the root's key as `Source`, the root's project and the caller as
author - and a `DreamRemands` row for the root and for each descendant that
moves. The root and each of those descendants move the way a move moves them
(`status_changed`, `ClosedAt`, no-code declaration, bottom of the column,
and the deferral note on whatever waits on them), and each also gets a
`remanded` issue event (see docs/hatch.md). A descendant already in a terminal
or a deferred column stays where it is and is listed in `Unchanged`, as is a
root already in the column - which is still remanded. `Changed` and
`Unchanged` are display keys.

**Refusals.** `/dreams` 400s a `limit` outside 1-200 or a negative `offset`,
the same as `/search`, and a malformed `issueKey` or `remandedFromKey`. POST 400s a blank or
over-length `Body`, or a `Source` over 500 characters; both POST and PATCH
404 an unknown `ProjectId`. PATCH and DELETE 404 an unknown `{id}`. `PUT
/issues/{key}/dreams` and the `GET` beside it 404 an unparseable or unknown
`{key}`; `PUT` 400s naming the first dream id in `DreamIds` that does not
exist, writing nothing when it does. `POST /issues/{key}/remand` 404s an
unparseable or unknown `{key}` and 400s, writing nothing, a `StatusId` that is
no column, one that is not deferred (naming it), more than 500 `Keys`, and the
first key that is not a descendant of `{key}` (`"{key} is not under {root}"`).

`POST /dreams/weave` is the one route under `/dreams` that files an issue
rather than a dream - a person's, never a key's. Its body is
`DreamWeaveRequest(ProjectId?, Mode?, Seed?, DreamIds?)`. `Mode` is one of
four: `seed` draws the thread out of the dreams that bear on a free-text
`Seed`; `pressing`, `common` and `next` take no seed and draw, respectively,
on the thread the dreams return to with the most urgency, the thread the
most dreams touch, and the nearest step toward the vision the dreams
describe. `DreamIds` are the dreams the person had on screen when they
pressed, if any - distinct, in the order sent, and merely a starting point
handed to the session that weaves, not a binding set. The epic is born in
the leftmost column a playbook row covers `epic` from (ties breaking the
same way `MatchReflection` ranks them - a project-specific row before a
board-wide one, then specificity, then id), titled and described by
`DreamWeave.Title`/`Describe`, and answers `201` with the resulting
`IssueDto`.

**Refusals.** `403`s a key outright. `400`s an unknown
or missing `Mode`; a missing or blank `Seed` under mode `seed`; a `Seed`
given under any other mode; a missing `ProjectId`; and the first id in
`DreamIds` naming no dream, in the wording `PUT /issues/{key}/dreams` uses.
`404`s an unknown `ProjectId`. `400`s with no playbook row covering `epic`
on the project, naming the project's key and the Playbooks page.

## Capabilities

| Method | Path | What it does |
|---|---|---|
| GET | `/issues/{key}/capabilities` | An ambition's capabilities, in position order |
| POST | `/issues/{key}/capabilities` | Adds one, at the end or at a position |
| PATCH | `/issues/{key}/capabilities/{id}` | Edits one's statement or how-to-tell line |
| PUT | `/issues/{key}/capabilities/order` | Puts them in the order the body names |
| DELETE | `/issues/{key}/capabilities/{id}` | Removes one and closes the gap |
| GET | `/issues/{key}/capabilities/{id}/verdicts` | A capability's verdicts, newest first |
| POST | `/issues/{key}/capabilities/{id}/verdicts` | Records a verdict on a capability |
| GET | `/issues/{key}/passes` | An ambition's passes, with the verdicts and comments each gave |
| GET | `/issues/{key}/changes` | An ambition's changes, newest first, with their outcomes |
| POST | `/issues/{key}/changes/{eventId}/outcomes` | Records one epic's outcome on one change |
| GET | `/issues/{key}/budget` | An ambition's dollar budget, what it has spent, and whether that is spent |
| PATCH | `/issues/{key}/budget` | **person** - sets, changes or clears the budget; answers the same body |

A capability is one thing an ambition says has to be true, and a line saying
how anyone would tell. Every route answers `400` on an issue that is not an
ambition, with a sentence naming its type: `only an ambition takes
capabilities - AER-12 is a story`. A key is open on all five, but may write
(POST, PATCH, PUT or DELETE) only while the ambition stands left of the
implementation column: in that column, past it, in a terminal column or in a
deferred one it gets `403`, and a person is never refused. Reading is open to
a key anywhere.

Each is a `CapabilityDto(Id, Position, Statement, HowToTell, CreatedBy,
CreatedAt, UpdatedAt, State, NewestVerdict, ServedBy)`. `Position` runs 1 to n with no
gaps, so a read is already in order and a write leaves it so. GET answers
`CapabilityDto[]`, empty for a fresh ambition. `State` and `NewestVerdict` are
derived on read, in one query for the whole list, and never stored - see
"A capability's verdicts" below. `ServedBy` is the keys of every epic that
serves the capability, in key order, empty when none does - see "Serves".

POST takes `CapabilityCreateRequest(Statement, HowToTell, Position?)` and
answers `201` with the row. `Position` left out appends; given, it must be
within `1` to one past the last, and the rows from there on shift down by one.
A blank `Statement` or `HowToTell`, or one over 300 characters, is a `400`.

PATCH takes `CapabilityPatchRequest(Statement?, HowToTell?)`. A field left out
keeps its value; one sent as an empty string is a `400`. An unknown `id`, or
one that is another ambition's, is a `404`. A PATCH that changes nothing writes
nothing and leaves `UpdatedAt` alone. It answers the row.

PUT `/order` takes `CapabilityOrderRequest(Ids)` - every capability's id once,
in the wanted order - and answers the reordered `CapabilityDto[]`. A list that
misses an id, repeats one, or names an id that is not this ambition's is a
`400` and changes nothing; a list naming the order already there writes
nothing.

DELETE answers `204`, and shifts every row above it down by one. An unknown
`id` is a `404`.

Each write that changes something writes one issue event on the ambition:
`capability_added`, `capability_edited`, `capability_moved` (one per reorder,
however many rows moved) or `capability_removed` - see docs/hatch.md. Retyping
an ambition away from `ambition` is refused while it holds any capabilities,
and deleting the ambition deletes them.

### A capability's verdicts

A verdict records whether one capability holds, as of a sha. This is not the
check session's verdict against a live claim - see `EfHatchVerdict` and `POST
/work/{key}/verdict` above - the entity, the route and the event are all named
differently so the two are never confused.

POST `/issues/{key}/capabilities/{id}/verdicts` takes
`CapabilityVerdictRequest(Verdict, LookedAt, Sha)` and answers `201` with the
row. `Verdict` is one of `holds`, `does_not_hold` and `cannot_tell`; any other
word is `400`, naming the three. A blank `LookedAt` or `Sha` is `400`.
`LookedAt` is at most 500 characters and `Sha` at most 64; over either is
`400`, naming the bound. An unknown `key` is `404`; an issue that is not an
ambition is `400`; an unknown capability id, or one that is another
ambition's, is `404`. Unlike the other capability routes, this one is never
gated on the column - a key may write a verdict while the ambition sits in
any column, including past implementation, in a terminal column, or deferred.

GET answers the capability's `CapabilityVerdictDto[]` newest first. Each is a
`CapabilityVerdictDto(Id, CapabilityId, Verdict, LookedAt, Sha, Author, At)`.

A capability's `State` is its newest verdict's word, or `not_checked` with
none - the four states a capability can read as. `NewestVerdict` is the row
`State` was read from, or null. Both are derived on read and never stored.

A verdict writes one `capability_judged` event on the ambition, with payload
`{ id, position, verdict, sha, author }` - see docs/hatch.md. Removing a
capability removes its verdicts; the events on the ambition remain.

### An ambition's passes

GET `/issues/{key}/passes` answers every pass an ambition has had, newest
first, as `AmbitionPassDto[]`. `400` on an issue that is not an ambition,
naming its type; `404` on an unknown key. Open to a key and a person, like
the routes above.

A pass is one work log row on the ambition that ran from the implementation
column and was released `worked` or carried no outcome; a row released
`dropped`, `preempted`, `recused`, `unservable`, `abandoned` or `unstarted`,
or one from any other column, is left out. Each is an
`AmbitionPassDto(RowId, SessionId, StartedAt, EndedAt, Title, Summary,
CanRecordVerdicts, Sha, Verdicts, Comments)`. `CanRecordVerdicts` is true
where the row is typed `ambition` and ended in the column it started in,
false for an `epic` row. `Sha` is the sha its `Verdicts` share, or null where
they differ or there are none.

`Verdicts` is every verdict on the ambition's capabilities timed between the
row's start and end, one per capability, as
`AmbitionPassVerdictDto(Id, CapabilityId, Position, Statement, Verdict,
LookedAt, Sha, Author, At, Changed)`. `Changed` is true where the verdict's
word differs from the one the next older pass gave the same capability; a
capability the older pass did not judge is not changed. `Comments` is the
`CommentDto[]` posted on the ambition between the row's start and end,
oldest first.

A verdict timed inside no row's window answers as a pass of its own, with no
`RowId` or `SessionId`, dated at the verdict's own time - so nothing recorded
is ever hidden.

A pass row that names a change (`changeEventId` on the work log entry) is left
out here - it is a session answering that change, not a pass over the
capabilities, and it shows up instead on `GET /issues/{key}/changes` below.

### An ambition's changes

A change is read, never stored: it is every `capability_added`,
`capability_edited` and `capability_removed` event on one ambition, grouped
by the work log rows that worked them out. `capability_moved` is never part
of one. See docs/hatch.md, "ChangeOutcome".

GET `/issues/{key}/changes` answers the ambition's changes, newest first, as
`ChangeDto[]`. `400` on an issue that is not an ambition, naming its type,
with the sentence `only an ambition has changes - AER-12 is a story`; `404`
on an unknown key. Open to a key and a person, reading with the same scope
`GET /issues/{key}/capabilities` does. An ambition never changed answers an
empty list.

Each is a `ChangeDto(Id, SavedAt, WorkedOut, WorkedOutAt, Entries, Outcomes)`.
`Id` is the id of the change's newest event. `WorkedOut` is true where a work
log row with outcome `worked` names this change's id in `changeEventId`;
`WorkedOutAt` is when, or null. A change not yet worked out is the first in
the list.

`Entries` is one `ChangeEntryDto(EventId, Kind, CapabilityId, Position,
StatementBefore, HowToTellBefore, StatementAfter, HowToTellAfter, Actor, At)`
per event, oldest first. `Kind` is `added`, `changed` or `removed`. The
before and after lines are null on the side that does not apply - an add has
no before, a removal no after - and both are null where the payload will not
parse, a tolerant read rather than a failed one.

`Outcomes` is one `ChangeOutcomeDto(ChangeEventId, EpicKey, EpicTitle,
EpicColumn, Outcome, Why, RecordedBy, RecordedAt, Decision)` per epic that has
one. `EpicColumn` is the epic's column now, by name - an outcome is a record;
the column stays the board's. `Decision` is a `ChangeDecisionDto(Decision,
DecidedBy, DecidedAt)` or null - nothing writes it yet.

POST `/issues/{key}/changes/{eventId}/outcomes` takes
`ChangeOutcomeRequest(EpicKey, Outcome, Why)` and answers the
`ChangeOutcomeDto`: `201` for a new row, `200` for a replace. `key` is the
ambition; it accepts a key in any column, as `POST .../verdicts` does -
recording what a change did to an epic is a judgment about the work, not a
change to what the ambition asks for. These are all `400`, in order, and
each leaves the table as it was:

- the issue is not an ambition;
- `eventId` is not an added, edited or removed event of it;
- `epicKey` does not parse, names no issue, or is not an epic directly under
  the ambition (not merely beneath it, the way `serves` reads);
- `Outcome` is not `carries_on`, `not_touched` or `serves_nothing`, naming
  the three;
- `Why` is blank after trimming, or over 300 characters.

Recording again for the same epic in the same change replaces the row's
`Outcome`, `Why`, `RecordedBy` and `RecordedAt`. A row a person has already
decided is refused with `409`, naming the epic, and nothing is written. A
recording identical to the one already stored writes nothing and answers
`200` with the row as it stands.

A write (new or replaced, never a no-op) writes one `change_outcome_recorded`
event on the epic - not the ambition - payload `{ ambition, changeEventId,
outcome, why }`, `ambition` the ambition's display key. See docs/hatch.md,
"Issue event". It shows on the epic's own History.

## Budget

`GET /issues/{key}/budget` answers `IssueBudgetDto(BudgetUsd, SpentUsd, UnpricedSessions, Spent)` to a key and to a person alike.
`BudgetUsd` is the budget, or null when none is set. `SpentUsd` is the summed cost of every work log row on the ambition and on
everything beneath it, at any depth and in any project. `UnpricedSessions` counts the sessions that carried no price and so added nothing.
`Spent` is true when there is a budget and `SpentUsd` has reached it, and is false for an ambition with no budget, whatever it has cost.
The sum is worked out on each read and never stored, and it does not depend on who reads it: a person whose scope hides a project
that holds one of the ambition's epics still reads the whole figure.

`PATCH /issues/{key}/budget` takes `IssueBudgetRequest(Amount?)` and answers the same body. `Amount` is a positive number of dollars,
to the cent, up to 1,000,000,000: `50` or `12.50`, not `$5`, `1e3`, `12.345`, `0` or a negative. Left out, it changes nothing; an empty string
(or whitespace) clears the budget. Anything else is a `400` with a sentence. Both verbs answer `400` on an issue that is not an ambition,
naming its type: `only an ambition takes a budget - AER-12 is a story`. The write is a person's alone: a key gets `403`, with the wall on
or off, and no other route - the issue patch, the bulk edit - takes a budget field. A change writes a `budget_changed` event holding
the old amount, the new one and who made it. A spent budget stops a key filing an epic under the ambition, and the sum that refusal reads is the one this route returns; the route itself only holds and reads it.

## Serves

| Method | Path | What it does |
|---|---|---|
| GET | `/issues/{key}/serves` | The capabilities an issue serves |
| PUT | `/issues/{key}/serves` | Replaces the set of capabilities an epic serves |

An epic serves the capabilities of the ambition it hangs beneath. GET works on
an issue of any type and answers `ServedCapabilityDto[]` - `AmbitionKey,
CapabilityId, Position, Statement, State` - ordered by ambition key and then
position, empty for an issue that serves none. `State` is read the way
`CapabilityDto.State` is: the newest verdict's word, or `not_checked`.

PUT takes `IssueServesWriteRequest(CapabilityIds)` and answers the same list.
It replaces the whole set, so an empty list clears it, and a repeated id counts
once. It is open to a key in any column. These are all `400`, and each changes
nothing:

- the issue is not an epic: `only an epic serves capabilities - AER-12 is a
  story`;
- an id names no capability: `there is no capability 9`;
- an id belongs to an ambition the epic does not hang beneath. The sentence
  names the capability and the ambition it belongs to. An epic hangs beneath an
  ambition at any depth, through any number of epics and reflections; one under
  no ambition serves nothing.

A PUT that changes the set writes one `serves_changed` issue event on the epic,
payload `{ from, to }`, both sides every capability id in ascending order. A
PUT naming the set already there, in any order, writes no event and leaves
`UpdatedAt` alone. Unknown `key` is `404`.

Patching an epic's `parentKey` is refused when it would leave the epic - or
an epic below it - serving a capability of an ambition it would no longer hang
beneath, naming the capability. A move that stays under the same ambition is
unaffected. Deleting the epic or the capability removes the link alone.

## The loop's own routes

Everything above is a resource the board itself owns. What follows is what a
runner calls to do its own work, and what the operator reads to watch it -
the second half of this page, after the board's own.

### Work: what a runner asks the board for

| Method | Path | What it does |
|---|---|---|
| GET | `/work/next` | The next clear issue an unattended pass would pick up; `?dangerous=false` for a runner that refuses issues flagged dangerous; `?runner=` names the caller's own runner, so a ticket it let go of is left to a different live runner, a ticket it is omitted from is left out, and a move on a model its key refuses (see `/runners/{name}/refused-models`) is folded; `?model=` (repeatable) and `?agent=` say what the caller's runner offers, so a move whose model it does not offer is folded, nothing sent folds nothing, and a bad entry is a `400`; `?has=` (repeatable, `id` or `id@version`) says what development dependencies the runner has, so a move that runs the issue's code is folded when a bound repository's `.hatch/dependencies.json` declares one it does not satisfy, or when that file is invalid - absent folds nothing, and a bad entry is a `400` |
| GET | `/work/queue` | What a whole pass would do, blocked issues included, each with the sentence saying why; `?dangerous=false`, `?runner=`, `?model=`, `?agent=` and `?has=` as on `/work/next` (`?asRunner=` reads the runner's own row instead, and a row that never reported models or dependencies folds nothing; `?runner=` and `?asRunner=` agree on the key's refused models); where that key holds live loop sessions, `asRunner` and the let-go peer check judge it off its beat row and those sessions together, and a key with none answers exactly as before; a call with no key and neither `tag` nor `dangerous` sent is the board's own scan, so a person's board-wide read (wall off) folds nothing on tags or the danger flag; `?takers=true` (default `false`) judges, for every clear row, which live runners could take it right now, adding `takers` (runner names, sorted; null when not asked for; empty when none can) and `untaken` (one `{runner, reason}` per live runner that cannot, the fold's own sentence verbatim; filled in only when `takers` holds fewer than two names, empty otherwise, null alongside a null `takers`) - `hatch queue` and the web queue ask for this, the runner loop's own poll does not |
| GET | `/work/review` | Every issue in review or merges this caller holds a checkout of, and what the board holds about its branch |
| GET | `/work/{key}` | The same dispatch answer for one named issue; `?dangerous=false`, `?model=`, `?agent=` and `?has=` as on `/work/next` (a model the caller's runner does not offer, or a dependency it lacks, comes back blocked); `?runner=` names the caller's own runner - the calling key's own name where none is sent - so an issue omitted for it, or on a model its key refuses, comes back blocked |
| POST | `/work/{key}/hop` | Carries an express issue, or a child its parent pulls, across its column with no session, folded the same as any other move - runner tags and omissions included (`?runner=` names the caller's own runner) |
| POST | `/work/{key}/verdict` | Applies a check session's recorded verdict - passes it on, sends it back, or holds it for a person on a second fail in a row |
| POST | `/work/{key}/merged` | Advances a reviewed issue whose pull request the forge says merged; of two reports of one merge, the first moves the issue and the second gets a `409` |
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
holds. The `WorkDto` also carries `model`, the model the session runs on, and
`modelIsLocal`, true where it is the playbook row's local model because the
ticket requires it, or prefers it and the asking runner runs it; and
`modelFellBack`, true where the ticket prefers a local model and the row names
one, yet the session runs on the row's ordinary model. `model` is the same
value as `playbook.model`, the issue's own override first, and both are null on
a hop, as `playbook` is.
The `WorkDto` also carries `buildPast`, the open questions on the issue and the
issues above it that the session may build past: each is at `build` or `merge`
and has a recommended option. It is a second list beside `questions`, which
stays the ticket's own, and is null where there are none.
The `WorkDto` also carries `answering`, null on every dispatch but one: an
ambition aimed at its answering row, whose `kind` is then `answer`. It names the
question the session is to answer - `issueKey`, `issueTitle`, `questionId`,
`body`, `options`, `level` (`build` or `merge`), `askedBy` and `askedAt` - and
`playbook` is the row marked *answers questions*. Which question, and when none
qualifies, is in [what a session may answer](questions-and-decisions#what-a-session-may-answer).
Each row of `GET /work/queue` carries `answering` too, as one sentence naming the
issue and the question, and null on every other row.

`next`, `queue` and `{key}` take `remote`, `standing`, `clones` to declare a
caller's checkouts (any one opts into the repository fold), plus
`ancestorKey` to narrow to one corner of the board and `mine` to narrow to
the caller's own tickets; `next` and `queue` also take `project`, the project
keys it is bound to - repeatable, one value per occurrence
(`?project=FIL&project=AER`). `next`, `queue` and `hop` also take `tag`, what
the caller's runner advertises, repeatable the same way (`{key}` takes it
too). A call made with an API key that sends no `tag` is judged on that key's
own runner tags, the list set on the Runners page and answered back on a
heartbeat, or none where the key has no config; a `tag` sent replaces them,
never joins them. A call with no key and neither `tag` nor `dangerous` sent
is the board's own scan, judged on no tags at all - so a person's board-wide
read folds nothing on tags - but either sent is still a declaration, exactly
as a keyed caller's. An
absent or empty `project` folds nothing, so a caller that never declared a
binding sees every project as before; a key no project has is refused with a
400 naming it on `next`. `{key}` and `next` also take `heldToken`, a claim
the caller already holds, so re-reading a dispatch for a ticket it is already
working is not folded as a conflict. `queue` instead takes `asRunner`, which
answers off that named runner's own row - its repositories, `mine` and
`ancestorKey` - rather than the query string, and may not be combined with
any of them; where that key holds live loop sessions, the answer is judged
off its beat row and those sessions together, never the beat row alone. The four write routes (`merged`, `pushback`, `closed`) take
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
carries it back as `TtlSeconds` alongside `StallLapseSeconds` - the claim's own
quiet window where its playbook set one, the board's otherwise, `0` when lapsing
is off for it - so a caller never has to know the setting's name to honor it.
The take also answers `MaxRunSeconds`, the run cap its playbook set in seconds
(`0` for none), and `RunnerGoneAfterSeconds`, how long the board waits before a
runner's row reads gone (`0` from an older server). The holder beats its claim
every `min(lease / 5, gone limit / 3)`, so one missed beat does not take a
working runner past the limit.
The claim object on every read (the board card, the issue, the search) carries
the same `StallLapseSeconds` beside `TtlSeconds`. A heartbeat renews the
lease and may replace the playbook it is running under. A take's or heartbeat's
`playbookId` may be a repo-sourced file row's (negative) id, the same one
`GET work/{key}` hands back on `Playbook.Id`; the claim's quiet window is
copied from the playbook named, as is its run cap, and a heartbeat naming none
leaves both alone. It
writes no event on
an ordinary beat, only on a preemption, which is the board's own decision to
make room for an emergency ticket and is the one case a heartbeat answers a
`ClaimPreemptedDto` instead of `204`.

A live claim refuses a second `POST`, including the same runner's own -
re-claiming is not a thing a runner does, it heartbeats - and a claim on an
ancestor or descendant issue refuses it too: one runner per line of the tree.
A take from a session the board has paused (kind `board-paused`, still ahead of
now) is refused `409` too, at the checkout the request's `runner` names: the
sentence says the board paused this runner, until when, what it saw, and that a
person can lift the pause on the Runners page. It takes no claim and writes no
event. A claim the session already holds keeps its heartbeats and its release,
another session under the same key at another checkout claims as before, and a
pause the runner set itself (`not-starting`, `signed-out`) refuses nothing. A heartbeat or release refused for its token answers `409` with `{ error,
reason }` - `error` the sentence a person reads, `reason` one of `quiet`
(the holder's own token, gone quiet), `run-time` (its own token, past the run
cap its playbook set - `this claim ran past its 60-minute limit`), `expired` (its own token, simply
lapsed), `cleared` (no token on the claim) or `taken` (somebody else's
token); a take's own `409`s are unchanged, bare sentences. The `DELETE`
carries an optional `token` (the one the claim was taken with), an optional
`outcome` - one of `dropped`, `worked`, `preempted`, `recused`, `unservable`, `abandoned`, `unstarted` - an optional
`reason` - `quiet`, `run-time` or `error` - and an optional `sessionId`, all three read only alongside a token, and `reason`
written into the event only beside an `outcome`. Where `sessionId` names a work-log row of this issue
and an `outcome` is given, that row's `outcome` and `reason` are stamped in the same save as the
event, and the event's own payload carries the session id too; a `sessionId` naming no row, or
given with no `outcome`, stamps nothing and is never refused for it. A `dropped` release whose
claim spent nothing - at least one work-log row reached the board since the
claim was taken, and every such row has zero tokens and `isError` true - is
written with the outcome the runner sent plus `spentNothing: true`, and
`letGo` neither counts it nor stops at it. `abandoned` is what a
starting loop sends for the claim its dead predecessor left (a crash, a closed
window): it is skipped over by `letGo`, neither counted nor resetting the
count. `unstarted` is what a runner sends when the session's agent never started
(no stream event before the process ended): skipped over the same way. Releasing a claim that
holds nothing, or releasing twice, is a `204` that writes nothing either
way.

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
| PUT | `/issues/{key}/merge-check` | Replaces the stored verdict for the issue's branch against one repository's trunk, and whether its diff changes `.hatch/playbooks.yaml` or `.hatch/dependencies.json` - either holds the merge train for a person, see [the merge train](review-pull-requests-and-the-merge-train#the-merge-train) |
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
A `PUT` to `/trunk-builds` naming a repository only a project outside the
caller's scope binds answers as one nobody binds does, and stores nothing.
`/trunk-builds/{id}/bug` is the one write in this group a person can also
take, from the attention panel's own button. The body is optional, and a
person may choose three fields in it: `preApproval` (`none`, `express` or
`preapproved`), `priority`, and `statusId`, the column the bug is born in.
`express` is the older spelling of `preApproval`, still accepted: it means
`express` when true and `none` when false, and only where `preApproval` is
absent - where both are sent, `preApproval` wins. `preapproved` takes an owner
or approver of the project that binds the trunk, and a person who is neither
gets the same 403 `POST /issues` gives, with nothing filed; a level that is
not one of the three is a 400. A key filing one gets none of the fields - they
come off the project's own auto-heal settings, and only where a project has
turned auto-filing on.

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
| POST | `/playbooks` | **person, at the browser** | Creates one |
| PATCH | `/playbooks/{id}` | **person, at the browser** | Edits one |
| DELETE | `/playbooks/{id}` | **person, at the browser** | Deletes one |
| GET | `/playbook-files` | key | Every stored verdict of what a repository's trunk actually carries at `.hatch/playbooks.yaml` |
| PUT | `/playbook-files` | key | Replaces the stored row for one repository's trunk, from a runner's poll of the file |

**A key may read a playbook and may not write one, and the refusal is
deliberate.** An agent that could widen its own prompt, or raise its own
model or effort, has no fixed point to settle at, and the failure that
follows is unbounded spend rather than a wrong answer - the same shape of
loop the claim's tokenless `DELETE` above and a runner's own `--max-spend`
are cut to prevent. Reading stays open because an agent has to be told what
it was dispatched with.

Person here means a person at the browser specifically: with the wall off
these three, and `PATCH /issues/{key}/playbook`, also refuse a call with no
key and no `Sec-Fetch-Mode` header, so a bare `curl` or `hatch api` call is
refused the same as a key.

`/playbook-files` is not that same edge, despite sitting beside it: it is
not the matrix above, it is a cache of what a runner's poll actually found
on disk at a repository-sourced project's trunk - a fact a key reports about
a file, not an edit to Hatch's own instructions - so both its `GET` and its
`PUT` stay key-open. Check each verb against its own attribute rather than
assume the group is uniform.

Each row in a `PlaybookFileDto` carries the same read-only `UnreachableReason`
a `PlaybookDto` does, derived from the live board on every `GET` and never
stored, so a column reorder changes it with no new `PUT`. A file carrying such a
row is still stored `parsed`, and the `PUT` response carries the sentence on
that row.

`PlaybookDto` carries the transition (`FromStatusId`/`FromStatusName`,
`ToStatusId`/`ToStatusName`), `Types`, `Shape`, the effective `Prompt`
beside the row's own `DefaultPrompt`, `Model`, `LocalModel` (the Ollama id a move runs on when it runs locally; null means the row never runs locally; `POST`/`PATCH` take it as a string, null leaving it alone on patch and blank clearing it, and an alias or another agent's id is a `400` - carried in a runner's work answer beside `Model`, and read by dispatch for a ticket that requires its local model), `Effort`, an optional `Budget`
(millions of tokens), `BriefLimit` (characters), `QuietLimitSeconds` (seconds a
claim may go without a word; null inherits the server's quiet lapse) and
`MaxRunSeconds` (seconds a session may run before it is cut off; null means no
limit - stored, not yet read), `DeniedTools`, `IsCheck`, `AnswersQuestions` (a row a session runs on to answer one question; the match never returns it, and a row is never also a check or a row that works out changes - a `POST` or `PATCH` that would make it more than one is a `400`),
`WorksOutChanges` (a row a session runs on to work out what a change to an ambition does to its epics in flight; the match never returns it, and a row is never also a check or a row that answers questions - a `POST` or `PATCH` that would make it more than one is a `400`),
`Source` - `"board"` or `"project"`, by whether `ProjectId` names one - and
`UnreachableReason`.
`UnreachableReason` is read-only and null wherever the dispatcher could pick
the row; otherwise it is one sentence saying that a dispatch from the row's
from-column goes to another column, or nowhere. It is derived from the live
board on every read and never stored, so a column reorder changes it with no
write to the row, and a proposal's `after` carries it too, judged against the
row as the proposal would leave it. Saving such a row is not refused.
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
A `PUT` naming a repository only a project outside the caller's scope
binds answers as one nobody binds does, and stores nothing.

### Local models

| Method | Path | Who | What it does |
|---|---|---|---|
| GET | `/local-models` | key | Every row, in creation order |
| POST | `/local-models` | **person, at the browser** | Adds one |
| DELETE | `/local-models/{id}` | **person, at the browser** | Removes one |

The board-wide table of Ollama models: an id, its context length and its
memory footprint. Guarded the same way playbooks are, and for the same
reason - reading is open to a key because a runner's own heartbeat has to
know the table to be told which ids fit its own machine; writing is a
person's alone, at the browser, with the same `Sec-Fetch-Mode` check the
three playbook writes make.

`LocalModelDto` carries `Id`, `OllamaId`, `ContextLengthTokens`,
`MemoryFootprintBytes`, `CreatedBy` and `CreatedAt`. `POST` takes
`LocalModelCreateRequest` (`OllamaId`, `ContextLengthTokens`,
`MemoryFootprintBytes`); a context length or a memory footprint that is not
a positive number is `400`, an id that is not an Ollama id - an alias or
another agent's pinned id - is `400`, and a duplicate id is `409`. `DELETE`
is `404` for an id nothing holds.

`GET /runners` and a heartbeat's own `RunnerInstructionDto` each carry
`hold`: the `OllamaId` of every row that fits that runner's own machine, in
the table's own order. Empty unless the runner's agent is `ollama` and it
has reported accelerator memory; a row holds when its own footprint is no
larger than the memory reported and the runner does not refuse it. See
"Runners and settings" below for where it sits on `RunnerDto`.

### Dependency files

| Method | Path | Who | What it does |
|---|---|---|---|
| GET | `/dependency-files` | key | Every stored verdict of what a repository's trunk actually carries at `.hatch/dependencies.json` |
| PUT | `/dependency-files` | key | Replaces the stored row for one repository's trunk, from a runner's poll of the file |

`/dependency-files` is not that same edge the playbook matrix above draws:
it is not the matrix, it is a cache of what a runner's poll actually found
on disk at a repository-sourced project's trunk - a fact a key reports about
a file, not an edit to Hatch's own instructions - so both its `GET` and its
`PUT` stay key-open, the same reasoning `/playbook-files` takes. What the file
says is in [the runner](the-runner#what-a-repository-needs).
A `PUT` naming a repository only a project outside the caller's scope
binds answers as one nobody binds does, and stores nothing.

`DependencyFileDto` carries `Remote`, `Canonical`, `Trunk`, `Sha`, `State`
(`parsed`, `invalid` or `absent`), `Hash` (the stored content's SHA-256),
`Entries` (each a resolved `Id`, current `Label` and optional `Version`,
from `DependencyDeclaration.Parse`), `Problem`, `LastGoodSha`/`LastGoodHash`,
`Url`, `CheckedAt`, `Runner` and `CheckedBy`.

**Refusals worth naming.** `PUT` 400s an empty remote, trunk, sha or runner,
non-base64 or non-UTF-8 content, or content over the stored cap, and writes
no event - there is no issue to write one on, and no question, since a bad
dependencies file is this row's own state rather than a stall. An unknown
dependency id is not one of these refusals: `DependencyDeclaration.Parse`
folds it into the same `Problem` any other shape failure produces, so it
reaches the stored row as `state: invalid`, not a 400.

### Changelog files

| Method | Path | Who | What it does |
|---|---|---|---|
| GET | `/changelog-files` | key | Every stored verdict of what a repository's trunk carries at `.hatch/changelog.yml`, the path itself, and the canonical remotes some project currently elects for a changelog |
| PUT | `/changelog-files` | key | Judges the bytes a runner read off one repository's trunk against the board's own file, stores the verdict, and files a task where the file is behind or absent |

`PUT` takes `{ remote, trunk, sha, content?, url?, runner }`, `content` the
blob's exact bytes, base64-encoded and absent for a trunk with nothing at the
path. The board compares those bytes with the file it would write itself
([the changelog file](the-runner#the-changelog-file)) and trusts no verdict from
the caller; like `/dependency-files`, it is a fact a key reports about a file,
so both routes stay key-open.

`GET` answers `Path`, `Elected` (the canonicals some project elects),
`Files` (ordered by canonical then trunk) and `ElectedRemotes`: of the
`remote` query values the caller sent (repeatable), the ones whose repository
is elected, spelled as sent. A runner holds its origin as its checkout spells
it and canonicalises nothing, so this is how it learns which checkouts to
read.

`ChangelogFileDto` carries `Remote`, `Canonical`, `Trunk`, `Sha`, `State`
(`current`, `behind`, `absent` or `foreign`), `Hash` (the bytes' SHA-256, null
for `absent`), `Behind` (for `behind`, the count of entries the file lacks,
holds differently, or holds that the board no longer does), `Problem` (for
`foreign`, the sentence naming the sha and saying to move or remove the file),
`Url`, `CheckedAt`, `Runner` and `CheckedBy`.

**Refusals worth naming.** `PUT` 400s an empty remote, trunk, sha or runner,
non-base64 content, or content over 2 MB, and `404`s a remote no project
elects for a changelog, naming the Projects page. It writes no event and asks
no question. Where the stored state is `behind` or `absent` and no recorded
changelog task for that canonical is open in any column, a deferred one
included, it files one task, from the newest entry the file lacks; a `foreign`
file files none.

### Playbook proposals

| Method | Path | Who | What it does |
|---|---|---|---|
| GET | `/playbook-proposals` | key | The queue for one state, oldest first - pending by default, or `?state=approved`, `rejected` or `all` |
| GET | `/playbook-proposals/{id}` | key | One proposal, with every change it carries |
| POST | `/playbook-proposals` | key | Files a proposal - a reason, an optional issue, and one or more changes - and changes nothing live |
| POST | `/playbook-proposals/reject-all` | **person, at the browser** | Marks every row named in `{ proposalIds }` rejected, naming an issue's events; `409` naming the reload where that set is not exactly what is pending |
| POST | `/playbook-proposals/approve-all` | **person, at the browser** | Applies every row named in `{ proposalIds }` in one transaction; `409` naming the reload, the out-of-date change, or the live-board refusal, applying nothing |

A session that found a playbook's prompt, model, effort or shape wrong can propose a fix without being handed the write a key is refused on `/playbooks` - the body is `{ reason, issueKey?, changes: [{ playbookId, ...PATCH's fields }] }`, each change naming a playbook and at least one field `PATCH /playbooks/{id}` takes - and the change sits for a person to decide on the Playbooks page, not anything live. Each `PlaybookProposalChangeDto` carries `before` and `after` as full `PlaybookDto`s, `changedFields` (the fixed vocabulary `fromStatusId, toStatusId, types, shape, prompt, model, localModel, effort, budget, briefLimit, deniedTools, isCheck, answersQuestions, worksOutChanges, projectId` - `quietLimitSeconds` and `maxRunSeconds` are not in it), and `outOfDate`/`outOfDateReason` for whether the playbook has since been edited or deleted. `POST` 400s a blank reason, an empty `changes` list, a change naming no field, the same sentences `PATCH /playbooks/{id}` gives against the live board, and a change naming a project that reads its playbooks from its own repository rather than from rows here at all; a playbook already named by a pending proposal refuses a second, naming it - one pending change per row at a time. An `issueKey` naming no issue is `404`. A named issue gets a `playbook_proposed` event.

### Runners and settings

| Method | Path | Who | What it does |
|---|---|---|---|
| GET | `/runners` | key | Every runner the caller may see - one entry per visible key, revoked included, plus one per visible heartbeat no key claims - most recently heard from first; `?live=true` keeps only the entries actually answering lately; carries `agentDownUntil`, `agentDownKind` and `agentDownLine` beside `exhaustedUntil`; carries `launcherVersion`, the version of the launcher that started the loop (absent for a loop with no launcher), and `launcherBehind`, true when that version is lower than the launcher version of the board's own build - the board makes the comparison, and a session carries the raw `launcherVersion` only |
| GET | `/runners/{name}` | key | One visible runner by its name, or `404` for a name nothing has ever used or this caller may not see |
| POST | `/runners/{name}` | key | A runner's own heartbeat: what it is (including the `models` it can run and the dependencies it `has`), and what the board would like it to do next; writes a heartbeat row regardless of whether the call carries a key; `agentDown`, `agentDownUntil`, `agentDownKind` and `agentDownLine` carry the agent's own pause the same tri-state way as `exhausted`; a pause the board wrote (`agentDownKind` `board-paused`) is left whole while it lasts; `usageSource` and `usageProbeOff` say where the usage reading came from and why its probe is off, and travel together: a beat that names a `usageSource` sets both (a null `usageProbeOff` clears a stored reason), and a beat without one leaves both as they were, with or without a reading; `rid` names the platform the build was published for (`win-x64`, `osx-arm64`, `osx-x64` or `linux-x64`) and is never stored; the answer's `boardBuild` names the board's own build when it differs from the beat's `revision`, and is absent otherwise |
| POST | `/runners/sessions` | key | The session-level heartbeat: the bearer key alone, never a name; the CLI's heartbeat goes here now, and `POST /runners/{name}` above is the legacy route for a genuinely keyless beat (wall off, no key at all); takes the same `agentDown` fields, and leaves a `board-paused` pause alone the same way, and takes `usageSource`/`usageProbeOff` on the same pairing rule; takes `rid` and answers `boardBuild` exactly as `POST /runners/{name}` does; while that pause is ahead of now the answer's `state` is `paused` (a stored `stopping` stays `stopping`), and the stored state is left as it was, so the page does not read it as a person's pause; takes `launcherVersion`, the version of the launcher the loop was started by, on every beat of a loop under one and none otherwise - a beat that omits it leaves the stored value alone, a fresh `loop` beat that omits it clears it, and a board built before the field ignores it |
| POST | `/runners/sessions/{id}/end` | key | Ends one of the calling key's own sessions - a loop leaving, so a fresh loop in the same checkout is not refused for it; `204` whether or not it was already ended, `404` for an id that is not this key's |
| POST | `/runners` | **admin** | Mints a runner - a key and its config - and answers it with its `secret`, shown once and sent `no-store`; `409` for a name already held, `400` for `names` or `suggest-name`; the owner defaults to the caller, and a key is refused |
| GET | `/runners/suggest-name` | **admin** | A cast-list name no key (revoked included) and no heartbeat holds, as a bare string |
| PATCH | `/runners/{name}` | **person** | Sets its state, under, max-runs, max-spend, max-tokens, max-tokens-per-hour, until, tags, and `statusMode`/`statusIds` (the column rule) - `statusMode` is `all`, `except` or `only`; the two take each other or neither, `400` otherwise; `400` for a mode none of the three names, or an id no column has; `all` keeps the ids `[]` regardless of what was sent; a name with no key is `400`, since the rule has nowhere to live |
| PUT | `/runners/{name}/refused-models` | key, own row, or person | Replaces the whole list of models this key refuses to run; `400` for an absent `models`, a blank entry, or one of a different agent than it reports; `404` for a name no key answers to, `409` for a revoked key |
| POST | `/runners/{name}/revoke` | **admin** | Stops the runner's key working, by the runner's name; the runner stays listed, `404` for a name no key answers to |
| POST | `/runners/{name}/rotate` | **admin** | Replaces the runner's secret: the old one is refused from the next call; answers the runner and the new `secret` once, sent `no-store`, with `prefix` and `rotatedAt` updated; name, owner, config, sessions and attribution carry over; `404` for a name no key answers to, `409` for a revoked key |
| PUT | `/runners/{name}/owner` | **admin** | Sets or clears (`personId: null`) whose tickets the runner's `--mine` reaches; `400` for an unknown person, `404` for a name no key answers to |
| POST | `/runners/{name}/order` | **person** | Orders this runner to work a named ticket, or to pass on its next pick |
| DELETE | `/runners/{name}/order` | **person** | Withdraws whatever standing order it holds |
| PATCH | `/runners/{name}/sessions/{id}` | **person** | Sets one session's state, by its id, without touching any sibling session under the same key |
| POST | `/runners/{name}/sessions/{id}/order` | **person** | Orders one session to work a named ticket, or to pass on its next pick |
| DELETE | `/runners/{name}/sessions/{id}/order` | **person** | Withdraws whatever order that session holds |
| DELETE | `/runners/{name}/sessions/{id}/pause` | **person** | Lifts a `board-paused` pause on one session - clears `agentDownUntil`, `agentDownKind` and `agentDownLine`, and resets the failure streak that led to it; `409` for a pause the session set on itself (`not-starting` or `signed-out`), which is the runner's own to clear; `204` and no write for a session that is not paused, or whose pause has passed |
| POST | `/runners/{name}/pause-all` | **person** | Pauses every live `loop` session under this key at once |
| POST | `/runners/{name}/stop-all` | **person** | Stops every live `loop` session under this key at once |
| GET | `/runner` | key | What platforms this image can hand out, the commit they and it were built from, and each build's `sha256` - lowercase hex of the exact bytes its download serves |
| GET | `/runner/download/{rid}` | key | One platform's `hatch` binary |
| GET | `/settings` | **person** | Hatch's own settings: the Claude subscription token, the agents' API keys, the local person's name, and the site's default token caps (`defaultMaxTokens`, `defaultMaxTokensPerHour`) |
| PUT | `/settings` | **person** | Edits them - the two default token caps only for an admin, or for anyone at the keyboard with the wall off |
| GET | `/settings/credentials/{agent}` | key | One agent's credential, unprotected, for the container runner to start it with |
| GET | `/settings/claude-token` | key | The same, kept for an older container; `credentials/claude` is its successor |
| GET | `/backup` | key | The backup folder's truth and the last attempt's: configured, path, keep-days, copies, and the last outcome |
| POST | `/backup/run` | **person** | Runs one backup attempt, capped at two minutes, and answers the same shape `GET /backup` does |
| GET | `/me` | key | Who, or what, is calling - a browser or a key or a runner - for the nav strip |
| PUT | `/me/home` | **person** | Sets the caller's own home screen, `board` or `ambitions`, and answers the fresh `/me` |

A claim heartbeat under a key touches the key's live session at the claim's
checkout. If the sweep has already ended that session while the claim was still
being renewed, the heartbeat revives it: `EndedAt` cleared, `LastSeenAt` now. A
session the runner ended itself through `POST /runners/sessions/{id}/end` is
never revived, nor is one while a newer live session stands at the same
checkout.

A runner is a key. `GET /runners` joins each key to its config - the
`RunnerConfigs` row, by the key's id - and to its heartbeat row, by name
(`Runner.JoinAsync`); a key that has never run is listed, not live. A heartbeat
no key claims is still listed, with its key fields null. HA-1937 adds the
session routes below beside the heartbeat table, not instead of it
(`Runner.IsLive`, `RunnerSessions.MarkAllGoneAsync`): a key with no sessions
behaves exactly as before, and a key with some is judged by the beat and its
sessions together, never one instead of the other. `?live=true` keeps the
entries whose heartbeat is not past `Runners.DropBefore`.

`/runners/{name}` is the one call a running loop makes about itself, and the
whole round trip: it reports what it is - its repositories, whether it
clones, `mine`, `standing`, where it runs, which agent spawned it, and
`host`, `runs`, `spent`, `tokensUsed` and `tokensLastHour`, the machine's short
name, the increments run, the dollars spent and the tokens used so far, and the
tokens used in the last sixty minutes - running totals the loop keeps (written
every beat, with none of the gating the bounds have; an absent one leaves the
row alone, and a `once` beat sends only `host`) - and
reads back a `RunnerInstructionDto` saying whether to keep going, pause or
stop, and the six bounds (`Under`, `MaxRuns`, `MaxSpend`, `MaxTokens`,
`MaxTokensPerHour`, `UntilAt`) it is
running within. Those six
bounds are written by a fresh loop's first beat
(`Fresh: true`, a `loop` beat only), which also clears any the row held for a
flag the loop was not given, and otherwise only by a person's `PATCH` -
a supervisor restart sending its original `--max-runs` again never undoes an
operator's midnight edit, a `once` beat never writes them, and a running agent
cannot raise its own budget: only a new process started with flags can, which
is the failure the bounds exist to prevent. The two token bounds are longs on
the beat (`MaxTokens`, `MaxTokensPerHour`); a value of zero or less, or an
absent one, writes none. Their `PATCH` fields are strings in the same
notation the flags take - a whole number, or `500k`, `2M`, `1.5M` (one decimal
digit, on `M` only), at least one token: blank clears the bound, absent leaves
it, and zero or anything unreadable is a 400 reading `--max-tokens takes a
token count, like 500k or 2M - not "x"` (`--max-tokens-per-hour` for the other).
`Projects` is a fact about the process, written on every beat of any kind
like `models` and `has`: absent leaves the row's projects as they are, `[]`
clears them, and a list sets them - never gated by `Fresh` or `Kind`, and
never a `PATCH` field. The instruction does not answer it back; a loop reads
its own bound `--project` flags straight off its own process, not off the row.
The `state` is a person's too, with one exception: a fresh `loop` beat that also sends `standby: true` writes the
row `paused` - on a new row, or over `running` or `stopping` - and is answered
`paused` on that same beat, since a start that asked to stand by should not run
a single increment first. `standby` is read on that beat alone: without
`fresh` (a supervisor restart) or on a `once` beat it writes nothing, and a
fresh beat without it leaves the state as it was. There is no
`DELETE` on the row itself: it ages out of `GET /runners` on its own once
nothing has heard from it in a while.

`boardBuild` is the board telling a runner that its build is not the board's,
and where to get the board's. It is on the answer exactly when the beat sent a
stamped `revision` and `sequence`, the board is stamped, and the two revisions
differ - on `POST /runners/{name}` and `POST /runners/sessions` alike, for a
`loop` beat and a `once` beat. It holds the board's `revision` and `sequence`,
`compare`, `download` and `sha256`. `compare` is `behind`, `ahead` or
`diverged` (two commits at the same count), and the board decides it; the
runner compares nothing. It is a string, so a word an older runner has never
seen does not fail its parse. `download` and `sha256` are what `GET /runner`
lists for the platform the beat names in `rid`, from the same code; both are
`null` for a beat that names no platform, one the board does not publish, or
an image that published nothing, and the part is still sent. A matching
revision, an unstamped side (`dev`, or no count) and a beat with no revision
all get no part, and so does an older board. A downloaded loop acts on it: on
`behind` it asks its launcher for the build at the top of its next pass, and on
any other word only once every beat for ten minutes has named the same revision.
A loop with no launcher only reads it. `rid` is never stored.

`throttledUntil` is a fact the runner reports about its own
`--max-tokens-per-hour`, never a person's to set: it is the instant the loop
expects to be back under that cap while it waits, written on every `loop` beat
(so a beat that carries null clears it) and left alone by a `once` beat. It has
no `PATCH` field.

`pulling` is a fact the loop reports about itself, on the same terms as
`throttledUntil`: true while it is pulling a model, written on every `loop`
beat, and cleared by a `loop` beat that does not say so; a `once` beat leaves
it alone. A keyed runner with no beat row of its own folds it from its live
sessions the way `exhaustedUntil` does, true the moment any live session says
so. Dispatch reads it off the row: a runner that is pulling is live, but is
never offered as the free local runner a preferred local model folds a cloud
asker onto. It has no `PATCH` field.

`agentDownUntil`, `agentDownKind` and `agentDownLine` are the same tri-state
`exhausted`/`exhaustedUntil` already has, for the agent rather than the
account: `agentDown: true` writes `agentDownUntil` and normalises
`agentDownKind` to one of `not-starting` or `signed-out` (anything else sent,
`board-paused` included, is stored as `not-starting`), `agentDown: false` clears all three at once, and
absent - an older CLI, or `hatch work`'s single beat - leaves them alone. A
keyed runner composes them off its live sessions the way `exhaustedUntil` does:
whichever session's own `agentDownUntil` is latest and still ahead of now,
copied whole rather than mixing a kind from one session with a line from
another. None of the three has a `PATCH` field. The board writes the three
itself, with kind `board-paused`, on the session whose releases spent nothing
(see [the runner](the-runner#when-the-board-pauses-a-session)). While that
pause lasts a heartbeat from the session cannot clear, shorten or reword it:
`agentDown: false`, `agentDown: true` with the runner's own kind, and an absent
field all leave it whole. Once `agentDownUntil` has passed the heartbeat writes
the three as it always did. A new session row for the same key and checkout,
started while the pause is still in force, begins with the same three. Unlike exhaustion, a runner
whose agent is down is left out of `Dispatch`'s peer set and is never elected
driver until the check passes. `DELETE /runners/{name}/sessions/{id}/pause` is
the operator's way to end a board pause early: it clears every row at that
key and checkout with a live `board-paused` pause, ended ones included, so a
restarted loop does not inherit the pause straight back from the row it
replaced. The next heartbeat is then answered with the session's own stored
state, and three more releases that spent nothing pause it again.

`models` is a fact the runner reports, written on every beat like `remotes`:
an absent one leaves the row alone, `[]` clears it, and any other list replaces
it, kept in the order given with repeats dropped. Any entry that breaks the
`RunnerModels` rule - more than 100 entries, a blank entry, an entry that is
neither an alias nor a pinned id of a known model family, or a pinned id of a
different agent than the beat's `agent` - refuses the whole beat with a `400`
carrying the rule's sentence, and the row is left exactly as it was (a refused
first beat creates no row). It has no `PATCH` field.

`has` is a fact the runner reports about the process, written on every beat
like `remotes`: an absent one leaves the row alone, `[]` clears it, and any
other list replaces it, kept in the order given with repeats dropped. Each entry
is a dependency `id` or `id@version`, lowercased before it is stored; an entry
that breaks the `DependencySet` rule - a blank entry, an id not on the master
list, or a malformed version - refuses the whole beat with a `400` carrying the
rule's sentence, and the row is left exactly as it was (a refused first beat
creates no row). It has no `PATCH` field.

`refusesDangerous` is a fact about the process, written on every beat of any
kind like `Projects`: absent leaves the row alone, true or false sets it. A
row never told reads as false, and `GET /work/queue?asRunner=` reads it from
the row, so it folds flagged work for a runner whose row says true whatever
`?dangerous=` said. It has no `PATCH` field, and the instruction does not
answer it back.

A heartbeat's own `tags` is ignored outright, on this route and on the session
one alike: a runner's tags are never taken off a beat, in any form - only
`PATCH`'s `tags` field moves them (see above), and a beat naming one is never
refused for it. The instruction answers the row's tags as they stand.

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

`POST /runners/sessions` is the session heartbeat `RunnerSessions` answers to
- the bearer key alone, never a name or a header, so `403`s with none ("a
session heartbeat is the bearer key's, not a name or a header"). The body is
the same `RunnerHeartbeatRequest` `/runners/{name}` takes, minus `tags`: a
session has no column for it, and it is silently dropped. `models` and `has`
are validated exactly as on `/runners/{name}` - `400` on a refusal - and
stored on the session. The row matched is this key's own session at the body's
`where` - `(ApiKeyId, Where)` is the pair a session is held by - touched if it
is still live, seeded fresh if not; `409`s ("`<key> is already the runner on
<where>`") only for `fresh: true`, `kind: loop`, and a live session already at
that `where`. The `409` body is `{ error, lastSeenAt, goneInSeconds }`: the
sentence, when the held session last beat, and at most how many seconds (at
least 1) until it is marked gone. A fresh loop that meets it waits that long,
asking again every few seconds: a `lastSeenAt` that has moved is a live second
loop and is refused for good; a session that has gone quiet is marked gone and
the next beat is answered. It answers a `RunnerInstructionDto`, now carrying
`sessionId`, and `drives`: the project keys this key is elected to drive, from the
same election `POST /runners/{name}` answers. `go-to-work` gates its merge train
on it, so a session beat that answered none would leave no loop driving.

`POST /runners/sessions/{id}/end` is how a loop says it is leaving. It is the
bearer key alone, like the heartbeat (`403` with none), and `404`s a session
that is not this key's or does not exist. It stamps `endedAt` if the session is
still open and answers `204` either way, so it can be sent twice. A fresh beat
afterwards finds no live session at that `where` and seeds a new one. The CLI
sends it on every exit of `go-to-work` except the hand-over to a restarted
incarnation, which joins the same session.

The body also takes six machine fields - `os`, `arch`, `agentVersion`,
`container`, `username`, `processStartedAt` - stored on the session; an
absent field leaves the stored value alone, and a string over its column's
length is capped, not refused. `ip` is never read from the body - a session's
`ip` is the address the server read off the connection (after
`UseForwardedHeaders` rewrites it from `X-Forwarded-For`), refreshed on every
beat regardless of what else the body says. `RunnerSessionDto` carries all
seven - `os`, `arch`, `agentVersion`, `container`, `username`,
`processStartedAt`, `ip` - beside the rest of the session. That same
`RunnerSessionDto` is what `GET /runners` answers under each runner's
`sessions`, and what `hatch runners --json` prints unchanged.

A heartbeat's `usageSource` and `usageProbeOff` say where its usage reading came
from and why its probe is off. They travel together, the way `agentDown` and its
fields do: a beat that names a `usageSource` is authoritative for both on that
beat (`usageProbeOff` null means the probe is not off, and clears a reason
stored earlier), and a beat without one leaves both exactly as they were,
whether or not it carries a reading. A source with no reading is not stored.
`POST /issues/{key}/claim/heartbeat` takes the same two fields beside its usage
reading, under the same rule. `RunnerSessionDto` answers both back, and so does
each `GET /utilization` runner entry, from its freshest reading.

The body takes three accelerator facts beside them - `acceleratorMemoryBytes`
(the total across devices, in bytes), `acceleratorDeviceCount` and
`acceleratorBackend` (`cuda`, `rocm`, `metal`, `vulkan` or `none`, capped at 16
characters) - which a runner reports only where its Ollama is on its own
machine. They are kept on the session and on the runner's row alike: an absent
field leaves the stored value alone, except that a fresh `loop` beat writes the
row's exactly as sent, so a restart that reports none clears the last process's.
Both `RunnerSessionDto` and the `GET /runners` row carry all three, and a
config ceiling on the row does not hide them.

Both the `GET /runners` row and a heartbeat's own `RunnerInstructionDto`
also carry `hold`: the "Local models" section's own table, filtered to the
`OllamaId` of every row that fits this runner's own machine, in the table's
order. Empty unless the runner's `agent` is `ollama` and it has reported
`acceleratorMemoryBytes` - a beat-less key with no legacy row falls back to
its newest live loop session's own agent and accelerator memory, the same
fallback `PUT /refused-models` makes. A row holds when its own memory
footprint is no larger than the memory reported and this runner's held
`refusedModels` does not refuse it.

`PATCH /runners/{name}/sessions/{id}` sets one session's state
(`RunnerSessionPatchRequest { state }`) without touching any sibling session
under the same key; `404`s an id that is not this key's own.

`POST`/`DELETE /runners/{name}/sessions/{id}/order` are `/runners/{name}/order`'s
own request and refusals, narrowed to one session: `400`s a kind that is not
`work` or `pass`; `404`s a `work` key naming an issue that does not exist;
`409`s a `once` session, a `stopping` session, and a session that already
holds an order, naming the kind and key held and saying to withdraw it first.
`DELETE` withdraws it unread, `204` either way.

`POST /runners/{name}/pause-all` and `POST /runners/{name}/stop-all` set
every *live* `loop` session under this key to `paused` or `stopping` at once
- a `once` session, an already-ended one, and one past the gone horizon are
left alone. Both answer `204` with no body.

All five session routes share one gate before they touch a session: not a
person, a usable name, a key by that name, then `PatchRunner`'s own `403`
("only `<owner>`, or an admin, may control `<runner>`").

`GET /runners` answers with the rows the caller may see:

| Caller | Sees |
|---|---|
| A person at a browser, or any caller on a Hatch with its wall off | Every runner |
| A key whose owner is an admin | Every runner |
| A key whose owner is anyone else | Only that owner's runners |
| A key with no owner | Only the runners that belong to nobody |

Each row carries `CanControl`, whether the caller may `PATCH` it, and `Yours`,
whether it is the caller's own: `Yours` is true for the caller's runners and for
every row with the wall off, while `CanControl` is also true on others' rows for
an admin and on rows that belong to nobody. `ClaimKey` is the ticket the runner
holds and `ClaimStatus` the name of that ticket's column now; both are null
between increments.

Each row also carries the token side of the bounds: `maxTokens` and
`maxTokensPerHour`, each with a `...Source` word beside it (`maxTokensSource`,
`maxTokensPerHourSource`) saying where the number came from - `runner` when the
row holds it, `default` when it comes from the site default on `/settings`,
`none` when there is no such bound anywhere - then `tokensUsed` and
`tokensLastHour`, the loop's own running counts, and `throttledUntil`, the
instant a waiting loop expects to be under its hourly cap, or null, and
`pulling`, whether the loop is pulling a model. `models`
is the runner's advertised models in the order given, `[]` for a row that never
reported; `has` is the dependencies it advertised, as `id` or `id@version`
strings in the order given, `[]` for a row that never said.
`maxTokens` and `maxTokensPerHour` are the effective cap in either of the first
two cases, so a row never makes the reader add the two together.

A cap comes from the row, else from the site default, else from nowhere. The
heartbeat's `RunnerInstructionDto` carries the effective cap, so a runner with
none of its own runs under the default, and a fresh start that gave no flag
clears the row's own cap and so falls back to the default rather than running
uncapped. The same holds for a person's `PATCH`: clearing a runner's own cap
with `""` returns it to the default, it does not uncap it. Clearing the default
on `/settings` writes no row - it takes the cap off every runner that was
inheriting it, read at each runner's next beat. `/settings` is cached for up to
30 seconds, a backstop for another replica, so a changed default can take that
long to reach a beat answered there.

`/runner` and `/runner/download/{rid}` take a key as well as a person: a
person at a browser setting up a machine may fetch both, and so may a
runner's own key, which checks its build against the `sha256` the listing
carries. Each build's hash is worked out once, from the bytes the image
actually serves, and kept rather than recomputed per request.

`/settings` is Hatch's own configuration, carrying nothing an agent reads:
it is person-only on both verbs by design, beside `/runners/{name}` and
`/playbooks`' person-at-the-browser writes above - an API key that could write here could set the
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

The two defaults, `defaultMaxTokens` and `defaultMaxTokensPerHour`, are the one
write on `/settings` that is **admin-only** inside a person-only route: with the
wall up a person who is not an admin is a 403, with the wall off anyone at the
keyboard may set them, and a key never may. A `PUT` that
carries only the name or the credentials is unaffected, so a person who is not
an admin still writes those. On the wire they are strings in the runner
`PATCH`'s notation - a whole number, or `500k`, `2M` - where absent leaves the
default alone, `""` clears it, and zero or anything unreadable is a 400. `GET`
answers each as a number, or null while unset. The page does not draw them yet.

`/backup` answers a `BackupStatusDto`: `configured` and `path` (the
operator's own setting, or `false`/`null` when there is none), `keepDays`,
`copies` (newest first, each with its name, size and the instant it was
taken) and `lastSuccessfulAt`, all read from the backup folder itself rather
than from the last attempt - deleting a file by hand changes the next read.
`lastAttemptAt`, `lastOutcome` (`ok`, `failed`, `not-configured`, or `never`)
and `lastError` (set only when the outcome is `failed`) come from the last
attempt, whoever made it. `POST /backup/run`, person-only, runs one backup
synchronously, capped at two minutes, and answers the same shape: a `failed`
attempt is still a `200`, with its sentence in `lastError`. It `400`s with no
folder configured, `409`s while another run already holds the gate, and
`403`s for an API key, the same as a program reaching `/overnight/seen`.

`/me` answers a `MeDto(Kind, Name, Role, Configured, CanSignOut, CanSignIn,
Owner, Scope, Home)` - `kind: key` for an API key, naming its own name, its
owner's name (`Owner`, null with no owner) and its owner's role lowercased (`Role`, null with no
owner); `kind: person` when a grant holds a person;
`kind: local` with the wall off and the machine's own person speaking;
`kind: none` with the wall off and nobody local either (an unidentified
browser, still able to sign in); and `204` only for an unidentified browser
against a walled Hatch - no grant, no key, no local actor.

`Scope` is `MeScopeDto(Mode, ProjectIds)`, on every answer but the `204`: the
projects the caller reaches, a key's being its owner's. `mode` is `all` for an
unbounded caller, `none` for one bounded to nothing, and `some` otherwise. It is
read from what the caller can see and not from the stored row, so a `some`
whose projects are all deleted reads `none`. `projectIds` is the ids in
ascending order for `some`, and `[]` for `all` and `none`: `all` does not list
every project, `GET /projects` does. A client that finds no `scope` - a server
that predates it - carries on as before.

`Home` is the screen the caller chose to open Hatch on: `board` or `ambitions`.
It is read from a row of the caller's own, a key reading its owner's. With no
row, no owner, or nobody at all, it is `board`, and so is a stored value this
server does not recognise. A client that finds no `home` - a server that
predates it - reads `board`. A person sets it with `PUT /me/home`, a body of
`{ "home": "board" }` or `{ "home": "ambitions" }`, which answers the same
body `GET /me` would now give. Any other word is a `400` naming the two, and a
key, a keyless runner, or nobody signed in is a `403` with a sentence.

**Refusals worth naming.** `PATCH /runners/{name}` 404s a name no runner has
ever used - it edits, it never creates. With the wall up, it also 403s a
caller who is neither the runner's own owner nor an admin, naming the owner
and the runner in the sentence. A tag that breaks the rule - more than 8, one
over 32 characters, or one with a space or comma inside - 400s with the
rule's own sentence; a heartbeat's own `tags` is never checked against it at
all, since it is ignored outright.
`POST /runners/{name}` 409s when two live runners would otherwise share one
name. `POST /runners/{name}/order` 400s a kind that is not `work` or `pass`,
and `work` with no key or one that is not an issue key; 404s a `work` key
naming an issue that does not exist; 409s a `once` runner, a `stopping`
runner, and a runner that already holds an order, naming the kind and key
held and saying to withdraw it first. `credentials/{agent}` 404s an agent it
does not know and 403s wherever the wall is up; a credential that is unset or
blank answers `204` rather than either refusal. `PUT /settings` 400s a default
token cap that is not a token count, naming the field - `the default max tokens
takes a token count, like 500k or 2M - not "x"`, or `the default max tokens per
hour ...` for the other - and writes nothing, not even a name sent in the same
request. When either default is present it also 403s a caller who is not an
admin with the wall up (`the default token budget is an admin's to set`), and
a key.

### What a night left behind

| Method | Path | Who | What it does |
|---|---|---|---|
| POST | `/issues/{key}/work-log` | key | Records what one session cost on this issue |
| GET | `/issues/{key}/work-log` | key | This issue's own sessions, and what its whole subtree has cost |
| GET | `/work-log/history` | key | Spend over time, in equal buckets |
| GET | `/work-log/sessions` | key | The sessions in a range, ranked, and what the range cost in total |
| GET | `/work-log/by-playbook` | key | The sessions in a range, folded by the playbook, transition, issue type, model, effort, outcome and reason they ran under |
| GET | `/overnight` | key | What landed in the newest night |
| POST | `/overnight/seen` | **person** | Marks the night read, for the caller |
| GET | `/activity` | key | The event trail across every issue, newest first, a page at a time |
| GET | `/changelog` | key | What shipped, a window of days at a time |
| GET | `/changelog/file` | key | What shipped, as the YAML file a repository keeps checked in |

[Watching the loop](watching-the-loop) is what draws these on screen; this
page is their shapes. `work-log`'s `POST` is the one write on this whole page
that is a key's and nothing else's, the opposite cut from everything above
it: it refuses a person - a work log entry is written by the dispatcher,
with an API key - there is no control anywhere in the browser for writing
one and none is meant to exist. Its `GET`, on `issues/{key}/work-log`,
answers a `WorkLogDto(Key, Totals, Own, Entries)` where `Totals` covers the
whole subtree and `Own`/`Entries` cover only this issue - an epic showing a
handful of its own sessions beside a much larger `Totals` is the feature,
not a bug. Each entry, and each row of `/work-log/sessions`, names the model
that ran: `dispatchModel`, `modelPlacement` (`local`, `fallback`, `cloud`, or
null from a CLI built before it was recorded) and `modelDigest` (a local
model's digest as the local server listed it before the session, null
otherwise); the `POST` accepts the same three, stores any other placement as
null and keeps a digest only on a `local` row. Each entry also carries `outcome` and `reason`,
stamped by the claim release that named this session's id - null on a row never stamped, including
every row written before this shipped. It also carries `questionId`, the question
a session answered, which the `POST` accepts and is null on every other row; a `worked` row
naming a question is what keeps the board from sending a second session to it. It likewise
carries `changeEventId`, the id of the change a session recorded outcomes on, which the `POST`
accepts and is null on every other row - see "An ambition's changes" above; a row naming a
change is left out of `GET /issues/{key}/passes`. `/work-log/history` buckets by `hour` or `day`, chosen from the
range's length unless named, and answers the bucket size it actually used;
`/work-log/sessions` ranks by `tokens` (the default), `cost`, `ended`,
`requests` or `peakContext`, each descending. All three take an optional
`ancestorKey`, read inclusive of the issue itself - deliberately unlike the
same parameter on `GET /issues` - so the graph, the table and the by-playbook
fold can never describe different populations. All three also take a
repeatable `projectId`, narrowing to sessions whose issue is in any of those
projects; none names every project, and an id naming no project contributes
nothing rather than a refusal. It is ANDed with `ancestorKey` when both are
given, and narrows the totals, the buckets and the `first`/`last` bounds
alike.
`/work-log/by-playbook` folds the same range by the playbook, transition,
issue type, model, effort, outcome and reason each session ran under - eight
fields; it answers every group, heaviest first by total tokens, ties broken on
the eight fields in that order, with no sort or limit to choose, and an id
naming no project or an `ancestorKey` matching nothing contributes no rows
rather than a refusal (an `ancestorKey` naming no issue at all is still a
400). Sessions from an older runner, which did not record those, fold into one
row of nulls, and a status name is the board's current one - null where the
column has since gone.
All three of `/work-log/history`, `/work-log/sessions` and
`/work-log/by-playbook` also take an optional `outcome`: one of `dropped`,
`worked`, `preempted`, `recused`, `unservable`, `abandoned` or `unstarted`, or
`none` for sessions with no outcome. It is not case-sensitive, and blank is
the same as absent. Given, it narrows the buckets, the sessions listed or
folded, and the totals with them, ANDed with `projectId` and `ancestorKey`; it
leaves the `first`/`last` bounds on `history` and `sessions` alone, since they
describe the log and not the range. Absent, every session is read.
Each row of `/work-log/sessions` and of `/work-log/by-playbook` carries
`outcome` and `reason`, null where the session recorded none.

`/overnight` answers an `OvernightDto(From, To, Count, Rows, Unseen)`,
grouping and gap-splitting the newest night's terminal landings; `seen` is a
person's own mark of having looked, and refuses a program (a key) for the
same reason `work-log`'s write refuses a person, in reverse. `/activity`
pages the same event trail
`/issues/{key}/events` reads for one issue, across every issue instead,
ordered the same way so one event never sorts differently between the two
reads; it takes `limit` (1 to 500, default 100) and `offset`, and a
repeatable `projectId` that narrows to events whose issue is in any of those
projects (an id naming no project contributes nothing and is not a refusal),
with `limit` and `offset` paging the narrowed rows, and answers `hasMore`
rather than a count. `/changelog` takes `before`, a repeatable `projectId`
(an id naming no project contributes nothing and is not a refusal) and
`days` (default 14), answering the newest window of days that shipped
something, with every closed month's own count beside it for the jump
control - this is the feed agents read (`hatch changelog`), unchanged; no
page of the app draws from it any more, since the Changelog report under
Reports reads `/reports/changelog` instead.

`/changelog/file` takes `remote` and answers the same bytes every time
nothing on the board has changed: every closed, parentless issue in a
project that elects `remote` for a changelog, as the YAML file that
project's repository keeps checked in. A `remote` that does not
canonicalise is a `400` with `RemoteIdentity`'s own sentence; a `remote` no
project elects is a `404` naming the Projects page.
Entries are oldest first; two projects electing the same remote interleave
their entries by close time, each carrying its own `project` key. It
answers `text/yaml; charset=utf-8`, not JSON, since this route exists for a
file on disk rather than a client reading a response body.

**Refusals worth naming.** `work-log`'s `POST` 400s a missing session id.
`history` 400s an offset outside +-1440 minutes, an unrecognised bucket
name, or a request whose buckets would exceed 1000. `sessions` 400s an
unrecognised sort or a limit outside 1-500. `history`, `sessions` and
`by-playbook` all 400 an `outcome` that is neither one of the seven outcomes
nor `none`, naming the accepted values, and all three 400 an `ancestorKey`
naming no issue. `activity` 400s a limit outside
1-500 or a negative offset. `changelog` 400s a window under one day.
`history`/`sessions`/`by-playbook`/`changelog` all 400 an
unparseable range bound, in the order `to`, then `from`, then the inversion.

### Reports and the battery

| Method | Path | What it does |
|---|---|---|
| GET | `/reports/timeline` | The top-level epics active in a range, and the work beneath each as stretches across the board's columns |
| GET | `/reports/cumulative-flow` | How many issues sat in each column over time, as a grid; a terminal column baselines at zero at the window start and counts only what closed inside it. The answer also carries `projectCounts` beside `counts`, cut by the same `project` scope, agreeing with it on the total at every point. The grid is the finest of 1, 5, 15, 30 minutes, 1, 3, 6, 12 hours, a day or a week whose points fit 1000, and the answer carries it as `stepMinutes` |
| GET | `/reports/flow` | How work moved through the board in a range: per column what entered, left and stands, and how long it dwelled, plus the window's drops, hops, send-backs, questions and sessions - counts and durations only |
| GET | `/reports/changelog` | Closed work, bucketed over a range rather than split by day - `level` is `epic` (closed top-level epics, the default) or `story` (closed stories grouped under their nearest epic). `bucket` is `hour`, `day`, `week` or `month` (day by default), aligned to the reader's own clock with `offsetMinutes`. `from`/`to` default to the changelog feed's own fourteen days; `projectId` is repeatable. `stories` answers empty until a later story; `loose`, at `level=epic`, carries closed bugs and parentless stories with `priority` |
| GET | `/reports/activity` | One actor's events in a range: `{ actor, from, to, total, kinds, issues, events, truncated, bucket, offsetMinutes, series, sessions, estimate }`. `actor` is required and matched exactly; `from`/`to` default to the fourteen days ending now, `from` clamped forward to the first event; `projectId` is repeatable. `offsetMinutes` (default 0, -1440 to 1440), `gap` (default 30, 1-1440 minutes) and `lead` (default 15, 0-240 minutes) shape `sessions`; `window` (default 3, 1-90 days) shapes `series`'s rolling mean. `includeKeys` (default `false`) adds, for an `actor` that is a person, the events under that person's own keys that a hand typed rather than the loop - a heuristic read off the trail (outside any work-log session of the issue, and outside a `claim_taken` not yet released) - each row marked `viaKey`. `total`, `kinds` (per kind, most first), `issues` (one card per issue touched, with `first`, `last`, `count`, `kinds`), `series` (one point per bucket, oldest first, with rolling means) and `sessions` (newest `from` first, with `estimatedMinutes`) cover every event in range; `events` is the newest 500 with their payloads, and `truncated` says more existed. `bucket` is the size the server chose (`hour` or `day`, the work log's own rule), and `estimate` carries the session totals beside the parameters they were computed with. An actor no row answers to answers empty. `400` for a blank `actor`, an unparseable or inverted bound, an out-of-range `offsetMinutes`/`gap`/`lead`/`window`, an unknown `projectId`, or too many buckets for the range. Open to a key |
| GET | `/reports/consumption` | What each issue matching the filter - and everything beneath it - consumed in a window: tokens, notional USD and runner time, as `{ from, to, type, sort, limit, matched, totals, firstSessionAt, lastSessionAt, rows, history }`. `type` is one of the issue types or `all` (default `epic`); `sort` is `activity` (default, `lastSessionAt` descending), `tokens`, `cost` (rows with nothing priced last) or `time`; `limit` is 1-500 (default 100) and caps `rows` only - `matched`, `totals`, `firstSessionAt` and `lastSessionAt` cover the whole filter. `statusId` and `projectId` are repeatable. A row's own `totals` is its subtree's in-window sum; a sub-epic is its own row and inside its parent's, and `totals`/`history` count each session once. `400`s, in order: a bad range bound, an offset outside +-1440 minutes, an unrecognised `type`, `sort` or an id in `statusId` or `projectId` that does not exist, or a `limit` outside 1-500. Open to a key |
| GET | `/reports/consumption.csv` | The same filter as `/reports/consumption`, with no `limit` at all - every matching row, uncapped, as `consumption-<from date>-<to date>.csv`: UTF-8 with a byte-order mark, a header row, CRLF line ends, RFC 4180 quoting. Columns: `key, title, type, project, statusId, parentKey, sessions, errors, inputTokens, outputTokens, cacheCreationTokens, cacheReadTokens, totalTokens, costUsd, unpricedSessions, runnerMs, firstSessionAt, lastSessionAt`; `costUsd` is empty rather than `0` on a row with nothing priced. The same `400`s as the JSON route, in the same order, minus the limit step. Open to a key |
| GET | `/utilization` | The caller's own Claude headroom - the battery in the nav strip |
| GET | `/attention` | What the loop is waiting on a person for, in one request |

All six a key's. `timeline` takes `from`, `to` (default the ninety days
ending now), a repeatable `projectId` and `createdBy`; `cumulative-flow`
takes `from`/`to`, the same ninety-day default, plus a repeatable `project`,
and 400s an unknown key; `flow` takes `from`/`to` the same way, plus a
repeatable `projectId` and `type`, and 400s an unknown `type`. On `timeline`
and `flow`, a `projectId` naming no project contributes nothing and is
not a refusal - unlike `changelog`'s own, below. `changelog` takes `from`/`to`
(default the **fourteen** days ending now), a repeatable `projectId`,
`bucket` (default `day`),
`offsetMinutes` (-1440..1440), `level` (`epic`, the default, or `story`) and
`loose` (`true` or `false`, trimmed/case-insensitive, default `false`), and
400s a bad range bound, an offset outside +-1440 minutes, an unrecognised
bucket name, a `level` other than `epic` or `story`, a `loose` other than
`true` or `false`, or any id in `projectId` that does not exist - in that order.
None of the four queries is itself bounded by the range - the fold needs an
issue's first move even where it falls outside it - but the ranges' bounds
are. `utilization` answers a
`UtilizationReading(State, ReadAt, ReceivedAt, Limits, Runners)` off whatever this
caller's own runners last reported on their own heartbeat, never the
account's endpoint directly, and `204`s for a caller with no runner ever
reporting one, which is the ordinary state for a fresh install rather than
an error. `ReadAt` is the runner's own word for when it read the account;
`ReceivedAt` is Hatch's own clock at the moment that reading last actually
changed, and `State` is judged on it. `Runners` is the panel behind that
headline: one `UtilizationRunner(Name, Live, State, ReadAt, ReceivedAt, Limits, Source, ProbeOff)`
per runner of the caller's that is live now, plus the one runner whose own
reading is the headline even once it is no longer live, so the headline is
never unexplained. A runner that is up but has reported nothing of its own
carries `Live: true` with the other six fields null, which is how a client
tells "no reading yet" from "not linked" without a second field. Another
person's runners, and an ownerless key's, never appear.

`attention` is the one request the nav strip's badge and panel both draw
from, since two polls a poll interval apart would show a lit control beside
an empty list. It answers an `AttentionDto`: open questions waiting on
somebody, pull requests in review split into plain reviews, conflicts, and
failing builds, how many sit in review with no pull request at all, the
issues in review that declared they were finished with no code and wait for a
person to close them (`noCodeHeld`) - not one that is pre-approved or in a
project with the no-code setting on, which the close hop carries - the
review column's issues whose branch is a contribution's, conflicted or with a
failed build, in a project with the runners-work-contributions setting off
(`contributionsHeld`: `key`, `title`, `type`, `pullRequestUrl`, a `sentence`
saying what is wrong, and the `originInstance` and `originKey` it was mirrored
from, null for one filed here) - listed there and not under `conflicts`,
`failingBuilds` or `reviewsHeldBack`, since a person acts on it - the
merge column's own held-back and still-progressing rows, trunk-landing rows
the judge holds, failing trunk builds, exhausted runners, and the
deadlocked rows the dispatcher's own scan flags - an issue waiting, directly
or through a chain, on something itself waiting on it - and the backup's
own trouble (`backup`), null when `Hatch__BackupPath` is unset or the
backup is healthy. `waitingOnARunner` lists the issues whose next move is
clear but that no live runner can take, because every live runner's own
declaration folds it on the model it runs on or on a development dependency
its project's repository asks for - a move whose ticket requires its local
model, and that no live runner lists, reads `requires <id> locally - no live
runner lists it`; each entry carries `key`, `title`, `type`,
`move` (the column the move starts from) and `missing` (what no live runner
has), ordered by tier and then rank, and the list is empty when no runner is
live. `trainStalled` lists the projects whose merge train has passed over every
live runner eligible to drive it while the head stayed on one issue, ordered by
project key. A runner that keeps checking in is still counted as passed over,
and is not elected to that head again until another eligible runner has been passed
over in turn, or no other is live. A project with a live eligible runner the train has not yet tried is not
listed. Each entry carries `projectKey`, `projectName`, `passedOverRunner`,
`electedRunner` (null when no other runner was eligible), `headKey` (the issue
at the head of the train), `at` and `reason` (the head issue's newest comment,
which a driver that skips a head writes, or null when it has none). It is
empty, not null, when no train is stalled, and the panel draws nothing for an
empty list; unlike `waitingOnARunner` it is not counted, since the row clears
only when the head moves on. `runnerCapacity`
lists the projects whose clear implementation steps no live runner, or only
one, can take - always computed, with no query flag - ordered by project key;
each entry carries `projectKey`, `steps` (how many clear implementation steps
the project has), `keys` (the first ten, in rank order), `takers` (the union
of runners able to take at least one of them - zero or one name, never more,
since a project with two or more is not listed at all) and `reasons` (one
`{runner, reason}` per live runner outside `takers`, from the project's
highest-ranked step that runner cannot take). Empty, not null, when nothing
qualifies or no runner is live. `stalled` lists every issue, outside a
terminal or deferred column, that a person has held or the board has marked
stalled, in board order (column, then rank, then id), whatever the board's
shape; each entry carries `key`, `title`, `type`, `stalledAt` (null when it is
only held), `stalledWhy`, `held`, `letGo` (how many times in a row the loop let
it go, 0 when it is not marked stalled), `sentence` (the queue's own words for
why it is folded, null when nothing folds it now) and `kind`: `person` when
only a person can move it on - held, stalled on a board that never resumes, or
stalled past the window at the resume limit - and `loop` when the board
resumes it by itself. `stallResumeSeconds` is the board's resume window, 0 when
it never resumes one. `stalled` is empty, not null, when there is none.
`heldAtProjectLine` lists the issues you can see that the loop would carry now
if every level and pull they inherit were honoured, and does not, because the
level or pull comes from another project whose setter has no say there: an
issue in review with a clean branch under a pre-approved ancestor, one in a
column marked for express under an express ancestor, or a child left of
implementation under a parent in progress, each in another project. Only an
issue held for nothing but that is listed - one a claim, a dependency or a full
column already holds is not. Each entry carries `key`, `title`, `type`,
`projectKey` (the issue's own project, the one whose approver is needed) and
`sentence` (the queue's own words). Where the ancestor is outside your scope the
sentence names no key, only "an ancestor in another project" or "its parent in
another project". An entry leaves when an approver of `projectKey` sets the level
on the issue itself, or when the issue moves; a child held for its parent's pull
has no level to set, so it leaves only by a move. It is in the queue's order,
and empty, not null, whatever the board's shape. It is not counted here.
`findings` lists every open [goose](the-goose#findings) finding you may see,
longest-standing first (`beganAt`, then `id`); each entry carries `id`, `check`,
`projectKey` (null where the check names no project), `sentence`, `beganAt`,
`openedAt`, `bugIssueKey` (null until a bug is filed from it) and `issueKey`,
the issue the finding is about, or null when it is about the project - derived
when read, from the finding's token being a key in its own project, and never
stored. A caller whose scope is narrowed gets only the findings naming a project
they can see, and never one that names none. It is empty, not null, whatever the
board's shape. `goose` is the goose's own state: `lastSweepAt` (null when no run
is stored), `intervalSeconds` and `stopped`, true when the newest run's last
sweep is older than three intervals - or, with no run stored, when the server
has been up that long. The read judges `stopped` by its own clock, and the sweep
never writes it. It is answered to every caller whatever their scope. Both are
omitted by a server that predates them, so a client reads an absent field as
none found and a goose that is running.

**Refusals.** None beyond the ordinary ones - every route in this group is
a read with no body to reject.

### The goose

The server's own sweep of the board, and the record it keeps of each one. See
[The goose](the-goose) for what a sweep is and how to read a run.

| Method | Path | What it does |
|---|---|---|
| GET | `/goose/runs` | Runs newest first by last sweep, as `[{ id, firstRanAt, lastRanAt, sweeps, longestMs, checks, checksFailed }]`, without the per-check detail: `checks` is how many entries the run holds and `checksFailed` how many of them threw. `limit` is 1-200 (default 50); a value outside it is clamped. A caller whose scope is not every project reads `[]`. Open to a key |
| GET | `/goose/runs/{id}` | One run with its detail: the same fields and `entries`, one per check in the order the checks ran, each `{ check, looked, concluded, failed, error }`. `404` for an id that is not there, and for every id when the caller's scope is not every project - a run holds what was seen in all of them. Open to a key |
| GET | `/goose/findings` | Findings, open ones first (longest-standing first), then closed ones (newest first), as `[{ id, check, subject, projectKey, sentence, evidence, beganAt, openedAt, closedAt, bugIssueKey }]`, `evidence` a list of `{ label, value }`. `?open=true` narrows it to the open ones. A finding that names a project is left out for a caller who cannot see it; one that names none is only in an unbounded caller's list. Open to a key |
| POST | `/goose/findings/{id}/bug` | Files a bug from a finding and stores it on the finding, then answers the bug as an issue. The body is optional: `priority`, `statusId` and `preApproval` as on `/trunk-builds/{id}/bug` for a person (expedited, no pre-approval and the leftmost column by default; `preapproved` takes an owner or approver of the project), and `projectKey`. The bug goes to the finding's own project; a finding that names none needs `projectKey`. A second call answers the bug already filed. Refused to a key |

**Refusals.** `404` as above; nothing else beyond the ordinary ones.

`POST /goose/findings/{id}/bug` is a person's press, so a key is refused it with
`403 key_not_accepted`. Its refusals are `404` for a finding that is missing or
that names a project the caller cannot see; `400` for a `projectKey` that names a
project other than the finding's own, for a finding that names no project and
comes with no `projectKey`, for one that comes with a project the caller cannot
see, and for an unknown `priority` or `preApproval`; `403` for `preapproved`
from someone who is not an owner or approver of the project; and the ordinary
column refusals for `statusId`. A closed finding is filed like an open one.

## Who may call what

A key reaches every route of the board's own API (everything under `/api` except sign-in, `people`, `ui-logs` and the routes that take a **person only**). A key that calls one of those gets a
`403`, and that is the gate working as designed, not a misconfiguration -
[People, roles and keys](people-roles-and-keys#runners-and-their-keys) says the same thing
in prose, grouped by what the route does; this list is the route-by-route
version it already points to, and should not repeat or contradict it.

Recounted directly from the `[RequireRole]` attributes - the method's where
it carries one, the class's otherwise (`AssigneeController` is the one file
on this page with both) - there are **46** person-only paths on this page,
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
PUT    /projects/{id}/check-approves-merge
PUT    /projects/{id}/no-code-closes
PUT    /projects/{id}/runners-work-contributions
PUT    /projects/{id}/files-contributions
PUT    /projects/{id}/one-pr-policy
PUT    /projects/{id}/priority
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
PATCH  /issues/{key}/budget
PUT    /issues/{key}/assignee
PUT    /issues/{key}/priority
PUT    /issues/{key}/expedite
PUT    /issues/{key}/pre-approval
PUT    /issues/{key}/express
PUT    /issues/{key}/local-model
PUT    /issues/{key}/human-work
PUT    /issues/{key}/hold
PATCH  /issues/{key}/playbook
POST   /attachments
DELETE /attachments/{id}
PATCH  /runners/{name}
POST   /runners/{name}/order
DELETE /runners/{name}/order
GET    /settings
PUT    /settings
POST   /overnight/seen
PUT    /me/home
POST   /backup/run
POST   /playbooks
PATCH  /playbooks/{id}
DELETE /playbooks/{id}
```

Thirty-one belong to a project, a column, the WIP section, a ticket's own
fields, or attachment upload and delete; the other eleven are a runner's own record (now
three - the heartbeat's `PATCH` and the order's `POST`/`DELETE`),
Hatch's settings on both verbs, marking the overnight seen, the home screen,
running a backup, and the three playbook writes.

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

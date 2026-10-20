# Hatch

## Summary

The house project tracker: a kanban board at `hatch.${DOMAIN}`, issues with
Jira-style keys (`AER-12`), operator-editable columns, comments, and an
append-only audit trail. It replaced a folder of markdown plan files, and it is
the system of record for what Hatch is working on.

The name is the product. You hatch a plan here, and epics hatch into stories
into shipped work.

It is spelled `Hatch` / `hatch` everywhere — C# namespace
`Hatch.Api.Modules.Hatch`, Postgres schema `hatch`, routes under `/api/hatch`,
bundle at `/apps/hatch/`, and its own host `hatch.${DOMAIN}`.

Four properties carry the design:

- **One board, every project.** A project is a key namespace, not a container.
  Switching boards to find out what is next is the thing the markdown files
  already did badly.
- **Behind the wall, and admin-gated on top of it.** The bundle 404s and the
  API 403s for anyone who is not the operator — the same posture the admin app
  takes, through the same gate.
- **Claude is a first-class caller.** An `Authorization: Bearer hatch_ak_…` key
  reaches `/api/hatch/*` and nothing else, decided by the same gate that
  decides everything else. A ticket link is a complete instruction, and the
  ticket is where the answer goes back.
- **Everything an agent needs to decide is decided on the server.** Which issue
  is next, which column it is headed for, whether it may go there, what it is
  told when it gets there, and what a subtree adds up to. A client that
  re-derived any of those would drift the first time a column was renamed.

The [workflow contract](#the-operator-and-claude-contract) — what an agent may
do to a ticket, and what only a person may — is the last section, and is
mirrored in [`CLAUDE.md`](../CLAUDE.md) because that is where an agent reads it.

## Goals

1. **A board the operator lives on** — every issue from every project on one
   kanban board, drag to change status, drag to reorder, always visible.
2. **Claude as a first-class user** — paste an issue link into a session and it
   reads the ticket and gets to work; a planning session writes the plan back
   onto the ticket and files the children.
3. **Audit from day one** — every mutation leaves an append-only event row, so
   throughput and cycle time are a query away rather than a migration away.
4. **The smallest tool that beats a markdown editor**, on the existing pod,
   with no new infrastructure.

## Non-goals

These are absences on purpose, and each one is cheap to add later if the
absence starts to hurt:

- **One status-transition rule.** A move whose *target* column is protected
  (`EfHatchStatus.IsProtected`) is refused unless the caller is a live owner or
  approver of the issue's project — checked by `IProjectAccess.CanApproveAsync`
  in `IssuesController.cs`'s `MoveIssue`/`StageEditAsync` (shared by
  `PatchIssue`/`BulkEdit`), folded a second time in `Dispatch.Blocked`
  (`Dispatch.cs:803`) beside the terminal fold — with one hop excepted from
  it by name, the **approve** hop (`hopKind != HopKinds.Approve`), the one
  case a dispatch *does* land an issue in a protected column, because a
  pre-approval is that owner's or approver's permission already given in
  advance rather than a program's own judgement call (see [the hop](#the-hop)
  and [the four paths past the terminal guard](#the-four-paths-past-the-terminal-guard))
  — and refused a third time, client-side, in `hatch move`'s speed bump
  (`IssueCommands.cs:256`). Moving *out* of a protected column, and
  reordering within one, need no membership — the check only fires when the
  target column changes and is itself protected. A program (an API key, or the
  runner header — `ICallerIdentity.IsProgramAsync`) is refused regardless of
  membership; the local operator with the wall off passes, subject to the same
  check any other person gets. A narrower, separate refusal rides beside it: a
  move into a column with `MergesPullRequest` set is `400` for an issue with no
  `PullRequestUrl` recorded. Beside that one rule, any column to any column; we
  trust ourselves. The other rule in the whole flow is a speed bump rather than
  a wall: a move into a full [WIP](#wip) section is refused, and a person may
  always step over it by saying *move anyway*. The next asymmetry is not a rule
  in the database either — it is that an agent is never dispatched *into* a
  terminal column (see [the dispatcher](#the-dispatcher)). The next thing that
  could be mistaken for one is [closing a subtree](#closing-a-subtree), and it
  is not a rule either: it is a question the browser asks a person after a
  move has already committed. Nothing is forbidden by it and nothing is
  required — the answer is the operator's, and "leave them" is one of the two.
  Nor is `fromStatusId` on a move: it is a precondition on what the caller last
  saw, not a rule about which columns may follow which.
- **One granular permission.** A project's `EfHatchProjectMember` role
  (`Owner` or `Approver`, from HA-290) grants exactly one additional thing
  beyond the global role — moving an issue into a protected column, read
  through `IProjectAccess.CanApproveAsync`/`HasRoleAsync`. Beside that one
  permission, reaching Hatch at all means trusted to do everything else in it.
  The API key's scope is still a statement about *which surface*, never about
  which verb.
- **No swimlanes or sprints.**
- **No GitHub integration beyond one read and two writes.** A commit sha in a
  comment is the link, and it is written by whoever did the work. What is read
  is the build on the tip of a branch in review, and with it the pull request's
  merge state and review decision — the forge's own answer to whether it can
  merge and whether it is approved. Those two are stored on the issue, with the
  head sha they were read at and when, and drawn; the merge state is read by
  [the merge column's judge list](#the-approval-column) and nothing else on the server, and the review
  decision by nothing. What is written, once a pull request stands approved in a protected
  column, is the merge itself and the branch update that keeps it current with
  the trunk while it waits. All three are the runner's own `gh`, never the
  server — see [the build half of checking the branches in
  review](#checking-the-branches-in-review). What is still not read: anything
  at all from the server. Nothing in Hatch names a forge or inspects a host.
  The review decision is drawn beside the pull-request chip
  (`PullRequestStateChips.tsx`) and that is all it is for: Hatch's own approval,
  a person moving the issue into a protected column, stays the one gate
  (HA-558, decided *hatch-still-approves*). The read that carried the build
  grew two fields, the merge state and the review decision, and nothing on the
  server acts on either beyond the merge state above - `MergeWork.Judge` takes
  the merge state and the sha it was read at, and no review decision.
- **No websockets.** The board refetches on action and on focus, and every 30
  seconds while it is on screen, and at once when it returns to the screen. It
  was once refetched on action and focus alone, on the premise that whoever
  moves a card is looking at it; the loop moves cards all night, so the premise
  lapsed and the board polls.
- **Events are rendered as column history, and no further.** The
  [Timeline](#the-timeline) reads the `status_changed` events and draws them as
  stretches in each column; no other kind is drawn, and nothing draws the rest
  of the trail as a report of its own. The Plan view still answers "how far
  along is this" from current state.

## Where it lives

**Backend: a module.** `src/Hatch.Api/Modules/Hatch`, its own `hatch` schema,
its own migration history, one line in `AddAppModules` — the shape
[`Modules/README.md`](../src/Hatch.Api/Modules/README.md) describes. Splitting
it into its own service later is a connection-string change, so "part of the
API for now" costs nothing architecturally.

**Frontend: its own workspace SPA**, not a family-shell module.
`src/Hatch.Web/apps/hatch`, built into `wwwroot/apps/hatch`, served by the same
pod — the arrangement the admin and docs apps already use. It is an operator's
tool, and the family shell is the household's.

**The wire contract: its own project.** `src/Hatch.Contracts` holds the
records the API serves and the records a client sends it — nothing else, and
nothing that needs a `DbContext` to compile. It is a project rather than a file
in the module because there are now two kinds of client compiled against it: the
API on one side, and the runner below on the other. A runner that redeclared the
shape of a dispatch would drift from the server the first time somebody added a
field, and drift *silently*, because JSON does not complain about what it did
not find.

**The runner: a program.** `src/Hatch.Cli` is `work` and `go-to-work` — see
[where the loop lives](#where-the-loop-lives), which is about why they stopped
being shell.

**Host: one container, one origin.** The root [`compose.yaml`](../compose.yaml)
is the standard way to run this install: `db`, a `migrate` step, and `api`
serving every app — Hatch included — from `http://localhost:8080` (or
`HATCH_PORT`). There is no reverse proxy in front of it and nothing to rewrite:
the bundle's own assets live under `/apps/hatch/`, and `/api/hatch/*` is how a
key can `curl` the API on the same origin the browser uses. The bundle reads its
basename off the URL (`lib/basename.ts`) rather than assuming a prefix, which is
what lets it serve unchanged wherever `/apps/hatch/` ends up mounted.

**The installed app.** `manifest.webmanifest`
(`src/Hatch.Web/apps/hatch/public/manifest.webmanifest`) scopes itself to the
whole origin, `/`, rather than `/apps/hatch/`, because sign-in lives outside
that prefix (`/apps/auth/`) and because a deployment that serves Hatch at a
host's own root has ticket links with no prefix at all — either one, inside
a narrower scope, would open behind a browser bar instead of staying in the
installed app's own window. There is no service worker: Hatch's deploy story
depends on the browser revalidating `index.html` on every load, and a
service worker's cache is exactly the layer that turns a deploy into a stale
app until somebody clears it by hand. The manifest's own `<link>`, in
`index.html`, carries `crossorigin="use-credentials"` because the wall
answers a cookieless fetch with a 401, and a 401 on the manifest is an app
that silently never offers to install. `GET /.well-known/assetlinks.json`
(`AssetLinksController`, [below](#the-wall-the-roles-and-api-keys)) answers
Android's own link-ownership check with the operator's package name and
certificate fingerprint — supplied as deploy-time config
(`Apps__AssetLinks__0__PackageName`/`…Sha256CertFingerprints__0`,
`compose.yaml`), never a value this repository could know on an operator's
behalf.

**Read [`hatch-at-home.md`](../src/Hatch.Web/apps/hatch/public/hatch-at-home.md)**
for the operator-facing version of the above — the one line, the runner, and
the block to paste into a friend's own `CLAUDE.md`. It ships inside the Hatch
bundle itself (`/apps/hatch/hatch-at-home.md`) rather than living only in this
repository, so it travels with the board it describes.

## Domain model

Seven tables in the `hatch` schema, all carrying the house `Ef` prefix. The
authority is [`Entities.cs`](../src/Hatch.Api/Modules/Hatch/Entities.cs), which
carries the per-field reasoning; this is the shape and the decisions worth
having in one place.

### Migration order

[`HatchContext`](../src/Hatch.Api/Modules/Hatch/HatchContext.cs) owns its own
migration history, applied from its own
[`Modules/Hatch/Migrations/`](../src/Hatch.Api/Modules/Hatch/Migrations/)
folder against its own `__EFMigrationsHistory` rows - distinct from the
unrelated `AppDbContext`'s home-automation migrations at
[`Migrations/`](../src/Hatch.Api/Migrations/). `Program.cs` calls
`MigrateAsync` once per registered module context, `HatchContext` included, in
a loop (`Program.cs:384-387`), only in migrate mode, ahead of any serving
replica.

EF Core's `MigrateAsync` reads `__EFMigrationsHistory` - one row per applied
`MigrationId` - and applies every migration in its assembly whose
`MigrationId` is not already a row there, in ascending `MigrationId` order:
the id, not the merge commit that landed it, decides the order. So two
branches that each add a migration, merged into trunk in either order, both
apply correctly - a migration created first but merged second is still
applied in its own id's place, never skipped or reordered by merge order.

What a migration-order worry actually finds is a different file: two
branches that both touch
[`HatchContextModelSnapshot.cs`](../src/Hatch.Api/Modules/Hatch/Migrations/HatchContextModelSnapshot.cs)
produce a real merge or semantic conflict there - caught today by
`ModelSnapshotTests.TheModelHasNoChangesItsSnapshotDoesNotKnowAbout`
(`Hatch.Api.Tests/Modules/ModelSnapshotTests.cs:37`), which fails the moment a
context's live model and its stored snapshot disagree, the same way HA-150's
`HasDefaultValue` on `Playbooks.Shape` once did.

### Project

`EfHatchProject` — `Key` (unique, `^[A-Z][A-Z0-9]{1,5}$`), `Name`,
`NextIssueNumber`, `CreatedAt`, `Color` (`#rrggbb`, nullable - no default and no
backfill, so the browser paints `--muted` when none is set), `Icon` (a slug
into a closed set the browser owns, not the server, nullable the same way),
`Landing` (`"pull-request"` default, or `"trunk"` - how a project's finished
work reaches the trunk, by pull request or pushed straight to it), and
`ChecksRequired` (`bool`, default `true` - whether a trunk tip, or a
merge-column branch, with no checks run on it counts as green; see
[the merge column's judge list](#the-approval-column) and [the trunk-landing
list](#landing-on-the-trunk)), and `OnePullRequest` (`bool`, default `false` -
the project's default for whether a story's tasks share the story's branch and
one pull request, which a story can override either way - see
[One pull request](#one-pull-request)), and `RepoSourced` (`bool`, default
`false` - whether the project's playbooks are read from
`.hatch/playbooks.yaml` on its primary repository's trunk instead of from
rows naming it on the Playbooks page - see [Playbooks](#playbooks)), with
`RepoSourcedBy` and `RepoSourcedAt` beside it - who set it last and when, a
name and an instant, null until the first write. A project may also carry an
uploaded logo, in its own table — see [Logo](#logo).

An owner alone sets how a project's work lands. `Landing`, `ChecksRequired` and
`OnePullRequest` are written through `PUT /projects/{id}/landing`
(`ProjectsController.PutLanding`) and nowhere else; `PATCH /projects/{id}`
carries the name, key, colour and icon and never reads those three. The route
is plain `[RequireRole(User)]`, so a key cannot call it, and it answers `403`
naming the project's key to any person who is not one of its owners - the
`PATCH` row in [the API table](#api-surface) says nothing of why, and this is why. The
project dialog follows the same guard: where the project's `canAdminister` is
false it shows a person who is not an owner the choice read out as a sentence
(*"On the trunk|By pull request, checks required|not required, one pull
request per story|task."*) in place of the controls
(`ProjectEditDialog.tsx`).

`RepoSourced` is a fourth field that sentence does not reach: it is written
through its own route, `PUT /projects/{id}/playbooks-source`
(`ProjectsController.PutPlaybooksSource`), guarded exactly the way
`PutLanding` is - `[RequireRole(User)]`, `NotAPerson` checked again in the
action, then an owner check answering `403` naming the project's key (*"only
an owner of `<KEY>` may change whether its playbooks are sourced from its
repository"*) to anyone else. Turning it on is refused for a project that
binds no repository (*"`<KEY>` binds no repository - bind one on the
Projects page first"*) and for one that table rows still name - naming the
count: *"`<N>` playbook row(s) still name(s) this project - delete it/them or
hand it/them to the board first"*. Turning it off writes nothing else; the
playbook rows are untouched either way, on or off. `PlaybooksController`
refuses to create a row naming a repo-sourced project, or to re-point one at
it: *"project `<KEY>` reads its playbooks from .hatch/playbooks.yaml on its
trunk, not from a row here"* (`PlaybooksController.cs:73-74`, `:158-159`).

A project exists so an issue can be called `AER-12` rather than `#4471`, and so
two efforts can number themselves independently. It is deliberately **not a
container**: the board shows every project at once.

The key is changeable, behind a speed bump in the browser (type the old key to
confirm). The cost is stated to the operator rather than decided for them: every
`AER-12` written into a commit message, a branch name or a chat log goes dead,
because those are references this database has never seen and cannot rewrite.
What survives is the part that matters — parentage is a foreign key and the
number is the issue's own column, so a rekeyed project keeps every story under
its epic.

That is a project renaming its own key, in place. An issue changing which
project it belongs to is a different rekey, paid by that issue alone — see
[Moving between projects](#moving-between-projects).

### Project Member

`EfHatchProjectMember` — `ProjectId` (FK), `PersonId` (a bare `Guid`
rather than a foreign key, for the reason `EfHatchIssue.AssigneePersonId`
is one: Hatch owns its own schema and migration history, and a
constraint into `public.People` is the compile-time coupling
`Modules/README.md` exists to prevent), `Role`
(`ProjectMemberRole.Owner` or `.Approver`, nothing else), `CreatedAt`.
Unique on `(ProjectId, PersonId)`.

One row is one person's role on one project — who may own it, or approve
on it — the one question `IProjectAccess` answers about the caller
alone, never about an arbitrary person. `ProjectDto.Members` is the same
list `GET .../members` answers with, resolved — a row whose person no
longer exists is silently dropped rather than shown as a name nobody can
reach. `CanApprove` and `CanAdminister` are the same two questions asked
about the caller: a live owner or approver, and a live owner — the
booleans a client reads instead of re-deriving the role list itself.

**Reading is open to a key — "reading who is on a project is not a
power," in the route's own words.** Writing it is a person's alone, and
an owner's alone beyond that: `PUT /projects/{id}/members/{personId}`
sets or adds a role (`{ role }`, `"owner"` or `"approver"`), refused with
`403` from anyone who is not a live owner, `400` naming an unknown person
or an unknown role, and `409` where it would drop the project's last
owner; `DELETE /projects/{id}/members/{personId}` takes the same `403`
and the same no-owner-left `409`. Both are checked twice, the
`ProjectsController.NotAPerson` idiom a [repository](#repository) or a
[logo](#logo) write already uses — a key or a keyless runner is refused
outright before the owner check ever runs.

**`POST /projects/{id}/claim` is the way out of a project left with no
owner** — every guard above exists to keep from reaching that state any
other way, and this is the one door back out of it: whoever calls it
becomes an owner, refused with `409` ("{key} already has an owner") the
moment one already exists.

### Logo

`EfHatchProjectLogo` — `ProjectId` (PK, cascading FK), `Bytes`, `ContentType`,
`UpdatedAt`. The shape of `EfPersonPhoto` (see `Ef/People.cs`), moved into the
Hatch module: bytes in the row ride the existing backup, so there is no volume
and no second backup story.

The type is sniffed from the bytes by `PersonPhoto.TryDetectContentType`
(PNG, JPEG, GIF, WebP), never taken from the request's declared Content-Type —
a client that says PNG and sends HTML is describing an attack, not a logo. SVG
is refused for the reason `PersonPhoto.cs` states: it is a document that can
carry script, and an image format that executes is the one thing serving
uploads from the install's own origin must not do.

**Reading is open to a key; writing is a person's alone** — the same cut as a
[repository](#repository): `[RequireRole(User)]` with no scope on the `PUT`
and the `DELETE`, checked again in the action.

`ProjectDto.LogoUpdatedAt` is what lets the browser point an `<img>` at a
stable `/api/hatch/projects/{id}/logo?v={ticks}` URL and still see a new
upload the moment it lands — the same role `PersonDto.PhotoUpdatedAt` plays for
a person's photo. Deleting a project takes its logo with it.

### Repository

`EfHatchProjectRepository` — `ProjectId` (FK, cascade), `Remote` (as typed),
`Canonical`, `BaseBranch` (nullable), `SortOrder`, `CreatedAt`. Unique on
`(ProjectId, Canonical)`.

A project carries an ordered list of git remotes rather than one: the first is
the primary, and a monorepo bound into two key namespaces is a real shape, so
two projects may bind the same remote. The list is replaced whole on every
write — there is no "add one remote" verb — which is what makes "re-sending
what is already there writes nothing" a fact the handler checks rather than one
the database enforces for it.

**The canonical form is the one rule for when two spellings are the same
repository**, and it lives in exactly one place, `RemoteIdentity.Canonical` —
never in the CLI, never in the browser. `https://host/owner/repo.git`,
`ssh://git@host/owner/repo`, `git@host:owner/repo.git` and `host/owner/repo/`
all fold to `host/owner/repo`: scheme and user info dropped, a port kept, a
trailing `.git` and a trailing slash dropped, the whole lowercased. A local
path folds to its full path with its case intact — a Unix path is
case-sensitive, and folding it would silently merge two different repositories
on disk. A remote that yields no host-and-path is refused with a sentence.
Every client sends what it has and lets the server decide what matches, so two
callers can never come to disagree about it.

**Reading is open to a key; writing is a person's alone**, `[RequireRole(User)]`
with no scope, the same cut as a playbook, an assignee and a runner's bounds
(see ["the one edge that is deliberately cut"](#the-one-edge-that-is-deliberately-cut)).
A runner that could bind a remote could point every runner the board ever
dispatches at a repository nobody chose — the exact failure the closed edge
exists to prevent.

### Status

`EfHatchStatus` — `Name` (unique), `SortOrder`, `IsTerminal`, `IsDeferred`,
`IsWip`, `ExpressSkips`, `ParentPulls`, `AgentFiles`, `IsImplementation`,
`IsProtected`, `MergesPullRequest`, `Color` (`#rrggbb`).

One row is one column on the board. **Global, not per-project**, because the
board shows every project at once and a per-project set would have no column to
put a foreign issue in. Rows rather than an enum because the operator renames
and reorders them from the Statuses page, and an enum would make "add a review
column" a deploy.

`SortOrder` is sparse (the seed is 10 through 70, in tens) so inserting a column
between two is one write.

`IsTerminal` marks the columns that mean *shipped*. Three things read it: the
importer lands a checked box in a terminal column, the dispatcher refuses to
move anything into one, and the browser offers to close a subtree when an issue
lands in one (see [closing a subtree](#closing-a-subtree)).

`IsDeferred` marks the columns that mean *parked* — shelved, not shipped, and
not coming back on its own. Any number of columns may carry it, and it is
independent of `IsTerminal`: the two answer different questions, and both boxes
on one column is not refused (the reads that care take deferred first).

**A deferred column is not drawn on the board, and has no drop target.** That is
the whole of the feature and the reason it is a flag rather than a convention:
parking a ticket is a decision somebody makes about a particular ticket, not a
lane work drifts into, and a column nobody can drag to is a column nothing lands
in by accident. The way in is a press, not a drag: the issue page's status bar,
or the board's own peek, opened off a card and pressed rather than dropped —
both offer every column including the deferred ones — see [deferring an
issue](#deferring-an-issue). A drop may still overshoot a lane; a press in a
list cannot, which is why the peek may offer the shelf where a drag does not.

Everything that measures the board measures it off the columns that are drawn
(`Columns.Board`, and `boardColumns` in `lib/columns.ts`), so a shelf sorted
between two lanes moves no landmark: the review column stays where it was, and
`In Progress` still advances into `In Review` across the gap. The dispatcher
refuses to start anything sitting in one — *a person puts it back on the board,
not a pass* — and the rollup leaves deferred leaves out of every total, so an
epic finished except for work nobody is going to do reads finished without
claiming the shelved half shipped.

`IsWip` marks the columns in the WIP section — the lane or lanes the operator
wants bounded. It is read only where the column is neither deferred nor
terminal: a flag stranded on a column that has since become either does not
silently hold a limit down. It is written only through `PUT /api/hatch/wip`, by
a person — see ["the one edge that is deliberately
cut"](#the-one-edge-that-is-deliberately-cut) — never through a status write, so
the ordinarily key-writable `PATCH /api/hatch/statuses/{id}` cannot reach it.
The limit itself is a row in `hatch."WipLimits"` (`Types`, `Limit`), one per
slice of the board the flag is measured against; no row means no limit. Hatch
knows exactly two slices, `story,bug` and `epic`, in that fixed order — the
section is one set of columns and any number of slices, each with its own
limit, its own load and its own claimed-inbound count.

`ExpressSkips` marks the columns an [express](#pre-approval) issue is carried past
with no session, and — once something under a running epic stands there too —
a story or bug carried under it the same way — see [the hop](#the-hop). It is
**not** a "whose column is this" flag: who works a column is still derived
from the playbook matrix, for the reason [above](#status) — this box only
says which columns an express issue, or a running epic's own story or bug, is
carried past, and says nothing about who works the columns on either side of
it. Set only by a person, through its own route
(`PUT /api/hatch/statuses/{id}/express-skips`), for the reason given under
["the one edge that is deliberately cut"](#the-one-edge-that-is-deliberately-cut):
it decides which gates the loop may pass unattended, and a key that could tick
it could carry its own ticket through the night with nobody reading it first.
Neither `POST /api/hatch/statuses` nor `PATCH /api/hatch/statuses/{id}` can set
it.

`ParentPulls` marks the columns a child is carried on out of, with no session,
while its parent stands in the implementation column — seeded true on
`Backlog` and read by [the hop](#the-hop). Set only by a person, through its
own route (`PUT /api/hatch/statuses/{id}/parent-pulls`), for the same reason
`ExpressSkips` is: it decides which gates the loop may pass unattended.
Neither `POST /api/hatch/statuses` nor `PATCH /api/hatch/statuses/{id}` can
set it either.

`AgentFiles` marks the column a program's issue is born in — filing an issue
reads the leftmost column, as it always has, except where an issue is being
filed by a program (`ICallerIdentity.IsProgramAsync`) and a column carries
this flag, in which case that column is used instead. At most one column ever
holds it — `PUT /api/hatch/statuses/{id}/agent-files` clears every other row
when it ticks one, the same way `express-skips` does. It is read only where
the column is neither deferred nor terminal: a flag stranded on a column that
has since become either does not silently apply, the same guard `IsWip` reads
under. Set only by a person, through its own route
(`PUT /api/hatch/statuses/{id}/agent-files`), unreachable from either ordinary
status write — neither `POST /api/hatch/statuses` nor
`PATCH /api/hatch/statuses/{id}` can set it. A board with nothing ticked files
every issue leftmost, whoever files it, exactly as it always has.

`IsImplementation` marks the implementation column, the one where code gets
written, labelled *Code written here* on the Statuses page. Every rule keyed on
that column follows it: a [dependency](#dependency) holds the move into it, an
issue with a child still open is not carried out of it, and
[`ParentPulls`](#the-hop) carries a child on while its parent stands in it. All
three read `Columns.Implementation`. At most one column ever holds it —
`PUT /api/hatch/statuses/{id}/implementation` clears every other row when it
ticks one, the same way `agent-files` does. Ticking a deferred or terminal
column is refused with a 400 naming the column; unticking is always accepted.
It is read only where the column is neither deferred nor terminal: a tick
stranded on a column that has since become either is ignored, and the board
answers as if nothing were ticked, the same guard `IsWip` and `AgentFiles` read
under. Set only by a person, through its own route, for the reason under
["the one edge that is deliberately cut"](#the-one-edge-that-is-deliberately-cut):
it decides which move a dependency holds, and a key that could move it could
ungate its own ticket. Neither `POST /api/hatch/statuses` nor
`PATCH /api/hatch/statuses/{id}` can set it: a new column starts unticked, and a
PATCH of other fields leaves it as it was. **Nothing ticked means measured,
exactly as before.** The implementation column is then the one whose own next
move is into the awaiting-review column (see [Dependency](#dependency)). No
migration ticks it, so the stock board is measured.

`IsProtected` marks a column only an owner or approver of the issue's
project may move work into — never a key, never a hop, never a dispatch;
that gate is HA-293's own question, and this flag only carries it. Ticking
it refuses a deferred or a terminal column, the same refusal `AgentFiles`
and `IsImplementation` give those columns for their own question;
unticking it refuses while `MergesPullRequest` still holds, since a merge
column with nobody guarding its door would let anything that can move a
card merge its own code. Set only by a person, through its own route
(`PUT /api/hatch/statuses/{id}/protected`), checked again in the action —
the same `ProjectsController.NotAPerson` idiom a [repository](#repository)
write uses — for the same reason `ParentPulls` is closed to a key: it
decides who may move work past this door, and that is a playbook's kind of
power. Neither `POST /api/hatch/statuses` nor
`PATCH /api/hatch/statuses/{id}` can set it.

`MergesPullRequest` marks the column whose runner poll merges an issue's
pull request once it stands there, current and green, with no session —
HA-289's epic. Ticking it requires `IsProtected` already on — a column
that merges but is not protected would let anything that can move a card,
a key included, merge its own code — and refuses a deferred or a terminal
column for the same reason `IsProtected` does; unticking is always
allowed, the same stranded-flag tolerance `IsWip` gives. Set only by a
person, through its own route
(`PUT /api/hatch/statuses/{id}/merges-pull-request`), for the same reason
`IsProtected` is: it decides whose code merges unattended, and that is a
playbook's kind of power. Neither `POST /api/hatch/statuses` nor
`PATCH /api/hatch/statuses/{id}` can set it.

`Color` is a column rather than a palette keyed on the shipped names, because
the operator invents columns — a lookup by name would leave a new one grey
forever and lose a renamed one's colour. The ink written on a colour is computed
from its luminance (`@hatch/ui`'s `color.ts`, shared with `<ProjectMark>` — see
[`docs/design-system-architecture.md`](design-system-architecture.md)), because
CSS still cannot ask that question.

Every install starts with the same seven columns, and with the same flow
through them. `In Review` is the operator's except for two things: a pull request
that has stopped merging cleanly, and one whose build has failed, are an agent's
to fix, and that is the only work the loop does in it.

No column ships deferred. It is a box the operator ticks on the Statuses page
for a column they added — `Shelved`, `Someday`, `Won't Do For Now`, whatever
they call it — and until they do, every board behaves exactly as it did before
the flag existed.

| Column | Sort | Terminal | WIP | Whose | What happens in it |
|---|---|---|---|---|---|
| Draft | 10 | | | operator | An idea being written. Nothing reads it. |
| Breakdown | 20 | | | **agent** | Turn the draft into a specification: acceptance criteria on the issue, children under it. |
| Backlog | 30 | | | operator | Specified work, awaiting selection. `AgentFiles` is ticked here, so an issue a program files is born in this column. |
| To Do | 40 | | | **agent**; an epic is pulled in with no session | Analyse it until implementing it is mechanical. |
| In Progress | 50 | | ✓ | **agent** | Write the code, get it green, push it, put it up for review. Nothing is ticked `IsImplementation` on the stock board, so this is the implementation column by measurement: the one just left of review. |
| In Review | 60 | | ✓ | operator; **agent** for a conflict or a failing build | Read the pull request, wait for green, merge. An agent steps in only when the branch has stopped merging with the trunk, or the build on its tip has failed, and fixes that on the branch — see [the review playbook](#playbooks). Conflicts come first. A build that is passing, still running or unread is left alone. |
| Done | 70 | ✓ | | operator | Terminal. |

A board that adds one — ticking `Protected` then `MergesPullRequest` on a
column between `In Review` and `Done` — is walked through operator-side in
[Letting the loop merge for you](../src/Hatch.Web/apps/hatch/public/hatch-at-home.md#letting-the-loop-merge-for-you).

A migration flags `In Progress` and `In Review` on a board that still has
columns of exactly those names, and writes no limit row — the WIP section is
marked, and from then on `board.wip` reports a real load for both slices, but
the section-wide limit draws, refuses or folds nothing until the operator
types a number on the Statuses page. An epic's own limit is different: it
holds from the day this landed, at one, with no number typed anywhere — see
["An epic's own limit"](#an-epics-own-limit). A renamed board seeds neither.

**Which column belongs to whom is not a field.** It is derived: a column an
agent may leave is a column some [playbook](#playbooks) names as its `from`, for
that issue's type. A missing playbook row is how a column becomes the
operator's, and it is also how an epic in Breakdown can stay the operator's
while a story in Breakdown is the agent's, because a playbook names types. A
second "whose column is this" flag would be free to disagree with the first, and
a flag the loop could read is one step from a flag the loop could set.

**The loop reads the matrix and holds no list of its own.** It once held both,
and the copy in the source was asked *first*, so an epic in Breakdown was
folded on its type while the row the operator had written for exactly that move
sat unread. Two statements of which types a move applies to is one too many,
and the one that goes is the one nobody can edit.

The order is the point. `Backlog` is the gate between "somebody wrote a
paragraph" and "a pull request is open", and `In Review` is the gate before
shipped — which is what makes "never move a ticket to a terminal status"
something the board expresses rather than something a prompt has to ask for.

Three migrations built it, and the last one is the one to read before changing
any of it. `Init` seeded the first four columns; `Playbooks` measured `review`
into place immediately left of the first terminal column; `TheFlow` renamed the
five for people, added `Breakdown` and `Backlog`, and moved the specifying
playbooks onto the transition they now describe.

`TheFlow` touches **only a board that is still exactly the one those two
migrations shipped** — the five columns, in their order, with their terminal
flags. Anything else and it does nothing at all: no rename, no insert, no
repoint. That is a harder guard than the measurement `Playbooks` used, and
deliberately: one column relative to the first terminal one is a question a
board can answer, but `Breakdown` means nothing except "between Draft and
Backlog", and there is no measurement that finds those two on a board somebody
arranged by hand. A board whose columns no longer alternate is worse than one
that was left alone, because the loop would run, and it would run straight
through the gate that was supposed to stop it.

### Issue

`EfHatchIssue` — `ProjectId`, `Number`, `Type`, `Title`, `Description`,
`StatusId`, `ParentId`, `Rank`, `ReadyAt`/`ReadyAtHasTime`,
`DueAt`/`DueAtHasTime`, `PullRequestUrl`, `PullRequestMergeState`,
`PullRequestReviewDecision`, `PullRequestHeadSha`, `PullRequestReadAt`,
`ClosedAt`, `ModelOverride`, `EffortOverride`, `AssigneePersonId`/`AssigneeApiKeyId`,
`CreatedBy`, `CreatedAt`, `UpdatedAt`.

The display key `AER-12` is **computed** (`Project.Key + "-" + Number`) and
never stored, so there is exactly one fact about a key anywhere and no chance of
two copies disagreeing after a rename.

Types are `epic | story | task | bug`. Parentage is optional and validated
server-side: the parent must exist, be in the same project, form no cycle, and
be a legal type — epic→epic, story→epic, task→story, bug or epic, bug→epic or story.

`Description` is markdown, stored exactly as typed and rendered by the client
(`marked` + `dompurify`, the pair the docs app already bundles). What the
database holds is what somebody wrote, and changing renderer is a frontend
change.

**The two dates are a pair with different jobs.** `ReadyAt` is a *gate*: an
issue whose ready date has not arrived is folded off the board, which is what
lets a ticket be filed the moment it is thought of rather than the moment it can
be started — buy a certificate in September and the renewal appears next August
on its own. `DueAt` is a *deadline*, drawn as a chip that warms from three days
out. Both are optional, and both are written on the wire as one string per
field: either a date (`2027-08-15`) or an instant (`2027-09-01T17:00:00Z`).

The `…HasTime` bool is what keeps those two apart, and it is load-bearing rather
than decorative. A bare date has to be pinned to *some* midnight to be stored,
and a reader west of UTC would otherwise draw the day before. So a bare date is
read in UTC, where its components are the ones that were typed, and an instant
is read in the caller's zone, where the hour somebody meant is the hour they
meant. [`IssueMoment.cs`](../src/Hatch.Api/Modules/Hatch/IssueMoment.cs) holds
the server half of that contract and `lib/schedule.ts` the browser's.

Only formal validity is checked. A past due date is accepted without comment —
half of what a tracker is for is recording that something was due last Tuesday,
and a form that argues about it is a form people stop telling the truth to — and
a ready date after a due date is a mix-up worth seeing on the card rather than
one worth a `400`.

**`PullRequestUrl` is where the work is being reviewed**, and it is one URL
rather than a list on purpose: the field answers “where is this being reviewed
*now*”, and “what has it been” is already answered by the event log, which
keeps every value the column has ever held. A list beside that would be a second
history, and a worse one.

The only rule about the value is that it is an absolute `http` or `https`
address — anything else is refused with a sentence, because a field whose only
job is to be clicked should not hold something that does not open. Nothing
parses the host: a self-hosted forge on a private address is a pull request like
any other, and a column that only accepted one company's would be a fact about
exactly one installation. The field is still never parsed, and **the build is
not read from it**: the runner asks `gh` about the checkout's repository, at the
board's own identity for it (see [checking the branches in
review](#checking-the-branches-in-review)), so a pull request nobody recorded
still has its build read.

**The four fields after it are what the forge last said of that pull request.**
`PullRequestMergeState` is `clean`, `blocked`, `behind`, `dirty`, `unstable`,
`draft` or `unknown`; `PullRequestReviewDecision` is `none`, `approved`,
`changes-requested` or `review-required`; `PullRequestHeadSha` is the commit
that was read, and `PullRequestReadAt` is when. All four are nullable, and null
means *not read* — which is not `unknown` or `none`, both of which are things
the forge can say. Only `PUT /api/hatch/issues/{key}/pull-request-state` writes
them, and when the url changes they are cleared together with
`PullRequestClosedAt`, since a state read of one pull request says nothing of
another. They are stored and drawn, and `MergeWork.Judge` reads
the merge state and the head sha - see [the merge column's judge list](#the-approval-column) - and
nothing else on the server does: no gate or move reads them, and the review
decision, the read-at time and the url are still read by nothing. The issue page draws a chip beside the pull
request's link saying what the forge calls it, and the review decision beside
that where there is one; nothing is drawn while the state is unread or
`unknown`.

**`ClosedAt` is when the issue last entered a terminal column**, null while it
stands in any other — and it is a column, not a computed read. Every write that
changes `StatusId` keeps it, through the one helper `Columns.ClosedAt`, and so
does the status table's own flip of a column's `IsTerminal`, which closes or
reopens every issue standing in it without any of them moving. A move between
two terminal columns keeps the earlier instant: an issue is closed once, not
re-closed by being filed under a different ending. A move out of a terminal
column clears it. The event log stays the record of history, as it does for the
pull request above; this is the fast answer to “when did it close”, indexed with
`StatusId` so a board and a search can ask it without walking the trail. It is
not `PullRequestClosedAt`, which is when the forge closed a pull request. The
migration that added it backfilled every issue already in a terminal column from
its newest `status_changed` event, or from `UpdatedAt` where it had none.
Nothing reads it yet; the board's closed-recently view and the search's closed
filter will (HA-832, HA-833).

#### Assignee

**Who owns a ticket, said on the ticket.** An issue is assigned to a person, or
to an API key, or to nobody — two nullable columns, `AssigneePersonId` and
`AssigneeApiKeyId`, of which at most one is ever set. The controller clears one
when it writes the other, and a check constraint refuses a row wearing both, so
the constraint is a backstop and never the error a caller sees.

It is deliberately **not a claim**. A claim is a machine lease that comes and
goes with an increment, and Hatch does not have one for the reasons [one loop at
a time](#one-loop-at-a-time) gives. An assignee is a durable statement written
by a person, changing rarely and surviving every restart — which is why it is a
column here and a lease would not have been.

Neither column is a foreign key, and neither could be: Hatch owns its own schema
and its own migration history, and a constraint from a module into
`public.People` is the coupling `Modules/README.md` exists to prevent — the same
reason `CreatedBy` is a name. `ON DELETE SET NULL` would not have worked anyway,
because revoking a key sets a column and keeps the row
on purpose.

What does the work instead is **one predicate every reader applies**: an
assignee resolves only to a *live* identity — a person row that still exists, or
a key that exists and is not revoked — and an id that does not resolve reads as
**unassigned**. In the issue payload, on the card, in the board filter and at
the dispatcher, at the same instant. Nothing sweeps, nothing is cleaned up, and
nothing can be half-migrated: a predicate cannot fail to run, which is the same
argument [one loop at a time](#one-loop-at-a-time) makes for a lock whose owner
is gone being cleared rather than honoured. The rule lives in one place,
`Services/Auth/ActorDirectory.cs`, so that the day a second module wants an
owner there is one thing to ask.

The trade is stated rather than papered over: an issue assigned to a key that is
later revoked reads as unassigned without announcing it. The `assignee_changed`
event still names who it was — both sides carry a name beside the id, for the
reason `CreatedBy` is a name — so the trail answers "whose was this in March".

Setting one is closed to an API key: under the loop's `people only` rule an
assignee is a dispatch gate, so a key that could write one could hand itself
work somebody had reserved. See [The one edge that is deliberately
cut](#the-one-edge-that-is-deliberately-cut), and [what makes an issue
actionable](#what-makes-an-issue-actionable) for what a name on a ticket does to
a pass.

**`ModelOverride` and `EffortOverride` are what this ticket costs**, and null on
every issue until somebody says otherwise. A playbook prices a *transition*,
which is the right unit almost always; what it cannot say is that *this* story
is the hard one. Where one is set it beats every playbook that could speak for
the issue — all of them, not one transition's worth — and where it is null the
playbook decides exactly as it did before. The two are independent: an issue may
carry a model and no effort, an effort and no model, both, or neither.

They reach the issue and nothing beneath it. An epic set to `opus` does not
spend `opus` on its stories; a task that needs the big model says so itself, and
the alternative is one expensive decision made at the top of a tree and
inherited by work nobody weighed.

The values are a playbook's own values — the four aliases or a pinned
id for a model, the five efforts — validated by the playbook's own
rule and refused in the playbook's own sentence, so what an issue may be set to
and what a playbook may be set to cannot drift apart. Setting one is closed to
an API key; see [The one edge that is deliberately cut](#the-one-edge-that-is-deliberately-cut).

#### Expedite

**An issue sits at one of six levels, and emergency is the highest, paused
the lowest.** `Priority` is an int on the issue — paused, economy, low,
normal, expedited or emergency, in that order — set by a person, and honoured
by both halves of Hatch: the board floats an expedited or emergency card to
the top of its column and sinks a paused one to the bottom, and the dispatcher
considers every emergency candidate before every expedited candidate before
anything else, considers a low candidate only after every normal one,
considers an economy candidate only when an unattended pass finds the
account it would spend has a reserve of usage to spare, and never considers a
paused one at all. `PUT
/api/hatch/issues/{key}/priority` sets the level by name; the older
`PUT .../expedite` stays as a two-level alias (`{ expedited: true }` sets
expedited, `false` sets normal), so a script or a browser tab written against
the two-level flag does not break. A person sets it from a picker: a pill on
the issue page and on the board's peek that opens onto every level in a band,
the issue's own level marked, and a press sets exactly that one — the same
shape the status picker offers for a column, pressed rather than dragged.

Two things it deliberately is not.

**It is a sort key, not a gate — for the level an issue sets on itself.** Every
existing fold still applies. An open question, an unmet dependency, a ready
date in the future, a live claim, a missing playbook, a person's name on the
ticket and a terminal column fold an expedited or emergency issue exactly as
they fold any other, with exactly the same sentence. Priority changes the
order candidates are *considered* in, and nothing else — so an emergency issue
that is blocked is still blocked, and the pass carries on past it. It is no
longer the whole truth about a tier *below* one with clear work, which [the
park](#what-makes-an-issue-actionable) turns into a soft gate of its own: a
tier is reached at all only once every tier above it has no clear candidate.

**Paused, economy and low are the three levels that are also gates —
differently.** Every other level only reorders what is already dispatchable.
Paused takes the issue out of dispatch entirely, folded by `Dispatch.Blocked`
with its own sentence — *a person set it aside, and nothing picks it up until
they set it back* — for `work/next`, `work/queue`, `work/{key}` and a hop
alike, right after a live claim and before anything else
(`Dispatch.cs:555-559`) — nothing about who is asking changes it. Economy and
low each take the issue out of dispatch only for an unattended pass —
`work/next` and `work/queue` — when the account the dispatch would spend is
not projected to reset with usage unspent; a person naming the issue by hand
with `work/{key}`, or a hop that would otherwise carry it, gets it regardless,
for either level alike (`Dispatch.cs`, right after the ready date and before
the assignee fold). The two gates read the same account, judged twice: economy
reads every window Hatch knows the length of and holds ten points in reserve,
while low reads the session window alone and holds nothing back — strictly
laxer, so every reading that clears economy's gate clears low's too. Nothing
else about any of the three levels changes: its assignee, its claim, its
questions and its dependencies stand exactly where they were.

**It is inherited, live, from the nearest ancestor that sets one.** An issue
is at the first level other than normal found on itself or on its ancestors,
nearest first; normal is not a stop on the way up, so a story under a paused
epic is paused unless the story sets its own level, and a task under that
story set to expedited keeps its own regardless of what the epic or the story
say above it. This reverses what this section used to argue — that a level
means something only on the issue it is set on, because every type in a
walkable column is dispatchable and a second subtree mechanism beside
`ancestorKey` would be two answers to one question. The dispatchability
argument still holds, but a level that reached nothing under it could not
actually pause or expedite a subtree: an epic marked paused with its stories
still running is not paused in any sense a person watching the board would
recognize. Inheritance answers that without a second mechanism — setting a
level on an epic is the one write, and every descendant that does not set its
own reads it from there.

**It is computed at read, never stored or copied.** `PriorityTree.Effective`
and `DependencyGate.Effective` (`Dispatch.cs`) walk the (id, parent, own
level) map up from the issue to the first ancestor whose own level is not
normal, and that walk is what the board, the dispatcher and `hatch show` read
as the issue's priority; the level actually stored on the row is a separate
field, and the ancestor it was read from — or nothing, when the level is the
issue's own — travels alongside it. This reverses emergency's original rule
— taken from the parent at filing, and at no other time, with the `created`
event naming the parent it was copied from — because a copied level is an
override the parent could never take back: setting an epic to emergency after
a child already exists would reach nothing already filed, and moving a child
out from under an emergency parent would leave it emergency forever, with no
write left to undo. Reading the level from the tree on every request instead
of copying it onto the row at birth is what lets reparenting change the
effective level at once, both ways, and what lets a person raise or lower an
epic's level and have every descendant that hasn't set its own answer
differently from the next read — not the next time it happens to be refiled.

On the board it is `(StatusId, Priority desc, Rank, Id)`, served that way
rather than sorted in the browser, so the board, the plan and the queue cannot
disagree about where a card sits — a card dropped above an emergency or
expedited one comes to rest below it, and a card dropped below a paused one
comes to rest above it, because the float and the sink both win over the
rank. In the dispatcher it is [six walks of the columns](#the-dispatcher)
rather than a sort of the finished rows — low walked fourth, economy walked
fifth and paused walked last. Paused is always folded there, so it never
changes which issue `next` or a hop picks, only that `queue` can report it;
low and economy are each gated rather than always folded, so unlike every
other walk, a row reached on either pass can still be skipped by its own pace
check for `next` and `queue` alike — a hop is exempt from both gates, the same
way it is exempt from needing a playbook.

Setting it is closed to an API key; see [The one edge that is deliberately
cut](#the-one-edge-that-is-deliberately-cut). It follows that there is no
`hatch expedite` or `hatch priority` verb — the CLI authenticates with a key,
so the terminal shows the level (`board`'s counts, `next`'s and `show`'s
suffix, and `queue`'s `!!`/`! `/`||` marker) and sets it nowhere.

`CreatedBy` is a **name**, not a foreign key to `People`. The audit trail has to
read the same after a person row is deleted, and an API key's name goes in this
column beside a human one — neither of which a `People` FK from a module schema
could express.

#### Pre-approval

**Three levels, not a flag — `none`, `express` and `preapproved`
(`PreApprovalLevels`), carried on an issue's own row
(`EfHatchIssue.PreApproval`, `Dtos.cs:663-682`) and read live as the nearest
level other than `none` found on the issue itself or on an ancestor, nearest
first.** `none` is not a stop on the way up, the way `normal` is not for
priority: a story under a preapproved epic reads preapproved unless it sets
a level of its own, and a task under that story set to `express` keeps its
own regardless of what the epic says above it. At `express` or above, an
issue standing in a column marked [`ExpressSkips`](#status) with no
unanswered question is carried one column right with no session — see [the
hop](#the-hop) — exactly as the Express flag it replaces did.

**It is derived at read, never stored or copied — the same reversal HA-178
made to priority's own copy-at-filing rule, and for the same reason.**
Express used to be taken from the parent once, at filing, and never again;
a copied flag is an override the parent can never take back, and a
pre-approval is of a subtree, including the stories a breakdown session
files under an epic overnight, long after the epic was marked and before
those stories existed to copy anything onto. Reading the level from the
tree on every request instead lets reparenting change the effective level
at once, both ways, and lets a person raise or lower an epic's level and
have every descendant that hasn't set its own answer differently from the
next read.

**Setting it asks one of two questions, depending which way the level
moves.** Setting `express` — or clearing it back to `none` — asks the same
question an ordinary move into `Columns.SendBack` would: whether the
caller may move an issue of this project into the column that feeds
implementation, through `IProjectAccess.CanMoveIntoAsync`, the seam
`IssuesController.ProtectedMoveRefusalAsync` (`:1127`) already asks of a
real move — nobody is refused where the board has no such column. Setting
`preapproved`, or lowering away from it, needs `IProjectAccess.CanApproveAsync`
instead, on either side of the change: setting it is the approve press
given in advance, and lowering from it undoes that act, so both need the
same owner-or-approver permission.

**`PUT .../express` stays, as the two-level alias it always was.** `{
express: true }` sets `express`, `{ express: false }` clears it to `none`,
through the same two guards above — nothing written against the old flag
breaks.

**Setting either level is closed to a key, and to the runner header where
the wall is off — the same cut as [expedite](#expedite) and [stories at
once](#stories-at-once), and for the same reason: a key that could set
either could carry its own ticket through the night unattended, or approve
it in advance.** `IssuePreApprovalController` carries no class-level scope,
so both routes refuse a program outright before the issue is even looked
up. It follows that there is no `hatch` verb that sets it — the CLI
authenticates with a key, so the terminal shows the level (`show`, `board`,
`next`, `queue`) and sets it nowhere.

Setting a board up so a pre-approved epic runs this way end to end, with
nobody pressing anything but the one drag [the hop](#the-hop) cannot make
for it, is [its own walkthrough](#a-night-mode-board).

#### Stories at once

**One epic's own ceiling on how many of its stories run at once.** `WipLimit`
is a nullable int on the issue, meaning something on an epic and nothing on
any other type; null reads as **one** — `EfHatchIssue.DefaultEpicWipLimit`,
the one constant that says so, aliasing `IssueWipLimitRequest.DefaultLimit` in
`Hatch.Contracts` so the CLI can read the same number without seeing this
entity. Holding stories to it — folding a story past the first N in
progress — is a separate mechanism, [an epic's own
limit](#an-epics-own-limit); this field is only the setting.

It is not the [WIP](#wip) section above, whatever the shared vocabulary.
*WIP* bounds how many issues of a *type* sit in progress across the whole
board at once, in at most two slices everybody shares. This field bounds how
many of *one epic's own children* run at once, and every epic has its own.
Nothing here counts toward a WIP slice's load — an epic's own limit adds no
issue and no slot to it. What *does* read this field is the rule beside it: an
epic's own limit is measured against the same counted rows the section-wide
slice already holds, just narrowed to this one epic's direct children, so the
two limits can both apply to the same move without either counting the
other's load twice.

Written only through `PATCH /api/hatch/issues/{key}/wip` with `{ limit }` as a
string — `""` clears it back to the default, a whole number of one or more
sets it, anything else is refused with a sentence, and anything but an epic
is refused with a sentence saying only an epic takes one — by a person and
never a key, the same cut as [expedite](#expedite) and
[express](#pre-approval): see [The one edge that is deliberately
cut](#the-one-edge-that-is-deliberately-cut). Each change writes
`wip_limit_changed` with `{ from, to }`. Reading is open, like everything else
a dispatch needs: an agent is entitled to know how many of its siblings it is
competing against, and it rides `IssueDto` like `ModelOverride` does.

It follows that there is no `hatch` verb that sets it, as none sets expedite,
express or a playbook — the CLI authenticates with a key, so `hatch show`
prints it on an epic (`stories at once: 3`, or `stories at once: 1 (default)`
where none is set) and sets it nowhere.

#### Issue numbering

The one concurrency-sensitive spot. Inside the create request: read the project,
take `NextIssueNumber`, increment it, and save the issue and the project in one
`SaveChanges`.

`NextIssueNumber` is a column rather than `MAX(Number) + 1` because a deleted
issue must not hand its number to the next one — `AER-12` in an old chat log
should be a dead link, never a *different* ticket.

`[ConcurrencyCheck]` on that column is what makes the mint safe without a lock
or a sequence: two concurrent creates both read 12, and the second
`SaveChanges` finds the row no longer holding what it read and throws
`DbUpdateConcurrencyException`. The create path catches that (and unique-index
violations) and retries up to five times. The unique index on
`(ProjectId, Number)` is the backstop underneath, so the worst case is a refused
request rather than two issues wearing the same key. No raw SQL, and testable
in memory.

#### Moving between projects

An issue's project can change too — `PATCH /api/hatch/issues/{key}` takes a
`projectId` alongside the fields above. The move mints the issue a number in
the target project through the exact mint just described, with the same
retry on a lost race; its old key goes dead, the same cost a project's own
key rename pays (see [Project](#project)), just paid by one issue instead of
every issue under it.

A parent and its child always share a project — `ResolveParentAsync` refuses
otherwise, on every write — so a move has to settle the tree. An ancestor
never moves *with* its descendant, only a descendant moves down, so the
moved issue's own parent is always left behind unless the same request also
re-parents it: `parentKey`, resolved against the *target* project rather
than the old one. `moveDescendants: true` — the default when the field is
absent — carries everything below the moved issue along with it, so the
subtree's own parentage survives inside it; `moveDescendants: false`
detaches its direct children instead, and they stay in the old project with
their own subtrees under them intact.

What refuses, before anything is written: an unknown target project (`400`);
the issue's own project, read as no opinion at all rather than a move
(nothing staged); a caller that is a key or a keyless runner (`403` — an
issue's project decides which repository it is dispatched to, the same
routing-power argument that keeps [binding a remote](#repository) closed to
a key); any issue in the moving set holding a live [claim](#claim) (`409`,
naming it).

Every issue that moves, the one named and each descendant carried with it,
gets a `project_changed` event, `{ from, to }` as display keys; the named
issue's also carries `descendants`, the count of what moved alongside it.

#### Ordering

`Rank` is a `long` with 1024-sized gaps. Bottom of a column is last rank + 1024
(or 0 in an empty one), between two cards is `(a + b) / 2`, and top is
first − 1024 (negatives are fine). When `a + 1 == b` there is no midpoint: the
whole column is rewritten to 0, 1024, 2048… in the same transaction and the card
is then placed. That is
[`RankService`](../src/Hatch.Api/Modules/Hatch/RankService.cs), with unit tests.

Lexorank strings were considered and rejected: string midpoint arithmetic has
sharp edge cases, and a column here holds tens of cards, not millions —
renumbering is one cheap `UPDATE`.

#### Rank computation

The client says *before this card* or *after this card*; **the server picks the
number**. This is the first instance of the rule that also governs the
dispatcher and the rollup: keep every client dumb, including Claude. A browser
and a shell script that each computed a rank would eventually disagree, and the
disagreement would show up as cards in the wrong order with no failing request
anywhere.

The browser's `lib/place.ts` is therefore about *which neighbours to name* —
which matters because the board filters, and a drop has to land next to the card
the operator can see rather than next to a hidden row at the same index.

Which column a drop lands in at all is a separate question, decided in
`lib/aim.ts`: the column under the pointer, always, rather than whichever
droppable happens to score best by the corner geometry dnd-kit reaches for by
default — an average of four corners on a full-height column loses to a
card one column over at the same height, which is usually the dragged card's
own original slot.

#### Closing a subtree

Moving a parent into a terminal column used to move only the parent, and that
was the single largest source of a board disagreeing with reality: the epic read
shipped and the plan meter went on counting eleven leaves waiting, because
closing them one at a time is the work nobody does.

So an issue that lands in a terminal column with open work under it — dropped
there on the board, pressed there on its own status bar, or pressed there from
the board's peek — is answered with a question about that work. It costs no
request: `GET /board` hands the browser
every issue with its `parentKey` and its column, so `lib/closeSubtree.ts` walks
the subtree locally, and the dialog is on screen in the same frame the card
lands. A reorder inside a terminal column is not a close and asks nothing, and
neither does an issue whose descendants are all already closed.

The dialog **names the keys**, and the cascade is one `POST /issues/bulk` naming
exactly those — not a `cascade: true` flag the server would expand at write
time. That is the rule `IssueBulkEditRequest.Keys` states in `Dtos.cs`, met a
second time: a request that re-ran the query server-side could act on a row
filed between the operator reading the list and pressing the button, and what
was listed is what the operator agreed to.

Four smaller decisions, each of which reads as arbitrary until it is said:

- A descendant that has **already stopped stays where it is** — terminal or
  deferred. It is already closed or already shelved, and stopping it again in a
  different flavour rewrites what happened to it. That cuts both ways: a task
  that shipped is not un-shipped by its epic being shelved, and a task somebody
  parked on purpose is not quietly marked done by its epic closing.
- A descendant the filter is hiding, or a ready date is folding away, **moves
  like any other**. The fold is a view, not a fact — work that cannot start
  until August is still work under this issue.
- The descendants land in **whichever terminal column the parent landed in**,
  rather than in some canonical closed one. There is no such concept in the
  model, and a subtree abandoned into a second terminal column should read as
  abandoned.
- **The parent's move commits either way.** The question is about the
  descendants, so dismissing it means *leave them*, never *undo that* — which is
  also what lets the drag stay optimistic, with no card springing back out of a
  column it was deliberately dropped in.

The drop's chicklet can take an accepted cascade back too: see
[taking a drop back](#taking-a-drop-back).

#### Taking a drop back

A card dropped into another column on the board is confirmed in the bottom-left
corner, in the same chicklet a filing raises: the key (a link that opens the
issue in a new tab), the title, *from → to*, and an **Undo**. Move chicklets
stack with filed ones, newest at the bottom, every one the same width. Each
leaves after the lifetime set on the Settings page — 15 seconds by default, or
*Never*, which keeps it until it is closed or *Dismiss all* is pressed — counted
from its own raise. The clock is held for the whole stack while the pointer or
keyboard focus is on it, while a dialog is open, and while the tab is hidden, so
a confirmation does not expire while the operator is looking at something else;
an Undo in flight is not counted down either, and a chicklet that has just
finished one starts a full lifetime again. The choice is remembered by the
browser, not the install. Once a chicklet has left, ⌘Z / Ctrl+Z no longer
reaches its move. They live in the tab, above `<Routes>`, so they survive a click
through to an issue, and Undo works from whichever page they are showing on.

**Only a change of column is a transition.** A reorder inside a column raises
nothing, as it writes no event, and a drop the server refuses raises nothing
either. The chicklet comes after the reload, so the board it sits over already
shows the move.

⌘Z on a Mac and Ctrl+Z elsewhere press Undo on the newest move chicklet that has
not been undone, and each further press takes the next one back. That shortcut
belongs to the **board** — a keystroke made while reading an issue should not
rearrange a board nobody is looking at — and it steps aside for a text field
(where it is the field's own undo), for an open dialog, and for a drag under way.
With nothing to undo it does nothing, and the browser keeps the key.

**An undo never overrules somebody else.** The loop moves cards all night and
the board can be up to 30 seconds behind it, so the request names the column
the card is expected to be in, `fromStatusId`, and the server answers a card
anywhere else with a `409` naming where it is and writes nothing. The chicklet then says so
and offers no Undo. Any other failure leaves the button, so it can be pressed
again.

Undoing an override is an ordinary move out, with no dialog and no second
event - the section only ever asks on the way in. Taking a card back *into* a
section that has since filled is refused the same way any other undo is: the
server's sentence on the chicklet, and no Undo. The speed bump is for moving a
card in on purpose, not for reversing one already out.

An undo is an ordinary move: it writes its own `status_changed` event, and the
history shows the drop and the undo both. The card goes back between the
neighbours it had — read by rank, over every card in the column, since that is
how the server places it.

**Undo takes back an accepted close offer too**, key for key, because a stack of
work moves as a unit or not at all: an undo that left five tasks in Done under
an epic now back in review would not be an undo. The chicklet says how many
issues under the card were closed. Only what the offer *moved* is on it — an
issue that was already closed stays where it is, and so does one the offer
failed to move — and a dialog answered with "leave them" leaves an ordinary
chicklet whose Undo moves only the card.

The parent's move is checked first and gates the rest: if the card is no longer
where it was dropped, nothing under it is touched either. Then each descendant
goes back, one request at a time, top to bottom within its column — the server
tries a request's `beforeKey` before its `afterKey`, so restoring top-down means
each card's `afterKey` is already home when it lands, and neighbours return in
their old order — to the column it held, each naming the column the offer put
it in. One that has moved since stays where it is, and the chicklet names it.

#### Going to an issue

`/` on the board opens a **console** over it: a prompt, and a list of the issues
what is typed could mean, updating on every keystroke. Enter, or a click on a
row, closes it and opens that issue's peek — the same one a click on the card
opens — and rings the card on the board, scrolling it into view and opening its
`+ N waiting` fold if it is in one. The ring stays while the peek is open and for
about two seconds after, and goes at once when another card is clicked or a drag
begins. The board is never filtered, reloaded or left.

The shortcut belongs to the **board**, and steps aside for the same things Undo
does, and a fourth: a text field (where `/` is a slash), an open dialog, a drag
under way, and ⌘, Ctrl or Alt held. Escape, a click outside, or focus leaving
closes it and gives focus back to where it was before `/`. While it is open, ⌘Z
does not take back a move: to the rest of the board it is a dialog.

**What matches, and in which order.** Digits alone list every issue whose number
begins with them, lowest first — `11` is 11, 110, 111 and never 211 — and on a
board with more than one project, by number first and project second, so every
project's issue 1 comes before any issue 10. Anything else is a prefix of the
key, compared on letters and digits alone and ignoring case, so `AER-3`,
`aer-3` and `aer3` agree. Then come the issues whose title contains every typed
word, in any order, by key. An issue is listed once, and at most 50 rows are
drawn; the status line says how many matched.

**Everything the board holds can be found**, including the issues the filter is
hiding and the ones in a deferred column, which the board does not draw. A row
whose card the filter hides says `[filtered]`. Taking one opens its peek and
marks nothing: the filter, the folds and the scroll stay as the operator left
them.

#### Deferring an issue

A deferred column ([Status](#status)) is reached from two places: the status
bar on the issue page, and the board's own peek, opened off a card's status
pill. Both draw the board's own columns and then, after a divider, the deferred
ones. The board itself cannot offer them as a drop target, because a drag can
overshoot by a lane and work must not be parked by accident; a press in either
list is deliberate in a way a drop is not, so both may offer the shelf that a
drop may not.

**Deferring cascades exactly as closing does**, through the same module and the
same dialog — `closeOffer` fires on any column where work stops, and the dialog
words itself from `column.isDeferred`: *Defer what is under AER-12?*, and the
press that takes the subtree with it is the one the dialog leads with. An epic
put on the shelf with eleven live tasks under it has not been shelved, it has
been hidden, and the work under it goes on being dispatched from a board nobody
can see the parent on. A stack of work is deferred as a unit or not at all.

Two things follow it off the board:

- **A deferred blocker does not clear a dependency.** Only work that lands does
  — a story built on a branch that was never written is the failure the gate
  exists to prevent. So deferring a ticket writes a comment on every issue
  waiting on it, saying which ticket was shelved and that the gate still holds
  (`Deferrals.NoteAsync`). Without that sentence the edge quietly becomes
  permanent and nothing anywhere says why the dependent stopped moving. Moving
  between two deferred columns says nothing a second time.
- **A deferred leaf leaves the rollup's totals** — not counted done, not counted
  outstanding. "12 of 20" is a promise about work somebody still intends to do,
  and a shelved ticket is neither half of it.

Nothing about a deferred issue is hidden from the API: `GET /board` sends every
column and every card, the issue is reachable by key, by search, and from its
parent's children, and moving it back onto a lane is one press. The board is a
view, and this is the one thing it does not draw.

### Dependency

`EfHatchIssueDependency` — `IssueId` (the one that waits), `DependsOnId` (the
one waited on), `CreatedBy`, `CreatedAt`. Unique on the pair, and indexed on
`DependsOnId` alone for the reverse direction, which the **Blocks** list and the
dispatcher both read.

**An edge is what serialises work — not shared parentage.** The board used to
guess: no unattended run started an issue while a sibling of it was awaiting
review, which was right about the epics whose stories touch the same files and
wrong about every epic whose stories are independent, and said nothing at all
about two issues that must land in order under different parents. The guess is
gone and the fact is written down. An epic whose five stories must land one
after another gets a chain of them, a linked list the loop walks in order; an
epic whose five stories are independent gets none, and the loop takes them in
whatever order the board puts them in.

**A dependency gates implementation, and nothing else.** The old rule folded an
issue past every move it could make; this one holds exactly one. An issue
waiting on another still goes through Breakdown, still lands in Backlog, and is
still analysed — everything left of the writing keeps moving, which is the
point. What it does not do is get picked up and written.

That column is **ticked, and measured only where nothing is**. The
implementation column is the one a person ticked *Code written here* on
([`IsImplementation`](#status)), so long as that column is neither deferred nor
terminal. Where nothing is ticked it is measured: the one whose own next move is
into the awaiting-review column, which is itself the column immediately left of
the first terminal one — exactly where the migration that added `review` placed
it. On a stock board that is `To Do` to `In Progress`, the one transition where
code gets written. The measurement holds only on that layout: a board with agent
columns between building and review (`Building, Rule Check, QA, Ready to Merge`)
would measure `QA`, and the tick is for that board. Neither is a name: a name
would be a rule that quietly stopped applying the day an operator renamed a
column, and the tick is held by the row, not by its name.

An issue **already in** the implementation column is never gated. The move into
review is not a dependency's to refuse, so work that started finishes rather
than stalling half-written.

The mirror gates the way out, not this rule: an issue with a child that is
still open is not carried out of the implementation column either, because its
children are the work — see [What makes an issue
actionable](#what-makes-an-issue-actionable).

**Satisfied means done.** An edge clears when the issue it names is in a
terminal column — merged, not merely up for review. Anything softer and story
two starts on top of story one's unmerged branch, which is the failure the whole
feature exists to prevent, and it is why the chain advances at a merge — the
operator's, on the forge, or the poll's, in the merges column — rather than at
an agent's move.

**The one exception: a shared branch.** Between two tasks under the same story
that [shares one pull request](#one-pull-request), an edge is satisfied by a
task that is *waiting on the story's pull request* — pushed to the story's
branch and standing in the review column or past it, with no pull request of its
own. Building on a sibling's unmerged commits is the point of a shared branch,
and the sibling can only reach a terminal column when the whole story does, so
holding the edge would hold the story for ever. The exception is exactly that
wide: an edge to a task under a different story, to a sibling still being
written, or to anything under a story that does not share, is as strict as it
ever was.

**A dependency an ancestor holds reaches everything below it.** A task under a
blocked story is blocked and every story under a blocked epic is blocked, which
is what lets "phase two after phase one" be said once at the top. The fold names
the *nearest* holder and stops: clearing that one is what the reader has to do
first, and the pass after it says what is behind it.

An issue may wait on any number of issues, **including issues in another
project** — a dependency is not containment, and the same-project rule that
governs a parent does not govern this. It may not wait on itself, on an issue
above or below it in the tree (a parent is not done until its work is, so an
edge either way round could never be satisfied), or on anything that already
waits on it directly or through a chain. Each is refused with the sentence
saying which, and nothing is written. Adding an edge that is already there
writes nothing and is not an error, as re-applying any edit here is not.

**Writing one is open to a key**, unlike a [playbook](#playbooks) or a per-issue
override. An edge is a statement about the work rather than about an agent's
budget, and a planning session that has just filed five stories is exactly who
should chain them. Both verbs — `POST` and `DELETE` on
`/api/hatch/issues/{key}/dependencies` — answer with the whole issue, and there
is no `GET`: both lists ride `IssueDto`, where the board, the page and a client
at a terminal need them anyway. Both directions land in the issue's history, on the issue that
waits and on it alone.

### One pull request

Whether a story's tasks share the story's branch and one pull request, instead
of one branch and one pull request each, merged one after another. The goal is a
story done with fewer obstacles: no merge needed between siblings.

It is answered at two levels. The **project** holds a default
(`EfHatchProject.OnePullRequest`, off), and a **story** may override it
(`EfHatchIssue.OnePullRequest`, a nullable bool: null inherits, true is on,
false is off). Only a story carries an override; any other type is refused.

The effective value is **derived at read and never copied**, the same way
[pre-approval](#pre-approval) is. `PriorityTree.EffectiveOnePr` walks to the nearest
story at or above an issue and answers with that story's override, else its
project's default; an issue with no story above it - an epic, or a task filed
straight under one - reads off, because there is no story whose pull request it
could share. Reparenting, or flipping either level, changes what everything
beneath reads on the next request with no write to the issues beneath.
`IssueDto` and `IssueCardDto` carry `onePr` (the effective value), `onePrOwn`
(this story's own override, null when it inherits) and `onePrFrom` (the story a
task read it from, null when it is its own, the project's, or absent).

**Who may set what.** The project default is a person's, and an owner's: it
changes how every story under the project is worked, and it rides the same
route as the landing policy (`PUT /projects/{id}/landing`, `{ onePullRequest }`).
A story's override is open to anyone or anything, an API key included - it decides
how a story is branched, not what the loop reaches for or which gates it may
pass unattended, so it is not the edge [priority and pre-approval](#the-one-edge-that-is-deliberately-cut)
are. **It is locked once work under the story has started** — any descendant
standing in the implementation column or to its right, a terminal one included
(a shelved one has not started) — and the route answers `409` naming the first.
Work begun on one branch cannot be moved onto the other, and a task begun on the
story's cannot be given a pull request of its own, so a change would leave work
that no longer agrees with its story. Setting what a story already holds is
never refused and writes nothing; a change writes a `one_pr_changed` event
carrying both sides as `on`, `off` or `inherit`. The project's default is not
locked, but it cannot move a story under work already begun: a story with no
setting of its own reads the default, so when the default changes
(`PUT /projects/{id}/landing`) every story in the project that has started work
and carries no setting is first given, as its own, the value it was reading —
one `one_pr_changed` event on each, `inherit` to that value. Stories with
nothing started keep following the default. "Started" is the same test the lock
uses, so a story fixed this way is locked from then on like any other.

**A task waiting on the story's pull request.** With the effective value on, a
task that has pushed to the story's branch moves to the review column and
*waits there with no pull request of its own*. Precisely: a task under a story
that shares, standing in the review column or any non-terminal, non-deferred
column to its right, with no pull request recorded on it
(`PriorityTree.WaitingOn`). For the story's purposes it is finished:

- **The next sibling is pulled.** `FamilyGate.SiblingInFlight` does not count it,
  so a `ParentPulls` column carries the next child while this one waits, instead
  of waiting for a merge that cannot come until every sibling is done.
- **Edges between siblings clear** — the exception under
  [Dependency](#dependency).
- **The story reaches its closeout.** The fold that holds a story out of review
  while its children are the work (`FamilyGate.OpenWorkChildren`) does not count
  it, so the story's `In Progress` → `In Review` increment runs, and that
  increment — not a task — opens the one pull request, titled with the story's
  key. `OpenChildren` itself still counts it, because it has not merged: that is
  what keeps the `Close` hop from closing a story over a pull request nobody
  opened.

And for the merge machinery it is invisible: `Dispatch.Blocked` folds it first,
with *"waiting on AER-5's pull request - …"*, ahead of the merge column's judge
and the review column's conflict and build checks, both of which read a branch
named for the task's own key and would find none; `GET /work/review` leaves it
out, so a runner's poll does not look for a branch that does not exist; the
merges column's train never elects it head; and the attention panel does not
count it as *in review without a pull request*, because having none is the
design. The conflict or build verdict that matters is the story's own, filed on
the story.

**Done means merged.** The waiting task ends in a terminal column only when the
story's pull request merges. On the merge train path `POST /work/{key}/merged`
on the story moves every task waiting on it into the same terminal column with
it — one `status_changed` event on each, carrying `merged: true` and
`with: <story key>`, in the same save as the story. A person's manual move of
the story into a terminal column gets the *Closing a subtree* dialog instead,
which lists every descendant that has not stopped, the waiting tasks among them.
Tasks that are deferred, terminal, or have a pull request of their own are left
where they are.

**One sibling at a time.** Two tasks under a story that shares push to one
branch, so they may not be worked together. `ClaimLineage` treats them as one
line: while either holds a live claim the other is folded — *"AER-6, sharing
this story's branch, is held…"* — and a take that raced is let go by the same
check as an ancestor's. A task with a pull request of its own is on its own
branch and is not held. Tasks under different stories stay parallel.

**The branch a dispatch is named for.** `WorkDto.BranchKey` and `BranchTitle`
carry the story's key and title for a task that shares, and the issue's own
otherwise; the server decides, because whether a story shares is a live,
inherited fact. A task that already has an open branch of its own on `origin`
is a fact about git that only a runner can see, so the client applies that on
top. `ReviewCheckDto` carries the same two fields; every row in that list is its
own branch's, since a waiting task is left out.

**Known edges.** A story whose pull request merges early leaves a later task —
a gap filed after the merge — with no branch to join and no pull request coming
to carry it. The board sees that the story is in a terminal column and no
longer counts the task as sharing it (no waiting, no sibling claim, its own
branch key); the runner, which can see git, does the same where every branch the
story has on origin is already in the trunk. The task is cut its own branch and
opens its own pull request, as it would with sharing off. A conflict or failing build on the shared branch holds every
sibling, which is the cost of sharing it. The setting is locked once work under the story has started, so
flipping it can neither strand a waiting task nor make a task in review read as
waiting; the project's default is the one door that is not locked. And the one-at-a-time claim rule is about the branch
and not the move, so it also holds a sibling back from a session that would
never push - the cost of not having to ask what a session will do.

At a terminal, `hatch one-pr <key> [on|off|inherit]` reads and sets it, and
`hatch show` prints the line wherever it is on or a story has been told one way
or the other.

### WIP

How full the [WIP section](#status) is right now — the read every later story
shares (`Wip.LoadAsync` in `Modules/Hatch/Wip.cs`) rather than counting for
itself. Not to be confused with [Stories at once](#stories-at-once): that one
bounds a single epic's own children, this one bounds a type across the whole
board.

**One section, any number of slices.** The section is one set of columns;
each slice is a `WipLimits` row, keyed by the issue types it counts, with its
own limit, its own load and its own claimed-inbound count. Hatch knows exactly
two slices, `story,bug` and `epic`, always both and always in that order — a
story or a bug counts toward the first, an epic toward the second, and a task
toward neither. A slice with no row still exists and still reports a real
load; it simply has no limit to gate or fold anything with.

**A slice's load** is every issue of its counted types sitting in a WIP
column, plus every issue of its counted types outside the section that holds a
live [claim](#claim) whose next column is itself a WIP column. Which columns
count is exactly what `IsWip` says — see [Status](#status) — narrowed the same
way: a deferred or terminal column never counts, whatever it is flagged.

An effectively [paused](#expedite) issue — its own level or inherited — is
never part of a slice's load, nor of an epic's own count under it
(`Wip.LoadAsync`, `PriorityTree.Effective`). A pause frees the slot it held,
and an un-pause can momentarily read a section over its limit until something
next leaves — the same *over* tint the band already draws for any other
overfull slice.

A line of the tree costs the section one slot, not one per counted issue on
it: where an ancestor and a descendant are both counted, the nearer one's
presence is what lets the other cross into a section that already holds it,
and the load counts the family once, wherever in it the root happens to
stand — inside the section, or outside it on a live claim heading in.

**The claimed-inbound half** exists because a claim is what stops a second
runner filling the same slot: an issue in a feeder column that a runner has
already taken is effectively on its way into the section, and letting it
through the gate while ignoring it in the count would let the section overfill
by exactly the number of runners working the feeder column at once. It counts
only while the claim is live — an unclaimed issue in a feeder column counts for
nothing — and it drops out of the load the instant the claim does, one second
past the TTL, same as if it had never been claimed.

`board.wip` is null only where no column is flagged at all. Once a column is
flagged, it is present with every slice's load, whether or not that slice has
a limit — a board that has never turned WIP on reads exactly as one that
predates it.

Read at `GET /api/hatch/board`'s `wip` block, and printed as one line per
limited slice by `hatch board`.

On the board itself the WIP columns read as one zone. A band spans each run of
adjacent WIP columns - a section split by a column outside it draws two bands,
both showing the same meter. It is drawn only where at least one slice has a
limit, and reads one part per limited slice, joined by ` · ` - `WIP 3 of 5`
for the first, and every part after it prefixed with its own types, pluralised,
so an epic-only band reads `WIP epics 1 of 2`. Each part carries its own
claimed-inbound aside, once non-zero - `(1 on the way)`. Tightness is one of
four per slice, each its own tint: *room* (two or more left), *tight* (one
left), *full* (at the limit, said in the band as well as tinted, so the two do
not depend on colour alone to be told apart), and *over* (past it) - the band
and the lit columns are tinted by whichever limited slice is tightest.
Picking up a card from outside the section previews the slice covering its
type alone - a story or a bug previews the first slice, an epic the second,
and a task neither - for as long as the drag lasts, and lights every column of
the section as one target. The count never follows the filter - it is
`board.wip`'s own count, drawn whether or not the card counted is on screen -
and the same holds against the server's own done fold (see the `/board` row
above): the WIP count is unaffected either way, because `Wip.LoadAsync` reads
the database, not the board's own filtered and folded array.
Where no slice has a limit there is no band at all: nothing here asks about a
drop into a full section or refuses one, either - that is the server's `409`,
and the paragraph below is what a full section does with it.

**The refusal.** A move whose `from` is outside the section and whose `to` is
inside is refused with `409` while the slice covering the card's type is at or
over its limit - a move within the section or out of it, a type no slice
counts, a slice with no limit, and an issue already counted (inside the
section, or outside it on a live claim headed in) are never refused. The body
is `{ error, load, limit }` (`WipRefusalDto`), `error` the sentence naming the
slice's own types and `load` that slice's count *before* the move - the same
`409` from `POST /issues/{key}/move`, `PATCH /issues/{key}` and
`POST /issues/bulk` alike, and every door (the board, the issue page, the CLI,
the bulk page) prints the same sentence. In a batch, an issue counts only
against its own slice: one epic admitted does not use up a story's slot, and
the reverse. A person may say *move anyway* (`wipOverride: true` on a move or
a patch; bulk takes no override), which writes `wip_overridden` beside
`status_changed` - see [Issue event](#issue-event). Overriding is a person's
call and not an agent's: a key or a keyless runner sending `wipOverride: true`
is refused with `403`, whatever the load and even where no limit is set. On
the board and on the issue page, the browser never sends that flag on its own
first guess: the `409` raises a dialog quoting the sentence above and asking
*move anyway?*, and only a press of *Move anyway* resends the same request
with `wipOverride: true` - the event that follows names who pressed it.

#### An epic's own limit

**Held beside the section-wide slice above, not instead of it.** After the
section-wide check has had its say, a story or a bug moving from outside the
section to inside it, whose parent is an epic, is further held to that epic's
own [`WipLimit`](#stories-at-once) - null reading as **one**, the same
default *Stories at once* reads. A task, an epic moving itself, a move within
the section or out of it, and an issue the section already counts are never
held by this either, for the same reasons the section-wide slice never holds
them.

**The load under an epic** is its direct stories and bugs the section-wide
slice already counts - inside the section, or outside it on a live claim
whose next column is inside - narrowed to this one epic's own children.
Direct children only: a bug under a story under the epic is the story's line,
not the epic's, exactly as [a line of the tree costs the section one
slot](#wip) above. Where the epic itself stands does not matter - an epic in
*Backlog*, inside the section, or in *Done* holds its direct stories and bugs
to its limit just the same; pulling the epic itself into the section is a
separate question. **On by default**: the section exists wherever a column is
flagged, so an epic with no `WipLimit` set holds its stories to one from the
moment the section does, whether or not the section-wide slice has a limit
row of its own.

**The section's sentence wins where both are full.** It is the more general
fact, and both the `409` and the fold say it first - a caller holding neither
limit never sees the epic's sentence at all. Where only the epic is full, the
`409` carries `{ error, load, limit }` exactly as the section-wide refusal
does, except the numbers and the sentence are the epic's own: *`{epic} is at
its limit - {load} of {limit} of its stories and bugs {is/are} in the WIP
section`*. A move is judged by the parent the card is left with in the same
request - a `PATCH` that moves a story in and reparents it under a full epic
in one call is refused by the epic it is left under, and one that moves a
story in while clearing its parent in the same call is judged with none at
all.

**An override past both limits writes two `wip_overridden` events, not one** -
the section's first, as `{ limit, load, to }`, then the epic's, as
`{ limit, load, to, epic }` naming it - see [Issue event](#issue-event). One
`wipOverride: true` steps over both; the dialog shown is still the section's,
because the refusal that raised it was. A story an epic refuses spends no
section slot at all: within one bulk batch, the section's free slot a
refused story would have taken is still there for the next one.

### Claim

Seven nullable columns on the issue row — `ClaimToken`, `ClaimedBy`,
`ClaimRunner`, `ClaimedAt`, `ClaimHeartbeatAt`, `ClaimChatter`, `ClaimChatterAt`
— and a lease is all of them set or all of them null. An eighth,
`ClaimPlaybookId`, rides beside them but is not part of that rule: it is null
on a lease taken with no playbook named, and a lease with a token and no
playbook is ordinary.

**`ClaimPlaybookId` says which playbook the lease is working under.** It is set
by the take (`playbookId` on `POST …/claim`) and by a heartbeat (`playbookId` on
`POST …/claim/heartbeat`, where absent leaves it alone and nothing can clear it
back to null), and it is cleared with the rest on every release, by the holder
or by the operator; on a lease that merely expired it stays, like the rest,
until the next take overwrites it. The runner supplies it and the session never
does. It exists so the server can tell a [check](#check)'s lease from any other:
a key's move is refused while it is a check's, and a verdict is accepted only
under it.

**A claim is a lease held by a running dispatcher**: taken before an increment,
refreshed while it runs, released after it, and expiring on its own when the
runner dies. It is what makes two checkouts against one Hatch safe, which is the
whole of why it exists. Columns rather than a table because a claim is a fact
about the issue with at most one of it at a time, and because taking one is then
a single conditional `UPDATE` against a row the dispatcher is already reading.

**A claim and an assignee are different nouns.** An assignee is a person's
intent, answered in days and written by a person. A claim is a process's grip,
answered in minutes and written by a machine, and it is gone the moment the
machine is. Putting them in one field would mean a crashed runner erasing
somebody's plan for the week.

**Expiry is lazy, and there is no sweeper.** A claim is dead when its heartbeat
is older than the TTL, which is a predicate every reader evaluates rather than a
state anything writes: nothing has to run for a dead runner's ticket to become
claimable again, and there is no job to tune, schedule or notice has stopped.
The columns stay as they are until somebody takes the lease over, so the trail
of who last held it outlives the lease. The TTL is `Hatch:ClaimTtlSeconds`, five
minutes by default, and it is **returned to the client with the claim** — on
every read a claim rides, not only on the take — rather than configured on both
sides, because the server is what honours it and so is what says what it is. A
client holding a heartbeat four minutes old cannot otherwise tell a runner about
to go from one that is fine.

**A claim that has gone quiet is not live either, however recently it was
heartbeat.** *Quiet* here is a hard rule, not the card's amber dot below: the
later of `ClaimedAt` and `ClaimChatterAt` — the last time anybody said
anything, or the take itself where nobody ever has — is judged against
`Hatch:StallLapseMinutes`, five minutes by default.
A `--quiet` session sends no chatter at all, so its claim is measured from when
it was taken. Every reader treats a quiet claim exactly as it treats an expired
one — the dispatcher, the board, the issue page, the Runners page, and a take —
because `IssueClaims.IsLive` is the one place both conditions are asked, and
nothing downstream repeats the arithmetic. A heartbeat against a quiet claim is
refused `409` with `this claim has gone quiet for 5 minutes`, beside the three
reasons a heartbeat could already be refused, and checked before "expired"
where a claim happens to qualify as both. `Hatch:StallLapseMinutes = 0` turns
this off entirely — every claim is then judged on its TTL alone — and a
negative value falls back to the default, the guard `Hatch:ClaimTtlSeconds`
already has. The window rides back with the take as `stallLapseSeconds`, in
seconds, `0` when lapsing is off — the server is what honours it, so the
server is what says what it is, the same argument `TtlSeconds` makes for
itself.

**A marked issue resumes itself after `Hatch:StallResumeMinutes`**, fifteen
minutes by default — judged against `StalledAt`, and a separate window from
the claim-quiet one above on purpose (see `HatchOptions.StallResumeMinutes`'s
own remarks: raising the claim-quiet window to resume-length would make a
dead runner's claim hold three times as long, which isn't what either
setting is for). `0` turns unattended resuming off entirely — a marked ticket
then waits for a person regardless of how long it has sat — and a negative
value falls back to the default. `Hatch:StallResumeLimit`, three by default,
caps how many such resumes a ticket gets in a row before it holds for a
person instead — see `letGo`, below.

**A release may say how the increment ended.** `DELETE …/claim?token=…` takes
an optional `outcome`, `dropped`, `worked` or `preempted` — absent is accepted
exactly as it always has been, which covers the pick's own release, a restart,
and an older CLI, and a value that is none of the three is `400`. It is read
only where a token is given: the operator's tokenless clobber names no
increment to have an outcome. The `claim_released` event carries it, and it is
what `letGo` counts (see [Comment, question and answer](#comment-question-and-answer))
— except `preempted`, which it skips over, neither counted nor resetting the
count: the board itself ordered that put-down, not a choice the increment
made, and the runner's own account of what it pushed lands as its own comment,
not as a dropped increment.

**A heartbeat may answer that this runner has been preempted.** Ordinarily a
heartbeat that refreshed the lease answers `204`. Where the board has chosen
this runner's issue to make room for an emergency one — see
[The dispatcher](#the-dispatcher), "Preemption" — it answers `200` instead,
naming the emergency issue's key and title, and writes a `claim_preempted`
event on the issue being asked to stand down and a comment on the emergency
issue naming the runner and what it put down. An older `hatch` reads only
whether the heartbeat succeeded (`Claim.cs`'s `answer.Ok`, true of any `2xx`)
and is unaffected by the richer answer; only a CLI built for HA-169 acts on it.

**The token is a fencing token, and it is a capability.** Every heartbeat and
every release presents it, and a write whose token is not the one on the row is
refused — which is what stops a runner whose lease expired mid-increment from
clearing the lease that replaced it. It is handed out exactly once, in the
response to the claim that minted it, and **it is on no read anywhere**: a board
read that carried it would let anybody holding a board read steal or refresh
somebody else's lease. What a client gets instead is who holds it, from where,
when it was taken, when it was last heard from and what it last said — and
nothing at all where the claim has expired, because the arithmetic is the
server's and a card drawing a holder that stopped existing four hours ago is
worse than a card drawing nothing.

**A claim holds its line of the tree.** While anybody holds a live claim on an
issue, nobody else may be dispatched at, or claim, any issue above it or below
it, at any depth: a parent's session builds what its children describe, so two
sessions on one line produce duplicate work and conflicting pull requests.
*Level* here means ancestor or descendant — not depth in the tree and not issue
type — so siblings and cousins are unaffected and run in parallel. The
dispatcher folds a relative with the same sentence a claim on the issue itself
prints, naming the relative (`Nathan is working AER-12, above this, from
host:/path/to/checkout, last heard from 2 minutes ago`), and `POST …/claim`
answers `409` with it. A relative's claim past its TTL, or gone quiet, folds
nothing, exactly as an issue's own expired or quiet claim does.

The take cannot fence this in its `WHERE`: a conditional `UPDATE` fences one row
and a line of the tree is many, so a take on a parent and a take on its child
from one pre-claim state each write a row of their own and neither can see the
other. So **a take is made first and looked at after**: once its `UPDATE` lands
it re-reads the line, and if a relative holds a live claim it lets go of the
lease it just took and answers `409`, writing no event. Whichever take looks
second sees the first. The worst case is that both look before either lets go
and both release; the next pass tries again. Two runners never both keep a
claim.

**A claim is drawn where the work is looked at.** A claimed card carries a dot
in its head row, green while the holder is being heard from and amber once it
has gone quiet, with who is working it and how long since a word on the hover.
The issue page draws a **Claim** section saying the same things at length,
leading with the runner rather than who it runs for — "Jeff Winger is working
AER-12", with ", for Britta" appended only when the two differ — plus when it
was taken, when it was last heard from, and the last line the runner printed
with how long ago it printed it. *Quiet* is half the lease without a word — a
fraction rather than a count of minutes, so changing `Hatch:ClaimTtlSeconds`
moves the warning with it — and it is not expiry: a claim past its lease is
drawn as no claim at all, on the card and on the page, because the server has
already stopped sending one. Neither polls, with one bounded exception: the
issue page re-reads its comments and claim every five seconds **while a message
to the agent is waiting under a live claim** and the tab is on screen, and stops
the moment nothing is waiting, the claim ends, or the tab is hidden. The state
of that message changes on its own, and the person who just pressed Send is
watching for the change — the reason `useRunners` polls, and no other reason
extends it. Everything else goes stale with the read it came from and comes back
current on the next one.

**The Claim section talks to the session.** A box there, *Tell the agent*, posts a
comment of kind `message`, and each message under it says in words where it
stands: *not read yet* while a claim is live, *read 2 minutes ago by
`<runner>`*, or, when nobody is working the issue, *not read — nobody is working
AER-12; the next session on it is told*. The same words sit on the message in
the Comments thread. See [Comment, question and answer](#comment-question-and-answer)
for what *read* means, and [What a runner does](#what-a-runner-does-in-order)
for how a message reaches a session.

**Clearing a claim stops the runner, but not at once.** The operator's
`DELETE .../claim` takes the lease off the row straight away, so the issue is
claimable again from that instant — but the session that held it does not know
yet. It finds out at its next heartbeat, refused with "this claim was cleared",
and that is what ends it: the heartbeat calls `OnLost`, and the runner cancels
the session rather than let it keep pushing against a claim it no longer holds.
That is a beat away, not gone in the same instant — within a minute by default —
so a second session claimed in the gap may briefly be writing to the ticket
alongside the first. It is the sentence the confirmation on the issue page leads
with, and the reason to press it is a runner known to be gone or known to be
wrong.

**A take over a lapsed lease says so on the trail, before it says who holds it
now.** When a take finds a token still on the row whose lease is no longer
live — silence past the TTL, or any other reason the row stopped being kept
up — it writes a `claim_lapsed` event naming who stopped answering and when
they were last heard from, immediately before the `claim_taken` event for the
runner taking it over. A take against a row nothing held, or a row a release
left clean, writes no `claim_lapsed` — the event is for a takeover, not for
every take.

**The guarantee lives in a predicate on the write.** Reading the row first is
unavoidable — a refusal has to name who holds it, and that sentence can only be
written from the row — but nothing is decided by the read. Each write is one
conditional `UPDATE`: a take matches only a row nothing live holds, a refresh
and a release only a row still carrying the caller's own token. Two runners that
both looked at the same unclaimed row resolve to one claim, and the loser wrote
nothing rather than overwriting a lease it lost.

**What the claim fences is the claim, not the writes.** Two runners are never
*dispatched* at one ticket, which is the failure it exists to prevent. A runner
whose lease expired mid-increment can still push its branch and still move the
ticket, because moves are not gated on a token — and gating them would break
every move a person makes, which is not a trade worth taking for a window that
opens only when a runner stops answering for five minutes and then comes back.
It is named here rather than papered over.

Taking, releasing and clearing each write an event naming the actor, so the
trail says who took a ticket and who let it go. An ordinary heartbeat writes
none: it is a meter reading rather than a decision, and the trail would
otherwise be a row a minute for every running increment. A preempted one is
the exception, because it is the board's own decision and not a meter
reading — see "A heartbeat may answer that this runner has been preempted"
above.

**Its tests need a real Postgres**, and `make test-api-db` is how they get one —
`make test-api` runs the same suite and skips them, saying so. That is not
fastidiousness: the guarantee here is a `WHERE` clause, EF's in-memory provider
refuses a conditional `UPDATE` outright, and the SQLite provider cannot
translate the `DateTimeOffset` comparison the expiry rule is. Either substitute
would be green while the SQL was wrong, which is the one failure mode the lane
exists to prevent — the same argument the trading silo's queue makes about its
own. CI runs it on every pull request.

### Merge check

`EfHatchMergeCheck` — `IssueId`, `Remote`, `Canonical`, `Trunk`, `TrunkSha`,
`Branch?`, `BranchSha?`, `Verdict`, `Files`, `HoldsTrunk?`, `PlaybookFilePath?`,
`CheckedAt`, `Runner`, `CheckedBy`. What a runner found when it merged an
issue's branch against the trunk, and the two refs it was looking at when it
did. `Verdict` is one of four:

| Verdict | Means |
|---|---|
| `clean` | The branch merges with the trunk. Nothing for an agent to do |
| `conflicted` | It does not, in the paths `Files` names |
| `none` | No unmerged branch on origin is named for the issue |
| `ambiguous` | Two or more are, and nothing here guesses which is the issue's |

`none` and `ambiguous` carry no branch and no sha, on purpose: there is no one
branch to fingerprint, and the runner's poll keeps what it saw for those two in
memory rather than asking the board for something that is not there.

`HoldsTrunk` says whether the branch already had the trunk's tip - `true` when
`git merge-base --is-ancestor` said so outright, `false` when it took a
`merge-tree` merge to get there or when it is `conflicted`. `none` and
`ambiguous` name no branch and so carry no answer, and a board that predates
this field reads it as `null` too - *not said*, not *false* - and the poll
rechecks a stored `clean` verdict once to fill it in even where nothing else
about the branch has moved.

`PlaybookFilePath` names `.hatch/playbooks.yaml` when the branch's diff
against the trunk touches it, read with `git diff --name-only` between the
two full shas `Check` already holds - no worktree touched, no second fetch.
It is null for `none` and `ambiguous`, which name no branch; null when the
diff does not touch it; and null for a repository no project reads as its
own, repo-sourced primary repository, since nothing asked the runner to look
at all - the review read names the path, in `WorkRepositoryDto.PlaybookFilePath`,
so the runner never spells `.hatch/playbooks.yaml` itself.

**One verdict per issue per repository**, unique on `(IssueId, Canonical)`, where
`Canonical` is [`RemoteIdentity.Canonical`](#repository) of the remote as the
runner spelled it. A project may bind several repositories and a branch is
entered in every checkout that has one, so a single verdict on the issue would
let a clean repository overwrite a conflicted one — and two runners that spell
one repository two ways still write one row. An issue conflicts if any of its
verdicts does. A second write for the same pair replaces the first; a verdict
for a project that binds nothing is keyed on the remote the runner spelled, like
any other.

`PUT /api/hatch/issues/{key}/merge-check` writes it, and a key may: the caller is
a runner, and a verdict only a person could enter would be one nobody entered.
It is refused, in a sentence, for a remote that does not canonicalise, a verdict
that is not one of the four, `conflicted` with no files, and `clean` or
`conflicted` with no branch sha. An unknown key is a `404`. **The issue's column
is not checked**: a verdict taken at the end of an implementation increment
arrives before anybody has moved the ticket, and the rule that only an issue in
review is *dispatched* on one is the dispatcher's, not the write's.

**A verdict that changes the stored one writes a `merge_check_changed` event; a
repeat writes none.** Changed means a different verdict, or different files
(sorted, so the same conflict listed in another order is not a change), and the
first verdict for a repository counts as a change from nothing. The payload is
`{ remote, from, to }`, each side `{ verdict, files }`, the remote canonical.
A repeat still refreshes the shas, the time and the runner — the row says how
recently somebody looked — but the trail is for when a branch started and
stopped conflicting, and a poll that looks every interval would otherwise write
a row an interval for as long as nothing changed. It is the same call a
[claim](#claim)'s heartbeat makes.

Every `IssueDto` carries its verdicts, ordered by canonical remote, read in one
batched query for a list of issues.

### Landing

`EfHatchLanding` — `IssueId`, `Remote`, `Canonical`, `Trunk`, `Sha`,
`ContainedBy?`, `RecordedAt`, `CheckedAt?`, `RecordedBy`. What a repository
holds for an issue's branch: the sha a session or a runner last recorded
against it, the trunk ref it is landing on, and — once a poll has confirmed
it — the sha of whatever trunk commit contains it. `ContainedBy` is left null
until that confirmation, and `CheckedAt` moves only with it, never with a
session's own plain report of its sha.

**One landing per issue per repository**, unique on `(IssueId, Canonical)`,
where `Canonical` is [`RemoteIdentity.Canonical`](#repository) of the remote
as the caller spelled it, for the reason the [merge check](#merge-check) is: a
project may bind several repositories, and each gets its own row rather than
one overwriting another.

`PUT /api/hatch/issues/{key}/landing` writes it, and a key may, for the reason
a key may write a merge check. It is refused, in a sentence, for a remote that
does not canonicalise, an empty or non-hex sha, and `ContainedBy` given
without a `Trunk` — a landing that names what contains it must also name its
trunk. An unknown key is a `404`. **The issue's column is not checked**, for
the reason the merge check's is not: a landing is a fact about a repository
and a sha, true or not regardless of where the ticket sits.

**A write that changes the stored sha or what contains it writes a
`landing_changed` event; a repeat writes none.** The first landing for a
repository counts as a change from nothing. The payload is `{ remote, from,
to }`, each side `{ sha, containedBy }`, the remote canonical. A write that
omits `ContainedBy` — a session's own report — leaves whatever a poll already
found there rather than clearing it, so the two writers never race each
other's half of the row; a repeat still refreshes the remote, the trunk and
the time, the same call a [merge check](#merge-check)'s repeat makes.

Every `IssueDto` carries its landings, ordered by canonical remote, read in
one batched query for a list of issues.

### Build check

`EfHatchBuildCheck` — `IssueId`, `Remote`, `Canonical`, `Branch`, `Sha`,
`ShaSince`, `Verdict`, `Failing` (`jsonb`), `PushedByIncrement`, `CheckedAt`,
`Runner`, `CheckedBy`. What the build on the tip of an issue's branch came to in
one repository, and which sha it is about. **A verdict is about one sha**, the
branch's tip on origin: a build that has concluded on a sha does not change,
short of a re-run, and a verdict about any other sha says nothing about the
branch as it stands now. `Verdict` is one of four:

| Verdict | Means |
|---|---|
| `passed` | Every check on the sha has concluded and none failed. Nothing for an agent to do |
| `failed` | Every check has concluded and at least one failed. `Failing` names each, with a link |
| `pending` | Something is still queued or running. A runner waits for every check to conclude before calling the verdict `failed`, so one increment sees every failure at once instead of spending two — but `Failing` already names whatever has failed so far, so a check that fails while others still run is visible before the last one concludes |
| `none` | No check ran on the sha |

A check run fails on `failure`, `timed_out` or `startup_failure`; a commit status
on `failure` or `error`. `cancelled`, `skipped`, `neutral`, `stale` and
`action_required` are not failures. Every check counts, not only the required
ones: whether a check is required is a branch-protection setting a runner may
not be allowed to read, and an optional check that fails is still red on the
pull request somebody reads.

**One verdict per issue per repository**, unique on `(IssueId, Canonical)`, for
the reason the [merge check](#merge-check) is: a passing repository must not
overwrite a failing one. An issue's build failed if any of its rows says so.

`ShaSince` is when the board first heard about the sha. A push's checks take a
few seconds to appear, so a `none` read straight after a push is usually
premature, and the runner keeps asking about a sha that reads `none` until ten
minutes after `ShaSince`. `PushedByIncrement` records that a build increment
pushed the sha, and is what tells a build that fails on an agent's own fix — a
question — from one that fails on somebody else's push, which is new work.

`PUT /api/hatch/issues/{key}/build-check` writes it, and a key may, for the
reason a key may write a merge check. It is refused, in a sentence, for a remote
that does not canonicalise, an unknown verdict, no branch, no sha, `failed` with
no failing checks, more than 100 of them, a check name that is empty, over 200
characters or has a line break in it, and a runner over its limit; an unknown key
is a `404`. Failing checks on a verdict that is not `failed` or `pending` are
ignored, not refused. They are deduplicated by name and sorted, so the same
failure listed in another order is not a change. **A check's link is stored only if it is an
absolute `http` or `https` address, and as null otherwise**: a key writes it and
the issue page draws it as a link, so anything else would be stored script. The
verdict is not refused for it. The issue's column is not checked, for the reason
the merge check's is not.

Four rules decide what is stored, and they hold whichever order two runners'
calls arrive in — another runner's poll can read the build between an
increment's push and the increment marking it:

1. `PushedByIncrement` after the save is `stored || request` for the same sha and
   the request's for a new one. It never falls back to false on the same sha.
2. **A mark never lowers a concluded verdict.** A request that says an increment
   pushed the sha, on the sha the row holds, with `pending` while the row holds
   `passed` or `failed`, takes the flag and keeps the verdict. Every other
   request stores what it says: a poll's `pending` after a `failed` is a re-run.
3. `ShaSince` is kept for the same sha and is now for a new one.
4. **The mark is set when a save moves the row into *failed and flagged*.**
   That covers `pending` then `failed` on a marked sha, and `failed` read first
   and marked after. The board writes it in the same save as the verdict, so no
   pass can see that failure without it, and a repeated verdict writes no second.
   It sets `StalledAt` to now and `StalledWhy` to the branch and the sha it
   failed at, the same two writes `PUT .../stall` makes.
   It is skipped when a question is already open: that is already the flag.

**A verdict that changes the stored one writes a `build_check_changed` event; a
repeat writes none.** Changed means a different verdict, a different sha, or a
different set of failing names. The payload is `{ remote, from, to }`, each side
`{ verdict, sha, failing }` with `failing` the names, the remote canonical. The
mark alone writes none. The trail says when a build started failing and when it
stopped.

Every `IssueDto` carries its build verdicts, ordered by canonical remote, read in
one batched query for a list of issues. A board that predates them omits the
field, and readers take that as none.

### Trunk build

`EfHatchTrunkBuild` — `Remote`, `Canonical`, `Trunk`, `Sha`, `ShaSince`,
`Verdict`, `Failing` (`jsonb`), `CheckedAt`, `Runner`, `CheckedBy`, `BugIssueId?`,
`RerunCount`. The same fact as a [build check](#build-check), but about the tip
of a repository's trunk rather than an issue's branch — a trunk build is
nobody's issue, so it is not keyed on one. `Verdict` is the same four
([above](#build-check)), by the same rules: a check run or a commit status
fails the same way, a pending build carries whatever has already failed, and a
runner asks about `none` again until ten minutes after `ShaSince`.

**One verdict per repository per trunk**, unique on `(Canonical, Trunk)`: two
projects may bind one repository with different base branches, and a single
verdict would let one project's trunk overwrite another's. A second write for
the same pair replaces the first.

`BugIssueId` is the bug the Human half's *File a bug* button filed while this
trunk was failing (HA-95) — nullable, `SetNull` on the issue's own delete. It
stays attached across new failing shas, because a fix that fails again is the
same outage, and is cleared the moment a verdict for the pair is `passed`, so
the next failure offers the button again.

`RerunCount` counts how many times a runner has taken a re-run of `Sha`'s
failing checks ([below](#checking-the-branches-in-review)) — reset to zero on
exactly the two events `BugIssueId` is let go on: `Sha` changing and a verdict
for the pair reading `passed`. A new build, or an outage that is over, gets its
own budget. `TrunkBuildDto` carries it back, trailing and defaulted to `0` for
a client that predates it, beside `ProjectKey` — not a column on the row at
all, but `ProjectRepositoryBinding`'s own answer for `Canonical`, read fresh on
every request rather than copied onto it: the project that names the
repository primary, lowest project id first, else the lowest-id project that
binds it at all, else null. It is what gates a runner's own re-run and
auto-filing ([below](#checking-the-branches-in-review)) — a trunk build with no
project binding it, or bound to a project this runner does not drive, is
nobody's to act on without a person.

`PUT /api/hatch/trunk-builds` writes a verdict and a key may, for the reason it
may write a build check — the caller is a runner, and a verdict only a person
could enter would be one nobody entered. It takes the same refusals the build
check's write does (an uncanonicalisable remote, an unknown verdict, no trunk,
no sha, `failed` with no failing checks, an over-long or broken check name, an
over-limit runner), naming the trunk rather than the branch. **No event, and no
question**: there is no issue to write either on, and a trunk's failure is a
person's row in the attention panel rather than a stall a session is asked
about. `GET /api/hatch/trunk-builds` answers every stored verdict, ordered by
canonical then trunk — the runner's poll reads it once a poll, since a trunk
carries no issue for the verdict to ride in on.

The runner's poll ([below](#checking-the-branches-in-review)) reads the build on
every checkout's trunk whether or not anything of its is in review, so a quiet
board is not a poll that never gets there.

### Playbook file

`EfHatchPlaybookFile` — `Remote`, `Canonical`, `Trunk`, `Sha`, `State`, `Hash`,
`Rows` (`jsonb`), `Problem`, `LastGoodSha`, `LastGoodHash`, `Url`, `CheckedAt`,
`Runner`, `CheckedBy`. What a repository's trunk holds at
`.hatch/playbooks.yaml`, as of the last time a runner's poll checked — the
rows the file parsed to, or the one sentence saying why it did not. Like a
[trunk build](#trunk-build), a playbooks file is a fact about a repository's
trunk and nobody's issue, so it is not keyed on one. A row's `prompt` is the
whole of its wording and its `defaultPrompt` is always null: nothing marks it
as the operator's own, there is nothing to hand it back to, and it changes only
by a commit to the file.

**One row per repository per trunk**, unique on `(Canonical, Trunk)`, the same
reason a trunk build's own key is: two projects may bind one repository with
different base branches, and a single row would let one project's read
overwrite the other's.

`State` is one of `parsed`, `invalid` or `absent`. `parsed` means the bytes at
the path were valid UTF-8, valid YAML, and every entry in them validated
against the same field rules a playbook request is checked against — `Rows`
holds what they resolved to, by status id rather than name, so a column
rename afterwards cannot change what a stored copy means. `invalid` means the
bytes exist but failed one of those checks — a bad field, an unknown column, a
duplicate transition — and `Problem` carries the one sentence saying which,
with a line number where the parser found one. `absent` means the trunk has
no file at that path at all. `Hash`, the file's SHA-256 in lower hex, is set
whenever the bytes were read at all — `parsed` or `invalid` — and only null
for `absent`; `Rows` is set only for `parsed`, and `Problem` only for
`invalid`.

`LastGoodSha` and `LastGoodHash` remember the most recent sha and hash that
parsed, so an `invalid` row's card can still say what to roll back to. A write
that parses clears both — the row is its own good copy now. A write that
fails to parse, immediately following one that parsed, copies that superseded
sha and hash into them before the row is overwritten; a second `invalid`
write in a row, with no parse between, leaves them exactly as they were. A
write that finds the file `absent` leaves them untouched too — whatever was
last remembered good stays remembered until a parse clears it or a fresh
transition out of `parsed` updates it.

`PUT /api/hatch/playbook-files` writes a row and a key may, for the reason it
may write a trunk build — the caller is a runner, reporting what it read, not
proposing it. `{ remote, trunk, sha, content?, url?, runner }`, `content` the
blob's exact bytes, base64-encoded, and absent for a trunk with nothing at the
path. The route hashes and parses the content itself rather than trusting a
client-sent verdict, and refuses in the same shape the trunk build's write
does — an uncanonicalisable remote, an empty or over-long trunk, sha or
runner — plus content that is not valid base64 or over the parser's own size
limit; an unsafe or over-long `url` is not refused, it is simply stored as
null. **No event, and no question**: there is no issue to write either on,
and a bad playbooks file is this row's own state, not a stall a session is
asked about. `GET /api/hatch/playbook-files` answers every stored row,
ordered by canonical then trunk, with the path itself (`.hatch/playbooks.yaml`)
alongside them — what the Playbooks page reads once a load, and what a
runner's poll reads once a poll to know whether it has already put this sha.

The runner's poll ([below](#checking-the-branches-in-review)) reads the file
off every checkout's trunk that has an origin, whether or not anything of its
is in review and whether or not any project reads it, so a quiet board
still has a current copy on the page.

### Comment, question and answer

`EfHatchComment` — `IssueId`, `Author`, `Body` (markdown), `Kind`, `AnswersId`,
`Options`, `DeliveredAt`, `DeliveredTo`, `CreatedAt`.

Most comments are notes: a commit sha, a summary for a reviewer, a change of
mind. Four are not, and those carry a `Kind`.

- A **question** (`Kind = "question"`) is an agent saying it cannot proceed
  without a decision that is not its to make.
- An **answer** (`Kind = "answer"`) is that decision, bound to the question it
  settles by `AnswersId`.
- A **message** (`Kind = "message"`) is something said to whichever session is
  working the issue, written from the Claim panel.
- A **verdict** (`Kind = "verdict"`) is a [check](#check)'s findings, posted by
  the apply that acts on the verdict — author the verdict's, body its findings or
  `(no findings)` — on every apply, whether it moved the ticket or held it. The
  thread is where a person reads them, and the next build session is handed the
  newest as *The last check*.

  Nothing needs to write one by hand, but `POST /issues/{key}/comments` does not
  refuse it: `verdict` is a legal `Kind`, so the comment is stored as given, with
  a `commented` event like a note. Only the sentence for a bad `Kind`, which
  names `question`, `answer` and `message`, leaves it out. It is a comment and
  nothing more — no [verdict](#verdict) row, no move, no effect on the second-fail
  count — but the read of "the newest verdict comment" cannot tell a hand-posted
  one from the apply's, so don't.

A question is a row rather than a heading in a comment body for the same reason
a ready date is a column rather than a line saying "not until March": something
has to be able to *act* on it. Prose in a thread can be searched; it cannot be
counted, and it cannot block a dispatch.

**Open is computed, never stored** — a question is open when no comment points
at it. That is why the link runs answer→question rather than the other way: a
decision can be refined by a second answer without editing the first, and a
question can never be open and answered at once because two writes disagreed.

**Waiting on somebody is open, and outside a terminal column.** A question
on a ticket that has shipped is never going to get an answer, so the moment
its issue lands in a terminal column it stops being counted, badged or
listed anywhere a person is asked to act — though it stays open, and stays
on the record, exactly as it always did.
[`Questions.cs`](../src/Hatch.Api/Modules/Hatch/Questions.cs) holds both
`Open` and `Waiting` side by side, and most of what used to read `Open`
reads `Waiting` now: the attention panel, the board's badge, the Plan
page's meters, and the two routes a person actually answers from. One
caller keeps reading `Open` on purpose, because what it is deciding does
not change once a ticket has shipped: [build check](#build-check)'s rule
four, which refuses to mark the issue stalled again while an earlier
question is still open, whatever column it's in.

`Options` is a `jsonb` list of `{ label, detail, recommended }`, null on a
question asked in prose. It exists because the first cut of this let an agent
ask, and what came back were dense paragraphs each fusing the question, two
named alternatives and a recommendation into one block a person had to parse by
eye before they could reply. A choice between named things is not an essay — it
is a menu, and a menu the reader can press is a decision made in one gesture.
Prose questions stay legal, because not every decision is a menu.

**Which option was taken is deliberately not stored.** An answer's body *is* the
label, so the thread reads as a decision rather than as an index into a list
nobody kept, and the sentence the next agent's prompt carries is the same
sentence a person reads six months later. A second column saying it in numbers
is a second thing that can come to disagree with the first.

**A stall mark is three columns on the issue row, not a question.**
`StalledAt`, `StalledWhy` and `Held` (`EfHatchIssue`) — the first two set by
`PUT /issues/{key}/stall`, open to a key: a dispatcher is exactly who leaves
this mark, and `StalledWhy` is the sentence it leaves behind saying why. A
second mark replaces the first — the row holds only the latest `StalledWhy`
— but every call still writes its own `stalled` [event](#issue-event), so the
trail keeps every sentence a dispatch ever left even though the row answers
with only the newest. `Held` is set only by `PUT /issues/{key}/hold`, a
person's call and never a key's.

**A marked issue folds the dispatcher until it resumes itself, or a person
clears it.** `Dispatch.Blocked` reads `Held` first, right after the claim and
before a pause: a held issue is folded unconditionally, and only `PUT
.../hold` with `held: false` lifts it. Short of that, a marked issue is
folded while it has been less than `Hatch:StallResumeMinutes` since
`StalledAt`, naming how long ago it stalled and when it resumes. Past that
window it would ordinarily be picked up again, except once `letGo` (below)
has reached `Hatch:StallResumeLimit` it is folded instead with a sentence
naming the count and the limit: a ticket stuck through that many tries in a
row is held for a person, not resumed again. Resuming — `PUT .../hold` with
`held: false` — clears `StalledAt` and `StalledWhy` together, the same
gesture whether or not anything was ever actually held, so *Resume now*
always leaves a clean mark behind it.

**A row clear only because its resume window has passed names it.** `hatch
queue` prints it in place of the bare arrow, e.g. `clear for To Do -> In
Progress - it stalled 18 minutes ago and resumes itself after 15 minutes`
(`clearNote` on `QueueEntryDto`) — the same way it names a row clear only
because of economy's or low's own pace.

**`letGo` counts a ticket's trailing dropped increments, and is what the stall
mark's resume limit is measured against.** A runner's release
may say how the increment ended — `DELETE …/claim?token=…&outcome=dropped` or
`&outcome=worked` (see [Claim](#claim)) — and `letGo` on `work/{key}`,
`work/next` and a queue row is this issue's releases, newest first, whose
outcome was `dropped`, counted back to the first of: a release whose outcome
was `worked`, a `status_changed`, or an `answered` event written by a person.
A release with no outcome is skipped over — it neither counts nor stops the
count, which is what keeps an older CLI's releases, a restart, and the pick's
own release from reading as a string of drops. `0` on a client too old to
read the field. Once a marked issue's own `letGo` reaches
`Hatch:StallResumeLimit`, the dispatcher folds it instead of resuming it —
see the stall mark, above.

**A message is not every comment, on purpose.** The server cannot tell an
operator's comment from the session's own — both arrive on the same key under
the same name — so delivering every comment would feed a session its own commit
notes, and a note is often written for a reader six months on rather than for the
agent now. The Claim box talks to the agent; the Comments box stays a note.

`DeliveredAt` and `DeliveredTo` are null on everything but a message that has been
read. **Read means put into a session's context**: the hook that printed it, or
the runner that put it in a prompt, is what marks it, and nothing can prove the
model acted on it. `DeliveredTo` is the live claim's runner, or the caller's name
where nothing held the issue. They are written once, by `POST
/issues/{key}/messages/deliver`, whose conditional `UPDATE` repeats "and still
unread" in its `WHERE` — the way the claim closes its races
([`IssueClaims.cs`](../src/Hatch.Api/Modules/Hatch/IssueClaims.cs)) — so two
checks that race return a message once between them. Unread messages ride the
dispatch as `WorkDto.Messages`.

### Search

**Three stored, weighted `tsvector` columns, each computed by Postgres, never
assigned in code, each GIN-indexed:** `EfHatchIssue.SearchVector` (`Title`
weight A, `Description` weight B), `EfHatchComment.SearchVector` (`Body`,
weight C, every kind of comment — note, question, answer, message, verdict),
`EfHatchWorkLogEntry.SearchVector` (`Title` and `Summary`, both weight D).
See [`SearchQuery.cs`](../src/Hatch.Api/Modules/Hatch/SearchQuery.cs) and
[`SearchController.cs`](../src/Hatch.Api/Modules/Hatch/SearchController.cs).

**A hit is counted once per issue**, however many of its comments or work-log
rows matched — the match is an `EXISTS`, not a join.

**Ranking with `q` is `ts_rank` over the issue's own vector only**: a comment
or work-log match decides whether an issue is found, not where it sorts.
Ties: closed ahead of open, then newest-closed, then newest-created. Without
`q` — or with one that reduces to no terms, where `SearchQuery.ToTsQuery`
returns null — the list is newest first by closed, or created where open, so
a column's issues come most-recently-closed first.

**`matched` on a hit** lists which of `title`, `description`, `comment`,
`work-log` found it, in that order.

**Event payloads are deliberately not indexed**: they are superseded copies
of text already indexed elsewhere, and indexing the JSON would surface a
retitle's old wording as a match on the issue it no longer describes.

**Done issues are in the result by default**; `statusId` narrows to one
column, and none of the board's done-horizon folding applies.

### Issue event

`EfHatchIssueEvent` — `IssueId`, `Actor`, `Kind`, `Payload` (`jsonb`), `At`.
Append-only, written by every mutating endpoint, never edited and never deleted
except with its issue.

Kinds: `created`, `retitled`, `redescribed`, `retyped`, `status_changed` (payload `{ from, to, fromId, toId }` - the column
ids written beside the names, because columns are renamed (the `TheFlow`
migration renamed five) and a name alone cannot place a stretch of history
after the rename; `from` and `fromId` are null where there was no previous
column; an event written before the ids were added carries names only, so a
reader must not assume them; the hop flags - `express`, `pulled`, `merged`,
... - ride beside them unchanged; a move the [check](#check) apply made carries
`verdict` and `playbookId` there too),
`parent_changed`, `ready_changed`, `due_changed`, `pull_request_changed`,
`model_override_changed`, `effort_override_changed`, `assignee_changed`,
`priority_changed`, `express_changed`, `wip_limit_changed` (an epic's [Stories
at once](#stories-at-once) was set, changed or cleared - payload `{ from, to }`,
either side null rather than the default), `dependency_added`,
`dependency_removed`, `claim_taken`, `claim_lapsed` (a take found a lease that
had already gone quiet past its terms and is taking it over — payload
`{ from, heardAt }`, the previous holder and when it was last heard from,
written immediately before the `claim_taken` for the runner taking over),
`claim_released` (payload `{ from, to, outcome }`, `outcome` left out entirely
rather than serialized as null where the release named none), `claim_cleared`,
`verdict_recorded` (a [check](#check) session recorded its verdict - payload
`{ verdict, playbookId }`, written on every call, a replace under the same claim
included), `verdict_held` (the apply found a second consecutive fail and moved
nothing - payload `{ verdict, playbookId }`, `verdict` always `fail`, written
beside the `asked` for the question it posts), `merge_check_changed`, `build_check_changed`, `pull_request_state_changed` (the
forge's last word on the recorded pull request - its merge state, its review
decision, or the head sha they were read at - differs from what the board
held, the first read counting; payload `{ from, to }`, each side `{ mergeState,
reviewDecision, headSha }` and `from` null on the first read. The board stores
them; `MergeWork.Judge` reads the merge state and head sha (see
[the merge column's judge list](#the-approval-column)) and nothing else on the server does, and
the review decision is read by nothing. A read that repeats all three writes no
event and refreshes only when it was read), `wip_overridden` (a move into a
full [WIP](#wip) section, or past [an epic's own
limit](#an-epics-own-limit), let through because a person said *move
anyway* - payload `{ limit, load, to, epic? }`, `load` counting the card
itself and `epic` present only when it was the epic's own limit that was
stepped over, naming it; written beside `status_changed` only when the move
would otherwise have been refused, and twice - the section's first, with no
`epic`, then the epic's - where both limits were over at once),
`commented`, `messaged`, `message_delivered` (the payload
names the comment and the runner), `asked`, `answered` (payload `{ questionId,
lapsed }` - `lapsed: true` only on a row written before the stall mark
replaced lapsing, when taking a claim answered a question itself; nothing
writes that shape now, but an old one is still skipped rather than counted or
stopped at, same as `letGo` above), `imported`.

Only `status_changed` is read back, by the [Timeline](#the-timeline); no other
kind is drawn anywhere. All of it has been written since the first release
anyway, because an event log is the one feature that cannot be
added retroactively: turned on in March, it answers nothing about February. The
cost is a row per edit.

`Payload` is schemaless on purpose — `{ "from": …, "to": … }` for an edit, the
source filename for an import — because the shape differs per kind and a column
per field would be a migration every time a new verb is logged.

**Rank-only moves are deliberately not events.** Dragging a card up its column
is board hygiene, not work, and logging it would bury the status changes that
matter under a hundred lines of tidying.

### Playbook

`EfHatchPlaybook` — `FromStatusId`, `ToStatusId`, `Types`, `Shape`, `Prompt`,
`DefaultPrompt`, `Model`, `Effort`, `Budget`, `BriefLimit`, `DeniedTools`,
`IsCheck`, `ProjectId`. Unique on `(from, to, types, shape, project)`.

What an agent is told, and how much thought to spend, when it moves an issue of
some type and shape from one column to the next. `Shape` is one of `any`
(the issue's children are not consulted), `leaf` (it has none) or `parent` (it
has at least one). `DeniedTools` is null or empty for every tool, as today, or
else a list of Claude Code permission rules in deny position — a bare tool
name (`Edit`) or a scoped pattern (`Bash(git push *)`). `ProjectId` is null on
every row written before the column existed, and null means the row speaks
for every project on the board; a row naming one project speaks for that
project alone. See [the dispatcher](#the-dispatcher). `IsCheck` is false by
default and a person's write only — a key's `POST` or `PATCH` of it is refused
with the rest of the playbook write — and what it means is in [Check](#check).

**`Prompt` is the operator's own wording, `DefaultPrompt` the prescribed
text.** `Prompt` is null until somebody writes their own; `DefaultPrompt` is
null on a row a person created and always null on a row built from
`.hatch/playbooks.yaml`. A dispatched session is handed `Prompt ??
DefaultPrompt`, and on the wire `prompt` is that effective text beside
`defaultPrompt`, the prescribed one. See [Playbooks](#playbooks) for what a row
says about its own prompt.

**A read-only example, not a shipped list.** `DeniedTools: ["Read", "Edit",
"Write", "NotebookEdit", "Bash(git add *)", "Bash(git commit *)",
"Bash(git push *)", "Bash(git checkout *)", "Bash(git switch *)",
"Bash(git reset *)", "Bash(git restore *)", "Bash(git stash *)",
"Bash(git merge *)", "Bash(git rebase *)", "Bash(git cherry-pick *)",
"Bash(git clean *)", "Bash(rm *)", "Bash(mv *)", "Bash(cp *)", "Bash(tee *)",
"Bash(touch *)", "Bash(sed -i *)", "Bash(hatch move *)", "Bash(hatch start
*)"]` denies the file tools by their bare name (the file-shaped names in
`ClaudeTools.BuiltIn`), the git subcommands that write as one
`Bash(git <subcommand> *)` pattern apiece, the shell writers the same way,
and the two `hatch` verbs a session could use to move or start a ticket on
its own. It does not cover everything a tool rule can be asked to stop:
Claude Code's own documented pattern-matching behaviour is that a `Bash` rule
matches the command as written and not `sh -c '...'`, a program by its full
path, or `git -C . push` (quoted on HA-422). And a bare-name deny is narrower
than it reads — a real run denying `Write` alone did not stop the write it
named; the session routed around it with a plain `Bash` call instead, because
the deny list left `Bash` itself untouched (HA-454's own run). A bare-name
deny removes *that one tool*, not the action everywhere else capable of it.

### Verdict

`EfHatchVerdict` — `IssueId`, `PlaybookId`, `StatusId`, `Verdict`, `Findings`,
`Author`, `ClaimToken`, `CreatedAt`, `AppliedAt`. Unique on `(IssueId,
ClaimToken)`.

What a [check](#check) session concluded about an issue: `Verdict` is `pass` or
`fail`, `Findings` is whatever came with it (null when nothing did), and
`ClaimToken` is the lease the session was running under — which is why a second
write under the same claim replaces the first, and why a new claim starts a new
row. `Author` is the caller's name, as on a comment.

**The row is its own audit record.** Unlike the bare `ClaimPlaybookId` it was
written under, `PlaybookId` and `StatusId` are real navigations to the playbook
and the column the issue stood in when the verdict was given, so the row still
says under which playbook and where it was judged after the issue has moved on
and the claim is gone.

`AppliedAt` is null until [the apply](#check) sets it, and is set by nothing
else — not by a replace, which leaves it alone. A verdict with it set is not
looked at again: a retried apply under the same claim finds no unapplied row and
answers `404`. Written by `POST /issues/{key}/verdicts`; no route reads it back.

### Overnight seen

`EfHatchOvernightSeen` — `PersonId` (a bare `Guid` rather than a foreign key,
for the reason `EfHatchProjectMember.PersonId` is one: Hatch owns its own
schema and migration history, and a constraint into `public.People` is the
compile-time coupling `Modules/README.md` exists to prevent), `SeenAt`.

One reader's own mark on [the overnight](#the-overnight) page — when they last
read it, nothing more. Set only by `POST /api/hatch/overnight/seen`, a
person's call and nothing else's: a program or a keyless runner naming itself
with the runner header is refused the same way
(`OvernightController.NotAPerson`). No body — a person has only their own
mark to set — and a second call replaces the first rather than adding a row.

**Strictly later than the mark, not later-or-equal, decides unseen** — a
landing at exactly the mark is the mark, not after it.

## The wall, the roles, and API keys

Hatch ships its own wall, off by default (`HATCH_AUTH` in `compose.yaml`), with
Google sign-in. Turning it on is the walkthrough's *Putting a front door on it*
(`hatch-at-home.md`). Two boundaries follow: the wall, and the role gate on top
of it — and the wall has a second lane, the API key.

### The posture

- **The wall** (`AuthGate` + `AuthMiddleware`, enforced in process)
  decides whether a request reaches the app at all.
- **The role gate** (`RoleGate`) decides whether an already-authenticated
  request reaches what it asked for, by the role on the person the device
  belongs to — `pending`, `user` or `admin`, an ordered enum on `EfPerson.Role`.
  It asks "at least this role", and it is on whenever the wall is: there is no
  switch. Every Hatch controller carries
  `[RequireRole(PersonRole.User, AcceptScope = "hatch")]` (a handful of
  person-only routes carry `[RequireRole(PersonRole.User)]` with no scope), and
  the bundle is in `AdminAppMiddleware`'s `GatedApps` beside `/apps/admin`,
  also at User. The operator's own verbs — people writes and sessions, grants,
  invites, API keys, the core settings — are `[RequireRole(PersonRole.Admin)]`,
  spelled `[RequireAdmin]`.

A Pending person is refused at every level, and reaches only the sign-in screen
and the two routes that say who they are (`/api/auth/me`, `/api/auth/sign-out`),
which ask the wall for a credential and nothing more.

An Admin can skip the waiting by adding an email on the Users page first. The
address becomes an *unclaimed* identity (no `Subject` yet) carrying the chosen
role, and the first verified Google sign-in with that address binds it: from
then on the `sub` is the identity, and the email is never used to find it again.

| Reaching | As | Answer |
|---|---|---|
| `/apps/hatch/…` navigation | no credential | **302** to sign-in with `?r=` |
| `/api/…` | no credential, or a key nobody minted | **401** |
| `/apps/hatch/…` navigation | signed in, Pending (or a device nobody has claimed) | **302** to sign-in with no `?r=`, where the shell says they are waiting |
| `/api/hatch/…` | signed in, Pending | **403** `pending_approval` |
| an Admin route | signed in, User | **403** `not_admin` |
| `/apps/hatch/…` | an API key, or any non-navigation | **404** — indistinguishable from an install built without it |
| an Admin route | an API key | **403** `key_not_accepted` |

The two gates stay separate rather than being folded into one. The wall runs on
every request and is enforced in two places; the role gate runs on the handful
of routes that ask and is enforced only in this process. Folding them would put
a person lookup on the health probes and the media stream, which is precisely
what the wall's allow-list exists to prevent.

### One permission beyond the role

A third thing sits beside the wall and the role gate rather than inside
either: a project's `EfHatchProjectMember` role (`Owner` or `Approver`)
grants the one permission a person can hold beyond their global role —
moving an issue into a protected column, read through
`IProjectAccess.CanApproveAsync`. It is the single exception to the rule the
rest of this section runs on; see [One granular permission](#non-goals) for
why it stops at exactly one.

### The key

`EfApiKey` — `Name` (unique), `Prefix`, `Hash`, `Scopes` (`text[]`),
`CreatedAt`, `LastUsedAt`, `RevokedAt`.

A secret is `hatch_ak_` + 32 characters from a cryptographic RNG, drawn from
letters and digits only because a key is copied through shells, YAML and JSON
and every one of those has an opinion about punctuation. It is shown **exactly
once**, at mint, on the admin app's **API keys** page.

The `hatch_ak_` prefix is not decoration. It is there so that a string pasted
into a config file, a log line or a commit is recognisable as a Hatch
credential on sight — by a person reading a diff, and by the secret scanners
that read public repositories for exactly these shapes.

Only the SHA-256 of the secret is stored, exactly as for a grant token: a stolen
database yields nothing usable, and a lost key is replaced by minting another
rather than by looking this one up. `Prefix` — the first 12 characters — is
stored separately because it cannot be derived from a hash, and it is the only
part of a secret it is safe to show; without it, a key list is a list of names
somebody typed that you cannot check against the value in a config file.

Revocation is a **column, not a `DELETE`**, unlike a grant: the audit trail
names this key by a name that has to keep resolving, and "who was Claude in
March" is a question a deleted row cannot answer. `LastUsedAt` is written at
most once per `Auth:LastSeenThrottleSeconds`, the same throttle a grant's
last-seen takes and for the same reason.

**The row lives in the core `public` schema**, not in `hatch` beside the app
that prompted it. The wall reads this table on the way in, so a module owning it
would make the wall depend on a module — the one thing `Modules/README.md` says
never happens. The scope strings name modules; the rows do not belong to them.

A key is a **peer** of a grant rather than a variant of one. A grant is a device
the household enrolled and the wall lets in whole; a key is a named program
allowed a stated slice. They differ in every field that matters — a key carries
scopes and a grant does not, a key is revoked by a column and a grant by a
`DELETE`, and a key is never re-issued into a cookie. Folding them into one
table would mean a nullable half of the row for each.

### One gate, two lanes

The bearer header is **not a second authentication system**. `AuthMiddleware`
reads `Authorization: Bearer` alongside the cookies and hands both to the same
`AuthGate.EvaluateAsync`, which hashes the secret, matches an unrevoked key, and
returns an ordinary `AuthDecision` carrying the key. `AuthController.Verify` —
the endpoint a fronting proxy may call (the system this repository came from
used Traefik's forwardAuth) — reads the header off the forwarded request
exactly as it reads the cookie, so a proxy and the in-process half agree
without either learning a new concept.

That is the whole of the rationale, and it is worth stating plainly because it
is what the design is *for*: **there is one place that decides whether a request
gets in, and one allow-list of what never has to.** A separate key middleware
would have been less code to write and would have created a second answer to
"who is calling" — a second thing to keep in step with the allow-list, a second
thing to remember when the wall's rollback is exercised, and a second thing that
could be right when the first was wrong. A new credential is a lane through the
existing gate, never a gate beside it.

Three things fall out of that, none of them added on purpose:

- **No cookie is re-issued on the key lane.** A key is the whole credential,
  held in a file the wall did not write and cannot refresh.
- **`unknown_key` is a refusal reason like any other**, logged in the same line
  and shaped by the same `Refuse`. A caller that presented a bearer gets a bare
  `401` rather than a redirect to sign-in, because a program has no browser to
  redirect.
- **The key lane is decided before the person lane and never falls through to
  it.** A key has no person and never will, so a key that fails must be refused
  rather than handed to a check that would refuse it again for the wrong reason
  — and, more importantly, a key must not inherit the answer a browser signed in
  on the same machine would have got.

### What a scope is, and what it is not

`AcceptScope` on `[RequireRole]` names the one scope that route will take from
a key. It is **opt-in**: naming a scope is a claim that this surface has been
thought through for a caller that is a program rather than a person, and the
honest number of surfaces that have been is one. Every route that names none —
minting credentials, revoking sessions, editing the house, roughly forty verbs
— keeps refusing keys, with `key_not_accepted`. That is what makes adding a key
a bounded act rather than a broad one.

A scope widens nothing for a person: a route that accepts `hatch` is still
closed to a person below the role it asks for. And a key is never an
*administrator* — `RoleOutcome.Key` is its own outcome, and it means "this
program reaches what its scopes name", not "this program is the operator".

If a request comes back `403`, the key is working and the route is not one a key
may take.

### The one edge that is deliberately cut

**Writing a playbook is closed to a key.** The three write verbs on
`PlaybooksController` carry a plain `[RequireRole(PersonRole.User)]` naming no scope, while the
read carries the scope like everything else. `DeniedTools` rides the same
three verbs and the same person-only cut — it is not a new edge.

**So is writing an issue's override**, and it is the same edge rather than a
second one: `PATCH /api/hatch/issues/{key}/playbook` sets the model and the
effort every increment on that ticket runs on, which is a playbook's power
routed through another table. It lives on its own controller
(`IssuePlaybookController`) for exactly that reason — `IssuesController` accepts
the `hatch` scope on everything it holds, so two more fields on the ordinary
issue patch would have been an agent that can raise its own budget. Reading an
override is open, like everything else a dispatch needs: an agent is entitled to
know what it is being spent on, and both fields ride `IssueDto`.

This is the one edge in the graph that would close a loop. A playbook chooses
the model, the effort and the prompt for the next agent, so an agent able to
edit one could widen its own instructions and raise its own budget — and that
failure is unbounded spend rather than a wrong answer, with no fixed point to
settle at. Cutting it costs nothing, and it is cut *in the route* rather than
asked for in a prompt, because a rule an agent is merely told is a rule an agent
can reason its way past.

**And so is setting an assignee**, which is a *related* edge rather than the
same one. A playbook widens what an agent may spend; an assignee widens what it
may be **sent at** — because under the loop's `people only` rule, a name on a
ticket is what holds it off the night shift. A key that could write one could
clear a person's name off a ticket and hand itself work somebody had reserved.
So `PUT /api/hatch/issues/{key}/assignee` lives on its own controller
(`AssigneeController`) carrying no class-level scope, for the same reason and by
the same means. Reading is open, and deliberately: an agent has to be able to
say whose ticket it is leaving alone, so both `GET /api/hatch/assignees` and the
`assignee` on `IssueDto` are Hatch-scoped like everything else.

**And so is setting the priority level**, which is the same edge as the
assignee read from the other side. An assignee holds a ticket *off* the night
shift; priority puts one at the *front* of it — so a key that could set one
could put its own ticket ahead of everything a person filed, every night,
without anything looking wrong on the board. `PUT
/api/hatch/issues/{key}/priority` and its legacy two-level alias `PUT
.../expedite` therefore both live on `IssueExpediteController`, which carries
no class-level scope and guards each action the same way, cut in the route by
the same means. Reading is open like the rest: `priority` and the derived
`expedited` both ride `IssueDto` and `IssueCardDto`, because an agent is
entitled to know why it was sent where it was sent. It follows that there is
no `hatch priority` or `hatch expedite` verb — the CLI authenticates with a
key, so the terminal *shows* the level on `board`, `queue` and `show` and sets
it nowhere.

**Filing the bug a failing trunk's button offers (HA-95) bends this edge, and
only this one.** `TrunkBuildBugController` still carries no class-level
scope — a stranger key pressing the button for a ticket of its own choosing is
cut by the same means as priority. But the method itself accepts the `hatch`
scope (HA-792), gated at runtime rather than by the attribute, because the
attribute cannot vary project to project: a key may take it only where the
project that binds the row's repository has turned auto-filing on, and where
it has, the bug's priority, pre-approval level and column come off that
project's own `AutoHealBugPriority`, `AutoHealBugPreApproval` and
`AutoHealBugStatusId` — never off the request body a key sends, which is not
read on this path at all. A key choosing its own priority or pre-approval is
exactly what this edge exists to keep closed; what makes this one safe is
*whose* choice those three fields really are. They are written only through
`ProjectsController.PutLanding`, the same owner-only route that sets how a
project's work lands ([Pre-approval](#pre-approval)) — so turning auto-filing
on is an owner's standing permission for this one project, recorded in
advance, the same way setting `preapproved` directly is a press given in
advance for one issue, given here instead for every bug a trunk's own future
failure will file under it. A key against a project with auto-filing off is refused the same
sentence a key with no project binding it gets, naming the Projects page. A
person's own call is unchanged in every respect — same body, same defaults
(expedited, no pre-approval, the board's first column), same refusals. Filing
itself still goes through `IssuesController.CreateIssueAsync`'s internal,
level-aware overload, so the bug's number, rank, column and `created` event
are the ordinary ones and the level is set in the same save rather than a
second write after. Reading the trunk builds themselves stays Hatch-scoped,
the way reading a build check is: a verdict about a sha is a fact any runner
reads the same way, whichever repository it binds.

**A key's owner is cut the same way, and for the reason the assignee edge
names directly.** `--mine` (see [the dispatcher](#the-dispatcher)) reads a
key's `OwnerPersonId` to decide whose tickets it may take, so a key that could
set its own owner could simply say it is whoever holds the tickets it wants.
`ApiKeysController` is `[RequireAdmin]` with no `AcceptScope` on the whole
class — not one route carved out of it — so no key can mint a key, revoke one,
or change whose it is; a person does that, at the API Keys page, on purpose.

**And so is the WIP section and its limits.** Which columns are work in
progress, and how much of each slice of the board may sit across them at once,
is the operator's to set — a key that could raise a limit or empty the
section could pull more of its own work in overnight, the loop stalling behind
a ceiling it had just raised for itself. `PUT /api/hatch/wip` therefore lives
on its own controller (`WipController`) carrying no class-level scope, cut in
the route by the same means, and checked a second time in the action for
`IssueClaimController.NotAPerson`'s reason: `RoleGate` is dormant wherever the
wall is off, and a keyless runner there is a program too. Reading is open, like
the rest: an agent stalled behind a full section is entitled to know why.

**And so is pre-approval, both the issue's own level and the column's flag
it rides on the way through.** Pre-approval decides which gates the loop
may pass *unattended* — a key that could raise it to `express` or
`preapproved` could carry its own ticket through the night with nobody
reading it first, or approve it in advance, the same kind of widening a
playbook or an assignee would be. `PUT /api/hatch/issues/{key}/pre-approval`,
and its legacy two-level alias `PUT .../express`, both live on
`IssuePreApprovalController`, carrying no class-level scope, cut by the
same means as priority and expedite — and gated by what the change touches
rather than by one blanket check: moving onto or off `express` asks the
same question an ordinary move into `Columns.SendBack` would, through
`IProjectAccess.CanMoveIntoAsync`; moving onto or off `preapproved`, on
either side of the change, asks `IProjectAccess.CanApproveAsync` instead,
since setting it is the approve press given in advance and lowering from
it undoes that act. The column half cannot ride `PATCH
/api/hatch/statuses/{id}` — that route accepts the `hatch` scope on every
action, so one more field on the ordinary status patch would have been a
key ticking its own gate — so it gets its own action, `PUT
/api/hatch/statuses/{id}/express-skips`, carrying a plain
`[RequireRole(PersonRole.User)]` the way `ProjectsController.PutRepositories`
does; the rest of `StatusesController`'s actions each carry the class-level
attribute explicitly instead, because `RequireRoleAttribute` is
`AllowMultiple = false` and a method-level attribute silently *replaces* a
class-level one rather than tightening it. Reading both is open like the
rest: `preApproval` rides `IssueDto` and `IssueCardDto`, and `expressSkips`
rides `StatusDto`. It follows that there is no `hatch` verb that sets any
of it, the same as expedite.

**And so is an epic's own [Stories at once](#stories-at-once).** A key that
could raise its own epic's ceiling could pull more of its own stories into
progress at once, the same widening the WIP section's own limit is cut
against. `PATCH /api/hatch/issues/{key}/wip` therefore lives on its own
controller (`IssueWipLimitController`) carrying no class-level scope, cut by
the same means as the playbook override, and checked a second time in the
action for `WipController.NotAPerson`'s reason: `RoleGate` is dormant wherever
the wall is off, and a keyless runner there is a program too. Reading is
open like the rest: `wipLimit` rides `IssueDto`, because an agent is entitled
to know how many of its siblings it is competing against. It follows that
there is no `hatch` verb that sets it, the same as expedite and express.

One related edge is **not** cut, and is stated rather than papered over:
**nothing stops a key answering its own question.** A key is what
`hatch answer` types with and it is also what a spawned agent inherits; the
server cannot tell them apart, and a check that looked like it could would be
worse than none. What holds the loop shut is one step further out — the dispatch
is refused while a question is open, and the dispatch is a command the operator
types. A second scope minted for agents would close it properly, and is not
worth a column until somebody wants one.

One route sits outside the wall entirely rather than riding the exempt-paths
list for a reason worth stating: `/.well-known/assetlinks.json`
(`AssetLinksController`) answers Android's own Digital Asset Links verifier,
which holds no cookie and never will. That is safe to leave open because the
answer is public by design - it is a list of which Android package may open
this install's links without the user granting it by hand, nothing more, and
it grants nothing to anyone who merely reads it.

## API surface

Everything under `/api/hatch`, every route `[RequireRole(PersonRole.User,
AcceptScope = "hatch")]` except where noted. Issue routes take the display key (`AER-12`).

| Route | Verbs | Notes |
|---|---|---|
| `/projects` | GET, POST | POST validates key format and uniqueness, and carries `color`/`icon` |
| `/projects/{id}` | PATCH, DELETE | PATCH name and key, and carries `color`/`icon`; DELETE 409s unless the project is empty, and takes its [repositories](#repository) with it |
| `/projects/{id}/repositories` | GET | The ordered list of remotes, in `SortOrder` — see [Repository](#repository) |
| `/projects/{id}/repositories` | PUT | **Person only** — plain `[RequireRole(User)]`. The whole ordered list, `[{ remote, baseBranch? }]`; refused as a whole, naming the entry, on an empty, over-limit, unparseable or duplicate remote. Re-sending the same list writes nothing |
| `/projects/{id}/landing` | PUT | **Person only** — plain `[RequireRole(User)]`, owner only. `{ landing?, checksRequired?, onePullRequest? }` — `onePullRequest` is the project's default for [one pull request](#one-pull-request); `landing` is `"pull-request"` or `"trunk"`; `403` naming the project's key from anybody but an owner, `400` naming the bad value on an unknown `landing`, `404` on an unknown project. Re-sending what it already holds writes nothing |
| `/projects/{id}/members` | GET | Who owns or approves on this project — open to a key, reading who is on a project is not a power |
| `/projects/{id}/members/{personId}` | PUT | **Person only** — plain `[RequireRole(User)]`, and only an owner of the project may call it. `{ role }` — `"owner"` or `"approver"`. `403` from anybody else, `400` on an unknown person or role, `409` where it would leave the project with no owner |
| `/projects/{id}/members/{personId}` | DELETE | **Person only**, owner only, the same guard as the PUT beside it. `409` where it would leave the project with no owner |
| `/projects/{id}/claim` | POST | **Person only** — plain `[RequireRole(User)]`. Takes ownership of a project that has none — the way out of the state the owner-removal guards above exist to prevent reaching any other way. `409` once the project already has an owner |
| `/projects/{id}/logo` | GET | The bytes, sniffed type and an ETag from `UpdatedAt` — see [Logo](#logo). 404 with none uploaded |
| `/projects/{id}/logo` | PUT | **Person only** — plain `[RequireRole(User)]`. The body is the image itself, not multipart; refused with a sentence on an unsupported format, an empty body, or one over `PersonPhoto.MaxBytes` |
| `/projects/{id}/logo` | DELETE | **Person only** — plain `[RequireRole(User)]`. 404 with none uploaded |
| `/statuses` | GET, POST | |
| `/statuses/{id}` | PATCH, DELETE | DELETE 409s while any issue holds it |
| `/statuses/{id}/express-skips` | PUT | **Person only** — plain `[RequireRole(User)]`. `{ expressSkips }` — which columns an [express](#pre-approval) issue is carried past with no session. Neither `POST /statuses` nor `PATCH /statuses/{id}` can set it |
| `/statuses/{id}/parent-pulls` | PUT | **Person only** — plain `[RequireRole(User)]`. `{ parentPulls }` — the column a child is carried on out of while its parent stands in the implementation column. Neither `POST /statuses` nor `PATCH /statuses/{id}` can set it |
| `/statuses/{id}/implementation` | PUT | **Person only** — plain `[RequireRole(User)]`. `{ isImplementation }` — the column where code gets written. Ticking one clears the rest; ticking a deferred or terminal column is refused with a 400, and unticking is always accepted. Neither `POST /statuses` nor `PATCH /statuses/{id}` can set it |
| `/statuses/{id}/agent-files` | PUT | **Person only** — plain `[RequireRole(User)]`. `{ agentFiles }` — the column a program's issue is born in. Neither `POST /statuses` nor `PATCH /statuses/{id}` can set it |
| `/wip` | GET | `{ statusIds, slices }` — the flagged columns that are neither deferred nor terminal, in board order, and `slices`: always two entries, `{ types, limit }` for `story,bug` then `epic`, `limit` null where no row is held |
| `/wip` | PUT | **Person only** — plain `[RequireRole(User)]`, checked again in the action. `{ limit?, epicLimit?, statusIds? }`, the bulk rule throughout: `limit` and `epicLimit` are each a string (`""` clears the slice, a whole number of one or more sets it, and each is validated before anything is touched — a good field beside a bad one changes neither), `statusIds` is the whole section (`[]` clears it) and refuses a column that does not exist, or one that is deferred or terminal. Re-sending what is held writes nothing |
| `/board` | GET | Statuses plus every issue, ordered by `(StatusId, Priority desc, Rank, Id)`. Never filtered — the browser folds not-yet-ready cards away; the server hands over all of them — except the one card the server itself withholds: a terminal card whose `ClosedAt` is older than `doneHorizonHours` (48 by default, off at `0`) is not handed over at all, and `doneHorizonHours` on the DTO says what that horizon is — and `wip`: `{ statusIds, slices }`, `slices` the same two entries as `/wip`'s read but each with `load` and `claimedInbound` too, `null` only where no column is flagged (see [WIP](#wip)) |
| `/issues` | GET, POST | GET filters on `projectId`, `type`, `statusId`, `parentKey`, `ancestorKey`, `text`, ANDed, all optional |
| `/issues/bulk` | POST | `keys` plus any of `type`, `statusId`, `parentKey`, `readyAt`, `dueAt`. No `wipOverride` — a full [WIP](#wip) section or [an epic at its own limit](#an-epics-own-limit) is a per-key failure, named in `failures`; `projectId` with `moveDescendants` moves the batch the same way — see [Moving between projects](#moving-between-projects) — `400` on an unknown project, `403` from a key or a keyless runner, a live claim anywhere in the moving set a per-key failure rather than a whole-request refusal; the response gains `rekeyed: [{ from, to }]`, additive and empty when no project was named |
| `/issues/{key}` | GET, PATCH, DELETE | PATCH writes one event per changed field; `""` clears a parent, a date or the pull request URL; `wipOverride` — see [WIP](#wip) — moves a full section or a full epic anyway, `409` (`WipRefusalDto`, carrying the epic's own numbers when it is the epic's limit) otherwise, `403` from a key or a keyless runner; `projectId` with `moveDescendants` moves it to another project — see [Moving between projects](#moving-between-projects) — re-keying it and (by default) everything below it; `400` on an unknown project, `403` from a key or a keyless runner, `409` naming any issue in the moving set holding a live claim |
| `/issues/{key}/move` | POST | `{ statusId, afterKey?, beforeKey?, fromStatusId?, wipOverride? }` — the server computes the rank. A card no longer in `fromStatusId` is a 409 and nothing is written; a move into a full [WIP](#wip) section or past [an epic's own limit](#an-epics-own-limit) is the same (`409` carrying the epic's own numbers when it is the epic's), unless `wipOverride` is set (person only - `403` from a key or a keyless runner). **A key is refused with `409` while the issue's live claim is a [check](#check)'s** — *"<KEY>'s live claim is a check's - a session judges here, it does not move the ticket"* — before anything else is read, a reorder inside one column included; a person's move is not |
| `/issues/{key}/comments` | GET, POST | POST carries the kind (a note, `question`, `answer`, `message` or — though only the apply has a reason to — `verdict`), the `answersId`, and a question's options; every comment carries `deliveredAt` and `deliveredTo` |
| `/issues/{key}/messages/deliver` | POST | marks messages read, all unread or the `ids` named, and answers with only the ones this call marked |
| `/issues/{key}/questions` | GET | `?open=false` for the answered ones too |
| `/issues/{key}/events` | GET | Newest first |
| `/issues/{key}/playbook` | PATCH | **Person only** — plain `[RequireRole(User)]`. The issue's own model and effort; `""` hands either back to the playbook |
| `/issues/{key}/wip` | PATCH | **Person only** — plain `[RequireRole(User)]`. `{ limit }` — see [Stories at once](#stories-at-once). `""` clears it back to the default; a number below one, or not a number, is `400`; anything but an epic is `400` naming its type |
| `/assignees` | GET | Every person and every live key, plus who the caller is — the picker's rows and *Assign to me* in one read |
| `/issues/{key}/assignee` | PUT | **Person only** — plain `[RequireRole(User)]`. `{ kind, id }`, or both null to unassign — see [Assignee](#assignee) |
| `/issues/{key}/priority` | PUT | **Person only** — plain `[RequireRole(User)]`. `{ priority }` — `"paused"`, `"economy"`, `"low"`, `"normal"`, `"expedited"` or `"emergency"`. Every level but paused floats on the board and is taken first by the dispatcher, most severe first; paused sinks to the bottom and is folded by every dispatch with its own sentence. Setting what it already holds writes nothing; an unknown name is `400` |
| `/issues/{key}/expedite` | PUT | **Person only** — plain `[RequireRole(User)]`. Legacy two-level alias for `/priority` above. `{ expedited }` — `true` sets expedited, `false` sets normal. Setting what it already holds writes nothing |
| `/issues/{key}/pre-approval` | PUT | **Person only** — plain `[RequireRole(User)]`, checked again in the action so it holds with the wall off. `{ level }` — `"none"`, `"express"` or `"preapproved"` — see [Express](#pre-approval). Express needs the same permission a move into the column that feeds implementation needs; preapproved needs an approver. Setting what it already holds writes nothing; an unknown name is `400` |
| `/issues/{key}/one-pr` | PUT | **Open to a key** — `[RequireRole(User, AcceptScope = hatch)]`. `{ value }` — `"on"`, `"off"` or `"inherit"` (the project's default) — see [One pull request](#one-pull-request). A story only: anything else is `400` naming its type; an unknown value is `400`. `409` once work under the story has started. Setting what it already holds writes nothing |
| `/issues/{key}/express` | PUT | **Person only** — plain `[RequireRole(User)]`. `{ express }` — see [Express](#pre-approval). Setting what it already holds writes nothing. Now an alias for `/pre-approval` above, sharing its guards |
| `/issues/{key}/stall` | PUT | Carries no `[RequireRole]` at all — a dispatcher is exactly who leaves this mark. `{ why }` — the sentence an increment leaves behind it saying why it left the ticket where it found it; sets `StalledAt` to now and `StalledWhy` to it, normalised and capped the way claim chatter is. Always writes, so a second mark replaces the first on the row but still leaves its own `stalled` event behind it |
| `/issues/{key}/hold` | PUT | **Person only** — plain `[RequireRole(User)]`, checked again in the action: an agent that could hold its own ticket could park it off the board for the night. `{ held }` — holds it or resumes it. Resuming clears `StalledAt` and `StalledWhy` together, so *Resume now* leaves nothing behind for the dispatcher to fold on, even where nothing had set `held` true. Setting what it already holds writes nothing and does not move `UpdatedAt` — except that `held: false` still clears a stall mark where one is there to clear |
| `/issues/{key}/claim` | POST | Takes the [lease](#claim). `{ runner, playbookId? }` — the playbook the lease works under, stored as `ClaimPlaybookId`, `400` naming it where there is no such playbook; answers with the token, the holder, when it was taken and the TTL, and `stallLapseSeconds` — the same window a claim goes quiet by, in seconds, or `0` when lapsing is off. `409` naming the holder where something live already has it — including the same runner asking twice |
| `/issues/{key}/claim/heartbeat` | POST | `{ token, chatter?, playbookId? }` — refreshes it, `204`. `409` on a token that is not the row's, on a lease that is over, and on one that has gone quiet — see [Claim](#claim). `chatter` absent leaves the carried line alone, `""` clears it, anything longer than the column is truncated rather than refused. `playbookId` absent leaves the claim's playbook alone, and a value overwrites it (`400` for an unknown playbook); a heartbeat cannot clear it |
| `/issues/{key}/claim?token=…&outcome=…` | DELETE | Releases it, `204`. A mismatched token is `409` and clears nothing; an issue holding no claim is `204` and writes nothing. `outcome` (`dropped` or `worked`) is read only where `token` is given and is otherwise optional — an older CLI names none — and a value that is neither is `400`. **With no token at all it is person-only** — an agent that could clear another runner's claim could take a ticket off it mid-increment |
| `/issues/{key}/verdicts` | POST | Records a [check](#check) session's [verdict](#verdict). `{ verdict, findings? }`; open to a key and a person, `204`, no body. `400` where the live claim is not under a check playbook (*"a verdict needs a check playbook covering <KEY> where it stands, and none does"* — also what a claim with no playbook named, or no claim at all, gets), for a `verdict` that is not `pass` or `fail` (*"\"<word>\" is not a verdict - it is \"pass\" or \"fail\""*), and for `findings` over the comment body length (*"findings are at most <n> characters"*); `404` on an unknown key. A second call under the same claim replaces the first. Writes a `verdict_recorded` event and moves nothing. No `GET` |
| `/issues/{key}/merge-check` | PUT | Keeps a runner's [verdict](#merge-check) for one repository. `{ remote, trunk, trunkSha, verdict, branch?, branchSha?, files?, runner, holdsTrunk? }`; answers with what it now holds. Refused, in a sentence, for a remote that does not canonicalise, an unknown verdict, `conflicted` with no files, and `clean` or `conflicted` with no branch sha; `404` on an unknown key. The column is not checked. A verdict that repeats the stored one writes no event, and neither does a change to `holdsTrunk` alone |
| `/issues/{key}/pull-request-state` | PUT | Keeps what the forge last said of the pull request recorded on an issue: its merge state (`clean`, `blocked`, `behind`, `dirty`, `unstable`, `draft` or `unknown`) and its review decision (`none`, `approved`, `changes-requested` or `review-required`), with the head sha it was read at and when. `{ url, headSha, mergeState, reviewDecision, runner }`; answers `204`. Refused, in a sentence, with `409` for an issue with no pull request recorded or a `url` that is not the recorded one, and `400` for a merge state or review decision outside those lists, an empty head sha, or one longer than a sha can be; `404` on an unknown key. The column is not checked, and `runner` is neither checked nor kept. The board stores them; `MergeWork.Judge` reads the merge state and head sha (see [the merge column's judge list](#the-approval-column)) and nothing else on the server does, and the review decision is read by nothing. A read that repeats the stored merge state, review decision and head sha writes no event and refreshes only when it was read |
| `/issues/{key}/landing` | PUT | Keeps what a repository holds for an issue's branch — see [landing](#landing). `{ remote, sha, trunk?, containedBy?, runner? }`; answers with what it now holds. Refused, in a sentence, for a remote that does not canonicalise, an empty or non-hex sha, and `containedBy` given without a `trunk`; `404` on an unknown key. The column is not checked. A write that repeats the stored sha and `containedBy` writes no event |
| `/questions` | GET | Every open question in the house |
| `/search` | GET | `q`, `projectId`, `type`, `statusId`, `createdFrom`, `createdTo`, `closedFrom`, `closedTo`, `limit` (50, max 200), `offset`, all optional and ANDed — see [Search](#search). Done issues are included by default; `statusId` narrows to one column |
| `/plan`, `/plan/{key}` | GET | See [the level above the board](#the-level-above-the-board) |
| `/work/next` | GET | See [the dispatcher](#the-dispatcher). `?heldToken=` names a [claim](#claim) of one's own, so it is not folded past as somebody else's. `?remote=` (repeatable), `?standing=` and `?clones=` declare what the runner has; absent is undeclared and folds nothing. `?mine=true` narrows the pass to the caller's own tickets — see [one more, on `next` alone](#one-more-on-next-alone); `400` when the calling key belongs to nobody. Carries `letGo`, the count of this issue's trailing dropped releases — `0` on a client too old to read it |
| `/work/{key}` | GET | See [the dispatcher](#the-dispatcher). Same `?heldToken=`, `?remote=`, `?standing=` and `?clones=` as `/work/next`. `?mine=` is ignored — somebody who names a ticket has already chosen it. Carries `letGo` the same way. Also carries `failStatusName` — the implementation column's name, the column a failed [check](#check) sends the issue back to, set on every dispatch and null where the board has none — and `lastCheckFindings`, the newest check's findings, set only for an issue standing in that column whose most recent `status_changed` event carries `verdict: fail`, null otherwise |
| `/work/queue` | GET | The same walk `next` takes, reported rather than acted on, and the same three repository flags — see [what a pass skipped](#what-a-pass-skipped). Same `?mine=true`, folding every ticket that is not the caller's own with a sentence naming whose it is. `?asRunner=<name>` answers off that runner's own row instead of the query string — `remote`, `standing`, `clones`, `mine` and `ancestorKey` come from it and may not be given alongside it; a name no live row answers to is a `400` naming it. `clearNote` names a row that is clear only because a stall mark's resume window passed, e.g. `"it stalled 18 minutes ago and resumes itself after 15 minutes"` — null on every ordinary clear row |
| `/work/{key}/hop` | POST | Carries one of seven kinds one column right with no session — see [the hop](#the-hop): an issue [pre-approved](#pre-approval) at `express` or above standing in a column marked `ExpressSkips`; an epic still short of the implementation column, on each move right while it stays short of it; a story or bug under a running epic; a child its parent pulls; a childless issue over its playbook's `BriefLimit`, sent back; a pre-approved issue in review whose branch merges cleanly or needs only bringing current; or a pre-approved parent in review with no pull request and every child done, carried past review into the first terminal column. Takes the same query parameters as `GET /work/{key}` and no `heldToken`: a hop takes no claim. `409` carrying the fold's sentence where the issue is blocked, and `409` where it is clear but not a hop |
| `/work/{key}/merged` | POST | Advances an issue in the review column whose pull request has merged, one column right, with no claim and no session, the same whether the merge was clicked on the forge or made from the terminal with gh pr merge in any mode. `{ url, runner }` — `runner` rides the body unused, for shape parity with the requests that do read it. `409` for a column other than review, for no pull request recorded, for a `url` that does not match the one recorded (trimmed, ordinal), for no column after review, and for a claimed issue; `404` on an unknown key. See [the four paths past the terminal guard](#the-four-paths-past-the-terminal-guard) |
| `/work/{key}/closed` | POST | Records that an issue's pull request was closed without merging (`PullRequestClosedAt`) — a runner's poll calls it once, on the first read, and does nothing further; `MergeWork.Judge` holds an approval on this until a person acts. `{ url, runner }`, the same shape parity as `/merged`. `409` for a column that does not merge the pull request, for no pull request recorded, and for a `url` that does not match the one recorded; `404` on an unknown key |
| `/work/{key}/pushback` | POST | Carries an issue in the merge column back to the review column, with no claim and no session — judged by `MergeWork` itself, off the issue's own stored merge and build checks, not by the caller. `{ url, runner }` — `runner` rides the body unused, for shape parity. `409` for a column that does not merge the pull request, for no pull request recorded, for a `url` that does not match the one recorded (trimmed, ordinal), for a judged kind other than `pushback` (carrying the judge's own sentence), for no review column on the board or one that is the merge column itself, and for a claimed issue; `404` on an unknown key |
| `/work/{key}/landed` | POST | Carries a trunk-landing issue in the review column to the first terminal column after it, with no claim and no session — judged by `LandingWork` itself, off the issue's own stored [landings](#landing) and [trunk builds](#trunk-build), not by the caller. No body. `409` for a column other than review, for a project that lands by pull request (*"this project lands by pull request, so there is no landing to confirm"*), for an issue with a pull request recorded (*"a pull request is recorded on this issue, so it lands by merge and not by landing"*), for no terminal column after review, for a judged kind other than `land` (carrying the judge's own sentence), and for a claimed issue; `404` on an unknown key. See [landing on the trunk](#landing-on-the-trunk) and [the four paths past the terminal guard](#the-four-paths-past-the-terminal-guard) |
| `/work/{key}/verdict` | POST | Applies the [verdict](#verdict) a check session recorded under `?heldToken=` — the runner's own write, after the session, with no gate of any kind; see [Check](#check). A pass moves the issue to where a dispatch from the column it stands in would take it, a first fail to the implementation column, and a second consecutive fail moves nothing and asks a question. `200` with the issue. `409` for an issue holding no live claim (*"<KEY> holds no live claim, so there is nothing for this token to match"*) and for a token that is not the claim's (*"<KEY>'s live claim does not match this token"*); `404` on an unknown key and for no unapplied verdict under that claim (*"no verdict was recorded under this claim"*), which is also what a retried apply gets. Takes `?remote=`, `?standing=` and `?clones=` as `GET /work/{key}` does, and ignores them |
| `/issues/{key}/build-check` | PUT | Keeps a runner's [build verdict](#build-check) for one repository. `{ remote, branch, sha, verdict, failing?, runner, pushedByIncrement? }`; answers with what it now holds. Refused, in a sentence, for a remote that does not canonicalise, an unknown verdict, no branch or sha, and `failed` with no failing checks; a link that is not `http(s)` is stored as null; `404` on an unknown key. The column is not checked. A verdict that repeats the stored one writes no event, and one that moves the row into failed-and-flagged marks the issue stalled with it |
| `/trunk-builds` | PUT | Keeps a runner's [trunk build verdict](#trunk-build) for one repository's trunk. `{ remote, trunk, sha, verdict, failing?, runner }`; answers with what it now holds. The same refusals the build check's write takes, naming the trunk rather than the branch. No event and no question — there is no issue to write either on. `passed` lets an attached bug go |
| `/trunk-builds` | GET | Every stored [trunk verdict](#trunk-build), ordered by canonical then trunk — what a runner's poll reads once a poll, since a trunk carries no issue for the verdict to ride in on |
| `/trunk-builds/{id}/bug` | POST | No class-level scope, the way expedite is cut, but the method itself takes the `hatch` scope, gated at runtime rather than by the attribute, since the attribute cannot vary by project — see [the one edge that is deliberately cut](#the-one-edge-that-is-deliberately-cut). Files the bug a failing trunk's *File a bug* button asks for (HA-95), in the project that binds the repository (lowest project id first, else the lowest-id project that binds it at all) — see [what is waiting on you](#what-is-waiting-on-you). A person's own call is unchanged: expedited, no pre-approval, the board's first column, each overridable in the body. **A key's priority, pre-approval level and column come from the project instead, never from the body** — its own `AutoHealBugPriority`, `AutoHealBugPreApproval` and `AutoHealBugStatusId`, written only through the owner-only `PUT .../landing`. `409` once the row is no longer failing; `400` naming the Projects page when no project binds it, or when a key's project has auto-filing off; a second call answers the bug already filed rather than filing another |
| `/playbook-files` | GET | Every stored [playbook file](#playbook-file), ordered by canonical then trunk, with the path itself alongside them — what the Playbooks page reads once a load, and what a runner's poll reads once a poll to know whether it has already put this sha |
| `/playbook-files` | PUT | Keeps a runner's [playbook file](#playbook-file) for one repository's trunk. `{ remote, trunk, sha, content?, url?, runner }` — `content` the blob's exact bytes, base64-encoded, absent for a trunk with nothing at the path. Hashes and parses the content itself rather than trusting a client-sent verdict. The same refusals the trunk build's write takes, naming the trunk, plus content that is not valid base64 or over the parser's own size limit. No event and no question — there is no issue to write either on |
| `/work/review` | GET | Every issue in the review column the caller holds a checkout of, with its bound repositories and the [merge checks](#merge-check) the board holds — what a runner's poll reads before it asks git anything. `?remote=` (repeatable) and `?standing=` as on the queue; no `clones`, because a poll clones nothing. Not narrowed by a claim, a question, a date or an assignee — see [checking the branches in review](#checking-the-branches-in-review) |
| `/playbooks` | GET | **Reads only.** POST/PATCH/DELETE are plain `[RequireRole(User)]`. Each playbook carries `isCheck`; a person's `POST` and `PATCH` take `isCheck?` (absent is false on create and leaves it alone on patch) — see [Check](#check). A response's `prompt` is the effective text and `defaultPrompt` the prescribed one, or null; a `PATCH` of a blank `prompt`, or of one equal to the prescribed text, hands the row back to its prescribed text (400 *a playbook needs a prompt* where there is none) — see [Playbooks](#playbooks) |
| `/import/preview`, `/import/preview-text`, `/import` | POST | See [the importer](#the-importer) |
| `/utilization` | GET | My own Claude headroom, off my own runners' heartbeats. `204` when no runner of mine has ever reported a reading — see [the battery](#the-battery) |
| `/attention` | GET | What the loop is waiting on a person for — the issues up for review that carry a pull request, how many in that column carry none, every open question still waiting on somebody — not one whose ticket has already shipped, every repository's trunk whose latest build is failing (counted, unlike the rest below), and (listed, not counted) the issues in review whose branch conflicts or whose build has failed, plus how many of those are held back from the pull request list on that account (`reviewsHeldBack`), plus every live runner out of Claude usage (`exhaustedRunners`). One read for all of it — see [what is waiting on you](#what-is-waiting-on-you) |
| `/local-person` | GET | What to call whoever is sitting here, and whether anybody said so. `204` wherever the wall is up |
| `/settings` | GET, PUT | **Person only** — plain `[RequireRole(User)]`, so a key is refused the read as well as the write. The two settings a Hatch install of its own has — see [the token, still](#the-token-still). PUT follows the bulk rule: a field left out is left alone, `""` clears it |
| `/settings/claude-token` | GET | The token itself, wrapped with `SecretProtector` for the wire. The one route in Hatch that hands a live secret back out, and it is cut the opposite way to `/settings` beside it — **a key or a keyless runner may take it**, because its ordinary caller is the container runner's entrypoint (`containers/hatch-runner/`) authenticating a `claude` CLI it starts itself. **Refused outright wherever the wall is up** — every caller, key or person — because a token crossing a network is a different question from one handed to a container on the same laptop. `204` when none is set |
| `/issues/{key}/work-log` | GET, POST | What each session on this issue cost, each entry also carrying its playbook's source and, for a file-sourced row, the trunk, sha and hash it was confirmed against, and which agent ran it (`PlaybookSource`, `PlaybookFileTrunk`, `PlaybookFileSha`, `PlaybookFileHash`, `Agent`); an entry's cost is null when the agent reported no price — unpriced, not `$0` — and a `GET`'s totals carry `unpricedSessions` beside the priced sum. **POST is a key only** — a browser is refused outright, because the only honest writer of a meter reading is the dispatcher that read it. See [the leaderboard](#the-leaderboard) |
| `/work-log/sessions` | GET | The sessions in a range, ranked, with the range's own totals — see [the leaderboard](#the-leaderboard) |
| `/work-log/history` | GET | The same rows folded into equal buckets of time, for the graph |
| `/reports/timeline` | GET | When the work happened: top-level epics active in a range, and the work beneath each, as stretches in the board's active columns. `from`, `to` (default: the 90 days ending now), `projectId`, `createdBy`. Hatch-scoped, open to a key. `400` for an unparseable or inverted bound and for an unknown `projectId` — see [the timeline](#the-timeline) |
| `/reports/cumulative-flow` | GET | How many issues sat in each board column over time, read off the same status history as the timeline. `from`, `to` (default: the 90 days ending now). Hatch-scoped, open to a key. `400` for an unparseable or inverted bound — see [the cumulative flow](#the-cumulative-flow) |
| `/runners` | GET | Every runner heard from lately, most recent first — see [runners on the board](#runners-on-the-board) |
| `/runners/{name}` | POST | The heartbeat: says what this process is, answers with what it has been asked to do. The name is a character off the cast list, or `HATCH_RUNNER`'s override, escaped — a slash in it stays `%2F`. `where` (`host:/path/to/checkout`) rides beside it; a name already the *live* runner at a different `where` is refused with `409` |
| `/runners/{name}` | PATCH | **Person only** — plain `[RequireRole(User)]`. `{ state?, under?, maxRuns?, maxSpend?, untilAt? }`, all strings, the bulk rule throughout. An agent that could raise its own `--max-spend` could raise its own budget |

Two of the filters are worth knowing: `ancestorKey` returns everything below an
issue at any depth — an epic's stories and their tasks in one request — and
`parentKey=` (empty) finds the issues with no parent at all.

The bulk endpoint follows the same rules a single `PATCH` does: a field left out
is left alone, `""` clears one, and re-applying the same edit writes nothing. It
answers with `changed`, `unchanged` and `failures`. That "one refusal does not
take the batch with it" property is not a `try`/`catch` per key — the
single-issue edit path was pulled apart so that everything refusable is looked
up before anything is written, and the bulk path shares it. Naming `projectId`
moves every key through that same staged path a single `PATCH` does, with one
difference worth saying plainly: when the batch names both an issue and one of
its own descendants, the descendant is carried by the ancestor's move and
reported `unchanged` rather than moved a second time.

## The battery

The bar carries one element the board does not: how much Claude *I* have
left, and how long until it comes back. It is read from every page without
opening another app, and it is drawn from `GET /api/hatch/utilization`.

**Absent is a supported state, and it is the default.** A person no runner has
ever reported for gets a Hatch with no battery — no element, no placeholder, no
reserved space, and nothing anywhere reporting an error. A fresh install is in
that state, and so is anybody who has never run a runner. This is an
enhancement to a tracker, not a dependency of one, and the `204` the endpoint
answers with is what says so.

### Where the reading comes from

Not a token Hatch holds and reads over HTTP — the session's own stream. Every
runner already knows the account its sessions spend: the console draws bars
for it, off the session's own `rate_limit_event` events, and this is that same
reading, carried one hop further. The runner reports it on the heartbeat it
already sends — `Usage` and `UsageReadAt` beside `Exhausted` in
`RunnerHeartbeatRequest` — and Hatch keeps it against the person the calling
key belongs to, the same resolution `--mine` already takes
(`IActorDirectory.PrincipalAsync`). Bob's runner's numbers never appear under
Alice's name, because each is kept on Bob's and Alice's own rows and the
endpoint only ever answers the caller's own.

This is HA-120's answered question — *session stream* over a Hatch-held
battery — applied to the nav: it costs a number that only moves while a
session runs, and it needs no credential pasted anywhere. What it buys is a
battery that works on a fresh install, for anybody with a runner of their own,
including a friend on `do-my-work` spending their own account.

**A second reader, since HA-209: [economy's own gate](#expedite).** The
dispatcher asks the same freshest-whole-reading this widget draws, through the
same `IActorDirectory.PrincipalAsync` resolution, to judge whether the account
an unattended pass would spend has a reserve left on every window before it
will touch an economy issue. Nothing about the endpoint or its shape changes
for this — `GET /api/hatch/utilization` answers exactly as it always has; the
dispatcher is simply a second caller of the same `Utilization.Of`.

**A third reader, since HA-226: [low's own gate](#expedite).** The same one
reading, resolved once per pass whether a candidate sits at economy or low or
both, is judged a second way — the session window alone, nothing held in
reserve — before an unattended pass will touch a low issue. Nothing new about
the endpoint here either: `Utilization.Of` still has exactly one caller
outside this widget, read once and judged twice.

**Between sessions, HA-173 asks a second source.** A session's own stream is
silent while the loop is idle, so the reading would otherwise sit frozen from
the last time a session ran. At the top of every idle pass the runner asks its
own CLI's `/usage` directly, over the same login, in a throwaway temp
directory rather than any checkout — no project settings, no hooks, and
`--max-turns 1` so a CLI that mistakes it for a real prompt cannot spend more
than one turn finding out. It answers the same two windows the stream does,
plus a scoped weekly window named by the account and a fourth Hatch has never
carried before, `extra` — the account's own extra-usage balance. The CLI's own
schema calls this shape experimental, so a login it cannot read from — no plan,
or a token without the profile scope — gets nothing back, and the panel simply
keeps whatever it already knew. A reading from either source still replaces
the whole of what the heartbeat sends, never a per-window merge, so the number
that used to move only while a session ran now moves while the loop is merely
up.

Where somebody has two runners, **the freshest whole reading wins**, not a
per-window merge. One person has one Claude login, so two runners are two
readings of one account, and stitching a session percentage from one against a
weekly one taken an hour apart would be showing a number nobody's account ever
had.

### What the endpoint answers

`GET /api/hatch/utilization` is `[RequireRole(PersonRole.User, AcceptScope = "hatch")]` like the
rest of the module. It answers who is asking with
`IActorDirectory.PrincipalAsync` — no principal (the wall is up and the caller
is a key belonging to nobody) is `204`, the same as it is for nobody having a
reading at all: this is a nav widget, not a dispatch that was asked to do
something, so an ownerless key gets the same quiet nothing rather than the
refusal a `--mine` pass gives. Otherwise:

```json
{
  "state": "ok",
  "readAt": "2026-09-07T08:12:03Z",
  "limits": [
    { "window": "session", "label": "Session", "percent": 17,
      "resetsAt": "2026-09-07T12:00:00Z" }
  ]
}
```

Every name in it is Hatch's own — the runner already translated the account's
spelling before it ever reached the wire, so there is nothing upstream left to
rename underneath this endpoint.

- `state` is the whole of the degraded story, and there are only two of them. `ok`
  — read within the freshness window (five minutes). `stale` — outside it, and
  still the last thing this account is known to have reported; `readAt` gives
  its age. There is no third state: a reading is kept, not swept, so `unknown`
  ("the account could not be reached") went with the account nothing reaches
  any more.
- `window` is `session`, `weekly`, `weeklyModel` or `extra`, in the order the
  runner reported them.
- `label` is what the row is called on screen — `Session`, `Weekly`, `Extra
  usage`, or a per-model weekly window's own name. A session stream's scoped
  window arrives with no display name of its own and reads as `Weekly
  (model)`; the CLI's own `/usage` report names it, so the label there is
  `Weekly (<the account's own name for the model>)`. **No model name is
  written down in this repository.**

### The colour, and the two bars

Every row's colour, and the battery's own, is decided on the client from its
**pace** — the share of the window's allowance it is on course to have spent
by its own reset — rather than the percentage alone. Pace is
`used / max(gone, GRACE)`: the percentage spent divided by the share of the
window's time gone, with `GRACE` a floor on that share (the first fifteen
minutes of a five-hour window) so the opening minutes of a window cannot
project to several times the allowance before a rate means anything. `100` is
level — the window refreshes at the very moment the allowance runs out; below
it there is headroom left at the reset, above it the allowance goes first. The
ramp has five control points over this projected percentage, interpolated
linearly in sRGB one shade per whole projected percent: bright green at 0
("allowance to spare"), green at 60 ("well ahead of the reset"), yellow at 90
("just ahead of the reset"), orange at 100 ("level with the reset"), and red
at 150 ("spending ahead of the reset")
(`src/Hatch.Web/apps/hatch/src/lib/utilization.ts`). The endpoint sends no
colour and no tone: a ramp on the client beside a three-tone decision on the
server would be two answers to one question, so the percentage and the reset
instant are the whole of what crosses the wire, and pace is computed from
them alone.

The battery takes the colour of the **worst window** — the highest pace among
the rows — never the `extra` row's: a spent credit line is a monthly budget
the operator chose to buy, not a window the account will refuse on, and
painting the nav red over it would say "stop" about an account that can still
run. A window standing at 100% used at the very moment it resets is not bad
news on its own — it is a window spent exactly as intended, its allowance and
its clock running out together — so only spending faster than the window
refreshes reddens it. The battery's tooltip and accessible name say in words
what the colour says in colour, and name the window that decided it whenever
it is not the session's own.

A window whose reset instant has already passed reads as **not started**: 0%
used, pace 0, the ramp's brightest colour, and a phrase saying it has reset,
rather than the stale percentage sitting there in whatever colour it last
was.

Opened, every row in the panel is two bars on one scale rather than a
percentage and a `resets in` clause left for the reader to do the arithmetic
on: the share of the window's allowance spent, and the share of its time
that has gone. A usage bar that runs past the time bar says the account is
spending ahead of the window's cadence; one that stops short says it is
behind. A row with no reset instant draws its time bar as unknown — a dashed
track — rather than empty or full, and the `extra` row draws no time bar at
all: a monthly credit limit is not a window whose length Hatch knows.

### How old the reading is

There is no upstream to poll, so there is no cache, no freshness floor, no
failure backoff and no semaphore to collapse concurrent reads — all of that
belonged to a reading Hatch fetched, and this one arrives on a heartbeat a
runner was already sending. One number, `UsageReadAt`, and arithmetic against
it at the moment somebody asks: `ok` inside five minutes of it, `stale`
outside. The Refresh button in the modal re-reads Hatch's own row; there is
nothing upstream left for it to bypass.

### The token, still

`ClaudeSubscriptionToken` — the secret-valued site setting on Hatch's own
**Settings** page — no longer feeds the battery, but it has not left. It is
what the *container* runner's entrypoint (`containers/hatch-runner/`) reads to
authenticate the `claude` CLI it starts, via `hatch runner-claude-token` and
`GET /api/hatch/settings/claude-token`. An install whose runners all run on
somebody's own machine needs none of this. It is stored the way the Immich key
and the Anthropic key already are: obfuscated at rest, redacted on read, never
leaving the API — and, like every other credential in Hatch, it is the
operator's own to supply ([`docs/ethos.md`](ethos.md)).


## What is waiting on you

Beside the battery, at the right end of the same bar, is the other thing the
bar is for: whether the loop has stopped and is waiting on a person. Two
things stop a night — a pull request nobody has reviewed, and a question nobody
has answered — and the server already knows both. The control is quiet while
neither is true and loud the moment either is, and pressing it hands over the
links that unblock them.

The panel draws two groups, in this order: **Human** — trunk builds that fail,
then pull requests to review, then pull requests approved but not merging,
then questions to answer — and **Agent** — branches that conflict, then
branch builds that fail. A pull request only ever appears under one of the two:
one the loop is still working through a conflict or a red build on is not a
person's to look at yet, so it moves out of the pull-request section and into
the group naming what is holding it back, rather than sitting in both. A
merged pull request leaves the review half on its own: advancing the issue
takes it out of `/api/hatch/work/review` and out of `/api/hatch/attention`
both, so there is nothing left here for a person to look at either.

Each row under *Pull requests to review* also carries two small icons, after
the title, with no behaviour of their own — a click anywhere on the row,
icon included, opens the pull request. The **build** icon is one of three
states named in words as well as colour: *success* when every counted
repository's [build check](#build-check) on the branch's current tip passed,
*failure* when any has failed or is still running with a check that has
already failed, and *no results* for anything else — no verdict, a build
still running with nothing failed yet, no checks ran, or a verdict about an
older sha than the branch's tip. Because a pull request held back into
*Builds that fail* never reaches this list, *failure* is drawn and tested but
never actually seen here. The **up to date** icon says whether the branch
already holds the trunk's tip, from the same [merge check](#merge-check) that
answers the section above — naming the trunk as a runner reported it (*Up to
date with main*, *Behind main*), or *not checked against the trunk yet*, in
grey, when no runner has said.

A third line is drawn below the title and the pull request's own line, only
when the pull request changes `.hatch/playbooks.yaml` - the same sentence
the issue page's own chip shows, *"this change also rewrites the
instructions agents follow - `<path>`"* - since a person about to merge
from here is the same person who would see it there.

Unlike the battery, **it always draws something**. "Nothing is waiting" is an
answer worth having, and it is the one it gives most of the time.

`GET /api/hatch/attention` answers every list in one read, carrying
`[RequireRole(PersonRole.User, AcceptScope = "hatch")]` like the rest of the module:

```json
{
  "reviews": [
    { "key": "AER-12", "title": "A story that landed", "type": "story",
      "pullRequestUrl": "https://forge.example/pulls/12",
      "buildState": "success", "holdsTrunk": true, "trunk": "main",
      "playbookFilePath": null }
  ],
  "inReviewWithoutPullRequest": 3,
  "questions": [ ... ],
  "conflicts": [
    { "key": "AER-14", "title": "A story whose branch stopped merging", "type": "story",
      "pullRequestUrl": "https://forge.example/pulls/14",
      "checks": [ { "canonical": "forge.example/owner/repo", "trunk": "main",
                    "files": ["src/a.cs", "src/b.cs"], ... } ] }
  ],
  "reviewsHeldBack": 1,
  "trunkBuilds": [
    { "id": 7, "remote": "git@forge.example:owner/repo.git", "canonical": "forge.example/owner/repo",
      "trunk": "main", "sha": "1a2b3c4d5e...", "verdict": "failed",
      "failing": [ { "name": "CI", "url": "https://forge.example/checks/CI" } ],
      "bugIssueKey": null }
  ],
  "exhaustedRunners": [
    { "name": "host:/checkouts/one", "where": "host:/checkouts/one", "exhaustedUntil": "2026-09-28T23:40:00Z" }
  ],
  "approvedHeld": [
    { "key": "AER-15", "title": "A story stuck in the merge column", "type": "story",
      "pullRequestUrl": "https://forge.example/pulls/15",
      "sentence": "conflicts with main" }
  ],
  "approvedWaiting": [
    { "key": "AER-16", "title": "A story about to merge", "type": "story",
      "pullRequestUrl": "https://forge.example/pulls/16",
      "kind": "merge", "sentence": "merging now" }
  ],
  "landingHeld": [
    { "key": "AER-17", "title": "A story landed on the trunk",
      "type": "story",
      "sentence": "main's build on 1a2b3c4 failed after it landed" }
  ]
}
```

`approvedHeld` is every merge-column issue the judge called **hold** or
**pushback** — nothing in that column is an agent's to fix, so a row
there is a person's, every time. `approvedWaiting` is everything else —
**merge**, **update**, **pending** — the loop is still progressing those
on its own. `attentionCount` counts the first group towards the badge and
leaves the second out, for exactly that reason. The empty state, when
nothing is held, reads *Nothing approved has stopped merging.* The issue
page's own chip draws only for a **hold** verdict, never a **pushback**
one, even though this panel counts both the same way.

`landingHeld` is the same list for a project that [lands on its
trunk](#landing-on-the-trunk): every issue in review with no pull request
whose `LandingWork.Judge` says **hold** — the trunk's build failed after the
commit landed, or no checks ran there on a project that requires them. Only
the holds: a landing still waiting on a runner to see it or read its build
(**pending**), or one good to go (**land**), is the loop's and not a person's,
and the list leaves it out the way `approvedWaiting` is left out of the badge.
The row carries no pull request url, since it has none; `sentence` is the
judge's own. `attentionCount` counts it, for `approvedHeld`'s reason: nothing
in the loop will fix a trunk that is red. The empty state reads *Nothing landed
has stopped short of done.*

Pre-approval reaches this panel's review half too, the same way it already
reaches the merge column's. A row in `reviews` that is
[pre-approved](#pre-approval) and whose branch the judge would merge, bring
current, or is still progressing on its own is not listed here at all - it is
the loop's to carry, not a person's to look at, the same reasoning
`approvedWaiting` already rests on. One the judge still holds - a closed pull
request, an ambiguous or missing branch, a stale unread tip - stays in the
list, carrying `heldSentence`: the same sentence the merge column would print
for it. `heldSentence` is null for every row that is not pre-approved, and for
a pre-approved row that reaches this list for any other reason, since neither
case is held. `inReviewWithoutPullRequest` carries the same carve-out forward:
a pre-approved parent with no pull request and every child done - the close
hop's own row - is not counted either, exactly as it is not a row in
`reviews`. A trunk-landing issue is carved out the same way once its landing is
recorded: it was never going to have a pull request, and where it stands from
there is `LandingWork`'s to say, in `landingHeld` above or in the loop's
hands.

One endpoint rather than two, and that is the design rather than a convenience.
The control's loudness is a single number; two polls can be a poll interval
apart, and that disagreement shows on screen as a lit control whose panel is
empty. After that happens twice nobody reads the control again.

Neither half restates a rule that already lives somewhere:

- **Which column is the review column is measured, not named.** It is the one
  immediately left of the first terminal column — `Columns.AwaitingReview`, the
  same answer [the dispatcher](#the-dispatcher) works right to left by. Renaming
  `In Review` changes nothing, reordering the board changes nothing, and a board
  too short to have one answers with no rows rather than an error. The browser
  deliberately does not derive this from `/board`'s status list: that would be
  one rule written twice, in two languages, and the two would drift.
- **Waiting means what it means everywhere else.** `questions` is the same
  call `/questions` answers with — `Questions.Waiting`, an open question
  whose issue stands outside a terminal column — so the count on the bar,
  the badge on a board card, and the list somebody actually sits down to
  answer cannot disagree. `Questions.Open` is still the wider record of
  every question ever asked; this panel, like the board and the meters,
  deliberately reads the narrower one.

### The trunk build that fails

`trunkBuilds` is the fifth list, and the one build-shaped thing here that
**does** light the control. It is every stored [trunk build](#trunk-build)
that is `failed`, or `pending` with a check that has already failed, ordered by
canonical then trunk, whether or not there is a review column at all — a
repository's trunk is checked whether or not anything of its is in review, and
the section reads it the same way. The panel draws it first in the Human half,
as *Trunk builds that fail*, naming the trunk as the runner reported it and the
repository, the short sha, and every failing check as a link that opens its
job in a new tab.

**It counts, unlike a branch's conflict or its failing build**, because the
reasoning that keeps those two quiet does not hold here — except that it now
holds partway: nothing is dispatched at a trunk, so a failing one has no agent
already on it, but a row reaching this list may already have been re-run, and
may already be filing itself, with no agent and no person either
([below](#checking-the-branches-in-review)). What a person still does is press
**File a bug** where nothing has, which is what the badge exists to surface.

Each row carries a button, unless it already carries a bug's key as a link in
its place — `POST /api/hatch/trunk-builds/{id}/bug`
([the one edge that is deliberately cut](#the-one-edge-that-is-deliberately-cut)).
A person pressing it is unchanged: filed in the shape a bug against a failing
trunk was always filed by hand, from the forge's own page — a bug, titled
*Build failing on &lt;trunk&gt;*, described with the repository, the sha and a
link to each failing check, expedited, with no pre-approval, in the board's
first column, in the project that binds the repository as its primary, lowest
project id first, else the lowest-id project that binds it at all, else
refused naming the Projects page. A key may take the same route instead of a
person — the runner's own poll does, the moment it reads a trunk failure fresh
and is not re-running it ([below](#checking-the-branches-in-review)), where
the project settings decide: a project nobody has touched keeps
`AutoHealBugEnabled` off, which is today's behaviour, so a row of its sits here
with its button and nothing else having happened to it. A project whose owner
has turned it on names, too, the priority, the pre-approval level and the
column the bug is born in — default emergency, express and the column that
feeds implementation — and a key filing it takes exactly those three, never
the body it would otherwise send. The bug stays attached across new failing
shas — a fix that fails again
is the same outage — and is let go the moment a later build on the trunk
passes, so the next failure offers the button again. A second press, a second
person, or the poll finding one already attached, gets the bug already filed
rather than a second one.

### The branch in review that has stopped merging

`conflicts` is the third list, and the one that does **not** light the control.
It is the review column's issues whose [merge check](#merge-check) says
`conflicted` in at least one repository, in the column's own board order,
whether or not they carry a pull request — the branch conflicts either way —
each with only the repositories that conflict, so a clean one beside it is not
part of the sentence. The panel draws it as its own section, after the pull
requests, and the issue page draws a chip beside the pull request's naming the
trunk and the files.

It is listed and not counted because a conflict is the loop's to fix. One it
cannot fix becomes a stall, a stall is a question, and a question already lights
the control — so counting the conflict as well would light it twice for one
problem, and for the ordinary case, one the loop fixes before anybody looks, it
would light it for nothing. `attentionCount` leaves it out and its test says so.

A conflicted issue that carries a pull request is also pulled out of `reviews`
and counted instead in `reviewsHeldBack`, never appearing in both lists at
once. A branch that does not merge is not ready for a person to review, and the
loop is already the one working on it — a pull request sitting in the Human
group for that reason would be a link with nothing to do at the other end of
it.

### The issue in review whose build has failed

`failingBuilds` is the fourth list, and it too does **not** light the control. It
is the review column's issues whose [build check](#build-check) says `failed`,
or `pending` with a check that has already failed, in at least one repository,
in the column's own board order, each with only the repositories that are
failing. The panel draws it as *Branch builds that fail*, after the conflicts,
and the issue page draws a chip beside the pull request's naming the failing
checks, each linked — marked as still running while the verdict is `pending`.

The reasoning is the conflicts' own: the loop fixes a failing build, and one it
cannot fix becomes a question, which already lights the control. Counting it as
well would light the control twice for one problem, and for the ordinary case —
one the loop fixes before anybody looks — for nothing. `attentionCount` leaves
it out and its test says so.

The same hold-back applies here: an issue with a failing build that carries a
pull request is pulled out of `reviews` and counted in `reviewsHeldBack`
instead, for the same reason a conflicted one is — a red build is not ready
for a person, and the loop is already on it.

### The issue in review with no pull request

`inReviewWithoutPullRequest` is the one number here that is not a row, and it is
there for a specific failure. An issue can sit in review for weeks with no pull
request and no prospect of one — a phase story, something the operator is
judging by hand. A control that counted those would be permanently loud, and a
permanently loud control is one nobody reads after a week.

So they never make it loud, and they are not silently dropped either. The
section's empty state has three wordings, and the counts are what pick between
them: *Nothing is up for review*; *3 issues are in review with no pull request
recorded*; *1 pull request is waiting on the loop*, for `reviewsHeldBack`
alone. The last two combine into one sentence when both counts are nonzero, so
a ticket whose agent forgot `hatch pr` and a pull request the loop is still
clearing a conflict on are both visible at once, without either shouting. A
ticket with no pull request at all is never counted in `reviewsHeldBack`: that
count is only ever about a pull request the loop is sitting on, not about a
ticket with nowhere to review it in the first place.

The control keeps itself current on a sixty-second poll and on
`visibilitychange`, the way [the battery](#the-battery) does, and a read that
fails leaves the last answer drawn and says nothing at all. The bar is not
where a fetch failure gets announced.

### Runners out of Claude usage

`exhaustedRunners` is the fifth list, and the one that lights a **dot** rather
than growing a pill: it is not a count of anything to fix, only a fact about
an account, so it does not touch `attentionCount` or `attentionTone`. Each
entry is a runner's name, its `host:/path`, and when it expects to reset —
every runner still being heard from whose own heartbeat says it is out of
usage (see [Runners on the board](#runners-on-the-board)), soonest reset
first. A runner that has gone quiet is not listed even if its last-known reset
time has not passed: silence beats everything else this endpoint says about a
row.

The panel draws this section **above** *Human*, and only when it has rows -
unlike the four above it, which always draw something. This one is an alert
about a condition that ends by itself, and the report this shipped from asked
for it to disappear the moment it does, which it does on its own: a runner
whose reset instant has passed, or one that has stopped heartbeating, simply
is not in the list the next time the panel polls - nothing has to notice and
nothing is written. The dot on the nav control's glyph does not change its
width or its review-and-question pills, and its accessible name gains a phrase
like "1 runner is out of Claude usage". Each row links to the Runners page,
where the same runner's own row reads `Out of usage until 7:40pm`, in the
viewer's own time zone - unless it has stopped heartbeating, which still reads
*Gone* regardless of what its last heartbeat said.

Nothing a person presses sets or clears any of this - there is no button for
it anywhere. Only a runner's own heartbeat says it is out of usage, and a loop
heartbeat that says it is not clears the board's record at once - which is how
a person overrides the wait: restarting the loop by hand starts a fresh night
that carries nothing forward, and its first heartbeat clears whatever the
board remembered.

## The leaderboard

The battery says how much Claude is left. The leaderboard says **what the nights
cost, and on what** — which is a question account-wide utilization cannot answer
at all, because it is honest and anonymous and no arithmetic over it can say
which epic ate the evening.

Every number on the page comes from one table: `hatch.work_log_entries`, the row
the dispatcher writes at the end of every unattended increment. It carries the
session id, the issue, both instants, the duration, the turns, the four token
counts, the notional USD — null when the agent reported no price — whether
the run errored, and what the session said it did in its `work-log` block.
For a repo-sourced project's session, the row
also carries which of the three layers its playbook came from, and, for the
file, the trunk, the sha and the hash it was confirmed against at spawn -
null on a table-sourced row and on a row an older runner wrote. It also
carries which agent ran it, an id like `claude` - null on a row a CLI built
before this shipped wrote, and never guessed by the server. **No Claude
credential is anywhere in this path.**
A leaderboard on an installation with no subscription token is the whole
leaderboard rather than a reduced one.

The page is at `/leaderboard` in the Hatch app, under **Agents** in the primary
nav, because it answers what the nights cost. It holds three things over one
window and one optional issue filter:

- a **ranking** of the top-billing sessions, captioned in the house's own voice.
  A leaderboard of most expensive agent runs is funnier than it is useful, and
  being funny is how it gets read; it should still be exactly right.
- a **table** of the sessions in the window, filterable to one issue and
  everything beneath it, sortable by tokens, by notional USD or by when it ran.
- a **graph** of spend over time, one bar per bucket.

Tokens are the headline everywhere and notional USD is beside them, sortable —
so the day an account is billed per token, the money becomes the headline and
nothing has to be rebuilt.

An unpriced session draws as a dash with a title saying the agent reported no
price, never `$0` — a zero on a leaderboard would be a lie about money. A sum
over mixed rows reads as the priced total plus how many were left out (`3
sessions · 1.4M tokens · $3.41 · 2 unpriced`, or, with none priced, `3
sessions · 1.4M tokens · 3 unpriced`), the cost sort puts the unpriced rows
last rather than first or scattered, and a bucket on the graph names its own
unpriced count beside its total.

### The two reads behind it

```
GET /api/hatch/work-log/sessions?from=&to=&ancestorKey=&sort=&limit=
GET /api/hatch/work-log/history?from=&to=&ancestorKey=&bucket=&offsetMinutes=
```

They take the same range and the same `ancestorKey`, and **`ancestorKey` means
the same thing on both**: the issue itself *and* everything beneath it at any
depth, from `Rollup.DescendantIdsAsync`. That is deliberately not how the same
word reads on `GET /api/hatch/issues`, and the reason is that a planning session
run against an epic is money no child holds. It is also what keeps the ranking,
the table and the graph from ever describing different populations.

Four rules worth knowing before reading either:

- **A session is in the range when `from ≤ EndedAt < to`**, half-open on both
  reads, so a session on a boundary lands the same way in the table and in the
  graph. `EndedAt` is the instant the spend was known.
- **The totals cover the whole filter, not the page that came back.** `sessions`
  answers at most `limit` rows (100 by default, 500 at the most) and folds every
  row in the filter for its totals, so a capped table adds up honestly — and a
  caller tells the two apart by comparing `totals.sessions` with the rows it
  got. The page says so on screen rather than leaving a larger total to read as
  a bug.
- **`firstSessionAt` and `lastSessionAt` ignore the range** and respect the
  filter. They are what tell *nothing has ever been logged here* apart from
  *nothing ran in the window you asked for* — two different sentences, and the
  page says whichever is true once rather than in each empty section.
- **`sessions` uses the range as given; `history` snaps it outward onto the
  bucket grid** and names the bucket size it chose. The page's range presets are
  computed on that same grid, so for every range it offers the two reads
  describe one window and their totals are equal. A `from`/`to` typed into the
  URL by hand may be off-grid, and then the graph covers a little more than the
  table — so the page labels its window from what came back rather than from
  what it asked for.

An errored session's spend counts in all of it. It ran, and it was billed for
running; `totals.errors` says how many, and the table marks them.

### The graph

`history` folds the same rows along a time axis instead of the hierarchy, and
the page draws one bar per bucket, oldest at the left.

- **The page asks for no bucket size.** The server chooses one from the length
  of the range — hourly up to two days, daily past it — and names it back, and
  the graph labels its axis from the answer. A caller that asked in days and got
  hours has to say hours.
- **A bucket in which nothing ran is drawn as a zero, not as a gap**, and its
  slot stays hoverable. A poll's history has gaps because a missing reading
  means nobody looked; a work log has none, and drawing an empty hour as a break
  would assert an absence where there is a measurement.
- **Tokens or notional USD, one at a time, never on a shared axis.** They differ
  by six orders of magnitude and a second y-axis would draw two lies crossing,
  so the control rescales the whole graph. The choice is in the URL as `measure`
  with the rest of the page's state.
- `offsetMinutes` puts a daily bucket on the reader's midnight. An overnight run
  split across UTC midnight is two half-nights nobody worked.

It is hand-cut SVG in the house's own tokens — the Hatch app carries no charting
dependency, and the geometry lives in a tested function rather than in the
component.

**Nothing on this page writes**, and the absence is the guarantee rather than a
convention: there is no control that could, and the browser API client has no
function that could. A work log row is written by the dispatcher with an API
key, and `POST /api/hatch/issues/{key}/work-log` refuses a person outright — see
`IssueWorkLogController.NotAKey`.

## Reports

The board says what is where. The reports say **when it was** — questions about
the board's own history, which the board as it stands cannot answer because it
holds only the present column of every issue.

Reports is a top-level area of the Hatch app: a **Reports** link in the primary
nav between **Plan** and **Agents**, and an index at `/reports` with one card
per report, each stating the question it answers. There are two reports: the
Timeline and the Cumulative flow. A new report is one more entry in that index
and a route; neither report gets a nav entry of its own, and the index card
stays the only way in.

### The timeline

The Timeline answers **when each epic was in flight, and how long its work sat
in each column**: one row per top-level epic, drawn as coloured stretches along
a time axis, with the work beneath it a click away. Everything on it comes from
two tables that already exist — the issues, and the `status_changed` events of
the [issue event](#issue-event) trail — and nothing is stored for it. The page
is at `/reports/timeline`.

#### What "active" measures

A bar is drawn for the time an issue spent in an **active** column, and active
is measured off the board's shape rather than named: every column after the
first that is neither terminal nor deferred (`TimelineReport.BoardIndex`). On
the example board that is the columns from Breakdown through In Review. The
first column is where an idea waits to be written, a terminal column is where
work ended, and a deferred column is a siding, so a stretch in any of the three
is a **gap in the bar**, not a segment. A deferred stretch between two active
ones leaves them two segments.

It is not a column name because the stock names are seeded by migrations and
every column can be renamed, inserted or reordered (see [Status](#status)), so
a rule that named one would be true of exactly one installation. Reading the
flags and the order is true of any board.

#### The rebuild, and its one hole

An issue's history is rebuilt from its `status_changed` events, oldest first (by
`At`, then by event id). The column before the first event is that event's
`from`; each `to` holds until the next event; an issue with no such event has
been in its current column since it was created. Consecutive stretches in one
column are merged, and a stretch that ends no later than it starts is dropped.
The hierarchy is not rebuilt from history: it is today's tree.

An event is matched to a column in one of two ways. An event carrying
`fromId`/`toId` is resolved **by id only**; the name is not tried, since a
deleted column's name may have been reused. An event from before the ids were
written has names only, matched exactly against the board's current names.

That is the one hole. A stretch that matches no current column — the column was
renamed since, and the event predates the ids — cannot be placed. It is treated
as not active, counted in `unresolved`, and footnoted on the page ("*N* column
changes could not be placed, because the column has since been renamed."),
rather than drawn as a bar the report cannot stand behind. Events written since
the ids landed are immune to renames, so the hole closes going forward and
never reopens. An issue that is deleted takes its events with it and is absent
from the report, as everything the trail would have said about it is (see
[deferred on purpose](#deferred-on-purpose)).

#### The answer

```
GET /api/hatch/reports/timeline?from=&to=&projectId=&createdBy=
```

`from` and `to` are **instants** (`2026-09-12T17:00:00Z`), not dates. With
neither given the range is the 90 days ending now; with only `from` absent it is
the 90 days before `to`. Equal bounds are accepted. `from` is clamped forward
to the first issue's creation when the range reaches back before anything
existed, so a range wider than the board's history does not draw an empty
stretch. The `400`s are an unparseable bound, an inverted one (`from` after
`to`), and a `projectId` that names no project. The route is open to a person
or to a key with the `hatch` scope.

It answers `{ from, to, epics, other, creators, unresolved }`. `from` and `to`
are the range as the server resolved it. Each entry in `epics` is `{ issue,
createdBy, isLeaf, segments, children }`: `issue` is a card, each segment is
`{ statusId, from, to }` clipped to the range with `to` null while the stretch
carries on past its end, and `children` nest the same shape. `other` is null
when there is no loose work. `creators` is the distinct creating names of the
rows the filters have not yet narrowed, for the picker. `unresolved` is the
count above.

The fold is the server's for the reason `/plan` and `/work-log/history` are:
the events table is unbounded, a browser that pulled it down would be running an
export, and a number the browser derived would disagree with `hatch api GET`
the first time a column was added. `TimelineReport` takes rows and changes and
answers shapes, with no database and no clock; `ReportsController` loads them
and decorates the result with cards.

#### Who is in it

- **A top-level epic** — an epic with no epic above it — is in the answer when
  one of its **own** active stretches meets the range. Its children are listed
  only with a stretch of their own, or descendants that have one. An issue is
  drawn, and counted, once.
- **A loose root** — a parentless non-epic — is in when its own stretches, or any
  descendant's, meet the range.
- **Other** is one row under which every loose root stands. Its `issue` is a
  stand-in card with the key `other` and no real issue behind it, and its bar is
  the hull of every segment beneath it: the earliest start to the latest end,
  open if any is open. It exists so the report cannot quietly hide work filed
  without a parent, the reason the Plan page has *Under no epic*.

#### Filters, and hiding

The two are different things and live in different places.

**Filters belong to the server.** `projectId` and `createdBy` (an exact name)
narrow the **top-level rows only**; children ride with their root whatever
project they were filed in. `creators` ignores the creator filter, so the picker
still offers the people the filter hides, but is limited to roots that made the
answer; `unresolved` sums only the roots left after narrowing. On the page they
are in the URL as `project` (a key) and `creator` (a name), beside the window's
`from` and `to` and a `range` shorthand (`1d`, `1w`, `2w`, `1m`, `3m`, `6m`,
`1y`, `all`). With
none of them the page opens on the 90 days ending at the reader's **next
midnight** — the server's own default ends *now*, and the page's alignment to
the reader's day is the browser's.

**Hiding belongs to the browser.** Each top-level row has a **Hide** button, and
the hidden rows are listed in a **Hidden (n)** disclosure with **Show** beside
each and **Show all**. The set is a list of keys in `localStorage` under
`hatch.timeline.hidden`, per browser, and is deliberately not in the URL, so a
pasted link never silently hides anything. Which rows are expanded is page
state and not in the URL either. A hidden row is skipped with its children
beneath it; the server is not asked again.

#### The drawing

It is hand-cut SVG and CSS in the house's own tokens, with no charting
dependency; the geometry lives in a tested function (`lib/timeline.ts`) rather
than in the component. Each segment is painted in its column's colour from the
Statuses page, so a recoloured column recolours the history. The axis ticks fall
on the reader's midnights, Mondays and month starts, and a marker shows today
when today is inside the range.

**Nothing on this page writes**, and the absence is the guarantee rather than a
convention: there is no control that could, and the browser API client has no
function that could. There is **no CLI subcommand** for it either; a script
reaches the same answer with `hatch api GET /api/hatch/reports/timeline?...`,
which is the [API surface](#api-surface)'s row for it.

### The cumulative flow

```
GET /api/hatch/reports/cumulative-flow?from=&to=
```

The same question the timeline answers about one issue at a time, asked about
the whole board instead: **how many issues sat in each column, at each
instant**. `from` and `to` are the timeline's own bounds — instants, the 90
days ending now by default, the same `400`s for an unparseable or inverted
range. There is no `projectId` or `createdBy`: a column's count is a fact
about the board, not about any one project or creator.

It answers `{ from, to, step, columns, points, unresolved }`. `columns` is the
board's own columns in board order — every status that is not deferred, so a
deferred column contributes to no band. `points` is one `{ at, counts }` per
grid step from `from` to `to`, oldest first, `counts` parallel to `columns`;
on a board with no issues it is `[]`. The grid's step is the finest of hour,
day or week whose point count stays at or under the 1,000-point cap
`/work-log/history` already uses, falling back to week rather than refusing a
range that outgrows even that. `from` is clamped forward to the first issue's
creation, the same as the timeline's own `ClampStart`, so a range reaching
back before anything existed does not draw an empty stretch. `unresolved`
counts stretches the same way the timeline's does: a stretch naming a column
no longer on the board, because it was renamed since and the event predates
the ids — see [the rebuild, and its one hole](#the-rebuild-and-its-one-hole).

The page is at `/reports/cumulative-flow`: one band per column, stacked
bottom-up with the board's last column at the baseline, each in its column's
own colour and named in a legend above the chart. **Nothing on this page
writes**, and the absence is the guarantee rather than a convention, as it is
for [the timeline](#the-timeline). There is **no CLI subcommand** for it
either; a script reaches the same answer with
`hatch api GET /api/hatch/reports/cumulative-flow?...`, which is the
[API surface](#api-surface)'s row for it.

## The overnight

The leaderboard says what the nights cost. The overnight page says **what the
loop actually landed** — the one read nothing else on the board collapses
into: a card's own status, a trunk's own build, an open question each say one
thing about one issue, and none of them say what came in together while
nobody was watching.

The page is at `/overnight` in the Hatch app, under **Agents** in the primary
nav, beside the Leaderboard. It holds one table, the newest night in it —
epics, then bugs, then stories, then tasks; within a type, most descendants
first, then newest first — behind one read and one write:

```
GET  /api/hatch/overnight
POST /api/hatch/overnight/seen
```

`GET` answers `OvernightDto`: `from`, `to`, `count`, the ordered `rows`, and
`unseen` — this reader's own count of how many of those rows they have not
looked at yet (see [Overnight seen](#overnight-seen)). `POST` sets that mark
to now; the page fires it the moment it is opened, independent of whether the
row data itself has loaded, so reading the page is what counts as reading it,
and it reloads the nav control below rather than waiting on a page reload.
With nothing ever landed, the page draws an empty state rather than a table.

**Lands** is the moment an issue reaches a terminal column
(`EfHatchStatus.IsTerminal`) — read off the newest `status_changed`
[event](#issue-event) on the issue. **A night** is `Overnight.QuietGap` —
four hours, named as a constant so an operator who wants a different gap
knows where to look — and the newest night is the run of landings nearest
the newest one, split off by any gap bigger than that constant on either
side (`Overnight.NewestNight`): two landings exactly four hours apart are one
night; four hours and one tick apart, two. The fold is pure and synchronous,
computed only from landing instants, with no reference to whether a loop was
running — worth saying plainly, because this document already uses "night"
several dozen times elsewhere for a different thing: one `go-to-work` run's
own lifetime, start to whichever of its stop conditions fires (a bound, three
failures, the board asking it to stop). The two usually coincide, because
that lifetime is exactly what produces a run of landings with no four-hour
gap in it, but they are not the same computation — a person moving cards by
hand with no loop running at all still produces a night by this page's
definition, and a loop idling on a quiet board for over four hours without
landing anything splits what was one continuous process run into two nights
by this one, even though nothing about the loop itself ended. Said once here,
so a careful reader of this same document is not left reconciling the two
senses of the word silently.

**Not a session.** The leaderboard's session is one agent increment — one row
in `hatch.work_log_entries`, written at the end of one spawn, carrying a
cost. A night is coarser and carries no cost accounting of its own: several
increments across several tickets, or zero (a hand-moved card lands too),
over however long the loop kept landing something, with no tokens and no
notional USD anywhere in `Overnight.Landing` or `OvernightDto`.

**Agents → Overnight**'s own nav control carries `unseen` beside the page:
quiet while it is `0`, a lit mark and the count otherwise — and opening the
page is what clears it, the same `POST` above.

## The dispatcher

`GET /api/hatch/work/next` answers "what should an agent do next, and how?" in
one request: which issue, which column it is headed for, whether it may go
there, which playbook speaks for the move, the issue's children, and its
questions.

**The board is worked right to left.** `work/next` takes the top of the
rightmost column that still has something an agent may advance. That is a
scheduling policy worth saying out loud: a board worked left to right starts
everything and finishes nothing; one worked right to left pushes whatever is
furthest along over the line before it opens anything new. The second is what a
person does when they mean to ship.

**Except for what somebody marked off normal**, which is considered first or
last wherever it sits. The scan walks the columns six times — every
[emergency](#expedite) candidate right to left, then every
[expedited](#expedite) candidate right to left, then everything else right to
left, then every [low](#expedite) candidate right to left, then every
[economy](#expedite) candidate right to left, then every
[paused](#expedite) candidate right to left — so an emergency bug in the
leftmost column is reached before an expedited story in the rightmost one,
which is reached before a normal one in the rightmost one, which is reached
before a low one wherever it sits, which is reached before an economy one
wherever it sits, and inside each tier the order is the
board's own. The level that puts a candidate in the first, second, fourth,
fifth or sixth walk may be its own or inherited from an ancestor — the walk
does not care which. The paused walk is always last and always folded, so it
never changes which issue `next` or a hop picks — only that `queue` can report
it. The low and economy walks, unlike every other, are each gated rather than
always folded: a row reached on either can still be folded by [its own pace
check](#expedite), so unlike emergency, expedited or normal, reaching one of
those two walks does not guarantee the row is clear. Since HA-239, every tier
below the first one with a clear candidate is parked instead: the first clear
row seen, top down, names itself on every clear row in a strictly lower tier,
and `queue` reports the park rather than reporting the lower tier as available
when it is not. Six passes rather
than a sort of the finished rows, because the
[published scan](#what-a-pass-skipped) is the explanation of what `next`
picked, and a comparator applied afterwards would be a second opinion about
the order.

`?ancestorKey=AER-1` asks the same question of one epic's subtree instead of the
whole board — the same rule, narrower candidates, nothing else changed. It
shares `Rollup.DescendantIdsAsync` with the search filter rather than walking
the tree a second time, because a scope and a meter that disagreed about what is
under an epic is a bug nobody notices until the two are on one screen.

`?offsetMinutes=` is how a ready date is read against the caller's calendar day
rather than the server's.

**The playbook a dispatch carries holds the *effective* model and effort.**
Where the issue names one of its own it is folded in over the matched row's,
and everything else on that row — the transition, the types, the prompt — stays
its own, so the answer names the playbook that spoke *and* the values that won.
In place rather than in a second pair of fields beside them, deliberately: a
client that had to remember to check the second pair is a client that will one
day spawn `sonnet` on a ticket set to `opus`, and that failure is silent.
Nothing is hidden by it — `issue.modelOverride` rides the same payload, and is
what a printed line reads to say where the value came from. The payload also
names whose project chose it — `projectKey`/`projectName` ride the same
playbook record, null where a board-wide row spoke, the issue's own
project's key where a project-specific row won. Once the session ends, the
same `Source` (as `PlaybookSource` on `WorkLogEntryDto`) and file reference
ride the work-log row too, so the line a dry run printed before the session
ran and the row read back on the issue page afterwards say the same thing.

An override changes what a dispatch costs and never whether one happens.
Nothing in the refusals below consults one: an issue with no playbook for its
next move is refused in the same sentence whether it names a model or not.

**Twelve refusals**, and two of them are rules of the whole loop rather than
missing configuration:

1. The issue is already in a terminal column — there is nothing after it.
2. The issue is in a [deferred](#status) column — *a person puts it back on the
   board, not a pass*. Said before the next one, which would otherwise refuse it
   with "there is nowhere for this to go": true of a shelf, and no use to
   somebody reading a queue wondering why a ticket they parked is not moving.
3. There is nowhere for it to go. Every column has one — the column to its
   right — except the review column, which is dispatched to **itself** (see
   below) wherever a terminal column stands after it, and the last column of a
   board with no terminal one, which is where work ends.
4. The next column **is** terminal — *only the operator decides that something
   shipped*. Said for every column but review, which is not advanced at all: an
   issue in review is fixed or left alone, and only the operator moves it on.
5. Something else holds a live [claim](#claim) on it — *somebody is working this
   right now*, named with the runner it is being worked from and when it was
   last heard from. The claim may be a relative's: one on an ancestor or a
   descendant folds it too, in the same sentence with the relative's key and
   *above this* or *below this* in place of *this*. Its own claim is named
   first; a relative's only when it has none live.
6. The issue is [paused](#expedite), or inherits a paused level from the
   nearest ancestor that sets one — *a person set it aside, and nothing picks
   it up until they set it back*, naming the ancestor when the level is not
   the issue's own. A fact about the issue, so `work/{key}` is refused by it
   too, the same as the claim above it.
7. The issue holds an unanswered question — *it is waiting on a person, not on
   an agent*.
8. The project's *primary* [repository](#repository) — the first — matches none
   of the remotes the caller declared, on every move a session is spawned for:
   everything but a [hop](#the-hop), which runs no session and needs no
   checkout (a hop into the column where the code gets written is still
   checked). Every session spawns in the primary's checkout, so a runner that
   holds only a later repository is refused too, with a sentence naming the
   primary. A caller that declares nothing — an older
   CLI, or the issue page — is not folded by this at all: declaring is opt-in,
   which is what keeps them working unchanged. A project bound to nothing is
   worked from the caller's standing checkout exactly as before; one bound to
   remotes none of which match is refused unless the caller says it will clone
   what it lacks.
9. Something it [depends on](#dependency) is unfinished, and the move is into
   the column where the code gets written. Only that move: a pull request that
   already exists is not held back by what its ticket once waited on.
10. The move is into the [WIP section](#wip), and the section has no room for
    it: the load — not counting this issue — is already at or over the limit.
    Said after the dependency, which needs other work to land, and before the
    verdict below, which needs nothing at all once a branch is clean — a full
    section needs other work to leave. The load is always the board's own,
    whatever `ancestorKey` narrows the candidates to.
11. The move is into the WIP section, the issue's parent is an epic, and that
    epic is at its own [limit](#an-epics-own-limit) — the load under it, not
    counting this issue, is already at or over its `WipLimit`. Said right
    after the section-wide check above, for the same reason: it too needs
    other work to leave before anything more of this epic is pulled in.
12. It is in review and there is nothing for an agent to do on its branch: it
    neither conflicts with the trunk nor has a build that failed on its current
    tip — because it merges cleanly and its build passes, is still running, was
    not read on that tip or has no checks, because it has no branch on origin
    (one already merged counts as none), because more than one branch is named
    for it, or because no runner has checked yet. See [the review
    dispatch](#the-issue-in-review-whose-branch-conflicts-or-whose-build-failed-is-dispatched-to-review).

…and then, before the hop is even asked: a story, bug or task standing left
of the implementation column, whose own direct parent has not itself
reached it, stops where it stands — folded with "its parent has not reached
the implementation column, so nothing pulls it forward yet", whatever column
it stands in, not only one marked `ParentPulls`. Asked above the hop, so the
hold beats one: an express issue in a column marked `ExpressSkips`, and a
story under an epic that has not itself reached the implementation column,
are both folded here rather than carried.

Only past that hold is there one last check before the ordinary refusal: is
this a [hop](#the-hop)? [The hop](#the-hop) names seven kinds, asked in that
order, and this is where any of them is caught. One folds instead of landing
exactly: an epic that clears every other test but has nothing filed under it
is folded with "nothing is filed under it — an epic runs its stories, and it
has none". A parent-pull child past the hold above can now only ever be
folded by the other of `FamilyGate.Pulls`'s two conditions — a sibling
already in flight — with "a sibling is already in flight, so only one child
is pulled through at a time". Every refusal above this one still applies to
every kind of hop
exactly as it applies to any other issue: a hop answers only "does this
column still need a session", and nothing about a live claim, a pause, a
ready date, an assignee, a question, a repository or a dependency is any
different for it. [Economy's own gate](#expedite) is a second exception,
beside needing a playbook: both are loop policy rather than a fact about the
issue, so a hop is exempt from each — an express economy issue in a column
marked `ExpressSkips` is carried across whatever the account's pace reads.
Only where none of those earlier folds it either, and it is not a hop, does
the pass fall through to the ordinary refusal: no playbook covers this
transition for this type.

The claim is fifth rather than last because it is the only one of these that
says work is happening *now*; everything under it is about whether the issue
could be worked at all, and "no playbook covers this" is a true sentence about
the wrong thing when another runner is three minutes in.

The question is checked before the playbook, because "nobody has answered you"
is a more useful sentence than "no playbook covers this" when both are true. It
is also what keeps a question from being asked twice: without it, the loop's
only reaction to an open question would be to spawn another session that asks
it again.

`work/next` folds past everything blocked in silence — "nothing to do" is the
useful answer and a list of reasons is not — while `work/{key}`, which somebody
asked for by name, returns the refusal rather than a 404, because a person who
named a ticket is owed the sentence saying why it cannot move.

### The hop

**The loop's own write, on the loop's own say-so.** Seven kinds, asked in this
order:

1. **express** — an issue that is [express](#pre-approval) and stands in a column
   marked [`ExpressSkips`](#status), with no unanswered question.
2. **epic** — an epic standing in a column short of the implementation
   column, carried on each move right until it reaches that column, with
   something filed under it — any direct child, of any type, in any column —
   and whose own direct parent is itself an epic that has reached the
   implementation column. A top-level epic — one with no
   parent, or a parent that is not a running epic — is never carried this way:
   its own column is the whole signal, and only a person moves it in
   (HA-202). An epic that clears the parent test but has nothing filed under
   it is never a hop either: it is folded instead, with *"nothing is filed
   under it — an epic runs its stories, and it has none"*.
3. **under** — a story or bug standing in a column marked `ExpressSkips`,
   whose parent is an epic at or past the implementation column — in [In
   Progress](#status), in review, wherever it stands once it has reached
   that column. Measured off the implementation landmark, never off a
   column's name, so an epic sitting in the review column still counts.
4. **parent** — a child standing in a column marked [`ParentPulls`](#status),
   whose parent has reached the implementation column — stands in it, or in
   any column to its right, a terminal column included; a deferred column
   never counts — and none of whose siblings is standing further along, in
   board order, than the column this issue would be pulled into (HA-149).
5. **sendback** — a childless issue standing in a column, to the right
   (strictly, in board order) of `Columns.SendBack` — the column whose own
   `Target` is the implementation column (`Columns.Implementation`),
   measured off board shape rather than named — whose matched playbook sets
   a `BriefLimit` the issue's own description is longer than. Asked fifth,
   after the other four and before the sixth and seventh, so nothing already
   qualifying for one of the first four changes meaning (HA-394). Where the
   issue already stands in that column, or left of it, this is not a hop:
   going further would cross the Backlog threshold and re-require
   prioritisation, and the twentieth condition below folds the move instead.
6. **approve** — a [pre-approved](#pre-approval) issue standing in the
   review column with a pull request recorded, whose branch
   `MergeWork.Judge` says would merge cleanly or merely needs bringing
   current (`Dispatch.cs:1033-1040`) — carried into the column ticked
   [`MergesPullRequest`](#status) by `Columns.Advance`, the same column a
   session would otherwise have been dispatched to from review. Where the
   branch instead conflicts or its build has failed, `MergeWork.Judge`'s
   own `Pushback`/`Hold`/`Pending` kinds hold it in review exactly as they
   would an issue with no pre-approval at all — this kind never fires for
   those.
7. **close** — a [pre-approved](#pre-approval) parent standing in the
   review column with *no* pull request recorded, every child done by its
   own type's rule — an epic's own fold ignores a deferred child the way
   [running an epic](#running-an-epic) describes, while a story, bug or
   task counts one open child as enough to hold it (`Dispatch.cs:1042-1050`)
   — carried into the first terminal column strictly right of review
   (`Columns.Closes`, `Columns.cs:183-188`), whatever that column is named.
   A childless issue is never carried by this kind: having something filed
   under it is one of its own conditions.

Both are asked last, after sendback, so neither changes the meaning of a
row that already qualifies for one of the first five.

Hatch clears a blocker in this order — deterministic first (the board's own
shape answers it, and this fifth kind is that), heuristic second (a session
gets the narrowest brief that can settle it), and a person last, only for
what is theirs. A fold whose sentence is an imperative aimed at nobody —
"cut it into tasks first", said to no one — is a bug on its face, which is
why this kind exists rather than leaving that fold as the only answer.

`POST /api/hatch/work/{key}/hop` carries the issue to the column the kind
names: the same column `Columns.Advance` would send a session to, for the
first four kinds and the sixth, `Columns.SendBack` itself — a column
strictly left of where the issue stands — for the fifth, or
`Columns.Closes` — the first terminal column strictly right of review —
for the seventh. No model runs, nothing is spent, and no work-log row is
written, because a hop is not an increment. None of the first three kinds
ever fires on a move that ends where it starts: a review self-move —
resolving a conflict or a failing build — is never a hop, whatever column
is ticked. The fourth and fifth kinds do not carry that exclusion, each for
its own reason: the fourth's own gap on a `ParentPulls` review column, and
the fifth's own measurement off `Columns.SendBack` rather than off `to`.
The sixth and seventh never need the exclusion stated at all: each measures
its own target off `Columns.Advance` or `Columns.Closes` directly rather
than off `to`, and neither ever returns the column the issue already
stands in — so the column named is never the column it came from.

It takes the same query parameters `GET /api/hatch/work/{key}` takes, and no
`heldToken`: **a hop takes no claim.** A claim protects a session that runs for
minutes; a hop is one write, and the server re-judges the issue as the write is
made, with the same `Blocked` a named dispatch is judged by — so a stale runner
cannot carry an issue the server would no longer carry, and claiming would add
two history lines to protect nothing.

Two answers, both `409`, and each carries the sentence a reader would see on
`hatch queue`:

- The issue is blocked by some other fold — a claim, a ready date, an
  assignee, a question, a repository, a dependency, a full [WIP](#wip)
  section, [an epic at its own limit](#an-epics-own-limit), a terminal next
  column, a top-level epic (its own column is the signal, not a pass), an
  epic whose parent epic is not running, an epic with nothing filed under it,
  or (for a child of a `ParentPulls` column) a parent that has not reached the
  implementation column, or a sibling already in flight, or a childless issue
  over its playbook's `BriefLimit` with no column to send it back to. The
  sentence is exactly the one `Blocked` already gives for that fold.
- The issue is clear, but is not a hop — none of the seven kinds above applies,
  or the move is a review self-move. A session moves this issue, and a hop
  does not.

Otherwise the move is made: the issue's rank is set with
`RankService.BottomAsync`, the same as any other move, and the history gets
one `status_changed` event naming the caller as the actor, carrying one of
six shapes depending on which kind carried it: `{ from, to, express: true }`,
`{ from, to, epic: true }`, `{ from, to, under: "<epicKey>" }`,
`{ from, to, pulled: true }` for a parent pull, `{ from, to, sendback: true,
length, limit }` naming the brief's own length and its playbook's own
limit, or `{ from, to, preapproved: true }` — shared by the sixth kind and
the seventh, the only two that share a shape — the same shape
`IssuesController.MoveIssue` writes, with one more field, so a card that
passed a gate with nobody present says so on its own trail.

Both the sixth and seventh kinds write a second thing in the same save as
the move: a comment, naming the ancestor the pre-approval was read from
where it is inherited, or saying it stands on the issue's own row where it
is not (`WorkController.cs:505-526`). The approve hop's own comment quotes
`MergeWork.Judge`'s own sentence for the branch, the same sentence `hatch
queue` would otherwise have printed for it: *"Approved by its pre-approval
from AER-12: \<the merge judge's own sentence\>."* where the level is
inherited, or *"Approved by its pre-approval on its own row: \<the merge
judge's own sentence\>."* where it is the issue's own. The close hop's own
comment is the fixed sentence every child being done already is: *"Closed
by its pre-approval from AER-12: every child is done."*, or *"Closed by
its pre-approval on its own row: every child is done."* on the issue's own
level.

**Why the server performs the hop, on its own route, rather than the CLI
calling the ordinary move endpoint:** `IssuesController.MoveIssue` moves
anything a key asks it to, with no judgement of its own — a client that called
it for a hop would be the client deciding whether the move was safe, and the
trail could not say *express*. Judging *and* moving in the one write is what
keeps the rule that calls a row a hop and the write that performs it in one
place; see [where the loop's rules live](#where-the-loops-rules-live).

`WorkDto.Hop` and `QueueEntryDto.Hop` say which rows are hops, `HopKind` says
which of the seven carried it, and `HopUnder` carries the epic's key when
`HopKind` is `under` — null for every other kind. On `WorkDto`, `Playbook` is
always null where `Hop` is true, even where one covers the move, so no client
can spawn a session for a hop by accident.

### Check

**A check is a playbook that judges instead of editing.** `IsCheck` on a
[playbook](#playbook), written only by a person, the same cut `DeniedTools`
rides — a key's write of a playbook is refused ([the one edge that is
deliberately cut](#the-one-edge-that-is-deliberately-cut)). It is never the
review row, and no seeded row is one: see [Playbooks](#playbooks). The
Playbooks page has an *Is check* checkbox, and a row with `isCheck` set and no
`deniedTools` carries a red badge saying it can edit — a warning, not a
refusal, since the API never checks the pairing.

**`IsCheck` removes no tool.** The tools go with `DeniedTools`; what `IsCheck`
adds is everything below: a claim a key cannot move the ticket under, a route
to record a verdict, a different tail on the prompt, and the runner's look at
the tree afterwards. The issue stands in the playbook's *from* column while
the session runs, as under any playbook.

**The session cannot move the ticket.** The claim records the playbook it was
taken under (`ClaimPlaybookId`, see [Claim](#claim)), named by the runner on the
take or, in the loop, by the heartbeat straight after it, and never by the
session. While the issue's live claim is a check's, `POST /issues/{key}/move`
from a key is refused with a `409` and a sentence — a reorder inside one column
too — and a person's move is not. That is said of the move route only. The
check's prompt follows: its *Reaching Hatch* block drops `hatch start` and
`hatch move` and adds `hatch verdict KEY pass|fail ["findings"]`, and the first
line of *Where this increment ends* becomes *"KEY's session ends with a
verdict, not a move: a pass sends it to "X", and a fail sends it to "Y"."*
The rest of that section is unchanged. The attach is best-effort: when it
fails, the runner complains and keeps the claim, which then names no playbook,
so the move is not refused and `hatch verdict` answers `400`.

**The verdict is a record, not a move.** `POST /issues/{key}/verdicts`, open to
a key and to a person, takes `pass` or `fail` and optional findings, and writes
a [verdict](#verdict) row and a `verdict_recorded` event. It is a `400` unless
the live claim's playbook is a check, unless the word is exactly `pass` or
`fail`, and when the findings are over the comment body length; empty findings
are none. There is one row per claim, and a second call under the same claim
replaces the first. It answers `204` and moves nothing. `hatch verdict` is the
CLI, and refuses any other word before sending a request.

**The apply is the runner's, after the session, and is where the ticket
moves.** `POST /work/{key}/verdict?heldToken=` reads the unapplied verdict
recorded under that claim. It is a `409` for an issue with no live claim or a
token that is not the claim's, and a `404` for no unapplied verdict under it —
which a retry of an applied one also gets. It takes no gate: no dependency, no
[WIP](#wip) limit, no protected or terminal check on either target, as with
[a hop](#the-hop), so a check column belongs left of the review column.

- **Pass** moves the issue to where a dispatch from the column it stands in
  would take it (`Columns.Target`).
- **A first fail** moves it to the implementation column
  (`Columns.Implementation`), whatever the check's own columns are.
- **A second consecutive fail** moves nothing; see below.

A move is a `status_changed` event carrying `verdict` and `playbookId` beside
the usual payload, and goes to the bottom of its column. Every apply, moved or
held, also posts the findings as a comment of kind `verdict`
([Comment, question and answer](#comment-question-and-answer)) and sets the
row's `AppliedAt`.

**The second consecutive fail asks a person instead.** `VerdictStreak.Count`
walks the issue's events newest first, before this apply's own move. Events
that are not a `status_changed` are skipped, and so is every `status_changed`
with no `verdict` in its payload — a plain advance into the check column, but
equally a person's drag. A fail of the same playbook adds one. Three things end
the run: a `pass` of the same playbook, an `answered` event — any answer on the
issue, not only to this check's question — and a verdict of a *different*
playbook. When a fail finds one already counted, the apply moves nothing and,
in one write, records a `verdict_held` event, posts a question comment in prose
with no options — *"This check has failed twice in a row with nothing passing in
between. First: "…" Second: "…" - how should KEY proceed?"*, the first being the
newest earlier verdict comment — and an `asked` event, then the verdict comment
and `AppliedAt` as above. The open question blocks a dispatch the way any does,
and the answer ends the run, so the next fail counts as a first.

**After the session, on the runner, the tree comes first.** For a check
increment the runner looks at every checkout it entered: `git status
--porcelain` must be empty and `HEAD` must be what the entry left it at — after
the trunk was merged forward, not the branch's recorded sha. Then:

- **Changed.** Nothing is applied. The increment is stalled, with the reason
  *"the check changed the tree, so its verdict was not applied"* in the line the
  runner prints; the comment [a stall](#when-an-increment-does-nothing) writes is
  its ordinary one, not that sentence.
- **Clean.** The runner calls the apply. A `404` is *"the check ended without a
  verdict"*, a stall in the same way. Any other refusal, or a tree check that
  could not run, is complained about and flagged as not known, with nothing
  applied.

The verdict alone sets whether the increment moved, so a child filed during a
check counts for nothing. None of this runs when the lease was lost, on a usage
limit, when preempted, or when hard-limited. **A held second fail** reads as a
stall in the outcome line, since the ticket stayed where it was, but the
question the apply wrote is on the ticket by the time the runner reads the
questions, so it takes the *asked* branch: no let-go comment and no stall flag,
and the question is what holds the ticket. `hatch work -i` runs the same two
steps after the attached session, but with no report, no let-go and none of the
skip conditions: a changed tree is one plain comment (*"This check session left
the tree changed, so its verdict was not applied."*), a `404` another (*"This
check session ended without a verdict."*), and any other refusal is only
printed.

**The next build increment carries the findings.** `WorkDto.LastCheckFindings`
is set for an issue standing in the implementation column whose most recent
`status_changed` event carries `verdict: fail`; its value is the newest `verdict`
comment's body. The prompt prints it under `## The last check`, in a code fence,
and over 40 KiB leaves the findings out whole rather than cutting them.
`WorkDto.FailStatusName` is the implementation column's name, set on every
dispatch; for a check, `hatch work --dry-run` prints `# check: pass -> X, fail
-> Y`, with `?` for a missing name.

**Tick the build column.** `Columns.Implementation` is the ticked column, or
else the column whose next move is into review. On a board with check columns
between building and review the fallback is the last check column, and a fail
would send the ticket back to the check. Tick *Code written here* on the build
column (`PUT /statuses/{id}/implementation`, [a person's](#status)) and a fail
returns to it; the same column decides which move a [dependency](#dependency)
gates, so the tick puts that gate back on the build move. The worked example at the end of [Playbooks](#playbooks)
sets it up in full.

**A check column needs no code of its own.** Dispatch, the WIP gate,
dependencies, claims and the folds treat it like any other column: the
playbook that speaks for the move is matched by from, to, type and shape, and
nothing in `Dispatch` reads `IsCheck` or the claim's playbook. On the server
only the playbook writes, the verdict record and the move refusal above read
it. This is decision 8 of epic HA-422.

### Running an epic

**The pull is for a sub-epic, never a top-level one.** An operator drags a
*top-level* epic into the column before the WIP section and walks away — that
column, on its own, is the whole signal that its sub-tree should run, and only
a person sets it. No pass ever carries a top-level epic across on its own say-so
(HA-202): one with no parent, or whose parent is not itself a running epic,
is folded — *"a top-level epic is moved in by a person - its own column is the
signal for everything under it"*, or, where it has a parent epic that simply
is not running, *"its parent epic is not running, so nothing pulls it in"*.

Once a person has moved a top-level epic into the section, an epic **filed
under it** is carried in by the next pass with no session — [the `epic`
hop](#the-hop) — as long as something is filed under that sub-epic and the
epic slice has room; a sub-epic with nothing filed under it is folded instead,
not silently skipped. While an epic stands inside the section — including in
the review column — a story or bug under it, standing itself in a column
ticked `ExpressSkips`, is carried across the same way, one card a pass,
rightmost first: [the `under` hop](#the-hop). The per-epic limit ([HA-112](#an-epics-own-limit))
still holds the move into the implementation column, same as it always has.
Ticking `ExpressSkips` on the column before the section — typically *Draft* —
starts the flow there too: the tick now serves an express issue and a running
epic's stories alike, not express alone. The stock *"you are starting an
epic, which means choosing what starts"* playbook on the move into the
section is retired wherever a board's flags make this pull fire on that move;
see [Playbooks](#playbooks) for where it is kept.

Once every direct child of an epic standing in the WIP section is terminal or
deferred, the epic itself is dispatched to the next column under [the
verification playbook](#playbooks).

The session checks each acceptance criterion against the trunk, one at a
time, and comments the verdict on the epic. If every criterion holds — or the
only ones that do not were shelved with a deferred child — it moves the epic
on, and the operator closes it: see [what only the operator
does](#what-only-the-operator-does) for why closing it is never the session's
to do.

Where there are gaps, the session files them under the epic — one story or
bug per gap — and leaves the epic where it is. A filed gap flows like any
other work, and the epic is folded here until it ships; then the epic is
verified again. A deferred child's own criteria are named in the verdict, not
refiled as new work.

Once a parent - an epic or any other type - is verified and left standing in
the review column, one more thing can carry it past the operator's press
without one: a [pre-approved](#pre-approval) parent with no pull request
recorded, every child done by its own type's rule, is closed by the pass
itself - the **close** hop - rather than waiting for it. See [the
hop](#the-hop) for the fold it checks and the column it lands in, rather than
re-deriving either here.

Setting a board up so this whole path runs for one epic, start to finish,
with nobody pressing anything but the one drag nothing skips, is [its own
walkthrough](#a-night-mode-board).

### A night-mode board

How to set a board up so a pre-approved epic runs from `Draft` to `Done`
with nobody touching it but the one press nothing can skip, and then what
that run looks like, column by column. Nothing below is the only way to lay
a board out: every column is named the way the stock board names it
([above](#status), `:493-501`), never asserted as the one Hatch ships, and
the GitHub side stays a link rather than a repeat.

**The columns.** Start from [the stock seven](#status) and add one, then tick
two more. Add a column ticked `Protected` then `MergesPullRequest` between
`In Review` and `Done` - call it `Approved`, or anything else; [the approval
column](#the-approval-column) has the mechanics bought by ticking it, not
repeated here. Tick `ExpressSkips` on `Draft` and on `Backlog`. Leave `To
Do` unticked - what that costs, and why, is its own sentence under the path,
below.

**The playbooks the path needs.** [Playbooks](#playbooks) seeds nine rows;
this path only ever fires six of them, by current identity:

- `Breakdown → Backlog`, `epic` and `Breakdown → Backlog`, `story` -
  breakdown.
- `To Do → In Progress`, `''` (blank types) - analysis.
- `In Progress → In Review`, `''`/`Shape=leaf` - implementation.
- `In Progress → In Review`, `''`/`Shape=parent` - closeout.
- `In Progress → In Review`, `epic` - [the verification
  playbook](#playbooks).
- `Review → Review`, `''` - the review fix.

Not part of this path: the ninth row, `Breakdown → Backlog`, `task,bug`, and
the `To Do → In Progress` `epic` row - that row is retired on any
WIP-enabled board the moment a move crosses into the WIP section, whatever
`To Do` is ticked; see [the epic row above](#playbooks) for why the guard
reads that way.

**WIP room.** Give the [WIP section](#wip) a limit that admits at least one
epic and at least one story at once - the epic slice and the `story,bug`
slice both need room for this path to move at all. An epic's own
[`WipLimit`](#stories-at-once) reads as **one** when left unset, never zero
by default; a zero limit on either slice is only ever an operator's own
deliberate choice - made on the Statuses page for the section, or on the
epic's own page for its slice - not a default to warn against as an
accident.

**The GitHub side** is entirely [the approval column](#the-approval-column)'s
own, walked through operator-side in [Letting the loop merge for
you](../src/Hatch.Web/apps/hatch/public/hatch-at-home.md#letting-the-loop-merge-for-you);
linked here rather than repeated.

**The path.** The owner files the epic, sets it
[`preapproved`](#pre-approval), and leaves it in `Draft`.

- `Draft → Breakdown` is the express hop - no session.
- `Breakdown → Backlog` is the breakdown playbook, which writes the epic's
  own acceptance criteria and files its stories under it - each one
  inheriting `preapproved` live, the way [pre-approval](#pre-approval)
  describes.
- `Backlog → To Do` is the express hop again, for the epic and, separately,
  for each story as it is filed.
- **The epic's own `To Do → In Progress` move is the one press left for a
  person.** `To Do` is not ticked `ExpressSkips`, so the express hop never
  fires for the epic there, and the epic-start playbook that used to cover
  this move is retired on a WIP-enabled board regardless, per [the playbooks
  above](#playbooks). The move has no hop and no playbook either, so it
  folds on a missing playbook until somebody drags the epic across by hand.
  Everything after that one press is unattended.
- Each story's own `To Do → In Progress` is the analysis playbook; its
  `In Progress → In Review` is implementation, or closeout once its own
  tasks are all terminal or deferred.
- A story standing in review with a clean, mergeable branch is carried into
  `Approved` by the **approve** hop, where [the merge train](#the-merge-train)
  merges it with no session.
- Once every story is terminal or deferred, the epic's own `In Progress → In
  Review` move is the verification playbook. A pre-approved epic with every
  acceptance criterion met and no pull request of its own is then carried
  straight into `Done` by the **close** hop.

[The hop](#the-hop) and [running an epic](#running-an-epic) already document
every kind of move named above - linked here rather than re-derived.

### Preemption

**Who a claim heartbeat tells it has been preempted, decided lazily at each
call from the board as it stands.** Nothing is nominated, reserved or written
down in advance — that is what lets an emergency issue walk the board on one
runner instead of a saga. `Modules/Hatch/Preemption.cs` is the one place that
decides it, called from `IssueClaimController`'s heartbeat after a lease
refreshes; the pool of who might be told is `Runners.HeldAsync`, the same one
the Runners page draws from, so "what is this runner working" never has a
second copy to fall out of step with the first.

All five of these hold, or the heartbeating runner is told nothing:

1. Some [emergency](#issue)-level issue is actionable and unclaimed — asked
   through `Dispatch.ScanAsync` itself, never re-derived, with `offsetMinutes`
   pinned to `0`: the heartbeat carries no timezone the way `work/next` does,
   so a ready-dated emergency issue near a day boundary is judged against the
   UTC day rather than the caller's own.
2. The heartbeating runner's own issue is below emergency. Emergency work is
   never preempted, which is what makes this a surplus queue rather than a
   cascade.
3. Its issue is last in the dispatcher's own order among every live claim
   below emergency level that has not already been told. Ties break on issue
   key, so two heartbeats can never both believe they lost — though in
   practice there is no tie left to break, since the dispatcher's own order is
   already the tie-break. A live claim on an [economy](#expedite) issue is
   preempted before one on a low, normal or expedited issue, and a live claim
   on a low issue is preempted before one on a normal or expedited issue — and
   both are preempted after one on a paused issue, if a paused issue is ever
   still carrying a live claim — economy sits second-to-last in the walk, low
   third-to-last; paused does.
4. Fewer runners have already been told than there are unclaimed actionable
   emergency issues.
5. No live runner is free — a live row in `Runners`, holding no live claim. A
   free runner takes the emergency ticket on its own next pass, because
   emergency is the top of the walk, so preempting while one exists would
   spend a session to gain nothing.

**"Already told" is read off the trail, not stored.** The `claim_preempted`
event a preempted heartbeat writes (see [Claim](#claim)) doubles as the record
that this issue's runner has already been asked to stand down: rule 3's pool is
every live claim below emergency level *carrying no such event since its own
current claim was taken*, the same way `WorkController.LetGoAsync` asks
whether an issue's trail carries a release since its last status change,
against `ClaimedAt` instead since there is no "declaimed" event. Excluding a
told issue from the pool is what lets a second victim be found once the first
has been, and it is also what makes the same runner's second heartbeat, before
it releases, find nobody left to tell it — its own event is already on the
row, so it answers a plain `204` the second time, having already reacted once.

**One column for liveness.** Rule 5 reads `EfHatchRunner.LastSeenAt` and
nothing else, so the claim heartbeat and the claim release (the runner's own
lane, not the operator's tokenless clobber) both touch the holding runner's
row directly, the moment either happens — an unconditional `ExecuteUpdateAsync`
against `Runners`, matching zero rows harmlessly where a heartbeat races the
runner's own first `POST /api/hatch/runners/{name}`. Without this, a runner
that has just released would read as *gone* rather than *free* for as long as
its row's own last beat was stale, which is exactly the moment rule 5 needs to
answer correctly.

### The issue in review whose branch conflicts, or whose build failed, is dispatched to review

Every other dispatch ends in a different column, and this one ends where it
started. `Columns.Target` answers "where does a dispatch out of this column
end": the next column, except that the review column — when a terminal column
stands after it — is dispatched to itself. A move is a *review move* exactly
when its two ends are the same column, and `WorkDto.Kind` and
`QueueEntryDto.Kind` say which of two it is (`conflicts` or `build`; `advance`
for every other move) — derived, stored nowhere, and nothing compares a column's
name. A trunk-landing issue in review has no branch to conflict or fail, so its
review move reads `land` instead, `pending` or `hold` where the board is still
waiting or a person is — `Dispatch.KindOf` takes `LandingWork`'s kind for it,
and `Dispatch.Blocked` makes its sentence the row's, as the merge column's is
([landing on the trunk](#landing-on-the-trunk)). What decides between the two is what runners have reported about the
branch, and `ReviewWork.Judge` says, returning the kind and the fold together so
the two cannot disagree.

**Code decides, not a prompt.** A runner asks git (`git merge-tree`) and
reports a [merge check](#merge-check), and asks its own `gh` and reports a
[build check](#build-check); the dispatcher reads what the board holds and
applies one rule, on the server beside the other folds, so `hatch queue`
explains a passing branch the same way it explains a question or a claim. An
issue in review is actionable **only** when its branch conflicts with the trunk
**or** when the build on its branch's current tip has failed. The order is the
order of what a person would do about each, and **conflicts come first**:

1. Any merge verdict `conflicted` is conflict work, whatever the build says. The
   merge commit gives the branch a new sha, so the build's answer is about to be
   stale — and a pull request with a merge conflict often gets no CI run at all.
2. The three folds that say there is no one branch to ask about, unchanged:
   - *no runner has checked its branch against `<trunk>` yet*, when no verdict is
     held;
   - *more than one branch on origin is named for it - delete the ones that are
     not its branch*, when any verdict is `ambiguous` — the loop will not guess
     which branch is the ticket's, and says why;
   - *no branch on origin is named for it*, when every verdict is `none`.
3. For the repositories whose merge verdict is `clean`, **only build verdicts
   about that merge check's own branch sha count**: a verdict about any other sha
   says nothing about the branch as it stands. Any `failed` is **build work**,
   printed `In Review  fixing its failing build (<checks>)`.
4. Any `pending`: *its build on `<sha>` is still running*. Nothing is fixed until
   every check has concluded, so one increment sees every failure at once.
5. A clean repository whose build verdict is about a different sha than its merge
   check read: *no runner has read its build on `<sha>` yet*.
6. **No build verdict at all** in any clean repository: *its branch merges
   cleanly with `<trunk>` - nothing for an agent to do*, exactly as it was before
   builds were read. That is also every repository whose builds cannot be read —
   a runner without `gh`, one that is not signed in, a host it cannot reach — so
   not being able to read a build costs nothing.
7. Every counted verdict `passed`: *its branch merges cleanly with `<trunk>` and
   its build passes - nothing for an agent to do*.
8. Otherwise, `none`: *its branch merges cleanly with `<trunk>` and no checks ran
   on it*.

Where the project binds repositories only a verdict for one it still binds
counts, for a merge check and a build alike, and an issue conflicts, or has a
failing build, if any one of its counted verdicts says so. Every other fold still
applies to an issue in review, outranks both verdicts, and keeps its order: a
claim, a pause, a ready date, a person assignee, an open question, a
repository this runner has no checkout of. The verdicts come after the repository — a question
needs a person, a repository needs a clone, and a passing branch needs nothing
at all — and a missing review playbook is still the last fold. Nothing is ever
dispatched into a terminal column.

**Conflicts and builds share one playbook, and one model and effort.** The
review-to-review row stays the one playbook for an issue in review: the runner
brings the facts (a `## The conflict` or a `## The failing build` section) and
the playbook says what to do with them. The cost is that conflict work and build
work run on the same model and effort.

**Nothing is dispatched as build work until every runner knows how to run it.**
The build increment ([judged first by the push](#a-build-increment-is-judged-first-by-the-push-then-by-the-build))
landed before this dispatch did, so no runner is handed a dispatch it does not
know how to run. A runner on an old binary given `kind: build` would treat it as
an advance and stall; runners rebuild from the trunk between increments.

**A build that fails again on the agent's own fix is not sent to another agent.**
The board marks the issue stalled with the failed verdict, and a stall mark
folds the issue the same way a question does — see [build check](#build-check).
It resumes itself after `Hatch:StallResumeMinutes` without anybody answering
anything, unless it has already resumed that way `Hatch:StallResumeLimit`
times in a row, in which case it holds for a person instead.

**Only what is broken.** A branch that has fallen behind the trunk but still
merges cleanly, and whose build is green or still running, is left alone: the
branch is brought up to date when its pull request opens, and the forge does the
rest. There is no setting for keeping branches current, and a pull request that
merges and builds cleanly is never touched. Re-running a failed check is not
here either: a flaky check is a failure, and an increment that only re-runs one
leaves the tip where it was, which is a stall.

### The approval column

A column ticked `MergesPullRequest` is a merge column (see [Status](#status)
for the flag itself): the one place on the board a runner poll merges an
issue's own pull request, current and green, with no session. Nothing names
it; everything measures it.

**The geometry.** `Columns.Merges` (`Columns.cs:112-113`) finds the one
column a person ticked `MergesPullRequest` on, the same way
[`Columns.AwaitingReview`](#the-issue-in-review-whose-branch-conflicts-or-whose-build-failed-is-dispatched-to-review)
is found — off the board, never off a name. `AwaitingReview`
(`:98-105`) itself steps back over any protected column it meets walking left
from the first terminal one, so inserting a merge column — `Approved`,
say, between `In Review` and `Done` — does not make the dispatcher mistake it
for the review column: review is still whatever stands immediately left of
it. `Columns.Target` (`:77-80`) is why the review column's own advance never
lands a session in the merge column either: it dispatches a column to
itself, rather than past it, exactly when the next column is terminal **or**
protected, and a merge column is always protected (below). `In Review`
advancing therefore stays `In Review`, the same self-dispatch
[the conflict-and-build section](#the-issue-in-review-whose-branch-conflicts-or-whose-build-failed-is-dispatched-to-review)
already describes, not a step into the column beside it.

**The gate.** A merge column is always `IsProtected` as well — ticking
`MergesPullRequest` requires it (see [Status](#status)) — so every
move into one answers to `IssuesController.ProtectedMoveRefusalAsync`
(`:1106-1117`) before anything else runs: `"moving work into a protected
column is an owner's or approver's call, not an agent's"` for a program,
`"only an owner or approver of {key} may move work into \"{name}\""` for a
person or a key without the role. Inside `Dispatch.Blocked`
(`Dispatch.cs:756-772`), the merge column's own judgement is asked *ahead of*
that generic refusal — a `from.MergesPullRequest` branch returns the judge's
sentence (below) before the method ever reaches the plain `"the next column
is \"{name}\", and only its approvers move work there"` it gives every other
protected column further down the same method (`:778-779`). `hatch move`
carries the same speed bump at the client (`IssueCommands.cs:222-227`):
`"hatch: \"{name}\" is a protected column - only its approvers move a ticket
there (CLAUDE.md)"`. The browser mirrors both gates, in the same order, in
one function — `protectedColumns.ts`'s `moveBlocked`: `"Only an owner or
approver may move work in here"` checked first, so a caller who clears it
still meets the second. That second check rides beside the protected gate
rather than inside it: `IssuesController.MergeColumnRefusal` (`:1119-1123`),
a plain `400` for a move into the merges column on an issue with no pull
request recorded — `"\"{name}\" merges the pull request, and this issue has
none recorded"` — mirrored in the browser as `"This column merges the pull
request, and this issue has none"`.

**The judge.** `MergeWork.Judge` decides what an issue standing in the merge
column is waiting on, the way
[`ReviewWork.Judge`](#the-issue-in-review-whose-branch-conflicts-or-whose-build-failed-is-dispatched-to-review)
decides it for the review column — except its sentence is never null: unlike
a clean, unremarkable branch in review, nothing in this column is ever clear
for an agent, since the whole point of the column (HA-289) is that no agent
runs here. `Dispatch.Blocked` asks it for every column ticked
`MergesPullRequest`, ahead of the terminal and protected checks above, so a
session is never spawned there — the sentence is reported, not acted on. In
the order the judge checks them:

1. Its pull request already closed without merging: **hold**, *"its pull
   request was closed without merging"*.
2. Its pull request is a draft, by the forge's merge state: **hold**, *"its
   pull request is a draft"*.
3. No runner has checked its branch against the trunk yet: **pending**,
   *"no runner has checked its branch against `<trunk>` yet"*.
4. Any counted verdict `conflicted`: **pushback**, *"its branch conflicts
   with the trunk - back to `<review>` to fix it"*.
5. Any counted verdict `ambiguous` — more than one branch on origin named for
   it: **hold**, *"more than one branch on origin is named for it - delete
   the ones that are not its branch"*.
6. Every counted verdict `none` — no branch on origin named for it at all:
   **hold**, *"no branch on origin is named for it"*.
7. A clean branch whose build, read about that same merge check's own sha,
   is `failed`: **pushback**, *"its build on `<sha>` failed - back to
   `<review>` to fix it"*.
8. Clean but not holding the trunk: **update**, *"bringing its branch
   current from `<trunk>`"*.
9. A build about that sha still `pending`: **pending**, *"its build on
   `<sha>` is still running"*.
10. A build read, but about a different sha than the merge check's own:
    **pending**, *"no runner has read its build on `<sha>` yet"*.
11. Holding the trunk, with no build checks at all yet: **pending**, *"its
    branch holds `<trunk>` - no checks have run on it yet"*.
12. Every counted build `passed`: **merge**, *"its branch holds `<trunk>`
    and its build passes"*.
13. What is left is a build that read no checks at all. Inside
    `MergeWork.SettleWindow` (ten minutes, the same settling a push's checks
    are given elsewhere): **pending**, *"its branch holds `<trunk>` and no
    checks ran on it"*. Past it, in this order:
    **merge**, *"its branch holds `<trunk>`; this project requires no
    checks"*, where the project's `checksRequired` is off; **merge**, *"its
    branch holds `<trunk>`, no checks ran on it, and nothing is required of
    it"*, where the forge's merge state is `clean`; **hold**, *"its branch
    holds `<trunk>` and no checks ran on it - its pull request is blocked by
    the repository's rules"*, where it is `blocked`; and otherwise **hold**,
    *"its branch holds `<trunk>` and no checks ran on it"*. The forge's
    state counts only when the head sha it was read at is the branch sha the
    merge check read: a branch that has moved since is a branch the state
    says nothing about, and falls to the last hold. `unstable`, `behind` and
    `dirty` are not read, since the build check and git's merge check already
    answer those. This item qualifies HA-289's *"Zero checks is not green"*.

**A flagged path holds, not merges.** Each of three **merge** outcomes
above - item 12, and the two **merge** sub-cases of item 13 - is
**hold** instead, *"its pull request changes `<path>` - a person
merges it on the forge after reading it"*, when any counted clean
verdict names a path (HA-431). Every other item above - the pushback,
the update, every pending, and the holds already in the list - is
unaffected by the flag.

**Pushback is built, not deferred.** `POST /work/{key}/pushback`
(`WorkController.cs:575-642`, `WorkController.PushbackWork`) carries an issue
standing in the merge column back to the review column on its own say-so: it
takes no verdict from its caller, re-reads the issue's own stored merge and
build checks, calls `MergeWork.Judge` itself, and acts only when the kind
comes back `pushback` — anything else is a `409` carrying the judge's own
sentence instead. Its destination is
[`Columns.AwaitingReview`](#the-issue-in-review-whose-branch-conflicts-or-whose-build-failed-is-dispatched-to-review),
refused where the board has none or where that column is the merge column
itself. The comment it writes on the issue is the judge's own sentence,
capitalized with a period appended — *"Its branch conflicts with the trunk -
back to In Review to fix it."* `MergeTrain.PushbackApprovedAsync`
(`MergeTrain.cs:463-496`) calls it once a tick, for every issue in the merge
column whose judged kind is `pushback` — one of `MergeTrain`'s own halves,
run on its own clock, independently of
[the poll](#checking-the-branches-in-review) that reports the merge and
build checks `MergeWork.Judge` reads in the first place.

#### The merge train

**One train per project.** `Dispatch.Train` (`Dispatch.cs:370-396`) groups a
project's merges-column rows by `ProjectId` (`.GroupBy(i => i.ProjectId)`,
`:377`) and returns one `TrainSlot` (`Position`, `IsHead`, `HeadKey` — the
record at `:341`) per row, computed once per pass inside `ScanAsync`
(`:177-186`) rather than asked per row, so two rows in the same column never
disagree about whose turn it is.

**The order is the dispatcher's, not the column's rank.** Rows are ordered by
`effective[i.Id].Level` descending, then `Rank`, then `Id` (`:379-383`) — the
same priority tiering `ScanAsync`'s own six-pass walk uses everywhere else on
the board, so an expedited row in the merge column goes first even behind a
normal row with a lower `Rank`.

**The head, and why a held row is stepped over.** The head is the first row
in that order whose `MergeWork.Judge` kind is not `Hold` or `Pushback`
(`Dispatch.cs:403`). What it steps over, by the judge's own list already
above in this same section: a closed pull request and a draft, both **hold**;
a conflicted branch and a failed build, both **pushback**; an ambiguous or
missing branch, **hold**; and a tip with no checks past the settle window, on
a project that requires checks, where the forge does not call the pull
request clean — **hold**, whether the forge says blocked or says nothing. A
`pending` row is not stepped over: it is the head, and the train waits on it
until it settles, as it does on `update` and `merge`. Every other row in that project's column gets `IsHead: false` plus the head's own
key; `Blocked` (`:767-770`) turns that into the sentence an otherwise-
actionable row shows instead of acting on: `"{judgement} - waiting its turn
behind {headKey}, position {n} in the train"`. That sentence is also what
`hatch queue` prints for that row, in plain text, with no separate "train"
field anywhere in `QueueEntryDto`.

**The driver, and how the heartbeat elects one.** `Runners.DriversAsync`
(`Runners.cs:183-215`) elects, per project, the longest-live loop that holds
a checkout of it — `Dispatch.HoldsCheckout`, the same rule
`WorkController.GetReview` uses for its own eligibility, so the two can
never disagree. Live means `LastSeenAt` within `Hatch:RunnerGoneAfterSeconds`
(default 90) of now, and a `once` runner is never a candidate, since its own
`BeatAsync` throws the instruction away (`Runners.cs:188-190`). Ties go to
the oldest `FirstSeenAt`, then `Name` ordinally (`:208`) — deliberately
stable, so a project's driver only moves once it actually drops out of live.
The election rides the heartbeat: `Drives` on `RunnerDto`/`RunnerInstructionDto`
(`Dtos.cs:2318-2343, 2452-2463`) carries the elected project keys back, and
the CLI learns them into `ReadoutState.SetDrives`/`Drives`
(`Readout.cs:95-104`, folded on every real heartbeat at `GoToWork.cs:889`),
which gates every one of `MergeTrain`'s five halves (`MergeTrain.cs:161, 258,
370, 470, 520`): a runner acts only on a project it was elected to drive.

**Its own interval, beside the poll's.** `MergeTrain.Start(runtime,
TimeSpan.FromSeconds(mergeInterval))` (`GoToWork.cs:812`) runs `TickAsync`
once, then waits `--merge-interval` seconds (default 5, `:466`) —
independent of `--interval` (default 60, `:465`), which paces the poll that
reads build and merge-conflict verdicts. Cross-reference
[Checking the branches in review](#checking-the-branches-in-review), which
already says the pull-request, merge and update halves "run on their own
clock... rather than this poll's" (`:4358-4361`) without naming the clock —
this new section is that explanation.

**A merge with no checks rides the same train.** A merge judged under the
last item of the judge's list — a project that requires no checks, or a forge
that calls the pull request clean on the judged sha with no checks having run
— is a `merge` like any other: same head, same order, the same `gh pr merge
--merge --admin --match-head-commit`. Both outcomes are `WorkKinds.Merge`,
which `MergeTrain` already carried, and `MergeTrain` itself did not change for
it.

#### Landing on the trunk

A project whose `Landing` is `trunk` (see [Project](#project)) has no pull
request to merge, so nothing above applies to its issues; the column that does
the same job for them is review itself, and `LandingWork`
(`LandingWork.cs`) is its judge, decided the way `MergeWork` decides the merge
column's — from what runners have reported, and never by a prompt. The question
is not whether a branch is clean but whether the commit is **on the trunk and
the trunk's build passes there**. It reuses `MergeWork.Judgement` and
`MergeWork.SettleWindow`, so its kinds are the merge column's own, plus one:
`land`.

**When it applies.** `LandingWork.Applies` (`:14`): the project's `Landing` is
`trunk` and the issue has no pull request recorded. A pull request recorded on
an issue in such a project puts it back under the merge column's rules, and a
project that lands by pull request never meets this judge.

**The judge.** `LandingWork.Judge` reads the issue's [landings](#landing) —
only those for repositories the project still binds, or every one if it binds
none — and the [trunk builds](#trunk-build) for them, in canonical order. In
the order it checks them:

1. No counted landing: **pending**, *"no landing is recorded for it yet"*.
2. A landing no runner has confirmed on the trunk — `ContainedBy` still null:
   **pending**, *"no runner has seen `<sha>` on `<trunk>` yet"*.
3. No trunk build read at the sha that contains it, or one read at a different
   sha: **pending**, *"no runner has read `<trunk>`'s build on `<sha>` yet"*.
4. Any such build `failed`: **hold**, *"`<trunk>`'s build on `<sha>` failed
   after it landed"*.
5. Any such build `pending`: **pending**, *"`<trunk>`'s build on `<sha>` is
   still running"*.
6. Every build `passed`: **land**, *"it is on `<trunk>` at `<sha>` and the
   trunk's build passes"*.
7. What is left is a build that read no checks at all, alongside any that
   passed. Past `MergeWork.SettleWindow` (ten minutes from when the board first
   heard of the sha, the settling the merge column gives it) every such build
   settles: **hold**, *"it is on `<trunk>` at `<sha>` and no checks ran
   there"*, where the project's `checksRequired` is set, and **land**, *"it is
   on `<trunk>` at `<sha>`, and this project requires no checks"*, where it is
   not.
8. Inside the window: **pending**, *"no checks have run on `<trunk>` at `<sha>`
   yet"*.

**Trunk green, not trunk reached.** `ContainedBy` is not the commit's first
appearance on the trunk but the trunk's tip *now*: the poll rewrites it as the
trunk moves, so the build judged is the build on the latest tip that holds the
commit. A commit that landed green and was followed by a red one is **hold**
until the trunk is green again, and the judge neither knows nor says which
commit broke it.

**Who does what.** Three parties, none of them trusting the others:

- **The poll confirms.** `Poll.ConfirmLandingsAsync` asks git whether the
  landing's sha is in origin's trunk and puts the answer to the board as
  `ContainedBy` — see [Checking the branches in
  review](#checking-the-branches-in-review). It writes no verdict and moves no
  ticket. The trunk's build is read by the poll's trunk half, off the tip,
  whether or not anything is in review.
- **The server judges.** `LandingWork.Judge` is called wherever the answer is
  wanted — the review read, the queue, the attention panel, and the route — each
  asking again, so that none of them can hold a verdict the others have not.
- **The merge train calls the route.** `MergeTrain.CarryLandedAsync` calls
  `POST /work/{key}/landed` for every review row whose judged kind is `land`,
  on a project this runner is elected to drive, once per `--merge-interval`.
  The route is also what an operator or a script would call by hand; it takes
  no body and trusts no kind from its caller, so a stale read costs a `409`
  and nothing more.

**The route.** `POST /work/{key}/landed` (`WorkController.cs:893-952`,
`WorkController.LandedWork`) re-reads the landings and the trunk builds,
judges them, and only on `land` moves the issue from the review column to
`Columns.Closes` — the first terminal column after it — with a
`status_changed` event marked `landed: true`, a ticket comment (*"Landed on
`<trunk>` at `<sha>`; the trunk's build passes."*, or *"...; this project
requires no checks."* where it settled on no checks), and a rank at the bottom
of that column. It takes no claim and refuses a claimed issue, and it has no
WIP gate; unlike `/merged` it carries no story's waiting tasks, since that is
for a pull request several of them share and a landing has none. Its `409`s
are the ones the [API table](#api-surface) lists, and the sentence on a judged kind
other than `land` is the judge's own — the same one `hatch queue` prints.
See [the four paths past the terminal
guard](#the-four-paths-past-the-terminal-guard) for why a program may take
this step.

**The queue row.** `Dispatch.KindOf` takes `LandingWork`'s kind for a
trunk-landing review row, where a pull-request row would have `conflicts`,
`build` or `advance`, and `Dispatch.Blocked` makes the judge's sentence the
row's `Blocked`: nothing in the review column is ever an agent's to do for a
landing, so the sentence is reported and no session is spawned. The panel
lists only the **hold**s, as `landingHeld` — see [what is waiting on
you](#what-is-waiting-on-you).

### The four paths past the terminal guard

Nothing above ever lands an issue in a terminal column, and nothing above
ever lands one in a protected column either — that is what `Columns.Target`
is for, dispatching review to itself rather than past either, and what
`Dispatch.Blocked` refuses in every other case with *"the next column is
\<name\>, and only the operator moves work there"* or *"...and only its
approvers move work there"*. Four routes cross one of the two guards
anyway, each for its own reason, and none of them is a program's own
judgement call.

`POST /work/{key}/merged` is the first, and the oldest: a merged pull
request **is** the operator's decision to ship, made on the forge and
reported back rather than pressed on the board. So this route calls
`Columns.Advance` — the next column, terminal or not — and carries its own
refusals rather than `Dispatch.Blocked`'s: the issue must be standing in
whichever column is measured as review right now, it must carry a recorded
pull request url, the url the caller names must match it exactly (trimmed,
ordinal), there must be a column after review to advance into, and the
issue must not be claimed. No ready date, no assignee, no WIP section and
no dependency gate it — those all gate an agent picking up work, and
nothing here is an agent picking up work.

The **approve** hop is the second, and it crosses the *protected* fold
rather than the terminal one: `Dispatch.Blocked`'s own protected check
reads `to.IsProtected && hopKind != HopKinds.Approve` (`Dispatch.cs:803`),
so a pre-approved issue in review whose branch `MergeWork.Judge` would
merge or bring current is the one case a dispatch lands work in a
protected column at all. It is a path past the guard and not a hole in it
for the same reason `/merged` is: the move in is a decision an owner or
approver already made, in advance, on the ticket — pre-approval is the
approve press given before the fact, not a program judging for itself that
a branch is fine to ship.

The **close** hop is the third, and it crosses the *terminal* fold instead,
beside `/merged`: `Dispatch.Blocked`'s own terminal check reads
`to.IsTerminal && hopKind != HopKinds.Close` (`Dispatch.cs:800`), so a
pre-approved parent in review with no pull request recorded, and every
child done, is carried straight into the first terminal column right of
review, with nobody pressing anything on the board. The reason is the same
as the approve hop's: the move is a decision an owner or approver already
made in advance, carried out the moment the board can see it is true,
rather than the operator's own press on a forge or a card.

`POST /work/{key}/landed` is the fourth, and it crosses the terminal fold the
way `/merged` does, for a project that has no pull request to merge: an
owner's choice, on the project, to land on the trunk **is** the decision to
ship, so an issue that has [landed there and whose build passes](#landing-on-the-trunk)
needs nobody to press anything. It takes no body and trusts no verdict from its
caller — it re-reads the issue's landings and the trunk builds, calls
`LandingWork.Judge` itself, and acts only when the kind comes back `land`. Its
refusals are its own, as `/merged`'s are: the issue must be standing in
whichever column is measured as review right now, the project must land on its
trunk and the issue must have no pull request recorded, there must be a
terminal column after review, and the issue must not be claimed. The column it
moves into is `Columns.Closes`, the first terminal column after review — not
`Columns.Advance`'s next column, since a merge column may stand between, and a
landing does not go through one. No ready date, no assignee, no WIP section and
no dependency gate it, for `/merged`'s reason.

### One more, on `next` alone

The refusals above are facts about an issue. Two further rules are the *loop's
policy* — what an unattended run may **start**, as opposed to what may move — so
they are asked on `work/next` and not on `work/{key}`. A person who names a
ticket is giving an instruction, and housekeeping does not overrule it.

The issue's **type** is not among them, and was never the loop's to decide:
which types a move applies to is [the playbook row's](#playbooks) to state, and
a type an unattended run does not pick up is a transition no playbook covers
for that type. That fold is the last one in the list above, and it names the
Playbooks page because that is where the fix is.

1. **Its ready date has arrived**, read against the caller's calendar day
   (`?offsetMinutes=`) rather than the server's. A card folded off the board is
   not one an unattended pass should be spending an increment on — but somebody
   who names a ticket ahead of its date has said the date is not the point
   today, and is given the dispatch rather than a lecture about it.
2. **Nobody's name is on it** — nobody's meaning no *person's*. An issue
   [assigned](#assignee) to somebody is theirs to do, and a pass that took it
   anyway would be taking work off a person who had said they wanted it.
   Assigning something to yourself is therefore how you take it off the night
   shift, which is a gesture you already had a reason to make.

   **People only**, and the asymmetry is the point: an issue assigned to an API
   key is picked up exactly as it always was, because assigning a ticket to
   Claude and having Claude stop working on it would read backwards. An
   assignee whose person has been deleted, or whose key has been revoked, is
   nobody at all — the [liveness rule](#assignee) reaches the dispatcher the
   same as every other reader, so there is no such thing as a ticket held off
   the board by a name that no longer resolves.

An [unmet dependency](#dependency) is deliberately **not** here. It looks like
housekeeping and is not: a ready date is a decision about scheduling, while an
edge is a fact about the work, so `work/{key}` refuses on one too. Somebody who
disagrees takes the edge off, which is one press.

A full [WIP section](#wip) is not here either, for the same reason. The limit
is a fact about the board, as a claim is, so `work/{key}` is refused by it in
the same sentence a pass is. Overriding it is not a loop policy to set — it is
done on the board, by moving the card in.

**`?mine=true` is a third, and separate, policy** — a further narrowing rather
than a rewrite of the second: plain `next`/`queue` still skip every ticket
assigned to a person, including the caller's own, exactly as above.
`?mine=true` instead takes *only* the caller's own — a ticket assigned to the
person it works for, or to its own key — and folds everything else with a
sentence naming whose it is: `"assigned to Ada, not to you"`, or `"assigned to
nobody - a --mine pass takes only your own"`. "Its own" is read off the calling
key, never off anything the caller says about itself: the key carries an
`OwnerPersonId`, set only by an admin at the API Keys page, and a caller whose
key belongs to nobody — never set, or its person since deleted — is refused
before anything is scanned, with the sentence saying so. A setting like
`HATCH_ME=Ada` would have reopened [the one edge that is deliberately
cut](#the-one-edge-that-is-deliberately-cut): any key could say it is Ada and
take the work she kept for herself.

### What a pass skipped

`work/next` folding in silence is right for one increment and backwards for an
unattended loop. Nobody is watching, and the one thing worth having afterwards
is what the pass skipped and why — above all a column and type nobody has
written a playbook for, which reads as a finished board from the outside and is
not one.

`GET /api/hatch/work/queue` answers with every issue the dispatcher considers,
in the order it considers them, each carrying the sentence saying why it cannot
be advanced — or nothing, where it can. It takes the same `ancestorKey` and
`offsetMinutes` and means the same things by them, and each row is what a
dispatch carries minus the playbook prompt, the children and the questions:
those are the payload of one agent working one issue, and loading them for
every row would make a whole-board read expensive for nothing. What a row does
carry is `QueueEntryDto.Model`, the model a session would run on (the issue's
override, else the matched playbook's), which is what lets a runner skip a row
pinned to another agent's family before it claims it. `QueueEntryDto.DeniedTools`
is the matched playbook's deny rules as written (null with no playbook, empty
where it denies nothing), which lets a runner skip a row its agent cannot honour
a deny rule for before it claims it.

**The rows arrive in the board's own order.** Rightmost column first, and
within a column the `(rank, id)` that `GET /api/hatch/board` serves that column
in — so a card's line in the queue is its position on the board, and nothing
between the two re-sorts. That matters because most of a column being folded in
silence is indistinguishable from a column being read out of order, and a
report of one is usually the other.

**The first entry with no reason is what `work/next` returns**, because it is
the same walk. `GetNextWork` is the first clear row of the scan rather than a
second loop that happens to agree with it — two walks that could disagree about
the order of the board is precisely the bug this endpoint exists to expose.

Every fold therefore lives in one place and in one order, most fundamental
first: a terminal or deferred column, nowhere to go, a next column that is
terminal, a live [claim](#claim), a [pause](#expedite), a ready date, an
assignee, an unanswered question, a repository this runner lacks, an unmet
dependency, an open child, a
full [WIP section](#wip), [an epic at its own limit](#an-epics-own-limit), and
— for an issue in review — the verdict on its branch, then [the hop](#the-hop),
and last the missing playbook — last because it is only worth saying about an
issue that is otherwise a candidate. The two that are the
*loop's* policy rather than a fact about an issue — the ready date and the
assignee — are asked only when the pass is asking, so `work/{key}` still
ignores them.

**An epic's own open-child rule reads differently from the generic one.** The
generic fold — "its children are the work, and some are still open" — counts a
deferred child as still open, and reaches only the issue standing in the
implementation column. An epic has its own version instead: a deferred child
does not count against it at all, since shelving a story is the operator's
call and not a gap the epic is held for, and it reaches the epic wherever it
stands in the WIP section, not only the column where code is written, since an
epic is never itself the thing being implemented. It names how many of the
epic's counted children are still open — `{n} of its {m} children {is/are} not
done`, `its only child is not done` for exactly one, or `nothing is filed under
it` for none at all — each closing with "an epic is verified once its stories
are".

**A hop is marked, not merely clear.** `QueueEntryDto.Hop` is true on a row
that is express, stands in a column marked `ExpressSkips`, and clears every
fold above it — so a reader of the queue, and `hatch queue` at a terminal, can
tell a row that will be carried across with no session from one that will
spawn an ordinary one, even though both print no `Blocked` sentence.

The columns with nowhere to go — a terminal one, a deferred one, and a rightmost
one that is not terminal — are absent rather than listed as blocked. An issue the dispatcher
never reaches is not something the pass skipped, and shipped work is not a
backlog. The review column is listed: it has somewhere to go — itself — and each
issue in it is a conflict to resolve, printed `In Review  resolving conflicts
with <trunk>`, a failing build to fix, printed `In Review  fixing its failing
build (<checks>)`, or a row saying why not.

It is a read, and it costs what a read should: the statuses, the scope, which
dependencies are unmet, the open-question counts and the whole playbook matrix
are each read once for the pass rather than once per row, and the issues are
projected in one batch.

**And it is read rather than left on the server.** Everything needed to tell a
jammed board from a finished one was computed and served here long before
anything printed it, and for a while nothing did unless somebody thought to run
`queue` by hand — so ten minutes of an idle loop saying "nothing on the board is
an agent's to move" read as a broken loop. `hatch` now groups the scan's
sentences and prints them with a count each whenever a pass finds nothing, and
a board with nothing on the dispatcher's path at all says *that* instead. Two
different boards, two different sentences, no second command.

### Playbooks

A playbook row is `(from column, to column, issue types, issue shape) →
prompt, model, effort`. Shape is the second axis beside types: `any` (the
issue's children are not consulted), `leaf` (it has none) or `parent` (it has
at least one) - so a parent's closeout and a childless issue's implementation,
both `In Progress → In Review` for the same type, can be two different rows at
two different prices instead of one row doing both jobs.

On a board running more than one project, a row naming the issue's own project
is chosen before specificity is even asked, whatever either row names for type
or shape - a project row never loses to a board-wide row, and a row naming a
different project is never a candidate at all. Only once that is settled, or
neither row names a project, does specificity run.

**A repo-sourced project's own layer is a file, not a table row.** Its rows in
`.hatch/playbooks.yaml` on its primary repository's trunk stand in for its
own-project table rows, in the order drawn above between a project's own
layer and the board-wide one - a file row beats a board-wide row whatever
either names, specificity and the older-row tie-break run exactly as above,
and a file row's age for that tie-break is its position in the file, earlier
entries winning ties. A file row's `prompt` is the whole of its wording and its
`defaultPrompt` is always null, so nothing on the Playbooks page marks it as
the operator's own and there is nothing to hand it back to; it changes only by
a commit to the file. A move the file has no row for falls through to the
board-wide row; its own table rows are never a candidate at all while the
setting is on (`Dispatch.MatchAsync` excludes them from the query itself, not
by filtering afterward). `Dispatch.ScanAsync` builds this list once per
project per pass and `Dispatch.MatchAsync` for one dispatch; `PlaybookDto.Source`
says which layer spoke - `board`, `project` or `file`.

**Every move of a repo-sourced project folds** on a problem with the file
before a playbook is even asked for, each sentence naming the file, the
trunk and a sha: bound to no repository, *"`<KEY>` is repo-sourced but is
bound to no repository - bind one on the Projects page"*; naming no base
branch where more than one `.hatch/playbooks.yaml` trunk exists for its
canonical, *"`<KEY>` names no base branch and more than one
.hatch/playbooks.yaml trunk exists for `<canonical>` - set the base branch on
the Projects page"*; no copy ever read, *"no runner has read
.hatch/playbooks.yaml on `<trunk>` of `<remote>` yet"*; the copy `absent`,
*".hatch/playbooks.yaml is missing on `<trunk>` at `<sha>`"*; the copy
`invalid`, its own stored sentence followed by *" - fix it on the trunk"*. A
good copy that simply names nothing of its own (`playbooks: []`) folds
nothing by itself. Where the copy is good and no row speaks - not the file's,
not a board-wide one - the fallback sentence names the file too: *"no
playbook covers "`<from>`" to "`<to>`" for `<a/an issue type>` - add one to
.hatch/playbooks.yaml on `<trunk>`, or a board-wide one on the Playbooks
page"*, in place of the table-only project's "...add one on the Playbooks
page". Every one of these folds every move of the project, not only the ones
the file would have covered - a project that said its instructions live in
the file and cannot be read is misconfigured for everything, not just for
the transitions the file happens to mention.

The board's copy of the file decides *which* issue is dispatched; the
runner's own read of the trunk at spawn, below, decides *what the session is
told*. Nothing a key writes to the copy ever reaches a session's prompt,
model, effort or budget - see [What a runner does, in order](#what-a-runner-does-in-order).

Specificity is one number, 0 through 3, and the ordering it produces is
type+shape > type-only > shape-only > neither: naming the type is worth more
than naming the shape, so a row that names only a shape (1) never outranks one
that names only a type (2) - only a row naming both (3) beats a type-only row.
Underneath both axes, ties go to the older row. So "Breakdown to Backlog,
epics" can say something different from "Breakdown to Backlog, anything else"
without either having to know about the other, and "In Progress to In Review,
stories, parents" can say something different from "In Progress to In Review,
stories" the same way.

The matrix exists because **"do the next increment" is not one job**. Turning a
paragraph of intent into an epic with stories under it is the hardest thinking
in the flow and wants the largest model at the highest effort; picking up an
already-specified task and writing the code is ordinary work a smaller one does
well. Encoding that as rows rather than as branches in a script means the
operator retunes it from a page after watching a run go badly, which is the only
way anybody ever finds the right settings.

`Model` is an alias (`haiku`, `sonnet`, `opus`, `fable`) rather than a pinned
id, because a playbook says "the big one" and should still mean it a year from
now. An alias is a tier each agent maps for itself, so it means the same thing
to every runner. A pinned id is not: it belongs to one agent's family, and the
families are one list, `ModelFamilies` in `Hatch.Contracts`, which the server
validates against (the board's playbook, a repository's playbook file and an
issue's override all refuse in one sentence naming the aliases and the
families: `a model is one of haiku, sonnet, opus, fable, or a pinned id of a
supported agent (claude-opus-5 for claude; gpt-6-astra for codex; grok-4.6 for grok) - not "nonsense-1"`) and the CLI
reads. **Pinning an id is choosing an agent.** A runner whose agent
(`HATCH_AGENT`, or `--agent`) is not that family skips the row before claiming
it and says so under [what the pass skipped](#what-a-pass-skipped); `work <key>`
refuses with the same sentence, `--dry-run` prints it, and the ticket waits for
a runner of that agent (a board with none leaves it sitting). The sentence is
`pinned to {model}, a {agent} model - this runner's agent is {agent}`. An alias,
or no model, is never skipped, and `--model <alias>` on `work` beats the
playbook and proceeds. (`claude-…` is the Claude family's shape, not the shape
of a pinned id in general; `gpt-…` is the Codex family's shape.)
`Effort` is exactly what the CLI's `--effort` accepts, because a value it does
not recognise is one it refuses at spawn time, long after the operator has
stopped looking at the page they typed it on.

**One issue can say the matrix is wrong about it.** An issue carries an optional
`ModelOverride` and an optional `EffortOverride` of its own, and where one is
set it beats whichever playbook speaks for the issue's next move — every one of
them, not a single transition's worth, since a story that is harder than its
column suggests is still the hard one wherever it sits. One pair per issue, good
for every move it ever makes; independent of each other; reaching that issue and
nothing beneath it. They take exactly what a playbook takes, refused in the same
sentence, because it is the same rule called twice. There is no per-issue
*prompt*: a prompt is the method for a transition, and a per-issue method is a
paragraph, which is what the description is for. Setting one is a person's
(`PATCH /api/hatch/issues/{key}/playbook`); reading one is anybody's who can
read the issue.

**A row from the review column to itself is the review playbook.** It is the
one row whose two ends are the same column, and the Playbooks page accepts it
for the review column and for no other — measured off the board as it stands,
the way every rule about review is — and refuses any other column naming itself
with a sentence saying which may. It is written on that page, with its own model
and effort, like every other session instruction. One row answers both kinds of
review work: the runner adds the facts — for a conflict the branch, both shas and
the files, for a failing build the branch, the sha, each failing check with its
link and the lines of its log that lead up to the failure — and the playbook says
what to do with them: find the failure, fix it and nothing else, build and test
until green, push to the same branch, and stop, never rebase and never
force-push. A flaky check is still the session's to report on the ticket, not to
paper over. It is never a check: no migration in this repository sets `IsCheck`
on it, and a board that wants a code review adds a check column of its own
rather than widening this one (see the worked example at the end of this
section).

The stock text was written for a conflict alone and reworded when failing builds
were added. A migration (`RewordReviewPlaybook`) rewrote the row **only where it
still read the seeded text, byte for byte**, and left an edited row alone — the
guard every reword before the prescribed-text split (below) had to use. A reword
now moves `DefaultPrompt` and nothing else, so there is no guard text to chain
and no row left behind: an operator's own wording is untouched by construction,
and sits beside the new prescribed text. The reworded text does not say the merge is in progress, because
for a build dispatch it is not: it points at the branch section for the state of
the tree.

Nine rows are seeded, for the same reason the columns are: a Hatch whose agent
loop cannot run until somebody fills in a table is a Hatch that ships broken.
Seven come from `Playbooks.cs` (20260903204217) directly: the three
breakdown rows, the epic start, the analysis row, the implementation row and
the epic verification row. The eighth is the stock review playbook, which
`SeedConflictPlaybook` (20260928015312) adds, for every type, at `sonnet` and
`high`. The ninth is the closeout row `SeedCloseoutPlaybook` (20260930235751)
adds beside the implementation row, which it narrows to `leaf`. A feature that
does nothing until somebody fills in a table is the same failure as the loop
that cannot run, so the review migration seeds its row only where no row from
the review column to itself exists; an operator who deletes it turns conflict
and build work off, and nothing puts it back. The other eight cover every
transition an agent owns, so `go-to-work` on a fresh install needs no
configuration beyond an origin and a key. They are joined on column
*name*, because a migration cannot know identity-generated ids — so an install
that renamed its columns first seeds nothing, which is the right failure: a
playbook wired to the wrong transition is worse than an empty table.

**A row says when its prompt is the operator's own.** Every stock row carries
its prescribed text, `DefaultPrompt`, beside whatever the operator has made of
it, `Prompt`. `Prompt` is null until somebody writes their own wording, and a
dispatched session is handed `Prompt ?? DefaultPrompt`; on the wire `prompt` is
that effective text and `defaultPrompt` the prescribed one, or null on a row of
the operator's own making. A migration may move `DefaultPrompt` freely, because
nobody's wording lives in it. A row whose prompt differs from its prescribed text
is marked *the operator's own wording* on the Playbooks page, and its edit dialog
offers *Drop the operator's own wording*, which hands the row back. So does
saving a blank prompt, or one equal to the prescribed text — either stores null —
though a blank prompt is refused, as *a playbook needs a prompt*, on a row with
no prescribed text to fall back to. Once handed back, a reword reaches the row
again. Byte for byte was the test of the one backfill, `SeedPlaybookDefaultPrompt`,
which ran after `PlaybookDefaultPrompt` added the column and made `Prompt`
nullable: a stock row whose `Prompt` was identical to a stock text got that text
as its `DefaultPrompt` and a null `Prompt`; a row that was not identical kept
every word of its `Prompt`, and where it still opened with a stock text's first
line learned that text as its `DefaultPrompt`; a row whose opening sentence had
been rewritten learned none. Neither pass touched `UpdatedAt`.

Every one of the nine either edits the tree or judges it:

| Transition | Types / Shape | Name | Edits or judges |
|---|---|---|---|
| Breakdown → Backlog | `epic` | breakdown (epic) | judges |
| Breakdown → Backlog | `story` | breakdown (story) | judges |
| Breakdown → Backlog | `task,bug` | breakdown (task/bug) | judges |
| To Do → In Progress | `epic` | epic-start | judges |
| To Do → In Progress | `''` (any type) | analysis | judges |
| In Progress → In Review | `''`, shape `leaf` | implementation | **edits** |
| In Progress → In Review | `''`, shape `parent` | closeout | judges |
| In Progress → In Review | `epic` | verify-epic | judges |
| Review → Review | every type | the review row | **edits** |

*Judges* here means by instruction only. None of the seven is enforced by
`IsCheck` or `deniedTools`: each is told in its prompt to specify, verify or
report rather than to write the code, and nothing but the prompt keeps a
session to that. Only a [check playbook](#check), paired with denied tools, takes the write tools away.

Nothing re-asserts them afterwards, and nothing overwrites one. `TheFlow` moves
three of them from `inbox → todo` onto `Breakdown → Backlog`, because that is
where specifying a draft happens now, but it writes no prompt, no model and no
effort — an operator who has retuned a row keeps every word of it, and this only
says which transition their playbook is the playbook for. Rows that are not
there are not restored either: deleting a playbook is how an operator takes a
column back from the loop, and a migration that re-seeded one would be handing
it back.

The epic row on `In Progress → In Review` is the verification playbook: it
tells the session to check the epic's own acceptance criteria against the
trunk, not just that every child has closed — see [running an
epic](#running-an-epic). `VerifyEpicPlaybook` rewrote the row only where it
still read the seeded text, byte for byte, and left a retuned model or
effort alone; it seeded the row only where nothing already covered an epic on
that move and that shape, which is the one place it added a row, and only on a
board that did not already have one — so the count stays at nine on a
standard fresh install.

**The epic row on `To Do → In Progress`, seeded with the text *"you are
starting an epic, which means choosing what starts"*, is retired wherever the
board now does that itself.** [The epic pull](#the-hop) (HA-113) carries an
epic across that same move with no session once the WIP section covers it, so
a session told to choose what starts would be telling the board what it
already decided. `RetireEpicStartPlaybook` ran under the old guard: it deleted the row only where
it still read the seeded text, byte for byte, and only on a move that crossed
into the WIP section — an edited row, or a stock row on a board that had
never turned WIP on, was left exactly as it was, and still dispatches a session
the ordinary way.

**The breakdown, analysis and implementation rows ask for less, and verify
rather than redo.** The breakdown row caps a story's acceptance criteria at
five, retyping one that needs more to `epic` and breaking it into stories
under it; it and the analysis row now write the implementation plan onto the
tasks a ticket is cut into, not into its own description, with those tasks
filed into the column the ticket is leaving; and the implementation row, once
every one of a story's or a task's own child tasks stands in a terminal or
deferred column, verifies the ticket against the trunk criterion by criterion
and records or opens its pull request, rather than implementing it again.
`ShrinkStockPlaybooks` reworded all three rows only where each still read its
current text byte for byte — the same guard every reword before it used — and
left an edited row exactly as it was. From the prescribed-text split on, a stock
reword is a change to `DefaultPrompt` and needs no such guard (see *A row says
when its prompt is the operator's own*, above).

**A board that wants a code review adds a check column; it does not widen the
review row.** Take a six-column board, `To Do, In Progress, In Review, In QA,
Ready to Merge, Done`. Tick *Code written here* on `In Progress`
(`PUT /statuses/{id}/implementation`), so the build column is named. Add a
playbook on `In QA` with `IsCheck: true` and a read-only `DeniedTools` limit —
the example under [Playbook](#playbook) is the shape, and it is an example, not
a shipped list. A check's session judges only and cannot move the ticket: it
records a verdict and the runner applies it, forward on a pass, back to the
implementation column with the findings on a fail. The review row stays on
`Ready to Merge → Ready to Merge`, still only fixing conflicts and failed builds
at the merge gate.

## The unattended loop

`hatch go-to-work` is [the dispatcher](#the-dispatcher) run in a circle: read
the board, take the one issue an agent may advance, spend one increment on it,
and ask again. Given nothing else it runs until the board has nothing on it that
an agent may move, and then waits — asking again every interval, saying so once
and then rarely — which is what "leave it running overnight" has to mean if the
answer to "is anything left" can change while nobody is watching.

**The loop is a program, not the model.** The alternative was one long session
told to keep going, and it was rejected for two reasons that point the same way:
it would carry six hours of context into its last ticket, and the earliest
decisions in that context are exactly the ones nobody can audit afterwards. A
process per increment starts each ticket cold, on the model and effort the
ticket itself names — or, where it names neither, the ones its
[playbook](#playbooks) does — rather than whatever the session started as, and
costs less for the privilege.

A `--model` or `--effort` typed at `hatch work` still beats both, and is
still not carried across a night: a flag is one operator's opinion about one
increment, while an override is a fact recorded on a ticket somebody read. The
line naming the two values says which of them the issue chose.

### Agents

Between the loop and the CLI it spawns sits one seam, `IAgentAdapter`:
everything that knows an agent's own vocabulary — the argument lists for an
unattended and an attached spawn, the stream it reads back, the command that
rejoins a session, the shape a hook answers with — is reached through the
adapter rather than written into the loop. Today there is one, for `claude`; a
second agent would be a second implementation, not a second loop. `HATCH_AGENT`
chooses which, and `HATCH_CLAUDE_BIN` says where the claude adapter's
executable is when it is not on `PATH` (another agent's binary is named by a
setting of its own). `--agent <id>` on `hatch work` or `hatch go-to-work` beats
the setting for that run, and an id nobody has an adapter for is refused before
anything is spawned. The agent also decides which rows the runner takes: a row
pinned to another agent's model is skipped (see the `Model` paragraph under
[Playbooks](#playbooks)).

It has the same shape as `--model` and `--effort` and the opposite lifetime, on
purpose. Those are one operator's opinion about one increment, which is why a
night never carries them. Which agent a runner can spawn is a fact about the
machine it is on — where the binary is, whether it is logged in — and so is
true of every ticket that runner touches tonight. `go-to-work` therefore
resolves it once, before the loop starts, and carries it to the last
increment. That is deliberate: do not "fix" it to be read per increment to
match `--model`.

### What makes an issue actionable

Twenty conditions, the eighteenth a way out of the seventeenth rather than one
more gate, the nineteenth a fact about the issue rather than another way out,
and the twentieth a fact about the walk rather than either. An issue is the
loop's to pick up when it meets every one before it, and the sentence saying
which one it failed is what `work/queue` reports:

1. **There is somewhere for it to go, and that place is neither terminal
   nor protected — unless this is the close hop or the approve hop, each
   of which is a decision a person already made.** For most columns that
   is the column to their right: the end of the board is not a transition,
   the step into a terminal column is the operator's, and the step into a
   protected one is an owner's or approver's — *only the operator decides
   that something shipped, and only an owner or approver decides what else
   may pass*. The close hop crosses the terminal fold anyway, and the
   approve hop the protected one, because a pre-approval already given is
   that decision, made in advance (`Dispatch.cs:800`, `:803`) — see [the
   four paths past the terminal guard](#the-four-paths-past-the-terminal-guard).
   The review column is dispatched to itself instead of either (see the
   twelfth condition), and never into the column after it.
2. **No live [claim](#claim) is held by somebody else.** It is the only fold
   that says *this is being worked right now*; everything below it is about
   whether the issue could be worked at all, which is why nothing else is said
   about a ticket another runner is three minutes into. A caller naming a claim
   token of its own with `?heldToken=` is not folded by its own lease:
   re-reading the dispatch for a ticket one already holds is not a conflict.
   The claim may be a relative's — an ancestor's or a descendant's, at any
   depth — and that folds too; a token of one's own frees the relatives it
   holds as well as the issue.
3. **It is not held.** `Held` (`EfHatchIssue`) is a person's own
   mark, set only by `PUT /issues/{key}/hold` — never by a key, which keeps
   holding a ticket off the board a person's call and not an agent's. A fact
   about the issue like the claim before it, so `work/{key}` asks it too.
4. **It is not stalled past its own resume window, or stalled
   too many times in a row.** `StalledAt`/`StalledWhy` (`EfHatchIssue`), set by
   `PUT /issues/{key}/stall` — a dispatcher leaving a ticket where it found it, or
   the board itself after a build fails again on a fix (see [build
   check](#build-check)). Folded until `Hatch:StallResumeMinutes` have passed
   since `StalledAt` (naming how long and when it resumes), or — past that
   window — until a person moves it, once `letGo` has reached `Hatch:StallResumeLimit`
   (naming the count and the limit). A fact about the issue like the claim and
   the hold before it, so `work/{key}` asks it too.
5. **It is not paused, on its own row or on an ancestor's.** The nearest level
   other than normal, found the same walk the board and `hatch show` read — a
   person set it aside, and nothing picks it up until they set it back, named
   with the ancestor when the level is inherited. A fact about the issue like
   the claim before it, so `work/{key}` asks it too — unlike the ready date,
   [economy's and low's own gates](#expedite) and the assignee after them,
   which are the loop's own policy and are not.
6. **Its ready date has arrived**, read against the caller's calendar day. A
   card folded off the board is not one to spend an increment on tonight.
7. **It is not [economy](#expedite) level on an account with no reserve to
   spare**, asked only of an unattended pass. The account the dispatch would
   spend — the calling key's owner, resolved the same way a `?mine=true` pass
   resolves its own — is projected to reset with at least `Utilization.Reserve`
   points of every window Hatch knows the length of still unspent; where it is
   not, the issue is folded with the sentence naming the window and the
   numbers, and where no reading exists for the account at all, or the key
   belongs to nobody, with that sentence instead. `work/{key}` is not asked
   this at all, and neither is a hop — see the eighteenth condition.
8. **It is not [low](#expedite) level on an account whose session window is
   behind pace**, asked only of an unattended pass, reading the same account
   the seventh condition reads. The account's session window alone is projected
   to reset with its own pace matched or beaten — no reserve held back, so
   every reading that clears the seventh condition clears this one too; where it
   does not, the issue is folded with the sentence naming the window and the
   numbers, and where no reading exists for the account at all, or the key
   belongs to nobody, with that sentence instead. `work/{key}` is not asked
   this at all, and neither is a hop — see the eighteenth condition.
9. **Nobody's name is on it.** An issue [assigned](#assignee) to a person is
   somebody's to do, and an unattended pass leaves it alone. An issue assigned
   to an API key, or to nobody, is picked up exactly as it always was. A
   `?mine=true` pass inverts this condition rather than skipping it: it takes
   only a ticket assigned to the caller's own person or key, and folds every
   other one — see [one more, on `next` alone](#one-more-on-next-alone).
10. **It holds no unanswered question.** It is waiting on a person, and another
   agent sent at it would ask the same thing again or guess at the answer.
11. **The project's primary [repository](#repository) matches a remote the caller
   declared** — or the caller declared nothing at all, which this condition
   does not fold on, exactly as an issue page or an older CLI does not. A
   caller declares with `?remote=` (repeatable), `?standing=` and `?clones=`;
   the first two are checked here, on every move a session is spawned for (not
   a hop, unless it lands in the column where the code gets written), and the
   dependency below is checked on the move into that column alone. See [the dispatcher](#the-dispatcher) for the exact sentence.
12. **Nothing it depends on is unfinished** — and only when the move is into the
   column where the code gets written. Everything left of that still moves; an
   edge is satisfied only once the issue it names is in a terminal column. See
   [Dependency](#dependency).
13. **None of its children are still open**, when the move is out of the column
    where the code gets written. An issue standing there with at least one
    child not in a terminal column is not itself the work — its children are —
    so it is folded rather than carried into review. A deferred child counts as
    open, the same rule [Dependency](#dependency) already holds for a blocker.
    A childless issue is unaffected. An epic reads this differently: it is held
    wherever it stands at or past the implementation column, not only the
    column where the code gets written, and a deferred child does not count
    against it at all — see
    [Running an epic](#running-an-epic) for why. The sentence names how many of
    its counted children are still open, that its only child is not done, or
    that nothing is filed under it at all.
14. **The [WIP section](#wip) has room for it**, when the move is into it: the
    load, not counting this issue, is below the limit, nor counting any
    ancestor of this issue already standing in it — a family crosses together,
    at the cost of the one slot its nearest counted member already spent. Said
    after the dependency above, which needs other work to land, and before the
    epic's own limit and the verdict below, neither of which need anything at
    all once a branch is clean and nothing more of an epic is pulled in — a
    full section needs other work to leave. The load is always the board's own,
    whatever `ancestorKey` narrows the candidates to, and the limit is a fact
    about the board rather than the loop's policy: `work/{key}` is refused by
    it too, and overriding it
    is done on the board, by moving the card in.
15. **An epic above it has room too**, when the move is into the WIP section
    and this issue's parent is an epic: the load under that epic, not
    counting this issue, is below its own `WipLimit` (null reading as one) —
    see [An epic's own limit](#an-epics-own-limit). Said right after the
    section-wide check above, for the same reason — it too needs other work
    to leave before anything more of this epic is pulled in, and it is a fact
    about the board rather than the loop's policy, so `work/{key}` is refused
    by it too. Not asked for a task, for an epic moving itself, or for an
    issue whose parent is not an epic.
16. **In review, its branch conflicts with the trunk or its build failed —
    or it is [pre-approved](#pre-approval) and `MergeWork.Judge` would merge
    it or bring it current.** An issue in the review column is the loop's
    when a [merge check](#merge-check) says `conflicted`, or — on a branch
    that merges cleanly — when the [build check](#build-check) on the
    branch's current tip says `failed`; both are decided by a runner and by
    code, never by a prompt, and conflicts come first. A pre-approved issue
    needs no session either, the same way a conflict or a failed build does
    not — it is [the approve hop](#the-hop) instead, carried into the merge
    column with nobody reading the diff. The sentences for a passing build,
    a running one, one nobody has read on this tip, no checks, no branch,
    more than one branch and an unchecked one are what `hatch queue`
    prints. A clean branch that has merely fallen behind the trunk is left
    alone — but no longer universally: once the issue is pre-approved,
    falling behind alone is enough for the approve hop to bring it current,
    with no session either. See [the dispatcher](#the-issue-in-review-whose-branch-conflicts-or-whose-build-failed-is-dispatched-to-review).
17. **A playbook covers that transition for that type — or it does not need
    one.** Without one there is nothing to say to the session — and a column no
    playbook leads out of is exactly [how a column becomes the
    operator's](#status), which is why the absence is a fold rather than an
    error. **This is also where the issue's type is decided**, and the only
    place: a type an unattended run does not pick up is a type no row names for
    that move, said in the words that name the fix.
18. **Unless it does not need a session at all.** An issue that is
    [express](#pre-approval) and stands in a column marked
    [`ExpressSkips`](#status) is [a hop](#the-hop): the seventeenth condition's
    absence is answered not by a playbook but by the pass carrying the issue on
    itself, with `POST /api/hatch/work/{key}/hop`. Every condition above this
    one still has to hold — a hop is not an escape from a live claim, a pause,
    a ready date, an assignee, a question, a repository, a dependency, an open
    child, a full section or an epic at its own limit, only from needing a
    playbook, from [economy's or low's own gate](#expedite) — the seventh and
    eighth conditions — or from the park below — the twentieth. A clear hop
    is still carried across either way, parked or not; being carried across is
    not an escape from being the first clear row the walk saw, so a clear hop
    in a higher tier still parks the tiers below it.

    An epic standing in a column short of the implementation column, carried
    on each move right until it reaches that column, is a second kind of hop:
    something is filed under it, of any type. Where nothing is, the
    refinement is its own fold rather than a
    fall-through to the seventeenth condition: "nothing is filed under it — an
    epic runs its stories, and it has none". A story or bug standing in a
    column marked `ExpressSkips`, whose parent is an epic at or past the
    implementation column, is a third kind: the epic's own pull reaching the
    work filed under it, one card a pass, rightmost first — see [the
    hop](#the-hop) and [running an epic](#running-an-epic).

    Before any of the seven kinds is even asked, a story, bug or task
    standing left of the implementation column stops where it stands until
    its own direct parent reaches it — whatever column it stands in, not only
    one marked `ParentPulls`: "its parent has not reached the implementation
    column, so nothing pulls it forward yet". Asked above the hop, so the
    hold beats one: an express issue in a column marked `ExpressSkips`, and a
    story under an epic that has not itself reached the implementation
    column, are both folded here rather than carried. It never speaks for a
    top-level issue, a child already standing at or past the implementation
    column, or a review self-move.

    A child standing in a column marked [`ParentPulls`](#status) is a fourth
    kind of hop, for a different reason again: its parent has reached the
    implementation column (see [the hop](#the-hop)), and none of its siblings
    is standing further along, in board order, than the column this issue
    would be pulled into. Where the column is so marked but a sibling is
    already in flight, the refinement is its own fold rather than a
    fall-through to the seventeenth condition's "no playbook covers this":
    "a sibling is already in flight, so only one child is pulled through at a
    time" — the only one of `FamilyGate.Pulls`'s two conditions this fold can
    still be naming, since the hold above has already spoken for a parent
    that has not started.

    A childless issue whose brief is longer than its matched playbook's own
    `BriefLimit` is a fifth kind of hop, where `Columns.SendBack` — the
    column whose own `Target` is the implementation column — stands
    strictly left, in board order, of the column this issue stands in: a hop
    back to it needs no playbook and spawns no session, for the same reason
    the first four need none. Where no such column exists, or the issue
    already stands in it or left of it, this is not a hop, and condition
    nineteen, just below, folds the move instead.

    A [pre-approved](#pre-approval) issue standing in the review column, with a
    pull request recorded, whose branch `MergeWork.Judge` says would merge
    cleanly or needs only bringing current, is a sixth kind of hop — carried
    into the column ticked `MergesPullRequest` with nobody reading the diff
    first, since a pre-approval already is that reading. Where the branch
    instead conflicts or its build has failed, this is not that hop; the
    sixteenth condition's own fold governs it exactly as it would an issue
    with no pre-approval at all.

    A pre-approved parent standing in the review column with no pull request
    recorded, and no child left open by its own type's rule — an epic's own
    fold ignores a deferred child, where anything else counts one open child
    as enough to hold it — is a seventh kind of hop, carried past the terminal
    fold the first condition would otherwise raise, straight into the first
    terminal column strictly right of review. A childless pre-approved issue
    is never carried this way: nothing is filed under it for every child to be
    done of, so it is not this kind of hop at all.
19. **A childless issue's brief is no longer than its matched playbook's own
    `BriefLimit`, where one is set.** Not asked at all where the matched
    playbook leaves `BriefLimit` null, or where the issue already has a
    child — a brief big enough to want breaking down is exactly what children
    are for, so a parent is never folded by its own length. Tried first as
    the fifth kind of hop, just above: an over-the-limit, childless issue is
    carried back to `Columns.SendBack` wherever that column exists and
    stands strictly left of this one. Failing that — no such column on this
    board, or the issue already standing in it or further back — the move is
    folded instead, naming the length and the limit:

    ```
    its brief is 18,044 characters, and this move is spawned on at most
    8,000 - cut it into tasks first
    ```
20. **Nothing in a strictly higher [priority](#expedite) tier is clear.** The
    six levels are walked top down, once each, across every row the walk
    reaches; the first *clear* row the walk sees is remembered, and every row
    in a tier strictly below that one that would otherwise be clear is folded
    instead, with a sentence naming that row and its level — so `work/queue`
    reports the park rather than listing a lower tier as available when it is
    not. Two clear rows in the same tier never park each other, only a
    strictly higher one does, and the row remembered is never replaced once
    set: it is the first clear row in the whole walk, which is also the only
    one the sentence ever needs to name.

    ```
    HA-216 ranks normal and is clear - nothing at economy is picked up while higher-ranking work is available
    ```

    **A folded row parks nothing, whatever folds it** — a live claim, a
    running or unread build, an unmet dependency, open children that are the
    work, an epic's open children, a full WIP section, a ready date in the
    future, a sibling in flight, an unanswered question, a person's name on
    the ticket, anything at all. Most of these stand for days, not minutes,
    and a tier parked behind one would be a tier that never runs for as long
    as it stands — so a fold is never live for this rule's purposes, and the
    only thing that parks a lower tier is a row with no fold at all.

Fourteen of them — 1, 2, 3, 4, 5, 10, 11, 12, 13, 14, 15, 16, 17 and 19 — are
facts about the issue, and `work/{key}` asks them too. The eighteenth is as
well, and `work/{key}` answers it the same way `work/queue` does:
`WorkDto.Hop`. The other four — 6, 7, 8 and 9 — are the loop's policy and are
asked only when the pass is asking;
see [one more, on `next` alone](#one-more-on-next-alone). The twentieth is
neither: not a fact about one issue, since it depends on every other row the
same walk reaches, and not the loop's policy, since it folds `work/queue`
exactly as it folds `work/next`. It is a fact about the walk itself, asked only
by `Dispatch.ScanAsync` — so `work/{key}`, which never walks, never asks it,
and a person naming a ticket by hand is never told it is parked.

**The board is worked right to left**, for the reason the dispatcher gives, and
overnight it is the difference between a shape and a mess: a loop working left
to right would open every draft in the house before it finished a single story,
and the morning after would be a board where everything had started and nothing
had shipped. Right to left, the run pushes whatever is furthest along over the
line before it opens anything new, and the thing furthest along is a pull
request somebody can read.

### One loop at a time

**One loop per served checkout.** A loop is not limited to the one it is
standing in — `--repo <path>` (repeatable, on `work` and `go-to-work`) or
`HATCH_REPOS` (checkout paths joined on the platform's path separator) names
others it serves too, which is what lets one loop on a laptop work the two or
three repositories already cloned there and need no checkout of its own. Each
named path is resolved and validated at startup, in order, after the standing
checkout if there is one: it must exist, carry a `.git`, and have an `origin`
— a project's binding can only ever match a checkout that declares one —
before anything below is locked or claimed. `--repo` given on the command line
is the whole list for that run; `HATCH_REPOS` is not consulted, the same way
any higher settings layer wins whole rather than merging with a lower one.

**A directory of its own, cloned into as it goes.** `--workspace <dir>` (or
`HATCH_WORKSPACE`) is a third way in, for a loop with nothing checked out at
all: a directory the loop owns entirely, where a repository a project binds
that it has no checkout of is cloned the first time a ticket needs it, into
`<dir>/<host>/<owner>/<repo>` — a port in the host, if there is one, spelled
with `_` in place of `:`, since Windows takes no `:` in a name. The clone
happens with the ticket's claim already held, after the pick and before the
reset, so a checkout made for a ticket nobody ends up spending is a checkout
somebody else's increment would otherwise have to notice and clean up. A
remote two projects both bind is cloned once, whichever ticket asks for it
first, and served to both from then on; a directory already holding clones
from an earlier night is walked and served exactly as found, nothing
re-cloned. A clone that fails releases the claim, comments on the ticket with
git's own last line, and counts as a failed increment the same way a failed
spawn does — three in a row end the night. This is also what
[`?clones=`](#the-dispatcher) tells the server the caller is capable of, so a
project bound to a remote this runner does not yet have checked out is
offered to it rather than folded past.

**With no workspace named, there is still one.** A runner that is given neither
`--workspace` nor `HATCH_WORKSPACE` clones into a `workspace` directory beside
the per-user settings file (`ApplicationData/hatch/workspace`), built from the
operating system's per-user folder so it is the same convention on every
machine and a path on none in particular. It is held lightly, because nobody
chose it: it is not made until a clone needs it, and it gives way instead of
refusing. A run that stands in, or names, a checkout that is one of its clones
simply does not clone, and says nothing; a checkout that contains the
workspace, or a workspace that cannot be written, turns cloning off for that
run and says why. A second loop that finds the default workspace's lock held
serves its own checkouts and does not clone, where a workspace somebody named
is still refused. `--workspace off`, `HATCH_WORKSPACE=off` or `hatch config
--workspace off` turns it off outright for a runner that must never clone.

What says "one loop" is a directory under `TMPDIR`, holding the pid of the run
that took it, taken on every served checkout in order at startup — and, where
a workspace is set, on the workspace directory itself first, so a clone that
appears at three in the morning is already under a lock and a second loop
naming the same workspace is refused, by name, before it can clone into
anything the first is using. A refusal on any one releases every lock already
taken and says which checkout and which loop holds it. A directory because
creating one is atomic on every
filesystem this could land on and a file written with a redirect is not;
outside the repository because a lock in a tracked tree is a lock somebody
commits, and one that outlives a reboot is one somebody has to come and clear
by hand. A lock whose owner is gone — killed outright, or a machine that
rebooted out from under it — is cleared rather than honoured, which is the
difference between a loop that survives a crash and one somebody has to let
back in. The refusal names the pid *and* the checkout, because "already
running here" is ambiguous the moment there are two heres.

Per checkout rather than per machine, and that is the whole of what makes a
second loop possible. It was per machine while the [claim](#claim) did not
exist, because two loops with no way to divide the board between them would both
take the same ticket. The board divides it now, and the only thing two loops in
*one tree* would still collide over is the tree — one increment's reset landing
in the middle of another's branch.

**Two spellings of one path are one checkout.** On a case-insensitive filesystem
`/x/code/Hatch` and `/x/code/hatch` are one directory, and a lock keyed on the
string would let two loops into it, which is the one thing the lock is for. So
the key is the canonical path: each segment resolved to the name the directory
it sits in actually holds, with symlinks followed first. That is the answer
device-and-inode would give on Unix and is a question Windows can also answer —
and where a filesystem *is* case-sensitive, both spellings are separate entries
and both survive untouched, which is right there too.

So the two questions the `TMPDIR` directory used to answer together are answered
separately, each by the thing that can: the claim says one runner per ticket,
and the lock says one runner per tree.

The claim was once deferred, and each reason it was deferred has an answer.
A lease needs an expiry, and the expiry is lazy: a claim is dead when its
heartbeat is older than the TTL, evaluated by whoever reads the row, so there is
no timeout to tune and no sweeper to notice has stopped. An expiry needs a
heartbeat, and the heartbeat is the runner's own — it is sent by the process
holding the lease, not by anything the board has to run. And a run that dies
mid-increment leaves a row nothing may touch for exactly one TTL, after which
the next pass takes it with no operator action — and the lock it left behind is
cleared by the next loop in that checkout, so a `kill -9` needs nobody to tidy
up after it.

### What a runner does, in order

Before every pass, and after the heartbeat, the loop **polls the branches in
review** — see [checking the branches in
review](#checking-the-branches-in-review). It is not a step of a pass and holds
no claim; it is what keeps the board's verdicts on those branches current, so
that the queue below can tell a conflict from a branch that merges cleanly. A
runner that is paused polls nothing.

One pass, from the board to the release:

1. **Read the queue.** `work/queue` applies the loop's policy — the ready date,
   the person-assignee fold — and answers with every row and the reason it was
   folded. It has already folded past everything under somebody else's live
   claim, so what comes back clear is a shortlist.
2. **A clear row marked `Hop` is carried across, not claimed.** See [the
   hop](#the-hop): `POST work/{key}/hop`, no claim, no workspace reset, no
   spawn. One line is printed naming the issue and both columns, and the loop
   reads the queue again straight away rather than going on to the next step of
   this pass. A `409` here is an answer and not a fault, the same as the
   claim's below, and the walk moves to the next clear row.
3. **Claim the first clear row that is not a hop.** A `409` is an answer and not
   a fault: somebody took it between the scan and the take, and the pass moves
   on to the next clear row. Up to **five** attempts, because a board whose
   first five are all being worked is a board where waiting an interval is the
   honest thing to do — and an unbounded walk would take and release leases
   down a thousand-card column. Five refusals is a **busy** board, reported in
   its own sentence, naming the keys and who holds them. That is deliberately
   not the sentence an empty board gets: one means wait a minute, and the other
   means the night is over.
4. **Read the dispatch, by name.** `work/{key}?heldToken=…` and never
   `work/next`, which would answer with the first clear row *as it stands now* —
   a different ticket from the one just claimed, once another runner's lease has
   folded the row. The token is what stops the runner's own lease folding its own
   dispatch. A dispatch that comes back blocked is a ticket that changed under
   us: the lease goes straight back, and the walk goes on.
5. **Reset the workspace**, then spawn. In that order, and after the claim: a
   ticket held is a ticket nothing else will start, and a reset before the claim
   would be a fetch spent on an increment that never happens.

   For a repo-sourced project's issue, between the reset and the spawn the
   runner reads the trunk itself. The dispatch carries the file reference it
   was judged against - path, canonical, trunk, sha and hash - whichever
   layer spoke, file or board-wide; the runner brings the primary
   repository's `origin/<trunk>` current, reads the blob at
   `origin/<trunk>:<path>` through git (never a checked-out file, whose line
   endings the tree may have rewritten), and hashes it. Equal to the
   reference's hash means the playbook the server handed back - folded with
   the issue's own override, as today - is exactly what the trunk says, and
   the spawn goes on to the three things below. Unequal, or no file where the
   copy had one, refuses the increment instead: a sentence naming the file,
   the trunk, the copy's own sha and the trunk's current one, the copy put
   again with the bytes just read so the next pass dispatches on them, and
   the ticket let go uncounted - this is not one of its stalls, because
   nothing happened to the ticket. A fetch or a read that fails, or an origin
   that does not answer, spawns anyway and says so rather than refusing a
   session over a question it could not even ask.

   Three things are done at the spawn: two so that a message to the agent is
   heard, and one limiting what it can reach for:
   - **The hooks.** The runner writes a settings file into a directory of its
     own under the system's temporary directory — outside every checkout, so
     `git status` shows only what the session changed — and hands it to the
     session with `--settings`. It declares a `PostToolUse` hook (every tool) and
     a `Stop` hook, each ten seconds long, each running `hatch inbox <key>`. The
     per-step one asks Hatch at most once every fifteen seconds, by the
     modification time of a stamp file beside the settings; the stop one always
     asks, and a message it finds keeps the session going until it has been read.
     What comes back is printed as the hook's JSON, and `inbox` marks it read.
     Any failure — Hatch down, slow past five seconds, a Hatch without the route
     — prints nothing and exits zero, and the message stays unread for a later
     step. The directory is deleted when the session ends. An attached (`-i`)
     session gets no hooks: somebody is at the keyboard.
   - **The marking.** The dispatch carries the messages nothing has read
     (`WorkDto.Messages`); the prompt prints them under *Said to you since the
     last session*, and the runner marks exactly those ids read immediately
     before it spawns. `work --dry-run` prints the heading and marks nothing.
   - **The denials.** The spawn carries one `--disallowedTools <rule>` per
     entry in the matched playbook's `DeniedTools` — for every
     playbook-started session, headless, attached, conflict and build alike.
     See [Playbook](#playbook) above.
   For a [check](#check) dispatch the runner, once the session has ended, looks
   at the tree in every checkout it entered and only then applies the verdict.
   For a conflict dispatch the runner then **checks the branch again**, before
   it enters it — see [a conflict increment](#a-conflict-increment-is-judged-by-the-branch-not-by-the-column).
6. **Heartbeat**, carrying the last line the runner printed — and only when it
   has changed, so the time a card draws is when the line was printed rather than
   when a heartbeat happened to fire. A `--quiet` increment renders nothing and
   so carries nothing, which is what `--quiet` is.
7. **Release**, however the increment ended.

**A refused heartbeat ends the increment.** The lease expired underneath the run,
was taken over, or was cleared by the operator; the session is stopped rather
than left spending money on a ticket this runner no longer holds, the bill is
still posted — money was spent on that ticket, and a meter reading is not a claim
to have done the work — and nothing else is written. In particular no
[stall](#when-an-increment-does-nothing) is flagged: that would mark another
runner's increment as ours, and the ticket did not move because we stopped, which
the loop already knows. **A lost lease is not one of the three failures that end
a night**, for the same reason: it is the loop working correctly on a busy board.

Every other way a heartbeat can fail — a timeout, a `500`, an origin that did not
answer — changes nothing. The next tick tries again.

**A `200` is neither a refusal nor a lost lease.** It is the board naming this
runner as the one an emergency issue is preempting, and it is answered
differently from every other heartbeat failure: the lease is still good, so
the heartbeat keeps beating past it rather than stopping, and the session is
cancelled the moment the notice arrives — the same way a lost lease cancels
it, except this runner still owns the ticket. So it does what a lost lease
never does: it tidies, commits — untracked files included — and pushes onto
the issue's own branch, writes one comment naming the emergency issue and what
was pushed (or why not, per checkout), and releases the claim with
`preempted`. See [when an increment does nothing](#when-an-increment-does-nothing)
for the one exemption this buys it.

### The workspace, between increments

Every increment starts on the trunk, at the tip the remote has it at right now
— on every checkout the increment will use, in order: the ticket's project may
bind more than one repository, and each gets fetched, stashed and reset before
the session that reads all of them is spawned. A ticket for a repository this
loop has no checkout of, and [no workspace](#one-loop-at-a-time) to clone one
into, is folded past, with the reason, and never reaches this step at all; one
it can clone is cloned before this step, with the claim already held.

The loop fetches, stashes anything the tree was carrying, and puts the checkout
back on the default branch before it does anything else — so whatever comes
next starts from a base that is not in question. A binding's own base branch
beats `origin/HEAD` for that checkout; `HATCH_BASE_BRANCH` still only ever
overrides the checkout the loop is standing in, as it always has. What comes
next is [the branch step](#the-issues-branch), and then the spawn.

This is the loop's job rather than a [playbook](#playbooks)'s for the reason
every load-bearing sentence in a prompt eventually demonstrates: a playbook is
prose, edited by an operator who cannot be expected to know which of its
sentences the next four hours depend on, and read by a model that has a ticket
to think about. A night of unattended increments cannot have one run branching
off the last run's leftovers — that is how one session's unpushed commits arrive
inside another session's pull request — and the way to make something certain is
to stop asking for it. A playbook that says nothing at all about git now gets a
correct base.

Nothing it does destroys work that cannot be got back:

- **Uncommitted and untracked changes go into a stash**, named for the hour it
  was taken, and stay on the machine that took them. A stash rather than a
  discard because whatever is in the tree at two in the morning was probably
  left there by a person, and `git stash pop` is how they get it back. Ignored
  files are not touched, so a dependency directory or a local `.env` is never
  swallowed by one.
- **Committed work is never at risk.** A checkout moves `HEAD`; branches are
  refs, and the branch the last increment pushed is still under its own name.
- The exception is **a commit on the trunk itself and nowhere else**, which the
  reset moves off. The count is printed while there is still something to count,
  and `git reflog` holds the commits.
- **A branch whose upstream on origin has been deleted is deleted here too**,
  named on the terminal with the short sha it pointed at, so `git branch <name>
  <sha>` puts it back. Only that shape: a branch that never had an upstream is
  somebody's half-finished local work and is never touched, nor is one whose
  upstream is still there, nor one tracking a remote that is not origin — only
  origin was fetched and pruned, so a `[gone]` anywhere else is a fact the loop
  did not establish. Otherwise a checkout that has run a week accumulates a
  branch for every ticket it ever worked, long after the pull requests merged.

It runs once an increment is known to be due, not once per pass — on an idle
board the difference is a `git fetch` every interval until morning, against a
remote with nothing to say. The guarantee is about the spawn either way.

Two things can go wrong, and they are not the same thing. A fetch that does not
answer is **a minute of network**, and the loop waits its interval and asks
again, exactly as it does for a board that did not answer. A tree that cannot be
made current — not a repository, a trunk nobody can name, changes that will not
stash because something is half-merged — **ends the night**, because it is the
one condition under which every remaining ticket would be built wrong and no
further increment could tell the difference. Nothing is spawned and nothing is
spent on either.

The branch the trunk is is asked of the repository, not written down here: a
binding's own base branch first where the checkout has one, then `origin/HEAD`,
then the remote itself for a checkout that never got either, and
`HATCH_BASE_BRANCH` — for the checkout the loop is standing in only — for an
installation that calls it something else.

A merge the runner started and never finished — the process was interrupted
halfway through the branch step — is aborted by the next reset instead of ending
the night. The runner writes a marker in the git directory before it starts one,
which is how it tells its own unfinished merge from somebody else's; a merge
with no marker is a person's, and still ends the night, because it is not the
runner's to abort.

#### The issue's branch

A ticket that comes back for more work has a branch, and usually a pull request
on it. A session told to cut a fresh branch from the trunk abandons both — and
which branch is a ticket's was until now only prose in three places. So the
runner does it, after the reset and after the [self-change check](#where-the-loop-lives),
and before the spawn:

- **The branch of record is the one branch on `origin` named for the key**:
  `<key-lowercased>`, or that and a hyphen and anything after it, compared
  case-insensitively. `ha-3-` is not `ha-31-…`. There is no field on the issue
  for it, because the naming is already what every pull request here does, and a
  second place to keep it is a second place for it to be wrong.
- **A branch whose changes are all in the trunk counts as absent.** The test is
  that `git merge-tree --write-tree` of the trunk and the branch yields the
  trunk's own tree — which is true of a squash merge too, where the branch's
  commits are nowhere in the trunk's history and an ancestry check would call it
  unmerged for ever. Outside a project that lands on the trunk, the prompt then
  says the old branch merged and names a branch to cut that is not on `origin`
  (or here).
- **One branch: the tree goes onto it, at `origin`'s tip, and the trunk is
  merged into it.** The prompt says which branch, at which sha, how many commits
  ahead of the trunk, and whether the merge was a no-op, clean or conflicted. On
  a conflict the merge is left *in progress*, and the prompt lists the files and
  says to resolve them and commit the merge first.
- **Two or more unmerged branches: nothing is guessed.** The runner asks on the
  ticket which to use, with each branch an option, and spawns nothing. The
  answer arrives in the next session's prompt; an answer that names one of them
  is used, and one that names none is passed on to the session rather than asked
  again.
- **With no branch, the tree is on the trunk** and the prompt names the branch to
  cut, `<key-lowercased>-<slug of the title>` - or, where that name is taken (by
  a branch already merged, say), the same with `-2`, then `-3`, appended. The
  match runs from the key to the branch and never back, so `ha-3-2` is `ha-3`'s
  and not `ha-32`'s.
- **In a project that [lands on the trunk](#landing-on-the-trunk), a branch is
  not where the work is meant to end.** With no branch, the tree stays on
  `<trunk>` at `origin`'s tip and the prompt says to commit there and cut nothing
  (`Prompt.cs`, `BranchKind.None` with `TrunkLanding`; `Branches.Sentence` says
  the same on the terminal). The same holds when the only branches on `origin`
  have already merged into the trunk (`BranchKind.AlreadyMerged` with
  `TrunkLanding`, an earlier increment having landed or a branch left behind):
  the tree stays on `<trunk>`, the prompt says to commit there and cut nothing,
  and it names the merged branches only as having merged - no branch to cut.
  With one unmerged branch - a usage-limit push, since
  `PushForLimit` cuts one from the trunk when it finds work there, or an earlier
  increment's - it is entered as above, and the prompt adds that the session
  finishes on it, merges it into `<trunk>`, pushes `<trunk>`, deletes the branch
  on `origin` and records the landing. Such an increment has no pull request to
  open, and `## Landing on the trunk` stands in place of `## Opening a pull
  request`, as a task on its story's branch is told not to open one. It says to
  `git pull --no-rebase origin <trunk>` (a merge, never a rebase), run the build
  and tests again if that brought anything in, `git push origin <trunk>` (pull
  and push again if somebody landed first, never force), then `hatch landed <key>
  <sha>` for each repository and a comment with the sha. The increment is not
  finished until the landing is recorded, and `## Where this increment ends`
  still names review.
- **The copy on this machine is only a copy.** Behind `origin`, it is
  fast-forwarded. Ahead, it is used as it is. Diverged, the local tip is kept
  under `<branch>-local-<short sha>`, said on the terminal and in the prompt, and
  the branch goes to `origin`'s. Nothing that exists only here is thrown away
  silently.
- **Every checkout that has a branch for the key** is treated this way; the others
  stay on the trunk.

**A task that shares its story's branch is entered on the story's.** The server
names the branch for a dispatch (`WorkDto.BranchKey` — see
[One pull request](#one-pull-request)); every step above — the branch of
record, the cut name, the which-branch question, the push for a usage limit,
the trunk merged into the branch on leaving, the verdict reported afterwards —
then reads the story's key and title, and the verdict is filed on the story. The
which-branch question is still asked on the task, since the task is what its
answer unblocks. The runner takes the server's word back in two cases it alone
can see in git (`Lifecycle.Settle`): the task already has an unmerged branch of
its own on origin, or every branch the story has there is already in the trunk.
In both the dispatch is the task's own, as with sharing off. The prompt's
`## The branch` section says all of this in the first case, and a task on the
story's branch is told in place of `## Opening a pull request` not to open one
and not to run `hatch pr` — the story's closeout is told the one is its to open.
The runner's own check that an increment ending in review left no branch
without a pull request does not apply to such a task.

It is a **merge and never a rebase**, and nothing here is force-pushed: a branch
under review is somebody's to read, and that is the history this repository
already has. The prompt's `## The branch` section says all of this, and says it
overrides any instruction about branching in the playbook above it — because a
stock playbook still says to cut a branch from `origin/main`, which on a ticket
that has one would abandon it.

`hatch work KEY` does the same reset and the same branch step before it spawns.
One difference: a tree with changes in it is **refused, naming the files**, rather
than stashed, because somebody is sitting there and they are theirs.
`hatch work KEY --dry-run` prints the branch the increment would start on and what
it would merge, read from the refs as they stand — it fetches nothing and moves
nothing.

#### Leaving the tree

When the session ends, and before the claim is let go, the runner puts the tree
the way the next increment expects it. None of this happens when the lease was
lost — the ticket is somebody else's by then, and nothing is written on it or
pushed for it — but the checkouts are still put back on the trunk, so that a
restart between increments builds from there and not from whatever branch the
last session left checked out.

- **A merge, rebase or cherry-pick left in progress is aborted.** None of the
  three can be stashed, so left, each would end the night.
- **Uncommitted changes are stashed** under a message naming the ticket, and the
  ticket is told how many files.
- **Commits on the issue's branch that `origin` does not have are named** — the
  branch, how many, and the tip — and not pushed. Whether a branch is fit to
  leave the machine is the session's call: green before pushed.
- **Commits left on the local trunk go to `<key-lowercased>-rescued-<short sha>`**
  and the ticket names the branch, rather than leaving them to the reflog.
- **The checkout goes back on the trunk**, at `origin`'s tip.
- **A pull request's branch is brought up to date with the trunk**, when the
  issue has a pull request recorded and the branch on `origin` does not contain
  the trunk. The runner does it without a worktree: `git merge-tree` for the
  result, `git commit-tree` with both parents for the commit, and a plain push of
  that commit to the branch. The push is a fast-forward or nothing — somebody
  pushing in between makes it refuse, and it is never forced — and no agent is
  involved when the merge is clean. A merge that conflicts lists the files on the
  ticket and pushes nothing; turning that state into work for an agent is the
  dispatcher's job, once the step has told the board — see below. This needs git
  2.38 or later; an older one is said once, on the terminal, and the step is
  skipped. **A project that [lands on the trunk](#landing-on-the-trunk) has no
  pull request**, so the step has nothing to bring up to date: `Lifecycle.cs:231-236`
  never asks the board for one, and `Workspace.Leave` is called with
  `syncPullRequest` false. The merge check below still runs.
- **What origin's branch now looks like is reported to the board**, wherever the
  step fetched: a [merge check](#merge-check) for the branch, taken with the
  same `merge-tree` and put under the checkout's remote. That covers every
  outcome with one code path — clean at the new sha after a merge commit was
  pushed, clean where the trunk was already in the branch, conflicted with the
  files where the merge was refused, and no branch or more than one. A pull
  request that conflicts the moment it opens is therefore conflict work on the
  next pass, without waiting for the poll. Nothing is reported where nothing was
  fetched, because nothing is known, and a verdict the board refuses is a line on
  the terminal that does not stop the comment below being written.

Everything found goes to the ticket in **one comment**, so a reviewer reads one
thing. None of it fails the increment: a refused push — the forge, branch
protection, somebody else's push landing first — is a line on the ticket, and so
is a stash that would not go.

**An increment that moved, resolved or fixed and ended in review is refused as
unpublished** (`GoToWork.cs:1590-1596`, `Unpublished.RefuseAsync`; the same guard
in `WorkCommand.cs:632-639` for `hatch work <key>`) on either of two arms: a
branch left **unmerged on origin** (`left.BranchOnOrigin && !left.HasPullRequest`,
where `BranchOnOrigin` is the merge check's verdict being anything but `none`, so
a branch whose changes are all in the trunk counts as absent), or, in a
trunk-landing project, **no landing recorded** (`Lifecycle.HasLandingAsync`,
`LeaveOutcome.HasLanding`). The two call sites pick exactly one of those arms by
`work.Landing` (`var trunkLanding = work.Landing == Landings.Trunk`) rather than
evaluating both and OR'ing them, so a trunk-landing project is judged by
`!left.HasLanding` alone - never by `BranchOnOrigin` - and a trunk-landing issue
that already has a landing recorded no longer trips the guard merely because a
stray branch is still on origin; `RefuseAsync` takes that choice as a
`trunkLanding` parameter and words its comment, terminal line and question
around whichever arm fired. For a pull-request project the comment, terminal
line and question read exactly as before: the comment says "no pull request
recorded for the branch it pushed" (or `a branch on origin` where no branch was
cut or entered), the terminal line says "left a branch on origin with no pull
request", and the second occurrence in a row asks "has one been opened?" with
the options `pull request recorded` and `no pull request needed for this
ticket`. For a trunk-landing project the comment says "no landing recorded" and
names `hatch landed <key> <sha>`, the terminal line says "left no landing
recorded", and the second occurrence asks whether the work has landed, with the
options `landing recorded` and `no landing needed for this ticket`.

**The runner never pushes to the trunk**, in any project. Landing is the
session's own `git push`; `hatch landed` records it, the poll confirms it
(`Poll.ConfirmLandingsAsync`, `Poll.cs:290`), and the
[merge train](#the-merge-train) carries it. A session whose push was refused
leaves its commits on the local trunk, which the `-rescued-` bullet above names
(`Workspace.Leave.cs:84-86`).

**The one exception is a session that ran out of Claude usage** (below): before
any of the above runs, its tree is committed — untracked files included — onto
the issue's branch (cut first if the tree was still on the trunk) and pushed to
`origin`, no force - unchanged in a trunk-landing project (`PushForLimit`,
`Workspace.Limit.cs`): it commits onto the issue's own branch, cutting it first
(`CutName`) when the tree is on the trunk, and never pushes the trunk. That is
the one place a runner pushes a session's own work for it, because a session
that cannot say whether it is finished never gets to say whether it is fit to
publish either. `Leave` then runs exactly as above and
finds a clean tree, and the ticket gets the usage-limit comment described below
instead of the ordinary tidy-up note.

#### Checking the branches in review

Whether a pull request still merges with the trunk is decided by **git, in the
runner, and never by a prompt** — the [dispatcher](#the-issue-in-review-whose-branch-conflicts-or-whose-build-failed-is-dispatched-to-review)
only reads what the runner reports. Once per interval, between the heartbeat and
the pass, on an idle board and a busy one alike, the loop:

1. asks `GET /api/hatch/work/review` for every issue in the review column that
   this runner holds a checkout for, with the verdicts the board holds. It is
   the same repository rule as the queue's without the clone allowance — a poll
   clones nothing — and it is **not narrowed** by a claim, a question, a date or
   an assignee, because a verdict is a fact about a branch and not work. A
   repository this runner has never cloned is not polled by it, and the queue's
   *no runner has checked its branch* is the honest answer for it;
2. runs one `git ls-remote --heads origin` per checkout that has an issue to
   check, and builds a **fingerprint** for each: the trunk's sha and every
   branch the key claims, name and sha, sorted;
3. **fetches only where a fingerprint changed** — once per checkout however many
   of its issues moved — and then asks `git merge-tree` for each moved issue,
   also diffing the same two shas at the path the board named for the
   repository (`git diff --name-only`), where it named one, and puts the
   verdict — merge-tree's and the diff's together — to the board. The tree is
   never touched: `ls-remote` and `fetch` move no branch a worktree stands on,
   and `merge-tree --write-tree` and `diff --name-only` read and write objects
   only.

**Why it asks `ls-remote` first.** An idle loop that fetched every interval all
night against a remote with nothing to say is the thing
[the workspace](#the-workspace-between-increments) already says the loop does not
do. One `ls-remote` is the whole cost of an interval in which nothing moved, and
it says nothing and writes nothing.

**Why the fingerprint is not "compare with the last verdict".** A merged pull
request has no branch, and a verdict of `none` — like `ambiguous` — carries no
branch sha, so the board's verdict cannot say whether anything moved. Comparing
with it would fetch a merged branch's checkout every interval, and an in-review
ticket whose pull request has merged is the commonest thing in the column. The
runner instead remembers the fingerprint it took a verdict at, once the board
took the verdict, and also trusts a stored `clean` or `conflicted` verdict whose
trunk and branch shas equal what `ls-remote` printed, so a restarted loop does
not fetch everything once for nothing. A verdict the board refused is not
remembered, and is asked again next interval.

The terminal hears about a verdict only when it changes — `hatch: HA-12
conflicts with main (3 files)` — and an interval in which nothing moved prints
nothing. Every failure is one line and nothing more: an origin that does not
answer, a board that refuses a verdict or cannot be read, a git older than 2.38
(said once per run, as the step above says it). None of them fails the pass,
counts as a failed increment or ends the night, and a line that says what the
last poll's did is not said again until something changes.

**The build on the tip, asked of `gh`.** After each checkout's merge half, and
whatever it did, the poll reads the build on the tip of every branch it just
looked at that has exactly one candidate on origin — at the sha `ls-remote`
already printed, so it needs no fetch of its own — and puts a
[build verdict](#build-check) to the board. It runs `gh` in the checkout, as
whichever account the runner's `gh` is signed in as, and spends that account's
API budget:

- `GH_REPO` is set to the board's canonical identity for the repository
  (`host/owner/repo`, which is `gh`'s own `[HOST/]OWNER/REPO` form), where the
  project binds one, and neither it nor a host is set where it does not: `gh`
  resolves the repository from the checkout's remotes. Setting it also stops
  `gh` preferring an `upstream` remote over `origin`.
- **`gh api` ignores the host in `GH_REPO`** and asks the default host, so the
  two `api` calls also pass `--hostname` with the canonical's first segment.
  Without it a repository on another host would be read from a same-named
  repository on the default host, and the verdict would be wrong without
  saying so. This hands `gh` an argument taken from the board's own canonical;
  Hatch still names no forge and inspects no host, and a host `gh` cannot reach
  reads no builds. `gh run view` honours the host and is given none.
- It reads `check-runs` and the commit `status` for the sha, and classifies by
  the [four verdicts](#build-check). Only the status endpoint's `statuses` list
  counts: its own `state` says `pending` for a sha with no statuses at all.
  `--paginate` prints one document per page, so both calls use `--jq` and are
  read a line at a time.
- **A build is asked about until it concludes, and no longer.** `passed` and
  `failed` do not change on a sha, so they are asked once, and a board that
  already holds one for the tip vouches for it — a restarted loop does not ask
  again. `pending` is asked again next interval. `none` is asked again until ten
  minutes after the board first heard about the sha (`ShaSince`), because a
  push's checks take a few seconds to appear and a `none` straight after one is
  usually premature; past that a repository with no CI costs nothing more. A
  moved tip is a new sha and is asked afresh.
- **A runner that cannot read builds says so once and reads nothing.** A `gh`
  that is not installed, is not signed in, or cannot reach the host is one line
  — `hatch: could not read builds in <checkout> - <why>`, naming the checkout
  and not the issue so the dedupe holds — and the checkout's other issues are
  left alone that interval: one failed call, not one per issue. Nothing about
  it fails the merge half, a pass or a night, and the queue explains such an
  issue exactly as it does today. A board that predates build checks answers the
  write with a `404`, which is the same kind of line and is asked again next
  interval.

The terminal hears about a build only when it changes — `hatch: HA-12 build on
1a2b3c4 failed (api, CI)`.

**The trunk's own build, whether or not anything is in review.** Both halves
above run only for a checkout that holds an issue in review, because their
targets come from `/api/hatch/work/review` — a repository nobody happens to be
reviewing a branch of is never visited, and a quiet board goes all night
without its trunk read at all. A repository's trunk is nobody's issue, so once
per checkout that has an origin, the poll reads the build on its tip the same
way — reusing the `ls-remote` heads a checkout's review issues already took,
where there were any, so a checkout with something in review costs no second
one. It follows the same rules asking `gh` again: `passed` and `failed` are
asked once, `pending` every interval, `none` until ten minutes after
`ShaSince`, and a forge or a board that cannot answer is one line per checkout.

**The playbook file, read the same way.** For the same reason and on the same
schedule — once per checkout that has an origin, whether or not anything of
its is in review, and whether or not any project reads the file — the
poll reads [`.hatch/playbooks.yaml`](#playbook-file) off the trunk, reusing
the `ls-remote` heads already taken and fetching only where the sha moved. It
reads the blob itself through git (`origin/<trunk>:<path>`, never a file the
worktree has checked out, whose line endings the tree may have rewritten),
and puts it to the board with the trunk's sha, the bytes it found — or none,
where the trunk has none — this runner's name, and the forge link `gh browse
--no-browser` gives for the path on that trunk, where it has one.

**A fresh `failed` read branches before it is ever put to the board.** Where
the stored row's `ProjectKey` is one `runtime.Readout.Drives` says this runner
drives, and that sha's `RerunCount` is under the cap — three, enough for a
double transient blip (a flaky registry, a lost runner) to clear on its own,
not enough to spend a broken build's CI minutes forever behind retries that
were never going to help — the poll fetches each failing check's log from the
forge and asks whether it reads as transient. Where every one does, it re-runs
them on the forge and puts the trunk build with `Verdict = pending` and
`Reran = true`, not the `failed` it just read: the job really is queued again
the instant the re-run is taken, so it is not a fabrication, and `pending`
means the existing never-concluded handling above asks again next interval
with no change of its own, recovering correctly across a restart or a second
runner. Past the cap, with any failing check that does not read transient, or
on a project this runner does not drive, nothing new happens and the real
`failed` verdict is sent; a forge that will not take the re-run costs one line
— `hatch: could not re-run the failing checks in <checkout> - <why>` — and
still reports the failure for real rather than aborting the poll. `RerunCount`
resets to zero on the same two events `BugIssueId` does — the sha changing, or
a verdict for the pair passing — so a new build or an ended outage gets its
own budget. A `failed` verdict that was not re-run is where [the bug a project
may file without a person](#the-trunk-build-that-fails) is asked for next,
under the same `Drives` gate.

Either way the poll puts a [trunk build](#trunk-build) to the board.
`GET /api/hatch/trunk-builds` answers every stored trunk verdict once a poll,
and the runner matches its own checkout against it by the remote it spells
until the board's canonical form for that pair is known. The terminal hears
about a change the same way — `hatch: main build on 1a2b3c4 failed (api, CI)`,
or with `- re-running` appended where this interval took one — naming the
trunk rather than an issue.

**The landing half, asked of git and keyed on the checkout.** A trunk-landing
issue in review has no branch to merge-check and no pull request to ask `gh`
about; what it has is a [landing](#landing), the sha a session recorded and the
trunk it was landing on. Its targets come from the same
`/api/hatch/work/review` answer, and for each checkout that holds one the poll
(`Poll.ConfirmLandingsAsync`) reuses the `ls-remote` heads the merge half took,
so it costs no second one:

- **Skipped where nothing moved.** A landing is left alone when the trunk's tip
  on origin is the sha the board already holds as its `ContainedBy`, or the tip
  this runner last looked at it under. A sha that is *not* on the trunk is
  remembered too, so an idle loop does not fetch all night for a commit that
  has not landed.
- **One fetch, however many are due.** If any landing in the checkout is due,
  the checkout fetches once, and `ResolveOnTrunk` asks git whether each sha is
  in the trunk's history. The tree is never touched, as above.
- **A landing that is there is put to the board** with `PUT
  /issues/{key}/landing`, naming the trunk tip that now contains it as
  `ContainedBy`. That is the whole of the poll's part: it never moves a
  ticket, and it never reads a build for one — the trunk's own build is read by
  [the trunk half](#checking-the-branches-in-review) below, off the tip, whether
  or not anything is in review. A write the board refuses is not remembered,
  and is asked again next interval.
- **Failures are one line.** An origin with no such trunk, or a fetch that
  fails, is one line naming the checkout, and the checkout's landings are left
  for the next interval. The terminal hears about a landing only the first time
  it is confirmed — `hatch: HA-12 is on main at 1a2b3c4` — and a quiet interval
  says nothing.

**The pull request half, asked of `gh` and keyed on the issue.** This half, and
the ones after it, run on their own clock — `MergeTrain`'s, independent of
`--interval` — rather than this poll's, which is why their code lives apart
from it; the shape this paragraph describes still holds for all of them. See
[The merge train](#the-merge-train) for that clock itself. The
halves above are keyed on `(path, baseBranch)`, because a merge verdict
and a build verdict are per repository. A pull request is not: `PullRequestUrl`
is one field on one issue however many repositories its project binds, so this
half iterates the `/api/hatch/work/review` answer itself rather than the
per-checkout targets the others build, and reads each issue's pull request
once regardless of how many repositories its project names — the checkout is
only somewhere for `gh` to run, taken as the first one a project's
repositories resolve to. It asks `gh pr view <url>` for six fields — `--json
state,headRefOid,number,mergeStateStatus,reviewDecision,isDraft` — the first
because `git` and the merge verdict both lie about this: a squash merge leaves no
ancestry for `merge-tree` to find, and `MergeVerdicts.None` already means
three different things — a merge, a close, and a branch never pushed — none of
which `gh` confuses, and the rest because the board keeps the forge's account
of the pull request beside the issue (below). On `merged` it calls the route
that advances the issue out of review, with the url it read; a `409` is the row having moved under it
between the read and the write, the same as [the hop](#the-hop) - a line at
most, and the poll walks on to the next issue. `gh pr merge` in any mode -
`--merge`, `--squash` or `--rebase` - counts the same as the forge's own
button, because both leave `gh pr view --json state` reading `merged`; a
merge made with git alone and pushed by hand leaves the pull request open,
and closing it afterward is a close, which this half does not act on. On
`open` it puts the head sha,
the merge state and the review decision to
`PUT /api/hatch/issues/{key}/pull-request-state` wherever any of the three
differs from what `/work/review` carried for that row, so a quiet pull request
costs no call after the first; it does this for a review-column row and a
merge-column row alike, and an answer missing any of the three writes nothing.
The route answers `204` and the board stores what it was given, with the head
sha and when it was read; `MergeWork.Judge` reads the merge state and head sha (see
[the merge column's judge list](#the-approval-column)) and nothing else on the server does, and the
review decision is read by nothing. A `404` is a
board that predates the route, and reads as one line - "the board does not know
pull request state yet - it will be asked again next tick"; a `409` is the row
having moved, and is silent; any other refusal is one line. `merged` and
`closed` write no state. On `closed` a review-column row (`MergeWork`
null) is left alone too, the same as `open`: a closed-without-merging pull
request there is a real stuck state and is not this half's to surface. A
merge-column row
(`MergeWork` not null) is the one exception, and only where this runner is the
one this project's merge train is elected to - see the note after the two
halves below: there, `closed` calls `/work/{key}/closed` once, which records
`PullRequestClosedAt` the first time and moves nothing, and comments once -
"Its pull request was closed without merging." - deduplicated by issue key, so
a quiet interval after says nothing again. A pull request state of `unknown`
(not the merge state of that name, below) does nothing either way.
**There is no stored verdict and no window logic like `NoneWindow`**: an open
pull request is asked about every interval, by design, since noticing the
merge promptly is the point, and a merged one advances the issue out of review
- so `/api/hatch/work/review` stops returning it and nothing asks again. The
terminal hears about a change only - `hatch: HA-12 pull request merged ->
Done` - and a quiet interval says nothing. A `gh` that cannot answer is one
line per checkout, the same as the build half, and a board that predates the
route answers `404`, which is asked again next interval.

The forge's words become Hatch's here, in `GhForge` alone; the server never
sees GitHub's spellings, only the two vocabularies the route accepts. The merge
state is `clean`, `blocked`, `behind`, `dirty`, `unstable`, `draft` or
`unknown`. A draft is `draft` whatever `mergeStateStatus` says; `HAS_HOOKS`,
which is only a pending hook, is `clean`; `CLEAN`, `BLOCKED`, `BEHIND`, `DIRTY`
and `UNSTABLE` map straight across; and any other `mergeStateStatus`, or none,
is `unknown`. The review decision is `approved`, `changes-requested`,
`review-required` or `none`, and an empty `reviewDecision` and one the runner
does not recognise both read `none` - `unknown` is a word for the merge state
alone. A field the read failed to produce is neither: it is absent, and the
put is skipped.

**The approved-merge half.** For every row the merge column holds that the
judge marked **merge** and that is the merge train's own head for its project
(`MergeWork.IsHead` - the one row of a project's merge column eligible to act
at a time, named once per pass so two rows in the same column never disagree
about whose turn it is), this half re-reads the pull request's live state with
`gh` first, the same read the half
above takes. One `gh` already reports closed is left for the half above, which
has handled it or will next. One whose head has moved past the sha the judge
last called clean is reported once per sha - "Its pull request's head is now
{headSha}, not the {judgedSha} this runner approved to merge - skipping until
a fresh verdict says it is clean again." - and skipped until a fresh verdict
agrees it is still clean. Otherwise it runs `gh pr merge <url> --merge --admin
--match-head-commit <sha>`; a refusal is reported the same way, once per sha -
"gh would not merge its pull request - {why}". A merge that goes through calls
`/work/{key}/merged`, the same route [the four paths past the terminal
guard](#the-four-paths-past-the-terminal-guard) already documents an operator's
own report of a merge taking, comments "Merged its pull request at {sha}, as
{runner}.", and prints the same `hatch: HA-12 pull request merged -> Done`
line the half above prints for its own advance.

**The branch-update half.** The same shape, for every row the merge column
holds marked **update** and the train's own head: re-read the pull request,
leave a closed one for the half above, and report a stale head once per sha
the same way - "Its pull request's head is now {headSha}, not the {judgedSha}
this runner approved to bring current - skipping until a fresh verdict says it
is clean again." Bringing the branch current here is not
[`IWorkspace.Sync`](#the-workspace-between-increments) - that is a different
mechanism entirely, the local, no-worktree `git merge-tree` and `commit-tree`
push a session's own hand-back uses when leaving a pull request that has
fallen behind. This half instead asks the forge directly, with no local
checkout touched at all: `PUT repos/{owner}/{repo}/pulls/{number}/update-branch`,
with `expected_head_sha` set to the sha the judge called clean. Three outcomes
follow: updated comments "Brought its branch current with {trunk}, as
{runner}." and prints `hatch: HA-12 branch brought current with main` to the
terminal - no sha in either, since bringing a branch current is reported by
project and trunk, not by commit; conflicted comments "Its branch does not
merge cleanly with {trunk} - nothing was pushed." with no file list, unlike the
conflict `IWorkspace.Sync` reports elsewhere; anything else - the forge
unreachable, the account refused - is one `complain` line on the terminal
only, said again next interval the way a transient failure is, and nothing on
the ticket.

**The landing half, on the train's clock.** `MergeTrain.CarryLandedAsync` is
the fifth half, and the shortest: for every row the review poll returns whose
`landingWork` the judge marked **land**, it calls `POST /work/{key}/landed`.
It asks nothing of git or `gh` and keeps nothing across ticks — the route
re-judges the row itself and writes the ticket's comment, so this half writes
none. A `409` is the row having moved under the call, and is walked past
silently; any other refusal is one line, kept from repeating verbatim, and the
next tick tries again. A landing that goes through prints `hatch: HA-12 landed
on main -> Done`. It is the one half here that acts on a *review* row rather
than a merge-column row, and the election below gates it all the same.

The merge-column exception just above, and every half after it, run only
where this runner is the one a heartbeat answer elected to drive this
project's merge train: a choice made for a runner, never by it, and not this
section's to explain further. The landing confirmation above is not among
them: it is the poll's, and every runner that holds the checkout makes it.

### When an increment does nothing

The one failure mode of an unattended loop that is dangerous rather than merely
disappointing: a session ends with the ticket in the column it found it in.
Nothing about the board changed, so the next pass picks the same issue, spends
the same money, and fails the same way — all night. Every other way an increment
can go badly costs one increment.

So a stall is written on the ticket: a comment naming the session that ran and
the transition it was trying to make — with the `claude --resume` command, since
resuming the conversation is most of why a stall is worth recording rather than
merely counting — and, if nothing is already open there, **a mark**:
`StalledAt` and `StalledWhy`, set by `PUT /issues/{key}/stall`.

**A [check](#check) that did not judge is a stall of its own.** One that left
the tree changed, or ended with no verdict recorded, has nothing applied and is
let go of or flagged like any other stall, with its own sentence in the line the
runner prints; one held on a second consecutive fail has its question to say
why, and is not counted.

**A hop is never a stall.** No session ran, so there is no session to name and
nothing was spent on a ticket that did not move - the ticket did move, one
column, and the runner never held a claim to have written anything on it. See
[the hop](#the-hop).

**Filing is progress.** An increment that ends with its ticket in the column
it started in, but with new keys under it that were not there at dispatch, is
not a stall either - it is measured against the dispatch's own children, so a
child filed and a child moved elsewhere during the same run both count, and
nothing about a child that disappeared counts against it. This matters because
an increment's job is not always to move the one ticket it was dispatched
against: a verification that finds gaps in an epic files them and has to leave
the epic exactly where it found it, and a breakdown that filed every story it
was asked for did the work even if it forgot to also move the task describing
it. It applies the same way to every issue type, and a ticket that filed work
is offered again on the next pass exactly as if it had moved, unless something
else - a dependency, a playbook, a question - holds it back.

**Neither is a usage limit.** A session that ends because the Claude account it
ran under hit its usage limit is read from what it said on its way out - the
read now covers every spelling a limit comes back under (session, weekly, Opus,
Sonnet and Fable), as well as the pay-as-you-go phrasing that never says "limit"
at all (`You're out of usage credits · resets ...`); it handles a dated reset
given either with "at" or with a bare comma, an hour with no minutes, an unknown
zone (falls back to UTC rather than giving up), and a reset naming no zone at
all, read against the runner's own zone rather than falling to the one-hour-away
treatment. A time with no date is the next occurrence of that clock time after
the session ended, in the named zone; a limit the session named but whose time
could not be parsed at all is treated as an hour away, and both the terminal and
the ticket say so. No stall comment is written and no question is opened — the
ticket is left exactly where the session left it, with no flag blocking the next
dispatch — because nothing here is wrong with the ticket. The terminal says the
runner is out of Claude usage and the reset time it read, and after [the tree is
pushed](#leaving-the-tree), the ticket gets one comment: that the runner ran out
of usage, when it expects to resume, the branch and sha that were pushed (or
that there was nothing to push), and the `claude --resume` command. The night's
tally counts it as **interrupted**, a third list beside *moved* and *stalled* —
not a failure, and not one of the three in a row that end a night, the same
treatment a lost lease gets and for the same reason: this is the loop working
correctly against a spent account, not a broken increment. See [runners on the
board](#runners-on-the-board) for what a runner does about the account itself.

**The wait re-asks the same probe.** The sentence above is only the first
guess at when the account resets - the exhausted wait does not simply sit
until the wall clock reaches it. HA-173's own `/usage` probe ("Between
sessions, HA-173 asks a second source", above) already runs at the top of
every pass, including every pass through this wait, and for as long as the
wait lasts its session window is the authority over the sentence, not the
other way round: room left in that window clears
the wait outright, this same pass, whatever instant the sentence gave; a reset
instant that disagrees with the one carried in replaces it, in either
direction; and a probe with no session window at all - or no answer at all -
touches nothing, leaving the sentence standing. A correction says itself once,
the same "out of Claude usage until ..." line the sentence itself prints, and
not again merely because a later probe lands on the same corrected reading.

**The wait cannot outlast the window.** The reset instant above is only
ever a guess, and the "next occurrence of that clock time" rule it is
built on can roll a single second of clock drift all the way to tomorrow -
an hour's wait read as a day's. So the wait also carries the instant the
limit was hit and which of the two windows Hatch knows the length of - a
session's five hours or a weekly one's seven days - it belongs to. Once
that much time has passed, the wait ends exactly as if the account had
reset on its own, whatever the sentence's own instant still says and
whether or not a probe has said anything to the contrary. A ticket a
still-spent account refuses costs one session, not the rest of the window;
the three-in-a-row stall machinery is what catches a spawn that keeps
being refused.

**Neither is a preemption.** The board can order the runner holding the
lowest-priority ticket to stand down for an emergency one — see
[what a runner does, in order](#what-a-runner-does-in-order), where the `200`
that says so arrives on the heartbeat. No stall comment is written and no
question is opened, the same as a usage limit and for the same reason: nothing
here is wrong with the ticket, and the ticket is left exactly where the
session left it, free for the next pass. What *is* written is the one comment
that section already describes: that this runner was preempted, which
emergency issue took it, the branch and
sha that were pushed (or why not, per checkout), and the `claude --resume`
command — so this is not a case where nothing happens, only a case where
nothing is asked of a person. The night's tally counts it as **preempted**, a
list of its own beside *moved*, *stalled* and *interrupted* — not a failure,
and not one of the three in a row that end a night, the same treatment a lost
lease and a usage limit get and for the same reason: this is the loop working
correctly on a busy board, not a broken increment. The very next pass finds
the emergency issue at the top of the walk and picks it straight up.

**A mark rather than a question that waits on a person forever, which is what
this used to be.** Most of the time whatever happened is weather, and a ticket
that can try again by itself in fifteen minutes does not need to cost a
person's night too. The guard that keeps it from trying forever is `letGo` and
`Hatch:StallResumeLimit`: a ticket that stalls that many times in a row holds
for a person exactly as a question used to make every stall do, because a run
of failures that long is no longer weather.

A ticket already waiting on a question gets the comment and no mark — that
question is already the flag, usually raised by the session's own way out.

#### A conflict increment is judged by the branch, not by the column

An increment on an issue in review whose branch conflicts with the trunk starts
and ends in the same column, so "the ticket is where it was" is what success
looks like there, and the rule above would call every good one a stall. It is
judged by the **verdict** instead — the answer git gives about the branch on
origin — and it is the runner that asks, in the same code for the loop and for
`hatch work`:

- **Before anything is spawned**, after the claim and the reset, the runner
  checks the branch again against the refs the reset just fetched, and reports
  what it finds. The board's verdict was read before the claim and may be
  minutes old. If the conflict has gone, nothing is spawned and no increment is
  counted: the claim goes back, the verdict goes up as clean, and the loop reads
  the board again at once — unless the board refused the verdict, which still
  calls the issue conflicted, and a pass that went straight back would find it
  again in a tight loop. A check that cannot be made — a git older than 2.38, a
  fetch that failed — spawns nothing either, and the pass waits: a session
  should not be spent on a merge that may not exist.
- **The session starts on the issue's branch with the trunk merge in progress**,
  exactly as [the branch step](#the-issues-branch) leaves it. Its prompt is the
  review playbook, then the ticket — whose header reads `In Review (resolving
  conflicts with <trunk>)` — then a `## The conflict` section naming, for each
  repository that conflicts, the branch and the trunk with both shas and the
  files. Those facts are the fresh verdicts the recheck just took, not the
  board's.
- **After the session and its work log**, the runner fetches, asks git again and
  reports each verdict. Nothing conflicts: resolved, said as `HA-12 conflicts
  with main resolved` on the terminal, in the tally and on the runner's row, and
  not a stall. The column is still read and reported, and a session that moved
  the ticket out of review is reported as having moved it — and judged by the
  verdict all the same.
- **Anything that still conflicts is a stall**, flagged like any other and with
  the same mark, but the comment says the branch still conflicts with the
  trunk and lists the files, in place of "left this issue where it found it". A
  conflict nobody can resolve costs **one increment and not a night**, for the
  reason every stall does. A check that could not be made is *not known* and
  not a stall — the rule the column read already follows — and nothing is
  flagged on a guess.

None of it runs when the lease was lost: the ticket is somebody else's by then,
and what the board is told about its branch is theirs to say.

#### A build increment is judged first by the push, then by the build

An increment on an issue in review whose build failed starts and ends in the
same column too, so it is judged by the **branch** as a conflict increment is —
in the same code for the loop and for `hatch work` — but what it is asked is
different, because **CI will not have run by the time the session ends**. The
session is judged first by whether it pushed, and the build on what it pushed is
judged later, by the board:

- **Before anything is spawned**, after the claim and the reset, the runner reads
  the build on the branch's tip again: for each checkout that holds a failed
  verdict it takes the tip from `ls-remote` (no fetch, no tree touched), asks
  `gh`, and reports what it finds, whether or not the tip moved. If the tip has
  moved, or the build is no longer `failed`, nothing is spawned, no increment is
  counted, the claim goes back and the loop reads the board again at once —
  unless the board refused the verdict. A moved tip whose new build fails is
  picked up as fresh build work by the next pass, which is right. **A forge that
  cannot answer is unknown, not clear**: nothing is spawned and the pass waits.
- **Only then are the logs read.** For each check still failing on the dispatched
  tip the runner asks `gh run view --job <id> --log-failed`, and the excerpt is
  the lines *before* the failure and not the cleanup after it: up to 150 lines
  ending at the log's last `##[error]` line (or its end where there is none),
  each line's job, step and timestamp prefix stripped, at most 12 KB a check. A
  check that is not a job, or has no log, is *no log*. The prompt is composed
  from what was just read.
- **The session starts on the issue's branch with the trunk merged in**, as every
  increment does, and no merge in progress. Its prompt is the review playbook,
  then the ticket — whose header reads `In Review (fixing its failing build)` —
  then a `## The failing build` section: for each repository the branch and the
  sha, for each failing check its name and link and its excerpt in a fenced
  block, and the command that prints the whole log. The section is capped at
  40 KB; past it a log is left out and the command that prints it is named.
  Failures that only reproduce under the database tests are the reason the
  excerpt is there: the session's own machine will skip those tests, and the log
  as CI reported it is the only account of the failure there is.
- **After the session and its work log**, the runner reads origin's tip again,
  before it leaves the tree, so that a trunk merge it pushes on the way out is
  never taken for the session's fix. **A new tip is a fix pushed**, and not a
  stall: the terminal, the tally and the runner's row say `fix pushed, build
  pending`, and the runner puts a `pending` [build verdict](#build-check) on the
  new tip *marked as a build increment's*, which is what lets the board mark the
  issue stalled again if that build fails. One repository moved of two is
  progress. **A tip that did not move is a stall**, flagged like any other with
  the same mark, and the comment says the build still fails and names the
  checks. A tip that could not be read is *not known*, and flagged as such.
- **A build that fails on the tip an increment pushed is not sent to another
  agent.** The board marks the issue stalled, naming the checks that still fail
  — so a build nobody can fix costs one increment and then a wait, not a night.
  A failure on a tip somebody else pushed is new work, as it is today. The mark
  does not stop the loop by itself: the issue resumes and is dispatchable again,
  still failing on that sha, after `Hatch:StallResumeMinutes` — unless it has
  already stalled that way `Hatch:StallResumeLimit` times in a row, at which
  point it holds for a person instead of resuming again.

None of it runs when the lease was lost, for the reason the conflict's does not.

### Where the loop's rules live

On the server. `work/queue` is the same walk `work/next` takes, reported rather
than acted on, and `GetNextWork` is the first clear row of that scan rather
than a second walk that happens to agree with it — so a runner asks two
questions and cannot get two different boards. Everything above is decided in
one place, in one order: the routes are
[`WorkController.cs`](../src/Hatch.Api/Modules/Hatch/WorkController.cs), and
the one scan every route is built on — the walk, the fold and the sentence for
each — is
[`Dispatch.cs`](../src/Hatch.Api/Modules/Hatch/Dispatch.cs), registered in DI
the way `RankService` is, so anything else that needs to ask what a pass would
do asks it there instead of re-deriving a second opinion.

The alternative was **the client**, and it is the cheaper thing to write:
`queue` already parses the board, and folding the not-yet-ready and the
wrong-typed out of it is a few lines. It was rejected because it puts the rules
where the *caller* is, and there is more than one caller — a terminal, a loop,
and the issue page — so the first renamed column would leave two of them
disagreeing about what is workable, silently, with nobody watching. A runner's
whole job is to spend increments and say what happened; it decides nothing about
which.

The second of those two reads is `work/{key}`, named, carrying the runner's own
claim token — never `work/next`. The claim between them is what closes the
window the old pair had: a row read as clear and taken by somebody else in the
meantime comes back from the named read carrying their sentence, and the pass
walks on instead of spawning into it.

That is also why the loop passes no `--model` or `--effort` of its own, though
`work` accepts both. An override typed for one increment is one operator's
opinion about one ticket, and a loop that carried it across a night would be
applying it to tickets nobody looked at.

**And it is why the hop is a write the server makes, on its own route,
[`POST work/{key}/hop`](#the-hop), rather than the CLI calling the ordinary
move endpoint.** `IssuesController.MoveIssue` moves anything a key asks it to
— it judges nothing, because a person dragging a card has already judged. A
runner asking it to perform a hop would be the runner deciding the move was
safe, which is exactly the rule this section argues against: the caller spends
and reports, and decides nothing about which. Judging and moving in the one
write also means a stale runner cannot carry an issue the server would no
longer carry, and the history can say *express* in the same event the move
itself writes, rather than a second write racing the first.

### Where the loop lives

The CLI is one program — [`src/Hatch.Cli`](../src/Hatch.Cli), published as a
single binary called `hatch`. Every command is a command of it: `board`, `next`,
`queue`, `show`, `start`, `move`, `comment`, `pr`, `landed`, `depends`, `one-pr`,
`ask`, `questions`, `answer`, `api`, `config`, `work` and `go-to-work`.

One command in it is not for a person: `inbox`, which the hooks a spawned session
runs call to be handed a message and mark it read. It sits beside
`runner-claude-token` in `Program.Internal`, a command no session is ever told
about — if sessions knew of it, one could mark its operator's messages read
without reading them, and the page would say something untrue.
`DocsContractTests` holds that.

It got there in two steps, for two different reasons.

`work` and `go-to-work` moved first, because of what they do rather than what
they say. Every other command is one request and a sentence about the answer.
These two are a lease with a clock on it, a heartbeat on a background thread, a
session that has to be killed the moment the lease goes, and three signals — and
none of it could be tested, because nothing in this repository tested a shell
script and a race is precisely the thing a hand check cannot catch twice. The
question was asked as "how is the claim verified", and the honest answer was
that in a shell it could not be: the alternative on the table was a stub harness
of some six hundred lines driving `hatch.sh` through a fake `curl` and a fake
`claude` on `PATH`, which is a second program to maintain and one that can only
assert what a recorded request log happens to show.

The other fifteen followed, and not because shell was the wrong language for
`curl | jq` — it was a perfectly good one, and the ported code says the same
things. They followed because an operator who clones Hatch into their own house
has no copy of `scripts/hatch.sh` on their `PATH`, and a tracker reachable from
one checkout is a tracker for one person. `hatch` installs once, runs from
wherever somebody is standing, and keeps its settings with the person rather
than in one repository's `scripts/` directory. The seeded playbooks name that
command, so an agent working a friend's repository is told how to reach the
board in words that are true there.

[`scripts/hatch.sh`](../scripts/hatch.sh) is still here, and every command still
works through it — it finds or builds the binary and hands over. What is left in
it is the two things that are genuinely its own: that door, and the restart
supervisor below, which a process cannot be for itself.

So: a console program in the solution, compiled against
`src/Hatch.Contracts` — the same records the API serves, so a fixture that
goes stale does not quietly deserialise into a dispatch with empty fields, it
stops compiling — and asserted in
[`src/Hatch.Cli.Tests`](../src/Hatch.Cli.Tests), which `make test` and CI
already run because the solution already builds and tests everything in it. Two
loops racing one ticket, a lease refused mid-session, an interrupt between a
claim and its release, two spellings of one checkout: each of those is a test
that runs in milliseconds against a stub wire and a stub session, with no server,
no key and no agent.

Being cross-platform came along with it, and is worth naming because the shell
was never going to be: `hatch.sh` is Bash 3.2 on purpose, for macOS, and a
Windows operator had no runner at all. A Windows operator now has the program,
and in a checkout `scripts\hatch.ps1`, the PowerShell twin of `hatch.sh`, as
its door and its supervisor.

Two things in the shell version existed only because it was a shell, and are
gone rather than translated. The session id and the bill used to travel back
through the render pipeline wearing a control character, because every stage of
a pipeline is a subshell and nothing set in one survives; they are now an object
the renderer writes to. And the line a claim carries used to go through a file
under `TMPDIR` for the same reason, which forced the heartbeat to defend against
reading one mid-write; it is now a field.

`hatch.sh` prefers a built binary and falls back to `dotnet run`, so a checkout
with the SDK needs no build step — `make build-hatch` is the optimisation, and
`HATCH_RUNNER_BIN` names a binary for a machine with no SDK at all. That target
is deliberately a single fast Release build of this machine's own platform,
because the restart path below calls it between increments; `make publish-hatch`
is the other thing, and makes the self-contained single-file binary an operator
downloads — `win-x64`, `osx-arm64`, `osx-x64` and `linux-x64`, all four built on
every pull request.

**A fresh install does not run that target; it opens the Runner page.** The API
image publishes the same four binaries from the same build as itself
(`Dockerfile.api`) and Hatch's **Runner** page hands out the one matching the
browser's platform, beside the revision all of them were built from, the three
things to have installed first, and the two commands to type:

```
hatch config --origin https://hatch.<your domain>
hatch go-to-work
```

`config --origin` is the non-interactive third mode of `hatch config`: it writes
the origin alone and leaves the key and the claude path exactly as they were, so
it is safe to paste on a machine that is already configured. The page fills in
this Hatch's own origin, because the address bar is the one thing about an
install nobody can get wrong.

`config --key <key>` is its mirror for the credential: it writes the key alone,
needs an origin already set, and refuses an empty one. With the wall up the Runner
page shows it beside the origin command, and the API keys page is where the key
comes from. A key typed on a command line stays in shell history, which the
no-echo prompt of plain `hatch config` does not, so the page offers both.

So `make publish-hatch` is how *this repository* builds the artifact, and the
Runner page is how *a person* gets it. A friend with the stack running needs the
image and nothing else — no SDK, no clone, no copy of this Makefile.

One consequence of publishing trimmed is worth naming, because it fails nowhere
before the operator's machine: a trimmed .NET application has reflection-based
JSON switched off outright, so every wire record is registered in a
source-generated context (`HatchJson`). A record nobody registered refuses by
name at the call that needed it, rather than deserialising into a dispatch with
empty fields.

One difference between the two commands is worth naming, because it is the whole
of how a loop restarts itself. `work` is `exec`'d: one increment has nothing to
carry forward and nothing to come back as, so it replaces the shell rather than
being watched by it. `go-to-work` is *run*, in a loop, because a process cannot
exec itself into a newer build — relaunching the same binary relaunches the same
code, and the new source has to be compiled by something that outlives the
process being replaced. `hatch.sh` was already that something, and
`hatch.ps1` is the same something in PowerShell. It runs `work` and `go-to-work`
from a copy of the build under the temp directory, because Windows will not
overwrite a running binary and a session working a Hatch ticket builds
`src/Hatch.Cli` in the same checkout.

### What it stops for

Nothing, by default, and that is the point: a run that stopped for a reason
nobody asked for is a run somebody has to check on. Every stop is asked for,
except the last one:

- `--once` — one pass, whatever it found, and out. The loop's own dry run
  against a board that is not a fixture.
- `--max-runs N` — that many increments.
- `--max-spend USD` — measured from what the increments reported, not
  estimated; an increment whose agent reports no price is not in that sum, so
  it cannot bind one.
- `--until HH:MM` — wall clock. `--until 06:00` typed at eleven at night means
  the morning, because the alternative reading is a loop that stops seventeen
  hours before it started.
- `--stop-file PATH` — touch it and the loop ends. A path, so stopping needs
  nothing but a shell: no pid to find, and no signal that could land in the
  middle of a push. It is read between increments, so the one in flight finishes
  first, and a path that already exists is refused at startup rather than read
  as a board with nothing on it.
- **Three failures in a row** — the one nobody asks for. A failed increment is
  not a reason to stop; a ticket can be wrong and a test can be flaky, and the
  next ticket is a different question. Three in a row is something else:
  whatever is broken is broken for every ticket, and the loop is now spending
  money to prove it. An exception anywhere in a pass — picking, claiming,
  preparing the tree, the session, judging it, or tidying up — is one failed
  increment and nothing more: the claim is let go of, the ticket is told why
  in a comment naming the error, the tree goes back to the trunk, and the loop
  goes on to its next pass. It feeds this same count rather than a list of its
  own, so the list of things that end a night does not grow. **A usage limit
  is not one of the three** — see
  [when an increment does nothing](#when-an-increment-does-nothing) — and
  neither is [the wait that follows one](#runners-on-the-board): the loop is
  spending nothing while it waits, so there is nothing there to fail.
- **A workspace that cannot be made current** — the other one nobody asks for,
  and the only condition that ends a night without an increment having failed. A
  tree that will not reset is a tree every ticket would be built wrong on, and
  the loop has no way to make it right. A fetch that merely did not answer is
  not this: that is a wait, and the next pass tries again.

One thing that looks like a stop is not: **a restart**. A loop that spends the
night improving this repository is running the version it started with, and
would be until somebody came and stopped it — work that lands at one in the
morning never reaching the run that wrote it. So the loop watches its own source
and comes back as the new version, and the terminal says which trigger fired
rather than going quiet and back in a way that reads as a crash.

The runner asks for it by exiting **75** (`EX_TEMPFAIL`, "try again", which
collides with nothing else it answers with), and `hatch.sh` (or `hatch.ps1`)
rebuilds it and runs it again. Two triggers, because the answer to which one on
the ticket was both:

- **Its own source changed on the trunk.** The set is `scripts/hatch.sh`,
  `scripts/hatch.ps1` and everything under `src/Hatch.Cli` and
  `src/Hatch.Contracts` — the loop,
  the wire records it is compiled against, and the script that resolves and
  launches it — hashed on disk rather than read out of git, since the files that
  are there are the files that run. The baseline is taken once at startup, and
  the check happens after the reset, which is the only moment new source can
  have arrived, and before [the branch step](#the-issues-branch), so the tree
  is on the trunk when it is read: an issue's branch that edits the loop's own
  source does not make the loop restart the moment it is checked out. The
  changed paths are named on the way out.
- **Age**, `--restart-after MINUTES`, thirty by default and `0` to turn it off.
  It is the backstop for the loop this would otherwise miss entirely: an idle
  loop never resets — a fetch every interval all night against a remote with
  nothing to say is a fetch for nothing — so it never sees a change, and would
  sit there on the old code until morning. Read where no claim is held, so a
  restart is never something a ticket is waiting behind — and with every
  checkout [back on the trunk](#leaving-the-tree) after each increment, the build
  it restarts as is the trunk's, and not whichever branch the last session left.

What survives the restart is what was typed and what has been spent.
`--max-runs`, `--max-spend`, `--until`, `--under`, `--interval`, `--quiet` and
`--stop-file` are all still in argv, which the supervisor re-runs verbatim; the
increments, the spend, the failure streak, the night's start and the two lists
the tally prints travel in a state file the supervisor names once per night. So
a restart cannot outspend `--max-spend` or outrun `--max-runs` by starting over,
and the tally at the end covers the whole night and says how many times the loop
came back. `--until` is carried as the instant it resolved to rather than
re-read, which is the one bound that would otherwise be wrong: `--until 23:59`
typed at 23:58 and re-read at 00:01 means tomorrow, and adds a day to the night.

Three things it will not do. It will not restart holding a claim — the ticket
the deciding pass claimed goes back before the process exits, and nothing is
spawned on that pass. It will not restart-loop on a build that failed: the
supervisor says so and runs the version that is there, and that incarnation took
its baseline from the source already on disk, so it does not ask again for the
same change. And it will not restart at all under `--once`, under
`--no-restart`, or when started by hand rather than through `hatch.sh` or `hatch.ps1` — the
state path is what tells the runner somebody is standing over it, and a runner
with nobody to rebuild it is the loop it started as.

Three things that look like reasons to stop are not. **A lost lease** is the
loop working correctly on a busy board — the ticket went to a runner already
further into it — and does not count toward the three. **A board that did not
answer** rides the gap out on its own: every call retries, backing off, for up
to `HATCH_RETRY_SECONDS` (90s by default, `0` for exactly one attempt) before
it is even read as a failure — so a deploy with no health check costs the loop
nothing it would not have spent anyway. The one exception is a claim's own
heartbeat: one that keeps failing for longer than `StallLapseSeconds`
altogether — whether every beat in that stretch failed outright, or each spent
its own retry budget and still failed — gives up on its own, sets `Lost` and
stops the session exactly as a `409` does, on the understanding that a lease
gone quiet that long is one the board's own rule may already have let
somebody else take. And **a busy board** is a wait, not an ending: every
candidate being worked elsewhere is a report, and it is deliberately not the
sentence an empty board gets.

`--under AER-1` points a night at one project: the same rule, asked of one
epic's subtree. It and a bare key cannot be given together, and a bare key is
refused outright — `go-to-work` asks "what is next" over and over, and one
ticket cannot be the answer to that twice. One increment on one ticket is what
`work AER-12` is for.

The tally is printed from the exit path and nowhere else, because the ways a
loop ends include the ones nobody wrote code for — an interrupt, a terminal
closing — and those are the runs whose tally is most worth having. It counts the
increments, the elapsed time and the spend, and then lists the tickets in two
groups: the ones that moved, which is what the night got done, and the ones that
stalled, which is what is waiting on somebody.

### Runners on the board

A loop is a process on somebody's machine, and until it says so nothing on the
board knows it exists. So it says so: one call at the top of every pass, naming
itself, carrying the last thing it printed — and reading back, in the same round
trip, what the board would like it to do next. The **Runners** page is the other
end of that, and it is how a night is paused, bounded or ended from a browser.

**Nothing on the server starts or stops a process.** Every one of these is
picked up by the loop itself, between increments, which is what makes it work
for a runner in a container and for one on a laptop behind a router nothing can
reach. The cost is honest and is stated on the page: a press takes effect at the
top of the next pass, *after* whatever increment is in flight has finished.

**The runner is named as its claim names it** — a character off the cast list
in `src/Hatch.Cli/runner-names.txt` (main and recurring characters from three
TV shows, plus the colorful one-offs), or `HATCH_RUNNER`'s override, the same
string every [claim](#claim) already carries. A runner has one identity and
this is it; the table is keyed on it. The choice is made once per checkout —
a SHA-256 over the host and the canonical checkout path picks a starting slot
in the list, and the walk forward from there skips any name another checkout
on this machine already recorded, or a live runner elsewhere on the board
already holds — and then recorded in a per-user `runners` file beside
`config`, so every later run repeats it rather than choosing again. Editing
the list renames nobody who already has a name. `hatch config` offers the
chosen or recorded name as its default and accepts another with the same four
checks; `hatch config --runner <name>` sets it without asking.

Every heartbeat also carries `where` — the machine and checkout the process is
actually running from — drawn on the Runners page under the name. A heartbeat
whose name is already the *live* runner at a different `where` is refused with
`409`: two live runners never share one row, and a gone row can still be taken
over, which is how a checkout that moved keeps its name. That refusal is the
backstop; the first choice already tries to dodge it by reading the board.
`Agent` works the same way - the id of the agent that spawned the process
(`claude` today, per `Settings.KnownAgentIds`), absent from a runner that has
not reported one - but carries no uniqueness check, since two runners sharing
an agent id is not a conflict.

**It also says which repositories it serves.** Every heartbeat from
`go-to-work` carries the `origin` of each checkout the runner holds,
canonicalised the same way a project's own bindings are (`RemoteIdentity`) —
and the row is overwritten with whatever the latest beat sent, on every
heartbeat rather than seeded once, because these are facts about the running
process and not bounds an operator has set. The Runners page draws them beside
`Under`, naming "clones what it lacks" where the runner makes clones for
itself, so a person can tell which box a project's tickets will actually be
built on before any of them have moved. `hatch work`'s own one-off heartbeat
carries none of this — it does not know the answer any better than the row
already does — so a `hatch work` run, or an older CLI build, leaves whatever a
loop already reported in place rather than blanking it. `go-to-work --once`
shares the loop's own heartbeat and reports the same way, once.

**What it is working is not stored.** A row's ticket and the line beside it are
read off whichever issue carries that runner's live claim at the moment of the
request, the way an open question is computed rather than kept. A second copy
would drift the moment an operator cleared a claim out from under the runner
holding it. The row's own line is therefore only ever what a runner said
*between* tickets — "nothing on the board is an agent's to move", "AER-12 moved"
— because an increment's chatter already rides its lease.

**A claim's heartbeat counts as the runner being heard from, too.** A loop
beats its own row only between tickets, but a session well into a long
increment can beat its *claim* every minute the whole time — see
[Claim](#claim). `RunnerDto.LastSeenAt` is the later of the two, so a runner
reads *Working AER-12* for as long as either is fresh, and does not read
*Gone* out from under a session that is still there and still renewing its
lease, only quiet on its own row.

**Two horizons, and only one of them is a setting.** A row is *idle* while it is
being heard from, *gone* once its last heartbeat is older than
`Hatch:RunnerGoneAfterSeconds` (90 by default), and dropped from the read
entirely at ten times that — a quarter of an hour, which is not itself
configurable. The two are one judgement seen twice, "not answering" and "not
coming back", and an installation able to set them apart could set the second
shorter than the first and have rows vanish before they were ever drawn as gone.
Nothing sweeps: all three are arithmetic against the last heartbeat at the
moment somebody asks, the same lazy expiry the claim uses, so there is no
timeout to tune and no background job to notice has stopped. The row itself is
never deleted — a checkout that runs again next week is the same row, with
whatever bounds are on it.

**Three things a person can say**, and the loop obeys each at its next
heartbeat:

- **`stopping`** — finish the increment in flight and exit without picking
  another, saying on the terminal that the board asked it to.
- **`paused`** — take no ticket, and *keep heartbeating*, so a loop deliberately
  doing nothing does not drift from idle to gone while it does it.
- **`running`** — carry on, which is what every row starts as.

**And the four bounds** `go-to-work` takes on the command line — `--under`,
`--max-runs`, `--max-spend` and `--until` — are on the row too, folded into the
loop before the stop conditions are asked, so a cap lowered from a page stops
that loop at its very next pass. `until` on a row is always an instant, never a
bare date: "stop by the 12th" is a midnight in a timezone nobody named.

**The flags seed the row once.** The first heartbeat a name is ever seen under
writes the four bounds from what the process actually started with, which is
what makes them show on the page immediately; every heartbeat after that leaves
them alone. That rule is what stops the loop's own half-hourly
[restart](#what-it-stops-for) — which sends the same argv again — from quietly
undoing an edit somebody made at midnight. The consequence is worth knowing:
once a runner has a row, **the row is where its bounds live**, and a flag typed
at a checkout that already has one seeds nothing.

**The Max spend field counts only priced increments** — its hint on the page
says so once ("it counts only sessions whose agent reports a price, so it
cannot bind one that does not"), rather than repeating it on every row. The
loop's own closing tally says the same thing once a night, naming how many
increments reported no price and were left out of the dollars it printed. This
is unrelated to the hard limit and the clamp a runner carries per session,
which are counted in tokens, not dollars, and bind exactly as before.

**`hatch work` and `go-to-work --once` send exactly one heartbeat** and read
nothing back. There is no second pass in either to apply an instruction to, so
the row says `once` and the page draws no controls on it — a Pause nothing will
ever look at is worse than no Pause. It ages out on its own when the process
ends; nothing deregisters.

**A heartbeat that does not answer is weather**, exactly like a claim's. A Hatch
that is down, or too old to have the route at all, leaves the loop running on
the flags it started with — which is what it did before any of this existed.

The read and the heartbeat take a key, like the claim: a dispatcher that could
not say it was alive would leave a page that could only ever be empty. The write
does not, for the reason the [playbooks](#playbooks) are closed to one — an
agent that could raise its own `--max-spend` could raise its own budget, and a
loop with no end is exactly what the bounds exist to prevent.

**`exhaustedUntil` is a fact a runner reports about its own Claude account,
never a person's to set.** There is no control for it on the page and no field
on `PATCH` — only a heartbeat writes it, as a tri-state carried beside the
instant itself: absent leaves the row's own record alone (an older CLI, or
`hatch work`'s one-off beat, neither of which knows anything about the account
it ran under), a heartbeat that says *out* sets it, and a loop heartbeat that
says it is *not* clears it at once. `go-to-work` sends the tri-state
explicitly on every beat, so its own restart carries a wait across (below) and
a person overriding the wait by starting a fresh loop by hand clears the board's
record on that loop's first heartbeat, the same way a fresh night carries none
of the other bounds forward either. The Runners page draws such a row as *Out
of usage until 7:40pm*, in the viewer's own time zone — unless the row has
already gone quiet, which still reads *Gone* regardless of what the last
heartbeat said, the same rule the rest of this table already follows: silence
beats every other fact about a row. Nothing sweeps this either — a reset that
has passed is arithmetic against `ExhaustedUntil` at the moment somebody asks,
the same lazy expiry the horizons above use.

See [Runners out of Claude usage](#runners-out-of-claude-usage) for the read
that feeds the attention panel's dot, and
[when an increment does nothing](#when-an-increment-does-nothing) for what
sets this in the first place.

### The console

`go-to-work`, `do-my-work` and `hatch work` wrap every increment in a banner and,
at an interactive terminal, pin a readout to the bottom of it — see HA-120 for
the operator's brief and the reasoning behind each choice below.

**The banners are in every log, terminal or not.** Before a session is spawned,
`🥚🥚🥚🥚🥚 STARTING WORK ON <KEY>` opens it, naming the issue's title and type,
what the increment is for (a move, a conflict, or a failing build), the model
and effort — with the line saying the issue chose them, where it did — and the
runner's name with which increment of the night this is. When the increment is
over, a glyph row closes it: `🐣` where the ticket moved, a conflict was
resolved, or a fix was pushed; `🥚` where it did not move; `🍳` where the
session exited badly, was interrupted, or lost its lease before the end — that
glyph outranks the other two, however the board reads afterward. Under it, the
outcome in the tally's own words, a `Took` line with wall-clock time, tokens,
cost and turns, and the account's usage where there is one. A figure that never
arrived reads `not reported`, never `0` — a session interrupted deep enough to
lose its own read-back (`Increment.RunAsync`'s own `catch
(OperationCanceledException)`) still returns a report so its banner can close.
Nothing is printed for a pass that spawns no session — an express hop, a
conflict or a failing build that had already cleared, a ticket waiting to be
told which branch. Plain text, no colour, no cursor movement: a redirected log,
including the container runner's `docker logs`, reads exactly like a terminal's
scrollback.

**The readout is pinned to the bottom, not the top**, and only where output is
an interactive terminal — never redirected, never `TERM=dumb`, and never
`hatch work -i`, whose terminal belongs to the session a person is sitting in.
Bottom rather than top because that needs no terminal mode at all: a scroll
region left set by a runner killed outright (a second Ctrl-C, `kill -9`) would
leave a terminal that scrolls wrongly until somebody types `reset`, where a
footer erased and redrawn around every line — the way a build tool draws a
progress bar — leaves at most one stale copy in the scrollback and nothing to
repair. `Terminal` draws it (`LiveTerminal` in `src/Hatch.Cli/Terminal.cs`),
under the same lock every streamed and printed line already goes through, so a
clock tick can never land mid-line; what it draws is a pure function of a
snapshot and the clock (`Readout.Draw`), fed by `ReadoutState` — the one object
a running increment and the idle loop between them both write to.

While an increment runs, its first row names the issue — key, title, the move
or conflict or build it is for, and its address — and the second says whether
the agent is alive: elapsed time, tokens spent so far, and how long it has been
quiet with what it was last inside of (`quiet 2m14s — Bash  make test-api`),
turning the warn colour past two minutes and the danger one past ten. One row
follows per usage window the session's own stream reports — see below — each a
20-cell bar, a percentage and a reset time. Last is the runner's own row: its
character name, the person it works for (from the heartbeat's `for`, HA-122),
how many increments this loop has run and for how long, counted across the
loop's own restarts the way the closing tally already counts them, what the
night has spent, and any bound it will stop at. Between increments the runner's
row stays and one more appears, saying why nothing is being worked and when the
loop looks again. Colour drops under `NO_COLOR`; every row is clipped to the
terminal's own width, read fresh on each draw, so a resize clips rather than
scrambles it. On exit for any reason the runner can catch — the night ending,
Ctrl-C, a restart onto a newer build — the footer is erased and the closing
tally prints below the last banner, as it always has.

**The usage bars read the account, not Hatch's battery** — asked and answered
on HA-120: every session's `stream-json` carries a `rate_limit_event` naming
`unifiedWindows.five_hour` (labelled `Session`) and `seven_day` (`Weekly`),
plus a per-model weekly window for accounts that have one, labelled `Weekly
(model)` since it arrives with no name of its own. That is the account this
runner's sessions actually spend — right for a friend's `do-my-work` on their
own subscription, and it needs no token pasted into this Hatch's Settings
page. The reading updates while a session streams; a quiet run, or one that
ends before the first such event, has none.

Between increments the rows are no longer just whatever a session last said:
HA-173 has the loop ask its own CLI's `/usage` at the top of every idle pass
(`UsageReport`, `IUsageProbe` on `ClaudeSessionRunner`), which answers with the
same two windows plus a named scoped one and a fourth the session stream never
carries, `extra`. A probe that answers replaces the panel's whole reading, the
same rule a session's own stream already follows; a probe that answers nothing
— an account this CLI cannot read, or one already known to cost a turn for the
life of this process — leaves the panel exactly as it was. Only where neither
source has ever answered does the readout and the closing banner alike simply
draw no usage line rather than complain.

**The bottom row, when it is drawn, is a legend of the keys that do
something** — see HA-129 for the operator's brief. `n` ends the idle nap between
passes early and asks the board again at once, rather than waiting out the rest
of the interval; it does nothing during an increment, a pass, a pause or an
out-of-usage wait, is never queued for later, and writes one line to the log
when it acts. `s` stops the runner after
the increment in flight finishes, pushes and is written up — press again to
undo it, which the legend says while it is armed; `c` asks for confirmation
(`y` to confirm, any other key to take it back) and then ends the run exactly
as Ctrl-C does, process tree and all. `p` pauses the runner — still
heartbeating, so its row never drifts from idle to gone, but nothing further
is picked up — and press again to resume, which (HA-133) happens straight
away rather than waiting out the rest of the interval; a board that has this
runner paused outranks a local resume, so a `p` pressed here cannot read as
having un-paused a runner the board itself is holding still. `k` asks for
confirmation the same way `c` does (`y` to confirm, any other key to take it
back) and then ends the increment in flight without touching the ticket
further — the loop moves straight on to the next one, the same as any other
increment that spawned nothing new to wait on. `?` swaps the one-line legend
for a longer one naming every key in full, and back again on a second
press — the one key here that changes what is drawn and not what the runner
will do, so it writes nothing to the log. Keys are read exactly where the
readout draws, and only when this process's own standard input is itself a
terminal — a pipe, a service, or a container with no TTY reads no keys and
draws no legend, and `go-to-work`, `do-my-work` and `hatch work` all say so
once at startup, naming Ctrl-C and `--stop-file` as the ways to stop instead.
`--no-keys` turns the keyboard off outright on all three commands, and draws
the readout exactly as before minus the legend row and with no startup line —
nobody needs telling about a control they switched off. A key that means
nothing does nothing and says nothing; every key that changes what the runner
will do writes one line to the log — `?` is the one exception, since it
changes the display and not the runner — so a redirected run read the next
morning still says what was asked, and when. Ctrl-C is unchanged, and works
at any moment whether keys are on or off, including while a confirmation is
outstanding — a second way to reach the same cancellation, never a
replacement for it.

## The level above the board

The board shows every card, which is the one thing it cannot do: say which of
the running projects is nearest the line. `GET /api/hatch/plan` and
`/api/hatch/plan/{key}` answer that, and the **Plan** page draws it.

**The unit is the leaf, and every leaf weighs the same.** A subtree's total is
the histogram of its leaf descendants by status; an issue with no children
counts as itself, one leaf in its own column. The alternative on the table was
child-weighted — each story 1/n of its epic whatever its size — which makes
nested meters agree by construction but says a twenty-task story and a two-task
one are the same size. They are not, and the bar is there to say how much work
is left. The agreement comes for free anyway: a parent's total is exactly the
sum of its children's, and a test pins it.

**A parent's own column never lands in its own total.** A story sitting in In
Review whose tasks are all in To Do reads as To Do, because the tasks are the
work. Ready dates are not consulted either — a card folded off the board is
still work, and a total that shrank and grew as dates arrived would not be a
total.

**The bar shows the distribution across the columns, not a filled fraction.**
One stacked segment per status, in the colour the operator painted that column,
so where the bulk sits reads at a glance: an epic whose every story is in In
Review looks nothing like one whose every story is in Draft, and a single fill
would have drawn both as 0%. It also disposes of the partial-credit question
underneath it — nothing has to invent a score per column when every column is
drawn. A subtree with nothing filed under it draws no bar rather than an empty
trough, because an epic with no stories is at the start of its life, not stalled
at the bottom of one.

Every total is the same shape: `leaves`, `done` (the leaves in a terminal
column), `waiting` (open questions on the issue and everything below it), and
`slices`, one `{ statusId, count }` per column in board order with the empty
ones left out.

A leaf in a [deferred](#status) column is in none of them — not `leaves`, not
`done`, and not a slice. It is out of the denominator rather than counted either
way, because calling it done would claim something shipped that never did and
calling it outstanding would leave an epic that is finished except for three
parked tasks stuck at 85% forever, which is how a progress bar stops being read.
`waiting` is the exception, and now only on one side of it. A question on a
[deferred](#status) leaf still counts: deferred is parked, not finished,
and a person who un-parks it needs to see what is still unanswered, so a
count that quietly dropped it would be a question nobody looking at the
epic would know to go back for. A question on a leaf that has reached a
**terminal** column does not: the ticket shipped with it open, nobody is
coming back to move it, and keeping it in `waiting` forever would be the
meter holding open a decision nobody is being asked to make.
`Questions.Waiting` is where that line is drawn, and it is the same line
the attention panel and the board's badge draw, so a subtree cannot read
"waiting" on one screen and quiet on another.

[`Rollup.cs`](../src/Hatch.Api/Modules/Hatch/Rollup.cs) loads the tracker once
and folds post-order — O(n) for the tree rather than a walk per node — because
the Plan page asks about every epic in the house at once. It is server-side for
the reason the rank and the dispatcher's pick are: a browser that re-derived a
total would disagree with the CLI the first time a column was added.

## The importer

A read-only upload that turns a markdown document into a tree: the doc becomes
an **epic**, each `## Phase` heading a **story**, and each checkbox a **task**,
with a checked box landing in a terminal column. `preview` takes files,
`preview-text` takes a pasted `{ title, body }` through the same parser — the
title plays the filename's part, which is the provenance every issue carries and
the epic's title when the body has no `#` heading. Neither preview touches
anything; `POST /import` is what writes.

It exists because Hatch inherited a folder of plan files, and it stays because a
plan drafted in an editor is still the fastest way to think one through.

Nothing about the import is destructive at the source. **Retiring a source
document is a deliberate manual act** — verify the epic against the file it came
from, then `git rm` it — because an importer that deleted its input would be one
bad parse away from losing the only copy of a plan.

## The operator and Claude contract

This is the part [`CLAUDE.md`](../CLAUDE.md) mirrors, because that is where an
agent reads it. The rules are here so that they have somewhere to be argued
from.

That mirror is in two halves, because it is loaded on every increment and most
increments only write code. `CLAUDE.md` carries what every session needs;
[`hatch-planning.md`](hatch-planning.md) carries the shapes only a session that
files or reshapes work does — `parentKey`, the two dates, the bulk endpoint and
the plan reads — and is read on demand.

### Reaching Hatch

`hatch` is the calls a working session actually makes — `board`, `next`,
`queue`, `show`, `start`, `move`, `comment`, `pr`, `landed`, `depends`,
`one-pr`, `ask`, `questions`, `answer`, `config`, `work`, `go-to-work`,
`do-my-work`, and `api` for everything else. It finds a column by name rather than by id — on the
letters and digits alone, so `todo` at a terminal reaches the column the board
calls `To Do` — and folds off cards whose ready date has not arrived, exactly
as the board does. `hatch --help` lists the surface and every subcommand takes
`-h` for its own.

Only `work`, `go-to-work` and `do-my-work` need to be run inside a git
checkout, because only those three are about a codebase. The other fifteen
are one request and a sentence about the answer, and `hatch board` from a
directory that has never been a repository is the ordinary case.

`--mine` on `work`, `go-to-work` and `queue` narrows to the caller's own
tickets — assigned to the person the calling key belongs to, or to the key
itself. `hatch do-my-work` is exactly `go-to-work --mine`; plain `go-to-work`
still skips every person's own tickets, including the caller's — see
["Mine held"](#one-more-on-next-alone).

Its settings are read in three layers, highest first: an exported `HATCH_BASE`
or `HATCH_KEY`, then `scripts/.env` in the checkout you happen to be
standing in, then the per-user file `hatch config` writes — mode 600, under the
platform's application-data directory, outside every repository. An operator
configures once and every checkout on that machine is reached; a repository that
wants to pin its own origin still can. **The key lives outside the artifact** —
never a tracked file, never a value in a commit, never pasted into an issue.
That is not ordinary secret hygiene: Hatch ships to other operators, and a
credential in the artifact is one operator's credential inherited by everyone
who clones it ([`ethos.md`](ethos.md)).

The key is optional. Against a Hatch started with its wall off
("Local mode") there is no
credential to present, and every call names itself with an `X-Hatch-Runner`
header instead — which is a name and not a proof, and only that Hatch reads one.
So a `401` is two different sentences, and says which happened: a key that was
sent and refused is a key to go and look at, and no key at all is a Hatch with
its wall on and nothing to look at yet.

In this repository, [`scripts/hatch.sh`](../scripts/hatch.sh) still answers to
every one of these commands and hands them to the program, so nothing anybody
has typed here stops working. In PowerShell it is
[`scripts\hatch.ps1`](../scripts/hatch.ps1), and where the execution policy
refuses scripts, `powershell -NoProfile -ExecutionPolicy Bypass -File
.\scripts\hatch.ps1 <command>` runs it anyway; `Set-ExecutionPolicy -Scope
CurrentUser RemoteSigned` allows it for the account once. Group Policy outranks
both, and if it is what refuses, it is the thing to change.

`hatch work` reads `work/next`, then spawns a headless session with the
playbook's prompt, model and effort. It prints the session id first and last
with the `claude --resume` command beside it, and streams what the run is doing
as it happens — every tool call, a thinking-token pulse, and a heartbeat naming
what it is still waiting on. That last part is not a nicety: the CLI's default
output prints nothing until the run ends, so a four-minute increment was four
minutes of blank terminal indistinguishable from a hang, and the fix for "is it
working" is showing the work, not a spinner.

`hatch go-to-work` is `work` in a circle, and is
[its own section](#the-unattended-loop) — what it may pick up, what it does
about a ticket that did not move, and what it stops for.

`hatch queue` reads the scan and prints it, one issue a line — key, type,
column, and either the reason the pass would fold past it or the transition it
is clear for, in the dispatcher's order: every [emergency](#expedite) row
first whatever column it sits in, then every [expedited](#expedite) row, then
the rest, and inside each third the rightmost column first and the order the
board itself draws that column in. An emergency row is marked `!!` and an
expedited row `! ` — both two characters, so the columns after it still line
up — so a queue reordered by one says why. A clear row that is a
[hop](#the-hop) reads `-> <column>  (express, no session)`,
`(parent pulled, no session)`, `(epic, no session)`, `(under <epic>, no
session)`, `(sent back, no session)` or `(pre-approved, no session)` —
the last shared by the approve and close kinds — in place of the bare
arrow, naming which of the seven carried it, so
it reads differently from a row `go-to-work` would spawn a session for even
though both print no reason to fold past. A row that is clear only
because a stall mark's resume window has passed reads `clear for <column> ->
<column> - it stalled 18 minutes ago and resumes itself after 15 minutes`
instead of the bare arrow, for the same reason: a row clear for the ordinary
reason and one clear because its mark's window passed are both "clear", and
only one of them is worth a second look. `hatch queue AER-1`
scopes it to one epic's subtree. It spawns nothing and writes nothing, and an
empty board prints a sentence saying so rather than a blank line: "there is
nothing" and "something went wrong and printed nothing" look identical
otherwise, which is the one thing a run nobody watched cannot afford to be
unsure about.

This is the long form, and it is no longer the only way to see the folds.
`work` and `go-to-work` group the same sentences and print them with a count
each — worst first, one line per distinct reason, so a column of two hundred
cards folded for four reasons is four lines — whenever a pass has an increment
to run or finds nothing at all. A board with nothing on the dispatcher's path
says *that* instead, which is the difference between a finished board and a
jammed one, said without anybody having to run a second command. The idle loop
reprints the digest when it changes and otherwise says it is still alive every
ten minutes; `queue` is where the counts turn back into tickets.

### What an agent does with a ticket

- **Read it.** The description is the brief; the comments, the answered
  questions, and the event trail are the context.
- **Move it to *in progress* before starting.** The board saying what is being
  worked on right now is the board's whole job.
- **Comment the commit sha and the branch, and record the pull request.** The
  ticket is where somebody looks in six months, and a comment naming a commit is
  what makes that search short. The pull request is a field rather than a
  sentence — `hatch pr AER-12 <url>` puts it there, and the issue page draws
  it as something to click. In a project that [lands on the
  trunk](#landing-on-the-trunk) there is no pull request to record:
  `hatch landed AER-12 [<sha>]` records the sha the work reached the trunk at,
  per repository — the checkout's origin remote unless `--repo <path>` names
  another.
- **Name the ticket in the pull request, in two places.** The title is the key,
  one space, then the subject in house style — `AER-12 Auth: the first Admin`,
  with no brackets and no second colon. The first line of the description is
  `[AER-12](<origin>/apps/hatch/issues/AER-12)` and nothing else, then a blank
  line, then the summary. `<origin>` is the address Hatch was reached at
  (`HATCH_BASE`), or the install's public address where it sets one; `hatch work
  AER-12 --dry-run` prints the exact line. Never write a relative link.
- **Plan on the ticket, not in a chat log.** A planning session `PATCH`es
  acceptance criteria into the description and `POST`s the stories or tasks the
  work breaks into. An epic takes stories; a story takes tasks.
- **File the wait, don't write it down.** Something that cannot start until a
  soak test finishes or a renewal window opens gets a `readyAt`, not a sentence
  in a description saying "not until March".

Every one of those calls writes an event carrying the key's name as the actor,
so the trail says who did what without anybody being asked to record it.

### What only the operator does

- **Move work into a terminal column.** Implementation ends in *review*, with a
  comment saying what landed and what did not. Only the operator decides that
  something shipped, and the board enforces it: the dispatcher refuses a
  transition into a terminal column outright, or a pre-approval already
  given carries it through instead — the close hop, the one case a pass
  crosses this fold on a person's standing decision rather than on a
  person's own press.
- **Move work into a protected column.** Only an owner or approver can, the
  same gate [the one permission](#one-permission-beyond-the-role) describes —
  or, the same way, a pre-approval already given carries it through with no
  session: the approve hop, since a pre-approval is that owner's or
  approver's own permission, given in advance of the move rather than at
  it. Once an issue is there with `MergesPullRequest` ticked, merging it is
  [the merge train](#the-merge-train)'s, not a click the operator makes on
  the forge. The column itself is not terminal, so nothing above is broken:
  the move in is still a person's — made at the move, or standing from
  before it — only the merge out is not — and the train still makes that
  move while this runner is paused or out of usage, since it spends
  nothing a session would (`GoToWork.cs:947-950, 995-997`, HA-463).
- **Shelve work in a [deferred](#status) column.** Whether something is worth
  doing at all is the same kind of call, and it is refused the same way: `hatch
  move` will not take a ticket there, and no pass dispatches one out of there.
  If work should be parked, say so on the ticket.
- **Edit a playbook.** The API refuses it, and the refusal is deliberate; if a
  playbook is wrong, say so on the ticket and stop.
- **Mint and revoke keys**, and everything else behind a plain `[RequireAdmin]`.
- **Answer a question**, which is the next section.

### When a decision is not the implementer's

Some things a ticket needs are not an implementer's to choose: a product call, a
name that will be lived with for years, a tradeoff with no technically correct
side. The rule is: do not guess, and do not quietly take whichever branch is
cheapest to build. Ask on the ticket, **name the choices**, and stop.

```
hatch ask AER-12 "How should drain retries be scoped?" \
    --recommend "Per-node: one budget each, so a slow node cannot starve the rest" \
    --option   "Global: one budget for the drain, simpler to reason about"
```

The body is the question alone, in a sentence; the tradeoffs go inside the
options they belong to; and `--recommend` marks the one the asker would take, of
which there may be one. A label is short enough to press and reads as a decision
on its own — `per-node`, not `we should scope them per node` — because the label
becomes the answer's own text. One call per question, so each can be answered on
its own. Prose (`ask` with no options) is for the answers that are genuinely
open-ended: a name, a description, a direction.

Then **stop**. An open question blocks the ticket from being dispatched at all,
so nothing further is spawned at it until somebody answers, and anything built
past the question is built on a guess.

The operator answers at a terminal (`hatch answer` walks the open ones one at
a time, serially — a list of six printed at once gets answered in aggregate,
which is how a wrong assumption gets in) or on the issue page. The answer is a
comment bound to its question, so `work` carries the decisions already made into
the next session's prompt under **Decisions already made**. Those are settled;
build on them, and do not reopen them.

What the repository can answer, answer by reading the repository. A question the
code already settles is a round trip through a person for nothing.

## Deferred on purpose

- **The loop lifted onto the Hatch platform, headless.** Two checkouts on one
  box and several boxes in the house are what the [claim](#claim) makes
  possible; hatch-owned clones — a loop given a directory of its own that
  clones whatever a board binds it has no checkout of, `--workspace` /
  `HATCH_WORKSPACE` — are what let a headless box or a fresh container serve a
  whole board with nothing mounted by hand, and that is built too. What is
  still deferred is narrower: the loop as a *service* rather than a process
  somebody starts — a scheduler, a place the output goes, credentials it did
  not bring with it itself. Not the mutex, and not the clone.
- **A second scope for agents**, which would stop a key answering its own
  question. Worth a column when somebody wants it; see
  [the one edge](#the-one-edge-that-is-deliberately-cut).
- **Reports beyond the Timeline.** The [Timeline](#the-timeline) reads the
  `status_changed` events; what remains deferred is a report over the trail's
  other kinds, a menu under the Reports nav link (it waits for a second report
  to put in it), and a CLI subcommand for a report (`hatch api GET` reaches the
  Timeline already). Each is cheap to add later. The Plan view answers the
  question that was actually being asked.
- **GitHub integration, beyond one read and two writes.** A branch and a PR
  are named in a comment by whoever made them. The one thing read from a forge
  is the build on an in-review branch's tip, with the pull request's merge
  state and review decision beside it, stored on the issue and drawn; the two
  writes are the merge of a pull request the board has already gated behind a
  protected column, and the branch update that keeps it current with the trunk
  while it waits — all three through the runner's own `gh`
  ([checking the branches in review](#checking-the-branches-in-review)).
  A forge-side approval as a second gate was weighed and declined (HA-558,
  *hatch-still-approves*): Hatch's own approval is the gate in every shape that
  opens pull requests, so the review decision stays drawn and unread by the
  server (the merge state is read by [the merge column's judge
  list](#the-approval-column)). Webhooks and anything else the server would ask
  a forge itself are deferred: each is a credential on the server, which is
  what the runner asking `gh` as whoever it is signed in as avoids.
- **A deleted issue takes its events with it.** Hard delete, confirmed in the
  UI, and an accepted gap.
- **Pushed updates.** The board polls (see above); a server-sent signal would
  only make the same refresh arrive sooner.

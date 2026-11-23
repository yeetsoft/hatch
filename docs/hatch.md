# Hatch

## Section index

This file is larger than a context window (about 500 KB, 19 top-level
sections), so do not read it whole. Find the section below, or list the
headings with `grep -n '^## ' docs/hatch.md`, then read from that heading to
the next heading of the same level and stop.

- [Summary](#summary) - What is Hatch, and which four properties carry the
  design?
- [Goals](#goals) - What is Hatch for, in four things it must do?
- [Non-goals](#non-goals) - What is absent on purpose, and what does each
  absence cost to add later?
- [Where it lives](#where-it-lives) - Which module, app and project hold the
  backend, the frontend and the wire contract?
- [Domain model](#domain-model) - Which entities exist, and what are the
  rules on each?
  - [Migration order](#migration-order) - Which migration history does
    `HatchContext` own, and when is it applied?
  - [Project](#project) - What does a project row carry, and how does it
    land its work?
  - [Project Member](#project-member) - Who owns or approves a project, and
    why is the person a bare Guid?
  - [Person scope](#person-scope) - Which projects may a person see, and how
    does the server say so for a caller?
  - [How scope is enforced](#how-scope-is-enforced) - What stops a row of a
    project the caller cannot see leaving the data layer, and what is let
    through on purpose?
  - [Logo](#logo) - How is a project's logo stored and its type decided?
  - [Repository](#repository) - How does a project bind its git remotes?
  - [Status](#status) - What is a board column, and which flags make it
    behave?
  - [Issue](#issue) - What does an issue row hold, and how is its key made?
  - [Contribution](#contribution) - What does the record that an issue came
    from outside hold, and what is the envelope a Hatch-made pull request
    carries?
  - [Dependency](#dependency) - What serialises work, and how is an edge
    stored?
  - [One pull request](#one-pull-request) - Do a story's tasks share one
    branch and pull request?
  - [Finished with no code](#finished-with-no-code) - How does a ticket that
    changed nothing say so, and where does it stand?
  - [A pull request waiting to be opened](#a-pull-request-waiting-to-be-opened) -
    What does an issue carry when its pull request is written and not yet open?
  - [WIP](#wip) - How full is the WIP section right now?
  - [Claim](#claim) - What is a lease on an issue, and which columns hold
    it?
  - [Merge check](#merge-check) - What did merging an issue's branch against
    the trunk find?
  - [Landing](#landing) - Which sha has a repository recorded for an
    issue's branch, and is it on the trunk?
  - [Build check](#build-check) - What did the build on an issue's branch tip
    come to?
  - [Trunk build](#trunk-build) - What did the build on a repository's trunk
    tip come to?
  - [Playbook file](#playbook-file) - What does a repository's trunk hold at
    `.hatch/playbooks.yaml`?
  - [Dependency file](#dependency-file) - What does a repository's trunk hold at
    `.hatch/dependencies.json`?
  - [Changelog file](#changelog-file) - What does a repository's trunk hold at
    `.hatch/changelog.yml`?
  - [Comment, question and answer](#comment-question-and-answer) - Which
    comments are more than notes, and how are they bound?
  - [Search](#search) - How are issues, comments and work logs searched?
  - [Dream](#dream) - What is a half-thought, before it becomes an issue?
  - [Capability](#capability) - What does an ambition say has to be true, and
    how would anyone tell?
  - [Issue event](#issue-event) - What has happened to an issue, and what
    kinds of event record it?
  - [Playbook](#playbook) - What is an agent told when an issue moves from
    one column to the next?
  - [Playbook proposal](#playbook-proposal) - What does a session propose
    changing about a playbook, before a person decides it?
  - [Verdict](#verdict) - What did a check session conclude about an issue?
- [The wall, the roles, and API keys](#the-wall-the-roles-and-api-keys) - Who
  may reach Hatch, and how does an API key differ from a person?
- [API surface](#api-surface) - Which routes exist under `/api`, and
  who may call each?
- [The battery](#the-battery) - How much Claude is left, and when does it
  come back?
- [What is waiting on you](#what-is-waiting-on-you) - Has the loop stopped on
  a person, and which links unblock it?
  - [Runner capacity](#runner-capacity) - Which projects' clear steps one live
    runner, or none, can take right now, and why?
- [The leaderboard](#the-leaderboard) - What did the nights cost, and on
  which epics?
- [The goose](#the-goose) - What checks the board every minute with nobody
  connected, and what record does it keep?
  - [Findings](#findings) - What has a check seen stand too long, and what
    does it say about it?
- [Reports](#reports) - When did things happen on the board, which the board
  as it stands cannot say?
  - [The board health read](#the-board-health-read) - How healthy is the board
    right now, in one call?
- [The dispatcher](#the-dispatcher) - What should an agent do next, and how?
  - [Deadlocked](#deadlocked) - When will no pass ever clear a row on its
    own?
  - [The hop](#the-hop) - When does the board move an issue with no session?
  - [Check](#check) - How does a playbook judge instead of edit?
  - [Running an epic](#running-an-epic) - How does a person start a
    top-level epic and walk away?
  - [Running a pass](#running-a-pass) - How does a person start an ambition's
    own pass and walk away?
  - [A night-mode board](#a-night-mode-board) - How is a board laid out so a
    pre-approved epic runs Draft to Done unattended?
  - [Preemption](#preemption) - Who is told by a heartbeat that they have
    been preempted?
  - [Review dispatch](#the-issue-in-review-whose-branch-conflicts-or-whose-build-failed-is-dispatched-to-review) -
    Why does a review dispatch end in the column it started in?
  - [The approval column](#the-approval-column) - Which column merges a
    pull request with no session, and how is it found?
  - [The five paths past the terminal guard](#the-five-paths-past-the-terminal-guard) -
    Which routes cross the terminal and protected guards?
  - [One more, on `next` alone](#one-more-on-next-alone) - Which rules are
    the loop's policy rather than facts about an issue?
  - [What a pass skipped](#what-a-pass-skipped) - What did `work/queue` fold
    past, and why?
  - [Playbooks](#playbooks) - How does a `(from, to, types, shape)` row
    become a prompt, model and effort?
- [The unattended loop](#the-unattended-loop) - How does `go-to-work` run
  increments back to back, and what does it do when nothing is left?
  - [Agents](#agents) - How does the loop reach `claude`, `codex`, `grok`
    or `ollama` through one adapter?
  - [What makes an issue actionable](#what-makes-an-issue-actionable) - Which
    conditions must an issue meet before the loop picks it up?
  - [One loop at a time](#one-loop-at-a-time) - How many loops may serve a
    checkout or a workspace, and which checkouts?
  - [What a runner does, in order](#what-a-runner-does-in-order) - What does
    a runner do in a pass, step by step?
  - [The workspace, between increments](#the-workspace-between-increments) -
    What state is the tree reset to before each increment?
  - [When an increment does nothing](#when-an-increment-does-nothing) - What
    happens when a session ends with the ticket where it found it?
  - [Where the loop's rules live](#where-the-loops-rules-live) - Which side
    decides what is actionable, the server or the runner?
  - [Where the loop lives](#where-the-loop-lives) - Which program is the
    loop, and which commands does it have?
  - [What it stops for](#what-it-stops-for) - What ends a run, and which
    stops are asked for?
  - [Runners on the board](#runners-on-the-board) - How does a runner
    announce itself, get paused or ended from a browser, and what does the
    board keep of what it could not do?
  - [The console](#the-console) - What do the banners and pinned readout
    show during an increment?
- [The level above the board](#the-level-above-the-board) - Which running
  project is nearest the line?
- [The Dream Board](#the-dream-board) - How does the board hold what is not
  work yet, and how is an epic pulled out of it?
- [The importer](#the-importer) - How does a markdown plan become an epic
  with stories and tasks?
- [The operator and agent contract](#the-operator-and-agent-contract) -
  What may an agent do to a ticket, and what only a person?
- [Deferred on purpose](#deferred-on-purpose) - What is deliberately not
  built yet?

## Summary

The house project tracker: a kanban board at `hatch.${DOMAIN}`, issues with
Jira-style keys (`AER-12`), operator-editable columns, comments, and an
append-only audit trail. It replaced a folder of markdown plan files, and it is
the system of record for what Hatch is working on.

The name is the product. You hatch a plan here, and epics hatch into stories
into shipped work.

It is spelled `Hatch` / `hatch` everywhere — C# namespace
`Hatch.Api.Modules.Hatch`, Postgres schema `hatch`, routes under `/api`,
bundle at `/`, and its own host `hatch.${DOMAIN}`.

Four properties carry the design:

- **One board, every project.** A project is a key namespace, not a container.
  Switching boards to find out what is next is the thing the markdown files
  already did badly.
- **Behind the wall, and admin-gated on top of it.** The bundle 404s and the
  API 403s for anyone who is not the operator — the same posture an
  Admin-gated page takes, through the same gate.
- **An agent is a first-class caller.** An `Authorization: Bearer hatch_ak_…` key
  reaches the `/api/*` routes that accept a key, decided by the same gate that
  decides everything else. A ticket link is a complete instruction, and the
  ticket is where the answer goes back.
- **Everything an agent needs to decide is decided on the server.** Which issue
  is next, which column it is headed for, whether it may go there, what it is
  told when it gets there, and what a subtree adds up to. A client that
  re-derived any of those would drift the first time a column was renamed.
- **One adapter seam for every agent.** The loop reaches `claude`, `codex`,
  `grok` or `ollama` through one adapter — see [Agents](#agents).

The [workflow contract](#the-operator-and-agent-contract) — what an agent may
do to a ticket, and what only a person may — is the last section, and is
mirrored in [`CLAUDE.md`](../CLAUDE.md) because that is where an agent reads it.

## Goals

1. **A board the operator lives on** — every issue from every project on one
   kanban board, drag to change status, drag to reorder, always visible.
2. **An agent as a first-class user** — paste an issue link into a session and it
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
  (`Dispatch.cs:1252`) beside the terminal fold — with one hop excepted from
  it by name, the **approve** hop (`hopKind != HopKinds.Approve`), the one
  case a dispatch *does* land an issue in a protected column, because a
  pre-approval is that owner's or approver's permission already given in
  advance rather than a program's own judgement call (see [the hop](#the-hop)
  and [the five paths past the terminal guard](#the-five-paths-past-the-terminal-guard))
  — and refused a third time, client-side, in `hatch move`'s speed bump
  (`IssueCommands.cs:256`). Moving *out* of a protected column, and
  reordering within one, need no membership — the check only fires when the
  target column changes and is itself protected. A program (an API key —
  `ICallerIdentity.IsProgramAsync`) is refused regardless of
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
- **One granular permission, and one scope.** A project's `EfHatchProjectMember`
  role (`Owner` or `Approver`, from HA-290) grants exactly one additional thing
  beyond the global role — moving an issue into a protected column, read
  through `IProjectAccess.CanApproveAsync`/`HasRoleAsync`. Beside that one
  permission, reaching Hatch at all means trusted to do everything else in it.
  A [person's scope](#person-scope) is the second thing beside the role, and it
  grants nothing: it only narrows which projects the person sees. It is set by
  an admin, held per person, and read through `ICallerScope`; it is not
  enforced yet, so today every route still answers as if everyone saw every
  project. Which routes a key may take is still a statement about *which
  surface*, never about which verb.
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
its own migration history, one line in `Program.cs` — the shape
[`Modules/README.md`](../src/Hatch.Api/Modules/README.md) describes. Splitting
it into its own service later is a connection-string change, so "part of the
API for now" costs nothing architecturally.

**Frontend: its own workspace SPA.**
`src/Hatch.Web/apps/hatch`, built into `wwwroot/apps/hatch`, served by the same
pod. It is an operator's tool.

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
serving the board and the sign-in shell from `http://localhost:8080` (or
`HATCH_PORT`). There is no reverse proxy in front of it and nothing to rewrite:
the board is served at the origin's own root, and `/api/*` is how a key
can `curl` the API on the same origin the browser uses. A deep link pasted
into a chat window or a VS Code prompt — `<origin>/issues/AER-12` — needs no
prefix, and the old `/apps/hatch/...` form a bookmark or an open pull request
might still carry answers a permanent redirect to the same path at the root,
query string intact (`Program.cs`).

**The installed app.** `manifest.webmanifest`
(`src/Hatch.Web/apps/hatch/public/manifest.webmanifest`) scopes itself to the
whole origin, `/`, and starts there too, because sign-in lives outside it
(`/auth/`) and because a ticket link copied out of the installed app
carries no prefix at all — a narrower scope would open behind a browser bar
instead of staying in the installed app's own window. There is no service worker: Hatch's deploy story
depends on the browser revalidating `index.html` on every load, and a
service worker's cache is exactly the layer that turns a deploy into a stale
app until somebody clears it by hand. The manifest's own `<link>`, in
`index.html`, carries `crossorigin="use-credentials"` because the wall
answers a cookieless fetch with a 401, and a 401 on the manifest is an app
that silently never offers to install. `GET /.well-known/assetlinks.json`
(`AssetLinksController`, [below](#the-wall-the-roles-and-api-keys)) answers
Android's own link-ownership check with the operator's package name and
certificate fingerprint — supplied as deploy-time config
(`Hatch__AssetLinks__0__PackageName`/`…Sha256CertFingerprints__0`,
`compose.yaml`), never a value this repository could know on an operator's
behalf.

**Read the board's own Docs**, [`src/Hatch.Web/apps/hatch/docs/`](../src/Hatch.Web/apps/hatch/docs/),
served at `/docs/` and imported into the bundle at build time, for
the operator-facing version of the above — the one line, the runner, and the
block to paste into a friend's own `CLAUDE.md`. It travels with the board it
describes rather than living only in this repository.

## Domain model

Seven tables in the `hatch` schema, all carrying the house `Ef` prefix. The
authority is [`Entities.cs`](../src/Hatch.Api/Modules/Hatch/Entities.cs), which
carries the per-field reasoning; this is the shape and the decisions worth
having in one place.

### Migration order

[`HatchContext`](../src/Hatch.Api/Modules/Hatch/HatchContext.cs) owns its own
migration history, applied from its own
[`Modules/Hatch/Migrations/`](../src/Hatch.Api/Modules/Hatch/Migrations/)
folder against its own `__EFMigrationsHistory` rows - distinct from the core
`AppDbContext`'s migrations at [`Migrations/`](../src/Hatch.Api/Migrations/),
which are the `public` schema's: the home-automation tables, and the migration
that dropped the retired Gather, Photos, Quill and Storage schemas and their
two Immich settings (`DropFamilyAppSchemas`). `Program.cs` calls `MigrateAsync`
on `AppDbContext`, then once per registered module context - `HatchContext`
is the only one - in a loop, only in migrate mode, ahead of any serving
replica. The loop stays for when a second module exists; a by-name call would
be a second way to do the same thing.

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
(`Hatch.Api.Tests/Modules/ModelSnapshotTests.cs`), which fails the moment a
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
name and an instant, null until the first write, and `RunnerTags` (a JSON array
of strings, `"[]"` default - which runners may work the project's tickets,
written through its own route, `PUT /projects/{id}/runner-tags`; they are
matched against a runner's *own* tags, which are set from the Runners page -
see [Runners on the board](#runners-on-the-board)),
and `BindsTracker` (`bool`, default `false` - whether this project binds the
tracker's own repository), with `BindsTrackerBy` and `BindsTrackerAt` beside it
- who set it last and when, a name and an instant, null until the first write,
and `CheckApprovesMerge` (`bool`, default `false` - whether a passing check
counts as the approval that lets a pull request merge), with
`CheckApprovesMergeBy` and `CheckApprovesMergeAt` beside it - who changed it
last and when, a name and an instant, null until the first change, and
`NoCodeCloses` (`bool`, default `false` - whether a no-code declaration on an
issue closes it outright), with `NoCodeClosesBy` and `NoCodeClosesAt` beside
it - who changed it last and when, a name and an instant, null until the
first change, and
`RunnersWorkContributions` (`bool`, default `false` - whether a runner works
a contribution's branch when it conflicts or its build fails), with
`RunnersWorkContributionsBy` and `RunnersWorkContributionsAt` beside it - who
changed it last and when, a name and an instant, null until the first change,
and
`OnePullRequestPolicy` (`"optional"` by default, or `"enforced"` - whether a
story or bug may carry a one-pull-request setting of its own, see [One pull
request](#one-pull-request)), with `OnePullRequestPolicyBy` and
`OnePullRequestPolicyAt` beside it - who changed it last and when, a name and
an instant, null until the first change,
and
`Priority` (`"normal"` by default, or `"paused"` or `"expedited"` - how much
the project matters to the loop), with `PriorityBy` and `PriorityAt` beside it -
who changed it last and when, a name and an instant, null until the first
change. Nothing dispatches on it yet, and
`FilesContributions` (`bool`, default `false` - whether the board files its
repositories' outside pull requests as contributions), with `FilesContributionsBy` and
`FilesContributionsAt` beside it - who changed it last and when, a name and an
instant, null until the first change. Nothing reads it yet.
`AllowsUnsignedCommits` (`bool`, default `true` - whether the project allows commits
that are not signed), with `AllowsUnsignedCommitsBy` and `AllowsUnsignedCommitsAt`
beside it - who changed it last and when, a name and an instant, null until the
first change. Every dispatch carries it as `WorkDto.AllowsUnsignedCommits`.
A project may
also carry an uploaded logo, in its own table — see [Logo](#logo).

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

`RunnerTags` is written the same way, through its own route, `PUT
/projects/{id}/runner-tags` (`ProjectsController.PutRunnerTags`), guarded
exactly the way `PutPlaybooksSource` is - `[RequireRole(User)]`, `NotAPerson`
checked again in the action, then an owner check answering `403` naming the
project's key (*"only an owner of `<KEY>` may change which runners may
implement its work"*) to anyone else. The list is replaced whole, and run
through `RunnerTags.Normalise` in `Hatch.Contracts` first: trimmed, folded to
lower case and deduplicated, at most 8 tags, each at most 32 characters with no
space or comma inside. A tag that breaks that rule - a blank one included - or a
list over 8 is refused `400`, naming the tag (the count refusal names the limit
instead). Re-sending the list the project already holds, the same tags in the
same order, writes nothing, and an empty list clears it.
`ProjectPatchRequest` carries no field for it, so this route is the only way in
or out.

`CheckApprovesMerge` is written through its own route too, `PUT
/projects/{id}/check-approves-merge` (`ProjectsController.PutCheckApprovesMerge`),
guarded exactly the way `PutPlaybooksSource` is - `[RequireRole(User)]`,
`NotAPerson` checked again in the action, then an owner check answering `403`
naming the project's key (*"only an owner of `<KEY>` may change whether a
passing check approves the merge"*) to anyone else, and `404` on an unknown
project. It has no repository or playbook precondition: turning it on for a
project with no merge column is accepted. A change stamps `CheckApprovesMergeBy`
and `CheckApprovesMergeAt`; re-sending the value the project already holds
writes nothing, the stamp included. The close hop reads the flag, see [the
hop](#the-hop).

`NoCodeCloses` is written through its own route too, `PUT
/projects/{id}/no-code-closes` (`ProjectsController.PutNoCodeCloses`),
guarded the same way - `[RequireRole(User)]`, `NotAPerson` checked again in
the action, then an owner check answering `403` naming the project's key
(*"only an owner of `<KEY>` may change whether a no-code declaration closes
the ticket"*) to anyone else, and `404` on an unknown project. It has no
repository or playbook precondition. A change stamps `NoCodeClosesBy` and
`NoCodeClosesAt`; re-sending the value the project already holds writes
nothing, the stamp included. The close hop reads the flag, carrying a
declared childless issue through pre-approved or not once it is on, see [the
hop](#the-hop); a declared row it carries this way also stops being one of
[what is waiting on you](#what-is-waiting-on-you)'s `noCodeHeld`.

`RunnersWorkContributions` is written through its own route, `PUT
/projects/{id}/runners-work-contributions`
(`ProjectsController.PutRunnersWorkContributions`), guarded the same way -
`[RequireRole(User)]`, `NotAPerson` checked again in the action, then an owner
check answering `403` naming the project's key (*"only an owner of `<KEY>` may
change whether runners work a contribution's branch"*) to anyone else, and
`404` on an unknown project. A change stamps `RunnersWorkContributionsBy` and
`RunnersWorkContributionsAt`; re-sending the value the project already holds
writes nothing, the stamp included. With it off, a review row whose issue has a
contribution carrying a pull request, and which the review judge would
dispatch as conflict or build work, is folded for a person
(`ReviewWork.HeldContribution`) and listed in
[what is waiting on you](#what-is-waiting-on-you)'s `contributionsHeld`
instead of `conflicts` and `failingBuilds`; a row the judge folds anyway, or a
record with no pull request, is untouched.

`FilesContributions` is written through its own route, `PUT
/projects/{id}/files-contributions` (`ProjectsController.PutFilesContributions`),
guarded the same way - `[RequireRole(User)]`, `NotAPerson` checked again in the
action, then an owner check answering `403` naming the project's key (*"only an
owner of `<KEY>` may change whether this board files outside pull requests"*)
to anyone else, and `404` on an unknown project. A change stamps
`FilesContributionsBy` and `FilesContributionsAt`; re-sending the value the
project already holds writes nothing, the stamp included. Nothing reads the flag
yet.

`AllowsUnsignedCommits` is written through its own route, `PUT
/projects/{id}/allows-unsigned-commits` (`ProjectsController.PutAllowsUnsignedCommits`),
guarded the same way - `[RequireRole(User)]`, `NotAPerson` checked again in the
action, then an owner check answering `403` naming the project's key (*"only an
owner of `<KEY>` may change whether it allows unsigned commits"*) to anyone else,
and `404` on an unknown project. A change stamps `AllowsUnsignedCommitsBy` and
`AllowsUnsignedCommitsAt`; re-sending the value the project already holds writes
nothing, the stamp included. `PATCH` and `POST` do not reach it. Every existing
project allows unsigned commits after the migration. The project dialog shows an
owner a checkbox, *"Allow unsigned commits"*, ticked by default; anyone else reads
a sentence, only where the project allows only signed commits.

`OnePullRequestPolicy` is written through its own route too, `PUT
/projects/{id}/one-pr-policy` (`ProjectsController.PutOnePrPolicy`), guarded
the same way - `[RequireRole(User)]`, `NotAPerson` checked again in the
action, then an owner check answering `403` naming the project's key (*"only
an owner of `<KEY>` may change its one-pull-request policy"*) to anyone else,
and `404` on an unknown project. Anything but `optional` or `enforced` is
`400`, naming the bad value and the two good ones. A change stamps
`OnePullRequestPolicyBy` and `OnePullRequestPolicyAt`; re-sending the value the
project already holds writes nothing, the stamp included. `PATCH` and `POST`
do not reach it. Tightening it reconciles the overrides that already exist, see
[One pull request](#one-pull-request).

`Priority` is written through its own route too, `PUT /projects/{id}/priority`
(`ProjectsController.PutPriority`), guarded the same way - `[RequireRole(User)]`,
`NotAPerson` checked again in the action, then an owner check answering `403`
naming the project's key (*"only an owner of `<KEY>` may set its level"*) to
anyone else, a key included, and `404` on an unknown project. Anything but
`paused`, `normal` or `expedited` is `400`, naming the bad value and the three
good ones. A change stamps `PriorityBy` and `PriorityAt`; re-sending the level
the project already holds writes nothing, the stamp included. `PATCH` and `POST`
do not reach it. Nothing reads the level yet: dispatch, the board and the web
UI are unchanged.
The dispatcher reads the list on every move a runner is handed - in every
column, hops included - except a review self-move its own verdict already
folds, folding a caller whose runner does not advertise every tag it holds - see
[the dispatcher's ninth refusal](#the-dispatcher). The project's list is the
floor: an issue's own tags and each of its ancestors' add to it, and never
subtract from it.
The project dialog carries it the same way: where `canAdminister` is false it
reads the list out as a sentence after the landing one - *"Any runner."*
where it is empty, *"Runners tagged macos and xcode."* where it is not - and
otherwise shows an owner a *Runner tags* textarea, one tag per line
(`ProjectEditDialog.tsx`).

`BindsTracker` is written the same way, through its own route, `PUT
/projects/{id}/binds-tracker` (`ProjectsController.PutBindsTracker`), guarded
exactly the way `PutPlaybooksSource` is - `[RequireRole(User)]`, `NotAPerson`
checked again in the action, then an owner check answering `403` naming the
project's key (*"only an owner of `<KEY>` may change whether it binds the
tracker"*) to anyone else. Turning it on is refused `400` for a project that
binds no repository, the same wording `PutPlaybooksSource` uses (*"`<KEY>`
binds no repository - bind one on the Projects page first"*); otherwise it
clears the flag from whichever other project held it, in the same save - two
`SaveChangesAsync` calls inside one relational transaction, since the filtered
unique index backing the flag is checked row by row - and the response
(`ProjectBindsTrackerDto`) names the cleared project's key, or `null` where
none held it. Turning it off clears nothing else, and re-sending the value a
project already holds writes nothing.
The project dialog carries it the same way: where `canAdminister` is false it
reads the flag out as a sentence after the runner-tags one, shown only where
it is set - *"Binds the tracker's own repository, set by `<name>` on
`<date>`."* - and otherwise shows an owner a checkbox, *"This project binds
the tracker's own repository,"* disabled where the project binds no
repository, with a hint (*"Only one project does: setting this takes it from
whichever project has it now."*) and, once set, a line naming who set it and
when (`ProjectEditDialog.tsx`).

`ReflectionCadenceDays` (`int?`, null off - whole days) and
`ReflectionLastPeriodStart` (`DateTimeOffset?`, null meaning never reflected)
are a sixth and seventh field, written through their own route, `PUT
/projects/{id}/reflection` (`ProjectsController.PutReflection`), guarded
exactly the way `PutPlaybooksSource` is - `[RequireRole(User)]`, `NotAPerson`
checked again in the action, then an owner check answering `403` naming the
project's key (*"only an owner of `<KEY>` may change how it reflects"*) to
anyone else. A cadence of zero or fewer is refused (*"a cadence is a positive
number of days"*); null turns reflection off and is never refused. Turning on
a non-null cadence is refused where no playbook row would run the reflection
it files - `Dispatch.MatchReflection` ranked the same way `Dispatch.Match` is,
with no from/to to pin since filing is not a transition - naming the Playbooks
page (*"`<KEY>` has no reflection playbook row - add one on the Playbooks page
first"*). This route writes only `ReflectionCadenceDays`; `ReflectionLastPeriodStart`
is written only by the reflection job, never here. That job,
`ReflectionCadenceJob` (`Modules/Hatch/`), polls every fifteen minutes and is
idempotent by construction: it advances `ReflectionLastPeriodStart` to the
due instant it just filed for, never to the instant it ran, so a firing that
finds nothing newly due writes nothing twice. `ReflectionNextDueAt` - the
start of the last period it reflected on plus the cadence, or now where it
never has - is computed on every read, never stored. `ProjectPatchRequest`
carries no field for any of the three, so this route is the only way to change
the cadence. The project dialog carries it the same way as the runner tags: an
owner sees a *Reflects every ... day(s)* number box after the auto-heal controls
(`ProjectEditDialog.tsx`), blank meaning off, with a muted line under it naming
the next due instant where a cadence is set; where `canAdminister` is false the
landing sentence ends instead *"reflects every 5 day(s), next due ..."* or
*"never reflects"*.
`ReflectNowIssueId` (`long?`, a foreign key to the issue it names, `SetNull`
on delete) is the reflection a reflect-now press last filed - HA-931; the
job above never sets it. The press itself is `POST /projects/{id}/reflect`
(`ProjectsController.PostReflect`), guarded the same way `PutReflection` is -
`403` naming the project's key to anyone but an owner. It files through the
same `Dispatch.MatchReflection` playbook match and the same title/description
shape (`ReflectionCadence.Describe`) the job uses, over a window of
`ReflectionCadenceDays` or 30 days ending now, through
`IssuesController.CreateIssueAsync` so the pressing owner, not "the board",
is the recorded actor. Where `ReflectNowIssueId` already names a
non-terminal issue, a press answers that issue again and files nothing -
checked on that field alone, never a broader search, so a reflection the job
filed (which never sets the field) is never mistaken for one a press already
owns. It never writes `ReflectionLastPeriodStart` either; a press neither
postpones nor claims the job's next scheduled reflection.

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
[logo](#logo) write already uses — a key is refused
outright before the owner check ever runs.

**`POST /projects/{id}/claim` is the way out of a project left with no
owner** — every guard above exists to keep from reaching that state any
other way, and this is the one door back out of it: whoever calls it
becomes an owner, refused with `409` ("{key} already has an owner") the
moment one already exists.

### Person scope

What a person may see of the projects, set by an admin. Two tables in the
`hatch` schema. `EfHatchPersonScope` (`PersonScopes`) — `PersonId` (the
primary key, a bare `Guid` with no foreign key, for the reason
[`ProjectMembers.PersonId`](#project-member) is one), `Mode` (`none`, `all` or
`some`, from `PersonScopeMode`), `UpdatedBy`, `UpdatedAt`.
`EfHatchPersonScopeProject` (`PersonScopeProjects`) — `PersonId`, `ProjectId`
(a cascading foreign key to `Projects`), unique on the pair.

**No row is `all`.** A person who has never been given a scope sees every
project, so adding the tables changes nothing for anyone. `none` is the empty
set; `some` is the rows in `PersonScopeProjects`. Writing `all` or `none`
deletes the person's project rows, so no list is left behind to come back if
the mode changes again. Deleting a project drops it from every chosen list,
through the cascade; the reads also join to `Projects`, so a deleted project
never shows even where a provider does not enforce the cascade.

**The routes are an admin's, and a person's alone — no key takes any of
them,** since a key that could write a scope could widen its own owner's view.

- `GET /api/scopes` answers every live person's scope,
  `[{ personId, mode, projectIds }]`. A person with no row reads as `all` with
  every project id, and so does one written `all`, so a client never branches
  on the mode to draw the list.
- `GET /api/scopes/{personId}` answers one, `404` for a person who does not
  exist.
- `PUT /api/scopes/{personId}` takes `{ mode, projectIds }`. `projectIds` is
  required under `some` (an empty list is a real, empty scope) and ignored
  otherwise. An unknown or missing mode, a missing list under `some`, or a
  project id with no project is `400`; an unknown person is `404`, checked
  first. Nothing is written unless all of it is valid. An admin may write
  their own. The write is logged: who set whose scope to what.

**The resolver is `ICallerScope`**, scoped, beside `IProjectAccess` and, like
it, answering about the caller and never an arbitrary person. See
[the resolver's rules](#the-callers-scope). The query filters on `HatchContext`
read what it answers: see [How scope is enforced](#how-scope-is-enforced).

### How scope is enforced

No row that belongs to a project leaves `HatchContext` for a caller whose scope
does not hold that project. It is one place, `HatchContext.OnModelCreating`,
and two tests that keep it whole. A project outside the scope reads as one that
does not exist: a lookup by id or key finds nothing, so its existence is not
told.

**The filter.** `ScopeResolutionFilter` is an action filter registered for the
whole Hatch module (`HatchModule`). It runs after the role gate, which is an
authorization filter and so runs first, and before the action. It resolves
[`ICallerScope`](#the-callers-scope) once and puts the answer on `RequestScope`,
a scoped holder shared with the request's `HatchContext`. It acts on this
module's controllers only, so a core route pays no scope read. No query filter
does I/O: they read the answer the filter already left.

**Fail closed.** A context that belongs to a request and has no resolved scope
throws `InvalidOperationException` on its first query of a filtered table, and
never answers everything. A context built with no request holder at all — the
tests, the design-time factory — is unbounded explicitly, and so is one built
inside a job, which has a holder and no HTTP context. The resolver's own reads
go through the same context with `RequestScope.Resolving` up, so they see every
project and need no opt-out. A table with no filter (below) is not guarded, so
a query on it does not throw.

**What carries a filter.** Every predicate opens with `Unbounded ||`, so a
caller with everything reads exactly what it did before.

- Directly, by project: `Projects`, `Issues`, `ProjectRepositories`,
  `ProjectMembers`, `ProjectLogos`; and `Playbooks`, `Dreams` and
  `Attachments` where `ProjectId` is set. A global playbook, a dream with no
  project and an unlinked attachment stay visible to everyone.
- Through an issue: `Comments`, `IssueEvents`, `WorkLog`, `MergeChecks`,
  `BuildChecks`, `Landings`, `Verdicts`, `Contributions`, `RunnerOmissions`,
  `Dependencies` (by `IssueId`), `DreamThreads`, `DreamRemands` and
  `ChangelogTasks`. `PlaybookProposals` and `PlaybookProposalChanges` read
  their issue, else the playbook they edit; a proposal with neither is
  nobody's and only an unbounded caller reads it.
- Through a repository: `TrunkBuilds`, `PlaybookFiles`, `DependencyFiles` and
  `ChangelogFiles`, visible when any visible project binds that `Canonical`.

**What does not.** `Statuses`, `WipLimits`, the runner tables, `PersonScopes` and
`PersonScopeProjects` (the resolver reads them), and everything in the core
schema.

**Required navigations.** A row whose required navigation points at a hidden
row is dropped from an `Include` rather than returned with a hole in it: a
dependency whose blocker is hidden does not come back from
`Dependencies.Include(d => d.DependsOn)`. Without the `Include` the row is the
caller's own and carries the id.

**Bulk writes.** `ExecuteUpdate` and `ExecuteDelete` take the filter, so a bulk
write never touches a row the caller cannot see. The in-memory provider refuses
bulk writes, so only the Postgres lane (`make test-api-db`) proves it.

**The opt-outs.** `IgnoreQueryFilters()` is called only where a read must see
every project to answer correctly and returns no content:

1. House-wide WIP counts, in `Wip`: the limit is the house's. The priority tree
   it reads is read the same way (`PriorityTree.ForAsync(ignoreFilters: true)`).
   So is the dispatcher's: the dispatch scan, the merge-train read, the claim
   lineage, the close-with-story read and the attention counts read the tree
   whole, whatever the calling key's owner may see, so a level on a hidden
   ancestor counts and a parent is never closed over a child the key cannot see.
2. Rank placement, in `RankService`: a column is shared by every project.
3. The dependency gate and the family gate, in `Dispatch`: a blocker in a
   hidden project still blocks, and a child in one still counts as open. Each
   reads the same tree, and neither is ever handed a scoped one.
4. `ProjectRepositoryBinding.WinnersAsync`: the project a repository belongs to
   is the same whoever asks.
5. The changelog file, in `ChangelogFiles`: the same bytes for every caller.
6. Whether a project key is taken, in `ProjectsController.KeyTakenAsync`: a key
   is unique across the house, so a hidden project's key is taken too. It
   returns one bool, no row.

**The reader tree.** A projection reads that same whole tree - a level that came
from a hidden ancestor still applies, since the loop acts on it - and lays the
reader's scope over it: `PriorityTree.ForReaderAsync` reads the ids the context
holds, with no opt-out of its own, and skips that read for an unbounded context.
`PriorityTree.Disclose` then says, for one issue, whether its parent or some
child is hidden, and runs every from-key through the scope: a key naming a
hidden issue leaves as null and its value is listed in `FromBeyondScope`; a
runner-tag source or omission from one leaves with a blank key and
`BeyondScope` set. The `Effective*` reads keep returning the real keys, since
the dispatcher's own sentences use them. What leaves is a level or a flag,
never a hidden key. `IssueRunnerOmissionsController` still reads scoped: its
`404` sentence names the ancestor.

A seventh sits inside the model, in the playbook proposal filter, to break a loop:
a change's filter reads its proposal and the proposal's filter reads its changes,
and EF overflows the stack applying each. Merge-train driver election is not
an opt-out: a key scoped out of a project does not drive its train.

**The two audits.** `QueryFilterAuditTests` lists every `IgnoreQueryFilters()`
call site by `File.Method`, with a count and a sentence saying why; an unlisted
site fails the build. `QueryFilterModelAuditTests` walks the model and fails on
any table with a foreign-key path to `Projects`, or a `Canonical` column, that
has no filter and is not on its named list — `PersonScopeProjects` is, for the
reason above. `ScopeEnforcementTests` (in-memory) and
`ScopeEnforcementDatabaseTests` (Postgres) read every filtered table as a
`some`, a `none`, an `all` and an unbounded caller.

**The matrix.** The filters are held at the data; `ScopeRouteMatrixTests` holds
the routes. It runs on the wall-up host, `WalledHost`: the real server over
migrated scratch databases with `Auth:Enabled=true`, the scope seed with marker
text in each project, and three people made in the database - a user scoped to
`none`, an admin who owns project A scoped to `some [A]`, and a user scoped to
`all`. A person acts through a real grant (`IAuthService.CreateInviteAsync`,
then `RedeemAsync`, sent as the cookie) and a key through a real secret, so the
role gate and the scope filter run as they do for a browser and a runner. Every
Hatch action is on exactly one of two lists in the test: `ProjectBearing`, with a
probe, or `ProjectFree`, with the reason it names no project. An action on
neither fails the build, which is how a new route is made to answer the
question. It needs no database.

A probe is a *lookup* or a *list*. A lookup names a project or an issue: for a
caller who cannot see it, the status must equal the status for an id nothing has,
and never be a 2xx or a 403. Its visible twin, the same request at a project the
caller sees, must differ from that status, or a probe that is `404` everywhere
would pass. A list's body must hold neither the hidden project's text nor its
key. The matrix runs four times, for a person and a key owned by a `none` person
and by a `some` person, each on its own database. A key refused by the role gate
(`key_not_accepted`) is refused for all three targets before scope is read, and
the probe says so. A probe known to fail is listed with its ticket and not sent.
It runs in the Postgres lane: `make test-api-db`.

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
[repository](#repository): `[RequireRole(User)]` without `AcceptKey` on the `PUT`
and the `DELETE`, checked again in the action.

`ProjectDto.LogoUpdatedAt` is what lets the browser point an `<img>` at a
stable `/api/projects/{id}/logo?v={ticks}` URL and still see a new
upload the moment it lands — the same role `PersonDto.PhotoUpdatedAt` plays for
a person's photo. Deleting a project takes its logo with it.

### Repository

`EfHatchProjectRepository` — `ProjectId` (FK, cascade), `Remote` (as typed),
`Canonical`, `BaseBranch` (nullable), `Changelog` (bool, default false: the
repository takes a changelog file), `SortOrder`, `CreatedAt`. Unique on
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
without `AcceptKey`, the same cut as a playbook, an assignee and a runner's bounds
(see ["the one edge that is deliberately cut"](#the-one-edge-that-is-deliberately-cut)).
A runner that could bind a remote could point every runner the board ever
dispatches at a repository nobody chose — the exact failure the closed edge
exists to prevent.

### Status

`EfHatchStatus` — `Name` (unique), `SortOrder`, `IsTerminal`, `IsDeferred`,
`IsWip`, `ExpressSkips`, `ParentPulls`, `AgentFiles`, `IsImplementation`,
`IsProtected`, `MergesPullRequest`, `RunnerTags`, `Color` (`#rrggbb`).

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

**A deferred column is not drawn on the board, and has no drop target.** There is
one exception, and it is a door that asks first: while a card is dragged, a
cloud above the columns is a drop target that reaches a deferred column. It is
safe because a drop on it commits nothing — it opens a dialog, and a remand
(`POST /api/issues/{key}/remand`) is sent only once that dialog is answered
**Remand**; escape, the scrim and **Leave it on the board** send nothing. Apart
from the cloud, that is the whole of the feature and the reason it is a flag rather than a convention:
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
silently hold a limit down. It is written only through `PUT /api/wip`, by
a person — see ["the one edge that is deliberately
cut"](#the-one-edge-that-is-deliberately-cut) — never through a status write, so
the ordinarily key-writable `PATCH /api/statuses/{id}` cannot reach it.
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
(`PUT /api/statuses/{id}/express-skips`), for the reason given under
["the one edge that is deliberately cut"](#the-one-edge-that-is-deliberately-cut):
it decides which gates the loop may pass unattended, and a key that could tick
it could carry its own ticket through the night with nobody reading it first.
Neither `POST /api/statuses` nor `PATCH /api/statuses/{id}` can set
it.

`ParentPulls` marks the columns a child is carried on out of, with no session,
while its parent stands in the implementation column — seeded true on
`Backlog` and read by [the hop](#the-hop). Set only by a person, through its
own route (`PUT /api/statuses/{id}/parent-pulls`), for the same reason
`ExpressSkips` is: it decides which gates the loop may pass unattended.
Neither `POST /api/statuses` nor `PATCH /api/statuses/{id}` can
set it either.

Out of the one `ParentPulls` column that feeds the implementation column
(`Columns.SendBack`), the pull carries only a child its parent already
planned. A story under a running epic is analysed instead, since that
analysis is the step that cuts a story into tasks, and so is a child whose
description is over the `BriefLimit` its playbook sets in the implementation
column, since the sendback hop would only carry it straight back. Every other
`ParentPulls` column, `Backlog` included, pulls as it always has. Ticking it on
the feeding column is what lets a task its story planned skip a second analysis;
the seeded default leaves that column unticked (AR-52).

`AgentFiles` marks the column a program's issue is born in — filing an issue
reads the leftmost column, as it always has, except where an issue is being
filed by a program (`ICallerIdentity.IsProgramAsync`) and a column carries
this flag, in which case that column is used instead. At most one column ever
holds it — `PUT /api/statuses/{id}/agent-files` clears every other row
when it ticks one, the same way `express-skips` does. It is read only where
the column is neither deferred nor terminal: a flag stranded on a column that
has since become either does not silently apply, the same guard `IsWip` reads
under. Set only by a person, through its own route
(`PUT /api/statuses/{id}/agent-files`), unreachable from either ordinary
status write — neither `POST /api/statuses` nor
`PATCH /api/statuses/{id}` can set it. A board with nothing ticked files
every issue leftmost, whoever files it, exactly as it always has.

`IsImplementation` marks the implementation column, the one where code gets
written, labelled *Code written here* on the Statuses page. Every rule keyed on
that column follows it: a [dependency](#dependency) holds the move into it, an
issue with a child still open is not carried out of it, and
[`ParentPulls`](#the-hop) carries a child on while its parent stands in it. All
three read `Columns.Implementation`. At most one column ever holds it —
`PUT /api/statuses/{id}/implementation` clears every other row when it
ticks one, the same way `agent-files` does. Ticking a deferred or terminal
column is refused with a 400 naming the column; unticking is always accepted.
It is read only where the column is neither deferred nor terminal: a tick
stranded on a column that has since become either is ignored, and the board
answers as if nothing were ticked, the same guard `IsWip` and `AgentFiles` read
under. Set only by a person, through its own route, for the reason under
["the one edge that is deliberately cut"](#the-one-edge-that-is-deliberately-cut):
it decides which move a dependency holds, and a key that could move it could
ungate its own ticket. Neither `POST /api/statuses` nor
`PATCH /api/statuses/{id}` can set it: a new column starts unticked, and a
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
(`PUT /api/statuses/{id}/protected`), checked again in the action —
the same `ProjectsController.NotAPerson` idiom a [repository](#repository)
write uses — for the same reason `ParentPulls` is closed to a key: it
decides who may move work past this door, and that is a playbook's kind of
power. Neither `POST /api/statuses` nor
`PATCH /api/statuses/{id}` can set it.

`MergesPullRequest` marks the column whose runner poll merges an issue's
pull request once it stands there, current and green, with no session —
HA-289's epic. Ticking it requires `IsProtected` already on — a column
that merges but is not protected would let anything that can move a card,
a key included, merge its own code — and refuses a deferred or a terminal
column for the same reason `IsProtected` does; unticking is always
allowed, the same stranded-flag tolerance `IsWip` gives. Set only by a
person, through its own route
(`PUT /api/statuses/{id}/merges-pull-request`), for the same reason
`IsProtected` is: it decides whose code merges unattended, and that is a
playbook's kind of power. Neither `POST /api/statuses` nor
`PATCH /api/statuses/{id}` can set it.

`RunnerTags` is a column's own runner tags, written through its own route,
`PUT /api/statuses/{id}/runner-tags`, refused to a program the same
way `IsProtected` and `MergesPullRequest` are - `NotAPerson` checked in the
action, with no owner check beside it, since a column has no owner the way
a project does. The list is replaced whole, run through
`RunnerTags.Normalise` in `Hatch.Contracts` first - trimmed, folded to lower
case and deduplicated, at most 8 tags, each at most 32 characters with no
space or comma inside - the same rule a [project](#project)'s own list
answers to. A tag that breaks it, a blank one included, or a list over 8 is
refused `400`; a null body is refused `400` too, rather than read as "leave
it alone", since this route only ever replaces the whole list. Re-sending
the list the column already holds, the same tags in the same order, writes
nothing, and an empty list clears it. Neither `POST /api/statuses` nor
`PATCH /api/statuses/{id}` can set it.

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
[Letting the loop merge for you](../src/Hatch.Web/apps/hatch/docs/letting-the-loop-merge-for-you.md).

A migration flags `In Progress` and `In Review` on a board that still has
columns of exactly those names, and writes no limit row — the WIP section is
marked, and from then on `board.wip` reports a real load for both slices, but
the section-wide limit draws, refuses or folds nothing until the operator
types a number on the Statuses page. An epic's own limit is the same: a blank
*Stories at once* is no limit, and holds only where a number is typed on the
epic's page — see ["An epic's own limit"](#an-epics-own-limit). A renamed board
seeds neither.

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
`StatusId`, `ParentId`, `FiledFromId`, `Rank`, `ReadyAt`/`ReadyAtHasTime`,
`DueAt`/`DueAtHasTime`, `PullRequestUrl`, `PullRequestMergeState`,
`PullRequestReviewDecision`, `PullRequestHeadSha`, `PullRequestHeadSince`,
`PullRequestReadAt`,
`WaitingPrTitle`/`WaitingPrBody`/`WaitingPrBranch`/`WaitingPrSha`/`WaitingPrRemote`/`WaitingPrRunner`/`WaitingPrWhy`/`WaitingPrAt`
(one [waiting pull request](#a-pull-request-waiting-to-be-opened), flat nullable columns, all set or all null),
`ClosedAt`, `ModelOverride`, `EffortOverride`, `AssigneePersonId`/`AssigneeApiKeyId`,
`CreatedBy`, `CreatedAt`, `UpdatedAt`.

The display key `AER-12` is **computed** (`Project.Key + "-" + Number`) and
never stored, so there is exactly one fact about a key anywhere and no chance of
two copies disagreeing after a rename.

Types are `ambition | epic | reflection | story | task | bug`. Parentage is optional and
validated server-side: the parent must exist, form no
cycle, and be a legal type — ambition→none, epic→epic, reflection or ambition,
reflection→epic, story→epic or reflection, task→story, bug, epic or reflection,
bug→epic, story or reflection. An ambition takes no parent, and a request that
gives one is refused with *an ambition takes no parent*. Retyping is judged on
three counts and no others: an issue becomes an ambition only while it has no
parent and every child is an epic, an ambition becomes another type only while
every child could hang under it, and an epic under an ambition stays an epic;
each `400` names the first child, lowest number first, that stops it. A
reflection or an ambition with children answers [the hop](#the-hop) and [an
epic's own limit](#an-epics-own-limit) exactly the way an epic does: an epic
under an ambition standing in the implementation column is carried as a
sub-epic is, and one under an ambition left of it is folded with *its parent
epic is not running*. An ambition standing in the implementation column is
dispatched to its own pass, a move from that column to itself, while fewer of
its epics are open than its limit (one when it has none; a deferred epic does
not count). At the limit it is folded with *2 of its epics are open, and it
runs 2 at once - the next pass runs when one closes*, and an ambition with no
epics is clear. A pass that ran is not repeated on the same room: an ambition
whose newest `worked` pass is newer than anything that opened room since is
folded with *a pass ran on it 2 hours ago and nothing has opened room since -
the next runs when an epic under it closes or a person changes it*. A child epic
closing or being deferred, or a person changing the ambition's limit, column or
capabilities, opens room again; the pass's own writes (a verdict, a comment, a
new epic) do not. A pass released `dropped` is not counted - the stall path
retries it - and an ambition that has never had a pass is clear.

A parent may stand in another project. The one refusal that keeps a line is
[one pull request](#one-pull-request): a task cannot be filed, moved or
retyped under a story or bug of another project whose tasks share one pull
request, and the `400` names the story. Only `task` children count - a bug
under a story is its own sharer. The reverse holds as well: one pull request
cannot be turned on for a story with a task in another project, and neither
the project's default nor its `enforced` policy can be turned toward on while a
story that would read it has one; each `409` is a sentence with a count and
never the hidden task's key.

**An issue records the issue it was filed from.** `FiledFromId` is a nullable
foreign key to another issue, `SetNull` on delete and indexed: the issue whose
session filed this one. It is provenance and nothing else - no dispatch, hold,
order, rollup or merge gate reads it, so an issue filed from an open issue is
dispatched exactly as one filed from nothing, which is what separates it from a
[dependency](#dependency). It may name an issue in another project, as a
dependency may: a reflection files its tracker gaps in whichever project is
flagged `bindsTracker`. Set at creation with `filedFromKey`, or changed after
with `PATCH`/bulk, where `""` clears it, as for `parentKey`. Refused with a `400`
saying which when the key names no issue or the issue itself; there is no cycle
walk. A change writes a `filed_from_changed` event on the issue that changed and
on no other; a create writes none, and neither does deleting the source. A
project move leaves it alone. The issue read carries `filedFromKey` and
`filedKeys`, the issues filed from this one in key order.

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

**The five fields after it are what the forge last said of that pull request.**
`PullRequestMergeState` is `clean`, `blocked`, `behind`, `dirty`, `unstable`,
`draft` or `unknown`; `PullRequestReviewDecision` is `none`, `approved`,
`changes-requested` or `review-required`; `PullRequestHeadSha` is the commit
that was read, and `PullRequestReadAt` is when. `PullRequestHeadSince` is when
the head sha last changed as the board read it: it is set on the first read and
whenever the sha differs from the one held, and a repeat read, a new merge state
or a new review decision on the same head leaves it. It rides on the review read
(`ReviewCheckDto.PullRequestHeadSince`), and nothing reads it yet. All five are
nullable, and null means *not read* — which is not `unknown` or `none`, both of which are things
the forge can say. Only `PUT /api/issues/{key}/pull-request-state` writes
them, and when the url changes they are cleared together with
`PullRequestClosedAt`, since a state read of one pull request says nothing of
another. Beside them sits `PullRequestHeadBranch`, the branch the pull request
comes from, which is the pointer to the issue's change: the runner's read
sends it on the same route, a blank or absent one leaves what is held, and it
is cleared with the url and with nothing else. It is no event of its own and
does not move `UpdatedAt`. It rides on the review read as
`ReviewCheckDto.Branch` and on a dispatch as `IssueDto.PullRequestHeadBranch`,
and a contribution's recorded head branch, where the issue has one, is the same
pointer and comes first. They are stored and drawn, and `MergeWork.Judge` reads
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
/api/issues/{key}/priority` sets the level by name; the older
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
rank. In the dispatcher it is [one walk of the columns per tier](#the-dispatcher)
rather than a sort of the finished rows — an expedited project's work before
any other project's, then low, then economy, and paused walked last. Paused is always folded there, so it never
changes which issue `next` or a hop picks, only that `queue` can report it;
low and economy are each gated rather than always folded, so unlike every
other walk, a row reached on either pass can still be skipped by its own pace
check for `next` and `queue` alike — a hop is exempt from both gates, the same
way it is exempt from needing a playbook.

**A project has a level of its own: paused, normal or expedited.** An owner
sets it on the project (`PUT /api/projects/{id}/priority`), and it reaches
every issue in the project — it is not inherited down a tree, because a
project is not an issue's ancestor. An **expedited** project's clear work is
picked up before any other project's, whatever column it stands in and
whatever level its issues sit at: an expedited project's *normal* row goes
before another project's *expedited* row. An emergency issue in any unpaused
project still goes before an expedited project's work. Another project's clear
rows read as parked, naming the expedited project and the project level they
wait on — and only while the expedited project has a clear row. A runner that
cannot work the expedited project at all (bound to another project, short of a
runner tag, short of a declared dependency) folds every row in it with the
usual sentence, so those rows are not clear, park nothing, and the runner is
handed the next-best project's work. A **paused** project is set aside whole:
every row in it is folded for `work/next`, `work/queue`, `work/{key}` and every
hop with *project HA is paused - a person set it aside, and nothing in it picks
up until they set it back*, kind `person`, an emergency issue included. The
merge column's own judgement still comes first, so a merge-column row in a
paused project keeps the merge judge's sentence. A paused project's rows also
stop counting against the [WIP section](#wip), the way a paused issue's do.
`QueueEntryDto.ProjectPriority` carries each row's project level.

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

**A level crosses a project line only on its setter's authority.** The
issue row records who set its level and its priority - `PreApprovalSetBy` and
`PrioritySetBy`, a person id written by `IssuePreApprovalController`,
`IssueExpediteController` and the filing path from `IActorDirectory.MeAsync`,
cleared when the level goes back to `none` or `normal`. The trunk-failure bug
and the changelog task file with no setter. Inside one project a level is
honoured as it always was, with or without a setter and whatever role the
setter holds. Found on an ancestor in *another* project, the walk honours it
only while the setter could set the same level on the descendant now: for
`preapproved`, a live person who is an owner or approver of the descendant's
project; for `express`, a live person who may move an issue of that project
into the column that feeds implementation (the `CanMoveIntoAsync` test); for a
priority, a live person. In every case the descendant's project must be in the
setter's scope. A deleted person is not live - their membership row lingers
and counts for nothing. A level with no setter recorded, including every level
set before setters were recorded, is never honoured across a line. Where the
test fails the walk passes over the level as if it were not set and carries on
up. The rule is `SetterAuthority`, which `ProjectAccess` applies to the caller
too, so the two cannot disagree; `PriorityTree.ForAsync` reads the members,
scopes and live people once, past the caller's own scope, so the judgement is
live - take the setter's role away and every descendant reads the change on
the next request - and nothing is copied onto a descendant. `preApprovalPassedOver`
and `priorityPassedOver` on the issue and its card name the nearest ancestor
whose level was passed over, which is what a card needs to say *"pre-approved
on AER-1, not honoured in OPS"*.

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

**Setting either level is closed to a key — the same cut as
[expedite](#expedite) and [stories at once](#stories-at-once), and for the
same reason: a key that could set either could carry its own ticket through
the night unattended, or approve it in advance.** `IssuePreApprovalController` does not set `AcceptKey`,
so both routes refuse a program outright before the issue is even looked
up. It follows that there is no `hatch` verb that sets it — the CLI
authenticates with a key, so the terminal shows the level (`show`, `board`,
`next`, `queue`) and sets it nowhere.

Setting a board up so a pre-approved epic runs this way end to end, with
nobody pressing anything but the one drag [the hop](#the-hop) cannot make
for it, is [its own walkthrough](#a-night-mode-board).

#### Runner tags

**An issue's own say over which runner may work it.** `RunnerTags` is a
jsonb array of strings on the issue, `"[]"` default, the same shape as
[a project's](#project) - normalised, case folded and deduplicated by
`RunnerTags.Normalise` in `Hatch.Contracts` before a tag ever reaches the
column. Written only through its own route, `PUT
/issues/{key}/runner-tags` (`IssueRunnerTagsController.PutIssueRunnerTags`),
open to a key the same way [one pull request](#one-pull-request) is: it is a
statement about which runner may pick the ticket up, not a power over the
board, so a key is given nothing it could not already do by filing the
ticket tagged that way. No type restriction, no claim gate, no lock - any
issue may carry tags, and any key may set them at any time. `request.Tags`
absent (JSON `null`) is `400` - *"tags is required - send `[]` to clear"* -
since `Normalise` cannot otherwise tell "clear it" from "say nothing",
answering null for both. The list is replaced whole: re-sending the list the
issue already holds, the same tags in the same order, writes nothing; a
change writes `runner_tags_changed` with `{ from, to }`, both sides a real
array. `PATCH /issues/{key}`, create and the bulk endpoint carry no field
for it, so this route is the only way in or out.

**The tags a runner is actually held to are a union, not an override, and
are read live at every request the same way `EffectivePreApproval` is.**
`PriorityTree.EffectiveRunnerTags` walks the project's own list, then every
ancestor's own, root-most to nearest, then the issue's own, deduplicating
against what came before so a tag named twice counts once. A card or an
issue carries all three: `runnerTagsOwn` is the issue's own row, exactly
what `PUT .../runner-tags` last wrote and nothing else; `runnerTags` is the
union just described; `runnerTagsFrom` names where each tag in `runnerTags`
beyond the issue's own comes from, one entry per non-empty contributor in
the same root-most-first order - the issue's own row is never named there,
even when it carries tags.

#### Dangerous

**A reason running an issue is dangerous, carried down its subtree.**
`Dangerous` is a nullable text column on the issue - null is "not flagged",
and there is no separate boolean. Written only through its own route, `PUT
/issues/{key}/dangerous` (`IssueDangerousController.PutIssueDangerous`), open
to a key the same way [runner tags](#runner-tags) are: it is a statement about
the ticket, not a power over the board, and it only ever narrows who runs it.
No type restriction, no claim gate, no lock - any issue may carry a reason,
and any key may set it at any time. `{ reason }` is trimmed; `""` or
whitespace clears it (stores null). `reason` absent (JSON `null`) is `400` -
*"reason is required - send \"\" to clear"* - so clearing is something the
caller says. A reason over 200 characters, measured after the trim, is `400`
- *"a dangerous reason is at most 200 characters"*. Sending the value the
issue already holds writes nothing, adds no event and leaves `UpdatedAt`
where it was; a change writes `dangerous_changed` with `{ from, to }`, either
side null, and sets `UpdatedAt`. `PATCH /issues/{key}`, create and the bulk
endpoint carry no field for it, so this route is the only way in or out.

**The reason that applies is read live, the same way `EffectivePreApproval`
is.** `PriorityTree.EffectiveDangerous` walks from the issue up its parents
and answers the first non-empty reason it meets, with the key of the ancestor
it came from, or `(null, null)` when none is flagged. A card or an issue
carries three fields: `dangerousOwn` is the issue's own row, exactly what
`PUT .../dangerous` last wrote and nothing else; `dangerous` is the reason
that applies - its own, else the nearest flagged ancestor's, else null; and
`dangerousFrom` is that ancestor's key, null when the reason is the issue's
own or there is none. The issue, the card list, search and the board all
carry them. Nothing acts on the flag yet.

#### Human work

**A person's own say over who works an issue - a card for a person to move
around the board, with no runner ever reaching for it.** `HumanWork` is a
nullable boolean column on the issue: null is `inherit` (the default), true
is `human`, false is `agent`. Written only through its own route, `PUT
/issues/{key}/human-work` (`IssueHumanWorkController.PutIssueHumanWork`),
`{ value }` one of `human`, `agent` or `inherit` - a person's call, never a
key's, the same reach as Local model (`IssueLocalModelController`). An unknown value is
`400`, naming the three. Sending the value the issue already holds writes
nothing, adds no event and leaves `UpdatedAt` where it was; a change writes
`human_work_changed` with `{ from, to }`, each one of the three names, and
sets `UpdatedAt`.

**The value that applies is read live, the same shape `EffectiveLocalModel`
is, and nothing else.** `PriorityTree.EffectiveHumanWork` walks from the
issue up its parents and answers the first value that is not `inherit`,
with the key of the ancestor it came from - `agent` with no ancestor
flagged. Unlike priority and [Dangerous](#dangerous), where
only an explicit flag on an ancestor propagates and a child cannot clear
it, the human-work walk stops at an explicit `agent` the same as at an
explicit `human`: a descendant may set itself back to `agent` under a
human parent, and dispatch honours it. Nothing is copied onto a row, so
reparenting an issue changes the live value at once, with no write of its
own. A card or an issue carries three fields: `humanWorkOwn` is the
issue's own row, exactly what `PUT .../human-work` last wrote, by name;
`humanWork` is the value that applies - `human` or `agent`, never
`inherit`; and `humanWorkFrom` is the ancestor's key the value came from,
null when it is the issue's own or nothing is set anywhere. The issue, the
card list, search, the plan and the board all carry them.

**Folded in `Dispatch.Blocked`, beside the paused fold and ahead of every
loop-only one.** An issue whose `EffectiveHumanWork` reads `human` is
refused by `work/next`, `work/queue`, `work/{key}` and a hop alike, with
kind `BlockedKinds.Person`: *"human work - a person does this one, and
no runner picks it up"*, or *"human work from `HA-12` - ..."* when the
value is inherited from an ancestor. Not conditional on a hop the way the
economy and low-priority folds are: a hop is exactly a move with no
session, and a human issue gets none. Moving it by hand - the board, or
`hatch move` - is unaffected; dispatch is the only reader of the override,
so a person moving their own card is never refused by it.

#### Omitted runners

**A runner an issue - and everything beneath it - is closed to.**
`EfHatchIssueRunnerOmission` is a child table, `IssueRunnerOmissions`, keyed
on `(IssueId, RunnerName)` and cascade-deleted with the issue, not a jsonb
column the way [runner tags](#runner-tags) are: a list replaced whole would
lose the who-and-when of the entries it did not touch. Each row carries a
`Reason` (nullable, at most 200 characters after the trim), the actor name
that set it (`By`), and when (`At`); the runner is named the same way
everywhere else - a key's name, or for a genuinely keyless heartbeat the name
`LegacyRunners` keys on. Written through `PUT
/issues/{key}/omitted-runners/{runner}`
(`IssueRunnerOmissionsController.PutOmittedRunner`) with `{ reason }`, and
lifted through `DELETE` on the same path (`DeleteOmittedRunner`), open to a
key the same way [runner tags](#runner-tags) and [dangerous](#dangerous)
are: a statement about which runner may pick the ticket up, not a power
over the board, so no type restriction, claim gate or lock. Both answer the
`IssueDto`. The runner name and the reason are trimmed; an absent or blank
reason is stored as none - but unlike `dangerous`, the `DELETE` route, not
an empty reason, is how an omission goes. A blank runner name or one over
`EfHatchRunner.MaxNameLength` is `400`, as is a reason over 200 characters.
An unknown issue is `404`; on `DELETE`, so is a runner the issue does not
itself omit, naming the ancestor that carries it instead - *"HA-1 has no own
omission for \"x\" - it is inherited from HA-0"* - where only an ancestor
carries it, since lifting it is that ancestor's to do. A repeat `PUT` with
the same runner and the same reason writes nothing and leaves `UpdatedAt`
alone; a changed reason rewrites `Reason`, `By` and `At`.

**Written or lifted, the change is carried as an event, and the runners
actually closed are a union, not an override.** A write lands
`runner_omitted` with `{ runner, reason, from }` - `from` the previous
reason, null on a first omission - and a lift lands
`runner_omission_lifted` with `{ runner }`; both set `UpdatedAt` and name
the caller as actor. `PriorityTree.EffectiveOmittedRunners` walks ancestors
root-most first, then the issue's own, deduplicating by runner name so the
root-most contributor wins - the same shape as
[`EffectiveRunnerTags`](#runner-tags), and union rather than
nearest-ancestor-wins for the reason priority and pre-approval are not: an
omission only ever narrows who may run a ticket, so a child must not widen
what its parent already said - a runner recused from an epic for a toolchain
it lacks must not be handed one of its stories. A card or an issue carries
two fields: `omittedRunnersOwn` is the issue's own rows, ordered by runner
name, `fromKey` always null; `omittedRunners` is the union just described,
each entry a `RunnerOmissionDto(Runner, Reason, By, At, FromKey)` with
`fromKey` null for the issue's own. The issue, the card list and search
carry both. The dispatcher folds a ticket omitted for the runner that asks -
see [the dispatcher](#the-dispatcher), item 10.

#### Stories at once

**One epic's own ceiling on how many of its stories run at once.** `WipLimit`
is a nullable int on the issue, meaning something on an epic and nothing on
any other type; null is **no limit** — as many of its stories at once as the
board's other rules allow (dependencies, the section-wide slice, claims).
Holding stories to it — folding a story past the first N in
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

Written only through `PATCH /api/issues/{key}/wip` with `{ limit }` as a
string — `""` clears it to no limit, a whole number of one or more
sets it, anything else is refused with a sentence, and anything but an epic,
a reflection or an ambition is refused with a sentence saying only an epic, a
reflection or an ambition takes a limit on its children at once — by a person and
never a key, the same cut as [expedite](#expedite) and
[express](#pre-approval): see [The one edge that is deliberately
cut](#the-one-edge-that-is-deliberately-cut). Each change writes
`wip_limit_changed` with `{ from, to }`. Reading is open, like everything else
a dispatch needs: an agent is entitled to know how many of its siblings it is
competing against, and it rides `IssueDto` like `ModelOverride` does.

It follows that there is no `hatch` verb that sets it, as none sets expedite,
express or a playbook — the CLI authenticates with a key, so `hatch show`
prints it on an epic (`stories at once: 3`, or `stories at once: no limit`
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

An issue's project can change too — `PATCH /api/issues/{key}` takes a
`projectId` alongside the fields above. The move mints the issue a number in
the target project through the exact mint just described, with the same
retry on a lost race; its old key goes dead, the same cost a project's own
key rename pays (see [Project](#project)), just paid by one issue instead of
every issue under it.

A parent may stand in another project, so a move keeps the tree it finds. An
ancestor never moves *with* its descendant, only a descendant moves down, and
the moved issue keeps its own parent: a request with no `parentKey` is no
opinion on it, as everywhere else, so no `parent_changed` event is written. It
is re-parented only when the same request names another parent, resolved by the
type rules alone, or sends `""` to clear it. `moveDescendants: true` — the
default when the field is absent — carries the descendants reached through
links inside the moved issue's own project, so that subtree's parentage
survives inside it. A child in another project is not carried and not
re-keyed: it keeps its link to the moved parent, and nothing under it moves
either, even a grandchild in the moving project. `moveDescendants: false`
leaves the direct children in the old project instead, still under the moved
issue, with their own subtrees under them intact.

What refuses, before anything is written: an unknown target project (`400`);
the issue's own project, read as no opinion at all rather than a move
(nothing staged); a caller that is a key (`403` — an
issue's project decides which repository it is dispatched to, the same
routing-power argument that keeps [binding a remote](#repository) closed to
a key); any issue in the moving set holding a live [claim](#claim) (`409`,
naming it); a story or bug in the moving set whose one-PR setting would read
on in the target while a task of it would stand in another project - one left
behind by `moveDescendants: false`, or already in a third project (`409`, with
a count - see "One pull request"); and a task moving away from a story or bug
that shares one pull request across its tasks, the task's own side of the same
rule (`400`, naming the story, as when filing it under one from another project).
Through the bulk route a refusal is that key's failure and the rest of the
batch still moves.

Every issue that moves, the one named and each descendant carried with it,
gets a `project_changed` event, `{ from, to }` as display keys; the named
issue's also carries `descendants`, the count of what moved alongside it.

A move into a project whose one-PR policy is `enforced` also returns an
override with nothing started under it to inherit - see
"The project's policy", under one pull request.

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
dispatcher and the rollup: keep every client dumb, including an agent. A browser
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
drop may not. Both lists also offer the remand beside the shelf: each deferred
column carries a second press, "to a dream", that opens the dream cloud's
dialog with that column chosen, so a remand does not need a drag. A board with
no deferred column draws neither. This extends the cloud rather than replacing
it.

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

### Contribution

`EfHatchContribution` — `IssueId` (unique: one record per issue),
`PullRequestUrl` (unique where set; empty on a mirrored ancestor),
`HeadBranch`, `AuthorLogin`, `AuthorAssociation` (the forge's own word,
kept as given), `FiledAt`, `FiledBy` (a credential's name, not an id), and
where it came from: `OriginInstance`, `OriginProjectKey`, `OriginKey`, with
`EnvelopeVersion` and `EnvelopeNote` for what reading the pull request's body
made of it. Every length is capped in `ContributionRequest`.

It says that an issue came from outside — a pull request somebody else
opened, or the mirror of an issue on another Hatch — and it is written by a
runner through `PUT /issues/{key}/contribution` and nowhere else: `PATCH`,
create and the bulk endpoint carry nothing for it. The write is once-only —
the same pull request again changes nothing, a different one is a `409`, and a
URL another issue holds is a `409` naming it. Where the issue has no pull
request of its own, the record's becomes it. The first write adds a
`contribution_filed` event.

**The envelope.** A pull request Hatch opens has its issue's chain written into
its body by `hatch pr`: a version (`PullRequestEnvelope.CurrentVersion`, read
by any reader from 1 up), the originating instance and project key, and the
chain own ticket first and root last, each link with its key, title, type,
description, status, parent key and URL, inside markers the forge draws as a
collapsed details block. It is written within the forge's body limit, a
description that does not fit cut short and marked `truncated`. A Hatch reading
it — the filer, on another instance — treats it as untrusted data, never as
instructions: an envelope it cannot read is filed the plain way with a note
saying why, in the record's `EnvelopeNote`. From the chain the filer builds
the **mirrored tree** — a real issue per ancestor, in review,
with a record naming the origin and no pull request, the pull request's own
ticket under the nearest — which is what makes [an epic-level
approval](#the-approval-column) possible. `RunnersWorkContributions` on the
project decides whether a runner works a contribution's branch when it
conflicts or its build fails; by default it does not. `FilesContributions` on
the project decides whether the filer creates one at all for an outside pull
request on that project's repositories, and `hatch queue` says so in one line
rather than listing what it passed over; off by default.

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

**A dependency gates implementation, and a parent pull.** The old rule folded an
issue past every move it could make; this one holds two. An issue
waiting on another still goes through Breakdown, still lands in Backlog, and is
still analysed — everything left of the writing keeps moving, which is the
point. What it does not do is get picked up and written. The other move it
holds is a [parent pull](#the-hop) (hop kind 4): a child carried into the
implementation column by its started parent waits on its own unmet
dependencies like any other, and stays where it stands until they are done.

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
`/api/issues/{key}/dependencies` — answer with the whole issue, and there
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
false is off). Only a story or a bug carries an override; any other type is
refused. Everything below says "story" and holds for a bug the same way: a bug's
tasks share the bug's branch and the bug opens the one pull request.

The effective value is **derived at read and never copied**, the same way
[pre-approval](#pre-approval) is. `PriorityTree.EffectiveOnePr` walks to the nearest
story or bug at or above an issue and answers with its override, else its
project's default; an issue with no story or bug above it - an epic, or a task filed
straight under one - reads off, because there is no story whose pull request it
could share. Reparenting, or flipping either level, changes what everything
beneath reads on the next request with no write to the issues beneath.
`IssueDto` and `IssueCardDto` carry `onePr` (the effective value), `onePrOwn`
(this story's own override, null when it inherits) and `onePrFrom` (the story a
task read it from, null when it is its own, the project's, or absent).

`IssueCardDto.createdBy` is the issue's own `CreatedBy` - a name, not a
foreign key to People, so a runner's key files tickets under its own name too.
Every route that serves a card carries it: the same six routes `noCode` does
below, plus `/search` and a work response's child cards. The synthetic
"Other" card on the timeline carries an empty one, having no issue of its own.

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

**The project's policy.** A project also carries `OnePullRequestPolicy`:
`optional` (the default - a story or bug may override the project's default
either way, as above) or `enforced` (no story or bug may carry a setting of its
own). Under `enforced`, every override that exists is locked: it either
predates the policy, on a story that had already started when the policy
tightened, or was written by that tightening's own reconciliation. This field
decides what happens to the overrides that already exist; a new one is refused
at both doors, with a `409` and one sentence - *\<project> enforces its one-PR
default on every story - \<the issue> may not carry a setting of its own; an
owner can make it optional on the Projects page*: `PUT /issues/{key}/one-pr`
with `on` or `off` (ahead of the lock's `409`; `inherit` and a value already
held pass), and `POST /issues` with `onePr` `on` or `off` for a story or bug.
Tightening (`optional` to `enforced`)
reconciles in the same save: every story or bug with an override and no started
work returns to inherit, one `one_pr_changed` event each, `from` the
override's `on` or `off`, `to` `inherit`, with `fixedBecause` saying *the
project began enforcing its default with nothing under this story started*. One
with started work keeps its override untouched. Loosening (`enforced` to
`optional`) writes nothing to any issue. "Started" is not a second definition:
it is the test `OnePrLock.OverriddenAndUnstartedAsync` applies - any descendant
in the implementation column or to its right, terminal included - the same one
`PutLanding` uses to fix started stories that inherit. A board with no
implementation column returns nothing, so nothing is reconciled.

A move into an `enforced` project is the third door, and reconciles the same
way - by `PATCH /issues/{key}` with `projectId` or `POST /issues/bulk` with
`projectId`, one rule for both since the bulk route stages each issue through
the patch's own path. A story or bug in the moving set (the one named and every
descendant carried with it) that carries an override and has no started work
under it returns to inherit, one `one_pr_changed` event each, `from` `on` or
`off`, `to` `inherit`, with `fixedBecause` naming the move. One with started
work keeps its override. The started test is the same
`Columns.AtOrPastImplementation`, applied in memory (`OnePrLock.OverriddenAndUnstartedAmong`)
to the statuses the edit has just staged and to the moving set alone: with
`moveDescendants: false` the started tasks stay behind, so the story that moved
counts as unstarted and is reconciled - which keeps every override under an
enforced project locked. What the story then reads decides whether the tasks
left behind refuse the move, as above. A move out of an enforced project, or into an
`optional` one, writes no `one_pr_changed` event.

A bug was refused an override until it could carry one, so its tasks were always
begun on branches of their own. The migration `FreezeBugOnePr` gives every bug
that already has children an explicit `off`, so a project whose default is on does
not move that in-flight work onto the bug's branch; a bug with no children keeps
following the default, as a story does. It writes no events.

**A task waiting on the story's pull request.** With the effective value on, a
task whose session has finished moves to the review column and *waits there
with no pull request of its own*. Precisely: a task under a story or bug that
shares, standing in the review column or any non-terminal, non-deferred column
to its right, with no pull request recorded on it **and no
[no-code declaration](#finished-with-no-code)** (`PriorityTree.WaitingOn`,
which asks `SharedStoryOf` for the type, the pull request, the declaration and
the parent that shares, and the board for the column). That is the whole test.
The server never sees a push - only a runner sees git - so it does not ask whether the task put anything on the shared branch: a task that
reached review having pushed nothing, and declared nothing, reads as waiting
all the same, and saying that it pushed nothing is what the declaration is
for. A declared task is not waiting: it has nothing on the shared branch to
wait for, so it is held in review, or closed on its pre-approval, on its own.
For the story's purposes a waiting task is finished:

- **The next sibling is pulled.** `FamilyGate.SharedBranchSibling` does not count
  it, so a `ParentPulls` column carries the next child while this one waits,
  instead of waiting for a merge that cannot come until every sibling is done.
  What holds a parent pull is a shared story's sibling at or past the
  implementation column, not terminal, and not waiting on the story's pull
  request: the child is folded with "{sibling}, sharing its parent's branch, is
  already in flight - one sibling at a time". Under a story or bug with sharing
  off, or an epic, there is no shared branch and no sibling holds a pull;
  [dependencies](#dependency) order those siblings instead.
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
its parent's branch, is held…"* — and a take that raced is let go by the same
check as an ancestor's. A task with a pull request of its own is on its own
branch and is not held. Tasks under different stories stay parallel.

The same rule holds at the dispatcher, before any claim exists: a move into the
implementation column is folded while a sibling is in flight on the story's
branch — at or past that column, not terminal, and not waiting on the story's
pull request. The sibling's own pull request does not matter; the moving task is
the one that would go on the story's branch. The sentence is the same and the
kind is `family`.

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

### Finished with no code

A ticket whose work changed nothing - the setting was already what the ticket
asked for, the question was answered on the ticket, the bug did not reproduce -
has no branch to push and no pull request to open. It says so, and the board
takes its word: `hatch pr KEY --none "reason"`, which is `PUT
/api/issues/{key}/no-code` with `{ reason }`. The reason is trimmed and
kept on the issue (`EfHatchIssue.NoCode`, with `NoCodeAt` for when); `""` or
whitespace clears it, an absent reason is a `400` so that clearing is something
the caller says, and the length limit is `NoCodeRequest.MaxLength`. Setting
what the issue already holds writes nothing. The route is open to a key, as
[Dangerous](#dangerous) is: it gives a key no power over the board, and it is
the only way in - `PATCH`, create and the bulk endpoint carry nothing for it.
`IssueDto` carries `noCode` and `noCodeAt`, and `hatch show` prints the reason.

**Never on a card with a child.** `IssueCardDto.noCode` is null on any issue
that is some other issue's `parentKey` - a story with tasks, a bug with
tasks, an epic with stories - whatever it declared and whenever, because its
children carry the code and a declaration on it says nothing. Every route
that sends a card (`/board`, `/issues`, `/plan`, `/plan/{key}`,
`/reports/timeline`, `/reports/changelog`) answers through the one helper,
`NoCode.OnCard`, reading `PriorityTree.HasChildren` so the six cannot drift;
a ticket that gains its first child loses the glyph on the next load. The
declaration itself (`EfHatchIssue.NoCode`, `IssueDto.noCode`, `hatch show`,
the attention panel and the close hop below) is unchanged - a parent can
still make it, and it still stands in review for a person to close.

**Declared, never inferred.** A ticket with no branch and no pull request in
review reads, to the board, as a session that forgot to push, and that reading
is the useful one: it is the failure the attention panel's *in review without a
pull request* count exists to catch. Were the board to infer "no code" from
"no branch", that count would lose the very case it is for. So the board never
concludes it; only the session that did the work can say it, and it says so by
a call the trail records.

**When it is refused and when it is set aside.** The declaration is a claim that
the work needed no branch, and anything that shows otherwise defeats it:

- **Refused (`409`)** once the issue has a pull request recorded, or a counted
  [branch-finding merge check](#merge-check) - a branch on origin, on a
  repository the project binds, whose verdict is `clean`, `conflicted` or
  `ambiguous`. The sentence names the branch and says to push it and record a
  pull request instead. Clearing is never refused.
- **Set aside** when either turns up afterwards: a counted branch-finding merge
  check reported by a runner, or a pull request recorded on the issue.
  `NoCode.SetAside` clears the declaration and writes a `no_code_set_aside`
  event whose payload is the reason that was set aside beside what set it
  aside - the branch's remote and verdict, or the pull request's url. A
  repository the project no longer binds shows nothing.
- **Cleared by a move left of review.** Sending the ticket back to be worked
  withdraws the claim that it is done: any move into a column left of the
  review column clears it, through every route that writes a status
  (`NoCode.ClearOnMove`), with a `no_code_changed` event carrying `from`, a
  null `to` and `moved`, the column's name. A move into review or to its right
  keeps it, and so does a terminal or a deferred column - closing a ticket, or
  shelving it, does not retract what it said. Setting and clearing by hand are
  `no_code_changed` events as well, carrying `{ from, to }`.
- **Withdrawn by an increment's claim.** The declaration is the review
  column's, so a ticket still standing left of review loses it when an
  increment claims the ticket (`TakeClaim`) and again when that claim ends
  (`ReleaseClaim`, on the runner's token release and on the operator's
  tokenless clear alike), through `NoCode.ClearStanding`. The
  `no_code_changed` event carries `from`, a null `to` and `standing`, the
  column's name, and is written before the `claim_taken` or `claim_released`
  event. A ticket at or right of review, or in a terminal or a deferred
  column, is untouched by either, and a refused take or a release that clears
  nothing writes nothing. Without it a stale declaration would reach review
  and fold `ReviewWork.Judge`, hide the ticket from `inReviewWithoutPullRequest`
  and be carried by the close hop - for work the increment had just shown was
  not finished with no code.

**Where it stands.** The declared issue stands in the review column with no
pull request, and the board does not wait for one:

- Not [pre-approved](#pre-approval) and not in a project whose `NoCodeCloses`
  is on, it is held there. `ReviewWork.Judge` folds it first, with *finished
  with no code - a person closes it*, so no review session is spawned for a
  branch that does not exist, and it is not a row in `reviews`. It is not
  counted in `inReviewWithoutPullRequest` either, because having none is what
  it declared. The attention panel lists it, with the reason, under
  **Finished with no code** (`noCodeHeld` - see [what is waiting on
  you](#what-is-waiting-on-you)), and a person closes it.
- [Pre-approved](#pre-approval), or standing in a project whose
  `NoCodeCloses` is on, the **close** hop carries it into the first terminal
  column right of review - the one case that hop takes an issue with no
  children (see [the hop](#the-hop), kind 7), because the declaration stands in
  for the children it would otherwise have needed. A pre-approved row keeps
  its own comment and `status_changed` payload (`preapproved: true`); carried
  by the setting alone (not pre-approved), the comment names the project's
  setting instead and the payload carries `approved: "nocode"` in place of
  `preapproved`. Either way the comment says *finished with no code* and
  gives the reason, the payload carries `nocode: true`, and whatever
  [depends](#dependency) on the ticket is dispatched on the next pass. A
  declared issue that has open children still waits for them, whichever
  carried it - `CheckApprovesMerge` alone never carries a childless one.
- A task under a story or bug that shares one pull request is not [waiting on
  that pull request](#one-pull-request) once it has declared: the wait is for
  code that was pushed, and it has none.
- A project that [lands on the trunk](#landing-on-the-trunk) has no pull request
  to record for its issues either, and a declared one is treated the same: the
  panel lists it and the close hop carries it, and neither reads a landing.
  Nothing is recorded by `hatch landed` for work that changed nothing.

The prompt a runner is given tells a session whose work changed nothing, and
whose destination is review or a column to its right, to declare it with
`hatch pr --none` and to push no empty branch. One bound left of review is
told there is no pull request to open and nothing to declare, and to say what
it found in the comment instead - there is no review column there to declare
into. Either way the issue page shows the declaration and its reason.

### A pull request waiting to be opened

Work can be written and pushed where a pull request cannot be opened. The issue
then carries the pull request itself, so a person or a later runner can find it:
`PUT /api/issues/{key}/waiting-pull-request` with `{ title, body, branch, sha,
remote, why }` stores it in the flat `WaitingPr*` columns of `EfHatchIssue`
(`IssueWaitingPullRequestController`, with the rules beside it in
`WaitingPullRequest`, copying the [no-code declaration](#finished-with-no-code)
end to end). There is one per issue and a second write replaces it. `remote` is
the checkout's origin, the value `hatch landed` sends, because a project can
bind several repositories and whoever opens it has to know which one the branch
is in. The runner is never a field: it is the caller's name, so the trail and
the record cannot disagree.

`hatch pr KEY --title T --body-file F [--repo P]` is what writes it
(`IssueCommands.OpenPullRequestAsync`), and it tries the forge first. The
refusals, all before anything is sent, are a url already recorded, a project
that lands on the trunk, a task on its story's shared branch, a title that is
not `KEY ` and a subject or is over 256 characters, a body over the forge's
limit, a trunk or detached head, and a branch origin does not hold at HEAD after
one fetch (`Workspace.CurrentBranch`, `Workspace.OriginBranchSha`). The body is
the file as written, with the ticket link and a blank line put in front when its
first line is not that link. `IForge.OpenPullRequestAsync` is `gh pr create
--head --base --title --body-file` from a temporary file written without a
byte-order mark; success is the last line of standard output, and a failure
whose standard error says *already exists* answers the url it prints, so a retry
records the existing pull request and leaves none waiting. Any other failure
stores the pull request here, with `why` as gh's first line (a default sentence
where it said none, cut to 500), prints that it is written and waits, and exits
`0`: the prompt tells the session to carry on. A url recorded in between is the
route's `409`, the program's ordinary error exit. The envelope write takes the
checkout `--repo` names, not the working directory.

It is not a pull request. It does not count as having one (`HasPr` is unchanged,
so it blocks nobody), a conflict on its branch is still dispatched from the
merge checks, and it is neither a `reviews` row, since those need a recorded
url, nor part of `inReviewWithoutPullRequest`. `IssueDto.waitingPullRequest`
carries it; the board card does not. Recording a pull request url by any route
clears it in the same save - `PATCH` and the contribution route both call
`WaitingPullRequest.ClearOnRecord` beside `NoCode.SetAside` - with a
`waiting_pull_request_changed` event carrying the url as `pullRequest`. A move
to a column left of review leaves it in place. The issue page draws a `pull
request waiting` chip, and its Pull request field says *Written, waiting to be
opened* with the runner, the reason and the time, and the title and body behind
a `<details>`; there is nothing on the page to edit or delete it.

It rides on the review read as `ReviewCheckDto.WaitingPullRequest`
(`WorkController.GetReview`, the same `WaitingPullRequest.ToDto` the issue
carries), which is how the project's driver sees these rows at all; the queue
words such a row's sentence the way the merges column's head has it - naming
the driver that opens it, or saying no live runner is eligible to drive the
project (`Dispatch.WaitingToBeOpened`) - and a waiting row counts as having a
pull request for the judge's dead-end test, since something is about to open
it. The driver opens it in the merge train's tick - see *A waiting pull request is opened by the
driver* under [the merge train](#the-merge-train).

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

Read at `GET /api/board`'s `wip` block, and printed as one line per
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
call and not an agent's: a key sending `wipOverride: true`
is refused with `403`, whatever the load and even where no limit is set. On
the board and on the issue page, the browser never sends that flag on its own
first guess: the `409` raises a dialog quoting the sentence above and asking
*move anyway?*, and only a press of *Move anyway* resends the same request
with `wipOverride: true` - the event that follows names who pressed it.

#### An epic's own limit

**Held beside the section-wide slice above, not instead of it.** After the
section-wide check has had its say, a story or a bug moving from outside the
section to inside it, whose parent is an epic, is further held to that epic's
own [`WipLimit`](#stories-at-once) - null reading as **no limit**, in
which case it holds nothing. A task, an epic moving itself, a move within
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
separate question. **Off by default**: an epic with no `WipLimit` set
holds none of its stories back, and runs as many at once as dependencies, the
section-wide slice and claims allow. An epic with a number set is held to it
whether or not the section-wide slice has a limit row of its own.

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
under it. It holds a table row only: a repo-sourced file row can be named on a
take or a heartbeat too (by the negative id `GET work/{key}` hands back on
`Playbook.Id`), but that id is transient and is never stored here - a file
row's window is carried by `ClaimQuietLimitSeconds` alone.

**A claim is a lease held by a running dispatcher**: taken before an increment,
refreshed while it runs, released after it, and expiring on its own when the
runner dies. It is what makes two checkouts against one Hatch safe, which is the
whole of why it exists. Columns rather than a table because a claim is a fact
about the issue with at most one of it at a time, and because taking one is then
a single conditional `UPDATE` against a row the dispatcher is already reading.

**A session the board has paused takes no new claim.** `POST …/claim` looks up
the calling key's live session at the checkout `request.Runner` names, and where
that session's `AgentDownKind` is `board-paused` and `AgentDownUntil` is ahead of
now it answers `409` — the board paused this runner, until when, the session's
`AgentDownLine`, and that a person can lift it on the Runners page. It runs
before the live-claim read, takes no claim and writes no event. A claim the
session already holds is not touched: the heartbeat and the release routes do not
look at the pause. A session of the same key at another checkout, a call with no
key, and a pause the runner set itself (`not-starting`, `signed-out`) are not
refused. `GET /api/work/next` is left alone: it is sent no checkout, so it cannot
tell two sessions under one key apart; the loop's own heartbeat answer is what
keeps it from asking.

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
`Hatch:StallLapseMinutes`, five minutes by default, or against the claim's own
`ClaimQuietLimitSeconds` where it carries one — set with the lease, from the
playbook the take, or a heartbeat that attaches one, names: the server resolves
the id (a table row, or a repo-sourced file row by its transient id) and copies
that playbook's `QuietLimitSeconds`, so a playbook with none writes null, the
global. A heartbeat naming no playbook leaves the window alone. The value is
copied, not joined: editing or deleting the playbook later does not move a
running claim's window. A null or
non-positive column is exactly as if there were no column at all, falling back
to the global; a positive one is what every reader judges the claim against
instead, in place of the global, even where the global is `0`.
A `--quiet` session sends no chatter at all, so its claim is measured from when
it was taken. A line the session prints counts as a word every time it is
printed, even when its text is the same as the last one's - the CLI counts the
prints, not the text, since a long line is clipped and a run that keeps making
the same kind of call would otherwise read as silent. While a streamed session
has a tool call open the stream is silent, so the runner itself carries the
word for it: `still running: <the call as the transcript names it>, <n>m`, at
least once a minute (and often enough to fit a window shorter than three
minutes), so a single blocking call of any length within the agent's own tool
timeout keeps its claim live and no session needs to poll. It names the call
and its minutes only, never its output. It stops the moment the call returns,
is never sent between calls (thinking, writing, or no call at all), and ends
once the call has been open past the agent's own tool timeout (ten minutes),
so a hung call goes quiet one window after the last line it carried - unless
the claim carries a run cap (below), in which case it carries on past the
timeout until the cap itself stops the run; with no cap, it is exactly as
before. A streamed
session whose turn has ended while its process runs on - a background command
or monitor, with the session woken when it finishes - is waiting, and the
runner carries the wait the way it carries a call: `waiting on background work,
<n>m`, by the same rules (at least once a minute, ended past the agent's tool
timeout unless the claim carries a run cap, and never sent while it has said
something else lately). A new turn clears it, and an open call wins over a
wait. A dead
runner sends nothing, and its claim lapses on this rule unchanged; a `--quiet`
session has no stream to see a call open or a turn end in, so it carries none of this. Every reader treats a quiet claim exactly as it treats an expired
one — the dispatcher, the board, the issue page, the Runners page, and a take —
because `IssueClaims.IsLive` is the one place both conditions are asked, and
nothing downstream repeats the arithmetic. A heartbeat against a quiet claim is
refused `409` with `this claim has gone quiet for 5 minutes` - the claim's own
window, in whole minutes, so `25 minutes` for 1500 seconds, `1 minute` for 60,
and `90 seconds` where it is not a whole number of them - beside the reasons a
heartbeat could already be refused, and checked before "expired" where a claim
happens to qualify as both. The `409` answers `{ error,
reason }` - `error` the sentence it always printed, `reason` one of
`quiet`, `run-time` (past its cap, below), `expired`, `cleared` (no token on the
claim) or `taken` (somebody else's) - so a client can act on which without
parsing prose; a take's own `409`s are unchanged, bare strings. A release may
say `quiet`, `run-time` or `error` the same way: `DELETE
…/claim?token=…&outcome=…&reason=quiet` is read only alongside both a
token and an outcome, and rides into the `claim_released` payload beside
them; any other reason is `400`, naming both. `Hatch:StallLapseMinutes = 0` turns
this off entirely for a claim with no window of its own — such a claim is then
judged on its TTL alone — and a
negative value falls back to the default, the guard `Hatch:ClaimTtlSeconds`
already has. The window rides back with the take as `stallLapseSeconds`, in
seconds, `0` when lapsing is off, and on every claim a read carries - the board
card, the issue, the search - as that claim's own window rather than the
board's. The server is what honours it, so the server is what says what it is,
the same argument `TtlSeconds` makes for itself.

**A claim past its own cap is not live either, however recently it spoke.** A
claim carries `ClaimMaxRunSeconds`, a hard ceiling on a run judged from
`ClaimedAt` alone: neither a heartbeat nor a line of chatter moves it. Exactly at
`ClaimedAt` plus the cap the claim is live; one tick past, it is not, and
`IssueClaims.IsLive`, the live and takeable predicates every query shares, and a
take all agree - a take over it succeeds. A null or non-positive column is no
cap. The column is written the way the quiet window is: a take, or a heartbeat
that attaches a playbook, copies that playbook's `MaxRunSeconds` onto the claim
- a table row by id, a repo-sourced file row by its transient id with no id
stored - so a playbook with no cap writes null, a heartbeat naming none leaves
the cap alone, and a takeover never inherits the lapsed lease's. The take
answers it as `maxRunSeconds`, in seconds, `0` for none. A heartbeat from the
holder's own token past the cap is refused `409` with `this claim ran past its
60-minute limit` (`90-second` where the cap is not a whole number of minutes),
reason `run-time`, and checked before "quiet" where a claim is both: no amount
of chatter would have saved it. A release by the holder's own token is not
refused for it - it clears, as it does a quiet claim - and may name `run-time`
as its reason. `hatch`'s own runner races this the same way it races the quiet
window: it times the cap itself from the take, with no margin, and stops the
session a beat before a heartbeat could ever be refused this way - see
[The claim](../src/Hatch.Web/apps/hatch/docs/one-increment-and-the-loop.md#the-claim).

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
an optional `outcome`, `dropped`, `worked`, `preempted`, `recused`, `unservable`, `abandoned` or `unstarted` — absent
is accepted exactly as it always has been, which covers the pick's own
release, a restart, and an older CLI, and a value that is none of the seven is
`400`. It is read only where a token is given: the operator's tokenless
clobber names no increment to have an outcome. The `claim_released` event
carries it, and it is what `letGo` counts (see [Comment, question and
answer](#comment-question-and-answer)) — except `preempted`, which it skips
over, neither counted nor resetting the count: the board itself ordered that
put-down, not a choice the increment made, and the runner's own account of
what it pushed lands as its own comment, not as a dropped increment.
`recused` is skipped the other way: it *stops* the count exactly as `worked`
does, because the session's own `hatch recuse` is the decision, and the
omission — not the let-go count — is what folds the ticket; a recusal never
raises the let-go fold on its own. `unservable` means the runner's model could not be spawned; it is skipped over exactly like `preempted` (neither counted nor resetting the let-go count), because the increment neither stalled nor did work. `abandoned` means the loop that ran the increment died - a crash, a closed window - and the next loop in that checkout ended the session it left and released the claim for it (see *A loop that died* under [What a runner does, in order](#what-a-runner-does-in-order)); it is skipped over exactly like `unservable`, because no increment chose to end there. `unstarted` means the session's agent never started on this runner - it printed nothing before the process ended - so the ticket was never touched; it is skipped over exactly like `unservable`, neither counted nor stopped at, because there was no increment to let go.

**A `dropped` release whose claim spent nothing carries a mark the board added.**
"Spent nothing" is one rule, in `SpentNothing`, and the stall route asks it too
(see [the stall mark](#comment-question-and-answer)): it is true when at least
one work-log row reached the board since the claim was taken, and every such row
has zero tokens (all four counts) and `isError` true. A row is placed by when the
board first wrote it, not by the runner's clock, so an old session's row is never
pulled into a new claim, and a claim with two rows spent nothing only when both
did. A claim with no row at all is judged as it always was. The release is
written with the outcome the runner sent, `dropped`, and the payload gains
`spentNothing: true`; the runner's own comment is neither edited nor removed.
`letGo` neither counts a marked release nor stops at it, and the dropped-by
record steps over it, so the runner is not named as having let the ticket go.
The mark is added for `dropped` only: every other outcome is written as before.
The cause is the runner's, not the ticket's - a session that never got as far as
reading it - so it is not held against the ticket.

**A release that gave an outcome also names the runner.** The event's `from`
reads `"<who> on <runner>"`, which cannot be split reliably, so the
`claim_released` payload gains `runner` — the claim's runner name — beside
`outcome`, and only when an outcome was given: a release with none writes
exactly what it always has, and an older release has no `runner`. It is what
lets the dispatcher tell which runner let a ticket go last (see [A marked issue
folds the dispatcher](#the-dispatcher)): while a `dropped` release naming a
runner is the issue's newest release, and nothing newer is a release of any
other outcome (or none), a `status_changed`, a `resumed` event, or an `answered`
event a person wrote — claim takes, lapses, holds, clears and comments are
stepped over — that
runner is the one that let it go.

**A release may also name the session it ran as**, with `sessionId`. Where it
names a work-log row of this issue and an outcome was given, that row's
`outcome` and `reason` are stamped in the same save as the `claim_released`
event, and the payload gains `sessionId` beside `outcome` and `reason`, present
only then. A `sessionId` naming no row, or given with no outcome, stamps
nothing and is never refused for it: the row the CLI posts before it releases
is what the stamp looks for, and a mismatch is not a failure.

**A release may also name the runner failure the increment ended on**, with
`failure` (a kind from `RunnerFailureKinds.All`) and `failureWhy` (what the tool
said). Both are read only with a token and an outcome, as `reason` is. A kind the
board does not know is dropped without a `400`: the release goes through and
writes no failure. With a failure, the `claim_released` payload gains `failure`
and `failureWhy` (cut to the failure row's own length), beside `outcome`,
`reason` and `sessionId`, which are untouched, and so is the let-go count. Where
the releasing key has an open failure row of that kind at the
claim's checkout, the board also links the row to the issue, once per row and
issue, in the same save; with no open row the event is written alone. The loop
sends a kind when an increment ended on a signed-out agent (`agent-signed-out`),
an agent that never started (`agent-not-starting`), a ticket let go for want of a
signer (`signing`), or a pull request this increment left waiting (`forge`). The
failure rows then carry `tickets`: the keys of the issues linked, newest first, at
most twenty, on `current` and `earlier` alike.

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
`DependencyFilePath?`, `DependencyFileContent?`, `CheckedAt`, `Runner`,
`CheckedBy`. What a runner found when it merged an issue's branch against the
trunk, and the two refs it was looking at when it did. `Verdict` is one of
four:

| Verdict | Means |
|---|---|
| `clean` | The branch merges with the trunk. Nothing for an agent to do |
| `conflicted` | It does not, in the paths `Files` names |
| `none` | No unmerged branch on origin is named for the issue - or, for an issue whose pull request's head branch is known, that branch is not on origin or is merged |
| `ambiguous` | Two or more are named for the issue, and nothing here guesses which is its. Only for an issue with no pull request's head branch known yet: once there is one, it is the only branch looked at, and how many others are named for the key is never counted |

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

`DependencyFilePath` is the same question asked of `.hatch/dependencies.json`
instead - unlike `PlaybookFilePath`, nothing on the board tells the runner
where to look: the path is `DependencyDeclaration.Path`, a constant the
runner already knows, so `Check` asks the diff about it on every repository,
whatever the project reads as its own. `DependencyFileContent` rides beside
it only when the path changed: the branch's own blob there, as text, read
with `ReadBlob` against the branch's own ref rather than the diff - null
when the blob could not be read, is not valid UTF-8, or is over 16 KB, so a
file too large to carry still holds without the board trying to parse a
fragment of it. The board parses what arrives with `DependencyDeclaration.Parse`;
an unparseable file still holds, and names nothing further.

**One verdict per issue per repository**, unique on `(IssueId, Canonical)`, where
`Canonical` is [`RemoteIdentity.Canonical`](#repository) of the remote as the
runner spelled it. A project may bind several repositories and a branch is
entered in every checkout that has one, so a single verdict on the issue would
let a clean repository overwrite a conflicted one — and two runners that spell
one repository two ways still write one row. An issue conflicts if any of its
verdicts does. A second write for the same pair replaces the first; a verdict
for a project that binds nothing is keyed on the remote the runner spelled, like
any other.

`PUT /api/issues/{key}/merge-check` writes it, and a key may: the caller is
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

`PUT /api/issues/{key}/landing` writes it, and a key may, for the reason
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

`PUT /api/issues/{key}/build-check` writes it, and a key may, for the
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

`PUT /api/trunk-builds` writes a verdict and a key may, for the reason it
may write a build check — the caller is a runner, and a verdict only a person
could enter would be one nobody entered. It takes the same refusals the build
check's write does (an uncanonicalisable remote, an unknown verdict, no trunk,
no sha, `failed` with no failing checks, an over-long or broken check name, an
over-limit runner), naming the trunk rather than the branch. **No event, and no
question**: there is no issue to write either on, and a trunk's failure is a
person's row in the attention panel rather than a stall a session is asked
about. `GET /api/trunk-builds` answers every stored verdict, ordered by
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
by a commit to the file. A row's optional keys are `model`, `effort`, `shape`,
`types`, `budget`, `briefLimit`, `quietLimitSeconds`, `maxRunSeconds`,
`localModel`, `deniedTools` and `isCheck`; `quietLimitSeconds` and `maxRunSeconds` each
follow the same rule as `briefLimit` — a whole number of one or more, in
seconds — and a value that is not one makes the file `invalid`, with the line
the value is on. `maxRunSeconds` is stored and read back, and a [claim](#claim) copies it as its run cap. `localModel` must be an Ollama `name:tag` id; an alias or another agent's id makes the file `invalid` the same way.
A parsed row's `unreachableReason` is computed on read from the live columns
and never persisted, so reordering columns changes it with no new `PUT`;
`Rows` JSON is unchanged.

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

`PUT /api/playbook-files` writes a row and a key may, for the reason it
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
asked about. `GET /api/playbook-files` answers every stored row,
ordered by canonical then trunk, with the path itself (`.hatch/playbooks.yaml`)
alongside them — what the Playbooks page reads once a load, and what a
runner's poll reads once a poll to know whether it has already put this sha.

The runner's poll ([below](#checking-the-branches-in-review)) reads the file
off every checkout's trunk that has an origin, whether or not anything of its
is in review and whether or not any project reads it, so a quiet board
still has a current copy on the page.

### Dependency file

`EfHatchDependencyFile` — `Remote`, `Canonical`, `Trunk`, `Sha`, `State`, `Hash`,
`Entries` (`jsonb`), `Problem`, `LastGoodSha`, `LastGoodHash`, `Url`, `CheckedAt`,
`Runner`, `CheckedBy`. What a repository's trunk holds at
`.hatch/dependencies.json`, as of the last time a runner's poll checked — the
entries the file parsed to, or the one sentence saying why it did not. Like a
[playbook file](#playbook-file), a dependencies file is a fact about a
repository's trunk and nobody's issue, so it is not keyed on one. An entry is
an id — one of the dependencies Hatch knows, from `Dependencies.Default` — and
an optional `version`, a string of one to three dotted whole numbers. A
version is a minimum, met by that version and anything newer. The
operator-facing form is [What a repository
needs](../src/Hatch.Web/apps/hatch/docs/the-runner.md#what-a-repository-needs).

**One row per repository per trunk**, unique on `(Canonical, Trunk)`, the same
reason a playbook file's own key is: two projects may bind one repository with
different base branches, and a single row would let one project's read
overwrite the other's.

`State` is one of `parsed`, `invalid` or `absent`. `parsed` means the bytes at
the path were valid UTF-8, valid JSON, and every entry in them validated
against `DependencyDeclaration.Parse` — an id Hatch knows, declared once, with
a version in the shape it expects — `Entries` holds what they resolved to.
`invalid` means the bytes exist but failed one of those checks — a bad shape,
a duplicate id, an id Hatch does not know, a malformed version — and `Problem`
carries the one sentence saying which, with the entry's position where the
parser found one. `absent` means the trunk has no file at that path at all.
`Hash`, the file's SHA-256 in lower hex, is set whenever the bytes were read
at all — `parsed` or `invalid` — and only null for `absent`; `Entries` is set
only for `parsed`, and `Problem` only for `invalid`.

`LastGoodSha` and `LastGoodHash` remember the most recent sha and hash that
parsed, so an `invalid` row's card can still say what to roll back to. A write
that parses clears both — the row is its own good copy now. A write that
fails to parse, immediately following one that parsed, copies that superseded
sha and hash into them before the row is overwritten; a second `invalid`
write in a row, with no parse between, leaves them exactly as they were. A
write that finds the file `absent` leaves them untouched too — whatever was
last remembered good stays remembered until a parse clears it or a fresh
transition out of `parsed` updates it.

`PUT /api/dependency-files` writes a row and a key may, for the reason
it may write a playbook file — the caller is a runner, reporting what it
read, not proposing it. `{ remote, trunk, sha, content?, url?, runner }`,
`content` the blob's exact bytes, base64-encoded, and absent for a trunk with
nothing at the path. The route hashes and parses the content itself rather
than trusting a client-sent verdict, and refuses in the same shape the
playbook file's write does — an uncanonicalisable remote, an empty or
over-long trunk, sha or runner — plus content that is not valid base64 or
over the parser's own size limit; an unsafe or over-long `url` is not
refused, it is simply stored as null. **No event, and no question**: there
is no issue to write either on, and a bad dependencies file is this row's
own state, not a stall a session is asked about. `GET /api/dependency-files`
answers every stored row, ordered by canonical then trunk, with the path
itself (`.hatch/dependencies.json`) alongside them, each entry's id resolved
to its current label.

### Changelog file

`EfHatchChangelogFile` — `Remote`, `Canonical`, `Trunk`, `Sha`, `State`, `Hash`,
`Behind`, `Problem`, `Url`, `CheckedAt`, `Runner`, `CheckedBy`. What a
repository's trunk holds at `.hatch/changelog.yml`, as of the last time a
runner's poll checked, judged against [the changelog file](#the-changelog-file)
the board would write itself. Like a [dependency file](#dependency-file), it is
a fact about a repository's trunk and nobody's issue, so it is keyed on
`(Canonical, Trunk)`, unique: one row per repository per trunk.

`State` is one of:

- `current` — the bytes are exactly the board's own.
- `behind` — a file Hatch wrote, parsed with the same parser the projection is,
  and compared entry by entry on `key`, `url`, `project`, `type`, `title`,
  `note`, `shipped` and `beneath`. `Behind` is the count of entries the board
  has that the file lacks, plus entries both have that differ, plus entries the
  file has that the board no longer lists (an issue reopened). Bytes that
  differ but count none (order or whitespace only) are `Behind` 1, so a row
  never reads "behind by 0".
- `absent` — the trunk has nothing at the path.
- `foreign` — the bytes exist but are not a file Hatch wrote: not valid UTF-8,
  a first line that does not open with `Changelog.HeaderPrefix`, YAML that does
  not parse, a root that is not a mapping with `version: 1` and an `entries`
  list, or an entry that is not a mapping with a `key`, or repeats one.
  `Problem` is one sentence naming the sha: `.hatch/changelog.yml at <sha> is
  not a file Hatch wrote (<why>) - move or remove it before Hatch will write
  there`. The Projects page lists it as the repository's own problem to put
  right.

`Hash`, the SHA-256 in lower hex, is set whenever bytes were read; `Behind`
only for `behind`; `Problem` only for `foreign`.

`PUT /api/changelog-files` takes `{ remote, trunk, sha, content?, url?, runner }`
and a key may call it for the reason it may put a playbook file. `content` is
the blob's exact bytes, base64-encoded, absent for a trunk with nothing at the
path. The server compares them with its own projection (`ChangelogFiles.ProjectAsync`,
the same read `GET /api/changelog/file` renders, with the same origin rule) and
stores the verdict; it trusts none from its caller. It takes the dependency
file's refusals, content over 2 MB, and a `404` for a canonical no project
elects. **No event, no question.** `GET /api/changelog-files` answers every row
with the path, `.hatch/changelog.yml`, and the canonicals currently elected; a
runner sending its checkouts' remotes as `remote` also gets, in
`ElectedRemotes`, the ones that are elected, since a runner canonicalises
nothing. **Origin caveat:** the origin is inside every `url:` line, so a Hatch
with no `PublicBaseUrl`, whose runner reaches it by a different address than
the session that wrote the file, reads `behind`. Setting `PublicBaseUrl` is the
fix.

The runner's poll reads the file off the trunk of every checkout whose
repository is elected, once per sha; see [how the poll heals a
gap](#the-changelog-task).

### Comment, question and answer

`EfHatchComment` — `IssueId`, `Author`, `Body` (markdown), `Kind`, `AnswersId`,
`Options`, `Level`, `DeliveredAt`, `DeliveredTo`, `CreatedAt`.

`Level` is how far work may go before a question is answered: `hold`, `build`
or `merge`, strictest first. It is null on everything that is not a question and
on a question asked before the column existed, and both read as `hold`. A person
sets it with `PUT /issues/{key}/questions/{id}/level` - never a key, and
`POST /issues/{key}/comments` refuses one. Above `hold` it needs a recommended
option, and an answered question cannot change. A change writes a
`question_level_changed` event, and setting the level already held writes nothing.

`Questions.WaitingLevelsAsync` reads the levels for the dispatcher: one row per
issue with a [waiting](#comment-question-and-answer) question, holding the
strictest `Level` among its rows (`QuestionLevels.Strictest`, a null reading as
`hold`) and the count of questions at that level alone, so *1 question at build*
never counts a `merge` one. `PriorityTree.ForAsync` always loads it, with the
query filters off like the rest of the tree, and `PriorityTree.EffectiveQuestion`
walks the issue and its parents for the strictest, nearer on a tie, with the key
it was found on - null for the issue's own. A tree built without the read would
honour no question, so it is not opt-in.

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

**Waiting on somebody is open, and either outside a terminal column or asked
at `merge`.** A `hold` or `build` question on a ticket that has shipped is
never going to get an answer that matters, so the moment its issue lands in
a terminal column it stops being counted, badged or listed anywhere a person
is asked to act — though it stays open, and stays on the record, exactly as
it always did. A `merge` question is the exception: the work was built and
merged without waiting for it, so it is still owed a look back, and it stays
waiting after the issue closes until somebody answers it.
[`Questions.cs`](../src/Hatch.Api/Modules/Hatch/Questions.cs) holds `Open`,
`Waiting` and `Holding` side by side. `Holding` is the narrow one - open and
outside a terminal column - and is what the dispatcher's level read and the
build-past list use, so a closed ancestor's look-back question never holds
the work beneath it. Most of what used to read `Open` reads `Waiting` now: the attention panel, the board's badge, the Plan
page's meters, and the two routes a person actually answers from. One
caller keeps reading `Open` on purpose, because what it is deciding does
not change once a ticket has shipped: [build check](#build-check)'s rule
four, which refuses to mark the issue stalled again while an earlier
question is still open, whatever column it's in.

Two fields on the question are derived when it is read, never stored.
`QuestionDto.LookBack` is true for an open `merge` question whose issue has
reached a terminal column, which is also where a merged pull request leaves
it. `CommentDto.AnsweredAfter` is set on the answers inside a question, and is
true where the answer is later than the issue's `ClosedAt`. A reopened issue
has no `ClosedAt`, so both read false.

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

**A claim that spent nothing is not stall-marked.** Where the session a runner
ran on the ticket spent no tokens and ended in error (the rule is under
[Claim](#claim), "A `dropped` release whose claim spent nothing"), the board
declines the mark: `StalledAt` and `StalledWhy` stay exactly as they were, the
route still answers `200` with the issue, and the history gets one
`stall_declined` event naming who sent it, the sentence they sent, and
`because: spent_nothing`. The ticket is picked up again at once; the cause is
the runner's, not the ticket's. A claim with no work-log row, a row that spent
tokens, a row that is not an error, or a stall sent with no claim held is marked
as always. Counting such sessions toward a pause is not done here.

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
always leaves a clean mark behind it, and starts `letGo` over from zero.

**A ticket a runner let go of is left to a different live runner.** When the
caller names itself (`?runner=<name>` on `work/next` and `work/queue`; `asRunner`
already names it on `queue`, and wins), a ticket whose newest claim outcome is
that runner's own `dropped` release is folded for that runner, and only that
runner, for as long as some other runner could take it: another row that is
here (seen within the runners' gone-after window), `loop`, `running`, not
exhausted, not past its `until`, not at its `maxRuns`, and whose own declaration
— repositories, clones, standing, tags, projects, dangerous, `under`, and `mine`
with its owner — makes a scan on it find the issue clear, or parked only behind
higher-ranking work. A peer is a key's beat row or one of its live loop
sessions, each judged on its own — a key with both is two peers, never one —
and a caller is told apart from its own sibling sessions by its key name alone,
since that is the only name the loop ever sends as `runner`. The sentence
names the runner and how long ago, `blockedKind`
is `other`, and the row parks nothing below it. There is no time window: the
rule ends when another runner has been through, when the record is cleared (a
`worked` or other release, a status change or a person's answer), or when no
peer is left — an installation with one runner behaves exactly as it did.
`Hatch:StallResumeLimit` still holds the ticket for a person. The cost is
accepted: a capable but busy peer leaves the ticket waiting while the dropper
idles.

**A row can be left for a runner marked preferred.** When the caller names itself
the same way, a clear row is folded for it while a runner marked
[preferred](#runners-on-the-board) is free to take it: `HA-12 in In Progress is
left for build-box, which is preferred and free`, kind `other`. The peer is the
let-go deferral's own: a preferred key's beat or live session that is idle - no
live claim at its checkout, and not pulling a model - and whose own scan finds
the row clear, or parked only behind higher-ranking work, so a tag, an
omission, a refused model or a column rule of the preferred runner's own lets
the row through. A preferred runner is never folded by it, and a peer's own scan
asks nobody, so two preferred runners each read the row clear. It is said last,
after every fold that is the asker's own, the let-go one included, and never on
a [hop](#the-hop). An installation with no runner marked preferred behaves
exactly as it did.

**A row clear only because its resume window has passed names it.** `hatch
queue` prints it in place of the bare arrow, e.g. `clear for To Do -> In
Progress - it stalled 18 minutes ago and resumes itself after 15 minutes`
(`clearNote` on `QueueEntryDto`) — the same way it names a row clear only
because of economy's or low's own pace.

**`letGo` counts a ticket's trailing dropped increments, and is what the stall
mark's resume limit is measured against.** A runner's release
may say how the increment ended — `DELETE …/claim?token=…&outcome=dropped`,
`&outcome=worked` or `&outcome=recused` (see [Claim](#claim)) — and `letGo` on
`work/{key}`, `work/next` and a queue row is this issue's releases, newest
first, whose outcome was `dropped`, counted back to the first of: a release
whose outcome was `worked` or `recused`, a `status_changed`, a `resumed` event
(a person's *Resume now*), or an `answered` event written by a person. A
`held` event is stepped over: holding is not resuming.
A release with no outcome is skipped over — it neither counts nor stops the
count, which is what keeps an older CLI's releases, a restart, and the pick's
own release from reading as a string of drops. So is a `dropped` release marked
`spentNothing` (see [Claim](#claim)): the session never got as far as the
ticket, so three such releases leave `letGo` at `0` and the ticket is never held
for a person on their account. `0` on a client too old to
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
weight A, `Description` weight B, `PreviousTitles` also weight B),
`EfHatchComment.SearchVector` (`Body`, weight C, every kind of comment —
note, question, answer, message, verdict), `EfHatchWorkLogEntry.SearchVector`
(`Title` and `Summary`, both weight D). See
[`SearchQuery.cs`](../src/Hatch.Api/Modules/Hatch/SearchQuery.cs) and
[`SearchController.cs`](../src/Hatch.Api/Modules/Hatch/SearchController.cs).

**An issue is found by words from a title it used to have, not only its
current one.** `EfHatchIssue.PreviousTitles` holds every title this issue was
retitled away from, oldest-replaced first, one per line, never the current
title and never the same title twice: `EfHatchIssue.Retitle`, the one place
`Title` changes, drops a line equal to the new title and appends the old one
unless it is already there, so retitling A to B and back to A leaves only B.
A migration backfills it from each issue's own `retitled` events, replaying
the same rule. `PreviousTitlesNewestFirst()` reads it the other way round.

**A hit is counted once per issue**, however many of its comments or work-log
rows matched — the match is an `EXISTS`, not a join.

**Ranking with `q` is `ts_rank` over the issue's own vector only**: a comment
or work-log match decides whether an issue is found, not where it sorts.
Ties: closed ahead of open, then newest-closed, then newest-created. Without
`q` — or with one that reduces to no terms, where `SearchQuery.ToTsQuery`
returns null — the list is newest first by closed, or created where open, so
a column's issues come most-recently-closed first.

**`matched` on a hit** lists which of `title`, `previous-title`,
`description`, `comment`, `work-log` found it, in that order. A hit also
carries `previousTitles`, the issue's own previous titles newest first,
whether or not `previous-title` is among what matched - empty on an issue
never retitled.

**Event payloads are deliberately not indexed**: they are superseded copies
of text already indexed elsewhere, and indexing the JSON wholesale would
surface every previous description, and every retitle's wording twice over,
as a match on an issue that no longer reads that way. Previous titles are
the one exception, each held once, outside the payload, in
`PreviousTitles` above; a previous description is not searched at all.

**Done issues are in the result by default**; `statusId` narrows to one
column, and none of the board's done-horizon folding applies.

### Dream

`EfHatchDream` — `Body`, `Author`, `Source`, `ProjectId`, `CreatedAt`,
`UpdatedAt`. A half-thought, caught before it is shaped enough to be an
issue.

- **A dream is its own table, not an issue** — an issue needs a project, a
  key, a column and a card; a hundred half-thoughts as cards would bury the
  board.
- **No event trail on a dream** — a dream is a note; `UpdatedAt` is its
  whole history. The trail begins once a dream becomes work (HA-1722's
  `dreams_woven` issue event).
- **A thread is a link row, not a note.** `EfHatchDreamThread` —
  `DreamId`, `IssueId`, `Actor`, `At`, composite-keyed on the pair, each a
  cascade-delete FK: a deleted dream or a deleted issue takes its own
  threads with it, never the other side's data. Written by whoever did the
  reading — a breakdown session, a person on the issue page later — and
  never inferred from text; see `PUT /issues/{key}/dreams` under Issue.
- **A remand is a link row too.** `EfHatchDreamRemand` — `DreamId`,
  `IssueId`, `Actor`, `At`, `Root`, composite-keyed on the pair, each a
  cascade-delete FK like a thread's: deleting the dream removes its rows and
  moves no issue, and deleting an issue removes its row and leaves the dream.
  It is its own table rather than a flag on the thread because the two say
  different things — a thread is what an issue *drew on*, a remand is what an
  issue was *shelved into* — and one dream can be both for the same issue.
- **`POST /issues/{key}/remand` writes a remand.** It takes a deferred column
  and the descendants to shelve, posts one dream from the root (its title, a
  blank line, its description, cut to a comment's length; the root's key as
  `Source`), links the dream to the root (`Root` set) and to each descendant
  that moves, and moves them the way a move does — `status_changed`,
  `ClosedAt`, the no-code declaration, the bottom of the column and the
  [deferral note](#deferred-on-purpose) — writing a `remanded` event beside each. A
  descendant already in a terminal or a deferred column stays where it is. A
  root already in the column is still remanded. The dream's id is in each
  event's payload, so the dream is saved first and everything else second,
  inside one transaction: a second save that fails takes the dream with it.
  `GET /dreams?remandedFromKey=` reads the dreams remanded from an issue, and
  `DreamDto.RemandedFrom` reads the issues a dream was remanded from.
- `ProjectId` is a lean, not a home: `SetNull` on delete, not `Restrict`.

### Capability

`EfHatchCapability` — `Id`, `IssueId`, `Position`, `Statement`, `HowToTell`,
`CreatedBy`, `CreatedAt`, `UpdatedAt`. One thing an [ambition](#issue) says
has to be true, and the line that says how anyone would tell.

- **A row, not text in the description** — for the reason a question is a
  row and not text in a comment (see [Comment, question and
  answer](#comment-question-and-answer)): it can be counted, and it can
  carry a verdict — see [CapabilityVerdict](#capabilityverdict), below.
- **Only an ambition has them.** Every route answers `400` on an issue of any
  other type, with a sentence naming it. `IssueId` is a cascade-delete FK:
  deleting the ambition takes its capabilities with it. A retype that would
  stop an ambition being one is refused while it holds any, counting them.
- **`Position` runs 1 to n within one ambition, with no gaps.** The
  controller holds that, not the database: a unique index is checked per
  statement in Postgres, not at commit, so a renumber inside one
  `SaveChangesAsync` would trip it. An add at a position shifts the rows from
  there up by one, a removal shifts the rows above it down by one, and a
  reorder renumbers from the list it was given.
- **`Statement` and `HowToTell` are one line each**, a title's bound (300
  characters), and neither may be blank. `CapabilityDto` is the row as it is.
- **A write that changes nothing writes nothing** — no event, no
  `UpdatedAt`. Each write that does change something writes one issue event
  on the ambition: `capability_added`, `capability_edited`,
  `capability_moved` or `capability_removed`.
- **A key reads anywhere and writes only left of the implementation
  column.** Once the ambition is in the [implementation
  column](#status), past it, in a terminal column or in a deferred one, a key
  gets `403` on every write and a person is never refused. What an ambition
  asks for is settled before the work starts; changing it after is a
  person's call.
- **An epic serves capabilities through `CapabilityLinks`.**
  `EfHatchCapabilityLink` — `CapabilityId`, `IssueId`, `Actor`, `At`,
  composite-keyed on the pair, each a cascade-delete FK, the shape of a
  [dream thread](#dream): deleting the epic or the capability removes the
  link alone and leaves the other side standing. `PUT /issues/{key}/serves`
  replaces an epic's whole set; every id must belong to the one ambition the
  epic hangs beneath, at any depth - `LegalParentTypes` lets at most one
  ambition stand anywhere above an epic, reached through any number of
  intervening epics and reflections - else `400`, naming the capability and
  the ambition it belongs to. A reparent that would leave an epic (or one
  below it) serving a capability of an ambition it no longer hangs beneath is
  refused, naming the capability. `GET` reads from any issue, and
  `CapabilityDto.ServedBy` reads from the ambition: the keys of every epic
  linked, in key order. A key may write in any column - a link says which
  work serves which line, not what the line asks for.

### CapabilityVerdict

`EfHatchCapabilityVerdict` — `Id`, `CapabilityId`, `Verdict`, `LookedAt`,
`Sha`, `Author`, `CreatedAt`. A session's verdict on one
[capability](#capability): whether it holds, as of a sha.

Not `EfHatchVerdict`: that one is a check session's pass or fail against a
live claim (see [Check](#check)), and the merge train reads it. The two are
named, routed and evented differently on purpose so neither is ever read as
the other.

- **Append-only, newest first.** `POST
  /issues/{key}/capabilities/{id}/verdicts` writes one; `Verdict` is one of
  `holds`, `does_not_hold` and `cannot_tell` — any other word is `400`,
  naming the three. `LookedAt` and `Sha` may not be blank, and are bounded
  (500 and 64 characters); over either is `400`, naming the bound. `GET` on
  the same path answers them newest first by `Id`.
- **Never gated on the column.** Unlike the rest of a capability's writes, a
  key may record a verdict in any column, including past the implementation
  line, in a terminal column, or deferred: settling what an ambition asks for
  is a person's call, but checking whether it holds is not.
- **A capability's state is derived, never stored.** It is its newest
  verdict's word, or `not_checked` with none — one query for the whole
  list, the way an [open question](#comment-question-and-answer) is
  computed rather than flagged. `CapabilityDto` carries `State` and
  `NewestVerdict` alongside the row.
- **A verdict writes one `capability_judged` event** on the ambition — see
  [Issue event](#issue-event). `CapabilityId` is a cascade-delete FK:
  removing a capability removes its verdicts, but the events already on the
  ambition remain its history.

### ChangeOutcome

`EfHatchChangeOutcome` — `Id`, `IssueId`, `EpicId`, `ChangeEventId`,
`Outcome`, `Why`, `RecordedBy`, `RecordedAt`, `Decision`, `DecidedBy`,
`DecidedAt`. One epic's outcome on one change to the ambition above it.

A **change** is read, never stored: every `capability_added`,
`capability_edited` and `capability_removed` event on one [ambition](#issue)
newer than the last change worked out, named by the id of its newest event.
`capability_moved` is never part of one. A change is worked out when a work
log entry (`EfHatchWorkLogEntry`) with outcome `worked` names it in
`ChangeEventId`, and a change not yet worked out is the open one.
`AmbitionChanges` is the one piece of code that reads a change, so the
dispatcher reads the same thing `GET /issues/{key}/changes` does.

- **`IssueId` is the ambition and `EpicId` the epic**, each a cascade-delete
  FK wired as [Capability](#capability)'s `CapabilityLink` is: deleting
  either removes the row. Unlike that link, a row keeps its own `Id` — two
  outcomes can share an ambition and an epic across different changes — so
  the three together (`IssueId`, `ChangeEventId`, `EpicId`) are a unique
  index, not the key. `ChangeEventId` carries no foreign key, the way
  `EfHatchWorkLogEntry.QuestionId` does not: it names an issue event's id.
- **`Outcome` is one of `carries_on`, `not_touched` or `serves_nothing`** —
  any other word is `400`, naming the three. `Why` may not be blank, and is
  bounded at 300 characters.
- **`POST /issues/{key}/changes/{eventId}/outcomes`** records one, `key`
  the ambition. Never gated on the column, the way a capability verdict is
  not: recording what a change did to an epic is a judgment about the work,
  not a change to what the ambition asks for. Refused with `400`, naming what
  is wrong: an issue that is not an ambition; an `eventId` that is not an
  added, edited or removed event of it; an epic that is not a direct child of
  the ambition; a bad `Outcome` or `Why`. Recording again for the same epic on
  the same change replaces `Outcome`, `Why`, `RecordedBy` and `RecordedAt`; a
  row already decided is refused with `409` and nothing is written; a
  recording identical to the one stored writes nothing.
- **`Decision`, `DecidedBy` and `DecidedAt` are a person's decision on a
  `serves_nothing` outcome**, written by `POST
  /issues/{key}/changes/outcomes/{id}/decision` with `{ decision }`, `key`
  the ambition and `id` the outcome's own. `Decision` is `shelve` or `keep`,
  once per row. A person's route only: a key is refused `403
  key_not_accepted`. Refused with a sentence, writing nothing: `404` for an
  `id` that is not this ambition's; `400` for any other word, an outcome that
  does not serve nothing, or (shelve) a board with no deferred column; `409`
  for an outcome already decided, or (shelve) an epic in a terminal or a
  deferred column; `403` for (shelve) a protected deferred column the caller
  may not move work into. `keep` moves nothing. `shelve` moves the epic and
  the open work under it into the first deferred column in board order, in one
  transaction with the decision, leaving work already terminal or deferred
  where it is; it writes a `status_changed` event on each issue moved and the
  deferral note on every issue waiting on one, and files no
  changelog task - a deferred column is not a close.
- **A decision writes one `change_decided` event on the epic**, see [Issue
  event](#issue-event).
- **A write (new or replaced) writes one `change_outcome_recorded` event on
  the epic** — not the ambition — see [Issue event](#issue-event). It shows
  on the epic's own History.

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
`parent_changed`, `filed_from_changed` (the issue it was
[filed from](#issue) changed - payload `{ from, to }`, display keys, either
null, on the issue that changed alone), `changelog_task_filed` (the board filed
a [changelog task](#the-changelog-task) as this issue closed - payload
`{ key, canonical }`, the task's display key and the repository it is for,
written on the closed issue by the actor `the board`), `ready_changed`, `due_changed`,
`pull_request_changed`,
`no_code_changed` (an issue's [no-code declaration](#finished-with-no-code) was
set, cleared by hand, or cleared by a move left of review - payload `{ from, to }`,
with `moved` naming the column on the last), `no_code_set_aside` (a branch or a
pull request showed the issue did finish with code - payload `reason`, the
declaration that was set aside, beside `remote` and `verdict` or `pullRequest`),
`waiting_pull_request_changed` (an issue's [waiting pull request](#a-pull-request-waiting-to-be-opened)
was written, replaced, removed, or cleared by recording a url - payload `{ from, to }`,
each side `{ title, branch, sha }` or null, and `pullRequest` carrying the url on the last),
`model_override_changed`, `effort_override_changed`, `assignee_changed`,
`priority_changed`, `express_changed`, `question_level_changed` (a person set a
question's level - payload `{ questionId, from, to }`, both by name), `wip_limit_changed` (an epic's [Stories
at once](#stories-at-once) was set, changed or cleared - payload `{ from, to }`,
either side null rather than the default), `dependency_added`,
`dependency_removed`, `capability_added` (`POST /issues/{key}/capabilities` -
payload `{ id, position, statement, howToTell }`; an event written before
`howToTell` was added carries none), `capability_edited` (`PATCH
/issues/{key}/capabilities/{id}` - payload `{ id, position, from: { statement,
howToTell }, to: { statement, howToTell } }`, whole values each side),
`capability_moved` (`PUT /issues/{key}/capabilities/order`, one event per call
- payload `{ from, to }`, both sides every capability id in order),
`capability_removed` (`DELETE /issues/{key}/capabilities/{id}` - payload `{ id,
position, statement, howToTell }` as it stood; same note as `capability_added`),
`capability_judged` (`POST
/issues/{key}/capabilities/{id}/verdicts` recorded a
[CapabilityVerdict](#capabilityverdict) - payload `{ id, position, verdict,
sha, author }`; not `verdict_recorded`, which is a check session's own),
`change_outcome_recorded` (`POST /issues/{key}/changes/{eventId}/outcomes`
recorded a [ChangeOutcome](#changeoutcome) - written on the epic, not the
ambition the change happened to - payload `{ ambition, changeEventId,
outcome, why }`, `ambition` the ambition's display key),
`change_decided` (`POST /issues/{key}/changes/outcomes/{id}/decision`
recorded a person's decision on that epic's outcome - written on the epic -
payload `{ ambition, changeEventId, decision }`, `decision` `shelve` or
`keep`), `dreams_woven` (`PUT /issues/{key}/dreams` replaced the
set of dreams this issue drew on - payload `{ from, to }`, both sides every
dream id sorted ascending), `serves_changed` (`PUT /issues/{key}/serves`
replaced the set of [capabilities](#capability) this epic serves - payload `{
from, to }`, both sides every capability id sorted ascending; one per change,
none for a set already there), `remanded` (`POST /issues/{key}/remand` shelved
this issue into a [dream](#dream) - payload `{ dreamId, to, toId, root }`, the
column named and numbered as `status_changed` does, `root` true on the issue
the remand was made from; written on the root and on every descendant that
moved, beside its `status_changed`), `claim_taken`, `claim_lapsed` (a take found a lease that
had already gone quiet past its terms and is taking it over — payload
`{ from, heardAt }`, the previous holder and when it was last heard from,
written immediately before the `claim_taken` for the runner taking over),
`claim_released` (payload `{ from, to, outcome }`, `outcome` left out entirely
rather than serialized as null where the release named none; `spentNothing:
true` is added on a `dropped` release whose claim spent nothing; `failure` and
`failureWhy` are added where the release named a runner failure), `claim_cleared`,
`stalled` (an increment left the ticket where it found it - payload `{ why }`),
`stall_declined` (the board left a stall mark off because the claim spent
nothing - payload `{ why, because }`, `because` being `spent_nothing`, the actor
whoever sent the mark),
`ordered` (a person ordered this runner, from the Runners page, to work this
ticket — payload `{ runner, by }`),
`verdict_recorded` (a [check](#check) session recorded its verdict - payload
`{ verdict, playbookId }`, written on every call, a replace under the same claim
included), `verdict_held` (the apply found a second consecutive fail and moved
nothing - payload `{ verdict, playbookId }`, `verdict` always `fail`, written
beside the `asked` for the question it posts), `merge_check_changed`, `build_check_changed`, `contribution_filed` (a runner recorded that the issue came from outside - payload `{ pullRequestUrl, headBranch, authorLogin, authorAssociation, originInstance, originProjectKey, originKey }`, each null where the record holds none; written once, by the write that creates the record), `pull_request_state_changed` (the
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
stopped at, same as `letGo` above, and counted apart from a person's answers
by the flow report), `playbook_proposed` (a [playbook proposal](#playbook-proposal)
was filed naming this issue — payload `{ proposalId, changes, reason }`,
`changes` the count of changes the proposal carries), `imported`.

Only two kinds are read back. `status_changed` is read by the
[Timeline](#the-timeline) and by both flow reports ([the cumulative
flow](#the-cumulative-flow) and [the flow](#the-flow)); `claim_released` is read
by the flow report alone, for its `outcome`. No other kind is drawn or counted
anywhere. All of it has been written since the first release
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
`DefaultPrompt`, `Model`, `Effort`, `Budget`, `BriefLimit`, `QuietLimitSeconds`,
`MaxRunSeconds`, `LocalModel`, `DeniedTools`, `IsCheck`, `ProjectId`. Unique on `(from, to, types, shape, project)`.

What an agent is told, and how much thought to spend, when it moves an issue of
some type and shape from one column to the next. `Shape` is one of `any`
(the issue's children are not consulted), `leaf` (it has none) or `parent` (it
has at least one). `DeniedTools` is null or empty for every tool, as today, or
else a list of Claude Code permission rules in deny position — a bare tool
name (`Edit`) or a scoped pattern (`Bash(git push *)`). `ProjectId` is null on
every row written before the column existed, and null means the row speaks
for every project on the board; a row naming one project speaks for that
project alone. See [the dispatcher](#the-dispatcher). `QuietLimitSeconds` is
the seconds a claim under the row may go without a word before it counts as
quiet (see [a claim that has gone quiet](#claim)): null, the default, inherits
`Hatch:StallLapseMinutes`, and a value is a whole number of one or more,
written and cleared the way `Budget` and `BriefLimit` are. `MaxRunSeconds` is
stored and read back the same way, under the same three-state rule; nothing
reads it yet. `LocalModel` is the Ollama id (`name:tag`) a move under the row
runs on when it runs locally: null, the default, means the row never runs
locally; `POST` and `PATCH` take it as a string, null leaving it alone on
patch and blank clearing it, and an alias or another agent's id is a 400. It is
carried in a runner's work answer beside `Model`, which an issue's own model
override changes and `LocalModel` it does not; nothing in dispatch reads it
yet. `IsCheck` is false by default and a person's write only — a
key's `POST` or `PATCH` of it is refused with the rest of the playbook write —
and what it means is in [Check](#check).

**A row can be one the dispatcher never picks.** The wire carries
`UnreachableReason` beside the row: null where [the dispatcher](#the-dispatcher)
could pick it, otherwise one sentence saying that a dispatch from the row's
from-column goes to another column, or nowhere. It is derived from the live
board on every read and stored nowhere, so a column reorder changes it with no
write to the row, and saving such a row is not refused. A check row from review
into the protected merge column is reachable.

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

### Playbook proposal

`EfHatchPlaybookProposal` — `Reason`, `IssueId` (optional, the ticket the session
was working; null once that issue is deleted — the proposal stays), `Author`,
`State` (`pending`, `approved` or `rejected`), `CreatedAt`, `DecidedAt`,
`DecidedBy`. `EfHatchPlaybookProposalChange` — `ProposalId`, `PlaybookId` (null
once the row it names is deleted — `Before` still says what it was), `Before`
and `Patch` (jsonb, a `PlaybookDto` and a `PlaybookPatchRequest`, read and
written through a helper the way `EfHatchPlaybook.DeniedTools` is),
`BasedOnUpdatedAt`.

What a session proposed changing about one or more [playbook](#playbook) rows,
without the write a key is refused on the matrix itself. `Before` is the row as
it stood when the proposal was filed; `Patch` is the fields the change would
set, in `PlaybookPatchRequest`'s own shape; `BasedOnUpdatedAt` is the row's own
`UpdatedAt` at that moment, so a later edit to it is noticed. A change is **out
of date** when the playbook it names is gone (`PlaybookId` null) or its live
`UpdatedAt` no longer matches `BasedOnUpdatedAt` - edited or deleted since the
proposal was made - read back as `outOfDateReason`. At most one pending change
may name a playbook at a time; a second proposal naming a row already pending
is refused until the first is decided. A decided proposal, approved or
rejected, is kept rather than deleted, with `DecidedAt` and `DecidedBy` set.
Filing one that names an issue writes a `playbook_proposed` event there (see
[Issue event](#issue-event)).

A pending proposal is applied or discarded only together with every other one
pending, through `POST /playbook-proposals/approve-all` and `POST
/playbook-proposals/reject-all`, each taking `{ proposalIds }` — the exact set
the caller last read as pending; a `409` naming the reload where that set has
changed underneath it. Approving applies `PlaybookPatches.Apply` to every
named row in one `SaveChangesAsync`, exactly as `PATCH /playbooks/{id}` would,
with the same all-or-nothing rule `POST /playbook-proposals` itself keeps: one
out-of-date or now-invalid change in the batch and nothing in it is applied,
named in the `409` with the proposal, the playbook's transition and types,
and why. Rejecting touches no playbook row. A decided proposal naming an
issue writes `playbook_proposal_approved` or `playbook_proposal_rejected`
there, carrying the proposal id.

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

## The wall, the roles, and API keys

Hatch ships its own wall, off by default (`HATCH_AUTH` in `compose.yaml`), with
Google sign-in. Turning it on is
[The wall: off, on and required](../src/Hatch.Web/apps/hatch/docs/the-wall-off-on-and-required.md#putting-a-front-door-on-it)'s
*Putting a front door on it*. Two boundaries follow: the wall, and the role
gate on top of it — and the wall has a second lane, the API key.

### The posture

- **The wall** (`AuthGate` + `AuthMiddleware`, enforced in process)
  decides whether a request reaches the app at all.
- **The role gate** (`RoleGate`) decides whether an already-authenticated
  request reaches what it asked for, by the role on the person the device
  belongs to — `pending`, `user` or `admin`, an ordered enum on `EfPerson.Role`.
  It asks "at least this role", and it is on whenever the wall is: there is no
  switch. Every Hatch controller carries
  `[RequireRole(PersonRole.User, AcceptKey = true)]` (a handful of
  person-only routes carry `[RequireRole(PersonRole.User)]` with no `AcceptKey`), and
  `AdminAppMiddleware` holds every document request to the same User floor —
  every path except `/api/*` and whatever the wall already lets through
  unchallenged, now that the board is the whole origin rather than one bundle
  under a prefix. The operator's own verbs — people writes and sessions, grants,
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
| `/…` navigation (not `/api`) | no credential | **302** to sign-in with `?r=` |
| `/api/…` | no credential, or a key nobody minted | **401** |
| `/…` navigation (not `/api`) | signed in, Pending (or a device nobody has claimed) | **302** to sign-in with no `?r=`, where the shell says they are waiting |
| `/api/…` | signed in, Pending | **403** `pending_approval` |
| an Admin route | signed in, User | **403** `not_admin` |
| `/…` (not `/api`) | an API key, or any non-navigation | **404** — indistinguishable from an install built without it |
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
why it stops at exactly one. A person's [scope](#person-scope) sits beside it
but grants nothing: it narrows what is seen, never what may be done.

### The caller's scope

`ICallerScope.ResolveAsync` answers one question about the caller: sees every
project (`Unbounded`), or only a set of project ids. The rules, in order, the
first that applies winning:

1. **No HTTP context is unbounded** — work the server starts itself.
2. **A wall that is off is unbounded**, before any row is read: with the wall
   off there is one local person, and a row left from when it was up does not
   narrow them.
3. **A person reads their own row**, and no row is `all`.
4. **A key reads its live owner's row**, through `IActorDirectory.PrincipalAsync`
   (the owner resolved live, as it is for a key's other questions). A key with
   no live owner — none was set — is unbounded. Deleting an owner revokes the
   key, so a key never outlives its owner into that state.

`none` is the empty set. `some` is the rows that remain after any cascade. The
answer is read once per request. A wall that is up with no person and no key
never reaches a Hatch route, so a missing principal here is only a key without
a live owner. `ScopeResolutionFilter` asks once per request, and the answer is
applied by the query filters on `HatchContext`; see
[How scope is enforced](#how-scope-is-enforced).

### The key

`EfApiKey` — `Name` (unique), `Prefix`, `Hash`,
`CreatedAt`, `LastUsedAt`, `RevokedAt`.

An `EfApiKey` is what a runner *is*. The board's settings for it live in
`RunnerConfigs`, in the `hatch` schema, carrying the key's `ApiKeyId` with no
foreign key, because the key is in core. The secret is shown once, when an
person creates the runner.

A secret is `hatch_ak_` + 32 characters from a cryptographic RNG, drawn from
letters and digits only because a key is copied through shells, YAML and JSON
and every one of those has an opinion about punctuation. It is shown **exactly
once**, at mint, in the response to `POST /api/runners`, which the
Runners page's Create runner dialog calls.

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

A runner's failures are kept in `RunnerFailures` (`EfHatchRunnerFailure`), the
same bare `ApiKeyId` and no foreign key. It belongs to no project: it reaches
none through a foreign key, carries no query filter, and is held to that by
`QueryFilterModelAuditTests`, with the other runner tables. See
[Failures](#failures).

Revocation is a **column, not a `DELETE`**, unlike a grant: the audit trail
names this key by a name that has to keep resolving, and "who held this key in
March" is a question a deleted row cannot answer. `LastUsedAt` is written at
most once per `Auth:LastSeenThrottleSeconds`, the same throttle a grant's
last-seen takes and for the same reason.

**The row lives in the core `public` schema**, not in `hatch` beside the app
that prompted it. The wall reads this table on the way in, so a module owning it
would make the wall depend on a module — the one thing `Modules/README.md` says
never happens.

A key is a **peer** of a grant rather than a variant of one. A grant is a device
the household enrolled and the wall lets in whole; a key is a named program
allowed the routes that accept a key. They differ in every field that matters — a key names
a program and a grant names a device, a key is revoked by a column and a grant by a
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

### What a key may take

`AcceptKey` on `[RequireRole]` is a bool that lets a key into that route. It is
**opt-in**, default false: setting it is a claim that this surface has been
thought through for a caller that is a program rather than a person, and the
number of surfaces that have been is small. Every route that does not set it —
minting credentials, revoking sessions, editing the house, roughly forty verbs
— keeps refusing keys, with `key_not_accepted`. That is what makes adding a key
a bounded act rather than a broad one.

Accepting a key widens nothing for a person: a route that accepts one is still
closed to a person below the role it asks for. And a key is never an
*administrator* — `RoleOutcome.Key` is its own outcome, and it means "this
program reaches what the routes that accept a key name", not "this program is
the operator".

If a request comes back `403` for `key_not_accepted`, the key is working and
the route is not one a key may take. Any other `403` names its own cause
instead - a different role-gate code, or a plain sentence about who may make
the change.

### The one edge that is deliberately cut

**Writing a playbook is closed to a key.** The three write verbs on
`PlaybooksController` carry a plain `[RequireRole(PersonRole.User)]` which accepts no key, while the
read accepts one like everything else. `DeniedTools` rides the same
three verbs and the same person-only cut — it is not a new edge.

**That cut only held where the wall was on.** `RoleGate` is dormant with
the wall off, so on a local install a bare `curl` or `hatch api` call used
to reach the write as the local person, same as any other call. `PlaybookWrites.cs`
closes that gap with one guard, in one file, one sentence constant, checked
in the action on all four writing verbs — the three on `PlaybooksController`
plus `PATCH /api/issues/{key}/playbook` — before the row is read. It
refuses a program outright and, with no branch on the wall's state, any call
carrying neither a key nor `Sec-Fetch-Mode`. It does not
stop a caller forging a browser's headers on purpose — with the wall off
nothing on a request is a credential, the same as every other local-mode
guard in Hatch.

**So is writing an issue's override**, and it is the same edge rather than a
second one: `PATCH /api/issues/{key}/playbook` sets the model and the
effort every increment on that ticket runs on, which is a playbook's power
routed through another table. It lives on its own controller
(`IssuePlaybookController`) for exactly that reason — `IssuesController` accepts
a key on everything it holds, so two more fields on the ordinary
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
So `PUT /api/issues/{key}/assignee` lives on its own controller
(`AssigneeController`) not setting `AcceptKey`, for the same reason and by
the same means. Reading is open, and deliberately: an agent has to be able to
say whose ticket it is leaving alone, so both `GET /api/assignees` and the
`assignee` on `IssueDto` are open to a key like everything else.

**And so is setting the priority level**, which is the same edge as the
assignee read from the other side. An assignee holds a ticket *off* the night
shift; priority puts one at the *front* of it — so a key that could set one
could put its own ticket ahead of everything a person filed, every night,
without anything looking wrong on the board. `PUT
/api/issues/{key}/priority` and its legacy two-level alias `PUT
.../expedite` therefore both live on `IssueExpediteController`, which does
not set `AcceptKey` and guards each action the same way, cut in the route by
the same means. Reading is open like the rest: `priority` and the derived
`expedited` both ride `IssueDto` and `IssueCardDto`, because an agent is
entitled to know why it was sent where it was sent. It follows that there is
no `hatch priority` or `hatch expedite` verb — the CLI authenticates with a
key, so the terminal *shows* the level on `board`, `queue` and `show` and sets
it nowhere.

**Filing the bug a failing trunk's button offers (HA-95) bends this edge, and
only this one.** `TrunkBuildBugController` still does not set
`AcceptKey` — a stranger key pressing the button for a ticket of its own choosing is
cut by the same means as priority. But the method itself accepts a key
(HA-792), and is gated again at runtime, because the
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
second write after. Reading the trunk builds themselves stays open to a key,
the way reading a build check is: a verdict about a sha is a fact any runner
reads the same way, whichever repository it binds.

**A key's owner is cut the same way, and for the reason the assignee edge
names directly.** `--mine` (see [the dispatcher](#the-dispatcher)) reads a
key's `OwnerPersonId` to decide whose tickets it may take, so a key that could
set its own owner could simply say it is whoever holds the tickets it wants.
`RunnersController`'s re-owning route is `[RequireAdmin]` with no `AcceptKey`;
its minting route asks for a user and checks the owner inside the action, since
the role attribute reads only a role: with the wall up a user may mint only a
runner they own, naming anybody else is a `403`, and no request leaves a runner
with no owner, while an admin may name anyone; its rotating and revoking routes ask for the runner's
owner or an admin (`RequireRole(User)` and an ownership check, not
`CanControl`, which passes for a runner that belongs to nobody) and take no key
either — not one route carved out for a key — so no key can mint a runner,
revoke one, rotate one, or change whose it is; a person does that, on purpose.

**And so is the WIP section and its limits.** Which columns are work in
progress, and how much of each slice of the board may sit across them at once,
is the operator's to set — a key that could raise a limit or empty the
section could pull more of its own work in overnight, the loop stalling behind
a ceiling it had just raised for itself. `PUT /api/wip` therefore lives
on its own controller (`WipController`) not setting `AcceptKey`, cut in
the route by the same means, and checked a second time in the action for
`IssueClaimController.NotAPerson`'s reason: `RoleGate` is dormant wherever the
wall is off, so the second check is what still refuses a key there. Reading is
open, like the rest: an agent stalled behind a full section is entitled to know why.

**And so is pre-approval, both the issue's own level and the column's flag
it rides on the way through.** Pre-approval decides which gates the loop
may pass *unattended* — a key that could raise it to `express` or
`preapproved` could carry its own ticket through the night with nobody
reading it first, or approve it in advance, the same kind of widening a
playbook or an assignee would be. `PUT /api/issues/{key}/pre-approval`,
and its legacy two-level alias `PUT .../express`, both live on
`IssuePreApprovalController`, not setting `AcceptKey`, cut by the
same means as priority and expedite — and gated by what the change touches
rather than by one blanket check: moving onto or off `express` asks the
same question an ordinary move into `Columns.SendBack` would, through
`IProjectAccess.CanMoveIntoAsync`; moving onto or off `preapproved`, on
either side of the change, asks `IProjectAccess.CanApproveAsync` instead,
since setting it is the approve press given in advance and lowering from
it undoes that act. The column half cannot ride `PATCH
/api/statuses/{id}` — that route accepts a key on every
action, so one more field on the ordinary status patch would have been a
key ticking its own gate — so it gets its own action, `PUT
/api/statuses/{id}/express-skips`, carrying a plain
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
against. `PATCH /api/issues/{key}/wip` therefore lives on its own
controller (`IssueWipLimitController`) not setting `AcceptKey`, cut by
the same means as the playbook override, and checked a second time in the
action for `WipController.NotAPerson`'s reason: `RoleGate` is dormant wherever
the wall is off, so the second check is what still refuses a key there.
Reading is open like the rest: `wipLimit` rides `IssueDto`, because an agent is entitled
to know how many of its siblings it is competing against. It follows that
there is no `hatch` verb that sets it, the same as expedite and express.

One related edge is **not** cut, and is stated rather than papered over:
**nothing stops a key answering its own question.** A key is what
`hatch answer` types with and it is also what a spawned agent inherits; the
server cannot tell them apart, and a check that looked like it could would be
worse than none. What holds the loop shut is one step further out — the dispatch
is refused while a question is open, and the dispatch is a command the operator
types. A separate kind of key for agents would close it properly, and is not
worth a column until somebody wants one.

One route sits outside the wall entirely rather than riding the exempt-paths
list for a reason worth stating: `/.well-known/assetlinks.json`
(`AssetLinksController`) answers Android's own Digital Asset Links verifier,
which holds no cookie and never will. That is safe to leave open because the
answer is public by design - it is a list of which Android package may open
this install's links without the user granting it by hand, nothing more, and
it grants nothing to anyone who merely reads it.

## API surface

Everything under `/api`, every route `[RequireRole(PersonRole.User,
AcceptKey = true)]` except where noted. Issue routes take the display key (`AER-12`).

| Route | Verbs | Notes |
|---|---|---|
| `/projects` | GET, POST | POST validates key format and uniqueness, and carries `color`/`icon` |
| `/projects/{id}` | PATCH, DELETE | PATCH name and key, and carries `color`/`icon`; DELETE 409s unless the project is empty, and takes its [repositories](#repository) with it |
| `/projects/{id}/repositories` | GET | The ordered list of remotes, in `SortOrder` — see [Repository](#repository) |
| `/projects/{id}/repositories` | PUT | **Person only** — plain `[RequireRole(User)]`. The whole ordered list, `[{ remote, baseBranch? }]`; refused as a whole, naming the entry, on an empty, over-limit, unparseable or duplicate remote. Re-sending the same list writes nothing |
| `/projects/{id}/landing` | PUT | **Person only** — plain `[RequireRole(User)]`, owner only. `{ landing?, checksRequired?, onePullRequest? }` — `onePullRequest` is the project's default for [one pull request](#one-pull-request); `landing` is `"pull-request"` or `"trunk"`; `403` naming the project's key from anybody but an owner, `400` naming the bad value on an unknown `landing`, `404` on an unknown project. Re-sending what it already holds writes nothing |
| `/projects/{id}/runner-tags` | PUT | **Person only** — plain `[RequireRole(User)]`, owner only. `{ tags }` — the whole list, replaced; trimmed, lower-cased and deduplicated, at most 8 tags of at most 32 characters, none with a space or comma. `403` naming the project's key from anybody but an owner, `400` naming the tag that breaks the rule (or the limit, on a list over 8), `404` on an unknown project. Re-sending the list it already holds writes nothing, and an empty list clears it |
| `/projects/{id}/binds-tracker` | PUT | **Person only** — plain `[RequireRole(User)]`, owner only. `{ bindsTracker }` — turning it on clears the flag from whichever other project held it, in the same save, and the response names that project's key as `clearedKey` (`null` where none held it); turning it off clears nothing else. `403` naming the project's key from anybody but an owner, `400` where turning it on and the project binds no repository, `404` on an unknown project. Re-sending what it already holds writes nothing |
| `/projects/{id}/stories-on-epics` | PUT | **Person only** — plain `[RequireRole(User)]`, owner only. `{ storiesOnEpics }` — whether the project's stories show on their epics' cards instead of as cards of their own; this route is the only way to write the flag (`PATCH` and `POST` on `/projects` do not reach it), and the answer is the `ProjectDto`. `403` naming the project's key from anybody but an owner, `404` on an unknown project. Re-sending what it already holds writes nothing |
| `/projects/{id}/check-approves-merge` | PUT | **Person only** — plain `[RequireRole(User)]`, owner only. `{ checkApprovesMerge }` — whether a passing check counts as the approval that lets the project's pull request merge; this route is the only way to write the flag (`PATCH` and `POST` on `/projects` do not reach it), and the answer is the `ProjectDto`. A change stamps `checkApprovesMergeBy`/`checkApprovesMergeAt`. `403` naming the project's key from anybody but an owner, `404` on an unknown project. Re-sending what it already holds writes nothing. The close hop reads it, see [the hop](#the-hop) |
| `/projects/{id}/runners-work-contributions` | PUT | **Person only** — plain `[RequireRole(User)]`, owner only. `{ runnersWorkContributions }` — whether a runner works a contribution's branch when it conflicts or its build fails; this route is the only way to write the flag, and the answer is the `ProjectDto`. A change stamps `runnersWorkContributionsBy`/`runnersWorkContributionsAt`. `403` naming the project's key from anybody but an owner, `404` on an unknown project. Re-sending what it already holds writes nothing |
| `/projects/{id}/files-contributions` | PUT | **Person only** — plain `[RequireRole(User)]`, owner only. `{ filesContributions }` — whether the board files its repositories' outside pull requests as contributions; nothing reads it yet, this route is the only way to write the flag, and the answer is the `ProjectDto`. A change stamps `filesContributionsBy`/`filesContributionsAt`. `403` naming the project's key from anybody but an owner, `404` on an unknown project. Re-sending what it already holds writes nothing |
| `/projects/{id}/allows-unsigned-commits` | PUT | **Person only** — plain `[RequireRole(User)]`, owner only. `{ allowsUnsignedCommits }` — whether the project allows commits that are not signed, on until an owner says otherwise; every dispatch carries it as `WorkDto.AllowsUnsignedCommits`, this route is the only way to write the flag, and the answer is the `ProjectDto`. A change stamps `allowsUnsignedCommitsBy`/`allowsUnsignedCommitsAt`. `403` naming the project's key from anybody but an owner, `404` on an unknown project. Re-sending what it already holds writes nothing |
| `/projects/{id}/no-code-closes` | PUT | **Person only** — plain `[RequireRole(User)]`, owner only. `{ noCodeCloses }` — whether a no-code declaration on an issue closes it outright; this route is the only way to write the flag (`PATCH` and `POST` on `/projects` do not reach it), and the answer is the `ProjectDto`. A change stamps `noCodeClosesBy`/`noCodeClosesAt`. `403` naming the project's key from anybody but an owner, `404` on an unknown project. Re-sending what it already holds writes nothing. The close hop reads it, see [the hop](#the-hop) |
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
| `/board` | GET | Statuses plus every issue, ordered by `(StatusId, Priority desc, Rank, Id)`. Never filtered — the browser folds not-yet-ready cards away; the server hands over all of them — except the one card the server itself withholds: a terminal card whose `ClosedAt` is older than `doneHorizonHours` (48 by default, off at `0`) is not handed over at all, and `doneHorizonHours` on the DTO says what that horizon is; stories the horizon withheld whose epic is still on the board are handed over in `pastHorizon` instead, as ordinary cards, so a client can count them; `issues` is unchanged — and `wip`: `{ statusIds, slices }`, `slices` the same two entries as `/wip`'s read but each with `load` and `claimedInbound` too, `null` only where no column is flagged (see [WIP](#wip)) — and `normals`, `at`, and on each card `enteredColumnAt` and `aged`: how long a column's stays normally last and who is past it (see [the flow](#the-flow)) |
| `/board/health` | GET | The board's health in one read for a program: 24 bars over a `window` of `2h`, `4h`, `8h` (default), `12h` or `24h` - moves right by the board, a runner and a person, the bands waiting, in flight and done, and what waits on a person, whose last bar is the attention control's own count, a lane for each runner the caller may see (`runners`), each bar's four `ages` counts against its column's normal, and the issues in flight now (`inFlightIssues`). `project` (repeatable), which the lanes ignore. Open to a key. `400` for another window or an unknown `project` - see [the board health read](#the-board-health-read) |
| `/issues` | GET, POST | GET filters on `projectId`, `type`, `statusId`, `parentKey`, `ancestorKey`, `text`, `filedFromKey` (`""` finds the issues filed from nothing), ANDed, all optional; POST takes `filedFromKey` |
| `/issues/bulk` | POST | `keys` plus any of `type`, `statusId`, `parentKey`, `filedFromKey`, `readyAt`, `dueAt`. No `wipOverride` — a full [WIP](#wip) section or [an epic at its own limit](#an-epics-own-limit) is a per-key failure, named in `failures`; `projectId` with `moveDescendants` moves the batch the same way, each issue keeping its parent unless `parentKey` says otherwise — see [Moving between projects](#moving-between-projects) — `400` on an unknown project, `403` from a key, a live claim anywhere in the moving set a per-key failure rather than a whole-request refusal; the response gains `rekeyed: [{ from, to }]`, additive and empty when no project was named |
| `/issues/{key}` | GET, PATCH, DELETE | PATCH writes one event per changed field; `""` clears a parent, a date or the pull request URL; `wipOverride` — see [WIP](#wip) — moves a full section or a full epic anyway, `409` (`WipRefusalDto`, carrying the epic's own numbers when it is the epic's limit) otherwise, `403` from a key; `projectId` with `moveDescendants` moves it to another project — see [Moving between projects](#moving-between-projects) — re-keying it and (by default) everything below it, and keeping its parent unless `parentKey` names another or `""`; `400` on an unknown project, `403` from a key, `409` naming any issue in the moving set holding a live claim, or a one-PR story's tasks that the move would leave in another project |
| `/issues/{key}/move` | POST | `{ statusId, afterKey?, beforeKey?, fromStatusId?, wipOverride? }` — the server computes the rank. A card no longer in `fromStatusId` is a 409 and nothing is written; a move into a full [WIP](#wip) section or past [an epic's own limit](#an-epics-own-limit) is the same (`409` carrying the epic's own numbers when it is the epic's), unless `wipOverride` is set (person only - `403` from a key). **A key is refused with `409` while the issue's live claim is a [check](#check)'s** — *"<KEY>'s live claim is a check's - a session judges here, it does not move the ticket"* — before anything else is read, a reorder inside one column included; a person's move is not |
| `/issues/{key}/comments` | GET, POST | POST carries the kind (a note, `question`, `answer`, `message` or — though only the apply has a reason to — `verdict`), the `answersId`, and a question's options; every comment carries `deliveredAt` and `deliveredTo` |
| `/issues/{key}/messages/deliver` | POST | marks messages read, all unread or the `ids` named, and answers with only the ones this call marked |
| `/issues/{key}/questions` | GET | `?open=false` for the answered ones too |
| `/issues/{key}/events` | GET | Newest first |
| `/issues/{key}/capabilities` | GET, POST | An ambition's [capabilities](#capability). GET reads them in position order, open to a key anywhere; POST `{ statement, howToTell, position? }` adds one, at the end when no `position` is given, else at it with the rest shifted down - `201`. `400` on an issue that is not an ambition, a blank line, or a `position` outside `1..n+1` |
| `/issues/{key}/capabilities/{id}` | PATCH, DELETE | PATCH `{ statement?, howToTell? }` - a field left out keeps its value, one sent blank is a `400`; `404` on an unknown id. DELETE closes the gap, `204` |
| `/issues/{key}/capabilities/order` | PUT | `{ ids }` - every capability's id once, in the wanted order; `400` when the list misses, repeats or names an id that is not this ambition's. Answers the reordered list. A write (POST, PATCH, PUT or DELETE) from a key is `403` once the ambition is at or past the implementation column, or deferred; a person is never refused |
| `/issues/{key}/serves` | GET, PUT | The [capabilities](#capability) an epic serves. GET reads them on an issue of any type - each row `{ ambitionKey, capabilityId, position, statement, state }`, empty for an issue that serves none. PUT `{ capabilityIds }` replaces the whole set and answers the same list; `400` on an issue that is not an epic, an id that is no capability, or one that belongs to an ambition the epic does not hang beneath (naming both). A set already there writes nothing and leaves the event log and `UpdatedAt` alone. Open to a key in every column |
| `/issues/{key}/capabilities/{id}/verdicts` | GET, POST | A capability's [verdicts](#capabilityverdict), not `EfHatchVerdict`'s. GET answers them newest first. POST `{ verdict, lookedAt, sha }` - `verdict` one of `holds`, `does_not_hold`, `cannot_tell` - answers `201`. `400` on an unknown word (naming the three), a blank `lookedAt` or `sha`, or either over its bound; `404` on an unknown capability, or another ambition's. Never gated on the column: a key may write here in any column |
| `/issues/{key}/passes` | GET | An ambition's passes, newest first - one per work log row that ran from the implementation column and was released `worked` or carried no outcome, with the [capability verdicts](#capabilityverdict) and comments timed inside it. A verdict timed inside no row's window is a pass of its own, with no session. `400` on an issue that is not an ambition, naming its type; `404` on an unknown key |
| `/issues/{key}/playbook` | PATCH | **Person only** — plain `[RequireRole(User)]`. The issue's own model and effort; `""` hands either back to the playbook |
| `/issues/{key}/wip` | PATCH | **Person only** — plain `[RequireRole(User)]`. `{ limit }` — see [Stories at once](#stories-at-once). `""` clears it to no limit; a number below one, or not a number, is `400`; anything but an epic is `400` naming its type |
| `/assignees` | GET | Every person and every live key, plus who the caller is — the picker's rows and *Assign to me* in one read |
| `/issues/{key}/assignee` | PUT | **Person only** — plain `[RequireRole(User)]`. `{ kind, id }`, or both null to unassign — see [Assignee](#assignee) |
| `/issues/{key}/priority` | PUT | **Person only** — plain `[RequireRole(User)]`. `{ priority }` — `"paused"`, `"economy"`, `"low"`, `"normal"`, `"expedited"` or `"emergency"`. Every level but paused floats on the board and is taken first by the dispatcher, most severe first; paused sinks to the bottom and is folded by every dispatch with its own sentence. Setting what it already holds writes nothing; an unknown name is `400` |
| `/issues/{key}/expedite` | PUT | **Person only** — plain `[RequireRole(User)]`. Legacy two-level alias for `/priority` above. `{ expedited }` — `true` sets expedited, `false` sets normal. Setting what it already holds writes nothing |
| `/issues/{key}/pre-approval` | PUT | **Person only** — plain `[RequireRole(User)]`, checked again in the action so it holds with the wall off. `{ level }` — `"none"`, `"express"` or `"preapproved"` — see [Express](#pre-approval). Express needs the same permission a move into the column that feeds implementation needs; preapproved needs an approver. Setting what it already holds writes nothing; an unknown name is `400` |
| `/issues/{key}/one-pr` | PUT | **Open to a key** — `[RequireRole(User, AcceptKey = true)]`. `{ value }` — `"on"`, `"off"` or `"inherit"` (the project's default) — see [One pull request](#one-pull-request). A story only: anything else is `400` naming its type; an unknown value is `400`. `409` once work under the story has started. Setting what it already holds writes nothing |
| `/issues/{key}/runner-tags` | PUT | **Open to a key** — `[RequireRole(User, AcceptKey = true)]`. `{ tags }` — see [Runner tags](#runner-tags). Replaces the issue's own list whole; `400` naming the tag that breaks `RunnerTags.Normalise`'s rule (or the limit, on a list over 8), `400` if `tags` is absent, `404` on an unknown issue. Re-sending the list it already holds writes nothing, and an empty list clears it |
| `/issues/{key}/dangerous` | PUT | **Open to a key** — `[RequireRole(User, AcceptKey = true)]`. `{ reason }` — see [Dangerous](#dangerous). Trimmed; `""` or whitespace clears it. `400` if `reason` is absent, `400` over 200 characters after trimming, `404` on an unknown issue. Re-sending the value it already holds writes nothing; a change writes `dangerous_changed` |
| `/issues/{key}/omitted-runners/{runner}` | PUT | **Open to a key** — `[RequireRole(User, AcceptKey = true)]`. `{ reason }` — see [Omitted runners](#omitted-runners). The runner name may carry a slash the same way `/runners/{name}` does — escaped to `%2F` in the route, decoded once here before it is trimmed. Trimmed; absent or blank stores none. `400` on a blank runner name or one over `EfHatchRunner.MaxNameLength`, `400` on a reason over 200 characters after trimming, `404` on an unknown issue. Re-sending the reason it already holds writes nothing; a change writes `runner_omitted` |
| `/issues/{key}/omitted-runners/{runner}` | DELETE | **Open to a key** — `[RequireRole(User, AcceptKey = true)]`. Lifts the issue's own omission of the runner — see [Omitted runners](#omitted-runners). `404` on an unknown issue, and `404` naming the ancestor for a runner the issue does not itself omit; writes `runner_omission_lifted` |
| `/issues/{key}/release-note` | PUT | **Open to a key under the claim** — `[RequireRole(User, AcceptKey = true)]`. `{ note }`, trimmed; null, empty or whitespace clears it. Over 2,000 characters is `400` naming the cap. A person needs no token; a key must send `?heldToken=` equal to the issue's live claim, else `409` — "holds no live claim" where there is none or it has lapsed, "does not match this token" where it differs. The one second claim accepted is the live claim of the changelog task the board recorded for `{key}` ([why that one](#the-changelog-task)); a change under it writes `{ from, to, via }`, `via` the task's key, and no other issue's claim is taken. A change writes a `release_note_changed` trail entry carrying `{ from, to }`; setting what it already holds writes nothing. Answers the issue; `404` for no such issue |
| `/issues/{key}/express` | PUT | **Person only** — plain `[RequireRole(User)]`. `{ express }` — see [Express](#pre-approval). Setting what it already holds writes nothing. Now an alias for `/pre-approval` above, sharing its guards |
| `/issues/{key}/stall` | PUT | Carries no `[RequireRole]` at all — a dispatcher is exactly who leaves this mark. `{ why }` — the sentence an increment leaves behind it saying why it left the ticket where it found it; sets `StalledAt` to now and `StalledWhy` to it, normalised and capped the way claim chatter is. Writes, so a second mark replaces the first on the row but still leaves its own `stalled` event behind it - except on a claim that [spent nothing](#claim), where the row is left as it was, the answer is still `200` with the issue, and the event is `stall_declined` |
| `/issues/{key}/hold` | PUT | **Person only** — plain `[RequireRole(User)]`, checked again in the action: an agent that could hold its own ticket could park it off the board for the night. `{ held }` — holds it or resumes it. Resuming clears `StalledAt` and `StalledWhy` together, so *Resume now* leaves nothing behind for the dispatcher to fold on, even where nothing had set `held` true. Setting what it already holds writes nothing and does not move `UpdatedAt` — except that `held: false` still clears a stall mark where one is there to clear |
| `/issues/{key}/claim` | POST | Takes the [lease](#claim). `{ runner, playbookId? }` — the playbook the lease works under, stored as `ClaimPlaybookId` and giving the lease its quiet window (a repo-sourced file row's negative id works too, and sets the window without being stored), `400` naming it where there is no such playbook; answers with the token, the holder, when it was taken and the TTL, `stallLapseSeconds` — the same window a claim goes quiet by, in seconds, or `0` when lapsing is off — and `maxRunSeconds`, the playbook's run cap in seconds or `0` for none (also copied onto the claim, a file row's with no id stored). `409` naming the holder where something live already has it — including the same runner asking twice |
| `/issues/{key}/claim/heartbeat` | POST | `{ token, chatter?, playbookId?, usage?, usageReadAt? }` — refreshes it, `204`. `409` on a token that is not the row's, on a lease that is over, and on one that has gone quiet — see [Claim](#claim). `chatter` absent leaves the carried line alone, `""` clears it, anything longer than the column is truncated rather than refused. `playbookId` absent leaves the claim's playbook alone, and a value overwrites it, and the claim's quiet window with it (`400` for an unknown playbook; a repo-sourced file row's negative id is accepted); a heartbeat cannot clear it. `usage` is the runner heartbeat's reading and follows its rule (absent or empty leaves the row alone); it is written to the holding session, or to the one runner row the beat kept live (never to every row at the checkout), only on a beat that renewed the lease, is not written when `usageReadAt` is absent or older than the row's own, and a `409` writes nothing |
| `/issues/{key}/claim?token=…&outcome=…` | DELETE | Releases it, `204`. A mismatched token is `409` and clears nothing; an issue holding no claim is `204` and writes nothing. `outcome` (`dropped`, `worked`, `preempted` or `recused`) is read only where `token` is given and is otherwise optional — an older CLI names none — and a value that is none of those is `400`. A `dropped` release whose claim [spent nothing](#claim) is written with the outcome as sent plus `spentNothing: true`, and `letGo` does not count it. **With no token at all it is person-only** — an agent that could clear another runner's claim could take a ticket off it mid-increment |
| `/issues/{key}/verdicts` | POST | Records a [check](#check) session's [verdict](#verdict). `{ verdict, findings? }`; open to a key and a person, `204`, no body. `400` where the live claim is not under a check playbook (*"a verdict needs a check playbook covering <KEY> where it stands, and none does"* — also what a claim with no playbook named, or no claim at all, gets), for a `verdict` that is not `pass` or `fail` (*"\"<word>\" is not a verdict - it is \"pass\" or \"fail\""*), and for `findings` over the comment body length (*"findings are at most <n> characters"*); `404` on an unknown key. A second call under the same claim replaces the first. Writes a `verdict_recorded` event and moves nothing. No `GET` |
| `/issues/{key}/merge-check` | PUT | Keeps a runner's [verdict](#merge-check) for one repository. `{ remote, trunk, trunkSha, verdict, branch?, branchSha?, files?, runner, holdsTrunk? }`; answers with what it now holds. Refused, in a sentence, for a remote that does not canonicalise, an unknown verdict, `conflicted` with no files, and `clean` or `conflicted` with no branch sha; `404` on an unknown key. The column is not checked. A verdict that repeats the stored one writes no event, and neither does a change to `holdsTrunk` alone |
| `/issues/{key}/pull-request-state` | PUT | Keeps what the forge last said of the pull request recorded on an issue: its merge state (`clean`, `blocked`, `behind`, `dirty`, `unstable`, `draft` or `unknown`) and its review decision (`none`, `approved`, `changes-requested` or `review-required`), with the head sha it was read at and when. `{ url, headSha, mergeState, reviewDecision, runner, headBranch? }`; answers `204`. `headBranch` is the branch the pull request comes from, kept beside the state and cleared with the url; blank or absent leaves what is kept, and writes no event. Refused, in a sentence, with `409` for an issue with no pull request recorded or a `url` that is not the recorded one, and `400` for a merge state or review decision outside those lists, an empty head sha, or one longer than a sha can be, or a head branch longer than a contribution's can be; `404` on an unknown key. The column is not checked, and `runner` is neither checked nor kept. The board stores them; `MergeWork.Judge` reads the merge state and head sha (see [the merge column's judge list](#the-approval-column)) and nothing else on the server does, and the review decision is read by nothing. A read that repeats the stored merge state, review decision and head sha writes no event and refreshes only when it was read |
| `/issues/{key}/waiting-pull-request` | PUT, DELETE | Keeps one [waiting pull request](#a-pull-request-waiting-to-be-opened) on an issue. **PUT** is open to a key: `{ title, body?, branch, sha, remote?, why }`, trimmed, answering the `IssueDto`; the caller's name is kept as its runner. Refused, in a sentence, with `400` for a blank title, branch, sha or why, a title over 256 characters, a why over 500 or a body over the forge's limit, `409` once a pull request url is recorded, and `404` on an unknown key. A second write replaces the first; one that changes nothing writes no event and does not move `UpdatedAt`. **DELETE** is a person's alone (a key gets `403`), answers `204`, and answers `204` with no event where nothing waits. The column is not checked. Each real change adds a `waiting_pull_request_changed` event |
| `/issues/{key}/landing` | PUT | Keeps what a repository holds for an issue's branch — see [landing](#landing). `{ remote, sha, trunk?, containedBy?, runner? }`; answers with what it now holds. Refused, in a sentence, for a remote that does not canonicalise, an empty or non-hex sha, and `containedBy` given without a `trunk`; `404` on an unknown key. The column is not checked. A write that repeats the stored sha and `containedBy` writes no event |
| `/questions` | GET | Every open question in the house |
| `/search` | GET | `q`, `projectId`, `type`, `statusId`, `createdFrom`, `createdTo`, `closedFrom`, `closedTo`, `limit` (50, max 200), `offset`, all optional and ANDed — see [Search](#search). Done issues are included by default; `statusId` narrows to one column |
| `/plan`, `/plan/{key}` | GET | See [the level above the board](#the-level-above-the-board) |
| `/work/next` | GET | See [the dispatcher](#the-dispatcher). `?heldToken=` names a [claim](#claim) of one's own, so it is not folded past as somebody else's. `?remote=` (repeatable), `?standing=` and `?clones=` declare what the runner has; absent is undeclared and folds nothing. `?tag=` (repeatable) declares which [runner tags](#project) it advertises; absent is advertising none, and a tag that breaks `RunnerTags`'s own rule is a `400` naming it. `?dangerous=false` declares that it refuses dangerous work, folding an issue [flagged dangerous](#the-dispatcher); absent or `true` folds nothing. `?model=` (repeatable) and `?agent=` declare which models and which agent the runner offers, folding a move whose model it cannot spawn — see [the dispatcher](#the-dispatcher); absent folds nothing, and a bad entry is a `400`. `?has=` (repeatable, `id` or `id@version`) declares the development dependencies the runner has, folding a move that runs the issue's code whose project's bound repositories declare a dependency none of these satisfies, or whose `.hatch/dependencies.json` is invalid — see [the dispatcher](#the-dispatcher); absent folds nothing, and a bad entry is a `400`. `?mine=true` narrows the pass to the caller's own tickets — see [one more, on `next` alone](#one-more-on-next-alone); `400` when the calling key belongs to nobody. `?runner=<name>` names the caller's own runner, so a ticket that runner let go of last is folded for it while another live runner could take it — see [what a pass skipped](#what-a-pass-skipped); absent folds nothing. The same `?runner=` also folds a ticket that runner is [omitted](#omitted-runners) from, its own or an ancestor's — see [the dispatcher](#the-dispatcher) — and a clear row left for a runner marked [preferred](#runners-on-the-board) that is free to take it. Carries `letGo`, the count of this issue's trailing dropped releases — `0` on a client too old to read it. Also carries `nextBriefLimit`, the next row's brief limit — see the paragraph below `WorkDto.FailStatusName`'s for what it is and when it is null |
| `/work/{key}` | GET | See [the dispatcher](#the-dispatcher). Same `?heldToken=`, `?remote=`, `?standing=`, `?clones=`, `?tag=`, `?dangerous=`, `?model=`, `?agent=` and `?has=` as `/work/next`. `?mine=` is ignored — somebody who names a ticket has already chosen it. `?runner=<name>` folds an issue that runner is [omitted](#omitted-runners) from, and one left for a free runner marked [preferred](#runners-on-the-board): this route runs no let-go fold. Carries `letGo` the same way. Also carries `failStatusName` — the implementation column's name, the column a failed [check](#check) sends the issue back to, set on every dispatch and null where the board has none — `lastCheckFindings`, the newest check's findings, set only for an issue standing in that column whose most recent `status_changed` event carries `verdict: fail`, null otherwise — and `nextBriefLimit`, the next row's brief limit, same as `/work/next` |
| `/work/queue` | GET | The same walk `next` takes, reported rather than acted on, the same three repository flags and the same `?tag=`, `?dangerous=`, `?model=`, `?agent=` and `?has=` — see [what a pass skipped](#what-a-pass-skipped). Same `?mine=true`, folding every ticket that is not the caller's own with a sentence naming whose it is. `?asRunner=<name>` answers off that runner's own row instead of the query string — `remote`, `standing`, `clones`, `mine` and `ancestorKey` come from it and may not be given alongside it; the runner's row tags are read the same way, and a `?tag=` or `?dangerous=` beside it is ignored rather than refused; the row's `Models`, `Agent` and `Has` fold a move whose model it cannot spawn or whose dependency it lacks, and a row that never reported them folds nothing; a name no live row answers to is a `400` naming it. Where that key holds live loop sessions, both `asRunner` and the let-go peer check below judge it off its beat row, its config and those sessions together — a key with no sessions answers exactly as it always has. `?runner=<name>` names the caller's own runner as on `/work/next` (`asRunner` wins where both are given), folding a ticket that runner let go of while another live runner could take it, one it is [omitted](#omitted-runners) from, and a clear row left for a free runner marked [preferred](#runners-on-the-board). `blockedKind` names which of seven kinds folded the row, null wherever `blocked` is. `deadlocked` is true on a row no pass will clear on its own, and its `blocked` sentence starts `deadlocked - ` (see [deadlocked](#deadlocked)). `clearNote` names a row that is clear only because a stall mark's resume window passed, e.g. `"it stalled 18 minutes ago and resumes itself after 15 minutes"` — null on every ordinary clear row; `?takers=true` (default `false`) judges, for every clear row, which live runners could take it right now — see [runner capacity](#runner-capacity) — adding `takers` (runner names, sorted; null when not asked for; empty when none can) and `untaken` (one `{runner, reason}` per live runner that cannot, filled in only when `takers` holds fewer than two names). `hatch queue` and the web queue ask for this; `/work/next`'s own poll does not |
| `/work/{key}/hop` | POST | Carries one of seven kinds one column right with no session — see [the hop](#the-hop): an issue [pre-approved](#pre-approval) at `express` or above standing in a column marked `ExpressSkips`; an epic still short of the implementation column, on each move right while it stays short of it; a story or bug under a running epic; a child its parent pulls; a childless issue over its playbook's `BriefLimit`, sent back; a pre-approved issue in review whose branch merges cleanly or needs only bringing current; or a pre-approved parent, or a parent in a project whose *A passing check approves the merge* setting is on, in review with no pull request and every child done, or an issue that declared itself finished with no code and is pre-approved or in a project whose *A no-code declaration closes the ticket* setting is on, carried past review into the first terminal column. Takes the same query parameters as `GET /work/{key}` and no `heldToken`: a hop takes no claim. It reads `?tag=` the same way `GET /work/{key}` does, folded by `Dispatch.Blocked` the same way every other move is - [runner tags](#project) gate a hop of any kind, not only the scan's own candidate selection. It reads `?runner=<name>` for the omission fold alone, folding an issue that runner is [omitted](#omitted-runners) from, and not the let-go one. It reads no `?dangerous=`, because a hop runs no code: the endpoint is the loop's own write and judges no dangerous flag. `409` carrying the fold's sentence where the issue is blocked, and `409` where it is clear but not a hop |
| `/work/{key}/merged` | POST | Advances an issue in the review column whose pull request has merged, one column right, with no claim and no session, the same whether the merge was clicked on the forge or made from the terminal with gh pr merge in any mode. `{ url, runner }` — `runner` rides the body unused, for shape parity with the requests that do read it. `409` for a column other than review, for no pull request recorded, for a `url` that does not match the one recorded (trimmed, ordinal), for no column after review, and for a claimed issue; `404` on an unknown key. When two requests report one merge at once, the first moves the issue and the second gets the same `409` an issue already past review gets (see [two reports of one merge](#the-merge-train)). See [the five paths past the terminal guard](#the-five-paths-past-the-terminal-guard) |
| `/work/{key}/closed` | POST | Records that an issue's pull request was closed without merging (`PullRequestClosedAt`) — a runner's poll calls it once, on the first read, and does nothing further; `MergeWork.Judge` holds an approval on this until a person acts. `{ url, runner }`, the same shape parity as `/merged`. `409` for a column that does not merge the pull request, for no pull request recorded, and for a `url` that does not match the one recorded; `404` on an unknown key |
| `/work/{key}/pushback` | POST | Carries an issue in the merge column back to the review column, with no claim and no session — judged by `MergeWork` itself, off the issue's own stored merge and build checks, not by the caller. `{ url, runner }` — `runner` rides the body unused, for shape parity. `409` for a column that does not merge the pull request, for no pull request recorded, for a `url` that does not match the one recorded (trimmed, ordinal), for a judged kind other than `pushback` (carrying the judge's own sentence), for no review column on the board or one that is the merge column itself, and for a claimed issue; `404` on an unknown key |
| `/work/{key}/landed` | POST | Carries a trunk-landing issue in the review column to the first terminal column after it, with no claim and no session — judged by `LandingWork` itself, off the issue's own stored [landings](#landing) and [trunk builds](#trunk-build), not by the caller. No body. `409` for a column other than review, for a project that lands by pull request (*"this project lands by pull request, so there is no landing to confirm"*), for an issue with a pull request recorded (*"a pull request is recorded on this issue, so it lands by merge and not by landing"*), for no terminal column after review, for a judged kind other than `land` (carrying the judge's own sentence), and for a claimed issue; `404` on an unknown key. See [landing on the trunk](#landing-on-the-trunk) and [the five paths past the terminal guard](#the-five-paths-past-the-terminal-guard) |
| `/work/{key}/verdict` | POST | Applies the [verdict](#verdict) a check session recorded under `?heldToken=` — the runner's own write, after the session, with no gate of any kind; see [Check](#check). A pass moves the issue to where a dispatch from the column it stands in would take it, a first fail to the implementation column, and a second consecutive fail moves nothing and asks a question. `200` with the issue. `409` for an issue holding no live claim (*"<KEY> holds no live claim, so there is nothing for this token to match"*) and for a token that is not the claim's (*"<KEY>'s live claim does not match this token"*); `404` on an unknown key and for no unapplied verdict under that claim (*"no verdict was recorded under this claim"*), which is also what a retried apply gets. Takes `?remote=`, `?standing=` and `?clones=` as `GET /work/{key}` does, and ignores them |
| `/issues/{key}/build-check` | PUT | Keeps a runner's [build verdict](#build-check) for one repository. `{ remote, branch, sha, verdict, failing?, runner, pushedByIncrement? }`; answers with what it now holds. Refused, in a sentence, for a remote that does not canonicalise, an unknown verdict, no branch or sha, and `failed` with no failing checks; a link that is not `http(s)` is stored as null; `404` on an unknown key. The column is not checked. A verdict that repeats the stored one writes no event, and one that moves the row into failed-and-flagged marks the issue stalled with it |
| `/trunk-builds` | PUT | Keeps a runner's [trunk build verdict](#trunk-build) for one repository's trunk. `{ remote, trunk, sha, verdict, failing?, runner }`; answers with what it now holds. The same refusals the build check's write takes, naming the trunk rather than the branch. No event and no question — there is no issue to write either on. `passed` lets an attached bug go |
| `/trunk-builds` | GET | Every stored [trunk verdict](#trunk-build), ordered by canonical then trunk — what a runner's poll reads once a poll, since a trunk carries no issue for the verdict to ride in on |
| `/trunk-builds/{id}/bug` | POST | No class-level `RequireRole`, the way expedite is cut, but the method itself accepts a key, and is gated again at runtime, since the attribute cannot vary by project — see [the one edge that is deliberately cut](#the-one-edge-that-is-deliberately-cut). Files the bug a failing trunk's *File a bug* button asks for (HA-95), in the project that binds the repository (lowest project id first, else the lowest-id project that binds it at all) — see [what is waiting on you](#what-is-waiting-on-you). A person's own call is unchanged: expedited, no pre-approval, the board's first column, each overridable in the body. **A key's priority, pre-approval level and column come from the project instead, never from the body** — its own `AutoHealBugPriority`, `AutoHealBugPreApproval` and `AutoHealBugStatusId`, written only through the owner-only `PUT .../landing`. `409` once the row is no longer failing; `400` naming the Projects page when no project binds it, or when a key's project has auto-filing off; a second call answers the bug already filed rather than filing another |
| `/playbook-files` | GET | Every stored [playbook file](#playbook-file), ordered by canonical then trunk, with the path itself alongside them — what the Playbooks page reads once a load, and what a runner's poll reads once a poll to know whether it has already put this sha |
| `/playbook-files` | PUT | Keeps a runner's [playbook file](#playbook-file) for one repository's trunk. `{ remote, trunk, sha, content?, url?, runner }` — `content` the blob's exact bytes, base64-encoded, absent for a trunk with nothing at the path. Hashes and parses the content itself rather than trusting a client-sent verdict. The same refusals the trunk build's write takes, naming the trunk, plus content that is not valid base64 or over the parser's own size limit. No event and no question — there is no issue to write either on |
| `/dependency-files` | GET | Every stored [dependency file](#dependency-file), ordered by canonical then trunk, with the path itself alongside them, each entry's id resolved to its current label |
| `/dependency-files` | PUT | Keeps a runner's [dependency file](#dependency-file) for one repository's trunk. `{ remote, trunk, sha, content?, url?, runner }` — `content` the blob's exact bytes, base64-encoded, absent for a trunk with nothing at the path. Hashes and parses the content itself rather than trusting a client-sent verdict via `DependencyDeclaration.Parse`. The same refusals the trunk build's write takes, naming the trunk, plus content that is not valid base64 or over the parser's own size limit. No event and no question — there is no issue to write either on |
| `/changelog-files` | GET | Every stored [changelog file](#changelog-file), ordered by canonical then trunk, with the path itself and the canonicals currently elected; `?remote=` (repeatable) is answered in `ElectedRemotes` |
| `/changelog-files` | PUT | Keeps a runner's [changelog file](#changelog-file) for one repository's trunk. `{ remote, trunk, sha, content?, url?, runner }` — `content` the blob's exact bytes, base64-encoded, absent for a trunk with nothing at the path. Judges the bytes against the board's own projection rather than trusting a client-sent verdict; where the state is `behind` or `absent` and no [changelog task](#the-changelog-task) for that canonical is open in any column, files one. `404` for a remote no project elects. No event and no question |
| `/work/review` | GET | Every issue in the review column the caller holds a checkout of, with its bound repositories and the [merge checks](#merge-check) the board holds — what a runner's poll reads before it asks git anything. `?remote=` (repeatable) and `?standing=` as on the queue; no `clones`, because a poll clones nothing. Not narrowed by a claim, a question, a date or an assignee — see [checking the branches in review](#checking-the-branches-in-review) |
| `/playbooks` | GET | **Reads only.** POST/PATCH/DELETE are plain `[RequireRole(User)]`, and checked again in the action by `PlaybookWrites` so the cut holds with the wall off too - a person means a person at the browser, not merely a signed-in session with no UI. Each playbook carries `isCheck`, `quietLimitSeconds` (null inherits `Hatch:StallLapseMinutes`; `POST`/`PATCH` take it as a string, the three states `budget` takes, 400 *a quiet limit is a whole number of one or more, in seconds* otherwise) and `maxRunSeconds` (null means no limit; `POST`/`PATCH` take it as a string, the same three states, 400 *a run limit is a whole number of one or more, in seconds* otherwise - a claim copies it as its run cap, see [Claim](#claim)) and `localModel` (null means the row never runs locally; `POST`/`PATCH` take it as a string, null leaving it alone and blank clearing it, 400 *a local model is an Ollama id ...* for an alias or another agent's id - carried in a runner's work answer beside `model`, not yet read by dispatch); a person's `POST` and `PATCH` take `isCheck?` (absent is false on create and leaves it alone on patch) — see [Check](#check). A response's `prompt` is the effective text and `defaultPrompt` the prescribed one, or null; a `PATCH` of a blank `prompt`, or of one equal to the prescribed text, hands the row back to its prescribed text (400 *a playbook needs a prompt* where there is none) — see [Playbooks](#playbooks) |
| `/playbook-proposals` | GET | The queue for one [playbook proposal](#playbook-proposal) state, oldest first — pending by default, or `?state=approved`, `rejected` or `all` |
| `/playbook-proposals/{id}` | GET | One proposal, with every change it carries |
| `/playbook-proposals` | POST | Files a [playbook proposal](#playbook-proposal) — a reason, an optional issue, and one or more changes — checked against the live board the same way `PATCH /playbooks/{id}` is, and against every other pending proposal naming the same row, before anything saves; changes nothing live. A named issue gets a `playbook_proposed` event |
| `/playbook-proposals/approve-all` | POST | Plain `[RequireRole(User)]`, checked again by `PlaybookWrites` the way `/playbooks` POST/PATCH/DELETE are - a person means a person at the browser, not merely a signed-in session with no UI. Applies every [pending proposal](#playbook-proposal)'s change in one transaction, exactly as `PATCH /playbooks/{id}` would, checked against the live board before any row is touched. `{ proposalIds }` — the exact set the caller last read as pending. `409` naming the reload where that set has changed underneath it, and naming the proposal, the playbook's transition and types, and why, for one out-of-date or now-invalid change in the batch — either way nothing in it is applied. A decided proposal naming an issue gets a `playbook_proposal_approved` event |
| `/playbook-proposals/reject-all` | POST | The same person-only gate as the row above. Marks every [pending proposal](#playbook-proposal) rejected, touching no playbook row. `{ proposalIds }` — the exact set the caller last read as pending. `409` naming the reload where that set has changed underneath it. A decided proposal naming an issue gets a `playbook_proposal_rejected` event |
| `/import/preview`, `/import/preview-text`, `/import` | POST | See [the importer](#the-importer) |
| `/utilization` | GET | My own Claude headroom, off my own runners' heartbeats. `204` when no runner of mine has ever reported a reading — see [the battery](#the-battery) |
| `/goose/runs` | GET | The goose's runs, newest first by last sweep, without the per-check detail but with `checks` and `checksFailed` counts; `limit` 1-200, default 50. Empty for a caller whose scope is not every project — see [the goose](#the-goose) |
| `/goose/runs/{id}` | GET | One run with its per-check entries; `404` for a missing id and for every id to a caller whose scope is not every project — see [the goose](#the-goose) |
| `/goose/findings/{id}/bug` | POST | `[RequireRole(PersonRole.User)]` on the method with no `AcceptKey`, so a key is refused. Files a bug from a goose finding and stores it on the finding - expedited, no pre-approval and the leftmost column unless the optional body (`priority`, `statusId`, `preApproval`, `projectKey`) says otherwise. A `projectKey` naming another project than the finding's is `400`; a finding naming none needs one the caller can see. A second call, or one at the same moment, answers the bug already filed - see [the goose](#the-goose) |
| `/attention` | GET | What the loop is waiting on a person for — the issues up for review that carry a pull request, how many in that column carry none, every open question still waiting on somebody — not one whose ticket has already shipped, every repository's trunk whose latest build is failing (counted, unlike the rest below), and (listed, not counted) the issues in review whose branch conflicts or whose build has failed, plus how many of those are held back from the pull request list on that account (`reviewsHeldBack`), plus every live runner out of Claude usage (`exhaustedRunners`), plus every current failure of a runner this reader is sent (`runnerFailures`, counted, unlike the line above - see [runners that are failing](#runners-that-are-failing)), plus every issue the queue marks [deadlocked](#deadlocked) (`deadlocked`), plus every issue whose next move no live runner can take for want of a model or a development dependency (`waitingOnARunner`, see [a move no live runner can take](#a-move-no-live-runner-can-take)), plus every project whose clear implementation steps no live runner, or only one, can take (`runnerCapacity`, see [runner capacity](#runner-capacity)), plus every issue held or marked stalled, in board order, with who it waits on (`stalled`, `stallResumeSeconds`), plus whatever is wrong with the backup, or null when there is nothing (`backup`). One read for all of it — see [what is waiting on you](#what-is-waiting-on-you) |
| `/local-person` | GET | What to call whoever is sitting here, and whether anybody said so. `204` wherever the wall is up |
| `/settings` | GET, PUT | **Person only** — plain `[RequireRole(User)]`, so a key is refused the read as well as the write. The Claude subscription token and the two agent API keys a Hatch install of its own has — see [the credentials, still](#the-credentials-still). PUT follows the bulk rule: a field left out is left alone, `""` clears it. It also carries the site's default token caps, `defaultMaxTokens` and `defaultMaxTokensPerHour` (`500k`, `2M`, a whole number; zero or junk is a `400` that writes nothing) — **admin-only to write** inside this person-only route: a `403` for a person who is not an admin with the wall up, and for a key, but only when either default is present in the body. Runners with no cap of their own run under them — see [Runners on the board](#runners-on-the-board) |
| `/settings/claude-token` | GET | The token itself, wrapped with `SecretProtector` for the wire. The one route in Hatch that hands a live secret back out, and it is cut the opposite way to `/settings` beside it — **a key may take it**, because its ordinary caller is the container runner's entrypoint (`containers/hatch-runner/`) authenticating a `claude` CLI it starts itself. **Refused outright wherever the wall is up** — every caller, key or person — because a token crossing a network is a different question from one handed to a container on the same laptop. `204` when none is set. Kept for an older container; new callers use `/settings/credentials/{agent}` below |
| `/settings/credentials/{agent}` | GET | The same kind of route, generalised past Claude: `{agent}` is `claude`, `codex` or `grok`, else `404`. Wrapped with the same `SecretProtector`, taken by a key the same way, and refused outright wherever the wall is up — all exactly as `/settings/claude-token`'s row above. `204` when that agent's credential is unset |
| `/issues/{key}/work-log` | GET, POST | What each session on this issue cost, each entry also carrying its playbook's source and, for a file-sourced row, the trunk, sha and hash it was confirmed against, and which agent ran it (`PlaybookSource`, `PlaybookFileTrunk`, `PlaybookFileSha`, `PlaybookFileHash`, `Agent`), and the playbook, transition, issue type, model and effort it ran under (`PlaybookKey`, `PlaybookFromStatusId`, `PlaybookToStatusId`, `IssueType`, `PlaybookModel`, `PlaybookEffort`), and the model dispatch chose, where it ran and a local model's digest (`DispatchModel`, `ModelPlacement` of `local`, `fallback`, `cloud` or null, `ModelDigest`; `/work-log/sessions` rows carry the same three), and whether the increment moved its ticket and why (`Outcome`, `Reason`), stamped by the claim release that named this row's session id and null on a row never stamped; an entry's cost is null when the agent reported no price — unpriced, not `$0` — and a `GET`'s totals carry `unpricedSessions` beside the priced sum. **POST is a key only** — a browser is refused outright, because the only honest writer of a meter reading is the dispatcher that read it. See [the leaderboard](#the-leaderboard) |
| `/work-log/sessions` | GET | The sessions in a range, ranked, with the range's own totals — see [the leaderboard](#the-leaderboard) |
| `/work-log/history` | GET | The same rows folded into equal buckets of time, for the graph |
| `/work-log/by-playbook` | GET | One row per playbook/transition/type/model/effort/outcome/reason combination in a range, heaviest first; all three of `history`, `sessions` and `by-playbook` take an optional `outcome` (one of the claim outcomes, or `none`) and `ancestorKey`, and `sessions`/`by-playbook` carry `outcome` and `reason` on their rows — see [the leaderboard](#the-leaderboard) |
| `/reports/timeline` | GET | When the work happened: top-level epics active in a range, and the work beneath each, as stretches in the board's active columns. `from`, `to` (default: the 90 days ending now), `projectId`, `createdBy`. Open to a key. `400` for an unparseable or inverted bound and for an unknown `projectId` — see [the timeline](#the-timeline) |
| `/reports/cumulative-flow` | GET | How many issues sat in each board column over time, read off the same status history as the timeline; a terminal column baselines at zero at the window start and counts only what closed inside it. Also cut by project (`projects`/`projectCounts`), agreeing with `columns`/`counts` on the total at every point. `from`, `to` (default: the 90 days ending now), `project` (repeatable). Open to a key. `400` for an unparseable or inverted bound, or an unknown `project` key — see [the cumulative flow](#the-cumulative-flow) |
| `/reports/flow` | GET | How work moved through the board in a range: per column how many issues entered, left and dwelled, with median and p90 dwell and how many stand there at the end; and the window's drops, hops, send-backs, questions and sessions, as counts and durations with no per-issue rows. `from`, `to` (default: the 90 days ending now), `projectId`, `type`. Open to a key. `400` for an unparseable or inverted bound and for an unknown `type` or `projectId` — see [the flow](#the-flow) |
| `/reports/changelog` | GET | Closed top-level epics, bucketed over a range rather than split by day, each bucket's epics aligned to the reader's own clock. `from`, `to` (default: the 14 days ending now), `projectId` (repeatable), `bucket` (`hour`, `day`, `week` or `month`; default `day`), `offsetMinutes` (minutes east of UTC, default `0`), `level` (`epic` only, for now), `loose` (`true`/`false`, case-insensitive; closed bugs and parentless stories, with `priority`, at `level=epic`). Open to a key. `400` for an unparseable or inverted bound, an `offsetMinutes` outside -1440..1440, an unrecognised `bucket`, a `level` other than `epic`, an unparseable `loose`, or an unknown `projectId` — see [the changelog report](#the-changelog-report) |
| `/reports/activity` | GET | One actor's events in a range, as counts by kind, a per-issue rundown and the newest 500 rows. `actor` (required, matched exactly), `from`, `to` (default: the 14 days ending now), `projectId` (repeatable), `offsetMinutes`, `gap`, `lead`, `window`, `includeKeys` — also the rate of work as a `series`, the bracketed `sessions` and their `estimate`. Open to a key. `400` for a blank `actor`, an unparseable or inverted bound, an out-of-range `offsetMinutes`, `gap`, `lead` or `window`, or an unknown `projectId` — see [the activity report](#the-activity-report) |
| `/reports/consumption` | GET | What each issue matching the filter — and everything beneath it — consumed in a window: tokens, notional USD and runner time, plus a time series for a graph. `from`, `to` (default: the 90 days ending now), `type` (one of the issue types, or `all`; default `epic`), `sort` (`activity`, `tokens`, `cost` or `time`; default `activity`), `limit` (1-500, default 100, caps the rows only), `offsetMinutes`, `statusId` (repeatable), `projectId` (repeatable). Open to a key. `400`s in order: bound, offset, type, sort, limit, an unknown `statusId` or `projectId` — see [the consumption report](#the-consumption-report) |
| `/reports/consumption.csv` | GET | The same filter, with no `limit` at all — every matching row as a CSV a spreadsheet opens. Same `from`, `to`, `type`, `sort`, `offsetMinutes`, `statusId`, `projectId`; the same `400`s, in the same order, minus the limit step. Open to a key — see [the consumption report](#the-consumption-report) |
| `/runners` | GET | Every runner heard from lately, most recent first — see [runners on the board](#runners-on-the-board) |
| `/runners/{name}` | POST | The legacy heartbeat - the CLI no longer calls it, and beats `POST /runners/sessions` instead, named by its key alone; this route stays for old CLIs. Says what this process is, answers with what it has been asked to do. The name is a character off the cast list, escaped — a slash in it stays `%2F`. `where` (`host:/path/to/checkout`) rides beside it; a name already the *live* runner at a different `where` is refused with `409` |
| `/runners/{name}` | PATCH | **Person only** — plain `[RequireRole(User)]`. `{ state?, under?, maxRuns?, maxSpend?, maxTokens?, maxTokensPerHour?, untilAt? }`, all strings, the bulk rule throughout. An agent that could raise its own `--max-spend` could raise its own budget |

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
left, and how long until it comes back — a reading of a Claude account
specifically, drawn only for a runner whose agent is `claude`, and absent
for `codex`, `grok` and `ollama`. It is read from every page without
opening another app, and it is drawn from `GET /api/utilization`.

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
`RunnerHeartbeatRequest` — and, while an increment runs, on the claim
heartbeat too (`usage` and `usageReadAt` on `ClaimHeartbeatRequest`, written to
the holding session, or the one runner row the beat kept live, only when that
record holds nothing newer), a third
carrier beside the `/usage` probe and the stream. Hatch keeps it against the
person the calling key belongs to, the same resolution `--mine` already takes
(`IActorDirectory.PrincipalAsync`). Bob's runner's numbers never appear under
Alice's name, because each is kept on Bob's and Alice's own rows and sessions
and the endpoint only ever answers the caller's own: it reads the person's
runner rows and the sessions of the keys they own, and the freshest whole
reading wins.

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
for this — `GET /api/utilization` answers exactly as it always has; the
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
than one turn finding out. A CLI that takes `/usage` as a model turn rather
than a local command pays for that find-out once: the probe latches off for
the rest of this process, and the verdict is remembered beside the per-user
config (`hatch config --show` says where), keyed to this CLI's own
`--version` line, so the next restart inherits it rather than paying again.
Upgrading the CLI probes once more, under its own new version. It answers
the same two windows the stream does, plus a scoped weekly window named by
the account and a fourth Hatch has never carried before, `extra` — the
account's own extra-usage balance. The CLI's own schema calls this shape
experimental, so a login it cannot read from — no plan, or a token without
the profile scope — gets nothing back, and the panel simply
keeps whatever it already knew. A reading from either source still replaces
the whole of what the heartbeat sends, never a per-window merge, so the number
that used to move only while a session ran now moves while the loop is merely
up. The same probe, on the same two-minute clock, also runs through a
running increment, session or `--quiet`: a stream's `rate_limit_event` may not
arrive for minutes, so the claim heartbeat would otherwise carry a reading that
ages past what the board trusts. A probe that answers nothing leaves whichever
reading is already there.

Where somebody has two runners, **the freshest whole reading wins**, not a
per-window merge. One person has one Claude login, so two runners are two
readings of one account, and stitching a session percentage from one against a
weekly one taken an hour apart would be showing a number nobody's account ever
had.

### What the endpoint answers

`GET /api/utilization` is `[RequireRole(PersonRole.User, AcceptKey = true)]` like the
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

Every row's colour, and the battery's own, is decided on the client from the
**percentage used** alone, in three bands: green under 50% ("plenty left"),
yellow from 50% to 79% ("over half spent"), and red from 80% ("nearly spent")
(`src/Hatch.Web/apps/hatch/src/lib/utilization.ts`). The percentage is rounded
to the whole number the badge shows before it is compared, so 49.6% is yellow.
How fast a window is being spent against its own clock paints nothing; the time
bar below still shows it. The endpoint sends no colour and no tone: bands on the
client beside a tone decided on the server would be two answers to one
question, so the percentage and the reset instant are the whole of what crosses
the wire. The server's own pace gates, which fold economy and low work for an
unattended pass, are a separate question and are unchanged.

The battery takes the colour of the **worst window** — the highest percentage
among the rows — never the `extra` row's: a spent credit line is a monthly
budget the operator chose to buy, not a window the account will refuse on, and
painting the nav red over it would say "stop" about an account that can still
run. A weekly window at 90% therefore reddens a badge that shows the session at
10%. The battery's tooltip and accessible name say in words what the colour says
in colour, and name the window that decided it, with its percentage, whenever it
is not the session's own.

A window whose reset instant has already passed reads as **not started**: 0%
used, the lowest band's green, and a phrase saying it has reset, rather than the
stale percentage sitting there in whatever colour it last was. Being 0, it
cannot be the worst window.

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
nothing upstream left for it to bypass. The page itself re-reads every thirty
seconds, and when a tab is brought forward, so the battery follows a runner
within about a minute.

### The credentials, still

`ClaudeSubscriptionToken` — the secret-valued site setting on Hatch's own
**Settings** page — no longer feeds the battery, but it has not left; two
more sit beside it, `CodexApiKey` and `XaiApiKey`, one Settings field per
agent. All three are what the *container* runner's entrypoint
(`containers/hatch-runner/`) reads to authenticate whichever agent CLI
`HATCH_AGENT` starts, via `hatch runner-credential <agent>` and
`GET /api/settings/credentials/{agent}` (`/settings/claude-token`
still answers too, for an older container). An install whose runners all run
on somebody's own machine needs none of this. All three are stored the way
the Anthropic key already is: obfuscated at rest,
redacted on read, never leaving the API — and, like every other credential in
Hatch, each is the operator's own to supply ([`docs/ethos.md`](ethos.md)).


## What is waiting on you

Beside the battery, at the right end of the same bar, is the other thing the
bar is for: whether the loop has stopped and is waiting on a person. Two
things stop a night — a pull request nobody has reviewed, and a question nobody
has answered — and the server already knows both. The control is quiet while
neither is true and loud the moment either is, and pressing it hands over the
links that unblock them.

The panel draws two groups, in this order: **Human** — what the goose
found, headed by a row saying so when the goose itself has stopped, then a backup that is
failing or stale, then deadlocked issues, then trunk builds that fail, then
pull requests to review, then pull requests approved but not merging, then
issues landed but not done, then questions to answer — and **Agent** —
branches that conflict, then branch builds that fail. A pull request only ever appears under one of the two:
one the loop is still working through a conflict or a red build on is not a
person's to look at yet, so it moves out of the pull-request section and into
the group naming what is holding it back, rather than sitting in both. A
merged pull request leaves the review half on its own: advancing the issue
takes it out of `/api/work/review` and out of `/api/attention`
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

`GET /api/attention` answers every list in one read, carrying
`[RequireRole(PersonRole.User, AcceptKey = true)]` like the rest of the module:

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
  "runnerFailures": [
    { "name": "build-box", "where": "host:/checkouts/one", "kind": "forge",
      "why": "gh: token expired", "beganAt": "2026-09-28T19:40:00Z" }
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
  ],
  "noCodeHeld": [
    { "key": "AER-18", "title": "A task that needed no change",
      "type": "task",
      "reason": "the setting was already what the ticket asked for" }
  ],
  "waitingOnARunner": [
    { "key": "AER-19", "title": "A story that runs on fable", "type": "story",
      "move": "todo", "missing": "no live runner offers fable" }
  ],
  "runnerCapacity": [
    { "projectKey": "AER", "steps": 3, "keys": ["AER-20", "AER-21", "AER-22"],
      "takers": ["host:/a"], "reasons": [] }
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

`deadlocked` is every issue the work queue marks [deadlocked](#deadlocked),
judged board-wide and listed in the queue's own order: priority tier, then
columns right to left, then `(rank, id)`, not rank order across the board.
`sentence` is the queue's own, starting `deadlocked - `. It is listed whatever
the board's shape - an empty list, never absent, on a board with no edges and
on one with no review column - because it is read from the same unnarrowed scan
the queue marks its rows with, so the panel and the queue cannot disagree.
Counting it towards the badge is the panel's own change, not this read's.

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

A row in `reviews` whose project has *A passing check approves the merge* on,
and whose move from the review column into the merge column a check row
covers (`Columns.Approves` names the column; `Dispatch.MatchAsync` matches the
row there by the issue's type and shape, and the playbook found is itself
ticked `IsCheck`), is left out the same way a pre-approved row the judge would
carry is - a check is what clears it, not a person. A row no check row covers
stays listed, since a person is who unblocks it: by writing the pull request,
or by dragging. It counts towards nothing else, the same as the pre-approval
carve-out immediately above it - not `reviews`, not `reviewsHeldBack`, not the
badge, which follows the list.

`noCodeHeld` is every issue in review that declared it was finished with no
code (`PUT /issues/{key}/no-code`) and is neither [pre-approved](#pre-approval)
nor in a project whose `NoCodeCloses` is on: no pull request will ever be
recorded for it and the loop will not close it then, so it stands in review,
folded with *finished with no code - a person closes it*, for a person to
close. `reason` is the declaration's own. It is not counted in
`inReviewWithoutPullRequest` and is not a row in `reviews`. A pre-approved
declared issue, or one under the setting, is closed by the board with no person
and no session - the close hop, with a comment saying *finished with no code*
and the reason - and what `depends` on it is dispatched on the next pass; it is
not listed. A declared
task under a story that shares one pull request is not waiting on the story's:
it has nothing on that branch.

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
  whose issue stands outside a terminal column, or which was asked at
  `merge` — so the count on the bar,
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
the section reads it the same way. The panel draws it after Backups and
Deadlocked in the Human half, as *Trunk builds that fail*, naming the trunk as
the runner reported it and the repository, the short sha, and every failing
check as a link that opens its job in a new tab.

**It counts, unlike a branch's conflict or its failing build**, because the
reasoning that keeps those two quiet does not hold here — except that it now
holds partway: nothing is dispatched at a trunk, so a failing one has no agent
already on it, but a row reaching this list may already have been re-run, and
may already be filing itself, with no agent and no person either
([below](#checking-the-branches-in-review)). What a person still does is press
**File a bug** where nothing has, which is what the badge exists to surface.

Each row carries a button, unless it already carries a bug's key as a link in
its place — `POST /api/trunk-builds/{id}/bug`
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

### The backup that is failing or stale

`backup` is `{ sentence, lastSuccessfulAt }` or null, read off the same
`BackupService.Attention()` call as everything else here — never a second
poll of its own. First match wins: a path configured and the last attempt
`failed` carries its own error as `sentence`; a folder that cannot be listed
with no copy ever written carries the folder's own problem; a newest copy
older than twice the job's 24-hour interval, or no copy at all once a
five-minute startup grace has passed, is stale. The stale sentence is *no
backup in the last 48 hours*. `lastSuccessfulAt` is the newest copy's own
stamp, never the last attempt's, and null when there is no copy.

The folder listing underneath is cached for a minute and dropped the moment
any attempt finishes, so a backup that just ran is seen on the very next
read. It counts towards the badge like `approvedHeld` — nothing in the loop
will fix a folder, so a row here is a person's, every time. No path
configured (`Hatch__BackupPath` unset) and a healthy backup both answer
null, and the badge is unchanged either way.

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

An issue with a [waiting pull request](#a-pull-request-waiting-to-be-opened) is
not counted in `inReviewWithoutPullRequest` either: its pull request is written
and only needs opening, which is a fact the issue page carries, not a thing
forgotten. It is not a `reviews` row, because those need a recorded url.

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

### Runners that are failing

`runnerFailures` lists what a runner cannot do right now, for the person who
can fix it. Each entry is the runner's `name`, its `where` (the checkout, or
null where none was named), the `kind`, the `why` (what the tool said, or
null) and when it `beganAt`. The kinds are the five a runner keeps (see
[failures on a runner](#runners-on-the-board)): `forge`, `signing`,
`agent-signed-out`, `agent-not-starting` and `board-paused`. It is read the
way the Runners page reads a runner's current failures, so there is one
definition of current: an ended failure, and a runner that has gone quiet,
are simply not in the list at the next poll. Nothing is pressed, nothing is
written, and nothing sweeps. Longest-standing first, then by name, then by kind.
The list is empty, never absent, and is read whatever the board's shape.

Who is sent an entry is narrower than who may see the runner on the Runners
page, where any signed-in person sees every runner:

- A person is sent the failures of runners whose key is theirs.
- An admin is also sent those of runners whose key belongs to nobody.
- Where the wall is off, everybody is sent all of them.
- A key reading the route is sent what its owner would be. A key that belongs
  to nobody is sent those of runners that belong to nobody.
- Nobody else is sent the row at all. The Runners page still shows every
  runner's failures to whoever may read that runner.

Unlike a runner out of usage, a failure is a thing to fix, so each entry
counts towards `attentionCount` and lights the control. The accessible name
says `1 runner is failing`: it counts runners, not entries, so two failures on
one runner are two in the count and one runner in the sentence. There is no new
pill, and `hatch board` does not change.

The panel draws a **Failing runners** section above **Out of usage** and above
*Human*, only when it has rows. A row names the runner and its checkout, says
what it cannot do and since when in the viewer's own time zone, and what the
tool said, then what a person does about it:

| Kind | What a person does |
| --- | --- |
| `forge` | Sign `gh` in on that machine. |
| `signing` | Fix signing on that machine, or allow unsigned commits on the project. |
| `agent-signed-out` | Sign the agent in on that machine. |
| `agent-not-starting` | Read what the agent said, on the runner's page. |
| `board-paused` | Read why on the runner's page, where the pause can be lifted. |

A click anywhere on a row opens that runner's page. There is no way to dismiss
or snooze a row: it goes when the runner is well again. The
[troubleshooting](troubleshooting) page has an entry for each kind.

### A move no live runner can take

`waitingOnARunner` lists every issue whose next move is clear, but that no live
runner can take: every live runner's own declaration folds it on the
[model fold, the refusal fold, the dependency fold, the signing fold and the forge fold](#what-a-pass-skipped), and on nothing
else. A runner is live when it is being heard from, running, and not out of
usage - the same test a peer is held to when one runner's let-go ticket is left
for another. Each entry is `key`, `title`, `type`, `move` (the column the move
starts from) and `missing`, ordered by tier, then rank, as the queue is.

The list is empty when no runner is live - the existing "no runner" condition
says that - and when every gap is met by some live runner. A runner that never
declared models or what it has is never counted as lacking them: one that
declared neither, and refuses nothing, empties the list, and a check is skipped where no live runner
declared that capability. `missing` takes one of three shapes, joined with `; `
when a move needs more than one:

- `no live runner offers fable` - the move runs on a model alias no live
  runner offers; or `pinned to <id> - no live runner's agent is <agent>` for a
  pinned id. A live runner that [refuses](#runners-on-the-board) the model
  does not offer it, whatever it declared: `no live runner offers <model>`
  is said, for an alias or a pinned id, when every live runner that could
  take it refuses it or folds it on the model fold - and a runner that never
  declared models and refuses nothing is still never counted as lacking them.
- `no live runner has pwsh 7 or newer and playwright-browsers` - the
  project's bound repositories declare dependencies no live runner's `has`
  satisfies; an
  invalid dependencies file is named as it is.
- `no live runner signs its commits` - the ticket's project allows only signed
  commits, and no live runner's signing is `works`. Unlike the two above, a
  live runner that never said whether it signs counts as not signing, on
  purpose: a project that wants signed commits wants a runner known to sign,
  so an unprofiled fleet is spared the scans only where no project allows
  only signed commits.
- `no live runner can open a pull request` - the move ends in a pull request
  (see item 12d of [the dispatcher](#the-dispatcher)), and every live runner has
  said its forge sign-in `fails`. The reverse of the signing shape: a live
  runner that never said is not judged, so one such runner, or one that said
  `works`, empties the entry. Every runner in service is older than the
  check, and counting silence as failure would list work no runner has been
  asked about.

A split fleet lists nothing, deliberately: where one runner lacks the model
and another lacks the tool, no single sentence is true of all of them, and the
move may yet be taken once either is fixed.

### Runner capacity

`runnerCapacity` answers a different question than the one above: not *can
any live runner run this move at all* (a model or a tool), but *which live
runner, right now, could pick up this clear step*. A runner capable of the
move can still fold it on a claim, a full WIP slice, an open question, a
person, or a dependency edge, and those folds are a runner's own business,
not a capacity gap - and neither is a row left for a [preferred
runner](#the-dispatcher), because capacity reads the peers' own scans, which
never apply that deferral - so the two lists never disagree: `waitingOnARunner`'s
own rule is unchanged, and this one only narrows further, past the rows it
already lists.

**Judged rows.** Only the board-wide scan's own clear rows count - `Blocked`
null or `BlockedKind` `turn`, the rule `Dispatch.TakeableByAsync` already
uses - and, of those, only a project's implementation steps: `From.Id` is
the board's implementation column (the one `IsImplementation` marks, or, on
a stock board, the one just left of review) and the row is not a hop. A row
folded for everyone - a claim, a full WIP slice, a question, a person, or a
dependency edge - is not a capacity question at all and carries nothing.

**Live, eligible runner.** The same fleet `waitingOnARunner` is judged
against: a runner being heard from, running, and not out of usage, whose own
declaration carries no refusal - the test a peer is held to when one
runner's let-go ticket is left for another. A beat and a live session of the
same key are two peers sharing one name; a name takes a step if any of its
peers does, and its reason, when it cannot, is its first peer's. With no such
runner at all, nothing is judged and the list is empty, the same as
`waitingOnARunner`'s own *no runner is live* case.

**The reason.** Verbatim, the fold's own sentence for that peer's scan row -
no prose written for this endpoint. A runner that refuses the step's model reads
`<key> in <column> runs on <model> - <runner> refuses it`, and is no taker. A step outside a peer's own declared
scope reads `outside its scope: <under>`; a peer whose own scan failed uses
its failure text instead.

A project is listed only when the union of takers across its clear
implementation steps holds at most one name - two or more runners able to
take them makes it information nobody needs, not a gap. Each row carries
`projectKey`, `steps` (how many clear implementation steps the project has),
`keys` (the first ten, in rank order), `takers` (the union - zero or one
name, never more) and `reasons` (one `{runner, reason}` per live runner
outside `takers`, from the project's highest-ranked step that runner cannot
take). Ordered by project key, empty - never null - when nothing qualifies
or no runner is live.

## The leaderboard

The battery says how much Claude is left. The leaderboard says **what the nights
cost, and on what** — which is a question account-wide utilization cannot answer
at all, because it is honest and anonymous and no arithmetic over it can say
which epic ate the evening.

Every number on the page comes from one table: `hatch.work_log_entries`, the row
the dispatcher writes at the end of every unattended increment. It carries the
session id, the issue, both instants, the duration, the turns, the four token
counts, the notional USD — null when the agent reported no price — whether
the run errored, and what the session said it did in its `work-log` block. The
duration and the turns are summed over every turn the session ended, not just
the last: a session woken by a background task between turns ends more than
one, and each adds to the row rather than overwriting it.
For a repo-sourced project's session, the row
also carries which of the three layers its playbook came from, and, for the
file, the trunk, the sha and the hash it was confirmed against at spawn -
null on a table-sourced row and on a row an older runner wrote. It also
carries which agent ran it, an id like `claude` - null on a row a CLI built
before this shipped wrote, and never guessed by the server. Six more say what
the session ran under: the playbook's key, the two columns of the transition it
was spawned for, the issue's type, and the model and effort the playbook asked
for - null on the same two kinds of row, a table-sourced one and an older
runner's, and likewise never guessed by the server.
[`by-playbook`](#what-each-playbook-costs) folds on them. **No Claude
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
GET /api/work-log/sessions?from=&to=&ancestorKey=&sort=&limit=&outcome=
GET /api/work-log/history?from=&to=&ancestorKey=&bucket=&offsetMinutes=&outcome=
GET /api/work-log/by-playbook?from=&to=&ancestorKey=&projectId=&outcome=
```

The first two are what the page draws; the third is [what each playbook
costs](#what-each-playbook-costs).

All three take the same range and the same `ancestorKey`, and **`ancestorKey`
means the same thing on all three**: the issue itself *and* everything
beneath it at any depth, from `Rollup.DescendantIdsAsync`. That is
deliberately not how the same word reads on `GET /api/issues`, and the reason
is that a planning session run against an epic is money no child holds. It is
also what keeps the ranking, the table and the graph from ever describing
different populations.

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
key, and `POST /api/issues/{key}/work-log` refuses a person outright — see
`IssueWorkLogController.NotAKey`.

### What each playbook costs

`by-playbook` folds the same rows a third way: not along the hierarchy or a time
axis, but by what the session ran under and how it ended. A row is one
combination of eight fields - `PlaybookKey`, `PlaybookFromStatusId`,
`PlaybookToStatusId`, `IssueType`, `Model`, `Effort`, `Outcome`, `Reason` - with
its sessions, errors, turns, the four token counts and `TotalTokens`.

- **Every older-runner session is one row.** A session written before the six
  playbook fields existed has all six null, and null is a value to group on like
  any other: those sessions fold together as a single "unknown" row, with no
  branch in the fold that treats them specially. A session with no outcome or
  reason groups under null for those two the same way.
- **`outcome` narrows the read.** `history`, `sessions` and `by-playbook` all
  take an optional `outcome`: one of the claim outcomes, or `none` for
  sessions with no outcome. Given, it narrows the buckets, the sessions
  listed or folded, and the totals with them, as `projectId` does; absent,
  every session is read. A value the route does not know is a `400` naming
  the accepted ones. It leaves `history`'s and `sessions`' `first`/`last`
  bounds alone, which describe the log and not the range.
- **The same window, the same `ancestorKey` and the same `projectId` as the
  other reads.** The range is the half-open `EndedAt` rule `sessions` and
  `history` use, `ancestorKey` reads exactly as it does on those two reads -
  the issue itself and everything beneath it, an unknown key a `400` - and
  `projectId` reads as it does on `GET /api/issues` - an id that matches no
  project answers no rows, not a refusal.
- **Status names are today's names.** `FromStatusName` and `ToStatusName` are the
  board's *current* names for the stored ids, resolved fresh on every read, and
  null where the id is null or no longer names a column. Neither is a re-read of
  the playbook's own live prompt or row: `PlaybookKey` is what was stored when
  the session ran, and the read does not look it up again.
- **`CostUsd` is a floor.** It sums the priced sessions in the group only, and
  `UnpricedSessions` counts the rest, the same convention `Fold` uses for the
  other two reads.
- **The context and prompt figures are over what was reported.**
  `MedianPeakContextTokens`, `MaxPeakContextTokens` and `MedianPromptChars` take
  each group's non-null values only, and are null when none in the group
  reported one. A median of two values averages them; of three, it takes the
  middle.
- **Every group comes back, heaviest first.** There is no sort parameter and no
  limit. Rows are ordered by `TotalTokens` descending, ties broken on the eight
  grouping fields in the order above, `Outcome` and `Reason` last, so two reads of one window agree on their
  order.

## The goose

The goose is the server watching the board by itself. Every
`Hatch__GooseIntervalSeconds` (default 60; zero or less falls back to 60) a
sweep runs with no runner and no browser connected, and the fact that it ran
is kept, so a person can see the watcher is alive. A sweep asks each registered
check what it looked at and what it concluded and records the answers. A sweep
with no registered checks still records a run.

**The job.** `GooseJob` is an `IAppJob`, scheduled like the backup and the
reflection cadence by `JobsInit` on the clustered Quartz store and registered in
`HatchModule`. Its context has no request, so it reads every project
([how scope is enforced](#how-scope-is-enforced)). Checks implement
`IGooseCheck { Name; RunAsync(ct) }`, returning a `GooseObservation(Looked,
Concluded)`; they run in container registration order. A check that throws does
not stop the sweep: its entry is marked `failed` and carries the message, and
the others still run. The job never throws, because Quartz fires a job again
when it does; a failure is a log line. Time comes from `TimeProvider`, so a
test drives it with `FakeTimeProvider`.

**One sweep at a time.** `[DisallowConcurrentExecution]` stops Quartz firing
the job while it runs, and a trigger that falls due meanwhile is skipped or
fires once late, never queued in a pile. That is not enough across instances,
which share one scheduler instance id, so a sweep also takes a Postgres
advisory lock (`pg_try_advisory_lock`) on a connection of its own, held for the
whole sweep behind the `IGooseLock` seam. A sweep that does not get it writes
nothing and runs no check. A process that dies mid-sweep drops the lock with its
connection.

**The record.** Each sweep is stored as a run in `hatch."GooseRuns"`
(`EfHatchGooseRun`): `FirstRanAt`, `LastRanAt` (indexed), `Sweeps`, `LongestMs`
and `Entries`, a JSON list of `{ check, looked, concluded, failed, error }`,
free-form on purpose so a check needs no new column. The table belongs to no
project and has no foreign key into one, so it has no query filter and
`QueryFilterModelAuditTests` does not list it; the two routes are what keep a
bounded caller out.

**The fold.** A sweep is compared with the run of newest `LastRanAt`. Equal
means the same checks, in the same order, with the same `failed`, `concluded`
and `error` for each. What a check *looked* at, and every timing, never
counts. Equal: that run's `LastRanAt` moves to this sweep, `Sweeps` counts up,
`LongestMs` takes the larger, and `Entries` is replaced with this sweep's. Not
equal: a new row with `Sweeps` 1. Sixty identical sweeps are one row, and a
return to an earlier conclusion is a new row, since the fold looks only at the
newest.

**Retention.** Each sweep deletes the runs whose `LastRanAt` is older than
`Hatch__GooseKeepDays` (default 7). Zero keeps everything; a negative falls
back to 7. Both options are guarded in `GooseSettings`.

**The stopped goose.** `GET /api/attention` carries `goose`, `{ lastSweepAt,
intervalSeconds, stopped }`, beside the open findings. `AttentionController`
judges `stopped` on its own clock, `TimeProvider`'s: true when the newest
`LastRanAt` is more than three intervals old - strictly more, so exactly three
is still running - or, with no run stored, when `GooseWatch.StartedAt`, a
singleton recording when this process started, is. The sweep writes nothing for
it, so a sweep that has hung cannot report itself well. It reads `GooseRuns`,
which has no query filter, so a narrowed caller is told too.

**The routes.** `GET /api/goose/runs` and `GET /api/goose/runs/{id}`, both
`[RequireRole(PersonRole.User, AcceptKey = true)]`. A run holds what was seen in
every project, so for a caller whose scope is not everything the list is empty
and a run reads as not found. The reader's page is
[The goose](../src/Hatch.Web/apps/hatch/docs/the-goose.md).

`GooseRunDto` carries `Checks` and `ChecksFailed` beside the sweep tallies, so
the list can say how many checks a run holds without a detail call per run.
`ListRuns` reads the rows and counts in memory from `GooseEntry.Read`, as
`ListFindings` does for evidence.

**The page.** `/goose` (`GoosePage.tsx`, HA-2502), a row in the nav's Agents
group, reads the three routes above and draws, in order, a line for the goose's
state (from `GET /api/attention`, which the app already polls), open findings,
runs and closed findings, on the attention control's own interval. What can be
decided without a browser lives in `lib/goose.ts`: durations, the tallies on a
run's row, how many findings were open at a run's last sweep (opened by then
and not closed until after), and ordering. A run opens in place to
`GET /api/goose/runs/{id}`. A finding opens to its evidence; one with a bug also
reads `GET /api/issues/{key}`, its `work-log` and its `events`, so the bug's
description is the brief and the work log is what each session reported, and
nothing is stored a second time. A work log row names the tool, not the runner,
so the runner is the `runner` on the `claim_released` event whose `sessionId`
matches the row's; a session with no such event shows none. A caller whose
`GET /api/me` scope is not `all` is not sent for runs, and the page says why.
The file-a-bug button is `FindingBugDialog`, shared with the attention panel.

### Findings

A finding is a stored row a person can read and act on, with the evidence
beside it - `train-ready` below is the first check that opens one, `ready-unclaimed`,
`ready-no-runner`, `claim-repeating`, `questions-waiting` and `standing-still`
follow it, and a person files a bug from one (**Filing a bug**, below). `FindingCheck` (`GooseFindings.cs`) is the one
reconciler every such check shares, so none of them can disagree about when a
clock starts, when a row opens, or when it closes: a subclass says only what it
is watching (`ReadAsync`: a sentence for the run, the check's own bound, and
every condition currently true, one per subject) and, once a condition has
stood its bound, what to say about it (`DescribeAsync`: a sentence and a list
of `{ label, value }` evidence).

**The clock.** `hatch."GooseSightings"` (`EfHatchGooseSighting`) holds when a
condition began, for a check and subject, before it has stood long enough to
open a finding - unique on `(Check, Subject)`. The first sweep that sees a
subject's condition creates its row; a sweep that sees the same subject with a
different token (what the condition is *of* - a head's key, for `train-ready`)
restarts the clock, because the condition it was timing has ended and a new one
has begun; a sweep that no longer sees the subject at all deletes it. A pass-over,
a `brought_current` mark or a changed driver are none of these - the token is
the head's key alone, so none of them touch the clock.

**The row.** `hatch."GooseFindings"` (`EfHatchGooseFinding`) holds the check's
name, the subject, the token the condition was of when the finding opened,
the project it names or none, `BeganAt` (copied from the sighting and never
touched again), `OpenedAt`, `ClosedAt` (null while open), a `Sentence`, a jsonb
`Evidence` list, and a nullable `BugIssueId` - in the table from the start, so
a later check that files a bug needs no migration of its own. At most one
finding is open (`ClosedAt` null) for a given check and subject: a filtered
unique index on `(Check, Subject) WHERE "ClosedAt" IS NULL`, the database's own
enforcement. `GooseFindings.FiledFromAFindingAsync(db, issueId)` answers
whether an issue was filed as some finding's bug, the one helper every later
check reads so each agrees on what "filed from a finding" means.

A condition that has stood its bound opens a finding, `BeganAt` carried over
from the sighting; every later sweep the condition is still true refreshes the
open finding's `Sentence` and `Evidence` and never its two start times. A sweep
that stops seeing the condition - the subject is gone, or is now a different
token - closes it. A check that throws reads first and mutates only
afterwards, with no `await` in between, so a throw anywhere in the read half
touches neither table; `GooseJob` already catches it and tries again next
sweep.

**Scope.** Both tables reach a project and carry a query filter
(`HatchContext.OnModelCreating`): `Unbounded || (ProjectId != null &&
Visible.Contains(ProjectId.Value))`. Unlike a global playbook or an unlinked
dream, a finding or a sighting naming no project is nobody's but an unbounded
caller's - a check that names no project is not everyone's the way a global
playbook is.

**Retention.** Each sweep deletes a closed finding once it has been closed
longer than `Hatch__GooseKeepDays` and its bug, if it has one, stands in a
terminal column; an open finding is never deleted, however old.

**`GET /api/goose/findings`**, `[RequireRole(PersonRole.User, AcceptKey =
true)]`, the same as the run routes. Open findings first, longest-standing
first, then closed ones, newest first; `?open=true` narrows it to the open
ones. Each carries its id, check, subject, project key, sentence, evidence,
its three times and the bug's key. See the
[API reference](../src/Hatch.Web/apps/hatch/docs/api-reference.md).

**Filing a bug.** `POST /api/goose/findings/{id}/bug`
(`GooseFindingBugController`, HA-2516) hands a finding to an agent: it files a
bug carrying the finding's evidence and stores it on the finding
(`BugIssueId`). `[RequireRole(PersonRole.User)]` on the method, no role on the
class, and no `AcceptKey`, so a key is refused - a key filing work for itself is
the loop the goose must never become, and nothing in a sweep calls this route.
It is in `AdminSurfaceTests`' `PersonOnly`, not its `AcceptsKey`. The body is
optional: `priority`, `statusId` and `preApproval` are the trunk build route's
person branch, with its defaults (expedited, none, the leftmost column), and
`preapproved` asks an owner or approver of the project; there is no `express`.
The bug goes to the finding's own project, and a `projectKey` naming another is
a `400`. A finding that names no project (`questions-waiting`) needs
`projectKey`, a project the caller can see, else `400`; a caller whose scope is
narrowed never reaches such a finding, since its query filter hides it. A
closed finding is filed like an open one, and its brief says the problem cleared
and when. Filing is `IssuesController.CreateIssueAsync`'s, so the bug's number,
rank, column and `created` event are the ordinary ones. **Two calls, one bug:**
on Postgres the route holds an advisory lock on the finding (two 32-bit keys,
a constant and the id) on a connection of its own for the whole call, not a
transaction - `CreateIssueAsync` retries a number collision, which an aborted
transaction cannot - and reads `BugIssueId` again after taking it; a second call
answers the bug already filed and files nothing.
`GooseFindingBugConcurrencyTests` fires two at once against a real database.
The title and the brief come from `FindingBugs`, one entry per check, which a
test requires of every `FindingCheck`: the sentence, when the condition began,
the evidence one line each, the check and its bound in words, and one line asking
for the cause and then a fix or what a person has to do. It names a project key
and a subject, never an address of this installation. `GooseFindings.FiledFromAFindingAsync`
is what keeps every check from counting a bug filed this way.

**In the attention read.** `GET /api/attention` carries the open findings as
`findings`, longest-standing first (`BeganAt`, then `Id`), read through the same
query filter as the route above, so a narrowed caller gets their projects' rows
and never a project-less one, with no second filter to drift from it. Each adds
`IssueKey`, derived and never stored: the finding's token when it parses as an
issue key whose project half is the finding's own project, else null, and the
panel then links the row to the project's board. A `standing-still` finding's
token is a column and a claim, so its `IssueKey` reads the subject instead, held
to the same project rule. The attention panel draws them
first under **Findings**, and each row without a bug has a **File a bug** button
that posts to `/api/goose/findings/{id}/bug`. A stuck merge train can show twice
there, as the board's own row under **Stalled merge trains** and as a `train-ready` finding;
the two are different watchers and neither hides the other.

#### `train-ready`

The goose's own clock on a stuck merge train, beside [the merge
train](#the-merge-train)'s own stall watch, which only ever passes a driver
over. Its subject is the project; the condition is that the project's train
has a head (`Dispatch.TrainHeadsAsync`) judged `merge` or `update`, and the
token is the head's own key. The bound is `Hatch__GooseTrainReadyMinutes`
(default 30 - three of the train's own `Hatch__TrainStallMinutes` windows, so
the board's own pass-over goes first); zero turns the check off, and a
negative falls back to 30.

It needs no driver: a ready head with no live, eligible one is the same
standstill, and the finding opens either way. A head filed from a finding
opens no finding of its own. The sentence names the project and the head and
says nothing has merged, for example `AER's merge train is standing: AER-12
is ready to merge and has not` - it carries no duration, since whoever reads it
can work that out from `BeganAt`. The evidence carries the head's key and its
judgement in the queue's own words, the driver or that none is eligible, when
the driver last wrote an event on an issue in the project's merges column (or
that it never has), how many other rows share the merges column, and how many
times a driver has been passed over on this head since the condition began,
with which runners.

#### `ready-unclaimed` and `ready-no-runner`

Tells a person when ready work has sat unclaimed - once while a runner that
could take it sits idle, and once while no live runner can take any of it at
all (HA-2497). Both share one reader, `ReadyWork`, so a sweep scans the board
once rather than twice and the two checks can never disagree about what is
ready.

**What is ready.** `Dispatch.ReadyBoardAsync` is the board's own read, beside
`TakersAsync`'s: a row the walk calls clear, or parked only behind
higher-ranking work (`Blocked is null || BlockedKind == Turn`), that is not a
hop and whose `Kind` is `advance`, `conflicts` or `build` - the merge train's
own kinds (`merge`, `update`, `pushback`, `hold`) are the train's rows, not a
session's to claim. For every such row it also names which live peers - by id,
not only by name, since a name's two sessions can hold different checkouts -
take it, the same way `TakersAsync` does, plus the live fleet itself and every
runner this Hatch has ever heard from, live or not. `ReadyWork` then drops a
step filed as the bug for another finding (`GooseFindings.FiledFromAFindingAsync`),
groups what remains by project, and reads each project's newest `claim_taken`
event and, from `Runners.HeldAsync`, which of its able peers hold no live
claim at all - idle, in other words.

**`ready-unclaimed`.** Subject is the project; a candidate when it has ready
steps and an idle peer able to take one of them. The token is the project's
newest `claim_taken` event's own id, or `none` - a claim taken and released
again between two sweeps still changes the token, so the clock restarts with
no bookkeeping of its own. The bound is `Hatch__GooseUnclaimedMinutes`
(default 10); zero turns the check off, and a negative falls back to the
default. The sentence names the project and the count, for example `AER has 3
steps ready and no runner has taken one`; the evidence names the steps (the
first ten keys, and the count), each idle runner and when it was last heard
from, and the project's newest claim, or `none`.

**`ready-no-runner`.** Subject is the project; a candidate when it has ready
steps and no live peer takes any of them - including a project with no live
peer at all. The token is a constant: the condition is "ready and taken by
nobody," not any one step or claim. The bound is `Hatch__GooseNoRunnerMinutes`
(default 15); guarded the same way. The sentence reads `AER has 3 steps ready
and no live runner can take them`; the evidence names the steps the same way,
then, for each live runner, the distinct reasons the queue gives across the
project's steps, joined; with no live runner at all it says so and lists when
each known runner - live or not - was last heard from.

The two never both open for one project: an idle taker makes it
`ready-unclaimed`'s, and having none at all makes it `ready-no-runner`'s. Unlike
the attention panel's **Runner capacity** and **Waiting on a runner**
([watching the loop](../src/Hatch.Web/apps/hatch/docs/watching-the-loop.md)),
which only ever say what is true right now, these say a project has stood
that way for a while.

#### `claim-repeating`

One runner claiming one ticket again and again with the ticket not moving
(HA-2499). `LetGo.Count` skips several release outcomes (`unservable`,
`abandoned`, `unstarted`, none, and a drop marked `SpentNothing`), so a runner
can take one ticket many times and no counter on the board moves; this check
counts `claim_taken` events instead, whatever outcome the release carried
(`RepeatClaimsCheck.cs`). Its subject is the issue's key and its project the
issue's; the token is the id of the streak's oldest claim event, so a further
claim refreshes the same row and a new streak after a break is a new condition.

A **streak** (`RepeatClaimsCheck.Streak.Read`, a pure fold over the trail)
is one holder's `claim_taken` events on one issue, newest first, back to the
first event that shows movement: `status_changed`, a `pull_request_state_changed`
whose `to.headSha` differs from `from.headSha`, a `build_check_changed` whose
`to.sha` differs the same way, or a claim by another holder. Movement newer than
the newest claim means no streak. Unlike `LetGo`, a `resumed` event does not
stop it. The trail shows a new commit only through those two events, so a push
made before a pull request or a build verdict exists is invisible here.

The check is true for an issue when its streak holds at least
`Hatch__GooseRepeatClaims` claims (default 5; zero turns the check off, a
negative falls back to 5) and its newest claim is younger than two hours - a
constant on the check, not an option. The issue must stand outside terminal and
deferred columns and must not have been filed from a finding. The bound is
zero, so a finding opens on the sweep that sees the fifth claim; the condition
began at the oldest claim's time, which the check hands the reconciler as
`FindingCandidate.Began` (every other check passes none, and a sighting then
begins on the sweep that first saw it). The run's entry says how many issues
with a claim in the last two hours it looked at.

The sentence names the runner, the issue and the count, for example
`here:/one has claimed AER-12 7 times and it has not moved`. The evidence
carries the runner, the claims, the first and the newest, the release outcomes
within the streak's span, the issue's column, its stall mark and whether it is
held. The check pauses nothing, marks nothing stalled and changes nothing
`LetGo` counts.

#### `questions-waiting`

The goose's finding for questions left unanswered too long
(`QuestionsWaitingCheck`). It reads `Questions.Waiting` - open, on an issue
outside a terminal column or asked at `merge` - and adds no second rule for what waiting means.
Its subject is `board` and it names no project, so at most one such finding is
open and only an unbounded caller reads it (see **Scope** above). The token is
constant (`past-bound`), so a second question crossing the bound refreshes the
one row rather than opening another; it closes when none is past the bound.

A question counts once `now - asked >= Hatch__GooseQuestionHours` (default 24;
zero turns the check off, a negative falls back to 24), so one at exactly 24
hours opens the finding. The check applies the bound itself by choosing
whether there is a candidate, and returns a zero bound to `FindingCheck`.
A question on an issue filed from a finding is left out. The sentence gives the
count and the bound and no duration, for example `3 questions have waited more
than 24 hours`; the evidence lists every question past the bound, oldest first:
the issue's key as the label, then its title, who asked, when, and the
question's first line. The run's entry says how many waiting questions it
looked at and how many were past the bound. `BeganAt` is the first sweep that
sees a question past the bound - at most one sweep interval after it, the same
clock `train-ready` has. It judges by age alone and suggests or gives no answer.

#### `standing-still`

The goose's clock on a ticket standing still (`StandingStillCheck`, HA-2664),
whatever the cause: the five checks above each watch one named cause, and a
ticket that matched none of them - HA-2636 stood in review nine hours - was
missed. Its bound is `Hatch__GooseStandingHours` (default 4; `0` turns the check
off and closes what it holds, a negative falls back to 4; the guard is
`GooseSettings.StandingBound`).

**What it reads.** The scan `ReadyWork` already makes: `ReadySnapshot.Board` is
the whole `Scan` that `Dispatch.ReadyBoardAsync` kept the ready rows of, folded
rows included, memoised per sweep, so the check adds no second scan. It adds no
rule of its own about what holds a row - it reads the scan's `BlockedKind` and
the review judge's own mark, never the words of a sentence, so a reworded
sentence cannot change what is watched. `ScanRow.WaitsOnApprover` (from
`ReviewWork.Judgement.WaitsOnApprover`, carried by `Blocking`) is true exactly
when the review judge folds a clean row as idle and adds its "waits for an owner
or approver" clause - the issue's effective pre-approval is below
`PreApproved`. Its sentence and its kind (`other`) are unchanged, and a dead end
(HA-2663) is not marked: that row waits on no one, so this check does open on it.

**What is watched.** Leaf rows in the columns from `Columns.Implementation` up to,
not including, the first terminal column, less `Columns.Merges` (which
`train-ready` watches); a deferred column is no lane. Leaf means no child the
dispatcher counts as work: `FamilyGate.EpicOpenChildren` for an epic or
ambition, `FamilyGate.OpenWorkChildren` otherwise. A task waiting on its story's
shared pull request (`FamilyGate.IsWaiting`) is not watched, and so its story is
once every child is done or waiting on it.

**What keeps a row out.** A live claim (`IssueClaims.IsLive` on the row's own
claim, not the row's kind, since an earlier fold can say something else first),
`BlockedKind == person`, `WaitsOnApprover`, and an issue filed as the bug for a
finding (`GooseFindings.FiledFromAFindingAsync`).

**The clock** is the reconciler's own, starting on the first sweep that sees the
issue as a candidate: reading the start from the event trail was rejected, since
the trail does not say when a pause or a question ended. The subject is the
issue's key and the token is `<column id>:<newest claim_taken event id>`, so a
column change or any claim - even one taken and released between two sweeps -
is a new condition that restarts the clock and closes the finding, and a
candidate that drops out loses its sighting. One finding opens per issue, in the
issue's own project, so `AttentionController.FindingsAsync` reads the subject,
not the token, for its `IssueKey`.

The sentence is `<key> has stood in <column> with nothing set to move it`. The
evidence is `column` (the name, and when the newest `status_changed` event, or
failing that the `created` event, put it there), `queue` (the row's sentence, or
`clear`), `last claim` (the newest `claim_taken` event's holder and time, then
the first `claim_released` or `claim_cleared` after it, or `not released`; or
`none`) and `pull request` (the recorded url, or `none recorded`). The check
moves, claims and files nothing. `FindingBugs` titles a bug from it `Ticket
standing still: <key>`.

## Reports

The board says what is where. The reports say **when it was** — questions about
the board's own history, which the board as it stands cannot answer because it
holds only the present column of every issue.

Reports is a top-level area of the Hatch app: a **Reports** link in the primary
nav between **Plan** and **Agents**, and an index at `/reports` with one card
per report, each stating the question it answers. There are five reports with
a card each: the Timeline, the Cumulative flow, the
[Changelog](#the-changelog-report), the [Consumption](#the-consumption-report)
report at `/reports/consumption`, and the [Activity](#the-activity-report)
report. **One further read, the [flow](#the-flow), has no page and no index
card** — it is for a program, not a person at a screen. A new report with a
page is one more entry in that index and a route; no report gets a nav entry
of its own, and the index card stays the only way in.

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
GET /api/reports/timeline?from=&to=&projectId=&createdBy=
```

`from` and `to` are **instants** (`2026-09-12T17:00:00Z`), not dates. With
neither given the range is the 90 days ending now; with only `from` absent it is
the 90 days before `to`. Equal bounds are accepted. `from` is clamped forward
to the first issue's creation when the range reaches back before anything
existed, so a range wider than the board's history does not draw an empty
stretch. The `400`s are an unparseable bound, an inverted one (`from` after
`to`), and a `projectId` that names no project. The route is open to a person
or to a key.

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
reaches the same answer with `hatch api GET /api/reports/timeline?...`,
which is the [API surface](#api-surface)'s row for it.

### The cumulative flow

```
GET /api/reports/cumulative-flow?from=&to=&project=
```

The same question the timeline answers about one issue at a time, asked about
the whole board instead: **how many issues sat in each column, at each
instant**. `from` and `to` are the timeline's own bounds — instants, the 90
days ending now by default, the same `400`s for an unparseable or inverted
range. `project` narrows every count to one or more projects' issues —
repeatable, so two keys answer over both; absent or empty counts every
project, as it always has. A key no project has is a `400` naming it. There
is still no `createdBy`: a column's count is not a fact about any one
creator.

It answers `{ from, to, step, columns, projects, points, unresolved }`.
`columns` is the board's own columns in board order — every status that is
not deferred, so a deferred column contributes to no band. `projects` is the
project keys in scope — every project with an issue the sweep counted when
unscoped, or only those named by `project` when scoped — in the same order
`/api/projects` lists them. `points` is one `{ at, counts,
projectCounts }` per grid step from `from` to `to`, oldest first, `counts`
parallel to `columns` and `projectCounts` parallel to `projects`; on a board
with no issues both are `[]`. The two cuts of the same sweep always agree: at
every point `projectCounts` sums to the same total `counts` does, the
terminal-column baseline below applying to both alike since both come off the
same stretches. The grid's step is the finest of hour, day or week whose
point count stays at or under the 1,000-point cap `/work-log/history` already
uses, falling back to week rather than refusing a range that outgrows even
that. `from` is clamped forward to the first issue's creation, the same as
the timeline's own `ClampStart`, so a range reaching back before anything
existed does not draw an empty stretch. `unresolved` counts stretches the
same way the timeline's does: a stretch naming a column no longer on the
board, because it was renamed since and the event predates the ids — see
[the rebuild, and its one hole](#the-rebuild-and-its-one-hole). A terminal
column — the board's flag, never a name — counts only what closed inside the
window: a stretch there that was already open before `from` contributes
nothing, so the done band starts at zero at the chart's left edge and grows
only as issues close inside it, rather than opening already full of work
finished long before the range being asked about.

The page is at `/reports/cumulative-flow`: one band per column, stacked
bottom-up with the board's last column at the baseline, each in its column's
own colour and named in a legend above the chart. **Nothing on this page
writes**, and the absence is the guarantee rather than a convention, as it is
for [the timeline](#the-timeline). There is **no CLI subcommand** for it
either; a script reaches the same answer with
`hatch api GET /api/reports/cumulative-flow?...`, which is the
[API surface](#api-surface)'s row for it.

### The board health read

```
GET /api/board/health?window=&project=
```

The cumulative flow says how many issues stood in each column; the flow says
what moved and how long it took. This read says **how the board is doing right
now**, in one call and with no page: twenty-four equal bars across a window, so
a program can tell a board that is moving from one that is stuck, and a board
waiting on a person from one waiting on the loop. The route is open to a person
or to a key. The answer is counts and durations, and one list that names the
issues in flight now (`inFlightIssues`, below). Naming them is safe because the
route is open only to a caller who may see those issues, `db.Issues` carries
that caller's project filter, and `project` narrows the list as it narrows every
count; an issue outside the caller's scope never appears, key or title. Titles
are read for the listed issues alone.

`window` is one of `2h`, `4h`, `8h`, `12h` or `24h`, and `8h` when absent; any
other is a `400` naming the five. A bar is a twenty-fourth of it, so 20 minutes
at the default. `project` narrows every count to those projects and repeats; one
that names no project is a `400`.

Bar boundaries are on the UTC clock: the last bar starts at now floored to a
multiple of the bar's width, so a bar does not slide while now is inside it. The
**last bar is read at now**, not at its end, which is still to come.

It answers `{ from, to, barMinutes, bars, total, previousTotal, pastNormal,
hasWorkColumn, runners, inFlightIssues, lastWaitedAt, lastWaitedIsFloor }`. `runners` is [the runner lanes](#runner-lanes), below,
`inFlightIssues` is [the issues in flight now](#the-issues-in-flight-now), and
`lastWaitedAt` is when anything last waited, under **When anything last waited** below.
`total` is the moves right in the window and `previousTotal`
the same over the window before it, for comparison. `pastNormal` is how many
issues with no children now stand in flight longer than their column's
[normal](#the-flow). Each bar carries:

- `start`, `end`;
- `board`, `runner`, `person` and `moved` - the moves **right** made inside the
  bar, split three ways and summing to `moved`. A move is the board's when it
  carries a hop flag or a `merged`, `landed` or `pushback` flag; it is a runner's
  when a claim was open on the issue and its holder made the move; everything
  else is a person's;
- `waiting`, `inFlight` and `done` - read at the bar's end. They are the
  [cumulative flow](#the-cumulative-flow)'s own bands, cut from the same
  stretches and so equal to it at every bar's end: before the implementation
  column, from it up to a terminal column, and closed inside the window. They come
  from the columns' flags, not their names. A board with no implementation column
  has none of the three, and `hasWorkColumn` says so;
- `waitingPullRequests` and `waitingQuestions` - what waited on a person at the
  bar's end, and `oldestWaitSeconds` for the longest of either;
- `oldestInFlightSeconds` - how long the oldest issue with no children had
  stood in flight;
- `ages` - `{ healthy, aging, pastNormal, noNormal }`, four counts of the in-flight
  issues with no children, read at the bar's end (at now for the last bar), each
  by how long it had stood in its column against that column's
  [normal](#the-flow). **healthy** is under half the normal, **aging** is from half
  the normal up to and including the normal, **pastNormal** is longer than the
  normal (the rule `ColumnNormals.PastNormal` gives, the one the card's age chip
  uses) and **noNormal** is a column with none yet. The four add up to the
  in-flight issues with no children, **not** to `inFlight`, which also counts
  parents. The last bar's `pastNormal` equals the answer's `pastNormal`. The seconds
  are compared unrounded, so exactly half the normal and exactly the normal are
  both aging.

#### The issues in flight now

`inFlightIssues` lists every issue with no children that stands in flight now,
longest stay first (ties by when it entered, then by id). A row is `{ key, title,
statusId, statusName, enteredAt, seconds, normalSeconds, band }`: the issue, the
column it stands in and when it entered, how many seconds it has stood there,
the column's normal in seconds or null, and which of the four groups it is in
(`healthy`, `aging`, `pastNormal` or `noNormal`). `enteredAt` is the start of the
stretch, never earlier than the issue's creation. The first row's `seconds` equal
the last bar's `oldestInFlightSeconds`, and the rows with `band` `pastNormal` number
`pastNormal`: the answer's count is taken from the list, so the two cannot
differ. An empty board, or one with nothing in flight, answers an empty list.
**The server caps nothing**, so a page that draws fewer rows says how many it
left out.

The counts and the list come from the same pass over the issues that finds the
oldest in flight; no new read of the status history is made.

**The last bar is the attention control's own count.** Its waiting pull requests
and questions are what [the attention panel](#what-is-waiting-on-you) lists as
reviews and questions, from the same code, so the number a program reads here is
the number the control shows. Earlier bars are rebuilt from the trail and are
exact except where it cannot be.

**Known gaps in the past bars.** A check approving a merge, and a pull request
held back by a conflict or a failed build, count as waiting on a person in a
past bar; only the attention panel knows the difference today. Pre-approval is
walked up the parents an issue has today and ignores the project line a level
may not cross. A question of the merge level, or one awaiting its look back,
ends at a terminal column in a past bar yet counts now. Whether an issue has
children is today's answer, not the answer then. A parent in a project the
caller cannot see carries no level into a past bar.

**Two more approximations.** An earlier bar's `ages` are judged against today's
column normals, not the normals of that time. And whether an issue has children
(so whether it is counted at all) is today's answer, the same as for the oldest in
flight.

**When anything last waited.** `lastWaitedAt` is the instant the latest wait on a
person ended, so a page can say "clear for 3h 20m" where the waiting tiles show
only a zero. It is null while anything waits now - the last bar's waiting pull
requests plus waiting questions is above zero - and when no issue is in scope.
"Waited" follows the bars' own rules: a question that was open, or a pull request
in review that was recorded and not pre-approved. A wait ends at the event's own
instant, not a bar's edge: the question's answer or lapse, the issue leaving
review or entering a terminal column, the pull request record being removed, or
pre-approval being raised on the issue or an ancestor.

The read looks back **30 days** from now at most. When nothing waited in that time,
`lastWaitedAt` is now minus 30 days and `lastWaitedIsFloor` is true, so a page
can say "over 30d". A board younger than 30 days answers its earliest issue's
creation instead, flagged the same way, since nothing could have waited before it.
The value keeps to `project` when it is given, and to the projects the caller may
see; one caller never learns an instant from a project outside their reach.

Two more gaps, beside the ones above. The earlier bars count a pull request that a
check approved, or one held by a conflict or a failed build, as waiting, so this
value can name an instant that the attention control would not. And a wait the
trail still holds open, but the attention control does not list, is left out: it
has not ended, and while the control lists nothing the value is a floor or an
earlier instant.

The status history is never loaded whole. Status changes are read back to the
start of the window before this one, and everything else from the start of this
one; for each issue the newest event before that point is read too, since it
says where a stretch began or whether a claim was open. The 30-day read for
`lastWaitedAt` is the one longer look, kept for each project for up to five
minutes and read only on the caller's own view of the projects; the window's own
answer wins when it is the later one.

#### Runner lanes

`runners` is `{ up, paused, withLane, lanes }`: one lane for each runner that
was up at any time in the window, on the same 24 bars as `bars`, and the counts
a Runners tile heads itself with. `up` counts the lanes whose runner is not gone
now, `paused` those of them paused now (a pause the board imposed counts), and
`withLane` is the number of lanes. **The server caps nothing**: the lanes come
sorted up now first, then heard from most recently (`lastSeenAt`), then by name,
and a page that draws fewer says how many it left out.

A lane is `{ name, up, paused, lastSeenAt, cells }`, and `cells` has 24 entries,
oldest first, one for each bar. A cell is `{ state, why }`, `state` being the
first of these that holds in its bar:

| `state` | When | `why` |
|---|---|---|
| `let-go` | a claim of the runner's was released as `dropped`, lapsed, or was marked stalled, or a work-log session under its claim ended in error | `dropped`, `lapsed`, `stalled` or `errored`; the first of them in that order when a bar holds several |
| `down` | the [state record](#runners-on-the-board) has the runner out of usage or its agent down | `out-of-usage`; `not-starting` or `signed-out`; `agent-down` for a kind not known. An agent down beats out of usage |
| `moved` | the runner made a forward move under its own claim | none |
| `working` | the runner held a claim at any time in the bar | none |
| `idle` | the runner was up and running (stopping counts) and held nothing | none |
| `paused` | the record has the runner paused | `board` when the board imposed it (stored as an agent down of kind `board-paused`), else none |
| `off` | the record covers the bar and the runner was gone for all of it | none |
| `blank` | the record began after the bar ended, or never began, and nothing else shows | none |

A release the board ordered (`preempted`) and one the session chose
(`recused`) are not let-gos: their bars read as working. A drop and a stall
belong to the runner that made them. A lapse belongs to the holder it lapsed,
not to the runner that took over, since the lapse is written under the taker's
name. A failed work-log session belongs to the holder open when its row
reached the board.

**One rule each, shared with what draws it.** `moved` is the Moved count's own
rule for a runner's share, from the same function, so a bar the lanes draw as
moved is a bar the `runner` count includes: they differ only in scope. A
runner's timeline is the one `GET /api/runners/{name}/states` answers, rows and
the clock's tail folded the same way. The runners are the ones `GET /api/runners`
shows this caller and no others, by the same reach rule, and **the lanes ignore
`project`**, as the Runners page does. Nothing new is stored for them, and the
read writes nothing - it does not sweep sessions gone, since whether a session
is live is judged by the clock.

**Limits of the lanes.**

- A runner's work in a project the caller cannot see reads as idle: the claims
  of an issue the caller cannot see are not read.
- A bar before a runner's state record began draws from its claims alone, and is
  `blank` where it holds none. A runner that never had a row has a lane only for
  what its claims show.
- A claim left open, or ended by a lapse, reads as working only until the record
  says the runner went gone; a claim cannot make a gone stretch working.
- A work-log row carries no runner. A lease lost to another runner puts a failed
  session on the new holder.

### The flow

```
GET /api/reports/flow?from=&to=&projectId=&type=
```

The timeline says when one epic's work sat where, and the cumulative flow says
how many issues stood in each column at each instant. The flow says **what
moved, and how long it took**: across a range, per column, how often work came
in and went out and how long it stayed, and across the whole window how often
it was dropped, hopped, sent back, asked about and run. It is **for a
program**: counts and durations only, with **no page** and no index card, so a
session can read the whole answer in one call.

`from` and `to` are the timeline's own bounds — instants, the 90 days ending
now by default, the same `400`s for an unparseable or inverted range, and `from`
clamped forward to the first issue's creation the way the timeline's is.
`projectId` narrows to one project and `type` to one issue type; a `projectId`
that names no project and a `type` that is not one of the issue types are each
a `400`, the latter naming the types that are. The route is open to a person or
to a key.

It answers `{ from, to, columns, window, unresolved }`. `from` and `to` are the
range as the server resolved it. Each entry in `columns` is the board's own
column, in board order — every status that is not deferred, terminal columns
included — as:

- `statusId` and `name`;
- `entered` — how many stretches in the column **began** in the window;
- `left` — how many stretches in the column **ended** in the window;
- `dwelled` — how many of those stretches have a dwell to measure, which is the
  ones that ended in the window, so it is `left`'s count;
- `medianDwellSeconds` and `p90DwellSeconds` — the median and the ninetieth
  percentile of those dwells, the latter by nearest rank, both `null` when
  `dwelled` is `0`;
- `standing` — how many issues are in the column at `to`.

`window` is the tallies that belong to no one column:

- `dropped` — claims let go with the outcome `dropped`;
- `hops` — one `{ kind, count }` for each of the seven kinds, **always all
  seven**, in the fixed order `express`, `parent`, `epic`, `under`, `sendback`,
  `approve`, `close`. `approve` and `close` are one flag on the event
  (`preapproved`) and are told apart by the destination: a terminal column is a
  `close`, anything else an `approve`. A move that carries no hop flag — a drag,
  a merge, a push back, a landing, a check's verdict — is not a hop;
- `sendBacks` — any move to a column **left** of the one it came from, in board
  order, so the `sendback` hop is counted here as well as in `hops`;
- `questionsAsked`, `questionsAnswered` and `questionsLapsed` — questions whose
  own time falls in the window, and `medianAnswerWaitSeconds` and
  `longestAnswerWaitSeconds`, the wait from the ask to its earliest answer
  (never below `0`), both `null` when nothing was answered in the window.
  `questionsAnswered` and both waits are over a **person's** answers only: an
  answer the board wrote itself by lapsing the question (the `answered` event
  with `lapsed: true`) is counted in `questionsLapsed` and nowhere else, so a
  lapse minutes after the ask cannot pull the wait down. A lapsed question is
  in `questionsAsked` and `questionsLapsed` only, unless a person answered it
  afterwards, when it counts as answered and the wait is to that answer;
- `sessions` and `sessionErrors` — work-log sessions that **ended** in the
  window, and how many of them ended in error.

`unresolved` is the timeline's count: stretches that name a column no longer on
the board, left out of every column above.

#### The rules

- **The window is half-open**, `[from, to)`: an instant at `from` is in, an
  instant at `to` is out.
- **Creation is entry to the first column.** An issue's first stretch begins when
  it was made, so it counts in `entered` for the board's first column.
- **Dwell is of stretches that ended in the window**, and is the stretch's whole
  length: it is **never clipped** to `from`, so a stretch that began long before
  the range and ended inside it is measured from where it began. A stretch still
  open at `to` has no dwell yet; it is `standing`.
- **`standing` is read at `to`**, not at the window's last instant: a stretch
  that begins exactly at `to` is standing there without having entered.
- **`projectId` and `type` filter the issues themselves**, not the roots the
  timeline filters. Every count in the answer, `unresolved` included, is over the
  issues that were kept.
- **The rebuild is the timeline's**, and so is its one hole: see [the rebuild,
  and its one hole](#the-rebuild-and-its-one-hole). A stretch that cannot be
  placed on a current column is counted in `unresolved` and nowhere else.
- **Questions are read from comments**, because the `asked` event carries no
  question id: an ask and its answer cannot be paired from the trail alone, so
  the fold pairs them from the comments that carry them.
- **Only `status_changed` and `claim_released` are read** from the
  [issue event](#issue-event) trail; sessions come from the work log.

**No row names an issue, and there is no flag that adds them**, so what a
session reads is the whole answer and cannot be widened by a parameter. There is
**no page** and **no CLI subcommand**; a script reaches it with
`hatch api GET /api/reports/flow?...`, which is the
[API surface](#api-surface)'s row for it.

**The board read borrows this fold** for how long a column's stays normally last.
`GET /api/board` carries `normals`, one `{ statusId, seconds }` for every active
column (after the first, not terminal, not deferred, by flag and never by name),
and `at`, the instant the board was read at. `seconds` is the column's
`p90DwellSeconds` from this very fold, over the last thirty days, over every
project and over issues with no children at all, so the board and the flow page
cannot disagree; it is null where the column has under ten stays. The fold is
run by `ColumnNormals`, which keeps its answer for five minutes and recomputes at
once when a column's flags or place change. It reads past project scope on
purpose: a column is shared by every project, as its WIP count is, so a scoped
reader gets the same normal, and what leaves is a duration and never a row. Each
card carries `enteredColumnAt`, the instant of its newest `status_changed` event
in the [issue event](#issue-event) trail (its creation where it has none; a rank
move writes no such event), and `aged`: true when its column has a normal, the
card has stood there strictly longer, and `FamilyGate.IsLeaf` says no child of it
is open work. The family is read only when some card is past its column's normal.

### The changelog report

```
GET /api/reports/changelog?from=&to=&projectId=&bucket=&offsetMinutes=&level=&loose=
```

`GET /api/changelog` (`ChangelogController`) answers what shipped as a
page, before and after a cursor. It is the agent-facing feed behind
`hatch changelog`, unchanged; the web page that used to draw from it was
replaced by this report. This route answers the same question —
**what closed, in the reader's own time buckets** — as a report rather than a
paged feed: closed top-level epics, grouped into buckets aligned to the
reader's wall clock rather than split one day at a time. It has a page
(`ChangelogReportPage`, at `/reports/changelog`) and an index card on
[Reports](#reports), and the same answer stays reachable by a script.

`from` and `to` are instants, defaulting to the **fourteen days ending now**
rather than the other reports' ninety — the changelog feed's own window — with
the same `400`s for an unparseable or inverted bound, and `from` clamped
forward to the first issue's creation the way the timeline's is. `projectId`
is repeatable, narrowing to those projects' epics; an id naming no project is
a `400`. `bucket` is `hour`, `day`, `week` or `month`, case-insensitive and
trimmed; absent or blank is `day`; anything else is a `400` naming the four.
`offsetMinutes` is minutes east of UTC — local is UTC plus this — aligning
every bucket to the reader's own clock rather than UTC's; outside -1440..1440
is a `400`. `level` is absent, blank, `epic` or `story`; anything else is a
`400` naming the two. `loose` is `true` or `false`, trimmed/case-insensitive,
default `false`; else a `400` naming the two. The route is open to a person
or to a key, and the `400`s above come in that
order — bound, offset, bucket, level, loose, project.

It answers `{ from, to, bucket, level, loose, buckets }`. `from`, `to`,
`bucket`, `level` and `loose` are the request as the server resolved it.
`buckets` is newest first, one `{ start, end, groups, loose }` per bucket that
has at least one group — `start` and `end` are the bucket's own UTC instants.
Each entry in `groups` is `{ epic, closedHere, descendantCount, stories }`,
newest first within the bucket: `epic` is a card, `descendantCount` counts
everything below the epic at any depth, and `stories` is `[]` for now. At
`level=epic`, `closedHere` is always `true` — only the epic's own close is
read. At `level=story`, a group exists for every epic with a story closed in
the bucket, or whose own close falls in it, or both; `closedHere` is `true`
only when the epic itself closed in that bucket, and is `false` for a group
that exists purely because a story under it closed there. A story with no
epic above it is read by neither level yet, and is left for `loose` to carry
later. At `level=epic`, a bucket's `loose` is the closed bugs (any parent)
and parentless stories in it when `loose=true`, else `[]`; at `level=story`
it stays `[]`.

#### The rules

- **Closed is the board's own flag** (`Type == "epic"`, the status's
  `IsTerminal`, and `ClosedAt` set), never a rebuild off the `status_changed`
  trail — unlike [the rebuild, and its one hole](#the-rebuild-and-its-one-hole),
  so there is no `unresolved` count here and no renamed-column hole to fall
  into.
- **The window is half-open**, `[from, to)`, the same as the flow's.
- **At `level=epic`, only top-level epics** — an epic whose parent is not
  itself an epic — are grouped; a story or task closing underneath one is not
  read at this level.
- **At `level=story`, a story groups under its nearest epic above it** —
  walking parents up, whatever depth that epic sits at, so a story under a
  sub-epic groups under the sub-epic, never the root. A story with no epic
  above it makes no group at this level either; it is left for `loose` to
  carry later. Only `story`-typed issues are read this way — a closed bug or
  task underneath an epic makes no group of its own.
- **Buckets align to the reader**, via `offsetMinutes`: an hour, a day, or a
  calendar month on the reader's own clock, floored and then shifted back out
  to UTC for the wire — weeks start on **Monday**, the same as
  [the timeline's axis](#the-drawing).
- **Buckets are sparse.** An empty bucket is never emitted — no dense grid and
  no point cap, unlike [the cumulative flow](#the-cumulative-flow)'s.
- **`level` is `epic` or `story`** — absent or blank defaults to `epic`;
  anything else is a `400` naming the two.
- **`stories` still answers empty for now** — HA-1708's job.
- **`loose`, at `level=epic`**, carries closed bugs (any parent) and
  parentless stories, newest-closed first, with `priority`; nothing else
  appears; it decorates, never reorders. `[]` at `level=story`.

It has a page and an index card, but **no CLI subcommand** (`hatch changelog`
is the older feed, not this report); a script reaches the same answer with
`hatch api GET /api/reports/changelog?...`, which is the
[API surface](#api-surface)'s row for it.

### The changelog file

```
GET /api/changelog/file?remote=
```

A reader asks for a repository's changelog file and gets the same bytes
every time the board has not changed. `remote` is canonicalised the same
way a landing is (`RemoteIdentity`); one that does not canonicalise is a
`400` with that type's own sentence. A canonical remote no project elects
for a changelog (the `Changelog` flag on its `ProjectRepositories` row,
turned on from the Projects page) is a `404` naming the Projects page.

The answer is `text/yaml; charset=utf-8`, not JSON - every closed,
parentless issue in a project that elects the remote, oldest shipped first,
ties broken on project key then issue number so two calls over an unchanged
board answer byte-identical bytes. Two projects electing the same remote
interleave their entries by when each shipped, each entry carrying its own
project key. A title or a release note is a double-quoted YAML scalar, with
a line break folded to the two-character `\n` escape, so either stays one
physical line in the file. The route is open to a person or to a key, the
same as `ChangelogController`'s other route.

### The changelog task

The board files a task to keep a repository's changelog file current, the
moment a parentless issue closes, so the file is brought up to date with no
session reading the board. It is not a hop: nothing is carried, and the actor
is `the board`, as for a reflection the cadence job files.

**Trigger.** A write that lands a parentless issue in a terminal column, out of
one that was not (`ChangelogTasks.JustClosed`, asked beside each
`Columns.ClosedAt` line). For each repository its project binds with the
`Changelog` flag on, one task is filed; a repository elected by two of the
project's rows files once, by canonical remote.

**The doors.** Every write that sets `ClosedAt` into a terminal column on a
parentless issue calls the one helper, after its own save, so a close is never
lost to a mint conflict here: the board's drag and a patch, a bulk edit, the
`/merged` and `/landed` runner calls, the close [hop](#the-hop), a remand, a
column made terminal on the Statuses page (every parentless issue standing in
it), and a plan import. A move between two terminal columns is not a close.
A parented issue closing files nothing; it is carried by its parent.

**One stands at a time.** A recorded task for that remote that is still left of
the column where code gets written, and neither terminal nor deferred, stands
in for every later close: it will write the whole file when it is picked up.
Once it has moved in, or is done, the next close files another. Closing a
changelog task itself files nothing.

**Where it is born.** The column that feeds the implementation column
(`Columns.SendBack`), else the column an agent files into, else the leftmost.
Titled `Record <KEY> in the changelog`, it is a task under the closed issue,
or parentless where the closed issue is itself a task (a task cannot hang
under a task), and in both cases `FiledFromId` names the closed issue. A task
under the closed issue inherits its pre-approval; a parentless one takes the
closed issue's effective level on its own row. The description is one fixed
template naming the remote, the closed issue and the day it shipped, and
telling the session to run `hatch changelog --file` in that remote's checkout,
commit `.hatch/changelog.yml` alone, open a pull request, and declare the task
finished with no code if the command prints `unchanged`.

**The record.** Each task is recorded in `ChangelogTasks` (`IssueId`, unique
and cascading; `SourceIssueId`, set null if the source is deleted;
`Canonical`; `CreatedAt`), and `GET /api/changelog/file` never lists a recorded
task, open or closed. The `changelog_task_filed` event lands on the closed
issue.

**Healed by the poll.** A close files the task above, but a file can fall
behind with none standing: a task cancelled with no commit, a file deleted, a
repository elected before anything closed under it. The runner's poll puts each
elected trunk's file once per sha, and where the stored [changelog
file](#changelog-file) is `behind` or `absent` the put files one task
through the same helper (`ChangelogTasks.FileForGapAsync`), from the newest
entry the file lacks, or the newest entry at all where it has only extras. An
empty projection files nothing, and a `foreign` or `current` file files
nothing. Unlike the close, the poll is held by a recorded task standing in *any*
non-terminal column, deferred ones included, since a task somebody parked on
purpose must not be refiled every poll; a done or cancelled one does not hold
it. Known limits: two runners putting the same canonical in the same instant can
file two tasks (the close has the same window), and a gap whose task is
cancelled with no commit heals at the next trunk move, since the runner puts
once per sha.

**The release note it writes.** An issue that closed with no release note is
the one case where the task's session is the first to look at it with the
whole delivery in view. `WorkDto.ReleaseNoteFor` names that issue when the
dispatched issue is a recorded task and its source has no note (blank counts
as none), and the prompt asks for a ```` ```release-note AER-12 ```` block. The
runner puts it to the named issue under the task's own claim, and
`PUT /issues/{key}/release-note` accepts that token because the task is
recorded for `{key}` and its claim is live. The event on the delivered issue
carries `via`, the task's key. This is the one widening of who may write a
note, and it is the board's own task for a reason: it is the only session the
board itself asked to be in a closed issue's trail, with the source on a
record the board wrote. A program claim on any other issue, a lapsed claim,
and a task recorded for a different issue stay refused, so a session still
writes only the note of the ticket it holds, or of the one the board sent it
to finish.

**Carried by the parent hop.** Where the column that feeds the implementation
column is ticked **Parent pulls**, the [parent hop](#the-hop) carries a
recorded task into the implementation column with no session, whatever its
parent's type and whatever its brief's length: the board wrote the brief, so
there is nothing to analyse. A recorded task with no parent has nothing to
pull from and stays a session's, as any parentless task does. Every other task
and story under an epic is held for analysis as before.

**Turning a repository on.** `PUT /api/projects/{id}/repositories` files one
task for a repository that is new in the list, or whose flag went from off to
on, from the project's latest-closed parentless issue; one task stands for all
the history before it, because the file is regenerated whole. Nothing closed,
nothing filed. Turning the flag off files nothing and deletes nothing.

### The consumption report

```
GET /api/reports/consumption?from=&to=&type=&sort=&limit=&offsetMinutes=&statusId=&projectId=
```

**What did each epic — or any issue type — consume in a window?** Tokens,
notional USD and runner time, read off the work log rather than the board: one
JSON read carrying rows, totals and a time series. The page is at
`/reports/consumption`, a card on the Reports index, and is the one place
this read is for a person rather than a program; the CSV sibling below is
this route's own, not the page's.

`from` and `to` are the other reports' own bounds — instants, the 90 days
ending now by default, the same `400`s for an unparseable or inverted range.
`type` is one of the issue types, or `all`; absent or blank is `epic`,
and anything else is a `400` naming them and `all`. `sort` is `activity`
(default, `lastSessionAt` descending), `tokens`, `cost` or `time`, each
descending; anything else is a `400` naming the four. `limit` is 1 to 500,
default 100, caps the rows alone, and outside that range is a `400`.
`offsetMinutes` is minutes east of UTC, aligning the history's daily buckets
to the reader's own midnight, the same -1440..1440 rule as the changelog
report's. `statusId` and `projectId` are each repeatable; an id naming no
column or no project is a `400`. The route is open to a person or to a key,
and the `400`s above come in that order — bound, offset, type, sort, limit,
status, project.

It answers `{ from, to, type, sort, limit, matched, totals, firstSessionAt,
lastSessionAt, rows, history }`:

- `from`, `to`, `type`, `sort` and `limit` are the request as the server
  resolved it.
- `matched` is how many issues the filter matched before `limit` capped
  `rows` — so a caller can say "the first 100 of 240".
- `totals` is a work-log totals shape (the same one `/work-log/*` answers,
  now carrying `durationMs`) over **every session the filter holds, beneath
  any matching issue, counted once** — not the sum of `rows`' own totals,
  which double-count a sub-epic's sessions against its parent's.
- `firstSessionAt` and `lastSessionAt` **ignore the window** and respect the
  rest of the filter, read the same way `/work-log/history`'s own are, so a
  caller tells "nothing has ever run here" from "nothing ran in the range
  asked for".
- `rows` is one entry per issue that matches the filter **and** has at least
  one session in the window beneath it (itself included) — `{ key, title,
  type, projectKey, statusId, parentKey, totals, firstSessionAt,
  lastSessionAt }`. `totals` is that issue's own subtree, inside the window;
  `parentKey` is the nearest row directly above it, or `null` when its own
  parent is not itself a row. An issue matching the filter with nothing
  beneath it in the window is not a row at all.
- `history` is the same sessions `totals` counts, folded into equal time
  buckets — the shape `/work-log/history` answers, window snapped outward
  onto the grid, the bucket size chosen by the server the same way.

#### The rules

- **The window is half-open**, `[from, to)`, the same as the flow's — a
  session is in it when `from <= EndedAt < to`.
- **Rows may overlap; the total does not.** A sub-epic is a row of its own
  and its sessions are also inside its parent's row, because each row answers
  "what did this issue cost". The report's `totals` and `history` count each
  session once across every row, by walking each session's issue up through
  the tree once and crediting every matching ancestor — not by summing the
  rows, which would count a sub-epic's sessions twice.
- **A session beneath no matching issue is dropped** — from `rows`, from
  `totals` and from `history` alike.
- **`cost` puts every row with nothing priced last**, whatever its own
  `costUsd` reads as — the same rule `/work-log/sessions?sort=cost` already
  states for a null price. Every sort breaks a tie on the issue's key.
- **`statusId` filters on the issue's current column**, the same as
  elsewhere in the module; two ids read as **either**.

The page opens on `type=epic`, the last ninety days, sorted by activity (newest
session first), with **Projects**, **Type** and **Status** facets narrowing
the filter, a **Spend over time** graph toggling between tokens and notional
USD, and a line above the table reading "The first 100 of *N* issues" when
`matched` outran `limit`. There is **no CLI subcommand**; a script reaches the
read with `hatch api GET /api/reports/consumption?...`, which is the
[API surface](#api-surface)'s row for it. [Watching the
loop](../src/Hatch.Web/apps/hatch/docs/watching-the-loop.md#reports) covers the
page itself.

#### The CSV sibling

```
GET /api/reports/consumption.csv?from=&to=&type=&sort=&offsetMinutes=&statusId=&projectId=
```

**The whole filter, as a file a spreadsheet opens** rather than a JSON read a
program caps — the same `from`, `to`, `type`, `sort`, `offsetMinutes`,
`statusId` and `projectId`, parsed and refused the same way and in the same
order (bound, offset, type, sort, status, project), sharing
`ConsumptionReport.Fold` with the JSON route rather than folding twice. There
is **no `limit` parameter on this route at all** — one given on the JSON
route caps `rows`; given here, there is no `limit` to give, so it is ignored
with no code written to ignore it.

It answers `consumption-<from date>-<to date>.csv` (the resolved `from`/`to`,
UTC, as `yyyy-MM-dd`) with `Content-Type: text/csv; charset=utf-8` and
`Content-Disposition: attachment`, the same `File(bytes, contentType,
fileName)` call `/attachments/{id}` makes. The body is UTF-8 with a
byte-order mark, a header row, CRLF line ends and RFC 4180 quoting (a field
holding a comma, a quote, a CR or an LF is quoted and its own quotes
doubled), one row per issue in the sort — **uncapped**, unlike the JSON
route's `rows`. Columns, in order:

```
key, title, type, project, statusId, parentKey, sessions, errors,
inputTokens, outputTokens, cacheCreationTokens, cacheReadTokens,
totalTokens, costUsd, unpricedSessions, runnerMs, firstSessionAt, lastSessionAt
```

`costUsd` is empty, not `0`, on a row with nothing priced — the same
cost-sort sentinel the JSON route's `cost` sort reads off `UnpricedSessions ==
Sessions`. `parentKey` is empty on a root. Instants are ISO 8601 in UTC.

It has no page of its own — a **Download CSV** link beside the consumption
page's totals line reaches it, carrying that page's own filter — and no CLI
subcommand of its own; `hatch api`'s passthrough prints any body verbatim
regardless of content-type, so a script reaches it the same way it reaches
the JSON route:

```
hatch api GET /api/reports/consumption.csv?from=2026-08-01&to=2026-09-01
```

### The activity report

```
GET /api/reports/activity?actor=&from=&to=&projectId=&offsetMinutes=&gap=&lead=&window=&includeKeys=
```

**What did one actor do, in a range?** The trail every other report folds
(`IssueEvents`) read by who wrote it: one actor's events as counts by kind, a
rundown of the issues touched, the newest rows, the rate of work over time,
and the trail bracketed into working sessions with a rough length each. The
page is `/reports/activity`, a card on the Reports index and no nav entry of
its own: an actor picker (people first, then keys, each by name), the window
controls at two weeks and the project control within the tab's scope, a
headline, then the rate of work over time drawn from `series` — one bar per
bucket, oldest first, with the rolling mean as a line through them and an
**Events** / **Estimated hours** control that rescales the whole graph — a
rundown row per issue and the trail beneath, the newest 500 events with a
line saying the listing is cut when `truncated`. The graph names its axis
from the answer (the bucket size, the window's two ends in the reader's clock,
`estimate.windowDays`), says what the line is under it, and has a number field
for the rolling mean's `window`; the page always sends the reader's
`offsetMinutes`, so a daily bucket is the reader's day. The URL carries
`actor`, `project`, `from`/`to`/`range`, `includeKeys` (`true`, set by the **Include my
keys** control beside the picker, shown only when the actor is a person), `measure` (`events` or `hours`;
anything else reads as `events`) and `window` (1 to 90; anything else is left
to the server's own); changing `measure` does not read again, changing `window`
does. With no `actor` the page
opens on the signed-in person when the directory lists them, and writes that
name into the URL, so a pasted link never opens on whoever pastes it. An
`actor` the directory does not list is still sent as given, so a link to a
revoked key's trail still reads. The page writes `gap` and `lead` to the URL,
editable on the page, and draws the bracketed sessions and a rough-hours
estimate, alongside the rate of work over time. Past eight weeks a **Calendar**
view is offered beside **Bars**: one cell a day, Monday-to-Sunday columns,
shaded by the same chosen measure and the same `peak` as the bars, so the two
never disagree; the URL carries `view=calendar`, read as `bars` at eight weeks
or under or on anything else. `hatch activity
[--actor <name>] [--days <n>] [--before <instant>] [--project <KEY>] [--include-keys] [--json]`
prints the rundown as markdown, with `--actor` defaulting to the key's owner; it
renders neither the series nor the sessions, which `hatch api GET
/api/reports/activity?...` still reaches. The route is open to a person or
to a key.

`actor` is required and matched **exactly**, untrimmed, against the event's
own `Actor`. A name no live person or key answers to still answers, with
nothing in it: the trail outlives the row it names. `from` and `to` are
instants, defaulting to the **fourteen days ending now** — the same window as
[the changelog report](#the-changelog-report) — with `from` clamped forward to
the first event ever written. `projectId` is repeatable and narrows to events
whose issue is in one of those projects.

One more parameter widens whose events are read, and four shape `series` and `sessions`:

- `includeKeys` (default `false`) — when `actor` names a person, also counts the events under keys that person owns which a hand typed, each marked `viaKey: <key name>`. It is a heuristic read off the trail alone: an event under the key is the loop's, and left out, when a work-log session of the same issue spans it (a work-log row records no key, so the issue and the instant are all there is to match) or when the issue's trail shows that key's `claim_taken` with no release, lapse or other take since; the claim events themselves are the loop's too. Every other event under the key is counted as the person's hand. It has no effect when `actor` is not a person. A trail row so counted keeps the person as its `actor`, and the counts, rundown,
  series and sessions include it, under the same 500-row cap.

The four that shape `series` and `sessions`:

- `offsetMinutes` — minutes east of UTC, 0 when absent, -1440 to 1440 — aligns
  a daily bucket to the reader's own midnight, read exactly as the work log's
  own [`offsetMinutes`](#the-graph) is.
- `gap` — minutes, 30 when absent, 1 to 1440 — the longest stretch between two
  consecutive events that still counts as one working session.
- `lead` — minutes, 15 when absent, 0 to 240 — added to every session's
  estimated length, including a lone event's.
- `window` — days, 3 when absent, 1 to 90 — how many days the rolling mean in
  `series` trails over.

The `400`s come in this order: a missing or blank `actor` (`an actor is
required`); an unparseable or inverted bound, with the same sentences as the
[timeline](#the-timeline)'s; `offsetMinutes` out of range (the work log's own
sentence); `gap` out of range (`a gap is between 1 and 1440 minutes - not
{gap}`); `lead` out of range (`a lead is between 0 and 240 minutes - not
{lead}`); `window` out of range (`a window is between 1 and 90 days - not
{window}`); a `projectId` naming no project (`there is no project 9999`),
found with one query for all of them rather than one per id; and, once the
bucket size is chosen (see below), too many buckets (`{n} buckets is more
than the 1000 this answers in - ask for a shorter range`) — the work log's own
cap, [`WorkLogController.MaxBuckets`](#the-graph), without its "ask for daily
buckets" clause, since there is no `bucket` parameter here to ask for.

It answers `{ actor, from, to, total, kinds, issues, events, truncated,
bucket, offsetMinutes, series, sessions, estimate }`:

- `actor`, `from` and `to` are the request as the server resolved it.
- `total` is every event in range, after the project narrowing.
- `kinds` is `[{ kind, count }]`, most first, then by name.
- `issues` is `[{ issue, first, last, count, kinds }]`, one per issue touched,
  `issue` a card, latest `last` first (the issue's id breaking a tie).
- `events` is the rows the activity feed lists, newest
  first (`at`, then `id`, descending), **at most 500** — `ActivityController`'s
  own cap — with `truncated` `true` when more existed.
- `bucket` is the size the server chose, `hour` or `day` — [the work log's own
  rule](#the-graph): hourly up to 48 hours, daily past it, named back in the
  answer the same way.
- `offsetMinutes` echoes the request.
- `series` is `[{ at, events, hours, rollingEvents, rollingHours }]`, one
  point per bucket, oldest first; an empty bucket is a zero, never absent.
  `hours` is the estimated hours of every session that **starts** in that
  bucket — a session spanning a bucket edge is not split. `rollingEvents` and
  `rollingHours` are the trailing mean of `events` and `hours` over `window`
  days, expressed in buckets; the first buckets of the series average over
  what exists rather than over nothing.
- `sessions` is `[{ from, to, events, issueKeys, estimatedMinutes }]`, newest
  `from` first: a maximal run of the actor's events with no gap over `gap`
  minutes between two consecutive ones. `estimatedMinutes` is `(to - from)`
  plus `lead` — a one-event session has `to == from` and is worth `lead`
  alone.
- `estimate` is `{ sessions, minutes, gapMinutes, leadMinutes, windowDays }`:
  the totals `sessions` adds up to, beside the parameters they were computed
  with, so a page can state the rule in one sentence from the answer rather
  than from its inputs.

`total`, `kinds`, `issues`, `series` and `sessions` cover **every event in
range**, whatever the cap cut from `events` — narrowed by `projectId` first,
exactly as `total` is, so a project filter narrows the series and the
sessions too. The window is half-open, `[from, to)`, the same as the flow's.
The arithmetic is `ActivityReport.Fold`, a pure fold over light rows with no
payload; the payload is read in a second query for only the rows listed, so a
busy runner never drags every `jsonb` across. An index on
`IssueEvents (Actor, At)` is what keeps the first query from reading the whole
trail.

## The dispatcher

`GET /api/work/next` answers "what should an agent do next, and how?" in
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
last wherever it sits. The scan walks the columns once per tier, each tier
right to left: every [emergency](#expedite) candidate in a project that is not
paused; then, for an expedited project and then for every other unpaused one,
its expedited candidates, its normal ones, its low ones and its economy ones;
last, every candidate that is paused itself or sits in a paused project. So an
emergency bug in the leftmost column of any project is reached before an
expedited project's economy story in the rightmost one, which is reached
before another project's expedited one, and inside each tier the order is the
board's own. The level that puts a candidate in a tier may be its own or
inherited from an ancestor — the walk does not care which — and the project's
level is [its own](#expedite). The last tier is always folded, so it
never changes which issue `next` or a hop picks — only that `queue` can report
it. The low and economy tiers, unlike the others, are each gated rather than
always folded: a row reached on either can still be folded by [its own pace
check](#expedite), so reaching one of those tiers does not guarantee the row
is clear. Since HA-239, every tier
below the first one with a clear candidate is parked instead: the first clear
row seen, top down, names itself on every clear row in a strictly lower tier,
and `queue` reports the park rather than reporting the lower tier as available
when it is not. One pass per tier rather
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

An override changes what a dispatch costs, and whether one happens only for a
caller that declared what it can spawn (the model item below). Nothing else in
the refusals consults one: an issue with no playbook for its next move is
refused in the same sentence whether it names a model or not.

**Twenty refusals**, and two of them are rules of the whole loop rather than
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
7. The issue, or an ancestor, holds an unanswered question that holds this move
   (`Dispatch.QuestionFold`). At `hold` every move is folded — *it is waiting on
   a person, not on an agent*, naming the ancestor when the question is its
   own. At `build` only a move out of review into a merges or terminal column
   is folded (the approve and close hops and a check's advance), and in a
   trunk-landing project the move into the implementation column — *it is
   built, and a person answers before it merges*. `merge` folds nothing. A
   dangerous issue is folded at any level, naming the question's key and the
   reason. Every one is a person's to answer (`BlockedKinds.Person`), and it
   sits after the merges column's judgement, so a pull request there merges
   whatever is open.
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
9. The union of the project's, its ancestors' and its own tags, folded in with
   the column being left's own, requires [runner tags](#project) the caller's
   runner does not fully advertise — on every move a runner is handed, in every
   column and on every hop: out of the implementation column, into it, out of
   any other, on the review self-move while it is conflict or build work, and
   on a hop of any kind. A tag says which runner may work the ticket, not only
   which may write its code, and the scan hands a runner a hop's ticket whether
   or not a session is spawned for it. Never a review row the verdict below
   already folds - nothing is handed to any runner on that row, and its own
   sentence says why. A keyed caller that sends no `?tag=` has advertised its
   key's config tags, or none where the key has no config; a caller with no
   key and neither `?tag=` nor `?dangerous=` is the board's own scan and folds
   nothing on tags. A superset of the union qualifies. *"AER-12 is worked only
   by a runner advertising macos and xcode - this runner advertises linux"*
   (*"... advertises none"* for a keyed caller whose key sent or configured
   none), kind `other`. Named by the column only when that column's own tags
   are part of why it folded: *"AER-12 in Breakdown is worked only by a
   runner advertising gpu - this runner advertises none"*. Waits on nobody.
10. The caller's runner is [omitted](#omitted-runners) from the issue, on its
    own row or inherited from an ancestor's (the two are a union), and the
    caller sent `?runner=<name>`: somebody said this runner is not to work it.
    On every move a runner is handed, in every column and on every hop, and
    never a review row the verdict below already folds - the same reach as the
    runner-tag item above. It asks nothing of the column, and is not gated on
    the implementation column. Two sentence shapes, by who said it: *"HA-9 is
    not worked by gpu-1 - it recused itself 3 minutes ago (no toolchain)"*
    where the omission's own author is the runner asked about, and *"HA-9 is
    not worked by gpu-1 - operator omitted it 3 minutes ago (no toolchain)"*
    where somebody else wrote it. An omission inherited from an ancestor adds
    where it was set - *"HA-9 is not worked by gpu-1 - operator omitted it on
    HA-2 3 minutes ago (no toolchain)"* - and the parenthesised reason is left
    off where there is none. Kind `other`. Waits on nobody. It reads `?runner=`
    (and `?asRunner=` on the queue, which wins where both are sent) the way the
    let-go fold in [what a pass skipped](#what-a-pass-skipped) does, but on all
    four routes: `work/next`, `work/queue`, `work/{key}` and a hop. A name
    matching no runner is fine, and no `?runner=` folds nothing. Said after the
    runner-tag item and before the dangerous one.
11. The issue is flagged dangerous, on its own row or by inheritance from the
    nearest flagged ancestor, and the caller sent `?dangerous=false`: the
    runner refuses dangerous work. On the same moves as the runner-tag item
    above that run the issue's code — out of the implementation column, and on
    the review self-move while it is conflict or build work. Never a hop, and
    never the move into the implementation column, nothing being spawned for
    either; unlike the runner-tag item, a column's own tags make no
    difference to it. A caller that sends no `?dangerous=`, or `true`, is not
    folded by this at all, which keeps every older caller working unchanged.
    *"AER-12 is flagged dangerous (load-tests the API) - this runner refuses
    dangerous work"*, naming the issue asked about and the reason the flag
    carries, kind `other`. Waits on nobody. Said after the runner-tag item, so
    a runner wrong on both hears about the tags, and before the model item.
12. The model the move would run on — the issue's own override, else the
    matched playbook's, else the review row's on the review self-move — is one
    the caller's runner cannot spawn: an alias the caller's `?model=` list lacks
    (a non-empty list only), or a pinned id whose family (`ModelFamilies.AgentOf`)
    is not the caller's `?agent=`. On every move a session is spawned for, in
    every column, breakdown columns included. Never a hop, which spawns
    nothing, and never an idle review self-move the review judge already folds.
    A caller that sends neither folds nothing, which keeps every older caller
    working unchanged; `?asRunner=` and the let-go peer check read the runner
    row's `Models` and `Agent` instead - a live session folds in only its own
    `Agent`, as a fallback where the beat row names none - and a row that
    never reported them folds nothing. *"HA-12 in Breakdown runs on fable - this runner's claude does not
    offer it"* (*"... this runner does not offer it"* when no agent was sent),
    or *"pinned to gpt-6-luna, a codex model - this runner's agent is claude"*,
    kind `other`. Waits on nobody. Said after the dangerous item and before the
    refusal item.
12a. The same model is one the asking runner's key **refuses** — its
    [refused models](#runners-on-the-board), `RunnerModels.Refuses`: equal after
    trimming, with an alias lowercased, or the refused entry is an alias and
    the model a pinned id whose hyphen-separated tokens include it (`opus`
    refuses `claude-opus-5-5`, never `claude-fable-5-2`). A refused pinned id
    matches itself only, and a null or blank model matches nothing. Gated as
    the model item is — every move a session is spawned for, never a hop,
    never an idle review self-move. The runner is the one `?runner=` or
    `?asRunner=` names, and `work/{key}` falls back to the calling key's own
    name where it sent none, since `hatch work KEY` sends no `?runner=`; a
    name no key answers to refuses nothing, and a scan that names nobody (an
    attention or takers read of the whole board) refuses nothing. *"HA-12 in
    Breakdown runs on fable - build-box refuses it"*, kind `other`. Waits on
    nobody. A peer that refuses the model is no peer that could take a ticket
    another runner let go of. Said after the model item and before the
    toolchain item.
12b. The move's `from` column is one the asking runner's key **does not
    work** — its column rule, set on the Runners page and stored on the key's
    config row beside the refused models. Under `except`, a move out of a
    listed column folds; under `only`, a move out of a column that is not
    listed folds. A rule of `all`, and a key with no config row, fold
    nothing, and an id that names no column is dropped before the rule is
    judged. Gated as the refusal item is — never a hop, never an idle review
    self-move — and read for the same runner, off `?runner=`, `?asRunner=` or
    the calling key's own name, so a peer's scan behind `takers`, `untaken`
    and the let-go deferral each judge the peer on its own rule. *"HA-12 in
    In Progress is not worked by build-box - it works only Breakdown, In
    Review"*, or *"… it works every column except In Progress, Testing"*, the
    columns named in board order; an `only` rule with nothing listed reads
    *"it works no column"*. Kind `other`. Waits on nobody. Said after the
    refusal item and before the signing item.
12c. The ticket's project **allows only signed commits** — the project's
    *Allow unsigned commits* setting is off — and the asking runner has not
    said that it signs them: its signing is `off`, `fails`, or no answer, and
    only `works` clears. Gated as the refusal item is — every move a session
    is spawned for, never a hop, never an idle review self-move — and kind
    `other`, waiting on nobody. *"HA-12 allows only signed commits -
    build-box does not sign its commits"*, *"… signing fails on build-box
    (gpg: signing failed: No pinentry)"* (no parentheses where the runner gave
    no reason), or *"… build-box has not said whether it signs"*; a runner
    with no name reads *this runner*. Whose answer is read is the one thing
    that differs from the items above: `?signing=` where the caller sent one
    of `off`, `works` or `fails` (a `?signingWhy=` rides beside `fails`; any
    other word counts as not sent), else, for a caller holding a key, the
    worst answer of the live loop sessions of the runner `?runner=` names or
    the calling key's own - none, so no answer, where no such runner is live.
    A caller with no key and no `?signing=` is the board's own scan and is
    not judged. `?asRunner=` and every peer scan read that runner's own
    sessions and ignore `?signing=`, so `takers` and `untaken` print the
    sentence word for word and a peer that cannot sign is no peer that
    could take a ticket another runner let go of. A project that allows
    unsigned commits is never folded, for any caller. Said after the column
    item and before the forge item.
12d. The asking runner's **forge sign-in fails**, and the move needs the forge.
    Only a runner that said `fails` is folded: a runner that has said nothing,
    and every runner older than the check, is not judged. That is the one place
    this differs from the signing item, where no answer counts as not signing.
    A move needs the forge when it is either of:
    - a step out of the implementation column into another column, in a
      project that lands by pull request, for a ticket that opens its own pull
      request. A task that rides its story's one pull request opens none and is
      not folded; the story whose own step opens that pull request is, but not
      while it still has open children, which are the work. An epic or an
      ambition is never folded.
    - the review self-move while it is build work. The runner reads the failing
      check's log through `gh` before it spawns anything, and a runner that
      cannot reach its forge would wait there on every pass.

    Never a hop. Never an idle review self-move the review judge already folds.
    Never conflict work, which needs only git. Never a column left of
    implementation. Never a project that lands on its trunk. Kind `other`,
    waiting on nobody. *"HA-12 ends in a pull request - build-box cannot reach
    its forge (gh said: HTTP 401)"* or *"HA-12's failing build cannot be read
    by build-box - it cannot reach its forge (gh said: HTTP 401)"*, with no
    parentheses where the runner gave no reason; a runner with no name reads
    *this runner*. Whose answer is read is as for signing: `?forge=` where the
    caller sent `works` or `fails` (a `?forgeWhy=` rides beside `fails`; any
    other word counts as not sent), else, for a caller holding a key, the worst
    answer of the live loop sessions of the runner `?runner=` names or the
    calling key's own. `?asRunner=` and every peer scan read that runner's own
    sessions and ignore `?forge=`, so `takers`, `untaken` and the let-go
    deferral judge each peer on its own answer: a peer whose forge fails is no
    peer that could take a step another runner let go of, and
    `runnerCapacity` prints the sentence word for word. A caller with no key
    and no `?forge=` is the board's own scan and is not judged. Said after the
    signing item and before the toolchain item.
13. The issue's project binds one or more [repositories](#repository), and
    the union of their parsed `.hatch/dependencies.json` entries names a
    dependency the caller's `?has=` does not satisfy at a version
    `DependencyVersion.Satisfies` treats as a minimum - the caller's version
    must be at least it - or any bound repository's file is `invalid`. On the
    same moves as the dangerous item — out of the implementation column, and
    on the review self-move while it is conflict or build work. Never a hop,
    and never the move into the implementation column, nothing being spawned
    for either. A caller that sends no `?has=` is not folded by this at all,
    which keeps every older caller working unchanged. *"HA-12 needs dotnet 10
    or newer, pwsh 7 or newer and playwright-browsers (`acme/repo`'s
    .hatch/dependencies.json) - this runner has dotnet 8, no pwsh and no
    playwright-browsers"*, or *"`acme/repo`'s
    .hatch/dependencies.json is invalid at `<sha>` (`<problem>`) - fix it on
    the trunk"*, kind `other`. Waits on nobody. `?asRunner=` and the let-go
    peer check read the row's `Has` instead, and a row that never reported it
    folds nothing - a session carries no `Has` of its own, so a key with a
    beat reads that beat's, and a session-only key reads none at all. Said
    after the dangerous item and the model item, before the dependency-edge
    one.
14. Something it [depends on](#dependency) is unfinished, and the move is into
    the column where the code gets written, or a parent pull (hop kind 4) out of
    a `ParentPulls` column. Only those moves: a pull request that already exists
    is not held back by what its ticket once waited on.

    Also folded on that same move, just after the dependency: the issue is a
    task that shares its story's branch and a sibling is in flight on it — at or
    past the implementation column, not terminal, not waiting on the story's
    pull request. *"AER-12, sharing its parent's branch, is already in flight -
    one sibling at a time"*, kind `family`. See
    [one sibling at a time](#one-pull-request).
15. The move is into the [WIP section](#wip), and the section has no room for
    it: the load — not counting this issue — is already at or over the limit.
    Said after the dependency, which needs other work to land, and before the
    verdict below, which needs nothing at all once a branch is clean — a full
    section needs other work to leave. The load is always the board's own,
    whatever `ancestorKey` narrows the candidates to.
16. The move is into the WIP section, the issue's parent is an epic, and that
    epic is at its own [limit](#an-epics-own-limit) — the load under it, not
    counting this issue, is already at or over its `WipLimit`. Said right
    after the section-wide check above, for the same reason: it too needs
    other work to leave before anything more of this epic is pulled in.
17. It is in review and there is nothing for an agent to do on its branch: it
    neither conflicts with the trunk nor has a build that failed on its current
    tip — because it merges cleanly and its build passes, is still running, was
    not read on that tip or has no checks, because it has no branch on origin
    (one already merged counts as none), because more than one branch is named
    for it (an issue whose pull request's branch is not known yet), or because
    no runner has checked yet. A clean, pre-approved row with no pull request,
    no children and a merge column after review is a dead end, and its sentence
    says to record a pull request with `hatch pr`. See [the review
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
folded by the other of `FamilyGate.Pulls`'s two conditions — a sibling on a
shared story's branch already in flight — with "{sibling}, sharing its
parent's branch, is already in flight - one sibling at a time", kind `family`,
the same sentence the implementation-column fold gives. Under anything but a
shared story no sibling holds a pull. Every refusal above this one still applies to
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

### Deadlocked

A row is **deadlocked** when no pass will ever clear it on its own: the move it
waits on needs other rows to move first, and every one of those waits, through
the board's own folds, on a row that is itself stuck. A story that depends on a
story whose open child depends on the first is the plain case, and
`IssueDependenciesController` cannot refuse it, because it walks dependency
edges only and the parent edge is what closes the ring.

`Deadlocks.Find` works it out as a fixpoint over what each fold reports it
waits on (`ScanRow.WaitsOn`). A row with nothing to wait on can move, and a row
moves once everything it waits on can - except where the fold is *any-of*, as
the work-in-progress limit is: it frees as soon as any one of the rows holding
a slot can move, so it is deadlocked only when all of them are stuck. Whatever
cannot move at the fixpoint is deadlocked. A row parked behind a higher
priority tier is clear, not deadlocked: the park is not a fold.

One row is deadlocked while waiting on no issue at all: a [review dead
end](#the-issue-in-review-whose-branch-conflicts-or-whose-build-failed-is-dispatched-to-review)
(`ScanRow.DeadEnd`), a pre-approved issue in review that no hop will ever carry
because it has no pull request, no children and no no-code declaration, on a
board with a merge column after review. It waits on no issue, but no pass will
move it either: a person has to record its pull request with `hatch pr`. It is
never counted as movable, so the story or epic waiting on it, directly or
through a chain, is deadlocked too.

The queue marks them, and only the queue does (`work/next`, preemption and the
dashboard pay nothing for it). The judgement is **board-wide**, whatever
`ancestorKey`, `mine` or the runner's checkout say: a narrowed queue runs a
second, unnarrowed scan to decide, so a row outside the subtree, or a fold for
the wrong repository, cannot hide an edge of a ring. A live claim counts as
movable whoever holds it.

A deadlocked row has `deadlocked: true`, and its `blocked` sentence starts
`deadlocked - ` and then says what the fold said. `blockedKind` is unchanged, so
a deadlocked dependency row still groups with the dependency rows.

### The hop

**The loop's own write, on the loop's own say-so.** Seven kinds, asked in this
order. A check's own dispatch into the merge column is never one of them: on a
project with *A passing check approves the merge* on, `ScanAsync` and
`WorkController.ResolveAsync` route a passing review row into the merge column
as a plain `advance` move, not as a hop `HopWork` writes
([the review check](#the-issue-in-review-whose-branch-conflicts-or-whose-build-failed-is-dispatched-to-review)
says where that routing happens). `HopKind`'s own `intoCheck` guard
(`Dispatch.cs:1947-1952`) keeps that move from being misread as one of the
first three kinds anyway: a protected `to` that differs from `from` on the
review column can only be the check's own merge column — `Columns.Target`
self-dispatches review ahead of any other protected column — so `crosses`
reads false there, and the express, epic and under arms stay withheld from it
exactly as they are from a review self-move.

**An open question holds the approve and close hops at `build`.** A hop is
folded by the same question fold as every other move
(`Dispatch.QuestionFold`, the seventh fold in the list of what makes an issue
actionable). At `hold` every kind is folded. At `build` the work is built but not merged or
closed, so the approve hop out of review into the merges column and the close
hop into a terminal column are folded — *1 unanswered question at build on
`<key>` - it is built, and a person answers before it merges* — naming an
ancestor's key where the question is an ancestor's. The verdict apply follows
the same rule: `WorkController.VerdictWork`, where a passing check on a project
with *A passing check approves the merge* on would carry the issue into the
merges column, asks `QuestionFold` for that move first. Where it holds, the
issue stays in review with no status or rank change, the verdict is still
applied, and a comment says *Passed its check and stays in `<column>` - `<the
sentence>`*. That holds at `hold` too, since nothing beneath a `hold` question
moves. `MergedWork` and `LandedWork` are not touched: a merge on the forge or a
landing on the trunk is a fact, not a dispatch.

The epic, under and parent kinds cross a project line only on the starter's
say. Inside one project a running parent carries its children, whoever started
it. Across a line it carries a child only while the person who started the
parent - `startedBy`, written when the parent was first moved into the
implementation column - could have moved that child into the column it would
land in themselves: a live person with the child's project in scope, and, for a
protected column, an owner or approver of it (`SetterAuthority.MayPull`, the
rule `CanMoveIntoAsync` applies to a named person). The answer is read live, so
taking the role or the scope away stops the next pull and giving it back
resumes it. A parent started by nobody, by a key, or by a person since deleted
pulls nothing across a line. Where a parent in progress is not honoured, the
queue says "OPS-1 is in progress, but the person who started it has no say in
AER, so a person moves it" - one sentence for every reason - and the child's
`pullPassedOver` names OPS-1. A parent across the line that is not yet in
progress is the ordinary "its parent has not reached the implementation column"
hold. `FamilyGate.PullHeld` is the one test the queue, the passed-over guard
and the card all call. A level crosses only on its setter's authority ([Pre-approval](#pre-approval)):
the express, approve and close hops make no check of their own, they take the
effective level, so a level the walk passed over at the line carries nothing
and the queue says "pre-approved on AER-1, not honoured in OPS - its setter has
no say there, so a person moves it". Runner tags, a dangerous reason, omitted
runners, the epic's story limit and the local-model level still cross.

1. **express** — an issue that is [express](#pre-approval) and stands in a column
   marked [`ExpressSkips`](#status), with no unanswered question.
2. **epic** — an epic standing in a column short of the implementation
   column, carried on each move right until it reaches that column, with
   something filed under it — any direct child, of any type, in any column —
   and, for a sub-epic, whose own direct parent is itself an epic that has
   reached the implementation column. A top-level epic — one with no
   parent — is carried only by the move that lands it in the implementation
   column, and by no earlier one: the move off the backlog, into the column
   that feeds the implementation column, is the person's press, and this
   carries it the rest of the way (HA-1069). An epic whose parent is not a
   running epic is never carried (HA-202). An epic that clears those tests
   but has nothing filed under it is never a hop either: it is folded
   instead, with *"nothing is filed under it — an epic runs its stories, and
   it has none"*. A sub-epic whose parent epic is in another project follows it
   only on the starter's say, above.
3. **under** — a story or bug standing in a column marked `ExpressSkips`,
   whose parent is an epic at or past the implementation column — in [In
   Progress](#status), in review, wherever it stands once it has reached
   that column. Measured off the implementation landmark, never off a
   column's name, so an epic sitting in the review column still counts. A
   parent in another project counts only on the starter's say, above.
4. **parent** — a child standing in a column marked [`ParentPulls`](#status),
   whose parent has reached the implementation column — stands in it, or in
   any column to its right, a terminal column included; a deferred column
   never counts — and which has no sibling holding the pull: only a sibling on
   a shared story's branch holds it, at or past the implementation column, not
   terminal and not waiting on the story's pull request (HA-149, HA-1018) -
   and none of whose own [dependencies](#dependency) is unmet, which folds the
   pull instead (HA-1017). Out of the column that feeds the implementation
   column (`Columns.SendBack`) it carries a child only when the parent is not a
   type that runs children (a task under a story, not a story under an epic)
   and the child is not over the `BriefLimit` the implementation column's
   playbook would send it back on; either is analysed instead, so the
   sendback hop never carries a pulled child straight back (AR-52). A
   [changelog task](#the-changelog-task) the board filed (`FamilyGate.IsChangelogTask`)
   is neither analysed nor narrowed: it is carried whatever its parent's type
   and whatever its brief's length (`PullWithheld`). One with no parent is not
   carried, since a pull needs a parent already started. A parent in another
   project pulls only on the starter's say, above. Out of any
   other `ParentPulls` column nothing narrows it.
5. **sendback** — a childless issue standing in a column, to the right
   (strictly, in board order) of `Columns.SendBack` — the column whose own
   `Target` is the implementation column (`Columns.Implementation`),
   measured off board shape rather than named — whose matched playbook sets
   a `BriefLimit` the issue's own description is longer than. Asked fifth,
   after the other four and before the sixth and seventh, so nothing already
   qualifying for one of the first four changes meaning (HA-394). Where the
   issue already stands in that column, or left of it, this is not a hop:
   going further would cross the Backlog threshold and re-require
   prioritisation, and the twenty-fifth condition below folds the move instead.
6. **approve** — a [pre-approved](#pre-approval) issue standing in the
   review column with a pull request recorded, whose branch
   `MergeWork.Judge` says would merge cleanly or merely needs bringing
   current (`Dispatch.cs:1545-1553`) — carried into the column ticked
   [`MergesPullRequest`](#status) by `Columns.Advance`, the same column a
   session would otherwise have been dispatched to from review. Where the
   branch instead conflicts or its build has failed, `MergeWork.Judge`'s
   own `Pushback`/`Hold`/`Pending` kinds hold it in review exactly as they
   would an issue with no pre-approval at all — this kind never fires for
   those. Where a check playbook row already covers this exact move -
   `Dispatch.ChecksInto` answering true of the same `to` and `playbook` the
   scan already matched - this kind does not fire either: the check's own
   plain advance already carries it, and pre-approval adds nothing past what
   a passing check already would (HA-1592).
7. **close** — standing in the review column with *no* pull request
   recorded, and every child done by its own type's rule — an epic's own
   fold ignores a deferred child the way [running an epic](#running-an-epic)
   describes, while a story, bug or task counts one open child as enough to
   hold it (`Dispatch.cs:2170-2176`) — carried into the first terminal
   column strictly right of review (`Columns.Closes`, `Columns.cs:204-208`),
   whatever that column is named, when either of two things is true: it is a
   parent — something is filed under it — and it is
   [pre-approved](#pre-approval) or stands in a project whose *A passing
   check approves the merge* setting is on ([`CheckApprovesMerge`](#project));
   or it declared itself [finished with no code](#finished-with-no-code)
   (`hatch pr KEY --none`, `PUT /issues/{key}/no-code`) and is pre-approved or
   stands in a project whose *A no-code declaration closes the ticket*
   setting is on ([`NoCodeCloses`](#project)) — this second branch carries a
   declared issue whether or not it has children, as long as any it has are
   done. A childless, undeclared issue is never carried by this kind, and
   `CheckApprovesMerge` alone never carries a childless one even if it is
   declared: only a declaration that is itself pre-approved, or that stands
   under `NoCodeCloses`, does that. A declared issue with open children
   still waits for them, whichever setting or pre-approval would otherwise
   have carried it — the declaration is a statement that nothing was built,
   so it needs no children to have been done in its place, but it still
   needs the children it does have to be.

Both are asked last, after sendback, so neither changes the meaning of a
row that already qualifies for one of the first five.

Hatch clears a blocker in this order — deterministic first (the board's own
shape answers it, and this fifth kind is that), heuristic second (a session
gets the narrowest brief that can settle it), and a person last, only for
what is theirs. A fold whose sentence is an imperative aimed at nobody —
"cut it into tasks first", said to no one — is a bug on its face, which is
why this kind exists rather than leaving that fold as the only answer.

`POST /api/work/{key}/hop` carries the issue to the column the kind
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

It takes the same query parameters `GET /api/work/{key}` takes, and no
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
  column, an
  epic whose parent epic is not running, an epic with nothing filed under it,
  or (for a child of a `ParentPulls` column) a parent that has not reached the
  implementation column, or a sibling on a shared story's branch already in flight, or a childless issue
  over its playbook's `BriefLimit` with no column to send it back to. The
  sentence is exactly the one `Blocked` already gives for that fold.
- The issue is clear, but is not a hop — none of the seven kinds above applies,
  or the move is a review self-move. A session moves this issue, and a hop
  does not.

Otherwise the move is made: the issue's rank is set with
`RankService.BottomAsync`, the same as any other move, and the history gets
one `status_changed` event naming the caller as the actor, carrying one of
seven shapes depending on which kind carried it: `{ from, to, express: true }`,
`{ from, to, epic: true }`, `{ from, to, under: "<epicKey>" }`,
`{ from, to, pulled: true }` for a parent pull, `{ from, to, sendback: true,
length, limit }` naming the brief's own length and its playbook's own
limit, `{ from, to, preapproved: true }` — shared by the sixth kind and
a pre-approved seventh — the same shape `IssuesController.MoveIssue`
writes, with one more field, so a card that passed a gate with nobody
present says so on its own trail, or, for a seventh-kind close carried by a
setting alone (not pre-approved), one of two shapes naming which: `{ from,
to, approved: "setting" }` for the project's *A passing check approves the
merge* setting carrying a parent, or `{ from, to, approved: "nocode",
nocode: true }` for its *A no-code declaration closes the ticket* setting
carrying a declared issue. Neither setting's close carries a `preapproved`
field, since nobody's pre-approval carried it; where an issue is both
pre-approved and in a project whose setting is on, the pre-approval's own
shape and comment are written, unchanged. A pre-approved seventh kind
carrying an issue that [declared itself finished with no
code](#finished-with-no-code) adds `nocode: true` beside `preapproved: true`
instead.

Both the sixth and seventh kinds write a second thing in the same save as
the move: a comment, naming the ancestor the pre-approval was read from
where it is inherited, or saying it stands on the issue's own row where it
is not, or, for a close carried by a setting alone, naming the setting
(`WorkController.cs:616-648`). The approve hop's own comment quotes
`MergeWork.Judge`'s own sentence for the branch, the same sentence `hatch
queue` would otherwise have printed for it: *"Approved by its pre-approval
from AER-12: \<the merge judge's own sentence\>."* where the level is
inherited, or *"Approved by its pre-approval on its own row: \<the merge
judge's own sentence\>."* where it is the issue's own. The close hop's own
comment is one of four. Pre-approved, the fixed sentence every child being
done already is: *"Closed by its pre-approval from AER-12: every child is
done."*, or *"Closed by its pre-approval on its own row: every child is
done."* on the issue's own level. For an issue that declared it [finished
with no code](#finished-with-no-code) and is pre-approved, the comment says
so and gives the reason instead: *"Closed by its pre-approval from AER-12:
finished with no code - \<reason\>."*, or *"... on its own row: ..."*, and
the `status_changed` event carries `nocode: true` beside `preapproved:
true`. Carried by a setting alone, the third names *A passing check
approves the merge*: *"Closed under AER-12's setting, a passing check
approves the merge: every child is done."*, written with the `approved:
"setting"` payload above. The fourth names *A no-code declaration closes
the ticket*, for a declared issue carried by that setting and not
pre-approved: *"Closed under AER-12's setting, a no-code declaration closes
the ticket: finished with no code - \<reason\>."*, written with the
`approved: "nocode"` payload above.

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

Outside a check, the *Reaching Hatch* block prints `hatch start` only on the
increment bound for the implementation column - the one whose destination is
the column named by `FailStatusName`. Every other increment is told the claim
is the marker and that the ticket moves once, at the end, to the column named
under *Where this increment ends*.

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
  would take it (`Columns.Target`) - unless the issue stands in the review
  column, the project has *A passing check approves the merge*
  (`EfHatchProject.CheckApprovesMerge`) on, the column after review is both
  protected and ticked `MergesPullRequest`, and the issue carries a pull
  request: then it moves there instead (`Columns.Approves`), and the move adds
  a comment naming the playbook: `Approved by <playbook's From → To column
  names>'s passing verdict.`, beside the findings comment every apply posts.
- **A first fail** moves it to the implementation column
  (`Columns.Implementation`), whatever the check's own columns are.
- **A second consecutive fail** moves nothing; see below.

A move is a `status_changed` event carrying `verdict` and `playbookId` beside
the usual payload, and goes to the bottom of its column. Every apply, moved or
held, also posts the findings as a comment of kind `verdict`
([Comment, question and answer](#comment-question-and-answer)) and sets the
row's `AppliedAt`. A pass that lands in the merge column this way carries
`approved: "check"` beside them, and posts the approval comment above in the
same save as the findings comment.

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

**The next row's limit rides along too.** `WorkDto.NextBriefLimit` is the
`BriefLimit` of the playbook row that covers this issue's type out of the
column *after* the one this dispatch moves it to — the limit the session
after this one will be spawned under. Null wherever that row has none, the
dispatch is not a plain advance (a hop, a send-back, or anything other than
`Kind: advance`), or the issue has children, whose briefs are the children's
own. `QueueEntryDto`, `hatch queue` and the queue page carry none of this — a
queued row still answers only for the move it is itself waiting on. A
send-back for an over-long brief is unchanged by any of it: this is the
*next* row's limit, read in advance, not a change to how this one is judged.
The prompt is where it is spent — printed under `## How long the brief may
be` for the session that follows.

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

**A person starts a top-level epic by moving it off the backlog.** An
operator drags a *top-level* epic into the column that feeds the WIP section —
on the stock board, *Backlog → To Do* — and walks away: the next pass carries
it into the implementation column with no session, the same [`epic`
hop](#the-hop) a sub-epic gets, and the epic slice of the WIP section is the
only thing that holds it there (HA-1069). That earlier move is the press, and
nothing carries an epic across it for a person — a [pre-approved](#pre-approval)
epic is the exception only because pre-approval is that person's say-so given
in advance, and the express hop carries it across on its own. An epic with
nothing filed under it is folded — *"nothing is filed under it — an epic runs
its stories, and it has none"* — and one whose parent is not a running epic is
folded *"its parent epic is not running, so nothing pulls it in"*. A board
whose breakdown column empties directly into the feeder column has no such
press: an epic breakdown has just filled is carried straight in.

Once a top-level epic is in the section, an epic **filed
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
without one: a [pre-approved](#pre-approval) parent, or a parent in a project
whose *A passing check approves the merge* setting is on, with no pull request
recorded, every child done by its own type's rule, is closed by the pass
itself - the **close** hop - rather than waiting for it. See [the
hop](#the-hop) for the fold it checks and the column it lands in, rather than
re-deriving either here.

Setting a board up so this whole path runs for one epic, start to finish,
with nobody pressing anything but the one drag nothing skips, is [its own
walkthrough](#a-night-mode-board).

### Running a pass

A pass is the ambition's own version of running an epic. The operator's one
press is moving the ambition into the implementation column; after that, the
next pass runs unattended, the same way the next pass carries a started epic
along with no session. Unlike an epic, the ambition is never carried out of
that column by a pass itself: the parent-closeout row (`In Progress → In
Review`, shape `parent`) never fires for one, since the ambition's own column
never advances past where the operator put it. Only the operator moves an
ambition on. The two sentences a pass's own fold speaks are under [the
dispatcher](#the-dispatcher).

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

**The playbooks the path needs.** [Playbooks](#playbooks) seeds ten rows;
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
reads that way. The ambition's pass row, like the ninth row, is not part of
this path either - an ambition is outside this walkthrough entirely, since
the path carries an epic, never an ambition.

**WIP room.** Give the [WIP section](#wip) a limit that admits at least one
epic and at least one story at once - the epic slice and the `story,bug`
slice both need room for this path to move at all. An epic's own
[`WipLimit`](#stories-at-once) reads as **no limit** when left unset, never zero
by default; a zero limit on either slice is only ever an operator's own
deliberate choice - made on the Statuses page for the section, or on the
epic's own page for its slice - not a default to warn against as an
accident.

**The GitHub side** is entirely [the approval column](#the-approval-column)'s
own, walked through operator-side in [Letting the loop merge for
you](../src/Hatch.Web/apps/hatch/docs/letting-the-loop-merge-for-you.md);
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
- **The epic's own `To Do → In Progress` move is the epic hop.** `To Do` is
  not ticked `ExpressSkips`, so the express hop never fires for the epic
  there, but the epic hop does: a top-level epic with something filed under
  it is carried into the implementation column by the next pass, with no
  session and no playbook, as soon as the WIP section's epic slice has room.
  The one press a regular, un-pre-approved epic needs is the move off the
  backlog into `To Do`; a pre-approved epic needs none. Everything after is
  unattended.
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

**One column for liveness.** Rule 5 reads both `EfHatchRunner.LastSeenAt` and
`EfHatchRunnerSession.LastSeenAt` (a live, not ended, loop session), so the
claim heartbeat and the claim release (the runner's own lane, not the
operator's tokenless clobber) both mark the holding loop as heard from, the
moment either happens, in exactly one place — the caller's own record, by an
unconditional `ExecuteUpdateAsync`. Under a key that is the live (not ended)
session of that key at the claim's checkout, which is what a current CLI beats.
Failing that it is the runner row named by `ClaimedBy` (the wall off, where the
holder is the runner's own name). Failing both, for an older CLI beating
`POST /api/runners/{name}` under a key named otherwise, it is the single
row at the checkout that was live when the claim was taken; a row under a
superseded name was already gone by then and is never revived, and a checkout
with a live session never reaches this step. A heartbeat that races the
runner's own first beat matches nothing, harmlessly. The beat's usage reading
goes to that same record and no other. A record is free when no live claim
belongs to it: a session is busy when a live claim is held by its key's name at
its `where`, a beat row when one is held under its name or at its `where`.
Without this, a runner that has just
released would read as *gone* rather than *free* for as long as its last beat
was stale, which is exactly the moment rule 5 needs to answer correctly.

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

**A project with *A passing check approves the merge* on has a second answer.**
Fix work still comes first: a branch that conflicts, or whose build failed,
is still the self-move above, matched review-to-review, whatever the setting.
Where neither holds - a branch `ReviewWork.Judge` would not fold - the setting
means this row is matched against the merge column instead, and ends there
rather than back in review: `Dispatch.ScanAsync`'s own walk (`Dispatch.cs:350-357`)
and `WorkController.RouteAsync` (`WorkController.cs:1551-1568`, called from
`HopWork` at `:664` and `ResolveAsync` at `:1405`) both ask `Dispatch.CheckColumn`
(`Dispatch.cs:1268-1275` - null off the setting, off an issue with no pull
request recorded, or off a branch `ReviewWork.Judge` would still fold) and, where
it names a column, `Dispatch.ReviewTarget` (`:1284-1286`) reads the match
against that merge column rather than against review - so a row a playbook
matches there with a row itself ticked `IsCheck` ends with `to` the merge
column and kind `advance`, the same kind every other move carries, in place of
the review self-move above.

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
     which branch is the ticket's, and says why. An issue whose pull request's
     head branch is known is never read this way: that branch is the pointer,
     and a second branch named for the key is not consulted or counted;
   - *no branch on origin is named for it*, when every verdict is `none` and no
     pull request's head branch is known; where one is, *its recorded branch
     `<name>` is not on origin*.
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

The two "nothing for an agent to do" sentences of steps 6 and 7 are clean and
green, so they also say who the issue is waiting on. Only the approve hop carries
an issue past review, and only `preapproved` arms it. Below that the sentence
continues *; it waits for an owner or approver to approve it*, and where the
issue's effective level is `express` it says *; express does not carry it past
review, so it waits for an owner or approver to approve it*. A `preapproved`
issue is not held by an approver, so it never gets that tail. Two hops carry it
out of review: the approve hop needs a pull request recorded and a merge column
after review, and the close hop needs children or a no-code declaration. With
none of them it is a **review dead end**: `ReviewWork.Judge`, given the issue's
`Carry` (key, pull request, children, merge column after review), folds it with
*its branch merges cleanly with `<trunk>`[ and its build passes], but it is
pre-approved with no pull request recorded, so nothing carries it past review -
record its pull request with `hatch pr <KEY> <url>`* and sets `DeadEnd`. A
`preapproved` issue that still reaches this fold with a pull request the merge
judge holds, or on a board with no merge column after review - where recording
a pull request would move nothing - keeps the bare sentence. A dead end's kind
stays `other`, and [it is deadlocked](#deadlocked). The queue,
`GET /api/work/queue` and the board's own row all print the same text.

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
protected, and a merge column is always protected (below). `Columns.Target`
itself is unchanged by any of this and still self-dispatches review
unconditionally - it has no idea a check exists. What changes is what
`RouteAsync` and `ScanAsync` ask first: both read `Dispatch.CheckColumn` and
`Dispatch.ReviewTarget`
([the review check](#the-issue-in-review-whose-branch-conflicts-or-whose-build-failed-is-dispatched-to-review))
before ever falling back to `Columns.Target`, so "`In Review` advancing...
stays `In Review`" is now true only when no check playbook covers the move;
where one does, `to` is the merge column directly, with no session run for
that row, because the row that matched there is itself ticked `IsCheck`.

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
request, and this issue has none"`. A check's pass applied by
`WorkController.VerdictWork` crosses this gate too, the same way the hop and
`/merged` do - see [the five paths past the terminal
guard](#the-five-paths-past-the-terminal-guard) for why that is a path and not
a hole.

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
   the ones that are not its branch"*. Never said of an issue whose pull
   request's head branch is known: the runner checks that branch alone, so
   there is never more than one.
6. Every counted verdict `none` — no branch on origin named for it at all:
   **hold**, *"no branch on origin is named for it"*; or, where the pull
   request's head branch is known, *"its recorded branch `<name>` is not on
   origin"*.
7. A clean branch whose build, read about that same merge check's own sha,
   is `failed`: **pushback**, *"its build on `<sha>` failed - back to
   `<review>` to fix it"*.
8. The forge has not caught up with the branch: `mergeStateSha` not null, and
   no counted clean verdict's branch sha equals it: **pending**, *"waiting
   for the forge to catch up with its branch - its pull request names
   `<mergeStateSha>`, its branch is at `<tip-sha>`"*. The clock is the build
   check read about the tip's own sha; past `MergeWork.ForgeCatchUpWindow`
   (fifteen minutes) since that build was first read, with no fresher one:
   **hold** instead, *"its pull request has not caught up with its branch in
   15 minutes - it names `<mergeStateSha>`, its branch is at `<tip-sha>`"*.
   With no build check at all about the tip there is no clock, and the row
   stays **pending** regardless of how long it has waited. This runs before
   item 9 (**update**), so a branch the forge has not caught up with is
   never handed to `MergeTrain.UpdateApprovedAsync`, which would otherwise
   comment "skipping until a fresh verdict" and never try that sha again.
9. Clean but not holding the trunk: **update**, *"bringing its branch
   current from `<trunk>`"*.
10. A build about that sha still `pending`: **pending**, *"its build on
    `<sha>` is still running"*.
11. A build read, but about a different sha than the merge check's own:
    **pending**, *"no runner has read its build on `<sha>` yet"*.
12. Holding the trunk, with no build checks at all yet: **pending**, *"its
    branch holds `<trunk>` - no checks have run on it yet"*.
13. Every counted build `passed`: **merge**, *"its branch holds `<trunk>`
    and its build passes"*.
14. What is left is a build that read no checks at all. Inside
    `MergeWork.SettleWindow` (ten minutes, the same settling a push's checks
    are given elsewhere) - counted from the later of the build's own
    `ShaSince` and `PullRequestHeadSince`, the time the forge's pull request
    reached that same sha, when the two agree on the sha; a mismatch has
    already returned at item 8 above, and an issue read before HA-1299's
    migration has no `PullRequestHeadSince` and counts from `ShaSince` alone,
    exactly as it always did: **pending**, *"its branch holds `<trunk>` and
    no checks ran on it"*. Past it, in this order:
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
above - item 13, and the two **merge** sub-cases of item 14 - is
**hold** instead, *"its pull request changes `<path>` - a person
merges it on the forge after reading it"*, when any counted clean
verdict names a playbook path (HA-431) or, checked next, a dependency path
(HA-2278) - the playbook check wins when a branch somehow names both. Every
other item above - the pushback, the update, every pending, and the holds
already in the list - is unaffected by either flag. `MergeWork.Judge` takes
an optional trailing `fleetLacks`, appended as `" - {fleetLacks}"` onto the
dependency sentence alone; only `AttentionController` supplies one, from
`Dispatch.FleetLacksAsync` against the branch's own parsed declaration, so
the merges column and `hatch queue` always show the base sentence with no
clause.

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

**A contribution in the approval column.** A pull request nobody on the board
knew is filed by the project's elected driver as a ticket in review with a
[contribution record](#contribution) naming its filer, author, association
and head branch; that record's branch is the pull request's head branch, and
is the same pointer every issue with a pull request has: the review judge and
the merge column's judge look for it on origin and not for a name made from
the issue's key, and a missing one says so by name
(*"its recorded branch `…` is not on origin"*). A pull request Hatch itself
opened brings its issue's chain in an envelope, and the filer stands the
epic and story above it in review as a **mirrored tree**, each with its own
record and no pull request. Approval is then given where the work is read:
a person pre-approves the mirrored epic, and nothing else. A pre-approval
reaches every descendant, so the approve hop carries each contribution child
into the merge column once its branch holds the trunk and its build passes,
the comment it writes naming the ancestor the pre-approval came from; the
[merge train](#the-merge-train) merges each pull request and `/merged`
closes the ticket; and the close hop closes the epic once every child is done.
Nothing in between is a person's move. A child that is red or conflicted is
not carried, is held for a person while its project has not set *runners work
contributions* (`ReviewWork.HeldContribution`), and leaves the epic open
behind it while the others merge — `WorkControllerTests`'
*PreApprovingAMirroredEpic* cases pin both. For what a person reads, see
[Contributions](../src/Hatch.Web/apps/hatch/docs/review-pull-requests-and-the-merge-train.md#contributions).

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
(`Runners.cs:391-411`) elects, per project, the longest-live loop that holds
a checkout of it — `Dispatch.HoldsCheckout`, the same rule
`WorkController.GetReview` uses for its own eligibility, so the two can
never disagree. With the wall up, a project that has an owner is driven only
by a runner whose key belongs to one of that project's owners or approvers
(`EfHatchRunner.ForPersonId`, rewritten on every beat, against the raw
`ProjectMembers` rows): a stranger's runner, or one on a key that belongs to
nobody, is not a candidate however long it has run, and a project with no
qualifying runner live has no driver, so its pull requests wait. With the
wall off, or on a project with no owner, a role means nothing and every
holder of a checkout is eligible. Being a site admin is not being an owner.
Live means `LastSeenAt` within `Hatch:RunnerGoneAfterSeconds`
(default 90) of now, and a `once` runner is never a candidate, since its own
`BeatAsync` throws the instruction away (`Runners.cs:482-484`). A retired row
(`r.RetiredAt == null` in the same filter) is not a candidate either, however
recent its `LastSeenAt`; its own next beat clears the retirement and makes it
one again — see [Runners on the board](#runners-on-the-board). A runner whose
live loop sessions say the forge fails (`Forge == RunnerForge.Fails`, HA-2708)
is not a candidate either: it can merge nothing, and electing it would hold
the project until the stall watch passed it over, again for each pull
request. The runner is the unit — one failing live session leaves the whole
key name out, whether or not its agent is down — and a runner that said
nothing or said `works` is a candidate as before. The word is read on every
election, so the runner stops driving on the beat that says `fails` and drives
again from the next beat after one that says `works`. A runner left out this
way is never passed over: no `driver_stalled` event, no `TrainStalledDriver`.
A row's
`LastSeenAt` is moved by its own heartbeat and by the heartbeat or token
release of the claim it holds, found by the claim's `ClaimedBy` matching
the row's `Name`; that is what keeps a driver live and elected through a
long increment. It is not moved by another row's claim, even at the same
`Where`, so a ghost row left by a loop that came back under a new name
ages out. Ties go to
the oldest `FirstSeenAt`, then `Name` ordinally (`Runners.cs:557`) — deliberately
stable, so a project's driver only moves once it actually drops out of live.
The election rides the heartbeat: `Drives` on `RunnerDto`/`RunnerInstructionDto`
(`Dtos.cs:4022, 4136, 4487, 4508`) carries the elected project keys back, and
the CLI learns them into `ReadoutState.SetDrives`/`Drives`
(`Readout.cs:106-112`, folded on every real heartbeat at `GoToWork.cs:1387`),
which gates the acting of `MergeTrain`'s six halves (`MergeTrain.cs:247, 423,
521, 637, 813, 865`): a runner acts only on a project it was elected to drive.
Both heartbeats answer `Drives` from the one election, `TrainDriversAsync`
(`RunnersController.cs:606`): the bare beat, `POST /api/runners/{name}`
(`:456, :491`), and the session heartbeat, `POST /api/runners/sessions`
(`:330`), which `go-to-work` has sent since HA-2040. So a key that only ever
runs as a session is told what it drives; a session answer that carried `[]`
would leave no loop driving any project's train. The
one exception is the pull request half's `merged` read: reporting a merge
somebody already made needs read access alone, so any runner holding a
checkout reports it for a merge-column row, driver or not.

**Two reports of one merge.** The driver reports the merge it made, and a
runner that sees the same merge on the forge reports it too, so two requests
for one issue can arrive in the same instant. `EfHatchIssue.StatusId` is a
concurrency token: every `UPDATE` of an issue carries the column it read, so
the request that read second and wrote second finds no row to change, and its
whole save is refused with nothing written - no event, no task moved, no
changelog task filed. `MergedWork` then reads the issue again, and the gate
answers the loser with the same `409` an issue already past review gets, which
a runner already walks on from. The first report moves the issue once. The
same holds for every other move that takes no claim - the hops, `verdict`,
`pushback`, `landed`, the issue edit and move routes, and a remand: their
loser gets a `409` ("changed column while this request ran, so nothing was
written") from `ConcurrentMoveFilter`, not a `500`. `brought-current` never
moves the issue, so a repeat of it is still deliberate.

**Its own interval, beside the poll's.** `MergeTrain.Start(runtime,
TimeSpan.FromSeconds(mergeInterval))` (`GoToWork.cs:812`) runs `TickAsync`
once, then waits `--merge-interval` seconds (default 5, `:466`) —
independent of `--interval` (default 60, `:465`), which paces the poll that
reads build and merge-conflict verdicts. Cross-reference
[Checking the branches in review](#checking-the-branches-in-review), which
already says the pull-request, merge and update halves "run on their own
clock... rather than this poll's" (`:4358-4361`) without naming the clock —
this new section is that explanation. The head's own build is read on this
clock too (`ReadBuildsAsync`): the sha and branch come from the head's
clean merge check, so it needs no git, and a pass is noticed within one
`--merge-interval` of the forge reporting it rather than whenever a runner next
finishes an increment. A verdict that concluded is read once per sha; a
`pending` one, and a `none` inside the poll's ten-minute window, are asked
again every tick. The same half also reads the build on every review-column
row's pull request head, for the project's driver only; that pace is the
[review rows' own](#checking-the-branches-in-review), at `--interval` rather
than every tick.

**A merge with no checks rides the same train.** A merge judged under the
last item of the judge's list — a project that requires no checks, or a forge
that calls the pull request clean on the judged sha with no checks having run
— is a `merge` like any other: same head, same order, the same `gh pr merge
--merge --admin --match-head-commit`. Both outcomes are `WorkKinds.Merge`,
which `MergeTrain` already carried, and `MergeTrain` itself did not change for
it.

**A row the train brought current keeps the head.** The head is re-picked on
every read, so a row that became eligible again ahead of one the train had
just updated would take the head back and throw the build away. When the
train brings a branch current it says so with `PUT
/issues/{key}/brought-current`, which stores `BroughtCurrentAt` and
`BroughtCurrentTrunkSha` on the issue and writes a `brought_current` event. A
row so marked keeps the head over an otherwise-earlier row for as long as the
mark names the trunk sha its own clean merge check still reads, it is neither
a hold nor a pushback, and it has not changed column since the mark; of
several marked rows the latest mark wins. A mark outside those terms simply
stops counting, and the head is the first eligible row in order as before. The
route gives a `409` to a row outside the merges column.

**A head nobody acts on is passed over.** Every heartbeat judges the stall
(`RunnersController.TrainDriversAsync`, `RunnersController.cs:578-640`,
the `TrainWatch.Observe` call at `:600`). A head is judged only when its
`MergeWork.Judge` kind is `merge` or `update` and some live eligible runner
drives the project; any other state - no head, `hold`, `pushback`, `pending`,
or no driver - clears both watch columns on the project and
empties the roster described below (`TrainWatch.cs`). The one exception is a
project with a non-empty roster whose head has not changed: with nobody left
to elect, the watch and roster stay and the clock restarts, so a runner that
turns up later gets a full window. The clock, `TrainWatchedSince`, is when this head
was first *seen* acting, not when it entered the column; a different head,
or a null `since`, restarts it (`:43-48`). The bound is measured from that
`since`, raised to `BroughtCurrentAt` when that is later (`:50-53`): a
`brought_current` mark restarts the count, a pushback clears it, and a
completed merge changes the head and so restarts it. The bound is
`Hatch:TrainStallMinutes`, default 10 (`HatchOptions.cs:102`), read through
`Runners.TrainStallSeconds`, where zero or a negative falls back to the
default (`Runners.cs:59-62`; the reason is at `HatchOptions.cs:96-100`).
Past it, the beat sets `EfHatchProject.TrainStalledDriver` to the runner
and gives the project a fresh window (`Entities.cs:211`, written at
`RunnersController.cs:612-613`). `Runners.EligibleCandidates` then filters
out `c.Name != project.TrainStalledDriver` (`Runners.cs:525`), so the next
election skips that runner on this project only; it stays eligible on every
other (comment at `:521-524`). A heartbeat is proof a
runner is live, not that it is acting, so the runner's own next heartbeat does
not clear the mark while another eligible runner is live: `ReinstateAsync`
(`RunnersController.cs`, called from the beat) asks `Runners.EligibleNamesAsync`
and reinstates, with a fresh window, only when no other name is on the project's
list. A pass-over therefore lasts until the runner elected in its place has been
passed over in turn - a full window later - or until no other runner is live, or
until the head changes or clears (`TrainWatch.Observe` resets the mark with the
roster). With two eligible runners and neither acting, the head alternates
between them one full window at a time, and each pass-over writes one event. The
mark is only the latest pass-over, so the
beat also adds the runner to `EfHatchProject.TrainStalledDrivers`, a roster of
every runner passed over on the current head; a reinstatement leaves it, and a
changed or cleared head empties it. `GET /api/attention` lists the project's
`trainStalled` row exactly when the roster is non-empty and holds every live
runner eligible to drive the project (`Runners.EligibleNamesAsync`), carrying
the head issue's newest comment as `reason`. Each window writes one `driver_stalled`
event on the head issue, actor `Hatch` (`RunnersController.cs:510, 630`),
with payload `{ passedOver, elected }`, and `elected` null when nobody else
was eligible (`:628-634`; `Entities.cs:2360-2367`).

A second clock stands beside this one: the [`train-ready` finding](#train-ready)
opens a stored row a person can read, on its own bound, independently of
whether this one has passed anybody over.

**The queue says who is driving.** A merges-column row that reads as
actionable - kind `merge`, `update` or `pending` - and is not waiting behind
a head gets a clause naming the driver, in `Dispatch.BlockedWithKind`
(`Dispatch.cs:1831`, driver branch `:1851-1856`):
`"{judgement} - for the driver, last heard from {n} ago"` (`:1854`, with
`Minutes` at `:916`). When no runner is elected the whole sentence is
replaced by `no live runner is eligible to drive this project's merges`
(`:1855`). A `hold` or `pushback` row gets neither, since it already
explains itself (`:1858`). A row behind the head keeps `waiting its turn
behind {headKey}, position {n} in the train` unchanged, because that clause
is tested first (`:1842-1845`). The driver comes from
`Runners.ProjectDriversAsync` (`Runners.cs:421-433`, read at
`Dispatch.cs:270`), the same election the heartbeat uses, so the queue and
`Drives` agree; a runner just passed over stops being named the moment it
is passed over, through `EligibleCandidates`. It is a pure read: a driver
past `Hatch:RunnerGoneAfterSeconds` turns the head's sentence into the
no-driver one on the next scan, with no beat needed. The sentence reaches
`hatch queue` and `GET /api/work/queue` as the row's ordinary blocked
sentence, with no new field.

**A pull request on the wrong ticket is put right by the runner.** In the merge
train's tick (`MergeTrain.TickCoreAsync`), `PullRequestRepair`
(`MisplacedPullRequests.cs`) runs after the pull request half reads the open
pull requests and before the outside filer looks at them, taking the tick's own
list so a checkout is listed once. For a review row the runner drives - not in
the merges column, landing by pull request, with no pull request recorded and
one merge check on a branch - it takes the open pull requests whose head is
exactly that branch, from the repository itself and into the trunk. Exactly one
is the match; none, or two, writes nothing. If no issue holds it, the issue
records it, the call `hatch pr` makes, and says so in a comment. If another
issue holds it and a key recorded it there (`pullRequestRecordedByKind` is
`key`, and the issue is no filed outsider), the runner clears the other issue
first and then records, so a failure between the two leaves it on nobody and
the next tick finishes. A person's record, one the board does not say who made,
and a filed outsider's are never moved: the issue in review is told once per
runner run which ticket holds it, and neither issue changes. The filer runs
after, so it finds the pull request recorded and never files it as an outsider.

**A waiting pull request is opened by the driver.** In the same tick,
`WaitingPullRequestOpener` (`WaitingPullRequests.cs`) runs right after the
repair and before the filer, handed the repair's own instance so what it has
told a ticket is shared, and the tick's list of open pull requests. A row is
the opener's only where the runner drives its project, it is in review (no
`MergeWork`, no `LandingWork`), its project lands by pull request, no pull
request is recorded, `ReviewCheckDto.WaitingPullRequest` is set and exactly one
checkout of this runner's is the repository the text names (`Checkouts.ToPoll`
narrowed to the entry whose remote equals the stored one, ignoring case; with
none stored, the one entry). The trunk is the repository's base branch, else
`HATCH_BASE_BRANCH`, else what origin calls its trunk. In order:

1. An open pull request from the stored branch (the repair's own match: open,
   head ordinal, not from a fork, base the trunk) is handed to
   `PullRequestRepair.PutRightAsync` - recorded, as the repair records it, and
   nothing is opened. Two such pull requests write nothing.
2. The issue is read again; a url recorded or the text gone ends it. Then
   `IWorkspace.BranchHolds(branch, sha)` asks origin: `Heads()` (one
   `ls-remote`) says gone or equal, and only a tip that differs is fetched, so
   that `git merge-base --is-ancestor` can tell a branch that moved on from one
   rewritten. A branch gone, or whose tip is neither the stored sha nor a
   descendant of it, is left alone: one terminal line and one comment on the
   ticket, each once per run (keyed on the key, branch, sha and state, and let
   go again if the comment fails). A fetch that does not answer says one line,
   no comment, and the next tick asks again. This is the one place the tick
   reads git, and it moves no branch and no tree.
3. `OpenPullRequestAsync` on the watched forge, with the stored title and body
   unchanged - so a refused sign-in feeds the forge check, and the next
   heartbeat says `fails` and the election moves on. gh answering that the pull
   request already exists returns its url, so a retry is safe. A failure is one
   line, `hatch: KEY - could not open its pull request - why`, and the text
   stays for the next tick.
4. The url is recorded with a patch - which clears the text - and the ticket's
   chain is written into the pull request's body by the code `hatch pr` shares
   (`PullRequestEnvelopes.TryWriteAsync`); an envelope failure is one line.
   A failed record leaves the text and the pull request open, and the next tick
   finds it in the list and records it.
5. One comment names the runner that opened it, who left the text, and the
   url.

It asks nothing of a person, edits neither the title nor the body, and with no
driver the text waits.

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
See [the five paths past the terminal
guard](#the-five-paths-past-the-terminal-guard) for why a program may take
this step.

**Work that landed nothing.** An issue whose session changed nothing has no
commit to land, and waiting for one would hold it in review for good. It
declares that instead - [finished with no code](#finished-with-no-code) - and
the declaration stands in for a landing: the attention panel lists it for a
person to close, and a pre-approved one, or one under `NoCodeCloses`, is carried to the terminal
column by the close hop, neither reading a landing. `hatch landed` records nothing for it.

**The queue row.** `Dispatch.KindOf` takes `LandingWork`'s kind for a
trunk-landing review row, where a pull-request row would have `conflicts`,
`build` or `advance`, and `Dispatch.Blocked` makes the judge's sentence the
row's `Blocked`: nothing in the review column is ever an agent's to do for a
landing, so the sentence is reported and no session is spawned. The panel
lists only the **hold**s, as `landingHeld` — see [what is waiting on
you](#what-is-waiting-on-you).

### The five paths past the terminal guard

Nothing above ever lands an issue in a terminal column, and nothing above
ever lands one in a protected column either — that is what `Columns.Target`
is for, dispatching review to itself rather than past either, and what
`Dispatch.Blocked` refuses in every other case with *"the next column is
\<name\>, and only the operator moves work there"* or *"...and only its
approvers move work there"*. Five routes cross one of the two guards
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
Two reports of one merge cannot both move the issue: the second is turned
away with the gate's own `409` (see [the merge train](#the-merge-train)).

The **approve** hop is the second, and it crosses the *protected* fold
rather than the terminal one: `Dispatch.Blocked`'s own protected check
reads `to.IsProtected && hopKind != HopKinds.Approve` (`Dispatch.cs:1252`),
so a pre-approved issue in review whose branch `MergeWork.Judge` would
merge or bring current is the one case a dispatch lands work in a
protected column at all. It is a path past the guard and not a hole in it
for the same reason `/merged` is: the move in is a decision an owner or
approver already made, in advance, on the ticket — pre-approval is the
approve press given before the fact, not a program judging for itself that
a branch is fine to ship.

The **close** hop is the third, and it crosses the *terminal* fold instead,
beside `/merged`: `Dispatch.Blocked`'s own terminal check reads
`to.IsTerminal && hopKind != HopKinds.Close` (`Dispatch.cs:1249`), so a
parent in review with no pull request recorded, and every child done, is
carried straight into the first terminal column right of review, with nobody
pressing anything on the board, for either of two reasons: it is
pre-approved, or its project's *A passing check approves the merge* setting is
on. (A third route is a declared one: an issue that declared itself [finished
with no code](#finished-with-no-code) is carried the same way with no children
at all, when it is pre-approved or its project's *A no-code declaration closes
the ticket* setting is on; the merge setting does not carry it.) The reason is
the same as the approve hop's: the move is a decision an owner or approver already
made in advance, whether on the issue or on the project's setting, carried out
the moment the board can see it is true, rather than the operator's own press
on a forge or a card.

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

A check's pass into the merge column is the fifth, and it crosses the
*protected* fold the way the approve hop does: `Columns.Approves` names the
merge column only when the project has *A passing check approves the merge* on,
the issue stands in whatever column is measured as review, the column after it
is both protected and ticked `MergesPullRequest`, and the issue carries a pull
request - the same no-pull-request test `IssuesController.MergeColumnRefusal`
holds a drag to. `WorkController.VerdictWork` acts on it with no call to
`Dispatch.Blocked` or `ProtectedMoveRefusalAsync`, the same way the hop and
`/merged` do. It is a path and not a hole for the reason the approve hop is
one: the owner's or approver's decision was made in advance - on the project,
rather than the ticket - the moment *A passing check approves the merge* was
switched on, and the apply carries that decision out rather than making one of
its own.

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
   an agent and having that agent stop working on it would read backwards. An
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
`OwnerPersonId`, set only by an admin at the Runners page, and a caller whose
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

`GET /api/work/queue` answers with every issue the dispatcher considers,
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

**Each sentence carries a kind beside it.** `QueueEntryDto.BlockedKind` names
which of seven values folded the row — null exactly where `Blocked` is null —
so a client groups the queue, or colours a card by it, without reading
English: parsing the sentence on the client would be a second opinion about
what the server already decided, and would break the first time a sentence is
reworded. `working` is the issue's own live claim; `wip` a full WIP section, or
an epic at its own limit; `dependency` an unmet edge, its own or an ancestor's;
`turn` a row parked behind higher-ranking clear work, or waiting its turn in
the merge train; `family` a parent, a child, a sibling in flight, or a
relative's claim; `person` everything only a person resolves — an unanswered
question, an assignment, a hold, a pause, or a next column only a person moves
work into; and `other` every fold none of the six name — a repository this runner lacks, a
runner tag it does not advertise, a ticket omitted for it, a flagged-dangerous issue it refuses, a model it cannot
spawn or refuses, a column it does not work, a project that allows only signed
commits when it has not said it signs them, a step that ends in a pull request
when the runner's forge sign-in fails, or a
clean, checked review row a passing check would carry into the merge column
but no check playbook covers the move for, among them: no new kind.

**Two sentences come from a project's level.** A row in a paused project
reads *project HA is paused - a person set it aside, and nothing in it picks up
until they set it back*, kind `person`. A clear row in a project at normal,
while an expedited project has a clear row, reads *HA-216 is in expedited
project HA and is clear - nothing in a project at normal is picked up while an
expedited project has work available*, kind `turn`. Each row's own project
level is `QueueEntryDto.ProjectPriority`.

**A ticket this runner let go of is folded for it while another could take
it.** Send `?runner=<name>` (or `?asRunner=`) and a row whose newest claim
outcome is that runner's `dropped` release reads `<runner> let this go N
minutes ago, and another live runner could take it - it is left for them while
one can`, kind `other`; every other runner's row is unchanged. See [A ticket a
runner let go of](#the-dispatcher) for what counts as another runner that could
take it. A name matching no row is fine, and no `runner` folds nothing.

**A row left for a preferred runner is folded for an unmarked one.** The same
`?runner=<name>` (or `?asRunner=`) folds a clear row while a runner marked
[preferred](#runners-on-the-board) is live, idle and able to take it: `HA-12 in
In Progress is left for build-box, which is preferred and free`, kind `other`.
It is said last - after the let-go fold and every other fold the asker owns -
never on a hop, and never to a preferred runner. `work/{key}` carries the same
sentence as `blocked`, and `work/next` skips the row. A name matching no key
folds nothing.

**A ticket omitted for this runner is folded for it too.** The same
`?runner=<name>` (or `?asRunner=`) also folds a row the runner is
[omitted](#omitted-runners) from, its own or an ancestor's: `HA-9 is not worked
by gpu-1 - it recused itself 3 minutes ago (no toolchain)`, or `HA-9 is not
worked by gpu-1 - operator omitted it on HA-2 3 minutes ago (no toolchain)` for somebody
else's, kind `other`. Unlike the let-go fold it is not left to another runner -
it holds whatever the others are doing - and `work/{key}` and a hop read
`?runner=` for it as well, though neither runs the let-go fold. See item 10 of
[the dispatcher](#the-dispatcher).

**The rows arrive in the board's own order.** Rightmost column first, and
within a column the `(rank, id)` that `GET /api/board` serves that column
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
assignee, an unanswered question, a repository this runner lacks, a runner tag the
project asks for and this runner lacks, a ticket omitted for this runner, a flagged-dangerous issue a runner that
refuses dangerous work was asked about, a model the runner cannot spawn, a model the
runner refuses, a column it does not work, a project that allows only signed
commits it has not said it signs, a step that ends in a pull request for a
runner whose forge sign-in fails, an unmet dependency, an open child, a
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

**An ambition's own pass has its own two sentences, from `FamilyGate.PassFold`.**
Where fewer epics are open than the ambition's own limit allows, it is clear;
at the limit, *"{N} of its epics {is/are} open, and it runs {M} at once - the
next pass runs when one closes"*. Where room is open but a pass already ran
and nothing has reopened it, *"a pass ran on it {N} {unit}s ago and nothing
has opened room since - the next runs when an epic under it closes or a
person changes it"* instead. See [the eighteenth condition](#what-makes-an-issue-actionable)
and [running a pass](#running-a-pass).

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
supported agent (claude-opus-5 for claude; gpt-6-luna for codex; grok-4.7 for grok; qwen3-coder:30b for ollama) - not "nonsense-1"`) and the CLI
reads. **Pinning an id is choosing an agent.** A runner whose agent
(`HATCH_AGENT`, or `--agent`) is not that family skips the row before claiming
it and says so under [what the pass skipped](#what-a-pass-skipped); `work <key>`
refuses with the same sentence, `--dry-run` prints it, and the ticket waits for
a runner of that agent (a board with none leaves it sitting). The sentence is
`pinned to {model}, a {agent} model - this runner's agent is {agent}` (`an
ollama model` before a vowel). An alias, or no model, is never skipped, and
`--model <alias>` on `work` beats the playbook and proceeds. (`claude-…` is
the Claude family's shape, not the shape of a pinned id in general; `gpt-…`
is the Codex family's shape; `name:tag` - for example `qwen3-coder:30b` - is
Ollama's, with the tag required, no `ollama/` prefix (the id is what `ollama
list` prints), and the whole id at most 60 characters: a bare `llama3.3` is
not a pinned id.)
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
(`PATCH /api/issues/{key}/playbook`); reading one is anybody's who can
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

Eleven rows are seeded, for the same reason the columns are: a Hatch whose agent
loop cannot run until somebody fills in a table is a Hatch that ships broken.
Seven come from `Playbooks.cs` (20260903204217) directly: the three
breakdown rows, the epic start, the analysis row, the implementation row and
the epic verification row. The eighth is the stock review playbook, which
`SeedConflictPlaybook` (20260928015312) adds, for every type, at `sonnet` and
`high`. The ninth is the closeout row `SeedCloseoutPlaybook` (20260930235751)
adds beside the implementation row, which it narrows to `leaf`. The tenth is
the stock reflection row `SeedReflectionPlaybook` (20261020234815) adds, on the
same `Breakdown` → `Backlog` transition as the three breakdown rows, for
`reflection` alone, at the epic-breakdown row's own `fable` and `max` — the first
seeded row to carry a `deniedTools` list. A feature that
does nothing until somebody fills in a table is the same failure as the loop
that cannot run, so the review migration seeds its row only where no row from
the review column to itself exists; an operator who deletes it turns conflict
and build work off, and nothing puts it back. The other nine cover every
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

Every one of the ten either edits the tree or judges it:

| Transition | Types / Shape | Name | Edits or judges |
|---|---|---|---|
| Breakdown → Backlog | `epic` | breakdown (epic) | judges |
| Breakdown → Backlog | `story` | breakdown (story) | judges |
| Breakdown → Backlog | `task,bug` | breakdown (task/bug) | judges |
| Breakdown → Review | `reflection` | breakdown (reflection) | judges |
| To Do → In Progress | `epic` | epic-start | judges |
| To Do → In Progress | `''` (any type) | analysis | judges |
| In Progress → In Review | `''`, shape `leaf` | implementation | **edits** |
| In Progress → In Review | `''`, shape `parent` | closeout | judges |
| In Progress → In Review | `epic` | verify-epic | judges |
| Review → Review | every type | the review row | **edits** |

*Judges* here means by instruction only, with one exception. None of the other
nine is enforced by `IsCheck` or `deniedTools`: each is told in its prompt to
specify, verify or report rather than to write the code, and nothing but the
prompt keeps a session to that. The exception is the reflection row, whose
`deniedTools` list removes the file writers (`Edit`, `Write`, `NotebookEdit`)
and the git writers. It is still not a check — no migration sets `IsCheck` on
it — and it keeps `Read`, plain `Bash` and the `hatch` verbs, so it still reads
the repository and its files and moves its own ticket. Only a [check
playbook](#check), paired with denied tools, takes the write tools away.

**The reflection row reads back what earlier reflections predicted.**
`RewordReflectionPlaybook` (20261021180000) rewrites the row's `DefaultPrompt`
alone, only where it still reads the stock text byte for byte, as
`RewordImplementationPlaybook` does: `Prompt` and `UpdatedAt` are left
untouched, so an operator's own wording survives. `ReflectionFilesByLinkEndsInReview`
(20261111093343) rewords it again the same way, and separately retargets the
row's `ToStatusId` from Backlog to the review column, measured the way
`SeedConflictPlaybook` measures it, only where the row is still exactly on
`Breakdown` → `Backlog` for `reflection` - routing alone says an operator has
not moved it. The report it asks for has eight sections, in order: Window;
What ran and what it cost; Where the time went; Findings; Recommendations;
Hypotheses read; Hypotheses filed; What reflection could not see. The stock
prompt also files an instrumentation gap that lies in the tracker itself,
rather than in the project reflected on, as an issue with `filedFromKey` set
to the reflection and no parent in whichever project
`GET /api/projects` flags `"bindsTracker": true` (see
[Project](#project)), leaving it in the report as prose instead where none is
flagged. A hypothesis is an issue filed with `filedFromKey` set to the
reflection and no parent, a `readyAt` on its read-after date, a dependency
edge (`hatch depends <hypothesis key> <key>`) on the issue its `Change` line
names, and two sections in its description: the `## Hypothesis` block of
four lines - the metric, the baseline (its window and value), the change
(what moves, and the key of the issue carrying it) and the read-after date -
and a `## How to read it` section naming the comparison, the verdict rule
and a no-code declaration, so the session dispatched to it on its own date
can measure it without the reflection. A later reflection finds earlier ones
with `GET /api/issues?type=reflection&projectId=` and then each one's
filed issues with `GET /api/issues?filedFromKey=`, reports the verdict
already commented on any it finds, and measures only one whose read-after
date has passed and carries no verdict itself - the same way: an after
window as long as the baseline, ending on the read-after date, through the
same folds, called *confirmed*, *refuted* or *unreadable* — the metric is no
longer produced, or the window is too thin to say — and commented on the
hypothesis issue. A refuted hypothesis whose change landed as code gets an
issue filed with `filedFromKey` set to the hypothesis to revert or amend it.
`Hypotheses read` is never left out: with no earlier reflection, or none due,
it reads `None` and says why.

`MoveReflectionChildrenToFiledFrom` (20261111084455) moves what earlier
reflections filed under themselves to the shape that replaces it. Every issue
whose parent is of type `reflection`, in every project and every column, closed
ones included, gets `FiledFromId` set to that reflection and `ParentId`
cleared, with a `filed_from_changed` and a `parent_changed` event written by
the board, the pair an API edit would have written. The rule is *children of
reflections*, never a list of keys, and running it twice writes nothing the
second time, since the first run leaves no child with a reflection for a
parent. A child that was pre-approved only through its reflection is no longer,
since pre-approval travels by parentage alone. It closes nothing: a reflection
left with no children is for a person to close.

**The epic-breakdown row consults the Dream Board.**
`RewordEpicBreakdownForDreams` (20261104120000) rewrites that row's
`DefaultPrompt` alone, only where it still reads the stock text byte for byte,
as `RewordReflectionPlaybook` does: `Prompt` and `UpdatedAt` are left
untouched, so an operator's own wording survives and a row handed back receives
the new text. The paragraph it adds to the prompt sits between *Read what has
already happened* and *Ask the repository*: search `GET /api/dreams?q=`,
read what bears on the epic, quote it, and record the dreams drawn on with
`PUT /api/issues/<KEY>/dreams`, or say *no dream bore on this*. It
applies to every epic breakdown, woven or hand-written. The story and task
breakdown rows are not reworded, since a story's seed is its epic. This adds
reading, not writing, so the row stays *judges*.

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
board that did not already have one — so the count stays at ten on a
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
when its prompt is the operator's own*, above). `RewordImplementationPlaybook`
is the first to take that shape: it tells the implementation row's
green-before-pushed bullet to render a changed screen in the headless driver
(`docs/browser-testing.md`) rather than leaving all browser verification to
the operator, guarded and written on `DefaultPrompt` alone — no row's own
`Prompt` is read or touched, and `UpdatedAt` does not move.
`PushWorkThatIsNotGreen` is the second, and rewords that same bullet: work that
is not green is still pushed. Checks that cannot run on the machine (a missing
toolchain) are no reason to hold work back — push, open the pull request, say
which check was not run, and let the pull request's own checks be the gate —
and work that is red and cannot be fixed is committed and pushed for the next
runner, never put up for review, with the ticket left where it is.

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
adapter rather than written into the loop. Today there are three — `claude`,
`codex` and `grok` — each its own implementation, not a second loop.
`HATCH_AGENT` chooses which, and `HATCH_CLAUDE_BIN` / `HATCH_CODEX_BIN` /
`HATCH_GROK_BIN` say where that agent's own executable is when it is not on
`PATH` (another agent's binary is named by a setting of its own). `--agent
<id>` on `hatch work` or `hatch go-to-work` beats the setting for that run,
and an id nobody has an adapter for is refused before anything is spawned.
The agent also decides which rows the runner takes: a row pinned to another
agent's model is skipped (see the `Model` paragraph under
[Playbooks](#playbooks)).

#### Grok Build

The vendor's own CLI, `grok`, read against its headless-scripting guide
(`https://docs.x.ai/build/cli/headless-scripting` and the bundled
`14-headless-mode.md` in `xai-org/grok-build`) and then confirmed live on
HA-918, 2026-10-19, against a real `grok --version` of `1.0.62 (1474f9cbdd91)
[stable]`.

**The flags.** An unattended spawn is `--prompt-file <path> --verbatim
--output-format json|streaming-json --always-approve --trust --no-auto-update
--effort <level> --cwd <root>`, plus `-s <uuid>` for a streamed run and
`--deny`/`--disallowed-tools` for whatever the playbook's denied-tools rules
translate to (below). `--prompt-file` exists because the CLI never reads
stdin, and a prompt handed as `-p <PROMPT>` would sit in the process list and
risk Windows' own ~32,000-character argv limit; `--verbatim` stops it reading
a machine-composed prompt as anything but literal text. `--trust` is the
grant: the vendor gates `AGENTS.md`, project skills and hooks behind a
per-folder trust decision, and a runner cloning its own workspace has no
prior one — confirmed live, an untrusted real checkout reads no project
instructions at all, and the same checkout trusted reads `AGENTS.md` and
quotes it back correctly. `--no-auto-update` keeps a background update from
changing the binary mid-loop. None of `--trust`, `--prompt-file`,
`--verbatim`, `--always-approve` or `--no-auto-update` appears in `grok
--help` — headless scripting is covered by the vendor's own guide, not the
CLI's own help text — but all five are real, accepted flags: an unrecognised
one is refused outright ("unexpected argument"), which none of these are.
Resuming is `grok -r <session-id>`, confirmed live to find a session by its
id alone regardless of which directory the join runs from.

**The tiers.** A real `grok models` on the account HA-918 ran against lists
exactly one model, `grok-4.7` (the CLI's own default) — no second, smaller or
larger id to give `haiku` its own tier below `sonnet`, or `opus`/`fable` one
above it. All four of the playbook's tiers therefore ship unset, taking
whatever the CLI's own default resolves to; a pinned id a ticket or playbook
names directly still passes through `-m` unchanged.

**The effort rule.** Grok's own words are `low`, `medium`, `high` and
`xhigh` — not `max`, the playbook's own top word. Handing it `--effort max`
does not clamp to the account's own ceiling, it refuses outright ("unknown
effort level 'max'; use one of: xhigh, high, medium, low"), confirmed live.
`max` is mapped to `xhigh` before the flag is built, the same word Codex's
own effort already uses for the same reason; every other word, recognised or
not, passes straight through for the real CLI to accept or refuse on its own.

**Denied tools.** The playbook's own tool names map onto `--deny` rules
Grok's Claude-compatibility layer already recognises — `Bash`, `Read`,
`Edit`, `Write`, `Grep`, `Glob`, `WebFetch`, `WebSearch`, with or without a
parenthesised pattern — and the bare literal `Agent` maps onto
`--disallowed-tools Agent` instead, since it is not one of grok's own
`--deny` tools. Everything else, including `Agent(...)` with a pattern, has
no equivalent and refuses the whole increment before anything is spawned,
rather than spawning it under a narrower claim than the playbook asked for.
Confirmed live: `--deny Edit` and `--deny Write` each refuse a real file
change under `--always-approve`, and `--deny "Bash(git push*)"` refuses the
exact command before it runs - a real `git push` to a real remote never
reached it.

**No hooks.** Grok's hooks load only from `~/.grok/hooks/*.json`,
`~/.grok/config.toml`, `~/.claude/settings.json`, or the project itself (the
last forbidden without `--trust`) — confirmed live, neither `grok --help` nor
`grok agent --help` names a `--settings`/`--hooks` flag to point elsewhere,
and `grok inspect` run inside a real, trusted checkout reports "Hooks
(None)". Writing into the operator's own `~/.grok/hooks` would leak into
every grok session on the machine, not just Hatch's; relocating `GROK_HOME`
would copy `auth.json` (a `grok login` refresh rotates the token under the
real login) and move `sessions/` out from under `grok -r`. So there are
none, on purpose, which costs exactly what it costs Codex: a `hatch comment`
sent mid-run waits for the session's next increment rather than reaching it,
and only the hard token limit ends a run that nobody is watching.

**Reach.** Grok documents no `--add-dir`; a sibling checkout is still
reachable, by its absolute path only.

**Cost.** Unpriced unless grok's own stream reports one — `total_cost_usd` or
the exact `total_cost_usd_ticks / 10,000,000,000`, confirmed live to be the
same number either way on a real turn — never zero.

**The limit.** No reset time has been seen in a real sentence yet - HA-918's
own investigation hit the account's free-tier limit mid-run, and the
message it got ("You've reached your free Grok Build usage limit for now.
Get SuperGrok for much higher limits, or try again later: …") names no
clock at all. Every Grok limit report rests the same one-hour backstop every
other unknown-reset limit does.

**Install and auth.** The vendor's own install page
(`https://docs.x.ai/build/overview`) puts the binary wherever it is signed
in from; headless auth is `XAI_API_KEY` or a cached `grok login` token,
neither this adapter's concern since a CLI refusing to run without one says
so on its own stderr.

**What could not be checked.** The account used for this proof offers only
one model, so no second tier's effort ceiling or deny behaviour was ever
exercised against a different id. The free-tier limit was hit partway
through, which cut the live run short of a full `hatch work` increment
end to end (claim, streamed progress, ticket comment, pull request, claim
release) - the pieces checked individually (argv, stream shapes, trust,
resume, denial, the limit sentence itself) all held, but that exact
end-to-end sequence was not independently re-confirmed once the limit hit.
Only `Edit`, `Write` and the `Bash(git push*)` pattern were exercised
directly among the denied-tools allow-list; `Read`, `Grep`, `Glob`,
`WebFetch` and `WebSearch` were not, though nothing found says they differ.

It has the same shape as `--model` and `--effort` and the opposite lifetime, on
purpose. Those are one operator's opinion about one increment, which is why a
night never carries them. Which agent a runner can spawn is a fact about the
machine it is on — where the binary is, whether it is logged in — and so is
true of every ticket that runner touches tonight. `go-to-work` therefore
resolves it once, before the loop starts, and carries it to the last
increment. That is deliberate: do not "fix" it to be read per increment to
match `--model`. `--agent codex` on the command line, or `HATCH_AGENT=codex`,
resolves the same way and at the same moment as `claude` always has — once a
run, never read back out of a playbook.

#### Codex

**`HATCH_CODEX_BIN` names the binary when it is not on `PATH`**, the same
shape `HATCH_CLAUDE_BIN` already has. Finding neither refuses with `hatch: no
codex CLI on PATH.`, a blank line, then `Install it, or point at one you
have:` and `export HATCH_CODEX_BIN=/path/to/codex` — no vendor URL, since one
drifts and the environment variable does not.

The tier table, confirmed on 2026-10-20 against a real, logged-in CLI rather
than guessed from a model list that turned out not to apply to every login: a
ChatGPT-account login's own reachable catalog is `gpt-reserve`, `gpt-6-luna`,
`gpt-5.6-terra`, `gpt-5.6-luna` and `codex-auto-review` — every other id tried
against it, including ones a vendor page names, answers the same refusal, *"the
'\<id\>' model is not supported when using Codex with a ChatGPT account."*
No id in that catalog reads as a tier above `sonnet`'s, so `opus` and `fable`
rest on it too rather than naming an id this login cannot reach:

| Playbook tier    | Codex model     |
| ---------------- | --------------- |
| `haiku`          | `gpt-6-luna`    |
| `sonnet`         | `gpt-5.6-terra` |
| `opus` / `fable` | `gpt-5.6-terra` |

A pinned id that is not one of those four aliases passes through unchanged,
the same way `ClaudeAdapter` never resolves `sonnet` itself. The effort table
is the playbook's five words verbatim, except `max`, which this CLI spells
`xhigh`.

**The grant is one flag, never two.** Under `exec`, `--approve-for-me` cannot
sit beside `-s`/`--sandbox` and already implies `workspace-write` on its own —
confirmed against `codex exec --help` on the installed CLI, which lists no
`--ask-for-approval` at all. A denied `Edit`, `Write` or `NotebookEdit` (bare)
downgrades the whole session to `-s read-only` instead, the only restriction
this CLI has that stands in for any of the three; `Bash` bare, any
`Bash(…)` pattern, `mcp__…`, and every other built-in have no sandbox-level
analogue and refuse the increment before the claim, naming the rule. Never
`--dangerously-bypass-approvals-and-sandbox`, which would remove the very
sandbox the downgrade relies on.

**Hooks do not fire.** `codex exec` silently skips every hook in a
`CODEX_HOME` overlay's `hooks.json` unless the spawn also carries
`--dangerously-bypass-hook-trust` — confirmed by running the identical overlay
twice, once without the flag (no hook ran) and once with it (both `PostToolUse`
and `Stop` ran). This adapter will not send that flag, the same stance it
already takes on the sandbox-removing one above, so it declares no hooks and
no `CODEX_HOME` at all; a session degrades to running with neither, which it
already does cleanly for any agent with nothing to hook.

**The resume line** a session prints, `codex exec resume <thread_id>`, rejoins
it the normal way — confirmed against a real session once its increment had
ended, which only works because the hooks door above is off and no overlay
`CODEX_HOME` was ever set for it to disappear with.

**Limits** recognise Codex's own usage-limit wording once a real sentence has
been seen; until then every report rests on the one-hour backstop also used
where a window is mentioned but no reset time is given, the same way an
unrecognised Claude limit does. There is no usage probe for Codex: a session's
own cost is read from the stream it already sends, nothing is polled on the
side.

**What could not be checked.** Whether API-key auth reaches a larger reachable
catalog than the ChatGPT-account login this proof used: the refusal sentence's
own wording ("not supported when using Codex with a ChatGPT account") suggests
it might, and it was not tried, so the tier table above is only as wide as that
one login. Every real run also printed `WARNING: proceeding, even though we
could not create PATH aliases: Refusing to create helper binaries under
temporary dir...`; it never stopped a `hatch` command from working, so its
cause was not chased. And whether `turn.completed.usage.reasoning_output_tokens`
is ever nonzero on this login's models: the field was present and `0` on every
stream read, so it is emitted, but no nonzero case has been seen. The stream
reader adds it to the output count when it is there, so a nonzero value would
be counted, but no real one has exercised that. Two further things that
did not happen in the proof: no real usage-limit sentence was seen (see
**Limits** above), and the refreshed `auth.json` was never compared, which is
moot while no session sets `CODEX_HOME`.

### What makes an issue actionable

Twenty-six conditions, the twenty-third a way out of the twenty-second rather than
one more gate, the twenty-fourth a fact about the issue rather than another way
out, the twenty-fifth a fact about the walk rather than either, and the
twenty-sixth a fact about the issue again, the same shape as the fifth. An
issue is the loop's to pick up when it meets every one before it, and the
sentence saying which one it failed is what `work/queue` reports:

1. **There is somewhere for it to go, and that place is neither terminal
   nor protected — unless this is the close hop or the approve hop, each
   of which is a decision a person already made.** For most columns that
   is the column to their right: the end of the board is not a transition,
   the step into a terminal column is the operator's, and the step into a
   protected one is an owner's or approver's — *only the operator decides
   that something shipped, and only an owner or approver decides what else
   may pass*. The close hop crosses the terminal fold anyway, and the
   approve hop the protected one, because a pre-approval already given is
   that decision, made in advance (`Dispatch.cs:1249`, `:1252`) — see [the
   five paths past the terminal guard](#the-five-paths-past-the-terminal-guard).
   The review column is dispatched to itself instead of either (see the
   twenty-first condition), and never into the column after it.
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
5. **It is not paused, on its own row, on an ancestor's, or by its project.**
   The nearest level other than normal, found the same walk the board and
   `hatch show` read — a
   person set it aside, and nothing picks it up until they set it back, named
   with the ancestor when the level is inherited. A paused *project* folds
   every row in it, an emergency issue's included, with *project HA is paused -
   a person set it aside, and nothing in it picks up until they set it back*,
   kind `person`; it is asked after the merge column's judgement, the claim
   and the stall, so a merge-column row keeps the judge's sentence. A fact about the issue like
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
   this at all, and neither is a hop — see the twenty-third condition.
8. **It is not [low](#expedite) level on an account whose session window is
   behind pace**, asked only of an unattended pass, reading the same account
   the seventh condition reads. The account's session window alone is projected
   to reset with its own pace matched or beaten — no reserve held back, so
   every reading that clears the seventh condition clears this one too; where it
   does not, the issue is folded with the sentence naming the window and the
   numbers, and where no reading exists for the account at all, or the key
   belongs to nobody, with that sentence instead. `work/{key}` is not asked
   this at all, and neither is a hop — see the twenty-third condition.
9. **Nobody's name is on it.** An issue [assigned](#assignee) to a person is
   somebody's to do, and an unattended pass leaves it alone. An issue assigned
   to an API key, or to nobody, is picked up exactly as it always was. A
   `?mine=true` pass inverts this condition rather than skipping it: it takes
   only a ticket assigned to the caller's own person or key, and folds every
   other one — see [one more, on `next` alone](#one-more-on-next-alone).
10. **It holds no unanswered question that holds the move.** It is waiting on a
   person, and another agent sent at it would ask the same thing again or guess
   at the answer. A question's level decides which moves it holds — see
   [the hop](#the-hop) — and `merge` holds none.
11. **The project's primary [repository](#repository) matches a remote the caller
   declared** — or the caller declared nothing at all, which this condition
   does not fold on, exactly as an issue page or an older CLI does not. A
   caller declares with `?remote=` (repeatable), `?standing=` and `?clones=`;
   the first two are checked here, on every move a session is spawned for (not
   a hop, unless it lands in the column where the code gets written), and the
   dependency below is checked on the move into that column alone. See [the dispatcher](#the-dispatcher) for the exact sentence.
12. **The union of the project's, its ancestors' and its own tags, folded in
    with the column being left's own, requires no runner tag the caller's
    runner lacks.** A keyed caller that sends no `?tag=` has advertised its
    key's config tags, or none where the key has no config, so it fails a
    union that requires any; a caller with no key and neither `?tag=` nor
    `?dangerous=` is the board's own scan and folds nothing here. A runner
    advertising a superset of the union qualifies. Checked on every move a
    runner is handed, in every column
    and on every hop, the move into the implementation column included —
    never a review row the verdict below already folds. Kind `other`, and it
    waits on nobody. See
    [the dispatcher](#the-dispatcher) for the exact sentence, named with the
    column when that column's own tags are part of why it folded.
13. **It is not flagged dangerous, unless the caller's runner takes dangerous
    work.** A caller that sends `?dangerous=false` refuses it: an issue whose
    own flag or nearest flagged ancestor's is set is folded on every move a
    session is spawned for that runs its code — out of the implementation
    column, and on the review self-move while it is conflict or build work.
    Never a hop, and never the move into the implementation column. A caller
    that sends nothing, or `true`, folds nothing. Asked after the runner tag
    above and before the model item below. Kind `other`, and it waits on
    nobody. See [the dispatcher](#the-dispatcher) for the exact sentence.
14. **It is not on a model the caller's runner cannot spawn.** The model the
    move would run on — the issue's own override, else the matched playbook's,
    else the review row's on the review self-move — is folded when it is an
    alias the caller's `?model=` list lacks (a non-empty list only), or a
    pinned id whose family is not the caller's `?agent=`. Checked on every move
    a session is spawned for, in every column. Never a hop, which spawns
    nothing, and never an idle review self-move the verdict below already
    folds. A caller that sends neither param folds nothing. Asked after the
    dangerous condition and before the toolchain item below. Kind `other`, and it
    waits on nobody. See [the dispatcher](#the-dispatcher) for the exact
    sentences.
15. **It is not missing a development dependency its project's repositories
    declare.** The issue's project binds one or more [repositories](#repository),
    and the union of their parsed `.hatch/dependencies.json` entries names a
    dependency the caller's `?has=` does not satisfy at a version
    (`DependencyVersion.Satisfies`) treats as a minimum - the caller's version
    must be at least it - or any bound repository's file is `invalid`.
    Checked on the same moves as the dangerous condition above — out of the
    implementation column, and on the review self-move while it is
    conflict or build work. Never a hop, and never the move into the
    implementation column. A caller that sends no `?has=` folds nothing.
    `?asRunner=` and the let-go peer check read the row's `Has` instead, and a
    row that never reported it folds nothing - a session carries no `Has` of
    its own, so a key with a beat reads that beat's, and a session-only key
    reads none at all. Asked after the dangerous
    condition and the model one, and before the dependency-edge item below. Kind
    `other`, and it waits on nobody. See [the dispatcher](#the-dispatcher) for
    the exact sentences.
16. **Nothing it depends on is unfinished** — and only when the move is into the
   column where the code gets written. Everything left of that still moves; an
   edge is satisfied only once the issue it names is in a terminal column. See
   [Dependency](#dependency).
17. **None of its children are still open**, when the move is out of the column
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
18. **An ambition's pass has room**, asked only of an ambition's own pass
    (implementation aimed at itself). Fewer of its epics are open — not
    terminal, not deferred — than its own limit allows (one, where unset);
    where room is open but a pass already ran since nothing reopened it, the
    fold instead names how long ago and that the next one runs when an epic
    under it closes or a person changes the ambition's limit, column or
    capabilities. See [the dispatcher](#the-dispatcher) for the two
    sentences. Never asked of anything but a pass — the epic fold (17)
    speaks for every other parent.
19. **The [WIP section](#wip) has room for it**, when the move is into it: the
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
20. **An epic above it has room too**, when the move is into the WIP section
    and this issue's parent is an epic: the load under that epic, not
    counting this issue, is below its own `WipLimit` (an epic with no `WipLimit` has no limit, and this holds trivially) —
    see [An epic's own limit](#an-epics-own-limit). Said right after the
    section-wide check above, for the same reason — it too needs other work
    to leave before anything more of this epic is pulled in, and it is a fact
    about the board rather than the loop's policy, so `work/{key}` is refused
    by it too. Not asked for a task, for an epic moving itself, or for an
    issue whose parent is not an epic.
21. **In review, its branch conflicts with the trunk or its build failed —
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
22. **A playbook covers that transition for that type — or it does not need
    one.** Without one there is nothing to say to the session — and a column no
    playbook leads out of is exactly [how a column becomes the
    operator's](#status), which is why the absence is a fold rather than an
    error. **This is also where the issue's type is decided**, and the only
    place: a type an unattended run does not pick up is a type no row names for
    that move, said in the words that name the fix.
23. **Unless it does not need a session at all.** An issue that is
    [express](#pre-approval) and stands in a column marked
    [`ExpressSkips`](#status) is [a hop](#the-hop): the twenty-second condition's
    absence is answered not by a playbook but by the pass carrying the issue on
    itself, with `POST /api/work/{key}/hop`. Every condition above this
    one still has to hold — a hop is not an escape from a live claim, a pause,
    a ready date, an assignee, a question, a repository, a dependency, an open
    child, a full section or an epic at its own limit, a required runner tag
    — the twelfth — only from needing a
    playbook, from [economy's or low's own gate](#expedite) — the seventh and
    eighth conditions — from a flagged issue, a model the runner cannot
    spawn, or a development dependency the runner lacks — the thirteenth, the
    fourteenth and the fifteenth, none of which a hop meets — or from the park
    below — the twenty-fifth. A clear hop
    is still carried across either way, parked or not; being carried across is
    not an escape from being the first clear row the walk saw, so a clear hop
    in a higher tier still parks the tiers below it.

    An epic standing in a column short of the implementation column, carried
    on each move right until it reaches that column, is a second kind of hop:
    something is filed under it, of any type, and, for a sub-epic, a parent
    epic at or past the implementation column; a top-level epic is carried
    only by the move that lands it there (HA-1069). Where nothing is filed,
    the refinement is its own fold rather than a
    fall-through to the twenty-second condition: "nothing is filed under it — an
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
    implementation column (see [the hop](#the-hop)), and no sibling holds the
    pull: only a sibling on a shared story's branch does, at or past the
    implementation column, not terminal and not waiting on the story's pull
    request. Where the column is so marked but such a sibling is already in
    flight, the refinement is its own fold rather than a fall-through to the
    twenty-second condition's "no playbook covers this": "{sibling}, sharing its
    parent's branch, is already in flight - one sibling at a time", kind
    `family` — the only one of `FamilyGate.Pulls`'s two conditions this fold can
    still be naming, since the hold above has already spoken for a parent
    that has not started.

    A childless issue whose brief is longer than its matched playbook's own
    `BriefLimit` is a fifth kind of hop, where `Columns.SendBack` — the
    column whose own `Target` is the implementation column — stands
    strictly left, in board order, of the column this issue stands in: a hop
    back to it needs no playbook and spawns no session, for the same reason
    the first four need none. Where no such column exists, or the issue
    already stands in it or left of it, this is not a hop, and condition
    twenty-four, just below, folds the move instead.

    A [pre-approved](#pre-approval) issue standing in the review column, with a
    pull request recorded, whose branch `MergeWork.Judge` says would merge
    cleanly or needs only bringing current, is a sixth kind of hop — carried
    into the column ticked `MergesPullRequest` with nobody reading the diff
    first, since a pre-approval already is that reading. Where the branch
    instead conflicts or its build has failed, this is not that hop; the
    twenty-first condition's own fold governs it exactly as it would an issue
    with no pre-approval at all.

    A pre-approved parent, or a parent in a project whose *A passing check
    approves the merge* setting is on, standing in the review column with no
    pull request recorded, and no child left open by its own type's rule — an
    epic's own fold ignores a deferred child, where anything else counts one
    open child as enough to hold it — is a seventh kind of hop, carried past the terminal
    fold the first condition would otherwise raise, straight into the first
    terminal column strictly right of review. A childless pre-approved issue
    is never carried this way: nothing is filed under it for every child to be
    done of, so it is not this kind of hop at all.
24. **A childless issue's brief is no longer than its matched playbook's own
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
25. **Nothing in a strictly higher [priority](#expedite) tier is clear.** The
    tiers are walked top down, once each, across every row the walk
    reaches: emergency in an unpaused project; then an expedited project's
    expedited, normal, low and economy rows; then every other unpaused
    project's, in the same four; last what is paused. The first *clear* row the
    walk sees is remembered, and every row
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

    Where the row doing the parking sits in a project of a higher level than
    the row it parks, and is not an emergency, the sentence names the project
    instead:

    ```
    HA-216 is in expedited project HA and is clear - nothing in a project at normal is picked up while an expedited project has work available
    ```

    A caller that cannot work the expedited project is not parked: every row in
    it is folded, and a folded row parks nothing.

    **A folded row parks nothing, whatever folds it** — a live claim, a
    running or unread build, an unmet dependency, open children that are the
    work, an epic's open children, a full WIP section, a ready date in the
    future, a sibling in flight, an unanswered question, a person's name on
    the ticket, anything at all. Most of these stand for days, not minutes,
    and a tier parked behind one would be a tier that never runs for as long
    as it stands — so a fold is never live for this rule's purposes, and the
    only thing that parks a lower tier is a row with no fold at all.
26. **It is not human work, on its own row or on an ancestor's.** [Human
    work](#human-work) is read the same shape `EffectiveLocalModel` is:
    the nearest ancestor — itself first — whose own value is not `inherit`,
    `agent` with nothing set anywhere. Folded with *human work - a person
    does this one, and no runner picks it up*, named with the ancestor when
    the value is inherited, kind `person`; asked beside the paused fold,
    ahead of every loop-only one, and not conditional on a hop — a hop is a
    move with no session, and a human issue gets none. Unlike the paused
    fold, an explicit `agent` on a nearer row stops the walk the same as an
    explicit `human` does, so a descendant may opt back out from under a
    human ancestor and dispatch honours it. A fact about the issue like the
    claim and the pause before it, so `work/{key}` asks it too.

Twenty of them — 1, 2, 3, 4, 5, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20,
21, 22, 24 and 26 — are facts about the issue, and `work/{key}` asks them too. The
twenty-third is as well, and `work/{key}` answers it the same way `work/queue`
does: `WorkDto.Hop`. The other four — 6, 7, 8 and 9 — are the loop's policy and
are asked only when the pass is asking;
see [one more, on `next` alone](#one-more-on-next-alone). The twenty-fifth is
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
says so (`this run will not clone`), drops the workspace's clones from what it
serves, serves its own checkouts, and is not refused; a workspace somebody
named is refused, naming the pid and the checkout and `hatch config --here
--workspace <dir>`. `--workspace off`, `HATCH_WORKSPACE=off` or `hatch config
--workspace off` turns it off outright for a runner that must never clone.

What says "one loop" is a directory under `TMPDIR`, holding the pid of the run
that took it: a `LoopLock` per served checkout, taken in order at startup,
plus, where a workspace is set, a `WorkspaceLock` (`hatch-workspace-<fingerprint>`,
the pid and the standing checkout inside). `go-to-work` takes the workspace
lock first and keeps it for the night, and `work` takes it for the increment —
first because giving way prunes clones from the served list before the
per-checkout locks are taken. A clone that appears at three in the morning is
already under a lock, and a second loop naming the same workspace is refused,
by name, before it can clone into anything the first is using. A refusal on any one releases every lock already
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
cleared by the next loop in that checkout — which also ends the session it left
running and releases the claim, see *A loop that died* below — so a `kill -9`
needs nobody to tidy up after it.

Running two loops on one machine, each in its own checkout, is walked
through in [Several runners on one machine](../src/Hatch.Web/apps/hatch/docs/the-runner.md#several-runners-on-one-machine).

### What a runner does, in order

Before every pass, and after the heartbeat, the loop **polls the branches in
review** — see [checking the branches in
review](#checking-the-branches-in-review). It is not a step of a pass and holds
no claim; it is what keeps the board's verdicts on those branches current, so
that the queue below can tell a conflict from a branch that merges cleanly. A
runner that is paused polls nothing.

**A loop that died.** A loop that dies mid-increment — a crash, a closed
window — leaves two things behind: the agent it spawned, still running in the
checkout, and a claim that lapses on its own a lease later. While a session
runs, the increment keeps a small record of it beside the loop's lock, under
the system's temporary directory and named for the checkout, never in the tree
or the night state: the agent's pid and when that process started, the ticket
key and claim token, and the Hatch base the claim was taken against, with the
session's id added once the stream has said it. The release deletes it on every
outcome, and so does the loop's way out. The next `go-to-work` in that checkout
reads it straight after taking the checkout's lock — so no live loop holds it —
and before its own first heartbeat and claim. A pid that is still running **and**
whose start time matches the record's is the session the dead loop left: its
whole process tree is killed and waited for. A pid that is gone, or whose start
time differs (the number was reused), is left alone. Then the loop releases the
claim with `outcome=abandoned`. A `204` means the claim was still that token's
and is now released, and gets one comment saying a loop died, the session was
ended and the claim released, with the agent's resume command where the record
had a session id; a `409` or `404` means the claim had already lapsed, been
taken or been cleared, and gets no comment. The record is deleted either way,
and one terminal line says what was done. A record whose Hatch base is not this
loop's belongs to a checkout pointed at another board: it is reported and left.
Only the loop cleans up: `hatch work` writes and clears its own record and never
reads another's.

**A session the board has paused** reads `paused` off its heartbeat for as long
as the pause is ahead of now, and so runs no pass: it keeps heartbeating, prints
*the board has this runner paused*, and takes nothing, the same wait a person's
pause gives. A build that never learned the board's own pause still waits, since
it already waits on `state: paused`; and if one asked anyway, the claim is
refused. A restart into the same checkout inherits the pause. See
[Runners on the board](#runners-on-the-board).

**A runner whose agent is not starting** does not reach the pass at all. After
two `unstarted` increments in a row it waits out a back-off of five, fifteen,
then sixty minutes, heartbeating and claiming nothing, and a cheap headless
session proves the agent starts before the next pass reads the queue — see
[what it stops for](#what-it-stops-for). It sits after the usage-limit wait, so
an account out of usage says so first.

**A runner whose agent is signed out** pauses the same way, but after the very
first increment that ends on one — an expired OAuth session, a rejected key, a
prompt to log in again — rather than waiting for a second. The ticket it held
goes back to the board untouched and with one comment, not a let-go and not a
stall: the agent needs a person to sign in again, and nothing about the
increment's own run counts against it. The back-off ladder, the cheap check
and the terminal's sentence are the same as the not-starting wait; a check
that starts and answers is read as a sign-in, whatever reason it had paused
for.

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

   Also between the reset and the spawn, **before the issue's branch is
   prepared**, the runner asks git whether it can sign a commit in the checkout
   the session will run in (`off`, `works` or `fails`: one `commit-tree -S`
   of a test object, ended at twenty seconds, touching no ref, index or tree;
   a `fails` is not asked again for thirty minutes). Where it fails and the
   dispatch's `AllowsUnsignedCommits` (true by default) is true, the increment's
   own git calls and its session run with `commit.gpgsign` and `tag.gpgsign`
   false through `GIT_CONFIG_COUNT` and its numbered pairs, after any the
   environment held - `-c` on the runner's own calls where git is older than
   2.31 - and the branch section of the prompt says to commit as usual and
   say "committed unsigned". Where the machine cannot sign (`off` or `fails`)
   and the project allows only signed commits, nothing is spawned: the ticket
   is let go with one comment, a second in a row is flagged stalled, and the
   increment is no run and no failure. See [signing
   commits](../src/Hatch.Web/apps/hatch/docs/the-runner.md#signing-commits).

   The session's environment is set first from this runner's own `HATCH_BASE`
   and `HATCH_KEY`, plus `PATH` — the pair is how the session spawned is the
   same runner as the loop that spawned it - see
   [How the session runs](../src/Hatch.Web/apps/hatch/docs/what-a-session-is-told.md#how-the-session-runs).

   Three things are done at the spawn: two so that a message to the agent is
   heard, and one limiting what it can reach for:
   - **The hooks, where the agent has one.** Claude takes a settings file: the
     runner writes it into a directory of its own under the system's temporary
     directory — outside every checkout, so `git status` shows only what the
     session changed — and hands it to the session with `--settings`. It
     declares a `PostToolUse` hook (every tool) and a `Stop` hook, each ten
     seconds long, each running `hatch inbox <key>`. The per-step one asks
     Hatch at most once every fifteen seconds, by the modification time of a
     stamp file beside the settings; the stop one always asks, and a message
     it finds keeps the session going until it has been read. What comes back
     is printed as the hook's JSON, and `inbox` marks it read. Any failure —
     Hatch down, slow past five seconds, a Hatch without the route — prints
     nothing and exits zero, and the message stays unread for a later step.
     The directory is deleted when the session ends. An attached (`-i`)
     session gets no hooks: somebody is at the keyboard. `codex` and `ollama`
     declare none: hooks do not fire under `codex exec` without a flag the
     adapter refuses to send, and `ollama` inherits that untouched. `grok` has
     none either, since its hooks load only from fixed, trust-gated paths. See
     [Agents](#agents).
   - **The marking.** The dispatch carries the messages nothing has read
     (`WorkDto.Messages`); the prompt prints them under *Said to you since the
     last session*, and the runner marks exactly those ids read immediately
     before it spawns. `work --dry-run` prints the heading and marks nothing.
   - **The denials.** The spawn carries the matched playbook's `DeniedTools`,
     one rule per entry — for every playbook-started session, headless,
     attached, conflict and build alike — and each adapter translates the
     rules into its own agent's spelling; `--disallowedTools <rule>` is
     Claude's. See [Agents](#agents) and [Playbook](#playbook) above.
   For a [check](#check) dispatch the runner, once the session has ended, looks
   at the tree in every checkout it entered and only then applies the verdict.
   For a conflict dispatch the runner then **checks the branch again**, before
   it enters it — see [a conflict increment](#a-conflict-increment-is-judged-by-the-branch-not-by-the-column).
   After the session ends, the runner reads the issue once more — see [when an
   increment does nothing](#when-an-increment-does-nothing) — to find a recusal.
   A session that ends before its first stream event — the agent's own `init`,
   `thread.started` or first JSON line — has nothing in the stream to say why,
   so the runner keeps what the CLI itself wrote: the last 40 lines of its
   standard error and of its standard output that were not stream events,
   scrubbed of `hatch_ak_…` keys, other API keys and `Bearer` values, and
   capped at 200 characters a line. The usage-limit, model-refusal and
   sign-in recognisers read those lines whatever the exit code, and the
   let-go and stall comments carry them in a code block, as does the closing
   tally line for a stall (its last line). A session that streamed keeps
   nothing extra.
6. **Heartbeat**, carrying the last line the runner printed — and only when it
   has changed, so the time a card draws is when the line was printed rather than
   when a heartbeat happened to fire. A `--quiet` increment renders nothing and
   so carries nothing, which is what `--quiet` is.
7. **Release**, however the increment ended, `recused` included.

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

The same interrupt can kill `git` mid-write and leave an `index.lock` behind it,
which refuses `git merge --abort` on its own. The loop clears a lock it can tell
predates itself — one written before this process started, which can only be a
dead process's — and retries the abort, unasked. A lock written since is
somebody else's git, still running, and is left alone. A forced clear past
that, through `git reset --merge` and then `git reset --hard`, exists on the
workspace too — `--abort-merge` is how a runner nobody watches is told the
answer in advance, rather than ending the night on a merge it cannot know is
safe to clear.

#### The issue's branch

A ticket that comes back for more work has a branch, and usually a pull request
on it. A session told to cut a fresh branch from the trunk abandons both — and
which branch is a ticket's was until now only prose in three places. So the
runner does it, after the reset and after the [self-change check](#where-the-loop-lives),
and before the spawn:

- **The branch of record is the pull request's head branch, once the issue has
  one, and until then the one branch on `origin` named for the key**:
  `<key-lowercased>`, or that and a hyphen and anything after it, compared
  case-insensitively. `ha-3-` is not `ha-31-…`. The pull request is the only
  pointer to an issue's change, so once the board holds its head branch
  (`PullRequestHeadBranch`, or a contribution's recorded one) it is the only
  branch looked at, whatever it is named, and no branch named for the key is
  consulted or counted. The name is the rule only for an issue with no pull
  request yet, or one the runner has not read: the first read of an open pull
  request puts its head branch on the board, so that window is a tick or two.
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
- **The same paragraph can ask the session to bring `.hatch/README.md` current.**
  `Workspace` decides before the prompt is composed: a trunk-landing entry's
  `AsksReadme` (`Branches.cs`) is set only when `ReadBlob` reads the trunk's own
  copy of the file and `ReadmeFile.Judge` (`ReadmeFile.cs`; `hatch readme --file`
  applies the same rule to a working tree) calls it absent or older than the
  version this build ships - never on a copy that is current, newer, or one
  Hatch did not write, and never off the trunk.
  When it is set, `Prompt.Landing` adds a paragraph asking the session to run
  `hatch readme --file` before its last commit and to commit the file, if it
  prints `written`, in that same commit - never on its own. The runner never
  pushes the trunk itself (see [Leaving the tree](#leaving-the-tree)), so this
  is asked of the session instead, and a session that skips it is asked again
  the next time.
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
- **Commits on the issue's branch that `origin` does not have are pushed** to
  `origin` under the same name, no force, as a backstop for a session that held
  its push (`PushUnpublished`, `Workspace.Leave.cs`). The ticket names the
  branch, how many commits, the tip, and that the runner pushed them because the
  session did not. A branch `origin` already has is pushed; one it lacks is
  created only when it is the one branch with unpublished work and `origin` has
  no open branch for the issue — with several, or one already there, the ticket
  says which was not guessed and nothing is pushed. A push that is refused
  (somebody pushed in between, or the forge says no) is a line on the ticket and
  the terminal, nothing is forced, and the increment does not fail. A lost lease
  pushes nothing: `Return` runs, not `Leave`. A branch the runner pushes is a
  branch on `origin`, so a ticket with no pull request recorded is let go
  `dropped`, as for any pushed branch.
- **Commits left on the local trunk go to `<key-lowercased>-rescued-<short sha>`**
  and the ticket names the branch, rather than leaving them to the reflog. That
  branch is not pushed: a second unmerged branch naming the issue on `origin`
  would make which one is the issue's ambiguous.
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
- **The repository's own `.hatch/README.md` is kept current on the issue's
  branch**, when the increment moved it: the branch is new on `origin` or its
  tip there has moved since the runner entered it — a session that only read
  adds nothing, and neither does a checkout with no branch, or more than one,
  on `origin`. Where it runs, the step reads the file at the branch's tip and
  at the trunk's tip through git, and writes this build's text only when
  **this build's version is newer than both** — the one rule in `ReadmeFile`.
  The trunk already holding the current text and the branch lacking it is the
  trunk's own merge forward above to settle, not a second write here to
  conflict with it; a copy neither Hatch wrote is left alone, said once per
  run. The commit is made the same way as the merge forward — no worktree, a
  temporary index, a push that is a fast-forward or nothing — and signed the
  way every other commit this increment makes is: unsigned only where the
  project allows it, never a silent fallback when it does not. A refused push
  is a line on the ticket, same as any other. One build per repository per
  version of the text, plus whatever is open at the time.
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
branch left **unmerged on origin** (`left.BranchOnOrigin && !left.HasPullRequest &&
!left.HasWaitingPullRequest`, where `BranchOnOrigin` is the merge check's verdict being anything but `none`, so
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

An issue with a [waiting pull request](#a-pull-request-waiting-to-be-opened)
is not refused: `LeaveOutcome.HasWaitingPullRequest` is read from the same
issue read as `HasPullRequest` (`Lifecycle.PullRequestStateAsync`), and counts
as published for this guard alone. It is never fed to `Workspace.Leave`'s
`syncPullRequest`, so the trunk is merged forward into a branch only for a real
pull request. A read that fails answers neither.

**The runner never pushes to the trunk**, in any project. Landing is the
session's own `git push`; `hatch landed` records it, the poll confirms it
(`Poll.ConfirmLandingsAsync`, `Poll.cs:290`), and the
[merge train](#the-merge-train) carries it. A session whose push was refused
leaves its commits on the local trunk, which the `-rescued-` bullet above names
(`Workspace.Leave.cs`).

**A session that ran out of Claude usage is pushed for earlier than the rest**
(below), where the issue's-branch push above waits for `Leave`: before
any of the above runs, its tree is committed — untracked files included — onto
the issue's branch (cut first if the tree was still on the trunk) and pushed to
`origin`, no force - unchanged in a trunk-landing project (`PushForLimit`,
`Workspace.Limit.cs`): it commits onto the issue's own branch, cutting it first
(`CutName`) when the tree is on the trunk, and never pushes the trunk. It is
the one push that commits the session's uncommitted work first, because a session
that cannot say whether it is finished never gets to say whether it is fit to
publish either. `Leave` then runs exactly as above and
finds a clean tree, and the ticket gets the usage-limit comment described below
instead of the ordinary tidy-up note.

#### Checking the branches in review

Whether a pull request still merges with the trunk is decided by **git, in the
runner, and never by a prompt** — the [dispatcher](#the-issue-in-review-whose-branch-conflicts-or-whose-build-failed-is-dispatched-to-review)
only reads what the runner reports. Once per interval, between the heartbeat and
the pass, on an idle board and a busy one alike, the loop:

1. asks `GET /api/work/review` for every issue in the review column that
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
whatever it did, the poll reads the build on the tip of the one branch origin
has for each issue it just looked at — or, where origin has several, the one
the stored `clean` or `conflicted` merge check names (that check fetched, and
knows which is open; `ls-remote` does not). Where the merge check names none of
them the poll says so in one line naming the branches and reads no build. It
reads the tip at the sha `ls-remote` already printed, so it needs no fetch of
its own, and puts a [build verdict](#build-check) to the board. It runs `gh` in the checkout, as
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

**The train reads review builds too, during a session.** The poll runs between
sessions on the loop's one thread, so a runner holding a project's only
checkout in a long session would leave every ticket in review waiting for the
session to end before its build counted, and the longer it waited the likelier
the trunk moved under it. So the [merge train's clock](#the-merge-train) also
reads the build on each review-column row's pull request head — the head sha
the pull request half keeps current on the board every tick, not the clean
merge check's sha, which only the poll writes. It needs no git, so it never
overlaps a session. Only the project's elected driver reads, and a row with no
head sha, branch or pull request on the board yet is skipped until the pull
request half supplies them (a push is read one `--merge-interval` late). The
rules for asking again are the poll's: a concluded verdict is read once per
sha, and a board that already vouches for the sha is not asked. A row whose
verdict is `pending` or `none` is asked again no more often than `--interval`,
not every `--merge-interval`, so a column of pending builds does not spend the
`gh` budget every few seconds; a new sha is asked at once. Poll and train read
the same sha by the same rule, so they never write different verdicts for it.

The terminal hears about a build only when it changes — `hatch: HA-12 build on
1a2b3c4 failed (api, CI)`.

**The trunk's own build, whether or not anything is in review.** Both halves
above run only for a checkout that holds an issue in review, because their
targets come from `/api/work/review` — a repository nobody happens to be
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

**The dependency file, read the same way.** For the same reason and on the same
schedule — once per checkout that has an origin, whether or not anything of
its is in review, and whether or not any project reads the file — the
poll reads [`.hatch/dependencies.json`](#dependency-file) off the trunk, reusing
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
`GET /api/trunk-builds` answers every stored trunk verdict once a poll,
and the runner matches its own checkout against it by the remote it spells
until the board's canonical form for that pair is known. The terminal hears
about a change the same way — `hatch: main build on 1a2b3c4 failed (api, CI)`,
or with `- re-running` appended where this interval took one — naming the
trunk rather than an issue.

**The landing half, asked of git and keyed on the checkout.** A trunk-landing
issue in review has no branch to merge-check and no pull request to ask `gh`
about; what it has is a [landing](#landing), the sha a session recorded and the
trunk it was landing on. Its targets come from the same
`/api/work/review` answer, and for each checkout that holds one the poll
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
half iterates the `/api/work/review` answer itself rather than the
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
`PUT /api/issues/{key}/pull-request-state` wherever any of the three
differs from what `/work/review` carried for that row, or the board holds no
head branch for it (`Branch` null): the put carries the pull request's head
branch, read with it, so a quiet pull request costs no call after the first; it does this for a review-column row and a
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
(`MergeWork` not null) is the one exception. A `merged` read there advances
the issue on every runner that holds the checkout, with no driver and whether
or not the branch is still on origin - the same `/work/{key}/merged` call and
the same terminal line as a review row, and a second runner's `409` is walked
past silently. `closed` and `open` there stay the driver's: only where this
runner is the one this project's merge train is elected to - see the note after
the two halves below - `closed` calls `/work/{key}/closed` once, which records
`PullRequestClosedAt` the first time and moves nothing, and comments once -
"Its pull request was closed without merging." - deduplicated by issue key, so
a quiet interval after says nothing again. A pull request state of `unknown`
(not the merge state of that name, below) does nothing either way.
**There is no stored verdict and no window logic like `NoneWindow`**: an open
pull request is asked about every interval, by design, since noticing the
merge promptly is the point, and a merged one advances the issue out of review
- so `/api/work/review` stops returning it and nothing asks again. The
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
`/work/{key}/merged`, the same route [the five paths past the terminal
guard](#the-five-paths-past-the-terminal-guard) already documents an operator's
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

The merge-column exception just above (its `closed` record and `open` put;
its `merged` read is every runner's), and every half after it, run only
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
the transition it was trying to make — with the agent's resume command, since
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
ran under hit its usage limit is read first from the session's own stream -
the `rate_limit_event` window matching the one the sentence named, already in
the runner's own zone with nothing to parse - and only when that is missing,
already passed, or silent on that one window does it fall back to what it
said on its way out - the
read now covers every spelling a limit comes back under (session, weekly, Opus,
Sonnet and Fable), as well as the pay-as-you-go phrasing that never says "limit"
at all (`You're out of usage credits · resets ...`); it handles a dated reset
given either with "at" or with a bare comma, an hour with no minutes, an unknown
zone (falls back to the runner's own zone rather than giving up), and a reset naming no zone at
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
that there was nothing to push), and the agent's resume command. The night's
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
touches nothing, leaving the sentence standing. The probe's own instant
carries no zone of its own, so the correction converts it into the runner's
own local clock as it replaces the carried one - the same "out of Claude
usage until ..." line the sentence itself prints is shown in the runner's own
clock either way. A correction says itself once, and not again merely because
a later probe lands on the same corrected reading. On a CLI already known to
cost a turn - this run, or remembered from an earlier one - no probe runs at
all while that holds, so this correction does not either; the backstop below
is what answers instead.

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

A session reset further away than its own window is the same guess gone the
other way. A session never resets more than five hours out, so a sentence with
no date whose next occurrence is more than the window plus an hour away - a
`resets 7:40pm` read at 7:41pm, the account's own reset trailing the minute it
names - is read as *just passed*, not as tomorrow. The wait then ends five
minutes after the session ended, in the zone the sentence named, and the
console line, the outcome and the ticket's comment all say that the named reset
had just passed and when the runner tries again. A dated sentence is taken as
given, so a weekly limit is unchanged; an undated per-model weekly sentence
lands here too, which costs one refused spawn every five minutes.

**Neither is a preemption.** The board can order the runner holding the
lowest-priority ticket to stand down for an emergency one — see
[what a runner does, in order](#what-a-runner-does-in-order), where the `200`
that says so arrives on the heartbeat. No stall comment is written and no
question is opened, the same as a usage limit and for the same reason: nothing
here is wrong with the ticket, and the ticket is left exactly where the
session left it, free for the next pass. What *is* written is the one comment
that section already describes: that this runner was preempted, which
emergency issue took it, the branch and
sha that were pushed (or why not, per checkout), and the agent's resume
command — so this is not a case where nothing happens, only a case where
nothing is asked of a person. The night's tally counts it as **preempted**, a
list of its own beside *moved*, *stalled* and *interrupted* — not a failure,
and not one of the three in a row that end a night, the same treatment a lost
lease and a usage limit get and for the same reason: this is the loop working
correctly on a busy board, not a broken increment. The very next pass finds
the emergency issue at the top of the walk and picks it straight up.

**Neither is a recusal.** A session that ran `hatch recuse` on its ticket - it
found the work is one this runner cannot do - ends the increment as a
recusal: no stall comment, no mark, no question. The loop reads the issue
again after the session, the same fresh read [the claim](#claim) describes,
and finds this runner newly among the ticket's own omitted runners; it prints
one line - `hatch: {key} - {runner} recused itself ({reason}), going on to
the next` - and releases the claim `recused`, which stops the `letGo` count
rather than raising it: the omission, not the let-go fold, is what keeps the
ticket off this runner. The night's tally lists it with the let-go ones, its
own line reading `recused` rather than `let go`, and it is not one of the
three failures that end a night - the same treatment a usage limit and a
preemption get, and for the same reason: a runner refusing work it cannot do
is the loop working correctly, not a broken increment.

**Neither is a model the runner cannot spawn.** A session the account refused
to spawn a model on - recognised from what it said on its way out, the same
way a usage limit is - ends the increment unservable: no stall comment, no
mark, no question, the same treatment a usage limit and a preemption get, and
for the same reason - the session did not get to say whether what it left is
fit to publish, so the runner does not either. The one comment names the
model, says until when this runner no longer offers it - or that it is off
for the rest of this run, where the account gave no reset - and carries the
branch and sha that were pushed (or why not, per checkout) and the agent's
resume command. The terminal says the same thing once - `hatch: {model} is
off this runner's list until {clock}` (or `for the rest of this run`) - never
again for the same model until the narrowing itself changes, and from that
moment the model is off the list the next beat sends and the next pass's
picker offers: a row pinned to it is folded into the skip list rather than
claimed. A restart carries this, the same way it carries a usage limit's
wait; a fresh start forgets it. The night's tally counts it as
**interrupted**, not one of the three failures that end a night, for the same
reason a usage limit and a preemption are not either.

**Neither is a session that never started.** An agent that ends before its
first stream event - a CLI that refused to start and said why on stderr -
never touched the ticket, and the runner says so rather than
calling it a stall: the claim is released `unstarted`, which `letGo` steps over,
so it neither counts toward the ticket's let-goes nor resets them. The ticket
gets one comment - *An unattended increment could not start its agent on this
runner and left HA-nnn untouched* - carrying what the CLI wrote (scrubbed, and
cut to its last lines) in a fenced block, and *It exited N.*; no resume line,
since there is no session, no mark, and no question. A usage limit or a model
refusal read from the same text wins, as it does for `unservable`. A `--quiet`
run is never read this way, since quiet mode prints no `init` and so cannot
tell a run that never started from one that crashed after real work; and a
stop for quiet or run time, a lost lease, a put-down ticket and a skip keep
their own verdicts. The night's tally lists it under **interrupted** as
`unstarted`, and it neither resets nor advances the three failures that end a
night.

**A fault of the runner's own machine is named on the release.** When an
increment ends on a signed-out agent, an agent that never started, a ticket let
go for want of a signer, or a pull request it left written and waiting, the loop
sends the matching failure kind and what the tool said with the release (see
*Claim*), and the board writes it on the `claim_released` event and links the
runner's open failure row to the ticket. A waiting pull request is named only
where this runner wrote it since the session began, so an old one is never named
twice. The session is told the same thing in advance: a tool or a sign-in this
runner lacks is not a decision to ask about.

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
`runner-credential` in `Program.Internal`, a command no session is ever told
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

A downloaded `hatch` has no `hatch.sh` beside it, so it supervises itself: a
build published with `-p:HatchLauncher=true` (`make publish-hatch`, the image's
four `cli-*` stages, CI's `hatch-cli` job) and started as `go-to-work` or
`do-my-work` with nothing above it becomes a **launcher**, in
[`src/Hatch.Cli/Launcher.cs`](../src/Hatch.Cli/Launcher.cs). It runs its own
file again as the child, with `HATCH_NIGHT_STATE` pointing into a directory of
its own, and starts it again when it exits 75. It rebuilds nothing, since a
download has no source beside it. A build nobody stamped, a loop that already has
a `HATCH_NIGHT_STATE`, and the container image's build never become one.

The launcher's version rides the child's beat as `launcherVersion`, and the board
compares it with the launcher version of its own build
([`LauncherVersion.Current`](../src/Hatch.Contracts/LauncherVersion.cs), the one
number the launcher's own constant is set from) and answers `launcherBehind`.
The page only reads that answer, as a **Launcher behind** badge beside **Behind**
and **Rebuild**. The version rises only when the launcher learns something, so a
deploy alone never lights it; a child that finds its launcher older than its own
build says so once on its first start of the night.

A launcher also replaces its own file, so the `hatch` on a `PATH` is downloaded
once ([`LauncherSelf.cs`](../src/Hatch.Cli/LauncherSelf.cs)). At every start,
before the state directory is made, it sweeps what an earlier start left aside and
looks at the build every `boards/` record names as current: the one with the
highest sequence, read by running its `version`, replaces the file at the launcher's
own path when that sequence is higher than the launcher's. Newer means a higher
sequence, so a board that rolls back moves the child and leaves the launcher. The
copy is written beside the old file, run from there and asked for its revision,
then swapped in. A file that is a link is followed and the file it points at
replaced. On Linux and macOS the swap is a rename and the process then calls
`execve` on its own path with the same arguments and
`HATCH_LAUNCHER_HANDOFF=1`; a change of build in the middle of a night first writes
`resume` and the state to put back into the night's directory and passes it
as `HATCH_LAUNCHER_RESUME`, so the next child starts on the same trial, holds and
fallback. On Windows the running file is renamed aside as `<file>.old-<pid>-<id>`
and the new one put at the path; between children the launcher carries on as it
was, and at a start it starts the new file once with the handoff variable and
waits for it, so there is never a chain. Either variable is read once and removed.
Nothing here ends a night: a failure is one line, once per start. A build nobody
stamped, a development build, `scripts/hatch.sh`, `scripts/hatch.ps1`,
`HATCH_RUNNER_BIN` (all set `HATCH_NIGHT_STATE`) and the container image's build
never do it. The launcher version is 3 for it.

A launcher also changes build. When the child hears from its board that the
board is on another build, it writes `build.json` beside the night's state (the
board's origin, the revision, where to download it and its sha256) and exits 75;
the launcher fetches the build, checks it, keeps it under
`versions/<sha>/` and starts the next child from it, and records it as the
board's build once that child's first beat is answered. The child decides at the
top of a pass, after the stop conditions and before the order and paused
branches, so it holds no claim; `behind` is followed at once and any other word
after ten unbroken minutes. See [Restarts](#what-it-stops-for) below.

The launcher also removes the builds it can no longer need. After a change of
build and at each start it sweeps `versions/`, keeping the current and previous
build of each board a loop ran against in the last thirty days and any build in
use: a launcher says which builds it runs with a locked `<sha>.in-use-<guid>`
file, macOS and Linux are scanned for processes running from `versions/`, and
Windows refuses to open a running file for writing. A build is looked at
again for a lease or a record just before it is removed, since a change of
build can land while the sweep runs. A build is renamed to
`.old-<guid>` and then deleted, a delete that fails is left for the next sweep,
and an unfinished `.tmp-*` download goes at a day old. The macOS scan has never
been run by hand.

A launcher also hands on a plain command. Any command but `go-to-work`,
`do-my-work`, `version` and `inbox`, on a stamped build with no
`HATCH_LAUNCHER_VERSION` above it, works out its board the way it always has,
reads that board's record under `boards/`, and runs the same arguments through
the build recorded as current, or through the newest kept build (the binary
written last) where no build is recorded or the call names no board. The child
is told it has a launcher with `HATCH_LAUNCHER_VERSION` and
`HATCH_LAUNCHER_PATH` and no `HATCH_NIGHT_STATE`, so it never hands on again and
nothing takes it for a night. Nothing is redirected: the exit code, the streams
and the terminal are the child's, and the launcher passes the first signal on
and waits. A child a signal ended on Linux or macOS is answered as 128 plus the
signal. The decision only reads: no network, no download, no `Record`. Nothing
kept, a chosen build that is the launcher's own, a recorded build that is not
kept, or one that will not start, all leave the launcher to answer the command
itself, with one line on standard error for the last two. `hatch work` is such a
command, so its increment and its session's `PATH` and hooks name that build.
`hatch standby install` writes the launcher's path, never one under
`versions/`. The cost is one more process start.

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
the origin alone and leaves the key and the agent paths exactly as they were, so
it is safe to paste on a machine that is already configured. The page fills in
this Hatch's own origin, because the address bar is the one thing about an
install nobody can get wrong.

`config --key <key>` is its mirror for the credential: it writes the key alone,
needs an origin already set, and refuses an empty one. With the wall up the Runner
page shows it beside the origin command, and the Runners page is where the key
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
- `--max-tokens N` — `500k`, `2M` or `1.5M`, or a plain whole number; measured
  from what the increments reported, not estimated. An increment that reports
  no token count is not in that sum, so it cannot bind one, and the closing
  tally says how many reported none. A usage-limited increment's tokens do
  count, since they were spent.
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
- **A machine that cannot sign** — exit `78`, the third one nobody asks for.
  Where git cannot sign a commit and no project the runner serves allows an
  unsigned one, every ticket it could claim would be let go again. So it says
  so and exits `78` at the start, before a heartbeat or a claim, or at the first
  ticket it has to let go for that reason, holding no claim and printing the
  tally. It fails open against a board that does not answer or is too old to
  list the setting. A supervisor does not run it again on `78`: the launcher and
  both scripts pass it through, and a runner set's host exits `78` only when
  every member did.
- **A workspace that cannot be made current** — the other one nobody asks for,
  and the only condition that ends a night without an increment having failed. A
  tree that will not reset is a tree every ticket would be built wrong on, and
  the loop has no way to make it right. A fetch that merely did not answer is
  not this: that is a wait, and the next pass tries again.

What it **waits** for is not a stop, and not one of the three failures.
`--max-tokens-per-hour N` takes the same counts as `--max-tokens`; while the
tokens reported by increments that ended in the last sixty minutes are at or
over it, the loop takes no ticket, spawns nothing, keeps its heartbeat and
spends any order sent to it, then resumes by itself as the oldest entries age
out. The loop does not end, so there is no closing tally for it, and the wait
records no increment, no failure and no token. One increment bigger than the
count puts the loop over on its own until that increment ages out; the tally's
`peak hour` line is the number to size it from.

**A runner whose agent does not start** waits too. Two runs in a row that
ended `unstarted` — the agent printed nothing before its process ended — pause
the runner: it takes no ticket, keeps its heartbeat, spends any order sent to
it, and says on the terminal that the agent is not starting and when it will
check again. The first wait is five minutes. When it ends, one headless session
on the adapter's cheapest model, asked for a single word, runs in a temporary
directory of its own before anything is claimed: it claims nothing and touches
no ticket, and its cost joins the night's spend and is said on the terminal. If
it starts and answers, the runner says *the agent starts again - taking
tickets* and the next pass claims; if not, the wait becomes fifteen minutes,
then sixty, and stays at sixty. A run that started, whatever its exit code,
ends the streak, and a failed one still counts toward the three failures. A
restart mid-pause comes back paused to the same instant on the same rung, and
the closing tally says how many runs never started and how long the runner was
paused for it. Like the usage wait it records no increment and no failure, and
the merge train keeps ticking through it.

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
`--max-runs`, `--max-spend`, `--max-tokens`, `--max-tokens-per-hour`, `--until`,
`--under`, `--interval`, `--quiet` and `--stop-file` are all still in argv,
which the supervisor re-runs verbatim; the increments, the spend, the tokens
(and how many increments reported none), the last hour's token entries, the
failure streak, the night's start and the two lists the tally prints travel in
a state file the supervisor names once per night. So a restart cannot outspend
`--max-spend`, re-open `--max-tokens`, re-open a `--max-tokens-per-hour` wait or
outrun `--max-runs` by starting over,
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
`--no-restart`, or when started by hand rather than through `hatch.sh`,
`hatch.ps1` or a download's launcher — the state path is what tells the runner
somebody is standing over it, and a runner with nobody to run it again is the
loop it started as.

Under a launcher the loop restarts for `hatch use`, for its set and for a change
of build, and for nothing that needs source: a change in a checkout it serves is
not read, and `--restart-after` does nothing. A change of build needs a launcher
at version 2 and a loop that restarts at all, so it never happens under
`--no-restart`, `--once`, `hatch.sh`, `hatch.ps1`, an unstamped build or the
image; a launcher at version 1 gets one line and the loop carries on. It tells the loop who it is through
`HATCH_LAUNCHER_VERSION`, a whole number that starts at 1, and `HATCH_LAUNCHER_PATH`;
a child reads the version before relying on anything newer than 1, and where the
launcher is too old it carries on without that thing and says so in one line. No
pairing of launcher and child makes either one exit.

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
somebody else take. That window is the claim's own, and follows the playbook
the dispatch matched: a playbook's longer `quietLimitSeconds` also stretches how
long the CLI waits out an unreachable server before assuming it lost the lease,
once the playbook is attached (a limit of zero or none leaves what the take
said), so one number answers "how long may this session be out of touch". And **a busy board** is a wait, not an ending: every
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
top of the next pass, *after* whatever increment is in flight has finished. A
runner can just as well be left alive and paused from login onward - [Starting
it at
login](../src/Hatch.Web/apps/hatch/docs/the-runner.md#starting-it-at-login)
registers a platform login item that starts the loop paused at every sign-in -
and the board still starts nothing: it only sets that paused row running at its
next heartbeat.

**A runner's name is its key's** — assigned once, at mint, and never chosen
or recorded by the runner itself. `POST /api/runners`
(`RunnersController.CreateRunner`, `RequireRole(User)` and an owner rule, no
`AcceptKey`) is the only way one is minted, from the Runners page's Create runner dialog. The cast list in
`src/Hatch.Contracts/runner-names.txt` (main and recurring characters from
three TV shows, plus the colorful one-offs) only suggests a name there —
`GET /api/runners/suggest-name` (`RunnerCast.Suggest`, `RequireRole(User)`) answers one no key or heartbeat already holds —
and an admin is free to type anything else instead. There is no
`hatch config --runner`, or any other way, for a checkout to pick or override
the name it runs under; a runner has one identity, its key's, for good.

Every heartbeat also carries `where` — the machine and checkout the process is
actually running from — drawn on the Runners page under the name. A heartbeat
whose name is already the *live* runner at a different `where` is refused with
`409`: two live runners never share one row, and a gone row can still be taken
over, which is how a checkout that moved keeps its name.
`Agent` works the same way - the id of the agent that spawned the process
(`claude`, `codex`, `grok` or `ollama` today, per `Settings.KnownAgentIds`), absent from a runner that has
not reported one - but carries no uniqueness check, since two runners sharing
an agent id is not a conflict.

**Who sees which runners.** `GET /api/runners` filters its rows by the
caller. A person at a browser, and every caller on a Hatch with its wall off,
read every runner; a key whose owner is an admin does too; a key whose owner
is anyone else reads only that owner's runners; and a key with no owner reads
only the runners that belong to nobody. With the wall on, no key exists until
an admin has minted one, so "the wall is on" stands for "an admin has claimed
this Hatch". `Dispatch`, `DriversAsync`, `HeldAsync` and the attention read are
unscoped on purpose: they read the `LegacyRunners` table — kept only for a
genuinely keyless heartbeat, wall off and no key at all — not the route, and
the filter is on the route only.

**The runner is a key; the heartbeat row is kept beside it.** `GET /api/runners`
joins three things: the key (`EfApiKey`), its config (the `RunnerConfigs` row,
by the key's id) and the legacy heartbeat row (the `LegacyRunners` table, by
name — the one a genuinely keyless beat writes). A key
that has never run is listed, and is not live. A heartbeat no key claims is
listed too, with its key fields null. `?live=true` keeps only the entries whose
heartbeat is not past `Runners.DropBefore`. HA-1937 adds the sessions below
beside this join, not instead of it (`Runner.IsLive`) — a key with no sessions
is judged exactly as it always was, and a key with some is judged by the beat
and its sessions together.

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

**Tags are the board's alone.** No heartbeat ever seeds them onto a row — old
route or session route alike — only a `PATCH` on the row, from the Runners
page or directly, sets them. The answer carries the row's tags back, and the
loop adopts them on its next beat — saying so once, `hatch: the board set
--tag ...`, or `--tag off` for none — so the very next dispatch read carries
the board's list rather than the one the process started with. An older
Hatch answers with none, which the loop reads as "leave my own alone". The
Runners page draws them beside the repositories line, drawing nothing for a
runner that has none. `hatch work`'s own one-off heartbeat carries none of
this, and an older CLI never sends it at all, so either leaves a
previously-reported row's tags in place rather than blanking them.

**The reply names the board's build.** A runner on a different build than
its board is told so on the beat itself: the reply carries a `boardBuild`
part - the board's revision and sequence, `compare`, `download` and `sha256`.
The part is there exactly when the beat sent a stamped revision and sequence,
the board is stamped, and the two revisions differ. `compare` is `behind`,
`ahead` or `diverged` - two different commits at the same count, which no
ordering resolves - and the board decides it with the same rule the **Behind**
badge reads; the runner compares nothing. A matching revision, an unstamped
side (`dev`, or no count), a beat with no revision, and an older board all
leave no part, which a runner reads as nothing to do. The beat says its
platform in `rid` - `win-x64`, `osx-arm64`, `osx-x64` or `linux-x64`, null
for any other machine - because `os` is a shortened description and cannot
name a build; the board keeps nothing of it. `download` and `sha256` are what
the Runner page lists for that platform, read from the same code, and both
are null where the platform is unknown, unsent, or has nothing published -
the part is still sent. Every beat is answered this way: the named route and
the session route, `loop` and `once`. The **Behind** badge and `boardDrift` read
the stored row as they always did. A downloaded loop reads the part: `behind`
sends it to its launcher for that build at the top of the next pass, and any
other word does so only when every beat for ten minutes has named the same
revision (`BoardBuildWatch`). A loop with no launcher, or one run with
`--no-restart` or `--once`, never acts on it.

**Refused models are a key's alone, and a key may write its own.** `PUT
/api/runners/{name}/refused-models` holds the list of models this
runner's key will not be told to run — replaced whole, never a delta, and
normalised against whatever agent the row's beat or a live session last
reported (a pinned id of a different agent is refused; the check is skipped
where none is known). A person who can control the row may write it, the same
as every other bound — but so may the key itself, the one write on this
controller a key may take on its own name: a refusal only narrows what a
runner will be told to run, never raises a bound the way every other field
here does, so a key holding its own list is not the hole that gate exists to
close. A key naming another runner is refused. The list rides every
heartbeat back as `refusedModels`, on both heartbeat routes, so a loop
restarting reads the board's current list rather than the one a process
started with; an older Hatch answers with none. The dispatcher reads the
column too: a move whose model the key refuses is folded for it, from every
door — see item 12a of [the dispatcher](#the-dispatcher) — and across the
fleet, [a move no live runner can take](#a-move-no-live-runner-can-take)
counts a live runner that refuses the model as not offering it.

**It can also carry a column rule.** `statusMode` and `statusIds` on `PATCH
/api/runners/{name}` say which columns this key's runner works - `all` (the
whole board, the rule a runner with none carries today), `except` (every
column but the ones named) or `only` (just the ones named). The two fields
are taken together, never one without the other, and `all` keeps the ids
`[]` regardless of what was sent. The rule lives on the key's own config row,
the same as tags and refused models, so a name with no key is refused it
outright - there is nowhere for it to live. A column deleted after the rule
was written is dropped from `statusIds` on the next read rather than carried
as a ghost id. Nothing reads this rule for dispatch yet. The Runners page sets
it under **What it may work** and shows it to a reader as a `Columns:` line.

**It can also be marked preferred.** `preferred` on `PATCH /api/runners/{name}`
says this runner takes work before unmarked runners; absent leaves it as it
was, and `true` or `false` sets it. The flag lives on the key's config row,
like the column rule, so a name with no key is refused it outright. It is
only a person's to write - an admin, or the person who owns the runner - and
a key is refused with `403`, even on its own row, because the flag widens the
runner's share rather than narrowing it, as a refused model does. It reads
`false` for a key that has never run and for a heartbeat row with no key, and
a peer's declaration carries it. Dispatch reads it one way: a runner that is not
marked is told a clear row is [left for a preferred runner](#the-dispatcher)
while a marked one is live, idle and able to take it. The Runners page sets it
with a **Preferred** checkbox under **What it may work** (a reader sees `Preferred:
yes` or `no`), draws a `preferred` badge on a marked runner in its list, and the
work queue's runner list draws the same badge beside the name.

**It also says whether it refuses dangerous work, and which projects it is
bound to, on every beat.** `refusesDangerous` and `projects` ride every
heartbeat beside `remotes` and `agent`, the same way `models` and `has` do
below: absent leaves the row alone, and a sent value — `true`/`false` for the
one, `[]` or a list of keys for the other — overwrites it. Neither is a
`PATCH` field, and neither is answered back. A dispatch read declares them on
the wire as `?dangerous=false` and a repeatable `?project=` would, and `GET
/work/queue?asRunner=` still reads both off the row rather than off the query
string, so a runner whose row says `refusesDangerous: true` has a flagged
issue folded whatever `?dangerous=` said, and one bound to some projects has
every ticket outside them folded the same way.

**It also says what it can spawn and what it has, on every beat.** `models`
and `has` ride every heartbeat beside `remotes` and `agent` — see [the
runner](../src/Hatch.Web/apps/hatch/docs/the-runner.md#what-a-runner-advertises)
for what each names. Both are tri-state: absent leaves the row alone, and an
empty list clears it. Unlike tags, neither is owned by the board — there is no
page or `PATCH` that edits them, and the row is overwritten with whatever the
latest beat sent, the same way the repositories line is. On a dispatch read
each rides the query string as `model=`, `agent=` and `has=`, and is judged by
`Dispatch.ModelFold` and `Dispatch.DependencyFold`; `?asRunner=` and the let-go
peer check read the row through `RunnerDeclaration` instead, so the two ways
of asking agree. A repository names what it needs at
[what a repository needs](../src/Hatch.Web/apps/hatch/docs/the-runner.md#what-a-repository-needs).

**What it is working is not stored.** A row's ticket and the line beside it are
read off whichever issue carries that runner's live claim at the moment of the
request, the way an open question is computed rather than kept. A second copy
would drift the moment an operator cleared a claim out from under the runner
holding it. The row's own line is therefore only ever what a runner said
*between* tickets — "nothing on the board is an agent's to move", "AER-12 moved"
— because an increment's chatter already rides its lease.

**A claim's heartbeat counts as the runner being heard from, too.** A loop
beats its own row only between tickets, but a session well into a long
increment beats its *claim* the whole time — see [Claim](#claim). The beat is
sent every `min(lease / 5, gone limit / 3)`: 30 seconds by default, so one
missed beat leaves the row inside the 90-second limit. The take answers the
gone limit as `RunnerGoneAfterSeconds` so the loop never has to know the
setting; an older server answers `0` and the loop beats every 30 seconds.
`RunnerDto.LastSeenAt` is the later of the two, so a runner
reads *Working AER-12* for as long as either is fresh, and does not read
*Gone* out from under a session that is still there and still renewing its
lease, only quiet on its own row.

**A claim's heartbeat also revives a session the sweep ended.** A read of the
runner list stamps a session ended once it is past the limit
(`RunnerSessions.MarkAllGoneAsync`). If the claim is still being renewed, the
next claim heartbeat puts that session back (`RunnerSessions.ReviveAsync`):
`EndedAt` is cleared and `LastSeenAt` is set to now. It revives only the key's
newest ended session at that checkout, only when the key has no live session
there, and only when the sweep ended it — a session ended more than the gone
limit after its last sighting. A session the runner ended itself
(`POST /api/runners/sessions/{id}/end`) is written within a beat of its last
sighting and is never revived.

**Two horizons, and only one of them is a setting.** A row is *idle* while it is
being heard from, *gone* once its last heartbeat is older than
`Hatch:RunnerGoneAfterSeconds` (90 by default), and dropped from the read
entirely two hours after its last heartbeat (or at the gone horizon, if that is
longer), which is not itself configurable. The queue view and the Runners page
draw the same read. The two are one judgement seen twice, "not answering" and
"not coming back", and an installation able to set them apart could set the
second shorter than the first and have rows vanish before they were ever drawn
as gone.
Nothing sweeps: all three are arithmetic against the last heartbeat at the
moment somebody asks, the same lazy expiry the claim uses, so there is no
timeout to tune and no background job to notice has stopped. The row itself is
never deleted — a checkout that runs again next week is the same row, with
whatever bounds are on it.

**Retired is a third state, and a fact rather than a clock.** A row that has
been superseded is stamped `RetiredAt` (now) and `RetiredBy` (the `Name` of the
row that superseded it), and two things stamp it, both in
`RunnersController` and both on a beat:

- **A row beat** (`RetireSuperseded`). Every *other* row with the same `Where`,
  `Kind` and owner (`ForPersonId`), an earlier `FirstSeenAt`, and a `LastSeenAt`
  older than the gone horizon (`Runners.Cutoff`, `Hatch:RunnerGoneAfterSeconds`)
  is retired. A row that is still being heard from is never retired by this
  path, so two live runners in one checkout are left alone.
- **A session beat** (`RetireRenamedAsync`, from `POST
  /api/runners/sessions`, `loop` kind only). Every other `loop` row of the
  same `Where` and owner is retired, with no quiet test: renaming a checkout's
  runner leaves the old row behind, and the new name's first beat is the
  evidence it is gone. A `once` beat never retires anything.

In both, a row with no `Where` retires nothing, and an owner must match: with
the wall up a null owner retires nothing, with the wall off null matches null.
An already-retired row is not stamped again.

A retired row keeps every fact it had and is never deleted, but it is not live
(`Runner.IsLive` is false) and never elected driver (see
*The driver, and how the heartbeat elects one*, above). `?live=true` leaves
it out, and the Runners page and `hatch runners` both read `?live=true`, so it
leaves both at once, not two hours later; the page treats it as not
controllable. Only the row's own beat clears it — `Touch`, or
`RetireRenamedAsync`'s own-row branch, sets `RetiredAt` and `RetiredBy` back to
null — so nothing acting from outside can resurrect a row, and a runner started
again under the old name reappears by itself. Retirement is written by a beat,
not by a sweep: the "nothing sweeps" above stays true. `RunnerDto.RetiredAt`
and `RunnerDto.SupersededBy` carry it, both null for a row that is not retired;
a read without `live` still returns the retired row with both set.

**One key can hold several sessions, one per checkout.** Beside the beat row
above, `POST /api/runners/sessions` answers through `RunnerSessions`
instead — rows grouped by `(ApiKeyId, Where)`, so a key running `go-to-work`
in two checkouts at once holds two live sessions, each with its own line, its
own claim and its own bounds. `pause-all` and `stop-all` are the one write
that reaches every live `loop` session under a key at once; a `once` session,
an already-ended one, and one past its own gone horizon are left alone by
both. Each session judges its own liveness and its own gone horizon by the
identical `Runners.Cutoff`/`DropBefore` arithmetic a beat uses, against that
session's own `LastSeenAt` — one quiet checkout under a key never drags a live
one down, and `Runner.IsLive` reads true while any one session does, even
while the others, or the beat itself, have gone quiet. An ended session is
kept in history, newest twenty per key (`RunnerSessions.HistoryDepth`); a
twenty-first ended session deletes the oldest
(`RunnerSessions.TrimHistoryAsync`). A session's own bounds narrow against the
key's `RunnerConfig` as a ceiling, never past it — the smaller of the two
wins, and a tie reads as the session's own, not the ceiling's
(`RunnerSessions.Effective`/`Narrow`) — the same rule a beat already follows
against the site default above.

**A session's heartbeat carries six machine fields, plus `ip`.**
`RunnerSessions.Project` is the one projection that fills
`RunnerSessionDto` — `os`, `arch`, `agentVersion`, `container`, `username`,
`processStartedAt`, `ip` — for every reader of a session: the heartbeat's
own answer, `GET /runners`, and `hatch runners --json`. The six named first
come off the heartbeat's body (`RunnerHeartbeatRequest`) and are missing for
a session run by a CLI old enough to predate them. `ip` is not one of the
six and has no body field at all —
`RunnersController.SessionHeartbeat` reads it off
`HttpContext.Connection.RemoteIpAddress`, which `UseForwardedHeaders` has
already rewritten from the proxy's forwarded header before the controller
runs, so it is the server's own reading of the connection, never the CLI's
say. That reading is taken whenever the connection has one, regardless of
CLI version — a session from a CLI old enough to send none of the six still
carries `ip`. Nothing else is collected: no environment variable, no
process id, no MAC address.

**A loop also says whether it signs its commits.** Unlike the six machine
fields above, the answer can change during a night, so it is not read once: the
loop asks its signing cache on every beat, and a loop beat carries `signing`
(`off`, `works` or `fails`) and, beside `fails`, `signingWhy`. The answer is the
worst over the checkouts the loop serves — `fails`, then `off`, then `works`,
written once as `RunnerSigning.Worst` in `Hatch.Contracts` and called by the
loop and the board alike — taken from the latest check of each, so a checkout
not yet asked says nothing. `signingWhy` is the first line of what git said,
masked of keys by the loop and cut to 200 characters; the board cuts it to 200
again and keeps it only beside `fails`. It rides the *session* row
(`EfHatchRunnerSession.Signing`, `SigningWhy`): `RunnersController.SeedSession`
writes it, and `TouchSession` rewrites both when a beat sends a `signing`, so a
later check moves the answer and a `works` drops an old reason; a beat that
sends none — a `once` beat, an older loop — leaves them alone. A word the board
does not know is stored as no answer and the beat is still accepted. A runner
reads as the worst of its live loop sessions (`RunnersController.ProjectAsync`,
as `Models`, `Has` and `Pulling` are), so a keyed runner and its `RunnerDto`
carry it with no legacy row; the legacy `POST /runners/{name}` route ignores
both fields. The Runners page and `hatch runners` word it as *Signs its
commits*, *Does not sign its commits* and *Signing fails*, and the list marks
only a runner whose signing fails. The board refuses work on what a
runner said: a project that allows only signed commits is folded for a runner
that has not said `works`, see [the dispatcher](#the-dispatcher), item 12c.

**The board also keeps whether a loop can sign in to its forge.** A loop beat may carry `forge`
(`works` or `fails`) and, beside `fails`, `forgeWhy`, and the board stores both
on the session row (`EfHatchRunnerSession.Forge`, `ForgeWhy`) exactly as it
stores signing: `SeedSession` writes them, and `TouchSession` rewrites both when
a beat sends a `forge`, so a later check moves the answer and a `works` drops an
old reason. A beat that sends none leaves them alone, and a word the board does
not know is stored as no answer with the beat still accepted. `forgeWhy` is the
first line of what the forge said, cut to 200 characters and kept only beside
`fails`. A runner reads as the worst of its live loop sessions, `fails` then
`works`, written once as `RunnerForge.Worst` in `Hatch.Contracts` for the loop
and the board alike (`RunnerDeclaration.ForgeOf`, read in
`RunnersController.ProjectAsync`); a session carries its own on
`RunnerSessionDto`. A runner that never said reads null, and the legacy
`POST /runners/{name}` route ignores both fields. The board refuses work on it:
a runner that said `fails` is folded off a step that ends in a pull request, and
off build work in review, see [the dispatcher](#the-dispatcher), item 12d. A
runner that said nothing is not judged. The `/work` reads accept `?forge=` and
`?forgeWhy=` from the loop, and judge them the way they judge `?signing=`; the
board stores nothing from a read.

#### Failures

The board keeps a row for each thing a runner could not do
(`EfHatchRunnerFailure`, the `RunnerFailures` table), so a person sees what is
failing now, since when, and what failed earlier. A row holds the key
(`ApiKeyId`, bare, as on a session), the checkout (`Where`), the kind, what the
tool said (`Why`, at most 200 characters), `BeganAt`, `LastReportedAt` and
`EndedAt`, null while it stands. Rows hang off the key and the checkout, not
the session row, so a restarted loop continues the open row it finds. A
checkout nobody named is stored as `""`, never null: Postgres treats nulls as
distinct, and a null would let the index below admit two open rows. The
database holds the one rule that matters: a unique index on key, checkout and
kind, filtered to `EndedAt IS NULL`, so at most one row is open for each.

| Kind | Open while the beat says |
| --- | --- |
| `forge` | `forge` is `fails`; any other sent word ends it |
| `signing` | `signing` is `fails`; `off` is a choice, not a failure |
| `agent-signed-out` | `agentDownKind` is `signed-out` while `agentDown` is true |
| `agent-not-starting` | `agentDownKind` is `not-starting` while `agentDown` is true |
| `board-paused` | the board's own pause stands on the session |

Out of usage and the hourly token cap are not failures: they end by
themselves and keep their own wording.

Only a loop session's heartbeat and the board's own pause write these rows;
no route lets a key or a person open, edit or end one.
`RunnerFailures.ReconcileAsync` runs on every `loop` beat of
`POST /runners/sessions`, after the beat's own save, and a `once` beat touches
nothing. For each kind the beat either says it fails, says it does not, or
says nothing. Failing with no open row opens one; failing with an open row
refreshes `LastReportedAt` and `Why`; not failing with an open row ends it at
that beat; saying nothing leaves the row alone. The agent kinds are read from
what the beat said, never from the session, and are left alone while a board
pause stands, since the session ignores the runner's report then.
`board-paused` is read from the session on every loop beat, which covers a
pause that passed, one the lift route cleared, and a session seeded into a
standing pause. The board writes the pause in
`IssueClaimController.CountForPauseAsync`, which adds the row in the same save
(`RunnerFailures.OpenBoardPauseAsync`); nothing lifts it, and the session's
next beat ends it. Two beats racing to open the same row meet the index: the
loser reads again and settles against the winner's row. The legacy
`POST /runners/{name}` writes none of this.

Ended rows are kept, newest fifty per key (`EfHatchRunnerFailure.History`);
the beat that ends a fifty-first deletes the oldest. Nothing else sweeps. A row
is **current** when it is open and its runner is being heard from at that
checkout (`RunnerFailures.Current`: a live `loop` session at the same `Where`).
A runner that has gone quiet shows no current failure, as it shows no other
current fact, and its open rows are settled by its next beat; until then an
open row of a quiet runner is in neither list. `RunnerDto.failures` carries the
current rows, newest began first, and
`GET /api/runners/{name}/failures` answers `{ current, earlier }`, `earlier`
being the key's ended rows, newest ended first, under the rule
`GET /api/runners/{name}` reads by. The table belongs to no project, like the
other runner tables, and carries no query filter.

**Three things a person can say**, and the loop obeys each at its next
heartbeat:

- **`stopping`** — finish the increment in flight and exit without picking
  another, saying on the terminal that the board asked it to.
- **`paused`** — take no ticket, and *keep heartbeating*, so a loop deliberately
  doing nothing does not drift from idle to gone while it does it.
- **`running`** — carry on, which is what every row starts as.

**And one thing a person can order.** `work KEY` claims that ticket next, past
`--under` and `--mine` but not past the dispatcher's own folds — another
runner's claim, a person's assignee or a missing tag turns it away with the
fold's own sentence. `pass` is one ordinary pass: the queue read and its first
clear row taken, as it would have been. Either is read once and gone — the
beat that reads it clears it — and runs whether the loop is paused or running,
after which a paused loop reads paused again on its next beat. It counts as an
increment, toward `--max-runs`, `--max-spend` and the three-failures stop, and
is never run past a stop that has already been reached. A fold, or a loop out
of usage, spends the order with a sentence on the row (`AER-2 - <the fold>`,
`order spent - out of Claude usage until …`) and runs nothing. `--once` never
reads one.

**Only the row's owner or an admin can say any of the three.** `RunnerDto`
carries `For` — the owner's name, read off the key that heartbeats the
row — and `CanControl`, judged the same way the server's own PATCH gate
judges it, true for everybody while the wall is off. The Runners page draws
the three controls and the bounds form only where `CanControl` is true; a
row it cannot control draws a sentence naming `For` instead, except a `once`
row, which never drew controls at all and says so by kind already.

**And the six bounds** `go-to-work` takes on the command line — `--under`,
`--max-runs`, `--max-spend`, `--max-tokens`, `--max-tokens-per-hour` and
`--until` — are on the row too, folded into the loop before the stop conditions
are asked, so a cap lowered from a page stops that loop at its very next pass.
The loop folds both token bounds wholesale, and says so when the board changed
one: `hatch: the board set --max-tokens 2M`, or `--max-tokens off` when the
board cleared it. `until` on a row is always an instant, never a bare date:
"stop by the 12th" is a midnight in a timezone nobody named.

**`--max-tokens` and `--max-tokens-per-hour` are on the row and folded.** The
first stops the loop and the second waits and does not stop. Both are held,
read and set as a token count (`500k`, `2M`) like the other bounds; the server
also keeps what the runner reports beside them, `tokensUsed`, `tokensLastHour`
and `throttledUntil`. The page's Token budget column draws `tokensUsed` and
`tokensLastHour` against the two caps, and a row over its hourly cap reads
*Over 200k tokens/hour until 3:40am*.

**A cap comes from the row, else the site default, else nowhere.** The row
holds it when a fresh start gave the flag or a person's `PATCH` set it; failing
that, the site default on Settings (`defaultMaxTokens`,
`defaultMaxTokensPerHour`) stands in; failing that, there is no bound. The
heartbeat hands the *effective* cap down, so a runner with none of its own runs
under the default, and each row says which it is with a `maxTokensSource` of
`runner`, `default` or `none`. Clearing a runner's own cap in `PATCH` returns it
to the default rather than uncapping it; clearing the default writes no row and
takes the cap off every runner that was inheriting it, at that runner's next
beat (Settings is cached up to 30 seconds, so another replica can take that
long to notice). **Only an admin sets the default** — with the wall off, anyone
at the keyboard; never a key or a runner. The Runners page marks an inherited
cap `(default)` in both places it shows one, and Settings is where the two
defaults are set and cleared.

**A fresh start writes the row's bounds.** The first heartbeat of every *fresh*
loop start — one typed by hand, or the supervisor's first start, but never a
supervisor restart that carries the night's state and never a `once` beat —
writes the six bounds from what the process actually started with, including
their absence: a flag not given clears what the row held. That is what makes
them show on the page immediately, and what lets a person who types a new start
at a checkout overwrite the row. Every other heartbeat leaves them alone, and
that is what stops the loop's own half-hourly [restart](#what-it-stops-for) —
which sends the same argv again — from quietly undoing an edit somebody made at
midnight. The consequence is worth knowing: while a loop runs, **the row is
where its bounds live**, and only a new hand-typed start replaces them.

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

**`throttledUntil` is a fact a runner reports about its own `--max-tokens-per-hour`,
never a person's to set.** A loop whose last sixty minutes of reported tokens are
at or over that cap waits rather than stops, and every beat while it does carries
the instant it expects to be under the cap; a beat from a runner that is under
it, or has no cap, carries null. The runner reads it fresh at the top of every
pass, so the beat is never a pass behind. The server keeps it on the row, written
by every `loop` beat (so a null clears it) and left alone by a `once` beat; the
page draws it as the throttled status, ranked below *Gone* and *Out of usage*
and above *Paused*, in the viewer's own time zone like *Out of usage until*.

**`agentDownUntil`, `agentDownKind` and `agentDownLine` are a fact a runner
reports about its own agent, never a person's to set** — and the board's own
only in one case, below: a session whose releases spent no tokens three times
in a row is paused by the board with kind `board-paused` — the same tri-state
`exhaustedUntil` has, for *the agent will not start or is signed out* rather
than *the account is out of usage*. `agentDown: true` writes `agentDownUntil`
and normalises `agentDownKind` to one of `not-starting` or `signed-out`
(anything else sent is stored as `not-starting`), and `agentDown: false` clears
all three at once — the only way they clear other than expiring by arithmetic
at read time, the same lazy rule `exhaustedUntil` follows. A keyed runner with
no beat of its own composes the three off its live sessions the same way it
composes `exhaustedUntil`, except whole: the one live session whose own
`agentDownUntil` is latest and still ahead of now, with its kind and line
copied alongside it, never a kind from one session and a line from another.
The Runners page and `hatch runners` draw such a row *Agent not starting until
7:40pm* or *Agent needs signing in*, with what the agent said where it gave
one (a `board-paused` row reads *Paused by the board until 7:40pm*, with the
board's line after a dash, and `hatch runners` prints `paused by the board` and
`board said:`), ranked under *Gone* and over *Out of usage* — the agent itself is the
problem, which outranks the account having no usage left. Unlike exhaustion, a
runner down this way is left out of `Dispatch`'s own peer set
(`Dispatch.IsLiveBeat`/`IsLiveSession`) and out of the election
`Runners.LiveCandidatesAsync` runs for `DriversAsync` and
`ProjectDriversAsync`: a paused agent cannot answer a claim or a merge train's
own heartbeat, so it is never offered either. `go-to-work` writes the tri-state
on every beat except a `once` one, which sends none of it so a single
increment can never clear the loop's own pause out from under it.

The board writes the three itself on a session (`IssueClaimController.ReleaseClaim`)
that has dropped `Hatch:RunnerPauseAfter` (default 3, `0` is off) tickets in a row
that `SpentNothing` marked as having spent no tokens: `agentDownUntil` is
`Hatch:RunnerPauseMinutes` (default 60) ahead, `agentDownKind` is `board-paused`,
and the line names the count and the last ticket. The count is
`EfHatchRunnerSession.PauseStreak`, kept per session and not per ticket; a
`dropped` release whose rows spent tokens zeroes it, and a release with no row
since the claim or any other outcome leaves it. While the pause is ahead of now a
heartbeat cannot clear, shorten or reword it (`TouchSession`), and a session row
seeded at the same checkout inherits it. Nothing sweeps it; it ends when
`agentDownUntil` passes. It is out of the peer set and the election like any
other agent-down session. The Runners page and `hatch runners` word it as the
board's pause, on the session's line and the runner's row, in the viewer's zone
and ranked as any agent-down row; an older `hatch` prints *agent not starting*
for it. While it is ahead of now, `RunnerSessions.Instruct` answers the
session's heartbeat `paused` (a stored `stopping` stays `stopping`; `State`
itself is never written, so the page does not read it as a person's pause), and
`POST …/claim` from that key and checkout is refused — see [Claim](#claim).

A person can end a board pause before the hour is out:
`DELETE /api/runners/{name}/sessions/{id}/pause` (`RunnersController.DeleteRunnerSessionPause`)
clears the three fields and zeroes `PauseStreak` on that session and on every
other row at the same key and `Where` with a live `board-paused` pause —
ended rows included, since `TouchSession`'s own seed copies an unexpired
board pause from any row at that key and `Where`, not only a live one, and
clearing just the one row a person can see would let a restart bring the
pause straight back. It refuses a key or a keyless runner the same way
`IssueStallController.PutHold` does, `403` with its own sentence rather than
`GateAsync`'s — that one talks about spending, which reads wrong for a
pause — and refuses to touch a pause the runner set on itself
(`not-starting` or `signed-out`), `409` naming whose it is to clear. A
session with no live board pause is `204` and writes nothing. The next
heartbeat is then answered with the session's own stored state — nothing
about the heartbeat guard changes, since it only ever held while
`AgentDownKind` was still `board-paused` and ahead of now — and its next
claim is taken as normal; three more releases that spend nothing pause it
again. The Runners page draws **Lift pause** on a session with a live board
pause and nowhere else, beside the other session controls.

See [Runners out of Claude usage](#runners-out-of-claude-usage) for the read
that feeds the attention panel's dot, and
[when an increment does nothing](#when-an-increment-does-nothing) for what
sets this in the first place.

**A runner's states are kept, and a script can read them.** The board writes
a row each time a runner's state changes, and `GET /api/runners/{name}/states`
answers the stretches that overlap `?from=`/`?to=` (default: the last 24 hours,
no cap) as `[{ state, kind, from, to }]`, oldest first, not clipped to the
range and with a null `to` on the one still standing. Six states are kept:
`running`, `paused`, `stopping`, `exhausted`, `agent-down` (`kind` says why)
and `gone`. Working and idle are not kept, and a runner held back by its hourly
cap reads as `running`. The read writes nothing; what the clock ended since the
last row is added in memory. A script reaches it with `hatch api GET`; there
is no subcommand for it.

Running two loops on one machine, each in its own checkout, is walked
through in [Several runners on one machine](../src/Hatch.Web/apps/hatch/docs/the-runner.md#several-runners-on-one-machine).

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
a running increment and the idle loop between them both write to. Each redraw
reaches the terminal as one whole frame — the erase, the line being printed and
every footer row in a single write — and a clock tick that would draw the rows
already on screen writes nothing.

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

**The agent's standard error prints through the runner.** A headless session's
standard error, and the `/usage` probe's, is read by the runner beside the
standard output and printed above the readout like any other line, exactly as
the agent wrote it, so it can never land under the footer; `work -i` keeps the
terminal as the attached session's own stdio and is not redirected.

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
— an account this CLI cannot read, or one already known to cost a turn, this
process or a later one on the same CLI version — leaves the panel exactly as
it was. Only where neither
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

**`a` is live only while a reset is stopped on a merge in progress** — the
loop's own that would not abort, or somebody else's — and only then does the
legend read `a abort the merge`. A reset that stops there without
`--abort-merge` having already answered the question in advance prints a
banner naming the checkout, whose merge it is, git's own words where there
are any, and how many uncommitted files aborting it will discard; in colour
it is orange (256-colour index 208, since the readout's own palette has none),
and plain text under `NO_COLOR` or on a redirected output. `a` or `A` within
30 seconds clears it the same way `--abort-merge` would, says what it did and
discarded, and the pass carries on as if nothing had been in the way; nothing
in 30 seconds is "no action", and the run ends the same way a tree that
cannot be reset at all does. Where keys are off — a pipe, `--no-keys`,
`TERM=dumb`, or redirected output — the banner still prints and the run falls
through at once: the 30 seconds is for a person, and nobody can press a key
on a pipe.

## The level above the board

The board shows every card, which is the one thing it cannot do: say which of
the running projects is nearest the line. `GET /api/plan` and
`/api/plan/{key}` answer that, and the **Plan** page draws it.

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
epic would know to go back for. A `hold` or `build` question on a leaf that
has reached a **terminal** column does not: the ticket shipped with it open,
nobody is coming back to move it, and keeping it in `waiting` forever would be
the meter holding open a decision nobody is being asked to make. A `merge`
question does count there: it is owed a look back, so a closed leaf with one
still adds to the meter until it is answered.
`Questions.Waiting` is where that line is drawn, and it is the same line
the attention panel and the board's badge draw, so a subtree cannot read
"waiting" on one screen and quiet on another.

[`Rollup.cs`](../src/Hatch.Api/Modules/Hatch/Rollup.cs) loads the tracker once
and folds post-order — O(n) for the tree rather than a walk per node — because
the Plan page asks about every epic in the house at once. It is server-side for
the reason the rank and the dispatcher's pick are: a browser that re-derived a
total would disagree with the CLI the first time a column was added.

## The Dream Board

A half-thought has nowhere to go that is not an issue - no project, no type, no
column, no key, just the words (see [Dream](#dream)). The **Dream Board** is
where it goes instead, and the **weave press** (`POST /dreams/weave`) is how
one is pulled back out as an epic.

**A weave is an epic, run by the stock epic-breakdown row.** The press files a
`type: epic` issue in the from-column of the epic-breakdown row -
`Dispatch.MatchWeave` picks it: among the rows covering `epic` for the
project or the board, the leftmost from-column, ties broken the way
`Dispatch.MatchReflection` ranks (a project-specific row before a board-wide
one, then specificity, then id) - Breakdown, on a stock board. Its description
is a brief, built by `DreamWeave.Describe`, not the epic's own words; the
breakdown session then does what any breakdown does: reads the brief, gives the
epic a title of its own in place of `Weave: <seed>`, rewrites the description
to open with a summary and quote the dreams it drew on after it, files the
stories, moves the ticket on. See [`DreamWeave.cs`](../src/Hatch.Api/Modules/Hatch/DreamWeave.cs)
and [`DreamWeaveController.cs`](../src/Hatch.Api/Modules/Hatch/DreamWeaveController.cs).

**Why not a `weave` issue type with its own playbook row, as reflection has:**
a weave *is* an epic, and its product is an epic - a second row for the same
job is a second row to drift out of step with the first. The per-issue method
belongs in the description, which is what a description is for.

**Why not the server calling a model:** a credential on the server, a second
adapter seam beside the one the loop already has (see [Summary](#summary) and
[Agents](#agents)), and a result that sits outside the board's own record of
what ran and what it cost. Every LLM-shaped thing Hatch does is a session the
loop dispatches against a playbook row; the weave press is no exception.

**The press is a person's, not a key's.** Checked by `NotAPerson`, the same
guard `PostReflect` makes before a reflect-now press - weaving spends a
breakdown session at the largest model, which is a person's call to spend, not
an agent's to make for itself. A key is refused either way, wall on or off.

A few things the brief has to work around, since the epic it is filed for does
not exist yet when it is written: it cannot print the epic's own key, so it
says `<KEY>` and means the ticket's own, read back once filed; it tells the
session to ask `GET /api/dreams` again with no `projectId` as well, since
a dream leaning toward no project matches only that way; and nothing is parsed
back out of the description afterwards to find what it drew on - tracing is
still the link row the session writes itself, with
`PUT /api/issues/{key}/dreams`.

The brief also tells the session that a dream with a `remandedFrom` was once
planned work (see [a remand is a link row too](#dream)). It names the two
reads that bring the tree back, `GET /api/issues/<root>` and `GET
"/api/issues?ancestorKey=<root>"`, the root being the first key in
`remandedFrom`; asks the session to say in the epic's description which
remanded tree it drew on; and tells it to leave that tree in its deferred
column, the way the Dream Board itself is left as it is.

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

## The operator and agent contract

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

A key is optional for a read, never for a loop: `work`, `go-to-work` and
`do-my-work` refuse before they take a lock, a claim or make a request.
Against a Hatch started with its wall off ("Local mode") a keyless call reads
as the local person, never a program — there is no header or other way for it
to name itself instead. So a `401` is two different sentences, and says which
happened: a key that was sent and refused is a key to go and look at, and no
key at all is a Hatch with its wall on and nothing to look at yet.

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
with the agent's resume command beside it, and streams what the run is doing
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
  `[AER-12](<origin>/issues/AER-12)` and nothing else, then a blank
  line, then the summary. `<origin>` is the address Hatch was reached at
  (`HATCH_BASE`), or the install's public address where it sets one; `hatch work
  AER-12 --dry-run` prints the exact line. Never write a relative link.
- **Write the ticket the way a person would read it.** A title is a handful of
  words: "Tickets in a human voice", not "Weave: more human voice". A story
  opens with one sentence saying who gets what; an epic opens with a short
  summary. Sentences are short and name no coined things. A comment leads with
  the outcome, then the sha and branch. The rules are on the Issues docs page,
  under "How a ticket reads".
- **Plan on the ticket, not in a chat log.** A planning session `PATCH`es
  acceptance criteria into the description and `POST`s the stories or tasks the
  work breaks into. An epic takes stories; a story takes tasks.
- **File the wait, don't write it down.** Something that cannot start until a
  soak test finishes or a renewal window opens gets a `readyAt`, not a sentence
  in a description saying "not until March".
- **Recuse when the repository cannot be built here.** A session is never
  sent a development step on a runner that cannot build it — the board folds
  it by the model and toolchain items — so a session that finds it cannot
  build anyway, a tool missing that nothing declared, does not stall or
  guess: it runs `hatch recuse KEY "<what is missing>"`, which ends the
  increment and keeps that runner off the ticket (see
  [Omitted runners](#omitted-runners)), and says on the ticket that the
  repository should declare it in `.hatch/dependencies.json`.

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
- **Mint keys**, and everything else behind a plain `[RequireAdmin]`.
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
- **A separate kind of key for agents**, which would stop a key answering its own
  question. Worth a column when somebody wants it; see
  [the one edge](#the-one-edge-that-is-deliberately-cut).
- **Reports beyond the Timeline.** The [Timeline](#the-timeline) reads the
  `status_changed` events; what remains deferred is a report over the trail's
  other kinds, a menu under the Reports nav link (it waits for a second report
  to put in it), and a CLI subcommand for a report (`hatch api GET` reaches the
  Timeline already). Each is cheap to add later. The Plan view answers the
  question that was actually being asked.
- **GitHub integration, beyond what a runner's own `gh` reads and writes.** A
  branch and a PR are named in a comment by whoever made them. What is read
  from a forge is the build on an in-review branch's tip, with the pull
  request's merge state and review decision beside it, stored on the issue and
  drawn; and, for a project's elected driver, the open pull requests on a
  bound repository — their authors, the forge's word for how each author
  relates to the repository, their head branches and their bodies — read to
  file [a contribution](#contribution) for one nobody on the board knows. The
  two writes are the merge of a pull request the board has already gated
  behind a protected column, and the branch update that keeps it current with
  the trunk while it waits; a Hatch-made pull request's body also has its
  envelope written by `hatch pr`. All of it goes through the runner's own `gh`
  ([checking the branches in review](#checking-the-branches-in-review)); the
  server still asks nothing of a forge itself and holds no forge credential.
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

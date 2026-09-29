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

- **No status-transition rules.** Any column to any column; we trust ourselves.
  The one rule in the whole flow is a speed bump rather than a wall: a move
  into a full [WIP](#wip) section is refused, and a person may always step over
  it by saying *move anyway*. The second asymmetry is not a rule in the
  database either — it is that an agent is never dispatched *into* a terminal
  column (see [the dispatcher](#the-dispatcher)). The third thing that could be
  mistaken for one is [closing a subtree](#closing-a-subtree), and it is not a
  rule either: it is a question the browser asks a person after a move has
  already committed. Nothing is forbidden by it and nothing is required — the
  answer is the operator's, and "leave them" is one of the two. Nor is
  `fromStatusId` on a move: it is a precondition on what the caller last saw,
  not a rule about which columns may follow which.
- **No granular permissions.** Reaching Hatch at all means trusted to do
  everything in it. The one exception is the API key, whose scope is a
  statement about *which surface*, never about which verb.
- **No swimlanes or sprints.**
- **No GitHub integration beyond one read.** A commit sha in a comment is the
  link, and it is written by whoever did the work. What is read is the build on
  the tip of a branch in review, and it is read by the runner's own `gh`, never
  by the server — see [the build half of checking the branches in
  review](#checking-the-branches-in-review). What is still not read: whether a
  pull request is approved, whether it merges (that is git's answer), and
  anything at all from the server. Nothing in Hatch names a forge or inspects a
  host.
- **No websockets.** The board refetches on action and on focus, and every 30
  seconds while it is on screen, and at once when it returns to the screen. It
  was once refetched on action and focus alone, on the premise that whoever
  moves a card is looking at it; the loop moves cards all night, so the premise
  lapsed and the board polls.
- **Events are recorded, not rendered.** The Plan view answers "how far along
  is this" from current state; nothing draws the event log as a report.

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

### Project

`EfHatchProject` — `Key` (unique, `^[A-Z][A-Z0-9]{1,5}$`), `Name`,
`NextIssueNumber`, `CreatedAt`.

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
`IsWip`, `ExpressSkips`, `Color` (`#rrggbb`).

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
slice of the board the flag is measured against; no row means no limit. This
epic writes exactly one slice, `story,bug`.

`ExpressSkips` marks the columns an [express](#express) issue is carried past
with no session — see [the hop](#the-hop). It is **not** a "whose column is
this" flag: who works a column is still derived from the playbook matrix, for
the reason [above](#status) — this box only says which columns an express
issue is carried past, and says nothing about who works the columns on either
side of it. Set only by a person, through its own route
(`PUT /api/hatch/statuses/{id}/express-skips`), for the reason given under
["the one edge that is deliberately cut"](#the-one-edge-that-is-deliberately-cut):
it decides which gates the loop may pass unattended, and a key that could tick
it could carry its own ticket through the night with nobody reading it first.
Neither `POST /api/hatch/statuses` nor `PATCH /api/hatch/statuses/{id}` can set
it.

`Color` is a column rather than a palette keyed on the shipped names, because
the operator invents columns — a lookup by name would leave a new one grey
forever and lose a renamed one's colour. The ink written on a colour is computed
from its luminance (`lib/color.ts`), because CSS still cannot ask that question.

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
| Backlog | 30 | | | operator | Specified work, awaiting selection. |
| To Do | 40 | | | **agent** | Analyse it until implementing it is mechanical. |
| In Progress | 50 | | ✓ | **agent** | Write the code, get it green, push it, put it up for review. |
| In Review | 60 | | ✓ | operator; **agent** for a conflict or a failing build | Read the pull request, wait for green, merge. An agent steps in only when the branch has stopped merging with the trunk, or the build on its tip has failed, and fixes that on the branch — see [the review playbook](#playbooks). Conflicts come first. A build that is passing, still running or unread is left alone. |
| Done | 70 | ✓ | | operator | Terminal. |

A migration flags `In Progress` and `In Review` on a board that still has
columns of exactly those names, and writes no limit row — the WIP section is
marked, but nothing changes until the operator types a number on the Statuses
page. A renamed board seeds neither.

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
`DueAt`/`DueAtHasTime`, `PullRequestUrl`, `ModelOverride`, `EffortOverride`,
`AssigneePersonId`/`AssigneeApiKeyId`, `CreatedBy`, `CreatedAt`, `UpdatedAt`.

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
`claude-…` id for a model, the five efforts — validated by the playbook's own
rule and refused in the playbook's own sentence, so what an issue may be set to
and what a playbook may be set to cannot drift apart. Setting one is closed to
an API key; see [The one edge that is deliberately cut](#the-one-edge-that-is-deliberately-cut).

#### Expedite

**One flag meaning *this one first*.** `Expedited` is a boolean on the issue,
set by a person, and honoured by both halves of Hatch: the board floats the card
to the top of its column, and the dispatcher considers every expedited candidate
before anything else.

Two things it deliberately is not.

**It is a sort key, not a gate.** Every existing fold still applies. An open
question, an unmet dependency, a ready date in the future, a live claim, a
missing playbook, a person's name on the ticket and a terminal column fold an
expedited issue exactly as they fold any other, with exactly the same sentence.
Expedite changes the order candidates are *considered* in, and nothing else —
so an expedited issue that is blocked is still blocked, and the pass carries on
past it.

**It marks the issue it is set on, not the subtree under it.** Every type in a
walkable column is dispatchable — an epic in a breakdown column is broken down
by the loop the same as a story is implemented — so a flag on one issue means
something wherever it is set. "Point tonight at this epic" is already
`work --under`, and a second subtree mechanism beside `ancestorKey` would be two
answers to one question.

On the board it is `(StatusId, Expedited desc, Rank, Id)`, served that way
rather than sorted in the browser, so the board, the plan and the queue cannot
disagree about where a card sits — and a card dropped above an expedited one
comes to rest below it, because the float wins over the rank. In the dispatcher
it is [two walks of the columns](#the-dispatcher) rather than a sort of the
finished rows.

Setting it is closed to an API key; see [The one edge that is deliberately
cut](#the-one-edge-that-is-deliberately-cut). It follows that there is no
`hatch expedite` verb — the CLI authenticates with a key, so the terminal shows
the flag and sets it nowhere.

`CreatedBy` is a **name**, not a foreign key to `People`. The audit trail has to
read the same after a person row is deleted, and an API key's name goes in this
column beside a human one — neither of which a `People` FK from a module schema
could express.

#### Express

**One flag meaning *carry this past a column marked for it, with nobody reading
it first*.** `Express` is a boolean on the issue, set by a person, and read by
the dispatcher alone — see [the hop](#the-hop). Take an express issue standing
in a column marked [`ExpressSkips`](#status), with no unanswered question, and
the loop moves it one column right itself: no session, no model, nothing spent.

**It is a gate-passer, not a sort key — the opposite shape from
[expedite](#expedite).** Every fold an issue already meets still folds it: a
live claim, a ready date in the future, a person's name on the ticket, an
unanswered question, a missing repository or an unmet dependency all hold an
express issue exactly as they hold any other. Express answers one question
only — does this column still need a session — and changes nothing about
order, on the board or in `hatch queue`.

**It is taken from the parent at filing, and at no other time.** An issue
created under an express parent is born express, whoever files it — a person
or a key — and however: the New issue dialog, the child composer, or the API
directly. Its `created` event names the parent it took the flag from. Filed
under a parent that is not express, or under no parent, it is not express.
**Reparenting never touches it**: moving an issue under an express parent does
not mark it, and moving it away does not unmark it — the flag is a fact about
how an issue came to exist, not one that follows a parent around. This is also
why unmarking a parent leaves every child exactly as it was: the flag was
copied once, at filing, and the two are unlinked from that moment on.

Filed issues land in the leftmost column, Draft, which does not ship marked
`ExpressSkips` — so an express epic's freshly filed stories wait there until
the operator ticks Draft or moves them on by hand. That is deliberate: Draft is
where ideas are written, and leaving it unticked is the safe default.

Setting it is closed to an API key, the same cut as expedite; see [The one edge
that is deliberately cut](#the-one-edge-that-is-deliberately-cut). It follows
that there is no `hatch express` verb — the CLI authenticates with a key, so
the terminal shows the flag (`board`, `queue`, `show`, `next`) and sets it
nowhere.

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

That column is **measured rather than named**: the implementation column is the
one whose own next move is into the awaiting-review column, which is itself the
column immediately left of the first terminal one — exactly where the migration
that added `review` placed it. On a stock board that is `To Do` to `In
Progress`, the one transition where code gets written. Named, both would be
rules that quietly stopped applying the day an operator renamed a column.

An issue **already in** the implementation column is never gated. The move into
review is not a dependency's to refuse, so work that started finishes rather
than stalling half-written.

**Satisfied means done.** An edge clears when the issue it names is in a
terminal column — merged, not merely up for review. Anything softer and story
two starts on top of story one's unmerged branch, which is the failure the whole
feature exists to prevent, and it is why the chain advances at the operator's
merge rather than at an agent's move.

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

### WIP

How full the [WIP section](#status) is right now, as one number against one
limit — the read every later story shares (`Wip.LoadAsync` in
`Modules/Hatch/Wip.cs`) rather than counting for itself.

**The load** is every issue of a counted type sitting in a WIP column, plus
every issue of a counted type outside the section that holds a live
[claim](#claim) whose next column is itself a WIP column. Which columns count
and which types count are exactly what `IsWip` and the `WipLimits` row already
say — see [Status](#status) — narrowed the same way: a deferred or terminal
column never counts, whatever it is flagged.

**The claimed-inbound half** exists because a claim is what stops a second
runner filling the same slot: an issue in a feeder column that a runner has
already taken is effectively on its way into the section, and letting it
through the gate while ignoring it in the count would let the section overfill
by exactly the number of runners working the feeder column at once. It counts
only while the claim is live — an unclaimed issue in a feeder column counts for
nothing — and it drops out of the load the instant the claim does, one second
past the TTL, same as if it had never been claimed.

`null` means no WIP at all: no column is flagged, or no limit row exists. A
board that has never turned WIP on reads exactly as one that predates it.

Read at `GET /api/hatch/board`'s `wip` block, and printed as one line by
`hatch board`.

On the board itself the WIP columns read as one zone. A band spans each run of
adjacent WIP columns - a section split by a column outside it draws two bands,
both showing the same meter - reading `WIP 3 of 5` and, once claimed-inbound is
non-zero, `(1 on the way)`. Tightness is one of four, each its own tint: *room*
(two or more left), *tight* (one left), *full* (at the limit, said in the band
as well as tinted, so the two do not depend on colour alone to be told apart),
and *over* (past it). Picking up a story or a bug from outside the section
previews the band with that card added, for as long as the drag lasts, and
lights every column of the section as one target; a task, an epic, or a card
already inside changes nothing. The count never follows the filter - it is
`board.wip`'s own count, drawn whether or not the card counted is on screen.
Where `board.wip` is null there is no band at all: nothing here asks about a
drop into a full section or refuses one, either - that is the server's `409`,
and the paragraph below is what a full section does with it.

**The refusal.** A move whose `from` is outside the section and whose `to` is
inside is refused with `409` while the load is at or over the limit - a move
within the section or out of it, a task or an epic, and an issue already
counted (inside the section, or outside it on a live claim headed in) are
never refused. The body is `{ error, load, limit }` (`WipRefusalDto`), `error`
the sentence and `load` the count *before* the move - the same `409` from
`POST /issues/{key}/move`, `PATCH /issues/{key}` and `POST /issues/bulk`
alike, and every door (the board, the issue page, the CLI, the bulk page)
prints the same sentence. A person may say *move anyway* (`wipOverride: true`
on a move or a patch; bulk takes no override), which writes `wip_overridden`
beside `status_changed` - see [Issue event](#issue-event). Overriding is a
person's call and not an agent's: a key or a keyless runner sending
`wipOverride: true` is refused with `403`, whatever the load and even where no
limit is set. On the board and on the issue page, the browser never sends that
flag on its own first guess: the `409` raises a dialog quoting the sentence
above and asking *move anyway?*, and only a press of *Move anyway* resends the
same request with `wipOverride: true` - the event that follows names who
pressed it.

### Claim

Seven nullable columns on the issue row — `ClaimToken`, `ClaimedBy`,
`ClaimRunner`, `ClaimedAt`, `ClaimHeartbeatAt`, `ClaimChatter`, `ClaimChatterAt`
— and a lease is all of them set or all of them null.

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
answers `409` with it. A relative's claim past its TTL folds nothing, exactly as
an issue's own expired claim does.

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
leading with the runner rather than who it runs for — "Buster Bluth is working
AER-12", with ", for Nathan" appended only when the two differ — plus when it
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

**Clearing a claim does not stop the runner.** The operator's
`DELETE .../claim` takes the lease off the row and nothing else: that session
keeps running, keeps pushing, and may still move the ticket, because moves are
not gated on a token. What clearing does is make the issue claimable again, so
the next pass may spawn a second session at it and the two would then both be
writing to it. It is the sentence the confirmation on the issue page leads with,
and the reason to press it is a runner known to be gone or known to be wrong.

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
trail says who took a ticket and who let it go. A heartbeat writes none: it is a
meter reading rather than a decision, and the trail would otherwise be a row a
minute for every running increment.

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
`Branch?`, `BranchSha?`, `Verdict`, `Files`, `HoldsTrunk?`, `CheckedAt`, `Runner`,
`CheckedBy`. What a runner found when it merged an issue's branch against the
trunk, and the two refs it was looking at when it did. `Verdict` is one of four:

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
4. **The question opens when a save moves the row into *failed and flagged*.**
   That covers `pending` then `failed` on a marked sha, and `failed` read first
   and marked after. The board writes it in the same save as the verdict, so no
   pass can see that failure without it, and a repeated verdict writes no second.
   It names the sha and the failing checks, says a build increment pushed it, and
   offers the stall guard's two answers — `leave it` and `try again` — in the
   stall guard's words, which live in `Hatch.Contracts` so both share one wording.
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
`Verdict`, `Failing` (`jsonb`), `CheckedAt`, `Runner`, `CheckedBy`, `BugIssueId?`.
The same fact as a [build check](#build-check), but about the tip of a
repository's trunk rather than an issue's branch — a trunk build is nobody's
issue, so it is not keyed on one. `Verdict` is the same four
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

### Comment, question and answer

`EfHatchComment` — `IssueId`, `Author`, `Body` (markdown), `Kind`, `AnswersId`,
`Options`, `DeliveredAt`, `DeliveredTo`, `CreatedAt`.

Most comments are notes: a commit sha, a summary for a reviewer, a change of
mind. Three are not, and those carry a `Kind`.

- A **question** (`Kind = "question"`) is an agent saying it cannot proceed
  without a decision that is not its to make.
- An **answer** (`Kind = "answer"`) is that decision, bound to the question it
  settles by `AnswersId`.
- A **message** (`Kind = "message"`) is something said to whichever session is
  working the issue, written from the Claim panel.

A question is a row rather than a heading in a comment body for the same reason
a ready date is a column rather than a line saying "not until March": something
has to be able to *act* on it. Prose in a thread can be searched; it cannot be
counted, and it cannot block a dispatch.

**Open is computed, never stored** — a question is open when no comment points
at it. That is why the link runs answer→question rather than the other way: a
decision can be refined by a second answer without editing the first, and a
question can never be open and answered at once because two writes disagreed.
[`Questions.cs`](../src/Hatch.Api/Modules/Hatch/Questions.cs) holds the single
definition, and the three callers that need it — the board badging a card, the
dispatcher refusing a run, and the list somebody sits down to answer — share it
rather than writing three.

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

### Issue event

`EfHatchIssueEvent` — `IssueId`, `Actor`, `Kind`, `Payload` (`jsonb`), `At`.
Append-only, written by every mutating endpoint, never edited and never deleted
except with its issue.

Kinds: `created`, `retitled`, `redescribed`, `retyped`, `status_changed`,
`parent_changed`, `ready_changed`, `due_changed`, `pull_request_changed`,
`model_override_changed`, `effort_override_changed`, `assignee_changed`,
`expedited_changed`, `express_changed`, `dependency_added`,
`dependency_removed`, `claim_taken`, `claim_released`, `claim_cleared`,
`merge_check_changed`, `build_check_changed`, `wip_overridden` (a move into a
full [WIP](#wip) section, let through because a person said *move anyway* -
payload `{ limit, load, to }`, `load` counting the card itself, written beside
`status_changed` only when the move would otherwise have been refused),
`commented`, `messaged`, `message_delivered` (the payload
names the comment and the runner), `asked`, `answered`,
`imported`.

Nothing renders this, and it has been written since the first release anyway,
because an event log is the one feature that cannot be added retroactively:
turned on in March, it answers nothing about February. The cost is a row per
edit.

`Payload` is schemaless on purpose — `{ "from": …, "to": … }` for an edit, the
source filename for an import — because the shape differs per kind and a column
per field would be a migration every time a new verb is logged.

**Rank-only moves are deliberately not events.** Dragging a card up its column
is board hygiene, not work, and logging it would bury the status changes that
matter under a hundred lines of tidying.

### Playbook

`EfHatchPlaybook` — `FromStatusId`, `ToStatusId`, `Types`, `Prompt`, `Model`,
`Effort`. Unique on `(from, to, types)`.

What an agent is told, and how much thought to spend, when it moves an issue of
some type from one column to the next. See [the dispatcher](#the-dispatcher).

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
read carries the scope like everything else.

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

**And so is expediting one**, which is the same edge as the assignee read from
the other side. An assignee holds a ticket *off* the night shift; expedite puts
one at the *front* of it — so a key that could set one could put its own ticket
ahead of everything a person filed, every night, without anything looking wrong
on the board. `PUT /api/hatch/issues/{key}/expedite` therefore lives on its own
controller (`IssueExpediteController`) carrying no class-level scope, cut in the
route by the same means. Reading is open like the rest: `expedited` rides
`IssueDto` and `IssueCardDto`, because an agent is entitled to know why it was
sent where it was sent. It follows that there is no `hatch expedite` verb — the
CLI authenticates with a key, so the terminal *shows* the flag on `board`,
`queue` and `show` and sets it nowhere.

**And so is filing the bug a failing trunk's button offers (HA-95).** The bug
`POST /api/hatch/trunk-builds/{id}/bug` files is expedited from the moment it
exists, so a key that could press the button could put a ticket of its own
choosing at the front of the night the same way a key that could expedite
directly could. `TrunkBuildBugController` therefore carries no class-level
attribute either, cut by the same means as expedite; filing itself goes
through `IssuesController.CreateIssueAsync`'s internal, expedited-aware
overload, so the bug's number, rank, first column and `created` event are the
ordinary ones and the flag is set in the same save rather than a second write
after. Reading the trunk builds themselves stays Hatch-scoped, the way reading
a build check is: a verdict about a sha is a fact any runner reads the same
way, whichever repository it binds.

**A key's owner is cut the same way, and for the reason the assignee edge
names directly.** `--mine` (see [the dispatcher](#the-dispatcher)) reads a
key's `OwnerPersonId` to decide whose tickets it may take, so a key that could
set its own owner could simply say it is whoever holds the tickets it wants.
`ApiKeysController` is `[RequireAdmin]` with no `AcceptScope` on the whole
class — not one route carved out of it — so no key can mint a key, revoke one,
or change whose it is; a person does that, at the API Keys page, on purpose.

**And so is the WIP section and its limit.** Which columns are work in
progress, and how much of one slice of the board may sit across them at once,
is the operator's to set — a key that could raise the limit or empty the
section could pull more of its own work in overnight, the loop stalling behind
a ceiling it had just raised for itself. `PUT /api/hatch/wip` therefore lives
on its own controller (`WipController`) carrying no class-level scope, cut in
the route by the same means, and checked a second time in the action for
`IssueClaimController.NotAPerson`'s reason: `RoleGate` is dormant wherever the
wall is off, and a keyless runner there is a program too. Reading is open, like
the rest: an agent stalled behind a full section is entitled to know why.

**And so is express, both the issue's flag and the column's.** Express decides
which gates the loop may pass *unattended* — a key that could set either could
carry its own ticket through the night with nobody reading it first, the same
kind of widening a playbook or an assignee would be. `PUT
/api/hatch/issues/{key}/express` lives on its own controller
(`IssueExpressController`) carrying no class-level scope, cut by the same
means as expedite. The column half cannot ride `PATCH /api/hatch/statuses/{id}`
— that route accepts the `hatch` scope on every action, so one more field on
the ordinary status patch would have been a key ticking its own gate — so it
gets its own action, `PUT /api/hatch/statuses/{id}/express-skips`, carrying a
plain `[RequireRole(PersonRole.User)]` the way `ProjectsController.PutRepositories`
does; the rest of `StatusesController`'s actions each carry the class-level
attribute explicitly instead, because `RequireRoleAttribute` is
`AllowMultiple = false` and a method-level attribute silently *replaces* a
class-level one rather than tightening it. Reading both is open like the rest:
`express` rides `IssueDto` and `IssueCardDto`, and `expressSkips` rides
`StatusDto`. It follows that there is no `hatch express` verb, the same as
expedite.

One related edge is **not** cut, and is stated rather than papered over:
**nothing stops a key answering its own question.** A key is what
`hatch answer` types with and it is also what a spawned agent inherits; the
server cannot tell them apart, and a check that looked like it could would be
worse than none. What holds the loop shut is one step further out — the dispatch
is refused while a question is open, and the dispatch is a command the operator
types. A second scope minted for agents would close it properly, and is not
worth a column until somebody wants one.

## API surface

Everything under `/api/hatch`, every route `[RequireRole(PersonRole.User,
AcceptScope = "hatch")]` except where noted. Issue routes take the display key (`AER-12`).

| Route | Verbs | Notes |
|---|---|---|
| `/projects` | GET, POST | POST validates key format and uniqueness |
| `/projects/{id}` | PATCH, DELETE | PATCH name and key; DELETE 409s unless the project is empty, and takes its [repositories](#repository) with it |
| `/projects/{id}/repositories` | GET | The ordered list of remotes, in `SortOrder` — see [Repository](#repository) |
| `/projects/{id}/repositories` | PUT | **Person only** — plain `[RequireRole(User)]`. The whole ordered list, `[{ remote, baseBranch? }]`; refused as a whole, naming the entry, on an empty, over-limit, unparseable or duplicate remote. Re-sending the same list writes nothing |
| `/statuses` | GET, POST | |
| `/statuses/{id}` | PATCH, DELETE | DELETE 409s while any issue holds it |
| `/statuses/{id}/express-skips` | PUT | **Person only** — plain `[RequireRole(User)]`. `{ expressSkips }` — which columns an [express](#express) issue is carried past with no session. Neither `POST /statuses` nor `PATCH /statuses/{id}` can set it |
| `/wip` | GET | `{ limit, types, statusIds }` — the flagged columns that are neither deferred nor terminal, in board order |
| `/wip` | PUT | **Person only** — plain `[RequireRole(User)]`, checked again in the action. `{ limit?, statusIds? }`, the bulk rule throughout: `limit` is a string (`""` clears it, a whole number of one or more sets it), `statusIds` is the whole section (`[]` clears it) and refuses a column that does not exist, or one that is deferred or terminal. Re-sending what is held writes nothing |
| `/board` | GET | Statuses plus every issue, ordered by `(StatusId, Expedited desc, Rank, Id)`. Never filtered — the browser folds not-yet-ready cards away; the server hands over all of them — and `wip`: the section's limit, counted types, status ids, load and claimed-inbound part, or `null` where no column is flagged or no limit is set (see [WIP](#wip)) |
| `/issues` | GET, POST | GET filters on `projectId`, `type`, `statusId`, `parentKey`, `ancestorKey`, `text`, ANDed, all optional |
| `/issues/bulk` | POST | `keys` plus any of `type`, `statusId`, `parentKey`, `readyAt`, `dueAt`. No `wipOverride` — a full [WIP](#wip) section is a per-key failure, named in `failures` |
| `/issues/{key}` | GET, PATCH, DELETE | PATCH writes one event per changed field; `""` clears a parent, a date or the pull request URL; `wipOverride` — see [WIP](#wip) — moves a full section anyway, `409` (`WipRefusalDto`) otherwise, `403` from a key or a keyless runner |
| `/issues/{key}/move` | POST | `{ statusId, afterKey?, beforeKey?, fromStatusId?, wipOverride? }` — the server computes the rank. A card no longer in `fromStatusId` is a 409 and nothing is written; a move into a full [WIP](#wip) section is the same, unless `wipOverride` is set (person only - `403` from a key or a keyless runner) |
| `/issues/{key}/comments` | GET, POST | POST carries the kind (a note, `question`, `answer` or `message`), the `answersId`, and a question's options; every comment carries `deliveredAt` and `deliveredTo` |
| `/issues/{key}/messages/deliver` | POST | marks messages read, all unread or the `ids` named, and answers with only the ones this call marked |
| `/issues/{key}/questions` | GET | `?open=false` for the answered ones too |
| `/issues/{key}/events` | GET | Newest first |
| `/issues/{key}/playbook` | PATCH | **Person only** — plain `[RequireRole(User)]`. The issue's own model and effort; `""` hands either back to the playbook |
| `/assignees` | GET | Every person and every live key, plus who the caller is — the picker's rows and *Assign to me* in one read |
| `/issues/{key}/assignee` | PUT | **Person only** — plain `[RequireRole(User)]`. `{ kind, id }`, or both null to unassign — see [Assignee](#assignee) |
| `/issues/{key}/expedite` | PUT | **Person only** — plain `[RequireRole(User)]`. `{ expedited }` — *this one first*, floated on the board and taken first by the dispatcher. Setting what it already holds writes nothing |
| `/issues/{key}/express` | PUT | **Person only** — plain `[RequireRole(User)]`. `{ express }` — see [Express](#express). Setting what it already holds writes nothing |
| `/issues/{key}/claim` | POST | Takes the [lease](#claim). `{ runner }`; answers with the token, the holder, when it was taken and the TTL. `409` naming the holder where something live already has it — including the same runner asking twice |
| `/issues/{key}/claim/heartbeat` | POST | `{ token, chatter? }` — refreshes it, `204`. `409` on a token that is not the row's, and on a lease that is over. `chatter` absent leaves the carried line alone, `""` clears it, anything longer than the column is truncated rather than refused |
| `/issues/{key}/claim?token=…` | DELETE | Releases it, `204`. A mismatched token is `409` and clears nothing; an issue holding no claim is `204` and writes nothing. **With no token at all it is person-only** — an agent that could clear another runner's claim could take a ticket off it mid-increment |
| `/issues/{key}/merge-check` | PUT | Keeps a runner's [verdict](#merge-check) for one repository. `{ remote, trunk, trunkSha, verdict, branch?, branchSha?, files?, runner, holdsTrunk? }`; answers with what it now holds. Refused, in a sentence, for a remote that does not canonicalise, an unknown verdict, `conflicted` with no files, and `clean` or `conflicted` with no branch sha; `404` on an unknown key. The column is not checked. A verdict that repeats the stored one writes no event, and neither does a change to `holdsTrunk` alone |
| `/questions` | GET | Every open question in the house |
| `/plan`, `/plan/{key}` | GET | See [the level above the board](#the-level-above-the-board) |
| `/work/next` | GET | See [the dispatcher](#the-dispatcher). `?heldToken=` names a [claim](#claim) of one's own, so it is not folded past as somebody else's. `?remote=` (repeatable), `?standing=` and `?clones=` declare what the runner has; absent is undeclared and folds nothing. `?mine=true` narrows the pass to the caller's own tickets — see [one more, on `next` alone](#one-more-on-next-alone); `400` when the calling key belongs to nobody |
| `/work/{key}` | GET | See [the dispatcher](#the-dispatcher). Same `?heldToken=`, `?remote=`, `?standing=` and `?clones=` as `/work/next`. `?mine=` is ignored — somebody who names a ticket has already chosen it |
| `/work/queue` | GET | The same walk `next` takes, reported rather than acted on, and the same three repository flags — see [what a pass skipped](#what-a-pass-skipped). Same `?mine=true`, folding every ticket that is not the caller's own with a sentence naming whose it is |
| `/work/{key}/hop` | POST | Carries an [express](#express) issue one column right with no session — see [the hop](#the-hop). Takes the same query parameters as `GET /work/{key}` and no `heldToken`: a hop takes no claim. `409` carrying the fold's sentence where the issue is blocked, and `409` where it is clear but not a hop |
| `/issues/{key}/build-check` | PUT | Keeps a runner's [build verdict](#build-check) for one repository. `{ remote, branch, sha, verdict, failing?, runner, pushedByIncrement? }`; answers with what it now holds. Refused, in a sentence, for a remote that does not canonicalise, an unknown verdict, no branch or sha, and `failed` with no failing checks; a link that is not `http(s)` is stored as null; `404` on an unknown key. The column is not checked. A verdict that repeats the stored one writes no event, and one that moves the row into failed-and-flagged writes a question with it |
| `/trunk-builds` | PUT | Keeps a runner's [trunk build verdict](#trunk-build) for one repository's trunk. `{ remote, trunk, sha, verdict, failing?, runner }`; answers with what it now holds. The same refusals the build check's write takes, naming the trunk rather than the branch. No event and no question — there is no issue to write either on. `passed` lets an attached bug go |
| `/trunk-builds` | GET | Every stored [trunk verdict](#trunk-build), ordered by canonical then trunk — what a runner's poll reads once a poll, since a trunk carries no issue for the verdict to ride in on |
| `/trunk-builds/{id}/bug` | POST | **Person only** — no class-level scope, the way expedite is cut. Files the bug a failing trunk's *File a bug* button asks for (HA-95), expedited, in the project that binds the repository, in the board's first column — see [what is waiting on you](#what-is-waiting-on-you). `409` once the row is no longer failing; `400` naming the Projects page when no project binds it; a second call answers the bug already filed rather than filing another |
| `/work/review` | GET | Every issue in the review column the caller holds a checkout of, with its bound repositories and the [merge checks](#merge-check) the board holds — what a runner's poll reads before it asks git anything. `?remote=` (repeatable) and `?standing=` as on the queue; no `clones`, because a poll clones nothing. Not narrowed by a claim, a question, a date or an assignee — see [checking the branches in review](#checking-the-branches-in-review) |
| `/playbooks` | GET | **Reads only.** POST/PATCH/DELETE are plain `[RequireRole(User)]` |
| `/import/preview`, `/import/preview-text`, `/import` | POST | See [the importer](#the-importer) |
| `/utilization` | GET | The account's Claude headroom, read by the server. `204` when no token is configured; `?refresh=true` bypasses the cache — see [the battery](#the-battery) |
| `/attention` | GET | What the loop is waiting on a person for — the issues up for review that carry a pull request, how many in that column carry none, every open question in the house, every repository's trunk whose latest build is failing (counted, unlike the rest below), and (listed, not counted) the issues in review whose branch conflicts or whose build has failed, plus how many of those are held back from the pull request list on that account (`reviewsHeldBack`). One read for all of it — see [what is waiting on you](#what-is-waiting-on-you) |
| `/local-person` | GET | What to call whoever is sitting here, and whether anybody said so. `204` wherever the wall is up |
| `/settings` | GET, PUT | **Person only** — plain `[RequireRole(User)]`, so a key is refused the read as well as the write. The two settings a Hatch install of its own has — see [the credential](#the-credential). PUT follows the bulk rule: a field left out is left alone, `""` clears it |
| `/settings/claude-token` | GET | The token itself, wrapped with `SecretProtector` for the wire. The one route in Hatch that hands a live secret back out, and it is cut the opposite way to `/settings` beside it — **a key or a keyless runner may take it**, because its ordinary caller is the container runner's entrypoint (`containers/hatch-runner/`) authenticating a `claude` CLI it starts itself. **Refused outright wherever the wall is up** — every caller, key or person — because a token crossing a network is a different question from one handed to a container on the same laptop. `204` when none is set |
| `/issues/{key}/work-log` | GET, POST | What each session on this issue cost. **POST is a key only** — a browser is refused outright, because the only honest writer of a meter reading is the dispatcher that read it. See [the leaderboard](#the-leaderboard) |
| `/work-log/sessions` | GET | The sessions in a range, ranked, with the range's own totals — see [the leaderboard](#the-leaderboard) |
| `/work-log/history` | GET | The same rows folded into equal buckets of time, for the graph |
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
up before anything is written, and the bulk path shares it.

## The battery

The bar carries one element the board does not: how much Claude the
account has left, and how long until it comes back. It is read from every page
without opening another app, and it is drawn from `GET
/api/hatch/utilization`.

**Absent is a supported state, and it is the default.** An installation with no
Claude subscription token gets a Hatch with no battery — no element, no
placeholder, no reserved space, and nothing anywhere reporting an error. This
is an enhancement to a tracker, not a dependency of one, and the `204` the
endpoint answers with is what says so.

### The credential

The token is a **secret-valued site setting**, `ClaudeSubscriptionToken`, set on
Hatch's own **Settings** page and stored the way the Immich key and the
Anthropic key already are: obfuscated at rest, redacted on read, never leaving
the API. That page holds two settings and no others: this token, and
`LocalPersonName` — what Hatch calls whoever is sitting at the machine
("Local mode"), shown only where
there is no wall, because with one the name comes from the grant. It lives in
Hatch rather than on the admin app's Settings page so that an installation with
no admin app — which is every installation that is only somebody's tracker —
can still set both. It is an OAuth token for the operator's own Claude subscription, and
like every other credential in Hatch it is the operator's to supply
([`docs/ethos.md`](ethos.md)) — nothing about one household's account may be
true of the artifact.

A setting rather than an environment variable for a reason beyond habit: this
credential is genuinely optional, the provisioning path cannot express an
optional cluster secret, and an expiring token is re-pasted far more often than
a cluster is provisioned. Leave it blank and there is no battery, which is a
working configuration.

The token reaches the app through one interface, `IClaudeCredential`, whose one
member answers "the token, or null". Null is an ordinary value there rather
than a startup failure, and moving where the token lives is a change to one
class.

### What the endpoint answers

`GET /api/hatch/utilization` is `[RequireRole(PersonRole.User, AcceptScope = "hatch")]` like the
rest of the module. It answers `204 No Content` when no token is configured, and
otherwise:

```json
{
  "state": "ok",
  "readAt": "2026-09-07T08:12:03Z",
  "limits": [
    { "window": "session", "label": "Session", "percent": 17,
      "tone": "normal", "resetsAt": "2026-09-07T12:00:00Z", "isActive": true }
  ],
  "credits": { "isEnabled": true, "monthlyLimit": null, "usedCredits": 0,
               "currency": "USD", "spendLimitReached": false }
}
```

Every name in it is Hatch's own. The account's spelling stops at
`ClaudeUsageClient`, so the day a field is renamed upstream there is one file to
fix and no page that has gone blank — and a test asserts that not one of the
account's field names survives into the body.

- `state` is the whole of the degraded story. `ok` — read within the freshness
  window. `stale` — the account could not be reached and this is the last good
  reading, whose age `readAt` gives. `unknown` — could not be reached and there
  has never been a good reading, so `limits` is empty and `readAt` is null.
- `window` is `session`, `weekly`, `weeklyModel` or `other`, in the order the
  account listed them. `other` is the carry-through for a kind Hatch has never
  seen: it is rendered, not dropped and not thrown on.
- `label` is what the row is called on screen, decided on the server —
  `Session`, `Weekly`, the account's own display name for a model-scoped row,
  and for `other` the account's own kind with its underscores turned to spaces.
  So a fourth kind draws a row with a plain name rather than a blank one, and
  **no model name is written down in this repository**.
- `tone` is `normal`, `warn` or `danger` — the *decision*, not the account's
  word for it, so the rule lives in one file on the server and the client paints
  what it is told. Severity upstream is an open vocabulary and only `normal` has
  ever been observed, so a severity Hatch knows maps and anything else falls
  back to the percentage (90 and up is danger, 75 and up is warn). Without that
  fallback the first new word upstream would paint a spent window calm.
- `credits` is null when the account reports no extra usage block at all, and
  the modal then says nothing about credits.

### How often it is actually read

The reading is held by a process-wide cache, and the cache holds the last good
one **forever** — not an `IMemoryCache` entry, because "the last good reading,
however old" is exactly what an eviction policy would throw away and it is what
a `stale` answer is made of.

Two floors bound how often somebody else's endpoint is asked:

- **Five minutes of freshness**, with a semaphore and a re-check inside it, so
  twenty open tabs polling every two minutes are one upstream read.
- **Sixty seconds after a failure.** Without it the failure path would be the
  only path with no rate limit on it, and an outage would become a request per
  tab per poll.

`?refresh=true` ignores both. It is the modal's refresh control: one person
pressing a button once, which is not what the floors exist to bound.


## What is waiting on you

Beside the battery, at the right end of the same bar, is the other thing the
bar is for: whether the loop has stopped and is waiting on a person. Two
things stop a night — a pull request nobody has reviewed, and a question nobody
has answered — and the server already knows both. The control is quiet while
neither is true and loud the moment either is, and pressing it hands over the
links that unblock them.

The panel draws two groups, in this order: **Human** — trunk builds that fail,
then pull requests to review, then questions to answer — and **Agent** —
branches that conflict, then branch builds that fail. A pull request only ever appears under one of the two:
one the loop is still working through a conflict or a red build on is not a
person's to look at yet, so it moves out of the pull-request section and into
the group naming what is holding it back, rather than sitting in both.

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

Unlike the battery, **it always draws something**. "Nothing is waiting" is an
answer worth having, and it is the one it gives most of the time.

`GET /api/hatch/attention` answers every list in one read, carrying
`[RequireRole(PersonRole.User, AcceptScope = "hatch")]` like the rest of the module:

```json
{
  "reviews": [
    { "key": "AER-12", "title": "A story that landed", "type": "story",
      "pullRequestUrl": "https://forge.example/pulls/12",
      "buildState": "success", "holdsTrunk": true, "trunk": "main" }
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
  ]
}
```

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
- **Open means what it means everywhere else.** `questions` is the same call
  `/questions` answers with — `Questions.Open`, a question with nothing pointing
  at it — so the count on the bar and the badge on a board card cannot
  disagree.

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
reasoning that keeps those two quiet does not hold here: nothing is dispatched
at a trunk, so a failing one has no agent already on it. It sits exactly as it
is until a person presses **File a bug**, which is what the badge exists to
surface.

Each row carries a button, unless it already carries a bug's key as a link in
its place — `POST /api/hatch/trunk-builds/{id}/bug`, person-only, the same cut
[expedite](#the-one-edge-that-is-deliberately-cut) takes, because the bug it
files is expedited and expedite is a person's call. It is filed in the shape a
bug against a failing trunk was always filed by hand, from the forge's own
page: a bug, titled *Build failing on &lt;trunk&gt;*, described with the
repository, the sha and a link to each failing check, expedited, in the board's
first column — in the project that binds the repository as its primary,
lowest project id first, else the lowest-id project that binds it at all, else
refused naming the Projects page.
The bug stays attached across new failing shas — a fix that fails again is the
same outage — and is let go the moment a later build on the trunk passes, so
the next failure offers the button again. A second press, or a second person,
gets the bug already filed rather than a second one.

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


## The leaderboard

The battery says how much Claude is left. The leaderboard says **what the nights
cost, and on what** — which is a question account-wide utilization cannot answer
at all, because it is honest and anonymous and no arithmetic over it can say
which epic ate the evening.

Every number on the page comes from one table: `hatch.work_log_entries`, the row
the dispatcher writes at the end of every unattended increment. It carries the
session id, the issue, both instants, the duration, the turns, the four token
counts, the notional USD, whether the run errored, and what the session said it
did in its `work-log` block. **No Claude credential is anywhere in this path.**
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

**Except for what somebody expedited**, which is considered first wherever it
sits. The scan walks the columns twice — every [expedited](#expedite) candidate
right to left, then everything else right to left — so an expedited bug in the
leftmost column is reached before a non-expedited story in the rightmost one,
and inside each half the order is the board's own. Two passes rather than a sort
of the finished rows, because the [published scan](#what-a-pass-skipped) is the
explanation of what `next` picked, and a comparator applied afterwards would be
a second opinion about the order.

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
what a printed line reads to say where the value came from.

An override changes what a dispatch costs and never whether one happens.
Nothing in the refusals below consults one: an issue with no playbook for its
next move is refused in the same sentence whether it names a model or not.

**Nine refusals**, and two of them are rules of the whole loop rather than
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
6. The issue holds an unanswered question — *it is waiting on a person, not on
   an agent*.
7. The project's [repositories](#repository) match none of the remotes the
   caller declared, and the move is into the column where the code gets
   written, or is a conflict or a failing build in review — a session on a
   branch needs a checkout of the repository the branch is in. A caller that declares nothing — an older
   CLI, or the issue page — is not folded by this at all: declaring is opt-in,
   which is what keeps them working unchanged. A project bound to nothing is
   worked from the caller's standing checkout exactly as before; one bound to
   remotes none of which match is refused unless the caller says it will clone
   what it lacks.
8. Something it [depends on](#dependency) is unfinished, and the move is into
   the column where the code gets written. Only that move: a pull request that
   already exists is not held back by what its ticket once waited on.
9. It is in review and there is nothing for an agent to do on its branch: it
   neither conflicts with the trunk nor has a build that failed on its current
   tip — because it merges cleanly and its build passes, is still running, was
   not read on that tip or has no checks, because it has no branch on origin
   (one already merged counts as none), because more than one branch is named
   for it, or because no runner has checked yet. See [the review
   dispatch](#the-issue-in-review-whose-branch-conflicts-or-whose-build-failed-is-dispatched-to-review).

…and then, if none of those, one last check before the ordinary refusal: is
this a [hop](#the-hop)? An issue that is [express](#express) and stands in a
column marked [`ExpressSkips`](#status) needs no playbook at all — the pass
calls the row clear, and the caller carries it across itself with
`POST /api/hatch/work/{key}/hop` rather than spawning a session. Every refusal
above this one still applies to an express issue exactly as it applies to any
other: the hop answers only "does this column still need a session", and
nothing about a live claim, a ready date, an assignee, a question, a
repository or a dependency is any different for it. Only where none of those
folds it either, and it is not a hop, does the pass fall through to the
ordinary refusal: no playbook covers this transition for this type.

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

**The loop's own write, on the loop's own say-so.** Take an issue that is
[express](#express) and stands in a column marked
[`ExpressSkips`](#status), with no unanswered question, and
`POST /api/hatch/work/{key}/hop` carries it one column right — the same
column `Columns.Advance` would send a session to — and spawns nothing. No
model runs, nothing is spent, and no work-log row is written, because a hop is
not an increment.

It takes the same query parameters `GET /api/hatch/work/{key}` takes, and no
`heldToken`: **a hop takes no claim.** A claim protects a session that runs for
minutes; a hop is one write, and the server re-judges the issue as the write is
made, with the same `Blocked` a named dispatch is judged by — so a stale runner
cannot carry an issue the server would no longer carry, and claiming would add
two history lines to protect nothing.

Two answers, both `409`, and each carries the sentence a reader would see on
`hatch queue`:

- The issue is blocked by some other fold — a claim, a ready date, an
  assignee, a question, a repository, a dependency, a terminal next column.
  The sentence is exactly the one `Blocked` already gives for that fold.
- The issue is clear, but is not a hop — not express, or standing in a column
  nothing has ticked. A session moves this issue, and a hop does not.

Otherwise the move is made: the issue's rank is set with
`RankService.BottomAsync`, the same as any other move, and the history gets one
`status_changed` event naming the caller as the actor and carrying
`{ from, to, express: true }` — the same shape `IssuesController.MoveIssue`
writes, with one more field, so a card that passed a gate with nobody present
says so on its own trail.

**Why the server performs the hop, on its own route, rather than the CLI
calling the ordinary move endpoint:** `IssuesController.MoveIssue` moves
anything a key asks it to, with no judgement of its own — a client that called
it for a hop would be the client deciding whether the move was safe, and the
trail could not say *express*. Judging *and* moving in the one write is what
keeps the rule that calls a row a hop and the write that performs it in one
place; see [where the loop's rules live](#where-the-loops-rules-live).

`WorkDto.Hop` and `QueueEntryDto.Hop` say which rows are hops — on `WorkDto`,
`Playbook` is always null where `Hop` is true, even where one covers the move,
so no client can spawn a session for a hop by accident.

### The issue in review whose branch conflicts, or whose build failed, is dispatched to review

Every other dispatch ends in a different column, and this one ends where it
started. `Columns.Target` answers "where does a dispatch out of this column
end": the next column, except that the review column — when a terminal column
stands after it — is dispatched to itself. A move is a *review move* exactly
when its two ends are the same column, and `WorkDto.Kind` and
`QueueEntryDto.Kind` say which of two it is (`conflicts` or `build`; `advance`
for every other move) — derived, stored nowhere, and nothing compares a column's
name. What decides between the two is what runners have reported about the
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
claim, a ready date, a person assignee, an open question, a repository this
runner has no checkout of. The verdicts come after the repository — a question
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
The board opens a question with the failed verdict, and an open question folds
the issue — see [build check](#build-check). A `leave it` answer does not stop
the loop: once answered, the issue is dispatchable again while its build still
fails on that sha, exactly as after the stall guard's question.

**Only what is broken.** A branch that has fallen behind the trunk but still
merges cleanly, and whose build is green or still running, is left alone: the
branch is brought up to date when its pull request opens, and the forge does the
rest. There is no setting for keeping branches current, and a pull request that
merges and builds cleanly is never touched. Re-running a failed check is not
here either: a flaky check is a failure, and an increment that only re-runs one
leaves the tip where it was, which is a stall.

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
every row would make a whole-board read expensive for nothing.

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
first: a next column that is terminal, then a ready date, then an unanswered
question, then a repository this runner lacks, an unmet dependency, and — for an
issue in review — the verdict on its branch, then [the hop](#the-hop), and last
the missing playbook — last because it is only worth saying about an issue that
is otherwise a candidate. The one that is the *loop's* policy rather than a
fact about an issue is asked only when the pass is asking, so `work/{key}`
still ignores it.

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

A playbook row is `(from column, to column, issue types) → prompt, model,
effort`. A row naming the issue's type beats a row naming every type, and ties
go to the older row, so "Breakdown to Backlog, epics" can say something
different from "Breakdown to Backlog, anything else" without either having to
know about the other.

The matrix exists because **"do the next increment" is not one job**. Turning a
paragraph of intent into an epic with stories under it is the hardest thinking
in the flow and wants the largest model at the highest effort; picking up an
already-specified task and writing the code is ordinary work a smaller one does
well. Encoding that as rows rather than as branches in a script means the
operator retunes it from a page after watching a run go badly, which is the only
way anybody ever finds the right settings.

`Model` is an alias (`haiku`, `sonnet`, `opus`, `fable`) rather than a pinned
id, because a playbook says "the big one" and should still mean it a year from
now; a full `claude-…` id is accepted for an operator with a reason to pin.
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
paper over.

The stock text was written for a conflict alone and reworded when failing builds
were added. A migration (`RewordReviewPlaybook`) rewrites the row **only where it
still reads the seeded text, byte for byte**, and leaves an edited row alone —
an edited prompt is somebody's wording, and the operator is told on the ticket
what to change. The reworded text does not say the merge is in progress, because
for a build dispatch it is not: it points at the branch section for the state of
the tree.

Eight rows are seeded, for the same reason the columns are: a Hatch whose agent
loop cannot run until somebody fills in a table is a Hatch that ships broken. A
feature that does nothing until somebody fills in a table is the same failure,
so the eighth is the stock review playbook, for every type, at `sonnet` and
`high`. A migration seeds it only where no row from the review column to itself
exists; an operator who deletes it turns conflict and build work off, and nothing
puts it back. The other seven cover every transition an agent owns, so `go-to-work` on a fresh install
needs no configuration beyond an origin and a key. They are joined on column
*name*, because a migration cannot know identity-generated ids — so an install
that renamed its columns first seeds nothing, which is the right failure: a
playbook wired to the wrong transition is worse than an empty table.

Nothing re-asserts them afterwards, and nothing overwrites one. `TheFlow` moves
three of them from `inbox → todo` onto `Breakdown → Backlog`, because that is
where specifying a draft happens now, but it writes no prompt, no model and no
effort — an operator who has retuned a row keeps every word of it, and this only
says which transition their playbook is the playbook for. Rows that are not
there are not restored either: deleting a playbook is how an operator takes a
column back from the loop, and a migration that re-seeded one would be handing
it back.

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

### What makes an issue actionable

Ten conditions, the last one a way out of the ninth rather than one more gate.
An issue is the loop's to pick up when it meets every one before it, and the
sentence saying which one it failed is what `work/queue` reports:

1. **There is somewhere for it to go, and that place is not terminal.** For
   most columns that is the column to their right: the end of the board is not a
   transition, and the step into a terminal column is the operator's — *only the
   operator decides that something shipped*. The review column is dispatched to
   itself instead (see the ninth condition), and never into the column after it.
2. **No live [claim](#claim) is held by somebody else.** It is the only fold
   that says *this is being worked right now*; everything below it is about
   whether the issue could be worked at all, which is why nothing else is said
   about a ticket another runner is three minutes into. A caller naming a claim
   token of its own with `?heldToken=` is not folded by its own lease:
   re-reading the dispatch for a ticket one already holds is not a conflict.
   The claim may be a relative's — an ancestor's or a descendant's, at any
   depth — and that folds too; a token of one's own frees the relatives it
   holds as well as the issue.
3. **Its ready date has arrived**, read against the caller's calendar day. A
   card folded off the board is not one to spend an increment on tonight.
4. **Nobody's name is on it.** An issue [assigned](#assignee) to a person is
   somebody's to do, and an unattended pass leaves it alone. An issue assigned
   to an API key, or to nobody, is picked up exactly as it always was. A
   `?mine=true` pass inverts this condition rather than skipping it: it takes
   only a ticket assigned to the caller's own person or key, and folds every
   other one — see [one more, on `next` alone](#one-more-on-next-alone).
5. **It holds no unanswered question.** It is waiting on a person, and another
   agent sent at it would ask the same thing again or guess at the answer.
6. **The project's [repositories](#repository) match a remote the caller
   declared** — or the caller declared nothing at all, which this condition
   does not fold on, exactly as an issue page or an older CLI does not. A
   caller declares with `?remote=` (repeatable), `?standing=` and `?clones=`;
   the first two are checked here and only when the move is into the column
   where the code gets written, or is a conflict or a failing build in review,
   and the dependency
   below is checked on the first of those alone. See [the dispatcher](#the-dispatcher) for the exact sentence.
7. **Nothing it depends on is unfinished** — and only when the move is into the
   column where the code gets written. Everything left of that still moves; an
   edge is satisfied only once the issue it names is in a terminal column. See
   [Dependency](#dependency).
8. **In review, its branch conflicts with the trunk or its build failed.** An
   issue in the review column is the loop's only when a [merge check](#merge-check)
   says `conflicted`, or — on a branch that merges cleanly — when the
   [build check](#build-check) on the branch's current tip says `failed`. Both
   are decided by a runner and by code, never by a prompt, and conflicts come
   first. The sentences for a passing build, a running one, one nobody has read
   on this tip, no checks, no branch, more than one branch and an unchecked one
   are what `hatch queue` prints. A clean branch that has merely fallen behind
   the trunk is left alone. See [the dispatcher](#the-issue-in-review-whose-branch-conflicts-or-whose-build-failed-is-dispatched-to-review).
9. **A playbook covers that transition for that type — or it does not need
   one.** Without one there is nothing to say to the session — and a column no
   playbook leads out of is exactly [how a column becomes the
   operator's](#status), which is why the absence is a fold rather than an
   error. **This is also where the issue's type is decided**, and the only
   place: a type an unattended run does not pick up is a type no row names for
   that move, said in the words that name the fix.
10. **Unless it does not need a session at all.** An issue that is
    [express](#express) and stands in a column marked
    [`ExpressSkips`](#status) is [a hop](#the-hop): the ninth condition's
    absence is answered not by a playbook but by the pass carrying the issue on
    itself, with `POST /api/hatch/work/{key}/hop`. Every condition above this
    one still has to hold — a hop is not an escape from a live claim, a ready
    date, an assignee, a question, a repository or a dependency, only from
    needing a playbook.

Seven of them — 1, 2, 5, 6, 7, 8 and 9 — are facts about the issue, and `work/{key}`
asks them too. The tenth is as well, and `work/{key}` answers it the same way
`work/queue` does: `WorkDto.Hop`. The other two are the loop's policy and are
asked only when the pass is asking; see [one more, on `next`
alone](#one-more-on-next-alone).

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
   would be a fetch spent on an increment that never happens. Two things are
   done at the spawn so that a message to the agent is heard:
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
  unmerged for ever. The prompt then says the old branch merged and names a
  branch to cut that is not on `origin` (or here).
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
  cut, `<key-lowercased>-<slug of the title>`.
- **The copy on this machine is only a copy.** Behind `origin`, it is
  fast-forwarded. Ahead, it is used as it is. Diverged, the local tip is kept
  under `<branch>-local-<short sha>`, said on the terminal and in the prompt, and
  the branch goes to `origin`'s. Nothing that exists only here is thrown away
  silently.
- **Every checkout that has a branch for the key** is treated this way; the others
  stay on the trunk.

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
  skipped.
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
is a stash that would not go. The runner never pushes to the trunk.

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
   of its issues moved — and then asks `git merge-tree` for each moved issue and
   puts the verdict to the board. The tree is never touched: `ls-remote` and
   `fetch` move no branch a worktree stands on, and `merge-tree --write-tree`
   writes objects only.

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
one — and puts a [trunk build](#trunk-build) to the board. It follows the same
rules asking `gh` again: `passed` and `failed` are asked once, `pending` every
interval, `none` until ten minutes after `ShaSince`, and a forge or a board
that cannot answer is one line per checkout. `GET /api/hatch/trunk-builds`
answers every stored trunk verdict once a poll, and the runner matches its own
checkout against it by the remote it spells until the board's canonical form
for that pair is known. The terminal hears about a change the same way —
`hatch: main build on 1a2b3c4 failed (api, CI)` — naming the trunk rather than
an issue.

### When an increment does nothing

The one failure mode of an unattended loop that is dangerous rather than merely
disappointing: a session ends with the ticket in the column it found it in.
Nothing about the board changed, so the next pass picks the same issue, spends
the same money, and fails the same way — all night. Every other way an increment
can go badly costs one increment.

So a stall is written on the ticket: a comment naming the session that ran and
the transition it was trying to make — with the `claude --resume` command, since
resuming the conversation is most of why a stall is worth recording rather than
merely counting — and, if nothing is already open there, **a question**.

**A hop is never a stall.** No session ran, so there is no session to name and
nothing was spent on a ticket that did not move - the ticket did move, one
column, and the runner never held a claim to have written anything on it. See
[the hop](#the-hop).

A question rather than a **flag field**, which was the obvious alternative and
would have had to be taught three things a question already does: it blocks the
issue from being dispatched again, it badges the card on the board, and it is
the list `hatch answer` walks. Answering it clears the flag, which is the
right gesture, because the flag means "nobody has looked at this" and answering
is somebody having looked. A field would have been a fourth thing on the issue
row that only the loop writes and only the loop reads, and a second flag the
loop could read is one step from a flag the loop could set.

Neither of its options is recommended, and that is not modesty: `--recommend` is
for a choice something knows the answer to, and the whole content of a stall is
that nothing here knows why it happened. A ticket that is already waiting on a
question gets the comment and no second question — that question *is* the flag,
usually raised by the session's own way out.

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
  the same question, but the comment says the branch still conflicts with the
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
  new tip *marked as a build increment's*, which is what lets the board open the
  failed-again question if that build fails. One repository moved of two is
  progress. **A tip that did not move is a stall**, flagged like any other with
  the same question, and the comment says the build still fails and names the
  checks. A tip that could not be read is *not known*, and flagged as such.
- **A build that fails on the tip an increment pushed is not sent to another
  agent.** The board opens a question naming the checks that still fail, with the
  stall guard's `leave it` and `try again` — so a build nobody can fix costs one
  increment and then a question, not a night. A failure on a tip somebody else
  pushed is new work, as it is today. A `leave it` answer does not stop the loop:
  once it is answered the issue is dispatchable again while its build still
  fails on that sha, exactly as after the stall guard's question — the option's
  own text says the answer is how to make the loop stop.

None of it runs when the lease was lost, for the reason the conflict's does not.

### Where the loop's rules live

On the server. `work/queue` is the same walk `work/next` takes, reported rather
than acted on, and `GetNextWork` is the first clear row of that scan rather
than a second walk that happens to agree with it — so a runner asks two
questions and cannot get two different boards. Everything
above is decided in one place, in one order, in
[`WorkController.cs`](../src/Hatch.Api/Modules/Hatch/WorkController.cs).

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
`queue`, `show`, `start`, `move`, `comment`, `pr`, `depends`, `ask`,
`questions`, `answer`, `api`, `config`, `work` and `go-to-work`.

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

The other fourteen followed, and not because shell was the wrong language for
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
- `--max-spend USD` — measured from what the increments reported, not estimated.
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
  money to prove it.
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
answer** is a minute of bad network, and a loop that ended on one is a loop
somebody has to sit with. And **a busy board** is a wait, not an ending: every
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
in `src/Hatch.Cli/runner-names.txt` (main and recurring characters from five
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

**The usage bars read the session's own stream, not Hatch's battery** — asked
and answered on HA-120: every session's `stream-json` carries a
`rate_limit_event` naming `unifiedWindows.five_hour` (labelled `Session`) and
`seven_day` (`Weekly`), plus a per-model weekly window for accounts that have
one, labelled `Weekly (model)` since it arrives with no name of its own. That is
the account this runner's sessions actually spend — right for a friend's
`do-my-work` on their own subscription, and it needs no token pasted into this
Hatch's Settings page. The reading updates while a session streams and holds
its last value between increments; a quiet run, or one that ends before the
first such event, has none, and the readout and the closing banner alike simply
draw no usage line rather than complain.

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
`waiting` is the exception and deliberately: a question is waiting on a person
wherever its issue happens to stand, which is the rule the attention panel
states, and a count that quietly dropped would be a question nobody ever
answers.

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
`queue`, `show`, `start`, `move`, `comment`, `pr`, `depends`, `ask`,
`questions`, `answer`, `config`, `work`, `go-to-work`, `do-my-work`, and `api`
for everything else. It finds a column by name rather than by id — on the
letters and digits alone, so `todo` at a terminal reaches the column the board
calls `To Do` — and folds off cards whose ready date has not arrived, exactly
as the board does. `hatch --help` lists the surface and every subcommand takes
`-h` for its own.

Only `work`, `go-to-work` and `do-my-work` need to be run inside a git
checkout, because only those three are about a codebase. The other fourteen
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
is clear for, in the dispatcher's order: every [expedited](#expedite) row first
whatever column it sits in, then the rest, and inside each half the rightmost
column first and the order the board itself draws that column in. An expedited
row is marked, so a queue reordered by one says why. A clear row that is a
[hop](#the-hop) reads `-> <column>  (express, no session)` in place of the bare
arrow, so it reads differently from a row `go-to-work` would spawn a session
for even though both print no reason to fold past. `hatch queue AER-1`
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
  it as something to click.
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
  transition into a terminal column outright.
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
- **Reporting.** The events are there; nothing renders them. The Plan view
  answers the question that was actually being asked.
- **GitHub integration, beyond one read.** A branch and a PR are named in a
  comment by whoever made them. The one thing read from a forge is the build on
  an in-review branch's tip, through the runner's own `gh`
  ([checking the branches in review](#checking-the-branches-in-review)). Review
  state, mergeability, webhooks and anything the server would ask a forge itself
  are deferred: each is a credential on the server, which is what the runner
  asking `gh` as whoever it is signed in as avoids.
- **A deleted issue takes its events with it.** Hard delete, confirmed in the
  UI, and an accepted gap.
- **Pushed updates.** The board polls (see above); a server-sent signal would
  only make the same refresh arrive sooner.

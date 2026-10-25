using System.Globalization;
using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The runners, and the one way the board reaches one: it writes down what it
/// would like, and the runner asks for it between increments.
///
/// Nothing here starts or stops a process. A <c>PATCH</c> setting
/// <c>stopping</c> is a row in a table until the loop that row names heartbeats
/// again - which it does at the top of its next pass, after whatever increment
/// was in flight has finished - and then it is the loop that exits. That is
/// what makes this work for every runner, in a container or on somebody's
/// laptop behind a router nothing can reach.
/// </summary>
/// <remarks>
/// <para>Its own controller for the attribute it does not carry at the class
/// level. The heartbeat and the read are a key's, exactly like the claim: a
/// dispatcher that could not say it was alive would leave a page that could
/// only ever be empty. The <c>PATCH</c> is a person's, for the reason
/// <see cref="IssuePlaybookController"/> and <see cref="AssigneeController"/>
/// are: an agent that could raise its own <c>--max-spend</c> could raise its
/// own budget, and a loop with no end is exactly what the bounds exist to
/// prevent.</para>
///
/// <para>There is no <c>DELETE</c>. A row ages out of the read on its own - see
/// <see cref="Runners"/> - and a deregister verb would be a second way for a
/// runner to disappear, one of which an operator could press while the process
/// was still running.</para>
/// </remarks>
[ApiController]
[Route("api/hatch/runners")]
public class RunnersController(
    HatchContext db, Runners runners, IssueClaims claims, ICallerIdentity caller, IActorDirectory actors,
    TimeProvider time, IOptions<AuthOptions> auth) : ControllerBase
{
    /// <summary>
    /// Every runner that has spoken to this Hatch lately, most recently heard
    /// from first.
    /// </summary>
    /// <remarks>
    /// Two reads and no join. The claims are every issue holding one - tens of
    /// cards on the busiest board this is for - matched to runners in memory,
    /// because the thing being matched on is a name a checkout computed and not
    /// a key anybody indexed.
    ///
    /// <para>Rows outside <see cref="VisibleAsync"/>'s reach are dropped from
    /// the same table <see cref="Heartbeat"/> writes <c>ForPersonId</c> to,
    /// through <see cref="IActorDirectory.PrincipalAsync"/> - the same
    /// resolution, so "whose runner is this" and "who may see it" cannot
    /// disagree.</para>
    /// </remarks>
    [HttpGet]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<IReadOnlyList<RunnerDto>>> GetRunners(CancellationToken ct)
    {
        var now = time.GetUtcNow();

        // The drop horizon in the WHERE, so a runner nobody has heard from
        // since last night is not something a client has to know to filter out.
        // The row stays where it is: one that comes back under the same name
        // finds its own bounds still on it.
        var dropBefore = runners.DropBefore(now);

        var (all, personId) = await VisibleAsync(ct);

        var query = db.Runners.AsNoTracking().Where(r => r.LastSeenAt >= dropBefore);
        if (!all)
        {
            query = personId is { } id ? query.Where(r => r.ForPersonId == id) : query.Where(r => r.ForPersonId == null);
        }

        var rows = await query
            .OrderByDescending(r => r.LastSeenAt)
            .ToListAsync(ct);

        var held = await runners.HeldAsync(db, claims, now, ct);
        var drivers = await runners.DriversAsync(db, now, ct);

        var wallEnabled = auth.Value.Enabled;
        var callerPerson = wallEnabled ? await caller.PersonAsync(ct) : null;

        var dtos = new List<RunnerDto>(rows.Count);
        foreach (var r in rows)
        {
            var forName = r.ForPersonId is { } forId ? (await actors.ResolveAsync(ActorKind.Person, forId, ct))?.Name : null;
            var canControl = CanControl(r.ForPersonId, wallEnabled, callerPerson);
            var order = r.OrderKind is { Length: > 0 } ok ? new RunnerOrderDto(ok, r.OrderKey, r.OrderedBy, r.OrderedAt) : null;
            dtos.Add(runners.Project(r, Holding(held, r.Name), drivers.GetValueOrDefault(r.Name, []), forName, canControl, order));
        }

        return dtos;
    }

    /// <summary>
    /// The name of every runner heard from lately, most recently heard from
    /// first, and nothing else.
    /// </summary>
    /// <remarks>
    /// Open to every key the <c>hatch</c> scope admits, and the same list
    /// whoever asks: a name is already public - every <c>claim_taken</c> event
    /// carries it - while where a runner is, whom it is for, what it holds and
    /// what its line says are what stays inside <see cref="GetRunners"/>'s
    /// scope. So there is deliberately nothing here to scope, and nothing of
    /// the caller is read.
    /// </remarks>
    [HttpGet("names")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetRunnerNames(CancellationToken ct)
    {
        var dropBefore = runners.DropBefore(time.GetUtcNow());

        return await db.Runners.AsNoTracking()
            .Where(r => r.LastSeenAt >= dropBefore)
            .OrderByDescending(r => r.LastSeenAt)
            .Select(r => r.Name)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Still here - and what should I do next. The only call a runner makes
    /// about itself, and the whole of the round trip: it says what it is, and
    /// reads back what the board would like.
    /// </summary>
    /// <remarks>
    /// <para>The five bounds in the body are what this process started with,
    /// and they are written <em>when the row is created</em> and by a
    /// <c>loop</c> beat that says it is <c>Fresh</c> - never by any other beat.
    /// That is the one subtle thing in this file: a loop restarting overnight
    /// sends its original <c>--max-runs</c> on every incarnation's first
    /// heartbeat, but not as <c>Fresh</c>, because a row that took them would
    /// quietly undo the edit an operator made at midnight.</para>
    ///
    /// <para>The state is the operator's too, with one exception: a <c>loop</c>
    /// beat that is <c>Fresh</c> and says <c>Standby</c> leaves the row
    /// <c>paused</c> - new, <c>running</c> or <c>stopping</c> - and is answered
    /// <c>paused</c> on that same beat. <c>Standby</c> without <c>Fresh</c> (a
    /// supervisor restart) writes nothing.</para>
    ///
    /// <para>Nothing here is refused for being unreasonable. A heartbeat is not
    /// a decision, and a night that ended because a runner sent a line somebody
    /// would call too long would be the wrong trade twice over - so the line is
    /// truncated, a bound that makes no sense is dropped, and only a nameless
    /// runner is turned away.</para>
    /// </remarks>
    [HttpPost("{name}")]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<RunnerInstructionDto>> Heartbeat(
        string name, RunnerHeartbeatRequest request, CancellationToken ct)
    {
        if (Named(name) is not { } runner) return BadRequest(NameRefusal);
        if (RunnerTags.Normalise(request.Tags, out var normalisedTags) is { } tagsRefusal) return BadRequest(tagsRefusal);

        var kind = request.Kind?.Trim().ToLowerInvariant() switch
        {
            EfHatchRunner.OnceKind => EfHatchRunner.OnceKind,
            // A heartbeat that did not say reads as a loop. The kind decides
            // whether a page draws controls at all, and a loop drawn without
            // them is the feature missing; a single increment drawn with them
            // is two buttons nothing will ever read.
            _ => EfHatchRunner.LoopKind,
        };

        var now = time.GetUtcNow();
        var line = Runners.Normalise(request.Line);

        var row = await db.Runners.FirstOrDefaultAsync(r => r.Name == runner, ct);

        if (row is not null && request.Where is { Length: > 0 } where && row.Where is { Length: > 0 } existing &&
            !string.Equals(existing, where, StringComparison.Ordinal) && runners.IsHere(row.LastSeenAt, now))
        {
            // The one heartbeat answer allowed to end a run: two live runners
            // sharing a name would merge into one row, and the operator's
            // Pause would pause both. A gone row falls through instead and is
            // taken over below, which is how a checkout that moved keeps its
            // name.
            return Conflict($"{runner} is already the runner on {existing} - hatch config gives this checkout another name");
        }

        var forActor = await ForAsync(ct);
        var forName = forActor?.Name ?? (await caller.ApiKeyAsync(ct))?.Name;

        if (row is null)
        {
            db.Runners.Add(row = Seed(runner, kind, line, request, now, forActor?.Id, Joined(normalisedTags)));

            try
            {
                await db.SaveChangesAsync(ct);

                // A row born on this very beat has never been ordered.
                return Runners.Instruct(row, forName, (await runners.DriversAsync(db, now, ct)).GetValueOrDefault(row.Name, []), null);
            }
            catch (DbUpdateException)
            {
                // Two processes in one checkout - a loop and a `hatch work`
                // beside it - can both find no row and both insert one. The
                // second loses on the primary key and becomes what it should
                // have been, which is an update: it does not re-seed, so the
                // bounds on the row are the ones that got there first, unless
                // it is a fresh beat.
                db.Entry(row).State = EntityState.Detached;
                row = await db.Runners.FirstOrDefaultAsync(r => r.Name == runner, ct);
                if (row is null) throw;
            }
        }

        // Read-and-clear, and only on a loop beat: Touch below overwrites
        // row.Kind, and a `hatch work` (once) beat beside a loop shares this
        // runner's name - letting it through here would swallow an order the
        // loop has not had its next pass to read yet.
        RunnerOrderDto? order = null;
        if (kind == EfHatchRunner.LoopKind && row.OrderKind is { Length: > 0 } ok)
        {
            order = new RunnerOrderDto(ok, row.OrderKey, row.OrderedBy);
            row.OrderKind = null;
            row.OrderKey = null;
            row.OrderedBy = null;
            row.OrderedAt = null;
        }

        Touch(row, kind, line, now, request, forActor?.Id, Joined(normalisedTags));
        await db.SaveChangesAsync(ct);

        return Runners.Instruct(row, forName, (await runners.DriversAsync(db, now, ct)).GetValueOrDefault(row.Name, []), order);
    }

    /// <summary>
    /// Who this heartbeat's caller works for - the same resolution a
    /// <c>--mine</c> dispatch pass takes, so "whose runner is this" and "whose
    /// tickets does <c>--mine</c> reach" can never disagree, and so the id and
    /// the name this heartbeat writes come from one resolution rather than two
    /// that could fall out of step. Null is ordinary: a key that belongs to
    /// nobody keeps beating, and <see cref="EfHatchRunner.ForPersonId"/> is
    /// null on its row.
    /// </summary>
    private async Task<Actor?> ForAsync(CancellationToken ct) => await actors.PrincipalAsync(ct);

    /// <summary>
    /// Whether <see cref="GetRunners"/> may answer with every row, or only the
    /// ones <c>ForPersonId</c> ties to this caller: the wall off, a person
    /// (cookie) calling, or a key whose owner is an admin all see everything;
    /// anyone else sees only their own.
    /// </summary>
    /// <remarks>
    /// A positive "is a person" test, not <c>!IsProgramAsync</c>, so a caller
    /// the wall somehow let through with no identity at all falls to the
    /// narrow arm, never the wide one. <c>PersonAsync</c> and
    /// <c>ApiKeyAsync</c> are memoized on <see cref="ICallerIdentity"/>, so
    /// asking again here costs no extra query even though <see cref="GetRunners"/>
    /// already reads <c>PersonAsync</c> for its own <c>callerPerson</c>.
    /// </remarks>
    private async Task<(bool All, Guid? PersonId)> VisibleAsync(CancellationToken ct)
    {
        if (!auth.Value.Enabled) return (true, null);
        if (await caller.PersonAsync(ct) is not null) return (true, null);
        if (await caller.ApiKeyAsync(ct) is { Owner.Role: >= PersonRole.Admin }) return (true, null);

        return (false, (await actors.PrincipalAsync(ct))?.Id);
    }

    /// <summary>
    /// The windows off a non-empty reading, as JSON, percent clamped 0..100 -
    /// it is drawn as a fill, and a fill is a fraction of something. Null where
    /// the request carries none, so the caller can tell "nothing to write" from
    /// "write this".
    /// </summary>
    private static string? UsageJson(RunnerHeartbeatRequest request) =>
        request.Usage is not { Count: > 0 } windows
            ? null
            : JsonSerializer.Serialize(windows.Select(w => w with { Percent = Math.Clamp(w.Percent, 0, 100) }));

    /// <summary>
    /// Keep going, pause, stop after this one - and the bounds that are flags
    /// at a terminal.
    /// </summary>
    /// <remarks>
    /// The order of the body is the guarantee that a refused request writes
    /// nothing: every present field is parsed and judged before the row is
    /// touched, so a request naming a good state and a nonsense budget changes
    /// neither.
    ///
    /// <para>It creates nothing. A name no runner has ever used is a <c>404</c>
    /// rather than a row waiting for a process that may never exist - the table
    /// is a record of what has spoken to this Hatch, and an operator typing a
    /// hostname into a URL is not a runner.</para>
    /// </remarks>
    [HttpPatch("{name}")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<RunnerDto>> PatchRunner(
        string name, RunnerPatchRequest request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;
        if (Named(name) is not { } runner) return BadRequest(NameRefusal);

        var row = await db.Runners.FirstOrDefaultAsync(r => r.Name == runner, ct);
        if (row is null) return NotFound();

        var wallEnabled = auth.Value.Enabled;
        var callerPerson = wallEnabled ? await caller.PersonAsync(ct) : null;
        if (!CanControl(row.ForPersonId, wallEnabled, callerPerson))
        {
            var forName = row.ForPersonId is { } forId
                ? (await actors.ResolveAsync(ActorKind.Person, forId, ct))?.Name ?? "its owner"
                : "its owner";
            return new ObjectResult($"only {forName}, or an admin, may control {runner}")
                { StatusCode = StatusCodes.Status403Forbidden };
        }

        // Absent leaves a field alone, "" clears it, a value sets it - the
        // convention the two dates on an issue already established, and the
        // reason the numbers are strings on the wire: a nullable number can say
        // "leave it" or "set it", but not "take the cap off".
        string? state = null;
        if (request.State is not null)
        {
            state = request.State.Trim().ToLowerInvariant();
            if (!EfHatchRunner.States.Contains(state))
                return BadRequest($"a runner is {string.Join(", ", EfHatchRunner.States)} - not \"{request.State.Trim()}\"");
        }

        string? under = null;
        if (request.Under is { } wanted && wanted.Trim().Length > 0)
        {
            under = wanted.Trim().ToUpperInvariant();
            if (!IssueKey.TryParse(under, out _, out _))
                return BadRequest($"--under names an issue, like AER-12 - not \"{wanted.Trim()}\"");
        }

        if (Number(request.MaxRuns, out var maxRuns) is { } runsError) return BadRequest(runsError);
        if (Money(request.MaxSpend, out var maxSpend) is { } spendError) return BadRequest(spendError);
        if (RunnerTags.Normalise(request.Tags, out var normalisedTags) is { } tagsRefusal) return BadRequest(tagsRefusal);

        // A key is judged here, on the one write a person makes - a heartbeat
        // is not where a project is checked. Before any write below, so a
        // refused request changes nothing.
        var wantedProjects = request.Projects is null ? null : ProjectKeys(request.Projects);
        if (wantedProjects is { Count: > 0 })
        {
            var known = await db.Projects.Select(p => p.Key).ToListAsync(ct);
            var unknown = wantedProjects.Where(k => !known.Contains(k)).ToList();
            if (unknown.Count > 0) return BadRequest($"there is no project {string.Join(", ", unknown)}");
        }

        DateTimeOffset? untilAt = null;
        if (request.UntilAt is { } until && until.Trim().Length > 0)
        {
            // An instant, and the same parser a ready date is read with. A stop
            // hour is never a bare calendar date - "stop by the 12th" would be
            // midnight in a timezone nobody named - so the date-only form is
            // refused rather than pinned to one.
            if (!IssueMoment.TryParse(until, out var moment) || !moment.HasTime)
                return BadRequest("--until takes an instant, like 2026-09-12T17:00:00Z");

            untilAt = moment.At;
        }

        if (state is not null) row.State = state;
        if (request.Under is not null) row.Under = under;
        if (request.MaxRuns is not null) row.MaxRuns = maxRuns;
        if (request.MaxSpend is not null) row.MaxSpend = maxSpend;
        if (request.UntilAt is not null) row.UntilAt = untilAt;

        // "" clears the board, never null - the whole of how PATCH's clear is
        // told apart from a row no heartbeat has seeded yet, with no migration.
        if (request.Tags is not null) row.Tags = normalisedTags.Count > 0 ? string.Join('\n', normalisedTags) : "";

        // "" clears it, never null, the same as Tags above.
        if (wantedProjects is not null) row.Projects = wantedProjects.Count > 0 ? string.Join('\n', wantedProjects) : "";

        await db.SaveChangesAsync(ct);

        var now = time.GetUtcNow();
        var ownerName = row.ForPersonId is { } ownerId ? (await actors.ResolveAsync(ActorKind.Person, ownerId, ct))?.Name : null;
        var order = row.OrderKind is { Length: > 0 } ok ? new RunnerOrderDto(ok, row.OrderKey, row.OrderedBy, row.OrderedAt) : null;
        return runners.Project(
            row,
            Holding(await runners.HeldAsync(db, claims, now, ct), row.Name),
            (await runners.DriversAsync(db, now, ct)).GetValueOrDefault(row.Name, []),
            ownerName,
            CanControl(row.ForPersonId, wallEnabled, callerPerson),
            order);
    }

    /// <summary>
    /// Order this runner to work a named ticket, or to pass on its next pick -
    /// a one-shot instruction its next loop heartbeat reads and clears. See
    /// <see cref="EfHatchRunner.OrderKind"/>.
    /// </summary>
    /// <remarks>
    /// Refused ahead of a row that will never read it back - a <c>once</c>
    /// runner, or one already told to stop - and ahead of a standing order
    /// already held, which a second order would silently overwrite rather
    /// than queue.
    /// </remarks>
    [HttpPost("{name}/order")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<RunnerDto>> PostRunnerOrder(
        string name, RunnerOrderRequest request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;
        if (Named(name) is not { } runner) return BadRequest(NameRefusal);

        var row = await db.Runners.FirstOrDefaultAsync(r => r.Name == runner, ct);
        if (row is null) return NotFound();

        var wallEnabled = auth.Value.Enabled;
        var callerPerson = wallEnabled ? await caller.PersonAsync(ct) : null;
        if (!CanControl(row.ForPersonId, wallEnabled, callerPerson))
        {
            var forName = row.ForPersonId is { } forId
                ? (await actors.ResolveAsync(ActorKind.Person, forId, ct))?.Name ?? "its owner"
                : "its owner";
            return new ObjectResult($"only {forName}, or an admin, may control {runner}")
                { StatusCode = StatusCodes.Status403Forbidden };
        }

        var kind = request.Kind?.Trim().ToLowerInvariant();
        if (kind is null || !RunnerOrderKinds.All.Contains(kind))
            return BadRequest($"an order is {string.Join(", ", RunnerOrderKinds.All)} - not \"{request.Kind?.Trim()}\"");

        EfHatchIssue? issue = null;
        string? key = null;
        if (kind == RunnerOrderKinds.Work)
        {
            if (request.Key is not { } wanted || wanted.Trim().Length == 0 || !IssueKey.TryParse(wanted, out var projectKey, out var number))
                return BadRequest($"work names an issue, like AER-12 - not \"{request.Key?.Trim()}\"");

            key = IssueKey.Format(projectKey, number);
            issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
            if (issue is null) return NotFound($"{key} does not exist");
        }

        if (row.Kind == EfHatchRunner.OnceKind)
            return Conflict($"{runner} is a single increment and will never read an order back");

        if (row.State == EfHatchRunner.Stopping)
            return Conflict($"{runner} is stopping and will not take another order");

        if (row.OrderKind is { Length: > 0 } heldKind)
        {
            var held = heldKind == RunnerOrderKinds.Work ? $"{heldKind} {row.OrderKey}" : heldKind;
            return Conflict($"{runner} already holds an order to {held} - withdraw it first");
        }

        var now = time.GetUtcNow();
        var by = await caller.ActorNameAsync(ct);

        row.OrderKind = kind;
        row.OrderKey = key;
        row.OrderedBy = by;
        row.OrderedAt = now;

        if (issue is not null)
        {
            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = by,
                Kind = EfHatchIssueEvent.Ordered,
                Payload = JsonSerializer.Serialize(new { runner, by }),
                At = now,
            });
        }

        await db.SaveChangesAsync(ct);

        var ownerName = row.ForPersonId is { } ownerId ? (await actors.ResolveAsync(ActorKind.Person, ownerId, ct))?.Name : null;
        return runners.Project(
            row,
            Holding(await runners.HeldAsync(db, claims, now, ct), row.Name),
            (await runners.DriversAsync(db, now, ct)).GetValueOrDefault(row.Name, []),
            ownerName,
            CanControl(row.ForPersonId, wallEnabled, callerPerson),
            new RunnerOrderDto(kind, key, by, now));
    }

    /// <summary>Withdraws whatever standing order this runner holds - see <see cref="PostRunnerOrder"/>.</summary>
    [HttpDelete("{name}/order")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult> DeleteRunnerOrder(string name, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;
        if (Named(name) is not { } runner) return BadRequest(NameRefusal);

        var row = await db.Runners.FirstOrDefaultAsync(r => r.Name == runner, ct);
        if (row is null) return NotFound();

        var wallEnabled = auth.Value.Enabled;
        var callerPerson = wallEnabled ? await caller.PersonAsync(ct) : null;
        if (!CanControl(row.ForPersonId, wallEnabled, callerPerson))
        {
            var forName = row.ForPersonId is { } forId
                ? (await actors.ResolveAsync(ActorKind.Person, forId, ct))?.Name ?? "its owner"
                : "its owner";
            return new ObjectResult($"only {forName}, or an admin, may control {runner}")
                { StatusCode = StatusCodes.Status403Forbidden };
        }

        if (row.OrderKind is null) return NoContent();

        row.OrderKind = null;
        row.OrderKey = null;
        row.OrderedBy = null;
        row.OrderedAt = null;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ---- The one narrowing ----

    /// <summary>
    /// A person, not a key. Every field this route writes is a bound on how much
    /// a runner may spend and how long it may run, so an agent that could reach
    /// it could raise its own budget - which is the failure the bounds exist to
    /// prevent, reintroduced through the thing that reads them.
    /// </summary>
    /// <remarks>
    /// Checked in the action as well as declared in the attribute, for
    /// <c>IssueClaimController.NotAPerson</c>'s reason: <c>RoleGate</c> is
    /// dormant wherever the wall is off, which is all of local
    /// development, and a guarantee that evaporates under a switch is not a
    /// guarantee. A keyless runner that named itself with the runner header is
    /// refused here too - it is an agent whether or not it carries a
    /// credential.
    /// </remarks>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("what a runner may spend is the operator's to set, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;

    /// <summary>
    /// Whether this caller may press this runner's controls: the wall off, the
    /// runner belonging to nobody, the caller being the owner, or the caller
    /// being an admin. Written so the order route (HA-1359) can call it once it
    /// lands in this same controller.
    /// </summary>
    private static bool CanControl(Guid? forPersonId, bool wallEnabled, EfPerson? callerPerson) =>
        !wallEnabled || forPersonId is not { } forId
        || (callerPerson is not null && (callerPerson.Id == forId || callerPerson.Role >= PersonRole.Admin));

    // ---- The name ----

    private static readonly string NameRefusal =
        $"a runner names itself, as host:/path/to/checkout, in at most {EfHatchRunner.MaxNameLength} characters";

    /// <summary>
    /// The runner a route segment names, or null where it names nothing usable.
    /// </summary>
    /// <remarks>
    /// Unescaped once, because a runner name has slashes in it and Kestrel
    /// leaves <c>%2F</c> encoded in the path on purpose - decoding it would
    /// change how many segments a URL has. Everything else in the segment
    /// arrived decoded already, and unescaping a string with no percent left in
    /// it does nothing, so this is one call rather than a special case.
    /// </remarks>
    private static string? Named(string name)
    {
        var runner = Uri.UnescapeDataString(name ?? "").Trim();
        return runner.Length is > 0 and <= EfHatchRunner.MaxNameLength ? runner : null;
    }

    // ---- The rows ----

    /// <summary>
    /// The five bounds, from what the flags said, so the row shows the bounds
    /// it is actually running under rather than reading as unbounded until
    /// somebody sets something. One place for a new row and a fresh beat, so
    /// the two cannot drift.
    /// </summary>
    private static void Bound(EfHatchRunner row, RunnerHeartbeatRequest request)
    {
        var keys = ProjectKeys(request.Projects);
        row.Projects = keys.Count > 0 ? string.Join('\n', keys) : null;
        row.Under = Fits(request.Under, EfHatchRunner.MaxUnderLength);
        row.MaxRuns = request.MaxRuns is > 0 ? request.MaxRuns : null;
        row.MaxSpend = request.MaxSpend is > 0 ? request.MaxSpend : null;
        row.UntilAt = request.UntilAt;
    }

    /// <summary>A row that has never been seen before, seeded with what the process started with.</summary>
    private static EfHatchRunner Seed(
        string name, string kind, string? line, RunnerHeartbeatRequest request, DateTimeOffset now,
        Guid? forPersonId, string? tags)
    {
        var row = new EfHatchRunner
        {
            Name = name,
            Kind = kind,
            FirstSeenAt = now,
            LastSeenAt = now,
            Line = line is { Length: > 0 } ? line : null,
            LineAt = line is { Length: > 0 } ? now : null,
            State = kind == EfHatchRunner.LoopKind && request.Standby == true
                ? EfHatchRunner.Paused
                : EfHatchRunner.Running,

            // Facts about the process, not bounds an operator set - written from
            // whatever the first beat carried, the same as every later one.
            Remotes = Canonicalised(request.Remotes),
            Clones = request.Clones,
            Mine = request.Mine,
            Standing = request.Standing,
            Where = Fits(request.Where, EfHatchRunner.MaxNameLength),
            Agent = Fits(request.Agent, EfHatchRunner.MaxAgentLength),
            Revision = Fits(request.Revision, EfHatchRunner.MaxRevisionLength),
            Sequence = request.Sequence,
            SourceRevision = Fits(request.SourceRevision, EfHatchRunner.MaxRevisionLength),
            SourceSequence = request.SourceSequence,
            BuiltAt = request.BuiltAt,
            Tags = tags,
            ExhaustedUntil = request.Exhausted == true ? request.ExhaustedUntil : null,
            ForPersonId = forPersonId,
            Usage = UsageJson(request),
            UsageReadAt = request.UsageReadAt,
        };

        Bound(row, request);
        return row;
    }

    /// <summary>
    /// A row that already exists: kind, last seen, line, and the two facts
    /// about what it serves. The state is the operator's, with one exception: a
    /// fresh <c>loop</c> beat that says <c>Standby</c> writes it <c>paused</c>.
    /// The five bounds are the flags' at a fresh start - a <c>loop</c> beat that
    /// says so - and the operator's after: any other beat writing one would be
    /// the runner having the last word on what it may spend.
    /// </summary>
    private static void Touch(
        EfHatchRunner row, string kind, string? line, DateTimeOffset now, RunnerHeartbeatRequest request,
        Guid? forPersonId, string? tags)
    {
        row.Kind = kind;
        row.LastSeenAt = now;

        switch (line)
        {
            case null: break;
            case "":
                row.Line = null;
                row.LineAt = null;
                break;
            default:
                row.Line = line;
                row.LineAt = now;
                break;
        }

        // Facts about the process, overwritten by whatever the latest beat
        // said - absent leaves the row exactly as it was, the same tri-state
        // every other fact-vs-absent field on this contract already uses.
        if (request.Remotes is not null) row.Remotes = Canonicalised(request.Remotes);
        if (request.Clones is not null) row.Clones = request.Clones;
        if (request.Mine is not null) row.Mine = request.Mine;
        if (request.Standing is not null) row.Standing = request.Standing;
        if (request.Where is not null) row.Where = Fits(request.Where, EfHatchRunner.MaxNameLength);
        if (request.Agent is not null) row.Agent = Fits(request.Agent, EfHatchRunner.MaxAgentLength);
        if (request.Revision is not null) row.Revision = Fits(request.Revision, EfHatchRunner.MaxRevisionLength);
        if (request.Sequence is not null) row.Sequence = request.Sequence;
        if (request.SourceRevision is not null) row.SourceRevision = Fits(request.SourceRevision, EfHatchRunner.MaxRevisionLength);
        if (request.SourceSequence is not null) row.SourceSequence = request.SourceSequence;
        if (request.BuiltAt is not null) row.BuiltAt = request.BuiltAt;

        // Tags are seeded once, not a fact-vs-absent field like the rest of
        // this block: a row with any Tags already written - including the
        // empty string PATCH stores to clear the board - is never touched by
        // a later heartbeat again. Only a row still null, from a beat before
        // this one shipped, takes a heartbeat's tags at all.
        if (request.Tags is not null && row.Tags is null) row.Tags = tags;

        // A tri-state of its own: absent leaves the row's own record alone (an
        // older CLI, or hatch work's single beat, neither of which knows
        // anything about the account it ran under), true sets it, false clears
        // it at once - the one way a loop overrides its own past heartbeat.
        if (request.Exhausted == true) row.ExhaustedUntil = request.ExhaustedUntil;
        else if (request.Exhausted == false) row.ExhaustedUntil = null;

        // Whose key this is can change on the API Keys page, so this is
        // written every beat like Where and Remotes, unlike the five bounds,
        // which only a fresh loop's first beat writes. Not a `once` beat:
        // `hatch work` shares a runner name with a loop in the same checkout,
        // and its flagless beat must not clear the loop's budget.
        row.ForPersonId = forPersonId;

        if (request.Fresh == true && kind == EfHatchRunner.LoopKind)
        {
            Bound(row, request);

            // The one beat that writes state: a start that asked to stand by
            // is paused on the beat that says so, from any state, so its own
            // answer is already `paused`.
            if (request.Standby == true) row.State = EfHatchRunner.Paused;
        }

        // A non-empty reading writes; absent or empty leaves the row's own
        // reading alone - see the remark on RunnerHeartbeatRequest.Usage.
        if (UsageJson(request) is { } usage)
        {
            row.Usage = usage;
            row.UsageReadAt = request.UsageReadAt;
        }
    }

    /// <summary>
    /// What this runner holds, or null. Written out rather than reached for
    /// with <c>GetValueOrDefault</c>, which for a tuple hands back a pair of
    /// nulls that is not itself null - a claim on nothing, drawn as though it
    /// were one.
    /// </summary>
    private static (string Key, ClaimSnapshot Claim)? Holding(
        Dictionary<string, (string Key, ClaimSnapshot Claim)> held, string name) =>
        held.TryGetValue(name, out var claim) ? claim : null;

    // ---- The two numbers ----

    /// <summary>A count, or the sentence refusing one. Absent and <c>""</c> both come back null, and the caller tells them apart.</summary>
    private static string? Number(string? sent, out int? count)
    {
        count = null;
        if (sent is null || sent.Trim().Length == 0) return null;

        if (!int.TryParse(sent.Trim(), CultureInfo.InvariantCulture, out var runs) || runs < 0)
            return $"--max-runs takes a count - not \"{sent.Trim()}\"";

        count = runs;
        return null;
    }

    /// <summary>The same, in dollars.</summary>
    private static string? Money(string? sent, out decimal? amount)
    {
        amount = null;
        if (sent is null || sent.Trim().Length == 0) return null;

        if (!decimal.TryParse(sent.Trim(), CultureInfo.InvariantCulture, out var spend) || spend < 0)
            return $"--max-spend takes an amount in dollars - not \"{sent.Trim()}\"";

        // Two places, which is what the column holds. Rounded rather than
        // refused: an operator typing a third digit meant the amount, not a
        // lesson about scale.
        amount = decimal.Round(spend, 2, MidpointRounding.AwayFromZero);
        return null;
    }

    /// <summary>A reported value, capped rather than refused - see the remark on <see cref="Heartbeat"/>.</summary>
    private static string? Fits(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    /// <summary>
    /// The remotes a beat named, canonicalised and newline-joined - or null
    /// where none survived. An invalid remote canonicalises to null and is
    /// dropped rather than refusing the whole heartbeat, the same as
    /// <see cref="Fits"/>: a heartbeat is not a decision.
    /// </summary>
    private static string? Canonicalised(IReadOnlyList<string>? remotes)
    {
        if (remotes is null) return null;
        var canonical = remotes
            .Select(r => RemoteIdentity.Canonical(r).Canonical)
            .Where(c => c is not null)
            .ToList();
        return canonical.Count > 0 ? string.Join('\n', canonical) : null;
    }

    /// <summary>
    /// The project keys a request named: trimmed, upper-cased (the normal form
    /// of <see cref="EfHatchProject.Key"/>), blanks dropped and duplicates
    /// folded, in the order given.
    /// </summary>
    private static List<string> ProjectKeys(IEnumerable<string>? raw) =>
        (raw ?? []).Select(p => p.Trim().ToUpperInvariant()).Where(p => p.Length > 0).Distinct().ToList();

    /// <summary>The normalised tags, newline-joined - or null where none survived.</summary>
    private static string? Joined(IReadOnlyList<string> tags) => tags.Count > 0 ? string.Join('\n', tags) : null;
}

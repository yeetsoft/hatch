using System.Globalization;
using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Models.Auth;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.DeviceMapping;
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
    TimeProvider time, IOptions<AuthOptions> auth, ISiteSettingsService siteSettings, IAuthService keys,
    RunnerSessions sessions, Dispatch dispatch) : ControllerBase
{
    /// <summary>
    /// Every runner this Hatch knows of: one entry per visible key - revoked
    /// included, with its config - plus one for every visible heartbeat row no
    /// key claims. <c>?live=true</c> narrows that to the entries actually
    /// answering lately, the list the board and the Runners page both draw by
    /// default.
    /// </summary>
    /// <remarks>
    /// Three whole reads and a join in memory - see <see cref="Runner.JoinAsync"/>
    /// - because the thing being matched on is a name, not a key anybody
    /// indexed, and the scale here is tens of rows.
    ///
    /// <para>Rows outside <see cref="VisibleAsync"/>'s reach are dropped by the
    /// same resolution <see cref="Heartbeat"/> writes <c>ForPersonId</c>
    /// through, via <see cref="IActorDirectory.PrincipalAsync"/> - so "whose
    /// runner is this" and "who may see it" cannot disagree.</para>
    /// </remarks>
    [HttpGet]
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<ActionResult<IReadOnlyList<RunnerDto>>> GetRunners(CancellationToken ct, [FromQuery] bool live = false)
    {
        var now = time.GetUtcNow();
        var (all, personId) = await VisibleAsync(ct);

        // The in-memory provider cannot run the conditional UPDATE below -
        // guarded the same way ProjectsController.cs does for its own sweep.
        if (db.Database.IsRelational()) await sessions.MarkAllGoneAsync(db, now, ct);

        var joined = await Runner.JoinAsync(db, keys, ct);
        var visible = joined.Where(r => all || r.OwnerPersonId == personId).ToList();
        if (live) visible = visible.Where(r => r.IsLive(runners, sessions, now)).ToList();

        var held = await runners.HeldAsync(db, claims, now, ct);
        var drivers = await runners.DriversAsync(db, keys, now, ct);
        var wallEnabled = auth.Value.Enabled;
        var callerPerson = wallEnabled ? await caller.PersonAsync(ct) : null;
        var site = await siteSettings.GetAsync(ct);

        var dtos = new List<RunnerDto>(visible.Count);
        foreach (var r in visible)
            dtos.Add(await ProjectAsync(r, held, drivers, wallEnabled, callerPerson, site, now, ct));

        return dtos;
    }

    /// <summary>One visible runner by its name, or <c>404</c> for a name nothing has ever used or this caller may not see.</summary>
    [HttpGet("{name}")]
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<ActionResult<RunnerDto>> GetRunner(string name, CancellationToken ct)
    {
        if (Named(name) is not { } runner) return BadRequest(NameRefusal);

        var now = time.GetUtcNow();
        var (all, personId) = await VisibleAsync(ct);

        if (db.Database.IsRelational()) await sessions.MarkAllGoneAsync(db, now, ct);

        var joined = await Runner.JoinAsync(db, keys, ct);
        var found = joined.FirstOrDefault(r => string.Equals(r.Name, runner, StringComparison.Ordinal));
        if (found is null || !(all || found.OwnerPersonId == personId)) return NotFound();

        var held = await runners.HeldAsync(db, claims, now, ct);
        var drivers = await runners.DriversAsync(db, keys, now, ct);
        var wallEnabled = auth.Value.Enabled;
        var callerPerson = wallEnabled ? await caller.PersonAsync(ct) : null;
        var site = await siteSettings.GetAsync(ct);

        return await ProjectAsync(found, held, drivers, wallEnabled, callerPerson, site, now, ct);
    }

    /// <summary>
    /// Mints a runner: a key and the config row beside it, answered once with
    /// the secret. Admin only, and never a key - an agent that could mint
    /// runners could mint itself more of whatever a runner may do.
    /// </summary>
    /// <remarks>
    /// Every refusal comes before anything is written: the gate, the name, the
    /// owner and the tags are all judged first, so a refused request leaves no
    /// key and no row. <c>suggest-name</c> is refused as a name because it is
    /// the literal route beside <c>{name}</c>, and a runner called that could
    /// not be reached by its own URL.
    ///
    /// <para>The key's uniqueness is <see cref="IAuthService"/>'s to judge, and
    /// it logs the mint too - so there is no logger here. The config row is
    /// written even with no tags, so a minted runner always has one.</para>
    /// </remarks>
    [HttpPost]
    [RequireRole(PersonRole.Admin)]
    public async Task<ActionResult<RunnerMintedDto>> CreateRunner([FromBody] CreateRunnerRequest? request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest("a runner needs a name");
        if (name.Length > EfApiKey.MaxNameLength)
            return BadRequest($"a runner name is at most {EfApiKey.MaxNameLength} characters");
        if (name.Equals("suggest-name", StringComparison.OrdinalIgnoreCase))
            return BadRequest($"\"{name}\" is a route word");

        // No owner named defaults to whoever is minting it, or to nobody where
        // the wall is off, as the key route this was ported from does.
        Guid? ownerPersonId;
        if (request?.OwnerPersonId is { } named)
        {
            if (await actors.ResolveAsync(ActorKind.Person, named, ct) is null)
                return BadRequest("that person does not exist");
            ownerPersonId = named;
        }
        else
        {
            ownerPersonId = (await caller.PersonAsync(ct))?.Id;
        }

        if (RunnerTags.Normalise(request?.Tags, out var tags) is { } tagsRefusal) return BadRequest(tagsRefusal);

        var created = await keys.CreateApiKeyAsync(name, ownerPersonId, ct);
        if (created is null) return Conflict($"there is already a runner called \"{name}\"");

        var config = new EfHatchRunnerConfig
        {
            ApiKeyId = created.Key.Id,
            Tags = RunnerTagColumn.Write(tags),
            UpdatedAt = time.GetUtcNow(),
        };
        db.RunnerConfigs.Add(config);
        await db.SaveChangesAsync(ct);

        // A live credential has no business in a cache, anyone's.
        Response.Headers.CacheControl = "no-store";

        // A new key holds nothing and drives nothing, so nothing is read for it.
        var wallEnabled = auth.Value.Enabled;
        var callerPerson = wallEnabled ? await caller.PersonAsync(ct) : null;
        var dto = await ProjectAsync(
            new Runner(created.Key, config, null), [], new Dictionary<string, IReadOnlyList<string>>(),
            wallEnabled, callerPerson, await siteSettings.GetAsync(ct), time.GetUtcNow(), ct);

        return new RunnerMintedDto(dto, created.Secret);
    }

    /// <summary>
    /// A cast-list name no key and no heartbeat row holds, for the form that
    /// mints a runner to offer. Revoked keys count: a name stays unique.
    /// </summary>
    /// <remarks>
    /// The set is case-insensitive itself, since <see cref="RunnerCast.Suggest"/>
    /// only asks it <c>Contains</c>. A literal route, so it beats <c>{name}</c>.
    /// </remarks>
    [HttpGet("suggest-name")]
    [RequireRole(PersonRole.Admin)]
    public async Task<ActionResult<string>> SuggestRunnerName(CancellationToken ct)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in await keys.ListApiKeysAsync(ct)) taken.Add(key.Name);
        foreach (var beat in await db.Runners.AsNoTracking().Select(r => r.Name).ToListAsync(ct)) taken.Add(beat);

        return RunnerCast.Suggest(taken);
    }

    /// <summary>One joined row as the wire shape, resolving the owner's name and the control bits that take it.</summary>
    private async Task<RunnerDto> ProjectAsync(
        Runner runner, Dictionary<(string Runner, string Where), (string Key, ClaimSnapshot Claim, string? Status)> held,
        IReadOnlyDictionary<string, IReadOnlyList<string>> drivers, bool wallEnabled, EfPerson? callerPerson,
        SiteSettingsSnapshot site, DateTimeOffset now, CancellationToken ct)
    {
        var ownerId = runner.OwnerPersonId;
        var forName = ownerId is { } id ? (await actors.ResolveAsync(ActorKind.Person, id, ct))?.Name : null;
        var canControl = CanControl(ownerId, wallEnabled, callerPerson);
        var order = runner.Beat?.OrderKind is { Length: > 0 } ok ? new RunnerOrderDto(ok, runner.Beat.OrderKey, runner.Beat.OrderedBy, runner.Beat.OrderedAt) : null;

        var dto = runners.Project(
            runner, Holding(held, (runner.Name, runner.Beat?.Where ?? "")), drivers.GetValueOrDefault(runner.Name, []), forName, canControl, order,
            yours: Yours(ownerId, wallEnabled, callerPerson),
            defaultMaxTokens: site.RunnerDefaultMaxTokens, defaultMaxTokensPerHour: site.RunnerDefaultMaxTokensPerHour);

        // A beat-less key with no legacy row still has what its live loop
        // sessions declared, the same union RunnerDeclaration.OfAsync folds.
        var loop = runner.Sessions?.Where(s => sessions.IsLive(s, now) && s.Kind == EfHatchRunner.LoopKind).ToList() ?? [];
        if (dto.Models.Count == 0) dto = dto with { Models = RunnerDeclaration.Declared(null, loop.Select(s => s.Models)) };
        if (dto.Has.Count == 0) dto = dto with { Has = RunnerDeclaration.Declared(null, loop.Select(s => s.Has)) };

        if (runner.Sessions is not { Count: > 0 } own) return dto;

        return dto with
        {
            Sessions = own
                .OrderByDescending(s => s.StartedAt).ThenByDescending(s => s.Id)
                .Select(s =>
                {
                    // An ended session must not borrow the live one's claim at
                    // the same `where`.
                    var claim = sessions.IsLive(s, now) ? Holding(held, (runner.Name, s.Where ?? "")) : null;
                    return sessions.Project(s, runner.Config, claim, site.RunnerDefaultMaxTokens, site.RunnerDefaultMaxTokensPerHour);
                })
                .ToList(),
        };
    }

    /// <summary>
    /// The session-level heartbeat <see cref="RunnerSessions"/> answers to -
    /// the bearer key alone, never a name or a header, so a loop restarting
    /// under a different checkout name is still the same session as long as
    /// its key and its <c>--where</c> have not changed.
    /// </summary>
    /// <remarks>
    /// <para><b>Additive.</b> Nothing in this checkout's own loop calls this
    /// yet - <c>GoToWork.cs</c> stays on <see cref="Heartbeat"/> until HA-1939
    /// can mint a keyless install a key - so only the tests below exercise
    /// it, the same note HA-2005 left on <see cref="EfHatchRunnerConfig"/>.</para>
    ///
    /// <para>The literal <c>sessions</c> segment beats <c>{name}</c> on
    /// <c>POST</c> the same way <c>names</c> and <c>suggest-name</c> already
    /// do - so a runner that happens to be named <c>sessions</c> can no
    /// longer heartbeat through this verb; it is still visible and still
    /// controllable by every other route.</para>
    ///
    /// <para><c>tags</c> on the body is neither validated nor stored here: a
    /// session has no column for it, and it is silently dropped, the same as
    /// an older Hatch ignoring a field it has never heard of.</para>
    /// </remarks>
    [HttpPost("sessions")]
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<ActionResult<RunnerInstructionDto>> SessionHeartbeat(RunnerHeartbeatRequest request, CancellationToken ct)
    {
        if (await caller.ApiKeyAsync(ct) is not { } key)
            return StatusCode(403, "a session heartbeat is the bearer key's, not a name or a header");

        if (RunnerModels.Normalise(request.Models, request.Agent, out var normalisedModels) is { } modelsRefusal) return BadRequest(modelsRefusal);
        if (DependencySet.Normalise(request.Has, out var normalisedHas) is { } hasRefusal) return BadRequest(hasRefusal);

        var kind = request.Kind?.Trim().ToLowerInvariant() switch
        {
            EfHatchRunner.OnceKind => EfHatchRunner.OnceKind,
            _ => EfHatchRunner.LoopKind,
        };

        var now = time.GetUtcNow();
        var line = Runners.Normalise(request.Line);
        var where = Fits(request.Where, EfHatchRunner.MaxNameLength);
        var ip = HttpContext?.Connection.RemoteIpAddress?.ToString();

        await sessions.MarkGoneAsync(db, key.Id, now, ct);

        var live = await db.RunnerSessions
            .Where(s => s.ApiKeyId == key.Id && s.EndedAt == null && s.Where == where)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (request.Fresh == true && kind == EfHatchRunner.LoopKind && live is not null)
            return Conflict($"{key.Name} is already the runner on {where ?? "an unnamed checkout"}");

        EfHatchRunnerSession session;
        if (live is null)
        {
            session = SeedSession(key.Id, kind, where, line, request, now, Joined(normalisedModels), Joined(normalisedHas), ip);
            db.RunnerSessions.Add(session);
            await db.SaveChangesAsync(ct);
            await sessions.TrimHistoryAsync(db, key.Id, ct);
        }
        else
        {
            session = live;
            TouchSession(session, kind, line, now, request, Joined(normalisedModels), Joined(normalisedHas), ip);
            await db.SaveChangesAsync(ct);
        }

        if (kind == EfHatchRunner.LoopKind) await RetireRenamedAsync(key.Name, key.OwnerPersonId, where, now, ct);

        await ReinstateAsync(key.Name, now, ct);

        var config = await db.RunnerConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.ApiKeyId == key.Id, ct);
        var site = await siteSettings.GetAsync(ct);
        var forName = (await ForAsync(ct))?.Name ?? key.Name;

        return RunnerSessions.Instruct(session, config, key.Name, forName, site.RunnerDefaultMaxTokens, site.RunnerDefaultMaxTokensPerHour);
    }

    /// <summary>
    /// Still here - and what should I do next. The only call a runner makes
    /// about itself, and the whole of the round trip: it says what it is, and
    /// reads back what the board would like.
    /// </summary>
    /// <remarks>
    /// <para>The six bounds in the body are what this process started with,
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
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<ActionResult<RunnerInstructionDto>> Heartbeat(
        string name, RunnerHeartbeatRequest request, CancellationToken ct)
    {
        if (Named(name) is not { } runner) return BadRequest(NameRefusal);
        if (RunnerModels.Normalise(request.Models, request.Agent, out var normalisedModels) is { } modelsRefusal) return BadRequest(modelsRefusal);
        if (DependencySet.Normalise(request.Has, out var normalisedHas) is { } hasRefusal) return BadRequest(hasRefusal);

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
        var site = await siteSettings.GetAsync(ct);

        if (row is null)
        {
            db.Runners.Add(row = Seed(runner, kind, line, request, now, forActor?.Id, Joined(normalisedModels), Joined(normalisedHas)));

            try
            {
                // Before the save, so the one write carries the new row and
                // the retirements together.
                await RetireSuperseded(row, now, ct);
                await db.SaveChangesAsync(ct);

                // A row born on this very beat has never been ordered.
                return Runners.Instruct(row, forName, (await TrainDriversAsync(runner, now, ct)).GetValueOrDefault(row.Name, []), null,
                    defaultMaxTokens: site.RunnerDefaultMaxTokens, defaultMaxTokensPerHour: site.RunnerDefaultMaxTokensPerHour);
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

        Touch(row, kind, line, now, request, forActor?.Id, Joined(normalisedModels), Joined(normalisedHas));
        await RetireSuperseded(row, now, ct);
        await db.SaveChangesAsync(ct);

        return Runners.Instruct(row, forName, (await TrainDriversAsync(runner, now, ct)).GetValueOrDefault(row.Name, []), order,
            defaultMaxTokens: site.RunnerDefaultMaxTokens, defaultMaxTokensPerHour: site.RunnerDefaultMaxTokensPerHour);
    }

    /// <summary>
    /// Retires every older row that this heartbeat's row has replaced: same
    /// checkout, same owner, same kind, first seen earlier, and quiet past the
    /// cutoff. A checkout that was renamed leaves its old row behind, and
    /// nothing else would ever say it is gone for good. Idempotent - it runs on
    /// every beat, and a row already retired is not touched again. Null owner
    /// with the wall up proves nothing, so it retires nothing; with the wall
    /// off, null matches null. Does not save: the caller's write carries it.
    /// </summary>
    private async Task RetireSuperseded(EfHatchRunner row, DateTimeOffset now, CancellationToken ct)
    {
        if (row.Where is not { Length: > 0 } where) return;
        if (row.ForPersonId is null && auth.Value.Enabled) return;

        var name = row.Name;
        var kind = row.Kind;
        var owner = row.ForPersonId;
        var first = row.FirstSeenAt;
        var cutoff = runners.Cutoff(now);

        var older = await db.Runners.Where(r => r.Name != name && r.Where == where && r.Kind == kind
            && r.ForPersonId == owner && r.FirstSeenAt < first && r.LastSeenAt < cutoff
            && r.RetiredAt == null).ToListAsync(ct);

        foreach (var r in older)
        {
            r.RetiredAt = now;
            r.RetiredBy = name;
        }
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

    /// <summary>The name an answer nobody pressed is written under - the same shape <see cref="IssueClaimController"/>'s own constant takes, mirrored rather than shared, since that one is private.</summary>
    private const string HatchActor = "Hatch";

    /// <summary>
    /// A loop beat under <paramref name="name"/> from <paramref name="where"/>
    /// retires every other loop row from that same checkout, at once: renaming a
    /// checkout's runner starts a new name and leaves the old row behind, and
    /// nothing else would say it is gone for good. The row named
    /// <paramref name="name"/> is the one that is beating, so its own mark is
    /// cleared instead. Idempotent - a row already retired is not touched
    /// again, so its stamp is the beat that first retired it. Only rows of the
    /// same owner are touched: a null owner with the wall up proves nothing, so
    /// it retires nothing, and with the wall off null matches null. A
    /// <c>once</c> beat never reaches here - it shares the loop's name and
    /// proves nothing about what else has gone.
    /// </summary>
    private async Task RetireRenamedAsync(string name, Guid? owner, string? where, DateTimeOffset now, CancellationToken ct)
    {
        if (where is not { Length: > 0 }) return;

        var own = await db.Runners.FirstOrDefaultAsync(r => r.Name == name && r.RetiredAt != null, ct);
        if (own is not null)
        {
            own.RetiredAt = null;
            own.RetiredBy = null;
        }

        if (owner is not null || !auth.Value.Enabled)
        {
            var older = await db.Runners.Where(r => r.Name != name && r.Where == where
                && r.Kind == EfHatchRunner.LoopKind && r.ForPersonId == owner && r.RetiredAt == null).ToListAsync(ct);

            foreach (var r in older)
            {
                r.RetiredAt = now;
                r.RetiredBy = name;
            }
        }

        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// This runner's own beat, reinstating any project it had been passed over
    /// on: the runner answering is proof enough that it is live again, so the
    /// project it drives is given a fresh window rather than being passed over
    /// again on the very same call that just reinstated it - HA-2092's
    /// correction 2.
    /// </summary>
    private async Task ReinstateAsync(string runner, DateTimeOffset now, CancellationToken ct)
    {
        var reinstated = await db.Projects.Where(p => p.TrainStalledDriver == runner).ToListAsync(ct);
        if (reinstated.Count == 0) return;

        foreach (var project in reinstated)
        {
            project.TrainStalledDriver = null;
            if (project.TrainWatchedIssueId is not null) project.TrainWatchedSince = now;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// <see cref="Runners.DriversAsync"/>, folded through this heartbeat's own
    /// reinstatement and HA-2076's stall watch - the one place a beat's
    /// instruction and the train's own pass-over can never disagree, because
    /// both read the same election.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> TrainDriversAsync(
        string runner, DateTimeOffset now, CancellationToken ct)
    {
        await ReinstateAsync(runner, now, ct);

        var before = await runners.DriversAsync(db, keys, now, ct);
        var driverByProject = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, keys_) in before)
            foreach (var key in keys_)
                driverByProject[key] = name;

        var heads = await dispatch.TrainHeadsAsync(now, ct);
        var headByProject = heads.ToDictionary(h => h.ProjectId);

        var projects = await db.Projects.ToListAsync(ct);
        var stalled = new List<(EfHatchProject Project, long IssueId, string PassedOver)>();

        foreach (var project in projects)
        {
            var head = headByProject.GetValueOrDefault(project.Id);
            var hasDriver = driverByProject.TryGetValue(project.Key, out var passedOver);

            if (TrainWatch.Observe(project, head, hasDriver, now, TimeSpan.FromSeconds(runners.TrainStallSeconds)) && head is not null)
                stalled.Add((project, head.IssueId, passedOver!));
        }

        if (stalled.Count == 0)
        {
            await db.SaveChangesAsync(ct);
            return before;
        }

        foreach (var (project, _, passedOver) in stalled)
        {
            project.TrainStalledDriver = passedOver;
            project.TrainWatchedSince = now;
        }
        await db.SaveChangesAsync(ct);

        var after = await runners.DriversAsync(db, keys, now, ct);
        var driverByProjectAfter = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, keys_) in after)
            foreach (var key in keys_)
                driverByProjectAfter[key] = name;

        foreach (var (project, issueId, passedOver) in stalled)
        {
            var elected = driverByProjectAfter.GetValueOrDefault(project.Key);
            if (await db.Issues.FirstOrDefaultAsync(i => i.Id == issueId, ct) is not { } issue) continue;

            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = HatchActor,
                Kind = EfHatchIssueEvent.DriverStalled,
                Payload = JsonSerializer.Serialize(new { passedOver, elected }),
                At = now,
            });
            issue.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);

        return after;
    }

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

        var aggregate = (await Runner.JoinAsync(db, keys, ct)).FirstOrDefault(r => string.Equals(r.Name, runner, StringComparison.Ordinal));
        if (aggregate is null) return NotFound();

        // Before ownership: a revoked key's runner is not one anybody may
        // steer, whoever is asking.
        var now = time.GetUtcNow();
        if (aggregate.Key is { } liveKey && !liveKey.IsLive(now)) return Conflict($"{runner}'s key is revoked");

        var wallEnabled = auth.Value.Enabled;
        var callerPerson = wallEnabled ? await caller.PersonAsync(ct) : null;
        if (!CanControl(aggregate.OwnerPersonId, wallEnabled, callerPerson))
        {
            var forName = aggregate.OwnerPersonId is { } forId
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
        if (Tokens(request.MaxTokens, "--max-tokens", out var maxTokens) is { } tokensError) return BadRequest(tokensError);
        if (Tokens(request.MaxTokensPerHour, "--max-tokens-per-hour", out var maxTokensPerHour) is { } tokensPerHourError) return BadRequest(tokensPerHourError);
        if (RunnerTags.Normalise(request.Tags, out var normalisedTags) is { } tagsRefusal) return BadRequest(tagsRefusal);

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

        // The heartbeat row is tracked and may be absent: a key that has never
        // run has none. State lives only there, so with none it is skipped
        // rather than refused.
        var beatRow = await db.Runners.FirstOrDefaultAsync(r => r.Name == runner, ct);
        if (beatRow is not null && state is not null) beatRow.State = state;

        // "" clears the board, never null - the whole of how PATCH's clear is
        // told apart from a row no heartbeat has seeded yet, with no migration.
        var beatTags = normalisedTags.Count > 0 ? string.Join('\n', normalisedTags) : "";

        EfHatchRunnerConfig? config = null;
        if (aggregate.Key is { } key)
        {
            // The key's config row is the record of the bounds and tags; the
            // heartbeat row is mirrored so a runner reading its own row sees
            // the same thing. A key with no config row yet gets one here.
            var bounded = request.Under is not null || request.MaxRuns is not null || request.MaxSpend is not null
                || request.MaxTokens is not null || request.MaxTokensPerHour is not null || request.UntilAt is not null
                || request.Tags is not null;

            if (bounded)
            {
                config = await db.RunnerConfigs.FirstOrDefaultAsync(c => c.ApiKeyId == key.Id, ct);
                if (config is null)
                    db.RunnerConfigs.Add(config = new EfHatchRunnerConfig { ApiKeyId = key.Id, UpdatedAt = now });

                if (request.Under is not null) { config.Under = under; if (beatRow is not null) beatRow.Under = under; }
                if (request.MaxRuns is not null) { config.MaxRuns = maxRuns; if (beatRow is not null) beatRow.MaxRuns = maxRuns; }
                if (request.MaxSpend is not null) { config.MaxSpend = maxSpend; if (beatRow is not null) beatRow.MaxSpend = maxSpend; }
                if (request.MaxTokens is not null) { config.MaxTokens = maxTokens; if (beatRow is not null) beatRow.MaxTokens = maxTokens; }
                if (request.MaxTokensPerHour is not null) { config.MaxTokensPerHour = maxTokensPerHour; if (beatRow is not null) beatRow.MaxTokensPerHour = maxTokensPerHour; }
                if (request.UntilAt is not null) { config.UntilAt = untilAt; if (beatRow is not null) beatRow.UntilAt = untilAt; }
                if (request.Tags is not null) { config.Tags = RunnerTagColumn.Write(normalisedTags); if (beatRow is not null) beatRow.Tags = beatTags; }

                config.UpdatedAt = now;
            }
        }
        else if (beatRow is not null)
        {
            // A heartbeat no key claims has nowhere else to keep them, and no
            // config row is ever made for it.
            if (request.Under is not null) beatRow.Under = under;
            if (request.MaxRuns is not null) beatRow.MaxRuns = maxRuns;
            if (request.MaxSpend is not null) beatRow.MaxSpend = maxSpend;
            if (request.MaxTokens is not null) beatRow.MaxTokens = maxTokens;
            if (request.MaxTokensPerHour is not null) beatRow.MaxTokensPerHour = maxTokensPerHour;
            if (request.UntilAt is not null) beatRow.UntilAt = untilAt;
            if (request.Tags is not null) beatRow.Tags = beatTags;
        }

        await db.SaveChangesAsync(ct);

        return await ProjectAsync(
            new Runner(aggregate.Key, config ?? aggregate.Config, beatRow, aggregate.Sessions),
            await runners.HeldAsync(db, claims, now, ct), await runners.DriversAsync(db, keys, now, ct),
            wallEnabled, callerPerson, await siteSettings.GetAsync(ct), now, ct);
    }

    /// <summary>
    /// Stops a runner's key working, by the runner's name. The row stays - the
    /// audit trail names this key, and a revoked runner is still listed - so
    /// this is a <c>POST</c>, not a <c>DELETE</c>. Admin only, and never a key.
    /// </summary>
    [HttpPost("{name}/revoke")]
    [RequireAdmin]
    public async Task<IActionResult> RevokeRunner(string name, CancellationToken ct)
    {
        if (Named(name) is not { } runner) return BadRequest(NameRefusal);
        if (await KeyNamedAsync(runner, ct) is not { } key) return NotFound();

        return await keys.RevokeApiKeyAsync(key.Id, ct) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Replaces a runner's secret, answered once. Admin only, and never a key -
    /// a key that could rotate could take over another runner's name.
    /// </summary>
    /// <remarks>
    /// Config, heartbeat rows and sessions are untouched: they join to the key
    /// by id or by name, and rotating only changes the key's hash, prefix and
    /// <see cref="EfApiKey.RotatedAt"/>.
    /// </remarks>
    [HttpPost("{name}/rotate")]
    [RequireAdmin]
    public async Task<ActionResult<RunnerMintedDto>> RotateRunner(string name, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;
        if (Named(name) is not { } runner) return BadRequest(NameRefusal);
        if (await KeyNamedAsync(runner, ct) is not { } key) return NotFound();
        if (!key.IsLive(time.GetUtcNow())) return Conflict($"{runner}'s key is revoked");

        if (await keys.RotateApiKeyAsync(key.Id, ct) is not { } rotated) return NotFound();

        Response.Headers.CacheControl = "no-store";

        var now = time.GetUtcNow();
        var found = (await Runner.JoinAsync(db, keys, ct)).First(r => string.Equals(r.Name, runner, StringComparison.Ordinal));
        var held = await runners.HeldAsync(db, claims, now, ct);
        var drivers = await runners.DriversAsync(db, keys, now, ct);
        var wallEnabled = auth.Value.Enabled;
        var callerPerson = wallEnabled ? await caller.PersonAsync(ct) : null;
        var site = await siteSettings.GetAsync(ct);

        var dto = await ProjectAsync(found, held, drivers, wallEnabled, callerPerson, site, now, ct);
        return new RunnerMintedDto(dto, rotated.Secret);
    }

    /// <summary>
    /// Changes or clears whose tickets this runner's <c>--mine</c> reaches. Only
    /// an admin, and deliberately never the key itself: a key that could set its
    /// own owner could adopt anybody's tickets.
    /// </summary>
    [HttpPut("{name}/owner")]
    [RequireAdmin]
    public async Task<IActionResult> SetRunnerOwner(string name, [FromBody] RunnerOwnerRequest? request, CancellationToken ct)
    {
        if (Named(name) is not { } runner) return BadRequest(NameRefusal);
        if (request?.PersonId is { } personId && await actors.ResolveAsync(ActorKind.Person, personId, ct) is null)
            return BadRequest("that person does not exist");
        if (await KeyNamedAsync(runner, ct) is not { } key) return NotFound();

        return await keys.SetApiKeyOwnerAsync(key.Id, request?.PersonId, ct) ? NoContent() : NotFound();
    }

    /// <summary>The key a runner name answers to, revoked or not, or null where no key has that name.</summary>
    private async Task<EfApiKey?> KeyNamedAsync(string runner, CancellationToken ct) =>
        (await keys.ListApiKeysAsync(ct)).FirstOrDefault(k => string.Equals(k.Name, runner, StringComparison.Ordinal));

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
        var site = await siteSettings.GetAsync(ct);
        return runners.Project(
            row,
            Holding(await runners.HeldAsync(db, claims, now, ct), (row.Name, row.Where ?? "")),
            (await runners.DriversAsync(db, keys, now, ct)).GetValueOrDefault(row.Name, []),
            ownerName,
            CanControl(row.ForPersonId, wallEnabled, callerPerson),
            new RunnerOrderDto(kind, key, by, now),
            yours: Yours(row.ForPersonId, wallEnabled, callerPerson),
            defaultMaxTokens: site.RunnerDefaultMaxTokens, defaultMaxTokensPerHour: site.RunnerDefaultMaxTokensPerHour);
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

    // ---- Session controls ----

    /// <summary>
    /// Sets one session's state, by its id - <see cref="PatchRunner"/>'s own
    /// <c>State</c>, narrowed to the one session a session row names rather
    /// than the beat every other session under the key shares.
    /// </summary>
    [HttpPatch("{name}/sessions/{id:int}")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<RunnerDto>> PatchRunnerSession(
        string name, int id, RunnerSessionPatchRequest request, CancellationToken ct)
    {
        var (key, refusal) = await GateAsync(name, ct);
        if (refusal is not null) return refusal;

        var session = await SessionNamedAsync(key!.Id, id, ct);
        if (session is null) return NotFound();

        var sent = request.State?.Trim();
        var state = sent?.ToLowerInvariant();
        if (state is null || !EfHatchRunner.States.Contains(state))
            return BadRequest($"a runner is {string.Join(", ", EfHatchRunner.States)} - not \"{sent}\"");

        session.State = state;
        await db.SaveChangesAsync(ct);

        return await AnswerAsync(key.Name, ct);
    }

    /// <summary>
    /// Orders one session to work a named ticket, or to pass on its next pick
    /// - <see cref="PostRunnerOrder"/>'s own refusals and writes, narrowed to
    /// the session a session row names.
    /// </summary>
    [HttpPost("{name}/sessions/{id:int}/order")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<RunnerDto>> PostRunnerSessionOrder(
        string name, int id, RunnerOrderRequest request, CancellationToken ct)
    {
        var (key, refusal) = await GateAsync(name, ct);
        if (refusal is not null) return refusal;

        var session = await SessionNamedAsync(key!.Id, id, ct);
        if (session is null) return NotFound();

        var kind = request.Kind?.Trim().ToLowerInvariant();
        if (kind is null || !RunnerOrderKinds.All.Contains(kind))
            return BadRequest($"an order is {string.Join(", ", RunnerOrderKinds.All)} - not \"{request.Kind?.Trim()}\"");

        EfHatchIssue? issue = null;
        string? orderedKey = null;
        if (kind == RunnerOrderKinds.Work)
        {
            if (request.Key is not { } wanted || wanted.Trim().Length == 0 || !IssueKey.TryParse(wanted, out var projectKey, out var number))
                return BadRequest($"work names an issue, like AER-12 - not \"{request.Key?.Trim()}\"");

            orderedKey = IssueKey.Format(projectKey, number);
            issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
            if (issue is null) return NotFound($"{orderedKey} does not exist");
        }

        if (session.Kind == EfHatchRunner.OnceKind)
            return Conflict($"{key.Name} is a single increment and will never read an order back");

        if (session.State == EfHatchRunner.Stopping)
            return Conflict($"{key.Name} is stopping and will not take another order");

        if (session.OrderKind is { Length: > 0 } heldKind)
        {
            var held = heldKind == RunnerOrderKinds.Work ? $"{heldKind} {session.OrderKey}" : heldKind;
            return Conflict($"{key.Name} already holds an order to {held} - withdraw it first");
        }

        var now = time.GetUtcNow();
        var by = await caller.ActorNameAsync(ct);

        session.OrderKind = kind;
        session.OrderKey = orderedKey;
        session.OrderedBy = by;
        session.OrderedAt = now;

        if (issue is not null)
        {
            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = by,
                Kind = EfHatchIssueEvent.Ordered,
                Payload = JsonSerializer.Serialize(new { runner = key.Name, by }),
                At = now,
            });
        }

        await db.SaveChangesAsync(ct);

        return await AnswerAsync(key.Name, ct);
    }

    /// <summary>Withdraws whatever order one session holds - see <see cref="PostRunnerSessionOrder"/>.</summary>
    [HttpDelete("{name}/sessions/{id:int}/order")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult> DeleteRunnerSessionOrder(string name, int id, CancellationToken ct)
    {
        var (key, refusal) = await GateAsync(name, ct);
        if (refusal is not null) return refusal;

        var session = await SessionNamedAsync(key!.Id, id, ct);
        if (session is null) return NotFound();

        if (session.OrderKind is null) return NoContent();

        session.OrderKind = null;
        session.OrderKey = null;
        session.OrderedBy = null;
        session.OrderedAt = null;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Pauses every live <c>loop</c> session under this key at once - see <see cref="FanOutAsync"/>.</summary>
    [HttpPost("{name}/pause-all")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult> PauseAll(string name, CancellationToken ct) =>
        await FanOutAsync(name, EfHatchRunner.Paused, ct);

    /// <summary>Stops every live <c>loop</c> session under this key at once - see <see cref="FanOutAsync"/>.</summary>
    [HttpPost("{name}/stop-all")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult> StopAll(string name, CancellationToken ct) =>
        await FanOutAsync(name, EfHatchRunner.Stopping, ct);

    /// <summary>
    /// Sets <paramref name="state"/> on every live <c>loop</c> session under
    /// this key, <c>once</c> sessions, ended sessions and sessions past the
    /// horizon left untouched - <see cref="PauseAll"/> and
    /// <see cref="StopAll"/>'s one write. Plain reads and writes, never
    /// <c>ExecuteUpdateAsync</c>: the in-memory provider this controller's own
    /// tests run against refuses a conditional <c>UPDATE</c>, the same reason
    /// <see cref="RunnerSessions.MarkGoneAsync"/>'s own tests are
    /// <c>SkippableFact</c>.
    /// </summary>
    private async Task<ActionResult> FanOutAsync(string name, string state, CancellationToken ct)
    {
        var (key, refusal) = await GateAsync(name, ct);
        if (refusal is not null) return refusal;

        var now = time.GetUtcNow();
        var candidates = await db.RunnerSessions
            .Where(s => s.ApiKeyId == key!.Id && s.Kind == EfHatchRunner.LoopKind && s.EndedAt == null)
            .ToListAsync(ct);

        foreach (var session in candidates.Where(s => sessions.IsLive(s, now))) session.State = state;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// The four refusals every session control route shares before it touches
    /// a session: not a person, a usable name, a key by that name, and
    /// whether this caller controls it - <see cref="PatchRunner"/>'s own
    /// order, narrowed to the key, since a session answers to the key that
    /// holds it rather than to a beat row of its own.
    /// </summary>
    private async Task<(EfApiKey? Key, ActionResult? Refusal)> GateAsync(string name, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } personRefusal) return (null, personRefusal);
        if (Named(name) is not { } runner) return (null, BadRequest(NameRefusal));
        if (await KeyNamedAsync(runner, ct) is not { } key) return (null, NotFound());

        var wallEnabled = auth.Value.Enabled;
        var callerPerson = wallEnabled ? await caller.PersonAsync(ct) : null;
        if (!CanControl(key.OwnerPersonId, wallEnabled, callerPerson))
            return (null, await ControlRefusalAsync(key.Name, key.OwnerPersonId, ct));

        return (key, null);
    }

    /// <summary>
    /// The one 403 sentence <see cref="PatchRunner"/>, <see cref="PostRunnerOrder"/>
    /// and <see cref="DeleteRunnerOrder"/> already each paste their own copy
    /// of - written once more here for the four session routes rather than a
    /// fourth paste.
    /// </summary>
    private async Task<ObjectResult> ControlRefusalAsync(string runner, Guid? ownerPersonId, CancellationToken ct)
    {
        var forName = ownerPersonId is { } forId
            ? (await actors.ResolveAsync(ActorKind.Person, forId, ct))?.Name ?? "its owner"
            : "its owner";
        return new ObjectResult($"only {forName}, or an admin, may control {runner}")
            { StatusCode = StatusCodes.Status403Forbidden };
    }

    /// <summary>The session this key's id names, or null for one that is somebody else's or does not exist.</summary>
    private Task<EfHatchRunnerSession?> SessionNamedAsync(Guid apiKeyId, int id, CancellationToken ct) =>
        db.RunnerSessions.FirstOrDefaultAsync(s => s.Id == id && s.ApiKeyId == apiKeyId, ct);

    /// <summary>The joined row by name, re-read after a session write and projected exactly as <see cref="RotateRunner"/> answers.</summary>
    private async Task<RunnerDto> AnswerAsync(string name, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var found = (await Runner.JoinAsync(db, keys, ct)).First(r => string.Equals(r.Name, name, StringComparison.Ordinal));
        var held = await runners.HeldAsync(db, claims, now, ct);
        var drivers = await runners.DriversAsync(db, keys, now, ct);
        var wallEnabled = auth.Value.Enabled;
        var callerPerson = wallEnabled ? await caller.PersonAsync(ct) : null;
        var site = await siteSettings.GetAsync(ct);

        return await ProjectAsync(found, held, drivers, wallEnabled, callerPerson, site, now, ct);
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

    /// <summary>Whether this row is the caller's own: the wall off, or the caller being the row's owner. Unlike <see cref="CanControl"/>, an admin does not have it on others' rows, and nobody has it on a row that belongs to nobody.</summary>
    private static bool Yours(Guid? forPersonId, bool wallEnabled, EfPerson? callerPerson) =>
        !wallEnabled || (forPersonId is { } id && callerPerson?.Id == id);

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
    /// The six bounds, from what the flags said, so the row shows the bounds
    /// it is actually running under rather than reading as unbounded until
    /// somebody sets something. One place for a new row and a fresh beat, so
    /// the two cannot drift.
    /// </summary>
    private static void Bound(EfHatchRunner row, RunnerHeartbeatRequest request)
    {
        row.Under = Fits(request.Under, EfHatchRunner.MaxUnderLength);
        row.MaxRuns = request.MaxRuns is > 0 ? request.MaxRuns : null;
        row.MaxSpend = request.MaxSpend is > 0 ? request.MaxSpend : null;
        row.MaxTokens = request.MaxTokens is > 0 ? request.MaxTokens : null;
        row.MaxTokensPerHour = request.MaxTokensPerHour is > 0 ? request.MaxTokensPerHour : null;
        row.UntilAt = request.UntilAt;
    }

    /// <summary>A row that has never been seen before, seeded with what the process started with.</summary>
    private static EfHatchRunner Seed(
        string name, string kind, string? line, RunnerHeartbeatRequest request, DateTimeOffset now,
        Guid? forPersonId, string? models, string? has)
    {
        var keys = ProjectKeys(request.Projects);
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
            Runs = request.Runs,
            Spent = request.Spent,
            TokensUsed = request.TokensUsed,
            TokensLastHour = request.TokensLastHour,
            Host = Fits(request.Host, EfHatchRunner.MaxHostLength),
            Agent = Fits(request.Agent, EfHatchRunner.MaxAgentLength),
            Revision = Fits(request.Revision, EfHatchRunner.MaxRevisionLength),
            Sequence = request.Sequence,
            SourceRevision = Fits(request.SourceRevision, EfHatchRunner.MaxRevisionLength),
            SourceSequence = request.SourceSequence,
            BuiltAt = request.BuiltAt,
            Models = models,
            Has = has,
            Projects = keys.Count > 0 ? string.Join('\n', keys) : null,
            RefusesDangerous = request.RefusesDangerous,
            ExhaustedUntil = request.Exhausted == true ? request.ExhaustedUntil : null,
            ThrottledUntil = request.ThrottledUntil,
            ForPersonId = forPersonId,
            Usage = Utilization.Serialise(request.Usage),
            UsageReadAt = request.UsageReadAt,
        };

        Bound(row, request);
        return row;
    }

    /// <summary>
    /// A row that already exists: kind, last seen, line, and the two facts
    /// about what it serves. The state is the operator's, with one exception: a
    /// fresh <c>loop</c> beat that says <c>Standby</c> writes it <c>paused</c>.
    /// The six bounds are the flags' at a fresh start - a <c>loop</c> beat that
    /// says so - and the operator's after: any other beat writing one would be
    /// the runner having the last word on what it may spend.
    /// </summary>
    private static void Touch(
        EfHatchRunner row, string kind, string? line, DateTimeOffset now, RunnerHeartbeatRequest request,
        Guid? forPersonId, string? models, string? has)
    {
        row.Kind = kind;
        row.LastSeenAt = now;
        // The row's own beat is the one thing that un-retires it.
        row.RetiredAt = null;
        row.RetiredBy = null;

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
        if (request.Runs is not null) row.Runs = request.Runs;
        if (request.Spent is not null) row.Spent = request.Spent;
        if (request.TokensUsed is not null) row.TokensUsed = request.TokensUsed;
        if (request.TokensLastHour is not null) row.TokensLastHour = request.TokensLastHour;
        if (request.Host is not null) row.Host = Fits(request.Host, EfHatchRunner.MaxHostLength);
        if (request.Agent is not null) row.Agent = Fits(request.Agent, EfHatchRunner.MaxAgentLength);
        if (request.Revision is not null) row.Revision = Fits(request.Revision, EfHatchRunner.MaxRevisionLength);
        if (request.Sequence is not null) row.Sequence = request.Sequence;
        if (request.SourceRevision is not null) row.SourceRevision = Fits(request.SourceRevision, EfHatchRunner.MaxRevisionLength);
        if (request.SourceSequence is not null) row.SourceSequence = request.SourceSequence;
        if (request.BuiltAt is not null) row.BuiltAt = request.BuiltAt;

        // An empty list clears it: Joined answers null for one.
        if (request.Models is not null) row.Models = models;
        if (request.Has is not null) row.Has = has;

        // A fact about the process, like Where and Remotes: overwritten by
        // whatever the latest beat said, any kind. Tags are never taken off a
        // beat at all - only PatchRunner moves row.Tags.
        if (request.RefusesDangerous is not null) row.RefusesDangerous = request.RefusesDangerous;
        if (request.Projects is not null)
        {
            var keys = ProjectKeys(request.Projects);
            row.Projects = keys.Count > 0 ? string.Join('\n', keys) : null;
        }

        // A tri-state of its own: absent leaves the row's own record alone (an
        // older CLI, or hatch work's single beat, neither of which knows
        // anything about the account it ran under), true sets it, false clears
        // it at once - the one way a loop overrides its own past heartbeat.
        if (request.Exhausted == true) row.ExhaustedUntil = request.ExhaustedUntil;
        else if (request.Exhausted == false) row.ExhaustedUntil = null;

        // Written on every loop beat, null clearing it the moment the runner
        // says it is no longer throttled - not a tri-state like Exhausted,
        // since there is no separate flag to carry "not throttled" apart from
        // the value itself. Not a `once` beat: it shares the loop's row name
        // (the same reasoning as ForPersonId below) and sending null must not
        // clear the loop's own wait.
        if (kind == EfHatchRunner.LoopKind) row.ThrottledUntil = request.ThrottledUntil;

        // Whose key this is can change on the API Keys page, so this is
        // written every beat like Where and Remotes, unlike the six bounds,
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
        if (Utilization.Serialise(request.Usage) is { } usage)
        {
            row.Usage = usage;
            row.UsageReadAt = request.UsageReadAt;
        }
    }

    /// <summary>
    /// A session that has never run before, seeded with what the process
    /// started with - <see cref="Seed"/>'s own field list, minus
    /// <c>Tags</c> and <c>ForPersonId</c>, none of which a session has a
    /// column for, plus the six machine fields and the connection's IP,
    /// neither of which a beat has.
    /// </summary>
    private static EfHatchRunnerSession SeedSession(
        Guid apiKeyId, string kind, string? where, string? line, RunnerHeartbeatRequest request, DateTimeOffset now,
        string? models, string? has, string? ip)
    {
        var keys = ProjectKeys(request.Projects);

        return new EfHatchRunnerSession
        {
            ApiKeyId = apiKeyId,
            Kind = kind,
            Where = where,
            Host = Fits(request.Host, EfHatchRunner.MaxHostLength),
            StartedAt = now,
            LastSeenAt = now,
            Line = line is { Length: > 0 } ? line : null,
            LineAt = line is { Length: > 0 } ? now : null,
            State = kind == EfHatchRunner.LoopKind && request.Standby == true
                ? EfHatchRunner.Paused
                : EfHatchRunner.Running,

            Under = Fits(request.Under, EfHatchRunner.MaxUnderLength),
            MaxRuns = request.MaxRuns is > 0 ? request.MaxRuns : null,
            MaxSpend = request.MaxSpend is > 0 ? request.MaxSpend : null,
            MaxTokens = request.MaxTokens is > 0 ? request.MaxTokens : null,
            MaxTokensPerHour = request.MaxTokensPerHour is > 0 ? request.MaxTokensPerHour : null,
            UntilAt = request.UntilAt,
            Projects = keys.Count > 0 ? string.Join('\n', keys) : null,

            Remotes = Canonicalised(request.Remotes),
            Clones = request.Clones,
            Mine = request.Mine,
            Standing = request.Standing,
            Runs = request.Runs,
            Spent = request.Spent,
            TokensUsed = request.TokensUsed,
            TokensLastHour = request.TokensLastHour,
            Agent = Fits(request.Agent, EfHatchRunner.MaxAgentLength),
            Revision = Fits(request.Revision, EfHatchRunner.MaxRevisionLength),
            Sequence = request.Sequence,
            SourceRevision = Fits(request.SourceRevision, EfHatchRunner.MaxRevisionLength),
            SourceSequence = request.SourceSequence,
            BuiltAt = request.BuiltAt,
            RefusesDangerous = request.RefusesDangerous,
            ExhaustedUntil = request.Exhausted == true ? request.ExhaustedUntil : null,
            ThrottledUntil = request.ThrottledUntil,
            Usage = Utilization.Serialise(request.Usage),
            UsageReadAt = request.UsageReadAt,
            Models = models,
            Has = has,
            Os = Fits(request.Os, EfHatchRunnerSession.MaxOsLength),
            Arch = Fits(request.Arch, EfHatchRunnerSession.MaxArchLength),
            AgentVersion = Fits(request.AgentVersion, EfHatchRunnerSession.MaxAgentVersionLength),
            Container = request.Container,
            Username = Fits(request.Username, EfHatchRunnerSession.MaxUsernameLength),
            ProcessStartedAt = request.ProcessStartedAt,
            Ip = Fits(ip, EfHatchRunnerSession.MaxIpLength),
        };
    }

    /// <summary>
    /// A session that already exists: last seen, line, and the facts about
    /// what it served - <see cref="Touch"/>'s own field list, minus
    /// <c>Kind</c> (a <c>once</c> beat must not turn a loop session into a
    /// <c>once</c> one), <c>Tags</c>, <c>ForPersonId</c>, the <c>Fresh</c>
    /// bounds block and the order read-and-clear - none of which a session
    /// answers to, or which this route never reads or writes
    /// (<c>OrderKind</c>/<c>OrderedAt</c>) - plus the six machine fields,
    /// each absent leaving its stored value alone, and the connection's IP,
    /// written on every beat regardless.
    /// </summary>
    private static void TouchSession(
        EfHatchRunnerSession session, string kind, string? line, DateTimeOffset now, RunnerHeartbeatRequest request,
        string? models, string? has, string? ip)
    {
        session.LastSeenAt = now;

        switch (line)
        {
            case null: break;
            case "":
                session.Line = null;
                session.LineAt = null;
                break;
            default:
                session.Line = line;
                session.LineAt = now;
                break;
        }

        if (request.Remotes is not null) session.Remotes = Canonicalised(request.Remotes);
        if (request.Clones is not null) session.Clones = request.Clones;
        if (request.Mine is not null) session.Mine = request.Mine;
        if (request.Standing is not null) session.Standing = request.Standing;
        if (request.Where is not null) session.Where = Fits(request.Where, EfHatchRunner.MaxNameLength);
        if (request.Runs is not null) session.Runs = request.Runs;
        if (request.Spent is not null) session.Spent = request.Spent;
        if (request.TokensUsed is not null) session.TokensUsed = request.TokensUsed;
        if (request.TokensLastHour is not null) session.TokensLastHour = request.TokensLastHour;
        if (request.Host is not null) session.Host = Fits(request.Host, EfHatchRunner.MaxHostLength);
        if (request.Agent is not null) session.Agent = Fits(request.Agent, EfHatchRunner.MaxAgentLength);
        if (request.Revision is not null) session.Revision = Fits(request.Revision, EfHatchRunner.MaxRevisionLength);
        if (request.Sequence is not null) session.Sequence = request.Sequence;
        if (request.SourceRevision is not null) session.SourceRevision = Fits(request.SourceRevision, EfHatchRunner.MaxRevisionLength);
        if (request.SourceSequence is not null) session.SourceSequence = request.SourceSequence;
        if (request.BuiltAt is not null) session.BuiltAt = request.BuiltAt;

        if (request.Os is not null) session.Os = Fits(request.Os, EfHatchRunnerSession.MaxOsLength);
        if (request.Arch is not null) session.Arch = Fits(request.Arch, EfHatchRunnerSession.MaxArchLength);
        if (request.AgentVersion is not null) session.AgentVersion = Fits(request.AgentVersion, EfHatchRunnerSession.MaxAgentVersionLength);
        if (request.Username is not null) session.Username = Fits(request.Username, EfHatchRunnerSession.MaxUsernameLength);
        if (request.Container is not null) session.Container = request.Container;
        if (request.ProcessStartedAt is not null) session.ProcessStartedAt = request.ProcessStartedAt;
        if (ip is not null) session.Ip = Fits(ip, EfHatchRunnerSession.MaxIpLength);

        // An empty list clears it: Joined answers null for one, the same as Touch.
        if (request.Models is not null) session.Models = models;
        if (request.Has is not null) session.Has = has;

        // A fact about the process, like Where and Remotes - the same rule Touch draws for a beat.
        if (request.RefusesDangerous is not null) session.RefusesDangerous = request.RefusesDangerous;
        if (request.Projects is not null)
        {
            var keys = ProjectKeys(request.Projects);
            session.Projects = keys.Count > 0 ? string.Join('\n', keys) : null;
        }

        if (request.Exhausted == true) session.ExhaustedUntil = request.ExhaustedUntil;
        else if (request.Exhausted == false) session.ExhaustedUntil = null;

        // Loop-only, the same as Touch: a `once` beat shares the loop
        // session's (key, where) and must not clear its own wait.
        if (kind == EfHatchRunner.LoopKind) session.ThrottledUntil = request.ThrottledUntil;

        if (Utilization.Serialise(request.Usage) is { } usage)
        {
            session.Usage = usage;
            session.UsageReadAt = request.UsageReadAt;
        }
    }

    /// <summary>
    /// What this runner holds, or null. Written out rather than reached for
    /// with <c>GetValueOrDefault</c>, which for a tuple hands back a pair of
    /// nulls that is not itself null - a claim on nothing, drawn as though it
    /// were one.
    /// </summary>
    private static (string Key, ClaimSnapshot Claim, string? Status)? Holding(
        Dictionary<(string Runner, string Where), (string Key, ClaimSnapshot Claim, string? Status)> held,
        (string Runner, string Where) name) =>
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

    /// <summary>The same, as a token count - <see cref="TokenCount.TryParse"/>'s own notation.</summary>
    private static string? Tokens(string? sent, string flag, out long? count)
    {
        count = null;
        if (sent is null || sent.Trim().Length == 0) return null;

        if (!TokenCount.TryParse(sent.Trim(), out var n))
            return $"{flag} takes {TokenCount.Refusal} - not \"{sent.Trim()}\"";

        count = n;
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

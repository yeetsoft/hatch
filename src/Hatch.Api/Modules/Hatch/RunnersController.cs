using System.Globalization;
using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Models.Auth;
using Hatch.Api.Models.HatchRevision;
using Hatch.Api.Services;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Settings;
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
[Route("api/runners")]
public class RunnersController(
    HatchContext db, Runners runners, IssueClaims claims, ICallerIdentity caller, IActorDirectory actors,
    TimeProvider time, IOptions<AuthOptions> auth, ISiteSettingsService siteSettings, IAuthService keys,
    RunnerSessions sessions, Dispatch dispatch, IHatchRevision revision, IRunnerBuilds builds,
    RunnerStateLog stateLog, ILogger<RunnersController> logger) : ControllerBase
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
        var visibleKeys = await VisibleKeysAsync(ct);
        var statusIds = await StatusIdsAsync(ct);
        var localModels = await LocalModelsAsync(ct);
        var openFailures = await OpenFailuresAsync(ct);

        var dtos = new List<RunnerDto>(visible.Count);
        foreach (var r in visible)
            dtos.Add(await ProjectAsync(r, held, drivers, wallEnabled, callerPerson, site, visibleKeys, statusIds, localModels, openFailures, now, ct));

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

        return await ProjectAsync(found, held, drivers, wallEnabled, callerPerson, site, await VisibleKeysAsync(ct), await StatusIdsAsync(ct), await LocalModelsAsync(ct), await OpenFailuresAsync(ct), now, ct);
    }

    /// <summary>
    /// What one visible runner cannot do now and could not do earlier, or
    /// <c>404</c> for a name this caller may not see - the same rule
    /// <see cref="GetRunner"/> reads under.
    /// </summary>
    /// <remarks>
    /// <para><c>Current</c> is the key's open rows at a checkout the runner is
    /// being heard from at; <c>Earlier</c> is its ended rows, newest ended first,
    /// at most fifty. An open row of a runner gone quiet is in neither: its next
    /// beat settles it. A name with no key has no rows and answers two empty
    /// lists. This read writes nothing.</para>
    /// </remarks>
    [HttpGet("{name}/failures")]
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<ActionResult<RunnerFailuresDto>> GetRunnerFailures(string name, CancellationToken ct)
    {
        if (Named(name) is not { } runner) return BadRequest(NameRefusal);

        var now = time.GetUtcNow();
        var (all, personId) = await VisibleAsync(ct);

        if (db.Database.IsRelational()) await sessions.MarkAllGoneAsync(db, now, ct);

        var joined = await Runner.JoinAsync(db, keys, ct);
        var found = joined.FirstOrDefault(r => string.Equals(r.Name, runner, StringComparison.Ordinal));
        if (found is null || !(all || found.OwnerPersonId == personId)) return NotFound();

        if (found.Key is not { } key) return new RunnerFailuresDto([], []);

        var open = await OpenFailuresAsync(ct);
        return new RunnerFailuresDto(
            RunnerFailures.Current(open.Rows, found, sessions, now, open.Tickets),
            await RunnerFailures.EarlierAsync(db, key.Id, ct));
    }

    /// <summary>
    /// The states one visible runner was in over a range, as stretches, oldest
    /// first. <c>from</c> and <c>to</c> default to the last 24 hours; there is
    /// no cap on a longer range.
    /// </summary>
    /// <remarks>
    /// <para>A stretch is kept when it overlaps the range, and is not clipped to
    /// it: the one already open at <c>from</c> starts where it began, and the
    /// one still standing has a null <c>to</c>. Two adjacent rows in the same
    /// state are one stretch.</para>
    ///
    /// <para>This read writes nothing. What the clock has ended since the last
    /// row - an exhausted stretch that ran out, a runner gone quiet - is added
    /// in memory by <see cref="RunnerStateLog.ClockTailAsync"/>, so a lapsed
    /// stretch reads right with no sweep. The same name and visibility rules as
    /// <see cref="GetRunner"/>, and <c>404</c> for a runner nobody has or this
    /// caller may not see.</para>
    /// </remarks>
    [HttpGet("{name}/states")]
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<ActionResult<IReadOnlyList<RunnerStateStretchDto>>> GetRunnerStates(
        string name, CancellationToken ct, [FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        if (Named(name) is not { } runner) return BadRequest(NameRefusal);

        var now = time.GetUtcNow();
        if (RangeBounds.Resolve(from, to, now, TimeSpan.FromHours(24), out var start, out var end) is { } refusal)
            return BadRequest(refusal);

        var (all, personId) = await VisibleAsync(ct);

        var joined = await Runner.JoinAsync(db, keys, ct);
        var found = joined.FirstOrDefault(r => string.Equals(r.Name, runner, StringComparison.Ordinal));
        if (found is null || !(all || found.OwnerPersonId == personId)) return NotFound();

        var rows = await db.RunnerStates.AsNoTracking()
            .Where(s => s.Runner == runner)
            .OrderBy(s => s.At).ThenBy(s => s.Id)
            .ToListAsync(ct);

        // The clock's tail: what ran out since the last row, held in memory.
        var tail = rows.Count > 0
            ? await stateLog.ClockTailAsync(db, keys, runner, rows[^1].At, now, ct)
            : [];
        var changes = RunnerStateRule.Timeline(rows.Select(r => new RunnerStateRule.Change(r.At, r.State, r.Kind)).ToList(), tail);

        var stretches = new List<RunnerStateStretchDto>(changes.Count);
        for (var i = 0; i < changes.Count; i++)
        {
            var (at, state, kind) = changes[i];
            DateTimeOffset? until = i + 1 < changes.Count ? changes[i + 1].At : null;

            if (at <= end && (until is null || until > start))
                stretches.Add(new RunnerStateStretchDto(state, kind, at, until));
        }

        return stretches;
    }

    /// <summary>
    /// Mints a runner: a key and the config row beside it, answered once with
    /// the secret. A signed-in user may mint one for themselves and an admin
    /// for anyone; never a key - an agent that could mint runners could mint
    /// itself more of whatever a runner may do.
    /// </summary>
    /// <remarks>
    /// Every refusal comes before anything is written: the gate, the name, the
    /// owner and the tags are all judged first, so a refused request leaves no
    /// key and no row. <c>suggest-name</c> is refused as a name because it is
    /// the literal route beside <c>{name}</c>, and a runner called that could
    /// not be reached by its own URL.
    ///
    /// <para>The owner rule is read here and not in the role attribute, which
    /// reads only a role. Where the wall is on, a caller below admin owns what
    /// they mint: naming anybody else is a 403, judged before the person is
    /// looked up so a user cannot probe which ids exist, and a runner with no
    /// owner would see every project, so none is written for a user. With the
    /// wall off there are no roles and the rule is not read.</para>
    ///
    /// <para>The key's uniqueness is <see cref="IAuthService"/>'s to judge, and
    /// it logs the mint too - so there is no logger here. The config row is
    /// written even with no tags, so a minted runner always has one.</para>
    /// </remarks>
    [HttpPost]
    [RequireRole(PersonRole.User)]
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
        // the wall is off, as the key route this was ported from does. Below
        // admin, with the wall on, the owner is the caller and nobody else.
        var wallEnabled = auth.Value.Enabled;
        var callerPerson = wallEnabled ? await caller.PersonAsync(ct) : null;
        var mustOwn = wallEnabled && callerPerson?.Role is not >= PersonRole.Admin;

        Guid? ownerPersonId;
        if (request?.OwnerPersonId is { } named)
        {
            if (mustOwn && callerPerson?.Id != named) return OnlyAnAdminMintsForOthers();
            if (await actors.ResolveAsync(ActorKind.Person, named, ct) is null)
                return BadRequest("that person does not exist");
            ownerPersonId = named;
        }
        else
        {
            if (mustOwn && callerPerson is null) return OnlyAnAdminMintsForOthers();
            ownerPersonId = (callerPerson ?? await caller.PersonAsync(ct))?.Id;
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
        var dto = await ProjectAsync(
            new Runner(created.Key, config, null), [], new Dictionary<string, IReadOnlyList<string>>(),
            wallEnabled, callerPerson, await siteSettings.GetAsync(ct), await VisibleKeysAsync(ct), await StatusIdsAsync(ct), await LocalModelsAsync(ct), OpenFailures.None, time.GetUtcNow(), ct);

        return new RunnerMintedDto(dto, created.Secret);
    }

    /// <summary>The 403 for a caller below admin who names somebody else as a new runner's owner, or who has no person to default to.</summary>
    private static ObjectResult OnlyAnAdminMintsForOthers() =>
        new("only an admin may create a runner for somebody else") { StatusCode = StatusCodes.Status403Forbidden };

    /// <summary>
    /// A cast-list name no key and no heartbeat row holds, for the form that
    /// mints a runner to offer. Revoked keys count: a name stays unique.
    /// </summary>
    /// <remarks>
    /// The set is case-insensitive itself, since <see cref="RunnerCast.Suggest"/>
    /// only asks it <c>Contains</c>. A literal route, so it beats <c>{name}</c>.
    /// </remarks>
    [HttpGet("suggest-name")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<string>> SuggestRunnerName(CancellationToken ct)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in await keys.ListApiKeysAsync(ct)) taken.Add(key.Name);
        foreach (var beat in await db.LegacyRunners.AsNoTracking().Select(r => r.Name).ToListAsync(ct)) taken.Add(beat);

        return RunnerCast.Suggest(taken);
    }

    /// <summary>The keys of the projects this caller may see, read once per request: a project outside their scope is not named to them.</summary>
    private async Task<IReadOnlySet<string>> VisibleKeysAsync(CancellationToken ct) =>
        (await db.Projects.Select(p => p.Key).ToListAsync(ct)).ToHashSet();

    /// <summary>The board's own column ids, read once per request: a stale id a column rule still names is dropped on read by whoever threads this through.</summary>
    private async Task<IReadOnlySet<int>> StatusIdsAsync(CancellationToken ct) =>
        (await db.Statuses.Select(s => s.Id).ToListAsync(ct)).ToHashSet();

    /// <summary>The local-models table, oldest first, read once per request and threaded through <see cref="ProjectAsync"/>.</summary>
    private Task<List<EfHatchLocalModel>> LocalModelsAsync(CancellationToken ct) =>
        db.LocalModels.AsNoTracking().OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToListAsync(ct);

    /// <summary>Every open failure row, read once per request and threaded through <see cref="ProjectAsync"/>; <see cref="RunnerFailures.Current"/> says which of them are current.</summary>
    private async Task<OpenFailures> OpenFailuresAsync(CancellationToken ct)
    {
        var rows = await db.RunnerFailures.AsNoTracking().Where(f => f.EndedAt == null).ToListAsync(ct);
        return new OpenFailures(rows, await RunnerFailures.TicketsAsync(db, rows.Select(r => r.Id).ToList(), ct));
    }

    /// <summary>The open rows, and the issue keys linked to them - see <see cref="OpenFailuresAsync"/>.</summary>
    private sealed record OpenFailures(IReadOnlyList<EfHatchRunnerFailure> Rows, IReadOnlyDictionary<long, IReadOnlyList<string>> Tickets)
    {
        public static readonly OpenFailures None = new([], new Dictionary<long, IReadOnlyList<string>>());
    }

    /// <summary>One joined row as the wire shape, resolving the owner's name and the control bits that take it.</summary>
    private async Task<RunnerDto> ProjectAsync(
        Runner runner, Dictionary<(string Runner, string Where), (string Key, ClaimSnapshot Claim, string? Status)> held,
        IReadOnlyDictionary<string, IReadOnlyList<string>> drivers, bool wallEnabled, EfPerson? callerPerson,
        SiteSettingsSnapshot site, IReadOnlySet<string> visibleKeys, IReadOnlySet<int> statusIds, IReadOnlyList<EfHatchLocalModel> localModels,
        OpenFailures openFailures, DateTimeOffset now, CancellationToken ct)
    {
        var ownerId = runner.OwnerPersonId;
        var forName = ownerId is { } id ? (await actors.ResolveAsync(ActorKind.Person, id, ct))?.Name : null;
        var canControl = CanControl(ownerId, wallEnabled, callerPerson);
        var order = runner.Beat?.OrderKind is { Length: > 0 } ok ? new RunnerOrderDto(ok, runner.Beat.OrderKey, runner.Beat.OrderedBy, runner.Beat.OrderedAt) : null;

        var dto = runners.Project(
            runner, Holding(held, (runner.Name, runner.Beat?.Where ?? "")), drivers.GetValueOrDefault(runner.Name, []), forName, canControl, order,
            yours: Yours(ownerId, wallEnabled, callerPerson),
            defaultMaxTokens: site.RunnerDefaultMaxTokens, defaultMaxTokensPerHour: site.RunnerDefaultMaxTokensPerHour);

        // A column deleted since this rule was written is dropped on read
        // rather than carried as a ghost id nothing on the board can show.
        dto = dto with { StatusIds = dto.StatusIds.Where(statusIds.Contains).ToList() };

        // A beat-less key with no legacy row still has what its live loop
        // sessions declared, the same union RunnerDeclaration.OfAsync folds.
        var loop = runner.Sessions?.Where(s => sessions.IsLive(s, now) && s.Kind == EfHatchRunner.LoopKind).ToList() ?? [];
        if (dto.Models.Count == 0) dto = dto with { Models = RunnerDeclaration.Declared(null, loop.Select(s => s.Models)) };
        if (dto.Has.Count == 0) dto = dto with { Has = RunnerDeclaration.Declared(null, loop.Select(s => s.Has)) };

        // Whether this runner signs its commits is a fact about each loop's
        // own checkouts, so the runner reads as the worst of its live loops.
        if (dto.Signing is null &&
            RunnerDeclaration.SigningOf(runner.Name, loop) is { State: { } signingState } signing)
            dto = dto with { Signing = signingState, SigningWhy = signing.Why };

        // The forge sign-in reads the same way: the worst of the live loops.
        if (dto.Forge is null && RunnerDeclaration.ForgeOf(loop) is { } forge)
            dto = dto with { Forge = forge.State, ForgeWhy = forge.Why };

        // What this runner cannot do right now: its open failure rows at a
        // checkout it is being heard from at, none for a runner gone quiet.
        dto = dto with { Failures = RunnerFailures.Current(openFailures.Rows, runner, sessions, now, openFailures.Tickets) };

        // A keyed runner with no legacy row has no row-level accelerator fact
        // at all, so the newest live loop session's own carries it - the same
        // fallback PutRefusedModels makes for the agent alone.
        var newestLoop = loop.OrderByDescending(s => s.LastSeenAt).ThenByDescending(s => s.Id).FirstOrDefault();
        dto = dto with
        {
            Hold = LocalModelFit.Hold(
                localModels, dto.Agent ?? newestLoop?.Agent, dto.AcceleratorMemoryBytes ?? newestLoop?.AcceleratorMemoryBytes, dto.RefusedModels),
        };

        // What the newest live loop's last heartbeat was answered with, kept
        // beside the election the page runs again: where the two differ, the
        // loop was told something the page would not say.
        if (loop.Where(s => s.DrivesTold is not null).OrderByDescending(s => s.LastSeenAt).ThenByDescending(s => s.Id).FirstOrDefault() is { } told)
        {
            // That session's own scope wrote the list, so this viewer keeps only the keys they can see.
            dto = dto with { DrivesTold = told.DrivesTold!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(visibleKeys.Contains).ToList() };
        }

        if (runner.Sessions is not { Count: > 0 } own) return dto;

        // Since the claim moved onto the session, a keyed runner has no beat
        // `where` for the row's own lookup to hit. Fold the claim and the
        // usage facts up from its live sessions, the way Models and Has are.
        var live = own.Where(s => sessions.IsLive(s, now)).ToList();
        if (dto.ClaimKey is null)
        {
            var holding = live
                .OrderByDescending(s => s.LastSeenAt).ThenByDescending(s => s.Id)
                .Select(s => Holding(held, (runner.Name, s.Where ?? "")))
                .FirstOrDefault(h => h is not null);
            if (holding is { } h)
                dto = dto with { ClaimKey = h.Key, Line = h.Claim.Chatter, LineAt = h.Claim.ChatterAt, ClaimStatus = h.Status };
        }

        if (!(dto.ExhaustedUntil > now) && Latest(live.Select(s => s.ExhaustedUntil), now) is { } exhausted)
            dto = dto with { ExhaustedUntil = exhausted };
        if (!(dto.ThrottledUntil > now) && Latest(live.Select(s => s.ThrottledUntil), now) is { } throttled)
            dto = dto with { ThrottledUntil = throttled };

        // A keyed runner has no beat row of its own, so a live session
        // pulling is the only way the row ever shows it.
        if (!dto.Pulling && live.Any(s => s.Pulling)) dto = dto with { Pulling = true };

        // Copied whole from one session - never a kind from one session and a
        // line from another - so the latest ahead of now wins all three.
        if (!(dto.AgentDownUntil > now) &&
            live.Where(s => s.AgentDownUntil > now).OrderByDescending(s => s.AgentDownUntil).FirstOrDefault() is { } agentDown)
        {
            dto = dto with { AgentDownUntil = agentDown.AgentDownUntil, AgentDownKind = agentDown.AgentDownKind, AgentDownLine = agentDown.AgentDownLine };
        }

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

        return await HeartbeatSessionAsync(key, request, ct);
    }

    /// <summary>
    /// The body of a key-bearing beat; <see cref="SessionHeartbeat"/>'s own.
    /// </summary>
    private async Task<ActionResult<RunnerInstructionDto>> HeartbeatSessionAsync(EfApiKey key, RunnerHeartbeatRequest request, CancellationToken ct)
    {
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

        // Before anything is written: the sweep below ends sessions and the beat
        // overwrites the facts a quiet gap is judged by.
        await stateLog.SettleAsync(db, keys, key.Name, now, ct);

        await sessions.MarkGoneAsync(db, key.Id, now, ct);

        var live = await db.RunnerSessions
            .Where(s => s.ApiKeyId == key.Id && s.EndedAt == null && s.Where == where)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (request.Fresh == true && kind == EfHatchRunner.LoopKind && live is not null)
        {
            var goneIn = (int)Math.Ceiling((live.LastSeenAt.AddSeconds(runners.GoneAfterSeconds) - now).TotalSeconds);
            return Conflict(new RunnerSessionRefusalDto(
                $"{key.Name} is already the runner on {where ?? "an unnamed checkout"}",
                live.LastSeenAt,
                Math.Max(1, goneIn)));
        }

        EfHatchRunnerSession session;
        if (live is null)
        {
            session = SeedSession(key.Id, kind, where, line, request, now, Joined(normalisedModels), Joined(normalisedHas), ip);

            // A pause the board wrote outlives the row it was written on: a
            // loop restarted into the same checkout is the same loop.
            var paused = await db.RunnerSessions.AsNoTracking()
                .Where(s => s.ApiKeyId == key.Id && s.Where == where
                    && s.AgentDownKind == RunnerAgentDownKinds.BoardPaused && s.AgentDownUntil > now)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(ct);
            if (paused is not null)
            {
                session.AgentDownUntil = paused.AgentDownUntil;
                session.AgentDownKind = paused.AgentDownKind;
                session.AgentDownLine = paused.AgentDownLine;
            }

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

        if (kind == EfHatchRunner.LoopKind)
        {
            // What this beat found failing, kept; a `once` beat touches nothing.
            await RunnerFailures.ReconcileAsync(db, key.Id, session, request, now, ct);

            foreach (var retired in await RetireRenamedAsync(key.Name, key.OwnerPersonId, where, now, ct))
                await stateLog.RecordAsync(db, keys, retired, now, ct);

            // Before the config read below, so this beat's instruction already
            // carries what was carried.
            await CarryTagsAsync(key.Id, key.Name, now, ct);
        }

        var drives = (await TrainDriversAsync(key.Name, now, ct)).GetValueOrDefault(key.Name, []);

        // What this loop is being told, kept: the Runners page runs the
        // election again on every read, and the two differing is how a loop
        // told it drives nothing is found out. "" is told nothing.
        session.DrivesTold = string.Join('\n', drives);
        await db.SaveChangesAsync(ct);

        // The one record for this key's runner, however many sessions it holds.
        // It also reads the retirement RetireRenamedAsync cleared on this name.
        await stateLog.RecordAsync(db, keys, key.Name, now, ct);

        var config = await db.RunnerConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.ApiKeyId == key.Id, ct);
        var site = await siteSettings.GetAsync(ct);
        var forName = (await ForAsync(ct))?.Name ?? key.Name;
        var localModels = await LocalModelsAsync(ct);

        return RunnerSessions.Instruct(
            session, config, key.Name, forName, drives, site.RunnerDefaultMaxTokens, site.RunnerDefaultMaxTokensPerHour, localModels, now)
            with { BoardBuild = await BoardBuildAsync(request, ct) };
    }

    /// <summary>
    /// <see cref="Runners.BoardBuild"/> for this beat, with the build its platform
    /// names. The lookup is made only once the part is known to exist, since the
    /// first one hashes the file.
    /// </summary>
    private async Task<RunnerBoardBuildDto?> BoardBuildAsync(RunnerHeartbeatRequest request, CancellationToken ct)
    {
        if (Runners.BoardBuild(revision.Revision, revision.Sequence, request, null) is null) return null;

        ct.ThrowIfCancellationRequested();
        var build = request.Rid is { } rid ? await builds.FindAsync(rid) : null;
        return Runners.BoardBuild(revision.Revision, revision.Sequence, request, build);
    }

    /// <summary>
    /// Ends one of the calling key's own sessions - a <c>go-to-work</c> loop
    /// saying it is leaving, so a fresh loop started straight afterwards in
    /// the same checkout is not refused for a process that has already exited.
    /// </summary>
    /// <remarks>
    /// <para>The bearer key's alone, like <see cref="SessionHeartbeat"/>. A
    /// session that is not this key's, or does not exist, is <c>404</c>. An
    /// ended session is <c>204</c> again and keeps its first <c>EndedAt</c>,
    /// so the call is safe to repeat. A later fresh beat finds no live row and
    /// seeds a new one.</para>
    ///
    /// <para>A plain read and write, not <c>ExecuteUpdateAsync</c>, for the
    /// reason <c>FanOutAsync</c> gives.</para>
    /// </remarks>
    [HttpPost("sessions/{id:int}/end")]
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<IActionResult> EndSession(int id, CancellationToken ct)
    {
        if (await caller.ApiKeyAsync(ct) is not { } key)
            return StatusCode(403, "ending a session is the bearer key's, not a name or a header");

        var session = await SessionNamedAsync(key.Id, id, ct);
        if (session is null) return NotFound();

        if (session.EndedAt is null)
        {
            var now = time.GetUtcNow();
            await stateLog.SettleAsync(db, keys, key.Name, now, ct);

            session.EndedAt = now;
            await db.SaveChangesAsync(ct);
            await stateLog.RecordAsync(db, keys, key.Name, now, ct);
        }

        return NoContent();
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

        var row = await db.LegacyRunners.FirstOrDefaultAsync(r => r.Name == runner, ct);

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
        var localModels = await LocalModelsAsync(ct);

        // Before anything is written: the seed and the touch below overwrite
        // the facts a quiet gap is judged by. Once, for both exits - the
        // insert-race catch falls through to the same touch.
        await stateLog.SettleAsync(db, keys, runner, now, ct);

        if (row is null)
        {
            db.LegacyRunners.Add(row = Seed(runner, kind, line, request, now, forActor?.Id, Joined(normalisedModels), Joined(normalisedHas)));

            var seeded = false;
            var retired = new List<string>();
            try
            {
                // Before the save, so the one write carries the new row and
                // the retirements together.
                retired = await RetireSuperseded(row, now, ct);
                await db.SaveChangesAsync(ct);
                seeded = true;
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
                row = await db.LegacyRunners.FirstOrDefaultAsync(r => r.Name == runner, ct);
                if (row is null) throw;
            }

            if (seeded)
            {
                await RecordAsync(runner, retired, now, ct);

                // A row born on this very beat has never been ordered.
                var refusedModels = await RefusedFromKeyAsync(ct);
                return Runners.Instruct(row, forName, (await TrainDriversAsync(runner, now, ct)).GetValueOrDefault(row.Name, []), null,
                    defaultMaxTokens: site.RunnerDefaultMaxTokens, defaultMaxTokensPerHour: site.RunnerDefaultMaxTokensPerHour)
                    with { RefusedModels = refusedModels, Hold = LocalModelFit.Hold(localModels, row.Agent, row.AcceleratorMemoryBytes, refusedModels),
                        BoardBuild = await BoardBuildAsync(request, ct) };
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
        var retiredNow = await RetireSuperseded(row, now, ct);
        await db.SaveChangesAsync(ct);
        await RecordAsync(runner, retiredNow, now, ct);

        var refused = await RefusedFromKeyAsync(ct);
        return Runners.Instruct(row, forName, (await TrainDriversAsync(runner, now, ct)).GetValueOrDefault(row.Name, []), order,
            defaultMaxTokens: site.RunnerDefaultMaxTokens, defaultMaxTokensPerHour: site.RunnerDefaultMaxTokensPerHour)
            with { RefusedModels = refused, Hold = LocalModelFit.Hold(localModels, row.Agent, row.AcceleratorMemoryBytes, refused),
                BoardBuild = await BoardBuildAsync(request, ct) };
    }

    /// <summary>
    /// The calling key's own held refusals, for <see cref="RunnerInstructionDto.RefusedModels"/>
    /// on this route - <c>null</c> for a caller with no key, since nothing on
    /// the board is its to hold, and <c>[]</c> for a key with no config row
    /// yet, the same way <see cref="RefusedModels"/> on the read answers.
    /// </summary>
    private async Task<IReadOnlyList<string>?> RefusedFromKeyAsync(CancellationToken ct)
    {
        if (await caller.ApiKeyAsync(ct) is not { } key) return null;

        var config = await db.RunnerConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.ApiKeyId == key.Id, ct);
        return config is null ? [] : RunnerTagColumn.Read(config.RefusedModels).Order(StringComparer.Ordinal).ToArray();
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
    private async Task<List<string>> RetireSuperseded(EfHatchRunner row, DateTimeOffset now, CancellationToken ct)
    {
        if (row.Where is not { Length: > 0 } where) return [];
        if (row.ForPersonId is null && auth.Value.Enabled) return [];

        var name = row.Name;
        var kind = row.Kind;
        var owner = row.ForPersonId;
        var first = row.FirstSeenAt;
        var cutoff = runners.Cutoff(now);

        var older = await db.LegacyRunners.Where(r => r.Name != name && r.Where == where && r.Kind == kind
            && r.ForPersonId == owner && r.FirstSeenAt < first && r.LastSeenAt < cutoff
            && r.RetiredAt == null).ToListAsync(ct);

        foreach (var r in older)
        {
            r.RetiredAt = now;
            r.RetiredBy = name;
        }

        return older.Select(r => r.Name).ToList();
    }

    /// <summary>
    /// Records the beating runner, then every name the beat retired - each after
    /// the save that carried it, so a retired runner reads gone from its last beat.
    /// </summary>
    private async Task RecordAsync(string runner, IEnumerable<string> retired, DateTimeOffset now, CancellationToken ct)
    {
        await stateLog.RecordAsync(db, keys, runner, now, ct);
        foreach (var name in retired) await stateLog.RecordAsync(db, keys, name, now, ct);
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
    private async Task<List<string>> RetireRenamedAsync(string name, Guid? owner, string? where, DateTimeOffset now, CancellationToken ct)
    {
        if (where is not { Length: > 0 }) return [];
        var retired = new List<string>();

        var own = await db.LegacyRunners.FirstOrDefaultAsync(r => r.Name == name && r.RetiredAt != null, ct);
        if (own is not null)
        {
            own.RetiredAt = null;
            own.RetiredBy = null;
        }

        if (owner is not null || !auth.Value.Enabled)
        {
            var older = await db.LegacyRunners.Where(r => r.Name != name && r.Where == where
                && r.Kind == EfHatchRunner.LoopKind && r.ForPersonId == owner && r.RetiredAt == null).ToListAsync(ct);

            foreach (var r in older)
            {
                r.RetiredAt = now;
                r.RetiredBy = name;
                retired.Add(r.Name);
            }
        }

        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
        return retired;
    }

    /// <summary>
    /// Carries the tags older runner rows held onto a key that has never had a
    /// config row - the upgrade cliff HA-2042 left. A runner's tags live on its
    /// key's config now, and the migration that made the table left it empty,
    /// so a key whose rows were tagged would otherwise come up untagged the
    /// moment those rows retired. Once only: a config row that exists, even
    /// one with no tags, is the operator's and is never touched, which is how
    /// a deliberate "none" is told apart from "never set". A union with no
    /// tags in it creates nothing, for the same reason - an empty row would
    /// turn "never set" into "none". Reads the key's own-named row and every
    /// row the key's name retired, whether this beat retired it or an earlier
    /// one did; the most recently heard rows win when the union passes
    /// <see cref="RunnerTags.MaxCount"/>.
    /// </summary>
    private async Task CarryTagsAsync(Guid keyId, string keyName, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.RunnerConfigs.AnyAsync(c => c.ApiKeyId == keyId, ct)) return;

        var rows = await db.LegacyRunners.AsNoTracking()
            .Where(r => (r.RetiredBy == keyName || r.Name == keyName) && r.Tags != null && r.Tags != "")
            .OrderByDescending(r => r.LastSeenAt)
            .Select(r => new { r.Name, r.Tags })
            .ToListAsync(ct);

        var union = new List<string>();
        var from = new List<string>();
        foreach (var row in rows)
        {
            var carried = false;
            foreach (var raw in row.Tags!.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // One at a time, so a tag the rule refuses is skipped rather
                // than costing the rest.
                if (RunnerTags.Normalise([raw], out var one) is not null || one.Count == 0) continue;
                if (union.Count >= RunnerTags.MaxCount || union.Contains(one[0])) continue;

                union.Add(one[0]);
                carried = true;
            }

            if (carried) from.Add(row.Name);
        }

        if (union.Count == 0) return;

        var config = new EfHatchRunnerConfig { ApiKeyId = keyId, Tags = RunnerTagColumn.Write(union), UpdatedAt = now };
        db.RunnerConfigs.Add(config);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two checkouts on one key can both find no config and both insert
            // one. The loser stands down: the winner's row is what the
            // instruction read finds next.
            db.Entry(config).State = EntityState.Detached;
            return;
        }

        logger.LogInformation("Carried runner tags {Tags} onto key {Key}'s config from {Rows}",
            string.Join(',', union), keyName, string.Join(',', from));
    }

    /// <summary>
    /// This runner's own beat, reinstating any project it had been passed over
    /// on - but only where nobody else is live to take the head. A beat is proof
    /// the runner is alive, not that it is acting, so while another eligible
    /// runner could drive the project the pass-over stands until that runner is
    /// passed over in turn, or the head changes (<see cref="TrainWatch.Observe"/>
    /// clears it). Where this runner is the only one left, it is given a fresh
    /// window rather than being passed over again on the very call that just
    /// reinstated it - HA-2092's correction 2.
    /// </summary>
    private async Task ReinstateAsync(string runner, DateTimeOffset now, CancellationToken ct)
    {
        var passedOverOn = await db.Projects.Where(p => p.TrainStalledDriver == runner).ToListAsync(ct);
        if (passedOverOn.Count == 0) return;

        var eligible = await runners.EligibleNamesAsync(db, keys, now, ct);
        var changed = false;

        foreach (var project in passedOverOn)
        {
            if (eligible.TryGetValue(project.Id, out var names) && names.Any(n => n != runner)) continue;

            project.TrainStalledDriver = null;
            if (project.TrainWatchedIssueId is not null) project.TrainWatchedSince = now;
            changed = true;
        }

        if (changed) await db.SaveChangesAsync(ct);
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

            // The latest alone is cleared the moment it heartbeats; the roster
            // is what lets the attention page see every runner has failed.
            var roster = RunnerTagColumn.Read(project.TrainStalledDrivers).ToList();
            if (!roster.Contains(passedOver, StringComparer.Ordinal))
            {
                roster.Add(passedOver);
                project.TrainStalledDrivers = RunnerTagColumn.Write(roster);
            }
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
    /// ones <c>ForPersonId</c> ties to this caller. The rule is
    /// <see cref="RunnerReach"/>'s, which the board health read keeps too.
    /// </summary>
    private Task<(bool All, Guid? PersonId)> VisibleAsync(CancellationToken ct) =>
        RunnerReach.OfAsync(auth.Value.Enabled, caller, actors, ct);

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
            if (!IssueKey.TryParse(under, out var underProject, out var underNumber))
                return BadRequest($"--under names an issue, like AER-12 - not \"{wanted.Trim()}\"");

            // A key is a pointer to an issue the caller can see. One outside their scope
            // answers as one that does not exist, so the 404 tells them nothing.
            if (!await db.Issues.WithKey(underProject, underNumber).AnyAsync(ct))
                return NotFound($"there is no issue {under}");
        }

        if (Number(request.MaxRuns, out var maxRuns) is { } runsError) return BadRequest(runsError);
        if (Money(request.MaxSpend, out var maxSpend) is { } spendError) return BadRequest(spendError);
        if (Tokens(request.MaxTokens, "--max-tokens", out var maxTokens) is { } tokensError) return BadRequest(tokensError);
        if (Tokens(request.MaxTokensPerHour, "--max-tokens-per-hour", out var maxTokensPerHour) is { } tokensPerHourError) return BadRequest(tokensPerHourError);
        if (RunnerTags.Normalise(request.Tags, out var normalisedTags) is { } tagsRefusal) return BadRequest(tagsRefusal);

        if (request.StatusMode is null != request.StatusIds is null)
            return BadRequest("a runner's column rule takes a mode and a list together");

        if ((request.StatusMode is not null || request.StatusIds is not null) && aggregate.Key is null)
            return BadRequest($"{runner} has no key - its column rule has nowhere to live");

        if (request.Preferred is not null && aggregate.Key is null)
            return BadRequest($"{runner} has no key - its preferred flag has nowhere to live");

        if (RunnerStatusMode.Normalise(request.StatusMode, out var normalisedStatusMode) is { } statusModeRefusal)
            return BadRequest(statusModeRefusal);

        var normalisedStatusIds = Array.Empty<int>();
        if (request.StatusIds is not null && normalisedStatusMode != RunnerStatusMode.All)
        {
            var distinctIds = request.StatusIds.Distinct().ToArray();
            var columnIds = await StatusIdsAsync(ct);
            foreach (var id in distinctIds)
                if (!columnIds.Contains(id)) return BadRequest($"there is no column {id}");

            normalisedStatusIds = distinctIds;
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

        // Before the state is written over: a stretch the clock ended is judged by it.
        if (state is not null) await stateLog.SettleAsync(db, keys, runner, now, ct);

        // The heartbeat row is tracked and may be absent: a key that has never
        // run has none. State lives only there, so with none it is skipped
        // rather than refused.
        var beatRow = await db.LegacyRunners.FirstOrDefaultAsync(r => r.Name == runner, ct);
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
                || request.Tags is not null || request.StatusMode is not null || request.Preferred is not null;

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
                if (request.StatusMode is not null)
                {
                    config.StatusMode = normalisedStatusMode;
                    config.StatusIds = RunnerStatusIdsColumn.Write(normalisedStatusIds);
                }
                if (request.Preferred is { } preferred) config.Preferred = preferred;

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
        if (state is not null) await stateLog.RecordAsync(db, keys, runner, now, ct);

        return await ProjectAsync(
            new Runner(aggregate.Key, config ?? aggregate.Config, beatRow, aggregate.Sessions),
            await runners.HeldAsync(db, claims, now, ct), await runners.DriversAsync(db, keys, now, ct),
            wallEnabled, callerPerson, await siteSettings.GetAsync(ct), await VisibleKeysAsync(ct), await StatusIdsAsync(ct), await LocalModelsAsync(ct), await OpenFailuresAsync(ct), now, ct);
    }

    /// <summary>
    /// Replaces the models a runner's key refuses, whole. The one write on
    /// this controller a key may take on itself: a refusal only narrows what a
    /// runner will be told to run, never widens its own budget the way every
    /// other field <see cref="PatchRunner"/> guards does - so letting a key
    /// hold its own list is not the hole <see cref="NotAPerson"/> exists to
    /// close.
    /// </summary>
    /// <remarks>
    /// A person who can control the row may write it too, same as every other
    /// bound - <see cref="CanControl"/>. A key naming a runner that is not its
    /// own is refused, since a key with no owner otherwise reads as
    /// everybody's to control.
    /// </remarks>
    [HttpPut("{name}/refused-models")]
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<ActionResult<RunnerDto>> PutRefusedModels(
        string name, [FromBody] RunnerRefusedModelsWriteRequest? request, CancellationToken ct)
    {
        if (Named(name) is not { } runner) return BadRequest(NameRefusal);

        var aggregate = (await Runner.JoinAsync(db, keys, ct)).FirstOrDefault(r => string.Equals(r.Name, runner, StringComparison.Ordinal));
        if (aggregate?.Key is not { } key) return NotFound();

        var now = time.GetUtcNow();
        if (!key.IsLive(now)) return Conflict($"{runner}'s key is revoked");

        var wallEnabled = auth.Value.Enabled;
        if (await caller.ApiKeyAsync(ct) is { } callerKey)
        {
            if (!string.Equals(callerKey.Name, runner, StringComparison.Ordinal))
            {
                var forName = key.OwnerPersonId is { } forId
                    ? (await actors.ResolveAsync(ActorKind.Person, forId, ct))?.Name ?? key.Name
                    : key.Name;
                return new ObjectResult($"{runner} is {forName}'s runner - a key may write only its own")
                    { StatusCode = StatusCodes.Status403Forbidden };
            }
        }
        else
        {
            var callerPerson = wallEnabled ? await caller.PersonAsync(ct) : null;
            if (!CanControl(key.OwnerPersonId, wallEnabled, callerPerson))
                return await ControlRefusalAsync(runner, key.OwnerPersonId, ct);
        }

        if (request?.Models is null) return BadRequest("models is required - send [] to clear");

        var agent = aggregate.Beat?.Agent;
        if (string.IsNullOrWhiteSpace(agent))
        {
            agent = aggregate.Sessions?
                .Where(s => sessions.IsLive(s, now))
                .OrderBy(s => s.Id)
                .FirstOrDefault()?.Agent;
        }

        if (RunnerModels.Normalise(request.Models, agent, out var models) is { } modelsRefusal) return BadRequest(modelsRefusal);

        var written = RunnerTagColumn.Write(models);
        var config = await db.RunnerConfigs.FirstOrDefaultAsync(c => c.ApiKeyId == key.Id, ct);
        if ((config is null && models.Count == 0) || config?.RefusedModels == written)
        {
            // Nothing held, nothing sent, or exactly what is already held -
            // a no-op writes nothing and leaves UpdatedAt alone.
        }
        else
        {
            if (config is null)
                db.RunnerConfigs.Add(config = new EfHatchRunnerConfig { ApiKeyId = key.Id, UpdatedAt = now });

            config.RefusedModels = written;
            config.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await AnswerAsync(runner, ct);
    }

    /// <summary>
    /// Stops a runner's key working, by the runner's name. The row stays - the
    /// audit trail names this key, and a revoked runner is still listed - so
    /// this is a <c>POST</c>, not a <c>DELETE</c>. The runner's owner, or an
    /// admin, and never a key.
    /// </summary>
    [HttpPost("{name}/revoke")]
    [RequireRole(PersonRole.User)]
    public async Task<IActionResult> RevokeRunner(string name, CancellationToken ct)
    {
        if (Named(name) is not { } runner) return BadRequest(NameRefusal);
        if (await KeyNamedAsync(runner, ct) is not { } key) return NotFound();

        var wallEnabled = auth.Value.Enabled;
        var callerPerson = wallEnabled ? await caller.PersonAsync(ct) : null;
        if (await OwnerOrAdminRefusalAsync(key, wallEnabled, callerPerson, ct) is { } refused) return refused;

        return await keys.RevokeApiKeyAsync(key.Id, ct) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Replaces a runner's secret, answered once. The runner's owner, or an
    /// admin, and never a key - a key that could rotate could take over another
    /// runner's name.
    /// </summary>
    /// <remarks>
    /// Config, heartbeat rows and sessions are untouched: they join to the key
    /// by id or by name, and rotating only changes the key's hash, prefix and
    /// <see cref="EfApiKey.RotatedAt"/>. The ownership refusal comes before the
    /// revoked one, so a stranger learns nothing of the key's state.
    /// </remarks>
    [HttpPost("{name}/rotate")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<RunnerMintedDto>> RotateRunner(string name, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;
        if (Named(name) is not { } runner) return BadRequest(NameRefusal);
        if (await KeyNamedAsync(runner, ct) is not { } key) return NotFound();

        var wallEnabled = auth.Value.Enabled;
        var callerPerson = wallEnabled ? await caller.PersonAsync(ct) : null;
        if (await OwnerOrAdminRefusalAsync(key, wallEnabled, callerPerson, ct) is { } refused) return refused;

        if (!key.IsLive(time.GetUtcNow())) return Conflict($"{runner}'s key is revoked");

        if (await keys.RotateApiKeyAsync(key.Id, ct) is not { } rotated) return NotFound();

        Response.Headers.CacheControl = "no-store";

        var now = time.GetUtcNow();
        var found = (await Runner.JoinAsync(db, keys, ct)).First(r => string.Equals(r.Name, runner, StringComparison.Ordinal));
        var held = await runners.HeldAsync(db, claims, now, ct);
        var drivers = await runners.DriversAsync(db, keys, now, ct);
        var site = await siteSettings.GetAsync(ct);

        var dto = await ProjectAsync(found, held, drivers, wallEnabled, callerPerson, site, await VisibleKeysAsync(ct), await StatusIdsAsync(ct), await LocalModelsAsync(ct), await OpenFailuresAsync(ct), now, ct);
        return new RunnerMintedDto(dto, rotated.Secret);
    }

    /// <summary>
    /// The 403 for a caller who neither owns this key nor is an admin, or null
    /// where they may go on. Not <see cref="CanControl"/>: that passes for a
    /// runner that belongs to nobody, and rotating one would hand a user a
    /// secret that sees every project.
    /// </summary>
    private async Task<ObjectResult?> OwnerOrAdminRefusalAsync(EfApiKey key, bool wallEnabled, EfPerson? callerPerson, CancellationToken ct)
    {
        if (OwnsOrAdministers(key.OwnerPersonId, wallEnabled, callerPerson)) return null;

        return key.OwnerPersonId is null
            ? new ObjectResult($"only an admin may control {key.Name}, which belongs to nobody")
                { StatusCode = StatusCodes.Status403Forbidden }
            : await ControlRefusalAsync(key.Name, key.OwnerPersonId, ct);
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

        var row = await db.LegacyRunners.FirstOrDefaultAsync(r => r.Name == runner, ct);
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

        var row = await db.LegacyRunners.FirstOrDefaultAsync(r => r.Name == runner, ct);
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

        var now = time.GetUtcNow();
        await stateLog.SettleAsync(db, keys, key.Name, now, ct);

        session.State = state;
        await db.SaveChangesAsync(ct);
        await stateLog.RecordAsync(db, keys, key.Name, now, ct);

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

    /// <summary>
    /// Lifts a <c>board-paused</c> pause the board set on one session, so its
    /// next heartbeat is answered with its stored state and its next claim is
    /// taken, rather than waiting out <see cref="IssueClaims.RunnerPauseMinutes"/>.
    /// </summary>
    /// <remarks>
    /// A person's, never a key's or a keyless runner's - the same reason
    /// <see cref="IssueStallController.PutHold"/> is: an agent that could lift
    /// its own pause could undo the one thing the board did about it dropping
    /// tickets without spending a token. Checked ahead of <see cref="GateAsync"/>
    /// with its own sentence, since <see cref="NotAPerson"/>'s talks about
    /// spending, which would read wrong for a pause.
    /// </remarks>
    [HttpDelete("{name}/sessions/{id:int}/pause")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult> DeleteRunnerSessionPause(string name, int id, CancellationToken ct)
    {
        if (await caller.IsProgramAsync(ct))
        {
            return new ObjectResult("lifting a pause is the operator's, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }

        var (key, refusal) = await GateAsync(name, ct);
        if (refusal is not null) return refusal;

        var session = await SessionNamedAsync(key!.Id, id, ct);
        if (session is null) return NotFound();

        var now = time.GetUtcNow();
        if (session.AgentDownUntil is null || session.AgentDownUntil <= now) return NoContent();

        if (session.AgentDownKind != RunnerAgentDownKinds.BoardPaused)
            return Conflict($"{session.AgentDownKind} is the runner's own to clear, not the board's");

        // A pause the board wrote outlives the row it was written on - see the
        // same comment on HeartbeatSessionAsync - so a restart in this checkout
        // would otherwise inherit it straight back. Cleared everywhere at once,
        // ended rows included.
        var siblings = await db.RunnerSessions
            .Where(s => s.ApiKeyId == key.Id && s.Where == session.Where
                && s.AgentDownKind == RunnerAgentDownKinds.BoardPaused && s.AgentDownUntil > now)
            .ToListAsync(ct);

        await stateLog.SettleAsync(db, keys, key.Name, now, ct);

        foreach (var sibling in siblings)
        {
            sibling.AgentDownUntil = null;
            sibling.AgentDownKind = null;
            sibling.AgentDownLine = null;
            sibling.PauseStreak = 0;
        }

        await db.SaveChangesAsync(ct);
        await stateLog.RecordAsync(db, keys, key.Name, now, ct);
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

        await stateLog.SettleAsync(db, keys, key!.Name, now, ct);

        foreach (var session in candidates.Where(s => sessions.IsLive(s, now))) session.State = state;

        await db.SaveChangesAsync(ct);
        await stateLog.RecordAsync(db, keys, key.Name, now, ct);
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

        return await ProjectAsync(found, held, drivers, wallEnabled, callerPerson, site, await VisibleKeysAsync(ct), await StatusIdsAsync(ct), await LocalModelsAsync(ct), await OpenFailuresAsync(ct), now, ct);
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

    /// <summary>Whether the caller owns this key, or is an admin. Built on <see cref="Yours"/>, so a key that belongs to nobody is an admin's alone.</summary>
    private static bool OwnsOrAdministers(Guid? ownerPersonId, bool wallEnabled, EfPerson? callerPerson) =>
        Yours(ownerPersonId, wallEnabled, callerPerson) || (wallEnabled && callerPerson?.Role >= PersonRole.Admin);

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
            LauncherVersion = request.LauncherVersion,
            BuiltAt = request.BuiltAt,
            Models = models,
            Has = has,
            Image = Fits(request.Image, EfHatchRunner.MaxImageLength),
            AcceleratorMemoryBytes = request.AcceleratorMemoryBytes,
            AcceleratorDeviceCount = request.AcceleratorDeviceCount,
            AcceleratorBackend = Fits(request.AcceleratorBackend, EfHatchRunner.MaxAcceleratorBackendLength),
            Projects = keys.Count > 0 ? string.Join('\n', keys) : null,
            RefusesDangerous = request.RefusesDangerous,
            ExhaustedUntil = request.Exhausted == true ? request.ExhaustedUntil : null,
            AgentDownUntil = request.AgentDown == true ? request.AgentDownUntil : null,
            AgentDownKind = request.AgentDown == true ? NormaliseAgentDownKind(request.AgentDownKind) : null,
            AgentDownLine = request.AgentDown == true ? Fits(request.AgentDownLine, EfHatchRunner.MaxAgentDownLineLength) : null,
            ThrottledUntil = request.ThrottledUntil,
            Pulling = kind == EfHatchRunner.LoopKind && request.Pulling == true,
            ForPersonId = forPersonId,
            Usage = Utilization.Serialise(request.Usage),
            UsageReadAt = request.UsageReadAt,
            UsageReceivedAt = Utilization.Serialise(request.Usage) is not null ? now : null,
            // The source describes a reading, so a new row stores one only
            // where its first beat carries a reading.
            UsageSource = Utilization.Serialise(request.Usage) is not null ? request.UsageSource : null,
            UsageProbeOff = Utilization.Serialise(request.Usage) is not null && request.UsageSource is not null
                ? request.UsageProbeOff
                : null,
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
        if (request.LauncherVersion is not null) row.LauncherVersion = request.LauncherVersion;
        if (request.BuiltAt is not null) row.BuiltAt = request.BuiltAt;

        // An empty list clears it: Joined answers null for one.
        if (request.Models is not null) row.Models = models;
        if (request.Has is not null) row.Has = has;

        // Written when sent, absent leaving it alone - the same tri-state as
        // every other fact-vs-absent field here, Image included below for the
        // one exception a fresh loop beat draws.
        if (request.Image is not null) row.Image = Fits(request.Image, EfHatchRunner.MaxImageLength);

        // The accelerator is the same kind of fact as Image: written when
        // sent, absent leaving it alone, bar the fresh loop beat below.
        if (request.AcceleratorMemoryBytes is not null) row.AcceleratorMemoryBytes = request.AcceleratorMemoryBytes;
        if (request.AcceleratorDeviceCount is not null) row.AcceleratorDeviceCount = request.AcceleratorDeviceCount;
        if (request.AcceleratorBackend is not null) row.AcceleratorBackend = Fits(request.AcceleratorBackend, EfHatchRunner.MaxAcceleratorBackendLength);

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

        // The same tri-state, for the agent rather than the account.
        if (request.AgentDown == true)
        {
            row.AgentDownUntil = request.AgentDownUntil;
            row.AgentDownKind = NormaliseAgentDownKind(request.AgentDownKind);
            row.AgentDownLine = Fits(request.AgentDownLine, EfHatchRunner.MaxAgentDownLineLength);
        }
        else if (request.AgentDown == false)
        {
            row.AgentDownUntil = null;
            row.AgentDownKind = null;
            row.AgentDownLine = null;
        }

        // Written on every loop beat, null clearing it the moment the runner
        // says it is no longer throttled - not a tri-state like Exhausted,
        // since there is no separate flag to carry "not throttled" apart from
        // the value itself. Not a `once` beat: it shares the loop's row name
        // (the same reasoning as ForPersonId below) and sending null must not
        // clear the loop's own wait.
        if (kind == EfHatchRunner.LoopKind) row.ThrottledUntil = request.ThrottledUntil;

        // Written on every loop beat, the same as ThrottledUntil: a beat that
        // does not say it is pulling clears it. Not a `once` beat, for the
        // same reason.
        if (kind == EfHatchRunner.LoopKind) row.Pulling = request.Pulling == true;

        // Whose key this is can change on the API Keys page, so this is
        // written every beat like Where and Remotes, unlike the six bounds,
        // which only a fresh loop's first beat writes. Not a `once` beat:
        // `hatch work` shares a runner name with a loop in the same checkout,
        // and its flagless beat must not clear the loop's budget.
        row.ForPersonId = forPersonId;

        if (request.Fresh == true && kind == EfHatchRunner.LoopKind)
        {
            Bound(row, request);

            // The fields written even where the beat names none: a fresh
            // start that named no image must not keep a stale one from the
            // incarnation before it, the way a restart onto the generic image
            // would otherwise look like it was still the one that was built.
            // The accelerator goes the same way: a restart that now reports no
            // profile (its Ollama moved off the box) must not keep the
            // previous process's.
            row.Image = Fits(request.Image, EfHatchRunner.MaxImageLength);

            // The launcher goes the same way: a loop restarted by a script, or
            // by a newer launcher, must not keep the old launcher's number and
            // the badge that comes with it.
            row.LauncherVersion = request.LauncherVersion;
            row.AcceleratorMemoryBytes = request.AcceleratorMemoryBytes;
            row.AcceleratorDeviceCount = request.AcceleratorDeviceCount;
            row.AcceleratorBackend = Fits(request.AcceleratorBackend, EfHatchRunner.MaxAcceleratorBackendLength);

            // The one beat that writes state: a start that asked to stand by
            // is paused on the beat that says so, from any state, so its own
            // answer is already `paused`.
            if (request.Standby == true) row.State = EfHatchRunner.Paused;
        }

        // A non-empty reading writes; absent or empty leaves the row's own
        // reading alone - see the remark on RunnerHeartbeatRequest.Usage.
        if (Utilization.Serialise(request.Usage) is { } usage)
        {
            // UsageReceivedAt only moves when the reading itself changed, so
            // an idle beat resending the same reading does not look new.
            if (row.UsageReadAt != request.UsageReadAt) row.UsageReceivedAt = now;
            row.Usage = usage;
            row.UsageReadAt = request.UsageReadAt;

            // The source and the probe's reason travel together: a beat that
            // names a source is authoritative for both (a null reason clears
            // one), and one that does not leaves both as they were.
            if (request.UsageSource is not null)
            {
                row.UsageSource = request.UsageSource;
                row.UsageProbeOff = request.UsageProbeOff;
            }
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
            LauncherVersion = request.LauncherVersion,
            BuiltAt = request.BuiltAt,
            RefusesDangerous = request.RefusesDangerous,
            ExhaustedUntil = request.Exhausted == true ? request.ExhaustedUntil : null,
            AgentDownUntil = request.AgentDown == true ? request.AgentDownUntil : null,
            AgentDownKind = request.AgentDown == true ? NormaliseAgentDownKind(request.AgentDownKind) : null,
            AgentDownLine = request.AgentDown == true ? Fits(request.AgentDownLine, EfHatchRunner.MaxAgentDownLineLength) : null,
            ThrottledUntil = request.ThrottledUntil,
            Pulling = kind == EfHatchRunner.LoopKind && request.Pulling == true,
            Usage = Utilization.Serialise(request.Usage),
            UsageReadAt = request.UsageReadAt,
            UsageReceivedAt = Utilization.Serialise(request.Usage) is not null ? now : null,
            UsageSource = Utilization.Serialise(request.Usage) is not null ? request.UsageSource : null,
            UsageProbeOff = Utilization.Serialise(request.Usage) is not null && request.UsageSource is not null
                ? request.UsageProbeOff
                : null,
            Models = models,
            Has = has,
            Image = Fits(request.Image, EfHatchRunner.MaxImageLength),
            AcceleratorMemoryBytes = request.AcceleratorMemoryBytes,
            AcceleratorDeviceCount = request.AcceleratorDeviceCount,
            AcceleratorBackend = Fits(request.AcceleratorBackend, EfHatchRunner.MaxAcceleratorBackendLength),
            Signing = NormaliseSigning(request.Signing),
            SigningWhy = SigningWhy(request.Signing, request.SigningWhy),
            Forge = NormaliseForge(request.Forge),
            ForgeWhy = ForgeWhy(request.Forge, request.ForgeWhy),
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
        if (request.LauncherVersion is not null) session.LauncherVersion = request.LauncherVersion;
        if (request.BuiltAt is not null) session.BuiltAt = request.BuiltAt;

        // Written when sent, so a later check that changes the answer moves it
        // and a word the board does not know clears it; absent leaves it alone.
        if (request.Signing is not null)
        {
            session.Signing = NormaliseSigning(request.Signing);
            session.SigningWhy = SigningWhy(request.Signing, request.SigningWhy);
        }

        // The forge sign-in is written on the same terms.
        if (request.Forge is not null)
        {
            session.Forge = NormaliseForge(request.Forge);
            session.ForgeWhy = ForgeWhy(request.Forge, request.ForgeWhy);
        }

        if (request.Os is not null) session.Os = Fits(request.Os, EfHatchRunnerSession.MaxOsLength);
        if (request.Arch is not null) session.Arch = Fits(request.Arch, EfHatchRunnerSession.MaxArchLength);
        if (request.AcceleratorMemoryBytes is not null) session.AcceleratorMemoryBytes = request.AcceleratorMemoryBytes;
        if (request.AcceleratorDeviceCount is not null) session.AcceleratorDeviceCount = request.AcceleratorDeviceCount;
        if (request.AcceleratorBackend is not null) session.AcceleratorBackend = Fits(request.AcceleratorBackend, EfHatchRunner.MaxAcceleratorBackendLength);
        if (request.AgentVersion is not null) session.AgentVersion = Fits(request.AgentVersion, EfHatchRunnerSession.MaxAgentVersionLength);
        if (request.Username is not null) session.Username = Fits(request.Username, EfHatchRunnerSession.MaxUsernameLength);
        if (request.Container is not null) session.Container = request.Container;
        if (request.ProcessStartedAt is not null) session.ProcessStartedAt = request.ProcessStartedAt;
        if (ip is not null) session.Ip = Fits(ip, EfHatchRunnerSession.MaxIpLength);

        // An empty list clears it: Joined answers null for one, the same as Touch.
        if (request.Models is not null) session.Models = models;
        if (request.Has is not null) session.Has = has;

        // Written when sent, absent leaving it alone. Unlike Touch's own row,
        // a fresh loop beat never reaches here to need the clearing exception:
        // HeartbeatSessionAsync answers Conflict for a fresh loop beat against
        // a live session, so a live session is only ever touched on a beat
        // that is not fresh.
        if (request.Image is not null) session.Image = Fits(request.Image, EfHatchRunner.MaxImageLength);

        // A fact about the process, like Where and Remotes - the same rule Touch draws for a beat.
        if (request.RefusesDangerous is not null) session.RefusesDangerous = request.RefusesDangerous;
        if (request.Projects is not null)
        {
            var keys = ProjectKeys(request.Projects);
            session.Projects = keys.Count > 0 ? string.Join('\n', keys) : null;
        }

        if (request.Exhausted == true) session.ExhaustedUntil = request.ExhaustedUntil;
        else if (request.Exhausted == false) session.ExhaustedUntil = null;

        // A pause the board wrote is the board's to lift: nothing a runner
        // sends clears, shortens or rewords it before it passes.
        var boardPaused = session.AgentDownKind == RunnerAgentDownKinds.BoardPaused && session.AgentDownUntil > now;
        if (!boardPaused && request.AgentDown == true)
        {
            session.AgentDownUntil = request.AgentDownUntil;
            session.AgentDownKind = NormaliseAgentDownKind(request.AgentDownKind);
            session.AgentDownLine = Fits(request.AgentDownLine, EfHatchRunner.MaxAgentDownLineLength);
        }
        else if (!boardPaused && request.AgentDown == false)
        {
            session.AgentDownUntil = null;
            session.AgentDownKind = null;
            session.AgentDownLine = null;
        }

        // Loop-only, the same as Touch: a `once` beat shares the loop
        // session's (key, where) and must not clear its own wait.
        if (kind == EfHatchRunner.LoopKind) session.ThrottledUntil = request.ThrottledUntil;

        // Loop-only, the same as ThrottledUntil above.
        if (kind == EfHatchRunner.LoopKind) session.Pulling = request.Pulling == true;

        if (Utilization.Serialise(request.Usage) is { } usage)
        {
            // UsageReceivedAt only moves when the reading itself changed, so
            // an idle beat resending the same reading does not look new.
            if (session.UsageReadAt != request.UsageReadAt) session.UsageReceivedAt = now;
            session.Usage = usage;
            session.UsageReadAt = request.UsageReadAt;

            // Source and probe reason travel together, as on the runner row.
            if (request.UsageSource is not null)
            {
                session.UsageSource = request.UsageSource;
                session.UsageProbeOff = request.UsageProbeOff;
            }
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

    /// <summary>
    /// The latest of the times still ahead of <paramref name="now"/>, or null.
    /// Latest rather than earliest: one key is one account, and the account is
    /// out until the last session that was told so is.
    /// </summary>
    private static DateTimeOffset? Latest(IEnumerable<DateTimeOffset?> times, DateTimeOffset now) =>
        times.Where(t => t > now).Max();

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
    internal static string? Fits(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    /// <summary>
    /// One of the two kinds a runner may send - anything else, including absent
    /// and <see cref="RunnerAgentDownKinds.BoardPaused"/>, which only the board
    /// writes, reads as <see cref="RunnerAgentDownKinds.NotStarting"/>.
    /// </summary>
    internal static string NormaliseAgentDownKind(string? kind) =>
        kind == RunnerAgentDownKinds.SignedOut ? RunnerAgentDownKinds.SignedOut : RunnerAgentDownKinds.NotStarting;

    /// <summary>
    /// A signing word the board keeps: trimmed, lowercased, and one of
    /// <see cref="RunnerSigning.Words"/> - anything else is no answer, and the
    /// heartbeat that sent it is still accepted.
    /// </summary>
    internal static string? NormaliseSigning(string? word)
    {
        var normal = word?.Trim().ToLowerInvariant();
        return normal is not null && Array.IndexOf(RunnerSigning.Words, normal) >= 0 ? normal : null;
    }

    /// <summary>What git said, kept only beside <see cref="RunnerSigning.Fails"/> and cut to one line that fits.</summary>
    internal static string? SigningWhy(string? word, string? why) =>
        NormaliseSigning(word) == RunnerSigning.Fails
            ? Fits(why?.Split('\n', 2)[0], EfHatchRunner.MaxSigningWhyLength)
            : null;

    /// <summary>
    /// A forge word the board keeps: trimmed, lowercased, and one of
    /// <see cref="RunnerForge.Words"/> - anything else is no answer, and the
    /// heartbeat that sent it is still accepted.
    /// </summary>
    internal static string? NormaliseForge(string? word)
    {
        var normal = word?.Trim().ToLowerInvariant();
        return normal is not null && Array.IndexOf(RunnerForge.Words, normal) >= 0 ? normal : null;
    }

    /// <summary>What the forge said, kept only beside <see cref="RunnerForge.Fails"/> and cut to one line that fits.</summary>
    internal static string? ForgeWhy(string? word, string? why) =>
        NormaliseForge(word) == RunnerForge.Fails
            ? Fits(why?.Split('\n', 2)[0], EfHatchRunner.MaxForgeWhyLength)
            : null;

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

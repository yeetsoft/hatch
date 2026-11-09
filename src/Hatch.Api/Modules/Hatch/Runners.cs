using Hatch.Api.Ef;
using Hatch.Api.Models.HatchRevision;
using Hatch.Api.Services;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The runner, and the one place its two horizons live: how long a row may go
/// unheard from before it reads <em>gone</em>, how much longer before it stops
/// being returned at all, and what a row plus whatever claim it holds looks
/// like on the wire.
///
/// A class rather than a static for the reason <see cref="IssueClaims"/> is
/// one: it holds a configured number and nothing else, and every judgement it
/// makes takes the instant it is judging against rather than reading a clock.
/// </summary>
/// <remarks>
/// Nothing here sweeps and nothing here writes. A runner that stopped answering
/// at midnight is idle at 00:01, gone at 00:02 and not in the answer at all by
/// 02:01 - each of those by arithmetic against <c>LastSeenAt</c> at the moment
/// somebody asks, which is the same lazy expiry the claim uses. There is no
/// timeout to tune and no background job to notice has stopped.
/// </remarks>
public sealed class Runners(IOptions<HatchOptions> options, IOptions<AuthOptions> auth, IHatchRevision revision)
{
    /// <summary>
    /// How long after its last heartbeat a row is still returned by the read:
    /// two hours, so a runner that died overnight is a ghost row somebody can
    /// look at the next morning rather than one gone before they sit down.
    /// </summary>
    /// <remarks>
    /// A constant and not a setting. The read is what the queue view and the
    /// Runners page both draw, and <see cref="DropBefore"/> takes the longer of
    /// this and <see cref="GoneAfterSeconds"/>, so a gone horizon above two
    /// hours cannot drop a row before it was ever read as gone.
    /// </remarks>
    public const int RetentionSeconds = 2 * 60 * 60;

    /// <summary>
    /// The silence after which a runner reads as gone, in seconds. Guarded here
    /// and nowhere else: a configured zero or a negative would make every
    /// runner gone on arrival, and a misconfigured horizon should cost a
    /// fallback rather than a page that says every loop is dead.
    /// </summary>
    public int GoneAfterSeconds { get; } =
        options.Value.RunnerGoneAfterSeconds > 0
            ? options.Value.RunnerGoneAfterSeconds
            : new HatchOptions().RunnerGoneAfterSeconds;

    /// <summary>The heartbeat at or after which a runner is still being heard from.</summary>
    public DateTimeOffset Cutoff(DateTimeOffset now) => now.AddSeconds(-GoneAfterSeconds);

    /// <summary>The heartbeat before which a row is not handed out at all.</summary>
    public DateTimeOffset DropBefore(DateTimeOffset now) => now.AddSeconds(-Math.Max(GoneAfterSeconds, RetentionSeconds));

    /// <summary>
    /// Whether this runner is still answering. Exactly at the cutoff is here;
    /// one tick past it is gone - the same edge the claim draws, so the two
    /// never disagree about which side of a boundary an instant is on.
    /// </summary>
    public bool IsHere(DateTimeOffset lastSeenAt, DateTimeOffset now) => lastSeenAt >= Cutoff(now);

    /// <summary>
    /// Whether this row has aged out of the read. Not a deletion: the row stays
    /// where it is, and a runner that comes back under the same name picks its
    /// own bounds up again rather than being seeded afresh.
    /// </summary>
    public bool IsDropped(DateTimeOffset lastSeenAt, DateTimeOffset now) => lastSeenAt < DropBefore(now);

    /// <summary>
    /// The row as a client draws it, with whatever it holds folded in.
    /// </summary>
    /// <param name="claim">
    /// The live claim this runner holds, or null between increments. The
    /// liveness is the caller's to decide, because a scan judges every row
    /// against one instant - see <see cref="IssueClaims.IsLive"/>.
    /// </param>
    /// <remarks>
    /// The line is the claim's while there is one and the row's when there is
    /// not, and that is the whole of why the row has a line at all: an
    /// increment's chatter already rides its lease, so this column only ever
    /// carries what a runner said between tickets. <c>LastSeenAt</c> is read
    /// alone and nowhere folded against the claim's own heartbeat: the claim
    /// heartbeat and the claim release both touch the holding runner's row
    /// themselves (<see cref="IssueClaimController"/>), so this column is
    /// always current on its own - and reading it alone is load-bearing for
    /// <see cref="Preemption"/>'s rule 5, where a runner that has just
    /// released must read as free rather than gone in exactly the seconds
    /// that matters.
    /// </remarks>
    public RunnerDto Project(
        EfHatchRunner runner, (string Key, ClaimSnapshot Claim, string? Status)? claim, IReadOnlyList<string>? drives = null,
        string? forName = null, bool canControl = true, RunnerOrderDto? order = null,
        bool yours = false, long? defaultMaxTokens = null, long? defaultMaxTokensPerHour = null)
    {
        var maxTokens = Effective(runner.MaxTokens, defaultMaxTokens);
        var maxTokensPerHour = Effective(runner.MaxTokensPerHour, defaultMaxTokensPerHour);
        var drift = RevisionComparison.Compare(revision.Revision, revision.Sequence, runner.Revision ?? "", runner.Sequence ?? 0);
        // Reference and client swapped on purpose: the reference here is the
        // runner's own checkout, not the board.
        var sourceDrift = RevisionComparison.Compare(runner.SourceRevision ?? "", runner.SourceSequence ?? 0, runner.Revision ?? "", runner.Sequence ?? 0);

        return new(
            runner.Name,
            runner.Kind,
            runner.FirstSeenAt,
            runner.LastSeenAt,
            claim?.Key,
            claim is { } held ? held.Claim.Chatter : runner.Line,
            claim is { } at ? at.Claim.ChatterAt : runner.LineAt,
            runner.State,
            runner.Under,
            runner.MaxRuns,
            runner.MaxSpend,
            runner.UntilAt,
            GoneAfterSeconds,
            runner.Remotes?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [],
            runner.Clones,
            runner.Mine,
            runner.Standing,
            runner.Where,
            runner.Agent,
            runner.ExhaustedUntil,
            drives,
            runner.Tags?.Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray() ?? [],
            forName,
            canControl,
            order,
            runner.Revision,
            runner.Sequence,
            runner.BuiltAt,
            ToBoardDrift(drift),
            ToBoardDrift(sourceDrift),
            // Not sorted, unlike Tags: the order given is the order kept.
            runner.Projects?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [],
            yours,
            runner.Runs,
            runner.Spent,
            runner.Host,
            claim?.Status,
            runner.RefusesDangerous ?? false,
            maxTokens.Cap,
            maxTokensPerHour.Cap,
            maxTokens.Source,
            maxTokensPerHour.Source,
            runner.TokensUsed,
            runner.TokensLastHour,
            runner.ThrottledUntil,
            runner.Models?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [],
            runner.Has?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? []);
    }

    /// <summary>
    /// The same row, for a <see cref="Runner"/> aggregate rather than a bare
    /// heartbeat: a key with no beat projects as though it had just started -
    /// <see cref="Transient"/> - and a config row, where present, overrides
    /// the board's own six bounds and the tags on top of whichever beat is
    /// doing the projecting.
    /// </summary>
    public RunnerDto Project(
        Runner runner, (string Key, ClaimSnapshot Claim, string? Status)? claim, IReadOnlyList<string>? drives = null,
        string? forName = null, bool canControl = true, RunnerOrderDto? order = null,
        bool yours = false, long? defaultMaxTokens = null, long? defaultMaxTokensPerHour = null)
    {
        var beat = runner.Beat ?? Transient(runner.Key!);
        var effective = runner.Config is { } config ? Overlaid(beat, config) : beat;

        var dto = Project(effective, claim, drives, forName, canControl, order, yours, defaultMaxTokens, defaultMaxTokensPerHour)
            with { OwnerPersonId = runner.OwnerPersonId };

        if (runner.Config is { } tagged)
            dto = dto with { Tags = RunnerTagColumn.Read(tagged.Tags).Order(StringComparer.Ordinal).ToArray() };

        if (runner.Key is { } key)
        {
            dto = dto with
            {
                ApiKeyId = key.Id,
                Prefix = key.Prefix,
                CreatedAt = key.CreatedAt,
                LastUsedAt = key.LastUsedAt,
                RevokedAt = key.RevokedAt,
                RotatedAt = key.RotatedAt,
            };
        }

        return dto;
    }

    /// <summary>A key with no beat of its own, projected as though it had just started - so every field a beat guarantees non-null still is.</summary>
    private static EfHatchRunner Transient(EfApiKey key) => new()
    {
        Name = key.Name,
        Kind = EfHatchRunner.LoopKind,
        State = EfHatchRunner.Running,
        FirstSeenAt = key.CreatedAt,
        LastSeenAt = key.LastUsedAt ?? key.CreatedAt,
    };

    /// <summary>
    /// A shallow copy of <paramref name="beat"/> with the board's own six
    /// bounds taken from <paramref name="config"/> instead, even where that is
    /// null - a copy, never a mutation, because <paramref name="beat"/> may be
    /// the very row <see cref="HeldAsync"/> or a later caller reads again.
    /// </summary>
    private static EfHatchRunner Overlaid(EfHatchRunner beat, EfHatchRunnerConfig config) => new()
    {
        Name = beat.Name,
        Kind = beat.Kind,
        FirstSeenAt = beat.FirstSeenAt,
        LastSeenAt = beat.LastSeenAt,
        Line = beat.Line,
        LineAt = beat.LineAt,
        State = beat.State,
        Under = config.Under,
        MaxRuns = config.MaxRuns,
        MaxSpend = config.MaxSpend,
        MaxTokens = config.MaxTokens,
        MaxTokensPerHour = config.MaxTokensPerHour,
        UntilAt = config.UntilAt,
        Remotes = beat.Remotes,
        Clones = beat.Clones,
        Mine = beat.Mine,
        Standing = beat.Standing,
        Where = beat.Where,
        Runs = beat.Runs,
        Spent = beat.Spent,
        TokensUsed = beat.TokensUsed,
        TokensLastHour = beat.TokensLastHour,
        Host = beat.Host,
        Agent = beat.Agent,
        Revision = beat.Revision,
        Sequence = beat.Sequence,
        BuiltAt = beat.BuiltAt,
        SourceRevision = beat.SourceRevision,
        SourceSequence = beat.SourceSequence,
        Tags = beat.Tags,
        Models = beat.Models,
        Has = beat.Has,
        RefusesDangerous = beat.RefusesDangerous,
        Projects = beat.Projects,
        ExhaustedUntil = beat.ExhaustedUntil,
        ThrottledUntil = beat.ThrottledUntil,
        Usage = beat.Usage,
        UsageReadAt = beat.UsageReadAt,
        ForPersonId = beat.ForPersonId,
        OrderKind = beat.OrderKind,
        OrderKey = beat.OrderKey,
        OrderedBy = beat.OrderedBy,
        OrderedAt = beat.OrderedAt,
    };

    private static RunnerBoardDrift ToBoardDrift(RevisionDrift drift) => drift switch
    {
        RevisionDrift.Current => RunnerBoardDrift.Current,
        RevisionDrift.Behind => RunnerBoardDrift.Behind,
        RevisionDrift.Ahead => RunnerBoardDrift.Ahead,
        _ => RunnerBoardDrift.Unknown,
    };

    /// <summary>
    /// What the loop reads back off its own heartbeat: the operator's half of
    /// the row - or, for a token cap the row leaves unset, the site default -
    /// plus who it works for - a fact about the caller and not about the row,
    /// so it is handed in rather than read off <paramref name="runner"/>. The
    /// order is a pure projection too - the caller decides whether this beat
    /// is the one that picks it up, since <see cref="EfHatchRunner.OrderKind"/>
    /// has usually already been cleared by the time this is called.
    /// </summary>
    public static RunnerInstructionDto Instruct(
        EfHatchRunner runner, string? forName, IReadOnlyList<string>? drives = null, RunnerOrderDto? order = null,
        long? defaultMaxTokens = null, long? defaultMaxTokensPerHour = null) =>
        new(runner.State, runner.Under, runner.MaxRuns, runner.MaxSpend, runner.UntilAt, forName, drives,
            runner.Tags?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [], order,
            Projects: null,
            RefusesDangerous: null,
            Effective(runner.MaxTokens, defaultMaxTokens).Cap,
            Effective(runner.MaxTokensPerHour, defaultMaxTokensPerHour).Cap);

    /// <summary>
    /// A token cap as the row's own value, else the site default, else none,
    /// with which of the three it was. <see cref="Instruct"/> and
    /// <see cref="Project"/> both go through it, so the beat the loop obeys and
    /// the row the operator reads cannot disagree.
    /// </summary>
    private static (long? Cap, string Source) Effective(long? own, long? site) =>
        own is not null ? (own, "runner")
        : site is not null ? (site, "default")
        : (null, "none");

    /// <summary>
    /// Every live claim, by the runner and the <c>where</c> holding it. The key
    /// is <see cref="EfHatchIssue.ClaimedBy"/> and <see cref="EfHatchIssue.ClaimRunner"/>
    /// read backwards, which is what makes "what is this runner working, at
    /// this checkout" a question with one answer and no second copy to keep in
    /// step - shared between <see cref="RunnersController"/>'s own read and
    /// <see cref="Preemption"/>'s rule 5, rather than kept twice. One runner's
    /// several sessions each hold at most one ticket at a different
    /// <c>where</c>, so the runner's name alone is not a key.
    /// </summary>
    public async Task<Dictionary<(string Runner, string Where), (string Key, ClaimSnapshot Claim, string? Status)>> HeldAsync(
        HatchContext db, IssueClaims claims, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.Issues.AsNoTracking()
            .Where(i => i.ClaimToken != null && i.ClaimRunner != null)
            .Select(i => new
            {
                i.Project!.Key,
                i.Number,
                i.ClaimToken,
                i.ClaimedBy,
                i.ClaimRunner,
                i.ClaimedAt,
                i.ClaimHeartbeatAt,
                i.ClaimChatter,
                i.ClaimChatterAt,
                i.ClaimQuietLimitSeconds,
                Status = i.Status!.Name,
            })
            .ToListAsync(ct);

        return rows
            .Select(i => (
                Runner: i.ClaimedBy!,
                Where: i.ClaimRunner!,
                Key: IssueKey.Format(i.Key, i.Number),
                Claim: new ClaimSnapshot(
                    i.ClaimToken, i.ClaimedBy, i.ClaimRunner, i.ClaimedAt,
                    i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt,
                    i.ClaimQuietLimitSeconds),
                i.Status))
            .Where(i => claims.IsLive(i.Claim, now))
            // A runner holds one ticket at a time per checkout by construction,
            // and the most recent heartbeat is the one to draw if something ever
            // leaves two behind - a row about the wrong ticket is worse than a
            // row about none.
            .GroupBy(i => (i.Runner, i.Where))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(i => i.Claim.HeartbeatAt)
                    .Select(i => (i.Key, i.Claim, i.Status))
                    .First());
    }

    /// <summary>
    /// One project, one driver: the longest-running live loop that holds a
    /// checkout of it, by <see cref="Dispatch.HoldsCheckout"/> - the same rule
    /// <see cref="WorkController.GetReview"/> uses for its own eligibility, so
    /// the two can never disagree about which projects a runner can act on.
    /// A candidate is drawn from a beat or from a live session, additively -
    /// a key with two live sessions is one candidate name, elected at most
    /// once, never a duplicate or a throw.
    /// </summary>
    /// <remarks>
    /// With the wall up, a project that has an owner is driven only by a
    /// runner working for one of its owners or approvers - the people a merge
    /// belongs to, and whose forge credentials can make it. A runner for
    /// anybody else, or on a key that belongs to nobody, is not a candidate
    /// however long it has run, and a project with no such runner live has no
    /// driver at all. With the wall off, or on a project with no owner, a role
    /// means nothing and every holder of a checkout is eligible. "Has an owner"
    /// and "is one" read the raw member rows, as <c>ProjectAccess</c> does: this
    /// runs on every heartbeat, and a project whose only owner was deleted stays
    /// owned here, which errs toward nobody driving rather than anybody.
    ///
    /// The tie-break - oldest <c>FirstSeenAt</c> first, then <c>Name</c>
    /// ordinally - is what makes the election stable: the longest-running
    /// eligible runner keeps a project for as long as it keeps heartbeating,
    /// and only moves when it drops out of <c>live</c>, the same lazy
    /// arithmetic <see cref="IsHere"/> already draws everywhere else. No
    /// sweep, no new table, no migration - every input already lives on
    /// <see cref="EfHatchRunner"/>, <see cref="EfHatchRunnerSession"/> and
    /// <see cref="EfHatchProjectRepository"/>.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DriversAsync(
        HatchContext db, IAuthService keys, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = Cutoff(now);

        // Only a loop ever starts a MergeTrain; a `once` runner's own BeatAsync
        // throws its instruction away (GoToWork.cs), so electing one would
        // strand a project with a driver that can never answer.
        var live = await db.Runners.AsNoTracking()
            .Where(r => r.LastSeenAt >= cutoff && r.Kind == EfHatchRunner.LoopKind)
            .Select(r => new { r.Name, r.FirstSeenAt, r.Remotes, r.Standing, r.ForPersonId })
            .ToListAsync(ct);

        var candidates = live
            .Select(r => (r.Name, r.FirstSeenAt, r.ForPersonId, Repos: RepositoryDeclaration.From(
                r.Remotes?.Split('\n', StringSplitOptions.RemoveEmptyEntries), r.Standing, null)))
            .ToList();

        // A session is exactly as electable as a beat - additive, the same
        // rule Runner.IsLive draws - so a project whose only live claimant is
        // a session still gets a driver. A session whose key no longer exists
        // is skipped rather than thrown on.
        var liveSessions = await db.RunnerSessions.AsNoTracking()
            .Where(s => s.EndedAt == null && s.LastSeenAt >= cutoff && s.Kind == EfHatchRunner.LoopKind)
            .ToListAsync(ct);

        if (liveSessions.Count > 0)
        {
            var keysById = (await keys.ListApiKeysAsync(ct)).ToDictionary(k => k.Id);

            candidates.AddRange(liveSessions
                .Where(s => keysById.ContainsKey(s.ApiKeyId))
                .Select(s =>
                {
                    var key = keysById[s.ApiKeyId];
                    return (key.Name, s.StartedAt, key.OwnerPersonId, Repos: RepositoryDeclaration.From(
                        s.Remotes?.Split('\n', StringSplitOptions.RemoveEmptyEntries), s.Standing, null));
                }));
        }

        // Who a merge belongs to, per project, for the projects that have an
        // owner. Left empty with the wall down, where a role means nothing.
        Dictionary<int, HashSet<Guid>> people = [];
        if (auth.Value.Enabled)
        {
            var members = await db.ProjectMembers.AsNoTracking()
                .Where(m => m.Role == ProjectMemberRole.Owner || m.Role == ProjectMemberRole.Approver)
                .Select(m => new { m.ProjectId, m.PersonId, m.Role })
                .ToListAsync(ct);

            people = members
                .GroupBy(m => m.ProjectId)
                .Where(g => g.Any(m => m.Role == ProjectMemberRole.Owner))
                .ToDictionary(g => g.Key, g => g.Select(m => m.PersonId).ToHashSet());
        }

        var projects = await db.Projects.AsNoTracking().Include(p => p.Repositories).ToListAsync(ct);

        // From the candidates' own distinct names, not live.ToDictionary: two
        // sessions under one key are two candidates of the same name, and a
        // dictionary keyed straight off live's rows would throw on the second.
        var byRunner = candidates
            .Select(c => c.Name)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var project in projects)
        {
            var driver = candidates
                .Where(c => Dispatch.HoldsCheckout(project.Repositories, c.Repos))
                .Where(c => !people.TryGetValue(project.Id, out var qualifying)
                    || (c.ForPersonId is { } person && qualifying.Contains(person)))
                .OrderBy(c => c.FirstSeenAt).ThenBy(c => c.Name, StringComparer.Ordinal)
                .Select(c => c.Name).FirstOrDefault();

            if (driver is not null) byRunner[driver].Add(project.Key);
        }

        return byRunner.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// One line at most, trimmed and capped - <see cref="IssueClaimController"/>'s
    /// rule, called on the other kind of heartbeat. Null stays null, because
    /// "no opinion" and "clear it" are different instructions and the empty
    /// string is the second one.
    /// </summary>
    public static string? Normalise(string? line)
    {
        if (line is null) return null;

        var first = line.ReplaceLineEndings("\n").Split('\n')[0].Trim();
        return first.Length > EfHatchRunner.MaxLineLength ? first[..EfHatchRunner.MaxLineLength] : first;
    }
}

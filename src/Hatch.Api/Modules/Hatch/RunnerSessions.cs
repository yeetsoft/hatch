using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The judgement a session makes on its own, beside the one <see cref="Runners"/>
/// already makes for a beat: whether it is still live, how much of its ended
/// history a key keeps, and what a session's own bounds become once a
/// runner's own config overlays them.
///
/// A class rather than a static for <see cref="Runners"/>'s own reason: it
/// holds a configured number and nothing else, and every judgement it makes
/// takes the instant it is judging against rather than reading a clock.
/// </summary>
/// <remarks>
/// Nothing here sweeps. A session that stopped heartbeating is live until
/// somebody asks, the same lazy expiry <see cref="Runners.IsHere"/> already
/// draws for a beat - <see cref="MarkGoneAsync"/> is the one write here, and
/// it only ever stamps a session that is already past that horizon.
/// </remarks>
public sealed class RunnerSessions(Runners runners)
{
    /// <summary>How many ended sessions one key keeps; <see cref="TrimHistoryAsync"/> deletes the rest.</summary>
    public const int HistoryDepth = 20;

    /// <summary>
    /// Whether this session is still answering: not yet marked gone, and heard
    /// from inside the same horizon a beat is. Exactly the terms
    /// <see cref="Runners.IsHere"/> draws, so a session and the key's own beat
    /// never disagree about which side of the cutoff an instant is on.
    /// </summary>
    public bool IsLive(EfHatchRunnerSession s, DateTimeOffset now) => s.EndedAt is null && runners.IsHere(s.LastSeenAt, now);

    /// <summary>
    /// Stamps <see cref="EfHatchRunnerSession.EndedAt"/> on every one of this
    /// key's sessions that has gone quiet past the cutoff and has not already
    /// been marked - a conditional <c>UPDATE</c>, so a second call once the
    /// first has run touches nothing and reports nothing, the same guarantee
    /// <see cref="IssueClaims.TryTakeAsync"/> makes for a claim.
    /// </summary>
    /// <returns>How many sessions this call ended.</returns>
    public Task<int> MarkGoneAsync(HatchContext db, Guid apiKeyId, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = runners.Cutoff(now);

        return db.RunnerSessions
            .Where(s => s.ApiKeyId == apiKeyId && s.EndedAt == null && s.LastSeenAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, (DateTimeOffset?)now), ct);
    }

    /// <summary>
    /// The same write as <see cref="MarkGoneAsync"/>, across every key at
    /// once rather than one - the one <c>UPDATE</c> a read of the whole table,
    /// such as <see cref="RunnersController.GetRunners"/>, needs instead of a
    /// call per key.
    /// </summary>
    /// <returns>How many sessions this call ended.</returns>
    public Task<int> MarkAllGoneAsync(HatchContext db, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = runners.Cutoff(now);

        return db.RunnerSessions
            .Where(s => s.EndedAt == null && s.LastSeenAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, (DateTimeOffset?)now), ct);
    }

    /// <summary>
    /// Deletes every one of this key's ended sessions past the newest
    /// <see cref="HistoryDepth"/>, oldest first. Called only after a new
    /// session row has been inserted for the key, so the row just written is
    /// never among the ones a call right after it could delete.
    /// </summary>
    /// <returns>How many sessions this call deleted.</returns>
    public Task<int> TrimHistoryAsync(HatchContext db, Guid apiKeyId, CancellationToken ct) =>
        db.RunnerSessions
            .Where(s => s.ApiKeyId == apiKeyId && s.EndedAt != null && db.RunnerSessions
                .Where(o => o.ApiKeyId == apiKeyId && o.EndedAt != null)
                .OrderByDescending(o => o.EndedAt)
                .ThenByDescending(o => o.Id)
                .Skip(HistoryDepth)
                .Select(o => o.Id)
                .Contains(s.Id))
            .ExecuteDeleteAsync(ct);

    /// <summary>
    /// A bound as a session and a ceiling leave it: the value that won, and
    /// which of the two - or neither - it came from.
    /// </summary>
    public sealed record Narrowed<T>(T? Value, string Source) where T : struct;

    /// <summary>
    /// A session's six bounds once a runner's own config, where there is one,
    /// has had its say. <see cref="Project"/> is the one place this is read
    /// from, so the number a loop is told and the number a person reads can
    /// never come from two different computations.
    /// </summary>
    public sealed record EffectiveBounds(
        Narrowed<int> MaxRuns,
        Narrowed<decimal> MaxSpend,
        Narrowed<long> MaxTokens,
        Narrowed<long> MaxTokensPerHour,
        Narrowed<DateTimeOffset> UntilAt,
        string? Under);

    /// <summary>
    /// The six bounds a session answers to, narrowed against
    /// <paramref name="ceiling"/> where the project has one. A token cap the
    /// session and the ceiling both leave unset falls back to the site-wide
    /// default - <paramref name="siteMaxTokens"/>, <paramref name="siteMaxTokensPerHour"/> -
    /// for the reason <see cref="Runners.Instruct"/> already does: without it,
    /// the number a loop is told would move independently of the number this
    /// method answers.
    /// </summary>
    public static EffectiveBounds Effective(
        EfHatchRunnerConfig? ceiling, EfHatchRunnerSession session,
        long? siteMaxTokens = null, long? siteMaxTokensPerHour = null)
    {
        var (ceilingMaxTokens, maxTokensSource) = CeilingToken(ceiling?.MaxTokens, siteMaxTokens);
        var (ceilingMaxTokensPerHour, maxTokensPerHourSource) = CeilingToken(ceiling?.MaxTokensPerHour, siteMaxTokensPerHour);

        return new EffectiveBounds(
            Narrow(session.MaxRuns, ceiling?.MaxRuns, "runner"),
            Narrow(session.MaxSpend, ceiling?.MaxSpend, "runner"),
            Narrow(session.MaxTokens, ceilingMaxTokens, maxTokensSource),
            Narrow(session.MaxTokensPerHour, ceilingMaxTokensPerHour, maxTokensPerHourSource),
            Narrow(session.UntilAt, ceiling?.UntilAt, "runner"),
            session.Under ?? ceiling?.Under);
    }

    /// <summary>
    /// The row a client draws for one session, with whatever claim it holds
    /// folded in - <see cref="Runners.Project"/>'s own shape, for a session
    /// rather than a beat. The line is the claim's while there is one and the
    /// session's own when there is not, for the reason <see cref="Runners.Project"/>
    /// gives for a beat.
    /// </summary>
    public RunnerSessionDto Project(
        EfHatchRunnerSession session, EfHatchRunnerConfig? ceiling, (string Key, ClaimSnapshot Claim, string? Status)? claim,
        long? siteMaxTokens = null, long? siteMaxTokensPerHour = null)
    {
        var bounds = Effective(ceiling, session, siteMaxTokens, siteMaxTokensPerHour);

        return new RunnerSessionDto(
            session.Id,
            session.Where,
            session.Kind,
            session.Host,
            session.State,
            session.StartedAt,
            session.LastSeenAt,
            session.EndedAt,
            ClaimKey: claim?.Key,
            Line: claim is { } held ? held.Claim.Chatter : session.Line,
            LineAt: claim is { } at ? at.Claim.ChatterAt : session.LineAt,
            ClaimStatus: claim?.Status,
            Under: bounds.Under,
            MaxRuns: bounds.MaxRuns.Value,
            MaxSpend: bounds.MaxSpend.Value,
            MaxTokens: bounds.MaxTokens.Value,
            MaxTokensPerHour: bounds.MaxTokensPerHour.Value,
            UntilAt: bounds.UntilAt.Value,
            Runs: session.Runs,
            Spent: session.Spent,
            TokensUsed: session.TokensUsed,
            TokensLastHour: session.TokensLastHour,
            ExhaustedUntil: session.ExhaustedUntil,
            ThrottledUntil: session.ThrottledUntil,
            Usage: session.Usage,
            UsageReadAt: session.UsageReadAt,
            Remotes: session.Remotes?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [],
            Clones: session.Clones,
            Mine: session.Mine,
            Standing: session.Standing,
            Agent: session.Agent,
            Revision: session.Revision,
            Sequence: session.Sequence,
            BuiltAt: session.BuiltAt,
            SourceRevision: session.SourceRevision,
            SourceSequence: session.SourceSequence,
            Projects: session.Projects?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [],
            RefusesDangerous: session.RefusesDangerous,
            OrderKind: session.OrderKind,
            OrderKey: session.OrderKey,
            OrderedBy: session.OrderedBy,
            OrderedAt: session.OrderedAt,
            Os: session.Os,
            Arch: session.Arch,
            AgentVersion: session.AgentVersion,
            Container: session.Container,
            Username: session.Username,
            ProcessStartedAt: session.ProcessStartedAt,
            Ip: session.Ip,
            Image: session.Image);
    }

    /// <summary>
    /// What a session heartbeat reads back off its own beat:
    /// <see cref="Effective"/>'s six bounds and the ceiling's own tags where
    /// there is a ceiling - <see cref="Runners.Instruct"/>'s own shape, for a
    /// session rather than a bare beat.
    /// </summary>
    /// <remarks>
    /// <see cref="RunnerInstructionDto.Order"/> is answered null here: it has
    /// no session-level meaning. <see cref="RunnerInstructionDto.Drives"/> is
    /// the same election the row heartbeat reads, passed in by the caller.
    /// </remarks>
    public static RunnerInstructionDto Instruct(
        EfHatchRunnerSession session, EfHatchRunnerConfig? ceiling, string runnerName, string? forName,
        IReadOnlyList<string> drives, long? siteMaxTokens = null, long? siteMaxTokensPerHour = null)
    {
        var bounds = Effective(ceiling, session, siteMaxTokens, siteMaxTokensPerHour);
        var tags = ceiling is not null ? RunnerTagColumn.Read(ceiling.Tags).Order(StringComparer.Ordinal).ToArray() : [];
        var refusedModels = ceiling is not null ? RunnerTagColumn.Read(ceiling.RefusedModels).Order(StringComparer.Ordinal).ToArray() : [];

        return new RunnerInstructionDto(
            session.State, bounds.Under, bounds.MaxRuns.Value, bounds.MaxSpend.Value, bounds.UntilAt.Value, forName,
            Drives: drives,
            Tags: tags,
            Order: null,
            MaxTokens: bounds.MaxTokens.Value,
            MaxTokensPerHour: bounds.MaxTokensPerHour.Value,
            RunnerName: runnerName,
            SessionId: session.Id,
            RefusedModels: refusedModels);
    }

    /// <summary>
    /// The smaller of a session's own value and a ceiling, with which of the
    /// two it came from - a tie reads as the session's, so a session that
    /// matches the ceiling exactly is never reported as the ceiling binding
    /// it. Either side absent is the other side, undisputed; both absent is
    /// no bound at all.
    /// </summary>
    private static Narrowed<T> Narrow<T>(T? session, T? ceiling, string ceilingSource) where T : struct, IComparable<T>
    {
        if (session is { } s && ceiling is { } c) return s.CompareTo(c) <= 0 ? new(s, "session") : new(c, ceilingSource);
        if (session is { } soloSession) return new(soloSession, "session");
        if (ceiling is { } soloCeiling) return new(soloCeiling, ceilingSource);
        return new(null, "none");
    }

    /// <summary>
    /// A token ceiling as the config's own value, else the site default, else
    /// none - <see cref="Runners.Effective"/>'s own rule, repeated here because
    /// that one is private to the beat it projects.
    /// </summary>
    private static (long? Value, string Source) CeilingToken(long? own, long? site) =>
        own is not null ? (own, "runner")
        : site is not null ? (site, "default")
        : (null, "none");
}

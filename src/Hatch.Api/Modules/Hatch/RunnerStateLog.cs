using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Writes down every change of a runner's state with its instant, so the
/// state route and the lanes on the Runners tile have a record to read.
/// </summary>
/// <remarks>
/// <para>There is no background job. A state the clock ended - an exhausted
/// stretch that ran out, a runner that went quiet past the limit - is written,
/// at the instant the clock ended it, by the next call that touches the runner:
/// <see cref="SettleAsync"/> appends those rows from the facts as they stand,
/// and <see cref="RecordAsync"/> settles and then appends the state the
/// runner is in now. A caller settles <em>before</em> it mutates, because the
/// write overwrites the facts the gap is judged by, and records after the save
/// that carries the mutation.</para>
///
/// <para>A singleton like <see cref="RunnerSessions"/>, holding only the
/// configured limit. <c>IAuthService</c> is scoped, so a call takes the
/// controller's own and finds the key by name with it, the way
/// <see cref="Dispatch"/> does.</para>
///
/// <para>A runner is one record whatever number of sessions its key holds: the
/// sources are the heartbeat row by the name and every session of the key by
/// that name, judged together by <see cref="RunnerStateRule"/>.</para>
/// </remarks>
public sealed class RunnerStateLog(Runners runners)
{
    /// <summary>
    /// Appends every change the clock alone has made since the last row, each at
    /// the instant it happened, and saves. Does nothing for a runner with no
    /// row yet: the first row is <see cref="RecordAsync"/>'s.
    /// </summary>
    public async Task SettleAsync(HatchContext db, IAuthService keys, string name, DateTimeOffset now, CancellationToken ct)
    {
        var last = await LastAsync(db, name, ct);
        if (last is null) return;

        var sources = await SourcesAsync(db, keys, name, ct);
        if (Append(db, name, last, RunnerStateRule.ClockChanges(sources, GoneAfter, last.At, now))) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The changes the clock alone has made since <paramref name="after"/>, up to
    /// <paramref name="now"/>, judged from the facts as they stand. The read behind
    /// the states route: it shows what <see cref="SettleAsync"/> would write, and
    /// writes nothing.
    /// </summary>
    public async Task<IReadOnlyList<RunnerStateRule.Change>> ClockTailAsync(
        HatchContext db, IAuthService keys, string name, DateTimeOffset after, DateTimeOffset now, CancellationToken ct)
    {
        var sources = await SourcesAsync(db, keys, name, ct);
        return RunnerStateRule.ClockChanges(sources, GoneAfter, after, now);
    }

    /// <summary>
    /// The changes the clock alone has made since <paramref name="after"/>, up to
    /// <paramref name="now"/>, from sources already in hand: the same judgement
    /// as <see cref="ClockTailAsync"/>, with no query. The lanes on the board
    /// health read use it once for each runner.
    /// </summary>
    public IReadOnlyList<RunnerStateRule.Change> ClockTail(
        IReadOnlyList<RunnerStateRule.Source> sources, DateTimeOffset after, DateTimeOffset now) =>
        RunnerStateRule.ClockChanges(sources, GoneAfter, after, now);

    /// <summary>
    /// The sources of a runner already joined: the heartbeat row and every
    /// session of its key, as <see cref="SourcesAsync"/> builds them from the
    /// store. <see cref="Runner.JoinAsync"/> matches the heartbeat to the key by
    /// name and groups the sessions by key, so the two agree.
    /// </summary>
    public static IReadOnlyList<RunnerStateRule.Source> SourcesOf(Runner runner)
    {
        var sources = new List<RunnerStateRule.Source>();

        if (runner.Beat is { } beat)
        {
            sources.Add(new RunnerStateRule.Source(
                beat.LastSeenAt, null, beat.RetiredAt is not null, beat.State, beat.ExhaustedUntil, beat.AgentDownUntil, beat.AgentDownKind));
        }

        foreach (var s in runner.Sessions ?? [])
        {
            sources.Add(new RunnerStateRule.Source(
                s.LastSeenAt, s.EndedAt, false, s.State, s.ExhaustedUntil, s.AgentDownUntil, s.AgentDownKind));
        }

        return sources;
    }

    /// <summary>
    /// Settles, then appends one row at <paramref name="now"/> if the runner's
    /// state now differs from the last row, and saves. A runner with no heartbeat
    /// row and no session has no state and writes nothing; a beat that changed
    /// nothing writes nothing.
    /// </summary>
    public async Task RecordAsync(HatchContext db, IAuthService keys, string name, DateTimeOffset now, CancellationToken ct)
    {
        var sources = await SourcesAsync(db, keys, name, ct);
        if (sources.Count == 0) return;

        var last = await LastAsync(db, name, ct);
        var wrote = last is not null
            && Append(db, name, last, RunnerStateRule.ClockChanges(sources, GoneAfter, last.At, now));

        var current = RunnerStateRule.At(sources, GoneAfter, now);
        var latest = Latest(db, name, last);
        if (latest is null || latest.State != current.State)
        {
            db.RunnerStates.Add(new EfHatchRunnerState
            {
                Runner = name,
                At = Clamp(RowInstant(sources, current, now), latest),
                State = current.State,
                Kind = current.Kind,
            });
            wrote = true;
        }

        if (wrote) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// When the new row began. Now, except for gone: a runner whose last source
    /// went gone before this call (a retirement writes no instant of its own)
    /// became gone then, not now.
    /// </summary>
    private DateTimeOffset RowInstant(IReadOnlyList<RunnerStateRule.Source> sources, RunnerStateRule.Answer current, DateTimeOffset now) =>
        current.State == RunnerStateRule.Gone && RunnerStateRule.GoneAt(sources, GoneAfter) is { } at && at < now ? at : now;

    /// <summary>A row's instant is never earlier than the last row's.</summary>
    private static DateTimeOffset Clamp(DateTimeOffset at, EfHatchRunnerState? latest) =>
        latest is not null && at < latest.At ? latest.At : at;

    private TimeSpan GoneAfter => TimeSpan.FromSeconds(runners.GoneAfterSeconds);

    /// <summary>The newest row written for the runner, by instant and then id.</summary>
    private static Task<EfHatchRunnerState?> LastAsync(HatchContext db, string name, CancellationToken ct) =>
        db.RunnerStates.AsNoTracking()
            .Where(s => s.Runner == name)
            .OrderByDescending(s => s.At).ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>The last row once the rows this call has added are counted.</summary>
    private static EfHatchRunnerState? Latest(HatchContext db, string name, EfHatchRunnerState? last) =>
        db.ChangeTracker.Entries<EfHatchRunnerState>()
            .Where(e => e.State == EntityState.Added && e.Entity.Runner == name)
            .Select(e => e.Entity)
            .OrderBy(s => s.At)
            .LastOrDefault() ?? last;

    /// <summary>Adds a row for each change, none earlier than the last row. True when it added any.</summary>
    private static bool Append(
        HatchContext db, string name, EfHatchRunnerState last, IReadOnlyList<RunnerStateRule.Change> changes)
    {
        foreach (var change in changes)
        {
            db.RunnerStates.Add(new EfHatchRunnerState
            {
                Runner = name,
                At = change.At < last.At ? last.At : change.At,
                State = change.State,
                Kind = change.Kind,
            });
        }

        return changes.Count > 0;
    }

    /// <summary>
    /// The heartbeat row by name and every session of the key by that name -
    /// ended ones included, since an ended session still has an instant it was
    /// gone from. A name no key holds has no sessions. Read straight from the
    /// store, never from what the context has tracked.
    /// </summary>
    private async Task<IReadOnlyList<RunnerStateRule.Source>> SourcesAsync(
        HatchContext db, IAuthService keys, string name, CancellationToken ct)
    {
        var sources = new List<RunnerStateRule.Source>();

        var beat = await db.LegacyRunners.AsNoTracking().FirstOrDefaultAsync(r => r.Name == name, ct);
        if (beat is not null)
        {
            sources.Add(new RunnerStateRule.Source(
                beat.LastSeenAt, null, beat.RetiredAt is not null, beat.State, beat.ExhaustedUntil, beat.AgentDownUntil, beat.AgentDownKind));
        }

        var key = (await keys.ListApiKeysAsync(ct)).FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.Ordinal));
        if (key is null) return sources;

        var held = await db.RunnerSessions.AsNoTracking().Where(s => s.ApiKeyId == key.Id).ToListAsync(ct);
        foreach (var s in held)
        {
            sources.Add(new RunnerStateRule.Source(
                s.LastSeenAt, s.EndedAt, false, s.State, s.ExhaustedUntil, s.AgentDownUntil, s.AgentDownKind));
        }

        return sources;
    }
}

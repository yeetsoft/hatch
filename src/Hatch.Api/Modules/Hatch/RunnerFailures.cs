using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Keeps a row for each thing a runner cannot do - see
/// <see cref="EfHatchRunnerFailure"/> - and reads the rows back.
/// </summary>
/// <remarks>
/// <para>Two things write them: a loop session's heartbeat
/// (<see cref="ReconcileAsync"/>) and the board's own pause
/// (<see cref="OpenBoardPauseAsync"/>). No route lets a key or a person open,
/// edit or end one, and nothing sweeps: a runner that went quiet keeps its
/// open rows until its next beat settles them.</para>
///
/// <para>A row is <em>current</em> when it is open and the runner is being
/// heard from at its checkout (<see cref="Current"/>). An open row of a quiet
/// runner is read as neither current nor earlier.</para>
/// </remarks>
public static class RunnerFailures
{
    /// <summary>What one beat says about one kind of failure. Silence is no entry at all.</summary>
    private enum Verdict
    {
        /// <summary>The fact fails; the reason rides beside it.</summary>
        Failing,

        /// <summary>The fact is sent and does not fail.</summary>
        Fine,
    }

    /// <summary>
    /// Brings this key's rows at this session's checkout in line with what
    /// the loop beat said: a failing fact with no open row opens one, an open
    /// row whose fact still fails has its last-reported time and reason
    /// refreshed, and an open row whose fact no longer fails ends at this beat.
    /// A fact the beat did not send leaves that kind's row alone.
    /// </summary>
    /// <remarks>
    /// <para>Called for a loop beat only, after the beat's own save: the
    /// caller does not call it for a <c>once</c> beat, which touches nothing.
    /// One read of the open rows, one save. Two beats racing to open the same
    /// row meet the unique index; the loser reads again and settles against
    /// the winner's row, and a second failure is not ours to swallow.</para>
    ///
    /// <para>The agent kinds are read from what the beat said, never from the
    /// session, because a pause the board wrote stops the session taking the
    /// runner's report - so while one stands both are left alone. The
    /// <c>board-paused</c> kind is the opposite: read from the session after
    /// the touch, which covers a pause that passed, one the lift route
    /// cleared, and a seeded session that inherited a standing pause.</para>
    /// </remarks>
    public static async Task ReconcileAsync(
        HatchContext db, Guid apiKeyId, string keyName, Guid? ownerPersonId, EfHatchRunnerSession session, RunnerHeartbeatRequest request,
        RunnerFailureMessages messages, DateTimeOffset now, CancellationToken ct)
    {
        var where = session.Where ?? "";
        var verdicts = Verdicts(session, request, now);

        try
        {
            await SettleAsync(db, apiKeyId, keyName, ownerPersonId, where, verdicts, messages, now, ct);
        }
        catch (DbUpdateException)
        {
            // A racing beat opened the row first. Nothing this attempt tracked
            // is trustworthy - not the added rows, not the edited ones, not the
            // messages it queued for them - so all of it goes, and the second
            // attempt reads what is there. The save was one transaction, so
            // nothing of it persisted; this only clears the context.
            foreach (var entry in db.ChangeTracker.Entries()
                .Where(e => e.Entity is EfHatchRunnerFailure or EfHatchChatSubject or EfHatchChatMessage or EfHatchChatLink)
                .ToList())
                entry.State = EntityState.Detached;

            await SettleAsync(db, apiKeyId, keyName, ownerPersonId, where, verdicts, messages, now, ct);
        }
    }

    /// <summary>One attempt: read the open rows, apply the verdicts, save once, trim if a row ended.</summary>
    private static async Task SettleAsync(
        HatchContext db, Guid apiKeyId, string keyName, Guid? ownerPersonId, string where,
        IReadOnlyList<(string Kind, Verdict Verdict, string? Why)> verdicts,
        RunnerFailureMessages messages, DateTimeOffset now, CancellationToken ct)
    {
        if (verdicts.Count == 0) return;

        var open = await db.RunnerFailures
            .Where(f => f.ApiKeyId == apiKeyId && f.Where == where && f.EndedAt == null)
            .ToListAsync(ct);

        var ended = false;
        foreach (var (kind, verdict, why) in verdicts)
        {
            var row = open.FirstOrDefault(f => f.Kind == kind);
            switch (verdict)
            {
                case Verdict.Failing when row is null:
                    var opened = Open(apiKeyId, where, kind, why, now);
                    db.RunnerFailures.Add(opened);
                    await messages.OpenedAsync(opened, keyName, ownerPersonId, ct);
                    break;
                case Verdict.Failing:
                    row.LastReportedAt = now;
                    if (row.Why != why)
                    {
                        row.Why = why;
                        await messages.RefreshedAsync(row, keyName, ct);
                    }

                    break;
                case Verdict.Fine when row is not null:
                    row.EndedAt = now;
                    ended = true;
                    await messages.EndedAsync(row, keyName, now, ct);
                    break;
            }
        }

        await db.SaveChangesAsync(ct);
        if (ended) await TrimHistoryAsync(db, apiKeyId, ct);
    }

    /// <summary>What this beat says about each kind it says anything about.</summary>
    private static List<(string Kind, Verdict Verdict, string? Why)> Verdicts(
        EfHatchRunnerSession session, RunnerHeartbeatRequest request, DateTimeOffset now)
    {
        var said = new List<(string, Verdict, string?)>(5);

        // Any word the board knows but `fails` is a sent word that does not fail;
        // a word it does not know was stored as no answer, and clears the session's own.
        if (request.Forge is not null)
        {
            var failing = RunnersController.NormaliseForge(request.Forge) == RunnerForge.Fails;
            said.Add((RunnerFailureKinds.Forge, failing ? Verdict.Failing : Verdict.Fine,
                failing ? RunnersController.ForgeWhy(request.Forge, request.ForgeWhy) : null));
        }

        if (request.Signing is not null)
        {
            var failing = RunnersController.NormaliseSigning(request.Signing) == RunnerSigning.Fails;
            said.Add((RunnerFailureKinds.Signing, failing ? Verdict.Failing : Verdict.Fine,
                failing ? RunnersController.SigningWhy(request.Signing, request.SigningWhy) : null));
        }

        var standing = session.AgentDownKind == RunnerAgentDownKinds.BoardPaused && session.AgentDownUntil > now;

        // While the board's pause stands the session ignored the runner's report,
        // so the report is no evidence of anything.
        if (!standing && request.AgentDown is { } down)
        {
            var kind = down ? RunnersController.NormaliseAgentDownKind(request.AgentDownKind) : null;
            var why = RunnersController.Fits(request.AgentDownLine, EfHatchRunnerFailure.MaxWhyLength);

            said.Add((RunnerFailureKinds.AgentSignedOut,
                kind == RunnerAgentDownKinds.SignedOut ? Verdict.Failing : Verdict.Fine, why));
            said.Add((RunnerFailureKinds.AgentNotStarting,
                kind == RunnerAgentDownKinds.NotStarting ? Verdict.Failing : Verdict.Fine, why));
        }

        said.Add((RunnerFailureKinds.BoardPaused, standing ? Verdict.Failing : Verdict.Fine, standing ? session.AgentDownLine : null));

        return said;
    }

    /// <summary>
    /// Opens the board-paused row for a pause the board has just written, or
    /// refreshes the one already open, <em>without saving</em>: the caller's
    /// own save carries it, so the row, its chat message and the pause commit together.
    /// </summary>
    public static async Task OpenBoardPauseAsync(
        HatchContext db, Guid apiKeyId, string keyName, Guid? ownerPersonId, string? where, string? why,
        RunnerFailureMessages messages, DateTimeOffset now, CancellationToken ct)
    {
        var at = where ?? "";
        var row = await db.RunnerFailures.FirstOrDefaultAsync(
            f => f.ApiKeyId == apiKeyId && f.Where == at && f.Kind == RunnerFailureKinds.BoardPaused && f.EndedAt == null, ct);

        if (row is null)
        {
            var opened = Open(apiKeyId, at, RunnerFailureKinds.BoardPaused, why, now);
            db.RunnerFailures.Add(opened);
            await messages.OpenedAsync(opened, keyName, ownerPersonId, ct);
        }
        else
        {
            row.LastReportedAt = now;
            if (row.Why != why)
            {
                row.Why = why;
                await messages.RefreshedAsync(row, keyName, ct);
            }
        }
    }

    /// <summary>
    /// Links a ticket to the open row of this kind a runner has at a checkout,
    /// <em>without saving</em>: the release's own save carries it. One link per
    /// row and issue; no open row links nothing.
    /// </summary>
    public static async Task LinkIssueAsync(
        HatchContext db, Guid apiKeyId, string? where, string kind, long issueId, DateTimeOffset now, CancellationToken ct)
    {
        var at = where ?? "";
        var row = await db.RunnerFailures.AsNoTracking().FirstOrDefaultAsync(
            f => f.ApiKeyId == apiKeyId && f.Where == at && f.Kind == kind && f.EndedAt == null, ct);
        if (row is null) return;

        if (await db.RunnerFailureIssues.AnyAsync(l => l.FailureId == row.Id && l.IssueId == issueId, ct)) return;

        db.RunnerFailureIssues.Add(new EfHatchRunnerFailureIssue { FailureId = row.Id, IssueId = issueId, At = now });
    }

    private static EfHatchRunnerFailure Open(Guid apiKeyId, string where, string kind, string? why, DateTimeOffset now) => new()
    {
        ApiKeyId = apiKeyId,
        Where = where,
        Kind = kind,
        Why = why,
        BeganAt = now,
        LastReportedAt = now,
    };

    /// <summary>
    /// Deletes this key's ended rows past the newest <see cref="EfHatchRunnerFailure.History"/>,
    /// in the shape <see cref="RunnerSessions.TrimHistoryAsync"/> takes.
    /// </summary>
    private static Task<int> TrimHistoryAsync(HatchContext db, Guid apiKeyId, CancellationToken ct) =>
        db.RunnerFailures
            .Where(f => f.ApiKeyId == apiKeyId && f.EndedAt != null && db.RunnerFailures
                .Where(o => o.ApiKeyId == apiKeyId && o.EndedAt != null)
                .OrderByDescending(o => o.EndedAt)
                .ThenByDescending(o => o.Id)
                .Skip(EfHatchRunnerFailure.History)
                .Select(o => o.Id)
                .Contains(f.Id))
            .ExecuteDeleteAsync(ct);

    /// <summary>
    /// The current failures of one runner: its key's open rows at a checkout a
    /// live loop session is at, newest began first. No key, or no live loop
    /// session, means none - a runner gone quiet shows no current failure.
    /// </summary>
    /// <param name="open">Open rows, of any key; only this runner's are read.</param>
    /// <param name="tickets">The linked issue keys by row id, as <see cref="TicketsAsync"/> reads them; none where null.</param>
    public static IReadOnlyList<RunnerFailureDto> Current(
        IEnumerable<EfHatchRunnerFailure> open, Runner runner, RunnerSessions sessions, DateTimeOffset now,
        IReadOnlyDictionary<long, IReadOnlyList<string>>? tickets = null)
    {
        if (runner.Key is not { } key || runner.Sessions is not { Count: > 0 } own) return [];

        var here = own
            .Where(s => s.Kind == EfHatchRunner.LoopKind && sessions.IsLive(s, now))
            .Select(s => s.Where ?? "")
            .ToHashSet(StringComparer.Ordinal);
        if (here.Count == 0) return [];

        return open
            .Where(f => f.ApiKeyId == key.Id && f.EndedAt is null && here.Contains(f.Where))
            .OrderByDescending(f => f.BeganAt).ThenByDescending(f => f.Id)
            .Select(f => Project(f, tickets))
            .ToList();
    }

    /// <summary>The key's ended rows, newest ended first, at most <see cref="EfHatchRunnerFailure.History"/>.</summary>
    public static async Task<IReadOnlyList<RunnerFailureDto>> EarlierAsync(HatchContext db, Guid apiKeyId, CancellationToken ct)
    {
        var rows = await db.RunnerFailures.AsNoTracking()
            .Where(f => f.ApiKeyId == apiKeyId && f.EndedAt != null)
            .OrderByDescending(f => f.EndedAt).ThenByDescending(f => f.Id)
            .Take(EfHatchRunnerFailure.History)
            .ToListAsync(ct);

        var tickets = await TicketsAsync(db, rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(f => Project(f, tickets)).ToList();
    }

    /// <summary>The most issue keys one row carries on the wire.</summary>
    public const int MaxTickets = 20;

    /// <summary>
    /// The keys of the issues linked to each of these rows, newest link first,
    /// at most <see cref="MaxTickets"/> a row. One query; a row with none is
    /// not in the answer.
    /// </summary>
    public static async Task<IReadOnlyDictionary<long, IReadOnlyList<string>>> TicketsAsync(
        HatchContext db, IReadOnlyCollection<long> failureIds, CancellationToken ct)
    {
        if (failureIds.Count == 0) return new Dictionary<long, IReadOnlyList<string>>();

        var links = await db.RunnerFailureIssues.AsNoTracking()
            .Where(l => failureIds.Contains(l.FailureId))
            .OrderByDescending(l => l.At).ThenByDescending(l => l.Id)
            .Select(l => new { l.FailureId, ProjectKey = l.Issue!.Project!.Key, l.Issue.Number })
            .ToListAsync(ct);

        return links
            .GroupBy(l => l.FailureId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Take(MaxTickets).Select(l => IssueKey.Format(l.ProjectKey, l.Number)).ToList());
    }

    /// <summary>One row as the wire shape; the empty checkout is none.</summary>
    private static RunnerFailureDto Project(EfHatchRunnerFailure f, IReadOnlyDictionary<long, IReadOnlyList<string>>? tickets) =>
        new(f.Kind, f.Where.Length == 0 ? null : f.Where, f.Why, f.BeganAt, f.LastReportedAt, f.EndedAt)
        {
            Tickets = tickets is not null && tickets.TryGetValue(f.Id, out var keys) ? keys : [],
        };
}

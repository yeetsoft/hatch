using System.Text.Json;
using Hatch.Api.Ef;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The reads behind the board health fold, bounded in the query: the window's
/// own read (<see cref="BoardHealthController"/>) and the kept thirty-day one
/// (<see cref="WaitHistory"/>) both go through here, so the two cut an issue's
/// trail the same way. Every read runs on the context it is given and so on
/// that caller's own query filters.
/// </summary>
internal static class BoardHealthTrail
{
    /// <summary>One event, as the fold's inputs are cut from it.</summary>
    internal sealed record Ev(long IssueId, long Id, string Kind, DateTimeOffset At, string Actor, string? Payload);

    /// <summary>A row per issue, every project the context can see: a parent outside the scope still carries a level down.</summary>
    internal static async Task<List<BoardHealthReport.Issue>> IssuesAsync(HatchContext db, CancellationToken ct) =>
        (await db.Issues.AsNoTracking()
                .Select(i => new
                {
                    i.Id,
                    i.ParentId,
                    i.ProjectId,
                    ProjectKey = i.Project!.Key,
                    i.Number,
                    i.Type,
                    i.StatusId,
                    i.Rank,
                    i.CreatedAt,
                    i.CreatedBy,
                    i.PreApproval,
                    i.PullRequestUrl,
                })
                .ToListAsync(ct))
            .Select(i => new BoardHealthReport.Issue(
                new TimelineReport.Row(
                    i.Id, i.ParentId, i.ProjectId, i.ProjectKey, i.Number, i.Type,
                    i.StatusId, i.Rank, i.CreatedAt, i.CreatedBy),
                i.PreApproval,
                !string.IsNullOrWhiteSpace(i.PullRequestUrl)))
            .ToList();

    /// <summary>The status changes from <paramref name="since"/>, with the newest one before it for each issue.</summary>
    internal static async Task<List<BoardHealthReport.Move>> MovesAsync(
        HatchContext db, DateTimeOffset since, HashSet<string>? scope, CancellationToken ct) =>
        (await ReadAsync(db, [EfHatchIssueEvent.StatusChanged], since, scope, null, ct))
            .Select(e => new BoardHealthReport.Move(e.IssueId, e.Id, e.At, e.Actor, e.Payload))
            .ToList();

    /// <summary>The pre-approval changes of every issue from <paramref name="since"/>, with the newest one before it for each.</summary>
    internal static async Task<List<BoardHealthReport.PreApprovalChange>> LevelsAsync(
        HatchContext db, DateTimeOffset since, CancellationToken ct)
    {
        var levels = new List<BoardHealthReport.PreApprovalChange>();
        foreach (var e in await ReadAsync(db, [EfHatchIssueEvent.PreApprovalChanged], since, null, null, ct))
        {
            if (Sides(e.Payload) is not { } sides) continue;
            levels.Add(new BoardHealthReport.PreApprovalChange(
                e.IssueId, e.Id, e.At, sides.From ?? PreApprovalLevels.NoneName, sides.To ?? PreApprovalLevels.NoneName));
        }

        return levels;
    }

    /// <summary>The pull request record changes from <paramref name="since"/>, with the newest one before it for each issue.</summary>
    internal static async Task<List<BoardHealthReport.PullRequestChange>> RecordsAsync(
        HatchContext db, DateTimeOffset since, HashSet<string>? scope, CancellationToken ct)
    {
        var records = new List<BoardHealthReport.PullRequestChange>();
        foreach (var e in await ReadAsync(db, [EfHatchIssueEvent.PullRequestChanged], since, scope, null, ct))
        {
            if (Sides(e.Payload) is not { } sides) continue;
            records.Add(new BoardHealthReport.PullRequestChange(
                e.IssueId, e.Id, e.At, !string.IsNullOrWhiteSpace(sides.From), !string.IsNullOrWhiteSpace(sides.To)));
        }

        return records;
    }

    /// <summary>
    /// The events of these kinds at or after <paramref name="since"/>, plus, for
    /// each issue, the newest one of them before it - which is the state the
    /// window opens on. Bounded in the query, so history older than that is
    /// never loaded.
    /// </summary>
    /// <remarks>
    /// <para>Three reads, not one filter. The window is a range on the event
    /// indexes. The older ones are driven from <c>Issues</c>: for each issue and
    /// kind, one probe of (<c>IssueId</c>, <c>Kind</c>, <c>At</c>) for the time of
    /// the newest event before <paramref name="since"/>, and then one read of the
    /// rows at those times, which settles a tie by <c>Id</c>. The probe reads
    /// every issue, visible or not, and the read of the rows applies the
    /// caller's project visibility, so what is returned is the same. An <c>OR</c> against a per-row subquery cannot use an index,
    /// so the filter this replaced visited every event of these kinds.</para>
    /// <para>The newest is the latest by <c>At</c>, then by <c>Id</c>, over every
    /// kind asked for. <paramref name="olderOnlyIf"/> keeps it only when it is
    /// of this kind, so a take that a later release closed before the window is
    /// dropped and not read as open.</para>
    /// </remarks>
    internal static async Task<List<Ev>> ReadAsync(
        HatchContext db, string[] kinds, DateTimeOffset since, HashSet<string>? scope, List<long>? issueIds,
        CancellationToken ct, string? olderOnlyIf = null)
    {
        var window = db.IssueEvents.AsNoTracking()
            .Where(e => e.At >= since && kinds.Contains(e.Kind));

        var drivers = db.Issues.AsNoTracking().AsQueryable();

        if (scope is not null)
        {
            var keys = scope.ToList();
            window = window.Where(e => keys.Contains(e.Issue!.Project!.Key));
            drivers = drivers.Where(i => keys.Contains(i.Project!.Key));
        }

        if (issueIds is not null)
        {
            window = window.Where(e => issueIds.Contains(e.IssueId));
            drivers = drivers.Where(i => issueIds.Contains(i.Id));
        }

        var found = await window
            .Select(e => new Ev(e.IssueId, e.Id, e.Kind, e.At, e.Actor, e.Payload))
            .ToListAsync(ct);

        // One probe per issue and kind: the time of the newest event before the
        // window, as a correlated scalar MAX, which Postgres answers with one
        // backward probe of (IssueId, Kind, At). Two shapes were tried and
        // dropped: ORDER BY At, Id LIMIT 1 is matched exactly by the table-wide
        // (At, Id) index, which the planner then walks backward for every issue
        // that has no earlier event of the kind; and an At = (max) join is
        // flattened into a scan of every older event of the kind. The opt-out of the
        // query filters is query-wide, so the probe reads every issue and not just
        // the caller's; it only finds instants, and the read of the rows below
        // applies the caller's own filter, so nothing hidden is returned.
        var newest = new Dictionary<(long IssueId, string Kind), DateTimeOffset>();
        foreach (var kind in kinds)
        {
            var k = kind;
            var probed = await drivers
                .Select(i => new
                {
                    i.Id,
                    At = db.IssueEvents.IgnoreQueryFilters()
                        .Where(x => x.IssueId == i.Id && x.Kind == k && x.At < since)
                        .Max(x => (DateTimeOffset?)x.At),
                })
                .ToListAsync(ct);
            foreach (var p in probed)
            {
                if (p.At is { } at) newest[(p.Id, k)] = at;
            }
        }

        if (newest.Count > 0)
        {
            // The rows at those instants for those issues: the probed ones, and any
            // other of the same kind at the same instant, so the tie is settled by
            // Id here. A few more that match an instant but not their own issue's
            // are dropped below.
            var instants = newest.Values.Distinct().ToList();
            var owners = newest.Keys.Select(k => k.IssueId).Distinct().ToList();
            var atThose = await db.IssueEvents.AsNoTracking()
                .Where(e => kinds.Contains(e.Kind) && owners.Contains(e.IssueId) && instants.Contains(e.At) && e.At < since)
                .Select(e => new Ev(e.IssueId, e.Id, e.Kind, e.At, e.Actor, e.Payload))
                .ToListAsync(ct);

            var latestOfKind = atThose
                .Where(e => newest.TryGetValue((e.IssueId, e.Kind), out var at) && at == e.At)
                .GroupBy(e => (e.IssueId, e.Kind))
                .Select(g => g.OrderByDescending(e => e.Id).First());

            foreach (var latest in latestOfKind
                         .GroupBy(e => e.IssueId)
                         .Select(g => g.OrderByDescending(e => e.At).ThenByDescending(e => e.Id).First()))
            {
                if (olderOnlyIf is null || latest.Kind == olderOnlyIf) found.Add(latest);
            }
        }

        return found;
    }

    /// <summary>
    /// The state rows of these runners at or after <paramref name="from"/>, plus
    /// for each name the newest one before it, in time order. Probes
    /// (<c>Runner</c>, <c>At</c>, <c>Id</c>) once per name, so a long history
    /// is never read.
    /// </summary>
    internal static async Task<List<EfHatchRunnerState>> RunnerStatesAsync(
        HatchContext db, List<string> names, DateTimeOffset from, CancellationToken ct)
    {
        var rows = await db.RunnerStates.AsNoTracking()
            .Where(s => s.At >= from && names.Contains(s.Runner))
            .ToListAsync(ct);

        foreach (var name in names.Distinct(StringComparer.Ordinal))
        {
            var n = name;
            var before = await db.RunnerStates.AsNoTracking()
                .Where(x => x.Runner == n && x.At < from)
                .OrderByDescending(x => x.At).ThenByDescending(x => x.Id)
                .Take(1)
                .ToListAsync(ct);
            rows.AddRange(before);
        }

        return rows.OrderBy(s => s.At).ThenBy(s => s.Id).ToList();
    }

    /// <summary>The <c>from</c> and <c>to</c> of a change payload, as text; null when it will not parse.</summary>
    private static (string? From, string? To)? Sides(string? payload)
    {
        if (IssueEventPayload.Parse(payload) is not { ValueKind: JsonValueKind.Object } json) return null;

        string? Side(string name) =>
            json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        return (Side("from"), Side("to"));
    }
}

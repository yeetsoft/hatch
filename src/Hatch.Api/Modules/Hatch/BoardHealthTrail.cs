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
    /// <paramref name="olderOnlyIf"/> keeps that older one only when it is of
    /// this kind. The newest is still found over every kind asked for, so a
    /// take that a later release closed before the window is not read as open.
    /// </remarks>
    internal static async Task<List<Ev>> ReadAsync(
        HatchContext db, string[] kinds, DateTimeOffset since, HashSet<string>? scope, List<long>? issueIds,
        CancellationToken ct, string? olderOnlyIf = null)
    {
        var events = db.IssueEvents.AsNoTracking()
            .Where(e => kinds.Contains(e.Kind))
            .Where(e => e.At >= since
                || ((olderOnlyIf == null || e.Kind == olderOnlyIf)
                    && e.Id == db.IssueEvents
                        .Where(x => x.IssueId == e.IssueId && kinds.Contains(x.Kind) && x.At < since)
                        .Max(x => (long?)x.Id)));

        if (scope is not null)
        {
            var keys = scope.ToList();
            events = events.Where(e => keys.Contains(e.Issue!.Project!.Key));
        }

        if (issueIds is not null) events = events.Where(e => issueIds.Contains(e.IssueId));

        return await events
            .Select(e => new Ev(e.IssueId, e.Id, e.Kind, e.At, e.Actor, e.Payload))
            .ToListAsync(ct);
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

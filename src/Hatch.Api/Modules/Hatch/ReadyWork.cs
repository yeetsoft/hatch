using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// One project's ready work, as <see cref="ReadyWork"/> reads it for HA-2497's
/// two checks: its ready steps (<see cref="Dispatch.ReadyBoardAsync"/>'s own,
/// less anything filed from a finding), which of the live peers able to take
/// one of them hold no live claim at all, and the project's newest
/// <c>claim_taken</c>.
/// </summary>
public sealed record ReadyProject(
    int ProjectId, string ProjectKey, IReadOnlyList<Dispatch.ReadyStep> Steps,
    IReadOnlyList<Dispatch.ReadyPeer> IdlePeers, (long Id, string Key, DateTimeOffset At)? NewestClaim);

/// <summary>A sweep's whole reading of the board's ready work - see <see cref="ReadyWork.ReadAsync"/>.</summary>
public sealed record ReadySnapshot(
    IReadOnlyDictionary<string, ReadyProject> ByProject, IReadOnlyList<Dispatch.KnownRunner> KnownRunners,
    int LiveRunnerCount, string Looked);

/// <summary>
/// The one read <see cref="ReadyUnclaimedCheck"/> and <see cref="ReadyNoRunnerCheck"/>
/// share, so a sweep scans the board once rather than twice and the two can
/// never disagree about what is ready. Scoped like the two checks themselves
/// (see <c>HatchModule</c>), so the same instance - and the one
/// <see cref="ReadySnapshot"/> it memoises - serves both within a single
/// sweep (<see cref="GooseJob"/>'s own scope) and is thrown away with it.
/// </summary>
public sealed class ReadyWork(Dispatch dispatch, HatchContext db, Runners runners, IssueClaims claims, TimeProvider time)
{
    private ReadySnapshot? snapshot;

    public async Task<ReadySnapshot> ReadAsync(CancellationToken ct)
    {
        if (snapshot is not null) return snapshot;

        var now = time.GetUtcNow();
        var board = await dispatch.ReadyBoardAsync(runners.Cutoff(now), ct);

        var filed = new HashSet<long>();
        foreach (var step in board.Steps)
            if (await GooseFindings.FiledFromAFindingAsync(db, step.IssueId, ct)) filed.Add(step.IssueId);

        var ready = board.Steps.Where(s => !filed.Contains(s.IssueId)).ToList();

        var held = (await runners.HeldAsync(db, claims, now, ct)).Keys.ToHashSet();
        var peersById = board.LivePeers.ToDictionary(p => p.Id);

        var newestClaims = await NewestClaimsAsync(ready.Select(s => s.ProjectId).Distinct().ToList(), ct);

        var byProject = new Dictionary<string, ReadyProject>(StringComparer.Ordinal);
        foreach (var group in ready.GroupBy(s => s.ProjectId))
        {
            var steps = group.OrderBy(s => s.Rank).ThenBy(s => s.IssueId).ToList();
            var projectKey = steps[0].ProjectKey;

            var idle = steps
                .SelectMany(s => s.TakenByPeerIds)
                .Distinct(StringComparer.Ordinal)
                .Select(id => peersById.GetValueOrDefault(id))
                .Where(p => p is not null && !held.Contains((p.Name, p.Where)))
                .Select(p => p!)
                .DistinctBy(p => (p.Name, p.Where))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ThenBy(p => p.Where, StringComparer.Ordinal)
                .ToList();

            byProject[projectKey] = new ReadyProject(
                group.Key, projectKey, steps, idle,
                newestClaims.TryGetValue(group.Key, out var claim) ? claim : null);
        }

        var liveRunnerCount = board.LivePeers.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count();
        var looked = $"{byProject.Count} projects, {ready.Count} steps ready, {liveRunnerCount} runners live";

        snapshot = new ReadySnapshot(byProject, board.KnownRunners, liveRunnerCount, looked);
        return snapshot;
    }

    /// <summary>
    /// The newest <see cref="EfHatchIssueEvent.ClaimTaken"/> on each of
    /// <paramref name="projectIds"/>, whatever issue it was taken on - the
    /// token <see cref="ReadyUnclaimedCheck"/> restarts its clock on.
    /// </summary>
    private async Task<Dictionary<int, (long Id, string Key, DateTimeOffset At)>> NewestClaimsAsync(
        IReadOnlyList<int> projectIds, CancellationToken ct)
    {
        if (projectIds.Count == 0) return [];

        var events = await db.IssueEvents.AsNoTracking()
            .Where(e => e.Kind == EfHatchIssueEvent.ClaimTaken && e.Issue != null && projectIds.Contains(e.Issue.ProjectId))
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Select(e => new { e.Id, e.At, ProjectId = e.Issue!.ProjectId, ProjectKey = e.Issue!.Project!.Key, e.Issue!.Number })
            .ToListAsync(ct);

        var newest = new Dictionary<int, (long Id, string Key, DateTimeOffset At)>();
        foreach (var e in events)
            if (!newest.ContainsKey(e.ProjectId))
                newest[e.ProjectId] = (e.Id, IssueKey.Format(e.ProjectKey, e.Number), e.At);

        return newest;
    }
}

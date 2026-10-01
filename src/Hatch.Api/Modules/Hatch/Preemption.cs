using Hatch.Api.Ef;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Who a claim heartbeat tells it has been preempted, decided lazily at each
/// call from the board as it stands - nothing is nominated, reserved or
/// written down in advance, which is what lets an emergency issue walk the
/// board on one runner instead of a saga. See docs/hatch.md, "The
/// dispatcher", for the argument; the five rules below are its "Preemption"
/// section, in the order they are checked.
/// </summary>
/// <remarks>
/// A scoped service, for the reason <see cref="Dispatch"/> is: it holds a
/// scoped <see cref="HatchContext"/>, taken through DI here rather than
/// constructed, via <see cref="Dispatch"/> itself.
/// </remarks>
public sealed class Preemption(HatchContext db, Dispatch dispatch, IssueClaims claims, Runners runners, TimeProvider time)
{
    /// <summary>
    /// The emergency issue this heartbeating issue should be told it was
    /// preempted for, or null to say nothing at all.
    /// </summary>
    public async Task<EfHatchIssue?> ForAsync(EfHatchIssue issue, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        // "actionable for some unattended pass" - the same question GetQueue
        // asks, not "actionable for this caller's own checkout": the caller's
        // own repository situation is what its own next call, after freeing
        // itself, judges.
        var scan = await dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, ct);

        // Rule 2: emergency work is never preempted - this is what makes a
        // surplus queue instead of a cascade. This issue is the caller's own
        // held one, checked by its effective level rather than its own row:
        // a task inheriting emergency from its epic is never told, exactly as
        // one that is emergency on its own row. DependencyGate's tree is
        // whole-board, so Effective answers for this issue whether or not
        // scan.Rows enumerates it.
        if (scan.Gate.Effective(issue.Id).Level >= PriorityLevels.Emergency) return null;

        // Rule 1: some emergency issue is actionable and unclaimed.
        var emergencyRows = scan.Rows
            .Where(r => r.EffectivePriority == PriorityLevels.Emergency && r.Blocked is null)
            .ToList();
        if (emergencyRows.Count == 0) return null;

        // Every live claim below emergency level, carrying the dispatcher's
        // own row order - emergency first, then expedited, then normal, each
        // right to left - which is what makes "last" in rule 3 the board's
        // own order and not a second opinion about it.
        var heldBelow = scan.Rows
            .Select((row, index) => (row, index))
            .Where(x => x.row.EffectivePriority < PriorityLevels.Emergency)
            .Where(x => claims.IsLive(ClaimSnapshot.Of(x.row.Issue), now))
            .ToList();

        // "Already told" is read off the trail rather than stored - see
        // EfHatchIssueEvent.ClaimPreempted. Excluding a told issue from the
        // pool is what lets a second victim be found once the first has been,
        // with nothing nominated in advance.
        var told = await AlreadyToldIdsAsync(
            heldBelow.Select(x => (x.row.Issue.Id, x.row.Issue.ClaimedAt)), ct);

        var untold = heldBelow.Where(x => !told.Contains(x.row.Issue.Id)).ToList();
        if (untold.Count == 0) return null;

        // Rule 3: this issue is last among every live claim below emergency
        // that has not already been told. The index is unique and already in
        // the dispatcher's own order, so there is no tie left to break.
        var last = untold.MaxBy(x => x.index);
        if (last.row.Issue.Id != issue.Id) return null;

        // Rule 4: fewer runners have already been told than there are
        // unclaimed actionable emergency issues.
        if (told.Count >= emergencyRows.Count) return null;

        // Rule 5: no live runner is free. A free runner takes the emergency
        // ticket on its own next pass, because emergency is the top of the
        // walk, so preempting while one exists spends a session to gain
        // nothing.
        if (await AnyRunnerFreeAsync(now, ct)) return null;

        return emergencyRows[0].Issue;
    }

    /// <summary>
    /// Which of these issues already carry a <see cref="EfHatchIssueEvent.ClaimPreempted"/>
    /// event since their own current claim was taken - a stale event from a
    /// previous holder of the same issue must not count against the one
    /// holding it now, the same guard <c>WorkController.LetGoAsync</c> applies
    /// by stopping at a status change, just judged against <c>ClaimedAt</c>
    /// instead, since there is no "declaimed" event.
    /// </summary>
    private async Task<HashSet<long>> AlreadyToldIdsAsync(
        IEnumerable<(long Id, DateTimeOffset? ClaimedAt)> issues, CancellationToken ct)
    {
        var claimedAtById = issues.ToDictionary(i => i.Id, i => i.ClaimedAt);
        if (claimedAtById.Count == 0) return [];

        var ids = claimedAtById.Keys.ToList();
        var events = await db.IssueEvents.AsNoTracking()
            .Where(e => ids.Contains(e.IssueId) && e.Kind == EfHatchIssueEvent.ClaimPreempted)
            .Select(e => new { e.IssueId, e.At })
            .ToListAsync(ct);

        return events
            .Where(e => claimedAtById[e.IssueId] is not { } claimedAt || e.At >= claimedAt)
            .Select(e => e.IssueId)
            .ToHashSet();
    }

    /// <summary>
    /// Whether some live runner holds no claim at all - rule 5, read off the
    /// same pool <see cref="Runners.HeldAsync"/> already gives the runners
    /// page, so there is no second copy of "what is this runner working" to
    /// keep in step.
    /// </summary>
    private async Task<bool> AnyRunnerFreeAsync(DateTimeOffset now, CancellationToken ct)
    {
        var held = await runners.HeldAsync(db, claims, now, ct);

        var live = await db.Runners.AsNoTracking()
            .Where(r => r.LastSeenAt >= runners.Cutoff(now))
            .Select(r => r.Name)
            .ToListAsync(ct);

        return live.Any(name => !held.ContainsKey(name));
    }
}

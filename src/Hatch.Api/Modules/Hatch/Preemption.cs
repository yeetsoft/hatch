using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Who a claim heartbeat tells it has been preempted, decided lazily at each
/// call from the board as it stands - nothing is nominated, reserved or
/// written down in advance, which is what lets an emergency issue walk the
/// board on one runner instead of a saga. See docs/hatch.md, "The
/// dispatcher", for the argument; the five rules below are its "Preemption"
/// section, in the order they are checked.
/// <para>The whole judgement runs on a dispatch scan, and a scan reads every
/// issue on the board, so it runs only when <see cref="AnyOpenEmergencyAsync"/>
/// says an open issue stands at emergency level on its own row or an
/// ancestor's. A board with none answers null from a few indexed reads.</para>
/// </summary>
/// <remarks>
/// A scoped service, for the reason <see cref="Dispatch"/> is: it holds a
/// scoped <see cref="HatchContext"/>, taken through DI here rather than
/// constructed, via <see cref="Dispatch"/> itself.
/// </remarks>
public sealed class Preemption(HatchContext db, Dispatch dispatch, IssueClaims claims, Runners runners, TimeProvider time,
    IAuthService? keys = null)
{
    /// <summary>
    /// The emergency issue this heartbeating issue should be told it was
    /// preempted for, or null to say nothing at all.
    /// </summary>
    public async Task<EfHatchIssue?> ForAsync(EfHatchIssue issue, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        // The cheap question, asked of the database on every beat and never
        // remembered: no open issue at emergency level means rule 1 cannot
        // find one, so nothing below needs the board.
        if (!await AnyOpenEmergencyAsync(ct)) return null;

        // "actionable for some unattended pass" - the same question GetQueue
        // asks, not "actionable for this caller's own checkout": the caller's
        // own repository situation is what its own next call, after freeing
        // itself, judges.
        var scan = await dispatch.ScanAsync(ScanCaller.ClaimHeartbeat, 0, null, null, RepositoryDeclaration.Undeclared, false, ct);

        // Rule 2: emergency work is never preempted - this is what makes a
        // surplus queue instead of a cascade. This issue is the caller's own
        // held one, checked by its effective level rather than its own row:
        // a task inheriting emergency from its epic is never told, exactly as
        // one that is emergency on its own row. DependencyGate's tree is
        // whole-board, so Effective answers for this issue whether or not
        // scan.Rows enumerates it.
        if (scan.Gate.Effective(issue.Id).Level >= PriorityLevels.Emergency) return null;

        // Rule 1: some emergency issue is actionable and unclaimed. The scan
        // above ran only because AnyOpenEmergencyAsync said one might be.
        var emergencyRows = scan.Rows
            .Where(r => r.EffectivePriority == PriorityLevels.Emergency && r.Blocked is null)
            .ToList();
        if (emergencyRows.Count == 0) return null;

        // Every live claim below emergency level, carrying the dispatcher's
        // own row order - emergency first, then an expedited project's rows and
        // then every other project's, each from expedited down to economy, then
        // whatever is paused, each right to left - which is what
        // makes "last" in rule 3 the board's own order and not a second
        // opinion about it.
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

        // Rule 5: no live runner is free - a runner row or a live loop
        // session, whichever record the loop's heartbeat touches. A free one
        // takes the emergency ticket on its own next pass, because emergency
        // is the top of the walk, so preempting while one exists spends a
        // session to gain nothing.
        if (await AnyRunnerFreeAsync(now, ct)) return null;

        return emergencyRows[0].Issue;
    }

    /// <summary>
    /// Whether any open issue is at emergency level, on its own row or
    /// through an ancestor - a superset of what rule 1 can find, so it may say
    /// yes when the scan then finds nothing (an emergency issue that is
    /// blocked, or a level not honoured across a project line), and never says
    /// no where the scan would have found one.
    /// </summary>
    /// <remarks>
    /// <c>PriorityTree.Effective</c> gives an issue the nearest level that is
    /// not normal, from its own row or an ancestor's, so an emergency row on
    /// the issue or an ancestor covers everything rule 1 reads. "Open" is
    /// neither terminal nor deferred, the two flags the scan's walkable
    /// columns are drawn from. The first read takes the emergency rows,
    /// through a partial index. Only when every one is closed or shelved does
    /// the walk go down a generation at a time through <c>ParentId</c>, so a
    /// finished emergency epic with an open task under it still says yes. Each
    /// read is whole-board for the reason the scan's tree is: a level on a
    /// hidden ancestor still applies.
    /// </remarks>
    private async Task<bool> AnyOpenEmergencyAsync(CancellationToken ct)
    {
        var rows = await db.Issues.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.Priority == PriorityLevels.Emergency)
            .Select(i => new { i.Id, Open = !i.Status!.IsTerminal && !i.Status.IsDeferred })
            .ToListAsync(ct);

        if (rows.Count == 0) return false;
        if (rows.Any(r => r.Open)) return true;

        var seen = rows.Select(r => r.Id).ToHashSet();
        var frontier = seen.ToList();

        while (frontier.Count > 0)
        {
            var below = await db.Issues.IgnoreQueryFilters().AsNoTracking()
                .Where(i => i.ParentId != null && frontier.Contains(i.ParentId.Value))
                .Select(i => new { i.Id, Open = !i.Status!.IsTerminal && !i.Status.IsDeferred })
                .ToListAsync(ct);

            if (below.Any(r => r.Open)) return true;

            // The seen set is what stops a parent cycle.
            frontier = below.Select(r => r.Id).Where(seen.Add).ToList();
        }

        return false;
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
    /// keep in step. Both records a loop can heartbeat are read: a runner row
    /// is busy when a live claim is held under its name or at its
    /// <c>where</c> (the beat is bumped by either), and a live loop session
    /// is busy exactly when a live claim is held by its key's name at its
    /// <c>where</c> - the pair the runners page draws a session's claim by.
    /// </summary>
    private async Task<bool> AnyRunnerFreeAsync(DateTimeOffset now, CancellationToken ct)
    {
        var held = await runners.HeldAsync(db, claims, now, ct);
        var holdingNames = held.Keys.Select(k => k.Runner).ToHashSet(StringComparer.Ordinal);
        var holdingWheres = held.Keys.Select(k => k.Where).ToHashSet(StringComparer.Ordinal);
        var cutoff = runners.Cutoff(now);

        var live = await db.LegacyRunners.AsNoTracking()
            .Where(r => r.LastSeenAt >= cutoff)
            .Select(r => new { r.Name, r.Where })
            .ToListAsync(ct);

        if (live.Any(r => !holdingNames.Contains(r.Name) && !holdingWheres.Contains(r.Where ?? "")))
            return true;

        if (keys is null) return false;

        // A session whose key no longer exists is skipped rather than thrown
        // on, as Runners.LiveCandidatesAsync does.
        var liveSessions = await db.RunnerSessions.AsNoTracking()
            .Where(s => s.EndedAt == null && s.LastSeenAt >= cutoff && s.Kind == EfHatchRunner.LoopKind)
            .Select(s => new { s.ApiKeyId, s.Where })
            .ToListAsync(ct);

        if (liveSessions.Count == 0) return false;

        var keysById = (await keys.ListApiKeysAsync(ct)).ToDictionary(k => k.Id);

        return liveSessions
            .Where(s => keysById.ContainsKey(s.ApiKeyId))
            .Any(s => !held.ContainsKey((keysById[s.ApiKeyId].Name, s.Where ?? "")));
    }
}

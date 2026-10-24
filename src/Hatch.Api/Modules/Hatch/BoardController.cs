using System.Linq.Expressions;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The board, in one request: every column and every card in the house.
///
/// One endpoint rather than a column-at-a-time read because the board is the
/// screen the operator lives on (docs/hatch.md, "Goals") and it is refetched
/// after every action - two round trips per drag would be felt, and a board
/// assembled from separate reads can show a card in two columns at once.
///
/// This is also where the WIP meter is read: <see cref="Wip.LoadAsync"/> runs
/// against the same status list and the same instant the cards are drawn
/// against, so the board and its meter never disagree about what is claimed.
/// </summary>
[ApiController]
[Route("api/hatch/board")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class BoardController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, TimeProvider time, DoneHorizon horizon) : ControllerBase
{
    /// <summary>
    /// One row, shared by <see cref="GetBoard"/>'s two card queries - the board
    /// list and the past-horizon list - so a column added to one card's shape
    /// cannot drift from the other's.
    /// </summary>
    private sealed record CardRow(
        long Id,
        string ProjectKey,
        int Number,
        string Type,
        string Title,
        int StatusId,
        long Rank,
        string? ParentProjectKey,
        int? ParentNumber,
        DateTimeOffset? ReadyAt,
        bool ReadyAtHasTime,
        DateTimeOffset? DueAt,
        bool DueAtHasTime,
        Guid? AssigneePersonId,
        Guid? AssigneeApiKeyId,
        int Priority,
        int PreApproval,
        DateTimeOffset? StalledAt,
        string? StalledWhy,
        bool Held,
        string? PullRequestUrl,
        DateTimeOffset? ClosedAt,
        ClaimSnapshot Claim);

    /// <summary>
    /// The projection itself, as an expression tree so it translates to SQL -
    /// the same pattern as <c>PeopleController.ToDto</c>.
    /// </summary>
    private static readonly Expression<Func<EfHatchIssue, CardRow>> Row = i => new CardRow(
        i.Id,
        i.Project!.Key,
        i.Number,
        i.Type,
        i.Title,
        i.StatusId,
        i.Rank,
        i.Parent == null ? null : i.Parent.Project!.Key,
        i.Parent == null ? (int?)null : i.Parent.Number,
        i.ReadyAt,
        i.ReadyAtHasTime,
        i.DueAt,
        i.DueAtHasTime,
        i.AssigneePersonId,
        i.AssigneeApiKeyId,
        i.Priority,
        i.PreApproval,
        i.StalledAt,
        i.StalledWhy,
        i.Held,
        i.PullRequestUrl,
        i.ClosedAt,
        new ClaimSnapshot(
            i.ClaimToken, i.ClaimedBy, i.ClaimRunner,
            i.ClaimedAt, i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt));

    [HttpGet]
    public async Task<ActionResult<BoardDto>> GetBoard(CancellationToken ct)
    {
        var statuses = await db.Statuses.AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        var statusDtos = statuses
            .Select(s => new StatusDto(
                s.Id, s.Name, s.SortOrder, s.IsTerminal, s.IsDeferred, s.IsWip, s.Color, s.ExpressSkips, s.ParentPulls,
                IsImplementation: s.IsImplementation, RunnerTags: RunnerTagColumn.Read(s.RunnerTags)))
            .ToList();

        // One instant for the whole board, so two cards claimed a second apart
        // are not judged against two different clocks - and so the WIP load
        // below is judged against the same clock as every card's own claim.
        var now = time.GetUtcNow();

        var terminalStatusIds = statuses.Where(s => s.IsTerminal).Select(s => s.Id).ToHashSet();
        var cutoff = horizon.Cutoff(now);

        // Ordered by (StatusId, effective priority desc, Rank, Id) so the client can
        // slice the one list into columns without sorting, and so two cards
        // sharing a rank do not trade places between refetches.
        //
        // The float is here rather than in the browser, and that is the whole
        // of it: a higher-priority card is served above every lower-priority
        // card in its column whatever its rank - emergency above expedited
        // above the rest - two cards at the same level keep the board's own
        // (Rank, Id) between them, and the client still slices one ordered
        // list. Sorting in the client would have been a second opinion about
        // where a card sits, and the board, the plan and the queue disagreeing
        // about that is exactly what the server's ordering exists to rule out.
        // One grouped read for the whole board rather than a count per card.
        // A card that is waiting on somebody has to say so here: the board is
        // where the operator looks, and a question they cannot see is a question
        // they never answer.
        var waiting = await Questions.WaitingCountsAsync(db, ct);

        var issuesQuery = db.Issues.AsNoTracking().AsQueryable();
        if (cutoff is { } notBefore)
            issuesQuery = issuesQuery.Where(i =>
                !terminalStatusIds.Contains(i.StatusId) || i.ClosedAt == null || i.ClosedAt >= notBefore);

        var issues = await issuesQuery
            .OrderBy(i => i.StatusId)
            .ThenBy(i => i.Rank)
            .ThenBy(i => i.Id)
            .Select(Row)
            .ToListAsync(ct);

        // The exact complement of the horizon filter above: a story closed
        // before the cutoff, in a terminal column, whose parent epic is still
        // on the board and not itself dropped by the horizon or sitting in a
        // deferred column. One query for the whole board, run only where the
        // horizon is on - with it off there is nothing for it to find.
        var pastRows = cutoff is { } pastNotBefore
            ? await db.Issues.AsNoTracking()
                .Where(i => i.Type == "story"
                    && terminalStatusIds.Contains(i.StatusId)
                    && i.ClosedAt != null && i.ClosedAt < pastNotBefore
                    && i.Parent != null && i.Parent.Type == "epic"
                    && !i.Parent.Status!.IsDeferred
                    && (!terminalStatusIds.Contains(i.Parent.StatusId)
                        || i.Parent.ClosedAt == null || i.Parent.ClosedAt >= pastNotBefore))
                .OrderBy(i => i.StatusId)
                .ThenBy(i => i.Rank)
                .ThenBy(i => i.Id)
                .Select(Row)
                .ToListAsync(ct)
            : [];

        var priorities = await PriorityTree.ForAsync(db, ct);

        List<CardRow> Sort(List<CardRow> rows) => rows
            .OrderBy(r => r.StatusId)
            .ThenByDescending(r => priorities.Effective(r.Id).Level)
            .ThenBy(r => r.Rank)
            .ThenBy(r => r.Id)
            .ToList();

        issues = Sort(issues);
        var past = Sort(pastRows);

        // Two more queries for the whole board, however many cards it holds:
        // the directory is memoized per request, so this is not a lookup a card.
        // An id whose person has been deleted or whose key has been revoked
        // resolves to null and the card draws nothing - the same answer the
        // issue page and the dispatcher give, at the same instant.
        var assignees = new Dictionary<long, AssigneeDto?>();
        foreach (var i in issues.Concat(past))
            assignees[i.Id] = await IssueProjection.ToAssigneeAsync(actors, i.AssigneePersonId, i.AssigneeApiKeyId, ct);

        var wip = await Wip.LoadAsync(db, claims, statuses, now, ct);

        IssueCardDto Card(CardRow i)
        {
            var (effective, effectiveFrom) = priorities.Effective(i.Id);
            var (effectivePreApproval, effectivePreApprovalFrom) = priorities.EffectivePreApproval(i.Id);
            var (onePr, onePrOwn, onePrFrom) = priorities.EffectiveOnePr(i.Id);
            return new IssueCardDto(
                IssueKey.Format(i.ProjectKey, i.Number),
                i.ProjectKey,
                i.Type,
                i.Title,
                i.StatusId,
                i.Rank,
                i.ParentNumber is { } number ? IssueKey.Format(i.ParentProjectKey!, number) : null,
                IssueMoment.Format(i.ReadyAt, i.ReadyAtHasTime),
                IssueMoment.Format(i.DueAt, i.DueAtHasTime),
                waiting.GetValueOrDefault(i.Id),
                assignees[i.Id],
                claims.Project(i.Claim, now),
                effective >= PriorityLevels.Expedited,
                effectivePreApproval >= PreApprovalLevels.Express,
                PriorityLevels.Name(effective),
                PriorityLevels.Name(i.Priority),
                effectiveFrom,
                PreApproval: PreApprovalLevels.Name(effectivePreApproval),
                PreApprovalOwn: PreApprovalLevels.Name(i.PreApproval),
                PreApprovalFrom: effectivePreApprovalFrom,
                StalledAt: i.StalledAt,
                StalledWhy: i.StalledWhy,
                Held: i.Held,
                PullRequestUrl: i.PullRequestUrl,
                OnePr: onePr,
                OnePrOwn: onePrOwn,
                OnePrFrom: onePrFrom,
                ClosedAt: i.ClosedAt);
        }

        var cards = issues.Select(Card).ToList();
        var pastCards = past.Select(Card).ToList();

        return new BoardDto(
            statusDtos, cards, wip?.ToDto(), horizon.Hours > 0 ? horizon.Hours : (int?)null, pastCards);
    }
}

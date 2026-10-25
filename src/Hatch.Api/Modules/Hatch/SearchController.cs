using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Search: the issues whose words, or whose comments' or work log's words, match
/// what someone typed, narrowed by the same filters the issue list takes and by
/// when they were made and closed.
/// </summary>
/// <remarks>
/// A read, and reads in this module carry no project-membership check - so there
/// is no caller identity here, only the actor directory and claims the cards need.
/// </remarks>
[ApiController]
[Route("api/hatch/search")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class SearchController(
    HatchContext db,
    IActorDirectory actors,
    IssueClaims claims,
    TimeProvider time) : ControllerBase
{
    /// <summary>What a caller who named no limit gets.</summary>
    private const int DefaultLimit = 50;

    /// <summary>The most one page may hold.</summary>
    public const int MaxLimit = 200;

    /// <summary>
    /// The issues matching every filter given, a page at a time.
    /// </summary>
    /// <remarks>
    /// Every parameter is optional and they combine with AND. With words
    /// (<paramref name="q"/>) the best match comes first, closed issues ahead of
    /// open ones among equals; without, newest first, where newest means closed,
    /// or created where still open. A <paramref name="q"/>
    /// that reduces to no terms is no <paramref name="q"/> at all, because an
    /// empty search box means the whole list rather than none of it.
    /// </remarks>
    /// <param name="q">Words to find; each is a prefix, and all must be present.</param>
    /// <param name="createdFrom">
    /// Made at or after this. A bare date is the start of that day, in UTC.
    /// </param>
    /// <param name="createdTo">
    /// Made at or before this. A bare date is through the end of that day, in UTC;
    /// an instant is used exactly.
    /// </param>
    /// <param name="closedFrom">Closed at or after this; an open issue never matches.</param>
    /// <param name="closedTo">Closed at or before this; an open issue never matches.</param>
    /// <param name="limit">How many hits to list, 1 to <see cref="MaxLimit"/>.</param>
    /// <param name="offset">How many hits, best first, to skip before the page starts.</param>
    [HttpGet]
    public async Task<ActionResult<SearchDto>> GetSearch(
        [FromQuery] string? q,
        [FromQuery] int? projectId,
        [FromQuery] string? type,
        [FromQuery] int? statusId,
        [FromQuery] string? createdFrom,
        [FromQuery] string? createdTo,
        [FromQuery] string? closedFrom,
        [FromQuery] string? closedTo,
        [FromQuery] int limit = DefaultLimit,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        if (type is not null && !EfHatchIssue.IsValidType(type))
            return BadRequest($"an issue is one of {string.Join(", ", EfHatchIssue.Types)} - not \"{type}\"");

        if (limit is < 1 or > MaxLimit)
            return BadRequest($"a limit is between 1 and {MaxLimit} - not {limit}");

        if (offset < 0)
            return BadRequest($"an offset is not negative - not {offset}");

        if (!ReadBound(createdFrom, nameof(createdFrom), inclusiveUpper: false, out var createdAfter, out var error) ||
            !ReadBound(createdTo, nameof(createdTo), inclusiveUpper: true, out var createdBefore, out error) ||
            !ReadBound(closedFrom, nameof(closedFrom), inclusiveUpper: false, out var closedAfter, out error) ||
            !ReadBound(closedTo, nameof(closedTo), inclusiveUpper: true, out var closedBefore, out error))
            return BadRequest(error);

        var query = db.Issues.AsNoTracking().AsQueryable();

        if (projectId is { } project) query = query.Where(i => i.ProjectId == project);
        if (type is not null) query = query.Where(i => i.Type == type);
        if (statusId is { } status) query = query.Where(i => i.StatusId == status);

        if (createdAfter is { } cFrom) query = query.Where(i => i.CreatedAt >= cFrom);
        if (createdBefore is { } cTo) query = query.Where(i => i.CreatedAt <= cTo);

        // An open issue has no ClosedAt, and a comparison against null is false
        // in SQL - so it never matches a closed bound, with nothing to special-case.
        if (closedAfter is { } xFrom) query = query.Where(i => i.ClosedAt >= xFrom);
        if (closedBefore is { } xTo) query = query.Where(i => i.ClosedAt <= xTo);

        // Null is "no words", which is the whole list - not "no results".
        var tsquery = SearchQuery.ToTsQuery(q);

        if (tsquery is not null)
        {
            query = query.Where(i =>
                i.SearchVector.Matches(EF.Functions.ToTsQuery(SearchQuery.Config, tsquery)) ||
                db.Comments.Any(c => c.IssueId == i.Id &&
                    c.SearchVector.Matches(EF.Functions.ToTsQuery(SearchQuery.Config, tsquery))) ||
                db.WorkLog.Any(w => w.IssueId == i.Id &&
                    w.SearchVector.Matches(EF.Functions.ToTsQuery(SearchQuery.Config, tsquery))));
        }

        var total = await query.CountAsync(ct);

        var ordered = tsquery is not null
            ? query.OrderByDescending(i => i.SearchVector.Rank(EF.Functions.ToTsQuery(SearchQuery.Config, tsquery)))
                // The bool key is false before true, so this puts the closed
                // issues first among equals, and among those, newest-closed first.
                .ThenBy(i => i.ClosedAt == null)
                .ThenByDescending(i => i.ClosedAt)
                .ThenByDescending(i => i.CreatedAt)
                .ThenByDescending(i => i.Id)
            : query.OrderByDescending(i => i.ClosedAt ?? i.CreatedAt)
                .ThenByDescending(i => i.CreatedAt)
                .ThenByDescending(i => i.Id);

        var page = ordered.Skip(offset).Take(limit);

        // Both arms project the same columns, and so are one type. The match
        // flags are the only difference: without words they are constants, so
        // nothing off the InMemory provider's model (SearchVector is not on it)
        // is touched and the loop below reads one shape.
        var rows = await (tsquery is null
            ? page.Select(i => new
            {
                i.Id,
                ProjectKey = i.Project!.Key,
                i.Number,
                i.Type,
                i.Title,
                i.StatusId,
                i.Rank,
                ParentProjectKey = i.Parent == null ? null : i.Parent.Project!.Key,
                ParentNumber = i.Parent == null ? (int?)null : i.Parent.Number,
                i.ReadyAt,
                i.ReadyAtHasTime,
                i.DueAt,
                i.DueAtHasTime,
                i.AssigneePersonId,
                i.AssigneeApiKeyId,
                i.Priority,
                i.PreApproval,
                i.RunnerTags,
                i.Dangerous,
                i.StalledAt,
                i.StalledWhy,
                i.Held,
                i.PullRequestUrl,
                i.ClosedAt,
                Claim = new ClaimSnapshot(
                    i.ClaimToken, i.ClaimedBy, i.ClaimRunner,
                    i.ClaimedAt, i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt),
                i.CreatedAt,
                TitleMatched = false,
                DescriptionMatched = false,
                CommentMatched = false,
                WorkLogMatched = false,
            })
            : page.Select(i => new
            {
                i.Id,
                ProjectKey = i.Project!.Key,
                i.Number,
                i.Type,
                i.Title,
                i.StatusId,
                i.Rank,
                ParentProjectKey = i.Parent == null ? null : i.Parent.Project!.Key,
                ParentNumber = i.Parent == null ? (int?)null : i.Parent.Number,
                i.ReadyAt,
                i.ReadyAtHasTime,
                i.DueAt,
                i.DueAtHasTime,
                i.AssigneePersonId,
                i.AssigneeApiKeyId,
                i.Priority,
                i.PreApproval,
                i.RunnerTags,
                i.Dangerous,
                i.StalledAt,
                i.StalledWhy,
                i.Held,
                i.PullRequestUrl,
                i.ClosedAt,
                Claim = new ClaimSnapshot(
                    i.ClaimToken, i.ClaimedBy, i.ClaimRunner,
                    i.ClaimedAt, i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt),
                i.CreatedAt,
                TitleMatched = EF.Functions.ToTsVector(SearchQuery.Config, i.Title)
                    .Matches(EF.Functions.ToTsQuery(SearchQuery.Config, tsquery)),
                DescriptionMatched = EF.Functions.ToTsVector(SearchQuery.Config, i.Description)
                    .Matches(EF.Functions.ToTsQuery(SearchQuery.Config, tsquery)),
                CommentMatched = db.Comments.Any(c => c.IssueId == i.Id &&
                    c.SearchVector.Matches(EF.Functions.ToTsQuery(SearchQuery.Config, tsquery))),
                WorkLogMatched = db.WorkLog.Any(w => w.IssueId == i.Id &&
                    w.SearchVector.Matches(EF.Functions.ToTsQuery(SearchQuery.Config, tsquery))),
            }))
            .ToListAsync(ct);

        var now = time.GetUtcNow();
        var priorities = await PriorityTree.ForAsync(db, ct);

        var hits = new List<SearchHitDto>(rows.Count);
        foreach (var i in rows)
        {
            var (effective, effectiveFrom) = priorities.Effective(i.Id);
            var (effectivePreApproval, effectivePreApprovalFrom) = priorities.EffectivePreApproval(i.Id);
            var (onePr, onePrOwn, onePrFrom) = priorities.EffectiveOnePr(i.Id);
            var (effectiveRunnerTags, effectiveRunnerTagsFrom) = priorities.EffectiveRunnerTags(i.Id);
            var (dangerous, dangerousFrom) = priorities.EffectiveDangerous(i.Id);

            // The same card /issues answers, built the same way - a hit that read
            // one way here and another on the board is the divergence
            // IssueCardDto's own docstring exists to prevent.
            var card = new IssueCardDto(
                IssueKey.Format(i.ProjectKey, i.Number),
                i.ProjectKey,
                i.Type,
                i.Title,
                i.StatusId,
                i.Rank,
                i.ParentNumber is { } n ? IssueKey.Format(i.ParentProjectKey!, n) : null,
                IssueMoment.Format(i.ReadyAt, i.ReadyAtHasTime),
                IssueMoment.Format(i.DueAt, i.DueAtHasTime),
                Assignee: await IssueProjection.ToAssigneeAsync(actors, i.AssigneePersonId, i.AssigneeApiKeyId, ct),
                Claim: claims.Project(i.Claim, now),
                Expedited: effective >= PriorityLevels.Expedited,
                Express: effectivePreApproval >= PreApprovalLevels.Express,
                Priority: PriorityLevels.Name(effective),
                PriorityOwn: PriorityLevels.Name(i.Priority),
                RunnerTagsOwn: RunnerTagColumn.Read(i.RunnerTags),
                RunnerTags: effectiveRunnerTags,
                RunnerTagsFrom: effectiveRunnerTagsFrom,
                DangerousOwn: i.Dangerous,
                Dangerous: dangerous,
                DangerousFrom: dangerousFrom,
                PriorityFrom: effectiveFrom,
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

            var matched = new List<string>(4);
            if (i.TitleMatched) matched.Add("title");
            if (i.DescriptionMatched) matched.Add("description");
            if (i.CommentMatched) matched.Add("comment");
            if (i.WorkLogMatched) matched.Add("work-log");

            hits.Add(new SearchHitDto(card, i.CreatedAt, i.ClosedAt, matched));
        }

        return new SearchDto(total, hits);
    }

    /// <summary>
    /// Reads one of the four date bounds off the query string. Blank is "no
    /// bound". A bare date is that day's UTC midnight, which is where a lower
    /// bound wants it; an upper bound wants the day's last instant, so that
    /// "through the 12th" includes the 12th - an instant is used exactly,
    /// on either side.
    /// </summary>
    private static bool ReadBound(
        string? text, string field, bool inclusiveUpper, out DateTimeOffset? at, out string? error)
    {
        at = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text)) return true;

        if (!IssueMoment.TryParse(text, out var moment))
        {
            error = $"\"{text}\" is not {field} - give {IssueMoment.Expected}";
            return false;
        }

        at = inclusiveUpper && !moment.HasTime ? moment.At.AddDays(1).AddTicks(-1) : moment.At;
        return true;
    }
}

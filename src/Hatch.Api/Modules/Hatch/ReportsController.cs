using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Reads of the trail the board leaves behind, rather than of the board as it
/// stands: when the work happened, not what is where.
///
/// All of the arithmetic lives in <see cref="TimelineReport"/>. This controller
/// loads the rows, asks the fold, and decorates what it answers with cards.
/// </summary>
[ApiController]
[Route("api/hatch/reports")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class ReportsController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, TimeProvider time) : ControllerBase
{
    /// <summary>The range when a caller names neither bound: the ninety days ending now.</summary>
    private static readonly TimeSpan DefaultRange = TimeSpan.FromDays(90);

    /// <summary>
    /// When the work happened: the top-level epics active in a range, and the
    /// work beneath each, as stretches in the board's active columns.
    /// </summary>
    /// <param name="from">The start of the range; ninety days before <paramref name="to"/> when absent.</param>
    /// <param name="to">The end of the range; now when absent.</param>
    /// <param name="projectId">Narrows the top-level rows to one project's; what rides beneath them is not cut.</param>
    /// <param name="createdBy">Narrows the top-level rows to one creator's.</param>
    /// <remarks>
    /// Neither query below is bounded by the range or by the project. The fold
    /// needs an issue's first move even when it falls after the range's end -
    /// that move's <c>from</c> is the column the issue was in during it - and
    /// children ride with their root whatever project they were filed in.
    /// </remarks>
    [HttpGet("timeline")]
    public async Task<ActionResult<TimelineDto>> GetTimeline(
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] int? projectId, [FromQuery] string? createdBy, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        if (RangeBounds.Resolve(from, to, now, DefaultRange, out var start, out var end) is { } refusal)
            return BadRequest(refusal);

        if (projectId is { } wanted && !await db.Projects.AnyAsync(p => p.Id == wanted, ct))
            return BadRequest($"there is no project {wanted}");

        var (statuses, rows, changes) = await LoadHistoryAsync(ct);

        DateTimeOffset? first = rows.Count > 0 ? rows.Min(r => r.CreatedAt) : null;
        start = RangeBounds.ClampStart(start, end, first);

        var result = TimelineReport.Fold(rows, changes, statuses, start, end, projectId, createdBy);

        // Cards for exactly the issues in the answer, and none of the rest of
        // the tracker.
        var ids = new HashSet<long>();
        foreach (var epic in result.Epics) CollectIds(epic, ids);
        if (result.Other is { } loose) CollectIds(loose, ids);

        var cards = new Dictionary<long, IssueCardDto>();

        if (ids.Count > 0)
        {
            var priorities = await PriorityTree.ForAsync(db, ct);

            var issues = await db.Issues.AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .Select(i => new
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
                    i.StalledAt,
                    i.StalledWhy,
                    i.Held,
                    i.PullRequestUrl,
                    i.ClosedAt,
                    Claim = new ClaimSnapshot(
                        i.ClaimToken, i.ClaimedBy, i.ClaimRunner,
                        i.ClaimedAt, i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt),
                })
                .ToListAsync(ct);

            // Resolved up front rather than inside the walk below, which is
            // synchronous. The directory is memoized either way.
            var assignees = new Dictionary<long, AssigneeDto?>();
            foreach (var i in issues)
                assignees[i.Id] = await IssueProjection.ToAssigneeAsync(actors, i.AssigneePersonId, i.AssigneeApiKeyId, ct);

            foreach (var i in issues)
            {
                var (effectivePreApproval, effectivePreApprovalFrom) = priorities.EffectivePreApproval(i.Id);
                var (onePr, onePrOwn, onePrFrom) = priorities.EffectiveOnePr(i.Id);

                cards[i.Id] = new IssueCardDto(
                    IssueKey.Format(i.ProjectKey, i.Number),
                    i.ProjectKey,
                    i.Type,
                    i.Title,
                    i.StatusId,
                    i.Rank,
                    i.ParentNumber is { } n ? IssueKey.Format(i.ParentProjectKey!, n) : null,
                    IssueMoment.Format(i.ReadyAt, i.ReadyAtHasTime),
                    IssueMoment.Format(i.DueAt, i.DueAtHasTime),
                    Assignee: assignees[i.Id],
                    Claim: claims.Project(i.Claim, now),
                    Expedited: i.Priority >= PriorityLevels.Expedited,
                    Express: effectivePreApproval >= PreApprovalLevels.Express,
                    Priority: PriorityLevels.Name(i.Priority),
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
        }

        return new TimelineDto(
            start,
            end,
            result.Epics.Select(Entry).ToList(),
            result.Other is { } other ? Entry(other) : null,
            result.Creators,
            result.Unresolved);

        TimelineEntryDto Entry(TimelineReport.Node node) => new(
            node.IssueId is { } id ? cards[id] : OtherCard,
            node.CreatedBy,
            node.IsLeaf,
            node.Segments.Select(s => new TimelineSegmentDto(s.StatusId, s.From, s.To)).ToList(),
            node.Children.Select(Entry).ToList());
    }

    /// <summary>
    /// How many issues sat in each board column over time, read off the same
    /// status history as <see cref="GetTimeline"/> and swept once by
    /// <see cref="CumulativeFlowReport.Fold"/> rather than per issue.
    /// </summary>
    /// <param name="from">The start of the range; ninety days before <paramref name="to"/> when absent.</param>
    /// <param name="to">The end of the range; now when absent.</param>
    [HttpGet("cumulative-flow")]
    public async Task<ActionResult<CumulativeFlowDto>> GetCumulativeFlow(
        [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        if (RangeBounds.Resolve(from, to, now, DefaultRange, out var start, out var end) is { } refusal)
            return BadRequest(refusal);

        var (statuses, rows, changes) = await LoadHistoryAsync(ct);

        var result = CumulativeFlowReport.Fold(rows, changes, statuses, start, end, now);

        return new CumulativeFlowDto(
            result.From,
            result.To,
            result.Step,
            result.Columns,
            result.Points.Select(p => new CumulativeFlowPointDto(p.At, p.Counts)).ToList(),
            result.Unresolved);
    }

    /// <summary>
    /// How work moved through the board over a range: per column, how many
    /// issues entered and left it, how long the ones that left stayed, and how
    /// many stand there at the end - plus the tallies that belong to no one
    /// column, folded by <see cref="FlowReport.Fold"/>. Counts and durations
    /// only; nothing in the answer names an issue.
    /// </summary>
    /// <param name="from">The start of the range; ninety days before <paramref name="to"/> when absent.</param>
    /// <param name="to">The end of the range; now when absent.</param>
    /// <param name="projectId">Narrows every count to one project's issues.</param>
    /// <param name="type">Narrows every count to one issue type.</param>
    /// <remarks>
    /// The status history is loaded whole, as the other reports load it - the
    /// fold needs an issue's moves before the range to know where it stood.
    /// Releases and sessions are read only inside the range, since all they
    /// give is a count.
    /// </remarks>
    [HttpGet("flow")]
    public async Task<ActionResult<FlowDto>> GetFlow(
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] int? projectId, [FromQuery] string? type, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        if (RangeBounds.Resolve(from, to, now, DefaultRange, out var start, out var end) is { } refusal)
            return BadRequest(refusal);

        if (type is not null && !EfHatchIssue.IsValidType(type))
            return BadRequest($"an issue is one of {string.Join(", ", EfHatchIssue.Types)} - not \"{type}\"");

        if (projectId is { } wanted && !await db.Projects.AnyAsync(p => p.Id == wanted, ct))
            return BadRequest($"there is no project {wanted}");

        var (statuses, rows) = await LoadBoardAsync(ct);

        DateTimeOffset? first = rows.Count > 0 ? rows.Min(r => r.CreatedAt) : null;
        start = RangeBounds.ClampStart(start, end, first);

        var moves = await db.IssueEvents.AsNoTracking()
            .Where(e => e.Kind == EfHatchIssueEvent.StatusChanged)
            .Select(e => new { e.IssueId, e.Id, e.Kind, e.At, e.Payload })
            .ToListAsync(ct);

        var releases = await db.IssueEvents.AsNoTracking()
            .Where(e => e.Kind == EfHatchIssueEvent.ClaimReleased && e.At >= start && e.At < end)
            .Select(e => new { e.IssueId, e.Id, e.Kind, e.At, e.Payload })
            .ToListAsync(ct);

        var events = moves.Concat(releases)
            .OrderBy(e => e.IssueId)
            .ThenBy(e => e.At)
            .ThenBy(e => e.Id)
            .Select(e => new FlowReport.Event(e.IssueId, e.Id, e.Kind, e.At, e.Payload))
            .ToList();

        // Plain queries joined here rather than a subquery for the earliest
        // answer: comments rather than events, since the `asked` event carries
        // no question id. A lapse is the one answer the board writes itself, a
        // comment and an `answered` event {questionId, lapsed: true} at the
        // same instant; the comment carries no marker, so the event names it.
        // The wait figures are over a person's answers, so that comment is set
        // aside and the question is counted as lapsed instead.
        //
        // The prefilter is a containment on Postgres, which has no LIKE for
        // jsonb, and a substring on InMemory, which has no containment.
        var lapseAt = new Dictionary<long, DateTimeOffset>();
        var answered = db.IssueEvents.AsNoTracking()
            .Where(e => e.Kind == EfHatchIssueEvent.Answered && e.Payload != null);
        var lapseEvents = await (db.Database.IsNpgsql()
                ? answered.Where(e => EF.Functions.JsonContains(e.Payload!, "{\"lapsed\":true}"))
                : answered.Where(e => e.Payload!.Contains("lapsed")))
            .Select(e => new { e.At, e.Payload })
            .ToListAsync(ct);

        foreach (var e in lapseEvents)
        {
            if (!LetGo.IsLapsedAnswer(e.Payload)) continue;
            if (IssueEventPayload.Parse(e.Payload) is { } payload
                && payload.TryGetProperty("questionId", out var id) && id.TryGetInt64(out var questionId))
                lapseAt[questionId] = e.At;
        }

        var asked = await db.Comments.AsNoTracking()
            .Where(c => c.Kind == EfHatchComment.Question)
            .Select(c => new { c.Id, c.IssueId, c.CreatedAt })
            .ToListAsync(ct);

        var answers = await db.Comments.AsNoTracking()
            .Where(c => c.AnswersId != null)
            .Select(c => new { AnswersId = c.AnswersId!.Value, c.CreatedAt })
            .ToListAsync(ct);

        var earliestAnswer = answers
            .Where(a => !(lapseAt.TryGetValue(a.AnswersId, out var lapsedAt) && lapsedAt == a.CreatedAt))
            .GroupBy(a => a.AnswersId)
            .ToDictionary(g => g.Key, g => g.Min(a => a.CreatedAt));

        var questions = asked
            .Select(q => new FlowReport.Question(
                q.IssueId, q.Id, q.CreatedAt,
                earliestAnswer.TryGetValue(q.Id, out var answeredAt) ? answeredAt : null,
                !earliestAnswer.ContainsKey(q.Id) && lapseAt.TryGetValue(q.Id, out var lapsedAt) ? lapsedAt : null))
            .ToList();

        var sessions = (await db.WorkLog.AsNoTracking()
                .Where(w => w.EndedAt >= start && w.EndedAt < end)
                .Select(w => new { w.IssueId, w.EndedAt, w.IsError })
                .ToListAsync(ct))
            .Select(w => new FlowReport.Session(w.IssueId, w.EndedAt, w.IsError))
            .ToList();

        var result = FlowReport.Fold(rows, events, questions, sessions, statuses, start, end, projectId, type);

        // Decoration only: the fold answers non-deferred board columns, every
        // one of which is in the board just loaded.
        var names = statuses.ToDictionary(s => s.Id, s => s.Name);

        return new FlowDto(
            start,
            end,
            result.Columns
                .Select(c => new FlowColumnDto(
                    c.StatusId, names[c.StatusId], c.Entered, c.Left, c.Dwelled,
                    c.MedianDwellSeconds, c.P90DwellSeconds, c.Standing))
                .ToList(),
            new FlowWindowDto(
                result.Window.Dropped,
                result.Window.Hops.Select(h => new FlowHopDto(h.Kind, h.Count)).ToList(),
                result.Window.SendBacks,
                result.Window.QuestionsAsked,
                result.Window.QuestionsAnswered,
                result.Window.QuestionsLapsed,
                result.Window.MedianAnswerWaitSeconds,
                result.Window.LongestAnswerWaitSeconds,
                result.Window.Sessions,
                result.Window.SessionErrors),
            result.Unresolved);
    }

    /// <summary>The card the loose work is drawn under: no issue, so nothing on it is real.</summary>
    private static readonly IssueCardDto OtherCard = new("other", "", "epic", "Other", 0, 0, null, null, null);

    private static void CollectIds(TimelineReport.Node node, HashSet<long> ids)
    {
        if (node.IssueId is { } id) ids.Add(id);
        foreach (var child in node.Children) CollectIds(child, ids);
    }

    /// <summary>
    /// Every issue and <c>status_changed</c> event in the tracker, the raw
    /// material both reports fold - loaded once here so <see cref="GetTimeline"/>
    /// and <see cref="GetCumulativeFlow"/> read it the same way.
    /// </summary>
    private async Task<(List<EfHatchStatus> Statuses, List<TimelineReport.Row> Rows, List<TimelineReport.Change> Changes)>
        LoadHistoryAsync(CancellationToken ct)
    {
        var (statuses, rows) = await LoadBoardAsync(ct);

        var events = await db.IssueEvents.AsNoTracking()
            .Where(e => e.Kind == EfHatchIssueEvent.StatusChanged)
            .OrderBy(e => e.IssueId)
            .ThenBy(e => e.At)
            .ThenBy(e => e.Id)
            .Select(e => new { e.IssueId, e.Id, e.At, e.Payload })
            .ToListAsync(ct);

        var changes = events
            .Select(e => TimelineReport.ParseChange(e.IssueId, e.Id, e.At, e.Payload))
            .OfType<TimelineReport.Change>()
            .ToList();

        return (statuses, rows, changes);
    }

    /// <summary>The board's statuses in board order, and every issue as the folds want it.</summary>
    private async Task<(List<EfHatchStatus> Statuses, List<TimelineReport.Row> Rows)> LoadBoardAsync(CancellationToken ct)
    {
        var statuses = await db.Statuses.AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        var rows = (await db.Issues.AsNoTracking()
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
                })
                .ToListAsync(ct))
            .Select(i => new TimelineReport.Row(
                i.Id, i.ParentId, i.ProjectId, i.ProjectKey, i.Number, i.Type,
                i.StatusId, i.Rank, i.CreatedAt, i.CreatedBy))
            .ToList();

        return (statuses, rows);
    }
}

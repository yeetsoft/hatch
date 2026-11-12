using System.Text;
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
[Route("api/reports")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class ReportsController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, TimeProvider time,
    IAuthService? keys = null) : ControllerBase
{
    /// <summary>The range when a caller names neither bound: the ninety days ending now.</summary>
    private static readonly TimeSpan DefaultRange = TimeSpan.FromDays(90);

    /// <summary>The range when a caller names neither bound, on this report alone: the changelog feed's own fourteen days.</summary>
    private static readonly TimeSpan DefaultChangelogRange = TimeSpan.FromDays(14);

    /// <summary>The most rows a consumption read will list, past which the request is an export rather than a table.</summary>
    public const int MaxConsumptionRows = 500;

    /// <summary>What a caller who named no limit gets on the consumption read: more than a page draws, far less than the cap.</summary>
    private const int DefaultConsumptionRows = 100;

    /// <summary>
    /// When the work happened: the top-level epics active in a range, and the
    /// work beneath each, as stretches in the board's active columns.
    /// </summary>
    /// <param name="from">The start of the range; ninety days before <paramref name="to"/> when absent.</param>
    /// <param name="to">The end of the range; now when absent.</param>
    /// <param name="projectId">
    /// Narrows the top-level rows to these projects', repeatable; every project
    /// when absent or empty. An id naming no project contributes nothing and is
    /// not a refusal. What rides beneath the top-level rows is not cut.
    /// </param>
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
        [FromQuery] List<int>? projectId, [FromQuery] string? createdBy, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        if (RangeBounds.Resolve(from, to, now, DefaultRange, out var start, out var end) is { } refusal)
            return BadRequest(refusal);

        var projectIds = (projectId ?? []).Distinct().ToList();

        var (statuses, rows, changes) = await LoadHistoryAsync(ct);

        DateTimeOffset? first = rows.Count > 0 ? rows.Min(r => r.CreatedAt) : null;
        start = RangeBounds.ClampStart(start, end, first);

        var result = TimelineReport.Fold(rows, changes, statuses, start, end, projectIds, createdBy);

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
                    i.NoCode,
                    i.CreatedBy,
                    Claim = new ClaimSnapshot(
                        i.ClaimToken, i.ClaimedBy, i.ClaimRunner,
                        i.ClaimedAt, i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt,
                        i.ClaimQuietLimitSeconds, i.ClaimMaxRunSeconds),
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
                    ClosedAt: i.ClosedAt,
                    NoCode: NoCode.OnCard(i.NoCode, i.Id, priorities),
                    CreatedBy: i.CreatedBy);
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
    /// <param name="project">Narrows every count to these projects' issues, repeatable; every project when absent or empty.</param>
    [HttpGet("cumulative-flow")]
    public async Task<ActionResult<CumulativeFlowDto>> GetCumulativeFlow(
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] List<string>? project, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        if (RangeBounds.Resolve(from, to, now, DefaultRange, out var start, out var end) is { } refusal)
            return BadRequest(refusal);

        var keys = WorkController.ProjectKeys(project);
        if (keys.Count > 0)
        {
            var known = await db.Projects.Select(p => p.Key).ToListAsync(ct);
            var unknown = keys.Where(k => !known.Contains(k)).ToList();
            if (unknown.Count > 0) return BadRequest($"there is no project {string.Join(", ", unknown)}");
        }

        var (statuses, rows, changes) = await LoadHistoryAsync(ct);

        var result = CumulativeFlowReport.Fold(
            rows, changes, statuses, start, end, now, keys.Count > 0 ? keys.ToHashSet() : null);

        return new CumulativeFlowDto(
            result.From,
            result.To,
            result.StepMinutes,
            result.Columns,
            result.Projects,
            result.Points.Select(p => new CumulativeFlowPointDto(p.At, p.Counts, p.ProjectCounts)).ToList(),
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
    /// <param name="projectId">
    /// Narrows every count to these projects' issues, repeatable; every
    /// project when absent or empty. An id naming no project contributes
    /// nothing and is not a refusal.
    /// </param>
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
        [FromQuery] List<int>? projectId, [FromQuery] string? type, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        if (RangeBounds.Resolve(from, to, now, DefaultRange, out var start, out var end) is { } refusal)
            return BadRequest(refusal);

        if (type is not null && !EfHatchIssue.IsValidType(type))
            return BadRequest($"an issue is one of {string.Join(", ", EfHatchIssue.Types)} - not \"{type}\"");

        var ids = (projectId ?? []).Distinct().ToList();

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

        var result = FlowReport.Fold(rows, events, questions, sessions, statuses, start, end, ids, type);

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

    /// <summary>
    /// What shipped, a window at a time, bucketed rather than split by day -
    /// wires <see cref="ChangelogReport.Fold"/> to the database and decorates
    /// what it answers with cards, the way <see cref="GetTimeline"/> does.
    /// </summary>
    /// <param name="from">The start of the range; fourteen days before <paramref name="to"/> when absent.</param>
    /// <param name="to">The end of the range; now when absent.</param>
    /// <param name="projectId">Narrows the closed epics to these projects' own, repeatable; every project when absent or empty.</param>
    /// <param name="bucket">One of <c>hour</c>, <c>day</c>, <c>week</c>, <c>month</c>; day when absent.</param>
    /// <param name="offsetMinutes">Minutes east of UTC, so local is UTC plus this - aligns a bucket to the reader's own wall clock.</param>
    /// <param name="level">The depth this report groups at: absent or <c>epic</c> groups closed top-level epics; <c>story</c> groups closed stories under their nearest epic.</param>
    /// <param name="loose">Whether closed bugs (any parent) and parentless stories ride with the epic buckets, each with its priority; "true"/"false", trimmed/case-insensitive - "false" or absent runs no second query.</param>
    [HttpGet("changelog")]
    public async Task<ActionResult<ChangelogReportDto>> GetChangelogReport(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] List<int>? projectId,
        [FromQuery] string? bucket, [FromQuery] int offsetMinutes = 0,
        [FromQuery] string? level = null, [FromQuery] string? loose = null, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();

        if (RangeBounds.Resolve(from, to, now, DefaultChangelogRange, out var start, out var end) is { } refusal)
            return BadRequest(refusal);

        if (offsetMinutes is < -1440 or > 1440)
            return BadRequest($"an offset from UTC is minutes between -1440 and 1440 - not {offsetMinutes}");

        var trimmed = bucket?.Trim();
        ChangelogReport.ChangelogBucketSize size;
        if (string.IsNullOrEmpty(trimmed))
        {
            size = ChangelogReport.ChangelogBucketSize.Day;
        }
        else if (string.Equals(trimmed, "hour", StringComparison.OrdinalIgnoreCase))
        {
            size = ChangelogReport.ChangelogBucketSize.Hour;
        }
        else if (string.Equals(trimmed, "day", StringComparison.OrdinalIgnoreCase))
        {
            size = ChangelogReport.ChangelogBucketSize.Day;
        }
        else if (string.Equals(trimmed, "week", StringComparison.OrdinalIgnoreCase))
        {
            size = ChangelogReport.ChangelogBucketSize.Week;
        }
        else if (string.Equals(trimmed, "month", StringComparison.OrdinalIgnoreCase))
        {
            size = ChangelogReport.ChangelogBucketSize.Month;
        }
        else
        {
            return BadRequest($"a bucket is hour, day, week or month - not \"{bucket}\"");
        }

        if (!string.IsNullOrEmpty(level) && level != "epic" && level != "story")
            return BadRequest($"a level is epic or story - not \"{level}\"");

        var looseTrimmed = loose?.Trim();
        bool looseFlag;
        if (string.IsNullOrEmpty(looseTrimmed) || string.Equals(looseTrimmed, "false", StringComparison.OrdinalIgnoreCase))
            looseFlag = false;
        else if (string.Equals(looseTrimmed, "true", StringComparison.OrdinalIgnoreCase))
            looseFlag = true;
        else
            return BadRequest($"loose is true or false - not \"{loose}\"");

        var ids = (projectId ?? []).Distinct().ToList();
        foreach (var id in ids)
            if (!await db.Projects.AnyAsync(p => p.Id == id, ct))
                return BadRequest($"there is no project {id}");

        var first = await db.Issues.AsNoTracking()
            .OrderBy(i => i.CreatedAt)
            .Select(i => (DateTimeOffset?)i.CreatedAt)
            .FirstOrDefaultAsync(ct);
        start = RangeBounds.ClampStart(start, end, first);

        if (level == "story")
        {
            var byId = (await db.Issues.AsNoTracking()
                    .Select(i => new { i.Id, i.ParentId, i.Type })
                    .ToListAsync(ct))
                .ToDictionary(i => i.Id, i => (i.ParentId, i.Type));

            var closedStories = db.Issues.AsNoTracking()
                .Where(i => i.Type == "story" && i.Status!.IsTerminal && i.ClosedAt != null
                    && i.ClosedAt >= start && i.ClosedAt < end);

            if (ids.Count > 0) closedStories = closedStories.Where(i => ids.Contains(i.ProjectId));

            var storyRows = (await closedStories
                    .Select(i => new { i.Id, ClosedAt = i.ClosedAt!.Value })
                    .ToListAsync(ct))
                .Select(i => new ChangelogReport.StoryRow(i.Id, NearestEpicId(i.Id, byId), i.ClosedAt))
                .ToList();

            var closedEpics = db.Issues.AsNoTracking()
                .Where(i => i.Type == "epic" && i.Status!.IsTerminal && i.ClosedAt != null
                    && i.ClosedAt >= start && i.ClosedAt < end);

            if (ids.Count > 0) closedEpics = closedEpics.Where(i => ids.Contains(i.ProjectId));

            var epicCandidates = (await closedEpics
                    .Select(i => new { i.Id, i.ClosedAt })
                    .ToListAsync(ct))
                .Select(i => new ChangelogReport.EpicCandidate(i.Id, i.ClosedAt))
                .ToList();

            var storyBuckets = ChangelogReport.FoldStories(epicCandidates, storyRows, start, end, size, offsetMinutes);

            var storyEpicIds = storyBuckets.SelectMany(b => b.Groups).Select(g => g.EpicId).ToHashSet();
            var (cards, tree) = await LoadCardsAsync(storyEpicIds, now, ct);

            var storyBucketDtos = storyBuckets
                .Select(b => new ChangelogReportBucketDto(
                    b.Start,
                    b.End,
                    b.Groups
                        .Select(g => new ChangelogReportGroupDto(cards[g.EpicId], g.ClosedHere, tree!.DescendantCount(g.EpicId), Stories: []))
                        .ToList(),
                    Loose: []))
                .ToList();

            return new ChangelogReportDto(start, end, ChangelogReport.Name(size), "story", looseFlag, storyBucketDtos);
        }

        var closed = db.Issues.AsNoTracking()
            .Where(i => i.Type == "epic" && i.Status!.IsTerminal && i.ClosedAt != null);

        if (ids.Count > 0) closed = closed.Where(i => ids.Contains(i.ProjectId));

        var rows = (await closed
                .Select(i => new
                {
                    i.Id,
                    ParentType = i.Parent == null ? null : i.Parent.Type,
                    i.Type,
                    ClosedAt = i.ClosedAt!.Value,
                })
                .ToListAsync(ct))
            .Select(i => new ChangelogReport.Row(i.Id, i.ParentType, i.Type, i.ClosedAt))
            .ToList();

        var buckets = ChangelogReport.Fold(rows, start, end, size, offsetMinutes);

        IReadOnlyList<ChangelogReport.LooseBucket> looseBuckets = [];
        if (looseFlag)
        {
            var closedLoose = db.Issues.AsNoTracking()
                .Where(i => i.Status!.IsTerminal && i.ClosedAt != null
                    && (i.Type == "bug" || (i.Type == "story" && i.ParentId == null)));
            if (ids.Count > 0) closedLoose = closedLoose.Where(i => ids.Contains(i.ProjectId));

            var looseRows = (await closedLoose
                    .Select(i => new { i.Id, ParentType = i.Parent == null ? null : i.Parent.Type, i.Type, ClosedAt = i.ClosedAt!.Value })
                    .ToListAsync(ct))
                .Select(i => new ChangelogReport.Row(i.Id, i.ParentType, i.Type, i.ClosedAt))
                .ToList();
            looseBuckets = ChangelogReport.FoldLoose(looseRows, start, end, size, offsetMinutes);
        }

        var epicIds = buckets.SelectMany(b => b.EpicIds).ToHashSet();
        var (epicCards, epicTree) = await LoadCardsAsync(epicIds, now, ct);
        var looseIssueIds = looseBuckets.SelectMany(b => b.IssueIds).ToHashSet();
        var looseEntries = new Dictionary<long, ChangelogReportEntryDto>();
        if (looseIssueIds.Count > 0)
        {
            var rows2 = await db.Issues.AsNoTracking().Where(i => looseIssueIds.Contains(i.Id))
                .Select(i => new {
                    ProjectKey = i.Project!.Key, i.Id, i.Number, i.Type, i.Title, i.ReleaseNote, i.Priority,
                    ClosedAt = i.ClosedAt!.Value,
                }).ToListAsync(ct);
            foreach (var r in rows2)
                looseEntries[r.Id] = new ChangelogReportEntryDto(
                    IssueKey.Format(r.ProjectKey, r.Number), r.Title, r.Type, r.ProjectKey,
                    r.ReleaseNote, r.ClosedAt, PriorityLevels.Name(r.Priority));
        }
        var epicByStart = buckets.ToDictionary(b => b.Start);
        var looseByStart = looseBuckets.ToDictionary(b => b.Start);
        var bucketDtos = epicByStart.Keys.Concat(looseByStart.Keys).Distinct()
            .OrderByDescending(s => s)
            .Select(s =>
            {
                epicByStart.TryGetValue(s, out var eb);
                looseByStart.TryGetValue(s, out var lb);
                return new ChangelogReportBucketDto(
                    s, eb?.End ?? lb!.End,
                    (eb?.EpicIds ?? []).Select(id => new ChangelogReportGroupDto(
                        epicCards[id], ClosedHere: true, epicTree!.DescendantCount(id), Stories: [])).ToList(),
                    (lb?.IssueIds ?? []).Select(id => looseEntries[id]).ToList());
            })
            .ToList();

        return new ChangelogReportDto(start, end, ChangelogReport.Name(size), "epic", looseFlag, bucketDtos);
    }

    /// <summary>
    /// One actor's events over a range, as counts by kind, a rundown per issue
    /// touched, and the newest rows - wires <see cref="ActivityReport.Fold"/> to
    /// the database and decorates the issues with cards.
    /// </summary>
    /// <param name="actor">The actor, matched exactly - untrimmed - against the event's own; required.</param>
    /// <param name="from">The start of the range; fourteen days before <paramref name="to"/> when absent.</param>
    /// <param name="to">The end of the range; now when absent.</param>
    /// <param name="projectId">Narrows to events whose issue is in one of these projects, repeatable; every project when absent or empty.</param>
    /// <param name="offsetMinutes">Minutes east of UTC, aligning a daily bucket to the reader's own midnight - the work log's own parameter.</param>
    /// <param name="gap">
    /// The longest stretch, in minutes, between two consecutive events that
    /// still counts as one working session; 1 to 1440, 30 when absent.
    /// </param>
    /// <param name="lead">Added to every session's estimated length, in minutes; 0 to 240, 15 when absent.</param>
    /// <param name="window">How many days the rolling mean in <c>series</c> trails over; 1 to 90, 3 when absent.</param>
    /// <param name="includeKeys">
    /// When <paramref name="actor"/> names a person, also counts the events under the keys that person owns which
    /// a hand typed - see <see cref="ActivityReport.HandTyped"/> - each marked <c>viaKey</c>. False when absent;
    /// no effect when the actor is not a person.
    /// </param>
    /// <remarks>
    /// A name no live row answers to still answers, empty: the trail outlives
    /// the person or key it names. The counts, the issue rundown, the series
    /// and the sessions cover every event in range; only the rows are capped,
    /// at <see cref="ActivityController.MaxEvents"/>, and a payload is read for
    /// only the rows listed.
    /// </remarks>
    [HttpGet("activity")]
    public async Task<ActionResult<ActivityReportDto>> GetActivityReport(
        [FromQuery] string? actor, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] List<int>? projectId,
        [FromQuery] int offsetMinutes = 0, [FromQuery] int gap = 30, [FromQuery] int lead = 15,
        [FromQuery] int window = 3, [FromQuery] bool includeKeys = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor))
            return BadRequest("an actor is required");

        var now = time.GetUtcNow();

        if (RangeBounds.Resolve(from, to, now, DefaultChangelogRange, out var start, out var end) is { } refusal)
            return BadRequest(refusal);

        if (offsetMinutes is < -1440 or > 1440)
            return BadRequest($"an offset from UTC is minutes between -1440 and 1440 - not {offsetMinutes}");

        if (gap is < 1 or > 1440)
            return BadRequest($"a gap is between 1 and 1440 minutes - not {gap}");

        if (lead is < 0 or > 240)
            return BadRequest($"a lead is between 0 and 240 minutes - not {lead}");

        if (window is < 1 or > 90)
            return BadRequest($"a window is between 1 and 90 days - not {window}");

        var ids = (projectId ?? []).Distinct().ToList();
        if (ids.Count > 0)
        {
            var known = await db.Projects.AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync(ct);
            var missing = ids.Where(id => !known.Contains(id)).ToList();
            if (missing.Count > 0)
                return BadRequest($"there is no project {string.Join(", ", missing)}");
        }

        var first = await db.IssueEvents.AsNoTracking().MinAsync(e => (DateTimeOffset?)e.At, ct);
        start = RangeBounds.ClampStart(start, end, first);

        var bucketSize = WorkLogRollup.ChooseBucketSize(start, end);
        var seriesWindow = WorkLogRollup.Align(start, end, bucketSize, offsetMinutes);

        if (seriesWindow.Buckets > WorkLogController.MaxBuckets)
            return BadRequest(
                $"{seriesWindow.Buckets} buckets is more than the {WorkLogController.MaxBuckets} this answers in " +
                "- ask for a shorter range");

        var rows = await db.IssueEvents.AsNoTracking()
            .Where(e => e.Actor == actor && e.At >= start && e.At < end)
            .Select(e => new ActivityReport.Row(e.Id, e.IssueId, e.Issue!.ProjectId, e.Kind, e.At))
            .ToListAsync(ct);

        if (includeKeys) rows.AddRange(await HandTypedUnderKeysAsync(actor, start, end, ct));

        var result = ActivityReport.Fold(
            rows, ids, ActivityController.MaxEvents,
            seriesWindow, TimeSpan.FromMinutes(gap), TimeSpan.FromMinutes(lead), window);

        var listed = result.Events.Select(e => e.Id).ToList();
        var payloads = listed.Count == 0
            ? new Dictionary<long, string?>()
            : await db.IssueEvents.AsNoTracking()
                .Where(e => listed.Contains(e.Id))
                .Select(e => new { e.Id, e.Payload })
                .ToDictionaryAsync(e => e.Id, e => e.Payload, ct);

        var issueIds = result.Issues.Select(i => i.IssueId).ToHashSet();
        var (cards, _) = await LoadCardsAsync(issueIds, now, ct, withTree: false);

        var sessionIssueIds = result.Sessions.SelectMany(s => s.IssueIds).ToHashSet();
        var sessionIssueKeys = sessionIssueIds.Count == 0
            ? new Dictionary<long, string>()
            : await db.Issues.AsNoTracking()
                .Where(i => sessionIssueIds.Contains(i.Id))
                .Select(i => new { i.Id, ProjectKey = i.Project!.Key, i.Number })
                .ToDictionaryAsync(i => i.Id, i => IssueKey.Format(i.ProjectKey, i.Number), ct);

        ActivityKindCountDto[] KindsOf(IReadOnlyList<ActivityReport.KindCount> kinds) =>
            kinds.Select(k => new ActivityKindCountDto(k.Kind, k.Count)).ToArray();

        return new ActivityReportDto(
            actor, start, end, result.Total,
            KindsOf(result.Kinds),
            result.Issues
                .Where(i => cards.ContainsKey(i.IssueId))
                .Select(i => new ActivityIssueDto(cards[i.IssueId], i.First, i.Last, i.Count, KindsOf(i.Kinds)))
                .ToList(),
            result.Events
                .Where(e => cards.ContainsKey(e.IssueId))
                .Select(e => new ActivityEventDto(
                    e.Id, cards[e.IssueId].Key, actor, e.Kind,
                    IssueEventPayload.Parse(payloads.GetValueOrDefault(e.Id)), e.At, e.ViaKey))
                .ToList(),
            result.Truncated,
            WorkLogRollup.Name(bucketSize),
            offsetMinutes,
            result.Series
                .Select(p => new ActivitySeriesPointDto(p.At, p.Events, p.Hours, p.RollingEvents, p.RollingHours))
                .ToList(),
            result.Sessions
                .Select(s => new ActivitySessionDto(
                    s.From, s.To, s.Events,
                    s.IssueIds.Where(sessionIssueKeys.ContainsKey).Select(id => sessionIssueKeys[id]).ToList(),
                    s.EstimatedMinutes))
                .ToList(),
            new ActivityEstimateDto(
                result.Estimate.Sessions, result.Estimate.Minutes, gap, lead, result.Estimate.WindowDays));
    }

    /// <summary>
    /// The events a hand typed under the keys a person owns, in range - none
    /// when <paramref name="actor"/> names no live person or the person owns no key.
    /// </summary>
    private async Task<List<ActivityReport.Row>> HandTypedUnderKeysAsync(
        string actor, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        if (keys is null) return [];

        var person = (await actors.LiveAsync(ct))
            .FirstOrDefault(a => a.Kind == ActorKind.Person && string.Equals(a.Name, actor, StringComparison.Ordinal));
        if (person is null) return [];

        var names = (await keys.ListApiKeysAsync(ct))
            .Where(k => k.OwnerPersonId == person.Id)
            .Select(k => k.Name)
            .ToList();
        if (names.Count == 0) return [];

        var keyRows = (await db.IssueEvents.AsNoTracking()
                .Where(e => names.Contains(e.Actor) && e.At >= start && e.At < end)
                .Select(e => new { e.Id, e.IssueId, e.Issue!.ProjectId, e.Kind, e.At, e.Actor })
                .ToListAsync(ct))
            .Select(e => new ActivityReport.Row(e.Id, e.IssueId, e.ProjectId, e.Kind, e.At, e.Actor))
            .ToList();
        if (keyRows.Count == 0) return [];

        var issueIds = keyRows.Select(r => r.IssueId).Distinct().ToList();
        string[] claimKinds = [EfHatchIssueEvent.ClaimTaken, EfHatchIssueEvent.ClaimReleased, EfHatchIssueEvent.ClaimLapsed];

        var claimEvents = await db.IssueEvents.AsNoTracking()
            .Where(e => issueIds.Contains(e.IssueId) && claimKinds.Contains(e.Kind) && e.At < end)
            .Select(e => new ActivityReport.ClaimEvent(e.Id, e.IssueId, e.Actor, e.Kind, e.At))
            .ToListAsync(ct);

        var spans = await db.WorkLog.AsNoTracking()
            .Where(w => issueIds.Contains(w.IssueId) && w.EndedAt >= start && w.StartedAt < end)
            .Select(w => new ActivityReport.WorkSpan(w.IssueId, w.StartedAt, w.EndedAt))
            .ToListAsync(ct);

        return ActivityReport.HandTyped(keyRows, claimEvents, spans);
    }

    /// <summary>
    /// What each issue matching the filter - and everything beneath it -
    /// consumed in a window: tokens, notional USD and runner time, wired to
    /// <see cref="ConsumptionReport.Fold"/>. No cards: a script reads this,
    /// not a page.
    /// </summary>
    /// <param name="from">The start of the range; ninety days before <paramref name="to"/> when absent.</param>
    /// <param name="to">The end of the range; now when absent.</param>
    /// <param name="type">One of the five issue types, or <c>all</c>; <c>epic</c> when absent.</param>
    /// <param name="sort"><c>activity</c> (default, <c>lastSessionAt</c> descending), <c>tokens</c>, <c>cost</c> (unpriced rows last) or <c>time</c>.</param>
    /// <param name="limit">How many rows to list, 1 to <see cref="MaxConsumptionRows"/>.</param>
    /// <param name="offsetMinutes">Minutes east of UTC, aligning the history's daily buckets to the reader's own midnight.</param>
    /// <param name="statusId">Narrows to issues sitting in any of these columns, repeatable; every column when absent or empty. An id naming no column is a <c>400</c>.</param>
    /// <param name="projectId">Narrows to these projects' issues, repeatable; every project when absent or empty. An id naming no project is a <c>400</c>.</param>
    /// <remarks>
    /// Refused in this order: the bound, the offset, the type, the sort, the
    /// limit, the status, the project - so a request wrong in several places
    /// always comes back with the same sentence.
    /// </remarks>
    [HttpGet("consumption")]
    public async Task<ActionResult<ConsumptionReportDto>> GetConsumption(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? type, [FromQuery] string? sort,
        [FromQuery] int limit = DefaultConsumptionRows, [FromQuery] int offsetMinutes = 0,
        [FromQuery] List<int>? statusId = null, [FromQuery] List<int>? projectId = null, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();

        var (bounds, boundsRefusal) = ParseConsumptionBounds(from, to, type, sort, offsetMinutes, now);
        if (boundsRefusal is not null) return BadRequest(boundsRefusal);
        var (start, end, resolvedType, resolvedSort) = bounds!.Value;

        if (limit is < 1 or > MaxConsumptionRows)
            return BadRequest($"a limit is between 1 and {MaxConsumptionRows} - not {limit}");

        var (scope, scopeRefusal) = await ResolveConsumptionScopeAsync(statusId, projectId, ct);
        if (scopeRefusal is not null) return BadRequest(scopeRefusal);
        var (statusIds, projectIds) = scope!.Value;

        var inputs = await LoadConsumptionInputsAsync(resolvedType, statusIds, projectIds, start, end, offsetMinutes, ct);

        var result = ConsumptionReport.Fold(inputs.Sessions, inputs.Candidates, inputs.ParentOf, inputs.Window, resolvedSort);

        // Ignores the window and respects the rest of the filter, the same two
        // extra bound reads WorkLogRollup.FirstEndedAsync already makes -
        // scoped to every candidate and everything beneath it, so "nothing has
        // ever run here" reads apart from "nothing ran in the range asked for".
        var scopeIdSet = new HashSet<long>();
        var children = ChildrenOf(inputs.ParentOf);
        foreach (var c in inputs.Candidates)
        {
            scopeIdSet.Add(c.Id);
            scopeIdSet.UnionWith(Descend(children, c.Id));
        }

        var scopeIds = scopeIdSet.ToList();
        var boundsQuery = db.WorkLog.AsNoTracking().Where(w => scopeIds.Contains(w.IssueId));
        if (projectIds.Count > 0) boundsQuery = boundsQuery.Where(w => projectIds.Contains(w.Issue!.ProjectId));

        var firstSessionAt = await boundsQuery.OrderBy(w => w.EndedAt)
            .Select(w => (DateTimeOffset?)w.EndedAt).FirstOrDefaultAsync(ct);
        var lastSessionAt = await boundsQuery.OrderByDescending(w => w.EndedAt)
            .Select(w => (DateTimeOffset?)w.EndedAt).FirstOrDefaultAsync(ct);

        var rows = result.Rows
            .Take(limit)
            .Select(r => new ConsumptionRowDto(
                r.Key, r.Title, r.Type, r.ProjectKey, r.StatusId, r.ParentKey, r.Totals, r.FirstSessionAt, r.LastSessionAt))
            .ToList();

        var bucketSize = WorkLogRollup.ChooseBucketSize(start, end);

        return new ConsumptionReportDto(
            start,
            end,
            resolvedType,
            ConsumptionReport.Name(resolvedSort),
            limit,
            result.Rows.Count,
            result.Totals,
            firstSessionAt,
            lastSessionAt,
            rows,
            new WorkLogHistoryDto(
                inputs.Window.From,
                inputs.Window.From + inputs.Window.Length * inputs.Window.Buckets,
                WorkLogRollup.Name(bucketSize),
                result.Totals,
                firstSessionAt,
                lastSessionAt,
                result.Buckets.Select(b => new WorkLogBucketDto(b.Start, b.End, b.Totals)).ToList()));
    }

    /// <summary>
    /// <see cref="GetConsumption"/>'s whole filter, uncapped, as a file a
    /// spreadsheet opens - sharing its parsing and its fold rather than a
    /// second one, and reading no <c>limit</c> at all, which is how "one
    /// given is ignored" holds with no code written to ignore it.
    /// </summary>
    /// <param name="from">The start of the range; ninety days before <paramref name="to"/> when absent.</param>
    /// <param name="to">The end of the range; now when absent.</param>
    /// <param name="type">One of the five issue types, or <c>all</c>; <c>epic</c> when absent.</param>
    /// <param name="sort"><c>activity</c> (default, <c>lastSessionAt</c> descending), <c>tokens</c>, <c>cost</c> (unpriced rows last) or <c>time</c>.</param>
    /// <param name="offsetMinutes">Minutes east of UTC, the same bound as the JSON route's - this route folds no history, so it affects nothing here but is still refused out of range.</param>
    /// <param name="statusId">Narrows to issues sitting in any of these columns, repeatable; every column when absent or empty. An id naming no column is a <c>400</c>.</param>
    /// <param name="projectId">Narrows to these projects' issues, repeatable; every project when absent or empty. An id naming no project is a <c>400</c>.</param>
    [HttpGet("consumption.csv")]
    public async Task<IActionResult> GetConsumptionCsv(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? type, [FromQuery] string? sort,
        [FromQuery] int offsetMinutes = 0,
        [FromQuery] List<int>? statusId = null, [FromQuery] List<int>? projectId = null, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();

        var (bounds, boundsRefusal) = ParseConsumptionBounds(from, to, type, sort, offsetMinutes, now);
        if (boundsRefusal is not null) return BadRequest(boundsRefusal);
        var (start, end, resolvedType, resolvedSort) = bounds!.Value;

        var (scope, scopeRefusal) = await ResolveConsumptionScopeAsync(statusId, projectId, ct);
        if (scopeRefusal is not null) return BadRequest(scopeRefusal);
        var (statusIds, projectIds) = scope!.Value;

        var inputs = await LoadConsumptionInputsAsync(resolvedType, statusIds, projectIds, start, end, offsetMinutes, ct);

        var result = ConsumptionReport.Fold(inputs.Sessions, inputs.Candidates, inputs.ParentOf, inputs.Window, resolvedSort);

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ConsumptionCsv.Write(result.Rows))).ToArray();
        var fileName = $"consumption-{start.ToUniversalTime():yyyy-MM-dd}-{end.ToUniversalTime():yyyy-MM-dd}.csv";

        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    /// <summary>The bound, the offset, the type and the sort - the first of the consumption filter's refusals, shared by <see cref="GetConsumption"/> and <see cref="GetConsumptionCsv"/>.</summary>
    private (ConsumptionBounds? Bounds, string? Refusal) ParseConsumptionBounds(
        string? from, string? to, string? type, string? sort, int offsetMinutes, DateTimeOffset now)
    {
        if (RangeBounds.Resolve(from, to, now, DefaultRange, out var start, out var end) is { } refusal)
            return (null, refusal);

        if (offsetMinutes is < -1440 or > 1440)
            return (null, $"an offset from UTC is minutes between -1440 and 1440 - not {offsetMinutes}");

        var trimmedType = type?.Trim();
        string resolvedType;
        if (string.IsNullOrEmpty(trimmedType))
            resolvedType = "epic";
        else if (string.Equals(trimmedType, "all", StringComparison.OrdinalIgnoreCase))
            resolvedType = "all";
        else if (EfHatchIssue.IsValidType(trimmedType))
            resolvedType = trimmedType;
        else
            return (null, $"a type is {string.Join(", ", EfHatchIssue.Types)} or all - not \"{type}\"");

        var trimmedSort = sort?.Trim();
        ConsumptionReport.ConsumptionSort resolvedSort;
        if (string.IsNullOrEmpty(trimmedSort) || string.Equals(trimmedSort, "activity", StringComparison.OrdinalIgnoreCase))
            resolvedSort = ConsumptionReport.ConsumptionSort.Activity;
        else if (string.Equals(trimmedSort, "tokens", StringComparison.OrdinalIgnoreCase))
            resolvedSort = ConsumptionReport.ConsumptionSort.Tokens;
        else if (string.Equals(trimmedSort, "cost", StringComparison.OrdinalIgnoreCase))
            resolvedSort = ConsumptionReport.ConsumptionSort.Cost;
        else if (string.Equals(trimmedSort, "time", StringComparison.OrdinalIgnoreCase))
            resolvedSort = ConsumptionReport.ConsumptionSort.Time;
        else
            return (null, $"a sort is activity, tokens, cost or time - not \"{sort}\"");

        return (new ConsumptionBounds(start, end, resolvedType, resolvedSort), null);
    }

    /// <summary>The status and the project existence checks - the consumption filter's second pair of refusals, shared the same way.</summary>
    private async Task<(ConsumptionScope? Scope, string? Refusal)> ResolveConsumptionScopeAsync(
        List<int>? statusId, List<int>? projectId, CancellationToken ct)
    {
        var statusIds = (statusId ?? []).Distinct().ToList();
        foreach (var id in statusIds)
            if (!await db.Statuses.AnyAsync(s => s.Id == id, ct))
                return (null, $"there is no column {id}");

        var projectIds = (projectId ?? []).Distinct().ToList();
        foreach (var id in projectIds)
            if (!await db.Projects.AnyAsync(p => p.Id == id, ct))
                return (null, $"there is no project {id}");

        return (new ConsumptionScope(statusIds, projectIds), null);
    }

    /// <summary>The candidates, every issue's parent, the sessions in the window and the window itself - what <see cref="ConsumptionReport.Fold"/> needs, loaded once for both routes.</summary>
    private async Task<ConsumptionInputs> LoadConsumptionInputsAsync(
        string resolvedType, List<int> statusIds, List<int> projectIds,
        DateTimeOffset start, DateTimeOffset end, int offsetMinutes, CancellationToken ct)
    {
        var candidateQuery = db.Issues.AsNoTracking().AsQueryable();
        if (projectIds.Count > 0) candidateQuery = candidateQuery.Where(i => projectIds.Contains(i.ProjectId));
        if (resolvedType != "all") candidateQuery = candidateQuery.Where(i => i.Type == resolvedType);
        if (statusIds.Count > 0) candidateQuery = candidateQuery.Where(i => statusIds.Contains(i.StatusId));

        var candidates = (await candidateQuery
                .Select(i => new { i.Id, i.ParentId, ProjectKey = i.Project!.Key, i.Number, i.Title, i.Type, i.StatusId })
                .ToListAsync(ct))
            .Select(i => new ConsumptionReport.Candidate(
                i.Id, i.ParentId, IssueKey.Format(i.ProjectKey, i.Number), i.Title, i.Type, i.ProjectKey, i.StatusId))
            .ToList();

        // Every issue's parent, not only candidates' - a session or an
        // intervening ancestor need not be a candidate for one further up to
        // earn the credit.
        var parentOf = await db.Issues.AsNoTracking()
            .Select(i => new { i.Id, i.ParentId })
            .ToDictionaryAsync(i => i.Id, i => i.ParentId, ct);

        var sessionsQuery = db.WorkLog.AsNoTracking().Where(w => w.EndedAt >= start && w.EndedAt < end);
        if (projectIds.Count > 0) sessionsQuery = sessionsQuery.Where(w => projectIds.Contains(w.Issue!.ProjectId));

        var sessions = (await sessionsQuery
                .Select(w => new
                {
                    w.IssueId, w.EndedAt, w.IsError, w.InputTokens, w.OutputTokens,
                    w.CacheCreationTokens, w.CacheReadTokens, w.CostUsd, w.DurationMs,
                })
                .ToListAsync(ct))
            .Select(w => new ConsumptionReport.Session(
                w.IssueId, w.EndedAt, w.IsError, w.InputTokens, w.OutputTokens,
                w.CacheCreationTokens, w.CacheReadTokens, w.CostUsd, w.DurationMs))
            .ToList();

        var bucketSize = WorkLogRollup.ChooseBucketSize(start, end);
        var window = WorkLogRollup.Align(start, end, bucketSize, offsetMinutes);

        return new ConsumptionInputs(candidates, parentOf, sessions, window);
    }

    /// <summary>The bound, the offset, the type and the sort, resolved - <see cref="ParseConsumptionBounds"/>'s own success shape.</summary>
    private readonly record struct ConsumptionBounds(
        DateTimeOffset Start, DateTimeOffset End, string Type, ConsumptionReport.ConsumptionSort Sort);

    /// <summary>The status and project ids, resolved - <see cref="ResolveConsumptionScopeAsync"/>'s own success shape.</summary>
    private readonly record struct ConsumptionScope(List<int> StatusIds, List<int> ProjectIds);

    /// <summary>What <see cref="ConsumptionReport.Fold"/> needs - <see cref="LoadConsumptionInputsAsync"/>'s own shape.</summary>
    private readonly record struct ConsumptionInputs(
        List<ConsumptionReport.Candidate> Candidates,
        Dictionary<long, long?> ParentOf,
        List<ConsumptionReport.Session> Sessions,
        WorkLogWindow Window);

    /// <summary>
    /// The nearest epic above <paramref name="issueId"/>, walking <c>ParentId</c>
    /// through <paramref name="byId"/> - dictionary lookups only, never a query
    /// per step, unlike <see cref="IssuesController.ResolveParentAsync"/>'s own
    /// ancestor walk. A <c>seen</c> set guards a cycle the same way that walk does.
    /// </summary>
    private static long? NearestEpicId(long issueId, Dictionary<long, (long? ParentId, string Type)> byId)
    {
        var seen = new HashSet<long>();
        var ancestor = byId.TryGetValue(issueId, out var self) ? self.ParentId : null;
        while (ancestor is { } id && seen.Add(id))
        {
            if (!byId.TryGetValue(id, out var node)) return null;
            if (node.Type == "epic") return id;
            ancestor = node.ParentId;
        }

        return null;
    }

    /// <summary>
    /// The card for each of <paramref name="ids"/>, and the rollup tree
    /// <see cref="Rollup.Tree.DescendantCount"/> reads them against - the
    /// decoration <see cref="GetTimeline"/> and <see cref="GetChangelogReport"/>
    /// both want. Empty/<c>null</c> with no ids, so a caller never queries for
    /// nothing. <c>withTree: false</c> skips the rollup load for a caller that
    /// reads no count off it; the tree is then <c>null</c>.
    /// </summary>
    private async Task<(Dictionary<long, IssueCardDto> Cards, Rollup.Tree? Tree)> LoadCardsAsync(
        HashSet<long> ids, DateTimeOffset now, CancellationToken ct, bool withTree = true)
    {
        var cards = new Dictionary<long, IssueCardDto>();
        if (ids.Count == 0) return (cards, null);

        var priorities = await PriorityTree.ForAsync(db, ct);
        var tree = withTree ? await Rollup.LoadAsync(db, ct) : null;

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
                i.NoCode,
                i.CreatedBy,
                Claim = new ClaimSnapshot(
                    i.ClaimToken, i.ClaimedBy, i.ClaimRunner,
                    i.ClaimedAt, i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt,
                    i.ClaimQuietLimitSeconds, i.ClaimMaxRunSeconds),
            })
            .ToListAsync(ct);

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
                ClosedAt: i.ClosedAt,
                NoCode: NoCode.OnCard(i.NoCode, i.Id, priorities),
                CreatedBy: i.CreatedBy);
        }

        return (cards, tree);
    }

    /// <summary>The card the loose work is drawn under: no issue, so nothing on it is real.</summary>
    private static readonly IssueCardDto OtherCard = new("other", "", "epic", "Other", 0, 0, null, null, null);

    /// <summary>Shared, because a childless issue is the common case and each one would otherwise allocate a list to say so.</summary>
    private static readonly List<long> NoChildren = [];

    /// <summary><paramref name="parentOf"/>, inverted - every issue's direct children, for <see cref="GetConsumption"/>'s own descent.</summary>
    private static Dictionary<long, List<long>> ChildrenOf(Dictionary<long, long?> parentOf)
    {
        var children = new Dictionary<long, List<long>>();

        foreach (var (id, parentId) in parentOf)
        {
            if (parentId is not { } parent) continue;
            if (!children.TryGetValue(parent, out var siblings)) children[parent] = siblings = [];
            siblings.Add(id);
        }

        return children;
    }

    /// <summary>
    /// Every issue below <paramref name="rootId"/>, at any depth, and not the
    /// issue itself - <see cref="Rollup"/>'s own descent, copied rather than
    /// reached into since that walk is private there. The same <c>seen</c>-set
    /// guard against a cycle that got in some other way than parenting ever
    /// refuses to make.
    /// </summary>
    private static List<long> Descend(Dictionary<long, List<long>> children, long rootId)
    {
        var seen = new HashSet<long> { rootId };
        var found = new List<long>();
        var generation = new List<long> { rootId };

        while (generation.Count > 0)
        {
            var next = new List<long>();

            foreach (var id in generation)
            foreach (var child in children.TryGetValue(id, out var kids) ? kids : NoChildren)
                if (seen.Add(child))
                    next.Add(child);

            found.AddRange(next);
            generation = next;
        }

        return found;
    }

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

using System.Text.Json;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What changed under the reader's ambitions since an instant, as
/// <c>GET /api/ambitions/changes</c> answers it: four kinds of line, all read
/// from rows already kept, none of them stored.
/// </summary>
/// <remarks>
/// <para>The read never walks the whole trail. Every kind starts from an index
/// that cuts at <c>since</c> - <c>IX_IssueEvents_Kind_At</c>,
/// <c>IX_Comments_Questions</c>, <c>IX_Comments_Answers</c> - and the verdicts
/// are read as the window's own rows plus the one newest verdict before it for
/// each capability, so what comes back does not grow with the age of the
/// board.</para>
///
/// <para>Every kind narrows by its ambition, so <c>projectId</c> means the
/// ambition's project. Scope and sight come from <see cref="HatchContext"/>'s
/// query filters.</para>
/// </remarks>
public static class WhatChanged
{
    /// <summary>The most lines one read answers. The rest are counted in <c>Omitted</c>.</summary>
    public const int MaxLines = 30;

    public const string Holds = "holds";
    public const string Carved = "carved";
    public const string Decided = "decided";
    public const string Asked = "asked";

    /// <summary>The order kinds sort in when two lines share an instant.</summary>
    private static readonly string[] KindOrder = [Holds, Carved, Decided, Asked];

    private const string Ambition = "ambition";

    private const string Epic = "epic";

    public static async Task<AmbitionChangesDto> ReadAsync(
        HatchContext db, DateTimeOffset since, string basis, IReadOnlyList<int> projectIds, CancellationToken ct)
    {
        var scope = (await AmbitionsController.InProject(db.Issues.AsNoTracking().Where(i => i.Type == Ambition), projectIds.ToList())
            .Select(i => new { i.Id, ProjectKey = i.Project!.Key, i.Number, i.Title })
            .ToListAsync(ct))
            .ToDictionary(a => a.Id, a => new Scoped(a.Id, IssueKey.Format(a.ProjectKey, a.Number), a.Title));

        if (scope.Count == 0) return new AmbitionChangesDto(since, basis, 0, []);

        var ambitionIds = scope.Keys.ToList();
        var byKey = scope.Values.ToDictionary(a => a.Key);

        var lines = new List<AmbitionChangeLineDto>();
        lines.AddRange(await HoldsAsync(db, since, scope, ambitionIds, ct));
        lines.AddRange(await CarvedAsync(db, since, scope, ambitionIds, ct));
        lines.AddRange(await DecidedAsync(db, since, byKey, ct));
        lines.AddRange(await AskedAsync(db, since, byKey, ct));

        var ordered = lines
            .OrderByDescending(l => l.At)
            .ThenBy(l => Array.IndexOf(KindOrder, l.Kind))
            .ThenBy(l => l.IssueKey, StringComparer.Ordinal)
            .ThenBy(l => l.Statement, StringComparer.Ordinal)
            .ThenBy(l => l.WentAheadOn, StringComparer.Ordinal)
            .ThenBy(l => l.Question, StringComparer.Ordinal)
            .ToList();

        return new AmbitionChangesDto(since, basis, Math.Max(0, ordered.Count - MaxLines), ordered.Take(MaxLines).ToList());
    }

    private sealed record Scoped(long Id, string Key, string Title);

    // ---- holds ----

    /// <summary>
    /// A capability whose newest verdict inside the window holds, where the
    /// newest verdict before the window did not (or there was none). A
    /// capability that held and stopped holding inside the window draws
    /// nothing, and neither does one that held before and held again: the line
    /// says what is newly true.
    /// </summary>
    private static async Task<List<AmbitionChangeLineDto>> HoldsAsync(
        HatchContext db, DateTimeOffset since, Dictionary<long, Scoped> scope, List<long> ambitionIds, CancellationToken ct)
    {
        var events = await db.IssueEvents.AsNoTracking()
            .Where(e => e.Kind == EfHatchIssueEvent.CapabilityJudged && e.At >= since && ambitionIds.Contains(e.IssueId))
            .Select(e => e.Payload)
            .ToListAsync(ct);

        // Parsed here rather than filtered in SQL, for Ballot.MarkedMerged's
        // reason: a JSON containment filter would have to branch on the provider.
        var named = events.Select(JudgedHolding).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        if (named.Count == 0) return [];

        var capabilities = await db.Capabilities.AsNoTracking()
            .Where(c => named.Contains(c.Id) && ambitionIds.Contains(c.IssueId))
            .Select(c => new { c.Id, c.IssueId, c.Statement })
            .ToListAsync(ct);
        if (capabilities.Count == 0) return [];

        var ids = capabilities.Select(c => c.Id).ToList();

        var inWindow = await db.CapabilityVerdicts.AsNoTracking()
            .Where(v => ids.Contains(v.CapabilityId) && v.CreatedAt >= since)
            .ToListAsync(ct);

        // The one newest verdict per capability before the window - an
        // anti-join, as IssueCapabilitiesController.NewestVerdictsAsync reads
        // the newest overall, so the rows returned do not grow with history.
        var before = (await db.CapabilityVerdicts.AsNoTracking()
            .Where(v => ids.Contains(v.CapabilityId) && v.CreatedAt < since)
            .Where(v => !db.CapabilityVerdicts.Any(w => w.CapabilityId == v.CapabilityId && w.CreatedAt < since && w.Id > v.Id))
            .ToListAsync(ct))
            .ToDictionary(v => v.CapabilityId);

        var newestInWindow = inWindow
            .GroupBy(v => v.CapabilityId)
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.Id).Last());

        var lines = new List<AmbitionChangeLineDto>();
        foreach (var c in capabilities)
        {
            if (!newestInWindow.TryGetValue(c.Id, out var now) || now.Verdict != EfHatchCapabilityVerdict.Holds) continue;
            if (before.TryGetValue(c.Id, out var was) && was.Verdict == EfHatchCapabilityVerdict.Holds) continue;

            var ambition = scope[c.IssueId];
            lines.Add(new AmbitionChangeLineDto(
                Holds, now.CreatedAt, ambition.Key, ambition.Title, ambition.Key, ambition.Title,
                c.Statement, now.LookedAt, null, null, null, null));
        }

        return lines;
    }

    /// <summary>The capability id a <c>capability_judged</c> payload names when its verdict is <c>holds</c>; null otherwise, and for a payload that will not parse.</summary>
    private static long? JudgedHolding(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("verdict", out var verdict) && verdict.ValueKind == JsonValueKind.String
                && verdict.GetString() == EfHatchCapabilityVerdict.Holds
                && root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var value)
                    ? value
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---- carved ----

    /// <summary>
    /// An epic filed under an ambition inside the window. Read from the
    /// <c>created</c> event, on an epic whose parent is one of the ambitions
    /// now: an epic moved under an ambition later is not a line.
    /// </summary>
    private static async Task<List<AmbitionChangeLineDto>> CarvedAsync(
        HatchContext db, DateTimeOffset since, Dictionary<long, Scoped> scope, List<long> ambitionIds, CancellationToken ct)
    {
        var epics = await db.IssueEvents.AsNoTracking()
            .Where(e => e.Kind == EfHatchIssueEvent.Created && e.At >= since)
            .Where(e => e.Issue!.Type == Epic && e.Issue.ParentId != null && ambitionIds.Contains(e.Issue.ParentId.Value))
            .Select(e => new
            {
                e.At,
                e.IssueId,
                ParentId = e.Issue!.ParentId!.Value,
                ProjectKey = e.Issue.Project!.Key,
                e.Issue.Number,
                e.Issue.Title,
            })
            .ToListAsync(ct);
        if (epics.Count == 0) return [];

        var epicIds = epics.Select(e => e.IssueId).Distinct().ToList();

        // Only a capability of the epic's own ambition is served: a link
        // written across ambitions is not a position this epic has.
        var links = await db.CapabilityLinks.AsNoTracking()
            .Where(l => epicIds.Contains(l.IssueId))
            .Select(l => new { l.IssueId, AmbitionId = l.Capability!.IssueId, l.Capability.Position })
            .ToListAsync(ct);

        return epics
            .Select(e =>
            {
                var ambition = scope[e.ParentId];
                var serves = links.Where(l => l.IssueId == e.IssueId && l.AmbitionId == e.ParentId).Select(l => l.Position).Order().ToList();

                return new AmbitionChangeLineDto(
                    Carved, e.At, ambition.Key, ambition.Title, IssueKey.Format(e.ProjectKey, e.Number), e.Title,
                    null, null, serves, null, null, null);
            })
            .ToList();
    }

    // ---- asked ----

    /// <summary>
    /// Hold-level questions asked inside the window and still open, beneath an
    /// ambition. Several on one issue fold into one line.
    /// </summary>
    private static async Task<List<AmbitionChangeLineDto>> AskedAsync(
        HatchContext db, DateTimeOffset since, Dictionary<string, Scoped> byKey, CancellationToken ct)
    {
        // A null level, or one nobody recognises, reads as hold: QuestionLevels.Normalise.
        var ids = await Questions.Holding(db)
            .Where(c => c.CreatedAt >= since)
            .Where(c => c.Level == null || (c.Level != QuestionLevels.BuildName && c.Level != QuestionLevels.MergeName))
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (ids.Count == 0) return [];

        var questions = await Questions.ProjectAsync(db, Questions.Holding(db).Where(c => ids.Contains(c.Id)), ct);

        return questions
            .Where(q => q.AmbitionKey is not null && byKey.ContainsKey(q.AmbitionKey))
            .GroupBy(q => q.IssueKey)
            .Select(g =>
            {
                // Oldest first, as ProjectAsync reads them, so the last is the newest.
                var newest = g.Last();
                var ambition = byKey[newest.AmbitionKey!];

                return new AmbitionChangeLineDto(
                    Asked, newest.AskedAt, ambition.Key, ambition.Title, g.Key, newest.IssueTitle,
                    null, null, null, null, Questions.FirstLine(newest.Body), g.Count());
            })
            .ToList();
    }

    // ---- decided ----

    /// <summary>
    /// Look-backs beneath an ambition that a key answered inside the window, or
    /// that nobody answered and whose issue closed inside it: the work went
    /// ahead without a person. A person's later answer drops the line, as it
    /// drops it from the Ballot.
    /// </summary>
    private static async Task<List<AmbitionChangeLineDto>> DecidedAsync(
        HatchContext db, DateTimeOffset since, Dictionary<string, Scoped> byKey, CancellationToken ct)
    {
        var answered = await db.Comments.AsNoTracking()
            .Where(a => a.AnswersId != null && a.CreatedAt >= since && a.AuthorKind == ActorKind.Key)
            .Select(a => a.AnswersId!.Value)
            .Distinct()
            .ToListAsync(ct);

        // An unanswered merge question has no answer to date it by: the issue's
        // closing does.
        var closed = (await Questions.Open(db)
            .Where(c => c.Level == QuestionLevels.MergeName && c.Issue!.ClosedAt >= since)
            .Select(c => new { c.Id, ClosedAt = c.Issue!.ClosedAt!.Value })
            .ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.ClosedAt);

        var ids = answered.Concat(closed.Keys).Distinct().ToList();
        if (ids.Count == 0) return [];

        var questions = await Questions.ProjectAsync(db, Questions.Waiting(db).Where(c => ids.Contains(c.Id)), ct);

        return questions
            .Where(q => q.LookBack && q.AmbitionKey is not null && byKey.ContainsKey(q.AmbitionKey))
            .Select(q =>
            {
                var ambition = byKey[q.AmbitionKey!];
                var at = q.Answers.Count > 0 ? q.Answers.Max(a => a.CreatedAt) : closed[q.Id];

                return new AmbitionChangeLineDto(
                    Decided, at, ambition.Key, ambition.Title, q.IssueKey, q.IssueTitle,
                    null, null, null, Questions.WentAheadOn(q), null, null);
            })
            .ToList();
    }
}

/// <summary>The words <see cref="AmbitionChangesDto.Basis"/> takes.</summary>
public static class AmbitionChangeBasis
{
    /// <summary>The caller sent a <c>since</c>.</summary>
    public const string Given = "given";

    /// <summary>A person sent none, and the instant is the visit stored for them.</summary>
    public const string Visit = "visit";

    /// <summary>Nothing to go on: a first visit, or a key with no <c>since</c>.</summary>
    public const string Week = "week";

    public const int WeekDays = 7;
}

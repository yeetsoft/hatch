using System.Text.Json;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The questions waiting on a person, read as a tree: each under the chain of
/// issues that encloses it, so an operator answering a batch can see which
/// epic and story the decision belongs to without walking parents one request
/// at a time.
///
/// What is waiting is <see cref="Questions.Waiting"/>'s answer, read and never
/// restated here. The tree is only the issues on the way up from a question,
/// so a read scoped to a handful of questions loads a handful of issues, not
/// the tracker the way <see cref="Rollup"/> does.
/// </summary>
public static class Ballot
{
    /// <summary>The longest an excerpt runs before it is cut, full stop not counted.</summary>
    private const int ExcerptLength = 300;

    /// <summary>The most non-blank lines of a description an excerpt takes.</summary>
    private const int ExcerptLines = 3;

    private sealed record Row(
        long Id, long? ParentId, string ProjectKey, int Number, string Type, string Title, string Description, bool IsTerminal);

    /// <param name="projectIds">Narrows to the questions asked in these projects; empty is every project.</param>
    public static async Task<BallotDto> BuildAsync(HatchContext db, List<int> projectIds, CancellationToken ct)
    {
        var source = Questions.Waiting(db);
        if (projectIds.Count > 0) source = source.Where(c => projectIds.Contains(c.Issue!.ProjectId));

        var questions = await Questions.ProjectAsync(db, source, ct);
        if (questions.Count == 0) return new BallotDto([], []);

        var leafIds = await source.Select(c => c.IssueId).Distinct().ToListAsync(ct);

        // A level at a time, upward: the ids not yet loaded are the parents the
        // last level named. There are no cycles, so this ends at the roots.
        var rows = new Dictionary<long, Row>();
        var level = leafIds;
        while (level.Count > 0)
        {
            var loaded = await db.Issues.AsNoTracking()
                .Where(i => level.Contains(i.Id))
                .Select(i => new Row(
                    i.Id, i.ParentId, i.Project!.Key, i.Number, i.Type, i.Title, i.Description, i.Status!.IsTerminal))
                .ToListAsync(ct);

            foreach (var row in loaded) rows[row.Id] = row;

            level = loaded
                .Where(r => r.ParentId is not null && !rows.ContainsKey(r.ParentId.Value))
                .Select(r => r.ParentId!.Value)
                .Distinct()
                .ToList();
        }

        var idByKey = rows.Values.ToDictionary(r => IssueKey.Format(r.ProjectKey, r.Number), r => r.Id);
        var questionsByIssue = questions
            .GroupBy(q => idByKey[q.IssueKey])
            .ToDictionary(g => g.Key, g => g.ToList());
        var childrenByParent = rows.Values
            .Where(r => r.ParentId is not null)
            .GroupBy(r => r.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToList());

        // Ordering only, never on the wire: the question that has waited
        // longest anywhere beneath a node decides where the node sits.
        var oldest = new Dictionary<long, (DateTimeOffset At, long Id)>();
        (DateTimeOffset At, long Id) OldestBeneath(long id)
        {
            if (oldest.TryGetValue(id, out var known)) return known;

            var best = (At: DateTimeOffset.MaxValue, Id: long.MaxValue);
            if (questionsByIssue.TryGetValue(id, out var own))
                best = (own[0].AskedAt, own[0].Id);

            foreach (var child in childrenByParent.GetValueOrDefault(id, []))
            {
                var candidate = OldestBeneath(child);
                if (candidate.CompareTo(best) < 0) best = candidate;
            }

            return oldest[id] = best;
        }

        BallotNodeDto Node(Row row) => new(
            IssueKey.Format(row.ProjectKey, row.Number),
            row.ProjectKey,
            row.Type,
            row.Title,
            Excerpt(row.Description),
            row.IsTerminal,
            Ordered(childrenByParent.GetValueOrDefault(row.Id, [])).Select(id => Node(rows[id])).ToList(),
            questionsByIssue.GetValueOrDefault(row.Id, []));

        IEnumerable<long> Ordered(IEnumerable<long> ids) => ids.OrderBy(OldestBeneath);

        var roots = Ordered(rows.Values.Where(r => r.ParentId is null).Select(r => r.Id));
        return new BallotDto(
            roots.Select(id => Node(rows[id])).ToList(),
            await OrderedAsync(db, questions, idByKey, ct));
    }

    /// <summary>
    /// The questions as one list, in the order to answer them: the one holding
    /// the most work first, then the strictest level, then the oldest asked.
    /// Look-backs follow, oldest first - the work already went ahead on them.
    /// </summary>
    private static async Task<List<BallotQuestionDto>> OrderedAsync(
        HatchContext db, List<QuestionDto> questions, Dictionary<string, long> idByKey, CancellationToken ct)
    {
        var closed = await db.Statuses.AsNoTracking()
            .Where(s => s.IsTerminal || s.IsDeferred)
            .Select(s => s.Id)
            .ToListAsync(ct);

        // One fold of the tracker, not a reload per question.
        var tree = await Rollup.LoadAsync(db, ct);
        var below = questions
            .Select(q => idByKey[q.IssueKey])
            .Distinct()
            .ToDictionary(id => id, id => tree.DescendantIds(id));

        var wanted = below.Keys.Concat(below.Values.SelectMany(ids => ids)).Distinct().ToList();
        var statusOf = await db.Issues.AsNoTracking()
            .Where(i => wanted.Contains(i.Id))
            .Select(i => new { i.Id, i.StatusId })
            .ToDictionaryAsync(i => i.Id, i => i.StatusId, ct);

        bool Open(long id) => statusOf.TryGetValue(id, out var status) && !closed.Contains(status);
        var holds = below.ToDictionary(kv => kv.Key, kv => (Open(kv.Key) ? 1 : 0) + kv.Value.Count(Open));

        var lookBackIds = questions.Where(q => q.LookBack).Select(q => idByKey[q.IssueKey]).Distinct().ToList();
        var standing = await LookBackStandingAsync(db, lookBackIds, ct);

        BallotQuestionDto Entry(QuestionDto q)
        {
            var id = idByKey[q.IssueKey];
            if (!q.LookBack) return new BallotQuestionDto(q, holds[id]);

            var (status, closedAt, merged, landings) = standing[id];
            var programAnswer = q.Answers.FirstOrDefault(a => a.AuthorKind == ActorKind.Key);
            var wentAheadOn = programAnswer is not null
                ? Questions.FirstLine(programAnswer.Body)
                : q.Options?.FirstOrDefault(o => o.Recommended)?.Label;

            return new BallotQuestionDto(
                q,
                holds[id],
                wentAheadOn,
                status,
                closedAt,
                merged,
                landings);
        }

        return questions
            .OrderBy(q => q.LookBack)
            .ThenBy(q => q.LookBack ? 0 : -holds[idByKey[q.IssueKey]])
            .ThenBy(q => q.LookBack ? 0 : QuestionLevels.Rank(q.Level))
            .ThenBy(q => q.AskedAt)
            .ThenBy(q => q.Id)
            .Select(Entry)
            .ToList();
    }

    /// <summary>
    /// What has happened to each issue a look-back is on: its column and
    /// closing time, whether the merged route carried it, and its landings.
    /// Three queries however many look-backs there are.
    /// </summary>
    private static async Task<Dictionary<long, (string Status, DateTimeOffset? ClosedAt, bool Merged, IReadOnlyList<BallotLandingDto> Landings)>>
        LookBackStandingAsync(HatchContext db, List<long> issueIds, CancellationToken ct)
    {
        if (issueIds.Count == 0) return [];

        var issues = await db.Issues.AsNoTracking()
            .Where(i => issueIds.Contains(i.Id))
            .Select(i => new { i.Id, Status = i.Status!.Name, i.ClosedAt })
            .ToListAsync(ct);

        // Parsed here rather than filtered in SQL: a handful of rows, and a
        // JSON containment filter would have to branch on the provider.
        var changes = await db.IssueEvents.AsNoTracking()
            .Where(e => issueIds.Contains(e.IssueId) && e.Kind == EfHatchIssueEvent.StatusChanged)
            .Select(e => new { e.IssueId, e.Payload })
            .ToListAsync(ct);
        var merged = changes.Where(e => MarkedMerged(e.Payload)).Select(e => e.IssueId).ToHashSet();

        var landings = await db.Landings.AsNoTracking()
            .Where(l => issueIds.Contains(l.IssueId))
            .OrderBy(l => l.Canonical)
            .Select(l => new { l.IssueId, l.Canonical, l.Sha })
            .ToListAsync(ct);
        var landedOn = landings
            .GroupBy(l => l.IssueId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BallotLandingDto>)g.Select(l => new BallotLandingDto(l.Canonical, l.Sha)).ToList());

        return issues.ToDictionary(
            i => i.Id,
            i => (i.Status, i.ClosedAt, merged.Contains(i.Id), landedOn.GetValueOrDefault(i.Id, [])));
    }

    /// <summary>Whether a status change's payload carries <c>merged: true</c>. A payload that will not parse says no.</summary>
    private static bool MarkedMerged(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("merged", out var flag)
                && flag.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// The first three non-blank lines, joined with a newline, and cut to 300
    /// characters with a full stop where it was cut. A character count and
    /// nothing else: markdown is left as written, never rendered or stripped.
    /// </summary>
    private static string Excerpt(string description)
    {
        var lines = description
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Take(ExcerptLines);

        var text = string.Join('\n', lines);
        return text.Length > ExcerptLength ? text[..ExcerptLength] + "." : text;
    }
}

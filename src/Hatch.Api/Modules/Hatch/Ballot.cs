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
        if (questions.Count == 0) return new BallotDto([]);

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
        return new BallotDto(roots.Select(id => Node(rows[id])).ToList());
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

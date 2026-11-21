using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The open question an ambition is aimed at, if one qualifies: the one rule the
/// scan and the work route share, so the queue never says a session will answer
/// something the claim then does not hand out.
/// </summary>
/// <remarks>
/// A question qualifies when it is a question with no answer, asked at
/// <c>build</c> or <c>merge</c> - never <c>hold</c>, which waits for a person -
/// on an issue outside a terminal column, beneath the ambition at any depth and
/// never on the ambition itself. It does not qualify when the issue it is on, any
/// issue between that one and the ambition, or the ambition is dangerous, or when
/// a <c>worked</c> row of the work log already names it: a question is aimed at
/// once. The newest asked wins.
/// </remarks>
public static class AnswerQuestions
{
    /// <summary>The question an ambition is aimed at, with where it stands.</summary>
    public sealed record Candidate(
        long IssueId, string IssueKey, string IssueTitle, long QuestionId, string Body,
        string? Options, string Level, string AskedBy, DateTimeOffset AskedAt)
    {
        /// <summary>
        /// The one sentence the queue shows: the issue, and the first line of the
        /// question, clipped.
        /// </summary>
        public string Sentence()
        {
            var line = Questions.FirstLine(Body);
            if (line.Length > MaxLineLength) line = line[..(MaxLineLength - 3)].TrimEnd() + "...";
            return $"answers the question on {IssueKey}: {line}";
        }
    }

    /// <summary>The longest first line of a question the queue's sentence carries.</summary>
    public const int MaxLineLength = 80;

    /// <summary>
    /// For each of <paramref name="ambitionIds"/> that has one, the question it is
    /// aimed at, keyed by ambition id. An ambition with none is absent.
    /// </summary>
    public static async Task<Dictionary<long, Candidate>> ForAsync(
        HatchContext db, DependencyGate gate, IReadOnlyCollection<long> ambitionIds, CancellationToken ct)
    {
        var found = new Dictionary<long, Candidate>();
        if (ambitionIds.Count == 0) return found;

        // Every project's questions, as the tree is read: a question on an issue in
        // a project the caller cannot see still stands beneath the ambition. What
        // leaves is a key, a title and a question, and only to a caller who is
        // dispatched the ambition.
        var open = await db.Comments.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Kind == EfHatchComment.Question)
            .Where(c => c.Level == QuestionLevels.BuildName || c.Level == QuestionLevels.MergeName)
            .Where(c => !db.Comments.Any(a => a.AnswersId == c.Id))
            .Where(c => !c.Issue!.Status!.IsTerminal)
            .Select(c => new
            {
                c.Id, c.IssueId, c.Body, c.Options, c.Level, c.Author, c.CreatedAt,
                ProjectKey = c.Issue!.Project!.Key, c.Issue!.Number, c.Issue!.Title,
            })
            .ToListAsync(ct);
        if (open.Count == 0) return found;

        var wanted = ambitionIds.ToHashSet();
        var beneath = open
            .Select(q => (Question: q, Ambition: gate.AmbitionAbove(q.IssueId)))
            .Where(x => x.Ambition is { } a && wanted.Contains(a)
                        && gate.EffectiveDangerous(x.Question.IssueId).Reason is null)
            .ToList();
        if (beneath.Count == 0) return found;

        var ids = beneath.Select(x => (long?)x.Question.Id).ToList();
        var answered = (await db.WorkLog.IgnoreQueryFilters().AsNoTracking()
                .Where(w => w.Outcome == ClaimOutcomes.Worked && ids.Contains(w.QuestionId))
                .Select(w => w.QuestionId)
                .ToListAsync(ct))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();

        foreach (var group in beneath.Where(x => !answered.Contains(x.Question.Id)).GroupBy(x => x.Ambition!.Value))
        {
            var q = group
                .OrderByDescending(x => x.Question.CreatedAt)
                .ThenByDescending(x => x.Question.Id)
                .First().Question;
            found[group.Key] = new Candidate(
                q.IssueId, IssueKey.Format(q.ProjectKey, q.Number), q.Title, q.Id, q.Body, q.Options,
                QuestionLevels.Normalise(q.Level), q.Author, q.CreatedAt);
        }

        return found;
    }

    /// <summary>The wire form of a candidate, as a dispatch carries it.</summary>
    public static WorkQuestionDto ToDto(Candidate c) => new(
        c.IssueKey, c.IssueTitle, c.QuestionId, c.Body, Questions.ReadOptions(c.Options),
        c.Level, c.AskedBy, c.AskedAt);
}

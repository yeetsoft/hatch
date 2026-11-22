using Hatch.Api.Ef;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Every question as the flow folds read it: when it was asked, and the first
/// answer a person gave or the lapse that closed it. The flow report and the
/// board health read both call this, so the two pair a question with its answer
/// the same way.
/// </summary>
internal static class QuestionTrail
{
    /// <summary>
    /// The questions, whole when <paramref name="from"/> and <paramref name="to"/> are
    /// null. With bounds, only those asked before <paramref name="to"/> and not
    /// already answered, or lapsed, before <paramref name="from"/> - they ended
    /// before the range and no bar reads them - and with answers and lapses read
    /// only from <paramref name="from"/> on.
    /// </summary>
    /// <remarks>
    /// Plain queries joined here rather than a subquery for the earliest
    /// answer: comments rather than events, since the `asked` event carries
    /// no question id. A lapse is the one answer the board writes itself, a
    /// comment and an `answered` event {questionId, lapsed: true} at the
    /// same instant; the comment carries no marker, so the event names it.
    /// The wait figures are over a person's answers, so that comment is set
    /// aside and the question is counted as lapsed instead. A lapse also writes
    /// an answer comment, so a question that lapsed before <paramref name="from"/>
    /// drops out with the rest that were answered before it.
    ///
    /// The prefilter is a containment on Postgres, which has no LIKE for
    /// jsonb, and a substring on InMemory, which has no containment.
    /// </remarks>
    internal static async Task<List<FlowReport.Question>> ReadAsync(
        HatchContext db, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var lapseAt = new Dictionary<long, DateTimeOffset>();
        var answered = db.IssueEvents.AsNoTracking()
            .Where(e => e.Kind == EfHatchIssueEvent.Answered && e.Payload != null);
        if (from is { } lapsedFrom) answered = answered.Where(e => e.At >= lapsedFrom);

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

        var askedQuery = db.Comments.AsNoTracking().Where(c => c.Kind == EfHatchComment.Question);
        if (to is { } askedBefore) askedQuery = askedQuery.Where(c => c.CreatedAt < askedBefore);
        if (from is { } answeredFrom)
            askedQuery = askedQuery.Where(c => !db.Comments.Any(a => a.AnswersId == c.Id && a.CreatedAt < answeredFrom));

        var asked = await askedQuery
            .Select(c => new { c.Id, c.IssueId, c.CreatedAt })
            .ToListAsync(ct);

        var answerQuery = db.Comments.AsNoTracking().Where(c => c.AnswersId != null);
        if (from is { } since) answerQuery = answerQuery.Where(c => c.CreatedAt >= since);

        var answers = await answerQuery
            .Select(c => new { AnswersId = c.AnswersId!.Value, c.CreatedAt })
            .ToListAsync(ct);

        var earliestAnswer = answers
            .Where(a => !(lapseAt.TryGetValue(a.AnswersId, out var lapsedAt) && lapsedAt == a.CreatedAt))
            .GroupBy(a => a.AnswersId)
            .ToDictionary(g => g.Key, g => g.Min(a => a.CreatedAt));

        return asked
            .Select(q => new FlowReport.Question(
                q.IssueId, q.Id, q.CreatedAt,
                earliestAnswer.TryGetValue(q.Id, out var answeredAt) ? answeredAt : null,
                !earliestAnswer.ContainsKey(q.Id) && lapseAt.TryGetValue(q.Id, out var lapsedAt) ? lapsedAt : null))
            .ToList();
    }
}

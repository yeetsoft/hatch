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
    /// That drops one question the whole read keeps: one that lapsed before
    /// <paramref name="from"/> and was then answered by a person from it on, which
    /// the whole read counts as answered, since a person's answer beats the lapse.
    /// With <paramref name="laterAnswerBeatsLapse"/> it is put back: the questions
    /// a person answered from <paramref name="from"/> on that were dropped are the
    /// candidates, and one whose only earlier answer is the lapse comment (an
    /// <c>answered</c> event at that issue and instant marks it) is read by id. Two
    /// or more earlier answers mean a person answered before <paramref name="from"/>
    /// too, and it stays dropped, as the whole read has it answered then. No
    /// statement is sent when no question is a candidate.
    ///
    /// The prefilter is a containment on Postgres, which has no LIKE for
    /// jsonb, and a substring on InMemory, which has no containment.
    /// </remarks>
    internal static async Task<List<FlowReport.Question>> ReadAsync(
        HatchContext db, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct,
        bool laterAnswerBeatsLapse = false)
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

        var restored = new List<FlowReport.Question>();
        if (laterAnswerBeatsLapse && from is { } windowStart)
        {
            var kept = asked.Select(q => q.Id).ToHashSet();
            var candidates = earliestAnswer.Keys.Where(id => !kept.Contains(id)).ToList();

            if (candidates.Count > 0)
            {
                var before = await db.Comments.AsNoTracking()
                    .Where(c => c.AnswersId != null && candidates.Contains(c.AnswersId.Value) && c.CreatedAt < windowStart)
                    .Select(c => new { AnswersId = c.AnswersId!.Value, c.IssueId, c.CreatedAt })
                    .ToListAsync(ct);

                // Exactly one answer before the window, and the board's own.
                var single = before.GroupBy(a => a.AnswersId).Where(g => g.Count() == 1).Select(g => g.Single()).ToList();

                if (single.Count > 0)
                {
                    var issues = single.Select(a => a.IssueId).Distinct().ToList();
                    var instants = single.Select(a => a.CreatedAt).Distinct().ToList();
                    var marked = await db.IssueEvents.AsNoTracking()
                        .Where(e => e.Kind == EfHatchIssueEvent.Answered && e.Payload != null
                            && issues.Contains(e.IssueId) && instants.Contains(e.At))
                        .Select(e => new { e.IssueId, e.At, e.Payload })
                        .ToListAsync(ct);

                    var lapses = new HashSet<(long IssueId, DateTimeOffset At, long QuestionId)>();
                    foreach (var e in marked)
                    {
                        if (!LetGo.IsLapsedAnswer(e.Payload)) continue;
                        if (IssueEventPayload.Parse(e.Payload) is { } payload
                            && payload.TryGetProperty("questionId", out var id) && id.TryGetInt64(out var questionId))
                            lapses.Add((e.IssueId, e.At, questionId));
                    }

                    var ids = single
                        .Where(a => lapses.Contains((a.IssueId, a.CreatedAt, a.AnswersId)))
                        .Select(a => a.AnswersId)
                        .ToList();

                    if (ids.Count > 0)
                    {
                        var questionsQuery = db.Comments.AsNoTracking()
                            .Where(c => c.Kind == EfHatchComment.Question && ids.Contains(c.Id));
                        if (to is { } beforeEnd) questionsQuery = questionsQuery.Where(c => c.CreatedAt < beforeEnd);

                        restored = (await questionsQuery
                                .Select(c => new { c.Id, c.IssueId, c.CreatedAt })
                                .ToListAsync(ct))
                            .Select(q => new FlowReport.Question(q.IssueId, q.Id, q.CreatedAt, earliestAnswer[q.Id]))
                            .ToList();
                    }
                }
            }
        }

        return asked
            .Select(q => new FlowReport.Question(
                q.IssueId, q.Id, q.CreatedAt,
                earliestAnswer.TryGetValue(q.Id, out var answeredAt) ? answeredAt : null,
                !earliestAnswer.ContainsKey(q.Id) && lapseAt.TryGetValue(q.Id, out var lapsedAt) ? lapsedAt : null))
            .Concat(restored)
            .ToList();
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// One definition of "open," and one of "waiting on somebody" - used by
/// everything that asks either.
///
/// A question is open when no comment answers it - computed here rather than
/// stored as a flag on the row, so a question cannot be open and answered at
/// the same time because two writes disagreed. Waiting narrows that to a
/// question whose issue stands outside a terminal column: open is the whole
/// record, waiting is what is actually owed.
/// <see cref="BoardController"/>, <see cref="Rollup"/>,
/// <see cref="AttentionController"/> and <see cref="QuestionsController"/>
/// all read Waiting now; <see cref="BuildCheckController"/> and
/// <see cref="IssueClaimController"/> still read Open, on purpose, for
/// reasons each names where it calls in. They share this file so none of
/// them can come to a different answer than the others asking the same
/// question.
/// </summary>
public static class Questions
{
    /// <summary>Every unanswered question in the house.</summary>
    public static IQueryable<EfHatchComment> Open(HatchContext db) =>
        db.Comments.AsNoTracking()
            .Where(c => c.Kind == EfHatchComment.Question)
            .Where(c => !db.Comments.Any(a => a.AnswersId == c.Id));

    /// Every unanswered question outside a terminal column - the one a person is actually still owed.
    public static IQueryable<EfHatchComment> Waiting(HatchContext db) =>
        Open(db).Where(c => !c.Issue!.Status!.IsTerminal);

    /// <summary>How many questions each issue is waiting on. Issues waiting on none are absent rather than zero.</summary>
    public static async Task<Dictionary<long, int>> WaitingCountsAsync(HatchContext db, CancellationToken ct) =>
        await Waiting(db)
            .GroupBy(c => c.IssueId)
            .Select(g => new { IssueId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.IssueId, x => x.Count, ct);

    // ---- Lapsed ----

    /// <summary>
    /// Whether a stall question, asked at <paramref name="askedAt"/> and open
    /// still, has gone unanswered long enough to lapse - see
    /// <see cref="HatchOptions.StallLapseMinutes"/>. Two things must both be
    /// true: the question itself is old enough, and nothing has happened to the
    /// issue since - <paramref name="newestEventAt"/>, the latest row this
    /// issue's own <see cref="EfHatchIssueEvent"/>s hold, is old enough too. A
    /// comment left without answering, a status change, anything at all, resets
    /// the second half without touching the first: an issue somebody is still
    /// looking at does not lapse just because the question itself is old.
    /// </summary>
    /// <remarks>
    /// <paramref name="lapseSeconds"/> at or below zero is lapsing turned off -
    /// see <see cref="IssueClaims.StallLapseSeconds"/> - and this always answers
    /// false, whatever the ages.
    /// </remarks>
    public static bool IsLapsed(
        DateTimeOffset askedAt, DateTimeOffset? newestEventAt, int lapseSeconds, DateTimeOffset now)
    {
        if (lapseSeconds <= 0) return false;

        var cutoff = now.AddSeconds(-lapseSeconds);
        return askedAt <= cutoff && (newestEventAt ?? DateTimeOffset.MinValue) <= cutoff;
    }

    /// <summary>The latest event this issue's trail holds, or null for an issue with none.</summary>
    public static Task<DateTimeOffset?> NewestEventAtAsync(HatchContext db, long issueId, CancellationToken ct) =>
        db.IssueEvents.AsNoTracking()
            .Where(e => e.IssueId == issueId)
            .OrderByDescending(e => e.At)
            .Select(e => (DateTimeOffset?)e.At)
            .FirstOrDefaultAsync(ct);

    /// <summary>The same, for however many issues a scan is judging at once - one query rather than one a row.</summary>
    public static async Task<Dictionary<long, DateTimeOffset>> NewestEventAtByIssueAsync(
        HatchContext db, CancellationToken ct) =>
        await db.IssueEvents.AsNoTracking()
            .GroupBy(e => e.IssueId)
            .Select(g => new { IssueId = g.Key, At = g.Max(e => e.At) })
            .ToDictionaryAsync(x => x.IssueId, x => x.At, ct);

    /// <summary>How many open questions each issue is waiting on, and whether a lapsed stall question was among them - the dispatcher's own count, not the board's.</summary>
    /// <remarks>
    /// This is deliberately not <see cref="WaitingCountsAsync"/> with an extra
    /// argument. That method also badges a card and rolls up an epic
    /// (<see cref="BoardController"/>, <see cref="Rollup"/>), and a stall
    /// nobody has touched in the lapse window is still a real open question
    /// there - it is only the dispatcher's fold that a lapsed one stops
    /// counting against.
    /// </remarks>
    public static async Task<Dictionary<long, OpenSummary>> DispatchCountsAsync(
        HatchContext db, int lapseSeconds, DateTimeOffset now, CancellationToken ct)
    {
        var open = await Open(db)
            .Select(c => new { c.IssueId, c.CreatedAt, c.Options })
            .ToListAsync(ct);

        var result = new Dictionary<long, OpenSummary>();
        if (open.Count == 0) return result;

        var newest = await NewestEventAtByIssueAsync(db, ct);

        foreach (var q in open)
        {
            var lapsed = StallAnswers.IsStall(ReadOptions(q.Options))
                && IsLapsed(q.CreatedAt, newest.TryGetValue(q.IssueId, out var at) ? at : null, lapseSeconds, now);

            var soFar = result.GetValueOrDefault(q.IssueId, new OpenSummary(0, false));
            result[q.IssueId] = soFar with
            {
                Waiting = soFar.Waiting + (lapsed ? 0 : 1),
                LapsedStall = soFar.LapsedStall || lapsed,
            };
        }

        return result;
    }

    /// <summary>
    /// The stored options as a list, or null for a question asked in prose.
    /// </summary>
    /// <remarks>
    /// A row that will not parse reads as absent rather than throwing the
    /// question away - the same rule the event trail follows for its payload.
    /// The body is the question; the options are how it is offered, and a
    /// question that loses its menu is still answerable in the box below it.
    /// </remarks>
    public static IReadOnlyList<QuestionOptionDto>? ReadOptions(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;

        try
        {
            var options = JsonSerializer.Deserialize<List<QuestionOptionDto>>(stored, Json);
            return options is { Count: > 0 } ? options : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>What goes in the column, or null when nothing was offered.</summary>
    public static string? WriteOptions(IReadOnlyList<QuestionOptionDto>? options) =>
        options is { Count: > 0 } ? JsonSerializer.Serialize(options, Json) : null;

    /// <summary>
    /// camelCase, matching the wire. The column is read by the browser through
    /// the same DTO it is written from, and a jsonb column whose casing
    /// disagreed with its own API would be a trap laid for whoever queries it
    /// in SQL one day.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Every question ever asked about one issue, answers attached, oldest first.</summary>
    public static Task<List<QuestionDto>> ForIssueAsync(HatchContext db, long issueId, CancellationToken ct) =>
        ProjectAsync(db, db.Comments.AsNoTracking()
            .Where(c => c.IssueId == issueId && c.Kind == EfHatchComment.Question), ct);

    /// <summary>
    /// Questions with their answers attached, in two queries rather than one
    /// per question: the house-wide list is read when the board is stuck, which
    /// is exactly when there are most of them.
    /// </summary>
    /// <remarks>
    /// Oldest first, everywhere, because that is answering order - the question
    /// that has been waiting longest is the one holding something up longest.
    /// </remarks>
    public static async Task<List<QuestionDto>> ProjectAsync(HatchContext db, IQueryable<EfHatchComment> questions, CancellationToken ct)
    {
        var rows = await questions
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                c.Body,
                c.Author,
                c.Options,
                c.CreatedAt,
                IssueProjectKey = c.Issue!.Project!.Key,
                IssueNumber = c.Issue!.Number,
                IssueTitle = c.Issue!.Title,
                IssueIsTerminal = c.Issue!.Status!.IsTerminal,
            })
            .ToListAsync(ct);

        if (rows.Count == 0) return [];

        var ids = rows.Select(r => r.Id).ToList();
        var answers = await db.Comments.AsNoTracking()
            .Where(c => c.AnswersId != null && ids.Contains(c.AnswersId.Value))
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Author, c.Body, c.Kind, c.AnswersId, c.Options, c.CreatedAt })
            .ToListAsync(ct);

        var byQuestion = answers
            .Select(a => new CommentDto(a.Id, a.Author, a.Body, a.Kind, a.AnswersId, ReadOptions(a.Options), a.CreatedAt))
            .GroupBy(a => a.AnswersId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CommentDto>)g.ToList());

        return rows.Select(r => new QuestionDto(
            r.Id,
            IssueKey.Format(r.IssueProjectKey, r.IssueNumber),
            r.IssueTitle,
            r.Body,
            r.Author,
            r.CreatedAt,
            ReadOptions(r.Options),
            byQuestion.TryGetValue(r.Id, out var found) ? found : [],
            r.IssueIsTerminal)).ToList();
    }
}

/// <summary>
/// One issue's open questions, as the dispatcher counts them - see
/// <see cref="Questions.DispatchCountsAsync"/>.
/// </summary>
/// <param name="Waiting">Open questions, leaving out a lapsed stall question.</param>
/// <param name="LapsedStall">Whether at least one of those left out was a lapsed stall question - what lets a row that is clear say so.</param>
public sealed record OpenSummary(int Waiting, bool LapsedStall);

using System.Text.Json;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// One definition of "open," and one of "waiting on somebody" - used by
/// everything that asks either.
///
/// A question is open when no comment answers it - computed here rather than
/// stored as a flag on the row, so a question cannot be open and answered at
/// the same time because two writes disagreed. Waiting narrows that to a
/// question whose issue stands outside a terminal column, or one asked at
/// <c>merge</c>, which the work did not wait for and which stays owed a look
/// back after the issue closes: open is the whole record, waiting is what is
/// actually owed.
/// <see cref="BoardController"/>, <see cref="Rollup"/>,
/// <see cref="AttentionController"/> and <see cref="QuestionsController"/>
/// all read Waiting now; <see cref="BuildCheckController"/> still reads
/// Open, on purpose, for the reason it names where it calls in. They share
/// this file so none of them can come to a different answer than the others
/// asking the same question.
/// </summary>
public static class Questions
{
    /// <summary>Every unanswered question in the house.</summary>
    public static IQueryable<EfHatchComment> Open(HatchContext db) =>
        db.Comments.AsNoTracking()
            .Where(c => c.Kind == EfHatchComment.Question)
            .Where(c => !db.Comments.Any(a => a.AnswersId == c.Id));

    /// Every unanswered question outside a terminal column, every unanswered <c>merge</c> question wherever its issue stands, and every <c>build</c> or <c>merge</c> question a key alone has answered - the one a person is actually still owed.
    public static IQueryable<EfHatchComment> Waiting(HatchContext db) =>
        Open(db).Where(c => !c.Issue!.Status!.IsTerminal || c.Level == QuestionLevels.MergeName)
            .Union(ProgramAnswered(db));

    /// Every unanswered question outside a terminal column - what can still hold work, so a merge question awaiting its look back on a closed issue is not here.
    public static IQueryable<EfHatchComment> Holding(HatchContext db) =>
        Open(db).Where(c => !c.Issue!.Status!.IsTerminal);

    /// <summary>
    /// Every <c>build</c> or <c>merge</c> question with at least one answer, none of them a
    /// person's - the loop decided alone, on an open or a closed issue, and nobody has looked
    /// back on it yet. A null <see cref="EfHatchComment.AuthorKind"/> counts as a person's, so a
    /// row answered before the column existed stays answered rather than reopening as a look
    /// back.
    /// </summary>
    private static IQueryable<EfHatchComment> ProgramAnswered(HatchContext db) =>
        db.Comments.AsNoTracking()
            .Where(c => c.Kind == EfHatchComment.Question)
            .Where(c => c.Level == QuestionLevels.BuildName || c.Level == QuestionLevels.MergeName)
            .Where(c => db.Comments.Any(a => a.AnswersId == c.Id))
            .Where(c => !db.Comments.Any(a => a.AnswersId == c.Id && a.AuthorKind != ActorKind.Key));

    /// <summary>
    /// Whether a question is a look back: the work already went ahead on it, and what is left
    /// is to keep or overturn it after the fact. <see cref="ProjectAsync"/> and
    /// <see cref="QuestionOverturnController"/> both read this, so neither can decide a question
    /// is a look back the other does not.
    /// </summary>
    /// <remarks>
    /// True at <c>build</c> or <c>merge</c> when every answer given is a key's and there is at
    /// least one, whatever column the issue stands in - the loop decided and only a program
    /// spoke on it. Also true, as before, at <c>merge</c> with no answers once the issue has
    /// reached a terminal column - the work went ahead on the recommendation with nobody
    /// answering at all.
    /// </remarks>
    public static bool IsLookBack(string? level, bool issueIsTerminal, IReadOnlyList<string?> answerAuthorKinds)
    {
        var normalised = QuestionLevels.Normalise(level);
        if (normalised == QuestionLevels.HoldName) return false;

        if (answerAuthorKinds.Count > 0) return answerAuthorKinds.All(k => k == ActorKind.Key);

        return normalised == QuestionLevels.MergeName && issueIsTerminal;
    }

    /// <summary>
    /// Where the question <paramref name="questionId"/> stands, or null when
    /// there is no such question. Answers are read oldest first, so the latest is
    /// the last. <paramref name="ignoreFilters"/> reads past the request's
    /// project filters, as <see cref="WaitingLevelsAsync"/> does: a caller that
    /// has judged who may answer by other means must not have the question
    /// vanish because of whose request it runs in.
    /// </summary>
    public static async Task<QuestionStanding?> StandingAsync(
        HatchContext db, long questionId, CancellationToken ct, bool ignoreFilters = false)
    {
        var comments = ignoreFilters ? db.Comments.IgnoreQueryFilters() : db.Comments;

        var question = await comments.AsNoTracking()
            .Where(c => c.Id == questionId && c.Kind == EfHatchComment.Question)
            .Select(c => new { c.IssueId, c.Level, IsTerminal = c.Issue!.Status!.IsTerminal })
            .FirstOrDefaultAsync(ct);
        if (question is null) return null;

        var answers = await comments.AsNoTracking()
            .Where(a => a.AnswersId == questionId)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Select(a => new { a.Author, a.AuthorKind })
            .ToListAsync(ct);
        var latest = answers.LastOrDefault();

        var stand = IsLookBack(question.Level, question.IsTerminal, answers.Select(a => a.AuthorKind).ToList()) ? QuestionStand.LookBack
            : answers.Count == 0 ? QuestionStand.Unanswered
            : QuestionStand.Answered;

        return new QuestionStanding(stand, question.IssueId, latest?.Author, latest?.AuthorKind);
    }

    /// <summary>
    /// What the work went ahead on for a look-back: the first line of the first answer a key
    /// gave, or else the label of the option recommended. Null where neither is there.
    /// <see cref="Ballot"/> and the Ambitions page's changes read both say it from here, so
    /// they cannot word one decision two ways.
    /// </summary>
    public static string? WentAheadOn(QuestionDto question)
    {
        var programAnswer = question.Answers.FirstOrDefault(a => a.AuthorKind == ActorKind.Key);
        return programAnswer is not null
            ? FirstLine(programAnswer.Body)
            : question.Options?.FirstOrDefault(o => o.Recommended)?.Label;
    }

    /// <summary>The first non-blank line of a comment body, trimmed. Empty where the body is all blank.</summary>
    public static string FirstLine(string body) =>
        body.Split('\n').Select(l => l.TrimEnd('\r').Trim()).FirstOrDefault(l => l.Length > 0) ?? "";

    /// <summary>How many questions each issue is waiting on. Issues waiting on none are absent rather than zero.</summary>
    public static async Task<Dictionary<long, int>> WaitingCountsAsync(HatchContext db, CancellationToken ct) =>
        await Waiting(db)
            .GroupBy(c => c.IssueId)
            .Select(g => new { IssueId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.IssueId, x => x.Count, ct);

    /// <summary>
    /// The strictest level each issue is waiting at, and how many of its
    /// questions stand at that level. Issues waiting on none are absent.
    /// </summary>
    /// <remarks>
    /// The count is of the strictest level alone, so a sentence saying "1
    /// question at build" never counts a question that would have let the
    /// work merge. A row with no level reads as <see cref="QuestionLevels.HoldName"/>.
    /// <paramref name="ignoreFilters"/> reads every project's questions, as the
    /// dispatcher's tree reads every project's issues.
    /// </remarks>
    public static async Task<Dictionary<long, (string Level, int Count)>> WaitingLevelsAsync(
        HatchContext db, CancellationToken ct, bool ignoreFilters = false)
    {
        // Query-wide, so the answer lookup inside Open is unscoped too: an
        // ancestor's question in a project the reader cannot see still holds.
        var waiting = ignoreFilters
            ? db.Comments.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.Kind == EfHatchComment.Question)
                .Where(c => !db.Comments.Any(a => a.AnswersId == c.Id))
                .Where(c => !c.Issue!.Status!.IsTerminal)
            // Holding, not Waiting: a merge question on a closed ancestor is owed a look back, not a hold on its children.
            : Holding(db);

        var rows = await waiting
            .Select(c => new { c.IssueId, c.Level })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.IssueId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var level = QuestionLevels.Strictest(g.Select(r => r.Level));
                    return (level, g.Count(r => QuestionLevels.Normalise(r.Level) == level));
                });
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
                c.Level,
                c.CreatedAt,
                c.IssueId,
                IssueClosedAt = c.Issue!.ClosedAt,
                IssueProjectKey = c.Issue!.Project!.Key,
                IssueNumber = c.Issue!.Number,
                IssueTitle = c.Issue!.Title,
                IssueIsTerminal = c.Issue!.Status!.IsTerminal,
            })
            .ToListAsync(ct);

        if (rows.Count == 0) return [];

        // One walk per distinct issue, and one key lookup per distinct
        // ambition found - a house-wide list repeats neither for each of its
        // questions.
        var ambitionIds = new Dictionary<long, long?>();
        foreach (var issueId in rows.Select(r => r.IssueId).Distinct())
            ambitionIds[issueId] = await IssueServesController.AmbitionAboveAsync(db, issueId, ct);

        var ambitionKeys = new Dictionary<long, string?>();
        foreach (var ambitionId in ambitionIds.Values.Where(id => id is not null).Select(id => id!.Value).Distinct())
            ambitionKeys[ambitionId] = await IssueProjection.KeyOfAsync(db, ambitionId, ct);

        var ids = rows.Select(r => r.Id).ToList();
        var answers = await db.Comments.AsNoTracking()
            .Where(c => c.AnswersId != null && ids.Contains(c.AnswersId.Value))
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Author, c.Body, c.Kind, c.AnswersId, c.Options, c.CreatedAt, c.AuthorKind })
            .ToListAsync(ct);

        var closedAt = rows.ToDictionary(r => r.Id, r => r.IssueClosedAt);
        var byQuestion = answers
            .Select(a => new CommentDto(
                a.Id, a.Author, a.Body, a.Kind, a.AnswersId, ReadOptions(a.Options), a.CreatedAt,
                AnsweredAfter: closedAt[a.AnswersId!.Value] is { } closed && a.CreatedAt > closed,
                AuthorKind: a.AuthorKind))
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
            r.IssueIsTerminal,
            QuestionLevels.Normalise(r.Level),
            LookBack: IsLookBack(
                r.Level, r.IssueIsTerminal,
                byQuestion.TryGetValue(r.Id, out var ownAnswers) ? ownAnswers.Select(a => a.AuthorKind).ToList() : []),
            AmbitionKey: ambitionIds[r.IssueId] is { } ambitionId ? ambitionKeys.GetValueOrDefault(ambitionId) : null)).ToList();
    }
}

/// <summary>Where a question stands: nobody has answered it, somebody has, or the work went ahead on it and what is left is a look back.</summary>
public enum QuestionStand { Unanswered, Answered, LookBack }

/// <summary>What <see cref="Questions.StandingAsync"/> reads. See that method.</summary>
/// <param name="Stand">Where the question stands.</param>
/// <param name="IssueId">The issue the question is on.</param>
/// <param name="LatestAuthor">Who wrote the latest answer, or null where there is none - a closed <c>merge</c> question nobody answered is a look back with no latest.</param>
/// <param name="LatestAuthorKind">An <see cref="ActorKind"/> of the latest answer's author, or null where there is no answer or the row predates the column.</param>
public record QuestionStanding(QuestionStand Stand, long IssueId, string? LatestAuthor, string? LatestAuthorKind);

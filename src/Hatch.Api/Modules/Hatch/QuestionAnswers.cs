using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Who is answering: a name, and whether that is a person or a key. Told to
/// <see cref="QuestionAnswers"/> rather than read from a request, so an answer
/// can be written for someone who has no request behind them.
/// </summary>
/// <param name="Kind">An <see cref="ActorKind"/>.</param>
public sealed record AnsweringActor(string Name, string Kind);

/// <summary>
/// The one place an answer to a question is written: the comment, the
/// <c>answered</c> event, and the edit to the question's chat message, in one
/// save. <see cref="IssueThreadController"/> calls it for the Ballot's and the
/// API's answers alike.
/// </summary>
/// <remarks>
/// Nothing here decides whether the answerer may answer, or whether the
/// question is still open: a look back is kept by answering again, so a second
/// answer is written like the first. The caller decides before it writes, with
/// <see cref="PersonScope"/> and <see cref="Questions.StandingAsync"/>.
/// The chat message is queued only when both <see cref="ChatOutbox"/> and
/// <see cref="ChatLinks"/> are present, as everywhere else a question is said
/// in chat.
/// </remarks>
public sealed class QuestionAnswers(
    HatchContext db, TimeProvider time, ChatOutbox? chatOutbox = null, ChatLinks? links = null)
{
    /// <summary>
    /// Writes <paramref name="body"/> as <paramref name="by"/>'s answer to the
    /// question <paramref name="questionId"/> on <paramref name="issueId"/>, and
    /// returns the comment. Null when that is not a question on that issue;
    /// nothing is written then.
    /// </summary>
    /// <param name="via">Where the answer came from when it was not the Ballot or a key; see <see cref="AnswerVia"/>.</param>
    public async Task<EfHatchComment?> WriteAsync(
        long issueId, long questionId, string body, AnsweringActor by, string? via, CancellationToken ct)
    {
        // Same issue, and actually a question. A cross-issue link would put
        // an answer on a thread nobody reading the question can see.
        var question = await db.Comments.AsNoTracking()
            .Where(c => c.Id == questionId && c.IssueId == issueId && c.Kind == EfHatchComment.Question)
            .Select(c => new { c.Id, c.Body, c.Options, c.Level })
            .FirstOrDefaultAsync(ct);

        if (question is null) return null;

        var now = time.GetUtcNow();

        var comment = new EfHatchComment
        {
            IssueId = issueId,
            Author = by.Name,
            AuthorKind = by.Kind,
            Body = body,
            Kind = EfHatchComment.Answer,
            AnswersId = question.Id,
            CreatedAt = now,
        };
        db.Comments.Add(comment);

        // The event carries no copy of the body - the comment row is the record,
        // and duplicating it here would mean an edit could make the two disagree.
        // It carries the question's id, because "when did this stop waiting on
        // somebody" is the question the trail gets asked.
        db.IssueEvents.Add(new EfHatchIssueEvent
        {
            IssueId = issueId,
            Actor = by.Name,
            Kind = EfHatchIssueEvent.Answered,
            Payload = System.Text.Json.JsonSerializer.Serialize(IssueEventPayload.Answered(question.Id, via)),
            At = now,
        });

        if (chatOutbox is not null && links is not null)
        {
            var forChat = await db.Issues.AsNoTracking().Where(i => i.Id == issueId)
                .Select(i => new { i.ProjectId, i.Title, i.Number, ProjectKey = i.Project!.Key, IsTerminal = i.Status!.IsTerminal })
                .FirstAsync(ct);
            var level = QuestionLevels.Normalise(question.Level);
            // Read before this answer is saved, so it is not among them; it is added by hand.
            var priorAuthorKinds = await db.Comments.AsNoTracking()
                .Where(a => a.AnswersId == question.Id).Select(a => a.AuthorKind).ToListAsync(ct);
            var lookBack = Questions.IsLookBack(level, forChat.IsTerminal, [.. priorAuthorKinds, by.Kind]);
            var content = QuestionChatContent.Build(
                links, IssueKey.Format(forChat.ProjectKey, forChat.Number), forChat.Title, question.Body,
                Questions.ReadOptions(question.Options), level, by.Name, comment.Body, lookBack);
            await chatOutbox.QueueAsync(ChatMessageKind.Question, $"comment:{question.Id}", forChat.ProjectId, content, ct);
        }

        await db.SaveChangesAsync(ct);
        return comment;
    }
}

using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Services.Chat;

/// <summary>
/// What a question's chat message says, asked or answered - see
/// docs/hatch-in-slack.md, "Questions in a room". Every caller passes the same
/// subject for the same question, so an answer, an overturn or a level change
/// edits the row asking queued rather than adding a second one.
/// </summary>
public static class QuestionChatContent
{
    /// <summary>
    /// The line saying who answered and with what. A reply to a refused press uses
    /// this too, so the two cannot be worded apart.
    /// </summary>
    public static string AnswerLine(string? answeredBy, string answerBody, bool lookBack) =>
        $"{(lookBack ? "A program" : answeredBy)} answered: {answerBody}";

    /// <param name="answeredBy">The answer's author, shown unless <paramref name="lookBack"/>. Null while unanswered.</param>
    /// <param name="answerBody">The latest answer's own text, with no history of earlier ones. Null while unanswered.</param>
    /// <param name="lookBack">True when every answer so far is a key's - see <see cref="Questions.IsLookBack"/> - so the message names no key.</param>
    public static ChatMessageContent Build(
        ChatLinks links, string issueKey, string issueTitle, string body,
        IReadOnlyList<QuestionOptionDto>? options, string level,
        string? answeredBy = null, string? answerBody = null, bool lookBack = false)
    {
        var lines = new List<string>();

        if (options is { Count: > 0 })
            lines.AddRange(options.Select(o => o.Recommended ? $"{o.Label} (recommended)" : o.Label));

        lines.Add($"Level: {level}");

        if (answerBody is not null)
            lines.Add(AnswerLine(answeredBy, answerBody, lookBack));

        return new ChatMessageContent(
            $"{issueKey} {issueTitle}",
            body,
            lines,
            [new ChatLink(issueKey, links.Issue(issueKey)), new ChatLink("Answer it", links.Ballot())]);
    }
}

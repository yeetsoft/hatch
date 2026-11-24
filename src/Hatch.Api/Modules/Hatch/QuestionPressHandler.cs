using Hatch.Api.Services.Chat;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Answers a question from a button press, then tells the presser why when it
/// did not answer: one reply, seen by them alone.
/// </summary>
/// <remarks>
/// The listener resolves every registered handler in a fresh scope with no HTTP
/// context, so the query filters hide nothing from <see cref="QuestionPresses"/>.
/// <see cref="QuestionPresses.AnswerAsync"/> has released its transaction and lock
/// by the time it returns, so a slow Slack holds nothing. A reply that cannot be
/// sent is logged and dropped: no queue, no retry, no finding, and the press's
/// answer is untouched. Nothing here logs the press's reply address, the reply or
/// an answer's text, and nothing leaves this method holding an exception that
/// might carry the address.
/// </remarks>
public sealed class QuestionPressHandler(
    QuestionPresses presses, IChatProvider provider, ChatLinks links, ILogger<QuestionPressHandler> logger) : IChatPressHandler
{
    public async Task HandleAsync(int workspaceId, ChatPress press, CancellationToken ct)
    {
        var outcome = await presses.AnswerAsync(workspaceId, press, ct);

        var reply = QuestionPressReplyContent.Build(links, outcome);
        if (reply is null) return;

        try
        {
            var result = await provider.ReplyAsync(press.ReplyUrl, reply, ct);
            if (result.Outcome != ChatSendOutcome.Sent)
                logger.LogWarning(
                    "A reply to a button press in chat workspace {WorkspaceId} was not sent: {Sentence}", workspaceId, result.Sentence);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The type alone: an HTTP exception's message can carry the address.
            logger.LogWarning(
                "A reply to a button press in chat workspace {WorkspaceId} failed: {Kind}", workspaceId, ex.GetType().Name);
        }
    }
}

using Hatch.Api.Services.Chat;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Answers a question from a button press. The outcome is dropped here: the reply
/// to the presser comes next, and goes in this class.
/// </summary>
/// <remarks>
/// The listener resolves every registered handler in a fresh scope with no HTTP
/// context, so the query filters hide nothing from <see cref="QuestionPresses"/>.
/// Nothing here logs the press or its reply address.
/// </remarks>
public sealed class QuestionPressHandler(QuestionPresses presses) : IChatPressHandler
{
    public async Task HandleAsync(int workspaceId, ChatPress press, CancellationToken ct) =>
        _ = await presses.AnswerAsync(workspaceId, press, ct);
}

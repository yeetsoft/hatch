using Hatch.Api.Services.Chat;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What a runner failure says in a chat room: one message when the failure
/// opens, edited once to say when it ended. The one place a runner failure
/// queues anything, called by both writers of a row, so every message about a
/// failure agrees on when it began. It adds rows through <see cref="ChatOutbox"/>
/// to the caller's unit of work and saves nothing.
/// </summary>
/// <remarks>
/// <para>A failure belongs to no project, so it goes to the rooms that take
/// problems belonging to none, in every workspace.</para>
///
/// <para>A failure is named by its key, checkout, kind and the millisecond it
/// began, not by the row's id, which does not exist before the save that also
/// queues the message. A refresh queues nothing: the message says only the
/// runner, the kind and when it began, and a beat changes none of them. The
/// checkout and what the tool said are never put in the message - a whole
/// channel reads it, and the runner's page shows both to whoever may read that
/// runner.</para>
///
/// <para>Nothing here is called when a runner goes quiet, so its message keeps
/// saying the failure stands until the runner's next beat ends it.</para>
/// </remarks>
public sealed class RunnerFailureMessages(ChatOutbox outbox, ChatLinks links)
{
    /// <summary>Queues the message for a row that has just opened. Call it after the row is added and before the save.</summary>
    public Task OpenedAsync(EfHatchRunnerFailure row, string runnerName, CancellationToken ct) =>
        outbox.QueueAsync(ChatMessageKind.Problem, SubjectOf(row), null, Standing(row, runnerName), ct);

    /// <summary>
    /// Edits the message to say the failure ended. A failure that opened before
    /// rooms heard of runner failures was never announced, so nothing is queued
    /// for it: an edit would otherwise post as a first message nobody saw begin.
    /// </summary>
    public async Task EndedAsync(EfHatchRunnerFailure row, string runnerName, DateTimeOffset endedAt, CancellationToken ct)
    {
        var subject = SubjectOf(row);
        if (!await outbox.KnowsAsync(ChatMessageKind.Problem, subject, ct)) return;

        await outbox.QueueAsync(ChatMessageKind.Problem, subject, null, Ended(row, runnerName, endedAt), ct);
    }

    /// <summary>Milliseconds, because the database keeps microseconds: the name must come out the same before the save and after it.</summary>
    private static string SubjectOf(EfHatchRunnerFailure row) =>
        ChatSubjectKey.Occurrence("runner-failure", row.ApiKeyId, row.Where, row.Kind, row.BeganAt.ToUnixTimeMilliseconds());

    private ChatMessageContent Standing(EfHatchRunnerFailure row, string runnerName) =>
        new(Title(row, runnerName), null, [$"Since {ChatText.When(row.BeganAt)}"], Links(runnerName));

    private ChatMessageContent Ended(EfHatchRunnerFailure row, string runnerName, DateTimeOffset endedAt) =>
        new($"Ended: {Title(row, runnerName)}", null,
            [$"Began {ChatText.When(row.BeganAt)}", $"Ended {ChatText.When(endedAt)}"], Links(runnerName));

    private static string Title(EfHatchRunnerFailure row, string runnerName) =>
        $"{runnerName}: {RunnerFailureWords.Sentence(row.Kind)}";

    /// <summary>The runner's page; with no address to give, no link.</summary>
    private List<ChatLink> Links(string runnerName) =>
        links.Runner(runnerName) is { } url ? [new ChatLink("Runner", url)] : [];
}

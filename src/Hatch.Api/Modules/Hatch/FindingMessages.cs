using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What a goose finding says in a chat room: one message when it opens, edited
/// in place only when the finding's sentence changes, and edited once more to say
/// it ended. It queues rows through <see cref="ChatOutbox"/> in the sweep's own
/// unit of work and sends nothing; <see cref="ChatSendJob"/> does that.
/// </summary>
/// <remarks>
/// <para>A finding is named by what it was when it opened - its check, subject,
/// token and the moment it opened - not by its row id, which does not exist until
/// the sweep saves. The same finding always has the same subject, and a later
/// finding for the same condition has another, so each gets its own message.</para>
///
/// <para>A finding with no project goes to the rooms that take problems
/// belonging to no project; one with a project goes to the rooms that carry
/// that project's problems.</para>
/// </remarks>
public sealed class FindingMessages(ChatOutbox outbox, ChatLinks links, HatchContext db)
{
    /// <summary>
    /// Says the finding is standing. The first call queues it for the rooms that
    /// carry it; every later call is a change, which edits only when the
    /// finding's sentence or start is different. A room named in
    /// <paramref name="exceptRoomId"/>, or in <paramref name="exceptWorkspaceId"/>,
    /// is left out - a finding about a room's failing sends is not sent to it.
    /// </summary>
    public async Task StandingAsync(
        EfHatchGooseFinding finding, CancellationToken ct, int? exceptRoomId = null, int? exceptWorkspaceId = null)
    {
        var content = new ChatMessageContent(
            finding.Sentence, null, [$"Since {ChatText.When(finding.BeganAt)}"], await LinksAsync(finding, ct));

        await outbox.QueueAsync(
            ChatMessageKind.Problem, SubjectOf(finding), finding.ProjectId, content, ct, exceptRoomId, exceptWorkspaceId);
    }

    /// <summary>
    /// Says the finding ended, on the message that announced it. Nothing is said
    /// for a finding that was never announced - one that opened before this
    /// existed - since a room would hear that something ended that it never heard began.
    /// </summary>
    public async Task ClosedAsync(EfHatchGooseFinding finding, CancellationToken ct)
    {
        if (finding.ClosedAt is not { } closedAt) return;
        if (!await outbox.KnowsAsync(ChatMessageKind.Problem, SubjectOf(finding), ct)) return;

        var content = new ChatMessageContent(
            $"Ended: {finding.Sentence}", null,
            [$"Began {ChatText.When(finding.BeganAt)}", $"Ended {ChatText.When(closedAt)}"],
            await LinksAsync(finding, ct));

        await outbox.QueueAsync(ChatMessageKind.Problem, SubjectOf(finding), finding.ProjectId, content, ct);
    }

    /// <summary>
    /// The finding's chat subject. Milliseconds, because the database keeps
    /// microseconds and a new finding's <c>OpenedAt</c> is the sweep's clock: the
    /// name must come out the same before the save and after it.
    /// </summary>
    private static string SubjectOf(EfHatchGooseFinding finding) =>
        ChatSubjectKey.Occurrence("finding", finding.Check, finding.Subject, finding.Token, finding.OpenedAt.ToUnixTimeMilliseconds());

    /// <summary>The issue the finding names, when it names one, and the goose page. A link with no address to give is left out.</summary>
    private async Task<IReadOnlyList<ChatLink>> LinksAsync(EfHatchGooseFinding finding, CancellationToken ct)
    {
        var result = new List<ChatLink>();

        if (finding.ProjectId is { } projectId)
        {
            var projectKey = await db.Projects.IgnoreQueryFilters()
                .Where(p => p.Id == projectId)
                .Select(p => p.Key)
                .FirstOrDefaultAsync(ct);

            if (GooseFindings.IssueKey(finding, projectKey) is { } issueKey && links.Issue(issueKey) is { } issueUrl)
                result.Add(new ChatLink(issueKey, issueUrl));
        }

        if (links.Goose() is { } gooseUrl)
            result.Add(new ChatLink("Goose", gooseUrl));

        return result;
    }
}

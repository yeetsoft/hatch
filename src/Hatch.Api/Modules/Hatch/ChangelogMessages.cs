using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Queues the chat message that tells a team an issue shipped, and keeps it
/// current while its release note changes. It adds rows to the request's
/// <see cref="HatchContext"/> through <see cref="ChatOutbox"/> and never saves,
/// so every call goes before the write's own save and lands or fails with the
/// close.
/// </summary>
/// <remarks>
/// <para>Only a parentless issue is news, as in the changelog report
/// (<see cref="ChangelogReport"/>), and a task Hatch filed to bring a
/// changelog file current is left out, as it is left out of the changelog
/// itself (<see cref="ChangelogTasks"/>). An import never calls this: issues
/// that arrive already closed are history.</para>
///
/// <para>The text is handed to the provider as it is; the provider escapes it.
/// A repeat call about one issue is <see cref="ChatOutbox.QueueAsync"/>'s
/// change path, so a reopen and a second close of equal words posts nothing
/// new.</para>
/// </remarks>
public sealed class ChangelogMessages(ChatOutbox outbox, ChatLinks links, HatchContext db)
{
    private const string Subject = "issue:";

    /// <summary>
    /// Queues one message for each room that carries the changelog of the
    /// issue's project, for each distinct parentless issue that is not a
    /// recorded changelog task. Call it before the save that closes them.
    /// </summary>
    public async Task QueueClosedAsync(IEnumerable<EfHatchIssue> closed, CancellationToken ct)
    {
        foreach (var issue in closed.Where(i => i.ParentId == null).DistinctBy(i => i.Id))
        {
            if (await db.ChangelogTasks.AsNoTracking().AnyAsync(t => t.IssueId == issue.Id, ct)) continue;

            await outbox.QueueAsync(ChatMessageKind.Changelog, $"{Subject}{issue.Id}", issue.ProjectId, await ContentAsync(issue, ct), ct);
        }
    }

    /// <summary>
    /// Edits the message announcing <paramref name="issue"/> to carry its
    /// release note. Does nothing for an issue that was never announced, or an
    /// old one closed before the message existed would be announced by a note.
    /// </summary>
    public async Task QueueNoteAsync(EfHatchIssue issue, CancellationToken ct)
    {
        var subject = $"{Subject}{issue.Id}";
        var announced = db.ChatSubjects.Local.Any(s => s.Kind == "changelog" && s.Subject == subject)
            || await db.ChatSubjects.AsNoTracking().AnyAsync(s => s.Kind == "changelog" && s.Subject == subject, ct);
        if (!announced) return;

        await outbox.QueueAsync(ChatMessageKind.Changelog, subject, issue.ProjectId, await ContentAsync(issue, ct), ct);
    }

    private async Task<ChatMessageContent> ContentAsync(EfHatchIssue issue, CancellationToken ct)
    {
        // A project move changes the key after the issue was loaded, and a
        // column made terminal loads no project at all.
        var projectKey = issue.Project is { } loaded && loaded.Id == issue.ProjectId
            ? loaded.Key
            : await db.Projects.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.Id == issue.ProjectId).Select(p => p.Key).FirstAsync(ct);

        var key = IssueKey.Format(projectKey, issue.Number);

        return new ChatMessageContent(
            $"{issue.Type} shipped: {issue.Title}",
            issue.ReleaseNote,
            [],
            [new ChatLink(key, links.Issue(key))]);
    }
}

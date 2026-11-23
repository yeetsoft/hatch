using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>What a queued chat message is: the room switch it answers to.</summary>
public enum ChatMessageKind
{
    Changelog,
    Question,
    Problem,
}

/// <summary>
/// Queues a message for the rooms that should hear it, in the caller's own unit
/// of work. It adds rows to the request's <see cref="HatchContext"/> and never
/// saves, so the rows land or fail with the write that caused them. Nothing is
/// sent here; the sender reads what is waiting.
/// </summary>
public sealed class ChatOutbox(HatchContext db, TimeProvider time)
{
    /// <summary>
    /// Queues <paramref name="content"/> about <paramref name="subject"/>.
    /// </summary>
    /// <param name="kind">Which room switch the message answers to.</param>
    /// <param name="subject">
    /// The caller's stable id for the thing, at most
    /// <see cref="EfHatchChatSubject.MaxSubjectLength"/> characters:
    /// <c>issue:{id}</c>, <c>comment:{id}</c>, <c>finding:{id}</c>.
    /// </param>
    /// <param name="projectId">
    /// The project the thing belongs to. Null is for <see cref="ChatMessageKind.Problem"/>
    /// only, and goes to rooms that take unowned problems.
    /// </param>
    /// <remarks>
    /// <para>The first call for a subject chooses the rooms now and adds one row
    /// each; finding no room is not an error. Every later call is a change: it
    /// edits the rows that exist and adds none, so a room set up afterwards never
    /// receives an edit as a fresh post. A dropped row is left alone. Equal content
    /// changes nothing. A row already sent goes back to waiting, with its backoff
    /// cleared; a row still waiting keeps its place.</para>
    /// </remarks>
    public async Task QueueAsync(ChatMessageKind kind, string subject, int? projectId, ChatMessageContent content, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(content);
        if (subject.Length > EfHatchChatSubject.MaxSubjectLength)
            throw new ArgumentException($"A chat subject is at most {EfHatchChatSubject.MaxSubjectLength} characters.", nameof(subject));
        if (projectId is null && kind != ChatMessageKind.Problem)
            throw new ArgumentException("Only a problem may belong to no project.", nameof(projectId));

        var kindName = kind.ToString().ToLowerInvariant();
        var json = content.Write();
        var now = time.GetUtcNow();

        var known = db.ChatSubjects.Local.Any(s => s.Kind == kindName && s.Subject == subject)
            || await db.ChatSubjects.AnyAsync(s => s.Kind == kindName && s.Subject == subject, ct);

        if (!known)
        {
            db.ChatSubjects.Add(new EfHatchChatSubject { Kind = kindName, Subject = subject, CreatedAt = now });

            foreach (var room in await RoomsAsync(kind, projectId, ct))
            {
                db.ChatMessages.Add(new EfHatchChatMessage
                {
                    RoomId = room.Id,
                    WorkspaceId = room.WorkspaceId,
                    ProjectId = projectId,
                    Kind = kindName,
                    Subject = subject,
                    Content = json,
                    Revision = 1,
                    SentRevision = 0,
                    QueuedAt = now,
                    PendingSince = now,
                });
            }

            return;
        }

        // The tracked rows cover what this unit of work has added and the query
        // covers the rest; a row in both is the one instance.
        var rows = await db.ChatMessages.IgnoreQueryFilters()
            .Where(m => m.Kind == kindName && m.Subject == subject)
            .ToListAsync(ct);
        foreach (var added in db.ChatMessages.Local.Where(m => m.Kind == kindName && m.Subject == subject))
            if (!rows.Contains(added)) rows.Add(added);

        foreach (var row in rows)
        {
            if (row.DroppedAt is not null || row.Content == json) continue;

            var wasSent = row.SentRevision == row.Revision;
            row.Content = json;
            row.Revision++;

            if (wasSent)
            {
                row.PendingSince = now;
                row.NextAttemptAt = null;
                row.Attempts = 0;
                row.LastRefusal = null;
            }
        }
    }

    /// <summary>
    /// The rooms that carry a message of this kind about this project: the switch
    /// is on, the room's workspace is bound to the project, and the room takes
    /// every project or has chosen this one. Read without the caller's scope, so
    /// who is writing never changes who hears.
    /// </summary>
    private async Task<List<EfHatchChatRoom>> RoomsAsync(ChatMessageKind kind, int? projectId, CancellationToken ct)
    {
        var rooms = db.ChatRooms.IgnoreQueryFilters().AsQueryable();

        if (projectId is not { } pid)
            return await rooms.Where(r => r.UnownedProblems).OrderBy(r => r.Id).ToListAsync(ct);

        rooms = kind switch
        {
            ChatMessageKind.Changelog => rooms.Where(r => r.Changelog),
            ChatMessageKind.Question => rooms.Where(r => r.Questions),
            _ => rooms.Where(r => r.Problems),
        };

        return await rooms
            .Where(r => db.ChatWorkspaceProjects.Any(w => w.WorkspaceId == r.WorkspaceId && w.ProjectId == pid))
            .Where(r => r.AllProjects || db.ChatRoomProjects.Any(p => p.RoomId == r.Id && p.ProjectId == pid))
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
    }
}

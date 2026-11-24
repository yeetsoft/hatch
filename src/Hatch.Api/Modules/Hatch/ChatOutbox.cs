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
    /// <param name="exceptRoomId">A room to leave out. Only the first call for a subject reads it.</param>
    /// <param name="exceptWorkspaceId">A workspace whose every room is left out. Only the first call reads it.</param>
    /// <remarks>
    /// <para>The first call for a subject chooses the rooms now and adds one row
    /// each; finding no room is not an error. Every later call is a change: it
    /// edits the rows that exist and adds none, so a room set up afterwards never
    /// receives an edit as a fresh post. A dropped row is left alone. Equal content
    /// changes nothing. A row already sent goes back to waiting, with its backoff
    /// cleared; a row still waiting keeps its place.</para>
    /// </remarks>
    public Task QueueAsync(
        ChatMessageKind kind, string subject, int? projectId, ChatMessageContent content, CancellationToken ct,
        int? exceptRoomId = null, int? exceptWorkspaceId = null)
    {
        if (projectId is null && kind != ChatMessageKind.Problem)
            throw new ArgumentException("Only a problem may belong to no project.", nameof(projectId));

        return QueueCoreAsync(kind, subject, projectId is { } p ? [p] : [], content, exceptRoomId, exceptWorkspaceId, ct);
    }

    /// <summary>
    /// Queues <paramref name="content"/> for a thing that belongs to several
    /// projects. A different name from <see cref="QueueAsync"/>, not an overload,
    /// since a null project would otherwise be ambiguous. Everything said there
    /// holds, except the rooms: they are the union over the projects - a room
    /// whose workspace is bound to any of them, and that takes every project or
    /// has chosen any of them - and a room hears it once, however many match.
    /// </summary>
    /// <remarks>
    /// The row's <c>ProjectId</c> is the lowest id among the projects that put
    /// the room in the union. Known limit: unbinding that one project from the
    /// workspace removes the row, though another of the projects still binds.
    /// </remarks>
    /// <param name="projectIds">At least one project; use <see cref="QueueAsync"/> with a null project for none.</param>
    /// <param name="exceptRoomId">A room to leave out. Only the first call for a subject reads it.</param>
    /// <param name="exceptWorkspaceId">A workspace whose every room is left out. Only the first call reads it.</param>
    public Task QueueForProjectsAsync(
        ChatMessageKind kind, string subject, IReadOnlyCollection<int> projectIds, ChatMessageContent content, CancellationToken ct,
        int? exceptRoomId = null, int? exceptWorkspaceId = null)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        if (projectIds.Count == 0)
            throw new ArgumentException("Name at least one project; a message about none is QueueAsync with a null project.", nameof(projectIds));

        return QueueCoreAsync(kind, subject, projectIds, content, exceptRoomId, exceptWorkspaceId, ct);
    }

    /// <summary>
    /// Whether a first call has already been made for this subject: it is
    /// tracked by this unit of work or stored. A change is not a first call, so a
    /// caller that wants to say something only about a thing it announced asks first.
    /// </summary>
    public async Task<bool> KnowsAsync(ChatMessageKind kind, string subject, CancellationToken ct)
    {
        var kindName = kind.ToString().ToLowerInvariant();
        return db.ChatSubjects.Local.Any(s => s.Kind == kindName && s.Subject == subject)
            || await db.ChatSubjects.AnyAsync(s => s.Kind == kindName && s.Subject == subject, ct);
    }

    private async Task QueueCoreAsync(
        ChatMessageKind kind, string subject, IReadOnlyCollection<int> projectIds, ChatMessageContent content,
        int? exceptRoomId, int? exceptWorkspaceId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(content);
        if (subject.Length > EfHatchChatSubject.MaxSubjectLength)
            throw new ArgumentException($"A chat subject is at most {EfHatchChatSubject.MaxSubjectLength} characters.", nameof(subject));

        var kindName = kind.ToString().ToLowerInvariant();
        var json = content.Write();
        var now = time.GetUtcNow();

        if (!await KnowsAsync(kind, subject, ct))
        {
            // The rooms are read before the subject is added: a throw in the
            // read must not leave a known subject with no rows, which no later
            // call would ever choose a room for.
            var rooms = await RoomsAsync(kind, projectIds, exceptRoomId, exceptWorkspaceId, ct);

            db.ChatSubjects.Add(new EfHatchChatSubject { Kind = kindName, Subject = subject, CreatedAt = now });

            foreach (var (room, projectId) in rooms)
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
    /// The rooms that carry a message of this kind about these projects, each
    /// with the project its row is filed under: the switch is on, the room's
    /// workspace is bound to the project, and the room takes every project or has
    /// chosen it. No project means a problem for the rooms that take unowned ones.
    /// A room matching several projects is one row, filed under the lowest id.
    /// Read without the caller's scope, so who is writing never changes who hears.
    /// </summary>
    private async Task<List<(EfHatchChatRoom Room, int? ProjectId)>> RoomsAsync(
        ChatMessageKind kind, IReadOnlyCollection<int> projectIds, int? exceptRoomId, int? exceptWorkspaceId, CancellationToken ct)
    {
        var rooms = db.ChatRooms.IgnoreQueryFilters().AsQueryable();
        if (exceptRoomId is { } skipRoom) rooms = rooms.Where(r => r.Id != skipRoom);
        if (exceptWorkspaceId is { } skipWorkspace) rooms = rooms.Where(r => r.WorkspaceId != skipWorkspace);

        if (projectIds.Count == 0)
        {
            var unowned = await rooms.Where(r => r.UnownedProblems).OrderBy(r => r.Id).ToListAsync(ct);
            return unowned.Select(r => (r, (int?)null)).ToList();
        }

        rooms = kind switch
        {
            ChatMessageKind.Changelog => rooms.Where(r => r.Changelog),
            ChatMessageKind.Question => rooms.Where(r => r.Questions),
            _ => rooms.Where(r => r.Problems),
        };

        var ids = projectIds.Distinct().OrderBy(id => id).ToArray();

        var bound = (await db.ChatWorkspaceProjects.IgnoreQueryFilters()
            .Where(w => ids.Contains(w.ProjectId))
            .Select(w => new { w.WorkspaceId, w.ProjectId })
            .ToListAsync(ct)).Select(w => (w.WorkspaceId, w.ProjectId)).ToHashSet();
        var boundWorkspaces = bound.Select(b => b.WorkspaceId).Distinct().ToArray();

        var chosen = (await db.ChatRoomProjects.IgnoreQueryFilters()
            .Where(p => ids.Contains(p.ProjectId))
            .Select(p => new { p.RoomId, p.ProjectId })
            .ToListAsync(ct)).Select(p => (p.RoomId, p.ProjectId)).ToHashSet();

        var candidates = await rooms
            .Where(r => boundWorkspaces.Contains(r.WorkspaceId))
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var found = new List<(EfHatchChatRoom, int?)>();
        foreach (var room in candidates)
        {
            // ids is ascending, so the first match is the lowest.
            foreach (var id in ids)
            {
                if (!bound.Contains((room.WorkspaceId, id))) continue;
                if (!room.AllProjects && !chosen.Contains((room.Id, id))) continue;

                found.Add((room, id));
                break;
            }
        }

        return found;
    }
}

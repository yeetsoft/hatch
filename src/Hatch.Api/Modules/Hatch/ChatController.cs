using Hatch.Api.Common;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Hatch.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The chat workspaces Hatch is connected to, set by an admin: connect one,
/// list them, replace a token, remove one, and fetch the app manifest to
/// create the app from. A workspace binds projects, whose channels an admin
/// sets up as rooms and can send a test message to. Every route is admin and
/// person-only - a key never reads or writes a workspace or a room, since a
/// bot token is a credential that can speak for the install.
/// </summary>
/// <remarks>
/// <para>No class-level attribute, for the reason
/// <see cref="ProjectsController"/> gives: every action is decorated
/// explicitly.</para>
///
/// <para>Nothing logged here, and nothing returned, carries a token or any
/// part of one. The token is stored through <see cref="SecretProtector"/> and
/// the DTO has no field for it.</para>
///
/// <para>Removing a workspace is one <c>DELETE</c>. What hangs off a workspace
/// goes with it by the database's own cascade, not by code here, so a later
/// table needs only a cascading foreign key.</para>
/// </remarks>
[ApiController]
[Route("api/chat")]
public class ChatController(
    HatchContext db, IChatProvider provider, ICallerIdentity caller, TimeProvider time,
    ILogger<ChatController> logger) : ControllerBase
{
    /// <summary>Every connected workspace, oldest first.</summary>
    [HttpGet("workspaces")]
    [RequireAdmin]
    public async Task<ActionResult<IReadOnlyList<ChatWorkspaceDto>>> GetWorkspaces(CancellationToken ct)
    {
        var rows = await db.ChatWorkspaces.AsNoTracking().OrderBy(w => w.CreatedAt).ThenBy(w => w.Id).ToListAsync(ct);
        var bound = await BoundProjectsByWorkspaceAsync(ct);

        return rows.Select(w => Dto(w, bound.GetValueOrDefault(w.Id, []))).ToList();
    }

    /// <summary>
    /// Connects the workspace a token belongs to. The token is checked with the
    /// service first, and what the service says is the workspace's name and id;
    /// the caller names neither.
    /// </summary>
    [HttpPost("workspaces")]
    [RequireAdmin]
    public async Task<ActionResult<ChatWorkspaceDto>> ConnectWorkspace(ChatTokenRequest request, CancellationToken ct)
    {
        var token = request.Token?.Trim();
        if (string.IsNullOrEmpty(token)) return BadRequest("a bot token is needed");

        var check = await provider.CheckTokenAsync(token, ct);
        if (!check.Accepted) return BadRequest(check.Refusal ?? "the token was not accepted");
        if (TooLong(check) is { } tooLong) return BadRequest(tooLong);

        if (await FindAsync(check.WorkspaceId!, ct) is not null) return Conflict(AlreadyConnected);

        var now = time.GetUtcNow();
        var row = new EfHatchChatWorkspace
        {
            Provider = provider.Name,
            ExternalId = check.WorkspaceId!,
            Name = NameOf(check),
            Token = SecretProtector.Protect(token),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ChatWorkspaces.Add(row);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two connects of one workspace racing: the unique index let one in.
            // Looked up afresh rather than read off the exception, so this holds
            // for any provider of the database; anything else is a real failure.
            db.Entry(row).State = EntityState.Detached;
            if (await FindAsync(check.WorkspaceId!, ct) is null) throw;

            return Conflict(AlreadyConnected);
        }

        logger.LogInformation("Chat workspace {WorkspaceId} ({Provider}) connected by {Actor}",
            row.Id, row.Provider, await caller.ActorNameAsync(ct));

        return Dto(row, []);
    }

    /// <summary>
    /// Replaces a workspace's token with one for the same workspace. A token
    /// that belongs to another workspace is refused and the old one is kept.
    /// </summary>
    [HttpPut("workspaces/{id:int}/token")]
    [RequireAdmin]
    public async Task<ActionResult<ChatWorkspaceDto>> ReplaceToken(int id, ChatTokenRequest request, CancellationToken ct)
    {
        var row = await db.ChatWorkspaces.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (row is null) return NotFound();

        var token = request.Token?.Trim();
        if (string.IsNullOrEmpty(token)) return BadRequest("a bot token is needed");

        var check = await provider.CheckTokenAsync(token, ct);
        if (!check.Accepted) return BadRequest(check.Refusal ?? "the token was not accepted");
        if (TooLong(check) is { } tooLong) return BadRequest(tooLong);

        if (!string.Equals(check.WorkspaceId, row.ExternalId, StringComparison.Ordinal))
            return Conflict("that token belongs to another workspace; connect that one instead, or use a token for this one");

        row.Token = SecretProtector.Protect(token);
        row.Name = NameOf(check);
        row.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Chat workspace {WorkspaceId} ({Provider}) given a new token by {Actor}",
            row.Id, row.Provider, await caller.ActorNameAsync(ct));

        return Dto(row, await BoundProjectIdsAsync(row.Id, ct));
    }

    /// <summary>Removes a workspace. Whatever hangs off it goes with it, in the database.</summary>
    [HttpDelete("workspaces/{id:int}")]
    [RequireAdmin]
    public async Task<IActionResult> RemoveWorkspace(int id, CancellationToken ct)
    {
        var row = await db.ChatWorkspaces.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (row is null) return NotFound();

        db.ChatWorkspaces.Remove(row);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Chat workspace {WorkspaceId} ({Provider}) removed by {Actor}",
            id, row.Provider, await caller.ActorNameAsync(ct));

        return NoContent();
    }

    /// <summary>The app manifest to paste into the chat service's own screen to create the app.</summary>
    [HttpGet("manifest")]
    [RequireAdmin]
    public ActionResult<ChatManifestDto> GetManifest() => new ChatManifestDto(provider.Name, provider.Manifest());

    /// <summary>
    /// Replaces the set of projects a workspace is bound to, with what the
    /// caller can see of the ones given. A hidden binding - one made against a
    /// project outside the caller's own scope - is never read here and so
    /// never touched: this replaces only the set the caller can see.
    /// </summary>
    [HttpPut("workspaces/{id:int}/projects")]
    [RequireAdmin]
    public async Task<ActionResult<IReadOnlyList<int>>> PutProjects(int id, ChatProjectsRequest request, CancellationToken ct)
    {
        if (!await db.ChatWorkspaces.AnyAsync(w => w.Id == id, ct)) return NotFound();

        var wanted = (request.ProjectIds ?? []).Distinct().ToList();
        var known = await VisibleProjectIdsAsync(ct);
        if (wanted.Any(p => !known.Contains(p))) return BadRequest(UnknownProject);

        // Only the delta: dropping and re-adding every row, wanted or not,
        // would cascade a room's chosen project away through ChatRoomProjects'
        // composite key even where the project stayed bound.
        var existing = await db.ChatWorkspaceProjects.Where(p => p.WorkspaceId == id).ToListAsync(ct);
        var existingIds = existing.Select(p => p.ProjectId).ToHashSet();

        db.ChatWorkspaceProjects.RemoveRange(existing.Where(p => !wanted.Contains(p.ProjectId)));
        var now = time.GetUtcNow();
        db.ChatWorkspaceProjects.AddRange(wanted.Where(p => !existingIds.Contains(p))
            .Select(p => new EfHatchChatWorkspaceProject { WorkspaceId = id, ProjectId = p, CreatedAt = now }));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Chat workspace {WorkspaceId} bound to {Count} projects by {Actor}",
            id, wanted.Count, await caller.ActorNameAsync(ct));

        return await BoundProjectIdsAsync(id, ct);
    }

    /// <summary>Every channel the workspace's token can see, read from the service each time.</summary>
    [HttpGet("workspaces/{id:int}/channels")]
    [RequireAdmin]
    public async Task<ActionResult<IReadOnlyList<ChatChannelDto>>> GetChannels(int id, CancellationToken ct)
    {
        var workspace = await db.ChatWorkspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workspace is null) return NotFound();

        var token = SecretProtector.Unprotect(workspace.Token);
        if (token is null) return Conflict(TokenUnreadable);

        var channels = await provider.ListChannelsAsync(token, ct);
        if (channels.Refusal is not null) return StatusCode(StatusCodes.Status502BadGateway, channels.Refusal);

        return channels.Channels!.Select(c => new ChatChannelDto(c.Id, c.Name, c.IsPrivate, c.IsMember)).ToList();
    }

    /// <summary>Every room set up on the workspace, oldest first.</summary>
    [HttpGet("workspaces/{id:int}/rooms")]
    [RequireAdmin]
    public async Task<ActionResult<IReadOnlyList<ChatRoomDto>>> GetRooms(int id, CancellationToken ct)
    {
        if (!await db.ChatWorkspaces.AnyAsync(w => w.Id == id, ct)) return NotFound();

        var rooms = await db.ChatRooms.AsNoTracking().Where(r => r.WorkspaceId == id).OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToListAsync(ct);
        var chosen = await ChosenProjectsByRoomAsync(id, ct);

        return rooms.Select(r => Dto(r, chosen.GetValueOrDefault(r.Id, []))).ToList();
    }

    /// <summary>
    /// Sets a channel up as a room. A channel already a room on this workspace
    /// is a conflict; a chosen project that is not bound to the workspace is a
    /// bad request.
    /// </summary>
    [HttpPost("workspaces/{id:int}/rooms")]
    [RequireAdmin]
    public async Task<ActionResult<ChatRoomDto>> CreateRoom(int id, ChatRoomCreateRequest request, CancellationToken ct)
    {
        if (!await db.ChatWorkspaces.AnyAsync(w => w.Id == id, ct)) return NotFound();

        var channelId = request.ChannelId?.Trim();
        if (string.IsNullOrEmpty(channelId)) return BadRequest("a channel id is needed");

        if (await db.ChatRooms.AnyAsync(r => r.WorkspaceId == id && r.ExternalId == channelId, ct))
            return Conflict(ChannelAlreadyARoom);

        var wanted = request.AllProjects ? [] : (request.ProjectIds ?? []).Distinct().ToList();
        if (wanted.Count > 0)
        {
            var bound = await BoundProjectIdsAsync(id, ct);
            if (wanted.Any(p => !bound.Contains(p))) return BadRequest(UnknownProject);
        }

        var now = time.GetUtcNow();
        var room = new EfHatchChatRoom
        {
            WorkspaceId = id,
            ExternalId = channelId,
            Name = NameOf(request.ChannelName, channelId),
            Changelog = request.Changelog,
            Questions = request.Questions,
            Problems = request.Problems,
            UnownedProblems = request.UnownedProblems,
            AllProjects = request.AllProjects,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ChatRooms.Add(room);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two creates of one channel racing: the unique index let one in,
            // the same shape ConnectWorkspace guards against.
            db.Entry(room).State = EntityState.Detached;
            if (!await db.ChatRooms.AnyAsync(r => r.WorkspaceId == id && r.ExternalId == channelId, ct)) throw;

            return Conflict(ChannelAlreadyARoom);
        }

        if (wanted.Count > 0)
            db.ChatRoomProjects.AddRange(wanted.Select(p => new EfHatchChatRoomProject { RoomId = room.Id, WorkspaceId = id, ProjectId = p }));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Chat room {RoomId} ({ChannelId}) created in workspace {WorkspaceId} by {Actor}",
            room.Id, channelId, id, await caller.ActorNameAsync(ct));

        return Dto(room, wanted);
    }

    /// <summary>
    /// Changes a room's switches, its <c>allProjects</c> flag, or its chosen
    /// projects - a field left out of the request leaves it as it was.
    /// </summary>
    [HttpPatch("rooms/{id:int}")]
    [RequireAdmin]
    public async Task<ActionResult<ChatRoomDto>> PatchRoom(int id, ChatRoomPatchRequest request, CancellationToken ct)
    {
        var room = await db.ChatRooms.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (room is null) return NotFound();

        if (request.ProjectIds is { } projectIds)
        {
            var wanted = projectIds.Distinct().ToList();
            if (wanted.Count > 0)
            {
                var bound = await BoundProjectIdsAsync(room.WorkspaceId, ct);
                if (wanted.Any(p => !bound.Contains(p))) return BadRequest(UnknownProject);
            }

            db.ChatRoomProjects.RemoveRange(await db.ChatRoomProjects.Where(p => p.RoomId == id).ToListAsync(ct));
            db.ChatRoomProjects.AddRange(wanted.Select(p => new EfHatchChatRoomProject { RoomId = id, WorkspaceId = room.WorkspaceId, ProjectId = p }));
        }

        if (request.Changelog is { } changelog) room.Changelog = changelog;
        if (request.Questions is { } questions) room.Questions = questions;
        if (request.Problems is { } problems) room.Problems = problems;
        if (request.UnownedProblems is { } unownedProblems) room.UnownedProblems = unownedProblems;
        if (request.AllProjects is { } allProjects) room.AllProjects = allProjects;

        // Switched back to every project: whatever was chosen means nothing now.
        if (room.AllProjects && request.ProjectIds is null)
            db.ChatRoomProjects.RemoveRange(await db.ChatRoomProjects.Where(p => p.RoomId == id).ToListAsync(ct));

        room.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Chat room {RoomId} updated by {Actor}", id, await caller.ActorNameAsync(ct));

        return Dto(room, room.AllProjects ? [] : await ChosenProjectIdsAsync(id, ct));
    }

    /// <summary>Removes a room. What it chose goes with it, in the database.</summary>
    [HttpDelete("rooms/{id:int}")]
    [RequireAdmin]
    public async Task<IActionResult> RemoveRoom(int id, CancellationToken ct)
    {
        var room = await db.ChatRooms.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (room is null) return NotFound();

        db.ChatRooms.Remove(room);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Chat room {RoomId} removed by {Actor}", id, await caller.ActorNameAsync(ct));

        return NoContent();
    }

    /// <summary>
    /// Sends one fixed, anonymous test message to a room's channel. A Slack
    /// refusal is answered, not thrown: a test is an answer about the room,
    /// never a server error.
    /// </summary>
    [HttpPost("rooms/{id:int}/test")]
    [RequireAdmin]
    public async Task<ActionResult<ChatTestResultDto>> PostTestMessage(int id, CancellationToken ct)
    {
        var room = await db.ChatRooms.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.ExternalId, Token = r.Workspace!.Token })
            .FirstOrDefaultAsync(ct);
        if (room is null) return NotFound();

        var token = SecretProtector.Unprotect(room.Token);
        if (token is null) return Conflict(TokenUnreadable);

        var result = await provider.PostMessageAsync(token, room.ExternalId, TestMessage, ct);

        logger.LogInformation("Chat room {RoomId} sent a test message by {Actor}: delivered={Delivered}",
            id, await caller.ActorNameAsync(ct), result.Posted);

        return new ChatTestResultDto(result.Posted, result.Refusal);
    }

    private const string AlreadyConnected = "that workspace is already connected; replace its token instead";
    private const string TokenUnreadable = "the token for this workspace cannot be read; replace it";
    private const string ChannelAlreadyARoom = "that channel is already a room here";
    private const string UnknownProject = "unknown project";

    private const string TestMessage =
        "This is a test message from Hatch. If you can read it, this channel is set up.";

    private Task<EfHatchChatWorkspace?> FindAsync(string externalId, CancellationToken ct) =>
        db.ChatWorkspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Provider == provider.Name && w.ExternalId == externalId, ct);

    /// <summary>An id the column cannot hold is the service's answer being unusable, not the caller's fault; said plainly rather than cut short, since a cut id would name another workspace.</summary>
    private static string? TooLong(ChatTokenCheck check) =>
        check.WorkspaceId!.Length > EfHatchChatWorkspace.MaxExternalIdLength
            ? "the service named a workspace this install cannot store"
            : null;

    private static string NameOf(ChatTokenCheck check)
    {
        var name = string.IsNullOrWhiteSpace(check.WorkspaceName) ? check.WorkspaceId! : check.WorkspaceName.Trim();

        return name.Length > EfHatchChatWorkspace.MaxNameLength ? name[..EfHatchChatWorkspace.MaxNameLength] : name;
    }

    /// <summary>A room's name, as last seen from the service, falling back to its channel id.</summary>
    private static string NameOf(string? channelName, string channelId)
    {
        var name = string.IsNullOrWhiteSpace(channelName) ? channelId : channelName.Trim();

        return name.Length > EfHatchChatRoom.MaxNameLength ? name[..EfHatchChatRoom.MaxNameLength] : name;
    }

    private async Task<HashSet<int>> VisibleProjectIdsAsync(CancellationToken ct) =>
        (await db.Projects.AsNoTracking().Select(p => p.Id).ToListAsync(ct)).ToHashSet();

    private async Task<List<int>> BoundProjectIdsAsync(int workspaceId, CancellationToken ct) =>
        (await db.ChatWorkspaceProjects.AsNoTracking().Where(p => p.WorkspaceId == workspaceId).Select(p => p.ProjectId).ToListAsync(ct))
            .Order().ToList();

    private async Task<Dictionary<int, List<int>>> BoundProjectsByWorkspaceAsync(CancellationToken ct) =>
        (await db.ChatWorkspaceProjects.AsNoTracking().ToListAsync(ct))
            .GroupBy(p => p.WorkspaceId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ProjectId).Order().ToList());

    private async Task<List<int>> ChosenProjectIdsAsync(int roomId, CancellationToken ct) =>
        (await db.ChatRoomProjects.AsNoTracking().Where(p => p.RoomId == roomId).Select(p => p.ProjectId).ToListAsync(ct))
            .Order().ToList();

    private async Task<Dictionary<int, List<int>>> ChosenProjectsByRoomAsync(int workspaceId, CancellationToken ct) =>
        (await db.ChatRoomProjects.AsNoTracking().Where(p => p.WorkspaceId == workspaceId).ToListAsync(ct))
            .GroupBy(p => p.RoomId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ProjectId).Order().ToList());

    private static ChatWorkspaceDto Dto(EfHatchChatWorkspace w, IReadOnlyList<int> projectIds) =>
        new(w.Id, w.Provider, w.ExternalId, w.Name, !string.IsNullOrEmpty(w.Token), w.CreatedAt, projectIds);

    private static ChatRoomDto Dto(EfHatchChatRoom r, IReadOnlyList<int> projectIds) =>
        new(r.Id, r.WorkspaceId, r.ExternalId, r.Name, r.Changelog, r.Questions, r.Problems, r.UnownedProblems, r.AllProjects,
            r.AllProjects ? [] : projectIds);
}

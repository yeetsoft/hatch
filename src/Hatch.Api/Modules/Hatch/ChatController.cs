using Hatch.Api.Common;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The chat workspaces Hatch is connected to, set by an admin: connect one,
/// list them, replace a token, remove one, and fetch the app manifest to
/// create the app from. Five routes, all admin and all person-only - a key
/// never reads or writes a workspace, since a bot token is a credential that
/// can speak for the install.
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

        return rows.Select(Dto).ToList();
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

        return Dto(row);
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

        return Dto(row);
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

    private const string AlreadyConnected = "that workspace is already connected; replace its token instead";

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

    private static ChatWorkspaceDto Dto(EfHatchChatWorkspace w) =>
        new(w.Id, w.Provider, w.ExternalId, w.Name, !string.IsNullOrEmpty(w.Token), w.CreatedAt);
}

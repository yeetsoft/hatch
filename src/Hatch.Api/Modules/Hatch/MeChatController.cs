using System.Security.Cryptography;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// A person's own Slack links: which Slack member in each workspace is theirs,
/// so Hatch may send that member a direct message. Every route acts on the
/// caller and takes no person id.
/// </summary>
/// <remarks>
/// <para>Its own controller with no class-level attribute, for the reason
/// <see cref="MeHomeController"/> gives: a link is a person's own choice, never
/// an agent's, so each action is <c>RequireRole(User)</c> with no key accepted,
/// and refuses a program in the action as well, since the role gate sleeps
/// where the wall is off.</para>
///
/// <para>A link waits until its person opens the link in the message Hatch sent
/// to the member they named, and that message is all that is ever sent to a
/// waiting link. The secret in the message is in no log line and is stored only
/// as its hash.</para>
/// </remarks>
[ApiController]
[Route("api/me/chat")]
public class MeChatController(
    HatchContext db,
    AppDbContext appDb,
    IChatProvider provider,
    ICallerIdentity caller,
    IActorDirectory directory,
    ICallerScope callerScope,
    SecretProtector protector,
    ChatLinks links,
    ChatLinkRoster roster,
    TimeProvider time,
    ILogger<MeChatController> logger) : ControllerBase
{
    internal const string NotAnAgent = "linking a Slack account is a person's own, not an agent's";
    internal const string NeedsSomebody = "linking a Slack account needs somebody signed in";
    internal const string NoPublicUrl =
        "a Slack link cannot be sent until HATCH_PUBLIC_URL is set, because the message must carry a link back to Hatch; set it and restart";
    internal const string AlreadyLinked = "this Slack account is already linked here; unlink it first";
    internal const string MemberTaken = "that Slack member is already linked to somebody else; an admin can remove that link";
    internal const string SecretGone = "that link was already used, was replaced, or ran out";
    internal const string SomebodyElses = "that link belongs to somebody else's request";
    internal const string NoSuchMember = "that is not a Slack member a message can be sent to";

    /// <summary>The caller's workspaces and where their link stands in each.</summary>
    [HttpGet]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<MeChatDto>> Get(CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;
        if (await directory.PrincipalAsync(ct) is not { } principal) return NeedsSomebodyResult();

        var mine = await db.ChatLinks.AsNoTracking().Where(l => l.PersonId == principal.Id).ToListAsync(ct);
        var visible = await VisibleWorkspacesAsync(mine, ct);

        var rows = visible.Select(w =>
        {
            var link = mine.FirstOrDefault(l => l.WorkspaceId == w.Id);

            return new MeChatWorkspaceDto(w.Id, w.Name, link is null ? null : Dto(link));
        }).ToList();

        return new MeChatDto(links.On, rows);
    }

    /// <summary>
    /// The Slack member whose email is one the caller has signed in with, newest
    /// sign-in first, stopping at the first match. The local person has no
    /// sign-in email, so is told none without asking Slack.
    /// </summary>
    [HttpGet("workspaces/{id:int}/suggestion")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<MeChatSuggestionDto>> GetSuggestion(int id, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;
        if (await directory.PrincipalAsync(ct) is not { } principal) return NeedsSomebodyResult();
        if (await VisibleWorkspaceAsync(id, principal.Id, ct) is not { } workspace) return NotFound();

        var emails = (await appDb.ExternalIdentities.AsNoTracking()
                .Where(i => i.PersonId == principal.Id && i.Subject != null)
                .OrderByDescending(i => i.LastSignInAt)
                .Select(i => i.Email)
                .ToListAsync(ct))
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (emails.Count == 0) return new MeChatSuggestionDto(null);

        var token = protector.Unprotect(workspace.Token, SecretStores.ChatWorkspace(workspace.Provider, workspace.ExternalId));
        if (token is null) return Conflict(ChatController.TokenUnreadable);

        foreach (var email in emails)
        {
            var found = await provider.FindMemberByEmailAsync(token.Reveal(), email, ct);
            if (found.Refusal is not null) return StatusCode(StatusCodes.Status502BadGateway, found.Refusal);
            if (found.Member is { } member) return new MeChatSuggestionDto(Dto(member));
        }

        return new MeChatSuggestionDto(null);
    }

    /// <summary>Every member of the workspace a message could go to: names, never emails.</summary>
    [HttpGet("workspaces/{id:int}/members")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<IReadOnlyList<MeChatMemberDto>>> GetMembers(int id, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;
        if (await directory.PrincipalAsync(ct) is not { } principal) return NeedsSomebodyResult();
        if (await VisibleWorkspaceAsync(id, principal.Id, ct) is not { } workspace) return NotFound();

        var token = protector.Unprotect(workspace.Token, SecretStores.ChatWorkspace(workspace.Provider, workspace.ExternalId));
        if (token is null) return Conflict(ChatController.TokenUnreadable);

        var members = await provider.ListMembersAsync(token.Reveal(), ct);
        if (members.Refusal is not null) return StatusCode(StatusCodes.Status502BadGateway, members.Refusal);

        return members.Members!.Select(Dto).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Sends the named member a direct message with a link to confirm, and keeps
    /// a waiting link. Nothing is stored unless the message was sent. A waiting
    /// link of the caller's is replaced; a confirmed one is not.
    /// </summary>
    [HttpPost("links")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<MeChatLinkDto>> PostLink([FromBody] MeChatLinkRequest body, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;
        if (await directory.PrincipalAsync(ct) is not { } principal) return NeedsSomebodyResult();

        var mine = await db.ChatLinks.Where(l => l.PersonId == principal.Id).ToListAsync(ct);
        if (body is null || await VisibleWorkspaceAsync(body.WorkspaceId, principal.Id, ct, mine) is not { } workspace) return NotFound();

        if (!links.On) return Conflict(NoPublicUrl);

        var memberId = body.MemberId?.Trim();
        if (string.IsNullOrEmpty(memberId) || memberId.Length > EfHatchChatLink.MaxMemberIdLength) return BadRequest("a Slack member is needed");

        var existing = mine.FirstOrDefault(l => l.WorkspaceId == workspace.Id);
        if (existing?.ConfirmedAt is not null) return Conflict(AlreadyLinked);

        if (await MemberHeldByAnotherAsync(workspace.Id, memberId, principal.Id, ct)) return Conflict(MemberTaken);

        var now = time.GetUtcNow();
        var sends = await db.ChatLinkSends.Where(s => s.PersonId == principal.Id && s.SentAt > now - EfHatchChatLinkSend.Window)
            .OrderBy(s => s.SentAt).Select(s => s.SentAt).ToListAsync(ct);
        if (sends.Count >= EfHatchChatLinkSend.Limit)
        {
            var wait = sends[0] + EfHatchChatLinkSend.Window - now;
            if (HttpContext is not null) Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString();

            return StatusCode(StatusCodes.Status429TooManyRequests,
                $"{EfHatchChatLinkSend.Limit} link messages were sent in the last hour; try again later");
        }

        var token = protector.Unprotect(workspace.Token, SecretStores.ChatWorkspace(workspace.Provider, workspace.ExternalId));
        if (token is null) return Conflict(ChatController.TokenUnreadable);

        // The member comes from the list, not the request: that gives the name
        // to store, and refuses a bot or an id nobody has.
        var members = await provider.ListMembersAsync(token.Reveal(), ct);
        if (members.Refusal is not null) return StatusCode(StatusCodes.Status502BadGateway, members.Refusal);
        if (members.Members!.FirstOrDefault(m => m.Id == memberId) is not { } member) return BadRequest(NoSuchMember);

        var opened = await provider.OpenDirectMessageAsync(token.Reveal(), member.Id, ct);
        if (opened.Refusal is not null) return StatusCode(StatusCodes.Status502BadGateway, opened.Refusal);
        if (opened.ConversationId is not { Length: > 0 and <= EfHatchChatLink.MaxConversationIdLength } conversationId)
            return StatusCode(StatusCodes.Status502BadGateway, "the chat service named a conversation this install cannot store");

        var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var content = new ChatMessageContent(
            "Link your Slack account to Hatch",
            $"{principal.Name} asked to link this Slack account to their Hatch account.",
            ["If that was not you, ignore this message. Nothing happens until the link is opened."],
            [new ChatLink("Confirm the link", links.LinkBack(secret))]);

        var sent = await provider.PostAsync(token.Reveal(), conversationId, content, ct);
        if (sent.Outcome != ChatSendOutcome.Sent)
            return StatusCode(StatusCodes.Status502BadGateway, sent.Sentence ?? "the chat service did not take the message");

        var name = member.Name.Length > EfHatchChatLink.MaxMemberNameLength ? member.Name[..EfHatchChatLink.MaxMemberNameLength] : member.Name;

        // Past this line the message is out. If the save fails, its secret is
        // dead: no row holds its hash, and the person presses Link again.
        var row = existing ?? new EfHatchChatLink
        {
            WorkspaceId = workspace.Id,
            PersonId = principal.Id,
            MemberId = member.Id,
            MemberName = name,
            ConversationId = conversationId,
        };
        row.MemberId = member.Id;
        row.MemberName = name;
        row.ConversationId = conversationId;
        row.CreatedAt = now;
        row.SecretHash = AuthTokens.Hash(secret);
        row.SecretExpiresAt = now + EfHatchChatLink.SecretLife;
        if (existing is null) db.ChatLinks.Add(row);

        db.ChatLinkSends.Add(new EfHatchChatLinkSend { PersonId = principal.Id, SentAt = now });
        db.ChatLinkSends.RemoveRange(await db.ChatLinkSends.Where(s => s.SentAt <= now - EfHatchChatLinkSend.Window).ToListAsync(ct));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A unique index let somebody else in first. Read afresh, and answer
            // what the index meant; anything else is a real failure.
            db.ChangeTracker.Clear();

            var standing = await db.ChatLinks.AsNoTracking().FirstOrDefaultAsync(l => l.WorkspaceId == workspace.Id && l.PersonId == principal.Id, ct);
            if (standing?.ConfirmedAt is not null) return Conflict(AlreadyLinked);
            if (await MemberHeldByAnotherAsync(workspace.Id, member.Id, principal.Id, ct)) return Conflict(MemberTaken);
            if (standing is not null && existing is null) return Conflict("another request for this workspace was made at the same time; try again");

            throw;
        }

        logger.LogInformation("Chat link {LinkId} in workspace {WorkspaceId} requested by {Actor}",
            row.Id, workspace.Id, await caller.ActorNameAsync(ct));

        return Dto(row);
    }

    /// <summary>
    /// Confirms the link whose secret is given. The secret travels in the body,
    /// never the URL, and nothing on a <c>GET</c> confirms anything. Another
    /// person's secret confirms nothing and the row is left as it was.
    /// </summary>
    [HttpPost("links/confirm")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<MeChatLinkDto>> ConfirmLink([FromBody] MeChatConfirmRequest body, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;
        if (await directory.PrincipalAsync(ct) is not { } principal) return NeedsSomebodyResult();

        if (string.IsNullOrWhiteSpace(body?.Secret)) return StatusCode(StatusCodes.Status410Gone, SecretGone);

        var hash = AuthTokens.Hash(body.Secret.Trim());
        var now = time.GetUtcNow();

        var row = await db.ChatLinks.FirstOrDefaultAsync(l => l.SecretHash == hash, ct);
        if (row is null || row.ConfirmedAt is not null || row.SecretExpiresAt is not { } expires || expires <= now)
            return StatusCode(StatusCodes.Status410Gone, SecretGone);

        if (row.PersonId != principal.Id) return StatusCode(StatusCodes.Status403Forbidden, SomebodyElses);

        // Before ConfirmedAt is set: a removed person's confirmed link on this
        // member would otherwise hit the unique index, and the catch below
        // would find nothing to blame. The row above is not modified yet, so
        // the prune's save carries nothing else.
        await roster.PruneAsync(row.WorkspaceId, ct);

        row.ConfirmedAt = now;
        row.SecretHash = null;
        row.SecretExpiresAt = null;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var memberId = row.MemberId;
            var workspaceId = row.WorkspaceId;
            db.ChangeTracker.Clear();
            if (await MemberHeldByAnotherAsync(workspaceId, memberId, principal.Id, ct)) return Conflict(MemberTaken);

            throw;
        }

        logger.LogInformation("Chat link {LinkId} in workspace {WorkspaceId} confirmed by {Actor}",
            row.Id, row.WorkspaceId, await caller.ActorNameAsync(ct));

        return Dto(row);
    }

    /// <summary>
    /// Removes the caller's own link, waiting or confirmed, even from a workspace
    /// that has left their scope. Another person's link is not found.
    /// </summary>
    [HttpDelete("links/{id:int}")]
    [RequireRole(PersonRole.User)]
    public async Task<IActionResult> DeleteLink(int id, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;
        if (await directory.PrincipalAsync(ct) is not { } principal) return NeedsSomebodyResult();

        var row = await db.ChatLinks.FirstOrDefaultAsync(l => l.Id == id && l.PersonId == principal.Id, ct);
        if (row is null) return NotFound();

        db.ChatLinks.Remove(row);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Chat link {LinkId} in workspace {WorkspaceId} removed by {Actor}",
            id, row.WorkspaceId, await caller.ActorNameAsync(ct));

        return NoContent();
    }

    // ---- Who may see what ----

    /// <summary>
    /// The workspaces the caller may see: all of them for a caller who sees every
    /// project, else those a project they can see is bound to, and any where they
    /// hold a link, so they can always unlink.
    /// </summary>
    private async Task<List<EfHatchChatWorkspace>> VisibleWorkspacesAsync(IReadOnlyList<EfHatchChatLink> mine, CancellationToken ct)
    {
        var all = await db.ChatWorkspaces.AsNoTracking().OrderBy(w => w.CreatedAt).ThenBy(w => w.Id).ToListAsync(ct);
        var visible = await callerScope.ResolveAsync(ct);
        if (visible.Unbounded) return all;

        // The binding's own query filter narrows this already; the Where says it again.
        var ids = visible.Ids;
        var bound = (await db.ChatWorkspaceProjects.AsNoTracking().Where(p => ids.Contains(p.ProjectId))
            .Select(p => p.WorkspaceId).Distinct().ToListAsync(ct)).ToHashSet();
        bound.UnionWith(mine.Select(l => l.WorkspaceId));

        return all.Where(w => bound.Contains(w.Id)).ToList();
    }

    private async Task<EfHatchChatWorkspace?> VisibleWorkspaceAsync(
        int id, Guid personId, CancellationToken ct, IReadOnlyList<EfHatchChatLink>? mine = null)
    {
        var workspace = await db.ChatWorkspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workspace is null) return null;
        var visible = await callerScope.ResolveAsync(ct);
        if (visible.Unbounded) return workspace;

        if (mine is not null ? mine.Any(l => l.WorkspaceId == id) : await db.ChatLinks.AnyAsync(l => l.PersonId == personId && l.WorkspaceId == id, ct))
            return workspace;

        var ids = visible.Ids;

        return await db.ChatWorkspaceProjects.AnyAsync(p => p.WorkspaceId == id && ids.Contains(p.ProjectId), ct) ? workspace : null;
    }

    /// <summary>
    /// Whether somebody else holds the member. A link of a person who has been
    /// removed holds nothing, so those are dropped first, and the check is made
    /// on what is left. Callers reach it before they track a change, or just
    /// after clearing the tracker, so the drop saves nothing but itself.
    /// </summary>
    private async Task<bool> MemberHeldByAnotherAsync(int workspaceId, string memberId, Guid personId, CancellationToken ct)
    {
        await roster.PruneAsync(workspaceId, ct);

        return await db.ChatLinks.AsNoTracking().Confirmed()
            .AnyAsync(l => l.WorkspaceId == workspaceId && l.MemberId == memberId && l.PersonId != personId, ct);
    }

    private static MeChatLinkDto Dto(EfHatchChatLink l) =>
        l.ConfirmedAt is null
            ? new MeChatLinkDto(l.Id, "waiting", l.MemberName, l.SecretExpiresAt)
            : new MeChatLinkDto(l.Id, "linked", l.MemberName, null);

    private static MeChatMemberDto Dto(ChatMember m) => new(m.Id, m.Name, m.DisplayName);

    private ObjectResult NeedsSomebodyResult() =>
        new(NeedsSomebody) { StatusCode = StatusCodes.Status403Forbidden };

    /// <summary>
    /// A person, not a key - checked in the action as well as declared in the
    /// attribute, for <see cref="MeHomeController"/>'s reason: the role gate is
    /// dormant wherever the wall is off, and a guarantee that evaporates under a
    /// switch is not a guarantee.
    /// </summary>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult(NotAnAgent) { StatusCode = StatusCodes.Status403Forbidden }
            : null;
}

using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The other end of a message: a session, or the runner spawning one, saying it
/// has put what was sent into a session's context.
///
/// A sibling of <see cref="IssueThreadController"/>, which writes the message,
/// because this is the one write in the thread that must be exactly-once and it
/// needs the claim rules the thread does not.
/// </summary>
/// <remarks>
/// <para>"Read" means put into the session's context. Nothing here can prove a
/// model acted on a message, and the page says only what is true: which runner
/// was handed it, and when.</para>
///
/// <para>The guarantee lives in the <c>WHERE</c>, as it does for the claim
/// (<see cref="IssueClaims"/>): each message is marked by one conditional
/// <c>UPDATE</c> that repeats "and still unread", and a row count of one is the
/// only proof this call delivered it. Two checks that race over the same
/// message therefore return it once between them. The event is a second
/// statement, for the reason the claim's are: the trail is worth a lost row less
/// than a transaction is worth here.</para>
/// </remarks>
[ApiController]
[Route("api/hatch/issues/{key}/messages")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class IssueMessagesController(
    HatchContext db, IssueClaims claims, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Marks messages read and answers with the ones this call marked - so an
    /// empty list is "nothing new", and never an error.
    /// </summary>
    [HttpPost("deliver")]
    public async Task<ActionResult<IReadOnlyList<CommentDto>>> Deliver(
        string key, MessageDeliverRequest? request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.AsNoTracking()
            .WithKey(projectKey, number)
            .Select(i => new
            {
                i.Id,
                Claim = new ClaimSnapshot(
                    i.ClaimToken, i.ClaimedBy, i.ClaimRunner,
                    i.ClaimedAt, i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt),
            })
            .FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var now = time.GetUtcNow();

        var candidates = Unread(db, issue.Id);
        if (request?.Ids is { } ids) candidates = candidates.Where(c => ids.Contains(c.Id));

        var ordered = await candidates.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (ordered.Count == 0) return new List<CommentDto>();

        // Whoever is holding the issue is who was handed the message. Where
        // nothing is - a runner marking what it is about to put in a prompt,
        // before it has taken the claim - it is the caller.
        var to = claims.IsLive(issue.Claim, now)
            ? issue.Claim.Runner ?? ""
            : await caller.ActorNameAsync(ct);
        if (to.Length > ClaimRequest.MaxRunnerLength) to = to[..ClaimRequest.MaxRunnerLength];

        var delivered = new List<long>(ordered.Count);
        foreach (var id in ordered)
        {
            var marked = await db.Comments
                .Where(c => c.Id == id && c.Kind == EfHatchComment.Message && c.DeliveredAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.DeliveredAt, (DateTimeOffset?)now)
                    .SetProperty(c => c.DeliveredTo, to), ct);

            if (marked == 1) delivered.Add(id);
        }

        if (delivered.Count == 0) return new List<CommentDto>();

        var actor = await caller.ActorNameAsync(ct);
        foreach (var id in delivered)
            db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id,
                Actor = actor,
                Kind = EfHatchIssueEvent.MessageDelivered,
                Payload = JsonSerializer.Serialize(new { commentId = id, to }),
                At = now,
            });
        await db.SaveChangesAsync(ct);

        var rows = await db.Comments.AsNoTracking()
            .Where(c => delivered.Contains(c.Id))
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    /// <summary>The messages on an issue that no session has read, oldest first.</summary>
    public static async Task<List<CommentDto>> UnreadAsync(HatchContext db, long issueId, CancellationToken ct)
    {
        var rows = await Unread(db, issueId)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    private static IQueryable<EfHatchComment> Unread(HatchContext db, long issueId) =>
        db.Comments.AsNoTracking()
            .Where(c => c.IssueId == issueId && c.Kind == EfHatchComment.Message && c.DeliveredAt == null);

    private static CommentDto ToDto(EfHatchComment c) =>
        new(c.Id, c.Author, c.Body, c.Kind, c.AnswersId, null, c.CreatedAt, c.DeliveredAt, c.DeliveredTo);
}

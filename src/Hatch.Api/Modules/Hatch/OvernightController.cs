using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What landed in the newest night: wires the database to
/// <see cref="Overnight.NewestNight"/>, which does all of the grouping,
/// gap-splitting and ordering.
/// </summary>
/// <remarks>
/// No class-level attribute - the same split <see cref="ProjectsController"/>
/// draws, for the same reason: <see cref="GetOvernight"/> is a read, open to
/// a person and to a <c>hatch</c>-scoped key alike the way
/// <see cref="WorkLogController"/>'s reads are, but <see cref="PostSeen"/> is
/// a person's own mark and nothing else's.
/// </remarks>
[ApiController]
[Route("api/hatch/overnight")]
public class OvernightController(
    HatchContext db, ICallerIdentity caller, IActorDirectory directory, TimeProvider time) : ControllerBase
{
    [HttpGet]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<OvernightDto>> GetOvernight(CancellationToken ct)
    {
        var principal = await directory.PrincipalAsync(ct);
        DateTimeOffset? seenAt = principal is null
            ? null
            : await db.OvernightSeen.AsNoTracking()
                .Where(s => s.PersonId == principal.Id)
                .Select(s => (DateTimeOffset?)s.SeenAt)
                .FirstOrDefaultAsync(ct);

        var terminal = await db.Issues.AsNoTracking()
            .Where(i => i.Status!.IsTerminal)
            .Select(i => new { i.Id, ProjectKey = i.Project!.Key, i.Number, i.Type, i.Title })
            .ToListAsync(ct);

        if (terminal.Count == 0) return Overnight.NewestNight([], seenAt);

        var terminalIds = terminal.Select(i => i.Id).ToHashSet();

        // One query over the marked issues' status_changed events, materialized,
        // then grouped in memory for the newest `At` per issue - the same shape
        // SendBackStreak.CountsAsync and LetGo.CountsAsync already use for "one
        // round trip, many issues, grouped", and not a server-side GroupBy+Max:
        // ChannelLatestValues.cs already flags GroupBy's Npgsql translation as
        // uncertain, which is why that file avoids GroupBy entirely, and no
        // other grouped-aggregate call in this codebase asks the database to do
        // the grouping either.
        var events = await db.IssueEvents.AsNoTracking()
            .Where(e => e.Kind == EfHatchIssueEvent.StatusChanged && terminalIds.Contains(e.IssueId))
            .Select(e => new { e.IssueId, e.At })
            .ToListAsync(ct);

        var landedAt = events
            .GroupBy(e => e.IssueId)
            .ToDictionary(g => g.Key, g => g.Max(e => e.At));

        var tree = await Rollup.LoadAsync(db, ct);

        var landings = terminal
            // Filing into a terminal column is already refused
            // (IssuesController.CreateIssueAsync), so every terminal issue has
            // passed through at least one status_changed - this should never
            // drop a row, but a current status with no matching event is
            // excluded rather than crashing on a missing key.
            .Where(i => landedAt.ContainsKey(i.Id))
            .Select(i => new Overnight.Landing(
                i.Id, IssueKey.Format(i.ProjectKey, i.Number), i.Type, i.Title, i.ProjectKey,
                landedAt[i.Id], tree.DescendantCount(i.Id)))
            .ToList();

        return Overnight.NewestNight(landings, seenAt);
    }

    /// <summary>
    /// Marks the night as read, for the caller - now, for whoever is asking.
    /// No body: a person only has one mark to set, their own.
    /// </summary>
    [HttpPost("seen")]
    [RequireRole(PersonRole.User)]
    public async Task<IActionResult> PostSeen(CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        if (await directory.PrincipalAsync(ct) is not { } principal)
            return new ObjectResult("marking the night as seen needs somebody signed in")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };

        var personId = principal.Id;
        var now = time.GetUtcNow();

        var row = await db.OvernightSeen.FirstOrDefaultAsync(s => s.PersonId == personId, ct);
        if (row is null) db.OvernightSeen.Add(new EfHatchOvernightSeen { PersonId = personId, SeenAt = now });
        else row.SeenAt = now;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- The one narrowing ----

    /// <summary>
    /// A person, not a key - checked in the action as well as declared in the
    /// attribute, for <see cref="RunnersController"/>'s reason:
    /// <see cref="RoleGate"/> is dormant wherever the wall is off, which is
    /// all of local development, and a guarantee that evaporates under a
    /// switch is not a guarantee. A keyless runner that named itself with the
    /// runner header is refused here too - it is an agent whether or not it
    /// carries a credential.
    /// </summary>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("marking the night as seen is the operator's, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}

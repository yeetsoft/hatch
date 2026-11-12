using Hatch.Api.Common;
using Hatch.Api.Ef;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The event trail read across issues rather than about one: what happened
/// last, anywhere.
///
/// A controller of its own rather than a third action on
/// <see cref="IssueThreadController"/>, which is routed at
/// <c>api/issues/{key}</c> and is about one issue - the same argument
/// <see cref="WorkLogController"/> makes for sitting apart from
/// <c>IssueWorkLogController</c>.
/// </summary>
/// <remarks>
/// No <c>ICallerIdentity</c>: this is a read, and reads in this module are
/// open to an administrator and to a <c>hatch</c> key alike.
/// </remarks>
[ApiController]
[Route("api/activity")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class ActivityController(HatchContext db) : ControllerBase
{
    /// <summary>
    /// The most rows one answer will list, past which the request is an
    /// export rather than an activity page.
    /// </summary>
    public const int MaxEvents = 500;

    /// <summary>What a caller who named no limit gets.</summary>
    private const int DefaultEvents = 100;

    /// <summary>
    /// The event trail across every project, newest first, a page at a time.
    /// </summary>
    /// <remarks>
    /// Ordered the same way <see cref="IssueThreadController.GetEvents"/>
    /// orders one issue's trail - <c>At</c> descending, then <c>Id</c>
    /// descending - so one event never sorts differently between the two
    /// reads.
    ///
    /// <paramref name="limit"/> plus one rows are read and <paramref
    /// name="limit"/> of them are answered, with <c>hasMore</c> set from
    /// whether the extra one came back. There is no <c>COUNT(*)</c>: the page
    /// says whether there is another, not how many there are.
    /// </remarks>
    /// <param name="limit">How many rows to list, 1 to <see cref="MaxEvents"/>.</param>
    /// <param name="offset">How many rows, newest first, to skip before the page starts.</param>
    /// <param name="projectId">
    /// Narrows to events whose issue is in any of these projects; repeat it to
    /// name several. None names every project. An id naming no project
    /// contributes nothing and is not a refusal. <paramref name="limit"/> and
    /// <paramref name="offset"/> page the narrowed rows.
    /// </param>
    [HttpGet]
    public async Task<ActionResult<ActivityPageDto>> GetActivity(
        [FromQuery] int limit = DefaultEvents, [FromQuery] int offset = 0,
        [FromQuery] List<int>? projectId = null, CancellationToken ct = default)
    {
        if (limit is < 1 or > MaxEvents)
            return BadRequest($"a limit is between 1 and {MaxEvents} - not {limit}");

        if (offset < 0)
            return BadRequest($"an offset is not negative - not {offset}");

        var ids = (projectId ?? []).Distinct().ToList();

        var narrowed = db.IssueEvents.AsNoTracking();
        if (ids.Count > 0)
            narrowed = narrowed.Where(e => ids.Contains(e.Issue!.ProjectId));

        var rows = await narrowed
            .OrderByDescending(e => e.At)
            .ThenByDescending(e => e.Id)
            .Skip(offset)
            .Take(limit + 1)
            .Select(e => new
            {
                e.Id,
                ProjectKey = e.Issue!.Project!.Key,
                e.Issue!.Number,
                e.Actor,
                e.Kind,
                e.Payload,
                e.At,
            })
            .ToListAsync(ct);

        var hasMore = rows.Count > limit;

        var events = rows.Take(limit).Select(r => new ActivityEventDto(
            r.Id, IssueKey.Format(r.ProjectKey, r.Number), r.Actor, r.Kind, IssueEventPayload.Parse(r.Payload), r.At))
            .ToList();

        return new ActivityPageDto(events, hasMore);
    }
}

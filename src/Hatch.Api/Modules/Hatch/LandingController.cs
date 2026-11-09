using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Where a session or a runner writes down what a repository holds for an
/// issue's branch - see <see cref="EfHatchLanding"/> for what a landing is and
/// why there is one per repository.
/// </summary>
/// <remarks>
/// <para>There is no <c>GET</c>. Landings ride <see cref="IssueDto"/>, which is
/// where every client needs them, the same reasoning
/// <see cref="MergeCheckController"/>'s own remark gives.</para>
///
/// <para>Nothing here looks at the issue's column, for the same reason
/// <see cref="MergeCheckController"/> does not: a landing is a fact about a
/// repository and a sha, true or not regardless of where the ticket sits, and
/// the rule for which column a write is read from belongs to whichever
/// controller reads it, not to a write that would be the same rule twice.</para>
/// </remarks>
[ApiController]
[Route("api/hatch/issues/{key}/landing")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class LandingController(
    HatchContext db, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Records what a repository holds for this issue's branch, and says what
    /// it now holds.
    /// </summary>
    /// <remarks>
    /// An event is written when the sha or what contains it differs from what
    /// was stored, and the first landing for a repository counts: the trail
    /// says when a branch first landed. A write that omits
    /// <see cref="LandingWriteRequest.ContainedBy"/> - a session's own report -
    /// leaves whatever a poll already found there rather than clearing it, so
    /// the two writers never race each other's half of the row. A write that
    /// repeats what is stored refreshes the remote, the trunk and the time and
    /// writes nothing, so a poll that looks every interval leaves one entry
    /// rather than a hundred.
    /// </remarks>
    [HttpPut]
    public async Task<ActionResult<LandingDto>> PutLanding(
        string key, LandingWriteRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var remote = request.Remote?.Trim() ?? "";
        var (canonical, error) = RemoteIdentity.Canonical(remote);
        if (canonical is null) return BadRequest(error);
        if (remote.Length > EfHatchMergeCheck.MaxRemoteLength || canonical.Length > EfHatchMergeCheck.MaxRemoteLength)
            return BadRequest($"a remote is at most {EfHatchMergeCheck.MaxRemoteLength} characters");

        var sha = request.Sha?.Trim() ?? "";
        if (sha.Length == 0) return BadRequest("a landing names the sha it landed");
        if (!sha.All(Uri.IsHexDigit)) return BadRequest("a sha is hex digits only");
        if (sha.Length > EfHatchMergeCheck.MaxShaLength)
            return BadRequest($"a sha is at most {EfHatchMergeCheck.MaxShaLength} characters");

        var trunk = request.Trunk?.Trim();
        if (trunk is { Length: > EfHatchMergeCheck.MaxRefLength })
            return BadRequest($"a trunk is at most {EfHatchMergeCheck.MaxRefLength} characters");

        var containedBy = request.ContainedBy?.Trim();
        if (containedBy is { Length: 0 }) containedBy = null;
        if (containedBy is not null && string.IsNullOrEmpty(trunk))
            return BadRequest("a landing that names what contains it must also name its trunk");
        if (containedBy is { Length: > EfHatchMergeCheck.MaxShaLength })
            return BadRequest($"a sha is at most {EfHatchMergeCheck.MaxShaLength} characters");

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        var row = await db.Landings.FirstOrDefaultAsync(l => l.IssueId == issue.Id && l.Canonical == canonical, ct);

        if (row is null && string.IsNullOrEmpty(trunk))
            return BadRequest("a landing names its trunk the first time it's recorded");

        // A plain report (no ContainedBy) must not blank out what a poll
        // already confirmed, so the effective value - and what gets diffed
        // against what was stored - is whatever the caller sent or, failing
        // that, whatever is already there.
        var effectiveContainedBy = containedBy ?? row?.ContainedBy;
        var from = row is null ? null : Side(row.Sha, row.ContainedBy);
        var changed = row is null || row.Sha != sha || row.ContainedBy != effectiveContainedBy;

        if (row is null)
        {
            row = new EfHatchLanding
            {
                IssueId = issue.Id,
                Remote = remote,
                Canonical = canonical,
                Trunk = trunk!,
                Sha = sha,
                RecordedAt = now,
                RecordedBy = actor,
            };
            db.Landings.Add(row);
        }

        row.Remote = remote;
        if (!string.IsNullOrEmpty(trunk)) row.Trunk = trunk;
        row.Sha = sha;
        row.ContainedBy = effectiveContainedBy;
        row.RecordedAt = now;
        row.RecordedBy = actor;

        // Set only here, and only by the write that is the poll's own
        // confirmation - nothing else in this ticket's scope ever touches it.
        if (containedBy is not null) row.CheckedAt = now;

        if (changed)
        {
            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = actor,
                Kind = EfHatchIssueEvent.LandingChanged,

                // The remote in its canonical form: two callers spell one
                // repository two ways, and the trail should not read as two.
                Payload = JsonSerializer.Serialize(new { remote = canonical, from, to = Side(sha, effectiveContainedBy) }),
                At = now,
            });
        }

        await db.SaveChangesAsync(ct);

        return IssueLandings.Project(row);
    }

    /// <summary>One side of a change: the sha, and what contains it. Spelled out rather than handed a record, as every payload in this module is - a record would serialise with capitals.</summary>
    private static object Side(string sha, string? containedBy) => new { sha, containedBy };
}

/// <summary>The stored landing, as a client reads it.</summary>
public static class IssueLandings
{
    public static LandingDto Project(EfHatchLanding l) => new(
        l.Remote, l.Canonical, l.Trunk, l.Sha, l.ContainedBy, l.RecordedAt, l.CheckedAt);
}

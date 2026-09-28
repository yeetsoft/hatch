using System.Globalization;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Which columns are work in progress, and how much of one slice of the board
/// may sit across them at once.
/// </summary>
/// <remarks>
/// Its own controller for the same reason <see cref="IssueExpediteController"/>
/// is, and cut the same way: no class-level attribute, so the write below
/// inherits no scope from anything. A key that could raise the limit or empty
/// the section could pull more of its own work in overnight - the dispatcher
/// stalls at a full WIP section, and a key that could widen the section it is
/// stalled behind would be an agent lifting its own ceiling.
///
/// <para>Reading is open to a key, like <see cref="RunnersController"/>'s read
/// is: an agent that is refused a dispatch because the section is full is
/// entitled to be told why. Writing carries the second check <see cref="NotAPerson"/>
/// makes, for <see cref="IssueClaimController.NotAPerson"/>'s reason:
/// <c>RoleGate</c> is dormant wherever the wall is off, and a keyless runner
/// there is a program too.</para>
/// </remarks>
[ApiController]
[Route("api/hatch/wip")]
public class WipController(HatchContext db, ICallerIdentity caller) : ControllerBase
{
    [HttpGet]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public async Task<ActionResult<WipSectionDto>> GetWip(CancellationToken ct) => await Wip.SectionAsync(db, ct);

    /// <summary>
    /// Set the limit, the section, or both - under the bulk rule every other
    /// clearable field in Hatch follows: a field left out is left alone.
    /// </summary>
    /// <remarks>
    /// Everything is validated before anything is touched, so a request naming
    /// a good limit and a bad column changes neither. <paramref name="request"/>'s
    /// <c>StatusIds</c>, when sent, is the whole section: every column in the
    /// house is set to <see cref="EfHatchStatus.IsWip"/> true if it is named and
    /// false otherwise, which is what lets a flag stranded on a column that has
    /// since become deferred or terminal be cleared by the next write that
    /// touches the section at all.
    /// </remarks>
    [HttpPut]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<WipSectionDto>> PutWip(WipSectionRequest request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        var clearingLimit = false;
        int? limit = null;
        if (request.Limit is not null)
        {
            var trimmed = request.Limit.Trim();
            if (trimmed.Length == 0)
            {
                clearingLimit = true;
            }
            else if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
            {
                return BadRequest($"a WIP limit is a whole number of one or more - not \"{trimmed}\"");
            }
            else
            {
                limit = parsed;
            }
        }

        var ids = request.StatusIds?.Distinct().ToList();
        if (ids is not null)
        {
            var named = await db.Statuses.Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
            foreach (var id in ids)
            {
                if (!named.TryGetValue(id, out var status)) return BadRequest($"there is no column {id}");
                if (status.IsDeferred) return BadRequest($"\"{status.Name}\" is deferred - parked work is never in progress");
                if (status.IsTerminal) return BadRequest($"\"{status.Name}\" is a done column - shipped work is never in progress");
            }
        }

        var changed = false;

        if (clearingLimit || limit is not null)
        {
            var row = await db.WipLimits.FirstOrDefaultAsync(w => w.Types == EfHatchWipLimit.StoriesAndBugs, ct);
            if (clearingLimit)
            {
                if (row is not null)
                {
                    db.WipLimits.Remove(row);
                    changed = true;
                }
            }
            else if (row is null)
            {
                db.WipLimits.Add(new EfHatchWipLimit { Types = EfHatchWipLimit.StoriesAndBugs, Limit = limit!.Value });
                changed = true;
            }
            else if (row.Limit != limit!.Value)
            {
                row.Limit = limit.Value;
                changed = true;
            }
        }

        if (ids is not null)
        {
            var wanted = ids.ToHashSet();
            foreach (var status in await db.Statuses.ToListAsync(ct))
            {
                var isWip = wanted.Contains(status.Id);
                if (status.IsWip != isWip)
                {
                    status.IsWip = isWip;
                    changed = true;
                }
            }
        }

        if (changed) await db.SaveChangesAsync(ct);

        return await Wip.SectionAsync(db, ct);
    }

    // ---- The one narrowing ----

    /// <summary>
    /// A person, not a key. Which columns are work in progress, and how much of
    /// it, is the operator's to set - not an agent's, for
    /// <see cref="RunnersController.NotAPerson"/>'s reason: a key that could
    /// raise its own ceiling could dispatch through it.
    /// </summary>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("which columns are work in progress, and how much of it, is the operator's to set - not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}

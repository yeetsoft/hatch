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

        var (clearingLimit, limit, limitError) = ParseLimit(request.Limit, "a WIP limit");
        if (limitError is not null) return BadRequest(limitError);
        var (clearingEpicLimit, epicLimit, epicLimitError) = ParseLimit(request.EpicLimit, "an epic limit");
        if (epicLimitError is not null) return BadRequest(epicLimitError);

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

        if (await ApplyLimitAsync(EfHatchWipLimit.StoriesAndBugs, clearingLimit, limit, ct)) changed = true;
        if (await ApplyLimitAsync(EfHatchWipLimit.Epics, clearingEpicLimit, epicLimit, ct)) changed = true;

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

    /// <summary>
    /// The string rule every clearable WIP limit follows: absent leaves it
    /// alone, blank clears it, a whole number of one or more sets it, anything
    /// else is refused with <paramref name="noun"/> naming which limit. Parsed
    /// before anything is touched, so a good field beside a bad one changes
    /// neither.
    /// </summary>
    private static (bool Clearing, int? Limit, string? Error) ParseLimit(string? field, string noun)
    {
        if (field is null) return (false, null, null);

        var trimmed = field.Trim();
        if (trimmed.Length == 0) return (true, null, null);

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1
            ? (false, parsed, null)
            : (false, null, $"{noun} is a whole number of one or more - not \"{trimmed}\"");
    }

    private async Task<bool> ApplyLimitAsync(string types, bool clearing, int? limit, CancellationToken ct)
    {
        if (!clearing && limit is null) return false;

        var row = await db.WipLimits.FirstOrDefaultAsync(w => w.Types == types, ct);
        if (clearing)
        {
            if (row is null) return false;
            db.WipLimits.Remove(row);
            return true;
        }

        if (row is null)
        {
            db.WipLimits.Add(new EfHatchWipLimit { Types = types, Limit = limit!.Value });
            return true;
        }

        if (row.Limit == limit!.Value) return false;
        row.Limit = limit.Value;
        return true;
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

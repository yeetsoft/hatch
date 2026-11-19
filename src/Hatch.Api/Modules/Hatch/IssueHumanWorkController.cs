using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// A person's override of who works an issue - its own say, over what it
/// would otherwise inherit. See <see cref="EfHatchIssue.HumanWork"/> and
/// <c>PriorityTree.EffectiveHumanWork</c>.
/// </summary>
[ApiController]
[Route("api/issues")]
public class IssueHumanWorkController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Set the override by name - see <see cref="HumanWorkValues"/> and
    /// <see cref="HumanWorkRequest"/>.
    /// </summary>
    /// <remarks>
    /// <para><see cref="NotAPerson"/> first and unconditionally, so a key, or a
    /// keyless runner where the wall is off, is refused before the issue is
    /// looked up.</para>
    ///
    /// <para>Setting the value the issue already holds writes nothing and does
    /// not move <c>UpdatedAt</c>, the same as every other edit in Hatch.</para>
    /// </remarks>
    [HttpPut("{key}/human-work")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<IssueDto>> PutIssueHumanWork(string key, HumanWorkRequest request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        if (!HumanWorkValues.TryParse(request.Value, out var value))
            return BadRequest(UnknownValue(request.Value));

        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        if (value != issue.HumanWork)
        {
            var now = time.GetUtcNow();

            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = await caller.ActorNameAsync(ct),
                Kind = EfHatchIssueEvent.HumanWorkChanged,
                Payload = JsonSerializer.Serialize(new
                {
                    from = HumanWorkValues.Name(issue.HumanWork),
                    to = HumanWorkValues.Name(value),
                }),
                At = now,
            });

            issue.HumanWork = value;
            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
    }

    /// <summary>
    /// The 400's sentence for a value that is none of the three.
    /// </summary>
    internal static string UnknownValue(string? value) =>
        $"\"{value}\" is not a human-work setting - {HumanWorkValues.Human}, {HumanWorkValues.Agent} or {HumanWorkValues.Inherit}";

    /// <summary>
    /// A person, not a key - setting an issue aside for a person is a person's
    /// own call.
    /// </summary>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("marking an issue as human work is a person's call, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}

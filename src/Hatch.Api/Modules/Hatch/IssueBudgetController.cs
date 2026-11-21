using System.Globalization;
using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What an ambition may cost - see <see cref="EfHatchIssue.BudgetUsd"/> - and
/// how much of it is spent. Stopping work at the budget is a separate
/// mechanism; this route is the setting and the reading.
/// </summary>
/// <remarks>
/// Its own controller for <see cref="IssueWipLimitController"/>'s reason:
/// <see cref="IssuesController"/> accepts a key on every route it holds, so a
/// field on <c>IssuePatchRequest</c> would be an agent that can raise its own
/// ceiling. The budget and the spend come from this read and not from
/// <see cref="IssueDto"/>: spent needs a walk of the whole tree, and the issue
/// is read once for every card on the board.
///
/// Reading is open to a key, since an agent is entitled to know what its work
/// is allowed to cost.
/// </remarks>
[ApiController]
[Route("api/issues")]
public class IssueBudgetController(
    HatchContext db, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    /// <summary>The budget, what is spent, and whether it is spent.</summary>
    [HttpGet("{key}/budget")]
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<ActionResult<IssueBudgetDto>> GetIssueBudget(string key, CancellationToken ct)
    {
        var (issue, refusal) = await FindAmbitionAsync(key, ct);
        if (refusal is not null) return refusal;

        return await AmbitionBudget.ReadAsync(db, issue!, ct);
    }

    /// <summary>
    /// Set, change or clear the budget. Null leaves it alone and <c>""</c> - or
    /// whitespace, which trims to it - clears it to no budget.
    /// </summary>
    [HttpPatch("{key}/budget")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<IssueBudgetDto>> PatchIssueBudget(
        string key, IssueBudgetRequest request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } person) return person;

        var (issue, refusal) = await FindAmbitionAsync(key, ct);
        if (refusal is not null) return refusal;

        if (request.Amount is not null)
        {
            var trimmed = request.Amount.Trim();
            decimal? budget;
            if (trimmed.Length == 0)
            {
                budget = null;
            }
            else if (decimal.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
                     && parsed > 0
                     && parsed <= AmbitionBudget.MaxBudgetUsd
                     && decimal.Round(parsed, 2) == parsed)
            {
                budget = parsed;
            }
            else
            {
                return BadRequest($"a budget is a positive number of dollars, to the cent - not \"{trimmed}\"");
            }

            if (budget != issue!.BudgetUsd)
            {
                var actor = await caller.ActorNameAsync(ct);
                issue.Events.Add(new EfHatchIssueEvent
                {
                    Actor = actor,
                    Kind = EfHatchIssueEvent.BudgetChanged,
                    Payload = JsonSerializer.Serialize(new { from = issue.BudgetUsd, to = budget }),
                    At = time.GetUtcNow(),
                });

                issue.BudgetUsd = budget;
                issue.UpdatedAt = time.GetUtcNow();
                await db.SaveChangesAsync(ct);
            }
        }

        return await AmbitionBudget.ReadAsync(db, issue!, ct);
    }

    /// <summary>
    /// The issue, or why not: 404 for a key that is unknown or hidden from the
    /// caller, and 400 naming the type for any issue that is not an ambition.
    /// </summary>
    private async Task<(EfHatchIssue? Issue, ActionResult? Refusal)> FindAmbitionAsync(string key, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return (null, NotFound());

        var issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return (null, NotFound());

        if (issue.Type != "ambition")
            return (null, BadRequest($"only an ambition takes a budget - {key} is a {issue.Type}"));

        return (issue, null);
    }

    /// <summary>
    /// A person, not a key. Checked in the action as well as declared in the
    /// attribute, for <see cref="WipController.NotAPerson"/>'s reason:
    /// <c>RoleGate</c> is dormant wherever the wall is off, and a keyless runner
    /// there is a program too.
    /// </summary>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("an ambition's budget is a person's to set - not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}

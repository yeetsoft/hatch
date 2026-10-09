using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// One of six levels on an issue, set by a person and honoured by both
/// halves of Hatch: the board floats the card to the top of its column, and
/// the dispatcher considers every issue at a higher level before anything at
/// a lower one - see <see cref="PriorityLevels"/>.
///
/// It is a sort key and not a gate. Every fold an issue already meets still
/// folds it - see <see cref="EfHatchIssue.Priority"/>, which is where that
/// argument is made and where the second thing it is not is written down.
/// </summary>
/// <remarks>
/// <para>Its own controller for the same reason
/// <see cref="AssigneeController"/> and <see cref="IssuePlaybookController"/>
/// are, and cut the same way: no class-level attribute, so neither write below
/// inherits scope from anything. <see cref="IssuesController"/> accepts the
/// <c>hatch</c> scope on every route it holds, so one more field on
/// <c>IssuePatchRequest</c> would have made this a key's write - and priority
/// decides what the loop reaches for first, so a key that could set one could
/// put its own ticket at the front of every night (docs/hatch.md, "The one edge
/// that is deliberately cut"). It is cut in the route rather than asked for in
/// a prompt, because a rule an agent is merely told is a rule an agent can
/// reason its way past.</para>
///
/// <para>Reading is open, like everything else a dispatch needs: an agent is
/// entitled to know why it was sent where it was sent, and both
/// <see cref="IssueDto"/> and <see cref="IssueCardDto"/> carry it.</para>
///
/// <para>There is no <c>hatch priority</c> at a terminal for the same reason:
/// the CLI authenticates with a key. The terminal shows the level on
/// <c>board</c>, <c>queue</c> and <c>show</c>, and sets it nowhere.</para>
///
/// <para><c>PUT .../expedite</c> stays, as a two-level alias:
/// <c>{"expedited":true}</c> sets <see cref="PriorityLevels.Expedited"/> and
/// <c>false</c> sets <see cref="PriorityLevels.Normal"/>, so an older browser
/// tab or script does not break. Both routes write the one field and the one
/// trail.</para>
/// </remarks>
[ApiController]
[Route("api/hatch/issues")]
public class IssueExpediteController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Set the level, by name - see <see cref="PriorityLevels"/> and
    /// <see cref="PriorityRequest"/>.
    /// </summary>
    [HttpPut("{key}/priority")]
    [RequireRole(PersonRole.User)]
    public Task<ActionResult<IssueDto>> PutIssuePriority(string key, PriorityRequest request, CancellationToken ct) =>
        PriorityLevels.TryParse(request.Priority, out var level)
            ? SetAsync(key, level, ct)
            : Task.FromResult<ActionResult<IssueDto>>(
                BadRequest($"\"{request.Priority}\" is not a priority - paused, economy, low, normal, expedited or emergency"));

    /// <summary>
    /// Mark it expedited, or unmark it. The legacy two-level alias for
    /// <see cref="PutIssuePriority"/> - see <see cref="ExpediteRequest"/>.
    /// </summary>
    [HttpPut("{key}/expedite")]
    [RequireRole(PersonRole.User)]
    public Task<ActionResult<IssueDto>> PutIssueExpedite(string key, ExpediteRequest request, CancellationToken ct) =>
        SetAsync(key, request.Expedited ? PriorityLevels.Expedited : PriorityLevels.Normal, ct);

    /// <summary>
    /// Write the level if it differs from what the issue already holds, and
    /// leave a trail entry naming both sides. Shared by both routes above, so
    /// there is one place that decides what changing this means.
    /// </summary>
    /// <remarks>
    /// Setting the value the issue already holds writes nothing and does not
    /// move UpdatedAt, the same as every other edit in Hatch - so a control
    /// pressed twice on a card two people are looking at leaves one trail
    /// entry and not three.
    /// </remarks>
    private async Task<ActionResult<IssueDto>> SetAsync(string key, int level, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        if (level != issue.Priority)
        {
            var now = time.GetUtcNow();

            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = await caller.ActorNameAsync(ct),
                Kind = EfHatchIssueEvent.PriorityChanged,

                // Both sides by name, so the trail says which way it went
                // rather than only that somebody touched it. Spelled out
                // rather than handed a record, for the reason every other
                // payload in this module is - see AssigneeController.Side.
                Payload = JsonSerializer.Serialize(new
                {
                    from = PriorityLevels.Name(issue.Priority),
                    to = PriorityLevels.Name(level),
                }),
                At = now,
            });

            issue.Priority = level;
            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
    }
}

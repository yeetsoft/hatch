using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The night train's flag. Set by a person, honoured by the dispatcher: an
/// express issue standing in a column marked <see cref="EfHatchStatus.ExpressSkips"/>
/// is carried to the next column with no session, as long as it has no
/// unanswered question - see <see cref="EfHatchIssue.Express"/>.
///
/// It is a gate-passer and not a sort key, the opposite of
/// <see cref="EfHatchIssue.Priority"/>: every fold an issue already meets
/// still folds it, and this changes only whether a column marked for it needs
/// a session at all.
/// </summary>
/// <remarks>
/// <para>Its own controller for the reason <see cref="IssueExpediteController"/>
/// is, and cut the same way: no class-level attribute, so the write below
/// inherits no scope from anything. Express decides which gates the loop may
/// pass unattended, and that is a playbook's kind of power - a key that could
/// set it could carry its own ticket through the night unattended
/// (docs/hatch.md, "The one edge that is deliberately cut").</para>
///
/// <para>Reading is open, like everything else a dispatch needs: both
/// <see cref="IssueDto"/> and <see cref="IssueCardDto"/> carry the flag.</para>
/// </remarks>
[ApiController]
[Route("api/hatch/issues")]
public class IssueExpressController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Mark it, or unmark it. The body says which, rather than the route
    /// meaning "the other one" - see <see cref="ExpressRequest"/>.
    /// </summary>
    /// <remarks>
    /// Nothing else on the issue is touched, and nothing else touches this: a
    /// move, a retitle and a reparent all leave the flag exactly as it was
    /// set, because it is written here and nowhere else - except at filing,
    /// where it is inherited from an express parent.
    /// </remarks>
    [HttpPut("{key}/express")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<IssueDto>> PutIssueExpress(
        string key, ExpressRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        // Setting the value the issue already holds writes nothing and does not
        // move UpdatedAt, the same as every other edit in Hatch.
        if (request.Express != issue.Express)
        {
            var now = time.GetUtcNow();

            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = await caller.ActorNameAsync(ct),
                Kind = EfHatchIssueEvent.ExpressChanged,
                Payload = JsonSerializer.Serialize(new { from = issue.Express, to = request.Express }),
                At = now,
            });

            issue.Express = request.Express;
            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, time.GetUtcNow(), ct);
    }
}

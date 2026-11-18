using Hatch.Api.Common;
using Hatch.Api.Ef;
using Microsoft.AspNetCore.Mvc;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Every waiting question in the house, as a tree of the issues that enclose
/// them - the same questions <see cref="QuestionsController"/> lists flat, and
/// by the same rule for what is waiting.
/// </summary>
[ApiController]
[Route("api/ballot")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class BallotController(HatchContext db) : ControllerBase
{
    /// <param name="projectId">
    /// Repeatable, as <c>/api/plan</c> takes it: narrows the roots to the
    /// questions asked in the projects named. Absent is every project.
    /// </param>
    [HttpGet]
    public async Task<ActionResult<BallotDto>> GetBallot([FromQuery] List<int>? projectId, CancellationToken ct) =>
        await Ballot.BuildAsync(db, (projectId ?? []).Distinct().ToList(), ct);
}

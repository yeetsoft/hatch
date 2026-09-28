using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Whether an issue's branch still merges with the trunk, as a runner found it
/// with git and reports it - see <see cref="EfHatchMergeCheck"/>.
/// </summary>
/// <remarks>
/// <para>A key may write one, which is the point: the caller is a runner
/// reporting a fact about two shas, and a fact only a person could enter would
/// be one nobody enters. It is not the same kind of write as a playbook or an
/// assignee - it names no budget and reserves no work. What it does do is decide
/// whether an issue in review is dispatched at all, and that rule is the
/// server's (<see cref="WorkController"/>), not the runner's: the runner says
/// what git said, and the board decides what it means.</para>
///
/// <para>Nothing here looks at the issue's column. A verdict taken at the end of
/// an implementation increment arrives before anybody has moved the ticket, and
/// refusing it for standing in the wrong column would lose exactly the fact the
/// next pass wants.</para>
///
/// <para>A verdict that repeats the stored one writes no event. A runner asks
/// every interval, and a trail that recorded every answer would bury the two
/// lines it exists for: when a branch started conflicting, and when it
/// stopped.</para>
/// </remarks>
[ApiController]
[Route("api/hatch/issues/{key}/merge-check")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public partial class IssueMergeCheckController(
    HatchContext db, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FullSha();

    /// <summary>
    /// Records the verdict for this issue in this repository, replacing the one
    /// there, and writes an event only when it says something different.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<MergeCheckDto>> PutMergeCheck(
        string key, MergeCheckRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var (canonical, error) = RemoteIdentity.Canonical(request.Remote ?? "");
        if (canonical is null) return BadRequest(error);

        if (!MergeVerdicts.All.Contains(request.Verdict))
            return BadRequest($"a verdict is one of {string.Join(", ", MergeVerdicts.All)} - not \"{request.Verdict}\"");

        var trunk = request.Trunk?.Trim() ?? "";
        if (trunk.Length == 0) return BadRequest("a verdict names the trunk it was taken against");
        if (trunk.Length > EfHatchMergeCheck.MaxRefLength)
            return BadRequest($"a trunk is at most {EfHatchMergeCheck.MaxRefLength} characters");

        if (request.TrunkSha is null || !FullSha().IsMatch(request.TrunkSha))
            return BadRequest("a verdict names the trunk's full 40-character sha");

        var about = request.Verdict is MergeVerdicts.Clean or MergeVerdicts.Conflicted;

        var branch = request.Branch?.Trim();
        var branchSha = request.BranchSha?.Trim();
        var files = (request.Files ?? []).Select(f => f.Trim()).Where(f => f.Length > 0).Distinct().Order(StringComparer.Ordinal).ToList();

        if (about)
        {
            if (string.IsNullOrEmpty(branch)) return BadRequest($"a {request.Verdict} verdict names its branch");
            if (branch.Length > EfHatchMergeCheck.MaxRefLength)
                return BadRequest($"a branch is at most {EfHatchMergeCheck.MaxRefLength} characters");
            if (branchSha is null || !FullSha().IsMatch(branchSha))
                return BadRequest($"a {request.Verdict} verdict names its branch's full 40-character sha");
        }

        if (request.Verdict == MergeVerdicts.Conflicted && files.Count == 0)
            return BadRequest("a conflicted verdict names the files that conflict");

        var runner = request.Runner?.Trim();
        if (runner is { Length: > ClaimRequest.MaxRunnerLength })
            return BadRequest($"a runner is at most {ClaimRequest.MaxRunnerLength} characters");

        // What none and ambiguous do not have is dropped rather than refused: a
        // runner that names a branch for a verdict that is not about one has not
        // said anything untrue, but storing it would say something the row does
        // not mean.
        if (!about) (branch, branchSha) = (null, null);
        if (request.Verdict != MergeVerdicts.Conflicted) files = [];

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        var row = await db.MergeChecks.FirstOrDefaultAsync(m => m.IssueId == issue.Id && m.Canonical == canonical, ct);
        var before = row is null ? null : Side(row.Verdict, Split(row.Files));

        if (row is null)
        {
            row = new EfHatchMergeCheck
            {
                IssueId = issue.Id,
                Remote = request.Remote!.Trim(),
                Canonical = canonical,
                Trunk = trunk,
                TrunkSha = request.TrunkSha,
                Verdict = request.Verdict,
                CheckedAt = now,
                CheckedBy = actor,
            };
            db.MergeChecks.Add(row);
        }

        var after = Side(request.Verdict, files);
        var changed = before is null || before.Verdict != after.Verdict || !before.Files.SequenceEqual(after.Files);

        // Everything a repeat is still allowed to move, moved either way: the
        // poll compares these shas with origin's, and a stale one would have it
        // fetch again for nothing.
        row.Remote = request.Remote!.Trim();
        row.Trunk = trunk;
        row.TrunkSha = request.TrunkSha.ToLowerInvariant();
        row.Verdict = request.Verdict;
        row.Branch = branch;
        row.BranchSha = branchSha?.ToLowerInvariant();
        row.Files = files.Count == 0 ? null : string.Join('\n', files);
        row.CheckedAt = now;
        row.Runner = string.IsNullOrEmpty(runner) ? null : runner;
        row.CheckedBy = actor;

        if (changed)
        {
            db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id,
                Actor = actor,
                Kind = EfHatchIssueEvent.MergeCheckChanged,

                // Spelled out rather than handed a record, for the reason every
                // other payload in this module is - see AssigneeController.Side.
                Payload = JsonSerializer.Serialize(new { remote = canonical, from = before, to = after }),
                At = now,
            });
        }

        await db.SaveChangesAsync(ct);

        return Project(row);
    }

    /// <summary>One side of the event: the verdict, and the files it named.</summary>
    private sealed record Sided(
        [property: JsonPropertyName("verdict")] string Verdict,
        [property: JsonPropertyName("files")] IReadOnlyList<string> Files);

    private static Sided Side(string verdict, IReadOnlyList<string> files) => new(verdict, files);

    private static IReadOnlyList<string> Split(string? files) =>
        files?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [];

    /// <summary>The wire form of a stored verdict.</summary>
    public static MergeCheckDto Project(EfHatchMergeCheck m) => new(
        m.Remote, m.Canonical, m.Trunk, m.TrunkSha, m.Verdict, m.Branch, m.BranchSha,
        Split(m.Files), m.CheckedAt, m.Runner, m.CheckedBy);
}

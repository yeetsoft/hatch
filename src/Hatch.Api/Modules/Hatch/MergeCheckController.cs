using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Where a runner writes down whether an issue's branch merges with the trunk -
/// see <see cref="EfHatchMergeCheck"/> for what a verdict is and why there is
/// one per repository.
///
/// A key may write one, which is the whole point: the caller is a runner, and a
/// verdict only a person could enter would be one nobody ever entered. It
/// narrows nothing an agent could not already do to itself - the verdict is a
/// fact about two refs on origin that any runner computes the same way, and a
/// wrong one costs a stall, which is a question.
/// </summary>
/// <remarks>
/// <para>There is no <c>GET</c>. The verdicts ride <see cref="IssueDto"/>, which
/// is where every client needs them, and a second route serving the same list
/// would be a second thing to keep in step - the call
/// <see cref="IssueClaimController"/> makes about the claim.</para>
///
/// <para>Nothing here looks at the issue's column. A verdict taken at the end
/// of an implementation increment arrives before anybody has moved the ticket,
/// and the rule that only an issue in review is <em>dispatched</em> on one
/// belongs to <see cref="WorkController"/>, not to a write that would be the
/// same rule twice.</para>
/// </remarks>
[ApiController]
[Route("api/hatch/issues/{key}/merge-check")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class MergeCheckController(
    HatchContext db, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    /// <summary>The most conflicted paths one verdict will carry. Past this a merge is not one an agent resolves, and the column is not the place for the list.</summary>
    public const int MaxFiles = 500;

    /// <summary>
    /// Replaces the verdict for this issue and repository, and says what it now
    /// holds.
    /// </summary>
    /// <remarks>
    /// An event is written when the verdict or its files differ from what was
    /// stored, and the first verdict for a repository counts: the trail says
    /// when a branch started conflicting, and "was never checked" is a state
    /// too. A verdict that repeats the stored one refreshes the shas, the time
    /// and the runner and writes nothing, so a poll that looks every interval
    /// leaves one entry rather than a hundred.
    /// </remarks>
    [HttpPut]
    public async Task<ActionResult<MergeCheckDto>> PutMergeCheck(
        string key, MergeCheckRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var remote = request.Remote?.Trim() ?? "";
        var (canonical, error) = RemoteIdentity.Canonical(remote);
        if (canonical is null) return BadRequest(error);
        if (remote.Length > EfHatchMergeCheck.MaxRemoteLength || canonical.Length > EfHatchMergeCheck.MaxRemoteLength)
            return BadRequest($"a remote is at most {EfHatchMergeCheck.MaxRemoteLength} characters");

        var verdict = request.Verdict?.Trim() ?? "";
        if (!MergeVerdicts.All.Contains(verdict))
            return BadRequest($"a verdict is one of {string.Join(", ", MergeVerdicts.All)}");

        var trunk = request.Trunk?.Trim() ?? "";
        if (trunk.Length == 0) return BadRequest("a verdict names the trunk it was taken against");
        if (trunk.Length > EfHatchMergeCheck.MaxRefLength)
            return BadRequest($"a trunk is at most {EfHatchMergeCheck.MaxRefLength} characters");

        var trunkSha = request.TrunkSha?.Trim() ?? "";
        if (trunkSha.Length == 0) return BadRequest("a verdict names the sha the trunk stood at");
        if (trunkSha.Length > EfHatchMergeCheck.MaxShaLength)
            return BadRequest($"a sha is at most {EfHatchMergeCheck.MaxShaLength} characters");

        // No one branch to name for these two, and none is kept: see the
        // entity's remarks on why the poll wants that.
        var hasBranch = verdict is MergeVerdicts.Clean or MergeVerdicts.Conflicted;
        var branch = hasBranch ? request.Branch?.Trim() : null;
        var branchSha = hasBranch ? request.BranchSha?.Trim() : null;
        var holdsTrunk = hasBranch ? request.HoldsTrunk : null;
        if (hasBranch && string.IsNullOrEmpty(branchSha))
            return BadRequest($"a {verdict} verdict names the sha the branch stood at");
        if (branch is { Length: > EfHatchMergeCheck.MaxRefLength })
            return BadRequest($"a branch is at most {EfHatchMergeCheck.MaxRefLength} characters");
        if (branchSha is { Length: > EfHatchMergeCheck.MaxShaLength })
            return BadRequest($"a sha is at most {EfHatchMergeCheck.MaxShaLength} characters");

        // Sorted, so that the same conflict listed in another order by another
        // runner's git is the same verdict and not a change.
        var files = verdict == MergeVerdicts.Conflicted
            ? (request.Files ?? [])
                .Select(f => f?.Trim() ?? "")
                .Where(f => f.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];
        if (verdict == MergeVerdicts.Conflicted && files.Count == 0)
            return BadRequest("a conflicted verdict names the files that conflict");
        if (files.Count > MaxFiles)
            return BadRequest($"a verdict names at most {MaxFiles} files");
        if (files.Any(f => f.Contains('\n')))
            return BadRequest("a file name has no line break in it");

        var runner = request.Runner?.Trim() ?? "";
        if (runner.Length == 0) return BadRequest("a verdict names the runner that took it");
        if (runner.Length > ClaimRequest.MaxRunnerLength)
            return BadRequest($"a runner is at most {ClaimRequest.MaxRunnerLength} characters");

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        var row = await db.MergeChecks.FirstOrDefaultAsync(m => m.IssueId == issue.Id && m.Canonical == canonical, ct);

        var changed = row is null || row.Verdict != verdict || !EfHatchMergeCheck.SplitFiles(row.Files).SequenceEqual(files);
        var from = row is null ? null : Side(row.Verdict, EfHatchMergeCheck.SplitFiles(row.Files));

        if (row is null)
        {
            row = new EfHatchMergeCheck
            {
                IssueId = issue.Id,
                Remote = remote,
                Canonical = canonical,
                Trunk = trunk,
                TrunkSha = trunkSha,
                Verdict = verdict,
                CheckedAt = now,
                Runner = runner,
                CheckedBy = actor,
            };
            db.MergeChecks.Add(row);
        }

        row.Remote = remote;
        row.Trunk = trunk;
        row.TrunkSha = trunkSha;
        row.Branch = branch;
        row.BranchSha = branchSha;
        row.HoldsTrunk = holdsTrunk;
        row.Verdict = verdict;
        row.Files = EfHatchMergeCheck.JoinFiles(files);
        row.CheckedAt = now;
        row.Runner = runner;
        row.CheckedBy = actor;

        if (changed)
        {
            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = actor,
                Kind = EfHatchIssueEvent.MergeCheckChanged,

                // The remote in its canonical form: two runners spell one
                // repository two ways, and the trail should not read as two.
                Payload = JsonSerializer.Serialize(new { remote = canonical, from, to = Side(verdict, files) }),
                At = now,
            });
        }

        await db.SaveChangesAsync(ct);

        return IssueMergeChecks.Project(row);
    }

    /// <summary>One side of a change: the verdict, and the files it named. Spelled out rather than handed a record, as every payload in this module is - a record would serialise with capitals.</summary>
    private static object Side(string verdict, IReadOnlyList<string> files) => new { verdict, files };
}

/// <summary>The stored verdict, as a client reads it.</summary>
public static class IssueMergeChecks
{
    public static MergeCheckDto Project(EfHatchMergeCheck m) => new(
        m.Remote, m.Canonical, m.Trunk, m.TrunkSha, m.Verdict, m.Branch, m.BranchSha,
        EfHatchMergeCheck.SplitFiles(m.Files), m.HoldsTrunk, m.CheckedAt, m.Runner, m.CheckedBy);
}

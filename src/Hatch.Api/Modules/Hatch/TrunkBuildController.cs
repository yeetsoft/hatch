using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What the build on the tip of a repository's trunk came to - see
/// <see cref="EfHatchTrunkBuild"/> for what a verdict is and why there is one
/// per repository per trunk.
///
/// A key may write one, for the reason it may write a branch's build check: the
/// caller is a runner, and a verdict only a person could enter would be one
/// nobody ever entered. Reading is open for the same reason the runner needs it
/// back: the poll asks for every stored verdict once a poll, to avoid reading a
/// trunk that has already concluded.
/// </summary>
[ApiController]
[Route("api/hatch/trunk-builds")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class TrunkBuildController(HatchContext db, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    /// <summary>Every trunk verdict the board holds, ordered by canonical then trunk - what the poll reads once a poll.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TrunkBuildDto>>> GetTrunkBuilds(CancellationToken ct)
    {
        var rows = await db.TrunkBuilds.AsNoTracking()
            .Include(t => t.BugIssue).ThenInclude(i => i!.Project)
            .OrderBy(t => t.Canonical).ThenBy(t => t.Trunk)
            .ToListAsync(ct);

        return rows.Select(TrunkBuildProjection.Project).ToList();
    }

    /// <summary>
    /// Replaces the verdict for this repository's trunk, and says what it now
    /// holds. Takes the same validation <see cref="BuildCheckController.PutBuildCheck"/>
    /// does: canonicalisation, lengths, failing names required on <c>failed</c>,
    /// and a link stored only when it is absolute <c>http(s)</c>.
    /// </summary>
    /// <remarks>
    /// No event: there is no issue to write one on. And no question - a failing
    /// trunk is a person's row in the attention panel (HA-95), not a stall a
    /// session can be asked about. The one rule that is this route's own:
    /// <c>passed</c> lets the attached bug go, so the next failure offers the
    /// button again.
    /// </remarks>
    [HttpPut]
    public async Task<ActionResult<TrunkBuildDto>> PutTrunkBuild(TrunkBuildRequest request, CancellationToken ct)
    {
        var remote = request.Remote?.Trim() ?? "";
        var (canonical, error) = RemoteIdentity.Canonical(remote);
        if (canonical is null) return BadRequest(error);
        if (remote.Length > EfHatchTrunkBuild.MaxRemoteLength || canonical.Length > EfHatchTrunkBuild.MaxRemoteLength)
            return BadRequest($"a remote is at most {EfHatchTrunkBuild.MaxRemoteLength} characters");

        var verdict = request.Verdict?.Trim() ?? "";
        if (!BuildVerdicts.All.Contains(verdict))
            return BadRequest($"a verdict is one of {string.Join(", ", BuildVerdicts.All)}");

        var trunk = request.Trunk?.Trim() ?? "";
        if (trunk.Length == 0) return BadRequest("a verdict names the trunk it is about");
        if (trunk.Length > EfHatchTrunkBuild.MaxRefLength)
            return BadRequest($"a trunk is at most {EfHatchTrunkBuild.MaxRefLength} characters");

        var sha = request.Sha?.Trim() ?? "";
        if (sha.Length == 0) return BadRequest("a verdict names the sha it is about");
        if (sha.Length > EfHatchTrunkBuild.MaxShaLength)
            return BadRequest($"a sha is at most {EfHatchTrunkBuild.MaxShaLength} characters");

        // Failing checks on a verdict that is not failed or pending are ignored
        // rather than refused - the same rule the branch's build check takes.
        List<FailingCheckDto> failing = [];
        if (verdict is BuildVerdicts.Failed or BuildVerdicts.Pending)
        {
            var named = request.Failing ?? [];
            if (named.Count > EfHatchTrunkBuild.MaxFailing)
                return BadRequest($"a verdict names at most {EfHatchTrunkBuild.MaxFailing} failing checks");
            if (named.Any(f => f is null))
                return BadRequest("a failed verdict names the checks that failed");
            if (verdict == BuildVerdicts.Failed && named.Count == 0)
                return BadRequest("a failed verdict names the checks that failed");
            if (named.Any(f => (f.Name?.Trim() ?? "").Length is 0 or > EfHatchTrunkBuild.MaxCheckNameLength
                    || f.Name!.Contains('\n') || f.Name.Contains('\r')))
                return BadRequest($"a check's name is 1 to {EfHatchTrunkBuild.MaxCheckNameLength} characters with no line break in it");
            if (named.Any(f => f.Url is { Length: > EfHatchTrunkBuild.MaxCheckUrlLength }))
                return BadRequest($"a check's url is at most {EfHatchTrunkBuild.MaxCheckUrlLength} characters");

            failing = Failing(named);
        }

        var runner = request.Runner?.Trim() ?? "";
        if (runner.Length == 0) return BadRequest("a verdict names the runner that took it");
        if (runner.Length > ClaimRequest.MaxRunnerLength)
            return BadRequest($"a runner is at most {ClaimRequest.MaxRunnerLength} characters");

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        var row = await db.TrunkBuilds.Include(t => t.BugIssue).ThenInclude(i => i!.Project)
            .FirstOrDefaultAsync(t => t.Canonical == canonical && t.Trunk == trunk, ct);

        var sameSha = row is not null && row.Sha == sha;

        if (row is null)
        {
            row = new EfHatchTrunkBuild
            {
                Remote = remote,
                Canonical = canonical,
                Trunk = trunk,
                Sha = sha,
                ShaSince = now,
                Verdict = verdict,
                CheckedAt = now,
                Runner = runner,
                CheckedBy = actor,
            };
            db.TrunkBuilds.Add(row);
        }

        if (!sameSha) row.ShaSince = now;

        row.Remote = remote;
        row.Sha = sha;
        row.Verdict = verdict;
        row.Failing = EfHatchBuildCheck.WriteFailing(failing);
        row.CheckedAt = now;
        row.Runner = runner;
        row.CheckedBy = actor;

        // The outage is over: the next failure offers the button again.
        if (verdict == BuildVerdicts.Passed)
        {
            row.BugIssueId = null;
            row.BugIssue = null;
        }

        await db.SaveChangesAsync(ct);

        return TrunkBuildProjection.Project(row);
    }

    /// <summary>The names, trimmed, deduplicated and sorted, each with its link only where it is a web address a page may draw.</summary>
    private static List<FailingCheckDto> Failing(IReadOnlyList<FailingCheckDto>? failing) =>
        (failing ?? [])
            .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Name))
            .Select(f => new FailingCheckDto(f.Name.Trim(), SafeUrl(f.Url)))
            .GroupBy(f => f.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// A key writes these and the panel draws them as links, so a value that is
    /// not an absolute http or https address is stored as nothing rather than as
    /// something a browser would run.
    /// </summary>
    private static string? SafeUrl(string? url)
    {
        var trimmed = url?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > EfHatchTrunkBuild.MaxCheckUrlLength) return null;

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
               && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? trimmed
            : null;
    }
}

/// <summary>The stored trunk verdict, as a client reads it.</summary>
public static class TrunkBuildProjection
{
    public static TrunkBuildDto Project(EfHatchTrunkBuild t) => new(
        t.Id, t.Remote, t.Canonical, t.Trunk, t.Sha, t.ShaSince, t.Verdict,
        EfHatchBuildCheck.ReadFailing(t.Failing), t.CheckedAt, t.Runner, t.CheckedBy,
        t.BugIssue is null ? null : IssueKey.Format(t.BugIssue.Project!.Key, t.BugIssue.Number));
}

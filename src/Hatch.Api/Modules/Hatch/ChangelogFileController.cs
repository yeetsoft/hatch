using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The stored <see cref="EfHatchChangelogFile"/> rows, one per repository's
/// trunk, read back for the Projects page - see <see cref="ChangelogFileState"/>
/// for how a row's state is judged, and <see cref="ChangelogTasks.FileForGapAsync"/>
/// for what a gap in it does.
/// </summary>
[ApiController]
[Route("api/changelog-files")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class ChangelogFileController(
    HatchContext db, ICallerIdentity caller, TimeProvider time, IOptions<HatchOptions> hatchOptions,
    RankService ranks, ILogger<ChangelogFileController> log) : ControllerBase
{
    /// <summary>
    /// Every stored changelog-file verdict, ordered by canonical then trunk, and
    /// the canonicals some project currently elects - what the page reads once a
    /// load, and what a runner reads to know which checkouts to read. A
    /// <c>remote</c> query value per checkout the runner holds is answered in
    /// <see cref="ChangelogFilesDto.ElectedRemotes"/>, since a runner canonicalises nothing.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ChangelogFilesDto>> GetChangelogFiles([FromQuery] List<string>? remote, CancellationToken ct)
    {
        var rows = await db.ChangelogFiles.AsNoTracking()
            .OrderBy(f => f.Canonical).ThenBy(f => f.Trunk)
            .ToListAsync(ct);

        var elected = await db.ProjectRepositories.AsNoTracking()
            .Where(r => r.Changelog)
            .Select(r => r.Canonical).Distinct().OrderBy(c => c)
            .ToListAsync(ct);

        var electedRemotes = (remote ?? [])
            .Where(r => RemoteIdentity.Canonical(r).Canonical is { } c && elected.Contains(c))
            .Distinct()
            .ToList();

        return new ChangelogFilesDto(ChangelogFile.Path, elected, rows.Select(ToDto).ToList(), electedRemotes);
    }

    private static ChangelogFileDto ToDto(EfHatchChangelogFile f) => new(
        f.Id, f.Remote, f.Canonical, f.Trunk, f.Sha, f.State, f.Hash, f.Behind, f.Problem, f.Url, f.CheckedAt, f.Runner, f.CheckedBy);

    /// <summary>
    /// Replaces the stored row for this repository's trunk with the board's
    /// own judgement of the bytes the runner read at <see cref="ChangelogFile.Path"/>,
    /// and says what it now holds. Takes the validation the dependency file's
    /// put does, and trusts no verdict from the caller: the bytes are compared
    /// with the board's own projection here. Where the stored state is <c>behind</c>
    /// or <c>absent</c>, it also files the task to bring the file current,
    /// unless one is already open.
    /// </summary>
    /// <remarks>No event on a row: there is no issue to write one on. And no question - a foreign file is this row's own state, not a stall.</remarks>
    [HttpPut]
    public async Task<ActionResult<ChangelogFileDto>> PutChangelogFile(ChangelogFileRequest request, CancellationToken ct)
    {
        var remote = request.Remote?.Trim() ?? "";
        var (canonical, error) = RemoteIdentity.Canonical(remote);
        if (canonical is null) return BadRequest(error);
        if (remote.Length > EfHatchChangelogFile.MaxRemoteLength || canonical.Length > EfHatchChangelogFile.MaxRemoteLength)
            return BadRequest($"a remote is at most {EfHatchChangelogFile.MaxRemoteLength} characters");

        var trunk = request.Trunk?.Trim() ?? "";
        if (trunk.Length == 0) return BadRequest("a changelog file names the trunk it is about");
        if (trunk.Length > EfHatchChangelogFile.MaxRefLength)
            return BadRequest($"a trunk is at most {EfHatchChangelogFile.MaxRefLength} characters");

        var sha = request.Sha?.Trim() ?? "";
        if (sha.Length == 0) return BadRequest("a changelog file names the sha it is about");
        if (sha.Length > EfHatchChangelogFile.MaxShaLength)
            return BadRequest($"a sha is at most {EfHatchChangelogFile.MaxShaLength} characters");

        var runner = request.Runner?.Trim() ?? "";
        if (runner.Length == 0) return BadRequest("a changelog file names the runner that took it");
        if (runner.Length > ClaimRequest.MaxRunnerLength)
            return BadRequest($"a runner is at most {ClaimRequest.MaxRunnerLength} characters");

        byte[]? decoded = null;
        if (request.Content is not null)
        {
            try
            {
                decoded = Convert.FromBase64String(request.Content);
            }
            catch (FormatException)
            {
                return BadRequest("content is not valid base64");
            }

            if (decoded.Length > ChangelogFile.MaxFileBytes)
                return BadRequest($"a changelog file is at most {ChangelogFile.MaxFileBytes / (1024 * 1024)} MB");
        }

        var projection = await ChangelogFiles.ProjectAsync(db, canonical, ct);
        if (projection is null)
            return NotFound($"no project elects {canonical} for a changelog - turn it on from the Projects page");

        var verdict = ChangelogFileState.Judge(
            decoded, Changelog.RenderFile(projection, ChangelogFiles.Origin(hatchOptions.Value, Request)), sha);

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);

        var row = await db.ChangelogFiles.FirstOrDefaultAsync(f => f.Canonical == canonical && f.Trunk == trunk, ct);
        if (row is null)
        {
            row = new EfHatchChangelogFile
            {
                Remote = remote, Canonical = canonical, Trunk = trunk, Sha = sha,
                State = verdict.State, CheckedAt = now, Runner = runner, CheckedBy = actor,
            };
            db.ChangelogFiles.Add(row);
        }

        row.Remote = remote;
        row.Sha = sha;
        row.State = verdict.State;
        row.Hash = verdict.Hash;
        row.Behind = verdict.Behind;
        row.Problem = verdict.Problem;
        row.Url = SafeUrl(request.Url);
        row.CheckedAt = now;
        row.Runner = runner;
        row.CheckedBy = actor;

        await db.SaveChangesAsync(ct);

        if (verdict.State is ChangelogFile.BehindState or ChangelogFile.AbsentState)
            await ChangelogTasks.FileForGapAsync(db, ranks, time, canonical, projection, verdict.Lacking, ct, log);

        return ToDto(row);
    }

    // Copy of DependencyFileController.SafeUrl, bound to this entity's MaxUrlLength.
    private static string? SafeUrl(string? url)
    {
        var trimmed = url?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > EfHatchChangelogFile.MaxUrlLength) return null;

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
               && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? trimmed
            : null;
    }
}

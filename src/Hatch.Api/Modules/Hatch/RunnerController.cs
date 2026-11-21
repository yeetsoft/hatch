using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Where the runner comes from: the image that is already running.
///
/// A friend who has the stack up has one artifact and no .NET SDK, so the
/// binary they need is published from the same build as the API serving this
/// route (Dockerfile.api) and handed back from beside it. That is what makes
/// the revision below honest - it is the commit the image was built from, and
/// therefore the commit the downloaded `hatch` was built from too, rather than
/// two versions that happen to be near each other.
///
/// <c>AcceptKey = true</c>, matching <see cref="RunnersController"/> beside
/// it: a person at a browser setting a machine up may fetch both routes, and
/// so may a runner's own key, which is what hands the same sha256 on to
/// HA-2610's heartbeat reply. Wherever the wall is off - every local install -
/// the guard is dormant and this answers anyone, exactly like the rest of
/// the root.
/// </summary>
[ApiController]
[Route("api/runner")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class RunnerController(IHatchRevision revision, IRunnerBuilds builds) : ControllerBase
{
    /// <summary>
    /// What the Runner page draws, in one call.
    /// </summary>
    /// <remarks>
    /// The revision rides along rather than being fetched from
    /// <c>/api/hatch-revision</c>: it is a fact about the binaries listed
    /// beside it, and a page that asked twice could draw a download list from
    /// one build with a revision from another.
    ///
    /// Only what is actually on disk is listed. A `dotnet run` from a checkout
    /// has published nothing, so the list is empty and the page says there is
    /// nothing to download - which is true, and better than four links that
    /// 404.
    /// </remarks>
    [HttpGet]
    public async Task<RunnerDownloadsDto> Get()
    {
        var builtList = await builds.ListAsync();
        return new RunnerDownloadsDto(
            revision.Revision,
            builtList
                .Select(b => new RunnerDownloadDto(b.Rid, b.Platform, b.FileName, b.Url, b.Sha256))
                .ToArray());
    }

    /// <summary>
    /// One binary, as an attachment.
    /// </summary>
    /// <remarks>
    /// The RID is looked up against <see cref="IRunnerBuilds"/>'s own fixed
    /// list, and the path is built from what that lookup returned, so an
    /// unrecognised - or traversing - argument never reaches a file open at
    /// all. The check is the lookup, not a sanitiser.
    /// </remarks>
    [HttpGet("download/{rid}")]
    public async Task<ActionResult> Download(string rid)
    {
        var build = await builds.FindAsync(rid);
        if (build is null) return NotFound();

        // application/octet-stream with the file name: a browser that decided
        // for itself what `hatch` was would be a browser that rendered it.
        return PhysicalFile(build.Path, "application/octet-stream", build.FileName);
    }
}

/// <summary>
/// What the Runner page draws: which platforms this image can hand out, and
/// which commit all of them - and it - were built from.
/// </summary>
/// <remarks>
/// Not <c>RunnerDto</c>, which is one running loop on the board
/// (<see cref="RunnersController"/>). Everywhere else in Hatch a runner is a
/// process - the thing a claim names, the thing <c>HATCH_RUNNER</c> renames -
/// so the word belongs to that, and this is the downloads beside it.
/// </remarks>
/// <param name="Revision">The full sha stamped into this build, or "dev" for one nothing stamped.</param>
/// <param name="Downloads">In platform order, and only the ones present. Empty on a build that published none.</param>
public record RunnerDownloadsDto(string Revision, RunnerDownloadDto[] Downloads);

/// <summary>
/// One platform's binary.
/// </summary>
/// <param name="Rid">The .NET runtime identifier, which is also what the page's own detection answers.</param>
/// <param name="Platform">What to call it to a person.</param>
/// <param name="FileName">What it lands on disk as - `hatch.exe` on Windows, `hatch` everywhere else.</param>
/// <param name="Url">Where to get it. Named by the server so the page never builds a path of its own.</param>
/// <param name="Sha256">Lowercase hex, of the exact bytes <see cref="RunnerController.Download"/> serves for this RID.</param>
public record RunnerDownloadDto(string Rid, string Platform, string FileName, string Url, string Sha256);

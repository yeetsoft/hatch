using System.Security.Cryptography;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The stored <see cref="EfHatchDependencyFile"/> rows, one per repository's
/// trunk, read back for the Files-on-the-trunk page - see
/// <see cref="DependencyDeclaration"/> for what a stored row holds and how it
/// got there.
/// </summary>
[ApiController]
[Route("api/hatch/dependency-files")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class DependencyFileController(
    HatchContext db, ICallerIdentity caller, TimeProvider time, Dependencies? known = null) : ControllerBase
{
    /// <summary>Every stored dependency-file verdict, ordered by canonical then trunk - what the page reads once a load.</summary>
    [HttpGet]
    public async Task<ActionResult<DependencyFilesDto>> GetDependencyFiles(CancellationToken ct)
    {
        var rows = await db.DependencyFiles.AsNoTracking()
            .OrderBy(f => f.Canonical).ThenBy(f => f.Trunk)
            .ToListAsync(ct);

        return new DependencyFilesDto(DependencyDeclaration.Path, rows.Select(ToDto).ToList());
    }

    private DependencyFileDto ToDto(EfHatchDependencyFile f)
    {
        var dependencies = known ?? Dependencies.Default;

        return new(
            f.Id, f.Remote, f.Canonical, f.Trunk, f.Sha, f.State, f.Hash,
            EfHatchDependencyFile.ReadEntries(f.Entries)
                .Select(e => new DependencyFileEntryDto(e.Id, dependencies.Rows.First(r => r.Id == e.Id).Label, e.Version))
                .ToList(),
            f.Problem, f.LastGoodSha, f.LastGoodHash, f.Url, f.CheckedAt, f.Runner, f.CheckedBy);
    }

    /// <summary>
    /// Replaces the stored row for this repository's trunk with what the
    /// runner's poll found at <see cref="DependencyDeclaration.Path"/>, and
    /// says what it now holds. Takes the same validation
    /// <see cref="TrunkBuildController.PutTrunkBuild"/> does: canonicalisation
    /// and lengths, then hashes and parses the content itself rather than
    /// trusting a client-sent verdict.
    /// </summary>
    /// <remarks>No event: there is no issue to write one on. And no question - a bad dependencies file is this row's own state, not a stall.</remarks>
    [HttpPut]
    public async Task<ActionResult<DependencyFileDto>> PutDependencyFile(DependencyFileRequest request, CancellationToken ct)
    {
        var remote = request.Remote?.Trim() ?? "";
        var (canonical, error) = RemoteIdentity.Canonical(remote);
        if (canonical is null) return BadRequest(error);
        if (remote.Length > EfHatchDependencyFile.MaxRemoteLength || canonical.Length > EfHatchDependencyFile.MaxRemoteLength)
            return BadRequest($"a remote is at most {EfHatchDependencyFile.MaxRemoteLength} characters");

        var trunk = request.Trunk?.Trim() ?? "";
        if (trunk.Length == 0) return BadRequest("a dependency file names the trunk it is about");
        if (trunk.Length > EfHatchDependencyFile.MaxRefLength)
            return BadRequest($"a trunk is at most {EfHatchDependencyFile.MaxRefLength} characters");

        var sha = request.Sha?.Trim() ?? "";
        if (sha.Length == 0) return BadRequest("a dependency file names the sha it is about");
        if (sha.Length > EfHatchDependencyFile.MaxShaLength)
            return BadRequest($"a sha is at most {EfHatchDependencyFile.MaxShaLength} characters");

        var runner = request.Runner?.Trim() ?? "";
        if (runner.Length == 0) return BadRequest("a dependency file names the runner that took it");
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

            if (decoded.Length > DependencyDeclaration.MaxFileBytes)
                return BadRequest($"a dependency file is at most {DependencyDeclaration.MaxFileBytes / 1024} KB");
        }

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);
        var safeUrl = SafeUrl(request.Url);

        var row = await db.DependencyFiles.FirstOrDefaultAsync(f => f.Canonical == canonical && f.Trunk == trunk, ct);
        var previousSha = row?.Sha;
        var previousHash = row?.Hash;
        var previousWasParsed = row?.State == EfHatchDependencyFile.ParsedState;

        if (row is null)
        {
            row = new EfHatchDependencyFile
            {
                Remote = remote, Canonical = canonical, Trunk = trunk, Sha = sha,
                State = EfHatchDependencyFile.AbsentState, CheckedAt = now, Runner = runner, CheckedBy = actor,
            };
            db.DependencyFiles.Add(row);
        }

        if (decoded is null)
        {
            row.State = EfHatchDependencyFile.AbsentState;
            row.Hash = null;
            row.Entries = null;
            row.Problem = null;
        }
        else
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(decoded));
            var (entries, problem) = DependencyDeclaration.Parse(decoded, known);

            if (problem is null)
            {
                row.State = EfHatchDependencyFile.ParsedState;
                row.Hash = hash;
                row.Entries = EfHatchDependencyFile.WriteEntries(entries!);
                row.Problem = null;
                row.LastGoodSha = null;
                row.LastGoodHash = null;
            }
            else
            {
                row.State = EfHatchDependencyFile.InvalidState;
                row.Hash = hash;
                row.Entries = null;
                row.Problem = problem;
                if (previousWasParsed)
                {
                    row.LastGoodSha = previousSha;
                    row.LastGoodHash = previousHash;
                }
            }
        }

        row.Remote = remote;
        row.Sha = sha;
        row.Url = safeUrl;
        row.CheckedAt = now;
        row.Runner = runner;
        row.CheckedBy = actor;

        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    // Copy of TrunkBuildController.SafeUrl, bound to MaxUrlLength instead of MaxCheckUrlLength.
    private static string? SafeUrl(string? url)
    {
        var trimmed = url?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > EfHatchDependencyFile.MaxUrlLength) return null;

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
               && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? trimmed
            : null;
    }
}

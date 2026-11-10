using System.Collections.Immutable;
using System.Security.Cryptography;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The stored <see cref="EfHatchPlaybookFile"/> rows, one per repository's
/// trunk, read back for the Files-on-the-trunk page - see
/// <see cref="PlaybookFile"/> for what a stored row holds and how it got
/// there.
/// </summary>
[ApiController]
[Route("api/hatch/playbook-files")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class PlaybookFileController(
    HatchContext db, ICallerIdentity caller, TimeProvider time, ModelFamilies? families = null) : ControllerBase
{
    /// <summary>Every stored playbook-file verdict, ordered by canonical then trunk - what the page reads once a load.</summary>
    [HttpGet]
    public async Task<ActionResult<PlaybookFilesDto>> GetPlaybookFiles(CancellationToken ct)
    {
        var statuses = await db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct);
        var names = statuses.ToDictionary(s => s.Id, s => s.Name);

        var rows = await db.PlaybookFiles.AsNoTracking()
            .OrderBy(f => f.Canonical).ThenBy(f => f.Trunk)
            .ToListAsync(ct);

        return new PlaybookFilesDto(PlaybookFile.Path, rows.Select(f => ToDto(f, names, statuses)).ToList());
    }

    /// <summary>Full stored-row shape GET lists - mirrors TrunkBuildProjection.Project.</summary>
    private static PlaybookFileDto ToDto(
        EfHatchPlaybookFile f, IReadOnlyDictionary<int, string> names, List<EfHatchStatus> statuses) => new(
        f.Id, f.Remote, f.Canonical, f.Trunk, f.Sha, f.State, f.Hash,
        EfHatchPlaybookFile.ReadRows(f.Rows).Select(r => PlaybookFile.ToDto(r, names, statuses)).ToList(),
        f.Problem, f.LastGoodSha, f.LastGoodHash, f.Url, f.CheckedAt, f.Runner, f.CheckedBy);

    /// <summary>
    /// Replaces the stored row for this repository's trunk with what the
    /// runner's poll found at <see cref="PlaybookFile.Path"/>, and says what it
    /// now holds. Takes the same validation <see cref="TrunkBuildController.PutTrunkBuild"/>
    /// does: canonicalisation and lengths, then hashes and parses the content
    /// itself rather than trusting a client-sent verdict.
    /// </summary>
    /// <remarks>No event: there is no issue to write one on. And no question - a bad playbooks file is this row's own state, not a stall.</remarks>
    [HttpPut]
    public async Task<ActionResult<PlaybookFileDto>> PutPlaybookFile(PlaybookFileRequest request, CancellationToken ct)
    {
        var remote = request.Remote?.Trim() ?? "";
        var (canonical, error) = RemoteIdentity.Canonical(remote);
        if (canonical is null) return BadRequest(error);
        if (remote.Length > EfHatchPlaybookFile.MaxRemoteLength || canonical.Length > EfHatchPlaybookFile.MaxRemoteLength)
            return BadRequest($"a remote is at most {EfHatchPlaybookFile.MaxRemoteLength} characters");

        var trunk = request.Trunk?.Trim() ?? "";
        if (trunk.Length == 0) return BadRequest("a playbook file names the trunk it is about");
        if (trunk.Length > EfHatchPlaybookFile.MaxRefLength)
            return BadRequest($"a trunk is at most {EfHatchPlaybookFile.MaxRefLength} characters");

        var sha = request.Sha?.Trim() ?? "";
        if (sha.Length == 0) return BadRequest("a playbook file names the sha it is about");
        if (sha.Length > EfHatchPlaybookFile.MaxShaLength)
            return BadRequest($"a sha is at most {EfHatchPlaybookFile.MaxShaLength} characters");

        var runner = request.Runner?.Trim() ?? "";
        if (runner.Length == 0) return BadRequest("a playbook file names the runner that took it");
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

            if (decoded.Length > PlaybookFile.MaxFileBytes)
                return BadRequest($"a playbook file is at most {PlaybookFile.MaxFileBytes / 1024} KB");
        }

        var now = time.GetUtcNow();
        var actor = await caller.ActorNameAsync(ct);
        var safeUrl = SafeUrl(request.Url);

        var row = await db.PlaybookFiles.FirstOrDefaultAsync(f => f.Canonical == canonical && f.Trunk == trunk, ct);
        var previousSha = row?.Sha;
        var previousHash = row?.Hash;
        var previousWasParsed = row?.State == EfHatchPlaybookFile.ParsedState;

        if (row is null)
        {
            row = new EfHatchPlaybookFile
            {
                Remote = remote, Canonical = canonical, Trunk = trunk, Sha = sha,
                State = EfHatchPlaybookFile.AbsentState, CheckedAt = now, Runner = runner, CheckedBy = actor,
            };
            db.PlaybookFiles.Add(row);
        }

        IReadOnlyDictionary<int, string> names = ImmutableDictionary<int, string>.Empty;
        var statuses = new List<EfHatchStatus>();

        if (decoded is null)
        {
            row.State = EfHatchPlaybookFile.AbsentState;
            row.Hash = null;
            row.Rows = null;
            row.Problem = null;
        }
        else
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(decoded));
            statuses = await db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct);
            names = statuses.ToDictionary(s => s.Id, s => s.Name);
            var (parsedRows, problem) = PlaybookFile.Parse(decoded, statuses, families);

            if (problem is null)
            {
                row.State = EfHatchPlaybookFile.ParsedState;
                row.Hash = hash;
                row.Rows = EfHatchPlaybookFile.WriteRows(parsedRows!.Select(PlaybookFile.Resolve).ToList());
                row.Problem = null;
                row.LastGoodSha = null;
                row.LastGoodHash = null;
            }
            else
            {
                row.State = EfHatchPlaybookFile.InvalidState;
                row.Hash = hash;
                row.Rows = null;
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
        return ToDto(row, names, statuses);
    }

    // Copy of TrunkBuildController.SafeUrl, bound to MaxUrlLength instead of MaxCheckUrlLength.
    private static string? SafeUrl(string? url)
    {
        var trimmed = url?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > EfHatchPlaybookFile.MaxUrlLength) return null;

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
               && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? trimmed
            : null;
    }
}

using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The Backups card's API half: the folder's truth and the last attempt's,
/// and the one way a person can run a capped backup on demand.
/// </summary>
/// <remarks>
/// No class-level attribute - the same split <see cref="OvernightController"/>
/// draws, for the same reason: <see cref="GetBackup"/> is a read, open to a
/// person and to a <c>hatch</c>-scoped key alike, but <see cref="PostBackupRun"/>
/// is a person's own act and nothing else's.
/// </remarks>
[ApiController]
[Route("api/hatch/backup")]
public class BackupController(
    BackupService backup, BackupStatus status, IOptions<HatchOptions> options, ICallerIdentity caller)
    : ControllerBase
{
    [HttpGet]
    [RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
    public ActionResult<BackupStatusDto> GetBackup() =>
        Map(backup.Copies(), status.Last, options.Value.BackupPath, backup.KeepDays);

    /// <summary>
    /// Runs one backup attempt, capped at two minutes, and returns the same
    /// shape <see cref="GetBackup"/> does - a <c>failed</c> attempt is still a
    /// 200, with its sentence in <see cref="BackupStatusDto.LastError"/>.
    /// </summary>
    [HttpPost("run")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<BackupStatusDto>> PostBackupRun(CancellationToken ct)
    {
        if (await caller.IsProgramAsync(ct))
        {
            return new ObjectResult("running a backup is the operator's, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }

        if (string.IsNullOrEmpty(options.Value.BackupPath))
        {
            return BadRequest("backups are not configured");
        }

        var attempt = await backup.TryRunAsync(TimeSpan.FromMinutes(2), ct);
        if (attempt is null)
        {
            return Conflict("a backup is already running");
        }

        return Ok(Map(backup.Copies(), attempt, options.Value.BackupPath, backup.KeepDays));
    }

    private static BackupStatusDto Map(BackupCopies copies, BackupAttempt? last, string path, int keepDays)
    {
        var configured = !string.IsNullOrEmpty(path);

        return new BackupStatusDto(
            configured,
            configured ? path : null,
            keepDays,
            copies.Items.Count > 0 ? copies.Items[0].At : null,
            last?.At,
            last?.Outcome ?? "never",
            last?.Outcome == BackupOutcome.Failed ? last.Error : null,
            configured && copies.Problem is null,
            copies.Problem,
            copies.Items.Select(c => new BackupCopyDto(c.Name, c.Bytes, c.At)).ToList());
    }
}

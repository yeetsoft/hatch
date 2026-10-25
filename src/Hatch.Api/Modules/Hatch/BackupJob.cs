using Hatch.Api.Jobs;
using Quartz;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Takes the daily <c>pg_dump</c> copy - see <see cref="BackupService"/> for
/// what a run does and <see cref="BackupStatus"/> for where its outcome is
/// kept. A module's own job rather than a house job, so it sits in
/// <c>Modules/Hatch</c> beside <see cref="ReflectionCadenceJob"/>.
///
/// "On start, then daily" is not written here: <c>JobsInit</c> schedules every
/// <see cref="IAppJob"/> with <c>StartNow()</c> and <c>replace: true</c>, so
/// the first firing lands as the process comes up and the next
/// <see cref="Interval"/> later. Do not add a second trigger or edit
/// <c>JobsInit</c> for it.
///
/// Never throws, because Quartz re-fires a job that does:
/// <see cref="BackupService.RunAsync"/> records every failure as an attempt
/// instead. The job is rebuilt for each firing, so the singleton
/// <see cref="BackupStatus"/> is its memory of the last outcome - which is how
/// "backups are not configured" is said once per process rather than daily.
/// </summary>
public class BackupJob(BackupService backup, BackupStatus status, ILogger<BackupJob> logger) : IAppJob
{
    public string Name => "Backup";

    public string Group => "Hatch.Api";

    public TimeSpan Interval => TimeSpan.FromHours(24);

    public bool ServesTheHouse => false;

    public Task Execute(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        var previousOutcome = status.Last?.Outcome;

        var attempt = await backup.RunAsync(ct);

        switch (attempt.Outcome)
        {
            case BackupOutcome.Ok:
                logger.LogInformation("Backup {File} written ({Bytes} bytes)", attempt.File, attempt.Bytes);
                break;
            case BackupOutcome.Failed:
                logger.LogWarning("Backup failed: {Error}", attempt.Error);
                break;
            case BackupOutcome.NotConfigured when previousOutcome != BackupOutcome.NotConfigured:
                logger.LogInformation("backups are not configured");
                break;
        }
    }
}

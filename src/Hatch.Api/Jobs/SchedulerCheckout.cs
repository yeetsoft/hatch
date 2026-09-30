using Npgsql;
using Quartz;
using Quartz.Listener;

namespace Hatch.Api.Jobs;

/// <summary>
/// Deletes this scheduler's own <c>qrtz_scheduler_state</c> row once it has
/// shut down cleanly. Quartz's cluster recovery only notices a stopped node
/// once that row goes stale past the check-in interval, so without this a
/// clean stop still leaves a surviving node's jobs unclaimed until then.
///
/// <para>A scheduler listener rather than anything on the host's lifetime,
/// because <see cref="SchedulerShutdown"/> is the one moment that is exactly
/// right whoever stopped the scheduler: Quartz calls it after the job store has
/// stopped its cluster manager, so no check-in can write the row back, and only
/// on a shutdown that got that far. The host is not a reliable clock for it -
/// under WebApplicationFactory two StopAsync calls run at once, and whichever
/// loses the race to Shutdown() returns while the other is still stopping.</para>
///
/// <para>This is only ever this instance's own row because instanceId is
/// clusterwide-unique - except that HA-154 found quartz.scheduler.instanceId is
/// never configured here, so every replica carries the same default
/// ("NON_CLUSTERED") and every replica's check-in collapses onto one row. That
/// is a separate defect, flagged on HA-154 rather than folded in here.</para>
/// </summary>
public class SchedulerCheckout(IScheduler scheduler, string connectionString, ILogger<SchedulerCheckout> logger)
    : SchedulerListenerSupport
{
    public override async Task SchedulerShutdown(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                "DELETE FROM qrtz_scheduler_state WHERE sched_name = @schedName AND instance_name = @instanceId",
                connection);
            command.Parameters.AddWithValue("schedName", scheduler.SchedulerName);
            command.Parameters.AddWithValue("instanceId", scheduler.SchedulerInstanceId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Caught here rather than left to Quartz, which logs a listener
            // that throws at Error. A row left behind costs one check-in
            // interval of recovery - what every unclean stop costs anyway -
            // and is worth a Warning, not that.
            logger.LogWarning(ex, "Could not release this scheduler's cluster row; a surviving node will recover its jobs once the row goes stale.");
        }
    }
}

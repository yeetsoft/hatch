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
        // Quartz calls this with CancellationToken.None - SchedulerListenerSupport
        // supplies no token of its own - so nothing bounds the round trip below
        // unless this gives itself a deadline. Without one, a DELETE racing
        // Quartz's own clustered check-in (ClusterManager, writing this same row
        // on a recurring background loop for the scheduler's whole life, not only
        // at Start()) waits on Postgres's row lock indefinitely: server-side and
        // fully async, holding no CLR thread, which is exactly the hang HA-304
        // read off a stalled MinimalStartupTests run's thread dump - 17 OS threads,
        // all idle, none of them inside this call. CancelAfter rather than racing a
        // Task.Delay, because Npgsql's own cancellation plumbing is what actually
        // aborts the server-side wait rather than just abandoning the client-side one.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cts.Token);

            await using var command = new NpgsqlCommand(
                "DELETE FROM qrtz_scheduler_state WHERE sched_name = @schedName AND instance_name = @instanceId",
                connection);
            command.Parameters.AddWithValue("schedName", scheduler.SchedulerName);
            command.Parameters.AddWithValue("instanceId", scheduler.SchedulerInstanceId);
            await command.ExecuteNonQueryAsync(cts.Token);
        }
        catch (Exception ex)
        {
            // Caught here rather than left to Quartz, which logs a listener
            // that throws at Error. A row left behind costs one check-in
            // interval of recovery - what every unclean stop costs anyway -
            // and is worth a Warning, not that. A timed-out wait lands here
            // exactly like a dropped connection does, and costs the same.
            logger.LogWarning(ex, "Could not release this scheduler's cluster row; a surviving node will recover its jobs once the row goes stale.");
        }
    }
}

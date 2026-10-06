using Hatch.Api.Jobs;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Quartz;

namespace Hatch.Api.Tests.Jobs;

/// <summary>
/// A clean stop frees this node's cluster row, asserted against the store
/// rather than inferred from the absence of a warning.
///
/// <para>A real clustered scheduler against a real Postgres, configured the way
/// Program.cs configures the one that ships, because the row is written by
/// Quartz's own first check-in and nothing short of that writes it. Skippable
/// on the same variable as <see cref="HatchDatabase"/>, for the same reason.</para>
/// </summary>
[Collection(QuartzSchedulerCollection.Name)]
public class SchedulerCheckoutTests
{
    [SkippableFact]
    public async Task ACleanShutdown_DeletesThisSchedulersClusterRow()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var (_, quartz) = await MinimalDatabases.CreateAsync();

        var scheduler = await SchedulerBuilder.Create()
            .WithName($"SchedulerCheckoutTests-{Guid.NewGuid():N}")
            .UsePersistentStore(store =>
            {
                store.UseProperties = true;
                store.UseClustering();
                store.UsePostgres(quartz);
                store.UseSystemTextJsonSerializer();
            })
            .BuildScheduler();

        scheduler.ListenerManager.AddSchedulerListener(
            new SchedulerCheckout(scheduler, quartz, NullLogger<SchedulerCheckout>.Instance));

        // Awaited to the end, which is the point: the first check-in has
        // written the row by the time Start() returns. Asserting it is there is
        // what keeps the assertion after Shutdown() from passing vacuously.
        await scheduler.Start();
        Assert.Equal(1, await StateRowsAsync(quartz, scheduler.SchedulerName));

        await scheduler.Shutdown();
        Assert.Equal(0, await StateRowsAsync(quartz, scheduler.SchedulerName));
    }

    /// <summary>
    /// HA-304's regression case: proves the deadline at the mechanism a stalled
    /// MinimalStartupTests run's thread dump pointed at, rather than by rerunning
    /// that test and hoping. A second connection holds the row's own lock open
    /// exactly the way Quartz's ClusterManager check-in does, mid-transaction;
    /// without SchedulerShutdown's own deadline this hangs until the held lock is
    /// released, which this test never does until after its own assertion.
    /// </summary>
    [SkippableFact]
    public async Task AShutdownRacingAHeldRowLock_ReturnsRatherThanHanging()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var (_, quartz) = await MinimalDatabases.CreateAsync();

        var scheduler = await SchedulerBuilder.Create()
            .WithName($"SchedulerCheckoutTests-{Guid.NewGuid():N}")
            .UsePersistentStore(store =>
            {
                store.UseProperties = true;
                store.UseClustering();
                store.UsePostgres(quartz);
                store.UseSystemTextJsonSerializer();
            })
            .BuildScheduler();

        scheduler.ListenerManager.AddSchedulerListener(
            new SchedulerCheckout(scheduler, quartz, NullLogger<SchedulerCheckout>.Instance));

        await scheduler.Start();

        await using var locker = new NpgsqlConnection(quartz);
        await locker.OpenAsync();
        await using var tx = await locker.BeginTransactionAsync();
        await using (var lockRow = new NpgsqlCommand(
            "UPDATE qrtz_scheduler_state SET last_checkin_time = last_checkin_time WHERE sched_name = @name",
            locker, tx))
        {
            lockRow.Parameters.AddWithValue("name", scheduler.SchedulerName);
            await lockRow.ExecuteNonQueryAsync();
        }
        // tx is left open - holding the row lock SchedulerShutdown's own DELETE needs.

        var shutdown = scheduler.Shutdown();
        var finished = await Task.WhenAny(shutdown, Task.Delay(TimeSpan.FromSeconds(15))) == shutdown;

        await tx.RollbackAsync(); // release the lock either way, so the connection pool is clean after

        Assert.True(finished, "SchedulerShutdown should return once its own deadline elapses, not hang on the held lock");
    }

    private static async Task<long> StateRowsAsync(string quartz, string schedulerName)
    {
        await using var db = new NpgsqlConnection(quartz);
        await db.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT count(*) FROM qrtz_scheduler_state WHERE sched_name = @name", db);
        command.Parameters.AddWithValue("name", schedulerName);

        return (long)(await command.ExecuteScalarAsync())!;
    }
}

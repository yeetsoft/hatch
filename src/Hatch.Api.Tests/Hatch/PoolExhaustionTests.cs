using System.Net;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QuartzLogProvider = Quartz.Logging.LogProvider;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Criteria 1 and 2, against the real host rather than against
/// <see cref="DatabaseConnections"/> alone: a request that cannot get a
/// pooled connection within the wait is answered 503, and Postgres never
/// shows more connections than the cap under that application name.
///
/// <para>Boots the real host, the same way <see cref="MinimalStartupTests"/>
/// does, so it takes <see cref="Jobs.QuartzSchedulerCollection"/> rather than
/// <see cref="HatchDatabaseCollection"/>: a class has one collection, and the
/// Quartz static log provider needs the other.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class PoolExhaustionTests
{
    private const int PoolSize = 3;

    [SkippableFact(Timeout = 60_000)]
    public async Task A_full_pool_answers_503_within_the_wait_and_postgres_shows_no_more_than_the_cap()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var applicationName = $"ha-2802-{Guid.NewGuid():N}";
        var (hatchBase, quartz) = await MinimalDatabases.CreateAsync();
        await MinimalDatabases.MigrateAsync(hatchBase);
        var hatch = new NpgsqlConnectionStringBuilder(hatchBase) { ApplicationName = applicationName }.ConnectionString;

        // Same reasoning as MinimalStartupTests: Quartz's logging provider is a
        // static bound to whichever host builds a scheduler first.
        QuartzLogProvider.SetCurrentLogProvider(null);

        var logs = new CapturedLogs();
        var factory = new MinimalFactory(hatch, quartz, logs, settings: new Dictionary<string, string>
        {
            ["Database:HatchPoolSize"] = PoolSize.ToString(),
            ["Database:PoolWaitSeconds"] = "1",
        });

        var held = new List<AppDbContext>();
        try
        {
            using var client = await Task.Run(() => factory.CreateClient());

            // Criterion 1, read off the built host rather than off configuration alone.
            var connections = factory.Services.GetRequiredService<DatabaseConnections>();
            Assert.Equal(PoolSize, new NpgsqlConnectionStringBuilder(connections.Hatch!).MaxPoolSize);

            // The host's own jobs use this pool too - ReflectionCadence, Backup,
            // Goose and ChatSend all fire within moments of startup
            // (MinimalStartupTests' own remarks) - so a connection this grabs can
            // transiently fail against a job rather than against the cap. It
            // keeps trying until it holds every one of the pool's own slots
            // itself, which is the only state a further attempt is guaranteed to
            // find the pool full.
            var factoryDb = factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (held.Count < PoolSize && DateTimeOffset.UtcNow < deadline)
            {
                var db = factoryDb.CreateDbContext();
                try
                {
                    await db.Database.OpenConnectionAsync();
                    held.Add(db);
                }
                // EF wraps the pool's own exception in its own InvalidOperationException
                // before it reaches here - the same chain PoolExhaustedMiddleware walks.
                catch (Exception e) when (PoolExhaustedMiddleware.IsPoolExhausted(e))
                {
                    db.Dispose();
                    await Task.Delay(50);
                }
            }
            Assert.Equal(PoolSize, held.Count);

            // Every slot is this test's own now, so the next attempt - anywhere,
            // including through the controller below - has nowhere to go.
            var extra = factoryDb.CreateDbContext();
            try
            {
                await Assert.ThrowsAnyAsync<Exception>(() => extra.Database.OpenConnectionAsync());
            }
            finally
            {
                extra.Dispose();
            }

            var response = await client.GetAsync("/api/issues");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

            Assert.True(
                await ActiveCountAsync(hatchBase, applicationName) <= PoolSize,
                "pg_stat_activity shows more connections than the cap while the pool is held");
        }
        finally
        {
            foreach (var db in held) await db.DisposeAsync();
            await factory.DisposeAsync();
            QuartzLogProvider.SetCurrentLogProvider(null);

            Assert.True(
                await ActiveCountAsync(hatchBase, applicationName) <= PoolSize,
                "pg_stat_activity shows more connections than the cap once the pool is released");

            await MinimalDatabases.DropAsync(hatchBase, quartz);
        }
    }

    private static async Task<long> ActiveCountAsync(string connectionString, string applicationName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE application_name = @a AND pid <> pg_backend_pid()", connection);
        command.Parameters.AddWithValue("a", applicationName);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}

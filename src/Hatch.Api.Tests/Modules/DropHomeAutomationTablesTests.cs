using Hatch.Api.Ef;
using Hatch.Api.Tests.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Hatch.Api.Tests.Modules;

/// <summary>
/// What HA-2295's migration leaves in a database: a fresh one never carries
/// the 21 home-automation tables, and one that already did loses them - and
/// the dead settings rows that only those features ever read - when
/// <c>HATCH_MIGRATE=1</c> runs. Both asserted against a real Postgres, so both
/// skip without <c>HATCH_TEST_DATABASE_URL</c>.
/// </summary>
public class DropHomeAutomationTablesTests
{
    private const string DropMigration = "DropHomeAutomationTables";

    [SkippableFact]
    public async Task AFreshDatabase_HoldsOnlyTheKeptTables()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var (hatch, quartz) = await MinimalDatabases.CreateAsync();
        try
        {
            await MinimalDatabases.MigrateAsync(hatch);

            Assert.Equal(
                [
                    "ApiKeys",
                    "AuthGrants",
                    "AuthInvites",
                    "ExternalIdentities",
                    "OAuthStates",
                    "People",
                    "PersonPhotos",
                    "SiteSettings",
                    "__EFMigrationsHistory",
                ],
                await PublicTablesAsync(hatch));
        }
        finally
        {
            await MinimalDatabases.DropAsync(hatch, quartz);
        }
    }

    [SkippableFact]
    public async Task AnUpgradedDatabase_LosesTheTwentyOneTablesAndTheDeadSettings()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var (hatch, quartz) = await MinimalDatabases.CreateAsync();
        try
        {
            // The database as the last release left it: everything up to, and
            // not including, the migration under test.
            await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(hatch).Options))
            {
                var ids = db.GetService<IMigrationsAssembly>().Migrations.Keys.Order().ToList();
                var drop = ids.Single(id => id.EndsWith("_" + DropMigration, StringComparison.Ordinal));
                var before = ids[ids.IndexOf(drop) - 1];

                await db.GetService<IMigrator>().MigrateAsync(before);
            }

            string[] droppedTables =
            [
                "AirQualitySamples", "CalendarAccounts", "Calendars", "CalendarEvents", "CameraConnections",
                "Commands", "ControlDecisions", "ControlOverrides", "DeviceChannels", "Devices",
                "EnvironmentReadings", "Measurements", "PanelControlBindings", "PanelItems", "Panels",
                "RoutineActions", "Routines", "StateChanges", "WeatherAlerts", "ZoneConfigs", "Zones",
            ];
            Assert.Equal(21, droppedTables.Length);

            await using (var conn = new NpgsqlConnection(hatch))
            {
                await conn.OpenAsync();

                foreach (var table in droppedTables)
                    Assert.True(await TableExistsAsync(conn, table), $"expected {table} to exist before the migration under test");

                await ExecuteAsync(conn,
                    "INSERT INTO \"SiteSettings\" (\"Key\", \"Value\") VALUES " +
                    "('HomeAssistantToken', 'secret'), ('KioskWifiPassword', 'secret'), ('TimeZone', 'UTC'), ('GoogleClientId', 'client-id') " +
                    "ON CONFLICT (\"Key\") DO NOTHING");
            }

            await MinimalDatabases.MigrateAsync(hatch);

            await using var after = new NpgsqlConnection(hatch);
            await after.OpenAsync();

            foreach (var table in droppedTables)
                Assert.False(await TableExistsAsync(after, table), $"expected {table} to be dropped by the migration");

            await using var command = new NpgsqlCommand(
                "SELECT \"Key\" FROM \"SiteSettings\" WHERE \"Key\" IN " +
                "('HomeAssistantToken', 'KioskWifiPassword', 'TimeZone', 'GoogleClientId')", after);
            var keys = new List<string>();
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) keys.Add(reader.GetString(0));

            Assert.Equal(["GoogleClientId", "TimeZone"], keys.Order());
        }
        finally
        {
            await MinimalDatabases.DropAsync(hatch, quartz);
        }
    }

    /// <summary>Every table in the <c>public</c> schema, which is where the core context's tables live.</summary>
    private static async Task<List<string>> PublicTablesAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name", conn);

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string table)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @t)", connection);
        command.Parameters.AddWithValue("t", table);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

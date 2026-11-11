using Hatch.Api.Ef;
using Hatch.Api.Tests.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Hatch.Api.Tests.Modules;

/// <summary>
/// What the removal of the Gather, Photos, Quill and Storage modules leaves in
/// a database (HA-2253): a fresh one never has their schemas, and one that
/// already did loses them - tables, rows and the two Immich settings - when
/// <c>HATCH_MIGRATE=1</c> runs. Both asserted against a real Postgres, so both
/// skip without <c>HATCH_TEST_DATABASE_URL</c>.
/// </summary>
public class FamilySchemasTests
{
    private static readonly string[] FamilySchemas = ["gather", "photos", "quill", "storage"];

    private const string DropMigration = "DropFamilyAppSchemas";

    [SkippableFact]
    public async Task AFreshDatabase_HoldsPublicAndHatchAndNothingElse()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var (hatch, quartz) = await MinimalDatabases.CreateAsync();
        try
        {
            await MinimalDatabases.MigrateAsync(hatch);

            Assert.Equal(["hatch", "public"], await SchemasAsync(hatch));
        }
        finally
        {
            await MinimalDatabases.DropAsync(hatch, quartz);
        }
    }

    [SkippableFact]
    public async Task AnUpgradedDatabase_LosesTheFourSchemasAndTheTwoImmichSettings()
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

            await using (var conn = new NpgsqlConnection(hatch))
            {
                await conn.OpenAsync();
                foreach (var schema in FamilySchemas)
                {
                    await ExecuteAsync(conn, $"CREATE SCHEMA \"{schema}\"");
                    await ExecuteAsync(conn, $"CREATE TABLE \"{schema}\".\"Things\" (\"Id\" integer PRIMARY KEY)");
                    await ExecuteAsync(conn, $"INSERT INTO \"{schema}\".\"Things\" VALUES (1)");
                }

                await ExecuteAsync(conn,
                    "INSERT INTO \"SiteSettings\" (\"Key\", \"Value\") VALUES ('ImmichBaseUrl', 'https://photos.example'), ('ImmichApiKey', 'secret'), ('TimeZone', 'UTC') " +
                    "ON CONFLICT (\"Key\") DO NOTHING");

                Assert.Equal(new[] { "gather", "hatch", "photos", "public", "quill", "storage" }, await SchemasAsync(hatch));
            }

            await MinimalDatabases.MigrateAsync(hatch);

            Assert.Equal(["hatch", "public"], await SchemasAsync(hatch));

            await using var after = new NpgsqlConnection(hatch);
            await after.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT \"Key\" FROM \"SiteSettings\" WHERE \"Key\" IN ('ImmichBaseUrl', 'ImmichApiKey', 'TimeZone')", after);
            var keys = new List<string>();
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) keys.Add(reader.GetString(0));

            // Only the two Immich rows go: any other setting is left alone.
            Assert.Equal(["TimeZone"], keys);
        }
        finally
        {
            await MinimalDatabases.DropAsync(hatch, quartz);
        }
    }

    /// <summary>Every schema a person made: Postgres's own (<c>pg_*</c>, <c>information_schema</c>) are not the question.</summary>
    private static async Task<List<string>> SchemasAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT schema_name FROM information_schema.schemata " +
            "WHERE schema_name NOT LIKE 'pg\\_%' AND schema_name <> 'information_schema' ORDER BY schema_name", conn);

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

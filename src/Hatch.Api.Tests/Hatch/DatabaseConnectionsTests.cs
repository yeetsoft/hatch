using Hatch.Api.Ef;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="DatabaseConnections"/> against a real Postgres (HA-2802): the
/// defaults, the statement timeout actually cancelling the server-side
/// statement, and an operator's own connection-string keys winning over
/// everything this computes.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class DatabaseConnectionsTests
{
    [SkippableFact]
    public async Task With_no_configuration_the_two_pools_sum_to_40_or_fewer_against_npgsqls_own_default_of_100()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        // Pinned rather than trusted from memory - the brief's own correction.
        Assert.Equal(100, new NpgsqlConnectionStringBuilder().MaxPoolSize);

        var baseConnectionString = await HatchDatabase.PrepareAsync();
        var configuration = Configure(baseConnectionString);

        var connections = DatabaseConnections.From(configuration);

        var hatchPool = new NpgsqlConnectionStringBuilder(connections.Hatch!).MaxPoolSize;
        var quartzPool = new NpgsqlConnectionStringBuilder(connections.Quartz!).MaxPoolSize;

        Assert.Equal(DatabaseConnections.DefaultHatchPoolSize, hatchPool);
        Assert.Equal(DatabaseConnections.DefaultQuartzPoolSize, quartzPool);
        Assert.True(hatchPool + quartzPool <= 40, $"{hatchPool} + {quartzPool} is more than 40");
    }

    [SkippableFact]
    public async Task A_statement_that_sleeps_past_the_timeout_raises_and_pg_stat_activity_stops_showing_it()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var baseConnectionString = await HatchDatabase.PrepareAsync();
        var applicationName = $"ha-2802-{Guid.NewGuid():N}";
        var configuration = Configure(baseConnectionString, new Dictionary<string, string?>
        {
            ["Database:CommandTimeoutSeconds"] = "1",
        });

        var connections = DatabaseConnections.From(configuration);
        var builder = new NpgsqlConnectionStringBuilder(connections.Hatch!) { ApplicationName = applicationName };

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT pg_sleep(10)", connection);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<NpgsqlException>(() => command.ExecuteNonQueryAsync());
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");

        await AssertNoActiveQueryAsync(baseConnectionString, applicationName, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("Maximum Pool Size", 7)]
    [InlineData("MaxPoolSize", 7)]
    public void An_operator_written_pool_size_is_left_alone_under_either_spelling(string key, int value)
    {
        var connections = DatabaseConnections.From(Configure($"Host=localhost;Database=hatch;{key}={value}"));

        Assert.Equal(value, new NpgsqlConnectionStringBuilder(connections.Hatch!).MaxPoolSize);
    }

    [Theory]
    [InlineData("Command Timeout", 9)]
    [InlineData("CommandTimeout", 9)]
    public void An_operator_written_command_timeout_is_left_alone_under_either_spelling(string key, int value)
    {
        var connections = DatabaseConnections.From(Configure($"Host=localhost;Database=hatch;{key}={value}"));

        Assert.Equal(value, new NpgsqlConnectionStringBuilder(connections.Hatch!).CommandTimeout);
    }

    [Fact]
    public void An_operator_written_wait_timeout_is_left_alone()
    {
        var connections = DatabaseConnections.From(Configure("Host=localhost;Database=hatch;Timeout=11"));

        Assert.Equal(11, new NpgsqlConnectionStringBuilder(connections.Hatch!).Timeout);
    }

    [Fact]
    public void In_migrate_mode_the_hatch_string_carries_no_command_timeout_by_default()
    {
        var configuration = Configure(
            "Host=localhost;Database=hatch", extra: new Dictionary<string, string?> { ["HATCH_MIGRATE"] = "1" });

        var connections = DatabaseConnections.From(configuration);

        Assert.Equal(0, new NpgsqlConnectionStringBuilder(connections.Hatch!).CommandTimeout);
    }

    [Fact]
    public void In_migrate_mode_an_operator_setting_still_wins_over_no_timeout()
    {
        var configuration = Configure(
            "Host=localhost;Database=hatch",
            extra: new Dictionary<string, string?> { ["HATCH_MIGRATE"] = "1", ["Database:CommandTimeoutSeconds"] = "45" });

        var connections = DatabaseConnections.From(configuration);

        Assert.Equal(45, new NpgsqlConnectionStringBuilder(connections.Hatch!).CommandTimeout);
    }

    [Fact]
    public void A_blank_connection_string_is_returned_unchanged()
    {
        var configuration = Configure(null);

        var connections = DatabaseConnections.From(configuration);

        Assert.Null(connections.Hatch);
    }

    private static IConfiguration Configure(string? hatchConnectionString, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Hatch"] = hatchConnectionString,
            ["ConnectionStrings:Quartz"] = hatchConnectionString,
        };
        if (extra is not null)
            foreach (var (key, value) in extra) settings[key] = value;

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    /// <summary>Polls <c>pg_stat_activity</c> for an active query under <paramref name="applicationName"/>, bounded at <paramref name="bound"/>.</summary>
    private static async Task AssertNoActiveQueryAsync(string connectionString, string applicationName, TimeSpan bound)
    {
        var until = DateTimeOffset.UtcNow + bound;

        while (true)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE application_name = @a AND state = 'active' AND pid <> pg_backend_pid()",
                connection);
            command.Parameters.AddWithValue("a", applicationName);
            var count = (long)(await command.ExecuteScalarAsync())!;

            if (count == 0) return;
            if (DateTimeOffset.UtcNow >= until) Assert.Fail($"pg_stat_activity still shows {count} active query/queries for {applicationName}");

            await Task.Delay(100);
        }
    }
}

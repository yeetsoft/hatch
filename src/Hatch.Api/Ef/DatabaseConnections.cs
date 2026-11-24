using Npgsql;

namespace Hatch.Api.Ef;

/// <summary>
/// The two connection strings as the app actually opens them, rather than as
/// <c>ConnectionStrings:Hatch</c>/<c>ConnectionStrings:Quartz</c> are
/// configured: capped to a pool that fits Postgres instead of Npgsql's own
/// default of 100 per string per process, with a statement timeout and a
/// bounded wait for a pooled connection (HA-2802, off the figures HA-2765
/// read on the install that found the gap).
///
/// <para>Every opener of either string - <c>Program.cs</c>,
/// <see cref="Modules.ModuleRegistration"/>, <c>SchedulerCheckout</c> and
/// <c>SecretReseal</c> - has to arrive at this same record rather than
/// read the configured string on its own: a second computed string, even one
/// that differs by a single key, opens as a second pool.</para>
/// </summary>
public sealed record DatabaseConnections(string? Hatch, string? Quartz)
{
    public const int DefaultHatchPoolSize = 30;
    public const int DefaultQuartzPoolSize = 10;
    public const int DefaultCommandTimeoutSeconds = 30;
    public const int DefaultPoolWaitSeconds = 5;

    /// <summary>
    /// Reads the four <c>Database:*</c> settings and the configured strings,
    /// and applies them. In migrate mode (<c>HATCH_MIGRATE=1</c>) the
    /// <c>Hatch</c> string's command timeout defaults to <c>0</c> (none)
    /// instead of <see cref="DefaultCommandTimeoutSeconds"/>, since a
    /// migration is expected to run longer than an ordinary request - an
    /// operator who sets <c>Database:CommandTimeoutSeconds</c> still wins over
    /// that default, the same as everywhere else.
    /// </summary>
    public static DatabaseConnections From(IConfiguration configuration)
    {
        var migrateMode = configuration["HATCH_MIGRATE"] == "1";
        var waitSeconds = ReadInt(configuration, "Database:PoolWaitSeconds", DefaultPoolWaitSeconds);

        return new DatabaseConnections(
            Apply(
                configuration.GetConnectionString("Hatch"),
                ReadInt(configuration, "Database:HatchPoolSize", DefaultHatchPoolSize),
                ReadInt(configuration, "Database:CommandTimeoutSeconds", migrateMode ? 0 : DefaultCommandTimeoutSeconds),
                waitSeconds),
            Apply(
                configuration.GetConnectionString("Quartz"),
                ReadInt(configuration, "Database:QuartzPoolSize", DefaultQuartzPoolSize),
                ReadInt(configuration, "Database:CommandTimeoutSeconds", DefaultCommandTimeoutSeconds),
                waitSeconds));
    }

    /// <summary>
    /// A blank string is returned unchanged - this is not the place that
    /// decides a missing connection string is an error. Otherwise, each
    /// setting is written only where the string does not already hold it,
    /// under any spelling Npgsql accepts for the keyword (<c>MaxPoolSize</c>
    /// as well as <c>Maximum Pool Size</c>, and so on): a value the operator
    /// wrote into the connection string always wins.
    ///
    /// <para><c>ContainsKey</c> is not that test - it answers true for every
    /// keyword Npgsql knows, set or not, because the builder has a default
    /// for each. <c>ShouldSerialize</c>, asked of the canonical keyword name,
    /// is the one that is true only once something - canonical or alias -
    /// actually set it.</para>
    /// </summary>
    private static string? Apply(string? connectionString, int poolSize, int commandTimeoutSeconds, int waitSeconds)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return connectionString;

        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        if (!builder.ShouldSerialize("Maximum Pool Size")) builder.MaxPoolSize = poolSize;
        if (!builder.ShouldSerialize("Command Timeout")) builder.CommandTimeout = commandTimeoutSeconds;
        if (!builder.ShouldSerialize("Timeout")) builder.Timeout = waitSeconds;

        return builder.ConnectionString;
    }

    private static int ReadInt(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], out var value) && value > 0 ? value : fallback;
}

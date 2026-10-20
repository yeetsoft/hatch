using Hatch.Api.Ef;
using Hatch.Api.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// A scratch <c>hatch</c> and a scratch <c>quartz</c>, made the way a friend's
/// machine makes them: two empty databases, with the Quartz DDL applied to the
/// second. Both are dropped and recreated per call, so each test gets the
/// blank slate its criterion is about - and dropped again once the case is
/// done with them, so a server this runs against accumulates nothing.
/// </summary>
internal static class MinimalDatabases
{
    private static readonly SemaphoreSlim Once = new(1, 1);

    private static int counter;

    public static async Task<(string Hatch, string Quartz)> CreateAsync()
    {
        var configured = Environment.GetEnvironmentVariable(HatchDatabase.Variable)
            ?? throw new InvalidOperationException($"{HatchDatabase.Variable} is unset");

        var template = new NpgsqlConnectionStringBuilder(HatchDatabase.ToConnectionString(configured.Trim()));

        // Suffixed off the database the variable names, so this can only ever
        // drop something it made - the variable's own database is never
        // touched. The process id sits beside the counter so two test
        // processes against the one server never land on the same pair -
        // the counter alone repeats from 1 in every process.
        var n = Interlocked.Increment(ref counter);
        var hatch = Named(template, $"{template.Database}_min{Environment.ProcessId}_{n}_hatch");
        var quartz = Named(template, $"{template.Database}_min{Environment.ProcessId}_{n}_quartz");

        // CREATE DATABASE cannot run inside the database being created, and
        // two of these racing on one server is a duplicate-name error rather
        // than a queue, so the maintenance work is serialized.
        await Once.WaitAsync();
        try
        {
            await using var maintenance = MaintenanceConnection(template);
            await maintenance.OpenAsync();

            foreach (var name in new[] { new NpgsqlConnectionStringBuilder(hatch).Database!, new NpgsqlConnectionStringBuilder(quartz).Database! })
            {
                // WITH (FORCE) rather than a plain DROP: Npgsql pools
                // connections across tests in this process, and a leftover idle
                // one is enough to make "database is being accessed by other
                // users" the result instead.
                await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS {Quote(name)} WITH (FORCE)");
                await ExecuteAsync(maintenance, $"CREATE DATABASE {Quote(name)}");
            }
        }
        finally
        {
            Once.Release();
        }

        // The one that ships, read from where `db` reads it - containers/hatch-db/pginit.sql,
        // the script the compose db container runs on its own first boot. Copying
        // the DDL in here would be a second copy that could silently stop
        // matching the one the container runs; reading it up to and including
        // the CREATE DATABASE/`\c quartz` lines that only psql's own init
        // machinery can execute is the alternative to that copy, not a licence
        // to drift from that file's shape.
        var pginit = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "containers", "hatch-db", "pginit.sql"));
        var afterConnect = pginit.IndexOf("\\c quartz", StringComparison.Ordinal);
        Assert.True(afterConnect >= 0, "containers/hatch-db/pginit.sql has no `\\c quartz` line to split the Quartz DDL from.");
        var quartzDdl = pginit[(afterConnect + "\\c quartz".Length)..];

        await using (var quartzDb = new NpgsqlConnection(quartz))
        {
            await quartzDb.OpenAsync();
            await ExecuteAsync(quartzDb, quartzDdl);
        }

        return (hatch, quartz);
    }

    /// <summary>
    /// What <c>HATCH_MIGRATE=1</c> does to the core schema and to every
    /// module's own - the migrate-mode loop at Program.cs copied rather than
    /// exercised through Program itself, since migrate mode is a startup
    /// branch this factory's host never takes. <see cref="AppDbContext"/>
    /// first, then every <see cref="IModuleContext"/> <c>AddAppModules</c>
    /// registers, in registry order - there is no list of module contexts
    /// here for the same reason there is none in Program.cs: a module added
    /// to the registry is migrated by this loop with no second line.
    /// </summary>
    public static async Task MigrateAsync(string hatch)
    {
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(hatch).Options))
            await db.Database.MigrateAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Hatch"] = hatch })
            .Build();

        var services = new ServiceCollection().AddAppModules(configuration);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        foreach (var moduleDb in scope.ServiceProvider.GetServices<IModuleContext>())
            await moduleDb.Database.MigrateAsync();
    }

    /// <summary>
    /// Drops both scratch databases, so a server this runs against is left
    /// holding nothing from the run. Clears the Npgsql pool for each
    /// connection string first - a pooled idle connection is enough to make
    /// the drop fail with "database is being accessed by other users" even
    /// though the test itself is done with it - then runs the drop through
    /// the same serialized maintenance connection <see cref="CreateAsync"/>
    /// opens, for the same reason: two of these racing is a lock error, not a
    /// queue.
    /// </summary>
    public static async Task DropAsync(string hatch, string quartz)
    {
        using (var c = new NpgsqlConnection(hatch)) NpgsqlConnection.ClearPool(c);
        using (var c = new NpgsqlConnection(quartz)) NpgsqlConnection.ClearPool(c);

        await Once.WaitAsync();
        try
        {
            await using var maintenance = MaintenanceConnection(new NpgsqlConnectionStringBuilder(hatch));
            await maintenance.OpenAsync();

            foreach (var name in new[] { new NpgsqlConnectionStringBuilder(hatch).Database!, new NpgsqlConnectionStringBuilder(quartz).Database! })
                await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS {Quote(name)} WITH (FORCE)");
        }
        finally
        {
            Once.Release();
        }
    }

    /// <summary>Criterion 6, asked of the store rather than of the log: which of this group's jobs hold a trigger.</summary>
    public static async Task<IReadOnlyList<string>> TriggerNamesAsync(string quartz, string group)
    {
        await using var db = new NpgsqlConnection(quartz);
        await db.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT trigger_name FROM qrtz_triggers WHERE trigger_group = @g", db);
        command.Parameters.AddWithValue("g", group);

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));

        return names;
    }

    /// <summary>The board every install starts with, read back in the order the Init migration seeds it.</summary>
    public static async Task<IReadOnlyList<string>> StatusNamesAsync(string hatch)
    {
        await using var db = new NpgsqlConnection(hatch);
        await db.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT \"Name\" FROM hatch.\"Statuses\" ORDER BY \"SortOrder\"", db);

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));

        return names;
    }

    /// <summary>
    /// A content root with a <c>wwwroot</c> in it and nothing else. The
    /// directory has to exist (Program.cs combines a path off WebRootPath) and
    /// has to be empty of apps, which is what makes the boot under test the one
    /// a friend gets rather than one shaped by whatever is in the working copy.
    /// Named per process for the same reason the databases above are: two test
    /// processes sharing one temp directory would otherwise race each other's
    /// <c>wwwroot</c>.
    /// </summary>
    public static string ScratchContentRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hatch-minimal-startup-{Environment.ProcessId}");
        Directory.CreateDirectory(Path.Combine(root, "wwwroot"));
        return root;
    }

    /// <summary>Found by walking up for the solution file, so this works from wherever the test binary is run.</summary>
    private static string RepositoryRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "src", "Hatch.slnx")))
                return d.FullName;

        throw new InvalidOperationException("Could not find the repository root (no src/Hatch.slnx above the test binary).");
    }

    private static string Named(NpgsqlConnectionStringBuilder template, string database) =>
        new NpgsqlConnectionStringBuilder(template.ConnectionString) { Database = database }.ConnectionString;

    /// <summary>
    /// The <c>postgres</c> maintenance database, with a longer command timeout
    /// than the 30s default: <c>WITH (FORCE)</c> waits on Postgres to actually
    /// terminate the backends it finds, which a server busy running the rest of
    /// the suite in parallel can take a while to get to.
    /// </summary>
    private static NpgsqlConnection MaintenanceConnection(NpgsqlConnectionStringBuilder template) =>
        new(new NpgsqlConnectionStringBuilder(Named(template, "postgres")) { CommandTimeout = 60 }.ConnectionString);

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Database names are composed here from the variable's own, so there is nothing a caller could inject; quoting is so an unusual one still parses.</summary>
    private static string Quote(string name) => $"\"{name.Replace("\"", "\"\"")}\"";
}

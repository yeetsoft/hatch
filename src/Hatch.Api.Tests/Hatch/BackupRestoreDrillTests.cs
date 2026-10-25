using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The restore drill, run for real: <see cref="BackupService"/> dumps a seeded
/// database through the real <c>pg_dump</c>, and the copy is restored with
/// <c>psql</c> into a second scratch database - the thing an operator does the
/// day a backup is needed. <see cref="BackupServiceTests"/> proves the service
/// around a fake process; nothing else proves the bytes it writes are a copy
/// that comes back.
///
/// <para><b>These skip without the tools, and say why.</b> A <c>pg_dump</c> or
/// <c>psql</c> that is missing, or whose major version is not the server's
/// (<c>pg_dump</c> refuses an older client outright, and a newer one writes SQL
/// an older server may not read), would test the machine rather than Hatch.
/// The skips are decided before any database is made, so a skip leaks
/// nothing.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class BackupRestoreDrillTests
{
    private const string DownloadUrl = "https://www.postgresql.org/download/linux/ubuntu/";

    /// <summary>The comment's worth of awkward text a faithful copy has to carry byte for byte.</summary>
    private const string Awkward = "café ✓ \"quoted\"\nline two";

    [SkippableFact]
    public async Task ADump_RestoresIntoASecondDatabase_WithEveryRowIntact()
    {
        await using var drill = await PrepareAsync();
        await using var target = await RestoreTarget.CreateAsync(drill, "_restore");

        var restore = await target.RestoreAsync(drill.DumpPath);

        Assert.True(restore.Exit == 0, $"psql exited {restore.Exit}: {restore.Stderr}");

        await using var hatch = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(target.ConnectionString).Options);
        await using var app = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(target.ConnectionString).Options);

        Assert.Equal(drill.Counts, await CountsAsync(hatch, app));

        var sample = await hatch.Issues.AsNoTracking()
            .Where(i => i.Project!.Key == drill.SampleProject && i.Number == drill.SampleNumber)
            .SingleAsync();

        // Bytes, not strings: a string compare would pass a copy that came
        // back in the wrong encoding as long as both sides decoded alike.
        Assert.Equal(Encoding.UTF8.GetBytes(drill.SampleDescription), Encoding.UTF8.GetBytes(sample.Description));
    }

    [SkippableFact]
    public async Task ACorruptedDump_IsRefused_NotRestoredAsIfItWereWhole()
    {
        // Its own source and its own folder: the first dump's name is stamped
        // to the second, and a shared folder could clash on it.
        await using var drill = await PrepareAsync();
        await using var target = await RestoreTarget.CreateAsync(drill, "_restore2");

        // Not cut at half the file: a plain-format dump is the CREATEs, then
        // COPY ... FROM stdin data, then the constraints, and psql reads stdin
        // EOF inside a COPY as the end of the data - a cut there restores
        // partial rows and exits 0. A statement cut off mid-way, and then
        // terminated so psql runs it rather than wait on it, cannot.
        var text = await File.ReadAllTextAsync(drill.DumpPath);
        var cut = text.IndexOf("CREATE TABLE ", StringComparison.Ordinal);
        Assert.True(cut >= 0, "the dump has no CREATE TABLE to cut inside");

        var corrupted = Path.Combine(drill.BackupPath, "corrupted.sql");
        await File.WriteAllTextAsync(corrupted, text[..(cut + "CREATE TABLE ".Length)] + ";\n");

        var restore = await target.RestoreAsync(corrupted);

        Assert.NotEqual(0, restore.Exit);
        Assert.False(string.IsNullOrWhiteSpace(restore.Stderr), "psql refused the copy without saying why");
    }

    // ---- The source database ----

    /// <summary>
    /// A migrated, seeded database and a real dump of it, or a skip. The skips
    /// come first, before any database exists, so a machine without the tools
    /// is left with nothing to clean up.
    /// </summary>
    private static async Task<Drill> PrepareAsync()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var configured = Environment.GetEnvironmentVariable(HatchDatabase.Variable)!.Trim();
        var template = HatchDatabase.ToConnectionString(configured);

        var dumpMajor = await ToolMajorAsync("pg_dump");
        var psqlMajor = await ToolMajorAsync("psql");

        // Before the server is asked anything: a machine with no tools skips
        // whether or not the database is up.
        Skip.IfNot(
            dumpMajor is not null && psqlMajor is not null,
            $"pg_dump (major {dumpMajor?.ToString() ?? "missing"}) and psql (major {psqlMajor?.ToString() ?? "missing"}) " +
            $"must be on the PATH at the connected server's major version; install them from {DownloadUrl}");

        int serverMajor;
        // The maintenance database, as MinimalDatabases uses: the one the
        // variable names need not exist, since nothing here touches it.
        await using (var server = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(template) { Database = "postgres" }.ConnectionString))
        {
            await server.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT current_setting('server_version_num')::int / 10000", server);
            serverMajor = (int)(await command.ExecuteScalarAsync())!;
        }

        Skip.IfNot(
            dumpMajor == serverMajor && psqlMajor == serverMajor,
            $"pg_dump (major {dumpMajor}) and psql (major {psqlMajor}) " +
            $"must be the connected server's major version ({serverMajor}); install matching client tools from {DownloadUrl}");

        var (hatch, quartz) = await MinimalDatabases.CreateAsync();
        var backupPath = Directory.CreateTempSubdirectory("hatch-restore-drill-").FullName;

        try
        {
            await MinimalDatabases.MigrateAsync(hatch);

            var (counts, project, number, description) = await SeedAsync(hatch);

            var service = new BackupService(
                Options.Create(new HatchOptions { BackupPath = backupPath }),
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Hatch"] = hatch })
                    .Build(),
                new SystemBackupProcess(),
                new BackupStatus(),
                TimeProvider.System,
                NullLogger<BackupService>.Instance);

            var attempt = await service.RunAsync(CancellationToken.None);

            Assert.True(attempt.Outcome == BackupOutcome.Ok, attempt.Error);

            return new Drill(
                hatch, quartz, new NpgsqlConnectionStringBuilder(hatch), backupPath,
                Path.Combine(backupPath, attempt.File!), counts, project, number, description);
        }
        catch
        {
            await MinimalDatabases.DropAsync(hatch, quartz);
            DeleteQuietly(backupPath);
            throw;
        }
    }

    /// <summary>
    /// A project, three issues with a description that is not plain ASCII, a
    /// comment and an event on each, and a person and a key from the core
    /// schema - so the copy is checked across both of the dump's schemas.
    /// Counted afterwards rather than assumed: migrations seed rows of their own.
    /// </summary>
    private static async Task<(Counts Counts, string Project, int Number, string Description)> SeedAsync(string connectionString)
    {
        var now = DateTimeOffset.UtcNow;
        var unique = Guid.NewGuid().ToString("N");

        await using var hatch = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);
        await using var app = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);

        var statusId = await hatch.Statuses.Select(s => s.Id).FirstAsync();

        var project = new EfHatchProject { Key = "DRL", Name = "Restore drill", CreatedAt = now };
        hatch.Projects.Add(project);
        await hatch.SaveChangesAsync();

        for (var number = 1; number <= 3; number++)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = project.Id,
                Number = number,
                Type = "task",
                Title = $"Drill issue {number}",
                Description = Awkward,
                StatusId = statusId,
                Rank = 1024 * number,
                CreatedBy = "drill",
                CreatedAt = now,
                UpdatedAt = now,
            };

            issue.Comments.Add(new EfHatchComment { Author = "drill", Body = $"Drill comment {number}", CreatedAt = now });
            hatch.Issues.Add(issue);
        }

        await hatch.SaveChangesAsync();

        foreach (var issue in await hatch.Issues.Where(i => i.ProjectId == project.Id).ToListAsync())
        {
            hatch.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id,
                Actor = "drill",
                Kind = EfHatchIssueEvent.Created,
                Payload = "{}",
                At = now,
            });
        }

        await hatch.SaveChangesAsync();

        app.People.Add(new EfPerson { Name = "Drill person", CreatedAt = now, UpdatedAt = now });
        app.ApiKeys.Add(new EfApiKey
        {
            Name = $"drill-{unique}",
            Prefix = unique[..EfApiKey.PrefixLength],
            Hash = [1],
            Scopes = [ApiKeyScopes.Hatch],
            CreatedAt = now,
        });
        await app.SaveChangesAsync();

        return (await CountsAsync(hatch, app), project.Key, 2, Awkward);
    }

    private static async Task<Counts> CountsAsync(HatchContext hatch, AppDbContext app) =>
        new(
            await hatch.Issues.CountAsync(),
            await hatch.Comments.CountAsync(),
            await hatch.IssueEvents.CountAsync(),
            await app.People.CountAsync(),
            await app.ApiKeys.CountAsync());

    // ---- The tools ----

    /// <summary>
    /// The major version a tool reports, or null when it is not there or will
    /// not say - <c>pg_dump (PostgreSQL) 18.2</c> is 18.
    /// </summary>
    private static async Task<int?> ToolMajorAsync(string tool)
    {
        try
        {
            var result = await RunToolAsync(tool, ["--version"]);
            if (result.Exit != 0) return null;

            var match = Regex.Match(result.Stdout, @"\d+");
            return match.Success ? int.Parse(match.Value) : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// One child process to the end, mirroring <see cref="SystemBackupProcess"/>.
    /// Both output pipes are being read before any stdin is written: psql
    /// echoes a command tag for every statement it runs, and a pipe nobody
    /// drains fills and stops it - a deadlock on a restore of any size.
    /// </summary>
    private static async Task<(int Exit, string Stdout, string Stderr)> RunToolAsync(
        string file, IEnumerable<string> args, IReadOnlyDictionary<string, string>? env = null, byte[]? stdin = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
        };

        foreach (var arg in args) psi.ArgumentList.Add(arg);
        foreach (var (key, value) in env ?? new Dictionary<string, string>()) psi.Environment[key] = value;

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (stdin is not null)
        {
            try
            {
                await process.StandardInput.BaseStream.WriteAsync(stdin);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // psql stopped reading because it had already given up (the
                // ON_ERROR_STOP case); its exit code and stderr say why.
            }
        }

        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout, await stderr);
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ---- What the drill carries ----

    private sealed record Counts(int Issues, int Comments, int Events, int People, int ApiKeys);

    private sealed record Drill(
        string Hatch,
        string Quartz,
        NpgsqlConnectionStringBuilder Connection,
        string BackupPath,
        string DumpPath,
        Counts Counts,
        string SampleProject,
        int SampleNumber,
        string SampleDescription) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await MinimalDatabases.DropAsync(Hatch, Quartz);
            DeleteQuietly(BackupPath);
        }
    }

    /// <summary>
    /// An empty database beside the source, to restore a copy into, dropped
    /// again when the fact is done with it. A failure to create it fails the
    /// fact rather than skipping it: making the source already needed
    /// <c>CREATE DATABASE</c>, so a role that got this far has it.
    /// </summary>
    private sealed class RestoreTarget : IAsyncDisposable
    {
        private readonly Drill _drill;
        private readonly string _name;

        private RestoreTarget(Drill drill, string name, string connectionString)
        {
            _drill = drill;
            _name = name;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public static async Task<RestoreTarget> CreateAsync(Drill drill, string suffix)
        {
            var name = drill.Connection.Database + suffix;
            var target = new RestoreTarget(
                drill, name, new NpgsqlConnectionStringBuilder(drill.Connection.ConnectionString) { Database = name }.ConnectionString);

            // A leftover of an earlier run that shared this process id.
            await target.DropAsync();

            var created = await RunToolAsync("createdb", target.Args(name), target.Env());
            Assert.True(created.Exit == 0, $"createdb {name} exited {created.Exit}: {created.Stderr}");

            return target;
        }

        public async Task<(int Exit, string Stdout, string Stderr)> RestoreAsync(string dumpPath) =>
            await RunToolAsync(
                "psql",
                ["-q", "-v", "ON_ERROR_STOP=1", .. Args("-d", _name)],
                Env(),
                await File.ReadAllBytesAsync(dumpPath));

        public async ValueTask DisposeAsync() => await DropAsync();

        private async Task DropAsync()
        {
            // A pooled idle connection is enough to make the drop refuse.
            using (var c = new NpgsqlConnection(ConnectionString)) NpgsqlConnection.ClearPool(c);

            await RunToolAsync("dropdb", ["--if-exists", "--force", .. Args(_name)], Env());
        }

        private string[] Args(params string[] rest) =>
        [
            "-w",
            "-h", _drill.Connection.Host!,
            "-p", _drill.Connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-U", _drill.Connection.Username!,
            .. rest,
        ];

        private Dictionary<string, string> Env() => new() { ["PGPASSWORD"] = _drill.Connection.Password ?? "" };
    }
}

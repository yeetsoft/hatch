using Hatch.Api.Modules.Hatch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="BackupService"/> against a <see cref="FakeBackupProcess"/> -
/// never a real <c>pg_dump</c>, which is <see cref="SystemBackupProcess"/>'s
/// own untested wrapper. Each case is one of the failure sentences
/// HA-1548 names, or the one path with none.
/// </summary>
public sealed class BackupServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 3, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hatch-backup-service-" + Guid.NewGuid().ToString("N"));

    public BackupServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task RunAsync_ExitZero_WritesOneCompleteCopyAndRecordsOk()
    {
        var bytes = "dump-bytes"u8.ToArray();
        var process = new FakeBackupProcess { Stdout = bytes, ExitCode = 0 };
        var (service, status) = NewService(process, _dir);

        var attempt = await service.RunAsync(CancellationToken.None);

        var finalName = BackupNames.Final(Now);
        Assert.Equal(BackupOutcome.Ok, attempt.Outcome);
        Assert.Null(attempt.Error);
        Assert.Equal(finalName, attempt.File);
        Assert.Equal(bytes.Length, attempt.Bytes);
        Assert.Same(attempt, status.Last);

        var file = Assert.Single(Directory.GetFiles(_dir));
        Assert.Equal(finalName, Path.GetFileName(file));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(file));

        var start = process.LastStart;
        Assert.NotNull(start);
        Assert.Equal("pg_dump", start!.FileName);
        Assert.Equal(
            ["--clean", "--if-exists", "--no-password", "-h", "db-test", "-p", "5433", "-U", "hatchuser", "hatchdb"],
            start.Args);
        Assert.Equal("s3cret", start.Env["PGPASSWORD"]);
        Assert.DoesNotContain("s3cret", start.Args);
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_LeavesTheEarlierCopyUntouchedAndRecordsTheStderrTail()
    {
        var earlierAt = Now.AddDays(-1);
        var earlierBytes = new byte[] { 1, 2, 3 };
        await File.WriteAllBytesAsync(Path.Combine(_dir, BackupNames.Final(earlierAt)), earlierBytes);

        var stderr = "line1\n\nline2\r\nline3\nline4\nline5\nline6\n";
        var process = new FakeBackupProcess { ExitCode = 1, Stderr = stderr };
        var (service, status) = NewService(process, _dir);

        var attempt = await service.RunAsync(CancellationToken.None);

        Assert.Equal(BackupOutcome.Failed, attempt.Outcome);
        Assert.Contains("line6", attempt.Error);
        Assert.DoesNotContain("line1", attempt.Error);
        Assert.Same(attempt, status.Last);

        var file = Assert.Single(Directory.GetFiles(_dir));
        Assert.Equal(BackupNames.Final(earlierAt), Path.GetFileName(file));
        Assert.Equal(earlierBytes, await File.ReadAllBytesAsync(file));
    }

    [Fact]
    public async Task RunAsync_ProcessHangs_TimesOutAndLeavesNoPartial()
    {
        var process = new FakeBackupProcess { Hangs = true };
        var (service, status) = NewService(process, _dir, timeout: TimeSpan.FromMilliseconds(100));

        var attempt = await service.RunAsync(CancellationToken.None);

        Assert.Equal(BackupOutcome.Failed, attempt.Outcome);
        Assert.Contains("did not finish", attempt.Error);
        Assert.Same(attempt, status.Last);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task RunAsync_EmptyPath_RecordsNotConfiguredAndTouchesNothing()
    {
        var process = new FakeBackupProcess();
        var (service, status) = NewService(process, "");

        var attempt = await service.RunAsync(CancellationToken.None);

        Assert.Equal(BackupOutcome.NotConfigured, attempt.Outcome);
        Assert.Null(attempt.Error);
        Assert.Same(attempt, status.Last);
        Assert.Null(process.LastStart);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Copies_EmptyPath_ReturnsEmptyWithNoProblem_AndNeverListsTheFolder()
    {
        var (service, _) = NewService(new FakeBackupProcess(), "");

        var copies = service.Copies();

        Assert.Empty(copies.Items);
        Assert.Null(copies.Problem);
    }

    [Fact]
    public async Task RunAsync_MissingFolder_FailsNamingThePathAndNeverInvokesTheProcess()
    {
        var missing = Path.Combine(_dir, "does-not-exist");
        var process = new FakeBackupProcess();
        var (service, status) = NewService(process, missing);

        var attempt = await service.RunAsync(CancellationToken.None);

        Assert.Equal(BackupOutcome.Failed, attempt.Outcome);
        Assert.Contains(missing, attempt.Error);
        Assert.Same(attempt, status.Last);
        Assert.Null(process.LastStart);
    }

    [Fact]
    public async Task RunAsync_BackupPathIsAFile_FailsNamingThePathAndNeverInvokesTheProcess()
    {
        var filePath = Path.Combine(_dir, "not-a-folder");
        await File.WriteAllTextAsync(filePath, "x");
        var process = new FakeBackupProcess();
        var (service, status) = NewService(process, filePath);

        var attempt = await service.RunAsync(CancellationToken.None);

        Assert.Equal(BackupOutcome.Failed, attempt.Outcome);
        Assert.Contains(filePath, attempt.Error);
        Assert.Same(attempt, status.Last);
        Assert.Null(process.LastStart);
    }

    [Fact]
    public async Task RunAsync_FinalNameAlreadyExists_FailsAndLeavesTheExistingCopyIntact()
    {
        var existingBytes = new byte[] { 9, 9 };
        var finalPath = Path.Combine(_dir, BackupNames.Final(Now));
        await File.WriteAllBytesAsync(finalPath, existingBytes);

        var process = new FakeBackupProcess { ExitCode = 0, Stdout = [1] };
        var (service, status) = NewService(process, _dir);

        var attempt = await service.RunAsync(CancellationToken.None);

        Assert.Equal(BackupOutcome.Failed, attempt.Outcome);
        Assert.Contains(finalPath, attempt.Error);
        Assert.Same(attempt, status.Last);
        Assert.Null(process.LastStart);
        Assert.Equal(existingBytes, await File.ReadAllBytesAsync(finalPath));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task RunAsync_ProcessThrows_FailsWithTheExceptionsMessageAndLeavesNoPartial()
    {
        var process = new FakeBackupProcess { Throws = new InvalidOperationException("pg_dump not found") };
        var (service, status) = NewService(process, _dir);

        var attempt = await service.RunAsync(CancellationToken.None);

        Assert.Equal(BackupOutcome.Failed, attempt.Outcome);
        Assert.Contains("pg_dump not found", attempt.Error);
        Assert.Same(attempt, status.Last);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    private static (BackupService Service, BackupStatus Status) NewService(
        FakeBackupProcess process, string path, TimeSpan? timeout = null)
    {
        var status = new BackupStatus();
        var service = new BackupService(
            Options.Create(new HatchOptions { BackupPath = path }),
            Configuration(),
            process,
            status,
            new FakeTimeProvider(Now),
            timeout);

        return (service, status);
    }

    // db-test, never localhost: HA-1548's brief notes that under Docker the
    // database host is `db`, so a connection string that happened to work
    // against a developer's own machine would say nothing about the one
    // pg_dump is actually handed in the container.
    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Hatch"] = "Host=db-test;Port=5433;Username=hatchuser;Password=s3cret;Database=hatchdb",
            })
            .Build();

    private sealed class FakeBackupProcess : IBackupProcess
    {
        public byte[] Stdout { get; set; } = [];
        public int ExitCode { get; set; }
        public string Stderr { get; set; } = "";
        public Exception? Throws { get; set; }
        public bool Hangs { get; set; }

        public BackupProcessStart? LastStart { get; private set; }

        public async Task<BackupProcessResult> RunAsync(BackupProcessStart start, Stream stdout, CancellationToken ct)
        {
            LastStart = start;

            if (Throws is { } ex) throw ex;
            if (Hangs) await Task.Delay(Timeout.Infinite, ct);

            await stdout.WriteAsync(Stdout, ct);
            return new BackupProcessResult(ExitCode, Stderr);
        }
    }
}

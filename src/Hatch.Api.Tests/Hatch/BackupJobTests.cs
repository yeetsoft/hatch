using Hatch.Api.Modules.Hatch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="BackupJob"/> over a real <see cref="BackupService"/> and a
/// <see cref="FakeBackupProcess"/> - Quartz never involved, the same split
/// <c>ReflectionCadenceJobTests</c> uses: the job's <c>Execute</c> only hands
/// its token to <see cref="BackupJob.RunAsync"/>.
/// </summary>
public sealed class BackupJobTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 3, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hatch-backup-job-" + Guid.NewGuid().ToString("N"));

    public BackupJobTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task RunAsync_DumpFails_DoesNotThrowAndWarns()
    {
        var (job, logger) = NewJob(new FakeBackupProcess { ExitCode = 1, Stderr = "pg_dump: boom" }, _dir);

        await job.RunAsync(CancellationToken.None);

        var line = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains("pg_dump: boom", line.Message);
    }

    [Fact]
    public async Task RunAsync_ProcessThrows_DoesNotThrow()
    {
        var (job, logger) = NewJob(new FakeBackupProcess { Throws = new InvalidOperationException("cannot start") }, _dir);

        await job.RunAsync(CancellationToken.None);

        var line = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
    }

    [Fact]
    public async Task RunAsync_Ok_LogsTheFileAndItsSize()
    {
        var (job, logger) = NewJob(new FakeBackupProcess { Stdout = "dump-bytes"u8.ToArray() }, _dir);

        await job.RunAsync(CancellationToken.None);

        var line = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains(BackupNames.Final(Now), line.Message);
        Assert.Contains("10 bytes", line.Message);
    }

    [Fact]
    public void Interval_IsTwentyFourHours()
    {
        var (job, _) = NewJob(new FakeBackupProcess(), _dir);

        Assert.Equal(TimeSpan.FromHours(24), job.Interval);
    }

    [Fact]
    public void ServesTheHouse_IsFalse()
    {
        var (job, _) = NewJob(new FakeBackupProcess(), _dir);

        Assert.False(job.ServesTheHouse);
    }

    [Fact]
    public async Task RunAsync_NotConfigured_LogsOnTheFirstCallAndNotTheSecond()
    {
        var (job, logger) = NewJob(new FakeBackupProcess(), "");

        await job.RunAsync(CancellationToken.None);
        await job.RunAsync(CancellationToken.None);

        var line = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Equal("backups are not configured", line.Message);
    }

    private static (BackupJob Job, CapturingLogger Logger) NewJob(FakeBackupProcess process, string path)
    {
        var status = new BackupStatus();
        var service = new BackupService(
            Options.Create(new HatchOptions { BackupPath = path }),
            Configuration(),
            process,
            status,
            new FakeTimeProvider(Now),
            NullLogger<BackupService>.Instance);

        var logger = new CapturingLogger();
        return (new BackupJob(service, status, logger), logger);
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Hatch"] = "Host=db-test;Port=5433;Username=hatchuser;Password=s3cret;Database=hatchdb",
            })
            .Build();

    private sealed class CapturingLogger : ILogger<BackupJob>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception)));
    }

    private sealed class FakeBackupProcess : IBackupProcess
    {
        public byte[] Stdout { get; set; } = [];
        public int ExitCode { get; set; }
        public string Stderr { get; set; } = "";
        public Exception? Throws { get; set; }

        public async Task<BackupProcessResult> RunAsync(BackupProcessStart start, Stream stdout, CancellationToken ct)
        {
            if (Throws is { } ex) throw ex;

            await stdout.WriteAsync(Stdout, ct);
            return new BackupProcessResult(ExitCode, Stderr);
        }
    }
}

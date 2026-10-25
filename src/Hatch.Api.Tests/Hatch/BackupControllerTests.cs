using System.Reflection;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <c>GET /api/hatch/backup</c> and <c>POST /api/hatch/backup/run</c> - the
/// folder read, the gate around a run, and the person-only refusal, against a
/// <see cref="BackupService"/> wired the same way <see cref="BackupServiceTests"/>
/// wires it, never a real <c>pg_dump</c>.
/// </summary>
public sealed class BackupControllerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 3, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hatch-backup-controller-" + Guid.NewGuid().ToString("N"));

    public BackupControllerTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void GetBackup_NoPath_AnswersNotConfigured()
    {
        var (controller, _, _) = NewController(path: "");

        var dto = Value(controller.GetBackup());

        Assert.False(dto.Configured);
        Assert.Null(dto.Path);
        Assert.Empty(dto.Copies);
        Assert.Equal("never", dto.LastOutcome);
        Assert.False(dto.FolderReadable);
    }

    [Fact]
    public async Task GetBackup_ThreeCopiesAndAStranger_ListsOnlyTheThreeNewestFirst()
    {
        var oldest = Now.AddDays(-2);
        var middle = Now.AddDays(-1);
        var newest = Now;
        await Seed(oldest);
        await Seed(middle);
        await Seed(newest);
        await File.WriteAllTextAsync(Path.Combine(_dir, "notes.txt"), "not a backup");

        var (controller, _, _) = NewController();

        var dto = Value(controller.GetBackup());

        Assert.Equal(
            [BackupNames.Final(newest), BackupNames.Final(middle), BackupNames.Final(oldest)],
            dto.Copies.Select(c => c.Name));
        Assert.Equal(newest, dto.LastSuccessfulAt);
        Assert.DoesNotContain("notes.txt", dto.Copies.Select(c => c.Name));
    }

    [Fact]
    public async Task GetBackup_AfterTheNewestFileIsDeleted_LastSuccessfulAtFallsBackToThePrevious()
    {
        var older = Now.AddDays(-1);
        var newer = Now;
        await Seed(older);
        await Seed(newer);
        var (controller, _, _) = NewController();

        File.Delete(Path.Combine(_dir, BackupNames.Final(newer)));
        var dto = Value(controller.GetBackup());

        Assert.Equal(older, dto.LastSuccessfulAt);
    }

    [Fact]
    public void GetBackup_PathDoesNotExist_FolderUnreadableWithAProblem()
    {
        var missing = Path.Combine(_dir, "does-not-exist");
        var (controller, _, _) = NewController(path: missing);

        var dto = Value(controller.GetBackup());

        Assert.False(dto.FolderReadable);
        Assert.NotNull(dto.FolderProblem);
    }

    [Fact]
    public async Task GetBackup_AfterAFailedRun_ReportsFailedButKeepsTheOldCopyAsLastSuccessful()
    {
        var older = Now.AddDays(-1);
        await Seed(older);
        var (controller, service, _) = NewController(process: new FakeBackupProcess { ExitCode = 1, Stderr = "boom" });

        await service.RunAsync(CancellationToken.None);
        var dto = Value(controller.GetBackup());

        Assert.Equal(BackupOutcome.Failed, dto.LastOutcome);
        Assert.NotNull(dto.LastError);
        Assert.Equal(older, dto.LastSuccessfulAt);
    }

    [Fact]
    public async Task PostBackupRun_Ok_Writes200AndANewFile_AndItShowsUpInCopies()
    {
        var (controller, _, _) = NewController(process: new FakeBackupProcess { Stdout = [1, 2, 3], ExitCode = 0 });

        var result = await controller.PostBackupRun(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<BackupStatusDto>(ok.Value);
        Assert.Equal(BackupOutcome.Ok, dto.LastOutcome);
        var file = Assert.Single(Directory.GetFiles(_dir));
        Assert.Equal(BackupNames.Final(Now), Path.GetFileName(file));
        Assert.Contains(BackupNames.Final(Now), dto.Copies.Select(c => c.Name));
    }

    [Fact]
    public async Task PostBackupRun_NotConfigured_Is400AndNeverStartsTheProcess()
    {
        var process = new FakeBackupProcess();
        var (controller, _, _) = NewController(path: "", process: process);

        var result = await controller.PostBackupRun(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(0, process.Runs);
    }

    [Fact]
    public async Task PostBackupRun_WhileARunHoldsTheGate_Is409AndStartsNoSecondFile()
    {
        var process = new FakeBackupProcess { Hangs = true };
        var (controller, service, _) = NewController(process: process);
        using var first = new CancellationTokenSource();
        var firstRun = service.RunAsync(first.Token);
        await process.Started.Task;

        var result = await controller.PostBackupRun(CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(1, process.Runs);
        Assert.Empty(Directory.GetFiles(_dir, "*.sql"));

        await first.CancelAsync();
        await firstRun;
    }

    [Fact]
    public async Task PostBackupRun_WithAnApiKey_Is403AndTouchesNothing()
    {
        var process = new FakeBackupProcess();
        var (controller, _, caller) = NewController(process: process);
        caller.ApiKey = new EfApiKey
        {
            Name = "hatch",
            Hash = [1],
            Prefix = "hatch_ak_x",
            Scopes = [ApiKeyScopes.Hatch],
            CreatedAt = Now,
        };

        var result = await controller.PostBackupRun(CancellationToken.None);

        var refused = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal(0, process.Runs);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task PostBackupRun_WithAKeylessRunner_Is403AndTouchesNothing()
    {
        var process = new FakeBackupProcess();
        var (controller, _, caller) = NewController(process: process);
        caller.Local = new Actor(ActorKind.Key, Guid.NewGuid(), "host:/src");

        var result = await controller.PostBackupRun(CancellationToken.None);

        var refused = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal(0, process.Runs);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void PostBackupRun_CarriesAPersonOnlyRoleWithNoAcceptScope()
    {
        var method = typeof(BackupController).GetMethod(nameof(BackupController.PostBackupRun))!;
        var attribute = method.GetCustomAttribute<RequireRoleAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(PersonRole.User, attribute!.Minimum);
        Assert.Null(attribute.AcceptScope);
    }

    [Fact]
    public void GetBackup_AcceptsTheHatchScope()
    {
        var method = typeof(BackupController).GetMethod(nameof(BackupController.GetBackup))!;
        var attribute = method.GetCustomAttribute<RequireRoleAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(ApiKeyScopes.Hatch, attribute!.AcceptScope);
    }

    private Task Seed(DateTimeOffset at) =>
        File.WriteAllBytesAsync(Path.Combine(_dir, BackupNames.Final(at)), [1, 2, 3]);

    private (BackupController Controller, BackupService Service, StubCallerIdentity Caller) NewController(
        string? path = null, FakeBackupProcess? process = null)
    {
        var status = new BackupStatus();
        var options = Options.Create(new HatchOptions { BackupPath = path ?? _dir });
        var service = new BackupService(
            options, Configuration(), process ?? new FakeBackupProcess(), status,
            new FakeTimeProvider(Now), NullLogger<BackupService>.Instance);
        var caller = new StubCallerIdentity();
        var controller = new BackupController(service, status, options, caller);

        return (controller, service, caller);
    }

    // db-test, never localhost - the same reason BackupServiceTests gives: the
    // string pg_dump is actually handed under Docker, not whatever happens to
    // work on a developer's own machine.
    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Hatch"] = "Host=db-test;Port=5433;Username=hatchuser;Password=s3cret;Database=hatchdb",
            })
            .Build();

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");

    private sealed class FakeBackupProcess : IBackupProcess
    {
        public byte[] Stdout { get; set; } = [];
        public int ExitCode { get; set; }
        public string Stderr { get; set; } = "";
        public bool Hangs { get; set; }

        public TaskCompletionSource Started = new();
        public int Runs { get; private set; }

        public async Task<BackupProcessResult> RunAsync(BackupProcessStart start, Stream stdout, CancellationToken ct)
        {
            Runs++;
            Started.TrySetResult();

            if (Hangs) await Task.Delay(Timeout.Infinite, ct);

            await stdout.WriteAsync(Stdout, ct);
            return new BackupProcessResult(ExitCode, Stderr);
        }
    }
}

using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="PostgresGooseLock"/> against a real database, because in-memory
/// EF has no lock to take. <see cref="GooseJobTests"/> proves the job writes
/// nothing without the lock; this proves the lock is the one that keeps two
/// real jobs on two contexts apart.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class GooseJobLockTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task TwoJobsStartedTogether_OnOneDatabase_RecordOneRun()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");
        var connectionString = await HatchDatabase.PrepareAsync();

        await using var first = Open(connectionString);
        await using var second = Open(connectionString);

        // Whoever takes the lock enters the check and waits there, so the other
        // has to find the lock held rather than win a race it would have lost anyway.
        var gate = new BlockingCheck();
        var lockbox = new PostgresGooseLock();

        var a = Task.Run(() => Job(first, gate, lockbox).RunAsync(CancellationToken.None));
        var b = Task.Run(() => Job(second, gate, lockbox).RunAsync(CancellationToken.None));

        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var loser = await Task.WhenAny(a, b).WaitAsync(TimeSpan.FromSeconds(30));
        await loser;

        gate.Release.SetResult();
        await Task.WhenAll(a, b);

        await using var read = Open(connectionString);
        var run = Assert.Single(await read.GooseRuns.ToListAsync());
        Assert.Equal(1, run.Sweeps);
        Assert.Equal(1, gate.Entries);
    }

    [SkippableFact]
    public async Task TheLock_IsFreeAgainOnceAHolderIsDisposed()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");
        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = Open(connectionString);
        var lockbox = new PostgresGooseLock();

        var held = await lockbox.TryAcquireAsync(db, CancellationToken.None);
        Assert.NotNull(held);
        Assert.Null(await lockbox.TryAcquireAsync(db, CancellationToken.None));

        await held.DisposeAsync();

        var again = await lockbox.TryAcquireAsync(db, CancellationToken.None);
        Assert.NotNull(again);
        await again.DisposeAsync();
    }

    private static HatchContext Open(string connectionString) =>
        new(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

    private static GooseJob Job(HatchContext db, IGooseCheck check, IGooseLock lockbox) =>
        new(db, [check], lockbox, Options.Create(new HatchOptions()), new FakeTimeProvider(Now), NullLogger<GooseJob>.Instance);

    private sealed class BlockingCheck : IGooseCheck
    {
        private int entries;

        public string Name => "blocking";

        public int Entries => entries;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GooseObservation> RunAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref entries);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new GooseObservation("", "fine");
        }
    }
}

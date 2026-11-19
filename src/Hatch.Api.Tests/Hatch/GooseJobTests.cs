using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="GooseJob"/> - the sweep that records itself as a run, and folds a
/// sweep that concluded what the last one did into that run. The pins are the
/// ones the record cannot recover from getting wrong: a quiet board must cost
/// one row, a changed conclusion must cost a new one, and what a check merely
/// looked at must never be the difference.
/// </summary>
public class GooseJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_WithNoChecks_StillRecordsARun()
    {
        var h = NewHarness();

        await h.Job().RunAsync(CancellationToken.None);

        var run = Assert.Single(await h.Db.GooseRuns.ToListAsync());
        Assert.Equal(1, run.Sweeps);
        Assert.Equal(Now, run.FirstRanAt);
        Assert.Equal(Now, run.LastRanAt);
        Assert.Empty(GooseEntry.Read(run.Entries));
    }

    [Fact]
    public async Task RunAsync_SixtyIdenticalSweeps_LeaveOneRowThatKeepsFirstLastCountAndLongest()
    {
        var h = NewHarness();
        var check = new StubCheck("quiet", h.Time) { Looked = "3 issues", Concluded = "nothing wrong" };
        var job = h.Job(check);

        for (var i = 0; i < 60; i++)
        {
            // Set, not advanced: the check moves the clock by its own duration.
            h.Time.SetUtcNow(Now.AddSeconds(i * 60));
            check.TakesMs = i == 17 ? 900 : 5;
            await job.RunAsync(CancellationToken.None);
        }

        var run = Assert.Single(await h.Db.GooseRuns.ToListAsync());
        Assert.Equal(60, run.Sweeps);
        Assert.Equal(Now, run.FirstRanAt);
        Assert.Equal(Now.AddSeconds(59 * 60), run.LastRanAt);
        Assert.Equal(900, run.LongestMs);
    }

    [Fact]
    public async Task RunAsync_AChangedConclusion_AddsARow()
    {
        var h = NewHarness();
        var check = new StubCheck("stuck", h.Time) { Concluded = "none stuck" };
        var job = h.Job(check);

        await job.RunAsync(CancellationToken.None);
        h.Time.Advance(TimeSpan.FromSeconds(60));
        check.Concluded = "HA-1 is stuck";
        await job.RunAsync(CancellationToken.None);

        var runs = await h.Db.GooseRuns.OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(2, runs.Count);
        Assert.All(runs, r => Assert.Equal(1, r.Sweeps));
        Assert.Equal("HA-1 is stuck", Assert.Single(GooseEntry.Read(runs[1].Entries)).Concluded);
    }

    [Fact]
    public async Task RunAsync_ChangedLookedAloneAndSlowerTimings_DoNotAddARow()
    {
        var h = NewHarness();
        var check = new StubCheck("stuck", h.Time) { Looked = "3 issues", Concluded = "none stuck", TakesMs = 5 };
        var job = h.Job(check);

        await job.RunAsync(CancellationToken.None);
        check.Looked = "4 issues";
        check.TakesMs = 5000;
        await job.RunAsync(CancellationToken.None);

        var run = Assert.Single(await h.Db.GooseRuns.ToListAsync());
        Assert.Equal(2, run.Sweeps);

        // The run shows the latest sweep's look, not the first's.
        Assert.Equal("4 issues", Assert.Single(GooseEntry.Read(run.Entries)).Looked);
    }

    [Fact]
    public async Task RunAsync_AfterAChange_FoldsIntoTheNewestRunNotTheOldOne()
    {
        var h = NewHarness();
        var check = new StubCheck("stuck", h.Time) { Concluded = "a" };
        var job = h.Job(check);

        await job.RunAsync(CancellationToken.None);
        h.Time.Advance(TimeSpan.FromSeconds(60));
        check.Concluded = "b";
        await job.RunAsync(CancellationToken.None);
        h.Time.Advance(TimeSpan.FromSeconds(60));
        check.Concluded = "a";
        await job.RunAsync(CancellationToken.None);

        // a, b, a is three runs: the fold is against the newest only, so a
        // return to an earlier answer is a new fact, not a repeat.
        Assert.Equal(3, await h.Db.GooseRuns.CountAsync());
    }

    [Fact]
    public async Task RunAsync_ACheckThatThrows_IsMarkedFailedAndTheNextStillRuns()
    {
        var h = NewHarness();
        var bad = new StubCheck("bad", h.Time) { Throws = new InvalidOperationException("the board was unreadable") };
        var good = new StubCheck("good", h.Time) { Looked = "1 issue", Concluded = "fine" };

        await h.Job(bad, good).RunAsync(CancellationToken.None);

        var entries = GooseEntry.Read(Assert.Single(await h.Db.GooseRuns.ToListAsync()).Entries);
        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].Failed);
        Assert.Equal("the board was unreadable", entries[0].Error);
        Assert.False(entries[1].Failed);
        Assert.Equal("fine", entries[1].Concluded);
        Assert.Equal(1, good.Runs);
    }

    [Fact]
    public async Task RunAsync_ACheckThatKeepsThrowingTheSameError_FoldsAndANewErrorDoesNot()
    {
        var h = NewHarness();
        var check = new StubCheck("bad", h.Time) { Throws = new InvalidOperationException("one") };
        var job = h.Job(check);

        await job.RunAsync(CancellationToken.None);
        await job.RunAsync(CancellationToken.None);
        Assert.Equal(1, await h.Db.GooseRuns.CountAsync());

        check.Throws = new InvalidOperationException("two");
        await job.RunAsync(CancellationToken.None);
        Assert.Equal(2, await h.Db.GooseRuns.CountAsync());
    }

    [Fact]
    public async Task RunAsync_RunsChecksInRegistrationOrder()
    {
        var h = NewHarness();
        var order = new List<string>();
        var checks = new[] { "c", "a", "b" }.Select(n => new StubCheck(n, h.Time) { OnRun = order.Add }).ToArray();

        await h.Job(checks).RunAsync(CancellationToken.None);

        Assert.Equal(["c", "a", "b"], order);
        var entries = GooseEntry.Read(Assert.Single(await h.Db.GooseRuns.ToListAsync()).Entries);
        Assert.Equal(["c", "a", "b"], entries.Select(e => e.Check));
    }

    [Fact]
    public async Task RunAsync_ACheckAddedBetweenSweeps_IsADifferentConclusion()
    {
        var h = NewHarness();

        await h.Job().RunAsync(CancellationToken.None);
        await h.Job(new StubCheck("new", h.Time)).RunAsync(CancellationToken.None);

        Assert.Equal(2, await h.Db.GooseRuns.CountAsync());
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(-5, 60)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    public void Interval_FallsBackToSixtyWhenZeroOrNegative(int configured, int expectedSeconds)
    {
        var job = NewHarness(new HatchOptions { GooseIntervalSeconds = configured }).Job();

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), job.Interval);
    }

    [Fact]
    public void Defaults_AreSixtySecondsAndSevenDays()
    {
        Assert.Equal(60, GooseSettings.IntervalSeconds(new HatchOptions()));
        Assert.Equal(7, GooseSettings.KeepDays(new HatchOptions()));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 7)]
    [InlineData(3, 3)]
    public void KeepDays_ZeroKeepsEverythingAndNegativeFallsBackToSeven(int configured, int expected)
    {
        Assert.Equal(expected, GooseSettings.KeepDays(new HatchOptions { GooseKeepDays = configured }));
    }

    [Fact]
    public async Task RunAsync_DeletesRunsWhoseLastSweepIsOlderThanTheKeepWindow()
    {
        var h = NewHarness(new HatchOptions { GooseKeepDays = 7 });
        h.Db.GooseRuns.AddRange(
            OldRun(Now.AddDays(-8)),
            OldRun(Now.AddDays(-7).AddMinutes(1)));
        await h.Db.SaveChangesAsync();

        await h.Job().RunAsync(CancellationToken.None);

        var lasts = await h.Db.GooseRuns.Select(r => r.LastRanAt).ToListAsync();
        Assert.Equal(2, lasts.Count);
        Assert.DoesNotContain(Now.AddDays(-8), lasts);
    }

    [Fact]
    public async Task RunAsync_WithKeepDaysZero_KeepsEverything()
    {
        var h = NewHarness(new HatchOptions { GooseKeepDays = 0 });
        h.Db.GooseRuns.Add(OldRun(Now.AddDays(-4000)));
        await h.Db.SaveChangesAsync();

        await h.Job().RunAsync(CancellationToken.None);

        Assert.Equal(2, await h.Db.GooseRuns.CountAsync());
    }

    [Fact]
    public async Task RunAsync_WithNegativeKeepDays_PrunesAtTheDefaultOfSeven()
    {
        var h = NewHarness(new HatchOptions { GooseKeepDays = -3 });
        h.Db.GooseRuns.AddRange(OldRun(Now.AddDays(-8)), OldRun(Now.AddDays(-6)));
        await h.Db.SaveChangesAsync();

        await h.Job().RunAsync(CancellationToken.None);

        Assert.Equal(2, await h.Db.GooseRuns.CountAsync());
    }

    [Fact]
    public async Task RunAsync_WhileAnotherSweepHoldsTheLock_WritesNothingAndRunsNoCheck()
    {
        var h = NewHarness();
        var check = new StubCheck("quiet", h.Time);
        await using var other = await h.Lock.TryAcquireAsync(h.Db, CancellationToken.None);
        Assert.NotNull(other);

        await h.Job(check).RunAsync(CancellationToken.None);

        Assert.Empty(await h.Db.GooseRuns.ToListAsync());
        Assert.Equal(0, check.Runs);
    }

    [Fact]
    public async Task RunAsync_ReleasesTheLockWhenItIsDone()
    {
        var h = NewHarness();

        await h.Job().RunAsync(CancellationToken.None);
        await h.Job().RunAsync(CancellationToken.None);

        Assert.Equal(1, await h.Db.GooseRuns.CountAsync());
        Assert.Equal(2, (await h.Db.GooseRuns.SingleAsync()).Sweeps);
    }

    [Fact]
    public async Task RunAsync_WhenTheLockCannotBeTaken_DoesNotThrow()
    {
        var h = NewHarness();
        h.Lock.Throws = new InvalidOperationException("the database is down");

        await h.Job().RunAsync(CancellationToken.None);

        Assert.Empty(await h.Db.GooseRuns.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_WhenTheDatabaseFails_DoesNotThrow()
    {
        var h = NewHarness();
        var job = h.Job();
        await h.Db.DisposeAsync();

        await job.RunAsync(CancellationToken.None);
    }

    private static EfHatchGooseRun OldRun(DateTimeOffset last) => new()
    {
        FirstRanAt = last,
        LastRanAt = last,
        Sweeps = 1,
        Entries = "[{\"check\":\"old\",\"looked\":\"\",\"concluded\":\"old\",\"failed\":false,\"error\":null}]",
    };

    private static Harness NewHarness(HatchOptions? options = null) => new(options ?? new HatchOptions());

    private sealed class Harness(HatchOptions options)
    {
        public HatchContext Db { get; } = new(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        public FakeTimeProvider Time { get; } = new(Now);

        public InProcessLock Lock { get; } = new();

        public GooseJob Job(params IGooseCheck[] checks) =>
            new(Db, checks, Lock, Options.Create(options), Time, NullLogger<GooseJob>.Instance);
    }

    /// <summary>An in-process stand-in for the advisory lock, which in-memory EF has no way to take.</summary>
    private sealed class InProcessLock : IGooseLock
    {
        private readonly SemaphoreSlim gate = new(1, 1);

        public Exception? Throws { get; set; }

        public async Task<IAsyncDisposable?> TryAcquireAsync(HatchContext db, CancellationToken ct)
        {
            if (Throws is not null) throw Throws;
            return await gate.WaitAsync(0, ct) ? new Release(gate) : null;
        }

        private sealed class Release(SemaphoreSlim gate) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                gate.Release();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class StubCheck(string name, FakeTimeProvider time) : IGooseCheck
    {
        public string Name => name;

        public string Looked { get; set; } = "";

        public string Concluded { get; set; } = "";

        public int TakesMs { get; set; }

        public Exception? Throws { get; set; }

        public Action<string>? OnRun { get; set; }

        public int Runs { get; private set; }

        public Task<GooseObservation> RunAsync(CancellationToken ct)
        {
            Runs++;
            OnRun?.Invoke(name);
            time.Advance(TimeSpan.FromMilliseconds(TakesMs));
            if (Throws is not null) throw Throws;
            return Task.FromResult(new GooseObservation(Looked, Concluded));
        }
    }
}

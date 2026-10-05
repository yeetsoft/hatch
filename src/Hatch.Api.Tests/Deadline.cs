using System.Diagnostics;

namespace Hatch.Api.Tests;

/// <summary>
/// Races one case's phases against a single shared budget, so a stalled phase
/// fails that phase alone - naming it, how long it had left, the log captured
/// so far, and the thread pool's own counters - rather than taking the whole
/// test host down with it.
/// </summary>
internal sealed class Deadline(TimeSpan budget, Func<string> logsSoFar)
{
    private readonly Stopwatch elapsed = Stopwatch.StartNew();

    public async Task<T> BoundAsync<T>(string phase, Func<Task<T>> work)
    {
        var left = budget - elapsed.Elapsed;
        if (left <= TimeSpan.Zero) throw new DeadlineExceededException(phase, TimeSpan.Zero, logsSoFar());

        var workTask = work();
        using var cts = new CancellationTokenSource();
        var winner = await Task.WhenAny(workTask, Task.Delay(left, cts.Token));

        if (winner == workTask) { cts.Cancel(); return await workTask; }

        // The clamp still wins if this is itself the thing that is stuck;
        // this only stops a *finished* workTask from being reported as an
        // unobserved exception once this method has already thrown.
        _ = workTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        throw new DeadlineExceededException(phase, left, logsSoFar());
    }

    public Task BoundAsync(string phase, Func<Task> work) =>
        BoundAsync(phase, async () => { await work(); return true; });
}

internal sealed class DeadlineExceededException(string phase, TimeSpan budget, string logsSoFar)
    : TimeoutException(
        $"'{phase}' did not finish inside its {budget.TotalSeconds:0.0}s budget. " +
        $"ThreadPool: {ThreadPool.ThreadCount} threads, {ThreadPool.PendingWorkItemCount} pending. " +
        $"Logged so far:{Environment.NewLine}{logsSoFar}");

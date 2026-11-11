using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>
/// Drives a fake clock for a task that waits on it.
/// </summary>
public static class ClockDriver
{
    /// <summary>
    /// Advances <paramref name="clock"/> by <paramref name="step"/> every few
    /// real milliseconds until <paramref name="task"/> finishes or a real-time
    /// <paramref name="deadline"/> (30s by default) passes, then waits on the
    /// task for five more seconds so a miss is a <see cref="TimeoutException"/>
    /// rather than a hung test host. Bounded by real time, not by ticks: the
    /// timer under test only exists once the task has run up to its wait, and
    /// on a loaded machine that takes longer than any fixed count of ticks.
    /// </summary>
    public static async Task DriveAsync(FakeTimeProvider clock, Task task, TimeSpan step, TimeSpan? deadline = null)
    {
        var limit = deadline ?? TimeSpan.FromSeconds(30);
        var elapsed = Stopwatch.StartNew();

        while (!task.IsCompleted && elapsed.Elapsed < limit)
        {
            await Task.Delay(5);
            clock.Advance(step);
        }

        await task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}

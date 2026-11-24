using Quartz;

namespace Hatch.Api.Tests;

/// <summary>
/// Waits out the first firings of the four jobs a host starts with, so a test
/// that boots the real host can stop it with nothing in flight.
/// </summary>
internal static class FirstFirings
{
    /// <summary>
    /// Everything ReflectionCadence, Backup, Goose and ChatSend are registered under -
    /// the four jobs, and so the four triggers, this group is left holding.
    /// </summary>
    internal const string JobGroup = "Hatch.Api";

    /// <summary>
    /// ReflectionCadence's, Backup's, the goose's and ChatSend's triggers start
    /// now, so their first firings land inside the few hundred milliseconds
    /// between /health/ready answering and this host being disposed. Disposed
    /// mid-firing, Quartz fails to build the job from a service provider that
    /// is already gone and says so at Error - the test's own teardown, not a
    /// fault in startup. So the host is not stopped until all four firings
    /// have run their course; the next is five seconds away for ChatSend, and
    /// a minute, fifteen minutes and a day away for the other three. Backup
    /// with no path set is a no-op, but it is still a firing to be caught
    /// mid-construction. Bounded at ten seconds and then let go, so a firing that
    /// never comes is not this case's failure to report.
    ///
    /// <para>"Fired" and "executing" are not the same instant: Quartz records
    /// the previous fire time first, then builds the job, and only lists it
    /// among the currently executing jobs once it is running. A single sample
    /// of "fired and nothing executing" can land in that gap - and did, in CI,
    /// with the host disposed underneath the job's construction. So it takes
    /// several samples in a row, spanning far longer than the gap, before the
    /// firing counts as over.</para>
    /// </summary>
    internal static async Task SettleAsync(IScheduler scheduler)
    {
        const int IdleSamples = 10;

        var triggers = new[]
        {
            new TriggerKey("ReflectionCadence_Trigger", JobGroup),
            new TriggerKey("Backup_Trigger", JobGroup),
            new TriggerKey("Goose_Trigger", JobGroup),
            new TriggerKey("ChatSend_Trigger", JobGroup),
        };
        var until = DateTimeOffset.UtcNow.AddSeconds(10);
        var idle = 0;

        while (DateTimeOffset.UtcNow < until)
        {
            var fired = true;
            foreach (var trigger in triggers)
            {
                fired &= (await scheduler.GetTrigger(trigger))?.GetPreviousFireTimeUtc() is not null;
            }

            idle = fired && (await scheduler.GetCurrentlyExecutingJobs()).Count == 0 ? idle + 1 : 0;
            if (idle >= IdleSamples) return;
            await Task.Delay(50);
        }
    }
}

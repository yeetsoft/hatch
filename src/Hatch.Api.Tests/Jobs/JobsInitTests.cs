using Hatch.Api.Jobs;
using Quartz;
using Quartz.Impl;

namespace Hatch.Api.Tests.Jobs;

/// <summary>
/// Against a real in-memory Quartz scheduler rather than a recording stub.
/// IScheduler is forty-odd members, and a stub of it would assert that
/// WireUpJobs made certain <em>calls</em>; what matters is the state those
/// calls leave behind - whether a trigger exists, and on what interval.
/// </summary>
[Collection(QuartzSchedulerCollection.Name)]
public class JobsInitTests : IAsyncLifetime
{
    /// <summary>Every scheduler this test built, shut down when it ends - see <see cref="NewSchedulerAsync"/>.</summary>
    private readonly List<IScheduler> schedulers = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var scheduler in schedulers)
            await scheduler.Shutdown();
    }

    /// <summary>
    /// A stand-in for a real job. JobsInit reads Name, Group and Interval and
    /// nothing else, so a fake carrying those is the whole job as far as it is
    /// concerned - and using one keeps this test off the real jobs'
    /// constructor dependencies.
    /// </summary>
    private class FakeJob(string name, TimeSpan interval) : IAppJob
    {
        public string Name { get; } = name;

        public string Group => "Hatch.Api.Tests";

        public TimeSpan Interval { get; } = interval;

        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    /// <summary>
    /// A scheduler of its own per test, keyed on a fresh name: StdSchedulerFactory
    /// hands back the same instance for the same name, and Quartz's default job
    /// store is in-memory, so this is a real scheduler with nothing behind it.
    /// Never started - registration is what is under test, not execution.
    ///
    /// <para>Never started is not the same as never running, though: its
    /// scheduler thread is up from construction, parked in a
    /// <c>Monitor.Wait</c> inside a thread-pool task until Shutdown() says
    /// otherwise. Left alone it holds that pool thread for the life of the
    /// process, and this class would otherwise leave one of them to every test
    /// that runs after it - MinimalStartupTests, in this collection, first.</para>
    /// </summary>
    private async Task<IScheduler> NewSchedulerAsync()
    {
        var scheduler = await new StdSchedulerFactory(new System.Collections.Specialized.NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = $"JobsInitTests-{Guid.NewGuid():N}",
        }).GetScheduler();

        schedulers.Add(scheduler);
        return scheduler;
    }

    [Fact]
    public async Task EveryJobIsScheduledOnItsOwnInterval()
    {
        var scheduler = await NewSchedulerAsync();
        var jobs = new[]
        {
            new FakeJob("FirstJob", TimeSpan.FromMinutes(1)),
            new FakeJob("SecondJob", TimeSpan.FromMinutes(15)),
        };
        var init = new JobsInit(scheduler, jobs);

        // Twice: every replica calls this on every boot, so `replace: true`
        // has to make the second a no-op rather than an
        // ObjectAlreadyExistsException.
        await init.WireUpJobs();
        await init.WireUpJobs();

        foreach (var job in jobs)
        {
            Assert.True(await scheduler.CheckExists(new JobKey(job.Name, job.Group)));

            var trigger = Assert.Single(await scheduler.GetTriggersOfJob(new JobKey(job.Name, job.Group)));
            Assert.Equal(job.Interval, Assert.IsAssignableFrom<ISimpleTrigger>(trigger).RepeatInterval);
        }
    }
}

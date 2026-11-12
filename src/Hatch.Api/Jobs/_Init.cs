using Quartz;

namespace Hatch.Api.Jobs;

public interface IAppJob : IJob
{
    string Name { get; }
    string Group { get; }
    TimeSpan Interval { get; }
}

public class JobsInit(IScheduler scheduler, IEnumerable<IAppJob> jobs)
{
    /// <summary>
    /// Registers every recurring job, replacing whatever is already there.
    /// Quartz clustering handles execution (only one node in the cluster runs
    /// a given firing), not registration - every replica calls this on every
    /// boot, so `replace: true` is load-bearing: without it, two replicas
    /// racing between "does this job exist" and "create it" both win the
    /// race and the loser throws ObjectAlreadyExistsException and
    /// crash-loops.
    /// </summary>
    public async Task WireUpJobs()
    {
        foreach (var j in jobs)
        {
            var qj = JobBuilder.Create(j.GetType())
                .WithIdentity(j.Name, j.Group)
                .Build();

            var qt = TriggerBuilder.Create()
                .WithIdentity($"{j.Name}_Trigger", j.Group)
                .WithSimpleSchedule(s => s
                    .WithInterval(j.Interval)
                    .RepeatForever())
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(qj, [qt], replace: true);
        }
    }
}

using Hatch.Api.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The goose: walks the board on a timer of its own, so a person can see the
/// watcher is running when no runner and no browser is connected. Each sweep
/// asks every registered <see cref="IGooseCheck"/>, in registration order, what
/// it looked at and what it concluded, and keeps the answer as a
/// <see cref="EfHatchGooseRun"/>.
///
/// A sweep whose conclusions equal the newest run's is folded into that run
/// instead of adding a row - see <see cref="GooseEntry.SameConclusionAs"/> for
/// what "equal" leaves out. Timings are never compared.
///
/// <para>Two sweeps never run at once. <c>[DisallowConcurrentExecution]</c>
/// stops Quartz firing the job while it runs, and a trigger that falls due in
/// that time is skipped or fires once late, never a pile of them. That is not
/// enough across instances, which share one scheduler instance id, so a sweep
/// also takes an <see cref="IGooseLock"/> and writes nothing without it.</para>
///
/// A job's context has no request, so it reads every project
/// (docs/hatch.md, "How scope is enforced"). Never throws, because Quartz
/// re-fires a job that does: a failure is a log line and the next sweep tries
/// again.
/// </summary>
[DisallowConcurrentExecution]
public class GooseJob(
    HatchContext db, IEnumerable<IGooseCheck> checks, IGooseLock gate, IOptions<HatchOptions> options,
    TimeProvider time, ILogger<GooseJob> logger) : IAppJob
{
    public string Name => "Goose";

    public string Group => "Hatch.Api";

    public TimeSpan Interval => TimeSpan.FromSeconds(GooseSettings.IntervalSeconds(options.Value));

    public Task Execute(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await SweepAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "The goose's sweep failed - the next one tries again");
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        // A null here is another sweep in progress: it writes the record, so
        // this one writes nothing. Disposing null is skipped by await using.
        await using var held = await gate.TryAcquireAsync(db, ct);
        if (held is null)
        {
            logger.LogDebug("Another goose sweep is in progress - skipping this one");
            return;
        }

        var startedAt = time.GetUtcNow();
        var began = time.GetTimestamp();

        var entries = new List<GooseEntry>();
        foreach (var check in checks)
        {
            try
            {
                var seen = await check.RunAsync(ct);
                entries.Add(new GooseEntry(check.Name, seen.Looked, seen.Concluded, false, null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Goose check {Check} failed", check.Name);
                entries.Add(new GooseEntry(check.Name, "", "", true, e.Message));
            }
        }

        var tookMs = (long)time.GetElapsedTime(began).TotalMilliseconds;

        var newest = await db.GooseRuns
            .OrderByDescending(r => r.LastRanAt)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);

        if (newest is not null && Same(GooseEntry.Read(newest.Entries), entries))
        {
            newest.LastRanAt = startedAt;
            newest.Sweeps++;
            newest.LongestMs = Math.Max(newest.LongestMs, tookMs);
            newest.Entries = GooseEntry.Write(entries);
        }
        else
        {
            db.GooseRuns.Add(new EfHatchGooseRun
            {
                FirstRanAt = startedAt,
                LastRanAt = startedAt,
                Sweeps = 1,
                LongestMs = tookMs,
                Entries = GooseEntry.Write(entries),
            });
        }

        // RemoveRange rather than ExecuteDelete, which the in-memory provider
        // the tests use refuses. Zero keeps everything.
        var keepDays = GooseSettings.KeepDays(options.Value);
        if (keepDays > 0)
        {
            var cutoff = startedAt.AddDays(-keepDays);
            db.GooseRuns.RemoveRange(await db.GooseRuns.Where(r => r.LastRanAt < cutoff).ToListAsync(ct));
        }

        await db.SaveChangesAsync(ct);
    }

    private static bool Same(IReadOnlyList<GooseEntry> before, IReadOnlyList<GooseEntry> now) =>
        before.Count == now.Count && before.Zip(now).All(pair => pair.First.SameConclusionAs(pair.Second));
}

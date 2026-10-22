using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="ReflectionCadenceJob"/> - the Quartz firing that reads
/// <see cref="EfHatchProject.ReflectionCadenceDays"/>/<see cref="EfHatchProject.ReflectionLastPeriodStart"/>
/// and files a reflection where <see cref="ReflectionCadence.IsDue"/> says one
/// is due. <see cref="ReflectionCadence"/> and <see cref="Dispatch.MatchReflection"/>
/// are already covered on their own; these are about the job's own loop - the
/// per-project save, the warning instead of a throw, and idempotence across two
/// firings of the same clock.
/// </summary>
public class ReflectionCadenceJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_AProjectPastCadenceWithACoveringRow_FilesOneReflectionAndAdvancesToTheDueInstant()
    {
        var (db, from, _) = await NewDbAsync();
        var lastPeriodStart = Now.AddDays(-10);
        var project = await AddProjectAsync(db, "AER", cadenceDays: 7, lastPeriodStart: lastPeriodStart);
        db.Playbooks.Add(ReflectionPlaybook(from, from));
        await db.SaveChangesAsync();

        await NewJob(db, Now).RunAsync(CancellationToken.None);

        var issues = await db.Issues.Where(i => i.ProjectId == project.Id).ToListAsync();
        var issue = Assert.Single(issues);
        Assert.Equal("reflection", issue.Type);
        Assert.Equal(from, issue.StatusId);
        Assert.Equal(ReflectionCadenceJob.Actor, issue.CreatedBy);

        var windowEnd = lastPeriodStart.AddDays(7);
        Assert.Contains(windowEnd.ToString("yyyy-MM-dd"), issue.Title);

        var reread = await db.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.Equal(windowEnd, reread.ReflectionLastPeriodStart);
        Assert.NotEqual(Now, reread.ReflectionLastPeriodStart);
    }

    [Fact]
    public async Task RunAsync_NeverReflected_FilesOneReflectionThenNoneOnTheNextFiring()
    {
        var (db, from, _) = await NewDbAsync();
        var project = await AddProjectAsync(db, "AER", cadenceDays: 7, lastPeriodStart: null);
        db.Playbooks.Add(ReflectionPlaybook(from, from));
        await db.SaveChangesAsync();

        var job = NewJob(db, Now);
        await job.RunAsync(CancellationToken.None);

        var issues = await db.Issues.Where(i => i.ProjectId == project.Id).ToListAsync();
        var issue = Assert.Single(issues);
        Assert.Contains(Now.ToString("yyyy-MM-dd"), issue.Title);

        var reread = await db.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.Equal(Now, reread.ReflectionLastPeriodStart);

        await job.RunAsync(CancellationToken.None);

        Assert.Single(await db.Issues.Where(i => i.ProjectId == project.Id).ToListAsync());
    }

    [Fact]
    public async Task RunAsync_CalledTwiceWithTheSameClock_FilesOnlyTheOneIssue()
    {
        var (db, from, _) = await NewDbAsync();
        var project = await AddProjectAsync(db, "AER", cadenceDays: 7, lastPeriodStart: Now.AddDays(-10));
        db.Playbooks.Add(ReflectionPlaybook(from, from));
        await db.SaveChangesAsync();

        var job = NewJob(db, Now);
        await job.RunAsync(CancellationToken.None);
        await job.RunAsync(CancellationToken.None);

        var issues = await db.Issues.Where(i => i.ProjectId == project.Id).ToListAsync();
        Assert.Single(issues);
    }

    [Fact]
    public async Task RunAsync_NoCadenceSet_FilesNothing()
    {
        var (db, from, _) = await NewDbAsync();
        var project = await AddProjectAsync(db, "AER", cadenceDays: null, lastPeriodStart: null);
        db.Playbooks.Add(ReflectionPlaybook(from, from));
        await db.SaveChangesAsync();

        await NewJob(db, Now).RunAsync(CancellationToken.None);

        Assert.Empty(await db.Issues.Where(i => i.ProjectId == project.Id).ToListAsync());
    }

    [Fact]
    public async Task RunAsync_NextDueStillAhead_FilesNothing()
    {
        var (db, from, _) = await NewDbAsync();
        var project = await AddProjectAsync(db, "AER", cadenceDays: 7, lastPeriodStart: Now);
        db.Playbooks.Add(ReflectionPlaybook(from, from));
        await db.SaveChangesAsync();

        await NewJob(db, Now).RunAsync(CancellationToken.None);

        Assert.Empty(await db.Issues.Where(i => i.ProjectId == project.Id).ToListAsync());
        var reread = await db.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.Equal(Now, reread.ReflectionLastPeriodStart);
    }

    [Fact]
    public async Task RunAsync_DueWithNoCoveringPlaybook_LogsAndStillFilesEveryOtherDueProject()
    {
        var (db, from, _) = await NewDbAsync();
        var uncovered = await AddProjectAsync(db, "AER", cadenceDays: 7, lastPeriodStart: Now.AddDays(-10));
        var covered = await AddProjectAsync(db, "OPS", cadenceDays: 7, lastPeriodStart: Now.AddDays(-10));

        // No playbook row at all covers "uncovered"; a row scoped to "covered"
        // alone is the only one in the table, so MatchReflection finds nothing
        // for the first project and the real row for the second.
        db.Playbooks.Add(ReflectionPlaybook(from, from, projectId: covered.Id));
        await db.SaveChangesAsync();

        await NewJob(db, Now).RunAsync(CancellationToken.None);

        Assert.Empty(await db.Issues.Where(i => i.ProjectId == uncovered.Id).ToListAsync());
        Assert.Single(await db.Issues.Where(i => i.ProjectId == covered.Id).ToListAsync());
    }

    private static async Task<(HatchContext Db, int FromStatusId, int ToStatusId)> NewDbAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var from = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        var to = new EfHatchStatus { Name = "in progress", SortOrder = 20 };
        db.AddRange(from, to);
        await db.SaveChangesAsync();

        return (db, from.Id, to.Id);
    }

    private static async Task<EfHatchProject> AddProjectAsync(
        HatchContext db, string key, int? cadenceDays, DateTimeOffset? lastPeriodStart)
    {
        var project = new EfHatchProject
        {
            Key = key,
            Name = key,
            CreatedAt = Now,
            ReflectionCadenceDays = cadenceDays,
            ReflectionLastPeriodStart = lastPeriodStart,
        };
        db.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    private static EfHatchPlaybook ReflectionPlaybook(int from, int to, int? projectId = null) => new()
    {
        FromStatusId = from,
        ToStatusId = to,
        Types = "",
        Shape = EfHatchPlaybook.AnyShape,
        Model = "sonnet",
        Effort = "medium",
        ProjectId = projectId,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static ReflectionCadenceJob NewJob(HatchContext db, DateTimeOffset now) =>
        new(db, new RankService(db), new FakeTimeProvider(now), NullLogger<ReflectionCadenceJob>.Instance);
}

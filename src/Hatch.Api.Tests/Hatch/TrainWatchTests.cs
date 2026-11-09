using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="TrainWatch.Observe"/> alone, with no database under it: whether
/// a project's merge train has been stuck on the same head long enough that
/// its driver should be passed over.
/// </summary>
public class TrainWatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Bound = TimeSpan.FromMinutes(10);

    private static EfHatchProject Project(long? watchedIssueId = null, DateTimeOffset? watchedSince = null) => new()
    {
        Key = "AER",
        Name = "Hatch",
        CreatedAt = Now,
        TrainWatchedIssueId = watchedIssueId,
        TrainWatchedSince = watchedSince,
    };

    private static Dispatch.TrainHead Head(long issueId = 1, string kind = WorkKinds.Merge, DateTimeOffset? broughtCurrentAt = null) =>
        new(ProjectId: 1, IssueId: issueId, Key: "AER-1", Kind: kind, BroughtCurrentAt: broughtCurrentAt);

    [Fact]
    public void ANewHead_RestartsTheClockAndIsNotYetStalled()
    {
        var project = Project();

        var stalled = TrainWatch.Observe(project, Head(), hasDriver: true, Now, Bound);

        Assert.False(stalled);
        Assert.Equal(1, project.TrainWatchedIssueId);
        Assert.Equal(Now, project.TrainWatchedSince);
    }

    [Fact]
    public void ADifferentHeadThanTheOneWatched_RestartsTheClockToo()
    {
        var project = Project(watchedIssueId: 9, watchedSince: Now.AddMinutes(-20));

        var stalled = TrainWatch.Observe(project, Head(issueId: 1), hasDriver: true, Now, Bound);

        Assert.False(stalled);
        Assert.Equal(1, project.TrainWatchedIssueId);
        Assert.Equal(Now, project.TrainWatchedSince);
    }

    [Fact]
    public void TheSameHead_InsideTheWindow_IsNotStalledAndTheClockIsUntouched()
    {
        var since = Now.AddMinutes(-9);
        var project = Project(watchedIssueId: 1, watchedSince: since);

        var stalled = TrainWatch.Observe(project, Head(), hasDriver: true, Now, Bound);

        Assert.False(stalled);
        Assert.Equal(since, project.TrainWatchedSince);
    }

    [Fact]
    public void TheSameHead_PastTheWindow_IsStalled()
    {
        var project = Project(watchedIssueId: 1, watchedSince: Now.AddMinutes(-10));

        var stalled = TrainWatch.Observe(project, Head(), hasDriver: true, Now, Bound);

        Assert.True(stalled);
    }

    [Fact]
    public void ExactlyAtTheWindow_IsStalled()
    {
        var project = Project(watchedIssueId: 1, watchedSince: Now - Bound);

        Assert.True(TrainWatch.Observe(project, Head(), hasDriver: true, Now, Bound));
    }

    [Fact]
    public void OneTickShortOfTheWindow_IsNotStalled()
    {
        var project = Project(watchedIssueId: 1, watchedSince: Now - Bound + TimeSpan.FromTicks(1));

        Assert.False(TrainWatch.Observe(project, Head(), hasDriver: true, Now, Bound));
    }

    [Fact]
    public void ANullHead_ClearsTheWatchAndIsNotStalled()
    {
        var project = Project(watchedIssueId: 1, watchedSince: Now.AddMinutes(-20));

        var stalled = TrainWatch.Observe(project, null, hasDriver: true, Now, Bound);

        Assert.False(stalled);
        Assert.Null(project.TrainWatchedIssueId);
        Assert.Null(project.TrainWatchedSince);
    }

    [Theory]
    [InlineData(WorkKinds.Hold)]
    [InlineData(WorkKinds.Pending)]
    [InlineData(WorkKinds.Pushback)]
    [InlineData(WorkKinds.Land)]
    public void AHeadNotMergeOrUpdate_ClearsTheWatchAndIsNotStalled(string kind)
    {
        var project = Project(watchedIssueId: 1, watchedSince: Now.AddMinutes(-20));

        var stalled = TrainWatch.Observe(project, Head(kind: kind), hasDriver: true, Now, Bound);

        Assert.False(stalled);
        Assert.Null(project.TrainWatchedIssueId);
        Assert.Null(project.TrainWatchedSince);
    }

    [Fact]
    public void NobodyEligibleToDrive_ClearsTheWatchAndIsNotStalled()
    {
        // Nobody to pass the driver over to is not a driver's fault.
        var project = Project(watchedIssueId: 1, watchedSince: Now.AddMinutes(-20));

        var stalled = TrainWatch.Observe(project, Head(), hasDriver: false, Now, Bound);

        Assert.False(stalled);
        Assert.Null(project.TrainWatchedIssueId);
        Assert.Null(project.TrainWatchedSince);
    }

    [Fact]
    public void ABroughtCurrentMarkInsideTheWindow_DefersTheStall()
    {
        // The window is held to the later of the watch's own start and the
        // head's last BroughtCurrentAt - a build the train just started is
        // not thrown away just because the head itself has sat here a while.
        var project = Project(watchedIssueId: 1, watchedSince: Now.AddMinutes(-20));

        var stalled = TrainWatch.Observe(project, Head(broughtCurrentAt: Now.AddMinutes(-5)), hasDriver: true, Now, Bound);

        Assert.False(stalled);
    }

    [Fact]
    public void ABroughtCurrentMarkOutsideTheWindow_DoesNotDeferTheStall()
    {
        var project = Project(watchedIssueId: 1, watchedSince: Now.AddMinutes(-20));

        var stalled = TrainWatch.Observe(project, Head(broughtCurrentAt: Now.AddMinutes(-15)), hasDriver: true, Now, Bound);

        Assert.True(stalled);
    }
}

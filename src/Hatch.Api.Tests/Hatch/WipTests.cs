using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The load: how full the WIP section reads on the board, and what
/// <see cref="Wip.Counted"/> says about a single issue - the one helper every
/// later story (HA-89's gate, HA-90's fold, HA-91's zone) calls rather than
/// counting for itself.
/// </summary>
public class WipTests
{
    // ---- The board's wip block ----

    [Fact]
    public async Task StoriesAndBugsInTheSectionCount_TasksDoNot()
    {
        var h = await NewAsync();
        await h.WipAsync(3, h.InProgress, h.InReview);
        await h.IssueAsync("story", h.InProgress);
        await h.IssueAsync("story", h.InReview);
        await h.IssueAsync("task", h.InProgress);

        var wip = (await h.BoardAsync()).Wip;

        Assert.NotNull(wip);
        var slice = wip!.Slices.Single(s => s.Types.SequenceEqual(new[] { "story", "bug" }));
        Assert.Equal(3, slice.Limit);
        Assert.Equal([h.InProgress, h.InReview], wip.StatusIds);
        Assert.Equal(2, slice.Load);
        Assert.Equal(0, slice.ClaimedInbound);
    }

    [Fact]
    public async Task AnEpicInTheSection_CountsTowardTheEpicSliceAndNotTheOther()
    {
        var h = await NewAsync();
        await h.WipAsync(3, h.InProgress, h.InReview);
        await h.WriteLimitAsync(2, EfHatchWipLimit.Epics);
        await h.IssueAsync("story", h.InProgress);
        await h.IssueAsync("epic", h.InReview);

        var wip = (await h.BoardAsync()).Wip;

        Assert.NotNull(wip);
        var storyBug = wip!.Slices.Single(s => s.Types.SequenceEqual(new[] { "story", "bug" }));
        var epics = wip.Slices.Single(s => s.Types.SequenceEqual(new[] { "epic" }));
        Assert.Equal(1, storyBug.Load);
        Assert.Equal(1, epics.Load);
    }

    [Fact]
    public async Task ABugInTheSection_CountsAsAStoryDoes()
    {
        var h = await NewAsync();
        await h.WipAsync(3, h.InProgress, h.InReview);
        await h.IssueAsync("story", h.InProgress);
        await h.IssueAsync("bug", h.InReview);

        var wip = (await h.BoardAsync()).Wip;

        Assert.Equal(2, wip!.Slices[0].Load);
    }

    [Fact]
    public async Task ALiveClaimInTheFeederColumn_CountsInboundUntilItExpires()
    {
        var h = await NewAsync();
        await h.WipAsync(3, h.InProgress, h.InReview);
        await h.IssueAsync("story", h.InProgress);
        await h.IssueAsync("story", h.InReview);
        var claimed = await h.IssueAsync("story", h.ToDo);
        await h.ClaimAsync(claimed);

        var live = (await h.BoardAsync()).Wip;
        Assert.Equal(3, live!.Slices[0].Load);
        Assert.Equal(1, live.Slices[0].ClaimedInbound);

        h.Time.Advance(TimeSpan.FromSeconds(TestClaims.Ttl + 1));

        var expired = (await h.BoardAsync()).Wip;
        Assert.Equal(2, expired!.Slices[0].Load);
        Assert.Equal(0, expired.Slices[0].ClaimedInbound);
    }

    [Fact]
    public async Task ALiveClaimOutsideTheFeederColumn_CountsNothing()
    {
        var h = await NewAsync();
        await h.WipAsync(3, h.InProgress, h.InReview);

        var inBacklog = await h.IssueAsync("story", h.Backlog);
        await h.ClaimAsync(inBacklog);
        var inBreakdown = await h.IssueAsync("story", h.Breakdown);
        await h.ClaimAsync(inBreakdown);
        var taskInToDo = await h.IssueAsync("task", h.ToDo);
        await h.ClaimAsync(taskInToDo);

        var wip = (await h.BoardAsync()).Wip;

        Assert.Equal(0, wip!.Slices[0].Load);
        Assert.Equal(0, wip.Slices[0].ClaimedInbound);
    }

    [Fact]
    public async Task ALiveClaimOnAnEpicInTheFeederColumn_CountsInboundOnTheEpicSlice()
    {
        var h = await NewAsync();
        await h.WipAsync(3, h.InProgress, h.InReview);
        await h.WriteLimitAsync(2, EfHatchWipLimit.Epics);
        var claimed = await h.IssueAsync("epic", h.ToDo);
        await h.ClaimAsync(claimed);

        var wip = (await h.BoardAsync()).Wip;

        var epics = wip!.Slices.Single(s => s.Types.SequenceEqual(new[] { "epic" }));
        Assert.Equal(1, epics.Load);
        Assert.Equal(1, epics.ClaimedInbound);
    }

    [Fact]
    public async Task CountedMatchesExactlyTheSetTheLoadIsSummedFrom()
    {
        var h = await NewAsync();
        await h.WipAsync(3, h.InProgress, h.InReview);
        var inSection = await h.IssueAsync("story", h.InProgress);
        var claimedInToDo = await h.IssueAsync("story", h.ToDo);
        await h.ClaimAsync(claimedInToDo);
        var unclaimedInToDo = await h.IssueAsync("story", h.ToDo);
        var taskInSection = await h.IssueAsync("task", h.InReview);

        var section = await h.LoadAsync();
        var slice = section!.SliceFor("story")!;

        Assert.True(slice.Counted(await h.EntityAsync(inSection)));
        Assert.True(slice.Counted(await h.EntityAsync(claimedInToDo)));
        Assert.False(slice.Counted(await h.EntityAsync(unclaimedInToDo)));
        Assert.False(slice.Counted(await h.EntityAsync(taskInSection)));
        Assert.Equal(2, slice.Load);
    }

    [Fact]
    public async Task SliceForATask_IsNull()
    {
        var h = await NewAsync();
        await h.WipAsync(3, h.InProgress, h.InReview);

        var section = await h.LoadAsync();

        Assert.Null(section!.SliceFor("task"));
    }

    [Fact]
    public async Task AColumnMadeTerminalOrDeferred_DropsOutOfTheSection()
    {
        var h = await NewAsync();
        await h.WipAsync(3, h.InProgress, h.InReview);
        await h.IssueAsync("story", h.InProgress);

        var inReview = await h.Db.Statuses.SingleAsync(s => s.Id == h.InReview);
        inReview.IsTerminal = true;
        await h.Db.SaveChangesAsync();

        var wip = (await h.BoardAsync()).Wip;
        Assert.NotNull(wip);
        Assert.DoesNotContain(h.InReview, wip!.StatusIds);
        Assert.Equal(1, wip.Slices[0].Load);

        var inProgress = await h.Db.Statuses.SingleAsync(s => s.Id == h.InProgress);
        inProgress.IsDeferred = true;
        await h.Db.SaveChangesAsync();

        Assert.Null((await h.BoardAsync()).Wip);
    }

    [Fact]
    public async Task NoLimitRow_MeansNoWip()
    {
        var h = await NewAsync();

        var board = await h.BoardAsync();

        Assert.Null(board.Wip);
    }

    [Fact]
    public async Task ALimitWithNoFlaggedColumn_MeansNoWipButTheRestOfTheBoardIsUnchanged()
    {
        var h = await NewAsync();
        await h.IssueAsync("story", h.ToDo);
        await h.WriteLimitAsync(3);

        var board = await h.BoardAsync();

        Assert.Null(board.Wip);
        Assert.Equal(6, board.Statuses.Count);
        Assert.Single(board.Issues);
        Assert.Equal(h.ToDo, board.Issues[0].StatusId);
    }

    [Fact]
    public async Task FlaggedColumnsWithNoRow_GiveANonNullSectionWithBothLimitsNullAndRealLoads()
    {
        var h = await NewAsync();
        await h.FlagAsync(h.InProgress, h.InReview);
        await h.IssueAsync("story", h.InProgress);
        await h.IssueAsync("epic", h.InReview);

        var wip = (await h.BoardAsync()).Wip;

        Assert.NotNull(wip);
        Assert.Equal(2, wip!.Slices.Count);
        Assert.All(wip.Slices, s => Assert.Null(s.Limit));
        var storyBug = wip.Slices.Single(s => s.Types.SequenceEqual(new[] { "story", "bug" }));
        var epics = wip.Slices.Single(s => s.Types.SequenceEqual(new[] { "epic" }));
        Assert.Equal(1, storyBug.Load);
        Assert.Equal(1, epics.Load);
    }

    [Fact]
    public async Task AStrayRowWithOtherTypes_IsIgnored()
    {
        var h = await NewAsync();
        await h.WipAsync(3, h.InProgress, h.InReview);
        h.Db.WipLimits.Add(new EfHatchWipLimit { Types = "task", Limit = 5 });
        await h.Db.SaveChangesAsync();

        var wip = (await h.BoardAsync()).Wip;

        Assert.Equal(2, wip!.Slices.Count);
    }

    // ---- The sentence ----

    [Fact]
    public void TwoTypes_ReadAsBothJoinedWithAnd()
    {
        Assert.Equal(
            "the WIP section is full - 2 of 2 stories and bugs are in it",
            Wip.Sentence(2, 2, ["story", "bug"]));
    }

    [Fact]
    public void OneType_ReadsAsJustThatOnePluralised()
    {
        Assert.Equal("the WIP section is full - 5 of 5 stories are in it", Wip.Sentence(5, 5, ["story"]));
    }

    [Fact]
    public void ThreeTypes_ReadWithAnOxfordComma()
    {
        Assert.Equal(
            "the WIP section is full - 1 of 1 stories, bugs, and tasks are in it",
            Wip.Sentence(1, 1, ["story", "bug", "task"]));
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required BoardController Board { get; init; }
        public required IssueClaims Claims { get; init; }
        public required int ProjectId { get; init; }
        public required int Breakdown { get; init; }
        public required int Backlog { get; init; }
        public required int ToDo { get; init; }
        public required int InProgress { get; init; }
        public required int InReview { get; init; }
        public required int Done { get; init; }

        public async Task<BoardDto> BoardAsync() =>
            (await Board.GetBoard(default)).Value ?? throw new InvalidOperationException("expected a board");

        public async Task<WipSection?> LoadAsync()
        {
            var statuses = await Db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
            return await Wip.LoadAsync(Db, Claims, statuses, Time.GetUtcNow(), default);
        }

        public async Task<long> IssueAsync(string type, int statusId)
        {
            var now = Time.GetUtcNow();
            var project = await Db.Projects.SingleAsync(p => p.Id == ProjectId);
            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId,
                Number = project.NextIssueNumber,
                Type = type,
                Title = type,
                StatusId = statusId,
                Rank = 100,
                CreatedBy = "hatch",
                CreatedAt = now,
                UpdatedAt = now,
            };
            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue.Id;
        }

        public async Task<EfHatchIssue> EntityAsync(long id) =>
            await Db.Issues.AsNoTracking().SingleAsync(i => i.Id == id);

        public async Task ClaimAsync(long issueId)
        {
            var now = Time.GetUtcNow();
            var issue = await Db.Issues.SingleAsync(i => i.Id == issueId);
            issue.ClaimToken = Guid.NewGuid();
            issue.ClaimedBy = "hatch";
            issue.ClaimRunner = "somewhere:/checkouts/one";
            issue.ClaimedAt = now;
            issue.ClaimHeartbeatAt = now;
            await Db.SaveChangesAsync();
        }

        public async Task WipAsync(int limit, params int[] statusIds)
        {
            await FlagAsync(statusIds);
            await WriteLimitAsync(limit);
        }

        public async Task FlagAsync(params int[] statusIds)
        {
            foreach (var id in statusIds)
            {
                var status = await Db.Statuses.SingleAsync(s => s.Id == id);
                status.IsWip = true;
            }

            await Db.SaveChangesAsync();
        }

        public async Task WriteLimitAsync(int limit, string types = EfHatchWipLimit.StoriesAndBugs)
        {
            Db.WipLimits.Add(new EfHatchWipLimit { Types = types, Limit = limit });
            await Db.SaveChangesAsync();
        }

    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var breakdown = new EfHatchStatus { Name = "breakdown", SortOrder = 10 };
        var backlog = new EfHatchStatus { Name = "backlog", SortOrder = 20 };
        var todo = new EfHatchStatus { Name = "to do", SortOrder = 30 };
        var inProgress = new EfHatchStatus { Name = "in progress", SortOrder = 40 };
        var inReview = new EfHatchStatus { Name = "in review", SortOrder = 50 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 60, IsTerminal = true };
        db.AddRange(project, breakdown, backlog, todo, inProgress, inReview, done);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var actors = new StubActorDirectory();
        var claims = TestClaims.With();

        return new Harness
        {
            Db = db,
            Time = time,
            Board = new BoardController(db, actors, claims, time),
            Claims = claims,
            ProjectId = project.Id,
            Breakdown = breakdown.Id,
            Backlog = backlog.Id,
            ToDo = todo.Id,
            InProgress = inProgress.Id,
            InReview = inReview.Id,
            Done = done.Id,
        };
    }
}

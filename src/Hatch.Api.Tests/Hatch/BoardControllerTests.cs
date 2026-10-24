using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The one fold the server applies to its own board read: a terminal card
/// whose <see cref="EfHatchIssue.ClosedAt"/> is older than
/// <see cref="HatchOptions.DoneHorizonHours"/> is withheld - see HA-863.
/// </summary>
public class BoardControllerTests
{
    [Fact]
    public async Task TerminalCard_JustInsideTheHorizon_IsHandedOver()
    {
        var h = await NewAsync();
        var key = await h.BugAsync(h.Done, closedAt: Now.AddHours(-47));

        var board = Value(await h.Board.GetBoard(default));

        Assert.Contains(board.Issues, i => i.Key == key);
    }

    [Fact]
    public async Task TerminalCard_PastTheHorizon_IsWithheld()
    {
        var h = await NewAsync();
        var key = await h.BugAsync(h.Done, closedAt: Now.AddHours(-49));

        var board = Value(await h.Board.GetBoard(default));

        Assert.DoesNotContain(board.Issues, i => i.Key == key);
    }

    [Fact]
    public async Task NonTerminalCard_WithAnOldClosedAt_IsStillHandedOver()
    {
        // The column was flipped back to non-terminal after the issue closed
        // under it - the fold only ever looks at today's column.
        var h = await NewAsync();
        var key = await h.BugAsync(h.InProgress, closedAt: Now.AddHours(-100));

        var board = Value(await h.Board.GetBoard(default));

        Assert.Contains(board.Issues, i => i.Key == key);
    }

    [Fact]
    public async Task TerminalCard_WithNoClosedAt_IsHandedOver()
    {
        var h = await NewAsync();
        var key = await h.BugAsync(h.Done, closedAt: null);

        var board = Value(await h.Board.GetBoard(default));

        Assert.Contains(board.Issues, i => i.Key == key);
    }

    [Fact]
    public async Task HorizonOfZero_HandsOverEveryCard()
    {
        var h = await NewAsync(horizonHours: 0);
        var key = await h.BugAsync(h.Done, closedAt: Now.AddYears(-1));

        var board = Value(await h.Board.GetBoard(default));

        Assert.Contains(board.Issues, i => i.Key == key);
    }

    [Fact]
    public async Task DoneHorizonHours_ReadsTheConfiguredValue_AndNullWhenOff()
    {
        var h = await NewAsync();
        var board = Value(await h.Board.GetBoard(default));
        Assert.Equal(48, board.DoneHorizonHours);

        var off = await NewAsync(horizonHours: 0);
        var offBoard = Value(await off.Board.GetBoard(default));
        Assert.Null(offBoard.DoneHorizonHours);
    }

    [Fact]
    public async Task AColumnsRunnerTags_ReadBackEmptyByDefaultAndWrittenOnceSet()
    {
        var h = await NewAsync();

        var board = Value(await h.Board.GetBoard(default));
        Assert.Empty(board.Statuses.Single(s => s.Id == h.InProgress).RunnerTags);

        var status = await h.Db.Statuses.SingleAsync(s => s.Id == h.InProgress);
        status.RunnerTags = RunnerTagColumn.Write(["macos", "xcode"]);
        await h.Db.SaveChangesAsync();

        var reread = Value(await h.Board.GetBoard(default));
        Assert.Equal(["macos", "xcode"], reread.Statuses.Single(s => s.Id == h.InProgress).RunnerTags);
    }

    // ---- Past horizon: a closed story riding along under its still-open epic (HA-1489) ----

    [Fact]
    public async Task PastHorizon_StoryClosedUnderAnOpenEpic_RidesAlongThere()
    {
        var h = await NewAsync();
        var epic = await h.IssueAsync("epic", h.InProgress, closedAt: null);
        var story = await h.IssueAsync("story", h.Done, closedAt: Now.AddHours(-49), parentId: epic.Id);
        var epicKey = IssueKey.Format(h.ProjectKey, epic.Number);
        var storyKey = IssueKey.Format(h.ProjectKey, story.Number);

        var board = Value(await h.Board.GetBoard(default));

        Assert.DoesNotContain(board.Issues, i => i.Key == storyKey);
        var card = Assert.Single(board.PastHorizon, i => i.Key == storyKey);
        Assert.Equal("story", card.Type);
        Assert.Equal(epicKey, card.ParentKey);
        Assert.Equal(h.Done, card.StatusId);
        Assert.NotNull(card.ClosedAt);
    }

    [Fact]
    public async Task PastHorizon_EpicItselfPastTheHorizon_DropsTheStoryFromBoth()
    {
        var h = await NewAsync();
        var epic = await h.IssueAsync("epic", h.Done, closedAt: Now.AddHours(-100));
        var story = await h.IssueAsync("story", h.Done, closedAt: Now.AddHours(-49), parentId: epic.Id);
        var storyKey = IssueKey.Format(h.ProjectKey, story.Number);

        var board = Value(await h.Board.GetBoard(default));

        Assert.DoesNotContain(board.Issues, i => i.Key == storyKey);
        Assert.DoesNotContain(board.PastHorizon, i => i.Key == storyKey);
    }

    [Fact]
    public async Task PastHorizon_EpicInADeferredColumn_DropsTheStoryButKeepsTheEpic()
    {
        var h = await NewAsync();
        var epic = await h.IssueAsync("epic", h.Deferred, closedAt: null);
        var story = await h.IssueAsync("story", h.Done, closedAt: Now.AddHours(-49), parentId: epic.Id);
        var epicKey = IssueKey.Format(h.ProjectKey, epic.Number);
        var storyKey = IssueKey.Format(h.ProjectKey, story.Number);

        var board = Value(await h.Board.GetBoard(default));

        Assert.DoesNotContain(board.Issues, i => i.Key == storyKey);
        Assert.DoesNotContain(board.PastHorizon, i => i.Key == storyKey);
        Assert.Contains(board.Issues, i => i.Key == epicKey);
    }

    [Fact]
    public async Task PastHorizon_StoryWithNoParent_IsInNeitherList()
    {
        var h = await NewAsync();
        var story = await h.IssueAsync("story", h.Done, closedAt: Now.AddHours(-49));
        var storyKey = IssueKey.Format(h.ProjectKey, story.Number);

        var board = Value(await h.Board.GetBoard(default));

        Assert.DoesNotContain(board.Issues, i => i.Key == storyKey);
        Assert.DoesNotContain(board.PastHorizon, i => i.Key == storyKey);
    }

    [Theory]
    [InlineData("bug")]
    [InlineData("task")]
    public async Task PastHorizon_BugOrTask_NeverRidesAlong(string type)
    {
        var h = await NewAsync();
        var epic = await h.IssueAsync("epic", h.InProgress, closedAt: null);
        var issue = await h.IssueAsync(type, h.Done, closedAt: Now.AddHours(-49), parentId: epic.Id);
        var issueKey = IssueKey.Format(h.ProjectKey, issue.Number);

        var board = Value(await h.Board.GetBoard(default));

        Assert.DoesNotContain(board.Issues, i => i.Key == issueKey);
        Assert.DoesNotContain(board.PastHorizon, i => i.Key == issueKey);
    }

    [Fact]
    public async Task PastHorizon_StoryInsideTheHorizon_StaysInIssuesOnly()
    {
        var h = await NewAsync();
        var epic = await h.IssueAsync("epic", h.InProgress, closedAt: null);
        var story = await h.IssueAsync("story", h.Done, closedAt: Now.AddHours(-47), parentId: epic.Id);
        var storyKey = IssueKey.Format(h.ProjectKey, story.Number);

        var board = Value(await h.Board.GetBoard(default));

        Assert.Contains(board.Issues, i => i.Key == storyKey);
        Assert.DoesNotContain(board.PastHorizon, i => i.Key == storyKey);
    }

    [Fact]
    public async Task PastHorizon_HorizonOff_IsEmptyAndIssuesUnchanged()
    {
        var h = await NewAsync(horizonHours: 0);
        var epic = await h.IssueAsync("epic", h.InProgress, closedAt: null);
        var story = await h.IssueAsync("story", h.Done, closedAt: Now.AddYears(-1), parentId: epic.Id);
        var storyKey = IssueKey.Format(h.ProjectKey, story.Number);

        var board = Value(await h.Board.GetBoard(default));

        Assert.Contains(board.Issues, i => i.Key == storyKey);
        Assert.Empty(board.PastHorizon);
    }

    [Fact]
    public async Task PastHorizon_InheritsEffectivePriorityLikeAnyOtherCard()
    {
        var h = await NewAsync();
        var epic = await h.IssueAsync("epic", h.InProgress, closedAt: null);
        var epicKey = IssueKey.Format(h.ProjectKey, epic.Number);
        epic.Priority = PriorityLevels.Expedited;
        await h.Db.SaveChangesAsync();
        var inside = await h.IssueAsync("story", h.InProgress, closedAt: null, parentId: epic.Id);
        var past = await h.IssueAsync("story", h.Done, closedAt: Now.AddHours(-49), parentId: epic.Id);
        var insideKey = IssueKey.Format(h.ProjectKey, inside.Number);
        var pastKey = IssueKey.Format(h.ProjectKey, past.Number);

        var board = Value(await h.Board.GetBoard(default));

        var insideCard = Assert.Single(board.Issues, i => i.Key == insideKey);
        var pastCard = Assert.Single(board.PastHorizon, i => i.Key == pastKey);
        Assert.True(pastCard.Expedited);
        Assert.Equal(insideCard.Priority, pastCard.Priority);
        Assert.Equal(insideCard.PriorityFrom, pastCard.PriorityFrom);
        Assert.Equal("expedited", pastCard.Priority);
        Assert.Equal(epicKey, pastCard.PriorityFrom);
    }

    [Fact]
    public async Task PastHorizon_StoryReopenedToANonTerminalColumn_StaysOutOfPastHorizon()
    {
        // The same shape as NonTerminalCard_WithAnOldClosedAt_IsStillHandedOver above:
        // a terminal-card's ClosedAt surviving a move back to a non-terminal column.
        // Without this case, nothing exercises pastRows' terminalStatusIds.Contains(i.StatusId)
        // clause - every other case above leaves the story in h.Done.
        var h = await NewAsync();
        var epic = await h.IssueAsync("epic", h.InProgress, closedAt: null);
        var story = await h.IssueAsync("story", h.InProgress, closedAt: Now.AddHours(-100), parentId: epic.Id);
        var storyKey = IssueKey.Format(h.ProjectKey, story.Number);

        var board = Value(await h.Board.GetBoard(default));

        Assert.Contains(board.Issues, i => i.Key == storyKey);
        Assert.DoesNotContain(board.PastHorizon, i => i.Key == storyKey);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException("expected a value");

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required BoardController Board { get; init; }
        public required int ProjectId { get; init; }
        public required string ProjectKey { get; init; }
        public required int InProgress { get; init; }
        public required int Done { get; init; }
        public required int Deferred { get; init; }

        public async Task<EfHatchIssue> IssueAsync(string type, int statusId, DateTimeOffset? closedAt, long? parentId = null)
        {
            var project = await Db.Projects.SingleAsync(p => p.Id == ProjectId);
            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId,
                Number = project.NextIssueNumber++,
                Type = type,
                Title = $"a {type}",
                StatusId = statusId,
                Rank = 100,
                ParentId = parentId,
                ClosedAt = closedAt,
                CreatedBy = "hatch",
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        public async Task<string> BugAsync(int statusId, DateTimeOffset? closedAt) =>
            IssueKey.Format(ProjectKey, (await IssueAsync("bug", statusId, closedAt)).Number);
    }

    private static async Task<Harness> NewAsync(int horizonHours = 48)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var inProgress = new EfHatchStatus { Name = "in progress", SortOrder = 10 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true };
        var later = new EfHatchStatus { Name = "later", SortOrder = 30, IsDeferred = true };
        db.AddRange(project, inProgress, done, later);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Board = new BoardController(db, actors, TestClaims.With(), time, TestClaims.DoneHorizon(horizonHours)),
            ProjectId = project.Id,
            ProjectKey = project.Key,
            InProgress = inProgress.Id,
            Done = done.Id,
            Deferred = later.Id,
        };
    }
}

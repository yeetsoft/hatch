using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What the board read says about how long each card has stood in its column:
/// when it entered, and whether it is past the column's normal.
/// </summary>
public class BoardAgeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException("expected a value");

    [Fact]
    public async Task ACardPastNormal_IsAged_AndEntersAtItsNewestMove()
    {
        var h = await NewAsync();
        await h.FinishedStaysAsync(h.InProgress, 10, 3600);
        var card = await h.StandingAsync(h.InProgress, Now.AddHours(-3));

        var board = Value(await h.Board().GetBoard(default));

        var seen = board.Issues.Single(i => i.Key == h.KeyOf(card));
        Assert.True(seen.Aged);
        Assert.Equal(Now.AddHours(-3), seen.EnteredColumnAt);
    }

    [Fact]
    public async Task ACardWithNoMoves_EntersAtItsCreation()
    {
        var h = await NewAsync();
        var card = await h.IssueAsync("task", h.ToDo, parentId: null, createdAt: Now.AddDays(-4));

        var board = Value(await h.Board().GetBoard(default));

        Assert.Equal(Now.AddDays(-4), board.Issues.Single(i => i.Key == h.KeyOf(card)).EnteredColumnAt);
    }

    [Fact]
    public async Task ACardAtNormal_IsNotAged()
    {
        var h = await NewAsync();
        await h.FinishedStaysAsync(h.InProgress, 10, 3600);
        var card = await h.StandingAsync(h.InProgress, Now.AddHours(-1));

        var board = Value(await h.Board().GetBoard(default));

        Assert.False(board.Issues.Single(i => i.Key == h.KeyOf(card)).Aged);
    }

    [Fact]
    public async Task AStoryWithAnOpenChild_IsNotAged_HoweverOld()
    {
        var h = await NewAsync();
        await h.FinishedStaysAsync(h.InProgress, 10, 3600);
        var story = await h.StandingAsync(h.InProgress, Now.AddDays(-9), type: "story");
        await h.IssueAsync("task", h.ToDo, story.Id, Now.AddDays(-9));

        var board = Value(await h.Board().GetBoard(default));

        Assert.False(board.Issues.Single(i => i.Key == h.KeyOf(story)).Aged);
    }

    [Fact]
    public async Task UnderTenStays_GiveANullNormal_AndNothingAged()
    {
        var h = await NewAsync();
        await h.FinishedStaysAsync(h.InProgress, 9, 3600);
        await h.StandingAsync(h.InProgress, Now.AddDays(-9));

        var board = Value(await h.Board().GetBoard(default));

        Assert.Null(board.Normals!.Single(n => n.StatusId == h.InProgress).Seconds);
        Assert.DoesNotContain(board.Issues, i => i.Aged);
    }

    [Fact]
    public async Task NormalsHaveOneEntryForEveryActiveColumn()
    {
        var h = await NewAsync();

        var board = Value(await h.Board().GetBoard(default));

        Assert.Equal([h.InProgress, h.InReview], board.Normals!.Select(n => n.StatusId));
    }

    [Fact]
    public async Task AScopedReader_SeesTheSameNormals_AsAnUnscopedOne()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        await h.FinishedStaysAsync(h.InProgress, 10, 3600, projectId: other);
        var shared = new ColumnNormals();

        // The scoped reader asks first, so it is the one that fills the cache.
        var scoped = Value(await h.Board(shared, scopedTo: h.ProjectId).GetBoard(default));
        var unscoped = Value(await h.Board(shared).GetBoard(default));

        Assert.Equal(3600, scoped.Normals!.Single(n => n.StatusId == h.InProgress).Seconds);
        Assert.Equal(
            unscoped.Normals!.Select(n => (n.StatusId, n.Seconds)),
            scoped.Normals!.Select(n => (n.StatusId, n.Seconds)));
    }

    [Fact]
    public async Task At_IsTheBoardsInstant()
    {
        var h = await NewAsync();

        var board = Value(await h.Board().GetBoard(default));

        Assert.Equal(Now, board.At);
    }

    // ---- Harness ----

    private sealed class Harness
    {
        public required DbContextOptions<HatchContext> Options { get; init; }
        public required HatchContext Db { get; init; }
        public required int ProjectId { get; init; }
        public required string ProjectKey { get; init; }
        public required int ToDo { get; init; }
        public required int InProgress { get; init; }
        public required int InReview { get; init; }
        public required int Done { get; init; }

        public BoardController Board(ColumnNormals? normals = null, int? scopedTo = null) => new(
            scopedTo is { } id ? ScopeSeed.Request(Options, ScopeSeed.Only(id)) : Db,
            new StubActorDirectory(), TestClaims.With(), new FakeTimeProvider(Now), TestClaims.DoneHorizon(48),
            normals ?? new ColumnNormals());

        public string KeyOf(EfHatchIssue issue) => IssueKey.Format(ProjectKey, issue.Number);

        public async Task<EfHatchIssue> IssueAsync(
            string type, int statusId, long? parentId, DateTimeOffset createdAt, int? projectId = null)
        {
            var project = await Db.Projects.SingleAsync(p => p.Id == (projectId ?? ProjectId));
            var issue = new EfHatchIssue
            {
                ProjectId = project.Id,
                Number = project.NextIssueNumber++,
                Type = type,
                Title = $"a {type}",
                StatusId = statusId,
                Rank = 100,
                ParentId = parentId,
                CreatedBy = "hatch",
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
            };
            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        /// <summary>A card standing in <paramref name="statusId"/> since <paramref name="since"/>, by a status change.</summary>
        public async Task<EfHatchIssue> StandingAsync(int statusId, DateTimeOffset since, string type = "task")
        {
            var issue = await IssueAsync(type, statusId, null, since.AddDays(-30));
            await MoveAsync(issue.Id, since, ToDo, statusId);
            return issue;
        }

        /// <summary>
        /// <paramref name="count"/> issues that each stood in <paramref name="statusId"/> for
        /// <paramref name="seconds"/> and left it a day ago, on to review.
        /// </summary>
        public async Task FinishedStaysAsync(int statusId, int count, long seconds, int? projectId = null)
        {
            var left = Now.AddDays(-1);
            for (var i = 0; i < count; i++)
            {
                var issue = await IssueAsync("task", InReview, null, Now.AddDays(-20), projectId);
                await MoveAsync(issue.Id, left.AddSeconds(-seconds), ToDo, statusId);
                await MoveAsync(issue.Id, left, statusId, InReview);
            }
        }

        public async Task MoveAsync(long issueId, DateTimeOffset at, int from, int to)
        {
            var names = await Db.Statuses.ToDictionaryAsync(s => s.Id, s => s.Name);
            Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issueId,
                Actor = "someone",
                Kind = EfHatchIssueEvent.StatusChanged,
                Payload = $"{{\"from\":\"{names[from]}\",\"to\":\"{names[to]}\",\"fromId\":{from},\"toId\":{to}}}",
                At = at,
            });
            await Db.SaveChangesAsync();
        }

        public async Task<int> AddProjectAsync(string key)
        {
            var project = new EfHatchProject { Key = key, Name = key, CreatedAt = Now };
            Db.Add(project);
            await Db.SaveChangesAsync();
            return project.Id;
        }
    }

    private static async Task<Harness> NewAsync()
    {
        var options = new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new HatchContext(options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var toDo = new EfHatchStatus { Name = "to do", SortOrder = 10 };
        var inProgress = new EfHatchStatus { Name = "in progress", SortOrder = 20 };
        var inReview = new EfHatchStatus { Name = "in review", SortOrder = 30 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(project, toDo, inProgress, inReview, done);
        await db.SaveChangesAsync();

        return new Harness
        {
            Options = options,
            Db = db,
            ProjectId = project.Id,
            ProjectKey = project.Key,
            ToDo = toDo.Id,
            InProgress = inProgress.Id,
            InReview = inReview.Id,
            Done = done.Id,
        };
    }
}

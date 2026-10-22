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

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException("expected a value");

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required BoardController Board { get; init; }
        public required int ProjectId { get; init; }
        public required int InProgress { get; init; }
        public required int Done { get; init; }

        public async Task<string> BugAsync(int statusId, DateTimeOffset? closedAt)
        {
            var project = await Db.Projects.SingleAsync(p => p.Id == ProjectId);
            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId,
                Number = project.NextIssueNumber,
                Type = "bug",
                Title = "a bug",
                StatusId = statusId,
                Rank = 100,
                ClosedAt = closedAt,
                CreatedBy = "hatch",
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return IssueKey.Format(project.Key, issue.Number);
        }
    }

    private static async Task<Harness> NewAsync(int horizonHours = 48)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var inProgress = new EfHatchStatus { Name = "in progress", SortOrder = 10 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true };
        db.AddRange(project, inProgress, done);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Board = new BoardController(db, actors, TestClaims.With(), time, TestClaims.DoneHorizon(horizonHours)),
            ProjectId = project.Id,
            InProgress = inProgress.Id,
            Done = done.Id,
        };
    }
}

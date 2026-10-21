using Hatch.Api.Modules.Hatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The pure fold: how many issues sat in each board column at each instant,
/// pinned against hand-built histories the way <see cref="TimelineReportTests"/>
/// pins the per-issue segments it is built from. In memory throughout.
/// </summary>
public class CumulativeFlowReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Day(int n) => Now.AddDays(n);

    // First column 1, two active 2 and 3, review 4, terminal 5, deferred 6.
    private static List<EfHatchStatus> Board() =>
    [
        new() { Id = 1, Name = "backlog", SortOrder = 10 },
        new() { Id = 2, Name = "todo", SortOrder = 20 },
        new() { Id = 3, Name = "doing", SortOrder = 30 },
        new() { Id = 4, Name = "review", SortOrder = 40 },
        new() { Id = 5, Name = "done", SortOrder = 50, IsTerminal = true },
        new() { Id = 6, Name = "parked", SortOrder = 60, IsDeferred = true },
    ];

    private static TimelineReport.Row Issue(
        long id, string type, int status, DateTimeOffset createdAt, long? parent = null, int project = 1,
        string key = "HA", string by = "ann", long? rank = null, int? number = null) =>
        new(id, parent, project, key, number ?? (int)id, type, status, rank ?? id, createdAt, by);

    private static string NameOf(int id) => Board().Single(s => s.Id == id).Name;

    private static TimelineReport.Change Move(
        long issue, long eventId, DateTimeOffset at, int? from, int? to,
        bool names = true, bool ids = true, string? toName = null)
    {
        return new TimelineReport.Change(
            issue, eventId, at,
            names && from is { } f ? NameOf(f) : null,
            toName ?? (names && to is { } t ? NameOf(t) : null),
            ids ? from : null,
            ids ? to : null);
    }

    private static CumulativeFlowReport.Result Fold(
        TimelineReport.Row[] rows, TimelineReport.Change[]? changes = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, DateTimeOffset? now = null,
        List<EfHatchStatus>? board = null)
    {
        var resolvedTo = to ?? Day(5);
        return CumulativeFlowReport.Fold(
            rows, changes ?? [], board ?? Board(), from ?? Day(-5), resolvedTo, now ?? resolvedTo);
    }

    [Fact]
    public void Columns_AreNonDeferredIds_InSortOrderThenId()
    {
        var board = new List<EfHatchStatus>
        {
            new() { Id = 5, Name = "done", SortOrder = 50, IsTerminal = true },
            new() { Id = 2, Name = "todo", SortOrder = 20 },
            new() { Id = 6, Name = "parked", SortOrder = 20, IsDeferred = true },
            new() { Id = 3, Name = "doing", SortOrder = 20 },
            new() { Id = 1, Name = "backlog", SortOrder = 10 },
            new() { Id = 4, Name = "review", SortOrder = 40 },
        };

        var result = Fold([Issue(1, "epic", 1, Day(-1))], board: board);

        Assert.Equal([1, 2, 3, 4, 5], result.Columns);
    }

    [Fact]
    public void AMoveInstant_CountsTheColumnMovedTo_NotLeft_AndTheFirstColumnCountsToo()
    {
        var row = Issue(1, "epic", 1, Day(-10));
        var changes = new[] { Move(1, 1, Day(-3), 1, 2) };

        var result = Fold([row], changes, from: Day(-5), to: Day(0), now: Day(0));

        var before = result.Points.Single(p => p.At == Day(-5));
        var at = result.Points.Single(p => p.At == Day(-3));
        var after = result.Points.Single(p => p.At == Day(0));

        Assert.Equal(1, before.Counts[0]);
        Assert.Equal(0, before.Counts[1]);
        Assert.Equal(0, at.Counts[0]);
        Assert.Equal(1, at.Counts[1]);
        Assert.Equal(1, after.Counts[1]);
    }

    [Fact]
    public void ATerminalColumn_SinceBeforeFrom_CountsZero()
    {
        var row = Issue(1, "epic", 5, Day(-10));

        var result = Fold([row], from: Day(-5), to: Day(0), now: Day(0));

        Assert.All(result.Points, p => Assert.Equal(0, p.Counts[4]));
    }

    [Fact]
    public void ATerminalColumn_ClosedInsideTheWindow_CountsFromClose()
    {
        var row = Issue(1, "epic", 3, Day(-10));
        var changes = new[] { Move(1, 1, Day(-2), 3, 5) };

        var result = Fold([row], changes, from: Day(-5), to: Day(0), now: Day(0));

        Assert.Equal(0, result.Points.Single(p => p.At == Day(-5)).Counts[4]);
        Assert.Equal(1, result.Points.Single(p => p.At == Day(-2)).Counts[4]);
        Assert.Equal(1, result.Points.Single(p => p.At == Day(0)).Counts[4]);
    }

    [Fact]
    public void ATerminalColumn_OpeningExactlyAtFrom_Counts()
    {
        var row = Issue(1, "epic", 3, Day(-10));
        var changes = new[] { Move(1, 1, Day(-5), 3, 5) };

        var result = Fold([row], changes, from: Day(-5), to: Day(0), now: Day(0));

        Assert.Equal(1, result.Points.Single(p => p.At == Day(-5)).Counts[4]);
    }

    [Fact]
    public void ClosedBeforeFrom_ReopenedThenClosedAgainInsideTheWindow()
    {
        var row = Issue(1, "epic", 3, Day(-10));
        var changes = new[]
        {
            Move(1, 1, Day(-8), 3, 5),
            Move(1, 2, Day(-3), 5, 3),
            Move(1, 3, Day(-1), 3, 5),
        };

        var result = Fold([row], changes, from: Day(-5), to: Day(0), now: Day(0));

        Assert.Equal(0, result.Points.Single(p => p.At == Day(-5)).Counts[4]);
        Assert.Equal(1, result.Points.Single(p => p.At == Day(-3)).Counts[2]);
        Assert.Equal(0, result.Points.Single(p => p.At == Day(-3)).Counts[4]);
        Assert.Equal(1, result.Points.Single(p => p.At == Day(-1)).Counts[4]);
    }

    [Fact]
    public void TwoTerminalColumns_AreBothBaselined()
    {
        var board = new List<EfHatchStatus>
        {
            new() { Id = 1, Name = "backlog", SortOrder = 10 },
            new() { Id = 2, Name = "doing", SortOrder = 20 },
            new() { Id = 5, Name = "done", SortOrder = 50, IsTerminal = true },
            new() { Id = 7, Name = "wontfix", SortOrder = 60, IsTerminal = true },
        };
        var rows = new[] { Issue(1, "epic", 5, Day(-10)), Issue(2, "epic", 7, Day(-10)) };

        var result = Fold(rows, from: Day(-5), to: Day(0), now: Day(0), board: board);

        Assert.All(result.Points, p =>
        {
            Assert.Equal(0, p.Counts[2]);
            Assert.Equal(0, p.Counts[3]);
        });
    }

    [Fact]
    public void AcrossSeveralMoves_EachPointCountsTheColumnHeldAtThatInstant()
    {
        var row = Issue(1, "epic", 5, Day(-10));
        var changes = new[]
        {
            Move(1, 1, Day(-8), 1, 2),
            Move(1, 2, Day(-6), 2, 3),
            Move(1, 3, Day(-4), 3, 4),
            Move(1, 4, Day(-2), 4, 5),
        };

        var result = Fold([row], changes, from: Day(-9), to: Day(0), now: Day(0));

        Assert.Equal(1, result.Points.Single(p => p.At == Day(-9)).Counts[0]);
        Assert.Equal(1, result.Points.Single(p => p.At == Day(-8)).Counts[1]);
        Assert.Equal(1, result.Points.Single(p => p.At == Day(-6)).Counts[2]);
        Assert.Equal(1, result.Points.Single(p => p.At == Day(-4)).Counts[3]);
        Assert.Equal(1, result.Points.Single(p => p.At == Day(-2)).Counts[4]);
    }

    [Fact]
    public void ADeferredStretch_IsInNoColumn_AndDoesNotRaiseUnresolved()
    {
        var row = Issue(1, "epic", 2, Day(-10));
        var changes = new[]
        {
            Move(1, 1, Day(-8), 1, 2),
            Move(1, 2, Day(-6), 2, 6),
            Move(1, 3, Day(-4), 6, 2),
        };

        var result = Fold([row], changes, from: Day(-9), to: Day(0), now: Day(0));

        var during = result.Points.Single(p => p.At == Day(-6));
        Assert.Equal(0, during.Counts.Sum());
        Assert.Equal(0, result.Unresolved);
    }

    [Fact]
    public void AnUnresolvedStretch_IsInNoColumn_CountsOnce_AndMatchesTimelinesUnresolved()
    {
        // Status 999 names no current column: unresolved for the issue's whole life.
        var row = Issue(1, "epic", 999, Day(-10));
        var from = Day(-9);
        var to = Day(0);

        var result = Fold([row], from: from, to: to, now: to);

        Assert.Equal(1, result.Unresolved);
        Assert.All(result.Points, p => Assert.Equal(0, p.Counts.Sum()));

        var timeline = TimelineReport.Fold([row], [], Board(), from, to, null, null);
        Assert.Equal(timeline.Unresolved, result.Unresolved);
    }

    [Fact]
    public void AZeroLengthStretch_IsDroppedBeforeCounting_EitherWay()
    {
        var row = Issue(1, "epic", 1, Day(-10));
        var changes = new[]
        {
            Move(1, 1, Day(-5), 1, 2),
            Move(1, 2, Day(-5), 2, 3),
        };

        var result = Fold([row], changes, from: Day(-9), to: Day(0), now: Day(0));

        Assert.Equal(0, result.Unresolved);
        var at = result.Points.Single(p => p.At == Day(-5));
        Assert.Equal(0, at.Counts[1]);
        Assert.Equal(1, at.Counts[2]);
    }

    [Fact]
    public void TwoIssues_AddInTheSameColumn()
    {
        var rows = new[] { Issue(1, "epic", 3, Day(-10)), Issue(2, "story", 3, Day(-8)) };

        var result = Fold(rows, from: Day(-9), to: Day(0), now: Day(0));

        Assert.Equal(2, result.Points.Last().Counts[2]);
    }

    [Fact]
    public void EveryType_Counts_EpicStoryAndTask()
    {
        var rows = new[]
        {
            Issue(1, "epic", 2, Day(-10)),
            Issue(2, "story", 3, Day(-10)),
            Issue(3, "task", 4, Day(-10)),
        };

        var result = Fold(rows, from: Day(-5), to: Day(0), now: Day(0));

        var point = result.Points.Last();
        Assert.Equal(1, point.Counts[1]);
        Assert.Equal(1, point.Counts[2]);
        Assert.Equal(1, point.Counts[3]);
    }

    [Fact]
    public void ToAfterNow_EndsTheSweepAtNow_ButEchoesToAsGiven()
    {
        var row = Issue(1, "epic", 3, Day(-10));
        var changes = new[] { Move(1, 1, Day(-2), 1, 3) };
        var now = Day(0);
        var to = Day(10);

        var result = Fold([row], changes, from: Day(-5), to: to, now: now);

        Assert.Equal(to, result.To);
        Assert.Equal(now, result.Points.Last().At);
        Assert.Equal(1, result.Points.Last().Counts[2]);
    }

    [Fact]
    public void ToBeforeNow_OffGrid_EndsWithAPointAtTo_OnGrid_AddsNone()
    {
        var row = Issue(1, "epic", 1, Day(-10));
        var from = Day(-5);
        var now = Day(0);

        var offGrid = Fold([row], from: from, to: from.AddHours(2.5), now: now);
        Assert.Equal(
            [from, from.AddHours(1), from.AddHours(2), from.AddHours(2.5)],
            offGrid.Points.Select(p => p.At));

        var onGrid = Fold([row], from: from, to: from.AddHours(3), now: now);
        Assert.Equal(
            [from, from.AddHours(1), from.AddHours(2), from.AddHours(3)],
            onGrid.Points.Select(p => p.At));
    }

    [Fact]
    public void NoIssues_AnswersEmptyPoints_ColumnsListed_UnresolvedZero_FromUntouched()
    {
        var result = Fold([], from: Day(-5), to: Day(5), now: Day(5));

        Assert.Empty(result.Points);
        Assert.Equal([1, 2, 3, 4, 5], result.Columns);
        Assert.Equal(0, result.Unresolved);
        Assert.Equal(Day(-5), result.From);
    }

    [Fact]
    public void EveryIssueCreatedAfterEnd_AnswersTheSameAsNoIssues()
    {
        var row = Issue(1, "epic", 1, Day(10));

        var result = Fold([row], from: Day(-5), to: Day(5), now: Day(5));

        Assert.Empty(result.Points);
        Assert.Equal(0, result.Unresolved);
        Assert.Equal(Day(-5), result.From);
    }

    [Fact]
    public void ClampPullsFromForwardToFirstCreatedAt_WhenStartIsEarlier()
    {
        var row = Issue(1, "epic", 2, Day(-3));

        var result = Fold([row], from: Day(-10), to: Day(0), now: Day(0));

        Assert.Equal(Day(-3), result.From);
        Assert.Equal(Day(-3), result.Points.First().At);
        Assert.Equal(1, result.Points.First().Counts[1]);
    }

    [Fact]
    public void ClampLeavesFromUntouched_WhenStartIsAlreadyAtOrAfterFirstCreatedAt()
    {
        var row = Issue(1, "epic", 2, Day(-10));

        var result = Fold([row], from: Day(-5), to: Day(0), now: Day(0));

        Assert.Equal(Day(-5), result.From);
    }

    [Fact]
    public void ClampLeavesFromUntouched_WhenFirstCreatedAtIsAfterEnd()
    {
        var row = Issue(1, "epic", 2, Day(10));

        var result = Fold([row], from: Day(-5), to: Day(0), now: Day(0));

        Assert.Equal(Day(-5), result.From);
        Assert.Empty(result.Points);
    }

    [Theory]
    [InlineData(999, "hour")]
    [InlineData(1000, "day")]
    [InlineData(998.5, "hour")]
    [InlineData(999.5, "day")]
    public void StepRule_AtTheHourDayBoundary(double hours, string expected)
    {
        var from = Day(0);
        var to = from.AddHours(hours);
        var row = Issue(1, "epic", 1, from);

        var result = Fold([row], from: from, to: to, now: to);

        Assert.Equal(expected, result.Step);
    }

    [Fact]
    public void StepRule_999Hours_Is1000Points()
    {
        var from = Day(0);
        var to = from.AddHours(999);
        var row = Issue(1, "epic", 1, from);

        var result = Fold([row], from: from, to: to, now: to);

        Assert.Equal("hour", result.Step);
        Assert.Equal(1000, result.Points.Count);
    }

    [Theory]
    [InlineData(999, "day")]
    [InlineData(1000, "week")]
    public void StepRule_AtTheDayWeekBoundary(double days, string expected)
    {
        var from = Day(0);
        var to = from.AddDays(days);
        var row = Issue(1, "epic", 1, from);

        var result = Fold([row], from: from, to: to, now: to);

        Assert.Equal(expected, result.Step);
    }

    [Fact]
    public void StepRule_AThirtyYearRange_IsWeek_EvenOverCap()
    {
        var from = Day(0);
        var to = from.AddYears(30);
        var row = Issue(1, "epic", 1, from);

        var result = Fold([row], from: from, to: to, now: to);

        Assert.Equal("week", result.Step);
        Assert.True(result.Points.Count > WorkLogController.MaxBuckets);
    }
}

/// <summary>
/// The route: the same history <see cref="TimelineReportControllerTests"/>
/// loads, folded by <see cref="CumulativeFlowReport"/> instead and answered
/// as a <see cref="CumulativeFlowDto"/>.
/// </summary>
public class CumulativeFlowControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Ninety = TimeSpan.FromDays(90);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required ReportsController Reports { get; init; }
        public required int ProjectId { get; init; }
    }

    /// <summary>One project, and a board of five columns: first, two active, terminal, deferred.</summary>
    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.AddRange(
            project,
            new EfHatchStatus { Name = "backlog", SortOrder = 10 },
            new EfHatchStatus { Name = "todo", SortOrder = 20 },
            new EfHatchStatus { Name = "doing", SortOrder = 30 },
            new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true },
            new EfHatchStatus { Name = "parked", SortOrder = 50, IsDeferred = true });
        await db.SaveChangesAsync();

        return new Harness
        {
            Db = db,
            Reports = new ReportsController(
                db, new StubActorDirectory(), TestClaims.With(), new FakeTimeProvider(Now)),
            ProjectId = project.Id,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");

    private static string Refusal<T>(ActionResult<T> result) =>
        Assert.IsType<string>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);

    private static async Task<EfHatchIssue> AddIssueAsync(Harness h, int statusId, DateTimeOffset createdAt)
    {
        var issue = new EfHatchIssue
        {
            ProjectId = h.ProjectId,
            Number = await h.Db.Issues.CountAsync(i => i.ProjectId == h.ProjectId) + 1,
            Type = "epic", Title = "epic", Description = "", StatusId = statusId, Rank = 1024,
            CreatedBy = "ann", CreatedAt = createdAt, UpdatedAt = createdAt,
        };
        h.Db.Issues.Add(issue);
        await h.Db.SaveChangesAsync();
        return issue;
    }

    private static async Task MoveAsync(Harness h, long issueId, string payload, int daysAgo)
    {
        h.Db.IssueEvents.Add(new EfHatchIssueEvent
        {
            IssueId = issueId, Actor = "ann", Kind = EfHatchIssueEvent.StatusChanged,
            Payload = payload, At = Now.AddDays(-daysAgo),
        });
        await h.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task NoRange_IsTheNinetyDaysEndingNow_OnAnEmptyBoard()
    {
        var h = await NewAsync();

        var flow = Value(await h.Reports.GetCumulativeFlow(null, null, default));

        Assert.Equal(Now, flow.To);
        Assert.Equal(Now - Ninety, flow.From);
        Assert.Empty(flow.Points);
    }

    [Fact]
    public async Task FromOnly_EndsNow()
    {
        var h = await NewAsync();
        var from = Now.AddDays(-5);

        var flow = Value(await h.Reports.GetCumulativeFlow(from.ToString("O"), null, default));

        Assert.Equal(from, flow.From);
        Assert.Equal(Now, flow.To);
    }

    [Fact]
    public async Task ToOnly_BeginsNinetyDaysBeforeIt()
    {
        var h = await NewAsync();
        var to = Now.AddDays(-10);

        var flow = Value(await h.Reports.GetCumulativeFlow(null, to.ToString("O"), default));

        Assert.Equal(to, flow.To);
        Assert.Equal(to - Ninety, flow.From);
    }

    [Fact]
    public async Task AnInvertedRange_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Reports.GetCumulativeFlow(Now.ToString("O"), Now.AddDays(-1).ToString("O"), default);

        Assert.Equal("a range ends before it begins", Refusal(result));
    }

    [Theory]
    [InlineData("yesterday", null)]
    [InlineData(null, "next week")]
    public async Task AnUnparseableBound_IsRefusedByName(string? from, string? to)
    {
        var h = await NewAsync();

        var result = await h.Reports.GetCumulativeFlow(from, to, default);

        Assert.Equal(RangeBounds.NotAnInstant(from ?? to), Refusal(result));
    }

    [Fact]
    public async Task EndToEnd_SeededIssueAndMove_AnswersTheBoardsColumnsAndTheCountAtEachPoint()
    {
        var h = await NewAsync();
        var issue = await AddIssueAsync(h, statusId: 1, createdAt: Now.AddDays(-20));
        await MoveAsync(h, issue.Id, "{\"from\":\"backlog\",\"to\":\"doing\",\"fromId\":1,\"toId\":3}", 10);

        var flow = Value(await h.Reports.GetCumulativeFlow(Now.AddDays(-15).ToString("O"), Now.ToString("O"), default));

        Assert.Equal([1, 2, 3, 4], flow.Columns);
        Assert.Equal(0, flow.Unresolved);

        var before = flow.Points.First(p => p.At == Now.AddDays(-15));
        Assert.Equal(1, before.Counts[0]);
        Assert.Equal(0, before.Counts[2]);

        var after = flow.Points.Last();
        Assert.Equal(Now, after.At);
        Assert.Equal(0, after.Counts[0]);
        Assert.Equal(1, after.Counts[2]);
    }
}

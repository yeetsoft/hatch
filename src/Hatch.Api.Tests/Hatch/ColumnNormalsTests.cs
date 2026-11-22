using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// How long a column's stays normally last: the flow fold's own ninetieth
/// percentile, over childless issues, over the last thirty days, kept five minutes.
/// </summary>
public class ColumnNormalsTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 0, 0, 0, TimeSpan.Zero);

    // First column 1, three active 2, 3 and 4, terminal 5, deferred 6.
    private static List<EfHatchStatus> Board() =>
    [
        new() { Id = 1, Name = "backlog", SortOrder = 10 },
        new() { Id = 2, Name = "todo", SortOrder = 20 },
        new() { Id = 3, Name = "doing", SortOrder = 30 },
        new() { Id = 4, Name = "review", SortOrder = 40 },
        new() { Id = 5, Name = "done", SortOrder = 50, IsTerminal = true },
        new() { Id = 6, Name = "parked", SortOrder = 60, IsDeferred = true },
    ];

    private static TimelineReport.Row Issue(long id, string type, int status, DateTimeOffset createdAt, long? parent = null) =>
        new(id, parent, 1, "HA", (int)id, type, status, id, createdAt, "ann");

    private static string NameOf(int id) => Board().Single(s => s.Id == id).Name;

    private static FlowReport.Event Move(long issue, long eventId, DateTimeOffset at, int from, int to) =>
        new(issue, eventId, EfHatchIssueEvent.StatusChanged, at,
            $"{{\"from\":\"{NameOf(from)}\",\"to\":\"{NameOf(to)}\",\"fromId\":{from},\"toId\":{to}}}");

    /// <summary>
    /// One issue per entry of <paramref name="seconds"/>, each of which entered column 3
    /// <c>seconds[i]</c> before <paramref name="left"/> and left it, for review, at <paramref name="left"/>.
    /// </summary>
    private static (List<TimelineReport.Row> Rows, List<FlowReport.Event> Events) Stays(
        long[] seconds, DateTimeOffset left, long firstId = 100)
    {
        var rows = new List<TimelineReport.Row>();
        var events = new List<FlowReport.Event>();
        for (var i = 0; i < seconds.Length; i++)
        {
            var id = firstId + i;
            rows.Add(Issue(id, "task", 4, left.AddDays(-40)));
            events.Add(Move(id, id * 10, left.AddSeconds(-seconds[i]), 2, 3));
            events.Add(Move(id, id * 10 + 1, left, 3, 4));
        }

        return (rows, events);
    }

    private static long? SecondsOf(IReadOnlyList<ColumnNormals.Normal> normals, int id) =>
        normals.Single(n => n.StatusId == id).Seconds;

    private static readonly long[] OneToTen = [60, 120, 180, 240, 300, 360, 420, 480, 540, 600];

    [Fact]
    public void TenStays_GiveTheNearestRankNinetiethPercentile()
    {
        var (rows, events) = Stays(OneToTen, Now.AddDays(-1));

        var normals = ColumnNormals.Fold(rows, events, Board(), Now);

        // Nearest rank: ceil(0.9 * 10) = the ninth value.
        Assert.Equal(540, SecondsOf(normals, 3));
    }

    [Fact]
    public void NineStays_GiveNull()
    {
        var (rows, events) = Stays(OneToTen[..9], Now.AddDays(-1));

        var normals = ColumnNormals.Fold(rows, events, Board(), Now);

        Assert.Null(SecondsOf(normals, 3));
    }

    [Fact]
    public void EveryActiveColumn_HasAnEntry_InBoardOrder()
    {
        var normals = ColumnNormals.Fold([], [], Board(), Now);

        Assert.Equal([2, 3, 4], normals.Select(n => n.StatusId));
        Assert.All(normals, n => Assert.Null(n.Seconds));
    }

    [Fact]
    public void AParentsStay_IsLeftOut()
    {
        // Nine children's stays and the parent's own would be ten - but the parent is not counted.
        var (rows, events) = Stays(OneToTen[..9], Now.AddDays(-1));
        rows.Add(Issue(1, "story", 4, Now.AddDays(-20)));
        events.Add(Move(1, 1, Now.AddDays(-3), 2, 3));
        events.Add(Move(1, 2, Now.AddDays(-1), 3, 4));
        rows[0] = rows[0] with { ParentId = 1 };

        var normals = ColumnNormals.Fold(rows, events, Board(), Now);

        Assert.Null(SecondsOf(normals, 3));
    }

    [Fact]
    public void TheFirstColumn_ATerminalOne_AndADeferredOne_GetNoEntry_EvenRenamed()
    {
        var board = Board();
        board[0].Name = "doing";
        board[4].Name = "in progress";
        board[5].Name = "review";

        var normals = ColumnNormals.Fold([], [], board, Now);

        Assert.Equal([2, 3, 4], normals.Select(n => n.StatusId));
    }

    [Fact]
    public void AStay_ThatBeganBeforeTheWindow_AndEndedInIt_CountsWhole()
    {
        var left = Now.AddDays(-10);
        var thirtyFiveDays = (long)TimeSpan.FromDays(35).TotalSeconds;
        var (rows, events) = Stays([.. OneToTen[..9], thirtyFiveDays], left);

        var normals = ColumnNormals.Fold(rows, events, Board(), Now);

        // Ten stays, the longest of them 35 days: it counts, and the ninth value is 540.
        Assert.Equal(540, SecondsOf(normals, 3));
    }

    [Fact]
    public void AStay_ThatEndedBeforeTheWindow_DoesNotCount()
    {
        var (rows, events) = Stays(OneToTen[..9], Now.AddDays(-1));
        var (oldRows, oldEvents) = Stays([100], Now.AddDays(-31), firstId: 500);

        var normals = ColumnNormals.Fold([.. rows, .. oldRows], [.. events, .. oldEvents], Board(), Now);

        Assert.Null(SecondsOf(normals, 3));
    }

    [Fact]
    public void LeavingAndReturning_IsTwoStays()
    {
        var rows = new List<TimelineReport.Row>();
        var events = new List<FlowReport.Event>();
        long eventId = 1;
        for (long id = 1; id <= 5; id++)
        {
            rows.Add(Issue(id, "task", 4, Now.AddDays(-20)));
            events.Add(Move(id, eventId++, Now.AddDays(-10), 2, 3));
            events.Add(Move(id, eventId++, Now.AddDays(-9), 3, 2));
            events.Add(Move(id, eventId++, Now.AddDays(-8), 2, 3));
            events.Add(Move(id, eventId++, Now.AddDays(-7), 3, 4));
        }

        var normals = ColumnNormals.Fold(rows, events, Board(), Now);

        // Five issues, two stays each: ten stays in column 3.
        Assert.NotNull(SecondsOf(normals, 3));
    }

    // ---- ForAsync: the cache ----

    private static HatchContext NewDb() =>
        new(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<List<EfHatchStatus>> SeedBoardAsync(HatchContext db)
    {
        db.Projects.Add(new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now });
        var statuses = new List<EfHatchStatus>
        {
            new() { Name = "backlog", SortOrder = 10 },
            new() { Name = "todo", SortOrder = 20 },
            new() { Name = "doing", SortOrder = 30 },
            new() { Name = "done", SortOrder = 40, IsTerminal = true },
        };
        db.Statuses.AddRange(statuses);
        await db.SaveChangesAsync();
        return statuses;
    }

    private static async Task AddStaysAsync(HatchContext db, List<EfHatchStatus> statuses, int count, long seconds)
    {
        var project = await db.Projects.SingleAsync();
        var left = Now.AddDays(-1);
        for (var i = 0; i < count; i++)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = project.Id,
                Number = project.NextIssueNumber++,
                Type = "task",
                Title = "a task",
                StatusId = statuses[3].Id,
                Rank = 100,
                CreatedBy = "hatch",
                CreatedAt = Now.AddDays(-20),
                UpdatedAt = Now.AddDays(-20),
            };
            db.Issues.Add(issue);
            await db.SaveChangesAsync();
            db.IssueEvents.Add(Moved(issue.Id, left.AddSeconds(-seconds), statuses[1], statuses[2]));
            db.IssueEvents.Add(Moved(issue.Id, left, statuses[2], statuses[3]));
            await db.SaveChangesAsync();
        }
    }

    private static EfHatchIssueEvent Moved(long issueId, DateTimeOffset at, EfHatchStatus from, EfHatchStatus to) => new()
    {
        IssueId = issueId,
        Actor = "someone",
        Kind = EfHatchIssueEvent.StatusChanged,
        Payload = $"{{\"from\":\"{from.Name}\",\"to\":\"{to.Name}\",\"fromId\":{from.Id},\"toId\":{to.Id}}}",
        At = at,
    };

    [Fact]
    public async Task ASecondCall_InsideFiveMinutes_DoesNotReadAgain()
    {
        var db = NewDb();
        var statuses = await SeedBoardAsync(db);
        await AddStaysAsync(db, statuses, 10, 600);
        var normals = new ColumnNormals();

        var first = await normals.ForAsync(db, statuses, Now, default);
        await AddStaysAsync(db, statuses, 20, 6000);
        var second = await normals.ForAsync(db, statuses, Now.AddMinutes(4), default);

        Assert.Equal(600, SecondsOf(first, statuses[2].Id));
        Assert.Equal(600, SecondsOf(second, statuses[2].Id));
    }

    [Fact]
    public async Task ACall_AfterFiveMinutes_ReadsAgain()
    {
        var db = NewDb();
        var statuses = await SeedBoardAsync(db);
        await AddStaysAsync(db, statuses, 10, 600);
        var normals = new ColumnNormals();

        await normals.ForAsync(db, statuses, Now, default);
        await AddStaysAsync(db, statuses, 20, 6000);
        var later = await normals.ForAsync(db, statuses, Now.AddMinutes(5), default);

        Assert.Equal(6000, SecondsOf(later, statuses[2].Id));
    }

    [Fact]
    public async Task AChangedFlag_RecomputesAtOnce()
    {
        var db = NewDb();
        var statuses = await SeedBoardAsync(db);
        await AddStaysAsync(db, statuses, 10, 600);
        var normals = new ColumnNormals();

        var first = await normals.ForAsync(db, statuses, Now, default);
        Assert.Contains(first, n => n.StatusId == statuses[2].Id);

        statuses[2].IsDeferred = true;
        var second = await normals.ForAsync(db, statuses, Now.AddSeconds(1), default);

        Assert.DoesNotContain(second, n => n.StatusId == statuses[2].Id);
    }

    [Fact]
    public async Task ACancelledRead_CachesNothing()
    {
        var db = NewDb();
        var statuses = await SeedBoardAsync(db);
        await AddStaysAsync(db, statuses, 10, 600);
        var normals = new ColumnNormals();

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => normals.ForAsync(db, statuses, Now, cancelled.Token));

        var answer = await normals.ForAsync(db, statuses, Now.AddSeconds(1), default);

        Assert.Equal(600, SecondsOf(answer, statuses[2].Id));
    }
}

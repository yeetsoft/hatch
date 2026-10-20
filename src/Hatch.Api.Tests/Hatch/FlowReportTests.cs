using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The per-column half of the Flow fold: entries, leaves, dwell and standing,
/// over a half-open window, with no clock and no database.
/// </summary>
public class FlowReportTests
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

    private static string NameJson(int? id) => id is { } v ? $"\"{NameOf(v)}\"" : "null";

    private static string IdJson(int? id) => id is { } v ? v.ToString() : "null";

    private static FlowReport.Event Move(long issue, long eventId, DateTimeOffset at, int? from, int? to) =>
        new(issue, eventId, EfHatchIssueEvent.StatusChanged, at,
            $"{{\"from\":{NameJson(from)},\"to\":{NameJson(to)},\"fromId\":{IdJson(from)},\"toId\":{IdJson(to)}}}");

    private static FlowReport.Result Fold(
        TimelineReport.Row[] rows, FlowReport.Event[]? events = null, DateTimeOffset? from = null, DateTimeOffset? to = null,
        List<EfHatchStatus>? board = null, int? projectId = null, string? type = null) =>
        FlowReport.Fold(
            rows, events ?? [], [], [], board ?? Board(), from ?? Day(-5), to ?? Day(5), projectId, type);

    [Fact]
    public void Columns_AreTheBoardMinusDeferred_InSortOrder_RegardlessOfIds_AndOfNames()
    {
        var board = new List<EfHatchStatus>
        {
            new() { Id = 30, Name = "x", SortOrder = 3 },
            new() { Id = 10, Name = "x", SortOrder = 1 },
            new() { Id = 99, Name = "x", SortOrder = 5, IsDeferred = true },
            new() { Id = 20, Name = "x", SortOrder = 2 },
        };

        var result = Fold([], board: board);

        Assert.Equal([10, 20, 30], result.Columns.Select(c => c.StatusId));
    }

    [Fact]
    public void ACreatedThenMovedIssue_CountsACreationEntry_ThenALeaveAndAnEnter()
    {
        var issue = Issue(1, "task", 2, Day(-2));
        var events = new[] { Move(1, 1, Day(1), 2, 3) };

        var result = Fold([issue], events);

        var col2 = result.Columns.Single(c => c.StatusId == 2);
        var col3 = result.Columns.Single(c => c.StatusId == 3);
        Assert.Equal(1, col2.Entered);
        Assert.Equal(1, col2.Left);
        Assert.Equal(1, col3.Entered);
        Assert.Equal(0, col3.Left);
    }

    [Fact]
    public void AStretchBeginningBeforeFrom_EndingInside_CountsALeaveAndADwell_ButNoEntry()
    {
        var issue = Issue(1, "task", 2, Day(-20));
        var events = new[] { Move(1, 1, Day(-1), 2, 3) };

        var result = Fold([issue], events);

        var col2 = result.Columns.Single(c => c.StatusId == 2);
        Assert.Equal(0, col2.Entered);
        Assert.Equal(1, col2.Left);
        Assert.Equal(1, col2.Dwelled);
    }

    [Fact]
    public void MedianAndP90_AreComputedOverTheDwellList_AndNullWithNone()
    {
        var rows = new List<TimelineReport.Row>();
        var events = new List<FlowReport.Event>();
        long nextIssue = 1;
        long nextEvent = 1;

        // Enters statusId directly (no prior column, so nothing else is touched)
        // deep before the window and leaves at leaveAt, so the dwell equals
        // exactly dwellDays - the leave is what lands in the window.
        void Dwell(int statusId, DateTimeOffset leaveAt, int dwellDays)
        {
            var id = nextIssue++;
            rows.Add(Issue(id, "task", statusId, Day(-1000)));
            events.Add(Move(id, nextEvent++, leaveAt.AddDays(-dwellDays), null, statusId));
            events.Add(Move(id, nextEvent++, leaveAt, statusId, 5));
        }

        foreach (var d in new[] { 1, 2, 3 }) Dwell(2, Day(-4), d); // odd median: the middle, 2 days
        foreach (var d in new[] { 1, 2, 3, 4 }) Dwell(3, Day(-3), d); // even median: 2 and 3 days averaged up
        foreach (var d in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }) Dwell(4, Day(-2), d); // p90 of ten picks the ninth

        var result = Fold(rows.ToArray(), events.ToArray());

        var col2 = result.Columns.Single(c => c.StatusId == 2);
        Assert.Equal(3, col2.Dwelled);
        Assert.Equal((long)TimeSpan.FromDays(2).TotalSeconds, col2.MedianDwellSeconds);

        var col3 = result.Columns.Single(c => c.StatusId == 3);
        Assert.Equal(4, col3.Dwelled);
        Assert.Equal((long)TimeSpan.FromDays(2.5).TotalSeconds, col3.MedianDwellSeconds);

        var col4 = result.Columns.Single(c => c.StatusId == 4);
        Assert.Equal(10, col4.Dwelled);
        Assert.Equal((long)TimeSpan.FromDays(9).TotalSeconds, col4.P90DwellSeconds);

        var col1 = result.Columns.Single(c => c.StatusId == 1);
        Assert.Equal(0, col1.Dwelled);
        Assert.Null(col1.MedianDwellSeconds);
        Assert.Null(col1.P90DwellSeconds);
    }

    [Fact]
    public void Standing_IsInTheColumnAtTo_NotWhenItLeftBefore_OrWasCreatedAfter()
    {
        var stayed = Issue(1, "task", 2, Day(-10));
        var left = Issue(2, "task", 2, Day(-10));
        var leftEvents = new[] { Move(2, 1, Day(-1), 2, 3) };
        var createdAfter = Issue(3, "task", 2, Day(10));

        var result = Fold([stayed, left, createdAfter], leftEvents);

        var col2 = result.Columns.Single(c => c.StatusId == 2);
        Assert.Equal(1, col2.Standing);
    }

    [Fact]
    public void HalfOpenWindow_AnEventExactlyAtFrom_Counts_ExactlyAtTo_DoesNot()
    {
        var from = Day(-5);
        var to = Day(5);
        var atFrom = Issue(1, "task", 1, Day(-20));
        var atFromEvents = new[] { Move(1, 1, from, 1, 2) };
        var atTo = Issue(2, "task", 1, Day(-20));
        var atToEvents = new[] { Move(2, 1, to, 1, 3) };

        var result = Fold([atFrom, atTo], [.. atFromEvents, .. atToEvents], from, to);

        Assert.Equal(1, result.Columns.Single(c => c.StatusId == 2).Entered);
        Assert.Equal(0, result.Columns.Single(c => c.StatusId == 3).Entered);
    }

    [Fact]
    public void UnresolvedStretches_AreCounted_AndContributeNoEntryOfTheirOwn()
    {
        var nameOnly = Issue(1, "task", 1, Day(-1));
        var nameOnlyEvent = new FlowReport.Event(1, 1, EfHatchIssueEvent.StatusChanged, Day(0), """{"from":"backlog","to":"gone"}""");
        var idGone = Issue(2, "task", 1, Day(-1));
        var idGoneEvent = new FlowReport.Event(2, 1, EfHatchIssueEvent.StatusChanged, Day(0), """{"from":"backlog","to":"doing","fromId":1,"toId":99}""");

        var result = Fold([nameOnly, idGone], [nameOnlyEvent, idGoneEvent]);

        Assert.Equal(2, result.Unresolved);
        // Both rows still had a valid first stretch in column 1 before the unresolved one; only that counts.
        Assert.Equal(2, result.Columns.Sum(c => c.Entered));
        Assert.Equal(2, result.Columns.Sum(c => c.Left));
    }

    [Fact]
    public void ADeferredStretch_CountsNoColumn_ButTheStretchBeforeAndAfterStillDo()
    {
        var issue = Issue(1, "task", 1, Day(-10));
        var events = new[]
        {
            Move(1, 1, Day(-2), 1, 2),
            Move(1, 2, Day(0), 2, 6),
            Move(1, 3, Day(2), 6, 3),
        };

        var result = Fold([issue], events);

        var col2 = result.Columns.Single(c => c.StatusId == 2);
        var col3 = result.Columns.Single(c => c.StatusId == 3);
        Assert.Equal(1, col2.Entered);
        Assert.Equal(1, col2.Left);
        Assert.Equal(1, col3.Entered);
        Assert.Equal(5, result.Columns.Count); // the deferred column draws no column of its own
    }

    [Fact]
    public void ProjectIdAndType_EachNarrowTheRows_AndNoRows_IsZerosWithNoException()
    {
        var rows = new[]
        {
            Issue(1, "task", 2, Day(-1), project: 1),
            Issue(2, "bug", 2, Day(-1), project: 2),
        };

        var byProject = Fold(rows, projectId: 1);
        Assert.Equal(1, byProject.Columns.Single(c => c.StatusId == 2).Entered);

        var byType = Fold(rows, type: "bug");
        Assert.Equal(1, byType.Columns.Single(c => c.StatusId == 2).Entered);

        var none = Fold([]);
        Assert.All(none.Columns, c => Assert.Equal(0, c.Entered));
        Assert.Equal(0, none.Unresolved);
    }
}

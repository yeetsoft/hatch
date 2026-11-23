using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The pure fold behind the board-health read: bars of moves, bands read at
/// each bar's end and what waits on a person, pinned against boards and
/// histories built in memory. No route, no database.
/// </summary>
public class BoardHealthReportTests
{
    // An 8-hour window is 20-minute bars: the last starts 12:00, so From is 04:20 and To 12:20.
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 7, 30, TimeSpan.Zero);

    private static DateTimeOffset At(int hour, int minute, int second = 0) =>
        new(2026, 1, 31, hour, minute, second, TimeSpan.Zero);

    private static readonly DateTimeOffset From = At(4, 20);

    // First column 1, todo 2, doing 3 (code written here, by position), review 4, terminal 5, deferred 6.
    private static List<EfHatchStatus> Board() =>
    [
        new() { Id = 1, Name = "backlog", SortOrder = 10 },
        new() { Id = 2, Name = "todo", SortOrder = 20 },
        new() { Id = 3, Name = "doing", SortOrder = 30 },
        new() { Id = 4, Name = "review", SortOrder = 40 },
        new() { Id = 5, Name = "done", SortOrder = 50, IsTerminal = true },
        new() { Id = 6, Name = "parked", SortOrder = 60, IsDeferred = true },
    ];

    private static string NameOf(int id) => id == 7 ? "archive" : Board().Single(s => s.Id == id).Name;

    private static TimelineReport.Row Row(
        long id, int status, DateTimeOffset createdAt, long? parent = null, string key = "HA", string type = "task") =>
        new(id, parent, key == "HA" ? 1 : 2, key, (int)id, type, status, id, createdAt, "ann");

    private static BoardHealthReport.Issue Issue(
        long id, int status, DateTimeOffset createdAt, long? parent = null, string key = "HA",
        int own = PreApprovalLevels.None, bool pr = false, string type = "task") =>
        new(Row(id, status, createdAt, parent, key, type), own, pr);

    private static string Payload(int? from, int to, string extra = "") =>
        "{" + (from is { } f ? $"\"from\":\"{NameOf(f)}\",\"fromId\":{f}," : "")
        + $"\"to\":\"{NameOf(to)}\",\"toId\":{to}{extra}" + "}";

    private static BoardHealthReport.Move Move(
        long issue, long eventId, DateTimeOffset at, int? from, int to, string actor = "ann", string extra = "") =>
        new(issue, eventId, at, actor, Payload(from, to, extra));

    private static BoardHealthReport.ClaimEvent Claim(long issue, long eventId, string kind, DateTimeOffset at, string actor) =>
        new(issue, eventId, kind, at, actor);

    private static BoardHealthReport.Result Fold(
        BoardHealthReport.Issue[] rows, BoardHealthReport.Move[]? moves = null,
        BoardHealthReport.ClaimEvent[]? claims = null, BoardHealthReport.PreApprovalChange[]? levels = null,
        BoardHealthReport.PullRequestChange[]? prs = null, FlowReport.Question[]? questions = null,
        List<EfHatchStatus>? board = null, int hours = 8, DateTimeOffset? now = null, string[]? keys = null,
        long[]? leaves = null, Dictionary<int, long>? normals = null, BoardHealthReport.Attention? attention = null) =>
        BoardHealthReport.Fold(
            rows, moves ?? [], claims ?? [], levels ?? [], prs ?? [], questions ?? [], board ?? Board(), hours,
            now ?? Now, keys?.ToHashSet(), leaves?.ToHashSet() ?? [], normals ?? [], attention);

    // ---- the axis ----

    [Theory]
    [InlineData(2, 5)]
    [InlineData(4, 10)]
    [InlineData(8, 20)]
    [InlineData(12, 30)]
    [InlineData(24, 60)]
    public void Axis_HasTwentyFourBars_OfWindowOverTwentyFourMinutes(int hours, int minutes)
    {
        var axis = BoardHealthReport.AxisOf(hours, Now);
        var width = TimeSpan.FromMinutes(minutes);

        Assert.Equal(minutes, axis.BarMinutes);
        Assert.Equal(width * 24, axis.To - axis.From);
        Assert.Equal(0, (axis.To - width).UtcTicks % width.Ticks);
        Assert.True(axis.To - width <= Now && Now < axis.To);
    }

    [Fact]
    public void Axis_AnEightHourWindow_EndsOnTheTwentyMinuteBoundaryAfterNow()
    {
        var axis = BoardHealthReport.AxisOf(8, Now);

        Assert.Equal(At(4, 20), axis.From);
        Assert.Equal(At(12, 20), axis.To);
    }

    [Fact]
    public void Axis_DoesNotSlide_BetweenTwoNowsInsideOneBar()
    {
        var early = BoardHealthReport.AxisOf(8, At(12, 0, 1));
        var late = BoardHealthReport.AxisOf(8, At(12, 19, 59));
        var next = BoardHealthReport.AxisOf(8, At(12, 20));

        Assert.Equal(early, late);
        Assert.Equal(At(12, 40), next.To);
    }

    [Fact]
    public void Axis_AnUnknownWindow_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BoardHealthReport.AxisOf(6, Now));

    [Fact]
    public void Fold_ReturnsTheAxis_AndTwentyFourBarsOldestFirst()
    {
        var result = Fold([Issue(1, 3, At(1, 0))]);

        Assert.Equal(From, result.From);
        Assert.Equal(At(12, 20), result.To);
        Assert.Equal(20, result.BarMinutes);
        Assert.Equal(24, result.Bars.Count);
        Assert.Equal(From, result.Bars[0].Start);
        Assert.Equal(At(12, 0), result.Bars[23].Start);
        Assert.Equal(At(12, 20), result.Bars[23].End);
    }

    [Fact]
    public void NoIssues_IsNoBars_AndZeroTotals()
    {
        var result = Fold([]);

        Assert.Empty(result.Bars);
        Assert.Equal(0, result.Total);
        Assert.Equal(0, result.PreviousTotal);
        Assert.Equal(0, result.PastNormal);
    }

    [Fact]
    public void NoIssueCreatedByTheEnd_IsNoBars()
    {
        var result = Fold([Issue(1, 3, At(13, 0))]);

        Assert.Empty(result.Bars);
        Assert.Equal(0, result.Total);
    }

    // ---- moved ----

    [Fact]
    public void AForwardMove_IsCountedInTheBarHoldingIt()
    {
        var result = Fold([Issue(1, 3, At(1, 0))], [Move(1, 1, At(6, 10), 2, 3)]);

        Assert.Equal(1, result.Bars[5].Moved);
        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Bars.Sum(b => b.Moved));
    }

    [Fact]
    public void ABarIsHalfOpen_AMoveAtItsStartIsInIt_AtItsEndIsInTheNext()
    {
        var result = Fold(
            [Issue(1, 3, At(1, 0))],
            [Move(1, 1, At(12, 0), 2, 3), Move(1, 2, At(11, 59, 59), 1, 2)]);

        Assert.Equal(1, result.Bars[23].Moved);
        Assert.Equal(1, result.Bars[22].Moved);
    }

    [Fact]
    public void ABackwardMove_IsNotCounted()
    {
        var result = Fold([Issue(1, 2, At(1, 0))], [Move(1, 1, At(6, 10), 3, 2)]);

        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void AMoveToOrFromADeferredColumn_IsNotCounted()
    {
        var result = Fold(
            [Issue(1, 3, At(1, 0))],
            [Move(1, 1, At(6, 10), 2, 6), Move(1, 2, At(6, 30), 6, 3)]);

        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void Filing_IsNotAMove()
    {
        var result = Fold([Issue(1, 1, At(6, 0))], [Move(1, 1, At(6, 0), null, 1)]);

        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void AMoveOfAnIssueOutsideTheScope_IsNotCounted()
    {
        var result = Fold(
            [Issue(1, 3, At(1, 0)), Issue(2, 3, At(1, 0), key: "ZZ")],
            [Move(1, 1, At(6, 10), 2, 3), Move(2, 2, At(6, 10), 2, 3)],
            keys: ["HA"]);

        Assert.Equal(1, result.Total);
    }

    [Fact]
    public void TheSplit_IsBoardRunnerPerson_AndSumsToTheCount()
    {
        var claims = new[]
        {
            // Issue 3: a runner holds a claim from 05:00 to 06:00.
            Claim(3, 1, EfHatchIssueEvent.ClaimTaken, At(5, 0), "runner-a"),
            Claim(3, 2, EfHatchIssueEvent.ClaimReleased, At(6, 0), "runner-a"),
            // Issue 5: taken at the very instant of its move.
            Claim(5, 3, EfHatchIssueEvent.ClaimTaken, At(7, 0), "runner-b"),
            // Issue 6: released at the very instant of its move.
            Claim(6, 4, EfHatchIssueEvent.ClaimTaken, At(5, 0), "runner-c"),
            Claim(6, 5, EfHatchIssueEvent.ClaimCleared, At(7, 0), "runner-c"),
        };
        var moves = new[]
        {
            Move(1, 10, At(5, 30), 2, 3, "ann", ",\"express\":true"),                       // hop: board
            Move(2, 11, At(5, 30), 3, 5, "ann", ",\"merged\":true"),                        // merged: board
            Move(4, 12, At(5, 30), 3, 4, "ann", ",\"landed\":true"),                        // landed: board
            Move(7, 13, At(5, 30), 3, 4, "ann", ",\"pushback\":true"),                      // pushback flag: board
            Move(3, 14, At(5, 30), 2, 3, "runner-a"),                                        // open claim: runner
            Move(3, 15, At(6, 30), 3, 4, "runner-a"),                                        // claim closed: person
            Move(5, 16, At(7, 0), 2, 3, "runner-b"),                                         // take at the same instant: runner
            Move(6, 17, At(7, 0), 3, 4, "runner-c"),                                         // close at the same instant: runner
            Move(8, 18, At(8, 0), 2, 3, "bob"),                                              // person
            Move(3, 19, At(9, 0), 2, 3, "someone-else"),                                     // claim by another actor: person
        };
        var rows = Enumerable.Range(1, 8).Select(i => Issue(i, 3, At(1, 0))).ToArray();

        var result = Fold(rows, moves, claims);

        Assert.Equal(10, result.Total);
        Assert.Equal(4, result.Bars.Sum(b => b.Board));
        Assert.Equal(3, result.Bars.Sum(b => b.Runner));
        Assert.Equal(3, result.Bars.Sum(b => b.Person));
        Assert.Equal(result.Total, result.Bars.Sum(b => b.Board + b.Runner + b.Person));
    }

    [Fact]
    public void ABoardHop_CountsForTheBoard_EvenWhenARunnerHoldsTheClaim()
    {
        var result = Fold(
            [Issue(1, 3, At(1, 0))],
            [Move(1, 2, At(6, 10), 2, 3, "runner-a", ",\"pulled\":true")],
            [Claim(1, 1, EfHatchIssueEvent.ClaimTaken, At(5, 0), "runner-a")]);

        Assert.Equal(1, result.Bars.Sum(b => b.Board));
        Assert.Equal(0, result.Bars.Sum(b => b.Runner));
    }

    [Fact]
    public void Totals_SumTheWindow_AndThe24WidthsBefore()
    {
        // The window before is [From - 8h, From) = [20:20 the day before, 04:20).
        var before = At(4, 20).AddHours(-8);
        var moves = new[]
        {
            Move(1, 1, At(5, 0), 2, 3),
            Move(1, 2, At(6, 0), 3, 4),
            Move(1, 3, before, 1, 2),                       // first instant of the earlier window
            Move(1, 4, At(4, 19, 59), 1, 2),               // last instant of the earlier window
            Move(1, 5, before.AddSeconds(-1), 1, 2),       // before both
        };

        var result = Fold([Issue(1, 4, At(0, 0).AddDays(-2))], moves);

        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.PreviousTotal);
    }

    // ---- bands ----

    [Fact]
    public void Bands_WaitingIsBetweenFirstAndWork_InFlightFromWork_DoneIsTerminal()
    {
        var rows = new[]
        {
            Issue(1, 1, At(1, 0)),                // first column: nowhere
            Issue(2, 2, At(1, 0)),                // waiting
            Issue(3, 3, At(1, 0)),                // in flight
            Issue(4, 4, At(1, 0)),                // in flight (review)
            Issue(5, 5, At(6, 0)),                // done, opened inside the window
            Issue(6, 6, At(1, 0)),                // deferred: nowhere
            Issue(7, 5, At(1, 0)),                // done before the window: not counted
        };

        var last = Fold(rows).Bars[23];

        Assert.Equal(1, last.Waiting);
        Assert.Equal(2, last.InFlight);
        Assert.Equal(1, last.Done);
    }

    [Fact]
    public void ANonTerminalColumnAfterTheFirstTerminal_CountsNowhere()
    {
        var board = Board();
        board.Add(new EfHatchStatus { Id = 7, Name = "archive", SortOrder = 70 });

        var last = Fold([Issue(1, 7, At(6, 0)), Issue(2, 3, At(6, 0))], board: board).Bars[23];

        Assert.Equal(1, last.InFlight);
        Assert.Equal(0, last.Waiting);
        Assert.Equal(0, last.Done);
    }

    [Fact]
    public void ABar_ReadsAtItsEnd_AndTheLastBarAtNow()
    {
        // In doing from 06:30 to 12:05; now is 12:07:30.
        var moves = new[] { Move(1, 1, At(6, 30), 2, 3), Move(1, 2, At(12, 5), 3, 4) };

        var result = Fold([Issue(1, 4, At(1, 0))], moves);

        Assert.Equal(0, result.Bars[5].InFlight);   // reads 06:20
        Assert.Equal(1, result.Bars[6].InFlight);   // reads 06:40
        Assert.Equal(1, result.Bars[22].InFlight);  // reads 12:00
        Assert.Equal(1, result.Bars[23].InFlight);  // reads 12:07:30, review is in flight too
        Assert.Equal(0, result.Bars[23].Waiting);
    }

    [Fact]
    public void ADeferredDetour_DoesNotCount_AndTheIssueReturnsToItsBand()
    {
        var moves = new[]
        {
            Move(1, 1, At(5, 30), 2, 3),
            Move(1, 2, At(7, 30), 3, 6),
            Move(1, 3, At(9, 30), 6, 3),
        };

        var result = Fold([Issue(1, 3, At(1, 0))], moves);

        Assert.Equal(1, result.Bars[3].InFlight);   // reads 05:40
        Assert.Equal(0, result.Bars[9].InFlight);   // reads 07:40, parked
        Assert.Equal(1, result.Bars[15].InFlight);  // reads 09:40
    }

    [Fact]
    public void NoWorkColumn_ZeroesTheBands_AndSaysSo()
    {
        var board = new List<EfHatchStatus>
        {
            new() { Id = 1, Name = "backlog", SortOrder = 10 },
            new() { Id = 5, Name = "done", SortOrder = 50, IsTerminal = true },
        };

        var result = Fold([Issue(1, 1, At(1, 0)), Issue(2, 5, At(6, 0))], board: board, leaves: [1, 2]);

        Assert.False(result.HasWorkColumn);
        Assert.All(result.Bars, b =>
        {
            Assert.Equal(0, b.Waiting);
            Assert.Equal(0, b.InFlight);
            Assert.Equal(0, b.Done);
            Assert.Null(b.OldestInFlightSeconds);
        });
    }

    [Fact]
    public void AWorkColumn_IsReported()
    {
        Assert.True(Fold([Issue(1, 3, At(1, 0))]).HasWorkColumn);
    }

    // ---- oldest in flight, past normal ----

    [Fact]
    public void OldestInFlight_IsTheLongestStretchOfALeaf_InSeconds()
    {
        var rows = new[] { Issue(1, 3, At(10, 0)), Issue(2, 3, At(8, 0)), Issue(3, 4, At(11, 0)) };

        var last = Fold(rows, leaves: [1, 3]).Bars[23];

        Assert.Equal(2 * 3600 + 7 * 60 + 30, last.OldestInFlightSeconds);   // issue 1; issue 2 is not a leaf
        Assert.Equal(3, last.InFlight);
    }

    [Fact]
    public void OldestInFlight_IsNullWhenNoLeafIsInFlight_AndReadAtEachBarsEnd()
    {
        var rows = new[] { Issue(1, 3, At(8, 0)) };

        var none = Fold(rows, leaves: []);
        var some = Fold(rows, leaves: [1]);

        Assert.All(none.Bars, b => Assert.Null(b.OldestInFlightSeconds));
        Assert.Null(some.Bars[9].OldestInFlightSeconds);                        // reads 07:40, not yet created
        Assert.Equal(0, some.Bars[10].OldestInFlightSeconds);                   // reads 08:00, created at that instant
        Assert.Equal(3600, some.Bars[13].OldestInFlightSeconds);                // reads 09:00
    }

    [Fact]
    public void PastNormal_IsStrict_ExactlyTheNormalIsNotPast()
    {
        var normals = new Dictionary<int, long> { [3] = 3600 };
        var exactly = Issue(1, 3, Now.AddSeconds(-3600));
        var over = Issue(2, 3, Now.AddSeconds(-3601));

        Assert.Equal(0, Fold([exactly], leaves: [1], normals: normals).PastNormal);
        Assert.Equal(1, Fold([over], leaves: [2], normals: normals).PastNormal);
    }

    [Fact]
    public void PastNormal_CountsOnlyLeavesInAColumnThatHasANormal()
    {
        var normals = new Dictionary<int, long> { [3] = 60 };
        var rows = new[] { Issue(1, 3, At(8, 0)), Issue(2, 3, At(8, 0)), Issue(3, 4, At(8, 0)) };

        Assert.Equal(1, Fold(rows, leaves: [1, 3], normals: normals).PastNormal);
    }

    // ---- the four age groups, and the list of issues in flight now ----

    [Theory]
    [InlineData(499, "healthy")]
    [InlineData(500, "aging")]
    [InlineData(1000, "aging")]
    [InlineData(1001, "pastNormal")]
    public void Ages_AreCutAtHalfTheNormalAndAtTheNormal(int seconds, string band)
    {
        var normals = new Dictionary<int, long> { [3] = 1000 };

        var result = Fold([Issue(1, 3, Now.AddSeconds(-seconds))], leaves: [1], normals: normals);

        var row = Assert.Single(result.InFlightNow);
        Assert.Equal(band, row.Band);
        Assert.Equal(1000, row.NormalSeconds);
        var ages = result.Bars[23].Ages;
        Assert.Equal(new BoardHealthReport.Ages(
            band == "healthy" ? 1 : 0, band == "aging" ? 1 : 0, band == "pastNormal" ? 1 : 0, 0), ages);
    }

    [Fact]
    public void Ages_AColumnWithNoNormal_IsNoNormal()
    {
        var result = Fold([Issue(1, 3, At(8, 0))], leaves: [1], normals: new() { [4] = 60 });

        Assert.Equal(new BoardHealthReport.Ages(0, 0, 0, 1), result.Bars[23].Ages);
        var row = Assert.Single(result.InFlightNow);
        Assert.Equal("noNormal", row.Band);
        Assert.Null(row.NormalSeconds);
    }

    [Fact]
    public void Ages_AddUpToTheLeavesInFlight_NotToInFlight_WhichAlsoCountsParents()
    {
        var rows = new[]
        {
            Issue(1, 3, At(8, 0), type: "epic"), Issue(2, 3, At(8, 0), parent: 1), Issue(3, 4, At(11, 0), parent: 1),
            Issue(4, 1, At(8, 0)),
        };
        var normals = new Dictionary<int, long> { [3] = 7200 };

        var last = Fold(rows, leaves: [2, 3, 4], normals: normals).Bars[23];

        Assert.Equal(3, last.InFlight);
        Assert.Equal(2, last.Ages.Healthy + last.Ages.Aging + last.Ages.PastNormal + last.Ages.NoNormal);
        Assert.Equal(1, last.Ages.PastNormal);   // issue 2: 4h07m in a column whose normal is 2h
        Assert.Equal(1, last.Ages.NoNormal);     // issue 3 stands in a column with none
    }

    [Fact]
    public void Ages_AnEarlierBar_IsReadAtItsEnd_AgainstTodaysNormals()
    {
        var normals = new Dictionary<int, long> { [3] = 3600 };
        var result = Fold([Issue(1, 3, At(10, 0))], leaves: [1], normals: normals);

        Assert.Equal(new BoardHealthReport.Ages(1, 0, 0, 0), result.Bars[17].Ages);   // reads 10:20: 20 minutes
        Assert.Equal(new BoardHealthReport.Ages(0, 1, 0, 0), result.Bars[18].Ages);   // reads 10:40: 40 minutes
        Assert.Equal(new BoardHealthReport.Ages(0, 1, 0, 0), result.Bars[19].Ages);   // reads 11:00: exactly the normal
        Assert.Equal(new BoardHealthReport.Ages(0, 0, 1, 0), result.Bars[20].Ages);   // reads 11:20
        Assert.Equal(new BoardHealthReport.Ages(0, 0, 1, 0), result.Bars[23].Ages);   // now
        Assert.Equal(new BoardHealthReport.Ages(0, 0, 0, 0), result.Bars[9].Ages);    // not yet created
    }

    [Fact]
    public void Ages_TheLastBarsPastNormal_IsTheAnswersPastNormal()
    {
        var normals = new Dictionary<int, long> { [3] = 60, [4] = 60 };
        var rows = new[] { Issue(1, 3, At(8, 0)), Issue(2, 4, At(9, 0)), Issue(3, 3, At(12, 7)), Issue(4, 3, At(8, 0)) };

        var result = Fold(rows, leaves: [1, 2, 3], normals: normals);

        Assert.Equal(2, result.PastNormal);
        Assert.Equal(result.PastNormal, result.Bars[23].Ages.PastNormal);
        Assert.Equal(result.PastNormal, result.InFlightNow.Count(r => r.Band == "pastNormal"));
    }

    [Fact]
    public void InFlightNow_ListsEveryLeafInFlight_LongestStayFirst_TiesByEntryThenId()
    {
        var rows = new[]
        {
            Issue(1, 3, At(11, 0)), Issue(2, 4, At(8, 0)), Issue(3, 3, At(11, 0)),
            Issue(4, 3, At(9, 0)), Issue(5, 1, At(6, 0)), Issue(6, 5, At(6, 0)), Issue(7, 3, At(7, 0)),
        };

        var result = Fold(rows, leaves: [1, 2, 3, 4, 5, 6]);

        Assert.Equal([2L, 4, 1, 3], result.InFlightNow.Select(r => r.Row.Id));   // 5 waits, 6 is done, 7 is not a leaf
        Assert.Equal(result.Bars[23].OldestInFlightSeconds, result.InFlightNow[0].Seconds);
        Assert.Equal(result.Bars[23].InFlight - 1, result.InFlightNow.Count);    // the parent counts in InFlight only
        Assert.Equal(new[] { 4, 3, 3, 3 }, result.InFlightNow.Select(r => r.StatusId));
        Assert.Equal(At(8, 0), result.InFlightNow[0].Opens);
    }

    [Fact]
    public void InFlightNow_IsEmpty_ForAnEmptyBoard_AndForNothingInFlight()
    {
        Assert.Empty(Fold([]).InFlightNow);
        Assert.Empty(Fold([Issue(1, 1, At(8, 0)), Issue(2, 5, At(8, 0))], leaves: [1, 2]).InFlightNow);
        Assert.Empty(Fold([Issue(1, 3, At(8, 0))], leaves: []).InFlightNow);
    }

    [Fact]
    public void InFlightNow_KeepsToTheProjectScope()
    {
        var rows = new[] { Issue(1, 3, At(8, 0)), Issue(2, 3, At(8, 0), key: "OTH") };

        var result = Fold(rows, keys: ["HA"], leaves: [1]);

        Assert.Equal(1L, Assert.Single(result.InFlightNow).Row.Id);
    }

    // ---- waiting on you: questions ----

    [Fact]
    public void AQuestion_WaitsFromTheAsk_UntilItIsAnswered()
    {
        var q = new FlowReport.Question(1, 1, At(6, 0), At(7, 0));

        var bars = Fold([Issue(1, 3, At(1, 0))], questions: [q]).Bars;

        Assert.Equal(0, bars[3].WaitingQuestions);   // reads 05:40
        Assert.Equal(1, bars[4].WaitingQuestions);   // reads 06:00, asked at that instant
        Assert.Equal(1, bars[6].WaitingQuestions);   // reads 06:40
        Assert.Equal(0, bars[7].WaitingQuestions);   // reads 07:00, answered then
        Assert.Equal(3, bars.Sum(b => b.WaitingQuestions));
    }

    [Fact]
    public void AQuestion_AlsoStopsWhenItLapses()
    {
        var q = new FlowReport.Question(1, 1, At(6, 0), null, At(6, 30));

        var bars = Fold([Issue(1, 3, At(1, 0))], questions: [q]).Bars;

        Assert.Equal(2, bars.Sum(b => b.WaitingQuestions));
    }

    [Fact]
    public void AQuestion_StopsWhenTheIssueIsClosedUnderIt()
    {
        var q = new FlowReport.Question(1, 1, At(6, 0), null);
        var moves = new[] { Move(1, 1, At(8, 0), 3, 5) };

        var bars = Fold([Issue(1, 5, At(1, 0))], moves, questions: [q]).Bars;

        Assert.Equal(1, bars[9].WaitingQuestions);    // reads 07:40
        Assert.Equal(0, bars[10].WaitingQuestions);   // reads 08:00
    }

    [Fact]
    public void AQuestion_AskedOfAnIssueAlreadyTerminal_NeverWaits()
    {
        var q = new FlowReport.Question(1, 1, At(6, 0), null);
        var moves = new[] { Move(1, 1, At(5, 0), 3, 5) };

        var bars = Fold([Issue(1, 5, At(1, 0))], moves, questions: [q]).Bars;

        Assert.Equal(0, bars.Sum(b => b.WaitingQuestions));
    }

    [Fact]
    public void AQuestion_OfAnIssueOutsideTheScope_IsNotCounted()
    {
        var q = new FlowReport.Question(2, 1, At(6, 0), null);

        var bars = Fold([Issue(1, 3, At(1, 0)), Issue(2, 3, At(1, 0), key: "ZZ")], questions: [q], keys: ["HA"]).Bars;

        Assert.Equal(0, bars.Sum(b => b.WaitingQuestions));
    }

    [Fact]
    public void OldestWait_IsTheLongestOpenQuestion()
    {
        var qs = new[]
        {
            new FlowReport.Question(1, 1, At(6, 0), null),
            new FlowReport.Question(1, 2, At(8, 0), null),
        };

        var bars = Fold([Issue(1, 3, At(1, 0))], questions: qs).Bars;

        Assert.Equal(40 * 60, bars[6].OldestWaitSeconds);                          // reads 06:40
        Assert.Equal(6 * 3600 + 7 * 60 + 30, bars[23].OldestWaitSeconds);         // reads 12:07:30
        Assert.Null(bars[0].OldestWaitSeconds);
    }

    // ---- waiting on you: pull requests ----

    [Fact]
    public void APullRequest_WaitsWhileTheIssueStandsInReviewWithOneRecorded()
    {
        var moves = new[] { Move(1, 1, At(6, 0), 3, 4), Move(1, 2, At(9, 0), 4, 5) };

        var bars = Fold([Issue(1, 5, At(1, 0), pr: true)], moves).Bars;

        Assert.Equal(0, bars[3].WaitingPullRequests);   // reads 05:40, still doing
        Assert.Equal(1, bars[4].WaitingPullRequests);   // reads 06:00
        Assert.Equal(1, bars[12].WaitingPullRequests);  // reads 08:40
        Assert.Equal(0, bars[13].WaitingPullRequests);  // reads 09:00, done
    }

    [Fact]
    public void APullRequest_ReadsRecordedFromTheTrail_ThenTheFirstWas_ThenToday()
    {
        var moves = new[] { Move(1, 1, At(6, 0), 3, 4) };

        // Recorded at 08:00: waits from the record, not from entering review.
        var recordedLater = Fold(
            [Issue(1, 4, At(1, 0), pr: true)], moves,
            prs: [new BoardHealthReport.PullRequestChange(1, 5, At(8, 0), false, true)]).Bars;
        Assert.Equal(0, recordedLater[9].WaitingPullRequests);    // reads 07:40
        Assert.Equal(1, recordedLater[10].WaitingPullRequests);   // reads 08:00
        Assert.Equal(20 * 60, recordedLater[11].OldestWaitSeconds); // reads 08:20, since the record

        // Cleared at 08:00: before the first event, Was says one stood.
        var cleared = Fold(
            [Issue(1, 4, At(1, 0), pr: false)], moves,
            prs: [new BoardHealthReport.PullRequestChange(1, 5, At(8, 0), true, false)]).Bars;
        Assert.Equal(1, cleared[9].WaitingPullRequests);
        Assert.Equal(0, cleared[10].WaitingPullRequests);
        Assert.Equal(100 * 60, cleared[9].OldestWaitSeconds);     // reads 07:40, in review since 06:00; the record known only from Was counts from creation

        // No trail at all: today's value.
        Assert.Equal(1, Fold([Issue(1, 4, At(1, 0), pr: true)], moves).Bars[22].WaitingPullRequests);
        Assert.Equal(0, Fold([Issue(1, 4, At(1, 0), pr: false)], moves).Bars[22].WaitingPullRequests);
    }

    [Fact]
    public void APullRequest_StopsWaitingOnceItsOwnPreApprovalIsRaised()
    {
        var moves = new[] { Move(1, 1, At(6, 0), 3, 4) };
        var levels = new[] { new BoardHealthReport.PreApprovalChange(1, 5, At(10, 0), PreApprovalLevels.NoneName, PreApprovalLevels.PreApprovedName) };

        var bars = Fold([Issue(1, 4, At(1, 0), pr: true, own: PreApprovalLevels.PreApproved)], moves, levels: levels).Bars;

        Assert.Equal(1, bars[14].WaitingPullRequests);  // reads 09:20
        Assert.Equal(0, bars[16].WaitingPullRequests);  // reads 10:00, the change is at that instant
    }

    [Fact]
    public void APullRequest_WaitsAgainOnceItsPreApprovalIsLowered()
    {
        var moves = new[] { Move(1, 1, At(6, 0), 3, 4) };
        var levels = new[] { new BoardHealthReport.PreApprovalChange(1, 5, At(10, 0), PreApprovalLevels.PreApprovedName, PreApprovalLevels.NoneName) };

        var bars = Fold([Issue(1, 4, At(1, 0), pr: true, own: PreApprovalLevels.None)], moves, levels: levels).Bars;

        Assert.Equal(0, bars[14].WaitingPullRequests);  // reads 09:20, pre-approved by the first event's From
        Assert.Equal(1, bars[16].WaitingPullRequests);  // reads 10:00
    }

    [Fact]
    public void ExpressIsNotPreApproved_AndStopsTheWalkToAParent()
    {
        var parent = Issue(10, 3, At(1, 0), type: "epic", own: PreApprovalLevels.PreApproved);
        var child = Issue(1, 4, At(1, 0), parent: 10, pr: true, own: PreApprovalLevels.Express);

        Assert.Equal(1, Fold([parent, child]).Bars[23].WaitingPullRequests);
    }

    [Fact]
    public void APullRequest_InheritsPreApprovalFromAParent_EvenOutsideTheScope()
    {
        var parent = Issue(10, 3, At(1, 0), key: "ZZ", type: "epic", own: PreApprovalLevels.PreApproved);
        var child = Issue(1, 4, At(1, 0), parent: 10, pr: true);

        Assert.Equal(0, Fold([parent, child], keys: ["HA"]).Bars[23].WaitingPullRequests);
        Assert.Equal(1, Fold([Issue(10, 3, At(1, 0), key: "ZZ", type: "epic"), child], keys: ["HA"]).Bars[23].WaitingPullRequests);
    }

    [Fact]
    public void AParentsLevel_IsReadAtTheInstant_NotToday()
    {
        var parent = Issue(10, 3, At(1, 0), type: "epic", own: PreApprovalLevels.PreApproved);
        var child = Issue(1, 4, At(1, 0), parent: 10, pr: true);
        var levels = new[] { new BoardHealthReport.PreApprovalChange(10, 5, At(10, 0), PreApprovalLevels.NoneName, PreApprovalLevels.PreApprovedName) };

        var bars = Fold([parent, child], levels: levels).Bars;

        Assert.Equal(1, bars[14].WaitingPullRequests);  // reads 09:20
        Assert.Equal(0, bars[16].WaitingPullRequests);  // reads 10:00
    }

    [Fact]
    public void AParentCycle_DoesNotLoop()
    {
        var a = Issue(1, 4, At(1, 0), parent: 2, pr: true);
        var b = Issue(2, 3, At(1, 0), parent: 1, type: "epic");

        Assert.Equal(1, Fold([a, b]).Bars[23].WaitingPullRequests);
    }

    [Fact]
    public void Attention_ReplacesTheLastBarOnly()
    {
        var q = new FlowReport.Question(1, 1, At(6, 0), null);
        var rows = new[] { Issue(1, 3, At(1, 0)) };

        var replayed = Fold(rows, questions: [q]).Bars;
        var given = Fold(rows, questions: [q], attention: new BoardHealthReport.Attention(3, 2, 999)).Bars;

        Assert.Equal(1, replayed[23].WaitingQuestions);
        Assert.Equal(2, given[23].WaitingQuestions);
        Assert.Equal(3, given[23].WaitingPullRequests);
        Assert.Equal(999, given[23].OldestWaitSeconds);
        for (var k = 0; k < 23; k++)
        {
            Assert.Equal(replayed[k].WaitingQuestions, given[k].WaitingQuestions);
            Assert.Equal(replayed[k].OldestWaitSeconds, given[k].OldestWaitSeconds);
        }
    }

    // ---- equality with the cumulative flow ----

    [Fact]
    public void Bands_EqualTheCumulativeFlow_AtEveryBarEnd()
    {
        var rows = new[]
        {
            Issue(1, 5, At(2, 0)),                      // before From; a move before From; a deferred detour; closes mid-window
            Issue(2, 2, At(6, 0)),                      // waits in todo
            Issue(3, 1, At(8, 0)),                      // first column
            Issue(4, 5, At(3, 0)),                      // closed before the window with no moves
            Issue(5, 3, At(7, 0)),                      // jumps forward, then back
            Issue(6, 5, At(9, 0)),                      // created already done, inside the window
        };
        var moves = new[]
        {
            Move(1, 1, At(3, 0), 1, 2),
            Move(1, 2, At(5, 10), 2, 3),
            Move(1, 3, At(9, 0), 3, 6),
            Move(1, 4, At(9, 30), 6, 3),
            Move(1, 5, At(10, 15), 3, 4),
            Move(1, 6, At(11, 5), 4, 5),
            Move(5, 7, At(10, 0), 1, 4),
            Move(5, 8, At(11, 40), 4, 3),
        };
        var changes = moves
            .Select(m => TimelineReport.ParseChange(m.IssueId, m.EventId, m.At, m.Payload)!)
            .ToList();
        var flat = rows.Select(r => r.Row).ToList();

        var result = Fold(rows, moves);

        Assert.Equal(24, result.Bars.Count);
        var seenSomething = false;
        for (var k = 0; k < result.Bars.Count; k++)
        {
            var bar = result.Bars[k];
            var end = k == result.Bars.Count - 1 ? Now : bar.End;
            var flow = CumulativeFlowReport.Fold(flat, changes, Board(), result.From, end, Now);

            int Sum(params int[] ids) => flow.Points.Count == 0
                ? 0
                : ids.Sum(id => flow.Points[^1].Counts[flow.Columns.ToList().IndexOf(id)]);

            Assert.Equal(Sum(2), bar.Waiting);
            Assert.Equal(Sum(3, 4), bar.InFlight);
            Assert.Equal(Sum(5), bar.Done);
            seenSomething |= bar.InFlight > 0 && bar.Done > 0;
        }

        Assert.True(seenSomething);
        Assert.Contains(result.Bars, b => b.Done > 0);
        Assert.Equal(0, result.Bars[0].Done);
    }

    // ---- when anything last waited ----

    private static DateTimeOffset? Last(
        BoardHealthReport.Issue[] rows, BoardHealthReport.Move[]? moves = null,
        BoardHealthReport.PullRequestChange[]? prs = null, BoardHealthReport.PreApprovalChange[]? levels = null,
        FlowReport.Question[]? questions = null, DateTimeOffset? from = null, DateTimeOffset? to = null, string[]? keys = null) =>
        BoardHealthReport.LastWaited(
            rows, moves ?? [], prs ?? [], levels ?? [], questions ?? [], Board(), from ?? From, to ?? Now, keys?.ToHashSet());

    [Fact]
    public void LastWaited_AQuestionAnsweredInsideTheWindow_IsTheAnswersOwnInstant_NotABarEdge()
    {
        var q = new FlowReport.Question(1, 1, At(6, 0), At(7, 13, 41));

        Assert.Equal(At(7, 13, 41), Last([Issue(1, 3, At(1, 0))], questions: [q]));
    }

    [Fact]
    public void LastWaited_AQuestionAnsweredBeforeTheWindow_IsFoundOnlyByAWideEnoughRead()
    {
        var q = new FlowReport.Question(1, 1, At(1, 0), At(2, 5));
        var rows = new[] { Issue(1, 3, At(0, 30)) };

        Assert.Equal(At(2, 5), Last(rows, questions: [q], from: At(0, 0)));
        Assert.Null(Last(rows, questions: [q]));
    }

    [Fact]
    public void LastWaited_ALapse_EndsTheWaitAtTheLapse()
    {
        var q = new FlowReport.Question(1, 1, At(6, 0), null, At(6, 30));

        Assert.Equal(At(6, 30), Last([Issue(1, 3, At(1, 0))], questions: [q]));
    }

    [Fact]
    public void LastWaited_AQuestionClosedUnderByATerminalMove_EndsWhenTheIssueIsDone()
    {
        var q = new FlowReport.Question(1, 1, At(6, 0), null);
        var moves = new[] { Move(1, 1, At(8, 3), 3, 5) };

        Assert.Equal(At(8, 3), Last([Issue(1, 5, At(1, 0))], moves, questions: [q]));
    }

    [Fact]
    public void LastWaited_AQuestionStillOpen_IsSkipped_AndAQuestionAskedOfADoneIssueNeverWaited()
    {
        var open = new FlowReport.Question(1, 1, At(6, 0), null);
        Assert.Null(Last([Issue(1, 3, At(1, 0))], questions: [open]));

        var moves = new[] { Move(1, 1, At(5, 0), 3, 5) };
        Assert.Null(Last([Issue(1, 5, At(1, 0))], moves, questions: [open]));
    }

    [Fact]
    public void LastWaited_IsTheLatestEnd_AcrossQuestions_AndIgnoresAnEndAfterTo()
    {
        var qs = new[]
        {
            new FlowReport.Question(1, 1, At(5, 0), At(9, 10)),
            new FlowReport.Question(1, 2, At(5, 0), At(7, 0)),
            new FlowReport.Question(1, 3, At(5, 0), At(11, 55)),
        };
        var rows = new[] { Issue(1, 3, At(1, 0)) };

        Assert.Equal(At(11, 55), Last(rows, questions: qs));
        Assert.Equal(At(9, 10), Last(rows, questions: qs, to: At(10, 0)));
    }

    [Fact]
    public void LastWaited_APullRequestLeavingReview_EndsAtTheMove_InsideOrBeforeTheWindow()
    {
        var back = new[] { Move(1, 1, At(6, 0), 3, 4), Move(1, 2, At(6, 47), 4, 3) };
        Assert.Equal(At(6, 47), Last([Issue(1, 3, At(1, 0), pr: true)], back));

        var before = new[] { Move(1, 1, At(1, 0), 3, 4), Move(1, 2, At(2, 0), 4, 5) };
        var rows = new[] { Issue(1, 5, At(0, 30), pr: true) };
        Assert.Equal(At(2, 0), Last(rows, before, from: At(0, 0)));
        Assert.Null(Last(rows, before));
    }

    [Fact]
    public void LastWaited_APullRequestRecordRemoved_EndsAtTheRemoval()
    {
        var moves = new[] { Move(1, 1, At(6, 0), 3, 4) };
        var prs = new[] { new BoardHealthReport.PullRequestChange(1, 5, At(8, 13), true, false) };

        Assert.Equal(At(8, 13), Last([Issue(1, 4, At(1, 0), pr: false)], moves, prs));
    }

    [Fact]
    public void LastWaited_PreApprovalRaised_EndsTheWait_OnTheIssueOrOnAnAncestor()
    {
        var moves = new[] { Move(2, 1, At(6, 0), 3, 4) };
        var own = new[] { new BoardHealthReport.PreApprovalChange(2, 5, At(10, 11), PreApprovalLevels.NoneName, PreApprovalLevels.PreApprovedName) };
        Assert.Equal(At(10, 11), Last([Issue(2, 4, At(1, 0), pr: true, own: PreApprovalLevels.PreApproved)], moves, levels: own));

        var parentLevel = new[] { new BoardHealthReport.PreApprovalChange(1, 6, At(9, 42), PreApprovalLevels.NoneName, PreApprovalLevels.PreApprovedName) };
        var rows = new[]
        {
            Issue(1, 3, At(1, 0), type: "epic", own: PreApprovalLevels.PreApproved),
            Issue(2, 4, At(1, 0), parent: 1, pr: true),
        };
        Assert.Equal(At(9, 42), Last(rows, moves, levels: parentLevel));
    }

    [Fact]
    public void LastWaited_APullRequestStillWaiting_OrNeverRecorded_IsSkipped()
    {
        var moves = new[] { Move(1, 1, At(6, 0), 3, 4) };

        Assert.Null(Last([Issue(1, 4, At(1, 0), pr: true)], moves));
        Assert.Null(Last([Issue(1, 4, At(1, 0), pr: false)], [Move(1, 1, At(6, 0), 3, 4), Move(1, 2, At(7, 0), 4, 5)]));
    }

    [Fact]
    public void LastWaited_IssuesOutsideTheProjectKeys_AreNotCounted()
    {
        var q = new FlowReport.Question(2, 1, At(6, 0), At(7, 0));
        var rows = new[] { Issue(1, 3, At(1, 0)), Issue(2, 3, At(1, 0), key: "ZZ") };

        Assert.Null(Last(rows, questions: [q], keys: ["HA"]));
        Assert.Equal(At(7, 0), Last(rows, questions: [q], keys: ["ZZ"]));
        Assert.Equal(At(7, 0), Last(rows, questions: [q]));
    }

    [Fact]
    public void LastWaited_NoIssuesInScope_IsNull() => Assert.Null(Last([]));

    // ---- settling the answer ----

    private static readonly DateTimeOffset Month = Now - TimeSpan.FromDays(30);

    [Fact]
    public void Settle_SomethingWaitingNow_IsNull_AndNothingInScopeIsNull()
    {
        Assert.Equal((null, false), BoardHealthReport.Settle(At(6, 0), At(5, 0), 1, Now.AddDays(-9), Now));
        Assert.Equal((null, false), BoardHealthReport.Settle(null, null, 0, null, Now));
    }

    [Fact]
    public void Settle_TheLaterOfTheWindowAndTheKept_Wins()
    {
        var earlier = At(6, 0);
        var later = At(8, 0);
        var created = Now.AddDays(-9);

        Assert.Equal((later, false), BoardHealthReport.Settle(later, earlier, 0, created, Now));
        Assert.Equal((later, false), BoardHealthReport.Settle(earlier, later, 0, created, Now));
        Assert.Equal((earlier, false), BoardHealthReport.Settle(null, earlier, 0, created, Now));
        Assert.Equal((later, false), BoardHealthReport.Settle(later, null, 0, created, Now));
    }

    [Fact]
    public void Settle_NothingInThirtyDays_IsTheFloor_AndAYoungBoardAnswersItsFirstIssue()
    {
        Assert.Equal((Month, true), BoardHealthReport.Settle(null, null, 0, Now.AddDays(-40), Now));
        Assert.Equal((Now.AddDays(-2), true), BoardHealthReport.Settle(null, null, 0, Now.AddDays(-2), Now));
    }

    [Fact]
    public void Settle_AKeptAnswerOlderThanThirtyDays_IsDropped()
    {
        Assert.Equal((Month, true), BoardHealthReport.Settle(null, Month.AddMinutes(-1), 0, Now.AddDays(-60), Now));
        Assert.Equal((Month.AddMinutes(1), false), BoardHealthReport.Settle(null, Month.AddMinutes(1), 0, Now.AddDays(-60), Now));
    }
}

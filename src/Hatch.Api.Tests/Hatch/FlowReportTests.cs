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

    private static FlowReport.Event MoveWithFlag(long issue, long eventId, DateTimeOffset at, int? from, int? to, string flagJson) =>
        new(issue, eventId, EfHatchIssueEvent.StatusChanged, at,
            $"{{\"from\":{NameJson(from)},\"to\":{NameJson(to)},\"fromId\":{IdJson(from)},\"toId\":{IdJson(to)},{flagJson}}}");

    private static FlowReport.Event ClaimReleased(long issue, long eventId, DateTimeOffset at, string? outcome) =>
        new(issue, eventId, EfHatchIssueEvent.ClaimReleased, at,
            outcome is null ? "{}" : $"{{\"outcome\":\"{outcome}\"}}");

    private static FlowReport.Result Fold(
        TimelineReport.Row[] rows, FlowReport.Event[]? events = null, DateTimeOffset? from = null, DateTimeOffset? to = null,
        List<EfHatchStatus>? board = null, int? projectId = null, string? type = null,
        FlowReport.Question[]? questions = null, FlowReport.Session[]? sessions = null) =>
        FlowReport.Fold(
            rows, events ?? [], questions ?? [], sessions ?? [], board ?? Board(), from ?? Day(-5), to ?? Day(5), projectId, type);

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

    [Fact]
    public void Dropped_CountsOnlyDropped_NotWorkedPreemptedOrNoOutcome()
    {
        var issue = Issue(1, "task", 2, Day(-10));
        var events = new[]
        {
            ClaimReleased(1, 1, Day(0), ClaimOutcomes.Dropped),
            ClaimReleased(1, 2, Day(0), ClaimOutcomes.Worked),
            ClaimReleased(1, 3, Day(0), ClaimOutcomes.Preempted),
            ClaimReleased(1, 4, Day(0), null),
        };

        var result = Fold([issue], events);

        Assert.Equal(1, result.Window.Dropped);
    }

    [Fact]
    public void SevenHopKinds_AreEachRecognizedByTheirOwnFlag_IncludingNullValuedUnder_AndPreapprovedSplitByDestination()
    {
        var rows = Enumerable.Range(1, 7).Select(id => Issue(id, "task", 1, Day(-10))).ToArray();
        var events = new[]
        {
            MoveWithFlag(1, 1, Day(0), 1, 2, "\"express\":true"),
            MoveWithFlag(2, 1, Day(0), 1, 2, "\"pulled\":true"),
            MoveWithFlag(3, 1, Day(0), 1, 2, "\"epic\":true"),
            MoveWithFlag(4, 1, Day(0), 1, 2, "\"under\":null"),
            MoveWithFlag(5, 1, Day(0), 3, 2, "\"sendback\":true"),
            MoveWithFlag(6, 1, Day(0), 3, 4, "\"preapproved\":true"), // into review: not terminal
            MoveWithFlag(7, 1, Day(0), 3, 5, "\"preapproved\":true"), // into done: terminal
        };

        var result = Fold(rows, events);

        Assert.Equal(
            new[] { ("express", 1), ("parent", 1), ("epic", 1), ("under", 1), ("sendback", 1), ("approve", 1), ("close", 1) },
            result.Window.Hops.Select(h => (h.Kind, h.Count)).ToArray());
    }

    [Fact]
    public void AnEventWithNoHopFlag_IsNotCounted()
    {
        var issue = Issue(1, "task", 1, Day(-10));
        var events = new[] { Move(1, 1, Day(0), 1, 2) };

        var result = Fold([issue], events);

        Assert.All(result.Window.Hops, h => Assert.Equal(0, h.Count));
    }

    [Fact]
    public void HopsAreZeroFilled_InFixedOrder_WithNoEvents()
    {
        var result = Fold([]);

        Assert.Equal(
            [HopKinds.Express, HopKinds.Parent, HopKinds.Epic, HopKinds.Under, HopKinds.SendBack, HopKinds.Approve, HopKinds.Close],
            result.Window.Hops.Select(h => h.Kind).ToArray());
        Assert.All(result.Window.Hops, h => Assert.Equal(0, h.Count));
    }

    [Fact]
    public void LeftwardMove_CountsSendBack_RightwardMoveDoesNot()
    {
        var left = Issue(1, "task", 2, Day(-10));
        var right = Issue(2, "task", 2, Day(-10));
        var events = new[]
        {
            Move(1, 1, Day(0), 3, 2), // doing -> todo: leftward
            Move(2, 1, Day(0), 2, 3), // todo -> doing: rightward
        };

        var result = Fold([left, right], events);

        Assert.Equal(1, result.Window.SendBacks);
    }

    [Fact]
    public void MoveIntoDeferredColumn_UnresolvedColumn_AndFromLessEvent_CountNoSendBack()
    {
        var toDeferred = Issue(1, "task", 2, Day(-10));
        var unresolved = Issue(2, "task", 3, Day(-10));
        var noFrom = Issue(3, "task", 2, Day(-10));
        var events = new[]
        {
            Move(1, 1, Day(0), 3, 6), // doing -> parked: deferred
            new FlowReport.Event(2, 1, EfHatchIssueEvent.StatusChanged, Day(0), """{"from":"doing","to":"gone","fromId":3,"toId":99}"""),
            Move(3, 1, Day(0), null, 2), // no previous column
        };

        var result = Fold([toDeferred, unresolved, noFrom], events);

        Assert.Equal(0, result.Window.SendBacks);
    }

    [Fact]
    public void TheSendBackHop_IsCountedInBothHopsAndSendBacks()
    {
        var issue = Issue(1, "task", 3, Day(-10));
        var events = new[] { MoveWithFlag(1, 1, Day(0), 3, 2, "\"sendback\":true,\"length\":500,\"limit\":400") };

        var result = Fold([issue], events);

        Assert.Equal(1, result.Window.Hops.Single(h => h.Kind == HopKinds.SendBack).Count);
        Assert.Equal(1, result.Window.SendBacks);
    }

    [Fact]
    public void QuestionAskedBeforeWindow_AndAnsweredInside_CountsAsAnsweredOnly()
    {
        var issue = Issue(1, "task", 2, Day(-10));
        var questions = new[] { new FlowReport.Question(1, 1, Day(-10), Day(0)) };

        var result = Fold([issue], questions: questions);

        Assert.Equal(0, result.Window.QuestionsAsked);
        Assert.Equal(1, result.Window.QuestionsAnswered);
    }

    [Fact]
    public void MedianAndLongestAnswerWait_AreComputedOverAnsweredQuestions_AndNullWithNone()
    {
        var issue = Issue(1, "task", 2, Day(-10));
        var odd = new[]
        {
            new FlowReport.Question(1, 1, Day(-1), Day(-1).AddSeconds(10)),
            new FlowReport.Question(1, 2, Day(-1), Day(-1).AddSeconds(20)),
            new FlowReport.Question(1, 3, Day(-1), Day(-1).AddSeconds(30)),
        };

        var oddResult = Fold([issue], questions: odd);
        Assert.Equal(20, oddResult.Window.MedianAnswerWaitSeconds);
        Assert.Equal(30, oddResult.Window.LongestAnswerWaitSeconds);

        var even = new[]
        {
            new FlowReport.Question(1, 1, Day(-1), Day(-1).AddSeconds(10)),
            new FlowReport.Question(1, 2, Day(-1), Day(-1).AddSeconds(20)),
            new FlowReport.Question(1, 3, Day(-1), Day(-1).AddSeconds(30)),
            new FlowReport.Question(1, 4, Day(-1), Day(-1).AddSeconds(40)),
        };

        var evenResult = Fold([issue], questions: even);
        Assert.Equal(25, evenResult.Window.MedianAnswerWaitSeconds);
        Assert.Equal(40, evenResult.Window.LongestAnswerWaitSeconds);

        var none = Fold([issue]);
        Assert.Null(none.Window.MedianAnswerWaitSeconds);
        Assert.Null(none.Window.LongestAnswerWaitSeconds);
    }

    [Fact]
    public void SessionsAndSessionErrors_AreCounted()
    {
        var issue = Issue(1, "task", 2, Day(-10));
        var sessions = new[]
        {
            new FlowReport.Session(1, Day(0), IsError: false),
            new FlowReport.Session(1, Day(0), IsError: true),
            new FlowReport.Session(1, Day(0), IsError: true),
        };

        var result = Fold([issue], sessions: sessions);

        Assert.Equal(3, result.Window.Sessions);
        Assert.Equal(2, result.Window.SessionErrors);
    }

    [Fact]
    public void HalfOpenWindow_AppliesToDroppedHopsSendBacksQuestionsAndSessions()
    {
        var from = Day(-5);
        var to = Day(5);
        var issue = Issue(1, "task", 3, Day(-10));

        var atFrom = FlowReport.Fold(
            [issue],
            [ClaimReleased(1, 1, from, ClaimOutcomes.Dropped), MoveWithFlag(1, 2, from, 3, 2, "\"express\":true")],
            [new FlowReport.Question(1, 10, from, from)],
            [new FlowReport.Session(1, from, IsError: false)],
            Board(), from, to, null, null);

        Assert.Equal(1, atFrom.Window.Dropped);
        Assert.Equal(1, atFrom.Window.Hops.Single(h => h.Kind == HopKinds.Express).Count);
        Assert.Equal(1, atFrom.Window.SendBacks);
        Assert.Equal(1, atFrom.Window.QuestionsAsked);
        Assert.Equal(1, atFrom.Window.QuestionsAnswered);
        Assert.Equal(1, atFrom.Window.Sessions);

        var atTo = FlowReport.Fold(
            [issue],
            [ClaimReleased(1, 1, to, ClaimOutcomes.Dropped), MoveWithFlag(1, 2, to, 3, 2, "\"express\":true")],
            [new FlowReport.Question(1, 10, to, to)],
            [new FlowReport.Session(1, to, IsError: false)],
            Board(), from, to, null, null);

        Assert.Equal(0, atTo.Window.Dropped);
        Assert.Equal(0, atTo.Window.Hops.Single(h => h.Kind == HopKinds.Express).Count);
        Assert.Equal(0, atTo.Window.SendBacks);
        Assert.Equal(0, atTo.Window.QuestionsAsked);
        Assert.Equal(0, atTo.Window.QuestionsAnswered);
        Assert.Equal(0, atTo.Window.Sessions);
    }

    [Fact]
    public void ProjectIdAndType_ExcludeOtherIssuesEventsQuestionsAndSessions()
    {
        var rows = new[]
        {
            Issue(1, "task", 2, Day(-10), project: 1),
            Issue(2, "bug", 3, Day(-10), project: 2),
        };
        var events = new[]
        {
            ClaimReleased(1, 1, Day(0), ClaimOutcomes.Dropped),
            ClaimReleased(2, 1, Day(0), ClaimOutcomes.Dropped),
        };
        var questions = new[]
        {
            new FlowReport.Question(1, 1, Day(0), Day(0)),
            new FlowReport.Question(2, 2, Day(0), Day(0)),
        };
        var sessions = new[]
        {
            new FlowReport.Session(1, Day(0), IsError: false),
            new FlowReport.Session(2, Day(0), IsError: true),
        };

        var byProject = Fold(rows, events, questions: questions, sessions: sessions, projectId: 1);
        Assert.Equal(1, byProject.Window.Dropped);
        Assert.Equal(1, byProject.Window.Sessions);
        Assert.Equal(0, byProject.Window.SessionErrors);

        var byType = Fold(rows, events, questions: questions, sessions: sessions, type: "task");
        Assert.Equal(1, byType.Window.Dropped);
        Assert.Equal(1, byType.Window.Sessions);
        Assert.Equal(0, byType.Window.SessionErrors);
    }

    [Fact]
    public void UnparseablePayload_CountsNothing_ForDroppedAndHops()
    {
        var issue = Issue(1, "task", 3, Day(-10));
        var events = new[]
        {
            new FlowReport.Event(1, 1, EfHatchIssueEvent.ClaimReleased, Day(0), "not json"),
            new FlowReport.Event(1, 2, EfHatchIssueEvent.StatusChanged, Day(0), "not json"),
        };

        var result = Fold([issue], events);

        Assert.Equal(0, result.Window.Dropped);
        Assert.All(result.Window.Hops, h => Assert.Equal(0, h.Count));
        Assert.Equal(0, result.Window.SendBacks);
    }
}

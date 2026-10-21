using System.Reflection;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <c>GET /api/hatch/reports/flow</c>: the whole trail loaded, folded by
/// <see cref="FlowReport"/> and answered as a <see cref="FlowDto"/>, wired
/// against the in-memory database.
/// </summary>
public class ReportsControllerFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required ReportsController Reports { get; init; }
        public required int ProjectId { get; init; }
        public required Dictionary<string, int> Status { get; init; }
    }

    /// <summary>One project, and a board of five columns: first, two active, terminal, deferred.</summary>
    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var statuses = new[]
        {
            new EfHatchStatus { Name = "backlog", SortOrder = 10 },
            new EfHatchStatus { Name = "todo", SortOrder = 20 },
            new EfHatchStatus { Name = "doing", SortOrder = 30 },
            new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true },
            new EfHatchStatus { Name = "parked", SortOrder = 50, IsDeferred = true },
        };
        db.Add(project);
        db.AddRange(statuses);
        await db.SaveChangesAsync();

        return new Harness
        {
            Db = db,
            Reports = new ReportsController(
                db, new StubActorDirectory(), TestClaims.With(), new FakeTimeProvider(Now)),
            ProjectId = project.Id,
            Status = statuses.ToDictionary(s => s.Name, s => s.Id),
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
            Type = "task", Title = "task", Description = "", StatusId = statusId, Rank = 1024,
            CreatedBy = "ann", CreatedAt = createdAt, UpdatedAt = createdAt,
        };
        h.Db.Issues.Add(issue);
        await h.Db.SaveChangesAsync();
        return issue;
    }

    private static async Task EventAsync(Harness h, long issueId, string kind, string payload, int daysAgo)
    {
        h.Db.IssueEvents.Add(new EfHatchIssueEvent
        {
            IssueId = issueId, Actor = "ann", Kind = kind, Payload = payload, At = Now.AddDays(-daysAgo),
        });
        await h.Db.SaveChangesAsync();
    }

    private static Task MoveAsync(Harness h, long issueId, string from, string to, int daysAgo) =>
        EventAsync(h, issueId, EfHatchIssueEvent.StatusChanged,
            $"{{\"from\":\"{from}\",\"to\":\"{to}\",\"fromId\":{h.Status[from]},\"toId\":{h.Status[to]}}}", daysAgo);

    // ---- Refusals ----

    [Theory]
    [InlineData("yesterday-ish", null)]
    [InlineData(null, "not a date")]
    public async Task AnUnparseableBound_IsRefused_NamingIt(string? from, string? to)
    {
        var h = await NewAsync();

        var result = await h.Reports.GetFlow(from, to, null, null, default);

        Assert.Equal(RangeBounds.NotAnInstant(from ?? to), Refusal(result));
    }

    [Fact]
    public async Task AnInvertedRange_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Reports.GetFlow(
            Now.ToString("o"), Now.AddDays(-1).ToString("o"), null, null, default);

        Assert.Equal("a range ends before it begins", Refusal(result));
    }

    [Fact]
    public async Task AnUnknownType_IsRefused_WithTheSentenceTheIssueListUses()
    {
        var h = await NewAsync();

        var result = await h.Reports.GetFlow(null, null, null, "saga", default);

        Assert.Equal(
            $"an issue is one of {string.Join(", ", EfHatchIssue.Types)} - not \"saga\"", Refusal(result));
    }

    [Fact]
    public async Task AnUnknownProject_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Reports.GetFlow(null, null, h.ProjectId + 100, null, default);

        Assert.Equal($"there is no project {h.ProjectId + 100}", Refusal(result));
    }

    [Fact]
    public async Task ABadBound_IsRefusedBeforeAnUnknownType()
    {
        var h = await NewAsync();

        var result = await h.Reports.GetFlow("yesterday-ish", null, null, "saga", default);

        Assert.Equal(RangeBounds.NotAnInstant("yesterday-ish"), Refusal(result));
    }

    // ---- The answer ----

    [Fact]
    public async Task AnEmptyTracker_AnswersTheBoardsColumns_WithZerosAndNullDurations()
    {
        var h = await NewAsync();

        var flow = Value(await h.Reports.GetFlow(null, null, null, null, default));

        Assert.Equal(["backlog", "todo", "doing", "done"], flow.Columns.Select(c => c.Name));
        Assert.All(flow.Columns, c =>
        {
            Assert.Equal(0, c.Entered);
            Assert.Equal(0, c.Left);
            Assert.Equal(0, c.Dwelled);
            Assert.Equal(0, c.Standing);
            Assert.Null(c.MedianDwellSeconds);
            Assert.Null(c.P90DwellSeconds);
        });
        Assert.Equal(0, flow.Window.Dropped);
        Assert.Equal(0, flow.Window.Sessions);
        Assert.Null(flow.Window.MedianAnswerWaitSeconds);
        Assert.Null(flow.Window.LongestAnswerWaitSeconds);
        Assert.Equal(0, flow.Unresolved);
        Assert.Equal(Now, flow.To);
    }

    [Fact]
    public async Task OneTaskThroughTheBoard_IsCountedFromTheWholeTrail()
    {
        var h = await NewAsync();
        var issue = await AddIssueAsync(h, h.Status["doing"], Now.AddDays(-5));

        await MoveAsync(h, issue.Id, "backlog", "todo", 4);
        await MoveAsync(h, issue.Id, "todo", "doing", 3);
        await EventAsync(h, issue.Id, EfHatchIssueEvent.ClaimReleased, "{\"outcome\":\"dropped\"}", 2);

        var asked = new EfHatchComment
        {
            IssueId = issue.Id, Author = "ann", Body = "which way?", Kind = EfHatchComment.Question,
            CreatedAt = Now.AddDays(-3),
        };
        h.Db.Comments.Add(asked);
        await h.Db.SaveChangesAsync();
        h.Db.Comments.Add(new EfHatchComment
        {
            IssueId = issue.Id, Author = "bo", Body = "that way", Kind = EfHatchComment.Answer,
            AnswersId = asked.Id, CreatedAt = Now.AddDays(-3).AddSeconds(3600),
        });

        h.Db.WorkLog.Add(new EfHatchWorkLogEntry
        {
            IssueId = issue.Id, SessionId = "s1", StartedAt = Now.AddDays(-1).AddHours(-1),
            EndedAt = Now.AddDays(-1), DurationMs = 3_600_000, IsError = true, CreatedAt = Now.AddDays(-1),
        });
        await h.Db.SaveChangesAsync();

        var flow = Value(await h.Reports.GetFlow(null, null, null, null, default));

        Assert.Equal(Now.AddDays(-5), flow.From);
        Assert.Equal(1, flow.Columns.Single(c => c.Name == "todo").Entered);
        Assert.Equal(1, flow.Columns.Single(c => c.Name == "doing").Entered);
        Assert.Equal(1, flow.Columns.Single(c => c.Name == "doing").Standing);
        Assert.Equal(1, flow.Window.Dropped);
        Assert.Equal(1, flow.Window.QuestionsAsked);
        Assert.Equal(1, flow.Window.QuestionsAnswered);
        Assert.Equal(3600, flow.Window.MedianAnswerWaitSeconds);
        Assert.Equal(1, flow.Window.Sessions);
        Assert.Equal(1, flow.Window.SessionErrors);
        Assert.NotEmpty(flow.Window.Hops);
    }

    private static async Task<EfHatchComment> AskAsync(Harness h, long issueId, DateTimeOffset at)
    {
        var asked = new EfHatchComment
        {
            IssueId = issueId, Author = "ann", Body = "which way?", Kind = EfHatchComment.Question, CreatedAt = at,
        };
        h.Db.Comments.Add(asked);
        await h.Db.SaveChangesAsync();
        return asked;
    }

    private static async Task AnswerAsync(Harness h, EfHatchComment asked, string author, DateTimeOffset at, bool lapsed = false)
    {
        h.Db.Comments.Add(new EfHatchComment
        {
            IssueId = asked.IssueId, Author = author, Body = "that way", Kind = EfHatchComment.Answer,
            AnswersId = asked.Id, CreatedAt = at,
        });

        if (lapsed)
            h.Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = asked.IssueId, Actor = author, Kind = EfHatchIssueEvent.Answered, At = at,
                Payload = $"{{\"questionId\":{asked.Id},\"lapsed\":true}}",
            });

        await h.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task ALapsedQuestion_IsCountedApartFromAPersonsAnswer()
    {
        var h = await NewAsync();
        var issue = await AddIssueAsync(h, h.Status["doing"], Now.AddDays(-5));
        var at = Now.AddDays(-3);

        var lapsed = await AskAsync(h, issue.Id, at);
        await AnswerAsync(h, lapsed, "Hatch", at.AddSeconds(300), lapsed: true);
        var answered = await AskAsync(h, issue.Id, at);
        await AnswerAsync(h, answered, "bo", at.AddSeconds(3600));

        var flow = Value(await h.Reports.GetFlow(null, null, null, null, default));

        Assert.Equal(2, flow.Window.QuestionsAsked);
        Assert.Equal(1, flow.Window.QuestionsAnswered);
        Assert.Equal(1, flow.Window.QuestionsLapsed);
        Assert.Equal(3600, flow.Window.MedianAnswerWaitSeconds);
        Assert.Equal(3600, flow.Window.LongestAnswerWaitSeconds);
    }

    [Fact]
    public async Task APersonsAnswerAfterALapse_CountsAsAnswered_AndWaitsToThePerson()
    {
        var h = await NewAsync();
        var issue = await AddIssueAsync(h, h.Status["doing"], Now.AddDays(-5));
        var at = Now.AddDays(-3);

        var asked = await AskAsync(h, issue.Id, at);
        await AnswerAsync(h, asked, "Hatch", at.AddSeconds(300), lapsed: true);
        await AnswerAsync(h, asked, "bo", at.AddSeconds(7200));

        var flow = Value(await h.Reports.GetFlow(null, null, null, null, default));

        Assert.Equal(1, flow.Window.QuestionsAsked);
        Assert.Equal(1, flow.Window.QuestionsAnswered);
        Assert.Equal(0, flow.Window.QuestionsLapsed);
        Assert.Equal(7200, flow.Window.MedianAnswerWaitSeconds);
    }

    [Fact]
    public async Task ATypeFilter_DropsTheIssuesOfOtherTypes()
    {
        var h = await NewAsync();
        var issue = await AddIssueAsync(h, h.Status["todo"], Now.AddDays(-5));
        await MoveAsync(h, issue.Id, "backlog", "todo", 4);

        var flow = Value(await h.Reports.GetFlow(null, null, null, "epic", default));

        Assert.All(flow.Columns, c => Assert.Equal(0, c.Entered));
    }

    // ---- Who may read it ----

    [Fact]
    public void TheRoute_AcceptsTheHatchScope()
    {
        var guard = typeof(ReportsController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        Assert.Equal(ApiKeyScopes.Hatch, guard.AcceptScope);
    }

    // ---- What it leaves out ----

    [Theory]
    [InlineData(typeof(FlowDto), new[] { "From", "To", "Columns", "Window", "Unresolved" })]
    [InlineData(typeof(FlowColumnDto), new[]
    {
        "StatusId", "Name", "Entered", "Left", "Dwelled", "MedianDwellSeconds", "P90DwellSeconds", "Standing",
    })]
    [InlineData(typeof(FlowWindowDto), new[]
    {
        "Dropped", "Hops", "SendBacks", "QuestionsAsked", "QuestionsAnswered", "QuestionsLapsed",
        "MedianAnswerWaitSeconds", "LongestAnswerWaitSeconds", "Sessions", "SessionErrors",
    })]
    public void TheDtos_CarryCountsAndDurations_AndNoIssue(Type dto, string[] expected)
    {
        var names = dto.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name);

        Assert.Equal(expected.Order(), names.Order());
    }
}

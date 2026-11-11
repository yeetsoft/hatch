using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <c>GET /api/hatch/reports/consumption</c>: the work log loaded, folded by
/// <see cref="ConsumptionReport"/> and answered as a
/// <see cref="ConsumptionReportDto"/>, wired against the in-memory database.
/// </summary>
public class ReportsControllerConsumptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required ReportsController Reports { get; init; }
        public required int ProjectId { get; init; }
        public required int StatusId { get; init; }
        public required int OtherStatusId { get; init; }
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 10 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true };
        db.Add(project);
        db.AddRange(doing, done);
        await db.SaveChangesAsync();

        return new Harness
        {
            Db = db,
            Reports = new ReportsController(
                db, new StubActorDirectory(), TestClaims.With(), new FakeTimeProvider(Now)),
            ProjectId = project.Id,
            StatusId = doing.Id,
            OtherStatusId = done.Id,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");

    private static string Refusal<T>(ActionResult<T> result) =>
        Assert.IsType<string>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);

    private static async Task<EfHatchIssue> AddIssueAsync(
        Harness h, string type, int statusId, long? parentId = null, int? projectId = null)
    {
        var project = projectId ?? h.ProjectId;
        var issue = new EfHatchIssue
        {
            ProjectId = project,
            Number = await h.Db.Issues.CountAsync(i => i.ProjectId == project) + 1,
            Type = type, Title = type, Description = "", StatusId = statusId, ParentId = parentId, Rank = 1024,
            CreatedBy = "ann", CreatedAt = Now, UpdatedAt = Now,
        };
        h.Db.Issues.Add(issue);
        await h.Db.SaveChangesAsync();
        return issue;
    }

    private static async Task<int> AddProjectAsync(Harness h, string key)
    {
        var project = new EfHatchProject { Key = key, Name = key, CreatedAt = Now };
        h.Db.Projects.Add(project);
        await h.Db.SaveChangesAsync();
        return project.Id;
    }

    private static async Task SpendAsync(
        Harness h, long issueId, DateTimeOffset endedAt, long tokens = 100, decimal? costUsd = 1m, long durationMs = 1_000)
    {
        h.Db.WorkLog.Add(new EfHatchWorkLogEntry
        {
            IssueId = issueId,
            SessionId = $"s-{Guid.NewGuid():N}",
            StartedAt = endedAt,
            EndedAt = endedAt,
            DurationMs = durationMs,
            CostUsd = costUsd,
            InputTokens = tokens,
            CreatedAt = endedAt,
        });
        await h.Db.SaveChangesAsync();
    }

    // ---- Refusals, in order ----

    [Theory]
    [InlineData("yesterday-ish", null)]
    [InlineData(null, "not a date")]
    public async Task AnUnparseableBound_IsRefused_NamingIt(string? from, string? to)
    {
        var h = await NewAsync();

        var result = await h.Reports.GetConsumption(from, to, null, null, ct: default);

        Assert.Equal(RangeBounds.NotAnInstant(from ?? to), Refusal(result));
    }

    [Fact]
    public async Task AnInvertedRange_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Reports.GetConsumption(
            Now.ToString("o"), Now.AddDays(-1).ToString("o"), null, null, ct: default);

        Assert.Equal("a range ends before it begins", Refusal(result));
    }

    [Fact]
    public async Task AnOffsetOutOfRange_IsRefused_BeforeAnUnknownType()
    {
        var h = await NewAsync();

        var result = await h.Reports.GetConsumption(null, null, "saga", null, offsetMinutes: 1441, ct: default);

        Assert.Equal("an offset from UTC is minutes between -1440 and 1440 - not 1441", Refusal(result));
    }

    [Fact]
    public async Task AnUnknownType_IsRefused_NamingTheChoices()
    {
        var h = await NewAsync();

        var result = await h.Reports.GetConsumption(null, null, "saga", null, ct: default);

        Assert.Equal($"a type is {string.Join(", ", EfHatchIssue.Types)} or all - not \"saga\"", Refusal(result));
    }

    [Fact]
    public async Task AnUnknownSort_IsRefused_BeforeABadLimit_NamingTheChoices()
    {
        var h = await NewAsync();

        var result = await h.Reports.GetConsumption(null, null, null, "cheapest", limit: 0, ct: default);

        Assert.Equal("a sort is activity, tokens, cost or time - not \"cheapest\"", Refusal(result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task ABadLimit_IsRefused(int limit)
    {
        var h = await NewAsync();

        var result = await h.Reports.GetConsumption(null, null, null, null, limit: limit, ct: default);

        Assert.Equal($"a limit is between 1 and {ReportsController.MaxConsumptionRows} - not {limit}", Refusal(result));
    }

    [Fact]
    public async Task AnUnknownStatusId_IsRefused_BeforeAnUnknownProjectId()
    {
        var h = await NewAsync();

        var result = await h.Reports.GetConsumption(
            null, null, null, null, statusId: [999_999], projectId: [999_999], ct: default);

        Assert.Equal("there is no column 999999", Refusal(result));
    }

    [Fact]
    public async Task AnUnknownProjectId_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Reports.GetConsumption(null, null, null, null, projectId: [999_999], ct: default);

        Assert.Equal("there is no project 999999", Refusal(result));
    }

    // ---- type ----

    [Fact]
    public async Task TypeDefaultsToEpic_AndDropsOtherTypes()
    {
        var h = await NewAsync();
        var epic = await AddIssueAsync(h, "epic", h.StatusId);
        var task = await AddIssueAsync(h, "task", h.StatusId);
        await SpendAsync(h, epic.Id, Now.AddDays(-1));
        await SpendAsync(h, task.Id, Now.AddDays(-1));

        var report = Value(await h.Reports.GetConsumption(null, null, null, null, ct: default));

        Assert.Equal("epic", report.Type);
        Assert.Equal([$"AER-{epic.Number}"], report.Rows.Select(r => r.Key));
    }

    [Fact]
    public async Task TypeAll_ListsEveryType()
    {
        var h = await NewAsync();
        var epic = await AddIssueAsync(h, "epic", h.StatusId);
        var task = await AddIssueAsync(h, "task", h.StatusId);
        await SpendAsync(h, epic.Id, Now.AddDays(-1));
        await SpendAsync(h, task.Id, Now.AddDays(-1));

        var report = Value(await h.Reports.GetConsumption(null, null, "all", null, ct: default));

        Assert.Equal("all", report.Type);
        Assert.Equal(2, report.Rows.Count);
    }

    // ---- statusId ----

    [Fact]
    public async Task TwoStatusIds_ReadAsEither()
    {
        var h = await NewAsync();
        var inDoing = await AddIssueAsync(h, "epic", h.StatusId);
        var inDone = await AddIssueAsync(h, "epic", h.OtherStatusId);
        await SpendAsync(h, inDoing.Id, Now.AddDays(-1));
        await SpendAsync(h, inDone.Id, Now.AddDays(-1));

        var report = Value(await h.Reports.GetConsumption(
            null, null, null, null, statusId: [h.StatusId, h.OtherStatusId], ct: default));

        Assert.Equal(2, report.Rows.Count);
    }

    // ---- limit vs. matched/totals ----

    [Fact]
    public async Task ACappedTable_StillAddsUpHonestly()
    {
        var h = await NewAsync();
        var one = await AddIssueAsync(h, "epic", h.StatusId);
        var two = await AddIssueAsync(h, "epic", h.StatusId);
        var three = await AddIssueAsync(h, "epic", h.StatusId);
        await SpendAsync(h, one.Id, Now.AddDays(-1), tokens: 100);
        await SpendAsync(h, two.Id, Now.AddDays(-2), tokens: 200);
        await SpendAsync(h, three.Id, Now.AddDays(-3), tokens: 300);

        var report = Value(await h.Reports.GetConsumption(null, null, null, null, limit: 1, ct: default));

        Assert.Equal(1, report.Rows.Count);
        Assert.Equal(3, report.Matched);
        Assert.Equal(3, report.Totals.Sessions);
        Assert.Equal(600, report.Totals.TotalTokens);
    }

    // ---- nesting ----

    [Fact]
    public async Task ASubEpicsSessions_CreditItsOwnRowAndItsParents_AndTheTotalOnce()
    {
        var h = await NewAsync();
        var parentEpic = await AddIssueAsync(h, "epic", h.StatusId);
        var subEpic = await AddIssueAsync(h, "epic", h.StatusId, parentId: parentEpic.Id);
        await SpendAsync(h, subEpic.Id, Now.AddDays(-1), tokens: 1_000);

        var report = Value(await h.Reports.GetConsumption(null, null, null, null, ct: default));

        Assert.Equal(2, report.Rows.Count);
        Assert.All(report.Rows, r => Assert.Equal(1_000, r.Totals.TotalTokens));
        Assert.Equal(1, report.Totals.Sessions);
        Assert.Equal(1_000, report.Totals.TotalTokens);
        Assert.Equal($"AER-{parentEpic.Number}", report.Rows.Single(r => r.Key == $"AER-{subEpic.Number}").ParentKey);
    }

    // ---- who may read it ----

    [Fact]
    public void TheRoute_AcceptsAKey()
    {
        var guard = typeof(ReportsController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        Assert.True(guard.AcceptKey);
    }
}

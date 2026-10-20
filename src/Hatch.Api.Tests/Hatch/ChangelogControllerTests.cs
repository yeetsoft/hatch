using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <c>GET /api/hatch/changelog</c>, wired against the in-memory database - the
/// two queries and the <see cref="Rollup"/> call that feed
/// <see cref="Changelog"/>'s functions, which <see cref="ChangelogTests"/>
/// already pins on their own.
/// </summary>
/// <remarks>
/// A read, never a write to the event trail - the harness sets
/// <c>ClosedAt</c>/<c>StatusId</c>/<c>ParentId</c> directly rather than
/// writing <see cref="EfHatchIssueEvent"/> rows, since nothing here reads
/// the trail.
/// </remarks>
public class ChangelogControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AChildIsNeverAnEntry_WhateverItsColumn_IncludingAChildEpic()
    {
        var h = await NewAsync();
        var parent = await h.CloseAsync("epic", "the epic", Now - TimeSpan.FromMinutes(1));
        await h.CloseAsync("epic", "a child epic", Now - TimeSpan.FromMinutes(1), parentId: parent.Id);
        await h.CloseAsync("task", "a child task", Now - TimeSpan.FromMinutes(1), parentId: parent.Id);

        var result = await h.GetChangelogAsync();

        var day = Assert.Single(result.Days);
        Assert.Equal(["AER-1"], day.Features.Select(e => e.Key));
        Assert.Empty(day.Fixes);
    }

    [Fact]
    public async Task AnIssueInADeferredColumn_IsNotAnEntry()
    {
        var h = await NewAsync();
        await h.FileAsync("task", "parked", h.Deferred);

        var result = await h.GetChangelogAsync();

        Assert.Empty(result.Days);
    }

    [Fact]
    public async Task AnIssueMovedBackOutOfATerminalColumn_StopsBeingAnEntry()
    {
        var h = await NewAsync();
        var issue = await h.CloseAsync("task", "closed then reopened", Now);
        issue.StatusId = h.InProgress;
        issue.ClosedAt = null;
        await h.Db.SaveChangesAsync();

        var result = await h.GetChangelogAsync();

        Assert.Empty(result.Days);
    }

    [Fact]
    public async Task ProjectId_NarrowsBothTheWindowAndTheMonthCounts()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        await h.CloseAsync("task", "in AER", Now - TimeSpan.FromMinutes(1));
        await h.CloseAsync("task", "in OTH", Now - TimeSpan.FromMinutes(1), projectId: other);

        var result = await h.GetChangelogAsync(projectId: h.ProjectId);

        var day = Assert.Single(result.Days);
        Assert.Equal(["AER-1"], day.Fixes.Select(e => e.Key));
        Assert.Equal(1, Assert.Single(result.Months).Count);
    }

    [Fact]
    public async Task AnUnknownProjectId_IsRefusedWithASentence()
    {
        var h = await NewAsync();

        var refused = Assert.IsType<BadRequestObjectResult>(
            (await h.Controller.GetChangelog(before: null, projectId: 999_999)).Result);

        Assert.Equal("there is no project 999999", refused.Value);
    }

    [Fact]
    public async Task AnUnparseableBefore_IsRefusedWithRangeBoundsSentence()
    {
        var h = await NewAsync();

        var refused = Assert.IsType<BadRequestObjectResult>(
            (await h.Controller.GetChangelog(before: "not a date", projectId: null)).Result);

        Assert.Equal(RangeBounds.NotAnInstant("not a date"), refused.Value);
    }

    [Fact]
    public async Task MonthCounts_CoverAMonthEntirelyOutsideTheRequestedWindow()
    {
        var h = await NewAsync();
        await h.CloseAsync("task", "long ago", Now - TimeSpan.FromDays(400));
        await h.CloseAsync("task", "recent", Now);

        var result = await h.GetChangelogAsync();

        Assert.Equal(2, result.Months.Count);
    }

    [Fact]
    public async Task NoIssuesAtAll_AnswersAnEmptyChangelog_NotARefusal()
    {
        var h = await NewAsync();

        var result = await h.GetChangelogAsync();

        Assert.Empty(result.Days);
        Assert.False(result.HasOlder);
        Assert.Null(result.Oldest);
    }

    [Fact]
    public async Task IssuesButNoneTerminal_AnswersAnEmptyChangelog_NotARefusal()
    {
        var h = await NewAsync();
        await h.FileAsync("task", "not closed", h.InProgress);

        var result = await h.GetChangelogAsync();

        Assert.Empty(result.Days);
    }

    // ---- Who may read it ----

    [Fact]
    public void TheRoute_AcceptsTheHatchScope()
    {
        var guard = typeof(ChangelogController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        Assert.Equal(ApiKeyScopes.Hatch, guard.AcceptScope);
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        var inProgress = new EfHatchStatus { Name = "in progress", SortOrder = 20 };
        var deferred = new EfHatchStatus { Name = "parked", SortOrder = 25, IsDeferred = true };
        // Named deliberately unlike "Done" - IsTerminal is the flag the controller
        // reads, and this status proves it is not reading the name instead.
        var shipped = new EfHatchStatus { Name = "shipped", SortOrder = 30, IsTerminal = true };
        db.AddRange(project, todo, inProgress, deferred, shipped);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);

        return new Harness
        {
            Db = db,
            Controller = new ChangelogController(db, time),
            ProjectId = project.Id,
            InProgress = inProgress.Id,
            Deferred = deferred.Id,
            Shipped = shipped.Id,
        };
    }

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required ChangelogController Controller { get; init; }
        public required int ProjectId { get; init; }
        public required int InProgress { get; init; }
        public required int Deferred { get; init; }
        public required int Shipped { get; init; }

        private int next = 1;

        public async Task<int> AddProjectAsync(string key)
        {
            var project = new EfHatchProject { Key = key, Name = key, CreatedAt = Now };
            Db.Projects.Add(project);
            await Db.SaveChangesAsync();
            return project.Id;
        }

        public async Task<EfHatchIssue> FileAsync(
            string type, string title, int statusId, long? parentId = null, int? projectId = null)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = projectId ?? ProjectId,
                Number = next++,
                Type = type,
                Title = title,
                Description = "",
                StatusId = statusId,
                Rank = 1024,
                ParentId = parentId,
                CreatedBy = "operator",
                CreatedAt = Now,
                UpdatedAt = Now,
            };

            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        /// <summary>Files the issue already in the terminal column, with <c>ClosedAt</c> set - the
        /// column <see cref="ChangelogController"/> reads the close instant off of.</summary>
        public async Task<EfHatchIssue> CloseAsync(
            string type, string title, DateTimeOffset closedAt, long? parentId = null, int? projectId = null)
        {
            var issue = await FileAsync(type, title, Shipped, parentId, projectId);
            issue.ClosedAt = closedAt;
            await Db.SaveChangesAsync();
            return issue;
        }

        public async Task<ChangelogDto> GetChangelogAsync(string? before = null, int? projectId = null, int days = 14) =>
            Value(await Controller.GetChangelog(before, projectId, days));
    }

    private static T Value<T>(Microsoft.AspNetCore.Mvc.ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");
}

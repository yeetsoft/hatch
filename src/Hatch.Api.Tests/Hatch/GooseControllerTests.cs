using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <c>GET /api/goose/runs</c> and <c>/runs/{id}</c>. A run holds what was seen
/// in every project, so the one thing worth pinning past ordering and limits is
/// that a caller whose scope is narrower reads nothing of it.
/// </summary>
public class GooseControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListRuns_IsNewestFirst_WithoutTheDetail()
    {
        var (options, db) = Open();
        db.GooseRuns.AddRange(Run(Now.AddMinutes(-2), "old"), Run(Now, "new"), Run(Now.AddMinutes(-1), "mid"));
        await db.SaveChangesAsync();

        var runs = Value(await new GooseController(db).ListRuns(null, default));

        Assert.Equal(3, runs.Count);
        Assert.Equal([Now, Now.AddMinutes(-1), Now.AddMinutes(-2)], runs.Select(r => r.LastRanAt));
        Assert.IsType<GooseRunDto>(runs[0]);
        Assert.Equal(7, runs[0].Sweeps);
        Assert.Equal(42, runs[0].LongestMs);
    }

    [Fact]
    public async Task ListRuns_CountsTheChecksAndTheOnesThatFailed()
    {
        var (options, db) = Open();
        db.GooseRuns.Add(new EfHatchGooseRun
        {
            FirstRanAt = Now.AddMinutes(-5), LastRanAt = Now, Sweeps = 2, LongestMs = 9,
            Entries = GooseEntry.Write([
                new GooseEntry("a", "looked", "fine", false, null),
                new GooseEntry("b", "looked", "", true, "boom"),
                new GooseEntry("c", "looked", "fine", false, null),
            ]),
        });
        await db.SaveChangesAsync();

        var run = Assert.Single(Value(await new GooseController(db).ListRuns(null, default)));

        Assert.Equal(3, run.Checks);
        Assert.Equal(1, run.ChecksFailed);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(100000, 5)]
    public async Task ListRuns_TakesALimitClampedToTheBounds(int limit, int expected)
    {
        var (_, db) = Open();
        for (var i = 0; i < 5; i++) db.GooseRuns.Add(Run(Now.AddMinutes(i), $"c{i}"));
        await db.SaveChangesAsync();

        var runs = Value(await new GooseController(db).ListRuns(limit, default));

        Assert.Equal(expected, runs.Count);
        Assert.Equal(Now.AddMinutes(4), runs[0].LastRanAt);
    }

    [Fact]
    public async Task ListRuns_WithNoLimit_ReturnsAtMostFifty()
    {
        var (_, db) = Open();
        for (var i = 0; i < 60; i++) db.GooseRuns.Add(Run(Now.AddMinutes(i), $"c{i}"));
        await db.SaveChangesAsync();

        Assert.Equal(GooseController.DefaultLimit, Value(await new GooseController(db).ListRuns(null, default)).Count);
    }

    [Fact]
    public async Task GetRun_AnswersOneRunWithItsEntries()
    {
        var (_, db) = Open();
        var run = Run(Now, "stuck");
        db.GooseRuns.Add(run);
        await db.SaveChangesAsync();

        var detail = Assert.IsType<OkObjectResult>((await new GooseController(db).GetRun(run.Id, default)).Result).Value as GooseRunDetailDto;

        Assert.NotNull(detail);
        Assert.Equal(run.Id, detail.Id);
        var entry = Assert.Single(detail.Entries);
        Assert.Equal("stuck", entry.Check);
        Assert.Equal("looked at stuck", entry.Looked);
        Assert.Equal("concluded stuck", entry.Concluded);
        Assert.False(entry.Failed);
    }

    [Fact]
    public async Task GetRun_ForAnIdThatIsNotThere_IsNotFound()
    {
        var (_, db) = Open();

        Assert.IsType<NotFoundResult>((await new GooseController(db).GetRun(99, default)).Result);
    }

    [Fact]
    public async Task ABoundedCaller_ReadsAnEmptyListAndAnyRunAsNotFound()
    {
        var (options, db) = Open();
        var run = Run(Now, "stuck");
        db.GooseRuns.Add(run);
        await db.SaveChangesAsync();

        await using var bounded = ScopeSeed.Request(options, ScopeSeed.Only(1));
        var controller = new GooseController(bounded);

        Assert.Empty(Value(await controller.ListRuns(null, default)));
        Assert.IsType<NotFoundResult>((await controller.GetRun(run.Id, default)).Result);
    }

    [Fact]
    public async Task AnUnboundedCaller_WithAScopeThatReadsEverything_ReadsTheRuns()
    {
        var (options, db) = Open();
        db.GooseRuns.Add(Run(Now, "stuck"));
        await db.SaveChangesAsync();

        await using var everything = ScopeSeed.Request(options, VisibleProjects.Everything);

        Assert.Single(Value(await new GooseController(everything).ListRuns(null, default)));
    }

    // ---- GET /api/goose/findings (HA-2496) ----

    [Fact]
    public async Task ListFindings_OpenFirstLongestStandingFirst_ThenClosedNewestFirst()
    {
        var (_, db) = Open();
        db.GooseFindings.AddRange(
            Finding("open-old", "a", began: Now.AddHours(-3), opened: Now.AddHours(-2)),
            Finding("open-new", "b", began: Now.AddHours(-1), opened: Now),
            Finding("closed-old", "c", began: Now.AddHours(-5), opened: Now.AddHours(-4), closed: Now.AddHours(-3)),
            Finding("closed-new", "d", began: Now.AddHours(-4), opened: Now.AddHours(-3), closed: Now.AddHours(-1)));
        await db.SaveChangesAsync();

        var findings = Value(await new GooseController(db).ListFindings(null, default));

        Assert.Equal(["open-old", "open-new", "closed-new", "closed-old"], findings.Select(f => f.Subject));
    }

    [Fact]
    public async Task ListFindings_OpenTrue_NarrowsToTheOpenOnes()
    {
        var (_, db) = Open();
        db.GooseFindings.AddRange(
            Finding("open", "a", began: Now, opened: Now),
            Finding("closed", "b", began: Now, opened: Now, closed: Now));
        await db.SaveChangesAsync();

        var findings = Value(await new GooseController(db).ListFindings(true, default));

        Assert.Equal("open", Assert.Single(findings).Subject);
    }

    [Fact]
    public async Task ListFindings_CarriesTheProjectKeyTheSentenceTheEvidenceTheTimesAndTheBugKey()
    {
        var (_, db) = Open();
        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Projects.Add(project);
        var status = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        db.Add(status);
        db.Issues.Add(new EfHatchIssue
        {
            ProjectId = project.Id, Number = 1, Type = "bug", Title = "fix it", Description = "",
            StatusId = status.Id, Rank = 1, CreatedAt = Now, UpdatedAt = Now, CreatedBy = "test",
        });
        await db.SaveChangesAsync();
        var bug = await db.Issues.SingleAsync();

        db.GooseFindings.Add(new EfHatchGooseFinding
        {
            Check = "train-ready", Subject = "AER", Token = "AER-9", ProjectId = project.Id,
            BeganAt = Now.AddMinutes(-40), OpenedAt = Now.AddMinutes(-10), Sentence = "AER's train is standing",
            Evidence = GooseFindingEvidence.Write([new GooseFindingEvidence("head", "AER-9")]),
            BugIssueId = bug.Id,
        });
        await db.SaveChangesAsync();

        var finding = Assert.Single(Value(await new GooseController(db).ListFindings(null, default)));

        Assert.Equal("train-ready", finding.Check);
        Assert.Equal("AER", finding.Subject);
        Assert.Equal("AER", finding.ProjectKey);
        Assert.Equal("AER's train is standing", finding.Sentence);
        Assert.Equal("head", Assert.Single(finding.Evidence).Label);
        Assert.Equal("AER-9", Assert.Single(finding.Evidence).Value);
        Assert.Equal(Now.AddMinutes(-40), finding.BeganAt);
        Assert.Equal(Now.AddMinutes(-10), finding.OpenedAt);
        Assert.Null(finding.ClosedAt);
        Assert.Equal("AER-1", finding.BugIssueKey);
    }

    [Fact]
    public async Task AFindingWithAProject_IsReadOnlyByACallerWhoCanSeeThatProject()
    {
        var (options, db) = Open();
        var visible = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var hidden = new EfHatchProject { Key = "OPS", Name = "Ops", CreatedAt = Now };
        db.Projects.AddRange(visible, hidden);
        await db.SaveChangesAsync();

        db.GooseFindings.AddRange(
            Finding("visible", "AER-1", Now, Now, projectId: visible.Id),
            Finding("hidden", "OPS-1", Now, Now, projectId: hidden.Id));
        await db.SaveChangesAsync();

        await using var scoped = ScopeSeed.Request(options, ScopeSeed.Only(visible.Id));

        var findings = Value(await new GooseController(scoped).ListFindings(null, default));

        Assert.Equal("visible", Assert.Single(findings).Subject);
    }

    [Fact]
    public async Task AFindingWithNoProject_IsReadOnlyByAnUnboundedCaller()
    {
        var (options, db) = Open();
        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.GooseFindings.Add(Finding("none", "no-project", Now, Now, projectId: null));
        await db.SaveChangesAsync();

        await using var scoped = ScopeSeed.Request(options, ScopeSeed.Only(project.Id));
        Assert.Empty(Value(await new GooseController(scoped).ListFindings(null, default)));

        await using var everything = ScopeSeed.Request(options, VisibleProjects.Everything);
        Assert.Single(Value(await new GooseController(everything).ListFindings(null, default)));
    }

    private static (DbContextOptions<HatchContext> Options, HatchContext Db) Open()
    {
        var options = new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return (options, new HatchContext(options));
    }

    private static EfHatchGooseFinding Finding(
        string subject, string token, DateTimeOffset began, DateTimeOffset opened, DateTimeOffset? closed = null, int? projectId = null) => new()
    {
        Check = "train-ready", Subject = subject, Token = token, ProjectId = projectId,
        BeganAt = began, OpenedAt = opened, ClosedAt = closed, Sentence = $"{subject} standing",
    };

    private static IReadOnlyList<GooseFindingDto> Value(ActionResult<IReadOnlyList<GooseFindingDto>> result) =>
        Assert.IsAssignableFrom<IReadOnlyList<GooseFindingDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static EfHatchGooseRun Run(DateTimeOffset last, string check) => new()
    {
        FirstRanAt = last.AddMinutes(-5),
        LastRanAt = last,
        Sweeps = 7,
        LongestMs = 42,
        Entries = GooseEntry.Write([new GooseEntry(check, $"looked at {check}", $"concluded {check}", false, null)]),
    };

    private static IReadOnlyList<GooseRunDto> Value(ActionResult<IReadOnlyList<GooseRunDto>> result) =>
        Assert.IsAssignableFrom<IReadOnlyList<GooseRunDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
}

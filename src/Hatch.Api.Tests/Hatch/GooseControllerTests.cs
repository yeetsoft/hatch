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

    private static (DbContextOptions<HatchContext> Options, HatchContext Db) Open()
    {
        var options = new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return (options, new HatchContext(options));
    }

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

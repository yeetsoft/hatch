using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="DreamWeaveController"/>: the one route under <c>/dreams</c>
/// that files an epic rather than a dream, in the column a playbook row
/// covering <c>epic</c> says an epic is born in. The title and the brief
/// themselves are <see cref="DreamWeave"/>'s own and tested there; this file
/// is the route around them - validation order, the birth column, and the
/// refusals, each leaving the board untouched.
/// </summary>
public class DreamWeaveControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    // ---- Filing ----

    [Fact]
    public async Task ASeedPress_FilesAnEpicInBreakdownTitledAndDescribedFromTheSeed()
    {
        var h = await NewAsync();
        var (breakdown, _) = await AddBreakdownRowAsync(h);
        var dreamOne = await AddDreamAsync(h, "the first dream");
        var dreamTwo = await AddDreamAsync(h, "the second dream");

        var result = await h.Controller.PostWeave(
            new DreamWeaveRequest(h.ProjectId, "seed", "  a thread worth pulling  ", [dreamOne, dreamTwo]), default);

        var dto = Created(result);
        Assert.Equal("epic", dto.Type);
        Assert.Equal("Weave: a thread worth pulling", dto.Title);
        Assert.Contains("a thread worth pulling", dto.Description);
        Assert.Contains(dreamOne.ToString(), dto.Description);
        Assert.Contains(dreamTwo.ToString(), dto.Description);

        var issue = await h.Db.Issues.Include(i => i.Events).SingleAsync();
        Assert.Equal(breakdown.Id, issue.StatusId);

        var created = issue.Events.Single(e => e.Kind == EfHatchIssueEvent.Created);
        Assert.Equal(h.Nathan.Name, created.Actor);
    }

    [Theory]
    [InlineData("pressing", "Weave: the most pressing thread")]
    [InlineData("common", "Weave: the most common thread")]
    [InlineData("next", "Weave: the next thread")]
    public async Task AFixedMode_FilesWithItsOwnTitleAndNoSeed(string mode, string title)
    {
        var h = await NewAsync();
        await AddBreakdownRowAsync(h);

        var dto = Created(await h.Controller.PostWeave(new DreamWeaveRequest(h.ProjectId, mode), default));

        Assert.Equal("epic", dto.Type);
        Assert.Equal(title, dto.Title);
    }

    [Fact]
    public async Task TheBirthColumnIsTheLeftmostRowCoveringEpic_AndAProjectRowBeatsTheBoardTieThere()
    {
        var h = await NewAsync();
        var breakdown = new EfHatchStatus { Name = "breakdown", SortOrder = 10 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var inProgress = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40 };
        h.Db.AddRange(breakdown, todo, inProgress, done);
        await h.Db.SaveChangesAsync();

        AddWeaveRow(h, breakdown.Id, done.Id, "epic");
        AddWeaveRow(h, todo.Id, done.Id, "epic");
        AddWeaveRow(h, inProgress.Id, done.Id, "epic");
        await h.Db.SaveChangesAsync();

        await h.Controller.PostWeave(new DreamWeaveRequest(h.ProjectId, "next"), default);
        Assert.Equal(breakdown.Id, (await h.Db.Issues.SingleAsync()).StatusId);

        AddWeaveRow(h, todo.Id, done.Id, "epic", h.ProjectId);
        await h.Db.SaveChangesAsync();

        await h.Controller.PostWeave(new DreamWeaveRequest(h.ProjectId, "next"), default);
        Assert.All(await h.Db.Issues.ToListAsync(), i => Assert.Equal(breakdown.Id, i.StatusId));

        AddWeaveRow(h, breakdown.Id, done.Id, "epic", h.ProjectId);
        await h.Db.SaveChangesAsync();

        await h.Controller.PostWeave(new DreamWeaveRequest(h.ProjectId, "next"), default);
        Assert.All(await h.Db.Issues.ToListAsync(), i => Assert.Equal(breakdown.Id, i.StatusId));
    }

    // ---- Refusals ----

    [Fact]
    public async Task AnUnknownMode_Is400AndFilesNothing()
    {
        var h = await NewAsync();
        await AddBreakdownRowAsync(h);

        var result = await h.Controller.PostWeave(new DreamWeaveRequest(h.ProjectId, "urgent"), default);

        Assert.Equal(400, ((ObjectResult)result.Result!).StatusCode);
        Assert.Contains("a mode is one of seed, pressing, common, next - not \"urgent\"", Reason(result.Result));
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AMissingMode_Is400AndFilesNothing()
    {
        var h = await NewAsync();
        await AddBreakdownRowAsync(h);

        var result = await h.Controller.PostWeave(new DreamWeaveRequest(h.ProjectId, null), default);

        Assert.Equal(400, ((ObjectResult)result.Result!).StatusCode);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task ASeedModeWithNoSeed_Is400AndFilesNothing()
    {
        var h = await NewAsync();
        await AddBreakdownRowAsync(h);

        var result = await h.Controller.PostWeave(new DreamWeaveRequest(h.ProjectId, "seed", "   "), default);

        Assert.Equal(400, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal("a seed is needed for mode seed", Reason(result.Result));
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AnotherModeWithASeed_Is400AndFilesNothing()
    {
        var h = await NewAsync();
        await AddBreakdownRowAsync(h);

        var result = await h.Controller.PostWeave(new DreamWeaveRequest(h.ProjectId, "pressing", "not for this mode"), default);

        Assert.Equal(400, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal("a seed is for mode seed", Reason(result.Result));
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task ANullProject_Is400AndFilesNothing()
    {
        var h = await NewAsync();
        await AddBreakdownRowAsync(h);

        var result = await h.Controller.PostWeave(new DreamWeaveRequest(null, "next"), default);

        Assert.Equal(400, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal("a weave needs a project - an epic needs a home", Reason(result.Result));
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AnUnknownProject_Is404AndFilesNothing()
    {
        var h = await NewAsync();
        await AddBreakdownRowAsync(h);

        var result = await h.Controller.PostWeave(new DreamWeaveRequest(h.ProjectId + 1000, "next"), default);

        Assert.Equal(404, ((ObjectResult)result.Result!).StatusCode);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AnUnknownDreamId_Is400NamingTheFirstMissingOne_AndFilesNothing()
    {
        var h = await NewAsync();
        await AddBreakdownRowAsync(h);
        var known = await AddDreamAsync(h, "a real dream");

        var result = await h.Controller.PostWeave(
            new DreamWeaveRequest(h.ProjectId, "next", DreamIds: [999, known]), default);

        Assert.Equal(400, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal("there is no dream 999", Reason(result.Result));
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task NoPlaybookRowCoveringEpic_Is400NamingTheProjectAndThePlaybooksPage_AndFilesNothing()
    {
        var h = await NewAsync();

        var result = await h.Controller.PostWeave(new DreamWeaveRequest(h.ProjectId, "next"), default);

        Assert.Equal(400, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal($"{h.ProjectKey} has no playbook row for an epic - add one on the Playbooks page first", Reason(result.Result));
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AKey_IsRefusedWithStatus403AndFilesNothing()
    {
        var h = await NewAsync(program: true);
        await AddBreakdownRowAsync(h);

        var result = await h.Controller.PostWeave(new DreamWeaveRequest(h.ProjectId, "next"), default);

        Assert.Equal(403, ((ObjectResult)result.Result!).StatusCode);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    // ---- Harness ----

    private sealed class Harness
    {
        public required DreamWeaveController Controller { get; init; }
        public required HatchContext Db { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public required string ProjectKey { get; init; }
        public required Actor Nathan { get; init; }
    }

    private static async Task<Harness> NewAsync(bool program = false)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);

        var actors = new StubActorDirectory();
        var nathan = actors.AddPerson("Nathan");

        var caller = program
            ? new StubCaller
            {
                Key = new EfApiKey
                {
                    Name = "hatch",
                    Hash = [1],
                    Prefix = "hatch_ak_x",
                    Scopes = [ApiKeyScopes.Hatch],
                    CreatedAt = Now,
                },
            }
            : new StubCaller { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };

        if (!program)
        {
            actors.Principal = nathan;
            actors.Me = nathan;
        }

        var ranks = new RankService(db);

        return new Harness
        {
            Controller = new DreamWeaveController(db, time, caller, actors, ranks, TestClaims.With())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            },
            Db = db,
            Time = time,
            ProjectId = project.Id,
            ProjectKey = project.Key,
            Nathan = nathan,
        };
    }

    /// <summary>A single row covering <c>epic</c> from a fresh Breakdown column, for every test that only needs *some* home.</summary>
    private static async Task<(EfHatchStatus Breakdown, EfHatchStatus Done)> AddBreakdownRowAsync(Harness h)
    {
        var breakdown = new EfHatchStatus { Name = "breakdown", SortOrder = 10 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 20 };
        h.Db.AddRange(breakdown, done);
        await h.Db.SaveChangesAsync();

        AddWeaveRow(h, breakdown.Id, done.Id, "epic");
        await h.Db.SaveChangesAsync();

        return (breakdown, done);
    }

    private static void AddWeaveRow(Harness h, int fromStatusId, int toStatusId, string types, int? projectId = null) =>
        h.Db.Add(new EfHatchPlaybook
        {
            FromStatusId = fromStatusId,
            ToStatusId = toStatusId,
            Types = types,
            Shape = EfHatchPlaybook.AnyShape,
            Prompt = "weave it",
            Model = "sonnet",
            Effort = "high",
            ProjectId = projectId,
            CreatedAt = Now,
            UpdatedAt = Now,
        });

    /// <summary>
    /// Straight through a <see cref="DreamsController"/>, never by
    /// constructing <see cref="EfHatchDream"/> directly - its
    /// <c>SearchVector</c> is an <c>NpgsqlTsVector</c>, which the in-memory
    /// provider cannot materialise.
    /// </summary>
    private static async Task<long> AddDreamAsync(Harness h, string body)
    {
        var dreams = new DreamsController(h.Db, new StubCaller { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } }, h.Time);
        var result = await dreams.AddDream(new DreamCreateRequest(body), default);
        var created = (CreatedResult)result.Result!;
        return ((DreamDto)created.Value!).Id;
    }

    private static IssueDto Created(ActionResult<IssueDto> result) =>
        result.Result is CreatedResult created
            ? (IssueDto)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {Reason(result.Result)}");

    private static string? Reason(IActionResult? result) => result switch
    {
        ObjectResult o => o.Value?.ToString(),
        _ => null,
    };

    /// <summary>Whoever is holding the phone: a person, or the key an agent carries.</summary>
    private sealed class StubCaller : ICallerIdentity
    {
        public EfPerson? Person { get; init; }

        public EfApiKey? Key { get; init; }

        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult(Person?.Id);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(Person);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult(Key);

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult<Actor?>(null);

        public Task<bool> IsProgramAsync(CancellationToken ct) => Task.FromResult(Key is not null);

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? CallerIdentity.Unattributed);
    }
}

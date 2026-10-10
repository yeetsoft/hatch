using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The one numeric field a row carries: set, read back, and cleared the same
/// way <see cref="IssueWipLimitControllerTests"/> exercises a wip limit. That
/// a key cannot write to a playbook at all is
/// <c>AdminSurfaceTests</c>'s business already, so it is not repeated here -
/// this is only the parse.
/// </summary>
public class PlaybooksControllerTests
{
    // ---- Setting, changing, clearing ----

    [Fact]
    public async Task ABudget_IsSetAndReadBack()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var patched = await h.SetAsync(row.Id, "5");

        Assert.Equal(5, patched.Budget);
        Assert.Equal(5, (await h.ReadAsync(row.Id)).Budget);
    }

    [Fact]
    public async Task AnEmptyString_ClearsIt()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, "5");

        var cleared = await h.SetAsync(row.Id, "");

        Assert.Null(cleared.Budget);
    }

    [Fact]
    public async Task Whitespace_ClearsIt_TheSameAsEmpty()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, "5");

        var cleared = await h.SetAsync(row.Id, "   ");

        Assert.Null(cleared.Budget);
    }

    [Fact]
    public async Task PatchingSomethingElse_WithBudgetOmitted_LeavesItUnchanged()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, budget: "5");

        var patched = Value(await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, "a new prompt", null, null), default));

        Assert.Equal("a new prompt", patched.Prompt);
        Assert.Equal(5, patched.Budget);
    }

    // ---- Refusals ----

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    public async Task ABadValue_IsRefused_AndWritesNothing(string bad)
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var refusal = await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, bad), default);

        Assert.Equal(
            $"a budget is a whole number of one or more, in millions of tokens - not \"{bad}\"",
            Reason(refusal.Result));
        Assert.Null((await h.ReadAsync(row.Id)).Budget);
    }

    // ---- Create ----

    [Fact]
    public async Task ABudget_CanBeSetOnCreate()
    {
        var h = await NewAsync();

        var created = await h.CreateAsync(budget: "5");

        Assert.Equal(5, created.Budget);
    }

    [Fact]
    public async Task ANullBudgetOnCreate_MeansNoBudget()
    {
        var h = await NewAsync();

        var created = await h.CreateAsync();

        Assert.Null(created.Budget);
    }

    [Fact]
    public async Task ABadBudgetOnCreate_IsRefused()
    {
        var h = await NewAsync();

        var refusal = await h.Playbooks.CreatePlaybook(
            new PlaybookCreateRequest(h.Todo, h.InProgress, [], "do it", "sonnet", "high", "abc"), default);

        Assert.Equal(
            "a budget is a whole number of one or more, in millions of tokens - not \"abc\"",
            Reason(refusal.Result));
    }

    // ---- BriefLimit: setting, changing, clearing ----

    [Fact]
    public async Task ABriefLimit_IsSetAndReadBack()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var patched = await h.SetAsync(row.Id, briefLimit: "5");

        Assert.Equal(5, patched.BriefLimit);
        Assert.Equal(5, (await h.ReadAsync(row.Id)).BriefLimit);
    }

    [Fact]
    public async Task ABriefLimit_AnEmptyString_ClearsIt()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, briefLimit: "5");

        var cleared = await h.SetAsync(row.Id, briefLimit: "");

        Assert.Null(cleared.BriefLimit);
    }

    [Fact]
    public async Task ABriefLimit_Whitespace_ClearsIt_TheSameAsEmpty()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, briefLimit: "5");

        var cleared = await h.SetAsync(row.Id, briefLimit: "   ");

        Assert.Null(cleared.BriefLimit);
    }

    [Fact]
    public async Task PatchingSomethingElse_WithBriefLimitOmitted_LeavesItUnchanged()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, briefLimit: "5");

        var patched = Value(await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, "a new prompt", null, null), default));

        Assert.Equal("a new prompt", patched.Prompt);
        Assert.Equal(5, patched.BriefLimit);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    public async Task ABriefLimit_ABadValue_IsRefused_AndWritesNothing(string bad)
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var refusal = await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, null, bad), default);

        Assert.Equal(
            $"a brief limit is a whole number of one or more, in characters - not \"{bad}\"",
            Reason(refusal.Result));
        Assert.Null((await h.ReadAsync(row.Id)).BriefLimit);
    }

    // ---- BriefLimit: create ----

    [Fact]
    public async Task ABriefLimit_CanBeSetOnCreate()
    {
        var h = await NewAsync();

        var created = await h.CreateAsync(briefLimit: "5");

        Assert.Equal(5, created.BriefLimit);
    }

    [Fact]
    public async Task ANullBriefLimitOnCreate_MeansNoBriefLimit()
    {
        var h = await NewAsync();

        var created = await h.CreateAsync();

        Assert.Null(created.BriefLimit);
    }

    [Fact]
    public async Task ABadBriefLimitOnCreate_IsRefused()
    {
        var h = await NewAsync();

        var refusal = await h.Playbooks.CreatePlaybook(
            new PlaybookCreateRequest(h.Todo, h.InProgress, [], "do it", "sonnet", "high", null, "abc"), default);

        Assert.Equal(
            "a brief limit is a whole number of one or more, in characters - not \"abc\"",
            Reason(refusal.Result));
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required PlaybooksController Playbooks { get; init; }
        public required int Todo { get; init; }
        public required int InProgress { get; init; }

        public async Task<PlaybookDto> CreateAsync(string? budget = null, string? briefLimit = null) =>
            Created(await Playbooks.CreatePlaybook(
                new PlaybookCreateRequest(Todo, InProgress, [], "do it", "sonnet", "high", budget, briefLimit), default));

        public async Task<PlaybookDto> SetAsync(int id, string? budget = null, string? briefLimit = null) =>
            Value(await Playbooks.PatchPlaybook(
                id, new PlaybookPatchRequest(null, null, null, null, null, null, budget, briefLimit), default));

        public async Task<PlaybookDto> ReadAsync(int id) =>
            Value(await Playbooks.GetPlaybooks(default)).Single(p => p.Id == id);
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
        db.AddRange(todo, doing);
        await db.SaveChangesAsync();

        return new Harness
        {
            Playbooks = new PlaybooksController(db, new FakeTimeProvider(Now)),
            Todo = todo.Id,
            InProgress = doing.Id,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {Reason(result.Result)}");

    /// <summary>The plain-text reason on a refusal - what the UI puts on screen.</summary>
    private static string Reason(IActionResult? result) => result switch
    {
        ObjectResult o => o.Value?.ToString() ?? $"{o.StatusCode}",
        StatusCodeResult s => s.StatusCode.ToString(),
        null => "no result",
        _ => result.GetType().Name,
    };
}

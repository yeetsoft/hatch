using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The board-wide table, guarded the same way <see cref="PlaybooksControllerTests"/>
/// holds <c>PlaybooksController</c>: a read any key may take, and a write
/// only a person at a browser may take - "Who may write" below is that same
/// shape, not repeated from <c>AdminSurfaceTests</c>.
/// </summary>
public class LocalModelsControllerTests
{
    [Fact]
    public async Task ARow_IsCreated_AndReadBack()
    {
        var h = await NewAsync();

        var created = await h.CreateAsync("fake:1b", 8_000, 1_000_000_000L);

        Assert.Equal("fake:1b", created.OllamaId);
        Assert.Equal(8_000, created.ContextLengthTokens);
        Assert.Equal(1_000_000_000L, created.MemoryFootprintBytes);
        Assert.Equal("Nathan", created.CreatedBy);
        Assert.Equal(Now, created.CreatedAt);
        Assert.Equal(created, Assert.Single(await h.ListAsync()));
    }

    [Fact]
    public async Task SeveralRows_List_InCreationOrder()
    {
        var h = await NewAsync();
        var second = await h.CreateAsync("fake:7b", 8_000, 5_000_000_000L);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        var first = await h.CreateAsync("fake:1b", 8_000, 1_000_000_000L);

        var rows = await h.ListAsync();

        Assert.Equal([second.Id, first.Id], rows.Select(r => r.Id));
    }

    [Fact]
    public async Task ADuplicateId_Is409_AndWritesNothing()
    {
        var h = await NewAsync();
        await h.CreateAsync("fake:1b", 8_000, 1_000_000_000L);

        var refusal = await h.Models.CreateLocalModel(new LocalModelCreateRequest("fake:1b", 8_000, 1_000_000_000L), default);

        Assert.Equal(409, Code(refusal.Result));
        Assert.Single(await h.ListAsync());
    }

    [Theory]
    [InlineData("opus")]
    [InlineData("claude-opus-5")]
    [InlineData("not an id")]
    public async Task ABadId_Is400_AndWritesNothing(string bad)
    {
        var h = await NewAsync();

        var refusal = await h.Models.CreateLocalModel(new LocalModelCreateRequest(bad, 8_000, 1_000_000_000L), default);

        Assert.Equal(400, Code(refusal.Result));
        Assert.Empty(await h.ListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ANonPositiveContextLength_Is400_AndWritesNothing(int bad)
    {
        var h = await NewAsync();

        var refusal = await h.Models.CreateLocalModel(new LocalModelCreateRequest("fake:1b", bad, 1_000_000_000L), default);

        Assert.Equal(400, Code(refusal.Result));
        Assert.Empty(await h.ListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ANonPositiveMemoryFootprint_Is400_AndWritesNothing(long bad)
    {
        var h = await NewAsync();

        var refusal = await h.Models.CreateLocalModel(new LocalModelCreateRequest("fake:1b", 8_000, bad), default);

        Assert.Equal(400, Code(refusal.Result));
        Assert.Empty(await h.ListAsync());
    }

    [Fact]
    public async Task ARow_IsDeleted()
    {
        var h = await NewAsync();
        var created = await h.CreateAsync("fake:1b", 8_000, 1_000_000_000L);

        var result = await h.Models.DeleteLocalModel(created.Id, default);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await h.ListAsync());
    }

    [Fact]
    public async Task DeletingAnUnknownId_Is404()
    {
        var h = await NewAsync();

        var result = await h.Models.DeleteLocalModel(999, default);

        Assert.IsType<NotFoundResult>(result);
    }

    // ---- Who may write ----

    [Fact]
    public async Task AnApiKey_IsRefusedOnCreateAndDelete()
    {
        var h = await NewAsync();
        var created = await h.CreateAsync("fake:1b", 8_000, 1_000_000_000L);
        var models = Writing(h.Db, new StubCallerIdentity { ApiKey = new EfApiKey { Name = "Claude", Prefix = "hatch_ak_abc", Hash = [], CreatedAt = Now } }, browser: true);

        var createRefusal = await models.CreateLocalModel(new LocalModelCreateRequest("fake:7b", 8_000, 1_000_000_000L), default);
        var deleteRefusal = await models.DeleteLocalModel(created.Id, default);

        AssertRefused(createRefusal.Result);
        AssertRefused(deleteRefusal);
        Assert.Single(await h.ListAsync());
    }

    [Fact]
    public async Task AKeylessRunner_IsRefusedOnCreateAndDelete()
    {
        var h = await NewAsync();
        var created = await h.CreateAsync("fake:1b", 8_000, 1_000_000_000L);
        var runner = new StubCallerIdentity { Local = new Actor(ActorKind.Key, Guid.NewGuid(), "a-runner") };
        var models = Writing(h.Db, runner, browser: true);

        var createRefusal = await models.CreateLocalModel(new LocalModelCreateRequest("fake:7b", 8_000, 1_000_000_000L), default);
        var deleteRefusal = await models.DeleteLocalModel(created.Id, default);

        AssertRefused(createRefusal.Result);
        AssertRefused(deleteRefusal);
        Assert.Single(await h.ListAsync());
    }

    [Fact]
    public async Task APersonAtTheBrowser_MayWrite()
    {
        var h = await NewAsync();

        var created = await h.CreateAsync("fake:1b", 8_000, 1_000_000_000L);

        Assert.Equal("fake:1b", created.OllamaId);
    }

    [Fact]
    public async Task AKey_MayRead()
    {
        var h = await NewAsync();
        await h.CreateAsync("fake:1b", 8_000, 1_000_000_000L);
        var models = Writing(h.Db, new StubCallerIdentity { ApiKey = new EfApiKey { Name = "Claude", Prefix = "hatch_ak_abc", Hash = [], CreatedAt = Now } }, browser: false);

        var rows = Value(await models.GetLocalModels(default));

        Assert.Single(rows);
    }

    private static void AssertRefused(IActionResult? result)
    {
        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal(PlaybookWrites.Sentence, refused.Value);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required LocalModelsController Models { get; init; }
        public required FakeTimeProvider Time { get; init; }

        public async Task<LocalModelDto> CreateAsync(string ollamaId, int contextLengthTokens, long memoryFootprintBytes) =>
            Created(await Models.CreateLocalModel(new LocalModelCreateRequest(ollamaId, contextLengthTokens, memoryFootprintBytes), default));

        public async Task<IReadOnlyList<LocalModelDto>> ListAsync() => Value(await Models.GetLocalModels(default));
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var time = new FakeTimeProvider(Now);

        return new Harness { Db = db, Models = AtTheBrowser(db, time), Time = time };
    }

    private static LocalModelsController AtTheBrowser(HatchContext db, TimeProvider time) =>
        Writing(db, new StubCallerIdentity { Person = new EfPerson { Id = Guid.NewGuid(), Name = "Nathan", Role = PersonRole.User, CreatedAt = Now, UpdatedAt = Now } }, browser: true, time);

    private static LocalModelsController Writing(HatchContext db, StubCallerIdentity caller, bool browser, TimeProvider? time = null)
    {
        var http = new DefaultHttpContext();
        if (browser) http.Request.Headers["Sec-Fetch-Mode"] = "navigate";
        return new LocalModelsController(db, time ?? new FakeTimeProvider(Now), caller)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {Reason(result.Result)}");

    private static int Code(IActionResult? result) => result switch
    {
        ObjectResult o => o.StatusCode ?? 0,
        StatusCodeResult s => s.StatusCode,
        _ => 0,
    };

    private static string Reason(IActionResult? result) => result switch
    {
        ObjectResult o => o.Value?.ToString() ?? $"{o.StatusCode}",
        StatusCodeResult s => s.StatusCode.ToString(),
        null => "no result",
        _ => result.GetType().Name,
    };
}

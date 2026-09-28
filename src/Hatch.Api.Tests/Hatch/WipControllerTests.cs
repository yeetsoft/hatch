using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The WIP section: what the limit and the flagged columns round-trip to, the
/// three refusals, and the one property the control surface rests on - that an
/// agent cannot raise its own ceiling.
/// </summary>
public class WipControllerTests
{
    // ---- The one narrowing ----

    [Fact]
    public void TheRoutesAreScopedTheWayTheClaimAndTheRunnerBoundAre()
    {
        Assert.Equal(ApiKeyScopes.Hatch, Scope(nameof(WipController.GetWip))!.AcceptScope);

        var put = Scope(nameof(WipController.PutWip));
        Assert.NotNull(put);
        Assert.Null(put.AcceptScope);

        // No class-level attribute for the PUT to inherit a scope from.
        Assert.Empty(typeof(WipController).GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false));
    }

    [Fact]
    public async Task AProgramCaller_IsRefusedOnThePutAndWritesNothing()
    {
        var h = await NewAsync(program: true);

        var refusal = await h.Wip.PutWip(new WipSectionRequest(Limit: "5"), default);

        Assert.Equal(403, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Null((await h.GetAsync()).Limit);
    }

    // ---- The limit ----

    [Fact]
    public async Task TheLimitIsSetReadBackAndClearedWithAnEmptyString()
    {
        var h = await NewAsync();

        var set = await h.PutAsync(new WipSectionRequest(Limit: "5"));
        Assert.Equal(5, set.Limit);
        Assert.Equal(5, (await h.GetAsync()).Limit);

        var cleared = await h.PutAsync(new WipSectionRequest(Limit: ""));
        Assert.Null(cleared.Limit);
        Assert.Null((await h.GetAsync()).Limit);
    }

    [Fact]
    public async Task AWholeNumberBelowOne_IsRefusedWithASentence()
    {
        var h = await NewAsync();

        var result = await h.Wip.PutWip(new WipSectionRequest(Limit: "0"), default);

        Assert.Equal("a WIP limit is a whole number of one or more - not \"0\"", BadRequestValue(result.Result));
        Assert.Null((await h.GetAsync()).Limit);
    }

    [Fact]
    public async Task ResendingTheSameLimit_KeepsTheRowsIdAndWritesNothing()
    {
        var h = await NewAsync();
        await h.PutAsync(new WipSectionRequest(Limit: "5"));
        var idBefore = await h.Db.WipLimits.Select(w => w.Id).SingleAsync();

        await h.PutAsync(new WipSectionRequest(Limit: "5"));
        var idAfter = await h.Db.WipLimits.Select(w => w.Id).SingleAsync();

        Assert.Equal(idBefore, idAfter);
    }

    // ---- The section ----

    [Fact]
    public async Task AnAbsentFieldIsLeftAlone()
    {
        var h = await NewAsync();
        await h.PutAsync(new WipSectionRequest(Limit: "5", StatusIds: [h.InProgress]));

        var result = await h.PutAsync(new WipSectionRequest());

        Assert.Equal(5, result.Limit);
        Assert.Equal([h.InProgress], result.StatusIds);
    }

    [Fact]
    public async Task AnEmptyArrayClearsTheSection()
    {
        var h = await NewAsync();
        await h.PutAsync(new WipSectionRequest(StatusIds: [h.InProgress, h.InReview]));

        var result = await h.PutAsync(new WipSectionRequest(StatusIds: []));

        Assert.Empty(result.StatusIds);
    }

    [Fact]
    public async Task DuplicateIdsCollapse()
    {
        var h = await NewAsync();

        var result = await h.PutAsync(new WipSectionRequest(StatusIds: [h.InProgress, h.InProgress]));

        Assert.Equal([h.InProgress], result.StatusIds);
    }

    [Fact]
    public async Task AColumnThatDoesNotExist_IsRefusedWithItsId()
    {
        var h = await NewAsync();

        var result = await h.Wip.PutWip(new WipSectionRequest(StatusIds: [99999]), default);

        Assert.Equal("there is no column 99999", BadRequestValue(result.Result));
        Assert.Empty((await h.GetAsync()).StatusIds);
    }

    [Fact]
    public async Task ADeferredColumn_IsRefusedByName()
    {
        var h = await NewAsync();

        var result = await h.Wip.PutWip(new WipSectionRequest(StatusIds: [h.Shelved]), default);

        Assert.Equal("\"shelved\" is deferred - parked work is never in progress", BadRequestValue(result.Result));
        Assert.Empty((await h.GetAsync()).StatusIds);
    }

    [Fact]
    public async Task ATerminalColumn_IsRefusedByName()
    {
        var h = await NewAsync();

        var result = await h.Wip.PutWip(new WipSectionRequest(StatusIds: [h.Done]), default);

        Assert.Equal("\"done\" is a done column - shipped work is never in progress", BadRequestValue(result.Result));
        Assert.Empty((await h.GetAsync()).StatusIds);
    }

    [Fact]
    public async Task ARefusedStatusIds_LeavesAGoodLimitUntouchedToo()
    {
        var h = await NewAsync();
        await h.PutAsync(new WipSectionRequest(Limit: "3"));

        var result = await h.Wip.PutWip(new WipSectionRequest(Limit: "10", StatusIds: [h.Done]), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(3, (await h.GetAsync()).Limit);
    }

    [Fact]
    public async Task AStrandedFlagOnAColumnThatHasBecomeDeferred_IsLeftOutOfTheReadAndClearedByTheNextWrite()
    {
        var h = await NewAsync();
        await h.PutAsync(new WipSectionRequest(StatusIds: [h.InReview]));

        // The column becomes deferred underneath the section, the way an
        // edit on the Statuses page would.
        var row = await h.Db.Statuses.SingleAsync(s => s.Id == h.InReview);
        row.IsDeferred = true;
        await h.Db.SaveChangesAsync();

        Assert.DoesNotContain(h.InReview, (await h.GetAsync()).StatusIds);
        Assert.True((await h.Db.Statuses.AsNoTracking().SingleAsync(s => s.Id == h.InReview)).IsWip);

        await h.PutAsync(new WipSectionRequest(StatusIds: [h.InProgress]));

        Assert.False((await h.Db.Statuses.AsNoTracking().SingleAsync(s => s.Id == h.InReview)).IsWip);
    }

    [Fact]
    public async Task TheReadIsInBoardOrder()
    {
        var h = await NewAsync();

        var result = await h.PutAsync(new WipSectionRequest(StatusIds: [h.InReview, h.InProgress]));

        Assert.Equal([h.InProgress, h.InReview], result.StatusIds);
    }

    [Fact]
    public void TheReadNamesStoriesAndBugs()
    {
        Assert.Equal(["story", "bug"], EfHatchPlaybook.SplitTypes(EfHatchWipLimit.StoriesAndBugs));
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static string BadRequestValue(ActionResult? result) =>
        Assert.IsType<BadRequestObjectResult>(result).Value as string ?? "";

    private static RequireRoleAttribute? Scope(string method) =>
        typeof(WipController).GetMethod(method)!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .SingleOrDefault();

    private sealed class Harness
    {
        public required WipController Wip { get; init; }
        public required HatchContext Db { get; init; }
        public required int InProgress { get; init; }
        public required int InReview { get; init; }
        public required int Done { get; init; }
        public required int Shelved { get; init; }

        public async Task<WipSectionDto> GetAsync() => Value(await Wip.GetWip(default));

        public async Task<WipSectionDto> PutAsync(WipSectionRequest request) => Value(await Wip.PutWip(request, default));

        private static T Value<T>(ActionResult<T> result) =>
            result.Value ?? throw new InvalidOperationException("expected a value, got a refusal");
    }

    private static async Task<Harness> NewAsync(bool program = false)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var todo = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        var inProgress = new EfHatchStatus { Name = "in progress", SortOrder = 20 };
        var inReview = new EfHatchStatus { Name = "in review", SortOrder = 30 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        var shelved = new EfHatchStatus { Name = "shelved", SortOrder = 50, IsDeferred = true };
        db.AddRange(todo, inProgress, inReview, done, shelved);
        await db.SaveChangesAsync();

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

        return new Harness
        {
            Wip = new WipController(db, caller),
            Db = db,
            InProgress = inProgress.Id,
            InReview = inReview.Id,
            Done = done.Id,
            Shelved = shelved.Id,
        };
    }

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

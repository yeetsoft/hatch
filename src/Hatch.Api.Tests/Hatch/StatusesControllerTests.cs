using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="StatusesController.PutProtected"/> and
/// <see cref="StatusesController.PutMergesPullRequest"/>: who may tick each,
/// the order they must be ticked in, the refusal a deferred or terminal
/// column gives either, and the fields <see cref="StatusCreateRequest"/> and
/// <see cref="StatusPatchRequest"/> deliberately do not carry.
/// </summary>
public class StatusesControllerTests
{
    // ---- Closed to a key ----

    [Fact]
    public async Task AKey_Is403OnBothRoutes()
    {
        var h = await NewAsync(program: true);
        var status = await h.SeedAsync();

        var protected_ = await h.Statuses.PutProtected(status.Id, new ProtectedRequest(true), default);
        Assert.Equal(403, ((ObjectResult)protected_.Result!).StatusCode);

        var merges = await h.Statuses.PutMergesPullRequest(status.Id, new MergesPullRequestRequest(true), default);
        Assert.Equal(403, ((ObjectResult)merges.Result!).StatusCode);

        var row = await h.RowAsync(status.Id);
        Assert.False(row.IsProtected);
        Assert.False(row.MergesPullRequest);
    }

    // ---- The happy path, in order ----

    [Fact]
    public async Task APersonTicksProtectedThenMerges_BothSucceed()
    {
        var h = await NewAsync();
        var status = await h.SeedAsync();

        var protected_ = Value(await h.Statuses.PutProtected(status.Id, new ProtectedRequest(true), default));
        Assert.True(protected_.IsProtected);

        var merges = Value(await h.Statuses.PutMergesPullRequest(status.Id, new MergesPullRequestRequest(true), default));
        Assert.True(merges.MergesPullRequest);
    }

    // ---- The order the other way round ----

    [Fact]
    public async Task MergesBeforeProtected_IsRefused()
    {
        var h = await NewAsync();
        var status = await h.SeedAsync();

        var result = await h.Statuses.PutMergesPullRequest(status.Id, new MergesPullRequestRequest(true), default);

        Assert.Contains("not protected", BadRequestMessage(result.Result!));
        Assert.False((await h.RowAsync(status.Id)).MergesPullRequest);
    }

    [Fact]
    public async Task UntickingProtectedOnAMergeColumn_IsRefused()
    {
        var h = await NewAsync();
        var status = await h.SeedAsync();
        Value(await h.Statuses.PutProtected(status.Id, new ProtectedRequest(true), default));
        Value(await h.Statuses.PutMergesPullRequest(status.Id, new MergesPullRequestRequest(true), default));

        var result = await h.Statuses.PutProtected(status.Id, new ProtectedRequest(false), default);

        Assert.Contains("merges the pull request", BadRequestMessage(result.Result!));
        Assert.True((await h.RowAsync(status.Id)).IsProtected);
    }

    // ---- Deferred and terminal columns ----

    [Theory]
    [InlineData(true, false, "is a done column")]
    [InlineData(false, true, "is deferred")]
    public async Task EitherFlagOnATerminalOrDeferredColumn_IsRefused(bool isTerminal, bool isDeferred, string phrase)
    {
        var h = await NewAsync();
        var status = await h.SeedAsync(isTerminal: isTerminal, isDeferred: isDeferred);

        var protectedResult = await h.Statuses.PutProtected(status.Id, new ProtectedRequest(true), default);
        Assert.Contains(phrase, BadRequestMessage(protectedResult.Result!));

        // Protect it first (out of band for the test, before it became terminal/deferred),
        // then try the merge route against the same terminal/deferred state.
        status.IsTerminal = false;
        status.IsDeferred = false;
        status.IsProtected = true;
        await h.Db.SaveChangesAsync();
        status.IsTerminal = isTerminal;
        status.IsDeferred = isDeferred;
        await h.Db.SaveChangesAsync();

        var mergesResult = await h.Statuses.PutMergesPullRequest(status.Id, new MergesPullRequestRequest(true), default);
        Assert.Contains(phrase, BadRequestMessage(mergesResult.Result!));
    }

    // ---- PatchStatus refuses naming the flag ----

    [Theory]
    [InlineData(true, false, true, false, "protected")]
    [InlineData(true, false, false, true, "protected")]
    [InlineData(false, true, true, false, "merges the pull request")]
    [InlineData(false, true, false, true, "merges the pull request")]
    public async Task PatchMakingAProtectedOrMergingColumnDoneOrDeferred_IsRefusedNamingTheFlag(
        bool isProtected, bool mergesPullRequest, bool patchTerminal, bool patchDeferred, string phrase)
    {
        var h = await NewAsync();
        var status = await h.SeedAsync();
        status.IsProtected = isProtected;
        status.MergesPullRequest = mergesPullRequest;
        await h.Db.SaveChangesAsync();

        var request = new StatusPatchRequest(
            null, null, IsTerminal: patchTerminal ? true : null, null, IsDeferred: patchDeferred ? true : null);

        var result = await h.Statuses.PatchStatus(status.Id, request, default);

        Assert.Contains(phrase, BadRequestMessage(result.Result!));
        var row = await h.RowAsync(status.Id);
        Assert.False(row.IsTerminal);
        Assert.False(row.IsDeferred);
    }

    // ---- POST and PATCH have no such fields ----

    [Fact]
    public void CreateAndPatchRequests_HaveNoSuchFields()
    {
        Assert.Null(typeof(StatusCreateRequest).GetProperty("IsProtected"));
        Assert.Null(typeof(StatusCreateRequest).GetProperty("MergesPullRequest"));
        Assert.Null(typeof(StatusPatchRequest).GetProperty("IsProtected"));
        Assert.Null(typeof(StatusPatchRequest).GetProperty("MergesPullRequest"));
    }

    // ---- The edge that is cut ----

    [Theory]
    [InlineData(nameof(StatusesController.PutProtected))]
    [InlineData(nameof(StatusesController.PutMergesPullRequest))]
    public void SettingEither_IsClosedToAnApiKey(string actionName)
    {
        var guard = typeof(StatusesController)
            .GetMethod(actionName)!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .SingleOrDefault();

        Assert.NotNull(guard);
        Assert.Null(guard.AcceptScope);

        Assert.Empty(typeof(StatusesController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false));
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StatusesController Statuses { get; init; }

        private int _nextSortOrder = 10;

        public async Task<EfHatchStatus> SeedAsync(string name = "review", bool isTerminal = false, bool isDeferred = false)
        {
            var status = new EfHatchStatus
            {
                Name = $"{name}-{_nextSortOrder}",
                SortOrder = _nextSortOrder,
                IsTerminal = isTerminal,
                IsDeferred = isDeferred,
            };
            _nextSortOrder += 10;
            Db.Add(status);
            await Db.SaveChangesAsync();
            return status;
        }

        public async Task<EfHatchStatus> RowAsync(int id) => await Db.Statuses.AsNoTracking().FirstAsync(s => s.Id == id);
    }

    private static async Task<Harness> NewAsync(bool program = false)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var caller = program
            ? new StubCallerIdentity
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
            : new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };

        return new Harness
        {
            Db = db,
            Statuses = new StatusesController(db, caller),
        };
    }

    /// <summary>Whoever is holding the phone: a person, or the key an agent carries.</summary>
    private sealed class StubCallerIdentity : ICallerIdentity
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

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException("expected a value");

    private static string BadRequestMessage(ActionResult result) => result switch
    {
        BadRequestObjectResult bad => bad.Value?.ToString() ?? "",
        _ => throw new InvalidOperationException($"expected a 400, got {result.GetType().Name}"),
    };
}

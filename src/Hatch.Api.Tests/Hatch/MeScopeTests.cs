using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What <c>GET /api/me</c> says of the caller's own scope: the mode a client
/// tests, and the ids it does not need to.
/// </summary>
public class MeScopeTests
{
    [Fact]
    public async Task AnUnboundedCaller_ReadsAll_WithNoIds()
    {
        var me = await MeAsync(VisibleProjects.Everything);

        Assert.Equal("all", me.Scope?.Mode);
        Assert.Empty(me.Scope!.ProjectIds);
    }

    [Fact]
    public async Task ABoundedCallerWithNothing_ReadsNone()
    {
        var me = await MeAsync(VisibleProjects.Nothing);

        Assert.Equal("none", me.Scope?.Mode);
        Assert.Empty(me.Scope!.ProjectIds);
    }

    [Fact]
    public async Task ABoundedCallerWithProjects_ReadsSome_WithTheIdsSorted()
    {
        var me = await MeAsync(new VisibleProjects(false, new HashSet<int> { 9, 2, 5 }));

        Assert.Equal("some", me.Scope?.Mode);
        Assert.Equal([2, 5, 9], me.Scope!.ProjectIds);
    }

    [Fact]
    public async Task EveryKindOfCaller_CarriesTheScope()
    {
        var none = VisibleProjects.Nothing;
        var person = new EfPerson { Name = "Ada", Role = PersonRole.User, CreatedAt = default, UpdatedAt = default };
        var key = new EfApiKey { Name = "k", Prefix = "p", Hash = [], CreatedAt = default, Owner = person };
        var someone = new Actor(ActorKind.Person, LocalCaller.PersonId, "Ada");

        Assert.Equal("none", (await MeAsync(none, person: person)).Scope?.Mode);
        Assert.Equal("none", (await MeAsync(none, key: key)).Scope?.Mode);
        Assert.Equal("none", (await MeAsync(none, local: someone)).Scope?.Mode);
        Assert.Equal("none", (await MeAsync(none, local: null, enabled: false)).Scope?.Mode);
    }

    [Fact]
    public async Task WhereThereIsNobody_ThereIsStillNoContent()
    {
        var result = await Controller(VisibleProjects.Nothing, local: null, enabled: true).GetMe(default);

        Assert.IsType<NoContentResult>(result.Result);
    }

    /// <summary>
    /// The key's answer is its owner's, through the real resolver: a key whose
    /// owner holds a <c>none</c> row reads <c>none</c>, and one whose owner is
    /// on <c>all</c> reads <c>all</c>.
    /// </summary>
    [Fact]
    public async Task AKey_ReadsItsOwnersScope_ThroughTheResolver()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var actors = new StubActorDirectory();
        var owner = actors.AddPerson("Ada");
        actors.Me = actors.AddKey("Runner");
        actors.Principal = owner;
        db.PersonScopes.Add(new EfHatchPersonScope
        {
            PersonId = owner.Id, Mode = PersonScopeMode.None, UpdatedBy = "test", UpdatedAt = DateTimeOffset.UnixEpoch,
        });
        await db.SaveChangesAsync();

        var scope = new CallerScope(
            db, actors, new ScopeSeed.FixedAccessor(new DefaultHttpContext()), Options.Create(new AuthOptions { Enabled = true }));
        var key = new EfApiKey
        {
            Name = "Runner", Prefix = "p", Hash = [], CreatedAt = default,
            Owner = new EfPerson { Name = "Ada", Role = PersonRole.User, CreatedAt = default, UpdatedAt = default },
        };

        var me = await MeAsync(await scope.ResolveAsync(default), key: key, enabled: true);

        Assert.Equal("key", me.Kind);
        Assert.Equal("none", me.Scope?.Mode);
    }

    private static async Task<MeDto> MeAsync(
        VisibleProjects visible, Actor? local = null, EfPerson? person = null, EfApiKey? key = null, bool enabled = false)
    {
        var result = await Controller(visible, local, person, key, enabled).GetMe(default);
        return result.Value ?? throw new InvalidOperationException("expected a value");
    }

    internal static MeController Controller(
        VisibleProjects visible, Actor? local, EfPerson? person = null, EfApiKey? key = null, bool enabled = false,
        HatchContext? db = null) =>
        new(
            new StubCaller(local, person, key),
            new StubSiteSettings(localPersonName: null),
            new FixedScope(visible),
            Options.Create(new AuthOptions { Enabled = enabled }),
            db ?? NewContext());

    internal static HatchContext NewContext() =>
        new(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    internal sealed class FixedScope(VisibleProjects visible) : ICallerScope
    {
        public Task<VisibleProjects> ResolveAsync(CancellationToken ct) => Task.FromResult(visible);
    }

    internal sealed class StubCaller(Actor? local, EfPerson? person, EfApiKey? key) : ICallerIdentity
    {
        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(person);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult(key);

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult(local);

        public Task<bool> IsProgramAsync(CancellationToken ct) => Task.FromResult(key is not null);

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(local?.Name ?? CallerIdentity.Unattributed);
    }
}

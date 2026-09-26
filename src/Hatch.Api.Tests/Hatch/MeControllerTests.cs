using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What the nav strip is told about who is sitting here - and, on a cluster
/// install, that it is told nothing at all rather than an empty box.
/// </summary>
public class MeControllerTests
{
    [Fact]
    public async Task WithNobodyAtAll_ThereIsNothingToDraw()
    {
        var result = await NewController(local: null).GetMe(default);

        Assert.IsType<NoContentResult>(result.Result);
    }

    [Theory]
    [InlineData(PersonRole.User, "user")]
    [InlineData(PersonRole.Admin, "admin")]
    public async Task ASignedInPerson_IsAnsweredWithTheirRoleAndCanSignOut(PersonRole role, string expected)
    {
        var controller = NewController(local: null, person: new EfPerson { Name = "Ada", Role = role, CreatedAt = default, UpdatedAt = default });

        var me = Value(await controller.GetMe(default));

        Assert.Equal("person", me.Kind);
        Assert.Equal("Ada", me.Name);
        Assert.Equal(expected, me.Role);
        Assert.True(me.Configured);
        Assert.True(me.CanSignOut);
    }

    [Fact]
    public async Task AKey_IsNobodyToDraw()
    {
        var controller = NewController(local: null, key: new EfApiKey { Name = "k", Prefix = "p", Hash = [], CreatedAt = default });

        Assert.IsType<NoContentResult>((await controller.GetMe(default)).Result);
    }

    [Fact]
    public async Task WithANameConfigured_ItIsAnsweredAndSaidToBeConfigured()
    {
        var controller = NewController(
            local: new Actor(ActorKind.Person, LocalCaller.PersonId, "Ada"),
            configuredName: "Ada");

        var person = Value(await controller.GetMe(default));

        Assert.Equal("local", person.Kind);
        Assert.Equal("Ada", person.Name);
        Assert.Null(person.Role);
        Assert.False(person.CanSignOut);
        Assert.True(person.Configured);
    }

    /// <summary>
    /// The one thing an operator who has just started Hatch for the first time
    /// needs told, and the one thing no amount of correct behaviour would tell
    /// them.
    /// </summary>
    [Fact]
    public async Task WithNothingConfiguredAnywhere_TheNameIsTheDefaultAndItSaysSo()
    {
        var controller = NewController(
            local: new Actor(ActorKind.Person, LocalCaller.PersonId, LocalCaller.DefaultName));

        var person = Value(await controller.GetMe(default));

        Assert.Equal(LocalCaller.DefaultName, person.Name);
        Assert.False(person.Configured);
    }

    /// <summary>The setting is read too, which is what makes AERIE-936 a page rather than a restart.</summary>
    [Fact]
    public async Task ANameFromTheSettingsTable_CountsAsConfigured()
    {
        var controller = NewController(
            local: new Actor(ActorKind.Person, LocalCaller.PersonId, "Grace"),
            settingName: "Grace");

        Assert.True(Value(await controller.GetMe(default)).Configured);
    }

    /// <summary>A runner is a program that named itself, and the strip this feeds is a browser's.</summary>
    [Fact]
    public async Task ARunner_IsNobodyToDraw()
    {
        var runner = new Actor(ActorKind.Key, LocalCaller.RunnerIdFor("host:/src"), "host:/src");

        Assert.IsType<NoContentResult>((await NewController(runner).GetMe(default)).Result);
    }

    private static MeController NewController(
        Actor? local, string? configuredName = null, string? settingName = null,
        EfPerson? person = null, EfApiKey? key = null) =>
        new(
            new StubLocalCaller(local, person, key),
            new StubSiteSettings(localPersonName: settingName),
            Options.Create(new AuthOptions { LocalPerson = { Name = configuredName ?? "" } }));

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException("expected a value");

    /// <summary>A caller stubbed into whichever lane a test names.</summary>
    private sealed class StubLocalCaller(Actor? local, EfPerson? person = null, EfApiKey? key = null) : ICallerIdentity
    {
        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(person);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult(key);

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult(local);

        public Task<bool> IsProgramAsync(CancellationToken ct) =>
            Task.FromResult(key is not null || local is { Kind: ActorKind.Key });

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(local?.Name ?? CallerIdentity.Unattributed);
    }
}

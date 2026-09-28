using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Auth;

/// <summary>
/// Who is out there, once local mode has somebody in it.
///
/// The directory is where "Assign to me" is actually decided:
/// AssigneeController answers <c>me</c> by looking the caller up <em>inside</em>
/// LiveAsync, so a local person who was not in the list would be offered no
/// press however MeAsync answered. It is also the one resolver every reader
/// goes through, which is what makes the board, the peek and the plan one
/// change rather than three.
/// </summary>
public class ActorDirectoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly Actor LocalPerson =
        new(ActorKind.Person, LocalCaller.PersonId, "Ada");

    [Fact]
    public async Task TheLocalPerson_IsWhoIAmWhenNothingElseAnswered()
    {
        var directory = NewDirectory(local: LocalPerson);

        Assert.Equal(LocalPerson, await directory.MeAsync(default));
    }

    /// <summary>
    /// First, ahead of the A→Z. "You" is not one name among many on a picker:
    /// it is the one press that is always the same press.
    /// </summary>
    [Fact]
    public async Task TheLocalPerson_LeadsTheDirectory()
    {
        var db = NewDb();
        db.Add(NewPerson("Alan"));
        db.Add(NewPerson("Zoe"));
        await db.SaveChangesAsync();

        var live = await NewDirectory(db, LocalPerson).LiveAsync(default);

        Assert.Equal(["Ada", "Alan", "Zoe"], live.Select(a => a.Name));
    }

    /// <summary>
    /// The whole point of the fixed id: the name is only what is drawn, so
    /// renaming yourself leaves every issue you were assigned still assigned.
    /// </summary>
    [Fact]
    public async Task RenamingYourself_StillResolvesToTheSameActor()
    {
        var renamed = LocalPerson with { Name = "Ada Lovelace" };

        var resolved = await NewDirectory(local: renamed)
            .ResolveAsync(ActorKind.Person, LocalCaller.PersonId, default);

        Assert.Equal("Ada Lovelace", resolved?.Name);
    }

    /// <summary>
    /// A runner is transient and self-named, so nothing may be assigned to one -
    /// and an id that means nothing tomorrow resolving to nobody today is the
    /// liveness rule reading correctly rather than a gap.
    /// </summary>
    [Fact]
    public async Task ARunner_IsNotInTheDirectoryAndNothingResolvesToIt()
    {
        var runner = new Actor(ActorKind.Key, LocalCaller.RunnerIdFor("host:/src"), "host:/src");
        var directory = NewDirectory(local: runner);

        Assert.Empty(await directory.LiveAsync(default));
        Assert.Null(await directory.ResolveAsync(ActorKind.Key, runner.Id, default));

        // It is still who is calling, though - that is what puts its name in
        // the trail.
        Assert.Equal(runner, await directory.MeAsync(default));
    }

    /// <summary>With a wall up there is no local caller, and the directory is what it always was.</summary>
    [Fact]
    public async Task WithNoLocalCaller_NothingIsPrepended()
    {
        var db = NewDb();
        db.Add(NewPerson("Alan"));
        await db.SaveChangesAsync();

        var directory = NewDirectory(db);

        Assert.Equal(["Alan"], (await directory.LiveAsync(default)).Select(a => a.Name));
        Assert.Null(await directory.MeAsync(default));
    }

    // ---- PrincipalAsync: whose work the caller is doing ----

    [Fact]
    public async Task TheSignedInPerson_IsTheirOwnPrincipal()
    {
        var ada = NewPerson("Ada");
        var directory = NewDirectoryWithCaller(new StubCallerIdentity { Person = ada });

        var principal = await directory.PrincipalAsync(default);

        Assert.Equal(new Actor(ActorKind.Person, ada.Id, "Ada"), principal);
    }

    [Fact]
    public async Task AKeyWithALiveOwner_IsAStandInForThatPerson()
    {
        var db = NewDb();
        var ada = NewPerson("Ada");
        db.Add(ada);
        await db.SaveChangesAsync();
        var key = new EfApiKey
        {
            Id = Guid.NewGuid(), Name = "Claude", Prefix = "hatch_ak_abc", Hash = [], CreatedAt = Now,
            OwnerPersonId = ada.Id,
        };
        var directory = NewDirectoryWithCaller(new StubCallerIdentity { ApiKey = key }, db);

        var principal = await directory.PrincipalAsync(default);

        Assert.Equal("Ada", principal?.Name);
    }

    /// <summary>The liveness rule reaching --mine without a line of its own: an owner that no longer resolves is the same as no owner.</summary>
    [Fact]
    public async Task AKeyWhoseOwnerWasDeleted_HasNoPrincipal()
    {
        var key = new EfApiKey
        {
            Id = Guid.NewGuid(), Name = "Claude", Prefix = "hatch_ak_abc", Hash = [], CreatedAt = Now,
            OwnerPersonId = Guid.NewGuid(),
        };
        var directory = NewDirectoryWithCaller(new StubCallerIdentity { ApiKey = key });

        Assert.Null(await directory.PrincipalAsync(default));
    }

    [Fact]
    public async Task AKeyWithNoOwner_HasNoPrincipal()
    {
        var key = new EfApiKey { Id = Guid.NewGuid(), Name = "Claude", Prefix = "hatch_ak_abc", Hash = [], CreatedAt = Now };
        var directory = NewDirectoryWithCaller(new StubCallerIdentity { ApiKey = key });

        Assert.Null(await directory.PrincipalAsync(default));
    }

    /// <summary>
    /// The one place PrincipalAsync deliberately disagrees with LocalAsync: a
    /// runner that named itself is still the local person's own work, not the
    /// runner's - a runner is never assignable, so it could never be "its own"
    /// tickets otherwise.
    /// </summary>
    [Fact]
    public async Task WithTheWallOffAndARunnerHeader_ThePrincipalIsStillTheLocalPerson()
    {
        var runner = new Actor(ActorKind.Key, LocalCaller.RunnerIdFor("host:/src"), "host:/src");
        var directory = NewDirectoryWithCaller(new StubCallerIdentity { Local = runner }, localPersonName: "Ada");

        var principal = await directory.PrincipalAsync(default);

        Assert.Equal(new Actor(ActorKind.Person, LocalCaller.PersonId, "Ada"), principal);
    }

    /// <summary>With the wall up, a caller that is neither a person nor a key has no principal - the same as MeAsync.</summary>
    [Fact]
    public async Task WithTheWallUpAndNothingAuthenticated_ThereIsNoPrincipal()
    {
        var directory = NewDirectoryWithCaller(new StubCallerIdentity(), wallEnabled: true);

        Assert.Null(await directory.PrincipalAsync(default));
    }

    private static AppDbContext NewDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static EfPerson NewPerson(string name) =>
        new() { Id = Guid.NewGuid(), Name = name, CreatedAt = Now, UpdatedAt = Now };

    private static ActorDirectory NewDirectory(AppDbContext? db = null, Actor? local = null) =>
        new(db ?? NewDb(), new StubLocalCaller(local), new FakeTimeProvider(Now),
            new StubSiteSettings(), Options.Create(new AuthOptions()));

    private static ActorDirectory NewDirectoryWithCaller(
        ICallerIdentity caller, AppDbContext? db = null, bool wallEnabled = false, string? localPersonName = null) =>
        new(db ?? NewDb(), caller, new FakeTimeProvider(Now),
            new StubSiteSettings(localPersonName: localPersonName), Options.Create(new AuthOptions { Enabled = wallEnabled }));

    /// <summary>A caller that is only ever the third lane - which is every request in local mode.</summary>
    private sealed class StubLocalCaller(Actor? local) : ICallerIdentity
    {
        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult<EfPerson?>(null);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult<EfApiKey?>(null);

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult(local);

        public Task<bool> IsProgramAsync(CancellationToken ct) =>
            Task.FromResult(local is { Kind: ActorKind.Key });

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(local?.Name ?? CallerIdentity.Unattributed);
    }
}

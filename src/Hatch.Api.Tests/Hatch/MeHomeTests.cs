using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What <c>GET /api/me</c> says of the caller's home screen: their own row,
/// their owner's for a key, and <c>board</c> wherever there is no row to read.
/// </summary>
public class MeHomeTests
{
    [Fact]
    public async Task APersonWithNoRow_ReadsBoard()
    {
        var me = await MeAsync(person: NewPerson());

        Assert.Equal("person", me.Kind);
        Assert.Equal(PersonHomeValue.Board, me.Home);
    }

    [Fact]
    public async Task APersonWithARow_ReadsIt()
    {
        var person = NewPerson();
        var db = await WithRowsAsync((person.Id, PersonHomeValue.Ambitions));

        var me = await MeAsync(person: person, db: db);

        Assert.Equal(PersonHomeValue.Ambitions, me.Home);
    }

    [Fact]
    public async Task AKey_ReadsItsOwnersRow_AndNobodyElses()
    {
        var owner = NewPerson();
        var other = NewPerson();
        var db = await WithRowsAsync((owner.Id, PersonHomeValue.Ambitions), (other.Id, PersonHomeValue.Board));

        var me = await MeAsync(key: NewKey(owner), db: db, enabled: true);

        Assert.Equal("key", me.Kind);
        Assert.Equal(PersonHomeValue.Ambitions, me.Home);
    }

    [Fact]
    public async Task AKeyWithNoOwner_ReadsBoard_EvenWhereOthersHaveRows()
    {
        var db = await WithRowsAsync((Guid.NewGuid(), PersonHomeValue.Ambitions));

        var me = await MeAsync(key: NewKey(owner: null), db: db, enabled: true);

        Assert.Equal(PersonHomeValue.Board, me.Home);
    }

    [Fact]
    public async Task TheLocalPersonWithTheWallOff_ReadsTheirOwnRow()
    {
        var db = await WithRowsAsync((LocalCaller.PersonId, PersonHomeValue.Ambitions));
        var local = new Actor(ActorKind.Person, LocalCaller.PersonId, "Ada");

        var me = await MeAsync(local: local, db: db);

        Assert.Equal("local", me.Kind);
        Assert.Equal(PersonHomeValue.Ambitions, me.Home);
    }

    [Fact]
    public async Task NobodyAtAll_ReadsBoard()
    {
        var db = await WithRowsAsync((LocalCaller.PersonId, PersonHomeValue.Ambitions));

        var me = await MeAsync(db: db);

        Assert.Equal("none", me.Kind);
        Assert.Equal(PersonHomeValue.Board, me.Home);
    }

    /// <summary>A stale value reads as the board, so it can never break the nav strip.</summary>
    [Fact]
    public async Task ARowHoldingAnUnknownValue_ReadsBoard()
    {
        var person = NewPerson();
        var db = await WithRowsAsync((person.Id, "somewhere-else"));

        var me = await MeAsync(person: person, db: db);

        Assert.Equal(PersonHomeValue.Board, me.Home);
    }

    private static async Task<MeDto> MeAsync(
        Actor? local = null, EfPerson? person = null, EfApiKey? key = null, bool enabled = false,
        HatchContext? db = null)
    {
        var result = await MeScopeTests.Controller(VisibleProjects.Everything, local, person, key, enabled, db).GetMe(default);
        return result.Value ?? throw new InvalidOperationException("expected a value");
    }

    private static EfPerson NewPerson() =>
        new() { Id = Guid.NewGuid(), Name = "Ada", Role = PersonRole.User, CreatedAt = default, UpdatedAt = default };

    private static EfApiKey NewKey(EfPerson? owner) =>
        new()
        {
            Name = "Runner", Prefix = "p", Hash = [], CreatedAt = default,
            Owner = owner, OwnerPersonId = owner?.Id,
        };

    private static async Task<HatchContext> WithRowsAsync(params (Guid PersonId, string Home)[] rows)
    {
        var db = MeScopeTests.NewContext();
        foreach (var (personId, home) in rows)
            db.PersonHomes.Add(new EfHatchPersonHome { PersonId = personId, Home = home, UpdatedAt = DateTimeOffset.UnixEpoch });
        await db.SaveChangesAsync();
        return db;
    }
}

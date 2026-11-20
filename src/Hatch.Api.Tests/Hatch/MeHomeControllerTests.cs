using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <c>PUT /api/me/home</c>, wired against the in-memory database: the
/// person-only write, the row it leaves, and the <see cref="MeDto"/> it answers.
/// </summary>
public class MeHomeControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 3, 0, 0, TimeSpan.Zero);

    private const string NotAnAgent = "setting a home screen is a person's own, not an agent's";
    private const string TheTwo = "a home screen is one of board, ambitions";

    [Fact]
    public async Task WritingAmbitions_LandsOneRow_AnswersIt_AndGetMeReadsIt()
    {
        var h = New();

        var answer = Value(await h.Controller.PutHome(new PutHomeRequest("ambitions"), default));

        Assert.Equal("ambitions", answer.Home);
        var row = Assert.Single(await h.Db.PersonHomes.ToListAsync());
        Assert.Equal(h.Person!.Id, row.PersonId);
        Assert.Equal("ambitions", row.Home);
        Assert.Equal(Now, row.UpdatedAt);

        Assert.Equal("ambitions", Value(await h.Me().GetMe(default)).Home);
    }

    [Fact]
    public async Task ASecondWrite_ChangesTheSameRow()
    {
        var h = New();
        await h.Controller.PutHome(new PutHomeRequest("ambitions"), default);

        h.Time.Advance(TimeSpan.FromMinutes(5));
        var answer = Value(await h.Controller.PutHome(new PutHomeRequest("board"), default));

        Assert.Equal("board", answer.Home);
        var row = Assert.Single(await h.Db.PersonHomes.ToListAsync());
        Assert.Equal("board", row.Home);
        Assert.Equal(Now + TimeSpan.FromMinutes(5), row.UpdatedAt);
    }

    [Fact]
    public async Task WritingBoardForSomebodyWithNoRow_CreatesABoardRow()
    {
        var h = New();

        var answer = Value(await h.Controller.PutHome(new PutHomeRequest("board"), default));

        Assert.Equal("board", answer.Home);
        Assert.Equal("board", Assert.Single(await h.Db.PersonHomes.ToListAsync()).Home);
    }

    [Fact]
    public async Task AKey_IsRefusedWithASentence_AndWritesNothing()
    {
        var h = New(key: NewKey());

        var refused = Assert.IsType<ObjectResult>(
            (await h.Controller.PutHome(new PutHomeRequest("ambitions"), default)).Result);

        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal(NotAnAgent, refused.Value);
        Assert.Empty(await h.Db.PersonHomes.ToListAsync());
    }

    [Fact]
    public async Task AKeyWithAnUnknownWord_IsRefusedAsAKey_SoItNeverLearnsTheWords()
    {
        var h = New(key: NewKey());

        var refused = Assert.IsType<ObjectResult>(
            (await h.Controller.PutHome(new PutHomeRequest("somewhere-else"), default)).Result);

        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal(NotAnAgent, refused.Value);
    }

    [Theory]
    [InlineData("somewhere-else")]
    [InlineData("")]
    [InlineData("Board")]
    [InlineData(null)]
    public async Task AnUnknownWord_IsA400NamingTheTwo_AndWritesNothing(string? word)
    {
        var h = New();

        var refused = Assert.IsType<BadRequestObjectResult>(
            (await h.Controller.PutHome(new PutHomeRequest(word), default)).Result);

        Assert.Equal(TheTwo, refused.Value);
        Assert.Empty(await h.Db.PersonHomes.ToListAsync());
    }

    [Fact]
    public async Task ANullBody_IsA400_AndWritesNothing()
    {
        var h = New();

        var refused = Assert.IsType<BadRequestObjectResult>((await h.Controller.PutHome(null!, default)).Result);

        Assert.Equal(TheTwo, refused.Value);
        Assert.Empty(await h.Db.PersonHomes.ToListAsync());
    }

    [Fact]
    public async Task TheLocalPersonWithTheWallOff_Writes_KeyedByTheLocalPerson()
    {
        var local = new Actor(ActorKind.Person, LocalCaller.PersonId, "Ada");
        var h = New(local: local);

        var answer = Value(await h.Controller.PutHome(new PutHomeRequest("ambitions"), default));

        Assert.Equal("local", answer.Kind);
        Assert.Equal("ambitions", answer.Home);
        var row = Assert.Single(await h.Db.PersonHomes.ToListAsync());
        Assert.Equal(LocalCaller.PersonId, row.PersonId);
    }

    [Fact]
    public async Task WithNoPrincipalAtAll_IsRefusedWithASentence_NotThrown()
    {
        var h = New(nobody: true);

        var refused = Assert.IsType<ObjectResult>(
            (await h.Controller.PutHome(new PutHomeRequest("ambitions"), default)).Result);

        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal("setting a home screen needs somebody signed in", refused.Value);
        Assert.Empty(await h.Db.PersonHomes.ToListAsync());
    }

    private static EfApiKey NewKey() => new() { Name = "hatch", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };

    /// <summary>A signed-in person by default; a key, the local person, or nobody on request.</summary>
    private static Harness New(EfApiKey? key = null, Actor? local = null, bool nobody = false)
    {
        var db = MeScopeTests.NewContext();
        var time = new FakeTimeProvider(Now);

        var person = key is null && local is null && !nobody
            ? new EfPerson { Id = Guid.NewGuid(), Name = "Ada", Role = PersonRole.User, CreatedAt = Now, UpdatedAt = Now }
            : null;

        var actors = new StubActorDirectory
        {
            Principal = nobody ? null
                : person is not null ? new Actor(ActorKind.Person, person.Id, person.Name)
                : local,
        };

        return new Harness(db, time, actors, person, local, key);
    }

    private sealed class Harness
    {
        private readonly MeScopeTests.StubCaller caller;

        public Harness(
            HatchContext db, FakeTimeProvider time, StubActorDirectory actors, EfPerson? person, Actor? local, EfApiKey? key)
        {
            Db = db;
            Time = time;
            Person = person;
            caller = new MeScopeTests.StubCaller(local, person, key);
            Controller = new MeHomeController(
                db, caller, actors, time, new StubSiteSettings(),
                new MeScopeTests.FixedScope(VisibleProjects.Everything),
                Options.Create(new AuthOptions { Enabled = false }));
        }

        public HatchContext Db { get; }
        public FakeTimeProvider Time { get; }
        public EfPerson? Person { get; }
        public MeHomeController Controller { get; }

        public MeController Me() =>
            new(caller, new StubSiteSettings(), new MeScopeTests.FixedScope(VisibleProjects.Everything),
                Options.Create(new AuthOptions { Enabled = false }), Db);
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");
}

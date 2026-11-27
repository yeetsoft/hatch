using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <c>PUT /api/me/visits/ambitions</c>, wired against the in-memory database:
/// the person-only write and the one row it leaves.
/// </summary>
public class MeVisitControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 3, 0, 0, TimeSpan.Zero);

    private const string NotAnAgent = "recording a visit is a person's own, not an agent's";

    [Fact]
    public async Task APersonsVisit_LandsOneRow_AndAnswersTheInstantStored()
    {
        var h = New();

        var answer = Value(await h.Controller.PutAmbitions(default));

        Assert.Equal(new PersonVisitDto("ambitions", Now), answer);
        var row = Assert.Single(await h.Db.PersonVisits.ToListAsync());
        Assert.Equal(h.Person!.Id, row.PersonId);
        Assert.Equal("ambitions", row.Page);
        Assert.Equal(Now, row.VisitedAt);
    }

    [Fact]
    public async Task ASecondVisit_MovesTheSameRow()
    {
        var h = New();
        await h.Controller.PutAmbitions(default);

        h.Time.Advance(TimeSpan.FromMinutes(5));
        var answer = Value(await h.Controller.PutAmbitions(default));

        Assert.Equal(Now + TimeSpan.FromMinutes(5), answer.VisitedAt);
        var row = Assert.Single(await h.Db.PersonVisits.ToListAsync());
        Assert.Equal(Now + TimeSpan.FromMinutes(5), row.VisitedAt);
    }

    [Fact]
    public async Task TwoPeople_HaveARowEach()
    {
        var h = New();
        h.Db.PersonVisits.Add(new EfHatchPersonVisit { PersonId = Guid.NewGuid(), Page = PersonVisitPage.Ambitions, VisitedAt = Now.AddDays(-1) });
        await h.Db.SaveChangesAsync();

        await h.Controller.PutAmbitions(default);

        Assert.Equal(2, await h.Db.PersonVisits.CountAsync());
    }

    [Fact]
    public async Task AKey_IsRefusedWithASentence_AndWritesNothing()
    {
        var h = New(key: NewKey());

        var refused = Assert.IsType<ObjectResult>((await h.Controller.PutAmbitions(default)).Result);

        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal(NotAnAgent, refused.Value);
        Assert.Empty(await h.Db.PersonVisits.ToListAsync());
    }

    [Fact]
    public async Task TheLocalPersonWithTheWallOff_Writes_KeyedByTheLocalPerson()
    {
        var local = new Actor(ActorKind.Person, LocalCaller.PersonId, "Ada");
        var h = New(local: local);

        Value(await h.Controller.PutAmbitions(default));

        Assert.Equal(LocalCaller.PersonId, Assert.Single(await h.Db.PersonVisits.ToListAsync()).PersonId);
    }

    [Fact]
    public async Task WithNoPrincipalAtAll_IsRefusedWithASentence_NotThrown()
    {
        var h = New(nobody: true);

        var refused = Assert.IsType<ObjectResult>((await h.Controller.PutAmbitions(default)).Result);

        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal("recording a visit needs somebody signed in", refused.Value);
        Assert.Empty(await h.Db.PersonVisits.ToListAsync());
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

        return new Harness(db, time, person, new MeVisitController(db, new MeScopeTests.StubCaller(local, person, key), actors, time));
    }

    private sealed record Harness(HatchContext Db, FakeTimeProvider Time, EfPerson? Person, MeVisitController Controller);

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");
}

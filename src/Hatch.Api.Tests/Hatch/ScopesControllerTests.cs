using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// An admin reads and sets what each person may see. Nothing here enforces the
/// scope; <see cref="ScopeEnforcementTests"/> does.
/// </summary>
public class ScopesControllerTests
{
    [Fact]
    public async Task APersonWithNoRow_ReadsAsAllWithEveryProject()
    {
        var h = await NewAsync();

        var scope = Value(await h.Scopes.GetScope(h.Ada.Id, default));

        Assert.Equal(PersonScopeMode.All, scope.Mode);
        Assert.Equal(h.ProjectIds, scope.ProjectIds);
    }

    [Fact]
    public async Task TheList_HasEveryPerson_AndNotADeletedOne_NorAKey()
    {
        var h = await NewAsync();
        var gone = h.Actors.AddPerson("Gone");
        h.Actors.AddKey("Runner");
        await h.Scopes.PutScope(gone.Id, new PersonScopeWriteRequest("none", null), default);
        h.Actors.Live.Remove(gone);

        var all = Value(await h.Scopes.GetScopes(default));

        Assert.Equal([h.Admin.Id, h.Ada.Id], all.Select(s => s.PersonId));
        Assert.All(all, s => Assert.Equal(PersonScopeMode.All, s.Mode));
    }

    [Fact]
    public async Task TheList_ReadsWhatWasSetForEach()
    {
        var h = await NewAsync();
        await h.Scopes.PutScope(h.Ada.Id, new PersonScopeWriteRequest("some", [h.ProjectIds[1]]), default);

        var ada = Value(await h.Scopes.GetScopes(default)).Single(s => s.PersonId == h.Ada.Id);

        Assert.Equal(PersonScopeMode.Some, ada.Mode);
        Assert.Equal([h.ProjectIds[1]], ada.ProjectIds);
    }

    [Fact]
    public async Task APutThenAGet_RoundTripsNone()
    {
        var h = await NewAsync();

        var written = Value(await h.Scopes.PutScope(h.Ada.Id, new PersonScopeWriteRequest("none", null), default));
        var read = Value(await h.Scopes.GetScope(h.Ada.Id, default));

        AssertScope(new PersonScopeDto(h.Ada.Id, "none", []), written);
        AssertScope(written, read);
    }

    [Fact]
    public async Task APutThenAGet_RoundTripsAll()
    {
        var h = await NewAsync();
        await h.Scopes.PutScope(h.Ada.Id, new PersonScopeWriteRequest("none", null), default);

        var written = Value(await h.Scopes.PutScope(h.Ada.Id, new PersonScopeWriteRequest("all", null), default));
        var read = Value(await h.Scopes.GetScope(h.Ada.Id, default));

        AssertScope(new PersonScopeDto(h.Ada.Id, "all", h.ProjectIds), written);
        AssertScope(written, read);
    }

    [Fact]
    public async Task APutThenAGet_RoundTripsSome_AndAnEmptyListIsARealScope()
    {
        var h = await NewAsync();

        var written = Value(await h.Scopes.PutScope(h.Ada.Id, new PersonScopeWriteRequest("some", [h.ProjectIds[2], h.ProjectIds[0], h.ProjectIds[2]]), default));
        AssertScope(new PersonScopeDto(h.Ada.Id, "some", [h.ProjectIds[0], h.ProjectIds[2]]), written);
        AssertScope(written, Value(await h.Scopes.GetScope(h.Ada.Id, default)));

        var empty = Value(await h.Scopes.PutScope(h.Ada.Id, new PersonScopeWriteRequest("some", []), default));
        AssertScope(new PersonScopeDto(h.Ada.Id, "some", []), empty);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("none")]
    public async Task SettingAllOrNone_LeavesNoProjectListBehind(string mode)
    {
        var h = await NewAsync();
        await h.Scopes.PutScope(h.Ada.Id, new PersonScopeWriteRequest("some", h.ProjectIds), default);
        Assert.Equal(3, await h.Db.PersonScopeProjects.CountAsync());

        await h.Scopes.PutScope(h.Ada.Id, new PersonScopeWriteRequest(mode, h.ProjectIds), default);

        Assert.Empty(await h.Db.PersonScopeProjects.ToListAsync());
        Assert.Single(await h.Db.PersonScopes.ToListAsync());
    }

    [Fact]
    public async Task ProjectIds_AreIgnoredUnderAll()
    {
        var h = await NewAsync();

        // An id that does not exist is not even looked at when it is not read.
        var written = Value(await h.Scopes.PutScope(h.Ada.Id, new PersonScopeWriteRequest("all", [9999]), default));

        Assert.Equal(h.ProjectIds, written.ProjectIds);
        Assert.Empty(await h.Db.PersonScopeProjects.ToListAsync());
    }

    [Fact]
    public async Task AnAdmin_MaySetTheirOwn()
    {
        var h = await NewAsync();

        var written = Value(await h.Scopes.PutScope(h.Admin.Id, new PersonScopeWriteRequest("none", null), default));

        Assert.Equal("none", written.Mode);
    }

    [Fact]
    public async Task ADeletedProject_DisappearsFromAChosenList()
    {
        var h = await NewAsync();
        await h.Scopes.PutScope(h.Ada.Id, new PersonScopeWriteRequest("some", [h.ProjectIds[0], h.ProjectIds[1]]), default);

        h.Db.Projects.Remove(await h.Db.Projects.FirstAsync(p => p.Id == h.ProjectIds[1]));
        await h.Db.SaveChangesAsync();

        Assert.Equal([h.ProjectIds[0]], Value(await h.Scopes.GetScope(h.Ada.Id, default)).ProjectIds);
        Assert.Equal([h.ProjectIds[0]], Value(await h.Scopes.GetScopes(default)).Single(s => s.PersonId == h.Ada.Id).ProjectIds);
    }

    [Fact]
    public async Task AnUnknownPerson_IsNotFoundOnBothVerbs_AndNothingIsWritten()
    {
        var h = await NewAsync();
        var nobody = Guid.NewGuid();

        Assert.IsType<NotFoundResult>((await h.Scopes.GetScope(nobody, default)).Result);
        Assert.IsType<NotFoundResult>((await h.Scopes.PutScope(nobody, new PersonScopeWriteRequest("none", null), default)).Result);
        Assert.Empty(await h.Db.PersonScopes.ToListAsync());
    }

    [Fact]
    public async Task AKey_IsNotAPersonToGiveAScopeTo()
    {
        var h = await NewAsync();
        var key = h.Actors.AddKey("Runner");

        Assert.IsType<NotFoundResult>((await h.Scopes.PutScope(key.Id, new PersonScopeWriteRequest("none", null), default)).Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("most")]
    public async Task AnUnknownOrMissingMode_IsRefused_AndNothingIsWritten(string? mode)
    {
        var h = await NewAsync();

        var result = await h.Scopes.PutScope(h.Ada.Id, new PersonScopeWriteRequest(mode, h.ProjectIds), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await h.Db.PersonScopes.ToListAsync());
        Assert.Empty(await h.Db.PersonScopeProjects.ToListAsync());
    }

    [Fact]
    public async Task SomeWithNoProjectIds_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Scopes.PutScope(h.Ada.Id, new PersonScopeWriteRequest("some", null), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await h.Db.PersonScopes.ToListAsync());
    }

    [Fact]
    public async Task AnUnknownProject_IsRefusedNamingIt_AndTheEarlierScopeStands()
    {
        var h = await NewAsync();
        await h.Scopes.PutScope(h.Ada.Id, new PersonScopeWriteRequest("some", [h.ProjectIds[0]]), default);

        var result = await h.Scopes.PutScope(h.Ada.Id, new PersonScopeWriteRequest("some", [h.ProjectIds[1], 9999]), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("9999", (string)refusal.Value!);
        Assert.Equal([h.ProjectIds[0]], Value(await h.Scopes.GetScope(h.Ada.Id, default)).ProjectIds);
    }

    [Fact]
    public async Task AWrite_RecordsWhoSetItAndWhen_AndLogsWhoSetWhoseScopeToWhat()
    {
        var h = await NewAsync();

        await h.Scopes.PutScope(h.Ada.Id, new PersonScopeWriteRequest("some", [h.ProjectIds[0]]), default);

        var row = await h.Db.PersonScopes.SingleAsync();
        Assert.Equal("Root", row.UpdatedBy);
        Assert.Equal(Now, row.UpdatedAt);

        var line = Assert.Single(h.Log.Lines);
        Assert.Contains(h.Ada.Id.ToString(), line);
        Assert.Contains("some", line);
        Assert.Contains("Root", line);
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required ScopesController Scopes { get; init; }
        public required HatchContext Db { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required Actor Admin { get; init; }
        public required Actor Ada { get; init; }
        public required List<int> ProjectIds { get; init; }
        public required CapturingLogger Log { get; init; }
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var projects = new[] { "AAA", "BBB", "CCC" }.Select(k => new EfHatchProject { Key = k, Name = k, CreatedAt = Now }).ToList();
        db.Projects.AddRange(projects);
        await db.SaveChangesAsync();

        var actors = new StubActorDirectory();
        var admin = actors.AddPerson("Root");
        var ada = actors.AddPerson("Ada");
        var log = new CapturingLogger();

        return new Harness
        {
            Scopes = new ScopesController(db, actors, new StubCaller("Root"), new FakeTimeProvider(Now), log)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            },
            Db = db,
            Actors = actors,
            Admin = admin,
            Ada = ada,
            ProjectIds = projects.Select(p => p.Id).Order().ToList(),
            Log = log,
        };
    }

    private sealed class CapturingLogger : ILogger<ScopesController>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    private sealed class StubCaller(string name) : ICallerIdentity
    {
        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);
        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);
        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult<EfPerson?>(null);
        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult<EfApiKey?>(null);
        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult<Actor?>(null);
        public Task<bool> IsProgramAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<string> ActorNameAsync(CancellationToken ct) => Task.FromResult(name);
    }

    /// <summary>A record compares its list by reference, so compare the parts.</summary>
    private static void AssertScope(PersonScopeDto expected, PersonScopeDto actual)
    {
        Assert.Equal(expected.PersonId, actual.PersonId);
        Assert.Equal(expected.Mode, actual.Mode);
        Assert.Equal(expected.ProjectIds, actual.ProjectIds);
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name}");
}

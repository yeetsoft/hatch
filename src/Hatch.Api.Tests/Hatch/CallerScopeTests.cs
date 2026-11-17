using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What the server says a caller may see of the projects, rule by rule in the
/// order <see cref="CallerScope"/> reads them.
/// </summary>
public class CallerScopeTests
{
    [Fact]
    public async Task NoHttpContext_SeesEverything_EvenWithANoneRow()
    {
        var h = await NewAsync(httpContext: false);
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Principal = ada;
        await h.SetScopeAsync(ada.Id, PersonScopeMode.None);

        Assert.True((await h.Scope.ResolveAsync(default)).Unbounded);
    }

    [Fact]
    public async Task AWallThatIsOff_SeesEverything_EvenWithAPersonEnrolledOnANoneRow()
    {
        var h = await NewAsync(wall: false);
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Principal = ada;
        await h.SetScopeAsync(ada.Id, PersonScopeMode.None);

        Assert.True((await h.Scope.ResolveAsync(default)).Unbounded);
    }

    [Fact]
    public async Task APersonWithNoRow_SeesEverything()
    {
        var h = await NewAsync();
        h.Actors.Principal = h.Actors.AddPerson("Ada");

        Assert.True((await h.Scope.ResolveAsync(default)).Unbounded);
    }

    [Fact]
    public async Task APersonOnAll_SeesEverything()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Principal = ada;
        await h.SetScopeAsync(ada.Id, PersonScopeMode.All);

        Assert.True((await h.Scope.ResolveAsync(default)).Unbounded);
    }

    [Fact]
    public async Task APersonOnNone_SeesNothing()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Principal = ada;
        await h.SetScopeAsync(ada.Id, PersonScopeMode.None);

        var seen = await h.Scope.ResolveAsync(default);

        Assert.False(seen.Unbounded);
        Assert.Empty(seen.ProjectIds);
        Assert.False(seen.Sees(h.ProjectIds[0]));
    }

    [Fact]
    public async Task APersonOnSome_SeesExactlyTheChosenProjects()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Principal = ada;
        await h.SetScopeAsync(ada.Id, PersonScopeMode.Some, h.ProjectIds[0], h.ProjectIds[2]);

        var seen = await h.Scope.ResolveAsync(default);

        Assert.False(seen.Unbounded);
        Assert.Equal([h.ProjectIds[0], h.ProjectIds[2]], seen.ProjectIds.Order());
        Assert.True(seen.Sees(h.ProjectIds[0]));
        Assert.False(seen.Sees(h.ProjectIds[1]));
    }

    [Fact]
    public async Task ADeletedProject_IsDroppedFromASome_EvenWhereTheRowIsLeftBehind()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Principal = ada;
        await h.SetScopeAsync(ada.Id, PersonScopeMode.Some, h.ProjectIds[0], h.ProjectIds[1]);

        // Removed without the context's cascade: the in-memory provider only
        // cascades rows it is tracking, and the read must not depend on it.
        var doomed = await h.Db.Projects.FindAsync(h.ProjectIds[1]);
        h.Db.ChangeTracker.Clear();
        h.Db.Projects.Remove(doomed!);
        await h.Db.SaveChangesAsync();
        Assert.Equal(2, await h.Db.PersonScopeProjects.CountAsync());

        Assert.Equal([h.ProjectIds[0]], (await h.NewScope().ResolveAsync(default)).ProjectIds);
    }

    [Fact]
    public async Task AKey_ReadsItsLiveOwnersRow()
    {
        var h = await NewAsync();
        var owner = h.Actors.AddPerson("Ada");
        h.Actors.Me = h.Actors.AddKey("Runner");
        h.Actors.Principal = owner;
        await h.SetScopeAsync(owner.Id, PersonScopeMode.Some, h.ProjectIds[1]);

        Assert.Equal([h.ProjectIds[1]], (await h.Scope.ResolveAsync(default)).ProjectIds);
    }

    [Fact]
    public async Task AKeyWhoseOwnerWasDeleted_SeesEverything_WhateverRowIsLeftBehind()
    {
        var h = await NewAsync();
        var owner = h.Actors.AddPerson("Ada");
        await h.SetScopeAsync(owner.Id, PersonScopeMode.None);
        h.Actors.Live.Remove(owner);
        h.Actors.Me = h.Actors.AddKey("Runner");
        h.Actors.Principal = null;

        Assert.True((await h.Scope.ResolveAsync(default)).Unbounded);
    }

    [Fact]
    public async Task AKeyWithNoOwner_SeesEverything()
    {
        var h = await NewAsync();
        h.Actors.Me = h.Actors.AddKey("Runner");
        h.Actors.Principal = null;

        Assert.True((await h.Scope.ResolveAsync(default)).Unbounded);
    }

    private static async Task<Harness> NewAsync(bool httpContext = true, bool wall = true)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var now = DateTimeOffset.UnixEpoch;
        var projects = new[] { "AAA", "BBB", "CCC" }.Select(k => new EfHatchProject { Key = k, Name = k, CreatedAt = now }).ToList();
        db.Projects.AddRange(projects);
        await db.SaveChangesAsync();

        var h = new Harness
        {
            Db = db,
            Actors = new StubActorDirectory(),
            ProjectIds = projects.Select(p => p.Id).ToList(),
            Accessor = new FixedAccessor(httpContext ? new DefaultHttpContext() : null),
            Auth = Options.Create(new AuthOptions { Enabled = wall }),
        };
        return h;
    }

    /// <summary>
    /// The real accessor holds its context in an AsyncLocal, which a value set
    /// inside an async helper loses on the way back to the test.
    /// </summary>
    private sealed class FixedAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required List<int> ProjectIds { get; init; }
        public required IHttpContextAccessor Accessor { get; init; }
        public required IOptions<AuthOptions> Auth { get; init; }

        public ICallerScope Scope => field ??= NewScope();

        /// <summary>A fresh resolver, for a test that changes the data after the first answer.</summary>
        public ICallerScope NewScope() => new CallerScope(Db, Actors, Accessor, Auth);

        public async Task SetScopeAsync(Guid personId, string mode, params int[] projectIds)
        {
            Db.PersonScopes.Add(new EfHatchPersonScope
            {
                PersonId = personId, Mode = mode, UpdatedBy = "test", UpdatedAt = DateTimeOffset.UnixEpoch,
            });
            Db.PersonScopeProjects.AddRange(projectIds.Select(id => new EfHatchPersonScopeProject { PersonId = personId, ProjectId = id }));
            await Db.SaveChangesAsync();
        }
    }
}

using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Whether a named person sees a project - the rule <see cref="CallerScope"/>
/// applies to the caller, asked of anyone. <see cref="CallerScopeTests"/>
/// proves the same answers from the caller's side.
/// </summary>
public class PersonScopeTests
{
    private static readonly IOptions<AuthOptions> WallOn = Options.Create(new AuthOptions { Enabled = true });
    private static readonly IOptions<AuthOptions> WallOff = Options.Create(new AuthOptions { Enabled = false });

    [Fact]
    public async Task APersonWithNoRow_SeesEveryProject()
    {
        var h = await NewAsync();

        Assert.True((await PersonScope.ReadAsync(h.Db, Guid.NewGuid(), default)).Unbounded);
        Assert.True(await PersonScope.SeesAsync(h.Db, WallOn, Guid.NewGuid(), h.ProjectIds[0], default));
    }

    [Fact]
    public async Task APersonOnAll_SeesEveryProject()
    {
        var h = await NewAsync();
        var ada = Guid.NewGuid();
        await h.SetScopeAsync(ada, PersonScopeMode.All);

        Assert.True(await PersonScope.SeesAsync(h.Db, WallOn, ada, h.ProjectIds[1], default));
    }

    [Fact]
    public async Task APersonOnNone_SeesNothing()
    {
        var h = await NewAsync();
        var ada = Guid.NewGuid();
        await h.SetScopeAsync(ada, PersonScopeMode.None);

        Assert.False(await PersonScope.SeesAsync(h.Db, WallOn, ada, h.ProjectIds[0], default));
    }

    [Fact]
    public async Task APersonOnSome_SeesExactlyTheProjectsNamed()
    {
        var h = await NewAsync();
        var ada = Guid.NewGuid();
        await h.SetScopeAsync(ada, PersonScopeMode.Some, h.ProjectIds[0], h.ProjectIds[2]);

        Assert.True(await PersonScope.SeesAsync(h.Db, WallOn, ada, h.ProjectIds[0], default));
        Assert.False(await PersonScope.SeesAsync(h.Db, WallOn, ada, h.ProjectIds[1], default));
        Assert.True(await PersonScope.SeesAsync(h.Db, WallOn, ada, h.ProjectIds[2], default));
    }

    [Fact]
    public async Task AWallThatIsOff_LetsEveryPersonSeeEverything_WhateverTheirRow()
    {
        var h = await NewAsync();
        var ada = Guid.NewGuid();
        await h.SetScopeAsync(ada, PersonScopeMode.None);

        Assert.True(await PersonScope.SeesAsync(h.Db, WallOff, ada, h.ProjectIds[0], default));
    }

    [Fact]
    public async Task ASomeRowNamingADeletedProject_DropsIt_WhereTheRowIsLeftBehind()
    {
        var h = await NewAsync();
        var ada = Guid.NewGuid();
        await h.SetScopeAsync(ada, PersonScopeMode.Some, h.ProjectIds[0], h.ProjectIds[1]);

        var doomed = await h.Db.Projects.FindAsync(h.ProjectIds[1]);
        h.Db.ChangeTracker.Clear();
        h.Db.Projects.Remove(doomed!);
        await h.Db.SaveChangesAsync();
        Assert.Equal(2, await h.Db.PersonScopeProjects.CountAsync());

        var seen = await PersonScope.ReadAsync(h.Db, ada, default);

        Assert.Equal([h.ProjectIds[0]], seen.ProjectIds);
        Assert.False(await PersonScope.SeesAsync(h.Db, WallOn, ada, h.ProjectIds[1], default));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(PersonScopeMode.All, true)]
    [InlineData(PersonScopeMode.None, false)]
    [InlineData(PersonScopeMode.Some, false)]
    public void TheRuleOnRowsAlreadyRead_AnswersWithoutTheDatabase(string? mode, bool sees) =>
        Assert.Equal(sees, PersonScope.Sees(mode, new HashSet<int> { 1 }, 2));

    [Fact]
    public void TheRuleOnRowsAlreadyRead_SomeSeesTheProjectsNamed() =>
        Assert.True(PersonScope.Sees(PersonScopeMode.Some, new HashSet<int> { 1, 2 }, 2));

    [Fact]
    public async Task TheCallersAnswer_IsThePersonsAnswer()
    {
        var h = await NewAsync();
        var actors = new StubActorDirectory();
        var ada = actors.AddPerson("Ada");
        actors.Principal = ada;
        await h.SetScopeAsync(ada.Id, PersonScopeMode.Some, h.ProjectIds[2]);
        var caller = new CallerScope(
            h.Db, actors, new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }, WallOn);

        var viaCaller = await caller.ResolveAsync(default);
        var viaPerson = await PersonScope.ReadAsync(h.Db, ada.Id, default);

        Assert.Equal(viaPerson.Unbounded, viaCaller.Unbounded);
        Assert.Equal(viaPerson.ProjectIds.Order(), viaCaller.ProjectIds.Order());
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var projects = new[] { "AAA", "BBB", "CCC" }
            .Select(k => new EfHatchProject { Key = k, Name = k, CreatedAt = DateTimeOffset.UnixEpoch }).ToList();
        db.Projects.AddRange(projects);
        await db.SaveChangesAsync();
        return new Harness(db, projects.Select(p => p.Id).ToList());
    }

    private sealed class Harness(HatchContext db, List<int> projectIds)
    {
        public HatchContext Db { get; } = db;
        public List<int> ProjectIds { get; } = projectIds;

        public async Task SetScopeAsync(Guid personId, string mode, params int[] projects)
        {
            Db.PersonScopes.Add(new EfHatchPersonScope
            {
                PersonId = personId, Mode = mode, UpdatedBy = "test", UpdatedAt = DateTimeOffset.UnixEpoch,
            });
            Db.PersonScopeProjects.AddRange(projects.Select(id => new EfHatchPersonScopeProject { PersonId = personId, ProjectId = id }));
            await Db.SaveChangesAsync();
        }
    }
}

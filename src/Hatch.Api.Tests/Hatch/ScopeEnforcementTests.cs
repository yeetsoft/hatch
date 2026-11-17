using Hatch.Api.Controllers;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What a caller of each scope reads out of <see cref="HatchContext"/>, table by
/// table, on the in-memory provider. The same expectations run against a real
/// Postgres in <see cref="ScopeEnforcementDatabaseTests"/>.
/// </summary>
public class ScopeEnforcementTests
{
    [Fact]
    public async Task ASomeCaller_ReadsOnlyItsProjectsRows_InEveryFilteredTable()
    {
        var h = await NewAsync();
        await using var db = ScopeSeed.Request(h.Options, ScopeSeed.Only(h.Seed.ProjectA));

        foreach (var probe in ScopeSeed.Probes)
            Assert.True(
                await probe.Count(db) == probe.OfOneProject,
                $"{probe.Name}: a caller scoped to one project should count {probe.OfOneProject}");
    }

    [Fact]
    public async Task ANoneCaller_ReadsNothingProjectOwned_InEveryFilteredTable()
    {
        var h = await NewAsync();
        await using var db = ScopeSeed.Request(h.Options, VisibleProjects.Nothing);

        foreach (var probe in ScopeSeed.Probes)
            Assert.True(
                await probe.Count(db) == probe.OfNone,
                $"{probe.Name}: a caller scoped to nothing should count {probe.OfNone}");
    }

    [Fact]
    public async Task ASomeCallerWithBothProjects_ReadsWhatAnUnboundedCallerDoes()
    {
        var h = await NewAsync();
        await using var db = ScopeSeed.Request(h.Options, ScopeSeed.Only(h.Seed.ProjectA, h.Seed.ProjectB));

        foreach (var probe in ScopeSeed.Probes)
            Assert.True(await probe.Count(db) == probe.OfBoth, $"{probe.Name}: both projects should count {probe.OfBoth}");
    }

    [Fact]
    public async Task AnAllCaller_ReadsEverything()
    {
        var h = await NewAsync();
        await using var db = ScopeSeed.Request(h.Options, VisibleProjects.Everything);

        foreach (var probe in ScopeSeed.Probes)
            Assert.True(await probe.Count(db) == probe.All, $"{probe.Name}: an unbounded caller should count {probe.All}");
    }

    [Fact]
    public async Task AContextBuiltWithNoScope_ReadsEverything()
    {
        var h = await NewAsync();
        await using var db = new HatchContext(h.Options);

        foreach (var probe in ScopeSeed.Probes)
            Assert.True(await probe.Count(db) == probe.All, $"{probe.Name}: a context with no scope should count {probe.All}");
    }

    [Fact]
    public async Task AJob_WithARequestHolderAndNoHttpContext_ReadsEverything()
    {
        var h = await NewAsync();
        await using var db = new HatchContext(h.Options, new RequestScope(new ScopeSeed.FixedAccessor(null)));

        foreach (var probe in ScopeSeed.Probes)
            Assert.True(await probe.Count(db) == probe.All, $"{probe.Name}: a job should count {probe.All}");
    }

    // ---- Failing loudly ----

    [Fact]
    public async Task ARequestWithNoResolvedScope_ThrowsOnTheFirstFilteredQuery()
    {
        var h = await NewAsync();
        await using var db = ScopeSeed.Request(h.Options, visible: null);

        foreach (var probe in ScopeSeed.Probes)
        {
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => probe.Count(db));
            Assert.Contains("scope was resolved", thrown.Message);
        }
    }

    [Fact]
    public async Task ATableWithNoFilter_IsNotGuarded()
    {
        var h = await NewAsync();
        await using var db = ScopeSeed.Request(h.Options, visible: null);

        Assert.Equal(2, await db.Statuses.CountAsync());
    }

    // ---- A hidden project does not exist ----

    [Fact]
    public async Task AHiddenProject_ReadsAsOneThatDoesNotExist()
    {
        var h = await NewAsync();
        await using var db = ScopeSeed.Request(h.Options, ScopeSeed.Only(h.Seed.ProjectA));

        Assert.Null(await db.Projects.FirstOrDefaultAsync(p => p.Id == h.Seed.ProjectB));
        Assert.Null(await db.Projects.FirstOrDefaultAsync(p => p.Key == "BBB"));
        Assert.Null(await db.Issues.FirstOrDefaultAsync(i => i.Id == h.Seed.IssueB));
        Assert.Null(await db.Issues.FindAsync(h.Seed.IssueB));
        Assert.Equal(0, await db.Issues.CountAsync(i => i.ProjectId == h.Seed.ProjectB));
    }

    // ---- Reaching a hidden row through something visible ----

    [Fact]
    public async Task AnIncludeAcrossAFilteredNavigation_DoesNotResurrectAHiddenRow()
    {
        var h = await NewAsync();

        // A's first issue waits on B's, and a dream of B's is threaded to A's.
        await using (var seed = new HatchContext(h.Options))
        {
            seed.Dependencies.Add(new EfHatchIssueDependency
            {
                IssueId = h.Seed.IssueA, DependsOnId = h.Seed.IssueB, CreatedBy = "test", CreatedAt = DateTimeOffset.UnixEpoch,
            });
            seed.DreamThreads.Add(new EfHatchDreamThread
            {
                DreamId = h.Seed.DreamB, IssueId = h.Seed.IssueA, Actor = "test", At = DateTimeOffset.UnixEpoch,
            });
            await seed.SaveChangesAsync();
        }

        await using var db = ScopeSeed.Request(h.Options, ScopeSeed.Only(h.Seed.ProjectA));

        // The far end of each is required, so a row whose far end is hidden is
        // dropped from an Include rather than returned with a hole in it.
        Assert.Empty(await db.Dependencies.Include(d => d.DependsOn).Where(d => d.DependsOnId == h.Seed.IssueB).ToListAsync());
        Assert.Empty(await db.DreamThreads.Include(t => t.Dream).Where(t => t.DreamId == h.Seed.DreamB).ToListAsync());

        // Without the Include the row is the caller's own, and carries an id and
        // nothing of the hidden row.
        var edge = await db.Dependencies.SingleAsync(d => d.DependsOnId == h.Seed.IssueB);
        Assert.Null(edge.DependsOn);

        var issues = await db.Issues.Include(i => i.Project).Include(i => i.Comments).Include(i => i.Events).ToListAsync();
        Assert.All(issues, i => Assert.Equal(h.Seed.ProjectA, i.ProjectId));
        Assert.All(issues, i => Assert.NotNull(i.Project));

        var comments = await db.Comments.Include(c => c.Issue).ToListAsync();
        Assert.All(comments, c => Assert.Equal(h.Seed.ProjectA, c.Issue!.ProjectId));
    }

    [Fact]
    public async Task AProjectsIssues_AreNotLoadedByItsNavigation_ForACallerWhoCannotSeeThem()
    {
        var h = await NewAsync();
        await using var db = ScopeSeed.Request(h.Options, VisibleProjects.Nothing);

        Assert.Empty(await db.Projects.Include(p => p.Issues).ToListAsync());
    }

    // ---- The opt-outs read across projects, and return no content ----

    [Fact]
    public async Task TheDependencyGate_StillCountsABlockerInAHiddenProject()
    {
        var h = await NewAsync();
        await using (var seed = new HatchContext(h.Options))
        {
            seed.Dependencies.Add(new EfHatchIssueDependency
            {
                IssueId = h.Seed.IssueA, DependsOnId = h.Seed.IssueB, CreatedBy = "test", CreatedAt = DateTimeOffset.UnixEpoch,
            });
            await seed.SaveChangesAsync();
        }

        await using var db = ScopeSeed.Request(h.Options, ScopeSeed.Only(h.Seed.ProjectA));
        var gate = await DependencyGate.ForAsync(db, await db.Statuses.ToListAsync(), default);

        // A's own edge, and the one to B's issue, which still blocks.
        Assert.Equal(2, gate.Unmet(h.Seed.IssueA).Count);
        Assert.Contains(gate.Unmet(h.Seed.IssueA), e => e.BlockerKey == "BBB-1");
    }

    [Fact]
    public async Task RankPlacement_SeesTheWholeColumn_ForACallerWhoCannotSeeAllOfIt()
    {
        var h = await NewAsync();
        var todo = (await new HatchContext(h.Options).Statuses.SingleAsync(s => s.Name == "todo")).Id;

        await using var db = ScopeSeed.Request(h.Options, VisibleProjects.Nothing);
        var rank = await new RankService(db).BottomAsync(todo, default);

        // Past every card in the column, the hidden ones' included: the highest
        // rank seeded is 2000.
        Assert.True(rank > 2000, $"the bottom of the column is past 2000, not {rank}");
    }

    [Fact]
    public async Task TheRepositoryWinner_IsTheSameProject_ForEveryCaller()
    {
        var h = await NewAsync();

        foreach (var visible in new[] { VisibleProjects.Nothing, ScopeSeed.Only(h.Seed.ProjectB), VisibleProjects.Everything })
        {
            await using var db = ScopeSeed.Request(h.Options, visible);
            var winner = await ProjectRepositoryBinding.WinnerAsync(db, ScopeSeed.CanonicalShared, default);

            Assert.Equal((h.Seed.ProjectA, "AAA"), winner);
        }
    }

    [Fact]
    public async Task TheChangelogFile_IsTheSameEntries_ForEveryCaller()
    {
        var h = await NewAsync();
        await using (var seed = new HatchContext(h.Options))
        {
            var done = await seed.Statuses.SingleAsync(s => s.Name == "done");
            foreach (var repository in await seed.ProjectRepositories.Where(r => r.Canonical == ScopeSeed.CanonicalShared).ToListAsync())
                repository.Changelog = true;
            foreach (var issue in await seed.Issues.ToListAsync())
            {
                issue.StatusId = done.Id;
                issue.ClosedAt = DateTimeOffset.UnixEpoch;
            }

            await seed.SaveChangesAsync();
        }

        List<string> Keys(List<Changelog.Entry>? entries) => entries!.Select(e => e.Key).Order().ToList();

        await using var all = ScopeSeed.Request(h.Options, VisibleProjects.Everything);
        var expected = Keys(await ChangelogFiles.ProjectAsync(all, ScopeSeed.CanonicalShared, default));

        // The first issue of each project carries a changelog task, so it is
        // not an entry: what is left is the second of each.
        Assert.Equal(["AAA-2", "BBB-2"], expected);

        foreach (var visible in new[] { VisibleProjects.Nothing, ScopeSeed.Only(h.Seed.ProjectA) })
        {
            await using var db = ScopeSeed.Request(h.Options, visible);
            Assert.Equal(expected, Keys(await ChangelogFiles.ProjectAsync(db, ScopeSeed.CanonicalShared, default)));
        }
    }

    // ---- Resolving, with the same context the filters guard ----

    [Fact]
    public async Task TheResolver_ReadsThroughTheGuardedContext_AndPutsTheAnswerOnTheRequest()
    {
        var h = await NewAsync();
        var actors = new StubActorDirectory();
        var ada = actors.AddPerson("Ada");
        actors.Principal = ada;

        await using (var seed = new HatchContext(h.Options))
        {
            seed.PersonScopes.Add(new EfHatchPersonScope
            {
                PersonId = ada.Id, Mode = PersonScopeMode.Some, UpdatedBy = "test", UpdatedAt = DateTimeOffset.UnixEpoch,
            });
            seed.PersonScopeProjects.Add(new EfHatchPersonScopeProject { PersonId = ada.Id, ProjectId = h.Seed.ProjectA });
            await seed.SaveChangesAsync();
        }

        var accessor = new ScopeSeed.FixedAccessor(new DefaultHttpContext());
        var request = new RequestScope(accessor);
        await using var db = new HatchContext(h.Options, request);
        var scope = new CallerScope(db, actors, accessor, Options.Create(new AuthOptions { Enabled = true }), request);

        // Unresolved, the context refuses; the resolver's own reads still work.
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Projects.CountAsync());

        var visible = await scope.ResolveAsync(default);

        Assert.False(visible.Unbounded);
        Assert.Equal([h.Seed.ProjectA], visible.ProjectIds.ToArray());
        Assert.False(request.Resolving);
        Assert.Same(visible, request.Current);
        Assert.Equal(1, await db.Projects.CountAsync());
    }

    [Fact]
    public async Task AResolverThatFails_LeavesTheRequestUnresolved_SoTheDataStaysClosed()
    {
        var h = await NewAsync();
        var accessor = new ScopeSeed.FixedAccessor(new DefaultHttpContext());
        var request = new RequestScope(accessor);
        await using var db = new HatchContext(h.Options, request);
        var scope = new CallerScope(db, new ThrowingActors(), accessor, Options.Create(new AuthOptions { Enabled = true }), request);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ResolveAsync(default));

        Assert.False(request.Resolving);
        Assert.Null(request.Current);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Issues.CountAsync());
    }

    private sealed class ThrowingActors : IActorDirectory
    {
        public Task<IReadOnlyList<Actor>> LiveAsync(CancellationToken ct) => throw new InvalidOperationException("boom");

        public Task<Actor?> ResolveAsync(string? kind, Guid? id, CancellationToken ct) => throw new InvalidOperationException("boom");

        public Task<Actor?> MeAsync(CancellationToken ct) => throw new InvalidOperationException("boom");

        public Task<Actor?> PrincipalAsync(CancellationToken ct) => throw new InvalidOperationException("boom");
    }

    // ---- The filter ----

    [Fact]
    public async Task TheFilter_ResolvesTheScope_ForAHatchController()
    {
        var scope = new CountingScope();

        await RunFilterAsync(scope, typeof(BoardController));

        Assert.Equal(1, scope.Calls);
    }

    [Fact]
    public async Task TheFilter_LeavesACoreController_Alone()
    {
        var scope = new CountingScope();

        await RunFilterAsync(scope, typeof(AuthController));

        Assert.Equal(0, scope.Calls);
    }

    [Fact]
    public async Task TheFilter_RunsTheActionAfterResolving()
    {
        var order = new List<string>();
        var scope = new CountingScope { OnResolve = () => order.Add("resolve") };

        await RunFilterAsync(scope, typeof(BoardController), () => order.Add("action"));

        Assert.Equal(["resolve", "action"], order);
    }

    [Fact]
    public void TheHatchModule_RegistersTheFilterForEveryController()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Hatch"] = "Host=unused" })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddHatchModule(configuration);

        using var provider = services.BuildServiceProvider();
        var filters = provider.GetRequiredService<IOptions<MvcOptions>>().Value.Filters;

        Assert.Contains(
            filters.OfType<TypeFilterAttribute>(),
            f => f.ImplementationType == typeof(ScopeResolutionFilter));
    }

    [Fact]
    public async Task TheContext_AndTheResolver_ShareOneHolder_WithinAServiceScope()
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContext<HatchContext>(o => o.UseInMemoryDatabase(name));
        services.AddScoped<RequestScope>();
        services.AddSingleton<IHttpContextAccessor>(new ScopeSeed.FixedAccessor(new DefaultHttpContext()));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var request = scope.ServiceProvider.GetRequiredService<RequestScope>();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();

        // The request has an HTTP context and no scope yet, so the context the
        // container built must be the guarded one and not the open one.
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Issues.CountAsync());

        request.Current = VisibleProjects.Nothing;
        Assert.Equal(0, await db.Issues.CountAsync());
    }

    private static async Task RunFilterAsync(ICallerScope scope, Type controller, Action? action = null)
    {
        var services = new ServiceCollection().AddSingleton(scope).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var descriptor = new ControllerActionDescriptor { ControllerTypeInfo = controller.GetTypeInfo() };
        var actionContext = new ActionContext(http, new RouteData(), descriptor);
        var executing = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller);

        await new ScopeResolutionFilter().OnActionExecutionAsync(executing, () =>
        {
            action?.Invoke();
            return Task.FromResult(new ActionExecutedContext(actionContext, [], controller));
        });
    }

    private sealed class CountingScope : ICallerScope
    {
        public int Calls { get; private set; }
        public Action? OnResolve { get; init; }

        public Task<VisibleProjects> ResolveAsync(CancellationToken ct)
        {
            Calls++;
            OnResolve?.Invoke();
            return Task.FromResult(VisibleProjects.Everything);
        }
    }

    // ---- Harness ----

    private sealed record Harness(DbContextOptions<HatchContext> Options, ScopeSeed Seed);

    private static async Task<Harness> NewAsync()
    {
        var options = new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new HatchContext(options);
        return new Harness(options, await ScopeSeed.SeedAsync(db));
    }
}

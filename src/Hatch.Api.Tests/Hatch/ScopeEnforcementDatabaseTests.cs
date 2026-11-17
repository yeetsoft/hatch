using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The filters against the database that ships. The in-memory provider proves
/// what a filter means; only Postgres proves it translates, and only Postgres
/// takes the bulk writes, which are where a filter that did not apply would
/// quietly rewrite a hidden project's rows.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ScopeEnforcementDatabaseTests
{
    [SkippableFact]
    public async Task EveryFilterShape_TranslatesToSql_AndCountsWhatTheMemoryLaneDoes()
    {
        var h = await NewAsync();

        foreach (var (name, visible, expected) in new (string, VisibleProjects, Func<ScopeSeed.Probe, int>)[]
        {
            ("one project", ScopeSeed.Only(h.Seed.ProjectA), p => p.OfOneProject),
            ("none", VisibleProjects.Nothing, p => p.OfNone),
            ("everything", VisibleProjects.Everything, p => p.All),
        })
        {
            await using var db = ScopeSeed.Request(h.Options, visible);

            foreach (var probe in ScopeSeed.Probes)
                Assert.True(
                    await probe.Count(db) == expected(probe),
                    $"{probe.Name} for {name}: expected {expected(probe)}");
        }
    }

    [SkippableFact]
    public async Task TheFilterIsInTheSql_AsAParameter_NotBakedIntoTheCachedQuery()
    {
        var h = await NewAsync();

        await using var one = ScopeSeed.Request(h.Options, ScopeSeed.Only(h.Seed.ProjectA));
        await using var all = ScopeSeed.Request(h.Options, VisibleProjects.Everything);

        // The same query shape, run by two callers one after the other: the
        // compiled query is cached, and the answer still follows the caller.
        Assert.Equal(2, await one.Issues.CountAsync());
        Assert.Equal(4, await all.Issues.CountAsync());
        Assert.Equal(2, await one.Issues.CountAsync());

        var sql = one.Issues.ToQueryString();
        Assert.Contains("ProjectId", sql);
        Assert.Contains("ANY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task ABulkUpdate_TouchesNoRowTheCallerCannotSee()
    {
        var h = await NewAsync();

        await using (var db = ScopeSeed.Request(h.Options, ScopeSeed.Only(h.Seed.ProjectA)))
        {
            var touched = await db.Issues.ExecuteUpdateAsync(s => s.SetProperty(i => i.Title, "rewritten"));
            Assert.Equal(2, touched);

            var comments = await db.Comments.ExecuteUpdateAsync(s => s.SetProperty(c => c.Body, "rewritten"));
            Assert.Equal(1, comments);
        }

        await using var all = new HatchContext(h.Options);
        Assert.Equal(2, await all.Issues.CountAsync(i => i.Title == "rewritten"));
        Assert.Equal(2, await all.Issues.CountAsync(i => i.Title != "rewritten" && i.ProjectId == h.Seed.ProjectB));
        Assert.Equal(1, await all.Comments.CountAsync(c => c.Body == "rewritten"));
    }

    [SkippableFact]
    public async Task ABulkDelete_TouchesNoRowTheCallerCannotSee()
    {
        var h = await NewAsync();

        await using (var db = ScopeSeed.Request(h.Options, ScopeSeed.Only(h.Seed.ProjectA)))
        {
            Assert.Equal(1, await db.Comments.ExecuteDeleteAsync());
            Assert.Equal(1, await db.IssueEvents.ExecuteDeleteAsync());
            Assert.Equal(2, await db.TrunkBuilds.ExecuteDeleteAsync());
        }

        await using var all = new HatchContext(h.Options);
        Assert.Equal(1, await all.Comments.CountAsync());
        Assert.Equal(1, await all.IssueEvents.CountAsync());

        // B's own repository's build is left, and the one both bind is gone.
        Assert.Equal([ScopeSeed.CanonicalB], await all.TrunkBuilds.Select(t => t.Canonical).ToListAsync());
    }

    [SkippableFact]
    public async Task ABulkWrite_ByACallerScopedToNothing_TouchesNothing()
    {
        var h = await NewAsync();

        await using (var db = ScopeSeed.Request(h.Options, VisibleProjects.Nothing))
        {
            Assert.Equal(0, await db.Issues.ExecuteUpdateAsync(s => s.SetProperty(i => i.Title, "x")));
            Assert.Equal(0, await db.Comments.ExecuteDeleteAsync());
            Assert.Equal(0, await db.Projects.ExecuteDeleteAsync());
        }

        await using var all = new HatchContext(h.Options);
        Assert.Equal(2, await all.Projects.CountAsync());
        Assert.Equal(2, await all.Comments.CountAsync());
    }

    [SkippableFact]
    public async Task ARequestWithNoResolvedScope_ThrowsBeforeItReachesTheDatabase()
    {
        var h = await NewAsync();
        await using var db = ScopeSeed.Request(h.Options, visible: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Issues.ExecuteDeleteAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Issues.ToListAsync());

        await using var all = new HatchContext(h.Options);
        Assert.Equal(4, await all.Issues.CountAsync());
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task APutForARepositoryOnlyAHiddenProjectBinds_Answers200_WhereTheUniqueIndexWouldHaveFailedTheSave(bool none)
    {
        var h = await NewAsync();
        await using var db = ScopeSeed.Request(h.Options, none ? VisibleProjects.Nothing : ScopeSeed.Only(h.Seed.ProjectA));
        var caller = new MergeCheckControllerTests.StubCallerIdentity
        {
            Person = new EfPerson { Name = "Nathan", CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch },
        };
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var remote = "https://" + ScopeSeed.CanonicalB;

        var build = await new TrunkBuildController(db, caller, time)
            .PutTrunkBuild(new TrunkBuildRequest(remote, "main", "def", BuildVerdicts.Passed, null, "r"), default);
        var playbook = await new PlaybookFileController(db, caller, time)
            .PutPlaybookFile(new PlaybookFileRequest(remote, "main", "def", null, null, "r"), default);
        var dependency = await new DependencyFileController(db, caller, time, null)
            .PutDependencyFile(new DependencyFileRequest(remote, "main", "def", null, null, "r"), default);

        Assert.Null(Assert.IsType<TrunkBuildDto>(build.Value).ProjectKey);
        Assert.NotNull(playbook.Value);
        Assert.NotNull(dependency.Value);

        await using var all = new HatchContext(h.Options);
        Assert.Equal("abc", Assert.Single(await all.TrunkBuilds.Where(f => f.Canonical == ScopeSeed.CanonicalB).ToListAsync()).Sha);
        Assert.Equal("abc", Assert.Single(await all.PlaybookFiles.Where(f => f.Canonical == ScopeSeed.CanonicalB).ToListAsync()).Sha);
        Assert.Equal("abc", Assert.Single(await all.DependencyFiles.Where(f => f.Canonical == ScopeSeed.CanonicalB).ToListAsync()).Sha);
    }

    private sealed record Harness(DbContextOptions<HatchContext> Options, ScopeSeed Seed);

    private static async Task<Harness> NewAsync()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            $"{HatchDatabase.Variable} is unset - a bulk write and the SQL a filter becomes are Postgres's own, and "
            + "EF's in-memory provider has neither. Run `make test-api-db`, or point the variable at a scratch database.");

        var connectionString = await HatchDatabase.PrepareAsync();
        var options = new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options;

        await using var db = new HatchContext(options);
        return new Harness(options, await ScopeSeed.SeedAsync(db));
    }
}

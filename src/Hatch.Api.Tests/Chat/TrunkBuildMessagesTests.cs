using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Hatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// What a failing trunk says in a chat room: one message when it begins failing,
/// edited while the failure changes, edited to say passed, and a later failure a
/// new message. Everything runs through <see cref="TrunkBuildController.PutTrunkBuild"/>
/// as a runner does, and nothing calls Slack.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class TrunkBuildMessagesTests
{
    private static readonly DateTimeOffset Now = ChatOutboxTests.Now;

    private const string Remote = "git@forge.example:owner/repo.git";
    private const string Canonical = "forge.example/owner/repo";
    private const string Sha = "2222222222222222222222222222222222222222";
    private const string NextSha = "3333333333333333333333333333333333333333";

    [Fact]
    public async Task ABeginningFailure_QueuesOneRowForEachRoomThatCarriesItsProject()
    {
        var h = await NewAsync();
        var carrying = await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        await h.Seed.RoomAsync(h.Workspace, "C2", problems: false, changelog: true);
        var second = await h.Seed.RoomAsync(h.Workspace, "C3", problems: true);

        await h.PutAsync(Failed("b", "a"));

        var rows = await h.RowsAsync();
        Assert.Equal([carrying, second], rows.Select(r => r.RoomId).OrderBy(id => id));
        Assert.All(rows, r => Assert.Equal("problem", r.Kind));
        Assert.All(rows, r => Assert.Equal(1, r.Revision));

        var content = ChatMessageContent.Read(rows[0].Content)!;
        Assert.Equal($"Trunk build failing: {Canonical} main", content.Title);
        Assert.Equal(["Commit 2222222", "Since 2026-11-23 12:00 UTC"], content.Lines);
        Assert.Equal(["a", "b"], content.Links.Select(l => l.Label));
        Assert.Equal("https://forge.example/checks/a", content.Links[0].Url);
    }

    [Fact]
    public async Task ACheckWithNoLink_IsALinkWithNoAddress()
    {
        var h = await NewAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);

        await h.PutAsync(Failed("a") with { Failing = [new FailingCheckDto("a", null)] });

        var link = Assert.Single(ChatMessageContent.Read(Assert.Single(await h.RowsAsync()).Content)!.Links);
        Assert.Equal("a", link.Label);
        Assert.Null(link.Url);
    }

    [Fact]
    public async Task ARoomCarryingTwoBindingProjects_GetsOneRow()
    {
        var h = await NewAsync();
        var other = await h.Seed.ProjectAsync("OTH");
        await h.BindAsync(other);
        await h.Db.ChatWorkspaceProjects.AddAsync(new EfHatchChatWorkspaceProject { WorkspaceId = h.Workspace, ProjectId = other, CreatedAt = Now });
        await h.Db.SaveChangesAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);

        await h.PutAsync(Failed("a"));

        Assert.Single(await h.RowsAsync());
    }

    [Fact]
    public async Task ARoomCarryingNone_GetsNone()
    {
        var h = await NewAsync();
        var other = await h.Seed.ProjectAsync("OTH");
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true, allProjects: false, chosen: [other]);

        await h.PutAsync(Failed("a"));

        Assert.Empty(await h.RowsAsync());
    }

    [Fact]
    public async Task ARepositoryNoProjectBinds_IsSaidNowhere_ButTheVerdictIsKept()
    {
        var h = await NewAsync(bind: false);
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true, unownedProblems: true);

        await h.PutAsync(Failed("a"));

        Assert.Empty(await h.RowsAsync());
        Assert.Single(await h.Db.TrunkBuilds.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task ANewSha_EditsTheMessage_ButTheSameShaAndChecksDoNot()
    {
        var h = await NewAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        await h.PutAsync(Failed("a"));

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.PutAsync(Failed("a"));
        Assert.Equal(1, Assert.Single(await h.RowsAsync()).Revision);

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.PutAsync(Failed("a") with { Sha = NextSha });

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(2, row.Revision);
        var content = ChatMessageContent.Read(row.Content)!;
        Assert.Equal("Commit 3333333", content.Lines[0]);
        Assert.Equal("Since 2026-11-23 12:00 UTC", content.Lines[1]);
    }

    [Fact]
    public async Task ANewSetOfFailedChecks_EditsTheMessage()
    {
        var h = await NewAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        await h.PutAsync(Failed("a"));

        await h.PutAsync(Failed("a", "b"));

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(2, row.Revision);
        Assert.Equal(["a", "b"], ChatMessageContent.Read(row.Content)!.Links.Select(l => l.Label));
    }

    [Fact]
    public async Task APass_EditsTheMessageToPassed_AndEndsIt()
    {
        var h = await NewAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        await h.PutAsync(Failed("a"));

        h.Time.Advance(TimeSpan.FromMinutes(30));
        await h.PutAsync(Passed());

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(2, row.Revision);
        var content = ChatMessageContent.Read(row.Content)!;
        Assert.Equal($"Trunk build passing again: {Canonical} main", content.Title);
        Assert.Equal(["Failing since 2026-11-23 12:00 UTC", "Passed 2026-11-23 12:30 UTC"], content.Lines);
        Assert.Empty(content.Links);
        Assert.Null((await h.Db.TrunkBuilds.SingleAsync()).FailingSince);

        // A second pass has nothing left to end.
        await h.PutAsync(Passed());
        Assert.Equal(2, Assert.Single(await h.RowsAsync()).Revision);
    }

    [Fact]
    public async Task AFailureAfterAPass_IsANewMessage_UnderASecondSubject()
    {
        var h = await NewAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        await h.PutAsync(Failed("a"));
        h.Time.Advance(TimeSpan.FromMinutes(30));
        await h.PutAsync(Passed());

        h.Time.Advance(TimeSpan.FromMinutes(30));
        await h.PutAsync(Failed("a") with { Sha = NextSha });

        var rows = await h.RowsAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => r.Subject).Distinct().Count());
        Assert.Equal(["Trunk build failing: " + Canonical + " main", "Trunk build passing again: " + Canonical + " main"],
            rows.OrderByDescending(r => r.QueuedAt).Select(r => ChatMessageContent.Read(r.Content)!.Title));
        Assert.Equal(Now.AddHours(1), (await h.Db.TrunkBuilds.SingleAsync()).FailingSince);
    }

    [Fact]
    public async Task APendingVerdictWithAFailedCheck_Begins_AndOneWithNone_DoesNotEndIt()
    {
        var h = await NewAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);

        await h.PutAsync(Failed("a") with { Verdict = BuildVerdicts.Pending });
        Assert.Equal(1, Assert.Single(await h.RowsAsync()).Revision);

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.PutAsync(Passed() with { Verdict = BuildVerdicts.Pending, Failing = null });

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(1, row.Revision);
        Assert.Equal("Trunk build failing: " + Canonical + " main", ChatMessageContent.Read(row.Content)!.Title);
        Assert.Equal(Now, (await h.Db.TrunkBuilds.SingleAsync()).FailingSince);
    }

    [Fact]
    public async Task APendingVerdictWithNoFailedCheck_AndNoFailureBefore_SaysNothing()
    {
        var h = await NewAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);

        await h.PutAsync(Passed() with { Verdict = BuildVerdicts.Pending });
        await h.PutAsync(Passed());

        Assert.Empty(await h.RowsAsync());
    }

    [Fact]
    public async Task ARowAlreadyFailingBeforeThisExisted_IsAnnouncedOnItsNextWrite()
    {
        var h = await NewAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        h.Db.TrunkBuilds.Add(new EfHatchTrunkBuild
        {
            Remote = Remote, Canonical = Canonical, Trunk = "main", Sha = Sha, ShaSince = Now.AddDays(-1),
            Verdict = BuildVerdicts.Failed, Failing = EfHatchBuildCheck.WriteFailing([new FailingCheckDto("a", null)]),
            CheckedAt = Now.AddDays(-1), Runner = "box", CheckedBy = "Nathan",
        });
        await h.Db.SaveChangesAsync();

        await h.PutAsync(Failed("a"));

        Assert.Equal(1, Assert.Single(await h.RowsAsync()).Revision);
        Assert.Equal(Now, (await h.Db.TrunkBuilds.SingleAsync()).FailingSince);
    }

    [Fact]
    public async Task TwoWorkspaces_EachHearOnlyOfTheRepositoriesTheirProjectsBind()
    {
        var h = await NewAsync();
        var other = await h.Seed.ProjectAsync("OTH");
        var ours = h.Workspace;
        var theirs = await h.Seed.WorkspaceAsync("TOTHER", other);
        var ourRoom = await h.Seed.RoomAsync(ours, "C1", problems: true);
        await h.Seed.RoomAsync(theirs, "C1", problems: true);

        await h.PutAsync(Failed("a"));

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(ourRoom, row.RoomId);
        Assert.Equal(ours, row.WorkspaceId);
    }

    [Fact]
    public async Task AFailingWithoutTheMessages_QueuesNothing_AsATestThatOmitsThemExpects()
    {
        var h = await NewAsync(messages: false);
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);

        await h.PutAsync(Failed("a"));

        Assert.Empty(await h.RowsAsync());
        Assert.Null((await h.Db.TrunkBuilds.SingleAsync()).FailingSince);
    }

    [Fact]
    public async Task ARequestForAHiddenRow_QueuesNothing()
    {
        var options = new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        ScopeSeed seed;
        await using (var seeding = new HatchContext(options))
            seed = await ScopeSeed.SeedAsync(seeding);

        await using var db = ScopeSeed.Request(options, ScopeSeed.Only(seed.ProjectA));
        var chat = new ChatOutboxSeed(db, Now);
        var workspace = await chat.WorkspaceAsync("TACME", seed.ProjectA, seed.ProjectB);
        await chat.RoomAsync(workspace, "C1", problems: true);
        var time = new FakeTimeProvider(Now);
        var controller = new TrunkBuildController(
            db, new MergeCheckControllerTests.StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } },
            time, new TrunkBuildMessages(new ChatOutbox(db, time), db, time));

        var answer = await controller.PutTrunkBuild(Failed("a") with { Remote = "https://host/b" }, default);

        Assert.NotNull(answer.Value);
        await using var all = new HatchContext(options);
        Assert.Empty(await all.ChatMessages.IgnoreQueryFilters().ToListAsync());
        Assert.Null((await all.TrunkBuilds.SingleAsync(f => f.Canonical == ScopeSeed.CanonicalB)).FailingSince);
    }

    [SkippableFact]
    public async Task AFailingWrite_StoresNeitherTheVerdictNorTheRows()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset - the keys and cascades are the database's. Run `make test-api-db`.");
        var connection = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).Options);
        var h = await HarnessAsync(db, bind: true, messages: true);
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);

        // A row that breaks a key, in the same unit of work: its room does not exist.
        db.ChatRoomProjects.Add(new EfHatchChatRoomProject { RoomId = 1_000_000, WorkspaceId = h.Workspace, ProjectId = h.Project });
        await Assert.ThrowsAsync<DbUpdateException>(() => h.PutAsync(Failed("a")));

        await using var read = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).Options);
        Assert.Empty(await read.TrunkBuilds.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await read.ChatMessages.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await read.ChatSubjects.ToListAsync());
    }

    [SkippableFact]
    public async Task TheVerdictAndTheRows_LandInOneSave_OnPostgres_AndTheSubjectSurvivesTheRoundTrip()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset - the keys and cascades are the database's. Run `make test-api-db`.");
        var connection = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).Options);
        var h = await HarnessAsync(db, bind: true, messages: true);
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        h.Time.SetUtcNow(Now.AddTicks(1234567));

        await h.PutAsync(Failed("a"));

        // A new context reads the row back with the database's own precision.
        await using var later = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).Options);
        Assert.NotNull((await later.TrunkBuilds.SingleAsync()).FailingSince);
        var again = await HarnessAsync(later, bind: false, messages: true, seed: false);
        again.Time.SetUtcNow(Now.AddHours(1));
        await again.PutAsync(Passed());

        await using var read = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).Options);
        var row = Assert.Single(await read.ChatMessages.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(2, row.Revision);
        Assert.StartsWith("Trunk build passing again", ChatMessageContent.Read(row.Content)!.Title);
        Assert.Null((await read.TrunkBuilds.SingleAsync()).FailingSince);
    }

    [Fact]
    public void TheFailingRule_AgreesInMemoryAndAsAQuery_OnEveryVerdict()
    {
        foreach (var verdict in BuildVerdicts.All)
        foreach (var failing in new string?[] { null, "[{\"name\":\"a\"}]" })
        {
            var row = new EfHatchTrunkBuild
            {
                Remote = Remote, Canonical = Canonical, Trunk = "main", Sha = Sha, ShaSince = Now,
                Verdict = verdict, Failing = failing, CheckedAt = Now, Runner = "r", CheckedBy = "c",
            };

            var asQuery = new[] { row }.AsQueryable().Where(TrunkBuildFailing.Where).Any();

            Assert.Equal(TrunkBuildFailing.Is(verdict, failing), asQuery);
            Assert.Equal(verdict == BuildVerdicts.Failed || (verdict == BuildVerdicts.Pending && failing != null), asQuery);
        }
    }

    private static TrunkBuildRequest Passed() => new(Remote, "main", Sha, BuildVerdicts.Passed, null, "box:/work/repo");

    private static TrunkBuildRequest Failed(params string[] names) =>
        Passed() with
        {
            Verdict = BuildVerdicts.Failed,
            Failing = names.Select(n => new FailingCheckDto(n, $"https://forge.example/checks/{n}")).ToList(),
        };

    private static async Task<Harness> NewAsync(bool bind = true, bool messages = true) =>
        await HarnessAsync(
            new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
            bind, messages);

    private static async Task<Harness> HarnessAsync(HatchContext db, bool bind, bool messages, bool seed = true)
    {
        var chat = new ChatOutboxSeed(db, Now);
        var project = seed ? await chat.ProjectAsync("AER") : await db.Projects.IgnoreQueryFilters().Select(p => p.Id).FirstAsync();
        var workspace = seed ? await chat.WorkspaceAsync("TACME", project) : await db.ChatWorkspaces.Select(w => w.Id).FirstAsync();
        var time = new FakeTimeProvider(Now);
        var controller = new TrunkBuildController(
            db, new MergeCheckControllerTests.StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } },
            time, messages ? new TrunkBuildMessages(new ChatOutbox(db, time), db, time) : null);

        var h = new Harness { Db = db, Seed = chat, Build = controller, Time = time, Project = project, Workspace = workspace };
        if (bind) await h.BindAsync(project);
        return h;
    }

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required ChatOutboxSeed Seed { get; init; }
        public required TrunkBuildController Build { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int Project { get; init; }
        public required int Workspace { get; init; }

        public Task<ActionResult<TrunkBuildDto>> PutAsync(TrunkBuildRequest request) => Build.PutTrunkBuild(request, default);

        public async Task BindAsync(int project)
        {
            Db.ProjectRepositories.Add(new EfHatchProjectRepository
            {
                ProjectId = project, Remote = Canonical, Canonical = Canonical, SortOrder = project, CreatedAt = Now,
            });
            await Db.SaveChangesAsync();
        }

        public Task<List<EfHatchChatMessage>> RowsAsync() =>
            Db.ChatMessages.IgnoreQueryFilters().OrderBy(m => m.Id).ToListAsync();
    }
}

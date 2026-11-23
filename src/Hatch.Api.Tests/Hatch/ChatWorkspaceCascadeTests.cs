using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Hatch.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Removing a workspace is one delete, and what hangs off it goes with it in
/// the database, not in controller code. There is no such table yet; these keep
/// that true on the day one is added: a foreign key to a workspace that is not
/// a cascade fails the model audit, and on a real Postgres the same is read back
/// out of the schema.
/// </summary>
public class ChatWorkspaceCascadeModelTests
{
    [Fact]
    public void EveryForeignKeyToAWorkspace_IsACascadeDelete()
    {
        var model = Model();

        var offenders = ForeignKeysTo(model, typeof(EfHatchChatWorkspace))
            .Where(fk => fk.DeleteBehavior != DeleteBehavior.Cascade)
            .Select(fk => $"{fk.DeclaringEntityType.ClrType.Name}.{string.Join(",", fk.Properties.Select(p => p.Name))} is {fk.DeleteBehavior}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These foreign keys to a chat workspace are not a cascade delete: " + string.Join("; ", offenders)
            + ". Removing a workspace is one DELETE; give the key OnDelete(DeleteBehavior.Cascade).");
    }

    /// <summary>The scan is not blind: pointed at another entity it finds that entity's foreign keys.</summary>
    [Fact]
    public void TheScan_FindsForeignKeysToAnEntityThatHasThem()
    {
        Assert.NotEmpty(ForeignKeysTo(Model(), typeof(EfHatchProject)));
    }

    private static IModel Model()
    {
        using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return db.Model;
    }

    private static IEnumerable<IForeignKey> ForeignKeysTo(IModel model, Type principal) =>
        model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()).Where(fk => fk.PrincipalEntityType.ClrType == principal);
}

/// <summary>The same, and the routes, on a real Postgres. Skips without one; run it with <c>make test-api-db</c>.</summary>
[Collection(HatchDatabaseCollection.Name)]
public class ChatWorkspaceCascadeDatabaseTests
{
    private const string AcmeToken = "xoxb-1111-acmeSecretPart";

    [SkippableFact]
    public async Task NoForeignKeyToTheWorkspaceTable_HasADeleteRuleOtherThanCascade()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);

        var rules = await db.Database.SqlQueryRaw<string>(
            """
            SELECT rc.delete_rule AS "Value"
            FROM information_schema.referential_constraints rc
            JOIN information_schema.table_constraints pk
              ON pk.constraint_schema = rc.unique_constraint_schema AND pk.constraint_name = rc.unique_constraint_name
            WHERE pk.table_schema = 'hatch' AND pk.table_name = 'ChatWorkspaces'
            """).ToListAsync();

        Assert.DoesNotContain(rules, rule => rule != "CASCADE");
    }

    [SkippableFact]
    public async Task ADeleteThroughTheController_RemovesTheRow()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var chat = NewController(db);

        var acme = (await chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default)).Value!;
        Assert.IsType<NoContentResult>(await chat.RemoveWorkspace(acme.Id, default));

        await using var read = Open(connection);
        Assert.Empty(await read.ChatWorkspaces.ToListAsync());
    }

    [SkippableFact]
    public async Task ASecondInsertOfTheSameWorkspace_IsAConflict_NotAServerError()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var chat = NewController(db);

        Assert.NotNull((await chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default)).Value);
        var again = await chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default);

        Assert.IsType<ConflictObjectResult>(again.Result);
        await using var read = Open(connection);
        Assert.Single(await read.ChatWorkspaces.ToListAsync());
    }

    /// <summary>
    /// The race itself: another connect of the same workspace lands between the
    /// controller's look and its insert, so only the unique index stops it.
    /// </summary>
    [SkippableFact]
    public async Task ARacingInsert_IsCaughtFromTheUniqueIndex_AsAConflict()
    {
        var connection = await PrepareAsync();
        var rival = new RivalInserts(connection);
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).AddInterceptors(rival).Options);
        var chat = NewController(db);

        var result = await chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default);

        Assert.True(rival.Fired, "the competing insert never ran, so the index was not what refused");
        Assert.IsType<ConflictObjectResult>(result.Result);
        await using var read = Open(connection);
        Assert.Single(await read.ChatWorkspaces.ToListAsync());
    }

    [SkippableFact]
    public async Task RemovingAWorkspace_RemovesItsBindingsAndRooms_AndARoomsChosenProjects()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var chat = NewController(db);

        var acme = (await chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default)).Value!;
        var a = await AddProjectAsync(db, "AAA");
        Assert.NotNull((await chat.PutProjects(acme.Id, new ChatProjectsRequest([a]), default)).Value);
        var room = (await chat.CreateRoom(acme.Id, NewRoomRequest("C1", a), default)).Value!;

        Assert.IsType<NoContentResult>(await chat.RemoveWorkspace(acme.Id, default));

        await using var read = Open(connection);
        Assert.Empty(await read.ChatWorkspaces.ToListAsync());
        Assert.Empty(await read.ChatWorkspaceProjects.ToListAsync());
        Assert.Empty(await read.ChatRooms.ToListAsync());
        Assert.Empty(await read.ChatRoomProjects.Where(p => p.RoomId == room.Id).ToListAsync());
    }

    [SkippableFact]
    public async Task RemovingAProject_RemovesItsBindingsAndChosenRows_AndLeavesTheRoomAndWorkspace()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var chat = NewController(db);

        var acme = (await chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default)).Value!;
        var a = await AddProjectAsync(db, "AAA");
        Assert.NotNull((await chat.PutProjects(acme.Id, new ChatProjectsRequest([a]), default)).Value);
        var room = (await chat.CreateRoom(acme.Id, NewRoomRequest("C1", a), default)).Value!;

        db.Projects.Remove(await db.Projects.SingleAsync(p => p.Id == a));
        await db.SaveChangesAsync();

        await using var read = Open(connection);
        Assert.Empty(await read.ChatWorkspaceProjects.ToListAsync());
        Assert.Empty(await read.ChatRoomProjects.Where(p => p.RoomId == room.Id).ToListAsync());
        Assert.NotNull(await read.ChatWorkspaces.SingleOrDefaultAsync(w => w.Id == acme.Id));
        Assert.NotNull(await read.ChatRooms.SingleOrDefaultAsync(r => r.Id == room.Id));
    }

    [SkippableFact]
    public async Task UnbindingAProjectFromTheWorkspace_RemovesItFromTheRoomThatChoseIt()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var chat = NewController(db);

        var acme = (await chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default)).Value!;
        var a = await AddProjectAsync(db, "AAA");
        var b = await AddProjectAsync(db, "BBB");
        Assert.NotNull((await chat.PutProjects(acme.Id, new ChatProjectsRequest([a, b]), default)).Value);
        var room = (await chat.CreateRoom(acme.Id, NewRoomRequest("C1", a, b), default)).Value!;

        Assert.NotNull((await chat.PutProjects(acme.Id, new ChatProjectsRequest([b]), default)).Value);

        await using var read = Open(connection);
        Assert.Equal([b], await read.ChatRoomProjects.Where(p => p.RoomId == room.Id).Select(p => p.ProjectId).ToListAsync());
    }

    private static async Task<int> AddProjectAsync(HatchContext db, string key)
    {
        var project = new EfHatchProject { Key = key, Name = key, CreatedAt = DateTimeOffset.UtcNow };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    private static ChatRoomCreateRequest NewRoomRequest(string channelId, params int[] projectIds) =>
        new(channelId, "general", Changelog: false, Questions: false, Problems: false, UnownedProblems: false,
            AllProjects: projectIds.Length == 0, ProjectIds: projectIds.Length == 0 ? null : projectIds);

    private static async Task<string> PrepareAsync()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset - the foreign keys and the unique index are the database's. Run `make test-api-db`.");

        return await HatchDatabase.PrepareAsync();
    }

    private static HatchContext Open(string connection) =>
        new(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).Options);

    private static ChatController NewController(HatchContext db) =>
        new(db, new AcmeProvider(), new StubCaller(), new FakeTimeProvider(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero)), NullLogger<ChatController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private sealed class AcmeProvider : IChatProvider
    {
        public string Name => "fake";

        public string Manifest() => "{}";

        public Task<ChatTokenCheck> CheckTokenAsync(string token, CancellationToken ct) =>
            Task.FromResult(ChatTokenCheck.Workspace("TACME", "Acme"));

        public Task<ChatChannelList> ListChannelsAsync(string token, CancellationToken ct) =>
            Task.FromResult(ChatChannelList.Of([]));

        public Task<ChatPostResult> PostMessageAsync(string token, string channelId, string text, CancellationToken ct) =>
            Task.FromResult(ChatPostResult.Delivered());
    }

    /// <summary>Inserts the same workspace through a connection of its own, once, just before the first save lands.</summary>
    private sealed class RivalInserts(string connection) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Fired) return result;
            Fired = true;

            await using var other = Open(connection);
            var now = DateTimeOffset.UtcNow;
            other.ChatWorkspaces.Add(new EfHatchChatWorkspace
            {
                Provider = "fake", ExternalId = "TACME", Name = "Acme", Token = SecretProtector.Protect(AcmeToken), CreatedAt = now, UpdatedAt = now,
            });
            await other.SaveChangesAsync(ct);

            return result;
        }
    }

    private sealed class StubCaller : ICallerIdentity
    {
        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);
        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);
        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult<EfPerson?>(null);
        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult<EfApiKey?>(null);
        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult<Actor?>(null);
        public Task<bool> IsProgramAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<string> ActorNameAsync(CancellationToken ct) => Task.FromResult("Root");
    }
}

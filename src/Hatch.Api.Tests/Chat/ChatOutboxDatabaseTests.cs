using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// The half of the queue the database owns: a queued row lands or fails with the
/// write beside it, and unbinding a project, removing a room or removing a
/// workspace takes the rows under it. Skips without a Postgres; run it with
/// <c>make test-api-db</c>.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ChatOutboxDatabaseTests
{
    private static readonly DateTimeOffset Now = ChatOutboxTests.Now;

    [SkippableFact]
    public async Task AFailingWrite_QueuesNothing()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var seed = new ChatOutboxSeed(db, Now);
        var project = await seed.ProjectAsync("AER");
        var workspace = await seed.WorkspaceAsync("TACME", project);
        await seed.RoomAsync(workspace, "C1", changelog: true);
        var outbox = new ChatOutbox(db, new FakeTimeProvider(Now));

        await outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", project, ChatOutboxTests.Content("Done"), default);
        // A row that breaks a key, in the same unit of work: its room does not exist.
        db.ChatRoomProjects.Add(new EfHatchChatRoomProject { RoomId = 1_000_000, WorkspaceId = workspace, ProjectId = project });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await using var read = Open(connection);
        Assert.Empty(await read.ChatMessages.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await read.ChatSubjects.ToListAsync());
    }

    [SkippableFact]
    public async Task Unbinding_RemovesThatProjectsRowsInThatWorkspaceOnly()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var seed = new ChatOutboxSeed(db, Now);
        var aer = await seed.ProjectAsync("AER");
        var other = await seed.ProjectAsync("OTH");
        var acme = await seed.WorkspaceAsync("TACME", aer, other);
        var globex = await seed.WorkspaceAsync("TGLOBEX", aer);
        var acmeRoom = await seed.RoomAsync(acme, "C1", changelog: true, problems: true, unownedProblems: true);
        var globexRoom = await seed.RoomAsync(globex, "C1", changelog: true);
        var outbox = new ChatOutbox(db, new FakeTimeProvider(Now));

        await outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", aer, ChatOutboxTests.Content("About AER"), default);
        await outbox.QueueAsync(ChatMessageKind.Changelog, "issue:2", other, ChatOutboxTests.Content("About OTH"), default);
        await outbox.QueueAsync(ChatMessageKind.Problem, "finding:1", null, ChatOutboxTests.Content("Unowned"), default);
        await db.SaveChangesAsync();
        Assert.Equal(4, await db.ChatMessages.IgnoreQueryFilters().CountAsync());

        // The unbinding is the database's: a delete that EF does not walk.
        await db.ChatWorkspaceProjects.IgnoreQueryFilters()
            .Where(w => w.WorkspaceId == acme && w.ProjectId == aer)
            .ExecuteDeleteAsync();

        await using var read = Open(connection);
        var left = await read.ChatMessages.IgnoreQueryFilters().OrderBy(m => m.Subject).ThenBy(m => m.RoomId).ToListAsync();
        Assert.Equal(
            [("finding:1", acmeRoom), ("issue:1", globexRoom), ("issue:2", acmeRoom)],
            left.Select(m => (m.Subject, m.RoomId)));
        Assert.Null(left.Single(m => m.Subject == "finding:1").ProjectId);
    }

    [SkippableFact]
    public async Task RemovingARoom_RemovesItsRows_AndNoOthers()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var seed = new ChatOutboxSeed(db, Now);
        var project = await seed.ProjectAsync("AER");
        var workspace = await seed.WorkspaceAsync("TACME", project);
        var gone = await seed.RoomAsync(workspace, "C1", changelog: true);
        var kept = await seed.RoomAsync(workspace, "C2", changelog: true);
        var outbox = new ChatOutbox(db, new FakeTimeProvider(Now));
        await outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", project, ChatOutboxTests.Content("Done"), default);
        await db.SaveChangesAsync();

        await db.ChatRooms.Where(r => r.Id == gone).ExecuteDeleteAsync();

        await using var read = Open(connection);
        Assert.Equal([kept], await read.ChatMessages.IgnoreQueryFilters().Select(m => m.RoomId).ToListAsync());
        Assert.Single(await read.ChatSubjects.ToListAsync());
    }

    [SkippableFact]
    public async Task RemovingAWorkspace_RemovesItsRows_AndNoOthers()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var seed = new ChatOutboxSeed(db, Now);
        var project = await seed.ProjectAsync("AER");
        var acme = await seed.WorkspaceAsync("TACME", project);
        var globex = await seed.WorkspaceAsync("TGLOBEX", project);
        await seed.RoomAsync(acme, "C1", changelog: true, problems: true, unownedProblems: true);
        var kept = await seed.RoomAsync(globex, "C1", changelog: true);
        var outbox = new ChatOutbox(db, new FakeTimeProvider(Now));
        await outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", project, ChatOutboxTests.Content("Done"), default);
        await outbox.QueueAsync(ChatMessageKind.Problem, "finding:1", null, ChatOutboxTests.Content("Unowned"), default);
        await db.SaveChangesAsync();

        await db.ChatWorkspaces.Where(w => w.Id == acme).ExecuteDeleteAsync();

        await using var read = Open(connection);
        var left = await read.ChatMessages.IgnoreQueryFilters().ToListAsync();
        Assert.Equal([kept], left.Select(m => m.RoomId));
    }

    [SkippableFact]
    public async Task ASecondRowForTheSameRoomAndSubject_IsRefusedByTheUniqueIndex()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var seed = new ChatOutboxSeed(db, Now);
        var project = await seed.ProjectAsync("AER");
        var workspace = await seed.WorkspaceAsync("TACME", project);
        var room = await seed.RoomAsync(workspace, "C1", changelog: true);

        db.ChatMessages.Add(Row(room, workspace, project));
        db.ChatMessages.Add(Row(room, workspace, project));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static EfHatchChatMessage Row(int room, int workspace, int project) => new()
    {
        RoomId = room, WorkspaceId = workspace, ProjectId = project, Kind = "changelog", Subject = "issue:1",
        Content = "{}", QueuedAt = Now, PendingSince = Now,
    };

    private static async Task<string> PrepareAsync()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset - the keys and cascades are the database's. Run `make test-api-db`.");

        return await HatchDatabase.PrepareAsync();
    }

    private static HatchContext Open(string connection) =>
        new(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).Options);
}

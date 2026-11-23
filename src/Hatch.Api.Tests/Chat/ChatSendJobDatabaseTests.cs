using System.Security.Cryptography;
using Hatch.Api.Common;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// The half of the sender the database owns: the advisory lock that keeps two
/// senders from posting one message twice, the candidate query on a real
/// provider, and a binding removed while a message is out. Skips without a
/// Postgres; run it with <c>make test-api-db</c>.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ChatSendJobDatabaseTests
{
    private static readonly DateTimeOffset Now = ChatOutboxTests.Now;

    [SkippableFact]
    public async Task TwoSendersOnTwoContexts_SendEachMessageOnce()
    {
        var connection = await PrepareAsync();
        await using var seedDb = Open(connection);
        var f = await SeedAsync(seedDb, rooms: 3);

        // Whoever takes the lock sits inside its first send until the other has
        // come and gone, so the other has to find the lock held rather than lose a race.
        var entered = new TaskCompletionSource();
        var release = new ManualResetEventSlim();
        f.Provider.Script = call =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("the test never released the first send");
            return ChatSendResult.Sent($"ts-{call.ChannelId}");
        };

        await using var first = Open(connection);
        await using var second = Open(connection);
        var lockbox = new PostgresChatSendLock();
        var a = Task.Run(() => Job(first, f, lockbox).RunAsync(CancellationToken.None));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var b = Task.Run(() => Job(second, f, lockbox).RunAsync(CancellationToken.None));
        await b.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(f.Provider.Sends);

        release.Set();
        await a.WaitAsync(TimeSpan.FromSeconds(30));

        await using var read = Open(connection);
        var rows = await read.ChatMessages.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(r.Revision, r.SentRevision));
        Assert.Equal(3, f.Provider.Sends.Count);
        Assert.Equal(3, f.Provider.Sends.Select(s => s.ChannelId).Distinct().Count());
    }

    [SkippableFact]
    public async Task TheLock_IsFreeAgainOnceTheHolderIsDisposed()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var lockbox = new PostgresChatSendLock();

        var held = await lockbox.TryAcquireAsync(db, CancellationToken.None);
        Assert.NotNull(held);
        Assert.Null(await lockbox.TryAcquireAsync(db, CancellationToken.None));

        await held.DisposeAsync();

        var again = await lockbox.TryAcquireAsync(db, CancellationToken.None);
        Assert.NotNull(again);
        await again.DisposeAsync();
    }

    [SkippableFact]
    public async Task TheChatLock_IsNotTheGooseLock()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);

        await using var goose = await new PostgresGooseLock().TryAcquireAsync(db, CancellationToken.None);
        await using var chat = await new PostgresChatSendLock().TryAcquireAsync(db, CancellationToken.None);

        Assert.NotNull(goose);
        Assert.NotNull(chat);
    }

    [SkippableFact]
    public async Task ABindingRemovedDuringASend_LeavesTheRowsGone_AndTheTickFinishing()
    {
        var connection = await PrepareAsync();
        await using var seedDb = Open(connection);
        var f = await SeedAsync(seedDb, rooms: 2);

        f.Provider.Script = call =>
        {
            // An admin unbinds the project while the first message is out. The
            // database takes the waiting rows with it.
            using var other = Open(connection);
            other.ChatWorkspaceProjects.IgnoreQueryFilters()
                .Where(w => w.WorkspaceId == f.Workspace && w.ProjectId == f.Project)
                .ExecuteDelete();
            return ChatSendResult.Sent("ts-1");
        };

        await using var db = Open(connection);
        var job = Job(db, f, new PostgresChatSendLock());

        await job.RunAsync(CancellationToken.None);

        Assert.Single(f.Provider.Sends);
        Assert.False(f.Log.Lines.Any(l => l.Contains("tick failed")), string.Join(Environment.NewLine, f.Log.Lines));
        await using var read = Open(connection);
        Assert.Empty(await read.ChatMessages.IgnoreQueryFilters().ToListAsync());

        // And the next tick, on the same job, has nothing to do and does not fail.
        await job.RunAsync(CancellationToken.None);
        Assert.Single(f.Provider.Sends);
        Assert.False(f.Log.Lines.Any(l => l.Contains("tick failed")));
    }

    [SkippableFact]
    public async Task TheCandidateQuery_TakesEachRoomsOldest_OnARealDatabase()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var f = await SeedAsync(db, rooms: 2);
        var time = new FakeTimeProvider(Now.AddSeconds(5));
        var outbox = new ChatOutbox(db, time);
        await outbox.QueueAsync(ChatMessageKind.Changelog, "issue:2", f.Project, ChatOutboxTests.Content("later"), default);
        await db.SaveChangesAsync();

        await Job(db, f, new PostgresChatSendLock()).RunAsync(CancellationToken.None);

        // Both rooms hold the older "one" and the newer "later"; each hears its oldest.
        Assert.Equal(["one", "one"], f.Provider.Sends.Select(s => s.Content.Title));
        Assert.Equal(2, f.Provider.Sends.Select(s => s.ChannelId).Distinct().Count());
    }

    private static ChatSendJob Job(HatchContext db, Fixture f, IChatSendLock lockbox) =>
        new(db, f.Provider, f.Protector, lockbox, new FakeTimeProvider(Now.AddSeconds(10)), f.Log);

    /// <summary>A bound workspace with a sealed token and some rooms, each holding a queued message titled "one".</summary>
    private static async Task<Fixture> SeedAsync(HatchContext db, int rooms)
    {
        var seed = new ChatOutboxSeed(db, Now);
        var project = await seed.ProjectAsync("AER");
        var workspace = await seed.WorkspaceAsync("TACME", project);

        var protector = new SecretProtector(new SecretCipher(RandomNumberGenerator.GetBytes(32), null));
        var row = await db.ChatWorkspaces.SingleAsync(w => w.Id == workspace);
        Assert.True(protector.TrySeal(FakeChatProvider.AcmeToken, SecretStores.ChatWorkspace(row.Provider, row.ExternalId), out var sealedToken));
        row.Token = sealedToken;
        await db.SaveChangesAsync();

        for (var i = 1; i <= rooms; i++) await seed.RoomAsync(workspace, $"C{i}", changelog: true);

        var outbox = new ChatOutbox(db, new FakeTimeProvider(Now));
        await outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", project, ChatOutboxTests.Content("one"), default);
        await db.SaveChangesAsync();

        return new Fixture(project, workspace, protector);
    }

    private sealed class Fixture(int project, int workspace, SecretProtector protector)
    {
        public int Project { get; } = project;
        public int Workspace { get; } = workspace;
        public SecretProtector Protector { get; } = protector;
        public FakeChatProvider Provider { get; } = new();
        public ChatSendJobTests.CapturingLogger Log { get; } = new();
    }

    private static async Task<string> PrepareAsync()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");
        return await HatchDatabase.PrepareAsync();
    }

    private static HatchContext Open(string connection) =>
        new(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).Options);
}

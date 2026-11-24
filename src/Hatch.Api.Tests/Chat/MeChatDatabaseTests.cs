using System.Security.Cryptography;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// The half of the links the database owns: the unique indexes, and the cascade
/// from a workspace. The in-memory provider does not enforce an index, so these
/// run on Postgres and skip without one; run them with <c>make test-api-db</c>.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class MeChatDatabaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task ASecondLinkForOnePersonInOneWorkspace_IsRefused_WaitingOrNot()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var workspace = await WorkspaceAsync(db, "TACME");
        var person = Guid.NewGuid();
        db.ChatLinks.Add(Link(workspace, person, "U1"));
        await db.SaveChangesAsync();

        await using var again = Open(connection);
        again.ChatLinks.Add(Link(workspace, person, "U2"));

        await Assert.ThrowsAsync<DbUpdateException>(() => again.SaveChangesAsync());

        // Another workspace is another link.
        var other = await WorkspaceAsync(db, "TGLOBEX");
        db.ChatLinks.Add(Link(other, person, "U1"));
        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task ASecondConfirmedLinkOnOneMember_IsRefused_ButTwoWaitingAreNot()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var workspace = await WorkspaceAsync(db, "TACME");
        db.ChatLinks.Add(Link(workspace, Guid.NewGuid(), "U1", confirmed: true));
        db.ChatLinks.Add(Link(workspace, Guid.NewGuid(), "U2"));
        db.ChatLinks.Add(Link(workspace, Guid.NewGuid(), "U2"));
        await db.SaveChangesAsync();

        // A confirmed one beside a waiting one on the same member is fine; two confirmed are not.
        db.ChatLinks.Add(Link(workspace, Guid.NewGuid(), "U1"));
        await db.SaveChangesAsync();

        await using var second = Open(connection);
        second.ChatLinks.Add(Link(workspace, Guid.NewGuid(), "U1", confirmed: true));
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

        // The same member in another workspace is another member.
        var other = await WorkspaceAsync(db, "TGLOBEX");
        db.ChatLinks.Add(Link(other, Guid.NewGuid(), "U1", confirmed: true));
        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task AUsedSecretHash_CannotBeStoredTwice()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var workspace = await WorkspaceAsync(db, "TACME");
        var hash = RandomNumberGenerator.GetBytes(32);
        db.ChatLinks.Add(Link(workspace, Guid.NewGuid(), "U1", hash: hash));
        await db.SaveChangesAsync();

        await using var again = Open(connection);
        again.ChatLinks.Add(Link(workspace, Guid.NewGuid(), "U2", hash: hash));

        await Assert.ThrowsAsync<DbUpdateException>(() => again.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task TwoWaitingOnOneMember_TheFirstConfirmWins_AndTheSecondIs409()
    {
        var connection = await PrepareAsync();
        await using var seed = Open(connection);
        var workspace = await WorkspaceAsync(seed, "TACME");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        seed.ChatLinks.AddRange(Link(workspace, first, "U1", secret: "first-secret"), Link(workspace, second, "U1", secret: "second-secret"));
        await seed.SaveChangesAsync();

        await using var firstDb = Open(connection);
        var people = People(first, second);
        var firstController = Controller(firstDb, first, people);
        var confirmed = await firstController.ConfirmLink(new MeChatConfirmRequest("first-secret"), default);
        Assert.Equal("linked", confirmed.Value?.State);

        await using var secondDb = Open(connection);
        var secondController = Controller(secondDb, second, people);
        var refused = await secondController.ConfirmLink(new MeChatConfirmRequest("second-secret"), default);

        Assert.IsType<ConflictObjectResult>(refused.Result);
        await using var read = Open(connection);
        var rows = await read.ChatLinks.OrderBy(l => l.Id).ToListAsync();
        Assert.Equal([true, false], rows.Select(l => l.ConfirmedAt is not null));
        Assert.NotNull(rows[1].SecretHash);
    }

    [SkippableFact]
    public async Task RemovingTheWorkspace_RemovesItsLinks()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var workspace = await WorkspaceAsync(db, "TACME");
        var kept = await WorkspaceAsync(db, "TGLOBEX");
        db.ChatLinks.Add(Link(workspace, Guid.NewGuid(), "U1", confirmed: true));
        db.ChatLinks.Add(Link(workspace, Guid.NewGuid(), "U2"));
        db.ChatLinks.Add(Link(kept, Guid.NewGuid(), "U1"));
        await db.SaveChangesAsync();

        db.ChatWorkspaces.Remove(await db.ChatWorkspaces.SingleAsync(w => w.Id == workspace));
        await db.SaveChangesAsync();

        await using var read = Open(connection);
        Assert.Equal([kept], await read.ChatLinks.Select(l => l.WorkspaceId).ToListAsync());
    }

    [SkippableFact]
    public async Task RemovingALink_RemovesItsDirectMessages_AndNoOthers()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var workspace = await WorkspaceAsync(db, "TACME");
        var gone = Link(workspace, Guid.NewGuid(), "U1", confirmed: true);
        var kept = Link(workspace, Guid.NewGuid(), "U2", confirmed: true);
        db.ChatLinks.AddRange(gone, kept);
        await db.SaveChangesAsync();
        db.ChatMessages.AddRange(Direct(gone, "failure:1"), Direct(gone, "failure:2"), Direct(kept, "failure:1"));
        await db.SaveChangesAsync();

        db.ChatLinks.Remove(gone);
        await db.SaveChangesAsync();

        await using var read = Open(connection);
        Assert.Equal([kept.Id], await read.ChatMessages.IgnoreQueryFilters().Select(m => m.LinkId!.Value).ToListAsync());
    }

    [SkippableFact]
    public async Task RemovingTheWorkspace_RemovesItsDirectMessages_ThroughItsLinks()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var workspace = await WorkspaceAsync(db, "TACME");
        var link = Link(workspace, Guid.NewGuid(), "U1", confirmed: true);
        db.ChatLinks.Add(link);
        await db.SaveChangesAsync();
        db.ChatMessages.Add(Direct(link, "failure:1"));
        await db.SaveChangesAsync();

        db.ChatWorkspaces.Remove(await db.ChatWorkspaces.SingleAsync(w => w.Id == workspace));
        await db.SaveChangesAsync();

        await using var read = Open(connection);
        Assert.Empty(await read.ChatMessages.IgnoreQueryFilters().ToListAsync());
    }

    [SkippableFact]
    public async Task AMessageNeedsExactlyOneDestination_AndALinkHearsAnySubjectOnce()
    {
        var connection = await PrepareAsync();
        await using var db = Open(connection);
        var workspace = await WorkspaceAsync(db, "TACME");
        var link = Link(workspace, Guid.NewGuid(), "U1", confirmed: true);
        var room = new EfHatchChatRoom { WorkspaceId = workspace, ExternalId = "C1", Name = "c", CreatedAt = Now, UpdatedAt = Now };
        db.ChatLinks.Add(link);
        db.ChatRooms.Add(room);
        await db.SaveChangesAsync();

        await using var neither = Open(connection);
        var none = Direct(link, "failure:1");
        none.LinkId = null;
        neither.ChatMessages.Add(none);
        await Assert.ThrowsAsync<DbUpdateException>(() => neither.SaveChangesAsync());

        await using var both = Open(connection);
        var two = Direct(link, "failure:1");
        two.RoomId = room.Id;
        both.ChatMessages.Add(two);
        await Assert.ThrowsAsync<DbUpdateException>(() => both.SaveChangesAsync());

        db.ChatMessages.Add(Direct(link, "failure:1"));
        await db.SaveChangesAsync();

        await using var twice = Open(connection);
        twice.ChatMessages.Add(Direct(link, "failure:1"));
        await Assert.ThrowsAsync<DbUpdateException>(() => twice.SaveChangesAsync());
    }

    private static EfHatchChatMessage Direct(EfHatchChatLink link, string subject) => new()
    {
        LinkId = link.Id, WorkspaceId = link.WorkspaceId, Kind = "direct", Subject = subject,
        Content = "{}", QueuedAt = Now, PendingSince = Now,
    };

    [SkippableFact]
    public async Task AfterTheHolderIsRemoved_ASecondPersonConfirmsTheSameMemberAtOnce()
    {
        var connection = await PrepareAsync();
        await using var seed = Open(connection);
        var workspace = await WorkspaceAsync(seed, "TACME");
        var removed = Guid.NewGuid();
        var second = Guid.NewGuid();
        seed.ChatLinks.AddRange(Link(workspace, removed, "U1", confirmed: true), Link(workspace, second, "U1", secret: "second-secret"));
        await seed.SaveChangesAsync();

        // Only the second person is in the people table: the holder was removed, and nothing cascaded.
        await using var db = Open(connection);
        var confirmed = await Controller(db, second, People(second)).ConfirmLink(new MeChatConfirmRequest("second-secret"), default);

        Assert.Equal("linked", confirmed.Value?.State);
        await using var read = Open(connection);
        var rows = await read.ChatLinks.ToListAsync();
        Assert.Equal([second], rows.Select(l => l.PersonId));
        Assert.NotNull(rows[0].ConfirmedAt);
    }

    /// <summary>A people table holding exactly these people.</summary>
    private static AppDbContext People(params Guid[] ids)
    {
        var app = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        app.People.AddRange(ids.Select(id => new EfPerson { Id = id, Name = "P", Role = PersonRole.User, CreatedAt = Now, UpdatedAt = Now }));
        app.SaveChanges();

        return app;
    }

    /// <summary>The controller for one person, over a real context. Only the confirm route is used, which reads nothing from Slack.</summary>
    private static MeChatController Controller(HatchContext db, Guid personId, AppDbContext app) =>
        new(db, app,
            new FakeChatProvider(),
            new MeScopeTests.StubCaller(null, new EfPerson { Id = personId, Name = "P", Role = PersonRole.User, CreatedAt = Now, UpdatedAt = Now }, null),
            new StubActorDirectory { Principal = new Actor(ActorKind.Person, personId, "P") },
            new MeScopeTests.FixedScope(VisibleProjects.Everything),
            new SecretProtector(new SecretCipher(RandomNumberGenerator.GetBytes(32), null)),
            new ChatLinks(Options.Create(new HatchOptions { PublicBaseUrl = "https://hatch.example" })),
            RosterSeed.For(db, app),
            new FakeTimeProvider(Now), NullLogger<MeChatController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static async Task<int> WorkspaceAsync(HatchContext db, string externalId)
    {
        var workspace = new EfHatchChatWorkspace
        {
            Provider = "fake", ExternalId = externalId, Name = externalId, Token = "sealed", CreatedAt = Now, UpdatedAt = Now,
        };
        db.ChatWorkspaces.Add(workspace);
        await db.SaveChangesAsync();

        return workspace.Id;
    }

    private static EfHatchChatLink Link(int workspace, Guid person, string member, bool confirmed = false, byte[]? hash = null, string? secret = null) => new()
    {
        WorkspaceId = workspace, PersonId = person, MemberId = member, MemberName = member, ConversationId = $"D-{member}", CreatedAt = Now,
        ConfirmedAt = confirmed ? Now : null,
        SecretHash = confirmed ? null : hash ?? (secret is null ? RandomNumberGenerator.GetBytes(32) : AuthTokens.Hash(secret)),
        SecretExpiresAt = confirmed ? null : Now.AddYears(10),
    };

    private static async Task<string> PrepareAsync()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset - the indexes and the cascade are the database's. Run `make test-api-db`.");

        return await HatchDatabase.PrepareAsync();
    }

    private static HatchContext Open(string connection) =>
        new(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).Options);
}

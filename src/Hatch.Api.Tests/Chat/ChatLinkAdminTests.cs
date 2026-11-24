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
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// An admin's view of everybody's Slack links, and the rule that a link whose
/// person has been removed counts for nothing. Wired against the in-memory
/// databases, so the unique index is held by <see cref="MeChatDatabaseTests"/>.
/// </summary>
public class ChatLinkAdminTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly ChatMember Ada = new("U-ADA", "Ada Lovelace", "ada");

    [Fact]
    public async Task TheList_CarriesEveryPersonsLinks_WithNamesAndStates_InOrder()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        var globex = await h.WorkspaceAsync("TGLOBEX", "Globex");
        var bob = h.Person("Bob");
        var ada = h.Person("Ada");
        await h.LinkAsync(globex, bob, "U-BOB", "Bob Hope", confirmed: true);
        await h.LinkAsync(globex, ada, "U-ADA", "Ada Lovelace", confirmed: false);
        await h.LinkAsync(acme, ada, "U-ADA", "Ada Lovelace", confirmed: true);

        var rows = Value(await h.Chat.GetLinks(default));

        Assert.Equal(
            [("Ada", "Acme", "linked"), ("Ada", "Globex", "waiting"), ("Bob", "Globex", "linked")],
            rows.Select(r => (h.Names[r.PersonId], r.WorkspaceName, r.State)));
        Assert.Equal("Bob Hope", rows[2].MemberName);
        Assert.Null(rows[0].ExpiresAt);
        Assert.Equal(Now.AddHours(24), rows[1].ExpiresAt);
    }

    [Fact]
    public async Task TheList_NamesNoMemberIdConversationOrSecret()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        await h.LinkAsync(acme, h.Person("Ada"), "U-SECRETMEMBER", "Ada Lovelace", confirmed: false);

        var json = System.Text.Json.JsonSerializer.Serialize(Value(await h.Chat.GetLinks(default)));

        Assert.DoesNotContain("U-SECRETMEMBER", json);
        Assert.DoesNotContain("D-CONV", json);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemovingAWaitingLink_StopsItsSecretConfirming()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[FakeChatProvider.AcmeToken] = [Ada];
        var ada = h.Person("Ada");
        var link = Value(await h.Me(ada, "Ada").PostLink(new MeChatLinkRequest(acme, Ada.Id), default));
        var secret = QueryHelpers.ParseQuery(new Uri(h.Provider.Sends[^1].Content.Links[0].Url!).Query)["chatLink"].ToString();

        Assert.IsType<NoContentResult>(await h.Chat.RemoveLink(link.Id, default));

        var refused = Assert.IsType<ObjectResult>((await h.Me(ada, "Ada").ConfirmLink(new MeChatConfirmRequest(secret), default)).Result);
        Assert.Equal(410, refused.StatusCode);
        Assert.Empty(await h.Db.ChatLinks.ToListAsync());
        Assert.Contains(h.Logs.Messages, m => m.Contains($"Chat link {link.Id}") && m.Contains("removed by"));
    }

    [Fact]
    public async Task RemovingALink_LeavesThePersonsOtherWorkspacesAlone_AndAnUnknownIdIsNotFound()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        var globex = await h.WorkspaceAsync("TGLOBEX", "Globex");
        var ada = h.Person("Ada");
        var gone = await h.LinkAsync(acme, ada, "U-ADA", "Ada Lovelace", confirmed: true);
        var kept = await h.LinkAsync(globex, ada, "U-ADA", "Ada Lovelace", confirmed: true);

        Assert.IsType<NotFoundResult>(await h.Chat.RemoveLink(gone + 100, default));
        Assert.Equal(2, await h.Db.ChatLinks.CountAsync());

        Assert.IsType<NoContentResult>(await h.Chat.RemoveLink(gone, default));

        Assert.Equal([kept], (await h.Db.ChatLinks.ToListAsync()).Select(l => l.Id));
    }

    [Fact]
    public async Task ALinkOfARemovedPerson_IsNotConfirmed_AndIsDeletedByTheRead()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        var ada = h.Person("Ada");
        var bob = h.Person("Bob");
        await h.LinkAsync(acme, ada, "U-ADA", "Ada Lovelace", confirmed: true);
        await h.LinkAsync(acme, bob, "U-BOB", "Bob Hope", confirmed: true);
        await h.RemovePersonAsync(bob);

        var confirmed = await h.Roster.ConfirmedAsync(acme, default);

        Assert.Equal(["U-ADA"], confirmed.Select(l => l.MemberId));
        Assert.Equal(["U-ADA"], (await h.Db.ChatLinks.ToListAsync()).Select(l => l.MemberId));
    }

    [Fact]
    public async Task TheList_DropsTheLinksOfARemovedPerson_InEveryWorkspace()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        var globex = await h.WorkspaceAsync("TGLOBEX", "Globex");
        var bob = h.Person("Bob");
        await h.LinkAsync(acme, bob, "U-BOB", "Bob Hope", confirmed: true);
        await h.LinkAsync(globex, bob, "U-BOB", "Bob Hope", confirmed: false);
        await h.RemovePersonAsync(bob);

        Assert.Empty(Value(await h.Chat.GetLinks(default)));
        Assert.Empty(await h.Db.ChatLinks.ToListAsync());
    }

    [Fact]
    public async Task PruningAfterAnotherRequestGotThere_IsNotAnError_AndLeavesOtherTrackedRowsAlone()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        var ada = h.Person("Ada");
        var bob = h.Person("Bob");
        await h.LinkAsync(acme, ada, "U-ADA", "Ada Lovelace", confirmed: true);
        await h.LinkAsync(acme, bob, "U-BOB", "Bob Hope", confirmed: true);
        await h.RemovePersonAsync(bob);

        // Another request has already deleted the row this one is about to.
        await using var racing = new HatchContext(h.HatchOptions);
        racing.ChatLinks.RemoveRange(await racing.ChatLinks.Where(l => l.MemberId == "U-BOB").ToListAsync());
        await racing.SaveChangesAsync();

        Assert.Equal(0, await h.Roster.PruneAsync(acme, default));
        Assert.Equal(["U-ADA"], (await h.Db.ChatLinks.ToListAsync()).Select(l => l.MemberId));
    }

    // ---- The local person ----

    [Fact]
    public async Task TheLocalPersonsLink_Survives_WithTheWallOffAndNobodyEnrolled_AndNoRequest()
    {
        var h = New(localName: "Matt");
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        await h.LinkAsync(acme, LocalCaller.PersonId, "U-ADA", "Ada Lovelace", confirmed: true);

        // No HttpContext anywhere in this: it is what a background sender sees.
        Assert.Equal(["U-ADA"], (await h.Roster.ConfirmedAsync(acme, default)).Select(l => l.MemberId));
        Assert.Single(await h.Db.ChatLinks.ToListAsync());

        var row = Assert.Single(Value(await h.Chat.GetLinks(default)));
        Assert.Equal(LocalCaller.PersonId, row.PersonId);
        Assert.Equal("Matt", (await h.Roster.NamesAsync([LocalCaller.PersonId], default))[LocalCaller.PersonId]);
    }

    [Fact]
    public async Task TheLocalPersonsLink_IsPruned_OnceAPersonIsEnrolled_OrTheWallIsOn()
    {
        foreach (var (wallOn, anyPerson) in new[] { (false, true), (true, false) })
        {
            var h = New(wallOn: wallOn, anyPerson: anyPerson);
            var acme = await h.WorkspaceAsync("TACME", "Acme");
            await h.LinkAsync(acme, LocalCaller.PersonId, "U-ADA", "Ada Lovelace", confirmed: true);

            Assert.Empty(await h.Roster.ConfirmedAsync(acme, default));
            Assert.Empty(await h.Db.ChatLinks.ToListAsync());
        }
    }

    // ---- Harness ----

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {(result.Result as ObjectResult)?.StatusCode} {(result.Result as ObjectResult)?.Value ?? result.Result?.GetType().Name}");

    private static Harness New(bool wallOn = false, bool anyPerson = false, string? localName = null) => new(wallOn, anyPerson, localName);

    private sealed class Harness
    {
        private readonly AppDbContext appDb = RosterSeed.NewAppDb();
        private readonly SecretProtector protector = new(new SecretCipher(RandomNumberGenerator.GetBytes(32), null));
        private readonly ChatLinks links = new(Options.Create(new HatchOptions { PublicBaseUrl = "https://hatch.example" }));

        public Harness(bool wallOn, bool anyPerson, string? localName)
        {
            HatchOptions = new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            Db = new HatchContext(HatchOptions);
            Time = new FakeTimeProvider(Now);
            Logs = new CapturedLogs();
            Roster = RosterSeed.For(Db, appDb, wallOn, anyPerson, localName);
            Chat = new ChatController(
                Db, Provider, new MeScopeTests.StubCaller(null, null, null), Time, protector, links, Roster,
                new LoggerFactory([Logs]).CreateLogger<ChatController>())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
        }

        public DbContextOptions<HatchContext> HatchOptions { get; }
        public HatchContext Db { get; }
        public FakeTimeProvider Time { get; }
        public FakeChatProvider Provider { get; } = new();
        public CapturedLogs Logs { get; }
        public ChatLinkRoster Roster { get; }
        public ChatController Chat { get; }

        /// <summary>The names of the people this harness has made, by id.</summary>
        public Dictionary<Guid, string> Names { get; } = [];

        public Guid Person(string name)
        {
            var person = new EfPerson { Id = Guid.NewGuid(), Name = name, Role = PersonRole.User, CreatedAt = Now, UpdatedAt = Now };
            appDb.People.Add(person);
            appDb.SaveChanges();
            Names[person.Id] = name;

            return person.Id;
        }

        public async Task RemovePersonAsync(Guid id)
        {
            appDb.People.Remove(await appDb.People.SingleAsync(p => p.Id == id));
            await appDb.SaveChangesAsync();
        }

        public MeChatController Me(Guid id, string name) =>
            new(Db, appDb, Provider,
                new MeScopeTests.StubCaller(null, new EfPerson { Id = id, Name = name, Role = PersonRole.User, CreatedAt = Now, UpdatedAt = Now }, null),
                new StubActorDirectory { Principal = new Actor(ActorKind.Person, id, name) },
                new MeScopeTests.FixedScope(VisibleProjects.Everything), protector, links, Roster, Time, NullLogger<MeChatController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };

        public async Task<int> WorkspaceAsync(string externalId, string name)
        {
            if (!protector.TrySeal(FakeChatProvider.AcmeToken, SecretStores.ChatWorkspace("fake", externalId), out var sealedToken))
                throw new InvalidOperationException("the test protector could not seal");

            var workspace = new EfHatchChatWorkspace
            {
                Provider = "fake", ExternalId = externalId, Name = name, Token = sealedToken, CreatedAt = Now, UpdatedAt = Now,
            };
            Db.ChatWorkspaces.Add(workspace);
            await Db.SaveChangesAsync();

            return workspace.Id;
        }

        public async Task<int> LinkAsync(int workspaceId, Guid personId, string memberId, string memberName, bool confirmed)
        {
            var link = new EfHatchChatLink
            {
                WorkspaceId = workspaceId, PersonId = personId, MemberId = memberId, MemberName = memberName,
                ConversationId = "D-CONV", CreatedAt = Now,
                ConfirmedAt = confirmed ? Now : null,
                SecretHash = confirmed ? null : RandomNumberGenerator.GetBytes(AuthHash.Length),
                SecretExpiresAt = confirmed ? null : Now.AddHours(24),
            };
            Db.ChatLinks.Add(link);
            await Db.SaveChangesAsync();

            return link.Id;
        }
    }
}

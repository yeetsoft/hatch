using System.Security.Cryptography;
using System.Text.Json;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// An admin connects, lists, re-tokens and removes chat workspaces. The
/// provider is a fake answering from a token-to-workspace table, so nothing
/// here calls a chat service; <see cref="SlackChatProviderTests"/> holds that
/// half. The in-memory context does not enforce the unique index, so the race
/// it guards is in <c>ChatWorkspaceCascadeTests</c>, on Postgres.
/// </summary>
public class ChatControllerTests
{
    private const string AcmeToken = FakeChatProvider.AcmeToken;
    private const string AcmeNewToken = FakeChatProvider.AcmeNewToken;
    private const string GlobexToken = FakeChatProvider.GlobexToken;
    private const string RefusedToken = FakeChatProvider.RefusedToken;

    [Fact]
    public async Task TwoWorkspaces_Connect_AndBothAreListed_OldestFirst()
    {
        var h = NewHarness();

        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        h.Time.Advance(TimeSpan.FromMinutes(1));
        var globex = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(GlobexToken), default));

        var listed = Value(await h.Chat.GetWorkspaces(default));

        Assert.Equal([acme.Id, globex.Id], listed.Select(w => w.Id));
        Assert.Equal(["Acme", "Globex"], listed.Select(w => w.Name));
        Assert.Equal(["TACME", "TGLOBEX"], listed.Select(w => w.ExternalId));
        Assert.All(listed, w => Assert.Equal("fake", w.Provider));
        Assert.All(listed, w => Assert.True(w.HasToken));
        Assert.Equal(Now, acme.CreatedAt);
    }

    [Fact]
    public async Task ABlankToken_IsABadRequest_AndCallsNothing()
    {
        var h = NewHarness();

        foreach (var token in new string?[] { null, "", "   " })
        {
            var result = await h.Chat.ConnectWorkspace(new ChatTokenRequest(token), default);
            Assert.IsType<BadRequestObjectResult>(result.Result);
        }

        Assert.Equal(0, h.Provider.Checks);
        Assert.Empty(h.Db.ChatWorkspaces);
    }

    [Fact]
    public async Task ARefusedToken_AddsNothing_AndSaysTheProvidersSentence()
    {
        var h = NewHarness();

        var result = await h.Chat.ConnectWorkspace(new ChatTokenRequest(RefusedToken), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(FakeChatProvider.RefusalSentence, refusal.Value);
        Assert.Empty(h.Db.ChatWorkspaces);
    }

    [Fact]
    public async Task AWorkspaceAlreadyConnected_IsAConflict_NamingTheReplaceRoute()
    {
        var h = NewHarness();
        await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default);

        var again = await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeNewToken), default);

        var conflict = Assert.IsType<ConflictObjectResult>(again.Result);
        Assert.Contains("replace its token", (string)conflict.Value!);
        Assert.Single(h.Db.ChatWorkspaces);
        Assert.Equal(AcmeToken, h.Open((await h.Db.ChatWorkspaces.SingleAsync()).Token));
    }

    [Fact]
    public async Task ReplacingAToken_EndsTheSendersHoldOnTheWorkspace()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var row = await h.Db.ChatWorkspaces.SingleAsync();
        row.HoldUntil = Now.AddMinutes(10);
        row.FailStreak = 6;
        row.SendFailingSince = Now;
        row.LastRefusal = "Slack did not accept that token";
        row.LastRefusalAt = Now;
        await h.Db.SaveChangesAsync();

        await h.Chat.ReplaceToken(acme.Id, new ChatTokenRequest(AcmeNewToken), default);

        row = await h.Db.ChatWorkspaces.AsNoTracking().SingleAsync();
        Assert.Null(row.HoldUntil);
        Assert.Equal(0, row.FailStreak);
        Assert.Null(row.SendFailingSince);
    }

    [Fact]
    public async Task ReplacingATokenForTheSameWorkspace_ChangesTheTokenNameAndUpdatedAt_AndNothingElse()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        h.Provider.Workspaces[AcmeNewToken] = ("TACME", "Acme Renamed");
        h.Time.Advance(TimeSpan.FromHours(1));

        var replaced = Value(await h.Chat.ReplaceToken(acme.Id, new ChatTokenRequest(AcmeNewToken), default));

        var row = await h.Db.ChatWorkspaces.AsNoTracking().SingleAsync();
        Assert.Equal(AcmeNewToken, h.Open(row.Token));
        Assert.Equal("Acme Renamed", row.Name);
        Assert.Equal(Now.AddHours(1), row.UpdatedAt);
        Assert.Equal(acme.Id, row.Id);
        Assert.Equal("TACME", row.ExternalId);
        Assert.Equal("fake", row.Provider);
        Assert.Equal(Now, row.CreatedAt);
        AssertWorkspace(new ChatWorkspaceDto(acme.Id, "fake", "TACME", "Acme Renamed", true, Now, []), replaced);
    }

    [Fact]
    public async Task ReplacingWithATokenForAnotherWorkspace_IsAConflict_AndKeepsTheOldToken()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));

        var result = await h.Chat.ReplaceToken(acme.Id, new ChatTokenRequest(GlobexToken), default);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("another workspace", (string)conflict.Value!);
        var row = await h.Db.ChatWorkspaces.AsNoTracking().SingleAsync();
        Assert.Equal(AcmeToken, h.Open(row.Token));
        Assert.Equal("Acme", row.Name);
        Assert.Equal(Now, row.UpdatedAt);
    }

    [Fact]
    public async Task ReplacingAToken_ThatIsRefused_KeepsTheOldToken()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));

        var result = await h.Chat.ReplaceToken(acme.Id, new ChatTokenRequest(RefusedToken), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(AcmeToken, h.Open((await h.Db.ChatWorkspaces.AsNoTracking().SingleAsync()).Token));
    }

    [Fact]
    public async Task ReplacingTheTokenOfAnUnknownWorkspace_IsNotFound()
    {
        var h = NewHarness();

        var result = await h.Chat.ReplaceToken(404, new ChatTokenRequest(AcmeToken), default);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal(0, h.Provider.Checks);
    }

    [Fact]
    public async Task Removing_DeletesTheRow_AndAnUnknownIdIsNotFound()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var globex = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(GlobexToken), default));

        Assert.IsType<NoContentResult>(await h.Chat.RemoveWorkspace(acme.Id, default));
        Assert.IsType<NotFoundResult>(await h.Chat.RemoveWorkspace(acme.Id, default));

        Assert.Equal([globex.Id], (await h.Db.ChatWorkspaces.AsNoTracking().ToListAsync()).Select(w => w.Id));
    }

    [Fact]
    public async Task TheStoredToken_IsNotThePlaintext_AndUnprotectsBackToIt()
    {
        var h = NewHarness();
        await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default);

        var stored = (await h.Db.ChatWorkspaces.AsNoTracking().SingleAsync()).Token;

        Assert.NotEqual(AcmeToken, stored);
        Assert.DoesNotContain("acmeSecretPart", stored);
        Assert.StartsWith("v2:", stored, StringComparison.Ordinal);
        Assert.Equal(AcmeToken, h.Open(stored));
        // Sealed for this workspace's row and no other.
        Assert.Null(h.Protector.Unprotect(stored, SecretStores.ChatWorkspace("fake", "TGLOBEX")));
    }

    [Fact]
    public async Task TheToken_IsTrimmedBeforeItIsChecked()
    {
        var h = NewHarness();

        Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest($"  {AcmeToken}\n"), default));

        Assert.Equal(AcmeToken, h.Open((await h.Db.ChatWorkspaces.AsNoTracking().SingleAsync()).Token));
    }

    /// <summary>With no key there is nothing to seal under, so the token is never sent to the chat service.</summary>
    [Fact]
    public async Task WithNoKey_BothWritesAreABadRequestNamingTheKey_AndNeverCallTheProvider()
    {
        var h = NewHarness(withKey: false);
        var acme = new EfHatchChatWorkspace
        {
            Provider = "fake", ExternalId = "TACME", Name = "Acme", Token = "", CreatedAt = Now, UpdatedAt = Now,
        };
        h.Db.ChatWorkspaces.Add(acme);
        await h.Db.SaveChangesAsync();

        var connect = await h.Chat.ConnectWorkspace(new ChatTokenRequest(GlobexToken), default);
        var replace = await h.Chat.ReplaceToken(acme.Id, new ChatTokenRequest(AcmeNewToken), default);

        foreach (var refused in new[] { connect.Result, replace.Result })
        {
            var bad = Assert.IsType<BadRequestObjectResult>(refused);
            Assert.Contains("HATCH_SECRET_KEY", (string)bad.Value!);
        }

        Assert.Equal(0, h.Provider.Checks);
        Assert.Single(h.Db.ChatWorkspaces);
    }

    [Fact]
    public void TheManifest_IsTheProvidersJson_UnderItsName()
    {
        var h = NewHarness();

        var manifest = Value(h.Chat.GetManifest());

        Assert.Equal("fake", manifest.Provider);
        Assert.Equal(FakeChatProvider.ManifestText, manifest.Manifest);
    }

    [Fact]
    public async Task NoResponseAndNoLogLine_HoldsTheTokenOrAnyPartOfIt_OnSuccessOrAnyRefusal()
    {
        var h = NewHarness();
        var bodies = new List<string>();

        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        bodies.Add(Text(acme));
        bodies.Add(Text(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default)));
        bodies.Add(Text(await h.Chat.ConnectWorkspace(new ChatTokenRequest(RefusedToken), default)));
        bodies.Add(Text(await h.Chat.ConnectWorkspace(new ChatTokenRequest(" "), default)));
        bodies.Add(Text(await h.Chat.ReplaceToken(acme.Id, new ChatTokenRequest(AcmeNewToken), default)));
        bodies.Add(Text(await h.Chat.ReplaceToken(acme.Id, new ChatTokenRequest(GlobexToken), default)));
        bodies.Add(Text(await h.Chat.ReplaceToken(acme.Id, new ChatTokenRequest(RefusedToken), default)));
        bodies.Add(Text(await h.Chat.ReplaceToken(404, new ChatTokenRequest(AcmeToken), default)));
        bodies.Add(Text(await h.Chat.GetWorkspaces(default)));
        bodies.Add(Text(h.Chat.GetManifest()));
        await h.Chat.RemoveWorkspace(acme.Id, default);

        Assert.NotEmpty(h.Log.Lines);
        foreach (var token in new[] { AcmeToken, AcmeNewToken, GlobexToken, RefusedToken })
        foreach (var part in Parts(token))
        {
            Assert.DoesNotContain(bodies, b => b.Contains(part, StringComparison.Ordinal));
            Assert.DoesNotContain(h.Log.Lines, l => l.Contains(part, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task AWrite_LogsWhoDidWhatToWhich_AndNothingMore()
    {
        var h = NewHarness();

        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));

        var line = Assert.Single(h.Log.Lines);
        Assert.Contains("Root", line);
        Assert.Contains(acme.Id.ToString(), line);
    }

    [Fact]
    public void TheDto_HasNoTokenMember()
    {
        Assert.DoesNotContain(typeof(ChatWorkspaceDto).GetProperties(), p => p.Name.Contains("Token") && p.Name != "HasToken");
    }

    [Fact]
    public async Task PutProjects_BindsAndReplacesTheWholeSet()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var a = await AddProjectAsync(h.Db, "AAA");
        var b = await AddProjectAsync(h.Db, "BBB");

        var bound = Value(await h.Chat.PutProjects(acme.Id, new ChatProjectsRequest([a, b]), default));
        Assert.Equal([a, b], bound.Order());

        var replaced = Value(await h.Chat.PutProjects(acme.Id, new ChatProjectsRequest([b]), default));
        Assert.Equal([b], replaced);

        var listed = Value(await h.Chat.GetWorkspaces(default));
        Assert.Equal([b], Assert.Single(listed).ProjectIds);
    }

    [Fact]
    public async Task PutProjects_AnUnknownId_IsBadRequest_AndChangesNothing()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var a = await AddProjectAsync(h.Db, "AAA");
        Value(await h.Chat.PutProjects(acme.Id, new ChatProjectsRequest([a]), default));

        var result = await h.Chat.PutProjects(acme.Id, new ChatProjectsRequest([a, 999_999]), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal([a], await h.Db.ChatWorkspaceProjects.AsNoTracking().Select(p => p.ProjectId).ToListAsync());
    }

    [Fact]
    public async Task PutProjects_AnUnknownWorkspace_IsNotFound()
    {
        var h = NewHarness();

        var result = await h.Chat.PutProjects(404, new ChatProjectsRequest([]), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task PutProjects_AHiddenBinding_SurvivesAScopedReplace()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var a = await AddProjectAsync(h.Db, "AAA");
        var b = await AddProjectAsync(h.Db, "BBB");
        Value(await h.Chat.PutProjects(acme.Id, new ChatProjectsRequest([a, b]), default));

        var scoped = h.ScopedChat(new VisibleProjects(false, new HashSet<int> { a }));
        var bound = Value(await scoped.PutProjects(acme.Id, new ChatProjectsRequest([a]), default));
        Assert.Equal([a], bound);

        var everything = (await h.Db.ChatWorkspaceProjects.AsNoTracking().Select(p => p.ProjectId).ToListAsync()).Order();
        Assert.Equal([a, b], everything);
    }

    [Fact]
    public async Task PutProjects_AHiddenId_IsUnknownProject_AndNeverSaysHiddenOrCarriesNone()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var a = await AddProjectAsync(h.Db, "AAA");
        var b = await AddProjectAsync(h.Db, "BBB");

        var scoped = h.ScopedChat(new VisibleProjects(false, new HashSet<int> { a }));
        var result = await scoped.PutProjects(acme.Id, new ChatProjectsRequest([b]), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("unknown project", refusal.Value);
    }

    [Fact]
    public async Task GetDelivery_CountsWaitingAndDropped_PerWorkspace_AndCarriesWhatSlackSaid()
    {
        var h = NewHarness(publicUrl: "https://hatch.example.com");
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var globex = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(GlobexToken), default));
        var a = await AddProjectAsync(h.Db, "AAA");
        var room = await AddRoomAsync(h.Db, acme.Id);

        h.Db.ChatMessages.AddRange(
            Message(room, acme.Id, a, "issue:1"),
            Message(room, acme.Id, a, "issue:2"),
            Message(room, acme.Id, a, "issue:3", sent: true),
            Message(room, acme.Id, a, "issue:4", dropped: true));
        var row = await h.Db.ChatWorkspaces.SingleAsync(w => w.Id == acme.Id);
        row.LastRefusal = "ratelimited";
        row.LastRefusalAt = Now;
        row.SendFailingSince = Now.AddMinutes(-5);
        await h.Db.SaveChangesAsync();

        var delivery = Value(await h.Chat.GetDelivery(default));

        Assert.True(delivery.LinksOn);
        var acmeRow = delivery.Workspaces.Single(w => w.WorkspaceId == acme.Id);
        Assert.Equal(2, acmeRow.Waiting);
        Assert.Equal(1, acmeRow.Dropped);
        Assert.Equal("ratelimited", acmeRow.LastSaid);
        Assert.Equal(Now, acmeRow.LastSaidAt);
        Assert.Equal(Now.AddMinutes(-5), acmeRow.FailingSince);

        var globexRow = delivery.Workspaces.Single(w => w.WorkspaceId == globex.Id);
        Assert.Equal(0, globexRow.Waiting);
        Assert.Equal(0, globexRow.Dropped);
        Assert.Null(globexRow.LastSaid);
        Assert.Null(globexRow.FailingSince);
    }

    [Fact]
    public async Task GetDelivery_CountsDirectMessages_UnderTheirLinksWorkspace()
    {
        var h = NewHarness(publicUrl: "https://hatch.example.com");
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var a = await AddProjectAsync(h.Db, "AAA");
        var room = await AddRoomAsync(h.Db, acme.Id);
        var link = new EfHatchChatLink
        {
            WorkspaceId = acme.Id, PersonId = Guid.NewGuid(), MemberId = "U1", MemberName = "U1", ConversationId = "D1",
            CreatedAt = Now, ConfirmedAt = Now,
        };
        h.Db.ChatLinks.Add(link);
        await h.Db.SaveChangesAsync();
        h.Db.ChatMessages.AddRange(
            Message(room, acme.Id, a, "issue:1"),
            new EfHatchChatMessage
            {
                LinkId = link.Id, WorkspaceId = acme.Id, Kind = "direct", Subject = "failure:1", Content = "{}",
                QueuedAt = Now, PendingSince = Now,
            },
            new EfHatchChatMessage
            {
                LinkId = link.Id, WorkspaceId = acme.Id, Kind = "direct", Subject = "failure:2", Content = "{}",
                QueuedAt = Now, PendingSince = Now, DroppedAt = Now,
            });
        await h.Db.SaveChangesAsync();

        var row = Value(await h.Chat.GetDelivery(default)).Workspaces.Single(w => w.WorkspaceId == acme.Id);

        Assert.Equal(2, row.Waiting);
        Assert.Equal(1, row.Dropped);
    }

    [Fact]
    public async Task GetDelivery_WithNoPublicAddress_SaysLinksAreOff()
    {
        var h = NewHarness();

        Assert.False(Value(await h.Chat.GetDelivery(default)).LinksOn);
    }

    [Fact]
    public async Task GetDelivery_AScopedAdmin_CountsOnlyWhatIsAboutAProjectTheySee()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var a = await AddProjectAsync(h.Db, "AAA");
        var b = await AddProjectAsync(h.Db, "BBB");
        var room = await AddRoomAsync(h.Db, acme.Id);
        h.Db.ChatMessages.AddRange(
            Message(room, acme.Id, a, "issue:1"),
            Message(room, acme.Id, b, "issue:2"),
            Message(room, acme.Id, b, "issue:3", dropped: true));
        await h.Db.SaveChangesAsync();

        var everything = Value(await h.Chat.GetDelivery(default)).Workspaces.Single();
        var scoped = Value(await h.ScopedChat(new VisibleProjects(false, new HashSet<int> { a })).GetDelivery(default)).Workspaces.Single();

        Assert.Equal((2, 1), (everything.Waiting, everything.Dropped));
        Assert.Equal((1, 0), (scoped.Waiting, scoped.Dropped));
    }

    private static async Task<int> AddRoomAsync(HatchContext db, int workspaceId)
    {
        var room = new EfHatchChatRoom { WorkspaceId = workspaceId, ExternalId = "C1", Name = "general", CreatedAt = Now, UpdatedAt = Now };
        db.ChatRooms.Add(room);
        await db.SaveChangesAsync();

        return room.Id;
    }

    private static EfHatchChatMessage Message(int room, int workspace, int project, string subject, bool sent = false, bool dropped = false) => new()
    {
        RoomId = room, WorkspaceId = workspace, ProjectId = project, Kind = "changelog", Subject = subject,
        Content = "{}", QueuedAt = Now, PendingSince = Now,
        SentRevision = sent ? 1 : 0, DroppedAt = dropped ? Now : null,
    };

    [Fact]
    public async Task GetChannels_ListsFromTheProvider()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        h.Provider.Channels[AcmeToken] = [new ChatChannel("C1", "general", false, true)];

        var channels = Value(await h.Chat.GetChannels(acme.Id, default));

        Assert.Equal([new ChatChannelDto("C1", "general", false, true)], channels);
    }

    [Fact]
    public async Task GetChannels_AnUnknownWorkspace_IsNotFound()
    {
        var h = NewHarness();

        Assert.IsType<NotFoundResult>((await h.Chat.GetChannels(404, default)).Result);
    }

    [Fact]
    public async Task GetChannels_AProviderRefusal_Is502WithTheSentence()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        h.Provider.Workspaces.Remove(AcmeToken); // the fake refuses a token it does not know

        var result = await h.Chat.GetChannels(acme.Id, default);

        var bad = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, bad.StatusCode);
        Assert.Equal(FakeChatProvider.RefusalSentence, bad.Value);
    }

    [Fact]
    public async Task CreateRoom_SetsUpAChannel_AndItIsListed()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var a = await AddProjectAsync(h.Db, "AAA");
        Value(await h.Chat.PutProjects(acme.Id, new ChatProjectsRequest([a]), default));

        var room = Value(await h.Chat.CreateRoom(acme.Id, new ChatRoomCreateRequest(
            "C1", "general", Changelog: true, Questions: false, Problems: true, UnownedProblems: false,
            AllProjects: false, ProjectIds: [a]), default));

        Assert.Equal(acme.Id, room.WorkspaceId);
        Assert.Equal("C1", room.ChannelId);
        Assert.Equal("general", room.ChannelName);
        Assert.True(room.Changelog);
        Assert.False(room.Questions);
        Assert.True(room.Problems);
        Assert.False(room.AllProjects);
        Assert.Equal([a], room.ProjectIds);

        var rooms = Value(await h.Chat.GetRooms(acme.Id, default));
        AssertRoom(room, Assert.Single(rooms));
    }

    [Fact]
    public async Task CreateRoom_AChannelAlreadyARoom_IsAConflict()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        Value(await h.Chat.CreateRoom(acme.Id, NewRoomRequest("C1"), default));

        var result = await h.Chat.CreateRoom(acme.Id, NewRoomRequest("C1"), default);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("already a room", (string)conflict.Value!);
    }

    [Fact]
    public async Task CreateRoom_AChosenProjectNotBoundToTheWorkspace_IsBadRequest()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var a = await AddProjectAsync(h.Db, "AAA");

        var result = await h.Chat.CreateRoom(acme.Id, NewRoomRequest("C1") with { AllProjects = false, ProjectIds = [a] }, default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await h.Db.ChatRooms.ToListAsync());
    }

    [Fact]
    public async Task CreateRoom_AnUnknownWorkspace_IsNotFound()
    {
        var h = NewHarness();

        Assert.IsType<NotFoundResult>((await h.Chat.CreateRoom(404, NewRoomRequest("C1"), default)).Result);
    }

    [Fact]
    public async Task PatchRoom_ChangesSwitchesAllProjectsAndChosenProjects_LeavingOutAFieldLeavesItAlone()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var a = await AddProjectAsync(h.Db, "AAA");
        var b = await AddProjectAsync(h.Db, "BBB");
        Value(await h.Chat.PutProjects(acme.Id, new ChatProjectsRequest([a, b]), default));
        var room = Value(await h.Chat.CreateRoom(acme.Id, NewRoomRequest("C1") with { AllProjects = false, ProjectIds = [a] }, default));

        var patched = Value(await h.Chat.PatchRoom(room.Id, new ChatRoomPatchRequest(
            Changelog: true, Questions: null, Problems: null, UnownedProblems: null, AllProjects: null, ProjectIds: [a, b]), default));

        Assert.True(patched.Changelog);
        Assert.False(patched.AllProjects);
        Assert.Equal([a, b], patched.ProjectIds.Order());

        // Switching to every project clears what was chosen.
        var cleared = Value(await h.Chat.PatchRoom(room.Id, new ChatRoomPatchRequest(null, null, null, null, true, null), default));
        Assert.True(cleared.AllProjects);
        Assert.Empty(cleared.ProjectIds);
        Assert.Empty(await h.Db.ChatRoomProjects.Where(p => p.RoomId == room.Id).ToListAsync());
    }

    [Fact]
    public async Task PatchRoom_AChosenProjectNotBoundToTheWorkspace_IsBadRequest()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var room = Value(await h.Chat.CreateRoom(acme.Id, NewRoomRequest("C1"), default));
        var a = await AddProjectAsync(h.Db, "AAA");

        var result = await h.Chat.PatchRoom(room.Id, new ChatRoomPatchRequest(null, null, null, null, false, [a]), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("unknown project", refusal.Value);
    }

    [Fact]
    public async Task PatchRoom_AnUnknownRoom_IsNotFound()
    {
        var h = NewHarness();

        Assert.IsType<NotFoundResult>((await h.Chat.PatchRoom(404, new ChatRoomPatchRequest(null, null, null, null, null, null), default)).Result);
    }

    [Fact]
    public async Task RemoveRoom_DeletesIt_AndAnUnknownIdIsNotFound()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var room = Value(await h.Chat.CreateRoom(acme.Id, NewRoomRequest("C1"), default));

        Assert.IsType<NoContentResult>(await h.Chat.RemoveRoom(room.Id, default));
        Assert.IsType<NotFoundResult>(await h.Chat.RemoveRoom(room.Id, default));
        Assert.Empty(await h.Db.ChatRooms.ToListAsync());
    }

    [Fact]
    public async Task PostTestMessage_Delivered()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var room = Value(await h.Chat.CreateRoom(acme.Id, NewRoomRequest("C1"), default));

        var result = Value(await h.Chat.PostTestMessage(room.Id, default));

        Assert.True(result.Delivered);
        Assert.Null(result.Reason);
        var post = Assert.Single(h.Provider.Posts);
        Assert.Equal(AcmeToken, post.Token);
        Assert.Equal("C1", post.ChannelId);
        Assert.Contains("test message from Hatch", post.Text);
    }

    [Fact]
    public async Task PostTestMessage_ARefusal_Answers200WithDeliveredFalse()
    {
        var h = NewHarness();
        var acme = Value(await h.Chat.ConnectWorkspace(new ChatTokenRequest(AcmeToken), default));
        var room = Value(await h.Chat.CreateRoom(acme.Id, NewRoomRequest("C1"), default));
        h.Provider.PostRefusals["C1"] = "the fake refuses to post there";

        var result = Value(await h.Chat.PostTestMessage(room.Id, default));

        Assert.False(result.Delivered);
        Assert.Equal("the fake refuses to post there", result.Reason);
    }

    [Fact]
    public async Task PostTestMessage_AnUnknownRoom_IsNotFound()
    {
        var h = NewHarness();

        Assert.IsType<NotFoundResult>((await h.Chat.PostTestMessage(404, default)).Result);
    }

    private static ChatRoomCreateRequest NewRoomRequest(string channelId) =>
        new(channelId, "general", Changelog: false, Questions: false, Problems: false, UnownedProblems: false, AllProjects: true, ProjectIds: null);

    /// <summary>A record compares its list by reference, so compare the parts.</summary>
    private static void AssertWorkspace(ChatWorkspaceDto expected, ChatWorkspaceDto actual)
    {
        Assert.Equal(expected with { ProjectIds = [] }, actual with { ProjectIds = [] });
        Assert.Equal(expected.ProjectIds, actual.ProjectIds);
    }

    /// <summary>A record compares its list by reference, so compare the parts.</summary>
    private static void AssertRoom(ChatRoomDto expected, ChatRoomDto actual)
    {
        Assert.Equal(expected with { ProjectIds = [] }, actual with { ProjectIds = [] });
        Assert.Equal(expected.ProjectIds, actual.ProjectIds);
    }

    /// <summary>Every run of eight characters, so "any part of it" means a part long enough to be the token and not a prefix like <c>xoxb-</c>.</summary>
    private static IEnumerable<string> Parts(string token) =>
        Enumerable.Range(0, token.Length - 7).Select(i => token.Substring(i, 8));

    private static string Text(object? result) => result switch
    {
        ActionResult<ChatWorkspaceDto> a => Text(a.Result ?? (object?)a.Value),
        ActionResult<ChatManifestDto> a => Text(a.Result ?? (object?)a.Value),
        ActionResult<IReadOnlyList<ChatWorkspaceDto>> a => Text(a.Result ?? (object?)a.Value),
        ObjectResult o => Text(o.Value),
        string s => s,
        null => "",
        _ => JsonSerializer.Serialize(result, result.GetType()),
    };

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name}");

    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required ChatController Chat { get; init; }
        public required HatchContext Db { get; init; }
        public required FakeChatProvider Provider { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required CapturingLogger Log { get; init; }
        public required DbContextOptions<HatchContext> Options { get; init; }
        public required SecretProtector Protector { get; init; }
        public required ChatLinks Links { get; init; }

        /// <summary>A second controller over the same database, scoped as <paramref name="visible"/> sees it.</summary>
        public ChatController ScopedChat(VisibleProjects visible) =>
            new(ScopeSeed.Request(Options, visible), Provider, new StubCaller("Root"), Time, Protector, Links, Log)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };

        /// <summary>The plaintext of a stored token, opened for the Acme workspace's row.</summary>
        public string? Open(string stored) => Protector.Unprotect(stored, SecretStores.ChatWorkspace("fake", "TACME"))?.Reveal();
    }

    private static Harness NewHarness(bool withKey = true, string publicUrl = "")
    {
        var options = new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new HatchContext(options);
        var provider = new FakeChatProvider();
        var time = new FakeTimeProvider(Now);
        var log = new CapturingLogger();
        var links = new ChatLinks(Options.Create(new HatchOptions { PublicBaseUrl = publicUrl }));
        var protector = new SecretProtector(new SecretCipher(withKey ? RandomNumberGenerator.GetBytes(32) : null, null));

        return new Harness
        {
            Chat = new ChatController(db, provider, new StubCaller("Root"), time, protector, links, log)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            },
            Db = db,
            Provider = provider,
            Time = time,
            Log = log,
            Options = options,
            Protector = protector,
            Links = links,
        };
    }

    private static async Task<int> AddProjectAsync(HatchContext db, string key)
    {
        var project = new EfHatchProject { Key = key, Name = key, CreatedAt = Now };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    private sealed class CapturingLogger : ILogger<ChatController>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{formatter(state, exception)} {exception}");
    }

    private sealed class StubCaller(string name) : ICallerIdentity
    {
        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);
        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);
        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult<EfPerson?>(null);
        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult<EfApiKey?>(null);
        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult<Actor?>(null);
        public Task<bool> IsProgramAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<string> ActorNameAsync(CancellationToken ct) => Task.FromResult(name);
    }
}

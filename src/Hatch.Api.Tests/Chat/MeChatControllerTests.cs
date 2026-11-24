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
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// A person's own Slack links, wired against the in-memory database and the
/// fake chat provider, so nothing here calls Slack. The unique indexes are the
/// database's, so <see cref="MeChatDatabaseTests"/> holds them on Postgres.
/// </summary>
public class MeChatControllerTests
{
    private const string Token = FakeChatProvider.AcmeToken;
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    private static readonly ChatMember Ada = new("U-ADA", "Ada Lovelace", "ada");
    private static readonly ChatMember Bob = new("U-BOB", "Bob Hope", null);

    // ---- Seeing a workspace ----

    [Fact]
    public async Task TheList_ShowsEachWorkspace_WithNoLinkYet_AndSaysWhetherLinksAreOn()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");

        var answer = Value(await h.Ada.Get(default));

        Assert.True(answer.LinksOn);
        var row = Assert.Single(answer.Workspaces);
        Assert.Equal((acme, "Acme"), (row.Id, row.Name));
        Assert.Null(row.Link);
        Assert.False(Value(await New(publicUrl: "").Ada.Get(default)).LinksOn);
    }

    [Fact]
    public async Task ANarrowedPerson_IsShownOnlyTheWorkspacesBoundToAProjectTheySee_AndAnyWhereTheyHoldALink()
    {
        var h = New();
        var seen = await h.ProjectAsync("AAA");
        var hidden = await h.ProjectAsync("BBB");
        var open = await h.WorkspaceAsync("TOPEN", "Open", seen);
        var closed = await h.WorkspaceAsync("TCLOSED", "Closed", hidden);
        var held = await h.WorkspaceAsync("THELD", "Held", hidden);
        var narrowed = h.Narrowed(ScopeSeed.Only(seen));
        h.Db.ChatLinks.Add(WaitingLink(held, h.AdaId));
        await h.Db.SaveChangesAsync();

        var listed = Value(await narrowed.Get(default)).Workspaces.Select(w => w.Id).ToList();

        Assert.Equal([open, held], listed);
        Assert.DoesNotContain(closed, listed);
    }

    [Fact]
    public async Task ANarrowedPerson_CannotAsk_Suggest_List_OrLink_InAWorkspaceBoundOnlyOutOfScope()
    {
        var h = New();
        var seen = await h.ProjectAsync("AAA");
        var hidden = await h.ProjectAsync("BBB");
        var closed = await h.WorkspaceAsync("TCLOSED", "Closed", hidden);
        await h.WorkspaceAsync("TOPEN", "Open", seen);
        var narrowed = h.Narrowed(ScopeSeed.Only(seen));

        Assert.IsType<NotFoundResult>((await narrowed.GetSuggestion(closed, default)).Result);
        Assert.IsType<NotFoundResult>((await narrowed.GetMembers(closed, default)).Result);
        Assert.IsType<NotFoundResult>((await narrowed.PostLink(new MeChatLinkRequest(closed, Ada.Id), default)).Result);

        Assert.Empty(h.Provider.Lookups);
        Assert.Equal(0, h.Provider.MemberListReads);
        Assert.Empty(h.Provider.Opens);
        Assert.Empty(h.Provider.Sends);
        Assert.Empty(await h.Db.ChatLinks.ToListAsync());
    }

    [Fact]
    public async Task AWorkspaceThatIsGone_IsNotFound()
    {
        var h = New();

        Assert.IsType<NotFoundResult>((await h.Ada.GetMembers(99, default)).Result);
        Assert.IsType<NotFoundResult>((await h.Ada.PostLink(new MeChatLinkRequest(99, Ada.Id), default)).Result);
    }

    // ---- Choosing a member ----

    [Fact]
    public async Task AnEmailMatch_IsOffered_ByName()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.SignedInWith(h.AdaId, "ada@example.test", Now);
        h.Provider.Emails[Token] = new() { ["ada@example.test"] = Ada };

        var answer = Value(await h.Ada.GetSuggestion(acme, default));

        Assert.Equal(new MeChatMemberDto("U-ADA", "Ada Lovelace", "ada"), answer.Member);
    }

    [Fact]
    public async Task NoMatch_OffersNobody_AndTheListIsThereToPickFrom_WithNoEmailInIt()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.SignedInWith(h.AdaId, "ada@example.test", Now);
        h.Provider.Members[Token] = [Bob, Ada];

        Assert.Null(Value(await h.Ada.GetSuggestion(acme, default)).Member);

        var members = Value(await h.Ada.GetMembers(acme, default));
        Assert.Equal(["Ada Lovelace", "Bob Hope"], members.Select(m => m.Name));
        Assert.DoesNotContain("example.test", System.Text.Json.JsonSerializer.Serialize(members));
    }

    [Fact]
    public async Task TheNewestSignInIsTriedFirst_AndTheSearchStopsAtTheFirstMatch()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.SignedInWith(h.AdaId, "old@example.test", Now.AddDays(-30));
        h.SignedInWith(h.AdaId, "new@example.test", Now.AddDays(-1));
        h.SignedInWith(h.AdaId, "never@example.test", null);
        h.Provider.Emails[Token] = new() { ["new@example.test"] = Ada, ["old@example.test"] = Bob };

        var answer = Value(await h.Ada.GetSuggestion(acme, default));

        Assert.Equal("U-ADA", answer.Member!.Id);
        Assert.Equal(["new@example.test"], h.Provider.Lookups.Select(l => l.Email));
    }

    [Fact]
    public async Task AnAddressThatNeverSignedIn_IsNotTried_AndAnotherPersonsIsNot()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.SignedInWith(h.AdaId, "unclaimed@example.test", null, subject: false);
        h.SignedInWith(h.BobId, "bob@example.test", Now);
        h.Provider.Emails[Token] = new() { ["bob@example.test"] = Bob };

        Assert.Null(Value(await h.Ada.GetSuggestion(acme, default)).Member);
        Assert.Empty(h.Provider.Lookups);
    }

    [Fact]
    public async Task TheLocalPerson_HasNoSignInEmail_SoSlackIsNotAsked()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");

        Assert.Null(Value(await h.Ada.GetSuggestion(acme, default)).Member);

        Assert.Empty(h.Provider.Lookups);
        Assert.Equal(0, h.Provider.MemberListReads);
    }

    [Fact]
    public async Task ARefusalLookingUpAnEmail_IsA502WithSlacksSentence()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.SignedInWith(h.AdaId, "ada@example.test", Now);
        h.Provider.MemberRefusals[Token] = "the app is missing a permission it needs; install it again";

        var refused = Assert.IsType<ObjectResult>((await h.Ada.GetSuggestion(acme, default)).Result);

        Assert.Equal(502, refused.StatusCode);
        Assert.Equal("the app is missing a permission it needs; install it again", refused.Value);
        Assert.DoesNotContain("example.test", (string)refused.Value!);
    }

    [Fact]
    public async Task AnUnreadableToken_IsAConflict_AndNothingIsAsked()
    {
        var h = New();
        var acme = await h.TokenedWorkspaceAsync("TACME", "Acme", sealedToken: "not-a-sealed-value");
        h.SignedInWith(h.AdaId, "ada@example.test", Now);

        Assert.Equal(ChatController.TokenUnreadable, Assert.IsType<ConflictObjectResult>((await h.Ada.GetSuggestion(acme, default)).Result).Value);
        Assert.Equal(ChatController.TokenUnreadable, Assert.IsType<ConflictObjectResult>((await h.Ada.GetMembers(acme, default)).Result).Value);
        Assert.Equal(ChatController.TokenUnreadable, Assert.IsType<ConflictObjectResult>((await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default)).Result).Value);
        Assert.Empty(h.Provider.Lookups);
    }

    // ---- Linking ----

    [Fact]
    public async Task Linking_SendsOneMessage_ToTheOpenedConversation_AndKeepsAWaitingLink()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada, Bob];

        var link = Value(await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default));

        Assert.Equal(("waiting", "Ada Lovelace", Now.AddHours(24)), (link.State, link.MemberName, link.ExpiresAt));
        var row = await h.Db.ChatLinks.SingleAsync();
        Assert.Equal((acme, h.AdaId, "U-ADA", "D-U-ADA", Now, null), (row.WorkspaceId, row.PersonId, row.MemberId, row.ConversationId, row.CreatedAt, row.ConfirmedAt));
        Assert.NotNull(row.SecretHash);
        Assert.Equal(Now.AddHours(24), row.SecretExpiresAt);
        Assert.Equal([(Token, "U-ADA")], h.Provider.Opens);
        var sent = Assert.Single(h.Provider.Sends);
        Assert.Equal(("post", Token, "D-U-ADA"), (sent.Kind, sent.Token, sent.ChannelId));
        Assert.Single(await h.Db.ChatLinkSends.ToListAsync());
    }

    [Fact]
    public async Task TheMessage_HoldsTheHatchName_TheLinkBack_AndTheIgnoreLine()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];

        await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default);

        var content = Assert.Single(h.Provider.Sends).Content;
        Assert.Equal("Link your Slack account to Hatch", content.Title);
        Assert.Equal("Ada asked to link this Slack account to their Hatch account.", content.Body);
        Assert.Equal(["If that was not you, ignore this message. Nothing happens until the link is opened."], content.Lines);
        var link = Assert.Single(content.Links);
        Assert.Equal("Confirm the link", link.Label);
        Assert.StartsWith("https://hatch.example/settings?chatLink=", link.Url);
    }

    [Fact]
    public async Task TheSecret_IsInNoLogLine_AndNotInTheRow_OnlyItsHash()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];
        h.SignedInWith(h.AdaId, "ada@example.test", Now);

        var link = Value(await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default));
        var secret = SecretFrom(h.Provider);
        await h.Ada.ConfirmLink(new MeChatConfirmRequest(secret), default);
        await h.Ada.DeleteLink(link.Id, default);

        Assert.Equal(43, secret.Length);
        Assert.NotEmpty(h.Logs.Messages);
        Assert.DoesNotContain(h.Logs.Messages, line => line.Contains(secret) || line.Contains("example.test") || line.Contains("Confirm the link"));
        Assert.Contains(h.Logs.Messages, line => line.Contains($"Chat link {link.Id} in workspace {acme}"));
    }

    [Fact]
    public async Task TheStoredRow_HoldsTheHashOfTheSecretAndNeverTheSecret()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];

        await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default);
        var secret = SecretFrom(h.Provider);

        var row = await h.Db.ChatLinks.AsNoTracking().SingleAsync();
        Assert.Equal(AuthTokens.Hash(secret), row.SecretHash);
        Assert.DoesNotContain(secret, System.Text.Json.JsonSerializer.Serialize(row));
    }

    [Fact]
    public async Task ABotOrAnInventedMember_IsRefused_AndNothingIsOpenedOrSent()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];

        foreach (var id in new[] { "U-BOT", "USLACKBOT", "nobody" })
        {
            var refused = Assert.IsType<BadRequestObjectResult>((await h.Ada.PostLink(new MeChatLinkRequest(acme, id), default)).Result);
            Assert.Equal(MeChatController.NoSuchMember, refused.Value);
        }

        Assert.IsType<BadRequestObjectResult>((await h.Ada.PostLink(new MeChatLinkRequest(acme, "  "), default)).Result);
        Assert.Empty(h.Provider.Opens);
        Assert.Empty(h.Provider.Sends);
        Assert.Empty(await h.Db.ChatLinks.ToListAsync());
        Assert.Empty(await h.Db.ChatLinkSends.ToListAsync());
    }

    [Theory]
    [InlineData("list")]
    [InlineData("open")]
    [InlineData("send-refused")]
    [InlineData("send-throttled")]
    [InlineData("send-down")]
    public async Task ARefusalThrottleOrOutage_IsA502WithTheSentence_AndStoresNothing(string where)
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];
        const string Sentence = "Slack said no, in a plain sentence";
        switch (where)
        {
            case "list": h.Provider.MemberRefusals[Token] = Sentence; break;
            case "open": h.Provider.OpenRefusals[Ada.Id] = Sentence; break;
            case "send-refused": h.Provider.Script = _ => ChatSendResult.Failed(ChatSendOutcome.RoomRefused, Sentence); break;
            case "send-throttled": h.Provider.Script = _ => ChatSendResult.Throttled(TimeSpan.FromSeconds(30), Sentence); break;
            case "send-down": h.Provider.Script = _ => ChatSendResult.Failed(ChatSendOutcome.Unreachable, Sentence); break;
        }

        var refused = Assert.IsType<ObjectResult>((await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default)).Result);

        Assert.Equal(502, refused.StatusCode);
        Assert.Equal(Sentence, refused.Value);
        Assert.Empty(await h.Db.ChatLinks.ToListAsync());
        Assert.Empty(await h.Db.ChatLinkSends.ToListAsync());
    }

    [Fact]
    public async Task WhereThereIsNoPublicAddress_LinkingIsAConflictNamingTheSetting_AndCallsNothing()
    {
        var h = New(publicUrl: "");
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];

        var refused = Assert.IsType<ConflictObjectResult>((await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default)).Result);

        Assert.Contains("HATCH_PUBLIC_URL", (string)refused.Value!);
        Assert.Equal(0, h.Provider.MemberListReads);
        Assert.Empty(h.Provider.Opens);
        Assert.Empty(h.Provider.Sends);
    }

    [Fact]
    public async Task LinkingAgain_ReplacesTheWaitingLink_SendsAFreshMessage_AndKillsTheOldSecret()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada, Bob];
        var first = Value(await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default));
        var firstSecret = SecretFrom(h.Provider);

        h.Time.Advance(TimeSpan.FromMinutes(5));
        var second = Value(await h.Ada.PostLink(new MeChatLinkRequest(acme, Bob.Id), default));
        var secondSecret = SecretFrom(h.Provider);

        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(firstSecret, secondSecret);
        var row = await h.Db.ChatLinks.SingleAsync();
        Assert.Equal(("U-BOB", "Bob Hope", Now.AddMinutes(5)), (row.MemberId, row.MemberName, row.CreatedAt));
        Assert.Equal(Now.AddMinutes(5).AddHours(24), row.SecretExpiresAt);
        Assert.Equal(2, h.Provider.Sends.Count);

        Assert.Equal(410, Assert.IsType<ObjectResult>((await h.Ada.ConfirmLink(new MeChatConfirmRequest(firstSecret), default)).Result).StatusCode);
        Assert.Equal("linked", Value(await h.Ada.ConfirmLink(new MeChatConfirmRequest(secondSecret), default)).State);
    }

    [Fact]
    public async Task AConfirmedLink_IsNotReplaced_UnlinkFirst()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada, Bob];
        await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default);
        await h.Ada.ConfirmLink(new MeChatConfirmRequest(SecretFrom(h.Provider)), default);

        var refused = Assert.IsType<ConflictObjectResult>((await h.Ada.PostLink(new MeChatLinkRequest(acme, Bob.Id), default)).Result);

        Assert.Equal(MeChatController.AlreadyLinked, refused.Value);
        Assert.Single(h.Provider.Sends);
        Assert.Equal("U-ADA", (await h.Db.ChatLinks.SingleAsync()).MemberId);
    }

    [Fact]
    public async Task AMemberConfirmedForSomebodyElse_IsRefused_AndNothingIsSent()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];
        await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default);
        await h.Ada.ConfirmLink(new MeChatConfirmRequest(SecretFrom(h.Provider)), default);
        h.Provider.Sends.Clear();
        h.Provider.Opens.Clear();

        var refused = Assert.IsType<ConflictObjectResult>((await h.Bob.PostLink(new MeChatLinkRequest(acme, Ada.Id), default)).Result);

        Assert.Equal(MeChatController.MemberTaken, refused.Value);
        Assert.Contains("admin", (string)refused.Value!);
        Assert.Empty(h.Provider.Sends);
        Assert.Empty(h.Provider.Opens);
        Assert.Single(await h.Db.ChatLinks.ToListAsync());
    }

    [Fact]
    public async Task TheSixthMessageInAnHour_IsRefused_AndTheWindowRollsOn()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];

        for (var i = 0; i < 5; i++)
        {
            Value(await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default));
            h.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var sixth = Assert.IsType<ObjectResult>((await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default)).Result);

        Assert.Equal(429, sixth.StatusCode);
        Assert.Equal(5, h.Provider.Sends.Count);
        Assert.Equal(5, await h.Db.ChatLinkSends.CountAsync());

        // Only the first leaves the hour; a second message is allowed, then the limit holds again.
        h.Time.Advance(TimeSpan.FromSeconds(55 * 60 + 30));
        Value(await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default));
        Assert.Equal(429, Assert.IsType<ObjectResult>((await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default)).Result).StatusCode);
    }

    [Fact]
    public async Task TheLimitIsPerPerson_AndOldSendRowsAreDeletedOnTheNextWrite()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];
        for (var i = 0; i < 5; i++) h.Db.ChatLinkSends.Add(new EfHatchChatLinkSend { PersonId = h.BobId, SentAt = Now.AddMinutes(-10) });
        h.Db.ChatLinkSends.Add(new EfHatchChatLinkSend { PersonId = h.BobId, SentAt = Now.AddHours(-2) });
        await h.Db.SaveChangesAsync();

        Value(await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default));

        Assert.Equal(5, await h.Db.ChatLinkSends.CountAsync(s => s.PersonId == h.BobId));
        Assert.Equal(1, await h.Db.ChatLinkSends.CountAsync(s => s.PersonId == h.AdaId));
    }

    // ---- Confirming ----

    [Fact]
    public async Task TheRightPerson_Confirms_AndTheSecretIsClearedSoItFindsNoRow()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];
        await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default);
        var secret = SecretFrom(h.Provider);

        h.Time.Advance(TimeSpan.FromHours(2));
        var linked = Value(await h.Ada.ConfirmLink(new MeChatConfirmRequest(secret), default));

        Assert.Equal(("linked", null), (linked.State, linked.ExpiresAt));
        var row = await h.Db.ChatLinks.AsNoTracking().SingleAsync();
        Assert.Equal(Now.AddHours(2), row.ConfirmedAt);
        Assert.Null(row.SecretHash);
        Assert.Null(row.SecretExpiresAt);

        // Used: the same secret finds no row.
        Assert.Equal(410, Assert.IsType<ObjectResult>((await h.Ada.ConfirmLink(new MeChatConfirmRequest(secret), default)).Result).StatusCode);
    }

    [Fact]
    public async Task ADifferentPerson_ConfirmsNothing_AndTheRowIsLeftAsItWas()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];
        await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default);
        var secret = SecretFrom(h.Provider);
        var before = await h.Db.ChatLinks.AsNoTracking().SingleAsync();

        var refused = Assert.IsType<ObjectResult>((await h.Bob.ConfirmLink(new MeChatConfirmRequest(secret), default)).Result);

        Assert.Equal(403, refused.StatusCode);
        Assert.Equal(MeChatController.SomebodyElses, refused.Value);
        var after = await h.Db.ChatLinks.AsNoTracking().SingleAsync();
        Assert.Null(after.ConfirmedAt);
        Assert.Equal(before.SecretHash, after.SecretHash);
        Assert.Equal(before.SecretExpiresAt, after.SecretExpiresAt);

        // And the person it was for can still use it.
        Assert.Equal("linked", Value(await h.Ada.ConfirmLink(new MeChatConfirmRequest(secret), default)).State);
    }

    [Fact]
    public async Task ASecretThatRanOut_IsGone_ThoughItWasNeverUsed()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];
        await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default);
        var secret = SecretFrom(h.Provider);

        h.Time.Advance(TimeSpan.FromHours(24));
        var gone = Assert.IsType<ObjectResult>((await h.Ada.ConfirmLink(new MeChatConfirmRequest(secret), default)).Result);

        Assert.Equal(410, gone.StatusCode);
        Assert.Equal(MeChatController.SecretGone, gone.Value);
        Assert.Null((await h.Db.ChatLinks.AsNoTracking().SingleAsync()).ConfirmedAt);
    }

    [Fact]
    public async Task ABlankOrInventedSecret_IsGone()
    {
        var h = New();

        foreach (var secret in new string?[] { null, "", "  ", "not-a-secret" })
            Assert.Equal(410, Assert.IsType<ObjectResult>((await h.Ada.ConfirmLink(new MeChatConfirmRequest(secret), default)).Result).StatusCode);
    }

    [Fact]
    public void ConfirmingIsAPost_AndNoGetActionTakesASecret()
    {
        var confirms = typeof(MeChatController).GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>().Any(a => a.Template == "links/confirm"));
        Assert.Single(confirms);

        var gets = typeof(MeChatController).GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(HttpGetAttribute), false).Length > 0);
        Assert.All(gets, m => Assert.DoesNotContain(m.GetParameters(), p => p.Name!.Contains("secret", StringComparison.OrdinalIgnoreCase)));
    }

    // ---- A waiting link ----

    [Fact]
    public async Task AWaitingLink_IsReachedByNothingButItsOwnMessage_AndConfirmedLeavesItOut()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada, Bob];
        await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default);
        await h.Bob.PostLink(new MeChatLinkRequest(acme, Bob.Id), default);
        await h.Bob.ConfirmLink(new MeChatConfirmRequest(SecretFrom(h.Provider)), default);

        // The only post is each confirmation message; nothing else was sent to a waiting link's conversation.
        Assert.Equal(["D-U-ADA", "D-U-BOB"], h.Provider.Sends.Select(s => s.ChannelId));
        Assert.Equal(2, h.Provider.Sends.Count);

        var confirmed = await h.Db.ChatLinks.Confirmed().ToListAsync();
        Assert.Equal(["U-BOB"], confirmed.Select(l => l.MemberId));
    }

    [Fact]
    public async Task TheList_ShowsWaitingAndLinked_WithTheNameAndExpiry()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        var globex = await h.TokenedWorkspaceAsync("TGLOBEX", "Globex", token: FakeChatProvider.GlobexToken);
        h.Provider.Members[Token] = [Ada];
        h.Provider.Members[FakeChatProvider.GlobexToken] = [Ada];
        await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default);
        await h.Ada.PostLink(new MeChatLinkRequest(globex, Ada.Id), default);
        await h.Ada.ConfirmLink(new MeChatConfirmRequest(SecretFrom(h.Provider)), default);

        var rows = Value(await h.Ada.Get(default)).Workspaces;

        Assert.Equal("waiting", rows[0].Link!.State);
        Assert.Equal(Now.AddHours(24), rows[0].Link!.ExpiresAt);
        Assert.Equal(("linked", "Ada Lovelace", null), (rows[1].Link!.State, rows[1].Link!.MemberName, rows[1].Link!.ExpiresAt));
        Assert.Null(Value(await h.Bob.Get(default)).Workspaces[0].Link);
    }

    // ---- What a link is for ----

    [Fact]
    public async Task ALinkIsOnForRunnerFailures_ByDefault_AndTheSwitchIsReadAndWritten()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];
        var waiting = Value(await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default));
        Assert.True(waiting.RunnerFailures);
        Assert.Equal((null, null), (waiting.LastRefusal, waiting.LastRefusalAt));
        await h.Ada.ConfirmLink(new MeChatConfirmRequest(SecretFrom(h.Provider)), default);

        var off = Value(await h.Ada.PutLink(waiting.Id, new MeChatLinkPatchRequest(false), default));

        Assert.Equal(("linked", false), (off.State, off.RunnerFailures));
        Assert.False((await h.Db.ChatLinks.SingleAsync()).RunnerFailures);
        var listed = Assert.Single(Value(await h.Ada.Get(default)).Workspaces).Link!;
        Assert.False(listed.RunnerFailures);

        var on = Value(await h.Ada.PutLink(waiting.Id, new MeChatLinkPatchRequest(true), default));
        Assert.True(on.RunnerFailures);
    }

    [Fact]
    public async Task AFieldLeftOut_LeavesTheSwitchAlone_AndALastRefusalIsShown()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Db.ChatLinks.Add(WaitingLink(acme, h.AdaId));
        await h.Db.SaveChangesAsync();
        var row = await h.Db.ChatLinks.SingleAsync();
        row.ConfirmedAt = Now;
        row.RunnerFailures = false;
        row.LastRefusal = "cannot_dm_bot";
        row.LastRefusalAt = Now;
        await h.Db.SaveChangesAsync();

        var answer = Value(await h.Ada.PutLink(row.Id, new MeChatLinkPatchRequest(null), default));

        Assert.Equal((false, "cannot_dm_bot", (DateTimeOffset?)Now), (answer.RunnerFailures, answer.LastRefusal, answer.LastRefusalAt));
        Assert.False((await h.Db.ChatLinks.SingleAsync()).RunnerFailures);
    }

    [Fact]
    public async Task TheSwitch_IsTheCallersOwnToSet_AndAWaitingLinkTakesNone()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Db.ChatLinks.Add(WaitingLink(acme, h.AdaId));
        await h.Db.SaveChangesAsync();
        var link = await h.Db.ChatLinks.SingleAsync();

        Assert.IsType<NotFoundResult>((await h.Bob.PutLink(link.Id, new MeChatLinkPatchRequest(false), default)).Result);
        Assert.IsType<NotFoundResult>((await h.Ada.PutLink(link.Id + 100, new MeChatLinkPatchRequest(false), default)).Result);
        Assert.IsType<ConflictObjectResult>((await h.Ada.PutLink(link.Id, new MeChatLinkPatchRequest(false), default)).Result);

        Assert.True((await h.Db.ChatLinks.SingleAsync()).RunnerFailures);
    }

    // ---- A removed person ----

    [Fact]
    public async Task AfterThePersonHoldingAMemberIsRemoved_ASecondPersonLinksThatMemberAtOnce()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];
        await h.Bob.PostLink(new MeChatLinkRequest(acme, Ada.Id), default);
        await h.Bob.ConfirmLink(new MeChatConfirmRequest(SecretFrom(h.Provider)), default);
        Assert.IsType<ConflictObjectResult>((await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default)).Result);

        await h.RemovePersonAsync(h.BobId);

        var posted = Value(await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default));
        Assert.Equal("waiting", posted.State);
        Assert.Equal("linked", Value(await h.Ada.ConfirmLink(new MeChatConfirmRequest(SecretFrom(h.Provider)), default)).State);
        Assert.Equal([h.AdaId], (await h.Db.ChatLinks.ToListAsync()).Select(l => l.PersonId));
    }

    [Fact]
    public async Task AfterThePersonHoldingAMemberIsRemoved_ASecondPersonWaitingOnThemConfirmsAtOnce()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];
        await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default);
        var adaSecret = SecretFrom(h.Provider);
        await h.Bob.PostLink(new MeChatLinkRequest(acme, Ada.Id), default);
        await h.Bob.ConfirmLink(new MeChatConfirmRequest(SecretFrom(h.Provider)), default);
        await h.RemovePersonAsync(h.BobId);

        Assert.Equal("linked", Value(await h.Ada.ConfirmLink(new MeChatConfirmRequest(adaSecret), default)).State);
        Assert.Equal([h.AdaId], (await h.Db.ChatLinks.ToListAsync()).Select(l => l.PersonId));
    }

    // ---- Unlinking ----

    [Fact]
    public async Task Unlinking_RemovesTheCallersOwnLink_WaitingOrConfirmed()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        var globex = await h.TokenedWorkspaceAsync("TGLOBEX", "Globex", token: FakeChatProvider.GlobexToken);
        h.Provider.Members[Token] = [Ada];
        h.Provider.Members[FakeChatProvider.GlobexToken] = [Ada];
        var waiting = Value(await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default));
        var confirmed = Value(await h.Ada.PostLink(new MeChatLinkRequest(globex, Ada.Id), default));
        await h.Ada.ConfirmLink(new MeChatConfirmRequest(SecretFrom(h.Provider)), default);

        Assert.IsType<NoContentResult>(await h.Ada.DeleteLink(waiting.Id, default));
        Assert.IsType<NoContentResult>(await h.Ada.DeleteLink(confirmed.Id, default));

        Assert.Empty(await h.Db.ChatLinks.ToListAsync());
    }

    [Fact]
    public async Task AnotherPersonsLink_IsNotFound_AndStays()
    {
        var h = New();
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];
        var link = Value(await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default));

        Assert.IsType<NotFoundResult>(await h.Bob.DeleteLink(link.Id, default));
        Assert.IsType<NotFoundResult>(await h.Ada.DeleteLink(link.Id + 100, default));

        Assert.Single(await h.Db.ChatLinks.ToListAsync());
    }

    [Fact]
    public async Task ALinkIsRemovable_EvenWhenItsWorkspaceHasLeftTheCallersScope()
    {
        var h = New();
        var seen = await h.ProjectAsync("AAA");
        var hidden = await h.ProjectAsync("BBB");
        var acme = await h.WorkspaceAsync("TACME", "Acme", hidden);
        h.Db.ChatLinks.Add(WaitingLink(acme, h.AdaId));
        await h.Db.SaveChangesAsync();
        var link = await h.Db.ChatLinks.SingleAsync();

        Assert.IsType<NoContentResult>(await h.Narrowed(ScopeSeed.Only(seen)).DeleteLink(link.Id, default));

        Assert.Empty(await h.Db.ChatLinks.ToListAsync());
    }

    // ---- Who may ----

    [Fact]
    public async Task AKey_IsRefusedOnEveryAction_BeforeAnythingIsReadOrAsked()
    {
        var h = New(program: true);
        var acme = await h.WorkspaceAsync("TACME", "Acme");
        h.Provider.Members[Token] = [Ada];

        var refusals = new ObjectResult?[]
        {
            (await h.Ada.Get(default)).Result as ObjectResult,
            (await h.Ada.GetSuggestion(acme, default)).Result as ObjectResult,
            (await h.Ada.GetMembers(acme, default)).Result as ObjectResult,
            (await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default)).Result as ObjectResult,
            (await h.Ada.ConfirmLink(new MeChatConfirmRequest("x"), default)).Result as ObjectResult,
            (await h.Ada.PutLink(1, new MeChatLinkPatchRequest(false), default)).Result as ObjectResult,
            await h.Ada.DeleteLink(1, default) as ObjectResult,
        };

        Assert.All(refusals, r =>
        {
            Assert.Equal(403, r!.StatusCode);
            Assert.Equal(MeChatController.NotAnAgent, r.Value);
        });
        Assert.Equal(0, h.Provider.MemberListReads);
        Assert.Empty(h.Provider.Lookups);
        Assert.Empty(h.Provider.Sends);
    }

    [Fact]
    public async Task ACallerTheDirectoryNamesNobodyFor_IsRefused_OnEveryAction()
    {
        var h = New(nobody: true);
        var acme = await h.WorkspaceAsync("TACME", "Acme");

        var refusals = new ObjectResult?[]
        {
            (await h.Ada.Get(default)).Result as ObjectResult,
            (await h.Ada.GetSuggestion(acme, default)).Result as ObjectResult,
            (await h.Ada.GetMembers(acme, default)).Result as ObjectResult,
            (await h.Ada.PostLink(new MeChatLinkRequest(acme, Ada.Id), default)).Result as ObjectResult,
            (await h.Ada.ConfirmLink(new MeChatConfirmRequest("x"), default)).Result as ObjectResult,
            (await h.Ada.PutLink(1, new MeChatLinkPatchRequest(false), default)).Result as ObjectResult,
            await h.Ada.DeleteLink(1, default) as ObjectResult,
        };

        Assert.All(refusals, r =>
        {
            Assert.Equal(403, r!.StatusCode);
            Assert.Equal(MeChatController.NeedsSomebody, r.Value);
        });
    }

    // ---- The link back ----

    [Fact]
    public void TheLinkBack_SurvivesSignInAndTheReturnCheck_UnderTheLengthTheSignInFlowKeeps()
    {
        var links = new ChatLinks(Options.Create(new HatchOptions { PublicBaseUrl = "https://hatch.example" }));
        var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        var url = new Uri(links.LinkBack(secret)!);
        var back = url.PathAndQuery;

        Assert.Equal($"/settings?chatLink={secret}", back);
        Assert.Equal(back, AuthChallenge.SafeReturnTo(back));
        Assert.True(back.Length < 512, $"{back.Length} characters is more than the sign-in flow keeps");

        var location = AuthChallenge.SignInLocation(new AuthOptions(), back);
        var returned = QueryHelpers.ParseQuery(new Uri("https://hatch.example" + location).Query)["r"].ToString();
        Assert.Equal(back, returned);
    }

    [Fact]
    public void TheLinkBack_IsNullWhereThereIsNoAddress()
    {
        Assert.Null(new ChatLinks(Options.Create(new HatchOptions { PublicBaseUrl = "" })).LinkBack("secret"));
    }

    // ---- Harness ----

    private static EfHatchChatLink WaitingLink(int workspaceId, Guid personId) => new()
    {
        WorkspaceId = workspaceId, PersonId = personId, MemberId = "U-ADA", MemberName = "Ada Lovelace", ConversationId = "D1",
        CreatedAt = Now, SecretHash = RandomNumberGenerator.GetBytes(AuthHash.Length), SecretExpiresAt = Now.AddHours(24),
    };

    /// <summary>The secret out of the link in the last message sent, the way the person would read it.</summary>
    private static string SecretFrom(FakeChatProvider provider) =>
        QueryHelpers.ParseQuery(new Uri(provider.Sends[^1].Content.Links[0].Url!).Query)["chatLink"].ToString();

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {(result.Result as ObjectResult)?.StatusCode} {(result.Result as ObjectResult)?.Value ?? result.Result?.GetType().Name}");

    private static Harness New(string publicUrl = "https://hatch.example", bool program = false, bool nobody = false) =>
        new(publicUrl, program, nobody);

    private sealed class Harness
    {
        private readonly DbContextOptions<HatchContext> options =
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        private readonly AppDbContext appDb =
            new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private readonly SecretProtector protector = new(new SecretCipher(RandomNumberGenerator.GetBytes(32), null));
        private readonly ChatLinks links;
        private readonly bool program;
        private readonly bool nobody;

        public Harness(string publicUrl, bool program, bool nobody)
        {
            this.program = program;
            this.nobody = nobody;
            links = new ChatLinks(Options.Create(new HatchOptions { PublicBaseUrl = publicUrl }));
            Db = new HatchContext(options);
            Time = new FakeTimeProvider(Now);
            Logs = new CapturedLogs();
            // Both are people, as a real caller is: a link whose person is not in the
            // table counts for nothing.
            foreach (var (id, name) in new[] { (AdaId, "Ada"), (BobId, "Bob") })
                appDb.People.Add(new EfPerson { Id = id, Name = name, Role = PersonRole.User, CreatedAt = Now, UpdatedAt = Now });
            appDb.SaveChanges();

            Ada = Controller(Db, AdaId, "Ada", VisibleProjects.Everything);
            Bob = Controller(Db, BobId, "Bob", VisibleProjects.Everything);
        }

        public Guid AdaId { get; } = Guid.NewGuid();
        public Guid BobId { get; } = Guid.NewGuid();
        public HatchContext Db { get; }
        public FakeTimeProvider Time { get; }
        public FakeChatProvider Provider { get; } = new();
        public CapturedLogs Logs { get; }
        public MeChatController Ada { get; }
        public MeChatController Bob { get; }

        /// <summary>Removes a person the way <c>PeopleController</c> does: their row goes, and nothing here follows.</summary>
        public async Task RemovePersonAsync(Guid id)
        {
            appDb.People.Remove(await appDb.People.SingleAsync(p => p.Id == id));
            await appDb.SaveChangesAsync();
        }

        /// <summary>Ada, as a request whose scope resolved to <paramref name="visible"/> sees the data.</summary>
        public MeChatController Narrowed(VisibleProjects visible) =>
            Controller(ScopeSeed.Request(options, visible), AdaId, "Ada", visible);

        private MeChatController Controller(HatchContext db, Guid id, string name, VisibleProjects visible)
        {
            var person = new EfPerson { Id = id, Name = name, Role = PersonRole.User, CreatedAt = Now, UpdatedAt = Now };
            var actors = new StubActorDirectory { Principal = nobody ? null : new Actor(ActorKind.Person, id, name) };
            var key = program ? new EfApiKey { Name = "hatch", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now } : null;

            return new MeChatController(
                db, appDb, Provider, new MeScopeTests.StubCaller(null, key is null ? person : null, key), actors,
                new MeScopeTests.FixedScope(visible), protector, links, RosterSeed.For(db, appDb), Time,
                new LoggerFactory([Logs]).CreateLogger<MeChatController>())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
        }

        public async Task<int> ProjectAsync(string key)
        {
            var project = new EfHatchProject { Key = key, Name = key, CreatedAt = Now };
            Db.Projects.Add(project);
            await Db.SaveChangesAsync();
            return project.Id;
        }

        public Task<int> WorkspaceAsync(string externalId, string name, params int[] bound) =>
            TokenedWorkspaceAsync(externalId, name, bound: bound);

        public async Task<int> TokenedWorkspaceAsync(
            string externalId, string name, string? sealedToken = null, string token = Token, int[]? bound = null)
        {
            if (sealedToken is null && !protector.TrySeal(token, SecretStores.ChatWorkspace("fake", externalId), out sealedToken))
                throw new InvalidOperationException("the test protector could not seal");

            var workspace = new EfHatchChatWorkspace
            {
                Provider = "fake", ExternalId = externalId, Name = name, Token = sealedToken, CreatedAt = Now.AddSeconds(Db.ChatWorkspaces.Count()), UpdatedAt = Now,
            };
            Db.ChatWorkspaces.Add(workspace);
            await Db.SaveChangesAsync();

            foreach (var project in bound ?? [])
                Db.ChatWorkspaceProjects.Add(new EfHatchChatWorkspaceProject { WorkspaceId = workspace.Id, ProjectId = project, CreatedAt = Now });
            await Db.SaveChangesAsync();

            return workspace.Id;
        }

        /// <summary>An address the person has signed in with, newest by <paramref name="lastSignIn"/>.</summary>
        public void SignedInWith(Guid personId, string email, DateTimeOffset? lastSignIn, bool subject = true)
        {
            appDb.ExternalIdentities.Add(new EfExternalIdentity
            {
                Provider = EfExternalIdentity.GoogleProvider, Subject = subject ? Guid.NewGuid().ToString() : null,
                Email = email, PersonId = personId, CreatedAt = Now.AddYears(-1), LastSignInAt = lastSignIn,
            });
            appDb.SaveChanges();
        }
    }
}

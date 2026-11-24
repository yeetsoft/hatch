using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Hatch;
using Microsoft.Extensions.Logging;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// The three calls that find a person in Slack and open a conversation with them,
/// against a stub that answers as Slack does. The email travels in a POST body,
/// never a URL; no email, token or secret is in a sentence or a log line; and a
/// member list never carries an email back out.
/// </summary>
public class SlackChatProviderMemberTests
{
    private const string Token = "xoxb-1234-secret-value";
    private const string Email = "ada.private@example.test";

    // ---- users.lookupByEmail ----

    [Fact]
    public async Task TheEmail_GoesInAPostBody_NeverTheUrl_AndTheTokenOnlyInTheHeader()
    {
        var handler = Stub(Ok("""{"ok":true,"user":{"id":"U1","real_name":"Ada Lovelace"}}"""));

        await NewProvider(handler, out var logs).FindMemberByEmailAsync(Token, Email, default);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://slack.com/api/users.lookupByEmail", request.Url);
        Assert.Equal(new Dictionary<string, string> { ["email"] = Email }, handler.LastForm);
        Assert.Equal($"Bearer {Token}", Assert.Single(handler.AuthorizationHeaders));
        Assert.DoesNotContain("example.test", request.Url);
        Assert.DoesNotContain("secret-value", request.Url + request.Body);
        AssertNothingLeaked(logs);
    }

    [Fact]
    public async Task AFoundMember_IsReturnedWithItsIdNameAndDisplayName()
    {
        var handler = Stub(Ok("""{"ok":true,"user":{"id":"U1","name":"ada","real_name":"Ada Lovelace","profile":{"display_name":"ada l","email":"ada.private@example.test"}}}"""));

        var found = await NewProvider(handler, out _).FindMemberByEmailAsync(Token, Email, default);

        Assert.Equal(new ChatMember("U1", "Ada Lovelace", "ada l"), found.Member);
        Assert.Null(found.Refusal);
        Assert.DoesNotContain("example.test", JsonSerializer.Serialize(found));
    }

    [Fact]
    public async Task NoMemberForTheEmail_IsANullMember_NotARefusal()
    {
        var handler = Stub(Ok("""{"ok":false,"error":"users_not_found"}"""));

        var found = await NewProvider(handler, out var logs).FindMemberByEmailAsync(Token, Email, default);

        Assert.Null(found.Member);
        Assert.Null(found.Refusal);
        AssertNothingLeaked(logs);
    }

    [Theory]
    [InlineData("""{"ok":true,"user":{"id":"U1","real_name":"Gone","deleted":true}}""")]
    [InlineData("""{"ok":true,"user":{"id":"U1","real_name":"A Bot","is_bot":true}}""")]
    [InlineData("""{"ok":true,"user":{"id":"USLACKBOT","real_name":"Slackbot"}}""")]
    public async Task ADeactivatedMemberOrABot_IsNotOffered(string body)
    {
        var found = await NewProvider(Stub(Ok(body)), out _).FindMemberByEmailAsync(Token, Email, default);

        Assert.Null(found.Member);
        Assert.Null(found.Refusal);
    }

    [Theory]
    [InlineData("missing_scope", "permission")]
    [InlineData("invalid_auth", "did not accept")]
    [InlineData("something_new", "Slack refused: something_new")]
    public async Task ARefusalLookingUpAnEmail_IsASentence_WithNoEmailOrTokenInIt(string code, string fragment)
    {
        var handler = Stub(Ok($$"""{"ok":false,"error":"{{code}}"}"""));

        var found = await NewProvider(handler, out var logs).FindMemberByEmailAsync(Token, Email, default);

        Assert.Null(found.Member);
        Assert.Contains(fragment, found.Refusal);
        Assert.DoesNotContain("example.test", found.Refusal);
        Assert.DoesNotContain("xoxb", found.Refusal);
        AssertNothingLeaked(logs);
    }

    // ---- users.list ----

    [Fact]
    public async Task TheList_AsksForTwoHundred_WithTheTokenInTheHeader()
    {
        var handler = Stub(Ok("""{"ok":true,"members":[]}"""));

        var list = await NewProvider(handler, out _).ListMembersAsync(Token, default);

        Assert.Empty(list.Members!);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("https://slack.com/api/users.list?limit=200", request.Url);
        Assert.Equal($"Bearer {Token}", Assert.Single(handler.AuthorizationHeaders));
    }

    [Fact]
    public async Task TheList_LeavesOutDeactivatedMembersBotsAppUsersAndSlackbot()
    {
        var handler = Stub(Ok("""
            {"ok":true,"members":[
              {"id":"U1","real_name":"Ada Lovelace","profile":{"display_name":"ada"}},
              {"id":"U2","real_name":"Gone Gary","deleted":true},
              {"id":"U3","real_name":"Robo","is_bot":true},
              {"id":"U4","real_name":"App User","is_app_user":true},
              {"id":"USLACKBOT","real_name":"Slackbot","is_bot":false},
              {"id":"U5","real_name":"Bob Hope","deleted":false,"is_bot":false,"is_app_user":false}
            ]}
            """));

        var list = await NewProvider(handler, out _).ListMembersAsync(Token, default);

        Assert.Equal(["U1", "U5"], list.Members!.Select(m => m.Id));
    }

    [Fact]
    public async Task TheName_IsRealName_ElseTheProfilesRealName_ElseTheHandle_AndTheListNeverCarriesAnEmail()
    {
        var handler = Stub(Ok("""
            {"ok":true,"members":[
              {"id":"U1","name":"h1","real_name":"First Name","profile":{"real_name":"Profile First","display_name":"one","email":"one.private@example.test"}},
              {"id":"U2","name":"h2","profile":{"real_name":"Profile Second","display_name":"","email":"two.private@example.test"}},
              {"id":"U3","name":"h3","profile":{"email":"three.private@example.test"}}
            ]}
            """));

        var list = await NewProvider(handler, out _).ListMembersAsync(Token, default);

        Assert.Equal(
            [new ChatMember("U1", "First Name", "one"), new ChatMember("U2", "Profile Second", null), new ChatMember("U3", "h3", null)],
            list.Members);
        Assert.DoesNotContain("example.test", JsonSerializer.Serialize(list));
    }

    [Fact]
    public async Task EveryPageIsFollowed_ByItsCursor_UntilThereIsNone()
    {
        var handler = Stub(
            Ok("""{"ok":true,"members":[{"id":"U1","real_name":"A"}],"response_metadata":{"next_cursor":"dXNlcjpV/1="}}"""),
            Ok("""{"ok":true,"members":[{"id":"U2","real_name":"B"}],"response_metadata":{"next_cursor":"page3"}}"""),
            Ok("""{"ok":true,"members":[{"id":"U3","real_name":"C"}],"response_metadata":{"next_cursor":""}}"""));

        var list = await NewProvider(handler, out _).ListMembersAsync(Token, default);

        Assert.Equal(["U1", "U2", "U3"], list.Members!.Select(m => m.Id));
        Assert.Equal(
            ["https://slack.com/api/users.list?limit=200",
             "https://slack.com/api/users.list?limit=200&cursor=dXNlcjpV%2F1%3D",
             "https://slack.com/api/users.list?limit=200&cursor=page3"],
            handler.Requests.Select(r => r.Url));
    }

    [Fact]
    public async Task ACursorSeenBefore_EndsTheRead_RatherThanLoopingForever()
    {
        const string Page = """{"ok":true,"members":[{"id":"U1","real_name":"A"}],"response_metadata":{"next_cursor":"same"}}""";
        var handler = Stub(Ok(Page), Ok(Page));

        var list = await NewProvider(handler, out _).ListMembersAsync(Token, default);

        Assert.Equal(["U1", "U1"], list.Members!.Select(m => m.Id));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ATooManyRequests_StopsTheRead_SaysHowLongSlackAsked_AndNeitherRetriesNorWaits()
    {
        var throttled = Ok("""{"ok":false,"error":"ratelimited"}""", (HttpStatusCode)429);
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
        var handler = Stub(
            Ok("""{"ok":true,"members":[{"id":"U1","real_name":"A"}],"response_metadata":{"next_cursor":"page2"}}"""),
            throttled);

        var started = DateTimeOffset.UtcNow;
        var list = await NewProvider(handler, out var logs).ListMembersAsync(Token, default);

        Assert.Null(list.Members);
        Assert.Contains("42 seconds", list.Refusal);
        Assert.Equal(2, handler.Requests.Count);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(10), "the read waited for the throttle");
        Assert.DoesNotContain("xoxb", list.Refusal);
        AssertNothingLeaked(logs);
    }

    [Fact]
    public async Task ATooManyRequests_WithNoRetryAfter_SaysSo_PlainlyWithoutANumber()
    {
        var handler = Stub(Ok("""{"ok":false,"error":"ratelimited"}""", (HttpStatusCode)429));

        var list = await NewProvider(handler, out _).ListMembersAsync(Token, default);

        Assert.Equal("Slack asked Hatch to slow down; try again shortly", list.Refusal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ARefusalOnALaterPage_IsARefusal_NotAShortList()
    {
        var handler = Stub(
            Ok("""{"ok":true,"members":[{"id":"U1","real_name":"A"}],"response_metadata":{"next_cursor":"page2"}}"""),
            Ok("""{"ok":false,"error":"missing_scope"}"""));

        var list = await NewProvider(handler, out _).ListMembersAsync(Token, default);

        Assert.Null(list.Members);
        Assert.Contains("permission", list.Refusal);
    }

    // ---- conversations.open ----

    [Fact]
    public async Task OpeningADirectMessage_PostsTheMemberInTheBody_AndReturnsTheConversationId()
    {
        var handler = Stub(Ok("""{"ok":true,"channel":{"id":"D123"}}"""));

        var opened = await NewProvider(handler, out _).OpenDirectMessageAsync(Token, "U1", default);

        Assert.Equal("D123", opened.ConversationId);
        Assert.Null(opened.Refusal);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://slack.com/api/conversations.open", request.Url);
        Assert.Equal(new Dictionary<string, string> { ["users"] = "U1" }, handler.LastForm);
        Assert.Equal($"Bearer {Token}", Assert.Single(handler.AuthorizationHeaders));
    }

    [Theory]
    [InlineData("users_not_found", "could not find")]
    [InlineData("user_disabled", "deactivated")]
    [InlineData("cannot_dm_bot", "bot")]
    [InlineData("missing_scope", "permission")]
    public async Task ARefusedOpen_GivesItsOwnPlainSentence_NotTheCode(string code, string fragment)
    {
        var handler = Stub(Ok($$"""{"ok":false,"error":"{{code}}"}"""));

        var opened = await NewProvider(handler, out _).OpenDirectMessageAsync(Token, "U1", default);

        Assert.Null(opened.ConversationId);
        Assert.Contains(fragment, opened.Refusal);
        Assert.DoesNotContain(code, opened.Refusal);
    }

    [Fact]
    public async Task AnAcceptedOpenThatNamesNoConversation_IsARefusal()
    {
        var opened = await NewProvider(Stub(Ok("""{"ok":true}""")), out _).OpenDirectMessageAsync(Token, "U1", default);

        Assert.Null(opened.ConversationId);
        Assert.Equal("Slack could not be reached; try again", opened.Refusal);
    }

    // ---- The network ----

    [Fact]
    public async Task ADeadNetwork_IsASentenceOnEachCall_NotAnException_AndTheLogHoldsOnlyTheKind()
    {
        var handler = new ThrowingHandler(new HttpRequestException($"could not reach slack for {Email} with {Token}"));
        var provider = NewProvider(handler, out var logs);

        var found = await provider.FindMemberByEmailAsync(Token, Email, default);
        var list = await provider.ListMembersAsync(Token, default);
        var opened = await provider.OpenDirectMessageAsync(Token, "U1", default);

        Assert.Equal("Slack could not be reached; try again", found.Refusal);
        Assert.Equal("Slack could not be reached; try again", list.Refusal);
        Assert.Equal("Slack could not be reached; try again", opened.Refusal);
        Assert.NotEmpty(logs.Messages);
        AssertNothingLeaked(logs);
    }

    // ---- Helpers ----

    private static StubHttpMessageHandler Stub(params HttpResponseMessage[] responses) => new(responses);

    private static HttpResponseMessage Ok(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        StubHttpMessageHandler.Json(status, json);

    private static SlackChatProvider NewProvider(HttpMessageHandler handler, out CapturedLogs logs)
    {
        logs = new CapturedLogs();

        return new SlackChatProvider(new StubHttpClientFactory(handler), new LoggerFactory([logs]).CreateLogger<SlackChatProvider>());
    }

    private static void AssertNothingLeaked(CapturedLogs logs)
    {
        Assert.DoesNotContain(logs.Messages, line => line.Contains("example.test") || line.Contains("secret-value") || line.Contains("xoxb"));
    }

    private sealed class ThrowingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromException<HttpResponseMessage>(error);
    }
}

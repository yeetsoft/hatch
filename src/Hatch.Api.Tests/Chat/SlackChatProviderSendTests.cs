using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// Posting and editing through Slack: the form that goes out, what each answer
/// means to the sender, and what a message looks like once it is Slack text.
/// Nothing here calls Slack; <see cref="StubHttpMessageHandler"/> answers.
/// </summary>
public class SlackChatProviderSendTests
{
    private const string Token = "xoxb-1234-secret-value";

    private static ChatMessageContent Message(string title = "Done", string? body = null, string[]? lines = null, params ChatLink[] links) =>
        new(title, body, lines ?? [], links);

    [Fact]
    public async Task APost_GoesToPostMessage_WithTheFormSlackNeeds_AndReadsTheId()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true,"channel":"C1","ts":"1700000000.000100"}"""));

        var result = await NewProvider(handler).PostAsync(Token, "C1", Message("Shipped"), default);

        Assert.Equal(ChatSendOutcome.Sent, result.Outcome);
        Assert.Equal("1700000000.000100", result.MessageId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://slack.com/api/chat.postMessage", request.Url);
        Assert.Equal($"Bearer {Token}", Assert.Single(handler.AuthorizationHeaders));
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["channel"] = "C1", ["text"] = "*Shipped*", ["unfurl_links"] = "false", ["unfurl_media"] = "false",
            },
            handler.LastForm);
        Assert.DoesNotContain("secret-value", request.Url + request.Body);
    }

    [Fact]
    public async Task AnEdit_GoesToUpdate_NamingTheMessage()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true,"channel":"C1","ts":"1700000000.000100","text":"x"}"""));

        var result = await NewProvider(handler).UpdateAsync(Token, "C1", "1700000000.000100", Message("Shipped, again"), default);

        Assert.Equal(ChatSendOutcome.Sent, result.Outcome);
        Assert.Equal("1700000000.000100", result.MessageId);
        Assert.Equal("https://slack.com/api/chat.update", Assert.Single(handler.Requests).Url);
        Assert.Equal("1700000000.000100", handler.LastForm["ts"]);
        Assert.Equal("C1", handler.LastForm["channel"]);
        Assert.Equal("*Shipped, again*", handler.LastForm["text"]);
        Assert.Equal("false", handler.LastForm["unfurl_links"]);
    }

    [Fact]
    public async Task AnAcceptedPost_WithNoId_IsStillSent()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true}"""));

        var result = await NewProvider(handler).PostAsync(Token, "C1", Message(), default);

        Assert.Equal(ChatSendOutcome.Sent, result.Outcome);
        Assert.Null(result.MessageId);
    }

    [Fact]
    public async Task ATooManyRequestsAnswer_IsThrottled_AndKeepsTheSecondsItAskedFor()
    {
        var throttled = StubHttpMessageHandler.Json((HttpStatusCode)429, """{"ok":false,"error":"ratelimited"}""");
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));

        var result = await NewProvider(new StubHttpMessageHandler(throttled)).PostAsync(Token, "C1", Message(), default);

        Assert.Equal(ChatSendOutcome.Throttled, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(7), result.RetryAfter);
    }

    [Theory]
    [InlineData("ratelimited")]
    [InlineData("rate_limited")]
    public async Task ARateLimitCode_OnAnOkStatus_IsThrottled_WithNoSecondsWhenNoneWereGiven(string code)
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"ok":false,"error":"{{code}}"}"""));

        var result = await NewProvider(handler).PostAsync(Token, "C1", Message(), default);

        Assert.Equal(ChatSendOutcome.Throttled, result.Outcome);
        Assert.Null(result.RetryAfter);
    }

    [Theory]
    [InlineData("invalid_auth", ChatSendOutcome.WorkspaceRefused)]
    [InlineData("not_authed", ChatSendOutcome.WorkspaceRefused)]
    [InlineData("token_revoked", ChatSendOutcome.WorkspaceRefused)]
    [InlineData("token_expired", ChatSendOutcome.WorkspaceRefused)]
    [InlineData("account_inactive", ChatSendOutcome.WorkspaceRefused)]
    [InlineData("missing_scope", ChatSendOutcome.WorkspaceRefused)]
    [InlineData("org_login_required", ChatSendOutcome.WorkspaceRefused)]
    [InlineData("team_access_not_granted", ChatSendOutcome.WorkspaceRefused)]
    [InlineData("ekm_access_denied", ChatSendOutcome.WorkspaceRefused)]
    [InlineData("not_in_channel", ChatSendOutcome.RoomRefused)]
    [InlineData("channel_not_found", ChatSendOutcome.RoomRefused)]
    [InlineData("is_archived", ChatSendOutcome.RoomRefused)]
    [InlineData("message_not_found", ChatSendOutcome.RoomRefused)]
    [InlineData("cant_update_message", ChatSendOutcome.RoomRefused)]
    [InlineData("edit_window_closed", ChatSendOutcome.RoomRefused)]
    [InlineData("something_new", ChatSendOutcome.RoomRefused)]
    [InlineData("service_unavailable", ChatSendOutcome.Unreachable)]
    [InlineData("internal_error", ChatSendOutcome.Unreachable)]
    [InlineData("fatal_error", ChatSendOutcome.Unreachable)]
    [InlineData("request_timeout", ChatSendOutcome.Unreachable)]
    public async Task EachErrorCode_IsOneOfTheFiveOutcomes_AndItsSentenceHoldsNoToken(string code, ChatSendOutcome expected)
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"ok":false,"error":"{{code}}"}"""));

        var result = await NewProvider(handler).UpdateAsync(Token, "C1", "1.1", Message(), default);

        Assert.Equal(expected, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Sentence));
        Assert.Null(result.MessageId);
        AssertNoToken(result);
    }

    [Fact]
    public async Task AServerError_IsUnreachable()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "oops"));

        var result = await NewProvider(handler).PostAsync(Token, "C1", Message(), default);

        Assert.Equal(ChatSendOutcome.Unreachable, result.Outcome);
        AssertNoToken(result);
    }

    [Fact]
    public async Task ABodyThatIsNotJson_IsUnreachable()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, "<html>bad gateway</html>"));

        var result = await NewProvider(handler).PostAsync(Token, "C1", Message(), default);

        Assert.Equal(ChatSendOutcome.Unreachable, result.Outcome);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task ADeadNetwork_IsUnreachable_AndNeverThrows(Type error)
    {
        var handler = new ThrowingHandler((Exception)Activator.CreateInstance(error, "boom xoxb-1234-secret-value")!);

        var result = await NewProvider(handler).PostAsync(Token, "C1", Message(), default);

        Assert.Equal(ChatSendOutcome.Unreachable, result.Outcome);
        AssertNoToken(result);
    }

    [Fact]
    public async Task TextFromATicket_ArrivesEscaped_SoItCannotMentionOrLink()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true,"ts":"1.1"}"""));
        var content = Message(
            "<!channel> shipped", "Ask <@U123> & <!here>",
            ["see <https://x|y>", "fish & chips", "already &lt;escaped&gt;"],
            new ChatLink("<b>label</b> & more", "https://hatch.example/issues/HA-1"));

        await NewProvider(handler).PostAsync(Token, "C1", content, default);

        var text = handler.LastForm["text"];
        Assert.Equal(
            "*&lt;!channel&gt; shipped*\n"
            + "Ask &lt;@U123&gt; &amp; &lt;!here&gt;\n"
            + "see &lt;https://x|y&gt;\n"
            + "fish &amp; chips\n"
            + "already &amp;lt;escaped&amp;gt;\n"
            + "<https://hatch.example/issues/HA-1|&lt;b&gt;label&lt;/b&gt; &amp; more>",
            text);
    }

    [Fact]
    public void ALinkWithNoUrl_ShowsItsLabelAlone_AndLinksShareTheLastLine()
    {
        var text = SlackChatProvider.Format(Message(
            "Question", null, ["one"],
            new ChatLink("Answer it", "https://hatch.example/ballot"),
            new ChatLink("HA-9", null)));

        Assert.Equal("*Question*\none\n<https://hatch.example/ballot|Answer it> · HA-9", text);
    }

    [Fact]
    public void AUrlThatWouldEndTheLinkEarly_ShowsItsLabelAlone()
    {
        var text = SlackChatProvider.Format(Message("T", null, null, new ChatLink("Open", "https://x/a|b>c")));

        Assert.Equal("*T*\nOpen", text);
    }

    [Fact]
    public void ALongBody_IsCut_WithAnEllipsis_AndTheLinkIsKept()
    {
        var content = Message(
            "Long", new string('x', 10_000), ["a fact"],
            new ChatLink("HA-1", "https://hatch.example/issues/HA-1"));

        var text = SlackChatProvider.Format(content);

        Assert.True(text.Length <= SlackChatProvider.MaxTextLength, $"{text.Length} characters");
        Assert.EndsWith("\n<https://hatch.example/issues/HA-1|HA-1>", text);
        Assert.Contains("…\na fact\n", text);
        Assert.StartsWith("*Long*\nxxx", text);
    }

    [Fact]
    public void ALongQuestion_IsCut_WithItsIssueAndBallotLinksKept()
    {
        var links = new ChatLinks(Options.Create(new HatchOptions { PublicBaseUrl = "https://hatch.example" }));
        var content = QuestionChatContent.Build(
            links, 7, "HA-1", "A long question", new string('x', 10_000),
            [new QuestionOptionDto("leaf-weighted", Recommended: true), new QuestionOptionDto("child-weighted")],
            QuestionLevels.HoldName);

        var text = SlackChatProvider.Format(content);

        Assert.True(text.Length <= SlackChatProvider.MaxTextLength, $"{text.Length} characters");
        Assert.EndsWith("\n<https://hatch.example/issues/HA-1|HA-1> · <https://hatch.example/ballot|Answer it>", text);
        Assert.StartsWith("*HA-1 A long question*", text);
    }

    private static ChatMessageContent WithButtons(params ChatAction[] actions) =>
        new("HA-1 Q", "which?", ["Line"], [new ChatLink("HA-1", "https://hatch.example/issues/HA-1")], actions);

    private static System.Text.Json.JsonElement BlocksOf(StubHttpMessageHandler handler) =>
        System.Text.Json.JsonDocument.Parse(handler.LastForm["blocks"]).RootElement;

    private static StubHttpMessageHandler Ok() =>
        new(StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true,"channel":"C1","ts":"1700000000.000100"}"""));

    [Fact]
    public async Task APostWithButtons_SendsASectionAndARowOfButtons_AndStillTheText()
    {
        var handler = Ok();
        var content = WithButtons(
            new ChatAction("hatch.answer:7:r", "Accept recommended", Primary: true), new ChatAction("hatch.answer:7:0", "Five words"));

        await NewProvider(handler).PostAsync(Token, "C1", content, default);

        Assert.Equal(SlackChatProvider.Format(content), handler.LastForm["text"]);
        var blocks = BlocksOf(handler);
        Assert.Equal(2, blocks.GetArrayLength());
        Assert.Equal("section", blocks[0].GetProperty("type").GetString());
        Assert.Equal("mrkdwn", blocks[0].GetProperty("text").GetProperty("type").GetString());
        Assert.Equal(SlackChatProvider.Format(content, SlackChatProvider.MaxBlockTextLength), blocks[0].GetProperty("text").GetProperty("text").GetString());
        Assert.Equal("actions", blocks[1].GetProperty("type").GetString());
        var buttons = blocks[1].GetProperty("elements");
        Assert.Equal(2, buttons.GetArrayLength());
        Assert.Equal("button", buttons[0].GetProperty("type").GetString());
        Assert.Equal("hatch.answer:7:r", buttons[0].GetProperty("action_id").GetString());
        Assert.Equal("primary", buttons[0].GetProperty("style").GetString());
        Assert.Equal("Accept recommended", buttons[0].GetProperty("text").GetProperty("text").GetString());
        Assert.Equal("plain_text", buttons[0].GetProperty("text").GetProperty("type").GetString());
        Assert.False(buttons[0].GetProperty("text").GetProperty("emoji").GetBoolean());
        Assert.False(buttons[1].TryGetProperty("style", out _));
        Assert.False(buttons[1].TryGetProperty("value", out _));
    }

    [Fact]
    public async Task AnEditWithButtons_SendsThemToo()
    {
        var handler = Ok();

        await NewProvider(handler).UpdateAsync(Token, "C1", "1.1", WithButtons(new ChatAction("hatch.answer:7:0", "A")), default);

        Assert.Equal(2, BlocksOf(handler).GetArrayLength());
    }

    [Fact]
    public async Task AnEditWithNone_SendsAnEmptyList_ToTakeOldButtonsAway()
    {
        var handler = Ok();

        await NewProvider(handler).UpdateAsync(Token, "C1", "1.1", Message("Done"), default);

        Assert.Equal("[]", handler.LastForm["blocks"]);
    }

    [Fact]
    public async Task APostWithNone_SendsNoBlocksAtAll()
    {
        var handler = Ok();

        await NewProvider(handler).PostAsync(Token, "C1", Message("Done"), default);

        Assert.DoesNotContain("blocks", handler.LastForm.Keys);
    }

    [Fact]
    public async Task ALongLabel_IsCutTo75_WithAnEllipsis()
    {
        var handler = Ok();

        await NewProvider(handler).PostAsync(Token, "C1", WithButtons(new ChatAction("hatch.answer:7:0", new string('w', 120))), default);

        var label = BlocksOf(handler)[1].GetProperty("elements")[0].GetProperty("text").GetProperty("text").GetString()!;
        Assert.Equal(75, label.Length);
        Assert.EndsWith("…", label);
    }

    [Fact]
    public async Task ALabel_IsPlainText_NeverEscaped()
    {
        var handler = Ok();
        const string label = "<@U1> <http://x|y> & more";

        await NewProvider(handler).PostAsync(Token, "C1", WithButtons(new ChatAction("hatch.answer:7:0", label)), default);

        var sent = BlocksOf(handler)[1].GetProperty("elements")[0].GetProperty("text");
        Assert.Equal("plain_text", sent.GetProperty("type").GetString());
        Assert.Equal(label, sent.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ALongQuestion_HasASectionOfAtMost3000_WithItsLinksKept_AndTheTextItsOwnLength()
    {
        var handler = Ok();
        var links = new ChatLinks(Options.Create(new HatchOptions { PublicBaseUrl = "https://hatch.example" }));
        var content = QuestionChatContent.Build(
            links, 7, "HA-1", "A long question", new string('x', 10_000),
            [new QuestionOptionDto("leaf-weighted", Recommended: true), new QuestionOptionDto("child-weighted")],
            QuestionLevels.HoldName);

        await NewProvider(handler).PostAsync(Token, "C1", content, default);

        var section = BlocksOf(handler)[0].GetProperty("text").GetProperty("text").GetString()!;
        Assert.True(section.Length <= SlackChatProvider.MaxBlockTextLength, $"{section.Length} characters");
        Assert.EndsWith("\n<https://hatch.example/issues/HA-1|HA-1> · <https://hatch.example/ballot|Answer it>", section);
        Assert.True(handler.LastForm["text"].Length > SlackChatProvider.MaxBlockTextLength);
        Assert.Equal(SlackChatProvider.Format(content), handler.LastForm["text"]);
    }

    [Fact]
    public async Task EightOptionsAndAcceptRecommended_AreNineButtonsInOneRow()
    {
        var handler = Ok();
        var options = Enumerable.Range(0, QuestionOptionDto.MaxPerQuestion).Select(i => new QuestionOptionDto($"Option {i}", Recommended: i == 0)).ToList();
        var content = QuestionChatContent.Build(new ChatLinks(Options.Create(new HatchOptions())), 7, "HA-1", "Q", "which?", options, QuestionLevels.HoldName);

        await NewProvider(handler).PostAsync(Token, "C1", content, default);

        Assert.Equal(QuestionOptionDto.MaxPerQuestion + 1, BlocksOf(handler)[1].GetProperty("elements").GetArrayLength());
    }

    [Fact]
    public void LongLines_AreCutOnlyOnceTheBodyIsGone_InOrder()
    {
        var lines = Enumerable.Range(0, 200).Select(i => $"line {i} " + new string('y', 40)).ToArray();
        var text = SlackChatProvider.Format(Message("Many", "body stays", lines, new ChatLink("HA-1", "https://h/i/HA-1")));

        Assert.True(text.Length <= SlackChatProvider.MaxTextLength, $"{text.Length} characters");
        // The body gives way first, all of it, before a line is touched.
        Assert.DoesNotContain("body stays", text);
        Assert.Contains("line 0 ", text);
        Assert.DoesNotContain("line 199 ", text);
        Assert.EndsWith("<https://h/i/HA-1|HA-1>", text);
    }

    [Fact]
    public void ACutNeverSplitsAnEntity()
    {
        // Each ampersand is five characters once escaped, so a cut by raw length would land mid-entity.
        var text = SlackChatProvider.Format(Message("E", new string('&', 5000), null, new ChatLink("HA-1", null)));

        Assert.True(text.Length <= SlackChatProvider.MaxTextLength);
        Assert.DoesNotMatch(new Regex("&(?!amp;)"), text);
        Assert.EndsWith("…\nHA-1", text);
    }

    [Fact]
    public void ALoneTitle_IsCutAtThreeHundred()
    {
        var text = SlackChatProvider.Format(Message(new string('t', 2000)));

        Assert.Equal(SlackChatProvider.MaxTitleLength, text.Length);
        Assert.StartsWith("*ttt", text);
        Assert.EndsWith("…*", text);
    }

    [Fact]
    public void ATitleIsOneLine()
    {
        Assert.Equal("*one two*", SlackChatProvider.Format(Message("one\ntwo")));
    }

    private static void AssertNoToken(ChatSendResult result)
    {
        Assert.DoesNotContain("secret-value", result.Sentence ?? "");
        Assert.DoesNotContain("xoxb", result.Sentence ?? "");
    }

    private static SlackChatProvider NewProvider(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), NullLogger<SlackChatProvider>.Instance);

    private sealed class ThrowingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromException<HttpResponseMessage>(error);
    }
}

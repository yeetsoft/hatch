using System.Text.Json;
using Hatch.Api.Services.Chat;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// The Slack app manifest an admin pastes into Slack's own screen. It asks for
/// six bot scopes and nothing that needs Slack to call Hatch: no events, no
/// interactivity, no redirect and no request URL, and so no address of this
/// install in it.
/// </summary>
public class ChatManifestTests
{
    private static readonly string[] Scopes =
        ["chat:write", "channels:read", "groups:read", "users:read", "users:read.email", "im:write"];

    private static string Manifest() =>
        new SlackChatProvider(new StubHttpClientFactory(new StubHttpMessageHandler()), NullLogger<SlackChatProvider>.Instance).Manifest();

    [Fact]
    public void TheManifest_Parses_AsAnIndentedObject()
    {
        var text = Manifest();

        using var document = JsonDocument.Parse(text);

        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Contains('\n', text);
    }

    [Fact]
    public void TheManifest_NamesTheAppAndItsBot_Hatch()
    {
        using var document = JsonDocument.Parse(Manifest());
        var root = document.RootElement;

        Assert.Equal("Hatch", root.GetProperty("display_information").GetProperty("name").GetString());
        Assert.Equal("Hatch", root.GetProperty("features").GetProperty("bot_user").GetProperty("display_name").GetString());
    }

    [Fact]
    public void TheManifest_AsksForExactlyTheSixBotScopes()
    {
        using var document = JsonDocument.Parse(Manifest());

        var bot = document.RootElement.GetProperty("oauth_config").GetProperty("scopes").GetProperty("bot")
            .EnumerateArray().Select(s => s.GetString()).ToList();

        Assert.Equal(Scopes.Order(), bot.Order());
        Assert.False(document.RootElement.GetProperty("oauth_config").GetProperty("scopes").TryGetProperty("user", out _));
    }

    [Fact]
    public void TheManifest_HoldsNothingSlackCallsHatchBy()
    {
        using var document = JsonDocument.Parse(Manifest());
        var text = Manifest();

        foreach (var name in new[] { "event_subscriptions", "interactivity", "redirect_urls", "request_url" })
        {
            Assert.DoesNotContain(name, text);
            Assert.False(document.RootElement.TryGetProperty(name, out _), name);
        }

        Assert.DoesNotContain("://", text);
    }

    [Fact]
    public void TheManifest_TurnsSocketModeOrgDeployAndTokenRotationOff()
    {
        using var document = JsonDocument.Parse(Manifest());
        var settings = document.RootElement.GetProperty("settings");

        Assert.Equal(JsonValueKind.False, settings.GetProperty("socket_mode_enabled").ValueKind);
        Assert.Equal(JsonValueKind.False, settings.GetProperty("org_deploy_enabled").ValueKind);
        Assert.Equal(JsonValueKind.False, settings.GetProperty("token_rotation_enabled").ValueKind);
    }

    [Fact]
    public void EachScope_NamesTheMethodThatNeedsIt()
    {
        Assert.Equal(Scopes.Order(), SlackChatProvider.BotScopes.Select(s => s.Scope).Order());
        Assert.All(SlackChatProvider.BotScopes, s => Assert.False(string.IsNullOrWhiteSpace(s.Method)));
    }
}

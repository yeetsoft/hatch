using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The one property everything downstream leans on: the credential answers
/// "the secret, or null", and null is an ordinary value rather than a failure.
/// An installation with no Claude subscription lives here permanently.
/// </summary>
public class AgentCredentialTests
{
    private static StubSiteSettings Holding(string agent, string? stored) => agent switch
    {
        "claude" => new StubSiteSettings(claudeSubscriptionToken: stored),
        "codex" => new StubSiteSettings(codexApiKey: stored),
        _ => new StubSiteSettings(xaiApiKey: stored),
    };

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("grok")]
    public async Task ReadsTheCredentialFromSiteSettings(string agent)
    {
        var credential = new SiteSettingAgentCredential(Holding(agent, "sk-abc"));

        Assert.Equal("sk-abc", await credential.GetAsync(agent, CancellationToken.None));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("grok")]
    public async Task NullWhenNothingIsSet(string agent)
    {
        var credential = new SiteSettingAgentCredential(new StubSiteSettings());

        Assert.Null(await credential.GetAsync(agent, CancellationToken.None));
    }

    /// <summary>A box somebody cleared, and a value pasted with the newline the clipboard brought along, are both the obvious thing.</summary>
    [Theory]
    [InlineData("claude", "", null)]
    [InlineData("claude", "   ", null)]
    [InlineData("claude", "  sk-abc\n", "sk-abc")]
    [InlineData("codex", "", null)]
    [InlineData("codex", "   ", null)]
    [InlineData("codex", "  sk-abc\n", "sk-abc")]
    [InlineData("grok", "", null)]
    [InlineData("grok", "   ", null)]
    [InlineData("grok", "  sk-abc\n", "sk-abc")]
    public async Task TrimsAndTreatsBlankAsAbsent(string agent, string stored, string? expected)
    {
        var credential = new SiteSettingAgentCredential(Holding(agent, stored));

        Assert.Equal(expected, await credential.GetAsync(agent, CancellationToken.None));
    }

    /// <summary>Every credential set, and still nothing for a name that is not an agent.</summary>
    [Theory]
    [InlineData("gemini")]
    [InlineData("Claude")]
    [InlineData("")]
    public async Task NullForAnAgentThatIsNotKnown(string agent)
    {
        var credential = new SiteSettingAgentCredential(new StubSiteSettings(
            claudeSubscriptionToken: "a", codexApiKey: "b", xaiApiKey: "c"));

        Assert.Null(await credential.GetAsync(agent, CancellationToken.None));
    }
}

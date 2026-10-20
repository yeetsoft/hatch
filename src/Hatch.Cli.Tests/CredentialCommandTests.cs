using System.Net;
using System.Text;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>runner-credential &lt;agent&gt;</c> - the one call the container's
/// entrypoint makes before it starts the loop.
/// </summary>
/// <remarks>
/// The exit codes carry the meaning here, because the caller is a POSIX shell
/// with no JSON in it: <c>0</c> and a <c>NAME=value</c> line, <c>2</c> for a
/// Hatch holding none yet, <c>1</c> for everything else. Each is asserted, for
/// every agent, and so is the property the whole design rests on - that
/// nothing but that one line reaches standard output.
/// </remarks>
public sealed class CredentialCommandTests
{
    [Theory]
    [InlineData("claude", "CLAUDE_CODE_OAUTH_TOKEN")]
    [InlineData("codex", "CODEX_API_KEY")]
    [InlineData("grok", "XAI_API_KEY")]
    public async Task A_wrapped_credential_is_printed_as_name_equals_value_and_nothing_else(string agent, string variable)
    {
        using var h = new CliHarness();
        Answer(h, agent, Protected("sk-ant-oat-1"));

        Assert.Equal(0, await new CredentialCommand(h.Cli).RunAsync([agent], default));
        Assert.Equal([$"{variable}=sk-ant-oat-1"], h.Say.Said);
        Assert.Empty(h.Say.Complained);
    }

    /// <summary>
    /// The untagged form <c>SecretProtector.Unprotect</c> still reads - a value
    /// written before the scheme tag existed. Read the same way here, so a
    /// container is not the one caller that a data migration would have broken.
    /// </summary>
    [Theory]
    [InlineData("claude", "CLAUDE_CODE_OAUTH_TOKEN")]
    [InlineData("codex", "CODEX_API_KEY")]
    [InlineData("grok", "XAI_API_KEY")]
    public async Task An_untagged_payload_is_read_as_the_scheme_it_is(string agent, string variable)
    {
        using var h = new CliHarness();
        Answer(h, agent, Obfuscated("sk-ant-oat-1"));

        Assert.Equal(0, await new CredentialCommand(h.Cli).RunAsync([agent], default));
        Assert.Equal([$"{variable}=sk-ant-oat-1"], h.Say.Said);
    }

    /// <summary>
    /// No credential yet is an ordinary state, told apart from a fault by its
    /// own code so the entrypoint can wait rather than give up.
    /// </summary>
    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("grok")]
    public async Task No_credential_saved_is_its_own_exit_code_and_says_nothing(string agent)
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", $"/api/hatch/settings/credentials/{agent}", HttpStatusCode.NoContent);

        Assert.Equal(CredentialCommand.NoCredential, await new CredentialCommand(h.Cli).RunAsync([agent], default));
        Assert.Empty(h.Say.Said);
        Assert.Empty(h.Say.Complained);
    }

    /// <summary>A Hatch with its wall on refuses this route outright, and the refusal is worth reading.</summary>
    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("grok")]
    public async Task A_refusal_is_complained_about_and_exits_one(string agent)
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", $"/api/hatch/settings/credentials/{agent}", HttpStatusCode.Forbidden);

        Assert.Equal(1, await new CredentialCommand(h.Cli).RunAsync([agent], default));
        Assert.Contains("403", h.Complained);
        Assert.Empty(h.Say.Said);
    }

    /// <summary>
    /// A Hatch too old to have the route at all. The one thing that must not
    /// happen is the entrypoint reading this as "no credential yet" and waiting
    /// all night for one that is already set.
    /// </summary>
    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("grok")]
    public async Task A_hatch_without_the_route_exits_one_rather_than_two(string agent)
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new CredentialCommand(h.Cli).RunAsync([agent], default));
        Assert.Contains("404", h.Complained);
        Assert.Empty(h.Say.Said);
    }

    /// <summary>
    /// A payload written under a scheme this build has never heard of - a
    /// rolled-back runner reading a newer Hatch. Unreadable is the honest
    /// answer, and it is a fault rather than an empty line on stdout, which is
    /// what an entrypoint would have exported as a credential.
    /// </summary>
    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("grok")]
    public async Task A_payload_this_build_cannot_read_exits_one_and_prints_nothing(string agent)
    {
        using var h = new CliHarness();
        Answer(h, agent, "v2:" + Obfuscated("sk-ant-oat-1"));

        Assert.Equal(1, await new CredentialCommand(h.Cli).RunAsync([agent], default));
        Assert.Empty(h.Say.Said);
        Assert.Contains("answered with a credential this build cannot read", h.Complained);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("grok")]
    public async Task A_body_that_is_not_a_credential_exits_one(string agent)
    {
        using var h = new CliHarness();
        h.Wire.Reply("GET", $"/api/hatch/settings/credentials/{agent}", HttpStatusCode.OK, "not json at all");

        Assert.Equal(1, await new CredentialCommand(h.Cli).RunAsync([agent], default));
        Assert.Empty(h.Say.Said);
    }

    [Fact]
    public async Task No_argument_is_refused_and_calls_nothing()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new CredentialCommand(h.Cli).RunAsync([], default));
        Assert.Contains("exactly one argument", h.Complained);
        Assert.Empty(h.Say.Said);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task An_agent_nobody_has_heard_of_is_refused_before_any_call()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new CredentialCommand(h.Cli).RunAsync(["gemini"], default));
        Assert.Contains("gemini", h.Complained);
        Assert.Contains("claude, codex, grok", h.Complained);
        Assert.Empty(h.Say.Said);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Dash_h_prints_its_own_usage_and_calls_nothing()
    {
        using var h = new CliHarness();

        Assert.Equal(0, await new CredentialCommand(h.Cli).RunAsync(["-h"], default));
        Assert.StartsWith("usage: hatch runner-credential", h.Said);
        Assert.Empty(h.Wire.Calls);
    }

    /// <summary>
    /// A fourth agent added to <see cref="Settings.KnownAgentIds"/> without a
    /// variable here would be one whose container never got a credential, and
    /// said so only as a usage error at runtime.
    /// </summary>
    [Fact]
    public void Every_known_agent_has_a_variable()
    {
        foreach (var agent in Settings.KnownAgentIds)
            Assert.False(string.IsNullOrWhiteSpace(CredentialCommand.Variable(agent)), $"no variable for {agent}");
    }

    private static void Answer(CliHarness h, string agent, string protectedToken) =>
        h.Wire.Reply(
            "GET", $"/api/hatch/settings/credentials/{agent}", HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(new CredentialDto(protectedToken), Fixtures.Json));

    /// <summary>
    /// What the server writes, spelled out here rather than reversed through
    /// the code under test: a test that obfuscated with <c>Secret</c>'s own key
    /// would pass just as happily if that key had drifted from the API's.
    /// </summary>
    private static string Protected(string plaintext) => "v1:" + Obfuscated(plaintext);

    private static string Obfuscated(string plaintext)
    {
        var key = "Hatch.SiteSettings.Obfuscation.v1"u8.ToArray();
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        for (var i = 0; i < bytes.Length; i++) bytes[i] ^= key[i % key.Length];
        return Convert.ToBase64String(bytes);
    }
}

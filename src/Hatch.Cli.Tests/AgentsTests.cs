namespace Hatch.Cli.Tests;

/// <summary><see cref="Agents.Resolve"/>: an id names an adapter, or it names nothing and refuses.</summary>
public sealed class AgentsTests
{
    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("grok")]
    [InlineData("ollama")]
    public void A_known_id_resolves_to_the_adapter_of_that_name(string id)
    {
        Assert.True(Agents.Resolve(
            id,
            new Settings { Base = "https://x", Key = "", ClaudeBin = "/x/claude", CodexBin = "/x/codex", GrokBin = "/x/grok" },
            null, out var runner, out var adapter, out var refusal));

        Assert.Equal(id, adapter.Id);
        Assert.NotNull(runner);
        Assert.Equal("", refusal);
    }

    [Fact]
    public void An_unknown_id_refuses_and_names_every_known_one()
    {
        Assert.False(Agents.Resolve(
            "nope", new Settings { Base = "https://x", Key = "" }, null, out _, out _, out var refusal));

        Assert.Equal("hatch: no such agent \"nope\" - known: claude, codex, grok, ollama", refusal);
    }
}

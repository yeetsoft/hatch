namespace Hatch.Cli.Tests;

/// <summary><see cref="Agents.Resolve"/>: an id names an adapter, or it names nothing and refuses.</summary>
public sealed class AgentsTests
{
    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("grok")]
    public void A_known_id_resolves_to_the_adapter_of_that_name(string id)
    {
        Assert.True(Agents.Resolve(
            id, "/x/claude", "/x/codex", "/x/grok", null, out var runner, out var adapter, out var refusal));

        Assert.Equal(id, adapter.Id);
        Assert.NotNull(runner);
        Assert.Equal("", refusal);
    }

    [Fact]
    public void An_unknown_id_refuses_and_names_every_known_one()
    {
        Assert.False(Agents.Resolve("nope", null, null, null, null, out _, out _, out var refusal));

        Assert.Equal("hatch: no such agent \"nope\" - known: claude, codex, grok", refusal);
    }
}

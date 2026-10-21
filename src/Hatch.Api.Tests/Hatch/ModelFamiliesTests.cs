using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The one list of model families a pinned id is judged against, and the one
/// method that judges one: <see cref="ModelFamilies.AgentOf"/>.
/// </summary>
public class ModelFamiliesTests
{
    [Theory]
    [InlineData("claude-opus-5", "claude")]
    [InlineData("gpt-6-astra", "codex")]
    [InlineData("gpt-6.1-sol", "codex")]
    [InlineData("gpt-4", "codex")]
    [InlineData("grok-4.7", "grok")]
    [InlineData("grok-4.5", "grok")]
    [InlineData("grok", null)]
    [InlineData("groks-1", null)]
    [InlineData("opus", null)]
    [InlineData("nonsense-1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("qwen3-coder:30b", "ollama")]
    [InlineData("gpt-oss:20b", "ollama")]
    [InlineData("llama3.3:latest", "ollama")]
    [InlineData("hf.co/unsloth/Qwen3-Coder-30B-A3B-Instruct-GGUF:Q4_K_M", "ollama")]
    [InlineData("llama3.3", null)]
    [InlineData(":latest", null)]
    [InlineData("a:b:c", null)]
    [InlineData("qwen3:", null)]
    [InlineData("qwen3:30b/x", null)]
    [InlineData("-x:1", null)]
    [InlineData("qwen3:30b\n", null)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:30b", null)]
    [InlineData("haiku", null)]
    [InlineData("sonnet", null)]
    [InlineData("fable", null)]
    public void AgentOf_MatchesThePinnedIdsFamily(string? model, string? agent)
    {
        Assert.Equal(agent, ModelFamilies.Default.AgentOf(model));
    }

    [Fact]
    public void AgentOf_DrawsTheOllamaLineAtSixtyCharacters()
    {
        var sixty = new string('a', 56) + ":30b";
        Assert.Equal(60, sixty.Length);
        Assert.Equal("ollama", ModelFamilies.Default.AgentOf(sixty));

        var sixtyOne = new string('a', 57) + ":30b";
        Assert.Equal(61, sixtyOne.Length);
        Assert.Null(ModelFamilies.Default.AgentOf(sixtyOne));
    }

    [Theory]
    [InlineData("claude-opus-5", "claude")]
    [InlineData("fake-1", "fake")]
    public void AgentOf_ResolvesEachIdToItsOwnFamily_InATwoRowList(string model, string agent)
    {
        var families = new ModelFamilies([
            ModelFamilies.Default.Rows[0],
            new ModelFamily("fake", "^fake-[a-z0-9-]+$", "fake-1"),
        ]);

        Assert.Equal(agent, families.AgentOf(model));
    }

    [Theory]
    [InlineData("claude-opus-5", "claude")]
    [InlineData("gpt-6-astra", "codex")]
    [InlineData("grok-4.7", "grok")]
    [InlineData("qwen3-coder:30b", "ollama")]
    public void AgentOf_KeepsTheDefaultFamiliesApart(string model, string agent)
    {
        Assert.Equal(agent, ModelFamilies.Default.AgentOf(model));
        Assert.All(
            ModelFamilies.Default.Rows.Where(r => r.Agent != agent),
            r => Assert.DoesNotMatch(r.Pattern, model));
    }
}

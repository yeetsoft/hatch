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

    [Fact]
    public void Refusal_IsTheSentenceAPlaybookModelHasAlwaysBeenRefusedWith()
    {
        Assert.Equal(
            "a model is one of haiku, sonnet, opus, fable, " +
            "or a pinned id of a supported agent (claude-opus-5 for claude; gpt-6-luna for codex; " +
            "grok-4.7 for grok; qwen3-coder:30b for ollama) - not \"nonsense-1\"",
            ModelFamilies.Default.Refusal("nonsense-1"));
    }

    [Fact]
    public void Normalise_AbsentTouchesNothing()
    {
        Assert.Null(RunnerModels.Normalise(null, "claude", out var normalised));
        Assert.Empty(normalised);
    }

    [Fact]
    public void Normalise_LowercasesAnAliasAndKeepsAPinnedIdOfTheRunnersAgent()
    {
        Assert.Null(RunnerModels.Normalise([" Opus ", "SONNET", "claude-opus-5"], "claude", out var normalised));
        Assert.Equal(["opus", "sonnet", "claude-opus-5"], normalised);
    }

    [Fact]
    public void Normalise_RefusesAnotherAgentsIdWithTheWrongFamilySentence()
    {
        var refusal = RunnerModels.Normalise(["gpt-6-luna"], "claude", out var normalised);

        Assert.Equal("\"gpt-6-luna\" is a codex model - this runner's agent is claude", refusal);
        Assert.Empty(normalised);
        Assert.Equal(
            "\"claude-opus-5\" is a claude model - this runner's agent is ollama",
            RunnerModels.Normalise(["claude-opus-5"], "ollama", out _));
        Assert.Equal(
            "\"grok-4.7\" is a grok model - this runner's agent is claude",
            RunnerModels.Normalise(["grok-4.7"], "claude", out _));
        Assert.Equal(
            "\"claude-opus-5\" is a claude model - this runner's agent is codex",
            RunnerModels.Normalise(["claude-opus-5"], "codex", out _));
    }

    [Fact]
    public void Normalise_RefusesAnUnrecognisedIdWithTheModelSentence()
    {
        Assert.Equal(
            ModelFamilies.Default.Refusal("nonsense-1"),
            RunnerModels.Normalise(["opus", "nonsense-1"], "claude", out var normalised));
        Assert.Empty(normalised);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalise_RefusesABlankEntry(string blank)
    {
        Assert.Equal("a model cannot be blank", RunnerModels.Normalise(["opus", blank], "claude", out _));
    }

    [Fact]
    public void Normalise_DropsDuplicatesAndKeepsTheOrderGiven()
    {
        Assert.Null(RunnerModels.Normalise(
            ["sonnet", "claude-opus-5", "OPUS", "sonnet", "claude-opus-5", "haiku"], "claude", out var normalised));
        Assert.Equal(["sonnet", "claude-opus-5", "opus", "haiku"], normalised);
    }

    [Fact]
    public void Normalise_RefusesMoreThanTheMaximum()
    {
        var many = Enumerable.Range(0, RunnerModels.MaxCount + 1).Select(i => $"claude-m{i}").ToList();

        Assert.Equal("a runner advertises at most 100 models", RunnerModels.Normalise(many, "claude", out var normalised));
        Assert.Empty(normalised);
        Assert.Null(RunnerModels.Normalise(many.Take(RunnerModels.MaxCount).ToList(), "claude", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("mystery")]
    public void Normalise_AnAgentNoFamilyNamesSkipsOnlyTheFamilyCheck(string? agent)
    {
        Assert.Null(RunnerModels.Normalise(["opus", "gpt-6-luna", "claude-opus-5"], agent, out var normalised));
        Assert.Equal(["opus", "gpt-6-luna", "claude-opus-5"], normalised);

        Assert.Equal(
            ModelFamilies.Default.Refusal("nonsense-1"),
            RunnerModels.Normalise(["nonsense-1"], agent, out _));
    }
}

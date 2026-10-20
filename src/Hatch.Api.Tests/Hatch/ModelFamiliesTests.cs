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
    public void AgentOf_MatchesThePinnedIdsFamily(string? model, string? agent)
    {
        Assert.Equal(agent, ModelFamilies.Default.AgentOf(model));
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
    public void AgentOf_KeepsTheDefaultFamiliesApart(string model, string agent)
    {
        Assert.Equal(agent, ModelFamilies.Default.AgentOf(model));
        Assert.All(
            ModelFamilies.Default.Rows.Where(r => r.Agent != agent),
            r => Assert.DoesNotMatch(r.Pattern, model));
    }
}

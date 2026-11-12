using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The one matching rule a runner's refused models are judged by:
/// <see cref="RunnerModels.Refuses"/>.
/// </summary>
public class RunnerModelsRefusesTests
{
    [Theory]
    [InlineData("opus", "opus")]
    [InlineData("opus", "claude-opus-5-5")]
    [InlineData("fable", "claude-fable-5-2")]
    [InlineData("haiku", "claude-haiku-5")]
    [InlineData("claude-opus-5-5", "claude-opus-5-5")]
    [InlineData("gpt-6-luna", "gpt-6-luna")]
    public void Refuses_AnEqualModel_OrAPinnedIdNamingTheRefusedAlias(string refused, string model) =>
        Assert.True(RunnerModels.Refuses([refused], model));

    [Theory]
    [InlineData("opus", "claude-fable-5-2")]
    [InlineData("opus", "sonnet")]
    [InlineData("opus", "claude-opusx-5")]
    [InlineData("opus", "claude-nonopus-5")]
    [InlineData("claude-opus-5-5", "claude-opus-5-6")]
    [InlineData("claude-opus-5-5", "claude-opus-5")]
    [InlineData("claude-opus-5-5", "opus")]
    [InlineData("claude-opus-5", "claude-opus-5-5")]
    [InlineData("claude-opus-5-5", "CLAUDE-OPUS-5-5")]
    public void Refuses_NothingElse(string refused, string model) =>
        Assert.False(RunnerModels.Refuses([refused], model));

    [Fact]
    public void Refuses_AnAliasCoversAPinnedIdButAPinnedIdNeverCoversAnAlias()
    {
        Assert.True(RunnerModels.Refuses(["opus"], "claude-opus-5-5"));
        Assert.False(RunnerModels.Refuses(["claude-opus-5-5"], "opus"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_ABlankModelMatchesNothing(string? model) =>
        Assert.False(RunnerModels.Refuses(["opus", "fable"], model));

    [Fact]
    public void Refuses_NoListMatchesNothing()
    {
        Assert.False(RunnerModels.Refuses(null, "opus"));
        Assert.False(RunnerModels.Refuses([], "opus"));
    }

    [Theory]
    [InlineData("  OPUS ", "opus")]
    [InlineData("opus", " Opus")]
    [InlineData("Opus", "claude-opus-5-5")]
    [InlineData("  claude-opus-5-5  ", "claude-opus-5-5")]
    public void Refuses_TrimsBothSides_AndLowercasesAnAlias(string refused, string model) =>
        Assert.True(RunnerModels.Refuses([refused], model));

    [Fact]
    public void Refuses_AnyEntryOfTheListIsEnough()
    {
        Assert.True(RunnerModels.Refuses(["haiku", "claude-opus-5-5", "fable"], "claude-fable-5-2"));
        Assert.False(RunnerModels.Refuses(["haiku", "sonnet"], "claude-fable-5-2"));
    }

    [Fact]
    public void Refused_NamesTheTicketTheColumnTheModelAndTheRunner() =>
        Assert.Equal(
            "HA-12 in Breakdown runs on fable - build-box refuses it",
            ModelFamilies.Refused("HA-12", "Breakdown", "fable", "build-box"));
}

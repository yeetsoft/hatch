using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Which stage wins where more than one is true of an ambition. Each stage's own
/// case and sentence are pinned through the route in
/// <see cref="AmbitionsControllerTests"/>; here are the pairs that decide the
/// order, and the order of the groups.
/// </summary>
public class AmbitionStageTests
{
    private static readonly EfHatchStatus Idea = new() { Id = 1, Name = "idea", SortOrder = 10 };
    private static readonly EfHatchStatus Todo = new() { Id = 2, Name = "todo", SortOrder = 20 };
    private static readonly EfHatchStatus Doing = new() { Id = 3, Name = "doing", SortOrder = 30, IsImplementation = true };
    private static readonly EfHatchStatus Review = new() { Id = 4, Name = "review", SortOrder = 35 };
    private static readonly EfHatchStatus Done = new() { Id = 5, Name = "done", SortOrder = 40, IsTerminal = true };
    private static readonly EfHatchStatus Shelved = new() { Id = 6, Name = "shelved", SortOrder = 50, IsDeferred = true };

    private static readonly List<EfHatchStatus> Board = [Idea, Todo, Doing, Review, Done, Shelved];

    private static string Stage(
        EfHatchStatus status, int capabilities = 0, int holds = 0, int? shaping = null,
        bool budgetSpent = false, string? passFold = null) =>
        AmbitionStage.Of(status, Board, capabilities, holds, shaping, budgetSpent, passFold).Stage;

    [Fact]
    public void ATerminalColumn_BeatsTheShapingColumnAndEverythingElse() =>
        Assert.Equal("done", Stage(Done, capabilities: 1, holds: 1, shaping: Done.Id, budgetSpent: true));

    [Fact]
    public void ADeferredColumn_BeatsTheShapingColumn_AndIsNeverADraft() =>
        Assert.Equal("shelved", Stage(Shelved, shaping: Shelved.Id));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TheShapingColumn_BeatsDraftAndReady(int capabilities) =>
        Assert.Equal("shaping", Stage(Idea, capabilities, shaping: Idea.Id));

    [Fact]
    public void ABoardWithNoShapingRow_ReadsEveryColumnLeftOfImplementationAsDraftOrReady()
    {
        Assert.Equal("draft", Stage(Idea));
        Assert.Equal("ready", Stage(Idea, capabilities: 1));
    }

    [Fact]
    public void ADraftWithNoShapingRow_NamesTheTwoWaysOn() =>
        Assert.Equal(
            "It has no capabilities yet. Add them by hand, or add a playbook row that shapes ambitions on the Playbooks page.",
            AmbitionStage.Of(Idea, Board, 0, 0, null, false, null).Why);

    [Fact]
    public void ADraftWithAShapingRow_KeepsTheShortSentence() =>
        Assert.Equal(
            "It has no capabilities yet.",
            AmbitionStage.Of(Idea, Board, 0, 0, Todo.Id, false, null).Why);

    [Fact]
    public void BudgetSpent_BeatsComplete() =>
        Assert.Equal("budget spent", Stage(Doing, capabilities: 1, holds: 1, budgetSpent: true));

    [Fact]
    public void Complete_NeedsAtLeastOneCapability_AndEveryOneHolding()
    {
        Assert.Equal("running", Stage(Doing));
        Assert.Equal("running", Stage(Doing, capabilities: 2, holds: 1));
        Assert.Equal("complete", Stage(Doing, capabilities: 2, holds: 2));
    }

    [Fact]
    public void Complete_BeatsAFoldedPass()
    {
        var (stage, why) = AmbitionStage.Of(Doing, Board, 1, 1, null, budgetSpent: false, passFold: "the room is full");

        Assert.Equal(("complete", "Every capability holds, so a pass has nothing left to do."), (stage, why));
    }

    [Fact]
    public void PastImplementation_IsStopped_WhateverElseIsTrue() =>
        Assert.Equal("stopped", Stage(Review, capabilities: 1, holds: 1, budgetSpent: true));

    [Fact]
    public void TheGroups_ComeInOrder_WithDoneAndShelvedSharingTheLast()
    {
        string[] stages =
        [
            AmbitionStage.Done, AmbitionStage.Draft, AmbitionStage.Shaping, AmbitionStage.Ready, AmbitionStage.Complete,
            AmbitionStage.Stopped, AmbitionStage.Running, AmbitionStage.BudgetSpent,
        ];

        Assert.Equal(
            ["budget spent", "running", "stopped", "complete", "ready", "shaping", "draft", "done"],
            stages.OrderBy(AmbitionStage.Group));
        Assert.Equal(AmbitionStage.Group(AmbitionStage.Done), AmbitionStage.Group(AmbitionStage.Shelved));
    }
}

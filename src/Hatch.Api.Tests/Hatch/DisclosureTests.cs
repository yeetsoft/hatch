using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="PriorityTree.Disclose"/> on a tree built by hand: a level, a danger
/// reason, runner tags, omissions, a local model or a pull that came from an
/// ancestor the reader cannot see still applies, and what leaves is the value
/// and a flag, never the ancestor's key.
/// </summary>
public class DisclosureTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;

    // 1 BBB-1 (hidden) is the story every level lives on; 2 AAA-1 is a task under it.
    // 3 AAA-2 hangs under 2 and sees the whole chain.
    private static PriorityTree Tree(HashSet<long>? visible)
    {
        var rows = new Dictionary<long, (long?, string, int, int)>
        {
            [1] = (null, "BBB-1", PriorityLevels.Expedited, PreApprovalLevels.Express),
            [2] = (1, "AAA-1", PriorityLevels.Normal, PreApprovalLevels.None),
            [3] = (2, "AAA-2", PriorityLevels.Normal, PreApprovalLevels.None),
        };
        var onePr = new Dictionary<long, (string, bool?, bool, int, bool, bool)>
        {
            [1] = ("story", true, false, 1, false, false),
            [2] = ("task", null, false, 1, false, false),
            [3] = ("task", null, false, 1, false, false),
        };
        var tags = new Dictionary<long, (IReadOnlyList<string>, IReadOnlyList<string>)>
        {
            [1] = (["macos"], []), [2] = ([], []), [3] = ([], []),
        };
        var dangerous = new Dictionary<long, string?> { [1] = "load test", [2] = null, [3] = null };
        var omissions = new Dictionary<long, IReadOnlyList<(string Runner, string? Reason, string By, DateTimeOffset At)>>
        {
            [1] = [("runner-a", null, "operator", At)],
            [2] = [("runner-b", null, "operator", At)],
        };
        var localModels = new Dictionary<long, int> { [1] = LocalModelLevels.Prefer };

        return new PriorityTree(rows, onePr, tags, dangerous, omissions, localModels: localModels, visible: visible);
    }

    [Fact]
    public void ATreeWithNoVisibleSet_DisclosesNothing()
    {
        var tree = Tree(null);
        var d = tree.Disclose(3);

        Assert.False(d.ParentBeyondScope);
        Assert.False(d.ChildBeyondScope);
        Assert.Equal("BBB-1", d.Key(DisclosedValues.PreApproval, tree.EffectivePreApproval(3).FromKey));
        Assert.Empty(d.FromBeyondScope);
        Assert.Equal("BBB-1", d.Tags(tree.EffectiveRunnerTags(3).From).Single(s => s.Kind == RunnerTagSourceKinds.Issue).Key);
    }

    [Fact]
    public void EachFromKeyOfAHiddenAncestor_IsBlankedWhileTheValueIsKept()
    {
        var tree = Tree([2, 3]);
        var d = tree.Disclose(3);

        var (priority, priorityFrom) = tree.Effective(3);
        var (preApproval, preApprovalFrom) = tree.EffectivePreApproval(3);
        var (localModel, localModelFrom) = tree.EffectiveLocalModel(3);
        var (onePr, _, onePrFrom) = tree.EffectiveOnePr(3);
        var (dangerous, dangerousFrom) = tree.EffectiveDangerous(3);

        // The values still apply: a reader with a narrow scope sees what the dispatcher acts on.
        Assert.Equal(PriorityLevels.Expedited, priority);
        Assert.Equal(PreApprovalLevels.Express, preApproval);
        Assert.Equal(LocalModelLevels.Prefer, localModel);
        Assert.True(onePr);
        Assert.Equal("load test", dangerous);

        // The real keys are still there for the dispatcher...
        Assert.Equal("BBB-1", priorityFrom);

        // ...and none leaves.
        Assert.Null(d.Key(DisclosedValues.Priority, priorityFrom));
        Assert.Null(d.Key(DisclosedValues.PreApproval, preApprovalFrom));
        Assert.Null(d.Key(DisclosedValues.LocalModel, localModelFrom));
        Assert.Null(d.Key(DisclosedValues.OnePr, onePrFrom));
        Assert.Null(d.Key(DisclosedValues.Dangerous, dangerousFrom));
        Assert.Null(d.Key(DisclosedValues.PreApprovalPassedOver, "BBB-1"));
        Assert.Null(d.Key(DisclosedValues.PriorityPassedOver, "BBB-1"));
        Assert.Null(d.Key(DisclosedValues.PullPassedOver, "BBB-1"));

        Assert.Equal(
            ["priority", "preApproval", "localModel", "onePr", "dangerous", "preApprovalPassedOver", "priorityPassedOver", "pullPassedOver"],
            d.FromBeyondScope);
    }

    [Fact]
    public void AFromKeyOfAVisibleAncestorOrOfNobody_PassesUnchanged()
    {
        var d = Tree([2, 3]).Disclose(3);

        Assert.Equal("AAA-1", d.Key(DisclosedValues.Priority, "AAA-1"));
        Assert.Null(d.Key(DisclosedValues.PreApproval, null));
        Assert.Empty(d.FromBeyondScope);
    }

    [Fact]
    public void ATagSourceAndAnOmissionFromAHiddenAncestor_KeepTheirValueAndLoseTheirKey()
    {
        var tree = Tree([2, 3]);
        var d = tree.Disclose(3);

        var (all, from) = tree.EffectiveRunnerTags(3);
        var sources = d.Tags(from);
        var omissions = d.Omissions(tree.EffectiveOmittedRunners(3));

        Assert.Contains("macos", all);
        var hiddenSource = Assert.Single(sources);
        Assert.Equal("", hiddenSource.Key);
        Assert.True(hiddenSource.BeyondScope);
        Assert.Equal(["macos"], hiddenSource.Tags);

        var hidden = omissions.Single(o => o.Runner == "runner-a");
        Assert.Equal("", hidden.FromKey);
        Assert.True(hidden.BeyondScope);

        // A visible ancestor's omission still names it.
        var visible = omissions.Single(o => o.Runner == "runner-b");
        Assert.Equal("AAA-1", visible.FromKey);
        Assert.False(visible.BeyondScope);
    }

    [Fact]
    public void AParentOutsideTheVisibleSet_IsAFlag_AndSoIsAChildOutsideIt()
    {
        var tree = Tree([2, 3]);

        Assert.True(tree.Disclose(2).ParentBeyondScope);
        Assert.False(tree.Disclose(3).ParentBeyondScope);
        Assert.False(tree.Disclose(2).ChildBeyondScope);

        // Seen the other way round: the hidden story has a visible child and no hidden one.
        var parentOnly = Tree([1]);
        Assert.True(parentOnly.Disclose(1).ChildBeyondScope);
        Assert.False(parentOnly.Disclose(1).ParentBeyondScope);
    }

    [Fact]
    public void WithVisible_LaysAReadersScopeOverATreeReadWhole()
    {
        var whole = Tree(null);

        Assert.Same(whole, whole.WithVisible(null));

        var reader = whole.WithVisible([2, 3]);
        Assert.True(reader.Disclose(2).ParentBeyondScope);
        Assert.Null(reader.Disclose(3).Key(DisclosedValues.Priority, "BBB-1"));

        // The rows are the same: what the dispatcher reads is not narrowed.
        Assert.Equal(whole.Effective(3), reader.Effective(3));
        Assert.True(reader.HasChildren(1));
    }
}

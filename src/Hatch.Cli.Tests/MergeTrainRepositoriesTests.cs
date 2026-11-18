namespace Hatch.Cli.Tests;

/// <summary>
/// The merge, update and head-build halves of the merge train in a project bound to two repositories:
/// each acts in the one its pull request's url names, not in the first.
/// </summary>
public sealed partial class MergeTrainTests
{
    private const string OneCanonical = "example.test/one";
    private const string TwoCanonical = "example.test/two";
    private const string OneRemote = "https://example.test/one.git";
    private const string TwoRemote = "https://example.test/two.git";
    private const string TwoUrl = "https://example.test/two/pull/1";

    /// <summary>
    /// A row whose pull request is in the second of two bound repositories: the clean merge check is
    /// for <c>two</c>, and the one for <c>one</c> says no branch is named there.
    /// </summary>
    private static ReviewCheckDto SecondRow(string kind, string url = TwoUrl)
    {
        var one = Fixtures.Repository(OneRemote, OneCanonical, primary: true, matchedRemote: OneRemote);
        var two = Fixtures.Repository(TwoRemote, TwoCanonical, primary: false, matchedRemote: TwoRemote);
        var none = Fixtures.MergeCheck(MergeVerdicts.None, "main", OneCanonical);
        var clean = Fixtures.MergeCheck(MergeVerdicts.Clean, "main", TwoCanonical, holdsTrunk: true) with
        {
            Remote = TwoRemote, TrunkSha = Trunk, Branch = "aer-1-thing", BranchSha = Tip,
        };

        return Fixtures.Review(
            "AER-1", [one, two], [none, clean], [], url,
            new MergeWorkDto(kind, "its branch is ready", IsHead: true));
    }

    private static Runtime RuntimeHolding(Rig rig, params string[] paths) => rig.H.Runtime with
    {
        Clock = rig.Clock,
        Checkouts =
        [
            .. paths.Select(p => p == "/checkouts/one"
                ? new CheckoutEntry(p, OneRemote, Standing: true)
                : new CheckoutEntry(p, TwoRemote, Standing: false)),
        ],
    };

    [Fact]
    public async Task A_merge_in_the_second_repository_is_made_in_its_checkout()
    {
        using var rig = new Rig().Board(SecondRow(WorkKinds.Merge));
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null, HeadSha: Tip);
        rig.H.Forge.MergeAnswer = new(true, null);
        rig.H.Wire.Json("POST", Merged("AER-1"), Fixtures.Issue("AER-1") with { StatusId = 4 });
        rig.H.Wire.Json("GET", Statuses, new[] { Fixtures.Status(4, "Done") });
        rig.H.Wire.Json("POST", Comments("AER-1"), Fixtures.Comment());

        await rig.Train.TickAsync(RuntimeHolding(rig, "/checkouts/one", "/checkouts/two"), default);

        Assert.Equal([("/checkouts/two", TwoUrl, Tip)], rig.H.Forge.MergeCalls);
        Assert.Equal(TwoUrl, Assert.Single(rig.H.Wire.To("POST", Merged("AER-1"))).Read<PullRequestMergedRequest>().Url);
        Assert.Contains("hatch: AER-1 pull request merged -> Done", rig.H.Say.Said);
    }

    [Fact]
    public async Task An_update_in_the_second_repository_is_made_in_its_checkout()
    {
        using var rig = new Rig().Board(SecondRow(WorkKinds.Update));
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null, HeadSha: Tip, Number: 7);
        rig.H.Forge.UpdateAnswer = new(UpdateOutcomes.Updated, null);
        rig.H.Wire.Json("POST", Comments("AER-1"), Fixtures.Comment());
        rig.H.Wire.Reply("PUT", BroughtCurrentPath("AER-1"), System.Net.HttpStatusCode.NoContent);

        await rig.Train.TickAsync(RuntimeHolding(rig, "/checkouts/one", "/checkouts/two"), default);

        Assert.Equal([("/checkouts/two", 7L, Tip)], rig.H.Forge.UpdateCalls);
    }

    [Fact]
    public async Task A_head_in_the_second_repository_has_its_build_read_and_put_for_that_repository()
    {
        using var rig = new Rig().Board(SecondRow(WorkKinds.Hold));
        rig.H.Wire.Json("PUT", BuildPut("AER-1"), Fixtures.Build(BuildVerdicts.Passed, TwoCanonical, Tip));
        rig.H.Forge.Answer = Built(BuildVerdicts.Passed);

        await rig.Train.TickAsync(RuntimeHolding(rig, "/checkouts/one", "/checkouts/two"), default);

        Assert.Equal([$"/checkouts/two {Tip}"], rig.H.Forge.Reads);
        var put = Assert.Single(rig.H.Wire.To("PUT", BuildPut("AER-1"))).Read<BuildCheckRequest>();
        Assert.Equal((TwoRemote, "aer-1-thing", Tip), (put.Remote, put.Branch, put.Sha));
    }

    [Fact]
    public async Task A_runner_holding_only_the_first_repository_leaves_a_pull_request_in_the_second_alone()
    {
        foreach (var kind in new[] { WorkKinds.Merge, WorkKinds.Update, WorkKinds.Hold })
        {
            using var rig = new Rig().Board(SecondRow(kind));
            rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null, HeadSha: Tip, Number: 7);
            rig.H.Forge.MergeAnswer = new(true, null);
            rig.H.Forge.UpdateAnswer = new(UpdateOutcomes.Updated, null);
            rig.H.Forge.Answer = Built(BuildVerdicts.Passed);

            await rig.Train.TickAsync(RuntimeHolding(rig, "/checkouts/one"), default);

            Assert.Empty(rig.H.Forge.MergeCalls);
            Assert.Empty(rig.H.Forge.UpdateCalls);
            Assert.Empty(rig.H.Forge.Reads);
            Assert.Empty(rig.H.Wire.To("POST", Comments("AER-1")));
            Assert.Empty(rig.H.Wire.To("PUT", BuildPut("AER-1")));
            Assert.Empty(rig.H.Say.Complained);
        }
    }

    [Fact]
    public async Task A_runner_holding_only_the_second_repository_still_merges_there()
    {
        using var rig = new Rig().Board(SecondRow(WorkKinds.Merge));
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null, HeadSha: Tip);
        rig.H.Forge.MergeAnswer = new(true, null);
        rig.H.Wire.Json("POST", Merged("AER-1"), Fixtures.Issue("AER-1") with { StatusId = 4 });
        rig.H.Wire.Json("GET", Statuses, new[] { Fixtures.Status(4, "Done") });
        rig.H.Wire.Json("POST", Comments("AER-1"), Fixtures.Comment());

        await rig.Train.TickAsync(RuntimeHolding(rig, "/checkouts/two"), default);

        Assert.Equal([("/checkouts/two", TwoUrl, Tip)], rig.H.Forge.MergeCalls);
    }

    [Fact]
    public async Task A_pull_request_url_naming_neither_repository_is_merged_in_the_first_checkout()
    {
        const string Alias = "https://alias.test/elsewhere/pull/1";
        var row = SecondRow(WorkKinds.Merge, Alias);
        // The clean check is for the first repository, as the first checkout resolves to.
        row = row with
        {
            MergeChecks =
            [
                Fixtures.MergeCheck(MergeVerdicts.Clean, "main", OneCanonical, holdsTrunk: true) with
                {
                    Remote = OneRemote, TrunkSha = Trunk, Branch = "aer-1-thing", BranchSha = Tip,
                },
            ],
        };
        using var rig = new Rig().Board(row);
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null, HeadSha: Tip);
        rig.H.Forge.MergeAnswer = new(true, null);
        rig.H.Wire.Json("POST", Merged("AER-1"), Fixtures.Issue("AER-1") with { StatusId = 4 });
        rig.H.Wire.Json("GET", Statuses, new[] { Fixtures.Status(4, "Done") });
        rig.H.Wire.Json("POST", Comments("AER-1"), Fixtures.Comment());

        await rig.Train.TickAsync(RuntimeHolding(rig, "/checkouts/one", "/checkouts/two"), default);

        Assert.Equal([("/checkouts/one", Alias, Tip)], rig.H.Forge.MergeCalls);
    }

    [Fact]
    public void A_repository_whose_name_starts_another_s_is_not_mistaken_for_it()
    {
        var short_ = Fixtures.Repository("https://example.test/o/r.git", "example.test/o/r");
        var long_ = Fixtures.Repository("https://example.test/o/r-two.git", "example.test/o/r-two");
        var polled = new[]
        {
            new Checkouts.Polled("/a", "main", short_.Remote, short_.Canonical),
            new Checkouts.Polled("/b", "main", long_.Remote, long_.Canonical),
        };

        var found = Checkouts.ForPullRequest("https://example.test/o/r-two/pull/3", [short_, long_], polled);

        Assert.Equal("/b", found?.Path);
    }
}

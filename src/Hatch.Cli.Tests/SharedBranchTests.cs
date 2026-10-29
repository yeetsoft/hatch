namespace Hatch.Cli.Tests;

/// <summary>
/// A task under a story that shares one pull request works on the story's
/// branch: which branch git is asked for, what the session is told, and what
/// the runner leaves behind. With nothing shared, every test here has a
/// counterpart in <see cref="PromptTests"/> and <see cref="LifecycleTests"/>
/// saying nothing changed.
/// </summary>
public sealed class SharedBranchTests
{
    private const string Story = "AER-5";
    private const string Task = "AER-6";

    /// <summary>A task whose dispatch the server named the story's branch for.</summary>
    private static WorkDto Shared(string key = Task, string story = Story) =>
        Fixtures.Work(key) with { BranchKey = story, BranchTitle = "The story title" };

    private static string Said(Harness h) => string.Join("\n", h.Say.Said);

    private static Checkouts.Choice Choice(Harness h) => new(h.Root, [], [(h.Root, null)], []);

    // ---- Which branch ----

    [Fact]
    public void A_dispatch_the_server_named_for_the_issue_itself_is_not_shared()
    {
        Assert.False(Branches.IsShared(Fixtures.Work(Task)));
        Assert.False(Branches.IsShared(Fixtures.Work(Task) with { BranchKey = Task }));
        Assert.Equal(Task, Branches.KeyOf(Fixtures.Work(Task)));
    }

    [Fact]
    public void A_dispatch_named_for_a_story_is_shared_and_takes_its_title()
    {
        var work = Shared();

        Assert.True(Branches.IsShared(work));
        Assert.Equal(Story, Branches.KeyOf(work));
        Assert.Equal("The story title", Branches.TitleOf(work));
    }

    [Fact]
    public void A_shared_dispatch_with_no_title_gets_no_slug_rather_than_the_key_cut_into_one()
    {
        var work = Fixtures.Work(Task) with { BranchKey = Story, BranchTitle = null };

        Assert.Equal("", Branches.TitleOf(work));
        Assert.Equal("aer-5", Branches.Cut(Branches.KeyOf(work), Branches.TitleOf(work)));
    }

    [Fact]
    public void Settle_leaves_an_unshared_dispatch_alone_and_asks_git_nothing()
    {
        using var h = new Harness();
        var work = Fixtures.Work(Task);

        var settled = new Lifecycle(h.Runtime).Settle(work, Choice(h));

        Assert.Same(work, settled);
        Assert.DoesNotContain(h.Workspace.Calls, c => c.StartsWith("presence", StringComparison.Ordinal));
    }

    [Fact]
    public void Settle_keeps_a_shared_dispatch_when_the_story_has_no_branch_yet()
    {
        using var h = new Harness();

        var settled = new Lifecycle(h.Runtime).Settle(Shared(), Choice(h));

        Assert.Equal(Story, Branches.KeyOf(settled));
    }

    [Fact]
    public void Settle_keeps_a_shared_dispatch_when_the_story_has_an_open_branch()
    {
        using var h = new Harness();
        h.Workspace.PresenceFor[(h.Root, Story)] = BranchPresence.Open;

        Assert.Equal(Story, Branches.KeyOf(new Lifecycle(h.Runtime).Settle(Shared(), Choice(h))));
    }

    [Fact]
    public void Settle_hands_a_task_with_a_branch_of_its_own_back_to_it()
    {
        using var h = new Harness();
        h.Workspace.PresenceFor[(h.Root, Task)] = BranchPresence.Open;
        h.Workspace.PresenceFor[(h.Root, Story)] = BranchPresence.Open;

        var settled = new Lifecycle(h.Runtime).Settle(Shared(), Choice(h));

        Assert.False(Branches.IsShared(settled));
        Assert.Equal(Task, Branches.KeyOf(settled));
        Assert.Contains("already has a branch of its own", Said(h), StringComparison.Ordinal);
    }

    [Fact]
    public void Settle_hands_a_task_back_when_the_storys_branch_has_already_merged()
    {
        using var h = new Harness();
        h.Workspace.PresenceFor[(h.Root, Story)] = BranchPresence.MergedOnly;

        var settled = new Lifecycle(h.Runtime).Settle(Shared(), Choice(h));

        Assert.False(Branches.IsShared(settled));
        Assert.Contains("has already merged into the trunk", Said(h), StringComparison.Ordinal);
        Assert.Contains("opens its own pull request", Said(h), StringComparison.Ordinal);
    }

    [Fact]
    public void A_merged_branch_beside_an_open_one_is_still_the_storys()
    {
        using var h = new Harness();
        h.Workspace.PresenceFor[(h.Root, Story)] = BranchPresence.Open;

        Assert.True(Branches.IsShared(new Lifecycle(h.Runtime).Settle(Shared(), Choice(h))));
    }

    [Fact]
    public async Task Entering_asks_git_for_the_storys_branch_and_names_it_by_the_storys_title()
    {
        using var h = new Harness();
        string? asked = null;
        h.Workspace.Entry = (path, key) =>
        {
            asked = key;
            return new BranchEntry { Path = path, Kind = BranchKind.None, Cut = Branches.Cut(key, "the story") };
        };

        var entering = await new Lifecycle(h.Runtime).EnterAsync(Shared(), Choice(h), default);

        Assert.Equal(Story, asked);
        Assert.Equal("aer-5-the-story", Assert.Single(entering.Entries).Cut);
    }

    [Fact]
    public async Task An_answer_to_which_of_the_storys_branches_reaches_the_workspace()
    {
        using var h = new Harness();
        var work = Shared() with
        {
            Questions =
            [
                Fixtures.Question(1, Task, Branches.Question(Story)) with { Answers = [Fixtures.Comment("aer-5-second")] },
            ],
        };

        await new Lifecycle(h.Runtime).EnterAsync(work, Choice(h), default);

        Assert.Equal("aer-5-second", Assert.Single(h.Workspace.Answers));
    }

    [Fact]
    public void Planning_a_dry_run_names_the_storys_branch_too()
    {
        using var h = new Harness();
        string? asked = null;
        h.Workspace.Entry = (path, key) =>
        {
            asked = key;
            return new BranchEntry { Path = path, Kind = BranchKind.None, Cut = "aer-5-x" };
        };

        new Lifecycle(h.Runtime).Plan(Shared(), Choice(h));

        Assert.Equal(Story, asked);
    }

    // ---- Leaving ----

    [Fact]
    public async Task Leaving_works_on_the_storys_branch_and_is_not_owed_a_pull_request()
    {
        using var h = new Harness();
        h.Wire.Json("GET", $"/api/hatch/issues/{Story}", Fixtures.Issue(Story, type: "story"));
        h.Wire.Json("PUT", $"/api/hatch/issues/{Story}/merge-check", Fixtures.MergeCheck());
        h.Workspace.FoundFor[h.Root] = new Verdict(MergeVerdicts.Clean, "main", new string('a', 40), "aer-5-the-story", new string('b', 40), []);

        var left = await new Lifecycle(h.Runtime).LeaveAsync(Shared(), Choice(h), ownsTicket: true, default);

        var leave = Assert.Single(h.Workspace.Left);
        Assert.Equal(Story, leave.Key);
        Assert.False(leave.Sync);
        Assert.True(left.HasPullRequest);
        Assert.True(left.BranchOnOrigin);
        Assert.Single(h.Wire.To("PUT", $"/api/hatch/issues/{Story}/merge-check"));
        Assert.Empty(h.Wire.To("PUT", $"/api/hatch/issues/{Task}/merge-check"));
    }

    [Fact]
    public async Task Leaving_brings_the_storys_branch_current_once_the_story_has_a_pull_request()
    {
        using var h = new Harness();
        h.Wire.Json("GET", $"/api/hatch/issues/{Story}", Fixtures.Issue(Story, type: "story", pullRequestUrl: "https://forge.example/pulls/7"));

        await new Lifecycle(h.Runtime).LeaveAsync(Shared(), Choice(h), ownsTicket: true, default);

        Assert.True(Assert.Single(h.Workspace.Left).Sync);
    }

    [Fact]
    public async Task Leaving_an_unshared_task_is_exactly_as_it_was()
    {
        using var h = new Harness();
        h.Wire.Json("GET", $"/api/hatch/issues/{Task}", Fixtures.Issue(Task));

        var left = await new Lifecycle(h.Runtime).LeaveAsync(Fixtures.Work(Task, from: "In Review"), Choice(h), ownsTicket: true, default);

        Assert.Equal(Task, Assert.Single(h.Workspace.Left).Key);
        Assert.False(left.HasPullRequest);
    }

    // ---- The prompt ----

    [Fact]
    public void A_shared_task_is_told_its_work_goes_on_the_storys_branch()
    {
        var branches = new[] { new BranchEntry { Path = "/checkouts/repo", Kind = BranchKind.None, Cut = "aer-5-the-story" } };

        var prompt = Prompt.Compose(Shared(), branches: branches);

        Assert.Contains("**This ticket's work goes on AER-5's branch,**", prompt, StringComparison.Ordinal);
        Assert.Contains("cut `aer-5-the-story` from it.", prompt, StringComparison.Ordinal);
        Assert.Contains("Push to it as the work goes, so the next task builds on it. Do not open a pull request.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("open a pull request on it before this increment ends", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shared_task_is_not_told_to_open_or_record_a_pull_request()
    {
        var prompt = Prompt.Compose(Shared());

        Assert.DoesNotContain("## Opening a pull request", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("`hatch pr AER-6 <url>`", prompt, StringComparison.Ordinal);
        Assert.Contains("## No pull request from this ticket", prompt, StringComparison.Ordinal);
        Assert.Contains("do not run `hatch pr AER-6`", prompt, StringComparison.Ordinal);
        Assert.Contains("titled with the story's key, not", prompt, StringComparison.Ordinal);
        Assert.Contains("then move AER-6 to \"In Review\"", prompt, StringComparison.Ordinal);
        Assert.Contains("hatch pr AER-6 --none", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shared_task_that_enters_the_storys_branch_is_told_to_continue_on_it()
    {
        var branches = new[]
        {
            new BranchEntry
            {
                Path = "/checkouts/repo", Kind = BranchKind.Entered, Branch = "aer-5-the-story", Sha = "abc123", Head = "abc123",
                Ahead = 4, Merge = MergeOutcome.NoOp,
            },
        };

        var prompt = Prompt.Compose(Shared(), branches: branches);

        Assert.Contains("on `aer-5-the-story` at abc123, 4 commit(s) ahead of the trunk.", prompt, StringComparison.Ordinal);
        Assert.Contains("Continue on it: do not cut a new branch", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_task_that_does_not_share_is_told_what_it_always_was()
    {
        var prompt = Prompt.Compose(Fixtures.Work(Task));

        Assert.Contains("## Opening a pull request", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("No pull request from this ticket", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("goes on AER-5's branch", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_task_handed_back_to_its_own_branch_is_told_what_it_always_was()
    {
        var prompt = Prompt.Compose(Shared() with { BranchKey = null, BranchTitle = null });

        Assert.Contains("## Opening a pull request", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("No pull request from this ticket", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_story_that_shares_is_told_the_one_pull_request_is_its_to_open()
    {
        var story = Fixtures.Issue(Story, type: "story") with { OnePr = true };

        var prompt = Prompt.Compose(Fixtures.Work(Story, issue: story));

        Assert.Contains("## Opening a pull request", prompt, StringComparison.Ordinal);
        Assert.Contains("This story's tasks share its branch, and none of them opens a pull request of its", prompt, StringComparison.Ordinal);
        Assert.Contains("the one pull request for all of them is yours to open", prompt, StringComparison.Ordinal);
        Assert.Contains("**Title:** `AER-5 ` and then", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bug_that_shares_is_told_the_one_pull_request_is_its_to_open()
    {
        var bug = Fixtures.Issue(Story, type: "bug") with { OnePr = true };

        var prompt = Prompt.Compose(Fixtures.Work(Story, issue: bug));

        Assert.Contains("This bug's tasks share its branch, and none of them opens a pull request of its", prompt, StringComparison.Ordinal);
        Assert.Contains("If this increment is the bug's closeout", prompt, StringComparison.Ordinal);
        Assert.Contains("the one pull request for all of them is yours to open", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_story_that_does_not_share_gets_the_ordinary_section()
    {
        var prompt = Prompt.Compose(Fixtures.Work(Story, issue: Fixtures.Issue(Story, type: "story")));

        Assert.DoesNotContain("share its branch", prompt, StringComparison.Ordinal);
    }
}

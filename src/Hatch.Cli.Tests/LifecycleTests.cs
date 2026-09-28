using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// The runner's git half of an increment, as the loop and <c>work</c> call it:
/// the order it happens in, what the prompt is told, and what the ticket is told.
/// The git itself is <see cref="WorkspaceTests"/>'s.
/// </summary>
public sealed class LifecycleTests
{
    private const string Queue = "/api/hatch/work/queue";

    private static void Board(Harness h, string key = "AER-1", WorkDto? work = null, string? pullRequest = null,
        HttpStatusCode comments = HttpStatusCode.OK, bool taken = false)
    {
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row(key) });
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        if (taken) h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim/heartbeat", HttpStatusCode.Conflict, "\"this claim was taken over\"");
        else h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", $"/api/hatch/issues/{key}/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", $"/api/hatch/work/{key}", work ?? Fixtures.Work(key, from: "In Review"));
        h.Wire.Json("GET", $"/api/hatch/issues/{key}", Fixtures.Issue(key, pullRequestUrl: pullRequest));
        h.Wire.Json("POST", $"/api/hatch/issues/{key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/hatch/issues/{key}/questions", Array.Empty<QuestionDto>());
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/comments", comments, comments == HttpStatusCode.OK
            ? System.Text.Json.JsonSerializer.Serialize(Fixtures.Comment(), Fixtures.Json) : "\"no\"");
    }

    /// <summary>What the runner wrote about the tree - and not the stall guard, which a fixture board that never moves the ticket also triggers.</summary>
    private static List<CommentCreateRequest> Tidied(Harness h) =>
        h.Wire.To("POST", "/api/hatch/issues/AER-1/comments").Select(c => c.Read<CommentCreateRequest>())
            .Where(c => c.Body.StartsWith("The runner tidied", StringComparison.Ordinal)).ToList();

    private static BranchEntry On(string path, string branch = "aer-1-thing", MergeOutcome merge = MergeOutcome.Clean,
        IReadOnlyList<string>? conflicted = null) =>
        new()
        {
            Path = path, Kind = BranchKind.Entered, Branch = branch, Sha = "abc123def", Head = "fed321cba",
            Ahead = 3, Merge = merge, Conflicted = conflicted ?? [],
        };

    // ---- The loop ----

    [Fact]
    public async Task A_pass_resets_then_enters_the_branch_then_spawns_then_leaves_then_lets_go()
    {
        using var h = new Harness();
        Board(h);
        h.Workspace.Entry = (path, _) => On(path);
        h.Sessions.Behaviour = (_, _, _) =>
        {
            h.Workspace.Calls.Add("spawn");
            return Task.FromResult(new SessionResult(0, ""));
        };

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Equal(
            [$"prepare {h.Root}", $"enter {h.Root}", "spawn", $"leave {h.Root}"],
            h.Workspace.Calls);

        // The release is the last thing: the ticket is still the runner's to
        // write on while the tree is put right.
        var last = h.Wire.Calls[^1];
        Assert.Equal("DELETE /api/hatch/issues/AER-1/claim", last.Route);
    }

    [Fact]
    public async Task The_loops_own_source_is_checked_on_the_trunk_before_any_branch_is_entered()
    {
        using var h = new Harness();
        Board(h);
        h.Workspace.Watching = () => h.Self.Print = FakeSelf.Of(("src/Hatch.Cli/GoToWork.cs", "after"));

        await new GoToWorkCommand(h.Supervised).RunAsync([], default);

        Assert.Equal([$"prepare {h.Root}"], h.Workspace.Calls);
        Assert.Empty(h.Sessions.Spawned);
    }

    [Fact]
    public async Task The_prompt_says_which_branch_and_what_the_merge_came_to()
    {
        using var h = new Harness();
        Board(h);
        h.Workspace.Entry = (path, _) => On(path, merge: MergeOutcome.Conflicted, conflicted: ["a.txt", "b/c.txt"]);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        var prompt = Assert.Single(h.Sessions.Spawned).Prompt;
        Assert.Contains("## The branch", prompt);
        Assert.Contains("overrides any instruction about", prompt);
        Assert.Contains("on `aer-1-thing` at abc123def, 3 commit(s) ahead of the trunk", prompt);
        Assert.Contains("  - a.txt", prompt);
        Assert.Contains("  - b/c.txt", prompt);
        Assert.Contains("Resolve them and commit the merge before you do anything else", prompt);
    }

    [Fact]
    public async Task With_no_branch_the_prompt_names_the_one_to_cut()
    {
        using var h = new Harness();
        Board(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        var prompt = Assert.Single(h.Sessions.Spawned).Prompt;
        Assert.Contains("origin has no branch for this issue", prompt);
        Assert.Contains("cut `aer-1-a-ticket`", prompt);
    }

    [Fact]
    public async Task A_merged_branch_is_named_as_such_and_a_new_one_is_offered()
    {
        using var h = new Harness();
        Board(h);
        h.Workspace.Entry = (path, _) => new BranchEntry
        {
            Path = path, Kind = BranchKind.AlreadyMerged, Merged = ["aer-1-old"], Cut = "aer-1-old-2",
        };

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        var prompt = Assert.Single(h.Sessions.Spawned).Prompt;
        Assert.Contains("`aer-1-old` has already merged into the trunk", prompt);
        Assert.Contains("cut `aer-1-old-2`", prompt);
    }

    [Fact]
    public async Task Two_branches_ask_which_and_spawn_nothing()
    {
        using var h = new Harness();
        Board(h);
        h.Workspace.Entry = (path, _) => new BranchEntry
        {
            Path = path, Kind = BranchKind.Several, Candidates = ["aer-1-a", "aer-1-b"],
        };

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Empty(h.Sessions.Spawned);

        var asked = Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments")).Read<CommentCreateRequest>();
        Assert.Equal("question", asked.Kind);
        Assert.Equal("Which branch should AER-1 continue on?", asked.Body);
        Assert.Equal(["aer-1-a", "aer-1-b"], asked.Options!.Select(o => o.Label));

        // Given back, and the tree is put back on the trunk.
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains($"return {h.Root}", h.Workspace.Calls);
    }

    [Fact]
    public async Task An_answer_to_which_branch_is_handed_to_the_workspace()
    {
        using var h = new Harness();
        var answered = Fixtures.Question(9, body: "Which branch should AER-1 continue on?", answered: true);
        Board(h, work: Fixtures.Work("AER-1", from: "In Review", questions: [answered]));

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Equal(["That way."], h.Workspace.Answers);
    }

    [Fact]
    public async Task What_the_tidy_found_goes_to_the_ticket_in_one_comment()
    {
        using var h = new Harness();
        Board(h);
        h.Workspace.LeaveNotes.Add("2 uncommitted file(s) were stashed");
        h.Workspace.LeaveNotes.Add("aer-1-thing has 1 commit(s) origin does not");

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        var comment = Assert.Single(Tidied(h));
        Assert.Contains("- 2 uncommitted file(s) were stashed", comment.Body);
        Assert.Contains("- aer-1-thing has 1 commit(s) origin does not", comment.Body);
    }

    [Fact]
    public async Task A_tidy_with_nothing_to_say_writes_nothing()
    {
        using var h = new Harness();
        Board(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Empty(Tidied(h));
    }

    [Fact]
    public async Task A_recorded_pull_request_is_synced_and_none_is_not()
    {
        using var with = new Harness();
        Board(with, pullRequest: "https://forge.example/pr/1");
        await new GoToWorkCommand(with.Runtime).RunAsync(["--once"], default);
        Assert.True(Assert.Single(with.Workspace.Left).Sync);

        using var without = new Harness();
        Board(without);
        await new GoToWorkCommand(without.Runtime).RunAsync(["--once"], default);
        Assert.False(Assert.Single(without.Workspace.Left).Sync);
    }

    [Fact]
    public async Task A_lost_lease_puts_the_tree_back_and_writes_nothing()
    {
        using var h = new Harness();
        Board(h, taken: true);
        h.Workspace.LeaveNotes.Add("a note that must not be written");
        h.Sessions.Behaviour = FakeSessions.UntilStopped();

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Empty(h.Workspace.Left);
        Assert.Contains($"return {h.Root}", h.Workspace.Calls);
        Assert.Empty(Tidied(h));
    }

    [Fact]
    public async Task A_tidy_that_throws_does_not_fail_the_increment_or_keep_the_lease()
    {
        using var h = new Harness();
        Board(h, comments: HttpStatusCode.Forbidden);
        h.Workspace.LeaveNotes.Add("something");

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains(h.Say.Complained, l => l.Contains("could not be left tidy, or the ticket told"));
    }

    // ---- hatch work ----

    [Fact]
    public async Task Work_does_not_stash_and_starts_and_ends_the_tree_as_the_loop_does()
    {
        using var h = new Harness();
        Board(h);
        h.Workspace.Entry = (path, _) => On(path);

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync(["AER-1"], default));

        Assert.Equal([false], h.Workspace.Stashed);
        Assert.Equal([$"prepare {h.Root}", $"enter {h.Root}", $"leave {h.Root}"], h.Workspace.Calls);
        Assert.Contains("on `aer-1-thing`", Assert.Single(h.Sessions.Spawned).Prompt);
    }

    [Fact]
    public async Task Work_on_a_tree_that_will_not_reset_spawns_nothing_and_gives_the_ticket_back()
    {
        using var h = new Harness();
        Board(h);
        h.Workspace.Answer = Reset.Never;

        Assert.Equal(1, await new WorkCommand(h.Runtime).RunAsync(["AER-1"], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
    }

    [Fact]
    public async Task A_dry_run_prints_the_branch_it_would_start_on_and_changes_nothing()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Workspace.Entry = (path, _) => On(path, merge: MergeOutcome.Clean);

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync(["AER-1", "--dry-run"], default));

        Assert.Equal([$"plan {h.Root}"], h.Workspace.Calls);
        Assert.Empty(h.Workspace.Prepared);
        Assert.Contains(h.Say.Said, l => l.StartsWith("# would enter:", StringComparison.Ordinal)
            && l.Contains("would be on aer-1-thing", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("## The branch", StringComparison.Ordinal));
        Assert.Empty(h.Wire.Calls.Where(c => c.Method != "GET"));
    }
}

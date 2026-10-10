using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// A conflict increment: the prompt it is handed, the verdict it is judged by,
/// what the loop does when the conflict has gone before anything is spawned, and
/// that <c>hatch work</c> does the same through the same code.
/// </summary>
public sealed class ConflictIncrementTests
{
    private const string Queue = "/api/hatch/work/queue";
    private const string Key = "AER-1";
    private const string Put = "/api/hatch/issues/AER-1/merge-check";

    private static readonly string Trunk = new('a', 40);
    private static readonly string Tip = new('b', 40);
    private static readonly string NewTrunk = new('c', 40);

    private static Verdict Conflicted(params string[] files) =>
        new(MergeVerdicts.Conflicted, "main", Trunk, "aer-1-thing", Tip, files.Length == 0 ? ["a.txt"] : files);

    private static Verdict Clean() => new(MergeVerdicts.Clean, "main", NewTrunk, "aer-1-thing", Tip, []);

    private static Rechecked Found(params Verdict[] verdicts) =>
        new([.. verdicts.Select((v, i) => new Checked($"/checkouts/repo{i}", v))], Unknown: false, Reported: true);

    // ---- The prompt ----

    [Fact]
    public void The_header_says_it_is_resolving_conflicts_and_draws_no_arrow_to_the_same_column()
    {
        var prompt = Prompt.Compose(Fixtures.ConflictWork(Key, trunk: "develop"));

        Assert.Contains("moving:   In Review (resolving conflicts with develop)", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("In Review -> In Review", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_conflict_section_names_the_branch_the_trunk_both_shas_and_the_files_from_the_fresh_verdict()
    {
        var work = Fixtures.ConflictWork(Key);
        var prompt = Prompt.Compose(work, conflict: Found(Conflicted("a.txt", "dir/b.txt")));

        Assert.Contains("## The conflict", prompt, StringComparison.Ordinal);
        Assert.Contains($"`aer-1-thing` at {Tip} does not merge with `main` at {Trunk}. Conflicted files:", prompt, StringComparison.Ordinal);
        Assert.Contains("  - a.txt\n  - dir/b.txt", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The board's verdict was read before the claim and the fetch, and may be
    /// minutes old: the section is written from what the recheck just found.
    /// </summary>
    [Fact]
    public void The_fresh_verdicts_and_not_the_boards_are_what_the_section_says()
    {
        var work = Fixtures.ConflictWork(Key, checks: [Fixtures.MergeCheck(MergeVerdicts.Conflicted, "main", files: "stale.txt")]);

        var prompt = Prompt.Compose(work, conflict: Found(Conflicted("fresh.txt")));

        Assert.Contains("fresh.txt", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("stale.txt", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dry_run_has_no_git_and_says_what_the_board_holds()
    {
        var prompt = Prompt.Compose(Fixtures.ConflictWork(Key));

        Assert.Contains("## The conflict", prompt, StringComparison.Ordinal);
        Assert.Contains("  - a.txt", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_two_repository_conflict_names_each_repository_that_conflicts_and_only_those()
    {
        var found = Found(Conflicted("a.txt"), Clean(), Conflicted("x.txt"));

        var prompt = Prompt.Compose(Fixtures.ConflictWork(Key), conflict: found);

        Assert.Contains("- repo0: `aer-1-thing`", prompt, StringComparison.Ordinal);
        Assert.Contains("- repo2: `aer-1-thing`", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("repo1", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_section_follows_the_repositories_and_comes_before_the_branch_section()
    {
        var repositories = new[] { new Checkouts.RepositoryLine("/checkouts/repo0", "git@example.test:o/r.git", true, null) };
        var branches = new[] { new BranchEntry { Path = "/checkouts/repo0", Kind = BranchKind.None, Cut = "aer-1-x" } };

        var prompt = Prompt.Compose(Fixtures.ConflictWork(Key), repositories, branches, Found(Conflicted()));

        var repos = prompt.IndexOf("## Repositories", StringComparison.Ordinal);
        var conflict = prompt.IndexOf("## The conflict", StringComparison.Ordinal);
        var branch = prompt.IndexOf("## The branch", StringComparison.Ordinal);
        Assert.True(repos >= 0 && repos < conflict && conflict < branch);
    }

    [Fact]
    public void An_ordinary_dispatch_has_no_conflict_section()
    {
        var prompt = Prompt.Compose(Fixtures.Work(Key));

        Assert.DoesNotContain("## The conflict", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_playbooks_prompt_still_comes_first()
    {
        Assert.StartsWith("Do the thing.", Prompt.Compose(Fixtures.ConflictWork(Key)), StringComparison.Ordinal);
    }

    // ---- Judging the increment ----

    private static async Task<(IncrementReport Report, Harness H)> RunAsync(
        Func<Harness, ConflictRun> conflict, string endsIn = "In Review", bool lost = false, bool questionsOpen = false,
        int letGo = 0)
    {
        var h = new Harness();
        h.Wire.Reply("POST", $"/api/hatch/issues/{Key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("DELETE", $"/api/hatch/issues/{Key}/claim", HttpStatusCode.NoContent);
        h.Wire.Reply(
            "POST", $"/api/hatch/issues/{Key}/claim/heartbeat",
            lost ? HttpStatusCode.Conflict : HttpStatusCode.NoContent, lost ? "\"this claim was taken over\"" : "");
        h.Wire.Json("POST", $"/api/hatch/issues/{Key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/hatch/work/{Key}", Fixtures.Work(Key, from: endsIn));
        h.Wire.Json("GET", $"/api/hatch/issues/{Key}/questions", questionsOpen ? new[] { Fixtures.Question(9) } : Array.Empty<QuestionDto>());
        h.Wire.Json("POST", $"/api/hatch/issues/{Key}/comments", Fixtures.Comment());
        h.Wire.Json("PUT", $"/api/hatch/issues/{Key}/stall", Fixtures.Issue(Key));

        var (claim, _) = await Claim.TakeAsync(h.Client, Key, "test:/checkout", default, Harness.Beat);
        if (lost) h.Sessions.Behaviour = FakeSessions.UntilStopped();

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.ConflictWork(Key, letGo: letGo), h.Root, "sonnet", "high", quiet: false, claim!, default, conflict: conflict(h));

        await claim!.ReleaseAsync();
        return (report, h);
    }

    private static ConflictRun Judging(Rechecked answer, Action? called = null) =>
        new(Found(Conflicted()), _ =>
        {
            called?.Invoke();
            return Task.FromResult(answer);
        });

    [Fact]
    public async Task A_branch_that_merges_cleanly_afterwards_is_resolved_and_not_a_stall()
    {
        var (report, h) = await RunAsync(_ => Judging(Found(Clean())));
        using var _h = h;

        Assert.True(report.Resolved);
        Assert.False(report.Stalled);
        Assert.False(report.Moved);
        Assert.Equal("conflicts with main resolved", report.Outcome);

        // Nothing is flagged: no question, no comment naming a session.
        Assert.Empty(h.Wire.To("POST", $"/api/hatch/issues/{Key}/comments"));
        Assert.Contains(h.Say.Said, l => l == $"hatch: {Key} conflicts with main resolved");
    }

    [Fact]
    public async Task A_branch_that_still_conflicts_is_a_stall_flagged_with_the_files_in_the_comment_and_marked()
    {
        var (report, h) = await RunAsync(_ => Judging(Found(Conflicted("a.txt", "b.txt"))), letGo: 1);
        using var _h = h;

        Assert.True(report.Stalled);
        Assert.False(report.Resolved);
        Assert.Equal("flagged", report.Flag);
        Assert.Equal(["a.txt", "b.txt"], report.StillConflicting);
        Assert.Equal("still conflicts with main, flagged", report.Outcome);

        var written = h.Wire.To("POST", $"/api/hatch/issues/{Key}/comments");
        Assert.Single(written);

        var comment = written[0].Read<CommentCreateRequest>().Body;
        Assert.Contains("still conflicts with it", comment, StringComparison.Ordinal);
        Assert.Contains("- a.txt", comment, StringComparison.Ordinal);
        Assert.Contains("- b.txt", comment, StringComparison.Ordinal);
        Assert.DoesNotContain("left this issue where it found it", comment, StringComparison.Ordinal);
        Assert.Contains("claude --resume s-1", comment, StringComparison.Ordinal);

        var marked = Assert.Single(h.Wire.To("PUT", $"/api/hatch/issues/{Key}/stall"));
        Assert.Contains("still conflicts", marked.Read<StallRequest>().Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_check_that_could_not_be_made_is_not_known_and_flags_nothing()
    {
        var unknown = new Rechecked([], Unknown: true, Reported: true);
        var (report, h) = await RunAsync(_ => Judging(unknown));
        using var _h = h;

        Assert.False(report.Stalled);
        Assert.False(report.Resolved);
        Assert.Contains("not known", report.Flag!, StringComparison.Ordinal);
        Assert.Empty(h.Wire.To("POST", $"/api/hatch/issues/{Key}/comments"));
        Assert.Contains(h.Say.Complained, l => l.Contains("could not be checked", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_check_that_throws_is_not_known_either()
    {
        var (report, h) = await RunAsync(_ => new ConflictRun(Found(Conflicted()), _ => throw new InvalidOperationException("git fell over")));
        using var _h = h;

        Assert.False(report.Stalled);
        Assert.Contains("not known", report.Flag!, StringComparison.Ordinal);
        Assert.Empty(h.Wire.To("POST", $"/api/hatch/issues/{Key}/comments"));
    }

    [Fact]
    public async Task A_lost_lease_never_asks_git_and_writes_nothing()
    {
        var called = false;
        var (report, h) = await RunAsync(_ => Judging(Found(Clean()), () => called = true), lost: true);
        using var _h = h;

        Assert.True(report.LostLease);
        Assert.False(called, "the judgement is a write onto a ticket that is somebody else's by now");
        Assert.False(report.Resolved);
        Assert.Empty(h.Wire.To("POST", $"/api/hatch/issues/{Key}/comments"));
    }

    /// <summary>
    /// A session that moved the ticket out of review is reported as having moved
    /// it - and is judged by the branch all the same.
    /// </summary>
    [Fact]
    public async Task A_session_that_moved_the_ticket_out_of_review_is_reported_as_moved_and_still_a_stall_if_it_conflicts()
    {
        var (report, h) = await RunAsync(_ => Judging(Found(Conflicted("a.txt"))), endsIn: "In Progress", letGo: 1);
        using var _h = h;

        Assert.True(report.Moved);
        Assert.Equal("In Progress", report.Ended);
        Assert.True(report.Stalled);
        Assert.Equal("flagged", report.Flag);
        Assert.Single(h.Wire.To("POST", $"/api/hatch/issues/{Key}/comments"));
        Assert.Single(h.Wire.To("PUT", $"/api/hatch/issues/{Key}/stall"));

        // The ticket moved, so the claim's own verdict is worked - even though
        // its branch still needs another pass and is flagged for it.
        Assert.Equal(ClaimOutcomes.Worked, report.ReleaseOutcome);
    }

    [Fact]
    public async Task A_conflict_increment_that_leaves_the_column_alone_is_not_a_stall_by_the_column()
    {
        var (report, h) = await RunAsync(_ => Judging(Found(Clean())), endsIn: "In Review");
        using var _h = h;

        Assert.False(report.Stalled);
        Assert.Equal("In Review", report.Ended);
    }

    [Fact]
    public async Task An_ordinary_increment_is_still_judged_by_the_column()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", $"/api/hatch/issues/{Key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("DELETE", $"/api/hatch/issues/{Key}/claim", HttpStatusCode.NoContent);
        h.Wire.Reply("POST", $"/api/hatch/issues/{Key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", $"/api/hatch/issues/{Key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/hatch/work/{Key}", Fixtures.Work(Key, letGo: 1));
        h.Wire.Json("GET", $"/api/hatch/issues/{Key}/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", $"/api/hatch/issues/{Key}/comments", Fixtures.Comment());
        var (claim, _) = await Claim.TakeAsync(h.Client, Key, "test:/checkout", default, Harness.Beat);

        var report = await h.Runtime.Increment().RunAsync(Fixtures.Work(Key, letGo: 1), h.Root, "sonnet", "high", false, claim!, default);

        Assert.True(report.Stalled);
        Assert.Contains("left this issue where it found it", h.Wire.To("POST", $"/api/hatch/issues/{Key}/comments")[0].Read<CommentCreateRequest>().Body, StringComparison.Ordinal);
        await claim!.ReleaseAsync();
    }

    [Fact]
    public async Task The_session_is_handed_the_conflict_section_from_the_recheck()
    {
        var (_, h) = await RunAsync(_ => new ConflictRun(Found(Conflicted("fresh.txt")), _ => Task.FromResult(Found(Clean()))));
        using var _h = h;

        var prompt = Assert.Single(h.Sessions.Spawned).Prompt;
        Assert.Contains("## The conflict", prompt, StringComparison.Ordinal);
        Assert.Contains("fresh.txt", prompt, StringComparison.Ordinal);
    }

    // ---- The recheck and the judgement, over the fake tree ----

    private static void Board(Harness h, WorkDto work, HttpStatusCode put = HttpStatusCode.OK)
    {
        h.Wire.Json("GET", Queue, new[] { Fixtures.ConflictRow(Key) });
        h.Wire.Reply("POST", $"/api/hatch/issues/{Key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", $"/api/hatch/issues/{Key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", $"/api/hatch/issues/{Key}/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", $"/api/hatch/work/{Key}", work);
        h.Wire.Json("GET", $"/api/hatch/issues/{Key}", Fixtures.Issue(Key));
        h.Wire.Json("POST", $"/api/hatch/issues/{Key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/hatch/issues/{Key}/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", $"/api/hatch/issues/{Key}/comments", Fixtures.Comment());
        h.Wire.Reply(
            "PUT", Put, put,
            put == HttpStatusCode.OK ? System.Text.Json.JsonSerializer.Serialize(Fixtures.MergeCheck(), Fixtures.Json) : "\"no\"");
        h.Wire.Json("PUT", $"/api/hatch/issues/{Key}/stall", Fixtures.Issue(Key));
    }

    private static List<CommentCreateRequest> Stall(Harness h) =>
        h.Wire.To("POST", $"/api/hatch/issues/{Key}/comments").Select(c => c.Read<CommentCreateRequest>())
            .Where(c => !c.Body.StartsWith("The runner tidied", StringComparison.Ordinal)).ToList();

    /// <summary>A conflict that has gone by the time the claim is held: nothing spawned, and the claim goes back.</summary>
    [Fact]
    public async Task A_recheck_that_comes_back_clean_spawns_nothing_puts_the_verdict_and_releases_the_claim()
    {
        using var h = new Harness();
        Board(h, Fixtures.ConflictWork(Key));
        h.Workspace.Verdicts[(h.Root, Key)] = Clean();

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", $"/api/hatch/issues/{Key}/claim"));
        Assert.Equal(MergeVerdicts.Clean, Assert.Single(h.Wire.To("PUT", Put)).Read<MergeCheckRequest>().Verdict);
        Assert.Contains(h.Say.Said, l => l == $"hatch: {Key} no longer conflicts with main - nothing to do");

        // Reset, checked, and that is all: the branch was never entered.
        Assert.Equal([$"prepare {h.Root}", $"check {h.Root} {Key}"], h.Workspace.Calls);
    }

    [Fact]
    public async Task A_recheck_that_comes_back_clean_is_not_counted_and_goes_straight_on_without_a_nap()
    {
        using var h = new Harness();
        Board(h, Fixtures.ConflictWork(Key));
        h.Workspace.Verdicts[(h.Root, Key)] = Clean();

        // The first read is the conflict; everything after is an empty board,
        // which is what the PUT the loop just made would have made of it.
        h.Wire.Once("GET", Queue, HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.ConflictRow(Key) }, Fixtures.Json));
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        using var stop = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Runtime).RunAsync(["--interval", "600", "--max-runs", "1"], stop.Token);

        // A second read of the queue inside the test's own patience is the
        // proof there was no ten-minute nap in between.
        await Harness.Eventually(() => h.Wire.Count("GET", Queue) >= 2, "the loop to read the board again without waiting");
        await stop.CancelAsync();
        await running;

        Assert.Empty(h.Sessions.Spawned);
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("--max-runs 1 reached", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_recheck_whose_put_is_refused_naps_because_the_board_still_calls_it_conflicted()
    {
        using var h = new Harness();
        Board(h, Fixtures.ConflictWork(Key), put: HttpStatusCode.BadRequest);
        h.Workspace.Verdicts[(h.Root, Key)] = Clean();

        using var stop = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Runtime).RunAsync(["--interval", "600"], stop.Token);
        await Harness.Eventually(() => h.Wire.Count("DELETE", $"/api/hatch/issues/{Key}/claim") >= 1, "the claim to go back");

        // Long enough for a loop that did not nap to have read the queue again.
        await Task.Delay(400);
        await stop.CancelAsync();
        await running;

        Assert.Equal(1, h.Wire.Count("GET", Queue));
        Assert.Empty(h.Sessions.Spawned);
    }

    [Fact]
    public async Task A_recheck_that_could_not_be_made_spawns_nothing_and_naps()
    {
        using var h = new Harness();
        Board(h, Fixtures.ConflictWork(Key));

        using var stop = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Runtime).RunAsync(["--interval", "600"], stop.Token);
        await Harness.Eventually(() => h.Wire.Count("DELETE", $"/api/hatch/issues/{Key}/claim") >= 1, "the claim to go back");
        await Task.Delay(400);
        await stop.CancelAsync();
        await running;

        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Wire.To("PUT", Put));
        Assert.Equal(1, h.Wire.Count("GET", Queue));
        Assert.Contains(h.Say.Complained, l => l.Contains("could not be checked against the trunk", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_recheck_that_conflicts_checks_before_it_enters_and_then_spawns()
    {
        using var h = new Harness();
        Board(h, Fixtures.ConflictWork(Key));
        h.Workspace.Verdicts[(h.Root, Key)] = Conflicted("fresh.txt");
        h.Sessions.Behaviour = (_, _, _) =>
        {
            h.Workspace.Calls.Add("spawn");
            return Task.FromResult(new SessionResult(0, ""));
        };

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        var calls = h.Workspace.Calls;
        Assert.Equal(
            [$"prepare {h.Root}", $"check {h.Root} {Key}", $"enter {h.Root}", "spawn"],
            calls.Take(4));

        var prompt = Assert.Single(h.Sessions.Spawned).Prompt;
        Assert.Contains("fresh.txt", prompt, StringComparison.Ordinal);
        Assert.Contains("moving:   In Review (resolving conflicts with main)", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_conflict_increment_is_judged_after_the_session_by_fetching_and_checking_again_and_is_not_a_stall_when_resolved()
    {
        using var h = new Harness();
        Board(h, Fixtures.ConflictWork(Key));

        // Conflicted before the session, clean after it: what a session that
        // resolved the merge and pushed leaves behind.
        h.Sessions.Behaviour = (_, _, _) =>
        {
            h.Workspace.Verdicts[(h.Root, Key)] = Clean();
            return Task.FromResult(new SessionResult(0, ""));
        };
        h.Workspace.Verdicts[(h.Root, Key)] = Conflicted();

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--max-runs", "1"], default);

        var afterSession = h.Workspace.Calls.SkipWhile(c => !c.StartsWith("enter", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            [$"enter {h.Root}", $"fetch {h.Root}", $"check {h.Root} {Key}", $"leave {h.Root}"],
            afterSession);

        Assert.Empty(Stall(h));
        Assert.Contains(h.Say.Said, l => l.Contains($"{Key} conflicts with main resolved", StringComparison.Ordinal));

        // Both verdicts were put: the recheck's, and the judgement's.
        Assert.Equal(
            [MergeVerdicts.Conflicted, MergeVerdicts.Clean],
            h.Wire.To("PUT", Put).Select(c => c.Read<MergeCheckRequest>().Verdict));
    }

    [Fact]
    public async Task A_conflict_that_is_still_there_after_the_session_costs_one_increment_and_is_flagged()
    {
        using var h = new Harness();
        Board(h, Fixtures.ConflictWork(Key, letGo: 1));
        h.Workspace.Verdicts[(h.Root, Key)] = Conflicted("still.txt");

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Single(h.Sessions.Spawned);
        var written = Stall(h);
        Assert.Single(written);
        Assert.Contains("- still.txt", written[0].Body, StringComparison.Ordinal);

        var marked = Assert.Single(h.Wire.To("PUT", $"/api/hatch/issues/{Key}/stall"));
        Assert.Contains("still conflicts", marked.Read<StallRequest>().Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fetch_that_fails_after_the_session_is_not_known_and_flags_nothing()
    {
        using var h = new Harness();
        Board(h, Fixtures.ConflictWork(Key));
        h.Workspace.Verdicts[(h.Root, Key)] = Conflicted();
        h.Sessions.Behaviour = (_, _, _) =>
        {
            h.Workspace.FetchAnswer = false;
            return Task.FromResult(new SessionResult(0, ""));
        };

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Empty(Stall(h));
        Assert.Contains(h.Say.Complained, l => l.Contains("could not fetch from origin", StringComparison.Ordinal));
    }

    // ---- hatch work ----

    [Fact]
    public async Task Work_on_a_conflicted_issue_whose_conflict_has_gone_returns_zero_and_spawns_nothing()
    {
        using var h = new Harness();
        Board(h, Fixtures.ConflictWork(Key));
        h.Workspace.Verdicts[(h.Root, Key)] = Clean();

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync([Key], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", $"/api/hatch/issues/{Key}/claim"));
        Assert.Contains(h.Say.Said, l => l == $"hatch: {Key} no longer conflicts with main - nothing to do");
        Assert.DoesNotContain(h.Workspace.Calls, c => c.StartsWith("enter", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Work_that_cannot_check_the_branch_spawns_nothing_and_says_why()
    {
        using var h = new Harness();
        Board(h, Fixtures.ConflictWork(Key));

        Assert.Equal(1, await new WorkCommand(h.Runtime).RunAsync([Key], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", $"/api/hatch/issues/{Key}/claim"));
    }

    [Fact]
    public async Task Work_on_a_conflict_spawns_after_entry_and_is_judged_by_the_verdict()
    {
        using var h = new Harness();
        Board(h, Fixtures.ConflictWork(Key));
        h.Workspace.Verdicts[(h.Root, Key)] = Conflicted("fresh.txt");
        h.Sessions.Behaviour = (_, _, _) =>
        {
            h.Workspace.Verdicts[(h.Root, Key)] = Clean();
            return Task.FromResult(new SessionResult(0, ""));
        };

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync([Key], default));

        Assert.Equal(
            [$"prepare {h.Root}", $"check {h.Root} {Key}", $"enter {h.Root}"],
            h.Workspace.Calls.Take(3));
        Assert.Contains("fresh.txt", Assert.Single(h.Sessions.Spawned).Prompt, StringComparison.Ordinal);
        Assert.Contains(h.Say.Said, l => l.Contains($"{Key} conflicts with main resolved", StringComparison.Ordinal));
        Assert.Empty(Stall(h));
    }

    [Fact]
    public async Task Work_dry_run_composes_the_conflict_section_from_the_boards_verdicts_and_checks_nothing()
    {
        using var h = new Harness();
        h.Wire.Json("GET", $"/api/hatch/work/{Key}", Fixtures.ConflictWork(Key, trunk: "develop"));

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync([Key, "--dry-run"], default));

        Assert.Contains(h.Say.Said, l => l == $"# {Key} In Review, resolving conflicts with develop");
        Assert.Contains(h.Say.Said, l => l.Contains("## The conflict", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("- a.txt", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Workspace.Calls, c => c.StartsWith("check", StringComparison.Ordinal) || c.StartsWith("fetch", StringComparison.Ordinal));
        Assert.Empty(h.Sessions.Spawned);
    }

    [Fact]
    public async Task Work_attached_hands_the_fresh_conflict_to_the_session_it_sits_in()
    {
        using var h = new Harness();
        Board(h, Fixtures.ConflictWork(Key));
        h.Workspace.Verdicts[(h.Root, Key)] = Conflicted("fresh.txt");

        await new WorkCommand(h.Runtime).RunAsync([Key, "--interactive"], default);

        var prompt = Assert.Single(h.Sessions.Attached).Prompt;
        Assert.Contains("fresh.txt", prompt, StringComparison.Ordinal);
    }
}

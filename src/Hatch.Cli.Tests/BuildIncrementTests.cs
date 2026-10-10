using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// A build increment: the recheck that decides a session is worth spending, the
/// prompt it is handed, the push it is judged by, and that <c>hatch work</c> does
/// the same through the same code. Nothing on the server hands out build work
/// yet in this file's world - every path is reached from a dispatch a test builds.
/// </summary>
public sealed class BuildIncrementTests
{
    private const string Queue = "/api/hatch/work/queue";
    private const string Key = "AER-1";
    private const string Put = "/api/hatch/issues/AER-1/build-check";

    private static readonly string Trunk = new('a', 40);
    private static readonly string Tip = new('b', 40);
    private static readonly string Pushed = new('e', 40);

    private static RemoteHeads Heads(string tip) =>
        new("main", new Dictionary<string, string> { ["main"] = Trunk, ["aer-1-thing"] = tip });

    private static ForgeAnswer Failed(params string[] names) =>
        new(new BuildRead(
            BuildVerdicts.Failed,
            (names.Length == 0 ? ["api", "CI"] : names).Select((n, i) => new FailingCheck(n, $"https://example.test/checks/{n}", 100 + i)).ToList()),
            null);

    private static ForgeAnswer Read(string verdict) => new(new BuildRead(verdict, []), null);

    private static BuildFound Found(bool unknown = false) =>
        new([new BuiltRepo("/checkouts/repo", "https://example.test/repo.git", "aer-1-thing", Tip, Tip, BuildVerdicts.Failed,
            [new FailingBuild("api", "https://example.test/checks/api", "boom", 100), new FailingBuild("CI", null, null)])],
            unknown, Reported: true);

    // ---- The prompt ----

    [Fact]
    public void The_header_says_it_is_fixing_its_failing_build_and_draws_no_arrow_to_the_same_column()
    {
        var prompt = Prompt.Compose(Fixtures.BuildWork(Key));

        Assert.Contains("moving:   In Review (fixing its failing build)", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("In Review -> In Review", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_section_names_the_branch_the_sha_each_failing_check_with_its_link_and_the_log_from_the_fresh_read()
    {
        var prompt = Prompt.Compose(Fixtures.BuildWork(Key), build: Found());

        Assert.Contains("## The failing build", prompt, StringComparison.Ordinal);
        Assert.Contains($"`aer-1-thing` at {Tip}. Failing checks:", prompt, StringComparison.Ordinal);
        Assert.Contains("  - api: https://example.test/checks/api", prompt, StringComparison.Ordinal);
        Assert.Contains("    ```\n    boom\n    ```", prompt, StringComparison.Ordinal);
        Assert.Contains("`gh run view --job 100 --log-failed`", prompt, StringComparison.Ordinal);

        // A check with no log says so, and has no command to print one.
        Assert.Contains("  - CI\n    _no log_", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dry_run_has_no_forge_and_says_what_the_board_holds_with_no_log()
    {
        var prompt = Prompt.Compose(Fixtures.BuildWork(Key));

        Assert.Contains("## The failing build", prompt, StringComparison.Ordinal);
        Assert.Contains("  - api: https://example.test/repo/checks/api", prompt, StringComparison.Ordinal);
        Assert.Contains("    _no log_", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_log_that_contains_a_code_fence_cannot_end_the_block_early()
    {
        var found = new BuildFound(
            [new BuiltRepo("/checkouts/repo", "r", "b", Tip, Tip, BuildVerdicts.Failed,
                [new FailingBuild("api", null, "before\n```\ninside\n````\nafter", 1)])],
            false, true);

        var prompt = Prompt.Compose(Fixtures.BuildWork(Key), build: found);

        Assert.Contains("    `````\n    before", prompt, StringComparison.Ordinal);
        Assert.Contains("    after\n    `````", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_section_is_capped_and_past_the_cap_a_log_is_left_out_and_the_command_that_prints_it_is_named()
    {
        var big = string.Join('\n', Enumerable.Range(0, 200).Select(i => new string('x', 100) + i));
        Assert.True(big.Length > 12 * 1024);

        var checks = Enumerable.Range(0, 6).Select(i => new FailingBuild($"check{i}", null, big[..(12 * 1024)], 500 + i)).ToList();
        var found = new BuildFound([new BuiltRepo("/checkouts/repo", "r", "b", Tip, Tip, BuildVerdicts.Failed, checks)], false, true);

        var prompt = Prompt.Compose(Fixtures.BuildWork(Key), build: found);

        var section = prompt[prompt.IndexOf("## The failing build", StringComparison.Ordinal)..];
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(section) < Prompt.MaxBuildSectionBytes + 4096);
        Assert.Contains("_log left out: this section is at its size cap - `gh run view --job 505 --log-failed` prints it_", prompt, StringComparison.Ordinal);
        Assert.Contains("check0", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_two_repository_failure_names_each_repository_that_fails_and_only_those()
    {
        var found = new BuildFound(
            [
                new BuiltRepo("/checkouts/repo0", "r0", "b", Tip, Tip, BuildVerdicts.Failed, [new FailingBuild("api", null, null)]),
                new BuiltRepo("/checkouts/repo1", "r1", "b", Tip, Tip, BuildVerdicts.Passed, []),
                new BuiltRepo("/checkouts/repo2", "r2", "b", Tip, Tip, BuildVerdicts.Failed, [new FailingBuild("web", null, null)]),
            ],
            false, true);

        var prompt = Prompt.Compose(Fixtures.BuildWork(Key), build: found);

        Assert.Contains("- repo0: `b`", prompt, StringComparison.Ordinal);
        Assert.Contains("- repo2: `b`", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("repo1", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_section_comes_after_the_repositories_and_before_the_branch_section()
    {
        var repositories = new[] { new Checkouts.RepositoryLine("/checkouts/repo", "git@example.test:o/r.git", true, null) };
        var branches = new[] { new BranchEntry { Path = "/checkouts/repo", Kind = BranchKind.None, Cut = "aer-1-x" } };

        var prompt = Prompt.Compose(Fixtures.BuildWork(Key), repositories, branches, build: Found());

        var repos = prompt.IndexOf("## Repositories", StringComparison.Ordinal);
        var build = prompt.IndexOf("## The failing build", StringComparison.Ordinal);
        var branch = prompt.IndexOf("## The branch", StringComparison.Ordinal);
        Assert.True(repos >= 0 && repos < build && build < branch);
    }

    [Fact]
    public void An_ordinary_dispatch_and_a_conflict_have_no_failing_build_section()
    {
        Assert.DoesNotContain("## The failing build", Prompt.Compose(Fixtures.Work(Key)), StringComparison.Ordinal);
        Assert.DoesNotContain("## The failing build", Prompt.Compose(Fixtures.ConflictWork(Key)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_queue_words_name_the_distinct_failing_checks_in_the_boards_order()
    {
        var issue = Fixtures.Issue(Key) with
        {
            BuildChecks =
            [
                Fixtures.Build(BuildVerdicts.Failed, "example.test/one", failing: ["api", "CI"]),
                Fixtures.Build(BuildVerdicts.Passed, "example.test/two"),
                Fixtures.Build(BuildVerdicts.Failed, "example.test/three", failing: ["CI", "web"]),
            ],
        };

        Assert.Equal("fixing its failing build (api, CI, web)", Builds.Words(issue));
    }

    // ---- Judging the increment ----

    private static async Task<(IncrementReport Report, Harness H)> RunAsync(
        Func<Harness, BuildRun> build, string endsIn = "In Review", bool lost = false, int letGo = 0)
    {
        var h = new Harness();
        h.Wire.Reply("POST", $"/api/hatch/issues/{Key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("DELETE", $"/api/hatch/issues/{Key}/claim", HttpStatusCode.NoContent);
        h.Wire.Reply(
            "POST", $"/api/hatch/issues/{Key}/claim/heartbeat",
            lost ? HttpStatusCode.Conflict : HttpStatusCode.NoContent, lost ? "\"this claim was taken over\"" : "");
        h.Wire.Json("POST", $"/api/hatch/issues/{Key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/hatch/work/{Key}", Fixtures.Work(Key, from: endsIn));
        h.Wire.Json("GET", $"/api/hatch/issues/{Key}/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", $"/api/hatch/issues/{Key}/comments", Fixtures.Comment());
        h.Wire.Json("PUT", $"/api/hatch/issues/{Key}/stall", Fixtures.Issue(Key));

        var (claim, _) = await Claim.TakeAsync(h.Client, Key, "test:/checkout", default, Harness.Beat);
        if (lost) h.Sessions.Behaviour = FakeSessions.UntilStopped();

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.BuildWork(Key, letGo: letGo), h.Root, "sonnet", "high", quiet: false, claim!, default, build: build(h));

        await claim!.ReleaseAsync();
        return (report, h);
    }

    private static BuildRun Judging(BuildJudged answer, Action? called = null) =>
        new(Found(), _ =>
        {
            called?.Invoke();
            return Task.FromResult(answer);
        });

    [Fact]
    public async Task A_session_that_pushed_a_new_tip_is_a_fix_and_not_a_stall()
    {
        var (report, h) = await RunAsync(_ => Judging(new BuildJudged(Pushed: true, Unknown: false)));
        using var _h = h;

        Assert.True(report.FixPushed);
        Assert.False(report.Stalled);
        Assert.False(report.Moved);
        Assert.Equal("fix pushed, build pending", report.Outcome);
        Assert.Empty(h.Wire.To("POST", $"/api/hatch/issues/{Key}/comments"));
        Assert.Contains(h.Say.Said, l => l == $"hatch: {Key} fix pushed - the build on it is pending");
    }

    [Fact]
    public async Task A_session_that_pushed_nothing_is_a_stall_flagged_with_the_checks_in_the_comment_and_a_question()
    {
        var (report, h) = await RunAsync(_ => Judging(new BuildJudged(Pushed: false, Unknown: false)), letGo: 1);
        using var _h = h;

        Assert.True(report.Stalled);
        Assert.False(report.FixPushed);
        Assert.Equal("flagged", report.Flag);
        Assert.Equal(["api", "CI"], report.StillFailing);
        Assert.Equal("its build still fails (api, CI), flagged", report.Outcome);

        var written = h.Wire.To("POST", $"/api/hatch/issues/{Key}/comments");
        Assert.Single(written);

        var comment = written[0].Read<CommentCreateRequest>().Body;
        Assert.Contains("failing build", comment, StringComparison.Ordinal);
        Assert.Contains("has not moved", comment, StringComparison.Ordinal);
        Assert.Contains("- api", comment, StringComparison.Ordinal);
        Assert.Contains("- CI", comment, StringComparison.Ordinal);
        Assert.Contains("claude --resume s-1", comment, StringComparison.Ordinal);

        var marked = Assert.Single(h.Wire.To("PUT", $"/api/hatch/issues/{Key}/stall"));
        Assert.Contains("still fails", marked.Read<StallRequest>().Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tip_that_could_not_be_read_is_neither_a_fix_nor_a_stall_and_flags_nothing()
    {
        var (report, h) = await RunAsync(_ => Judging(new BuildJudged(Pushed: false, Unknown: true)));
        using var _h = h;

        Assert.False(report.Stalled);
        Assert.False(report.FixPushed);
        Assert.Contains("not known", report.Flag!, StringComparison.Ordinal);
        Assert.Empty(h.Wire.To("POST", $"/api/hatch/issues/{Key}/comments"));
    }

    [Fact]
    public async Task A_check_that_throws_is_not_known_either()
    {
        var (report, h) = await RunAsync(_ => new BuildRun(Found(), _ => throw new InvalidOperationException("git fell over")));
        using var _h = h;

        Assert.False(report.Stalled);
        Assert.Contains("not known", report.Flag!, StringComparison.Ordinal);
        Assert.Empty(h.Wire.To("POST", $"/api/hatch/issues/{Key}/comments"));
    }

    [Fact]
    public async Task A_lost_lease_judges_nothing()
    {
        var called = false;
        var (report, h) = await RunAsync(_ => Judging(new BuildJudged(true, false), () => called = true), lost: true);
        using var _h = h;

        Assert.True(report.LostLease);
        Assert.False(called, "the judgement is a write onto a ticket that is somebody else's by now");
        Assert.False(report.FixPushed);
        Assert.Empty(h.Wire.To("POST", $"/api/hatch/issues/{Key}/comments"));
    }

    [Fact]
    public async Task A_build_increment_is_not_a_stall_by_the_column()
    {
        var (report, h) = await RunAsync(_ => Judging(new BuildJudged(true, false)), endsIn: "In Review");
        using var _h = h;

        Assert.False(report.Stalled);
        Assert.Equal("In Review", report.Ended);
    }

    [Fact]
    public async Task The_session_is_handed_the_failing_build_section_from_the_recheck()
    {
        var (_, h) = await RunAsync(_ => new BuildRun(Found(), _ => Task.FromResult(new BuildJudged(true, false))));
        using var _h = h;

        var prompt = Assert.Single(h.Sessions.Spawned).Prompt;
        Assert.Contains("## The failing build", prompt, StringComparison.Ordinal);
        Assert.Contains("boom", prompt, StringComparison.Ordinal);
    }

    // ---- The recheck and the judgement, over the fake tree and the fake forge ----

    private static void Board(Harness h, WorkDto? work = null)
    {
        h.Wire.Json("GET", Queue, new[] { Fixtures.BuildRow(Key, "api", "CI") });
        h.Wire.Reply("POST", $"/api/hatch/issues/{Key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", $"/api/hatch/issues/{Key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", $"/api/hatch/issues/{Key}/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", $"/api/hatch/work/{Key}", work ?? Fixtures.BuildWork(Key));
        h.Wire.Json("GET", $"/api/hatch/issues/{Key}", Fixtures.Issue(Key));
        h.Wire.Json("POST", $"/api/hatch/issues/{Key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/hatch/issues/{Key}/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", $"/api/hatch/issues/{Key}/comments", Fixtures.Comment());
        h.Wire.Json("PUT", Put, Fixtures.Build());

        h.Workspace.HeadsFor[h.Root] = Heads(Tip);
    }

    private static List<CommentCreateRequest> Stall(Harness h) =>
        h.Wire.To("POST", $"/api/hatch/issues/{Key}/comments").Select(c => c.Read<CommentCreateRequest>())
            .Where(c => !c.Body.StartsWith("The runner tidied", StringComparison.Ordinal)).ToList();

    [Theory]
    [InlineData(BuildVerdicts.Passed)]
    [InlineData(BuildVerdicts.Pending)]
    [InlineData(BuildVerdicts.None)]
    public async Task A_recheck_that_finds_the_build_no_longer_failing_spawns_nothing_reports_it_and_releases_the_claim(string verdict)
    {
        using var h = new Harness();
        Board(h);
        h.Forge.Answer = Read(verdict);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", $"/api/hatch/issues/{Key}/claim"));

        var put = Assert.Single(h.Wire.To("PUT", Put)).Read<BuildCheckRequest>();
        Assert.Equal(verdict, put.Verdict);
        Assert.Equal(Tip, put.Sha);
        Assert.False(put.PushedByIncrement);
        Assert.Contains(h.Say.Said, l => l == $"hatch: {Key} its build is no longer failing - nothing to do");

        // Reset, read origin's tip, and that is all: the branch was never entered.
        Assert.Equal([$"prepare {h.Root}", $"heads {h.Root}"], h.Workspace.Calls);
        Assert.Empty(h.Forge.Logs);
    }

    [Fact]
    public async Task A_tip_that_moved_since_the_board_read_spawns_nothing_and_reports_the_build_on_the_new_tip()
    {
        using var h = new Harness();
        Board(h);
        h.Workspace.HeadsFor[h.Root] = Heads(Pushed);
        h.Forge.Answer = Failed();

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Equal([$"{h.Root} {Pushed}"], h.Forge.Reads);
        Assert.Equal(Pushed, Assert.Single(h.Wire.To("PUT", Put)).Read<BuildCheckRequest>().Sha);
        Assert.Contains(h.Say.Said, l => l.Contains("has moved since the build failed", StringComparison.Ordinal));
        Assert.Empty(h.Forge.Logs);
    }

    [Fact]
    public async Task A_forge_that_cannot_answer_at_the_recheck_spawns_nothing_and_naps()
    {
        using var h = new Harness();
        Board(h);
        h.Forge.Answer = new ForgeAnswer(null, "gh is not installed");

        using var stop = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Runtime).RunAsync(["--interval", "600"], stop.Token);
        await Harness.Eventually(() => h.Wire.Count("DELETE", $"/api/hatch/issues/{Key}/claim") >= 1, "the claim to go back");
        await Task.Delay(400);
        await stop.CancelAsync();
        await running;

        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Wire.To("PUT", Put));
        Assert.Equal(1, h.Wire.Count("GET", Queue));
        Assert.Contains(h.Say.Complained, l => l.Contains("gh is not installed", StringComparison.Ordinal));
        Assert.Contains(h.Say.Complained, l => l.Contains("its build could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_recheck_whose_put_is_refused_naps_because_the_board_still_calls_it_failed()
    {
        using var h = new Harness();
        Board(h);
        h.Wire.Replace("PUT", Put, HttpStatusCode.BadRequest, "\"no\"");
        h.Forge.Answer = Read(BuildVerdicts.Passed);

        using var stop = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Runtime).RunAsync(["--interval", "600"], stop.Token);
        await Harness.Eventually(() => h.Wire.Count("DELETE", $"/api/hatch/issues/{Key}/claim") >= 1, "the claim to go back");
        await Task.Delay(400);
        await stop.CancelAsync();
        await running;

        Assert.Equal(1, h.Wire.Count("GET", Queue));
        Assert.Empty(h.Sessions.Spawned);
    }

    [Fact]
    public async Task A_recheck_that_still_fails_reads_the_logs_checks_before_it_enters_and_then_spawns_with_them_in_the_prompt()
    {
        using var h = new Harness();
        Board(h);
        h.Forge.Answer = Failed("api", "CI");
        h.Forge.Excerpts["api"] = "Failed IssueClaimTests.X\n##[error]Process completed with exit code 1.";
        h.Sessions.Behaviour = (_, _, _) =>
        {
            h.Workspace.Calls.Add("spawn");
            h.Workspace.HeadsFor[h.Root] = Heads(Pushed);
            return Task.FromResult(new SessionResult(0, ""));
        };

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Equal(
            [$"prepare {h.Root}", $"heads {h.Root}", $"enter {h.Root}", "spawn"],
            h.Workspace.Calls.Take(4));

        // Only the failing checks' logs were asked for, and only now.
        Assert.Equal(["api", "CI"], h.Forge.Logs);

        var prompt = Assert.Single(h.Sessions.Spawned).Prompt;
        Assert.Contains("moving:   In Review (fixing its failing build)", prompt, StringComparison.Ordinal);
        Assert.Contains("Failed IssueClaimTests.X", prompt, StringComparison.Ordinal);
        Assert.Contains("  - CI: https://example.test/checks/CI\n    _no log_", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fix_pushed_by_the_session_is_told_to_the_board_as_pending_and_marked_and_is_not_a_stall()
    {
        using var h = new Harness();
        Board(h);
        h.Forge.Answer = Failed();
        h.Sessions.Behaviour = (_, _, _) =>
        {
            h.Workspace.HeadsFor[h.Root] = Heads(Pushed);
            return Task.FromResult(new SessionResult(0, ""));
        };

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--max-runs", "1"], default);

        var puts = h.Wire.To("PUT", Put).Select(c => c.Read<BuildCheckRequest>()).ToList();
        Assert.Equal(2, puts.Count);
        Assert.Equal(BuildVerdicts.Failed, puts[0].Verdict);
        Assert.False(puts[0].PushedByIncrement);

        Assert.Equal(BuildVerdicts.Pending, puts[1].Verdict);
        Assert.Equal(Pushed, puts[1].Sha);
        Assert.Equal("aer-1-thing", puts[1].Branch);
        Assert.Equal("https://example.test/repo.git", puts[1].Remote);
        Assert.True(puts[1].PushedByIncrement);

        Assert.Empty(Stall(h));
        Assert.Contains(h.Say.Said, l => l.Contains($"{Key} fix pushed, build pending", StringComparison.Ordinal));

        // Judged before the tree is left, so a trunk merge pushed on the way out
        // is never taken for the session's fix.
        var calls = h.Workspace.Calls.SkipWhile(c => !c.StartsWith("enter", StringComparison.Ordinal)).ToList();
        Assert.Equal([$"enter {h.Root}", $"heads {h.Root}", $"leave {h.Root}"], calls);
    }

    [Fact]
    public async Task A_session_that_pushed_nothing_costs_one_increment_and_is_flagged()
    {
        using var h = new Harness();
        Board(h, Fixtures.BuildWork(Key, letGo: 1));
        h.Forge.Answer = Failed("api");

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Single(h.Sessions.Spawned);
        var written = Stall(h);
        Assert.Equal(2, written.Count);
        Assert.Contains("- api", written[0].Body, StringComparison.Ordinal);
        Assert.Equal("question", written[1].Kind);

        // Only the recheck's verdict was put: nothing was pushed to mark.
        Assert.Single(h.Wire.To("PUT", Put));
    }

    [Fact]
    public async Task A_tip_that_cannot_be_read_after_the_session_flags_nothing()
    {
        using var h = new Harness();
        Board(h);
        h.Forge.Answer = Failed();
        h.Sessions.Behaviour = (_, _, _) =>
        {
            h.Workspace.HeadsFor[h.Root] = null;
            return Task.FromResult(new SessionResult(0, ""));
        };

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Empty(Stall(h));
        Assert.Contains(h.Say.Complained, l => l.Contains("whether a fix was pushed could not be checked", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_fix_in_one_of_two_repositories_is_progress_and_not_a_stall()
    {
        var one = Fixtures.Repository("https://example.test/one.git", "example.test/one", primary: true, matchedRemote: "https://example.test/one.git");
        var two = Fixtures.Repository("https://example.test/two.git", "example.test/two", primary: false, matchedRemote: "https://example.test/two.git");
        using var h = new Harness();
        var work = Fixtures.BuildWork(
            Key,
            [
                Fixtures.Build(BuildVerdicts.Failed, "example.test/one", failing: ["api"]),
                Fixtures.Build(BuildVerdicts.Failed, "example.test/two", failing: ["web"]),
            ],
            [one, two]);
        Board(h, work);
        var runtime = h.Runtime with
        {
            Checkouts =
            [
                new CheckoutEntry("/checkouts/one", "https://example.test/one.git", Standing: true),
                new CheckoutEntry("/checkouts/two", "https://example.test/two.git", Standing: false),
            ],
        };
        foreach (var path in new[] { "/checkouts/one", "/checkouts/two" }) h.Workspace.HeadsFor[path] = Heads(Tip);
        h.Forge.Reading = (path, _) => Failed(path.EndsWith("one", StringComparison.Ordinal) ? "api" : "web");
        h.Sessions.Behaviour = (_, _, _) =>
        {
            h.Workspace.HeadsFor["/checkouts/two"] = Heads(Pushed);
            return Task.FromResult(new SessionResult(0, ""));
        };

        await new GoToWorkCommand(runtime).RunAsync(["--once"], default);

        Assert.Empty(Stall(h));
        var marked = h.Wire.To("PUT", Put).Select(c => c.Read<BuildCheckRequest>()).Where(p => p.PushedByIncrement).ToList();
        Assert.Equal("https://example.test/two.git", Assert.Single(marked).Remote);
    }

    // ---- hatch work ----

    [Fact]
    public async Task Work_on_an_issue_whose_build_has_gone_green_returns_zero_and_spawns_nothing()
    {
        using var h = new Harness();
        Board(h);
        h.Forge.Answer = Read(BuildVerdicts.Passed);

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync([Key], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", $"/api/hatch/issues/{Key}/claim"));
        Assert.Contains(h.Say.Said, l => l == $"hatch: {Key} its build is no longer failing - nothing to do");
        Assert.DoesNotContain(h.Workspace.Calls, c => c.StartsWith("enter", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Work_that_cannot_read_the_build_spawns_nothing_and_says_why()
    {
        using var h = new Harness();
        Board(h);
        h.Forge.Answer = new ForgeAnswer(null, "gh is not installed");

        Assert.Equal(1, await new WorkCommand(h.Runtime).RunAsync([Key], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", $"/api/hatch/issues/{Key}/claim"));
    }

    [Fact]
    public async Task Work_on_a_failing_build_spawns_after_entry_and_is_judged_by_the_push()
    {
        using var h = new Harness();
        Board(h);
        h.Forge.Answer = Failed("api");
        h.Forge.Excerpts["api"] = "fresh failure";
        h.Sessions.Behaviour = (_, _, _) =>
        {
            h.Workspace.HeadsFor[h.Root] = Heads(Pushed);
            return Task.FromResult(new SessionResult(0, ""));
        };

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync([Key], default));

        Assert.Equal(
            [$"prepare {h.Root}", $"heads {h.Root}", $"enter {h.Root}"],
            h.Workspace.Calls.Take(3));
        Assert.Contains("fresh failure", Assert.Single(h.Sessions.Spawned).Prompt, StringComparison.Ordinal);
        Assert.Contains(h.Say.Said, l => l.Contains($"{Key} fix pushed", StringComparison.Ordinal));
        Assert.Empty(Stall(h));
        Assert.True(h.Wire.To("PUT", Put).Last().Read<BuildCheckRequest>().PushedByIncrement);
    }

    [Fact]
    public async Task Work_dry_run_composes_the_section_from_the_boards_verdicts_and_reads_nothing()
    {
        using var h = new Harness();
        h.Wire.Json("GET", $"/api/hatch/work/{Key}", Fixtures.BuildWork(Key));

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync([Key, "--dry-run"], default));

        Assert.Contains(h.Say.Said, l => l == $"# {Key} In Review, fixing its failing build (api, CI)");
        Assert.Contains(h.Say.Said, l => l.Contains("## The failing build", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("_no log_", StringComparison.Ordinal));
        Assert.Empty(h.Forge.Reads);
        Assert.Empty(h.Forge.Logs);
        Assert.Empty(h.Sessions.Spawned);
    }

    [Fact]
    public async Task Work_attached_hands_the_fresh_failure_to_the_session_it_sits_in()
    {
        using var h = new Harness();
        Board(h);
        h.Forge.Answer = Failed("api");
        h.Forge.Excerpts["api"] = "fresh failure";

        await new WorkCommand(h.Runtime).RunAsync([Key, "--interactive"], default);

        Assert.Contains("fresh failure", Assert.Single(h.Sessions.Attached).Prompt, StringComparison.Ordinal);
    }
}

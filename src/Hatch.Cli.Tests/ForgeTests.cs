namespace Hatch.Cli.Tests;

/// <summary>
/// What <c>gh</c> is asked and what its answers come to. The classification and
/// the excerpt are pure functions over the text <c>gh</c> prints, so they are
/// tested without a process; the calls are tested against a runner that says
/// what it was asked.
/// </summary>
public sealed class ForgeTests
{
    private static string Run(string name, string status, string? conclusion, long id = 1) =>
        conclusion is null
            ? $$"""{"id":{{id}},"name":"{{name}}","status":"{{status}}","conclusion":null,"url":"https://forge.example/checks/{{id}}"}"""
            : $$"""{"id":{{id}},"name":"{{name}}","status":"{{status}}","conclusion":"{{conclusion}}","url":"https://forge.example/checks/{{id}}"}""";

    private static string Status(string context, string state) =>
        $$"""{"context":"{{context}}","state":"{{state}}","url":"https://forge.example/statuses/{{context}}"}""";

    // ---- The four verdicts ----

    [Theory]
    [InlineData("failure")]
    [InlineData("timed_out")]
    [InlineData("startup_failure")]
    public void A_check_run_that_failed_says_failed_and_names_itself(string conclusion)
    {
        var read = GhForge.Classify(Run("api", "completed", "success", 1) + "\n" + Run("CI", "completed", conclusion, 2), "");

        Assert.Equal(BuildVerdicts.Failed, read.Verdict);
        var failing = Assert.Single(read.Failing);
        Assert.Equal("CI", failing.Name);
        Assert.Equal("https://forge.example/checks/2", failing.Url);
        Assert.Equal(2, failing.JobId);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("cancelled")]
    [InlineData("skipped")]
    [InlineData("neutral")]
    [InlineData("stale")]
    [InlineData("action_required")]
    public void A_check_run_that_ended_any_other_way_is_not_a_failure(string conclusion)
    {
        var read = GhForge.Classify(Run("api", "completed", conclusion), "");

        Assert.Equal(BuildVerdicts.Passed, read.Verdict);
        Assert.Empty(read.Failing);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("error")]
    public void A_status_that_failed_says_failed_and_has_no_job(string state)
    {
        var read = GhForge.Classify("", Status("ci/other", state));

        Assert.Equal(BuildVerdicts.Failed, read.Verdict);
        var failing = Assert.Single(read.Failing);
        Assert.Equal("ci/other", failing.Name);
        Assert.Null(failing.JobId);
    }

    [Fact]
    public void A_status_that_succeeded_is_a_pass()
    {
        Assert.Equal(BuildVerdicts.Passed, GhForge.Classify("", Status("ci/other", "success")).Verdict);
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("in_progress")]
    [InlineData("waiting")]
    [InlineData("pending")]
    public void A_check_run_that_has_not_completed_is_pending(string status)
    {
        Assert.Equal(BuildVerdicts.Pending, GhForge.Classify(Run("api", status, null), "").Verdict);
    }

    [Fact]
    public void A_status_that_is_pending_is_pending()
    {
        Assert.Equal(BuildVerdicts.Pending, GhForge.Classify("", Status("ci/other", "pending")).Verdict);
    }

    /// <summary>The runner waits for every check to conclude before calling the verdict failed, so one increment sees every failure - but a check that has already failed is visible on the pending verdict too.</summary>
    [Fact]
    public void A_check_still_running_does_not_hide_one_that_already_failed()
    {
        var read = GhForge.Classify(Run("api", "completed", "failure", 1) + "\n" + Run("CI", "in_progress", null, 2), "");

        Assert.Equal(BuildVerdicts.Pending, read.Verdict);
        Assert.Equal(["api"], read.Failing.Select(f => f.Name));
    }

    [Fact]
    public void A_check_still_running_and_nothing_failed_is_pending_with_nothing_named()
    {
        var read = GhForge.Classify(Run("api", "in_progress", null, 1) + "\n" + Run("CI", "in_progress", null, 2), "");

        Assert.Equal(BuildVerdicts.Pending, read.Verdict);
        Assert.Empty(read.Failing);
    }

    /// <summary>
    /// The endpoint's own <c>state</c> says <c>pending</c> for a sha with no
    /// statuses at all, and is never read: only the list counts, so an empty one
    /// is none.
    /// </summary>
    [Fact]
    public void No_check_runs_and_no_statuses_is_none()
    {
        var read = GhForge.Classify("", "");

        Assert.Equal(BuildVerdicts.None, read.Verdict);
        Assert.Empty(read.Failing);
    }

    [Fact]
    public void Two_checks_of_one_name_are_one_failure_and_the_failures_are_sorted()
    {
        var read = GhForge.Classify(
            Run("zeta", "completed", "failure", 1) + "\n" + Run("api", "completed", "failure", 2) + "\n" + Run("api", "completed", "failure", 3),
            Status("mid", "failure"));

        Assert.Equal(["api", "mid", "zeta"], read.Failing.Select(f => f.Name));
    }

    [Fact]
    public void Something_that_is_not_json_throws_for_the_caller_to_say_so()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => GhForge.Classify("not json", ""));
    }

    // ---- The calls ----

    private sealed class Runs
    {
        public List<(string Dir, IReadOnlyDictionary<string, string> Env, string[] Args)> Calls { get; } = [];
        public Func<string[], GhForge.Ran> Answer { get; set; } = _ => new GhForge.Ran(0, "", "");

        public GhForge Forge(string path = "/checkout", string? canonical = "forge.example/owner/repo") =>
            new(path, canonical, (dir, env, args, _) =>
            {
                Calls.Add((dir, env, [.. args]));
                return Task.FromResult(Answer([.. args]));
            });
    }

    [Fact]
    public async Task It_asks_for_check_runs_and_statuses_with_the_repository_and_host_the_board_named()
    {
        var runs = new Runs
        {
            Answer = args => args.Any(a => a.Contains("/check-runs", StringComparison.Ordinal))
                ? new GhForge.Ran(0, Run("api", "completed", "failure", 7) + "\n", "")
                : new GhForge.Ran(0, "", ""),
        };

        var answer = await runs.Forge().ReadAsync("abc123", default);

        Assert.Equal(BuildVerdicts.Failed, answer.Read!.Verdict);
        Assert.Null(answer.Why);
        Assert.Equal(2, runs.Calls.Count);

        var first = runs.Calls[0];
        Assert.Equal("/checkout", first.Dir);
        Assert.Equal("forge.example/owner/repo", first.Env["GH_REPO"]);
        Assert.Equal("1", first.Env["GH_PROMPT_DISABLED"]);
        Assert.Equal(["api", "--paginate", "--hostname", "forge.example"], first.Args[..4]);
        Assert.Contains("repos/{owner}/{repo}/commits/abc123/check-runs?per_page=100", first.Args);
        Assert.Contains("--jq", first.Args);
        Assert.Contains("repos/{owner}/{repo}/commits/abc123/status?per_page=100", runs.Calls[1].Args);
    }

    [Fact]
    public async Task A_host_with_a_port_is_handed_over_as_it_is()
    {
        var runs = new Runs();

        await runs.Forge(canonical: "forge.example:8443/owner/repo").ReadAsync("abc", default);

        Assert.Equal("forge.example:8443", runs.Calls[0].Args[3]);
        Assert.Equal("forge.example:8443/owner/repo", runs.Calls[0].Env["GH_REPO"]);
    }

    [Fact]
    public async Task A_project_that_binds_nothing_sets_neither_the_repository_nor_the_host()
    {
        var runs = new Runs();

        await runs.Forge(canonical: null).ReadAsync("abc", default);

        Assert.DoesNotContain("GH_REPO", runs.Calls[0].Env.Keys);
        Assert.DoesNotContain("--hostname", runs.Calls[0].Args);
    }

    [Fact]
    public async Task A_local_path_has_no_forge_and_no_call_is_made()
    {
        var runs = new Runs();

        var answer = await runs.Forge(canonical: "/srv/git/repo.git").ReadAsync("abc", default);

        Assert.Null(answer.Read);
        Assert.Contains("local path", answer.Why, StringComparison.Ordinal);
        Assert.Empty(runs.Calls);
    }

    [Fact]
    public async Task A_missing_gh_is_said_in_one_line()
    {
        var runs = new Runs { Answer = _ => new GhForge.Ran(-1, "", "") };

        var answer = await runs.Forge().ReadAsync("abc", default);

        Assert.Null(answer.Read);
        Assert.Equal("gh is not installed", answer.Why);
        Assert.Single(runs.Calls);
    }

    [Fact]
    public async Task A_gh_that_refuses_says_the_first_line_it_said()
    {
        var runs = new Runs { Answer = _ => new GhForge.Ran(4, "", "\nTo get started with GitHub CLI, please run:  gh auth login\nmore\n") };

        var answer = await runs.Forge().ReadAsync("abc", default);

        Assert.Null(answer.Read);
        Assert.Equal("To get started with GitHub CLI, please run:  gh auth login", answer.Why);
    }

    [Fact]
    public async Task A_gh_that_answers_nonsense_is_a_reason_and_not_an_exception()
    {
        var runs = new Runs { Answer = _ => new GhForge.Ran(0, "<html>", "") };

        var answer = await runs.Forge().ReadAsync("abc", default);

        Assert.Null(answer.Read);
        Assert.NotNull(answer.Why);
    }

    [Fact]
    public async Task A_log_is_read_by_job_with_the_repository_and_no_host()
    {
        var runs = new Runs { Answer = _ => new GhForge.Ran(0, "api\tstep\t2026-09-28T13:00:00.0000000Z boom\n", "") };

        var text = await runs.Forge().LogAsync(new FailingCheck("api", null, 42), default);

        Assert.Equal("boom", text);
        Assert.Equal(["run", "view", "--job", "42", "--log-failed"], runs.Calls[0].Args);
        Assert.Equal("forge.example/owner/repo", runs.Calls[0].Env["GH_REPO"]);
    }

    [Fact]
    public async Task A_status_has_no_log_and_a_failed_call_is_no_log()
    {
        var runs = new Runs { Answer = _ => new GhForge.Ran(1, "", "failed to get job: HTTP 404") };

        Assert.Null(await runs.Forge().LogAsync(new FailingCheck("ci/other", null), default));
        Assert.Empty(runs.Calls);

        Assert.Null(await runs.Forge().LogAsync(new FailingCheck("api", null, 5), default));
        Assert.Single(runs.Calls);
    }

    // ---- The excerpt ----

    private static string Line(string step, string text, int second = 0, string job = "api") =>
        $"{job}\t{step}\t2026-09-28T13:00:{second:00}.0000000Z {text}";

    /// <summary>
    /// The test failures sit before the job's <c>##[error]</c> line, and what
    /// follows it is post-job cleanup - the wrong lines to send.
    /// </summary>
    [Fact]
    public void The_excerpt_ends_at_the_last_error_line_and_drops_the_cleanup_after_it()
    {
        var log = string.Join('\n',
            Line("Run tests", "  Failed Some.Test", 1),
            Line("Run tests", "  Assert.Equal() Failure", 2),
            Line("Run tests", "##[error]Process completed with exit code 1.", 3),
            Line("Post Run actions/checkout@v4", "Post job cleanup.", 4),
            Line("Post Run actions/checkout@v4", "removing credentials", 5));

        Assert.Equal(
            "  Failed Some.Test\n  Assert.Equal() Failure\n##[error]Process completed with exit code 1.",
            GhForge.Excerpt(log));
    }

    [Fact]
    public void A_log_with_no_error_line_ends_where_it_ends()
    {
        var log = string.Join('\n', Line("Run tests", "one", 1), Line("Run tests", "two", 2));

        Assert.Equal("one\ntwo", GhForge.Excerpt(log));
    }

    /// <summary>The first line of a real log begins with a byte-order mark before its timestamp, and a step's name can hold spaces.</summary>
    [Fact]
    public void The_prefix_is_stripped_across_a_byte_order_mark_and_a_step_with_spaces()
    {
        var log = "api\tUNKNOWN STEP\t﻿2026-09-28T13:00:00.0000000Z first\n"
                  + "api\tRun the unit tests\t2026-09-28T13:00:01.0000000Z second\n"
                  + "﻿api\tstep\t2026-09-28T13:00:02.0000000Z third";

        Assert.Equal("first\nsecond\nthird", GhForge.Excerpt(log));
    }

    [Fact]
    public void A_line_with_fewer_than_two_tabs_is_kept_as_it_is()
    {
        Assert.Equal("no prefix at all\nsome\tthing", GhForge.Excerpt("no prefix at all\nsome\tthing"));
    }

    [Fact]
    public void Tabs_inside_the_text_survive_the_prefix()
    {
        Assert.Equal("a\tb", GhForge.Excerpt(Line("step", "a\tb")));
    }

    [Fact]
    public void The_excerpt_is_the_last_hundred_and_fifty_lines_before_the_error()
    {
        var lines = Enumerable.Range(1, 400).Select(i => Line("Run tests", $"line {i}", i % 60)).ToList();
        lines.Insert(300, Line("Run tests", "##[error]boom", 1));

        var excerpt = GhForge.Excerpt(string.Join('\n', lines))!.Split('\n');

        Assert.Equal(GhForge.MaxExcerptLines, excerpt.Length);
        Assert.Equal("##[error]boom", excerpt[^1]);
        Assert.Equal("line 152", excerpt[0]);
    }

    [Fact]
    public void The_excerpt_is_capped_in_bytes_and_keeps_the_end_that_is_nearest_the_failure()
    {
        var fat = new string('x', 500);
        var lines = Enumerable.Range(1, 100).Select(i => Line("Run tests", $"{i:000} {fat}", i % 60)).ToList();

        var excerpt = GhForge.Excerpt(string.Join('\n', lines))!;

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(excerpt) <= GhForge.MaxExcerptBytes);
        Assert.EndsWith(fat, excerpt, StringComparison.Ordinal);
        Assert.Contains("100 ", excerpt, StringComparison.Ordinal);
        Assert.DoesNotContain("001 ", excerpt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    public void An_empty_log_is_no_log(string log)
    {
        Assert.Null(GhForge.Excerpt(log));
    }

    // ---- Reading a pull request ----

    [Theory]
    [InlineData("MERGED", PullRequestStates.Merged)]
    [InlineData("OPEN", PullRequestStates.Open)]
    [InlineData("CLOSED", PullRequestStates.Closed)]
    [InlineData("merged", PullRequestStates.Merged)]
    [InlineData("Open", PullRequestStates.Open)]
    [InlineData("closed", PullRequestStates.Closed)]
    public void A_state_gh_prints_reads_as_the_matching_verdict_regardless_of_case(string printed, string verdict)
    {
        Assert.Equal(verdict, GhForge.ClassifyState(printed));
    }

    [Fact]
    public void A_state_gh_has_never_printed_reads_unknown_rather_than_throwing()
    {
        Assert.Equal(PullRequestStates.Unknown, GhForge.ClassifyState("DRAFT"));
    }

    [Fact]
    public async Task It_asks_gh_pr_view_with_the_url_as_given_and_no_hostname()
    {
        var runs = new Runs { Answer = _ => new GhForge.Ran(0, """{"state":"MERGED"}""", "") };

        var answer = await runs.Forge().ReadPullRequestAsync("https://forge.example/owner/repo/pull/7", default);

        Assert.Equal(PullRequestStates.Merged, answer.State);
        Assert.Null(answer.Why);
        var call = Assert.Single(runs.Calls);
        Assert.Equal(["pr", "view", "https://forge.example/owner/repo/pull/7", "--json", "state"], call.Args);
        Assert.DoesNotContain("--hostname", call.Args);
        Assert.Equal("forge.example/owner/repo", call.Env["GH_REPO"]);
    }

    [Fact]
    public async Task A_missing_gh_is_said_in_one_line_for_a_pull_request_too()
    {
        var runs = new Runs { Answer = _ => new GhForge.Ran(-1, "", "") };

        var answer = await runs.Forge().ReadPullRequestAsync("https://forge.example/owner/repo/pull/7", default);

        Assert.Null(answer.State);
        Assert.Equal("gh is not installed", answer.Why);
    }

    [Fact]
    public async Task A_gh_that_refuses_the_pull_request_says_the_first_line_it_said()
    {
        var runs = new Runs { Answer = _ => new GhForge.Ran(4, "", "\nTo get started with GitHub CLI, please run:  gh auth login\nmore\n") };

        var answer = await runs.Forge().ReadPullRequestAsync("https://forge.example/owner/repo/pull/7", default);

        Assert.Null(answer.State);
        Assert.Equal("To get started with GitHub CLI, please run:  gh auth login", answer.Why);
    }

    [Fact]
    public async Task Output_that_is_not_json_is_a_why_and_not_an_exception()
    {
        var runs = new Runs { Answer = _ => new GhForge.Ran(0, "<html>", "") };

        var answer = await runs.Forge().ReadPullRequestAsync("https://forge.example/owner/repo/pull/7", default);

        Assert.Null(answer.State);
        Assert.NotNull(answer.Why);
    }

    [Fact]
    public async Task A_local_path_asks_gh_nothing_for_a_pull_request()
    {
        var runs = new Runs();

        var answer = await runs.Forge(canonical: "/srv/git/repo.git").ReadPullRequestAsync("https://forge.example/owner/repo/pull/7", default);

        Assert.Null(answer.State);
        Assert.Contains("local path", answer.Why, StringComparison.Ordinal);
        Assert.Empty(runs.Calls);
    }
}

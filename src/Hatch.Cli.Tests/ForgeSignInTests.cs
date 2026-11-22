using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>
/// Whether the runner is signed in to a checkout's forge: what <c>gh auth status</c> is asked and what its
/// answers come to, and how the loop holds the answer and asks again. The calls are tested against a
/// runner that says what it was asked, as <see cref="ForgeTests"/> does.
/// </summary>
public sealed class ForgeSignInTests
{
    private static readonly string UnreachableHost = string.Join('\n',
        "forge.example",
        "  X Failed to log in to forge.example using token (GH_TOKEN)",
        "  - The token in GH_TOKEN is invalid.",
        "  - Error: Post \"https://forge.example/api/graphql\": dial tcp: lookup forge.example: no such host");

    private sealed class Runs
    {
        public List<(string Dir, IReadOnlyDictionary<string, string> Env, string[] Args)> Calls { get; } = [];
        public Func<string[], CancellationToken, Task<GhForge.Ran>> Answer { get; set; } = (_, _) => Task.FromResult(new GhForge.Ran(0, "", ""));

        public GhForge Forge(string path = "/checkout", string? canonical = "forge.example/owner/repo") =>
            new(path, canonical, (dir, env, args, ct) =>
            {
                Calls.Add((dir, env, [.. args]));
                return Answer([.. args], ct);
            });

        public void Says(int code, string err = "", string output = "") =>
            Answer = (_, _) => Task.FromResult(new GhForge.Ran(code, output, err));
    }

    // ---- The check ----

    [Fact]
    public async Task It_runs_auth_status_for_the_active_account_on_the_host_the_board_named_and_never_shows_a_token()
    {
        var runs = new Runs();

        await runs.Forge().CheckSignInAsync(GhForge.Limit, default);

        var call = Assert.Single(runs.Calls);
        Assert.Equal("/checkout", call.Dir);
        Assert.Equal(["auth", "status", "--active", "--hostname", "forge.example"], call.Args);
        Assert.DoesNotContain("--show-token", call.Args);
        Assert.Equal("forge.example/owner/repo", call.Env["GH_REPO"]);
        Assert.Equal("1", call.Env["GH_PROMPT_DISABLED"]);
    }

    [Fact]
    public async Task A_project_that_binds_nothing_sets_neither_the_repository_nor_the_host()
    {
        var runs = new Runs();

        await runs.Forge(canonical: null).CheckSignInAsync(GhForge.Limit, default);

        Assert.Equal(["auth", "status", "--active"], runs.Calls[0].Args);
        Assert.DoesNotContain("GH_REPO", runs.Calls[0].Env.Keys);
    }

    [Fact]
    public async Task A_sign_in_that_gh_accepts_works_with_no_reason()
    {
        var runs = new Runs();
        runs.Says(0, output: "forge.example\n  ✓ Logged in to forge.example account someone (keyring)\n");

        var answer = await runs.Forge().CheckSignInAsync(GhForge.Limit, default);

        Assert.Equal(new ForgeSignInAnswer("works", null), answer);
    }

    [Fact]
    public async Task A_binary_that_is_not_there_fails_and_says_so()
    {
        var runs = new Runs();
        runs.Says(-1);

        var answer = await runs.Forge().CheckSignInAsync(GhForge.Limit, default);

        Assert.Equal(new ForgeSignInAnswer("fails", "gh is not installed"), answer);
    }

    [Fact]
    public async Task A_signed_out_account_fails_with_the_line_gh_wrote()
    {
        var runs = new Runs();
        runs.Says(1, "You are not logged into any accounts on forge.example\n");

        var answer = await runs.Forge().CheckSignInAsync(GhForge.Limit, default);

        Assert.Equal(new ForgeSignInAnswer("fails", "You are not logged into any accounts on forge.example"), answer);
    }

    [Fact]
    public async Task A_refused_token_fails_with_the_sentence_after_the_mark_and_not_the_host_line()
    {
        var runs = new Runs();
        runs.Says(1, "forge.example\n  X Failed to log in to forge.example using token (GH_TOKEN)\n  - The token in GH_TOKEN is invalid.\n");

        var answer = await runs.Forge().CheckSignInAsync(GhForge.Limit, default);

        Assert.Equal(new ForgeSignInAnswer("fails", "Failed to log in to forge.example using token (GH_TOKEN)"), answer);
    }

    [Fact]
    public async Task A_key_in_what_gh_wrote_is_masked()
    {
        var runs = new Runs();
        runs.Says(1, "The token hatch_ak_0123456789abcdef was refused, Bearer abc.def-123\n");

        var answer = await runs.Forge().CheckSignInAsync(GhForge.Limit, default);

        Assert.Equal($"The token {Scrub.Marker} was refused, {Scrub.Marker}", answer.Why);
    }

    [Fact]
    public async Task A_long_reason_is_cut_to_two_hundred_characters_and_not_two_hundred_and_one()
    {
        var runs = new Runs();
        runs.Says(1, "not signed in " + new string('x', 500));

        var answer = await runs.Forge().CheckSignInAsync(GhForge.Limit, default);

        Assert.Equal("fails", answer.State);
        Assert.Equal(RunnerForge.MaxWhyLength, answer.Why!.Length);
    }

    [Fact]
    public async Task A_failure_with_nothing_readable_to_say_names_the_exit_code()
    {
        var runs = new Runs();
        runs.Says(4, "forge.example\n");

        var answer = await runs.Forge().CheckSignInAsync(GhForge.Limit, default);

        Assert.Equal(new ForgeSignInAnswer("fails", "gh exited 4"), answer);
    }

    // ---- Weather ----

    /// <summary>The token is not what is wrong, whatever the line above the error says.</summary>
    [Fact]
    public async Task A_host_that_cannot_be_reached_is_no_answer_though_gh_calls_the_token_invalid()
    {
        var runs = new Runs();
        runs.Says(1, UnreachableHost);

        var answer = await runs.Forge().CheckSignInAsync(GhForge.Limit, default);

        Assert.Equal(new ForgeSignInAnswer(null, null), answer);
    }

    [Theory]
    [InlineData("Error: Post \"https://forge.example/api/graphql\": dial tcp 10.0.0.1:443: connect: connection refused")]
    [InlineData("Error: read tcp 10.0.0.2:5000->10.0.0.1:443: read: connection reset by peer")]
    [InlineData("Error: Post \"https://forge.example/api/graphql\": context deadline exceeded (Client.Timeout exceeded while awaiting headers) timeout")]
    [InlineData("Error: request timed out")]
    [InlineData("Error: tls: failed to verify certificate")]
    [InlineData("Error: unexpected EOF")]
    [InlineData("Error: HTTP 502: Bad Gateway")]
    [InlineData("Error: proxyconnect tcp: dial tcp 127.0.0.1:3128")]
    public async Task An_error_naming_the_network_is_no_answer(string error)
    {
        var runs = new Runs();
        runs.Says(1, $"forge.example\n  X Failed to log in to forge.example using token (GH_TOKEN)\n  - {error}\n");

        var answer = await runs.Forge().CheckSignInAsync(GhForge.Limit, default);

        Assert.Null(answer.State);
    }

    [Fact]
    public async Task An_error_that_names_a_refusal_is_a_failure()
    {
        var runs = new Runs();
        runs.Says(1, "forge.example\n  X Failed to log in to forge.example using token (GH_TOKEN)\n  - Error: HTTP 401: Bad credentials\n");

        var answer = await runs.Forge().CheckSignInAsync(GhForge.Limit, default);

        Assert.Equal("fails", answer.State);
    }

    [Fact]
    public async Task A_check_that_waits_on_its_token_is_ended_at_the_limit_and_is_no_answer()
    {
        var runs = new Runs { Answer = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new GhForge.Ran(0, "", ""); } };
        var started = DateTimeOffset.UtcNow;

        var answer = await runs.Forge().CheckSignInAsync(TimeSpan.FromMilliseconds(50), default);

        Assert.Equal(new ForgeSignInAnswer(null, null), answer);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(10), "it did not wait out the call");
    }

    [Fact]
    public async Task A_caller_that_cancels_is_not_told_it_is_weather()
    {
        var runs = new Runs { Answer = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new GhForge.Ran(0, "", ""); } };
        using var cancelling = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runs.Forge().CheckSignInAsync(TimeSpan.FromMinutes(5), cancelling.Token));
    }

    [Theory]
    [InlineData("/srv/git/repo.git")]
    [InlineData("./repo")]
    public async Task A_local_path_has_no_forge_and_gh_is_not_run(string canonical)
    {
        var runs = new Runs();

        var answer = await runs.Forge(canonical: canonical).CheckSignInAsync(GhForge.Limit, default);

        Assert.Equal(new ForgeSignInAnswer(null, null), answer);
        Assert.Empty(runs.Calls);
    }

    [Fact]
    public void The_limit_is_fifteen_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), GhForge.Limit);
    }

    // ---- Holding the answer ----

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-22T12:00:00Z");

    private static string Path(string leaf) => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "forge-answer", leaf);

    private static Runtime Serving(Harness h, FakeTimeProvider clock, params string[] leaves) =>
        h.Runtime with { Clock = clock, Checkouts = [.. leaves.Select(l => new CheckoutEntry(Path(l), null, Standing: false))] };

    private static FakeTimeProvider Clock() => new(Now);

    private static readonly ForgeSignInAnswer SignedOut = new("fails", "You are not logged into any accounts on forge.example");
    private static readonly ForgeSignInAnswer SignedIn = new("works", null);
    private static readonly ForgeSignInAnswer Weather = new(null, null);

    [Fact]
    public async Task Two_asks_inside_a_minute_probe_once_and_the_second_gets_the_held_answer()
    {
        using var h = new Harness();
        var clock = Clock();
        var runtime = Serving(h, clock, "a");
        h.Forge.SignIn = SignedOut;

        var first = await runtime.AskForgeAsync(Path("a"), "forge.example/o/r", default);
        h.Forge.SignIn = SignedIn;
        clock.Advance(TimeSpan.FromSeconds(59));
        var second = await runtime.AskForgeAsync(Path("a"), "forge.example/o/r", default);

        Assert.Equal(SignedOut, first);
        Assert.Equal(SignedOut, second);
        Assert.Equal([(Path("a"), (string?)"forge.example/o/r")], h.Forge.SignInAsked);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(SignedIn, await runtime.AskForgeAsync(Path("a"), "forge.example/o/r", default));
        Assert.Equal(2, h.Forge.SignInAsked.Count);
    }

    [Fact]
    public async Task Weather_leaves_a_works_standing_and_leaves_a_fails_standing()
    {
        using var h = new Harness();
        var clock = Clock();
        var runtime = Serving(h, clock, "a", "b");

        h.Forge.SignIn = SignedIn;
        await runtime.AskForgeAsync(Path("a"), null, default);
        h.Forge.SignIn = SignedOut;
        await runtime.AskForgeAsync(Path("b"), null, default);

        h.Forge.SignIn = Weather;
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(SignedIn, await runtime.AskForgeAsync(Path("a"), null, default));
        Assert.Equal(SignedOut, await runtime.AskForgeAsync(Path("b"), null, default));

        Assert.Equal(4, h.Forge.SignInAsked.Count);
    }

    [Fact]
    public async Task Weather_still_counts_as_an_ask_so_a_dead_host_is_not_asked_every_beat()
    {
        using var h = new Harness();
        var clock = Clock();
        var runtime = Serving(h, clock, "a");
        h.Forge.SignIn = Weather;

        Assert.Null(await runtime.AskForgeAsync(Path("a"), null, default));
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(await runtime.AskForgeAsync(Path("a"), null, default));

        Assert.Single(h.Forge.SignInAsked);
    }

    [Fact]
    public async Task At_the_top_of_a_pass_a_works_is_left_thirty_minutes()
    {
        using var h = new Harness();
        var clock = Clock();
        var runtime = Serving(h, clock, "a");
        h.Forge.SignIn = SignedIn;
        await runtime.AskForgeAsync(Path("a"), null, default);

        clock.Advance(TimeSpan.FromMinutes(29));
        await runtime.AskForgeAsync(Path("a"), null, default, passTop: true);
        Assert.Single(h.Forge.SignInAsked);

        clock.Advance(TimeSpan.FromMinutes(2));
        await runtime.AskForgeAsync(Path("a"), null, default, passTop: true);
        Assert.Equal(2, h.Forge.SignInAsked.Count);
    }

    [Fact]
    public async Task At_the_top_of_a_pass_a_fails_is_asked_each_pass_once_a_minute_has_gone()
    {
        using var h = new Harness();
        var clock = Clock();
        var runtime = Serving(h, clock, "a");
        h.Forge.SignIn = SignedOut;
        await runtime.AskForgeAsync(Path("a"), null, default);

        clock.Advance(TimeSpan.FromSeconds(30));
        await runtime.AskForgeAsync(Path("a"), null, default, passTop: true);
        Assert.Single(h.Forge.SignInAsked);

        clock.Advance(TimeSpan.FromSeconds(31));
        await runtime.AskForgeAsync(Path("a"), null, default, passTop: true);
        Assert.Equal(2, h.Forge.SignInAsked.Count);

        clock.Advance(TimeSpan.FromMinutes(1));
        await runtime.AskForgeAsync(Path("a"), null, default, passTop: true);
        Assert.Equal(3, h.Forge.SignInAsked.Count);
    }

    [Fact]
    public async Task A_works_that_has_never_been_confirmed_since_weather_is_asked_at_the_top_of_the_pass()
    {
        using var h = new Harness();
        var clock = Clock();
        var runtime = Serving(h, clock, "a");
        h.Forge.SignIn = SignedIn;
        await runtime.AskForgeAsync(Path("a"), null, default);

        // Weather an hour on does not renew the works, so the next pass asks again: the age
        // counted is the answer's, and not the last time anybody asked.
        h.Forge.SignIn = Weather;
        clock.Advance(TimeSpan.FromMinutes(31));
        await runtime.AskForgeAsync(Path("a"), null, default, passTop: true);
        clock.Advance(TimeSpan.FromMinutes(2));
        await runtime.AskForgeAsync(Path("a"), null, default, passTop: true);

        Assert.Equal(3, h.Forge.SignInAsked.Count);
    }

    // ---- What the runner says ----

    [Fact]
    public void A_runner_with_nothing_asked_yet_has_no_answer()
    {
        using var h = new Harness();

        Assert.Null(Serving(h, Clock(), "one", "two").ForgeAnswer());
    }

    [Fact]
    public void Several_checkouts_read_as_the_worst_and_a_checkout_not_asked_is_skipped()
    {
        using var h = new Harness();
        var runtime = Serving(h, Clock(), "a", "b", "c");

        runtime.ForgeCache.Record(Path("a"), SignedIn, Now);
        Assert.Equal(("works", null), runtime.ForgeAnswer());

        runtime.ForgeCache.Record(Path("b"), SignedOut, Now);
        Assert.Equal(("fails", SignedOut.Why), runtime.ForgeAnswer());
    }

    [Fact]
    public void A_clone_made_under_the_workspace_after_start_counts_with_the_checkouts()
    {
        using var h = new Harness();
        var workspace = Path("workspace");
        var runtime = Serving(h, Clock(), "a") with { Settings = h.Runtime.Settings with { Workspace = workspace } };
        runtime.ForgeCache.Record(Path("a"), SignedIn, Now);
        runtime.ForgeCache.Record(System.IO.Path.Combine(workspace, "clone"), SignedOut, Now);

        Assert.Equal(("fails", SignedOut.Why), runtime.ForgeAnswer());
    }

    [Fact]
    public void A_reason_is_one_masked_line_of_at_most_two_hundred_characters_and_only_beside_fails()
    {
        var (_, masked) = Runtime.FoldForge([new("fails", "key hatch_ak_0123456789abcdef is refused\nsecond line")])!.Value;
        Assert.Equal($"key {Scrub.Marker} is refused", masked);

        var (_, cut) = Runtime.FoldForge([new("fails", new string('x', 500))])!.Value;
        Assert.Equal(200, cut!.Length);

        Assert.Equal(("works", null), Runtime.FoldForge([new("works", "stale")]));
        Assert.Null(Runtime.FoldForge([]));
    }

    [Fact]
    public void The_canonical_identity_comes_from_the_project_binding_that_names_the_checkouts_remote()
    {
        ProjectDto Project(params ProjectRepositoryDto[] repositories) =>
            new(1, "AER", "AER", 0, DateTimeOffset.UnixEpoch, null, null, repositories, null, [], false, false);

        var projects = new[]
        {
            Project(new ProjectRepositoryDto("git@forge.example:o/other.git", "forge.example/o/other", null)),
            Project(new ProjectRepositoryDto("git@forge.example:o/r.git", "forge.example/o/r", null)),
        };

        Assert.Equal("forge.example/o/r", Runtime.CanonicalOf(new CheckoutEntry("/c", "git@forge.example:o/r.git", false), projects));
        Assert.Null(Runtime.CanonicalOf(new CheckoutEntry("/c", "git@forge.example:o/unbound.git", false), projects));
        Assert.Null(Runtime.CanonicalOf(new CheckoutEntry("/c", null, false), projects));
    }

    // ---- A reason from another call asks again ----

    private static ForgeAnswer Cannot(string why) => new(null, why);

    [Fact]
    public async Task A_call_that_comes_back_with_a_reason_asks_whether_the_runner_is_signed_in_once()
    {
        using var h = new Harness();
        var clock = Clock();
        var runtime = Serving(h, clock, "a");
        h.Forge.Answer = Cannot("HTTP 401: Bad credentials");
        h.Forge.SignIn = SignedOut;

        var answer = await runtime.Forge(Path("a"), "forge.example/o/r").ReadAsync("abc", default);
        await runtime.Forge(Path("a"), "forge.example/o/r").ReadAsync("abc", default);

        Assert.Equal("HTTP 401: Bad credentials", answer.Why);
        Assert.Equal([(Path("a"), (string?)"forge.example/o/r")], h.Forge.SignInAsked);
        Assert.Equal(("fails", SignedOut.Why), runtime.ForgeAnswer());
    }

    [Fact]
    public async Task Every_kind_of_call_that_carries_a_reason_asks_and_the_ones_that_do_not_never_do()
    {
        using var h = new Harness();
        var clock = Clock();
        var runtime = Serving(h, clock, "a");
        h.Forge.PullRequestAnswer = new PullRequestAnswer(null, "no");
        h.Forge.PullRequestList = new PullRequestListAnswer(null, "no");
        h.Forge.AssociationAnswer = new AssociationAnswer(null, "no");
        h.Forge.MergeAnswer = new MergeAnswer(false, "no");
        h.Forge.BodyAnswer = new PullRequestBodyAnswer(null, "no");
        h.Forge.EditAnswer = new BodyEditAnswer(false, "no");
        h.Forge.UpdateAnswer = new UpdateAnswer(null, "no");
        h.Forge.RerunAnswer = new RerunAnswer(null, "no");

        async Task Asks(Func<IForge, Task> call)
        {
            h.Forge.SignInAsked.Clear();
            clock.Advance(TimeSpan.FromMinutes(2));
            await call(runtime.Forge(Path("a"), null));
            Assert.Single(h.Forge.SignInAsked);
        }

        await Asks(f => f.ReadPullRequestAsync("u", default));
        await Asks(f => f.ReadOpenPullRequestsAsync(default));
        await Asks(f => f.ReadAssociationAsync(1, default));
        await Asks(f => f.MergeAsync("u", "s", default));
        await Asks(f => f.ReadBodyAsync("u", default));
        await Asks(f => f.EditBodyAsync("u", "b", default));
        await Asks(f => f.UpdateAsync(1, "s", default));
        await Asks(f => f.RerunAsync(new FailingCheck("CI", null, 1), default));

        h.Forge.SignInAsked.Clear();
        clock.Advance(TimeSpan.FromMinutes(2));
        await runtime.Forge(Path("a"), null).LogAsync(new FailingCheck("CI", null, 1), default);
        await runtime.Forge(Path("a"), null).FileUrlAsync("x.md", "main", default);
        Assert.Empty(h.Forge.SignInAsked);
    }

    [Fact]
    public async Task A_call_that_came_back_clean_a_merge_that_took_and_a_conflict_ask_nothing()
    {
        using var h = new Harness();
        var runtime = Serving(h, Clock(), "a");
        h.Forge.Answer = new ForgeAnswer(new BuildRead(BuildVerdicts.Passed, []), null);
        h.Forge.MergeAnswer = new MergeAnswer(true, null);
        h.Forge.UpdateAnswer = new UpdateAnswer(UpdateOutcomes.Conflicted, "merge conflict between base and head");

        await runtime.Forge(Path("a"), null).ReadAsync("abc", default);
        await runtime.Forge(Path("a"), null).MergeAsync("u", "s", default);
        await runtime.Forge(Path("a"), null).UpdateAsync(1, "s", default);

        Assert.Empty(h.Forge.SignInAsked);
    }

    [Fact]
    public async Task The_check_itself_is_made_by_the_unwatched_forge_so_a_reason_cannot_start_a_check_that_starts_a_check()
    {
        using var h = new Harness();
        var made = new List<IForge>();
        var runtime = Serving(h, Clock(), "a") with
        {
            ForgeProbe = null,
            MakeForge = (p, c) =>
            {
                var forge = h.Forge.For(p, c);
                made.Add(forge);
                return forge;
            },
        };
        h.Forge.SignIn = SignedOut;

        var answer = await runtime.AskForgeAsync(Path("a"), null, default);

        // With no probe set it asks MakeForge's forge directly, not the watched one: the one forge made
        // is the unwatched one, and the harness's probe seam was never used.
        Assert.Equal(SignedOut, answer);
        Assert.Single(made);
        Assert.Empty(h.Forge.SignInAsked);
        Assert.Equal(SignedOut, runtime.ForgeCache.Latest(Path("a")));
    }
}

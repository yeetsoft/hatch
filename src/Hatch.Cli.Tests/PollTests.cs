using System.Net;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>
/// The poll: which branches in review it asks git about, when it fetches, and
/// what it tells the board - against a fake workspace, so what is asserted is
/// the order and the count of the calls and not what git does with them.
/// </summary>
public sealed class PollTests
{
    private const string Review = "/api/hatch/work/review";
    private const int Interval = 60;

    private static readonly string Trunk = new('a', 40);
    private static readonly string Moved = new('c', 40);
    private static readonly string Tip = new('b', 40);
    private static readonly string Tip2 = new('d', 40);

    private static string Put(string key) => $"/api/hatch/issues/{key}/merge-check";

    private static RemoteHeads Heads(string? trunk = null, params (string Name, string Sha)[] branches) =>
        new("main", new Dictionary<string, string>(
            [new("main", trunk ?? Trunk), .. branches.Select(b => new KeyValuePair<string, string>(b.Name, b.Sha))]));

    private static Verdict Conflicted(params string[] files) =>
        new(MergeVerdicts.Conflicted, "main", Trunk, "aer-1-thing", Tip, files.Length == 0 ? ["a.txt"] : files);

    private static Verdict Clean() => new(MergeVerdicts.Clean, "main", Trunk, "aer-1-thing", Tip, []);

    private static Verdict None() => new(MergeVerdicts.None, "main", Trunk, null, null, []);

    /// <summary>A board that holds one unbound issue in review, and takes any verdict.</summary>
    private sealed class Rig : IDisposable
    {
        public Harness H { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 27, 2, 0, 0, TimeSpan.Zero));
        public Poll Poll { get; } = new();

        public Runtime Runtime => H.Runtime with { Clock = Clock };

        public Rig Board(params ReviewCheckDto[] rows)
        {
            H.Wire.Json("GET", Review, rows);
            foreach (var row in rows) H.Wire.Json("PUT", Put(row.Key), Fixtures.MergeCheck());
            return this;
        }

        public Task RunAsync() => Poll.RunAsync(Runtime, Interval, default);

        /// <summary>Time passing, to the next interval.</summary>
        public Rig Later(int seconds = Interval)
        {
            Clock.Advance(TimeSpan.FromSeconds(seconds));
            return this;
        }

        public int Count(string what) => H.Workspace.Calls.Count(c => c.StartsWith(what + " ", StringComparison.Ordinal));

        public void Dispose() => H.Dispose();
    }

    // ---- What moved ----

    [Fact]
    public async Task A_branch_nobody_has_checked_is_fetched_checked_and_reported_under_the_checkouts_remote()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Conflicted("a.txt", "b.txt");

        await rig.RunAsync();

        Assert.Equal(
            [$"heads {rig.H.Root}", $"fetch {rig.H.Root}", $"check {rig.H.Root} AER-1"],
            rig.H.Workspace.Calls);

        var put = Assert.Single(rig.H.Wire.To("PUT", Put("AER-1"))).Read<MergeCheckRequest>();
        Assert.Equal("https://example.test/repo.git", put.Remote);
        Assert.Equal(MergeVerdicts.Conflicted, put.Verdict);
        Assert.Equal("main", put.Trunk);
        Assert.Equal(Trunk, put.TrunkSha);
        Assert.Equal("aer-1-thing", put.Branch);
        Assert.Equal(Tip, put.BranchSha);
        Assert.Equal(["a.txt", "b.txt"], put.Files);
        Assert.Equal("test:/checkout", put.Runner);
    }

    [Fact]
    public async Task The_terminal_hears_about_a_new_verdict_in_one_line()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Conflicted("a.txt", "b.txt", "c.txt");

        await rig.RunAsync();

        Assert.Equal(["hatch: AER-1 conflicts with main (3 files)"], rig.H.Say.Said);
    }

    [Fact]
    public async Task An_interval_in_which_nothing_moved_asks_git_for_its_heads_and_says_and_writes_nothing()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();
        await rig.RunAsync();
        var said = rig.H.Say.Said.Count;

        await rig.Later().RunAsync();

        // The one ls-remote per interval is the whole cost of an idle poll.
        Assert.Equal(2, rig.Count("heads"));
        Assert.Equal(1, rig.Count("fetch"));
        Assert.Equal(1, rig.Count("check"));
        Assert.Single(rig.H.Wire.To("PUT", Put("AER-1")));
        Assert.Equal(said, rig.H.Say.Said.Count);
        Assert.Empty(rig.H.Say.Complained);
    }

    [Fact]
    public async Task It_polls_at_most_once_per_interval()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));

        await rig.RunAsync();
        await rig.Later(Interval - 1).RunAsync();
        Assert.Single(rig.H.Wire.To("GET", Review));

        await rig.Later(1).RunAsync();
        Assert.Equal(2, rig.H.Wire.To("GET", Review).Count);
    }

    [Fact]
    public async Task A_branch_that_moved_on_origin_is_fetched_and_checked_again()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();
        await rig.RunAsync();

        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip2));
        await rig.Later().RunAsync();

        Assert.Equal(2, rig.Count("fetch"));
        Assert.Equal(2, rig.Count("check"));
    }

    [Fact]
    public async Task The_trunk_moving_is_a_change_for_every_issue_in_review()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();
        await rig.RunAsync();

        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(Moved, ("aer-1-thing", Tip));
        await rig.Later().RunAsync();

        Assert.Equal(2, rig.Count("fetch"));
    }

    [Fact]
    public async Task Two_issues_that_moved_in_one_checkout_cost_one_fetch()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"), Fixtures.Review("AER-2"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: [("aer-1-thing", Tip), ("aer-2-other", Tip2)]);
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Conflicted();
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-2")] = Clean();

        await rig.RunAsync();

        Assert.Equal(1, rig.Count("fetch"));
        Assert.Equal(2, rig.Count("check"));
        Assert.Single(rig.H.Wire.To("PUT", Put("AER-1")));
        Assert.Single(rig.H.Wire.To("PUT", Put("AER-2")));
    }

    // ---- What a merged pull request costs ----

    /// <summary>
    /// The commonest thing in the column: a pull request that merged has no
    /// branch, so its verdict carries no sha for the board to vouch with. The
    /// poll's own fingerprint is what stops it being fetched every interval.
    /// </summary>
    [Fact]
    public async Task A_merged_branch_is_fetched_once_and_not_again_while_the_fingerprint_holds()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = None();

        await rig.RunAsync();
        await rig.Later().RunAsync();
        await rig.Later().RunAsync();

        Assert.Equal(1, rig.Count("fetch"));
        Assert.Equal(3, rig.Count("heads"));
        Assert.Single(rig.H.Wire.To("PUT", Put("AER-1")));
    }

    [Fact]
    public async Task A_merged_branch_is_fetched_again_when_the_trunk_moves()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = None();
        await rig.RunAsync();

        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(Moved);
        await rig.Later().RunAsync();

        Assert.Equal(2, rig.Count("fetch"));
    }

    [Fact]
    public async Task A_second_branch_appearing_is_a_change()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();
        await rig.RunAsync();

        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: [("aer-1-thing", Tip), ("aer-1-again", Tip2)]);
        await rig.Later().RunAsync();

        Assert.Equal(2, rig.Count("fetch"));
    }

    [Fact]
    public async Task A_branch_that_names_another_issue_is_not_a_change()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();
        await rig.RunAsync();

        // aer-10 is not aer-1: the rule is hyphen-anchored, so a branch for a
        // sibling ticket does not have every issue in review refetched.
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: [("aer-1-thing", Tip), ("aer-10-other", Tip2), ("someone-else", Tip2)]);
        await rig.Later().RunAsync();

        Assert.Equal(1, rig.Count("fetch"));
    }

    // ---- A runner that was restarted ----

    [Fact]
    public async Task A_stored_verdict_whose_shas_match_origin_costs_no_fetch_at_all()
    {
        var stored = Fixtures.MergeCheck(MergeVerdicts.Clean, "main", "example.test/repo") with
        {
            Remote = "https://example.test/repo.git", TrunkSha = Trunk, Branch = "aer-1-thing", BranchSha = Tip,
        };
        using var rig = new Rig().Board(Fixtures.Review("AER-1", null, stored));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));

        await rig.RunAsync();

        Assert.Equal([$"heads {rig.H.Root}"], rig.H.Workspace.Calls);
        Assert.Empty(rig.H.Wire.To("PUT", Put("AER-1")));
        Assert.Empty(rig.H.Say.Said);
    }

    [Fact]
    public async Task A_stored_conflicted_verdict_whose_shas_match_is_trusted_too()
    {
        var stored = Fixtures.MergeCheck(MergeVerdicts.Conflicted, "main", "example.test/repo", "a.txt") with
        {
            Remote = "https://example.test/repo.git", TrunkSha = Trunk, Branch = "aer-1-thing", BranchSha = Tip,
        };
        using var rig = new Rig().Board(Fixtures.Review("AER-1", null, stored));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));

        await rig.RunAsync();

        Assert.Equal(0, rig.Count("fetch"));
    }

    [Theory]
    [InlineData("trunk")]
    [InlineData("branch")]
    public async Task A_stored_verdict_with_a_sha_that_no_longer_matches_is_checked_again(string moved)
    {
        var stored = Fixtures.MergeCheck(MergeVerdicts.Clean, "main", "example.test/repo") with
        {
            Remote = "https://example.test/repo.git",
            TrunkSha = moved == "trunk" ? Moved : Trunk,
            Branch = "aer-1-thing",
            BranchSha = moved == "branch" ? Tip2 : Tip,
        };
        using var rig = new Rig().Board(Fixtures.Review("AER-1", null, stored));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();

        await rig.RunAsync();

        Assert.Equal(1, rig.Count("fetch"));
    }

    /// <summary>
    /// A stored <c>none</c> carries no branch sha, so it cannot vouch for
    /// anything: a fresh runner checks it once, and remembers.
    /// </summary>
    [Fact]
    public async Task A_stored_none_verdict_cannot_vouch_for_origin()
    {
        var stored = Fixtures.MergeCheck(MergeVerdicts.None, "main", "example.test/repo") with
        {
            Remote = "https://example.test/repo.git", TrunkSha = Trunk,
        };
        using var rig = new Rig().Board(Fixtures.Review("AER-1", null, stored));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = None();

        await rig.RunAsync();

        Assert.Equal(1, rig.Count("fetch"));
        // ...and what it found is what the board already said, so nobody is told.
        Assert.Empty(rig.H.Say.Said);
    }

    [Fact]
    public async Task A_verdict_that_says_what_the_board_says_is_reported_and_not_announced()
    {
        var stored = Fixtures.MergeCheck(MergeVerdicts.Conflicted, "main", "example.test/repo", "a.txt") with
        {
            Remote = "https://example.test/repo.git", TrunkSha = Moved, Branch = "aer-1-thing", BranchSha = Tip,
        };
        using var rig = new Rig().Board(Fixtures.Review("AER-1", null, stored));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Conflicted("a.txt");

        await rig.RunAsync();

        // The shas moved, so the board is told - and it is still the same
        // conflict, so the terminal has nothing new to say about it.
        Assert.Single(rig.H.Wire.To("PUT", Put("AER-1")));
        Assert.Empty(rig.H.Say.Said);
    }

    // ---- Repositories ----

    [Fact]
    public async Task Every_bound_repository_the_runner_holds_is_checked_in_its_own_checkout_and_reported_under_its_own_remote()
    {
        var one = Fixtures.Repository("https://example.test/one.git", "example.test/one", primary: true, matchedRemote: "https://example.test/one.git");
        var two = Fixtures.Repository("https://example.test/two.git", "example.test/two", primary: false, matchedRemote: "https://example.test/two.git");
        using var rig = new Rig().Board(Fixtures.Review("AER-1", [one, two]));

        var runtime = rig.H.Runtime with
        {
            Clock = rig.Clock,
            Checkouts =
            [
                new CheckoutEntry("/checkouts/one", "https://example.test/one.git", Standing: true),
                new CheckoutEntry("/checkouts/two", "https://example.test/two.git", Standing: false),
            ],
        };
        foreach (var path in new[] { "/checkouts/one", "/checkouts/two" })
        {
            rig.H.Workspace.HeadsFor[path] = Heads(branches: ("aer-1-thing", Tip));
            rig.H.Workspace.Verdicts[(path, "AER-1")] = path.EndsWith("one", StringComparison.Ordinal) ? Clean() : Conflicted("x");
        }

        await rig.Poll.RunAsync(runtime, Interval, default);

        // One fetch per checkout, and each checkout is its own repository.
        Assert.Equal(2, rig.Count("fetch"));
        Assert.Equal(
            ["fetch /checkouts/one", "fetch /checkouts/two"],
            rig.H.Workspace.Calls.Where(c => c.StartsWith("fetch", StringComparison.Ordinal)).Order());

        var puts = rig.H.Wire.To("PUT", Put("AER-1")).Select(c => c.Read<MergeCheckRequest>()).ToList();
        Assert.Equal(2, puts.Count);
        Assert.Equal(MergeVerdicts.Clean, puts.Single(p => p.Remote == "https://example.test/one.git").Verdict);
        Assert.Equal(MergeVerdicts.Conflicted, puts.Single(p => p.Remote == "https://example.test/two.git").Verdict);
    }

    [Fact]
    public async Task A_runner_holding_only_the_second_of_two_repositories_still_checks_it()
    {
        var one = Fixtures.Repository("https://example.test/one.git", "example.test/one", primary: true, matchedRemote: null);
        var two = Fixtures.Repository("https://example.test/two.git", "example.test/two", primary: false, matchedRemote: "https://example.test/two.git");
        using var rig = new Rig().Board(Fixtures.Review("AER-1", [one, two]));

        var runtime = rig.H.Runtime with
        {
            Clock = rig.Clock,
            Checkouts = [new CheckoutEntry("/checkouts/two", "https://example.test/two.git", Standing: false)],
        };
        rig.H.Workspace.HeadsFor["/checkouts/two"] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[("/checkouts/two", "AER-1")] = Clean();

        await rig.Poll.RunAsync(runtime, Interval, default);

        Assert.Equal("https://example.test/two.git", Assert.Single(rig.H.Wire.To("PUT", Put("AER-1"))).Read<MergeCheckRequest>().Remote);
    }

    [Fact]
    public async Task A_standing_checkout_with_no_origin_is_skipped_with_one_line()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        var runtime = rig.H.Runtime with
        {
            Clock = rig.Clock,
            Checkouts = [new CheckoutEntry(rig.H.Root, null, Standing: true)],
        };

        await rig.Poll.RunAsync(runtime, Interval, default);
        await rig.Later().Poll.RunAsync(runtime, Interval, default);

        Assert.Single(rig.H.Say.Complained);
        Assert.Contains("no origin remote", rig.H.Say.Complained[0], StringComparison.Ordinal);
        Assert.Empty(rig.H.Workspace.Calls);
    }

    // ---- Failing ----

    [Fact]
    public async Task A_refused_put_is_a_line_and_is_asked_again_next_interval()
    {
        using var rig = new Rig();
        rig.H.Wire.Json("GET", Review, new[] { Fixtures.Review("AER-1") });
        rig.H.Wire.Reply("PUT", Put("AER-1"), HttpStatusCode.BadRequest, "\"a conflicted verdict names the files that conflict\"");
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Conflicted();

        await rig.RunAsync();
        await rig.Later().RunAsync();

        // Not remembered, so the second interval fetches and checks again.
        Assert.Equal(2, rig.Count("fetch"));
        Assert.Equal(2, rig.H.Wire.To("PUT", Put("AER-1")).Count);

        // ...and the same complaint twice in a row is said once.
        Assert.Single(rig.H.Say.Complained);
        Assert.Contains("would not take the verdict", rig.H.Say.Complained[0], StringComparison.Ordinal);
        Assert.Empty(rig.H.Say.Said);
    }

    [Fact]
    public async Task An_origin_that_does_not_answer_is_one_line_and_no_fetch()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Equal(0, rig.Count("fetch"));
        Assert.Single(rig.H.Say.Complained);
        Assert.Contains("origin did not answer", rig.H.Say.Complained[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_complaint_that_cleared_and_came_back_is_said_again()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        await rig.RunAsync();

        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = None();
        await rig.Later().RunAsync();

        rig.H.Workspace.HeadsFor.Remove(rig.H.Root);
        await rig.Later().RunAsync();

        Assert.Equal(2, rig.H.Say.Complained.Count(l => l.Contains("origin did not answer", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_fetch_that_fails_is_one_line_and_reports_nothing()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.FetchAnswer = false;

        await rig.RunAsync();

        Assert.Equal(0, rig.Count("check"));
        Assert.Empty(rig.H.Wire.To("PUT", Put("AER-1")));
        Assert.Contains("could not fetch", Assert.Single(rig.H.Say.Complained), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_check_that_cannot_be_made_is_one_line_per_checkout_and_nothing_is_reported()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"), Fixtures.Review("AER-2"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: [("aer-1-thing", Tip), ("aer-2-other", Tip2)]);

        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("PUT", Put("AER-1")));
        Assert.Empty(rig.H.Wire.To("PUT", Put("AER-2")));
        Assert.Contains("2 branch(es) in review", Assert.Single(rig.H.Say.Complained), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_board_that_cannot_be_read_is_one_line_and_touches_no_git()
    {
        using var rig = new Rig();
        rig.H.Wire.Reply("GET", Review, HttpStatusCode.NotFound, "\"no route\"");

        await rig.RunAsync();

        Assert.Empty(rig.H.Workspace.Calls);
        Assert.Contains("could not read what is in review", Assert.Single(rig.H.Say.Complained), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_column_is_one_read_and_nothing_else()
    {
        using var rig = new Rig().Board();

        await rig.RunAsync();

        Assert.Single(rig.H.Wire.Calls);
        Assert.Empty(rig.H.Workspace.Calls);
        Assert.Empty(rig.H.Say.Said);
        Assert.Empty(rig.H.Say.Complained);
    }

    [Fact]
    public async Task The_poll_declares_the_checkouts_it_holds_and_no_clones()
    {
        using var rig = new Rig().Board();

        await rig.RunAsync();

        var query = Assert.Single(rig.H.Wire.To("GET", Review)).Query;
        Assert.Contains("remote=https%3A%2F%2Fexample.test%2Frepo.git", query, StringComparison.Ordinal);
        Assert.Contains("standing=true", query, StringComparison.Ordinal);
        Assert.DoesNotContain("clones", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_ends_the_poll_rather_than_being_swallowed_as_a_failure()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Poll.RunAsync(rig.Runtime, Interval, cancelled.Token));
    }
}

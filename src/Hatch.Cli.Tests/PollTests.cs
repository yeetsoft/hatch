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
    private static string PutBuild(string key) => $"/api/hatch/issues/{key}/build-check";
    private const string TrunkBuilds = "/api/hatch/trunk-builds";

    private static RemoteHeads Heads(string? trunk = null, params (string Name, string Sha)[] branches) =>
        new("main", new Dictionary<string, string>(
            [new("main", trunk ?? Trunk), .. branches.Select(b => new KeyValuePair<string, string>(b.Name, b.Sha))]));

    private static Verdict Conflicted(params string[] files) =>
        new(MergeVerdicts.Conflicted, "main", Trunk, "aer-1-thing", Tip, files.Length == 0 ? ["a.txt"] : files, false);

    private static Verdict Clean(bool? holdsTrunk = true) => new(MergeVerdicts.Clean, "main", Trunk, "aer-1-thing", Tip, [], holdsTrunk);

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

        /// <summary>What the board answers a build verdict with: the row it now holds.</summary>
        public Rig Keeps(string key, BuildCheckDto held)
        {
            H.Wire.Replace("PUT", PutBuild(key), HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(held, Fixtures.Json));
            return this;
        }

        /// <summary>What the board says it holds for every trunk, in place of the default settled one the harness stubs.</summary>
        public Rig StoresTrunks(params TrunkBuildDto[] rows)
        {
            H.Wire.Replace("GET", TrunkBuilds, HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(rows, Fixtures.Json));
            return this;
        }

        /// <summary>What the board answers a trunk verdict with: the row it now holds.</summary>
        public Rig KeepsTrunk(TrunkBuildDto held)
        {
            H.Wire.Replace("PUT", TrunkBuilds, HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(held, Fixtures.Json));
            return this;
        }

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
        Assert.False(put.HoldsTrunk);
    }

    [Fact]
    public async Task A_clean_verdict_reports_whether_it_holds_the_trunk()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean(true);

        await rig.RunAsync();

        Assert.True(Assert.Single(rig.H.Wire.To("PUT", Put("AER-1"))).Read<MergeCheckRequest>().HoldsTrunk);
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
        var stored = Fixtures.MergeCheck(MergeVerdicts.Clean, "main", "example.test/repo", holdsTrunk: true) with
        {
            Remote = "https://example.test/repo.git", TrunkSha = Trunk, Branch = "aer-1-thing", BranchSha = Tip,
        };
        using var rig = new Rig().Board(Fixtures.Review("AER-1", null, checks: [stored]));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));

        await rig.RunAsync();

        Assert.Equal([$"heads {rig.H.Root}"], rig.H.Workspace.Calls);
        Assert.Empty(rig.H.Wire.To("PUT", Put("AER-1")));
        Assert.Empty(rig.H.Say.Said);
    }

    [Fact]
    public async Task A_stored_clean_verdict_missing_the_holdstrunk_field_is_rechecked_once_though_nothing_moved()
    {
        var stored = Fixtures.MergeCheck(MergeVerdicts.Clean, "main", "example.test/repo") with
        {
            Remote = "https://example.test/repo.git", TrunkSha = Trunk, Branch = "aer-1-thing", BranchSha = Tip,
            HoldsTrunk = null,
        };
        using var rig = new Rig().Board(Fixtures.Review("AER-1", null, checks: [stored]));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean(true);

        await rig.RunAsync();

        Assert.Equal(1, rig.Count("fetch"));
        Assert.Single(rig.H.Wire.To("PUT", Put("AER-1")));

        await rig.Later().RunAsync();

        // The fingerprint the first check recorded still holds: no second look.
        Assert.Equal(1, rig.Count("fetch"));
        Assert.Single(rig.H.Wire.To("PUT", Put("AER-1")));
    }

    [Fact]
    public async Task A_stored_conflicted_verdict_whose_shas_match_is_trusted_too()
    {
        var stored = Fixtures.MergeCheck(MergeVerdicts.Conflicted, "main", "example.test/repo", files: ["a.txt"]) with
        {
            Remote = "https://example.test/repo.git", TrunkSha = Trunk, Branch = "aer-1-thing", BranchSha = Tip,
        };
        using var rig = new Rig().Board(Fixtures.Review("AER-1", null, checks: [stored]));
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
        using var rig = new Rig().Board(Fixtures.Review("AER-1", null, checks: [stored]));
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
        using var rig = new Rig().Board(Fixtures.Review("AER-1", null, checks: [stored]));
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
        var stored = Fixtures.MergeCheck(MergeVerdicts.Conflicted, "main", "example.test/repo", files: ["a.txt"]) with
        {
            Remote = "https://example.test/repo.git", TrunkSha = Moved, Branch = "aer-1-thing", BranchSha = Tip,
        };
        using var rig = new Rig().Board(Fixtures.Review("AER-1", null, checks: [stored]));
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
    public async Task An_empty_column_is_one_read_and_the_trunk_still_gets_checked()
    {
        using var rig = new Rig().Board();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();

        await rig.RunAsync();

        // The review read, and the trunk half's own - which runs whether or
        // not anything is in review, so a quiet board is not a poll that never
        // gets there.
        Assert.Equal(2, rig.H.Wire.Calls.Count);
        Assert.Equal([$"heads {rig.H.Root}"], rig.H.Workspace.Calls);
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

    // ---- The build on the tip ----

    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    private static ForgeAnswer Failed(params string[] names) =>
        new(new BuildRead(BuildVerdicts.Failed, (names.Length == 0 ? ["api"] : names).Select(n => new FailingCheck(n, $"https://forge.example/{n}", 9)).ToList()), null);

    private static ForgeAnswer PendingFailing(params string[] names) =>
        new(new BuildRead(BuildVerdicts.Pending, (names.Length == 0 ? ["api"] : names).Select(n => new FailingCheck(n, $"https://forge.example/{n}", 9)).ToList()), null);

    private static ForgeAnswer Read(string verdict) => new(new BuildRead(verdict, []), null);

    /// <summary>One issue in review with one branch on origin, a merge verdict that is already known, and a board that keeps the build.</summary>
    private static Rig Building(BuildCheckDto? stored = null, string sha = "", BuildCheckDto? kept = null, string? pullRequestUrl = null)
    {
        var tip = sha.Length == 0 ? Tip : sha;
        var review = stored is null
            ? Fixtures.Review("AER-1", pullRequestUrl: pullRequestUrl)
            : Fixtures.Review("AER-1", [], [], [stored], pullRequestUrl);
        var rig = new Rig().Board(review);
        rig.Keeps("AER-1", kept ?? Fixtures.Build(BuildVerdicts.Passed, "example.test/repo", sha: tip));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();
        return rig;
    }

    [Fact]
    public async Task A_branch_in_review_gets_the_build_on_its_tip_read_and_reported_without_a_fetch_of_its_own()
    {
        using var rig = Building(kept: Fixtures.Build(BuildVerdicts.Failed, "example.test/repo", Tip, failing: ["api", "CI"]));
        rig.H.Forge.Answer = Failed("CI", "api");

        await rig.RunAsync();

        Assert.Equal([$"{rig.H.Root} {Tip}"], rig.H.Forge.Reads);

        var put = Assert.Single(rig.H.Wire.To("PUT", PutBuild("AER-1"))).Read<BuildCheckRequest>();
        Assert.Equal("https://example.test/repo.git", put.Remote);
        Assert.Equal("aer-1-thing", put.Branch);
        Assert.Equal(Tip, put.Sha);
        Assert.Equal(BuildVerdicts.Failed, put.Verdict);
        Assert.Equal(["CI", "api"], put.Failing!.Select(f => f.Name));
        Assert.Equal("https://forge.example/CI", put.Failing![0].Url);
        Assert.Equal("test:/checkout", put.Runner);
        Assert.False(put.PushedByIncrement);

        // The merge half fetched once for itself; the build half added none.
        Assert.Equal(1, rig.Count("fetch"));
        Assert.Contains($"hatch: AER-1 build on {Tip[..7]} failed (CI, api)", rig.H.Say.Said);
    }

    [Fact]
    public async Task A_check_that_has_failed_is_sent_while_the_build_is_still_pending()
    {
        using var rig = Building(kept: Fixtures.Build(BuildVerdicts.Pending, "example.test/repo", Tip, failing: ["api"]));
        rig.H.Forge.Answer = PendingFailing("api");

        await rig.RunAsync();

        var put = Assert.Single(rig.H.Wire.To("PUT", PutBuild("AER-1"))).Read<BuildCheckRequest>();
        Assert.Equal(BuildVerdicts.Pending, put.Verdict);
        Assert.Equal(["api"], put.Failing!.Select(f => f.Name));
    }

    [Fact]
    public async Task The_forge_is_handed_the_boards_identity_for_the_repository_where_the_project_binds_one()
    {
        var repo = Fixtures.Repository("https://example.test/repo.git", "example.test/repo", primary: true, matchedRemote: "https://example.test/repo.git");
        using var rig = new Rig().Board(Fixtures.Review("AER-1", [repo]));
        rig.Keeps("AER-1", Fixtures.Build(BuildVerdicts.Passed, "example.test/repo", Tip));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();
        rig.H.Forge.Answer = Read(BuildVerdicts.Passed);

        await rig.RunAsync();

        Assert.Equal(["example.test/repo"], rig.H.Forge.Canonicals);
    }

    [Theory]
    [InlineData(BuildVerdicts.Passed)]
    [InlineData(BuildVerdicts.Failed)]
    public async Task A_concluded_build_is_asked_about_once_however_many_intervals_pass(string verdict)
    {
        using var rig = Building(kept: Fixtures.Build(verdict, "example.test/repo", Tip));
        rig.H.Forge.Answer = verdict == BuildVerdicts.Failed ? Failed() : Read(verdict);

        await rig.RunAsync();
        await rig.Later().RunAsync();
        await rig.Later().RunAsync();

        Assert.Single(rig.H.Forge.Reads);
        Assert.Single(rig.H.Wire.To("PUT", PutBuild("AER-1")));
    }

    [Theory]
    [InlineData(BuildVerdicts.Passed)]
    [InlineData(BuildVerdicts.Failed)]
    public async Task A_runner_that_restarts_does_not_ask_again_about_a_build_the_board_already_holds(string verdict)
    {
        var stored = Fixtures.Build(verdict, "example.test/repo", Tip) with { Remote = "https://example.test/repo.git" };
        using var rig = Building(stored);
        rig.H.Forge.Answer = Read(BuildVerdicts.Passed);

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.Reads);
        Assert.Empty(rig.H.Wire.To("PUT", PutBuild("AER-1")));
    }

    [Fact]
    public async Task A_pending_build_is_asked_about_again_next_interval()
    {
        using var rig = Building(kept: Fixtures.Build(BuildVerdicts.Pending, "example.test/repo", Tip));
        rig.H.Forge.Answer = Read(BuildVerdicts.Pending);

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Equal(2, rig.H.Forge.Reads.Count);
    }

    [Fact]
    public async Task A_pending_build_the_board_holds_is_not_vouched_for()
    {
        var stored = Fixtures.Build(BuildVerdicts.Pending, "example.test/repo", Tip) with { Remote = "https://example.test/repo.git" };
        using var rig = Building(stored, kept: stored);
        rig.H.Forge.Answer = Read(BuildVerdicts.Pending);

        await rig.RunAsync();

        Assert.Single(rig.H.Forge.Reads);
    }

    [Fact]
    public async Task None_is_asked_about_again_until_ten_minutes_after_the_board_first_heard_of_the_sha_and_then_not()
    {
        using var rig = Building();
        rig.H.Forge.Answer = Read(BuildVerdicts.None);
        var heard = new DateTimeOffset(2026, 9, 27, 2, 0, 0, TimeSpan.Zero);
        BuildCheckDto None(int minutes) =>
            Fixtures.Build(BuildVerdicts.None, "example.test/repo", Tip, shaSince: heard) with { CheckedAt = heard.AddMinutes(minutes) };

        // Just pushed: the checks have not appeared yet.
        rig.Keeps("AER-1", None(0));
        await rig.RunAsync();
        Assert.Single(rig.H.Forge.Reads);

        rig.Keeps("AER-1", None(9));
        await rig.Later(60).RunAsync();
        Assert.Equal(2, rig.H.Forge.Reads.Count);

        // A read that was taken past the window is the last one.
        rig.Keeps("AER-1", None(10));
        await rig.Later(60).RunAsync();
        Assert.Equal(3, rig.H.Forge.Reads.Count);

        await rig.Later(60).RunAsync();
        await rig.Later(60).RunAsync();
        Assert.Equal(3, rig.H.Forge.Reads.Count);
    }

    [Fact]
    public async Task A_stored_none_read_past_the_window_vouches_for_its_tip_and_a_younger_one_does_not()
    {
        var heard = new DateTimeOffset(2026, 9, 27, 1, 0, 0, TimeSpan.Zero);
        var old = Fixtures.Build(BuildVerdicts.None, "example.test/repo", Tip, shaSince: heard) with
        {
            Remote = "https://example.test/repo.git", CheckedAt = heard.AddMinutes(10),
        };
        using (var rig = Building(old))
        {
            rig.H.Forge.Answer = Read(BuildVerdicts.None);
            await rig.RunAsync();
            Assert.Empty(rig.H.Forge.Reads);
        }

        using (var rig = Building(old with { CheckedAt = heard.AddMinutes(9) }))
        {
            rig.H.Forge.Answer = Read(BuildVerdicts.None);
            await rig.RunAsync();
            Assert.Single(rig.H.Forge.Reads);
        }
    }

    [Fact]
    public async Task A_stored_verdict_about_another_sha_does_not_vouch_and_a_moved_tip_is_read_again()
    {
        var stored = Fixtures.Build(BuildVerdicts.Failed, "example.test/repo", Tip2) with { Remote = "https://example.test/repo.git" };
        using var rig = Building(stored, kept: Fixtures.Build(BuildVerdicts.Passed, "example.test/repo", Tip));
        rig.H.Forge.Answer = Read(BuildVerdicts.Passed);

        await rig.RunAsync();
        Assert.Single(rig.H.Forge.Reads);

        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Moved));
        await rig.Later().RunAsync();

        Assert.Equal([$"{rig.H.Root} {Tip}", $"{rig.H.Root} {Moved}"], rig.H.Forge.Reads);
    }

    [Fact]
    public async Task A_verdict_that_says_what_the_board_says_is_reported_and_not_announced_again()
    {
        var stored = Fixtures.Build(BuildVerdicts.Pending, "example.test/repo", Tip) with { Remote = "https://example.test/repo.git" };
        using var rig = Building(stored, kept: stored);
        rig.H.Forge.Answer = Read(BuildVerdicts.Pending);

        await rig.RunAsync();

        Assert.Single(rig.H.Wire.To("PUT", PutBuild("AER-1")));
        Assert.DoesNotContain(rig.H.Say.Said, l => l.Contains("build on", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_ambiguous_or_missing_branch_is_not_read()
    {
        using var rig = Building();
        rig.H.Forge.Answer = Read(BuildVerdicts.Passed);

        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: [("aer-1-thing", Tip), ("aer-1-again", Tip2)]);
        await rig.RunAsync();

        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        await rig.Later().RunAsync();

        Assert.Empty(rig.H.Forge.Reads);
        Assert.Empty(rig.H.Wire.To("PUT", PutBuild("AER-1")));
    }

    [Fact]
    public async Task A_forge_that_cannot_answer_reports_nothing_says_one_line_once_and_leaves_the_merge_half_alone()
    {
        using var rig = Building();
        rig.H.Forge.Answer = new ForgeAnswer(null, "gh is not installed");

        await rig.RunAsync();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip2));
        await rig.Later().RunAsync();

        Assert.Empty(rig.H.Wire.To("PUT", PutBuild("AER-1")));
        Assert.Equal(["hatch: could not read builds in checkout - gh is not installed"], rig.H.Say.Complained);

        // The merge half reported both times, whatever the forge said.
        Assert.Equal(2, rig.H.Wire.To("PUT", Put("AER-1")).Count);
    }

    [Fact]
    public async Task A_forge_that_cannot_answer_is_one_call_for_the_checkout_and_not_one_per_issue()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"), Fixtures.Review("AER-2"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: [("aer-1-thing", Tip), ("aer-2-other", Tip2)]);
        rig.H.Forge.Answer = new ForgeAnswer(null, "gh is not installed");

        await rig.RunAsync();

        Assert.Single(rig.H.Forge.Reads);
    }

    [Fact]
    public async Task A_forge_that_throws_is_a_line_and_the_merge_half_still_reported()
    {
        using var rig = Building();
        rig.H.Forge.Reading = (_, _) => throw new InvalidOperationException("gh fell over");

        await rig.RunAsync();

        Assert.Single(rig.H.Wire.To("PUT", Put("AER-1")));
        Assert.Contains("gh fell over", Assert.Single(rig.H.Say.Complained), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_board_that_would_not_take_the_build_is_a_line_and_is_asked_again_next_interval()
    {
        using var rig = Building();
        rig.H.Wire.Replace("PUT", PutBuild("AER-1"), HttpStatusCode.NotFound, "\"no route\"");
        rig.H.Forge.Answer = Read(BuildVerdicts.Passed);

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Equal(2, rig.H.Forge.Reads.Count);
        Assert.Single(rig.H.Say.Complained);
        Assert.Contains("would not take the build", rig.H.Say.Complained[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stored_build_is_matched_by_the_boards_identity_where_the_project_binds_one()
    {
        var repo = Fixtures.Repository("https://example.test/repo.git", "example.test/repo", primary: true, matchedRemote: "https://example.test/repo.git");
        var stored = Fixtures.Build(BuildVerdicts.Failed, "example.test/repo", Tip);
        using var rig = new Rig().Board(Fixtures.Review("AER-1", [repo], [], [stored]));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();
        rig.H.Forge.Answer = Read(BuildVerdicts.Passed);

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.Reads);
    }

    [Fact]
    public async Task A_checkout_whose_trunk_origin_lacks_still_has_its_builds_read()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        rig.Keeps("AER-1", Fixtures.Build(BuildVerdicts.Passed, "example.test/repo", Tip));
        rig.H.Workspace.HeadsFor[rig.H.Root] = new RemoteHeads("main", new Dictionary<string, string> { ["aer-1-thing"] = Tip });
        rig.H.Forge.Answer = Read(BuildVerdicts.Passed);

        await rig.RunAsync();

        // The merge half says it could not compare; the build needs no trunk.
        Assert.Single(rig.H.Forge.Reads);
        Assert.Contains("origin has no main", Assert.Single(rig.H.Say.Complained), StringComparison.Ordinal);
    }

    // ---- The trunk's own build (HA-100) ----

    /// <summary>A checkout with an origin and nothing in review, whose trunk is at the default sha with nothing stored about it.</summary>
    private static Rig NoStoredTrunk()
    {
        var rig = new Rig().Board();
        rig.StoresTrunks();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        return rig;
    }

    [Fact]
    public async Task A_trunk_nobody_has_checked_is_read_and_reported_even_with_nothing_in_review()
    {
        using var rig = NoStoredTrunk();
        rig.H.Forge.Answer = Failed("CI");
        rig.KeepsTrunk(Fixtures.TrunkBuild(BuildVerdicts.Failed, failing: ["CI"]));

        await rig.RunAsync();

        Assert.Equal([$"{rig.H.Root} {Trunk}"], rig.H.Forge.Reads);

        var put = Assert.Single(rig.H.Wire.To("PUT", TrunkBuilds)).Read<TrunkBuildRequest>();
        Assert.Equal("https://example.test/repo.git", put.Remote);
        Assert.Equal("main", put.Trunk);
        Assert.Equal(Trunk, put.Sha);
        Assert.Equal(BuildVerdicts.Failed, put.Verdict);
        Assert.Equal(["CI"], put.Failing!.Select(f => f.Name));
        Assert.Equal("test:/checkout", put.Runner);

        Assert.Contains($"hatch: main build on {Trunk[..7]} failed (CI)", rig.H.Say.Said);
    }

    [Fact]
    public async Task Only_checkouts_with_an_origin_have_their_trunk_checked()
    {
        using var rig = new Rig().Board();
        rig.StoresTrunks();
        var runtime = rig.H.Runtime with
        {
            Clock = rig.Clock,
            Checkouts = [new CheckoutEntry(rig.H.Root, null, Standing: true)],
        };

        await rig.Poll.RunAsync(runtime, Interval, default);

        Assert.Empty(rig.H.Wire.To("GET", TrunkBuilds));
        Assert.Empty(rig.H.Workspace.Calls);
    }

    [Fact]
    public async Task The_trunk_half_reuses_the_heads_the_review_half_already_took()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        rig.StoresTrunks();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();

        await rig.RunAsync();

        Assert.Equal(1, rig.Count("heads"));
    }

    [Theory]
    [InlineData(BuildVerdicts.Passed)]
    [InlineData(BuildVerdicts.Failed)]
    public async Task A_concluded_trunk_build_is_asked_about_once_however_many_intervals_pass(string verdict)
    {
        using var rig = NoStoredTrunk();
        rig.H.Forge.Answer = verdict == BuildVerdicts.Failed ? Failed() : Read(verdict);
        rig.KeepsTrunk(Fixtures.TrunkBuild(verdict, failing: verdict == BuildVerdicts.Failed ? ["api"] : []));

        await rig.RunAsync();
        await rig.Later().RunAsync();
        await rig.Later().RunAsync();

        Assert.Single(rig.H.Forge.Reads);
        Assert.Single(rig.H.Wire.To("PUT", TrunkBuilds));
    }

    [Fact]
    public async Task A_runner_that_restarts_does_not_ask_again_about_a_trunk_build_the_board_already_holds()
    {
        using var rig = new Rig().Board();
        rig.StoresTrunks(Fixtures.TrunkBuild(BuildVerdicts.Passed));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.H.Forge.Answer = Read(BuildVerdicts.Passed);

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.Reads);
        Assert.Empty(rig.H.Wire.To("PUT", TrunkBuilds));
    }

    [Fact]
    public async Task A_pending_trunk_build_is_asked_about_again_next_interval()
    {
        using var rig = NoStoredTrunk();
        rig.H.Forge.Answer = Read(BuildVerdicts.Pending);
        rig.KeepsTrunk(Fixtures.TrunkBuild(BuildVerdicts.Pending));

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Equal(2, rig.H.Forge.Reads.Count);
    }

    [Fact]
    public async Task A_trunk_none_is_asked_about_again_until_ten_minutes_after_the_board_first_heard_of_the_sha_and_then_not()
    {
        using var rig = NoStoredTrunk();
        rig.H.Forge.Answer = Read(BuildVerdicts.None);
        var heard = new DateTimeOffset(2026, 9, 27, 2, 0, 0, TimeSpan.Zero);
        TrunkBuildDto None(int minutes) =>
            Fixtures.TrunkBuild(BuildVerdicts.None, sha: Trunk, shaSince: heard) with { CheckedAt = heard.AddMinutes(minutes) };

        rig.KeepsTrunk(None(0));
        await rig.RunAsync();
        Assert.Single(rig.H.Forge.Reads);

        rig.KeepsTrunk(None(9));
        await rig.Later(60).RunAsync();
        Assert.Equal(2, rig.H.Forge.Reads.Count);

        rig.KeepsTrunk(None(10));
        await rig.Later(60).RunAsync();
        Assert.Equal(3, rig.H.Forge.Reads.Count);

        await rig.Later(60).RunAsync();
        Assert.Equal(3, rig.H.Forge.Reads.Count);
    }

    [Fact]
    public async Task A_moved_trunk_tip_is_read_again()
    {
        using var rig = NoStoredTrunk();
        rig.H.Forge.Answer = Read(BuildVerdicts.Passed);
        rig.KeepsTrunk(Fixtures.TrunkBuild(BuildVerdicts.Passed));

        await rig.RunAsync();
        Assert.Single(rig.H.Forge.Reads);

        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(Moved);
        rig.KeepsTrunk(Fixtures.TrunkBuild(BuildVerdicts.Passed, sha: Moved));
        await rig.Later().RunAsync();

        Assert.Equal([$"{rig.H.Root} {Trunk}", $"{rig.H.Root} {Moved}"], rig.H.Forge.Reads);
    }

    [Fact]
    public async Task A_forge_that_cannot_answer_the_trunk_is_one_line_and_reports_nothing()
    {
        using var rig = NoStoredTrunk();
        rig.H.Forge.Answer = new ForgeAnswer(null, "gh is not installed");

        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("PUT", TrunkBuilds));
        Assert.Contains("gh is not installed", Assert.Single(rig.H.Say.Complained), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_board_that_would_not_take_the_trunk_build_is_a_line_and_is_asked_again_next_interval()
    {
        using var rig = NoStoredTrunk();
        rig.H.Wire.Replace("PUT", TrunkBuilds, HttpStatusCode.NotFound, "\"no route\"");
        rig.H.Forge.Answer = Read(BuildVerdicts.Passed);

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Equal(2, rig.H.Forge.Reads.Count);
        Assert.Single(rig.H.Say.Complained);
        Assert.Contains("would not take the trunk build", rig.H.Say.Complained[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_board_that_cannot_answer_for_trunk_builds_is_one_line_and_the_review_half_still_works()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        rig.H.Wire.Replace("GET", TrunkBuilds, HttpStatusCode.NotFound, "\"no route\"");
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();

        await rig.RunAsync();

        Assert.Single(rig.H.Wire.To("PUT", Put("AER-1")));
        Assert.Contains("could not read the board's trunk builds", Assert.Single(rig.H.Say.Complained), StringComparison.Ordinal);
    }

    // ---- The pull request half ----

    private static readonly string PrUrl = "https://github.com/o/r/pull/1";
    private static readonly string PrUrl2 = "https://github.com/o/r/pull/2";
    private const string Statuses = "/api/hatch/statuses";

    private static string Merged(string key) => $"/api/hatch/work/{key}/merged";

    [Fact]
    public async Task A_merged_pull_request_advances_the_issue_with_the_url_that_was_read_and_is_announced()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1", pullRequestUrl: PrUrl));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Merged, null);
        rig.H.Wire.Json("POST", Merged("AER-1"), Fixtures.Issue("AER-1") with { StatusId = 4 });
        rig.H.Wire.Json("GET", Statuses, new[] { Fixtures.Status(4, "Done") });

        await rig.RunAsync();

        Assert.Equal([$"{rig.H.Root} {PrUrl}"], rig.H.Forge.PullRequestReads);
        Assert.Equal(PrUrl, Assert.Single(rig.H.Wire.To("POST", Merged("AER-1"))).Read<PullRequestMergedRequest>().Url);
        Assert.Contains("hatch: AER-1 pull request merged -> Done", rig.H.Say.Said);
    }

    [Theory]
    [InlineData(PullRequestStates.Open)]
    [InlineData(PullRequestStates.Closed)]
    [InlineData(PullRequestStates.Unknown)]
    public async Task An_open_closed_or_unknown_pull_request_is_not_advanced_and_nothing_is_said(string state)
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1", pullRequestUrl: PrUrl));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();
        rig.H.Forge.PullRequestAnswer = new(state, null);

        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("POST", Merged("AER-1")));
        Assert.DoesNotContain(rig.H.Say.Said, l => l.Contains("pull request", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_issue_with_no_pull_request_url_is_never_asked_about()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.PullRequestReads);
    }

    [Fact]
    public async Task A_409_from_the_merged_route_is_not_a_fault_and_the_poll_carries_on_to_the_next_issue()
    {
        using var rig = new Rig().Board(
            Fixtures.Review("AER-1", pullRequestUrl: PrUrl),
            Fixtures.Review("AER-2", pullRequestUrl: PrUrl2));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: [("aer-1-thing", Tip), ("aer-2-other", Tip2)]);
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-2")] = Clean();
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Merged, null);
        rig.H.Wire.Reply("POST", Merged("AER-1"), HttpStatusCode.Conflict, "\"AER-1 moved\"");
        rig.H.Wire.Json("POST", Merged("AER-2"), Fixtures.Issue("AER-2") with { StatusId = 4 });
        rig.H.Wire.Json("GET", Statuses, new[] { Fixtures.Status(4, "Done") });

        await rig.RunAsync();

        Assert.Equal([$"{rig.H.Root} {PrUrl}", $"{rig.H.Root} {PrUrl2}"], rig.H.Forge.PullRequestReads);
        Assert.Single(rig.H.Wire.To("POST", Merged("AER-2")));
        Assert.Contains("hatch: AER-2 pull request merged -> Done", rig.H.Say.Said);
    }

    [Fact]
    public async Task A_forge_that_cannot_answer_pull_requests_complains_once_for_the_checkout_and_leaves_its_other_issues_alone()
    {
        using var rig = new Rig().Board(
            Fixtures.Review("AER-1", pullRequestUrl: PrUrl),
            Fixtures.Review("AER-2", pullRequestUrl: PrUrl2));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: [("aer-1-thing", Tip), ("aer-2-other", Tip2)]);
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-2")] = Clean();
        rig.H.Forge.PullRequestAnswer = new(null, "gh is not installed");

        await rig.RunAsync();

        Assert.Single(rig.H.Forge.PullRequestReads);
        Assert.Equal(["hatch: could not read pull requests in checkout - gh is not installed"], rig.H.Say.Complained);
    }

    [Fact]
    public async Task The_same_unreadable_forge_on_two_consecutive_intervals_complains_once()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1", pullRequestUrl: PrUrl));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();
        rig.H.Forge.PullRequestAnswer = new(null, "gh is not installed");

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Equal(["hatch: could not read pull requests in checkout - gh is not installed"], rig.H.Say.Complained);
    }

    [Fact]
    public async Task One_read_per_issue_when_its_project_binds_two_repositories()
    {
        var one = Fixtures.Repository("https://example.test/one.git", "example.test/one", primary: true, matchedRemote: "https://example.test/one.git");
        var two = Fixtures.Repository("https://example.test/two.git", "example.test/two", primary: false, matchedRemote: "https://example.test/two.git");
        using var rig = new Rig().Board(Fixtures.Review("AER-1", [one, two], pullRequestUrl: PrUrl));

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
            rig.H.Workspace.Verdicts[(path, "AER-1")] = Clean();
        }
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null);

        await rig.Poll.RunAsync(runtime, Interval, default);

        Assert.Single(rig.H.Forge.PullRequestReads);
    }

    [Fact]
    public async Task The_pull_request_half_does_not_stop_the_merge_or_build_halves()
    {
        using var rig = Building(kept: Fixtures.Build(BuildVerdicts.Passed, "example.test/repo", Tip), pullRequestUrl: PrUrl);
        rig.H.Forge.Answer = Read(BuildVerdicts.Passed);
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null);

        await rig.RunAsync();

        Assert.Single(rig.H.Wire.To("PUT", Put("AER-1")));
        Assert.Single(rig.H.Wire.To("PUT", PutBuild("AER-1")));
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

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
    private const string PlaybookFiles = "/api/hatch/playbook-files";

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

        /// <summary>What the board says it holds for every playbook file, in place of the default current one the harness stubs.</summary>
        public Rig StoresPlaybookFiles(params PlaybookFileDto[] rows)
        {
            H.Wire.Replace("GET", PlaybookFiles, HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(new PlaybookFilesDto(".hatch/playbooks.yaml", rows), Fixtures.Json));
            return this;
        }

        /// <summary>What the board answers a playbook file put with: the row it now holds.</summary>
        public Rig KeepsPlaybookFile(PlaybookFileDto held)
        {
            H.Wire.Replace("PUT", PlaybookFiles, HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(held, Fixtures.Json));
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

        // The harness's own playbook-files default only vouches for its one
        // default checkout - these two are neither, so each needs its own row
        // or the playbook half would fetch both, same as a trunk build would.
        rig.H.Wire.Replace("GET", "/api/hatch/playbook-files", HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(
            new PlaybookFilesDto(".hatch/playbooks.yaml",
            [
                Fixtures.PlaybookFile(remote: "https://example.test/one.git", canonical: "example.test/one"),
                Fixtures.PlaybookFile(remote: "https://example.test/two.git", canonical: "example.test/two"),
            ]),
            Fixtures.Json));

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

        // See the sibling test above: this checkout is not the harness's own default one either.
        rig.H.Wire.Replace("GET", "/api/hatch/playbook-files", HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(
            new PlaybookFilesDto(".hatch/playbooks.yaml",
                [Fixtures.PlaybookFile(remote: "https://example.test/two.git", canonical: "example.test/two")]),
            Fixtures.Json));

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

        // The review read, and the trunk and playbook-file halves' own -
        // both run whether or not anything is in review, so a quiet board is
        // not a poll that never gets there.
        Assert.Equal(3, rig.H.Wire.Calls.Count);
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

    private static readonly DateTimeOffset HeardAt = new(2026, 9, 27, 2, 0, 0, TimeSpan.Zero);

    private static BuildCheckDto NoneAt(int minutes) =>
        Fixtures.Build(BuildVerdicts.None, "example.test/repo", Tip, shaSince: HeardAt) with
        {
            Remote = "https://example.test/repo.git", CheckedAt = HeardAt.AddMinutes(minutes),
        };

    private static ReviewCheckDto Merging(BuildCheckDto stored, string? headSha, DateTimeOffset? headSince) =>
        Fixtures.Review("AER-1", [], [], [stored], mergeWork: new MergeWorkDto(WorkKinds.Merge, "merge")) with
        {
            PullRequestHeadSha = headSha, PullRequestHeadSince = headSince,
        };

    /// <summary>A row in the merges column, with a branch on origin and a merge verdict that is already known.</summary>
    private static Rig Holding(ReviewCheckDto row, BuildCheckDto kept)
    {
        var rig = new Rig().Board(row);
        rig.Keeps("AER-1", kept);
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-1")] = Clean();
        return rig;
    }

    [Fact]
    public async Task A_merging_rows_none_is_never_final_while_the_pull_request_names_another_head()
    {
        using var rig = Holding(Merging(NoneAt(12), Tip2, HeardAt), NoneAt(12));
        rig.H.Forge.Answer = Read(BuildVerdicts.None);

        await rig.RunAsync();
        Assert.Single(rig.H.Forge.Reads);

        await rig.Later(60).RunAsync();
        Assert.Equal(2, rig.H.Forge.Reads.Count);
    }

    [Fact]
    public async Task A_merging_rows_none_window_counts_from_when_the_pull_request_reached_the_tip_then_one_read_per_interval()
    {
        using var rig = Holding(Merging(NoneAt(15), Tip, HeardAt.AddMinutes(11)), NoneAt(22));
        rig.H.Forge.Answer = Read(BuildVerdicts.None);

        // Four minutes past the head, inside the window: read.
        await rig.RunAsync();
        Assert.Single(rig.H.Forge.Reads);

        // Past the window now, and read again only once per interval.
        await rig.Later(240).RunAsync();
        Assert.Single(rig.H.Forge.Reads);

        await rig.Later(60).RunAsync();
        Assert.Equal(2, rig.H.Forge.Reads.Count);
    }

    [Fact]
    public async Task A_merging_row_past_the_window_reports_a_build_that_starts_late_and_then_stops_asking()
    {
        using var rig = Holding(
            Merging(NoneAt(15), Tip, HeardAt),
            Fixtures.Build(BuildVerdicts.Passed, "example.test/repo", Tip, shaSince: HeardAt));
        rig.H.Forge.Answer = Read(BuildVerdicts.Passed);

        await rig.RunAsync();

        var put = Assert.Single(rig.H.Wire.To("PUT", PutBuild("AER-1"))).Read<BuildCheckRequest>();
        Assert.Equal(BuildVerdicts.Passed, put.Verdict);
        Assert.Single(rig.H.Forge.Reads);

        await rig.Later(600).RunAsync();
        await rig.Later(600).RunAsync();
        Assert.Single(rig.H.Forge.Reads);
        Assert.Single(rig.H.Wire.To("PUT", PutBuild("AER-1")));
    }

    [Fact]
    public async Task A_none_concluded_in_review_does_not_hide_the_build_once_the_row_is_merging()
    {
        using var rig = Building(NoneAt(5), kept: NoneAt(15));
        rig.H.Forge.Answer = Read(BuildVerdicts.None);

        await rig.RunAsync();
        Assert.Single(rig.H.Forge.Reads);

        await rig.Later(60).RunAsync();
        Assert.Single(rig.H.Forge.Reads);

        rig.H.Wire.Replace(
            "GET", Review, HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(new[] { Merging(NoneAt(15), Tip, HeardAt) }, Fixtures.Json));
        await rig.Later(300).RunAsync();
        Assert.Equal(2, rig.H.Forge.Reads.Count);
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

    // ---- The playbook file (HA-459) ----

    /// <summary>A checkout with an origin and nothing in review, whose playbook file the board does not hold at any sha.</summary>
    private static Rig NoStoredPlaybookFile()
    {
        var rig = new Rig().Board();
        rig.StoresPlaybookFiles();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        return rig;
    }

    [Fact]
    public async Task A_playbook_file_at_a_sha_the_board_does_not_hold_is_fetched_read_and_put()
    {
        using var rig = NoStoredPlaybookFile();
        var bytes = "playbooks: []"u8.ToArray();
        rig.H.Workspace.BlobsFor[(rig.H.Root, "main", ".hatch/playbooks.yaml")] = new BlobRead(BlobOutcome.Found, bytes, null);
        rig.H.Forge.FileUrlFor[".hatch/playbooks.yaml"] = "https://example.test/repo/blob/main/.hatch/playbooks.yaml";
        rig.KeepsPlaybookFile(Fixtures.PlaybookFile());

        await rig.RunAsync();

        Assert.Equal(1, rig.Count("fetch"));
        Assert.Equal(1, rig.Count("read-blob"));
        Assert.Contains($"read-blob {rig.H.Root} main .hatch/playbooks.yaml", rig.H.Workspace.Calls);

        var put = Assert.Single(rig.H.Wire.To("PUT", PlaybookFiles)).Read<PlaybookFileRequest>();
        Assert.Equal("https://example.test/repo.git", put.Remote);
        Assert.Equal("main", put.Trunk);
        Assert.Equal(Trunk, put.Sha);
        Assert.Equal(Convert.ToBase64String(bytes), put.Content);
        Assert.Equal("https://example.test/repo/blob/main/.hatch/playbooks.yaml", put.Url);
        Assert.Equal("test:/checkout", put.Runner);
    }

    [Fact]
    public async Task A_playbook_file_the_board_already_holds_at_the_trunks_sha_is_neither_fetched_nor_put()
    {
        using var rig = new Rig().Board();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();

        await rig.RunAsync();

        Assert.Equal(0, rig.Count("fetch"));
        Assert.Equal(0, rig.Count("read-blob"));
        Assert.Empty(rig.H.Wire.To("PUT", PlaybookFiles));
    }

    [Fact]
    public async Task A_trunk_with_no_playbook_file_at_all_is_put_with_null_content()
    {
        using var rig = NoStoredPlaybookFile();
        rig.H.Workspace.BlobsFor[(rig.H.Root, "main", ".hatch/playbooks.yaml")] = new BlobRead(BlobOutcome.Absent, null, null);
        rig.H.Forge.FileUrlFor[".hatch/playbooks.yaml"] = "https://example.test/repo/blob/main/.hatch/playbooks.yaml";
        rig.KeepsPlaybookFile(Fixtures.PlaybookFile());

        await rig.RunAsync();

        Assert.Null(Assert.Single(rig.H.Wire.To("PUT", PlaybookFiles)).Read<PlaybookFileRequest>().Content);
    }

    [Fact]
    public async Task A_board_that_would_not_take_the_playbook_file_is_a_line_and_is_asked_again_next_interval()
    {
        using var rig = NoStoredPlaybookFile();
        rig.H.Workspace.BlobsFor[(rig.H.Root, "main", ".hatch/playbooks.yaml")] = new BlobRead(BlobOutcome.Found, "playbooks: []"u8.ToArray(), null);
        rig.H.Forge.FileUrlFor[".hatch/playbooks.yaml"] = "https://example.test/repo/blob/main/.hatch/playbooks.yaml";
        rig.H.Wire.Replace("PUT", PlaybookFiles, HttpStatusCode.NotFound, "\"no route\"");

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Equal(2, rig.Count("fetch"));
        Assert.Equal(2, rig.Count("read-blob"));
        Assert.Equal(2, rig.H.Wire.To("PUT", PlaybookFiles).Count);
        Assert.Contains("playbook file", Assert.Single(rig.H.Say.Complained), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_playbook_file_put_with_no_forge_link_has_a_null_url()
    {
        using var rig = NoStoredPlaybookFile();
        rig.H.Workspace.BlobsFor[(rig.H.Root, "main", ".hatch/playbooks.yaml")] = new BlobRead(BlobOutcome.Found, "playbooks: []"u8.ToArray(), null);
        rig.KeepsPlaybookFile(Fixtures.PlaybookFile());

        await rig.RunAsync();

        Assert.Null(Assert.Single(rig.H.Wire.To("PUT", PlaybookFiles)).Read<PlaybookFileRequest>().Url);
    }

    [Fact]
    public async Task A_failed_blob_read_is_one_complaint_and_is_not_cached_as_an_answer()
    {
        using var rig = NoStoredPlaybookFile();
        rig.H.Workspace.BlobsFor[(rig.H.Root, "main", ".hatch/playbooks.yaml")] = new BlobRead(BlobOutcome.Failed, null, "some git error");

        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("PUT", PlaybookFiles));
        Assert.Contains("some git error", Assert.Single(rig.H.Say.Complained), StringComparison.Ordinal);

        await rig.Later().RunAsync();

        Assert.Equal(2, rig.Count("fetch"));
        Assert.Equal(2, rig.Count("read-blob"));
    }

    // ---- Re-running a transient trunk failure (HA-794) ----

    // A stored row at the trunk's *current* sha vouches for itself once it is
    // `passed` or `failed` (see VouchesTrunk/ConcludedTrunk above) and the poll
    // never asks the forge again - so every case below that wants to reach the
    // new rerun logic on a fresh `failed` read stores its row at the trunk's
    // *previous* tip (`Moved`), the way a trunk that just advanced actually
    // looks: a project binding and (where relevant) a rerun count carried over
    // from a row about a sha other than the one just read, exactly the "starts
    // fresh" case `ReadTrunksAsync`'s own comment describes. The one case about
    // the cap itself instead stores the row at the *current* sha with verdict
    // `pending` - which still does not vouch - so its `RerunCount` is read as
    // the real in-progress count for this sha.

    [Fact]
    public async Task A_transient_failure_under_the_cap_is_re_run_and_told_to_the_board_as_pending()
    {
        using var rig = new Rig().Board();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.StoresTrunks(Fixtures.TrunkBuild(BuildVerdicts.Passed, projectKey: "AER", sha: Moved));
        rig.Runtime.Readout.SetDrives(["AER"]);
        rig.H.Forge.Answer = Failed("CI");
        rig.H.Forge.Excerpts["CI"] = "ECONNRESET";
        rig.H.Forge.RerunAnswer = new RerunAnswer(RerunOutcomes.Reran, null);
        rig.KeepsTrunk(Fixtures.TrunkBuild(BuildVerdicts.Pending, projectKey: "AER", rerunCount: 1));

        await rig.RunAsync();

        Assert.Equal(["CI"], rig.H.Forge.Logs);
        Assert.Single(rig.H.Forge.RerunCalls);

        var put = Assert.Single(rig.H.Wire.To("PUT", TrunkBuilds)).Read<TrunkBuildRequest>();
        Assert.Equal(BuildVerdicts.Pending, put.Verdict);
        Assert.True(put.Reran);

        Assert.Contains(rig.H.Say.Said, l => l.EndsWith("- re-running", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_re_run_is_refused_once_the_sha_has_already_been_re_run_to_the_cap()
    {
        using var rig = new Rig().Board();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.StoresTrunks(Fixtures.TrunkBuild(BuildVerdicts.Pending, projectKey: "AER", rerunCount: Poll.RerunCap));
        rig.Runtime.Readout.SetDrives(["AER"]);
        rig.H.Forge.Answer = Failed("CI");
        rig.H.Forge.Excerpts["CI"] = "ECONNRESET";
        rig.KeepsTrunk(Fixtures.TrunkBuild(BuildVerdicts.Failed, projectKey: "AER", rerunCount: Poll.RerunCap, failing: ["CI"]));

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.Logs);
        Assert.Empty(rig.H.Forge.RerunCalls);

        var put = Assert.Single(rig.H.Wire.To("PUT", TrunkBuilds)).Read<TrunkBuildRequest>();
        Assert.Equal(BuildVerdicts.Failed, put.Verdict);
        Assert.False(put.Reran);
    }

    [Fact]
    public async Task A_non_transient_failure_is_left_alone()
    {
        using var rig = new Rig().Board();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.StoresTrunks(Fixtures.TrunkBuild(BuildVerdicts.Passed, projectKey: "AER", sha: Moved));
        rig.Runtime.Readout.SetDrives(["AER"]);
        rig.H.Forge.Answer = Failed("CI");
        // No excerpt stubbed for "CI" - LogAsync answers null, which IsTransient reads as false.
        rig.KeepsTrunk(Fixtures.TrunkBuild(BuildVerdicts.Failed, projectKey: "AER", failing: ["CI"]));

        await rig.RunAsync();

        Assert.Equal(["CI"], rig.H.Forge.Logs);
        Assert.Empty(rig.H.Forge.RerunCalls);

        var put = Assert.Single(rig.H.Wire.To("PUT", TrunkBuilds)).Read<TrunkBuildRequest>();
        Assert.Equal(BuildVerdicts.Failed, put.Verdict);
        Assert.False(put.Reran);
    }

    [Fact]
    public async Task A_mixed_set_of_failing_checks_is_left_alone_and_stops_reading_logs_at_the_first_non_transient_one()
    {
        using var rig = new Rig().Board();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.StoresTrunks(Fixtures.TrunkBuild(BuildVerdicts.Passed, projectKey: "AER", sha: Moved));
        rig.Runtime.Readout.SetDrives(["AER"]);
        rig.H.Forge.Answer = Failed("Lint", "CI");
        rig.H.Forge.Excerpts["CI"] = "ECONNRESET";
        rig.KeepsTrunk(Fixtures.TrunkBuild(BuildVerdicts.Failed, projectKey: "AER", failing: ["Lint", "CI"]));

        await rig.RunAsync();

        Assert.Equal(["Lint"], rig.H.Forge.Logs);
        Assert.Empty(rig.H.Forge.RerunCalls);

        var put = Assert.Single(rig.H.Wire.To("PUT", TrunkBuilds)).Read<TrunkBuildRequest>();
        Assert.Equal(BuildVerdicts.Failed, put.Verdict);
        Assert.False(put.Reran);
    }

    [Fact]
    public async Task A_runner_that_does_not_drive_the_project_does_not_re_run_a_transient_failure()
    {
        using var rig = new Rig().Board();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.StoresTrunks(Fixtures.TrunkBuild(BuildVerdicts.Passed, projectKey: "AER", sha: Moved));
        rig.H.Forge.Answer = Failed("CI");
        rig.H.Forge.Excerpts["CI"] = "ECONNRESET";
        rig.KeepsTrunk(Fixtures.TrunkBuild(BuildVerdicts.Failed, projectKey: "AER", failing: ["CI"]));

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.Logs);
        Assert.Empty(rig.H.Forge.RerunCalls);

        var put = Assert.Single(rig.H.Wire.To("PUT", TrunkBuilds)).Read<TrunkBuildRequest>();
        Assert.Equal(BuildVerdicts.Failed, put.Verdict);
        Assert.False(put.Reran);
    }

    [Fact]
    public async Task A_forge_that_cannot_answer_a_re_run_costs_one_line_and_not_the_night()
    {
        using var rig = new Rig().Board();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.StoresTrunks(Fixtures.TrunkBuild(BuildVerdicts.Passed, projectKey: "AER", sha: Moved));
        rig.Runtime.Readout.SetDrives(["AER"]);
        rig.H.Forge.Answer = Failed("CI");
        rig.H.Forge.Excerpts["CI"] = "ECONNRESET";
        rig.H.Forge.RerunAnswer = new RerunAnswer(null, "gh is not installed");
        rig.KeepsTrunk(Fixtures.TrunkBuild(BuildVerdicts.Failed, projectKey: "AER", failing: ["CI"]));
        // A failed rerun attempt still leaves the build really failed, so this
        // also reaches the HA-795 bug-filing gate below - stubbed as refused the
        // same way an auto-filing-off project is, which is silent, so it does
        // not add to the one line this test is actually about.
        rig.H.Wire.Reply("POST", "/api/hatch/trunk-builds/1/bug", HttpStatusCode.BadRequest, "auto-filing off");

        await rig.RunAsync();

        Assert.Contains("could not re-run the failing checks", Assert.Single(rig.H.Say.Complained), StringComparison.Ordinal);

        var put = Assert.Single(rig.H.Wire.To("PUT", TrunkBuilds)).Read<TrunkBuildRequest>();
        Assert.Equal(BuildVerdicts.Failed, put.Verdict);
        Assert.False(put.Reran);

        // The night is not ended: the row is now concluded (really failed), so a
        // later interval does not ask again and says nothing new.
        await rig.Later().RunAsync();
        Assert.Single(rig.H.Say.Complained);
    }

    // ---- Filing the bug a trunk failure never got re-run into (HA-795) ----

    private static string PostBug(long id) => $"/api/hatch/trunk-builds/{id}/bug";

    [Fact]
    public async Task A_trunk_failure_that_was_not_re_run_is_filed_and_its_key_printed()
    {
        using var rig = new Rig().Board();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.StoresTrunks(Fixtures.TrunkBuild(BuildVerdicts.Passed, projectKey: "AER", sha: Moved));
        rig.Runtime.Readout.SetDrives(["AER"]);
        rig.H.Forge.Answer = Failed("CI");
        rig.KeepsTrunk(Fixtures.TrunkBuild(BuildVerdicts.Failed, projectKey: "AER", failing: ["CI"]));
        rig.H.Wire.Json("POST", PostBug(1), Fixtures.Issue("AER-50"));

        await rig.RunAsync();

        Assert.Single(rig.H.Wire.To("POST", PostBug(1)));
        Assert.Contains(rig.H.Say.Said, l => l.EndsWith("- filed AER-50", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_board_that_refuses_to_file_costs_one_line_and_not_the_night()
    {
        using var rig = new Rig().Board();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.StoresTrunks(Fixtures.TrunkBuild(BuildVerdicts.Passed, projectKey: "AER", sha: Moved));
        rig.Runtime.Readout.SetDrives(["AER"]);
        rig.H.Forge.Answer = Failed("CI");
        rig.KeepsTrunk(Fixtures.TrunkBuild(BuildVerdicts.Failed, projectKey: "AER", failing: ["CI"]));
        // 500, not 503/502 - this harness's HatchClient keeps its own real
        // retry clock (Harness.Clock, not Rig.Clock), so a transient status
        // here would actually spend HatchClient's 90s retry window and run
        // past this suite's blame-hang-timeout instead of failing at once.
        rig.H.Wire.Reply("POST", PostBug(1), HttpStatusCode.InternalServerError, "down");

        await rig.RunAsync();

        Assert.Contains("could not file a bug", Assert.Single(rig.H.Say.Complained), StringComparison.Ordinal);

        // The sha is already concluded from the PUT above, regardless of the
        // POST's own failure - so a later interval asks neither again.
        await rig.Later().RunAsync();
        Assert.Single(rig.H.Say.Complained);
    }

    [Fact]
    public async Task A_row_that_already_carries_a_bug_makes_no_call()
    {
        using var rig = new Rig().Board();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.StoresTrunks(Fixtures.TrunkBuild(BuildVerdicts.Passed, projectKey: "AER", sha: Moved));
        rig.Runtime.Readout.SetDrives(["AER"]);
        rig.H.Forge.Answer = Failed("CI");
        rig.KeepsTrunk(Fixtures.TrunkBuild(BuildVerdicts.Failed, projectKey: "AER", bugIssueKey: "AER-50", failing: ["CI"]));

        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("POST", PostBug(1)));
    }

    [Fact]
    public async Task A_project_with_auto_filing_off_makes_the_call_and_says_nothing()
    {
        using var rig = new Rig().Board();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.StoresTrunks(Fixtures.TrunkBuild(BuildVerdicts.Passed, projectKey: "AER", sha: Moved));
        rig.Runtime.Readout.SetDrives(["AER"]);
        rig.H.Forge.Answer = Failed("CI");
        rig.KeepsTrunk(Fixtures.TrunkBuild(BuildVerdicts.Failed, projectKey: "AER", failing: ["CI"]));
        rig.H.Wire.Reply("POST", PostBug(1), HttpStatusCode.BadRequest, "AER has auto-filing off - turn it on from the Projects page");

        await rig.RunAsync();

        Assert.Single(rig.H.Wire.To("POST", PostBug(1)));
        Assert.Empty(rig.H.Say.Complained);
        Assert.DoesNotContain(rig.H.Say.Said, l => l.Contains("- filed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_runner_that_does_not_drive_the_project_does_not_file_a_bug()
    {
        using var rig = new Rig().Board();
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        rig.StoresTrunks(Fixtures.TrunkBuild(BuildVerdicts.Passed, projectKey: "AER", sha: Moved));
        rig.H.Forge.Answer = Failed("CI");
        rig.KeepsTrunk(Fixtures.TrunkBuild(BuildVerdicts.Failed, projectKey: "AER", failing: ["CI"]));

        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("POST", PostBug(1)));
    }

    // ---- Landings ----

    private const string Remote = "https://example.test/repo.git";
    private static readonly string Landed = new('e', 40);

    private static string PutLanding(string key) => $"/api/hatch/issues/{key}/landing";

    /// <summary>A trunk-landing row in review, holding one landing as the unbound checkout spells it.</summary>
    private static ReviewCheckDto LandingRow(string key, string? containedBy = null, string? sha = null) =>
        Fixtures.Review(key) with
        {
            Landings = [Fixtures.Landing(sha: sha ?? Landed, containedBy: containedBy) with { Remote = Remote }],
            LandingWork = new("pending", "waiting on the trunk"),
        };

    private static Rig LandingBoard(params ReviewCheckDto[] rows)
    {
        var rig = new Rig();
        rig.H.Wire.Json("GET", Review, rows);
        foreach (var row in rows) rig.H.Wire.Json("PUT", PutLanding(row.Key), Fixtures.Landing());
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();
        return rig;
    }

    [Fact]
    public async Task A_landing_in_the_trunk_is_told_to_the_board_with_the_tip_that_holds_it_and_nothing_else_is_written()
    {
        using var rig = LandingBoard(LandingRow("AER-1"));
        rig.H.Workspace.OnTrunkFor[(rig.H.Root, Landed)] = true;

        await rig.RunAsync();

        var put = Assert.Single(rig.H.Wire.To("PUT", PutLanding("AER-1"))).Read<LandingWriteRequest>();
        Assert.Equal(Remote, put.Remote);
        Assert.Equal(Landed, put.Sha);
        Assert.Equal("main", put.Trunk);
        Assert.Equal(Trunk, put.ContainedBy);
        Assert.Empty(rig.H.Wire.To("PUT", Put("AER-1")));
        Assert.Empty(rig.H.Wire.To("PUT", PutBuild("AER-1")));
        Assert.Equal(0, rig.Count("check"));
        Assert.Equal([$"AER-1 is on main at {Landed[..7]}"], rig.H.Say.Said.Select(l => l.Replace("hatch: ", "")));
    }

    [Fact]
    public async Task Two_landing_rows_in_one_checkout_are_one_fetch()
    {
        using var rig = LandingBoard(LandingRow("AER-1"), LandingRow("AER-2"));
        rig.H.Workspace.OnTrunkFor[(rig.H.Root, Landed)] = true;

        await rig.RunAsync();

        Assert.Equal(1, rig.Count("fetch"));
        Assert.Single(rig.H.Wire.To("PUT", PutLanding("AER-1")));
        Assert.Single(rig.H.Wire.To("PUT", PutLanding("AER-2")));
    }

    [Fact]
    public async Task Landings_the_board_already_holds_at_the_trunks_tip_are_not_fetched_for()
    {
        using var rig = LandingBoard(LandingRow("AER-1", containedBy: Trunk), LandingRow("AER-2", containedBy: Trunk));

        await rig.RunAsync();

        Assert.Equal(0, rig.Count("fetch"));
        Assert.Equal(0, rig.Count("resolve"));
        Assert.Empty(rig.H.Wire.To("PUT", PutLanding("AER-1")));
    }

    [Fact]
    public async Task A_trunk_that_moved_since_the_stored_tip_is_told_again_with_the_new_tip_and_not_announced()
    {
        using var rig = LandingBoard(LandingRow("AER-1", containedBy: Moved));
        rig.H.Workspace.OnTrunkFor[(rig.H.Root, Landed)] = true;

        await rig.RunAsync();

        var put = Assert.Single(rig.H.Wire.To("PUT", PutLanding("AER-1"))).Read<LandingWriteRequest>();
        Assert.Equal(Trunk, put.ContainedBy);
        Assert.Empty(rig.H.Say.Said);
    }

    [Fact]
    public async Task A_landing_not_in_the_trunk_writes_nothing_and_is_not_fetched_for_again_until_the_trunk_moves()
    {
        using var rig = LandingBoard(LandingRow("AER-1"));

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Equal(1, rig.Count("fetch"));
        Assert.Empty(rig.H.Wire.To("PUT", PutLanding("AER-1")));

        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(trunk: Moved);
        await rig.Later().RunAsync();

        Assert.Equal(2, rig.Count("fetch"));
        Assert.Empty(rig.H.Wire.To("PUT", PutLanding("AER-1")));
    }

    [Fact]
    public async Task A_landing_the_board_refused_is_one_line_across_two_polls_and_is_asked_again()
    {
        using var rig = LandingBoard(LandingRow("AER-1"));
        rig.H.Workspace.OnTrunkFor[(rig.H.Root, Landed)] = true;
        rig.H.Wire.Replace("PUT", PutLanding("AER-1"), HttpStatusCode.BadRequest, "\"no such landing\"");

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Equal(2, rig.H.Wire.To("PUT", PutLanding("AER-1")).Count);
        Assert.Contains("would not take the landing on main", Assert.Single(rig.H.Say.Complained), StringComparison.Ordinal);
        Assert.Empty(rig.H.Say.Said);
    }

    [Fact]
    public async Task An_origin_without_the_landings_trunk_is_one_line_and_nothing_is_written()
    {
        using var rig = LandingBoard(LandingRow("AER-1"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = new RemoteHeads("main", new Dictionary<string, string>());

        await rig.RunAsync();

        Assert.Contains("origin has no main", Assert.Single(rig.H.Say.Complained), StringComparison.Ordinal);
        Assert.Equal(0, rig.Count("fetch"));
        Assert.Empty(rig.H.Wire.To("PUT", PutLanding("AER-1")));
    }

    [Fact]
    public async Task A_failed_fetch_is_one_line_and_nothing_is_written()
    {
        using var rig = LandingBoard(LandingRow("AER-1"));
        rig.H.Workspace.FetchAnswer = false;
        rig.H.Workspace.OnTrunkFor[(rig.H.Root, Landed)] = true;

        await rig.RunAsync();

        Assert.Contains("could not fetch from origin", Assert.Single(rig.H.Say.Complained), StringComparison.Ordinal);
        Assert.Equal(0, rig.Count("resolve"));
        Assert.Empty(rig.H.Wire.To("PUT", PutLanding("AER-1")));
    }

    [Fact]
    public async Task A_pull_request_row_is_still_merge_checked_beside_a_landing_row()
    {
        using var rig = LandingBoard(LandingRow("AER-1"), Fixtures.Review("AER-2"));
        rig.H.Wire.Json("PUT", Put("AER-2"), Fixtures.MergeCheck());
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-2-thing", Tip));
        rig.H.Workspace.Verdicts[(rig.H.Root, "AER-2")] = Clean();

        await rig.RunAsync();

        Assert.Single(rig.H.Wire.To("PUT", Put("AER-2")));
        Assert.Empty(rig.H.Wire.To("PUT", Put("AER-1")));
        Assert.Equal(1, rig.Count("check"));
    }
}

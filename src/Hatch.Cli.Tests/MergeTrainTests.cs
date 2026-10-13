using System.Net;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>
/// The merge train: the pull request, merge and update halves that used to run
/// inside <see cref="Poll"/> - against a fake workspace, so what is asserted is
/// the order and the count of the calls and not what git or <c>gh</c> does with
/// them.
/// </summary>
public sealed class MergeTrainTests
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

    private static Verdict Clean(bool? holdsTrunk = true) => new(MergeVerdicts.Clean, "main", Trunk, "aer-1-thing", Tip, [], holdsTrunk);

    /// <summary>A board that holds one unbound issue in review, and takes any verdict.</summary>
    private sealed class Rig : IDisposable
    {
        public Harness H { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 27, 2, 0, 0, TimeSpan.Zero));
        public MergeTrain Train { get; } = new();

        public Runtime Runtime => H.Runtime with { Clock = Clock };

        public Rig Board(params ReviewCheckDto[] rows)
        {
            H.Wire.Json("GET", Review, rows);
            foreach (var row in rows) H.Wire.Json("PUT", Put(row.Key), Fixtures.MergeCheck());
            return this;
        }

        public Task RunAsync() => Train.TickAsync(Runtime, default);

        /// <summary>Time passing, to the next interval.</summary>
        public Rig Later(int seconds = Interval)
        {
            Clock.Advance(TimeSpan.FromSeconds(seconds));
            return this;
        }

        public void Dispose() => H.Dispose();
    }

    // ---- The pull request half ----

    private const string PrUrl = "https://github.com/o/r/pull/1";
    private const string PrUrl2 = "https://github.com/o/r/pull/2";
    private const string Statuses = "/api/hatch/statuses";

    private static string Merged(string key) => $"/api/hatch/work/{key}/merged";
    private static string Closed(string key) => $"/api/hatch/work/{key}/closed";

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

        await rig.Train.TickAsync(runtime, default);

        Assert.Single(rig.H.Forge.PullRequestReads);
    }

    // ---- The merge half ----

    private const string MergeCanonical = "example.test/repo";

    private static string Comments(string key) => $"/api/hatch/issues/{key}/comments";

    /// <summary>
    /// A row in the merges column, judged <paramref name="kind"/>: the project's
    /// one repository, bound so <c>where.Canonical</c> resolves to
    /// <see cref="MergeCanonical"/>, and a clean merge check on <paramref name="branch"/>
    /// whose shas already match origin - so Poll's own merge-check half vouches for
    /// it and fetches nothing, leaving only this half to ask anything of <c>gh</c>.
    /// Null <paramref name="kind"/> is an ordinary review row: no <c>MergeWork</c> at all.
    /// </summary>
    private static ReviewCheckDto MergeRow(
        string key, string branch, string pullRequestUrl, string? kind = WorkKinds.Merge, string? judgedSha = null)
    {
        var sha = judgedSha ?? Tip;
        var repo = Fixtures.Repository("https://example.test/repo.git", MergeCanonical);
        var stored = Fixtures.MergeCheck(MergeVerdicts.Clean, "main", MergeCanonical, holdsTrunk: true) with
        {
            Remote = "https://example.test/repo.git", TrunkSha = Trunk, Branch = branch, BranchSha = sha,
        };

        return Fixtures.Review(
            key, [repo], pullRequestUrl: pullRequestUrl,
            mergeWork: kind is null ? null : new MergeWorkDto(kind, "its branch holds main and its build passes"),
            checks: [stored]);
    }

    [Fact]
    public async Task A_merge_row_whose_head_still_matches_the_judged_sha_is_merged_and_announced()
    {
        using var rig = new Rig().Board(MergeRow("AER-1", "aer-1-thing", PrUrl));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null, HeadSha: Tip);
        rig.H.Forge.MergeAnswer = new(true, null);
        rig.H.Wire.Json("POST", Merged("AER-1"), Fixtures.Issue("AER-1") with { StatusId = 4 });
        rig.H.Wire.Json("GET", Statuses, new[] { Fixtures.Status(4, "Done") });
        rig.H.Wire.Json("POST", Comments("AER-1"), Fixtures.Comment());

        await rig.RunAsync();

        Assert.Equal([(rig.H.Root, PrUrl, Tip)], rig.H.Forge.MergeCalls);
        Assert.Equal(PrUrl, Assert.Single(rig.H.Wire.To("POST", Merged("AER-1"))).Read<PullRequestMergedRequest>().Url);
        Assert.Contains("hatch: AER-1 pull request merged -> Done", rig.H.Say.Said);

        var comment = Assert.Single(rig.H.Wire.To("POST", Comments("AER-1"))).Read<CommentCreateRequest>().Body;
        Assert.Contains(Tip, comment, StringComparison.Ordinal);
        Assert.Contains("test:/checkout", comment, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_moved_head_is_not_merged_and_comments_once()
    {
        using var rig = new Rig().Board(MergeRow("AER-1", "aer-1-thing", PrUrl));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null, HeadSha: Moved);
        rig.H.Wire.Json("POST", Comments("AER-1"), Fixtures.Comment());

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Empty(rig.H.Forge.MergeCalls);
        Assert.Single(rig.H.Wire.To("POST", Comments("AER-1")));
    }

    [Fact]
    public async Task A_closed_pull_request_on_a_merge_row_comments_once_and_is_never_merged()
    {
        using var rig = new Rig().Board(MergeRow("AER-1", "aer-1-thing", PrUrl));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Closed, null);
        rig.H.Wire.Json("POST", Closed("AER-1"), Fixtures.Issue("AER-1"));
        rig.H.Wire.Json("POST", Comments("AER-1"), Fixtures.Comment());

        await rig.RunAsync();
        await rig.Later().RunAsync();

        var comment = Assert.Single(rig.H.Wire.To("POST", Comments("AER-1"))).Read<CommentCreateRequest>().Body;
        Assert.Equal("Its pull request was closed without merging.", comment);
        Assert.Empty(rig.H.Forge.MergeCalls);
    }

    [Fact]
    public async Task A_409_from_the_closed_route_is_not_a_fault_and_comments_nothing()
    {
        using var rig = new Rig().Board(MergeRow("AER-1", "aer-1-thing", PrUrl));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Closed, null);
        rig.H.Wire.Reply("POST", Closed("AER-1"), HttpStatusCode.Conflict, "\"AER-1 moved\"");

        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("POST", Comments("AER-1")));
        Assert.Empty(rig.H.Say.Complained);
    }

    [Fact]
    public async Task A_review_row_s_closed_pull_request_is_never_posted_to_the_closed_route()
    {
        using var rig = new Rig().Board(MergeRow("AER-1", "aer-1-thing", PrUrl, kind: null));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Closed, null);

        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("POST", Closed("AER-1")));
    }

    [Fact]
    public async Task A_gh_pr_merge_refusal_comments_the_reason_verbatim_once_and_is_never_retried()
    {
        using var rig = new Rig().Board(MergeRow("AER-1", "aer-1-thing", PrUrl));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null, HeadSha: Tip);
        rig.H.Forge.MergeAnswer = new(false, "a required review is still outstanding");
        rig.H.Wire.Json("POST", Comments("AER-1"), Fixtures.Comment());

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Single(rig.H.Forge.MergeCalls);
        var comment = Assert.Single(rig.H.Wire.To("POST", Comments("AER-1"))).Read<CommentCreateRequest>().Body;
        Assert.Contains("a required review is still outstanding", comment, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_409_from_the_merged_route_on_a_merge_row_is_not_a_fault_and_the_poll_reaches_the_next_merge_row()
    {
        using var rig = new Rig().Board(
            MergeRow("AER-1", "aer-1-thing", PrUrl),
            MergeRow("AER-2", "aer-2-other", PrUrl2, judgedSha: Tip2));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: [("aer-1-thing", Tip), ("aer-2-other", Tip2)]);
        rig.H.Forge.PullRequestFor[PrUrl] = new(PullRequestStates.Open, null, HeadSha: Tip);
        rig.H.Forge.PullRequestFor[PrUrl2] = new(PullRequestStates.Open, null, HeadSha: Tip2);
        rig.H.Forge.MergeAnswer = new(true, null);
        rig.H.Wire.Reply("POST", Merged("AER-1"), HttpStatusCode.Conflict, "\"AER-1 moved\"");
        rig.H.Wire.Json("POST", Merged("AER-2"), Fixtures.Issue("AER-2") with { StatusId = 4 });
        rig.H.Wire.Json("GET", Statuses, new[] { Fixtures.Status(4, "Done") });
        rig.H.Wire.Json("POST", Comments("AER-2"), Fixtures.Comment());

        await rig.RunAsync();

        Assert.Equal([(rig.H.Root, PrUrl, Tip), (rig.H.Root, PrUrl2, Tip2)], rig.H.Forge.MergeCalls);
        Assert.Single(rig.H.Wire.To("POST", Merged("AER-2")));
        Assert.Contains("hatch: AER-2 pull request merged -> Done", rig.H.Say.Said);
    }

    [Theory]
    [InlineData(WorkKinds.Update, 2)]
    [InlineData(WorkKinds.Hold, 1)]
    [InlineData(null, 1)]
    public async Task A_row_not_judged_merge_is_never_asked_about_by_this_half(string? kind, int pullRequestReads)
    {
        using var rig = new Rig().Board(MergeRow("AER-1", "aer-1-thing", PrUrl, kind: kind));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null, HeadSha: Tip);
        rig.H.Forge.MergeAnswer = new(true, null);

        await rig.RunAsync();

        // The pull-request half above still reads it, MergeWork or no, and the
        // update half below reads it again for a row judged Update - what is
        // this half's alone is that it never calls MergeAsync or comments.
        Assert.Equal(pullRequestReads, rig.H.Forge.PullRequestReads.Count);
        Assert.Empty(rig.H.Forge.MergeCalls);
        Assert.Empty(rig.H.Wire.To("POST", Comments("AER-1")));
    }

    [Fact]
    public async Task A_runner_with_no_checkout_of_the_repository_is_silently_skipped()
    {
        var repo = Fixtures.Repository("https://example.other/repo.git", MergeCanonical);
        var stored = Fixtures.MergeCheck(MergeVerdicts.Clean, "main", MergeCanonical, holdsTrunk: true) with
        {
            Remote = "https://example.other/repo.git", TrunkSha = Trunk, Branch = "aer-1-thing", BranchSha = Tip,
        };
        var review = Fixtures.Review(
            "AER-1", [repo], pullRequestUrl: PrUrl,
            mergeWork: new MergeWorkDto(WorkKinds.Merge, "its branch holds main and its build passes"),
            checks: [stored]);
        using var rig = new Rig().Board(review);

        // Unrelated to the issue's own (unmatched) repository: the standing
        // checkout still gets its trunk build read every poll, stubbed quiet
        // here so the only thing under test is this half's silence.
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.PullRequestReads);
        Assert.Empty(rig.H.Forge.MergeCalls);
        Assert.Empty(rig.H.Wire.To("POST", Comments("AER-1")));
        Assert.Empty(rig.H.Say.Complained);
    }

    [Fact]
    public async Task A_forge_that_cannot_read_the_pull_request_on_a_merge_row_is_skipped_with_the_one_checkout_complaint()
    {
        using var rig = new Rig().Board(MergeRow("AER-1", "aer-1-thing", PrUrl));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Forge.PullRequestAnswer = new(null, "gh is not installed");

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.MergeCalls);
        Assert.Empty(rig.H.Wire.To("POST", Comments("AER-1")));

        // The pull-request half above complains about the same checkout too -
        // its own, separate dedupe - so this proves no crash and nothing else
        // said, not that the whole poll says it only once.
        Assert.NotEmpty(rig.H.Say.Complained);
        Assert.All(
            rig.H.Say.Complained,
            l => Assert.Equal("hatch: could not read pull requests in checkout - gh is not installed", l));
    }

    // ---- The update half ----

    /// <summary>
    /// A row in the merges column, judged <paramref name="kind"/> (<see
    /// cref="WorkKinds.Update"/> unless said otherwise): the project's one
    /// repository, bound so <c>where.Canonical</c> resolves to <see
    /// cref="MergeCanonical"/>, and a clean merge check on <paramref name="branch"/>
    /// whose shas already match origin - so Poll's own merge-check half vouches
    /// for it and fetches nothing, leaving only this half to call the forge.
    /// Null <paramref name="kind"/> is an ordinary review row: no <c>MergeWork</c>
    /// at all.
    /// </summary>
    private static ReviewCheckDto UpdateRow(
        string key, string branch, string? pullRequestUrl = PrUrl, string? kind = WorkKinds.Update, string? judgedSha = null)
    {
        var sha = judgedSha ?? Tip;
        var repo = Fixtures.Repository("https://example.test/repo.git", MergeCanonical);
        var stored = Fixtures.MergeCheck(MergeVerdicts.Clean, "main", MergeCanonical, holdsTrunk: true) with
        {
            Remote = "https://example.test/repo.git", TrunkSha = Trunk, Branch = branch, BranchSha = sha,
        };

        return Fixtures.Review(
            key, [repo], pullRequestUrl: pullRequestUrl,
            mergeWork: kind is null ? null : new MergeWorkDto(kind, "its branch holds main but needs the trunk brought in"),
            checks: [stored]);
    }

    [Fact]
    public async Task A_row_whose_head_still_matches_the_judged_sha_is_updated_and_announced()
    {
        using var rig = new Rig().Board(UpdateRow("AER-1", "aer-1-thing"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null, HeadSha: Tip, Number: 7);
        rig.H.Forge.UpdateAnswer = new(UpdateOutcomes.Updated, null);
        rig.H.Wire.Json("POST", Comments("AER-1"), Fixtures.Comment());

        await rig.RunAsync();

        Assert.Equal([(rig.H.Root, 7L, Tip)], rig.H.Forge.UpdateCalls);
        var comment = Assert.Single(rig.H.Wire.To("POST", Comments("AER-1"))).Read<CommentCreateRequest>().Body;
        Assert.Equal("Brought its branch current with main, as test:/checkout.", comment);
        Assert.Contains("hatch: AER-1 branch brought current with main", rig.H.Say.Said);
    }

    [Fact]
    public async Task A_moved_head_is_not_updated_and_comments_once()
    {
        using var rig = new Rig().Board(UpdateRow("AER-1", "aer-1-thing"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null, HeadSha: Moved, Number: 7);
        rig.H.Wire.Json("POST", Comments("AER-1"), Fixtures.Comment());

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Empty(rig.H.Forge.UpdateCalls);
        Assert.Single(rig.H.Wire.To("POST", Comments("AER-1")));
    }

    [Fact]
    public async Task A_conflicted_update_comments_once_and_is_not_repeated_at_the_same_judged_sha()
    {
        using var rig = new Rig().Board(UpdateRow("AER-1", "aer-1-thing"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null, HeadSha: Tip, Number: 7);
        rig.H.Forge.UpdateAnswer = new(UpdateOutcomes.Conflicted, "merge conflict between base and head");
        rig.H.Wire.Json("POST", Comments("AER-1"), Fixtures.Comment());

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Single(rig.H.Forge.UpdateCalls);
        var comment = Assert.Single(rig.H.Wire.To("POST", Comments("AER-1"))).Read<CommentCreateRequest>().Body;
        Assert.Equal("Its branch does not merge cleanly with main - nothing was pushed.", comment);
        Assert.Empty(rig.H.Say.Said.Where(l => l.StartsWith("hatch: AER-1", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_generic_update_failure_complains_and_is_retried_next_interval()
    {
        using var rig = new Rig().Board(UpdateRow("AER-1", "aer-1-thing"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null, HeadSha: Tip, Number: 7);
        rig.H.Forge.UpdateAnswer = new(null, "gh is not installed");

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Equal(2, rig.H.Forge.UpdateCalls.Count);
        Assert.Empty(rig.H.Wire.To("POST", Comments("AER-1")));
        Assert.All(rig.H.Say.Complained, l => Assert.Equal("hatch: could not bring AER-1's branch current in checkout - gh is not installed", l));
    }

    [Theory]
    [InlineData(WorkKinds.Merge)]
    [InlineData(WorkKinds.Hold)]
    [InlineData(null)]
    public async Task A_row_not_judged_update_is_never_asked_about_by_this_half(string? kind)
    {
        using var rig = new Rig().Board(UpdateRow("AER-1", "aer-1-thing", kind: kind));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Forge.PullRequestAnswer = new(PullRequestStates.Open, null, HeadSha: Tip, Number: 7);
        rig.H.Forge.MergeAnswer = new(true, null);

        await rig.RunAsync();

        // The merge half above still reads it and acts on a Merge row - what is
        // this half's alone is that it never calls UpdateAsync.
        Assert.Empty(rig.H.Forge.UpdateCalls);
    }

    [Fact]
    public async Task An_update_row_with_no_pull_request_url_is_never_asked_about_by_this_half()
    {
        using var rig = new Rig().Board(UpdateRow("AER-1", "aer-1-thing", pullRequestUrl: null));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.PullRequestReads);
        Assert.Empty(rig.H.Forge.UpdateCalls);
    }

    [Fact]
    public async Task A_runner_with_no_checkout_of_the_repository_is_silently_skipped_by_the_update_half()
    {
        var repo = Fixtures.Repository("https://example.other/repo.git", MergeCanonical);
        var stored = Fixtures.MergeCheck(MergeVerdicts.Clean, "main", MergeCanonical, holdsTrunk: true) with
        {
            Remote = "https://example.other/repo.git", TrunkSha = Trunk, Branch = "aer-1-thing", BranchSha = Tip,
        };
        var review = Fixtures.Review(
            "AER-1", [repo], pullRequestUrl: PrUrl,
            mergeWork: new MergeWorkDto(WorkKinds.Update, "its branch holds main but needs the trunk brought in"),
            checks: [stored]);
        using var rig = new Rig().Board(review);
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads();

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.PullRequestReads);
        Assert.Empty(rig.H.Forge.UpdateCalls);
        Assert.Empty(rig.H.Say.Complained);
    }

    [Fact]
    public async Task A_forge_that_cannot_read_the_pull_request_on_an_update_row_is_skipped_with_the_one_checkout_complaint()
    {
        using var rig = new Rig().Board(UpdateRow("AER-1", "aer-1-thing"));
        rig.H.Workspace.HeadsFor[rig.H.Root] = Heads(branches: ("aer-1-thing", Tip));
        rig.H.Forge.PullRequestAnswer = new(null, "gh is not installed");

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.UpdateCalls);
        Assert.Empty(rig.H.Wire.To("POST", Comments("AER-1")));
        Assert.NotEmpty(rig.H.Say.Complained);
        Assert.All(
            rig.H.Say.Complained,
            l => Assert.Equal("hatch: could not read pull requests in checkout - gh is not installed", l));
    }

    [Fact]
    public async Task Cancellation_ends_the_poll_rather_than_being_swallowed_as_a_failure()
    {
        using var rig = new Rig().Board(Fixtures.Review("AER-1"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Train.TickAsync(rig.Runtime, cancelled.Token));
    }
}

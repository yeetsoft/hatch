using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// The misplaced pull request half of the merge train: an issue in review with none recorded gets the one its
/// branch has, off another ticket where a key recorded it there, and is told where a person did.
/// </summary>
public sealed partial class MergeTrainTests
{
    private const string RepairUrl = "https://example.test/repo/pull/60";
    private const string RepairBranch = "aer-1-a-thing";

    private static string IssuePath(string key) => $"/api/issues/{key}";
    private static string CommentsPath(string key) => $"/api/issues/{key}/comments";

    /// <summary>What the board holds about who has the pull request, and what it is asked to change.</summary>
    private sealed class PullRequestHolding
    {
        public string? Owner { get; set; }
        public string? Kind { get; set; }
        public ContributionDto? Contribution { get; set; }
        public string? NoCodeOn { get; set; }

        /// <summary>Whether the first write on AER-1 is refused.</summary>
        public bool RefuseRecord { get; set; }
    }

    private static IssueCardDto HolderCard(string key, string? kind, ContributionDto? contribution = null, string type = "task") =>
        Fixtures.Card(key, type) with { PullRequestUrl = RepairUrl, PullRequestRecordedBy = "somebody", PullRequestRecordedByKind = kind, Contribution = contribution };

    private static ReviewCheckDto Candidate(
        string key = "AER-1", string? url = null, MergeWorkDto? mergeWork = null, string landing = Landings.PullRequest,
        string? projectKey = null, string branch = RepairBranch, IReadOnlyList<BuildCheckDto>? builds = null)
    {
        var check = Fixtures.MergeCheck(MergeVerdicts.Clean, "main", Fixtures.MergeCanonical) with
        {
            Remote = "https://example.test/repo.git", Branch = branch,
        };

        return Fixtures.Review(
            key, [Fixtures.Repository("https://example.test/repo.git", Fixtures.MergeCanonical)], [check],
            builds ?? [], url, mergeWork, projectKey) with { Landing = landing };
    }

    /// <summary>
    /// A board with <paramref name="rows"/> in review and a forge that lists <paramref name="pr"/> at
    /// <see cref="RepairUrl"/>. The board answers the holder lookup from <paramref name="held"/>, and a write
    /// of the pull request changes who holds it, as the real one does.
    /// </summary>
    private static Rig Repairing(PullRequestHolding held, PullRequestAnswer? pr, params ReviewCheckDto[] rows)
    {
        var rig = new Rig().Board(rows);
        var wire = rig.H.Wire;

        wire.Replace("GET", TrunkBuilds, HttpStatusCode.OK, AsJson(new[] { Fixtures.TrunkBuild() }));
        wire.Replace("GET", Projects, HttpStatusCode.OK, AsJson(new[] { AProject(1, "AER", Fixtures.MergeCanonical) }));
        wire.Replace("GET", Statuses, HttpStatusCode.OK, AsJson(new[]
        {
            Fixtures.Status(1, "Backlog"), Fixtures.Status(3, "In Progress"), Fixtures.Status(4, "In Review"),
            Fixtures.Status(5, "Done", terminal: true),
        }));

        wire.Serve("GET", Issues, call =>
        {
            if (!call.Query.Contains("pullRequestUrl=", StringComparison.Ordinal)) return "[]";
            return AsJson(held.Owner is null ? [] : new[] { HolderCard(held.Owner, held.Kind, held.Contribution) });
        });

        foreach (var key in new[] { "AER-1", "AER-2", "AER-3" })
        {
            wire.Serve("GET", IssuePath(key), _ =>
                AsJson(Fixtures.Issue(key, statusId: 4, pullRequestUrl: held.Owner == key ? RepairUrl : null)
                    with { NoCode = held.NoCodeOn == key ? "nothing to write" : null }));

            if (key == "AER-1" && held.RefuseRecord) wire.Once("PATCH", IssuePath(key), HttpStatusCode.Conflict, "refused");

            wire.Serve("PATCH", IssuePath(key), call =>
            {
                var patch = call.Read<IssuePatchRequest>();
                if (patch.PullRequestUrl is { Length: > 0 })
                {
                    held.Owner = key;
                    held.Kind = "key";
                }
                else if (held.Owner == key)
                {
                    held.Owner = null;
                    held.Kind = null;
                }

                return AsJson(Fixtures.Issue(key, statusId: 4, pullRequestUrl: held.Owner == key ? RepairUrl : null));
            });

            wire.Json("POST", CommentsPath(key), Fixtures.Comment());
        }

        rig.H.Forge.PullRequestList = pr is null
            ? new(new Dictionary<string, PullRequestAnswer>(), null)
            : new(new Dictionary<string, PullRequestAnswer> { [RepairUrl] = pr }, null);
        return rig;
    }

    private static PullRequestAnswer FromBranch(
        string head = RepairBranch, bool isFork = false, string baseBranch = "main", long number = 60) =>
        Outsider(number, head, isFork: isFork, baseBranch: baseBranch);

    private static IReadOnlyList<Call> RepairWrites(Rig rig) =>
        [.. rig.H.Wire.Calls.Where(c => c.Method == "PATCH" || (c.Method == "POST" && c.Path.EndsWith("/comments", StringComparison.Ordinal)))];

    [Fact]
    public async Task An_issue_in_review_with_no_pull_request_records_the_one_its_branch_has_and_says_so()
    {
        var held = new PullRequestHolding();
        using var rig = Repairing(held, FromBranch(), Candidate());

        await rig.RunAsync();

        var patch = Assert.Single(rig.H.Wire.To("PATCH", IssuePath("AER-1"))).Read<IssuePatchRequest>();
        Assert.Equal(RepairUrl, patch.PullRequestUrl);
        Assert.Null(patch.Description);
        Assert.Equal("AER-1", held.Owner);

        var comment = Assert.Single(rig.H.Wire.To("POST", CommentsPath("AER-1"))).Read<CommentCreateRequest>();
        Assert.Contains(RepairUrl, comment.Body);
        Assert.Contains(RepairBranch, comment.Body);
        Assert.Contains("recorded", comment.Body);
        Assert.Empty(rig.H.Say.Complained);
    }

    [Fact]
    public async Task A_pull_request_a_key_recorded_on_another_ticket_is_taken_off_it_first_and_both_say_so()
    {
        var held = new PullRequestHolding { Owner = "AER-2", Kind = "key" };
        using var rig = Repairing(held, FromBranch(), Candidate());

        await rig.RunAsync();

        var writes = RepairWrites(rig).Select(c => c.Route).ToList();
        Assert.Equal(
            [$"PATCH {IssuePath("AER-2")}", $"PATCH {IssuePath("AER-1")}", $"POST {CommentsPath("AER-1")}", $"POST {CommentsPath("AER-2")}"],
            writes);

        Assert.Equal("", Assert.Single(rig.H.Wire.To("PATCH", IssuePath("AER-2"))).Read<IssuePatchRequest>().PullRequestUrl);
        Assert.Equal(RepairUrl, Assert.Single(rig.H.Wire.To("PATCH", IssuePath("AER-1"))).Read<IssuePatchRequest>().PullRequestUrl);
        Assert.Equal("AER-1", held.Owner);

        Assert.Contains("AER-2", Assert.Single(rig.H.Wire.To("POST", CommentsPath("AER-1"))).Read<CommentCreateRequest>().Body);
        Assert.Contains("AER-1", Assert.Single(rig.H.Wire.To("POST", CommentsPath("AER-2"))).Read<CommentCreateRequest>().Body);
    }

    [Theory]
    [InlineData("person", false)]
    [InlineData(null, false)]
    [InlineData("robot", false)]
    [InlineData("key", true)]
    public async Task A_pull_request_somebody_else_recorded_is_left_alone_and_the_issue_is_told_once_per_run(
        string? kind, bool filedOutsider)
    {
        var contribution = filedOutsider
            ? new ContributionDto(RepairUrl, RepairBranch, "octocat", "MEMBER", DateTimeOffset.UnixEpoch, "runner")
            : null;
        var held = new PullRequestHolding { Owner = "AER-2", Kind = kind, Contribution = contribution };
        using var rig = Repairing(held, FromBranch(), Candidate());

        await rig.RunAsync();
        rig.Later();
        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("PATCH", IssuePath("AER-1")));
        Assert.Empty(rig.H.Wire.To("PATCH", IssuePath("AER-2")));
        Assert.Empty(rig.H.Wire.To("POST", CommentsPath("AER-2")));
        Assert.Equal("AER-2", held.Owner);

        var said = Assert.Single(rig.H.Wire.To("POST", CommentsPath("AER-1"))).Read<CommentCreateRequest>();
        Assert.Contains("AER-2", said.Body);
    }

    [Fact]
    public async Task A_restarted_runner_says_it_again()
    {
        var held = new PullRequestHolding { Owner = "AER-2", Kind = "person" };
        using var rig = Repairing(held, FromBranch(), Candidate());

        await rig.RunAsync();
        await new MergeTrain().TickAsync(rig.Runtime, default);

        Assert.Equal(2, rig.H.Wire.Count("POST", CommentsPath("AER-1")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task No_pull_request_from_the_branch_or_more_than_one_writes_nothing(int count)
    {
        var held = new PullRequestHolding();
        using var rig = Repairing(held, null, Candidate());

        var url = (string n) => $"https://example.test/repo/pull/{n}";
        rig.H.Forge.PullRequestList = new(
            Enumerable.Range(0, count).ToDictionary(n => url((70 + n).ToString()), n => FromBranch(number: 70 + n)), null);

        await rig.RunAsync();

        Assert.Empty(RepairWrites(rig));
        Assert.Empty(rig.H.Say.Complained);
        Assert.Null(held.Owner);
    }

    [Theory]
    [InlineData(true, "main", RepairBranch)]
    [InlineData(false, "release", RepairBranch)]
    [InlineData(false, "main", "aer-1-a-thing-and-more")]
    [InlineData(false, "main", "AER-1-A-THING")]
    public async Task A_fork_or_a_pull_request_into_another_base_or_another_branch_is_not_matched(
        bool isFork, string baseBranch, string head)
    {
        using var rig = Repairing(new PullRequestHolding(), FromBranch(head, isFork, baseBranch), Candidate());

        await rig.RunAsync();

        Assert.Empty(RepairWrites(rig));
    }

    [Fact]
    public async Task A_pull_request_that_is_not_open_is_not_matched()
    {
        using var rig = Repairing(new PullRequestHolding(), FromBranch() with { State = PullRequestStates.Closed }, Candidate());

        await rig.RunAsync();

        Assert.Empty(RepairWrites(rig));
    }

    public static TheoryData<string> Untouched => new() { "merges", "trunk", "undriven", "non-driver", "no-code", "has-url", "landing-work" };

    [Theory]
    [MemberData(nameof(Untouched))]
    public async Task A_row_that_is_not_the_drivers_to_repair_is_never_touched(string which)
    {
        var held = new PullRequestHolding();
        var row = which switch
        {
            "merges" => Candidate(mergeWork: new MergeWorkDto(WorkKinds.Merge, "its branch holds main")),
            "trunk" => Candidate(landing: Landings.Trunk),
            "undriven" => Candidate(projectKey: "OTH"),
            "has-url" => Candidate(url: "https://example.test/repo/pull/99"),
            "landing-work" => Candidate() with { LandingWork = new LandingWorkDto("land", "lands on the trunk") },
            _ => Candidate(),
        };

        if (which == "no-code") held.NoCodeOn = "AER-1";

        using var rig = Repairing(held, FromBranch(), row);
        if (which == "undriven") rig.H.Runtime.Readout.SetDrives(["AER"]);
        if (which == "non-driver") rig.H.Runtime.Readout.SetDrives([]);

        await rig.RunAsync();

        Assert.Empty(RepairWrites(rig));
        Assert.Null(held.Owner);
    }

    [Fact]
    public async Task A_task_on_its_storys_shared_branch_is_never_written_to()
    {
        // The board leaves such a task out of the review list, so the story's row is all the runner sees.
        var held = new PullRequestHolding { Owner = "AER-2", Kind = "key" };
        using var rig = Repairing(held, FromBranch(), Candidate("AER-2", url: RepairUrl));

        await rig.RunAsync();

        Assert.Empty(RepairWrites(rig));
        Assert.Equal("AER-2", held.Owner);
    }

    [Fact]
    public async Task A_record_that_fails_after_the_clear_is_made_by_the_next_tick()
    {
        var held = new PullRequestHolding { Owner = "AER-2", Kind = "key", RefuseRecord = true };
        using var rig = Repairing(held, FromBranch(), Candidate());

        await rig.RunAsync();

        Assert.Null(held.Owner);
        Assert.Contains("could not record its pull request", string.Join(' ', rig.H.Say.Complained));
        Assert.Empty(rig.H.Wire.To("POST", CommentsPath("AER-1")));

        rig.Later();
        await rig.RunAsync();

        Assert.Equal("AER-1", held.Owner);
        Assert.Single(rig.H.Wire.To("POST", CommentsPath("AER-1")));
        Assert.Single(rig.H.Wire.To("PATCH", IssuePath("AER-2")));
    }

    [Fact]
    public async Task An_issue_that_declared_no_code_since_the_row_was_read_is_not_written_to()
    {
        var held = new PullRequestHolding { NoCodeOn = "AER-1" };
        using var rig = Repairing(held, FromBranch(), Candidate());

        await rig.RunAsync();

        Assert.Empty(RepairWrites(rig));
    }

    [Fact]
    public async Task The_filer_does_not_file_the_pull_request_as_an_outsider_afterwards()
    {
        // Nobody holds it, so without the repair the filer would take it for an outsider's.
        var held = new PullRequestHolding();
        using var rig = Repairing(held, FromBranch(), Candidate());
        rig.H.Wire.Serve("POST", Issues, _ => AsJson(Fixtures.Issue("AER-50", statusId: 1)));

        await rig.RunAsync();

        Assert.Equal("AER-1", held.Owner);
        Assert.Empty(rig.H.Wire.To("POST", Issues));
    }

    [Fact]
    public async Task The_shape_of_a_task_whose_pull_request_a_key_put_on_its_story_ends_on_the_task()
    {
        // A story whose tasks do not share a branch; the task is in review with a clean, green branch.
        var held = new PullRequestHolding { Owner = "AER-2", Kind = "key" };
        var green = Fixtures.Build(BuildVerdicts.Passed, Fixtures.MergeCanonical);
        using var rig = Repairing(held, FromBranch(), Candidate(builds: [green]));

        await rig.RunAsync();

        Assert.Equal("AER-1", held.Owner);
        Assert.Equal("", Assert.Single(rig.H.Wire.To("PATCH", IssuePath("AER-2"))).Read<IssuePatchRequest>().PullRequestUrl);
        Assert.Equal(RepairUrl, Assert.Single(rig.H.Wire.To("PATCH", IssuePath("AER-1"))).Read<IssuePatchRequest>().PullRequestUrl);
    }
}

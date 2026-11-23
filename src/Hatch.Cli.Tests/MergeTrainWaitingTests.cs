using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// The waiting pull request half of the merge train: a review row whose pull request was written and left
/// waiting is opened by the project's driver, as written, and recorded - or, where one is already open from
/// its branch, that one is recorded and nothing is opened.
/// </summary>
public sealed partial class MergeTrainTests
{
    private const string WaitingUrl = "https://example.test/repo/pull/61";
    private const string WaitingRemote = "https://example.test/repo.git";
    private static readonly string WaitingSha = new('c', 40);

    /// <summary>What the board holds on the one ticket these tests are about.</summary>
    private sealed class WaitingBoard
    {
        public string? Url { get; set; }
        public WaitingPullRequestDto? Waiting { get; set; } = Written();

        /// <summary>Whether the first write of the pull request is refused.</summary>
        public bool RefuseRecord { get; set; }

        /// <summary>Whether the first comment is refused.</summary>
        public bool RefuseComment { get; set; }
    }

    private static WaitingPullRequestDto Written(string? remote = WaitingRemote) => new(
        "AER-1 Area: what changed", "[AER-1](https://hatch.example.test/issues/AER-1)\n\nThe body.",
        RepairBranch, WaitingSha, remote, "a runner", "gh is not signed in", DateTimeOffset.UnixEpoch);

    /// <summary>A review row with no merge check, which a waiting ticket may well be.</summary>
    private static ReviewCheckDto Waits(
        string key = "AER-1", string landing = Landings.PullRequest, MergeWorkDto? mergeWork = null,
        WaitingPullRequestDto? waiting = null, string? projectKey = null) =>
        Candidate(key, landing: landing, mergeWork: mergeWork, projectKey: projectKey) with
        {
            MergeChecks = [],
            WaitingPullRequest = waiting ?? Written(),
        };

    private static Rig Waiting(WaitingBoard board, PullRequestAnswer? listed, params ReviewCheckDto[] rows)
    {
        var rig = new Rig().Board(rows);

        // The project names no trunk and neither does the runner, so it is whatever origin calls it.
        foreach (var checkout in rig.H.Runtime.Checkouts)
            rig.H.Workspace.HeadsFor[checkout.Path] = new RemoteHeads("main", new Dictionary<string, string>());
        var wire = rig.H.Wire;

        wire.Replace("GET", TrunkBuilds, HttpStatusCode.OK, AsJson(new[] { Fixtures.TrunkBuild() }));
        wire.Replace("GET", Projects, HttpStatusCode.OK, AsJson(new[] { AProject(1, "AER", Fixtures.MergeCanonical) }));
        wire.Replace("GET", Statuses, HttpStatusCode.OK, AsJson(new[]
        {
            Fixtures.Status(1, "Backlog"), Fixtures.Status(3, "In Progress"), Fixtures.Status(4, "In Review"),
            Fixtures.Status(5, "Done", terminal: true),
        }));

        wire.Serve("GET", Issues, call =>
            !call.Query.Contains("pullRequestUrl=", StringComparison.Ordinal) || board.Url is null
                ? "[]"
                : AsJson(new[] { HolderCard("AER-1", "key") }));

        wire.Serve("GET", IssuePath("AER-1"), _ =>
            AsJson(Fixtures.Issue("AER-1", statusId: 4, pullRequestUrl: board.Url) with { WaitingPullRequest = board.Waiting }));

        // The first write is refused, once, when a test says so: the rule is added to the wire ahead of the one below.
        if (board.RefuseRecord) wire.Once("PATCH", IssuePath("AER-1"), HttpStatusCode.Conflict, "refused");
        if (board.RefuseComment) wire.Once("POST", CommentsPath("AER-1"), HttpStatusCode.InternalServerError, "no");

        wire.Serve("PATCH", IssuePath("AER-1"), call =>
        {
            var patch = call.Read<IssuePatchRequest>();
            if (patch.PullRequestUrl is { Length: > 0 } url)
            {
                board.Url = url;
                board.Waiting = null;
            }

            return AsJson(Fixtures.Issue("AER-1", statusId: 4, pullRequestUrl: board.Url) with { WaitingPullRequest = board.Waiting });
        });

        wire.Json("POST", CommentsPath("AER-1"), Fixtures.Comment());

        rig.H.Forge.PullRequestList = listed is null
            ? new(new Dictionary<string, PullRequestAnswer>(), null)
            : new(new Dictionary<string, PullRequestAnswer> { [RepairUrl] = listed }, null);
        rig.H.Forge.OpenAnswer = new PullRequestOpenAnswer(WaitingUrl, null);
        return rig;
    }

    private static IReadOnlyList<Call> Patches(Rig rig) => rig.H.Wire.To("PATCH", IssuePath("AER-1"));

    private static IReadOnlyList<Call> Comments(Rig rig) => rig.H.Wire.To("POST", CommentsPath("AER-1"));

    [Fact]
    public async Task A_waiting_pull_request_is_opened_as_written_recorded_and_said_once()
    {
        var board = new WaitingBoard();
        var written = board.Waiting!;
        using var rig = Waiting(board, null, Waits());

        await rig.RunAsync();

        var opened = Assert.Single(rig.H.Forge.Opened);
        Assert.Equal(RepairBranch, opened.Head);
        Assert.Equal("main", opened.Trunk);
        Assert.Equal(written.Title, opened.Title);
        Assert.Equal(written.Body, opened.Body);

        Assert.Equal(WaitingUrl, Assert.Single(Patches(rig)).Read<IssuePatchRequest>().PullRequestUrl);
        Assert.Equal(WaitingUrl, board.Url);
        Assert.Null(board.Waiting);

        // The chain is written into the body the way `hatch pr` writes it.
        var edit = Assert.Single(rig.H.Forge.BodyEdits);
        Assert.Equal(WaitingUrl, edit.Url);
        Assert.Contains("AER-1", edit.Body);

        var said = Assert.Single(Comments(rig)).Read<CommentCreateRequest>().Body;
        Assert.Contains(WaitingUrl, said);
        Assert.Contains(rig.H.Runtime.RunnerName, said);
        Assert.Contains("a runner", said);
        Assert.Empty(rig.H.Say.Complained);

        // The next tick finds nothing waiting, and nothing more is written.
        rig.Later();
        await rig.RunAsync();

        Assert.Single(rig.H.Forge.Opened);
        Assert.Single(Patches(rig));
        Assert.Single(Comments(rig));
    }

    [Fact]
    public async Task A_trunk_nobody_named_is_the_one_origin_calls_its_trunk_and_where_origin_says_none_nothing_is_opened()
    {
        var board = new WaitingBoard();
        using var rig = Waiting(board, null, Waits());
        rig.H.Workspace.HeadsFor.Clear();

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.Opened);
        Assert.NotNull(board.Waiting);

        foreach (var checkout in rig.H.Runtime.Checkouts)
            rig.H.Workspace.HeadsFor[checkout.Path] = new RemoteHeads("trunky", new Dictionary<string, string>());
        rig.Later();
        await rig.RunAsync();

        Assert.Equal("trunky", Assert.Single(rig.H.Forge.Opened).Trunk);
    }

    [Fact]
    public async Task A_waiting_pull_request_with_no_stored_remote_is_opened_by_the_one_checkout()
    {
        var board = new WaitingBoard { Waiting = Written(remote: null) };
        using var rig = Waiting(board, null, Waits(waiting: board.Waiting));

        await rig.RunAsync();

        Assert.Single(rig.H.Forge.Opened);
        Assert.Equal(WaitingUrl, board.Url);
    }

    [Fact]
    public async Task A_pull_request_already_open_from_the_branch_is_recorded_and_none_is_opened()
    {
        var board = new WaitingBoard();
        using var rig = Waiting(board, FromBranch(), Waits());

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.Opened);
        Assert.Equal(RepairUrl, Assert.Single(Patches(rig)).Read<IssuePatchRequest>().PullRequestUrl);
        Assert.Equal(RepairUrl, board.Url);
        Assert.Null(board.Waiting);
        Assert.Contains(RepairUrl, Assert.Single(Comments(rig)).Read<CommentCreateRequest>().Body);
    }

    [Fact]
    public async Task A_runner_that_does_not_drive_the_project_opens_nothing()
    {
        var board = new WaitingBoard();
        using var rig = Waiting(board, null, Waits());
        rig.H.Runtime.Readout.SetDrives([]);

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.Opened);
        Assert.Empty(Patches(rig));
        Assert.Empty(Comments(rig));
        Assert.NotNull(board.Waiting);
    }

    public static TheoryData<string> LeftAlone => new() { "merges", "trunk", "another-project", "other-remote", "two-checkouts", "has-url", "landing-work" };

    [Theory]
    [MemberData(nameof(LeftAlone))]
    public async Task A_row_that_is_not_this_drivers_to_open_is_left_alone(string which)
    {
        var board = new WaitingBoard();
        var row = which switch
        {
            "merges" => Waits(mergeWork: new MergeWorkDto(WorkKinds.Merge, "its branch holds main")),
            "trunk" => Waits(landing: Landings.Trunk),
            "another-project" => Waits(projectKey: "OTH"),
            "other-remote" => Waits(waiting: Written(remote: "https://example.test/elsewhere.git")),
            "has-url" => Waits() with { PullRequestUrl = "https://example.test/repo/pull/99" },
            "landing-work" => Waits() with { LandingWork = new LandingWorkDto("land", "lands on the trunk") },
            _ => Waits() with
            {
                Repositories =
                [
                    Fixtures.Repository(WaitingRemote, Fixtures.MergeCanonical),
                    Fixtures.Repository(WaitingRemote, "example.test/another"),
                ],
            },
        };

        using var rig = Waiting(board, null, row);
        if (which == "another-project") rig.H.Runtime.Readout.SetDrives(["AER"]);

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.Opened);
        Assert.Empty(Patches(rig));
        Assert.Empty(Comments(rig));
        Assert.NotNull(board.Waiting);
    }

    [Theory]
    [InlineData(BranchHolding.Gone, "is gone from origin")]
    [InlineData(BranchHolding.Moved, "no longer holds")]
    public async Task A_branch_that_is_gone_or_moved_is_left_alone_and_said_once_per_run(BranchHolding holding, string said)
    {
        var board = new WaitingBoard();
        using var rig = Waiting(board, null, Waits());
        rig.H.Workspace.HoldingFor[RepairBranch] = holding;

        await rig.RunAsync();
        rig.Later();
        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.Opened);
        Assert.Empty(Patches(rig));
        Assert.NotNull(board.Waiting);

        var comment = Assert.Single(Comments(rig)).Read<CommentCreateRequest>().Body;
        Assert.Contains(said, comment);
        Assert.Contains(RepairBranch, comment);

        var lines = rig.H.Say.Complained.Where(l => l.Contains(said, StringComparison.Ordinal)).ToList();
        Assert.Single(lines);
    }

    [Fact]
    public async Task A_comment_that_fails_is_tried_again_on_the_next_tick()
    {
        var board = new WaitingBoard { RefuseComment = true };
        using var rig = Waiting(board, null, Waits());
        rig.H.Workspace.HoldingFor[RepairBranch] = BranchHolding.Gone;

        await rig.RunAsync();
        rig.Later();
        await rig.RunAsync();

        Assert.Equal(2, Comments(rig).Count);
    }

    [Fact]
    public async Task A_branch_origin_would_not_answer_for_is_not_said_on_the_ticket_and_is_tried_again()
    {
        var board = new WaitingBoard();
        using var rig = Waiting(board, null, Waits());
        rig.H.Workspace.HoldingFor[RepairBranch] = BranchHolding.Unknown;

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.Opened);
        Assert.Empty(Comments(rig));
        Assert.Contains("would not say", string.Join(' ', rig.H.Say.Complained));

        rig.H.Workspace.HoldingFor.Clear();
        rig.Later();
        await rig.RunAsync();

        Assert.Equal(WaitingUrl, board.Url);
    }

    [Fact]
    public async Task A_failed_open_leaves_the_text_and_the_next_tick_opens_it()
    {
        var board = new WaitingBoard();
        using var rig = Waiting(board, null, Waits());
        rig.H.Forge.OpenAnswer = new PullRequestOpenAnswer(null, "gh could not reach the forge\nsecond line");

        await rig.RunAsync();

        Assert.Single(rig.H.Forge.Opened);
        Assert.Empty(Patches(rig));
        Assert.Empty(Comments(rig));
        Assert.NotNull(board.Waiting);
        var line = Assert.Single(rig.H.Say.Complained);
        Assert.Equal("hatch: AER-1 - could not open its pull request - gh could not reach the forge second line", line);

        rig.H.Forge.OpenAnswer = new PullRequestOpenAnswer(WaitingUrl, null);
        rig.Later();
        await rig.RunAsync();

        Assert.Equal(2, rig.H.Forge.Opened.Count);
        Assert.Equal(WaitingUrl, board.Url);
        Assert.Null(board.Waiting);
    }

    [Fact]
    public async Task A_failed_record_leaves_the_text_and_the_next_tick_records_the_pull_request_that_was_opened()
    {
        var board = new WaitingBoard { RefuseRecord = true };
        using var rig = Waiting(board, null, Waits());

        await rig.RunAsync();

        Assert.Single(rig.H.Forge.Opened);
        Assert.NotNull(board.Waiting);
        Assert.Null(board.Url);
        Assert.Empty(Comments(rig));
        Assert.Contains("could not record it", string.Join(' ', rig.H.Say.Complained));

        // The pull request it opened is open now, and is listed.
        rig.H.Forge.PullRequestList = new(new Dictionary<string, PullRequestAnswer> { [WaitingUrl] = FromBranch(number: 61) }, null);
        rig.Later();
        await rig.RunAsync();

        Assert.Single(rig.H.Forge.Opened);
        Assert.Equal(WaitingUrl, board.Url);
        Assert.Null(board.Waiting);
        Assert.Single(Comments(rig));
    }

    [Fact]
    public async Task A_ticket_that_was_recorded_or_cleared_since_the_row_was_read_is_not_opened()
    {
        var board = new WaitingBoard();
        using var rig = Waiting(board, null, Waits());
        board.Waiting = null;

        await rig.RunAsync();

        Assert.Empty(rig.H.Forge.Opened);
        Assert.Empty(Patches(rig));
        Assert.Empty(Comments(rig));
    }

    [Fact]
    public async Task A_failure_to_write_the_envelope_is_one_line_and_the_pull_request_stays_recorded()
    {
        var board = new WaitingBoard();
        using var rig = Waiting(board, null, Waits());
        rig.H.Forge.EditAnswer = new BodyEditAnswer(false, "the body could not be written");

        await rig.RunAsync();

        Assert.Equal(WaitingUrl, board.Url);
        Assert.Single(Comments(rig));
        Assert.Contains("body was not updated with the envelope", Assert.Single(rig.H.Say.Complained));
    }

    [Fact]
    public async Task The_filer_does_not_file_a_pull_request_it_opened_as_an_outsider()
    {
        var board = new WaitingBoard();
        using var rig = Waiting(board, null, Waits());
        rig.H.Wire.Serve("POST", Issues, _ => AsJson(Fixtures.Issue("AER-50", statusId: 1)));

        await rig.RunAsync();

        Assert.Equal(WaitingUrl, board.Url);
        Assert.Empty(rig.H.Wire.To("POST", Issues));
    }
}

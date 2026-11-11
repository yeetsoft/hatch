using System.Net;
using System.Text.Json;

namespace Hatch.Cli.Tests;

/// <summary>
/// The outside pull request half of the merge train: an open pull request no issue records is filed in
/// review by the project's driver, once, or passed over with a reason.
/// </summary>
public sealed partial class MergeTrainTests
{
    private const string OutsideUrl = "https://example.test/repo/pull/50";
    private const string Issues = "/api/hatch/issues";
    private const string TrunkBuilds = "/api/hatch/trunk-builds";
    private const string Projects = "/api/hatch/projects";
    private const string Contribution = "/api/hatch/issues/AER-50/contribution";
    private const string Move = "/api/hatch/issues/AER-50/move";
    private const string FiledComments = "/api/hatch/issues/AER-50/comments";

    private static ProjectDto AProject(int id, string key, params string[] canonicals) =>
        new(id, key, key, 0, DateTimeOffset.UnixEpoch, null, null,
            [.. canonicals.Select(c => new ProjectRepositoryDto($"https://{c}.git", c, null))],
            null, [], false, false);

    /// <summary>A pull request the forge lists, opened an hour and a half before the rig's clock by default.</summary>
    private static PullRequestAnswer Outsider(
        long number = 50, string head = "feature/x", string login = "octocat", bool isFork = false,
        string merge = MergeStates.Clean, string baseBranch = "main", int minutesOld = 90, string? title = "Fix the thing",
        string? body = "What it does.\n\nWhy it does it.") =>
        new(PullRequestStates.Open, null, new string('b', 40), number, merge, ReviewDecisions.None, baseBranch, null,
            head, login, isFork, title, body, new DateTimeOffset(2026, 9, 27, 2, 0, 0, TimeSpan.Zero).AddMinutes(-minutesOld));

    private static string AsJson(object body) => JsonSerializer.Serialize(body, Fixtures.Json);

    /// <summary>
    /// A board that binds the checkout's repository to <c>AER</c> and holds nothing in review, with this runner
    /// driving <c>AER</c>, a forge that lists <paramref name="pr"/>, and a board that takes every write. The
    /// issue is not findable by its pull request until it has been created.
    /// </summary>
    private static Rig Outside(PullRequestAnswer pr, bool drives = true, string[]? projectKeys = null)
    {
        var rig = new Rig().Board();
        var wire = rig.H.Wire;
        var created = false;

        wire.Replace("GET", TrunkBuilds, HttpStatusCode.OK, AsJson(new[] { Fixtures.TrunkBuild() }));
        wire.Replace("GET", Projects, HttpStatusCode.OK, AsJson(new[] { AProject(1, "AER", Fixtures.MergeCanonical) }));
        wire.Replace("GET", Statuses, HttpStatusCode.OK, AsJson(new[]
        {
            Fixtures.Status(1, "Backlog"), Fixtures.Status(3, "In Progress"), Fixtures.Status(4, "In Review"),
            Fixtures.Status(5, "Done", terminal: true),
        }));
        wire.Serve("GET", Issues, _ => AsJson(created ? new[] { Fixtures.Card("AER-50") } : []));
        wire.Serve("POST", Issues, _ =>
        {
            created = true;
            return AsJson(Fixtures.Issue("AER-50", statusId: 1));
        });
        wire.Json("PUT", Contribution, Fixtures.Issue("AER-50", statusId: 1));
        wire.Json("POST", Move, Fixtures.Issue("AER-50", statusId: 4));
        wire.Json("POST", FiledComments, new CommentDto(1, "me", "x", "comment", null, null, DateTimeOffset.UnixEpoch));

        rig.H.Runtime.Readout.SetDrives(drives ? [.. projectKeys ?? ["AER"]] : []);
        rig.H.Forge.PullRequestList = new(new Dictionary<string, PullRequestAnswer> { [OutsideUrl] = pr }, null);
        return rig;
    }

    private static IssueCreateRequest Filed(Rig rig) =>
        Assert.Single(rig.H.Wire.To("POST", Issues)).Read<IssueCreateRequest>();

    private static ContributionRequest Recorded(Rig rig) =>
        Assert.Single(rig.H.Wire.To("PUT", Contribution)).Read<ContributionRequest>();

    [Fact]
    public async Task An_outside_pull_request_is_filed_as_a_task_in_review_with_its_record_in_that_order()
    {
        using var rig = Outside(Outsider());

        await rig.RunAsync();

        var order = rig.H.Wire.Calls.Where(c => c.Method != "GET").Select(c => c.Route).ToList();
        Assert.Equal([$"POST {Issues}", $"PUT {Contribution}", $"POST {Move}"], order);

        var issue = Filed(rig);
        Assert.Equal(1, issue.ProjectId);
        Assert.Equal("task", issue.Type);
        Assert.Equal("Fix the thing", issue.Title);
        Assert.Equal(new ContributionRequest(OutsideUrl, "feature/x", "octocat", "MEMBER"), Recorded(rig));
        Assert.Equal(4, Assert.Single(rig.H.Wire.To("POST", Move)).Read<IssueMoveRequest>().StatusId);
        Assert.Contains($"hatch: AER-50 filed from pull request #50 ({OutsideUrl}) -> In Review", rig.H.Say.Said);
    }

    [Fact]
    public async Task The_description_is_a_header_and_then_the_body_exactly_as_written()
    {
        var body = "# Heading\n\n  indented\r\n- list\n\n```\ncode\n```\n";
        using var rig = Outside(Outsider(body: body));

        await rig.RunAsync();

        Assert.Equal(
            $"Filed from pull request #50 ({OutsideUrl}) by @octocat (MEMBER), head branch `feature/x`, opened 2026-09-27 00:30 UTC.\n\n"
            + "Project: AER, because it is the primary repository of AER.\n\n" + body,
            Filed(rig).Description);
    }

    [Fact]
    public async Task A_version_zero_title_and_first_line_record_where_the_pull_request_came_from()
    {
        using var rig = Outside(Outsider(title: "HA-12 Something", body: "[HA-12](https://other.example/apps/hatch/issues/HA-12)\n\nrest"));

        await rig.RunAsync();

        var record = Recorded(rig);
        Assert.Equal("HA-12", record.OriginKey);
        Assert.Equal("HA", record.OriginProjectKey);
        Assert.NotNull(record.OriginInstance);
        Assert.Equal(0, record.EnvelopeVersion);
    }

    [Fact]
    public async Task The_association_the_forge_gave_is_recorded_as_it_gave_it()
    {
        using var rig = Outside(Outsider());
        rig.H.Forge.AssociationFor[50] = new AssociationAnswer("COLLABORATOR", null);

        await rig.RunAsync();

        Assert.Equal("COLLABORATOR", Recorded(rig).AuthorAssociation);
    }

    [Fact]
    public async Task Two_ticks_and_two_runners_file_a_pull_request_once()
    {
        using var rig = Outside(Outsider());
        var second = new MergeTrain();

        await rig.RunAsync();
        await rig.Later().RunAsync();
        await second.TickAsync(rig.Runtime, default);

        Assert.Single(rig.H.Wire.To("POST", Issues));
        Assert.Single(rig.H.Wire.To("PUT", Contribution));
        Assert.Single(rig.H.Wire.To("POST", Move));
        Assert.Empty(rig.H.Say.Complained);
    }

    [Fact]
    public async Task A_pull_request_a_review_row_already_records_is_not_looked_up_or_read_about()
    {
        using var rig = Outside(Outsider());
        rig.H.Wire.Replace("GET", Review, HttpStatusCode.OK, AsJson(new[] { Fixtures.Review("AER-7", pullRequestUrl: OutsideUrl + "/") }));

        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("POST", Issues));
        Assert.Empty(rig.H.Wire.To("GET", Issues));
        Assert.Empty(rig.H.Forge.AssociationReads);
    }

    [Fact]
    public async Task A_pull_request_some_issue_records_is_passed_over_without_a_word_even_when_it_looks_like_a_draft()
    {
        using var rig = Outside(Outsider(merge: MergeStates.Draft));
        rig.H.Wire.Replace("GET", Issues, HttpStatusCode.OK, AsJson(new[] { Fixtures.Card("AER-9") }));

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Empty(rig.H.Wire.To("POST", Issues));
        Assert.Empty(rig.H.Say.Said);
        Assert.Single(rig.H.Wire.To("GET", Issues));
    }

    [Fact]
    public async Task A_pull_request_younger_than_ten_minutes_is_passed_over_once_and_filed_once_it_is_older()
    {
        using var rig = Outside(Outsider(minutesOld: 5));

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Empty(rig.H.Wire.To("POST", Issues));
        Assert.Empty(rig.H.Forge.AssociationReads);
        Assert.Equal(
            [$"hatch: pull request #50 ({OutsideUrl}) is not filed - it was opened less than 10 minutes ago"],
            rig.H.Say.Said);

        await rig.Later(600).RunAsync();

        Assert.Single(rig.H.Wire.To("POST", Issues));
    }

    [Theory]
    [InlineData("branch", "its branch aer-12-thing is named for an issue on the board")]
    [InlineData("draft", "it is a draft")]
    [InlineData("fork", "it comes from a fork")]
    [InlineData("base", "it merges into release, not main")]
    [InlineData("stranger", "its author is none, not a member, owner or collaborator")]
    [InlineData("unread", "could not read who its author is - HTTP 403")]
    public async Task A_pull_request_that_is_not_to_be_filed_is_passed_over_once_with_its_reason(string kind, string reason)
    {
        var pr = kind switch
        {
            "branch" => Outsider(head: "aer-12-thing"),
            "draft" => Outsider(merge: MergeStates.Draft),
            "fork" => Outsider(isFork: true),
            "base" => Outsider(baseBranch: "release"),
            _ => Outsider(),
        };
        using var rig = Outside(pr);
        if (kind == "stranger") rig.H.Forge.AssociationAnswer = new AssociationAnswer("NONE", null);
        if (kind == "unread") rig.H.Forge.AssociationAnswer = new AssociationAnswer(null, "HTTP 403");

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Empty(rig.H.Wire.To("POST", Issues));
        Assert.Equal([$"hatch: pull request #50 ({OutsideUrl}) is not filed - {reason}"], rig.H.Say.Said);
    }

    [Fact]
    public async Task A_pull_request_the_forge_did_not_say_was_a_fork_about_is_passed_over_as_one()
    {
        using var rig = Outside(Outsider() with { IsCrossRepository = null });

        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("POST", Issues));
        Assert.EndsWith("it comes from a fork", Assert.Single(rig.H.Say.Said), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_branch_named_for_the_key_of_any_project_on_the_board_is_passed_over()
    {
        using var rig = Outside(Outsider(head: "bee-3-other"));
        rig.H.Wire.Replace("GET", Projects, HttpStatusCode.OK, AsJson(new[]
        {
            AProject(1, "AER", Fixtures.MergeCanonical), AProject(2, "BEE", "other.example/repo"),
        }));

        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("POST", Issues));
        Assert.Contains("bee-3-other is named for an issue", Assert.Single(rig.H.Say.Said), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stranger_is_asked_about_once_and_not_every_tick()
    {
        using var rig = Outside(Outsider());
        rig.H.Forge.AssociationAnswer = new AssociationAnswer("NONE", null);

        await rig.RunAsync();
        await rig.Later().RunAsync();
        await rig.Later().RunAsync();

        Assert.Single(rig.H.Forge.AssociationReads);
    }

    [Fact]
    public async Task A_reason_that_changes_is_said_again()
    {
        using var rig = Outside(Outsider(merge: MergeStates.Draft));

        await rig.RunAsync();
        rig.H.Forge.PullRequestList = new(new Dictionary<string, PullRequestAnswer> { [OutsideUrl] = Outsider(isFork: true) }, null);
        await rig.Later().RunAsync();

        Assert.Equal(2, rig.H.Say.Said.Count);
        Assert.EndsWith("it is a draft", rig.H.Say.Said[0], StringComparison.Ordinal);
        Assert.EndsWith("it comes from a fork", rig.H.Say.Said[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_runner_that_does_not_drive_the_project_files_nothing_and_reads_no_association()
    {
        using var rig = Outside(Outsider(), drives: false);

        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("POST", Issues));
        Assert.Empty(rig.H.Forge.AssociationReads);
        Assert.Empty(rig.H.Say.Said);
    }

    [Fact]
    public async Task A_runner_that_drives_another_project_files_nothing_and_reads_no_association()
    {
        using var rig = Outside(Outsider(), projectKeys: ["BEE"]);

        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("POST", Issues));
        Assert.Empty(rig.H.Forge.AssociationReads);
        Assert.Empty(rig.H.Forge.PullRequestListReads);
    }

    [Fact]
    public async Task A_repository_no_project_binds_and_one_the_board_has_not_reported_file_nothing_and_say_nothing()
    {
        using var unbound = Outside(Outsider());
        unbound.H.Wire.Replace("GET", Projects, HttpStatusCode.OK, "[]");
        using var unreported = Outside(Outsider());
        unreported.H.Wire.Replace("GET", TrunkBuilds, HttpStatusCode.OK, "[]");

        await unbound.RunAsync();
        await unreported.RunAsync();

        Assert.Empty(unbound.H.Wire.To("POST", Issues));
        Assert.Empty(unreported.H.Wire.To("POST", Issues));
        Assert.Empty(unbound.H.Say.Said);
        Assert.Empty(unreported.H.Say.Complained);
    }

    [Fact]
    public async Task Where_two_projects_bind_a_repository_the_one_it_is_primary_for_files_it_and_the_description_says_why()
    {
        using var rig = Outside(Outsider(), projectKeys: ["BEE"]);
        rig.H.Wire.Replace("GET", Projects, HttpStatusCode.OK, AsJson(new[]
        {
            AProject(1, "AER", "other.example/repo", Fixtures.MergeCanonical), AProject(2, "BEE", Fixtures.MergeCanonical),
        }));

        await rig.RunAsync();

        var issue = Filed(rig);
        Assert.Equal(2, issue.ProjectId);
        Assert.Contains("Project: BEE, because it is the primary repository of BEE, of the 2 projects that bind it.", issue.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Where_no_project_has_it_as_primary_the_lowest_id_wins_and_the_base_is_the_bindings_own()
    {
        var trunks = new[] { Fixtures.TrunkBuild() };
        var second = AProject(7, "SEC", "other.example/repo", Fixtures.MergeCanonical);
        var first = AProject(3, "FST", "other.example/repo", Fixtures.MergeCanonical.ToUpperInvariant());
        first = first with
        {
            Repositories = [first.Repositories[0], first.Repositories[1] with { BaseBranch = "develop" }],
        };

        var home = OutsidePullRequests.Resolve("https://example.test/repo.git", trunks, [second, first])!;

        Assert.Equal("FST", home.Project.Key);
        Assert.Equal("develop", home.BaseBranch);
        Assert.Equal("2 projects bind it, none as its primary repository, and FST has the lowest id", home.Why);
    }

    [Fact]
    public void The_base_is_the_trunk_the_board_reads_where_the_binding_names_none()
    {
        var home = OutsidePullRequests.Resolve(
            "https://example.test/repo.git", [Fixtures.TrunkBuild(trunk: "trunk")], [AProject(1, "AER", Fixtures.MergeCanonical)])!;

        Assert.Equal("trunk", home.BaseBranch);
    }

    [Fact]
    public async Task A_409_from_the_record_route_is_passed_over_with_its_reason_and_the_orphan_is_commented_on()
    {
        using var rig = Outside(Outsider());
        rig.H.Wire.Replace("PUT", Contribution, HttpStatusCode.Conflict, $"{OutsideUrl} is already the contribution of AER-3");

        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("POST", Move));
        Assert.Contains(
            $"hatch: pull request #50 ({OutsideUrl}) is not filed - the board already holds it - {OutsideUrl} is already the contribution of AER-3",
            rig.H.Say.Said);
        var comment = Assert.Single(rig.H.Wire.To("POST", FiledComments)).Read<CommentCreateRequest>().Body;
        Assert.Contains("Filed in error", comment, StringComparison.Ordinal);
        Assert.Contains("AER-3", comment, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_record_that_fails_is_resumed_on_the_next_tick_on_the_same_issue_and_never_filed_twice()
    {
        using var rig = Outside(Outsider());
        rig.H.Wire.Replace("PUT", Contribution, HttpStatusCode.InternalServerError, "boom");

        await rig.RunAsync();

        Assert.Single(rig.H.Wire.To("POST", Issues));
        Assert.Empty(rig.H.Wire.To("POST", Move));
        Assert.Single(rig.H.Say.Complained);

        rig.H.Wire.Replace("PUT", Contribution, HttpStatusCode.OK, AsJson(Fixtures.Issue("AER-50", statusId: 1)));
        await rig.Later().RunAsync();

        Assert.Single(rig.H.Wire.To("POST", Issues));
        Assert.Equal(2, rig.H.Wire.To("PUT", Contribution).Count);
        Assert.Single(rig.H.Wire.To("POST", Move));
        Assert.Contains($"hatch: AER-50 filed from pull request #50 ({OutsideUrl}) -> In Review", rig.H.Say.Said);
    }

    [Fact]
    public async Task A_move_that_fails_is_resumed_at_the_move_even_though_the_issue_now_records_the_pull_request()
    {
        using var rig = Outside(Outsider());
        rig.H.Wire.Replace("POST", Move, HttpStatusCode.Conflict, "the WIP limit is reached");

        await rig.RunAsync();
        rig.H.Wire.Replace("POST", Move, HttpStatusCode.OK, AsJson(Fixtures.Issue("AER-50", statusId: 4)));
        await rig.Later().RunAsync();

        Assert.Single(rig.H.Wire.To("POST", Issues));
        Assert.Single(rig.H.Wire.To("PUT", Contribution));
        Assert.Equal(2, rig.H.Wire.To("POST", Move).Count);
        Assert.Contains($"hatch: AER-50 filed from pull request #50 ({OutsideUrl}) -> In Review", rig.H.Say.Said);
    }

    [Fact]
    public async Task A_board_with_no_review_column_leaves_the_issue_where_it_was_filed_and_says_so()
    {
        using var rig = Outside(Outsider());
        rig.H.Wire.Replace("GET", Statuses, HttpStatusCode.OK, AsJson(new[] { Fixtures.Status(5, "Done", terminal: true) }));

        await rig.RunAsync();

        Assert.Empty(rig.H.Wire.To("POST", Move));
        Assert.Contains("no review column", Assert.Single(rig.H.Say.Complained), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_forge_that_cannot_list_complains_once_and_files_nothing()
    {
        using var rig = Outside(Outsider());
        rig.H.Forge.PullRequestList = new(null, "gh is not installed");

        await rig.RunAsync();
        await rig.Later().RunAsync();

        Assert.Equal(["hatch: could not read pull requests in checkout - gh is not installed"], rig.H.Say.Complained);
        Assert.Empty(rig.H.Wire.To("POST", Issues));
    }
}

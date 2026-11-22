namespace Hatch.Cli.Tests;

/// <summary>
/// <c>hatch pr KEY --title ... --body-file ...</c>, against a real git checkout: it opens the pull
/// request through the forge and records it, or leaves it waiting on the ticket and still exits 0.
/// </summary>
public sealed class PrOpenCommandTests : RepoFixture
{
    private const string Route = "/api/issues/AER-12";
    private const string Waiting = "/api/issues/AER-12/waiting-pull-request";
    private const string Opened = "https://forge.example.test/pull/1";
    private const string Title = "AER-12 Pr: open it from the command";
    private const string Link = "[AER-12](https://hatch.example/issues/AER-12)";

    private string _branch = "ha-12-open-it";

    /// <summary>A branch with one commit on it, pushed to origin unless said otherwise.</summary>
    private string Branch(bool push = true)
    {
        G(_work, "checkout", "--quiet", "-b", _branch);
        File.WriteAllText(Path.Combine(_work, "work.txt"), "work");
        Commit(_work, "The work");
        if (push) G(_work, "push", "--quiet", "origin", _branch);
        return G(_work, "rev-parse", "HEAD").Trim();
    }

    private string Body(string text)
    {
        var path = Path.Combine(_temp, "body.md");
        File.WriteAllText(path, text);
        return path;
    }

    private static ProjectDto AProject(string landing = Landings.PullRequest) =>
        new ProjectDto(1, "AER", "Aerie", 0, DateTimeOffset.UtcNow, null, null, [], null, [], false, false) with { Landing = landing };

    private static void Board(CliHarness h, IssueDto? issue = null, string landing = Landings.PullRequest)
    {
        h.Wire.Json("GET", Route, issue ?? Fixtures.Issue("AER-12"));
        h.Wire.Json("GET", "/api/projects", new[] { AProject(landing) });
        h.Wire.Json("GET", "/api/statuses", new[] { Fixtures.Status(3, "In Progress") });
        h.Wire.Json("PATCH", Route, Fixtures.Issue("AER-12", pullRequestUrl: Opened));
        h.Wire.Json("PUT", Waiting, Fixtures.Issue("AER-12"));
    }

    private Task<int> Run(CliHarness h, params string[] args) =>
        new IssueCommands(h.Cli).PrAsync([.. args, "--repo", _work], default);

    private static void NothingSent(CliHarness h)
    {
        Assert.Empty(h.Wire.To("PATCH", Route));
        Assert.Empty(h.Wire.To("PUT", Waiting));
        Assert.Empty(h.Forge.Opened);
    }

    [Fact]
    public async Task Opening_records_the_pull_request_and_writes_the_envelope()
    {
        using var h = new CliHarness();
        Board(h);
        Branch();

        var code = await Run(h, "AER-12", "--title", Title, "--body-file", Body("The summary.\n"));

        Assert.Equal(0, code);
        var open = Assert.Single(h.Forge.Opened);
        Assert.Equal(_branch, open.Head);
        Assert.Equal("main", open.Trunk);
        Assert.Equal(Title, open.Title);
        Assert.Equal(Link + "\n\nThe summary.\n", open.Body);
        Assert.Equal(_work, open.Path);

        Assert.Equal(Opened, Assert.Single(h.Wire.To("PATCH", Route)).Read<IssuePatchRequest>().PullRequestUrl);
        Assert.Single(h.Forge.BodyEdits);
        Assert.Empty(h.Wire.To("PUT", Waiting));
        Assert.Equal(Opened, h.Said);
    }

    [Fact]
    public async Task A_forge_that_cannot_open_it_stores_the_text_sha_and_branch_and_exits_zero()
    {
        using var h = new CliHarness();
        Board(h);
        var sha = Branch();
        h.Forge.OpenAnswer = new PullRequestOpenAnswer(null, "gh is not signed in");

        var code = await Run(h, "AER-12", "--title", Title, "--body-file", Body("The summary.\n"));

        Assert.Equal(0, code);
        var put = Assert.Single(h.Wire.To("PUT", Waiting)).Read<WaitingPullRequestRequest>();
        Assert.Equal(Title, put.Title);
        Assert.Equal(Link + "\n\nThe summary.\n", put.Body);
        Assert.Equal(_branch, put.Branch);
        Assert.Equal(sha, put.Sha);
        Assert.Equal(_origin, put.Remote);
        Assert.Equal("gh is not signed in", put.Why);
        Assert.Empty(h.Wire.To("PATCH", Route));
        Assert.Contains("AER-12: the pull request is written and waits for a runner that can open it (gh is not signed in)", h.Said);
    }

    [Fact]
    public async Task A_reason_the_forge_did_not_give_is_filled_in_and_a_long_one_is_cut()
    {
        using var h = new CliHarness();
        Board(h);
        Branch();
        h.Forge.OpenAnswer = new PullRequestOpenAnswer(null, null);

        await Run(h, "AER-12", "--title", Title, "--body-file", Body("x"));
        Assert.Equal("the pull request could not be opened", Assert.Single(h.Wire.To("PUT", Waiting)).Read<WaitingPullRequestRequest>().Why);

        h.Forge.OpenAnswer = new PullRequestOpenAnswer(null, new string('w', 800));
        await Run(h, "AER-12", "--title", Title, "--body-file", Body("x"));
        Assert.Equal(500, h.Wire.To("PUT", Waiting).Last().Read<WaitingPullRequestRequest>().Why!.Length);
    }

    [Fact]
    public async Task A_pull_request_the_forge_says_already_exists_is_recorded_and_none_is_left_waiting()
    {
        using var h = new CliHarness();
        Board(h);
        Branch();
        h.Forge.OpenAnswer = new PullRequestOpenAnswer("https://forge.example.test/pull/7", null);

        Assert.Equal(0, await Run(h, "AER-12", "--title", Title, "--body-file", Body("x")));

        Assert.Single(h.Wire.To("PATCH", Route));
        Assert.Empty(h.Wire.To("PUT", Waiting));
    }

    [Theory]
    [InlineData("Pr: no key in front")]
    [InlineData("AER-12")]
    [InlineData("AER-12 ")]
    [InlineData("AER-120 Pr: another key")]
    public async Task A_title_that_is_not_the_key_and_a_subject_is_refused(string title)
    {
        using var h = new CliHarness();
        Board(h);
        Branch();

        Assert.Equal(1, await Run(h, "AER-12", "--title", title, "--body-file", Body("x")));

        Assert.Contains("starts with the key", h.Complained);
        NothingSent(h);
    }

    [Fact]
    public async Task A_title_over_the_limit_is_refused()
    {
        using var h = new CliHarness();
        Board(h);
        Branch();

        Assert.Equal(1, await Run(h, "AER-12", "--title", "AER-12 " + new string('t', 300), "--body-file", Body("x")));

        Assert.Contains("the limit is 256", h.Complained);
        NothingSent(h);
    }

    [Fact]
    public async Task A_body_over_the_limit_is_refused()
    {
        using var h = new CliHarness();
        Board(h);
        Branch();

        Assert.Equal(1, await Run(h, "AER-12", "--title", Title, "--body-file", Body(new string('b', PullRequestEnvelopeWriter.GitHubBodyLimit))));

        Assert.Contains("the limit is", h.Complained);
        NothingSent(h);
    }

    [Fact]
    public async Task A_body_that_already_opens_with_the_link_is_not_given_a_second()
    {
        using var h = new CliHarness();
        Board(h);
        Branch();
        var body = Link + "\n\nThe summary.\n";

        await Run(h, "AER-12", "--title", Title, "--body-file", Body(body));

        Assert.Equal(body, Assert.Single(h.Forge.Opened).Body);
    }

    [Fact]
    public async Task A_body_of_any_text_gets_the_link_put_in_front()
    {
        using var h = new CliHarness();
        Board(h);
        Branch();

        await Run(h, "AER-12", "--title", Title, "--body-file", Body("A line.\r\nThe link is [AER-12](x) in the middle.\n"));

        Assert.Equal(Link + "\n\nA line.\r\nThe link is [AER-12](x) in the middle.\n", Assert.Single(h.Forge.Opened).Body);
    }

    [Fact]
    public async Task A_dash_reads_the_body_from_standard_input()
    {
        using var h = new CliHarness(replies: ["From stdin.", "Second line."]);
        Board(h);
        Branch();

        Assert.Equal(0, await Run(h, "AER-12", "--title", Title, "--body-file", "-"));

        Assert.StartsWith(Link + "\n\nFrom stdin.\nSecond line.", Assert.Single(h.Forge.Opened).Body);
    }

    [Fact]
    public async Task A_body_file_that_is_not_there_is_refused()
    {
        using var h = new CliHarness();
        Board(h);
        Branch();

        Assert.Equal(1, await Run(h, "AER-12", "--title", Title, "--body-file", Path.Combine(_temp, "nope.md")));

        Assert.Contains("the body could not be read", h.Complained);
        NothingSent(h);
    }

    [Fact]
    public async Task A_trunk_landing_project_has_no_pull_request_to_open()
    {
        using var h = new CliHarness();
        Board(h, landing: Landings.Trunk);
        Branch();

        Assert.Equal(1, await Run(h, "AER-12", "--title", Title, "--body-file", Body("x")));

        Assert.Contains("This project lands on the trunk, so there is no pull request to open for AER-12.", h.Complained);
        NothingSent(h);
    }

    [Fact]
    public async Task A_task_on_its_storys_branch_is_refused_naming_the_story()
    {
        using var h = new CliHarness();
        Board(h, Fixtures.Issue("AER-12", parentKey: "AER-11") with { OnePr = true });
        h.Wire.Json("GET", "/api/issues/AER-11", Fixtures.Issue("AER-11", "story", parentKey: "AER-10"));
        Branch();

        Assert.Equal(1, await Run(h, "AER-12", "--title", Title, "--body-file", Body("x")));

        Assert.Contains("AER-11's branch", h.Complained);
        Assert.Contains("one pull request", h.Complained);
        NothingSent(h);
    }

    [Fact]
    public async Task A_ticket_with_a_pull_request_recorded_is_refused_until_it_is_cleared()
    {
        using var h = new CliHarness();
        Board(h, Fixtures.Issue("AER-12", pullRequestUrl: "https://forge.example.test/pull/5"));
        Branch();

        Assert.Equal(1, await Run(h, "AER-12", "--title", Title, "--body-file", Body("x")));

        Assert.Contains("AER-12 already points at https://forge.example.test/pull/5 - hatch pr AER-12 --clear first", h.Complained);
        NothingSent(h);
    }

    [Fact]
    public async Task A_branch_origin_does_not_hold_is_refused_before_the_forge_is_asked()
    {
        using var h = new CliHarness();
        Board(h);
        Branch(push: false);

        Assert.Equal(1, await Run(h, "AER-12", "--title", Title, "--body-file", Body("x")));

        Assert.Contains($"push {_branch} first", h.Complained);
        NothingSent(h);
    }

    [Fact]
    public async Task A_branch_origin_holds_at_an_older_commit_is_refused_too()
    {
        using var h = new CliHarness();
        Board(h);
        Branch();
        File.WriteAllText(Path.Combine(_work, "more.txt"), "more");
        Commit(_work, "Not pushed");

        Assert.Equal(1, await Run(h, "AER-12", "--title", Title, "--body-file", Body("x")));

        Assert.Contains($"push {_branch} first", h.Complained);
        NothingSent(h);
    }

    [Fact]
    public async Task The_trunk_is_refused()
    {
        using var h = new CliHarness();
        Board(h);

        Assert.Equal(1, await Run(h, "AER-12", "--title", Title, "--body-file", Body("x")));

        Assert.Contains("the trunk", h.Complained);
        NothingSent(h);
    }

    [Fact]
    public async Task A_detached_head_is_refused()
    {
        using var h = new CliHarness();
        Board(h);
        G(_work, "checkout", "--quiet", "--detach");

        Assert.Equal(1, await Run(h, "AER-12", "--title", Title, "--body-file", Body("x")));

        Assert.Contains("detached", h.Complained);
        NothingSent(h);
    }

    [Theory]
    [InlineData("https://forge.example.test/pull/5", "--title", Title, "--body-file", "-")]
    [InlineData("--clear", "--title", Title, "--body-file", "-")]
    [InlineData("--none", "why", "--title", Title, "--body-file", "-")]
    public async Task The_new_form_does_not_mix_with_a_url_clear_or_none(params string[] rest)
    {
        using var h = new CliHarness();
        Board(h);
        Branch();

        var code = await new IssueCommands(h.Cli).PrAsync(["AER-12", .. rest, "--repo", _work], default);

        Assert.Equal(1, code);
        NothingSent(h);
        Assert.Empty(h.Wire.To("GET", Route));
    }

    [Theory]
    [InlineData("--title")]
    [InlineData("--body-file")]
    public async Task One_half_of_the_new_form_alone_is_a_usage_refusal(string flag)
    {
        using var h = new CliHarness();
        Board(h);
        Branch();

        var code = await new IssueCommands(h.Cli).PrAsync(["AER-12", flag, flag == "--title" ? Title : "-", "--repo", _work], default);

        Assert.Equal(1, code);
        NothingSent(h);
    }

    [Fact]
    public async Task The_old_forms_behave_as_before()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", Route, Fixtures.Issue("AER-12", pullRequestUrl: Opened));
        h.Wire.Json("PUT", "/api/issues/AER-12/no-code", Fixtures.Issue("AER-12") with { NoCode = "a finding" });

        Assert.Equal(0, await new IssueCommands(h.Cli).PrAsync(["AER-12"], default));
        Assert.Equal(Opened, h.Said);

        // A reason is free text: one that reads like a flag is still a reason.
        Assert.Equal(0, await new IssueCommands(h.Cli).PrAsync(["AER-12", "--none", "--title"], default));
        Assert.Equal("--title", Assert.Single(h.Wire.To("PUT", "/api/issues/AER-12/no-code")).Read<NoCodeRequest>().Reason);

        Assert.Equal(1, await new IssueCommands(h.Cli).PrAsync(["AER-12", "a", "b", "c"], default));
        NothingSent(h);
    }
}

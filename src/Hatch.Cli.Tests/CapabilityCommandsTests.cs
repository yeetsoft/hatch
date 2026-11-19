using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>capability</c> and <c>serves</c>: an ambition's capabilities, the verdicts
/// on them, and the epics that serve them.
/// </summary>
public sealed class CapabilityCommandsTests : RepoFixture
{
    private const string Caps = "/api/issues/AER-1/capabilities";

    private static CapabilityDto Cap(
        long id, int position, string statement = "It works", string how = "Run it",
        CapabilityVerdictDto? verdict = null, string[]? servedBy = null) =>
        new(id, position, statement, how, "somebody", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            verdict?.Verdict ?? "not_checked", verdict, servedBy ?? []);

    private static CapabilityVerdictDto Verdict(string word = "holds", string sha = "0123456789abcdef0123456789abcdef01234567") =>
        new(1, 7, word, "Ran the suite\nand read the page", sha, "Zimm-runner",
            new DateTimeOffset(2026, 11, 19, 23, 30, 0, TimeSpan.Zero));

    private static ServedCapabilityDto Served(string ambition, long id, string state = "not_checked", string statement = "It works") =>
        new(ambition, id, 1, statement, state);

    private static Task<int> CapabilityIn(CliHarness h, string directory, params string[] args) =>
        new CapabilityCommands(h.Cli, directory).CapabilityAsync(args, default);

    private static Task<int> Capability(CliHarness h, params string[] args) =>
        new CapabilityCommands(h.Cli).CapabilityAsync(args, default);

    private static Task<int> Serves(CliHarness h, params string[] args) =>
        new CapabilityCommands(h.Cli).ServesAsync(args, default);

    // ---- list ----

    [Fact]
    public async Task List_prints_each_capability_with_its_verdict_and_its_epics()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", Caps, new[]
        {
            Cap(7, 1, "It starts", "Start it", Verdict(), ["AER-5", "AER-6"]),
            Cap(9, 2, "It stops", "Stop it"),
        });

        Assert.Equal(0, await Capability(h, "list", "AER-1"));

        Assert.Equal(
            """
            1  #7  holds  It starts
                how to tell: Start it
                verdict: holds at 0123456 by Zimm-runner, 2026-11-19 - Ran the suite and read the page
                served by: AER-5, AER-6
            2  #9  not_checked  It stops
                how to tell: Stop it
                verdict: none yet
                served by: no epic
            """.ReplaceLineEndings("\n"),
            h.Said);
    }

    [Fact]
    public async Task List_of_an_ambition_with_none_says_so()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", Caps, Array.Empty<CapabilityDto>());

        Assert.Equal(0, await Capability(h, "list", "AER-1"));
        Assert.Equal("AER-1 has no capabilities", h.Said);
    }

    // ---- add, edit, remove ----

    [Fact]
    public async Task Add_sends_the_statement_and_the_how_and_prints_the_line()
    {
        using var h = new CliHarness();
        h.Wire.Reply("POST", Caps, HttpStatusCode.Created,
            System.Text.Json.JsonSerializer.Serialize(Cap(7, 3, "It starts"), Fixtures.Json));

        Assert.Equal(0, await Capability(h, "add", "AER-1", "It starts", "--how", "Start it"));

        var sent = Assert.Single(h.Wire.To("POST", Caps)).Read<CapabilityCreateRequest>();
        Assert.Equal("It starts", sent.Statement);
        Assert.Equal("Start it", sent.HowToTell);
        Assert.Null(sent.Position);
        Assert.Equal("3  #7  not_checked  It starts", h.Said);
    }

    [Fact]
    public async Task Add_at_a_position_sends_it()
    {
        using var h = new CliHarness();
        h.Wire.Json("POST", Caps, Cap(7, 1));

        Assert.Equal(0, await Capability(h, "add", "AER-1", "It starts", "--at", "1", "--how", "Start it"));
        Assert.Equal(1, Assert.Single(h.Wire.To("POST", Caps)).Read<CapabilityCreateRequest>().Position);
    }

    [Fact]
    public async Task Edit_sends_only_the_field_it_was_given()
    {
        using var h = new CliHarness();
        h.Wire.Json("PATCH", $"{Caps}/7", Cap(7, 1, "Better"));

        Assert.Equal(0, await Capability(h, "edit", "AER-1", "7", "--statement", "Better"));

        var body = Assert.Single(h.Wire.To("PATCH", $"{Caps}/7")).Body;
        Assert.Contains("\"statement\":\"Better\"", body);
        Assert.DoesNotContain("howToTell", body);
        Assert.Equal("1  #7  not_checked  Better", h.Said);
    }

    [Fact]
    public async Task Edit_of_the_how_sends_only_the_how()
    {
        using var h = new CliHarness();
        h.Wire.Json("PATCH", $"{Caps}/7", Cap(7, 1));

        Assert.Equal(0, await Capability(h, "edit", "AER-1", "7", "--how", "Look harder"));

        var body = Assert.Single(h.Wire.To("PATCH", $"{Caps}/7")).Body;
        Assert.Contains("\"howToTell\":\"Look harder\"", body);
        Assert.DoesNotContain("\"statement\"", body);
    }

    [Fact]
    public async Task Remove_deletes_by_id_and_says_so()
    {
        using var h = new CliHarness();
        h.Wire.Reply("DELETE", $"{Caps}/7", HttpStatusCode.NoContent);

        Assert.Equal(0, await Capability(h, "remove", "AER-1", "7"));

        Assert.Single(h.Wire.To("DELETE", $"{Caps}/7"));
        Assert.Equal("AER-1: removed capability 7", h.Said);
    }

    // ---- move ----

    [Fact]
    public async Task Move_sends_the_whole_order_with_the_id_at_its_new_place()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", Caps, new[] { Cap(7, 1), Cap(8, 2), Cap(9, 3) });
        h.Wire.Json("PUT", $"{Caps}/order", new[] { Cap(9, 1), Cap(7, 2), Cap(8, 3) });

        Assert.Equal(0, await Capability(h, "move", "AER-1", "9", "1"));

        var order = Assert.Single(h.Wire.To("PUT", $"{Caps}/order")).Read<CapabilityOrderRequest>();
        Assert.Equal([9L, 7L, 8L], order.Ids);
        Assert.Equal("AER-1: capability 9 now at position 1", h.Said);
    }

    [Fact]
    public async Task Move_to_the_last_position_is_allowed()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", Caps, new[] { Cap(7, 1), Cap(8, 2), Cap(9, 3) });
        h.Wire.Json("PUT", $"{Caps}/order", Array.Empty<CapabilityDto>());

        Assert.Equal(0, await Capability(h, "move", "AER-1", "7", "3"));
        Assert.Equal([8L, 9L, 7L], Assert.Single(h.Wire.To("PUT", $"{Caps}/order")).Read<CapabilityOrderRequest>().Ids);
    }

    [Fact]
    public async Task Move_past_the_last_position_is_refused_with_no_put()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", Caps, new[] { Cap(7, 1), Cap(8, 2) });

        Assert.Equal(1, await Capability(h, "move", "AER-1", "7", "3"));

        Assert.Contains("AER-1 has 2 capabilities - there is no position 3", h.Complained);
        Assert.Empty(h.Wire.To("PUT", $"{Caps}/order"));
    }

    [Fact]
    public async Task Move_of_an_id_that_is_not_on_the_ambition_is_refused_with_no_put()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", Caps, new[] { Cap(7, 1), Cap(8, 2) });

        Assert.Equal(1, await Capability(h, "move", "AER-1", "99", "1"));

        Assert.Contains("capability 99 is not on AER-1", h.Complained);
        Assert.Empty(h.Wire.To("PUT", $"{Caps}/order"));
    }

    [Fact]
    public async Task Move_to_a_position_under_one_is_refused_before_any_request()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await Capability(h, "move", "AER-1", "7", "0"));
        Assert.Empty(h.Wire.Calls);
    }

    // ---- judge ----

    [Fact]
    public async Task Judge_with_a_sha_sends_it_as_typed_and_needs_no_git()
    {
        using var h = new CliHarness();
        h.Wire.Json("POST", $"{Caps}/7/verdicts", Verdict("does_not_hold", "abc1234def"));
        var nowhere = Directory.CreateTempSubdirectory("hatch-nowhere-").FullName;

        Assert.Equal(0, await CapabilityIn(h, nowhere, "judge", "AER-1", "7", "does_not_hold",
            "--looked-at", "Ran it", "--sha", "abc1234def"));

        var sent = Assert.Single(h.Wire.To("POST", $"{Caps}/7/verdicts")).Read<CapabilityVerdictRequest>();
        Assert.Equal(new CapabilityVerdictRequest("does_not_hold", "Ran it", "abc1234def"), sent);
        Assert.Equal("AER-1: capability 7 judged does_not_hold at abc1234", h.Said);
    }

    [Fact]
    public async Task Judge_without_a_sha_reads_the_head_of_the_checkout_it_stands_in()
    {
        using var h = new CliHarness();
        var head = G(_work, "rev-parse", "HEAD").Trim();
        h.Wire.Json("POST", $"{Caps}/7/verdicts", Verdict("holds", head));

        Assert.Equal(0, await CapabilityIn(h, _work, "judge", "AER-1", "7", "holds", "--looked-at", "Ran it"));

        var sent = Assert.Single(h.Wire.To("POST", $"{Caps}/7/verdicts")).Read<CapabilityVerdictRequest>();
        Assert.Equal(head, sent.Sha);
        Assert.Equal(40, sent.Sha.Length);
        Assert.Equal($"AER-1: capability 7 judged holds at {head[..7]}", h.Said);
    }

    [Fact]
    public async Task Judge_outside_a_checkout_without_a_sha_is_refused_and_sends_nothing()
    {
        using var h = new CliHarness();
        var nowhere = Directory.CreateTempSubdirectory("hatch-nowhere-").FullName;

        Assert.Equal(1, await CapabilityIn(h, nowhere, "judge", "AER-1", "7", "holds", "--looked-at", "Ran it"));

        Assert.Contains($"{nowhere} is not inside a git checkout - name the commit with --sha", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Judge_in_a_checkout_with_no_commit_is_refused_and_sends_nothing()
    {
        using var h = new CliHarness();
        var empty = Directory.CreateTempSubdirectory("hatch-unborn-").FullName;
        G(empty, "init", "--quiet", "-b", "main");

        Assert.Equal(1, await CapabilityIn(h, empty, "judge", "AER-1", "7", "holds", "--looked-at", "Ran it"));

        Assert.Contains("HEAD does not resolve to a commit here", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Judge_with_a_word_that_is_not_a_verdict_is_refused_before_any_request()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await Capability(h, "judge", "AER-1", "7", "pass", "--looked-at", "Ran it", "--sha", "abc"));
        Assert.Contains("pass is not a verdict", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    // ---- refusals before any request ----

    [Theory]
    [InlineData("")]
    [InlineData("list")]
    [InlineData("frobnicate", "AER-1")]
    [InlineData("list", "AER-1", "AER-2")]
    [InlineData("list", "--what")]
    [InlineData("add", "AER-1", "It starts")]
    [InlineData("add", "AER-1", "--how", "Start it")]
    [InlineData("add", "AER-1", "It starts", "--how")]
    [InlineData("add", "AER-1", "It starts", "--how", "")]
    [InlineData("add", "AER-1", "It starts", "--how", "x", "--at", "0")]
    [InlineData("add", "AER-1", "It starts", "--how", "x", "--at", "two")]
    [InlineData("add", "AER-1", "It starts", "--how", "x", "--how", "y")]
    [InlineData("add", "AER-1", "It starts", "--how", "x", "--nope", "y")]
    [InlineData("edit", "AER-1", "7")]
    [InlineData("edit", "AER-1", "seven", "--how", "x")]
    [InlineData("edit", "AER-1", "0", "--how", "x")]
    [InlineData("edit", "AER-1", "7", "--statement", "")]
    [InlineData("move", "AER-1", "7")]
    [InlineData("move", "AER-1", "seven", "1")]
    [InlineData("move", "AER-1", "7", "first")]
    [InlineData("remove", "AER-1")]
    [InlineData("remove", "AER-1", "seven")]
    [InlineData("judge", "AER-1", "7", "holds")]
    [InlineData("judge", "AER-1", "seven", "holds", "--looked-at", "x")]
    [InlineData("judge", "AER-1", "7", "--looked-at", "x")]
    public async Task A_usage_mistake_is_refused_and_sends_nothing(params string[] args)
    {
        using var h = new CliHarness();
        var argv = args is [""] ? [] : args;

        Assert.Equal(1, await Capability(h, argv));

        Assert.StartsWith("hatch: ", h.Complained);
        Assert.Contains("usage: hatch capability list", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("add", "--help")]
    [InlineData("judge", "-h")]
    public async Task Help_prints_the_usage_on_standard_output(params string[] args)
    {
        using var h = new CliHarness();

        Assert.Equal(0, await Capability(h, args));

        Assert.Contains("usage: hatch capability list", h.Said);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_server_refusal_surfaces_as_it_came()
    {
        using var h = new CliHarness();
        h.Wire.Reply("POST", Caps, HttpStatusCode.BadRequest, "\"the statement may not be blank\"");
        h.Wire.Reply("PATCH", $"{Caps}/7", HttpStatusCode.Forbidden, "\"AER-1 is past planning\"");

        var bad = await Assert.ThrowsAsync<HatchException>(() =>
            Capability(h, "add", "AER-1", "x", "--how", "y"));
        Assert.Contains("400", bad.Message);
        Assert.Contains("the statement may not be blank", bad.Message);

        var refused = await Assert.ThrowsAsync<HatchException>(() =>
            Capability(h, "edit", "AER-1", "7", "--how", "y"));
        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Contains("AER-1 is past planning", refused.Message);
    }

    // ---- serves ----

    [Fact]
    public async Task Serves_with_only_an_epic_reads_the_set()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/issues/AER-5/serves",
            new[] { Served("AER-1", 7, "holds", "It starts"), Served("AER-1", 9) });

        Assert.Equal(0, await Serves(h, "AER-5"));

        Assert.Equal(
            """
            AER-5 serves 2 capabilities:
              AER-1 #7  holds  It starts
              AER-1 #9  not_checked  It works
            """.ReplaceLineEndings("\n"),
            h.Said);
        Assert.Empty(h.Wire.To("PUT", "/api/issues/AER-5/serves"));
    }

    [Fact]
    public async Task Serves_of_nothing_says_so()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/issues/AER-5/serves", Array.Empty<ServedCapabilityDto>());

        Assert.Equal(0, await Serves(h, "AER-5"));
        Assert.Equal("AER-5 serves nothing", h.Said);
    }

    [Fact]
    public async Task Serves_with_ids_replaces_the_set()
    {
        using var h = new CliHarness();
        h.Wire.Json("PUT", "/api/issues/AER-5/serves", new[] { Served("AER-1", 7) });

        Assert.Equal(0, await Serves(h, "AER-5", "7", "9"));

        var sent = Assert.Single(h.Wire.To("PUT", "/api/issues/AER-5/serves")).Read<IssueServesWriteRequest>();
        Assert.Equal([7L, 9L], sent.CapabilityIds);
        Assert.StartsWith("AER-5 serves 1 capability:", h.Said);
    }

    [Fact]
    public async Task Serves_none_sends_an_empty_set()
    {
        using var h = new CliHarness();
        h.Wire.Json("PUT", "/api/issues/AER-5/serves", Array.Empty<ServedCapabilityDto>());

        Assert.Equal(0, await Serves(h, "AER-5", "--none"));

        Assert.Empty(Assert.Single(h.Wire.To("PUT", "/api/issues/AER-5/serves")).Read<IssueServesWriteRequest>().CapabilityIds);
        Assert.Equal("AER-5 serves nothing", h.Said);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AER-5", "7", "--none")]
    [InlineData("AER-5", "seven")]
    [InlineData("AER-5", "0")]
    [InlineData("AER-5", "--all")]
    [InlineData("--none")]
    public async Task A_serves_mistake_is_refused_and_sends_nothing(params string[] args)
    {
        using var h = new CliHarness();

        Assert.Equal(1, await Serves(h, args is [""] ? [] : args));

        Assert.StartsWith("hatch: ", h.Complained);
        Assert.Contains("usage: hatch serves <epic>", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Serves_help_prints_the_usage()
    {
        using var h = new CliHarness();

        Assert.Equal(0, await Serves(h, "--help"));
        Assert.Contains("usage: hatch serves <epic>", h.Said);
    }

    [Fact]
    public async Task A_serves_refusal_surfaces_as_it_came()
    {
        using var h = new CliHarness();
        h.Wire.Reply("PUT", "/api/issues/AER-5/serves", HttpStatusCode.BadRequest, "\"capability 99 does not exist\"");

        var bad = await Assert.ThrowsAsync<HatchException>(() => Serves(h, "AER-5", "99"));
        Assert.Contains("capability 99 does not exist", bad.Message);
    }

    // ---- show ----

    [Fact]
    public async Task Show_of_an_ambition_prints_its_capabilities_after_the_children_line()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/issues/AER-1", Fixtures.Issue("AER-1", "ambition") with { ChildKeys = ["AER-5"] });
        h.Wire.Json("GET", "/api/statuses", new[] { Fixtures.Status(3, "To Do") });
        h.Wire.Json("GET", "/api/issues/AER-1/comments", Array.Empty<CommentDto>());
        h.Wire.Json("GET", Caps, new[] { Cap(7, 1, "It starts", verdict: Verdict()), Cap(9, 2, "It stops") });

        Assert.Equal(0, await new IssueCommands(h.Cli).ShowAsync(["AER-1"], default));

        Assert.Contains(
            "children: AER-5\ncapabilities:\n  1  #7  holds  It starts\n  2  #9  not_checked  It stops\n",
            h.Said);
    }

    [Fact]
    public async Task Show_of_an_epic_prints_a_serves_line()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/issues/AER-5", Fixtures.Issue("AER-5", "epic"));
        h.Wire.Json("GET", "/api/statuses", new[] { Fixtures.Status(3, "To Do") });
        h.Wire.Json("GET", "/api/issues/AER-5/comments", Array.Empty<CommentDto>());
        h.Wire.Json("GET", "/api/issues/AER-5/serves", new[] { Served("AER-1", 7, "holds"), Served("AER-1", 9) });

        Assert.Equal(0, await new IssueCommands(h.Cli).ShowAsync(["AER-5"], default));

        Assert.Contains("serves:   AER-1 #7 (holds), AER-1 #9 (not_checked)", h.Said);
    }

    [Fact]
    public async Task Show_prints_nothing_for_an_empty_set_of_either()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/issues/AER-1", Fixtures.Issue("AER-1", "ambition"));
        h.Wire.Json("GET", "/api/issues/AER-5", Fixtures.Issue("AER-5", "epic"));
        h.Wire.Json("GET", "/api/statuses", new[] { Fixtures.Status(3, "To Do") });
        h.Wire.Json("GET", "/api/issues/AER-1/comments", Array.Empty<CommentDto>());
        h.Wire.Json("GET", "/api/issues/AER-5/comments", Array.Empty<CommentDto>());
        h.Wire.Json("GET", Caps, Array.Empty<CapabilityDto>());
        h.Wire.Json("GET", "/api/issues/AER-5/serves", Array.Empty<ServedCapabilityDto>());

        await new IssueCommands(h.Cli).ShowAsync(["AER-1"], default);
        await new IssueCommands(h.Cli).ShowAsync(["AER-5"], default);

        Assert.DoesNotContain("capabilities:", h.Said);
        Assert.DoesNotContain("serves:", h.Said);
    }

    [Fact]
    public async Task Show_still_works_where_the_extra_route_answers_404()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/issues/AER-1", Fixtures.Issue("AER-1", "ambition"));
        h.Wire.Json("GET", "/api/statuses", new[] { Fixtures.Status(3, "To Do") });
        h.Wire.Json("GET", "/api/issues/AER-1/comments", Array.Empty<CommentDto>());

        Assert.Equal(0, await new IssueCommands(h.Cli).ShowAsync(["AER-1"], default));
        Assert.Contains("AER-1  [ambition]", h.Said);
        Assert.DoesNotContain("capabilities:", h.Said);
    }

    [Fact]
    public async Task Show_of_a_task_asks_for_neither()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/issues/AER-12", Fixtures.Issue("AER-12"));
        h.Wire.Json("GET", "/api/statuses", new[] { Fixtures.Status(3, "To Do") });
        h.Wire.Json("GET", "/api/issues/AER-12/comments", Array.Empty<CommentDto>());

        await new IssueCommands(h.Cli).ShowAsync(["AER-12"], default);

        Assert.DoesNotContain(h.Wire.Calls, c => c.Path.EndsWith("/serves") || c.Path.EndsWith("/capabilities"));
    }
}

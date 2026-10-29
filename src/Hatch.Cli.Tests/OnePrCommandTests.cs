namespace Hatch.Cli.Tests;

/// <summary>
/// <c>one-pr</c> - whether a story's tasks share its branch and one pull
/// request, read and set - and the line <c>show</c> prints for it.
/// </summary>
public sealed class OnePrCommandTests
{
    private static IssueDto AStory(string key = "AER-5", bool onePr = false, bool? own = null, string? from = null) =>
        Fixtures.Issue(key, type: "story") with { OnePr = onePr, OnePrOwn = own, OnePrFrom = from };

    [Fact]
    public async Task One_argument_reads_it()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/issues/AER-5", AStory(onePr: true));

        Assert.Equal(0, await new OnePrCommand(h.Cli).RunAsync(["AER-5"], default));

        Assert.Equal("one-pr:   on (the project's default)", h.Said.Trim());
    }

    [Theory]
    [InlineData("on")]
    [InlineData("off")]
    [InlineData("inherit")]
    public async Task A_second_argument_is_the_setting_put_to_the_one_pr_route(string value)
    {
        using var h = new CliHarness();
        h.Wire.Json("PUT", "/api/hatch/issues/AER-5/one-pr", AStory(onePr: value == "on", own: value == "inherit" ? null : value == "on"));

        Assert.Equal(0, await new OnePrCommand(h.Cli).RunAsync(["AER-5", value], default));

        Assert.Equal(value, h.Wire.To("PUT", "/api/hatch/issues/AER-5/one-pr").Single().Read<OnePrRequest>().Value);
    }

    [Fact]
    public void It_says_whether_the_value_was_set_on_the_story_or_came_from_elsewhere()
    {
        Assert.Equal("one-pr:   on (set on this story)", OnePrCommand.Draw(AStory(onePr: true, own: true)));
        Assert.Equal("one-pr:   off (set on this story)", OnePrCommand.Draw(AStory(onePr: false, own: false)));
        Assert.Equal("one-pr:   off (the project's default)", OnePrCommand.Draw(AStory()));
        Assert.Equal(
            "one-pr:   on (inherited from AER-5)",
            OnePrCommand.Draw(Fixtures.Issue("AER-6") with { OnePr = true, OnePrFrom = "AER-5" }));
        Assert.Equal("one-pr:   off (not under a story or bug)", OnePrCommand.Draw(Fixtures.Issue("AER-7")));
    }

    [Fact]
    public void A_bug_is_read_and_explained_as_a_story_is()
    {
        var bug = Fixtures.Issue("AER-8", type: "bug");

        Assert.Equal("one-pr:   on (set on this bug)", OnePrCommand.Draw(bug with { OnePr = true, OnePrOwn = true }));
        Assert.Equal("one-pr:   off (set on this bug)", OnePrCommand.Draw(bug with { OnePr = false, OnePrOwn = false }));
        Assert.Equal("one-pr:   off (the project's default)", OnePrCommand.Draw(bug));
    }

    [Fact]
    public async Task A_bug_takes_the_setting_put_to_the_one_pr_route()
    {
        using var h = new CliHarness();
        h.Wire.Json("PUT", "/api/hatch/issues/AER-8/one-pr",
            Fixtures.Issue("AER-8", type: "bug") with { OnePr = true, OnePrOwn = true });

        Assert.Equal(0, await new OnePrCommand(h.Cli).RunAsync(["AER-8", "on"], default));

        Assert.Equal("one-pr:   on (set on this bug)", h.Said.Trim());
    }

    [Fact]
    public async Task Anything_else_is_refused_and_calls_nothing()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new OnePrCommand(h.Cli).RunAsync(["AER-5", "maybe"], default));

        Assert.Contains("\"maybe\" is not a one-pr setting", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task An_empty_second_argument_is_the_mistake_and_not_a_read()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new OnePrCommand(h.Cli).RunAsync(["AER-5", ""], default));
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task No_key_at_all_is_the_usage_block()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new OnePrCommand(h.Cli).RunAsync([], default));
        Assert.Contains("usage: hatch one-pr", h.Complained);
    }

    [Fact]
    public async Task Dash_h_prints_its_own_usage_and_calls_nothing()
    {
        using var h = new CliHarness();

        Assert.Equal(0, await new OnePrCommand(h.Cli).RunAsync(["-h"], default));
        Assert.StartsWith("usage: hatch one-pr", h.Said);
        Assert.Empty(h.Wire.Calls);
    }

    // ---- show ----

    [Fact]
    public async Task Show_prints_the_line_where_it_is_on()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/issues/AER-6", Fixtures.Issue("AER-6") with { OnePr = true, OnePrFrom = "AER-5" });
        h.Wire.Json("GET", "/api/hatch/statuses", new[] { Fixtures.Status(3, "In Progress") });
        h.Wire.Json("GET", "/api/hatch/issues/AER-6/comments", Array.Empty<CommentDto>());

        await new IssueCommands(h.Cli).ShowAsync(["AER-6"], default);

        Assert.Contains("one-pr:   on (inherited from AER-5)", h.Said);
    }

    [Fact]
    public async Task Show_prints_the_line_for_a_story_told_off()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/issues/AER-5", AStory(onePr: false, own: false));
        h.Wire.Json("GET", "/api/hatch/statuses", new[] { Fixtures.Status(3, "In Progress") });
        h.Wire.Json("GET", "/api/hatch/issues/AER-5/comments", Array.Empty<CommentDto>());

        await new IssueCommands(h.Cli).ShowAsync(["AER-5"], default);

        Assert.Contains("one-pr:   off (set on this story)", h.Said);
    }

    [Fact]
    public async Task Show_says_nothing_of_it_where_nobody_turned_it_on()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/issues/AER-5", AStory());
        h.Wire.Json("GET", "/api/hatch/statuses", new[] { Fixtures.Status(3, "In Progress") });
        h.Wire.Json("GET", "/api/hatch/issues/AER-5/comments", Array.Empty<CommentDto>());

        await new IssueCommands(h.Cli).ShowAsync(["AER-5"], default);

        Assert.DoesNotContain("one-pr", h.Said);
    }
}

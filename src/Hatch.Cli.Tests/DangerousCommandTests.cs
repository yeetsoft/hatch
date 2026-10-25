namespace Hatch.Cli.Tests;

/// <summary>
/// <c>dangerous</c> - whether running an issue's work could destabilise the
/// machine, read, set and cleared.
/// </summary>
public sealed class DangerousCommandTests
{
    private const string Route = "/api/hatch/issues/AER-5/dangerous";

    private static IssueDto Flagged(string reason) =>
        Fixtures.Issue("AER-5") with { Dangerous = reason, DangerousOwn = reason };

    [Fact]
    public async Task One_argument_reads_it_and_puts_nothing()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/issues/AER-5", Flagged("load tests the node"));

        Assert.Equal(0, await new DangerousCommand(h.Cli).RunAsync(["AER-5"], default));

        Assert.Equal("dangerous:    load tests the node", h.Said.Trim());
        Assert.Empty(h.Wire.To("PUT", Route));
    }

    [Fact]
    public async Task Reading_an_unflagged_issue_says_so()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/issues/AER-5", Fixtures.Issue("AER-5"));

        Assert.Equal(0, await new DangerousCommand(h.Cli).RunAsync(["AER-5"], default));

        Assert.Equal("dangerous:    not flagged", h.Said.Trim());
    }

    [Fact]
    public async Task A_reason_is_put_as_the_reason()
    {
        using var h = new CliHarness();
        h.Wire.Json("PUT", Route, Flagged("load tests the node"));

        Assert.Equal(0, await new DangerousCommand(h.Cli).RunAsync(["AER-5", "  load tests the node  "], default));

        Assert.Equal("load tests the node", h.Wire.To("PUT", Route).Single().Read<DangerousRequest>().Reason);
        Assert.Contains("dangerous:    load tests the node", h.Said);
    }

    [Fact]
    public async Task Clear_puts_an_empty_reason()
    {
        using var h = new CliHarness();
        h.Wire.Json("PUT", Route, Fixtures.Issue("AER-5"));

        Assert.Equal(0, await new DangerousCommand(h.Cli).RunAsync(["AER-5", "--clear"], default));

        Assert.Equal("", h.Wire.To("PUT", Route).Single().Read<DangerousRequest>().Reason);
        Assert.Contains("not flagged", h.Said);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reason_is_the_mistake_and_not_a_clear(string reason)
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new DangerousCommand(h.Cli).RunAsync(["AER-5", reason], default));

        Assert.Contains("--clear", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_reason_over_the_limit_is_refused_saying_the_limit_and_the_length()
    {
        using var h = new CliHarness();
        var reason = new string('x', DangerousRequest.MaxLength + 1);

        Assert.Equal(1, await new DangerousCommand(h.Cli).RunAsync(["AER-5", reason], default));

        Assert.Contains($"{DangerousRequest.MaxLength + 1} characters", h.Complained);
        Assert.Contains($"limit is {DangerousRequest.MaxLength}", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_reason_at_the_limit_is_put()
    {
        using var h = new CliHarness();
        var reason = new string('x', DangerousRequest.MaxLength);
        h.Wire.Json("PUT", Route, Flagged(reason));

        Assert.Equal(0, await new DangerousCommand(h.Cli).RunAsync(["AER-5", reason], default));
        Assert.Single(h.Wire.To("PUT", Route));
    }

    [Fact]
    public async Task An_unknown_flag_is_refused_and_calls_nothing()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new DangerousCommand(h.Cli).RunAsync(["AER-5", "--remove"], default));

        Assert.Contains("\"--remove\" is not a dangerous option", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task No_key_at_all_is_the_usage_block()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new DangerousCommand(h.Cli).RunAsync([], default));
        Assert.Contains("usage: hatch dangerous", h.Complained);
    }

    [Fact]
    public async Task Dash_h_prints_its_own_usage_and_calls_nothing()
    {
        using var h = new CliHarness();

        Assert.Equal(0, await new DangerousCommand(h.Cli).RunAsync(["-h"], default));
        Assert.StartsWith("usage: hatch dangerous", h.Said);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public void It_names_the_ancestor_an_inherited_reason_came_from()
    {
        Assert.Equal("dangerous:    load tests the node", DangerousCommand.Draw(Flagged("load tests the node")));
        Assert.Equal(
            "dangerous:    load tests the node (from AER-5)",
            DangerousCommand.Draw(Fixtures.Issue("AER-6") with { Dangerous = "load tests the node", DangerousFrom = "AER-5" }));
        Assert.Null(DangerousCommand.Draw(Fixtures.Issue("AER-7")));
    }
}

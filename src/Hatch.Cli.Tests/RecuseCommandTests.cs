using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>recuse</c> - <c>omit</c> for the runner name its key carries.
/// </summary>
public sealed class RecuseCommandTests
{
    private const string Route = "/api/issues/AER-5/omitted-runners/test%3A%2Fcheckout";

    [Fact]
    public async Task Recusing_trims_puts_the_reason_and_says_what_it_recorded()
    {
        using var h = new CliHarness();
        h.Wire.Json("PUT", Route, Fixtures.Issue("AER-5"));

        Assert.Equal(0, await new RecuseCommand(h.Cli).RunAsync(["AER-5", "  needs a GPU  "], default));

        Assert.Equal("needs a GPU", h.Wire.To("PUT", Route).Single().Read<OmitRunnerRequest>().Reason);
        Assert.Equal("recused test:/checkout from AER-5: needs a GPU", h.Said.Trim());
    }

    [Fact]
    public async Task A_reason_at_the_limit_is_put()
    {
        using var h = new CliHarness();
        h.Wire.Json("PUT", Route, Fixtures.Issue("AER-5"));

        Assert.Equal(0, await new RecuseCommand(h.Cli).RunAsync(["AER-5", new string('x', OmitRunnerRequest.MaxLength)], default));
        Assert.Single(h.Wire.To("PUT", Route));
    }

    [Fact]
    public async Task A_reason_over_the_limit_is_refused_saying_the_length()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new RecuseCommand(h.Cli).RunAsync(["AER-5", new string('x', OmitRunnerRequest.MaxLength + 1)], default));

        Assert.Contains("201", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Theory]
    [InlineData(new[] { "AER-5" }, "needs a reason")]
    [InlineData(new[] { "AER-5", "   " }, "needs a reason")]
    [InlineData(new[] { "AER-5", "--why" }, "not a recuse option")]
    [InlineData(new[] { "AER-5", "a", "b" }, "needs a reason")]
    public async Task Bad_usage_is_refused_before_any_call(string[] args, string said)
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new RecuseCommand(h.Cli).RunAsync(args, default));

        Assert.Contains(said, h.Complained);
        Assert.Contains("usage: hatch recuse", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task No_key_at_all_is_the_usage_block()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new RecuseCommand(h.Cli).RunAsync([], default));

        Assert.Contains("usage: hatch recuse", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_blank_runner_name_is_refused_saying_a_key_names_the_runner_before_any_call()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new RecuseCommand(h.Cli with { RunnerName = "" }).RunAsync(["AER-5", "why"], default));

        Assert.Contains("recuse needs a key - a runner's name is its key's; run `hatch config`", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task An_unknown_issue_says_there_is_nothing_there()
    {
        using var h = new CliHarness();
        h.Wire.Reply("PUT", Route, HttpStatusCode.OK);

        Assert.Equal(1, await new RecuseCommand(h.Cli).RunAsync(["AER-5", "why"], default));

        Assert.Contains("there is nothing there", h.Complained);
        Assert.Single(h.Wire.To("PUT", Route));
    }

    [Fact]
    public async Task Help_prints_the_usage_and_calls_nothing()
    {
        using var h = new CliHarness();

        Assert.Equal(0, await new RecuseCommand(h.Cli).RunAsync(["-h"], default));

        Assert.Equal(string.Join("\n", RecuseCommand.RecuseUsage), h.Said.TrimEnd());
        Assert.Empty(h.Wire.Calls);
    }
}

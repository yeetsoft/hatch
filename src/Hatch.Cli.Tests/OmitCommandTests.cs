using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>omit</c> - the runners kept off an issue's work, read, set and lifted.
/// </summary>
public sealed class OmitCommandTests
{
    private const string Route = "/api/issues/AER-5/omitted-runners/Sisyphus";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-04T12:00:00+00:00");

    private static RunnerOmissionDto Own(string runner = "Sisyphus", string? reason = "too slow") =>
        new(runner, reason, "key-one", Now.AddMinutes(-5), null);

    private static IssueDto Omitting(params RunnerOmissionDto[] omitted) =>
        Fixtures.Issue("AER-5") with { OmittedRunners = omitted, OmittedRunnersOwn = omitted.Where(o => o.FromKey is null).ToArray() };

    [Fact]
    public async Task One_argument_reads_an_empty_list_and_puts_nothing()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/issues/AER-5", Omitting());

        Assert.Equal(0, await new OmitCommand(h.Cli).RunAsync(["AER-5"], default));

        Assert.Equal("omitted runners: none", h.Said.Trim());
        Assert.Empty(h.Wire.To("PUT", Route));
    }

    [Fact]
    public async Task The_read_names_an_inherited_entry_and_not_an_own_one()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        var inherited = new RunnerOmissionDto("Atlas", null, "key-two", Now.AddHours(-3), "AER-3");
        h.Wire.Json("GET", "/api/issues/AER-5", Omitting(Own(), inherited));

        Assert.Equal(0, await new OmitCommand(h.Cli).RunAsync(["AER-5"], default));

        var lines = h.Said.Split('\n', StringSplitOptions.TrimEntries);
        Assert.Equal("omitted runners:", lines[0]);
        Assert.Equal("Sisyphus - too slow - key-one, 5m ago", lines[1]);
        Assert.Equal("Atlas - no reason given - key-two, 3h ago (from AER-3)", lines[2]);
    }

    [Fact]
    public async Task Setting_trims_puts_the_reason_and_prints_the_answered_list()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("PUT", Route, Omitting(Own()));

        Assert.Equal(0, await new OmitCommand(h.Cli).RunAsync(["AER-5", " Sisyphus ", "  too slow  "], default));

        Assert.Equal("too slow", h.Wire.To("PUT", Route).Single().Read<OmitRunnerRequest>().Reason);
        Assert.Contains("Sisyphus - too slow - key-one, 5m ago", h.Said);
    }

    [Fact]
    public async Task A_reason_at_the_limit_is_put()
    {
        using var h = new CliHarness();
        var reason = new string('x', OmitRunnerRequest.MaxLength);
        h.Wire.Json("PUT", Route, Omitting(Own(reason: reason)));

        Assert.Equal(0, await new OmitCommand(h.Cli).RunAsync(["AER-5", "Sisyphus", reason], default));
        Assert.Single(h.Wire.To("PUT", Route));
    }

    [Fact]
    public async Task A_reason_over_the_limit_is_refused_saying_the_length()
    {
        using var h = new CliHarness();
        var reason = new string('x', OmitRunnerRequest.MaxLength + 1);

        Assert.Equal(1, await new OmitCommand(h.Cli).RunAsync(["AER-5", "Sisyphus", reason], default));

        Assert.Contains("201", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Theory]
    [InlineData(new[] { "AER-5", "Sisyphus" }, "needs a reason")]
    [InlineData(new[] { "AER-5", "Sisyphus", "   " }, "needs a reason")]
    [InlineData(new[] { "AER-5", "   ", "why" }, "needs a runner")]
    [InlineData(new[] { "AER-5", "--remove", "why" }, "not an omit option")]
    [InlineData(new[] { "AER-5", "Sisyphus", "too", "slow" }, "quote the reason")]
    [InlineData(new[] { "AER-5", "--lift" }, "--lift needs a runner")]
    [InlineData(new[] { "AER-5", "--lift", "Sisyphus", "extra" }, "--lift needs a runner")]
    public async Task Bad_usage_is_refused_before_any_call(string[] args, string said)
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new OmitCommand(h.Cli).RunAsync(args, default));

        Assert.Contains(said, h.Complained);
        Assert.Contains("usage: hatch omit", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task No_key_at_all_is_the_usage_block()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new OmitCommand(h.Cli).RunAsync([], default));

        Assert.Contains("usage: hatch omit", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Lift_deletes_and_prints_the_answered_list()
    {
        using var h = new CliHarness();
        h.Wire.Json("DELETE", Route, Omitting());

        Assert.Equal(0, await new OmitCommand(h.Cli).RunAsync(["AER-5", "--lift", "Sisyphus"], default));

        Assert.Single(h.Wire.To("DELETE", Route));
        Assert.Equal("omitted runners: none", h.Said.Trim());
    }

    [Fact]
    public async Task Lifting_an_inherited_omission_prints_the_sentence_naming_the_ancestor()
    {
        using var h = new CliHarness();
        h.Wire.Reply("DELETE", Route, HttpStatusCode.NotFound, "\"AER-5 has no own omission for Sisyphus - it is inherited from AER-3\"");

        Assert.Equal(1, await new OmitCommand(h.Cli).RunAsync(["AER-5", "--lift", "Sisyphus"], default));

        Assert.Equal("hatch: AER-5 has no own omission for Sisyphus - it is inherited from AER-3", h.Complained.Trim());
    }

    [Fact]
    public async Task Lifting_on_an_unknown_issue_throws_the_generic_refusal()
    {
        using var h = new CliHarness();
        h.Wire.Reply("DELETE", Route, HttpStatusCode.NotFound);

        var thrown = await Assert.ThrowsAsync<HatchException>(
            () => new OmitCommand(h.Cli).RunAsync(["AER-5", "--lift", "Sisyphus"], default));

        Assert.Equal(HttpStatusCode.NotFound, thrown.Status);
    }

    [Fact]
    public async Task Dash_h_prints_its_own_usage_and_calls_nothing()
    {
        using var h = new CliHarness();

        Assert.Equal(0, await new OmitCommand(h.Cli).RunAsync(["-h"], default));

        Assert.StartsWith("usage: hatch omit", h.Said);
        Assert.Empty(h.Wire.Calls);
    }
}

using System.Text.Json;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>runners</c> - every runner that has spoken to this Hatch lately, and the
/// state word computed off each one's own facts.
/// </summary>
public sealed class RunnersCommandTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T12:00:00Z");

    private static RunnerDto ARunner(
        string name = "runner-1", string? claimKey = null, DateTimeOffset? lastSeenAt = null,
        int goneAfterSeconds = 3600, string state = "running", DateTimeOffset? exhaustedUntil = null,
        IReadOnlyList<string>? projects = null) =>
        new(
            Name: name, Kind: "loop", FirstSeenAt: DateTimeOffset.UnixEpoch,
            LastSeenAt: lastSeenAt ?? Now - TimeSpan.FromSeconds(10),
            ClaimKey: claimKey, Line: null, LineAt: null, State: state, Under: null,
            MaxRuns: null, MaxSpend: null, UntilAt: null, GoneAfterSeconds: goneAfterSeconds,
            Repositories: [], Clones: null, Mine: null, Standing: null, Where: null, Agent: null,
            ExhaustedUntil: exhaustedUntil, Projects: projects ?? []);

    [Fact]
    public async Task No_runner_heard_from_prints_one_sentence_rather_than_an_empty_table()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/hatch/runners", Array.Empty<RunnerDto>());

        Assert.Equal(0, await new RunnersCommand(h.Cli).RunAsync([], default));
        Assert.Equal(RunnersCommand.EmptyWords, h.Said);
    }

    [Fact]
    public async Task A_row_holding_a_claim_and_heard_from_recently_prints_working_and_the_key()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/hatch/runners", new List<RunnerDto> { ARunner(claimKey: "AER-12") });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("working", h.Said);
        Assert.Contains("AER-12", h.Said);
    }

    [Fact]
    public async Task The_same_row_with_no_claim_prints_idle_and_none()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/hatch/runners", new List<RunnerDto> { ARunner(claimKey: null) });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("idle", h.Said);
        Assert.Contains("none", h.Said);
    }

    [Fact]
    public async Task A_row_bound_to_projects_ends_with_them_and_a_row_bound_to_none_does_not()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/hatch/runners", new List<RunnerDto>
        {
            ARunner(name: "bound", projects: ["FIL", "AER"]),
            ARunner(name: "free"),
        });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        Assert.EndsWith("  projects FIL AER", lines.Single(l => l.StartsWith("bound", StringComparison.Ordinal)));
        Assert.DoesNotContain("projects", lines.Single(l => l.StartsWith("free", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_row_silent_past_its_own_gone_after_seconds_prints_gone_and_still_names_its_key()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/hatch/runners", new List<RunnerDto>
        {
            ARunner(claimKey: "AER-12", goneAfterSeconds: 60, lastSeenAt: Now - TimeSpan.FromSeconds(120)),
        });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("gone", h.Said);
        Assert.Contains("AER-12", h.Said);
        Assert.DoesNotContain("none", h.Said);
    }

    [Fact]
    public async Task A_paused_row_heard_from_recently_prints_paused()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/hatch/runners", new List<RunnerDto> { ARunner(state: "paused") });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("paused", h.Said);
    }

    [Fact]
    public async Task Json_prints_the_endpoints_own_answer_and_nothing_else()
    {
        using var h = new CliHarness();
        var rows = new List<RunnerDto> { ARunner(claimKey: "AER-12") };
        h.Wire.Json("GET", "/api/hatch/runners", rows);

        Assert.Equal(0, await new RunnersCommand(h.Cli).RunAsync(["--json"], default));

        Assert.Equal(JsonSerializer.Serialize(rows, Fixtures.Json), h.Said);
    }

    [Fact]
    public async Task An_unknown_flag_is_refused_and_calls_the_wire_zero_times()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new RunnersCommand(h.Cli).RunAsync(["--nonsense"], default));
        Assert.Contains("runners does not take \"--nonsense\"", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Dash_h_prints_its_own_usage_and_calls_the_wire_zero_times()
    {
        using var h = new CliHarness();

        Assert.Equal(0, await new RunnersCommand(h.Cli).RunAsync(["-h"], default));
        Assert.StartsWith("usage: hatch runners", h.Said);
        Assert.Empty(h.Wire.Calls);
    }
}

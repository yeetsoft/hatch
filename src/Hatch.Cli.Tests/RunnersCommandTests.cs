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
        IReadOnlyList<string>? projects = null, int? runs = null, decimal? spent = null, string? host = null,
        long? tokensUsed = null, long? tokensLastHour = null, IReadOnlyList<string>? models = null,
        IReadOnlyList<string>? has = null, IReadOnlyList<RunnerSessionDto>? sessions = null) =>
        new(
            Name: name, Kind: "loop", FirstSeenAt: DateTimeOffset.UnixEpoch,
            LastSeenAt: lastSeenAt ?? Now - TimeSpan.FromSeconds(10),
            ClaimKey: claimKey, Line: null, LineAt: null, State: state, Under: null,
            MaxRuns: null, MaxSpend: null, UntilAt: null, GoneAfterSeconds: goneAfterSeconds,
            Repositories: [], Clones: null, Mine: null, Standing: null, Where: null, Agent: null,
            ExhaustedUntil: exhaustedUntil, Projects: projects ?? [], Runs: runs, Spent: spent, Host: host,
            TokensUsed: tokensUsed, TokensLastHour: tokensLastHour, Models: models, Has: has, Sessions: sessions);

    private static RunnerSessionDto ASession(
        string where = "Fry:/code/hatch", string? agent = "claude", string state = "running",
        string? claimKey = null, DateTimeOffset? lastSeenAt = null, DateTimeOffset? endedAt = null) =>
        new(
            Id: 1, Where: where, Kind: "loop", Host: null, State: state,
            StartedAt: Now - TimeSpan.FromHours(1), LastSeenAt: lastSeenAt ?? Now - TimeSpan.FromSeconds(10),
            EndedAt: endedAt, ClaimKey: claimKey, Agent: agent);

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
    public async Task A_row_with_dependencies_closes_with_them_before_its_projects_and_a_row_with_none_does_not()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/hatch/runners", new List<RunnerDto>
        {
            ARunner(name: "told", projects: ["HA"], has: ["dotnet@10.0.100", "node@22.11.0", "pwsh"]),
            ARunner(name: "silent"),
        });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        Assert.Contains("  has dotnet 10.0.100, node 22.11.0, pwsh  projects HA", lines.Single(l => l.StartsWith("told", StringComparison.Ordinal)));
        Assert.DoesNotContain("has", lines.Single(l => l.StartsWith("silent", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_row_with_models_closes_with_them_after_its_projects_and_a_row_with_none_does_not()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/hatch/runners", new List<RunnerDto>
        {
            ARunner(name: "told", projects: ["HA"], models: ["haiku", "sonnet"]),
            ARunner(name: "silent"),
        });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        Assert.EndsWith("  projects HA  models haiku sonnet", lines.Single(l => l.StartsWith("told", StringComparison.Ordinal)));
        Assert.DoesNotContain("models", lines.Single(l => l.StartsWith("silent", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_row_reporting_its_tokens_prints_them_after_the_spend_with_the_hour_where_it_has_one()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/hatch/runners", new List<RunnerDto>
        {
            ARunner(name: "both", runs: 3, spent: 1.25m, host: "Fry", tokensUsed: 2_400_000, tokensLastHour: 180_000),
            ARunner(name: "total", tokensUsed: 950_000),
            ARunner(name: "older", runs: 3, spent: 1.25m, host: "Fry"),
        });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        Assert.EndsWith("  on Fry 3 runs $1.25 2.4M tokens (180k last hour)", lines.Single(l => l.StartsWith("both", StringComparison.Ordinal)));
        Assert.EndsWith("  950k tokens", lines.Single(l => l.StartsWith("total", StringComparison.Ordinal)));
        Assert.DoesNotContain("tokens", lines.Single(l => l.StartsWith("older", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_row_reporting_its_host_runs_and_spend_prints_them_before_any_projects_and_a_row_reporting_none_does_not()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/hatch/runners", new List<RunnerDto>
        {
            ARunner(name: "told", runs: 3, spent: 1.25m, host: "Fry", projects: ["HA"]),
            ARunner(name: "mute"),
        });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        Assert.EndsWith("  on Fry 3 runs $1.25  projects HA", lines.Single(l => l.StartsWith("told", StringComparison.Ordinal)));
        var mute = lines.Single(l => l.StartsWith("mute", StringComparison.Ordinal));
        Assert.DoesNotContain(" runs", mute);
        Assert.DoesNotContain("$", mute);
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
    public async Task A_row_with_two_live_sessions_prints_them_indented_in_server_order()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/hatch/runners", new List<RunnerDto>
        {
            ARunner(name: "busy", sessions: [
                ASession(where: "Fry:/code/hatch", agent: "claude", claimKey: "AER-12"),
                ASession(where: "Leela:/code/aerie", agent: "codex", state: "paused"),
            ]),
        });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        var at = Array.FindIndex(lines, l => l.StartsWith("busy", StringComparison.Ordinal));
        Assert.Equal("  Fry:/code/hatch    claude  working", lines[at + 1]);
        Assert.Equal("  Leela:/code/aerie  codex   paused", lines[at + 2]);
    }

    [Fact]
    public async Task A_gone_runner_with_only_ended_sessions_prints_no_session_line()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/hatch/runners", new List<RunnerDto>
        {
            ARunner(
                name: "silent", goneAfterSeconds: 60, lastSeenAt: Now - TimeSpan.FromSeconds(120),
                sessions: [ASession(endedAt: Now - TimeSpan.FromSeconds(90))]),
        });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        var at = Array.FindIndex(lines, l => l.StartsWith("silent", StringComparison.Ordinal));
        Assert.Contains("gone", lines[at]);
        Assert.Equal(at + 1, lines.Length);
    }

    [Fact]
    public async Task An_ended_session_on_an_otherwise_live_runner_is_not_printed()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/hatch/runners", new List<RunnerDto>
        {
            ARunner(name: "mixed", sessions: [
                ASession(where: "Fry:/code/hatch"),
                ASession(where: "Leela:/code/aerie", endedAt: Now - TimeSpan.FromSeconds(5)),
            ]),
        });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("Fry:/code/hatch", h.Said);
        Assert.DoesNotContain("Leela:/code/aerie", h.Said);
    }

    [Fact]
    public async Task A_session_with_no_agent_prints_a_dash_in_its_place()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/hatch/runners", new List<RunnerDto>
        {
            ARunner(name: "quiet", sessions: [ASession(agent: null)]),
        });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        var at = Array.FindIndex(lines, l => l.StartsWith("quiet", StringComparison.Ordinal));
        Assert.Equal("  Fry:/code/hatch  -  idle", lines[at + 1]);
    }

    [Fact]
    public async Task Json_prints_the_endpoints_own_answer_and_nothing_else()
    {
        using var h = new CliHarness();
        var rows = new List<RunnerDto> { ARunner(claimKey: "AER-12", sessions: [ASession(), ASession(where: "Leela:/code/aerie")]) };
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

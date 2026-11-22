using System.Net;
using System.Text.Json;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>runners</c> - every runner that has spoken to this Hatch lately, and the
/// state word computed off each one's own facts.
/// </summary>
public sealed class RunnersCommandTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T12:00:00Z");

    internal static RunnerDto ARunner(
        string name = "runner-1", string? claimKey = null, DateTimeOffset? lastSeenAt = null,
        int goneAfterSeconds = 3600, string state = "running", DateTimeOffset? exhaustedUntil = null,
        IReadOnlyList<string>? projects = null, int? runs = null, decimal? spent = null, string? host = null,
        long? tokensUsed = null, long? tokensLastHour = null, IReadOnlyList<string>? models = null,
        IReadOnlyList<string>? has = null, IReadOnlyList<RunnerSessionDto>? sessions = null, string? image = null,
        DateTimeOffset? agentDownUntil = null, string? agentDownKind = null, string? agentDownLine = null,
        IReadOnlyList<string>? refusedModels = null, long? acceleratorMemoryBytes = null,
        int? acceleratorDeviceCount = null, string? acceleratorBackend = null,
        string statusMode = RunnerStatusMode.All, IReadOnlyList<int>? statusIds = null,
        string? signing = null, string? signingWhy = null, bool preferred = false,
        string? forge = null, string? forgeWhy = null) =>
        new(
            Name: name, Kind: "loop", FirstSeenAt: DateTimeOffset.UnixEpoch,
            LastSeenAt: lastSeenAt ?? Now - TimeSpan.FromSeconds(10),
            ClaimKey: claimKey, Line: null, LineAt: null, State: state, Under: null,
            MaxRuns: null, MaxSpend: null, UntilAt: null, GoneAfterSeconds: goneAfterSeconds,
            Repositories: [], Clones: null, Mine: null, Standing: null, Where: null, Agent: null,
            ExhaustedUntil: exhaustedUntil, Projects: projects ?? [], Runs: runs, Spent: spent, Host: host,
            TokensUsed: tokensUsed, TokensLastHour: tokensLastHour, Models: models, Has: has, Sessions: sessions,
            Image: image, AgentDownUntil: agentDownUntil, AgentDownKind: agentDownKind, AgentDownLine: agentDownLine,
            RefusedModels: refusedModels ?? [], AcceleratorMemoryBytes: acceleratorMemoryBytes,
            AcceleratorDeviceCount: acceleratorDeviceCount, AcceleratorBackend: acceleratorBackend,
            StatusMode: statusMode, StatusIds: statusIds ?? [], Signing: signing, SigningWhy: signingWhy, Preferred: preferred,
            Forge: forge, ForgeWhy: forgeWhy);

    private static RunnerSessionDto ASession(
        string where = "Fry:/code/hatch", string? agent = "claude", string? os = "Darwin 23.6.0",
        string state = "running", string? claimKey = null, DateTimeOffset? lastSeenAt = null, DateTimeOffset? endedAt = null,
        DateTimeOffset? agentDownUntil = null, string? agentDownKind = null) =>
        new(
            Id: 1, Where: where, Kind: "loop", Host: null, State: state,
            StartedAt: Now - TimeSpan.FromHours(1), LastSeenAt: lastSeenAt ?? Now - TimeSpan.FromSeconds(10),
            EndedAt: endedAt, ClaimKey: claimKey, Agent: agent, Os: os,
            AgentDownUntil: agentDownUntil, AgentDownKind: agentDownKind);

    [Fact]
    public async Task No_runner_heard_from_prints_one_sentence_rather_than_an_empty_table()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/runners", Array.Empty<RunnerDto>());

        Assert.Equal(0, await new RunnersCommand(h.Cli).RunAsync([], default));
        Assert.Equal(RunnersCommand.EmptyWords, h.Said);
    }

    [Fact]
    public async Task A_row_holding_a_claim_and_heard_from_recently_prints_working_and_the_key()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto> { ARunner(claimKey: "AER-12") });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("working", h.Said);
        Assert.Contains("AER-12", h.Said);
    }

    [Fact]
    public async Task The_same_row_with_no_claim_prints_idle_and_none()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto> { ARunner(claimKey: null) });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("idle", h.Said);
        Assert.Contains("none", h.Said);
    }

    [Fact]
    public async Task A_row_bound_to_projects_ends_with_them_and_a_row_bound_to_none_does_not()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "bound", projects: ["FIL", "AER"]),
            ARunner(name: "free"),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        Assert.EndsWith("  projects FIL AER", lines.Single(l => l.StartsWith("bound", StringComparison.Ordinal)));
        Assert.DoesNotContain("projects", lines.Single(l => l.StartsWith("free", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_row_with_dependencies_closes_with_them_before_its_projects_and_a_row_with_none_does_not()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "told", projects: ["HA"], has: ["dotnet@10.0.100", "node@22.11.0", "pwsh"]),
            ARunner(name: "silent"),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        Assert.Contains("  has dotnet 10.0.100, node 22.11.0, pwsh  projects HA", lines.Single(l => l.StartsWith("told", StringComparison.Ordinal)));
        Assert.DoesNotContain("has", lines.Single(l => l.StartsWith("silent", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_row_with_models_closes_with_them_after_its_projects_and_a_row_with_none_does_not()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "told", projects: ["HA"], models: ["haiku", "sonnet"]),
            ARunner(name: "silent"),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        Assert.EndsWith("  projects HA  models haiku sonnet", lines.Single(l => l.StartsWith("told", StringComparison.Ordinal)));
        Assert.DoesNotContain("models", lines.Single(l => l.StartsWith("silent", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_row_reporting_its_tokens_prints_them_after_the_spend_with_the_hour_where_it_has_one()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "both", runs: 3, spent: 1.25m, host: "Fry", tokensUsed: 2_400_000, tokensLastHour: 180_000),
            ARunner(name: "total", tokensUsed: 950_000),
            ARunner(name: "older", runs: 3, spent: 1.25m, host: "Fry"),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

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
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "told", runs: 3, spent: 1.25m, host: "Fry", projects: ["HA"]),
            ARunner(name: "mute"),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        Assert.EndsWith("  on Fry 3 runs $1.25  projects HA", lines.Single(l => l.StartsWith("told", StringComparison.Ordinal)));
        var mute = lines.Single(l => l.StartsWith("mute", StringComparison.Ordinal));
        Assert.DoesNotContain(" runs", mute);
        Assert.DoesNotContain("$", mute);
    }

    [Fact]
    public async Task A_row_reporting_its_image_prints_it_after_the_host_and_a_row_reporting_none_does_not()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "built", host: "Fry", image: "ghcr.io/o/r:1"),
            ARunner(name: "generic", host: "Fry"),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        Assert.EndsWith("  on Fry image ghcr.io/o/r:1", lines.Single(l => l.StartsWith("built", StringComparison.Ordinal)));
        Assert.DoesNotContain("image", lines.Single(l => l.StartsWith("generic", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_row_reporting_an_accelerator_prints_it_after_has_and_before_projects_and_one_that_did_not_prints_nothing_extra()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "one", acceleratorBackend: "metal", acceleratorMemoryBytes: 38_654_705_664L, acceleratorDeviceCount: 1),
            ARunner(name: "many", acceleratorBackend: "cuda", acceleratorMemoryBytes: 25_769_803_776L, acceleratorDeviceCount: 2),
            ARunner(name: "nomem", acceleratorBackend: "rocm"),
            ARunner(name: "frac", acceleratorBackend: "vulkan", acceleratorMemoryBytes: 1_610_612_736L),
            ARunner(name: "none", acceleratorBackend: "none", acceleratorMemoryBytes: 0, acceleratorDeviceCount: 0),
            ARunner(name: "silent"),
            ARunner(name: "all", projects: ["HA"], has: ["pwsh"], acceleratorBackend: "cuda", acceleratorMemoryBytes: 25_769_803_776L, acceleratorDeviceCount: 2),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        string Row(string name) => lines.Single(l => l.StartsWith(name, StringComparison.Ordinal));
        Assert.EndsWith("  accelerator metal 36GB", Row("one"));
        Assert.EndsWith("  accelerator cuda 24GB x2", Row("many"));
        Assert.EndsWith("  accelerator rocm", Row("nomem"));
        Assert.EndsWith("  accelerator vulkan 1.5GB", Row("frac"));
        Assert.DoesNotContain("accelerator", Row("none"));
        Assert.DoesNotContain("accelerator", Row("silent"));
        Assert.EndsWith("  has pwsh  accelerator cuda 24GB x2  projects HA", Row("all"));
    }

    [Fact]
    public async Task A_row_prints_the_three_signing_readings_after_the_accelerator_and_one_that_never_said_prints_nothing()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "signs", signing: "works"),
            ARunner(name: "unsigned", signing: "off"),
            ARunner(name: "locked", signing: "fails", signingWhy: "gpg failed to sign the data"),
            ARunner(name: "unexplained", signing: "fails"),
            ARunner(name: "unknown", signing: "maybe"),
            ARunner(name: "silent"),
            ARunner(name: "all", projects: ["HA"], acceleratorBackend: "cuda", signing: "works"),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        string Row(string name) => lines.Single(l => l.StartsWith(name, StringComparison.Ordinal));
        Assert.EndsWith("  signs its commits", Row("signs"));
        Assert.EndsWith("  does not sign its commits", Row("unsigned"));
        Assert.EndsWith("  signing fails: gpg failed to sign the data", Row("locked"));
        Assert.EndsWith("  signing fails", Row("unexplained"));
        Assert.DoesNotContain("sign", Row("unknown"));
        Assert.DoesNotContain("sign", Row("silent"));
        Assert.EndsWith("  accelerator cuda  signs its commits  projects HA", Row("all"));
    }

    [Fact]
    public async Task A_row_prints_the_forge_readings_after_the_signing_and_one_that_never_said_prints_nothing()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "in", forge: "works"),
            ARunner(name: "locked", forge: "fails", forgeWhy: "token expired"),
            ARunner(name: "unexplained", forge: "fails"),
            ARunner(name: "unknown", forge: "maybe"),
            ARunner(name: "silent"),
            ARunner(name: "all", projects: ["HA"], acceleratorBackend: "cuda", signing: "works", forge: "works"),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split(n);
        string Row(string name) => lines.Single(l => l.StartsWith(name, StringComparison.Ordinal));
        Assert.EndsWith("  signed in to its forge", Row("in"));
        Assert.EndsWith("  forge sign-in fails: token expired", Row("locked"));
        Assert.EndsWith("  forge sign-in fails", Row("unexplained"));
        Assert.DoesNotContain("forge", Row("unknown"));
        Assert.DoesNotContain("forge", Row("silent"));
        Assert.EndsWith("  accelerator cuda  signs its commits  signed in to its forge  projects HA", Row("all"));
    }

    [Fact]
    public async Task A_row_silent_past_its_own_gone_after_seconds_prints_gone_and_still_names_its_key()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(claimKey: "AER-12", goneAfterSeconds: 60, lastSeenAt: Now - TimeSpan.FromSeconds(120)),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("gone", h.Said);
        Assert.Contains("AER-12", h.Said);
        Assert.DoesNotContain("none", h.Said);
    }

    [Fact]
    public async Task A_paused_row_heard_from_recently_prints_paused()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto> { ARunner(state: "paused") });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("paused", h.Said);
    }

    [Fact]
    public async Task A_row_whose_agent_down_check_is_ahead_prints_agent_not_starting()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(agentDownUntil: Now.AddHours(1), agentDownKind: "not-starting"),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("agent not starting", h.Said);
    }

    [Fact]
    public async Task A_row_signed_out_prints_needs_signing_in()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(agentDownUntil: Now.AddHours(1), agentDownKind: "signed-out"),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("needs signing in", h.Said);
    }

    [Fact]
    public async Task A_row_silent_past_its_own_gone_after_seconds_prints_gone_even_while_agent_down()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(goneAfterSeconds: 60, lastSeenAt: Now - TimeSpan.FromSeconds(120), agentDownUntil: Now.AddHours(1), agentDownKind: "not-starting"),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("gone", h.Said);
        Assert.DoesNotContain("agent not starting", h.Said);
    }

    [Fact]
    public async Task A_row_whose_agent_down_check_has_passed_prints_idle_not_agent_not_starting()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(agentDownUntil: Now.AddHours(-1), agentDownKind: "not-starting"),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("idle", h.Said);
        Assert.DoesNotContain("agent not starting", h.Said);
    }

    [Fact]
    public async Task An_agent_down_row_with_a_line_closes_with_what_the_agent_said()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(agentDownUntil: Now.AddHours(1), agentDownKind: "not-starting", agentDownLine: "npm not found"),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("agent said: npm not found", h.Said);
    }

    [Fact]
    public async Task A_session_whose_agent_is_down_prints_agent_not_starting_under_its_runner()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(sessions: [ASession(agentDownUntil: Now.AddHours(1), agentDownKind: "signed-out")]),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("needs signing in", h.Said);
    }

    [Fact]
    public async Task A_board_paused_row_prints_paused_by_the_board_and_what_the_board_said()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(agentDownUntil: Now.AddHours(1), agentDownKind: "board-paused", agentDownLine: "three sessions ended in error"),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("paused by the board", h.Said);
        Assert.Contains("board said: three sessions ended in error", h.Said);
        Assert.DoesNotContain("agent not starting", h.Said);
        Assert.DoesNotContain("agent said", h.Said);
    }

    [Fact]
    public async Task A_board_paused_session_prints_paused_by_the_board_under_its_runner()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(sessions: [ASession(agentDownUntil: Now.AddHours(1), agentDownKind: "board-paused")]),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("paused by the board", h.Said);
        Assert.DoesNotContain("agent not starting", h.Said);
    }

    [Fact]
    public async Task A_board_pause_that_has_passed_prints_idle_and_no_line()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(agentDownUntil: Now.AddHours(-1), agentDownKind: "board-paused", agentDownLine: "three sessions ended in error"),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("idle", h.Said);
        Assert.DoesNotContain("paused by the board", h.Said);
        Assert.DoesNotContain("board said", h.Said);
    }

    /// <summary>
    /// The server writes <c>boardDrift</c>/<c>sourceDrift</c> as strings
    /// (<c>JsonStringEnumConverter</c> in <c>Hatch.Api</c>); <see cref="Fixtures"/>'s
    /// own serialiser writes the numeric form instead, so the body here is
    /// built by hand to stand in for what the server actually sends.
    /// </summary>
    private static string RunnerBodyWithStringDrift(string name)
    {
        var body = JsonSerializer.Serialize(new List<RunnerDto> { ARunner(name: name) }, Fixtures.Json);
        Assert.Contains("\"boardDrift\":0", body);
        Assert.Contains("\"sourceDrift\":0", body);
        body = body.Replace("\"boardDrift\":0", "\"boardDrift\":\"Behind\"");
        body = body.Replace("\"sourceDrift\":0", "\"sourceDrift\":\"Current\"");
        Assert.Contains("\"boardDrift\":\"Behind\"", body);
        Assert.Contains("\"sourceDrift\":\"Current\"", body);
        return body;
    }

    [Fact]
    public async Task Runners_written_with_string_drift_are_listed()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Reply("GET", "/api/runners", HttpStatusCode.OK, RunnerBodyWithStringDrift("behind-runner"));
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        Assert.Equal(0, await new RunnersCommand(h.Cli).RunAsync([], default));
        Assert.Contains("behind-runner", h.Said);
        Assert.Empty(h.Complained);
    }

    [Fact]
    public async Task Board_reads_string_drift_as_the_enum()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Reply("GET", "/api/runners", HttpStatusCode.OK, RunnerBodyWithStringDrift("behind-runner"));

        var runners = await h.Board.RunnersAsync(default);

        Assert.Equal(RunnerBoardDrift.Behind, runners.Single().BoardDrift);
        Assert.Equal(RunnerBoardDrift.Current, runners.Single().SourceDrift);
    }

    [Fact]
    public async Task A_row_with_two_live_sessions_prints_them_indented_in_server_order()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "busy", sessions: [
                ASession(where: "Fry:/code/hatch", agent: "claude", os: "Darwin 23.6.0", claimKey: "AER-12"),
                ASession(where: "Leela:/code/aerie", agent: "codex", os: "Ubuntu 22.04", state: "paused"),
            ]),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        var at = Array.FindIndex(lines, l => l.StartsWith("busy", StringComparison.Ordinal));
        Assert.Equal("  Fry:/code/hatch    claude  Darwin 23.6.0  working", lines[at + 1]);
        Assert.Equal("  Leela:/code/aerie  codex   Ubuntu 22.04   paused", lines[at + 2]);
    }

    [Fact]
    public async Task A_gone_runner_with_only_ended_sessions_prints_no_session_line()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(
                name: "silent", goneAfterSeconds: 60, lastSeenAt: Now - TimeSpan.FromSeconds(120),
                sessions: [ASession(endedAt: Now - TimeSpan.FromSeconds(90))]),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

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
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "mixed", sessions: [
                ASession(where: "Fry:/code/hatch"),
                ASession(where: "Leela:/code/aerie", endedAt: Now - TimeSpan.FromSeconds(5)),
            ]),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        Assert.Contains("Fry:/code/hatch", h.Said);
        Assert.DoesNotContain("Leela:/code/aerie", h.Said);
    }

    [Fact]
    public async Task A_session_with_no_agent_prints_a_dash_in_its_place()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "quiet", sessions: [ASession(agent: null)]),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        var at = Array.FindIndex(lines, l => l.StartsWith("quiet", StringComparison.Ordinal));
        Assert.Equal("  Fry:/code/hatch  -  Darwin 23.6.0  idle", lines[at + 1]);
    }

    [Fact]
    public async Task A_session_with_no_os_prints_a_dash_and_its_sibling_stays_aligned()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "quiet", sessions: [
                ASession(where: "Fry:/code/hatch", os: null),
                ASession(where: "Leela:/code/aerie", os: "Ubuntu 22.04"),
            ]),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        var at = Array.FindIndex(lines, l => l.StartsWith("quiet", StringComparison.Ordinal));
        var osWidth = "Ubuntu 22.04".Length;
        Assert.Equal("  Fry:/code/hatch    claude  " + "-".PadRight(osWidth) + "  idle", lines[at + 1]);
        Assert.Equal("  Leela:/code/aerie  claude  Ubuntu 22.04  idle", lines[at + 2]);
    }

    [Fact]
    public async Task Json_prints_the_endpoints_own_answer_and_nothing_else()
    {
        using var h = new CliHarness();
        var rows = new List<RunnerDto> { ARunner(claimKey: "AER-12", sessions: [ASession(), ASession(where: "Leela:/code/aerie")]) };
        h.Wire.Json("GET", "/api/runners", rows);

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

    [Fact]
    public async Task A_row_refusing_some_models_closes_with_them_after_its_models_and_a_row_refusing_none_does_not()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "told", models: ["haiku"], refusedModels: ["haiku", "fable"]),
            ARunner(name: "silent", models: ["haiku"]),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        Assert.EndsWith("  models haiku  refuses haiku, fable", lines.Single(l => l.StartsWith("told", StringComparison.Ordinal)));
        Assert.DoesNotContain("refuses", lines.Single(l => l.StartsWith("silent", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_row_marked_preferred_closes_with_the_word_and_an_unmarked_row_does_not()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "first", refusedModels: ["fable"], preferred: true),
            ARunner(name: "other", refusedModels: ["fable"]),
        });
        h.Wire.Json("GET", "/api/statuses", Array.Empty<StatusDto>());

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        Assert.EndsWith("  refuses fable  preferred", lines.Single(l => l.StartsWith("first", StringComparison.Ordinal)));
        Assert.DoesNotContain("preferred", lines.Single(l => l.StartsWith("other", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Json_carries_preferred_on_every_row()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "first", preferred: true),
            ARunner(name: "other"),
        });

        Assert.Equal(0, await new RunnersCommand(h.Cli).RunAsync(["--json"], default));

        using var doc = JsonDocument.Parse(h.Said);
        var rows = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].GetProperty("preferred").GetBoolean());
        Assert.False(rows[1].GetProperty("preferred").GetBoolean());
    }

    [Fact]
    public async Task A_row_under_only_closes_with_the_named_columns_in_board_order()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "picky", statusMode: RunnerStatusMode.Only, statusIds: [3, 1]),
        });
        h.Wire.Json("GET", "/api/statuses", new[]
        {
            Fixtures.Status(1, "Breakdown"), Fixtures.Status(2, "In Progress"), Fixtures.Status(3, "In Review"),
        });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        Assert.EndsWith("  only Breakdown, In Review", lines.Single(l => l.StartsWith("picky", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_row_under_except_closes_with_skips_and_a_row_under_all_closes_with_nothing_extra()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "picky", statusMode: RunnerStatusMode.Except, statusIds: [1, 2]),
            ARunner(name: "open", statusMode: RunnerStatusMode.All),
        });
        h.Wire.Json("GET", "/api/statuses", new[]
        {
            Fixtures.Status(1, "In Progress"), Fixtures.Status(2, "Testing"), Fixtures.Status(3, "Done", terminal: true),
        });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        Assert.EndsWith("  skips In Progress, Testing", lines.Single(l => l.StartsWith("picky", StringComparison.Ordinal)));
        var open = lines.Single(l => l.StartsWith("open", StringComparison.Ordinal));
        Assert.DoesNotContain("only", open);
        Assert.DoesNotContain("skips", open);
    }

    [Fact]
    public async Task A_rule_naming_no_current_column_closes_with_nothing_extra()
    {
        using var h = new CliHarness(clock: new FrozenClock(Now));
        h.Wire.Json("GET", "/api/runners", new List<RunnerDto>
        {
            ARunner(name: "stale", statusMode: RunnerStatusMode.Only, statusIds: [99]),
        });
        h.Wire.Json("GET", "/api/statuses", new[] { Fixtures.Status(1, "Backlog") });

        await new RunnersCommand(h.Cli).RunAsync([], default);

        var lines = h.Said.Split('\n');
        var stale = lines.Single(l => l.StartsWith("stale", StringComparison.Ordinal));
        Assert.DoesNotContain("only", stale);
    }
}

/// <summary>
/// <c>runners refuse</c> - a runner's own refused models, read, set and
/// lifted from a terminal.
/// </summary>
public sealed class RunnersRefuseCommandTests
{
    private static MeDto AKey(string name = "runner-1") => new("key", name, null, true, false, false);

    [Fact]
    public async Task No_argument_prints_the_held_models_one_per_line()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/me", AKey());
        h.Wire.Json("GET", "/api/runners/runner-1", RunnersCommandTests.ARunner(refusedModels: ["haiku", "fable"]));

        Assert.Equal(0, await new RunnersCommand(h.Cli).RunAsync(["refuse"], default));

        Assert.Equal("haiku\nfable", h.Said);
    }

    [Fact]
    public async Task No_argument_with_nothing_held_prints_the_sentence()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/me", AKey());
        h.Wire.Json("GET", "/api/runners/runner-1", RunnersCommandTests.ARunner());

        Assert.Equal(0, await new RunnersCommand(h.Cli).RunAsync(["refuse"], default));

        Assert.Equal(RunnersCommand.RefusesNone, h.Said);
    }

    [Fact]
    public async Task Adding_a_model_reads_the_held_list_and_sends_the_whole_thing()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/me", AKey());
        h.Wire.Json("GET", "/api/runners/runner-1", RunnersCommandTests.ARunner(refusedModels: ["haiku"]));
        h.Wire.Json("PUT", "/api/runners/runner-1/refused-models", RunnersCommandTests.ARunner(refusedModels: ["haiku", "fable"]));

        Assert.Equal(0, await new RunnersCommand(h.Cli).RunAsync(["refuse", "fable"], default));

        var put = Assert.Single(h.Wire.To("PUT", "/api/runners/runner-1/refused-models"));
        Assert.Equal(new[] { "haiku", "fable" }, put.Read<RunnerRefusedModelsWriteRequest>().Models);
        Assert.Equal("haiku\nfable", h.Said);
    }

    [Fact]
    public async Task Lifting_a_held_model_sends_the_remainder()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/me", AKey());
        h.Wire.Json("GET", "/api/runners/runner-1", RunnersCommandTests.ARunner(refusedModels: ["haiku", "fable"]));
        h.Wire.Json("PUT", "/api/runners/runner-1/refused-models", RunnersCommandTests.ARunner(refusedModels: ["fable"]));

        Assert.Equal(0, await new RunnersCommand(h.Cli).RunAsync(["refuse", "--lift", "haiku"], default));

        var put = Assert.Single(h.Wire.To("PUT", "/api/runners/runner-1/refused-models"));
        Assert.Equal(new[] { "fable" }, put.Read<RunnerRefusedModelsWriteRequest>().Models);
        Assert.Equal("fable", h.Said);
    }

    [Fact]
    public async Task Lifting_a_model_not_held_complains_and_sends_nothing()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/me", AKey());
        h.Wire.Json("GET", "/api/runners/runner-1", RunnersCommandTests.ARunner(refusedModels: ["haiku"]));

        Assert.Equal(1, await new RunnersCommand(h.Cli).RunAsync(["refuse", "--lift", "fable"], default));

        Assert.Contains("fable is not on the list - nothing written", h.Complained);
        Assert.Empty(h.Wire.To("PUT", "/api/runners/runner-1/refused-models"));
    }

    [Fact]
    public async Task A_model_the_normaliser_refuses_is_refused_before_anything_is_sent()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/me", AKey());

        Assert.Equal(1, await new RunnersCommand(h.Cli).RunAsync(["refuse", "not-a-model"], default));

        Assert.Contains("hatch:", h.Complained);
        Assert.Empty(h.Wire.To("GET", "/api/runners/runner-1"));
    }

    [Fact]
    public async Task A_comma_inside_one_argument_is_refused_the_way_config_models_refuses_it()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/me", AKey());

        Assert.Equal(1, await new RunnersCommand(h.Cli).RunAsync(["refuse", "haiku,fable"], default));

        Assert.Empty(h.Wire.To("GET", "/api/runners/runner-1"));
    }

    [Fact]
    public async Task Lift_with_no_model_is_usage()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new RunnersCommand(h.Cli).RunAsync(["refuse", "--lift"], default));

        Assert.Contains("--lift needs a model", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task An_unknown_option_is_usage()
    {
        using var h = new CliHarness();

        Assert.Equal(1, await new RunnersCommand(h.Cli).RunAsync(["refuse", "--nonsense"], default));

        Assert.Contains("runners refuse does not take \"--nonsense\"", h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_call_with_no_key_is_refused_and_calls_the_wire_zero_times()
    {
        using var h = new CliHarness(key: "");

        Assert.Equal(1, await new RunnersCommand(h.Cli).RunAsync(["refuse"], default));

        Assert.Contains(RunnersCommand.KeylessRefusal, h.Complained);
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_call_that_is_not_a_key_is_refused()
    {
        using var h = new CliHarness();
        h.Wire.Json("GET", "/api/me", new MeDto("person", "Nathan", null, true, true, true));

        Assert.Equal(1, await new RunnersCommand(h.Cli).RunAsync(["refuse"], default));

        Assert.Contains(RunnersCommand.NotAKeyRefusal, h.Complained);
    }
}

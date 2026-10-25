using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>
/// The loop: what a pass does in what order, what it waits for, and what it
/// stops for.
/// </summary>
public sealed class GoToWorkTests
{
    private const string Queue = "/api/hatch/work/queue";
    private const string Review = "/api/hatch/work/review";

    private static string Held(string runner) =>
        $"\"hatch is working this from {runner}, last heard from 8 seconds ago\"";

    /// <summary>A board with one clear ticket on it, and everything an increment on it reads.</summary>
    private static Guid OneTicket(Harness h, string key = "AER-1")
    {
        var token = Guid.NewGuid();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row(key) });
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", $"/api/hatch/issues/{key}/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", $"/api/hatch/work/{key}", Fixtures.Work(key, from: "In Review"));
        h.Wire.Json("POST", $"/api/hatch/issues/{key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/hatch/issues/{key}/questions", Array.Empty<QuestionDto>());

        return token;
    }

    [Fact]
    public async Task A_pass_claims_before_it_resets_the_workspace_and_before_it_spawns()
    {
        using var h = new Harness();
        OneTicket(h);

        // What had already happened by the time the tree was asked to make
        // itself current. A ticket held is a ticket nothing else will start, and
        // a reset before the claim would be a fetch spent on an increment that
        // never happens.
        var claimedByThen = false;
        var spawnedByThen = false;
        h.Workspace.Watching = () =>
        {
            claimedByThen = h.Wire.Count("POST", "/api/hatch/issues/AER-1/claim") == 1;
            spawnedByThen = h.Sessions.Spawned.Count > 0;
        };

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.True(claimedByThen, "the ticket was claimed before the workspace was reset");
        Assert.False(spawnedByThen, "nothing was spawned before the workspace was reset");
        Assert.Single(h.Workspace.Prepared);
        Assert.Single(h.Sessions.Spawned);

        // ...and let go of when the increment ended.
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
    }

    /// <summary>
    /// HA-118: the claim's own verdict rides along with the release - dropped
    /// for the ticket a pass leaves where it found it, so the board's own
    /// count of consecutive let-goes is what decides whether the next one is
    /// let go quietly again or flagged.
    /// </summary>
    [Fact]
    public async Task A_pass_that_leaves_its_ticket_where_it_found_it_releases_the_claim_dropped()
    {
        using var h = new Harness();
        OneTicket(h);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        var released = Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains("outcome=dropped", released.Query, StringComparison.Ordinal);
    }

    /// <summary>The same release, for a pass that actually moved the ticket.</summary>
    [Fact]
    public async Task A_pass_that_moves_its_ticket_releases_the_claim_worked()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);
        // The dispatch starts in progress; the post-run read (the second call
        // to this route - the first is the pick itself) says it landed in
        // review, the ordinary shape of an increment that did its job.
        h.Wire.Once(
            "GET", "/api/hatch/work/AER-1", HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(Fixtures.Work("AER-1", from: "In Progress", to: "In Review"), Fixtures.Json));
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review", to: "In Review"));
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        var released = Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains("outcome=worked", released.Query, StringComparison.Ordinal);
    }

    /// <summary>The playbook's limit reaches the session a go-to-work pass spawns - HA-453.</summary>
    [Fact]
    public async Task A_pass_carries_the_playbooks_denied_tools_to_the_spawned_session()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);
        h.Wire.Json(
            "GET", "/api/hatch/work/AER-1",
            Fixtures.Work("AER-1", from: "In Review") with { Playbook = Fixtures.Playbook(deniedTools: ["Bash", "WebFetch"]) });
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Equal(["Bash", "WebFetch"], Assert.Single(h.Sessions.Spawned).DeniedTools);
    }

    [Fact]
    public async Task A_tree_that_will_not_reset_ends_the_night_and_gives_the_ticket_back()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Workspace.Answer = Reset.Never;

        await new GoToWorkCommand(h.Runtime).RunAsync([], default);

        // Nothing was spawned onto a tree the loop could not make current, and
        // the lease did not sit there until its TTL.
        Assert.Empty(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains(h.Say.Said, l => l.Contains("the workspace could not be reset", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_fetch_that_did_not_answer_is_a_wait_and_the_ticket_still_goes_back()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Workspace.Answer = Reset.Later;

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Empty(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains(h.Say.Complained, l => l.Contains("the workspace is not ready", StringComparison.Ordinal));
    }

    // ---- --mine ----

    [Fact]
    public async Task Mine_is_sent_on_the_queue_read()
    {
        using var h = new Harness();
        OneTicket(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--mine"], default);

        Assert.Contains("mine=true", h.Wire.To("GET", Queue).Single().Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_flag_mine_is_never_sent()
    {
        using var h = new Harness();
        OneTicket(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.DoesNotContain("mine=true", h.Wire.To("GET", Queue).Single().Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_heartbeat_carries_mine()
    {
        using var h = new Harness();
        OneTicket(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--mine"], default);

        var beat = h.Wire.Calls
            .Single(c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}")
            .Read<RunnerHeartbeatRequest>();
        Assert.True(beat.Mine);
    }

    [Fact]
    public async Task A_loop_beat_carries_the_tallys_runs_and_spend_and_this_machine()
    {
        using var h = new Harness();
        OneTicket(h);
        Carrying(h, new NightState { Restarts = 1, Runs = 3, Spent = 1.25m });
        h.Wire.Json("POST", Beat, new RunnerInstructionDto("running", null, 4, null, null));

        Assert.Equal(0, await new GoToWorkCommand(h.Supervised).RunAsync(["--max-runs", "4"], default));

        var first = Beats(h)[0];
        Assert.Equal(3, first.Runs);
        Assert.Equal(1.25m, first.Spent);
        Assert.Equal(Checkout.Host(), first.Host);
    }

    [Fact]
    public async Task A_once_beat_carries_this_machine_but_not_runs_or_spend()
    {
        using var h = new Harness();
        OneTicket(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        var beat = Assert.Single(Beats(h));
        Assert.Equal(Checkout.Host(), beat.Host);
        Assert.Null(beat.Runs);
        Assert.Null(beat.Spent);
    }

    [Fact]
    public async Task The_heartbeat_carries_standing()
    {
        using var h = new Harness();
        OneTicket(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        var beat = h.Wire.Calls
            .Single(c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}")
            .Read<RunnerHeartbeatRequest>();
        Assert.True(beat.Standing);
    }

    [Fact]
    public async Task The_heartbeat_carries_this_runners_build_identity()
    {
        using var h = new Harness();
        OneTicket(h);
        var builtAt = DateTimeOffset.Parse("2026-01-02T03:04:05Z");
        var runtime = h.Runtime with
        {
            CliRevision = new FakeCliRevision("b465267d5809bc0c672d250a1e7cc81b8714ed92", 4127, builtAt),
        };

        await new GoToWorkCommand(runtime).RunAsync(["--once"], default);

        var beat = h.Wire.Calls
            .Single(c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}")
            .Read<RunnerHeartbeatRequest>();
        Assert.Equal("b465267d5809bc0c672d250a1e7cc81b8714ed92", beat.Revision);
        Assert.Equal(4127, beat.Sequence);
        Assert.Equal(builtAt, beat.BuiltAt);
    }

    [Fact]
    public async Task An_unstamped_build_reports_dev_on_the_heartbeat()
    {
        using var h = new Harness();
        OneTicket(h);
        var runtime = h.Runtime with { CliRevision = new FakeCliRevision() };

        await new GoToWorkCommand(runtime).RunAsync(["--once"], default);

        var beat = h.Wire.Calls
            .Single(c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}")
            .Read<RunnerHeartbeatRequest>();
        Assert.Equal("dev", beat.Revision);
        Assert.Equal(0, beat.Sequence);
    }

    [Fact]
    public async Task Inside_a_checkout_of_its_own_source_the_heartbeat_carries_the_newest_commit_touching_it()
    {
        using var h = new Harness();
        OneTicket(h);
        Directory.CreateDirectory(Path.Combine(h.Runtime.Root, "src", "Hatch.Cli"));
        h.SourceRevision.Info = new SourceRevisionInfo("b465267d5809bc0c672d250a1e7cc81b8714ed92", 4127);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        var beat = h.Wire.Calls
            .Single(c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}")
            .Read<RunnerHeartbeatRequest>();
        Assert.Equal("b465267d5809bc0c672d250a1e7cc81b8714ed92", beat.SourceRevision);
        Assert.Equal(4127, beat.SourceSequence);
    }

    [Fact]
    public async Task Outside_a_checkout_of_its_own_source_the_heartbeat_sends_neither_field()
    {
        using var h = new Harness();
        OneTicket(h);
        h.SourceRevision.Info = new SourceRevisionInfo("b465267d5809bc0c672d250a1e7cc81b8714ed92", 4127);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        var beat = h.Wire.Calls
            .Single(c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}")
            .Read<RunnerHeartbeatRequest>();
        Assert.Null(beat.SourceRevision);
        Assert.Null(beat.SourceSequence);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_heartbeat_and_the_queue_read_carry_the_tags(bool tagged)
    {
        string[] tags = tagged ? ["gpu", "linux"] : [];
        using var h = new Harness(tags: tags);
        OneTicket(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        var beat = h.Wire.Calls
            .Single(c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}")
            .Read<RunnerHeartbeatRequest>();
        Assert.NotNull(beat.Tags);
        Assert.Equal(tags, beat.Tags);

        var call = h.Wire.To("GET", Queue)[0];
        Assert.Contains("remote=", call.Query);
        if (tagged)
        {
            Assert.Contains("tag=gpu", call.Query);
            Assert.Contains("tag=linux", call.Query);
        }
        else
        {
            Assert.DoesNotContain("tag=", call.Query);
        }
    }

    /// <summary>
    /// One pass of a loop that is not <c>--once</c>, answered with <paramref name="told"/>
    /// - the only shape in which a heartbeat answer is applied back. MaxRuns
    /// echoes the flag so the answer ends the loop and says no <c>--max-runs</c>.
    /// </summary>
    private static async Task<Harness> OnePassTold(string[] started, IReadOnlyList<string>? told)
    {
        var h = new Harness(tags: started);
        OneTicket(h);
        h.Wire.Json("POST", $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}",
            new RunnerInstructionDto("running", null, 1, null, null, Tags: told));

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "1"], default));
        return h;
    }

    [Fact]
    public async Task The_board_s_tags_replace_the_loops_own_and_the_next_read_carries_them()
    {
        using var h = await OnePassTold([], ["gpu"]);

        Assert.All(h.Wire.To("GET", Queue), c => Assert.Contains("tag=gpu", c.Query));
        Assert.Single(h.Say.Said, l => l == "hatch: the board set --tag gpu");
        Assert.Equal(new[] { "gpu" }, h.Runtime.Board.Tags);
    }

    [Fact]
    public async Task The_board_clearing_the_tags_clears_the_next_read()
    {
        using var h = await OnePassTold(["gpu"], []);

        Assert.All(h.Wire.To("GET", Queue), c => Assert.DoesNotContain("tag=", c.Query));
        Assert.Single(h.Say.Said, l => l == "hatch: the board set --tag off");
        Assert.Empty(h.Runtime.Board.Tags);
    }

    [Fact]
    public async Task An_older_hatch_that_answers_no_tags_leaves_them_alone()
    {
        using var h = await OnePassTold(["linux"], null);

        Assert.All(h.Wire.To("GET", Queue), c => Assert.Contains("tag=linux", c.Query));
        Assert.Equal(new[] { "linux" }, h.Runtime.Board.Tags);
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("--tag", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_board_whose_tags_match_the_loops_says_nothing()
    {
        using var h = await OnePassTold(["gpu"], ["gpu"]);

        Assert.All(h.Wire.To("GET", Queue), c => Assert.Contains("tag=gpu", c.Query));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("--tag", StringComparison.Ordinal));
    }

    // ---- --project: the projects a runner is bound to ----

    private static RunnerHeartbeatRequest OnlyBeat(Harness h) =>
        h.Wire.To("POST", Beat).Single().Read<RunnerHeartbeatRequest>();

    [Fact]
    public async Task Project_flags_ride_the_heartbeat_and_the_queue_read_upper_cased()
    {
        using var h = new Harness();
        OneTicket(h);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(
            ["--once", "--project", "FIL", "--project", "aer", "--project", "fil"], default));

        Assert.Equal(new[] { "FIL", "AER" }, OnlyBeat(h).Projects);
        var query = h.Wire.To("GET", Queue)[0].Query;
        Assert.Contains("project=FIL", query);
        Assert.Contains("project=AER", query);
    }

    [Fact]
    public async Task No_project_flag_sends_no_projects_and_reads_with_none()
    {
        using var h = new Harness();
        OneTicket(h);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Null(OnlyBeat(h).Projects);
        Assert.DoesNotContain("project=", h.Wire.To("GET", Queue)[0].Query);
    }

    [Theory]
    [InlineData("fil1x9z")]
    [InlineData("")]
    [InlineData("1AB")]
    public async Task A_malformed_project_is_refused_before_anything_is_sent(string bad)
    {
        using var h = new Harness();

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--project", bad], default));

        Assert.Contains(h.Say.Complained, l => l.Contains("names a project, like AER", StringComparison.Ordinal));
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_project_flag_with_no_value_is_refused()
    {
        using var h = new Harness();

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["--project"], default));

        Assert.Contains(h.Say.Complained, l => l.Contains("--project takes a project key", StringComparison.Ordinal));
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_project_flag_survives_the_board_being_rebuilt_for_a_repo_override()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());
        var tree = Tree(h, "bound", "https://example.test/bound.git");

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(
            ["--once", "--repo", tree, "--project", "FIL"], default));

        Assert.Contains("project=FIL", h.Wire.To("GET", Queue)[0].Query);
        var beat = h.Wire.Calls.Single(c => c.Method == "POST" && c.Path.StartsWith("/api/hatch/runners/", StringComparison.Ordinal));
        Assert.Equal(new[] { "FIL" }, beat.Read<RunnerHeartbeatRequest>().Projects);
    }

    /// <summary>
    /// <see cref="OnePassTold"/> for the projects: a loop started with
    /// <paramref name="started"/> flags, answered with <paramref name="told"/>.
    /// </summary>
    private static async Task<Harness> OnePassToldProjects(string[] started, IReadOnlyList<string>? told)
    {
        var h = new Harness();
        OneTicket(h);
        h.Wire.Json("POST", Beat, new RunnerInstructionDto("running", null, 1, null, null, Projects: told));

        string[] args = ["--max-runs", "1", .. started.SelectMany(p => new[] { "--project", p })];
        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(args, default));
        return h;
    }

    [Fact]
    public async Task The_board_s_projects_replace_the_loops_own_and_the_next_read_carries_them()
    {
        using var h = await OnePassToldProjects([], ["FIL", "AER"]);

        Assert.Single(h.Say.Said, l => l == "hatch: the board set --project FIL AER");
        Assert.Equal(new[] { "FIL", "AER" }, h.Runtime.Board.Projects);
        var reads = h.Wire.To("GET", Queue);
        Assert.Contains("project=FIL", reads[^1].Query);
        Assert.Contains("project=AER", reads[^1].Query);
    }

    [Fact]
    public async Task The_board_clearing_the_projects_clears_the_next_read()
    {
        using var h = await OnePassToldProjects(["FIL"], []);

        Assert.Single(h.Say.Said, l => l == "hatch: the board set --project off");
        Assert.Empty(h.Runtime.Board.Projects);
        Assert.DoesNotContain("project=", h.Wire.To("GET", Queue)[^1].Query);
    }

    [Fact]
    public async Task A_board_whose_projects_match_the_loops_says_nothing()
    {
        using var h = await OnePassToldProjects(["FIL", "AER"], ["FIL", "AER"]);

        Assert.DoesNotContain(h.Say.Said, l => l.Contains("--project", StringComparison.Ordinal));
        Assert.All(h.Wire.To("GET", Queue), c => Assert.Contains("project=FIL", c.Query));
    }

    [Fact]
    public async Task An_older_hatch_that_answers_no_projects_leaves_them_alone()
    {
        using var h = await OnePassToldProjects(["FIL"], null);

        Assert.DoesNotContain(h.Say.Said, l => l.Contains("--project", StringComparison.Ordinal));
        Assert.Equal(new[] { "FIL" }, h.Runtime.Board.Projects);
        Assert.All(h.Wire.To("GET", Queue), c => Assert.Contains("project=FIL", c.Query));
    }

    // ---- A fresh loop says so on its first answered beat ----

    private static IReadOnlyList<RunnerHeartbeatRequest> Beats(Harness h) =>
        [.. h.Wire.To("POST", Beat).Select(c => c.Read<RunnerHeartbeatRequest>())];

    [Fact]
    public async Task A_loop_with_no_carried_state_sends_fresh_on_its_first_beat_with_its_bounds()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Wire.Json("POST", Beat, new RunnerInstructionDto("running", null, 1, 20m, null));

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--max-spend", "20", "--max-runs", "1"], default));

        var first = Beats(h)[0];
        Assert.True(first.Fresh);
        Assert.Equal(20m, first.MaxSpend);
        Assert.Equal(1, first.MaxRuns);
        Assert.DoesNotContain(h.Say.Said, l => l.StartsWith("hatch: the board set", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_loop_restarted_with_carried_state_never_sends_fresh()
    {
        using var h = new Harness();
        OneTicket(h);
        Carrying(h, new NightState { Restarts = 1 });
        h.Wire.Json("POST", Beat, new RunnerInstructionDto("running", null, 1, null, null));

        Assert.Equal(0, await new GoToWorkCommand(h.Supervised).RunAsync(["--max-runs", "1"], default));

        Assert.NotEmpty(Beats(h));
        Assert.All(Beats(h), b => Assert.NotEqual(true, b.Fresh));
    }

    [Fact]
    public async Task Only_the_first_answered_beat_of_a_fresh_loop_is_fresh()
    {
        using var h = new Harness();
        OneTicket(h, "AER-1");
        OneTicket(h, "AER-2");
        h.Wire.Json("POST", Beat, new RunnerInstructionDto("running", null, 2, null, null));

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "2"], default));

        var beats = Beats(h);
        Assert.True(beats.Count >= 2);
        Assert.True(beats[0].Fresh);
        Assert.All(beats.Skip(1), b => Assert.NotEqual(true, b.Fresh));
    }

    [Fact]
    public async Task A_once_beat_is_never_fresh()
    {
        using var h = new Harness();
        OneTicket(h);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.All(Beats(h), b => Assert.NotEqual(true, b.Fresh));
    }

    private const string StandingBy = "hatch: standing by - the board's Go to Work starts this runner";

    [Fact]
    public async Task A_standby_start_sends_standby_on_its_first_beat_only()
    {
        using var h = new Harness();
        OneTicket(h, "AER-1");
        OneTicket(h, "AER-2");
        h.Wire.Json("POST", Beat, new RunnerInstructionDto("running", null, 2, null, null));

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--standby", "--max-runs", "2"], default));

        var beats = Beats(h);
        Assert.True(beats.Count >= 2);
        Assert.True(beats[0].Fresh);
        Assert.True(beats[0].Standby);
        Assert.All(beats.Skip(1), b => Assert.Null(b.Standby));
    }

    [Fact]
    public async Task A_standby_start_says_it_is_standing_by_once()
    {
        using var h = new Harness();
        OneTicket(h, "AER-1");
        OneTicket(h, "AER-2");
        h.Wire.Json("POST", Beat, new RunnerInstructionDto("running", null, 2, null, null));

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--standby", "--max-runs", "2"], default));

        Assert.Single(h.Say.Said, l => l == StandingBy);
    }

    [Fact]
    public async Task A_restarted_standby_loop_sends_no_standby_and_says_nothing()
    {
        using var h = new Harness();
        OneTicket(h);
        Carrying(h, new NightState { Restarts = 1 });
        h.Wire.Json("POST", Beat, new RunnerInstructionDto("running", null, 1, null, null));

        Assert.Equal(0, await new GoToWorkCommand(h.Supervised).RunAsync(["--standby", "--max-runs", "1"], default));

        Assert.NotEmpty(Beats(h));
        Assert.All(Beats(h), b => Assert.Null(b.Standby));
        Assert.All(Beats(h), b => Assert.NotEqual(true, b.Fresh));
        Assert.DoesNotContain(h.Say.Said, l => l == StandingBy);
    }

    [Fact]
    public async Task Standby_with_once_is_refused_before_any_beat()
    {
        using var h = new Harness();
        OneTicket(h);

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["--standby", "--once"], default));

        Assert.Contains(h.Say.Complained, l => l.Contains("--standby", StringComparison.Ordinal) && l.Contains("--once", StringComparison.Ordinal));
        Assert.Empty(Beats(h));
    }

    [Fact]
    public async Task Without_standby_the_beat_carries_none()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Wire.Json("POST", Beat, new RunnerInstructionDto("running", null, 1, null, null));

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "1"], default));

        Assert.NotEmpty(Beats(h));
        Assert.All(Beats(h), b => Assert.Null(b.Standby));
        Assert.DoesNotContain(h.Say.Said, l => l == StandingBy);
    }

    [Fact]
    public async Task A_standby_start_answered_paused_takes_no_ticket()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Wire.Json("POST", Beat, new RunnerInstructionDto("paused", null, null, null, null));

        using var cts = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Runtime).RunAsync(["--standby", "--interval", "3600"], cts.Token);

        await Harness.Eventually(() => h.Wire.Count("POST", Beat) > 0, "the first heartbeat");
        await Harness.Eventually(
            () => h.Say.Said.Any(l => l.Contains("the board has this runner paused", StringComparison.Ordinal)),
            "the board's paused line");
        await Task.Delay(50);

        Assert.True(Beats(h)[0].Standby);
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));

        cts.Cancel();
        await running;
    }

    [Fact]
    public async Task The_heartbeat_carries_standing_false_when_no_checkout_is_standing()
    {
        using var h = new Harness();
        OneTicket(h);
        var path = h.Runtime.Checkouts[0].Path;
        var runtime = h.Runtime with { Checkouts = [new CheckoutEntry(path, "https://example.test/repo.git", Standing: false)] };

        await new GoToWorkCommand(runtime).RunAsync(["--once"], default);

        var beat = h.Wire.Calls
            .Single(c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}")
            .Read<RunnerHeartbeatRequest>();
        Assert.False(beat.Standing);
    }

    [Fact]
    public async Task The_heartbeat_carries_the_readouts_reading()
    {
        using var h = new Harness();
        OneTicket(h);
        var readAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        h.Runtime.Readout.SetUsage([new UsageWindow("session", "Session", 0.42, null)], readAt);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        var beat = h.Wire.Calls
            .Single(c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}")
            .Read<RunnerHeartbeatRequest>();
        Assert.Equal(readAt, beat.UsageReadAt);
        var window = Assert.Single(beat.Usage!);
        Assert.Equal("session", window.Window);
        Assert.Equal("Session", window.Label);
        Assert.Equal(42, window.Percent);
    }

    [Fact]
    public async Task A_loop_with_no_reading_yet_sends_none()
    {
        using var h = new Harness();
        OneTicket(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        var beat = h.Wire.Calls
            .Single(c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}")
            .Read<RunnerHeartbeatRequest>();
        Assert.Null(beat.Usage);
        Assert.Null(beat.UsageReadAt);
    }

    // ---- The one row that outranked the ticket about to be spawned (HA-369) ----

    /// <summary>A board with one row folded above the spawned ticket's priority, plus the clear row <see cref="OneTicket"/> wires.</summary>
    private static Guid WithRowAbove(
        Harness h, string blockedKey, string blockedSentence, string blockedPriority, string key = "AER-1")
    {
        var token = Guid.NewGuid();

        h.Wire.Json("GET", Queue, new[]
        {
            Fixtures.Row(blockedKey, blockedSentence, priority: blockedPriority),
            Fixtures.Row(key),
        });
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", $"/api/hatch/issues/{key}/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", $"/api/hatch/work/{key}", Fixtures.Work(key, from: "In Review"));
        h.Wire.Json("POST", $"/api/hatch/issues/{key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/hatch/issues/{key}/questions", Array.Empty<QuestionDto>());

        return token;
    }

    [Fact]
    public async Task A_row_above_the_spawned_priority_is_named_on_its_own_ungrouped_line()
    {
        using var h = new Harness();
        const string sentence = "bound to a repository, and this runner has no checkout of it";
        WithRowAbove(h, "AER-2", sentence, PriorityLevels.EmergencyName);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Contains(h.Say.Said, l => l.Contains($"AER-2  {sentence}", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains($"1  {sentence}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_row_at_the_spawned_tickets_own_priority_still_goes_through_the_grouped_digest()
    {
        using var h = new Harness();
        const string aboveSentence = "bound to a repository, and this runner has no checkout of it";
        const string sameSentence = "AER-9 has not merged";
        WithRowAbove(h, "AER-2", aboveSentence, PriorityLevels.EmergencyName);
        h.Wire.Replace("GET", Queue, HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(
            new[]
            {
                Fixtures.Row("AER-2", aboveSentence, priority: PriorityLevels.EmergencyName),
                Fixtures.Row("AER-3", sameSentence, priority: PriorityLevels.NormalName),
                Fixtures.Row("AER-1"),
            },
            Fixtures.Json));

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Contains(h.Say.Said, l => l.Contains($"AER-2  {aboveSentence}", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains($"1  {sameSentence}", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("folded past 2 issue(s)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nothing_above_the_spawned_priority_prints_exactly_what_today_prints()
    {
        using var h = new Harness();
        const string sentence = "AER-2 has not merged";
        h.Wire.Json("GET", Queue, new[]
        {
            Fixtures.Row("AER-2", sentence, priority: PriorityLevels.LowName),
            Fixtures.Row("AER-1"),
        });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Contains(h.Say.Said, l => l.Contains($"1  {sentence}", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains($"AER-2  {sentence}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_folded_above_sentence_reaches_the_next_beat_not_only_the_terminal()
    {
        using var h = new Harness();
        const string sentence = "bound to a repository, and this runner has no checkout of it";
        WithRowAbove(h, "AER-2", sentence, PriorityLevels.EmergencyName);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "2"], default);

        var beats = h.Wire.Calls
            .Where(c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}")
            .ToList();
        Assert.True(beats.Count >= 2, "at least two beats were sent");
        Assert.Equal($"AER-2  {sentence}", beats[1].Read<RunnerHeartbeatRequest>().Line);
    }

    // ---- The usage probe - HA-173 ----

    /// <summary>
    /// A session runner that can also answer the CLI's own <c>/usage</c> - what
    /// the sealed <see cref="FakeSessions"/> is not, since most facts in this
    /// file never ask it. Delegates every ordinary spawn to one, and mimics
    /// <see cref="ClaudeSessionRunner"/>'s own guard: once a probe is told it
    /// cost a turn, every later call is a free no-op.
    /// </summary>
    private sealed class FakeProbingSessions : ISessionRunner, IUsageProbe
    {
        public FakeSessions Inner { get; } = new();

        /// <summary>Every temp directory a probe was asked to run in, in order - so a test can assert how many times, and only that.</summary>
        public List<string> Probed { get; } = [];

        /// <summary>What the next probe answers with. Null is a probe that answered nothing.</summary>
        public Func<IReadOnlyList<string>?> Behaviour { get; set; } = () => null;

        private bool _costly;

        public Task<IReadOnlyList<string>?> ProbeAsync(string tempDirectory, CancellationToken ct)
        {
            Probed.Add(tempDirectory);
            if (_costly) return Task.FromResult<IReadOnlyList<string>?>(null);

            var lines = Behaviour();
            if (lines is not null && UsageReport.Turns(lines) > 0)
            {
                _costly = true;
                return Task.FromResult<IReadOnlyList<string>?>(null);
            }

            return Task.FromResult(lines);
        }

        public Task<SessionResult> RunAsync(SessionRequest request, Action<string>? onLine, CancellationToken ct) =>
            Inner.RunAsync(request, onLine, ct);

        public Task<int> AttachAsync(SessionRequest request, CancellationToken ct) => Inner.AttachAsync(request, ct);

        public bool CanSpawn(out string refusal) => Inner.CanSpawn(out refusal);
    }

    [Fact]
    public async Task A_probes_reading_is_what_the_beat_carries()
    {
        using var h = new Harness();
        OneTicket(h);
        var fake = new FakeProbingSessions
        {
            Behaviour = () => [Fixtures.UsageReport([("session", 42, null)]), Fixtures.Result(turns: 0)],
        };
        var runtime = h.Runtime with { Sessions = fake };

        await new GoToWorkCommand(runtime).RunAsync(["--once"], default);

        var beat = h.Wire.Calls
            .Single(c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}")
            .Read<RunnerHeartbeatRequest>();
        var window = Assert.Single(beat.Usage!);
        Assert.Equal("session", window.Window);
        Assert.Equal(42, window.Percent);
        Assert.Single(fake.Probed);

        // HA-1457: the caller says whether the probe's own answer carried a
        // session window, every time it asks.
        Assert.Contains(h.Say.Said, l => l.Contains("probe found a session window", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_probe_leaves_the_streams_reading_on_the_beat()
    {
        using var h = new Harness();
        OneTicket(h);
        var fake = new FakeProbingSessions { Behaviour = () => null };
        var runtime = h.Runtime with { Sessions = fake };
        var readAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        runtime.Readout.SetUsage([new UsageWindow("weekly", "Weekly", 0.10, null)], readAt);

        await new GoToWorkCommand(runtime).RunAsync(["--once"], default);

        var beat = h.Wire.Calls
            .Single(c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}")
            .Read<RunnerHeartbeatRequest>();
        Assert.Equal(readAt, beat.UsageReadAt);
        var window = Assert.Single(beat.Usage!);
        Assert.Equal("weekly", window.Window);
        Assert.Equal(10, window.Percent);

        // HA-1457: a probe that answered nothing says why, once, rather than
        // leaving a reader to guess from the readout alone.
        Assert.Contains(h.Say.Said, l => l.Contains("probe found no session window", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("probe returned nothing", StringComparison.Ordinal));
    }

    /// <summary>
    /// An idle loop - nothing ever claimed - probes once and then leaves the
    /// reading alone for two minutes, rather than asking again every
    /// <c>--interval</c>.
    /// </summary>
    [Fact]
    public async Task No_second_probe_inside_two_minutes_on_an_idle_loop()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());
        var fake = new FakeProbingSessions
        {
            Behaviour = () => [Fixtures.UsageReport([("session", 5, null)]), Fixtures.Result(turns: 0)],
        };
        var runtime = h.Runtime with { Sessions = fake };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2_500));
        await new GoToWorkCommand(runtime).RunAsync(["--interval", "1"], cts.Token);

        Assert.Single(fake.Probed);
    }

    /// <summary>
    /// Pinned to <see cref="GoToWorkCommand.Pass.Worked"/>: a session's own
    /// stream has just replaced the readout with whatever it carried, so the
    /// very next pass probes again however fresh the last one still is.
    /// </summary>
    [Fact]
    public async Task A_probe_runs_after_every_increment_however_recent_the_last_one()
    {
        using var h = new Harness();
        OneTicket(h);
        var fake = new FakeProbingSessions
        {
            Behaviour = () => [Fixtures.UsageReport([("session", 5, null)]), Fixtures.Result(turns: 0)],
        };
        var runtime = h.Runtime with { Sessions = fake };

        await new GoToWorkCommand(runtime).RunAsync(["--max-runs", "2"], default);

        // Two increments run, plus the top of the iteration that then finds
        // --max-runs satisfied and stops before claiming a third - the probe
        // runs before that discovery, not after it.
        Assert.Equal(3, fake.Probed.Count);
    }

    /// <summary>
    /// The guard belongs to the runner, not the loop: the loop keeps checking
    /// in every pass it is due, but a probe that has already cost a turn once
    /// answers nothing for the rest of the process, never handing the loop a
    /// reading again.
    /// </summary>
    [Fact]
    public async Task A_probe_that_cost_a_turn_is_never_asked_again()
    {
        using var h = new Harness();
        OneTicket(h);
        var fake = new FakeProbingSessions
        {
            Behaviour = () => [Fixtures.UsageReport([("session", 99, null)]), Fixtures.Result(turns: 1)],
        };
        var runtime = h.Runtime with { Sessions = fake };

        await new GoToWorkCommand(runtime).RunAsync(["--max-runs", "2"], default);

        Assert.Equal(3, fake.Probed.Count);
        Assert.Empty(runtime.Readout.Snapshot().UsageWindows);
    }

    /// <summary>
    /// The same path "the workspace could not be reset" takes: nothing was
    /// spawned, so this ends the night without counting toward the
    /// three-failures tally - the sentence printed is the server's own, not
    /// "three increments in a row failed".
    /// </summary>
    [Fact]
    public async Task A_400_on_the_pick_ends_the_run_and_claims_nothing()
    {
        using var h = new Harness();
        h.Wire.Reply("GET", Queue, HttpStatusCode.BadRequest,
            "\"this key belongs to nobody, so it has no tickets of its own\"");

        await new GoToWorkCommand(h.Runtime).RunAsync(["--mine"], default);

        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Wire.Calls.Where(c => c.Path.EndsWith("/claim", StringComparison.Ordinal)));
        Assert.Contains(h.Say.Said, l => l.Contains("this key belongs to nobody", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("three increments in a row failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_busy_board_waits_rather_than_ending_and_does_not_read_like_an_empty_one()
    {
        using var h = new Harness();
        var rows = Enumerable.Range(1, 5).Select(n => Fixtures.Row($"AER-{n}")).ToArray();

        h.Wire.Json("GET", Queue, rows);
        foreach (var row in rows)
            h.Wire.Reply("POST", $"/api/hatch/issues/{row.Issue.Key}/claim", HttpStatusCode.Conflict,
                Held("other:/tree"));

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Contains(h.Say.Said, l => l.Contains("being worked by another runner", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("other:/tree", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("nothing on the board", StringComparison.Ordinal));

        // Nothing was spawned, and the tree was never touched for a pass that
        // had nothing to do.
        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Workspace.Prepared);
        Assert.Contains(h.Say.Said, l => l.Contains("--once, and the pass is done", StringComparison.Ordinal));
    }

    /// <summary>Claude's family and one more, so a row can be pinned to an agent the runner is not.</summary>
    private static readonly ModelFamilies TwoAgents = new([
        ModelFamilies.Default.Rows[0], new ModelFamily("fake-agent", "^fake-[a-z0-9-]+$", "fake-1")]);

    [Fact]
    public async Task A_board_whose_only_clear_row_is_pinned_to_another_agent_waits_and_says_why()
    {
        using var h = new Harness();
        var runtime = h.Runtime with { Families = TwoAgents };

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1", model: "fake-1") });

        await new GoToWorkCommand(runtime).RunAsync(["--once"], default);

        Assert.Contains(h.Say.Said, l => l.Contains("skipped - one this agent cannot take", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains(
            "AER-1  pinned to fake-1, a fake-agent model - this runner's agent is claude", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Wire.Calls, c => c.Path.EndsWith("/claim", StringComparison.Ordinal));
        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Workspace.Prepared);
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("nothing on the board", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("--once, and the pass is done", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_claimed_row_behind_a_skipped_one_prints_what_it_skipped_on_the_way()
    {
        using var h = new Harness();
        var runtime = h.Runtime with { Families = TwoAgents };
        OneTicket(h, "AER-2");
        h.Wire.Replace("GET", Queue, HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(
            new[] { Fixtures.Row("AER-1", model: "fake-1"), Fixtures.Row("AER-2") },
            Fixtures.Json));

        await new GoToWorkCommand(runtime).RunAsync(["--once"], default);

        Assert.Contains(h.Say.Said, l => l.Contains(
            "hatch:   skipped  AER-1  pinned to", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_empty_board_reads_like_an_empty_board()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Contains(h.Say.Said, l => l.Contains("nothing on the board is an agent's to move", StringComparison.Ordinal));

        // `--once` is one pass and out, so it does not promise to ask again.
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("waiting, and asking again", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rows_folded_for_their_tag_are_grouped_and_counted_on_the_servers_sentence()
    {
        using var h = new Harness();
        const string sentence = "AER-7 is implemented only by a runner advertising macos and xcode - this runner advertises linux";
        h.Wire.Json("GET", Queue, new[]
        {
            Fixtures.Row("AER-7", sentence),
            Fixtures.Row("AER-8", sentence),
            Fixtures.Row("AER-9", "AER-7 has not merged"),
        });
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Contains(h.Say.Said, l => l.Contains("3 issue(s) were on the dispatcher's path", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains($"2  {sentence}", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("1  AER-7 has not merged", StringComparison.Ordinal));
        Assert.Empty(h.Sessions.Spawned);
    }

    [Fact]
    public async Task A_night_with_no_CLI_to_spawn_ends_before_it_reads_the_board()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Sessions.Missing = "hatch: no claude CLI on PATH.";

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Contains(h.Say.Complained, l => l.Contains("no claude CLI on PATH", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("there is no claude CLI to spawn", StringComparison.Ordinal));
        Assert.Empty(h.Sessions.Spawned);

        // Nothing was read and nothing was claimed: the question is asked before
        // the board, because a night that cannot spend should not take a ticket
        // off it to find that out. The merge train starts before this check and
        // is no exception to it - merging an approved pull request needs no
        // Claude CLI - so its own independent read of review is not this
        // invariant's concern.
        Assert.Empty(h.Wire.Calls.Where(c => c.Path != Review));
    }

    /// <summary>The StopWhy sentence names whichever agent was asked for, not always claude.</summary>
    [Fact]
    public async Task A_night_with_no_grok_CLI_to_spawn_names_grok_rather_than_claude()
    {
        using var h = new Harness();
        OneTicket(h);
        var missing = Path.Combine(h.Temp, "nowhere", "grok");
        var runtime = h.Runtime with { Settings = h.Runtime.Settings with { GrokBin = missing } };

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--once", "--agent", "grok"], default));

        Assert.Contains(h.Say.Complained, l => l.Contains($"HATCH_GROK_BIN is not there: {missing}", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("there is no grok CLI to spawn", StringComparison.Ordinal));
        Assert.Empty(h.Wire.Calls.Where(c => c.Path != Review));
    }

    /// <summary>Ollama spawns through the codex binary, so a missing one refuses the same way codex itself does.</summary>
    [Fact]
    public async Task A_night_with_no_ollama_CLI_to_spawn_names_ollama_rather_than_claude()
    {
        using var h = new Harness();
        OneTicket(h);
        var missing = Path.Combine(h.Temp, "nowhere", "codex");
        var runtime = h.Runtime with { Settings = h.Runtime.Settings with { CodexBin = missing } };

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--once", "--agent", "ollama"], default));

        Assert.Contains(h.Say.Complained, l => l.Contains($"HATCH_CODEX_BIN is not there: {missing}", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("there is no ollama CLI to spawn", StringComparison.Ordinal));
        Assert.Empty(h.Wire.Calls.Where(c => c.Path != Review));
    }

    [Fact]
    public async Task An_interrupt_gives_the_ticket_back_and_still_prints_the_tally()
    {
        using var h = new Harness();
        OneTicket(h);
        using var interrupting = new CancellationTokenSource();

        h.Sessions.Behaviour = FakeSessions.UntilStopped();

        var running = new GoToWorkCommand(h.Runtime).RunAsync([], interrupting.Token);
        await h.Sessions.StartedWithin();
        await interrupting.CancelAsync();
        await running;

        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains(h.Say.Said, l => l.Contains("increment(s) in", StringComparison.Ordinal));
    }

    // ---- A pass that fails lets its ticket go and the night goes on (HA-115) ----

    [Fact]
    public async Task A_tree_that_cannot_be_prepared_lets_the_ticket_go_and_the_loop_takes_the_next_one()
    {
        using var h = new Harness();
        OneTicket(h, "AER-1");
        OneTicket(h, "AER-2");

        // Both clear at first. The board would not really offer AER-1 straight
        // back the moment its claim was released, so the swap below - made the
        // instant the first reset falls over - stands in for that: what matters
        // to this test is what the loop does next, not how the board gets there.
        h.Wire.Replace(
            "GET", Queue, HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-1"), Fixtures.Row("AER-2") }, Fixtures.Json));
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments", Fixtures.Comment());

        // Only the first reset throws - a git command that failed outright,
        // rather than the ordinary Reset.Never a bad tree answers with.
        var thrown = false;
        h.Workspace.Watching = () =>
        {
            if (!thrown)
            {
                thrown = true;
                h.Workspace.PrepareThrows = new InvalidOperationException("git blew up");
                h.Wire.Replace(
                    "GET", Queue, HttpStatusCode.OK,
                    System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-2") }, Fixtures.Json));
            }
            else h.Workspace.PrepareThrows = null;
        };

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "2"], default));

        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-2/claim"));
        Assert.Single(h.Sessions.Spawned);

        var comment = Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));
        Assert.Contains("git blew up", comment.Body);
        Assert.Contains(h.Say.Complained, l => l.Contains("AER-1", StringComparison.Ordinal) && l.Contains("git blew up", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_session_runner_that_throws_lets_the_ticket_go_and_puts_the_tree_back_on_the_trunk()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments", Fixtures.Comment());
        h.Sessions.Behaviour = (_, _, _) => throw new InvalidOperationException("the CLI fell over");

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));

        var comment = Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));
        Assert.Contains("the CLI fell over", comment.Body);

        // Back on the trunk - the same door a lost lease leaves through.
        Assert.Contains($"return {h.Root}", h.Workspace.Calls);
    }

    [Fact]
    public async Task Three_failed_passes_in_a_row_end_the_night_the_same_way_three_failed_sessions_do()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments", Fixtures.Comment());
        h.Sessions.Behaviour = (_, _, _) => throw new InvalidOperationException("the CLI fell over");

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync([], default));

        Assert.Contains(h.Say.Said, l => l.Contains("three increments in a row failed", StringComparison.Ordinal));
        Assert.Equal(3, h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim").Count);
        Assert.Equal(3, h.Wire.To("POST", "/api/hatch/issues/AER-1/comments").Count);
    }

    // ---- More than one checkout in play ----

    private static (CheckoutEntry Primary, CheckoutEntry Other, WorkRepositoryDto[] Repositories) TwoCheckouts(Harness h)
    {
        var other = Path.Combine(h.Temp, "other");
        Directory.CreateDirectory(other);

        var primary = h.Runtime.Checkouts[0];
        var entry = new CheckoutEntry(other, "https://example.test/other.git", Standing: false);

        var repos = new[]
        {
            Fixtures.Repository(primary.Remote!, primary: true, matchedRemote: primary.Remote),
            Fixtures.Repository(entry.Remote!, matchedRemote: entry.Remote),
        };

        return (primary, entry, repos);
    }

    [Fact]
    public async Task Two_checkouts_reset_in_order_before_one_spawn()
    {
        using var h = new Harness();
        var (primary, other, repos) = TwoCheckouts(h);
        var runtime = h.Runtime with { Checkouts = [primary, other] };

        var token = Guid.NewGuid();
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review", repositories: repos));
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--once"], default));

        Assert.Equal([primary.Path, other.Path], h.Workspace.Prepared);
        var spawned = Assert.Single(h.Sessions.Spawned);
        Assert.Equal(primary.Path, spawned.Root);
        Assert.Equal([other.Path], spawned.AddDirs);
    }

    [Fact]
    public async Task Never_on_the_second_checkout_ends_the_increment_with_nothing_spawned()
    {
        using var h = new Harness();
        var (primary, other, repos) = TwoCheckouts(h);
        var runtime = h.Runtime with { Checkouts = [primary, other] };
        h.Workspace.AnswerFor[other.Path] = Reset.Never;

        var token = Guid.NewGuid();
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", repositories: repos));

        await new GoToWorkCommand(runtime).RunAsync([], default);

        Assert.Equal([primary.Path, other.Path], h.Workspace.Prepared);
        Assert.Empty(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains(h.Say.Said, l => l.Contains("the workspace could not be reset", StringComparison.Ordinal));
    }

    // ---- --repo: checkouts a person names, rather than the one standing in ----

    private static string Tree(Harness h, string name, string origin)
    {
        var path = Path.Combine(h.Temp, name);
        Directory.CreateDirectory(path);
        Git(path, "init", "--quiet");
        Git(path, "remote", "add", "origin", origin);
        return path;
    }

    private static void Git(string dir, params string[] args)
    {
        var start = new ProcessStartInfo { FileName = "git", WorkingDirectory = dir, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        process.WaitForExit();
    }

    [Fact]
    public async Task Repo_twice_serves_three_checkouts_with_the_standing_one_first_and_declares_three_remotes()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        // The standing checkout's origin is re-read from git, same as every
        // other checkout's - Harness's own bare .git directory has none set,
        // so this test gives it the remote it already claims to have.
        Git(h.Root, "init", "--quiet");
        Git(h.Root, "remote", "add", "origin", h.Runtime.Checkouts[0].Remote!);

        var b = Tree(h, "b", "https://example.test/b.git");
        var c = Tree(h, "c", "https://example.test/c.git");

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--repo", b, "--repo", c], default));

        var call = Assert.Single(h.Wire.To("GET", Queue));
        Assert.Contains($"remote={Uri.EscapeDataString(h.Runtime.Checkouts[0].Remote!)}", call.Query);
        Assert.Contains($"remote={Uri.EscapeDataString("https://example.test/b.git")}", call.Query);
        Assert.Contains($"remote={Uri.EscapeDataString("https://example.test/c.git")}", call.Query);
        Assert.Contains("standing=true", call.Query);
    }

    [Fact]
    public async Task A_repo_override_records_the_runner_under_the_profile_the_run_resolved()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());
        var tree = Tree(h, "profiled", "https://example.test/profiled.git");
        var runtime = h.Runtime with { ProfileName = "night" };

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--once", "--repo", tree], default));

        var recorded = RunnerNames.Record.Read(runtime.RunnersPath);
        Assert.Contains(RunnerNames.Record.Key("night", Checkout.Canonical(h.Root)), recorded.Keys);
        Assert.DoesNotContain(Checkout.Canonical(h.Root), recorded.Keys);
    }

    [Fact]
    public async Task A_repo_override_with_no_profile_records_the_runner_under_the_bare_path()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());
        var tree = Tree(h, "unprofiled", "https://example.test/unprofiled.git");

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--repo", tree], default));

        Assert.Equal([Checkout.Canonical(h.Root)], RunnerNames.Record.Read(h.Runtime.RunnersPath).Keys.ToArray());
    }

    [Fact]
    public async Task A_loop_with_no_checkout_is_recorded_under_the_workspace_it_serves()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());
        var workspace = Path.Combine(h.Temp, "empty-workspace");
        Directory.CreateDirectory(workspace);
        var runtime = h.Runtime with { Checkouts = [] };

        Assert.Equal(
            0, await new GoToWorkCommand(runtime).RunAsync(["--once", "--workspace", workspace], default));

        var recorded = RunnerNames.Record.Read(runtime.RunnersPath);
        Assert.Equal([Checkout.Canonical(workspace)], recorded.Keys.ToArray());
        Assert.DoesNotContain(Checkout.Canonical(h.Root), recorded.Keys);
    }

    [Fact]
    public async Task Two_members_over_two_workspaces_are_named_apart_and_a_second_over_the_same_one_is_refused()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());
        var a = Path.Combine(h.Temp, "member-2");
        var b = Path.Combine(h.Temp, "member-3");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        var none = h.Runtime with { Checkouts = [] };

        Assert.Equal(0, await new GoToWorkCommand(none).RunAsync(["--once", "--workspace", a], default));
        Assert.Equal(0, await new GoToWorkCommand(none).RunAsync(["--once", "--workspace", b], default));

        var recorded = RunnerNames.Record.Read(none.RunnersPath);
        Assert.Equal(
            new[] { Checkout.Canonical(a), Checkout.Canonical(b) }.Order(), recorded.Keys.Order());
        Assert.Equal(2, recorded.Values.Distinct().Count());

        var wheres = h.Wire.Calls
            .Where(c => c.Method == "POST" && c.Path.StartsWith("/api/hatch/runners/", StringComparison.Ordinal))
            .Select(c => c.Read<RunnerHeartbeatRequest>().Where)
            .Distinct()
            .ToList();
        Assert.Equal(2, wheres.Count);
        Assert.NotEqual(WorkspaceLock.PathFor(a, h.Temp), WorkspaceLock.PathFor(b, h.Temp));

        // A second member over a workspace the first still holds is refused.
        using var held = WorkspaceLock.Take(a, "elsewhere-checkout", h.Temp, out _);
        Assert.NotNull(held);

        Assert.Equal(1, await new GoToWorkCommand(none).RunAsync(["--once", "--workspace", a], default));

        Assert.Contains(h.Say.Complained, l => l.Contains(a, StringComparison.Ordinal));
        Assert.Contains(h.Say.Complained, l => l.Contains(Environment.ProcessId.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Repo_given_on_the_command_line_ignores_whatever_HATCH_REPOS_produced()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        var fromSettings = new CheckoutEntry(
            Path.Combine(h.Temp, "from-settings"), "https://example.test/from-settings.git", Standing: false);
        var runtime = h.Runtime with { Checkouts = [h.Runtime.Checkouts[0], fromSettings] };

        var fromFlag = Tree(h, "from-flag", "https://example.test/from-flag.git");

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--once", "--repo", fromFlag], default));

        var call = Assert.Single(h.Wire.To("GET", Queue));
        Assert.Contains($"remote={Uri.EscapeDataString("https://example.test/from-flag.git")}", call.Query);
        Assert.DoesNotContain("from-settings.git", call.Query);
    }

    [SkippableFact]
    public async Task Two_spellings_of_one_repo_path_is_one_checkout_and_one_lock()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        var tree = Tree(h, "Named", "https://example.test/named.git");
        var lowered = Path.Combine(h.Temp, "named");
        Skip.IfNot(Directory.Exists(lowered), "this filesystem is case-sensitive, so there is one spelling");

        // If the two spellings were taken as two checkouts, the second
        // LoopLock.Take would refuse itself - the pid file the first left
        // behind is this same process's.
        Assert.Equal(
            0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--repo", tree, "--repo", lowered], default));
        Assert.DoesNotContain(h.Say.Complained, l => l.Contains("already running", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_lock_held_on_the_second_named_checkout_refuses_naming_it_and_releases_the_first()
    {
        using var h = new Harness();
        var other = Tree(h, "other", "https://example.test/other.git");

        using var held = LoopLock.Take(other, h.Temp, out _);
        Assert.NotNull(held);

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--repo", other], default));

        Assert.Contains(h.Say.Complained, l => l.Contains("already running in", StringComparison.Ordinal));
        Assert.Contains(h.Say.Complained, l => l.Contains(other, StringComparison.Ordinal));

        // The standing checkout's lock - taken first - was released rather
        // than left behind: a second loop can take it.
        using var retaken = LoopLock.Take(h.Root, h.Temp, out var refusal);
        Assert.NotNull(retaken);
        Assert.Equal("", refusal);

        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_missing_repo_path_refuses_before_any_lock_and_before_any_claim()
    {
        using var h = new Harness();
        var missing = Path.Combine(h.Temp, "does-not-exist");

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--repo", missing], default));

        Assert.Contains(h.Say.Complained, l => l.Contains(missing, StringComparison.Ordinal));
        Assert.Empty(h.Wire.Calls);

        using var takeable = LoopLock.Take(h.Root, h.Temp, out var refusal);
        Assert.NotNull(takeable);
        Assert.Equal("", refusal);
    }

    [Fact]
    public async Task An_unknown_agent_refuses_before_any_lock_and_before_any_claim()
    {
        using var h = new Harness();
        var runtime = h.Runtime with { Settings = h.Runtime.Settings with { Agent = "nope" } };

        Assert.Equal(1, await new GoToWorkCommand(runtime).RunAsync(["--once"], default));

        Assert.Contains(h.Say.Complained, l => l.Contains("nope", StringComparison.Ordinal) && l.Contains("claude", StringComparison.Ordinal));
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Started_outside_a_checkout_with_repo_is_named_after_the_first_and_sends_no_standing()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        var elsewhere = Tree(h, "elsewhere", "https://example.test/elsewhere.git");
        var runtime = h.Runtime with { Checkouts = [] };
        var runnerName = await Checkout.RunnerAsync(
            null, Checkout.Host(), elsewhere, null, null, default, h.Runtime.RunnersPath);

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--once", "--repo", elsewhere], default));

        Assert.Contains(h.Wire.Calls, c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString(runnerName)}");

        var call = Assert.Single(h.Wire.To("GET", Queue));
        Assert.DoesNotContain("standing=true", call.Query);
        Assert.Contains($"remote={Uri.EscapeDataString("https://example.test/elsewhere.git")}", call.Query);
    }

    [Fact]
    public async Task A_second_loop_in_this_checkout_is_refused_and_the_first_keeps_its_lock()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        using var held = LoopLock.Take(h.Root, h.Temp, out _);
        Assert.NotNull(held);

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));
        Assert.Contains(h.Say.Complained, l => l.Contains("already running in", StringComparison.Ordinal));

        // Nothing was read and nothing was spawned: the refusal is before the
        // board, because a loop that cannot run should not be asking.
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Two_loops_in_two_checkouts_both_get_going()
    {
        using var first = new Harness();
        using var second = new Harness();

        foreach (var h in (Harness[])[first, second])
        {
            h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
            h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());
        }

        Assert.Equal(0, await new GoToWorkCommand(first.Runtime).RunAsync(["--once"], default));
        Assert.Equal(0, await new GoToWorkCommand(second.Runtime).RunAsync(["--once"], default));

        Assert.DoesNotContain(first.Say.Complained, l => l.Contains("already running", StringComparison.Ordinal));
        Assert.DoesNotContain(second.Say.Complained, l => l.Contains("already running", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_loop_carries_no_flag_of_its_own_across_a_night()
    {
        using var h = new Harness();
        OneTicket(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        // `work --model` is one operator's opinion about one increment; what the
        // loop carries is the playbook's, which already has the ticket's own
        // overrides folded into it.
        var spawned = Assert.Single(h.Sessions.Spawned);
        Assert.Equal("opus", spawned.Model);
        Assert.Equal("high", spawned.Effort);
        Assert.Equal(h.Root, spawned.Root);
    }

    [Fact]
    public async Task It_takes_where_to_look_or_a_ticket_and_a_ticket_is_never_the_answer()
    {
        using var h = new Harness();

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["AER-1"], default));
        Assert.Contains(h.Say.Complained, l => l.Contains("does not take a ticket", StringComparison.Ordinal));

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["AER-1", "--under", "AER-9"], default));
        Assert.Contains(h.Say.Complained, l => l.Contains("not both", StringComparison.Ordinal));

        // Zero is refused rather than clamped: it reads as "as fast as possible"
        // and means a board asked the same question thousands of times a minute.
        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["--interval", "0"], default));
        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["--until", "tea time"], default));

        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_stop_file_that_is_already_there_is_refused_rather_than_read_as_an_empty_board()
    {
        using var h = new Harness();
        var stop = Path.Combine(h.Temp, "stop");
        File.WriteAllText(stop, "");

        Assert.Equal(1, await new GoToWorkCommand(h.Runtime).RunAsync(["--stop-file", stop], default));
        Assert.Contains(h.Say.Complained, l => l.Contains("already exists", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Max_runs_bounds_the_night()
    {
        using var h = new Harness();
        OneTicket(h);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "2"], default);

        Assert.Equal(2, h.Sessions.Spawned.Count);
        Assert.Equal(2, h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim").Count);
        Assert.Contains(h.Say.Said, l => l.Contains("--max-runs 2 reached", StringComparison.Ordinal));
    }

    // ---- The keyboard (HA-132) ----

    [Fact]
    public async Task A_keyboard_stop_pressed_during_the_first_ticket_ends_the_night_before_a_second_is_claimed()
    {
        using var h = new Harness();
        OneTicket(h, "AER-1");
        OneTicket(h, "AER-2");

        // No real keyboard in a test: the same press a KeyReader would make,
        // called directly once the first ticket is claimed (Prepare() runs
        // after the claim and before the spawn) and before the loop asks
        // the board again.
        h.Workspace.Watching = () => h.Runtime.Controls.Press('s');

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync([], default));

        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-2/claim"));
        Assert.Contains(h.Say.Said, l => l.Contains("keyboard", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("--stop-file", StringComparison.Ordinal));
    }

    // ---- The keyboard: p, k and ? (HA-133) ----

    [Fact]
    public async Task A_keyboard_pause_claims_nothing_and_resumes_before_the_interval_elapses()
    {
        using var h = new Harness();
        OneTicket(h);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 2, 0, 0, TimeSpan.Zero));
        var runtime = h.Runtime with { Clock = clock };

        // Pressed before the loop's first beat - exactly the keyboard's own
        // read, not the board's.
        runtime.Controls.Press('p');

        var running = new GoToWorkCommand(runtime).RunAsync(["--max-runs", "1", "--interval", "3600"], default);

        await Harness.Eventually(
            () => h.Say.Said.Any(l => l.Contains("paused from the keyboard", StringComparison.Ordinal)),
            "the paused line");
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));

        // A second press resumes it, and the nap wakes on the very next slice
        // rather than waiting out the rest of the hour-long interval.
        runtime.Controls.Press('p');
        clock.Advance(TimeSpan.FromSeconds(5));

        await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
    }

    [Fact]
    public async Task N_ends_the_idle_nap_at_once_and_the_next_pass_claims_without_the_clock_moving()
    {
        using var h = new Harness();
        OneTicket(h);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 2, 0, 0, TimeSpan.Zero));
        var runtime = h.Runtime with { Clock = clock };

        // The first pass finds nothing, so the loop settles into its idle nap.
        h.Wire.Replace("GET", Queue, HttpStatusCode.OK, "[]");

        var running = new GoToWorkCommand(runtime).RunAsync(["--max-runs", "1", "--interval", "3600"], default);

        await Harness.Eventually(
            () => h.Say.Said.Any(l => l.Contains("waiting, and asking again", StringComparison.Ordinal)),
            "the idle line");

        h.Wire.Replace(
            "GET", Queue, HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-1") }, Fixtures.Json));

        // An n before the nap begins is a no-op, so pressing until the claim
        // lands cannot race the moment the nap says it is napping. The clock
        // never moves: only n can have ended the hour.
        await Harness.Eventually(
            () =>
            {
                runtime.Controls.Press('n');
                return h.Wire.To("POST", "/api/hatch/issues/AER-1/claim").Count > 0;
            },
            "the claim after n");

        await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
        Assert.Single(h.Sessions.Spawned);
    }

    [Fact]
    public async Task N_does_not_end_a_keyboard_pause()
    {
        using var h = new Harness();
        OneTicket(h);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 2, 0, 0, TimeSpan.Zero));
        var runtime = h.Runtime with { Clock = clock };

        runtime.Controls.Press('p');

        var running = new GoToWorkCommand(runtime).RunAsync(["--max-runs", "1", "--interval", "3600"], default);

        await Harness.Eventually(
            () => h.Say.Said.Any(l => l.Contains("paused from the keyboard", StringComparison.Ordinal)),
            "the paused line");

        for (var i = 0; i < 3; i++)
        {
            runtime.Controls.Press('n');
            await Task.Delay(20);
        }

        // A paused loop never runs a pass, so the queue is never read.
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
        Assert.Empty(h.Wire.To("GET", Queue));

        runtime.Controls.Press('p');
        clock.Advance(TimeSpan.FromSeconds(5));

        await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
    }

    [Fact]
    public async Task A_board_pause_outranks_a_local_resume_and_the_idle_line_still_names_the_board()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Wire.Json("POST", "/api/hatch/runners/test%3A%2Fcheckout", new RunnerInstructionDto("paused", null, null, null, null));

        using var cts = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Runtime).RunAsync(["--interval", "3600"], cts.Token);

        await Harness.Eventually(
            () => h.Wire.Count("POST", "/api/hatch/runners/test%3A%2Fcheckout") > 0, "the first heartbeat");
        await Harness.Eventually(
            () => h.Say.Said.Any(l => l.Contains("the board has this runner paused", StringComparison.Ordinal)),
            "the board's own paused line");

        // A local p cannot read as having un-paused a board-paused runner.
        h.Runtime.Controls.Press('p');
        await Task.Delay(50);

        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains(h.Say.Said, l => l.Contains("the board has this runner paused", StringComparison.Ordinal));

        cts.Cancel();
        await running;
    }

    // ---- HA-1359: an order, read once from the heartbeat and acted on between increments ----

    private const string Beat = "/api/hatch/runners/test%3A%2Fcheckout";

    /// <summary>
    /// The board answering a heartbeat with an order, <paramref name="times"/> beats in a row, and then
    /// with the same state and no order - which is what the real server does, since it clears an order
    /// in the beat that reads it. Registered in that order because the wire matches rules first-added-first.
    /// </summary>
    private static void Ordering(Harness h, string state, RunnerOrderDto order, int? maxRuns = null, int times = 1)
    {
        for (var i = 0; i < times; i++)
            h.Wire.Once("POST", Beat, HttpStatusCode.OK,
                System.Text.Json.JsonSerializer.Serialize(
                    new RunnerInstructionDto(state, null, maxRuns, null, null, Order: order), Fixtures.Json));

        h.Wire.Json("POST", Beat, new RunnerInstructionDto(state, null, maxRuns, null, null));
    }

    private static string[] Claims(Harness h) =>
        h.Wire.Calls
            .Where(c => c.Method == "POST" && c.Path.EndsWith("/claim", StringComparison.Ordinal))
            .Select(c => c.Path)
            .ToArray();

    [Fact]
    public async Task A_paused_loop_given_a_work_order_works_that_ticket_once_and_reads_paused_again()
    {
        using var h = new Harness();
        OneTicket(h, "AER-2");
        Ordering(h, "paused", new RunnerOrderDto(RunnerOrderKinds.Work, "AER-2", "Nathan"));

        using var cts = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Runtime).RunAsync(["--interval", "3600"], cts.Token);

        await Harness.Eventually(
            () => h.Wire.Count("DELETE", "/api/hatch/issues/AER-2/claim") == 1, "the ordered ticket's claim let go");
        await Harness.Eventually(
            () => h.Say.Said.Any(l => l.Contains("the board has this runner paused", StringComparison.Ordinal)),
            "the paused line, on the beat after the order");

        cts.Cancel();
        await running;

        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-2/claim"));
        Assert.Single(h.Sessions.Spawned);

        // The queue is never walked for a named ticket.
        Assert.Empty(h.Wire.To("GET", Queue));
    }

    [Fact]
    public async Task A_running_loop_takes_the_ordered_ticket_ahead_of_the_queues_first_clear_row()
    {
        using var h = new Harness();
        OneTicket(h, "AER-1");
        OneTicket(h, "AER-2");
        Ordering(h, "running", new RunnerOrderDto(RunnerOrderKinds.Work, "AER-2", "Nathan"), maxRuns: 2);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "2"], default));

        // The ordered increment is the first of the two the cap allows, and
        // the queue's own pick is the second.
        Assert.Equal(new[] { "/api/hatch/issues/AER-2/claim", "/api/hatch/issues/AER-1/claim" }, Claims(h));
        Assert.Equal(2, h.Sessions.Spawned.Count);
    }

    [Fact]
    public async Task A_pass_order_on_a_paused_loop_runs_one_ordinary_pass_and_pauses_again()
    {
        using var h = new Harness();
        OneTicket(h, "AER-1");
        Ordering(h, "paused", new RunnerOrderDto(RunnerOrderKinds.Pass, null, "Nathan"));

        using var cts = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Runtime).RunAsync(["--interval", "3600"], cts.Token);

        await Harness.Eventually(
            () => h.Wire.Count("DELETE", "/api/hatch/issues/AER-1/claim") == 1, "the ordinary pass's claim let go");
        await Harness.Eventually(
            () => h.Say.Said.Any(l => l.Contains("the board has this runner paused", StringComparison.Ordinal)),
            "the paused line, on the beat after the pass");

        cts.Cancel();
        await running;

        // One ordinary pass: the queue read once, the row it offered claimed once.
        Assert.Equal(1, h.Wire.Count("GET", Queue));
        Assert.Equal(new[] { "/api/hatch/issues/AER-1/claim" }, Claims(h));
        Assert.Single(h.Sessions.Spawned);
    }

    [Fact]
    public async Task A_work_order_the_dispatcher_folds_is_spent_with_the_folds_own_sentence_and_runs_nothing()
    {
        using var h = new Harness();
        var fold = Held("other:/tree").Trim('"');
        h.Wire.Json("GET", "/api/hatch/work/AER-2", Fixtures.Work("AER-2", blocked: fold));
        Ordering(h, "paused", new RunnerOrderDto(RunnerOrderKinds.Work, "AER-2", "Nathan"));

        using var cts = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Runtime).RunAsync(["--interval", "3600"], cts.Token);

        await Harness.Eventually(() => h.Wire.Count("POST", Beat) >= 2, "the beat after the order");
        cts.Cancel();
        await running;

        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-2/claim"));
        Assert.Empty(h.Sessions.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains($"AER-2 - {fold}", StringComparison.Ordinal));
        Assert.Equal($"AER-2 - {fold}", h.Wire.To("POST", Beat)[1].Read<RunnerHeartbeatRequest>().Line);
    }

    [Fact]
    public async Task An_ordered_increment_counts_toward_the_three_failures_stop_like_any_other()
    {
        using var h = new Harness();
        OneTicket(h, "AER-1");
        OneTicket(h, "AER-2");
        h.Wire.Json("POST", "/api/hatch/issues/AER-2/comments", Fixtures.Comment());
        h.Sessions.Behaviour = (_, _, _) => throw new InvalidOperationException("the CLI fell over");
        Ordering(h, "running", new RunnerOrderDto(RunnerOrderKinds.Work, "AER-2", "Nathan"), times: 3);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync([], default));

        Assert.Contains(h.Say.Said, l => l.Contains("three increments in a row failed", StringComparison.Ordinal));
        Assert.Equal(3, h.Wire.To("DELETE", "/api/hatch/issues/AER-2/claim").Count);

        // The stop is asked before the queue's own pick, so the board's first row was never taken.
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
    }

    [Fact]
    public async Task Once_never_reads_an_order()
    {
        using var h = new Harness();
        OneTicket(h, "AER-1");
        OneTicket(h, "AER-2");
        Ordering(h, "running", new RunnerOrderDto(RunnerOrderKinds.Work, "AER-2", "Nathan"));

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Equal(new[] { "/api/hatch/issues/AER-1/claim" }, Claims(h));
    }

    [Fact]
    public async Task A_loop_out_of_usage_spends_an_order_with_a_sentence_and_claims_nothing()
    {
        using var h = new Harness();
        OneTicket(h, "AER-2");
        Carrying(h, new NightState { ExhaustedUntil = DateTimeOffset.UtcNow.AddDays(1) });
        Ordering(h, "running", new RunnerOrderDto(RunnerOrderKinds.Work, "AER-2", "Nathan"));

        using var cts = new CancellationTokenSource();
        var running = new GoToWorkCommand(h.Supervised).RunAsync(["--interval", "3600"], cts.Token);

        await Harness.Eventually(
            () => h.Say.Said.Any(l => l.Contains("order spent - out of Claude usage until", StringComparison.Ordinal)),
            "the sentence the order was spent with");

        cts.Cancel();
        await running;

        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-2/claim"));
        Assert.Empty(h.Sessions.Spawned);
    }

    [Fact]
    public async Task A_skipped_increment_is_followed_immediately_by_the_next_queued_ticket()
    {
        using var h = new Harness();
        OneTicket(h, "AER-1");
        OneTicket(h, "AER-2");

        // Both offered at first - the same swap-the-queue technique the
        // tree-that-cannot-be-prepared test above uses, standing in for a
        // board that would not really offer AER-1 straight back once it has
        // been skipped.
        h.Wire.Replace(
            "GET", Queue, HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-1"), Fixtures.Row("AER-2") }, Fixtures.Json));

        var skippedFirst = false;
        h.Sessions.Behaviour = (request, onLine, _) =>
        {
            if (!skippedFirst && request.Prompt.Contains("AER-1", StringComparison.Ordinal))
            {
                skippedFirst = true;
                h.Wire.Replace(
                    "GET", Queue, HttpStatusCode.OK,
                    System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-2") }, Fixtures.Json));

                // The keyboard's k, confirmed with y - the equivalent
                // injection HA-132 used for s, one key further.
                h.Runtime.Controls.Press('k');
                h.Runtime.Controls.Press('y');
            }
            else
            {
                onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            }

            return Task.FromResult(new SessionResult(0, ""));
        };

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "2"], default));

        // Neither claim stall ended the night - the first ticket's own skip
        // is followed straight on by the second, queued ticket.
        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-2/claim"));
    }

    // ---- HA-370: one queue read per increment, nothing carried from the last ----

    /// <summary>
    /// `LoopAsync` beats, polls, runs one `PassAsync` and goes straight back to
    /// the top - so nothing stands between two increments to cache a manifest
    /// across them. This is the clamp: a future change that reintroduced one
    /// would show up here as a third increment running on a second read.
    /// </summary>
    [Fact]
    public async Task Two_increments_read_the_queue_exactly_once_each()
    {
        using var h = new Harness();
        OneTicket(h, "AER-1");

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "2"], default));

        Assert.Equal(2, h.Wire.Count("GET", Queue));
    }

    [Fact]
    public async Task A_ticket_promoted_mid_increment_is_claimed_by_the_very_next_one()
    {
        using var h = new Harness();
        OneTicket(h, "AER-1");
        OneTicket(h, "AER-2");

        // Only AER-1 on the read the first increment picks from - AER-2 does
        // not exist on the board yet, standing in for a ticket raised to the
        // top while the first increment is still running.
        h.Wire.Replace(
            "GET", Queue, HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-1") }, Fixtures.Json));

        var ran = false;
        h.Sessions.Behaviour = (request, onLine, _) =>
        {
            if (!ran && request.Prompt.Contains("AER-1", StringComparison.Ordinal))
            {
                ran = true;

                // Promoted onto the board while AER-1's own increment is in
                // flight - the second increment's read is the first one that
                // ever offers it.
                h.Wire.Replace(
                    "GET", Queue, HttpStatusCode.OK,
                    System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-2") }, Fixtures.Json));
            }

            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "2"], default));

        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-2/claim"));
    }

    [Fact]
    public async Task A_pass_claims_the_first_clear_row_of_its_own_read_not_one_the_last_read_left_behind()
    {
        using var h = new Harness();
        OneTicket(h, "AER-1");
        OneTicket(h, "AER-2");
        OneTicket(h, "AER-3");

        h.Wire.Replace(
            "GET", Queue, HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-1"), Fixtures.Row("AER-2") }, Fixtures.Json));

        var ran = false;
        h.Sessions.Behaviour = (request, onLine, _) =>
        {
            if (!ran && request.Prompt.Contains("AER-1", StringComparison.Ordinal))
            {
                ran = true;

                // AER-1 is done and gone; AER-2 - second on the read just
                // spent - is first on this one, ahead of a new AER-3. A pass
                // that carried any position from the read before would walk
                // past AER-2, the row it actually owes a claim to, and reach
                // for AER-3 instead.
                h.Wire.Replace(
                    "GET", Queue, HttpStatusCode.OK,
                    System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-2"), Fixtures.Row("AER-3") }, Fixtures.Json));
            }

            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "2"], default));

        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-2/claim"));
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-3/claim"));
    }

    // ---- The hop ----

    [Fact]
    public async Task A_hop_spawns_nothing_and_the_loop_reads_the_board_again()
    {
        using var h = new Harness();
        h.Wire.Once(
            "GET", Queue, HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-1", hop: true) }, Fixtures.Json));
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("POST", "/api/hatch/work/AER-1/hop", Fixtures.Issue("AER-1"));

        // `--once` waits for the pass that actually is one: a hop is not a
        // session, so the loop reads the board again rather than stopping on
        // it, and stops on the idle pass that follows instead.
        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Workspace.Calls);
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log"));
        Assert.Equal(2, h.Wire.Count("GET", Queue));
        Assert.Single(h.Wire.To("POST", "/api/hatch/work/AER-1/hop"));
        Assert.Contains(h.Say.Said, l => l.Contains("AER-1", StringComparison.Ordinal) && l.Contains("express, no session", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_pre_approved_hop_spawns_nothing_and_the_loop_reads_the_board_again()
    {
        using var h = new Harness();
        h.Wire.Once(
            "GET", Queue, HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-1", hop: true, hopKind: HopKinds.Approve) }, Fixtures.Json));
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("POST", "/api/hatch/work/AER-1/hop", Fixtures.Issue("AER-1"));

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Workspace.Calls);
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log"));
        Assert.Equal(2, h.Wire.Count("GET", Queue));
        Assert.Single(h.Wire.To("POST", "/api/hatch/work/AER-1/hop"));
        Assert.Contains(h.Say.Said, l => l.Contains("AER-1", StringComparison.Ordinal) && l.Contains("pre-approved, no session", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_hop_under_a_running_epic_names_it()
    {
        using var h = new Harness();
        h.Wire.Once(
            "GET", Queue, HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(
                new[] { Fixtures.Row("AER-1", hop: true, hopKind: HopKinds.Under, hopUnder: "HA-86") }, Fixtures.Json));
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("POST", "/api/hatch/work/AER-1/hop", Fixtures.Issue("AER-1"));

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Contains(h.Say.Said, l => l.Contains("AER-1", StringComparison.Ordinal) && l.Contains("under HA-86, no session", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Max_runs_1_with_a_hop_then_a_session_runs_exactly_one_session()
    {
        using var h = new Harness();
        h.Wire.Once(
            "GET", Queue, HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-1", hop: true) }, Fixtures.Json));
        h.Wire.Json("POST", "/api/hatch/work/AER-1/hop", Fixtures.Issue("AER-1"));
        OneTicket(h, "AER-2");

        await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "1"], default);

        // The hop did not advance --max-runs, so the cap is spent on the one
        // pass that actually spawned something.
        Assert.Single(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("POST", "/api/hatch/work/AER-1/hop"));
        Assert.Contains(h.Say.Said, l => l.Contains("--max-runs 1 reached", StringComparison.Ordinal));
    }

    // ---- Coming back as a newer version of itself ----

    /// <summary>The totals one incarnation left for the next.</summary>
    private static void Carrying(Harness h, NightState state) => Assert.True(state.Write(h.NightState));

    [Fact]
    public async Task A_loop_whose_own_source_changed_at_the_reset_comes_back_as_the_new_one()
    {
        using var h = new Harness();
        OneTicket(h);

        // The change lands exactly where a real one would: inside the reset,
        // which is the moment the new source arrives on disk.
        var resets = 0;
        h.Workspace.Watching = () =>
        {
            if (++resets == 2) h.Self.Print = FakeSelf.Of(("src/Hatch.Cli/GoToWork.cs", "after"));
        };

        Assert.Equal(
            GoToWorkCommand.RestartExitCode,
            await new GoToWorkCommand(h.Supervised).RunAsync([], default));

        // Nothing was spawned on the pass that decided to restart, and the
        // ticket it had claimed went back before the process exited: a restart
        // holds no claim, or the version coming back would find its own work
        // being worked by a runner that no longer exists.
        Assert.Single(h.Sessions.Spawned);
        Assert.Equal(2, h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim").Count);

        Assert.Contains(h.Say.Said, l => l.Contains("the loop's own source changed", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("src/Hatch.Cli/GoToWork.cs", StringComparison.Ordinal));

        // And the one increment that did run is counted once, in the file the
        // next incarnation reads.
        var carried = NightState.Read(h.NightState);
        Assert.NotNull(carried);
        Assert.Equal(1, carried.Runs);
        Assert.Equal(1.5m, carried.Spent);
        Assert.Equal(1, carried.Restarts);
        // The one ticket a pass spent money on was left where it found it -
        // the first time in a row, so it is let go of quietly rather than
        // flagged, and carried in its own list.
        Assert.Empty(carried.Stalled);
        Assert.Single(carried.LetGo);
    }

    // ---- Following the active profile ----

    /// <summary>
    /// A runtime that follows the active-profile pointer rather than naming its
    /// profile: the pointer says <c>day</c>, and both profiles have a file.
    /// </summary>
    private static (Runtime Runtime, string Pointer) Following(Harness h, bool supervised = true)
    {
        var pointer = Path.Combine(h.Temp, "profile");
        var profiles = Path.Combine(h.Temp, "profiles");
        Directory.CreateDirectory(profiles);
        File.WriteAllText(pointer, "day");
        File.WriteAllText(Path.Combine(profiles, "day"), "");
        File.WriteAllText(Path.Combine(profiles, "night"), "");

        var runtime = (supervised ? h.Supervised : h.Runtime) with
        {
            ProfileName = "day",
            ActiveProfilePath = pointer,
            ProfilesDirectory = profiles,
        };

        return (runtime, pointer);
    }

    /// <summary>Runs <paramref name="then"/> once the first session has finished, as <c>hatch use</c> would from another terminal.</summary>
    private static void AfterTheFirstSession(Harness h, Action then)
    {
        var finished = h.Sessions.Behaviour;
        var seen = 0;
        h.Sessions.Behaviour = async (request, onLine, ct) =>
        {
            var result = await finished(request, onLine, ct);
            if (++seen == 1) then();
            return result;
        };
    }

    private static bool Restarted(Harness h) =>
        h.Say.Said.Any(l => l.Contains("restarting", StringComparison.Ordinal));

    [Fact]
    public async Task A_loop_following_the_active_profile_restarts_when_the_pointer_moves_mid_increment()
    {
        using var h = new Harness();
        OneTicket(h);
        var (runtime, pointer) = Following(h);
        AfterTheFirstSession(h, () => File.WriteAllText(pointer, "night"));

        Assert.Equal(GoToWorkCommand.RestartExitCode, await new GoToWorkCommand(runtime).RunAsync([], default));

        Assert.Single(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains(h.Say.Said, l => l.Contains("changed from day to night", StringComparison.Ordinal));

        var carried = NightState.Read(h.NightState);
        Assert.NotNull(carried);
        Assert.Equal(1, carried.Runs);
        Assert.Equal(1, carried.Restarts);
        Assert.Equal(1.5m, carried.Spent);
    }

    [Fact]
    public async Task A_pointer_that_moves_during_an_increment_is_not_acted_on_until_the_next_pass()
    {
        using var h = new Harness();
        OneTicket(h);
        var (runtime, pointer) = Following(h);
        AfterTheFirstSession(h, () => File.WriteAllText(pointer, "night"));

        Assert.Equal(GoToWorkCommand.RestartExitCode, await new GoToWorkCommand(runtime).RunAsync(["--max-runs", "2"], default));

        // The cap would have allowed a second increment; the move is found at
        // the top of the next pass, before one starts.
        Assert.Single(h.Sessions.Spawned);
    }

    [Fact]
    public async Task A_restart_for_a_moved_profile_carries_the_totals_of_the_night()
    {
        using var h = new Harness();
        OneTicket(h);
        Carrying(h, new NightState { Runs = 3, Spent = 4m });
        var (runtime, pointer) = Following(h);
        AfterTheFirstSession(h, () => File.WriteAllText(pointer, "night"));

        Assert.Equal(GoToWorkCommand.RestartExitCode, await new GoToWorkCommand(runtime).RunAsync([], default));

        var carried = NightState.Read(h.NightState);
        Assert.NotNull(carried);
        Assert.Equal(4, carried.Runs);
    }

    [Fact]
    public async Task A_pinned_loop_ignores_the_pointer_moving()
    {
        using var h = new Harness();
        OneTicket(h);
        var (followed, pointer) = Following(h);
        var runtime = followed with { ActiveProfilePath = null };
        AfterTheFirstSession(h, () => File.WriteAllText(pointer, "night"));

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--max-runs", "2"], default));

        Assert.Equal(2, h.Sessions.Spawned.Count);
        Assert.False(Restarted(h));
    }

    [Fact]
    public async Task An_unsupervised_loop_stops_saying_why_when_the_pointer_moves()
    {
        using var h = new Harness();
        OneTicket(h);
        var (runtime, pointer) = Following(h, supervised: false);
        AfterTheFirstSession(h, () => File.WriteAllText(pointer, "night"));

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync([], default));

        Assert.Single(h.Sessions.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("cannot restart onto it", StringComparison.Ordinal));
        Assert.False(File.Exists(h.NightState));
    }

    [Fact]
    public async Task A_loop_run_with_no_restart_stops_saying_why_when_the_pointer_moves()
    {
        using var h = new Harness();
        OneTicket(h);
        var (runtime, pointer) = Following(h);
        AfterTheFirstSession(h, () => File.WriteAllText(pointer, "night"));

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--no-restart"], default));

        Assert.Single(h.Sessions.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("cannot restart onto it", StringComparison.Ordinal));
        Assert.False(File.Exists(h.NightState));
    }

    [Fact]
    public async Task A_pointer_removed_by_use_none_restarts_the_loop_onto_no_profile()
    {
        using var h = new Harness();
        OneTicket(h);
        var (runtime, pointer) = Following(h);
        AfterTheFirstSession(h, () => File.Delete(pointer));

        Assert.Equal(GoToWorkCommand.RestartExitCode, await new GoToWorkCommand(runtime).RunAsync([], default));

        Assert.Contains(h.Say.Said, l => l.Contains("to <none>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_pointer_that_has_not_moved_leaves_the_loop_alone()
    {
        using var h = new Harness();
        OneTicket(h);
        var (runtime, _) = Following(h);

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--max-runs", "2"], default));

        Assert.Equal(2, h.Sessions.Spawned.Count);
        Assert.False(Restarted(h));
    }

    [Fact]
    public async Task A_pointer_naming_a_profile_with_no_file_is_said_once_and_ignored()
    {
        using var h = new Harness();
        OneTicket(h);
        var (runtime, pointer) = Following(h);
        AfterTheFirstSession(h, () => File.WriteAllText(pointer, "ghost"));

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--max-runs", "3"], default));

        Assert.Equal(3, h.Sessions.Spawned.Count);
        Assert.False(Restarted(h));
        Assert.Single(h.Say.Said, l => l.Contains("no profile file has that name", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_pointer_that_is_not_a_profile_name_is_ignored()
    {
        using var h = new Harness();
        OneTicket(h);
        var (runtime, pointer) = Following(h);
        File.WriteAllText(Path.Combine(h.Temp, "x"), "");
        AfterTheFirstSession(h, () => File.WriteAllText(pointer, "../x"));

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--max-runs", "2"], default));

        Assert.Equal(2, h.Sessions.Spawned.Count);
        Assert.False(Restarted(h));
    }

    [Fact]
    public async Task Two_loops_sharing_a_pointer_move_only_the_one_that_follows_it()
    {
        using var a = new Harness();
        using var b = new Harness();
        OneTicket(a, "AER-1");
        OneTicket(b, "AER-2");

        var (following, pointer) = Following(a);
        var (pinnedFrom, _) = Following(b);
        var pinned = pinnedFrom with { ActiveProfilePath = null };
        AfterTheFirstSession(a, () => File.WriteAllText(pointer, "night"));

        var first = new GoToWorkCommand(following).RunAsync(["--max-runs", "2"], default);
        var second = new GoToWorkCommand(pinned).RunAsync(["--max-runs", "2"], default);
        var exits = await Task.WhenAll(first, second);

        Assert.Equal(GoToWorkCommand.RestartExitCode, exits[0]);
        Assert.Single(a.Sessions.Spawned);
        Assert.Equal(0, exits[1]);
        Assert.Equal(2, b.Sessions.Spawned.Count);
        Assert.Empty(a.Wire.To("POST", "/api/hatch/issues/AER-2/claim"));
        Assert.Empty(b.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
    }

    [Fact]
    public async Task A_loop_whose_source_did_not_change_carries_on_and_leaves_nothing_behind()
    {
        using var h = new Harness();
        OneTicket(h);

        Assert.Equal(0, await new GoToWorkCommand(h.Supervised).RunAsync(["--max-runs", "2"], default));

        Assert.Equal(2, h.Sessions.Spawned.Count);
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("restarting", StringComparison.Ordinal));

        // A state file on disk means "somebody is coming back". A night that
        // ended does not leave one for the next night to read.
        Assert.False(File.Exists(h.NightState));
    }

    [Fact]
    public async Task A_loop_that_has_been_up_long_enough_restarts_without_having_claimed_anything()
    {
        using var h = new Harness();
        OneTicket(h);

        // The backstop, and the reason there is one: an idle loop never resets,
        // so it never sees a change, and would run the version it started with
        // until somebody came and stopped it.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 2, 0, 0, TimeSpan.Zero))
        {
            AutoAdvanceAmount = TimeSpan.FromMinutes(40),
        };

        var runtime = h.Supervised with { Clock = clock };

        Assert.Equal(
            GoToWorkCommand.RestartExitCode,
            await new GoToWorkCommand(runtime).RunAsync([], default));

        Assert.Contains(h.Say.Said, l => l.Contains("this loop has been running", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("restarting to pick up anything that landed", StringComparison.Ordinal));

        // Read where no claim is held, so there is nothing to give back and
        // nothing half-started.
        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
    }

    [Fact]
    public async Task A_loop_in_a_set_whose_host_asked_for_a_restart_comes_back_without_having_claimed_anything()
    {
        using var h = new Harness();
        OneTicket(h);

        var dir = Directory.CreateTempSubdirectory("hatch-set-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "restart"), "");

            Assert.Equal(
                GoToWorkCommand.RestartExitCode,
                await new GoToWorkCommand(h.Supervised with { SetDir = dir.FullName }).RunAsync([], default));

            Assert.True(File.Exists(h.NightState));
            Assert.Empty(h.Sessions.Spawned);
            Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_loop_in_a_set_whose_host_asked_it_to_stop_ends_the_night_and_leaves_nothing_behind()
    {
        using var h = new Harness();
        OneTicket(h);

        var dir = Directory.CreateTempSubdirectory("hatch-set-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "stop"), "");

            Assert.Equal(0, await new GoToWorkCommand(h.Supervised with { SetDir = dir.FullName }).RunAsync([], default));

            Assert.Empty(h.Sessions.Spawned);
            Assert.False(File.Exists(h.NightState));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // ---- A usage limit's own wait ----

    /// <summary>
    /// The probe is the authority over the sentence that started the wait:
    /// still exhausted, since the window is still at 100%, but the reset
    /// instant it names replaces the one carried in - and the "out of Claude
    /// usage" sentence prints once for the correction, not once per probe that
    /// lands on the same corrected reading afterwards.
    /// </summary>
    [Fact]
    public async Task A_probed_reset_that_disagrees_with_the_carried_instant_replaces_it_and_says_so_once()
    {
        // HA-1107: the probe's resets_at carries no zone of its own - usually
        // Z - so the printed reading must come out in the runner's own zone,
        // not the one the probe answered in. A FrozenClock cannot stand in
        // for the runner here (see the real-time note below), so this is a
        // LiveZoneClock: real GetUtcNow(), a fixed offset for LocalTimeZone.
        var offset = TimeSpan.FromHours(-4);
        using var h = new Harness(clock: new LiveZoneClock(offset));
        OneTicket(h);

        var start = DateTimeOffset.UtcNow;
        var carriedUntil = start.AddHours(1);
        Carrying(h, new NightState { ExhaustedUntil = carriedUntil });

        // Real time rather than a FakeTimeProvider: the exhausted wait's own
        // nap is a Task.Delay against the runtime clock, and a fake clock only
        // ever advances when something reads it - it does not drive a pending
        // timer forward on its own, so a delay against one just hangs. A few
        // real seconds, polled every --interval 1, is what the existing
        // until/reset tests above already use for the same reason.
        var correctedAt = start.AddSeconds(3);
        var fake = new FakeProbingSessions
        {
            Behaviour = () => [Fixtures.UsageReport([("session", 100, correctedAt.ToString("O"))]), Fixtures.Result(turns: 0)],
        };

        var runtime = h.Supervised with { Sessions = fake };

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--interval", "1", "--max-runs", "1"], default));

        Assert.Single(fake.Inner.Spawned);

        // A literal built by hand from correctedAt and the fixed offset,
        // rather than through UsageLimit.Clock(correctedAt, known: true) -
        // that call reads correctedAt's own carried (UTC) offset and would
        // say the same thing whether or not the conversion this ticket adds
        // ever ran.
        var shownInRunnersZone = correctedAt.ToOffset(offset).ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant();
        Assert.Contains(h.Say.Said, l => l.Contains(shownInRunnersZone, StringComparison.Ordinal));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains(UsageLimit.Clock(carriedUntil, known: true), StringComparison.Ordinal));
        Assert.Single(h.Say.Said, l => l.Contains("out of Claude usage until", StringComparison.Ordinal));
    }

    /// <summary>A session window with room left ends the wait outright, the moment the probe says so - never waiting on the stale instant.</summary>
    [Fact]
    public async Task A_probed_session_with_room_ends_the_wait_at_once()
    {
        using var h = new Harness();
        OneTicket(h);

        Carrying(h, new NightState { ExhaustedUntil = DateTimeOffset.UtcNow.AddDays(1) });

        var fake = new FakeProbingSessions
        {
            Behaviour = () => [Fixtures.UsageReport([("session", 42, null)]), Fixtures.Result(turns: 0)],
        };

        var runtime = h.Supervised with { Sessions = fake };

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--once"], default));

        Assert.Single(fake.Inner.Spawned);
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("out of Claude usage", StringComparison.Ordinal));
    }

    /// <summary>No session window in the probe's answer - or no answer at all - touches nothing; the carried sentence still stands.</summary>
    [Fact]
    public async Task A_probe_that_answers_nothing_leaves_the_sentence_standing()
    {
        using var h = new Harness();
        OneTicket(h);

        var start = DateTimeOffset.UtcNow;
        var carriedUntil = start.AddSeconds(3);
        Carrying(h, new NightState { ExhaustedUntil = carriedUntil });

        var fake = new FakeProbingSessions { Behaviour = () => null };
        var runtime = h.Supervised with { Sessions = fake };

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--interval", "1", "--max-runs", "1"], default));

        Assert.Single(fake.Inner.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains(UsageLimit.Clock(carriedUntil, known: true), StringComparison.Ordinal));
    }

    /// <summary>
    /// Drives a fake clock forward by hand rather than through
    /// <c>AutoAdvanceAmount</c>: the exhausted wait's own nap is
    /// <c>Task.Delay(slice, runtime.Clock, ct)</c> (GoToWork.cs around line
    /// 1740), so nothing about it ever elapses on its own - a read of
    /// <c>AutoAdvanceAmount</c>'s clock advances it, but nothing reads the
    /// clock while a pending <c>Task.Delay</c> is the only thing waiting on
    /// it, which is exactly the shape of this wait. Advancing it from outside
    /// is what lets that delay - and the loop behind it - complete without
    /// an actual multi-hour wait.
    /// </summary>
    private static async Task DriveAsync(FakeTimeProvider clock, Task run, TimeSpan step)
    {
        for (var i = 0; !run.IsCompleted; i++)
        {
            Assert.True(i < 1_000, "the clock-driven run never finished");
            clock.Advance(step);

            // A real tick rather than Task.Yield(): what is left after the
            // clamp clears is ordinary async work (claiming, spawning) with
            // nothing left to drive by hand, and a bare yield never gives the
            // thread pool behind it an actual turn.
            await Task.Delay(1);
        }
    }

    [Fact]
    public async Task A_reset_rolled_a_day_out_is_clamped_to_the_sessions_own_window()
    {
        using var h = new Harness();
        OneTicket(h);

        var start = DateTimeOffset.UtcNow;
        Carrying(h, new NightState { ExhaustedUntil = start.AddDays(1) });

        var clock = new FakeTimeProvider(start);
        var fake = new FakeProbingSessions { Behaviour = () => null };
        var runtime = h.Supervised with { Clock = clock, Sessions = fake };

        var run = new GoToWorkCommand(runtime).RunAsync(["--interval", "1", "--max-runs", "1", "--restart-after", "0"], default);
        await DriveAsync(clock, run, TimeSpan.FromMinutes(15));

        Assert.Equal(0, await run);

        // Spawned at all, while the carried ExhaustedUntil (a day out) was
        // nowhere near reached, is only possible through the clamp.
        Assert.Single(fake.Inner.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("back within Claude usage", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refusal_a_minute_after_the_named_reset_retries_in_minutes_and_not_a_day()
    {
        using var h = new Harness();
        OneTicket(h);
        h.Wire.Json("GET", "/api/hatch/issues/AER-1", Fixtures.Issue("AER-1"));
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments", Fixtures.Comment());

        // 7:41pm in New York: the wait cleared at 7:40pm and the account's own
        // reset trailed it, so the sentence names a minute already gone.
        var start = DateTimeOffset.Parse("2026-09-28T23:41:00+00:00");
        var clock = new FakeTimeProvider(start);

        var calls = 0;
        var fake = new FakeProbingSessions { Behaviour = () => null };
        fake.Inner.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            if (++calls == 1)
            {
                onLine?.Invoke(Fixtures.Result(
                    error: true, said: "You've hit your session limit · resets 7:40pm (America/New_York)"));
                return Task.FromResult(new SessionResult(1, ""));
            }

            onLine?.Invoke(Fixtures.Result());
            return Task.FromResult(new SessionResult(0, ""));
        };
        var runtime = h.Supervised with { Clock = clock, Sessions = fake };

        // A minute a tick, not the quarter hour the clamp tests use: every real
        // tick moves the clock a whole step, so a coarse one lets the run's own
        // async work eat the hour this asserts it stays inside.
        var run = new GoToWorkCommand(runtime).RunAsync(["--interval", "1", "--max-runs", "2", "--restart-after", "0"], default);
        await DriveAsync(clock, run, TimeSpan.FromMinutes(1));

        Assert.Equal(0, await run);

        // Rolled to tomorrow it would be 23h out and only the five-hour clamp
        // would free the runner; read as just passed it is back in minutes.
        Assert.Equal(2, fake.Inner.Spawned.Count);
        Assert.True(clock.GetUtcNow() - start < TimeSpan.FromHours(1), "the retry waited as long as a day's roll would");
        Assert.Contains(h.Say.Said, l => l.Contains("just passed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_weekly_limits_seven_day_window_is_not_clamped_to_the_sessions_five_hours()
    {
        using var h = new Harness();
        OneTicket(h);

        var start = DateTimeOffset.UtcNow;
        Carrying(h, new NightState
        {
            ExhaustedUntil = start.AddDays(6),
            ExhaustedWindow = "weekly",
            UntilAt = start.AddHours(12), // well past 5h, nowhere near 7d
        });

        var clock = new FakeTimeProvider(start);
        var fake = new FakeProbingSessions { Behaviour = () => null };
        var runtime = h.Supervised with { Clock = clock, Sessions = fake };

        var run = new GoToWorkCommand(runtime).RunAsync(["--interval", "1", "--restart-after", "0"], default);
        await DriveAsync(clock, run, TimeSpan.FromMinutes(15));

        Assert.Equal(0, await run);

        // Never spawned: by the time --until ended the night, well past the
        // session's own 5 hours had elapsed on the fake clock, and the wait
        // was still standing - proof the clamp used the weekly length.
        Assert.Empty(fake.Inner.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("has come", StringComparison.Ordinal));
    }

    /// <summary>
    /// HA-1457: the exhausted-wait's own reminder names the window it is
    /// waiting on and when the wait was set, so a stuck wait's own log line
    /// says more than "still out of usage" on its way past.
    /// </summary>
    [Fact]
    public async Task The_exhausted_reminder_names_its_window_and_when_it_was_set()
    {
        using var h = new Harness();
        OneTicket(h);

        var start = DateTimeOffset.UtcNow;
        Carrying(h, new NightState
        {
            ExhaustedUntil = start.AddDays(6),
            ExhaustedWindow = "weekly",
            ExhaustedAt = start,
            UntilAt = start.AddSeconds(2),
        });

        var clock = new FakeTimeProvider(start);
        var fake = new FakeProbingSessions { Behaviour = () => null };
        var runtime = h.Supervised with { Clock = clock, Sessions = fake };

        var run = new GoToWorkCommand(runtime).RunAsync(["--interval", "1", "--restart-after", "0"], default);
        await DriveAsync(clock, run, TimeSpan.FromSeconds(1));

        Assert.Equal(0, await run);

        Assert.Contains(h.Say.Said, l =>
            l.Contains("taking no tickets until then", StringComparison.Ordinal) &&
            l.Contains("(weekly)", StringComparison.Ordinal) &&
            l.Contains("set ", StringComparison.Ordinal));
    }

    /// <summary>
    /// HA-1457, AC4: a wait that outlasts its sentence's reset time by more
    /// than one reminder interval logs why - judged against the probe's own
    /// last-known session reset, since the carried ExhaustedUntil is exactly
    /// the value an incident can show was wrong. Here the probe's one good
    /// reading (at the start) goes stale while a fresh increment's own
    /// sentence sets a much later ExhaustedUntil behind it - the real
    /// incident's shape.
    /// </summary>
    [Fact]
    public async Task A_wait_set_past_the_probes_own_last_known_reset_says_why()
    {
        using var h = new Harness();
        OneTicket(h);

        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        Carrying(h, new NightState { UntilAt = start.AddSeconds(8) });

        var clock = new FakeTimeProvider(start);
        var probeCalls = 0;
        var fake = new FakeProbingSessions
        {
            // The one good probe, at the start - its own session window never
            // moves again, because every later call answers nothing.
            Behaviour = () => ++probeCalls == 1
                ? [Fixtures.UsageReport([("session", 100, start.ToString("O"))]), Fixtures.Result(turns: 0)]
                : null,
        };

        var sessionCalls = 0;
        fake.Inner.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            if (++sessionCalls == 1)
            {
                // The sentence's own reset, two hours out - set well behind
                // the probe's own reading above, which never moves again.
                onLine?.Invoke(Fixtures.RateLimitEvent(("five_hour", 1.0, start.AddHours(2).ToUnixTimeSeconds())));
                onLine?.Invoke(Fixtures.Result(error: true, said: "You've hit your session limit · resets 7:40pm (America/New_York)"));
                return Task.FromResult(new SessionResult(1, ""));
            }

            onLine?.Invoke(Fixtures.Result());
            return Task.FromResult(new SessionResult(0, ""));
        };

        var runtime = h.Supervised with { Clock = clock, Sessions = fake };

        var run = new GoToWorkCommand(runtime).RunAsync(["--interval", "1", "--restart-after", "0"], default);
        await DriveAsync(clock, run, TimeSpan.FromSeconds(1));

        Assert.Equal(0, await run);

        Assert.Single(fake.Inner.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("probe's own session window reset", StringComparison.Ordinal) && l.Contains("still waiting", StringComparison.Ordinal));
    }

    /// <summary>
    /// HA-1457: nothing can print during a single nap, however long - so the
    /// watchdog checks the moment execution resumes. An idle loop with an
    /// interval longer than twice the reminder spacing naps clean through
    /// that window in one call, and the only way the stall still surfaces is
    /// this check firing right after.
    /// </summary>
    [Fact]
    public async Task A_single_nap_past_twice_the_reminder_spacing_prints_on_waking()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        var start = DateTimeOffset.UtcNow;
        var longInterval = 1_300; // > twice the 600s reminder spacing

        // Generous headroom past one nap's own length: a single driven clock
        // jump can overshoot mid-nap, and a bound too close behind the nap's
        // own duration catches that overshoot as --until itself, before the
        // nap ever returns and gives the watchdog its turn.
        Carrying(h, new NightState { UntilAt = start.AddSeconds(longInterval * 3) });

        var clock = new FakeTimeProvider(start);
        var runtime = h.Supervised with { Clock = clock };

        var run = new GoToWorkCommand(runtime).RunAsync(
            ["--interval", longInterval.ToString(CultureInfo.InvariantCulture), "--restart-after", "0"], default);

        // Five seconds a tick - NapAsync's own slice size - rather than the
        // coarser minute this file's other drives use: a long nap chains many
        // of those 5s slices internally, and a bigger jump races ahead of
        // that chain, landing --until's own check on an already-advanced
        // clock before the nap it was meant to outlast ever returns.
        await DriveAsync(clock, run, TimeSpan.FromSeconds(5));

        Assert.Equal(0, await run);

        Assert.Contains(h.Say.Said, l => l.Contains("still waiting - nothing printed in", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Source_changed_during_the_wait_restarts_the_loop_and_carries_the_instant_forward()
    {
        using var h = new Harness();

        // The change lands where the wait's own self-check reads it - the very
        // first pass through the wait, since nothing has been checked yet.
        h.Workspace.Watching = () => h.Self.Print = FakeSelf.Of(("src/Hatch.Cli/GoToWork.cs", "after"));

        var resetAt = DateTimeOffset.UtcNow.AddHours(1);
        Assert.True(new NightState { ExhaustedUntil = resetAt }.Write(h.Supervised.NightStatePath));

        Assert.Equal(
            GoToWorkCommand.RestartExitCode,
            await new GoToWorkCommand(h.Supervised).RunAsync([], default));

        // No ticket was ever picked up: the restart came from the wait itself.
        Assert.Empty(h.Sessions.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("the loop's own source changed", StringComparison.Ordinal));

        // And the incarnation coming back is still out of usage until the same
        // instant - a restart is the middle of the wait, not the end of it.
        var carried = NightState.Read(h.NightState);
        Assert.NotNull(carried);
        Assert.Equal(resetAt, carried.ExhaustedUntil);
    }

    [Fact]
    public async Task Stopping_from_the_board_ends_the_wait_rather_than_carrying_it_through()
    {
        using var h = new Harness();
        h.Wire.Json("POST", "/api/hatch/runners/test%3A%2Fcheckout", new RunnerInstructionDto("stopping", null, null, null, null));

        Assert.True(new NightState { ExhaustedUntil = DateTimeOffset.UtcNow.AddHours(1) }.Write(h.Supervised.NightStatePath));

        Assert.Equal(0, await new GoToWorkCommand(h.Supervised).RunAsync([], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("the board asked this runner to stop", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_until_earlier_than_the_reset_ends_the_night_at_until()
    {
        using var h = new Harness();

        // The reset is an hour out; --until is a second out - carried directly
        // rather than typed, so the test is not chasing a minute boundary.
        Assert.True(new NightState
        {
            ExhaustedUntil = DateTimeOffset.UtcNow.AddHours(1),
            UntilAt = DateTimeOffset.UtcNow.AddSeconds(1),
        }.Write(h.Supervised.NightStatePath));

        Assert.Equal(0, await new GoToWorkCommand(h.Supervised).RunAsync(["--interval", "1"], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("has come", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_age_of_zero_turns_the_backstop_off_and_a_negative_one_is_refused()
    {
        using var h = new Harness();
        OneTicket(h);

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 2, 0, 0, TimeSpan.Zero))
        {
            AutoAdvanceAmount = TimeSpan.FromHours(4),
        };

        var runtime = h.Supervised with { Clock = clock };

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--restart-after", "0", "--max-runs", "1"], default));
        Assert.Single(h.Sessions.Spawned);
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("has been running", StringComparison.Ordinal));

        // Refused rather than clamped: a loop that is always too old is a loop
        // that does nothing but restart.
        Assert.Equal(1, await new GoToWorkCommand(h.Supervised).RunAsync(["--restart-after", "-1"], default));
        Assert.Contains(h.Say.Complained, l => l.Contains("--restart-after takes a number of minutes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task What_a_restart_carried_counts_against_the_bounds_that_were_typed()
    {
        using var h = new Harness();
        OneTicket(h);
        Carrying(h, new NightState { Runs = 2, Spent = 5m, Restarts = 1 });

        Assert.Equal(0, await new GoToWorkCommand(h.Supervised).RunAsync(["--max-runs", "3"], default));

        // Two increments were already spent before this process existed, so
        // --max-runs 3 buys one more and not three.
        Assert.Single(h.Sessions.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("--max-runs 3 reached", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_restart_cannot_outspend_the_budget_by_restarting()
    {
        using var h = new Harness();
        OneTicket(h);
        Carrying(h, new NightState { Runs = 2, Spent = 5m });

        Assert.Equal(0, await new GoToWorkCommand(h.Supervised).RunAsync(["--max-spend", "6"], default));

        Assert.Single(h.Sessions.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("--max-spend 6 reached at $6.50", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_hour_a_night_was_given_is_the_hour_it_was_given_and_not_one_re_read_after_midnight()
    {
        using var h = new Harness();
        OneTicket(h);

        // `--until 23:59` typed at 23:58 and restarted at 00:01 re-reads as
        // 23:59 tomorrow, which adds a day to the night. The instant is carried,
        // so it is the one it always was - and it has now gone by.
        Carrying(h, new NightState
        {
            Runs = 1,
            UntilAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });

        Assert.Equal(0, await new GoToWorkCommand(h.Supervised).RunAsync(["--until", "23:59"], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("--until 23:59 has come", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_tally_at_the_end_is_the_whole_night_and_says_how_many_times_it_came_back()
    {
        using var h = new Harness();
        OneTicket(h);
        Carrying(h, new NightState
        {
            Runs = 2,
            Spent = 5m,
            Restarts = 1,
            Started = DateTimeOffset.UtcNow.AddHours(-2),
            Moved = ["hatch:   moved    AER-9  landed earlier"],
        });

        await new GoToWorkCommand(h.Supervised).RunAsync(["--max-runs", "3"], default);

        // Every increment, the whole spend, and the elapsed time measured from
        // the first incarnation's start rather than this one's.
        Assert.Contains(h.Say.Said, l => l.Contains("3 increment(s) in 2h", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("$6.50, 1 restart(s)", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("moved    AER-9", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("let go   AER-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_night_with_no_restarts_in_it_reads_exactly_as_it_always_did()
    {
        using var h = new Harness();
        OneTicket(h);

        await new GoToWorkCommand(h.Supervised).RunAsync(["--max-runs", "1"], default);

        Assert.Contains(h.Say.Said, l => l.Contains("1 increment(s) in", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("restart(s)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Three_ways_of_not_restarting_at_all()
    {
        // The source changes under every one of these, and none of them comes
        // back as the new version.
        foreach (var (what, args, supervised, warns) in (ValueTuple<string, string[], bool, bool>[])
                 [
                     ("--no-restart", ["--no-restart", "--max-runs", "1"], true, false),
                     ("--once", ["--once"], true, false),
                     ("no supervisor", ["--max-runs", "1"], false, true),
                 ])
        {
            using var h = new Harness();
            OneTicket(h);
            h.Workspace.Watching = () => h.Self.Print = FakeSelf.Of(("src/Hatch.Cli/GoToWork.cs", "after"));

            var runtime = supervised ? h.Supervised : h.Runtime;

            Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(args, default));
            Assert.Single(h.Sessions.Spawned);
            Assert.DoesNotContain(h.Say.Said, l => l.Contains("restarting", StringComparison.Ordinal));
            Assert.False(File.Exists(h.NightState));

            // A runner started by hand never even reads its own source: there is
            // nobody standing over it to build the new one.
            if (!supervised) Assert.Equal(0, h.Self.Taken);

            var mentionsWarning = h.Say.Said.Any(l => l.Contains("will not rebuild or restart itself", StringComparison.Ordinal));
            Assert.Equal(warns, mentionsWarning);
        }
    }

    // ---- --workspace: cloning what the board binds (HA-19) ----

    private static Runtime WithWorkspace(Harness h, string workspace) =>
        h.Runtime with { Settings = h.Runtime.Settings with { Workspace = workspace } };

    [Fact]
    public async Task A_default_workspace_held_by_another_loop_is_served_around_not_refused()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        var workspace = Path.Combine(h.Temp, "default-workspace");
        using var held = WorkspaceLock.Take(workspace, "elsewhere-checkout", h.Temp, out _);
        Assert.NotNull(held);

        var runtime = h.Runtime with { Settings = h.Runtime.Settings.WithWorkspace(workspace, defaulted: true) };

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--once"], default));

        Assert.Contains(
            h.Say.Complained,
            l => l.Contains("this run will not clone", StringComparison.Ordinal)
                 && l.Contains(Environment.ProcessId.ToString(), StringComparison.Ordinal)
                 && l.Contains("elsewhere-checkout", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Say.Complained, l => l.Contains("already running", StringComparison.Ordinal));
        Assert.DoesNotContain("clones=true", Assert.Single(h.Wire.To("GET", Queue)).Query);

        // The heartbeat says the same: a run that gave way declares no clones.
        var beat = h.Wire.To("POST", $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}")[0];
        Assert.False(beat.Read<RunnerHeartbeatRequest>().Clones);
    }

    [Fact]
    public async Task A_workspace_somebody_chose_that_another_loop_holds_is_still_refused()
    {
        using var h = new Harness();

        var workspace = Path.Combine(h.Temp, "chosen-workspace");
        using var held = WorkspaceLock.Take(workspace, "elsewhere-checkout", h.Temp, out _);
        Assert.NotNull(held);

        var runtime = h.Runtime with { Settings = h.Runtime.Settings.WithWorkspace(workspace, defaulted: false) };

        Assert.Equal(1, await new GoToWorkCommand(runtime).RunAsync(["--once"], default));

        Assert.Contains(h.Say.Complained, l => l.Contains(workspace, StringComparison.Ordinal));
        Assert.Contains(h.Say.Complained, l => l.Contains(Environment.ProcessId.ToString(), StringComparison.Ordinal));
        Assert.Contains(h.Say.Complained, l => l.Contains("elsewhere-checkout", StringComparison.Ordinal));
        Assert.Contains(h.Say.Complained, l => l.Contains("hatch config --here --workspace", StringComparison.Ordinal));
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task Workspace_off_on_the_command_line_declares_no_clones_even_where_a_workspace_is_set()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        var workspace = Path.Combine(h.Temp, "set-workspace");
        Directory.CreateDirectory(workspace);

        Assert.Equal(
            0, await new GoToWorkCommand(WithWorkspace(h, workspace)).RunAsync(["--once", "--workspace", "off"], default));

        Assert.DoesNotContain("clones=true", Assert.Single(h.Wire.To("GET", Queue)).Query);
        Assert.Empty(h.Clone.Requested);
    }

    [Fact]
    public async Task A_default_workspace_that_the_standing_checkout_sits_in_gives_way_to_a_named_repo()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        // --repo names a clone that lives under the default workspace: a
        // conflict the person never made, so the run serves it and goes on.
        var workspace = Path.Combine(h.Temp, "default-workspace");
        var clone = Path.Combine(workspace, "example.test", "owner", "repo");
        Directory.CreateDirectory(clone);
        Git(clone, "init", "--quiet");
        Git(clone, "remote", "add", "origin", "https://example.test/owner/repo.git");

        var runtime = h.Runtime with { Settings = h.Runtime.Settings.WithWorkspace(workspace, defaulted: true) };

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--once", "--repo", clone], default));

        Assert.DoesNotContain(h.Say.Complained, l => l.Contains("one contains the other", StringComparison.Ordinal));
        var query = Assert.Single(h.Wire.To("GET", Queue)).Query;
        Assert.Contains($"remote={Uri.EscapeDataString("https://example.test/owner/repo.git")}", query);
        Assert.DoesNotContain("clones=true", query);
    }

    [Fact]
    public async Task A_workspace_holding_two_prior_clones_is_served_and_their_remotes_declared_with_clones_true()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        var workspace = Path.Combine(h.Temp, "clones");
        var a = Path.Combine(workspace, "example.test", "owner", "a");
        var b = Path.Combine(workspace, "example.test", "owner", "b");
        Directory.CreateDirectory(a);
        Git(a, "init", "--quiet");
        Git(a, "remote", "add", "origin", "https://example.test/owner/a.git");
        Directory.CreateDirectory(b);
        Git(b, "init", "--quiet");
        Git(b, "remote", "add", "origin", "https://example.test/owner/b.git");

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once", "--workspace", workspace], default));

        var call = Assert.Single(h.Wire.To("GET", Queue));
        Assert.Contains($"remote={Uri.EscapeDataString("https://example.test/owner/a.git")}", call.Query);
        Assert.Contains($"remote={Uri.EscapeDataString("https://example.test/owner/b.git")}", call.Query);
        Assert.Contains("clones=true", call.Query);

        // Nothing was cloned: both were already there.
        Assert.Empty(h.Clone.Requested);
    }

    [Fact]
    public async Task Clones_is_absent_from_the_queue_read_without_a_workspace()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        var call = Assert.Single(h.Wire.To("GET", Queue));
        Assert.DoesNotContain("clones=true", call.Query);
    }

    [Fact]
    public async Task A_primary_with_no_local_checkout_is_cloned_after_the_claim_and_before_the_reset_then_spawned_there()
    {
        using var h = new Harness();
        var workspace = Path.Combine(h.Temp, "clones");
        var runtime = WithWorkspace(h, workspace);

        var repo = Fixtures.Repository(
            "https://example.test/owner/repo.git", canonical: "example.test/owner/repo", primary: true, matchedRemote: null);

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", repositories: [repo]));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var claimedByThen = false;
        h.Workspace.Watching = () => claimedByThen = h.Wire.Count("POST", "/api/hatch/issues/AER-1/claim") == 1;

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--once"], default));

        var expected = Checkouts.PathFor(workspace, "example.test/owner/repo");
        var cloned = Assert.Single(h.Clone.Requested);
        Assert.Equal("https://example.test/owner/repo.git", cloned.Remote);
        Assert.Equal(expected, cloned.Path);

        Assert.True(claimedByThen, "the clone happened before the workspace was reset, but after the claim");
        Assert.Contains(expected, h.Workspace.Prepared);

        var spawned = Assert.Single(h.Sessions.Spawned);
        Assert.Equal(expected, spawned.Root);
    }

    [Fact]
    public async Task A_remote_bound_by_two_projects_across_a_whole_night_is_cloned_once()
    {
        using var h = new Harness();
        var workspace = Path.Combine(h.Temp, "clones");
        var runtime = WithWorkspace(h, workspace);

        var repo = Fixtures.Repository(
            "https://example.test/owner/shared.git", canonical: "example.test/owner/shared", primary: true, matchedRemote: null);

        h.Wire.Once(
            "GET", Queue, HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-1"), Fixtures.Row("AER-2") }, Fixtures.Json));
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-2") });

        foreach (var key in new[] { "AER-1", "AER-2" })
        {
            h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
            h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim/heartbeat", HttpStatusCode.NoContent);
            h.Wire.Json("GET", $"/api/hatch/work/{key}", Fixtures.Work(key, repositories: [repo]));
            h.Wire.Reply("DELETE", $"/api/hatch/issues/{key}/claim", HttpStatusCode.NoContent);
            h.Wire.Json("POST", $"/api/hatch/issues/{key}/work-log", Fixtures.WorkLogRow());
            h.Wire.Json("GET", $"/api/hatch/issues/{key}/questions", Array.Empty<QuestionDto>());
        }

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--max-runs", "2"], default));

        Assert.Single(h.Clone.Requested);
        Assert.Equal(2, h.Sessions.Spawned.Count);
    }

    [Fact]
    public async Task A_failed_clone_releases_the_lease_comments_the_ticket_walks_on_and_three_end_the_night()
    {
        using var h = new Harness();
        var workspace = Path.Combine(h.Temp, "clones");
        var runtime = WithWorkspace(h, workspace);

        h.Clone.Error = "fatal: could not read from remote repository.";

        // All three clear at once, so the walk inside a single PickAsync call
        // (Attempts = 5) tries all of them - no interval wait needed between
        // passes to reach three failures.
        var keys = new[] { "AER-1", "AER-2", "AER-3" };
        h.Wire.Json("GET", Queue, keys.Select(k => Fixtures.Row(k)).ToArray());

        foreach (var key in keys)
        {
            var repo = Fixtures.Repository(
                $"https://example.test/owner/{key}.git", canonical: $"example.test/owner/{key}",
                primary: true, matchedRemote: null);

            h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
            h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim/heartbeat", HttpStatusCode.NoContent);
            h.Wire.Json("GET", $"/api/hatch/work/{key}", Fixtures.Work(key, repositories: [repo]));
            h.Wire.Reply("DELETE", $"/api/hatch/issues/{key}/claim", HttpStatusCode.NoContent);
            h.Wire.Json($"POST", $"/api/hatch/issues/{key}/comments",
                new CommentDto(1, "hatch", "noted", "comment", null, null, DateTimeOffset.UnixEpoch));
        }

        // A short interval: the walk records all three failures inside its one
        // pass, so only the wait before the loop notices `ShouldStop` is real.
        await new GoToWorkCommand(runtime).RunAsync(["--interval", "1"], default);

        Assert.Empty(h.Sessions.Spawned);
        foreach (var key in keys)
        {
            Assert.Single(h.Wire.To("POST", $"/api/hatch/issues/{key}/claim"));
            Assert.Single(h.Wire.To("DELETE", $"/api/hatch/issues/{key}/claim"));

            var comment = Assert.Single(h.Wire.To("POST", $"/api/hatch/issues/{key}/comments"));
            var body = comment.Read<CommentCreateRequest>().Body;
            Assert.Contains("could not clone", body, StringComparison.Ordinal);
            Assert.Contains("fatal: could not read from remote repository.", body, StringComparison.Ordinal);
        }

        Assert.Equal(3, h.Clone.Requested.Count);
        Assert.Contains(h.Say.Said, l => l.Contains("three increments in a row failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_lock_held_on_the_workspace_itself_refuses_a_second_loop_naming_it()
    {
        using var h = new Harness();
        var workspace = Path.Combine(h.Temp, "clones");
        Directory.CreateDirectory(workspace);
        var runtime = WithWorkspace(h, workspace);

        using var held = WorkspaceLock.Take(workspace, "elsewhere-checkout", h.Temp, out _);
        Assert.NotNull(held);

        Assert.Equal(1, await new GoToWorkCommand(runtime).RunAsync(["--once"], default));

        Assert.Contains(h.Say.Complained, l => l.Contains(workspace, StringComparison.Ordinal));
        Assert.Contains(h.Say.Complained, l => l.Contains(Environment.ProcessId.ToString(), StringComparison.Ordinal));
        Assert.Contains(h.Say.Complained, l => l.Contains("elsewhere-checkout", StringComparison.Ordinal));
        Assert.Contains(h.Say.Complained, l => l.Contains("hatch config --here --workspace", StringComparison.Ordinal));
        Assert.Empty(h.Wire.Calls);

        // The standing checkout's own lock was released on the way out, same as
        // any other refusal before a claim: this loop never got past the door.
        using var retaken = LoopLock.Take(h.Root, h.Temp, out var refusal);
        Assert.NotNull(retaken);
        Assert.Equal("", refusal);
    }

    [Fact]
    public async Task A_workspace_lock_whose_owner_is_gone_is_cleared_and_the_run_clones()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        var workspace = Path.Combine(h.Temp, "clones");
        Directory.CreateDirectory(workspace);

        // A pid no process can have: the lock a crashed loop left behind.
        var stale = WorkspaceLock.PathFor(workspace, h.Temp);
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "pid"), int.MaxValue.ToString());

        Assert.Equal(0, await new GoToWorkCommand(WithWorkspace(h, workspace)).RunAsync(["--once"], default));

        Assert.Contains("clones=true", Assert.Single(h.Wire.To("GET", Queue)).Query);
        using var retaken = WorkspaceLock.Take(workspace, h.Root, h.Temp, out _);
        Assert.NotNull(retaken);
    }

    [Fact]
    public async Task Two_loops_on_two_workspaces_do_not_meet()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        var first = Path.Combine(h.Temp, "clones-a");
        var second = Path.Combine(h.Temp, "clones-b");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);

        using var held = WorkspaceLock.Take(first, "elsewhere-checkout", h.Temp, out _);
        Assert.NotNull(held);

        Assert.Equal(0, await new GoToWorkCommand(WithWorkspace(h, second)).RunAsync(["--once"], default));

        Assert.DoesNotContain(h.Say.Complained, l => l.Contains(first, StringComparison.Ordinal));
        Assert.DoesNotContain(h.Say.Complained, l => l.Contains(second, StringComparison.Ordinal));
    }

    [Fact]
    public async Task One_loop_on_one_workspace_takes_the_lock_for_the_night_and_releases_it()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        var workspace = Path.Combine(h.Temp, "clones");
        Directory.CreateDirectory(workspace);

        Assert.Equal(0, await new GoToWorkCommand(WithWorkspace(h, workspace)).RunAsync(["--once"], default));

        Assert.Contains("clones=true", Assert.Single(h.Wire.To("GET", Queue)).Query);
        using var retaken = WorkspaceLock.Take(workspace, h.Root, h.Temp, out _);
        Assert.NotNull(retaken);
    }

    [Fact]
    public async Task A_loop_that_restarts_into_its_new_source_releases_the_workspace_lock()
    {
        using var h = new Harness();
        OneTicket(h);

        var workspace = Path.Combine(h.Temp, "clones");
        Directory.CreateDirectory(workspace);

        var resets = 0;
        h.Workspace.Watching = () =>
        {
            if (++resets == 2) h.Self.Print = FakeSelf.Of(("src/Hatch.Cli/GoToWork.cs", "after"));
        };

        var runtime = h.Supervised with { Settings = h.Supervised.Settings with { Workspace = workspace } };

        Assert.Equal(GoToWorkCommand.RestartExitCode, await new GoToWorkCommand(runtime).RunAsync([], default));

        // The exit that hands the night to the next incarnation is a way out
        // like any other: the lock is gone before the process is.
        using var retaken = WorkspaceLock.Take(workspace, h.Root, h.Temp, out _);
        Assert.NotNull(retaken);
    }

    [Fact]
    public async Task Without_a_workspace_a_primary_matching_nothing_ends_changed_under_us_with_no_clone_attempted()
    {
        using var h = new Harness();
        var repo = Fixtures.Repository("https://example.test/elsewhere.git", primary: true, matchedRemote: null);

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-1") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", repositories: [repo]));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);

        await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Clone.Requested);
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
    }

    // ---- The banners and the readout's facts (HA-121, HA-122) ----

    [Fact]
    public async Task An_increment_opens_and_closes_with_a_banner_and_the_readout_learns_who_it_works_for()
    {
        using var h = new Harness();
        OneTicket(h);

        // Not --once: that sends one heartbeat and reads nothing back, which is
        // no place to see the "for" name arrive. MaxRuns echoes the flag this
        // process started with, the way a real server's first-seen seed does -
        // folding back a bare null would clear the cap this test relies on to
        // end the loop.
        h.Wire.Json("POST", $"/api/hatch/runners/{Uri.EscapeDataString("test:/checkout")}",
            new RunnerInstructionDto("running", null, 1, null, null, "Nathan", Drives: ["AER"]));

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "1"], default));

        // OneTicket's own stub answers the same "In Review" for the picked
        // ticket and for the reread, so this increment is a stall rather than a
        // move - the glyph logic itself has its own tests in BannerTests.cs;
        // this one is only about the wiring, that a banner opened and closed
        // and that the readout heard who the runner works for.
        Assert.Contains(h.Say.Said, l => l.StartsWith("🥚🥚🥚🥚🥚 STARTING WORK ON AER-1", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("STOPPING WORK ON AER-1", StringComparison.Ordinal));

        var snapshot = h.Runtime.Readout.Snapshot();
        Assert.Equal("Nathan", snapshot.Runner.ForName);
        Assert.Equal(1, snapshot.Runner.NightRuns);
        Assert.Null(snapshot.Increment);
        Assert.True(h.Runtime.Readout.Drives("AER"));
    }

    // ---- HA-245: a branch on origin nobody can find a pull request for ----

    /// <summary>
    /// The same shape <see cref="A_pass_that_moves_its_ticket_releases_the_claim_worked"/>
    /// uses - a dispatch that starts in progress and a post-run read that
    /// lands wherever the test wants - plus the branch and pull request
    /// HA-245's guard is judged by.
    /// </summary>
    private static void UnpublishedBoard(
        Harness h, string key = "AER-1", string endsIn = "In Review", bool endsInReview = true,
        bool branchOnOrigin = true, string? pullRequest = null, int letGo = 0,
        string? landing = null, IReadOnlyList<LandingDto>? landings = null)
    {
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row(key) });
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", $"/api/hatch/issues/{key}/claim", HttpStatusCode.NoContent);

        h.Wire.Once(
            "GET", $"/api/hatch/work/{key}", HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(
                Fixtures.Work(key, from: "In Progress", to: "In Review", letGo: letGo, landing: landing), Fixtures.Json));
        h.Wire.Json("GET", $"/api/hatch/work/{key}", Fixtures.Work(key, from: endsIn, to: endsIn, inReview: endsInReview));

        h.Wire.Json("GET", $"/api/hatch/issues/{key}", Fixtures.Issue(key, pullRequestUrl: pullRequest, landings: landings));
        h.Wire.Json("POST", $"/api/hatch/issues/{key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/hatch/issues/{key}/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", $"/api/hatch/issues/{key}/comments", Fixtures.Comment());
        h.Wire.Json("PUT", $"/api/hatch/issues/{key}/merge-check", Fixtures.MergeCheck());

        if (branchOnOrigin)
            h.Workspace.FoundFor[h.Root] =
                new Verdict(MergeVerdicts.Clean, "main", new string('a', 40), "aer-1-thing", new string('b', 40), []);
    }

    private static List<CommentCreateRequest> UnpublishedComments(Harness h, string key = "AER-1") =>
        h.Wire.To("POST", $"/api/hatch/issues/{key}/comments").Select(c => c.Read<CommentCreateRequest>()).ToList();

    [Fact]
    public async Task A_branch_on_origin_with_no_pull_request_is_noted_once_and_released_dropped()
    {
        using var h = new Harness();
        UnpublishedBoard(h);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        var written = UnpublishedComments(h);
        Assert.Single(written);
        Assert.DoesNotContain(written, c => c.Kind == "question");
        Assert.Contains("no pull request recorded", written[0].Body, StringComparison.Ordinal);

        var released = Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains("outcome=dropped", released.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_second_occurrence_in_a_row_adds_a_question_whose_labels_are_not_the_stall_pair()
    {
        using var h = new Harness();
        UnpublishedBoard(h, letGo: 1);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        var written = UnpublishedComments(h);
        Assert.Equal(2, written.Count);
        Assert.Equal("question", written[1].Kind);

        var options = written[1].Options!;
        Assert.Contains(options, o => o.Label == Unpublished.Recorded);
        Assert.Contains(options, o => o.Label == Unpublished.NotNeeded);

        var released = Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains("outcome=dropped", released.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pull_request_already_recorded_means_the_guard_never_fires()
    {
        using var h = new Harness();
        UnpublishedBoard(h, pullRequest: "https://forge.example/pulls/1");

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Empty(UnpublishedComments(h));
        var released = Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains("outcome=worked", released.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_trunk_landing_ticket_with_no_landing_recorded_is_noted_once_and_released_dropped()
    {
        using var h = new Harness();
        UnpublishedBoard(h, branchOnOrigin: false, landing: Landings.Trunk);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        var written = UnpublishedComments(h);
        Assert.Single(written);
        Assert.DoesNotContain(written, c => c.Kind == "question");
        Assert.Contains("no landing recorded", written[0].Body, StringComparison.Ordinal);
        Assert.Contains("hatch landed", written[0].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("pull request", written[0].Body, StringComparison.Ordinal);

        var released = Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains("outcome=dropped", released.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_second_occurrence_in_a_row_on_a_trunk_landing_ticket_asks_with_landing_labelled_options()
    {
        using var h = new Harness();
        UnpublishedBoard(h, branchOnOrigin: false, landing: Landings.Trunk, letGo: 1);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        var written = UnpublishedComments(h);
        Assert.Equal(2, written.Count);
        Assert.Equal("question", written[1].Kind);

        var options = written[1].Options!;
        Assert.Contains(options, o => o.Label == Unpublished.Landed);
        Assert.Contains(options, o => o.Label == Unpublished.LandingNotNeeded);

        var released = Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains("outcome=dropped", released.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_trunk_landing_ticket_with_a_landing_recorded_means_the_guard_never_fires()
    {
        using var h = new Harness();
        UnpublishedBoard(h, branchOnOrigin: false, landing: Landings.Trunk, landings: [Fixtures.Landing()]);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Empty(UnpublishedComments(h));
        var released = Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains("outcome=worked", released.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_trunk_landing_ticket_with_a_landing_recorded_is_never_flagged_even_with_a_branch_on_origin()
    {
        using var h = new Harness();
        UnpublishedBoard(h, branchOnOrigin: true, landing: Landings.Trunk, landings: [Fixtures.Landing()]);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Empty(UnpublishedComments(h));
        var released = Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains("outcome=worked", released.Query, StringComparison.Ordinal);
    }

    /// <summary>Acceptance criterion 5: a ticket with no branch at all never reaches this guard.</summary>
    [Fact]
    public async Task No_branch_on_origin_at_all_means_the_guard_never_fires()
    {
        using var h = new Harness();
        UnpublishedBoard(h, branchOnOrigin: false);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Empty(UnpublishedComments(h));
        var released = Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains("outcome=worked", released.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Moving_somewhere_that_is_not_the_review_column_never_fires_the_guard()
    {
        using var h = new Harness();
        UnpublishedBoard(h, endsIn: "Backlog", endsInReview: false);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Empty(UnpublishedComments(h));
        var released = Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains("outcome=worked", released.Query, StringComparison.Ordinal);
    }

    /// <summary>A conflict increment that resolves is judged by its branch exactly like an ordinary move.</summary>
    [Fact]
    public async Task A_resolved_conflict_with_a_branch_on_origin_and_no_pull_request_fires_the_guard_too()
    {
        using var h = new Harness();
        const string key = "AER-1";
        h.Wire.Json("GET", Queue, new[] { Fixtures.ConflictRow(key) });
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", $"/api/hatch/issues/{key}/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", $"/api/hatch/work/{key}", Fixtures.ConflictWork(key) with { InReview = true });
        h.Wire.Json("GET", $"/api/hatch/issues/{key}", Fixtures.Issue(key));
        h.Wire.Json("POST", $"/api/hatch/issues/{key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/hatch/issues/{key}/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", $"/api/hatch/issues/{key}/comments", Fixtures.Comment());
        h.Wire.Json("PUT", $"/api/hatch/issues/{key}/merge-check", Fixtures.MergeCheck());

        // Conflicted before the session, clean after it - a session that
        // resolved the merge and pushed, but never opened a pull request.
        h.Workspace.Verdicts[(h.Root, key)] =
            new Verdict(MergeVerdicts.Conflicted, "main", new string('a', 40), "aer-1-thing", new string('b', 40), ["a.txt"]);
        h.Sessions.Behaviour = (_, _, _) =>
        {
            h.Workspace.Verdicts[(h.Root, key)] =
                new Verdict(MergeVerdicts.Clean, "main", new string('c', 40), "aer-1-thing", new string('b', 40), []);
            return Task.FromResult(new SessionResult(0, ""));
        };
        h.Workspace.FoundFor[h.Root] =
            new Verdict(MergeVerdicts.Clean, "main", new string('c', 40), "aer-1-thing", new string('b', 40), []);

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        Assert.Contains(h.Say.Said, l => l.Contains("conflicts with main resolved", StringComparison.Ordinal));

        var written = UnpublishedComments(h, key);
        Assert.Single(written);

        var released = Assert.Single(h.Wire.To("DELETE", $"/api/hatch/issues/{key}/claim"));
        Assert.Contains("outcome=dropped", released.Query, StringComparison.Ordinal);
    }

    /// <summary>
    /// An ordinary stall - the ticket left where it found it - must never
    /// also trip this guard, even with a branch on origin and no pull
    /// request: the two guards' conditions are mutually exclusive, and the
    /// existing stall guard owns this case on its own.
    /// </summary>
    [Fact]
    public async Task A_genuine_stall_never_also_trips_the_unpublished_guard()
    {
        using var h = new Harness();

        // An ordinary advance that starts and ends in review - the same
        // column both before and after, which is a stall - with a branch on
        // origin and no pull request all the same.
        UnpublishedBoard(h, endsIn: "In Review", endsInReview: true);
        h.Wire.Replace(
            "GET", "/api/hatch/work/AER-1", HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(
                Fixtures.Work("AER-1", from: "In Review", to: "In Review", inReview: true), Fixtures.Json));

        Assert.Equal(0, await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default));

        var written = UnpublishedComments(h);
        Assert.Single(written);
        Assert.Contains("where it found it and let it go", written[0].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("pull request", written[0].Body, StringComparison.Ordinal);

        var released = Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
        Assert.Contains("outcome=dropped", released.Query, StringComparison.Ordinal);
    }
}

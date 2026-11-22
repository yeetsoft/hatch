using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// A machine that is not signed in to its forge, and what the loop does about it: say so at the start,
/// ask again before each increment and at the top of each pass, and tell the board on every beat and every
/// read. It decides nothing - dispatch is the board's - so a ticket is worked whatever the forge said.
/// </summary>
public sealed class GoToWorkForgeSignInTests
{
    private const string Queue = "/api/work/queue";
    private const string Beat = "/api/runners/sessions";

    private static readonly ForgeSignInAnswer SignedOut = new("fails", "You are not logged into any accounts on forge.example");
    private static readonly ForgeSignInAnswer SignedIn = new("works", null);

    private static void Ticket(Harness h, string key = "AER-1")
    {
        h.Wire.Json("GET", Queue, new[] { Fixtures.Row(key) });
        h.Wire.Reply("POST", $"/api/issues/{key}/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", $"/api/issues/{key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", $"/api/issues/{key}/claim", HttpStatusCode.NoContent);
        h.Wire.Json("GET", $"/api/work/{key}", Fixtures.Work(key, from: "In Review"));
        h.Wire.Json("POST", $"/api/issues/{key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("POST", $"/api/issues/{key}/comments", Fixtures.Comment());
        h.Wire.Json("PUT", $"/api/issues/{key}/stall", Fixtures.Issue(key));
        h.Wire.Json("GET", $"/api/issues/{key}/questions", Array.Empty<QuestionDto>());
    }

    private static async Task<int> Once(Harness h) => await new GoToWorkCommand(h.Runtime).RunAsync(["--once"], default);

    private static async Task<int> OneRun(Harness h) => await new GoToWorkCommand(h.Runtime).RunAsync(["--max-runs", "1"], default);

    private static List<RunnerHeartbeatRequest> Beats(Harness h) =>
        [.. h.Wire.To("POST", Beat).Select(c => c.Read<RunnerHeartbeatRequest>())];

    // ---- The start-up line ----

    [Fact]
    public async Task A_forge_that_refuses_this_runner_is_said_once_at_the_start_with_what_gh_said()
    {
        using var h = new Harness();
        h.Forge.SignIn = SignedOut;
        Ticket(h);

        await Once(h);

        var line = Assert.Single(h.Say.Complained, l => l.Contains("not logged into any accounts", StringComparison.Ordinal));
        Assert.Contains("builds and pull requests will not be read or merged there", line, StringComparison.Ordinal);
        Assert.Contains(h.Runtime.Root, line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_forge_that_refuses_this_runner_leaves_the_ticket_to_be_worked()
    {
        using var h = new Harness();
        h.Forge.SignIn = SignedOut;
        Ticket(h);

        Assert.Equal(0, await Once(h));

        Assert.Single(h.Sessions.Spawned);
    }

    [Fact]
    public async Task A_forge_that_accepts_this_runner_is_not_mentioned_at_the_start()
    {
        using var h = new Harness();
        h.Forge.SignIn = SignedIn;
        Ticket(h);

        await Once(h);

        Assert.DoesNotContain(h.Say.Complained, l => l.Contains("pull requests will not be read", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("pull requests will not be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_forge_that_could_not_be_reached_is_not_mentioned_at_the_start()
    {
        using var h = new Harness();
        Ticket(h);

        await Once(h);

        Assert.DoesNotContain(h.Say.Complained, l => l.Contains("pull requests will not be read", StringComparison.Ordinal));
    }

    // ---- Before each increment ----

    [Fact]
    public async Task The_checkout_the_session_will_run_in_is_asked_before_the_increment()
    {
        using var h = new Harness();
        h.Forge.SignIn = SignedIn;
        Ticket(h);

        await Once(h);

        // Once at the start for the checkout, and the increment's own ask for the same one is held
        // inside the minute, which is the rule: never twice in a minute for one checkout.
        Assert.Equal([(h.Runtime.Root, (string?)null)], h.Forge.SignInAsked);
    }

    [Fact]
    public async Task The_increments_own_ask_is_made_when_the_start_has_not_asked_that_checkout_recently()
    {
        using var h = new Harness();

        // Every read of the clock moves it two minutes on, so the minute between asks never holds one back.
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero))
        {
            AutoAdvanceAmount = TimeSpan.FromMinutes(2),
        };
        var workspaceAtAsk = new List<List<string>>();
        var runtime = h.Runtime with
        {
            Clock = clock,
            ForgeProbe = (_, _, _) =>
            {
                workspaceAtAsk.Add(h.Workspace.Calls.ToList());
                return Task.FromResult(SignedIn);
            },
        };
        Ticket(h);

        Assert.Equal(0, await new GoToWorkCommand(runtime).RunAsync(["--max-runs", "1"], default));

        // After the checkout was prepared and before its branch was entered, as the signing check is.
        Assert.Contains(workspaceAtAsk, calls =>
            calls.Any(c => c.StartsWith("prepare", StringComparison.Ordinal))
            && !calls.Any(c => c.StartsWith("enter", StringComparison.Ordinal)));
    }

    // ---- A reason from the poll ----

    private const string Review = "/api/work/review";
    private static readonly string Trunk = new('a', 40);
    private static readonly string Tip = new('b', 40);

    /// <summary>The poll could not read a build, so it says why - and the forge is asked whether the sign-in is what is wrong.</summary>
    [Fact]
    public async Task A_build_the_poll_could_not_read_asks_whether_the_runner_is_signed_in()
    {
        using var h = new Harness();
        Ticket(h);
        h.Wire.Json("GET", Review, new[] { Fixtures.Review("AER-9") });
        h.Wire.Reply(
            "PUT", "/api/issues/AER-9/merge-check", HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(Fixtures.MergeCheck(), Fixtures.Json));
        h.Workspace.HeadsFor[h.Root] = new RemoteHeads(
            "main", new Dictionary<string, string> { ["main"] = Trunk, ["aer-9-thing"] = Tip });
        h.Workspace.Verdicts[(h.Root, "AER-9")] = new Verdict(
            MergeVerdicts.Conflicted, "main", Trunk, "aer-9-thing", Tip, ["a.txt"]);
        h.Forge.Answer = new ForgeAnswer(null, "HTTP 401: Bad credentials");
        h.Forge.SignIn = SignedOut;

        // Every read of the clock moves it two minutes on, so the minute between asks never holds one back.
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero))
        {
            AutoAdvanceAmount = TimeSpan.FromMinutes(2),
        };
        var routesAtAsk = new List<List<string>>();
        var runtime = h.Runtime with
        {
            Clock = clock,
            ForgeProbe = (_, _, _) =>
            {
                routesAtAsk.Add(h.Wire.Calls.Select(c => c.Route).ToList());
                return Task.FromResult(SignedOut);
            },
        };

        await new GoToWorkCommand(runtime).RunAsync(["--once"], default);

        Assert.Contains(h.Say.Complained, l => l.Contains("could not read builds", StringComparison.Ordinal));
        Assert.Contains(routesAtAsk, routes => routes.Contains($"GET {Review}"));
    }

    // ---- What the board is told ----

    [Fact]
    public async Task The_beat_carries_the_answer_and_the_reason_beside_a_failure()
    {
        using var h = new Harness();
        h.Forge.SignIn = SignedOut;
        Ticket(h);

        await OneRun(h);

        var beat = Beats(h)[0];
        Assert.Equal(RunnerForge.Fails, beat.Forge);
        Assert.Equal(SignedOut.Why, beat.ForgeWhy);
    }

    [Fact]
    public async Task The_beat_carries_no_reason_beside_works()
    {
        using var h = new Harness();
        h.Forge.SignIn = SignedIn;
        Ticket(h);

        await OneRun(h);

        Assert.All(Beats(h), b =>
        {
            Assert.Equal(RunnerForge.Works, b.Forge);
            Assert.Null(b.ForgeWhy);
        });
    }

    [Fact]
    public async Task A_checkout_with_no_answer_says_nothing_on_the_beat()
    {
        using var h = new Harness();
        Ticket(h);

        await OneRun(h);

        Assert.All(Beats(h), b =>
        {
            Assert.Null(b.Forge);
            Assert.Null(b.ForgeWhy);
        });
    }

    /// <summary>A once beat shares the loop's row and sends nothing about it, so it cannot blank or overwrite what the loop said.</summary>
    [Fact]
    public async Task A_once_beat_sends_neither()
    {
        using var h = new Harness();
        h.Forge.SignIn = SignedOut;
        Ticket(h);

        await Once(h);

        var beats = Beats(h);
        Assert.NotEmpty(beats);
        Assert.All(beats, b =>
        {
            Assert.Equal(RunnerKinds.Once, b.Kind);
            Assert.Null(b.Forge);
            Assert.Null(b.ForgeWhy);
        });
    }

    [Fact]
    public async Task The_queue_read_and_a_hop_carry_what_the_forge_said()
    {
        using var h = new Harness();
        h.Forge.SignIn = SignedOut;
        h.Wire.Once(
            "GET", Queue, HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(new[] { Fixtures.Row("AER-1", hop: true, hopKind: HopKinds.Approve) }, Fixtures.Json));
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("POST", "/api/work/AER-1/hop", Fixtures.Issue("AER-1"));

        Assert.Equal(0, await Once(h));

        var expected = $"forge=fails&forgeWhy={Uri.EscapeDataString(SignedOut.Why!)}";
        Assert.All(h.Wire.To("GET", Queue), c => Assert.Contains(expected, c.Query));
        Assert.Contains(expected, h.Wire.To("POST", "/api/work/AER-1/hop")[0].Query);
    }

    [Fact]
    public async Task A_read_carries_no_forge_parameter_where_nothing_was_answered()
    {
        using var h = new Harness();
        Ticket(h);

        await Once(h);

        Assert.All(h.Wire.To("GET", Queue), c => Assert.DoesNotContain("forge", c.Query, StringComparison.Ordinal));
    }

    // ---- hatch work ----

    private static void Bookkeeping(Harness h, string key)
    {
        h.Wire.Json("POST", $"/api/issues/{key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/issues/{key}/questions", Array.Empty<QuestionDto>());
        h.Wire.Reply("DELETE", $"/api/issues/{key}/claim", HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("works", null, "forge=works")]
    [InlineData("fails", "no key & more", "forge=fails&forgeWhy=no%20key%20%26%20more")]
    public async Task The_named_read_carries_what_the_forge_said_asked_before_the_read(string state, string? why, string expected)
    {
        using var h = new Harness();
        h.Forge.SignIn = new ForgeSignInAnswer(state, why);
        h.Wire.Json("GET", "/api/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Reply("POST", "/api/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", "/api/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        Bookkeeping(h, "AER-1");

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync(["AER-1"], default));

        var call = h.Wire.To("GET", "/api/work/AER-1")[0];
        Assert.Contains(expected, call.Query);
        if (state != "fails") Assert.DoesNotContain("forgeWhy=", call.Query);
    }

    [Fact]
    public async Task A_named_dry_run_asks_before_it_reads()
    {
        using var h = new Harness();
        h.Forge.SignIn = SignedIn;
        h.Wire.Json("GET", "/api/work/AER-1", Fixtures.Work("AER-1"));

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync(["AER-1", "--dry-run"], default));

        Assert.NotEmpty(h.Forge.SignInAsked);
        Assert.Contains("forge=works", h.Wire.To("GET", "/api/work/AER-1")[0].Query);
    }

    [Fact]
    public async Task A_named_run_says_a_refusing_forge_at_the_start()
    {
        using var h = new Harness();
        h.Forge.SignIn = SignedOut;
        h.Wire.Json("GET", "/api/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Reply("POST", "/api/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", "/api/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        Bookkeeping(h, "AER-1");

        await new WorkCommand(h.Runtime).RunAsync(["AER-1"], default);

        Assert.Single(h.Say.Complained, l => l.Contains("not logged into any accounts", StringComparison.Ordinal));
    }

    // ---- Asking every checkout at once ----

    private static ProjectDto Project(params ProjectRepositoryDto[] repositories) =>
        new(1, "AER", "AER", 0, DateTimeOffset.UnixEpoch, null, null, repositories, null, [], false, false);

    [Fact]
    public async Task Every_checkout_that_is_due_is_asked_with_the_identity_the_board_binds_and_the_board_is_read_once()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/projects", new[]
        {
            Project(
                new ProjectRepositoryDto("git@forge.example:o/a.git", "forge.example/o/a", null),
                new ProjectRepositoryDto("git@forge.example:o/b.git", "forge.example/o/b", null)),
        });
        var runtime = h.Runtime with
        {
            Checkouts =
            [
                new CheckoutEntry("/checkouts/a", "git@forge.example:o/a.git", Standing: false),
                new CheckoutEntry("/checkouts/b", "git@forge.example:o/b.git", Standing: false),
                new CheckoutEntry("/checkouts/c", null, Standing: false),
            ],
        };
        h.Forge.SignIn = SignedIn;

        var asked = await runtime.AskForgesAsync(default);

        Assert.Equal(3, asked.Count);
        Assert.Equal(
            ["forge.example/o/a", "forge.example/o/b", null],
            h.Forge.SignInAsked.OrderBy(a => a.Path, StringComparer.Ordinal).Select(a => a.Canonical));
        Assert.Equal(1, h.Wire.Count("GET", "/api/projects"));

        // Nothing is due a moment later, so the board is not read for it either.
        Assert.Empty(await runtime.AskForgesAsync(default, passTop: true));
        Assert.Equal(1, h.Wire.Count("GET", "/api/projects"));
    }

    [Fact]
    public async Task A_board_that_cannot_be_read_means_no_identity_and_the_check_is_still_made()
    {
        using var h = new Harness();
        var runtime = h.Runtime with
        {
            Checkouts = [new CheckoutEntry("/checkouts/a", "git@forge.example:o/a.git", Standing: false)],
        };
        h.Forge.SignIn = SignedIn;

        await runtime.AskForgesAsync(default);

        Assert.Equal([("/checkouts/a", (string?)null)], h.Forge.SignInAsked);
    }
}

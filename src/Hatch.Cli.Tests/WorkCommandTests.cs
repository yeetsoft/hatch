using System.Diagnostics;
using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>work</c>: what it claims, what it refuses, and the one door every way out
/// of it goes through.
/// </summary>
public sealed class WorkCommandTests
{
    private const string Queue = "/api/hatch/work/queue";

    /// <summary>Everything an increment reads and writes after the session ends.</summary>
    private static void Bookkeeping(Harness h, string key)
    {
        h.Wire.Json("POST", $"/api/hatch/issues/{key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/hatch/issues/{key}/questions", Array.Empty<QuestionDto>());
        h.Wire.Reply("DELETE", $"/api/hatch/issues/{key}/claim", HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_dry_run_claims_nothing()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/next", Fixtures.Work("AER-1"));

        var code = await new WorkCommand(h.Runtime).RunAsync(["--dry-run"], default);

        Assert.Equal(0, code);
        Assert.Empty(h.Wire.Calls.Where(c => c.Path.Contains("/claim", StringComparison.Ordinal)));
        Assert.Empty(h.Sessions.Spawned);

        // It is a prompt and a print, and the prompt is the whole of it.
        Assert.Contains(h.Say.Said, l => l.Contains("## The ticket", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.StartsWith("# AER-1 In Progress -> In Review", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_ticket_another_runner_holds_refuses_and_spawns_nothing()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1",
            Fixtures.Work("AER-1", blocked: "hatch is working this from other:/tree, last heard from 8 seconds ago"));

        var code = await new WorkCommand(h.Runtime).RunAsync(["AER-1"], default);

        Assert.Equal(2, code);
        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Wire.Calls.Where(c => c.Method == "POST" && c.Path.EndsWith("/claim", StringComparison.Ordinal)));
        Assert.Contains(h.Say.Complained, l => l.Contains("other:/tree", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_blocked_ticket_prints_the_question_it_is_blocked_by()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work(
            "AER-1", blocked: "a question is open here",
            questions: [Fixtures.Question(7, body: "Which way?")]));

        Assert.Equal(2, await new WorkCommand(h.Runtime).RunAsync(["AER-1"], default));
        Assert.Contains(h.Say.Complained, l => l.Contains("Which way?", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_race_between_the_read_and_the_take_is_reported_and_nothing_is_spawned()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.Conflict,
            "\"hatch is working this from other:/tree, last heard from just now\"");

        Assert.Equal(2, await new WorkCommand(h.Runtime).RunAsync(["AER-1"], default));
        Assert.Empty(h.Sessions.Spawned);
        Assert.Contains(h.Say.Complained, l => l.Contains("other:/tree", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_named_tickets_primary_matching_no_checkout_here_refuses_before_it_claims()
    {
        using var h = new Harness();
        var repos = new[] { Fixtures.Repository("https://example.test/elsewhere.git", primary: true, matchedRemote: null) };
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", repositories: repos));

        Assert.Equal(2, await new WorkCommand(h.Runtime).RunAsync(["AER-1"], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Wire.Calls.Where(c => c.Method == "POST" && c.Path.EndsWith("/claim", StringComparison.Ordinal)));
        Assert.Contains(h.Say.Complained, l => l.Contains("changed under us", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_named_ticket_is_claimed_before_it_is_spawned_at_and_released_after()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();

        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        Bookkeeping(h, "AER-1");

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync(["AER-1"], default));

        var order = h.Wire.Calls.Select(c => c.Route).ToList();
        var claimed = order.IndexOf("POST /api/hatch/issues/AER-1/claim");
        var released = order.IndexOf("DELETE /api/hatch/issues/AER-1/claim");

        Assert.True(claimed >= 0, "the ticket was claimed");
        Assert.True(released > claimed, "the claim was let go of after it was taken");
        Assert.Single(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
    }

    [Fact]
    public async Task A_session_that_fails_still_gives_the_ticket_back()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        Bookkeeping(h, "AER-1");

        h.Sessions.Behaviour = (_, _, _) => throw new InvalidOperationException("the CLI fell over");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WorkCommand(h.Runtime).RunAsync(["AER-1"], default));

        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
    }

    [Fact]
    public async Task With_no_CLI_to_spawn_it_refuses_before_it_reads_the_board()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Sessions.Missing = "hatch: no claude CLI on PATH.";

        Assert.Equal(1, await new WorkCommand(h.Runtime).RunAsync(["AER-1"], default));

        Assert.Contains(h.Say.Complained, l => l.Contains("no claude CLI on PATH", StringComparison.Ordinal));
        Assert.Empty(h.Sessions.Spawned);

        // Nothing was read and nothing was claimed - an increment that cannot
        // start should not take a ticket off the board to find that out.
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task A_dry_run_prints_its_prompt_with_no_CLI_installed()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/next", Fixtures.Work("AER-1"));
        h.Sessions.Missing = "hatch: no claude CLI on PATH.";

        // It spawns nothing, so it never asks. A machine with no CLI on it can
        // still read what one would have been told.
        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync(["--dry-run"], default));

        Assert.Contains(h.Say.Said, l => l.Contains("## The ticket", StringComparison.Ordinal));
        Assert.Empty(h.Say.Complained);
        Assert.Empty(h.Sessions.Spawned);
    }

    [Fact]
    public async Task An_interrupt_lets_go_of_the_ticket_before_it_exits()
    {
        using var h = new Harness();
        using var interrupting = new CancellationTokenSource();

        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        Bookkeeping(h, "AER-1");

        h.Sessions.Behaviour = FakeSessions.UntilStopped();

        var running = new WorkCommand(h.Runtime).RunAsync(["AER-1"], interrupting.Token);
        await h.Sessions.StartedWithin();

        // Ctrl-C. The token is cancelled, the session is killed, and the release
        // happens on the way out of the block the claim was taken in.
        await interrupting.CancelAsync();
        await running;

        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
    }

    [Fact]
    public async Task An_attached_session_holds_the_lease_for_as_long_as_it_runs()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync(["-i", "AER-1"], default));

        Assert.Single(h.Sessions.Attached);
        Assert.Empty(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));
    }

    [Fact]
    public async Task A_dry_run_prints_the_messages_and_marks_nothing()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/next", Fixtures.Work("AER-1", messages: [Fixtures.Message(7)]));

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync(["--dry-run"], default));

        Assert.Contains(h.Say.Said, l => l.Contains("## Said to you since the last session", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("use the other table", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Wire.Calls, c => c.Path.Contains("/messages/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_attached_session_is_handed_the_messages_in_its_prompt_and_they_are_marked_but_it_has_no_hooks()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", messages: [Fixtures.Message(7)]));
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/messages/deliver", new[] { Fixtures.Message(7) });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync(["-i", "AER-1"], default));

        var attached = Assert.Single(h.Sessions.Attached);
        Assert.Null(attached.HookSettings);
        Assert.Contains("use the other table", attached.Prompt, StringComparison.Ordinal);
        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/messages/deliver"));
    }

    [Fact]
    public async Task With_no_key_it_claims_through_the_same_walk_the_loop_uses()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-9") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-9/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-9", Fixtures.Work("AER-9"));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-9/claim/heartbeat", HttpStatusCode.NoContent);
        Bookkeeping(h, "AER-9");

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync([], default));
        Assert.Single(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-9/claim"));
    }

    [Fact]
    public async Task With_no_key_a_busy_board_refuses_the_way_the_loop_reports_it()
    {
        using var h = new Harness();
        var rows = Enumerable.Range(1, 5).Select(n => Fixtures.Row($"AER-{n}")).ToArray();

        h.Wire.Json("GET", Queue, rows);
        foreach (var row in rows)
            h.Wire.Reply("POST", $"/api/hatch/issues/{row.Issue.Key}/claim", HttpStatusCode.Conflict,
                "\"hatch is working this from other:/tree, last heard from 8 seconds ago\"");

        Assert.Equal(2, await new WorkCommand(h.Runtime).RunAsync([], default));
        Assert.Empty(h.Sessions.Spawned);
        Assert.Contains(h.Say.Said, l => l.Contains("being worked by another runner", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_key_and_an_epic_together_is_a_refusal()
    {
        using var h = new Harness();

        Assert.Equal(1, await new WorkCommand(h.Runtime).RunAsync(["AER-1", "--under", "AER-9"], default));
        Assert.Contains(h.Say.Complained, l => l.Contains("not both", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_key_and_mine_together_is_a_refusal()
    {
        using var h = new Harness();

        Assert.Equal(1, await new WorkCommand(h.Runtime).RunAsync(["AER-1", "--mine"], default));
        Assert.Contains(h.Say.Complained, l => l.Contains("not both", StringComparison.Ordinal));
        Assert.Empty(h.Wire.Calls);
    }

    [Fact]
    public async Task With_no_key_mine_is_sent_on_the_queue_read()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();

        h.Wire.Json("GET", Queue, new[] { Fixtures.Row("AER-9") });
        h.Wire.Reply("POST", "/api/hatch/issues/AER-9/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Json("GET", "/api/hatch/work/AER-9", Fixtures.Work("AER-9"));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-9/claim/heartbeat", HttpStatusCode.NoContent);
        Bookkeeping(h, "AER-9");

        Assert.Equal(0, await new WorkCommand(h.Runtime).RunAsync(["--mine"], default));
        Assert.Contains("mine=true", h.Wire.To("GET", Queue)[0].Query, StringComparison.Ordinal);
    }

    /// <summary>
    /// A 400 from a --mine pass whose key belongs to nobody prints the server's
    /// sentence and refuses, exactly the same shape any other refusal here
    /// takes - nothing about it needed the Pick.Refused/Pass.Fatal machinery
    /// go-to-work needs to keep a whole night running past one.
    /// </summary>
    [Fact]
    public async Task Mine_refused_by_the_board_prints_the_sentence_and_exits_1()
    {
        using var h = new Harness();
        h.Wire.Reply("GET", Queue, HttpStatusCode.BadRequest,
            "\"this key belongs to nobody, so it has no tickets of its own\"");

        Assert.Equal(1, await new WorkCommand(h.Runtime).RunAsync(["--mine"], default));
        Assert.Contains(h.Say.Complained, l => l.Contains("this key belongs to nobody", StringComparison.Ordinal));
        Assert.Empty(h.Sessions.Spawned);
    }

    [Fact]
    public async Task An_empty_board_says_which_of_the_three_empties_it_is()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, Array.Empty<QueueEntryDto>());
        h.Wire.Json("GET", "/api/hatch/questions", Array.Empty<QuestionDto>());

        Assert.Equal(2, await new WorkCommand(h.Runtime).RunAsync([], default));
        Assert.Contains(h.Say.Said, l => l.Contains("nothing on the board is an agent's to move", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("what is left is in a terminal column", StringComparison.Ordinal));
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
    public async Task Repo_given_on_the_command_line_ignores_whatever_HATCH_REPOS_produced()
    {
        using var h = new Harness();
        var fromSettings = new CheckoutEntry(
            Path.Combine(h.Temp, "from-settings"), "https://example.test/from-settings.git", Standing: false);
        var runtime = h.Runtime with { Checkouts = [h.Runtime.Checkouts[0], fromSettings] };

        var fromFlag = Tree(h, "from-flag", "https://example.test/from-flag.git");

        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        Bookkeeping(h, "AER-1");

        Assert.Equal(0, await new WorkCommand(runtime).RunAsync(["AER-1", "--repo", fromFlag], default));

        var call = h.Wire.To("GET", "/api/hatch/work/AER-1")[0];
        Assert.Contains($"remote={Uri.EscapeDataString("https://example.test/from-flag.git")}", call.Query);
        Assert.DoesNotContain("from-settings.git", call.Query);
    }

    [Fact]
    public async Task A_missing_repo_path_refuses_before_any_claim()
    {
        using var h = new Harness();
        var missing = Path.Combine(h.Temp, "does-not-exist");

        Assert.Equal(1, await new WorkCommand(h.Runtime).RunAsync(["AER-1", "--repo", missing], default));

        Assert.Contains(h.Say.Complained, l => l.Contains(missing, StringComparison.Ordinal));
        Assert.Empty(h.Wire.Calls);
        Assert.Empty(h.Sessions.Spawned);
    }

    [Fact]
    public async Task Started_outside_a_checkout_with_repo_is_named_after_the_first_checkout_and_sends_no_standing()
    {
        using var h = new Harness();
        var elsewhere = Tree(h, "elsewhere", "https://example.test/elsewhere.git");
        var runtime = h.Runtime with { Checkouts = [] };
        var runnerName = Checkout.Runner(null, Checkout.Host(), elsewhere);

        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        Bookkeeping(h, "AER-1");

        Assert.Equal(0, await new WorkCommand(runtime).RunAsync(["AER-1", "--repo", elsewhere], default));

        Assert.Contains(h.Wire.Calls, c => c.Path == $"/api/hatch/runners/{Uri.EscapeDataString(runnerName)}");
        Assert.Single(h.Sessions.Spawned);

        var call = h.Wire.To("GET", "/api/hatch/work/AER-1")[0];
        Assert.DoesNotContain("standing=true", call.Query);
        Assert.Contains($"remote={Uri.EscapeDataString("https://example.test/elsewhere.git")}", call.Query);
    }

    [Fact]
    public async Task A_jammed_board_counts_the_reasons_and_names_the_questions_waiting()
    {
        using var h = new Harness();
        h.Wire.Json("GET", Queue, new[]
        {
            Fixtures.Row("AER-1", "a question is open"),
            Fixtures.Row("AER-2", "a question is open"),
            Fixtures.Row("AER-3", "AER-2 has not merged"),
        });
        h.Wire.Json("GET", "/api/hatch/questions", new[] { Fixtures.Question(1), Fixtures.Question(2) });

        Assert.Equal(2, await new WorkCommand(h.Runtime).RunAsync([], default));

        Assert.Contains(h.Say.Said, l => l.Contains("3 issue(s) were on the dispatcher's path", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("2  a question is open", StringComparison.Ordinal));
        Assert.Contains(h.Say.Said, l => l.Contains("2 question(s) are waiting on you", StringComparison.Ordinal));
    }

    // ---- --workspace: cloning what the board binds (HA-19) ----

    [Fact]
    public async Task A_named_tickets_clone_happens_after_the_claim_and_before_it_is_spawned_at()
    {
        using var h = new Harness();
        var workspace = Path.Combine(h.Temp, "clones");
        var runtime = h.Runtime with { Settings = h.Runtime.Settings with { Workspace = workspace } };

        var repo = Fixtures.Repository(
            "https://example.test/owner/repo.git", canonical: "example.test/owner/repo", primary: true, matchedRemote: null);

        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", repositories: [repo]));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        Bookkeeping(h, "AER-1");

        // The claim already made, at the moment the clone is asked for - the
        // one thing that tells the order apart from the reverse of it.
        var claimedByThen = false;
        h.Clone.Watching = () => claimedByThen = h.Wire.Count("POST", "/api/hatch/issues/AER-1/claim") == 1;

        Assert.Equal(0, await new WorkCommand(runtime).RunAsync(["AER-1"], default));

        var expected = Checkouts.PathFor(workspace, "example.test/owner/repo");
        var cloned = Assert.Single(h.Clone.Requested);
        Assert.Equal("https://example.test/owner/repo.git", cloned.Remote);
        Assert.Equal(expected, cloned.Path);

        Assert.True(claimedByThen, "the clone happened after the claim, not before it");
        var spawned = Assert.Single(h.Sessions.Spawned);
        Assert.Equal(expected, spawned.Root);
    }

    [Fact]
    public async Task A_failed_clone_releases_the_lease_and_comments_the_ticket_rather_than_spawning()
    {
        using var h = new Harness();
        var workspace = Path.Combine(h.Temp, "clones");
        var runtime = h.Runtime with { Settings = h.Runtime.Settings with { Workspace = workspace } };
        h.Clone.Error = "fatal: repository not found";

        var repo = Fixtures.Repository(
            "https://example.test/owner/gone.git", canonical: "example.test/owner/gone", primary: true, matchedRemote: null);

        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", repositories: [repo]));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Guid.NewGuid()));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "noted", "comment", null, null, DateTimeOffset.UnixEpoch));

        Assert.Equal(2, await new WorkCommand(runtime).RunAsync(["AER-1"], default));

        Assert.Empty(h.Sessions.Spawned);
        Assert.Single(h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim"));

        var comment = Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));
        var body = comment.Read<CommentCreateRequest>().Body;
        Assert.Contains("could not clone", body, StringComparison.Ordinal);
        Assert.Contains("fatal: repository not found", body, StringComparison.Ordinal);
        Assert.Contains(h.Say.Complained, l => l.Contains("could not clone", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_dry_run_does_not_clone_even_with_a_workspace_and_an_unmatched_primary()
    {
        using var h = new Harness();
        var workspace = Path.Combine(h.Temp, "clones");
        var runtime = h.Runtime with { Settings = h.Runtime.Settings with { Workspace = workspace } };

        var repo = Fixtures.Repository(
            "https://example.test/owner/repo.git", canonical: "example.test/owner/repo", primary: true, matchedRemote: null);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", repositories: [repo]));

        Assert.Equal(2, await new WorkCommand(runtime).RunAsync(["--dry-run", "AER-1"], default));

        Assert.Empty(h.Clone.Requested);
        Assert.Empty(h.Sessions.Spawned);
        Assert.Empty(h.Wire.Calls.Where(c => c.Path.Contains("/claim", StringComparison.Ordinal)));
        Assert.Contains(h.Say.Complained, l => l.Contains("changed under us", StringComparison.Ordinal));
    }
}

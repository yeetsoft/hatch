using System.Net;

namespace Hatch.Cli.Tests;

/// <summary>What a starting loop does about the session its dead predecessor left.</summary>
public sealed class OrphanedTests
{
    private static readonly Guid Token = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Started = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private const string Release = "/api/issues/AER-1/claim";
    private const string Comments = "/api/issues/AER-1/comments";

    private static void Leave(Harness h, string? sessionId = "sess-1", string board = "https://hatch.example", int pid = 4242) =>
        new SessionRecord(pid, Started.Ticks, "AER-1", Token, DateTimeOffset.UnixEpoch, board, sessionId)
            .Write(SessionRecord.PathFor(h.Root, h.Temp));

    private static Task<bool> EndAsync(
        Harness h, bool alive = true, DateTime? startedAt = null, List<int>? killed = null, string baseUrl = "https://hatch.example") =>
        Orphaned.EndAsync(
            h.Root, h.Temp, h.Board, baseUrl, h.Runtime.Adapter, h.Say, default,
            alive: _ => alive,
            startTimeOf: _ => alive ? startedAt ?? Started : null,
            kill: pid => killed?.Add(pid),
            ownPid: 1);

    private static bool Recorded(Harness h) => File.Exists(SessionRecord.PathFor(h.Root, h.Temp));

    [Fact]
    public async Task A_live_pid_that_started_when_the_record_says_is_killed_and_the_claim_released_once_with_one_comment()
    {
        using var h = new Harness();
        Leave(h);
        h.Wire.Reply("DELETE", Release, HttpStatusCode.NoContent);
        h.Wire.Json("POST", Comments, Fixtures.Comment());
        var killed = new List<int>();

        Assert.True(await EndAsync(h, killed: killed));

        Assert.Equal([4242], killed);
        var released = Assert.Single(h.Wire.To("DELETE", Release));
        Assert.Contains($"token={Token}", released.Query, StringComparison.Ordinal);
        Assert.Contains("outcome=abandoned", released.Query, StringComparison.Ordinal);

        var body = Assert.Single(h.Wire.To("POST", Comments)).Read<CommentCreateRequest>().Body;
        Assert.Contains("A loop died while an unattended increment was running here", body, StringComparison.Ordinal);
        Assert.Contains("claude --resume sess-1", body, StringComparison.Ordinal);
        Assert.False(Recorded(h));
        Assert.Contains(h.Say.Said, l => l.Contains("ended its session and released the claim", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_record_with_no_session_id_says_there_is_no_session_to_resume()
    {
        using var h = new Harness();
        Leave(h, sessionId: null);
        h.Wire.Reply("DELETE", Release, HttpStatusCode.NoContent);
        h.Wire.Json("POST", Comments, Fixtures.Comment());

        await EndAsync(h);

        var body = Assert.Single(h.Wire.To("POST", Comments)).Read<CommentCreateRequest>().Body;
        Assert.DoesNotContain("--resume", body, StringComparison.Ordinal);
        Assert.Contains("no session to resume", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reused_pid_is_not_killed_but_the_claim_is_still_released()
    {
        using var h = new Harness();
        Leave(h);
        h.Wire.Reply("DELETE", Release, HttpStatusCode.NoContent);
        h.Wire.Json("POST", Comments, Fixtures.Comment());
        var killed = new List<int>();

        await EndAsync(h, startedAt: Started.AddHours(3), killed: killed);

        Assert.Empty(killed);
        Assert.Single(h.Wire.To("DELETE", Release));
        Assert.Single(h.Wire.To("POST", Comments));
        Assert.False(Recorded(h));
    }

    [Fact]
    public async Task A_start_time_a_moment_off_is_still_that_process()
    {
        using var h = new Harness();
        Leave(h);
        h.Wire.Reply("DELETE", Release, HttpStatusCode.NoContent);
        h.Wire.Json("POST", Comments, Fixtures.Comment());
        var killed = new List<int>();

        await EndAsync(h, startedAt: Started.AddMilliseconds(900), killed: killed);

        Assert.Equal([4242], killed);
    }

    [Fact]
    public async Task A_gone_pid_releases_the_claim_without_killing()
    {
        using var h = new Harness();
        Leave(h);
        h.Wire.Reply("DELETE", Release, HttpStatusCode.NoContent);
        h.Wire.Json("POST", Comments, Fixtures.Comment());
        var killed = new List<int>();

        await EndAsync(h, alive: false, killed: killed);

        Assert.Empty(killed);
        Assert.Single(h.Wire.To("DELETE", Release));
        Assert.Single(h.Wire.To("POST", Comments));
        Assert.False(Recorded(h));
    }

    [Fact]
    public async Task The_loops_own_pid_is_never_killed()
    {
        using var h = new Harness();
        Leave(h, pid: 1);
        h.Wire.Reply("DELETE", Release, HttpStatusCode.NoContent);
        h.Wire.Json("POST", Comments, Fixtures.Comment());
        var killed = new List<int>();

        await EndAsync(h, killed: killed);

        Assert.Empty(killed);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_claim_that_lapsed_or_was_taken_or_cleared_gets_no_comment(HttpStatusCode answer)
    {
        using var h = new Harness();
        Leave(h);
        h.Wire.Reply("DELETE", Release, answer);
        var killed = new List<int>();

        await EndAsync(h, killed: killed);

        Assert.Equal([4242], killed);
        Assert.Empty(h.Wire.To("POST", Comments));
        Assert.False(Recorded(h));
        Assert.Contains(h.Say.Said, l => l.Contains("already gone", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_board_that_will_not_release_is_complained_of_and_the_record_is_still_gone()
    {
        using var h = new Harness();
        Leave(h);
        h.Wire.Reply("DELETE", Release, HttpStatusCode.InternalServerError);

        await EndAsync(h);

        Assert.Empty(h.Wire.To("POST", Comments));
        Assert.False(Recorded(h));
        Assert.Contains(h.Say.Complained, l => l.Contains("could not be released", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_record_for_another_board_is_reported_and_left_untouched()
    {
        using var h = new Harness();
        Leave(h, board: "https://elsewhere.example");
        var killed = new List<int>();

        Assert.False(await EndAsync(h, killed: killed));

        Assert.Empty(killed);
        Assert.Empty(h.Wire.To("DELETE", Release));
        Assert.True(Recorded(h));
        Assert.Contains(h.Say.Complained, l => l.Contains("another board", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_trailing_slash_on_either_base_is_still_this_board()
    {
        using var h = new Harness();
        Leave(h, board: "https://hatch.example/");
        h.Wire.Reply("DELETE", Release, HttpStatusCode.NoContent);
        h.Wire.Json("POST", Comments, Fixtures.Comment());

        Assert.True(await EndAsync(h));

        Assert.Single(h.Wire.To("DELETE", Release));
    }

    [Fact]
    public async Task A_loop_with_no_record_sends_nothing()
    {
        using var h = new Harness();

        Assert.False(await EndAsync(h));

        Assert.Empty(h.Wire.Calls);
        Assert.Empty(h.Say.Said);
    }

    [Fact]
    public void A_pid_that_does_not_exist_reads_as_not_alive_and_without_a_start_time()
    {
        Assert.False(Orphaned.StillRunning(int.MaxValue));
        Assert.Null(Orphaned.StartTimeOf(int.MaxValue));
    }

    [Fact]
    public void This_process_reads_as_alive_and_with_a_start_time_in_the_past()
    {
        Assert.True(Orphaned.StillRunning(Environment.ProcessId));
        Assert.True(Orphaned.StartTimeOf(Environment.ProcessId) < DateTime.UtcNow);
    }
}

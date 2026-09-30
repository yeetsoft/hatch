using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>
/// One increment, and the two things that must still be true when its lease goes
/// underneath it: the meter reading goes up, and nothing else does.
/// </summary>
public sealed class IncrementTests
{
    private static async Task<(Claim Claim, Harness Harness)> HoldingAsync(Harness h, string key, Guid token)
    {
        h.Wire.Reply("POST", $"/api/hatch/issues/{key}/claim", HttpStatusCode.OK, Fixtures.Taken(token));
        h.Wire.Reply("DELETE", $"/api/hatch/issues/{key}/claim", HttpStatusCode.NoContent);

        var (claim, _) = await Claim.TakeAsync(h.Client, key, "test:/checkout", default, Harness.Beat);
        return (claim!, h);
    }

    [Fact]
    public async Task An_increment_posts_its_bill_and_asks_the_board_where_the_ticket_ended_up()
    {
        using var h = new Harness();
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.Moved);
        Assert.Equal("In Progress -> In Review", report.Outcome);
        Assert.Equal("s-1", report.SessionId);
        Assert.Equal(1.5m, report.Cost);

        var billed = h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log")[0].Read<WorkLogEntryRequest>();
        Assert.Equal("s-1", billed.SessionId);
        Assert.Equal("Did a thing", billed.Title);
        Assert.Equal("In detail.", billed.Summary);
        Assert.Equal(12, billed.Turns);
        Assert.NotEqual(default, billed.StartedAt);

        // The board read carries the runner's own token, so its own lease does
        // not fold its own dispatch.
        Assert.Contains($"heldToken={token}", h.Wire.To("GET", "/api/hatch/work/AER-1")[0].Query,
            StringComparison.OrdinalIgnoreCase);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-116: a minute of Hatch not answering costs nothing. Two blips on the
    /// post-session read - a 503 and a 502 - are ridden out inside
    /// <see cref="HatchClient.Send"/> itself, so the increment never even sees
    /// a failure: it reads as moved, with no stall comment and no flag, exactly
    /// as if the outage had never happened.
    /// </summary>
    [Fact]
    public async Task A_post_session_read_that_fails_twice_then_answers_still_reads_as_moved()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var h = new Harness(clock: clock);
        var token = Guid.NewGuid();
        var (claim, _) = await HoldingAsync(h, "AER-1", token);

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Once("GET", "/api/hatch/work/AER-1", HttpStatusCode.ServiceUnavailable);
        h.Wire.Once("GET", "/api/hatch/work/AER-1", HttpStatusCode.BadGateway);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var running = h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        // Advances the fake clock past the backoff the two blips cost - real
        // milliseconds spent polling so the call's own seconds never have to
        // be.
        for (var i = 0; i < 10 && !running.IsCompleted; i++)
        {
            await Task.Delay(5);
            clock.Advance(TimeSpan.FromSeconds(15));
        }

        var report = await running;

        Assert.True(report.Moved);
        Assert.Equal("In Progress -> In Review", report.Outcome);
        Assert.Null(report.Flag);
        Assert.Equal(3, h.Wire.Count("GET", "/api/hatch/work/AER-1"));

        // A stall comment here would flag another runner's increment as ours,
        // exactly as it does today for a read that answers on the first try -
        // there is nothing here for anybody to see.
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// HA-118: the first increment in a row to leave a ticket where it found
    /// it is let go quietly - one comment, no question - rather than flagged.
    /// Most of the time whatever happened is weather, and a retry a few
    /// minutes later just works, so the ticket goes straight back onto the
    /// board rather than costing a person a trip to answer a question about it.
    /// </summary>
    [Fact]
    public async Task A_ticket_that_did_not_move_is_let_go_the_first_time()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1", letGo: 0), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.Stalled);
        Assert.True(report.LetGo);
        Assert.Equal("let go", report.Flag);
        Assert.Equal(ClaimOutcomes.Dropped, report.ReleaseOutcome);

        // One comment naming the session, and no question: the ticket is free
        // for the very next pass to try again.
        var written = h.Wire.To("POST", "/api/hatch/issues/AER-1/comments");
        Assert.Single(written);
        Assert.Contains("let it go", written[0].Read<CommentCreateRequest>().Body, StringComparison.Ordinal);
        Assert.Contains("the session ended without moving it", written[0].Read<CommentCreateRequest>().Body, StringComparison.Ordinal);
        Assert.Contains("claude --resume s-1", written[0].Read<CommentCreateRequest>().Body, StringComparison.Ordinal);
        Assert.Null(written[0].Read<CommentCreateRequest>().Kind);

        await claim.ReleaseAsync();
    }

    /// <summary>The second increment in a row to do the same is flagged exactly as every stall used to be.</summary>
    [Fact]
    public async Task A_ticket_that_did_not_move_a_second_time_in_a_row_is_flagged_where_a_person_will_see_it()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", letGo: 1));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1", letGo: 1), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.Stalled);
        Assert.False(report.LetGo);
        Assert.Equal("flagged", report.Flag);
        Assert.Equal(ClaimOutcomes.Dropped, report.ReleaseOutcome);

        // A comment naming the session, mentioning this is the second increment
        // in a row, and a question, which is what actually stops the next pass
        // spending the same money the same way.
        var written = h.Wire.To("POST", "/api/hatch/issues/AER-1/comments");
        Assert.Equal(2, written.Count);
        Assert.Contains("second increment in a row", written[0].Read<CommentCreateRequest>().Body, StringComparison.Ordinal);
        Assert.Contains("claude --resume s-1", written[0].Read<CommentCreateRequest>().Body, StringComparison.Ordinal);
        Assert.Equal("question", written[1].Read<CommentCreateRequest>().Kind);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_ticket_already_waiting_on_a_question_gets_the_comment_and_not_a_second_question()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        // The question is already open before the increment starts, so the
        // session did not ask it - it is the second increment in a row to
        // leave the ticket where it was, which is what puts this on the
        // flagged path rather than a quiet let-go.
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", letGo: 1, questions: [Fixtures.Question(42)]));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", new[] { Fixtures.Question(42) });
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1", letGo: 1, questions: [Fixtures.Question(42)]), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.Equal("waiting on a question", report.Flag);
        Assert.Equal(ClaimOutcomes.Dropped, report.ReleaseOutcome);
        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));

        // The session raised no question of its own this time - the one open
        // is the same one that was already there.
        Assert.Equal(0, report.Asked);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// The session asked its own question on the way out: that already does
    /// everything a stall comment would, so nothing further is written and the
    /// claim reads as having worked rather than been dropped.
    /// </summary>
    [Fact]
    public async Task A_session_that_asked_its_own_question_is_not_let_go_or_flagged()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", new[] { Fixtures.Question(42) });

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.Equal(1, report.Asked);
        Assert.False(report.LetGo);
        Assert.Null(report.Flag);
        Assert.Equal(ClaimOutcomes.Worked, report.ReleaseOutcome);
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));

        await claim.ReleaseAsync();
    }

    /// <summary>An erroring session's let-go comment quotes its own result text.</summary>
    [Fact]
    public async Task An_erroring_sessions_let_go_comment_quotes_its_result_text()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.Result(error: true, said: "the build failed on main"));
            return Task.FromResult(new SessionResult(1, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.LetGo);
        Assert.Equal(ClaimOutcomes.Dropped, report.ReleaseOutcome);

        var body = h.Wire.To("POST", "/api/hatch/issues/AER-1/comments")[0].Read<CommentCreateRequest>().Body;
        Assert.Contains("the session ended with an error", body, StringComparison.Ordinal);
        Assert.Contains("the build failed on main", body, StringComparison.Ordinal);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_usage_limit_writes_no_stall_comment_and_asks_nothing()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.Result(
                error: true, said: "You've hit your session limit · resets 7:40pm (America/New_York)"));
            return Task.FromResult(new SessionResult(1, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.UsageLimited);
        Assert.True(report.UsageLimitResetKnown);
        Assert.False(report.Moved);
        Assert.Null(report.Flag);
        Assert.Equal(0, report.Asked);
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));
        Assert.Contains(h.Say.Said, l => l.Contains("out of Claude usage", StringComparison.Ordinal));

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task An_ordinary_error_still_stalls_rather_than_reading_as_a_usage_limit()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.Result(error: true, said: "the build failed on main"));
            return Task.FromResult(new SessionResult(1, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/comments",
            new CommentDto(1, "hatch", "…", "comment", null, null, DateTimeOffset.UnixEpoch));

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.False(report.UsageLimited);
        Assert.True(report.Stalled);
        Assert.Equal("let go", report.Flag);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_quoted_limit_sentence_in_an_ordinary_successful_run_is_not_mistaken_for_one()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            // Talking about this very feature, not reporting a real limit -
            // and the run ends cleanly, exit 0, with no result event lost.
            onLine?.Invoke(
                """{"type":"assistant","message":{"content":[{"type":"text","text":"The sample sentence is You've hit your session limit · resets 7:40pm (America/New_York)"}]}}""");
            return Task.FromResult(new SessionResult(0, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", to: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.False(report.UsageLimited);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_crash_with_no_result_event_still_reads_a_real_limit_off_the_assistants_last_words()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            // The stream is cut off before a "result" event ever arrives -
            // the one place a limit hit mid-stream still shows up.
            onLine?.Invoke(
                """{"type":"assistant","message":{"content":[{"type":"text","text":"You've hit your session limit · resets 7:40pm (America/New_York)"}]}}""");
            return Task.FromResult(new SessionResult(1, ""));
        };

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.UsageLimited);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_refused_heartbeat_stops_the_session_and_writes_nothing_past_the_work_log()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.Conflict,
            "\"this claim was taken over\"");
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        h.Sessions.Behaviour = FakeSessions.UntilStopped();

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.LostLease);
        Assert.Contains("the claim was taken", report.Flag!, StringComparison.Ordinal);

        // The session was stopped rather than left spending money on a ticket
        // this runner no longer holds.
        Assert.Equal(143, report.ExitCode);

        // A stall comment here would flag another runner's increment as ours,
        // and the ticket did not move because we stopped.
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/comments"));

        // The meter reading goes up either way: money was spent on that ticket,
        // and a bill is not a claim to have done the work. (This run was killed
        // before it could report, so there is nothing to post - and that too is
        // said out loud rather than posted as a row of zeros.)
        Assert.Contains(h.Say.Complained, l => l.Contains("no work log entry", StringComparison.Ordinal));

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_run_that_reported_its_bill_before_the_lease_went_still_posts_it()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.Conflict,
            "\"this claim has expired\"");
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        h.Sessions.Behaviour = async (_, onLine, ct) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nGot most of the way\n\nAnd then stopped.\n```"));

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException)
            {
                return new SessionResult(143, "");
            }

            return new SessionResult(0, "");
        };

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.True(report.LostLease);
        Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log"));
        Assert.Equal("Got most of the way",
            h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log")[0].Read<WorkLogEntryRequest>().Title);

        // The board read after a lost lease presents no token: this runner is no
        // longer the holder, and asking as one would be a lie about a lease.
        Assert.DoesNotContain("heldToken", h.Wire.To("GET", "/api/hatch/work/AER-1")[0].Query,
            StringComparison.OrdinalIgnoreCase);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task The_line_the_session_is_inside_of_reaches_the_board()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        List<string?> Carried() => h.Wire.To("POST", "/api/hatch/issues/AER-1/claim/heartbeat")
            .Select(c => c.Read<ClaimHeartbeatRequest>().Chatter)
            .Where(c => c is not null)
            .ToList();

        static bool Says(List<string?> lines) =>
            lines.Any(c => c!.Contains("make test-api", StringComparison.Ordinal));

        h.Sessions.Behaviour = async (_, onLine, ct) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.ToolUse("Bash", "make test-api"));

            // Inside the tool use until a heartbeat has carried it, rather than
            // for a span long enough that one usually has - Harness.Eventually's
            // rule, which this test is the one place that was not keeping. The
            // beat is 25ms and the old wait was eight of them, so a loaded
            // machine that missed the window failed a test about whether the
            // line reaches the board at all. Bounded and not asserted here: the
            // assertion after the run is what judges it, and a failure there
            // reads as the missing line rather than as a crashed session.
            var waited = Stopwatch.StartNew();
            try
            {
                while (!Says(Carried()) && waited.ElapsedMilliseconds < 5_000)
                {
                    await Task.Delay(Harness.Beat, ct);
                }
            }
            catch (OperationCanceledException)
            {
                return new SessionResult(143, "");
            }

            onLine?.Invoke(Fixtures.Result());
            return new SessionResult(0, "");
        };

        await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.Contains(Carried(), c => c!.Contains("make test-api", StringComparison.Ordinal));

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_run_that_never_said_what_it_spent_posts_no_row_of_zeros()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", from: "In Review"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            return Task.FromResult(new SessionResult(1, ""));
        };

        await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/work-log"));
        Assert.Contains(h.Say.Complained, l => l.Contains("no work log entry", StringComparison.Ordinal));

        await claim.ReleaseAsync();
    }
    // ---- Hearing a message sent while it works ----

    private static async Task<(IncrementReport Report, Claim Claim)> RunWithAsync(Harness h, WorkDto work)
    {
        var (claim, _) = await HoldingAsync(h, work.Issue.Key, Guid.NewGuid());
        h.Wire.Reply("POST", $"/api/hatch/issues/{work.Issue.Key}/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("POST", $"/api/hatch/issues/{work.Issue.Key}/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", $"/api/hatch/work/{work.Issue.Key}", Fixtures.Work(work.Issue.Key, from: "In Review"));
        h.Wire.Json("GET", $"/api/hatch/issues/{work.Issue.Key}/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(work, h.Root, "opus", "high", quiet: false, claim, default);
        return (report, claim);
    }

    [Fact]
    public async Task The_spawned_session_is_given_a_settings_file_outside_the_checkout_with_both_hooks()
    {
        using var h = new Harness();
        string? path = null, text = null;
        h.Sessions.Behaviour = (request, onLine, _) =>
        {
            path = request.HookSettings;
            text = path is null ? null : File.ReadAllText(path);
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        var (_, claim) = await RunWithAsync(h, Fixtures.Work("AER-1"));

        Assert.NotNull(path);
        Assert.False(path!.StartsWith(h.Root, StringComparison.Ordinal), "the settings file is inside the checkout");
        Assert.StartsWith(h.Temp, path, StringComparison.Ordinal);

        var hooks = System.Text.Json.JsonDocument.Parse(text!).RootElement.GetProperty("hooks");
        var step = hooks.GetProperty("PostToolUse")[0];
        Assert.Equal("*", step.GetProperty("matcher").GetString());
        var stepHook = step.GetProperty("hooks")[0];
        Assert.Equal("command", stepHook.GetProperty("type").GetString());
        Assert.Equal(10, stepHook.GetProperty("timeout").GetInt32());
        Assert.Contains("inbox \"AER-1\" --hook post-tool-use --stamp ", stepHook.GetProperty("command").GetString(), StringComparison.Ordinal);
        Assert.Contains(Path.Combine(Path.GetDirectoryName(path)!, "inbox.stamp"), stepHook.GetProperty("command").GetString(), StringComparison.Ordinal);

        var stop = hooks.GetProperty("Stop")[0].GetProperty("hooks")[0];
        Assert.Equal(10, stop.GetProperty("timeout").GetInt32());
        Assert.EndsWith("inbox \"AER-1\" --hook stop", stop.GetProperty("command").GetString(), StringComparison.Ordinal);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task The_hooks_directory_is_gone_after_the_run_and_the_checkout_was_never_written_to()
    {
        using var h = new Harness();
        string? directory = null;
        h.Sessions.Behaviour = (request, onLine, _) =>
        {
            directory = Path.GetDirectoryName(request.HookSettings);
            Assert.True(Directory.Exists(directory));
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };
        var before = Directory.GetFileSystemEntries(h.Root, "*", SearchOption.AllDirectories).Order().ToList();

        var (_, claim) = await RunWithAsync(h, Fixtures.Work("AER-1"));

        Assert.NotNull(directory);
        Assert.False(Directory.Exists(directory));
        Assert.Equal(before, Directory.GetFileSystemEntries(h.Root, "*", SearchOption.AllDirectories).Order().ToList());

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task The_hooks_directory_is_gone_when_the_session_is_stopped_too()
    {
        using var h = new Harness();
        string? directory = null;
        h.Sessions.Behaviour = (request, onLine, ct) =>
        {
            directory = Path.GetDirectoryName(request.HookSettings);
            return FakeSessions.UntilStopped()(request, onLine, ct);
        };
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.Conflict, "\"taken\"");
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/work-log", Fixtures.WorkLogRow());
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        await h.Runtime.Increment().RunAsync(Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: false, claim, default);

        Assert.NotNull(directory);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Unread_messages_are_marked_by_id_before_the_session_is_spawned()
    {
        using var h = new Harness();
        var spawnedAfter = -1;
        h.Wire.Json("POST", "/api/hatch/issues/AER-1/messages/deliver", new[] { Fixtures.Message(7), Fixtures.Message(9) });
        h.Sessions.Behaviour = (_, onLine, _) =>
        {
            spawnedAfter = h.Wire.Count("POST", "/api/hatch/issues/AER-1/messages/deliver");
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

        var (_, claim) = await RunWithAsync(
            h, Fixtures.Work("AER-1", messages: [Fixtures.Message(7), Fixtures.Message(9)]));

        Assert.Equal(1, spawnedAfter);
        var marked = Assert.Single(h.Wire.To("POST", "/api/hatch/issues/AER-1/messages/deliver")).Read<MessageDeliverRequest>();
        Assert.Equal([7L, 9L], marked.Ids);

        // And they were said in the prompt the session was handed.
        Assert.Contains("use the other table", h.Sessions.Spawned[0].Prompt, StringComparison.Ordinal);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_dispatch_with_no_messages_marks_nothing()
    {
        using var h = new Harness();

        var (_, claim) = await RunWithAsync(h, Fixtures.Work("AER-1", messages: []));

        Assert.Equal(0, h.Wire.Count("POST", "/api/hatch/issues/AER-1/messages/deliver"));
        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_failure_to_mark_them_does_not_stop_the_spawn()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/messages/deliver", HttpStatusCode.InternalServerError, "boom");

        var (report, claim) = await RunWithAsync(h, Fixtures.Work("AER-1", messages: [Fixtures.Message(7)]));

        Assert.Single(h.Sessions.Spawned);
        Assert.True(report.Moved);
        Assert.Contains(h.Say.Complained, l => l.Contains("could not mark", StringComparison.Ordinal));
        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_quiet_run_is_wired_the_same_way()
    {
        using var h = new Harness();
        h.Sessions.Behaviour = (request, _, _) =>
        {
            Assert.NotNull(request.HookSettings);
            return Task.FromResult(new SessionResult(0, ""));
        };
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        await h.Runtime.Increment().RunAsync(Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: true, claim, default);

        Assert.Single(h.Sessions.Spawned);
        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_quiet_run_that_crashed_without_a_final_object_still_reads_the_limit_off_its_raw_output()
    {
        using var h = new Harness();
        var (claim, _) = await HoldingAsync(h, "AER-1", Guid.NewGuid());

        // Killed before it ever printed its one closing object - quiet mode's
        // only account of the run is whatever made it to stdout regardless.
        h.Sessions.Behaviour = (_, _, _) =>
            Task.FromResult(new SessionResult(1, "You've hit your session limit · resets 7:40pm (America/New_York)"));

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        h.Wire.Json("GET", "/api/hatch/issues/AER-1/questions", Array.Empty<QuestionDto>());

        var report = await h.Runtime.Increment().RunAsync(
            Fixtures.Work("AER-1"), h.Root, "opus", "high", quiet: true, claim, default);

        Assert.True(report.UsageLimited);

        await claim.ReleaseAsync();
    }

    [Fact]
    public void The_hooks_run_this_binary_where_it_is_hatch_and_the_bare_word_otherwise()
    {
        Assert.Equal("/opt/hatch/hatch", SessionHooks.Binary("/opt/hatch/hatch"));
        Assert.Equal("hatch", SessionHooks.Binary("/usr/share/dotnet/dotnet"));
        Assert.Equal("hatch", SessionHooks.Binary(null));
    }
}

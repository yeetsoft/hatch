using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>Taking the lease, saying so while it runs, and letting go of it.</summary>
public sealed class ClaimTests
{
    private static readonly Guid Token = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task A_claim_names_the_runner_and_comes_back_with_a_token()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Token, ttlSeconds: 300));

        var (claim, refused) = await Claim.TakeAsync(h.Client, "AER-1", "test:/checkout", default, Harness.Beat);

        Assert.Null(refused);
        Assert.NotNull(claim);
        Assert.Equal(Token, claim.Token);
        Assert.Equal(300, claim.TtlSeconds);
        Assert.Equal("test:/checkout", h.Wire.To("POST", "/api/hatch/issues/AER-1/claim")[0].Read<ClaimRequest>().Runner);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task A_ticket_somebody_else_holds_is_an_answer_and_not_a_fault()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.Conflict,
            "\"hatch is working this from other:/tree, last heard from 8 seconds ago\"");

        var (claim, refused) = await Claim.TakeAsync(h.Client, "AER-1", "test:/checkout", default, Harness.Beat);

        Assert.Null(claim);
        Assert.NotNull(refused);
        Assert.True(refused.Held);
        Assert.Contains("other:/tree", refused.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_board_that_will_not_answer_is_not_a_ticket_somebody_holds()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.InternalServerError, "boom");

        var (claim, refused) = await Claim.TakeAsync(h.Client, "AER-1", "test:/checkout", default, Harness.Beat);

        Assert.Null(claim);
        Assert.NotNull(refused);
        Assert.False(refused.Held);
    }

    [Fact]
    public async Task A_2xx_body_that_is_not_a_claim_is_a_HatchException_and_not_a_crash()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, "not json at all");

        await Assert.ThrowsAsync<HatchException>(
            () => Claim.TakeAsync(h.Client, "AER-1", "test:/checkout", default, Harness.Beat));
    }

    [Fact]
    public async Task The_interval_is_a_fifth_of_the_lease_and_never_a_busy_loop()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), Claim.Interval(300));
        Assert.Equal(TimeSpan.FromSeconds(24), Claim.Interval(120));

        // A TTL short enough to make a fifth of it a busy loop is a
        // misconfiguration, and not one to amplify.
        Assert.Equal(TimeSpan.FromSeconds(5), Claim.Interval(4));
        Assert.Equal(TimeSpan.FromSeconds(60), Claim.Interval(0));

        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_release_presents_the_token_and_happens_once()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Token));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);

        var (claim, _) = await Claim.TakeAsync(h.Client, "AER-1", "test:/checkout", default, Harness.Beat);

        await claim!.ReleaseAsync();
        await claim.ReleaseAsync();
        await claim.DisposeAsync();

        var released = h.Wire.To("DELETE", "/api/hatch/issues/AER-1/claim");
        Assert.Single(released);
        Assert.Contains(Token.ToString(), released[0].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_heartbeat_carries_the_line_only_when_it_has_changed()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Token));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);

        var (claim, _) = await Claim.TakeAsync(h.Client, "AER-1", "test:/checkout", default, Harness.Beat);
        claim!.Chatter.Line = "⏺ Bash  make test-api";

        await Harness.Eventually(
            () => Beats(h).Count(b => b.Chatter is not null) >= 1, "the line to reach the board");
        await Harness.Eventually(
            () => Beats(h).Count >= 4, "several more heartbeats");

        await claim.ReleaseAsync();

        var carried = Beats(h).Where(b => b.Chatter is not null).ToList();

        // Once, and never again while it says the same thing - so the time the
        // card draws is when the line was printed rather than when a heartbeat
        // happened to fire.
        Assert.Single(carried);
        Assert.Equal("⏺ Bash  make test-api", carried[0].Chatter);
    }

    [Fact]
    public async Task A_new_line_is_carried_and_the_token_is_on_every_beat()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Token));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);

        var (claim, _) = await Claim.TakeAsync(h.Client, "AER-1", "test:/checkout", default, Harness.Beat);

        claim!.Chatter.Line = "first";
        await Harness.Eventually(() => Beats(h).Any(b => b.Chatter == "first"), "the first line");

        claim.Chatter.Line = "second";
        await Harness.Eventually(() => Beats(h).Any(b => b.Chatter == "second"), "the second line");

        await claim.ReleaseAsync();

        Assert.All(Beats(h), b => Assert.Equal(Token, b.Token));
    }

    [Fact]
    public async Task A_refused_heartbeat_is_a_lost_lease_and_says_so_once()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Token));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.Conflict,
            "\"this claim was taken over\"");
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);

        var (claim, _) = await Claim.TakeAsync(h.Client, "AER-1", "test:/checkout", default, Harness.Beat);

        var stopped = 0;
        claim!.OnLost = _ => Interlocked.Increment(ref stopped);

        await Harness.Eventually(() => claim.Lost is not null, "the lease to be reported lost");
        Assert.Equal("this claim was taken over", claim.Lost);

        // And then it stops asking. A lease that is over is over, and a runner
        // that kept knocking would be one more request a minute about a ticket
        // somebody else is working.
        var beats = h.Wire.Count("POST", "/api/hatch/issues/AER-1/claim/heartbeat");
        await Task.Delay(Harness.Beat * 6);
        Assert.Equal(beats, h.Wire.Count("POST", "/api/hatch/issues/AER-1/claim/heartbeat"));
        Assert.Equal(1, stopped);

        await claim.ReleaseAsync();
    }

    [Fact]
    public async Task Any_other_failure_is_weather_and_the_next_tick_tries_again()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Token));
        h.Wire.Once("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.InternalServerError, "boom");
        h.Wire.Once("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.RequestTimeout, "slow");
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);

        var (claim, _) = await Claim.TakeAsync(h.Client, "AER-1", "test:/checkout", default, Harness.Beat);

        await Harness.Eventually(
            () => h.Wire.Count("POST", "/api/hatch/issues/AER-1/claim/heartbeat") >= 4, "four heartbeats");

        // A timeout, a 500 and an origin that did not answer are all reasons to
        // ask again in a minute, and none of them is a reason to end an
        // increment that is doing fine.
        Assert.Null(claim!.Lost);

        await claim.ReleaseAsync();
    }

    // ---- HA-116: giving up on a longer outage ----

    /// <summary>
    /// A heartbeat that keeps failing for longer than <c>stallLapse</c>
    /// altogether gives up on its own, exactly as an explicit <c>409</c> does -
    /// checked before each attempt, so it does not wait for a beat already past
    /// the window to fail on its own account first.
    /// </summary>
    [Fact]
    public async Task A_heartbeat_that_keeps_failing_past_stallLapse_gives_up_and_stops_beating()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Token));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.InternalServerError, "boom");
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);

        var (claim, _) = await Claim.TakeAsync(
            h.Client, "AER-1", "test:/checkout", default, Harness.Beat, stallLapse: TimeSpan.FromMilliseconds(80));

        var stopped = 0;
        claim!.OnLost = _ => Interlocked.Increment(ref stopped);

        await Harness.Eventually(() => claim.Lost is not null, "the claim to give up on its own");
        Assert.Contains("no answer from Hatch", claim.Lost, StringComparison.Ordinal);

        // And then it stops asking, exactly as it does for a 409 - a lease this
        // runner has already stopped believing it holds is not one to keep
        // knocking about.
        var beats = h.Wire.Count("POST", "/api/hatch/issues/AER-1/claim/heartbeat");
        await Task.Delay(Harness.Beat * 6);
        Assert.Equal(beats, h.Wire.Count("POST", "/api/hatch/issues/AER-1/claim/heartbeat"));
        Assert.Equal(1, stopped);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// A beat that lands resets the give-up clock - an outage shorter than
    /// <c>stallLapse</c>, even with several failures inside it, never trips the
    /// give-up as long as at least one beat lands within any <c>stallLapse</c>
    /// -sized window.
    /// </summary>
    [Fact]
    public async Task A_heartbeat_that_succeeds_resets_the_give_up_clock()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Token));
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);

        // Two failures, then a success, on repeat - each success lands well
        // inside stallLapse, so the give-up clock keeps resetting and the
        // outage, however long it runs on, never actually elapses.
        for (var i = 0; i < 8; i++)
        {
            h.Wire.Once("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.InternalServerError, "boom");
            h.Wire.Once("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.InternalServerError, "boom");
            h.Wire.Once("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);
        }

        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.NoContent);

        var (claim, _) = await Claim.TakeAsync(
            h.Client, "AER-1", "test:/checkout", default, Harness.Beat, stallLapse: TimeSpan.FromMilliseconds(150));

        await Harness.Eventually(
            () => h.Wire.Count("POST", "/api/hatch/issues/AER-1/claim/heartbeat") >= 24,
            "several cycles of failures and successes");

        Assert.Null(claim!.Lost);

        await claim.ReleaseAsync();
    }

    /// <summary><c>0</c> - lapsing off, install-wide - never gives up this way, however long beats keep failing.</summary>
    [Fact]
    public async Task A_stallLapse_of_zero_never_gives_up_however_long_beats_keep_failing()
    {
        using var h = new Harness();
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.OK, Fixtures.Taken(Token));
        h.Wire.Reply("POST", "/api/hatch/issues/AER-1/claim/heartbeat", HttpStatusCode.InternalServerError, "boom");
        h.Wire.Reply("DELETE", "/api/hatch/issues/AER-1/claim", HttpStatusCode.NoContent);

        // Explicit zero, well past what a real stallLapse would have given up
        // at by now.
        var (claim, _) = await Claim.TakeAsync(
            h.Client, "AER-1", "test:/checkout", default, Harness.Beat, stallLapse: TimeSpan.Zero);

        await Harness.Eventually(
            () => h.Wire.Count("POST", "/api/hatch/issues/AER-1/claim/heartbeat") >= 10, "several failed heartbeats");

        Assert.Null(claim!.Lost);

        await claim.ReleaseAsync();
    }

    /// <summary>
    /// The release is best-effort, and it is not the caller's ninety seconds to
    /// spend: against an origin that never answers, it gives up within its own
    /// ~10s budget rather than the retry window <see cref="HatchClient.Send"/>
    /// would otherwise spend on a <c>DELETE</c>.
    /// </summary>
    [Fact]
    public async Task A_release_against_a_dead_origin_gives_up_within_its_own_budget()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var settings = new Settings { Base = "https://hatch.example", Key = "hatch_ak_test", HeartbeatSeconds = 0 };
        using var wire = new DeadAfterTake(Fixtures.Taken(Token));
        using var client = new HatchClient(settings, "test:/checkout", wire, clock);

        var (claim, _) = await Claim.TakeAsync(client, "AER-1", "test:/checkout", default, Harness.Beat);
        Assert.NotNull(claim);

        var releasing = claim!.ReleaseAsync();

        // Advances the fake clock past the release's own budget - real
        // milliseconds spent polling so the ten seconds this proves never
        // hangs on never have to be spent for real.
        for (var i = 0; i < 10 && !releasing.IsCompleted; i++)
        {
            await Task.Delay(5);
            clock.Advance(TimeSpan.FromSeconds(3));
        }

        await releasing.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(releasing.IsCompletedSuccessfully);
        Assert.True(wire.DeleteAttempts > 1, "the release should have retried at least once before giving up");
    }

    /// <summary>An origin that answers the claim once, and then is never there again.</summary>
    private sealed class DeadAfterTake(string takenBody) : HttpMessageHandler
    {
        public int DeleteAttempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/claim"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(takenBody, Encoding.UTF8, "application/json"),
                });

            DeleteAttempts++;
            throw new HttpRequestException("connection refused");
        }
    }

    private static List<ClaimHeartbeatRequest> Beats(Harness h) =>
        h.Wire.To("POST", "/api/hatch/issues/AER-1/claim/heartbeat")
            .Select(c => c.Read<ClaimHeartbeatRequest>())
            .ToList();
}

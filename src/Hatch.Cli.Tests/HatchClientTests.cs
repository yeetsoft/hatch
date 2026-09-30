using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>
/// The one branch every call takes before it is made: a key present is a
/// credential, a key absent is a name - and the 401 that comes back says which
/// of the two happened.
/// </summary>
public sealed class HatchClientTests
{
    private static Settings Settings(string key, int retrySeconds = 90) =>
        new() { Base = "https://hatch.example", Key = key, HeartbeatSeconds = 0, RetrySeconds = retrySeconds };

    /// <summary>
    /// Advances a fake clock far enough, and often enough, for whatever backoff
    /// a call under test is waiting on to resolve - real milliseconds spent
    /// polling so the call's own seconds never have to be.
    /// </summary>
    private static async Task DriveAsync(FakeTimeProvider clock, Task sending, int maxTicks = 30)
    {
        for (var i = 0; i < maxTicks && !sending.IsCompleted; i++)
        {
            await Task.Delay(5);
            clock.Advance(TimeSpan.FromSeconds(15));
        }
    }

    /// <summary>
    /// A key is a credential and outranks a name; the header is only a name, and
    /// only a Hatch with its wall off reads one. One or the other, never both.
    /// </summary>
    [Fact]
    public async Task A_key_is_sent_as_a_bearer_and_no_runner_header_goes_with_it()
    {
        using var wire = new HeaderWire();
        using var client = new HatchClient(Settings("hatch_ak_test"), "test:/checkout", wire);

        await client.Send(HttpMethod.Get, "/api/hatch/board", null, default);

        Assert.True(client.Keyed);
        Assert.Equal("Bearer hatch_ak_test", wire.Authorization);
        Assert.Null(wire.Runner);
    }

    [Fact]
    public async Task No_key_names_the_runner_instead_and_sends_no_authorization()
    {
        using var wire = new HeaderWire();
        using var client = new HatchClient(Settings(""), "test:/checkout", wire);

        await client.Send(HttpMethod.Get, "/api/hatch/board", null, default);

        Assert.False(client.Keyed);
        Assert.Null(wire.Authorization);
        Assert.Equal("test:/checkout", wire.Runner);
    }

    /// <summary>The same header the server reads, spelled once on each side.</summary>
    [Fact]
    public void The_runner_header_is_the_one_the_server_reads()
    {
        Assert.Equal("X-Hatch-Runner", HatchClient.RunnerHeader);
    }

    [Fact]
    public async Task A_key_of_only_spaces_is_no_key()
    {
        using var wire = new HeaderWire();
        using var client = new HatchClient(Settings("   "), "test:/checkout", wire);

        await client.Send(HttpMethod.Get, "/api/hatch/board", null, default);

        Assert.False(client.Keyed);
        Assert.Equal("test:/checkout", wire.Runner);
    }

    // ---- the two 401s ----

    /// <summary>
    /// A key that was refused is a key to go and look at: minted, not revoked,
    /// copied whole?
    /// </summary>
    [Fact]
    public async Task A_401_against_a_key_that_was_sent_says_the_key_was_not_accepted()
    {
        using var wire = new Wire();
        wire.Reply("GET", "/api/hatch/board", HttpStatusCode.Unauthorized);
        using var client = new HatchClient(Settings("hatch_ak_wrong"), "test:/checkout", wire);

        var thrown = await Assert.ThrowsAsync<HatchException>(
            () => client.GetAsync<BoardDto>("/api/hatch/board", default));

        Assert.Contains("401 - the key was not accepted", thrown.Message);
    }

    /// <summary>
    /// No credential was sent, so nothing was rejected: this Hatch has its wall
    /// up and wants one. Said plainly, because "the key was not accepted" would
    /// send somebody looking at a key they never set.
    /// </summary>
    [Fact]
    public async Task A_401_against_no_key_at_all_says_the_wall_is_on_and_names_config()
    {
        using var wire = new Wire();
        wire.Reply("GET", "/api/hatch/board", HttpStatusCode.Unauthorized);
        using var client = new HatchClient(Settings(""), "test:/checkout", wire);

        var thrown = await Assert.ThrowsAsync<HatchException>(
            () => client.GetAsync<BoardDto>("/api/hatch/board", default));

        Assert.Contains("no key was sent and this Hatch has its wall on", thrown.Message);
        Assert.Contains("hatch config", thrown.Message);
        Assert.DoesNotContain("./scripts/hatch.sh", thrown.Message);
        Assert.DoesNotContain("the key was not accepted", thrown.Message);
    }

    /// <summary>
    /// A 403 is the other way round: the key worked, and the route is not one it
    /// may take.
    /// </summary>
    [Fact]
    public async Task A_403_is_the_key_working_and_the_route_being_refused()
    {
        using var wire = new Wire();
        wire.Reply("GET", "/api/hatch/playbooks", HttpStatusCode.Forbidden);
        using var client = new HatchClient(Settings("hatch_ak_test"), "test:/checkout", wire);

        var thrown = await Assert.ThrowsAsync<HatchException>(
            () => client.GetAsync<BoardDto>("/api/hatch/playbooks", default));

        Assert.Contains("403 - the key is good and this route is not one it may take", thrown.Message);
    }

    // ---- HA-116: a minute of Hatch not answering costs nothing ----

    /// <summary>A read may always ask again - nothing is lost by asking twice.</summary>
    [Fact]
    public async Task A_GET_retries_through_two_refused_connections_then_a_503_and_answers()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var wire = new FlakyWire(
            [FlakyWire.Failure.Refused, FlakyWire.Failure.Refused, FlakyWire.Failure.Bad503]);
        using var client = new HatchClient(Settings("hatch_ak_test"), "test:/checkout", wire, clock);

        var sending = client.Send(HttpMethod.Get, "/api/hatch/board", null, default);
        await DriveAsync(clock, sending);
        var answer = await sending;

        Assert.True(answer.Ok);
        Assert.Equal(4, wire.Attempts);
    }

    /// <summary>
    /// A refused connection never reached anything, so a POST may ask again too -
    /// the idempotency rule is about the answer, not the verb's own safety.
    /// </summary>
    [Fact]
    public async Task A_POST_retries_through_a_refused_connection_and_answers()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var wire = new FlakyWire([FlakyWire.Failure.Refused]);
        using var client = new HatchClient(Settings("hatch_ak_test"), "test:/checkout", wire, clock);

        var sending = client.Send(
            HttpMethod.Post, "/api/hatch/issues/AER-1/claim", new ClaimRequest("test:/checkout"), default);
        await DriveAsync(clock, sending);
        var answer = await sending;

        Assert.True(answer.Ok);
        Assert.Equal(2, wire.Attempts);
    }

    /// <summary>
    /// A 504 is a proxy saying the app itself took the request and then went
    /// quiet - it might already have done what was asked, so a POST does not
    /// risk asking again.
    /// </summary>
    [Fact]
    public async Task A_POST_is_not_retried_after_a_504()
    {
        using var wire = new FlakyWire([FlakyWire.Failure.Bad504]);
        using var client = new HatchClient(Settings("hatch_ak_test"), "test:/checkout", wire);

        var answer = await client.Send(
            HttpMethod.Post, "/api/hatch/issues/AER-1/claim", new ClaimRequest("test:/checkout"), default);

        Assert.Equal(HttpStatusCode.GatewayTimeout, answer.Status);
        Assert.Equal(1, wire.Attempts);
    }

    /// <summary>
    /// A timeout is ambiguous - the request went out and nobody knows what
    /// happened to it - so it is treated as "may have arrived" and a POST ends
    /// the call rather than risk a second write.
    /// </summary>
    [Fact]
    public async Task A_POST_is_not_retried_after_a_timeout()
    {
        using var wire = new FlakyWire([FlakyWire.Failure.TimedOut]);
        using var client = new HatchClient(Settings("hatch_ak_test"), "test:/checkout", wire);

        var answer = await client.Send(
            HttpMethod.Post, "/api/hatch/issues/AER-1/claim", new ClaimRequest("test:/checkout"), default);

        Assert.Null(answer.Status);
        Assert.Equal(1, wire.Attempts);
    }

    /// <summary>A claim reads a 409 as an answer, not a fault - and never retries it.</summary>
    [Fact]
    public async Task A_409_is_a_real_answer_and_is_never_retried()
    {
        using var wire = new Wire();
        wire.Reply("POST", "/api/hatch/issues/AER-1/claim", HttpStatusCode.Conflict, "\"busy\"");
        using var client = new HatchClient(Settings("hatch_ak_test"), "test:/checkout", wire);

        var answer = await client.Send(
            HttpMethod.Post, "/api/hatch/issues/AER-1/claim", new ClaimRequest("test:/checkout"), default);

        Assert.Equal(HttpStatusCode.Conflict, answer.Status);
        Assert.Single(wire.To("POST", "/api/hatch/issues/AER-1/claim"));
    }

    /// <summary>A 500 is a real answer too, whatever the verb.</summary>
    [Fact]
    public async Task A_500_is_never_retried()
    {
        using var wire = new Wire();
        wire.Reply("GET", "/api/hatch/board", HttpStatusCode.InternalServerError, "boom");
        using var client = new HatchClient(Settings("hatch_ak_test"), "test:/checkout", wire);

        var answer = await client.Send(HttpMethod.Get, "/api/hatch/board", null, default);

        Assert.Equal(HttpStatusCode.InternalServerError, answer.Status);
        Assert.Single(wire.To("GET", "/api/hatch/board"));
    }

    /// <summary>
    /// The window is spent in a fixed schedule - 1s, 2s, 4s, 8s, then every 10s -
    /// so a call that never gets an answer gives up once that schedule has spent
    /// the whole window, and says how long it waited.
    /// </summary>
    [Fact]
    public async Task When_the_window_runs_out_the_answer_and_the_refusal_say_how_long_it_waited()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var wire = new FlakyWire(Enumerable.Repeat(FlakyWire.Failure.Refused, 20));
        using var client = new HatchClient(Settings("hatch_ak_test", retrySeconds: 5), "test:/checkout", wire, clock);

        var sending = client.Send(HttpMethod.Get, "/api/hatch/board", null, default);
        await DriveAsync(clock, sending);
        var answer = await sending;

        Assert.False(answer.Ok);
        Assert.Equal(5, answer.WaitedSeconds);

        var reading = client.GetAsync<BoardDto>("/api/hatch/board", default);
        var thrown = await Assert.ThrowsAsync<HatchException>(async () =>
        {
            await DriveAsync(clock, reading);
            await reading;
        });

        Assert.Contains("could not reach https://hatch.example for 5s", thrown.Message);
    }

    /// <summary><c>0</c> is a valid window, and means exactly one attempt - not a wait of no time at all.</summary>
    [Fact]
    public async Task Retry_seconds_zero_means_exactly_one_attempt()
    {
        using var wire = new FlakyWire(Enumerable.Repeat(FlakyWire.Failure.Refused, 5));
        using var client = new HatchClient(Settings("hatch_ak_test", retrySeconds: 0), "test:/checkout", wire);

        var answer = await client.Send(HttpMethod.Get, "/api/hatch/board", null, default);

        Assert.False(answer.Ok);
        Assert.Equal(1, wire.Attempts);
    }

    /// <summary>
    /// Said once, at the first retry, and not again however many follow - a
    /// heartbeat is not weather report, and neither is a call somebody is
    /// waiting on.
    /// </summary>
    [Fact]
    public async Task The_waiting_callback_fires_once_per_call_not_once_per_attempt()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var wire = new FlakyWire(
            [FlakyWire.Failure.Refused, FlakyWire.Failure.Refused, FlakyWire.Failure.Refused]);
        var said = new List<string>();
        using var client = new HatchClient(Settings("hatch_ak_test"), "test:/checkout", wire, clock, said.Add);

        var sending = client.Send(HttpMethod.Get, "/api/hatch/board", null, default);
        await DriveAsync(clock, sending);
        var answer = await sending;

        Assert.True(answer.Ok);
        Assert.Single(said);
        Assert.Contains("hatch.example", said[0], StringComparison.Ordinal);
        Assert.Contains("90s", said[0], StringComparison.Ordinal);
    }

    // ---- The catch-all, and a structured body's own sentence ----

    /// <summary>
    /// The WIP <c>409</c>'s <c>{ error, load, limit }</c> prints as the
    /// sentence alone, not the JSON it arrived as.
    /// </summary>
    [Fact]
    public async Task A_409_carrying_error_load_and_limit_prints_the_sentence_and_not_the_json()
    {
        using var wire = new Wire();
        wire.Reply(
            "POST", "/api/hatch/issues/AER-1/move", HttpStatusCode.Conflict,
            """{"error":"the WIP section is full - 2 of 2 stories and bugs are in it","load":2,"limit":2}""");
        using var client = new HatchClient(Settings("hatch_ak_test"), "test:/checkout", wire);

        var thrown = await Assert.ThrowsAsync<HatchException>(
            () => client.PostAsync<IssueDto>(
                "/api/hatch/issues/AER-1/move", new IssueMoveRequest(2, null, null), default));

        Assert.Equal("hatch: 409 - the WIP section is full - 2 of 2 stories and bugs are in it", thrown.Message);
    }

    /// <summary>A bare-string 409, as every other refusal in the house still is, prints exactly as before.</summary>
    [Fact]
    public async Task A_bare_string_409_prints_as_before()
    {
        using var wire = new Wire();
        wire.Reply("POST", "/api/hatch/issues/AER-1/move", HttpStatusCode.Conflict, "\"AER-1 is in Done now - nothing moved\"");
        using var client = new HatchClient(Settings("hatch_ak_test"), "test:/checkout", wire);

        var thrown = await Assert.ThrowsAsync<HatchException>(
            () => client.PostAsync<IssueDto>(
                "/api/hatch/issues/AER-1/move", new IssueMoveRequest(2, null, null), default));

        Assert.Equal("hatch: 409 - \"AER-1 is in Done now - nothing moved\"", thrown.Message);
    }

    /// <summary>
    /// A write's refusal is already handled above the deserialise - what is left
    /// unguarded is a 2xx whose body is not what the caller asked for, the way
    /// <see cref="GetAsync{T}"/> already guards its own read.
    /// </summary>
    [Fact]
    public async Task A_2xx_body_that_is_not_the_write_s_answer_is_a_HatchException_and_not_a_crash()
    {
        using var wire = new Wire();
        wire.Reply("POST", "/api/hatch/issues/AER-1/move", HttpStatusCode.OK, "not json at all");
        using var client = new HatchClient(Settings("hatch_ak_test"), "test:/checkout", wire);

        var thrown = await Assert.ThrowsAsync<HatchException>(
            () => client.PostAsync<IssueDto>(
                "/api/hatch/issues/AER-1/move", new IssueMoveRequest(2, null, null), default));

        Assert.Contains("IssueDto", thrown.Message);
    }

    /// <summary>
    /// A connection that never happened is a different thing from a refusal, and
    /// every caller treats it as one - the difference matters most to the
    /// heartbeat, where one is a lost lease and the other is a minute of bad
    /// network.
    /// </summary>
    [Fact]
    public async Task An_origin_that_did_not_answer_is_a_null_status_and_not_a_code()
    {
        // RetrySeconds: 0 - this is about what a connection failure maps to,
        // not about the retry loop, which HA-116's own tests above cover.
        using var client = new HatchClient(
            Settings("hatch_ak_test", retrySeconds: 0), "test:/checkout", new DeadWire());

        var answer = await client.Send(HttpMethod.Get, "/api/hatch/board", null, default);

        Assert.Null(answer.Status);
        Assert.False(answer.Ok);
    }

    /// <summary>
    /// The trimmed binary has reflection-based serialization switched off, so
    /// every wire record has to be in the generated table. One that is not says
    /// so by name rather than coming back empty.
    /// </summary>
    [Fact]
    public async Task A_record_nobody_registered_refuses_by_naming_itself()
    {
        using var wire = new Wire();
        wire.Reply("GET", "/api/hatch/board", HttpStatusCode.OK, "{}");
        using var client = new HatchClient(Settings("hatch_ak_test"), "test:/checkout", wire);

        var thrown = await Assert.ThrowsAsync<HatchException>(
            () => client.GetAsync<UnregisteredDto>("/api/hatch/board", default));

        Assert.Contains("UnregisteredDto is not registered in HatchJson", thrown.Message);
    }

    /// <summary>
    /// <c>WorkRepositoryDto</c> has no <c>[JsonSerializable]</c> of its own -
    /// it rides inside the already-registered <c>WorkDto</c>, and the
    /// generator produces metadata for everything reachable from a registered
    /// root. Proved by demonstration rather than by adding a registration
    /// nothing would ever call.
    /// </summary>
    [Fact]
    public async Task A_dispatches_repositories_ride_through_the_trimmed_client_with_no_registration_of_their_own()
    {
        using var wire = new Wire();
        wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work(
            "AER-1",
            repositories: [Fixtures.Repository("https://example.com/o/r", primary: true, matchedRemote: "https://example.com/o/r")]));
        using var client = new HatchClient(Settings("hatch_ak_test"), "test:/checkout", wire);

        var work = await client.GetAsync<WorkDto>("/api/hatch/work/AER-1", default);

        var repo = Assert.Single(work!.Repositories);
        Assert.Equal("https://example.com/o/r", repo.Remote);
        Assert.True(repo.Primary);
        Assert.Equal("https://example.com/o/r", repo.MatchedRemote);
    }

    private sealed record UnregisteredDto(string Nothing);

    /// <summary>A wire that keeps the headers it was called with, and answers nothing much.</summary>
    private sealed class HeaderWire : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        public string? Runner { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Authorization = request.Headers.Authorization?.ToString();
            Runner = request.Headers.TryGetValues(HatchClient.RunnerHeader, out var values)
                ? string.Join(",", values)
                : null;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    /// <summary>An origin that is not there.</summary>
    private sealed class DeadWire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("connection refused");
    }

    /// <summary>
    /// An origin that fails a scripted number of times, in a scripted way,
    /// before it starts answering normally - what proves the retry loop asks
    /// again, and that it stops asking once it should.
    /// </summary>
    private sealed class FlakyWire : HttpMessageHandler
    {
        public enum Failure { Refused, Bad502, Bad503, Bad504, TimedOut }

        private readonly Queue<Failure> _failures;
        private readonly HttpStatusCode _thenCode;
        private readonly string _thenBody;

        public FlakyWire(IEnumerable<Failure> failures, HttpStatusCode thenCode = HttpStatusCode.OK, string thenBody = "{}")
        {
            _failures = new Queue<Failure>(failures);
            _thenCode = thenCode;
            _thenBody = thenBody;
        }

        /// <summary>Every request made, failures and the final answer both.</summary>
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Attempts++;

            if (_failures.Count == 0)
                return Task.FromResult(new HttpResponseMessage(_thenCode)
                {
                    Content = new StringContent(_thenBody, Encoding.UTF8, "application/json"),
                });

            return _failures.Dequeue() switch
            {
                Failure.Refused => throw new HttpRequestException(
                    HttpRequestError.ConnectionError, "connection refused"),
                Failure.TimedOut => throw new TaskCanceledException(
                    "the request was canceled due to the configured HttpClient.Timeout"),
                Failure.Bad502 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)),
                Failure.Bad503 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
                Failure.Bad504 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.GatewayTimeout)),
                var other => throw new NotSupportedException(other.ToString()),
            };
        }
    }
}

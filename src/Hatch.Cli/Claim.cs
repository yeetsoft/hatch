namespace Hatch.Cli;

/// <summary>
/// The last line the runner printed, shared between whoever is printing and the
/// heartbeat that carries it to the board.
/// </summary>
/// <remarks>
/// A field and not a file. The shell needed one because its renderer ran inside
/// a pipeline stage, which is a subshell, and nothing set in there survives -
/// so the line had to leave the process to come back into it. One process needs
/// no such trip, and losing the file also loses the truncated-mid-write read it
/// forced the heartbeat to defend against.
/// </remarks>
public sealed class Chatter
{
    private string? _line;

    /// <summary>What the run is inside of right now, or null if it has not said.</summary>
    public string? Line
    {
        get => Volatile.Read(ref _line);
        set => Volatile.Write(ref _line, value);
    }
}

/// <summary>Why a claim was not taken, and whether the reason was that somebody else holds it.</summary>
/// <param name="Held">
/// True for the <c>409</c>: an answer rather than a fault, and the one a pass
/// walks on from. Anything else is a board that would not answer.
/// </param>
public sealed record NotClaimed(string Sentence, bool Held);

/// <summary>
/// A lease on one issue: taken before an increment, refreshed while it runs,
/// let go of on the way out.
/// </summary>
/// <remarks>
/// <para>Everything about the lease that is a policy - how long it lasts, when
/// it is over, who is refused - lives on the server. What lives here is the
/// three calls and the clock between them.</para>
///
/// <para>Releasing is idempotent by construction on both sides: this clears its
/// own state before it calls, so a second release is a no-op even if the first
/// died mid-flight, and the endpoint accepts a release of nothing. Being called
/// twice is not a case to handle - which matters, because on an interrupt it
/// will be.</para>
/// </remarks>
public sealed class Claim : IAsyncDisposable
{
    /// <summary>The release's own budget - see <see cref="ReleaseAsync"/>.</summary>
    private static readonly TimeSpan ReleaseBudget = TimeSpan.FromSeconds(10);

    private readonly HatchClient _client;
    private readonly HatchClient _quiet;
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeSpan _interval;
    private readonly TimeSpan? _stallLapse;
    private Task? _heartbeat;
    private int _released;

    private Claim(HatchClient client, string key, Guid token, int ttlSeconds, TimeSpan interval, TimeSpan? stallLapse)
    {
        _client = client;
        _quiet = client.Quiet();
        _interval = interval;
        _stallLapse = stallLapse;
        Key = key;
        Token = token;
        TtlSeconds = ttlSeconds;
    }

    /// <summary>The issue this runner holds.</summary>
    public string Key { get; }

    /// <summary>The capability. Presented by every heartbeat and by the release, and printed nowhere.</summary>
    public Guid Token { get; }

    /// <summary>What the server said the lease is worth, in seconds.</summary>
    public int TtlSeconds { get; }

    /// <summary>The line the heartbeat carries. Written by whoever is rendering the session.</summary>
    public Chatter Chatter { get; } = new();

    /// <summary>
    /// The server's sentence, once a heartbeat has been refused - the lease
    /// expired underneath us, was taken over, or was cleared by the operator.
    /// Null while the lease is good.
    /// </summary>
    public string? Lost { get; private set; }

    /// <summary>
    /// What to do the moment the lease goes: stop the session, so that nothing
    /// is spent on a ticket this runner no longer holds. Set by the increment,
    /// because the increment is what owns the process.
    /// </summary>
    public Action<string>? OnLost { get; set; }

    /// <summary>
    /// How often to say "still here". A fifth of the lease, so four heartbeats
    /// may go missing before the board gives the ticket to somebody else, and
    /// never less often than every five seconds - a TTL short enough to make
    /// that a busy loop is a misconfiguration this should not amplify.
    /// </summary>
    public static TimeSpan Interval(int ttlSeconds) =>
        TimeSpan.FromSeconds(Math.Max(5, ttlSeconds > 0 ? ttlSeconds / 5 : 60));

    /// <summary>
    /// Take the lease, and start saying so. Answers the claim, or the reason it
    /// was refused.
    /// </summary>
    /// <param name="stallLapse">
    /// Overrides what the take answered in <c>StallLapseSeconds</c> - null
    /// takes that, so a test can hand the heartbeat a window measured in
    /// milliseconds rather than waiting out real minutes. See <see
    /// cref="BeatAsync"/>.
    /// </param>
    public static async Task<(Claim? Held, NotClaimed? Refused)> TakeAsync(
        HatchClient client, string key, string runner, CancellationToken ct, TimeSpan? interval = null,
        TimeSpan? stallLapse = null)
    {
        var answer = await client.Send(
            HttpMethod.Post, $"/api/hatch/issues/{key}/claim", new ClaimRequest(runner), ct);

        if (answer.Conflict) return (null, new NotClaimed(answer.Sentence, Held: true));
        if (!answer.Ok) return (null, new NotClaimed($"the claim was refused - {answer.Sentence}", Held: false));

        ClaimTakenDto? taken;
        try
        {
            taken = System.Text.Json.JsonSerializer.Deserialize(answer.Body, HatchJson.Default.ClaimTakenDto);
        }
        catch (System.Text.Json.JsonException e)
        {
            throw new HatchException($"hatch: the claim answered with something that is not a claim: {e.Message}");
        }

        if (taken is null || taken.Token == Guid.Empty)
            return (null, new NotClaimed("the claim came back without a token", Held: false));

        var claim = new Claim(
            client, key, taken.Token, taken.TtlSeconds, interval ?? Interval(taken.TtlSeconds),
            stallLapse ?? (taken.StallLapseSeconds > 0 ? TimeSpan.FromSeconds(taken.StallLapseSeconds) : null));
        claim._heartbeat = Task.Run(() => claim.BeatAsync(claim._stop.Token), CancellationToken.None);
        return (claim, null);
    }

    /// <summary>
    /// Still here, every interval, carrying the last line printed - and only
    /// when it has changed, so that the time on it is when the line was printed
    /// rather than when a heartbeat happened to fire.
    /// </summary>
    /// <remarks>
    /// <para>Nothing in here may take a night's run down. A refused heartbeat is
    /// a lost lease and is reported as one; every other failure is a blip on its
    /// own - each beat already spent up to <see cref="Settings.RetrySeconds"/>
    /// retrying inside <see cref="HatchClient.Send"/> before it came back here
    /// at all - and the next tick tries again.</para>
    ///
    /// <para><b>Unless the blips add up to more than <see cref="_stallLapse"/>
    /// altogether.</b> This is a client-side mitigation and not a guarantee -
    /// the server's own quiet-claim rule is what actually lets somebody else
    /// take the ticket - but a runner that cannot reach Hatch has no way to know
    /// whether it still holds the lease, and going quiet on its own, once it has
    /// been that long, is what keeps it from spending real work on a ticket the
    /// board may already have handed to somebody else. Checked before each
    /// attempt rather than only after one fails, so a beat already past the
    /// window gives up without spending another retry's worth of time finding
    /// out the call still fails - which keeps the give-up within about one
    /// heartbeat interval of the window, not the window plus a retry's slack.
    /// A <see cref="_stallLapse"/> of zero or null - lapsing off, install-wide -
    /// never gives up this way: every failure reads as weather, as it always
    /// has.</para>
    /// </remarks>
    private async Task BeatAsync(CancellationToken ct)
    {
        string? sent = null;
        var lastGood = DateTimeOffset.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_stallLapse is { } lapse && lapse > TimeSpan.Zero && DateTimeOffset.UtcNow - lastGood >= lapse)
            {
                Lost = $"no answer from Hatch for over {(int)lapse.TotalSeconds}s";
                OnLost?.Invoke(Lost);
                return;
            }

            // An empty line is a session that has not said anything yet, not a
            // session that has stopped saying things: the field is cosmetic, and
            // a blank flicker on a card is worse than a line a minute stale.
            var line = Chatter.Line;
            if (string.IsNullOrEmpty(line)) line = sent;

            var body = line == sent
                ? new ClaimHeartbeatRequest(Token, null)
                : new ClaimHeartbeatRequest(Token, line);

            // Quiet: a heartbeat says nothing about weather today, whatever
            // HatchClient.Send spent retrying underneath it - see docs/hatch.md,
            // "What it stops for".
            var answer = await _quiet.Send(
                HttpMethod.Post, $"/api/hatch/issues/{Key}/claim/heartbeat", body, ct);

            if (answer.Ok)
            {
                sent = line;
                lastGood = DateTimeOffset.UtcNow;
                continue;
            }

            if (answer.Conflict)
            {
                Lost = answer.Sentence;
                OnLost?.Invoke(answer.Sentence);
                return;
            }

            // Anything else is weather - lastGood is left alone, and the check
            // at the top of the next iteration is what decides whether this
            // stretch of it has gone on long enough to give up.
        }
    }

    /// <summary>
    /// Let go. Stops the heartbeat first, so nothing refreshes a lease that is
    /// on its way out, then presents the token - which is what lets a runner
    /// release its own lease and nobody else's.
    /// </summary>
    public async Task ReleaseAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) == 1) return;

        await _stop.CancelAsync();
        if (_heartbeat is { } beat)
        {
            try
            {
                await beat;
            }
            catch (Exception)
            {
                // Whatever the heartbeat died of, it is not a reason to fail the
                // release - and the release is on the path out of an increment
                // that has already happened. A cancelled delay is the ordinary
                // case and the rest is weather; both end the same way, with the
                // lease given back below.
            }
        }

        // Its own budget, and not the caller's token: this already runs on the
        // way out of an interrupt, so CancellationToken.None would mean a
        // Ctrl-C against a dead origin sits through the full retry window
        // before the process is free to exit. Ten seconds, not ninety - a
        // best-effort release is worth a short wait and not a long one. Timed
        // against the client's own clock, so a test can drive the same fake
        // one the retries inside Send are waiting on rather than the ten
        // real seconds this would otherwise cost.
        using var budget = new CancellationTokenSource(ReleaseBudget, _client.Clock);

        try
        {
            // Not through the throwing lane: a release that was refused is a
            // lease that has already gone, which is the state a DELETE was
            // asking for.
            await _client.Send(
                HttpMethod.Delete, $"/api/hatch/issues/{Key}/claim?token={Token}", null, budget.Token);
        }
        catch (OperationCanceledException)
        {
            // The budget ran out against an origin that never answered - still
            // best-effort, and still not worth failing the release over.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync();
        _stop.Dispose();
    }
}

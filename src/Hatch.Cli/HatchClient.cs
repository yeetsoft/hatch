using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Hatch.Cli;

/// <summary>What came back: the code, and the body, and no opinion about either.</summary>
/// <param name="Status">
/// The code, or null where the request never reached a server at all - which is
/// a different thing from a refusal and is treated as one everywhere below.
/// </param>
/// <param name="WaitedSeconds">
/// How long <see cref="HatchClient.Send"/> spent retrying before it gave this
/// answer up as final, or null where it never retried at all - either because
/// the answer was not one worth asking again, or because asking again would
/// risk a second write. Only <see cref="HatchClient.Refusal"/>'s null-status
/// sentence reads this; nothing else about an <see cref="Answer"/> changes
/// because of it.
/// </param>
public readonly record struct Answer(HttpStatusCode? Status, string Body, int? WaitedSeconds = null)
{
    public bool Ok => Status is { } code && (int)code is >= 200 and < 300;

    /// <summary>Whether this is the one refusal a claim treats as an answer rather than a fault.</summary>
    public bool Conflict => Status == HttpStatusCode.Conflict;

    /// <summary>The sentence to print, which for Hatch is usually the body: its errors are worth reading.</summary>
    public string Sentence => Body.Trim().Length > 0
        ? Body.Trim().Trim('"')
        : Status is { } code ? $"{(int)code}" : "the origin did not answer";
}

/// <summary>
/// A call that failed in a way the caller had no answer for.
/// </summary>
/// <param name="Status">
/// The code the server answered with, or null when the request never reached
/// one at all. What lets a catcher tell a <c>400</c> - the dispatcher refusing
/// what was asked, which will never succeed by retrying - apart from a
/// <c>500</c> or a dropped connection, which is ordinary weather.
/// </param>
public sealed class HatchException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// A body that is already JSON, sent as it was typed.
/// </summary>
/// <remarks>
/// Only <c>hatch api</c> has one. Every other call builds a record and lets the
/// serialiser write it; the passthrough takes whatever was on the command line,
/// and re-encoding that as a JSON string would send the text of the object
/// rather than the object.
/// </remarks>
public readonly record struct RawJson(string Value);

/// <summary>
/// The one place that talks to Hatch, and the one place that decides what a
/// failure means.
/// </summary>
/// <remarks>
/// Two ways to ask, deliberately. <see cref="GetAsync{T}"/> and friends throw,
/// because for every call the runner makes about a ticket a refusal is a fault
/// and the sentence Hatch wrote is the thing worth printing. <see cref="Send"/>
/// hands back the code instead, because the claim's three calls each have a
/// refusal that is an answer - a ticket somebody else is working is not an
/// error, and a loop that ended because a ticket was busy would be the opposite
/// of what the claim is for.
/// </remarks>
public sealed class HatchClient : IDisposable
{
    /// <summary>
    /// Hatch speaks camelCase, and reads it tolerantly.
    /// </summary>
    /// <remarks>
    /// The resolver is source-generated rather than reflective, because
    /// <c>make publish-hatch</c> trims the binary and a trimmed application has
    /// reflection-based serialization switched off outright. See
    /// <see cref="HatchJson"/>.
    /// </remarks>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = HatchJson.Default,
    };

    /// <summary>
    /// The reader or writer for a type, from the generated table.
    /// </summary>
    /// <remarks>
    /// Every serialising call goes through here rather than through
    /// <c>JsonSerializer.Deserialize&lt;T&gt;(string, JsonSerializerOptions)</c>,
    /// which the trimmer cannot see through and warns about at every call site.
    /// A type nobody registered throws here, naming itself.
    /// </remarks>
    private static JsonTypeInfo TypeInfo(Type type)
    {
        try
        {
            return Json.GetTypeInfo(type);
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        {
            throw new HatchException(
                $"hatch: {type.Name} is not registered in HatchJson - add a [JsonSerializable] for it.");
        }
    }

    /// <summary>
    /// The header a keyless call names itself with. The same one
    /// <c>LocalCaller</c> reads on the server, spelled once on each side.
    /// </summary>
    public const string RunnerHeader = "X-Hatch-Runner";

    private readonly HttpClient _http;
    private readonly bool _owned;
    private readonly bool _shared;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _window;
    private readonly Action<string>? _onWaiting;
    private HatchClient? _quiet;

    /// <param name="runnerName">
    /// What a keyless call calls itself. Only read when there is no key, and
    /// only read at all by a Hatch with its wall off - see
    /// docs/auth-architecture.md, "Local mode".
    /// </param>
    /// <param name="clock">
    /// What <see cref="Send"/> measures its retry window against, and waits
    /// on between attempts - <see cref="TimeProvider.System"/> for every real
    /// run, and a fake one for a test that would rather not spend ninety
    /// seconds proving a timeout works.
    /// </param>
    /// <param name="onWaiting">
    /// Told once per call, the moment the first retry begins - the origin and
    /// how long <see cref="Send"/> will keep trying it. Null for a call that
    /// has nothing to say about weather: the claim's own heartbeat and the
    /// idle poll build a client with none, through <see cref="Quiet"/>.
    /// </param>
    public HatchClient(
        Settings settings, string? runnerName = null, HttpMessageHandler? handler = null,
        TimeProvider? clock = null, Action<string>? onWaiting = null)
    {
        _owned = handler is null;
        _http = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: _owned)
        {
            BaseAddress = new Uri(settings.Base + "/"),
            Timeout = TimeSpan.FromMinutes(2),
        };

        // One or the other, never both. A key is a credential and outranks a
        // name; the header is only a name, and only a Hatch with its wall off
        // reads one.
        Keyed = !string.IsNullOrWhiteSpace(settings.Key);
        if (Keyed)
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.Key);
        else
            _http.DefaultRequestHeaders.Add(RunnerHeader, runnerName ?? "hatch");

        Origin = settings.Base;
        _clock = clock ?? TimeProvider.System;
        _window = TimeSpan.FromSeconds(settings.RetrySeconds);
        _onWaiting = onWaiting;
    }

    /// <summary>
    /// The same client, sharing the one <see cref="HttpClient"/> - its auth,
    /// its base address, its connection pool - but with <see cref="_onWaiting"/>
    /// switched off. Not a second connection: a call still retries exactly as
    /// noisily as one made through the original, it just never says so. What a
    /// claim's heartbeat and the idle poll are built with, because they say
    /// nothing about weather today - see docs/hatch.md, "What it stops for".
    /// </summary>
    private HatchClient(HatchClient source)
    {
        _owned = false;
        _shared = true;
        _http = source._http;
        Keyed = source.Keyed;
        Origin = source.Origin;
        _clock = source._clock;
        _window = source._window;
        _onWaiting = null;
    }

    /// <summary>The origin, for the sentences that name it.</summary>
    public string Origin { get; }

    /// <summary>
    /// What <see cref="Send"/> measures its retry window and waits against - the
    /// same clock a caller's own bounded wait should use, so a test can drive
    /// both with one fake rather than two that might drift apart. See <see
    /// cref="Claim.ReleaseAsync"/>'s own budget.
    /// </summary>
    public TimeProvider Clock => _clock;

    /// <summary>
    /// Whether a credential was sent. What makes a 401 two different sentences:
    /// a key that was refused is a key to go and look at, and no key at all is a
    /// Hatch with its wall on and nothing to look at yet.
    /// </summary>
    public bool Keyed { get; }

    /// <summary>
    /// This client, quiet - cached, so asking twice does not build a second
    /// one. See the private constructor above.
    /// </summary>
    public HatchClient Quiet() => _quiet ??= new HatchClient(this);

    /// <summary>
    /// The call itself, with no opinion about what a failure means beyond
    /// whether it was worth asking again.
    /// </summary>
    /// <remarks>
    /// <para>Nothing here throws, short of the caller's own token going. A
    /// connection that never happened comes back as a null status, which every
    /// caller treats as a blip rather than as a refusal - the difference
    /// matters most to the heartbeat, where one is a lost lease and the other
    /// is a minute of bad network.</para>
    ///
    /// <para><b>This is the one place that retries.</b> Every call the runner
    /// makes - the queue, the claim, its heartbeat and release, the post-session
    /// reads, the comments and questions - and every <c>hatch</c> command a
    /// session runs goes through here, so backing off lives here and nowhere
    /// else. While Hatch does not answer - no connection, or a 502, 503 or 504 -
    /// a call waits and tries again: 1s, 2s, 4s, 8s, then every 10s, for up to
    /// <see cref="Settings.RetrySeconds"/> in all, honouring <paramref
    /// name="ct"/> between attempts so a Ctrl-C stops the wait at once.</para>
    ///
    /// <para>A write that may have reached Hatch is never sent twice. <c>GET</c>,
    /// and the idempotent <c>PUT</c> and <c>DELETE</c>, retry on anything
    /// transient. <c>POST</c> and <c>PATCH</c> retry only when the request
    /// cannot have arrived at all - refused before any connection was made, or
    /// answered by a proxy that never reached the app behind it (502, 503). A
    /// 504, a timeout, or a connection that died mid-answer might already have
    /// done what was asked, and ends the call rather than risk doing it twice.
    /// Anything else - a real answer, <c>409</c> included - comes back at once
    /// and is never retried.</para>
    /// </remarks>
    public async Task<Answer> Send(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var start = _clock.GetUtcNow();
        var attempt = 0;
        var announced = false;

        while (true)
        {
            attempt++;

            using var request = new HttpRequestMessage(method, path.TrimStart('/'));
            if (body is RawJson raw)
                request.Content = new StringContent(raw.Value, Encoding.UTF8, "application/json");
            else if (body is not null)
                request.Content = new StringContent(
                    JsonSerializer.Serialize(body, TypeInfo(body.GetType())), Encoding.UTF8, "application/json");

            Answer answer;
            HttpRequestError? requestError = null;
            var timedOut = false;

            try
            {
                using var response = await _http.SendAsync(request, ct);
                answer = new Answer(response.StatusCode, await response.Content.ReadAsStringAsync(ct));
            }
            catch (HttpRequestException e) when (!ct.IsCancellationRequested)
            {
                requestError = e.HttpRequestError;
                answer = new Answer(null, e.Message);
            }
            catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
            {
                timedOut = true;
                answer = new Answer(null, e.Message);
            }

            if (!Retryable(method, answer.Status, requestError, timedOut)) return answer;

            var elapsed = _clock.GetUtcNow() - start;
            var delay = Backoff(attempt);
            if (elapsed + delay >= _window) return answer with { WaitedSeconds = (int)_window.TotalSeconds };

            if (!announced)
            {
                announced = true;
                _onWaiting?.Invoke(
                    $"hatch: {Origin} is not answering - waiting up to {(int)_window.TotalSeconds}s before giving up");
            }

            await Task.Delay(delay, _clock, ct);
        }
    }

    /// <summary>
    /// Null, or a 502, 503 or 504 - Hatch not answering, at whatever layer
    /// noticed: no connection at all, or a proxy that could not reach the app
    /// behind it.
    /// </summary>
    private static bool Transient(HttpStatusCode? status) =>
        status is null or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    /// <summary>
    /// Whether asking again is safe - the idempotency rule the class remarks
    /// describe. A read, and the idempotent <c>PUT</c> and <c>DELETE</c>, may
    /// always ask again once the answer is transient at all. A <c>POST</c> or a
    /// <c>PATCH</c> may only ask again when the first ask cannot have arrived:
    /// refused before any connection was made (<see cref="HttpRequestError.ConnectionError"/>
    /// or <see cref="HttpRequestError.NameResolutionError"/>), or answered by a
    /// proxy that never reached the app (502, 503). A timeout is deliberately
    /// not treated as never-arrived - <c>HttpClient.Timeout</c> firing means the
    /// request went out and nobody knows what happened to it - and neither is a
    /// 504, which is a proxy saying the app itself took the request and then
    /// went quiet.
    /// </summary>
    private static bool Retryable(HttpMethod method, HttpStatusCode? status, HttpRequestError? requestError, bool timedOut)
    {
        if (!Transient(status)) return false;
        if (method == HttpMethod.Get || method == HttpMethod.Put || method == HttpMethod.Delete) return true;

        return status switch
        {
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable => true,
            HttpStatusCode.GatewayTimeout => false,
            null => !timedOut && requestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError,
            _ => false,
        };
    }

    /// <summary>
    /// The wait before the Nth retry: 1s, 2s, 4s, 8s, then every 10s - short at
    /// first, when a blip is likeliest to have already cleared, and cheap to
    /// keep asking after.
    /// </summary>
    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(attempt switch
    {
        1 => 1,
        2 => 2,
        3 => 4,
        4 => 8,
        _ => 10,
    });

    /// <summary>
    /// A read whose refusal is a fault. The sentence is Hatch's own where it
    /// wrote one, because "AER-12 is in another project" is worth more than
    /// "400".
    /// </summary>
    /// <returns>
    /// The parsed body, or null on a <c>204</c> - which is the answer a
    /// finished board gives to <c>work/next</c> and not a failure.
    /// </returns>
    public async Task<T?> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        var answer = await Send(HttpMethod.Get, path, null, ct);
        if (!answer.Ok) throw new HatchException(Refusal(answer, path), answer.Status);
        if (answer.Body.Trim().Length == 0) return null;

        try
        {
            return (T?)JsonSerializer.Deserialize(answer.Body, TypeInfo(typeof(T)));
        }
        catch (JsonException e)
        {
            throw new HatchException($"hatch: {path} answered with something that is not {typeof(T).Name}: {e.Message}");
        }
    }

    /// <summary>A write whose refusal is a fault, answering with whatever came back.</summary>
    public Task<T?> PostAsync<T>(string path, object body, CancellationToken ct) where T : class =>
        WriteAsync<T>(HttpMethod.Post, path, body, ct);

    /// <summary>The same, for the verbs a patch and a delete need.</summary>
    public async Task<T?> WriteAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
        where T : class
    {
        var answer = await Send(method, path, body, ct);
        if (!answer.Ok) throw new HatchException(Refusal(answer, path), answer.Status);
        if (answer.Body.Trim().Length == 0) return null;

        try
        {
            return (T?)JsonSerializer.Deserialize(answer.Body, TypeInfo(typeof(T)));
        }
        catch (JsonException e)
        {
            throw new HatchException($"hatch: {path} answered with something that is not {typeof(T).Name}: {e.Message}");
        }
    }

    /// <summary>The four refusals worth their own sentence, and everything else.</summary>
    internal string Refusal(Answer answer, string path) => answer.Status switch
    {
        // Retried until the window ran out is a different sentence from
        // refused on the spot - "for 90s" is the difference between a deploy
        // this rode out for nothing and one that is still down.
        null => answer.WaitedSeconds is { } waited
            ? $"hatch: could not reach {Origin} for {waited}s - {answer.Body}"
            : $"hatch: could not reach {Origin} - {answer.Body}",
        // No credential was sent, so nothing was rejected: this Hatch has its wall
        // up and wants one. Said plainly, because "the key was not accepted"
        // would send somebody looking at a key they never set.
        HttpStatusCode.Unauthorized when !Keyed =>
            "hatch: 401 - no key was sent and this Hatch has its wall on. Run `hatch config`.",
        HttpStatusCode.Unauthorized =>
            "hatch: 401 - the key was not accepted. Minted, not revoked, copied whole?",
        HttpStatusCode.Forbidden =>
            "hatch: 403 - the key is good and this route is not one it may take (CLAUDE.md).",
        HttpStatusCode.NotFound => $"hatch: 404 - no such issue or route: {path}",
        var code => $"hatch: {(int)code!} - {Sentence(answer.Body)}",
    };

    /// <summary>
    /// A structured body's own sentence, where it has one - the WIP
    /// <c>409</c>'s <c>{ error, load, limit }</c>, following
    /// <c>AuthErrorDto</c>'s <c>{ error }</c> shape. Everything else -
    /// Hatch's ordinary bare-string refusal, an object with no string
    /// <c>error</c>, anything unparseable - prints exactly as it arrived, the
    /// way it always has.
    /// </summary>
    private static string Sentence(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                ? error.GetString() ?? body
                : body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    /// <summary>
    /// Never for the quiet copy <see cref="Quiet"/> hands back - it shares the
    /// original's <see cref="HttpClient"/> rather than owning one, and the
    /// original is whose job disposing it is.
    /// </summary>
    public void Dispose()
    {
        if (!_shared) _http.Dispose();
    }
}

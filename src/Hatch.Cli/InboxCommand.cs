using System.Text.Json;

namespace Hatch.Cli;

/// <summary>What a <c>PostToolUse</c> hook prints to put text in front of the model.</summary>
public sealed record PostToolUseOutput(PostToolUseContext HookSpecificOutput);

/// <summary>The text itself, and the event it answers.</summary>
public sealed record PostToolUseContext(string HookEventName, string AdditionalContext);

/// <summary>What a <c>Stop</c> hook prints to keep the session going, with the reason it is given.</summary>
public sealed record StopBlock(string Decision, string Reason);

/// <summary>
/// <c>hatch inbox</c>: what a session's hooks call to ask whether anyone has said
/// something to it, and to hand it over if they have.
/// </summary>
/// <remarks>
/// <para>An internal command. It exists only for the hooks
/// <see cref="Increment"/> declares, and no session is told about it: a session
/// that knew could mark its operator's messages read without reading them, and
/// then the page would say something that is not true. See
/// <see cref="Program.Internal"/>.</para>
///
/// <para>It must never fail, stop or slow a session, whatever Hatch does. Every
/// way a call can go wrong ends in nothing printed and an exit of zero; the
/// message stays unread on the server and a later step asks again.</para>
///
/// <para>The per-step hook is throttled to one request every
/// <see cref="ThrottleSeconds"/>, by the modification time of a stamp file the
/// runner keeps in a directory of its own: a warm process starts in tens of
/// milliseconds but a round trip is a quarter of a second, and asking after
/// every one of three hundred tool calls would add over a minute to an
/// increment. The stop hook always asks, because a session that ends with a
/// message unread has dropped it.</para>
/// </remarks>
public sealed class InboxCommand(Board board, Terminal say, TextReader stdin, TimeProvider clock)
{
    /// <summary>The fewest seconds between two per-step checks.</summary>
    public const int ThrottleSeconds = 15;

    /// <summary>
    /// How long a call may take. The hooks are given ten seconds, and the
    /// client's own timeout is two minutes, so this is what keeps a Hatch that
    /// never answers from being a session that never continues.
    /// </summary>
    public const int CallSeconds = 5;

    /// <summary>Replaceable so a test need not wait the whole of <see cref="CallSeconds"/> for a Hatch that never answers.</summary>
    public TimeSpan Limit { get; init; } = TimeSpan.FromSeconds(CallSeconds);

    public static readonly string[] InboxUsage =
    [
        "usage: hatch inbox <key> --hook post-tool-use|stop [--stamp <path>] [--clamp <path>]",
        "",
        "  For the hooks a spawned session runs, and nothing else: it prints what was",
        "  said to the session on this ticket, in the shape the hook expects, and marks",
        "  it read. It never fails - a Hatch that does not answer prints nothing.",
    ];

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(say, InboxUsage);

        string? key = null, hook = null, stamp = null, clamp = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--hook" when i + 1 < args.Length: hook = args[++i]; break;
                case "--stamp" when i + 1 < args.Length: stamp = args[++i]; break;
                case "--clamp" when i + 1 < args.Length: clamp = args[++i]; break;
                case var other when !other.StartsWith('-') && key is null: key = other; break;
                default: return Usage.Refuse(say, $"inbox does not know {args[i]}", InboxUsage);
            }
        }

        if (key is null || hook is not ("post-tool-use" or "stop"))
            return Usage.Refuse(say, "inbox takes an issue key and --hook post-tool-use|stop", InboxUsage);

        // The hook's own JSON, which nothing here needs. Read to the end so the
        // session is not left writing into a pipe nobody drains.
        try
        {
            await stdin.ReadToEndAsync(ct);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Nothing to discard.
        }

        var wrapUp = Clamped(clamp, key);

        if (hook == "post-tool-use" && stamp is not null)
        {
            var due = Due(stamp);
            if (wrapUp is null && !due) return 0;
        }

        IReadOnlyList<CommentDto> messages;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(Limit);
            messages = await board.DeliverMessagesAsync(key, null, limit.Token);
        }
        catch (Exception e) when (e is HatchException or OperationCanceledException or HttpRequestException or JsonException)
        {
            // A Hatch that is down, slow, or has never heard of the route - the
            // wrap-up below, if there is one, is on disk already and does not
            // depend on this call.
            messages = [];
        }

        var parts = new List<string>();
        if (wrapUp is not null) parts.Add(wrapUp);
        parts.AddRange(messages.Select(m => Prompt.Message(key, m, whileWorking: true)));

        if (parts.Count == 0) return 0;

        var text = string.Join("\n\n---\n\n", parts);
        say.Line(hook == "stop"
            ? JsonSerializer.Serialize(new StopBlock("block", text), HatchJson.Default.StopBlock)
            : JsonSerializer.Serialize(
                new PostToolUseOutput(new PostToolUseContext("PostToolUse", text)), HatchJson.Default.PostToolUseOutput));
        return 0;
    }

    /// <summary>
    /// The wrap-up text, the one time it is owed: a <c>--clamp</c> path was
    /// given, the fact record is there, and nothing has delivered it yet. The
    /// marker is touched before the text is handed back, so a session that
    /// crashes between this call and printing its output is not told twice -
    /// the cost of that race is silence, not repetition.
    /// </summary>
    private string? Clamped(string? clamp, string key)
    {
        if (clamp is null) return null;

        try
        {
            if (!File.Exists(clamp)) return null;

            var delivered = Path.ChangeExtension(clamp, ".delivered");
            if (File.Exists(delivered)) return null;

            File.WriteAllBytes(delivered, []);
            return Prompt.WrapUp(key);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A marker that cannot be kept must not become a wrap-up said every
            // call - nor one never said at all. Skip this one.
            return null;
        }
    }

    /// <summary>
    /// Whether a per-step check is due, and if it is, the stamp is touched
    /// before the call rather than after: a call that hangs still spends the
    /// interval, so a slow Hatch costs one wait per fifteen seconds and not one
    /// per tool call.
    /// </summary>
    private bool Due(string stamp)
    {
        try
        {
            var now = clock.GetUtcNow();
            if (File.Exists(stamp) && now - new DateTimeOffset(File.GetLastWriteTimeUtc(stamp), TimeSpan.Zero) < TimeSpan.FromSeconds(ThrottleSeconds))
                return false;

            File.WriteAllBytes(stamp, []);
            File.SetLastWriteTimeUtc(stamp, now.UtcDateTime);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A stamp that cannot be kept must not become a check that is never
            // made - nor one made every step. Skip this one.
            return false;
        }
    }
}

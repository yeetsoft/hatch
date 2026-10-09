using System.Text;
using System.Text.Json;

namespace Hatch.Cli;

/// <summary>
/// The hooks that carry a message sent while a session works into its context,
/// and the directory outside every checkout that they live in.
/// </summary>
/// <remarks>
/// <para>A settings file handed to <c>claude --settings</c> rather than one
/// written into the checkout, so that wiring this in leaves <c>git status</c>
/// showing only what the session changed. The stamp that throttles the per-step
/// check lives beside it for the same reason.</para>
///
/// <para>Two hooks, each ten seconds long: <c>PostToolUse</c> on every tool, to
/// deliver at the session's next step, and <c>Stop</c>, to keep a session that is
/// finishing going long enough to read what is waiting. Both run <c>hatch
/// inbox</c>, which is this program.</para>
///
/// <para>A third, <c>PreToolUse</c> on <c>Bash</c>, is written when an <c>rtk</c>
/// binary is on <c>PATH</c> and <c>HATCH_RTK</c> is not <c>off</c>: RTK rewrites
/// a shell command into a compact proxy of itself, so the output that reaches
/// the model is a fraction of the size. It lives here because this is the one
/// settings file every session is handed, whether or not the operator ever ran
/// <c>rtk init -g</c>. Without RTK the file is what it always was.</para>
/// </remarks>
public sealed class SessionHooks : IDisposable
{
    /// <summary>What a hook is given before it is killed - comfortably more than <see cref="InboxCommand.CallSeconds"/>.</summary>
    public const int TimeoutSeconds = 10;

    private SessionHooks(string directory, string settings)
    {
        Directory = directory;
        Settings = settings;
    }

    /// <summary>The directory made for one increment.</summary>
    public string Directory { get; }

    /// <summary>The settings file, to hand to <c>--settings</c>.</summary>
    public string Settings { get; }

    /// <summary>The file whose modification time is when a per-step check last asked.</summary>
    public string Stamp => Path.Combine(Directory, "inbox.stamp");

    /// <summary>
    /// The fact record written once this session's tokens cross its playbook's
    /// budget - whichever hook call notices first writes it, and it is never
    /// deleted until the whole directory goes with the rest of <see cref="Dispose"/>.
    /// </summary>
    public string Clamp => Path.Combine(Directory, "clamp.json");

    /// <summary>
    /// Touched the first time <see cref="InboxCommand"/> folds the wrap-up text
    /// into a hook's output, and never reset - so a session is told once, not
    /// on every call for the rest of its life.
    /// </summary>
    public string ClampDelivered => Path.ChangeExtension(Clamp, ".delivered");

    /// <summary>
    /// The playbook's budget, in tokens, written once before the session's
    /// quiet/streamed branches split - its mere presence on disk is the signal
    /// that this session has a budget at all, so a <c>--quiet</c> session's hook
    /// knows whether to read its own transcript for the count a streamed
    /// session's in-process loop already has.
    /// </summary>
    public string Budget => Path.Combine(Directory, "budget");

    /// <summary>
    /// Makes the directory and writes the file, or answers null where it cannot.
    /// A session without hooks still works its ticket; it is a message sent
    /// mid-run that waits for the next session instead.
    /// </summary>
    /// <param name="temp">Where directories are made: <see cref="Path.GetTempPath"/> in a real run, and never inside a checkout.</param>
    /// <param name="hatch">
    /// What the hooks run. <see cref="Environment.ProcessPath"/> where this is a
    /// binary called <c>hatch</c>, the bare word otherwise - the same rule
    /// <see cref="Reach"/> uses for the session's <c>PATH</c>, and for the same
    /// reason: the word only resolves where <c>Reach</c> made it.
    /// </param>
    /// <param name="rtk">The absolute path to <c>rtk</c> from <see cref="Rtk"/>, or null to write no <c>PreToolUse</c> hook.</param>
    public static SessionHooks? Write(string temp, string key, string hatch, string? rtk = null)
    {
        string? made = null;
        try
        {
            made = Path.Combine(temp, $"hatch-hooks-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(made);
            var hooks = new SessionHooks(made, Path.Combine(made, "settings.json"));

            var run = $"{Quote(hatch)} inbox {Quote(key)} --hook";
            File.WriteAllText(hooks.Settings, Json(
                rtk is null ? null : $"{Quote(rtk)} hook claude",
                $"{run} post-tool-use --stamp {Quote(hooks.Stamp)} --clamp {Quote(hooks.Clamp)} --budget {Quote(hooks.Budget)}",
                $"{run} stop --clamp {Quote(hooks.Clamp)} --budget {Quote(hooks.Budget)}"));
            return hooks;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (made is not null) TryDelete(made);
            return null;
        }
    }

    /// <summary>The command a hook runs for this binary, as it will be written.</summary>
    public static string Binary(string? processPath) =>
        Reach.OwnDirectory(processPath) is null ? "hatch" : processPath!;

    /// <summary>
    /// The <c>rtk</c> to hook in, or null when <paramref name="setting"/> (the
    /// <c>HATCH_RTK</c> value) is <c>off</c> or none is on <paramref name="path"/>.
    /// </summary>
    public static string? Rtk(string? setting, string? path) =>
        string.Equals(setting?.Trim(), "off", StringComparison.OrdinalIgnoreCase) ? null : Reach.Find("rtk", path);

    private static string Json(string? preToolUse, string postToolUse, string stop)
    {
        // Written by hand, because the binary is trimmed and this is a shape
        // known at compile time.
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteStartObject("hooks");
            if (preToolUse is not null) Hook(json, "PreToolUse", preToolUse, matcher: "Bash");
            Hook(json, "PostToolUse", postToolUse, matcher: "*");
            Hook(json, "Stop", stop, matcher: null);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Hook(Utf8JsonWriter json, string name, string command, string? matcher)
    {
        json.WriteStartArray(name);
        json.WriteStartObject();
        if (matcher is not null) json.WriteString("matcher", matcher);
        json.WriteStartArray("hooks");
        json.WriteStartObject();
        json.WriteString("type", "command");
        json.WriteString("command", command);
        json.WriteNumber("timeout", TimeoutSeconds);
        json.WriteEndObject();
        json.WriteEndArray();
        json.WriteEndObject();
        json.WriteEndArray();
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    public void Dispose() => TryDelete(Directory);

    private static void TryDelete(string directory)
    {
        try
        {
            System.IO.Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left in the temporary directory, which is where it was made.
        }
    }
}

/// <summary>What is written to <see cref="SessionHooks.Clamp"/>: the tally at the moment the budget was crossed.</summary>
public sealed record ClampFact(long Tokens, long Requests);

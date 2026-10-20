using System.Text;
using System.Text.Json;

namespace Hatch.Cli;

/// <summary>
/// The Codex counterpart to <see cref="SessionHooks"/>: the hooks that carry a
/// message sent while a session works into its context, in a directory outside
/// every checkout that the spawn is handed as its <c>CODEX_HOME</c>.
/// </summary>
/// <remarks>
/// <para>Codex has no flag that takes a hooks file, and <c>-c</c> sets one scalar
/// per flag where a hook list is an array of tables, so the door is the home
/// directory itself: <c>hooks.json</c> is written into a directory of its own and
/// <see cref="Opaque"/> is that directory. Only <c>auth.json</c> is copied in from
/// the operator's real home - an overlay without the login would log the session
/// out - so the operator's own hooks and MCP config do not apply to the run, which
/// is the isolation wanted. Where the real <c>auth.json</c> does not exist (the
/// login is in a keyring, or there is none) <see cref="Write"/> answers null for
/// the same reason.</para>
///
/// <para>Two hooks, as <see cref="SessionHooks"/> writes for Claude: <c>PostToolUse</c>
/// on every tool and <c>Stop</c>, each running <c>hatch inbox</c>. No <c>PreToolUse</c>
/// hook is written; <c>rtk</c> is accepted and ignored.</para>
///
/// <para>A refreshed login is written back: Codex rotates the tokens in
/// <c>auth.json</c> as it runs, and the copy is deleted with the directory, so
/// <see cref="Dispose"/> copies it over the real one if it changed.</para>
///
/// <para>HA-910's proof against a real, logged-in CLI found that <c>codex exec</c>
/// skips every hook in an overlay's <c>hooks.json</c> unless the spawn also carries
/// <c>--dangerously-bypass-hook-trust</c>, a flag the adapter never sends. That is
/// why the adapter's <c>WriteHooks</c> answers null rather than reaching
/// <see cref="Write"/>, and a session runs with no <c>CODEX_HOME</c> at all.</para>
///
/// <para>Do not re-open that door without solving resume. The overlay is the
/// session's whole Codex home, so the thread's rollout is written inside it and
/// <see cref="Dispose"/> deletes it with the directory; <c>codex exec resume &lt;id&gt;</c>
/// then fails with <c>thread/resume failed: no rollout found for thread id</c>
/// once the increment ends (seen on a real session, HA-1026). Either copy the
/// session's rollout and thread files back into the real home in
/// <see cref="Dispose"/>, as <c>auth.json</c> is, or change the sentence
/// <see cref="CodexAdapter.ResumeCommand"/> prints so it does not promise a join
/// that will not outlive the increment.</para>
/// </remarks>
public sealed class CodexHooks : IAgentHooks
{
    private const string AuthFile = "auth.json";

    private readonly string _realAuth;
    private readonly byte[] _copied;

    private CodexHooks(string directory, string realAuth, byte[] copied)
    {
        Directory = directory;
        _realAuth = realAuth;
        _copied = copied;
    }

    /// <summary>The directory made for one increment.</summary>
    public string Directory { get; }

    /// <summary>The hooks file Codex reads out of its home.</summary>
    public string HooksFile => Path.Combine(Directory, "hooks.json");

    /// <summary>The directory itself, which the spawn sets as <c>CODEX_HOME</c>.</summary>
    public string? Opaque => Directory;

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

    private string OverlayAuth => Path.Combine(Directory, AuthFile);

    /// <summary>
    /// Makes the directory, writes the hooks and copies the login in, or answers
    /// null where it cannot. A session without hooks still works its ticket; it
    /// is a message sent mid-run that waits for the next session instead.
    /// </summary>
    /// <param name="temp">Where directories are made: <see cref="Path.GetTempPath"/> in a real run, and never inside a checkout.</param>
    /// <param name="hatch">What the hooks run - see <see cref="SessionHooks.Binary"/>.</param>
    /// <param name="rtk">Accepted so the call matches <see cref="SessionHooks.Write"/>, and ignored.</param>
    /// <param name="codexHome">
    /// The operator's real Codex home, or null for <c>CODEX_HOME</c> when that is
    /// set and not blank, and <c>.codex</c> under the user's profile otherwise.
    /// </param>
    public static CodexHooks? Write(string temp, string key, string hatch, string? rtk = null, string? codexHome = null)
    {
        var home = string.IsNullOrWhiteSpace(codexHome) ? Environment.GetEnvironmentVariable("CODEX_HOME") : codexHome;
        if (string.IsNullOrWhiteSpace(home))
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

        var realAuth = Path.Combine(home, AuthFile);
        string? made = null;
        try
        {
            if (!File.Exists(realAuth)) return null;
            var original = File.ReadAllBytes(realAuth);

            made = Path.Combine(temp, $"hatch-hooks-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(made);
            var hooks = new CodexHooks(made, realAuth, original);

            var run = $"{SessionHooks.Quote(hatch)} inbox {SessionHooks.Quote(key)} --hook";
            File.WriteAllText(hooks.HooksFile, Json(
                $"{run} post-tool-use --stamp {SessionHooks.Quote(hooks.Stamp)} --clamp {SessionHooks.Quote(hooks.Clamp)} --budget {SessionHooks.Quote(hooks.Budget)}",
                $"{run} stop --clamp {SessionHooks.Quote(hooks.Clamp)} --budget {SessionHooks.Quote(hooks.Budget)}"));

            // A credential in a temporary directory: written, then restricted.
            File.WriteAllBytes(hooks.OverlayAuth, original);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(hooks.OverlayAuth, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return hooks;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (made is not null) SessionHooks.TryDelete(made);
            return null;
        }
    }

    /// <summary>Writes a refreshed login back to the real home, then removes the directory.</summary>
    public void Dispose()
    {
        try
        {
            if (File.Exists(OverlayAuth))
            {
                var now = File.ReadAllBytes(OverlayAuth);
                if (!now.AsSpan().SequenceEqual(_copied)) File.WriteAllBytes(_realAuth, now);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The next session starts from the login the real home still has.
        }

        SessionHooks.TryDelete(Directory);
    }

    private static string Json(string postToolUse, string stop)
    {
        // Written by hand, because the binary is trimmed and this is a shape
        // known at compile time.
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteStartObject("hooks");
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
        json.WriteNumber("timeout", SessionHooks.TimeoutSeconds);
        json.WriteEndObject();
        json.WriteEndArray();
        json.WriteEndObject();
        json.WriteEndArray();
    }
}

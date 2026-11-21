using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Hatch.Cli;

/// <summary>
/// A child's ask to be moved to another build: which board it is about, which
/// build, and where to fetch it and what the bytes must hash to.
/// </summary>
/// <param name="Origin">The board the child was running against. A request about another board is not this launcher's to act on.</param>
/// <param name="Revision">The build's full sha, the folder it will be kept under.</param>
/// <param name="Sequence">The build's own commit count, carried for the log.</param>
/// <param name="Download">The path the build is fetched from, on the same board.</param>
/// <param name="Sha256">What the downloaded bytes must hash to.</param>
/// <param name="Rid">The platform the build is for.</param>
public sealed record BuildRequest(string Origin, string Revision, int Sequence, string Download, string Sha256, string Rid);

/// <summary>
/// The whole conversation between a launcher and its child about a change of
/// build, both ends in one place so neither can drift from the other. Every
/// file lives beside the night's state file, which is the one place a launcher
/// and every member of a set already share.
/// </summary>
/// <remarks>
/// <para><c>build.json</c> goes from child to launcher, and the last writer
/// wins. <c>answered</c> goes the same way and holds the revision of the build
/// whose first beat the board answered. <c>HATCH_BUILD_HOLD</c> and
/// <c>HATCH_BUILD_NOTE</c> go from launcher to child as environment, and are
/// absent unless there is something to say.</para>
///
/// <para>Nothing here throws on a missing or damaged file: a handshake that
/// fails is the same as one never made.</para>
/// </remarks>
public static class BuildHandoff
{
    /// <summary>The file a child asks for another build in.</summary>
    public const string RequestFileName = "build.json";

    /// <summary>The file a child says its build was heard from in.</summary>
    public const string AnsweredFileName = "answered";

    /// <summary>Revisions the launcher will not take, and until when. Set by the launcher and by no person.</summary>
    public const string HoldVariable = "HATCH_BUILD_HOLD";

    /// <summary>One line the child says at start, so what the launcher decided reaches the log. Set by the launcher and by no person.</summary>
    public const string NoteVariable = "HATCH_BUILD_NOTE";

    /// <summary>Writes a request, whole or not at all.</summary>
    public static void Write(string directory, BuildRequest request) =>
        AtomicFile.WriteText(
            Path.Combine(directory, RequestFileName),
            JsonSerializer.Serialize(request, HatchJson.Default.BuildRequest));

    /// <summary>
    /// The request, read and removed. Null when there is none or it cannot be
    /// read - a garbled one is removed too, so it is not read again.
    /// </summary>
    public static BuildRequest? Take(string directory)
    {
        var path = Path.Combine(directory, RequestFileName);

        try
        {
            if (!File.Exists(path)) return null;

            BuildRequest? request = null;
            try
            {
                request = JsonSerializer.Deserialize(File.ReadAllText(path), HatchJson.Default.BuildRequest);
            }
            finally
            {
                File.Delete(path);
            }

            return request is { Revision.Length: > 0, Origin.Length: > 0, Download.Length: > 0 } ? request : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Says the build of <paramref name="revision"/> was heard from. False where it could not be written.</summary>
    public static bool MarkAnswered(string directory, string revision)
    {
        try
        {
            AtomicFile.WriteText(Path.Combine(directory, AnsweredFileName), revision);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The revision a child said it was heard from, or null.</summary>
    public static string? Answered(string directory)
    {
        try
        {
            var path = Path.Combine(directory, AnsweredFileName);
            return File.Exists(path) ? File.ReadAllText(path).Trim() is { Length: > 0 } text ? text : null : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Removes the request and the answer, before a child starts, so it never reads the last one's.</summary>
    public static void Clear(string directory)
    {
        foreach (var name in new[] { RequestFileName, AnsweredFileName })
        {
            try { File.Delete(Path.Combine(directory, name)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}

/// <summary>
/// Revisions that are not to be asked for again, each until a time or for
/// good. The launcher keeps it in memory and tells its child through
/// <see cref="BuildHandoff.HoldVariable"/>, so a child does not ask for what
/// the launcher would drop.
/// </summary>
public sealed class BuildHold
{
    private const string Forever = "never";

    private readonly Dictionary<string, long?> until = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Holds <paramref name="revision"/> until <paramref name="when"/>, unless it is already held for longer.</summary>
    public void Until(string revision, DateTimeOffset when)
    {
        var seconds = when.ToUnixTimeSeconds();
        if (until.TryGetValue(revision, out var existing) && (existing is null || existing >= seconds)) return;
        until[revision] = seconds;
    }

    /// <summary>Holds <paramref name="revision"/> for good.</summary>
    public void Never(string revision) => until[revision] = null;

    /// <summary>Whether <paramref name="revision"/> is held at <paramref name="now"/>.</summary>
    public bool Holds(string revision, DateTimeOffset now) =>
        until.TryGetValue(revision, out var seconds) && (seconds is null || now.ToUnixTimeSeconds() < seconds);

    /// <summary>
    /// <c>&lt;sha&gt;=&lt;unix seconds&gt;</c> or <c>&lt;sha&gt;=never</c>, comma separated, for the holds still
    /// in force. Null where there are none, so the variable is not set at all.
    /// </summary>
    public string? Format(DateTimeOffset now)
    {
        var live = until
            .Where(h => h.Value is null || now.ToUnixTimeSeconds() < h.Value)
            .OrderBy(h => h.Key, StringComparer.Ordinal)
            .Select(h => h.Key + "=" + (h.Value is { } s ? s.ToString(CultureInfo.InvariantCulture) : Forever))
            .ToList();

        return live.Count == 0 ? null : string.Join(',', live);
    }

    /// <summary>Reads what <see cref="Format"/> wrote. Anything it cannot read is left out: garbage is an empty hold.</summary>
    public static BuildHold Parse(string? text)
    {
        var hold = new BuildHold();
        if (string.IsNullOrWhiteSpace(text)) return hold;

        foreach (var pair in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var at = pair.IndexOf('=');
            if (at <= 0) continue;

            var revision = pair[..at].Trim();
            var value = pair[(at + 1)..].Trim();
            if (revision.Length == 0) continue;

            if (string.Equals(value, Forever, StringComparison.OrdinalIgnoreCase)) hold.Never(revision);
            else if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) hold.until[revision] = seconds;
        }

        return hold;
    }

    /// <summary>How long a revision is held after its <paramref name="failures"/>th failure in a row: a minute, doubling, to an hour.</summary>
    public static TimeSpan Backoff(int failures) =>
        TimeSpan.FromMinutes(Math.Min(60, 1L << Math.Clamp(failures - 1, 0, 6)));

    /// <summary>A hold in the words of a one-line note: <c>2m</c>, <c>1h</c>.</summary>
    public static string Words(TimeSpan span) =>
        span >= TimeSpan.FromHours(1) ? $"{(int)span.TotalHours}h" : $"{(int)Math.Max(1, span.TotalMinutes)}m";
}

/// <summary>The board a launcher moves builds for: where it is, and the settings that reach it.</summary>
public sealed record LauncherBoard(string Origin, Settings Settings);

/// <summary>Fetches a build from the board, so a test can stand in for it.</summary>
public interface IBuildFetch
{
    /// <summary>Downloads <paramref name="path"/> from <paramref name="board"/> into <paramref name="destination"/>.</summary>
    Task<Download> DownloadAsync(LauncherBoard board, string path, string destination, CancellationToken ct);
}

/// <summary>The real fetch: a client over the board's own settings, keyed.</summary>
public sealed class HatchBuildFetch : IBuildFetch
{
    public async Task<Download> DownloadAsync(LauncherBoard board, string path, string destination, CancellationToken ct)
    {
        using var client = new HatchClient(board.Settings);
        return await client.DownloadAsync(path, destination, ct);
    }
}

/// <summary>
/// Everything the launcher needs to change builds, beside what it needs to
/// start one. Null in <see cref="Launcher.RunAsync"/> means it never does.
/// </summary>
/// <param name="Board">Which board this launcher is for right now. Asked at every start and handoff, since <c>hatch use</c> can move it. Null when it cannot say.</param>
/// <param name="Store">Where builds are kept and each board's record.</param>
/// <param name="Fetch">How a build is fetched.</param>
/// <param name="Verify">Runs a stored build's <c>version</c> and answers null or a failure sentence: binary, then revision.</param>
/// <param name="Clock">What a hold is measured against.</param>
/// <param name="Poll">How often the launcher looks for the child's first answered beat.</param>
public sealed record LauncherBuilds(
    Func<LauncherBoard?> Board, BuildStore Store, IBuildFetch Fetch, Func<string, string, Task<string?>> Verify,
    TimeProvider Clock, TimeSpan Poll)
{
    /// <summary>The real ones: the per-user folder, the board's own client, and a minute for a build to say what it is.</summary>
    public static LauncherBuilds Real(Func<LauncherBoard?> board) =>
        new(board, new BuildStore(Settings.UserFolder()), new HatchBuildFetch(),
            (binary, revision) => BuildCheck.RunVersionAsync(binary, revision, TimeSpan.FromSeconds(60)),
            TimeProvider.System, TimeSpan.FromSeconds(2));
}

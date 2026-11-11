using System.Text.Json;

namespace Hatch.Cli;

/// <summary>
/// What an unattended increment leaves behind for the next loop in the checkout
/// to find if this one dies: the agent's pid and when that process started, and
/// the claim it was running under.
/// </summary>
/// <remarks>
/// <para>Beside <see cref="LoopLock"/> and for the same reason - under the
/// temporary directory, keyed by the checkout, never in the tree or the night
/// state. The lock says a loop is running; this says what that loop had spawned,
/// which is the one thing a crash or a closed window leaves running with nobody
/// to stop it.</para>
///
/// <para>The start time is what makes the pid safe to act on. A pid is reused,
/// and killing whatever now has the number is not the same as ending the
/// session this recorded; a process is the recorded one only if it started when
/// the record says.</para>
/// </remarks>
/// <param name="Pid">The agent's process id.</param>
/// <param name="StartTicks">When that process started, as UTC ticks.</param>
/// <param name="Key">The issue the increment was working.</param>
/// <param name="Token">The claim's token, which is what lets the clean-up release it and nobody else's.</param>
/// <param name="TakenAt">When the record was written.</param>
/// <param name="Base">The Hatch the claim was taken against - a record for another board is not this loop's to act on.</param>
/// <param name="SessionId">What the agent's resume command takes, once the session has said it; null before that.</param>
public sealed record SessionRecord(
    int Pid, long StartTicks, string Key, Guid Token, DateTimeOffset TakenAt, string Base, string? SessionId = null)
{
    /// <summary>Where the record for this checkout lives.</summary>
    public static string PathFor(string root, string tempDirectory) =>
        Path.Combine(tempDirectory, $"hatch-session-{Checkout.Fingerprint(root)}.json");

    /// <summary>When the recorded process started.</summary>
    public DateTime StartedAt => new(StartTicks, DateTimeKind.Utc);

    /// <summary>
    /// Written whole or not at all. Best-effort: a record that cannot be written
    /// costs the next loop its chance to clean up, and must not fail the
    /// increment that was about to run.
    /// </summary>
    public void Write(string path)
    {
        try
        {
            AtomicFile.WriteText(path, JsonSerializer.Serialize(this, HatchJson.Default.SessionRecord));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The record, or null where there is none or what is there cannot be read as one.</summary>
    public static SessionRecord? Read(string path)
    {
        try
        {
            var record = JsonSerializer.Deserialize(File.ReadAllText(path), HatchJson.Default.SessionRecord);
            return record is { Pid: > 0, Key.Length: > 0, Base: not null } ? record : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gone, unless it names another board: that one is reported by the loop
    /// that finds it and left for a checkout that can act on it.
    /// </summary>
    public static void DeleteOurs(string path, string baseUrl)
    {
        if (Read(path) is { } record
            && !string.Equals(record.Base.TrimEnd('/'), baseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            return;

        Delete(path);
    }

    /// <summary>Gone, and quietly so where it already was.</summary>
    public static void Delete(string? path)
    {
        if (path is null) return;

        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

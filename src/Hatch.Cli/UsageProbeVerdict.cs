using System.Text.Json;

namespace Hatch.Cli;

/// <summary>
/// What one process learned about <c>/usage</c> on a CLI version - whether it
/// cost a model turn, and what that turn cost - carried to the next process
/// that spawns the same version, so it is not asked to re-learn it.
/// </summary>
public sealed record UsageProbeVerdict(string Version, decimal? CostUsd)
{
    /// <summary>A sibling of <see cref="Settings.UserConfigPath"/>, named for what it holds.</summary>
    public static string Path() =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Settings.UserConfigPath())!, "usage-probe-verdict");

    /// <summary>
    /// What is at <paramref name="path"/>, or null - which is no verdict
    /// known yet. A file that is missing, unreadable or malformed reads as
    /// null rather than throwing: the worst it costs is a probe re-run.
    /// </summary>
    public static UsageProbeVerdict? Read(string? path)
    {
        if (path is not { Length: > 0 } || !File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), HatchJson.Default.UsageProbeVerdict);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Hand it forward. Answers whether it got there.</summary>
    public bool Write(string? path)
    {
        if (path is not { Length: > 0 }) return false;

        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, HatchJson.Default.UsageProbeVerdict));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

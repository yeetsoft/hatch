using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Hatch.Cli;

/// <summary>
/// Seven facts about the machine a process runs on, read once per process and
/// stamped on every beat. A fact that cannot be read is null rather than
/// guessed.
/// </summary>
public sealed record MachineFacts(
    string? Os, string? Arch, string? AgentVersion, bool? Container, string? Username, DateTimeOffset? ProcessStartedAt,
    string? Image)
{
    /// <summary>Every fact null - a process that never asked, or an adapter with no binary of its own.</summary>
    public static readonly MachineFacts None = new(null, null, null, null, null, null, null);

    /// <summary>
    /// Every fact a running process can read: <see cref="RuntimeInformation.OSDescription"/>
    /// shortened by <see cref="ShortOs"/>, <see cref="RuntimeInformation.OSArchitecture"/>
    /// lowercased, <paramref name="adapter"/>'s own <c>--version</c> read through
    /// <paramref name="probe"/>, whether <see cref="InContainer"/> says this process is
    /// inside one, the OS username, when this process itself started, and
    /// <see cref="ImageFrom"/>'s own answer off the real environment.
    /// </summary>
    public static async Task<MachineFacts> ReadAsync(IAgentAdapter adapter, Probe probe, CancellationToken ct)
    {
        var os = ShortOs(RuntimeInformation.OSDescription);
        var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        var agentVersion = await AgentVersionAsync(adapter.Executable, probe, ct);
        var container = InContainer(File.Exists, path =>
        {
            try { return File.ReadAllText(path); }
            catch { return null; }
        });
        var username = Environment.UserName;
        var image = ImageFrom(Environment.GetEnvironmentVariable);

        DateTimeOffset? startedAt;
        try { startedAt = Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
        catch { startedAt = null; }

        return new MachineFacts(os, arch, agentVersion, container, username, startedAt, image);
    }

    /// <summary><paramref name="env"/>'s <c>HATCH_IMAGE</c>, trimmed - null where unset, blank, or all whitespace.</summary>
    public static string? ImageFrom(Func<string, string?> env)
    {
        var value = env("HATCH_IMAGE")?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// <paramref name="description"/>'s first token, and a second where the first is
    /// <c>Microsoft</c> (so <c>Microsoft Windows 10.0.22631</c> keeps <c>Microsoft
    /// Windows</c>), plus the token after that - <c>Darwin 23.6.0 Darwin Kernel
    /// Version...</c> becomes <c>Darwin 23.6.0</c>. Null for null or blank; at most
    /// 64 characters.
    /// </summary>
    public static string? ShortOs(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;

        var tokens = description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;

        var take = tokens[0] == "Microsoft" ? 2 : 1;
        if (tokens.Length <= take) return Truncate(string.Join(' ', tokens.Take(take)));

        var result = string.Join(' ', tokens.Take(take + 1));
        return Truncate(result);
    }

    /// <summary>
    /// True when <paramref name="exists"/> finds <c>/.dockerenv</c>, or
    /// <paramref name="readText"/> reads <c>/proc/self/cgroup</c> containing
    /// <c>docker</c>, <c>kubepods</c>, <c>containerd</c> or <c>libpod</c>. A
    /// throwing or null-returning reader is false, same as no cgroup file at all.
    /// </summary>
    public static bool InContainer(Func<string, bool> exists, Func<string, string?> readText)
    {
        string[] markers = ["docker", "kubepods", "containerd", "libpod"];

        if (Safe(() => exists("/.dockerenv"), false)) return true;

        var cgroup = Safe(() => readText("/proc/self/cgroup"), null);
        return cgroup is not null && markers.Any(marker => cgroup.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>
    /// Null for a null <paramref name="file"/>; else <paramref name="file"/> run with
    /// <c>--version</c> through <paramref name="probe"/>, the first non-blank output
    /// line trimmed to at most 64 characters where it completed with exit code 0,
    /// else null. A throw other than cancellation is caught and answered as null.
    /// </summary>
    public static async Task<string?> AgentVersionAsync(string? file, Probe probe, CancellationToken ct)
    {
        if (file is null) return null;

        ProbeResult result;
        try
        {
            result = await probe(file, ["--version"], Detect.ProbeTimeout, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }

        if (!result.Completed || result.ExitCode != 0) return null;

        var line = result.Output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        return line is null ? null : Truncate(line);
    }

    /// <summary><paramref name="request"/> with these seven fields set and every other field left alone.</summary>
    public RunnerHeartbeatRequest Stamp(RunnerHeartbeatRequest request) => request with
    {
        Os = Os,
        Arch = Arch,
        AgentVersion = AgentVersion,
        Container = Container,
        Username = Username,
        ProcessStartedAt = ProcessStartedAt,
        Image = Image,
    };

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }

    private static string? Truncate(string? text) => text is { Length: > 64 } ? text[..64] : text;
}

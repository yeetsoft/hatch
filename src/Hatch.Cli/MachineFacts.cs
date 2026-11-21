using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>
/// Eleven facts about the machine a process runs on, read once per process and
/// stamped on every beat. A fact that cannot be read is null rather than
/// guessed.
/// </summary>
/// <param name="Rid">
/// The platform this build was published for, by <see cref="RidOf"/> - the name
/// the board's downloads go by, which <paramref name="Os"/> is not.
/// </param>
public sealed record MachineFacts(
    string? Os, string? Arch, string? AgentVersion, bool? Container, string? Username, DateTimeOffset? ProcessStartedAt,
    string? Image, long? AcceleratorMemoryBytes, int? AcceleratorDeviceCount, string? AcceleratorBackend,
    string? Rid = null)
{
    /// <summary>Every fact null - a process that never asked, or an adapter with no binary of its own.</summary>
    public static readonly MachineFacts None = new(null, null, null, null, null, null, null, null, null, null);

    /// <summary>
    /// Every fact a running process can read: <see cref="RuntimeInformation.OSDescription"/>
    /// shortened by <see cref="ShortOs"/>, <see cref="RuntimeInformation.OSArchitecture"/>
    /// lowercased, <paramref name="adapter"/>'s own <c>--version</c> read through
    /// <paramref name="probe"/>, whether <see cref="InContainer"/> says this process is
    /// inside one, the OS username, when this process itself started, and
    /// <see cref="ImageFrom"/>'s own answer off the real environment. The accelerator
    /// is read by <see cref="ReadAcceleratorAsync"/> only where <paramref name="localOllama"/>
    /// is true; an Ollama hosted elsewhere makes this machine's accelerator a fact
    /// about the wrong machine, so all three accelerator facts stay null.
    /// </summary>
    public static async Task<MachineFacts> ReadAsync(IAgentAdapter adapter, Probe probe, bool localOllama, CancellationToken ct)
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

        long? acceleratorMemory = null;
        int? acceleratorDevices = null;
        string? acceleratorBackend = null;
        if (localOllama)
        {
            var appleSilicon = OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64;
            (acceleratorMemory, acceleratorDevices, acceleratorBackend) = await ReadAcceleratorAsync(appleSilicon, probe, ct);
        }

        var rid = RidOf(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), OperatingSystem.IsLinux(), RuntimeInformation.OSArchitecture);

        return new MachineFacts(
            os, arch, agentVersion, container, username, startedAt, image,
            acceleratorMemory, acceleratorDevices, acceleratorBackend, rid);
    }

    /// <summary>
    /// The runtime identifier of the build for this platform, one of the four
    /// the board publishes - <c>win-x64</c>, <c>osx-arm64</c>, <c>osx-x64</c>,
    /// <c>linux-x64</c> - else null: ARM Windows and ARM Linux have no build to
    /// name, and null is what says so.
    /// </summary>
    public static string? RidOf(bool windows, bool mac, bool linux, Architecture arch) => (windows, mac, linux, arch) switch
    {
        (true, _, _, Architecture.X64) => "win-x64",
        (_, true, _, Architecture.Arm64) => "osx-arm64",
        (_, true, _, Architecture.X64) => "osx-x64",
        (_, _, true, Architecture.X64) => "linux-x64",
        _ => null,
    };

    private static readonly Regex CardKey = new(@"^card\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The accelerator this machine has, read through <paramref name="probe"/>; the
    /// first branch that answers wins. <paramref name="appleSilicon"/>: memory from
    /// <c>sysctl -n hw.memsize</c> (null where the reading fails), one device,
    /// <c>metal</c>. NVIDIA: <c>nvidia-smi</c>'s MiB per line, summed, <c>cuda</c>.
    /// ROCm: <c>rocm-smi</c>'s <c>card&lt;N&gt;</c> entries' VRAM bytes, summed,
    /// <c>rocm</c>. Else <c>(null, null, "none")</c>. A branch whose output is
    /// missing or not in the shape expected is absent, not an error; a throw other
    /// than cancellation is caught.
    /// </summary>
    public static async Task<(long? Memory, int? Devices, string? Backend)> ReadAcceleratorAsync(
        bool appleSilicon, Probe probe, CancellationToken ct)
    {
        if (appleSilicon)
        {
            var memsize = await ProbeOutputAsync("sysctl", ["-n", "hw.memsize"], probe, ct);
            long? memory = long.TryParse(memsize?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) ? bytes : null;
            return (memory, 1, "metal");
        }

        var nvidia = await ProbeOutputAsync("nvidia-smi", ["--query-gpu=memory.total", "--format=csv,noheader,nounits"], probe, ct);
        if (nvidia is not null)
        {
            var lines = nvidia.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            var mebibytes = lines
                .Select(l => long.TryParse(l, NumberStyles.None, CultureInfo.InvariantCulture, out var mib) ? (long?)mib : null)
                .ToArray();
            if (lines.Length > 0 && mebibytes.All(mib => mib is not null))
                return (mebibytes.Sum(mib => mib!.Value) * 1024 * 1024, lines.Length, "cuda");
        }

        var rocm = await ProbeOutputAsync("rocm-smi", ["--showmeminfo", "vram", "--json"], probe, ct);
        if (rocm is not null && RocmVram(rocm) is { } vram)
            return (vram.Bytes, vram.Cards, "rocm");

        return (null, null, "none");
    }

    /// <summary>
    /// <paramref name="json"/>'s top-level <c>card&lt;N&gt;</c> properties, each one's
    /// <c>VRAM Total Memory (B)</c> summed - null with no such card, or any whose
    /// reading does not parse. Other keys, such as <c>system</c>, are ignored.
    /// </summary>
    private static (long Bytes, int Cards)? RocmVram(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            long total = 0;
            var cards = 0;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!CardKey.IsMatch(property.Name)) continue;
                if (property.Value.ValueKind != JsonValueKind.Object
                    || !property.Value.TryGetProperty("VRAM Total Memory (B)", out var reading)
                    || reading.ValueKind != JsonValueKind.String
                    || !long.TryParse(reading.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var bytes))
                    return null;

                total += bytes;
                cards++;
            }

            return cards == 0 ? null : (total, cards);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// <paramref name="file"/> run with <paramref name="args"/> through <paramref name="probe"/>:
    /// its output where it completed with exit code 0, else null. A throw other than
    /// cancellation is caught and answered as null.
    /// </summary>
    private static async Task<string?> ProbeOutputAsync(string file, string[] args, Probe probe, CancellationToken ct)
    {
        ProbeResult result;
        try
        {
            result = await probe(file, args, Detect.ProbeTimeout, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }

        return result.Completed && result.ExitCode == 0 ? result.Output : null;
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

    /// <summary><paramref name="request"/> with these eleven fields set and every other field left alone.</summary>
    public RunnerHeartbeatRequest Stamp(RunnerHeartbeatRequest request) => request with
    {
        Os = Os,
        Arch = Arch,
        AgentVersion = AgentVersion,
        Container = Container,
        Username = Username,
        ProcessStartedAt = ProcessStartedAt,
        Image = Image,
        AcceleratorMemoryBytes = AcceleratorMemoryBytes,
        AcceleratorDeviceCount = AcceleratorDeviceCount,
        AcceleratorBackend = AcceleratorBackend,
        Rid = Rid,
    };

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }

    private static string? Truncate(string? text) => text is { Length: > 64 } ? text[..64] : text;
}

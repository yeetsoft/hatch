using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Hatch.Cli;

/// <summary>
/// The three checks a downloaded build passes before <see cref="BuildStore"/>
/// ever lets it sit at <c>versions/&lt;sha&gt;</c>: the header the download
/// answered with, the bytes it actually sent, and the build's own say.
/// </summary>
public static class BuildCheck
{
    /// <summary>
    /// Whether the download answered from the build it was asked for. A
    /// mismatch is a board mid-deploy: the list was read at one revision and
    /// the binary came from another.
    /// </summary>
    public static string? Header(string? got, string wanted) =>
        got == wanted ? null : $"hatch: the board answered from another build (mid-deploy?) - asked for {Short(wanted)}";

    /// <summary>Whether what actually arrived hashes to what the board said it would.</summary>
    public static string? Sha(string got, string wanted) =>
        string.Equals(got, wanted, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"hatch: the download's sha256 is not the one the board gave - {Short(wanted)}";

    /// <summary>
    /// Whether the binary, run, says it is the build it claims to be. A
    /// non-zero exit - what a build from before <c>version</c> existed gives,
    /// printing "no such command" - fails this the same as a mismatched
    /// revision does.
    /// </summary>
    public static string? Verdict(string stdout, int exitCode, string revision)
    {
        if (exitCode == 0)
        {
            try
            {
                var report = JsonSerializer.Deserialize(stdout.Trim(), HatchJson.Default.VersionReport);
                if (report?.Revision == revision) return null;
            }
            catch (JsonException) { }
        }

        return $"hatch: the new build did not report revision {Short(revision)}";
    }

    /// <summary>
    /// Runs <c>&lt;binary&gt; version</c> and judges its answer against
    /// <paramref name="revision"/> with <see cref="Verdict"/>.
    /// </summary>
    /// <remarks>
    /// <c>HATCH_NIGHT_STATE</c>, <c>HATCH_LAUNCHER_VERSION</c> and <c>HATCH_LAUNCHER_PATH</c> are
    /// stripped from the child's environment - this run is a check, not an
    /// increment of whatever loop called it, and a child that saw those would
    /// read as the launcher's own supervised process. A start that throws
    /// (<see cref="Win32Exception"/>) is tried once more after 200ms, since a
    /// binary just written to disk can briefly still be busy with its
    /// antivirus scan or its own unpack on Linux.
    /// </remarks>
    public static async Task<string?> RunVersionAsync(string binary, string revision, TimeSpan limit)
    {
        var result = await TryRunAsync(binary, limit);
        if (!result.Started)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            result = await TryRunAsync(binary, limit);
        }

        if (!result.Started) return $"hatch: could not start {binary}";
        if (result.TimedOut) return $"hatch: {binary} did not answer \"version\" within {limit.TotalSeconds:0}s";

        return Verdict(result.Stdout, result.ExitCode, revision);
    }

    private readonly record struct RunResult(bool Started, bool TimedOut, string Stdout, int ExitCode);

    private static async Task<RunResult> TryRunAsync(string binary, TimeSpan limit)
    {
        var start = new ProcessStartInfo
        {
            FileName = binary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("version");

        start.Environment.Remove(Launcher.StateVariable);
        start.Environment.Remove(Launcher.VersionVariable);
        start.Environment.Remove(Launcher.PathVariable);

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException($"could not start {binary}");
        }
        catch (Win32Exception)
        {
            return new RunResult(false, false, "", -1);
        }

        using (process)
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(limit);

            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
                var stderr = process.StandardError.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);
                var output = await stdout;
                await stderr;

                return new RunResult(true, false, output, process.ExitCode);
            }
            catch (OperationCanceledException)
            {
                // The readers are not awaited: a grandchild that outlived the
                // kill can hold a pipe open, and this call must not wait on it.
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return new RunResult(true, true, "", -1);
            }
        }
    }

    /// <summary>The first eight of a sha - enough to tell builds apart in a one-line failure, never the whole thing.</summary>
    private static string Short(string sha) => sha.Length > 8 ? sha[..8] : sha;
}

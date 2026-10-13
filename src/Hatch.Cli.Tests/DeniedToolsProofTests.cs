using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Cli.Tests;

/// <summary>
/// Proof, not unit test: a real <c>claude</c> CLI, spawned the way
/// <see cref="ClaudeSessionRunner"/> spawns one, really refuses the tools
/// named in <c>--disallowedTools</c> under <c>bypassPermissions</c>. See
/// HA-454 and the siblings it was split into, HA-494 and HA-495.
/// </summary>
public sealed class DeniedToolsProofTests(ITestOutputHelper log)
{
    private const string Prompt = """
        Write a file named "proof.txt" in this directory containing the text "hello". Then run this exact shell command: git push --dry-run
        Attempt both steps even if one is refused or fails, and briefly say what happened with each one.
        """;

    [SkippableFact]
    public async Task AKnownRule_IsRefused()
    {
        var bin = FindClaude();
        Skip.IfNot(bin is not null, "no claude CLI reachable - set HATCH_CLAUDE_BIN or install claude on PATH");

        var dir = Directory.CreateTempSubdirectory("hatch-denied-tools-known-");
        try
        {
            var result = await RunClaudeAsync(bin!, dir.FullName, ["Write", "Bash(git push *)"], log);

            Assert.Equal(0, result.ExitCode);

            var refusals = Refusals(result.Stdout);
            Assert.Contains(refusals, r => r.Name == "Write");
            Assert.Contains(refusals, r =>
                r.Name == "Bash" &&
                r.Input.TryGetProperty("command", out var command) &&
                (command.GetString() ?? "").Contains("git push", StringComparison.OrdinalIgnoreCase));

            Assert.False(result.ProofFileExists);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The same two steps as <see cref="ClaudeSessionRunner.TryFind"/>, which is
    /// private - this test re-does them rather than reaching in.
    /// </summary>
    private static string? FindClaude()
    {
        var configured = Environment.GetEnvironmentVariable("HATCH_CLAUDE_BIN");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        return Reach.Find("claude", Environment.GetEnvironmentVariable("PATH"));
    }

    /// <summary>
    /// One real session, spawned directly rather than through
    /// <see cref="ClaudeSessionRunner"/> so this proof stays independent of its
    /// exact wiring. HA-494's unknown-name run shares this verbatim, with only
    /// the <c>--disallowedTools</c> rules and the temp directory differing.
    /// </summary>
    private static async Task<(int ExitCode, List<string> Stdout, List<string> Stderr, bool ProofFileExists)> RunClaudeAsync(
        string bin, string tempDir, IReadOnlyList<string> disallowedToolsArgs, ITestOutputHelper log)
    {
        var start = new ProcessStartInfo
        {
            FileName = bin,
            WorkingDirectory = tempDir,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-p");
        start.ArgumentList.Add("--permission-mode");
        start.ArgumentList.Add("bypassPermissions");
        start.ArgumentList.Add("--max-turns");
        start.ArgumentList.Add("5");
        start.ArgumentList.Add("--output-format");
        start.ArgumentList.Add("stream-json");
        start.ArgumentList.Add("--verbose");
        foreach (var rule in disallowedToolsArgs)
        {
            start.ArgumentList.Add("--disallowedTools");
            start.ArgumentList.Add(rule);
        }

        using var process = new Process { StartInfo = start };
        process.Start();

        var writing = Task.Run(async () =>
        {
            await process.StandardInput.WriteAsync(Prompt);
            process.StandardInput.Close();
        });

        var stdout = new List<string>();
        var reading = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
            {
                stdout.Add(line);
                log.WriteLine($"stdout: {line}");
            }
        });

        var stderr = new List<string>();
        var readingError = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(CancellationToken.None) is { } line)
            {
                stderr.Add(line);
                log.WriteLine($"stderr: {line}");
            }
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Stop(process);
        }

        await Task.WhenAll(writing, reading, readingError).WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await process.WaitForExitAsync(CancellationToken.None);

        var proofFileExists = File.Exists(Path.Combine(tempDir, "proof.txt"));
        return (process.ExitCode, stdout, stderr, proofFileExists);
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or SystemException)
        {
            // It ended between the question and the answer, which is the
            // outcome that was being asked for.
        }
    }

    /// <summary>
    /// A small local version of the matching <see cref="StreamRender.Failures"/>
    /// and <see cref="UsageReport"/> already do twice over - neither answers
    /// "was tool X refused", so this is a one-test helper rather than a third
    /// copy of shared plumbing. A <c>tool_result</c>'s <c>tool_use_id</c> is
    /// resolved back against the <c>tool_use</c> that named it, on the
    /// matching <c>assistant</c> event.
    /// </summary>
    private static List<(string Name, JsonElement Input)> Refusals(IEnumerable<string> lines)
    {
        var toolUses = new Dictionary<string, (string Name, JsonElement Input)>();
        var refusals = new List<(string Name, JsonElement Input)>();

        foreach (var e in Objects(lines))
        {
            var type = Text(e, "type");
            if (type == "assistant")
            {
                foreach (var part in Content(e))
                {
                    if (Text(part, "type") != "tool_use") continue;
                    if (Text(part, "id") is not { } id) continue;

                    var name = Text(part, "name") ?? "";
                    var input = part.TryGetProperty("input", out var i) ? i : default;
                    toolUses[id] = (name, input);
                }
            }
            else if (type == "user")
            {
                foreach (var part in Content(e))
                {
                    if (Text(part, "type") != "tool_result") continue;
                    if (!part.TryGetProperty("is_error", out var failed) || failed.ValueKind != JsonValueKind.True) continue;
                    if (Text(part, "tool_use_id") is not { } toolUseId) continue;
                    if (toolUses.TryGetValue(toolUseId, out var use)) refusals.Add(use);
                }
            }
        }

        return refusals;
    }

    private static IEnumerable<JsonElement> Content(JsonElement e) =>
        e.TryGetProperty("message", out var message) &&
        message.TryGetProperty("content", out var content) &&
        content.ValueKind == JsonValueKind.Array
            ? content.EnumerateArray()
            : [];

    private static IEnumerable<JsonElement> Objects(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.TrimStart();
            if (!line.StartsWith('{')) continue;

            JsonElement e;
            try
            {
                e = JsonDocument.Parse(line).RootElement;
            }
            catch (JsonException)
            {
                continue;
            }

            yield return e;
        }
    }

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

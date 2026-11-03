using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>The claude CLI, behind <see cref="IAgentAdapter"/>.</summary>
public sealed class ClaudeAdapter(
    string? configured = null, Action<string>? warn = null, string? tools = null,
    Func<string, string?>? help = null) : IAgentAdapter, IUsageProbe
{
    /// <summary>
    /// The tools an unattended session is spawned with. Every request a session
    /// makes re-reads its whole context from cache, and Claude Code's full set of
    /// tool definitions is most of what a session starts with before it has done
    /// anything; these are the ones the loop's sessions actually call.
    /// <c>HATCH_CLAUDE_TOOLS</c> widens or replaces them.
    /// </summary>
    public const string DefaultTools = "Bash,Read,Edit,Write,Grep,Glob,Monitor,TaskStop";

    /// <summary>The value of <c>HATCH_CLAUDE_TOOLS</c> that asks for Claude Code's own full set.</summary>
    public const string FullToolSet = "default";

    private const string ToolsFlag = "--tools";

    private const string StableSystemPromptFlag = "--exclude-dynamic-system-prompt-sections";

    /// <summary>How long a probe is given before it is killed the way a lost lease is.</summary>
    private static readonly TimeSpan ProbeCeiling = TimeSpan.FromSeconds(30);

    /// <summary>The binary <see cref="CanSpawn"/> found, and null until it has.</summary>
    private string? _bin;

    /// <summary>
    /// Set the first time a probe comes back having cost a turn - this CLI did
    /// not take <c>/usage</c> as a local command, so nothing here asks it
    /// again for the life of this process. <c>--max-turns 1</c> is what caps
    /// what that one mistake can cost.
    /// </summary>
    private bool _probeCostly;

    /// <summary>Whether the CLI's <c>--help</c> names <c>--tools</c>. False until <see cref="CanSpawn"/> has looked.</summary>
    private bool _knowsTools;

    /// <summary>Whether the CLI's <c>--help</c> names <c>--exclude-dynamic-system-prompt-sections</c>.</summary>
    private bool _knowsStablePrompt;

    public string Id => "claude";

    public string DisplayName => "Claude";

    public IUsageProbe Probe => this;

    public IHookOutputWriter HookOutput { get; } = new ClaudeHookOutputWriter();

    public bool CanSpawn(out string refusal)
    {
        if (_bin is not null)
        {
            refusal = "";
            return true;
        }

        if (!TryFind(configured, out var found, out refusal)) return false;

        _bin = found;
        DetectFlags(found);
        return true;
    }

    /// <summary>
    /// Asks the CLI once which of the spawn flags this adapter adds it knows. An
    /// unknown option fails the spawn, and a spawn that fails every time ends the
    /// night, so a flag the CLI does not list is left off and said so once.
    /// </summary>
    private void DetectFlags(string bin)
    {
        var text = (help ?? RunHelp)(bin);
        _knowsTools = Lists(text, ToolsFlag);
        _knowsStablePrompt = Lists(text, StableSystemPromptFlag);

        var missing = new List<string>();
        if (!_knowsTools) missing.Add(ToolsFlag);
        if (!_knowsStablePrompt) missing.Add(StableSystemPromptFlag);
        if (missing.Count > 0)
            warn?.Invoke($"hatch: this claude CLI does not list {string.Join(" or ", missing)} - spawning without it, so sessions start with a larger context. Update the CLI.");
    }

    /// <summary>Whether <paramref name="flag"/> is an option the help text defines, rather than a word in a description.</summary>
    private static bool Lists(string? help, string flag) =>
        help is not null && Regex.IsMatch(
            help, $@"^\s*(-\w,\s*)?{Regex.Escape(flag)}(?=[\s<\[,]|$)", RegexOptions.Multiline);

    /// <summary>Null when it could not be run, which reads as a CLI that knows neither flag.</summary>
    private static string? RunHelp(string bin)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = bin,
                ArgumentList = { "--help" },
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null) return null;

            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(15)))
            {
                Stop(process);
                return null;
            }

            return output.Wait(TimeSpan.FromSeconds(5)) ? output.Result : null;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// The <c>--tools</c> argument, or null for none: the configured list when
    /// there is one, the default otherwise, and nothing at all when it is the
    /// word <c>default</c>.
    /// </summary>
    private string? ToolsArgument()
    {
        if (string.IsNullOrWhiteSpace(tools)) return DefaultTools;

        var list = tools.Trim();
        return string.Equals(list, FullToolSet, StringComparison.OrdinalIgnoreCase) ? null : list;
    }

    public ProcessStartInfo BuildHeadless(SessionRequest request)
    {
        // bypassPermissions because in print mode nothing can answer a prompt:
        // any permission this did not anticipate becomes a silent denial in the
        // middle of a run nobody is watching. That is a deliberate grant, and
        // the reason `work` is a command an operator types rather than
        // something a cron job does.
        var start = Base(request);
        start.ArgumentList.Add("-p");
        start.ArgumentList.Add("--permission-mode");
        start.ArgumentList.Add("bypassPermissions");

        // Only the tools a session calls, so the starting context is not mostly
        // definitions it never uses - see DefaultTools. Not in Base: an operator
        // is present at BuildAttached and may want any of them.
        if (_knowsTools && ToolsArgument() is { } toolList)
        {
            start.ArgumentList.Add(ToolsFlag);
            start.ArgumentList.Add(toolList);
        }

        // Keeps the system prompt identical from one session to the next, so it
        // is read from cache rather than written again.
        if (_knowsStablePrompt) start.ArgumentList.Add(StableSystemPromptFlag);

        // Read from a file the runner wrote outside the tree, so that wiring the
        // hooks in leaves the checkout exactly as the session changed it.
        if (request.HookSettings is { Length: > 0 } hooks)
        {
            start.ArgumentList.Add("--settings");
            start.ArgumentList.Add(hooks);
        }

        if (request.Quiet)
        {
            start.ArgumentList.Add("--output-format");
            start.ArgumentList.Add("json");
        }
        else
        {
            start.ArgumentList.Add("--output-format");
            start.ArgumentList.Add("stream-json");
            start.ArgumentList.Add("--verbose");
        }

        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;

        return start;
    }

    public ProcessStartInfo BuildAttached(SessionRequest request)
    {
        // No bypassPermissions - there is somebody here to answer a prompt, and
        // the grant the unattended path makes exists only because in print mode
        // there is not.
        //
        // And the prompt is an argument rather than stdin, because stdin is the
        // terminal: it is what the operator is about to type into.
        var start = Base(request);
        start.ArgumentList.Add(request.Prompt);
        return start;
    }

    public IAgentStreamReader CreateReader(string root, RunFacts facts, string model) => new StreamRender(root, facts, ResumeCommand);

    public string? ReadClosing(string json) => StreamRender.Closing(json);

    public string ResumeCommand(string sessionId) => $"claude --resume {sessionId}";

    public IAgentHooks? WriteHooks(string temp, string key, string hatch, string? rtk) => SessionHooks.Write(temp, key, hatch, rtk);

    public UsageLimit.Recognised? RecogniseLimit(string? said, DateTimeOffset endedAt, TimeZoneInfo? runnerZone = null) =>
        UsageLimit.Recognise(said, endedAt, runnerZone);

    public async Task<IReadOnlyList<string>?> ProbeAsync(string tempDirectory, CancellationToken ct)
    {
        if (_probeCostly)
        {
            warn?.Invoke("hatch: probe skipped - /usage was taken as a model turn earlier this run");
            return null;
        }

        using var process = new Process { StartInfo = ProbeStart(tempDirectory) };
        process.Start();

        // Nothing is written to it - the prompt is the `-p` argument - so it
        // is closed at once, which is what saves the seconds the CLI would
        // otherwise spend waiting on a stdin nobody closed.
        process.StandardInput.Close();

        var lines = new List<string>();
        var reading = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
                lines.Add(line);
        }, CancellationToken.None);

        // Its stderr is the runner's console otherwise, under the readout.
        var errors = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(CancellationToken.None) is { } line)
                warn?.Invoke(line);
        }, CancellationToken.None);

        using var timeout = new CancellationTokenSource(ProbeCeiling);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            Stop(process);
        }

        await Task.WhenAll(reading, errors).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await process.WaitForExitAsync(CancellationToken.None);

        warn?.Invoke($"hatch: /usage probe exited {process.ExitCode}, {lines.Count} line(s)");

        if (UsageReport.Turns(lines) > 0)
        {
            _probeCostly = true;
            warn?.Invoke("hatch: the CLI took /usage as a model turn rather than a local command - no further probes this run");
            return null;
        }

        return lines;
    }

    /// <summary>The <c>/usage</c> spawn <see cref="ProbeAsync"/> starts, pinned here so its argv can be read without actually spawning the CLI.</summary>
    internal ProcessStartInfo ProbeStart(string tempDirectory)
    {
        var start = new ProcessStartInfo
        {
            FileName = Bin,
            WorkingDirectory = tempDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-p");
        start.ArgumentList.Add("/usage");
        start.ArgumentList.Add("--output-format");
        start.ArgumentList.Add("stream-json");
        start.ArgumentList.Add("--verbose");
        start.ArgumentList.Add("--max-turns");
        start.ArgumentList.Add("1");
        return start;
    }

    /// <summary>
    /// The binary to spawn, which is only known once <see cref="CanSpawn"/> has
    /// said there is one. A caller that skipped the question is a bug, and
    /// spawning whatever <c>PATH</c> happens to offer would hide it.
    /// </summary>
    private string Bin => _bin
        ?? throw new InvalidOperationException("CanSpawn was never asked, so there is no claude CLI to spawn");

    internal ProcessStartInfo Base(SessionRequest request)
    {
        var start = new ProcessStartInfo
        {
            FileName = Bin,
            WorkingDirectory = request.Root,
            UseShellExecute = false,
        };

        // The prompt this session is about to be handed names `hatch` commands,
        // so the word has to resolve inside it. See Reach. Its calls name the
        // runner that dispatched it, not the person the checkout belongs to.
        Reach.OnPath(start.Environment, Reach.OwnDirectory(Environment.ProcessPath));
        Reach.AsRunner(start.Environment, request.RunnerName);
        Reach.ToHatch(start.Environment, request.Settings);

        start.ArgumentList.Add("--model");
        start.ArgumentList.Add(request.Model);
        start.ArgumentList.Add("--effort");
        start.ArgumentList.Add(request.Effort);
        start.ArgumentList.Add("--add-dir");
        start.ArgumentList.Add(request.Root);
        foreach (var dir in request.AddDirs)
        {
            start.ArgumentList.Add("--add-dir");
            start.ArgumentList.Add(dir);
        }

        foreach (var rule in request.DeniedTools)
        {
            start.ArgumentList.Add("--disallowedTools");
            start.ArgumentList.Add(rule);
        }

        return start;
    }

    /// <summary>
    /// The whole tree, because a session has spawned tools of its own and a
    /// lease that is over should not leave a `make test` running for four more
    /// minutes on a ticket somebody else now holds.
    /// </summary>
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
    /// The CLI that runs the increment.
    /// </summary>
    /// <remarks>
    /// No search of an editor extension's bundle, though a binary does live in
    /// one: it is an implementation detail of a program that updates itself
    /// weekly, and a loop built on that path breaks on somebody else's release
    /// schedule. Install the CLI, or name it once in <c>HATCH_CLAUDE_BIN</c>.
    /// </remarks>
    private static bool TryFind(string? configured, out string bin, out string refusal)
    {
        refusal = "";

        if (!string.IsNullOrWhiteSpace(configured))
        {
            bin = configured;
            if (File.Exists(bin)) return true;

            refusal = $"hatch: HATCH_CLAUDE_BIN is not there: {bin}";
            return false;
        }

        bin = "claude";
        if (Reach.Find("claude", Environment.GetEnvironmentVariable("PATH")) is { } found)
        {
            bin = found;
            return true;
        }

        refusal = string.Join('\n',
            "hatch: no claude CLI on PATH.",
            "",
            "  Install it, or point at one you have:",
            "      export HATCH_CLAUDE_BIN=/path/to/claude",
            "",
            "  The binary inside an editor extension's directory will work, but it moves",
            "  with every update - name it here and expect to rename it, or install the",
            "  standalone CLI and forget about it.");
        return false;
    }
}

/// <summary>Claude's own hook-answer shapes, reproduced from <see cref="InboxCommand"/>.</summary>
internal sealed class ClaudeHookOutputWriter : IHookOutputWriter
{
    public string WriteStop(string text) =>
        JsonSerializer.Serialize(new StopBlock("block", text), HatchJson.Default.StopBlock);

    public string WritePostToolUse(string text) =>
        JsonSerializer.Serialize(
            new PostToolUseOutput(new PostToolUseContext("PostToolUse", text)), HatchJson.Default.PostToolUseOutput);
}

namespace Hatch.Cli;

/// <summary>What to spawn, and where.</summary>
/// <param name="Quiet">
/// No live rendering. The facts then come from the CLI's own summary instead -
/// one object at the end carrying the session id, the cost, and the last thing
/// the session said.
/// </param>
/// <param name="AddDirs">
/// Every other checkout the dispatch's project binds and this runner holds, so
/// a session can reach a sibling repository without leaving <see cref="Root"/>.
/// </param>
/// <param name="HookSettings">
/// A settings file, outside every checkout, declaring the hooks that carry a
/// message sent while the session works into its context. Null where nobody is
/// to be spoken to that way - an attached session has a person at the keyboard.
/// </param>
/// <param name="DeniedTools">
/// The playbook's own limit on what the session may reach for, carried to the
/// spawned process as one <c>--disallowedTools</c> rule per entry. Empty where
/// the playbook denies nothing.
/// </param>
/// <param name="SessionId">
/// A session id the agent is to use, minted by its stream reader before the
/// spawn. Null for an agent that chooses its own (Claude).
/// </param>
/// <param name="RunnerName">
/// The name the board knows the dispatching runner by, exported to the spawned
/// process as <c>HATCH_RUNNER</c> so the session's own <c>hatch</c> calls name
/// themselves as it. Null or empty exports nothing.
/// </param>
public sealed record SessionRequest(
    string Root, string Model, string Effort, string Prompt, bool Quiet,
    IReadOnlyList<string>? AddDirs = null, string? HookSettings = null,
    IReadOnlyList<string>? DeniedTools = null, string? SessionId = null,
    string? RunnerName = null)
{
    public IReadOnlyList<string> AddDirs { get; init; } = AddDirs ?? [];
    public IReadOnlyList<string> DeniedTools { get; init; } = DeniedTools ?? [];
}

/// <param name="Output">Everything the CLI wrote, on a quiet run. Empty on a streamed one, which was rendered as it arrived.</param>
public sealed record SessionResult(int ExitCode, string Output);

/// <summary>
/// The spawn, behind an interface so that everything around it - the claim, the
/// heartbeat, what happens when a lease is lost - can be tested without a CLI,
/// an API key or four minutes of wall clock.
/// </summary>
public interface ISessionRunner
{
    /// <summary>
    /// Runs one increment's session. Cancelling kills it, which is what a
    /// refused heartbeat does.
    /// </summary>
    Task<SessionResult> RunAsync(SessionRequest request, Action<string>? onLine, CancellationToken ct);

    /// <summary>
    /// The same ticket, the same playbook, the same budget, in a session
    /// somebody is sitting in front of - stdio is the terminal's.
    /// </summary>
    Task<int> AttachAsync(SessionRequest request, CancellationToken ct);

    /// <summary>
    /// Whether there is anything to spawn, and the sentence saying why not.
    /// Asked before a claim is taken: an increment that cannot start should not
    /// take a ticket off the board to find that out.
    /// </summary>
    bool CanSpawn(out string refusal);
}

/// <summary>
/// Asked between sessions, of the CLI's own login, rather than out of any
/// session's stream - see <see cref="UsageReport"/> and the decisions on
/// HA-173. Its own interface rather than a third method on <see
/// cref="ISessionRunner"/>, so a fake that never probes is not made to answer
/// a question most tests never ask.
/// </summary>
public interface IUsageProbe
{
    /// <summary>
    /// The probe's raw stream-json lines, for <see cref="UsageReport.Parse"/>
    /// to read - or null where there is nothing to read: the guard has already
    /// switched probing off for this process, or the CLI never answered.
    /// </summary>
    Task<IReadOnlyList<string>?> ProbeAsync(string tempDirectory, CancellationToken ct);
}

/// <summary>
/// Picks the <see cref="ISessionRunner"/> an agent id names - <c>claude</c>
/// behind <see cref="ClaudeAdapter"/>, <c>codex</c> behind
/// <see cref="CodexAdapter"/> and <c>grok</c> behind
/// <see cref="GrokAdapter"/>, each wrapped in the generic
/// <see cref="AgentSessionRunner"/>. This is what makes <c>HATCH_AGENT</c>
/// and <c>--agent</c> refuse an unknown id before anything is spawned.
/// </summary>
public static class Agents
{
    public static bool Resolve(
        string id, string? claudeBin, string? codexBin, string? grokBin, Action<string>? warn,
        out ISessionRunner runner, out IAgentAdapter adapter, out string refusal)
    {
        if (id == "claude")
        {
            adapter = new ClaudeAdapter(claudeBin, warn);
            runner = new AgentSessionRunner(adapter);
            refusal = "";
            return true;
        }

        if (id == "codex")
        {
            adapter = new CodexAdapter(codexBin);
            runner = new AgentSessionRunner(adapter);
            refusal = "";
            return true;
        }

        if (id == "grok")
        {
            adapter = new GrokAdapter(grokBin, warn);
            runner = new AgentSessionRunner(adapter);
            refusal = "";
            return true;
        }

        runner = null!;
        adapter = null!;
        refusal = $"hatch: no such agent \"{id}\" - known: {string.Join(", ", Settings.KnownAgentIds)}";
        return false;
    }
}

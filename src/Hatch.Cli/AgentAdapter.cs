using System.Diagnostics;

namespace Hatch.Cli;

/// <summary>
/// One playbook's own CLI, behind an interface general enough that the runner,
/// the hooks, and the usage probe can all be built without a caller naming
/// which agent it is.
/// </summary>
public interface IAgentAdapter
{
    /// <summary>The playbook's own word for this agent, e.g. "claude".</summary>
    string Id { get; }

    /// <summary>What a person reads where <see cref="Id"/> would read as code.</summary>
    string DisplayName { get; }

    /// <summary>The binary this agent spawns, resolved without side effects, or null where it has none.</summary>
    string? Executable => null;

    /// <summary>
    /// Whether there is anything to spawn, and the sentence saying why not.
    /// Asked before a claim is taken: an increment that cannot start should not
    /// take a ticket off the board to find that out.
    /// </summary>
    bool CanSpawn(out string refusal);

    /// <summary>
    /// Whether this agent can honour every one of a playbook's denied-tools
    /// rules faithfully. False with the sentence naming the first rule it
    /// cannot honour, asked before a claim is taken for the same reason as
    /// <see cref="CanSpawn"/>. The default says every rule can be honoured,
    /// for an adapter - <see cref="ClaudeAdapter"/> - that passes them
    /// straight through.
    /// </summary>
    bool CanDeny(IReadOnlyList<string> rules, out string refusal)
    {
        refusal = "";
        return true;
    }

    /// <summary>
    /// Whether this agent can serve a row pinned to <paramref name="model"/> -
    /// a pinned id or a mapped alias, as <see cref="QueueEntryDto.Model"/>
    /// carries it. False with the sentence naming what is missing, asked
    /// before a claim is taken for the same reason as <see cref="CanSpawn"/>
    /// and <see cref="CanDeny"/>. The default says yes, for every adapter with
    /// no server-side model list to miss.
    /// </summary>
    bool CanServe(string? model, out string refusal)
    {
        refusal = "";
        return true;
    }

    /// <summary>
    /// What this agent can spawn, as the beat advertises it: the aliases unless
    /// the adapter knows better. Empty where it can name none.
    /// </summary>
    IReadOnlyList<string> Models => ModelFamilies.Aliases;

    /// <summary>
    /// The model a health check spawns: the cheapest this agent offers. The
    /// default is the first of <see cref="Models"/>, which is how every adapter
    /// lists its aliases.
    /// </summary>
    string CheapestModel => Models.FirstOrDefault() ?? "";

    /// <summary>The unattended spawn - stdout read by a caller, stdin closed once the prompt is written.</summary>
    ProcessStartInfo BuildHeadless(SessionRequest request);

    /// <summary>The attached spawn - stdio left to whoever is at the keyboard.</summary>
    ProcessStartInfo BuildAttached(SessionRequest request);

    /// <summary>
    /// Whether the unattended spawn reads its prompt on stdin. False where the
    /// adapter delivers it itself, as a file or an argument - the runner then
    /// writes nothing and closes stdin at once.
    /// </summary>
    bool PromptOnStdin => true;

    /// <summary>
    /// Called once per spawn after the process is gone - success, failure or
    /// cancellation - so an adapter can delete what <see cref="BuildHeadless"/>
    /// wrote. Must not throw.
    /// </summary>
    void Release(SessionRequest request) { }

    /// <summary>
    /// The file <see cref="BuildHeadless"/> told this agent's CLI to write its
    /// final answer to, or null where it asked for none - recomputed from
    /// <paramref name="request"/> alone, since the path follows from the same
    /// inputs the build had. Read back once the run ends, and preferred over the
    /// stream's own last text where the two disagree: a stream cut off mid-render
    /// can end before the model's true last word.
    /// </summary>
    string? OutputLastMessagePath(SessionRequest request) => null;

    /// <summary>
    /// Whether this agent has a door for a hooks file outside the checkout at
    /// all. False is not a failure to say anything about; it is distinct from
    /// <see cref="WriteHooks"/> returning null because a write failed.
    /// </summary>
    bool CanHook => true;

    /// <summary>
    /// The reader that turns this agent's own stream into lines and the facts a
    /// caller needs out of it. <paramref name="model"/> is the one the session
    /// runs, for an agent whose stream never names it.
    /// </summary>
    IAgentStreamReader CreateReader(string root, RunFacts facts, string model);

    /// <summary>
    /// The longest one foreground tool call runs in this agent before the agent
    /// itself ends it. A call still open past this is treated as hung, and the
    /// runner stops saying it is still running - see <see cref="IAgentStreamReader.Open"/> -
    /// unless the claim carries a run cap, in which case the runner keeps
    /// saying it until that cap stops the session; this bounds the call only
    /// where there is none. Ten minutes is the upper bound used for every
    /// agent: one with a shorter timeout closes the call sooner anyway.
    /// </summary>
    TimeSpan ToolCeiling => TimeSpan.FromMinutes(10);

    /// <summary>The prose a quiet run ended with, out of the one object it answers with.</summary>
    string? ReadClosing(string json);

    /// <summary>What rejoins a session by its id, the way a person would type it.</summary>
    string ResumeCommand(string sessionId);

    /// <summary>
    /// The hooks that carry a message sent while a session works into its
    /// context, or null where none could be written - a session without them
    /// still works its ticket, a message sent mid-run waits for the next one.
    /// </summary>
    IAgentHooks? WriteHooks(string temp, string key, string hatch, string? rtk);

    /// <summary>The shape a hook call answers with, for this agent.</summary>
    IHookOutputWriter HookOutput { get; }

    /// <summary>Whether a session ended on this agent's own usage limit, read out of what it said rather than an API.</summary>
    UsageLimit.Recognised? RecogniseLimit(string? said, DateTimeOffset endedAt, TimeZoneInfo? runnerZone = null);

    /// <summary>Whether a session ended because this agent could not spawn the model it was asked for, read out of what it said.</summary>
    ModelRefusal.Recognised? RecogniseModelRefusal(string? said, DateTimeOffset endedAt, TimeZoneInfo? runnerZone = null) => null;

    /// <summary>Whether a session ended because this agent's account is signed out, read out of what it said.</summary>
    AuthFailure.Recognised? RecogniseAuthFailure(string? said) => null;

    /// <summary>Asked between sessions for the account's own usage windows, or null where this agent has none to ask.</summary>
    IUsageProbe? Probe { get; }
}

/// <summary>One session's stream, read back as terminal lines and the facts a caller needs out of it.</summary>
public interface IAgentStreamReader
{
    /// <summary>The lines one event turns into, in order. Empty for an event that draws nothing.</summary>
    IEnumerable<string> Read(string raw);

    /// <summary>The four counts added up, over every message seen so far in this session.</summary>
    long TokensSoFar { get; }

    /// <summary>One request per distinct message seen so far.</summary>
    int Requests { get; }

    /// <summary>The largest context carried by any one message so far.</summary>
    long PeakContextTokens { get; }

    /// <summary>What the session read, by path or endpoint - the heaviest <see cref="ReadsLedger.Max"/>, never free text.</summary>
    IReadOnlyList<WorkLogReadDto> Reads { get; }

    /// <summary>The account's usage windows, out of the stream's own report - empty before the first one arrives.</summary>
    IReadOnlyList<UsageWindow> Usage { get; }

    /// <summary>When <see cref="Usage"/> was last taken.</summary>
    DateTimeOffset? UsageReadAt { get; }

    /// <summary>
    /// The most recently started tool call that has not returned yet, or null
    /// when none is open. Updated before <see cref="Read"/> returns, whether or
    /// not the caller enumerates its lines.
    /// </summary>
    OpenCall? Open { get; }
}

/// <summary>
/// A tool call in flight. <paramref name="Line"/> is the call as the transcript
/// names it, without the leading marker (<c>Bash  make test-api</c>) - never
/// its output.
/// </summary>
public sealed record OpenCall(string Id, string Line);

/// <summary>
/// The calls a reader has seen start and not yet seen return, in the order they
/// started. Parallel calls are normal, so this is a list and not a slot.
/// </summary>
internal sealed class OpenCalls
{
    private readonly List<OpenCall> _calls = [];

    /// <summary>The newest call still open, or null.</summary>
    public OpenCall? Current => _calls.Count > 0 ? _calls[^1] : null;

    /// <summary>A call started. One already open under the same id is left as it is.</summary>
    public void Begin(string? id, string line)
    {
        if (string.IsNullOrEmpty(id) || _calls.Any(c => c.Id == id)) return;
        _calls.Add(new OpenCall(id, line));
    }

    /// <summary>A call returned, whether or not it failed.</summary>
    public void End(string? id)
    {
        if (id is null) return;
        _calls.RemoveAll(c => c.Id == id);
    }

    /// <summary>The session's end: nothing is in flight any more.</summary>
    public void Clear() => _calls.Clear();
}

/// <summary>The hooks a session was handed, and the directory outside every checkout that they live in.</summary>
public interface IAgentHooks : IDisposable
{
    /// <summary>The file whose modification time is when a per-step check last asked.</summary>
    string Stamp { get; }

    /// <summary>The fact record written once a session's tokens cross its playbook's budget.</summary>
    string Clamp { get; }

    /// <summary>The playbook's budget, in tokens, written once before a session starts.</summary>
    string Budget { get; }

    /// <summary>Touched the first time a session is told its budget was crossed, and never reset.</summary>
    string ClampDelivered { get; }

    /// <summary>
    /// Whatever this agent's own spawn needs back out of the hooks it was
    /// handed - a settings-file path for Claude's, null where an agent needs
    /// nothing more.
    /// </summary>
    string? Opaque { get; }
}

/// <summary>The shape a hook call answers with, so a session already mid-step reads what is waiting.</summary>
public interface IHookOutputWriter
{
    /// <summary>What a <c>Stop</c> hook prints to keep a session going long enough to read <paramref name="text"/>.</summary>
    string WriteStop(string text);

    /// <summary>What a <c>PostToolUse</c> hook prints to put <paramref name="text"/> in front of the model at its next step.</summary>
    string WritePostToolUse(string text);
}

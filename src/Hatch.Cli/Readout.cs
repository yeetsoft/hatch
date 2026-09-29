namespace Hatch.Cli;

/// <summary>What the readout's first two rows say about the increment in progress, or null between them.</summary>
public sealed record IncrementSnapshot(
    string Key, string Title, string What, string? IssueUrl,
    DateTimeOffset StartedAt, long TokensSoFar, DateTimeOffset LastActivityAt, string? LastTool);

/// <summary>What the readout says between increments, or null while one is running.</summary>
public sealed record IdleSnapshot(string Line, DateTimeOffset? NextLookAt);

/// <summary>
/// One usage window - the session's own, the weekly one, or a model-scoped
/// weekly one where the source reports one. See HA-124: nothing feeds this yet,
/// and an empty list draws no rows and complains about nothing, which is the
/// point.
/// </summary>
public sealed record UsageWindow(string Label, double Utilization, DateTimeOffset? ResetsAt);

/// <summary>Whose runner this is, and how its night is going.</summary>
public sealed record RunnerSnapshot(
    string RunnerName, string? ForName, int NightRuns, TimeSpan NightElapsed, decimal NightSpent, string? Bound)
{
    public static readonly RunnerSnapshot Empty = new("", null, 0, TimeSpan.Zero, 0m, null);
}

/// <summary>Everything the readout draws, at one instant.</summary>
public sealed record ReadoutSnapshot(
    IncrementSnapshot? Increment, IdleSnapshot? Idle, RunnerSnapshot Runner, IReadOnlyList<UsageWindow> UsageWindows);

/// <summary>
/// The one object a live increment and the idle loop between them both write
/// to, and the one the terminal's clock reads from on its own - so the footer
/// never has to ask which of the two states the loop is currently in.
/// </summary>
/// <remarks>
/// Always constructed, whether or not anything is watching it: writing a few
/// fields under a lock on every rendered line costs nothing worth branching
/// around, and a <see cref="Readout"/> that had to ask "is anyone drawing me"
/// would be a second source of truth about that question.
/// </remarks>
public sealed class ReadoutState
{
    private readonly Lock _gate = new();
    private IncrementSnapshot? _increment;
    private IdleSnapshot? _idle;
    private RunnerSnapshot _runner = RunnerSnapshot.Empty;
    private IReadOnlyList<UsageWindow> _usage = [];

    /// <summary>A session is about to be spawned. Clears whatever idle line was showing.</summary>
    public void BeginIncrement(string key, string title, string what, string? issueUrl, DateTimeOffset startedAt)
    {
        lock (_gate)
        {
            _increment = new IncrementSnapshot(key, title, what, issueUrl, startedAt, 0, startedAt, null);
            _idle = null;
        }
    }

    /// <summary>A line arrived. Read only while an increment is showing - a stray one after it ends is dropped.</summary>
    public void Activity(long tokensSoFar, DateTimeOffset at, string? lastTool)
    {
        lock (_gate)
        {
            if (_increment is not { } inc) return;
            _increment = inc with { TokensSoFar = tokensSoFar, LastActivityAt = at, LastTool = lastTool ?? inc.LastTool };
        }
    }

    /// <summary>The increment is over, whatever it came to. Nothing is left showing until the next one begins.</summary>
    public void EndIncrement()
    {
        lock (_gate) _increment = null;
    }

    /// <summary>Between increments: why, and when the loop looks again.</summary>
    public void SetIdle(string line, DateTimeOffset? nextLookAt)
    {
        lock (_gate) _idle = new IdleSnapshot(line, nextLookAt);
    }

    /// <summary>Whose runner this is and how its night is going, refreshed on every beat.</summary>
    public void SetRunner(RunnerSnapshot runner)
    {
        lock (_gate) _runner = runner;
    }

    /// <summary>The account's usage windows, refreshed while a session is running - see HA-124.</summary>
    public void SetUsage(IReadOnlyList<UsageWindow> windows)
    {
        lock (_gate) _usage = windows;
    }

    public ReadoutSnapshot Snapshot()
    {
        lock (_gate) return new ReadoutSnapshot(_increment, _idle, _runner, _usage);
    }
}

/// <summary>
/// What the readout says, as of one instant - a pure function of a snapshot and
/// the clock, so the drawing terminal that calls it stays thin. See
/// <c>docs/hatch.md</c>, "The console".
/// </summary>
public static class Readout
{
    private const int BarWidth = 20;
    private const string Green = "\x1b[32m";
    private const string Yellow = "\x1b[33m";
    private const string Red = "\x1b[31m";
    private const string Reset = "\x1b[0m";

    /// <summary>How long a quiet increment reads as the warn colour, then the danger one.</summary>
    private static readonly TimeSpan Warn = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Danger = TimeSpan.FromMinutes(10);

    /// <summary>Every row, in the order the report asked for, each already clipped to <paramref name="width"/>.</summary>
    public static IReadOnlyList<string> Draw(ReadoutSnapshot snapshot, DateTimeOffset now, int width, bool color)
    {
        var rows = new List<string>();

        if (snapshot.Increment is { } inc)
        {
            rows.Add(Clip(IssueRow(inc), width));
            rows.Add(AliveRow(inc, now, width, color));
        }

        foreach (var window in snapshot.UsageWindows) rows.Add(Clip(UsageRow(window, now, color), width));

        rows.Add(Clip(RunnerRow(snapshot.Runner), width));

        if (snapshot.Idle is { } idle) rows.Add(Clip(IdleRow(idle, now), width));

        return rows;
    }

    private static string IssueRow(IncrementSnapshot inc)
    {
        var url = inc.IssueUrl is { Length: > 0 } u ? $"  {u}" : "";
        return $"{inc.Key}  {inc.Title}  {inc.What}{url}";
    }

    /// <summary>
    /// Whether the agent is alive: elapsed time, tokens, and how long it has
    /// been quiet - coloured, past the two thresholds the report asks for.
    /// Coloured after clipping, not before: the escape codes are zero-width and
    /// clipping the plain text first is what keeps a narrow terminal from ever
    /// cutting one in half.
    /// </summary>
    private static string AliveRow(IncrementSnapshot inc, DateTimeOffset now, int width, bool color)
    {
        var elapsed = Format.Duration((long)Math.Max(0, (now - inc.StartedAt).TotalSeconds));
        var tokens = Format.Compact(inc.TokensSoFar);
        var quietFor = now - inc.LastActivityAt;
        var quiet = "quiet " + Format.Duration((long)Math.Max(0, quietFor.TotalSeconds))
            + (inc.LastTool is { Length: > 0 } tool ? $" — {tool}" : "");

        var row = Clip($"{elapsed} elapsed, {tokens} tokens, {quiet}", width);
        if (!color || !row.Contains(quiet, StringComparison.Ordinal)) return row;

        var tone = quietFor >= Danger ? Red : quietFor >= Warn ? Yellow : null;
        return tone is null ? row : row.Replace(quiet, tone + quiet + Reset, StringComparison.Ordinal);
    }

    private static string UsageRow(UsageWindow window, DateTimeOffset now, bool color)
    {
        var filled = (int)Math.Round(Math.Clamp(window.Utilization, 0, 1) * BarWidth);
        var bar = new string('█', filled) + new string('░', BarWidth - filled);
        if (color)
        {
            var tone = window.Utilization >= 0.9 ? Red : window.Utilization >= 0.7 ? Yellow : Green;
            bar = tone + bar + Reset;
        }

        var pct = $"{Math.Round(window.Utilization * 100)}%";
        var resets = window.ResetsAt is { } at
            ? $"resets in {Format.Duration(Math.Max(0, (long)(at - now).TotalSeconds))}"
            : "no reset given";

        return $"{window.Label,-16} [{bar}] {pct,4}  {resets}";
    }

    private static string RunnerRow(RunnerSnapshot runner)
    {
        var who = runner.RunnerName.Length == 0 ? "" : runner.ForName is { Length: > 0 } f ? $"{runner.RunnerName} for {f}" : runner.RunnerName;
        var night = $"{runner.NightRuns} increment(s) in {Format.Duration((long)runner.NightElapsed.TotalSeconds)}, {Format.Spent(runner.NightSpent)} spent";
        var bound = runner.Bound is { Length: > 0 } b ? $", stops at {b}" : "";
        return who.Length == 0 ? $"{night}{bound}" : $"{who} — {night}{bound}";
    }

    private static string IdleRow(IdleSnapshot idle, DateTimeOffset now)
    {
        var next = idle.NextLookAt is { } at
            ? $" — looking again in {Format.Duration(Math.Max(0, (long)(at - now).TotalSeconds))}"
            : "";
        return $"{idle.Line}{next}";
    }

    private static string Clip(string text, int width) => width > 0 && text.Length > width ? text[..width] : text;

    /// <summary>
    /// The closing banner's usage line: one window per label, compact - or
    /// null where there is no reading, which a quiet run and one that ended
    /// before the CLI's first <c>rate_limit_event</c> both are.
    /// </summary>
    public static string? OneLine(IReadOnlyList<UsageWindow> windows) =>
        windows.Count == 0
            ? null
            : string.Join(", ", windows.Select(w => $"{w.Label} {Math.Round(w.Utilization * 100)}%"));
}

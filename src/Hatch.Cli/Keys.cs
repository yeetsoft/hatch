namespace Hatch.Cli;

/// <summary>What question is outstanding, if any - see <see cref="Keys.Decide"/>.</summary>
public enum Confirming
{
    None,
    Cancel,
    Skip,
}

/// <summary>
/// What <see cref="Readout.Draw"/> and <see cref="Tally.ShouldStop"/> read -
/// <see cref="KeysOn"/> is what tells the readout whether to draw the legend
/// row at all.
/// </summary>
public sealed record ControlsSnapshot(bool KeysOn, bool StopArmed, bool Paused, bool LongLegend, Confirming Confirming);

/// <summary>
/// The pure decision table a key press is run through - no lock, no <see
/// cref="Console"/>, no <see cref="Terminal"/>, so it is reachable with
/// nothing standing in front of it. See <c>docs/hatch.md</c>, "The console".
/// </summary>
public static class Keys
{
    public static (bool StopArmed, bool Paused, bool LongLegend, Confirming Confirming, bool CancelNow, bool SkipNow, string? Log) Decide(
        char key, bool stopArmed, bool paused, bool longLegend, Confirming confirming, bool incrementRunning)
    {
        if (confirming == Confirming.Cancel)
        {
            return key is 'y' or 'Y'
                ? (stopArmed, paused, longLegend, Confirming.None, CancelNow: true, SkipNow: false, Log: "cancel now — confirmed (c, y)")
                : (stopArmed, paused, longLegend, Confirming.None, CancelNow: false, SkipNow: false, Log: null);
        }

        if (confirming == Confirming.Skip)
        {
            return key is 'y' or 'Y'
                ? (stopArmed, paused, longLegend, Confirming.None, CancelNow: false, SkipNow: true, Log: "skip confirmed — this increment ends now (k, y)")
                : (stopArmed, paused, longLegend, Confirming.None, CancelNow: false, SkipNow: false, Log: null);
        }

        if (key is 's' or 'S')
        {
            var armed = !stopArmed;
            return (armed, paused, longLegend, Confirming.None, CancelNow: false, SkipNow: false,
                Log: armed
                    ? "stop armed — the night ends after this increment (s to undo)"
                    : "stop undone — the night carries on (s)");
        }

        if (key is 'c' or 'C') return (stopArmed, paused, longLegend, Confirming.Cancel, CancelNow: false, SkipNow: false, Log: null);

        if (key is 'p' or 'P')
        {
            var armed = !paused;
            return (stopArmed, armed, longLegend, confirming, CancelNow: false, SkipNow: false,
                Log: armed
                    ? "paused from the keyboard (p to resume)"
                    : "resumed from the keyboard (p to pause)");
        }

        if ((key is 'k' or 'K') && incrementRunning)
            return (stopArmed, paused, longLegend, Confirming.Skip, CancelNow: false, SkipNow: false, Log: null);

        if (key == '?')
            return (stopArmed, paused, !longLegend, confirming, CancelNow: false, SkipNow: false, Log: null);

        return (stopArmed, paused, longLegend, confirming, CancelNow: false, SkipNow: false, Log: null);
    }
}

/// <summary>
/// What a night's keyboard has decided so far - copying <see
/// cref="ReadoutState"/>'s shape exactly, for the same reason: always
/// constructed, whether or not anything is reading it, with a lock around a
/// handful of fields nobody has to ask "is anyone listening" to write to.
/// </summary>
public sealed class Controls(bool keysOn)
{
    private readonly Lock _gate = new();
    private bool _stopArmed;
    private bool _paused;
    private bool _longLegend;
    private Confirming _confirming = Confirming.None;

    /// <summary>
    /// Whether an increment is currently running - read only by <see
    /// cref="Press"/>, to gate <c>k</c>, and written by <see
    /// cref="Increment.RunAsync"/> the same moment <see
    /// cref="ReadoutState.BeginIncrement"/>/<see cref="ReadoutState.EndIncrement"/>
    /// bracket it. Not part of <see cref="ControlsSnapshot"/> - nothing outside
    /// this class reads it.
    /// </summary>
    private bool _incrementRunning;

    /// <summary>Set once at construction and never mutated after - readable with no lock.</summary>
    public bool KeysOn => keysOn;

    /// <summary>
    /// Raised by <see cref="Press"/>, after its lock is released, the moment a
    /// skip is confirmed - so a subscriber calling back into this instance
    /// cannot deadlock. <see cref="GoToWork.PassAsync"/> and <see
    /// cref="WorkCommand"/>'s own spend each subscribe for the duration of
    /// their own <see cref="Increment.RunAsync"/> call.
    /// </summary>
    public event Action? Skipped;

    public ControlsSnapshot Snapshot()
    {
        lock (_gate) return new ControlsSnapshot(keysOn, _stopArmed, _paused, _longLegend, _confirming);
    }

    /// <summary>Written the same moment an increment begins and ends - see <see cref="_incrementRunning"/>.</summary>
    public void SetIncrementRunning(bool running)
    {
        lock (_gate) _incrementRunning = running;
    }

    /// <summary>
    /// The one place <see cref="Keys.Decide"/>'s output ever lands in the
    /// mutable state - the table, run under the lock, with the result written
    /// back before it returns.
    /// </summary>
    public (string? Log, bool CancelNow, bool SkipNow) Press(char key)
    {
        string? log;
        bool cancelNow;
        bool skipNow;

        lock (_gate)
        {
            var (stopArmed, paused, longLegend, confirming, cancel, skip, decided) =
                Keys.Decide(key, _stopArmed, _paused, _longLegend, _confirming, _incrementRunning);
            _stopArmed = stopArmed;
            _paused = paused;
            _longLegend = longLegend;
            _confirming = confirming;
            cancelNow = cancel;
            skipNow = skip;
            log = decided;
        }

        if (skipNow) Skipped?.Invoke();
        return (log, cancelNow, skipNow);
    }
}

/// <summary>
/// The background thread that reads this process's own standard input, a key
/// at a time, for as long as a night runs.
/// </summary>
/// <remarks>
/// <see cref="Stop"/> cannot interrupt a blocked <see
/// cref="Console.ReadKey(bool)"/> - the story's own decisions already accept
/// that cost - so the thread runs as a background one: that is what stops a
/// blocked read from holding the process open past the night.
/// </remarks>
public sealed class KeyReader : IDisposable
{
    private readonly Controls _controls;
    private readonly Terminal _say;
    private readonly CancellationTokenSource _cancelling;
    private readonly Thread _thread;
    private volatile bool _stopped;

    public KeyReader(Controls controls, Terminal say, CancellationTokenSource cancelling)
    {
        _controls = controls;
        _say = say;
        _cancelling = cancelling;
        _thread = new Thread(Run) { IsBackground = true };
        _thread.Start();
    }

    private void Run()
    {
        // A console that went away - the same reasoning Terminal.Line's own
        // try/catch already gives - ends this thread rather than throwing on
        // one nobody is awaiting.
        try
        {
            while (!_stopped)
            {
                var key = Console.ReadKey(intercept: true);
                var (log, cancelNow, _) = _controls.Press(key.KeyChar);
                if (log is { Length: > 0 }) _say.Line($"hatch: {log}");
                if (cancelNow) _cancelling.Cancel();
            }
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Stop() => _stopped = true;

    public void Dispose() => Stop();
}

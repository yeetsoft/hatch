namespace Hatch.Cli;

/// <summary>What question is outstanding, if any - see <see cref="Keys.Decide"/>.</summary>
public enum Confirming
{
    None,
    Cancel,
}

/// <summary>
/// What <see cref="Readout.Draw"/> and <see cref="Tally.ShouldStop"/> read -
/// <see cref="KeysOn"/> is what tells the readout whether to draw the legend
/// row at all.
/// </summary>
public sealed record ControlsSnapshot(bool KeysOn, bool StopArmed, Confirming Confirming);

/// <summary>
/// The pure decision table a key press is run through - no lock, no <see
/// cref="Console"/>, no <see cref="Terminal"/>, so it is reachable with
/// nothing standing in front of it. See <c>docs/hatch.md</c>, "The console".
/// </summary>
public static class Keys
{
    public static (bool StopArmed, Confirming Confirming, bool CancelNow, string? Log) Decide(
        char key, bool stopArmed, Confirming confirming)
    {
        if (confirming == Confirming.Cancel)
        {
            return key is 'y' or 'Y'
                ? (stopArmed, Confirming.None, CancelNow: true, Log: "cancel now — confirmed (c, y)")
                : (stopArmed, Confirming.None, CancelNow: false, Log: null);
        }

        if (key is 's' or 'S')
        {
            var armed = !stopArmed;
            return (armed, Confirming.None, CancelNow: false,
                Log: armed
                    ? "stop armed — the night ends after this increment (s to undo)"
                    : "stop undone — the night carries on (s)");
        }

        if (key is 'c' or 'C') return (stopArmed, Confirming.Cancel, CancelNow: false, Log: null);

        return (stopArmed, confirming, CancelNow: false, Log: null);
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
    private Confirming _confirming = Confirming.None;

    /// <summary>Set once at construction and never mutated after - readable with no lock.</summary>
    public bool KeysOn => keysOn;

    public ControlsSnapshot Snapshot()
    {
        lock (_gate) return new ControlsSnapshot(keysOn, _stopArmed, _confirming);
    }

    /// <summary>
    /// The one place <see cref="Keys.Decide"/>'s output ever lands in the
    /// mutable state - the table, run under the lock, with the result written
    /// back before it returns.
    /// </summary>
    public (string? Log, bool CancelNow) Press(char key)
    {
        lock (_gate)
        {
            var (stopArmed, confirming, cancelNow, log) = Keys.Decide(key, _stopArmed, _confirming);
            _stopArmed = stopArmed;
            _confirming = confirming;
            return (log, cancelNow);
        }
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
                var (log, cancelNow) = _controls.Press(key.KeyChar);
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

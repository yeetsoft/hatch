using System.Text;

namespace Hatch.Cli;

/// <summary>
/// Where the runner says things.
/// </summary>
/// <remarks>
/// An object rather than <c>Console</c> directly, for two reasons that are the
/// same reason. A test needs to read back what a pass printed - "the board is
/// busy" and "the board is empty" are different sentences and telling them apart
/// is an acceptance criterion - and the streamed lines of a session arrive on a
/// reader thread while the main one is writing its own, so somewhere has to hold
/// the lock that stops two lines landing on top of each other.
/// </remarks>
public class Terminal
{
    private readonly Lock _gate = new();

    /// <summary>What a person reads. Standard output.</summary>
    public virtual void Line(string line)
    {
        // A closed terminal - the window went away, or the pipe on the other
        // end did - is not a reason for an unattended loop to go down with the
        // claim still held. There is nobody left to read this line either way.
        try
        {
            lock (_gate) Console.Out.WriteLine(line);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>What went wrong. Standard error, so a redirected log keeps the two apart.</summary>
    public virtual void Complain(string line)
    {
        try
        {
            lock (_gate) Console.Error.WriteLine(line);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Several of them, in order.</summary>
    public void Lines(IEnumerable<string> lines)
    {
        foreach (var line in lines) Line(line);
    }
}

/// <summary>
/// The bottom of the terminal, pinned: the readout is erased and redrawn
/// around every line, the way a build tool draws a progress bar, so the
/// session's stream scrolls above it and never overwrites it.
/// </summary>
/// <remarks>
/// <para>Pinned to the bottom rather than the top because that needs no
/// terminal mode at all - see the decisions on HA-120. A scroll region left set
/// by a runner killed outright would leave a terminal that scrolls wrongly
/// until somebody types <c>reset</c>; erasing and redrawing a footer leaves at
/// most one stale copy in the scrollback, and nothing to repair.</para>
///
/// <para>One lock, shared with the base class's own writes by not calling into
/// them: every line - the session's, the runner's own, and the footer's
/// redraw - goes out from in here, under <see cref="_gate"/>, so a tick from
/// the clock can never land in the middle of a streamed line.</para>
/// </remarks>
public sealed class LiveTerminal : Terminal, IDisposable
{
    private readonly Lock _gate = new();
    private readonly ReadoutState _state;
    private readonly Controls? _controls;
    private readonly TimeProvider _clock;
    private readonly bool _color;
    private readonly TextWriter? _stdout;
    private readonly Timer _timer;
    private int _footerLines;
    private string _lastRows = "";
    private bool _stopped;

    /// <param name="tickEvery">
    /// How often the clock redraws on its own, with nothing printed - twice a
    /// second by default, fast enough that a clock reads as ticking (criterion
    /// 13 asks for at least once a second) and slow enough that redrawing a
    /// handful of lines is not something anybody could call load. A test
    /// gives it a tick nothing it does could ever wait for, so a slow machine
    /// scheduling this thread late can never fire the timer mid-assertion and
    /// write to whatever <see cref="Console.Out"/> happens to be by then.
    /// </param>
    /// <param name="controls">
    /// What the keyboard has decided, read fresh on every draw - null where
    /// nothing reads keys for this run, which draws the footer exactly as
    /// before with no legend row.
    /// </param>
    /// <param name="stdout">
    /// Where the footer and the lines printed around it go, flushed once per
    /// frame - null reads <see cref="Console.Out"/> afresh at every write. A
    /// writer given here needs a buffer larger than any frame: the console's
    /// own writer hands the terminal 256 characters at a time, and a coloured
    /// footer is well past that.
    /// </param>
    public LiveTerminal(
        ReadoutState state, TimeProvider clock, bool color, TimeSpan? tickEvery = null, Controls? controls = null,
        TextWriter? stdout = null)
    {
        _state = state;
        _clock = clock;
        _color = color;
        _controls = controls;
        _stdout = stdout;

        var every = tickEvery ?? TimeSpan.FromMilliseconds(500);
        _timer = new Timer(_ => Redraw(), null, every, every);
    }

    public override void Line(string line)
    {
        // A closed terminal is not a reason for an unattended loop to go down
        // with the claim still held - see the base class's own guard, which a
        // direct write here bypasses.
        try
        {
            lock (_gate)
            {
                if (_stopped) { Console.Out.WriteLine(line); return; }

                WriteFrame(Frame(line));
            }
        }
        catch (IOException)
        {
        }
    }

    public override void Complain(string line)
    {
        try
        {
            lock (_gate)
            {
                if (_stopped) { Console.Error.WriteLine(line); return; }

                // Three writes, because the line belongs to the other stream:
                // the erase lands before it and the redraw after it.
                WriteFrame(Erase());
                Console.Error.WriteLine(line);
                WriteFrame(Frame(null));
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// The clock's redraw - run by the timer, and by a test that does not want
    /// to wait on it. Writes nothing when the rows are what is already on
    /// screen, so a tick where nothing changed costs the terminal nothing.
    /// </summary>
    internal void Redraw()
    {
        // Runs on a timer thread - an exception left to escape a timer
        // callback takes the whole process down, claim and all.
        try
        {
            lock (_gate)
            {
                if (_stopped) return;

                var rows = Rows();
                if (_footerLines > 0 && string.Join("\n", rows) == _lastRows) return;

                WriteFrame(Frame(null, rows));
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// The erase, the line being printed if there is one, and the footer
    /// redrawn - all of it one string, so it can leave in one write.
    /// </summary>
    private string Frame(string? line, IReadOnlyList<string>? rows = null)
    {
        rows ??= Rows();
        var frame = new StringBuilder(Erase());
        if (line is not null) frame.Append(line).Append('\n');
        foreach (var row in rows) frame.Append(row).Append('\n');

        _footerLines = rows.Count;
        _lastRows = string.Join("\n", rows);
        return frame.ToString();
    }

    private IReadOnlyList<string> Rows() =>
        Readout.Draw(_state.Snapshot(_controls?.Snapshot()), _clock.GetUtcNow(), SafeWidth(), _color);

    /// <summary>
    /// <c>CPL</c> to the first footer row, then <c>ED 0J</c> to clear from
    /// there to the end of the display - both standard VT100, and both
    /// something a terminal that took the footer in the first place already
    /// understands. Empty where no footer is on screen.
    /// </summary>
    private string Erase()
    {
        if (_footerLines == 0) return "";

        var erase = $"\x1b[{_footerLines}F\x1b[0J";
        _footerLines = 0;
        _lastRows = "";
        return erase;
    }

    /// <summary>
    /// The only place this class writes to standard output: one write and one
    /// flush, so the terminal is never handed half a footer.
    /// </summary>
    private void WriteFrame(string frame)
    {
        if (frame.Length == 0) return;

        var w = _stdout ?? Console.Out;
        w.Write(frame);
        w.Flush();
    }

    /// <summary>
    /// The terminal's width, read fresh on every draw so a resize clips rather
    /// than scrambles - criterion 16. A console that will not answer (rare,
    /// but the same one <see cref="Program"/> already guards elsewhere) reads
    /// as a plain 80 columns rather than failing the draw outright.
    /// </summary>
    private static int SafeWidth()
    {
        try
        {
            return Console.WindowWidth > 0 ? Console.WindowWidth : 80;
        }
        catch (IOException)
        {
            return 80;
        }
    }

    /// <summary>
    /// The night is over, however it ended - erase the footer and go back to
    /// being an ordinary terminal, so whatever prints after this (the closing
    /// tally, a shell prompt) scrolls normally with nothing pinned below it.
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (_stopped) return;

            _stopped = true;
            try
            {
                WriteFrame(Erase());
            }
            catch (IOException)
            {
            }
        }

        _timer.Dispose();
    }

    public void Dispose() => Stop();
}

/// <summary>Everything said, in order, and nothing printed. What a test reads back.</summary>
public sealed class Transcript : Terminal
{
    private readonly Lock _gate = new();
    private readonly List<string> _said = [];
    private readonly List<string> _complained = [];

    public IReadOnlyList<string> Said
    {
        get { lock (_gate) return [.. _said]; }
    }

    public IReadOnlyList<string> Complained
    {
        get { lock (_gate) return [.. _complained]; }
    }

    /// <summary>Whether anything said anywhere contains this.</summary>
    public bool Mentions(string what)
    {
        lock (_gate) return _said.Concat(_complained).Any(l => l.Contains(what, StringComparison.Ordinal));
    }

    public override void Line(string line)
    {
        lock (_gate) _said.Add(line);
    }

    public override void Complain(string line)
    {
        lock (_gate) _complained.Add(line);
    }
}

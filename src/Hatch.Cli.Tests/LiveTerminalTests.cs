namespace Hatch.Cli.Tests;

/// <summary>
/// The footer, erased and redrawn around every line - the one thing this class
/// does that <see cref="Readout"/> itself is deliberately kept out of.
/// </summary>
/// <remarks>
/// Redirects <see cref="Console.Out"/> and <see cref="Console.Error"/>, which
/// are process-wide - restored in a <c>finally</c>, the same guard
/// <c>RunnerNamesTests</c> already takes for the same reason.
/// </remarks>
public sealed class LiveTerminalTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static ReadoutState StateWithRunner() =>
        With(new ReadoutState(), s => s.SetRunner(new RunnerSnapshot("Chrissy", "Nathan", 1, TimeSpan.Zero, 0m, null)));

    private static T With<T>(T value, Action<T> configure)
    {
        configure(value);
        return value;
    }

    [Fact]
    public void ALine_IsFollowedByTheFooter()
    {
        var text = Captured(live => live.Line("hello"));

        Assert.StartsWith("hello\n", text, StringComparison.Ordinal);
        Assert.Contains("Chrissy for Nathan", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondLine_ErasesTheFirstFootersDrawBeforePrinting()
    {
        var text = Captured(live =>
        {
            live.Line("one");
            live.Line("two");
        });

        // The erase sequence: cursor to the start of the footer, then clear to
        // the end of the display - both present, in order, before "two".
        var eraseAt = text.IndexOf("\x1b[1F\x1b[0J", StringComparison.Ordinal);
        var twoAt = text.IndexOf("two", StringComparison.Ordinal);
        Assert.True(eraseAt >= 0 && eraseAt < twoAt);
    }

    [Fact]
    public void Stop_ErasesTheFooterAndDrawsNoMore()
    {
        var text = Captured(live =>
        {
            live.Line("one");
            live.Stop();
            live.Line("two");
        });

        // After Stop, "two" is a plain line - no footer content following it.
        var afterStop = text[(text.IndexOf("two", StringComparison.Ordinal))..];
        Assert.DoesNotContain("Chrissy", afterStop, StringComparison.Ordinal);
    }

    [Fact]
    public void Complain_AlsoGetsAFooterOnStandardError()
    {
        var original = Console.Error;
        var capturedErr = new StringWriter();
        var capturedOut = new StringWriter();
        Console.SetError(capturedErr);
        var originalOut = Console.Out;
        Console.SetOut(capturedOut);
        try
        {
            using var live = new LiveTerminal(StateWithRunner(), new FrozenClock(Now), color: false, tickEvery: TimeSpan.FromHours(1));
            live.Complain("uh oh");
        }
        finally
        {
            Console.SetError(original);
            Console.SetOut(originalOut);
        }

        Assert.StartsWith("uh oh\n", capturedErr.ToString(), StringComparison.Ordinal);
        Assert.Contains("Chrissy for Nathan", capturedOut.ToString(), StringComparison.Ordinal);
    }

    private static string Captured(Action<LiveTerminal> act)
    {
        var original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            using var live = new LiveTerminal(StateWithRunner(), new FrozenClock(Now), color: false, tickEvery: TimeSpan.FromHours(1));
            act(live);
        }
        finally
        {
            Console.SetOut(original);
        }

        return captured.ToString();
    }

    // ---- A frame is one write (HA-1354) ----

    [Fact]
    public void A_line_reaches_standard_output_as_one_write()
    {
        var recording = new RecordingWriter();
        using var live = new LiveTerminal(StateWithRunner(), new FrozenClock(Now), color: false, tickEvery: TimeSpan.FromHours(1), stdout: recording);

        live.Line("one");
        var first = Assert.Single(recording.Writes);
        Assert.StartsWith("one\n", first, StringComparison.Ordinal);
        Assert.Contains("Chrissy for Nathan", first, StringComparison.Ordinal);

        var footerHeight = first.Count(c => c == '\n') - 1;
        live.Line("two");
        Assert.Equal(2, recording.Writes.Count);
        var second = recording.Writes[1];
        Assert.StartsWith($"\x1b[{footerHeight}F\x1b[0J", second, StringComparison.Ordinal);
        Assert.Contains("two", second, StringComparison.Ordinal);
        Assert.Contains("Chrissy for Nathan", second, StringComparison.Ordinal);
    }

    [Fact]
    public void Complain_is_three_writes_in_order()
    {
        var recording = new RecordingWriter();
        var originalErr = Console.Error;
        var err = new StringWriter();
        Console.SetError(err);
        try
        {
            using var live = new LiveTerminal(StateWithRunner(), new FrozenClock(Now), color: false, tickEvery: TimeSpan.FromHours(1), stdout: recording);
            live.Line("one");
            var before = recording.Writes.Count;
            var footerHeight = recording.Writes[0].Count(c => c == '\n') - 1;

            var errAtEachWrite = new List<string>();
            recording.OnWrite = () => errAtEachWrite.Add(err.ToString().Replace("\r\n", "\n"));
            live.Complain("uh oh");

            // The erase alone, then (on the other stream) the line, then the footer alone.
            Assert.Equal(before + 2, recording.Writes.Count);
            Assert.Equal($"\x1b[{footerHeight}F\x1b[0J", recording.Writes[before]);
            Assert.Equal(["", "uh oh\n"], errAtEachWrite);
            Assert.StartsWith("Chrissy", recording.Writes[before + 1], StringComparison.Ordinal);
            Assert.DoesNotContain(recording.Writes, w => w.Contains("uh oh", StringComparison.Ordinal));
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public void A_tick_that_would_redraw_the_same_rows_writes_nothing()
    {
        var recording = new RecordingWriter();
        var state = StateWithRunner();
        using var live = new LiveTerminal(state, new FrozenClock(Now), color: false, tickEvery: TimeSpan.FromHours(1), stdout: recording);

        live.Line("one");
        var drawn = recording.Writes.Count;

        live.Redraw();
        Assert.Equal(drawn, recording.Writes.Count);

        state.SetRunner(new RunnerSnapshot("Chrissy", "Nathan", 2, TimeSpan.Zero, 0m, null));
        live.Redraw();
        Assert.Equal(drawn + 1, recording.Writes.Count);
        Assert.StartsWith("\x1b[", recording.Writes[^1], StringComparison.Ordinal);
        Assert.Contains("Chrissy for Nathan", recording.Writes[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Stop_after_a_line_writes_the_erase_alone()
    {
        var recording = new RecordingWriter();
        using var live = new LiveTerminal(StateWithRunner(), new FrozenClock(Now), color: false, tickEvery: TimeSpan.FromHours(1), stdout: recording);

        live.Line("one");
        var footerHeight = recording.Writes[0].Count(c => c == '\n') - 1;
        live.Stop();

        Assert.Equal(2, recording.Writes.Count);
        Assert.Equal($"\x1b[{footerHeight}F\x1b[0J", recording.Writes[1]);
    }

    private sealed class RecordingWriter : TextWriter
    {
        public List<string> Writes { get; } = [];
        public Action? OnWrite { get; set; }
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(string? value) { OnWrite?.Invoke(); Writes.Add(value ?? ""); }
        public override void Write(char value) { OnWrite?.Invoke(); Writes.Add(value.ToString()); }
        public override void WriteLine(string? value) { OnWrite?.Invoke(); Writes.Add((value ?? "") + "\n"); }
    }

    // ---- A closed terminal (HA-115) ----

    [Fact]
    public void A_terminal_that_cannot_be_written_to_does_not_throw()
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        Console.SetOut(new ThrowingWriter());
        Console.SetError(new ThrowingWriter());
        try
        {
            using var live = new LiveTerminal(StateWithRunner(), new FrozenClock(Now), color: false, tickEvery: TimeSpan.FromHours(1));
            live.Line("hello");
            live.Complain("uh oh");
            live.Redraw();
            live.Stop();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    private sealed class ThrowingWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(string? value) => throw new IOException("the pipe is gone");
        public override void WriteLine(string? value) => throw new IOException("the pipe is gone");
    }
}

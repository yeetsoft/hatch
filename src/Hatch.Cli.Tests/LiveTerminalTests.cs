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

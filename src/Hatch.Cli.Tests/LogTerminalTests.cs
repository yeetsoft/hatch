using System.Text.RegularExpressions;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>
/// The plain-text copy a night leaves behind: every line the screen got, with
/// the colour taken out, and a stamp in front of each.
/// </summary>
public sealed partial class LogTerminalTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hatch-log-" + Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(_dir, "night.log");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static FakeTimeProvider Clock() => new(Now);

    [Fact]
    public void EveryLine_ReachesTheInnerTerminalUnchanged()
    {
        var inner = new Transcript();
        using (var log = new LogTerminal(inner, LogPath, Clock()))
        {
            log.Line("\x1b[32mgreen\x1b[0m");
            log.Complain("\x1b[31mred\x1b[0m");
            log.Line("two\nlines");
        }

        Assert.Equal(["\x1b[32mgreen\x1b[0m", "two\nlines"], inner.Said);
        Assert.Equal(["\x1b[31mred\x1b[0m"], inner.Complained);
    }

    [Fact]
    public void TheFile_HasOneStampedEscapeFreeLinePerLine_InOrder()
    {
        var inner = new Transcript();
        using (var log = new LogTerminal(inner, LogPath, Clock()))
        {
            log.Line("\x1b[32mgreen\x1b[0m");
            log.Complain("went wrong");
            log.Line("first\nsecond");
            log.Line("");
            log.Line("\x1b[2Amoved\x1b[1;1H");
            log.Line("\x1b]0;a title\x07titled");
            log.Line("\x1b]8;;http://x\x1b\\linked\x1b]8;;\x1b\\");
            log.Line("crlf\r");
        }

        var stamp = "2026-01-01T12:00:00Z  ";
        Assert.Equal(
            [
                stamp + "green",
                stamp + "went wrong",
                stamp + "first",
                stamp + "second",
                stamp,
                stamp + "moved",
                stamp + "titled",
                stamp + "linked",
                stamp + "crlf",
            ],
            File.ReadAllLines(LogPath));
    }

    [Fact]
    public void TheStamp_IsReadFromTheClockAtEachLine()
    {
        var clock = Clock();
        using (var log = new LogTerminal(new Transcript(), LogPath, clock))
        {
            log.Line("one");
            clock.Advance(TimeSpan.FromSeconds(75));
            log.Line("two");
        }

        Assert.Equal(["2026-01-01T12:00:00Z  one", "2026-01-01T12:01:15Z  two"], File.ReadAllLines(LogPath));
    }

    [Fact]
    public void AFileThatCannotBeOpened_IsOneComplaint_AndTheLinesStillGetThrough()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "a-file");
        File.WriteAllText(file, "in the way");
        var path = Path.Combine(file, "x.log");

        var inner = new Transcript();
        using var log = new LogTerminal(inner, path, Clock());
        log.Line("still here");
        log.Complain("and this");

        Assert.Single(inner.Complained.Where(c => c.Contains(path, StringComparison.Ordinal)));
        Assert.Equal(2, inner.Complained.Count);
        Assert.Equal(["still here"], inner.Said);
    }

    [Fact]
    public void LinesFromSeveralThreads_AllLandWhole()
    {
        var inner = new Transcript();
        using (var log = new LogTerminal(inner, LogPath, Clock()))
        {
            Parallel.For(0, 8, t =>
            {
                for (var i = 0; i < 100; i++) log.Line($"line-{t}-{i}");
            });
        }

        var lines = File.ReadAllLines(LogPath);
        Assert.Equal(800, lines.Length);
        Assert.All(lines, l => Assert.Matches(StampedLine(), l));
        Assert.Equal(800, inner.Said.Count);
    }

    [Fact]
    public void TheFile_CanBeReadWhileTheTerminalIsOpen()
    {
        using var log = new LogTerminal(new Transcript(), LogPath, Clock());
        log.Line("already on disk");

        using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        Assert.Equal("2026-01-01T12:00:00Z  already on disk", reader.ReadLine());
    }

    [Fact]
    public void Dispose_IsIdempotent_AndALineAfterItOnlyForwards()
    {
        var inner = new Transcript();
        var log = new LogTerminal(inner, LogPath, Clock());
        log.Line("before");
        log.Dispose();
        log.Dispose();
        log.Line("after");

        Assert.Equal(["before", "after"], inner.Said);
        Assert.Equal(["2026-01-01T12:00:00Z  before"], File.ReadAllLines(LogPath));
        Assert.Empty(inner.Complained);
    }

    [GeneratedRegex(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ  line-\d+-\d+$")]
    private static partial Regex StampedLine();
}

using System.Text.RegularExpressions;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>
/// Switching the log on for one run: the file it names, the line that says
/// where it is, and that the screen is told everything the file is.
/// </summary>
public sealed class RunLogTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hatch-runlog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    // The log is still open for writing, so read it the way tail -f does.
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void With_no_directory_there_is_no_log()
    {
        var inner = new Transcript();

        Assert.Null(RunLog.Open(inner, null, "go-to-work", "runner", new FakeTimeProvider(Now)));

        Assert.Empty(inner.Said);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void The_file_is_named_for_the_command_the_runner_and_the_moment()
    {
        using var log = RunLog.Open(new Transcript(), _dir, "go-to-work", "A.B c", new FakeTimeProvider(Now));

        Assert.NotNull(log);
        Assert.Equal(Path.Combine(_dir, "go-to-work-ABc-20260101-120000.log"), log.Path);
        Assert.True(File.Exists(log.Path));
    }

    [Fact]
    public void A_name_with_nothing_usable_in_it_is_called_runner()
    {
        Assert.Equal("work-runner-20260101-120000.log", RunLog.NameFor("work", "../..", Now));
        Assert.Equal("work-ab-20260101-120000.log", RunLog.NameFor("work", "a/../b", Now));
    }

    [Fact]
    public void The_first_line_says_where_the_log_is_and_is_in_the_file_too()
    {
        var inner = new Transcript();
        using var log = RunLog.Open(inner, _dir, "go-to-work", "r", new FakeTimeProvider(Now))!;

        Assert.Equal([$"hatch: logging to {log.Path}"], inner.Said);
        Assert.Matches(
            $"^2026-01-01T12:00:00Z  hatch: logging to {Regex.Escape(log.Path)}\n$",
            ReadShared(log.Path));
    }

    [Fact]
    public void Lines_said_through_it_reach_the_screen_and_the_file_without_escapes()
    {
        var inner = new Transcript();
        using (var log = RunLog.Open(inner, _dir, "go-to-work", "r", new FakeTimeProvider(Now))!)
        {
            log.Line("\x1b[32mgreen\x1b[0m");
            log.Complain("hatch: clearing a stale lock left by pid 7");
        }

        Assert.Equal("\x1b[32mgreen\x1b[0m", inner.Said[1]);
        Assert.Equal(["hatch: clearing a stale lock left by pid 7"], inner.Complained);

        var lines = ReadShared(Directory.GetFiles(_dir).Single()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.EndsWith("  green", lines[1], StringComparison.Ordinal);
        Assert.EndsWith("  hatch: clearing a stale lock left by pid 7", lines[2], StringComparison.Ordinal);
        Assert.DoesNotContain("\x1b", string.Join('\n', lines), StringComparison.Ordinal);
    }
}

namespace Hatch.Cli.Tests;

/// <summary>
/// <see cref="Tty.ShouldReclaim"/> alone, with plain integers standing in for
/// <c>tcgetpgrp</c>'s answer - no pty, no process, no libc call.
/// </summary>
public sealed class TtyTests
{
    [Fact]
    public void AlreadyForegroundIsNothingToDo()
    {
        Assert.False(Tty.ShouldReclaim(foreground: 42, ours: 42));
    }

    [Fact]
    public void AnotherGroupInForegroundIsReclaimed()
    {
        Assert.True(Tty.ShouldReclaim(foreground: 7, ours: 42));
    }

    [Fact]
    public void FailedTcgetpgrpIsNotReclaimed()
    {
        Assert.False(Tty.ShouldReclaim(foreground: -1, ours: 42));
    }
}

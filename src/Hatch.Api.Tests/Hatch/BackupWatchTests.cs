using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="BackupWatch.Judge"/> - the rule the attention read lights its
/// backup row by. Pure, so these hand it values and touch no folder or clock.
/// </summary>
public class BackupWatchTests
{
    private const string Path = "/backups";
    private const string Stale = "no backup in the last 48 hours";

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LongAgo = Now.AddDays(-30);

    private static BackupCopies Copies(params DateTimeOffset[] at) =>
        new(at.OrderByDescending(a => a).Select(a => new BackupCopy(BackupNames.Final(a), a, 1)).ToList(), null);

    private static BackupAttempt Attempt(string outcome, string? error = null) => new(Now, outcome, error, null, null);

    [Fact]
    public void Judge_IsNullWithNoPath_WhateverElseIsWrong()
    {
        Assert.Null(BackupWatch.Judge("", Attempt(BackupOutcome.Failed, "boom"), Copies(), LongAgo, Now));
        Assert.Null(BackupWatch.Judge(null, Attempt(BackupOutcome.Failed, "boom"), Copies(), LongAgo, Now));
    }

    [Fact]
    public void Judge_ExactlyTwoIntervalsOldIsNotStale_OneSecondOverIs()
    {
        var edge = Now - 2 * BackupJob.Every;

        Assert.Null(BackupWatch.Judge(Path, null, Copies(edge), LongAgo, Now));

        var over = BackupWatch.Judge(Path, null, Copies(edge.AddSeconds(-1)), LongAgo, Now);
        Assert.Equal(Stale, over?.Sentence);
        Assert.Equal(edge.AddSeconds(-1), over?.LastSuccessfulAt);
    }

    [Fact]
    public void Judge_AFailedLastAttemptBeatsAStaleCopy_AndKeepsItsStamp()
    {
        var copy = Now.AddDays(-10);

        var row = BackupWatch.Judge(Path, Attempt(BackupOutcome.Failed, "boom"), Copies(copy), LongAgo, Now);

        Assert.Equal("boom", row?.Sentence);
        Assert.Equal(copy, row?.LastSuccessfulAt);
    }

    [Fact]
    public void Judge_ABlankErrorFallsBackToASentenceOfItsOwn()
    {
        Assert.Equal("the last backup failed", BackupWatch.Judge(Path, Attempt(BackupOutcome.Failed, "  "), Copies(), LongAgo, Now)?.Sentence);
        Assert.Equal("the last backup failed", BackupWatch.Judge(Path, Attempt(BackupOutcome.Failed), Copies(), LongAgo, Now)?.Sentence);
    }

    [Fact]
    public void Judge_AFolderProblemWithNoCopiesIsTheSentence_EvenInsideTheGrace()
    {
        var row = BackupWatch.Judge(Path, null, new BackupCopies([], "/backups does not exist"), Now, Now);

        Assert.Equal("/backups does not exist", row?.Sentence);
        Assert.Null(row?.LastSuccessfulAt);
    }

    [Fact]
    public void Judge_NoCopiesIsStaleOnlyOncePastTheStartupGrace()
    {
        Assert.Null(BackupWatch.Judge(Path, null, Copies(), Now.AddMinutes(-5), Now));

        var row = BackupWatch.Judge(Path, null, Copies(), Now.AddMinutes(-5).AddSeconds(-1), Now);
        Assert.Equal(Stale, row?.Sentence);
        Assert.Null(row?.LastSuccessfulAt);
    }

    [Fact]
    public void Judge_NoLastAttemptWithAFreshCopyIsFine()
    {
        Assert.Null(BackupWatch.Judge(Path, null, Copies(Now.AddHours(-1)), LongAgo, Now));
    }

    [Fact]
    public void Judge_AnOkLastAttemptWithAFreshCopyIsFine()
    {
        Assert.Null(BackupWatch.Judge(Path, Attempt(BackupOutcome.Ok), Copies(Now.AddHours(-1)), LongAgo, Now));
    }
}

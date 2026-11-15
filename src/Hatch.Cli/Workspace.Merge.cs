using System.Diagnostics;

namespace Hatch.Cli;

/// <summary>Which step cleared a merge <see cref="Workspace.AbortMerge"/> was asked to clear.</summary>
public enum AbortOutcome
{
    /// <summary><c>git merge --abort</c> did it.</summary>
    Aborted,

    /// <summary><c>git merge --abort</c> refused; <c>git reset --merge</c> did it.</summary>
    ResetMerge,

    /// <summary>Both of those refused; <c>git reset --hard</c> did it.</summary>
    ResetHard,

    /// <summary>All three refused. <see cref="ForcedAbort.Step"/> and <see cref="ForcedAbort.Why"/> name the last one tried.</summary>
    Refused,
}

/// <summary>
/// What <see cref="Workspace.AbortMerge"/> came to: which step cleared the merge,
/// whether it was the loop's own (the <c>hatch-merge</c> marker was there before
/// it ran), and how many lines <c>git status --porcelain</c> held before the
/// first destructive step touched anything - what a caller can say was thrown
/// away. <see cref="Step"/> and <see cref="Why"/> are null except on
/// <see cref="AbortOutcome.Refused"/>.
/// </summary>
public sealed record ForcedAbort(AbortOutcome Outcome, bool OwnMarker, int Discarded, string? Step, string? Why);

/// <summary>
/// Clearing a merge, two ways: the narrow one <see cref="Workspace.Prepare"/> and
/// <see cref="Workspace.Leave"/> already made - a plain <c>merge --abort</c>,
/// retried once past a lock the loop's own killed git left behind - and the
/// general one, <see cref="Workspace.AbortMerge"/>, that escalates past a merge
/// <c>--abort</c> will not take back at all.
/// </summary>
public sealed partial class Workspace
{
    /// <summary>
    /// When this process itself started - so a lock dated before it can only be
    /// somebody else's git, already dead, and not a merge still being written
    /// by this one.
    /// </summary>
    private static readonly DateTime ProcessStartedAt = ReadProcessStartedAt();

    private static DateTime ReadProcessStartedAt()
    {
        try { return Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
        catch { return DateTime.UtcNow; }
    }

    /// <summary>Whether a merge in progress is clear to proceed past, or still blocks.</summary>
    private enum MergeSettled { Clear, Blocked }

    /// <summary>
    /// A merge in progress, and whether it is the runner's: it wrote a marker
    /// before it started one, so a merge that is there with the marker was
    /// interrupted, and one that is there without was somebody's.
    /// </summary>
    /// <remarks>
    /// The marker is cleared whenever no merge is in progress, so one that
    /// outlived its merge - the session resolved it and committed - does not
    /// make a later merge by a person look like the runner's.
    /// </remarks>
    private MergeSettled SettleMerge()
    {
        var marker = MarkerPath();
        var merging = MergeInProgress();

        if (!merging)
        {
            if (marker is not null && File.Exists(marker)) File.Delete(marker);
            return MergeSettled.Clear;
        }

        if (marker is null || !File.Exists(marker))
        {
            complain("hatch: a merge is in progress in the tree and hatch did not start it - finish it or abort it by hand");
            return MergeSettled.Blocked;
        }

        var abort = TryAbortMerge();
        if (!abort.Ok)
        {
            complain($"hatch: a merge the loop started was never finished, and would not abort{abort.Because}");
            return MergeSettled.Blocked;
        }

        File.Delete(marker);
        say("hatch:   aborted a merge the loop started and never finished");
        return MergeSettled.Clear;
    }

    /// <summary>
    /// A plain <c>git merge --abort</c> - retried once past an
    /// <c>index.lock</c> dated before this process started, which is nobody's
    /// to leave in the way and is removed unasked. A lock written after this
    /// process started is somebody else's doing right now, and is left alone.
    /// Returns the last <c>git merge --abort</c> it ran, so a refusal can say
    /// what git said.
    /// </summary>
    private Ran TryAbortMerge()
    {
        var first = Git("merge", "--abort");
        if (first.Ok) return first;

        if (GitPath("index.lock") is not { } lockPath || !File.Exists(lockPath)) return first;
        if (File.GetLastWriteTimeUtc(lockPath) >= ProcessStartedAt) return first;

        File.Delete(lockPath);
        var retry = Git("merge", "--abort");
        if (!retry.Ok) return retry;

        say("hatch:   removed a lock the loop's own interrupted git left behind");
        return retry;
    }

    /// <summary>
    /// Clears a merge in progress past what <c>git merge --abort</c> alone can
    /// take back - the one place in <see cref="Workspace"/> that reaches past it,
    /// to <c>git reset --merge</c> and then <c>git reset --hard</c>. Never asked
    /// by <see cref="Prepare"/> or <see cref="Leave"/> themselves: both still only
    /// try the plain abort, and stop where it refuses.
    /// </summary>
    public ForcedAbort AbortMerge()
    {
        var ownMarker = MarkerPath() is { } marker && File.Exists(marker);

        var status = Git("status", "--porcelain");
        var discarded = status.Ok && status.Out.Trim().Length > 0 ? status.Out.Trim().Split('\n').Length : 0;

        if (GitPath("index.lock") is { } lockPath && File.Exists(lockPath)) File.Delete(lockPath);

        Ran last = default;
        string lastStep = "";
        foreach (var (step, outcome, args) in AbortSteps)
        {
            last = Git(args);
            lastStep = step;
            if (!last.Ok) continue;

            if (MarkerPath() is { } own && File.Exists(own)) File.Delete(own);
            return new ForcedAbort(outcome, ownMarker, discarded, null, null);
        }

        return new ForcedAbort(AbortOutcome.Refused, ownMarker, discarded, lastStep, last.Why);
    }

    private static readonly (string Step, AbortOutcome Outcome, string[] Args)[] AbortSteps =
    [
        ("merge --abort", AbortOutcome.Aborted, ["merge", "--abort"]),
        ("reset --merge", AbortOutcome.ResetMerge, ["reset", "--merge"]),
        ("reset --hard", AbortOutcome.ResetHard, ["reset", "--hard"]),
    ];
}

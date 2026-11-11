using System.ComponentModel;
using System.Diagnostics;
using System.Net;

namespace Hatch.Cli;

/// <summary>
/// What a starting loop does about the session its dead predecessor left: ends
/// it, releases its claim, and says so on the ticket once.
/// </summary>
/// <remarks>
/// <para>A loop that dies mid-increment - a crash, a closed window - takes
/// nothing with it but itself. The agent it spawned keeps running in the
/// checkout, and the claim sits out its lease on a ticket nobody is working.
/// Run only once <see cref="LoopLock.Take"/> has succeeded, so no live loop
/// holds the checkout, and before this loop's first claim, so the session
/// being ended is never one this loop started. Only the loop reads another's
/// record: <c>hatch work</c> writes and clears its own and never acts on a
/// leftover.</para>
///
/// <para>The pid is acted on only where its start time is the record's - a
/// reused pid is somebody else's process, and the claim is released all the
/// same, since the session it was recorded for is gone either way.</para>
/// </remarks>
public static class Orphaned
{
    /// <summary>
    /// How far apart two readings of one process's start may be and still be
    /// that process. Wider than any real disagreement between the two reads and
    /// far narrower than a reused pid: Linux derives a start from whole clock
    /// ticks, so the same process does not always read to the millisecond.
    /// </summary>
    private static readonly TimeSpan StartTolerance = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan KillBudget = TimeSpan.FromSeconds(10);

    /// <summary>
    /// <paramref name="alive"/>, <paramref name="startTimeOf"/> and
    /// <paramref name="kill"/> are how a process is asked about and ended, so a
    /// test can answer without one.
    /// </summary>
    /// <returns>Whether there was a record of this loop's own board to act on.</returns>
    public static async Task<bool> EndAsync(
        string root, string tempDirectory, Board board, string baseUrl, IAgentAdapter adapter, Terminal say,
        CancellationToken ct,
        Func<int, bool>? alive = null, Func<int, DateTime?>? startTimeOf = null, Action<int>? kill = null,
        int? ownPid = null)
    {
        var path = SessionRecord.PathFor(root, tempDirectory);
        if (SessionRecord.Read(path) is not { } record) return false;

        // A checkout pointed at another board left this, and what it names is
        // that board's claim: not ours to release, and its token means nothing
        // here. Left for the loop that can.
        if (!string.Equals(record.Base.TrimEnd('/'), baseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            say.Complain(
                $"hatch: {root} holds a session record from {record.Base}, not {baseUrl} - " +
                "left alone; it belongs to a checkout pointed at another board");
            return false;
        }

        alive ??= StillRunning;
        startTimeOf ??= StartTimeOf;
        kill ??= Kill;

        try
        {
            var ended = false;
            if (record.Pid != (ownPid ?? Environment.ProcessId) && alive(record.Pid)
                && startTimeOf(record.Pid) is { } started
                && (started - record.StartedAt).Duration() <= StartTolerance)
            {
                kill(record.Pid);
                ended = true;
            }

            HttpStatusCode? released;
            try
            {
                released = await board.ReleaseAbandonedAsync(record.Key, record.Token, ct);
            }
            catch (Exception e) when (e is HatchException or OperationCanceledException)
            {
                say.Complain($"hatch: {record.Key} - the claim a dead loop left could not be released: {e.Message}");
                return true;
            }

            var what = ended ? "ended its session" : "found its session already gone";

            if (released == HttpStatusCode.NoContent)
            {
                await CommentAsync(board, say, adapter, record, ct);
                say.Line($"hatch: a loop died in {root} while running {record.Key} - {what} and released the claim");
            }
            else if (released is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
            {
                say.Line($"hatch: a loop died in {root} while running {record.Key} - {what}; its claim was already gone");
            }
            else
            {
                say.Complain(
                    $"hatch: a loop died in {root} while running {record.Key} - {what}; " +
                    $"its claim could not be released ({(released is { } code ? (int)code : "no answer")}) and will lapse on its own");
            }

            return true;
        }
        finally
        {
            SessionRecord.Delete(path);
        }
    }

    private static async Task CommentAsync(
        Board board, Terminal say, IAgentAdapter adapter, SessionRecord record, CancellationToken ct)
    {
        var body = "A loop died while an unattended increment was running here; the session it left was ended and the claim released.";

        body += record.SessionId is { Length: > 0 } session
            ? $"\n\nThe session it ran in is still there, with everything it did in context:\n\n    {adapter.ResumeCommand(session)}"
            : "\n\nThere is no session to resume: the run ended before it said what its id was.";

        try
        {
            await board.CommentAsync(record.Key, body, ct);
        }
        catch (Exception e) when (e is HatchException or OperationCanceledException)
        {
            say.Complain($"hatch: {record.Key} could not be told its claim was released - {e.Message}");
        }
    }

    /// <summary>
    /// Whether that pid is a process this user can see: one that is gone, or
    /// that cannot be opened, reads as not alive - there is nothing here to
    /// end either way.
    /// </summary>
    internal static bool StillRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    /// <summary>When that pid's process started, in UTC, or null where it cannot be read.</summary>
    internal static DateTime? StartTimeOf(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The whole tree, waited for - the agent has spawned tools of its own.</summary>
    internal static void Kill(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(KillBudget);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // It ended between the question and the answer, which is the
            // outcome that was being asked for.
        }
    }
}

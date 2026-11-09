using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hatch.Cli;

/// <summary>
/// Backstop for a shell probe that left the controlling terminal in its own
/// process group - a shell killed at <see cref="Detect.ShellTimeout"/>, or an
/// rc file that runs <c>setopt monitor</c>. HA-2104's <c>+m</c> is the actual
/// fix; this only covers what <c>+m</c> cannot reach.
/// </summary>
[UnsupportedOSPlatform("windows")]
internal static class Tty
{
    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetpgrp(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetpgrp(int fd, int pgrp);

    [DllImport("libc", SetLastError = true)]
    private static extern int getpgrp();

    /// <summary><c>foreground</c> &lt; 0 is <c>tcgetpgrp</c> failing - nothing to act on, not a signal to reclaim.</summary>
    internal static bool ShouldReclaim(int foreground, int ours) => foreground >= 0 && foreground != ours;

    public static void TryReclaimForeground()
    {
        try
        {
            var ours = getpgrp();
            if (!ShouldReclaim(tcgetpgrp(0), ours)) return;

            using var ignore = PosixSignalRegistration.Create(PosixSignal.SIGTTOU, context => context.Cancel = true);
            tcsetpgrp(0, ours);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { }
    }
}

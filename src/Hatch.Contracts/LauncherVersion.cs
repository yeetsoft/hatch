namespace Hatch.Contracts;

/// <summary>
/// The launcher version of this build, read by the command line and by the
/// board so both name one number. The launcher's own file says which version
/// it is; a runner's beat carries it; the board compares it with this.
/// </summary>
public static class LauncherVersion
{
    /// <summary>
    /// One whole number, starting at 1. It goes up when the launcher learns to
    /// do something a child or the board should know about, and never when it
    /// merely changes inside.
    /// </summary>
    public const int Current = 1;
}

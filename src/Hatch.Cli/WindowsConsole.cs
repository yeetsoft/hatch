using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hatch.Cli;

/// <summary>
/// The one thing a Windows console needs before it will read the escape
/// sequences the readout draws with: virtual terminal processing, switched on
/// for this process's own standard output. Not checked at HA-123 time - see
/// HA-120's "what I could not" - so this fails closed: a console that will not
/// take the mode switch answers false, and the caller falls back to plain
/// lines rather than risk a garbled terminal.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsConsole
{
    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    public static bool TryEnableVirtualTerminalProcessing()
    {
        try
        {
            var handle = GetStdHandle(StdOutputHandle);
            if (handle == nint.Zero || handle == new nint(-1)) return false;
            if (!GetConsoleMode(handle, out var mode)) return false;
            if ((mode & EnableVirtualTerminalProcessing) != 0) return true;

            return SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return false;
        }
    }
}

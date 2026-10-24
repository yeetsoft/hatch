namespace Hatch.Cli;

/// <summary>
/// A file written whole or not at all, for the files that hold a credential or
/// a pointer a later command reads back.
/// </summary>
internal static class AtomicFile
{
    /// <summary>
    /// Temp file, then mode, then rename - the rename is what makes a
    /// half-written file impossible to read as a whole one, and the mode is set
    /// before there is anything in it to read. <c>File.SetUnixFileMode</c> is a
    /// no-op concept on Windows, where the file inherits the directory's ACL
    /// and <c>%APPDATA%</c> is already per-user.
    /// </summary>
    public static void Write(string target, IEnumerable<string> lines)
    {
        var directory = Path.GetDirectoryName(target);
        if (directory is { Length: > 0 }) Directory.CreateDirectory(directory);

        var temp = $"{target}.{Environment.ProcessId}";

        File.WriteAllLines(temp, lines);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        File.Move(temp, target, overwrite: true);
    }
}

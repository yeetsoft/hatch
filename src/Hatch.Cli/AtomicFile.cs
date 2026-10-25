using System.Text;

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
    public static void Write(string target, IEnumerable<string> lines) =>
        Replace(target, temp => File.WriteAllLines(temp, lines));

    /// <summary>
    /// The same, for text that is already exactly what the file should hold -
    /// <see cref="Write"/> joins its lines with the platform newline, which would
    /// turn a generator's CRLF into LF on a Mac and the other way round.
    /// UTF-8 with no byte-order mark unless <paramref name="encoding"/> says
    /// otherwise.
    /// </summary>
    public static void WriteText(string target, string text, Encoding? encoding = null) =>
        Replace(target, temp => File.WriteAllText(temp, text, encoding ?? new UTF8Encoding(false)));

    private static void Replace(string target, Action<string> fill)
    {
        var directory = Path.GetDirectoryName(target);
        if (directory is { Length: > 0 }) Directory.CreateDirectory(directory);

        var temp = $"{target}.{Environment.ProcessId}";

        fill(temp);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        File.Move(temp, target, overwrite: true);
    }
}

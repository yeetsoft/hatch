namespace Hatch.Cli;

/// <summary>
/// Making the word <c>hatch</c> resolve inside a session this program spawned.
/// </summary>
/// <remarks>
/// <para>The prompt an agent receives names <c>hatch show</c>, <c>hatch
/// comment</c>, <c>hatch ask</c> and the rest (AERIE-934). That instruction is
/// only true if the word resolves, and telling an agent to run a command that
/// is not there costs an increment to find out.</para>
///
/// <para>This process <em>is</em> <c>hatch</c>. So where it is running as a
/// built binary of that name, its own directory goes on the front of the
/// session's <c>PATH</c> - which makes the loop work in a fresh checkout
/// without anybody having installed a copy first, and is the same program an
/// installed copy would be. Where it is running some other way - <c>dotnet
/// run</c>, where the executable is <c>dotnet</c> - there is nothing to point
/// at and nothing is changed, because inventing a path to a binary that may not
/// exist would be worse than leaving <c>PATH</c> alone.</para>
///
/// <para>The session's calls also have to name the runner that dispatched it,
/// or an installation with its wall off answers them as the local person - see
/// <see cref="AsRunner"/>.</para>
/// </remarks>
public static class Reach
{
    /// <summary>
    /// The first file called <paramref name="name"/> in a directory of
    /// <paramref name="path"/> that could be run, as an absolute path, or null.
    /// On Windows <c>.exe</c> and <c>.cmd</c> are tried before the bare name; on
    /// Unix a file with no execute bit is passed over, as a shell would.
    /// </summary>
    /// <remarks>
    /// Absolute because a hook runs from the session's root and not from here,
    /// so a relative <c>PATH</c> entry that resolved now would not then.
    /// </remarks>
    /// <param name="path"><see cref="Environment"/>'s <c>PATH</c> in a real run; a parameter so a test can name directories of its own.</param>
    public static string? Find(string name, string? path)
    {
        foreach (var dir in (path ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;

            foreach (var candidateName in OperatingSystem.IsWindows() ? (string[])[$"{name}.exe", $"{name}.cmd", name] : [name])
            {
                var candidate = Path.Combine(dir, candidateName);
                if (File.Exists(candidate) && CanRun(candidate)) return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static bool CanRun(string file)
    {
        if (OperatingSystem.IsWindows()) return true;

        const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        try { return (File.GetUnixFileMode(file) & execute) != 0; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// The directory holding this program, if this program is a binary called
    /// <c>hatch</c>. Null for every other way of being started.
    /// </summary>
    /// <param name="processPath">
    /// <see cref="Environment.ProcessPath"/> in a real run. A parameter so a
    /// test can ask about a path this machine does not have.
    /// </param>
    public static string? OwnDirectory(string? processPath)
    {
        if (string.IsNullOrEmpty(processPath)) return null;

        // The published name is `hatch`, and `hatch.exe` on Windows. Anything
        // else is `dotnet`, a test host, or a rename nobody here can vouch for.
        var name = Path.GetFileNameWithoutExtension(processPath);
        if (!string.Equals(name, "hatch", StringComparison.OrdinalIgnoreCase)) return null;

        return Path.GetDirectoryName(processPath) is { Length: > 0 } directory ? directory : null;
    }

    /// <summary>
    /// <paramref name="directory"/> on the front of <c>PATH</c>, leaving what
    /// was already there behind it.
    /// </summary>
    /// <remarks>
    /// The front rather than the back, so the <c>hatch</c> that answers is the
    /// one that composed the prompt - a session told to comment on a ticket by
    /// one build and doing it through another is a difference nobody would
    /// think to look for. A directory already at the front is left alone rather
    /// than added twice.
    /// </remarks>
    public static void OnPath(IDictionary<string, string?> environment, string? directory)
    {
        if (directory is not { Length: > 0 }) return;

        var existing = environment.TryGetValue("PATH", out var path) ? path ?? "" : "";
        var separator = Path.PathSeparator.ToString();

        if (existing.Length == 0)
        {
            environment["PATH"] = directory;
            return;
        }

        var already = existing.Split(Path.PathSeparator).FirstOrDefault();
        if (string.Equals(already, directory, StringComparison.Ordinal)) return;

        environment["PATH"] = directory + separator + existing;
    }

    /// <summary>
    /// <c>HATCH_RUNNER</c> set to <paramref name="runnerName"/>, so a
    /// <c>hatch</c> call made inside the session names itself as the runner that
    /// dispatched it rather than as whoever is at the keyboard. A null or blank
    /// name leaves the environment as it was, an inherited value included.
    /// </summary>
    public static void AsRunner(IDictionary<string, string?> environment, string? runnerName)
    {
        if (string.IsNullOrWhiteSpace(runnerName)) return;

        environment["HATCH_RUNNER"] = runnerName;
    }

    /// <summary>
    /// <c>HATCH_BASE</c> set to <paramref name="settings"/>'s <see cref="Settings.Base"/>,
    /// and <c>HATCH_KEY</c> to its <see cref="Settings.Key"/> where that is not empty. A
    /// blank key removes any <c>HATCH_KEY</c> already exported rather than exporting
    /// nothing: a key goes with its origin, so one the parent ignored and left in the
    /// environment would pair, in the child, with the origin exported here.
    /// Null <paramref name="settings"/> leaves the environment exactly as it was.
    /// </summary>
    public static void ToHatch(IDictionary<string, string?> environment, Settings? settings)
    {
        if (settings is null) return;

        environment["HATCH_BASE"] = settings.Base;
        if (settings.Key.Length > 0) environment["HATCH_KEY"] = settings.Key;
        else environment.Remove("HATCH_KEY");
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Hatch.Cli;

/// <summary>Runs one program to its end: the exit code, and its stdout and stderr together.</summary>
public delegate (int Exit, string Output) RunTool(string file, string[] args);

public enum StandbyOutcome { Changed, Unchanged, Refused }

/// <summary>What a host did for an item, and when it did not, one sentence of why.</summary>
public sealed record StandbyResult(StandbyOutcome Outcome, string Refusal = "")
{
    public static StandbyResult Changed { get; } = new(StandbyOutcome.Changed);

    public static StandbyResult Unchanged { get; } = new(StandbyOutcome.Unchanged);

    public static StandbyResult Refused(string refusal) => new(StandbyOutcome.Refused, refusal);
}

/// <summary>
/// The part of standby that touches the machine: puts an item's files where the
/// platform looks, and registers or forgets them with the platform's own tool.
/// </summary>
/// <remarks>
/// Install and Remove are idempotent - the second call finds nothing to do and
/// says <see cref="StandbyOutcome.Unchanged"/> - and Remove never stops a
/// process that is already running: it forgets the login item, which is a
/// different thing from ending the loop it started.
/// </remarks>
public interface IStandbyHost
{
    bool Installed(StandbyItem item);

    StandbyResult Install(StandbyItem item);

    StandbyResult Remove(StandbyItem item);
}

/// <summary>The host for the machine this is running on, and the real way to run a tool.</summary>
public static class StandbyHosts
{
    public static IStandbyHost ForThisMachine(RunTool run)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";

        if (OperatingSystem.IsWindows())
            return new WindowsStandbyHost(run, Environment.UserDomainName, Environment.UserName, Path.GetTempPath());

        if (OperatingSystem.IsMacOS())
            return new MacStandbyHost(home, path);

        return new LinuxStandbyHost(run, home, Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"), path);
    }

    public static (int Exit, string Output) RunProcess(string file, string[] args)
    {
        var start = new ProcessStartInfo
        {
            FileName = file,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(start);
            if (process is null) return (-1, $"could not start {file}");

            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdout + stderr.GetAwaiter().GetResult());
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return (-1, $"could not start {file}: {e.Message}");
        }
    }
}

/// <summary>
/// What the three hosts share. A step that fails returns its refusal as a
/// string and a step that works returns null, so a host reads as a sequence of
/// <c>if (Step() is { } refusal) return Refused(refusal)</c> and never as a
/// try block.
/// </summary>
internal static class StandbyFiles
{
    /// <summary>The file is there and holds exactly <paramref name="text"/>.</summary>
    public static bool Same(string path, string text)
    {
        try
        {
            return File.Exists(path) && File.ReadAllText(path) == text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string? Write(string path, string text, Encoding? encoding = null)
    {
        try
        {
            AtomicFile.WriteText(path, text, encoding);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Failed(path, e);
        }
    }

    public static string? Delete(string path)
    {
        try
        {
            File.Delete(path);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Failed(path, e);
        }
    }

    /// <summary>The folder the loop's log goes in, which nothing else creates.</summary>
    public static string? LogFolder(StandbyItem item)
    {
        var folder = Path.GetDirectoryName(item.Log);
        if (string.IsNullOrEmpty(folder)) return null;

        try
        {
            Directory.CreateDirectory(folder);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Failed(folder, e);
        }
    }

    /// <summary>Runs a tool; null when it exits 0, else the refusal carrying what it said.</summary>
    public static string? Tool(RunTool run, string file, params string[] args)
    {
        var (exit, output) = run(file, args);
        if (exit == 0) return null;

        var said = output.Trim();
        var tail = said.Length == 0 ? "" : $": {said}";
        return $"hatch: {file} {string.Join(' ', args)} exited {exit}{tail}";
    }

    public static bool Succeeds(RunTool run, string file, params string[] args) => run(file, args).Exit == 0;

    private static string Failed(string path, Exception e) => $"hatch: {path} - {e.Message}";
}

/// <summary>
/// A scheduled task at logon, in the current user's own Task Scheduler.
/// </summary>
/// <param name="run">How to run <c>schtasks</c>.</param>
/// <param name="domain">The user's domain, as <c>Environment.UserDomainName</c> says it.</param>
/// <param name="user">The user's name.</param>
/// <param name="tempFolder">Where the task definition waits while <c>schtasks</c> reads it.</param>
internal sealed class WindowsStandbyHost(RunTool run, string domain, string user, string tempFolder) : IStandbyHost
{
    private const string Tasks = "schtasks";

    public bool Installed(StandbyItem item) => StandbyFiles.Succeeds(run, Tasks, "/Query", "/TN", item.Id);

    public StandbyResult Install(StandbyItem item)
    {
        var wrapper = StandbyItem.WindowsWrapper(item);
        if (StandbyFiles.Same(item.Wrapper, wrapper) && Installed(item)) return StandbyResult.Unchanged;

        if (StandbyFiles.LogFolder(item) is { } noLog) return StandbyResult.Refused(noLog);
        if (StandbyFiles.Write(item.Wrapper, wrapper) is { } noWrapper) return StandbyResult.Refused(noWrapper);

        // The definition is UTF-16, and schtasks reads it from a file; it is the
        // only way to give a task a logon trigger for one user and no console.
        var xml = Path.Combine(tempFolder, item.Id + ".xml");
        try
        {
            if (StandbyFiles.Write(xml, StandbyItem.TaskXml(item, domain, user), Encoding.Unicode) is { } noXml)
                return StandbyResult.Refused(noXml);

            return StandbyFiles.Tool(run, Tasks, "/Create", "/TN", item.Id, "/XML", xml, "/F") is { } refusal
                ? StandbyResult.Refused(refusal)
                : StandbyResult.Changed;
        }
        finally
        {
            try
            {
                File.Delete(xml);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A temp folder the OS will reap.
            }
        }
    }

    public StandbyResult Remove(StandbyItem item)
    {
        var changed = false;
        if (Installed(item))
        {
            if (StandbyFiles.Tool(run, Tasks, "/Delete", "/TN", item.Id, "/F") is { } refusal)
                return StandbyResult.Refused(refusal);
            changed = true;
        }

        if (File.Exists(item.Wrapper))
        {
            if (StandbyFiles.Delete(item.Wrapper) is { } stuck) return StandbyResult.Refused(stuck);
            changed = true;
        }

        return changed ? StandbyResult.Changed : StandbyResult.Unchanged;
    }
}

/// <summary>
/// A launchd agent in the user's <c>LaunchAgents</c> folder, which launchd reads
/// at the next login. No tool is run: loading it now would start the loop, and
/// unloading it would stop one that is running.
/// </summary>
/// <param name="home">The user's home folder.</param>
/// <param name="path">The <c>PATH</c> written into the wrapper.</param>
internal sealed class MacStandbyHost(string home, string path) : IStandbyHost
{
    public bool Installed(StandbyItem item) => File.Exists(item.LaunchAgentPath(home));

    public StandbyResult Install(StandbyItem item)
    {
        var wrapper = StandbyItem.PosixWrapper(item, path);
        var plist = StandbyItem.LaunchdPlist(item);
        var agent = item.LaunchAgentPath(home);
        if (StandbyFiles.Same(item.Wrapper, wrapper) && StandbyFiles.Same(agent, plist)) return StandbyResult.Unchanged;

        if (StandbyFiles.LogFolder(item) is { } noLog) return StandbyResult.Refused(noLog);
        if (StandbyFiles.Write(item.Wrapper, wrapper) is { } noWrapper) return StandbyResult.Refused(noWrapper);
        if (StandbyFiles.Write(agent, plist) is { } noPlist) return StandbyResult.Refused(noPlist);

        return StandbyResult.Changed;
    }

    public StandbyResult Remove(StandbyItem item)
    {
        var changed = false;
        foreach (var file in new[] { item.LaunchAgentPath(home), item.Wrapper })
        {
            if (!File.Exists(file)) continue;
            if (StandbyFiles.Delete(file) is { } stuck) return StandbyResult.Refused(stuck);
            changed = true;
        }

        return changed ? StandbyResult.Changed : StandbyResult.Unchanged;
    }
}

/// <summary>
/// A systemd user unit where the user has a systemd, else an autostart entry
/// for the desktop session.
/// </summary>
/// <param name="run">How to run <c>systemctl</c>.</param>
/// <param name="home">The user's home folder.</param>
/// <param name="xdgConfigHome"><c>XDG_CONFIG_HOME</c>, or null when it is not set.</param>
/// <param name="path">The <c>PATH</c> written into the wrapper.</param>
internal sealed class LinuxStandbyHost(RunTool run, string home, string? xdgConfigHome, string path) : IStandbyHost
{
    private const string Systemctl = "systemctl";

    private string Config => StandbyItem.ConfigHome(xdgConfigHome, home);

    public bool Installed(StandbyItem item) =>
        File.Exists(item.SystemdUnitPath(Config)) || File.Exists(item.AutostartPath(Config));

    public StandbyResult Install(StandbyItem item) =>
        StandbyFiles.Succeeds(run, Systemctl, "--user", "show-environment") ? InstallUnit(item) : InstallAutostart(item);

    private StandbyResult InstallUnit(StandbyItem item)
    {
        var wrapper = StandbyItem.PosixWrapper(item, path);
        var unit = StandbyItem.SystemdUnit(item);
        var unitPath = item.SystemdUnitPath(Config);

        // A machine that had no user systemd when this was last installed has an
        // autostart entry, and would start the loop twice.
        var stale = item.AutostartPath(Config);
        if (File.Exists(stale) && StandbyFiles.Delete(stale) is { } stuck) return StandbyResult.Refused(stuck);

        if (StandbyFiles.Same(item.Wrapper, wrapper) && StandbyFiles.Same(unitPath, unit)) return StandbyResult.Unchanged;

        if (StandbyFiles.LogFolder(item) is { } noLog) return StandbyResult.Refused(noLog);
        if (StandbyFiles.Write(item.Wrapper, wrapper) is { } noWrapper) return StandbyResult.Refused(noWrapper);
        if (StandbyFiles.Write(unitPath, unit) is { } noUnit) return StandbyResult.Refused(noUnit);

        // Enabled for the next login and not started: `--now` would run a second
        // loop beside the one that is probably running already.
        if (StandbyFiles.Tool(run, Systemctl, "--user", "daemon-reload") is { } reload) return StandbyResult.Refused(reload);
        if (StandbyFiles.Tool(run, Systemctl, "--user", "enable", item.Id + ".service") is { } enable)
            return StandbyResult.Refused(enable);

        return StandbyResult.Changed;
    }

    private StandbyResult InstallAutostart(StandbyItem item)
    {
        var wrapper = StandbyItem.PosixWrapper(item, path);
        var entry = StandbyItem.DesktopEntry(item);
        var entryPath = item.AutostartPath(Config);
        if (StandbyFiles.Same(item.Wrapper, wrapper) && StandbyFiles.Same(entryPath, entry)) return StandbyResult.Unchanged;

        if (StandbyFiles.LogFolder(item) is { } noLog) return StandbyResult.Refused(noLog);
        if (StandbyFiles.Write(item.Wrapper, wrapper) is { } noWrapper) return StandbyResult.Refused(noWrapper);
        if (StandbyFiles.Write(entryPath, entry) is { } noEntry) return StandbyResult.Refused(noEntry);

        return StandbyResult.Changed;
    }

    public StandbyResult Remove(StandbyItem item)
    {
        var unit = item.SystemdUnitPath(Config);
        var hadUnit = File.Exists(unit);
        var changed = false;

        if (hadUnit)
        {
            if (StandbyFiles.Tool(run, Systemctl, "--user", "disable", item.Id + ".service") is { } refusal)
                return StandbyResult.Refused(refusal);
            changed = true;
        }

        foreach (var file in new[] { unit, item.AutostartPath(Config), item.Wrapper })
        {
            if (!File.Exists(file)) continue;
            if (StandbyFiles.Delete(file) is { } stuck) return StandbyResult.Refused(stuck);
            changed = true;
        }

        if (hadUnit && StandbyFiles.Tool(run, Systemctl, "--user", "daemon-reload") is { } reload)
            return StandbyResult.Refused(reload);

        return changed ? StandbyResult.Changed : StandbyResult.Unchanged;
    }
}

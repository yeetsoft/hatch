using System.Text;

namespace Hatch.Cli;

/// <summary>
/// The login item that starts a checkout's loop when its operator signs in:
/// what it is called, what it runs, where it writes, and the text each platform
/// registers for it.
/// </summary>
/// <remarks>
/// <para>Pure - no file written, no process run, and nothing here asks the host.
/// Every path arrives as a string and every generator returns a string, so the
/// text for a Windows task can be produced, and tested, on a Mac. Registering
/// the item and writing the files is a different seam.</para>
///
/// <para><see cref="Command"/> is an argv and not a command line, because each
/// platform quotes a command its own way. A record compares a list by reference,
/// so two equal items are compared field by field, with the command as a
/// sequence.</para>
/// </remarks>
public sealed record StandbyItem(
    string Id, string Description, string Root, string Log, string Wrapper, IReadOnlyList<string> Command)
{
    /// <summary>
    /// The item for one checkout. The same checkout gives an equal item on every
    /// call, whatever the runner is called by then.
    /// </summary>
    /// <param name="root">The checkout.</param>
    /// <param name="runner">Its runner's name, for the description only.</param>
    /// <param name="profile">The profile to start under, or null for the default.</param>
    /// <param name="processPath">The running program, <c>Environment.ProcessPath</c>.</param>
    /// <param name="windows">Whether the item is for Windows, <c>OperatingSystem.IsWindows()</c>.</param>
    /// <param name="folder">The per-user hatch folder, which holds the log and the wrapper.</param>
    /// <returns>The item, or null - in which case <paramref name="refusal"/> says why.</returns>
    public static StandbyItem? For(
        string root, string runner, string? profile, string? processPath, bool windows, string folder,
        out string refusal)
    {
        refusal = "";

        var full = Path.GetFullPath(root);
        var program = Program(full, processPath, windows);
        if (program is null)
        {
            refusal = "hatch: there is no hatch program to start at login under the dotnet host; "
                + "run this from a published hatch, or from a Hatch checkout";
            return null;
        }

        var command = new List<string>(program) { "go-to-work", "--standby" };
        if (!string.IsNullOrEmpty(profile))
        {
            command.Add("--profile");
            command.Add(profile);
        }

        // The checkout's fingerprint and not the runner's name: renaming the
        // runner with `hatch config` must not leave a second item behind.
        var fingerprint = Checkout.Fingerprint(root);
        var id = "hatch-standby-" + fingerprint;

        return new StandbyItem(
            Id: id,
            Description: $"Hatch standby for {runner}",
            Root: full,
            Log: Path.Combine(folder, "logs", $"standby-{fingerprint}.log"),
            Wrapper: Path.Combine(folder, "standby", id + (windows ? ".cmd" : ".sh")),
            Command: command);
    }

    /// <summary>
    /// What starts hatch: the checkout's own script when this is a Hatch
    /// checkout, else the published binary that is running. Null where there is
    /// neither - the dotnet host, which is not a program anyone can start.
    /// </summary>
    private static string[]? Program(string root, string? processPath, bool windows)
    {
        var script = Path.Combine(root, "scripts", "hatch.sh");
        if (File.Exists(script) && Directory.Exists(Path.Combine(root, "src", "Hatch.Cli")))
        {
            return windows
                ? ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                    Path.Combine(root, "scripts", "hatch.ps1")]
                : ["bash", script];
        }

        if (string.IsNullOrEmpty(processPath)
            || string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return [processPath];
    }

    /// <summary>Where launchd looks for a user's agents.</summary>
    public string LaunchAgentPath(string home) =>
        Path.Combine(home, "Library", "LaunchAgents", Id + ".plist");

    /// <summary>
    /// The XDG config home: <paramref name="xdg"/> when it is an absolute path,
    /// which is all the specification lets it be, else <c>~/.config</c>.
    /// </summary>
    public static string ConfigHome(string? xdg, string home) =>
        !string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg) ? xdg : Path.Combine(home, ".config");

    /// <summary>Where systemd looks for a user's units.</summary>
    public string SystemdUnitPath(string configHome) =>
        Path.Combine(configHome, "systemd", "user", Id + ".service");

    /// <summary>Where a desktop session looks for what to start at login.</summary>
    public string AutostartPath(string configHome) =>
        Path.Combine(configHome, "autostart", Id + ".desktop");

    /// <summary>
    /// The batch file the task runs, CRLF. <c>&gt;</c> and not a file-handle
    /// redirect: it truncates at each start, where a handle would leave the
    /// output of a crash on disk.
    /// </summary>
    public static string WindowsWrapper(StandbyItem item) => Lines("\r\n",
        "@echo off",
        $"cd /d \"{Percent(item.Root)}\"",
        $"{string.Join(' ', item.Command.Select(BatchWord))} > \"{Percent(item.Log)}\" 2>&1");

    /// <summary>
    /// The shell script the unit or agent runs, LF. A login item starts with a
    /// bare <c>PATH</c> and the loop needs git, dotnet, make and the agent CLI,
    /// so the caller's is written into it.
    /// </summary>
    public static string PosixWrapper(StandbyItem item, string path) => Lines("\n",
        "#!/bin/sh",
        $"PATH={ShellWord(path, always: true)}; export PATH",
        $"cd {ShellWord(item.Root, always: true)} || exit 1",
        $"exec {string.Join(' ', item.Command.Select(arg => ShellWord(arg, always: false)))} > {ShellWord(item.Log, always: true)} 2>&1");

    /// <summary>
    /// The Task Scheduler definition, at logon for one user. Written UTF-16 by
    /// the caller; here it is a string.
    /// </summary>
    /// <remarks>
    /// The wrapper in <c>Arguments</c> is a command line and not a batch file,
    /// so a <c>%</c> there is not doubled. <c>conhost --headless</c> is what
    /// keeps a console program from opening a window.
    /// </remarks>
    public static string TaskXml(StandbyItem item, string domain, string user)
    {
        var account = Xml($@"{domain}\{user}");
        return Lines("\n",
            """<?xml version="1.0" encoding="UTF-16"?>""",
            """<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">""",
            $"  <RegistrationInfo><Description>{Xml(item.Description)}</Description></RegistrationInfo>",
            $"  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{account}</UserId></LogonTrigger></Triggers>",
            $"""  <Principals><Principal id="Author"><UserId>{account}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>""",
            "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><Enabled>true</Enabled><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings>",
            $"""  <Actions Context="Author"><Exec><Command>conhost.exe</Command><Arguments>--headless cmd.exe /d /c ""{Xml(item.Wrapper)}""</Arguments></Exec></Actions>""",
            "</Task>");
    }

    /// <summary>
    /// The launchd agent. Run at load and not kept alive: launchd starts it at
    /// login and does not start it again when it ends.
    /// </summary>
    public static string LaunchdPlist(StandbyItem item) => Lines("\n",
        """<?xml version="1.0" encoding="UTF-8"?>""",
        """"<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">"""",
        """<plist version="1.0">""",
        "<dict>",
        "  <key>Label</key>",
        $"  <string>{Xml(item.Id)}</string>",
        "  <key>ProgramArguments</key>",
        "  <array>",
        "    <string>/bin/sh</string>",
        $"    <string>{Xml(item.Wrapper)}</string>",
        "  </array>",
        "  <key>RunAtLoad</key>",
        "  <true/>",
        "</dict>",
        "</plist>");

    /// <summary>The systemd user unit.</summary>
    public static string SystemdUnit(StandbyItem item) => Lines("\n",
        "[Unit]",
        $"Description={item.Description.Replace("%", "%%", StringComparison.Ordinal)}",
        "",
        "[Service]",
        "Type=simple",
        $"ExecStart=/bin/sh \"{SystemdQuoted(item.Wrapper)}\"",
        "",
        "[Install]",
        "WantedBy=default.target");

    /// <summary>The freedesktop autostart entry, for a desktop with no user systemd.</summary>
    public static string DesktopEntry(StandbyItem item) => Lines("\n",
        "[Desktop Entry]",
        "Type=Application",
        $"Name={item.Description}",
        $"Exec=/bin/sh \"{DesktopQuoted(item.Wrapper)}\"");

    private static string Lines(string newline, params string[] lines) =>
        string.Concat(lines.Select(line => line + newline));

    private static string Percent(string text) => text.Replace("%", "%%", StringComparison.Ordinal);

    /// <summary>One argument of a batch command: bare when it is only safe characters, else quoted.</summary>
    private static string BatchWord(string arg) =>
        arg.Length > 0 && arg.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or ':' or '\\' or '/' or '-')
            ? arg
            : $"\"{Percent(arg)}\"";

    /// <summary>One shell word: bare when it is only safe characters, else single-quoted.</summary>
    private static string ShellWord(string arg, bool always) =>
        !always && arg.Length > 0
            && arg.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '/' or ':' or '@' or '+' or '=' or ',' or '-')
            ? arg
            : $"'{arg.Replace("'", @"'\''", StringComparison.Ordinal)}'";

    private static string Xml(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>Inside the double quotes of a systemd <c>ExecStart</c>.</summary>
    private static string SystemdQuoted(string text)
    {
        var quoted = new StringBuilder();
        foreach (var c in text)
        {
            quoted.Append(c switch
            {
                '\\' => @"\\",
                '"' => "\\\"",
                '$' => "$$",
                '%' => "%%",
                _ => c.ToString(),
            });
        }

        return quoted.ToString();
    }

    /// <summary>
    /// Inside the double quotes of a desktop entry's <c>Exec</c>: the quoting
    /// rule puts a backslash before <c>\ " ` $</c>, and then the key's own
    /// string escaping doubles every backslash that results.
    /// </summary>
    private static string DesktopQuoted(string text)
    {
        var quoted = new StringBuilder();
        foreach (var c in text)
        {
            quoted.Append(c switch
            {
                '\\' => @"\\\\",
                '"' or '`' or '$' => $@"\\{c}",
                '%' => "%%",
                _ => c.ToString(),
            });
        }

        return quoted.ToString();
    }
}

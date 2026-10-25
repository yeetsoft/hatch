namespace Hatch.Cli;

/// <summary>
/// Which named profile is active: switch to one, show it, or clear it.
/// </summary>
/// <remarks>
/// No board call and no checkout - it reads and writes two local files, so it
/// works with nothing configured, the way <see cref="ConfigCommand"/> does.
/// </remarks>
public sealed record UseCommand(Terminal Say)
{
    /// <summary>The directory of profile files. Named so a test can put one somewhere that is not the machine's own.</summary>
    public string ProfilesDirectory { get; init; } = Settings.ProfilesDirectory();

    /// <summary>The pointer file. Named so a test can put one somewhere that is not the machine's own.</summary>
    public string ActiveProfilePath { get; init; } = Settings.ActiveProfilePath();

    public static readonly string[] UseUsage = [
        "usage: hatch use [<name> | --none]",
        "",
        "  hatch use <name>   make <name> the active profile - refused if no profile",
        "                        file by that name exists",
        "  hatch use          print the active profile's name, or <none>",
        "  hatch use --none   clear the active profile",
    ];

    public int Run(string[] args)
    {
        if (Usage.Wanted(args)) return Usage.Print(Say, UseUsage);
        if (args is ["--none"]) return Clear();
        if (args.Length == 0) return Print();
        if (args is not [var name])
            return Usage.Refuse(Say, "use takes a profile name, nothing, or --none", UseUsage);

        // The same sentence Settings.TryResolveProfile refuses with, checked
        // here against this command's own directory. A leading hyphen passes the
        // name pattern and is caught by there being no file of that name.
        if (!Settings.ValidProfileName(name) || !File.Exists(Path.Combine(ProfilesDirectory, name)))
        {
            Say.Complain($"""hatch: no such profile "{name}" - looked in {ProfilesDirectory}""");
            return 1;
        }

        AtomicFile.Write(ActiveProfilePath, [name]);
        Say.Line(name);
        return 0;
    }

    private int Print()
    {
        Say.Line(ActiveName());
        return 0;
    }

    private int Clear()
    {
        File.Delete(ActiveProfilePath);
        Say.Line("<none>");
        return 0;
    }

    private string ActiveName() => Settings.ReadActiveProfile(ActiveProfilePath) ?? "<none>";
}

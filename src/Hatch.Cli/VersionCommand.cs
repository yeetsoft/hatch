namespace Hatch.Cli;

/// <summary>One line of JSON describing the build this process is running.</summary>
/// <param name="Revision">The full 40-character sha, or <see cref="CliRevision.Development"/>.</param>
/// <param name="Sequence">Commit count at build time, 0 for an unstamped build.</param>
/// <param name="Launcher">The launcher protocol version this build speaks, <see cref="Hatch.Cli.Launcher.Version"/>.</param>
public sealed record VersionReport(string Revision, int Sequence, int Launcher);

/// <summary>
/// What a launcher checking a downloaded build runs, and the whole reason it has
/// to exist: a build's own revision, read back from the binary itself rather
/// than trusted from whatever named the download.
/// </summary>
/// <remarks>
/// No board call and no checkout, so it has to work on a bare machine - a
/// build just downloaded, with nothing configured yet. Mirrors
/// <see cref="UseCommand"/> for the same reason.
/// </remarks>
public sealed record VersionCommand(Terminal Say, ICliRevision Revision)
{
    public static readonly string[] VersionUsage = [
        "usage: hatch version",
        "",
        "  hatch version   one line of JSON: this build's revision, sequence and launcher version",
    ];

    public int Run(string[] args)
    {
        if (Usage.Wanted(args)) return Usage.Print(Say, VersionUsage);
        if (args.Length > 0)
            return Usage.Refuse(Say, "version takes no arguments", VersionUsage);

        var report = new VersionReport(Revision.Revision, Revision.Sequence, Launcher.Version);
        Say.Line(System.Text.Json.JsonSerializer.Serialize(report, HatchJson.Default.VersionReport));
        return 0;
    }
}

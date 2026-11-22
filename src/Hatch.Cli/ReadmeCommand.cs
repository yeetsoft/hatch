namespace Hatch.Cli;

/// <summary>
/// The short readme Hatch ships for an agent that has never heard of it: printed,
/// or written into a checkout as <c>.hatch/README.md</c>.
/// </summary>
/// <remarks>
/// No key and no board call, so it answers on a machine with nothing set up yet,
/// like <see cref="VersionCommand"/>. Only <c>--file</c> needs a checkout. Whether
/// a copy already there is replaced is <see cref="ReadmeFile.Judge"/>'s rule.
/// </remarks>
public sealed class ReadmeCommand(Terminal say)
{
    public static readonly string[] ReadmeUsage =
    [
        "usage: hatch readme [--file]",
        "",
        "  hatch readme         print the readme: what Hatch is and how to file an issue",
        "  hatch readme --file  write it to .hatch/README.md at the root of this checkout,",
        "                       and print `written .hatch/README.md` or `unchanged .hatch/README.md`",
        "",
        "  A copy Hatch wrote with a lower version is replaced. One with the same or a",
        "  higher version is left. A file Hatch did not write is left, and the command",
        "  says so and exits 1.",
    ];

    public int Run(string[] args) => Run(args, Directory.GetCurrentDirectory());

    /// <summary>As the one above, from <paramref name="workingDirectory"/> rather than the process's own.</summary>
    public int Run(string[] args, string workingDirectory)
    {
        if (Usage.Wanted(args)) return Usage.Print(say, ReadmeUsage);

        if (args.Length == 0)
        {
            say.Lines(ReadmeFile.Text.TrimEnd('\n').Split('\n'));
            return 0;
        }

        if (args is not ["--file"])
            return Usage.Refuse(say, $"readme does not take \"{string.Join(' ', args)}\"", ReadmeUsage);

        var root = Checkouts.Toplevel(workingDirectory);
        if (root is null)
        {
            say.Complain("hatch: readme --file needs a checkout");
            return 1;
        }

        var target = System.IO.Path.GetFullPath(ReadmeFile.Path, root);
        switch (ReadmeFile.Judge(File.Exists(target) ? new System.Text.UTF8Encoding(false).GetString(File.ReadAllBytes(target)) : null))
        {
            case ReadmeVerdict.Foreign:
                say.Complain(ReadmeFile.ForeignSentence);
                return 1;

            case ReadmeVerdict.Keep:
                say.Line($"unchanged {ReadmeFile.Path}");
                return 0;

            default:
                AtomicFile.WriteText(target, ReadmeFile.Text);
                say.Line($"written {ReadmeFile.Path}");
                return 0;
        }
    }
}

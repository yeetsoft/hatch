namespace Hatch.Cli;

/// <summary>
/// The login item that starts this checkout's loop at sign-in: register it,
/// say what is registered, or forget it.
/// </summary>
/// <remarks>
/// No board call - it reads the checkout, the host's own registration and the
/// loop's lock. <c>remove</c> never touches the lock or a process: it forgets
/// the login item, which is a different thing from ending a loop that is
/// already running.
/// </remarks>
public sealed class StandbyCommand(Cli cli, string? profile, IStandbyHost host)
{
    public static readonly string[] StandbyUsage =
    [
        "usage: hatch standby [install | show | remove]",
        "",
        "  hatch standby install   register a login item that starts this checkout's loop",
        "                            at every sign-in; running it again says so and changes nothing",
        "  hatch standby show      whether it is installed, the command and log it uses, and the",
        "                            pid of the loop holding this checkout right now, if any",
        "  hatch standby remove    forget the login item - a loop already running keeps running",
        "  hatch standby           the same as show",
    ];

    /// <summary>Where the loop's lock lives. Named so a test can put it somewhere that is not the machine's own.</summary>
    public string TempDirectory { get; init; } = Path.GetTempPath();

    public Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Task.FromResult(Usage.Print(cli.Say, StandbyUsage));

        if (args is not ([] or ["show"] or ["install"] or ["remove"]))
            return Task.FromResult(Usage.Refuse(cli.Say, $"standby does not take \"{args[0]}\"", StandbyUsage));

        var root = cli.Checkouts.FirstOrDefault(c => c.Standing)?.Path;
        if (root is null)
            return Task.FromResult(Usage.Refuse(cli.Say, "run it inside a checkout", StandbyUsage));

        var folder = Path.GetDirectoryName(Settings.UserConfigPath())!;
        var item = StandbyItem.For(
            root, cli.RunnerName, profile, Environment.ProcessPath, OperatingSystem.IsWindows(), folder,
            out var refusal);
        if (item is null)
        {
            cli.Say.Complain(refusal);
            return Task.FromResult(1);
        }

        return Task.FromResult(args switch
        {
            ["install"] => Install(item),
            ["remove"] => Remove(item),
            _ => Show(item, root),
        });
    }

    private int Install(StandbyItem item)
    {
        var result = host.Install(item);
        switch (result.Outcome)
        {
            case StandbyOutcome.Refused:
                cli.Say.Complain(result.Refusal);
                return 1;
            case StandbyOutcome.Unchanged:
                cli.Say.Line("Standby is already installed for this checkout.");
                return 0;
            default:
                cli.Say.Line($"Registered a login item for {item.Root}.");
                cli.Say.Line($"  command: {string.Join(' ', item.Command)}");
                cli.Say.Line($"  log:     {item.Log}");
                cli.Say.Line("It starts at the next sign-in; `hatch go-to-work --standby` starts it now.");
                return 0;
        }
    }

    private int Remove(StandbyItem item)
    {
        var result = host.Remove(item);
        switch (result.Outcome)
        {
            case StandbyOutcome.Refused:
                cli.Say.Complain(result.Refusal);
                return 1;
            case StandbyOutcome.Unchanged:
                cli.Say.Line("Standby is not installed for this checkout; nothing to remove.");
                return 0;
            default:
                cli.Say.Line($"Removed the login item for {item.Root}.");
                cli.Say.Line("A loop already running is not stopped by this - only the login item goes away.");
                return 0;
        }
    }

    private int Show(StandbyItem item, string root)
    {
        cli.Say.Line(host.Installed(item)
            ? $"Standby is installed for {item.Root}."
            : $"Standby is not installed for {item.Root}.");
        cli.Say.Line($"  command: {string.Join(' ', item.Command)}");
        cli.Say.Line($"  log:     {item.Log}");
        cli.Say.Line(LoopLock.Holder(root, TempDirectory) is { } pid
            ? $"A loop holds this checkout now, pid {pid}."
            : "No loop holds this checkout now.");
        return 0;
    }
}

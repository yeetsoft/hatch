namespace Hatch.Cli;

/// <summary>
/// An issue's work kept off this checkout's own runner: <c>omit</c>, for the
/// name the checkout already resolved.
/// </summary>
/// <remarks>
/// Open to a key, as <see cref="OmitCommand"/> is. What a session calls on its
/// way out, when it finds the ticket is one this runner cannot do.
/// </remarks>
public sealed class RecuseCommand(Cli cli)
{
    public static readonly string[] RecuseUsage =
    [
        "usage: hatch recuse AER-12 \"reason\"",
        "",
        "  Omits this runner's own name from the issue, with that reason.",
        "",
        $"  The reason is required, and is at most {OmitRunnerRequest.MaxLength} characters. The dispatcher then never",
        "  hands AER-12 to this runner again.",
    ];

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(cli.Say, RecuseUsage);
        if (args.Length == 0) return Usage.Refuse(cli.Say, "recuse takes an issue key and a reason", RecuseUsage);
        if (args.Length != 2) return Usage.Refuse(cli.Say, NeedsReason, RecuseUsage);

        var key = args[0];
        var reason = args[1].Trim();
        if (reason.StartsWith("--", StringComparison.Ordinal))
            return Usage.Refuse(cli.Say, $"\"{args[1]}\" is not a recuse option - a reason", RecuseUsage);
        if (reason.Length == 0) return Usage.Refuse(cli.Say, NeedsReason, RecuseUsage);
        if (OmitCommand.CheckReason(reason) is { } tooLong) return Usage.Refuse(cli.Say, tooLong, RecuseUsage);

        if (string.IsNullOrWhiteSpace(cli.RunnerName))
        {
            cli.Say.Complain("hatch: this checkout has no runner name to recuse - set HATCH_RUNNER or run `hatch config`");
            return 1;
        }

        var issue = await cli.Board.OmitRunnerAsync(key, cli.RunnerName, reason, ct);
        if (issue is null)
        {
            cli.Say.Complain($"hatch: {key} - there is nothing there");
            return 1;
        }

        cli.Say.Line($"recused {cli.RunnerName} from {key}: {reason}");
        return 0;
    }

    private const string NeedsReason = "recuse needs a reason - say what this runner lacks";
}

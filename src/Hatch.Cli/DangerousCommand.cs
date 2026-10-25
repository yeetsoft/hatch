namespace Hatch.Cli;

/// <summary>
/// Whether running an issue's work could destabilise the machine that runs it,
/// read, set and cleared.
/// </summary>
/// <remarks>
/// Open to a key: a flag only ever makes a runner's operator more careful, and
/// nothing in it lets the loop advance its own ticket - see
/// <c>IssueDangerousController</c>. A task inherits its ancestors' flag, which
/// <see cref="Draw"/> names.
/// </remarks>
public sealed class DangerousCommand(Cli cli)
{
    public static readonly string[] DangerousUsage =
    [
        "usage: hatch dangerous AER-5 [\"reason\" | --clear]",
        "",
        "  hatch dangerous AER-5            whether it is flagged, and why",
        "  hatch dangerous AER-5 \"reason\"   flag it, for the operator of a runner to read",
        "  hatch dangerous AER-5 --clear    clear its own flag",
        "",
        $"  The reason is at most {DangerousRequest.MaxLength} characters. A task inherits the flag of the story",
        "  or epic above it, and `hatch show` names where it came from.",
    ];

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(cli.Say, DangerousUsage);
        if (args.Length is 0 or > 2) return Usage.Refuse(cli.Say, "dangerous takes an issue key and, to set it, a reason or --clear", DangerousUsage);

        var key = args[0];
        IssueDto? issue;

        // No second argument is a read, distinguished by the count and not the
        // value - so an empty reason is the mistake and not a silent clear,
        // the same rule `one-pr` and `pr` split on. Clearing is said as --clear.
        if (args.Length == 1)
        {
            issue = await cli.Board.IssueAsync(key, ct);
        }
        else if (args[1] == "--clear")
        {
            issue = await cli.Board.SetDangerousAsync(key, "", ct);
        }
        else
        {
            var reason = args[1].Trim();
            if (reason.StartsWith("--", StringComparison.Ordinal))
                return Usage.Refuse(cli.Say, $"\"{args[1]}\" is not a dangerous option - a reason or --clear", DangerousUsage);
            if (reason.Length == 0)
                return Usage.Refuse(cli.Say, "dangerous needs a reason; to clear the flag say --clear", DangerousUsage);
            if (reason.Length > DangerousRequest.MaxLength)
                return Usage.Refuse(
                    cli.Say, $"the reason is {reason.Length} characters; the limit is {DangerousRequest.MaxLength}", DangerousUsage);

            issue = await cli.Board.SetDangerousAsync(key, reason, ct);
        }

        if (issue is null)
        {
            cli.Say.Complain($"hatch: {key} - there is nothing there");
            return 1;
        }

        cli.Say.Line(Draw(issue) ?? "dangerous:    not flagged");
        return 0;
    }

    /// <summary>
    /// The effective reason, and the ancestor it came from when it is not the
    /// issue's own; null where nothing is flagged.
    /// </summary>
    public static string? Draw(IssueDto issue) =>
        string.IsNullOrEmpty(issue.Dangerous) ? null
        : issue.DangerousFrom is { Length: > 0 } from ? $"dangerous:    {issue.Dangerous} (from {from})"
        : $"dangerous:    {issue.Dangerous}";
}

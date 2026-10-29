namespace Hatch.Cli;

/// <summary>
/// Whether a story's or a bug's tasks share its branch and one pull request,
/// read and set.
/// </summary>
/// <remarks>
/// <para>The project holds a default, and an owner sets it in the browser. This
/// is the story's or bug's own say over it: <c>on</c> or <c>off</c> overrides the
/// default, <c>inherit</c> goes back to following it. Open to a key, because it
/// decides how a story or a bug is branched and nothing the loop could use to advance its
/// own ticket - see <c>IssueOnePrController</c>.</para>
///
/// <para>Locked once work under it has started: the server refuses a
/// change then, naming the task, because work begun on one branch cannot be moved
/// onto the other.</para>
/// </remarks>
public sealed class OnePrCommand(Cli cli)
{
    public static readonly string[] OnePrUsage =
    [
        "usage: hatch one-pr AER-5 [on|off|inherit]",
        "",
        "  hatch one-pr AER-5          whether its tasks share its branch and one PR",
        "  hatch one-pr AER-5 on       ...share them, whatever the project's default is",
        "  hatch one-pr AER-5 off      ...do not, whatever the project's default is",
        "  hatch one-pr AER-5 inherit  ...follow the project's default",
        "",
        "  A story or a bug only, and locked once any task under it has started - set it",
        "  when it is filed, before its tasks are worked.",
    ];

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(cli.Say, OnePrUsage);
        if (args.Length is 0 or > 2) return Usage.Refuse(cli.Say, "one-pr takes an issue key and, to set it, on, off or inherit", OnePrUsage);

        var key = args[0];
        IssueDto? issue;

        // No second argument is a read, distinguished by the count and not the
        // value - so an empty one is the mistake and not a silent read, the
        // same rule `depends` and `pr` split on.
        if (args.Length == 1)
        {
            issue = await cli.Board.IssueAsync(key, ct);
        }
        else if (args[1] is "on" or "off" or "inherit")
        {
            issue = await cli.Board.SetOnePrAsync(key, args[1], ct);
        }
        else
        {
            cli.Say.Complain($"hatch: \"{args[1]}\" is not a one-pr setting - on, off or inherit");
            return 1;
        }

        if (issue is null)
        {
            cli.Say.Complain($"hatch: {key} - there is nothing there");
            return 1;
        }

        cli.Say.Line(Draw(issue));
        return 0;
    }

    /// <summary>
    /// The effective value and where it comes from, in the shape <c>show</c>
    /// prints its other inherited lines.
    /// </summary>
    public static string Draw(IssueDto issue)
    {
        var value = issue.OnePr ? "on" : "off";
        var source = issue.OnePrOwn is not null ? $"set on this {issue.Type}"
            : issue.OnePrFrom is { Length: > 0 } from ? $"inherited from {from}"
            : issue.Type is "story" or "bug" ? "the project's default"
            : "not under a story or bug";

        return $"one-pr:   {value} ({source})";
    }
}

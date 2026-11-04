namespace Hatch.Cli;

/// <summary>
/// The runners kept off an issue's work, read, set and lifted.
/// </summary>
/// <remarks>
/// Open to a key, as <see cref="DangerousCommand"/> is. A task inherits its
/// ancestors' omissions, which <see cref="Draw"/> names, and which are lifted
/// on the ticket that carries them.
/// </remarks>
public sealed class OmitCommand(Cli cli)
{
    public static readonly string[] OmitUsage =
    [
        "usage: hatch omit AER-12 [<runner> \"reason\" | --lift <runner>]",
        "",
        "  hatch omit AER-12                    the runners omitted from it, and where each came from",
        "  hatch omit AER-12 <runner> \"reason\"  omit that runner from it",
        "  hatch omit AER-12 --lift <runner>    lift its own omission of that runner",
        "",
        $"  The reason is required here, and is at most {OmitRunnerRequest.MaxLength} characters. An omission inherited",
        "  from a story or epic is lifted on the ticket that carries it.",
    ];

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(cli.Say, OmitUsage);
        if (args.Length == 0) return Usage.Refuse(cli.Say, "omit takes an issue key", OmitUsage);

        var key = args[0];
        IssueDto? issue;

        if (args.Length == 1)
        {
            issue = await cli.Board.IssueAsync(key, ct);
        }
        else if (args[1] == "--lift")
        {
            if (args.Length != 3) return Usage.Refuse(cli.Say, "--lift needs a runner name", OmitUsage);

            var runner = args[2].Trim();
            if (runner.Length == 0) return Usage.Refuse(cli.Say, "--lift needs a runner name", OmitUsage);

            var (lifted, refusal) = await cli.Board.LiftOmissionAsync(key, runner, ct);
            if (refusal is not null)
            {
                cli.Say.Complain($"hatch: {refusal}");
                return 1;
            }

            issue = lifted;
        }
        else
        {
            var runner = args[1].Trim();
            if (runner.StartsWith("--", StringComparison.Ordinal))
                return Usage.Refuse(cli.Say, $"\"{args[1]}\" is not an omit option - a runner or --lift", OmitUsage);
            if (runner.Length == 0)
                return Usage.Refuse(cli.Say, "omit needs a runner name", OmitUsage);
            if (args.Length == 2)
                return Usage.Refuse(cli.Say, "omit needs a reason - a runner omitted with no reason teaches nobody", OmitUsage);
            if (args.Length > 3)
                return Usage.Refuse(cli.Say, "omit takes an issue key, a runner and one reason; quote the reason", OmitUsage);

            var reason = args[2].Trim();
            if (reason.Length == 0)
                return Usage.Refuse(cli.Say, "omit needs a reason - a runner omitted with no reason teaches nobody", OmitUsage);
            if (reason.Length > OmitRunnerRequest.MaxLength)
                return Usage.Refuse(
                    cli.Say, $"the reason is {reason.Length} characters; the limit is {OmitRunnerRequest.MaxLength}", OmitUsage);

            issue = await cli.Board.OmitRunnerAsync(key, runner, reason, ct);
        }

        if (issue is null)
        {
            cli.Say.Complain($"hatch: {key} - there is nothing there");
            return 1;
        }

        cli.Say.Lines(Draw(issue, cli.Clock.GetUtcNow()));
        return 0;
    }

    /// <summary>
    /// The issue's effective omitted runners, one line each, with the ancestor
    /// that carries one when it is not the issue's own.
    /// </summary>
    public static IReadOnlyList<string> Draw(IssueDto issue, DateTimeOffset now)
    {
        if (issue.OmittedRunners is not { Count: > 0 } omitted) return ["omitted runners: none"];

        List<string> lines = ["omitted runners:"];
        lines.AddRange(omitted.Select(o =>
            $"  {o.Runner} - {o.Reason ?? "no reason given"} - {o.By}, {Format.Ago(now - o.At)}"
            + (string.IsNullOrEmpty(o.FromKey) ? "" : $" (from {o.FromKey})")));
        return lines;
    }
}

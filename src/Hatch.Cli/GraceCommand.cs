namespace Hatch.Cli;

/// <summary>
/// Asks that a claim not be called quiet for a while, ahead of a command the
/// session knows will say nothing that long.
/// </summary>
/// <remarks>
/// <para>A claim is judged quiet when nothing has been said on it for
/// <c>Hatch:StallLapseMinutes</c>, and a UI suite or a long build says nothing
/// from start to finish. The window is install-wide and small on purpose, so
/// the slow command says so itself, on its own ticket, for as long as it needs
/// - and the allowance ends with the claim.</para>
///
/// <para>Open to a key, and keyed on the ticket and not the lease: the caller
/// is the session a dispatcher spawned, which knows its ticket and not the
/// token the dispatcher holds. The server bounds the ask and refuses it on a
/// claim that is not live - see <c>IssueClaimController.Grace</c>.</para>
/// </remarks>
public sealed class GraceCommand(Cli cli)
{
    public static readonly string[] GraceUsage =
    [
        "usage: hatch grace AER-12 <minutes>",
        "",
        "  hatch grace AER-12 20   AER-12's claim is not called quiet for the next 20 minutes",
        "",
        "  For a command that will say nothing for longer than the quiet window - a UI",
        "  suite, a long build. Run it before the command, not after. It ends with the",
        "  claim, a smaller ask never shortens one already given, and the server names",
        "  the most it allows when the ask is above it.",
    ];

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(cli.Say, GraceUsage);
        if (args.Length != 2) return Usage.Refuse(cli.Say, "grace takes an issue key and a number of minutes", GraceUsage);

        var key = args[0];
        if (!int.TryParse(args[1], out var minutes) || minutes <= 0)
        {
            cli.Say.Complain($"hatch: \"{args[1]}\" is not a number of minutes above zero");
            return 1;
        }

        var path = $"/api/hatch/issues/{key}/claim/grace";
        var answer = await cli.Board.Client.Send(HttpMethod.Post, path, new ClaimGraceRequest(minutes), ct);
        if (!answer.Ok)
        {
            cli.Say.Complain(cli.Board.Client.Refusal(answer, path));
            return 1;
        }

        cli.Say.Line($"{key}: not quiet for the next {minutes} minutes");
        return 0;
    }
}

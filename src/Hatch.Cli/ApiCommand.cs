namespace Hatch.Cli;

/// <summary>
/// The raw passthrough: a method, a path, and a body that is already JSON.
/// </summary>
/// <remarks>
/// Fifteen commands are the calls a working session actually makes. This one is
/// everything else - filing an issue, setting a date, reading the plan - because
/// the alternative to one honest escape hatch is fifty thin wrappers, each of
/// which is a second place the API is written down.
///
/// It goes through <see cref="HatchClient.Send"/> rather than the throwing
/// reads, since here a non-2xx is the answer and not a fault: somebody asking
/// what a route says wants to see what it said.
/// </remarks>
public sealed class ApiCommand(Cli cli)
{
    public static readonly string[] ApiUsage =
    [
        "usage: hatch api <METHOD> <path> [<json body> | -]",
        "",
        "  hatch api GET /api/issues?statusId=2",
        "  hatch api PATCH /api/issues/AER-12 '{\"dueAt\":\"2026-10-01\"}'",
        "  echo '{\"dueAt\":\"2026-10-01\"}' | hatch api PATCH /api/issues/AER-12 -",
        "",
        "  The body is sent as it was typed. A body of - is read from standard input",
        "  instead, which keeps its double quotes in Windows PowerShell 5.1, where an",
        "  argument loses them. docs/hatch-planning.md has the shapes for filing and",
        "  reshaping work.",
    ];

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(cli.Say, ApiUsage);
        if (args.Length is < 2 or > 3) return Usage.Refuse(cli.Say, "api takes a method, a path and a body", ApiUsage);

        var method = new HttpMethod(args[0].ToUpperInvariant());
        var refusal = RoutePath.Read(args[1], out var path);
        if (refusal is not null)
        {
            cli.Say.Complain(refusal);
            return 1;
        }

        var text = args.Length == 3 ? args[2] : "";
        if (text == "-")
        {
            if (cli.In.Interactive)
            {
                cli.Say.Complain("hatch: api - reads the body from standard input, and none was piped");
                return 1;
            }

            text = cli.In.ReadAll().TrimEnd('\r', '\n');
        }

        object? body = text.Length > 0 ? new RawJson(text) : null;

        var answer = await cli.Board.Client.Send(method, path, body, ct);

        if (!answer.Ok)
        {
            cli.Say.Complain(cli.Board.Client.Refusal(answer, path));
            return 1;
        }

        if (answer.Body.Length > 0) cli.Say.Lines(answer.Body.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'));
        return 0;
    }
}

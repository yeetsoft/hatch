namespace Hatch.Cli;

/// <summary>
/// Every runner that has spoken to this Hatch lately, most recently heard from
/// first - the terminal's own reading of <c>GET /api/hatch/runners</c>.
/// </summary>
/// <remarks>
/// Nothing here re-litigates the endpoint: the ordering and the drop horizon
/// are its own (<c>src/Hatch.Api/Modules/Hatch/RunnersController.cs</c>). The
/// state word is this command's own, computed off the same facts
/// <c>runnerActivity</c> reads (<c>apps/hatch/src/lib/runners.ts</c>),
/// translated to the DTO's own field names.
/// </remarks>
public sealed class RunnersCommand(Cli cli)
{
    public static readonly string[] RunnersUsage =
    [
        "usage: hatch runners [--json]",
        "",
        "  One line per runner that has spoken to this Hatch lately, most recently",
        "  heard from first: its name, what it is doing, the ticket it holds (or",
        "  none), and how long ago it last heartbeat. A caller with nothing to see",
        "  prints one sentence saying so.",
        "",
        "  --json           the endpoint's own answer, and nothing else",
    ];

    public const string EmptyWords = "No runner this caller can see has spoken to this Hatch lately.";

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(cli.Say, RunnersUsage);

        var json = false;

        foreach (var arg in args)
        {
            if (arg == "--json") { json = true; continue; }

            return Usage.Refuse(cli.Say, $"runners does not take \"{arg}\"", RunnersUsage);
        }

        if (json)
        {
            const string path = "/api/hatch/runners";
            var answer = await cli.Board.Client.Send(HttpMethod.Get, path, null, ct);
            if (!answer.Ok)
            {
                cli.Say.Complain(cli.Board.Client.Refusal(answer, path));
                return 1;
            }

            if (answer.Body.Length > 0) cli.Say.Lines(answer.Body.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'));
            return 0;
        }

        var rows = await cli.Board.RunnersAsync(ct);
        if (rows.Count == 0)
        {
            cli.Say.Line(EmptyWords);
            return 0;
        }

        foreach (var line in Render(rows, cli.Clock.GetUtcNow())) cli.Say.Line(line);
        return 0;
    }

    private static IReadOnlyList<string> Render(IReadOnlyList<RunnerDto> rows, DateTimeOffset now)
    {
        var table = new List<(string Runner, string State, string Ticket, string Heard)> { ("runner", "state", "ticket", "heard") };
        table.AddRange(rows.Select(r => (r.Name, State(r, now), r.ClaimKey ?? "none", Format.Ago(now - r.LastSeenAt) + Bound(r))));

        var runnerWidth = table.Max(r => r.Runner.Length);
        var stateWidth = table.Max(r => r.State.Length);
        var ticketWidth = table.Max(r => r.Ticket.Length);

        return table
            .Select(r => r.Runner.PadRight(runnerWidth) + "  " + r.State.PadRight(stateWidth) + "  " + r.Ticket.PadRight(ticketWidth) + "  " + r.Heard)
            .ToList();
    }

    /// <summary>
    /// The projects a runner is bound to, closing its row; nothing where it is
    /// bound to none, so a row without them reads as it always has.
    /// </summary>
    private static string Bound(RunnerDto row) =>
        row.Projects.Count > 0 ? $"  projects {string.Join(" ", row.Projects)}" : "";

    /// <summary>
    /// The state word, in the order <c>runnerActivity</c> reads it - silence
    /// beats everything, then a fact the runner reports about its own account,
    /// then what it was asked to do, then what it is holding.
    /// </summary>
    private static string State(RunnerDto row, DateTimeOffset now) =>
        (now - row.LastSeenAt).TotalSeconds > row.GoneAfterSeconds ? "gone"
        : row.ExhaustedUntil is { } u && u > now ? "out of usage"
        : row.State == "paused" ? "paused"
        : row.State == "stopping" ? "stopping"
        : row.ClaimKey is not null ? "working"
        : "idle";
}

using System.Globalization;

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
        "  none), and how long ago it last heartbeat. A row closes with the projects",
        "  it is bound to and the models it advertises, where it has any. The row",
        "  is followed by one indented line per live session beneath it: where,",
        "  agent, OS, state. A caller with nothing to see prints one sentence",
        "  saying so.",
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
            const string path = "/api/hatch/runners?live=true";
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
        table.AddRange(rows.Select(r => (r.Name, State(r.LastSeenAt, r.GoneAfterSeconds, r.ExhaustedUntil, r.State, r.ClaimKey, now), r.ClaimKey ?? "none", Format.Ago(now - r.LastSeenAt) + Facts(r) + Has(r) + Bound(r) + Models(r))));

        var runnerWidth = table.Max(r => r.Runner.Length);
        var stateWidth = table.Max(r => r.State.Length);
        var ticketWidth = table.Max(r => r.Ticket.Length);

        var lines = new List<string>();
        for (var i = 0; i < table.Count; i++)
        {
            var r = table[i];
            lines.Add(r.Runner.PadRight(runnerWidth) + "  " + r.State.PadRight(stateWidth) + "  " + r.Ticket.PadRight(ticketWidth) + "  " + r.Heard);
            if (i > 0) lines.AddRange(SessionLines(rows[i - 1], now));
        }

        return lines;
    }

    /// <summary>
    /// One indented line per live session beneath a runner's own row - where,
    /// agent, OS, state, in that order, column-aligned over this runner's live
    /// sessions alone. A session is live when it carries no end and has not
    /// fallen past its runner's own gone horizon, since the in-memory provider
    /// skips the lazy <c>EndedAt</c> write the server makes on this very read.
    /// </summary>
    private static IEnumerable<string> SessionLines(RunnerDto row, DateTimeOffset now)
    {
        var live = row.Sessions
            .Where(s => s.EndedAt is null)
            .Select(s => (Where: s.Where ?? "-", Agent: s.Agent ?? "-", Os: s.Os ?? "-", State: State(s.LastSeenAt, row.GoneAfterSeconds, s.ExhaustedUntil, s.State, s.ClaimKey, now)))
            .Where(s => s.State != "gone")
            .ToList();

        if (live.Count == 0) yield break;

        var whereWidth = live.Max(s => s.Where.Length);
        var agentWidth = live.Max(s => s.Agent.Length);
        var osWidth = live.Max(s => s.Os.Length);

        foreach (var s in live)
            yield return "  " + s.Where.PadRight(whereWidth) + "  " + s.Agent.PadRight(agentWidth) + "  " + s.Os.PadRight(osWidth) + "  " + s.State;
    }

    /// <summary>
    /// What a runner reports about its own process - the machine, the increments
    /// run and the dollars spent - each only where it has said so, so a row from
    /// an older runner reads as it always has.
    /// </summary>
    private static string Facts(RunnerDto row)
    {
        var parts = new List<string>();
        if (row.Host is { Length: > 0 } host) parts.Add($"on {host}");
        if (row.Image is { Length: > 0 } image) parts.Add($"image {image}");
        if (row.Runs is { } runs) parts.Add($"{runs} runs");
        if (row.Spent is { } spent) parts.Add(string.Create(CultureInfo.InvariantCulture, $"${spent:F2}"));
        if (row.TokensUsed is { } used)
            parts.Add($"{Format.Compact(used)} tokens" + (row.TokensLastHour is { } lastHour ? $" ({Format.Compact(lastHour)} last hour)" : ""));
        return parts.Count > 0 ? "  " + string.Join(" ", parts) : "";
    }

    /// <summary>
    /// The dependencies a runner says it has, each entry's <c>id@version</c>
    /// rendered <c>id version</c> and comma-joined; nothing where it has said
    /// none, so a row without them reads as it always has.
    /// </summary>
    private static string Has(RunnerDto row)
    {
        if (row.Has.Count == 0) return "";
        var entries = row.Has.Select(entry =>
        {
            var at = entry.IndexOf('@');
            return at == -1 ? entry : $"{entry[..at]} {entry[(at + 1)..]}";
        });
        return "  has " + string.Join(", ", entries);
    }

    /// <summary>
    /// The projects a runner is bound to, closing its row; nothing where it is
    /// bound to none, so a row without them reads as it always has.
    /// </summary>
    private static string Bound(RunnerDto row) =>
        row.Projects.Count > 0 ? $"  projects {string.Join(" ", row.Projects)}" : "";

    /// <summary>
    /// The models a runner advertises, closing its row after its projects;
    /// nothing where it advertises none.
    /// </summary>
    private static string Models(RunnerDto row) =>
        row.Models.Count > 0 ? $"  models {string.Join(" ", row.Models)}" : "";

    /// <summary>
    /// The state word, in the order <c>runnerActivity</c> reads it - silence
    /// beats everything, then a fact the runner reports about its own account,
    /// then what it was asked to do, then what it is holding. Shared between a
    /// runner's own row and each of its sessions, which carry the same facts
    /// under their own names.
    /// </summary>
    private static string State(DateTimeOffset lastSeenAt, int goneAfterSeconds, DateTimeOffset? exhaustedUntil, string state, string? claimKey, DateTimeOffset now) =>
        (now - lastSeenAt).TotalSeconds > goneAfterSeconds ? "gone"
        : exhaustedUntil is { } u && u > now ? "out of usage"
        : state == "paused" ? "paused"
        : state == "stopping" ? "stopping"
        : claimKey is not null ? "working"
        : "idle";
}

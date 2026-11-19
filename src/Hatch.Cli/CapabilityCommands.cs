using System.Globalization;

namespace Hatch.Cli;

/// <summary>
/// An ambition's capabilities, the verdicts on them, and the epics that serve them.
/// </summary>
/// <remarks>
/// <para>A capability is a thing that has to be true, with a line saying how
/// anyone would tell. It is named by its own id, never by its position: a
/// position moves whenever a neighbour is added or taken away, and a session
/// that typed <c>3</c> a minute ago may now mean a different one.</para>
///
/// <para>Not <c>hatch verdict</c>, which is a check session's pass or fail on
/// the ticket it is looking at. A verdict here is whether a capability holds,
/// as of a commit.</para>
///
/// <para>A server refusal is a <see cref="HatchException"/> and is left to
/// <c>Program</c>, which prints it as it came and exits 1.</para>
/// </remarks>
/// <param name="directory">
/// Where <c>judge</c> looks for the checkout when no <c>--sha</c> is given;
/// the working directory when null. It is how a test stands in a checkout
/// without a flag the operator never needs.
/// </param>
public sealed class CapabilityCommands(Cli cli, string? directory = null)
{
    private static readonly string[] Verdicts = ["holds", "does_not_hold", "cannot_tell"];

    public static readonly string[] CapabilityUsage =
    [
        "usage: hatch capability list <ambition>",
        "       hatch capability add <ambition> \"<statement>\" --how \"<how to tell>\" [--at <position>]",
        "       hatch capability edit <ambition> <id> [--statement \"...\"] [--how \"...\"]",
        "       hatch capability move <ambition> <id> <position>",
        "       hatch capability remove <ambition> <id>",
        "       hatch capability judge <ambition> <id> holds|does_not_hold|cannot_tell",
        "                              --looked-at \"...\" [--sha <sha>]",
        "",
        "  What has to be true of an ambition, and whether it is. Each capability is",
        "  named by its own id, which `list` prints after the #; a position is where",
        "  it stands now and moves as others come and go.",
        "",
        "  list    every capability: its state, how to tell, the newest verdict, and",
        "          the epics serving it",
        "  add     --how says how anyone would tell; --at puts it at a position (1 to",
        "          one past the last), and left out it goes at the end",
        "  edit    changes the statement, the how-to-tell line, or both",
        "  move    puts it at a position; the others close up around it",
        "  remove  takes it off the ambition",
        "  judge   records whether it holds as of a commit. --looked-at says what you",
        "          read or ran. --sha names the commit; without it, the head of the",
        "          checkout you are standing in, and outside one it is refused.",
        "",
        "  A key is refused by add, edit, move and remove once the ambition has",
        "  reached the column where code is written. judge never is.",
        "",
        "  hatch verdict is something else: a check session's pass or fail.",
    ];

    public static readonly string[] ServesUsage =
    [
        "usage: hatch serves <epic>",
        "       hatch serves <epic> <id> [<id>...]",
        "       hatch serves <epic> --none",
        "",
        "  The capabilities an epic serves.",
        "",
        "  hatch serves AER-5          what it serves now",
        "  hatch serves AER-5 7 9      it serves #7 and #9, and nothing else",
        "  hatch serves AER-5 --none   ...serves nothing",
        "",
        "  The ids are a capability's own, printed by `hatch capability list`. Naming",
        "  ids replaces the whole set, so name every one the epic should serve.",
        "  A key is never refused this.",
    ];

    public async Task<int> CapabilityAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(cli.Say, CapabilityUsage);
        if (args.Length == 0) return Usage.Refuse(cli.Say, "capability takes a verb", CapabilityUsage);

        var verb = args[0];
        var rest = args[1..];
        if (Usage.Wanted(rest)) return Usage.Print(cli.Say, CapabilityUsage);

        return verb switch
        {
            "list" => await ListAsync(rest, ct),
            "add" => await AddAsync(rest, ct),
            "edit" => await EditAsync(rest, ct),
            "move" => await MoveAsync(rest, ct),
            "remove" => await RemoveAsync(rest, ct),
            "judge" => await JudgeAsync(rest, ct),
            _ => Usage.Refuse(cli.Say, $"capability does not do {verb}", CapabilityUsage),
        };
    }

    private async Task<int> ListAsync(string[] args, CancellationToken ct)
    {
        if (Parse("list", args, ["ambition"], []) is not { } given) return 1;

        var key = given.Positional[0];
        var capabilities = await cli.Board.CapabilitiesAsync(key, ct);
        if (capabilities.Count == 0)
        {
            cli.Say.Line($"{key} has no capabilities");
            return 0;
        }

        foreach (var capability in capabilities) cli.Say.Lines(Draw(capability));
        return 0;
    }

    private async Task<int> AddAsync(string[] args, CancellationToken ct)
    {
        if (Parse("add", args, ["ambition", "statement"], ["--how", "--at"]) is not { } given) return 1;
        if (given.Flags.GetValueOrDefault("--how") is not { Length: > 0 } how)
            return Usage.Refuse(cli.Say, "capability add needs --how, the way anyone would tell", CapabilityUsage);

        var statement = given.Positional[1];
        int? at = null;
        if (given.Flags.TryGetValue("--at", out var typed))
        {
            if (!int.TryParse(typed, NumberStyles.None, CultureInfo.InvariantCulture, out var position) || position < 1)
                return Usage.Refuse(cli.Say, "--at takes a position, 1 or more", CapabilityUsage);
            at = position;
        }

        var added = await cli.Board.AddCapabilityAsync(
            given.Positional[0], new CapabilityCreateRequest(statement, how, at), ct);
        if (added is not null) cli.Say.Line(DrawLine(added));
        return 0;
    }

    private async Task<int> EditAsync(string[] args, CancellationToken ct)
    {
        if (Parse("edit", args, ["ambition", "id"], ["--statement", "--how"]) is not { } given) return 1;
        if (!Id(given.Positional[1], out var id)) return 1;

        var statement = given.Flags.GetValueOrDefault("--statement");
        var how = given.Flags.GetValueOrDefault("--how");
        if (statement is null && how is null)
            return Usage.Refuse(cli.Say, "capability edit needs --statement or --how", CapabilityUsage);

        var edited = await cli.Board.EditCapabilityAsync(
            given.Positional[0], id, new CapabilityPatchRequest(statement, how), ct);
        if (edited is not null) cli.Say.Line(DrawLine(edited));
        return 0;
    }

    /// <summary>
    /// The server has no move of its own - it takes the whole order. So the list
    /// is read, the one id is taken out and put back at the position, and what
    /// results is sent. A position the list cannot hold, or an id it does not
    /// have, is refused here rather than as a 400 for a body this built.
    /// </summary>
    private async Task<int> MoveAsync(string[] args, CancellationToken ct)
    {
        if (Parse("move", args, ["ambition", "id", "position"], []) is not { } given) return 1;
        if (!Id(given.Positional[1], out var id)) return 1;
        if (!int.TryParse(given.Positional[2], NumberStyles.None, CultureInfo.InvariantCulture, out var position) || position < 1)
            return Usage.Refuse(cli.Say, "capability move takes a position, 1 or more", CapabilityUsage);

        var key = given.Positional[0];
        var ids = (await cli.Board.CapabilitiesAsync(key, ct)).Select(c => c.Id).ToList();
        if (!ids.Remove(id))
        {
            cli.Say.Complain($"hatch: capability {id} is not on {key}");
            return 1;
        }
        if (position > ids.Count + 1)
        {
            cli.Say.Complain($"hatch: {key} has {ids.Count + 1} capabilities - there is no position {position}");
            return 1;
        }

        ids.Insert(position - 1, id);
        await cli.Board.OrderCapabilitiesAsync(key, ids, ct);
        cli.Say.Line($"{key}: capability {id} now at position {position}");
        return 0;
    }

    private async Task<int> RemoveAsync(string[] args, CancellationToken ct)
    {
        if (Parse("remove", args, ["ambition", "id"], []) is not { } given) return 1;
        if (!Id(given.Positional[1], out var id)) return 1;

        var key = given.Positional[0];
        await cli.Board.RemoveCapabilityAsync(key, id, ct);
        cli.Say.Line($"{key}: removed capability {id}");
        return 0;
    }

    private async Task<int> JudgeAsync(string[] args, CancellationToken ct)
    {
        if (Parse("judge", args, ["ambition", "id", "verdict"], ["--looked-at", "--sha"]) is not { } given) return 1;
        if (!Id(given.Positional[1], out var id)) return 1;

        var word = given.Positional[2];
        if (!Verdicts.Contains(word))
            return Usage.Refuse(cli.Say, $"{word} is not a verdict - holds, does_not_hold or cannot_tell", CapabilityUsage);
        if (given.Flags.GetValueOrDefault("--looked-at") is not { Length: > 0 } lookedAt)
            return Usage.Refuse(cli.Say, "capability judge needs --looked-at, what you read or ran", CapabilityUsage);

        var sha = given.Flags.GetValueOrDefault("--sha");
        if (sha is null)
        {
            var dir = directory ?? Directory.GetCurrentDirectory();
            if (Checkouts.Toplevel(dir) is not { Length: > 0 } root)
            {
                cli.Say.Complain($"hatch: {dir} is not inside a git checkout - name the commit with --sha");
                return 1;
            }

            sha = new Workspace(root, null, cli.Say.Line, cli.Say.Complain).Head();
            if (sha is null)
            {
                cli.Say.Complain("hatch: HEAD does not resolve to a commit here");
                return 1;
            }
        }

        var key = given.Positional[0];
        var judged = await cli.Board.JudgeCapabilityAsync(key, id, new CapabilityVerdictRequest(word, lookedAt, sha), ct);
        cli.Say.Line($"{key}: capability {id} judged {judged?.Verdict ?? word} at {Short(judged?.Sha ?? sha)}");
        return 0;
    }

    /// <summary>
    /// What an epic serves: read with no ids, replaced with some, cleared with
    /// <c>--none</c>. The count of arguments picks the read, as
    /// <c>depends</c> does, so a typo is never a silent read.
    /// </summary>
    public async Task<int> ServesAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(cli.Say, ServesUsage);
        if (args.Length == 0) return Usage.Refuse(cli.Say, "serves takes an epic key", ServesUsage);

        var epic = args[0];
        if (epic.StartsWith('-')) return Usage.Refuse(cli.Say, $"serves does not take {epic} before the epic", ServesUsage);

        var none = false;
        var ids = new List<long>();
        foreach (var arg in args.Skip(1))
        {
            if (arg == "--none") none = true;
            else if (arg.StartsWith('-')) return Usage.Refuse(cli.Say, $"serves does not take {arg}", ServesUsage);
            else if (long.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0) ids.Add(id);
            else return Usage.Refuse(cli.Say, $"{arg} is not a capability id - the number after the # in `hatch capability list`", ServesUsage);
        }
        if (none && ids.Count > 0)
            return Usage.Refuse(cli.Say, "serves takes capability ids or --none, not both", ServesUsage);

        var serves = none || ids.Count > 0
            ? await cli.Board.SetServesAsync(epic, ids, ct)
            : await cli.Board.ServesAsync(epic, ct);

        if (serves.Count == 0)
        {
            cli.Say.Line($"{epic} serves nothing");
            return 0;
        }

        cli.Say.Line($"{epic} serves {serves.Count} {(serves.Count == 1 ? "capability" : "capabilities")}:");
        foreach (var s in serves)
            cli.Say.Line($"  {s.AmbitionKey} #{s.CapabilityId}  {s.State}  {Folded(s.Statement)}");
        return 0;
    }

    /// <summary>
    /// The capability as <c>list</c> prints it, one line: where it stands, its
    /// own id, its state and what has to be true.
    /// </summary>
    public static string DrawLine(CapabilityDto c) =>
        $"{c.Position}  #{c.Id}  {c.State}  {Folded(c.Statement)}";

    /// <summary>The line and what goes under it, which only <c>list</c> has room for.</summary>
    public static IReadOnlyList<string> Draw(CapabilityDto c)
    {
        var verdict = c.NewestVerdict is { } v
            ? $"verdict: {v.Verdict} at {Short(v.Sha)} by {v.Author}, " +
              $"{v.At.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} - {Folded(v.LookedAt)}"
            : "verdict: none yet";
        var servedBy = c.ServedBy.Count > 0 ? string.Join(", ", c.ServedBy) : "no epic";

        return
        [
            DrawLine(c),
            $"    how to tell: {Folded(c.HowToTell)}",
            $"    {verdict}",
            $"    served by: {servedBy}",
        ];
    }

    /// <summary>
    /// The <c>serves:</c> line of <c>show</c>: each capability by its ambition
    /// and id, and its state in brackets.
    /// </summary>
    public static string ServesLine(IEnumerable<ServedCapabilityDto> serves) =>
        $"serves:   {string.Join(", ", serves.Select(s => $"{s.AmbitionKey} #{s.CapabilityId} ({s.State})"))}";

    private static string Short(string sha) => sha.Length > 7 ? sha[..7] : sha;

    private static string Folded(string text) => text.ReplaceLineEndings(" ");

    private bool Id(string typed, out long id)
    {
        if (long.TryParse(typed, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0) return true;

        Usage.Refuse(cli.Say, $"{typed} is not a capability id - the number after the # in `hatch capability list`", CapabilityUsage);
        return false;
    }

    private sealed record Given(List<string> Positional, Dictionary<string, string> Flags);

    /// <summary>
    /// Splits one verb's arguments into what it was given and what it was told,
    /// refusing - before anything is sent - an unknown flag, a flag with no
    /// value or a blank one, a flag said twice, or the wrong number of
    /// positional arguments, named by <paramref name="names"/>.
    /// Null means the refusal has been printed.
    /// </summary>
    private Given? Parse(string verb, string[] args, string[] names, string[] flags)
    {
        var given = new Given([], []);
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case var flag when flags.Contains(flag) && i + 1 < args.Length && args[i + 1].Trim().Length > 0:
                    if (!given.Flags.TryAdd(flag, args[++i]))
                    {
                        Usage.Refuse(cli.Say, $"{flag} is said twice", CapabilityUsage);
                        return null;
                    }
                    break;

                case var flag when flags.Contains(flag):
                    Usage.Refuse(cli.Say, $"{flag} needs a value", CapabilityUsage);
                    return null;

                case var flag when flag.StartsWith('-'):
                    Usage.Refuse(cli.Say, $"capability {verb} does not take {flag}", CapabilityUsage);
                    return null;

                default:
                    given.Positional.Add(args[i]);
                    break;
            }
        }

        if (given.Positional.Count != names.Length || given.Positional.Any(p => p.Length == 0))
        {
            Usage.Refuse(cli.Say, $"capability {verb} takes {string.Join(" ", names.Select(n => $"<{n}>"))}", CapabilityUsage);
            return null;
        }

        return given;
    }
}

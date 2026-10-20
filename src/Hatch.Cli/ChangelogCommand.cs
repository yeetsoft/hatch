using System.Globalization;

namespace Hatch.Cli;

/// <summary>
/// What shipped, printed as markdown - the terminal and a Claude session's own
/// reading of the page at <c>/changelog</c>.
/// </summary>
/// <remarks>
/// Nothing here re-litigates the endpoint: the window, the day-counting and the
/// feature/fix split are <c>GET /api/hatch/changelog</c>'s own
/// (<c>src/Hatch.Api/Modules/Hatch/Changelog.cs</c>). This prints what it was
/// served, in the same words the page already uses - <c>fixLine</c> and
/// <c>beneathLabel</c>, mirrored rather than reinvented.
/// </remarks>
public sealed class ChangelogCommand(Cli cli)
{
    public static readonly string[] ChangelogUsage =
    [
        "usage: hatch changelog [--days <n>] [--before <date>] [--project <KEY>] [--json]",
        "",
        "  What shipped, as markdown: a heading per day, a bullet per feature",
        "  carrying how much shipped beneath it, and an Also list of that day's",
        "  fixes. An empty window prints one sentence saying so.",
        "",
        "  --days <n>       the newest <n> days that shipped something - not the",
        "                   last <n> calendar days. Seven by default.",
        "  --before <date>  move the window back to end before this instant",
        "  --project <KEY>  narrow it to one project; a key nobody has is refused",
        "  --json           the endpoint's own answer, and nothing else",
    ];

    private const int DefaultDays = 7;

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(cli.Say, ChangelogUsage);

        var days = DefaultDays;
        string? before = null;
        string? projectKey = null;
        var json = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--json") { json = true; continue; }

            if (arg == "--days")
            {
                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out days) || days < 1)
                    return Usage.Refuse(cli.Say, "--days takes a whole number of at least one", ChangelogUsage);
                i++;
                continue;
            }

            if (arg == "--before")
            {
                if (i + 1 >= args.Length || args[i + 1].Length == 0)
                    return Usage.Refuse(cli.Say, "--before takes an instant", ChangelogUsage);
                before = args[++i];
                continue;
            }

            if (arg == "--project")
            {
                if (i + 1 >= args.Length || args[i + 1].Length == 0)
                    return Usage.Refuse(cli.Say, "--project takes a key", ChangelogUsage);
                projectKey = args[++i];
                continue;
            }

            return Usage.Refuse(cli.Say, $"changelog does not take \"{arg}\"", ChangelogUsage);
        }

        // The route itself takes only an id, the way every route does; hatch
        // speaks keys everywhere, so the key is resolved here rather than
        // teaching the route a second spelling of one filter.
        int? projectId = null;
        if (projectKey is not null)
        {
            var projects = await cli.Board.ProjectsAsync(ct);
            var match = projects.FirstOrDefault(p => p.Key.Equals(projectKey, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                cli.Say.Complain($"hatch: there is no project {projectKey}");
                return 1;
            }

            projectId = match.Id;
        }

        if (json)
        {
            var path = Board.ChangelogPath(days, before, projectId);
            var answer = await cli.Board.Client.Send(HttpMethod.Get, path, null, ct);
            if (!answer.Ok)
            {
                cli.Say.Complain(cli.Board.Client.Refusal(answer, path));
                return 1;
            }

            if (answer.Body.Length > 0) cli.Say.Lines(answer.Body.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'));
            return 0;
        }

        var changelog = await cli.Board.ChangelogAsync(days, before, projectId, ct);
        if (changelog is null) return 1;

        foreach (var line in Render(changelog)) cli.Say.Line(line);
        return 0;
    }

    /// <summary>The page's own empty-state wording - see <c>changelogEmptyWords</c>, <c>apps/hatch/src/lib/changelog.ts</c>.</summary>
    public const string EmptyWords = "Nothing has shipped yet.";

    /// <summary>
    /// The window, drawn one day at a time, newest first - nothing re-sorted,
    /// since the endpoint already ordered every day and every entry inside it.
    /// </summary>
    public static IReadOnlyList<string> Render(ChangelogDto changelog)
    {
        if (changelog.Days.Count == 0) return [EmptyWords];

        var lines = new List<string>();
        for (var i = 0; i < changelog.Days.Count; i++)
        {
            if (i > 0) lines.Add("");
            lines.AddRange(DayBlock(changelog.Days[i]));
        }

        return lines;
    }

    private static IReadOnlyList<string> DayBlock(ChangelogDayDto day)
    {
        var lines = new List<string> { $"## {DayHeading(day.Date)}" };

        if (day.Features.Count > 0)
        {
            lines.Add("");
            foreach (var feature in day.Features) lines.Add(FeatureBullet(feature));
        }

        if (day.Fixes.Count > 0)
        {
            lines.Add("");
            lines.Add("Also:");
            foreach (var fix in day.Fixes) lines.Add($"- {FixLine(fix)}");
        }

        return lines;
    }

    private static string FeatureBullet(ChangelogEntryDto feature)
    {
        var note = NoteFirstLine(feature.ReleaseNote);
        var suffix = note is null ? "" : $" — {note}";
        return $"- **{feature.Key}** {feature.Title}{suffix} ({BeneathLabel(feature.DescendantCount)})";
    }

    /// <summary>`Tuesday, October 13, 2026` - <c>dayHeading</c>'s own wording, <c>apps/hatch/src/lib/changelog.ts</c>, for a server day already in <c>yyyy-MM-dd</c>.</summary>
    private static string DayHeading(string date) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ToDateTime(TimeOnly.MinValue)
            .ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>A fix's one-liner: its release note's first line, or its title when the note is null or blank - <c>fixLine</c>'s own rule, <c>apps/hatch/src/lib/changelog.ts:20</c>.</summary>
    private static string FixLine(ChangelogEntryDto entry) => NoteFirstLine(entry.ReleaseNote) ?? entry.Title;

    private static string? NoteFirstLine(string? releaseNote)
    {
        var note = releaseNote?.Trim();
        return string.IsNullOrEmpty(note) ? null : note.Split('\n')[0].Trim();
    }

    /// <summary>`none beneath`, `1 beneath`, `4 beneath` - <c>beneathLabel</c>'s own wording, <c>apps/hatch/src/lib/overnight.ts:28</c>.</summary>
    private static string BeneathLabel(int descendantCount) => descendantCount == 0 ? "none beneath" : $"{descendantCount} beneath";
}

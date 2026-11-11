using System.Globalization;

namespace Hatch.Cli;

/// <summary>
/// One actor's rundown over a window, printed as markdown - the terminal's
/// reading of the page at <c>/reports/activity</c>, for pasting into a check-in
/// somewhere else.
/// </summary>
/// <remarks>
/// Nothing here re-litigates the endpoint: the totals, the per-issue counts and
/// their order, and the session estimate are <c>GET /api/hatch/reports/activity</c>'s
/// own. This prints what it was served, in the same words the page already
/// uses - <c>kindWords</c>, <c>headlineWords</c>, <c>emptyWords</c> and
/// <c>touchWords</c>, <c>apps/hatch/src/lib/activity.ts</c>, mirrored rather
/// than reinvented. It draws no series and no sessions.
/// </remarks>
public sealed class ActivityCommand(Cli cli)
{
    public static readonly string[] ActivityUsage =
    [
        "usage: hatch activity [--actor <name>] [--days <n>] [--before <instant>] [--project <KEY>] [--include-keys] [--json]",
        "",
        "  One actor's rundown, as markdown: what they did in the window, a heading",
        "  per issue they touched with a bullet per kind of thing done, and an",
        "  estimate of the hours it came to. An empty window prints one sentence",
        "  saying so.",
        "",
        "  --actor <name>      whose activity to read, as the trail spells it. Defaults",
        "                      to the owner of the key this runs as; a key with none",
        "                      needs this",
        "  --days <n>          the window's length, ending at --before. Fourteen by default",
        "  --before <instant>  move the window back to end at this instant",
        "                      (2026-09-12T17:00:00Z). Now by default",
        "  --project <KEY>     narrow it to one project; a key nobody has is refused",
        "  --include-keys      also count what a hand typed under the keys the actor owns",
        "                      (a heuristic: events outside any work-log session or claim",
        "                      of the loop). Only when --actor names a person",
        "  --json              the endpoint's own answer, and nothing else",
    ];

    private const int DefaultDays = 14;

    /// <summary>How many kinds the headline names before the rest become one tail - <c>HEADLINE_KINDS</c>, <c>activity.ts</c>.</summary>
    private const int HeadlineKinds = 3;

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(cli.Say, ActivityUsage);

        var days = DefaultDays;
        string? actor = null;
        DateTimeOffset? before = null;
        string? projectKey = null;
        var json = false;
        var includeKeys = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--json") { json = true; continue; }
            if (arg == "--include-keys") { includeKeys = true; continue; }

            if (arg == "--days")
            {
                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out days) || days < 1)
                    return Usage.Refuse(cli.Say, "--days takes a whole number of at least one", ActivityUsage);
                i++;
                continue;
            }

            if (arg == "--actor")
            {
                if (i + 1 >= args.Length || args[i + 1].Length == 0)
                    return Usage.Refuse(cli.Say, "--actor takes a name", ActivityUsage);
                actor = args[++i];
                continue;
            }

            if (arg == "--before")
            {
                var value = i + 1 < args.Length ? args[i + 1] : "";
                if (!DateTimeOffset.TryParse(
                        value, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                    return Usage.Refuse(cli.Say, $"--before takes an instant (2026-09-12T17:00:00Z) - not \"{value}\"", ActivityUsage);
                before = parsed;
                i++;
                continue;
            }

            if (arg == "--project")
            {
                if (i + 1 >= args.Length || args[i + 1].Length == 0)
                    return Usage.Refuse(cli.Say, "--project takes a key", ActivityUsage);
                projectKey = args[++i];
                continue;
            }

            return Usage.Refuse(cli.Say, $"activity does not take \"{arg}\"", ActivityUsage);
        }

        var to = before ?? cli.Clock.GetUtcNow();
        var from = to - TimeSpan.FromDays(days);

        // The route takes only an id, the way every route does; hatch speaks
        // keys everywhere, so the key is resolved here.
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

        if (actor is null)
        {
            var me = await cli.Board.MeAsync(ct);
            actor = me is { Kind: "key", Owner: { } owner } ? owner : null;
            if (actor is null)
            {
                cli.Say.Complain("hatch: an actor is required - pass --actor <name>, or run as a key with an owner");
                return 1;
            }
        }

        if (json)
        {
            var path = Board.ActivityPath(actor, from, to, projectId, includeKeys);
            var answer = await cli.Board.Client.Send(HttpMethod.Get, path, null, ct);
            if (!answer.Ok)
            {
                cli.Say.Complain(cli.Board.Client.Refusal(answer, path));
                return 1;
            }

            if (answer.Body.Length > 0) cli.Say.Lines(answer.Body.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'));
            return 0;
        }

        var report = await cli.Board.ActivityAsync(actor, from, to, projectId, includeKeys, ct);
        if (report is null) return 1;

        foreach (var line in Render(report, Board.OffsetMinutes(cli.Clock.GetUtcNow()))) cli.Say.Line(line);
        return 0;
    }

    /// <summary>
    /// The rundown, one issue at a time in the order the endpoint gave -
    /// nothing re-sorted. <paramref name="offsetMinutes"/> (minutes east of
    /// UTC) puts each touch on the reader's own clock.
    /// </summary>
    public static IReadOnlyList<string> Render(ActivityReportDto report, int offsetMinutes)
    {
        if (report.Total == 0) return [EmptyWords(report.Actor)];

        var estimate = report.Estimate;
        var lines = new List<string>
        {
            $"# {report.Actor}, {Touch(report.From, offsetMinutes)} to {Touch(report.To, offsetMinutes)}",
            "",
            $"**{HeadlineWords(report)}**",
            "",
            $"Estimated {(estimate.Minutes / 60.0).ToString("0.0", CultureInfo.InvariantCulture)}h across {estimate.Sessions} " +
            $"{(estimate.Sessions == 1 ? "session" : "sessions")} " +
            $"({estimate.GapMinutes}m idle gap ends one, {estimate.LeadMinutes}m lead on each).",
        };

        foreach (var touched in report.Issues)
        {
            lines.Add("");
            lines.Add($"## {touched.Issue.Key} {touched.Issue.Title}");
            foreach (var kind in touched.Kinds) lines.Add($"- {KindWords(kind.Kind, kind.Count)}");
            lines.Add($"- first {Touch(touched.First, offsetMinutes)}, last {Touch(touched.Last, offsetMinutes)}");
        }

        return lines;
    }

    /// <summary>The empty range as a sentence - <c>emptyWords</c>, <c>activity.ts</c>.</summary>
    public static string EmptyWords(string actor) => $"Nothing recorded for {actor} in this window.";

    /// <summary>
    /// The headline: the total and the top three kinds, in the server's order.
    /// Further kinds are one tail, <c>and 4 others</c>, counting their events -
    /// the number of events, not of kinds. <c>headlineWords</c>, <c>activity.ts</c>.
    /// </summary>
    public static string HeadlineWords(ActivityReportDto report)
    {
        var events = $"{report.Total} {(report.Total == 1 ? "event" : "events")}";
        if (report.Kinds.Count == 0) return events;

        var top = report.Kinds.Take(HeadlineKinds).Select(k => KindWords(k.Kind, k.Count)).ToList();
        var rest = report.Kinds.Skip(HeadlineKinds).Sum(k => k.Count);
        if (rest > 0) top.Add($"and {rest} {(rest == 1 ? "other" : "others")}");

        return $"{events}: {string.Join(", ", top)}";
    }

    /// <summary>
    /// Kind → [one, many]. The common kinds in the words a person says; anything
    /// else falls back to the kind's own name spaced out. <c>KIND_WORDS</c>, <c>activity.ts</c>.
    /// </summary>
    private static readonly Dictionary<string, (string One, string Many)> KindNames = new()
    {
        ["commented"] = ("comment", "comments"),
        ["status_changed"] = ("move", "moves"),
        ["created"] = ("created", "created"),
        ["retitled"] = ("retitle", "retitles"),
        ["redescribed"] = ("description edit", "description edits"),
        ["retyped"] = ("retype", "retypes"),
        ["parent_changed"] = ("reparent", "reparents"),
        ["assignee_changed"] = ("assignment", "assignments"),
        ["priority_changed"] = ("priority change", "priority changes"),
        ["dependency_added"] = ("dependency added", "dependencies added"),
        ["dependency_removed"] = ("dependency removed", "dependencies removed"),
        ["pull_request_changed"] = ("pull request change", "pull request changes"),
        ["claim_taken"] = ("claim taken", "claims taken"),
        ["claim_released"] = ("claim released", "claims released"),
        ["messaged"] = ("message", "messages"),
        ["asked"] = ("question", "questions"),
        ["answered"] = ("answer", "answers"),
    };

    /// <summary>One kind and its count in words: `1 comment`, `9 moves`, `6 created` - <c>kindWords</c>, <c>activity.ts</c>.</summary>
    public static string KindWords(string kind, int count)
    {
        var (one, many) = KindNames.TryGetValue(kind, out var words)
            ? words
            : (kind.Replace('_', ' '), kind.Replace('_', ' '));

        return $"{count} {(count == 1 ? one : many)}";
    }

    /// <summary>An instant on the reader's own clock, as `4 Nov 2026, 14:05` - <c>touchWords</c>, <c>activity.ts</c>.</summary>
    public static string Touch(DateTimeOffset at, int offsetMinutes) =>
        at.UtcDateTime.AddMinutes(offsetMinutes).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
}

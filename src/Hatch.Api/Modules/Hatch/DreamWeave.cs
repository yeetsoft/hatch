using System.Text;
using System.Text.RegularExpressions;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The title and brief a weave press files its epic-to-be with - one thread
/// drawn out of the Dream Board, by one of four modes.
///
/// Pure and synchronous - no <see cref="HatchContext"/> - because filing the
/// issue is the controller's job and the wording is tested without one.
/// </summary>
public static partial class DreamWeave
{
    public static readonly string[] Modes = ["seed", "pressing", "common", "next"];

    private const string Prefix = "Weave: ";

    /// <summary>
    /// The title the weave is filed under: the seed, on one line, in seed mode,
    /// and a fixed phrase for the others. Cut to
    /// <see cref="EfHatchIssue.MaxTitleLength"/>.
    /// </summary>
    public static string Title(string mode, string? seed)
    {
        RequireMode(mode, seed);

        if (mode != "seed") return $"{Prefix}the {Phrase(mode)} thread";

        var title = Prefix + WhitespaceRun().Replace(seed!, " ").Trim();
        if (title.Length <= EfHatchIssue.MaxTitleLength) return title;

        var cut = title[..EfHatchIssue.MaxTitleLength];
        // A cut through a surrogate pair would leave half a character.
        if (char.IsHighSurrogate(cut[^1])) cut = cut[..^1];

        return cut.TrimEnd();
    }

    /// <summary>
    /// The markdown brief for the session that weaves: what the mode asks for,
    /// how to read the dreams, and how to record what it drew on.
    /// <paramref name="dreamIds"/> are the dreams the person had on screen when
    /// they pressed, empty where none.
    /// </summary>
    public static string Describe(string mode, string? seed, int projectId, IReadOnlyList<long> dreamIds)
    {
        RequireMode(mode, seed);

        var read = mode == "seed"
            ? $"hatch api GET \"/api/dreams?q={Uri.EscapeDataString(seed!)}&projectId={projectId}&limit=200\""
            : $"hatch api GET \"/api/dreams?projectId={projectId}&limit=200\"";
        var order = mode == "seed" ? "best match first" : "newest first";

        var sb = new StringBuilder();
        sb.AppendLine("# This epic does not exist yet");
        sb.AppendLine();
        sb.AppendLine("It is to be woven from the Dream Board (the dreams at `/dreams`). This description is the brief for the weave, not the epic's own.");
        sb.AppendLine();
        sb.AppendLine($"Mode: **{mode}**.");

        if (mode == "seed")
        {
            sb.AppendLine();
            foreach (var line in NewLine().Split(seed!)) sb.AppendLine(line.Length == 0 ? ">" : $"> {line}");
        }

        sb.AppendLine();
        sb.AppendLine("## What the mode asks for");
        sb.AppendLine();
        sb.AppendLine(Asks(mode, projectId));
        sb.AppendLine();
        sb.AppendLine("## Reading the dreams");
        sb.AppendLine();
        sb.AppendLine($"`{read}` ({order}).");
        sb.AppendLine();
        sb.AppendLine("A dream that leans toward no project matches only without `projectId`, so ask again without it. Page with `offset` until `total` is reached, and read them all - a dream is a fragment and the thread runs between them.");

        if (dreamIds.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"The person had these on screen when they pressed, so start from them - {string.Join(", ", dreamIds)}. There is no route that reads one dream, so find each by `id` in the pages the list returns.");
        }

        sb.AppendLine();
        sb.AppendLine("## Writing the epic");
        sb.AppendLine();
        sb.AppendLine("Rewrite this description as the epic's own, quoting the dreams it rests on. A dream that bears on the epic but falls outside it becomes a story under it, or a comment on this ticket saying why not.");
        sb.AppendLine();
        sb.AppendLine("## Recording what you drew on");
        sb.AppendLine();
        sb.AppendLine("Before moving the ticket on, run `hatch api PUT /api/issues/<KEY>/dreams '{\"dreamIds\":[...]}'`, where `<KEY>` is this ticket's own key.");
        sb.AppendLine();
        sb.AppendLine("## The Dream Board stays as it is");
        sb.AppendLine();
        sb.Append("This session posts, edits and deletes no dream.");

        return sb.ToString();
    }

    private static string Phrase(string mode) => mode switch
    {
        "pressing" => "most pressing",
        "common" => "most common",
        _ => "next",
    };

    private static string Asks(string mode, int projectId) => mode switch
    {
        "seed" => "Find the thread running through the dreams that bear on the seed.",
        "pressing" => "Find the thread the dreams return to with the most urgency.",
        "common" => "Find the thread the most dreams touch.",
        _ => $"Find the nearest step toward the vision these dreams describe, given what `hatch api GET \"/api/plan?projectId={projectId}\"` says has already shipped.",
    };

    private static void RequireMode(string mode, string? seed)
    {
        if (!Modes.Contains(mode)) throw new ArgumentException($"Unknown weave mode '{mode}'.", nameof(mode));
        if (mode == "seed" && string.IsNullOrWhiteSpace(seed)) throw new ArgumentException("Seed mode needs a seed.", nameof(seed));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"\r\n|\n|\r")]
    private static partial Regex NewLine();
}

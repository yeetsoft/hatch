namespace Hatch.Cli;

/// <summary>
/// The opening and closing lines that wrap one increment in the log - the eggs
/// a night is read by searching for.
/// </summary>
/// <remarks>
/// Pure, on purpose: given what an increment knows about itself, these are the
/// lines. Printing them, in every log and only at a terminal in the same
/// breath, is <see cref="Terminal"/>'s job and nothing here touches one - the
/// split the readout's own <c>Readout.cs</c> makes too.
/// </remarks>
public static class Banner
{
    private const string Egg = "🥚🥚🥚🥚🥚";
    private const string Chick = "🐣🐣🐣🐣🐣";
    private const string Pan = "🍳🍳🍳🍳🍳";
    private const string Siren = "🚨🚨🚨🚨🚨";

    /// <summary>
    /// Before a session is spawned: the ticket, what the increment is for, the
    /// model and effort it runs under, and whose night this is.
    /// </summary>
    public static IReadOnlyList<string> Opening(
        IncrementReport report, WorkDto work, string model, string effort,
        string runnerName, int incrementNumber, ConflictRun? conflict, BuildRun? build)
    {
        List<string> lines =
        [
            $"{Egg} STARTING WORK ON {report.Key}",
            $"  {work.Issue.Title} [{work.Issue.Type}]",
            $"  {What(report, conflict, build)}",
            $"  {model}, effort {effort}",
        ];

        if (Prompt.OverrideLine(work, model, effort) is { } chose) lines.Add($"  {chose}");

        lines.Add($"  {runnerName}, increment {incrementNumber} of the night");
        return lines;
    }

    /// <summary>
    /// When the increment is over: how it went, in the tally's own words, and
    /// what it took.
    /// </summary>
    /// <param name="usageLine">
    /// One line reading the account's usage, where there is one - see HA-124.
    /// Absent leaves the banner with no such line, rather than a blank one.
    /// </param>
    public static IReadOnlyList<string> Closing(IncrementReport report, string? usageLine = null)
    {
        List<string> lines =
        [
            $"{Glyph(report)} STOPPING WORK ON {report.Key}",
            $"  {Outcome(report)}",
            $"  Took {Took(report)}",
        ];

        if (usageLine is { Length: > 0 }) lines.Add($"  {usageLine}");

        return lines;
    }

    /// <summary>
    /// What the increment is for, in the phrase the opening banner and the
    /// readout's issue row both use - a move, a conflict, or a failing build.
    /// </summary>
    public static string What(IncrementReport report, ConflictRun? conflict, BuildRun? build) =>
        conflict is not null ? $"resolving conflicts with {report.ConflictTrunk}"
        : build is not null ? Builds.Words(build.Found.Names)
        : $"{report.From} -> {report.To}";

    /// <summary>
    /// The error glyph outranks the rest: a session that exited badly, was cut
    /// off, or lost its ticket mid-run said nothing trustworthy about whether
    /// the board moved, however the column reads afterward.
    /// </summary>
    private static string Glyph(IncrementReport report) =>
        report.Preempted ? Siren
        : report.ExitCode != 0 || report.Interrupted || report.LostLease ? Pan
        : report.Moved || report.Resolved || report.FixPushed ? Chick
        : Egg;

    /// <summary>
    /// <see cref="IncrementReport.Outcome"/>'s own words for everything except a
    /// move, which that property phrases as a running commentary reads it
    /// ("Breakdown -> Backlog") and a banner somebody reads afterward does not
    /// ("Moved from Breakdown to Backlog").
    /// </summary>
    private static string Outcome(IncrementReport report) =>
        report.Moved ? $"Moved from {report.From} to {report.Ended}" : report.Outcome;

    private static string Took(IncrementReport report)
    {
        var elapsed = Format.Duration((long)(report.EndedAt - report.StartedAt).TotalSeconds);
        var tokens = report.TotalTokens is { } t ? $"{Format.Compact(t)} tokens" : "tokens not reported";
        var cost = report.Cost is { } c ? Format.Spent(c) : "cost not reported";
        var turns = report.Turns is { } n ? $"{n} turns" : "turns not reported";
        return $"{elapsed}, {tokens}, {cost}, {turns}";
    }
}

namespace Hatch.Cli;

/// <summary>
/// The guard that refuses to count an increment finished when it leaves a
/// branch on origin with no pull request recorded, for a ticket sitting in
/// the review column - see HA-214 for the five sessions that pushed code
/// nobody could find a review for. Escalates exactly the way <see
/// cref="Increment.FlagStallAsync"/> does, and for the same reason: the first
/// occurrence in a row is let go of quietly, because most of the time
/// somebody simply has not opened the pull request yet, and the second is
/// worth a person's eye.
/// </summary>
/// <remarks>
/// Never an <see cref="Increment"/> stall: the ticket moved, or its conflict
/// resolved, or its build pushed a fix - something happened, and the trouble
/// is that nobody can find it. The two guards are mutually exclusive by the
/// condition each is reached under, and neither touches the issue's status -
/// only <see cref="IncrementReport.ReleaseOutcome"/> changes, so the claim
/// counts the increment as not having finished cleanly.
/// </remarks>
public static class Unpublished
{
    /// <summary>
    /// This question's own label pair - never <see cref="StallAnswers.Options"/>,
    /// whose exact pair is how <see cref="StallAnswers.IsStall"/> lets a stall
    /// lapse unattended after five minutes. Nothing here knows whether a pull
    /// request is actually unnecessary, so this condition must not clear
    /// itself the same way: it waits for a person, however long that takes.
    /// </summary>
    public const string Recorded = "pull request recorded";
    public const string NotNeeded = "no pull request needed for this ticket";

    private static IReadOnlyList<QuestionOptionDto> Options() =>
    [
        new(Recorded, "Open it with hatch pr, or say it is already set - this comment does not record one for you."),
        new(NotNeeded, "This ticket's branch is not meant to go through a pull request."),
    ];

    /// <summary>
    /// Writes the one comment - and, on the second occurrence in a row, the
    /// question beside it - and answers with what the claim should be
    /// released with: always <see cref="ClaimOutcomes.Dropped"/>, because an
    /// increment that pushed code nobody can find a review for has not
    /// finished, whatever else it did.
    /// </summary>
    public static async Task<string> RefuseAsync(
        Board board, Terminal say, IncrementReport report, IReadOnlyList<BranchEntry> branches, int letGo, CancellationToken ct)
    {
        var named = branches
            .Select(b => (b.Path, Branch: b.Kind == BranchKind.Entered ? b.Branch : b.Cut))
            .Where(b => b.Branch is { Length: > 0 })
            .ToList();
        var multiple = named.Count > 1;

        var where = named.Count == 0
            ? "a branch on origin"
            : string.Join('\n', named.Select(b =>
                $"- {(multiple ? $"{Path.GetFileName(b.Path.TrimEnd('/', '\\'))}: " : "")}{b.Branch}"));

        var body = $"""
            An unattended increment left {report.Key} in "{report.Ended}" with no pull request recorded
            for the branch it pushed:

            {where}
            """;

        if (letGo > 0)
            body += $"\n\nThis is the {Increment.Ordinal(letGo + 1)} increment in a row to leave this ticket here without one.";

        body += report.SessionId is { Length: > 0 } session
            ? $"\n\nThe session it ran in is still there, with everything it did in context:\n\n    claude --resume {session}"
            : "\n\nThere is no session to resume: the run ended before it said what its id was.";

        say.Line("");
        if (letGo > 0)
        {
            body += "\n\nA question goes up with this comment, so nothing further will be dispatched at\n"
                  + "this issue until somebody answers it. Answering it does not itself open or record a\n"
                  + "pull request - that is still hatch pr - it only unblocks the next pass.";
            say.Line($"hatch: {report.Key} left a branch on origin with no pull request - flagging it, and going on to the next");
        }
        else
        {
            say.Line($"hatch: {report.Key} left a branch on origin with no pull request - noted, and going on to the next");
        }

        say.Line("");

        try
        {
            await board.CommentAsync(report.Key, body, ct);

            if (letGo > 0)
                await board.AskAsync(
                    report.Key,
                    $"{report.Key} has a branch on origin with no pull request recorded, sitting in \"{report.Ended}\" - has one been opened?",
                    Options(),
                    ct);

            report.Flag = letGo > 0 ? "flagged - no pull request recorded" : "no pull request recorded";
        }
        catch (Exception e) when (e is HatchException or OperationCanceledException)
        {
            report.Flag = "not flagged - the write was refused";
            say.Complain($"hatch: {report.Key} could not be flagged for its missing pull request - a later pass may offer it again");
        }

        return ClaimOutcomes.Dropped;
    }
}

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What a bug filed from a goose finding says about it: a title per check, and
/// the brief an agent reads to start - HA-2516. One entry per check name; a test
/// fails when a <see cref="FindingCheck"/> has none, so a new check cannot land
/// without a title. Writes a project key and a subject, never an address of this
/// installation.
/// </summary>
public static class FindingBugs
{
    /// <summary>
    /// How a check says its own bound and what reaching it means. <paramref name="Title"/>
    /// reads the finding's <see cref="EfHatchGooseFinding.Token"/> and
    /// <see cref="EfHatchGooseFinding.Subject"/>.
    /// </summary>
    private sealed record Entry(
        Func<EfHatchGooseFinding, string> Title, Func<HatchOptions, string> Bound, string Opens);

    private const string StoodThatLong = "it opens a finding once a condition has stood that long";

    private static readonly Dictionary<string, Entry> Checks = new()
    {
        ["train-ready"] = new(
            f => $"Merge train standing behind {f.Token}",
            o => Minutes(GooseSettings.TrainReadyBound(o)), StoodThatLong),
        ["ready-unclaimed"] = new(
            f => $"Ready work unclaimed in {f.Subject}",
            o => Minutes(GooseSettings.UnclaimedBound(o)), StoodThatLong),
        ["ready-no-runner"] = new(
            f => $"No runner can take ready work in {f.Subject}",
            o => Minutes(GooseSettings.NoRunnerBound(o)), StoodThatLong),
        ["claim-repeating"] = new(
            f => $"Runner stuck re-claiming {f.Subject}",
            o => Count(GooseSettings.RepeatClaims(o), "claim"),
            "it opens a finding once a runner has claimed one issue that many times in a row with the issue not moving"),
        ["questions-waiting"] = new(
            _ => "Questions waiting too long for an answer",
            o => Hours(GooseSettings.QuestionBound(o)), StoodThatLong),
        [StandingStillCheck.CheckName] = new(
            f => $"Ticket standing still: {f.Subject}",
            o => Hours(GooseSettings.StandingBound(o)),
            "it opens a finding once a ticket has stood in one column that long with nothing set to move it"),
    };

    /// <summary>Whether <paramref name="check"/> has its own entry, rather than the fallback title.</summary>
    public static bool Knows(string check) => Checks.ContainsKey(check);

    public static string Title(EfHatchGooseFinding finding) =>
        Checks.TryGetValue(finding.Check, out var entry)
            ? entry.Title(finding)
            : $"The goose found a problem: {finding.Check}";

    /// <summary>
    /// The bug's description, in markdown: the sentence, when it began (and, for a
    /// closed finding, that it cleared), the evidence one line each, the check and
    /// its bound, and what to do about it.
    /// </summary>
    public static string Description(EfHatchGooseFinding finding, HatchOptions options)
    {
        var lines = new List<string> { finding.Sentence, "", $"Began: {finding.BeganAt:O}" };

        if (finding.ClosedAt is { } closed)
        {
            lines.Add("");
            lines.Add($"The problem cleared at {closed:O}. The cause may still be worth finding.");
        }

        lines.Add("");
        lines.Add("## Evidence");
        lines.Add("");
        lines.AddRange(GooseFindingEvidence.Read(finding.Evidence).Select(e => $"- {e.Label}: {e.Value}"));

        lines.Add("");
        lines.Add("## The check");
        lines.Add("");
        lines.Add(Checks.TryGetValue(finding.Check, out var entry)
            ? $"The check is `{finding.Check}`. Its bound is {entry.Bound(options)}; {entry.Opens}."
            : $"The check is `{finding.Check}`. It opens a finding once a condition has stood past its bound.");

        lines.Add("");
        lines.Add("Find the cause, then either fix it or say what a person has to do.");

        return string.Join("\n", lines);
    }

    private static string Minutes(TimeSpan bound) => Count((int)bound.TotalMinutes, "minute");

    private static string Hours(TimeSpan bound) => Count((int)bound.TotalHours, "hour");

    private static string Count(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
}

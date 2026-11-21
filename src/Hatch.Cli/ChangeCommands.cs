using System.Globalization;

namespace Hatch.Cli;

/// <summary>
/// An ambition's changes - what its capabilities gained, lost or had rewritten -
/// and the outcome each epic beneath it records against one.
/// </summary>
/// <remarks>
/// A server refusal is a <see cref="HatchException"/> and is left to
/// <c>Program</c>, which prints it as it came and exits 1.
/// </remarks>
public sealed class ChangeCommands(Cli cli)
{
    private static readonly string[] Outcomes = ["carries_on", "not_touched", "serves_nothing"];

    public static readonly string[] ChangeUsage =
    [
        "usage: hatch change list <ambition>",
        "       hatch change outcome <ambition> <epic> carries_on|not_touched|serves_nothing \"<why>\"",
        "                            --change <id>",
        "",
        "  An ambition's latest change to its capabilities, and the outcome each",
        "  epic beneath it records against one.",
        "",
        "  list     the newest change: what was added, changed or removed, and the",
        "           outcomes recorded so far. An ambition with no change says so.",
        "  outcome  records one epic's outcome on a change. --change names the id",
        "           `list` prints after \"change\"; it is required, since a default",
        "           of the newest change could record against one that has since",
        "           moved on.",
    ];

    public async Task<int> ChangeAsync(string[] args, CancellationToken ct)
    {
        if (Usage.Wanted(args)) return Usage.Print(cli.Say, ChangeUsage);
        if (args.Length == 0) return Usage.Refuse(cli.Say, "change takes a verb", ChangeUsage);

        var verb = args[0];
        var rest = args[1..];
        if (Usage.Wanted(rest)) return Usage.Print(cli.Say, ChangeUsage);

        return verb switch
        {
            "list" => await ListAsync(rest, ct),
            "outcome" => await OutcomeAsync(rest, ct),
            _ => Usage.Refuse(cli.Say, $"change does not do {verb}", ChangeUsage),
        };
    }

    private async Task<int> ListAsync(string[] args, CancellationToken ct)
    {
        if (Parse("list", args, ["ambition"], []) is not { } given) return 1;

        var key = given.Positional[0];
        var changes = await cli.Board.ChangesAsync(key, ct);
        if (changes.Count == 0)
        {
            cli.Say.Line($"{key} has not been changed");
            return 0;
        }

        cli.Say.Lines(Draw(key, changes[0]));
        return 0;
    }

    private async Task<int> OutcomeAsync(string[] args, CancellationToken ct)
    {
        if (Parse("outcome", args, ["ambition", "epic", "outcome", "why"], ["--change"]) is not { } given) return 1;

        var word = given.Positional[2];
        if (!Outcomes.Contains(word))
            return Usage.Refuse(cli.Say, $"{word} is not an outcome - carries_on, not_touched or serves_nothing", ChangeUsage);

        var why = given.Positional[3];
        if (why.Trim().Length == 0)
            return Usage.Refuse(cli.Say, "change outcome needs a why that is not blank", ChangeUsage);

        if (given.Flags.GetValueOrDefault("--change") is not { Length: > 0 } typed)
            return Usage.Refuse(cli.Say, "change outcome needs --change, the id `list` prints after \"change\"", ChangeUsage);
        if (!long.TryParse(typed, NumberStyles.None, CultureInfo.InvariantCulture, out var changeId) || changeId < 1)
            return Usage.Refuse(cli.Say, "--change takes a positive number, the id `list` prints after \"change\"", ChangeUsage);

        var key = given.Positional[0];
        var epic = given.Positional[1];
        await cli.Board.RecordOutcomeAsync(key, changeId, new ChangeOutcomeRequest(epic, word, why), ct);
        cli.Say.Line($"{key}: {epic} {word} for change {changeId}");
        return 0;
    }

    private static IReadOnlyList<string> Draw(string key, ChangeDto change)
    {
        var lines = new List<string>
        {
            change.WorkedOut
                ? $"{key}: change {change.Id}, saved {Day(change.SavedAt)}, worked out {Day(change.WorkedOutAt!.Value)}"
                : $"{key}: change {change.Id}, saved {Day(change.SavedAt)}, not worked out",
        };

        foreach (var entry in change.Entries) lines.AddRange(DrawEntry(entry));

        if (change.Outcomes.Count == 0)
        {
            lines.Add("  no outcomes yet");
        }
        else
        {
            foreach (var outcome in change.Outcomes)
                lines.Add($"  {outcome.EpicKey}  {outcome.Outcome}  {Folded(outcome.Why)}");
        }

        return lines;
    }

    private static IReadOnlyList<string> DrawEntry(ChangeEntryDto entry)
    {
        var lines = new List<string>();
        switch (entry.Kind)
        {
            case "added":
                lines.Add($"  added    #{entry.CapabilityId}  {Folded(entry.StatementAfter ?? "")}");
                if (entry.HowToTellAfter is { } howAfter) lines.Add($"    how to tell: {Folded(howAfter)}");
                break;

            case "removed":
                lines.Add($"  removed  #{entry.CapabilityId}  {Folded(entry.StatementBefore ?? "")}");
                if (entry.HowToTellBefore is { } howBefore) lines.Add($"    how to tell: {Folded(howBefore)}");
                break;

            case "changed":
                var statement = Arrow(entry.StatementBefore, entry.StatementAfter);
                lines.Add($"  changed  #{entry.CapabilityId}  {statement}");
                if (entry.HowToTellBefore != entry.HowToTellAfter)
                {
                    var howChanged = Arrow(entry.HowToTellBefore, entry.HowToTellAfter);
                    lines.Add($"    how to tell: {howChanged}");
                }
                break;
        }

        return lines;
    }

    private static string Arrow(string? before, string? after) =>
        before == after ? Folded(before ?? "") : $"{Folded(before ?? "")} -> {Folded(after ?? "")}";

    private static string Day(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Folded(string text) => text.ReplaceLineEndings(" ");

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
                        Usage.Refuse(cli.Say, $"{flag} is said twice", ChangeUsage);
                        return null;
                    }
                    break;

                case var flag when flags.Contains(flag):
                    Usage.Refuse(cli.Say, $"{flag} needs a value", ChangeUsage);
                    return null;

                case var flag when flag.StartsWith('-'):
                    Usage.Refuse(cli.Say, $"change {verb} does not take {flag}", ChangeUsage);
                    return null;

                default:
                    given.Positional.Add(args[i]);
                    break;
            }
        }

        if (given.Positional.Count != names.Length || given.Positional.Any(p => p.Length == 0))
        {
            Usage.Refuse(cli.Say, $"change {verb} takes {string.Join(" ", names.Select(n => $"<{n}>"))}", ChangeUsage);
            return null;
        }

        return given;
    }
}

namespace Hatch.Contracts;

/// <summary>
/// The three words a runner's column rule answers to - the whole board
/// (<see cref="All"/>), every column but the ones named (<see cref="Except"/>),
/// or only the ones named (<see cref="Only"/>) - so the controller and
/// whatever reads the rule back both work off the same three words rather
/// than inventing their own.
/// </summary>
public static class RunnerStatusMode
{
    public const string All = "all";
    public const string Except = "except";
    public const string Only = "only";

    public static readonly IReadOnlyList<string> Words = [All, Except, Only];

    /// <summary>
    /// The trimmed, lowercased mode, or the sentence refusing it.
    /// <paramref name="mode"/> absent (<c>null</c>) answers <see cref="All"/>
    /// in <paramref name="normalised"/> and null - "no mode given" is "all",
    /// the unbounded rule a runner with no rule at all already works under.
    /// </summary>
    public static string? Normalise(string? mode, out string normalised)
    {
        normalised = All;
        if (mode is null) return null;

        var folded = mode.Trim().ToLowerInvariant();
        if (!Words.Contains(folded))
            return $"a runner's column rule is {string.Join(", ", Words)} - not \"{mode.Trim()}\"";

        normalised = folded;
        return null;
    }
}

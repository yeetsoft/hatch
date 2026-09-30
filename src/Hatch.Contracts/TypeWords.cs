namespace Hatch.Contracts;

/// <summary>
/// How a set of issue types reads in a sentence - "stories and bugs", "epics",
/// "stories, bugs, and tasks". The one place that spells a type's plural, so a
/// slice's wording is the same whether it is built by the server
/// (<c>Wip.Sentence</c>) or by the CLI (<c>BoardCommands</c>).
/// </summary>
public static class TypeWords
{
    public static string Plural(IReadOnlyList<string> types)
    {
        var plural = types.Select(PluralOne).ToList();
        return plural.Count switch
        {
            0 => "issues",
            1 => plural[0],
            2 => $"{plural[0]} and {plural[1]}",
            _ => $"{string.Join(", ", plural.Take(plural.Count - 1))}, and {plural[^1]}",
        };
    }

    private static string PluralOne(string type) =>
        type.EndsWith('y') ? $"{type[..^1]}ies" : $"{type}s";
}

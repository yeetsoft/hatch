namespace Hatch.Contracts;

/// <summary>One thing a runner may need installed: the id everything else keys off, and the name a person reads.</summary>
public sealed record Dependency(string Id, string Label);

/// <summary>
/// The one master list of dependencies Hatch knows, and the one rule an id
/// answers to, so a detector, a recipe and a request are all held to the same
/// list rather than each carrying its own copy. Rows are kept in ordinal order
/// by id - that is the canonical order - so a row added later goes in its place.
/// </summary>
public sealed class Dependencies(IReadOnlyList<Dependency> rows)
{
    public const int MaxIdLength = 32;

    public IReadOnlyList<Dependency> Rows { get; } = rows;

    public static Dependencies Default { get; } = new([
        new Dependency("dotnet", ".NET SDK"),
        new Dependency("make", "make"),
        new Dependency("node", "Node.js"),
        new Dependency("playwright-browsers", "Playwright browsers"),
        new Dependency("postgres", "PostgreSQL"),
        new Dependency("pwsh", "PowerShell"),
    ]);

    /// <summary>
    /// Lowercase letters, digits and hyphens, one to <see cref="MaxIdLength"/>
    /// of them. A char loop rather than a <c>$</c> regex, which would accept a
    /// trailing newline.
    /// </summary>
    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength) return false;

        foreach (var c in id)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')) return false;
        }

        return true;
    }

    public bool Knows(string id) => Rows.Any(row => string.Equals(row.Id, id, StringComparison.Ordinal));

    /// <summary>Null for a known id, or the sentence for the first rule it breaks - the id rule, then the list.</summary>
    public string? IdProblem(string id)
    {
        if (!IsValidId(id))
            return $"a dependency id is lowercase letters, digits and hyphens, at most {MaxIdLength} characters - not \"{id}\"";

        if (!Knows(id))
            return $"`{id}` is not a dependency Hatch knows - the list is {string.Join(", ", Rows.Select(row => row.Id))}";

        return null;
    }
}

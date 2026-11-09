using System.Text;

namespace Hatch.Contracts;

/// <summary>
/// The cast list a checkout's runner is named from.
/// </summary>
/// <remarks>
/// A runner used to be named <c>host:/path/to/checkout</c> - unique by
/// construction, and unreadable at a glance on a board shared with anybody
/// else. A character is the opposite trade: readable, and not unique by
/// construction.
/// </remarks>
public static class RunnerCast
{
    /// <summary>
    /// Every name in <c>runner-names.txt</c>, in file order - main and
    /// recurring characters from three shows, plus the one-offs worth having.
    /// See that file for the cast.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = Load();

    private static IReadOnlyList<string> Load()
    {
        var assembly = typeof(RunnerCast).Assembly;
        using var stream = assembly.GetManifestResourceStream("runner-names.txt")
                            ?? throw new InvalidOperationException("runner-names.txt is not embedded in this build");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            // Kept rather than refused: a duplicate slipped into the file is a
            // typo to fix, not a reason every runner in the house fails to
            // start. The first spelling wins, which is the one that would
            // already have been chosen from.
            if (seen.Add(trimmed)) names.Add(trimmed);
        }

        return names;
    }

    /// <summary>
    /// The first name in <see cref="All"/>'s file order not already in
    /// <paramref name="taken"/>; once every name is taken, <c>All[0]</c>
    /// with the smallest numeric suffix starting at 2 not already taken.
    /// </summary>
    /// <param name="taken">
    /// Every name already spoken for - case-insensitively, since a person
    /// typing one by hand should not land beside the list's own spelling
    /// under a different case.
    /// </param>
    public static string Suggest(IReadOnlySet<string> taken)
    {
        foreach (var name in All)
            if (!taken.Contains(name)) return name;

        var suffix = 2;
        while (taken.Contains($"{All[0]} {suffix}")) suffix++;
        return $"{All[0]} {suffix}";
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace Hatch.Contracts;

/// <summary>
/// A declared dependency set rendered to the Dockerfile text that installs it,
/// or the one sentence saying why it cannot be.
/// </summary>
/// <remarks>
/// A pure function of the set: the same entries give the same bytes however
/// they are ordered or repeated, so an image built from them is cacheable by
/// its Dockerfile alone. Recipes are concatenated verbatim in id order, with no
/// <c>FROM</c> and no separator - each ends in a newline of its own. The return
/// shape mirrors <see cref="DependencyDeclaration.Parse"/>.
/// </remarks>
public static class DependencyRecipes
{
    // What a recipe needs installed before it runs. Each is added bare when the
    // set lacks it. A table of the renderer's own, not a declaration: its ids are
    // not re-checked against the master list.
    private static readonly IReadOnlyDictionary<string, string[]> Requires = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["playwright-browsers"] = ["node"],
    };

    // The first line of a recipe that carries its version, up to and including the `=`.
    private static readonly Regex VersionLine = new(
        @"^(ARG [A-Z][A-Z0-9_]*_VERSION=)[^\n]*", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Embedded = new(() =>
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in Dependencies.Default.Rows)
        {
            if (Recipe(row.Id) is { } text) found[row.Id] = text;
        }

        return found;
    });

    /// <summary>The install recipe embedded for <paramref name="id"/>, or null when there is none.</summary>
    public static string? Recipe(string id)
    {
        using var stream = typeof(DependencyRecipes).Assembly.GetManifestResourceStream($"recipes/{id}.Dockerfile");
        if (stream is null) return null;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static (string? Dockerfile, string? Problem) Render(IReadOnlyList<DeclaredDependency> set) =>
        Render(set, Embedded.Value, Dependencies.Default);

    internal static (string? Dockerfile, string? Problem) Render(
        IReadOnlyList<DeclaredDependency> set, IReadOnlyDictionary<string, string> recipes, Dependencies known)
    {
        // Render is public and takes any list, so nothing Parse checked is taken as read:
        // a version is spliced into a Dockerfile line.
        foreach (var entry in set)
        {
            var problem = known.IdProblem(entry.Id) ?? DependencyVersion.Problem(entry.Version);
            if (problem is not null) return (null, problem);
        }

        var versions = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var group in set.GroupBy(entry => entry.Id, StringComparer.Ordinal))
        {
            var pinned = group.Select(entry => entry.Version).OfType<string>()
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

            if (pinned.Count > 1)
                return (null, $"`{group.Key}` is declared at {pinned[0]} and at {pinned[1]} - a recipe installs one version");

            versions[group.Key] = pinned.Count == 1 ? pinned[0] : null;
        }

        foreach (var id in versions.Keys.ToList())
        {
            if (!Requires.TryGetValue(id, out var needed)) continue;
            foreach (var other in needed) versions.TryAdd(other, null);
        }

        var text = new StringBuilder();
        foreach (var (id, version) in versions)
        {
            if (!recipes.TryGetValue(id, out var recipe))
                return (null, $"`{id}` is on the master list but ships no install recipe");

            if (version is not null)
            {
                if (!VersionLine.IsMatch(recipe))
                    return (null, $"`{id}` installs whatever its package provides - declare it without a version");

                recipe = VersionLine.Replace(recipe, match => match.Groups[1].Value + version, 1);
            }

            text.Append(recipe);
        }

        return (text.ToString(), null);
    }
}

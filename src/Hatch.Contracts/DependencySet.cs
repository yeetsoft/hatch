namespace Hatch.Contracts;

/// <summary>
/// The one reading of what a runner has - the <c>id</c> or <c>id@version</c>
/// strings it advertises on its heartbeat and sends as <c>?has=</c> - so the
/// controller and the CLI both hold it to the same master list and the same
/// refusals.
/// </summary>
public static class DependencySet
{
    /// <summary>
    /// The trimmed, lowercased, deduplicated dependency strings, or the
    /// sentence refusing them. <paramref name="has"/> absent (<c>null</c>)
    /// answers null with <paramref name="normalised"/> empty - nothing to
    /// touch, as <see cref="RunnerTags.Normalise"/> does. One string is one
    /// <c>?has=</c> value: no comma splitting and no count cap here. Two
    /// versions of one id are both kept, since a machine can carry two SDKs.
    /// A refusal refuses the whole list. <paramref name="known"/> defaults to
    /// <see cref="Dependencies.Default"/>.
    /// </summary>
    public static string? Normalise(IReadOnlyList<string>? has, out IReadOnlyList<string> normalised, Dependencies? known = null)
    {
        normalised = [];
        if (has is null) return null;

        known ??= Dependencies.Default;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>();
        foreach (var entry in has)
        {
            var trimmed = entry.Trim();
            if (trimmed.Length == 0)
                return "a dependency cannot be blank";

            var lowered = trimmed.ToLowerInvariant();
            var at = lowered.IndexOf('@');
            var id = at < 0 ? lowered : lowered[..at];
            var version = at < 0 ? null : lowered[(at + 1)..];

            var problem = known.IdProblem(id) ?? DependencyVersion.Problem(version);
            if (problem is not null) return problem;

            if (seen.Add(lowered)) kept.Add(lowered);
        }

        normalised = kept;
        return null;
    }
}

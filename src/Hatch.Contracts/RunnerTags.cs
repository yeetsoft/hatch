namespace Hatch.Contracts;

/// <summary>
/// The shared rule a tag answers to - at most <see cref="MaxCount"/> on a
/// runner, each at most <see cref="MaxLength"/> characters - so the
/// controller and <c>hatch config --tag</c> both read the same refusal
/// rather than inventing their own.
/// </summary>
public static class RunnerTags
{
    public const int MaxLength = 32;
    public const int MaxCount = 8;

    /// <summary>
    /// The trimmed, lowercased, deduplicated tags, or the sentence refusing
    /// them. <paramref name="tags"/> absent (<c>null</c>) answers null with
    /// <paramref name="normalised"/> empty - nothing to touch, the same way
    /// <c>Remotes</c>/<c>Canonicalised</c> tell "absent" from "empty" off the
    /// original parameter rather than the answer.
    /// </summary>
    public static string? Normalise(IReadOnlyList<string>? tags, out IReadOnlyList<string> normalised)
    {
        normalised = [];
        if (tags is null) return null;

        if (tags.Count > MaxCount)
            return $"a runner carries at most {MaxCount} tags";

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>();
        foreach (var tag in tags)
        {
            var trimmed = tag.Trim();
            if (trimmed.Length == 0)
                return "a tag cannot be blank";

            if (trimmed.Length > MaxLength)
                return $"a tag is at most {MaxLength} characters - not \"{trimmed}\"";

            if (trimmed.Any(char.IsWhiteSpace) || trimmed.Contains(','))
                return $"a tag has no space or comma inside - not \"{trimmed}\"";

            var lowered = trimmed.ToLowerInvariant();
            if (seen.Add(lowered)) kept.Add(lowered);
        }

        normalised = kept;
        return null;
    }
}

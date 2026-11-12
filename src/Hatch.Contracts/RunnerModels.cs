namespace Hatch.Contracts;

/// <summary>
/// The shared rule a runner's advertised models answer to - at most
/// <see cref="MaxCount"/>, each an alias or a pinned id of a supported agent
/// - so the controller and the CLI both read the same refusal rather than
/// inventing their own.
/// </summary>
public static class RunnerModels
{
    public const int MaxCount = 100;

    /// <summary>
    /// The trimmed, deduplicated models in the order given, or the sentence
    /// refusing them. <paramref name="models"/> absent (<c>null</c>) answers
    /// null with <paramref name="normalised"/> empty - nothing to touch, the
    /// same way <see cref="RunnerTags.Normalise"/> tells "absent" from "empty".
    /// An alias is lowercased; any other entry must be a pinned id some family
    /// in <paramref name="families"/> recognises. When <paramref name="agent"/>
    /// names one of the families' agents, a pinned id of a different agent is
    /// refused too; blank or an agent no family names skips only that check.
    /// </summary>
    public static string? Normalise(
        IReadOnlyList<string>? models,
        string? agent,
        out IReadOnlyList<string> normalised,
        ModelFamilies? families = null)
    {
        normalised = [];
        if (models is null) return null;

        if (models.Count > MaxCount)
            return $"a runner advertises at most {MaxCount} models";

        families ??= ModelFamilies.Default;
        var agentKnown = !string.IsNullOrWhiteSpace(agent) && families.Rows.Any(r => r.Agent == agent);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>();
        foreach (var model in models)
        {
            var id = model?.Trim() ?? "";
            if (id.Length == 0)
                return "a model cannot be blank";

            var lowered = id.ToLowerInvariant();
            if (ModelFamilies.Aliases.Contains(lowered))
                id = lowered;
            else
            {
                var owner = families.AgentOf(id);
                if (owner is null)
                    return families.Refusal(id);

                if (agentKnown && owner != agent)
                    return $"\"{id}\" is {Article(owner)} {owner} model - this runner's agent is {agent}";
            }

            if (seen.Add(id)) kept.Add(id);
        }

        normalised = kept;
        return null;
    }

    /// <summary>
    /// Whether a runner that refuses <paramref name="refused"/> refuses a move
    /// on <paramref name="model"/>. Both sides are trimmed, and lowercased where
    /// the result is an alias; equal after that is a match. A refused alias
    /// also covers a pinned id whose hyphen-separated tokens include it, so
    /// <c>opus</c> refuses <c>claude-opus-5-5</c>. A refused pinned id matches
    /// itself only, and an alias is never refused by a pinned id. A null or
    /// blank model, or a null or empty list, matches nothing.
    /// </summary>
    public static bool Refuses(IReadOnlyList<string>? refused, string? model)
    {
        if (refused is null || refused.Count == 0 || string.IsNullOrWhiteSpace(model)) return false;

        var effective = Fold(model);
        var effectiveIsAlias = ModelFamilies.Aliases.Contains(effective);
        foreach (var entry in refused)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;

            var held = Fold(entry);
            if (string.Equals(held, effective, StringComparison.Ordinal)) return true;
            if (ModelFamilies.Aliases.Contains(held) && !effectiveIsAlias
                && effective.Split('-').Contains(held, StringComparer.Ordinal))
                return true;
        }

        return false;
    }

    private static string Fold(string model)
    {
        var trimmed = model.Trim();
        var lowered = trimmed.ToLowerInvariant();
        return ModelFamilies.Aliases.Contains(lowered) ? lowered : trimmed;
    }

    /// <summary>"an" before a vowel-initial word, "a" otherwise.</summary>
    private static string Article(string word) => "aeiou".Contains(word[0]) ? "an" : "a";
}

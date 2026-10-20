using System.Text.RegularExpressions;

namespace Hatch.Contracts;

/// <summary>One agent's pinned-id shape: what it looks like, and the one example shown in a refusal.</summary>
public sealed record ModelFamily(string Agent, string Pattern, string Example);

/// <summary>
/// The one list of model families a pinned id is judged against - the server's
/// own idea of which agents it can dispatch to, so a board playbook, a repo
/// playbook file and an issue override are all held to the same list rather
/// than each carrying its own copy of the pattern.
/// </summary>
public sealed class ModelFamilies(IReadOnlyList<ModelFamily> rows)
{
    public IReadOnlyList<ModelFamily> Rows { get; } = rows;

    public static ModelFamilies Default { get; } = new([
        new ModelFamily("claude", "^claude-[a-z0-9][a-z0-9.-]{0,48}$", "claude-opus-5"),
        new ModelFamily("codex", "^gpt-[a-z0-9][a-z0-9.-]{0,48}$", "gpt-6-luna"),
        new ModelFamily("grok", "^grok-[a-z0-9][a-z0-9.-]{0,48}$", "grok-4.6"),
    ]);

    /// <summary>
    /// The agent of the first row whose pattern matches <paramref name="model"/>,
    /// or null for null, blank, an alias, or a pinned id no row recognises.
    /// </summary>
    public string? AgentOf(string? model)
    {
        if (string.IsNullOrEmpty(model)) return null;

        foreach (var row in Rows)
        {
            if (Regex.IsMatch(model, row.Pattern, RegexOptions.None, TimeSpan.FromSeconds(1)))
                return row.Agent;
        }

        return null;
    }
}

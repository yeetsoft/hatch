namespace Hatch.Cli;

/// <summary>
/// The complaint a loop makes when git here can never sign a commit and no
/// project it was started for allows an unsigned one - so every ticket it
/// could claim would be let go again, and it would spend the night doing that.
/// </summary>
/// <remarks>
/// It fails open, as <see cref="NoScope"/> does: a board that does not answer,
/// one too old to list <c>allowsUnsignedCommits</c> (the field reads true when
/// absent), a <c>--project</c> key the board does not return, and a loop with no
/// checkout to ask about all read as "carry on". The server narrows the project
/// list to the key's scope, so the list is already the projects this loop can
/// be given work from.
/// </remarks>
public static class CannotSign
{
    private const int Named = 5;

    /// <summary>
    /// The complaint, one line to a string, or null to carry on.
    /// </summary>
    /// <param name="board">Where the projects are read.</param>
    /// <param name="projectKeys">The <c>--project</c> list; empty for every project the key reaches.</param>
    /// <param name="answers">What git said in each checkout the loop serves.</param>
    public static async Task<string[]?> RefusalAsync(
        Board board, IReadOnlyList<string> projectKeys, IReadOnlyList<(string Path, SigningCheck Check)> answers, CancellationToken ct)
    {
        if (answers.Count == 0 || answers.Any(a => a.Check.State == Signing.Works)) return null;

        var served = await ServedAsync(board, projectKeys, ct);
        if (served is null || served.Any(p => p.AllowsUnsignedCommits)) return null;

        var keys = served.Select(p => p.Key).Order(StringComparer.Ordinal).ToList();
        var names = keys.Count > Named
            ? $"{string.Join(", ", keys.Take(Named))} and {keys.Count - Named} more"
            : string.Join(", ", keys);

        var lines = new List<string>
        {
            $"hatch: git cannot sign commits here, and every project this runner serves allows only signed commits ({names}) - there is no ticket it could work",
        };

        foreach (var (path, check) in answers)
            lines.Add(check.Fails
                ? $"hatch: {path} - git cannot sign commits here ({check.Why})"
                : $"hatch: {path} - git is not set to sign commits here");

        lines.Add("hatch: fix signing on this machine, or have an owner of those projects allow unsigned commits (the project's settings), then start again");
        return [.. lines];
    }

    /// <summary>
    /// Whether the loop may carry on: false only where the board says no project
    /// it serves allows an unsigned commit. Asked again at a ticket the loop has
    /// just let go for want of a signer, since a checkout that signed at the
    /// start may not now.
    /// </summary>
    public static async Task<bool> NoneAllowAsync(Board board, IReadOnlyList<string> projectKeys, CancellationToken ct)
    {
        var served = await ServedAsync(board, projectKeys, ct);
        return served is not null && !served.Any(p => p.AllowsUnsignedCommits);
    }

    /// <summary>The projects this loop serves, or null where that cannot be told.</summary>
    private static async Task<List<ProjectDto>?> ServedAsync(
        Board board, IReadOnlyList<string> projectKeys, CancellationToken ct)
    {
        try
        {
            var all = await board.ProjectsAsync(ct);
            var wanted = projectKeys.Select(k => k.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
            var served = all.Where(p => wanted.Count == 0 || wanted.Contains(p.Key.ToUpperInvariant())).ToList();
            return served.Count == 0 ? null : served;
        }
        catch (HatchException)
        {
            return null;
        }
    }
}

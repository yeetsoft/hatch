using System.ComponentModel;
using System.Diagnostics;

namespace Hatch.Cli;

/// <summary>
/// One checkout this runner serves: its path, the <c>origin</c> remote it
/// declares itself with, and whether it is the one the process is standing in.
/// </summary>
/// <param name="Remote">
/// Null for a standing checkout with no <c>origin</c> configured - it is still
/// declared, as standing, just with nothing to match a project's binding
/// against.
/// </param>
public sealed record CheckoutEntry(string Path, string? Remote, bool Standing);

/// <summary>
/// Which checkouts a runner serves, and which tree a dispatch resolves to.
/// </summary>
/// <remarks>
/// This story's only source of checkouts is the one the process is standing
/// in. A person naming others, and the clones a runner makes for itself, are
/// later stories on the same epic - <see cref="Discover"/> is where each would
/// add its own entries.
/// </remarks>
public static class Checkouts
{
    public static IReadOnlyList<CheckoutEntry> Discover(string standingRoot) =>
        [new CheckoutEntry(standingRoot, Origin(standingRoot), Standing: true)];

    /// <summary>
    /// <c>git remote get-url origin</c>, swallowing every way that can fail: no
    /// git on <c>PATH</c>, no <c>.git</c> here, no remote named <c>origin</c>.
    /// A checkout without one is still a checkout - just not one a project's
    /// binding can ever match.
    /// </summary>
    private static string? Origin(string root)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("remote");
        start.ArgumentList.Add("get-url");
        start.ArgumentList.Add("origin");

        try
        {
            using var process = Process.Start(start);
            if (process is null) return null;

            var stdout = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0 && stdout.Trim().Length > 0 ? stdout.Trim() : null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// One repository the project binds, resolved against the checkouts this
    /// runner holds - for the prompt's own <c>## Repositories</c> section, which
    /// names every one the project binds whether or not this runner has it.
    /// </summary>
    /// <param name="Path">Null where this runner holds no checkout of it.</param>
    public sealed record RepositoryLine(string? Path, string Remote, bool Primary, string? BaseBranch);

    /// <summary>Where a dispatch spawns, what it resets first, and what the prompt names.</summary>
    /// <param name="Root">The checkout the session is spawned in.</param>
    /// <param name="AddDirs">Every other matched checkout, for <c>--add-dir</c>.</param>
    /// <param name="Resets">Every matched checkout, in the project's own order, with the base branch to reset each to.</param>
    /// <param name="Repositories">Every bound repository, matched or not - empty for an unbound project.</param>
    public sealed record Choice(
        string Root,
        IReadOnlyList<string> AddDirs,
        IReadOnlyList<(string Path, string? BaseBranch)> Resets,
        IReadOnlyList<RepositoryLine> Repositories);

    /// <summary>
    /// Where to spawn an unbound project's dispatch, or one bound to
    /// repositories this runner holds - or null when the primary matched
    /// nothing this runner has, which is the caller's cue to treat the ticket
    /// as having changed under it: the queue said otherwise a moment before.
    /// </summary>
    public static Choice? Choose(
        IReadOnlyList<WorkRepositoryDto> repositories,
        IReadOnlyList<CheckoutEntry> checkouts,
        string standingRoot,
        string? standingBaseBranch)
    {
        if (repositories.Count == 0)
            return new Choice(standingRoot, [], [(standingRoot, standingBaseBranch)], []);

        var primary = repositories.Single(r => r.Primary);
        if (primary.MatchedRemote is not { } primaryRemote) return null;

        var byRemote = checkouts.Where(c => c.Remote is not null).ToDictionary(c => c.Remote!, c => c);
        if (!byRemote.TryGetValue(primaryRemote, out _)) return null;

        // HATCH_BASE_BRANCH beats a binding's own BaseBranch, but only for the
        // checkout it has always applied to - the standing one - wherever that
        // checkout sits in the project's own order. Everywhere else, the
        // binding's BaseBranch is what Workspace.Prepare() resets to, falling
        // back to origin/HEAD there when it too is null.
        var lines = repositories.Select(r =>
        {
            var matched = r.MatchedRemote is { } m && byRemote.TryGetValue(m, out var entry) ? entry : null;
            var baseBranch = matched is { Standing: true } ? standingBaseBranch ?? r.BaseBranch : r.BaseBranch;
            return new RepositoryLine(matched?.Path, r.Remote, r.Primary, baseBranch);
        }).ToList();

        var root = lines.First(l => l.Primary).Path!;
        var addDirs = lines.Where(l => !l.Primary && l.Path is not null).Select(l => l.Path!).ToList();
        var resets = lines.Where(l => l.Path is not null).Select(l => (l.Path!, l.BaseBranch)).ToList();
        return new Choice(root, addDirs, resets, lines);
    }
}

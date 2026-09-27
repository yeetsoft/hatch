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
public static class Checkouts
{
    /// <summary>
    /// The standing checkout, if there is one, followed by every named path -
    /// resolved, deduplicated, and validated before any of it is claimed or
    /// locked.
    /// </summary>
    /// <param name="standingRoot">
    /// The checkout the process is standing in, or null where there is none -
    /// a loop with no checkout of its own, served entirely by <paramref
    /// name="named"/>.
    /// </param>
    /// <param name="named">
    /// Every other checkout this runner serves, in the order named - from
    /// <c>--repo</c> or <c>HATCH_REPOS</c>.
    /// </param>
    /// <returns>
    /// False, with <paramref name="checkouts"/> empty and <paramref
    /// name="refusal"/> naming the path, the moment a named path does not
    /// exist, is not a checkout, or has no <c>origin</c> - before anything is
    /// locked or claimed.
    /// </returns>
    public static bool TryDiscover(
        string? standingRoot,
        IReadOnlyList<string> named,
        out IReadOnlyList<CheckoutEntry> checkouts,
        out string refusal)
    {
        var found = new List<CheckoutEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        refusal = "";
        checkouts = [];

        if (standingRoot is not null)
        {
            found.Add(new CheckoutEntry(standingRoot, Origin(standingRoot), Standing: true));
            seen.Add(Checkout.Canonical(standingRoot));
        }

        foreach (var path in named)
        {
            var canonical = Checkout.Canonical(path);
            if (!seen.Add(canonical)) continue;

            if (!IsCheckout(path, out refusal))
            {
                checkouts = [];
                return false;
            }

            var origin = Origin(path);
            if (origin is null)
            {
                refusal = $"hatch: {path} - has no origin remote, so a project's binding could never match it";
                return false;
            }

            found.Add(new CheckoutEntry(path, origin, Standing: false));
        }

        checkouts = found;
        return true;
    }

    /// <summary>
    /// Whether a path exists and carries a <c>.git</c> - the one check
    /// <c>hatch config --repo</c> makes before it writes a path down, and the
    /// first two <see cref="TryDiscover"/> makes before it ever asks for an
    /// origin.
    /// </summary>
    public static bool IsCheckout(string path, out string refusal)
    {
        if (!Directory.Exists(path))
        {
            refusal = $"hatch: {path} - no such directory";
            return false;
        }

        if (!Directory.Exists(Path.Combine(path, ".git")) && !File.Exists(Path.Combine(path, ".git")))
        {
            refusal = $"hatch: {path} - not a checkout, there is no .git here";
            return false;
        }

        refusal = "";
        return true;
    }

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

        // GroupBy rather than ToDictionary: two checkouts can now share one
        // remote - two clones of the same repository named on one runner -
        // which was impossible when the only source was the standing checkout.
        // The first survivor wins, in discovery's own order (standing, then
        // named in the order given), which is the sensible one.
        var byRemote = checkouts.Where(c => c.Remote is not null)
            .GroupBy(c => c.Remote!).ToDictionary(g => g.Key, g => g.First());
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

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
    /// <param name="workspace">
    /// A directory this runner owns entirely - where <see cref="Clones"/>
    /// clones what the board binds, and where a clone from an earlier
    /// incarnation is found again rather than cloned twice. Null for a runner
    /// with none, which is every one before HA-19.
    /// </param>
    /// <param name="strays">
    /// Every top-level entry under <paramref name="workspace"/> that is not
    /// itself a checkout and holds no checkout anywhere beneath it - printed
    /// rather than refused on, since a workspace a person also drops other
    /// things into is not this runner's mistake to make fatal.
    /// </param>
    public static bool TryDiscover(
        string? standingRoot,
        IReadOnlyList<string> named,
        string? workspace,
        out IReadOnlyList<CheckoutEntry> checkouts,
        out IReadOnlyList<string> strays,
        out string refusal)
    {
        var found = new List<CheckoutEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var strayList = new List<string>();
        refusal = "";
        checkouts = [];
        strays = [];

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

        if (workspace is not null)
        {
            var others = standingRoot is null ? named : (IReadOnlyList<string>)[standingRoot, .. named];
            if (!OverlapsCheckout(workspace, others, out refusal)) return false;
            if (!EnsureWorkspace(workspace, out refusal)) return false;

            foreach (var top in Directory.EnumerateDirectories(workspace))
            {
                var here = WalkForCheckouts(top).ToList();
                if (here.Count == 0)
                {
                    strayList.Add(top);
                    continue;
                }

                foreach (var dir in here)
                {
                    var canonical = Checkout.Canonical(dir);
                    if (!seen.Add(canonical)) continue;

                    // No refusal on a missing origin here, unlike a named path: a
                    // stray `git init` under the workspace is still served, just
                    // unmatchable - the same as a standing checkout with none.
                    found.Add(new CheckoutEntry(dir, Origin(dir), Standing: false));
                }
            }
        }

        checkouts = found;
        strays = strayList;
        return true;
    }

    /// <summary>Every checkout under <paramref name="dir"/>, not descending into one found.</summary>
    private static IEnumerable<string> WalkForCheckouts(string dir)
    {
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            if (Directory.Exists(Path.Combine(sub, ".git")) || File.Exists(Path.Combine(sub, ".git")))
            {
                yield return sub;
                continue;
            }

            foreach (var nested in WalkForCheckouts(sub)) yield return nested;
        }
    }

    /// <summary>Where a clone of <paramref name="canonical"/> lives under <paramref name="workspace"/>.</summary>
    public static string PathFor(string workspace, string canonical)
    {
        var segments = canonical.Split('/');
        segments[0] = segments[0].Replace(':', '_'); // a port's ':' - Windows will not take one in a name
        return Path.Combine([workspace, .. segments]);
    }

    /// <summary>The reverse of <see cref="PathFor"/> - test-only today, kept for symmetry.</summary>
    public static string CanonicalFor(string workspace, string path)
    {
        var relative = Path.GetRelativePath(workspace, path).Replace(Path.DirectorySeparatorChar, '/');
        var slash = relative.IndexOf('/');
        var host = slash < 0 ? relative : relative[..slash];
        return host.Replace('_', ':') + (slash < 0 ? "" : relative[slash..]);
    }

    /// <summary>
    /// Whether <paramref name="path"/> can be made into a directory a clone may
    /// be written under - created if it does not exist, and proved writable.
    /// </summary>
    public static bool EnsureWorkspace(string path, out string refusal)
    {
        refusal = "";
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $".hatch-write-check-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            refusal = $"hatch: {path} - could not be created, or is not writable";
            return false;
        }
    }

    /// <summary>Two owners of one path: a clone under a tree being reset, or a tree under a directory of clones.</summary>
    public static bool OverlapsCheckout(string workspace, IReadOnlyList<string> others, out string refusal)
    {
        var ws = Checkout.Canonical(workspace);
        foreach (var raw in others)
        {
            var other = Checkout.Canonical(raw);
            if (other == ws || IsAncestor(ws, other) || IsAncestor(other, ws))
            {
                refusal = $"hatch: {workspace} and {raw} - one contains the other, and a clone under a tree being " +
                           "reset (or a tree under a directory of clones) is two owners of one path";
                return false;
            }
        }

        refusal = "";
        return true;

        static bool IsAncestor(string a, string b) => b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.Ordinal);
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

        // GroupBy rather than ToDictionary: two checkouts can now share one
        // remote - two clones of the same repository named on one runner -
        // which was impossible when the only source was the standing checkout.
        // The first survivor wins, in discovery's own order (standing, then
        // named in the order given), which is the sensible one.
        var byRemote = checkouts.Where(c => c.Remote is not null)
            .GroupBy(c => c.Remote!).ToDictionary(g => g.Key, g => g.First());

        // A binding's matched checkout: the server's own MatchedRemote first,
        // matched against what it declared - or, for a checkout this pass
        // itself just cloned, which the server never saw, one whose raw Remote
        // happens to equal this binding's own. Safe because a pre-existing
        // checkout whose raw .Remote equalled a binding's Remote exactly would
        // already have MatchedRemote set by the server; the raw fallback can
        // only ever resolve a checkout this pass just cloned.
        CheckoutEntry? Match(WorkRepositoryDto r) =>
            r.MatchedRemote is { } m && byRemote.TryGetValue(m, out var byDeclared)
                ? byDeclared
                : byRemote.TryGetValue(r.Remote, out var byRaw) ? byRaw : null;

        var primary = repositories.Single(r => r.Primary);
        if (Match(primary) is null) return null;

        // HATCH_BASE_BRANCH beats a binding's own BaseBranch, but only for the
        // checkout it has always applied to - the standing one - wherever that
        // checkout sits in the project's own order. Everywhere else, the
        // binding's BaseBranch is what Workspace.Prepare() resets to, falling
        // back to origin/HEAD there when it too is null.
        var lines = repositories.Select(r =>
        {
            var matched = Match(r);
            var baseBranch = matched is { Standing: true } ? standingBaseBranch ?? r.BaseBranch : r.BaseBranch;
            return new RepositoryLine(matched?.Path, r.Remote, r.Primary, baseBranch);
        }).ToList();

        var root = lines.First(l => l.Primary).Path!;
        var addDirs = lines.Where(l => !l.Primary && l.Path is not null).Select(l => l.Path!).ToList();
        var resets = lines.Where(l => l.Path is not null).Select(l => (l.Path!, l.BaseBranch)).ToList();
        return new Choice(root, addDirs, resets, lines);
    }
}

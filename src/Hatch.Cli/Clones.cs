namespace Hatch.Cli;

/// <summary>
/// Where <see cref="Checkouts.Choose"/> and a clone meet: resolve what a
/// dispatch needs, cloning whatever the board binds and this runner has no
/// checkout of yet - shared by <see cref="Picker"/> and <see
/// cref="WorkCommand"/>'s named-ticket path, so a remote is cloned the same
/// way wherever a ticket is picked.
/// </summary>
public static class Clones
{
    /// <param name="Failed">
    /// The remote, the path a clone of it was attempted into, and git's own
    /// last line - set only when a clone failed, which is the caller's cue to
    /// give the ticket back rather than spawn onto a tree that is not there.
    /// </param>
    public sealed record Result(
        Checkouts.Choice? Chosen,
        IReadOnlyList<CheckoutEntry> Checkouts,
        (string Remote, string Path, string Error)? Failed);

    /// <summary>
    /// <see cref="Checkouts.Choose"/>, and - only when that comes back empty
    /// because a bound repository has no checkout here yet, and a workspace
    /// and a way to clone were both given - a clone of each such repository,
    /// followed by <see cref="Checkouts.Choose"/> again over the grown list.
    /// </summary>
    public static Result Resolve(
        WorkDto work, IReadOnlyList<CheckoutEntry> checkouts, string standingRoot, string? standingBaseBranch,
        string? workspace, Func<string, string, IClone>? clone)
    {
        var chosen = Checkouts.Choose(work.Repositories, checkouts, standingRoot, standingBaseBranch);
        if (chosen is not null || workspace is null || clone is null) return new Result(chosen, checkouts, null);

        var list = checkouts.ToList();
        foreach (var r in work.Repositories.Where(r => r.MatchedRemote is null))
        {
            if (list.Any(c => c.Remote == r.Remote)) continue; // already served - this pass or an earlier one

            var path = Checkouts.PathFor(workspace, r.Canonical);
            if (clone(r.Remote, path).Run() is { } error) return new Result(null, list, (r.Remote, path, error));

            list.Add(new CheckoutEntry(path, r.Remote, Standing: false));
        }

        return new Result(Checkouts.Choose(work.Repositories, list, standingRoot, standingBaseBranch), list, null);
    }
}

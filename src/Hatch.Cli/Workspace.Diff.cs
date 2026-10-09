namespace Hatch.Cli;

/// <summary>What a branch changes relative to the trunk, read best-effort.</summary>
public sealed partial class Workspace
{
    /// <summary>
    /// Three dots, not two: what <paramref name="branch"/> itself added since it
    /// forked from the trunk, not a working-tree diff against the trunk's
    /// current tip. <c>refs/remotes/origin/{trunk}</c> rather than the bare
    /// name, the same way <see cref="PushForLimit"/>'s own baseline does, since
    /// the trunk may not have a local branch checked out under that name.
    /// </summary>
    public string? DiffStat(string branch)
    {
        if (BaseBranch() is not { Length: > 0 } trunk) return null;

        var diff = Git("diff", "--stat", $"refs/remotes/origin/{trunk}...{branch}");
        return diff.Ok ? diff.Out.Trim() : null;
    }
}

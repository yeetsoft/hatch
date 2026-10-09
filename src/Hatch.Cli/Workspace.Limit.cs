namespace Hatch.Cli;

/// <summary>
/// The one exception to "nothing here pushes a session's work" - see the
/// remarks on <see cref="Leave"/>. A session that ran out of Claude usage never
/// gets to say whether what it left is fit to publish, so the runner publishes
/// it anyway, onto the issue's own branch and nowhere else.
/// </summary>
public sealed partial class Workspace
{
    public LimitPushed PushForLimit(string key, string title)
    {
        if (!Git("rev-parse", "--git-dir").Ok)
            return new LimitPushed(LimitPush.Refused, null, null, "not a git repository");

        var trunk = BaseBranch();
        var current = Git("rev-parse", "--abbrev-ref", "HEAD").Out.Trim();
        var onTrunk = trunk is { Length: > 0 } && current == trunk;

        var dirty = Git("status", "--porcelain");
        var hasChanges = dirty.Ok && dirty.Out.Trim().Length > 0;

        var baseline =
            onTrunk ? $"refs/remotes/origin/{trunk}"
            : Exists($"refs/remotes/origin/{current}") ? $"refs/remotes/origin/{current}"
            : trunk is { Length: > 0 } ? $"refs/remotes/origin/{trunk}"
            : "HEAD";
        var ahead = Count($"{baseline}..HEAD");

        // Still on the trunk with nothing committed and nothing dirty: there is
        // no session work here, and cutting a branch for it would be a branch
        // with nobody's work on it.
        if (!hasChanges && ahead == 0) return new LimitPushed(LimitPush.Nothing, onTrunk ? null : current, null, null);

        var branch = current;
        if (onTrunk)
        {
            branch = CutName(key, title);
            if (!Git("checkout", "-b", branch).Ok)
                return new LimitPushed(LimitPush.Refused, null, null, $"{branch} would not check out");
        }

        if (hasChanges)
        {
            if (!Git("add", "--all").Ok ||
                !Git("commit", "--quiet", "-m", $"{key}: work in progress when the account ran out of usage").Ok)
                return new LimitPushed(LimitPush.Refused, branch, null, "what was in the tree would not commit");
        }

        var sha = Short("HEAD");

        var push = Git("push", "origin", branch);
        if (!push.Ok) return new LimitPushed(LimitPush.Refused, branch, sha, push.Why);

        return new LimitPushed(LimitPush.Pushed, branch, sha, null);
    }
}

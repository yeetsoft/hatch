namespace Hatch.Cli;

/// <summary>
/// The second half of the slate: not just the trunk, but the issue's own branch
/// where it has one.
/// </summary>
/// <remarks>
/// <para>A ticket that comes back for more work has a branch, and a pull
/// request on it. A session that cuts a new one from the trunk abandons both.
/// Which branch is an issue's is not written anywhere but the branch's name -
/// <c>&lt;key-lowercased&gt;-&lt;slug&gt;</c>, which every pull request here
/// follows - so the name is the rule, and origin is the authority: what is on
/// this machine is only a copy of it, possibly with commits that never left.</para>
///
/// <para>Never rebased, never force-pushed. A branch under review is somebody's
/// to read, and the trunk is merged into it the way a person here always has.</para>
/// </remarks>
public sealed partial class Workspace
{
    /// <summary>
    /// What origin has for a key: the branches that are not in the trunk yet,
    /// and the ones that are.
    /// </summary>
    private (List<string> Open, List<string> Merged) Branched(string key, string trunk)
    {
        var open = new List<string>();
        var merged = new List<string>();

        var refs = Git("for-each-ref", "--format=%(refname:strip=3)", "refs/remotes/origin");
        if (!refs.Ok) return (open, merged);

        foreach (var name in refs.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // origin/HEAD strips to "HEAD", which no key names, and the trunk
            // can be named for a key only by somebody who has been very
            // unlucky - it is never a branch of the issue's own.
            if (name == trunk || !Branches.Names(name, key)) continue;

            (InTrunk(trunk, $"refs/remotes/origin/{name}") ? merged : open).Add(name);
        }

        open.Sort(StringComparer.Ordinal);
        merged.Sort(StringComparer.Ordinal);
        return (open, merged);
    }

    /// <summary>
    /// Whether everything on a branch is already in the trunk. A merge of the
    /// two that comes out as the trunk's own tree says so, and it says so for a
    /// squash merge too - which leaves the branch's commits nowhere in the
    /// trunk's history, so an ancestry test calls the branch unmerged for ever.
    /// </summary>
    private bool InTrunk(string trunk, string branchRef)
    {
        var tip = Git("rev-parse", "--verify", "--quiet", $"refs/remotes/origin/{trunk}^{{tree}}");

        var merge = Git("merge-tree", "--write-tree", $"refs/remotes/origin/{trunk}", branchRef);
        if (merge.Ok && tip.Ok) return merge.Out.Split('\n')[0].Trim() == tip.Out.Trim();

        // Exit 1 is a conflict, which is not a merged branch. Anything else is
        // a git too old to have the command, and the plain answer is the one
        // it can give.
        if (merge.Code == 1) return false;
        return Git("merge-base", "--is-ancestor", branchRef, $"refs/remotes/origin/{trunk}").Ok;
    }

    private bool Exists(string reference) => Git("rev-parse", "--verify", "--quiet", reference).Ok;

    private string Short(string reference) => Git("rev-parse", "--short=9", reference).Out.Trim();

    private int Count(string range)
    {
        var count = Git("rev-list", "--count", range);
        return count.Ok && int.TryParse(count.Out.Trim(), out var n) ? n : 0;
    }

    /// <summary>The name to cut: the wanted one, unless origin or this checkout already has it.</summary>
    private string CutName(string key, string title) =>
        Branches.Free(
            Branches.Cut(key, title),
            name => Exists($"refs/remotes/origin/{name}") || Exists($"refs/heads/{name}"));

    /// <summary>
    /// Which of the candidates to use, or none: the one origin has, or the one
    /// a person named when there were several.
    /// </summary>
    private (BranchEntry? Settled, string? Branch) Settle(
        string key, string title, string trunk, string? answer, bool planned, out List<string> open, out List<string> merged)
    {
        (open, merged) = Branched(key, trunk);

        if (open.Count == 0)
        {
            var cut = CutName(key, title);
            return (new BranchEntry
            {
                Path = root,
                Kind = merged.Count > 0 ? BranchKind.AlreadyMerged : BranchKind.None,
                Merged = merged,
                Cut = cut,
                Planned = planned,
            }, null);
        }

        if (open.Count == 1) return (null, open[0]);

        if (Branches.Chosen(answer, open) is { } chosen) return (null, chosen);

        return (new BranchEntry
        {
            Path = root,
            Kind = BranchKind.Several,
            Candidates = open,
            Merged = merged,
            Decided = !string.IsNullOrWhiteSpace(answer),
            Planned = planned,
        }, null);
    }

    public BranchEntry Enter(string key, string title, string? answer)
    {
        if (BaseBranch() is not { Length: > 0 } trunk)
            return Failed("cannot tell which branch is the trunk here");

        var (settled, branch) = Settle(key, title, trunk, answer, planned: false, out var open, out var merged);
        if (settled is not null)
        {
            say($"hatch:   {settled.Sentence()}");
            return settled;
        }

        ArgumentNullException.ThrowIfNull(branch);

        var remote = $"refs/remotes/origin/{branch}";
        var local = $"refs/heads/{branch}";
        var keptAs = "";
        var localAhead = false;

        if (Exists(local))
        {
            // Left-right so the two directions come back from one call: what
            // origin has that this copy lacks, then what this copy has that
            // origin lacks.
            var sides = Git("rev-list", "--left-right", "--count", $"{remote}...{local}").Out
                .Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            var (behind, ahead) = sides.Length == 2 && int.TryParse(sides[0], out var b) && int.TryParse(sides[1], out var a)
                ? (b, a) : (0, 0);

            if (behind > 0 && ahead > 0)
            {
                // Diverged. Origin is the authority, so the branch goes to
                // origin's tip - and the local tip is kept under a name of its
                // own first, because commits that exist only here are never
                // the runner's to throw away.
                keptAs = $"{branch}-local-{Short(local)}";
                if (!Exists($"refs/heads/{keptAs}") && !Git("branch", keptAs, local).Ok)
                    return Failed($"{branch} has diverged from origin and its local tip would not be kept - left alone");

                complain($"hatch:   {branch} had diverged from origin - the {ahead} local commit(s) are kept as {keptAs}");
                if (!Checkout(branch, remote)) return Failed($"could not put the tree on {branch}");
            }
            else
            {
                localAhead = ahead > 0;
                if (localAhead) say($"hatch:   {branch} is {ahead} commit(s) ahead of origin - the local copy is used");

                if (!Checkout(branch, behind > 0 ? remote : null))
                    return Failed($"could not put the tree on {branch}");
            }
        }
        else if (!Git("checkout", "--quiet", "--track", "-B", branch, remote).Ok)
        {
            return Failed($"could not put the tree on {branch}");
        }

        var sha = Short("HEAD");
        var aheadOfTrunk = Count($"refs/remotes/origin/{trunk}..HEAD");

        var (outcome, conflicted, problem) = Merging(branch, trunk);
        var head = Short("HEAD");

        var entry = new BranchEntry
        {
            Path = root,
            Kind = BranchKind.Entered,
            Branch = branch,
            Sha = sha,
            Head = head,
            Ahead = aheadOfTrunk,
            Merge = outcome,
            Conflicted = conflicted,
            Problem = problem,
            KeptAs = keptAs,
            LocalAhead = localAhead,
            Candidates = open,
            Merged = merged,
        };
        say($"hatch:   {entry.Sentence()}");
        return entry;
    }

    /// <summary>
    /// A checkout onto a branch that is already here, which is two shapes of the
    /// same call: plain, and reset to origin's tip.
    /// </summary>
    private bool Checkout(string branch, string? resetTo) =>
        resetTo is null
            ? Git("checkout", "--quiet", branch).Ok
            : Git("checkout", "--quiet", "-B", branch, resetTo).Ok;

    private BranchEntry Failed(string problem)
    {
        complain($"hatch:   {problem}");
        return new BranchEntry { Path = root, Kind = BranchKind.Failed, Problem = problem };
    }

    /// <summary>
    /// The trunk into the branch, with the marker down first so a merge that is
    /// interrupted - or is left for the session and never finished - is known
    /// for the runner's.
    /// </summary>
    private (MergeOutcome, IReadOnlyList<string>, string) Merging(string branch, string trunk)
    {
        var trunkRef = $"refs/remotes/origin/{trunk}";
        if (Git("merge-base", "--is-ancestor", trunkRef, "HEAD").Ok) return (MergeOutcome.NoOp, [], "");

        var marker = MarkerPath();
        if (marker is not null) File.WriteAllText(marker, branch);

        var merge = Git("merge", "--no-edit", "--quiet", "-m", $"Merge branch '{trunk}' into {branch}", trunkRef);
        if (merge.Ok)
        {
            if (marker is not null) File.Delete(marker);
            return (MergeOutcome.Clean, [], "");
        }

        if (MergeInProgress())
        {
            var files = Git("diff", "--name-only", "--diff-filter=U").Out
                .Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            return (MergeOutcome.Conflicted, files, "");
        }

        if (marker is not null && File.Exists(marker)) File.Delete(marker);
        return (MergeOutcome.Failed, [], merge.Why);
    }

    public BranchEntry Plan(string key, string title, string? answer)
    {
        if (BaseBranch() is not { Length: > 0 } trunk)
            return new BranchEntry { Path = root, Kind = BranchKind.Failed, Problem = "cannot tell which branch is the trunk here", Planned = true };

        var (settled, branch) = Settle(key, title, trunk, answer, planned: true, out var open, out var merged);
        if (settled is not null) return settled;
        ArgumentNullException.ThrowIfNull(branch);

        var remote = $"refs/remotes/origin/{branch}";
        var local = $"refs/heads/{branch}";
        var trunkRef = $"refs/remotes/origin/{trunk}";

        // What the entry would stand on: the local copy where it has commits
        // origin lacks and none it is missing, origin's tip otherwise.
        var tip = remote;
        var localAhead = false;
        if (Exists(local) && Count($"{remote}..{local}") > 0 && Count($"{local}..{remote}") == 0)
        {
            tip = local;
            localAhead = true;
        }

        MergeOutcome outcome;
        IReadOnlyList<string> conflicted = [];

        if (Git("merge-base", "--is-ancestor", trunkRef, tip).Ok)
        {
            outcome = MergeOutcome.NoOp;
        }
        else
        {
            var merge = Git("merge-tree", "--write-tree", "--name-only", tip, trunkRef);
            if (merge.Ok)
            {
                outcome = MergeOutcome.Clean;
            }
            else if (merge.Code == 1)
            {
                outcome = MergeOutcome.Conflicted;
                conflicted = ConflictedFiles(merge.Out);
            }
            else
            {
                outcome = MergeOutcome.Unknown;
            }
        }

        return new BranchEntry
        {
            Path = root,
            Kind = BranchKind.Entered,
            Branch = branch,
            Sha = Short(tip),
            Ahead = Count($"{trunkRef}..{tip}"),
            Merge = outcome,
            Conflicted = conflicted,
            LocalAhead = localAhead,
            Candidates = open,
            Merged = merged,
            Planned = true,
        };
    }

    /// <summary>
    /// The paths in <c>merge-tree --name-only</c>'s output: after the tree's id,
    /// up to the blank line that separates them from git's messages.
    /// </summary>
    internal static IReadOnlyList<string> ConflictedFiles(string output) =>
        output.Split('\n').Skip(1).TakeWhile(l => l.Length > 0).Distinct().ToList();
}

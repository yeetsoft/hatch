namespace Hatch.Cli;

/// <summary>
/// The loop's look at every branch in review: whether it still merges with the
/// trunk, asked of git and told to the board.
/// </summary>
/// <remarks>
/// <para><b>An idle loop must not fetch all night.</b> So the poll asks
/// <c>git ls-remote</c> - one round trip per checkout, and nothing written - and
/// fetches only where a ref it cares about has changed. "A ref it cares about"
/// is a fingerprint: the trunk's sha and every branch the key claims, name and
/// sha. An issue is unchanged when this runner has already taken a verdict at
/// that fingerprint, or when the board's stored <c>clean</c> or
/// <c>conflicted</c> verdict names the one branch at both the shas origin has
/// now - which is what stops a runner that was restarted fetching everything
/// once for nothing.</para>
///
/// <para>The board's verdict cannot vouch for the other two. <c>none</c> and
/// <c>ambiguous</c> carry no branch sha, so a pull request that has merged - the
/// commonest thing in the column - would be fetched every interval if the poll
/// compared what it saw with the last verdict. It remembers the fingerprint
/// instead, in memory, once the board has taken the verdict.</para>
///
/// <para>It runs between passes on the loop's one thread, so it never overlaps
/// a session, and touches no worktree or index, so the tree stays on the trunk.
/// Nothing in it ends a night: every failure is one line, and a line that says
/// what the last poll's did is not said again until something changes.</para>
/// </remarks>
public sealed class Poll
{
    private readonly Dictionary<(string Path, string Key), string> _seen = [];
    private DateTimeOffset? _last;
    private HashSet<string> _saidBefore = [];

    /// <summary>One issue in one checkout, and what the board already holds for it.</summary>
    private sealed record Target(string Key, Checkouts.Polled Where, MergeCheckDto? Stored);

    /// <summary>
    /// Takes the verdicts that are due, if this interval's poll is - once per
    /// <paramref name="intervalSeconds"/>, on the runtime's own clock.
    /// </summary>
    public async Task RunAsync(Runtime runtime, int intervalSeconds, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var now = runtime.Clock.GetUtcNow();
        if (_last is { } last && now - last < TimeSpan.FromSeconds(intervalSeconds)) return;
        _last = now;

        var saying = new HashSet<string>();

        // A line said by the poll before this one is not said again: an origin
        // that is down for the night is one line and not four hundred.
        void Complain(string line)
        {
            saying.Add(line);
            if (!_saidBefore.Contains(line)) runtime.Say.Complain(line);
        }

        try
        {
            await PollAsync(runtime, Complain, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Nothing the poll does is worth a night: the pass that follows
            // works whatever the board says is workable, poll or no poll.
            Complain($"hatch: could not check the branches in review - {e.Message}");
        }

        _saidBefore = saying;
    }

    private async Task PollAsync(Runtime runtime, Action<string> complain, CancellationToken ct)
    {
        IReadOnlyList<ReviewCheckDto> review;
        try
        {
            review = await runtime.Board.ReviewAsync(runtime.Checkouts, ct);
        }
        catch (HatchException e)
        {
            complain($"hatch: could not read what is in review to check - {e.Message}");
            return;
        }

        var targets = new Dictionary<(string Path, string? BaseBranch), List<Target>>();
        foreach (var issue in review)
        {
            foreach (var where in Checkouts.ToPoll(issue.Repositories, runtime.Checkouts, runtime.Settings.BaseBranch))
            {
                if (where.Remote is null)
                {
                    complain($"hatch: {where.Path} has no origin remote, so there is nothing to report a verdict under");
                    continue;
                }

                // The board's verdict for this repository: by the identity it
                // gave where the project binds one, and by the spelling this
                // runner used where it does not - a different spelling costs
                // one fetch, once, and never a wrong answer.
                var stored = issue.MergeChecks.FirstOrDefault(c =>
                    where.Canonical is not null ? c.Canonical == where.Canonical : c.Remote == where.Remote);

                var key = (where.Path, where.BaseBranch);
                if (!targets.TryGetValue(key, out var list)) targets[key] = list = [];
                list.Add(new Target(issue.Key, where, stored));
            }
        }

        foreach (var ((path, baseBranch), issues) in targets)
        {
            // Between checkouts, where a fetch has just taken however long it
            // took: an interrupt is answered here and not a poll later.
            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileName(path.TrimEnd('/', '\\'));
            var workspace = runtime.Workspace(path, baseBranch);

            if (workspace.Heads() is not { } heads)
            {
                complain($"hatch: origin did not answer for {name}, so its branches in review were not checked");
                continue;
            }

            var moved = new List<(Target Target, string Fingerprint)>();
            foreach (var target in issues)
            {
                if (heads.Fingerprint(target.Key) is not { } fingerprint)
                {
                    complain($"hatch: origin has no {heads.Trunk} for {name}, so its branches in review were not checked");
                    break;
                }

                if (_seen.TryGetValue((path, target.Key), out var seen) && seen == fingerprint) continue;

                if (Vouches(target.Stored, heads, target.Key))
                {
                    _seen[(path, target.Key)] = fingerprint;
                    continue;
                }

                moved.Add((target, fingerprint));
            }

            if (moved.Count == 0) continue;

            // Once, however many of the checkout's issues moved.
            if (!workspace.Fetch())
            {
                complain($"hatch: could not fetch from origin for {name}, so its branches in review were not checked");
                continue;
            }

            var unknown = 0;
            foreach (var (target, fingerprint) in moved)
            {
                if (workspace.Check(target.Key) is not { } verdict)
                {
                    unknown++;
                    continue;
                }

                try
                {
                    await runtime.Board.MergeCheckAsync(
                        target.Key, verdict.ToRequest(target.Where.Remote!, runtime.RunnerName), ct);
                }
                catch (HatchException e)
                {
                    // Not remembered: a verdict the board refused is asked
                    // again next interval.
                    complain($"hatch: {target.Key} - the board would not take the verdict on its branch - {e.Message}");
                    continue;
                }

                _seen[(path, target.Key)] = fingerprint;

                if (target.Stored is null || !verdict.Says(target.Stored))
                    runtime.Say.Line($"hatch: {verdict.Words(target.Key)}");
            }

            if (unknown > 0)
                complain($"hatch: {unknown} branch(es) in review in {name} could not be checked against {heads.Trunk}");
        }
    }

    /// <summary>
    /// Whether the board's stored verdict is still about what origin has: it
    /// names the one branch the key claims, at the trunk's and the branch's
    /// shas as they are now. Only <c>clean</c> and <c>conflicted</c> can - the
    /// other two carry no branch sha to compare.
    /// </summary>
    private static bool Vouches(MergeCheckDto? stored, RemoteHeads heads, string key)
    {
        if (stored is not { Verdict: MergeVerdicts.Clean or MergeVerdicts.Conflicted, Branch: { } branch }) return false;

        return heads.Candidates(key) is [var only]
            && only == branch
            && stored.BranchSha == heads.Shas[only]
            && stored.TrunkSha == heads.TrunkSha
            && stored.Trunk == heads.Trunk;
    }
}

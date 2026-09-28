namespace Hatch.Cli;

/// <summary>
/// The loop's look at every branch in review: whether it still merges with the
/// trunk, asked of git, and what the build on its tip came to, asked of the
/// runner's own <c>gh</c> - both told to the board.
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
/// <para><b>The build half asks about one sha, until its build concludes.</b>
/// <c>ls-remote</c> has already given every branch's tip, so it needs no fetch.
/// A build that has concluded on a sha does not change, short of a re-run, so a
/// <c>passed</c> or <c>failed</c> verdict is asked about once - and a board that
/// already holds one for the tip vouches for it, which is what stops a runner
/// that was restarted asking again. <c>pending</c> is asked again next interval.
/// <c>none</c> is asked again for <see cref="NoneWindow"/> after the board first
/// heard about the sha, because a push's checks take a few seconds to appear and
/// a <c>none</c> straight after one is usually premature; past it a repository
/// with no CI stops costing two <c>gh</c> calls an interval. A runner that
/// cannot read builds says so once and reads nothing, and nothing about it fails
/// the merge half, a pass or a night.</para>
///
/// <para>It runs between passes on the loop's one thread, so it never overlaps
/// a session, and touches no worktree or index, so the tree stays on the trunk.
/// Nothing in it ends a night: every failure is one line, and a line that says
/// what the last poll's did is not said again until something changes.</para>
/// </remarks>
public sealed class Poll
{
    /// <summary>How long a sha that reads <c>none</c> is asked about again, counted from when the board first heard about it.</summary>
    public static readonly TimeSpan NoneWindow = TimeSpan.FromMinutes(10);

    private readonly Dictionary<(string Path, string Key), string> _seen = [];
    private readonly Dictionary<(string Path, string Key), string> _seenBuild = [];
    private DateTimeOffset? _last;
    private HashSet<string> _saidBefore = [];

    /// <summary>One issue in one checkout, and what the board already holds for it.</summary>
    private sealed record Target(string Key, Checkouts.Polled Where, MergeCheckDto? Stored, BuildCheckDto? StoredBuild);

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
                var storedBuild = (issue.BuildChecks ?? []).FirstOrDefault(c =>
                    where.Canonical is not null ? c.Canonical == where.Canonical : c.Remote == where.Remote);

                list.Add(new Target(issue.Key, where, stored, storedBuild));
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

            // The merge half, and then the build half whatever it did: a build
            // that is still pending is asked about again on a branch that has
            // not moved, and neither half is the other's reason to stop.
            await CheckMergesAsync(runtime, complain, path, name, workspace, heads, issues, ct);
            await ReadBuildsAsync(runtime, complain, path, name, heads, issues, ct);
        }
    }

    /// <summary>The merge half for one checkout: fetch once if anything moved, and report the verdicts that are due.</summary>
    private async Task CheckMergesAsync(
        Runtime runtime, Action<string> complain, string path, string name, IWorkspace workspace,
        RemoteHeads heads, List<Target> issues, CancellationToken ct)
    {
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

        if (moved.Count == 0) return;

        // Once, however many of the checkout's issues moved.
        if (!workspace.Fetch())
        {
            complain($"hatch: could not fetch from origin for {name}, so its branches in review were not checked");
            return;
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

    /// <summary>
    /// The build half for one checkout: for each issue whose branch is the one
    /// branch origin has for it, read the build on its tip unless something
    /// already vouches for it, and report what it came to.
    /// </summary>
    /// <remarks>
    /// Nothing in here throws out of the poll: a forge that cannot answer is one
    /// line per checkout and the rest of the checkout's issues are left alone
    /// this interval - one <c>gh</c> call that failed, not one per issue - and
    /// anything else is a line and the next checkout.
    /// </remarks>
    private async Task ReadBuildsAsync(
        Runtime runtime, Action<string> complain, string path, string name, RemoteHeads heads,
        List<Target> issues, CancellationToken ct)
    {
        try
        {
            foreach (var target in issues)
            {
                ct.ThrowIfCancellationRequested();

                // Exactly one branch: the merge check that says which is the
                // issue's is what made it an issue with a branch at all, and a
                // build is about a tip, not about a guess between two.
                if (heads.Candidates(target.Key) is not [var branch]) continue;
                var sha = heads.Shas[branch];

                if (_seenBuild.TryGetValue((path, target.Key), out var seen) && seen == sha) continue;

                if (VouchesBuild(target.StoredBuild, sha))
                {
                    _seenBuild[(path, target.Key)] = sha;
                    continue;
                }

                var answer = await runtime.Forge(path, target.Where.Canonical).ReadAsync(sha, ct);
                if (answer.Read is not { } read)
                {
                    // The checkout and not the issue or the sha, so that the
                    // poll's dedupe holds however many issues are in review.
                    if (answer.Why is { } why) complain($"hatch: could not read builds in {name} - {why}");
                    return;
                }

                BuildCheckDto? kept;
                try
                {
                    kept = await runtime.Board.BuildCheckAsync(
                        target.Key,
                        new BuildCheckRequest(
                            target.Where.Remote!, branch, sha, read.Verdict,
                            read.Failing.Select(f => new FailingCheckDto(f.Name, f.Url)).ToList(),
                            runtime.RunnerName),
                        ct);
                }
                catch (HatchException e)
                {
                    // Not remembered: a verdict the board refused is asked
                    // again next interval. A board that predates build checks
                    // answers this way for every issue, and says so once.
                    complain($"hatch: {target.Key} - the board would not take the build on its branch - {e.Message}");
                    continue;
                }

                // What the board says it now holds, which is what says when it
                // first heard about the sha. A board that answered nothing has
                // only what was read: a none is asked about again.
                var concluded = kept is not null
                    ? Concluded(kept)
                    : read.Verdict is BuildVerdicts.Passed or BuildVerdicts.Failed;
                if (concluded) _seenBuild[(path, target.Key)] = sha;

                var was = target.StoredBuild;
                if (was is null || was.Sha != sha || was.Verdict != read.Verdict)
                    runtime.Say.Line($"hatch: {BuildWords(target.Key, sha, read)}");
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            complain($"hatch: could not read builds in {name} - {e.Message}");
        }
    }

    /// <summary>
    /// Whether the board's stored verdict is still the answer for this tip: it is
    /// about the same sha and is <c>passed</c> or <c>failed</c>, or is <c>none</c>
    /// and was read ten minutes or more after the board first heard about the
    /// sha. <c>pending</c> never is.
    /// </summary>
    private static bool VouchesBuild(BuildCheckDto? stored, string sha) =>
        stored is not null && stored.Sha == sha && Concluded(stored);

    /// <summary>
    /// Whether a verdict is one that will not change without a re-run.
    /// <c>CheckedAt</c> and not now: it stops asking only after a read that was
    /// itself taken past the window.
    /// </summary>
    private static bool Concluded(BuildCheckDto check) => check.Verdict switch
    {
        BuildVerdicts.Passed or BuildVerdicts.Failed => true,
        BuildVerdicts.None => check.CheckedAt - check.ShaSince >= NoneWindow,
        _ => false,
    };

    /// <summary>A line for the terminal: <c>HA-12 build on 1a2b3c4 failed (api, CI)</c>.</summary>
    private static string BuildWords(string key, string sha, BuildRead read)
    {
        var at = sha.Length > 7 ? sha[..7] : sha;

        return read.Verdict switch
        {
            BuildVerdicts.Failed => $"{key} build on {at} failed ({string.Join(", ", read.Failing.Select(f => f.Name))})",
            BuildVerdicts.Passed => $"{key} build on {at} passed",
            BuildVerdicts.Pending => $"{key} build on {at} is still running",
            _ => $"{key} build on {at} - no checks ran",
        };
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

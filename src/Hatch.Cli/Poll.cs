namespace Hatch.Cli;

/// <summary>
/// The loop's look at every branch in review: whether it still merges with the
/// trunk, asked of git, and what the build on its tip came to, asked of the
/// runner's own <c>gh</c> - both told to the board. Alongside it, for every
/// checkout with an origin whether or not anything of its is in review, the
/// build on the trunk's own tip (HA-95).
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
/// <para><b>The trunk half runs whether or not anything is in review.</b> The
/// merge and build halves above visit only a checkout whose review issues name
/// it, because their targets come from <c>/api/hatch/work/review</c> - a quiet
/// board would otherwise never have its trunk read at all. So, separately,
/// every checkout <see cref="Runtime.Checkouts"/> holds that has an origin gets
/// its trunk's build read the same way the branch half reads a tip's, reusing
/// the <c>ls-remote</c> heads the review half already took where there were
/// any. A trunk carries no issue for its verdict to ride in on, so the board's
/// stored trunk verdicts are read once a poll, and matched to a checkout by the
/// remote it spells - a project's canonical for it once the board has told
/// this runner one, else null - the same fallback the branch half's <c>where
/// the project binds one, else the remote it spelled</c> is.</para>
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
    private readonly Dictionary<string, string> _seenTrunk = [];
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
            review = await runtime.QuietBoard.ReviewAsync(runtime.Checkouts, ct);
        }
        catch (HatchException e)
        {
            complain($"hatch: could not read what is in review to check - {e.Message}");
            return;
        }

        // The pull request half, independent of the targets/headsByPath
        // machinery below it: keyed on the issue rather than the checkout,
        // since PullRequestUrl is one field on one issue however many
        // repositories its project binds.
        await ReadPullRequestsAsync(runtime, complain, review, ct);

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

        // Remembered so the trunk half below can reuse the ls-remote a
        // checkout's review issues already took, rather than asking origin a
        // second time for the same answer - null for a checkout origin did
        // not answer for, so the trunk half knows not to ask again either.
        var headsByPath = new Dictionary<string, RemoteHeads?>();

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
                headsByPath[path] = null;
                continue;
            }

            headsByPath[path] = heads;

            // The merge half, and then the build half whatever it did: a build
            // that is still pending is asked about again on a branch that has
            // not moved, and neither half is the other's reason to stop.
            await CheckMergesAsync(runtime, complain, path, name, workspace, heads, issues, ct);
            await ReadBuildsAsync(runtime, complain, path, name, heads, issues, ct);
        }

        // The trunk half: every checkout with an origin, whether or not
        // anything of its is in review - a repository's trunk is nobody's
        // issue, and a quiet board must not go all night without it.
        await ReadTrunksAsync(runtime, complain, headsByPath, ct);
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
                await runtime.QuietBoard.MergeCheckAsync(
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
                    kept = await runtime.QuietBoard.BuildCheckAsync(
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
    /// The trunk half: for every checkout with an origin, read the build on its
    /// trunk's tip unless something already vouches for it, and report what it
    /// came to. A trunk is nobody's issue, so its verdicts are matched by
    /// remote and trunk name rather than ridden in on a review row, and read
    /// once for the whole poll rather than once per checkout.
    /// </summary>
    /// <remarks>
    /// Copies <see cref="ReadBuildsAsync"/>'s rules for when to ask again - see
    /// <see cref="ConcludedTrunk"/> - and, like it, throws nothing out of the
    /// poll: a forge or a board that cannot answer is one line per checkout and
    /// the rest are left alone this interval.
    /// </remarks>
    private async Task ReadTrunksAsync(
        Runtime runtime, Action<string> complain, IReadOnlyDictionary<string, RemoteHeads?> headsByPath,
        CancellationToken ct)
    {
        var checkouts = runtime.Checkouts.Where(c => c.Remote is not null).ToList();
        if (checkouts.Count == 0) return;

        IReadOnlyList<TrunkBuildDto> stored;
        try
        {
            stored = await runtime.QuietBoard.TrunkBuildsAsync(ct);
        }
        catch (HatchException e)
        {
            complain($"hatch: could not read the board's trunk builds - {e.Message}");
            return;
        }

        foreach (var checkout in checkouts)
        {
            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileName(checkout.Path.TrimEnd('/', '\\'));

            try
            {
                RemoteHeads? heads;
                if (!headsByPath.TryGetValue(checkout.Path, out heads))
                {
                    heads = runtime.Workspace(checkout.Path, runtime.Settings.BaseBranch).Heads();
                    if (heads is null)
                    {
                        complain($"hatch: origin did not answer for {name}, so its trunk build was not checked");
                        continue;
                    }
                }

                // Null either because origin did not answer for a checkout the
                // review half already tried, or because this trunk has no
                // branch of that name on origin - the first is already said,
                // and the second has nothing to build.
                if (heads?.TrunkSha is not { } sha) continue;

                if (_seenTrunk.TryGetValue(checkout.Path, out var seen) && seen == sha) continue;

                var matched = stored.FirstOrDefault(t => t.Remote == checkout.Remote && t.Trunk == heads.Trunk);

                if (VouchesTrunk(matched, sha))
                {
                    _seenTrunk[checkout.Path] = sha;
                    continue;
                }

                var answer = await runtime.Forge(checkout.Path, matched?.Canonical).ReadAsync(sha, ct);
                if (answer.Read is not { } read)
                {
                    if (answer.Why is { } why) complain($"hatch: could not read the trunk build in {name} - {why}");
                    continue;
                }

                TrunkBuildDto? kept;
                try
                {
                    kept = await runtime.QuietBoard.TrunkBuildAsync(
                        new TrunkBuildRequest(
                            checkout.Remote!, heads.Trunk, sha, read.Verdict,
                            read.Failing.Select(f => new FailingCheckDto(f.Name, f.Url)).ToList(),
                            runtime.RunnerName),
                        ct);
                }
                catch (HatchException e)
                {
                    // Not remembered: a verdict the board refused is asked
                    // again next interval.
                    complain($"hatch: the board would not take the trunk build in {name} - {e.Message}");
                    continue;
                }

                var concluded = kept is not null
                    ? ConcludedTrunk(kept)
                    : read.Verdict is BuildVerdicts.Passed or BuildVerdicts.Failed;
                if (concluded) _seenTrunk[checkout.Path] = sha;

                if (matched is null || matched.Sha != sha || matched.Verdict != read.Verdict)
                    runtime.Say.Line($"hatch: {TrunkWords(heads.Trunk, sha, read)}");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                complain($"hatch: could not read the trunk build in {name} - {e.Message}");
            }
        }
    }

    /// <summary>
    /// The pull request half: for every issue in review that carries a
    /// <c>pullRequestUrl</c>, ask <c>gh</c> what it reads and, on <c>merged</c>,
    /// advance the issue. There is no stored verdict and no window logic like
    /// <see cref="NoneWindow"/> - an open pull request is asked about every
    /// interval by design, and a merged one stops appearing in
    /// <c>/api/hatch/work/review</c> at all, so nothing asks again.
    /// </summary>
    /// <remarks>
    /// Keyed on the issue and not the checkout, unlike the three halves above
    /// it: a merge or build verdict is per repository, but a pull request url is
    /// one field on one issue however many repositories its project binds - so
    /// the checkout is only somewhere for <c>gh</c> to run, taken as the first
    /// one <see cref="Checkouts.ToPoll"/> yields for the issue. <c>failed</c>
    /// is the per-checkout dedupe the other halves get for free by being
    /// grouped by checkout already: the first failing <c>gh</c> call for a path
    /// adds it, and every later issue on that path is skipped silently for the
    /// rest of this poll. The outer try/catch keeps anything unanticipated here
    /// from propagating into <see cref="PollAsync"/> and aborting the halves
    /// that run after it.
    /// </remarks>
    private async Task ReadPullRequestsAsync(
        Runtime runtime, Action<string> complain, IReadOnlyList<ReviewCheckDto> review, CancellationToken ct)
    {
        var failed = new HashSet<string>();
        try
        {
            foreach (var issue in review)
            {
                if (issue.PullRequestUrl is not { Length: > 0 } url) continue;
                ct.ThrowIfCancellationRequested();

                var where = Checkouts.ToPoll(issue.Repositories, runtime.Checkouts, runtime.Settings.BaseBranch)
                    .FirstOrDefault();
                if (where is null || failed.Contains(where.Path)) continue;

                var name = Path.GetFileName(where.Path.TrimEnd('/', '\\'));
                var answer = await runtime.Forge(where.Path, where.Canonical).ReadPullRequestAsync(url, ct);
                if (answer.State is not { } state)
                {
                    failed.Add(where.Path);
                    if (answer.Why is { } why) complain($"hatch: could not read pull requests in {name} - {why}");
                    continue;
                }

                if (state != PullRequestStates.Merged) continue;

                (IssueDto? Issue, string? WalkOn) result;
                try
                {
                    result = await runtime.QuietBoard.MergedAsync(
                        issue.Key, new PullRequestMergedRequest(url, runtime.RunnerName), ct);
                }
                catch (HatchException e)
                {
                    complain($"hatch: {issue.Key} - the board would not take the merge - {e.Message}");
                    continue;
                }

                if (result.WalkOn is not null) continue;

                var to = await ResolvedColumnNameAsync(runtime, result.Issue!.StatusId, ct);
                runtime.Say.Line($"hatch: {PullRequestWords(issue.Key, to)}");
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            complain($"hatch: could not read pull requests - {e.Message}");
        }
    }

    /// <summary>The column a merged pull request advanced its issue into, by name - <c>"?"</c> where it cannot be read.</summary>
    private static async Task<string> ResolvedColumnNameAsync(Runtime runtime, int statusId, CancellationToken ct)
    {
        try
        {
            var statuses = await runtime.QuietBoard.StatusesAsync(ct);
            return statuses.FirstOrDefault(s => s.Id == statusId)?.Name ?? "?";
        }
        catch (HatchException)
        {
            return "?";
        }
    }

    /// <summary>A line for the terminal: <c>HA-12 pull request merged -> Done</c>.</summary>
    private static string PullRequestWords(string key, string to) => $"{key} pull request merged -> {to}";

    /// <summary>The trunk equivalent of <see cref="VouchesBuild"/>.</summary>
    private static bool VouchesTrunk(TrunkBuildDto? stored, string sha) =>
        stored is not null && stored.Sha == sha && ConcludedTrunk(stored);

    /// <summary>The trunk equivalent of <see cref="Concluded"/>.</summary>
    private static bool ConcludedTrunk(TrunkBuildDto check) => check.Verdict switch
    {
        BuildVerdicts.Passed or BuildVerdicts.Failed => true,
        BuildVerdicts.None => check.CheckedAt - check.ShaSince >= NoneWindow,
        _ => false,
    };

    /// <summary>The trunk equivalent of <see cref="BuildWords"/>: <c>main build on 1a2b3c4 failed (api, CI)</c>.</summary>
    private static string TrunkWords(string trunk, string sha, BuildRead read)
    {
        var at = sha.Length > 7 ? sha[..7] : sha;

        return read.Verdict switch
        {
            BuildVerdicts.Failed => $"{trunk} build on {at} failed ({string.Join(", ", read.Failing.Select(f => f.Name))})",
            BuildVerdicts.Passed => $"{trunk} build on {at} passed",
            BuildVerdicts.Pending => $"{trunk} build on {at} is still running",
            _ => $"{trunk} build on {at} - no checks ran",
        };
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
        if (stored.Verdict == MergeVerdicts.Clean && stored.HoldsTrunk is null) return false;

        return heads.Candidates(key) is [var only]
            && only == branch
            && stored.BranchSha == heads.Shas[only]
            && stored.TrunkSha == heads.TrunkSha
            && stored.Trunk == heads.Trunk;
    }
}

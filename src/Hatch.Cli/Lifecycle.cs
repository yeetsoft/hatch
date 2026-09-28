namespace Hatch.Cli;

/// <summary>What entering the issue's branch across every checkout came to.</summary>
/// <param name="Entries">One per checkout the increment resets, in the project's order.</param>
/// <param name="Asked">Somebody has to say which branch, the question is on the ticket, and nothing is to be spawned.</param>
public sealed record Entering(IReadOnlyList<BranchEntry> Entries, bool Asked);

/// <summary>
/// The git half of an increment, done by the runner: the branch the session
/// starts on, and the state it leaves the tree in - the same for the loop and for
/// <c>hatch work</c>, so that one rule decides both.
/// </summary>
/// <remarks>
/// Both callers reset the trunk themselves, because what a reset that would not
/// happen means differs between them. Everything from there on is here.
/// </remarks>
public sealed class Lifecycle(Runtime runtime)
{
    /// <summary>
    /// The branch, in every checkout the increment serves. A checkout that has
    /// no branch for the key stays on the trunk and is told what to cut.
    /// </summary>
    public async Task<Entering> EnterAsync(WorkDto work, Checkouts.Choice chosen, CancellationToken ct)
    {
        var key = work.Issue.Key;
        var answer = Answer(work);

        var entries = chosen.Resets
            .Select(r => runtime.Workspace(r.Path, r.BaseBranch).Enter(key, work.Issue.Title, answer))
            .ToList();

        // Two or more branches and nobody has said which: the runner does not
        // guess. This is off the happy path, and a guess is how a night comes to
        // have a branch for every increment.
        var undecided = entries.Where(e => e is { Kind: BranchKind.Several, Decided: false }).ToList();
        if (undecided.Count == 0) return new Entering(entries, false);

        var candidates = undecided.SelectMany(e => e.Candidates).Distinct().ToList();
        var offered = candidates.Take(QuestionOptionDto.MaxPerQuestion).ToList();

        runtime.Say.Line("");
        runtime.Say.Line($"hatch: {key} has {candidates.Count} unmerged branches on origin, and it is not the runner's to choose:");
        foreach (var name in candidates) runtime.Say.Line($"hatch:   {name}");

        try
        {
            await runtime.Board.AskAsync(
                key,
                Branches.Question(key),
                offered.Select(name => new QuestionOptionDto(name, "On origin, and not merged into the trunk yet.")).ToList(),
                ct);
            runtime.Say.Line($"hatch:   asked on {key}; nothing is spawned until somebody answers");
        }
        catch (Exception e) when (e is HatchException or OperationCanceledException)
        {
            runtime.Say.Complain($"hatch: {key} could not be asked which branch - {e.Message}");
        }

        return new Entering(entries, true);
    }

    /// <summary>
    /// What <see cref="EnterAsync"/> would do, without doing any of it - the
    /// entries a dry run composes its prompt from.
    /// </summary>
    public IReadOnlyList<BranchEntry> Plan(WorkDto work, Checkouts.Choice chosen)
    {
        var answer = Answer(work);
        return chosen.Resets
            .Select(r => runtime.Workspace(r.Path, r.BaseBranch).Plan(work.Issue.Key, work.Issue.Title, answer))
            .ToList();
    }

    /// <summary>
    /// The last answer to a which-branch question on the ticket, whichever it
    /// was - carried in the prompt too, under the decisions already made.
    /// </summary>
    private static string? Answer(WorkDto work) =>
        work.Questions
            .Where(q => q.Body.StartsWith(Branches.Question(work.Issue.Key), StringComparison.Ordinal) && q.Answers.Count > 0)
            .Select(q => q.Answers[^1].Body)
            .LastOrDefault();

    /// <summary>
    /// Leave every checkout the way the next increment expects it, and tell the
    /// ticket what was found - in one comment, so a reviewer reads one thing.
    /// </summary>
    /// <param name="ownsTicket">
    /// False when the lease went to somebody else: nothing is written on their
    /// increment, and nothing is pushed for it. The trees still go back to the
    /// trunk, because a restart between increments must build from there.
    /// </param>
    public async Task LeaveAsync(WorkDto work, Checkouts.Choice chosen, bool ownsTicket, CancellationToken ct)
    {
        var key = work.Issue.Key;
        var lines = new List<string>();

        try
        {
            if (!ownsTicket)
            {
                foreach (var (path, baseBranch) in chosen.Resets) runtime.Workspace(path, baseBranch).Return();
                return;
            }

            var pullRequest = await HasPullRequestAsync(key, ct);

            foreach (var (path, baseBranch) in chosen.Resets)
            {
                var left = runtime.Workspace(path, baseBranch).Leave(key, pullRequest);
                var where = chosen.Resets.Count > 1 ? $"{Path.GetFileName(path.TrimEnd('/', '\\'))}: " : "";
                lines.AddRange(left.Notes.Select(n => $"- {where}{n}"));

                if (left.Found is { } found) await ReportAsync(key, chosen, path, found, ct);
            }

            if (lines.Count == 0) return;

            await runtime.Board.CommentAsync(
                key, "The runner tidied the tree after this increment:\n\n" + string.Join('\n', lines), ct);
        }
        catch (Exception e)
        {
            // Nothing here fails an increment: the work happened, and what is
            // left is housekeeping that the next reset does as well.
            runtime.Say.Complain($"hatch: {key} - the tree could not be left tidy, or the ticket told - {e.Message}");
        }
    }

    /// <summary>
    /// Whether the conflict that made this a conflict dispatch is still there,
    /// asked of git against origin as it stands now - and told to the board.
    /// </summary>
    /// <remarks>
    /// Called after the reset, which fetched, so the refs are current: the board's
    /// verdict was read before the claim and the fetch, and may be minutes old.
    /// It is the recheck that decides whether a session is spent, and it is one
    /// call here so that the loop and <c>hatch work</c> cannot decide differently.
    /// </remarks>
    public Task<Rechecked> RecheckAsync(WorkDto work, Checkouts.Choice chosen, CancellationToken ct) =>
        CheckAllAsync(work, chosen, fetch: false, ct);

    /// <summary>
    /// The same question after the session, when it is the increment's verdict:
    /// each checkout is fetched first, because the session pushed and the refs
    /// have to be about origin's branch as it is now.
    /// </summary>
    public Task<Rechecked> JudgeAsync(WorkDto work, Checkouts.Choice chosen, CancellationToken ct) =>
        CheckAllAsync(work, chosen, fetch: true, ct);

    /// <summary>
    /// Whether the build that made this a build dispatch is still failing on the
    /// sha it was about, read off origin's tip as it stands now - and told to the
    /// board.
    /// </summary>
    /// <remarks>
    /// <para>Called after the reset, before anything is spawned: the board's
    /// verdict was read before the claim and may be minutes old, and a push may
    /// have landed since. For each checkout that holds a failed verdict it takes
    /// the branch's tip from <c>ls-remote</c> - which touches no tree - reads the
    /// build on it, and reports what it found, whether or not the tip moved. A
    /// moved tip whose new build fails is picked up as fresh build work by the
    /// next pass, which is right.</para>
    ///
    /// <para>Only where something is still red on the dispatched sha is each
    /// failing check's log fetched, so the prompt is composed from what was just
    /// read and nothing is spent on a log nobody will use. A forge that cannot
    /// answer is unknown, and not clear. It is one call here so that the loop and
    /// <c>hatch work</c> cannot decide differently.</para>
    /// </remarks>
    public async Task<BuildFound> RecheckBuildAsync(WorkDto work, Checkouts.Choice chosen, CancellationToken ct)
    {
        var key = work.Issue.Key;
        var repos = new List<BuiltRepo>();
        var unknown = false;
        var reported = true;

        foreach (var (path, baseBranch) in chosen.Resets)
        {
            var name = Path.GetFileName(path.TrimEnd('/', '\\'));
            var remote = RemoteFor(chosen, path);
            var canonical = CanonicalFor(work, chosen, path);

            // Only a repository that holds a failed verdict: another repository's
            // build is not this dispatch's to read.
            var failed = Builds.Of(work.Issue).FirstOrDefault(b =>
                canonical is not null ? b.Canonical == canonical : b.Remote == remote);
            if (failed is null) continue;

            var heads = runtime.Workspace(path, baseBranch).Heads();

            // The merge check that made this build work said one branch, and
            // origin may have changed since.
            if (heads is null || heads.Candidates(key) is not [var branch])
            {
                runtime.Say.Complain($"hatch: {key} - origin's branch for it could not be read in {name}, so its build was not checked");
                unknown = true;
                continue;
            }

            var tip = heads.Shas[branch];

            var forge = runtime.Forge(path, canonical);
            var answer = await forge.ReadAsync(tip, ct);
            if (answer.Read is not { } read)
            {
                runtime.Say.Complain($"hatch: {key} - could not read its build in {name}{(answer.Why is { } why ? $" - {why}" : "")}");
                unknown = true;
                continue;
            }

            if (!await ReportBuildAsync(key, remote, branch, tip, read, pushedByIncrement: false, ct)) reported = false;

            var failing = new List<FailingBuild>();
            if (tip == failed.Sha && read.Verdict == BuildVerdicts.Failed)
            {
                foreach (var check in read.Failing)
                    failing.Add(new FailingBuild(check.Name, check.Url, await forge.LogAsync(check, ct), check.JobId));
            }

            repos.Add(new BuiltRepo(path, remote, branch, failed.Sha, tip, read.Verdict, failing));
        }

        return new BuildFound(repos, unknown, reported);
    }

    /// <summary>
    /// What the session did to the branch, asked of origin after it ended: is the
    /// tip still the one the dispatch was about? Reads the tip with
    /// <c>ls-remote</c> and fetches nothing.
    /// </summary>
    /// <remarks>
    /// Any checkout whose tip moved is a fix pushed, and the new tip is told to
    /// the board as <c>pending</c> and marked as a build increment's - which is
    /// what lets the board open the failed-again question if the build on it
    /// fails. It is done here and not in <see cref="Increment"/> because the
    /// remote is <see cref="Lifecycle"/>'s to name, and <see cref="Increment"/>
    /// stays free of git. Judged before <see cref="LeaveAsync"/>, so a trunk merge
    /// the loop pushes on the way out is never taken for the session's fix.
    /// </remarks>
    public async Task<BuildJudged> JudgeBuildAsync(string key, BuildFound found, Checkouts.Choice chosen, CancellationToken ct)
    {
        var pushed = false;
        var unknown = false;

        foreach (var repo in found.StillFailing)
        {
            var baseBranch = chosen.Resets.FirstOrDefault(r => r.Path == repo.Path).BaseBranch;

            if (runtime.Workspace(repo.Path, baseBranch).Heads() is not { } heads
                || !heads.Shas.TryGetValue(repo.Branch, out var now))
            {
                unknown = true;
                continue;
            }

            if (now == repo.TipSha) continue;

            pushed = true;
            var read = new BuildRead(BuildVerdicts.Pending, []);
            await ReportBuildAsync(key, repo.Remote, repo.Branch, now, read, pushedByIncrement: true, ct);
        }

        return new BuildJudged(pushed, unknown && !pushed);
    }

    private async Task<Rechecked> CheckAllAsync(WorkDto work, Checkouts.Choice chosen, bool fetch, CancellationToken ct)
    {
        var key = work.Issue.Key;
        var verdicts = new List<Checked>();
        var unknown = false;
        var reported = true;

        foreach (var (path, baseBranch) in chosen.Resets)
        {
            var workspace = runtime.Workspace(path, baseBranch);

            if (fetch && !workspace.Fetch())
            {
                runtime.Say.Complain($"hatch: {key} - could not fetch from origin in {Path.GetFileName(path.TrimEnd('/', '\\'))}, so its branch was not checked");
                unknown = true;
                continue;
            }

            if (workspace.Check(key) is not { } verdict)
            {
                unknown = true;
                continue;
            }

            verdicts.Add(new Checked(path, verdict));
            if (!await ReportAsync(key, chosen, path, verdict, ct)) reported = false;
        }

        return new Rechecked(verdicts, unknown, reported);
    }

    /// <summary>
    /// What origin's branch now comes to, told to the board under the remote
    /// this checkout is for - so a pull request that conflicts the moment it
    /// opens is conflict work on the next pass and does not wait for the poll.
    /// </summary>
    /// <remarks>
    /// Its own failure and no more: a verdict the board refused is a line, and
    /// the tidy comment is still written.
    /// </remarks>
    /// <returns>Whether the board took it.</returns>
    public async Task<bool> ReportAsync(
        string key, Checkouts.Choice chosen, string path, Verdict verdict, CancellationToken ct)
    {
        var remote = RemoteFor(chosen, path);
        if (remote is null) return false;

        try
        {
            await runtime.Board.MergeCheckAsync(key, verdict.ToRequest(remote, runtime.RunnerName), ct);
            return true;
        }
        catch (HatchException e)
        {
            runtime.Say.Complain($"hatch: {key} - the board would not take the verdict on its branch - {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// What a build read came to, told to the board under the remote this
    /// checkout is for. Its own failure and no more: a verdict the board refused
    /// is a line.
    /// </summary>
    /// <returns>Whether the board took it.</returns>
    private async Task<bool> ReportBuildAsync(
        string key, string? remote, string branch, string sha, BuildRead read, bool pushedByIncrement, CancellationToken ct)
    {
        if (remote is null) return false;

        try
        {
            await runtime.Board.BuildCheckAsync(
                key,
                new BuildCheckRequest(
                    remote, branch, sha, read.Verdict,
                    read.Failing.Select(f => new FailingCheckDto(f.Name, f.Url)).ToList(),
                    runtime.RunnerName, pushedByIncrement),
                ct);
            return true;
        }
        catch (HatchException e)
        {
            runtime.Say.Complain($"hatch: {key} - the board would not take the build on its branch - {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// The board's own identity for the repository a checkout is, where the
    /// project binds one - matched from the binding's remote, as
    /// <see cref="RemoteFor"/> does. Null for a project that binds nothing.
    /// </summary>
    private string? CanonicalFor(WorkDto work, Checkouts.Choice chosen, string path)
    {
        if (chosen.Repositories.FirstOrDefault(r => r.Path == path)?.Remote is not { } bound) return null;
        return work.Repositories.FirstOrDefault(r => r.Remote == bound)?.Canonical;
    }

    /// <summary>
    /// The remote a verdict for this checkout is put under: the binding's own
    /// spelling where the project binds it, and the standing checkout's origin
    /// where it binds nothing. Null where there is nothing to report under.
    /// </summary>
    private string? RemoteFor(Checkouts.Choice chosen, string path) =>
        chosen.Repositories.FirstOrDefault(r => r.Path == path)?.Remote
        ?? runtime.Checkouts.FirstOrDefault(c => c.Path == path)?.Remote;

    private async Task<bool> HasPullRequestAsync(string key, CancellationToken ct)
    {
        try
        {
            return (await runtime.Board.IssueAsync(key, ct))?.PullRequestUrl is { Length: > 0 };
        }
        catch (Exception e) when (e is HatchException or OperationCanceledException)
        {
            runtime.Say.Complain($"hatch: {key} - could not read whether it has a pull request, so its branch was not brought up to date");
            return false;
        }
    }
}

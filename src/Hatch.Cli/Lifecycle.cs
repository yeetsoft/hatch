namespace Hatch.Cli;

/// <summary>
/// What a session said on its way out, when it ended because its Claude
/// account ran out of usage - everything <see cref="Lifecycle.LeaveAsync"/>
/// needs to push what it left and write the one comment about it.
/// </summary>
public sealed record UsageLimitInfo(DateTimeOffset ResetAt, bool ResetKnown, string? SessionId);

/// <summary>
/// What the board's preemption notice said, when the session ended because a
/// heartbeat answered with one - everything <see cref="Lifecycle.LeaveAsync"/>
/// needs to push what it left and write the one comment about it.
/// </summary>
public sealed record PreemptionInfo(string EmergencyKey, string EmergencyTitle, string? SessionId);

/// <summary>
/// What a streamed session said on its way out, when it ended because it
/// crossed the runner's own hard limit - everything
/// <see cref="Lifecycle.LeaveAsync"/> needs to push what it left, file the
/// continuation task, and write the one comment about both.
/// </summary>
public sealed record HardLimitInfo(long Tokens, int? Requests, string? SessionId);

/// <summary>What <see cref="Lifecycle.LeaveAsync"/> learned on its way out.</summary>
/// <param name="HasPullRequest">Whether a pull request is recorded on the issue.</param>
/// <param name="BranchOnOrigin">Whether any checkout found a branch on origin for the issue.</param>
/// <param name="FiledKey">
/// The continuation task's key, when a hard limit filed one - <see cref="GoToWork"/>
/// splices it into the report's own <c>Filed</c> list, since nothing re-reads the
/// board afterward to pick it up on its own.
/// </param>
public sealed record LeaveOutcome(bool HasPullRequest, bool BranchOnOrigin, string? FiledKey = null);

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
    /// <param name="limit">
    /// The session ended on a usage limit - push what it left onto the issue's
    /// branch first, and write the one comment the story asks for instead of
    /// the ordinary tidy-up note.
    /// </param>
    /// <param name="preempted">
    /// The session ended because the board asked this runner to stand down for
    /// an emergency ticket - the same push-then-one-comment path a usage limit
    /// takes, for the same reason: the session did not get to say whether what
    /// it left is fit to publish, so the runner does not either.
    /// </param>
    /// <param name="hardLimit">
    /// The session was stopped for crossing the runner's own hard limit - the
    /// same push-then-one-comment path a usage limit and a preemption take, with
    /// one addition: a continuation task is filed under the ticket first, and the
    /// comment names it.
    /// </param>
    public async Task<LeaveOutcome> LeaveAsync(
        WorkDto work, Checkouts.Choice chosen, bool ownsTicket, CancellationToken ct,
        UsageLimitInfo? limit = null, PreemptionInfo? preempted = null, HardLimitInfo? hardLimit = null)
    {
        var key = work.Issue.Key;
        var lines = new List<string>();

        try
        {
            if (!ownsTicket)
            {
                foreach (var (path, baseBranch) in chosen.Resets) runtime.Workspace(path, baseBranch).Return();
                return new LeaveOutcome(false, false);
            }

            var pushed = new List<(string Path, LimitPushed Result)>();
            if (limit is not null || preempted is not null || hardLimit is not null)
                foreach (var (path, baseBranch) in chosen.Resets)
                    pushed.Add((path, runtime.Workspace(path, baseBranch).PushForLimit(key, work.Issue.Title)));

            var pullRequest = await HasPullRequestAsync(key, ct);
            var branchOnOrigin = false;

            foreach (var (path, baseBranch) in chosen.Resets)
            {
                var left = runtime.Workspace(path, baseBranch).Leave(key, pullRequest);
                var where = chosen.Resets.Count > 1 ? $"{Path.GetFileName(path.TrimEnd('/', '\\'))}: " : "";
                lines.AddRange(left.Notes.Select(n => $"- {where}{n}"));

                if (left.Found is { } found)
                {
                    await ReportAsync(key, chosen, path, found, ct);
                    if (found.Kind != MergeVerdicts.None) branchOnOrigin = true;
                }
            }

            if (limit is not null)
            {
                await runtime.Board.CommentAsync(key, UsageLimitBody(limit, chosen, pushed, lines), ct);
                return new LeaveOutcome(pullRequest, false);
            }

            if (preempted is not null)
            {
                await runtime.Board.CommentAsync(key, PreemptedBody(preempted, chosen, pushed, lines), ct);
                return new LeaveOutcome(pullRequest, false);
            }

            if (hardLimit is not null)
            {
                var filed = await FileContinuationAsync(key, work, chosen, pushed, hardLimit, ct);
                await runtime.Board.CommentAsync(key, HardLimitBody(hardLimit, chosen, pushed, lines, filed), ct);
                return new LeaveOutcome(pullRequest, false, filed?.Key);
            }

            if (lines.Count == 0) return new LeaveOutcome(pullRequest, branchOnOrigin);

            await runtime.Board.CommentAsync(
                key, "The runner tidied the tree after this increment:\n\n" + string.Join('\n', lines), ct);

            return new LeaveOutcome(pullRequest, branchOnOrigin);
        }
        catch (Exception e)
        {
            // Nothing here fails an increment: the work happened, and what is
            // left is housekeeping that the next reset does as well.
            runtime.Say.Complain($"hatch: {key} - the tree could not be left tidy, or the ticket told - {e.Message}");
            return new LeaveOutcome(false, false);
        }
    }

    /// <summary>
    /// The one comment a usage limit writes: that it happened, when the runner
    /// expects to resume, the branch and sha that were pushed (or why not, per
    /// checkout), and the session to rejoin.
    /// </summary>
    private static string UsageLimitBody(
        UsageLimitInfo limit, Checkouts.Choice chosen, IReadOnlyList<(string Path, LimitPushed Result)> pushed, IReadOnlyList<string> tidyLines)
    {
        var body = limit.ResetKnown
            ? $"This runner ran out of Claude usage. It expects to resume at {UsageLimit.Clock(limit.ResetAt)}."
            : "This runner ran out of Claude usage, and the reset time it gave could not be read - "
              + $"treating it as an hour away, until {UsageLimit.Clock(limit.ResetAt)}.";

        return body + PushedTidyAndResumeBody(chosen, pushed, tidyLines, limit.SessionId);
    }

    /// <summary>
    /// The one comment a preemption writes: that the board put this ticket
    /// down for an emergency, which issue that was, the branch and sha that
    /// were pushed (or why not, per checkout), and the session to rejoin.
    /// Modelled on <see cref="UsageLimitBody"/> almost line for line, for the
    /// reason the story gives: the session did not get to say whether what it
    /// left is fit to publish, so the runner does not either.
    /// </summary>
    private string PreemptedBody(
        PreemptionInfo preempted, Checkouts.Choice chosen, IReadOnlyList<(string Path, LimitPushed Result)> pushed, IReadOnlyList<string> tidyLines)
    {
        var origin = runtime.Board.Client.Origin;
        var body = $"This runner was preempted by [{preempted.EmergencyKey}]({origin}/apps/hatch/issues/{preempted.EmergencyKey}) - {preempted.EmergencyTitle}.";

        return body + PushedTidyAndResumeBody(chosen, pushed, tidyLines, preempted.SessionId);
    }

    /// <summary>
    /// The task the next session picks up from, filed under the ticket's own
    /// parent when the ticket is itself a task - a task's parent is never a
    /// task, per <c>docs/hatch.md</c>'s own rule - and under the ticket
    /// otherwise. Null when there is no parent to file under, or when the
    /// board refused the issue: either way the branch and sha the comment
    /// names are still where to pick this up by hand.
    /// </summary>
    private async Task<IssueDto?> FileContinuationAsync(
        string key, WorkDto work, Checkouts.Choice chosen, IReadOnlyList<(string Path, LimitPushed Result)> pushed,
        HardLimitInfo hardLimit, CancellationToken ct)
    {
        var parentKey = work.Issue.Type == "task" ? work.Issue.ParentKey : key;
        if (parentKey is null)
        {
            runtime.Say.Complain($"hatch: {key} - has no parent to file its continuation under; nothing was filed");
            return null;
        }

        var body = ContinuationBody(key, chosen, pushed, hardLimit);

        try
        {
            return await runtime.Board.CreateIssueAsync(
                new IssueCreateRequest(work.Issue.ProjectId, "task", $"Continue: {work.Issue.Title}", body, parentKey, null, null), ct);
        }
        catch (Exception e) when (e is HatchException or OperationCanceledException)
        {
            runtime.Say.Complain($"hatch: {key} - its continuation task could not be filed - {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// The continuation task's own body: the branch and sha the hard-limited
    /// session's work was pushed to, what it changes against the trunk, and
    /// the session to rejoin.
    /// </summary>
    private string ContinuationBody(
        string key, Checkouts.Choice chosen, IReadOnlyList<(string Path, LimitPushed Result)> pushed, HardLimitInfo hardLimit)
    {
        var origin = runtime.Board.Client.Origin;
        var body = $"Continuing [{key}]({origin}/apps/hatch/issues/{key}) past the runner's hard limit, at {Format.Compact(hardLimit.Tokens)} tokens.";

        foreach (var (path, result) in pushed)
        {
            if (result.Branch is not { Length: > 0 } branch) continue;

            var where = chosen.Resets.Count > 1 ? $"{Path.GetFileName(path.TrimEnd('/', '\\'))}: " : "";
            var baseBranch = chosen.Resets.FirstOrDefault(r => r.Path == path).BaseBranch;
            var stat = runtime.Workspace(path, baseBranch).DiffStat(branch);

            body += $"\n\nBranch: `{where}{branch}`" + (result.Sha is { Length: > 0 } sha ? $" at `{sha}`" : "");
            body += "\n\n    " + (stat is { Length: > 0 } ? stat.Replace("\n", "\n    ") : "nothing to diff");
        }

        body += hardLimit.SessionId is { Length: > 0 } session
            ? $"\n\nThe session it ran in is still there, with everything it did in context:\n\n    claude --resume {session}"
            : "\n\nThere is no session to resume: the run ended before it said what its id was.";

        return body;
    }

    /// <summary>
    /// The one comment a hard limit writes: that it fired, at how many tokens
    /// and requests, the branch and sha that were pushed (or why not, per
    /// checkout), and the task just filed to continue (or that it could not
    /// be). Modelled on <see cref="UsageLimitBody"/>/<see cref="PreemptedBody"/>
    /// almost line for line, for the same reason those two share: the session
    /// did not get to say whether what it left is fit to publish, so the
    /// runner does not either.
    /// </summary>
    private static string HardLimitBody(
        HardLimitInfo hardLimit, Checkouts.Choice chosen, IReadOnlyList<(string Path, LimitPushed Result)> pushed,
        IReadOnlyList<string> tidyLines, IssueDto? filed)
    {
        var body = $"This runner hit its hard limit, at {Format.Compact(hardLimit.Tokens)} tokens"
            + (hardLimit.Requests is { } requests ? $", {requests} requests" : "") + ".";

        body += PushedTidyAndResumeBody(chosen, pushed, tidyLines, hardLimit.SessionId);

        body += filed is not null
            ? $"\n\nFiled {filed.Key} to continue: {filed.Title}"
            : "\n\nIts continuation could not be filed - the branch and sha above are where to pick this up by hand.";

        return body;
    }

    /// <summary>
    /// The middle and the tail a usage limit's comment and a preemption's
    /// share, word for word: what was pushed per checkout (or why not), the
    /// ordinary tidy lines, and the session to rejoin.
    /// </summary>
    private static string PushedTidyAndResumeBody(
        Checkouts.Choice chosen, IReadOnlyList<(string Path, LimitPushed Result)> pushed, IReadOnlyList<string> tidyLines,
        string? sessionId)
    {
        var body = "\n\n" + string.Join('\n', pushed.Select(p =>
        {
            var where = chosen.Resets.Count > 1 ? $"{Path.GetFileName(p.Path.TrimEnd('/', '\\'))}: " : "";
            return p.Result.Outcome switch
            {
                LimitPush.Pushed => $"- {where}{p.Result.Branch} was pushed, now at {p.Result.Sha}.",
                LimitPush.Nothing => $"- {where}nothing to push - the tree matched what origin already had.",
                _ => $"- {where}{(p.Result.Branch is { Length: > 0 } b ? $"{b} " : "")}would not push - {p.Result.Why}; the work stays on this machine.",
            };
        }));

        if (tidyLines.Count > 0) body += "\n\n" + string.Join('\n', tidyLines);

        body += sessionId is { Length: > 0 } session
            ? $"\n\nJoin it with\n\n    claude --resume {session}"
            : "\n\nThere is no session to resume: the run ended before it said what its id was.";

        return body;
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
            // build is not this dispatch's to read. A project that binds nothing
            // has one standing checkout and no identity to match on, and another
            // runner may have spelled its remote differently, so any failed
            // verdict is the standing checkout's.
            var failed = Builds.Of(work.Issue).FirstOrDefault(b =>
                canonical is not null ? b.Canonical == canonical : b.Remote == remote)
                ?? (work.Repositories.Count == 0 ? Builds.Of(work.Issue).FirstOrDefault() : null);
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

        // A failed verdict in a repository this runner holds no checkout of is not
        // one it can read, and "nothing to do" would be a false answer that sends
        // the pass straight back to the same issue in a tight loop.
        if (repos.Count == 0 && !unknown)
        {
            runtime.Say.Complain($"hatch: {key} - no checkout here holds the repository whose build failed, so it was not read");
            unknown = true;
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

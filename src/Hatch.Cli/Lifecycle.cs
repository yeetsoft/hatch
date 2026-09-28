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

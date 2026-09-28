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

                if (left.Found is { } found) await ReportAsync(key, path, found, chosen, ct);
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
    /// What origin's branch looks like after the increment, put on the board
    /// without waiting for the poll: a pull request that conflicts the moment it
    /// opens is conflict work on the next pass. A verdict the board would not
    /// take is one line, and never a reason for the ticket not to be told what
    /// the tidy found.
    /// </summary>
    private async Task ReportAsync(string key, string path, Verdict found, Checkouts.Choice chosen, CancellationToken ct)
    {
        // The binding's own remote where the project has one, and the standing
        // checkout's where it binds nothing: the board keys a verdict on the
        // canonical form of either.
        var remote = chosen.Repositories.FirstOrDefault(r => r.Path == path)?.Remote
            ?? runtime.Checkouts.FirstOrDefault(c => c.Path == path)?.Remote;
        if (remote is null)
        {
            runtime.Say.Complain($"hatch: {key} - {Path.GetFileName(path.TrimEnd('/', '\\'))} has no remote to report its branch under");
            return;
        }

        try
        {
            await runtime.Board.MergeCheckAsync(
                key,
                new MergeCheckRequest(
                    remote, found.Trunk, found.TrunkSha, found.Kind, found.Branch, found.BranchSha, found.Files,
                    runtime.RunnerName),
                ct);
        }
        catch (HatchException e)
        {
            runtime.Say.Complain($"hatch: {key} - the board did not take the verdict on its branch - {e.Message}");
        }
    }

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

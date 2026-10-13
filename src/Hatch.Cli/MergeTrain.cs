namespace Hatch.Cli;

/// <summary>
/// The loop's own acting on what is in review: a pull request that merged or
/// closed advances or comments its issue, one judged <see cref="WorkKinds.Merge"/>
/// is merged, and one judged <see cref="WorkKinds.Update"/> is brought current
/// with the trunk - all three told to the board.
/// </summary>
public sealed class MergeTrain
{
    private readonly Dictionary<(string Path, string Key), string> _seenMergeRefusal = [];
    private readonly Dictionary<(string Path, string Key), string> _seenUpdate = [];
    private readonly HashSet<string> _seenClosed = [];
    private HashSet<string> _saidBefore = [];

    /// <summary>
    /// Takes the pull request, merge and update halves in that order, against
    /// what is in review right now. Nothing it does throws out: a cause that
    /// persists across calls is said once and not every call.
    /// </summary>
    public async Task TickAsync(Runtime runtime, CancellationToken ct)
    {
        var saying = new HashSet<string>();

        // A line said by the train before this one is not said again: an
        // origin that is down for the night is one line and not four hundred.
        void Complain(string line)
        {
            saying.Add(line);
            if (!_saidBefore.Contains(line)) runtime.Say.Complain(line);
        }

        try
        {
            await TickCoreAsync(runtime, Complain, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Nothing the train does is worth a night: the pass that follows
            // works whatever the board says is workable, train or no train.
            Complain($"hatch: could not check the branches in review - {e.Message}");
        }

        _saidBefore = saying;
    }

    private async Task TickCoreAsync(Runtime runtime, Action<string> complain, CancellationToken ct)
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

        // The pull request half, independent of the merge and update halves
        // below it: keyed on the issue rather than the checkout, since
        // PullRequestUrl is one field on one issue however many repositories
        // its project binds.
        await ReadPullRequestsAsync(runtime, complain, review, ct);

        await MergeApprovedAsync(runtime, complain, review, ct);

        await UpdateApprovedAsync(runtime, complain, review, ct);
    }

    /// <summary>
    /// The pull request half: for every issue in review that carries a
    /// <c>pullRequestUrl</c>, ask <c>gh</c> what it reads and, on <c>merged</c>,
    /// advance the issue. There is no stored verdict and no window logic like
    /// <see cref="Poll.NoneWindow"/> - an open pull request is asked about every
    /// interval by design, and a merged one stops appearing in
    /// <c>/api/hatch/work/review</c> at all, so nothing asks again.
    /// </summary>
    /// <remarks>
    /// Keyed on the issue and not the checkout, unlike Poll's own merge-check,
    /// build-check and trunk halves: a merge or build verdict is per repository,
    /// but a pull request url is one field on one issue however many
    /// repositories its project binds - so the checkout is only somewhere for
    /// <c>gh</c> to run, taken as the first one <see cref="Checkouts.ToPoll"/>
    /// yields for the issue. <c>failed</c> is the per-checkout dedupe the other
    /// halves get for free by being grouped by checkout already: the first
    /// failing <c>gh</c> call for a path adds it, and every later issue on that
    /// path is skipped silently for the rest of this tick. The outer try/catch
    /// keeps anything unanticipated here from propagating into
    /// <see cref="TickAsync"/> and aborting the halves that run after it.
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

                if (issue.MergeWork is not null && state == PullRequestStates.Closed)
                {
                    (IssueDto? Issue, string? WalkOn) closed;
                    try
                    {
                        closed = await runtime.QuietBoard.ClosedAsync(
                            issue.Key, new PullRequestClosedRequest(url, runtime.RunnerName), ct);
                    }
                    catch (HatchException e)
                    {
                        complain($"hatch: {issue.Key} - the board would not take the closed pull request - {e.Message}");
                        continue;
                    }

                    if (closed.WalkOn is null && _seenClosed.Add(issue.Key))
                    {
                        await runtime.QuietBoard.CommentAsync(
                            issue.Key, "Its pull request was closed without merging.", ct);
                    }

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

    /// <summary>
    /// The merge half: for every issue in the merges column the judge marked
    /// <see cref="WorkKinds.Merge"/>, merge its pull request and advance it -
    /// <see cref="WorkKinds.Update"/> and <see cref="WorkKinds.Hold"/> rows, and
    /// every row in the review column (<c>MergeWork</c> null), are left for
    /// whatever else acts on them and not asked about here at all.
    /// </summary>
    /// <remarks>
    /// Keyed on the issue, the same as <see cref="ReadPullRequestsAsync"/> and
    /// for the same reason - a pull request url is one field on one issue
    /// however many repositories its project binds. <c>judgedSha</c> is read
    /// from the resolved checkout's own clean verdict rather than any bound
    /// repository's, since the pull request lives in exactly one of them: the
    /// primary, which is what <see cref="Checkouts.ToPoll"/> already resolved
    /// <c>where</c> to. <c>failed</c> is the same per-checkout dedupe
    /// <see cref="ReadPullRequestsAsync"/> keeps, and <c>_seenMergeRefusal</c>
    /// is the same idea again, remembered across intervals rather than within
    /// one: a head that has moved, or a merge <c>gh</c> refused, is reported
    /// once per sha and not every interval - "never a retry storm". The outer
    /// try/catch keeps anything unanticipated here from propagating into
    /// <see cref="TickAsync"/> and aborting the halves that run after it.
    /// </remarks>
    private async Task MergeApprovedAsync(
        Runtime runtime, Action<string> complain, IReadOnlyList<ReviewCheckDto> review, CancellationToken ct)
    {
        var failed = new HashSet<string>();
        try
        {
            foreach (var issue in review)
            {
                if (issue.MergeWork?.Kind != WorkKinds.Merge) continue;
                if (issue.PullRequestUrl is not { Length: > 0 } url) continue;
                ct.ThrowIfCancellationRequested();

                var where = Checkouts.ToPoll(issue.Repositories, runtime.Checkouts, runtime.Settings.BaseBranch)
                    .FirstOrDefault();
                if (where is null || failed.Contains(where.Path)) continue;

                var judgedSha = issue.MergeChecks
                    .FirstOrDefault(c => c.Canonical == where.Canonical && c.Verdict == MergeVerdicts.Clean)?.BranchSha;
                if (judgedSha is null) continue;

                var name = Path.GetFileName(where.Path.TrimEnd('/', '\\'));
                var pr = await runtime.Forge(where.Path, where.Canonical).ReadPullRequestAsync(url, ct);
                if (pr.State is not { } state)
                {
                    failed.Add(where.Path);
                    if (pr.Why is { } why) complain($"hatch: could not read pull requests in {name} - {why}");
                    continue;
                }

                if (state == PullRequestStates.Closed) continue;

                var dedupeKey = (where.Path, issue.Key);
                var alreadySeen = _seenMergeRefusal.TryGetValue(dedupeKey, out var seenSha) && seenSha == judgedSha;

                if (state != PullRequestStates.Open || pr.HeadSha != judgedSha)
                {
                    if (alreadySeen) continue;
                    _seenMergeRefusal[dedupeKey] = judgedSha;
                    await runtime.QuietBoard.CommentAsync(
                        issue.Key,
                        $"Its pull request's head is now {pr.HeadSha ?? "unknown"}, not the {judgedSha} this "
                        + "runner approved to merge - skipping until a fresh verdict says it is clean again.",
                        ct);
                    continue;
                }

                // The same dedupe as above, now stopping the gh pr merge call
                // itself and not only the comment about it: a refusal on this
                // sha is asked about once, not every interval.
                if (alreadySeen) continue;

                var merge = await runtime.Forge(where.Path, where.Canonical).MergeAsync(url, judgedSha, ct);
                if (!merge.Merged)
                {
                    _seenMergeRefusal[dedupeKey] = judgedSha;
                    await runtime.QuietBoard.CommentAsync(issue.Key, $"gh would not merge its pull request - {merge.Why}", ct);
                    continue;
                }

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

                await runtime.QuietBoard.CommentAsync(
                    issue.Key, $"Merged its pull request at {judgedSha}, as {runtime.RunnerName}.", ct);

                var to = await ResolvedColumnNameAsync(runtime, result.Issue!.StatusId, ct);
                runtime.Say.Line($"hatch: {PullRequestWords(issue.Key, to)}");
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            complain($"hatch: could not merge approved pull requests - {e.Message}");
        }
    }

    /// <summary>
    /// The update half: for every issue in the merges column the judge marked
    /// <see cref="WorkKinds.Update"/>, bring its branch current with the trunk
    /// by asking the forge to update it - <see cref="WorkKinds.Merge"/> and
    /// <see cref="WorkKinds.Hold"/> rows, and every row in the review column
    /// (<c>MergeWork</c> null), are left for whatever else acts on them and
    /// not asked about here at all.
    /// </summary>
    /// <remarks>
    /// The same shape as <see cref="MergeApprovedAsync"/> right above it, and
    /// for the same reason: a pull request's head is read first and compared
    /// against <c>judgedSha</c>, so a branch that moved under the board is
    /// never handed to the forge at all - the forge's own
    /// <c>expected_head_sha</c> guard (see <see cref="GhForge.UpdateAsync"/>)
    /// is only the race between that read and this call, not the whole of the
    /// check. <c>_seenUpdate</c> is the same dedupe idea as
    /// <c>_seenMergeRefusal</c> - a head that has moved, or an update that
    /// conflicts, is reported once per judged sha, not every interval. A
    /// generic failure (the forge unreachable, or the account refused) is
    /// never remembered, so it is retried next interval the way a transient
    /// one should be - <c>complain</c>'s own dedupe is what stops it being
    /// said more than once for as long as it persists. The outer try/catch
    /// keeps anything unanticipated here from propagating into
    /// <see cref="TickAsync"/> and aborting the halves that run after it.
    /// </remarks>
    private async Task UpdateApprovedAsync(
        Runtime runtime, Action<string> complain, IReadOnlyList<ReviewCheckDto> review, CancellationToken ct)
    {
        var failed = new HashSet<string>();
        try
        {
            foreach (var issue in review)
            {
                if (issue.MergeWork?.Kind != WorkKinds.Update) continue;
                if (issue.PullRequestUrl is not { Length: > 0 } url) continue;
                ct.ThrowIfCancellationRequested();

                var where = Checkouts.ToPoll(issue.Repositories, runtime.Checkouts, runtime.Settings.BaseBranch)
                    .FirstOrDefault();
                if (where is null || failed.Contains(where.Path)) continue;

                var judged = issue.MergeChecks
                    .FirstOrDefault(c => c.Canonical == where.Canonical && c.Verdict == MergeVerdicts.Clean);
                if (judged?.BranchSha is not { } judgedSha) continue;

                var name = Path.GetFileName(where.Path.TrimEnd('/', '\\'));
                var pr = await runtime.Forge(where.Path, where.Canonical).ReadPullRequestAsync(url, ct);
                if (pr.State is not { } state)
                {
                    failed.Add(where.Path);
                    if (pr.Why is { } why) complain($"hatch: could not read pull requests in {name} - {why}");
                    continue;
                }

                if (state == PullRequestStates.Closed) continue;

                var dedupeKey = (where.Path, issue.Key);
                var alreadySeen = _seenUpdate.TryGetValue(dedupeKey, out var seenSha) && seenSha == judgedSha;

                if (state != PullRequestStates.Open || pr.HeadSha != judgedSha)
                {
                    if (alreadySeen) continue;
                    _seenUpdate[dedupeKey] = judgedSha;
                    await runtime.QuietBoard.CommentAsync(
                        issue.Key,
                        $"Its pull request's head is now {pr.HeadSha ?? "unknown"}, not the {judgedSha} this "
                        + "runner approved to bring current - skipping until a fresh verdict says it is clean again.",
                        ct);
                    continue;
                }

                if (alreadySeen || pr.Number is not { } number) continue;

                var updated = await runtime.Forge(where.Path, where.Canonical).UpdateAsync(number, judgedSha, ct);

                switch (updated.Outcome)
                {
                    case UpdateOutcomes.Updated:
                        _seenUpdate[dedupeKey] = judgedSha;
                        await runtime.QuietBoard.CommentAsync(
                            issue.Key, $"Brought its branch current with {judged.Trunk}, as {runtime.RunnerName}.", ct);
                        runtime.Say.Line($"hatch: {UpdateWords(issue.Key, judged.Trunk)}");
                        break;

                    case UpdateOutcomes.Conflicted:
                        _seenUpdate[dedupeKey] = judgedSha;
                        await runtime.QuietBoard.CommentAsync(
                            issue.Key, $"Its branch does not merge cleanly with {judged.Trunk} - nothing was pushed.", ct);
                        break;

                    default:
                        complain($"hatch: could not bring {issue.Key}'s branch current in {name} - {updated.Why}");
                        break;
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            complain($"hatch: could not bring approved branches current - {e.Message}");
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

    /// <summary>A line for the terminal: <c>HA-12 branch brought current with main</c>.</summary>
    private static string UpdateWords(string key, string trunk) => $"{key} branch brought current with {trunk}";
}

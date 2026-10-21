using System.Net;

namespace Hatch.Cli;

/// <summary>
/// The loop's own acting on what is in review: a pull request that merged or
/// closed advances or comments its issue, one judged <see cref="WorkKinds.Merge"/>
/// is merged, one judged <see cref="WorkKinds.Update"/> is brought current with
/// the trunk, and one judged <see cref="WorkKinds.Pushback"/> is carried back to
/// review, and a trunk-landing row the board judged <see cref="WorkKinds.Land"/>
/// is landed - all five told to the board.
/// </summary>
/// <remarks>
/// The clock is optional and lives apart from <see cref="TickAsync"/> itself:
/// the bare constructor stays inert, which is what lets a test construct one
/// and call <see cref="TickAsync"/> by hand, once, without a background loop
/// racing it. Only <see cref="Start"/> spins one up - the same "stop first,
/// then let go" shape as <see cref="Claim"/>.
/// </remarks>
public sealed class MergeTrain : IAsyncDisposable
{
    private readonly Dictionary<(string Path, string Key), string> _seenMergeRefusal = [];
    private readonly Dictionary<(string Path, string Key), string> _seenUpdate = [];
    private readonly HashSet<string> _seenClosed = [];
    private HashSet<string> _saidBefore = [];

    private readonly CancellationTokenSource _stop = new();
    private Task? _clock;

    /// <summary>Starts the clock: <see cref="TickAsync"/>, then its own interval, until stopped.</summary>
    public static MergeTrain Start(Runtime runtime, TimeSpan interval)
    {
        var train = new MergeTrain();
        train._clock = Task.Run(() => train.ClockAsync(runtime, interval, train._stop.Token), CancellationToken.None);
        return train;
    }

    private async Task ClockAsync(Runtime runtime, TimeSpan interval, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await TickAsync(runtime, ct);
                await Task.Delay(interval, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Stops the clock first, then lets it go - the same order <see cref="Claim.ReleaseAsync"/> uses.</summary>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_clock is { } clock)
        {
            try { await clock; }
            catch (Exception)
            {
                // Whatever the clock died of, it is not a reason to fail the
                // stop - and this already runs on the way out.
            }
        }

        _stop.Dispose();
    }

    /// <summary>
    /// Takes the pull request, merge, update, pushback and landing halves in
    /// that order, against what is in review right now. Nothing it does throws
    /// out: a cause that persists across calls is said once and not every
    /// call.
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

        // Ordered after the two halves above rather than before them for no
        // reason that matters: unlike those two, this one writes to nothing
        // but the board, and the board's own route re-judges the issue's
        // stored merge and build checks itself - so a row this tick read as
        // pushback a moment ago that the board no longer agrees with costs
        // one 409, walked on the same way Board.PushbackAsync documents, and
        // never a wrong move either way.
        await PushbackApprovedAsync(runtime, complain, review, ct);

        // Last, and for the same reason: it writes to nothing but the board,
        // whose route re-judges the row itself.
        await CarryLandedAsync(runtime, complain, review, ct);
    }

    /// <summary>
    /// The pull request half: for every issue in review that carries a
    /// <c>pullRequestUrl</c>, ask <c>gh</c> what it reads and, on <c>merged</c>,
    /// advance the issue; on <c>open</c>, put the forge's head, merge state and
    /// review decision on the board where they differ from what it holds (see
    /// <see cref="PutPullRequestStateAsync"/>). There is no stored verdict and no
    /// window logic like <see cref="Poll.NoneWindow"/> - an open pull request is
    /// asked about every interval by design, and a merged one stops appearing in
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
                if (issue.MergeWork is not null && !runtime.Readout.Drives(issue.ProjectKey)) continue;
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

                if (state == PullRequestStates.Open)
                {
                    await PutPullRequestStateAsync(runtime, complain, issue, url, answer, ct);
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
    /// Tell the board what the forge says of an open pull request, when that is
    /// news: all three of the answer's head, merge state and review decision
    /// read, and any of them different from what <paramref name="issue"/> holds.
    /// An answer that did not read all three (a failed read, a fake that says
    /// nothing) writes nothing, and an identical one writes nothing either, so a
    /// quiet pull request costs no call after the first.
    /// </summary>
    /// <remarks>
    /// A <c>404</c> is a board that predates the route: said once (the caller
    /// de-duplicates across ticks) and asked again next tick. A <c>409</c> is
    /// the row having moved between the read and the write, and is walked on
    /// silently the way <see cref="Board.MergedAsync"/> treats it.
    /// </remarks>
    private static async Task PutPullRequestStateAsync(
        Runtime runtime, Action<string> complain, ReviewCheckDto issue, string url, PullRequestAnswer answer,
        CancellationToken ct)
    {
        if (answer.HeadSha is not { } sha || answer.MergeState is not { } merge
            || answer.ReviewDecision is not { } review) return;

        if (string.Equals(sha, issue.PullRequestHeadSha, StringComparison.Ordinal)
            && string.Equals(merge, issue.PullRequestMergeState, StringComparison.Ordinal)
            && string.Equals(review, issue.PullRequestReviewDecision, StringComparison.Ordinal)) return;

        try
        {
            await runtime.QuietBoard.PullRequestStateAsync(
                issue.Key, new PullRequestStateRequest(url, sha, merge, review, runtime.RunnerName), ct);
        }
        catch (HatchException e) when (e.Status == HttpStatusCode.NotFound)
        {
            complain($"hatch: {issue.Key} - the board does not know pull request state yet - it will be asked again next tick");
        }
        catch (HatchException e) when (e.Status == HttpStatusCode.Conflict)
        {
            // The row changed between the read and the write.
        }
        catch (HatchException e)
        {
            complain($"hatch: {issue.Key} - the board would not take the pull request's state - {e.Message}");
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
                if (issue.MergeWork?.Kind != WorkKinds.Merge || issue.MergeWork?.IsHead != true
                    || !runtime.Readout.Drives(issue.ProjectKey)) continue;
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
                if (issue.MergeWork?.Kind != WorkKinds.Update || issue.MergeWork?.IsHead != true
                    || !runtime.Readout.Drives(issue.ProjectKey)) continue;
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
                    if (alreadySeen)
                    {
                        // Our own update landing, read on a later tick: the
                        // forge's background merge has now finished, and
                        // what it produced is a fact we already hold, not a
                        // question to ask git.
                        await TryWriteUpdateMergeCheckAsync(runtime, issue, where, judged, dedupeKey, pr, ct);
                        continue;
                    }

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

                        // The fast path: read the pull request again, right
                        // away, in case the forge's merge already finished in
                        // the moment it took to answer the update itself. The
                        // slow path above is what catches it otherwise, on a
                        // later tick.
                        var fresh = await runtime.Forge(where.Path, where.Canonical).ReadPullRequestAsync(url, ct);
                        await TryWriteUpdateMergeCheckAsync(runtime, issue, where, judged, dedupeKey, fresh, ct);
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

    /// <summary>
    /// Writes the merge check an update's own success already answers, from
    /// <paramref name="pr"/>'s freshest read - the fast path's second read in
    /// the same tick, or the slow path's top-of-loop one on a later tick. A
    /// success at the forge <em>is</em> <c>holdsTrunk: true</c> at the new
    /// sha, because the forge performed that merge itself: this runs no git
    /// and asks the board once, naming the new sha.
    /// </summary>
    /// <remarks>
    /// Writes nothing, and advances <see cref="_seenUpdate"/> not at all, when
    /// the read does not yet show a sha other than <paramref name="judged"/>'s
    /// own - the forge's background merge has not finished, see
    /// <see cref="GhForge.UpdateAsync"/>'s own remarks for why that can still
    /// be true on this same read - when the pull request is no longer open,
    /// or when the read carries no base branch or sha (an older <c>gh</c>, or
    /// a fake that answers neither); the caller's own dedupe and comment,
    /// where it has one, is what a true refusal falls back to.
    /// <paramref name="judged"/>'s own <c>PlaybookFilePath</c> is carried
    /// forward unchanged onto the new check: an update merges the trunk's
    /// changes into the branch, never the branch's own changes into the
    /// trunk, so a branch that already changed the playbook file still
    /// changes it at the new sha, and one that did not cannot start to -
    /// carrying the value forward can only keep a row held that was already
    /// held, never release one that should not be.
    /// </remarks>
    private async Task TryWriteUpdateMergeCheckAsync(
        Runtime runtime, ReviewCheckDto issue, Checkouts.Polled where, MergeCheckDto judged,
        (string Path, string Key) dedupeKey, PullRequestAnswer pr, CancellationToken ct)
    {
        if (pr.State != PullRequestStates.Open || pr.HeadSha is not { } sha || sha == judged.BranchSha
            || pr.Base is not { } baseRef || pr.BaseSha is not { } baseSha) return;

        _seenUpdate[dedupeKey] = sha;
        await runtime.QuietBoard.MergeCheckAsync(
            issue.Key,
            new MergeCheckRequest(
                where.Remote!, baseRef, baseSha, MergeVerdicts.Clean, judged.Branch, sha, [], runtime.RunnerName,
                true, judged.PlaybookFilePath),
            ct);
    }

    /// <summary>
    /// The pushback half: for every issue in the merges column the judge marked
    /// <see cref="WorkKinds.Pushback"/>, ask the board to carry it back to review -
    /// <see cref="WorkKinds.Merge"/>, <see cref="WorkKinds.Update"/> and
    /// <see cref="WorkKinds.Hold"/> rows, and every row in the review column
    /// (<c>MergeWork</c> null), are left for whatever else acts on them and not
    /// asked about here at all. Unlike <see cref="MergeApprovedAsync"/> and
    /// <see cref="UpdateApprovedAsync"/> above it, this never checks
    /// <c>IsHead</c>: a row judged <c>pushback</c> is never the train's head in
    /// the first place (see <see cref="Dispatch.Train"/>), so waiting for its
    /// turn would wait forever.
    /// </summary>
    /// <remarks>
    /// This asks nothing of <c>gh</c> and keeps no <c>judgedSha</c>: the board's
    /// own route re-judges the issue's stored merge and build checks itself, so
    /// there is nothing here for a fresh read to be compared against, and
    /// nothing that needs remembering across ticks either - a successful
    /// pushback moves the issue out of the merge column, so it is no longer
    /// judged <see cref="WorkKinds.Pushback"/> and this half never visits it
    /// again; a <c>409</c> says nothing at all; and a genuine refusal is a line
    /// the outer <c>complain</c> already keeps from repeating verbatim. The
    /// outer try/catch keeps anything unanticipated here from propagating into
    /// <see cref="TickAsync"/> and aborting the halves that run after it.
    /// </remarks>
    private async Task PushbackApprovedAsync(
        Runtime runtime, Action<string> complain, IReadOnlyList<ReviewCheckDto> review, CancellationToken ct)
    {
        try
        {
            foreach (var issue in review)
            {
                if (issue.MergeWork?.Kind != WorkKinds.Pushback || !runtime.Readout.Drives(issue.ProjectKey)) continue;
                if (issue.PullRequestUrl is not { Length: > 0 } url) continue;
                ct.ThrowIfCancellationRequested();

                (IssueDto? Issue, string? WalkOn) result;
                try
                {
                    result = await runtime.QuietBoard.PushbackAsync(
                        issue.Key, new PullRequestPushbackRequest(url, runtime.RunnerName), ct);
                }
                catch (HatchException e)
                {
                    complain($"hatch: {issue.Key} - the board would not take the pushback - {e.Message}");
                    continue;
                }

                if (result.WalkOn is not null) continue;

                var to = await ResolvedColumnNameAsync(runtime, result.Issue!.StatusId, ct);
                runtime.Say.Line($"hatch: {PushbackWords(issue.Key, to)}");
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            complain($"hatch: could not push back approved pull requests - {e.Message}");
        }
    }

    /// <summary>
    /// The landing half: for every trunk-landing issue in review the board
    /// judged <see cref="WorkKinds.Land"/>, call the route that carries it to
    /// its next column. Rows judged <see cref="WorkKinds.Pending"/> or
    /// <see cref="WorkKinds.Hold"/>, and rows with no <c>LandingWork</c> at all
    /// (every pull-request row), are left alone.
    /// </summary>
    /// <remarks>
    /// Like <see cref="PushbackApprovedAsync"/> this asks nothing of <c>gh</c>
    /// and keeps nothing across ticks: the route re-judges the row, writes the
    /// ticket's comment itself (so this half writes none), and a landed row
    /// leaves review. A <c>409</c> says the row moved under this call and is
    /// walked past silently; a genuine refusal is a line the outer
    /// <c>complain</c> keeps from repeating verbatim, and the next tick tries
    /// again. The outer try/catch keeps anything unanticipated here from
    /// propagating into <see cref="TickAsync"/>.
    /// </remarks>
    private async Task CarryLandedAsync(
        Runtime runtime, Action<string> complain, IReadOnlyList<ReviewCheckDto> review, CancellationToken ct)
    {
        try
        {
            foreach (var issue in review)
            {
                if (issue.LandingWork?.Kind != WorkKinds.Land || !runtime.Readout.Drives(issue.ProjectKey)) continue;
                ct.ThrowIfCancellationRequested();

                (IssueDto? Issue, string? WalkOn) result;
                try
                {
                    result = await runtime.QuietBoard.LandWorkAsync(issue.Key, ct);
                }
                catch (HatchException e)
                {
                    complain($"hatch: {issue.Key} - the board would not take the landing - {e.Message}");
                    continue;
                }

                if (result.WalkOn is not null) continue;

                var trunk = issue.Landings?.FirstOrDefault()?.Trunk ?? "the trunk";
                var to = await ResolvedColumnNameAsync(runtime, result.Issue!.StatusId, ct);
                runtime.Say.Line($"hatch: {issue.Key} landed on {trunk} -> {to}");
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            complain($"hatch: could not land approved issues - {e.Message}");
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

    /// <summary>A line for the terminal: <c>HA-12 pull request pushed back -> In Review</c>.</summary>
    private static string PushbackWords(string key, string to) => $"{key} pull request pushed back -> {to}";

    /// <summary>A line for the terminal: <c>HA-12 branch brought current with main</c>.</summary>
    private static string UpdateWords(string key, string trunk) => $"{key} branch brought current with {trunk}";
}

namespace Hatch.Cli;

/// <summary>
/// Opens a pull request that was left waiting on its ticket: the text a runner that could not reach the
/// forge stored (<c>hatch pr</c>), opened by the runner that drives the ticket's project, as written, with
/// no session and nobody asked.
/// </summary>
/// <remarks>
/// <para>Only the project's driver does this, and only for a row it could otherwise merge: a review row,
/// not in the merges column, landing by pull request, with no pull request recorded, and one checkout here
/// of the repository the text was written for. Another runner's row is left to that runner.</para>
///
/// <para>Look before opening. A pull request already open from the stored branch is recorded, not opened
/// again; a branch that is gone from origin, or no longer holds the commit the text was written at, is
/// said once on the ticket and left alone, since the text no longer describes it. The text itself is never
/// edited.</para>
///
/// <para>Open, then record, then say. A failure between the first two leaves the pull request open and
/// the text waiting; the next tick finds it listed, or the forge answers that it already exists, and
/// records it. Nothing is retried inside a tick.</para>
/// </remarks>
public sealed class WaitingPullRequestOpener
{
    /// <summary>What has already been said on a board this run: the issue, the branch, the sha and what was found.</summary>
    private readonly HashSet<string> _told = new(StringComparer.Ordinal);

    /// <summary>
    /// One look at every review row with a waiting pull request this runner could open. <paramref name="lists"/>
    /// is the tick's own, so a path is listed at most once a tick, and <paramref name="repair"/> is the tick's
    /// too, so what it has told a ticket is not told again.
    /// </summary>
    public async Task OpenAsync(
        Runtime runtime, Action<string> complain, IReadOnlyList<ReviewCheckDto> review,
        Dictionary<string, PullRequestListAnswer> lists, PullRequestRepair repair, CancellationToken ct)
    {
        foreach (var issue in review)
        {
            ct.ThrowIfCancellationRequested();

            if (issue.WaitingPullRequest is not { } waiting) continue;
            if (issue.MergeWork is not null || issue.LandingWork is not null) continue;
            if (issue.Landing != Landings.PullRequest) continue;
            if (!string.IsNullOrEmpty(issue.PullRequestUrl)) continue;
            if (waiting.Branch.Length == 0 || waiting.Title.Length == 0) continue;
            if (!runtime.Readout.Drives(issue.ProjectKey)) continue;

            var where = Checkouts.ToPoll(issue.Repositories, runtime.Checkouts, runtime.Settings.BaseBranch)
                .Where(p => waiting.Remote is not { Length: > 0 } remote
                    || string.Equals(p.Remote, remote, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // None is another runner's row; two is ambiguous.
            if (where.Count != 1) continue;

            var polled = where[0];

            // Named by the project's repository, else by this runner's setting, else - as every other half
            // of the loop has it - whatever origin calls its trunk.
            var trunk = polled.BaseBranch ?? runtime.Settings.BaseBranch
                ?? runtime.Workspace(polled.Path, null).Heads()?.Trunk;
            if (trunk is not { Length: > 0 }) continue;

            var list = await PullRequestRepair.ReadListAsync(runtime, complain, polled, lists, ct);
            if (list.Open is null) continue;

            var found = PullRequestRepair.FromBranch(list, waiting.Branch, trunk);
            if (found.Count > 1) continue;

            try
            {
                if (found.Count == 1)
                    await repair.PutRightAsync(runtime, complain, issue.Key, waiting.Branch, found[0].Key, ct);
                else
                    await OpenOneAsync(runtime, complain, issue.Key, waiting, polled, trunk, ct);
            }
            catch (HatchException e)
            {
                complain($"hatch: {issue.Key} - could not open its pull request - {e.Message}");
            }
        }
    }

    private async Task OpenOneAsync(
        Runtime runtime, Action<string> complain, string key, WaitingPullRequestDto row,
        Checkouts.Polled polled, string trunk, CancellationToken ct)
    {
        // The row is a moment old: the ticket may have had a pull request recorded, or its text replaced, since.
        var own = await runtime.QuietBoard.IssueAsync(key, ct);
        if (own is null || !string.IsNullOrEmpty(own.PullRequestUrl)) return;
        if (own.WaitingPullRequest is not { } waiting
            || waiting.Branch != row.Branch || waiting.Sha != row.Sha) return;

        var holding = runtime.Workspace(polled.Path, trunk).BranchHolds(waiting.Branch, waiting.Sha);
        if (holding == BranchHolding.Unknown)
        {
            complain($"hatch: {key} - origin would not say whether branch {waiting.Branch} is there, so its pull request is not opened");
            return;
        }

        if (holding != BranchHolding.Holds)
        {
            await SayLeftAloneAsync(runtime, complain, key, waiting, holding, ct);
            return;
        }

        var opened = await runtime.Forge(polled.Path, polled.Canonical)
            .OpenPullRequestAsync(waiting.Branch, trunk, waiting.Title, waiting.Body, ct);
        if (opened.Url is not { Length: > 0 } url)
        {
            var why = opened.Why is { } said && OneLine(said) is { Length: > 0 } line ? line : "the forge gave no reason";
            complain($"hatch: {key} - could not open its pull request - {why}");
            return;
        }

        IssueDto? recorded;
        try
        {
            recorded = await runtime.Board.PatchAsync(key, PullRequestRepair.Patch(url), ct);
        }
        catch (HatchException e)
        {
            // Open, and the text still waiting: the next tick finds it listed, and records it.
            complain($"hatch: {key} - opened its pull request ({url}) but could not record it - {e.Message}");
            return;
        }

        runtime.Say.Line($"hatch: {key} - opened its waiting pull request ({url}) from branch {waiting.Branch}");

        await WriteEnvelopeAsync(runtime, complain, key, url, recorded ?? own, polled, ct);

        try
        {
            await runtime.Board.CommentAsync(key,
                $"The runner {runtime.RunnerName} opened pull request {url} from branch `{waiting.Branch}`, "
                + $"as {waiting.Runner} wrote it: {waiting.Runner} could not open it ({OneLine(waiting.Why)}).", ct);
        }
        catch (HatchException e)
        {
            complain($"hatch: {key} - opened its pull request ({url}) but could not say so on the ticket - {e.Message}");
        }
    }

    /// <summary>
    /// The chain goes into the pull request's body as <c>hatch pr</c> writes it. The record is already made,
    /// so a failure here costs one line and nothing else.
    /// </summary>
    private static async Task WriteEnvelopeAsync(
        Runtime runtime, Action<string> complain, string key, string url, IssueDto issue,
        Checkouts.Polled polled, CancellationToken ct)
    {
        string? why;
        try
        {
            why = await PullRequestEnvelopes.TryWriteAsync(
                runtime.Board, runtime.Forge(polled.Path, polled.Canonical), url, issue, ct);
        }
        catch (HatchException e)
        {
            why = e.Message;
        }

        if (why is not null)
            complain($"hatch: {key} - its pull request's body was not updated with the envelope - {why}");
    }

    /// <summary>A branch the text no longer describes is said once on the ticket and once in the terminal.</summary>
    private async Task SayLeftAloneAsync(
        Runtime runtime, Action<string> complain, string key, WaitingPullRequestDto waiting,
        BranchHolding holding, CancellationToken ct)
    {
        var told = $"{key}|{waiting.Branch}|{waiting.Sha}|{holding}";
        if (!_told.Add(told)) return;

        var sha = waiting.Sha.Length > 7 ? waiting.Sha[..7] : waiting.Sha;
        var gone = holding == BranchHolding.Gone;

        complain(gone
            ? $"hatch: {key} - branch {waiting.Branch} is gone from origin, so its waiting pull request is left alone"
            : $"hatch: {key} - branch {waiting.Branch} no longer holds {sha}, so its waiting pull request is left alone");

        try
        {
            await runtime.Board.CommentAsync(key, gone
                ? $"The runner did not open the pull request {waiting.Runner} left waiting: its branch `{waiting.Branch}` is gone from origin. "
                    + $"Push the branch again and run `hatch pr {key}` to write it afresh."
                : $"The runner did not open the pull request {waiting.Runner} left waiting: its branch `{waiting.Branch}` no longer holds "
                    + $"{sha}, which the text was written at. Run `hatch pr {key}` from the branch to write it afresh.", ct);
        }
        catch (HatchException e)
        {
            _told.Remove(told);
            complain($"hatch: {key} - could not say its waiting pull request was left alone - {e.Message}");
        }
    }

    private static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

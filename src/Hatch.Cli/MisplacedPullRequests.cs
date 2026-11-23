namespace Hatch.Cli;

/// <summary>
/// Puts a misplaced pull request right: an issue in review with no pull request recorded, whose branch the
/// forge lists exactly one open pull request from, gets that pull request recorded on itself - taking it off
/// another issue where a key recorded it there.
/// </summary>
/// <remarks>
/// <para>Only the project's driver does this, and only for a row it could otherwise merge: a review row, not
/// in the merges column, landing by pull request, with one checkout here whose repository holds a merge check
/// on a branch. A person's record is never moved, nor one the board does not say a key made, nor a filed
/// outsider's: the issue in review is told once per run which ticket holds it, and nothing else changes.</para>
///
/// <para>Clear first, then record. A failure between the two leaves the URL on no issue, and the next tick
/// finds the issue again with nothing to clear. Nothing is retried inside a tick.</para>
///
/// <para>The pull request's own body is left alone: the chain <c>hatch pr</c> writes there is for a board
/// that receives an outsider, which a recorded pull request never is.</para>
/// </remarks>
public sealed class PullRequestRepair
{
    /// <summary>What has already been said on a board this run, keyed on the issue, the holder and the url.</summary>
    private readonly HashSet<string> _told = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One look at every review row this runner could repair. <paramref name="lists"/> is the tick's own, so a
    /// path is listed at most once a tick.
    /// </summary>
    public async Task RepairAsync(
        Runtime runtime, Action<string> complain, IReadOnlyList<ReviewCheckDto> review,
        Dictionary<string, PullRequestListAnswer> lists, CancellationToken ct)
    {
        var failed = new HashSet<string>();

        foreach (var issue in review)
        {
            ct.ThrowIfCancellationRequested();

            if (issue.MergeWork is not null || issue.LandingWork is not null) continue;
            if (issue.Landing != Landings.PullRequest) continue;
            if (!string.IsNullOrEmpty(issue.PullRequestUrl)) continue;
            if (!runtime.Readout.Drives(issue.ProjectKey)) continue;

            var where = Checkouts.ToPoll(issue.Repositories, runtime.Checkouts, runtime.Settings.BaseBranch)
                .Where(p => p.Canonical is not null
                    && issue.MergeChecks.Any(c => c.Canonical == p.Canonical && !string.IsNullOrEmpty(c.Branch)))
                .ToList();

            // None is another runner's row, or a project that binds nothing; two is ambiguous.
            if (where.Count != 1) continue;

            var polled = where[0];
            var matching = issue.MergeChecks.Where(c => c.Canonical == polled.Canonical && !string.IsNullOrEmpty(c.Branch)).ToList();
            if (matching.Count != 1) continue;
            var check = matching[0];

            if (failed.Contains(polled.Path)) continue;

            var list = await ReadListAsync(runtime, complain, polled, lists, ct);
            if (list.Open is null)
            {
                failed.Add(polled.Path);
                continue;
            }

            var found = FromBranch(list, check.Branch!, check.Trunk);

            if (found.Count != 1) continue;

            try
            {
                await PutRightAsync(runtime, complain, issue.Key, check.Branch!, found[0].Key, ct);
            }
            catch (HatchException e)
            {
                complain($"hatch: {issue.Key} - could not record its pull request ({found[0].Key}) - {e.Message}");
            }
        }
    }

    /// <summary>
    /// The open pull requests of <paramref name="polled"/>'s repository, read once a tick through
    /// <paramref name="lists"/> and said once when the forge would not answer.
    /// </summary>
    internal static async Task<PullRequestListAnswer> ReadListAsync(
        Runtime runtime, Action<string> complain, Checkouts.Polled polled,
        Dictionary<string, PullRequestListAnswer> lists, CancellationToken ct)
    {
        if (lists.TryGetValue(polled.Path, out var list)) return list;

        var forge = runtime.Forge(polled.Path, polled.Canonical);
        lists[polled.Path] = list = await forge.ReadOpenPullRequestsAsync(ct);
        if (list.Open is null && list.Why is { } why)
        {
            var name = Path.GetFileName(polled.Path.TrimEnd('/', '\\'));
            complain($"hatch: could not read pull requests in {name} - {why}");
        }

        return list;
    }

    /// <summary>
    /// The open pull requests from <paramref name="branch"/> into <paramref name="trunk"/> in the same
    /// repository, at most two: one is the answer, and a second only says it is not one.
    /// </summary>
    internal static List<KeyValuePair<string, PullRequestAnswer>> FromBranch(
        PullRequestListAnswer list, string branch, string? trunk) =>
        [.. list.Open!
            .Where(p => p.Value.State == PullRequestStates.Open
                && string.Equals(p.Value.HeadBranch, branch, StringComparison.Ordinal)
                && p.Value.IsCrossRepository != true
                && (p.Value.Base is null || string.Equals(p.Value.Base, trunk, StringComparison.Ordinal)))
            .Take(2)];

    internal async Task PutRightAsync(
        Runtime runtime, Action<string> complain, string key, string branch, string url, CancellationToken ct)
    {
        // The row is a moment old: the issue may have declared no code, or had a pull request recorded, since.
        var own = await runtime.QuietBoard.IssueAsync(key, ct);
        if (own is null || own.NoCode is not null || !string.IsNullOrEmpty(own.PullRequestUrl)) return;

        var holders = await runtime.QuietBoard.PullRequestRecordedAsync(url, ct);
        if (holders.Count > 1) return;

        IssueCardDto? holder = null;
        if (holders.Count == 1)
        {
            holder = holders[0];
            if (string.Equals(holder.Key, key, StringComparison.OrdinalIgnoreCase)) return;

            // A key put it there by mistake. Anything else - a person, a board that does not say, an
            // outsider the runner filed - is somebody's word, and is not ours to move.
            if (holder.Contribution is not null || holder.PullRequestRecordedByKind != "key")
            {
                if (_told.Add($"{key}|{holder.Key}|{url}"))
                {
                    try
                    {
                        await runtime.Board.CommentAsync(key,
                            $"The runner found pull request {url} on this issue's branch `{branch}`, but {holder.Key} records it, "
                            + "and a person or an outside pull request put it there, so it was left where it is. "
                            + $"Move it with `hatch pr {key} {url}` if it belongs here.", ct);
                    }
                    catch (HatchException)
                    {
                        _told.Remove($"{key}|{holder.Key}|{url}");
                        throw;
                    }
                }

                return;
            }

            await runtime.Board.PatchAsync(holder.Key, Patch(""), ct);
        }

        await runtime.Board.PatchAsync(key, Patch(url), ct);
        runtime.Say.Line($"hatch: {key} - recorded its pull request ({url}), found from branch {branch}"
            + (holder is null ? "" : $" and taken off {holder.Key}"));

        await runtime.Board.CommentAsync(key,
            $"The runner recorded pull request {url} on this issue, found from its branch `{branch}`"
            + (holder is null ? "." : $", and took it off {holder.Key}, where a key had recorded it."), ct);

        if (holder is not null)
        {
            try
            {
                await runtime.Board.CommentAsync(holder.Key,
                    $"The runner took pull request {url} off this issue: it is the pull request for {key}'s branch `{branch}`, "
                    + $"and now sits on {key}.", ct);
            }
            catch (HatchException e)
            {
                complain($"hatch: {holder.Key} - could not say its pull request moved to {key} - {e.Message}");
            }
        }
    }

    internal static IssuePatchRequest Patch(string url) => new(
        Title: null, Description: null, Type: null, StatusId: null, ParentKey: null,
        ReadyAt: null, DueAt: null, PullRequestUrl: url, ProjectId: null, MoveDescendants: null);
}

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The board's refusals of <c>POST /api/hatch/work/{key}/merged</c>, as one
/// function of board state - so the route and the queue's train note say the
/// same sentence, and a refusal that repeats can be read off without making
/// the write.
/// </summary>
public static class MergedGate
{
    /// <summary>
    /// The sentence the merged route answers <c>409</c> with, or null where it
    /// would take the merge. The order is the route's: the column, the
    /// recorded pull request, the url, the column after, then a held claim.
    /// </summary>
    /// <param name="statuses">The board's columns, as <see cref="Columns"/> reads them.</param>
    /// <param name="from">The column the issue is in.</param>
    /// <param name="recordedUrl">The pull request recorded on the issue.</param>
    /// <param name="requestUrl">The pull request the merge names.</param>
    /// <param name="claimHolder">Asked last: who holds the issue's claim, or null.</param>
    public static string? Refusal(
        List<EfHatchStatus> statuses, EfHatchStatus from, string? recordedUrl, string? requestUrl, Func<string?> claimHolder)
    {
        if (Columns.AwaitingReview(statuses)?.Id != from.Id && !from.MergesPullRequest)
        {
            return $"\"{from.Name}\" is not the review column and does not merge the pull request - " +
                "a merge only advances a ticket waiting there";
        }

        var recorded = recordedUrl?.Trim();
        if (string.IsNullOrEmpty(recorded))
            return "no pull request is recorded on this issue, so there is nothing for a merge to confirm";

        var url = requestUrl?.Trim() ?? "";
        if (!string.Equals(url, recorded, StringComparison.Ordinal))
            return $"\"{url}\" is not the pull request recorded on this issue";

        if (Columns.Closes(statuses, from) is null)
            return $"there is no column after \"{from.Name}\", so there is nowhere for this to go";

        return claimHolder();
    }
}

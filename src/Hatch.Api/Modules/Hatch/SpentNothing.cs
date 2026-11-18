using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The board's one rule for "this claim spent nothing" - a session a runner
/// ran that used no tokens and ended in error, so it never got as far as
/// doing anything to the ticket. Asked by both the stall mark
/// (<see cref="IssueStallController"/>) and a release with outcome
/// <see cref="ClaimOutcomes.Dropped"/> (<see cref="IssueClaimController"/>), so
/// the two cannot come to disagree about what counts.
/// </summary>
public static class SpentNothing
{
    /// <summary>The key a marked release carries in its payload - see <see cref="LetGo.SpentNothing"/>.</summary>
    public const string Marker = "spentNothing";

    /// <summary>The reason a declined stall mark gives in its payload.</summary>
    public const string Because = "spent_nothing";

    /// <summary>
    /// True when there is at least one row and every row has zero tokens (all
    /// four counts, summed) and is an error. A claim with no row spent
    /// nothing that anyone can show, so it is judged as it always was.
    /// </summary>
    public static bool Judge(IEnumerable<(long Tokens, bool IsError)> rows)
    {
        var any = false;
        foreach (var (tokens, isError) in rows)
        {
            if (tokens != 0 || !isError) return false;
            any = true;
        }

        return any;
    }

    /// <summary>What the work-log rows since a claim say about it.</summary>
    public enum Verdict
    {
        /// <summary>No row reached the board since the claim, so there is nothing to judge it by.</summary>
        NoRows,

        /// <summary>There is at least one row and every one spent nothing and is an error.</summary>
        Nothing,

        /// <summary>At least one row spent tokens or was not an error.</summary>
        Spent,
    }

    /// <summary>
    /// Whether the claim taken at <paramref name="claimedAt"/> spent nothing:
    /// the issue's work-log rows that reached the board since, judged by
    /// <see cref="Judge"/>. Rows are placed by <see cref="EfHatchWorkLogEntry.CreatedAt"/>,
    /// the board's own clock like the claim's, and not by the runner's
    /// <see cref="EfHatchWorkLogEntry.StartedAt"/>: a runner whose clock runs
    /// behind cannot push its own row to before the claim, and a re-posted
    /// row keeps its creation, so an old row is never pulled into a new claim.
    /// </summary>
    public static async Task<bool> ClaimAsync(
        HatchContext db, long issueId, DateTimeOffset? claimedAt, CancellationToken ct) =>
        await ClaimVerdictAsync(db, issueId, claimedAt, ct) == Verdict.Nothing;

    /// <summary>
    /// The same read as <see cref="ClaimAsync"/>, keeping apart a claim with no
    /// row since from one whose rows spent tokens: the first is no evidence
    /// either way, the second is.
    /// </summary>
    public static async Task<Verdict> ClaimVerdictAsync(
        HatchContext db, long issueId, DateTimeOffset? claimedAt, CancellationToken ct)
    {
        if (claimedAt is not { } since) return Verdict.NoRows;

        var rows = await db.WorkLog.AsNoTracking()
            .Where(w => w.IssueId == issueId && w.CreatedAt >= since)
            .Select(w => new
            {
                Tokens = w.InputTokens + w.OutputTokens + w.CacheCreationTokens + w.CacheReadTokens,
                w.IsError,
            })
            .ToListAsync(ct);

        if (rows.Count == 0) return Verdict.NoRows;

        return Judge(rows.Select(r => (r.Tokens, r.IsError))) ? Verdict.Nothing : Verdict.Spent;
    }
}

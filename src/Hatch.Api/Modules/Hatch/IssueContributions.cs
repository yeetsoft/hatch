using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>The stored contribution record, as a client reads it.</summary>
public static class IssueContributions
{
    public static ContributionDto Project(EfHatchContribution c) => new(
        c.PullRequestUrl, c.HeadBranch, c.AuthorLogin, c.AuthorAssociation, c.FiledAt, c.FiledBy,
        c.OriginInstance, c.OriginProjectKey, c.OriginKey, c.EnvelopeVersion, c.EnvelopeNote);

    /// <summary>
    /// An origin instance as it is stored and as it is asked for: trimmed, and
    /// without a trailing slash, so that two spellings of one address match
    /// exactly.
    /// </summary>
    public static string NormalizeInstance(string instance) => instance.Trim().TrimEnd('/');

    /// <summary>
    /// The records of a batch of issues, by issue id, in one query - a fixed
    /// number of reads however many issues are scanned.
    /// </summary>
    public static async Task<Dictionary<long, ContributionDto>> ForIssuesAsync(
        HatchContext db, IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        var rows = await db.Contributions.AsNoTracking()
            .Where(c => ids.Contains(c.IssueId))
            .ToListAsync(ct);
        return rows.ToDictionary(c => c.IssueId, Project);
    }
}

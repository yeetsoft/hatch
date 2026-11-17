using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The board's own reading of a repository's changelog - the entries
/// <c>GET /api/changelog/file</c> writes and the ones <c>PUT
/// /api/changelog-files</c> judges a trunk's bytes against - read in one place
/// so the two cannot disagree about what "current" is.
/// </summary>
public static class ChangelogFiles
{
    /// <summary>
    /// The origin inside every <c>url:</c> line of the file: the install's
    /// configured public address, else the one this request came in by. A
    /// Hatch with none set, whose runner reaches it by a different address
    /// than the session that wrote the file, reads <c>behind</c> - setting
    /// <see cref="HatchOptions.PublicBaseUrl"/> is the fix.
    /// </summary>
    public static string Origin(HatchOptions options, HttpRequest request) =>
        HatchOptions.NormalizeBaseUrl(options.PublicBaseUrl) ?? $"{request.Scheme}://{request.Host}";

    /// <summary>
    /// Every closed, parentless issue in a project that elects
    /// <paramref name="canonical"/> for a changelog, less the board's own
    /// changelog tasks. Null when no project elects it.
    /// </summary>
    /// <remarks>
    /// Reads every project, whoever the caller is. The file is the same bytes
    /// for every caller - a trunk's own file is judged against it, and a
    /// scoped caller who got fewer entries would see a file that is forever
    /// behind - so what leaves is entries for a file, never a row the caller can read back.
    /// </remarks>
    public static async Task<List<Changelog.Entry>?> ProjectAsync(HatchContext db, string canonical, CancellationToken ct)
    {
        var projectIds = await db.ProjectRepositories.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Canonical == canonical && r.Changelog)
            .Select(r => r.ProjectId).Distinct().ToListAsync(ct);

        if (projectIds.Count == 0) return null;

        var rows = await db.Issues.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.ParentId == null && i.Status!.IsTerminal && i.ClosedAt != null && projectIds.Contains(i.ProjectId)
                        && !db.ChangelogTasks.Any(t => t.IssueId == i.Id))
            .Select(i => new
            {
                i.Id,
                ProjectKey = i.Project!.Key,
                i.Number,
                i.Type,
                i.Title,
                i.ReleaseNote,
                ClosedAt = i.ClosedAt!.Value,
            })
            .ToListAsync(ct);

        var tree = Rollup.ShapeOf(
            (await db.Issues.IgnoreQueryFilters().AsNoTracking()
                .OrderBy(i => i.Rank).ThenBy(i => i.Id)
                .Select(i => new { i.Id, i.ParentId, i.StatusId })
                .ToListAsync(ct))
            .Select(i => new Rollup.Node(i.Id, i.ParentId, i.StatusId))
            .ToList());

        return rows
            .Select(r => new Changelog.Entry(
                r.Id, IssueKey.Format(r.ProjectKey, r.Number), r.Number, r.Title, r.Type, r.ProjectKey,
                r.ReleaseNote, r.ClosedAt, tree.DescendantCount(r.Id)))
            .ToList();
    }
}

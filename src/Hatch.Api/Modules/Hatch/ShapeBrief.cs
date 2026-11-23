using System.Text.RegularExpressions;
using Hatch.Api.Ef;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What the person gave a draft that a shaping dispatch is for: the dreams
/// recorded on it and the files its description links. Read when the work
/// answer is built, and stored nowhere.
/// </summary>
public static partial class ShapeBrief
{
    /// <summary>
    /// The dreams <c>GET /api/issues/{key}/dreams</c> lists, newest first, and
    /// each file the description links, once, in the order the links first
    /// appear. A link to a file that is not stored is listed as deleted. The
    /// bytes of a file are never loaded.
    /// </summary>
    public static async Task<WorkShapingDto> ReadAsync(HatchContext db, EfHatchIssue issue, CancellationToken ct)
    {
        var dreams = await db.DreamThreads.AsNoTracking()
            .Where(t => t.IssueId == issue.Id)
            .Join(db.Dreams.AsNoTracking(), t => t.DreamId, d => d.Id, (t, d) => d)
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Select(d => new WorkDreamDto(d.Id, d.Body, d.Source))
            .ToListAsync(ct);

        var linked = LinkedFiles(issue.Description);
        var stored = linked.Count == 0
            ? []
            : (await AttachmentsController.Metadata(db, linked).ToListAsync(ct)).ToDictionary(a => a.Id);

        var files = linked
            .Select(id => stored.TryGetValue(id, out var a)
                ? new WorkFileDto(a.Id, a.FileName, a.ContentType, a.Size)
                : new WorkFileDto(id, null, null, null, Deleted: true))
            .ToList();

        return new WorkShapingDto(dreams, files);
    }

    /// <summary>
    /// The ids of the files a description links through <c>/api/attachments/</c>
    /// or <c>/api/images/</c>, each once, in the order they first appear.
    /// </summary>
    internal static IReadOnlyList<Guid> LinkedFiles(string? description)
    {
        if (string.IsNullOrEmpty(description)) return [];

        var seen = new HashSet<Guid>();
        var ids = new List<Guid>();
        foreach (Match m in FileLink().Matches(description))
        {
            if (Guid.TryParse(m.Groups["id"].Value, out var id) && seen.Add(id)) ids.Add(id);
        }

        return ids;
    }

    [GeneratedRegex(@"/api/(?:attachments|images)/(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})", RegexOptions.CultureInvariant)]
    private static partial Regex FileLink();
}

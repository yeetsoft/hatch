using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// An ambition's passes, as data: every work log row that ran from the
/// implementation column and was not dropped, with the verdicts it recorded
/// and the comments said while it ran - see docs/hatch.md.
/// </summary>
[ApiController]
[Route("api/issues/{key}/passes")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class IssuePassesController(HatchContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AmbitionPassDto[]>> GetPasses(string key, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();
        if (NotAnAmbition(issue, key) is { } refusal) return refusal;

        var statuses = await db.Statuses.AsNoTracking().ToListAsync(ct);
        var implementation = Columns.Implementation(statuses);

        var rows = implementation is null
            ? []
            : await db.WorkLog.AsNoTracking()
                .Where(w => w.IssueId == issue.Id && w.PlaybookFromStatusId == implementation.Id
                    && (w.Outcome == null || w.Outcome == ClaimOutcomes.Worked))
                .OrderByDescending(w => w.EndedAt).ThenByDescending(w => w.Id)
                .ToListAsync(ct);

        var capabilities = await db.Capabilities.AsNoTracking()
            .Where(c => c.IssueId == issue.Id)
            .ToDictionaryAsync(c => c.Id, ct);

        var verdicts = capabilities.Count == 0
            ? []
            : await db.CapabilityVerdicts.AsNoTracking()
                .Where(v => capabilities.Keys.Contains(v.CapabilityId))
                .OrderBy(v => v.Id)
                .ToListAsync(ct);

        // Each verdict belongs to the newest row whose window holds it - rows
        // is already newest first - or stands on its own where none does.
        var byRow = new Dictionary<long, List<EfHatchCapabilityVerdict>>();
        var loose = new List<EfHatchCapabilityVerdict>();
        foreach (var v in verdicts)
        {
            var row = rows.FirstOrDefault(r => v.CreatedAt >= r.StartedAt && v.CreatedAt <= r.EndedAt);
            if (row is null) { loose.Add(v); continue; }
            if (!byRow.TryGetValue(row.Id, out var list)) byRow[row.Id] = list = [];
            list.Add(v);
        }

        // Within a row, a capability judged more than once keeps the newer verdict.
        var dedupedByRow = byRow.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.GroupBy(v => v.CapabilityId).Select(g => g.OrderByDescending(v => v.Id).First()).ToList());

        var raw = rows
            .Select(row => new RawEntry(
                row.Id, row.SessionId, row.StartedAt, row.EndedAt, row.Title, row.Summary,
                row.IssueType == "ambition" && row.PlaybookToStatusId == row.PlaybookFromStatusId,
                dedupedByRow.GetValueOrDefault(row.Id, [])))
            .Concat(loose.Select(v => new RawEntry(null, null, v.CreatedAt, v.CreatedAt, null, null, false, [v])))
            .OrderByDescending(e => e.EndedAt)
            .ThenByDescending(e => e.RowId ?? long.MinValue)
            .ToList();

        var earliest = raw.Count == 0 ? default : raw.Min(e => e.StartedAt);
        var latest = raw.Count == 0 ? default : raw.Max(e => e.EndedAt);

        var comments = raw.Count == 0
            ? []
            : await db.Comments.AsNoTracking()
                .Where(c => c.IssueId == issue.Id && c.CreatedAt >= earliest && c.CreatedAt <= latest)
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
                .Select(c => new { c.Id, c.Author, c.Body, c.Kind, c.AnswersId, c.Options, c.Level, c.CreatedAt, c.DeliveredAt, c.DeliveredTo, c.AuthorKind })
                .ToListAsync(ct);

        var result = new List<AmbitionPassDto>();
        for (var i = 0; i < raw.Count; i++)
        {
            var entry = raw[i];
            var older = i + 1 < raw.Count ? raw[i + 1] : null;
            var olderWords = older?.Verdicts.ToDictionary(v => v.CapabilityId, v => v.Verdict) ?? [];

            var shas = entry.Verdicts.Select(v => v.Sha).Distinct().ToList();
            var sha = shas.Count == 1 ? shas[0] : null;

            var verdictDtos = entry.Verdicts
                .OrderBy(v => capabilities[v.CapabilityId].Position)
                .Select(v => new AmbitionPassVerdictDto(
                    v.Id, v.CapabilityId, capabilities[v.CapabilityId].Position, capabilities[v.CapabilityId].Statement,
                    v.Verdict, v.LookedAt, v.Sha, v.Author, v.CreatedAt,
                    olderWords.TryGetValue(v.CapabilityId, out var olderWord) && olderWord != v.Verdict))
                .ToList();

            var commentDtos = comments
                .Where(c => c.CreatedAt >= entry.StartedAt && c.CreatedAt <= entry.EndedAt)
                .Select(c => new CommentDto(c.Id, c.Author, c.Body, c.Kind, c.AnswersId, Questions.ReadOptions(c.Options), c.CreatedAt, c.DeliveredAt, c.DeliveredTo,
                    c.Kind == EfHatchComment.Question ? QuestionLevels.Normalise(c.Level) : null, AuthorKind: c.AuthorKind))
                .ToList();

            result.Add(new AmbitionPassDto(
                entry.RowId, entry.SessionId, entry.StartedAt, entry.EndedAt, entry.Title, entry.Summary,
                entry.CanRecordVerdicts, sha, verdictDtos, commentDtos));
        }

        return result.ToArray();
    }

    private sealed record RawEntry(
        long? RowId, string? SessionId, DateTimeOffset StartedAt, DateTimeOffset EndedAt,
        string? Title, string? Summary, bool CanRecordVerdicts, List<EfHatchCapabilityVerdict> Verdicts);

    private static BadRequestObjectResult? NotAnAmbition(EfHatchIssue issue, string key) =>
        issue.Type == "ambition" ? null : new($"only an ambition has passes - {key} is a {issue.Type}");

    private async Task<EfHatchIssue?> LoadAsync(string key, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return null;

        return await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
    }
}

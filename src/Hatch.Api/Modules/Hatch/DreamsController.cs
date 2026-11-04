using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StorageSearchQuery = Hatch.Api.Modules.Storage.SearchQuery;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Dreams: a line somebody wanted said, leaning toward a project or loose.
/// No page reads this yet - see HA-1723.
/// </summary>
[ApiController]
[Route("api/hatch/dreams")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class DreamsController(HatchContext db, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    private const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    [HttpGet]
    public async Task<ActionResult<DreamsPageDto>> GetDreams(
        [FromQuery] string? q,
        [FromQuery] int? projectId,
        [FromQuery] int limit = DefaultLimit,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        if (limit is < 1 or > MaxLimit)
            return BadRequest($"a limit is between 1 and {MaxLimit} - not {limit}");

        if (offset < 0)
            return BadRequest($"an offset is not negative - not {offset}");

        var query = db.Dreams.AsNoTracking().AsQueryable();

        if (projectId is { } project) query = query.Where(d => d.ProjectId == project);

        var tsquery = StorageSearchQuery.ToTsQuery(q, anyWord: true);

        if (tsquery is not null)
        {
            query = query.Where(d => d.SearchVector.Matches(EF.Functions.ToTsQuery(StorageSearchQuery.Config, tsquery)));
        }

        var total = await query.CountAsync(ct);

        var ordered = tsquery is not null
            ? query.OrderByDescending(d => d.SearchVector.Rank(EF.Functions.ToTsQuery(StorageSearchQuery.Config, tsquery)))
                .ThenByDescending(d => d.CreatedAt)
                .ThenByDescending(d => d.Id)
            : query.OrderByDescending(d => d.CreatedAt)
                .ThenByDescending(d => d.Id);

        var rows = await ordered.Skip(offset).Take(limit)
            .Select(d => new DreamDto(
                d.Id, d.Body, d.Author, d.Source,
                d.ProjectId, d.Project == null ? null : d.Project.Key, d.CreatedAt, d.UpdatedAt))
            .ToListAsync(ct);

        return new DreamsPageDto(rows, total);
    }

    [HttpPost]
    public async Task<ActionResult<DreamDto>> AddDream(DreamCreateRequest request, CancellationToken ct)
    {
        var body = request.Body?.Trim();
        if (string.IsNullOrEmpty(body)) return BadRequest("a dream needs some words");
        if (body.Length > EfHatchComment.MaxBodyLength)
            return BadRequest($"a dream is at most {EfHatchComment.MaxBodyLength} characters");

        var source = request.Source?.Trim();
        if (source is { Length: 0 }) source = null;
        if (source is { Length: > 500 }) return BadRequest("a dream's source is at most 500 characters");

        string? projectKey = null;
        if (request.ProjectId is { } id)
        {
            projectKey = await db.Projects.Where(p => p.Id == id).Select(p => p.Key).FirstOrDefaultAsync(ct);
            if (projectKey is null) return NotFound($"there is no project {id}");
        }

        var now = time.GetUtcNow();
        var dream = new EfHatchDream
        {
            Body = body,
            Author = await caller.ActorNameAsync(ct),
            Source = source,
            ProjectId = request.ProjectId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Dreams.Add(dream);
        await db.SaveChangesAsync(ct);

        var dto = new DreamDto(dream.Id, dream.Body, dream.Author, dream.Source, dream.ProjectId, projectKey, dream.CreatedAt, dream.UpdatedAt);
        return Created($"/api/hatch/dreams/{dream.Id}", dto);
    }

    [HttpPatch("{id:long}")]
    public async Task<ActionResult<DreamDto>> EditDream(long id, [FromBody] JsonElement request, CancellationToken ct)
    {
        var dream = await db.Dreams.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dream is null) return NotFound();

        var changed = false;

        if (request.TryGetProperty("body", out var bodyProp))
        {
            if (bodyProp.ValueKind != JsonValueKind.String) return BadRequest("a dream needs some words");

            var body = bodyProp.GetString()?.Trim();
            if (string.IsNullOrEmpty(body)) return BadRequest("a dream needs some words");
            if (body.Length > EfHatchComment.MaxBodyLength)
                return BadRequest($"a dream is at most {EfHatchComment.MaxBodyLength} characters");

            if (body != dream.Body)
            {
                dream.Body = body;
                changed = true;
            }
        }

        if (request.TryGetProperty("source", out var sourceProp))
        {
            string? source;
            if (sourceProp.ValueKind == JsonValueKind.Null)
            {
                source = null;
            }
            else if (sourceProp.ValueKind == JsonValueKind.String)
            {
                source = sourceProp.GetString()?.Trim();
                if (source is { Length: 0 }) source = null;
                if (source is { Length: > 500 }) return BadRequest("a dream's source is at most 500 characters");
            }
            else
            {
                return BadRequest("a dream's source is a string or null");
            }

            if (source != dream.Source)
            {
                dream.Source = source;
                changed = true;
            }
        }

        if (request.TryGetProperty("projectId", out var projectIdProp))
        {
            int? projectId;
            if (projectIdProp.ValueKind == JsonValueKind.Null)
            {
                projectId = null;
            }
            else if (projectIdProp.ValueKind == JsonValueKind.Number)
            {
                projectId = projectIdProp.GetInt32();
                if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
                    return NotFound($"there is no project {projectId}");
            }
            else
            {
                return BadRequest("a dream's projectId is a number or null");
            }

            if (projectId != dream.ProjectId)
            {
                dream.ProjectId = projectId;
                changed = true;
            }
        }

        if (changed)
        {
            dream.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        var projectKey = dream.ProjectId is { } finalProjectId
            ? await db.Projects.Where(p => p.Id == finalProjectId).Select(p => p.Key).FirstOrDefaultAsync(ct)
            : null;

        return new DreamDto(dream.Id, dream.Body, dream.Author, dream.Source, dream.ProjectId, projectKey, dream.CreatedAt, dream.UpdatedAt);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> DeleteDream(long id, CancellationToken ct)
    {
        var dream = await db.Dreams.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dream is null) return NotFound();

        db.Dreams.Remove(dream);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}

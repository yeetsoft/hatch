using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What an issue drew on: the dreams woven into it, read and replaced whole.
/// </summary>
[ApiController]
[Route("api/issues/{key}/dreams")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class IssueDreamsController(HatchContext db, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Replaces the set of dreams this issue drew on. A request that names the
    /// set already there writes nothing, logs nothing and moves no
    /// <c>UpdatedAt</c> - re-applying an edit is safe everywhere else in Hatch
    /// and is safe here.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<DreamDto[]>> PutIssueDreams(string key, IssueDreamsWriteRequest request, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();

        var requested = request.DreamIds.Distinct().ToList();

        var known = (await db.Dreams.Where(d => requested.Contains(d.Id)).Select(d => d.Id).ToListAsync(ct)).ToHashSet();
        foreach (var id in requested)
        {
            if (!known.Contains(id)) return BadRequest($"there is no dream {id}");
        }

        var current = await db.DreamThreads.Where(t => t.IssueId == issue.Id).Select(t => t.DreamId).ToListAsync(ct);

        if (!current.ToHashSet().SetEquals(requested))
        {
            var toAdd = requested.Except(current).ToList();
            var toRemove = current.Except(requested).ToList();

            if (toRemove.Count > 0)
            {
                var rows = await db.DreamThreads
                    .Where(t => t.IssueId == issue.Id && toRemove.Contains(t.DreamId))
                    .ToListAsync(ct);
                db.DreamThreads.RemoveRange(rows);
            }

            var now = time.GetUtcNow();
            var actor = await caller.ActorNameAsync(ct);

            foreach (var dreamId in toAdd)
            {
                db.DreamThreads.Add(new EfHatchDreamThread
                {
                    DreamId = dreamId,
                    IssueId = issue.Id,
                    Actor = actor,
                    At = now,
                });
            }

            issue.Events.Add(Event(
                actor,
                EfHatchIssueEvent.DreamsWoven,
                new { from = current.OrderBy(x => x), to = requested.OrderBy(x => x) },
                now));

            issue.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return await ReadAsync(issue.Id, ct);
    }

    /// <summary>The dreams this issue drew on, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<DreamDto[]>> GetIssueDreams(string key, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();

        return await ReadAsync(issue.Id, ct);
    }

    private async Task<DreamDto[]> ReadAsync(long issueId, CancellationToken ct)
    {
        var page = await db.DreamThreads.AsNoTracking()
            .Where(t => t.IssueId == issueId)
            .Join(db.Dreams.AsNoTracking(), t => t.DreamId, d => d.Id, (t, d) => d)
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Select(d => new
            {
                d.Id, d.Body, d.Author, d.Source,
                d.ProjectId, ProjectKey = d.Project == null ? null : d.Project.Key, d.CreatedAt, d.UpdatedAt,
            })
            .ToListAsync(ct);

        var lookup = await DreamsController.WovenIntoAsync(db, page.Select(d => d.Id).ToList(), ct);

        return page
            .Select(d => new DreamDto(
                d.Id, d.Body, d.Author, d.Source,
                d.ProjectId, d.ProjectKey, d.CreatedAt, d.UpdatedAt,
                lookup.GetValueOrDefault(d.Id, [])))
            .ToArray();
    }

    private async Task<EfHatchIssue?> LoadAsync(string key, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return null;

        return await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
    }

    private static EfHatchIssueEvent Event(string actor, string kind, object? payload, DateTimeOffset at) => new()
    {
        Actor = actor,
        Kind = kind,
        Payload = payload is null ? null : JsonSerializer.Serialize(payload),
        At = at,
    };
}

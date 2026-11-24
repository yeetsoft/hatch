using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Shelves an issue and the open work under it into a deferred column and
/// posts a dream from it, in one call - see docs/hatch.md, "Dream".
/// </summary>
[ApiController]
[Route("api/issues/{key}/remand")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class IssueRemandController(HatchContext db, IActorDirectory actors, RankService ranks, ICallerIdentity caller, TimeProvider time,
    ChangelogMessages? chat = null) : ControllerBase
{
    /// <summary>The same cap a bulk edit keeps, for the same reason.</summary>
    private const int MaxKeys = 500;

    /// <summary>
    /// Writes the dream, a link row for the root and each descendant that
    /// moves, and the moves themselves. The root always gets a row and a
    /// <c>remanded</c> event, and moves only if it is not already in the
    /// column. A descendant already in a terminal or a deferred column is left
    /// where it is and reported as unchanged.
    /// </summary>
    /// <remarks>
    /// The dream's id is in each event's payload, so the dream is saved first
    /// and everything else second, both inside one transaction: a second save
    /// that fails takes the dream with it.
    /// </remarks>
    [HttpPost]
    public async Task<ActionResult<DreamRemandResultDto>> Remand(string key, DreamRemandRequest request, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } root) return NotFound();

        var statuses = await db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct);
        var target = statuses.FirstOrDefault(s => s.Id == request.StatusId);
        if (target is null) return BadRequest($"there is no column {request.StatusId}");
        if (!target.IsDeferred) return BadRequest($"\"{target.Name}\" is not a deferred column - a remand shelves work");

        var rootKey = IssueKey.Format(root.Project!.Key, root.Number);

        var keys = (request.Keys ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (keys.Count > MaxKeys)
            return BadRequest($"a remand covers at most {MaxKeys} issues at once - this one named {keys.Count}");

        var named = await ResolveDescendantsAsync(root, keys, ct);
        if (named.NotUnder is { } stray) return BadRequest($"{stray} is not under {rootKey}");

        var actor = await caller.ActorNameAsync(ct);
        var who = await SetterAuthority.CallerAsync(actors, ct);
        var now = time.GetUtcNow();
        var byId = statuses.ToDictionary(s => s.Id);

        // The root first, then what the operator named, in the order they named
        // it - the order the shelved work lands at the bottom of the column.
        var moving = new List<EfHatchIssue>();
        var unchanged = new List<string>();
        var remanded = new List<(EfHatchIssue Issue, bool Root)> { (root, true) };

        if (root.StatusId != target.Id) moving.Add(root);
        else unchanged.Add(rootKey);

        foreach (var issue in named.Issues)
        {
            var issueKey = IssueKey.Format(issue.Project!.Key, issue.Number);
            var from = byId.GetValueOrDefault(issue.StatusId);
            if (from is { IsTerminal: true } or { IsDeferred: true })
            {
                unchanged.Add(issueKey);
                continue;
            }

            moving.Add(issue);
            remanded.Add((issue, false));
        }

        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

        var dream = new EfHatchDream
        {
            Body = DreamBody(root),
            Author = actor,
            Source = rootKey,
            ProjectId = root.ProjectId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Dreams.Add(dream);
        await db.SaveChangesAsync(ct);

        foreach (var (issue, isRoot) in remanded)
        {
            db.DreamRemands.Add(new EfHatchDreamRemand
            {
                DreamId = dream.Id,
                IssueId = issue.Id,
                Actor = actor,
                At = now,
                Root = isRoot,
            });

            issue.Events.Add(Event(
                actor,
                EfHatchIssueEvent.Remanded,
                new { dreamId = dream.Id, to = target.Name, toId = target.Id, root = isRoot },
                now));
            issue.UpdatedAt = now;
        }

        var bottoms = new ColumnBottoms(ranks);
        var shelved = new List<long>();
        var changed = new List<string>();
        var closed = new List<EfHatchIssue>();

        foreach (var issue in moving)
        {
            var from = byId.GetValueOrDefault(issue.StatusId);
            issue.Events.Add(Event(actor, EfHatchIssueEvent.StatusChanged, IssueEventPayload.StatusChanged(from, target), now));

            Starts.Record(issue, from is not null && Columns.CrossesStart(statuses, from.Id, target.Id), who, now);

            issue.StatusId = target.Id;
            issue.ClosedAt = Columns.ClosedAt(from, target, now, issue.ClosedAt);
            if (ChangelogTasks.JustClosed(from, target, issue)) closed.Add(issue);
            if (NoCode.ClearOnMove(issue, target, statuses, actor, now) is { } cleared)
                issue.Events.Add(cleared);
            issue.Rank = await bottoms.NextAsync(target.Id, ct);

            if (from?.IsDeferred != true) shelved.Add(issue.Id);
            changed.Add(IssueKey.Format(issue.Project!.Key, issue.Number));
        }

        // Newly shelved, as a move into a deferred column does it: whoever is
        // waiting on one of these is told once, on their own ticket.
        await Deferrals.NoteAsync(db, shelved, target, actor, now, ct);

        if (chat is not null) await chat.QueueClosedAsync(closed, ct);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        await ChangelogTasks.FileForAsync(db, actors, ranks, time, closed, ct);

        var remandedFrom = remanded
            .OrderByDescending(r => r.Root)
            .ThenBy(r => r.Issue.Project!.Key, StringComparer.Ordinal)
            .ThenBy(r => r.Issue.Number)
            .Select(r => IssueKey.Format(r.Issue.Project!.Key, r.Issue.Number))
            .ToArray();

        var dto = new DreamDto(
            dream.Id, dream.Body, dream.Author, dream.Source, dream.ProjectId, root.Project.Key,
            dream.CreatedAt, dream.UpdatedAt, WovenInto: [], RemandedFrom: remandedFrom);

        return Created($"/api/dreams/{dream.Id}", new DreamRemandResultDto(dto, changed, unchanged));
    }

    /// <summary>
    /// The title, a blank line, then the description - the title alone when
    /// there is no description - cut to what a dream may hold.
    /// </summary>
    private static string DreamBody(EfHatchIssue root)
    {
        var body = $"{root.Title}\n\n{root.Description}".Trim();
        return body.Length > EfHatchComment.MaxBodyLength ? body[..EfHatchComment.MaxBodyLength] : body;
    }

    /// <summary>
    /// The tracked issues the keys name, in the order named, or the first key
    /// that names nothing under <paramref name="root"/>.
    /// </summary>
    private async Task<(List<EfHatchIssue> Issues, string? NotUnder)> ResolveDescendantsAsync(
        EfHatchIssue root, IReadOnlyList<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0) return ([], null);

        var descendantIds = (await Rollup.DescendantIdsAsync(db, root.Id, ct)).ToHashSet();
        var under = await db.Issues.AsNoTracking()
            .Where(i => descendantIds.Contains(i.Id))
            .Select(i => new { i.Id, ProjectKey = i.Project!.Key, i.Number })
            .ToListAsync(ct);
        var idOf = under.ToDictionary(i => IssueKey.Format(i.ProjectKey, i.Number), i => i.Id);

        var ids = new List<long>();
        foreach (var key in keys)
        {
            if (!IssueKey.TryParse(key, out var projectKey, out var number)
                || !idOf.TryGetValue(IssueKey.Format(projectKey, number), out var id))
                return ([], key);

            if (!ids.Contains(id)) ids.Add(id);
        }

        var loaded = await db.Issues.Include(i => i.Project).Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        return (ids.Select(id => loaded.Single(i => i.Id == id)).ToList(), null);
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

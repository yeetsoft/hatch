using System.Globalization;
using System.Text.Json;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Files a task to bring a repository's changelog file current the moment a
/// parentless issue closes, in a project with a repository that elects the
/// changelog (<see cref="EfHatchProjectRepository.Changelog"/>) - so the file
/// is kept with no session reading the board.
///
/// Every write that lands a parentless issue in a terminal column asks
/// <see cref="JustClosed"/> beside its own <see cref="Columns.ClosedAt"/> line,
/// and calls <see cref="FileForAsync"/> after its own save, so the close is
/// never lost to a mint conflict here. It is not a hop: the dispatcher carries
/// nothing, and the board is the actor, the way <see cref="ReflectionCadenceJob"/>
/// is - never <see cref="IssuesController.CreateIssueAsync"/>, which resolves
/// an actor from a request that a close hop does not have.
///
/// <para>One task stands per repository. A recorded task (<see
/// cref="EfHatchChangelogTask"/>) still left of the implementation column, and
/// neither done nor shelved, will be picked up and will bring the file current
/// with every close until then, so a second is not filed. Once it has moved
/// in, the file it writes may already be stale, and the next close files
/// another. The file is regenerated whole, so one task stands for all earlier
/// history.</para>
///
/// <para>A recorded task is never in the file (<c>GET /api/changelog/file</c>),
/// and closing one files nothing, or each task's close would file the next.</para>
/// </summary>
public static class ChangelogTasks
{
    public const string Actor = ReflectionCadenceJob.Actor;

    /// <summary>Same bound as <c>ReflectionCadenceJob</c>'s, for the same reason.</summary>
    private const int MintAttempts = 5;

    /// <summary>
    /// What the task says, filled with the remote, the closed issue's key and
    /// the day it closed. Fixed text: the session that picks it up is told the
    /// whole job, and told to change nothing else.
    /// </summary>
    public const string DescriptionTemplate =
        "{1} shipped on {2}. Bring the changelog file of {0} current.\n\n" +
        "In a checkout of {0}:\n\n" +
        "1. Run `hatch changelog --file`.\n" +
        "2. Commit `.hatch/changelog.yml` alone, and push.\n" +
        "3. Open a pull request.\n\n" +
        "Change nothing else. Never edit the file by hand: the command writes it whole from the board.\n\n" +
        "If the command prints `unchanged`, the file is already current. Declare this task finished with no code.";

    /// <summary>
    /// Whether a move from <paramref name="from"/> to <paramref name="to"/>
    /// closed <paramref name="issue"/> now: parentless, into a terminal column,
    /// out of one that was not. Beside <see cref="Columns.ClosedAt"/> at every
    /// call site, which answers the same question about the date.
    /// </summary>
    public static bool JustClosed(EfHatchStatus? from, EfHatchStatus to, EfHatchIssue issue) =>
        issue.ParentId == null && to.IsTerminal && from?.IsTerminal != true;

    /// <summary>
    /// Files the changelog task for each closed issue, one issue at a time and
    /// one save each, so the second of two closes in one request sees the
    /// first's task and files nothing. Call it after the caller's own save.
    /// </summary>
    public static async Task FileForAsync(
        HatchContext db, IActorDirectory actors, RankService ranks, TimeProvider time, IEnumerable<EfHatchIssue> closed, CancellationToken ct, ILogger? log = null)
    {
        var issues = closed.Where(i => i.ParentId == null && i.ClosedAt != null).DistinctBy(i => i.Id).ToList();
        if (issues.Count == 0) return;

        var statuses = await LoadStatusesAsync(db, ct);

        foreach (var issue in issues)
        {
            // Closing a changelog task is the file being brought current, not
            // news for the file - and filing another would never end.
            if (await db.ChangelogTasks.AsNoTracking().AnyAsync(t => t.IssueId == issue.Id, ct)) continue;

            var canonicals = await db.ProjectRepositories.AsNoTracking()
                .Where(r => r.ProjectId == issue.ProjectId && r.Changelog)
                .OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
                .Select(r => r.Canonical)
                .ToListAsync(ct);

            foreach (var canonical in canonicals.Distinct())
                await FileOneAsync(db, actors, ranks, time, statuses, issue, canonical, log, ct);
        }
    }

    /// <summary>
    /// Files one task for <paramref name="canonical"/> from the latest-closed
    /// parentless issue in the project, where a repository has just been
    /// elected: one task stands for all the history before it. None closed,
    /// none filed. The same dedupe applies.
    /// </summary>
    public static async Task FileForRepositoryAsync(
        HatchContext db, IActorDirectory actors, RankService ranks, TimeProvider time, int projectId, string canonical, CancellationToken ct, ILogger? log = null)
    {
        var latest = await db.Issues
            .Where(i => i.ProjectId == projectId && i.ParentId == null && i.Status!.IsTerminal && i.ClosedAt != null
                        && !db.ChangelogTasks.Any(t => t.IssueId == i.Id))
            .OrderByDescending(i => i.ClosedAt).ThenByDescending(i => i.Id)
            .FirstOrDefaultAsync(ct);
        if (latest is null) return;

        await FileOneAsync(db, actors, ranks, time, await LoadStatusesAsync(db, ct), latest, canonical, log, ct);
    }

    private static Task<List<EfHatchStatus>> LoadStatusesAsync(HatchContext db, CancellationToken ct) =>
        db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct);

    private static async Task FileOneAsync(
        HatchContext db, IActorDirectory actors, RankService ranks, TimeProvider time, List<EfHatchStatus> statuses,
        EfHatchIssue closed, string canonical, ILogger? log, CancellationToken ct)
    {
        if (await StandsAsync(db, statuses, canonical, ct)) return;

        await MintAsync(db, actors, ranks, time, statuses, closed, canonical, log, ct);
    }

    /// <summary>
    /// Files one task for <paramref name="canonical"/> when its trunk's file
    /// has been read and found behind or absent - the poll healing a gap no
    /// close left a task for (a task cancelled with no commit, a file deleted,
    /// a repository elected before the poll could read it). The task is filed
    /// from the newest entry of <paramref name="projection"/> the file lacks
    /// (<paramref name="lacking"/>, as <see cref="ChangelogFileState.Judge"/>
    /// names them), or the newest entry at all where the file only has extras.
    /// An empty projection has nothing to record and files nothing.
    /// </summary>
    /// <remarks>
    /// Held by a recorded task standing in <em>any</em> non-terminal column,
    /// deferred ones included (<see cref="OpenAsync"/>), unlike the close's own
    /// <see cref="StandsAsync"/>: a close may file another once the first has
    /// moved into implementation, but a poll runs every interval, and a task
    /// somebody parked on purpose must not be refiled each time. Two runners
    /// putting the same canonical in the same instant can still file two; the
    /// close has the same window.
    /// </remarks>
    public static async Task FileForGapAsync(
        HatchContext db, IActorDirectory actors, RankService ranks, TimeProvider time, string canonical,
        IReadOnlyList<Changelog.Entry> projection, IReadOnlySet<string> lacking, CancellationToken ct, ILogger? log = null)
    {
        if (projection.Count == 0) return;

        var statuses = await LoadStatusesAsync(db, ct);
        if (await OpenAsync(db, canonical, ct)) return;

        var newest = projection
            .Where(e => lacking.Contains(IssueKey.Format(e.ProjectKey, e.Number)))
            .DefaultIfEmpty(projection.MaxBy(e => (e.ClosedAt, e.IssueId)))
            .MaxBy(e => (e.ClosedAt, e.IssueId));

        var closed = await db.Issues.FirstOrDefaultAsync(i => i.Id == newest.IssueId, ct);
        if (closed is null) return;

        await MintAsync(db, actors, ranks, time, statuses, closed, canonical, log, ct);
    }

    private static async Task MintAsync(
        HatchContext db, IActorDirectory actors, RankService ranks, TimeProvider time, List<EfHatchStatus> statuses,
        EfHatchIssue closed, string canonical, ILogger? log, CancellationToken ct)
    {
        var column = Columns.SendBack(statuses)
            ?? statuses.FirstOrDefault(s => s.AgentFiles && !s.IsDeferred && !s.IsTerminal)
            ?? statuses.FirstOrDefault();
        if (column is null) return;

        var now = time.GetUtcNow();

        // A task cannot hang under a task, so one that closes gets a parentless
        // task beside it, and carries its own effective level, since nothing
        // above it will.
        var parentless = !EfHatchIssue.LegalParentTypes["task"].Contains(closed.Type);
        var preApproval = parentless
            ? (await PriorityTree.ForAsync(db, actors, ct)).EffectivePreApproval(closed.Id).Level
            : PreApprovalLevels.None;

        for (var attempt = 1; ; attempt++)
        {
            var project = await db.Projects.FirstAsync(p => p.Id == closed.ProjectId, ct);

            var key = IssueKey.Format(project.Key, closed.Number);
            var title = $"Record {key} in the changelog";
            var taskNumber = project.NextIssueNumber;
            var taskKey = IssueKey.Format(project.Key, taskNumber);

            var issue = new EfHatchIssue
            {
                ProjectId = project.Id,
                Number = taskNumber,
                Type = "task",
                Title = title,
                Description = string.Format(
                    CultureInfo.InvariantCulture, DescriptionTemplate,
                    canonical, key, closed.ClosedAt!.Value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                StatusId = column.Id,
                ParentId = parentless ? null : closed.Id,
                FiledFromId = closed.Id,
                Rank = await ranks.BottomAsync(column.Id, ct),
                PreApproval = preApproval,
                CreatedBy = Actor,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var created = Event(Actor, EfHatchIssueEvent.Created, new { type = issue.Type, title }, now);
            issue.Events.Add(created);

            var filed = new EfHatchIssueEvent
            {
                IssueId = closed.Id,
                Actor = Actor,
                Kind = EfHatchIssueEvent.ChangelogTaskFiled,
                Payload = JsonSerializer.Serialize(new { key = taskKey, canonical }),
                At = now,
            };
            var record = new EfHatchChangelogTask
            {
                Issue = issue,
                SourceIssueId = closed.Id,
                Canonical = canonical,
                CreatedAt = now,
            };

            project.NextIssueNumber++;
            db.Issues.Add(issue);
            db.IssueEvents.Add(filed);
            db.ChangelogTasks.Add(record);

            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException) when (attempt < MintAttempts)
            {
                // Only what this attempt staged: the caller still holds tracked
                // entities of its own, so the tracker is not cleared.
                foreach (var staged in new object[] { record, filed, created, issue })
                    db.Entry(staged).State = EntityState.Detached;
                await db.Entry(project).ReloadAsync(ct);
            }
            catch (DbUpdateException)
            {
                foreach (var staged in new object[] { record, filed, created, issue })
                    db.Entry(staged).State = EntityState.Detached;
                await db.Entry(project).ReloadAsync(ct);

                log?.LogWarning("Project {ProjectKey} could not mint a changelog task for {Remote} after {Attempts} attempts - left for the next close", project.Key, canonical, MintAttempts);
                return;
            }
        }
    }

    /// <summary>
    /// Whether a recorded task for this remote is still to be picked up: left
    /// of the implementation column, and neither done nor shelved.
    /// </summary>
    private static async Task<bool> StandsAsync(HatchContext db, List<EfHatchStatus> statuses, string canonical, CancellationToken ct)
    {
        var standing = await db.ChangelogTasks.AsNoTracking()
            .Where(t => t.Canonical == canonical)
            .Select(t => t.Issue!.StatusId)
            .ToListAsync(ct);

        var byId = statuses.ToDictionary(s => s.Id);
        return standing.Any(id =>
            byId.TryGetValue(id, out var s) && !s.IsTerminal && !s.IsDeferred && !Columns.AtOrPastImplementation(statuses, id));
    }

    /// <summary>Whether a recorded task for this remote stands in any column that is not terminal - deferred ones too.</summary>
    private static Task<bool> OpenAsync(HatchContext db, string canonical, CancellationToken ct) =>
        db.ChangelogTasks.AsNoTracking()
            .AnyAsync(t => t.Canonical == canonical && !t.Issue!.Status!.IsTerminal, ct);

    private static EfHatchIssueEvent Event(string actor, string kind, object? payload, DateTimeOffset at) => new()
    {
        Actor = actor,
        Kind = kind,
        Payload = payload is null ? null : JsonSerializer.Serialize(payload),
        At = at,
    };
}

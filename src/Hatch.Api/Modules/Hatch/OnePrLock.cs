using Hatch.Api.Ef;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Whether work under a story or bug has started - the fact that locks its one-PR
/// setting (<see cref="IssueOnePrController"/>) and that decides which stories and bugs
/// the project's default must stop moving (<see cref="ProjectsController.PutLanding"/>).
/// </summary>
/// <remarks>
/// "Started" is a descendant standing in the implementation column or any
/// column to its right, a terminal one included; a shelved descendant has not
/// started, and nothing has on a board with no implementation column. Measured
/// off the board, the way every landmark is (<see cref="Columns"/>). Work begun
/// on one branch cannot be moved onto the other, so a story's or bug's setting - its own,
/// or the project's it reads - must not change under it.
/// </remarks>
public static class OnePrLock
{
    /// <summary>
    /// The first descendant of the story that has started, as <c>AER-6 is in
    /// "In Progress"</c>, or null while none has.
    /// </summary>
    public static async Task<string?> FirstStartedAsync(HatchContext db, EfHatchIssue story, CancellationToken ct)
    {
        var statuses = await db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct);
        if (Columns.Implementation(statuses) is not { } implementation) return null;

        var board = Columns.Board(statuses);
        var from = board.FindIndex(s => s.Id == implementation.Id);

        var ids = await Rollup.DescendantIdsAsync(db, story.Id, ct);
        if (ids.Count == 0) return null;

        var rows = await db.Issues.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .OrderBy(i => i.Rank).ThenBy(i => i.Id)
            .Select(i => new { ProjectKey = i.Project!.Key, i.Number, i.StatusId })
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            var at = board.FindIndex(s => s.Id == row.StatusId);
            if (at >= from && at >= 0) return $"{IssueKey.Format(row.ProjectKey, row.Number)} is in \"{board[at].Name}\"";
        }

        return null;
    }

    /// <summary>
    /// The stories of a project that have started work and carry no setting of
    /// their own - the ones that read the project's default, and so the ones a
    /// change to it would move under their tasks. Tracked, with their trail, for
    /// the caller to write on.
    /// </summary>
    public static async Task<List<EfHatchIssue>> StartedInheritingAsync(HatchContext db, int projectId, CancellationToken ct)
    {
        var statuses = await db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct);
        if (Columns.Implementation(statuses) is not { } implementation) return [];

        var board = Columns.Board(statuses);
        var from = board.FindIndex(s => s.Id == implementation.Id);

        var rows = await db.Issues.AsNoTracking()
            .Where(i => i.ProjectId == projectId)
            .Select(i => new { i.Id, i.ParentId, i.Type, i.StatusId, i.OnePullRequest })
            .ToListAsync(ct);

        var children = rows.Where(r => r.ParentId != null).GroupBy(r => r.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        var started = rows.ToDictionary(r => r.Id, r => board.FindIndex(s => s.Id == r.StatusId) is var at && at >= 0 && at >= from);

        bool Started(long storyId)
        {
            var seen = new HashSet<long> { storyId };
            var stack = new Stack<long>([storyId]);
            while (stack.Count > 0)
                foreach (var child in children.GetValueOrDefault(stack.Pop(), []))
                {
                    if (!seen.Add(child.Id)) continue;
                    if (started[child.Id]) return true;
                    stack.Push(child.Id);
                }

            return false;
        }

        var ids = rows.Where(r => r.Type is "story" or "bug" && r.OnePullRequest is null && Started(r.Id)).Select(r => r.Id).ToList();
        if (ids.Count == 0) return [];

        return await db.Issues.Include(i => i.Events).Where(i => ids.Contains(i.Id)).OrderBy(i => i.Id).ToListAsync(ct);
    }
}

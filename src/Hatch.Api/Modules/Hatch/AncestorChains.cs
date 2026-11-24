using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The rows a few issues inherit through - each issue and every ancestor above
/// it, and nothing else - and the two questions the merge-train poll asks of
/// them: is this task waiting on its story's pull request, and what priority
/// is live on this row.
/// </summary>
/// <remarks>
/// <para>Both questions are answers about a row and its ancestors alone. They
/// are the very <see cref="PriorityTree"/> walks - this builds the tree with
/// only those rows in it - so the poll and every other reader cannot come to
/// disagree. What the walks read, checked in <see cref="PriorityTree"/>:
/// <see cref="PriorityTree.WaitingOn"/> reads the row's type, pull request and
/// column, then walks parent ids to the nearest story or bug and reads that
/// one's terminal flag, its own one-pull-request flag and its project's;
/// <see cref="PriorityTree.Effective"/> walks parent ids to the nearest level
/// that is not normal, and only across a project line asks
/// <see cref="SetterAuthority.HonoursPriority"/>, which needs live people and
/// their scopes and nothing about the board.</para>
///
/// <para>One statement a level of the chain, so the cost grows with how deep
/// the poll's rows sit and never with how many issues the board holds. It is
/// not a <see cref="PriorityTree"/> build - that reads every issue, and is
/// counted as one by <see cref="ScanTally.RecordTreeBuild"/> - and does not
/// call it: the statement count is what shows its cost.</para>
/// </remarks>
internal sealed class AncestorChains
{
    private readonly PriorityTree _tree;

    private AncestorChains(PriorityTree tree) => _tree = tree;

    /// <summary>The story or bug whose pull request this task is waiting on, or null - see <see cref="PriorityTree.WaitingOn"/>.</summary>
    public string? WaitingOn(long issueId, List<EfHatchStatus> statuses) => _tree.WaitingOn(issueId, statuses);

    /// <summary>The priority live on this issue - see <see cref="PriorityTree.Effective"/>.</summary>
    public (int Level, string? FromKey) Effective(long issueId) => _tree.Effective(issueId);

    /// <summary>
    /// Reads <paramref name="ids"/> and then their parents, and theirs, a level
    /// at a time until no parent is new. Past every query filter: a level that
    /// comes from an ancestor in a project the caller cannot see still applies.
    /// </summary>
    public static async Task<AncestorChains> ForAsync(
        HatchContext db, IActorDirectory actors, IEnumerable<long> ids, List<EfHatchStatus> statuses, CancellationToken ct)
    {
        var terminal = statuses.Where(s => s.IsTerminal).Select(s => s.Id).ToHashSet();
        var loaded = new Dictionary<long, Row>();
        var wanted = ids.Distinct().ToList();

        while (wanted.Count > 0)
        {
            var level = wanted;

            // The same columns PriorityTree.ForAsync reads for its rows, one-PR
            // rows, projects and setters - and HasPr is the same expression.
            var read = await db.Issues.IgnoreQueryFilters().AsNoTracking()
                .Where(i => level.Contains(i.Id))
                .Select(i => new
                {
                    i.Id, i.ParentId, i.Priority, i.PreApproval, ProjectKey = i.Project!.Key, i.Number,
                    i.Type, i.OnePullRequest, ProjectOnePr = i.Project!.OnePullRequest,
                    i.StatusId, HasPr = (i.PullRequestUrl != null && i.PullRequestUrl != "") || (i.NoCode != null && i.NoCode != ""),
                    i.ProjectId, i.PreApprovalSetBy, i.PrioritySetBy,
                })
                .ToListAsync(ct);

            foreach (var r in read)
                loaded[r.Id] = new Row(
                    r.Id, r.ParentId, r.Priority, r.PreApproval, IssueKey.Format(r.ProjectKey, r.Number),
                    r.Type, r.OnePullRequest, r.ProjectOnePr, r.StatusId, r.HasPr, terminal.Contains(r.StatusId),
                    r.ProjectId, r.PreApprovalSetBy, r.PrioritySetBy);

            wanted = read
                .Where(r => r.ParentId is { } parent && !loaded.ContainsKey(parent))
                .Select(r => r.ParentId!.Value)
                .Distinct()
                .ToList();
        }

        // Who may set a level across a project line is only asked when the
        // chains cross one, so a poll that stays in one project reads no more.
        var authority = loaded.Values.Select(r => r.ProjectId).Distinct().Count() > 1
            ? await SetterAuthority.ReadAsync(db, actors, statuses, ct)
            : SetterAuthority.Nobody;

        var tree = new PriorityTree(
            loaded.ToDictionary(r => r.Key, r => ((long?)r.Value.ParentId, r.Value.Display, r.Value.Priority, r.Value.PreApproval)),
            loaded.ToDictionary(r => r.Key, r => (r.Value.Type, r.Value.OnePr, r.Value.ProjectOnePr, r.Value.StatusId, r.Value.HasPr, r.Value.Terminal)),
            projects: loaded.ToDictionary(r => r.Key, r => r.Value.ProjectId),
            setters: loaded.ToDictionary(r => r.Key, r => (r.Value.PreApprovalSetBy, r.Value.PrioritySetBy)),
            authority: authority,
            statuses: statuses);

        return new AncestorChains(tree);
    }

    private sealed record Row(
        long Id, long? ParentId, int Priority, int PreApproval, string Display,
        string Type, bool? OnePr, bool ProjectOnePr, int StatusId, bool HasPr, bool Terminal,
        int ProjectId, Guid? PreApprovalSetBy, Guid? PrioritySetBy);
}

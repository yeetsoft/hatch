using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The rows a scan walks, read without the two columns it never judges by: the
/// description and the search vector built from it. A scan reads the whole
/// board's walkable columns, so what it costs must not follow how much people
/// write in descriptions.
///
/// <para>What a scan needs from a description is its length, and only where a
/// playbook's brief limit could be over it. The length comes back in the same
/// statement as <c>length("Description")</c>; the text is read, in one more
/// statement, only for the rows where it can matter (<see cref="LoadBriefsAsync"/>).
/// A row not loaded carries an empty description, which no fold reads as over a
/// limit.</para>
///
/// <para>Rows are untracked, so nothing a fold does to one is written back, and
/// a scan leaves the context's change tracker as it found it.</para>
/// </summary>
internal static class ScanCandidates
{
    /// <summary>
    /// Every mapped scalar of <see cref="EfHatchIssue"/> except
    /// <see cref="EfHatchIssue.Description"/> and
    /// <see cref="EfHatchIssue.SearchVector"/>. A column added to the entity must
    /// be added here, or a fold reads its default: the guard test walks this
    /// expression and names the property that is missing.
    /// </summary>
    internal static readonly Expression<Func<EfHatchIssue, EfHatchIssue>> Columns = i => new EfHatchIssue
    {
        Id = i.Id,
        ProjectId = i.ProjectId,
        Number = i.Number,
        Type = i.Type,
        Title = i.Title,
        StatusId = i.StatusId,
        ClosedAt = i.ClosedAt,
        PreviousTitles = i.PreviousTitles,
        ParentId = i.ParentId,
        FiledFromId = i.FiledFromId,
        Rank = i.Rank,
        ReadyAt = i.ReadyAt,
        ReadyAtHasTime = i.ReadyAtHasTime,
        DueAt = i.DueAt,
        DueAtHasTime = i.DueAtHasTime,
        PullRequestUrl = i.PullRequestUrl,
        PullRequestRecordedBy = i.PullRequestRecordedBy,
        PullRequestRecordedByKind = i.PullRequestRecordedByKind,
        PullRequestClosedAt = i.PullRequestClosedAt,
        PullRequestMergeState = i.PullRequestMergeState,
        PullRequestReviewDecision = i.PullRequestReviewDecision,
        PullRequestHeadSha = i.PullRequestHeadSha,
        PullRequestHeadBranch = i.PullRequestHeadBranch,
        PullRequestHeadSince = i.PullRequestHeadSince,
        PullRequestReadAt = i.PullRequestReadAt,
        BroughtCurrentAt = i.BroughtCurrentAt,
        BroughtCurrentTrunkSha = i.BroughtCurrentTrunkSha,
        ModelOverride = i.ModelOverride,
        EffortOverride = i.EffortOverride,
        Priority = i.Priority,
        PrioritySetBy = i.PrioritySetBy,
        PreApproval = i.PreApproval,
        PreApprovalSetBy = i.PreApprovalSetBy,
        StartedBy = i.StartedBy,
        StartedAt = i.StartedAt,
        LocalModel = i.LocalModel,
        RunnerTags = i.RunnerTags,
        OnePullRequest = i.OnePullRequest,
        HumanWork = i.HumanWork,
        ReleaseNote = i.ReleaseNote,
        Dangerous = i.Dangerous,
        NoCode = i.NoCode,
        NoCodeAt = i.NoCodeAt,
        WaitingPrTitle = i.WaitingPrTitle,
        WaitingPrBody = i.WaitingPrBody,
        WaitingPrBranch = i.WaitingPrBranch,
        WaitingPrSha = i.WaitingPrSha,
        WaitingPrRemote = i.WaitingPrRemote,
        WaitingPrRunner = i.WaitingPrRunner,
        WaitingPrWhy = i.WaitingPrWhy,
        WaitingPrAt = i.WaitingPrAt,
        StalledAt = i.StalledAt,
        StalledWhy = i.StalledWhy,
        Held = i.Held,
        WipLimit = i.WipLimit,
        BudgetUsd = i.BudgetUsd,
        ClaimToken = i.ClaimToken,
        ClaimedBy = i.ClaimedBy,
        ClaimRunner = i.ClaimRunner,
        ClaimedAt = i.ClaimedAt,
        ClaimHeartbeatAt = i.ClaimHeartbeatAt,
        ClaimChatter = i.ClaimChatter,
        ClaimChatterAt = i.ClaimChatterAt,
        ClaimPlaybookId = i.ClaimPlaybookId,
        ClaimQuietLimitSeconds = i.ClaimQuietLimitSeconds,
        ClaimMaxRunSeconds = i.ClaimMaxRunSeconds,
        AssigneePersonId = i.AssigneePersonId,
        AssigneeApiKeyId = i.AssigneeApiKeyId,
        CreatedBy = i.CreatedBy,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt,
    };

    /// <summary>The columns, and the description's length as the database counts it - characters, not UTF-16 units.</summary>
    private sealed class Read
    {
        public EfHatchIssue Row { get; set; } = null!;
        public int Characters { get; set; }
    }

    /// <summary><see cref="Columns"/> with <c>length("Description")</c> beside it.</summary>
    private static readonly Expression<Func<EfHatchIssue, Read>> WithLength = BuildWithLength();

    private static Expression<Func<EfHatchIssue, Read>> BuildWithLength()
    {
        var issue = Columns.Parameters[0];
        var length = Expression.Property(Expression.Property(issue, nameof(EfHatchIssue.Description)), nameof(string.Length));
        var body = Expression.MemberInit(
            Expression.New(typeof(Read)),
            Expression.Bind(typeof(Read).GetProperty(nameof(Read.Row))!, Columns.Body),
            Expression.Bind(typeof(Read).GetProperty(nameof(Read.Characters))!, length));
        return Expression.Lambda<Func<EfHatchIssue, Read>>(body, issue);
    }

    /// <summary>The rows, in the order <paramref name="ordered"/> gave them, and each one's description length in characters.</summary>
    internal sealed record Rows(List<EfHatchIssue> Issues, IReadOnlyDictionary<long, int> Characters);

    /// <summary>
    /// Reads <paramref name="ordered"/> - already filtered and ordered - untracked and
    /// without its descriptions, then the projects (with their repositories) and
    /// contributions those rows name, each in one statement and shared between
    /// the rows that name them, as tracking shared them before.
    /// </summary>
    internal static async Task<Rows> ReadAsync(HatchContext db, IQueryable<EfHatchIssue> ordered, CancellationToken ct)
    {
        var read = await ordered.AsNoTracking().Select(WithLength).ToListAsync(ct);
        var issues = read.Select(r => r.Row).ToList();
        if (issues.Count == 0) return new Rows(issues, new Dictionary<long, int>());

        var projectIds = issues.Select(i => i.ProjectId).Distinct().ToList();
        var projects = (await db.Projects.AsNoTracking()
                .Include(p => p.Repositories)
                .Where(p => projectIds.Contains(p.Id))
                .ToListAsync(ct))
            .ToDictionary(p => p.Id);

        var ids = issues.Select(i => i.Id).ToList();
        var contributions = (await db.Contributions.AsNoTracking()
                .Where(c => ids.Contains(c.IssueId))
                .ToListAsync(ct))
            .ToDictionary(c => c.IssueId);

        foreach (var issue in issues)
        {
            issue.Project = projects.GetValueOrDefault(issue.ProjectId);
            issue.Contribution = contributions.GetValueOrDefault(issue.Id);
        }

        return new Rows(issues, read.ToDictionary(r => r.Row.Id, r => r.Characters));
    }

    /// <summary>
    /// Loads the description of each row whose brief could be over the limit that
    /// applies to it, in one statement, and none otherwise. .NET counts UTF-16
    /// units and Postgres counts characters, so a description of n characters is
    /// between n and 2n long to the folds: <c>2n &lt;= ceiling</c> can never be over.
    /// The text, where it is loaded, gives the exact length the sentence prints.
    /// </summary>
    /// <param name="ceilingOf">
    /// The smallest brief limit among the playbooks that could speak for a row of
    /// the project, or null where none carries one. A safe superset: the playbook
    /// a row is actually matched to is among them.
    /// </param>
    internal static async Task LoadBriefsAsync(
        HatchContext db, Rows rows, Func<int, int?> ceilingOf, CancellationToken ct)
    {
        var needed = new List<EfHatchIssue>();
        foreach (var issue in rows.Issues)
        {
            if (ceilingOf(issue.ProjectId) is not { } ceiling) continue;
            if (2L * rows.Characters[issue.Id] > ceiling) needed.Add(issue);
        }
        if (needed.Count == 0) return;

        var ids = needed.Select(i => i.Id).ToList();
        var text = await db.Issues.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .Select(i => new { i.Id, i.Description })
            .ToListAsync(ct);
        var byId = text.ToDictionary(t => t.Id, t => t.Description);
        foreach (var issue in needed)
            if (byId.TryGetValue(issue.Id, out var description)) issue.Description = description;
    }

    /// <summary>
    /// Loads the description of one row, for a route that answers with it. The
    /// text may already be there: a row <see cref="LoadBriefsAsync"/> loaded is left alone.
    /// </summary>
    internal static async Task LoadDescriptionAsync(HatchContext db, EfHatchIssue issue, CancellationToken ct)
    {
        issue.Description = await db.Issues.AsNoTracking()
            .Where(i => i.Id == issue.Id)
            .Select(i => i.Description)
            .FirstOrDefaultAsync(ct) ?? "";
    }

    /// <summary>Loads the description of every row in <paramref name="issues"/>, in one statement - the queue's default answer carries them.</summary>
    internal static async Task LoadDescriptionsAsync(HatchContext db, IReadOnlyCollection<EfHatchIssue> issues, CancellationToken ct)
    {
        if (issues.Count == 0) return;
        var ids = issues.Select(i => i.Id).ToList();
        var text = (await db.Issues.AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, i.Description })
                .ToListAsync(ct))
            .ToDictionary(t => t.Id, t => t.Description);
        foreach (var issue in issues)
            if (text.TryGetValue(issue.Id, out var description)) issue.Description = description;
    }
}

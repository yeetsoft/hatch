using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// A batch of <see cref="EfHatchPlaybookProposal"/> rows, rendered for the wire:
/// each change's <c>before</c> as it was stored, <c>after</c> as the patch would
/// leave it, which fields the patch touches, and whether the playbook it names
/// has moved since. Shared with the submission route the next task adds, so a
/// proposal reads the same way wherever it is handed out.
/// </summary>
internal static class PlaybookProposalProjection
{
    internal const string DeletedSinceProposed = "deleted since it was proposed";
    internal const string EditedSinceProposed = "edited since it was proposed";

    public static async Task<IReadOnlyList<PlaybookProposalDto>> ToDtosAsync(
        HatchContext db, IReadOnlyList<EfHatchPlaybookProposal> proposals, CancellationToken ct)
    {
        if (proposals.Count == 0) return [];

        var issueIds = proposals.Select(p => p.IssueId).OfType<long>().Distinct().ToList();
        var issueKeys = await db.Issues.AsNoTracking()
            .Where(i => issueIds.Contains(i.Id))
            .Select(i => new { i.Id, ProjectKey = i.Project!.Key, i.Number })
            .ToDictionaryAsync(r => r.Id, r => IssueKey.Format(r.ProjectKey, r.Number), ct);

        var changes = proposals.SelectMany(p => p.Changes).ToList();
        var prepared = changes.Select(Prepare).ToList();

        var statuses = await db.Statuses.AsNoTracking()
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .ToListAsync(ct);
        var statusNames = statuses.ToDictionary(s => s.Id, s => s.Name);

        var projectIds = prepared
            .SelectMany(c => new[] { c.Before.ProjectId, c.After.ProjectId })
            .OfType<int>().Distinct().ToList();
        var projects = await db.Projects.AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => (p.Key, p.Name), ct);

        var playbookIds = changes.Select(c => c.PlaybookId).OfType<int>().Distinct().ToList();
        var liveUpdatedAt = await db.Playbooks.AsNoTracking()
            .Where(p => playbookIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.UpdatedAt, ct);

        var byChangeId = prepared.ToDictionary(c => c.Change.Id, c => ToChangeDto(c, statuses, statusNames, projects, liveUpdatedAt));

        return proposals.Select(p => new PlaybookProposalDto(
            p.Id,
            p.Reason,
            p.IssueId is { } issueId && issueKeys.TryGetValue(issueId, out var key) ? key : null,
            p.Author,
            p.State,
            p.CreatedAt,
            p.DecidedAt,
            p.DecidedBy,
            p.Changes.Select(c => byChangeId[c.Id]).ToList())).ToList();
    }

    private sealed record Prepared(EfHatchPlaybookProposalChange Change, PlaybookDto BeforeDto, PlaybookState Before, PlaybookState After);

    private static Prepared Prepare(EfHatchPlaybookProposalChange change)
    {
        var beforeDto = EfHatchPlaybookProposalChange.ReadBefore(change.Before);
        var patch = EfHatchPlaybookProposalChange.ReadPatch(change.Patch);

        var before = new PlaybookState(
            beforeDto.FromStatusId, beforeDto.ToStatusId, EfHatchPlaybook.NormalizeTypes(beforeDto.Types), beforeDto.Shape,
            beforeDto.Prompt == beforeDto.DefaultPrompt ? null : beforeDto.Prompt, beforeDto.DefaultPrompt,
            beforeDto.Model, beforeDto.Effort, beforeDto.Budget, beforeDto.BriefLimit, beforeDto.QuietLimitSeconds,
            EfHatchPlaybook.WriteDeniedTools(beforeDto.DeniedTools), beforeDto.IsCheck, beforeDto.ProjectId);

        var after = PlaybookPatches.Apply(before, patch);

        return new Prepared(change, beforeDto, before, after);
    }

    private static PlaybookProposalChangeDto ToChangeDto(
        Prepared p, List<EfHatchStatus> statuses, Dictionary<int, string> statusNames,
        Dictionary<int, (string Key, string Name)> projects, Dictionary<int, DateTimeOffset> liveUpdatedAt)
    {
        var afterFromName = statusNames.TryGetValue(p.After.FromStatusId, out var fromName) ? fromName : p.BeforeDto.FromStatusName;
        var afterToName = statusNames.TryGetValue(p.After.ToStatusId, out var toName) ? toName : p.BeforeDto.ToStatusName;
        var (afterProjectKey, afterProjectName) = ProjectOf(p.After.ProjectId, p.BeforeDto, projects);
        var afterUnreachableReason = Columns.UnreachableReason(statuses, p.After.FromStatusId, p.After.ToStatusId, p.After.IsCheck);

        var after = PlaybooksController.From(
            p.BeforeDto.Id, p.After, afterFromName, afterToName, p.BeforeDto.UpdatedAt, afterProjectKey, afterProjectName,
            afterUnreachableReason);

        bool outOfDate;
        string? outOfDateReason;
        if (p.Change.PlaybookId is not { } playbookId || !liveUpdatedAt.TryGetValue(playbookId, out var updatedAt))
        {
            outOfDate = true;
            outOfDateReason = DeletedSinceProposed;
        }
        else if (updatedAt != p.Change.BasedOnUpdatedAt)
        {
            outOfDate = true;
            outOfDateReason = EditedSinceProposed;
        }
        else
        {
            outOfDate = false;
            outOfDateReason = null;
        }

        return new PlaybookProposalChangeDto(
            p.Change.Id, p.Change.PlaybookId, p.BeforeDto, after, ChangedFields(p.Before, p.After), outOfDate, outOfDateReason);
    }

    /// <summary>The project named by the patched state - the batch's own lookup first, then the one the proposal was made against, when the id did not change.</summary>
    private static (string? Key, string? Name) ProjectOf(
        int? projectId, PlaybookDto before, Dictionary<int, (string Key, string Name)> projects)
    {
        if (projectId is null) return (null, null);
        if (projects.TryGetValue(projectId.Value, out var found)) return (found.Key, found.Name);
        return projectId == before.ProjectId ? (before.ProjectKey, before.ProjectName) : (null, null);
    }

    /// <summary>The fixed vocabulary, in the order the wire always lists it, that <c>quietLimitSeconds</c> is deliberately not part of.</summary>
    private static IReadOnlyList<string> ChangedFields(PlaybookState before, PlaybookState after)
    {
        var fields = new List<string>();
        if (before.FromStatusId != after.FromStatusId) fields.Add("fromStatusId");
        if (before.ToStatusId != after.ToStatusId) fields.Add("toStatusId");
        if (before.Types != after.Types) fields.Add("types");
        if (before.Shape != after.Shape) fields.Add("shape");
        if (before.EffectivePrompt != after.EffectivePrompt) fields.Add("prompt");
        if (before.Model != after.Model) fields.Add("model");
        if (before.Effort != after.Effort) fields.Add("effort");
        if (before.Budget != after.Budget) fields.Add("budget");
        if (before.BriefLimit != after.BriefLimit) fields.Add("briefLimit");
        if (!DeniedToolsOf(before).SequenceEqual(DeniedToolsOf(after))) fields.Add("deniedTools");
        if (before.IsCheck != after.IsCheck) fields.Add("isCheck");
        if (before.ProjectId != after.ProjectId) fields.Add("projectId");
        return fields;
    }

    private static IReadOnlyList<string> DeniedToolsOf(PlaybookState state) =>
        EfHatchPlaybook.ReadDeniedTools(state.DeniedTools) ?? [];
}

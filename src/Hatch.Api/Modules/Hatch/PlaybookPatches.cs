using System.Globalization;
using Hatch.Api.Ef;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>The fields a PATCH can change, as resolved values - a row's, or what the row would be after the patch.</summary>
/// <remarks><see cref="DefaultPrompt"/> is carried so the prompt rule can read it; a patch never changes it.</remarks>
internal sealed record PlaybookState(
    int FromStatusId, int ToStatusId, string Types, string Shape,
    string? Prompt, string? DefaultPrompt, string Model, string Effort,
    int? Budget, int? BriefLimit, string? DeniedTools, bool IsCheck, int? ProjectId)
{
    public static PlaybookState Of(EfHatchPlaybook p) => new(
        p.FromStatusId, p.ToStatusId, p.Types, p.Shape,
        p.Prompt, p.DefaultPrompt, p.Model, p.Effort,
        p.Budget, p.BriefLimit, p.DeniedTools, p.IsCheck, p.ProjectId);

    public string EffectivePrompt => Prompt ?? DefaultPrompt ?? "";
}

/// <summary>
/// What a change to a playbook row must satisfy, without a controller: the
/// refusals PATCH has always made, in the order it has always made them, and
/// the row that results. Shared so a route that proposes a change checks it by
/// the same rules as the route that makes one.
/// </summary>
internal static class PlaybookPatches
{
    /// <summary>
    /// Null when the patch is legal against this state and the live board;
    /// otherwise the sentence PATCH has always used. Checked in order: shape,
    /// project id, project, column and clash, prompt, model and effort, budget,
    /// brief limit, denied tools - so two faults at once report the same one first.
    /// </summary>
    public static async Task<string?> RefusalAsync(
        HatchContext db, int id, PlaybookState current, PlaybookPatchRequest request,
        ModelFamilies? families, CancellationToken ct)
    {
        var from = request.FromStatusId ?? current.FromStatusId;
        var to = request.ToStatusId ?? current.ToStatusId;
        var types = request.Types is null ? current.Types : EfHatchPlaybook.NormalizeTypes(request.Types);
        var shape = request.Shape?.Trim() ?? current.Shape;
        if (PlaybookFile.InvalidShape(shape) is { } shapeError) return shapeError;

        var projectId = current.ProjectId;
        if (request.ProjectId is not null)
        {
            var (value, projectIdError) = ParseProjectId(request.ProjectId);
            if (projectIdError is not null) return projectIdError;
            projectId = value;
        }
        if (projectId is not null)
        {
            var project = await db.Projects
                .Where(p => p.Id == projectId)
                .Select(p => new { p.Key, p.RepoSourced })
                .FirstOrDefaultAsync(ct);
            if (project is null) return $"there is no project {projectId}";
            if (project.RepoSourced)
                return $"project {project.Key} reads its playbooks from .hatch/playbooks.yaml on its trunk, not from a row here";
        }

        if (await Refusal(db, from, to, types, shape, projectId, id, ct) is { } error) return error;

        if (request.Prompt is not null)
        {
            var prompt = request.Prompt.Trim();
            if (prompt.Length == 0)
            {
                if (current.DefaultPrompt is null) return PlaybookFile.InvalidPrompt(prompt);
            }
            else if (PlaybookFile.InvalidPrompt(prompt) is { } promptError)
            {
                return promptError;
            }
        }

        var model = request.Model?.Trim() ?? current.Model;
        var effort = request.Effort?.Trim() ?? current.Effort;
        if (InvalidDispatch(model, effort, families) is { } dispatchError) return dispatchError;

        if (request.Budget is not null && PlaybookFile.ParseBudget(request.Budget).Error is { } budgetError)
            return budgetError;

        if (request.BriefLimit is not null && PlaybookFile.ParseBriefLimit(request.BriefLimit).Error is { } briefLimitError)
            return briefLimitError;

        if (request.DeniedTools is not null &&
            PlaybookFile.InvalidDeniedTools(EfHatchPlaybook.NormalizeDeniedTools(request.DeniedTools)) is { } deniedToolsError)
            return deniedToolsError;

        return null;
    }

    /// <summary>The state after the patch. Pure: assumes <see cref="RefusalAsync"/> returned null, and reads no database and no clock.</summary>
    public static PlaybookState Apply(PlaybookState current, PlaybookPatchRequest request)
    {
        var prompt = current.Prompt;
        if (request.Prompt is not null)
        {
            var trimmed = request.Prompt.Trim();
            prompt = trimmed.Length == 0 || trimmed == current.DefaultPrompt ? null : trimmed;
        }

        var budget = current.Budget;
        if (request.Budget is not null) budget = PlaybookFile.ParseBudget(request.Budget).Value;

        var briefLimit = current.BriefLimit;
        if (request.BriefLimit is not null) briefLimit = PlaybookFile.ParseBriefLimit(request.BriefLimit).Value;

        var deniedTools = current.DeniedTools;
        if (request.DeniedTools is not null)
            deniedTools = EfHatchPlaybook.WriteDeniedTools(EfHatchPlaybook.NormalizeDeniedTools(request.DeniedTools));

        var projectId = current.ProjectId;
        if (request.ProjectId is not null) projectId = ParseProjectId(request.ProjectId).Value;

        return current with
        {
            FromStatusId = request.FromStatusId ?? current.FromStatusId,
            ToStatusId = request.ToStatusId ?? current.ToStatusId,
            Types = request.Types is null ? current.Types : EfHatchPlaybook.NormalizeTypes(request.Types),
            Shape = request.Shape?.Trim() ?? current.Shape,
            Prompt = prompt,
            Model = request.Model?.Trim() ?? current.Model,
            Effort = request.Effort?.Trim() ?? current.Effort,
            Budget = budget,
            BriefLimit = briefLimit,
            DeniedTools = deniedTools,
            IsCheck = request.IsCheck ?? current.IsCheck,
            ProjectId = projectId,
        };
    }

    /// <summary>
    /// Everything about a row that is wrong before its prompt is even read: a
    /// column that does not exist, a transition that goes nowhere, or a
    /// duplicate of one already filed.
    /// </summary>
    /// <remarks>
    /// A row moves an issue between two columns, with one exception: the review
    /// column may name itself, and that row is the review playbook - what an
    /// agent is told when a pull request has stopped merging cleanly or its build
    /// has failed, an increment that starts and ends in review (<see cref="Columns.Target"/>).
    /// Which column that is, is measured off the board as it stands and not
    /// named, the way every other rule about review is.
    /// </remarks>
    internal static async Task<string?> Refusal(
        HatchContext db, int from, int to, string types, string shape, int? projectId, int? id, CancellationToken ct)
    {
        var known = await db.Statuses.Where(s => s.Id == from || s.Id == to).Select(s => s.Id).ToListAsync(ct);
        if (!known.Contains(from)) return $"there is no column {from}";
        if (!known.Contains(to)) return $"there is no column {to}";

        if (from == to)
        {
            var statuses = await db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct);
            if (PlaybookFile.ReviewRefusal(from, to, statuses) is { } reviewError) return reviewError;
        }

        var clash = await db.Playbooks
            .AnyAsync(p => p.FromStatusId == from && p.ToStatusId == to && p.Types == types &&
                           p.Shape == shape && p.ProjectId == projectId && p.Id != id, ct);

        return clash
            ? "there is already a playbook for that transition, those types and that shape"
            : null;
    }

    internal static string? InvalidDispatch(string model, string effort, ModelFamilies? families = null) =>
        PlaybookFile.InvalidModel(model, families) ?? PlaybookFile.InvalidEffort(effort);

    /// <summary>The same, for the project this row names - a whole number that still has to name a project, which the caller checks.</summary>
    internal static (int? Value, string? Error) ParseProjectId(string projectId)
    {
        var trimmed = projectId.Trim();
        if (trimmed.Length == 0) return (null, null);

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1)
            return (parsed, null);

        return (null, $"a project is a whole number of one or more - not \"{trimmed}\"");
    }
}

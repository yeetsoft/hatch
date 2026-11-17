using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Which projects a caller may see: all of them, or exactly the ones named.
/// <see cref="ProjectIds"/> is empty when <see cref="Unbounded"/> is true and
/// means nothing there - ask <see cref="Unbounded"/> first.
/// </summary>
public record VisibleProjects(bool Unbounded, IReadOnlySet<int> ProjectIds)
{
    public static VisibleProjects Everything { get; } = new(true, new HashSet<int>());
    public static VisibleProjects Nothing { get; } = new(false, new HashSet<int>());

    public bool Sees(int projectId) => Unbounded || ProjectIds.Contains(projectId);
}

/// <summary>
/// The one question a filter asks about the caller's scope: everything, or
/// only this set of projects. Nothing reads it yet - the fact and its routes
/// land first, the filter that reads it is the next story.
/// </summary>
public interface ICallerScope
{
    Task<VisibleProjects> ResolveAsync(CancellationToken ct);
}

/// <summary>
/// Answers about the caller, never about an arbitrary person, as
/// <see cref="ProjectAccess"/> does. The rules, in order:
/// no HTTP context is unbounded; a wall that is off is unbounded; a person
/// reads their row and no row is <c>all</c>; a key reads its live owner's row,
/// and a key with no live owner is unbounded.
/// </summary>
/// <remarks>
/// A wall that is up with no person and no key never reaches a Hatch route,
/// so a null principal here is only a key without a live owner.
/// <c>some</c> joins to <c>db.Projects</c> so a project that is gone cannot
/// linger in the answer, whether or not the provider cascades the delete.
/// </remarks>
public class CallerScope(
    HatchContext db, IActorDirectory actors, IHttpContextAccessor accessor, IOptions<AuthOptions> auth) : ICallerScope
{
    private VisibleProjects? resolved;

    public async Task<VisibleProjects> ResolveAsync(CancellationToken ct) => resolved ??= await ReadAsync(ct);

    private async Task<VisibleProjects> ReadAsync(CancellationToken ct)
    {
        if (accessor.HttpContext is null) return VisibleProjects.Everything;
        if (!auth.Value.Enabled) return VisibleProjects.Everything;
        if (await actors.PrincipalAsync(ct) is not { } principal) return VisibleProjects.Everything;

        var row = await db.PersonScopes.AsNoTracking().FirstOrDefaultAsync(s => s.PersonId == principal.Id, ct);
        switch (row?.Mode)
        {
            case null or PersonScopeMode.All:
                return VisibleProjects.Everything;
            case PersonScopeMode.None:
                return VisibleProjects.Nothing;
            default:
                var ids = await db.PersonScopeProjects.AsNoTracking()
                    .Where(p => p.PersonId == principal.Id)
                    .Join(db.Projects, p => p.ProjectId, project => project.Id, (p, _) => p.ProjectId)
                    .ToListAsync(ct);
                return new VisibleProjects(false, ids.ToHashSet());
        }
    }
}

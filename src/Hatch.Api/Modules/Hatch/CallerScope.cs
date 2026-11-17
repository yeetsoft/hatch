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

    /// <summary>
    /// <see cref="ProjectIds"/> as an array - what a query filter hands EF,
    /// which turns an array into one parameter where a set it would have to
    /// enumerate. Built on each call and not kept: a field on a record is part
    /// of its equality.
    /// </summary>
    public int[] Ids => [.. ProjectIds];
}

/// <summary>
/// The one question a filter asks about the caller's scope: everything, or
/// only this set of projects. <see cref="ScopeResolutionFilter"/> asks it once
/// per request and <see cref="RequestScope"/> carries the answer to
/// <see cref="HatchContext"/>, whose query filters are what enforce it.
/// </summary>
public interface ICallerScope
{
    Task<VisibleProjects> ResolveAsync(CancellationToken ct);
}

/// <summary>
/// The caller's resolved scope, held for one request. Scoped, so the filter
/// that resolves it and the <see cref="HatchContext"/> that reads it are the
/// same instance for as long as the request lives.
/// </summary>
/// <remarks>
/// <see cref="Effective"/> is the fail-closed rule: a request that reaches a
/// filtered table before anyone resolved its scope throws rather than
/// answering unbounded. Work with no request behind it - a Quartz job, a
/// hosted service - has no HTTP context and is unbounded, as it always was.
/// </remarks>
public sealed class RequestScope(IHttpContextAccessor? accessor = null)
{
    /// <summary>What the filter resolved, or null before it has.</summary>
    public VisibleProjects? Current { get; set; }

    /// <summary>
    /// True while <see cref="CallerScope"/> is reading its own rows through
    /// the context, so that read sees every project instead of asking for the
    /// answer it is in the middle of finding.
    /// </summary>
    public bool Resolving { get; set; }

    public VisibleProjects Effective()
    {
        if (Resolving) return VisibleProjects.Everything;
        if (Current is { } current) return current;
        if (accessor?.HttpContext is null) return VisibleProjects.Everything;

        throw new InvalidOperationException(
            "A request reached the Hatch data before its scope was resolved. "
            + "ScopeResolutionFilter resolves it for every Hatch controller; a route outside one must resolve ICallerScope itself.");
    }
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
    HatchContext db, IActorDirectory actors, IHttpContextAccessor accessor, IOptions<AuthOptions> auth,
    RequestScope? request = null) : ICallerScope
{
    private VisibleProjects? resolved;

    /// <summary>
    /// Reads once, then remembers. The read goes through the same context the
    /// filters guard, so <see cref="RequestScope.Resolving"/> is up while it
    /// runs and its own queries see everything; the answer is then put on the
    /// request for every later query to read.
    /// </summary>
    public async Task<VisibleProjects> ResolveAsync(CancellationToken ct)
    {
        if (resolved is not null) return resolved;

        if (request is not null) request.Resolving = true;
        try
        {
            resolved = await ReadAsync(ct);
        }
        finally
        {
            if (request is not null) request.Resolving = false;
        }

        if (request is not null) request.Current = resolved;
        return resolved;
    }

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

using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The questions a move gate asks about the caller: is this person an
/// owner of the project, may this person approve on it, and may this person
/// move an issue into a given status at all.
/// </summary>
public interface IProjectAccess
{
    Task<bool> IsOwnerAsync(int projectId, CancellationToken ct);
    Task<bool> CanApproveAsync(int projectId, CancellationToken ct);
    Task<bool> CanMoveIntoAsync(int projectId, EfHatchStatus status, CancellationToken ct);
}

/// <summary>
/// Answers both questions about the caller, never about an arbitrary person -
/// that is the whole question a move gate needs answered, and the only one
/// this service exists to answer. A key or a keyless program is never an
/// owner or approver, so it answers <c>false</c> for one without a query.
/// </summary>
public class ProjectAccess(HatchContext db, IActorDirectory actors) : IProjectAccess
{
    public Task<bool> IsOwnerAsync(int projectId, CancellationToken ct) =>
        HasRoleAsync(projectId, ProjectMemberRole.Owner, ct);

    public async Task<bool> CanApproveAsync(int projectId, CancellationToken ct) =>
        SetterAuthority.Approves(
            await HasRoleAsync(projectId, ProjectMemberRole.Owner, ct),
            await HasRoleAsync(projectId, ProjectMemberRole.Approver, ct));

    public async Task<bool> CanMoveIntoAsync(int projectId, EfHatchStatus status, CancellationToken ct) =>
        SetterAuthority.MayMoveInto(status, await CanApproveAsync(projectId, ct));

    private async Task<bool> HasRoleAsync(int projectId, string role, CancellationToken ct)
    {
        var me = await actors.MeAsync(ct);
        if (me is not { Kind: ActorKind.Person }) return false;

        return await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.PersonId == me.Id && m.Role == role, ct);
    }
}

/// <summary>
/// What a person who set a level may do, answered for any person from rows
/// read once - the rule <see cref="ProjectAccess"/> applies to the caller,
/// written once and shared, so a level honoured across a project line and a
/// press made on the descendant itself cannot disagree.
/// </summary>
/// <remarks>
/// <para>Built by <see cref="PriorityTree.ForAsync"/> with every member row,
/// every scope row and every live person read past the caller's own scope: a
/// judgement about the setter must not depend on who is reading. It is a
/// snapshot of those rows, so the answer is live - take a role away and the
/// next tree reads the level as passed over - and nothing is copied onto a
/// descendant.</para>
///
/// <para>A person is live when <see cref="IActorDirectory.LiveAsync"/> returns
/// them. A deleted person's member row lingers and counts for nothing.</para>
/// </remarks>
public sealed class SetterAuthority
{
    /// <summary>An authority with nobody in it: every setter is passed over.</summary>
    public static readonly SetterAuthority Nobody = new([], [], [], null);

    private readonly HashSet<Guid> _live;
    private readonly Dictionary<(int ProjectId, Guid PersonId), HashSet<string>> _roles;
    private readonly Dictionary<Guid, (string Mode, HashSet<int> Projects)> _scopes;
    private readonly EfHatchStatus? _sendBack;

    internal SetterAuthority(
        HashSet<Guid> live,
        Dictionary<(int, Guid), HashSet<string>> roles,
        Dictionary<Guid, (string, HashSet<int>)> scopes,
        EfHatchStatus? sendBack)
    {
        _live = live;
        _roles = roles;
        _scopes = scopes;
        _sendBack = sendBack;
    }

    /// <summary>
    /// Whether the roles held make an approver: an owner or an approver. The
    /// one place the answer is written, for <see cref="ProjectAccess.CanApproveAsync"/>
    /// and for <see cref="HonoursPreApproval"/> alike.
    /// </summary>
    internal static bool Approves(bool owner, bool approver) => owner || approver;

    /// <summary>
    /// Whether someone who <paramref name="approves"/> may move an issue into
    /// <paramref name="status"/>: any column that is not protected, and a
    /// protected one only for an approver.
    /// </summary>
    internal static bool MayMoveInto(EfHatchStatus status, bool approves) => !status.IsProtected || approves;

    /// <summary>Reads the rows, past every query filter.</summary>
    internal static async Task<SetterAuthority> ReadAsync(HatchContext db, IActorDirectory actors, List<EfHatchStatus> statuses, CancellationToken ct)
    {
        var live = (await actors.LiveAsync(ct)).Where(a => a.Kind == ActorKind.Person).Select(a => a.Id).ToHashSet();

        var roles = new Dictionary<(int, Guid), HashSet<string>>();
        foreach (var m in await db.ProjectMembers.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct))
        {
            if (!roles.TryGetValue((m.ProjectId, m.PersonId), out var held))
                roles[(m.ProjectId, m.PersonId)] = held = [];
            held.Add(m.Role);
        }

        var chosen = (await db.PersonScopeProjects.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct))
            .GroupBy(p => p.PersonId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ProjectId).ToHashSet());

        var scopes = new Dictionary<Guid, (string, HashSet<int>)>();
        foreach (var row in await db.PersonScopes.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct))
            scopes[row.PersonId] = (row.Mode, chosen.GetValueOrDefault(row.PersonId) ?? []);

        return new SetterAuthority(live, roles, scopes, Columns.SendBack(statuses));
    }

    /// <summary>
    /// Whether <paramref name="level"/> of pre-approval, set by
    /// <paramref name="setter"/>, may be honoured on an issue of
    /// <paramref name="projectId"/>. A null setter is never honoured.
    /// </summary>
    public bool HonoursPreApproval(int level, Guid? setter, int projectId)
    {
        if (!Honours(setter, projectId)) return false;

        var approves = Approves(Holds(setter!.Value, projectId, ProjectMemberRole.Owner), Holds(setter.Value, projectId, ProjectMemberRole.Approver));

        return level switch
        {
            PreApprovalLevels.PreApproved => approves,
            PreApprovalLevels.Express => _sendBack is null || MayMoveInto(_sendBack, approves),
            _ => true,
        };
    }

    /// <summary>
    /// Whether a parent started by <paramref name="starter"/> may carry a child of
    /// <paramref name="projectId"/> into <paramref name="into"/> across a project
    /// line: the starter is a live person with that project in scope, and could
    /// have moved the child into that column themselves - a protected one only
    /// as an owner or approver. <see cref="IProjectAccess.CanMoveIntoAsync"/>'s
    /// rule for a named person. A null starter may pull nothing.
    /// </summary>
    public bool MayPull(Guid? starter, int projectId, EfHatchStatus into) =>
        Honours(starter, projectId)
        && MayMoveInto(into, Approves(Holds(starter!.Value, projectId, ProjectMemberRole.Owner), Holds(starter.Value, projectId, ProjectMemberRole.Approver)));

    /// <summary>Whether a priority set by <paramref name="setter"/> may be honoured on an issue of <paramref name="projectId"/>: a live person with that project in scope.</summary>
    public bool HonoursPriority(Guid? setter, int projectId) => Honours(setter, projectId);

    private bool Honours(Guid? setter, int projectId) =>
        setter is { } person && _live.Contains(person) && InScope(person, projectId);

    private bool Holds(Guid person, int projectId, string role) =>
        _roles.TryGetValue((projectId, person), out var held) && held.Contains(role);

    // PersonScope's rule, the one CallerScope reads too.
    private bool InScope(Guid person, int projectId) =>
        !_scopes.TryGetValue(person, out var scope) || PersonScope.Sees(scope.Mode, scope.Projects, projectId);

    /// <summary>
    /// The caller's person id: the person <see cref="IActorDirectory.MeAsync"/>
    /// answers, the same source <see cref="ProjectAccess"/> reads, so a local
    /// person with the wall off counts. Null for a key or for no one.
    /// </summary>
    internal static async Task<Guid?> CallerAsync(IActorDirectory actors, CancellationToken ct) =>
        await actors.MeAsync(ct) is { Kind: ActorKind.Person } me ? me.Id : null;
}

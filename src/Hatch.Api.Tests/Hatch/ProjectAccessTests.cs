using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The two questions a move gate asks: is the caller an owner, and may the
/// caller approve. Both ask about <see cref="IActorDirectory.MeAsync"/>, never
/// about an arbitrary person, and a key is never either one.
/// </summary>
public class ProjectAccessTests
{
    [Fact]
    public async Task AnOwner_IsOwnerAndMayApprove()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Me = ada;
        await h.AddMemberAsync(ada.Id, ProjectMemberRole.Owner);

        Assert.True(await h.Access.IsOwnerAsync(h.ProjectId, default));
        Assert.True(await h.Access.CanApproveAsync(h.ProjectId, default));
    }

    [Fact]
    public async Task AnApprover_IsNotOwnerButMayApprove()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Me = ada;
        await h.AddMemberAsync(ada.Id, ProjectMemberRole.Approver);

        Assert.False(await h.Access.IsOwnerAsync(h.ProjectId, default));
        Assert.True(await h.Access.CanApproveAsync(h.ProjectId, default));
    }

    [Fact]
    public async Task APersonWithNoRow_IsNeitherOwnerNorApprover()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Me = ada;

        Assert.False(await h.Access.IsOwnerAsync(h.ProjectId, default));
        Assert.False(await h.Access.CanApproveAsync(h.ProjectId, default));
    }

    /// <summary>
    /// A key is never an owner, approver or claimant (HA-289). Proved rather
    /// than assumed: a membership row exists for the key's own guid under
    /// ActorKind.Person - a shape that cannot happen in practice - and it is
    /// still read as false, because HasRoleAsync never gets as far as the
    /// query for a caller that is not a person.
    /// </summary>
    [Fact]
    public async Task AKey_IsNeitherOwnerNorApprover_EvenWithAMatchingRow()
    {
        var h = await NewAsync();
        var claude = h.Actors.AddKey("Claude");
        h.Actors.Me = claude;
        await h.AddMemberAsync(claude.Id, ProjectMemberRole.Owner);

        Assert.False(await h.Access.IsOwnerAsync(h.ProjectId, default));
        Assert.False(await h.Access.CanApproveAsync(h.ProjectId, default));
    }

    [Fact]
    public async Task ADeadMember_ReadsAsNeitherOwnerNorApprover()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Me = ada;
        await h.AddMemberAsync(ada.Id, ProjectMemberRole.Owner);

        Assert.True(await h.Access.IsOwnerAsync(h.ProjectId, default));

        // Since deleted: the same liveness rule AssigneeControllerTests
        // exercises for an assignee - a caller that no longer resolves to
        // anybody reads as nobody, whatever row still carries their guid.
        h.Actors.Live.Remove(ada);
        h.Actors.Me = null;

        Assert.False(await h.Access.IsOwnerAsync(h.ProjectId, default));
        Assert.False(await h.Access.CanApproveAsync(h.ProjectId, default));
    }

    // ---- Harness ----

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required IProjectAccess Access { get; init; }
        public required int ProjectId { get; init; }

        public async Task AddMemberAsync(Guid personId, string role)
        {
            Db.ProjectMembers.Add(new EfHatchProjectMember
            {
                ProjectId = ProjectId,
                PersonId = personId,
                Role = role,
                CreatedAt = Now,
            });
            await Db.SaveChangesAsync();
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        await db.SaveChangesAsync();

        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Actors = actors,
            Access = new ProjectAccess(db, actors),
            ProjectId = project.Id,
        };
    }
}

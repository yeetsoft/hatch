using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The ordered list of remotes bound to a project - the one place two
/// spellings of a remote are compared, and the one write in this file closed
/// to a key. Every host below is a reserved name (<c>example.com</c>), never a
/// real forge or account - docs/ethos.md.
/// </summary>
public class ProjectsControllerTests
{
    [Fact]
    public async Task APutThenAGet_RoundTripsAnOrderedListWithABaseBranch()
    {
        var h = await NewAsync();

        var written = Value(await h.Projects.PutRepositories(h.ProjectId,
        [
            new ProjectRepositoryWriteRequest("https://example.com/owner/one.git", "main"),
            new ProjectRepositoryWriteRequest("https://example.com/owner/two.git", null),
        ], default));

        Assert.Equal(["https://example.com/owner/one.git", "https://example.com/owner/two.git"], written.Select(r => r.Remote));
        Assert.Equal("main", written[0].BaseBranch);
        Assert.Null(written[1].BaseBranch);

        var read = Value(await h.Projects.GetRepositories(h.ProjectId, default));
        Assert.Equal(written.Select(r => r.Remote), read.Select(r => r.Remote));
        Assert.Equal(written.Select(r => r.BaseBranch), read.Select(r => r.BaseBranch));
    }

    /// <summary>The one example the acceptance criteria name.</summary>
    [Fact]
    public async Task TheCanonicalFormOfAnScpLikeRemote_ReadsAsHostOwnerRepo()
    {
        var h = await NewAsync();

        var written = Value(await h.Projects.PutRepositories(
            h.ProjectId, [new ProjectRepositoryWriteRequest("git@example.com:Owner/Repo.git", null)], default));

        Assert.Equal("example.com/owner/repo", Assert.Single(written).Canonical);
    }

    [Fact]
    public async Task APut_ReplacesAndReorders()
    {
        var h = await NewAsync();
        await h.Projects.PutRepositories(h.ProjectId,
        [
            new ProjectRepositoryWriteRequest("https://example.com/owner/one.git", null),
            new ProjectRepositoryWriteRequest("https://example.com/owner/two.git", null),
        ], default);

        var written = Value(await h.Projects.PutRepositories(h.ProjectId,
        [
            new ProjectRepositoryWriteRequest("https://example.com/owner/two.git", null),
            new ProjectRepositoryWriteRequest("https://example.com/owner/three.git", null),
        ], default));

        Assert.Equal(["example.com/owner/two", "example.com/owner/three"], written.Select(r => r.Canonical));
        Assert.Equal(2, await h.Db.ProjectRepositories.CountAsync());
    }

    [Fact]
    public async Task ADuplicateByCanonicalForm_IsRefusedNamingBothEntries()
    {
        var h = await NewAsync();

        var result = await h.Projects.PutRepositories(h.ProjectId,
        [
            new ProjectRepositoryWriteRequest("https://example.com/owner/repo.git", null),
            new ProjectRepositoryWriteRequest("git@example.com:owner/repo.git", null),
        ], default);

        Assert.Contains("entry 2", Reason(result.Result));
        Assert.Contains("entry 1", Reason(result.Result));
        Assert.Empty(await h.Db.ProjectRepositories.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyRemote_IsRefusedNamingTheEntry(string remote)
    {
        var h = await NewAsync();

        var result = await h.Projects.PutRepositories(
            h.ProjectId, [new ProjectRepositoryWriteRequest(remote, null)], default);

        Assert.Contains("entry 1", Reason(result.Result));
    }

    [Fact]
    public async Task AnUnparseableRemote_IsRefusedNamingTheEntry()
    {
        var h = await NewAsync();

        var result = await h.Projects.PutRepositories(
            h.ProjectId, [new ProjectRepositoryWriteRequest("not a url", null)], default);

        Assert.Contains("entry 1", Reason(result.Result));
    }

    [Fact]
    public async Task OverTheLimit_IsRefused()
    {
        var h = await NewAsync();
        var many = Enumerable.Range(0, ProjectsController.MaxRepositories + 1)
            .Select(i => new ProjectRepositoryWriteRequest($"https://example.com/owner/repo{i}.git", (string?)null))
            .ToList();

        var result = await h.Projects.PutRepositories(h.ProjectId, many, default);

        Assert.Contains("at most", Reason(result.Result));
    }

    /// <summary>There is no UpdatedAt here to make this true by accident - the handler has to notice for itself.</summary>
    [Fact]
    public async Task ResendingTheSameList_WritesNothing()
    {
        var h = await NewAsync();
        var request = new List<ProjectRepositoryWriteRequest> { new("https://example.com/owner/repo.git", "main") };
        var first = Value(await h.Projects.PutRepositories(h.ProjectId, request, default));
        var idsBefore = await h.Db.ProjectRepositories.Select(r => r.Id).ToListAsync();

        h.Time.Advance(TimeSpan.FromHours(1));
        var second = Value(await h.Projects.PutRepositories(h.ProjectId, request, default));
        var idsAfter = await h.Db.ProjectRepositories.Select(r => r.Id).ToListAsync();

        Assert.Equal(first.Select(r => r.Canonical), second.Select(r => r.Canonical));
        Assert.Equal(idsBefore, idsAfter);
    }

    [Fact]
    public async Task AKey_IsRefusedOnThePutAndAllowedOnTheGet()
    {
        var h = await NewAsync(program: true);

        var put = await h.Projects.PutRepositories(
            h.ProjectId, [new ProjectRepositoryWriteRequest("https://example.com/owner/repo.git", null)], default);
        Assert.Equal(403, ((ObjectResult)put.Result!).StatusCode);
        Assert.Empty(await h.Db.ProjectRepositories.ToListAsync());

        var get = Value(await h.Projects.GetRepositories(h.ProjectId, default));
        Assert.Empty(get);
    }

    [Fact]
    public async Task APutAgainstAProjectThatDoesNotExist_Is404()
    {
        var h = await NewAsync();

        var result = await h.Projects.PutRepositories(
            12345, [new ProjectRepositoryWriteRequest("https://example.com/owner/repo.git", null)], default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    /// <summary>The bindings are not a reason to keep a project around - they go with it, the way an empty project itself may.</summary>
    [Fact]
    public async Task DeletingAProject_TakesItsBindingsWithIt()
    {
        var h = await NewAsync();
        await h.Projects.PutRepositories(
            h.ProjectId, [new ProjectRepositoryWriteRequest("https://example.com/owner/repo.git", null)], default);

        var result = await h.Projects.DeleteProject(h.ProjectId, default);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await h.Db.ProjectRepositories.ToListAsync());
    }

    [Fact]
    public async Task GetProjects_CarriesTheListInOrder()
    {
        var h = await NewAsync();
        await h.Projects.PutRepositories(h.ProjectId,
        [
            new ProjectRepositoryWriteRequest("https://example.com/owner/one.git", null),
            new ProjectRepositoryWriteRequest("https://example.com/owner/two.git", null),
        ], default);

        var project = Value(await h.Projects.GetProjects(default)).Single(p => p.Id == h.ProjectId);

        Assert.Equal(["example.com/owner/one", "example.com/owner/two"], project.Repositories.Select(r => r.Canonical));
    }

    [Fact]
    public async Task GetProjects_CarriesMembersAndTheCallersCanApproveCanAdminister()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        var stranger = h.Actors.AddPerson("Alice");
        await h.Projects.PutMember(created.Id, stranger.Id, new ProjectMemberWriteRequest(ProjectMemberRole.Approver), default);

        var ownerView = Value(await h.Projects.GetProjects(default)).Single(p => p.Id == created.Id);
        Assert.Equal(2, ownerView.Members.Count);
        Assert.Contains(ownerView.Members, m => m.PersonId == h.Nathan.Id && m.Role == ProjectMemberRole.Owner);
        Assert.Contains(ownerView.Members, m => m.PersonId == stranger.Id && m.Role == ProjectMemberRole.Approver);
        Assert.True(ownerView.CanAdminister);
        Assert.True(ownerView.CanApprove);

        h.Actors.Me = stranger;
        var approverView = Value(await h.Projects.GetProjects(default)).Single(p => p.Id == created.Id);
        Assert.False(approverView.CanAdminister);
        Assert.True(approverView.CanApprove);

        var directoryOnly = h.Actors.AddPerson("Bob");
        h.Actors.Me = directoryOnly;
        var strangerView = Value(await h.Projects.GetProjects(default)).Single(p => p.Id == created.Id);
        Assert.False(strangerView.CanAdminister);
        Assert.False(strangerView.CanApprove);
    }

    // ---- Colour and icon ----

    [Fact]
    public async Task ACreatedProjectsColourAndIcon_RoundTripThroughTheList()
    {
        var h = await NewAsync();

        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test", "#AB12EF", "rocket"), default));

        Assert.Equal("#ab12ef", created.Color);
        Assert.Equal("rocket", created.Icon);

        var read = Value(await h.Projects.GetProjects(default)).Single(p => p.Id == created.Id);
        Assert.Equal("#ab12ef", read.Color);
        Assert.Equal("rocket", read.Icon);
    }

    [Fact]
    public async Task PatchingColorToEmpty_ClearsJustTheColour()
    {
        var h = await NewAsync();
        await h.Projects.PatchProject(h.ProjectId, new ProjectPatchRequest(null, Color: "#2a78d6", Icon: "rocket"), default);

        var patched = Value(await h.Projects.PatchProject(h.ProjectId, new ProjectPatchRequest(null, Color: ""), default));

        Assert.Null(patched.Color);
        Assert.Equal("rocket", patched.Icon);
    }

    [Fact]
    public async Task PatchingIconToEmpty_ClearsJustTheIcon()
    {
        var h = await NewAsync();
        await h.Projects.PatchProject(h.ProjectId, new ProjectPatchRequest(null, Color: "#2a78d6", Icon: "rocket"), default);

        var patched = Value(await h.Projects.PatchProject(h.ProjectId, new ProjectPatchRequest(null, Icon: ""), default));

        Assert.Equal("#2a78d6", patched.Color);
        Assert.Null(patched.Icon);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#ab1")]
    [InlineData("6b7280")]
    [InlineData("#gggggg")]
    [InlineData("rgb(1,2,3)")]
    public async Task ACreateWithAColourThatIsNotAHexValue_IsRefusedNamingColourAndWritesNothing(string color)
    {
        var h = await NewAsync();

        var result = await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test", color), default);

        Assert.Contains("colour", Reason(result.Result));
        Assert.Empty(await h.Db.Projects.Where(p => p.Key == "TST").ToListAsync());
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#ab1")]
    [InlineData("6b7280")]
    [InlineData("#gggggg")]
    [InlineData("rgb(1,2,3)")]
    public async Task APatchWithAColourThatIsNotAHexValue_IsRefusedNamingColourAndChangesNothing(string color)
    {
        var h = await NewAsync();

        var result = await h.Projects.PatchProject(h.ProjectId, new ProjectPatchRequest(null, Color: color), default);

        Assert.Contains("colour", Reason(result.Result));
        Assert.Null((await h.Db.Projects.FindAsync(h.ProjectId))!.Color);
    }

    [Theory]
    [InlineData("Rocket")]
    [InlineData("a slug")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task ACreateWithABadIconSlug_IsRefusedNamingIconAndWritesNothing(string icon)
    {
        var h = await NewAsync();

        var result = await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test", Icon: icon), default);

        Assert.Contains("icon", Reason(result.Result));
        Assert.Empty(await h.Db.Projects.Where(p => p.Key == "TST").ToListAsync());
    }

    [Theory]
    [InlineData("Rocket")]
    [InlineData("a slug")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task APatchWithABadIconSlug_IsRefusedNamingIconAndChangesNothing(string icon)
    {
        var h = await NewAsync();

        var result = await h.Projects.PatchProject(h.ProjectId, new ProjectPatchRequest(null, Icon: icon), default);

        Assert.Contains("icon", Reason(result.Result));
        Assert.Null((await h.Db.Projects.FindAsync(h.ProjectId))!.Icon);
    }

    [Fact]
    public async Task ResendingTheColourAndIconAProjectAlreadyHolds_IsANoOp()
    {
        var h = await NewAsync();
        var first = Value(await h.Projects.PatchProject(h.ProjectId, new ProjectPatchRequest(null, Color: "#2a78d6", Icon: "rocket"), default));

        var second = Value(await h.Projects.PatchProject(h.ProjectId, new ProjectPatchRequest(null, Color: "#2a78d6", Icon: "rocket"), default));

        Assert.Equal(first.Color, second.Color);
        Assert.Equal(first.Icon, second.Icon);
    }

    // ---- Logo ----

    [Fact]
    public async Task APngUpload_Returns200AndSetsLogoUpdatedAt()
    {
        var h = await NewAsync();

        var written = Value(await PutLogo(h.Projects, h.ProjectId, Png));

        Assert.Equal(Now, written.LogoUpdatedAt);
        var read = Value(await h.Projects.GetProjects(default)).Single(p => p.Id == h.ProjectId);
        Assert.Equal(Now, read.LogoUpdatedAt);
    }

    [Fact]
    public async Task TheUploadedBytes_AreServedBackWithTheSniffedTypeAndAnETag()
    {
        var h = await NewAsync();
        await PutLogo(h.Projects, h.ProjectId, Png);

        var result = Assert.IsType<FileContentResult>(await h.Projects.GetLogo(h.ProjectId, default));

        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(Png, result.FileContents);
        Assert.NotNull(result.EntityTag);
        Assert.Equal("nosniff", h.Projects.ControllerContext.HttpContext.Response.Headers.XContentTypeOptions);
    }

    [Fact]
    public async Task BytesThatAreNotAnImage_AreRefusedAndNothingIsStored()
    {
        var h = await NewAsync();

        var result = await PutLogo(h.Projects, h.ProjectId, "<script>alert(1)</script>"u8.ToArray());

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(PersonPhoto.UnsupportedError, bad.Value);
        Assert.Empty(await h.Db.ProjectLogos.ToListAsync());
    }

    [Fact]
    public async Task AnEmptyBody_IsRefused()
    {
        var h = await NewAsync();

        var result = await PutLogo(h.Projects, h.ProjectId, []);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(PersonPhoto.EmptyError, bad.Value);
    }

    [Fact]
    public async Task AKey_IsRefusedOnThePutAndTheDeleteButNotTheGet()
    {
        var h = await NewAsync(program: true);

        var put = await PutLogo(h.Projects, h.ProjectId, Png);
        Assert.Equal(403, ((ObjectResult)put.Result!).StatusCode);

        var delete = await h.Projects.DeleteLogo(h.ProjectId, default);
        Assert.Equal(403, ((ObjectResult)delete.Result!).StatusCode);

        Assert.IsType<NotFoundResult>(await h.Projects.GetLogo(h.ProjectId, default));
    }

    [Fact]
    public async Task DeletingTheLogo_Makes404TheNextGetAndClearsLogoUpdatedAt()
    {
        var h = await NewAsync();
        await PutLogo(h.Projects, h.ProjectId, Png);

        var deleted = Value(await h.Projects.DeleteLogo(h.ProjectId, default));

        Assert.Null(deleted.LogoUpdatedAt);
        Assert.IsType<NotFoundResult>(await h.Projects.GetLogo(h.ProjectId, default));
        Assert.Empty(await h.Db.ProjectLogos.ToListAsync());
    }

    [Fact]
    public async Task DeletingAProject_TakesItsLogoWithIt()
    {
        var h = await NewAsync();
        await PutLogo(h.Projects, h.ProjectId, Png);

        await h.Projects.DeleteProject(h.ProjectId, default);

        Assert.Empty(await h.Db.ProjectLogos.ToListAsync());
    }

    [Fact]
    public async Task HasNoLogoToServeUntilOneIsUploaded()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>(await h.Projects.GetLogo(h.ProjectId, default));
    }

    [Fact]
    public async Task DeletingALogoThatIsNotThere_Is404()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>((await h.Projects.DeleteLogo(h.ProjectId, default)).Result);
    }

    // ---- Members ----

    [Fact]
    public async Task CreatingAProjectAsAPerson_MakesThemSoleOwner()
    {
        var h = await NewAsync();

        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        var members = Value(await h.Projects.GetMembers(created.Id, default));

        var member = Assert.Single(members);
        Assert.Equal(h.Nathan.Id, member.PersonId);
        Assert.Equal(ProjectMemberRole.Owner, member.Role);
    }

    /// <summary>The ordinary case: the person who created it is the one asking, so both questions are yes.</summary>
    [Fact]
    public async Task CreatingAProjectAsAPerson_TheResponseSaysTheyCanAdministerAndApprove()
    {
        var h = await NewAsync();

        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        Assert.True(created.CanAdminister);
        Assert.True(created.CanApprove);
        var member = Assert.Single(created.Members);
        Assert.Equal(h.Nathan.Id, member.PersonId);
    }

    [Fact]
    public async Task CreatingAsAKeyWithAnOwnerPrincipal_MakesThatPersonOwner()
    {
        var h = await NewAsync(program: true);
        h.Actors.Principal = h.Nathan;

        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        var members = Value(await h.Projects.GetMembers(created.Id, default));

        var member = Assert.Single(members);
        Assert.Equal(h.Nathan.Id, member.PersonId);
        Assert.Equal(ProjectMemberRole.Owner, member.Role);
    }

    /// <summary>
    /// The key made Nathan the owner, but the key is not Nathan: the response's
    /// own CanAdminister/CanApprove must stay false for the caller, which is the
    /// key - not the principal it just wrote as owner.
    /// </summary>
    [Fact]
    public async Task CreatingAsAKeyWithAnOwnerPrincipal_TheResponseSaysTheKeyCannotAdministerOrApprove()
    {
        var h = await NewAsync(program: true);
        h.Actors.Principal = h.Nathan;

        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        Assert.False(created.CanAdminister);
        Assert.False(created.CanApprove);
        var member = Assert.Single(created.Members);
        Assert.Equal(h.Nathan.Id, member.PersonId);
    }

    [Fact]
    public async Task CreatingAsAKeyWithNoPrincipal_HasNoMembers()
    {
        var h = await NewAsync(program: true);

        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        var members = Value(await h.Projects.GetMembers(created.Id, default));

        Assert.Empty(members);
    }

    [Fact]
    public async Task AKey_Is403OnTheWritesAnd200OnGetMembers()
    {
        var h = await NewAsync(program: true);

        var put = await h.Projects.PutMember(h.ProjectId, Guid.NewGuid(), new ProjectMemberWriteRequest(ProjectMemberRole.Owner), default);
        Assert.Equal(403, ((ObjectResult)put.Result!).StatusCode);

        var delete = await h.Projects.DeleteMember(h.ProjectId, Guid.NewGuid(), default);
        Assert.Equal(403, ((ObjectResult)delete.Result!).StatusCode);

        var claim = await h.Projects.ClaimProject(h.ProjectId, default);
        Assert.Equal(403, ((ObjectResult)claim.Result!).StatusCode);

        var get = Value(await h.Projects.GetMembers(h.ProjectId, default));
        Assert.Empty(get);
    }

    [Fact]
    public async Task ANonOwnerPerson_Is403OnPutAndDeleteMemberNamingTheProjectKey()
    {
        var h = await NewAsync();
        await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default);
        var notAnOwner = h.Actors.AddPerson("Alice");
        h.Actors.Principal = notAnOwner;
        h.Actors.Me = notAnOwner;

        var put = await h.Projects.PutMember(h.ProjectId, Guid.NewGuid(), new ProjectMemberWriteRequest(ProjectMemberRole.Approver), default);
        Assert.Contains("AER", Reason(put.Result));

        var delete = await h.Projects.DeleteMember(h.ProjectId, h.Nathan.Id, default);
        Assert.Contains("AER", Reason(delete.Result));
    }

    [Fact]
    public async Task AddingASecondOwnerThenRemovingTheFirst_Succeeds()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        var alice = h.Actors.AddPerson("Alice");

        var afterAdd = Value(await h.Projects.PutMember(created.Id, alice.Id, new ProjectMemberWriteRequest(ProjectMemberRole.Owner), default));
        Assert.Equal(2, afterAdd.Count);
        Assert.All(afterAdd, m => Assert.Equal(ProjectMemberRole.Owner, m.Role));

        var afterRemove = Value(await h.Projects.DeleteMember(created.Id, h.Nathan.Id, default));
        var remaining = Assert.Single(afterRemove);
        Assert.Equal(alice.Id, remaining.PersonId);
    }

    [Fact]
    public async Task RemovingTheSoleRemainingOwner_IsRefusedAndChangesNothing()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        var result = await h.Projects.DeleteMember(created.Id, h.Nathan.Id, default);

        Assert.IsType<ConflictObjectResult>(result.Result);
        var members = Value(await h.Projects.GetMembers(created.Id, default));
        var member = Assert.Single(members);
        Assert.Equal(ProjectMemberRole.Owner, member.Role);
    }

    [Fact]
    public async Task DemotingTheSoleRemainingOwner_IsRefusedAndChangesNothing()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        var result = await h.Projects.PutMember(created.Id, h.Nathan.Id, new ProjectMemberWriteRequest(ProjectMemberRole.Approver), default);

        Assert.IsType<ConflictObjectResult>(result.Result);
        var members = Value(await h.Projects.GetMembers(created.Id, default));
        var member = Assert.Single(members);
        Assert.Equal(ProjectMemberRole.Owner, member.Role);
    }

    [Fact]
    public async Task ClaimProject_SucceedsAgainstAProjectWithNoMembersAtAll()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        // The sole-owner guard on DeleteMember would refuse reaching this
        // state through the route, so clear the table directly instead.
        h.Db.ProjectMembers.RemoveRange(h.Db.ProjectMembers);
        await h.Db.SaveChangesAsync();

        var alice = h.Actors.AddPerson("Alice");
        h.Actors.Me = alice;

        var claimed = Value(await h.Projects.ClaimProject(created.Id, default));

        var member = Assert.Single(claimed);
        Assert.Equal(alice.Id, member.PersonId);
        Assert.Equal(ProjectMemberRole.Owner, member.Role);
    }

    [Fact]
    public async Task ClaimProject_SucceedsWhenTheOnlyOwnerIsNoLongerLive()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        h.Actors.Live.Remove(h.Nathan);
        var alice = h.Actors.AddPerson("Alice");
        h.Actors.Me = alice;

        var claimed = Value(await h.Projects.ClaimProject(created.Id, default));

        var member = Assert.Single(claimed);
        Assert.Equal(alice.Id, member.PersonId);
        Assert.Equal(ProjectMemberRole.Owner, member.Role);
    }

    [Fact]
    public async Task ClaimProject_Is409AgainstAProjectWithALiveOwner()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        var alice = h.Actors.AddPerson("Alice");
        h.Actors.Me = alice;

        var result = await h.Projects.ClaimProject(created.Id, default);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task RePuttingARoleAMemberAlreadyHolds_WritesNothing()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        var countBefore = await h.Db.ProjectMembers.CountAsync();

        var result = Value(await h.Projects.PutMember(created.Id, h.Nathan.Id, new ProjectMemberWriteRequest(ProjectMemberRole.Owner), default));

        Assert.Equal(countBefore, await h.Db.ProjectMembers.CountAsync());
        Assert.Equal(countBefore, result.Count);
    }

    // ---- Harness ----

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required ProjectsController Projects { get; init; }
        public required HatchContext Db { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required Actor Nathan { get; init; }
    }

    private static async Task<Harness> NewAsync(bool program = false)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);

        var actors = new StubActorDirectory();
        var nathan = actors.AddPerson("Nathan");

        var caller = program
            ? new StubCaller
            {
                Key = new EfApiKey
                {
                    Name = "hatch",
                    Hash = [1],
                    Prefix = "hatch_ak_x",
                    Scopes = [ApiKeyScopes.Hatch],
                    CreatedAt = Now,
                },
            }
            : new StubCaller { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };

        if (!program)
        {
            actors.Principal = nathan;
            actors.Me = nathan;
        }

        return new Harness
        {
            Projects = new ProjectsController(db, time, caller, actors, new ProjectAccess(db, actors), NullLogger<ProjectsController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            },
            Db = db,
            Time = time,
            ProjectId = project.Id,
            Actors = actors,
            Nathan = nathan,
        };
    }

    private static Task<ActionResult<ProjectDto>> PutLogo(
        ProjectsController controller, int id, byte[] bytes, string declaredContentType = "application/octet-stream")
    {
        controller.ControllerContext.HttpContext.Request.Body = new MemoryStream(bytes);
        controller.ControllerContext.HttpContext.Request.ContentType = declaredContentType;
        return controller.PutLogo(id, CancellationToken.None);
    }

    /// <summary>Whoever is holding the phone: a person, or the key an agent carries.</summary>
    private sealed class StubCaller : ICallerIdentity
    {
        public EfPerson? Person { get; init; }

        public EfApiKey? Key { get; init; }

        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult(Person?.Id);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(Person);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult(Key);

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult<Actor?>(null);

        public Task<bool> IsProgramAsync(CancellationToken ct) => Task.FromResult(Key is not null);

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? CallerIdentity.Unattributed);
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {Reason(result.Result)}");

    /// <summary>The plain-text reason on a refusal - what the UI puts on screen.</summary>
    private static string Reason(IActionResult? result) => result switch
    {
        ObjectResult o => $"{o.StatusCode}: {o.Value}",
        StatusCodeResult s => s.StatusCode.ToString(),
        null => "no result",
        _ => result.GetType().Name,
    };
}

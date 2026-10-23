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

    /// <summary>A playbook naming the project is a reason to keep it around, the same as an issue - see <see cref="DeletingAProject_TakesItsBindingsWithIt"/> above for what is not.</summary>
    [Fact]
    public async Task DeletingAProjectAPlaybookNames_IsRefused()
    {
        var h = await NewAsync();
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
        h.Db.AddRange(todo, doing);
        await h.Db.SaveChangesAsync();
        h.Db.Add(new EfHatchPlaybook
        {
            FromStatusId = todo.Id,
            ToStatusId = doing.Id,
            Types = "",
            Shape = EfHatchPlaybook.AnyShape,
            Prompt = "do it",
            Model = "sonnet",
            Effort = "high",
            ProjectId = h.ProjectId,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await h.Db.SaveChangesAsync();

        var result = await h.Projects.DeleteProject(h.ProjectId, default);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(await h.Db.Projects.FindAsync(h.ProjectId));
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

    // ---- Landing ----

    [Fact]
    public async Task AnOwnerSettingLandingAndChecksRequired_RoundTripsThroughGetProjects()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        var written = Value(await h.Projects.PutLanding(created.Id, new ProjectLandingWriteRequest(Landings.Trunk, false), default));
        Assert.Equal(Landings.Trunk, written.Landing);
        Assert.False(written.ChecksRequired);

        var read = Value(await h.Projects.GetProjects(default)).Single(p => p.Id == created.Id);
        Assert.Equal(Landings.Trunk, read.Landing);
        Assert.False(read.ChecksRequired);
    }

    [Fact]
    public async Task ANonOwnerApprover_Is403OnPutLandingNamingTheProjectKey()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        var approver = h.Actors.AddPerson("Alice");
        await h.Projects.PutMember(created.Id, approver.Id, new ProjectMemberWriteRequest(ProjectMemberRole.Approver), default);
        h.Actors.Me = approver;

        var result = await h.Projects.PutLanding(created.Id, new ProjectLandingWriteRequest(Landings.Trunk, false), default);

        Assert.Contains("TST", Reason(result.Result));
        Assert.Equal(Landings.PullRequest, (await h.Db.Projects.FindAsync(created.Id))!.Landing);
    }

    [Fact]
    public async Task AKey_Is403OnPutLanding()
    {
        var h = await NewAsync(program: true);

        var result = await h.Projects.PutLanding(h.ProjectId, new ProjectLandingWriteRequest(Landings.Trunk, false), default);

        Assert.Equal(403, ((ObjectResult)result.Result!).StatusCode);
    }

    [Fact]
    public async Task AnUnknownLandingValue_IsRefusedAndWritesNothing()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        var result = await h.Projects.PutLanding(created.Id, new ProjectLandingWriteRequest("carrier-pigeon", false), default);

        Assert.Contains("landing", Reason(result.Result));
        var project = await h.Db.Projects.FindAsync(created.Id);
        Assert.Equal(Landings.PullRequest, project!.Landing);
        Assert.True(project.ChecksRequired);
    }

    [Fact]
    public async Task ResendingTheLandingAProjectAlreadyHolds_WritesNothing()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await h.Projects.PutLanding(created.Id, new ProjectLandingWriteRequest(Landings.Trunk, false), default);
        var countBefore = await h.Db.Projects.CountAsync();

        var result = Value(await h.Projects.PutLanding(created.Id, new ProjectLandingWriteRequest(Landings.Trunk, false), default));

        Assert.Equal(countBefore, await h.Db.Projects.CountAsync());
        Assert.Equal(Landings.Trunk, result.Landing);
        Assert.False(result.ChecksRequired);
    }

    [Fact]
    public async Task PatchProject_LeavesLandingAndChecksRequiredAlone()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await h.Projects.PutLanding(created.Id, new ProjectLandingWriteRequest(Landings.Trunk, false), default);

        var patched = Value(await h.Projects.PatchProject(created.Id, new ProjectPatchRequest("Renamed"), default));

        Assert.Equal(Landings.Trunk, patched.Landing);
        Assert.False(patched.ChecksRequired);
    }

    [Fact]
    public async Task AProject_StartsWithOnePullRequestOff()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        Assert.False(created.OnePullRequest);
        Assert.False(Value(await h.Projects.GetProjects(default)).Single(p => p.Id == created.Id).OnePullRequest);
    }

    [Fact]
    public async Task AnOwnerSettingOnePullRequest_RoundTripsAndLeavesLandingAlone()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        var written = Value(await h.Projects.PutLanding(created.Id, new ProjectLandingWriteRequest(null, null, true), default));

        Assert.True(written.OnePullRequest);
        Assert.Equal(Landings.PullRequest, written.Landing);
        Assert.True(written.ChecksRequired);
        Assert.True(Value(await h.Projects.GetProjects(default)).Single(p => p.Id == created.Id).OnePullRequest);
    }

    [Fact]
    public async Task OmittingOnePullRequest_LeavesItWhereItWas()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await h.Projects.PutLanding(created.Id, new ProjectLandingWriteRequest(null, null, true), default);

        var written = Value(await h.Projects.PutLanding(created.Id, new ProjectLandingWriteRequest(Landings.Trunk, false), default));

        Assert.True(written.OnePullRequest);
    }

    [Fact]
    public async Task OnePullRequest_CanBeTurnedBackOff()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await h.Projects.PutLanding(created.Id, new ProjectLandingWriteRequest(null, null, true), default);

        var written = Value(await h.Projects.PutLanding(created.Id, new ProjectLandingWriteRequest(null, null, false), default));

        Assert.False(written.OnePullRequest);
    }

    [Fact]
    public async Task ANonOwnerApprover_CannotSetOnePullRequest()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        var approver = h.Actors.AddPerson("Alice");
        await h.Projects.PutMember(created.Id, approver.Id, new ProjectMemberWriteRequest(ProjectMemberRole.Approver), default);
        h.Actors.Me = approver;

        var result = await h.Projects.PutLanding(created.Id, new ProjectLandingWriteRequest(null, null, true), default);

        Assert.Equal(403, ((ObjectResult)result.Result!).StatusCode);
        Assert.False((await h.Db.Projects.FindAsync(created.Id))!.OnePullRequest);
    }

    [Fact]
    public async Task AKey_CannotSetOnePullRequest()
    {
        var h = await NewAsync(program: true);

        var result = await h.Projects.PutLanding(h.ProjectId, new ProjectLandingWriteRequest(null, null, true), default);

        Assert.Equal(403, ((ObjectResult)result.Result!).StatusCode);
        Assert.False((await h.Db.Projects.FindAsync(h.ProjectId))!.OnePullRequest);
    }

    // ---- Playbooks source ----

    [Fact]
    public async Task AKey_Is403OnPutPlaybooksSource()
    {
        var h = await NewAsync(program: true);

        var result = await h.Projects.PutPlaybooksSource(h.ProjectId, new ProjectRepoSourcedWriteRequest(true), default);

        Assert.Equal(403, ((ObjectResult)result.Result!).StatusCode);
    }

    [Fact]
    public async Task ANonOwnerApprover_Is403OnPutPlaybooksSourceNamingTheProjectKey()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        var approver = h.Actors.AddPerson("Alice");
        await h.Projects.PutMember(created.Id, approver.Id, new ProjectMemberWriteRequest(ProjectMemberRole.Approver), default);
        h.Actors.Me = approver;

        var result = await h.Projects.PutPlaybooksSource(created.Id, new ProjectRepoSourcedWriteRequest(true), default);

        Assert.Contains("TST", Reason(result.Result));
        Assert.False((await h.Db.Projects.FindAsync(created.Id))!.RepoSourced);
    }

    [Fact]
    public async Task TurningOnWithNoRepositoryBound_IsRefusedNamingTheProjectsPage()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        var result = await h.Projects.PutPlaybooksSource(created.Id, new ProjectRepoSourcedWriteRequest(true), default);

        Assert.Contains("Projects page", Reason(result.Result));
        Assert.False((await h.Db.Projects.FindAsync(created.Id))!.RepoSourced);
    }

    [Fact]
    public async Task TurningOnWithOnePlaybookRowNamingIt_IsRefusedInTheSingular()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await h.Projects.PutRepositories(
            created.Id, [new ProjectRepositoryWriteRequest("https://example.com/owner/repo.git", null)], default);
        await AddPlaybookRowsAsync(h, created.Id, 1);

        var result = await h.Projects.PutPlaybooksSource(created.Id, new ProjectRepoSourcedWriteRequest(true), default);

        var reason = Reason(result.Result);
        Assert.Contains("1 playbook row", reason);
        Assert.Contains("still names this project", reason);
        Assert.Contains("delete it or hand it", reason);
        Assert.False((await h.Db.Projects.FindAsync(created.Id))!.RepoSourced);
    }

    [Fact]
    public async Task TurningOnWithTwoPlaybookRowsNamingIt_IsRefusedInThePlural()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await h.Projects.PutRepositories(
            created.Id, [new ProjectRepositoryWriteRequest("https://example.com/owner/repo.git", null)], default);
        await AddPlaybookRowsAsync(h, created.Id, 2);

        var result = await h.Projects.PutPlaybooksSource(created.Id, new ProjectRepoSourcedWriteRequest(true), default);

        var reason = Reason(result.Result);
        Assert.Contains("2 playbook rows", reason);
        Assert.Contains("still name this project", reason);
        Assert.Contains("delete them or hand them", reason);
        Assert.False((await h.Db.Projects.FindAsync(created.Id))!.RepoSourced);
    }

    [Fact]
    public async Task TurningOnWithARepositoryBoundAndNoPlaybookRows_Succeeds()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await h.Projects.PutRepositories(
            created.Id, [new ProjectRepositoryWriteRequest("https://example.com/owner/repo.git", null)], default);

        var written = Value(await h.Projects.PutPlaybooksSource(created.Id, new ProjectRepoSourcedWriteRequest(true), default));

        Assert.True(written.RepoSourced);
        Assert.Equal(h.Nathan.Name, written.RepoSourcedBy);
        Assert.Equal(Now, written.RepoSourcedAt);
    }

    [Fact]
    public async Task TurningOff_SucceedsAndLeavesEveryTableRowExactlyAsItWas()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await h.Projects.PutRepositories(
            created.Id, [new ProjectRepositoryWriteRequest("https://example.com/owner/repo.git", null)], default);
        await h.Projects.PutPlaybooksSource(created.Id, new ProjectRepoSourcedWriteRequest(true), default);
        var repositoryId = (await h.Db.ProjectRepositories.SingleAsync(r => r.ProjectId == created.Id)).Id;
        var rowCountBefore = await h.Db.Projects.CountAsync();

        var written = Value(await h.Projects.PutPlaybooksSource(created.Id, new ProjectRepoSourcedWriteRequest(false), default));

        Assert.False(written.RepoSourced);
        Assert.Equal(h.Nathan.Name, written.RepoSourcedBy);
        Assert.Equal(Now, written.RepoSourcedAt);
        Assert.Equal(rowCountBefore, await h.Db.Projects.CountAsync());
        Assert.Equal(repositoryId, (await h.Db.ProjectRepositories.SingleAsync(r => r.ProjectId == created.Id)).Id);
    }

    [Fact]
    public async Task ProjectDto_RoundTripsRepoSourcedFieldsThroughGetProjects()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        Assert.False(created.RepoSourced);
        Assert.Null(created.RepoSourcedBy);
        Assert.Null(created.RepoSourcedAt);

        await h.Projects.PutRepositories(
            created.Id, [new ProjectRepositoryWriteRequest("https://example.com/owner/repo.git", null)], default);
        await h.Projects.PutPlaybooksSource(created.Id, new ProjectRepoSourcedWriteRequest(true), default);

        var read = Value(await h.Projects.GetProjects(default)).Single(p => p.Id == created.Id);
        Assert.True(read.RepoSourced);
        Assert.Equal(h.Nathan.Name, read.RepoSourcedBy);
        Assert.Equal(Now, read.RepoSourcedAt);
    }

    // ---- Binds tracker ----

    [Fact]
    public async Task AKey_Is403OnPutBindsTracker()
    {
        var h = await NewAsync(program: true);

        var result = await h.Projects.PutBindsTracker(h.ProjectId, new ProjectBindsTrackerWriteRequest(true), default);

        Assert.Equal(403, ((ObjectResult)result.Result!).StatusCode);
    }

    [Fact]
    public async Task ANonOwnerApprover_Is403OnPutBindsTrackerNamingTheProjectKey()
    {
        var h = await NewAsync();
        var created = await NewBoundProjectAsync(h, "TST");
        var approver = h.Actors.AddPerson("Alice");
        await h.Projects.PutMember(created.Id, approver.Id, new ProjectMemberWriteRequest(ProjectMemberRole.Approver), default);
        h.Actors.Me = approver;

        var result = await h.Projects.PutBindsTracker(created.Id, new ProjectBindsTrackerWriteRequest(true), default);

        Assert.Contains("403", Reason(result.Result));
        Assert.Contains("TST", Reason(result.Result));
        Assert.False((await h.Db.Projects.FindAsync(created.Id))!.BindsTracker);
    }

    [Fact]
    public async Task TurningOnWithNoRepositoryBound_IsRefusedNamingTheProjectKey()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        var result = await h.Projects.PutBindsTracker(created.Id, new ProjectBindsTrackerWriteRequest(true), default);

        Assert.Contains("400", Reason(result.Result));
        Assert.Contains("TST binds no repository", Reason(result.Result));
        Assert.False((await h.Db.Projects.FindAsync(created.Id))!.BindsTracker);
    }

    [Fact]
    public async Task TurningOn_SucceedsRecordingWhoAndWhen_AndClearsNothingWhenNoProjectHadIt()
    {
        var h = await NewAsync();
        var created = await NewBoundProjectAsync(h, "TST");

        var written = Value(await h.Projects.PutBindsTracker(created.Id, new ProjectBindsTrackerWriteRequest(true), default));

        Assert.True(written.Project.BindsTracker);
        Assert.Equal(h.Nathan.Name, written.Project.BindsTrackerBy);
        Assert.Equal(Now, written.Project.BindsTrackerAt);
        Assert.Null(written.ClearedKey);
    }

    [Fact]
    public async Task ASecondProjectTurningOn_TakesTheFlagFromTheFirst_AndNamesItsKey()
    {
        var h = await NewAsync();
        var first = await NewBoundProjectAsync(h, "ONE");
        var second = await NewBoundProjectAsync(h, "TWO");
        await h.Projects.PutBindsTracker(first.Id, new ProjectBindsTrackerWriteRequest(true), default);
        h.Time.Advance(TimeSpan.FromHours(1));

        var written = Value(await h.Projects.PutBindsTracker(second.Id, new ProjectBindsTrackerWriteRequest(true), default));

        Assert.True(written.Project.BindsTracker);
        Assert.Equal("ONE", written.ClearedKey);
        var rows = await h.Db.Projects.ToListAsync();
        Assert.Equal(["TWO"], rows.Where(p => p.BindsTracker).Select(p => p.Key));
        var cleared = rows.Single(p => p.Key == "ONE");
        Assert.Equal(h.Nathan.Name, cleared.BindsTrackerBy);
        Assert.Equal(Now.AddHours(1), cleared.BindsTrackerAt);
    }

    [Fact]
    public async Task TurningOff_ClearsOnlyThatProject()
    {
        var h = await NewAsync();
        var first = await NewBoundProjectAsync(h, "ONE");
        var second = await NewBoundProjectAsync(h, "TWO");
        await h.Projects.PutBindsTracker(first.Id, new ProjectBindsTrackerWriteRequest(true), default);

        var written = Value(await h.Projects.PutBindsTracker(first.Id, new ProjectBindsTrackerWriteRequest(false), default));

        Assert.False(written.Project.BindsTracker);
        Assert.Null(written.ClearedKey);
        Assert.Empty(await h.Db.Projects.Where(p => p.BindsTracker).ToListAsync());
        Assert.Null((await h.Db.Projects.FindAsync(second.Id))!.BindsTrackerBy);
    }

    [Fact]
    public async Task TurningOffAProjectThatDoesNotHaveIt_LeavesTheHolderAlone()
    {
        var h = await NewAsync();
        var first = await NewBoundProjectAsync(h, "ONE");
        var second = await NewBoundProjectAsync(h, "TWO");
        await h.Projects.PutBindsTracker(first.Id, new ProjectBindsTrackerWriteRequest(true), default);

        var written = Value(await h.Projects.PutBindsTracker(second.Id, new ProjectBindsTrackerWriteRequest(false), default));

        Assert.Null(written.ClearedKey);
        Assert.Null(written.Project.BindsTrackerAt);
        Assert.True((await h.Db.Projects.FindAsync(first.Id))!.BindsTracker);
    }

    [Fact]
    public async Task TurningOnAProjectThatAlreadyHasIt_ChangesNothing()
    {
        var h = await NewAsync();
        var created = await NewBoundProjectAsync(h, "TST");
        await h.Projects.PutBindsTracker(created.Id, new ProjectBindsTrackerWriteRequest(true), default);
        h.Time.Advance(TimeSpan.FromHours(1));

        var written = Value(await h.Projects.PutBindsTracker(created.Id, new ProjectBindsTrackerWriteRequest(true), default));

        Assert.True(written.Project.BindsTracker);
        Assert.Null(written.ClearedKey);
        Assert.Equal(Now, written.Project.BindsTrackerAt);
    }

    [Fact]
    public async Task GetProjects_CarriesTheFlagOnEveryProject_AndExactlyOneReadsTrue()
    {
        var h = await NewAsync();
        var first = await NewBoundProjectAsync(h, "ONE");
        await NewBoundProjectAsync(h, "TWO");
        await h.Projects.PutBindsTracker(first.Id, new ProjectBindsTrackerWriteRequest(true), default);

        var read = Value(await h.Projects.GetProjects(default));

        Assert.Equal(3, read.Count);
        Assert.Equal(["ONE"], read.Where(p => p.BindsTracker).Select(p => p.Key));
        Assert.Equal(h.Nathan.Name, read.Single(p => p.Key == "ONE").BindsTrackerBy);
    }

    [Fact]
    public async Task APatchProject_LeavesTheFlagUntouched()
    {
        var h = await NewAsync();
        var created = await NewBoundProjectAsync(h, "TST");
        await h.Projects.PutBindsTracker(created.Id, new ProjectBindsTrackerWriteRequest(true), default);

        var patched = Value(await h.Projects.PatchProject(created.Id, new ProjectPatchRequest("Renamed"), default));

        Assert.Equal("Renamed", patched.Name);
        Assert.True(patched.BindsTracker);
    }

    private static async Task<ProjectDto> NewBoundProjectAsync(Harness h, string key)
    {
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest(key, key), default));
        await h.Projects.PutRepositories(
            created.Id, [new ProjectRepositoryWriteRequest($"https://example.com/owner/{key.ToLowerInvariant()}.git", null)], default);
        return created;
    }

    // ---- Reflection ----

    [Fact]
    public async Task AnOwnerSettingACadenceWithAReflectionPlaybookRow_Succeeds()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await AddPlaybookRowsAsync(h, created.Id, 1);

        var written = Value(await h.Projects.PutReflection(created.Id, new ProjectReflectionWriteRequest(7), default));

        Assert.Equal(7, written.ReflectionCadenceDays);
        Assert.Null(written.ReflectionLastPeriodStart);
        Assert.Equal(Now, written.ReflectionNextDueAt);
    }

    [Fact]
    public async Task ANonOwnerApprover_Is403OnPutReflectionNamingTheProjectKey()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await AddPlaybookRowsAsync(h, created.Id, 1);
        var approver = h.Actors.AddPerson("Alice");
        await h.Projects.PutMember(created.Id, approver.Id, new ProjectMemberWriteRequest(ProjectMemberRole.Approver), default);
        h.Actors.Me = approver;

        var result = await h.Projects.PutReflection(created.Id, new ProjectReflectionWriteRequest(7), default);

        Assert.Contains("TST", Reason(result.Result));
        Assert.Null((await h.Db.Projects.FindAsync(created.Id))!.ReflectionCadenceDays);
    }

    [Fact]
    public async Task ACadenceWithNoReflectionPlaybookRow_IsRefusedNamingThePlaybooksPage()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        var result = await h.Projects.PutReflection(created.Id, new ProjectReflectionWriteRequest(7), default);

        var reason = Reason(result.Result);
        Assert.Contains("TST has no reflection playbook row", reason);
        Assert.Contains("Playbooks page", reason);
        Assert.Null((await h.Db.Projects.FindAsync(created.Id))!.ReflectionCadenceDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ANonPositiveCadence_IsRefused(int cadenceDays)
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await AddPlaybookRowsAsync(h, created.Id, 1);

        var result = await h.Projects.PutReflection(created.Id, new ProjectReflectionWriteRequest(cadenceDays), default);

        Assert.Contains("a positive number of days", Reason(result.Result));
        Assert.Null((await h.Db.Projects.FindAsync(created.Id))!.ReflectionCadenceDays);
    }

    [Fact]
    public async Task TurningItOff_NeverTouchesLastPeriodStart()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await AddPlaybookRowsAsync(h, created.Id, 1);
        await h.Projects.PutReflection(created.Id, new ProjectReflectionWriteRequest(7), default);
        var project = (await h.Db.Projects.FindAsync(created.Id))!;
        project.ReflectionLastPeriodStart = Now;
        await h.Db.SaveChangesAsync();

        var written = Value(await h.Projects.PutReflection(created.Id, new ProjectReflectionWriteRequest(null), default));

        Assert.Null(written.ReflectionCadenceDays);
        Assert.Equal(Now, (await h.Db.Projects.FindAsync(created.Id))!.ReflectionLastPeriodStart);
    }

    [Fact]
    public async Task PatchProject_LeavesReflectionFieldsAlone()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await AddPlaybookRowsAsync(h, created.Id, 1);
        await h.Projects.PutReflection(created.Id, new ProjectReflectionWriteRequest(7), default);

        var patched = Value(await h.Projects.PatchProject(created.Id, new ProjectPatchRequest("Renamed"), default));

        Assert.Equal(7, patched.ReflectionCadenceDays);
        Assert.Null(patched.ReflectionLastPeriodStart);
    }

    [Fact]
    public async Task ProjectDto_RoundTripsReflectionNextDueAtThroughGetProjects()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await AddPlaybookRowsAsync(h, created.Id, 1);
        await h.Projects.PutReflection(created.Id, new ProjectReflectionWriteRequest(7), default);

        var read = Value(await h.Projects.GetProjects(default)).Single(p => p.Id == created.Id);

        Assert.Equal(7, read.ReflectionCadenceDays);
        Assert.Equal(Now, read.ReflectionNextDueAt);
    }

    // ---- Reflect now ----

    [Fact]
    public async Task AnOwnersPress_FilesOneReflectionWithA30DayWindowByDefault()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        var (todo, _) = await AddPlaybookRowsAsync(h, created.Id, 1);

        var filed = Value(await h.Projects.PostReflect(created.Id, default));

        Assert.Equal("reflection", filed.Type);
        Assert.Equal(todo.Id, filed.StatusId);
        Assert.Contains(Now.AddDays(-30).ToString("yyyy-MM-dd"), filed.Description);
        Assert.Contains(Now.ToString("yyyy-MM-dd"), filed.Description);
        IssueKey.TryParse(filed.Key, out var projectKey, out var number);
        var filedId = (await h.Db.Issues.WithKey(projectKey, number).SingleAsync()).Id;
        Assert.Equal(filedId, (await h.Db.Projects.FindAsync(created.Id))!.ReflectNowIssueId);
    }

    [Fact]
    public async Task ACadenceOf7_FilesA7DayWindow()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await AddPlaybookRowsAsync(h, created.Id, 1);
        await h.Projects.PutReflection(created.Id, new ProjectReflectionWriteRequest(7), default);

        var filed = Value(await h.Projects.PostReflect(created.Id, default));

        Assert.Contains(Now.AddDays(-7).ToString("yyyy-MM-dd"), filed.Description);
    }

    [Fact]
    public async Task ASecondPressWhileItStands_ReturnsTheSameIssueAndFilesNothing()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await AddPlaybookRowsAsync(h, created.Id, 1);

        var first = Value(await h.Projects.PostReflect(created.Id, default));
        var second = Value(await h.Projects.PostReflect(created.Id, default));

        Assert.Equal(first.Key, second.Key);
        Assert.Equal(1, await h.Db.Issues.CountAsync(i => i.ProjectId == created.Id && i.Type == "reflection"));
    }

    [Fact]
    public async Task OnceThatIssueIsTerminal_APressFilesASecondDifferentOne()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await AddPlaybookRowsAsync(h, created.Id, 1);
        var first = Value(await h.Projects.PostReflect(created.Id, default));

        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        h.Db.Add(done);
        await h.Db.SaveChangesAsync();
        IssueKey.TryParse(first.Key, out var projectKey, out var number);
        var firstIssue = await h.Db.Issues.WithKey(projectKey, number).SingleAsync();
        firstIssue.StatusId = done.Id;
        await h.Db.SaveChangesAsync();

        var second = Value(await h.Projects.PostReflect(created.Id, default));

        Assert.NotEqual(first.Key, second.Key);
        Assert.Equal(2, await h.Db.Issues.CountAsync(i => i.ProjectId == created.Id && i.Type == "reflection"));
    }

    [Fact]
    public async Task NoCoveringPlaybookRow_Is400NamingThePlaybooksPageAndFilesNothing()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        var result = await h.Projects.PostReflect(created.Id, default);

        var reason = Reason(result.Result);
        Assert.Contains("has no reflection playbook row", reason);
        Assert.Contains("Playbooks page", reason);
        Assert.Equal(0, await h.Db.Issues.CountAsync(i => i.ProjectId == created.Id));
    }

    [Fact]
    public async Task ANonOwnerApprover_Is403OnPostReflectNamingTheProjectKey()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await AddPlaybookRowsAsync(h, created.Id, 1);
        var approver = h.Actors.AddPerson("Alice");
        await h.Projects.PutMember(created.Id, approver.Id, new ProjectMemberWriteRequest(ProjectMemberRole.Approver), default);
        h.Actors.Me = approver;

        var result = await h.Projects.PostReflect(created.Id, default);

        Assert.Contains("TST", Reason(result.Result));
        Assert.Equal(0, await h.Db.Issues.CountAsync(i => i.ProjectId == created.Id));
    }

    [Fact]
    public async Task AKey_Is403OnPostReflect()
    {
        var h = await NewAsync(program: true);
        await AddPlaybookRowsAsync(h, h.ProjectId, 1);

        var result = await h.Projects.PostReflect(h.ProjectId, default);

        Assert.Equal(403, ((ObjectResult)result.Result!).StatusCode);
    }

    [Fact]
    public async Task AnUnknownProject_Is404OnPostReflect()
    {
        var h = await NewAsync();

        var result = await h.Projects.PostReflect(h.ProjectId + 999, default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task AnIssueTheJobFiled_DoesNotSatisfyAPress()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        var (todo, _) = await AddPlaybookRowsAsync(h, created.Id, 1);
        var project = (await h.Db.Projects.FindAsync(created.Id))!;

        // Simulating the job: filed directly, never through ReflectNowIssueId.
        h.Db.Issues.Add(new EfHatchIssue
        {
            ProjectId = created.Id,
            Number = project.NextIssueNumber++,
            Type = "reflection",
            Title = "Reflection: job-filed",
            Description = "",
            StatusId = todo.Id,
            Rank = 1024,
            CreatedBy = ReflectionCadenceJob.Actor,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await h.Db.SaveChangesAsync();

        var filed = Value(await h.Projects.PostReflect(created.Id, default));

        Assert.Equal(2, await h.Db.Issues.CountAsync(i => i.ProjectId == created.Id && i.Type == "reflection"));
        var afterPress = (await h.Db.Projects.FindAsync(created.Id))!;
        IssueKey.TryParse(filed.Key, out var projectKey, out var number);
        var filedIssue = await h.Db.Issues.WithKey(projectKey, number).SingleAsync();
        Assert.Equal(filedIssue.Id, afterPress.ReflectNowIssueId);
    }

    // ---- Runner tags ----

    [Fact]
    public async Task AnOwnerSettingRunnerTags_StoresCaseFoldedAndDeduplicatedInTheOrderSent()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        var written = Value(await h.Projects.PutRunnerTags(created.Id, new ProjectRunnerTagsWriteRequest(["GPU", "gpu", "Windows"]), default));

        Assert.Equal(["gpu", "windows"], written.RunnerTags);
    }

    [Fact]
    public async Task ProjectDto_RoundTripsRunnerTagsThroughGetProjects()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await h.Projects.PutRunnerTags(created.Id, new ProjectRunnerTagsWriteRequest(["gpu", "windows"]), default);

        var read = Value(await h.Projects.GetProjects(default)).Single(p => p.Id == created.Id);

        Assert.Equal(["gpu", "windows"], read.RunnerTags);
    }

    [Fact]
    public async Task ANonOwnerApprover_Is403OnPutRunnerTagsNamingTheProjectKey()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        var approver = h.Actors.AddPerson("Alice");
        await h.Projects.PutMember(created.Id, approver.Id, new ProjectMemberWriteRequest(ProjectMemberRole.Approver), default);
        h.Actors.Me = approver;

        var result = await h.Projects.PutRunnerTags(created.Id, new ProjectRunnerTagsWriteRequest(["gpu"]), default);

        Assert.Contains("TST", Reason(result.Result));
        Assert.Equal("[]", (await h.Db.Projects.FindAsync(created.Id))!.RunnerTags);
    }

    [Fact]
    public async Task AKey_Is403OnPutRunnerTags()
    {
        var h = await NewAsync(program: true);

        var result = await h.Projects.PutRunnerTags(h.ProjectId, new ProjectRunnerTagsWriteRequest(["gpu"]), default);

        Assert.Equal(403, ((ObjectResult)result.Result!).StatusCode);
        Assert.Contains("operator's to set", Reason(result.Result));
        Assert.Equal("[]", (await h.Db.Projects.FindAsync(h.ProjectId))!.RunnerTags);
    }

    [Fact]
    public async Task ATagBreakingTheRule_IsRefusedAndWritesNothing()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));

        var result = await h.Projects.PutRunnerTags(created.Id, new ProjectRunnerTagsWriteRequest(["needs gpu"]), default);

        Assert.Contains("no space or comma", Reason(result.Result));
        Assert.Equal("[]", (await h.Db.Projects.FindAsync(created.Id))!.RunnerTags);
    }

    [Fact]
    public async Task TooManyRunnerTags_IsRefused400AndWritesNothing()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        var tags = Enumerable.Range(1, RunnerTags.MaxCount + 1).Select(i => $"tag{i}").ToArray();

        var result = await h.Projects.PutRunnerTags(created.Id, new ProjectRunnerTagsWriteRequest(tags), default);

        Assert.Contains("at most 8", Reason(result.Result));
        Assert.Equal("[]", (await h.Db.Projects.FindAsync(created.Id))!.RunnerTags);
    }

    [Fact]
    public async Task ResendingTheSameRunnerTags_WritesNothing()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await h.Projects.PutRunnerTags(created.Id, new ProjectRunnerTagsWriteRequest(["gpu", "windows"]), default);
        // The same tags, spaced the way RunnerTagColumn.Write never writes them: a rewrite
        // would replace this string, a no-op leaves it.
        const string Sentinel = "[\"gpu\", \"windows\"]";
        (await h.Db.Projects.FindAsync(created.Id))!.RunnerTags = Sentinel;
        await h.Db.SaveChangesAsync();

        var result = Value(await h.Projects.PutRunnerTags(created.Id, new ProjectRunnerTagsWriteRequest(["gpu", "windows"]), default));

        Assert.Equal(Sentinel, (await h.Db.Projects.FindAsync(created.Id))!.RunnerTags);
        Assert.Equal(["gpu", "windows"], result.RunnerTags);
    }

    [Fact]
    public async Task APutOfAnEmptyList_ClearsTheStoredTags()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await h.Projects.PutRunnerTags(created.Id, new ProjectRunnerTagsWriteRequest(["gpu"]), default);

        var written = Value(await h.Projects.PutRunnerTags(created.Id, new ProjectRunnerTagsWriteRequest([]), default));

        Assert.Empty(written.RunnerTags);
        var read = Value(await h.Projects.GetProjects(default)).Single(p => p.Id == created.Id);
        Assert.Empty(read.RunnerTags);
    }

    [Fact]
    public async Task PatchProject_LeavesRunnerTagsAlone()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        await h.Projects.PutRunnerTags(created.Id, new ProjectRunnerTagsWriteRequest(["gpu"]), default);

        var patched = Value(await h.Projects.PatchProject(created.Id, new ProjectPatchRequest("Renamed"), default));

        Assert.Equal(["gpu"], patched.RunnerTags);
    }

    private static async Task<(EfHatchStatus Todo, EfHatchStatus Doing)> AddPlaybookRowsAsync(Harness h, int projectId, int count)
    {
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
        h.Db.AddRange(todo, doing);
        await h.Db.SaveChangesAsync();

        for (var i = 0; i < count; i++)
        {
            h.Db.Add(new EfHatchPlaybook
            {
                FromStatusId = todo.Id,
                ToStatusId = doing.Id,
                Types = "",
                Shape = EfHatchPlaybook.AnyShape,
                Prompt = "do it",
                Model = "sonnet",
                Effort = "high",
                ProjectId = projectId,
                CreatedAt = Now,
                UpdatedAt = Now,
            });
        }

        await h.Db.SaveChangesAsync();
        return (todo, doing);
    }

    // ---- Auto-heal ----

    [Fact]
    public async Task AProject_StartsWithAutoHealOffAtEmergencyAndExpressAndNoColumn()
    {
        var h = await NewAsync();

        var read = Value(await h.Projects.GetProjects(default)).Single(p => p.Id == h.ProjectId);

        Assert.False(read.AutoHealBugEnabled);
        Assert.Equal(PriorityLevels.EmergencyName, read.AutoHealBugPriority);
        Assert.Equal(PreApprovalLevels.ExpressName, read.AutoHealBugPreApproval);
        Assert.Null(read.AutoHealBugStatusId);
    }

    [Fact]
    public async Task AnOwnerSettingEachAutoHealField_RoundTripsThroughGetProjects()
    {
        var h = await NewAsync();
        var (todo, _) = await BoardAsync(h);

        var written = Value(await h.Projects.PutLanding(h.ProjectId,
            new ProjectLandingWriteRequest(null, null, AutoHealBugEnabled: true, AutoHealBugPriority: PriorityLevels.LowName,
                AutoHealBugPreApproval: PreApprovalLevels.PreApprovedName, AutoHealBugStatusId: todo.Id.ToString()), default));

        Assert.True(written.AutoHealBugEnabled);
        Assert.Equal(PriorityLevels.LowName, written.AutoHealBugPriority);
        Assert.Equal(PreApprovalLevels.PreApprovedName, written.AutoHealBugPreApproval);
        Assert.Equal(todo.Id, written.AutoHealBugStatusId);

        var read = Value(await h.Projects.GetProjects(default)).Single(p => p.Id == h.ProjectId);
        Assert.True(read.AutoHealBugEnabled);
        Assert.Equal(PriorityLevels.LowName, read.AutoHealBugPriority);
        Assert.Equal(PreApprovalLevels.PreApprovedName, read.AutoHealBugPreApproval);
        Assert.Equal(todo.Id, read.AutoHealBugStatusId);
    }

    [Fact]
    public async Task AnOwnerClearingAutoHealStatusId_ReturnsItToNull()
    {
        var h = await NewAsync();
        var (todo, _) = await BoardAsync(h);
        await h.Projects.PutLanding(h.ProjectId, new ProjectLandingWriteRequest(null, null, AutoHealBugStatusId: todo.Id.ToString()), default);

        var written = Value(await h.Projects.PutLanding(h.ProjectId, new ProjectLandingWriteRequest(null, null, AutoHealBugStatusId: ""), default));

        Assert.Null(written.AutoHealBugStatusId);
    }

    [Fact]
    public async Task ANonOwnerApprover_CannotSetAutoHeal()
    {
        var h = await NewAsync();
        var created = Created(await h.Projects.CreateProject(new ProjectCreateRequest("TST", "Test"), default));
        var approver = h.Actors.AddPerson("Alice");
        await h.Projects.PutMember(created.Id, approver.Id, new ProjectMemberWriteRequest(ProjectMemberRole.Approver), default);
        h.Actors.Me = approver;

        var result = await h.Projects.PutLanding(created.Id, new ProjectLandingWriteRequest(null, null, AutoHealBugEnabled: true), default);

        Assert.Equal(403, ((ObjectResult)result.Result!).StatusCode);
        Assert.False((await h.Db.Projects.FindAsync(created.Id))!.AutoHealBugEnabled);
    }

    [Fact]
    public async Task AnUnknownAutoHealPriorityValue_IsRefusedAndWritesNothing()
    {
        var h = await NewAsync();
        await BoardAsync(h);

        var result = await h.Projects.PutLanding(h.ProjectId, new ProjectLandingWriteRequest(null, null, AutoHealBugPriority: "carrier-pigeon"), default);

        Assert.Contains("priority", Reason(result.Result));
        var project = await h.Db.Projects.FindAsync(h.ProjectId);
        Assert.Equal(PriorityLevels.Emergency, project!.AutoHealBugPriority);
    }

    [Fact]
    public async Task AnUnknownAutoHealPreApprovalValue_IsRefusedAndWritesNothing()
    {
        var h = await NewAsync();
        await BoardAsync(h);

        var result = await h.Projects.PutLanding(h.ProjectId, new ProjectLandingWriteRequest(null, null, AutoHealBugPreApproval: "carrier-pigeon"), default);

        Assert.Contains("pre-approval", Reason(result.Result));
        var project = await h.Db.Projects.FindAsync(h.ProjectId);
        Assert.Equal(PreApprovalLevels.Express, project!.AutoHealBugPreApproval);
    }

    [Fact]
    public async Task AnAutoHealStatusIdNamingNoColumn_IsRefusedAndWritesNothing()
    {
        var h = await NewAsync();
        await BoardAsync(h);

        var result = await h.Projects.PutLanding(h.ProjectId, new ProjectLandingWriteRequest(null, null, AutoHealBugStatusId: "404"), default);

        Assert.Contains("no column", Reason(result.Result));
        var project = await h.Db.Projects.FindAsync(h.ProjectId);
        Assert.Null(project!.AutoHealBugStatusId);
    }

    [Fact]
    public async Task AnAutoHealStatusIdThatIsNotANumber_IsRefused()
    {
        var h = await NewAsync();
        await BoardAsync(h);

        var result = await h.Projects.PutLanding(h.ProjectId, new ProjectLandingWriteRequest(null, null, AutoHealBugStatusId: "carrier-pigeon"), default);

        Assert.Contains("no column", Reason(result.Result));
    }

    [Fact]
    public async Task OmittingAutoHealFields_LeavesThemWhereTheyWere()
    {
        var h = await NewAsync();
        var (todo, _) = await BoardAsync(h);
        await h.Projects.PutLanding(h.ProjectId,
            new ProjectLandingWriteRequest(null, null, AutoHealBugEnabled: true, AutoHealBugPriority: PriorityLevels.LowName,
                AutoHealBugPreApproval: PreApprovalLevels.PreApprovedName, AutoHealBugStatusId: todo.Id.ToString()), default);

        var written = Value(await h.Projects.PutLanding(h.ProjectId, new ProjectLandingWriteRequest(Landings.Trunk, false), default));

        Assert.True(written.AutoHealBugEnabled);
        Assert.Equal(PriorityLevels.LowName, written.AutoHealBugPriority);
        Assert.Equal(PreApprovalLevels.PreApprovedName, written.AutoHealBugPreApproval);
        Assert.Equal(todo.Id, written.AutoHealBugStatusId);
    }

    [Fact]
    public async Task PatchProject_LeavesAutoHealAlone()
    {
        var h = await NewAsync();
        var (todo, _) = await BoardAsync(h);
        await h.Projects.PutLanding(h.ProjectId,
            new ProjectLandingWriteRequest(null, null, AutoHealBugEnabled: true, AutoHealBugStatusId: todo.Id.ToString()), default);

        var patched = Value(await h.Projects.PatchProject(h.ProjectId, new ProjectPatchRequest("Renamed"), default));

        Assert.True(patched.AutoHealBugEnabled);
        Assert.Equal(todo.Id, patched.AutoHealBugStatusId);
    }

    // ---- The project's one-PR default, and stories that have started ----
    //
    // A story with no setting of its own reads the project's default, so
    // changing it would move the story under tasks already begun on one branch.
    // The change fixes those stories at the value they were reading first.

    private static async Task<(EfHatchStatus Todo, EfHatchStatus Doing)> BoardAsync(Harness h)
    {
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        var doing = new EfHatchStatus { Name = "in progress", SortOrder = 20, IsImplementation = true };
        h.Db.AddRange(todo, doing, new EfHatchStatus { Name = "done", SortOrder = 30, IsTerminal = true });

        // The caller owns the project - only an owner writes its default.
        h.Db.ProjectMembers.Add(new EfHatchProjectMember
        {
            ProjectId = h.ProjectId, PersonId = h.Nathan.Id, Role = ProjectMemberRole.Owner, CreatedAt = Now,
        });
        await h.Db.SaveChangesAsync();
        return (todo, doing);
    }

    private static async Task<EfHatchIssue> IssueAsync(
        Harness h, string type, int statusId, long? parentId = null, bool? onePr = null, int? projectId = null)
    {
        var project = projectId ?? h.ProjectId;
        var issue = new EfHatchIssue
        {
            ProjectId = project,
            Number = await h.Db.Issues.CountAsync(i => i.ProjectId == project) + 1,
            Type = type, Title = type, Description = "", StatusId = statusId, Rank = 1024, ParentId = parentId,
            OnePullRequest = onePr, CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
        };
        h.Db.Issues.Add(issue);
        await h.Db.SaveChangesAsync();
        return issue;
    }

    private static async Task SetDefaultAsync(Harness h, bool on) =>
        Value(await h.Projects.PutLanding(h.ProjectId, new ProjectLandingWriteRequest(null, null, on), default));

    [Fact]
    public async Task ChangingTheDefault_FixesAStartedStoryAtTheValueItWasReading()
    {
        var h = await NewAsync();
        var (todo, doing) = await BoardAsync(h);
        await SetDefaultAsync(h, true);
        var story = await IssueAsync(h, "story", doing.Id);
        await IssueAsync(h, "task", doing.Id, story.Id);

        await SetDefaultAsync(h, false);

        h.Db.ChangeTracker.Clear();
        var row = await h.Db.Issues.Include(i => i.Events).SingleAsync(i => i.Id == story.Id);
        Assert.True(row.OnePullRequest);
        var e = Assert.Single(row.Events);
        Assert.Equal(EfHatchIssueEvent.OnePrChanged, e.Kind);
        Assert.Equal("Nathan", e.Actor);
        var payload = System.Text.Json.JsonDocument.Parse(e.Payload!).RootElement;
        Assert.Equal("inherit", payload.GetProperty("from").GetString());
        Assert.Equal("on", payload.GetProperty("to").GetString());
        Assert.False((await h.Db.Projects.SingleAsync(p => p.Id == h.ProjectId)).OnePullRequest);
    }

    [Fact]
    public async Task ChangingTheDefault_FixesTheOtherWayToo()
    {
        var h = await NewAsync();
        var (_, doing) = await BoardAsync(h);
        var story = await IssueAsync(h, "story", doing.Id);
        await IssueAsync(h, "task", doing.Id, story.Id);

        await SetDefaultAsync(h, true);

        h.Db.ChangeTracker.Clear();
        Assert.False((await h.Db.Issues.SingleAsync(i => i.Id == story.Id)).OnePullRequest);
    }

    [Fact]
    public async Task ChangingTheDefault_LeavesAStoryWithNothingStartedToFollowIt()
    {
        var h = await NewAsync();
        var (todo, doing) = await BoardAsync(h);
        var story = await IssueAsync(h, "story", doing.Id);
        await IssueAsync(h, "task", todo.Id, story.Id);
        var childless = await IssueAsync(h, "story", todo.Id);

        await SetDefaultAsync(h, true);

        h.Db.ChangeTracker.Clear();
        Assert.Null((await h.Db.Issues.SingleAsync(i => i.Id == story.Id)).OnePullRequest);
        Assert.Null((await h.Db.Issues.SingleAsync(i => i.Id == childless.Id)).OnePullRequest);
    }

    [Fact]
    public async Task ChangingTheDefault_LeavesAStoryWithASettingOfItsOwn_AsItWas()
    {
        var h = await NewAsync();
        var (_, doing) = await BoardAsync(h);
        var story = await IssueAsync(h, "story", doing.Id, onePr: false);
        await IssueAsync(h, "task", doing.Id, story.Id);

        await SetDefaultAsync(h, true);

        h.Db.ChangeTracker.Clear();
        var row = await h.Db.Issues.Include(i => i.Events).SingleAsync(i => i.Id == story.Id);
        Assert.False(row.OnePullRequest);
        Assert.Empty(row.Events);
    }

    [Fact]
    public async Task ChangingTheDefault_ReachesATaskStartedBelowABug()
    {
        var h = await NewAsync();
        var (todo, doing) = await BoardAsync(h);
        var story = await IssueAsync(h, "story", todo.Id);
        var bug = await IssueAsync(h, "bug", todo.Id, story.Id);
        await IssueAsync(h, "task", doing.Id, bug.Id);

        await SetDefaultAsync(h, true);

        h.Db.ChangeTracker.Clear();
        Assert.False((await h.Db.Issues.SingleAsync(i => i.Id == story.Id)).OnePullRequest);
    }

    [Fact]
    public async Task ChangingTheDefault_LeavesAnotherProjectsStoriesAlone()
    {
        var h = await NewAsync();
        var (_, doing) = await BoardAsync(h);
        var other = new EfHatchProject { Key = "OTH", Name = "Other", CreatedAt = Now };
        h.Db.Add(other);
        await h.Db.SaveChangesAsync();
        var story = await IssueAsync(h, "story", doing.Id, projectId: other.Id);
        await IssueAsync(h, "task", doing.Id, story.Id, projectId: other.Id);

        await SetDefaultAsync(h, true);

        h.Db.ChangeTracker.Clear();
        Assert.Null((await h.Db.Issues.SingleAsync(i => i.Id == story.Id)).OnePullRequest);
    }

    [Fact]
    public async Task ResendingTheDefaultItAlreadyHolds_FixesNothing()
    {
        var h = await NewAsync();
        var (_, doing) = await BoardAsync(h);
        var story = await IssueAsync(h, "story", doing.Id);
        await IssueAsync(h, "task", doing.Id, story.Id);

        await SetDefaultAsync(h, false);

        h.Db.ChangeTracker.Clear();
        Assert.Null((await h.Db.Issues.SingleAsync(i => i.Id == story.Id)).OnePullRequest);
    }

    [Fact]
    public async Task ChangingLanding_NotTheDefault_FixesNothing()
    {
        var h = await NewAsync();
        var (_, doing) = await BoardAsync(h);
        var story = await IssueAsync(h, "story", doing.Id);
        await IssueAsync(h, "task", doing.Id, story.Id);

        Value(await h.Projects.PutLanding(h.ProjectId, new ProjectLandingWriteRequest(Landings.Trunk, null), default));

        h.Db.ChangeTracker.Clear();
        Assert.Null((await h.Db.Issues.SingleAsync(i => i.Id == story.Id)).OnePullRequest);
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

        var ranks = new RankService(db);

        return new Harness
        {
            Projects = new ProjectsController(db, time, caller, actors, new ProjectAccess(db, actors), NullLogger<ProjectsController>.Instance, ranks, TestClaims.With())
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

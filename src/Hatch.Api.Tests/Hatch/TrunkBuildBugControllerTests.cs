using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The button a failing trunk's row offers: what it files, in whose project, and
/// why a second press never gets a second bug.
/// </summary>
public class TrunkBuildBugControllerTests
{
    private const string Canonical = "forge.example/owner/repo";

    [Fact]
    public async Task PressingIt_FilesAnExpeditedBug_InTheBoundProjectsFirstColumn()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api", "CI"]);

        var dto = Value(await h.Bug.PostBug(row.Id, null, default));

        Assert.Equal("bug", dto.Type);
        Assert.Equal("Build failing on main", dto.Title);
        Assert.True(dto.Expedited);
        Assert.False(dto.Express);
        Assert.Contains(Canonical, dto.Description);
        Assert.Contains(row.Sha, dto.Description);
        Assert.Contains("https://forge.example/checks/api", dto.Description);
        Assert.Contains("https://forge.example/checks/CI", dto.Description);

        var inbox = await h.Db.Statuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).FirstAsync();
        Assert.Equal(inbox.Id, dto.StatusId);
    }

    [Fact]
    public async Task PressingIt_AttachesTheBugToTheRow()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);

        var dto = Value(await h.Bug.PostBug(row.Id, null, default));

        var stored = await h.Db.TrunkBuilds.SingleAsync(t => t.Id == row.Id);
        Assert.Equal(dto.Key, IssueKey.Format("AER", (await h.Db.Issues.SingleAsync(i => i.Id == stored.BugIssueId)).Number));
    }

    [Fact]
    public async Task ASecondPress_AnswersTheSameBug_AndFilesNoSecondOne()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);

        var first = Value(await h.Bug.PostBug(row.Id, null, default));
        var second = Value(await h.Bug.PostBug(row.Id, null, default));

        Assert.Equal(first.Key, second.Key);
        Assert.Equal(1, await h.Db.Issues.CountAsync(i => i.Type == "bug"));
    }

    [Fact]
    public async Task ATrunkThatHasSincePassed_IsRefusedWithConflict()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Passed);

        Assert.IsType<ConflictObjectResult>((await h.Bug.PostBug(row.Id, null, default)).Result);
        Assert.Empty(await h.Db.Issues.Where(i => i.Type == "bug").ToListAsync());
    }

    [Fact]
    public async Task NoProjectBindingTheRepository_IsRefusedNamingTheProjectsPage()
    {
        var h = await NewAsync();
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);

        var reason = Assert.IsType<BadRequestObjectResult>((await h.Bug.PostBug(row.Id, null, default)).Result).Value!.ToString();

        Assert.Contains(Canonical, reason);
        Assert.Contains("Projects page", reason);
    }

    [Fact]
    public async Task ARowThatBindsToTwoProjects_FilesInThePrimarysFirst()
    {
        var h = await NewAsync();
        var second = await h.NewProjectAsync("OPS");
        await h.BindRepositoryAsync(second, Canonical, sortOrder: 0);
        await h.BindRepositoryAsync(h.ProjectId, Canonical, sortOrder: 1);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);

        var dto = Value(await h.Bug.PostBug(row.Id, null, default));

        Assert.StartsWith("OPS-", dto.Key);
    }

    [Fact]
    public async Task ARowThatBindsToTwoProjects_NeitherPrimary_FilesInTheLowestId()
    {
        var h = await NewAsync();
        var second = await h.NewProjectAsync("OPS");
        await h.BindRepositoryAsync(second, Canonical, sortOrder: 1);
        await h.BindRepositoryAsync(h.ProjectId, Canonical, sortOrder: 1);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);

        var dto = Value(await h.Bug.PostBug(row.Id, null, default));

        Assert.StartsWith("AER-", dto.Key);
    }

    [Fact]
    public async Task ARowThatIsNotThere_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>((await h.Bug.PostBug(404, null, default)).Result);
    }

    // ---- The body ----

    [Fact]
    public async Task ABody_FilesExpressAtALevel_IntoAChosenColumn()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);
        var doing = await h.AddStatusAsync("doing", sortOrder: 15);

        var dto = Value(await h.Bug.PostBug(row.Id, new TrunkBuildBugRequest(true, "emergency", doing.Id), default));

        Assert.True(dto.Express);
        Assert.Equal(doing.Id, dto.StatusId);
        Assert.Equal(PriorityLevels.EmergencyName, dto.Priority);
    }

    [Fact]
    public async Task APreApprovalLevel_IsFiledAsNamed_ForAPersonWhoMayApprove()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Me = ada;
        h.Db.ProjectMembers.Add(new EfHatchProjectMember
        {
            ProjectId = h.ProjectId, PersonId = ada.Id, Role = ProjectMemberRole.Approver, CreatedAt = Now,
        });
        await h.Db.SaveChangesAsync();

        var dto = Value(await h.Bug.PostBug(row.Id, new TrunkBuildBugRequest(null, null, null, "preapproved"), default));

        Assert.Equal("preapproved", dto.PreApproval);
    }

    [Fact]
    public async Task PreApprovedFromAPersonWhoMayNotApprove_IsRefusedForbidden_AndFilesNothing()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);

        var result = Assert.IsType<ObjectResult>(
            (await h.Bug.PostBug(row.Id, new TrunkBuildBugRequest(null, null, null, "preapproved"), default)).Result);

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Contains("owner or approver", result.Value!.ToString());
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task ThePreApprovalLevel_WinsOverTheOlderExpressBoolean_WhenBothAreSent()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);

        var dto = Value(await h.Bug.PostBug(row.Id, new TrunkBuildBugRequest(true, null, null, "none"), default));

        Assert.Equal("none", dto.PreApproval);
        Assert.False(dto.Express);
    }

    [Fact]
    public async Task TheOlderExpressBoolean_StillFilesExpress_WhenNoLevelIsSent()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);

        var dto = Value(await h.Bug.PostBug(row.Id, new TrunkBuildBugRequest(true, null, null), default));

        Assert.Equal("express", dto.PreApproval);
    }

    [Fact]
    public async Task AnUnknownPreApprovalLevel_IsRefused_InTheWordsTheLevelRouteUses()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);

        var result = Assert.IsType<BadRequestObjectResult>(
            (await h.Bug.PostBug(row.Id, new TrunkBuildBugRequest(null, null, null, "nonsense"), default)).Result);

        Assert.Equal(IssuePreApprovalController.UnknownLevel("nonsense"), result.Value);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task ABody_WithNoStatusId_StillFilesIntoTheLeftmostColumn()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);

        var dto = Value(await h.Bug.PostBug(row.Id, new TrunkBuildBugRequest(true, "emergency", null), default));

        var inbox = await h.Db.Statuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).FirstAsync();
        Assert.Equal(inbox.Id, dto.StatusId);
        Assert.True(dto.Express);
        Assert.Equal(PriorityLevels.EmergencyName, dto.Priority);
    }

    [Fact]
    public async Task ABadPriorityName_IsRefused_AndFilesNothing()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);

        var reason = Assert.IsType<BadRequestObjectResult>(
            (await h.Bug.PostBug(row.Id, new TrunkBuildBugRequest(null, "urgent", null), default)).Result).Value!.ToString();

        Assert.Contains("urgent", reason);
        Assert.Contains("paused, economy, low, normal, expedited or emergency", reason);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AStatusIdNamingNoColumn_IsRefused_AndFilesNothing()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);

        var reason = Assert.IsType<BadRequestObjectResult>(
            (await h.Bug.PostBug(row.Id, new TrunkBuildBugRequest(null, null, 999999), default)).Result).Value!.ToString();

        Assert.Contains("999999", reason);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AStatusIdNamingATerminalColumn_IsRefused_AndFilesNothing()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);
        var done = await h.Db.Statuses.SingleAsync(s => s.Name == "done");

        Assert.IsType<BadRequestObjectResult>(
            (await h.Bug.PostBug(row.Id, new TrunkBuildBugRequest(null, null, done.Id), default)).Result);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AStatusIdNamingADeferredColumn_IsRefused_AndFilesNothing()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);
        var parked = await h.AddStatusAsync("parked", sortOrder: 15, isDeferred: true);

        Assert.IsType<BadRequestObjectResult>(
            (await h.Bug.PostBug(row.Id, new TrunkBuildBugRequest(null, null, parked.Id), default)).Result);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AStatusIdNamingAProtectedColumn_WithNoApprover_IsRefusedForbidden()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);
        var guarded = await h.AddStatusAsync("guarded", sortOrder: 15, isProtected: true);

        var result = Assert.IsType<ObjectResult>((await h.Bug.PostBug(row.Id, new TrunkBuildBugRequest(null, null, guarded.Id), default)).Result);

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Contains("owner or approver", result.Value!.ToString());
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AStatusIdNamingAProtectedColumn_WithAnApprover_Files()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);
        var guarded = await h.AddStatusAsync("guarded", sortOrder: 15, isProtected: true);
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Me = ada;
        h.Db.ProjectMembers.Add(new EfHatchProjectMember
        {
            ProjectId = h.ProjectId, PersonId = ada.Id, Role = ProjectMemberRole.Approver, CreatedAt = Now,
        });
        await h.Db.SaveChangesAsync();

        var dto = Value(await h.Bug.PostBug(row.Id, new TrunkBuildBugRequest(null, null, guarded.Id), default));

        Assert.Equal(guarded.Id, dto.StatusId);
    }

    [Fact]
    public async Task AStatusIdNamingAFullWipColumn_IsRefusedWithConflict_AndFilesNothing()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);
        var full = await h.AddStatusAsync("doing", sortOrder: 15, isWip: true);
        h.Db.WipLimits.Add(new EfHatchWipLimit { Types = EfHatchWipLimit.StoriesAndBugs, Limit = 0 });
        await h.Db.SaveChangesAsync();

        Assert.IsType<ConflictObjectResult>(
            (await h.Bug.PostBug(row.Id, new TrunkBuildBugRequest(null, null, full.Id), default)).Result);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task NoStatusId_NeverConsultsTheWipGate_EvenWhenTheLeftmostColumnIsFull()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);
        var inbox = await h.Db.Statuses.SingleAsync(s => s.Name == "inbox");
        inbox.IsWip = true;
        h.Db.WipLimits.Add(new EfHatchWipLimit { Types = EfHatchWipLimit.StoriesAndBugs, Limit = 0 });
        await h.Db.SaveChangesAsync();

        var dto = Value(await h.Bug.PostBug(row.Id, null, default));

        Assert.Equal(inbox.Id, dto.StatusId);
    }

    // ---- The gate ----

    [Fact]
    public void PostingIt_CarriesNoClassLevelAttribute_SoTheRouteTakesNoKey()
    {
        Assert.Null(typeof(TrunkBuildBugController).GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false).SingleOrDefault());
    }

    [Fact]
    public void PostingIt_AcceptsAKey_AtPersonMinimum()
    {
        var guard = typeof(TrunkBuildBugController)
            .GetMethods()
            .Single(m => m.Name == nameof(TrunkBuildBugController.PostBug))
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        Assert.Equal(PersonRole.User, guard.Minimum);
        Assert.True(guard.AcceptKey);
    }

    // ---- A key's call ----

    [Fact]
    public async Task AKey_WithAutoFilingOn_FilesAtTheProjectsConfiguredValues()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var doing = await h.AddStatusAsync("doing", sortOrder: 15);
        await h.ConfigureAutoHealAsync(true, priority: PriorityLevels.Low, preApproval: PreApprovalLevels.PreApproved, statusId: doing.Id);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);
        h.Caller.Person = null;
        h.Caller.Key = new EfApiKey { Name = "a key", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };

        var dto = Value(await h.Bug.PostBug(row.Id, null, default));

        Assert.Equal(PriorityLevels.LowName, dto.Priority);
        Assert.Equal(PreApprovalLevels.PreApprovedName, dto.PreApproval);
        Assert.Equal(doing.Id, dto.StatusId);
    }

    [Fact]
    public async Task AKey_WithAutoFilingOff_IsRefusedNamingTheProjectsPage()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);
        h.Caller.Person = null;
        h.Caller.Key = new EfApiKey { Name = "a key", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };

        var reason = Assert.IsType<BadRequestObjectResult>((await h.Bug.PostBug(row.Id, null, default)).Result).Value!.ToString();

        Assert.Contains("Projects page", reason);
        Assert.Empty(await h.Db.Issues.Where(i => i.Type == "bug").ToListAsync());
    }

    [Fact]
    public async Task AKey_WithNoProjectBindingTheRepository_IsRefusedNamingTheProjectsPage()
    {
        var h = await NewAsync();
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);
        h.Caller.Person = null;
        h.Caller.Key = new EfApiKey { Name = "a key", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };

        var reason = Assert.IsType<BadRequestObjectResult>((await h.Bug.PostBug(row.Id, null, default)).Result).Value!.ToString();

        Assert.Contains(Canonical, reason);
        Assert.Contains("Projects page", reason);
    }

    [Fact]
    public async Task ASecondKeyCall_AnswersTheSameBug_AndFilesNoSecondOne()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        await h.ConfigureAutoHealAsync(true);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);
        h.Caller.Person = null;
        h.Caller.Key = new EfApiKey { Name = "a key", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };

        var first = Value(await h.Bug.PostBug(row.Id, null, default));
        var second = Value(await h.Bug.PostBug(row.Id, null, default));

        Assert.Equal(first.Key, second.Key);
        Assert.Equal(1, await h.Db.Issues.CountAsync(i => i.Type == "bug"));
    }

    [Fact]
    public async Task APerson_FilesRegardlessOfAutoHealEnabled()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync(h.ProjectId, Canonical);
        var row = await h.TrunkAsync(BuildVerdicts.Failed, failing: ["api"]);

        var dto = Value(await h.Bug.PostBug(row.Id, null, default));

        Assert.True(dto.Expedited);
        Assert.False(dto.Express);
        var inbox = await h.Db.Statuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).FirstAsync();
        Assert.Equal(inbox.Id, dto.StatusId);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required TrunkBuildBugController Bug { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required int ProjectId { get; init; }
        public required MergeCheckControllerTests.StubCallerIdentity Caller { get; init; }

        private int nextRepoOrder = 1;

        public async Task<EfHatchStatus> AddStatusAsync(
            string name, int sortOrder, bool isDeferred = false, bool isProtected = false, bool isWip = false)
        {
            var status = new EfHatchStatus
            {
                Name = name, SortOrder = sortOrder, IsDeferred = isDeferred, IsProtected = isProtected, IsWip = isWip,
            };
            Db.Statuses.Add(status);
            await Db.SaveChangesAsync();
            return status;
        }

        public async Task<EfHatchTrunkBuild> TrunkAsync(string verdict, string[]? failing = null)
        {
            var row = new EfHatchTrunkBuild
            {
                Remote = "git@forge.example:owner/repo.git",
                Canonical = Canonical,
                Trunk = "main",
                Sha = new string('2', 40),
                ShaSince = Now,
                Verdict = verdict,
                Failing = EfHatchBuildCheck.WriteFailing(
                    (failing ?? []).Select(n => new FailingCheckDto(n, $"https://forge.example/checks/{n}")).ToList()),
                CheckedAt = Now,
                Runner = "box:/work/repo",
                CheckedBy = "runner",
            };

            Db.TrunkBuilds.Add(row);
            await Db.SaveChangesAsync();
            return row;
        }

        public async Task<int> NewProjectAsync(string key)
        {
            var project = new EfHatchProject { Key = key, Name = key, CreatedAt = Now };
            Db.Projects.Add(project);
            await Db.SaveChangesAsync();
            return project.Id;
        }

        public async Task BindRepositoryAsync(int projectId, string canonical, int? sortOrder = null)
        {
            Db.ProjectRepositories.Add(new EfHatchProjectRepository
            {
                ProjectId = projectId,
                Remote = canonical,
                Canonical = canonical,
                SortOrder = sortOrder ?? nextRepoOrder++,
                CreatedAt = Now,
            });
            await Db.SaveChangesAsync();
        }

        public async Task ConfigureAutoHealAsync(
            bool enabled, int priority = PriorityLevels.Emergency, int preApproval = PreApprovalLevels.Express, int? statusId = null)
        {
            var project = await Db.Projects.SingleAsync(p => p.Id == ProjectId);
            project.AutoHealBugEnabled = enabled;
            project.AutoHealBugPriority = priority;
            project.AutoHealBugPreApproval = preApproval;
            project.AutoHealBugStatusId = statusId;
            await Db.SaveChangesAsync();
        }
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var hatch = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(hatch);
        db.Add(new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        db.Add(new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new MergeCheckControllerTests.StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        var httpContext = new DefaultHttpContext();
        var controllerContext = new ControllerContext(new ActionContext(
            httpContext, new RouteData(), new ControllerActionDescriptor(), new ModelStateDictionary()));

        return new Harness
        {
            Db = db,
            Bug = new TrunkBuildBugController(db, new RankService(db), actors, TestClaims.With(), caller, time)
            {
                ControllerContext = controllerContext,
            },
            Actors = actors,
            ProjectId = hatch.Id,
            Caller = caller,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");
}

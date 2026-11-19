using System.Runtime.CompilerServices;
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
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The bug a person files from a goose finding: what it files, where, what it
/// says, and why a second call never gets a second bug (HA-2516).
/// </summary>
public class GooseFindingBugControllerTests
{
    [Fact]
    public async Task Filing_FilesAnExpeditedBug_InTheFindingsProjectsFirstColumn()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId);

        var dto = Value(await h.Bug.PostBug(finding.Id, null, default));

        Assert.Equal("bug", dto.Type);
        Assert.StartsWith("AER-", dto.Key);
        Assert.True(dto.Expedited);
        Assert.Equal("none", dto.PreApproval);
        var inbox = await h.Db.Statuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).FirstAsync();
        Assert.Equal(inbox.Id, dto.StatusId);
    }

    [Fact]
    public async Task Filing_StoresTheBugOnTheFinding()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId);

        var dto = Value(await h.Bug.PostBug(finding.Id, null, default));

        var stored = await h.Db.GooseFindings.AsNoTracking().Include(f => f.BugIssue).SingleAsync(f => f.Id == finding.Id);
        Assert.Equal(dto.Key, IssueKey.Format("AER", stored.BugIssue!.Number));
    }

    [Fact]
    public async Task TheBug_HasACreatedEvent()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId);

        Value(await h.Bug.PostBug(finding.Id, null, default));

        var bug = await h.Db.Issues.Include(i => i.Events).AsNoTracking().SingleAsync();
        Assert.Single(bug.Events, e => e.Kind == EfHatchIssueEvent.Created);
    }

    [Fact]
    public async Task AFindingThatIsNotThere_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>((await h.Bug.PostBug(404, null, default)).Result);
    }

    // ---- The project ----

    [Fact]
    public async Task AProjectKeyOfAnotherProject_IsRefused_AndFilesNothing()
    {
        var h = await NewAsync();
        await h.NewProjectAsync("OPS");
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId);

        var result = await h.Bug.PostBug(finding.Id, new GooseFindingBugRequest(null, null, null, "OPS"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task ThePlainProjectKeyOfTheFindingsOwnProject_Files()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId);

        var dto = Value(await h.Bug.PostBug(finding.Id, new GooseFindingBugRequest(null, null, null, "aer"), default));

        Assert.StartsWith("AER-", dto.Key);
    }

    [Fact]
    public async Task AFindingWithNoProject_IsRefusedWithoutAProjectKey()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("questions-waiting", "board", "board", projectId: null);

        var reason = Assert.IsType<BadRequestObjectResult>((await h.Bug.PostBug(finding.Id, null, default)).Result).Value!.ToString();

        Assert.Contains("projectKey", reason);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AFindingWithNoProject_IsRefusedForAProjectTheCallerCannotSee()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("questions-waiting", "board", "board", projectId: null);

        var result = await h.Bug.PostBug(finding.Id, new GooseFindingBugRequest(null, null, null, "NOPE"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AFindingWithNoProject_FilesInTheProjectNamed()
    {
        var h = await NewAsync();
        await h.NewProjectAsync("OPS");
        var finding = await h.FindingAsync("questions-waiting", "board", "board", projectId: null);

        var dto = Value(await h.Bug.PostBug(finding.Id, new GooseFindingBugRequest(null, null, null, "OPS"), default));

        Assert.StartsWith("OPS-", dto.Key);
    }

    // ---- The body ----

    [Fact]
    public async Task ABody_ChoosesThePriority()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId);

        var dto = Value(await h.Bug.PostBug(finding.Id, new GooseFindingBugRequest("low", null, null, null), default));

        Assert.False(dto.Expedited);
    }

    [Fact]
    public async Task ABadPriorityName_IsRefused_AndFilesNothing()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId);

        var result = await h.Bug.PostBug(finding.Id, new GooseFindingBugRequest("urgent-ish", null, null, null), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AnUnknownPreApprovalLevel_IsRefused_AndFilesNothing()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId);

        var result = await h.Bug.PostBug(finding.Id, new GooseFindingBugRequest(null, null, "whenever", null), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task APreApprovalLevel_IsFiledAsNamed_ForAPersonWhoMayApprove()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId);
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Me = ada;
        h.Db.ProjectMembers.Add(new EfHatchProjectMember
        {
            ProjectId = h.ProjectId, PersonId = ada.Id, Role = ProjectMemberRole.Approver, CreatedAt = Now,
        });
        await h.Db.SaveChangesAsync();

        var dto = Value(await h.Bug.PostBug(finding.Id, new GooseFindingBugRequest(null, null, "preapproved", null), default));

        Assert.Equal("preapproved", dto.PreApproval);
    }

    [Fact]
    public async Task PreApproved_FromAPersonWhoMayNotApprove_IsRefusedForbidden_AndFilesNothing()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId);

        var result = Assert.IsType<ObjectResult>(
            (await h.Bug.PostBug(finding.Id, new GooseFindingBugRequest(null, null, "preapproved", null), default)).Result);

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Empty(await h.Db.Issues.ToListAsync());
    }

    [Fact]
    public async Task AStatusId_ChoosesTheColumn()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId);
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 15 };
        h.Db.Statuses.Add(doing);
        await h.Db.SaveChangesAsync();

        var dto = Value(await h.Bug.PostBug(finding.Id, new GooseFindingBugRequest(null, doing.Id, null, null), default));

        Assert.Equal(doing.Id, dto.StatusId);
    }

    // ---- A second call ----

    [Fact]
    public async Task ASecondCall_AnswersTheSameBug_AndFilesNoSecondOne()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId);

        var first = Value(await h.Bug.PostBug(finding.Id, null, default));
        var second = Value(await h.Bug.PostBug(finding.Id, null, default));

        Assert.Equal(first.Key, second.Key);
        Assert.Equal(1, await h.Db.Issues.CountAsync(i => i.Type == "bug"));
    }

    // ---- A closed finding ----

    [Fact]
    public async Task AClosedFinding_IsFiledLikeAnOpenOne_AndItsBriefSaysItCleared()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId, closedAt: Now.AddMinutes(-5));

        var dto = Value(await h.Bug.PostBug(finding.Id, null, default));

        Assert.Contains("cleared", dto.Description);
        Assert.Contains($"{Now.AddMinutes(-5):O}", dto.Description);
        Assert.Contains("cause", dto.Description);
    }

    [Fact]
    public async Task AnOpenFindings_BriefSaysNothingCleared()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId);

        var dto = Value(await h.Bug.PostBug(finding.Id, null, default));

        Assert.DoesNotContain("cleared", dto.Description);
    }

    // ---- The title ----

    [Theory]
    [InlineData("train-ready", "AER", "AER-12", "Merge train standing behind AER-12")]
    [InlineData("ready-unclaimed", "AER", "AER", "Ready work unclaimed in AER")]
    [InlineData("ready-no-runner", "AER", "AER", "No runner can take ready work in AER")]
    [InlineData("claim-repeating", "AER-7", "41", "Runner stuck re-claiming AER-7")]
    [InlineData("questions-waiting", "board", "board", "Questions waiting too long for an answer")]
    [InlineData("a-check-from-the-future", "AER", "AER", "The goose found a problem: a-check-from-the-future")]
    public async Task TheTitle_NamesTheProblem(string check, string subject, string token, string title)
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync(check, subject, token, h.ProjectId);

        var dto = Value(await h.Bug.PostBug(finding.Id, null, default));

        Assert.Equal(title, dto.Title);
    }

    [Fact]
    public void EveryCheck_HasATitle()
    {
        var checks = typeof(FindingCheck).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(FindingCheck)) && !t.IsAbstract)
            .ToList();
        Assert.NotEmpty(checks);

        foreach (var type in checks)
        {
            var name = ((FindingCheck)RuntimeHelpers.GetUninitializedObject(type)).Name;
            Assert.True(FindingBugs.Knows(name), $"{type.Name} ({name}) has no entry in FindingBugs");
        }
    }

    // ---- The brief ----

    [Fact]
    public async Task TheBrief_ReadsInOrder_AndCarriesEachEvidenceLine()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId);

        var description = Value(await h.Bug.PostBug(finding.Id, null, default)).Description;

        var order = new[]
        {
            "The merge train has stood.", $"Began: {Began:O}", "## Evidence", "- head: AER-12", "- judged: merge",
            "## The check", "`train-ready`", $"{new HatchOptions().GooseTrainReadyMinutes} minutes", "stood that long", "Find the cause, then either fix it or say what a person has to do.",
        };
        var at = -1;
        foreach (var part in order)
        {
            var next = description.IndexOf(part, at + 1, StringComparison.Ordinal);
            Assert.True(next > at, $"\"{part}\" is missing or out of order in:\n{description}");
            at = next;
        }
    }

    [Fact]
    public async Task TheBrief_IsNotJson_AndWritesNoAddress()
    {
        var h = await NewAsync();
        var finding = await h.FindingAsync("train-ready", "AER", "AER-12", h.ProjectId);

        var description = Value(await h.Bug.PostBug(finding.Id, null, default)).Description;

        Assert.DoesNotContain("{", description);
        Assert.DoesNotContain("http", description);
    }

    [Fact]
    public async Task TheBrief_SaysItsBoundInTheChecksOwnUnit()
    {
        var h = await NewAsync();
        var questions = await h.FindingAsync("questions-waiting", "board", "board", h.ProjectId);
        var claims = await h.FindingAsync("claim-repeating", "AER-7", "41", h.ProjectId);

        var hours = Value(await h.Bug.PostBug(questions.Id, null, default)).Description;
        var count = Value(await h.Bug.PostBug(claims.Id, null, default)).Description;

        Assert.Contains($"{new HatchOptions().GooseQuestionHours} hours", hours);
        Assert.Contains($"{new HatchOptions().GooseRepeatClaims} claims", count);
    }

    // ---- The gate ----

    [Fact]
    public void PostingIt_CarriesNoClassLevelAttribute_AndTakesNoKey()
    {
        Assert.Null(typeof(GooseFindingBugController).GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false).SingleOrDefault());

        var guard = typeof(GooseFindingBugController)
            .GetMethod(nameof(GooseFindingBugController.PostBug))!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        Assert.Equal(PersonRole.User, guard.Minimum);
        Assert.False(guard.AcceptKey);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Began = Now.AddHours(-1);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required GooseFindingBugController Bug { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required int ProjectId { get; init; }

        public async Task<EfHatchGooseFinding> FindingAsync(
            string check, string subject, string token, int? projectId, DateTimeOffset? closedAt = null)
        {
            var finding = new EfHatchGooseFinding
            {
                Check = check,
                Subject = subject,
                Token = token,
                ProjectId = projectId,
                BeganAt = Began,
                OpenedAt = Began.AddMinutes(15),
                ClosedAt = closedAt,
                Sentence = "The merge train has stood.",
                Evidence = GooseFindingEvidence.Write([new("head", "AER-12"), new("judged", "merge")]),
            };
            Db.GooseFindings.Add(finding);
            await Db.SaveChangesAsync();
            return finding;
        }

        public async Task<int> NewProjectAsync(string key)
        {
            var project = new EfHatchProject { Key = key, Name = key, CreatedAt = Now };
            Db.Projects.Add(project);
            await Db.SaveChangesAsync();
            return project.Id;
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
            Bug = new GooseFindingBugController(
                db, new RankService(db), actors, TestClaims.With(), caller, time, Options.Create(new HatchOptions()))
            {
                ControllerContext = controllerContext,
            },
            Actors = actors,
            ProjectId = hatch.Id,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");
}

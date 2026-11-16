using System.Text.Json;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Auth;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Hatch.Api.Ef;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="ChangelogTasks"/>: the task the board files under a parentless
/// issue the moment it closes, in a project with a repository that elects the
/// changelog. The doors that close an issue are covered here where one
/// controller reaches them, and beside their own harness where it takes
/// <c>WorkController</c> or <c>ImportController</c> to.
/// </summary>
public class ChangelogTasksTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private const string Remote = "https://example.com/owner/repo.git";

    [Fact]
    public async Task ClosingAParentlessIssue_InAProjectThatElectsARemote_FilesOneTaskUnderIt_BornInTheSendBackColumn()
    {
        var h = await NewAsync();
        await h.ElectAsync(Remote);
        var story = await h.FileAsync("story", "ships a thing", h.Doing);

        await h.CloseAsync(story);

        var task = Assert.Single(await h.TasksAsync());
        Assert.Equal("task", task.Type);
        Assert.Equal("Record AER-1 in the changelog", task.Title);
        Assert.Equal(h.Todo, task.StatusId);
        Assert.Equal(story.Id, task.ParentId);
        Assert.Equal(story.Id, task.FiledFromId);
        Assert.Equal(ChangelogTasks.Actor, task.CreatedBy);

        var record = await h.Db.ChangelogTasks.SingleAsync();
        Assert.Equal(task.Id, record.IssueId);
        Assert.Equal(story.Id, record.SourceIssueId);
        Assert.Equal("example.com/owner/repo", record.Canonical);
    }

    [Fact]
    public async Task TheDescription_NamesTheRemote_TheIssueAndTheCommand_AndStaysShort()
    {
        var h = await NewAsync();
        await h.ElectAsync(Remote);
        var story = await h.FileAsync("story", "ships a thing", h.Doing);

        await h.CloseAsync(story);

        var task = Assert.Single(await h.TasksAsync());
        Assert.Contains("example.com/owner/repo", task.Description);
        Assert.Contains("AER-1 shipped on 2026-11-15", task.Description);
        Assert.Contains("hatch changelog --file", task.Description);
        Assert.Contains(".hatch/changelog.yml", task.Description);
        Assert.Contains("`unchanged`", task.Description);
        Assert.True(task.Description.Length < 1500, $"{task.Description.Length} characters");
    }

    [Fact]
    public async Task TheClosedIssue_CarriesAChangelogTaskFiledEvent_NamingTheTaskAndTheRemote()
    {
        var h = await NewAsync();
        await h.ElectAsync(Remote);
        var story = await h.FileAsync("story", "ships a thing", h.Doing);

        await h.CloseAsync(story);

        var events = await h.Db.IssueEvents.Where(e => e.IssueId == story.Id && e.Kind == EfHatchIssueEvent.ChangelogTaskFiled).ToListAsync();
        var filed = Assert.Single(events);
        Assert.Equal(ChangelogTasks.Actor, filed.Actor);
        var payload = JsonDocument.Parse(filed.Payload!).RootElement;
        Assert.Equal("AER-2", payload.GetProperty("key").GetString());
        Assert.Equal("example.com/owner/repo", payload.GetProperty("canonical").GetString());

        var born = await h.Db.IssueEvents.Where(e => e.Issue!.Number == 2).SingleAsync();
        Assert.Equal(EfHatchIssueEvent.Created, born.Kind);
    }

    [Fact]
    public async Task TwoElectedRemotes_FileTwoTasks()
    {
        var h = await NewAsync();
        await h.ElectAsync(Remote);
        await h.ElectAsync("https://example.com/owner/other.git");
        var story = await h.FileAsync("story", "ships a thing", h.Doing);

        await h.CloseAsync(story);

        Assert.Equal(2, (await h.TasksAsync()).Count);
        Assert.Equal(
            ["example.com/owner/other", "example.com/owner/repo"],
            await h.Db.ChangelogTasks.Select(t => t.Canonical).OrderBy(c => c).ToListAsync());
    }

    [Fact]
    public async Task AFlagOff_AndAProjectWithNoRepository_FileNothing()
    {
        var h = await NewAsync();
        await h.ElectAsync(Remote, changelog: false);
        var story = await h.FileAsync("story", "flag off", h.Doing);
        var bare = await h.FileAsync("story", "other project", h.Doing, projectId: h.OtherProjectId);

        await h.CloseAsync(story);
        await h.CloseAsync(bare);

        Assert.Empty(await h.TasksAsync());
        Assert.Empty(await h.Db.ChangelogTasks.ToListAsync());
    }

    [Fact]
    public async Task AParentedIssueClosing_FilesNothing()
    {
        var h = await NewAsync();
        await h.ElectAsync(Remote);
        var story = await h.FileAsync("story", "ships a thing", h.Doing);
        var task = await h.FileAsync("task", "a part of it", h.Doing, parentId: story.Id);

        await h.CloseAsync(task);

        Assert.Empty(await h.Db.ChangelogTasks.ToListAsync());
    }

    [Fact]
    public async Task ATaskLeftOfImplementation_StandsInForTheNextCloses_UntilItHasMovedIn()
    {
        var h = await NewAsync();
        await h.ElectAsync(Remote);
        var first = await h.FileAsync("story", "one", h.Doing);
        var second = await h.FileAsync("story", "two", h.Doing);
        var third = await h.FileAsync("story", "three", h.Doing);

        await h.CloseAsync(first);
        await h.CloseAsync(second);

        Assert.Single(await h.Db.ChangelogTasks.ToListAsync());

        // Picked up: the file it writes may already be stale, so the next close files another.
        var standing = await h.Db.Issues.SingleAsync(i => i.Id == (h.Db.ChangelogTasks.Single().IssueId));
        standing.StatusId = h.Doing;
        await h.Db.SaveChangesAsync();

        await h.CloseAsync(third);

        Assert.Equal(2, await h.Db.ChangelogTasks.CountAsync());
    }

    [Fact]
    public async Task ATaskThatIsDone_NoLongerStandsIn()
    {
        var h = await NewAsync();
        await h.ElectAsync(Remote);
        var first = await h.FileAsync("story", "one", h.Doing);
        var second = await h.FileAsync("story", "two", h.Doing);

        await h.CloseAsync(first);
        var recorded = await h.Db.Issues.SingleAsync(i => i.Id == h.Db.ChangelogTasks.Single().IssueId);
        recorded.StatusId = h.Done;
        recorded.ClosedAt = Now;
        await h.Db.SaveChangesAsync();

        await h.CloseAsync(second);

        Assert.Equal(2, await h.Db.ChangelogTasks.CountAsync());
    }

    [Fact]
    public async Task ClosingARecordedTask_FilesNothingAfterIt()
    {
        var h = await NewAsync();
        await h.ElectAsync(Remote);
        // A closing task gets a parentless task, which is the one that can itself close.
        var chore = await h.FileAsync("task", "a loose task", h.Doing);
        await h.CloseAsync(chore);
        var recorded = await h.Db.Issues.SingleAsync(i => i.Id == h.Db.ChangelogTasks.Single().IssueId);
        Assert.Null(recorded.ParentId);

        await h.CloseAsync(recorded);

        Assert.Single(await h.Db.ChangelogTasks.ToListAsync());
    }

    [Fact]
    public async Task ATaskClosing_GetsAParentlessTask_NamingItAndCarryingItsEffectiveLevel()
    {
        var h = await NewAsync();
        await h.ElectAsync(Remote);
        var chore = await h.FileAsync("task", "a loose task", h.Doing);
        chore.PreApproval = PreApprovalLevels.Express;
        await h.Db.SaveChangesAsync();

        await h.CloseAsync(chore);

        var task = Assert.Single(await h.TasksAsync());
        Assert.Null(task.ParentId);
        Assert.Equal(chore.Id, task.FiledFromId);
        Assert.Equal(PreApprovalLevels.Express, task.PreApproval);
    }

    [Fact]
    public async Task ATaskUnderTheClosedIssue_LeavesItsOwnLevelUnset_ToInheritTheParents()
    {
        var h = await NewAsync();
        await h.ElectAsync(Remote);
        var story = await h.FileAsync("story", "one", h.Doing);
        story.PreApproval = PreApprovalLevels.PreApproved;
        await h.Db.SaveChangesAsync();

        await h.CloseAsync(story);

        var task = Assert.Single(await h.TasksAsync());
        Assert.Equal(PreApprovalLevels.None, task.PreApproval);
        var tree = await PriorityTree.ForAsync(h.Db, default);
        Assert.Equal(PreApprovalLevels.PreApproved, tree.EffectivePreApproval(task.Id).Level);
    }

    [Fact]
    public async Task ABulkClose_OfTwoIssues_FilesOneTask()
    {
        var h = await NewAsync();
        await h.ElectAsync(Remote);
        await h.FileAsync("story", "one", h.Doing);
        await h.FileAsync("story", "two", h.Doing);

        Assert.Empty((await h.Issues.BulkEdit(new IssueBulkEditRequest(["AER-1", "AER-2"], StatusId: h.Done), default)).Value!.Failures);

        Assert.Single(await h.Db.ChangelogTasks.ToListAsync());
    }

    [Fact]
    public async Task APatchIntoADoneColumn_FilesOneTask()
    {
        var h = await NewAsync();
        await h.ElectAsync(Remote);
        await h.FileAsync("story", "one", h.Doing);

        var patched = await h.Issues.PatchIssue("AER-1", new IssuePatchRequest(null, null, null, h.Done, null, null, null, null, null, null), default);
        Assert.NotNull(patched.Value);

        Assert.Single(await h.Db.ChangelogTasks.ToListAsync());
    }

    [Fact]
    public async Task MakingAColumnDone_FilesOneTaskForEveryParentlessIssueStandingInIt_AndNoneForAChild()
    {
        var h = await NewAsync();
        await h.ElectAsync(Remote);
        var story = await h.FileAsync("story", "one", h.Shelf);
        await h.FileAsync("task", "a child", h.Shelf, parentId: story.Id);

        Assert.NotNull((await h.Statuses.PatchStatus(h.Shelf, new StatusPatchRequest(null, null, true), default)).Value);

        var recorded = await h.Db.ChangelogTasks.SingleAsync();
        Assert.Equal(story.Id, recorded.SourceIssueId);
    }

    [Fact]
    public async Task AMoveBetweenTwoDoneColumns_FilesNothing()
    {
        var h = await NewAsync();
        await h.ElectAsync(Remote);
        var story = await h.FileAsync("story", "one", h.Done);
        story.ClosedAt = Now;
        await h.Db.SaveChangesAsync();

        await h.Issues.MoveIssue("AER-1", new IssueMoveRequest(h.Done, null, null), default);

        Assert.Empty(await h.Db.ChangelogTasks.ToListAsync());
    }

    [Fact]
    public void JustClosed_IsTrueOnlyForAParentlessIssueEnteringATerminalColumnFromANonTerminalOne()
    {
        var open = new EfHatchStatus { Id = 1, Name = "open", SortOrder = 1 };
        var done = new EfHatchStatus { Id = 2, Name = "done", SortOrder = 2, IsTerminal = true };
        var other = new EfHatchStatus { Id = 3, Name = "other", SortOrder = 3, IsTerminal = true };
        EfHatchIssue Issue(long? parent) => new() { ProjectId = 1, Number = 1, Type = "task", Title = "t", StatusId = 1, ParentId = parent, Rank = 1, CreatedBy = "x", CreatedAt = Now, UpdatedAt = Now };

        Assert.True(ChangelogTasks.JustClosed(open, done, Issue(null)));
        Assert.True(ChangelogTasks.JustClosed(null, done, Issue(null)));
        Assert.False(ChangelogTasks.JustClosed(done, other, Issue(null)));
        Assert.False(ChangelogTasks.JustClosed(open, open, Issue(null)));
        Assert.False(ChangelogTasks.JustClosed(open, done, Issue(7)));
    }

    // ---- Harness ----

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var other = new EfHatchProject { Key = "OPS", Name = "Operations", CreatedAt = Now };
        var ideas = new EfHatchStatus { Name = "ideas", SortOrder = 5 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsImplementation = true };
        var review = new EfHatchStatus { Name = "review", SortOrder = 40 };
        var shelf = new EfHatchStatus { Name = "shelf", SortOrder = 45 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 50, IsTerminal = true };
        db.AddRange(project, other, ideas, todo, doing, review, shelf, done);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var ranks = new RankService(db);
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Issues = new IssuesController(db, ranks, actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Statuses = new StatusesController(db, caller, time, NullLogger<StatusesController>.Instance),
            ProjectId = project.Id,
            OtherProjectId = other.Id,
            Todo = todo.Id,
            Doing = doing.Id,
            Shelf = shelf.Id,
            Done = done.Id,
        };
    }

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required IssuesController Issues { get; init; }
        public required StatusesController Statuses { get; init; }
        public required int ProjectId { get; init; }
        public required int OtherProjectId { get; init; }
        public required int Todo { get; init; }
        public required int Doing { get; init; }
        public required int Shelf { get; init; }
        public required int Done { get; init; }

        public async Task ElectAsync(string remote, bool changelog = true)
        {
            var (canonical, _) = RemoteIdentity.Canonical(remote);
            Db.ProjectRepositories.Add(new EfHatchProjectRepository
            {
                ProjectId = ProjectId,
                Remote = remote,
                Canonical = canonical!,
                Changelog = changelog,
                SortOrder = await Db.ProjectRepositories.CountAsync(r => r.ProjectId == ProjectId),
                CreatedAt = Now,
            });
            await Db.SaveChangesAsync();
        }

        public async Task<EfHatchIssue> FileAsync(string type, string title, int statusId, long? parentId = null, int? projectId = null)
        {
            var pid = projectId ?? ProjectId;
            var project = await Db.Projects.SingleAsync(p => p.Id == pid);
            var issue = new EfHatchIssue
            {
                ProjectId = pid,
                Number = project.NextIssueNumber++,
                Type = type,
                Title = title,
                Description = "",
                StatusId = statusId,
                ParentId = parentId,
                Rank = 1024 * (project.NextIssueNumber),
                CreatedBy = "operator",
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        /// <summary>The drag: the move route into the done column.</summary>
        public async Task CloseAsync(EfHatchIssue issue)
        {
            var key = IssueKey.Format("AER", issue.Number);
            var result = await Issues.MoveIssue(key, new IssueMoveRequest(Done, null, null), default);
            Assert.NotNull(result.Value);
        }

        public Task<List<EfHatchIssue>> TasksAsync() =>
            Db.Issues.Where(i => Db.ChangelogTasks.Any(t => t.IssueId == i.Id)).OrderBy(i => i.Id).ToListAsync();
    }
}

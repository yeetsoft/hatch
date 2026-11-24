using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Auth;
using Hatch.Api.Tests.Chat;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Hatch.Api.Ef;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="ChangelogMessages"/>: the message a room gets when a parentless
/// issue closes, queued in the same save as the close. The doors that close an
/// issue through <c>IssuesController</c> and <c>StatusesController</c> are here;
/// the work, remand and release-note doors are beside their own harnesses.
/// Nothing here calls Slack: <see cref="FakeChatProvider"/> answers.
/// </summary>
public class ChangelogMessagesTests
{
    private static readonly DateTimeOffset Now = ChatOutboxTests.Now;

    private const string Origin = "https://hatch.test";

    // ---- What the message says ----

    [Fact]
    public async Task ClosingAParentlessIssue_QueuesOneMessage_ForEachRoomThatCarriesTheChangelog_InTheSameSaveAsTheClose()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        await h.RoomAsync("C2");
        await h.RoomAsync("C3", changelog: false);
        await h.FileAsync("story", "ships a thing", h.Doing);

        await h.PatchIntoDoneAsync("AER-1");

        var rows = await h.MessagesAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal("changelog", r.Kind);
            Assert.Equal("issue:1", r.Subject);
            Assert.Equal(h.ProjectId, r.ProjectId);
            Assert.Equal(1, r.Revision);
        });
        Assert.Equal(2, rows.Select(r => r.RoomId).Distinct().Count());
        Assert.Equal(h.Done, (await h.Db.Issues.AsNoTracking().SingleAsync()).StatusId);
    }

    [Fact]
    public async Task TheMessage_NamesTheKeyTypeAndTitle_AndLinksToTheIssue()
    {
        var h = await NewAsync(Origin);
        await h.RoomAsync("C1");
        await h.FileAsync("story", "ships a thing", h.Doing);

        await h.CloseAsync("AER-1");

        var content = Assert.Single(await h.ContentsAsync());
        Assert.Equal("story shipped: ships a thing", content.Title);
        Assert.Null(content.Body);
        Assert.Empty(content.Lines);
        var link = Assert.Single(content.Links);
        Assert.Equal("AER-1", link.Label);
        Assert.Equal($"{Origin}/issues/AER-1", link.Url);
    }

    [Fact]
    public async Task TheMessage_CarriesTheReleaseNote_WhenThereIsOne()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        var story = await h.FileAsync("story", "ships a thing", h.Doing);
        story.ReleaseNote = "You can ship a thing now.";
        await h.Db.SaveChangesAsync();

        await h.CloseAsync("AER-1");

        Assert.Equal("You can ship a thing now.", Assert.Single(await h.ContentsAsync()).Body);
    }

    [Fact]
    public async Task WithoutAPublicBaseUrl_TheMessageNamesTheKey_WithNoLink()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        await h.FileAsync("story", "ships a thing", h.Doing);

        await h.CloseAsync("AER-1");

        var link = Assert.Single(Assert.Single(await h.ContentsAsync()).Links);
        Assert.Equal("AER-1", link.Label);
        Assert.Null(link.Url);
    }

    [Fact]
    public async Task TheTitle_IsHandedOverAsItIs_ForTheProviderToEscape()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        await h.FileAsync("story", "fix <b>&amp; *bold* @here", h.Doing);

        await h.CloseAsync("AER-1");

        Assert.Equal("story shipped: fix <b>&amp; *bold* @here", Assert.Single(await h.ContentsAsync()).Title);
    }

    // ---- Every door that closes an issue ----

    [Fact]
    public async Task APatchIntoADoneColumn_QueuesTheMessage()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        await h.FileAsync("story", "one", h.Doing);

        await h.PatchIntoDoneAsync("AER-1");

        Assert.Single(await h.MessagesAsync());
    }

    [Fact]
    public async Task APatchThatClosesAndMovesTheProject_QueuesTheMessage_UnderTheNewKey_ForTheNewProjectsRooms()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        await h.RoomAsync("C2", workspace: h.OtherWorkspace);
        await h.FileAsync("story", "moves as it closes", h.Doing);

        var patched = await h.Issues.PatchIssue(
            "AER-1", new IssuePatchRequest(null, null, null, h.Done, null, null, null, null, h.OtherProjectId, null), default);
        Assert.NotNull(patched.Value);

        var row = Assert.Single(await h.MessagesAsync());
        Assert.Equal(h.OtherProjectId, row.ProjectId);
        Assert.Equal(h.OtherWorkspace, row.WorkspaceId);
        var link = Assert.Single(ChatMessageContent.Read(row.Content)!.Links);
        Assert.Equal("OPS-1", link.Label);
    }

    [Fact]
    public async Task AMoveIntoADoneColumn_QueuesTheMessage()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        await h.FileAsync("story", "one", h.Doing);

        await h.CloseAsync("AER-1");

        Assert.Single(await h.MessagesAsync());
    }

    [Fact]
    public async Task ABulkClose_QueuesOneMessagePerParentlessIssue_AndNoneForAChild()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        var first = await h.FileAsync("story", "one", h.Doing);
        await h.FileAsync("story", "two", h.Doing);
        await h.FileAsync("task", "a part of one", h.Doing, parentId: first.Id);

        var result = await h.Issues.BulkEdit(new IssueBulkEditRequest(["AER-1", "AER-2", "AER-3"], StatusId: h.Done), default);
        Assert.Empty(result.Value!.Failures);

        Assert.Equal(["issue:1", "issue:2"], (await h.MessagesAsync()).Select(r => r.Subject).Order());
    }

    [Fact]
    public async Task ABulkCloseThatMovesTheProject_QueuesTheMessageUnderTheNewKey()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1", workspace: h.OtherWorkspace);
        await h.FileAsync("story", "one", h.Doing);

        var result = await h.Issues.BulkEdit(new IssueBulkEditRequest(["AER-1"], StatusId: h.Done, ProjectId: h.OtherProjectId), default);
        Assert.Empty(result.Value!.Failures);

        var row = Assert.Single(await h.MessagesAsync());
        Assert.Equal("OPS-1", Assert.Single(ChatMessageContent.Read(row.Content)!.Links).Label);
    }

    [Fact]
    public async Task MakingAColumnDone_QueuesOneMessageForEveryParentlessIssueStandingInIt_AndNoneForAChild()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        var story = await h.FileAsync("story", "one", h.Shelf);
        await h.FileAsync("story", "two", h.Shelf);
        await h.FileAsync("task", "a child", h.Shelf, parentId: story.Id);
        h.Db.ChangeTracker.Clear();

        Assert.NotNull((await h.Statuses.PatchStatus(h.Shelf, new StatusPatchRequest(null, null, true), default)).Value);

        var rows = await h.MessagesAsync();
        Assert.Equal(["issue:1", "issue:2"], rows.Select(r => r.Subject).Order());
        Assert.Equal(
            ["AER-1", "AER-2"],
            rows.Select(r => Assert.Single(ChatMessageContent.Read(r.Content)!.Links).Label).Order());
    }

    [Fact]
    public async Task MakingAColumnNotDone_QueuesNothing()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        await h.FileAsync("story", "one", h.Done);

        Assert.NotNull((await h.Statuses.PatchStatus(h.Done, new StatusPatchRequest(null, null, false), default)).Value);

        Assert.Empty(await h.MessagesAsync());
    }

    // ---- What posts nothing ----

    [Fact]
    public async Task AChildIssueClosing_PostsNothing()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        var story = await h.FileAsync("story", "ships a thing", h.Doing);
        await h.FileAsync("task", "a part of it", h.Doing, parentId: story.Id);

        await h.CloseAsync("AER-2");

        Assert.Empty(await h.MessagesAsync());
    }

    [Fact]
    public async Task AnIssueThatMovesBetweenTwoDoneColumns_PostsNothing()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        var story = await h.FileAsync("story", "one", h.Done);
        story.ClosedAt = Now;
        await h.Db.SaveChangesAsync();

        await h.CloseAsync("AER-1");

        Assert.Empty(await h.MessagesAsync());
    }

    [Fact]
    public async Task ATaskHatchFiledToBringTheChangelogCurrent_PostsNothing_WhenItCloses()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        var chore = await h.FileAsync("task", "a loose task", h.Doing);
        await h.CloseAsync("AER-1");
        var recorded = await h.Db.Issues.AsNoTracking().SingleAsync(i => i.Id == chore.Id);
        // The board's own record, as ChangelogTasks writes it, for a second parentless task.
        var task = await h.FileAsync("task", "Record AER-1 in the changelog", h.Doing);
        h.Db.ChangelogTasks.Add(new EfHatchChangelogTask
        {
            IssueId = task.Id, SourceIssueId = recorded.Id, Canonical = "host/owner/repo", CreatedAt = Now,
        });
        await h.Db.SaveChangesAsync();

        await h.CloseAsync("AER-2");

        Assert.Equal(["issue:1"], (await h.MessagesAsync()).Select(r => r.Subject));
    }

    [Fact]
    public async Task AnImport_PostsNothing_ForIssuesThatArriveClosed()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        var import = new ImportController(
            h.Db, new StubActorDirectory(), new PlanImportParser(), new RankService(h.Db), h.Caller, h.Send.Time);
        var epic = new ParsedEpic(
            "plan.md", "A finished plan", "what it was", PlanState.Done,
            [new ParsedStory("A finished phase", "", PlanState.Done, [new ParsedTask("a finished step", "", PlanState.Done)])]);

        Assert.NotNull((await import.Import(new ImportRequest(h.ProjectId, [epic]), default)).Value);

        Assert.Equal(3, await h.Db.Issues.CountAsync(i => i.StatusId == h.Done));
        Assert.Empty(await h.MessagesAsync());
        Assert.Empty(await h.Db.ChatSubjects.ToListAsync());
    }

    // ---- Posting once ----

    [Fact]
    public async Task AReopenedIssueClosedAgain_AddsNoSecondMessage_AndEditsNothing_IfItsWordsAreUnchanged()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        await h.FileAsync("story", "ships a thing", h.Doing);
        await h.CloseAsync("AER-1");

        await h.MoveAsync("AER-1", h.Doing);
        await h.CloseAsync("AER-1");

        var row = Assert.Single(await h.MessagesAsync());
        Assert.Equal(1, row.Revision);
        Assert.Single(await h.Db.ChatSubjects.ToListAsync());
    }

    [Fact]
    public async Task AReopenedIssueClosedAgain_WithADifferentTitle_EditsTheOneMessage()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        await h.FileAsync("story", "ships a thing", h.Doing);
        await h.CloseAsync("AER-1");
        await h.MoveAsync("AER-1", h.Doing);
        var issue = await h.Db.Issues.SingleAsync();
        issue.Title = "ships two things";
        await h.Db.SaveChangesAsync();

        await h.CloseAsync("AER-1");

        var row = Assert.Single(await h.MessagesAsync());
        Assert.Equal(2, row.Revision);
        Assert.Equal("story shipped: ships two things", ChatMessageContent.Read(row.Content)!.Title);
    }

    [Fact]
    public async Task ARoomAddedAfterTheClose_GetsNothingAboutIt_NotEvenAnEditOfIt()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        await h.FileAsync("story", "ships a thing", h.Doing);
        await h.CloseAsync("AER-1");
        await h.RoomAsync("C2");

        await h.MoveAsync("AER-1", h.Doing);
        await h.CloseAsync("AER-1");

        var row = Assert.Single(await h.MessagesAsync());
        Assert.Equal("C1", (await h.Db.ChatRooms.SingleAsync(r => r.Id == row.RoomId)).ExternalId);
    }

    // ---- The release note after the post ----

    [Fact]
    public async Task ANoteChangedAfterThePost_EditsTheMessage_ToCarryIt()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        await h.FileAsync("story", "ships a thing", h.Doing);
        await h.CloseAsync("AER-1");

        await h.SetNoteAsync("AER-1", "You can ship a thing now.");

        var row = Assert.Single(await h.MessagesAsync());
        Assert.Equal(2, row.Revision);
        var content = ChatMessageContent.Read(row.Content)!;
        Assert.Equal("story shipped: ships a thing", content.Title);
        Assert.Equal("You can ship a thing now.", content.Body);
    }

    [Fact]
    public async Task ANoteOnAnIssueThatWasNeverAnnounced_PostsNothing()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        var story = await h.FileAsync("story", "still open", h.Doing);
        await h.FileAsync("task", "a child", h.Doing, parentId: story.Id);
        await h.CloseAsync("AER-2");

        await h.SetNoteAsync("AER-1", "Written early.");
        await h.SetNoteAsync("AER-2", "A child's note.");

        Assert.Empty(await h.MessagesAsync());
        Assert.Empty(await h.Db.ChatSubjects.ToListAsync());
    }

    // ---- Two workspaces ----

    [Fact]
    public async Task WithTwoWorkspaces_AndADifferentProjectBoundToEach_ACloseInOneProject_PostsOnlyInItsOwnWorkspace()
    {
        var h = await NewAsync();
        await h.RoomAsync("C1");
        await h.RoomAsync("C9", workspace: h.OtherWorkspace);
        await h.FileAsync("story", "in the first", h.Doing);
        await h.FileAsync("story", "in the second", h.Doing, projectId: h.OtherProjectId);

        await h.CloseAsync("AER-1");

        var first = Assert.Single(await h.MessagesAsync());
        Assert.Equal(h.Workspace, first.WorkspaceId);
        Assert.Equal(h.ProjectId, first.ProjectId);

        await h.CloseAsync("OPS-1");

        var rows = await h.MessagesAsync();
        Assert.Equal(2, rows.Count);
        var second = rows.Single(r => r.Subject == "issue:2");
        Assert.Equal(h.OtherWorkspace, second.WorkspaceId);
        Assert.Equal(h.OtherProjectId, second.ProjectId);
    }

    // ---- Sent through the job ----

    [Fact]
    public async Task ACloseThenANoteChange_IsOnePostThenOneEdit_ThroughTheSendJob()
    {
        var h = await NewAsync(Origin);
        await h.SealAsync();
        await h.RoomAsync("C1");
        await h.FileAsync("story", "ships a thing", h.Doing);
        await h.CloseAsync("AER-1");

        await h.Send.TickAsync();

        var post = Assert.Single(h.Send.Provider.Sends);
        Assert.Equal(("post", "C1", null), (post.Kind, post.ChannelId, post.MessageId));
        Assert.Equal("story shipped: ships a thing", post.Content.Title);
        Assert.Equal($"{Origin}/issues/AER-1", Assert.Single(post.Content.Links).Url);

        await h.MoveAsync("AER-1", h.Doing);
        await h.CloseAsync("AER-1");
        await h.Send.TickAsync();
        Assert.Single(h.Send.Provider.Sends);

        await h.SetNoteAsync("AER-1", "You can ship a thing now.");
        await h.Send.TickAsync();

        Assert.Equal(["post", "update"], h.Send.Provider.Sends.Select(s => s.Kind));
        var edit = h.Send.Provider.Sends[1];
        Assert.Equal("ts-1", edit.MessageId);
        Assert.Equal("You can ship a thing now.", edit.Content.Body);
    }

    // ---- Harness ----

    private static async Task<Harness> NewAsync(string? baseUrl = null)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var other = new EfHatchProject { Key = "OPS", Name = "Operations", CreatedAt = Now };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsImplementation = true };
        var shelf = new EfHatchStatus { Name = "shelf", SortOrder = 45 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 50, IsTerminal = true };
        db.AddRange(project, other, todo, doing, shelf, done);
        await db.SaveChangesAsync();

        var send = new ChatSendJobTests.Harness(db);
        var seed = send.Seed;

        // One workspace for each project, so a room is told only about its own.
        var workspace = await seed.WorkspaceAsync("TACME", project.Id);
        var otherWorkspace = await seed.WorkspaceAsync("TGLOBEX", other.Id);

        var time = send.Time;
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var ranks = new RankService(db);
        var actors = new StubActorDirectory();
        var options = Options.Create(new HatchOptions { PublicBaseUrl = baseUrl! });
        var chat = new ChangelogMessages(send.Outbox, new ChatLinks(options), db);

        return new Harness
        {
            Db = db,
            Send = send,
            Caller = caller,
            Issues = new IssuesController(db, ranks, actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors), chat),
            Statuses = new StatusesController(db, actors, caller, time, NullLogger<StatusesController>.Instance, chat),
            Notes = new IssueReleaseNoteController(db, actors, TestClaims.With(), caller, time, chat),
            ProjectId = project.Id,
            OtherProjectId = other.Id,
            Workspace = workspace,
            OtherWorkspace = otherWorkspace,
            Doing = doing.Id,
            Shelf = shelf.Id,
            Done = done.Id,
        };
    }

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required ChatSendJobTests.Harness Send { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required IssuesController Issues { get; init; }
        public required StatusesController Statuses { get; init; }
        public required IssueReleaseNoteController Notes { get; init; }
        public required int ProjectId { get; init; }
        public required int OtherProjectId { get; init; }

        /// <summary>The workspace bound to <see cref="ProjectId"/>.</summary>
        public required int Workspace { get; init; }

        /// <summary>The workspace bound to <see cref="OtherProjectId"/>.</summary>
        public required int OtherWorkspace { get; init; }
        public required int Doing { get; init; }
        public required int Shelf { get; init; }
        public required int Done { get; init; }

        /// <summary>A room in the first workspace unless another is named, carrying the changelog unless it is told not to.</summary>
        public Task<int> RoomAsync(string channel, bool changelog = true, int? workspace = null) =>
            Send.Seed.RoomAsync(workspace ?? Workspace, channel, changelog: changelog);

        public Task SealAsync() => Send.SealAsync(Workspace);

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
        public Task CloseAsync(string key) => MoveAsync(key, Done);

        public async Task MoveAsync(string key, int statusId)
        {
            var result = await Issues.MoveIssue(key, new IssueMoveRequest(statusId, null, null), default);
            Assert.NotNull(result.Value);
        }

        public async Task PatchIntoDoneAsync(string key)
        {
            var patched = await Issues.PatchIssue(key, new IssuePatchRequest(null, null, null, Done, null, null, null, null, null, null), default);
            Assert.NotNull(patched.Value);
        }

        public async Task SetNoteAsync(string key, string note) =>
            Assert.NotNull((await Notes.PutIssueReleaseNote(key, new ReleaseNoteRequest(note), null, default)).Value);

        public Task<List<EfHatchChatMessage>> MessagesAsync() =>
            Db.ChatMessages.IgnoreQueryFilters().AsNoTracking().OrderBy(m => m.Id).ToListAsync();

        public async Task<List<ChatMessageContent>> ContentsAsync() =>
            (await MessagesAsync()).Select(m => ChatMessageContent.Read(m.Content)!).ToList();
    }
}

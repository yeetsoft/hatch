using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// A write queues a message for the rooms that should hear it, and the rows are
/// the write's own: nothing is saved here and nothing is sent. The in-memory
/// context enforces no key, so what the database does with a failed save or an
/// unbound project is in <see cref="ChatOutboxDatabaseTests"/>.
/// </summary>
public class ChatOutboxTests
{
    [Fact]
    public async Task ARoomWithTheSwitchOff_GetsNothing()
    {
        var h = NewHarness();
        var project = await h.Seed.ProjectAsync("AER");
        var workspace = await h.Seed.WorkspaceAsync("TACME", project);
        await h.Seed.RoomAsync(workspace, "C1", changelog: false, questions: true, problems: true);

        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", project, Content("Done"), default);
        await h.Db.SaveChangesAsync();

        Assert.Empty(h.Db.ChatMessages);
        Assert.Single(h.Db.ChatSubjects);
    }

    [Theory]
    [InlineData(ChatMessageKind.Changelog)]
    [InlineData(ChatMessageKind.Question)]
    [InlineData(ChatMessageKind.Problem)]
    public async Task EachKind_AnswersToItsOwnSwitch(ChatMessageKind kind)
    {
        var h = NewHarness();
        var project = await h.Seed.ProjectAsync("AER");
        var workspace = await h.Seed.WorkspaceAsync("TACME", project);
        var changelog = await h.Seed.RoomAsync(workspace, "C1", changelog: true);
        var questions = await h.Seed.RoomAsync(workspace, "C2", questions: true);
        var problems = await h.Seed.RoomAsync(workspace, "C3", problems: true);

        await h.Outbox.QueueAsync(kind, "x:1", project, Content("Hello"), default);
        await h.Db.SaveChangesAsync();

        var expected = kind switch
        {
            ChatMessageKind.Changelog => changelog,
            ChatMessageKind.Question => questions,
            _ => problems,
        };
        Assert.Equal([expected], await h.Db.ChatMessages.Select(m => m.RoomId).ToListAsync());
    }

    [Fact]
    public async Task ARoomInAWorkspaceNotBoundToTheProject_GetsNothing_AllProjectsOrChosen()
    {
        var h = NewHarness();
        var aer = await h.Seed.ProjectAsync("AER");
        var other = await h.Seed.ProjectAsync("OTH");
        var workspace = await h.Seed.WorkspaceAsync("TACME", other);
        await h.Seed.RoomAsync(workspace, "C1", changelog: true, allProjects: true);
        await h.Seed.RoomAsync(workspace, "C2", changelog: true, allProjects: false, chosen: [aer]);

        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", aer, Content("Done"), default);
        await h.Db.SaveChangesAsync();

        Assert.Empty(h.Db.ChatMessages);
    }

    [Fact]
    public async Task ARoomThatChoseFewProjects_GetsThoseAndNoOthers()
    {
        var h = NewHarness();
        var aer = await h.Seed.ProjectAsync("AER");
        var other = await h.Seed.ProjectAsync("OTH");
        var workspace = await h.Seed.WorkspaceAsync("TACME", aer, other);
        var room = await h.Seed.RoomAsync(workspace, "C1", changelog: true, allProjects: false, chosen: [aer]);
        var noneChosen = await h.Seed.RoomAsync(workspace, "C2", changelog: true, allProjects: false);

        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", aer, Content("In"), default);
        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:2", other, Content("Out"), default);
        await h.Db.SaveChangesAsync();

        var rows = await h.Db.ChatMessages.ToListAsync();
        var only = Assert.Single(rows);
        Assert.Equal(("issue:1", room), (only.Subject, only.RoomId));
        Assert.DoesNotContain(rows, m => m.RoomId == noneChosen);
    }

    [Fact]
    public async Task TwoWorkspacesBoundToOneProject_EachRoomGetsARow_WithItsOwnWorkspace()
    {
        var h = NewHarness();
        var project = await h.Seed.ProjectAsync("AER");
        var acme = await h.Seed.WorkspaceAsync("TACME", project);
        var globex = await h.Seed.WorkspaceAsync("TGLOBEX", project);
        var one = await h.Seed.RoomAsync(acme, "C1", changelog: true);
        var two = await h.Seed.RoomAsync(globex, "C1", changelog: true);

        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", project, Content("Done"), default);
        await h.Db.SaveChangesAsync();

        var rows = await h.Db.ChatMessages.OrderBy(m => m.RoomId).ToListAsync();
        Assert.Equal([one, two], rows.Select(m => m.RoomId));
        Assert.Equal([acme, globex], rows.Select(m => m.WorkspaceId));
        Assert.All(rows, m =>
        {
            Assert.Equal(project, m.ProjectId);
            Assert.Equal(1, m.Revision);
            Assert.Equal(0, m.SentRevision);
            Assert.Equal(Now, m.QueuedAt);
            Assert.Equal(Now, m.PendingSince);
        });
    }

    [Fact]
    public async Task AnUnownedProblem_GoesOnlyToRoomsThatTakeUnownedProblems()
    {
        var h = NewHarness();
        var project = await h.Seed.ProjectAsync("AER");
        var workspace = await h.Seed.WorkspaceAsync("TACME", project);
        var takes = await h.Seed.RoomAsync(workspace, "C1", problems: true, unownedProblems: true);
        await h.Seed.RoomAsync(workspace, "C2", problems: true, unownedProblems: false);
        await h.Seed.RoomAsync(workspace, "C3", changelog: true, questions: true);

        await h.Outbox.QueueAsync(ChatMessageKind.Problem, "finding:1", null, Content("Odd"), default);
        await h.Db.SaveChangesAsync();

        var only = Assert.Single(h.Db.ChatMessages);
        Assert.Equal(takes, only.RoomId);
        Assert.Null(only.ProjectId);
    }

    [Theory]
    [InlineData(ChatMessageKind.Changelog)]
    [InlineData(ChatMessageKind.Question)]
    public async Task AnUnownedMessageOtherThanAProblem_Throws_AndQueuesNothing(ChatMessageKind kind)
    {
        var h = NewHarness();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            h.Outbox.QueueAsync(kind, "x:1", null, Content("No home"), default));

        Assert.Empty(h.Db.ChatSubjects.Local);
        Assert.Empty(h.Db.ChatMessages.Local);
    }

    [Fact]
    public async Task ASubjectLongerThanTheColumn_Throws()
    {
        var h = NewHarness();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            h.Outbox.QueueAsync(ChatMessageKind.Problem, new string('a', 65), null, Content("Long"), default));
    }

    [Fact]
    public async Task TheSameCallAgain_IsOneRow_WithTheLatestContent_AtRevisionTwo()
    {
        var h = await WithOneRoomAsync();

        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", h.Project, Content("First"), default);
        await h.Db.SaveChangesAsync();
        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", h.Project, Content("Second"), default);
        await h.Db.SaveChangesAsync();

        var row = Assert.Single(h.Db.ChatMessages);
        Assert.Equal(2, row.Revision);
        Assert.Equal("Second", ChatMessageContent.Read(row.Content)!.Title);
    }

    [Fact]
    public async Task AChangeToASentRow_PutsItBackToWaiting_AndClearsItsBackoff()
    {
        var h = await WithOneRoomAsync();
        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", h.Project, Content("First"), default);
        await h.Db.SaveChangesAsync();
        var row = await h.Db.ChatMessages.SingleAsync();
        row.SentRevision = 1;
        row.ExternalId = "1700000000.0001";
        row.Attempts = 3;
        row.NextAttemptAt = Now.AddHours(1);
        row.LastRefusal = "ratelimited";
        await h.Db.SaveChangesAsync();

        h.Time.Advance(TimeSpan.FromHours(2));
        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", h.Project, Content("Second"), default);
        await h.Db.SaveChangesAsync();

        var after = await h.Db.ChatMessages.SingleAsync();
        Assert.Equal(2, after.Revision);
        Assert.Equal(1, after.SentRevision);
        Assert.Equal(Now.AddHours(2), after.PendingSince);
        Assert.Equal(Now, after.QueuedAt);
        Assert.Null(after.NextAttemptAt);
        Assert.Equal(0, after.Attempts);
        Assert.Null(after.LastRefusal);
        Assert.Equal("1700000000.0001", after.ExternalId);
    }

    [Fact]
    public async Task AChangeToARowStillWaiting_KeepsItsDayAndItsBackoff()
    {
        var h = await WithOneRoomAsync();
        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", h.Project, Content("First"), default);
        await h.Db.SaveChangesAsync();
        var row = await h.Db.ChatMessages.SingleAsync();
        row.Attempts = 2;
        row.NextAttemptAt = Now.AddMinutes(30);
        row.LastRefusal = "ratelimited";
        await h.Db.SaveChangesAsync();

        h.Time.Advance(TimeSpan.FromHours(2));
        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", h.Project, Content("Second"), default);
        await h.Db.SaveChangesAsync();

        var after = await h.Db.ChatMessages.SingleAsync();
        Assert.Equal(2, after.Revision);
        Assert.Equal(Now, after.PendingSince);
        Assert.Equal(2, after.Attempts);
        Assert.Equal(Now.AddMinutes(30), after.NextAttemptAt);
        Assert.Equal("ratelimited", after.LastRefusal);
    }

    [Fact]
    public async Task EqualContent_ChangesNothing()
    {
        var h = await WithOneRoomAsync();
        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", h.Project, Content("Same"), default);
        await h.Db.SaveChangesAsync();
        var row = await h.Db.ChatMessages.SingleAsync();
        row.SentRevision = 1;
        await h.Db.SaveChangesAsync();

        h.Time.Advance(TimeSpan.FromHours(2));
        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", h.Project, Content("Same"), default);

        Assert.False(h.Db.ChangeTracker.HasChanges());
        var after = await h.Db.ChatMessages.SingleAsync();
        Assert.Equal(1, after.Revision);
        Assert.Equal(Now, after.PendingSince);
    }

    [Fact]
    public async Task ADroppedRow_IsLeftAlone()
    {
        var h = await WithOneRoomAsync();
        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", h.Project, Content("First"), default);
        await h.Db.SaveChangesAsync();
        var row = await h.Db.ChatMessages.SingleAsync();
        row.DroppedAt = Now;
        await h.Db.SaveChangesAsync();

        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", h.Project, Content("Second"), default);
        await h.Db.SaveChangesAsync();

        var after = await h.Db.ChatMessages.SingleAsync();
        Assert.Equal(1, after.Revision);
        Assert.Equal("First", ChatMessageContent.Read(after.Content)!.Title);
        Assert.Equal(Now, after.DroppedAt);
    }

    [Fact]
    public async Task TwoCallsBeforeOneSave_AddOneRow_AtTheLatestContent()
    {
        var h = await WithOneRoomAsync();

        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", h.Project, Content("First"), default);
        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", h.Project, Content("Second"), default);
        await h.Db.SaveChangesAsync();

        Assert.Single(h.Db.ChatSubjects);
        var row = Assert.Single(h.Db.ChatMessages);
        Assert.Equal("Second", ChatMessageContent.Read(row.Content)!.Title);
    }

    [Fact]
    public async Task ARoomAddedAfterAFirstCall_GetsNoRowFromTheRepeatCall()
    {
        var h = await WithOneRoomAsync();
        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", h.Project, Content("First"), default);
        await h.Db.SaveChangesAsync();
        var later = await h.Seed.RoomAsync(h.Workspace, "C9", changelog: true);

        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", h.Project, Content("Second"), default);
        await h.Db.SaveChangesAsync();

        Assert.DoesNotContain(h.Db.ChatMessages, m => m.RoomId == later);
        Assert.Single(h.Db.ChatMessages);
    }

    [Fact]
    public async Task ARoomAddedAfterAFirstCallThatFoundNoRoom_GetsNoRowFromTheRepeatCall()
    {
        var h = NewHarness();
        var project = await h.Seed.ProjectAsync("AER");
        var workspace = await h.Seed.WorkspaceAsync("TACME", project);
        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", project, Content("First"), default);
        await h.Db.SaveChangesAsync();
        await h.Seed.RoomAsync(workspace, "C1", changelog: true);

        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", project, Content("Second"), default);
        await h.Db.SaveChangesAsync();

        Assert.Empty(h.Db.ChatMessages);
        Assert.Single(h.Db.ChatSubjects);
    }

    [Fact]
    public void TheContent_RoundTripsAsJson_AndTheSameContentWritesTheSameText()
    {
        var content = new ChatMessageContent("Title", "Body", ["one", "two"], [new ChatLink("Open", "https://hatch.test/issues/AER-1"), new ChatLink("Plain", null)]);

        var json = content.Write();
        var back = ChatMessageContent.Read(json)!;

        Assert.Equal(json, back.Write());
        Assert.Equal(["one", "two"], back.Lines);
        Assert.Null(back.Links[1].Url);
        Assert.Null(ChatMessageContent.Read("not json"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("home.example.com")]
    public void LinksWithoutAUsableBaseUrl_AreNull_AndOff(string? baseUrl)
    {
        var links = Links(baseUrl);

        Assert.False(links.On);
        Assert.Null(links.Issue("AER-1"));
        Assert.Null(links.Ballot());
    }

    [Theory]
    [InlineData("https://hatch.test")]
    [InlineData("https://hatch.test/")]
    [InlineData("https://hatch.test///")]
    public void Links_JoinTheOriginWithoutADoubleSlash(string baseUrl)
    {
        var links = Links(baseUrl);

        Assert.True(links.On);
        Assert.Equal("https://hatch.test/issues/AER-1", links.Issue("AER-1"));
        Assert.Equal("https://hatch.test/ballot", links.Ballot());
    }

    internal static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    internal static ChatMessageContent Content(string title) => new(title, null, [], []);

    private static ChatLinks Links(string? baseUrl) =>
        new(Options.Create(new HatchOptions { PublicBaseUrl = baseUrl! }));

    private static Harness NewHarness()
    {
        var options = new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new HatchContext(options);
        var time = new FakeTimeProvider(Now);
        return new Harness(db, time, new ChatOutbox(db, time), new ChatOutboxSeed(db, Now));
    }

    private static async Task<Harness> WithOneRoomAsync()
    {
        var h = NewHarness();
        var project = await h.Seed.ProjectAsync("AER");
        var workspace = await h.Seed.WorkspaceAsync("TACME", project);
        await h.Seed.RoomAsync(workspace, "C1", changelog: true);
        return h with { Project = project, Workspace = workspace };
    }

    private sealed record Harness(HatchContext Db, FakeTimeProvider Time, ChatOutbox Outbox, ChatOutboxSeed Seed)
    {
        public int Project { get; init; }
        public int Workspace { get; init; }
    }
}

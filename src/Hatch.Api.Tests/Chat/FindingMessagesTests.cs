using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// What a goose finding says in a chat room: one message when it opens, edited
/// only when its sentence changes, edited once more to say it ended. Everything
/// runs through <see cref="FindingCheck.RunAsync"/> and a save, as the goose job
/// drives it, with a check whose candidates a test sets. Nothing calls Slack.
/// </summary>
public class FindingMessagesTests
{
    private static readonly DateTimeOffset Now = ChatOutboxTests.Now;

    [Fact]
    public async Task AnOpenFinding_QueuesOneRowForEachRoomThatCarriesIt_AndNoneForARoomThatDoesNot()
    {
        var h = await NewHarnessAsync();
        var carrying = await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        await h.Seed.RoomAsync(h.Workspace, "C2", problems: false, changelog: true);
        var second = await h.Seed.RoomAsync(h.Workspace, "C3", problems: true);

        h.Check.Candidates = [Candidate("a", "t1", h.Project)];
        await h.SweepAsync();

        var rows = await h.RowsAsync();
        Assert.Equal([carrying, second], rows.Select(r => r.RoomId).OrderBy(id => id));
        Assert.All(rows, r => Assert.Equal(h.Project, r.ProjectId));
        Assert.All(rows, r => Assert.Equal("problem", r.Kind));
        Assert.All(rows, r => Assert.Equal(1, r.Revision));

        var content = ChatMessageContent.Read(rows[0].Content)!;
        Assert.Equal("the sentence", content.Title);
        Assert.Equal(["Since 2026-11-23 12:00 UTC"], content.Lines);
    }

    [Fact]
    public async Task TwoSweepsAMinuteApart_ChangeNothing()
    {
        var h = await NewHarnessAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        h.Check.Candidates = [Candidate("a", "t1", h.Project)];
        await h.SweepAsync();

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(1, row.Revision);
        Assert.Single(await h.Db.ChatSubjects.ToListAsync());
    }

    [Fact]
    public async Task ANewSentence_IsRevisionTwo_OnTheSameRow()
    {
        var h = await NewHarnessAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        h.Check.Candidates = [Candidate("a", "t1", h.Project)];
        await h.SweepAsync();

        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.Check.Sentence = "a different sentence";
        await h.SweepAsync();

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(2, row.Revision);
        Assert.Equal("a different sentence", ChatMessageContent.Read(row.Content)!.Title);
    }

    [Fact]
    public async Task ClosingTheFinding_IsAnEndedRevision_AndALaterSweepLeavesItThere()
    {
        var h = await NewHarnessAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        h.Check.Candidates = [Candidate("a", "t1", h.Project)];
        await h.SweepAsync();

        h.Time.Advance(TimeSpan.FromMinutes(5));
        h.Check.Candidates = [];
        await h.SweepAsync();

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(2, row.Revision);
        var content = ChatMessageContent.Read(row.Content)!;
        Assert.Equal("Ended: the sentence", content.Title);
        Assert.Equal(["Began 2026-11-23 12:00 UTC", "Ended 2026-11-23 12:05 UTC"], content.Lines);

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();

        Assert.Equal(2, (await h.RowsAsync()).Single().Revision);
    }

    [Fact]
    public async Task AFindingClosedByHandAMinuteAgo_AsALostSweepLeavesIt_StillGetsItsEndedRevision()
    {
        var h = await NewHarnessAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        h.Check.Candidates = [Candidate("a", "t1", h.Project)];
        await h.SweepAsync();

        // A sweep that closed the finding and saved, but threw before the ended message was queued.
        h.Time.Advance(TimeSpan.FromMinutes(5));
        (await h.Db.GooseFindings.SingleAsync()).ClosedAt = h.Time.GetUtcNow();
        await h.Db.SaveChangesAsync();
        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.Check.Candidates = [];
        await h.SweepAsync();

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(2, row.Revision);
        Assert.StartsWith("Ended: ", ChatMessageContent.Read(row.Content)!.Title);
    }

    [Fact]
    public async Task AFindingClosedLongAgo_IsNotRetried()
    {
        var h = await NewHarnessAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        h.Check.Candidates = [Candidate("a", "t1", h.Project)];
        await h.SweepAsync();

        (await h.Db.GooseFindings.SingleAsync()).ClosedAt = h.Time.GetUtcNow().AddMinutes(1);
        await h.Db.SaveChangesAsync();
        h.Time.Advance(TimeSpan.FromHours(2));
        h.Check.Candidates = [];
        await h.SweepAsync();

        Assert.Equal(1, (await h.RowsAsync()).Single().Revision);
    }

    [Fact]
    public async Task AClosedFindingThatWasNeverAnnounced_QueuesNothing()
    {
        var h = await NewHarnessAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        h.Db.GooseFindings.Add(new EfHatchGooseFinding
        {
            Check = h.Check.Name, Subject = "a", Token = "t1", ProjectId = h.Project, BeganAt = Now.AddHours(-3),
            OpenedAt = Now.AddHours(-2), ClosedAt = Now.AddMinutes(-1), Sentence = "old news",
        });
        await h.Db.SaveChangesAsync();

        await h.SweepAsync();

        Assert.Empty(await h.RowsAsync());
        Assert.Empty(await h.Db.ChatSubjects.ToListAsync());
    }

    [Fact]
    public async Task AFindingWithNoProject_ReachesOnlyTheRoomsThatTakeUnownedProblems()
    {
        var h = await NewHarnessAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        var unowned = await h.Seed.RoomAsync(h.Workspace, "C2", problems: true, unownedProblems: true);
        var unownedOnly = await h.Seed.RoomAsync(h.Workspace, "C3", unownedProblems: true);

        h.Check.Candidates = [Candidate("a", "t1", null)];
        await h.SweepAsync();

        var rows = await h.RowsAsync();
        Assert.Equal([unowned, unownedOnly], rows.Select(r => r.RoomId).OrderBy(id => id));
        Assert.All(rows, r => Assert.Null(r.ProjectId));
    }

    [Fact]
    public async Task AFindingOnOneProject_PostsOnlyInTheWorkspaceBoundToIt_AndARoomWithoutUnownedProblemsHearsNothingOfAProjectlessOne()
    {
        var h = await NewHarnessAsync();
        var other = await h.Seed.ProjectAsync("OTH");
        var otherWorkspace = await h.Seed.WorkspaceAsync("TOTHER", other);
        var mine = await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        await h.Seed.RoomAsync(otherWorkspace, "C9", problems: true);

        h.Check.Candidates = [Candidate("a", "t1", h.Project), Candidate("b", "t1", null)];
        await h.SweepAsync();

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(mine, row.RoomId);
        Assert.Equal(h.Workspace, row.WorkspaceId);
    }

    [Fact]
    public async Task TheIssueLink_AppearsOnlyWhenTheFindingNamesAnIssue_AndNoLinkAtAllWithoutAPublicBaseUrl()
    {
        var h = await NewHarnessAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);

        h.Check.Candidates = [Candidate("a", "AER-5", h.Project), Candidate("b", "not-an-issue", h.Project), Candidate("c", "OTH-9", h.Project)];
        await h.SweepAsync();

        var contents = (await h.RowsAsync()).ToDictionary(r => ChatMessageContent.Read(r.Content)!.Title + r.Subject, r => ChatMessageContent.Read(r.Content)!);
        var links = (await h.RowsAsync()).Select(r => ChatMessageContent.Read(r.Content)!.Links.Select(l => (l.Label, l.Url)).ToList()).ToList();

        Assert.Equal(3, links.Count);
        Assert.Single(links, l => l.SequenceEqual([("AER-5", "https://hatch.test/issues/AER-5"), ("Goose", "https://hatch.test/goose")]));
        Assert.Equal(2, links.Count(l => l.SequenceEqual([("Goose", "https://hatch.test/goose")])));
        Assert.Equal(3, contents.Count);

        var bare = await NewHarnessAsync(baseUrl: null);
        await bare.Seed.RoomAsync(bare.Workspace, "C1", problems: true);
        bare.Check.Candidates = [Candidate("a", "AER-5", bare.Project)];
        await bare.SweepAsync();

        Assert.Empty(ChatMessageContent.Read((await bare.RowsAsync()).Single().Content)!.Links);
    }

    [Fact]
    public async Task AThrowInReadAsync_QueuesNothing()
    {
        var h = await NewHarnessAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        h.Check.Throws = new InvalidOperationException("unreadable");

        await Assert.ThrowsAsync<InvalidOperationException>(h.SweepAsync);

        Assert.Empty(await h.RowsAsync());
        Assert.Empty(await h.Db.ChatSubjects.ToListAsync());
    }

    [Fact]
    public async Task AFindingAboutARoomsSends_IsNotQueuedForThatRoom_ButReachesAnother()
    {
        var h = await NewHarnessAsync();
        var failing = await h.Seed.RoomAsync(h.Workspace, "C1", unownedProblems: true);
        var other = await h.Seed.RoomAsync(h.Workspace, "C2", unownedProblems: true);
        (await h.Db.ChatRooms.SingleAsync(r => r.Id == failing)).SendFailingSince = Now.AddHours(-2);
        await h.Db.SaveChangesAsync();

        var check = new ChatSendsFailingCheck(h.Db, Options.Create(new HatchOptions()), h.Time, h.Messages);
        await check.RunAsync(CancellationToken.None);
        await h.Db.SaveChangesAsync();

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(other, row.RoomId);
    }

    [Fact]
    public async Task AFindingAboutAWorkspacesSends_IsNotQueuedForAnyRoomInIt_ButReachesAnotherWorkspace()
    {
        var h = await NewHarnessAsync();
        var failingWorkspace = h.Workspace;
        await h.Seed.RoomAsync(failingWorkspace, "C1", unownedProblems: true);
        await h.Seed.RoomAsync(failingWorkspace, "C2", unownedProblems: true);
        var elsewhere = await h.Seed.WorkspaceAsync("TELSE");
        var reached = await h.Seed.RoomAsync(elsewhere, "C3", unownedProblems: true);
        (await h.Db.ChatWorkspaces.SingleAsync(w => w.Id == failingWorkspace)).SendFailingSince = Now.AddHours(-2);
        await h.Db.SaveChangesAsync();

        var check = new ChatSendsFailingCheck(h.Db, Options.Create(new HatchOptions()), h.Time, h.Messages);
        await check.RunAsync(CancellationToken.None);
        await h.Db.SaveChangesAsync();

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(reached, row.RoomId);
    }

    [Fact]
    public async Task TheChatSendsSentence_NamesTheMomentItBegan_SoTheMessageIsNotEditedEverySweep()
    {
        var h = await NewHarnessAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", unownedProblems: true);
        var elsewhere = await h.Seed.WorkspaceAsync("TELSE");
        await h.Seed.RoomAsync(elsewhere, "C2", unownedProblems: true);
        (await h.Db.ChatWorkspaces.SingleAsync(w => w.Id == h.Workspace)).SendFailingSince = Now.AddHours(-2);
        await h.Db.SaveChangesAsync();

        var check = new ChatSendsFailingCheck(h.Db, Options.Create(new HatchOptions()), h.Time, h.Messages);
        for (var i = 0; i < 3; i++)
        {
            await check.RunAsync(CancellationToken.None);
            await h.Db.SaveChangesAsync();
            h.Time.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(1, (await h.RowsAsync()).Single().Revision);
    }

    [Fact]
    public async Task OneSendRun_PostsTheFinding_AndALaterOneEditsItToEnded()
    {
        var h = await NewHarnessAsync();
        await h.Seed.RoomAsync(h.Workspace, "C1", problems: true);
        await h.Send.SealAsync(h.Workspace);

        h.Check.Candidates = [Candidate("a", "t1", h.Project)];
        await h.SweepAsync();
        await h.Send.TickAsync();

        h.Time.Advance(TimeSpan.FromMinutes(5));
        h.Check.Candidates = [];
        await h.SweepAsync();
        await h.Send.TickAsync();

        Assert.Equal(["post", "update"], h.Send.Provider.Sends.Select(s => s.Kind));
        Assert.Equal("the sentence", h.Send.Provider.Sends[0].Content.Title);
        Assert.Equal("Ended: the sentence", h.Send.Provider.Sends[1].Content.Title);
        Assert.Equal("ts-1", h.Send.Provider.Sends[1].MessageId);
    }

    [Fact]
    public void TheSubjectKey_IsStable_NamesTheOccurrence_AndFitsTheLimit()
    {
        var one = ChatSubjectKey.Occurrence("finding", "check", "subject", "token", 1L);

        Assert.Equal(one, ChatSubjectKey.Occurrence("finding", "check", "subject", "token", 1L));
        Assert.NotEqual(one, ChatSubjectKey.Occurrence("finding", "check", "subject", "token", 2L));
        Assert.NotEqual(one, ChatSubjectKey.Occurrence("finding", "check", "subject", "other", 1L));
        Assert.Equal("finding:".Length + 22, one.Length);
        Assert.True(one.Length <= EfHatchChatSubject.MaxSubjectLength);
        Assert.DoesNotContain('+', one);
        Assert.DoesNotContain('/', one);
        Assert.DoesNotContain('=', one);
    }

    [Fact]
    public void When_IsTheMinuteInUtc()
    {
        Assert.Equal("2026-11-23 12:05 UTC", ChatText.When(new DateTimeOffset(2026, 11, 23, 14, 5, 59, TimeSpan.FromHours(2))));
    }

    private static FindingCandidate Candidate(string subject, string token, int? project) => new(subject, token, project);

    private static async Task<Harness> NewHarnessAsync(string? baseUrl = "https://hatch.test")
    {
        var send = new ChatSendJobTests.Harness(new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        var project = await send.Seed.ProjectAsync("AER");
        var workspace = await send.Seed.WorkspaceAsync("TACME", project);
        send.Project = project;

        var links = new ChatLinks(Options.Create(new HatchOptions { PublicBaseUrl = baseUrl! }));
        var messages = new FindingMessages(send.Outbox, links, send.Db);
        return new Harness(send, messages, new StubCheck(send.Db, send.Time, messages)) { Project = project, Workspace = workspace };
    }

    private sealed record Harness(ChatSendJobTests.Harness Send, FindingMessages Messages, StubCheck Check)
    {
        public int Project { get; init; }
        public int Workspace { get; init; }

        public HatchContext Db => Send.Db;
        public Microsoft.Extensions.Time.Testing.FakeTimeProvider Time => Send.Time;
        public ChatOutboxSeed Seed => Send.Seed;

        /// <summary>One sweep, as the goose job drives a check: run it, then save.</summary>
        public async Task SweepAsync()
        {
            await Check.RunAsync(CancellationToken.None);
            await Db.SaveChangesAsync();
        }

        public Task<List<EfHatchChatMessage>> RowsAsync() =>
            Db.ChatMessages.IgnoreQueryFilters().OrderBy(m => m.Id).ToListAsync();
    }

    /// <summary>A <see cref="FindingCheck"/> whose candidates a test sets, with no bound, so a candidate opens a finding at once.</summary>
    private sealed class StubCheck(HatchContext db, TimeProvider time, FindingMessages messages) : FindingCheck(db, time, messages)
    {
        public override string Name => "stub-check";

        public IReadOnlyList<FindingCandidate> Candidates { get; set; } = [];

        public string Sentence { get; set; } = "the sentence";

        public Exception? Throws { get; set; }

        protected override Task<(string Looked, TimeSpan Bound, IReadOnlyList<FindingCandidate> Candidates)> ReadAsync(
            DateTimeOffset now, CancellationToken ct) =>
            Throws is { } e ? throw e : Task.FromResult(("looked", TimeSpan.Zero, Candidates));

        protected override Task<(string Sentence, IReadOnlyList<GooseFindingEvidence> Evidence)> DescribeAsync(
            FindingCandidate candidate, DateTimeOffset began, CancellationToken ct) =>
            Task.FromResult((Sentence, (IReadOnlyList<GooseFindingEvidence>)[]));
    }
}

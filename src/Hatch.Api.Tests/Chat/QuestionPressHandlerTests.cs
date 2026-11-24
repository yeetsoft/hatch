using Hatch.Api.Services.Auth;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// The reply a refused press gets: one, to the press's own address, and nothing
/// written for it. In memory, with <see cref="FakeChatProvider"/>; nothing calls Slack.
/// </summary>
public class QuestionPressHandlerTests
{
    private const string Address = "https://hooks.example/reply-address";
    private const string Origin = "https://hatch.example";

    [Fact]
    public async Task APressWithNoConfirmedLink_IsToldToLinkInSettings()
    {
        var t = await NewAsync();

        await t.Handler.HandleAsync(t.World.WorkspaceId, t.World.Press(member: "U404"), default);

        var (url, content) = Assert.Single(t.Provider.Replies);
        Assert.Equal(Address, url);
        Assert.Equal("Link your Slack account to answer from here", content.Title);
        Assert.Equal([new ChatLink("Settings", $"{Origin}/settings")], content.Links);
        await t.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task APersonWhoCannotSeeTheProject_IsToldSo_WithNothingNamed()
    {
        var t = await NewAsync(wallOn: true);
        var other = await new ChatOutboxSeed(t.Db, PressWorld.Now).ProjectAsync("OTH");
        t.Db.PersonScopes.Add(new EfHatchPersonScope { PersonId = t.World.Person, Mode = PersonScopeMode.Some, UpdatedBy = "test", UpdatedAt = PressWorld.Now });
        t.Db.PersonScopeProjects.Add(new EfHatchPersonScopeProject { PersonId = t.World.Person, ProjectId = other });
        await t.Db.SaveChangesAsync();
        t.Snapshot = await PressWorld.SnapshotAsync(t.Db);

        await t.Handler.HandleAsync(t.World.WorkspaceId, t.World.Press(), default);

        var (_, content) = Assert.Single(t.Provider.Replies);
        Assert.Equal("Your Hatch account cannot see this project", content.Title);
        Assert.Empty(content.Links);
        var written = content.Write();
        Assert.DoesNotContain("HA-1", written);
        Assert.DoesNotContain("Rank weighting", written);
        Assert.DoesNotContain("leaf-weighted", written);
        await t.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AnAnsweredQuestion_SaysWhoAnsweredAndWithWhat()
    {
        var t = await NewAsync();
        await AnswerAsync(t, "Grace", ActorKind.Person, "child-weighted");

        await t.Handler.HandleAsync(t.World.WorkspaceId, t.World.Press(), default);

        var (url, content) = Assert.Single(t.Provider.Replies);
        Assert.Equal(Address, url);
        Assert.Equal("This question already has an answer", content.Title);
        Assert.Equal(["Grace answered: child-weighted"], content.Lines);
        Assert.Equal([new ChatLink("HA-1", $"{Origin}/issues/HA-1")], content.Links);
        await t.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AQuestionOnlyAProgramAnswered_SaysAProgramDid_ForAMergeLevelQuestion_AndNamesTheKeyForAHoldOne()
    {
        var merge = await NewAsync(level: QuestionLevels.MergeName);
        await AnswerAsync(merge, "hatch-agent", ActorKind.Key, "leaf-weighted");
        await merge.Handler.HandleAsync(merge.World.WorkspaceId, merge.World.Press(), default);
        Assert.Equal(["A program answered: leaf-weighted"], Assert.Single(merge.Provider.Replies).Content.Lines);

        var hold = await NewAsync(level: QuestionLevels.HoldName);
        await AnswerAsync(hold, "hatch-agent", ActorKind.Key, "leaf-weighted");
        await hold.Handler.HandleAsync(hold.World.WorkspaceId, hold.World.Press(), default);
        Assert.Equal(["hatch-agent answered: leaf-weighted"], Assert.Single(hold.Provider.Replies).Content.Lines);
    }

    [Fact]
    public async Task AWorkThatWentAhead_IsToldSo_WithALinkToTheBallot()
    {
        var t = await NewAsync(level: QuestionLevels.MergeName, terminal: true);

        await t.Handler.HandleAsync(t.World.WorkspaceId, t.World.Press(), default);

        var (_, content) = Assert.Single(t.Provider.Replies);
        Assert.Equal("The work already went ahead on this question", content.Title);
        Assert.Equal([new ChatLink("Ballot", $"{Origin}/ballot")], content.Links);
        await t.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AButtonOnAMessageHatchDidNotSend_IsToldSo_AndIsNeverTiedToAnIssue()
    {
        var t = await NewAsync();

        await t.Handler.HandleAsync(t.World.WorkspaceId, t.World.Press() with { MessageId = "1.1" }, default);

        var (_, content) = Assert.Single(t.Provider.Replies);
        Assert.Equal("This button no longer works", content.Title);
        Assert.Equal([new ChatLink("Ballot", $"{Origin}/ballot")], content.Links);
        Assert.DoesNotContain("HA-1", content.Write());
        await t.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AnOptionNotOnTheQuestion_IsToldSo_WithALinkToTheIssue()
    {
        var t = await NewAsync();

        await t.Handler.HandleAsync(t.World.WorkspaceId, t.World.Press(place: 9), default);

        var (_, content) = Assert.Single(t.Provider.Replies);
        Assert.Equal("This button no longer works", content.Title);
        Assert.Equal([new ChatLink("HA-1", $"{Origin}/issues/HA-1")], content.Links);
        await t.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task APressThatAnswered_GetsNoReply()
    {
        var t = await NewAsync();

        await t.Handler.HandleAsync(t.World.WorkspaceId, t.World.Press(1), default);

        Assert.Empty(t.Provider.Replies);
        Assert.Equal(1, await t.Db.Comments.CountAsync(c => c.Kind == EfHatchComment.Answer));
    }

    [Fact]
    public async Task APressOnAnotherKindOfButton_GetsNoReply()
    {
        var t = await NewAsync();

        await t.Handler.HandleAsync(t.World.WorkspaceId, t.World.Press() with { ActionId = "approve" }, default);

        Assert.Empty(t.Provider.Replies);
        await t.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AReplyThatCannotBeSent_IsLoggedWithoutItsAddress_AndNotTriedAgain()
    {
        var t = await NewAsync();
        t.Provider.ReplyScript = (_, _) => ChatSendResult.Failed(ChatSendOutcome.RoomRefused, "Slack refused: used_url");

        await t.Handler.HandleAsync(t.World.WorkspaceId, t.World.Press(member: "U404"), default);

        Assert.Single(t.Provider.Replies);
        var line = Assert.Single(t.Log.Lines);
        Assert.Contains("Slack refused: used_url", line);
        Assert.DoesNotContain("reply-address", line);
        await t.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AReplyThatThrows_WithTheAddressInItsMessage_LeavesTheHandlerReturning_AndLogsTheTypeAlone()
    {
        var t = await NewAsync();
        t.Provider.ReplyScript = (url, _) => throw new HttpRequestException($"could not reach {url}");

        await t.Handler.HandleAsync(t.World.WorkspaceId, t.World.Press(member: "U404"), default);

        Assert.Single(t.Provider.Replies);
        var line = Assert.Single(t.Log.Lines);
        Assert.Contains(nameof(HttpRequestException), line);
        Assert.DoesNotContain("reply-address", line);
        Assert.DoesNotContain("hooks.example", line);
        await t.AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task ARefusedPress_WithNoPublicAddress_NamesThePageWithNoLink()
    {
        var t = await NewAsync(origin: null);

        await t.Handler.HandleAsync(t.World.WorkspaceId, t.World.Press(member: "U404"), default);

        var (_, content) = Assert.Single(t.Provider.Replies);
        var link = Assert.Single(content.Links);
        Assert.Equal("Settings", link.Label);
        Assert.Null(link.Url);
    }

    [Fact]
    public async Task ACancelledPress_StillCancels()
    {
        var t = await NewAsync();
        using var cts = new CancellationTokenSource();
        t.Provider.ReplyScript = (_, _) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => t.Handler.HandleAsync(t.World.WorkspaceId, t.World.Press(member: "U404"), cts.Token));
    }

    private static async Task AnswerAsync(Theatre t, string author, string kind, string body)
    {
        t.Db.Comments.Add(new EfHatchComment
        {
            IssueId = t.World.IssueId, Author = author, AuthorKind = kind, Body = body, Kind = EfHatchComment.Answer,
            AnswersId = t.World.QuestionId, CreatedAt = PressWorld.Now,
        });
        await t.Db.SaveChangesAsync();
        t.Snapshot = await PressWorld.SnapshotAsync(t.Db);
    }

    private static async Task<Theatre> NewAsync(
        string level = QuestionLevels.HoldName, bool terminal = false, bool wallOn = false, string? origin = Origin)
    {
        var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var appDb = RosterSeed.NewAppDb();
        var world = await PressWorld.SeedAsync(db, appDb, level, terminal);
        var provider = new FakeChatProvider();
        var log = new CapturingLogger();
        var links = new ChatLinks(Options.Create(new HatchOptions { PublicBaseUrl = origin }));
        var handler = new QuestionPressHandler(PressWorld.Presses(db, appDb, wallOn), provider, links, log);

        return new Theatre(db, world, provider, log, handler) { Snapshot = await PressWorld.SnapshotAsync(db) };
    }

    private sealed class Theatre(HatchContext db, PressWorld world, FakeChatProvider provider, CapturingLogger log, QuestionPressHandler handler)
    {
        public HatchContext Db { get; } = db;
        public PressWorld World { get; } = world;
        public FakeChatProvider Provider { get; } = provider;
        public CapturingLogger Log { get; } = log;
        public QuestionPressHandler Handler { get; } = handler;

        /// <summary>The board as it stood before the press.</summary>
        public string Snapshot { get; set; } = "";

        public async Task AssertNothingWrittenAsync() => Assert.Equal(Snapshot, await PressWorld.SnapshotAsync(Db));
    }

    private sealed class CapturingLogger : ILogger<QuestionPressHandler>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{formatter(state, exception)} {exception}");
    }
}

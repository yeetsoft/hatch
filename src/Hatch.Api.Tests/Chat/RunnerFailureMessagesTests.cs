using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// What a runner failure says in a chat room, apart from the heartbeat that
/// opens it: the words for each kind, who hears it, what it leaves out and how
/// it is escaped. The heartbeat's own cases are in <c>RunnerFailuresTests</c>.
/// Nothing calls Slack.
/// </summary>
public class RunnerFailureMessagesTests
{
    private static readonly DateTimeOffset Now = ChatOutboxTests.Now;

    private const string Origin = "https://hatch.example.test";
    private const string RunnerName = "mike-laptop";
    private const string Tree = "C:/work/secret-client/repo";

    /// <summary>The messages over this context, for a test that builds a controller of its own.</summary>
    internal static RunnerFailureMessages For(HatchContext db, TimeProvider time, string publicBaseUrl = Origin) =>
        new(new ChatOutbox(db, time), new ChatLinks(Options.Create(new HatchOptions { PublicBaseUrl = publicBaseUrl })));

    [Fact]
    public void EveryKind_HasItsOwnWords()
    {
        foreach (var kind in RunnerFailureKinds.All)
        {
            Assert.True(RunnerFailureWords.Has(kind), $"{kind} has no sentence");
            Assert.NotEqual(kind, RunnerFailureWords.Sentence(kind));
        }

        Assert.Equal("a-kind-from-later", RunnerFailureWords.Sentence("a-kind-from-later"));
    }

    [Fact]
    public void ARunnerLink_EscapesTheName_AndIsNoneWithNoAddress()
    {
        var on = new ChatLinks(Options.Create(new HatchOptions { PublicBaseUrl = Origin }));
        Assert.Equal($"{Origin}/runners/mike-laptop", on.Runner("mike-laptop"));
        Assert.Equal($"{Origin}/runners/a%2Fb%3Fc%23d", on.Runner("a/b?c#d"));

        Assert.Null(new ChatLinks(Options.Create(new HatchOptions { PublicBaseUrl = "" })).Runner("mike-laptop"));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task AFailureThatOpens_IsOneMessagePerRoomThatTakesUnownedProblems_ForEachKind(string kind)
    {
        var h = NewHarness();
        var workspace = await h.Seed.WorkspaceAsync("TACME");
        var first = await h.Seed.RoomAsync(workspace, "C1", unownedProblems: true);
        await h.Seed.RoomAsync(workspace, "C2", problems: true, changelog: true);
        var second = await h.Seed.RoomAsync(workspace, "C3", unownedProblems: true);

        await h.Messages.OpenedAsync(h.Row(kind), RunnerName, default);
        await h.Db.SaveChangesAsync();

        var rows = await h.RowsAsync();
        Assert.Equal([first, second], rows.Select(r => r.RoomId).OrderBy(id => id));
        Assert.All(rows, r => Assert.Null(r.ProjectId));
        Assert.All(rows, r => Assert.Equal("problem", r.Kind));
        Assert.All(rows, r => Assert.Equal(1, r.Revision));

        var content = ChatMessageContent.Read(rows[0].Content)!;
        Assert.Equal($"{RunnerName}: {RunnerFailureWords.Sentence(kind)}", content.Title);
        Assert.Equal(["Since 2026-11-23 12:00 UTC"], content.Lines);
        Assert.Equal([new ChatLink("Runner", $"{Origin}/runners/{RunnerName}")], content.Links);
    }

    public static TheoryData<string> Kinds() => new(RunnerFailureKinds.All);

    [Fact]
    public async Task ARoomThatHasNotAskedForUnownedProblems_HearsNothing()
    {
        var h = NewHarness();
        var workspace = await h.Seed.WorkspaceAsync("TACME");
        await h.Seed.RoomAsync(workspace, "C1", problems: true, changelog: true, questions: true);

        await h.Messages.OpenedAsync(h.Row(RunnerFailureKinds.Forge), RunnerName, default);
        await h.Db.SaveChangesAsync();

        Assert.Empty(await h.RowsAsync());
    }

    [Fact]
    public async Task TwoWorkspaces_EachWithSuchARoom_BothHearIt()
    {
        var h = NewHarness();
        var one = await h.Seed.RoomAsync(await h.Seed.WorkspaceAsync("TONE"), "C1", unownedProblems: true);
        var two = await h.Seed.RoomAsync(await h.Seed.WorkspaceAsync("TTWO"), "C2", unownedProblems: true);

        await h.Messages.OpenedAsync(h.Row(RunnerFailureKinds.Signing), RunnerName, default);
        await h.Db.SaveChangesAsync();

        var rows = await h.RowsAsync();
        Assert.Equal([one, two], rows.Select(r => r.RoomId).OrderBy(id => id));
        Assert.Equal(2, rows.Select(r => r.WorkspaceId).Distinct().Count());
    }

    [Fact]
    public async Task AFailureThatEnds_EditsTheSameMessage_WithWhenItBeganAndEnded()
    {
        var h = NewHarness();
        await h.Seed.RoomAsync(await h.Seed.WorkspaceAsync("TACME"), "C1", unownedProblems: true);
        var row = h.Row(RunnerFailureKinds.AgentSignedOut);
        await h.Messages.OpenedAsync(row, RunnerName, default);
        await h.Db.SaveChangesAsync();

        var ended = Now.AddMinutes(30);
        row.EndedAt = ended;
        await h.Messages.EndedAsync(row, RunnerName, ended, default);
        await h.Db.SaveChangesAsync();

        var message = Assert.Single(await h.RowsAsync());
        Assert.Equal(2, message.Revision);
        var content = ChatMessageContent.Read(message.Content)!;
        Assert.Equal($"Ended: {RunnerName}: Its agent is signed out", content.Title);
        Assert.Equal(["Began 2026-11-23 12:00 UTC", "Ended 2026-11-23 12:30 UTC"], content.Lines);
        Assert.Single(content.Links);
    }

    [Fact]
    public async Task AFailureThatComesBack_IsASecondMessage()
    {
        var h = NewHarness();
        await h.Seed.RoomAsync(await h.Seed.WorkspaceAsync("TACME"), "C1", unownedProblems: true);
        var first = h.Row(RunnerFailureKinds.Forge);
        await h.Messages.OpenedAsync(first, RunnerName, default);
        await h.Messages.EndedAsync(first, RunnerName, Now.AddMinutes(5), default);
        await h.Db.SaveChangesAsync();

        h.Time.Advance(TimeSpan.FromMinutes(10));
        await h.Messages.OpenedAsync(h.Row(RunnerFailureKinds.Forge), RunnerName, default);
        await h.Db.SaveChangesAsync();

        var rows = await h.RowsAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => r.Subject).Distinct().Count());
        Assert.Equal([2, 1], rows.Select(r => r.Revision));
    }

    [Fact]
    public async Task AFailureThatOpenedBeforeRoomsHeardOfThem_EndsInSilence()
    {
        var h = NewHarness();
        await h.Seed.RoomAsync(await h.Seed.WorkspaceAsync("TACME"), "C1", unownedProblems: true);

        // Never announced: no OpenedAsync call for it.
        var row = h.Row(RunnerFailureKinds.Forge);
        row.EndedAt = Now.AddMinutes(5);
        await h.Messages.EndedAsync(row, RunnerName, Now.AddMinutes(5), default);
        await h.Db.SaveChangesAsync();

        Assert.Empty(await h.RowsAsync());
        Assert.Empty(await h.Db.ChatSubjects.ToListAsync());
    }

    [Fact]
    public async Task TheMessage_NamesNeitherTheCheckoutNorWhatTheToolSaid()
    {
        var h = NewHarness();
        await h.Seed.RoomAsync(await h.Seed.WorkspaceAsync("TACME"), "C1", unownedProblems: true);
        var row = h.Row(RunnerFailureKinds.Forge);
        row.Why = "gh said: could not resolve host git.client.example for account jdoe";

        await h.Messages.OpenedAsync(row, RunnerName, default);
        await h.Db.SaveChangesAsync();
        var standing = Assert.Single(await h.RowsAsync()).Content;

        await h.Messages.EndedAsync(row, RunnerName, Now.AddMinutes(5), default);
        await h.Db.SaveChangesAsync();
        var ended = Assert.Single(await h.RowsAsync()).Content;

        foreach (var stored in new[] { standing, ended })
        {
            Assert.DoesNotContain("secret-client", stored);
            Assert.DoesNotContain("client.example", stored);
            Assert.DoesNotContain("jdoe", stored);
        }
    }

    [Fact]
    public async Task WithNoPublicAddress_TheMessageNamesTheRunnerWithNoLink()
    {
        var h = NewHarness(publicBaseUrl: "");
        await h.Seed.RoomAsync(await h.Seed.WorkspaceAsync("TACME"), "C1", unownedProblems: true);

        await h.Messages.OpenedAsync(h.Row(RunnerFailureKinds.Signing), RunnerName, default);
        await h.Db.SaveChangesAsync();

        var content = ChatMessageContent.Read(Assert.Single(await h.RowsAsync()).Content)!;
        Assert.Equal($"{RunnerName}: Cannot sign its commits", content.Title);
        Assert.Empty(content.Links);
    }

    [Theory]
    [InlineData("<!channel>")]
    [InlineData("<https://x.test|click>")]
    public async Task ARunnerNamedLikeAMention_ComesOutOfTheSlackFormatterEscaped(string name)
    {
        var h = NewHarness();
        await h.Seed.RoomAsync(await h.Seed.WorkspaceAsync("TACME"), "C1", unownedProblems: true);

        await h.Messages.OpenedAsync(h.Row(RunnerFailureKinds.Forge), name, default);
        await h.Db.SaveChangesAsync();

        var text = SlackChatProvider.Format(ChatMessageContent.Read(Assert.Single(await h.RowsAsync()).Content)!);
        var title = text.Split('\n')[0];
        Assert.DoesNotContain("<!channel>", title);
        Assert.DoesNotContain("<https://x.test|click>", title);
        Assert.Contains("&lt;", title);
    }

    private static Harness NewHarness(string publicBaseUrl = Origin) => new(publicBaseUrl);

    private sealed class Harness
    {
        private readonly Guid key = Guid.NewGuid();

        public Harness(string publicBaseUrl)
        {
            Time = new FakeTimeProvider(Now);
            Db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            Seed = new ChatOutboxSeed(Db, Now);
            Messages = For(Db, Time, publicBaseUrl);
        }

        public FakeTimeProvider Time { get; }
        public HatchContext Db { get; }
        public ChatOutboxSeed Seed { get; }
        public RunnerFailureMessages Messages { get; }

        /// <summary>A row opening at the clock's moment, as either writer would add it.</summary>
        public EfHatchRunnerFailure Row(string kind) => new()
        {
            ApiKeyId = key, Where = Tree, Kind = kind, BeganAt = Time.GetUtcNow(), LastReportedAt = Time.GetUtcNow(),
        };

        public Task<List<EfHatchChatMessage>> RowsAsync() =>
            Db.ChatMessages.AsNoTracking().OrderBy(m => m.Id).ToListAsync();
    }
}

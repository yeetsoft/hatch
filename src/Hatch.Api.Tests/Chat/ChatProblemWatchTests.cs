using System.Security.Cryptography;
using Hatch.Api.Common;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// What the chat sender says when the goose has stopped or the backups need a
/// person: one message per room that takes problems belonging to no project,
/// edited once to say it ended. The judging is the attention read's own, so these
/// cases move the clock, the goose's runs and the backup folder and read what was
/// queued. Nothing calls Slack.
/// </summary>
public sealed class ChatProblemWatchTests : IDisposable
{
    private static readonly DateTimeOffset Now = ChatOutboxTests.Now;

    private const string Origin = "https://hatch.example.test";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hatch-chat-problems-" + Guid.NewGuid().ToString("N"));

    public ChatProblemWatchTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    // ---- The goose ----

    [Fact]
    public async Task AGooseThatStops_IsOneMessagePerRoomThatTakesUnownedProblems_AndNoneForTheRest()
    {
        var h = NewHarness();
        var workspace = await h.Seed.WorkspaceAsync("TACME");
        var first = await h.Seed.RoomAsync(workspace, "C1", unownedProblems: true);
        await h.Seed.RoomAsync(workspace, "C2", problems: true, changelog: true);
        var second = await h.Seed.RoomAsync(workspace, "C3", unownedProblems: true);
        await h.AddRunAsync(Now);

        await h.TickAsync();
        Assert.Empty(await h.RowsAsync());

        h.Time.Advance(TimeSpan.FromMinutes(4));
        await h.TickAsync();

        var rows = await h.RowsAsync();
        Assert.Equal([first, second], rows.Select(r => r.RoomId).OrderBy(id => id));
        Assert.All(rows, r => Assert.Null(r.ProjectId));
        Assert.All(rows, r => Assert.Equal("problem", r.Kind));
        Assert.All(rows, r => Assert.Equal(1, r.Revision));

        var content = ChatMessageContent.Read(rows[0].Content)!;
        Assert.Equal("The goose has stopped", content.Title);
        Assert.Equal(["Since 2026-11-23 12:00 UTC"], content.Lines);
        Assert.Equal([new ChatLink("Goose", $"{Origin}/goose")], content.Links);

        var problem = Assert.Single(await h.Db.ChatProblems.ToListAsync());
        Assert.Equal("goose", problem.Kind);
        Assert.Equal(Now, problem.BeganAt);
        Assert.Null(problem.EndedAt);
        Assert.Equal(problem.Subject, rows[0].Subject);
    }

    [Fact]
    public async Task AGooseThatNeverSwept_BeginsAtTheMomentItWasSeenStopped_AndLeavesTheLinkOutWithNoAddress()
    {
        var h = NewHarness(publicBaseUrl: "");
        var room = await h.RoomAsync(unownedProblems: true);

        h.Time.Advance(TimeSpan.FromMinutes(4));
        await h.TickAsync();

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(room, row.RoomId);
        var content = ChatMessageContent.Read(row.Content)!;
        Assert.Equal(["Since 2026-11-23 12:04 UTC"], content.Lines);
        Assert.Empty(content.Links);
    }

    [Fact]
    public async Task AGooseThatSweepsAgain_EditsTheSameMessage_WithWhenItBeganAndEnded()
    {
        var h = NewHarness();
        await h.RoomAsync(unownedProblems: true);
        await h.AddRunAsync(Now);
        h.Time.Advance(TimeSpan.FromMinutes(4));
        await h.TickAsync();
        var began = Assert.Single(await h.RowsAsync());

        h.Time.Advance(TimeSpan.FromMinutes(6));
        await h.AddRunAsync(h.Time.GetUtcNow());
        await h.TickAsync();

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(began.Id, row.Id);
        Assert.Equal(2, row.Revision);
        var content = ChatMessageContent.Read(row.Content)!;
        Assert.Equal("Ended: The goose has stopped", content.Title);
        Assert.Equal(["Began 2026-11-23 12:00 UTC", "Ended 2026-11-23 12:10 UTC"], content.Lines);
        Assert.Equal([new ChatLink("Goose", $"{Origin}/goose")], content.Links);

        var problem = Assert.Single(await h.Db.ChatProblems.ToListAsync());
        Assert.Equal(h.Time.GetUtcNow(), problem.EndedAt);
    }

    [Fact]
    public async Task AGooseThatStopsAgain_IsASecondMessage_NotAnEditOfTheFirst()
    {
        var h = NewHarness();
        await h.RoomAsync(unownedProblems: true);
        await h.AddRunAsync(Now);
        h.Time.Advance(TimeSpan.FromMinutes(4));
        await h.TickAsync();
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.AddRunAsync(h.Time.GetUtcNow());
        await h.TickAsync();

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.TickAsync();

        var rows = await h.RowsAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => r.Subject).Distinct().Count());
        Assert.Equal(2, rows[0].Revision);
        Assert.Equal(1, rows[1].Revision);
        Assert.Equal("The goose has stopped", ChatMessageContent.Read(rows[1].Content)!.Title);

        var problems = await h.Db.ChatProblems.OrderBy(p => p.Id).ToListAsync();
        Assert.Equal(2, problems.Count);
        Assert.NotNull(problems[0].EndedAt);
        Assert.Null(problems[1].EndedAt);
    }

    [Fact]
    public async Task ATickThatChangesNothing_QueuesNothing()
    {
        var h = NewHarness();
        await h.RoomAsync(unownedProblems: true);
        await h.AddRunAsync(Now);
        h.Time.Advance(TimeSpan.FromMinutes(4));
        await h.TickAsync();

        h.Time.Advance(TimeSpan.FromSeconds(5));
        await h.TickAsync();
        h.Time.Advance(TimeSpan.FromMinutes(30));
        await h.TickAsync();

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(1, row.Revision);
        Assert.Single(await h.Db.ChatSubjects.ToListAsync());
        Assert.Single(await h.Db.ChatProblems.ToListAsync());
    }

    // ---- The backups ----

    [Fact]
    public async Task BackupsThatNeedAPerson_BeginOneMessage_AndSayWhy()
    {
        var h = NewHarness(backups: true);
        var room = await h.RoomAsync(unownedProblems: true);

        // Inside the startup grace nothing is stale yet.
        await h.TickAsync();
        Assert.Empty(await h.RowsAsync());

        h.Time.Advance(BackupWatch.StartupGrace + TimeSpan.FromMinutes(1));
        await h.TickAsync();

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(room, row.RoomId);
        Assert.Null(row.ProjectId);
        var content = ChatMessageContent.Read(row.Content)!;
        Assert.Equal("The backups need a person: no backup in the last 48 hours", content.Title);
        Assert.Equal(["Since 2026-11-23 12:06 UTC"], content.Lines);
        Assert.Empty(content.Links);

        var problem = Assert.Single(await h.Db.ChatProblems.ToListAsync());
        Assert.Equal("backups", problem.Kind);
        Assert.StartsWith("backups:", problem.Subject);
    }

    [Fact]
    public async Task ABackupSentenceThatChanges_EditsTheMessage_AndTheSameSentenceDoesNot()
    {
        var h = NewHarness(backups: true);
        await h.RoomAsync(unownedProblems: true);
        h.Time.Advance(BackupWatch.StartupGrace + TimeSpan.FromMinutes(1));
        await h.TickAsync();

        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.Status.Record(new BackupAttempt(h.Time.GetUtcNow(), BackupOutcome.Failed, "pg_dump:  no space\nleft on device", null, null));
        await h.TickAsync();
        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.TickAsync();

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(2, row.Revision);
        Assert.Equal(
            "The backups need a person: pg_dump: no space left on device",
            ChatMessageContent.Read(row.Content)!.Title);
        Assert.Equal("pg_dump: no space left on device", (await h.Db.ChatProblems.SingleAsync()).Sentence);
    }

    [Fact]
    public async Task ABackupProblem_DoesNotCloseOnAHealthyJudgement_WithoutACopyNewerThanItsStart()
    {
        var h = NewHarness(backups: true);
        await h.RoomAsync(unownedProblems: true);
        h.AddCopy(Now.AddHours(-1));
        h.Status.Record(new BackupAttempt(Now, BackupOutcome.Failed, "disk full", null, null));
        await h.TickAsync();
        Assert.Equal(1, Assert.Single(await h.RowsAsync()).Revision);

        // Another instance took a backup the folder cannot see yet, or this
        // process was told so: it judges healthy, but no copy is newer than the start.
        h.Time.Advance(TimeSpan.FromMinutes(10));
        h.Status.Record(new BackupAttempt(h.Time.GetUtcNow(), BackupOutcome.Ok, null, "hatch.sql", 10));
        await h.TickAsync();
        h.Time.Advance(TimeSpan.FromMinutes(10));
        await h.TickAsync();

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(1, row.Revision);
        Assert.Null((await h.Db.ChatProblems.SingleAsync()).EndedAt);
    }

    [Fact]
    public async Task ABackupProblem_Closes_OnceACopyNewerThanItsStartExists()
    {
        var h = NewHarness(backups: true);
        await h.RoomAsync(unownedProblems: true);
        h.AddCopy(Now.AddHours(-1));
        h.Status.Record(new BackupAttempt(Now, BackupOutcome.Failed, "disk full", null, null));
        await h.TickAsync();

        h.Time.Advance(TimeSpan.FromMinutes(10));
        h.AddCopy(h.Time.GetUtcNow());
        h.Status.Record(new BackupAttempt(h.Time.GetUtcNow(), BackupOutcome.Ok, null, "hatch.sql", 10));
        await h.TickAsync();

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(2, row.Revision);
        var content = ChatMessageContent.Read(row.Content)!;
        Assert.Equal("Ended: The backups need a person: disk full", content.Title);
        Assert.Equal(["Began 2026-11-23 12:00 UTC", "Ended 2026-11-23 12:10 UTC"], content.Lines);
        Assert.Equal(h.Time.GetUtcNow(), (await h.Db.ChatProblems.SingleAsync()).EndedAt);
    }

    [Fact]
    public async Task BackupsThatAreNotConfigured_PostNothing()
    {
        var h = NewHarness(backups: false, quietGoose: true);
        await h.RoomAsync(unownedProblems: true);

        h.Time.Advance(TimeSpan.FromDays(3));
        h.Status.Record(new BackupAttempt(h.Time.GetUtcNow(), BackupOutcome.Failed, "disk full", null, null));
        await h.TickAsync();

        Assert.Empty(await h.RowsAsync());
        Assert.Empty(await h.Db.ChatProblems.Where(p => p.Kind == "backups").ToListAsync());
    }

    [Fact]
    public async Task BackupsThatBecomeNotConfigured_EndTheRow_AndEditNothing()
    {
        var h = NewHarness(backups: true);
        await h.RoomAsync(unownedProblems: true);
        h.Status.Record(new BackupAttempt(Now, BackupOutcome.Failed, "disk full", null, null));
        await h.TickAsync();

        h.Options.BackupPath = "";
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.TickAsync();

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(1, row.Revision);
        Assert.Equal(h.Time.GetUtcNow(), (await h.Db.ChatProblems.SingleAsync()).EndedAt);
    }

    // ---- Who hears ----

    [Fact]
    public async Task WithAProjectBoundToEachOfTwoWorkspaces_NoProjectsProblemsAppear_AndARoomWithoutTheSwitchHearsNothing()
    {
        var h = NewHarness();
        var one = await h.Seed.ProjectAsync("AER");
        var two = await h.Seed.ProjectAsync("BEE");
        var acme = await h.Seed.WorkspaceAsync("TACME", one);
        var globex = await h.Seed.WorkspaceAsync("TGLOBEX", two);
        var acmeUnowned = await h.Seed.RoomAsync(acme, "C1", problems: true, unownedProblems: true);
        await h.Seed.RoomAsync(acme, "C2", problems: true);
        var globexUnowned = await h.Seed.RoomAsync(globex, "C3", unownedProblems: true);
        await h.Seed.RoomAsync(globex, "C4", problems: true, changelog: true, questions: true);
        h.Time.Advance(TimeSpan.FromMinutes(4));

        await h.TickAsync();

        var rows = await h.RowsAsync();
        Assert.Equal([acmeUnowned, globexUnowned], rows.Select(r => r.RoomId).OrderBy(id => id));
        Assert.All(rows, r => Assert.Null(r.ProjectId));
    }

    // ---- Through the sender ----

    [Fact]
    public async Task TheSender_PostsTheProblem_ThenEditsThatMessageToSayItEnded()
    {
        var h = NewHarness();
        var workspace = await h.Seed.WorkspaceAsync("TACME");
        await h.SealAsync(workspace);
        await h.Seed.RoomAsync(workspace, "C1", unownedProblems: true);
        await h.AddRunAsync(Now);
        var provider = new FakeChatProvider();
        var job = h.Job(provider, h.Watch);

        h.Time.Advance(TimeSpan.FromMinutes(4));
        await job.RunAsync(CancellationToken.None);

        var post = Assert.Single(provider.Sends);
        Assert.Equal(("post", "C1"), (post.Kind, post.ChannelId));
        Assert.Equal("The goose has stopped", post.Content.Title);

        h.Time.Advance(TimeSpan.FromMinutes(6));
        await h.AddRunAsync(h.Time.GetUtcNow());
        await job.RunAsync(CancellationToken.None);

        Assert.Equal(["post", "update"], provider.Sends.Select(s => s.Kind));
        Assert.Equal("ts-1", provider.Sends[1].MessageId);
        Assert.Equal("Ended: The goose has stopped", provider.Sends[1].Content.Title);
        Assert.Contains("Ended 2026-11-23 12:10 UTC", provider.Sends[1].Content.Lines);
    }

    [Fact]
    public async Task AWatchThatThrows_LeavesTheSendsGoing_AndKeepsWhatItHadNotSaved()
    {
        var h = NewHarness();
        var project = await h.Seed.ProjectAsync("AER");
        var workspace = await h.Seed.WorkspaceAsync("TACME", project);
        await h.SealAsync(workspace);
        await h.Seed.RoomAsync(workspace, "C1", changelog: true);
        await h.Outbox.QueueAsync(ChatMessageKind.Changelog, "issue:1", project, ChatOutboxTests.Content("Done"), default);
        await h.Db.SaveChangesAsync();

        // Something the watch had added before it failed, and never saved.
        h.Db.ChatProblems.Add(new EfHatchChatProblem { Kind = "goose", Subject = "goose:unsaved", BeganAt = Now, Sentence = "x" });

        var provider = new FakeChatProvider();
        var log = new ChatSendJobTests.CapturingLogger();
        var broken = h.WatchWith(new ThrowingOptions());
        var job = new ChatSendJob(h.Db, provider, h.Protector, new ChatSendJobTests.InProcessSendLock(), h.Time, log, broken);

        await job.RunAsync(CancellationToken.None);

        var sent = Assert.Single(provider.Sends);
        Assert.Equal("Done", sent.Content.Title);
        Assert.Contains(log.Lines, l => l.Contains("could not check the goose and the backups"));
        Assert.DoesNotContain(log.Lines, l => l.Contains("tick failed"));
        Assert.Empty(await h.Db.ChatProblems.ToListAsync());
    }

    // ---- The setup ----

    /// <param name="backups">Whether a backup folder is configured.</param>
    /// <param name="publicBaseUrl">The address links are built from.</param>
    /// <param name="quietGoose">Sets the goose's interval so far out that moving the clock never stops it.</param>
    private Harness NewHarness(bool backups = false, string publicBaseUrl = Origin, bool quietGoose = false) =>
        new(_dir, backups, publicBaseUrl, quietGoose || backups);

    private sealed class ThrowingOptions : IOptions<HatchOptions>
    {
        public HatchOptions Value => throw new InvalidOperationException("the options could not be read");
    }

    private sealed class Harness
    {
        private readonly string _dir;

        public Harness(string dir, bool backups, string publicBaseUrl, bool quietGoose)
        {
            _dir = dir;
            Time = new FakeTimeProvider(Now);
            Db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            Seed = new ChatOutboxSeed(Db, Now);
            Outbox = new ChatOutbox(Db, Time);
            Status = new BackupStatus(Time);
            Gooses = new GooseWatch(Time);
            Protector = new SecretProtector(new SecretCipher(RandomNumberGenerator.GetBytes(32), null));

            // Left alone, the goose stops once the clock is three intervals (three minutes) past its last sweep.
            Options = new HatchOptions
            {
                PublicBaseUrl = publicBaseUrl,
                BackupPath = backups ? dir : "",
                GooseIntervalSeconds = quietGoose ? 1_000_000 : new HatchOptions().GooseIntervalSeconds,
            };
            Backup = new BackupService(
                Microsoft.Extensions.Options.Options.Create(Options), new ConfigurationBuilder().Build(), new NoProcess(),
                Status, Time, NullLogger<BackupService>.Instance);
            Watch = WatchWith(Microsoft.Extensions.Options.Options.Create(Options));
        }

        public FakeTimeProvider Time { get; }
        public HatchContext Db { get; }
        public ChatOutboxSeed Seed { get; }
        public ChatOutbox Outbox { get; }
        public BackupStatus Status { get; }
        public GooseWatch Gooses { get; }
        public SecretProtector Protector { get; }
        public HatchOptions Options { get; }
        public BackupService Backup { get; }
        public ChatProblemWatch Watch { get; }

        /// <summary>A watch over this setup that reads its options from <paramref name="options"/>, to make it fail.</summary>
        public ChatProblemWatch WatchWith(IOptions<HatchOptions> options) =>
            new(Db, Outbox, new ChatLinks(options), Backup, Gooses, options, Time);

        public ChatSendJob Job(FakeChatProvider provider, ChatProblemWatch watch) =>
            new(Db, provider, Protector, new ChatSendJobTests.InProcessSendLock(), Time, new ChatSendJobTests.CapturingLogger(), watch);

        public async Task<int> RoomAsync(bool unownedProblems = false)
        {
            var workspace = await Seed.WorkspaceAsync("TACME");
            return await Seed.RoomAsync(workspace, "C1", unownedProblems: unownedProblems);
        }

        public async Task SealAsync(int workspaceId)
        {
            var workspace = await Db.ChatWorkspaces.SingleAsync(w => w.Id == workspaceId);
            Assert.True(Protector.TrySeal(
                FakeChatProvider.AcmeToken, SecretStores.ChatWorkspace(workspace.Provider, workspace.ExternalId), out var sealedToken));
            workspace.Token = sealedToken;
            await Db.SaveChangesAsync();
        }

        public async Task AddRunAsync(DateTimeOffset lastRanAt)
        {
            Db.GooseRuns.Add(new EfHatchGooseRun { FirstRanAt = lastRanAt.AddHours(-1), LastRanAt = lastRanAt, Sweeps = 3 });
            await Db.SaveChangesAsync();
        }

        public void AddCopy(DateTimeOffset at) =>
            File.WriteAllBytes(Path.Combine(_dir, BackupNames.Final(at)), "dump"u8.ToArray());

        public Task TickAsync() => Watch.TickAsync(CancellationToken.None);

        public async Task<List<EfHatchChatMessage>> RowsAsync() =>
            await Db.ChatMessages.IgnoreQueryFilters().OrderBy(m => m.Id).ToListAsync();
    }

    /// <summary>Never run: these cases read the backups and never take one.</summary>
    private sealed class NoProcess : IBackupProcess
    {
        public Task<BackupProcessResult> RunAsync(BackupProcessStart start, Stream stdout, CancellationToken ct) =>
            throw new InvalidOperationException("no backup is taken here");
    }
}

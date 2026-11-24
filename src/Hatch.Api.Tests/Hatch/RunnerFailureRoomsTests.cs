using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Chat;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What the failure rows a heartbeat keeps say in a chat room: one message when
/// a row opens, edited once when it ends, and nothing for anything else. The
/// wording and the rooms are <see cref="RunnerFailureMessagesTests"/>'s; these
/// cases drive the two writers - the loop beat and the board's pause - and
/// read what was queued. Nothing calls Slack.
/// </summary>
public partial class RunnerFailuresTests
{
    private const string Origin = "https://hatch.example.test";

    // ---- When it posts ----

    [SkippableTheory]
    [InlineData(RunnerFailureKinds.Forge)]
    [InlineData(RunnerFailureKinds.Signing)]
    [InlineData(RunnerFailureKinds.AgentSignedOut)]
    [InlineData(RunnerFailureKinds.AgentNotStarting)]
    public async Task ABeatThatOpensARow_QueuesOneMessageForEachRoom_ForEachKindABeatCanOpen(string kind)
    {
        var h = await NewAsync(await ConnectAsync());
        var rooms = await h.RoomsAsync(2);

        await h.BeatAsync(BeatOf(kind));

        var row = Assert.Single(await h.RowsAsync());
        Assert.Equal(kind, row.Kind);
        var messages = await h.MessagesAsync();
        Assert.Equal(rooms, messages.Select(m => m.RoomId!.Value).OrderBy(id => id));

        var content = Said.Of(messages[0]);
        Assert.Equal($"{Name}: {RunnerFailureWords.Sentence(kind)}", content.Title);
        Assert.Equal(["Since 2026-09-08 12:00 UTC"], content.Lines);
        Assert.Equal([$"{Origin}/runners/{Uri.EscapeDataString(Name)}"], content.Links.Select(l => l.Url));
    }

    [SkippableFact]
    public async Task TheBoardsOwnPause_QueuesItsMessage_InTheSameSaveAsTheRow()
    {
        var h = await NewAsync(await ConnectAsync());
        var key = await h.KeyAsync();
        var rooms = await h.RoomsAsync(2);
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree));

        await h.PauseAsync(key, Now.AddMinutes(30));

        Assert.Equal(RunnerFailureKinds.BoardPaused, Assert.Single(await h.RowsAsync()).Kind);
        var messages = await h.MessagesAsync();
        Assert.Equal(rooms, messages.Select(m => m.RoomId!.Value).OrderBy(id => id));
        Assert.Equal($"{Name}: Paused by the board", Said.Of(messages[0]).Title);
    }

    [SkippableFact]
    public async Task ABoardPausedRowThatOpensThroughABeat_QueuesItsMessage()
    {
        var h = await NewAsync(await ConnectAsync());
        var key = await h.KeyAsync();
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree));
        await h.RoomsAsync(1);

        // A loop restarted into a checkout whose pause stands: the new session
        // inherits it, and its first beat opens the row the old one never saved.
        var live = await h.Db.RunnerSessions.SingleAsync(s => s.ApiKeyId == key.Id && s.EndedAt == null);
        live.AgentDownUntil = Now.AddMinutes(30);
        live.AgentDownKind = RunnerAgentDownKinds.BoardPaused;
        live.AgentDownLine = "3 sessions in a row ended in error without spending a token";
        await h.Db.SaveChangesAsync();

        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree));

        Assert.Equal(RunnerFailureKinds.BoardPaused, Assert.Single(await h.RowsAsync()).Kind);
        Assert.Equal($"{Name}: Paused by the board", Said.Of(Assert.Single(await h.MessagesAsync())).Title);
    }

    [SkippableFact]
    public async Task ARowThatEnds_EditsItsMessage_AndAFailureThatComesBackIsASecondOne()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.RoomsAsync(1);
        await h.BeatAsync(Forge("fails", "no"));

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.BeatAsync(Forge("works"));

        var message = Assert.Single(await h.MessagesAsync());
        Assert.Equal(2, message.Revision);
        var ended = Said.Of(message);
        Assert.Equal($"Ended: {Name}: Cannot reach its forge", ended.Title);
        Assert.Equal(["Began 2026-09-08 12:00 UTC", "Ended 2026-09-08 12:05 UTC"], ended.Lines);

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.BeatAsync(Forge("fails", "no again"));

        var both = await h.MessagesAsync();
        Assert.Equal(2, both.Count);
        Assert.Equal(2, both.Select(m => m.Subject).Distinct().Count());
        Assert.Equal([2, 1], both.Select(m => m.Revision));
        Assert.Equal(["Since 2026-09-08 12:06 UTC"], Said.Of(both[1]).Lines);
    }

    [SkippableFact]
    public async Task ABeatThatChangesNothingTheMessageSays_QueuesNothing()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.RoomsAsync(1);
        await h.BeatAsync(Forge("fails", "token expired"));
        var before = Assert.Single(await h.MessagesAsync());

        h.Time.Advance(TimeSpan.FromSeconds(20));
        await h.BeatAsync(Forge("fails", "a different line from gh"));
        h.Time.Advance(TimeSpan.FromSeconds(20));
        await h.BeatAsync(Forge("fails", "token expired"));

        var after = Assert.Single(await h.MessagesAsync());
        Assert.Equal((before.Id, before.Revision, before.Content, before.PendingSince), (after.Id, after.Revision, after.Content, after.PendingSince));
        Assert.Equal(1, await h.Db.ChatSubjects.CountAsync());
    }

    [SkippableFact]
    public async Task RunningOutOfUsage_AndTheHourlyTokenCap_OpenNoRow_AndPostNothing()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.RoomsAsync(1);

        await h.BeatAsync(new RunnerHeartbeatRequest(
            Kind: "loop", Where: Tree, Exhausted: true, ExhaustedUntil: Now.AddHours(2),
            ThrottledUntil: Now.AddMinutes(20), MaxTokensPerHour: 100, TokensLastHour: 500));

        Assert.Empty(await h.RowsAsync());
        Assert.Empty(await h.MessagesAsync());
    }

    // ---- A runner that goes quiet, and the trim ----

    [SkippableFact]
    public async Task ARunnerThatGoesQuiet_KeepsItsRowOpenAndItsMessageAsItWas_WhileAnotherRunnersBeatsGoOn()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.RoomsAsync(1);
        await h.BeatAsync(Forge("fails", "no"));
        var before = Assert.Single(await h.MessagesAsync());

        h.Time.Advance(TimeSpan.FromMinutes(10));
        h.AsKey(await h.KeyAsync("here:/checkouts/other"));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: "here:/other"));

        Assert.Null(Assert.Single(await h.RowsAsync()).EndedAt);
        var after = Assert.Single(await h.MessagesAsync());
        Assert.Equal((before.Revision, before.Content), (after.Revision, after.Content));
    }

    [SkippableFact]
    public async Task TrimmingTheOldestEndedRows_ChangesNoMessage()
    {
        var h = await NewAsync(await ConnectAsync());
        var key = await h.KeyAsync();
        await h.RoomsAsync(1);
        await h.BeatAsync(Forge("fails", "no"));
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.BeatAsync(Forge("works"));
        var oldest = Assert.Single(await h.RowsAsync());
        var message = Assert.Single(await h.MessagesAsync());

        // Fifty more ended rows, all newer than the first, so the next ending trims it.
        for (var i = 0; i < EfHatchRunnerFailure.History; i++)
            h.Db.RunnerFailures.Add(Row(key.Id, Tree, RunnerFailureKinds.Signing, ended: Now.AddHours(1).AddMinutes(i)));
        await h.Db.SaveChangesAsync();

        h.Time.Advance(TimeSpan.FromHours(3));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree, Signing: "fails", SigningWhy: "no key"));
        h.Time.Advance(TimeSpan.FromSeconds(5));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree, Signing: "works"));

        Assert.DoesNotContain(await h.RowsAsync(), r => r.Id == oldest.Id);

        var kept = (await h.MessagesAsync()).Single(m => m.Subject == message.Subject);
        Assert.Equal((message.Id, message.Revision, message.Content), (kept.Id, kept.Revision, kept.Content));
    }

    // ---- The race ----

    [SkippableFact]
    public async Task TwoBeatsRacingToOpenARow_PostOneMessage()
    {
        var connection = await ConnectAsync();
        var h = await NewAsync(connection);
        var key = await h.KeyAsync();
        var rooms = await h.RoomsAsync(2);
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree));

        var interceptor = new BeforeFirstSave(async () =>
        {
            // The winner commits its row and its messages from its own connection.
            await using var winner = h.Connect();
            var row = Row(key.Id, Tree, RunnerFailureKinds.Forge);
            winner.RunnerFailures.Add(row);
            await RunnerFailureMessagesTests.For(winner, h.Time).OpenedAsync(row, key.Name, null, default);
            await winner.SaveChangesAsync();
        });
        await using var loser = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).AddInterceptors(interceptor).Options);
        var session = await loser.RunnerSessions.AsNoTracking().SingleAsync();

        // The loser's beat is a moment later, so its own message would name a different failure
        // and could not collide with the winner's: only dropping it keeps the rooms to one.
        await RunnerFailures.ReconcileAsync(
            loser, key.Id, key.Name, null, session, new RunnerHeartbeatRequest(Kind: "loop", Where: Tree, Forge: "fails", ForgeWhy: "later"),
            RunnerFailureMessagesTests.For(loser, h.Time), Now.AddSeconds(5), default);

        Assert.Single(await h.RowsAsync());
        var messages = await h.MessagesAsync();
        Assert.Equal(rooms, messages.Select(m => m.RoomId!.Value).OrderBy(id => id));
        Assert.Equal(1, await h.Db.ChatSubjects.CountAsync());
        Assert.All(messages, m => Assert.Equal(1, m.Revision));
        Assert.Equal(["Since 2026-09-08 12:00 UTC"], Said.Of(messages[0]).Lines);
    }

    // ---- Helpers ----

    private static RunnerHeartbeatRequest BeatOf(string kind) => kind switch
    {
        RunnerFailureKinds.Forge => Forge("fails", "gh said: no"),
        RunnerFailureKinds.Signing => new(Kind: "loop", Where: Tree, Signing: "fails", SigningWhy: "no key"),
        RunnerFailureKinds.AgentSignedOut => Agent(true, RunnerAgentDownKinds.SignedOut, "signed out"),
        RunnerFailureKinds.AgentNotStarting => Agent(true, RunnerAgentDownKinds.NotStarting, "would not start"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "a beat cannot open this kind"),
    };

    private sealed partial class Harness
    {
        /// <summary>One workspace with <paramref name="count"/> rooms that take problems belonging to no project; their ids.</summary>
        public async Task<int[]> RoomsAsync(int count)
        {
            var seed = new ChatOutboxSeed(Db, Now);
            var workspace = await seed.WorkspaceAsync("TACME");
            var rooms = new List<int>();
            for (var i = 0; i < count; i++)
                rooms.Add(await seed.RoomAsync(workspace, $"C{i}", unownedProblems: true));
            return [.. rooms];
        }

        public async Task<List<EfHatchChatMessage>> MessagesAsync() =>
            await Db.ChatMessages.AsNoTracking().OrderBy(m => m.Id).ToListAsync();
    }

    private static class Said
    {
        public static global::Hatch.Api.Services.Chat.ChatMessageContent Of(EfHatchChatMessage message) =>
            global::Hatch.Api.Services.Chat.ChatMessageContent.Read(message.Content)!;
    }
}

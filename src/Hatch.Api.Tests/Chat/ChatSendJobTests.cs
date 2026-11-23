using System.Security.Cryptography;
using Hatch.Api.Common;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// <see cref="ChatSendJob"/>: what a tick sends, in what order, and what a
/// failure costs a message. Nothing here calls Slack - <see cref="FakeChatProvider"/>
/// answers from a script - and the in-memory context has no lock to take, so
/// <see cref="ChatSendJobDatabaseTests"/> holds the two-jobs-one-database cases.
/// </summary>
public class ChatSendJobTests
{
    private static readonly DateTimeOffset Now = ChatOutboxTests.Now;

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task AQueuedMessage_IsPosted_AndItsIdStored()
    {
        var h = await WithOneRoomAsync();
        await h.QueueAsync("issue:1", "Done");

        await h.TickAsync();

        var call = Assert.Single(h.Provider.Sends);
        Assert.Equal(("post", FakeChatProvider.AcmeToken, "C1", null), (call.Kind, call.Token, call.ChannelId, call.MessageId));
        Assert.Equal("Done", call.Content.Title);
        var row = await h.OnlyRowAsync();
        Assert.Equal("ts-1", row.ExternalId);
        Assert.Equal(1, row.SentRevision);
        Assert.Equal(0, row.Attempts);
        Assert.Null(row.NextAttemptAt);
    }

    [Fact]
    public async Task AChangeAfterTheSend_EditsThatMessageInPlace()
    {
        var h = await WithOneRoomAsync();
        await h.QueueAsync("issue:1", "Done");
        await h.TickAsync();

        await h.QueueAsync("issue:1", "Done, and merged");
        await h.TickAsync();

        Assert.Equal(["post", "update"], h.Provider.Sends.Select(s => s.Kind));
        Assert.Equal("ts-1", h.Provider.Sends[1].MessageId);
        Assert.Equal("Done, and merged", h.Provider.Sends[1].Content.Title);
        var row = await h.OnlyRowAsync();
        Assert.Equal("ts-1", row.ExternalId);
        Assert.Equal(2, row.SentRevision);
    }

    [Fact]
    public async Task NothingNewToSay_SendsNothingMore()
    {
        var h = await WithOneRoomAsync();
        await h.QueueAsync("issue:1", "Done");
        await h.TickAsync();

        await h.QueueAsync("issue:1", "Done");
        await h.TickAsync();
        await h.TickAsync();

        Assert.Single(h.Provider.Sends);
    }

    [Fact]
    public async Task TwoChangesBeforeTheFirstSend_AreOnePost_WithTheLatestWords()
    {
        var h = await WithOneRoomAsync();
        await h.QueueAsync("issue:1", "First");
        await h.QueueAsync("issue:1", "Second");
        await h.QueueAsync("issue:1", "Third");

        await h.TickAsync();

        var call = Assert.Single(h.Provider.Sends);
        Assert.Equal("post", call.Kind);
        Assert.Equal("Third", call.Content.Title);
        Assert.Equal(3, (await h.OnlyRowAsync()).SentRevision);
    }

    [Fact]
    public async Task AChangeThatLandsDuringTheSend_IsLeftWaiting_ForTheNextTick()
    {
        var h = await WithOneRoomAsync();
        await h.QueueAsync("issue:1", "First");
        h.Provider.Script = call =>
        {
            // The board writes while the call is out.
            var row = h.Db.ChatMessages.Local.Single();
            row.Content = ChatOutboxTests.Content("Second").Write();
            row.Revision++;
            return ChatSendResult.Sent("ts-9");
        };

        await h.TickAsync();

        var row = await h.OnlyRowAsync();
        Assert.Equal(1, row.SentRevision);
        Assert.Equal(2, row.Revision);

        h.Provider.Script = null;
        await h.TickAsync();

        Assert.Equal("update", h.Provider.Sends[1].Kind);
        Assert.Equal("ts-9", h.Provider.Sends[1].MessageId);
        Assert.Equal("Second", h.Provider.Sends[1].Content.Title);
        Assert.Equal(2, (await h.OnlyRowAsync()).SentRevision);
    }

    [Fact]
    public async Task TheOldestMessageGoesFirst_ThenOneMorePerRoomEachTick()
    {
        var h = await NewHarnessAsync();
        var project = await h.Seed.ProjectAsync("AER");
        var workspace = await h.Seed.WorkspaceAsync("TACME", project);
        await h.SealAsync(workspace);
        await h.Seed.RoomAsync(workspace, "C1", changelog: true);
        await h.Seed.RoomAsync(workspace, "C2", changelog: true);
        h.Project = project;

        await h.QueueAsync("issue:1", "one");
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.QueueAsync("issue:2", "two");
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.QueueAsync("issue:3", "three");

        await h.TickAsync();

        // One per room: the two rooms' oldest, oldest first.
        Assert.Equal([("C1", "one"), ("C2", "one")], h.Provider.Sends.Select(s => (s.ChannelId, s.Content.Title)).OrderBy(s => s.ChannelId));
        Assert.Equal(2, h.Provider.Sends.Count);

        await h.TickAsync();
        Assert.Equal(4, h.Provider.Sends.Count);
        Assert.Equal(["two", "two"], h.Provider.Sends.Skip(2).Select(s => s.Content.Title));

        await h.TickAsync();
        Assert.Equal(["three", "three"], h.Provider.Sends.Skip(4).Select(s => s.Content.Title));
        await h.TickAsync();
        Assert.Equal(6, h.Provider.Sends.Count);
    }

    [Fact]
    public async Task RoomsAreServedInTheOrderOfTheirOldestMessage()
    {
        var h = await NewHarnessAsync();
        var project = await h.Seed.ProjectAsync("AER");
        var workspace = await h.Seed.WorkspaceAsync("TACME", project);
        await h.SealAsync(workspace);
        h.Project = project;

        // C2 is set up after the first message is queued, so it hears only the second.
        await h.Seed.RoomAsync(workspace, "C1", changelog: true);
        await h.QueueAsync("issue:1", "early");
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await h.Seed.RoomAsync(workspace, "C2", changelog: true);
        await h.QueueAsync("issue:2", "late");

        await h.TickAsync();

        Assert.Equal([("C1", "early"), ("C2", "late")], h.Provider.Sends.Select(s => (s.ChannelId, s.Content.Title)));
    }

    [Fact]
    public async Task AStuckRoom_StarvesNoOther()
    {
        var h = await NewHarnessAsync();
        var project = await h.Seed.ProjectAsync("AER");
        var workspace = await h.Seed.WorkspaceAsync("TACME", project);
        await h.SealAsync(workspace);
        await h.Seed.RoomAsync(workspace, "C1", changelog: true);
        await h.Seed.RoomAsync(workspace, "C2", changelog: true);
        h.Project = project;
        h.Provider.Script = call => call.ChannelId == "C1"
            ? ChatSendResult.Failed(ChatSendOutcome.RoomRefused, "not invited")
            : ChatSendResult.Sent("ts-ok");

        for (var i = 1; i <= 5; i++)
        {
            await h.QueueAsync($"issue:{i}", $"m{i}");
            h.Time.Advance(TimeSpan.FromSeconds(1));
        }

        await h.TickAsync();
        await h.TickAsync();
        await h.TickAsync();

        // C2 hears one a tick, in order, whatever C1 is doing. C1 is tried
        // once a tick too, on a message that is not backing off.
        Assert.Equal(["m1", "m2", "m3"], h.Provider.Sends.Where(s => s.ChannelId == "C2").Select(s => s.Content.Title));
        Assert.Equal(["m1", "m2", "m3"], h.Provider.Sends.Where(s => s.ChannelId == "C1").Select(s => s.Content.Title));
        Assert.Equal(6, h.Provider.Sends.Count);
    }

    [Fact]
    public async Task AnOutage_DelaysMessages_ThenEachIsDeliveredOnce_InOrder()
    {
        var h = await WithOneRoomAsync();
        h.Provider.Script = _ => ChatSendResult.Failed(ChatSendOutcome.Unreachable, "Slack could not be reached; try again");
        for (var i = 1; i <= 3; i++)
        {
            await h.QueueAsync($"issue:{i}", $"m{i}");
            h.Time.Advance(TimeSpan.FromSeconds(1));
        }

        // Ten, twenty, forty seconds, doubling to ten minutes and staying there.
        var expected = new[] { 10, 20, 40, 80, 160, 320, 600, 600 };
        foreach (var seconds in expected)
        {
            await h.TickAsync();
            var row = await h.RowAsync("issue:1");
            Assert.Equal(h.Time.GetUtcNow() + TimeSpan.FromSeconds(seconds), row.NextAttemptAt);

            // Too early: nothing is tried.
            var calls = h.Provider.Sends.Count;
            h.Time.Advance(TimeSpan.FromSeconds(seconds - 1));
            await h.TickAsync();
            Assert.Equal(calls, h.Provider.Sends.Count);
            h.Time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(expected.Length, h.Provider.Sends.Count);
        var workspace = await h.Db.ChatWorkspaces.SingleAsync();
        Assert.Equal(expected.Length, workspace.FailStreak);
        Assert.NotNull(workspace.SendFailingSince);

        // Slack comes back.
        h.Provider.Script = null;
        h.Provider.Sends.Clear();
        for (var i = 0; i < 3; i++)
        {
            await h.TickAsync();
            h.Time.Advance(Tick);
        }

        Assert.Equal(["m1", "m2", "m3"], h.Provider.Sends.Select(s => s.Content.Title));
        Assert.All(h.Provider.Sends, s => Assert.Equal("post", s.Kind));
        Assert.All(await h.Db.ChatMessages.ToListAsync(), r => Assert.Equal(r.Revision, r.SentRevision));
        workspace = await h.Db.ChatWorkspaces.SingleAsync();
        Assert.Equal(0, workspace.FailStreak);
        Assert.Null(workspace.SendFailingSince);
        Assert.Null(workspace.LastRefusal);
    }

    [Fact]
    public async Task ARetryAfter_HoldsTheWorkspace_NotTheMessage_AndAnotherWorkspaceSends()
    {
        var h = await NewHarnessAsync();
        var project = await h.Seed.ProjectAsync("AER");
        var acme = await h.Seed.WorkspaceAsync("TACME", project);
        var globex = await h.Seed.WorkspaceAsync("TGLOBEX", project);
        await h.SealAsync(acme, FakeChatProvider.AcmeToken);
        await h.SealAsync(globex, FakeChatProvider.GlobexToken);
        await h.Seed.RoomAsync(acme, "A1", changelog: true);
        await h.Seed.RoomAsync(acme, "A2", changelog: true);
        await h.Seed.RoomAsync(globex, "G1", changelog: true);
        h.Project = project;
        var throttled = true;
        h.Provider.Script = call => throttled && call.Token == FakeChatProvider.AcmeToken
            ? ChatSendResult.Throttled(TimeSpan.FromSeconds(7), "slow down")
            : ChatSendResult.Sent("ts-ok");
        await h.QueueAsync("issue:1", "one");

        await h.TickAsync();

        // One call for Acme (its second room is skipped), one for Globex.
        Assert.Equal(1, h.Provider.Sends.Count(s => s.Token == FakeChatProvider.AcmeToken));
        Assert.Equal(1, h.Provider.Sends.Count(s => s.Token == FakeChatProvider.GlobexToken));
        Assert.All(await h.Db.ChatMessages.Where(m => m.WorkspaceId == acme).ToListAsync(), r =>
        {
            Assert.Equal(0, r.Attempts);
            Assert.Null(r.NextAttemptAt);
            Assert.Equal(0, r.SentRevision);
        });
        Assert.Equal(Now + TimeSpan.FromSeconds(7), (await h.Db.ChatWorkspaces.SingleAsync(w => w.Id == acme)).HoldUntil);

        // Six seconds on: still waiting. Seven: it goes, and nothing was spent.
        throttled = false;
        h.Time.Advance(TimeSpan.FromSeconds(6));
        await h.TickAsync();
        Assert.Equal(1, h.Provider.Sends.Count(s => s.Token == FakeChatProvider.AcmeToken));

        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.TickAsync();
        Assert.Equal(3, h.Provider.Sends.Count(s => s.Token == FakeChatProvider.AcmeToken));
        Assert.All(await h.Db.ChatMessages.Where(m => m.WorkspaceId == acme).ToListAsync(), r => Assert.Equal(1, r.SentRevision));
    }

    [Fact]
    public async Task AThrottleWithNoSeconds_WaitsThirty()
    {
        var h = await WithOneRoomAsync();
        h.Provider.Script = _ => ChatSendResult.Throttled(null, "slow down");
        await h.QueueAsync("issue:1", "one");

        await h.TickAsync();

        Assert.Equal(Now + TimeSpan.FromSeconds(30), (await h.Db.ChatWorkspaces.SingleAsync()).HoldUntil);
    }

    [Fact]
    public async Task ARefusedToken_IsOneCallPerTick_ForTheWorkspace_AndAnotherWorkspaceIsUnaffected()
    {
        var h = await NewHarnessAsync();
        var project = await h.Seed.ProjectAsync("AER");
        var acme = await h.Seed.WorkspaceAsync("TACME", project);
        var globex = await h.Seed.WorkspaceAsync("TGLOBEX", project);
        await h.SealAsync(acme, FakeChatProvider.AcmeToken);
        await h.SealAsync(globex, FakeChatProvider.GlobexToken);
        await h.Seed.RoomAsync(acme, "A1", changelog: true);
        await h.Seed.RoomAsync(acme, "A2", changelog: true);
        await h.Seed.RoomAsync(globex, "G1", changelog: true);
        h.Project = project;
        h.Provider.Script = call => call.Token == FakeChatProvider.AcmeToken
            ? ChatSendResult.Failed(ChatSendOutcome.WorkspaceRefused, "Slack did not accept that token")
            : ChatSendResult.Sent("ts-ok");
        await h.QueueAsync("issue:1", "one");

        await h.TickAsync();

        Assert.Equal(1, h.Provider.Sends.Count(s => s.Token == FakeChatProvider.AcmeToken));
        Assert.Equal(1, h.Provider.Sends.Count(s => s.Token == FakeChatProvider.GlobexToken));
        var acmeRows = await h.Db.ChatMessages.Where(m => m.WorkspaceId == acme).OrderBy(m => m.RoomId).ToListAsync();
        Assert.Equal(1, acmeRows.Sum(r => r.Attempts));
        Assert.Single(acmeRows, r => r.Attempts == 0 && r.NextAttemptAt is null && r.LastRefusal is null);
        var workspace = await h.Db.ChatWorkspaces.SingleAsync(w => w.Id == acme);
        Assert.Equal(1, workspace.FailStreak);
        Assert.Equal(Now, workspace.SendFailingSince);
        Assert.Equal(Now + TimeSpan.FromSeconds(10), workspace.HoldUntil);
        Assert.Equal("Slack did not accept that token", workspace.LastRefusal);
        Assert.Equal(Now, workspace.LastRefusalAt);
        var other = await h.Db.ChatWorkspaces.SingleAsync(w => w.Id == globex);
        Assert.Equal(0, other.FailStreak);
        Assert.Null(other.HoldUntil);
        Assert.Null(other.LastRefusal);

        // The next tick, still inside the wait: not one more call for Acme.
        await h.TickAsync();
        Assert.Equal(1, h.Provider.Sends.Count(s => s.Token == FakeChatProvider.AcmeToken));
    }

    [Fact]
    public async Task ARoomTheAppIsNotIn_RetriesOnTheBackoff_OthersSend_AndItIsDeliveredOnceTheRefusalStops()
    {
        var h = await NewHarnessAsync();
        var project = await h.Seed.ProjectAsync("AER");
        var workspace = await h.Seed.WorkspaceAsync("TACME", project);
        await h.SealAsync(workspace);
        var shut = await h.Seed.RoomAsync(workspace, "C1", changelog: true);
        var open = await h.Seed.RoomAsync(workspace, "C2", changelog: true);
        h.Project = project;
        var refusing = true;
        h.Provider.Script = call => refusing && call.ChannelId == "C1"
            ? ChatSendResult.Failed(ChatSendOutcome.RoomRefused, "the app has not been invited to that channel")
            : ChatSendResult.Sent("ts-ok");
        await h.QueueAsync("issue:1", "one");

        await h.TickAsync();

        var row = await h.Db.ChatMessages.SingleAsync(m => m.RoomId == shut);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(Now + TimeSpan.FromSeconds(10), row.NextAttemptAt);
        Assert.Equal("the app has not been invited to that channel", row.LastRefusal);
        var room = await h.Db.ChatRooms.SingleAsync(r => r.Id == shut);
        Assert.Equal(Now, room.SendFailingSince);
        Assert.Equal("the app has not been invited to that channel", room.LastRefusal);
        var otherRoom = await h.Db.ChatRooms.SingleAsync(r => r.Id == open);
        Assert.Null(otherRoom.SendFailingSince);
        var ws = await h.Db.ChatWorkspaces.SingleAsync();
        Assert.Equal(0, ws.FailStreak);
        Assert.Null(ws.HoldUntil);
        // The other room's send, a moment later, cleared the workspace's note; the room keeps its own.
        Assert.Null(ws.LastRefusal);
        Assert.Equal(1, (await h.Db.ChatMessages.SingleAsync(m => m.RoomId == open)).SentRevision);

        // A second try on the backoff; the second wait is twenty seconds.
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.TickAsync();
        Assert.Equal(2, (await h.Db.ChatMessages.SingleAsync(m => m.RoomId == shut)).Attempts);
        Assert.Equal(Now + TimeSpan.FromSeconds(30), (await h.Db.ChatMessages.SingleAsync(m => m.RoomId == shut)).NextAttemptAt);

        // Someone invites the app.
        refusing = false;
        h.Time.Advance(TimeSpan.FromSeconds(20));
        await h.TickAsync();

        row = await h.Db.ChatMessages.SingleAsync(m => m.RoomId == shut);
        Assert.Equal(1, row.SentRevision);
        Assert.Equal(0, row.Attempts);
        Assert.Null(row.NextAttemptAt);
        Assert.Null(row.LastRefusal);
        Assert.Equal(1, h.Provider.Sends.Count(s => s.ChannelId == "C2"));
        room = await h.Db.ChatRooms.SingleAsync(r => r.Id == shut);
        Assert.Null(room.SendFailingSince);
        Assert.Null(room.LastRefusal);
        ws = await h.Db.ChatWorkspaces.SingleAsync();
        Assert.Null(ws.LastRefusal);
        Assert.Null(ws.LastRefusalAt);
    }

    [Fact]
    public async Task ARoomRefusal_IsNotedOnTheWorkspaceToo_WithoutHoldingIt()
    {
        var h = await WithOneRoomAsync();
        h.Provider.Script = _ => ChatSendResult.Failed(ChatSendOutcome.RoomRefused, "that channel is archived");
        await h.QueueAsync("issue:1", "one");

        await h.TickAsync();

        var workspace = await h.Db.ChatWorkspaces.SingleAsync();
        Assert.Equal("that channel is archived", workspace.LastRefusal);
        Assert.Equal(Now, workspace.LastRefusalAt);
        Assert.Equal(0, workspace.FailStreak);
        Assert.Null(workspace.HoldUntil);
        Assert.Null(workspace.SendFailingSince);
    }

    [Fact]
    public async Task AMessageThatWaitedADay_IsDropped_NotSent_AndALaterChangeSendsNothing()
    {
        var h = await WithOneRoomAsync();
        h.Provider.Script = _ => ChatSendResult.Failed(ChatSendOutcome.Unreachable, "down");
        await h.QueueAsync("issue:1", "one");
        await h.TickAsync();

        h.Provider.Script = null;
        h.Provider.Sends.Clear();
        h.Time.Advance(TimeSpan.FromDays(1));
        await h.TickAsync();

        var row = await h.OnlyRowAsync();
        Assert.Equal(h.Time.GetUtcNow(), row.DroppedAt);
        Assert.Empty(h.Provider.Sends);

        await h.QueueAsync("issue:1", "one, changed");
        await h.TickAsync();

        Assert.Empty(h.Provider.Sends);
        Assert.Equal(1, (await h.OnlyRowAsync()).Revision);
    }

    [Fact]
    public async Task AMessageJustUnderADay_IsStillTried()
    {
        var h = await WithOneRoomAsync();
        await h.QueueAsync("issue:1", "one");
        h.Time.Advance(TimeSpan.FromDays(1) - TimeSpan.FromSeconds(1));

        await h.TickAsync();

        Assert.Single(h.Provider.Sends);
        Assert.Null((await h.OnlyRowAsync()).DroppedAt);
    }

    [Fact]
    public async Task ContentThatCannotBeRead_IsDropped_AndNothingIsSent()
    {
        var h = await WithOneRoomAsync();
        await h.QueueAsync("issue:1", "one");
        var row = await h.OnlyRowAsync();
        row.Content = "this is not json";
        await h.Db.SaveChangesAsync();

        await h.TickAsync();

        Assert.Empty(h.Provider.Sends);
        row = await h.OnlyRowAsync();
        Assert.NotNull(row.DroppedAt);
        Assert.NotNull(row.LastRefusal);
    }

    [Fact]
    public async Task AWorkspaceWhoseTokenCannotBeRead_IsRefused_WithoutACall()
    {
        var h = await WithOneRoomAsync();
        var workspace = await h.Db.ChatWorkspaces.SingleAsync();
        workspace.Token = "v2:garbage";
        await h.Db.SaveChangesAsync();
        await h.QueueAsync("issue:1", "one");

        await h.TickAsync();

        Assert.Empty(h.Provider.Sends);
        workspace = await h.Db.ChatWorkspaces.SingleAsync();
        Assert.Equal(ChatController.TokenUnreadable, workspace.LastRefusal);
        Assert.Equal(1, workspace.FailStreak);
        Assert.Equal(1, (await h.OnlyRowAsync()).Attempts);
    }

    [Fact]
    public async Task AWorkspaceOfAnotherProvider_IsLeftAlone()
    {
        var h = await WithOneRoomAsync();
        var workspace = await h.Db.ChatWorkspaces.SingleAsync();
        workspace.Provider = "elsewhere";
        await h.Db.SaveChangesAsync();
        await h.QueueAsync("issue:1", "one");

        await h.TickAsync();

        Assert.Empty(h.Provider.Sends);
    }

    [Fact]
    public async Task AtMostFiftySendsATick_TheRestWaitForTheNext()
    {
        var h = await NewHarnessAsync();
        var project = await h.Seed.ProjectAsync("AER");
        var workspace = await h.Seed.WorkspaceAsync("TACME", project);
        await h.SealAsync(workspace);
        for (var i = 0; i < 60; i++) await h.Seed.RoomAsync(workspace, $"C{i}", changelog: true);
        h.Project = project;
        await h.QueueAsync("issue:1", "one");

        await h.TickAsync();
        Assert.Equal(ChatDelivery.MaxSendsPerTick, h.Provider.Sends.Count);

        await h.TickAsync();
        Assert.Equal(60, h.Provider.Sends.Count);
        Assert.Equal(60, h.Provider.Sends.Select(s => s.ChannelId).Distinct().Count());
    }

    [Fact]
    public async Task WithTheLockHeldElsewhere_NothingIsSent()
    {
        var h = await WithOneRoomAsync();
        await h.QueueAsync("issue:1", "one");
        await using var other = await h.Lock.TryAcquireAsync(h.Db, default);
        Assert.NotNull(other);

        await h.TickAsync();

        Assert.Empty(h.Provider.Sends);
        Assert.Equal(0, (await h.OnlyRowAsync()).SentRevision);
    }

    [Fact]
    public async Task AProviderThatThrows_DoesNotEscapeRunAsync()
    {
        var h = await WithOneRoomAsync();
        h.Provider.Script = _ => throw new InvalidOperationException("the provider broke");
        await h.QueueAsync("issue:1", "one");

        await h.TickAsync(mayFail: true);

        Assert.Single(h.Provider.Sends);
        Assert.Contains(h.Log.Lines, l => l.Contains("tick failed"));
    }

    [Fact]
    public async Task ALockThatFails_DoesNotEscapeRunAsync()
    {
        var h = await WithOneRoomAsync();
        h.Lock.Throws = new InvalidOperationException("no database");
        await h.QueueAsync("issue:1", "one");

        await h.TickAsync(mayFail: true);

        Assert.Empty(h.Provider.Sends);
    }

    [Fact]
    public async Task AWorkingSend_ClearsEveryFailingSign()
    {
        var h = await WithOneRoomAsync();
        h.Provider.Script = _ => ChatSendResult.Failed(ChatSendOutcome.WorkspaceRefused, "no");
        await h.QueueAsync("issue:1", "one");
        await h.TickAsync();
        Assert.NotNull((await h.Db.ChatWorkspaces.SingleAsync()).SendFailingSince);

        h.Provider.Script = null;
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.TickAsync();

        var workspace = await h.Db.ChatWorkspaces.SingleAsync();
        Assert.Equal(0, workspace.FailStreak);
        Assert.Null(workspace.SendFailingSince);
        Assert.Null(workspace.LastRefusal);
        Assert.Null(workspace.LastRefusalAt);
        var room = await h.Db.ChatRooms.SingleAsync();
        Assert.Null(room.SendFailingSince);
        Assert.Null(room.LastRefusal);
    }

    [Fact]
    public void Backoff_StartsAtTen_DoublesEachTime_AndStopsAtTenMinutes()
    {
        Assert.Equal(
            [10, 20, 40, 80, 160, 320, 600, 600, 600],
            Enumerable.Range(1, 9).Select(n => (int)ChatDelivery.Backoff(n).TotalSeconds));
        Assert.Equal(TimeSpan.FromMinutes(10), ChatDelivery.Backoff(int.MaxValue));
        Assert.Equal(TimeSpan.FromSeconds(10), ChatDelivery.Backoff(0));
    }

    [Fact]
    public async Task TheJob_IsNamedAndTimedAsTheScheduleExpects()
    {
        var job = (await NewHarnessAsync()).Job;

        Assert.Equal("ChatSend", job.Name);
        Assert.Equal("Hatch.Api", job.Group);
        Assert.Equal(TimeSpan.FromSeconds(5), job.Interval);
    }

    private static async Task<Harness> WithOneRoomAsync()
    {
        var h = await NewHarnessAsync();
        var project = await h.Seed.ProjectAsync("AER");
        var workspace = await h.Seed.WorkspaceAsync("TACME", project);
        await h.SealAsync(workspace);
        await h.Seed.RoomAsync(workspace, "C1", changelog: true);
        h.Project = project;
        return h;
    }

    private static Task<Harness> NewHarnessAsync()
    {
        var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return Task.FromResult(new Harness(db));
    }

    internal sealed class Harness
    {
        public Harness(HatchContext db)
        {
            Db = db;
            Time = new FakeTimeProvider(Now);
            Outbox = new ChatOutbox(db, Time);
            Seed = new ChatOutboxSeed(db, Now);
            Protector = new SecretProtector(new SecretCipher(RandomNumberGenerator.GetBytes(32), null));
            Job = new ChatSendJob(db, Provider, Protector, Lock, Time, Log);
        }

        public HatchContext Db { get; }
        public FakeTimeProvider Time { get; }
        public ChatOutbox Outbox { get; }
        public ChatOutboxSeed Seed { get; }
        public SecretProtector Protector { get; }
        public FakeChatProvider Provider { get; } = new();
        public InProcessSendLock Lock { get; } = new();
        public ChatSendJob Job { get; }
        public CapturingLogger Log { get; } = new();
        public int Project { get; set; }

        /// <summary>Seals a token for the workspace, the way the controller does, so the job can open it.</summary>
        public async Task SealAsync(int workspaceId, string token = FakeChatProvider.AcmeToken)
        {
            var workspace = await Db.ChatWorkspaces.SingleAsync(w => w.Id == workspaceId);
            Assert.True(Protector.TrySeal(token, SecretStores.ChatWorkspace(workspace.Provider, workspace.ExternalId), out var sealedToken));
            workspace.Token = sealedToken;
            await Db.SaveChangesAsync();
        }

        public async Task QueueAsync(string subject, string title)
        {
            await Outbox.QueueAsync(ChatMessageKind.Changelog, subject, Project, ChatOutboxTests.Content(title), default);
            await Db.SaveChangesAsync();
        }

        /// <summary>One tick. A tick that failed is a test failure unless the test said it would.</summary>
        public async Task TickAsync(bool mayFail = false)
        {
            await Job.RunAsync(CancellationToken.None);
            if (!mayFail) Assert.False(Log.Lines.Any(l => l.Contains("tick failed")), string.Join(Environment.NewLine, Log.Lines));
        }

        public Task<EfHatchChatMessage> OnlyRowAsync() => Db.ChatMessages.IgnoreQueryFilters().SingleAsync();

        public Task<EfHatchChatMessage> RowAsync(string subject) =>
            Db.ChatMessages.IgnoreQueryFilters().FirstAsync(m => m.Subject == subject);
    }

    internal sealed class CapturingLogger : ILogger<ChatSendJob>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{formatter(state, exception)} {exception}");
    }

    /// <summary>An in-process stand-in for the advisory lock, which in-memory EF has no way to take.</summary>
    internal sealed class InProcessSendLock : IChatSendLock
    {
        private readonly SemaphoreSlim gate = new(1, 1);

        public Exception? Throws { get; set; }

        public async Task<IAsyncDisposable?> TryAcquireAsync(HatchContext db, CancellationToken ct)
        {
            if (Throws is not null) throw Throws;
            return await gate.WaitAsync(0, ct) ? new Release(gate) : null;
        }

        private sealed class Release(SemaphoreSlim gate) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                gate.Release();
                return ValueTask.CompletedTask;
            }
        }
    }
}

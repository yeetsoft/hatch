using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Chat;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What a runner failure says to a person directly: who is sent one, what it
/// carries, how it is edited, and when it is left alone. The wording of the room's
/// message is <see cref="RunnerFailureRoomsTests"/>'s; these cases drive the two
/// writers - the loop beat and the board's pause - and read the rows queued for
/// links. Nothing calls Slack.
/// </summary>
public partial class RunnerFailuresTests
{
    // ---- Who is sent one, and what it says ----

    [SkippableTheory]
    [InlineData(RunnerFailureKinds.Forge)]
    [InlineData(RunnerFailureKinds.Signing)]
    [InlineData(RunnerFailureKinds.AgentSignedOut)]
    [InlineData(RunnerFailureKinds.AgentNotStarting)]
    public async Task AFailureThatOpens_IsOneDirectMessage_ForEachKindABeatCanOpen(string kind)
    {
        var h = await NewAsync(await ConnectAsync());
        var link = await h.LinkAsync("TACME");

        await h.BeatAsync(BeatOf(kind));

        var message = Assert.Single(await h.DirectAsync());
        Assert.Equal((link, 1), (message.LinkId, message.Revision));
        Assert.Null(message.RoomId);
        Assert.Null(message.ProjectId);

        var content = Said.Of(message);
        Assert.Equal($"{Name}: {RunnerFailureWords.Sentence(kind)}", content.Title);
        Assert.Equal(RunnerFailureWords.Advice(kind), content.Body);
        Assert.Equal(3, content.Lines.Count);
        Assert.Equal($"Checkout: {Tree}", content.Lines[0]);
        Assert.StartsWith("The tool said: ", content.Lines[1]);
        Assert.Equal("Since 2026-09-08 12:00 UTC", content.Lines[2]);
        Assert.Equal([$"{Origin}/runners/{Uri.EscapeDataString(Name)}"], content.Links.Select(l => l.Url));
    }

    [SkippableFact]
    public async Task TheBoardsOwnPause_SendsADirectMessage_InTheSameSaveAsTheRow()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.LinkAsync("TACME");
        var key = await h.KeyAsync();
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree));

        await h.PauseAsync(key, Now.AddMinutes(30));

        var content = Said.Of(Assert.Single(await h.DirectAsync()));
        Assert.Equal($"{Name}: Paused by the board", content.Title);
        Assert.Equal(RunnerFailureWords.Advice(RunnerFailureKinds.BoardPaused), content.Body);
    }

    [SkippableFact]
    public async Task TwoWorkspaces_EachGetOneMessage_ToTheirOwnConversation()
    {
        var h = await NewAsync(await ConnectAsync());
        var person = h.Actors.AddPerson("Ada").Id;
        var acme = await h.LinkAsync("TACME", person, "U1");
        var globex = await h.LinkAsync("TGLOBEX", person, "U2");

        await h.BeatAsync(Forge("fails", "no"));

        var messages = await h.DirectAsync();
        Assert.Equal([acme, globex], messages.Select(m => m.LinkId!.Value).Order());
        Assert.Equal(2, messages.Select(m => m.WorkspaceId).Distinct().Count());
    }

    [SkippableFact]
    public async Task WithTheWallUp_OnlyTheKeysOwnerIsTold()
    {
        var ada = Guid.NewGuid();
        var h = await NewAsync(await ConnectAsync(), wall: true, owner: ada);
        h.Actors.AddPerson("Ada", ada);
        var other = h.Actors.AddPerson("Bea").Id;
        var mine = await h.LinkAsync("TACME", ada, "U1");
        await h.LinkAsync("TACME", other, "U2");

        await h.BeatAsync(Forge("fails", "no"));

        Assert.Equal(mine, Assert.Single(await h.DirectAsync()).LinkId);
    }

    [SkippableFact]
    public async Task WithTheWallUp_AKeyWithNoOwner_TellsNobody()
    {
        var h = await NewAsync(await ConnectAsync(), wall: true);
        await h.LinkAsync("TACME");

        await h.BeatAsync(Forge("fails", "no"));

        Assert.Empty(await h.DirectAsync());
        Assert.Equal(0, await h.Db.ChatSubjects.CountAsync(s => s.Kind == "direct"));
    }

    [SkippableFact]
    public async Task WithTheWallDown_ARunnersFailuresReachEveryConfirmedLink_WhoseSwitchIsOn()
    {
        var h = await NewAsync(await ConnectAsync());
        var ada = await h.LinkAsync("TACME", h.Actors.AddPerson("Ada").Id, "U1");
        await h.LinkAsync("TGLOBEX", h.Actors.AddPerson("Bea").Id, "U2", runnerFailures: false);

        await h.BeatAsync(Forge("fails", "no"));
        h.AsKey(await h.KeyAsync("here:/checkouts/other"));
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: "here:/other", Forge: "fails", ForgeWhy: "no"));

        var messages = await h.DirectAsync();
        Assert.Equal([ada, ada], messages.Select(m => m.LinkId!.Value));
        Assert.StartsWith($"{Name}: ", Said.Of(messages[0]).Title);
        Assert.StartsWith("here:/checkouts/other: ", Said.Of(messages[1]).Title);
    }

    [SkippableFact]
    public async Task AWaitingLink_IsToldNothing()
    {
        var h = await NewAsync(await ConnectAsync());
        var ada = h.Actors.AddPerson("Ada").Id;
        await h.LinkAsync("TACME", ada, "U1", confirmed: false);

        await h.BeatAsync(Forge("fails", "no"));

        Assert.Empty(await h.DirectAsync());
    }

    [SkippableFact]
    public async Task ALinkWhosePersonIsGone_IsRemovedInTheSameSave_WithNothingQueuedForIt()
    {
        var h = await NewAsync(await ConnectAsync());
        var gone = await h.LinkAsync("TACME", Guid.NewGuid(), "U1");
        var kept = await h.LinkAsync("TGLOBEX", h.Actors.AddPerson("Ada").Id, "U2");

        await h.BeatAsync(Forge("fails", "no"));

        Assert.Equal(kept, Assert.Single(await h.DirectAsync()).LinkId);
        Assert.DoesNotContain(await h.Db.ChatLinks.AsNoTracking().ToListAsync(), l => l.Id == gone);
    }

    [SkippableFact]
    public async Task ALinkWhosePersonLeavesMidFailure_IsRemovedWithItsMessage_WhenTheFailureEnds()
    {
        var h = await NewAsync(await ConnectAsync());
        var person = h.Actors.AddPerson("Ada");
        await h.LinkAsync("TACME", person.Id, "U1");
        await h.BeatAsync(Forge("fails", "no"));
        Assert.Single(await h.DirectAsync());

        h.Actors.Live.Remove(person);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.BeatAsync(Forge("works"));

        Assert.Empty(await h.DirectAsync());
        Assert.Empty(await h.Db.ChatLinks.AsNoTracking().ToListAsync());
    }

    // ---- Edits ----

    [SkippableFact]
    public async Task TheEndEdit_CarriesTheCheckoutAndTheToolsLastWords()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.LinkAsync("TACME");
        await h.BeatAsync(Forge("fails", "token expired"));

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.BeatAsync(Forge("works"));

        var message = Assert.Single(await h.DirectAsync());
        Assert.Equal(2, message.Revision);
        var ended = Said.Of(message);
        Assert.Equal($"Ended: {Name}: Cannot reach its forge", ended.Title);
        Assert.Null(ended.Body);
        Assert.Equal(
            [$"Checkout: {Tree}", "The tool said: token expired", "Began 2026-09-08 12:00 UTC", "Ended 2026-09-08 12:05 UTC"],
            ended.Lines);
    }

    [SkippableFact]
    public async Task AChangedReason_EditsTheMessage_AndAnEqualOneDoesNot()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.LinkAsync("TACME");
        await h.BeatAsync(Forge("fails", "token expired"));
        var first = Assert.Single(await h.DirectAsync());

        h.Time.Advance(TimeSpan.FromSeconds(20));
        await h.BeatAsync(Forge("fails", "token expired"));
        var same = Assert.Single(await h.DirectAsync());
        Assert.Equal((first.Revision, first.Content), (same.Revision, same.Content));

        h.Time.Advance(TimeSpan.FromSeconds(20));
        await h.BeatAsync(Forge("fails", "bad credentials"));
        var changed = Assert.Single(await h.DirectAsync());
        Assert.Equal(2, changed.Revision);
        Assert.Contains("The tool said: bad credentials", Said.Of(changed).Lines);
        Assert.Contains("Since 2026-09-08 12:00 UTC", Said.Of(changed).Lines);

        // Nothing for the room changed: its message is still the first.
        h.Time.Advance(TimeSpan.FromSeconds(20));
        await h.BeatAsync(Forge("fails", "bad credentials"));
        Assert.Equal(2, Assert.Single(await h.DirectAsync()).Revision);
    }

    [SkippableFact]
    public async Task ASwitchTurnedOffLater_StillLetsTheEndGo()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.LinkAsync("TACME");
        await h.BeatAsync(Forge("fails", "no"));

        var link = await h.Db.ChatLinks.SingleAsync();
        link.RunnerFailures = false;
        await h.Db.SaveChangesAsync();

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.BeatAsync(Forge("works"));

        Assert.Equal(2, Assert.Single(await h.DirectAsync()).Revision);
    }

    [SkippableFact]
    public async Task ARemovedLink_TakesItsMessages_AndALinkConfirmedLater_IsToldNothingMore()
    {
        var h = await NewAsync(await ConnectAsync());
        var person = h.Actors.AddPerson("Ada").Id;
        await h.LinkAsync("TACME", person, "U1");
        await h.BeatAsync(Forge("fails", "no"));
        Assert.Single(await h.DirectAsync());

        h.Db.ChatLinks.RemoveRange(await h.Db.ChatLinks.ToListAsync());
        await h.Db.SaveChangesAsync();
        Assert.Empty(await h.DirectAsync());

        await h.LinkAsync("TACME", person, "U1");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.BeatAsync(Forge("fails", "different"));
        await h.BeatAsync(Forge("works"));

        Assert.Empty(await h.DirectAsync());
    }

    [SkippableFact]
    public async Task AFailureOpenBeforeThereWasALink_IsNeverSaidToOne_AndSendsNothingOnRefreshOrEnd()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.BeatAsync(Forge("fails", "no"));
        await h.LinkAsync("TACME");

        h.Time.Advance(TimeSpan.FromSeconds(20));
        await h.BeatAsync(Forge("fails", "still no"));
        h.Time.Advance(TimeSpan.FromSeconds(20));
        await h.BeatAsync(Forge("works"));

        Assert.Empty(await h.DirectAsync());
        Assert.Equal(0, await h.Db.ChatSubjects.CountAsync(s => s.Kind == "direct"));
    }

    // ---- What they leave alone ----

    [SkippableFact]
    public async Task AFailedWrite_QueuesNothing()
    {
        var connection = await ConnectAsync();
        var h = await NewAsync(connection);
        var key = await h.KeyAsync();
        await h.LinkAsync("TACME");
        await h.BeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Tree));

        var interceptor = new BeforeFirstSave(() => throw new InvalidOperationException("the save failed"));
        await using var loser = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).AddInterceptors(interceptor).Options);
        var session = await loser.RunnerSessions.AsNoTracking().SingleAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunnerFailures.ReconcileAsync(
            loser, key.Id, key.Name, null, session, Forge("fails", "no"),
            global::Hatch.Api.Tests.Chat.RunnerFailureMessagesTests.For(loser, h.Time, actors: h.Actors), Now, default));

        Assert.Empty(await h.RowsAsync());
        Assert.Empty(await h.DirectAsync());
        Assert.Equal(0, await h.Db.ChatSubjects.CountAsync());
    }

    [SkippableFact]
    public async Task RunningOutOfUsage_SendsNoDirectMessage()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.LinkAsync("TACME");

        await h.BeatAsync(new RunnerHeartbeatRequest(
            Kind: "loop", Where: Tree, Exhausted: true, ExhaustedUntil: Now.AddHours(2),
            ThrottledUntil: Now.AddMinutes(20), MaxTokensPerHour: 100, TokensLastHour: 500));

        Assert.Empty(await h.RowsAsync());
        Assert.Empty(await h.DirectAsync());
    }

    [SkippableFact]
    public async Task TheRoomsMessage_StillNamesNeitherTheCheckoutNorTheToolsWords()
    {
        var h = await NewAsync(await ConnectAsync());
        await h.RoomsAsync(1);
        await h.LinkAsync("TGLOBEX");

        await h.BeatAsync(Forge("fails", "token expired"));

        var room = Assert.Single(await h.MessagesAsync(), m => m.RoomId is not null);
        Assert.Equal(["Since 2026-09-08 12:00 UTC"], Said.Of(room).Lines);
        Assert.Null(Said.Of(room).Body);
    }

    // ---- Harness ----

    private sealed partial class Harness
    {
        /// <summary>A confirmed link in the named workspace, made on first use; its id. A person who is none is a live one.</summary>
        public async Task<int> LinkAsync(
            string workspace, Guid? person = null, string memberId = "U1", bool confirmed = true, bool runnerFailures = true)
        {
            var seed = new ChatOutboxSeed(Db, Now);
            var id = await Db.ChatWorkspaces.Where(w => w.ExternalId == workspace).Select(w => (int?)w.Id).SingleOrDefaultAsync()
                ?? await seed.WorkspaceAsync(workspace);
            var owner = person ?? Actors.AddPerson("Ada").Id;

            return await seed.LinkAsync(id, owner, memberId, confirmed, runnerFailures);
        }

        /// <summary>The direct messages, oldest first.</summary>
        public async Task<List<EfHatchChatMessage>> DirectAsync() =>
            await Db.ChatMessages.AsNoTracking().Where(m => m.LinkId != null).OrderBy(m => m.Id).ToListAsync();
    }
}

using System.Text.Json;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// A press on a question's button: who it answers as, and each way it writes
/// nothing. In memory, with the press built by hand. The race between two presses
/// is <see cref="QuestionPressesDatabaseTests"/>'s, since only a database locks.
/// </summary>
public class QuestionPressesTests
{
    [Fact]
    public async Task ALinkedPersonsPress_AnswersAsThem()
    {
        var (db, appDb, w) = await NewAsync();
        var before = await db.ChatMessages.AsNoTracking().OrderBy(m => m.RoomId).ToListAsync();

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(1), default);

        Assert.Equal(PressCase.Answered, outcome.Case);
        Assert.Equal(w.IssueKey, outcome.IssueKey);
        var answer = await db.Comments.SingleAsync(c => c.Kind == EfHatchComment.Answer);
        Assert.Equal("Ada", answer.Author);
        Assert.Equal(ActorKind.Person, answer.AuthorKind);
        Assert.Equal("child-weighted", answer.Body);
        Assert.Equal(w.QuestionId, answer.AnswersId);

        var answered = await db.IssueEvents.SingleAsync(e => e.Kind == EfHatchIssueEvent.Answered);
        Assert.Equal("Ada", answered.Actor);
        using var payload = JsonDocument.Parse(answered.Payload!);
        Assert.Equal(AnswerVia.Slack, payload.RootElement.GetProperty("via").GetString());
        Assert.Equal(w.QuestionId, payload.RootElement.GetProperty("questionId").GetInt64());

        // Both rooms' rows are edited, by the queue; the handler edits nothing itself.
        var after = await db.ChatMessages.AsNoTracking().OrderBy(m => m.RoomId).ToListAsync();
        Assert.Equal(2, after.Count);
        for (var i = 0; i < after.Count; i++)
        {
            Assert.Equal(before[i].Revision + 1, after[i].Revision);
            Assert.Contains("Ada answered: child-weighted", ChatMessageContent.Read(after[i].Content)!.Lines);
        }
    }

    [Fact]
    public async Task AcceptRecommended_AnswersWithTheRecommendedLabel()
    {
        var (db, appDb, w) = await NewAsync(options: [new("first"), new("second", Recommended: true), new("third")]);

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.PressRecommended(), default);

        Assert.Equal(PressCase.Answered, outcome.Case);
        Assert.Equal("second", (await db.Comments.SingleAsync(c => c.Kind == EfHatchComment.Answer)).Body);
    }

    [Fact]
    public async Task APressOnAnotherKindOfButton_IsIgnored()
    {
        var (db, appDb, w) = await NewAsync();
        var snapshot = await PressWorld.SnapshotAsync(db);
        var press = w.Press() with { ActionId = "approve" };

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, press, default);

        Assert.Equal(PressCase.Ignored, outcome.Case);
        Assert.Equal(snapshot, await PressWorld.SnapshotAsync(db));
    }

    [Fact]
    public async Task ANoteOnTheValue_IsNeverTheAnswer()
    {
        var (db, appDb, w) = await NewAsync();
        var press = w.Press(0) with { Value = "something else entirely" };

        await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, press, default);

        Assert.Equal("leaf-weighted", (await db.Comments.SingleAsync(c => c.Kind == EfHatchComment.Answer)).Body);
    }

    [Fact]
    public async Task NoConfirmedLink_IsCase6_AndWritesNothing()
    {
        var (db, appDb, w) = await NewAsync(link: false);
        var snapshot = await PressWorld.SnapshotAsync(db);

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.NoLink, outcome.Case);
        Assert.Equal(snapshot, await PressWorld.SnapshotAsync(db));
    }

    [Fact]
    public async Task ALinkWaitingToBeConfirmed_IsCase6()
    {
        var (db, appDb, w) = await NewAsync(link: false);
        await new ChatOutboxSeed(db, PressWorld.Now).LinkAsync(w.WorkspaceId, w.Person, PressWorld.Member, confirmed: false);
        var snapshot = await PressWorld.SnapshotAsync(db);

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.NoLink, outcome.Case);
        Assert.Equal(snapshot, await PressWorld.SnapshotAsync(db));
    }

    [Fact]
    public async Task ALinkWhosePersonWasRemoved_IsCase6()
    {
        var (db, appDb, w) = await NewAsync();
        appDb.People.Remove(await appDb.People.SingleAsync(p => p.Id == w.Person));
        await appDb.SaveChangesAsync();
        var snapshot = await PressWorld.SnapshotAsync(db);

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.NoLink, outcome.Case);
        Assert.Equal(snapshot, await PressWorld.SnapshotAsync(db));
    }

    [Fact]
    public async Task AnotherMember_IsCase6()
    {
        var (db, appDb, w) = await NewAsync();

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(member: "U2"), default);

        Assert.Equal(PressCase.NoLink, outcome.Case);
    }

    [Fact]
    public async Task TheSameMemberIdInAnotherWorkspace_ActsForNoLinkHeldInThisOne()
    {
        var (db, appDb, w) = await NewAsync();
        // A second workspace, bound to the same project, whose member U1 holds no link.
        var other = await new ChatOutboxSeed(db, PressWorld.Now).WorkspaceAsync("TGLOBEX", w.ProjectId);
        var snapshot = await PressWorld.SnapshotAsync(db);

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(other, w.Press(), default);

        Assert.Equal(PressCase.NoLink, outcome.Case);
        Assert.Equal(snapshot, await PressWorld.SnapshotAsync(db));
    }

    [Fact]
    public async Task AWallOnPersonNarrowedToAnotherProject_IsCase7()
    {
        var (db, appDb, w) = await NewAsync();
        var other = await new ChatOutboxSeed(db, PressWorld.Now).ProjectAsync("OTH");
        db.PersonScopes.Add(new EfHatchPersonScope { PersonId = w.Person, Mode = PersonScopeMode.Some, UpdatedBy = "test", UpdatedAt = PressWorld.Now });
        db.PersonScopeProjects.Add(new EfHatchPersonScopeProject { PersonId = w.Person, ProjectId = other });
        await db.SaveChangesAsync();
        var snapshot = await PressWorld.SnapshotAsync(db);

        var outcome = await PressWorld.Presses(db, appDb, wallOn: true).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.CannotSee, outcome.Case);
        Assert.Equal(snapshot, await PressWorld.SnapshotAsync(db));
    }

    [Fact]
    public async Task AWallOnPersonWhoSeesTheProject_Answers()
    {
        var (db, appDb, w) = await NewAsync();
        db.PersonScopes.Add(new EfHatchPersonScope { PersonId = w.Person, Mode = PersonScopeMode.Some, UpdatedBy = "test", UpdatedAt = PressWorld.Now });
        db.PersonScopeProjects.Add(new EfHatchPersonScopeProject { PersonId = w.Person, ProjectId = w.ProjectId });
        await db.SaveChangesAsync();

        var outcome = await PressWorld.Presses(db, appDb, wallOn: true).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.Answered, outcome.Case);
    }

    [Fact]
    public async Task WithTheWallOff_TheLocalPersonsPressAnswers()
    {
        var (db, appDb, w) = await NewAsync(link: false);
        // The local person has no row in People: they are live only while the wall is off.
        await new ChatOutboxSeed(db, PressWorld.Now).LinkAsync(w.WorkspaceId, LocalCaller.PersonId, PressWorld.Member);

        var outcome = await PressWorld.Presses(db, appDb, wallOn: false).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.Answered, outcome.Case);
        var answer = await db.Comments.SingleAsync(c => c.Kind == EfHatchComment.Answer);
        Assert.Equal(ActorKind.Person, answer.AuthorKind);
    }

    [Fact]
    public async Task AQuestionAnsweredByAPerson_IsCase8_WithWhoAndWhat()
    {
        var (db, appDb, w) = await NewAsync();
        await AnswerAsync(db, w, "Grace", ActorKind.Person, "child-weighted");
        var snapshot = await PressWorld.SnapshotAsync(db);

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.AlreadyAnswered, outcome.Case);
        Assert.Equal("Grace", outcome.AnsweredBy);
        Assert.Equal(ActorKind.Person, outcome.AnsweredByKind);
        Assert.Equal("child-weighted", outcome.AnswerBody);
        Assert.Equal(snapshot, await PressWorld.SnapshotAsync(db));
    }

    [Fact]
    public async Task AQuestionAnsweredByAKey_IsCase8()
    {
        var (db, appDb, w) = await NewAsync();
        await AnswerAsync(db, w, "hatch-agent", ActorKind.Key, "leaf-weighted");

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.AlreadyAnswered, outcome.Case);
        Assert.Equal(ActorKind.Key, outcome.AnsweredByKind);
    }

    [Theory]
    [InlineData(QuestionLevels.MergeName, "hatch-agent", ActorKind.Key, true)]
    [InlineData(QuestionLevels.MergeName, "Grace", ActorKind.Person, false)]
    [InlineData(QuestionLevels.HoldName, "hatch-agent", ActorKind.Key, false)]
    public async Task AnAlreadyAnsweredOutcome_SaysWhetherTheMessageWouldCallItAProgramsAnswer(
        string level, string author, string kind, bool lookBack)
    {
        var (db, appDb, w) = await NewAsync(level: level);
        await AnswerAsync(db, w, author, kind, "leaf-weighted");

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.AlreadyAnswered, outcome.Case);
        Assert.Equal(lookBack, outcome.LookBack);
    }

    [Fact]
    public async Task ThePressDeliveredTwice_AnswersOnce_AndTheSecondIsCase8()
    {
        var (db, appDb, w) = await NewAsync();
        var presses = PressWorld.Presses(db, appDb);

        var first = await presses.AnswerAsync(w.WorkspaceId, w.Press(), default);
        var second = await presses.AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.Answered, first.Case);
        Assert.Equal(PressCase.AlreadyAnswered, second.Case);
        Assert.Equal("Ada", second.AnsweredBy);
        Assert.Equal(1, await db.Comments.CountAsync(c => c.Kind == EfHatchComment.Answer));
        Assert.Equal(1, await db.IssueEvents.CountAsync(e => e.Kind == EfHatchIssueEvent.Answered));
    }

    [Fact]
    public async Task AMergeQuestionOnAClosedIssue_WithNoAnswer_IsCase9()
    {
        var (db, appDb, w) = await NewAsync(level: QuestionLevels.MergeName, terminal: true);
        var snapshot = await PressWorld.SnapshotAsync(db);

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.WorkWentAhead, outcome.Case);
        Assert.Equal(snapshot, await PressWorld.SnapshotAsync(db));
    }

    [Fact]
    public async Task AHoldQuestion_WithNoAnswer_IsNotALookBack_EvenOnAClosedIssue()
    {
        var (db, appDb, w) = await NewAsync(level: QuestionLevels.HoldName, terminal: true);

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.Answered, outcome.Case);
    }

    [Fact]
    public async Task AMergeQuestionOnAnOpenIssue_Answers()
    {
        var (db, appDb, w) = await NewAsync(level: QuestionLevels.MergeName, terminal: false);

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.Answered, outcome.Case);
    }

    [Fact]
    public async Task AMessageHatchDidNotSend_IsCase10()
    {
        var (db, appDb, w) = await NewAsync();
        var snapshot = await PressWorld.SnapshotAsync(db);
        var presses = PressWorld.Presses(db, appDb);

        Assert.Equal(PressCase.NotOurs, (await presses.AnswerAsync(w.WorkspaceId, w.Press() with { MessageId = "1.1" }, default)).Case);
        Assert.Equal(PressCase.NotOurs, (await presses.AnswerAsync(w.WorkspaceId, w.Press() with { ChannelId = "C9" }, default)).Case);
        // The right message id, but in the other room's channel.
        Assert.Equal(PressCase.NotOurs, (await presses.AnswerAsync(w.WorkspaceId, w.Press() with { ChannelId = PressWorld.ChannelTwo }, default)).Case);
        Assert.Equal(snapshot, await PressWorld.SnapshotAsync(db));
    }

    [Fact]
    public async Task ANotSentMessage_IsCase10_ForAQuestionThatHasNoExternalIdYet()
    {
        var (db, appDb, w) = await NewAsync();
        foreach (var row in await db.ChatMessages.ToListAsync()) row.ExternalId = null;
        await db.SaveChangesAsync();

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.NotOurs, outcome.Case);
    }

    [Fact]
    public async Task ADroppedRow_StillCounts()
    {
        var (db, appDb, w) = await NewAsync();
        foreach (var row in await db.ChatMessages.ToListAsync()) row.DroppedAt = PressWorld.Now;
        await db.SaveChangesAsync();

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.Answered, outcome.Case);
    }

    [Fact]
    public async Task AProjectUnboundAfterTheMessageWasSent_IsCase10()
    {
        var (db, appDb, w) = await NewAsync();
        // The database's cascade, done by hand: unbinding takes the project's rows in that workspace.
        db.ChatMessages.RemoveRange(await db.ChatMessages.IgnoreQueryFilters().Where(m => m.WorkspaceId == w.WorkspaceId).ToListAsync());
        await db.SaveChangesAsync();
        var snapshot = await PressWorld.SnapshotAsync(db);

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.Equal(PressCase.NotOurs, outcome.Case);
        Assert.Equal(snapshot, await PressWorld.SnapshotAsync(db));
    }

    [Fact]
    public async Task ARowInAnotherWorkspace_IsNotThisOnes()
    {
        var (db, appDb, w) = await NewAsync();
        // The same member holds a link in a second workspace, and the message is only in the first.
        var seed = new ChatOutboxSeed(db, PressWorld.Now);
        var other = await seed.WorkspaceAsync("TGLOBEX", w.ProjectId);
        await seed.LinkAsync(other, w.Person, PressWorld.Member);

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(other, w.Press(), default);

        Assert.Equal(PressCase.NotOurs, outcome.Case);
    }

    [Fact]
    public async Task AQuestionThatIsNotThere_IsCase10()
    {
        var (db, appDb, w) = await NewAsync();
        var press = w.Press() with { ActionId = ChatPressAction.Write(w.QuestionId + 1000, 0) };

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, press, default);

        Assert.Equal(PressCase.NotOurs, outcome.Case);
    }

    [Fact]
    public async Task AnOptionPlaceNotOnTheQuestion_IsCase11()
    {
        var (db, appDb, w) = await NewAsync();
        var snapshot = await PressWorld.SnapshotAsync(db);

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(place: 2), default);

        Assert.Equal(PressCase.NoSuchOption, outcome.Case);
        Assert.Equal(snapshot, await PressWorld.SnapshotAsync(db));
    }

    [Fact]
    public async Task AcceptRecommended_OnAQuestionThatRecommendsNothing_IsCase11()
    {
        var (db, appDb, w) = await NewAsync(options: [new("a"), new("b")]);
        var snapshot = await PressWorld.SnapshotAsync(db);

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.PressRecommended(), default);

        Assert.Equal(PressCase.NoSuchOption, outcome.Case);
        Assert.Equal(snapshot, await PressWorld.SnapshotAsync(db));
    }

    [Fact]
    public async Task AQuestionAskedInProse_HasNoOptionToPress()
    {
        var (db, appDb, w) = await NewAsync();
        var question = await db.Comments.SingleAsync(c => c.Id == w.QuestionId);
        question.Options = null;
        await db.SaveChangesAsync();

        Assert.Equal(PressCase.NoSuchOption, (await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default)).Case);
        Assert.Equal(PressCase.NoSuchOption, (await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.PressRecommended(), default)).Case);
    }

    [Fact]
    public async Task ThePressesLogLine_HasNoReplyAddress_AndTheOutcomeCarriesNone()
    {
        var (db, appDb, w) = await NewAsync();

        var outcome = await PressWorld.Presses(db, appDb).AnswerAsync(w.WorkspaceId, w.Press(), default);

        Assert.DoesNotContain("reply-address", outcome.ToString());
    }

    [Fact]
    public void AButtonsAction_ReadsBackAsItWasWritten()
    {
        Assert.Equal(new ChatPressTarget(42, 0), ChatPressAction.Read(ChatPressAction.Write(42, 0)));
        Assert.Equal(new ChatPressTarget(42, 3), ChatPressAction.Read(ChatPressAction.Write(42, 3)));
        Assert.Equal(new ChatPressTarget(long.MaxValue, null), ChatPressAction.Read(ChatPressAction.WriteRecommended(long.MaxValue)));
        Assert.True(ChatPressAction.Read(ChatPressAction.WriteRecommended(7))!.AcceptRecommended);
        Assert.False(ChatPressAction.Read(ChatPressAction.Write(7, 0))!.AcceptRecommended);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("approve")]
    [InlineData("hatch.answer:")]
    [InlineData("hatch.answer:5")]
    [InlineData("hatch.answer:5:")]
    [InlineData("hatch.answer::1")]
    [InlineData("hatch.answer:x:1")]
    [InlineData("hatch.answer:5:x")]
    [InlineData("hatch.answer:5:-1")]
    [InlineData("hatch.answer:5:1:2")]
    [InlineData("hatch.answer:5:99999999999")]
    [InlineData("hatch.answer:99999999999999999999:1")]
    [InlineData("hatch.keep:5:1")]
    [InlineData("Hatch.Answer:5:1")]
    public void JunkReadsAsNothing(string? actionId) => Assert.Null(ChatPressAction.Read(actionId));

    [Fact]
    public void TheHandler_IsAmongThoseTheListenerResolves()
    {
        var services = new ServiceCollection().AddHatchModule(new ConfigurationBuilder().Build());

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IChatPressHandler) && d.ImplementationType == typeof(QuestionPressHandler)
            && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(QuestionPresses) && d.Lifetime == ServiceLifetime.Scoped);
    }

    private static async Task AnswerAsync(HatchContext db, PressWorld w, string author, string kind, string body)
    {
        db.Comments.Add(new EfHatchComment
        {
            IssueId = w.IssueId, Author = author, AuthorKind = kind, Body = body, Kind = EfHatchComment.Answer,
            AnswersId = w.QuestionId, CreatedAt = PressWorld.Now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<(HatchContext Db, AppDbContext AppDb, PressWorld World)> NewAsync(
        string level = QuestionLevels.HoldName, bool terminal = false, QuestionOptionDto[]? options = null, bool link = true)
    {
        var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var appDb = RosterSeed.NewAppDb();
        var world = await PressWorld.SeedAsync(db, appDb, level, terminal, options, link: link);
        return (db, appDb, world);
    }
}

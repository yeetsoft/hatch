using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Hatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// Where a question's own chat message lands and changes: one row per
/// questions-room when it is asked, from either controller that asks one, and
/// the same row edited - its id kept, its revision up - when it is answered,
/// overturned or moved to a new level. No Slack; <see cref="ChatOutbox"/> and
/// an in-memory <see cref="HatchContext"/> are what is under test. The content
/// itself is <see cref="QuestionChatContentTests"/>'s business.
/// </summary>
public class QuestionChatMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AskingThroughIssueThread_QueuesOneRowPerQuestionsRoom()
    {
        var h = await NewAsync();

        var asked = Value(await h.Thread.AddComment(
            h.Key, new CommentCreateRequest("leaf-weighted or child-weighted?", EfHatchComment.Question), default));

        var row = await h.Db.ChatMessages.SingleAsync();
        Assert.Equal(h.QuestionsRoom, row.RoomId);
        Assert.Equal($"comment:{asked.Id}", row.Subject);
        var content = ChatMessageContent.Read(row.Content)!;
        Assert.Equal($"{h.Key} {h.Issue.Title}", content.Title);
        Assert.Equal("leaf-weighted or child-weighted?", content.Body);
    }

    [Fact]
    public async Task AskingThroughWorkController_OnASecondConsecutiveFail_QueuesOneRowPerQuestionsRoom()
    {
        var h = await NewAsync();
        var token = Guid.NewGuid();
        const int playbookId = 9;
        h.Issue.ClaimToken = token;
        await h.Db.SaveChangesAsync();

        await h.LogEventAsync(EfHatchIssueEvent.StatusChanged,
            new { from = "checking", to = "in progress", verdict = CheckVerdicts.Fail, playbookId }, Now.AddMinutes(-10));
        h.Db.Verdicts.Add(new EfHatchVerdict
        {
            IssueId = h.Issue.Id, PlaybookId = playbookId, StatusId = h.Issue.StatusId, Verdict = CheckVerdicts.Fail,
            Findings = "still broken", Author = "hatch-agent", ClaimToken = token, CreatedAt = Now,
        });
        await h.Db.SaveChangesAsync();

        Value(await h.Work.VerdictWork(h.Key, token, ct: default));

        var row = await h.Db.ChatMessages.SingleAsync();
        Assert.Equal(h.QuestionsRoom, row.RoomId);
        var question = await h.Db.Comments.SingleAsync(c => c.Kind == EfHatchComment.Question);
        Assert.Equal($"comment:{question.Id}", row.Subject);
        Assert.Contains("still broken", ChatMessageContent.Read(row.Content)!.Body);
    }

    [Fact]
    public async Task AnsweringThroughIssueThread_EditsTheSameRow()
    {
        var h = await NewAsync();
        var asked = Value(await h.Thread.AddComment(
            h.Key, new CommentCreateRequest("leaf-weighted or child-weighted?", EfHatchComment.Question), default));
        var before = await h.Db.ChatMessages.SingleAsync();

        Value(await h.Thread.AddComment(
            h.Key, new CommentCreateRequest("leaf-weighted", EfHatchComment.Answer, AnswersId: asked.Id), default));

        var after = await h.Db.ChatMessages.SingleAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(2, after.Revision);
        var content = ChatMessageContent.Read(after.Content)!;
        Assert.Equal($"{h.Caller.Local!.Name} answered: leaf-weighted", content.Lines[^1]);
    }

    [Fact]
    public async Task OverturningALookBackQuestion_EditsTheSameRow()
    {
        var h = await NewAsync(issueTerminal: true);
        var question = new EfHatchComment
        {
            IssueId = h.Issue.Id, Author = "hatch-agent", AuthorKind = ActorKind.Key, Body = "leaf-weighted or child-weighted?",
            Kind = EfHatchComment.Question, Level = QuestionLevels.MergeName, CreatedAt = Now,
        };
        h.Db.Comments.Add(question);
        await h.Db.SaveChangesAsync();
        await h.Outbox.QueueAsync(
            ChatMessageKind.Question, $"comment:{question.Id}", h.ProjectId,
            QuestionChatContent.Build(h.Links, question.Id, h.Key, h.Issue.Title, question.Body, null, QuestionLevels.MergeName),
            default);
        await h.Db.SaveChangesAsync();
        var before = await h.Db.ChatMessages.SingleAsync();

        Assert.IsType<OkObjectResult>((await h.Overturn.Overturn(h.Key, question.Id, new QuestionOverturnRequest("child-weighted"), default)).Result);

        var after = await h.Db.ChatMessages.SingleAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(2, after.Revision);
        var content = ChatMessageContent.Read(after.Content)!;
        Assert.Equal($"{h.Caller.Local!.Name} answered: child-weighted", content.Lines[^1]);
    }

    [Fact]
    public async Task ChangingALevel_EditsTheSameRow()
    {
        var h = await NewAsync();
        var question = new EfHatchComment
        {
            IssueId = h.Issue.Id, Author = h.Caller.Local!.Name, Body = "leaf-weighted or child-weighted?",
            Kind = EfHatchComment.Question,
            Options = Questions.WriteOptions([new QuestionOptionDto("leaf-weighted", Recommended: true), new QuestionOptionDto("child-weighted")]),
            CreatedAt = Now,
        };
        h.Db.Comments.Add(question);
        await h.Db.SaveChangesAsync();
        await h.Outbox.QueueAsync(
            ChatMessageKind.Question, $"comment:{question.Id}", h.ProjectId,
            QuestionChatContent.Build(h.Links, question.Id, h.Key, h.Issue.Title, question.Body, Questions.ReadOptions(question.Options), QuestionLevels.HoldName),
            default);
        await h.Db.SaveChangesAsync();
        var before = await h.Db.ChatMessages.SingleAsync();

        Value(await h.Level.PutQuestionLevel(h.Key, question.Id, new QuestionLevelRequest(QuestionLevels.BuildName), default));

        var after = await h.Db.ChatMessages.SingleAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(2, after.Revision);
        var content = ChatMessageContent.Read(after.Content)!;
        Assert.Equal("Level: build", content.Lines.Single(l => l.StartsWith("Level:")));
    }

    [Fact]
    public async Task TwoWorkspacesBoundToOneProject_EachRoomGetsItsOwnRow()
    {
        var h = await NewAsync();
        var otherWorkspace = await h.Seed.WorkspaceAsync("TGLOBEX", h.ProjectId);
        var otherRoom = await h.Seed.RoomAsync(otherWorkspace, "C1", questions: true);

        var asked = Value(await h.Thread.AddComment(
            h.Key, new CommentCreateRequest("leaf-weighted or child-weighted?", EfHatchComment.Question), default));

        var rows = await h.Db.ChatMessages.OrderBy(m => m.RoomId).ToListAsync();
        Assert.Equal([h.QuestionsRoom, otherRoom], rows.Select(m => m.RoomId).OrderBy(id => id));
        Assert.All(rows, m => Assert.Equal($"comment:{asked.Id}", m.Subject));
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name}");

    private static async Task<Harness> NewAsync(bool issueTerminal = false)
    {
        var options = new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new HatchContext(options);
        var time = new FakeTimeProvider(Now);
        var seed = new ChatOutboxSeed(db, Now);

        var projectId = await seed.ProjectAsync("HA");
        var workspace = await seed.WorkspaceAsync("TACME", projectId);
        var questionsRoom = await seed.RoomAsync(workspace, "C1", questions: true);

        var inProgress = new EfHatchStatus { Name = "In Progress", SortOrder = 20 };
        var checking = new EfHatchStatus { Name = "Checking", SortOrder = 30 };
        var done = new EfHatchStatus { Name = "Done", SortOrder = 40, IsTerminal = true };
        db.Statuses.AddRange(inProgress, checking, done);
        await db.SaveChangesAsync();

        var issue = new EfHatchIssue
        {
            ProjectId = projectId, Number = 1, Type = "story", Title = "Rank weighting", Description = "",
            StatusId = issueTerminal ? done.Id : checking.Id, Rank = 1024,
            CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
        };
        db.Issues.Add(issue);
        await db.SaveChangesAsync();

        var links = new ChatLinks(Options.Create(new HatchOptions()));
        var outbox = new ChatOutbox(db, time);
        var actors = new StubActorDirectory();
        var caller = new WorkControllerTests.StubCallerIdentity { Local = new Actor(ActorKind.Person, Guid.NewGuid(), "nathan") };
        var runners = new Runners(
            Options.Create(new HatchOptions()), Options.Create(new AuthOptions()), new StubHatchRevision());

        return new Harness(db, seed, outbox, links, issue, projectId, questionsRoom, caller)
        {
            Thread = new IssueThreadController(db, caller, time, outbox, links),
            Work = new WorkController(
                db, actors, TestClaims.With(), time, Options.Create(new HatchOptions()), new RankService(db),
                caller, runners, chatOutbox: outbox, links: links),
            Overturn = new QuestionOverturnController(db, new RankService(db), caller, time, outbox, links),
            Level = new QuestionLevelController(db, caller, time, outbox, links),
        };
    }

    private sealed class Harness(
        HatchContext db, ChatOutboxSeed seed, ChatOutbox outbox, ChatLinks links,
        EfHatchIssue issue, int projectId, int questionsRoom, WorkControllerTests.StubCallerIdentity caller)
    {
        public HatchContext Db { get; } = db;
        public ChatOutboxSeed Seed { get; } = seed;
        public ChatOutbox Outbox { get; } = outbox;
        public ChatLinks Links { get; } = links;
        public EfHatchIssue Issue { get; } = issue;
        public int ProjectId { get; } = projectId;
        public int QuestionsRoom { get; } = questionsRoom;
        public WorkControllerTests.StubCallerIdentity Caller { get; } = caller;
        public string Key => $"HA-{Issue.Number}";

        public required IssueThreadController Thread { get; init; }
        public required WorkController Work { get; init; }
        public required QuestionOverturnController Overturn { get; init; }
        public required QuestionLevelController Level { get; init; }

        public async Task LogEventAsync(string kind, object? payload, DateTimeOffset at)
        {
            Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = Issue.Id, Actor = "hatch-agent", Kind = kind,
                Payload = payload is null ? null : System.Text.Json.JsonSerializer.Serialize(payload), At = at,
            });
            await Db.SaveChangesAsync();
        }
    }
}

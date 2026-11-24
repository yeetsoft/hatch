using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Hatch.Api.Common;
using Hatch.Api.Models.Auth;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Chat;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Overturning a look-back question answers it against the loop and files the
/// rework, in one save - and refuses anything that is not a look back.
/// </summary>
public class QuestionOverturnTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AMergeQuestionOnAClosedStory_IsAnsweredAndFilesTheReworkUnderTheStory()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", h.Done, closedAt: Now.AddHours(-1));
        var question = await h.AskAsync(story, QuestionLevels.MergeName, "Which store should it use?");

        var result = Assert.IsType<OkObjectResult>((await h.Overturn(story, question, "Use the other store")).Result);
        var overturn = Assert.IsType<QuestionOverturnDto>(result.Value);

        var task = await h.Db.Issues.Include(i => i.Events).SingleAsync(i => i.Id != story.Id);
        Assert.Equal(overturn.TaskKey, $"AER-{task.Number}");
        Assert.Equal("task", task.Type);
        Assert.Equal(story.Id, task.ParentId);
        Assert.Equal(story.Id, task.FiledFromId);
        Assert.Equal("Bring AER-1 in line with the answer", task.Title);
        Assert.Contains($"question {question.Id}", task.Description);
        Assert.Contains("AER-1", task.Description);
        Assert.Contains("Keep the first store", task.Description);
        Assert.Contains("Use the other store", task.Description);
        Assert.Contains("2026-11-18", task.Description);
        Assert.Contains("Bring the work in line with the answer.", task.Description);
        Assert.Contains("> Which store should it use?", task.Description);
        Assert.Contains(task.Events, e => e.Kind == EfHatchIssueEvent.Created);

        Assert.Empty(await Questions.Open(h.Db).ToListAsync());
        Assert.Empty((await h.Ballot.GetBallot(null, default)).Value!.Roots);

        var answer = Assert.Single(overturn.Question.Answers);
        Assert.Equal("Use the other store", answer.Body);
        Assert.True(answer.AnsweredAfter);
        Assert.Contains(await h.Db.IssueEvents.Where(e => e.IssueId == story.Id).ToListAsync(),
            e => e.Kind == EfHatchIssueEvent.Answered && e.Payload!.Contains($"\"questionId\":{question.Id}"));
    }

    [Fact]
    public async Task AQuestionOnAClosedTask_FilesItsReworkUnderTheTasksStory_AndOnAParentlessTaskUnderNothing()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", h.Done, closedAt: Now);
        var child = await h.FileAsync("task", h.Done, closedAt: Now, parentId: story.Id);
        var loose = await h.FileAsync("task", h.Done, closedAt: Now);
        var onChild = await h.AskAsync(child, QuestionLevels.MergeName);
        var onLoose = await h.AskAsync(loose, QuestionLevels.MergeName);

        var first = Assert.IsType<QuestionOverturnDto>(Assert.IsType<OkObjectResult>((await h.Overturn(child, onChild, "No")).Result).Value);
        var second = Assert.IsType<QuestionOverturnDto>(Assert.IsType<OkObjectResult>((await h.Overturn(loose, onLoose, "No")).Result).Value);

        var filedUnderStory = await h.Db.Issues.SingleAsync(i => i.FiledFromId == child.Id);
        var filedUnderNothing = await h.Db.Issues.SingleAsync(i => i.FiledFromId == loose.Id);
        Assert.Equal(story.Id, filedUnderStory.ParentId);
        Assert.Null(filedUnderNothing.ParentId);
        Assert.Equal($"AER-{filedUnderStory.Number}", first.TaskKey);
        Assert.Equal($"AER-{filedUnderNothing.Number}", second.TaskKey);
    }

    [Fact]
    public async Task AKeyFilesIntoTheAgentsColumn_AndAPersonIntoTheLeftmost()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", h.Done, closedAt: Now);
        var byKey = await h.AskAsync(story, QuestionLevels.MergeName);
        var byPerson = await h.AskAsync(story, QuestionLevels.MergeName);

        h.Caller.Person = null;
        h.Caller.Key = new EfApiKey { Name = "hatch-loop", Prefix = "hatch_ak_", Hash = [], CreatedAt = Now };
        var keyTask = (await h.Overturn(story, byKey, "No")).Result;
        Assert.IsType<OkObjectResult>(keyTask);

        h.Caller.Key = null;
        h.Caller.Person = new EfPerson { Name = "A Person", Role = PersonRole.User, CreatedAt = Now, UpdatedAt = Now };
        Assert.IsType<OkObjectResult>((await h.Overturn(story, byPerson, "No")).Result);

        var tasks = await h.Db.Issues.Where(i => i.FiledFromId == story.Id).OrderBy(i => i.Number).ToListAsync();
        Assert.Equal([h.Agents, h.Inbox], tasks.Select(t => t.StatusId));
        Assert.Equal(["hatch-loop", "A Person"], tasks.Select(t => t.CreatedBy));
    }

    [Fact]
    public async Task WhatIsNotALookBack_IsRefusedAndNothingIsWritten()
    {
        var h = await NewAsync();
        var closed = await h.FileAsync("story", h.Done, closedAt: Now);
        var open = await h.FileAsync("story", h.Agents);
        var hold = await h.AskAsync(closed, QuestionLevels.HoldName);
        var answered = await h.AskAsync(closed, QuestionLevels.MergeName);
        await h.AnswerAsync(answered);
        var onOpen = await h.AskAsync(open, QuestionLevels.MergeName);
        var live = await h.AskAsync(closed, QuestionLevels.MergeName);
        var before = await h.CountsAsync();

        Assert.IsType<BadRequestObjectResult>((await h.Overturn(closed, hold, "No")).Result);
        Assert.IsType<BadRequestObjectResult>((await h.Overturn(closed, answered, "No")).Result);
        Assert.IsType<BadRequestObjectResult>((await h.Overturn(open, onOpen, "No")).Result);
        Assert.IsType<BadRequestObjectResult>((await h.Overturn(closed, live, "  ")).Result);
        Assert.IsType<BadRequestObjectResult>((await h.Overturn(closed, live, null)).Result);
        Assert.IsType<BadRequestObjectResult>((await h.Overturn(closed, live, new string('x', EfHatchComment.MaxBodyLength + 1))).Result);
        // A comment that is a question, but on another issue.
        Assert.IsType<BadRequestObjectResult>((await h.Overturn(closed, onOpen, "No")).Result);
        // Not a question at all: the answer written above.
        var notAQuestion = await h.Db.Comments.FirstAsync(c => c.Kind == EfHatchComment.Answer);
        Assert.IsType<BadRequestObjectResult>((await h.Overturn(closed, notAQuestion, "No")).Result);

        Assert.Equal(before, await h.CountsAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AKeyOnlyAnswer_IsOverturned_AtBuildOnAnOpenIssueOrAtMergeOnAClosedOne(bool closed)
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", closed ? h.Done : h.Agents, closedAt: closed ? Now : null);
        var question = await h.AskAsync(story, closed ? QuestionLevels.MergeName : QuestionLevels.BuildName);
        await h.AnswerAsync(question, ActorKind.Key, "Went with the first store anyway.\n\nMore detail follows.");

        var result = Assert.IsType<OkObjectResult>((await h.Overturn(story, question, "Use the other store")).Result);
        var overturn = Assert.IsType<QuestionOverturnDto>(result.Value);

        var task = await h.Db.Issues.SingleAsync(i => i.FiledFromId == story.Id);
        Assert.Equal(overturn.TaskKey, $"AER-{task.Number}");
        Assert.Contains("The loop decided: Went with the first store anyway.", task.Description);
        Assert.Contains("Use the other store", task.Description);

        var answers = overturn.Question.Answers;
        Assert.Equal(2, answers.Count);
        Assert.Contains(answers, a => a.Body == "Use the other store");
    }

    [Fact]
    public async Task APersonsAnswer_IsRefusedEvenAlongsideAKeysAnswer()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", h.Done, closedAt: Now);
        var question = await h.AskAsync(story, QuestionLevels.MergeName);
        await h.AnswerAsync(question, ActorKind.Key, "Keep the first store");
        await h.AnswerAsync(question, ActorKind.Person, "Keep the first store");
        var before = await h.CountsAsync();

        Assert.IsType<BadRequestObjectResult>((await h.Overturn(story, question, "No")).Result);
        Assert.Equal(before, await h.CountsAsync());
    }

    [Fact]
    public async Task AKeyOnlyAnswer_AtHold_IsRefused()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", h.Agents);
        var question = await h.AskAsync(story, QuestionLevels.HoldName);
        await h.AnswerAsync(question, ActorKind.Key, "Keep the first store");
        var before = await h.CountsAsync();

        Assert.IsType<BadRequestObjectResult>((await h.Overturn(story, question, "No")).Result);
        Assert.Equal(before, await h.CountsAsync());
    }

    [Fact]
    public async Task Keep_AsAPersonsAnswerThroughTheCommentsRoute_LandsBesideTheProgramsAndClearsTheBallot()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", h.Agents);
        var question = await h.AskAsync(story, QuestionLevels.BuildName);
        await h.AnswerAsync(question, ActorKind.Key, "Keep the first store");

        var before = Assert.Single((await h.Ballot.GetBallot(null, default)).Value!.Questions);
        Assert.True(before.Question.LookBack);

        var request = new CommentCreateRequest("Keep the first store", EfHatchComment.Answer, question.Id);
        var result = Assert.IsType<CommentDto>((await h.Thread.AddComment($"AER-{story.Number}", request, default)).Value);
        Assert.Equal("Keep the first store", result.Body);

        Assert.Empty((await h.Ballot.GetBallot(null, default)).Value!.Questions);
    }

    [Fact]
    public async Task WithChatWired_OverturningEditsTheQuestionsRoomMessage()
    {
        var h = await NewAsync();
        var seed = new ChatOutboxSeed(h.Db, Now);
        var workspace = await seed.WorkspaceAsync("TACME", h.ProjectId);
        await seed.RoomAsync(workspace, "C1", questions: true);
        var outbox = new ChatOutbox(h.Db, new FakeTimeProvider(Now));
        var links = new ChatLinks(Options.Create(new HatchOptions()));
        var controller = new QuestionOverturnController(h.Db, new RankService(h.Db), h.Caller, new FakeTimeProvider(Now), outbox, links);

        var story = await h.FileAsync("story", h.Done, closedAt: Now.AddHours(-1));
        var question = await h.AskAsync(story, QuestionLevels.MergeName);
        await outbox.QueueAsync(
            ChatMessageKind.Question, $"comment:{question.Id}", h.ProjectId,
            QuestionChatContent.Build(links, question.Id, $"AER-{story.Number}", story.Title, question.Body, Questions.ReadOptions(question.Options), QuestionLevels.MergeName),
            default);
        await h.Db.SaveChangesAsync();
        var before = await h.Db.ChatMessages.SingleAsync();

        Assert.IsType<OkObjectResult>((await controller.Overturn($"AER-{story.Number}", question.Id, new QuestionOverturnRequest("Use the other store"), default)).Result);

        var after = await h.Db.ChatMessages.SingleAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(2, after.Revision);
        Assert.Contains("answered: Use the other store", ChatMessageContent.Read(after.Content)!.Lines[^1]);
    }

    [Fact]
    public async Task AnUnknownIssueOrKey_IsNotFound()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", h.Done, closedAt: Now);
        var question = await h.AskAsync(story, QuestionLevels.MergeName);

        Assert.IsType<NotFoundResult>((await h.Controller.Overturn("AER-99", question.Id, new QuestionOverturnRequest("No"), default)).Result);
        Assert.IsType<NotFoundResult>((await h.Controller.Overturn("nonsense", question.Id, new QuestionOverturnRequest("No"), default)).Result);
    }

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required QuestionOverturnController Controller { get; init; }
        public required BallotController Ballot { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required WorkControllerTests.StubCallerIdentity Caller { get; init; }
        public required int ProjectId { get; init; }
        public required int Inbox { get; init; }
        public required int Agents { get; init; }
        public required int Done { get; init; }

        private int next = 1;

        public async Task<EfHatchIssue> FileAsync(string type, int statusId, DateTimeOffset? closedAt = null, long? parentId = null)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId, Number = next++, Type = type, Title = $"issue {next}",
                StatusId = statusId, Rank = 1024, ParentId = parentId, CreatedBy = "person",
                CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1), ClosedAt = closedAt,
            };
            Db.Issues.Add(issue);
            (await Db.Projects.SingleAsync(p => p.Id == ProjectId)).NextIssueNumber = next;
            await Db.SaveChangesAsync();
            return issue;
        }

        public async Task<EfHatchComment> AskAsync(EfHatchIssue issue, string level, string body = "A question")
        {
            var question = new EfHatchComment
            {
                IssueId = issue.Id, Author = "agent", Body = body, Kind = EfHatchComment.Question, Level = level,
                Options = Questions.WriteOptions([new("Keep the first store", null, true), new("Use the other store")]),
                CreatedAt = Now.AddHours(-2),
            };
            Db.Comments.Add(question);
            await Db.SaveChangesAsync();
            return question;
        }

        public async Task AnswerAsync(EfHatchComment question, string? authorKind = null, string body = "Yes")
        {
            Db.Comments.Add(new EfHatchComment
            {
                IssueId = question.IssueId, Author = authorKind == ActorKind.Key ? "hatch-loop" : "person", Body = body,
                Kind = EfHatchComment.Answer, AnswersId = question.Id, AuthorKind = authorKind, CreatedAt = Now.AddHours(1),
            });
            await Db.SaveChangesAsync();
        }

        public Task<ActionResult<QuestionOverturnDto>> Overturn(EfHatchIssue issue, EfHatchComment question, string? answer) =>
            Controller.Overturn($"AER-{issue.Number}", question.Id, new QuestionOverturnRequest(answer), default);

        public async Task<(int Issues, int Comments, int Events, int NextNumber)> CountsAsync() =>
            (await Db.Issues.CountAsync(), await Db.Comments.CountAsync(), await Db.IssueEvents.CountAsync(),
             await Db.Projects.Where(p => p.Id == ProjectId).Select(p => p.NextIssueNumber).SingleAsync());
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var agents = new EfHatchStatus { Name = "to do", SortOrder = 20, AgentFiles = true };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(project, inbox, agents, done);
        await db.SaveChangesAsync();

        var caller = new WorkControllerTests.StubCallerIdentity { Person = new EfPerson { Name = "A Person", Role = PersonRole.User, CreatedAt = Now, UpdatedAt = Now } };

        return new Harness
        {
            Db = db,
            Controller = new QuestionOverturnController(db, new RankService(db), caller, new FakeTimeProvider(Now)),
            Ballot = new BallotController(db),
            Thread = new IssueThreadController(db, caller, new FakeTimeProvider(Now)),
            Caller = caller,
            ProjectId = project.Id, Inbox = inbox.Id, Agents = agents.Id, Done = done.Id,
        };
    }
}

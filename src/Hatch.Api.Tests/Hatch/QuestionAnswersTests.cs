using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The one writer of an answer, driven with no request behind it: the comment,
/// the <c>answered</c> event and the edit to the question's chat message, told
/// who is answering instead of reading it from the caller.
/// </summary>
public class QuestionAnswersTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task APersonsAnswer_WritesTheCommentAndTheEventAsThem()
    {
        var h = await NewAsync();
        var question = await h.AskAsync();

        var answer = await h.Answers.WriteAsync(h.Issue.Id, question.Id, "leaf-weighted", new AnsweringActor("Ada", ActorKind.Person), null, default);

        Assert.NotNull(answer);
        var stored = await h.Db.Comments.SingleAsync(c => c.AnswersId == question.Id);
        Assert.Equal(answer.Id, stored.Id);
        Assert.Equal(("Ada", ActorKind.Person, EfHatchComment.Answer, "leaf-weighted"), (stored.Author, stored.AuthorKind, stored.Kind, stored.Body));
        Assert.Equal(Now, stored.CreatedAt);

        var answered = await h.Db.IssueEvents.SingleAsync(e => e.Kind == EfHatchIssueEvent.Answered);
        Assert.Equal("Ada", answered.Actor);
        Assert.Equal("{\"questionId\":" + question.Id + "}", answered.Payload);
    }

    [Fact]
    public async Task AKeysAnswer_IsWrittenAsAKeys()
    {
        var h = await NewAsync();
        var question = await h.AskAsync();

        await h.Answers.WriteAsync(h.Issue.Id, question.Id, "merge it", new AnsweringActor("runner-1", ActorKind.Key), null, default);

        Assert.Equal(ActorKind.Key, (await h.Db.Comments.SingleAsync(c => c.AnswersId == question.Id)).AuthorKind);
    }

    [Fact]
    public async Task Via_IsInTheEventsPayload_AndOnlyThere()
    {
        var h = await NewAsync();
        var question = await h.AskAsync();

        await h.Answers.WriteAsync(h.Issue.Id, question.Id, "leaf-weighted", new AnsweringActor("Ada", ActorKind.Person), AnswerVia.Slack, default);

        var answered = await h.Db.IssueEvents.SingleAsync(e => e.Kind == EfHatchIssueEvent.Answered);
        Assert.Equal("{\"questionId\":" + question.Id + ",\"via\":\"slack\"}", answered.Payload);
    }

    [Fact]
    public async Task TheQuestionsChatMessage_IsEditedUnderItsOwnSubject_InTheSameSave()
    {
        var h = await NewAsync();
        var question = await h.AskAsync();
        var before = await h.Db.ChatMessages.SingleAsync();

        await h.Answers.WriteAsync(h.Issue.Id, question.Id, "leaf-weighted", new AnsweringActor("Ada", ActorKind.Person), null, default);

        var after = await h.Db.ChatMessages.SingleAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal($"comment:{question.Id}", after.Subject);
        Assert.Equal(2, after.Revision);
        var content = ChatMessageContent.Read(after.Content)!;
        Assert.Equal("HA-1 Rank weighting", content.Title);
        Assert.Contains("Ada answered: leaf-weighted", content.Lines);
    }

    [Fact]
    public async Task WhenOnlyKeysHaveAnswered_TheMessageReadsAsALookBack()
    {
        var h = await NewAsync(level: QuestionLevels.BuildName);
        var question = await h.AskAsync();

        await h.Answers.WriteAsync(h.Issue.Id, question.Id, "go ahead", new AnsweringActor("runner-1", ActorKind.Key), null, default);

        var content = ChatMessageContent.Read((await h.Db.ChatMessages.SingleAsync()).Content)!;
        Assert.Contains("A program answered: go ahead", content.Lines);
        Assert.DoesNotContain(content.Lines, l => l.Contains("runner-1"));
    }

    [Fact]
    public async Task WhenAPersonHasAnswered_TheMessageNamesThem_NotALookBack()
    {
        var h = await NewAsync(level: QuestionLevels.BuildName);
        var question = await h.AskAsync();
        await h.Answers.WriteAsync(h.Issue.Id, question.Id, "go ahead", new AnsweringActor("runner-1", ActorKind.Key), null, default);

        await h.Answers.WriteAsync(h.Issue.Id, question.Id, "no, hold", new AnsweringActor("Ada", ActorKind.Person), null, default);

        var content = ChatMessageContent.Read((await h.Db.ChatMessages.SingleAsync()).Content)!;
        Assert.Contains("Ada answered: no, hold", content.Lines);
    }

    [Fact]
    public async Task WithNoOutbox_ItStillWritesTheAnswerAndTheEvent()
    {
        var h = await NewAsync(chat: false);
        var question = await h.AskAsync();

        Assert.NotNull(await h.Answers.WriteAsync(h.Issue.Id, question.Id, "leaf-weighted", new AnsweringActor("Ada", ActorKind.Person), null, default));

        Assert.Equal(1, await h.Db.IssueEvents.CountAsync(e => e.Kind == EfHatchIssueEvent.Answered));
        Assert.Empty(await h.Db.ChatMessages.ToListAsync());
    }

    [Fact]
    public async Task AnotherIssuesQuestion_IsNullAndWritesNothing()
    {
        var h = await NewAsync();
        var question = await h.AskAsync();
        var other = new EfHatchIssue
        {
            ProjectId = h.Issue.ProjectId, Number = 2, Type = "story", Title = "Other", Description = "",
            StatusId = h.Issue.StatusId, Rank = 2048, CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
        };
        h.Db.Issues.Add(other);
        await h.Db.SaveChangesAsync();

        Assert.Null(await h.Answers.WriteAsync(other.Id, question.Id, "x", new AnsweringActor("Ada", ActorKind.Person), null, default));

        Assert.Empty(await h.Db.Comments.Where(c => c.Kind == EfHatchComment.Answer).ToListAsync());
        Assert.Empty(await h.Db.IssueEvents.Where(e => e.Kind == EfHatchIssueEvent.Answered).ToListAsync());
    }

    [Fact]
    public async Task ANoteThatIsNotAQuestion_IsNull()
    {
        var h = await NewAsync();
        var note = new EfHatchComment { IssueId = h.Issue.Id, Author = "Ada", Body = "fyi", Kind = EfHatchComment.Note, CreatedAt = Now };
        h.Db.Comments.Add(note);
        await h.Db.SaveChangesAsync();

        Assert.Null(await h.Answers.WriteAsync(h.Issue.Id, note.Id, "x", new AnsweringActor("Ada", ActorKind.Person), null, default));
    }

    private static async Task<Harness> NewAsync(string? level = null, bool chat = true)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var time = new FakeTimeProvider(Now);
        var seed = new ChatOutboxSeed(db, Now);

        var projectId = await seed.ProjectAsync("HA");
        var workspace = await seed.WorkspaceAsync("TACME", projectId);
        await seed.RoomAsync(workspace, "C1", questions: true);

        var checking = new EfHatchStatus { Name = "Checking", SortOrder = 30 };
        db.Statuses.Add(checking);
        await db.SaveChangesAsync();

        var issue = new EfHatchIssue
        {
            ProjectId = projectId, Number = 1, Type = "story", Title = "Rank weighting", Description = "",
            StatusId = checking.Id, Rank = 1024, CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
        };
        db.Issues.Add(issue);
        await db.SaveChangesAsync();

        var links = new ChatLinks(Options.Create(new HatchOptions()));
        var outbox = new ChatOutbox(db, time);
        return new Harness(db, issue, outbox, links, level, chat)
        {
            Answers = chat ? new QuestionAnswers(db, time, outbox, links) : new QuestionAnswers(db, time),
        };
    }

    private sealed class Harness(HatchContext db, EfHatchIssue issue, ChatOutbox outbox, ChatLinks links, string? level, bool chat)
    {
        public HatchContext Db { get; } = db;
        public EfHatchIssue Issue { get; } = issue;
        public required QuestionAnswers Answers { get; init; }

        /// <summary>A question on the issue, with its chat message queued when chat is on.</summary>
        public async Task<EfHatchComment> AskAsync()
        {
            var question = new EfHatchComment
            {
                IssueId = Issue.Id, Author = "hatch-agent", AuthorKind = ActorKind.Key, Kind = EfHatchComment.Question,
                Body = "leaf-weighted or child-weighted?", Level = level, CreatedAt = Now,
                Options = Questions.WriteOptions([new QuestionOptionDto("leaf-weighted"), new QuestionOptionDto("child-weighted")]),
            };
            Db.Comments.Add(question);
            await Db.SaveChangesAsync();

            if (chat)
            {
                await outbox.QueueAsync(
                    ChatMessageKind.Question, $"comment:{question.Id}", Issue.ProjectId,
                    QuestionChatContent.Build(
                        links, "HA-1", Issue.Title, question.Body, Questions.ReadOptions(question.Options),
                        QuestionLevels.Normalise(question.Level)),
                    default);
                await Db.SaveChangesAsync();
            }

            return question;
        }
    }
}

using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Where one question stands, for a caller that must decide before it writes:
/// <see cref="Questions.StandingAsync"/>. The look-back rule itself is
/// <see cref="Questions.IsLookBack"/>'s, tested where it lives.
/// </summary>
public class QuestionStandingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ANoSuchQuestion_IsNull()
    {
        var h = await NewAsync();

        Assert.Null(await Questions.StandingAsync(h.Db, 9999, default));
    }

    [Fact]
    public async Task ANote_IsNotAQuestion()
    {
        var h = await NewAsync();
        var note = await h.AddAsync(EfHatchComment.Note, "Ada", ActorKind.Person);

        Assert.Null(await Questions.StandingAsync(h.Db, note.Id, default));
    }

    [Fact]
    public async Task NoAnswer_IsUnanswered()
    {
        var h = await NewAsync();
        var question = await h.AskAsync(QuestionLevels.HoldName);

        var standing = await Questions.StandingAsync(h.Db, question.Id, default);

        Assert.Equal(new QuestionStanding(QuestionStand.Unanswered, h.Issue.Id, null, null), standing);
    }

    [Fact]
    public async Task APersonsAnswer_IsAnswered_AndNamesThem()
    {
        var h = await NewAsync();
        var question = await h.AskAsync(QuestionLevels.HoldName);
        await h.AnswerAsync(question, "Ada", ActorKind.Person);

        var standing = await Questions.StandingAsync(h.Db, question.Id, default);

        Assert.Equal(new QuestionStanding(QuestionStand.Answered, h.Issue.Id, "Ada", ActorKind.Person), standing);
    }

    [Fact]
    public async Task AKeyAtHold_IsAnswered_NotALookBack()
    {
        var h = await NewAsync();
        var question = await h.AskAsync(QuestionLevels.HoldName);
        await h.AnswerAsync(question, "runner-1", ActorKind.Key);

        Assert.Equal(QuestionStand.Answered, (await Questions.StandingAsync(h.Db, question.Id, default))!.Stand);
    }

    [Fact]
    public async Task AKeyAtBuild_IsALookBack_WithTheKeyAsLatest()
    {
        var h = await NewAsync();
        var question = await h.AskAsync(QuestionLevels.BuildName);
        await h.AnswerAsync(question, "runner-1", ActorKind.Key);

        var standing = await Questions.StandingAsync(h.Db, question.Id, default);

        Assert.Equal(new QuestionStanding(QuestionStand.LookBack, h.Issue.Id, "runner-1", ActorKind.Key), standing);
    }

    [Fact]
    public async Task APersonAnsweringAfterAKey_IsAnswered_AndTheLatestIsThePerson()
    {
        var h = await NewAsync();
        var question = await h.AskAsync(QuestionLevels.BuildName);
        await h.AnswerAsync(question, "runner-1", ActorKind.Key);
        await h.AnswerAsync(question, "Ada", ActorKind.Person);

        var standing = await Questions.StandingAsync(h.Db, question.Id, default);

        Assert.Equal(new QuestionStanding(QuestionStand.Answered, h.Issue.Id, "Ada", ActorKind.Person), standing);
    }

    [Fact]
    public async Task AClosedMergeQuestionNobodyAnswered_IsALookBack_WithNoLatest()
    {
        var h = await NewAsync(issueTerminal: true);
        var question = await h.AskAsync(QuestionLevels.MergeName);

        var standing = await Questions.StandingAsync(h.Db, question.Id, default);

        Assert.Equal(new QuestionStanding(QuestionStand.LookBack, h.Issue.Id, null, null), standing);
    }

    [Fact]
    public async Task APersonScopedAway_SeesNoQuestion_UnlessTheReadIgnoresTheFilters()
    {
        var h = await NewAsync();
        var question = await h.AskAsync(QuestionLevels.HoldName);
        h.Scope.Current = new VisibleProjects(false, new HashSet<int>());

        Assert.Null(await Questions.StandingAsync(h.Db, question.Id, default));
        Assert.Equal(
            QuestionStand.Unanswered,
            (await Questions.StandingAsync(h.Db, question.Id, default, ignoreFilters: true))!.Stand);
    }

    private static async Task<Harness> NewAsync(bool issueTerminal = false)
    {
        var scope = new RequestScope();
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, scope);

        var project = new EfHatchProject { Key = "HA", Name = "HA", CreatedAt = Now };
        var status = new EfHatchStatus { Name = issueTerminal ? "Done" : "Checking", SortOrder = 30, IsTerminal = issueTerminal };
        db.Projects.Add(project);
        db.Statuses.Add(status);
        await db.SaveChangesAsync();

        var issue = new EfHatchIssue
        {
            ProjectId = project.Id, Number = 1, Type = "story", Title = "Rank weighting", Description = "",
            StatusId = status.Id, Rank = 1024, CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
        };
        db.Issues.Add(issue);
        await db.SaveChangesAsync();
        return new Harness(db, issue, scope);
    }

    private sealed class Harness(HatchContext db, EfHatchIssue issue, RequestScope scope)
    {
        public HatchContext Db { get; } = db;
        public EfHatchIssue Issue { get; } = issue;
        public RequestScope Scope { get; } = scope;
        private int ticks;

        public Task<EfHatchComment> AskAsync(string level) => AddAsync(EfHatchComment.Question, "hatch-agent", ActorKind.Key, level);

        public Task<EfHatchComment> AnswerAsync(EfHatchComment question, string author, string kind) =>
            AddAsync(EfHatchComment.Answer, author, kind, answers: question.Id);

        public async Task<EfHatchComment> AddAsync(string kind, string author, string authorKind, string? level = null, long? answers = null)
        {
            var comment = new EfHatchComment
            {
                IssueId = Issue.Id, Author = author, AuthorKind = authorKind, Kind = kind, Body = "body",
                Level = level, AnswersId = answers, CreatedAt = Now.AddMinutes(++ticks),
            };
            Db.Comments.Add(comment);
            await Db.SaveChangesAsync();
            return comment;
        }
    }
}

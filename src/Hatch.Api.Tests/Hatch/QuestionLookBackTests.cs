using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// A <c>merge</c> question stays waiting after its issue closes, and reads as
/// a look back; its answers read as given after the fact when they come after
/// the close. Both are derived when read - nothing is stored.
/// </summary>
public class QuestionLookBackTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AMergeQuestionOnAClosedIssue_IsInTheOpenListAndTheBallot_AndAHoldOneIsNot()
    {
        var h = await NewAsync();
        var closed = await h.FileAsync(h.Done, closedAt: Now);
        var merge = await h.AskAsync(closed, QuestionLevels.MergeName);
        await h.AskAsync(closed, QuestionLevels.HoldName);
        await h.AskAsync(closed, QuestionLevels.BuildName);

        var listed = Assert.IsAssignableFrom<IReadOnlyList<QuestionDto>>((await h.Controller.GetQuestions(true, default)).Value);
        var ballot = Assert.Single((await h.Ballot.GetBallot(null, default)).Value!.Roots);

        Assert.Equal(merge.Id, Assert.Single(listed).Id);
        Assert.Equal(merge.Id, Assert.Single(ballot.Questions).Id);
        Assert.True(ballot.IsTerminal);
    }

    [Fact]
    public async Task LookBack_IsTrueOnlyOnAnOpenMergeQuestionOfAClosedIssue()
    {
        var h = await NewAsync();
        var closed = await h.FileAsync(h.Done, closedAt: Now);
        var live = await h.FileAsync(h.Open);
        var openOnClosed = await h.AskAsync(closed, QuestionLevels.MergeName);
        var answeredOnClosed = await h.AskAsync(closed, QuestionLevels.MergeName);
        await h.AnswerAsync(answeredOnClosed, Now.AddHours(1));
        var onLive = await h.AskAsync(live, QuestionLevels.MergeName);
        var holdOnClosed = await h.AskAsync(closed, QuestionLevels.HoldName);

        var all = (await h.Controller.GetQuestions(false, default)).Value!.ToDictionary(q => q.Id);

        Assert.True(all[openOnClosed.Id].LookBack);
        Assert.False(all[answeredOnClosed.Id].LookBack);
        Assert.False(all[onLive.Id].LookBack);
        Assert.False(all[holdOnClosed.Id].LookBack);
    }

    [Fact]
    public async Task AnAnswerGivenAfterTheIssueClosed_ReadsAnsweredAfter_AndOneGivenBeforeDoesNot()
    {
        var h = await NewAsync();
        var closed = await h.FileAsync(h.Done, closedAt: Now);
        var before = await h.AskAsync(closed, QuestionLevels.MergeName);
        var after = await h.AskAsync(closed, QuestionLevels.MergeName);
        await h.AnswerAsync(before, Now.AddHours(-1));
        await h.AnswerAsync(after, Now.AddHours(1));

        var all = (await h.Controller.GetQuestions(false, default)).Value!.ToDictionary(q => q.Id);

        Assert.False(Assert.Single(all[before.Id].Answers).AnsweredAfter);
        Assert.True(Assert.Single(all[after.Id].Answers).AnsweredAfter);
    }

    [Fact]
    public async Task AnAnswerOnAnIssueThatNeverClosed_IsNeverAnsweredAfter()
    {
        var h = await NewAsync();
        var live = await h.FileAsync(h.Open);
        var question = await h.AskAsync(live, QuestionLevels.HoldName);
        await h.AnswerAsync(question, Now.AddDays(30));

        var read = Assert.Single((await h.Controller.GetQuestions(false, default)).Value!);

        Assert.False(Assert.Single(read.Answers).AnsweredAfter);
    }

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required QuestionsController Controller { get; init; }
        public required BallotController Ballot { get; init; }
        public required int ProjectId { get; init; }
        public required int Open { get; init; }
        public required int Done { get; init; }

        private int next = 1;

        public async Task<EfHatchIssue> FileAsync(int statusId, DateTimeOffset? closedAt = null)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId, Number = next++, Type = "task", Title = $"issue {next}",
                StatusId = statusId, Rank = 1024, CreatedBy = "person", CreatedAt = Now.AddDays(-1),
                UpdatedAt = Now.AddDays(-1), ClosedAt = closedAt,
            };
            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        /// <summary>Set on the row, as the level route is for a person alone.</summary>
        public async Task<EfHatchComment> AskAsync(EfHatchIssue issue, string level)
        {
            var question = new EfHatchComment
            {
                IssueId = issue.Id, Author = "agent", Body = "A question", Kind = EfHatchComment.Question,
                Level = level, CreatedAt = Now.AddHours(-2),
            };
            Db.Comments.Add(question);
            await Db.SaveChangesAsync();
            return question;
        }

        public async Task AnswerAsync(EfHatchComment question, DateTimeOffset at)
        {
            Db.Comments.Add(new EfHatchComment
            {
                IssueId = question.IssueId, Author = "person", Body = "Yes", Kind = EfHatchComment.Answer,
                AnswersId = question.Id, CreatedAt = at,
            });
            await Db.SaveChangesAsync();
        }
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var open = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(project, open, done);
        await db.SaveChangesAsync();

        return new Harness
        {
            Db = db, Controller = new QuestionsController(db), Ballot = new BallotController(db),
            ProjectId = project.Id, Open = open.Id, Done = done.Id,
        };
    }
}

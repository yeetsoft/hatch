using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The two reads the dispatcher folds a question by:
/// <see cref="Questions.WaitingLevelsAsync"/> per issue, and
/// <see cref="PriorityTree.EffectiveQuestion"/> up the parent chain. The fold
/// that uses them is pinned in <c>WorkControllerTests</c>.
/// </summary>
public class QuestionLevelReadTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    // ---- WaitingLevelsAsync ----

    [Fact]
    public async Task WaitingLevels_IsTheStrictestLevelAndTheCountAtIt()
    {
        var (db, issue, _) = await SeedAsync();
        await AskAsync(db, issue, QuestionLevels.MergeName);
        await AskAsync(db, issue, QuestionLevels.BuildName);
        await AskAsync(db, issue, QuestionLevels.BuildName);

        var read = await Questions.WaitingLevelsAsync(db, default);

        Assert.Equal((QuestionLevels.BuildName, 2), read[issue.Id]);
    }

    [Fact]
    public async Task WaitingLevels_ARowWithNoLevelReadsAsHold_AndOutranksTheRest()
    {
        var (db, issue, _) = await SeedAsync();
        await AskAsync(db, issue, QuestionLevels.BuildName);
        await AskAsync(db, issue, null);

        var read = await Questions.WaitingLevelsAsync(db, default);

        Assert.Equal((QuestionLevels.HoldName, 1), read[issue.Id]);
    }

    [Fact]
    public async Task WaitingLevels_AnAnsweredQuestionAndATerminalIssueAreAbsent()
    {
        var (db, issue, finished) = await SeedAsync();
        var answered = await AskAsync(db, issue, QuestionLevels.HoldName);
        db.Comments.Add(new EfHatchComment
        {
            IssueId = issue.Id, Author = "person", Body = "Yes", Kind = EfHatchComment.Answer,
            AnswersId = answered.Id, CreatedAt = Now,
        });
        await db.SaveChangesAsync();
        await AskAsync(db, finished, QuestionLevels.HoldName);

        var read = await Questions.WaitingLevelsAsync(db, default);

        Assert.Empty(read);
    }

    // ---- EffectiveQuestion ----

    // 1 EPIC-1 is the epic, 2 EPIC-2 a story under it, 3 EPIC-3 a task under that.
    private static PriorityTree Tree(Dictionary<long, (string Level, int Count)> questions)
    {
        var rows = new Dictionary<long, (long?, string, int, int)>
        {
            [1] = (null, "EPIC-1", PriorityLevels.Normal, PreApprovalLevels.None),
            [2] = (1, "EPIC-2", PriorityLevels.Normal, PreApprovalLevels.None),
            [3] = (2, "EPIC-3", PriorityLevels.Normal, PreApprovalLevels.None),
        };

        return new PriorityTree(rows, questions: questions);
    }

    [Fact]
    public void EffectiveQuestion_NoQuestionAnywhere_IsNull()
    {
        Assert.Null(Tree([]).EffectiveQuestion(3));
    }

    [Fact]
    public void EffectiveQuestion_TheIssuesOwn_NamesNoKey()
    {
        var tree = Tree(new() { [3] = (QuestionLevels.BuildName, 2) });

        Assert.Equal((QuestionLevels.BuildName, 2, (string?)null), tree.EffectiveQuestion(3));
    }

    [Fact]
    public void EffectiveQuestion_AnAncestorsOnly_NamesItsKey()
    {
        var tree = Tree(new() { [1] = (QuestionLevels.HoldName, 1) });

        Assert.Equal((QuestionLevels.HoldName, 1, (string?)"EPIC-1"), tree.EffectiveQuestion(3));
    }

    [Fact]
    public void EffectiveQuestion_TheStrictestWins_WhereverItSits()
    {
        var tree = Tree(new()
        {
            [1] = (QuestionLevels.HoldName, 1),
            [3] = (QuestionLevels.BuildName, 1),
        });

        Assert.Equal((QuestionLevels.HoldName, 1, (string?)"EPIC-1"), tree.EffectiveQuestion(3));
    }

    [Fact]
    public void EffectiveQuestion_ATieKeepsTheNearerIssue()
    {
        var tree = Tree(new()
        {
            [1] = (QuestionLevels.BuildName, 5),
            [2] = (QuestionLevels.BuildName, 1),
        });

        Assert.Equal((QuestionLevels.BuildName, 1, (string?)"EPIC-2"), tree.EffectiveQuestion(3));
    }

    // ---- fixtures ----

    private static async Task<(HatchContext Db, EfHatchIssue Open, EfHatchIssue Finished)> SeedAsync()
    {
        var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var open = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(project, open, done);
        await db.SaveChangesAsync();

        EfHatchIssue Issue(int number, EfHatchStatus status) => new()
        {
            ProjectId = project.Id, Number = number, Type = "task", Title = $"issue {number}",
            StatusId = status.Id, Rank = number, CreatedBy = "person", CreatedAt = Now, UpdatedAt = Now,
        };

        var live = Issue(1, open);
        var finished = Issue(2, done);
        db.AddRange(live, finished);
        await db.SaveChangesAsync();
        return (db, live, finished);
    }

    private static async Task<EfHatchComment> AskAsync(HatchContext db, EfHatchIssue issue, string? level)
    {
        var question = new EfHatchComment
        {
            IssueId = issue.Id, Author = "agent", Body = "A question", Kind = EfHatchComment.Question,
            Level = level, CreatedAt = Now,
        };
        db.Comments.Add(question);
        await db.SaveChangesAsync();
        return question;
    }
}

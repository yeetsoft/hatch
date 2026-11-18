using Hatch.Api.Modules.Hatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The waiting questions, read as a tree.
///
/// What is pinned is the shape a reader cannot recover from getting wrong: a
/// container drawn once however many questions sit beneath it, and an order
/// that puts the question that has waited longest first at every level -
/// since the page is answered from the top.
/// </summary>
public class BallotControllerTests
{
    [Fact]
    public async Task TwoQuestionsOnOneTask_AreOneNodeWithBothQuestionsOldestFirst()
    {
        var h = await NewAsync();
        var task = await h.FileAsync("task", "wire it");
        await h.AskAsync(task, "second asked", at: Now.AddHours(2));
        await h.AskAsync(task, "first asked", at: Now.AddHours(1));

        var root = Assert.Single(Value(await h.Ballot.GetBallot(null, default)).Roots);

        Assert.Equal(h.Key(task), root.Key);
        Assert.Empty(root.Children);
        Assert.Equal(["first asked", "second asked"], root.Questions.Select(q => q.Body));
    }

    [Fact]
    public async Task ASiblingStoryWithItsOwnQuestion_IsASecondChildUnderOneRootForTheEpic()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        var one = await h.FileAsync("story", "one", parentId: epic.Id);
        var two = await h.FileAsync("story", "two", parentId: epic.Id);
        await h.AskAsync(one, "about one");
        await h.AskAsync(two, "about two");

        var root = Assert.Single(Value(await h.Ballot.GetBallot(null, default)).Roots);

        Assert.Equal(h.Key(epic), root.Key);
        Assert.Empty(root.Questions);
        Assert.Equal(2, root.Children.Count);
        Assert.Equal([h.Key(one), h.Key(two)], root.Children.Select(c => c.Key).Order());
        Assert.All(root.Children, c => Assert.Single(c.Questions));
    }

    [Fact]
    public async Task AParentlessTaskWithAQuestion_IsARootOfItsOwn()
    {
        var h = await NewAsync();
        var task = await h.FileAsync("task", "alone");
        await h.AskAsync(task, "which?");

        var root = Assert.Single(Value(await h.Ballot.GetBallot(null, default)).Roots);

        Assert.Equal(h.Key(task), root.Key);
        Assert.Equal("AER", root.ProjectKey);
        Assert.Equal("task", root.Type);
        Assert.Empty(root.Children);
    }

    [Fact]
    public async Task NoWaitingQuestions_IsNoRoots()
    {
        var h = await NewAsync();
        await h.FileAsync("task", "quiet");

        Assert.Empty(Value(await h.Ballot.GetBallot(null, default)).Roots);
    }

    [Fact]
    public async Task AnAnsweredQuestion_IsNotOnTheBallot()
    {
        var h = await NewAsync();
        var task = await h.FileAsync("task", "settled");
        await h.AnswerAsync(task, await h.AskAsync(task, "which?"), "this one");

        Assert.Empty(Value(await h.Ballot.GetBallot(null, default)).Roots);
    }

    [Fact]
    public async Task AQuestionInASecondProject_IsASeparateRoot_AndProjectIdNarrowsThem()
    {
        var h = await NewAsync();
        var here = await h.FileAsync("task", "here");
        var there = await h.FileAsync("task", "there", project: h.Other);
        await h.AskAsync(here, "a");
        await h.AskAsync(there, "b");

        var both = Value(await h.Ballot.GetBallot(null, default)).Roots;
        Assert.Equal(2, both.Count);
        Assert.Equal(["AER", "OPS"], both.Select(r => r.ProjectKey).Order());

        var one = Assert.Single(Value(await h.Ballot.GetBallot([h.Other], default)).Roots);
        Assert.Equal("OPS", one.ProjectKey);

        Assert.Equal(2, Value(await h.Ballot.GetBallot([h.ProjectId, h.Other], default)).Roots.Count);
    }

    [Fact]
    public async Task RootsAndChildren_AreOrderedByTheOldestQuestionBeneathThem()
    {
        var h = await NewAsync();

        // Root A holds the middle question under one story and the newest under
        // another; root B holds the oldest. Within A, the story with the older
        // question comes first although it was filed second.
        var rootA = await h.FileAsync("epic", "A");
        var newer = await h.FileAsync("story", "newer", parentId: rootA.Id);
        var older = await h.FileAsync("story", "older", parentId: rootA.Id);
        var rootB = await h.FileAsync("task", "B");
        await h.AskAsync(newer, "newest", at: Now.AddHours(3));
        await h.AskAsync(older, "middle", at: Now.AddHours(2));
        await h.AskAsync(rootB, "oldest", at: Now.AddHours(1));

        var roots = Value(await h.Ballot.GetBallot(null, default)).Roots;

        Assert.Equal([h.Key(rootB), h.Key(rootA)], roots.Select(r => r.Key));
        Assert.Equal([h.Key(older), h.Key(newer)], roots[1].Children.Select(c => c.Key));
    }

    [Fact]
    public async Task ADeepChain_IsDrawnLevelByLevelWithEachContainerOnce()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "e");
        var story = await h.FileAsync("story", "s", parentId: epic.Id);
        var task = await h.FileAsync("task", "t", parentId: story.Id);
        await h.AskAsync(task, "deep");
        await h.AskAsync(story, "mid");

        var root = Assert.Single(Value(await h.Ballot.GetBallot(null, default)).Roots);
        var mid = Assert.Single(root.Children);
        var leaf = Assert.Single(mid.Children);

        Assert.Equal([h.Key(epic), h.Key(story), h.Key(task)], new[] { root.Key, mid.Key, leaf.Key });
        Assert.Equal("mid", Assert.Single(mid.Questions).Body);
        Assert.Equal("deep", Assert.Single(leaf.Questions).Body);
    }

    [Fact]
    public async Task TheExcerpt_IsTheFirstThreeNonBlankLines()
    {
        var h = await NewAsync();
        var task = await h.FileAsync("task", "t", description: "# one\n\n   \n**two**\nthree\nfour");
        await h.AskAsync(task, "?");

        var root = Assert.Single(Value(await h.Ballot.GetBallot(null, default)).Roots);

        Assert.Equal("# one\n**two**\nthree", root.Excerpt);
    }

    [Fact]
    public async Task AnExcerptOverThreeHundredCharacters_IsCutWithAFullStop()
    {
        var h = await NewAsync();
        var task = await h.FileAsync("task", "t", description: new string('x', 400));
        await h.AskAsync(task, "?");

        var root = Assert.Single(Value(await h.Ballot.GetBallot(null, default)).Roots);

        Assert.Equal(new string('x', 300) + ".", root.Excerpt);
    }

    [Fact]
    public async Task AnExcerptOfExactlyThreeHundredCharacters_IsLeftAlone()
    {
        var h = await NewAsync();
        var task = await h.FileAsync("task", "t", description: new string('x', 300));
        await h.AskAsync(task, "?");

        var root = Assert.Single(Value(await h.Ballot.GetBallot(null, default)).Roots);

        Assert.Equal(new string('x', 300), root.Excerpt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n\n \t\n")]
    public async Task NoDescription_OrAnAllBlankOne_IsAnEmptyExcerpt(string description)
    {
        var h = await NewAsync();
        var task = await h.FileAsync("task", "t", description: description);
        await h.AskAsync(task, "?");

        var root = Assert.Single(Value(await h.Ballot.GetBallot(null, default)).Roots);

        Assert.Equal("", root.Excerpt);
    }

    [Fact]
    public async Task ATerminalAncestor_StillComesBackAsAContainerMarkedTerminal()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "closed epic", statusId: h.Done);
        var task = await h.FileAsync("task", "still open", parentId: epic.Id);
        await h.AskAsync(task, "?");

        var root = Assert.Single(Value(await h.Ballot.GetBallot(null, default)).Roots);
        var child = Assert.Single(root.Children);

        Assert.True(root.IsTerminal);
        Assert.False(child.IsTerminal);
        Assert.Single(child.Questions);
    }

    [Fact]
    public async Task AQuestionOnATerminalIssue_IsNotWaiting()
    {
        var h = await NewAsync();
        var task = await h.FileAsync("task", "finished", statusId: h.Done);
        await h.AskAsync(task, "?");

        Assert.Empty(Value(await h.Ballot.GetBallot(null, default)).Roots);
    }

    // ---- The harness ----

    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private class Harness
    {
        public required HatchContext Db { get; init; }
        public required BallotController Ballot { get; init; }
        public required int ProjectId { get; init; }
        public required int Other { get; init; }
        public required int Todo { get; init; }
        public required int Done { get; init; }

        private int next = 1;

        public async Task<EfHatchIssue> FileAsync(
            string type, string title, int? statusId = null, long? parentId = null, int? project = null, string description = "")
        {
            var issue = new EfHatchIssue
            {
                ProjectId = project ?? ProjectId,
                Number = next++,
                Type = type,
                Title = title,
                Description = description,
                StatusId = statusId ?? Todo,
                Rank = 1024,
                ParentId = parentId,
                CreatedBy = "operator",
                CreatedAt = Now,
                UpdatedAt = Now,
            };

            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        public async Task<EfHatchComment> AskAsync(EfHatchIssue issue, string body, DateTimeOffset? at = null)
        {
            var comment = new EfHatchComment
            {
                IssueId = issue.Id,
                Author = "hatch-agent",
                Body = body,
                Kind = EfHatchComment.Question,
                CreatedAt = at ?? Now,
            };

            Db.Comments.Add(comment);
            await Db.SaveChangesAsync();
            return comment;
        }

        public async Task AnswerAsync(EfHatchIssue issue, EfHatchComment question, string body)
        {
            Db.Comments.Add(new EfHatchComment
            {
                IssueId = issue.Id,
                Author = "operator",
                Body = body,
                Kind = EfHatchComment.Answer,
                AnswersId = question.Id,
                CreatedAt = Now,
            });

            await Db.SaveChangesAsync();
        }

        /// <summary>The key of an issue filed here, in whichever of the two projects it went to.</summary>
        public string Key(EfHatchIssue issue) =>
            IssueKey.Format(issue.ProjectId == ProjectId ? "AER" : "OPS", issue.Number);
    }

    /// <summary>Two projects, and a board with a column to work in and one to finish in.</summary>
    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var other = new EfHatchProject { Key = "OPS", Name = "Operations", CreatedAt = Now };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(project, other, todo, done);
        await db.SaveChangesAsync();

        return new Harness
        {
            Db = db,
            Ballot = new BallotController(db),
            ProjectId = project.Id,
            Other = other.Id,
            Todo = todo.Id,
            Done = done.Id,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");
}

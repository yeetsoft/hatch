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
/// since the page is answered from the top. The same questions come back a
/// second way, as one list ordered by the work each holds, and what is pinned
/// there is that order and the look-back fields a person acts on.
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

    // ---- The ordered list ----

    [Fact]
    public async Task AnEpicQuestionOverThreeOpenTasks_ComesBeforeAStoryQuestionOverOne()
    {
        var h = await NewAsync();

        // The story is asked first, so only what it holds can put it second.
        var story = await h.FileAsync("story", "small");
        await h.FileAsync("task", "its task", parentId: story.Id);
        var epic = await h.FileAsync("epic", "large");
        for (var i = 0; i < 3; i++) await h.FileAsync("task", $"task {i}", parentId: epic.Id);
        await h.AskAsync(story, "story question", at: Now.AddHours(1));
        await h.AskAsync(epic, "epic question", at: Now.AddHours(2));

        var ordered = Value(await h.Ballot.GetBallot(null, default)).Questions;

        Assert.Equal(["epic question", "story question"], ordered.Select(q => q.Question.Body));
        Assert.Equal([4, 2], ordered.Select(q => q.Holds));
        Assert.All(ordered, q => Assert.Null(q.WentAheadOn));
        Assert.All(ordered, q => Assert.Null(q.Landings));
    }

    [Fact]
    public async Task AQuestionOnALeaf_HoldsOne()
    {
        var h = await NewAsync();
        var task = await h.FileAsync("task", "alone");
        await h.AskAsync(task, "which?");

        var entry = Assert.Single(Value(await h.Ballot.GetBallot(null, default)).Questions);

        Assert.Equal(1, entry.Holds);
    }

    [Fact]
    public async Task AHoldAndABuildQuestionHoldingTheSame_ComeHoldFirst()
    {
        var h = await NewAsync();
        var build = await h.FileAsync("task", "build one");
        var hold = await h.FileAsync("task", "hold one");
        await h.AskAsync(build, "build question", at: Now.AddHours(1), level: QuestionLevels.BuildName);
        await h.AskAsync(hold, "hold question", at: Now.AddHours(2), level: QuestionLevels.HoldName);

        var ordered = Value(await h.Ballot.GetBallot(null, default)).Questions;

        Assert.Equal(["hold question", "build question"], ordered.Select(q => q.Question.Body));
    }

    [Fact]
    public async Task TheSameLevelAndHolds_ComeOldestAskedFirst()
    {
        var h = await NewAsync();
        var one = await h.FileAsync("task", "one");
        var two = await h.FileAsync("task", "two");
        await h.AskAsync(one, "newer", at: Now.AddHours(2));
        await h.AskAsync(two, "older", at: Now.AddHours(1));

        var ordered = Value(await h.Ballot.GetBallot(null, default)).Questions;

        Assert.Equal(["older", "newer"], ordered.Select(q => q.Question.Body));
    }

    [Fact]
    public async Task LookBacks_SitAfterEveryOpenQuestion_OldestFirst()
    {
        var h = await NewAsync();

        // An open question over one task is asked last; the look-backs, asked
        // earlier, still come after it.
        var epic = await h.FileAsync("epic", "closed epic", statusId: h.Done);
        await h.FileAsync("task", "still open", parentId: epic.Id);
        var shipped = await h.FileAsync("task", "shipped", statusId: h.Done);
        var waiting = await h.FileAsync("task", "waiting");
        await h.AskAsync(shipped, "second look-back", at: Now.AddHours(2), level: QuestionLevels.MergeName);
        await h.AskAsync(epic, "first look-back", at: Now.AddHours(1), level: QuestionLevels.MergeName);
        await h.AskAsync(waiting, "open question", at: Now.AddHours(3));

        var ordered = Value(await h.Ballot.GetBallot(null, default)).Questions;

        Assert.Equal(["open question", "first look-back", "second look-back"], ordered.Select(q => q.Question.Body));
        Assert.Equal([false, true, true], ordered.Select(q => q.Question.LookBack));

        // The closed epic holds its one open child and not itself; the shipped task holds none.
        Assert.Equal([1, 1, 0], ordered.Select(q => q.Holds));
    }

    [Fact]
    public async Task ADoneChildAndADeferredChild_AreNotCounted()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "s");
        await h.FileAsync("task", "open", parentId: story.Id);
        await h.FileAsync("task", "finished", parentId: story.Id, statusId: h.Done);
        await h.FileAsync("task", "shelved", parentId: story.Id, statusId: h.Shelved);
        await h.AskAsync(story, "?");

        var entry = Assert.Single(Value(await h.Ballot.GetBallot(null, default)).Questions);

        Assert.Equal(2, entry.Holds);
    }

    [Fact]
    public async Task ALookBack_CarriesWhatTheWorkWentAheadOnAndWhatHappenedSince()
    {
        var h = await NewAsync();
        var merged = await h.FileAsync("task", "merged", statusId: h.Done);
        var landed = await h.FileAsync("task", "landed", statusId: h.Done);
        var plain = await h.FileAsync("task", "plain", statusId: h.Done);
        merged.ClosedAt = Now.AddHours(5);
        await h.AskAsync(
            merged, "which route?", at: Now.AddHours(1), level: QuestionLevels.MergeName,
            options: [new QuestionOptionDto("keep"), new QuestionOptionDto("drop", Recommended: true)]);
        await h.AskAsync(landed, "landed?", at: Now.AddHours(2), level: QuestionLevels.MergeName);
        await h.AskAsync(plain, "plain?", at: Now.AddHours(3), level: QuestionLevels.MergeName);
        h.Db.IssueEvents.Add(new EfHatchIssueEvent
        {
            IssueId = merged.Id,
            Actor = "train",
            Kind = EfHatchIssueEvent.StatusChanged,
            Payload = "{\"from\":\"todo\",\"to\":\"done\",\"merged\":true}",
            At = Now.AddHours(5),
        });
        h.Db.IssueEvents.Add(new EfHatchIssueEvent
        {
            IssueId = plain.Id,
            Actor = "operator",
            Kind = EfHatchIssueEvent.StatusChanged,
            Payload = "{\"from\":\"todo\",\"to\":\"done\"}",
            At = Now.AddHours(5),
        });
        h.Db.Landings.Add(new EfHatchLanding
        {
            IssueId = landed.Id,
            Remote = "git@example.test:org/repo.git",
            Canonical = "example.test/org/repo",
            Trunk = "main",
            Sha = "abc1234",
            RecordedAt = Now.AddHours(4),
            RecordedBy = "runner",
        });
        await h.Db.SaveChangesAsync();

        var ordered = Value(await h.Ballot.GetBallot(null, default)).Questions;
        var byBody = ordered.ToDictionary(q => q.Question.Body);

        var first = byBody["which route?"];
        Assert.Equal("drop", first.WentAheadOn);
        Assert.Equal("done", first.IssueStatus);
        Assert.Equal(Now.AddHours(5), first.ClosedAt);
        Assert.True(first.Merged);
        Assert.Empty(first.Landings!);

        var second = byBody["landed?"];
        Assert.Null(second.WentAheadOn);
        Assert.False(second.Merged);
        var landing = Assert.Single(second.Landings!);
        Assert.Equal(("example.test/org/repo", "abc1234"), (landing.Canonical, landing.Sha));

        Assert.False(byBody["plain?"].Merged);
    }

    [Fact]
    public async Task ProjectId_NarrowsTheOrderedListAsItNarrowsTheRoots()
    {
        var h = await NewAsync();
        var here = await h.FileAsync("task", "here");
        var there = await h.FileAsync("task", "there", project: h.Other);
        await h.AskAsync(here, "a");
        await h.AskAsync(there, "b");

        Assert.Equal(["a", "b"], Value(await h.Ballot.GetBallot(null, default)).Questions.Select(q => q.Question.Body).Order());

        var one = Assert.Single(Value(await h.Ballot.GetBallot([h.Other], default)).Questions);
        Assert.Equal("b", one.Question.Body);
    }

    [Fact]
    public async Task NoWaitingQuestions_IsAnEmptyOrderedList()
    {
        var h = await NewAsync();
        await h.FileAsync("task", "quiet");

        Assert.Empty(Value(await h.Ballot.GetBallot(null, default)).Questions);
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
        public required int Shelved { get; init; }

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

        public async Task<EfHatchComment> AskAsync(
            EfHatchIssue issue, string body, DateTimeOffset? at = null, string? level = null,
            IReadOnlyList<QuestionOptionDto>? options = null)
        {
            var comment = new EfHatchComment
            {
                IssueId = issue.Id,
                Author = "hatch-agent",
                Body = body,
                Kind = EfHatchComment.Question,
                Level = level,
                Options = Questions.WriteOptions(options),
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
        var shelved = new EfHatchStatus { Name = "shelved", SortOrder = 50, IsDeferred = true };
        db.AddRange(project, other, todo, done, shelved);
        await db.SaveChangesAsync();

        return new Harness
        {
            Db = db,
            Ballot = new BallotController(db),
            ProjectId = project.Id,
            Other = other.Id,
            Todo = todo.Id,
            Done = done.Id,
            Shelved = shelved.Id,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");
}

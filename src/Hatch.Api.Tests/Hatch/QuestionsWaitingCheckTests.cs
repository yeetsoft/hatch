using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="QuestionsWaitingCheck"/> - the <c>questions-waiting</c> check,
/// HA-2498's own <see cref="FindingCheck"/>. Pinned here: the bound on a
/// question's age, that one row serves every question past it, that it reads
/// <see cref="Questions.Waiting"/> and so never counts a question on a done
/// issue, that an issue filed from a finding is left out, the evidence and the
/// sentence, and the three-way guard on its own setting. The reconciler itself
/// is <c>GooseFindingsTests</c>'s. All of it runs on fake time.
/// </summary>
public class QuestionsWaitingCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Name_IsQuestionsWaiting()
    {
        Assert.Equal("questions-waiting", NewHarness().Check.Name);
    }

    [Fact]
    public async Task AQuestionAtTwentyThreeHours_OpensNothing_AndAtTwentyFourOpensOneFindingAboutTheBoard()
    {
        var h = NewHarness();
        await h.AskAsync("first", Now - TimeSpan.FromHours(22));

        await h.SweepAsync();
        Assert.Empty(await h.Db.GooseFindings.ToListAsync());

        // 23 hours old now.
        h.Time.Advance(TimeSpan.FromHours(1));
        await h.SweepAsync();
        Assert.Empty(await h.Db.GooseFindings.ToListAsync());

        // 24 hours exactly.
        h.Time.Advance(TimeSpan.FromHours(1));
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal("questions-waiting", finding.Check);
        Assert.Equal("board", finding.Subject);
        Assert.Null(finding.ProjectId);
        Assert.Null(finding.ClosedAt);
        Assert.Equal("1 question has waited more than 24 hours", finding.Sentence);
    }

    [Fact]
    public async Task ASecondQuestionPassingTheBound_RefreshesTheSameRow()
    {
        var h = NewHarness();
        await h.AskAsync("older", Now - TimeSpan.FromHours(25), body: "Which way?\nSecond line that is left out");
        await h.AskAsync("newer", Now - TimeSpan.FromHours(20), author: "other-runner");

        await h.SweepAsync();
        var first = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal("1 question has waited more than 24 hours", first.Sentence);

        h.Time.Advance(TimeSpan.FromHours(5));
        await h.SweepAsync();

        var refreshed = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal(first.Id, refreshed.Id);
        Assert.Equal(first.OpenedAt, refreshed.OpenedAt);
        Assert.Null(refreshed.ClosedAt);
        Assert.Equal("2 questions have waited more than 24 hours", refreshed.Sentence);

        var evidence = GooseFindingEvidence.Read(refreshed.Evidence);
        Assert.Equal(2, evidence.Count);
        Assert.Equal("AER-1", evidence[0].Label);
        Assert.Equal($"older issue - asked by Zimm-runner at {(Now - TimeSpan.FromHours(25)):O} - Which way?", evidence[0].Value);
        Assert.Equal("AER-2", evidence[1].Label);
        Assert.Equal($"newer issue - asked by other-runner at {(Now - TimeSpan.FromHours(20)):O} - A question", evidence[1].Value);
    }

    [Fact]
    public async Task AnsweringBoth_ClosesTheFinding_AndDeletesTheSighting()
    {
        var h = NewHarness();
        var a = await h.AskAsync("a", Now - TimeSpan.FromHours(30));
        var b = await h.AskAsync("b", Now - TimeSpan.FromHours(26));

        await h.SweepAsync();
        Assert.Null((await h.Db.GooseFindings.SingleAsync()).ClosedAt);

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.AnswerAsync(a);
        await h.SweepAsync();
        var stillOpen = await h.Db.GooseFindings.SingleAsync();
        Assert.Null(stillOpen.ClosedAt);
        Assert.Equal("1 question has waited more than 24 hours", stillOpen.Sentence);

        await h.AnswerAsync(b);
        await h.SweepAsync();

        var closed = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal(h.Time.GetUtcNow(), closed.ClosedAt);
        Assert.Empty(await h.Db.GooseSightings.ToListAsync());
    }

    [Fact]
    public async Task AQuestionOnADoneIssue_IsNeverCounted()
    {
        var h = NewHarness();
        await h.AskAsync("done", Now - TimeSpan.FromHours(100), terminal: true);

        var observation = await h.Check.RunAsync(CancellationToken.None);
        await h.Db.SaveChangesAsync();

        Assert.Equal("0 waiting questions, 0 past 24 hours", observation.Looked);
        Assert.Empty(await h.Db.GooseFindings.ToListAsync());
        Assert.Empty(await h.Db.GooseSightings.ToListAsync());
    }

    [Fact]
    public async Task AMergeQuestionOnADoneIssue_IsCountedUntilAnswered_AndAHoldOneIsNot()
    {
        var h = NewHarness();
        await h.AskAsync("merge", Now - TimeSpan.FromHours(100), terminal: true, level: QuestionLevels.MergeName);
        await h.AskAsync("hold", Now - TimeSpan.FromHours(100), terminal: true, level: QuestionLevels.HoldName);

        var observation = await h.Check.RunAsync(CancellationToken.None);
        await h.Db.SaveChangesAsync();

        Assert.Equal("1 waiting questions, 1 past 24 hours", observation.Looked);
    }

    [Fact]
    public async Task AQuestionOnAnIssueFiledFromAFinding_IsLeftOut()
    {
        var h = NewHarness();
        var question = await h.AskAsync("filed", Now - TimeSpan.FromHours(30));

        h.Db.GooseFindings.Add(new EfHatchGooseFinding
        {
            Check = "some-other-check", Subject = "whatever", Token = "whatever",
            BeganAt = Now, OpenedAt = Now, Sentence = "s", BugIssueId = question.IssueId,
        });
        await h.Db.SaveChangesAsync();

        await h.SweepAsync();

        Assert.Empty(await h.Db.GooseFindings.Where(f => f.Check == "questions-waiting").ToListAsync());
    }

    [Fact]
    public async Task Looked_SaysHowManyWereWaitingAndHowManyWerePastTheBound()
    {
        var h = NewHarness();
        await h.AskAsync("a", Now - TimeSpan.FromHours(30));
        await h.AskAsync("b", Now - TimeSpan.FromHours(1));
        await h.AskAsync("c", Now - TimeSpan.FromHours(25));

        var observation = await h.Check.RunAsync(CancellationToken.None);

        Assert.Equal("3 waiting questions, 2 past 24 hours", observation.Looked);
    }

    [Theory]
    [InlineData(24, 24)]
    [InlineData(6, 6)]
    [InlineData(-1, 24)]
    public async Task TheBound_FallsBackToTwentyFourHoursOnlyWhenNegative(int configured, int expectedHours)
    {
        var h = NewHarness(questionHours: configured);
        await h.AskAsync("a", Now);

        h.Time.Advance(TimeSpan.FromHours(expectedHours).Subtract(TimeSpan.FromSeconds(1)));
        await h.SweepAsync();
        Assert.Empty(await h.Db.GooseFindings.ToListAsync());

        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.SweepAsync();
        Assert.Single(await h.Db.GooseFindings.ToListAsync());
    }

    [Fact]
    public async Task ZeroTurnsTheCheckOff_AndClosesAnOpenFinding()
    {
        var h = NewHarness();
        await h.AskAsync("a", Now - TimeSpan.FromHours(30));

        await h.SweepAsync();
        Assert.Null((await h.Db.GooseFindings.SingleAsync()).ClosedAt);

        h.Check = h.BuildCheck(0);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();

        Assert.Equal(h.Time.GetUtcNow(), (await h.Db.GooseFindings.SingleAsync()).ClosedAt);
        Assert.Empty(await h.Db.GooseSightings.ToListAsync());
    }

    private static Harness NewHarness(int? questionHours = null)
    {
        var h = new Harness();
        if (questionHours is { } hours) h.Check = h.BuildCheck(hours);
        return h;
    }

    private sealed class Harness
    {
        public HatchContext Db { get; }

        public FakeTimeProvider Time { get; } = new(Now);

        public QuestionsWaitingCheck Check { get; set; }

        private readonly EfHatchProject project;
        private readonly EfHatchStatus open;
        private readonly EfHatchStatus done;
        private int nextNumber = 1;

        public Harness()
        {
            Db = new(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
            open = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
            done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
            Db.AddRange(project, open, done);
            Db.SaveChanges();

            Check = BuildCheck(null);
        }

        public QuestionsWaitingCheck BuildCheck(int? questionHours) =>
            new(Db, Options.Create(questionHours is { } hours ? new HatchOptions { GooseQuestionHours = hours } : new HatchOptions()), Time);

        public async Task SweepAsync()
        {
            await Check.RunAsync(CancellationToken.None);
            await Db.SaveChangesAsync();
        }

        /// <summary>A new issue in the open column (or the done one) with one question asked at <paramref name="at"/>.</summary>
        public async Task<EfHatchComment> AskAsync(
            string name, DateTimeOffset at, string author = "Zimm-runner", string body = "A question", bool terminal = false, string? level = null)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = project.Id, Number = nextNumber++, Type = "task",
                Title = $"{name} issue", StatusId = (terminal ? done : open).Id, Rank = nextNumber,
                CreatedBy = "person", CreatedAt = Now - TimeSpan.FromDays(10), UpdatedAt = Now - TimeSpan.FromDays(10),
            };
            var question = new EfHatchComment
            {
                Issue = issue, Author = author, Body = body, Kind = EfHatchComment.Question, CreatedAt = at, Level = level,
            };
            Db.AddRange(issue, question);
            await Db.SaveChangesAsync();
            return question;
        }

        public async Task AnswerAsync(EfHatchComment question)
        {
            Db.Comments.Add(new EfHatchComment
            {
                IssueId = question.IssueId, Author = "person", Body = "Yes", Kind = EfHatchComment.Answer,
                AnswersId = question.Id, CreatedAt = Time.GetUtcNow(),
            });
            await Db.SaveChangesAsync();
        }
    }
}

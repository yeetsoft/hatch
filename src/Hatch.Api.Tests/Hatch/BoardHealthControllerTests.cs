using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Models.Auth;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <c>GET /api/board/health</c>: the fold of <see cref="BoardHealthReport"/>
/// wired to the database, and the two promises it makes beyond the fold - the
/// last bar is the attention panel's own count, and the bands are the cumulative
/// flow's - pinned against the in-memory database.
/// </summary>
public class BoardHealthControllerTests
{
    // An 8-hour window is 20-minute bars: the last starts 12:00, so From is 04:20 and To 12:20.
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 7, 30, TimeSpan.Zero);

    private static DateTimeOffset At(int hour, int minute) => new(2026, 9, 9, hour, minute, 0, TimeSpan.Zero);

    private const string SecretTitle = "a title nobody may read";

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required BoardHealthController Health { get; init; }
        public required AttentionController Attention { get; init; }
        public required ReportsController Reports { get; init; }
        public required global::Hatch.Api.Services.Auth.AuthOptions Auth { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required AuthService Keys { get; init; }
        public required int ProjectId { get; init; }
        public required int Inbox { get; init; }
        public required int Todo { get; init; }
        public required int Doing { get; init; }
        public required int Review { get; init; }
        public required int Done { get; init; }

        private int next = 1;

        public string Name(int statusId) => statusId == Inbox ? "inbox" : statusId == Todo ? "todo"
            : statusId == Doing ? "doing" : statusId == Review ? "review" : "done";

        public async Task<EfHatchIssue> FileAsync(
            int statusId, DateTimeOffset? createdAt = null, string? pullRequestUrl = null, int? projectId = null, int preApproval = 0)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = projectId ?? ProjectId,
                Number = next++,
                Type = "task",
                Title = SecretTitle,
                Description = "",
                StatusId = statusId,
                Rank = 1024,
                PullRequestUrl = pullRequestUrl,
                PreApproval = preApproval,
                CreatedBy = "operator",
                CreatedAt = createdAt ?? At(2, 0),
                UpdatedAt = createdAt ?? At(2, 0),
            };
            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        public async Task EventAsync(EfHatchIssue issue, string kind, string? payload, DateTimeOffset at, string actor = "ann")
        {
            Db.IssueEvents.Add(new EfHatchIssueEvent { IssueId = issue.Id, Actor = actor, Kind = kind, Payload = payload, At = at });
            await Db.SaveChangesAsync();
        }

        /// <summary>A forward or backward move: the event, and the column the issue now stands in.</summary>
        public async Task MoveAsync(EfHatchIssue issue, int from, int to, DateTimeOffset at, string actor = "ann", string extra = "")
        {
            await EventAsync(issue, EfHatchIssueEvent.StatusChanged,
                $"{{\"from\":\"{Name(from)}\",\"to\":\"{Name(to)}\",\"fromId\":{from},\"toId\":{to}{extra}}}", at, actor);
            issue.StatusId = to;
            await Db.SaveChangesAsync();
        }

        public async Task<EfHatchComment> AskAsync(EfHatchIssue issue, DateTimeOffset at)
        {
            var question = new EfHatchComment
            {
                IssueId = issue.Id, Author = "hatch-agent", Body = "which way?", Kind = EfHatchComment.Question, CreatedAt = at,
            };
            Db.Comments.Add(question);
            await Db.SaveChangesAsync();
            return question;
        }

        public async Task AnswerAsync(EfHatchIssue issue, EfHatchComment question, DateTimeOffset at)
        {
            Db.Comments.Add(new EfHatchComment
            {
                IssueId = issue.Id, Author = "ann", Body = "that way", Kind = EfHatchComment.Answer, AnswersId = question.Id, CreatedAt = at,
            });
            await Db.SaveChangesAsync();
        }

        public async Task ConflictAsync(EfHatchIssue issue)
        {
            Db.MergeChecks.Add(new EfHatchMergeCheck
            {
                IssueId = issue.Id,
                Remote = "forge.example/owner/repo",
                Canonical = "forge.example/owner/repo",
                Trunk = "main",
                TrunkSha = new string('1', 40),
                Verdict = MergeVerdicts.Conflicted,
                Branch = "ha-1-thing",
                BranchSha = new string('2', 40),
                Files = EfHatchMergeCheck.JoinFiles([]),
                CheckedAt = At(3, 0),
                Runner = "box:/work/repo",
                CheckedBy = "runner",
            });
            await Db.SaveChangesAsync();
        }

        /// <summary>A key by this name, owned by this person, which makes the runner it speaks for.</summary>
        public async Task<EfApiKey> KeyAsync(string name, Guid? owner = null) =>
            (await Keys.CreateApiKeyAsync(name, owner, default))!.Key;

        public async Task StateAsync(string runner, DateTimeOffset at, string state, string? kind = null)
        {
            Db.RunnerStates.Add(new EfHatchRunnerState { Runner = runner, At = at, State = state, Kind = kind });
            await Db.SaveChangesAsync();
        }

        public async Task<int> AddProjectAsync(string key)
        {
            var project = new EfHatchProject { Key = key, Name = key, CreatedAt = At(1, 0) };
            Db.Projects.Add(project);
            await Db.SaveChangesAsync();
            return project.Id;
        }
    }

    private sealed class NoBackupProcess : IBackupProcess
    {
        public Task<BackupProcessResult> RunAsync(BackupProcessStart start, Stream stdout, CancellationToken ct) =>
            Task.FromResult(new BackupProcessResult(0, ""));
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var actors = new StubActorDirectory();

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = At(1, 0) };
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30 };
        var review = new EfHatchStatus { Name = "review", SortOrder = 35 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(project, inbox, todo, doing, review, done);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var auth = new global::Hatch.Api.Services.Auth.AuthOptions();
        var authOptions = Microsoft.Extensions.Options.Options.Create(auth);
        var runners = new Runners(Microsoft.Extensions.Options.Options.Create(new HatchOptions()), authOptions, new StubHatchRevision());
        var coreDb = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var keys = new AuthService(coreDb, authOptions, time, NullLogger<AuthService>.Instance);
        var sessions = new RunnerSessions(runners);
        var dispatch = new Dispatch(db, actors, TestClaims.With(), time, keys, sessions);
        var caller = new StubCallerIdentity();
        var backup = new BackupService(
            Microsoft.Extensions.Options.Options.Create(new HatchOptions { BackupPath = "" }),
            new ConfigurationBuilder().Build(), new NoBackupProcess(), new BackupStatus(time), time, NullLogger<BackupService>.Instance);

        return new Harness
        {
            Db = db,
            Health = new BoardHealthController(
                db, actors, runners, dispatch, time, new ColumnNormals(), caller, authOptions, keys, new RunnerStateLog(runners), sessions),
            Attention = new AttentionController(
                db, actors, runners, time, dispatch, backup, keys, new GooseWatch(time),
                Microsoft.Extensions.Options.Options.Create(new HatchOptions()), caller, sessions, authOptions),
            Reports = new ReportsController(db, actors, TestClaims.With(), time),
            Auth = auth,
            Caller = caller,
            Actors = actors,
            Keys = keys,
            ProjectId = project.Id,
            Inbox = inbox.Id,
            Todo = todo.Id,
            Doing = doing.Id,
            Review = review.Id,
            Done = done.Id,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");

    private static string Refusal<T>(ActionResult<T> result) =>
        Assert.IsType<string>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);

    // ---- Refusals and the shape ----

    [Fact]
    public async Task AWindowThatIsNotOneOfTheFive_IsRefused_NamingIt()
    {
        var h = await NewAsync();

        var result = await h.Health.GetBoardHealth("3h", null, default);

        Assert.Equal("a window is one of 2h, 4h, 8h, 12h, 24h - not 3h", Refusal(result));
    }

    [Fact]
    public async Task AnUnknownProject_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Health.GetBoardHealth(null, ["NOPE"], default);

        Assert.Equal("there is no project NOPE", Refusal(result));
    }

    [Fact]
    public async Task NoIssues_NoBars()
    {
        var h = await NewAsync();

        var answer = Value(await h.Health.GetBoardHealth(null, null, default));

        Assert.Empty(answer.Bars);
        Assert.Equal(0, answer.Total);
    }

    [Fact]
    public async Task TheDefault_IsTwentyMinuteBarsOverEightHours()
    {
        var h = await NewAsync();
        await h.FileAsync(h.Todo);

        var answer = Value(await h.Health.GetBoardHealth(null, null, default));

        Assert.Equal(20, answer.BarMinutes);
        Assert.Equal(24, answer.Bars.Count);
        Assert.Equal(At(4, 20), answer.From);
        Assert.Equal(At(12, 20), answer.To);
        Assert.True(answer.HasWorkColumn);
    }

    [Fact]
    public async Task AProject_NarrowsTheCounts()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var mine = await h.FileAsync(h.Todo);
        var theirs = await h.FileAsync(h.Todo, projectId: other);
        await h.MoveAsync(mine, h.Todo, h.Doing, At(5, 5));
        await h.MoveAsync(theirs, h.Todo, h.Doing, At(5, 6));

        Assert.Equal(2, Value(await h.Health.GetBoardHealth(null, null, default)).Total);
        Assert.Equal(1, Value(await h.Health.GetBoardHealth(null, ["aer"], default)).Total);
    }

    // ---- Who moved ----

    [Fact]
    public async Task AMoveByTheBoard_ARunnerAndAPerson_AreCountedOnceEach_InTheirBars()
    {
        var h = await NewAsync();
        var byBoard = await h.FileAsync(h.Todo);
        var byRunner = await h.FileAsync(h.Todo);
        var byPerson = await h.FileAsync(h.Todo);

        await h.MoveAsync(byBoard, h.Todo, h.Doing, At(5, 5), "hatch-board", ",\"express\":true");
        await h.EventAsync(byRunner, EfHatchIssueEvent.ClaimTaken, null, At(5, 30), "runner-1");
        await h.MoveAsync(byRunner, h.Todo, h.Doing, At(6, 10), "runner-1");
        await h.MoveAsync(byPerson, h.Todo, h.Doing, At(7, 30), "ann");

        var answer = Value(await h.Health.GetBoardHealth(null, null, default));

        Assert.Equal(3, answer.Total);
        Assert.Equal(0, answer.PreviousTotal);
        Assert.Equal(1, answer.Bars[2].Board);
        Assert.Equal(1, answer.Bars[5].Runner);
        Assert.Equal(1, answer.Bars[9].Person);
        Assert.Equal(3, answer.Bars.Sum(b => b.Moved));
        Assert.Equal(1, answer.Bars.Sum(b => b.Board));
        Assert.Equal(1, answer.Bars.Sum(b => b.Runner));
        Assert.Equal(1, answer.Bars.Sum(b => b.Person));
    }

    // ---- Waiting on you ----

    [Fact]
    public async Task TheLastBar_IsWhatTheAttentionPanelCounts()
    {
        var h = await NewAsync();

        await h.FileAsync(h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        var conflicted = await h.FileAsync(h.Review, pullRequestUrl: "https://forge.example/pulls/2");
        await h.ConflictAsync(conflicted);
        await h.FileAsync(h.Review, pullRequestUrl: "https://forge.example/pulls/3", preApproval: PreApprovalLevels.PreApproved);
        var working = await h.FileAsync(h.Doing);
        await h.AskAsync(working, At(8, 0));

        var attention = Value(await h.Attention.GetAttention(default));
        var answer = Value(await h.Health.GetBoardHealth(null, null, default));
        var last = answer.Bars[^1];

        Assert.Equal(attention.Reviews.Count, last.WaitingPullRequests);
        Assert.Equal(attention.Questions.Count, last.WaitingQuestions);
        Assert.Equal(attention.Reviews.Count + attention.Questions.Count, last.WaitingPullRequests + last.WaitingQuestions);

        // The conflicted row is the loop's, never a person's.
        Assert.DoesNotContain(attention.Reviews, r => r.PullRequestUrl.EndsWith("/2", StringComparison.Ordinal));
        Assert.Equal(1, last.WaitingQuestions);
        // The plain row and the question: the conflicted row is the loop's and the pre-approved
        // one is carried by the judge, which only the panel knows.
        Assert.Equal(1, last.WaitingPullRequests);
        Assert.Equal(2, last.WaitingPullRequests + last.WaitingQuestions);
        Assert.NotNull(last.OldestWaitSeconds);
    }

    [Fact]
    public async Task NothingInTheAnswer_NamesAnIssue()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync(h.Review, pullRequestUrl: "https://forge.example/pulls/1");
        await h.AskAsync(issue, At(8, 0));
        var mover = await h.FileAsync(h.Todo);
        await h.MoveAsync(mover, h.Todo, h.Doing, At(5, 5));

        var json = JsonSerializer.Serialize(Value(await h.Health.GetBoardHealth(null, null, default)));

        Assert.DoesNotContain("AER-", json);
        Assert.DoesNotContain(SecretTitle, json);
        Assert.DoesNotContain("pulls/1", json);
    }

    // ---- What happened before the window ----

    [Fact]
    public async Task OldHistory_IsIgnoredByTheTotals_AndTheOldQuestionIsInNoBar()
    {
        var h = await NewAsync();

        // Moved forward 30 hours ago: before the previous window opened too.
        var old = await h.FileAsync(h.Todo, createdAt: At(2, 0).AddDays(-3));
        await h.MoveAsync(old, h.Todo, h.Doing, Now.AddHours(-30));

        // Asked two days ago and answered a day ago.
        var answered = await h.AskAsync(old, Now.AddDays(-2));
        await h.AnswerAsync(old, answered, Now.AddDays(-1));

        var answer = Value(await h.Health.GetBoardHealth(null, null, default));

        Assert.Equal(0, answer.Total);
        Assert.Equal(0, answer.PreviousTotal);
        Assert.All(answer.Bars, b => Assert.Equal(0, b.WaitingQuestions));
        Assert.All(answer.Bars, b => Assert.Equal(1, b.InFlight));
    }

    [Fact]
    public async Task APreApprovalChangedBeforeTheWindow_StillGoverns()
    {
        var h = await NewAsync();

        // Today it carries no level of its own; the trail says it was pre-approved
        // three days ago, and nothing has changed it since, so the newest event
        // before the window is the one that says what governs.
        var review = await h.FileAsync(h.Review, createdAt: At(2, 0).AddDays(-3), pullRequestUrl: "https://forge.example/pulls/1");
        await h.EventAsync(
            review, EfHatchIssueEvent.PreApprovalChanged,
            "{\"from\":\"none\",\"to\":\"preapproved\"}", Now.AddDays(-3));

        var answer = Value(await h.Health.GetBoardHealth(null, null, default));

        Assert.All(answer.Bars.Take(23), b => Assert.Equal(0, b.WaitingPullRequests));
    }

    // ---- The bands ----

    [Fact]
    public async Task TheBands_EqualTheCumulativeFlows_AtEveryBarEnd()
    {
        var h = await NewAsync();

        var shipped = await h.FileAsync(h.Todo);
        await h.MoveAsync(shipped, h.Todo, h.Doing, At(5, 0));
        await h.MoveAsync(shipped, h.Doing, h.Review, At(6, 0));
        await h.MoveAsync(shipped, h.Review, h.Done, At(9, 10));

        var late = await h.FileAsync(h.Todo, createdAt: At(6, 30));
        await h.MoveAsync(late, h.Todo, h.Doing, At(8, 0));

        var closedBefore = await h.FileAsync(h.Todo, createdAt: At(1, 0));
        await h.MoveAsync(closedBefore, h.Todo, h.Doing, At(2, 0));
        await h.MoveAsync(closedBefore, h.Doing, h.Done, At(3, 0));

        await h.FileAsync(h.Todo, createdAt: At(1, 30));
        await h.FileAsync(h.Review, createdAt: At(1, 30), pullRequestUrl: "https://forge.example/pulls/9");

        var health = Value(await h.Health.GetBoardHealth("8h", null, default));
        var flow = Value(await h.Reports.GetCumulativeFlow(
            health.From.ToString("o"), health.To.ToString("o"), null, default));

        int Count(CumulativeFlowPointDto point, params int[] ids) =>
            ids.Sum(id => point.Counts[flow.Columns.ToList().IndexOf(id)]);

        for (var k = 0; k < 23; k++)
        {
            var bar = health.Bars[k];
            var point = flow.Points.Single(p => p.At == bar.End);

            Assert.Equal(Count(point, h.Todo), bar.Waiting);
            Assert.Equal(Count(point, h.Doing, h.Review), bar.InFlight);
            Assert.Equal(Count(point, h.Done), bar.Done);
        }
    }

    // ---- The runners' lanes ----

    private static BoardHealthLaneDto Lane(BoardHealthDto answer, string name) =>
        answer.Runners.Lanes.Single(l => l.Name == name);

    private static BoardHealthCellDto CellAt(BoardHealthLaneDto lane, DateTimeOffset at) =>
        lane.Cells[(int)((at - At(4, 20)).TotalMinutes / 20)];

    private static (string State, string? Why) Said(BoardHealthLaneDto lane, DateTimeOffset at) =>
        (CellAt(lane, at).State, CellAt(lane, at).Why);

    [Fact]
    public async Task ARunnerThatIsUp_HasALane_OfTwentyFourCells_AndIsCounted()
    {
        var h = await NewAsync();
        await h.KeyAsync("r1");
        await h.StateAsync("r1", At(5, 0), "running");

        var answer = Value(await h.Health.GetBoardHealth(null, null, default));

        Assert.Equal((1, 0, 1), (answer.Runners.Up, answer.Runners.Paused, answer.Runners.WithLane));
        var lane = Assert.Single(answer.Runners.Lanes);
        Assert.Equal(("r1", true, false), (lane.Name, lane.Up, lane.Paused));
        Assert.Equal(24, lane.Cells.Count);
        Assert.Equal("blank", lane.Cells[0].State);
        Assert.Equal("idle", CellAt(lane, At(5, 0)).State);
        Assert.Equal("idle", lane.Cells[23].State);
    }

    [Fact]
    public async Task NoRunners_NoLanes_AndTheBarsAreStillThere()
    {
        var h = await NewAsync();
        await h.FileAsync(h.Todo);

        var answer = Value(await h.Health.GetBoardHealth(null, null, default));

        Assert.Equal((0, 0, 0), (answer.Runners.Up, answer.Runners.Paused, answer.Runners.WithLane));
        Assert.Empty(answer.Runners.Lanes);
        Assert.Equal(24, answer.Bars.Count);
    }

    [Fact]
    public async Task AKeyThatSeesOnlyItsOwnRunner_GetsOneLane()
    {
        var h = await NewAsync();
        var mine = Guid.NewGuid();
        await h.KeyAsync("mine", mine);
        await h.KeyAsync("theirs", Guid.NewGuid());
        await h.StateAsync("mine", At(5, 0), "running");
        await h.StateAsync("theirs", At(5, 0), "running");

        h.Auth.Enabled = true;
        h.Caller.ApiKey = new EfApiKey
        {
            Name = "caller", Prefix = "hatch_ak_abc", Hash = [], CreatedAt = Now,
            Owner = new EfPerson { Name = "Ann", Role = PersonRole.User, CreatedAt = default, UpdatedAt = default },
        };
        h.Actors.Principal = new Actor(ActorKind.Person, mine, "Ann");

        var narrow = Value(await h.Health.GetBoardHealth(null, null, default));

        Assert.Equal(["mine"], narrow.Runners.Lanes.Select(l => l.Name));
        Assert.Equal((1, 1), (narrow.Runners.Up, narrow.Runners.WithLane));

        h.Auth.Enabled = false;
        var wide = Value(await h.Health.GetBoardHealth(null, null, default));

        Assert.Equal(new[] { "mine", "theirs" }, wide.Runners.Lanes.Select(l => l.Name).Order());
    }

    [Fact]
    public async Task AProject_LeavesTheLanesAlone_EvenForWorkInAnotherProject()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        await h.FileAsync(h.Todo);
        var theirs = await h.FileAsync(h.Doing, projectId: other);
        await h.KeyAsync("r1");
        await h.StateAsync("r1", At(4, 0), "running");
        await h.EventAsync(theirs, EfHatchIssueEvent.ClaimTaken, null, At(6, 5), "r1");

        var all = Value(await h.Health.GetBoardHealth(null, null, default));
        var scoped = Value(await h.Health.GetBoardHealth(null, ["aer"], default));

        Assert.Equal("working", CellAt(Lane(scoped, "r1"), At(6, 5)).State);
        Assert.Equal(
            Lane(all, "r1").Cells.Select(c => (c.State, c.Why)),
            Lane(scoped, "r1").Cells.Select(c => (c.State, c.Why)));
        Assert.Equal(all.Runners.WithLane, scoped.Runners.WithLane);
    }

    [Fact]
    public async Task TheWindowOpensOnTheNewestRowBeforeIt_NotAnOlderOne()
    {
        var h = await NewAsync();
        await h.KeyAsync("r1");
        await h.StateAsync("r1", At(0, 30), "gone");
        await h.StateAsync("r1", At(1, 0), "running");
        await h.StateAsync("r1", At(3, 0), "paused");
        await h.StateAsync("r1", At(6, 0), "running");

        var lane = Lane(Value(await h.Health.GetBoardHealth(null, null, default)), "r1");

        Assert.Equal("paused", lane.Cells[0].State);
        Assert.Equal("paused", CellAt(lane, At(5, 40)).State);
        Assert.Equal("idle", CellAt(lane, At(6, 0)).State);
    }

    [Fact]
    public async Task ARunnerWhoseLastRowBeforeTheWindowIsGone_HasNoLane()
    {
        var h = await NewAsync();
        await h.KeyAsync("r1");
        await h.StateAsync("r1", At(1, 0), "running");
        await h.StateAsync("r1", At(2, 0), "gone");

        var answer = Value(await h.Health.GetBoardHealth(null, null, default));

        Assert.Empty(answer.Runners.Lanes);
    }

    [Fact]
    public async Task AClaimOpenFromBeforeTheWindow_IsWorking_ButOneClosedBeforeItIsNot()
    {
        var h = await NewAsync();
        var open = await h.FileAsync(h.Doing);
        var closed = await h.FileAsync(h.Doing);
        await h.KeyAsync("open");
        await h.KeyAsync("closed");
        await h.StateAsync("open", At(1, 0), "running");
        await h.StateAsync("closed", At(1, 0), "running");
        await h.EventAsync(open, EfHatchIssueEvent.ClaimTaken, null, At(3, 0), "open");
        await h.EventAsync(closed, EfHatchIssueEvent.ClaimTaken, null, At(3, 0), "closed");
        await h.EventAsync(closed, EfHatchIssueEvent.ClaimReleased, "{\"outcome\":\"worked\"}", At(3, 30), "closed");

        var answer = Value(await h.Health.GetBoardHealth(null, null, default));

        Assert.All(Lane(answer, "open").Cells, c => Assert.Equal("working", c.State));
        Assert.All(Lane(answer, "closed").Cells, c => Assert.Equal("idle", c.State));
    }

    [Fact]
    public async Task ADrop_ALapse_AStall_AndAnErroredSession_AreLetGos_OfTheRunnerThatOwnsEach()
    {
        var h = await NewAsync();
        var a = await h.FileAsync(h.Doing);
        var b = await h.FileAsync(h.Doing);
        var c = await h.FileAsync(h.Doing);
        var d = await h.FileAsync(h.Doing);
        foreach (var name in new[] { "r1", "r2", "r3", "r4" })
        {
            await h.KeyAsync(name);
            await h.StateAsync(name, At(4, 0), "running");
        }

        // r1 drops a; r2 lapses on b and r3 takes it over; r3 stalls c; r4's session on d errors.
        await h.EventAsync(a, EfHatchIssueEvent.ClaimTaken, null, At(5, 0), "r1");
        await h.EventAsync(a, EfHatchIssueEvent.ClaimReleased, "{\"outcome\":\"dropped\"}", At(5, 10), "r1");
        await h.EventAsync(b, EfHatchIssueEvent.ClaimTaken, null, At(5, 0), "r2");
        await h.EventAsync(b, EfHatchIssueEvent.ClaimLapsed, null, At(6, 0), "r3");
        await h.EventAsync(b, EfHatchIssueEvent.ClaimTaken, null, At(6, 0), "r3");
        await h.EventAsync(c, EfHatchIssueEvent.Stalled, null, At(7, 0), "r3");
        await h.EventAsync(d, EfHatchIssueEvent.ClaimTaken, null, At(8, 0), "r4");
        h.Db.WorkLog.Add(new EfHatchWorkLogEntry
        {
            IssueId = d.Id, SessionId = "s", StartedAt = At(8, 0), EndedAt = At(8, 5), DurationMs = 300_000, IsError = true, CreatedAt = At(8, 30),
        });
        await h.Db.SaveChangesAsync();

        var answer = Value(await h.Health.GetBoardHealth(null, null, default));

        Assert.Equal(("let-go", "dropped"), Said(Lane(answer, "r1"), At(5, 10)));
        Assert.Equal(("let-go", "lapsed"), Said(Lane(answer, "r2"), At(6, 0)));
        Assert.Equal("working", CellAt(Lane(answer, "r3"), At(6, 20)).State);
        Assert.Equal(("let-go", "stalled"), Said(Lane(answer, "r3"), At(7, 0)));
        Assert.Equal(("let-go", "errored"), Said(Lane(answer, "r4"), At(8, 30)));
    }

    [Fact]
    public async Task ARunnersForwardMove_IsDrawnWhereTheMovedTileCountsIt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync(h.Todo);
        await h.KeyAsync("r1");
        await h.StateAsync("r1", At(4, 0), "running");
        await h.EventAsync(issue, EfHatchIssueEvent.ClaimTaken, null, At(5, 0), "r1");
        await h.MoveAsync(issue, h.Todo, h.Doing, At(5, 25), "r1");

        var answer = Value(await h.Health.GetBoardHealth(null, null, default));

        var moved = answer.Bars.Select((b, k) => b.Runner > 0 ? k : -1).Where(k => k >= 0).ToList();
        var drawn = Lane(answer, "r1").Cells.Select((c, k) => c.State == "moved" ? k : -1).Where(k => k >= 0).ToList();
        Assert.Equal([3], moved);
        Assert.Equal(moved, drawn);
    }

    [Fact]
    public async Task TheLanesWriteNothing()
    {
        var h = await NewAsync();
        await h.KeyAsync("r1");
        await h.StateAsync("r1", At(4, 0), "running");
        var rows = await h.Db.RunnerStates.CountAsync();

        Value(await h.Health.GetBoardHealth(null, null, default));

        Assert.Equal(rows, await h.Db.RunnerStates.CountAsync());
    }
}

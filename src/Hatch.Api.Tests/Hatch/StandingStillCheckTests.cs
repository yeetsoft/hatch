using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="StandingStillCheck"/> - HA-2664: a ticket that has stood in one
/// column for hours with no runner on it and nobody it is waiting for. Pinned
/// here: what is watched, what keeps an issue out, what restarts the clock, and
/// what a finding says. The reconciler itself is <c>GooseFindingsTests</c>'s.
/// No test asserts the review sentence's exact words: the check reads marks, not words.
/// </summary>
public class StandingStillCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Bound = TimeSpan.FromHours(4);

    [Fact]
    public void TheCheck_IsNamedStandingStill()
    {
        var h = new Harness();
        Assert.Equal("standing-still", h.NewCheck().Name);
        Assert.Equal(StandingStillCheck.CheckName, h.NewCheck().Name);
    }

    [Fact]
    public async Task ThePreApprovedReviewTaskWithNoPullRequest_OpensOneFindingAtTheBound_KeptAcrossSweeps_AndClosesWhenItMoves()
    {
        var h = new Harness();
        var (_, task) = await h.SeedReviewShapeAsync();

        await h.SweepAsync();
        h.Time.Advance(Bound - TimeSpan.FromMinutes(1));
        await h.SweepAsync();
        Assert.Empty(await h.OpenFindingsAsync());

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();

        var finding = Assert.Single(await h.OpenFindingsAsync());
        var key = h.KeyOf(task);
        Assert.Equal(key, finding.Subject);
        Assert.Equal(h.ProjectId, finding.ProjectId);
        Assert.Equal($"{key} has stood in review with nothing set to move it", finding.Sentence);

        var evidence = GooseFindingEvidence.Read(finding.Evidence);
        Assert.StartsWith("review, entered ", evidence.Single(e => e.Label == "column").Value);
        Assert.False(string.IsNullOrWhiteSpace(evidence.Single(e => e.Label == "queue").Value));
        Assert.Equal("none", evidence.Single(e => e.Label == "last claim").Value);
        Assert.Equal("none recorded", evidence.Single(e => e.Label == "pull request").Value);

        // The same finding, not a new one, across many more sweeps.
        for (var i = 0; i < 10; i++)
        {
            h.Time.Advance(TimeSpan.FromMinutes(30));
            await h.SweepAsync();
        }

        Assert.Equal(finding.Id, Assert.Single(await h.OpenFindingsAsync()).Id);
        Assert.Single(await h.Db.GooseFindings.ToListAsync());

        await h.MoveAsync(task, h.Done);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.SweepAsync();

        Assert.Empty(await h.OpenFindingsAsync());
        Assert.Equal(h.Time.GetUtcNow(), (await h.Db.GooseFindings.SingleAsync()).ClosedAt);
    }

    [Fact]
    public async Task AFinding_SaysTheLastClaimAndThePullRequestRecorded()
    {
        var h = new Harness();
        var issue = await h.SeedAsync("task", h.InProgress);

        h.Claim(issue, "alder");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.ReleaseClaim(issue);
        var released = h.Time.GetUtcNow();
        issue.PullRequestUrl = "https://example.com/o/r/pull/9";
        await h.Db.SaveChangesAsync();

        await h.SweepAsync();
        h.Time.Advance(Bound);
        await h.SweepAsync();

        var evidence = GooseFindingEvidence.Read(Assert.Single(await h.OpenFindingsAsync()).Evidence);
        Assert.Equal(
            $"alder at host:/one, taken {Now:O}, released {released:O}",
            evidence.Single(e => e.Label == "last claim").Value);
        Assert.Equal("https://example.com/o/r/pull/9", evidence.Single(e => e.Label == "pull request").Value);
    }

    [Fact]
    public async Task AFindingOnARowTheQueueCallsClear_SaysClear()
    {
        var h = new Harness();
        await h.SeedAsync("task", h.InProgress);
        h.AddPlaybook(h.InProgress, h.Review);

        await h.SweepAsync();
        h.Time.Advance(Bound);
        await h.SweepAsync();

        var evidence = GooseFindingEvidence.Read(Assert.Single(await h.OpenFindingsAsync()).Evidence);
        Assert.Equal("clear", evidence.Single(e => e.Label == "queue").Value);
    }

    [Fact]
    public async Task ALiveClaim_KeepsTheIssueOut_AndAClaimTakenAndReleasedBetweenSweeps_RestartsTheClock()
    {
        var h = new Harness();
        var issue = await h.SeedAsync("task", h.InProgress);

        await h.SweepAsync();

        // Held live: not a candidate at all, however long.
        h.Time.Advance(TimeSpan.FromHours(3));
        h.Claim(issue, "alder");
        await h.SweepAsync();
        Assert.Empty(await h.Db.GooseSightings.Where(s => s.Check == "standing-still").ToListAsync());

        // Released: the clock starts again on the next sweep.
        h.ReleaseClaim(issue);
        await h.SweepAsync();
        h.Time.Advance(Bound - TimeSpan.FromMinutes(1));
        await h.SweepAsync();
        Assert.Empty(await h.OpenFindingsAsync());

        // Taken and released between two sweeps: a new token, so a new clock.
        h.Claim(issue, "alder");
        h.ReleaseClaim(issue);
        await h.SweepAsync();

        h.Time.Advance(Bound - TimeSpan.FromMinutes(1));
        await h.SweepAsync();
        Assert.Empty(await h.OpenFindingsAsync());

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();
        Assert.Single(await h.OpenFindingsAsync());
    }

    [Fact]
    public async Task AFindingCloses_WhenARunnerClaimsTheIssue()
    {
        var h = new Harness();
        var issue = await h.SeedAsync("task", h.InProgress);

        await h.SweepAsync();
        h.Time.Advance(Bound);
        await h.SweepAsync();
        Assert.Single(await h.OpenFindingsAsync());

        h.Time.Advance(TimeSpan.FromSeconds(1));
        h.Claim(issue, "alder");
        await h.SweepAsync();

        Assert.Empty(await h.OpenFindingsAsync());
        Assert.Equal(h.Time.GetUtcNow(), (await h.Db.GooseFindings.SingleAsync()).ClosedAt);
    }

    [Fact]
    public async Task AFindingCloses_AndAnotherClockStarts_WhenTheIssueChangesColumn()
    {
        var h = new Harness();
        var issue = await h.SeedAsync("task", h.InProgress);

        await h.SweepAsync();
        h.Time.Advance(Bound);
        await h.SweepAsync();
        Assert.Single(await h.OpenFindingsAsync());

        // Still watched, but a different column: the old finding closes, and the
        // new condition has stood for no time at all.
        await h.MoveAsync(issue, h.Review);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();
        Assert.Empty(await h.OpenFindingsAsync());

        h.Time.Advance(Bound - TimeSpan.FromMinutes(1));
        await h.SweepAsync();
        Assert.Empty(await h.OpenFindingsAsync());

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();
        var finding = Assert.Single(await h.OpenFindingsAsync());
        Assert.Contains("has stood in review", finding.Sentence);
    }

    public enum Line { Person, Approver, FiledBug, LiveClaim }

    [Theory]
    [InlineData(Line.Person)]
    [InlineData(Line.Approver)]
    [InlineData(Line.FiledBug)]
    [InlineData(Line.LiveClaim)]
    public async Task EachLineThatKeepsAnIssueOut_RestartsTheClockWhenItEnds(Line line)
    {
        var h = new Harness();
        // The approver line is a review row that starts pre-approved - a candidate -
        // and is then set back below pre-approved, so it waits for an approver.
        var issue = line == Line.Approver
            ? (await h.SeedReviewShapeAsync()).Task
            : await h.SeedAsync("task", h.InProgress);

        // It stands for three hours, as a candidate.
        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromHours(3));
        await h.SweepAsync();

        // The line is true: out, and for longer than the bound in all.
        await h.BeginAsync(line, issue);
        await h.SweepAsync();
        Assert.Empty(await h.Db.GooseSightings.Where(s => s.Check == "standing-still").ToListAsync());
        h.Time.Advance(TimeSpan.FromHours(3));
        await h.SweepAsync();
        Assert.Empty(await h.OpenFindingsAsync());

        // The line ends: the clock starts at this sweep, not before.
        await h.EndAsync(line, issue);
        await h.SweepAsync();
        h.Time.Advance(Bound - TimeSpan.FromMinutes(1));
        await h.SweepAsync();
        Assert.Empty(await h.OpenFindingsAsync());

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();
        Assert.Single(await h.OpenFindingsAsync());
    }

    [Fact]
    public async Task AFindingCloses_WhenTheIssueStopsBeingACandidate()
    {
        var h = new Harness();
        var issue = await h.SeedAsync("task", h.InProgress);

        await h.SweepAsync();
        h.Time.Advance(Bound);
        await h.SweepAsync();
        Assert.Single(await h.OpenFindingsAsync());

        await h.BeginAsync(Line.Person, issue);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.SweepAsync();

        Assert.Empty(await h.OpenFindingsAsync());
    }

    [Fact]
    public async Task AParentWithAnOpenChild_IsNotWatched_AndItsChildIs()
    {
        var h = new Harness();
        var story = await h.SeedAsync("story", h.InProgress);
        var task = await h.SeedAsync("task", h.InProgress, parentId: story.Id);

        await h.SweepAsync();
        h.Time.Advance(Bound);
        await h.SweepAsync();

        Assert.Equal(h.KeyOf(task), Assert.Single(await h.OpenFindingsAsync()).Subject);
    }

    [Fact]
    public async Task AParentWhoseChildrenAreAllDone_IsWatched()
    {
        var h = new Harness();
        var story = await h.SeedAsync("story", h.InProgress);
        await h.SeedAsync("task", h.Done, parentId: story.Id);

        await h.SweepAsync();
        h.Time.Advance(Bound);
        await h.SweepAsync();

        Assert.Equal(h.KeyOf(story), Assert.Single(await h.OpenFindingsAsync()).Subject);
    }

    [Fact]
    public async Task ATaskWaitingOnItsStorysSharedPullRequest_IsNotWatched_AndTheStoryIsOnceAllItsTasksWait()
    {
        var h = new Harness();
        var story = await h.SeedAsync("story", h.InProgress);
        story.OnePullRequest = true;
        await h.Db.SaveChangesAsync();
        var waiting = await h.SeedAsync("task", h.Review, parentId: story.Id);
        var working = await h.SeedAsync("task", h.InProgress, parentId: story.Id);

        await h.SweepAsync();
        h.Time.Advance(Bound);
        await h.SweepAsync();

        // The waiting task is not watched, the story still has one task to work,
        // and that one is.
        Assert.Equal(h.KeyOf(working), Assert.Single(await h.OpenFindingsAsync()).Subject);

        await h.MoveAsync(working, h.Review);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.SweepAsync();
        Assert.Empty(await h.OpenFindingsAsync());

        // Every task now waits on the story: the story is the one standing still.
        h.Time.Advance(Bound);
        await h.SweepAsync();
        Assert.Equal(h.KeyOf(story), Assert.Single(await h.OpenFindingsAsync()).Subject);
    }

    [Fact]
    public async Task AReviewRowWaitingForAnOwnerOrApprover_IsNotWatched()
    {
        var h = new Harness();
        await h.SeedReviewShapeAsync(approved: false);

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromHours(10));
        await h.SweepAsync();

        Assert.Empty(await h.OpenFindingsAsync());
        Assert.Empty(await h.Db.GooseSightings.Where(s => s.Check == "standing-still").ToListAsync());
    }

    [Fact]
    public async Task NothingOutsideTheWorkingColumnsIsWatched()
    {
        var h = new Harness();
        await h.SeedAsync("task", h.Todo);
        await h.SeedAsync("task", h.Merging);
        await h.SeedAsync("task", h.Parked);
        await h.SeedAsync("task", h.Done);
        await h.SeedAsync("task", h.Backlog);

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromHours(10));
        await h.SweepAsync();

        Assert.Empty(await h.Db.GooseFindings.ToListAsync());
        Assert.Empty(await h.Db.GooseSightings.Where(s => s.Check == "standing-still").ToListAsync());
    }

    [Fact]
    public async Task TheImplementationAndReviewColumns_AreWatched()
    {
        var h = new Harness();
        var doing = await h.SeedAsync("task", h.InProgress);
        var reviewing = await h.SeedAsync("task", h.Review);

        await h.SweepAsync();
        h.Time.Advance(Bound);
        await h.SweepAsync();

        Assert.Equal(
            new[] { h.KeyOf(doing), h.KeyOf(reviewing) }.Order(),
            (await h.OpenFindingsAsync()).Select(f => f.Subject).Order());
    }

    [Fact]
    public async Task AnIssueFiledFromAFinding_IsNotWatched()
    {
        var h = new Harness();
        var issue = await h.SeedAsync("task", h.InProgress);
        await h.FileBugAsync(issue);

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromHours(10));
        await h.SweepAsync();

        Assert.Empty(await h.Db.GooseFindings.Where(f => f.Check == "standing-still").ToListAsync());
    }

    [Theory]
    [InlineData(4, 4 * 60)]
    [InlineData(2, 2 * 60)]
    [InlineData(-1, 4 * 60)]
    public async Task TheBound_IsConfigured_AndANegativeValueReadsAsFour(int configured, int expectedMinutes)
    {
        var h = new Harness(standingHours: configured);
        await h.SeedAsync("task", h.InProgress);

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(expectedMinutes - 1));
        await h.SweepAsync();
        Assert.Empty(await h.OpenFindingsAsync());

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();
        Assert.Single(await h.OpenFindingsAsync());
    }

    [Fact]
    public async Task ZeroTurnsTheCheckOff_OpensNothing_AndClosesWhatWasOpen()
    {
        var h = new Harness();
        await h.SeedAsync("task", h.InProgress);

        await h.SweepAsync();
        h.Time.Advance(Bound);
        await h.SweepAsync();
        Assert.Single(await h.OpenFindingsAsync());

        h.UseStandingHours(0);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        var observation = await h.SweepAsync();

        Assert.Equal("turned off", observation.Looked);
        Assert.Empty(await h.OpenFindingsAsync());
        Assert.Equal(h.Time.GetUtcNow(), (await h.Db.GooseFindings.SingleAsync()).ClosedAt);
        Assert.Empty(await h.Db.GooseSightings.Where(s => s.Check == "standing-still").ToListAsync());

        h.Time.Advance(TimeSpan.FromHours(10));
        await h.SweepAsync();
        Assert.Empty(await h.OpenFindingsAsync());
    }

    [Fact]
    public async Task ASweep_ReadsTheBoardOnce_ForThisCheckAndTheReadyWorkChecksTogether()
    {
        var h = new Harness();
        await h.SeedAsync("task", h.InProgress);
        var work = h.NewWork();

        var first = await work.ReadAsync(CancellationToken.None);
        var second = await work.ReadAsync(CancellationToken.None);

        Assert.NotNull(first.Board);
        Assert.Same(first.Board, second.Board);
    }

    [Fact]
    public async Task TheCheck_MovesNothingAndFilesNothing()
    {
        var h = new Harness();
        var issue = await h.SeedAsync("task", h.InProgress);
        var events = await h.Db.IssueEvents.CountAsync();

        await h.SweepAsync();
        h.Time.Advance(Bound);
        await h.SweepAsync();

        Assert.Single(await h.OpenFindingsAsync());
        Assert.Equal(h.InProgress, (await h.Db.Issues.SingleAsync(i => i.Id == issue.Id)).StatusId);
        Assert.Equal(1, await h.Db.Issues.CountAsync());
        Assert.Equal(events, await h.Db.IssueEvents.CountAsync());
    }

    private sealed class Harness
    {
        public HatchContext Db { get; }

        public FakeTimeProvider Time { get; } = new(Now);

        public int ProjectId { get; }
        public int Backlog { get; }
        public int Todo { get; }
        public int InProgress { get; }
        public int Review { get; }
        public int Merging { get; }
        public int Parked { get; }
        public int Done { get; }

        private readonly StubActorDirectory actors = new();
        private readonly IssueClaims claims = TestClaims.With();
        private readonly Dispatch dispatch;
        private readonly Runners runners;
        private readonly HatchOptions goose = new();
        private int nextNumber = 1;

        public Harness(int? standingHours = null)
        {
            Db = new(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
            var backlog = new EfHatchStatus { Name = "backlog", SortOrder = 10 };
            var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
            var doing = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
            var review = new EfHatchStatus { Name = "review", SortOrder = 35 };
            var merging = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
            var parked = new EfHatchStatus { Name = "parked", SortOrder = 38, IsDeferred = true };
            var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
            Db.AddRange(project, backlog, todo, doing, review, merging, parked, done);
            Db.SaveChanges();

            ProjectId = project.Id;
            Backlog = backlog.Id;
            Todo = todo.Id;
            InProgress = doing.Id;
            Review = review.Id;
            Merging = merging.Id;
            Parked = parked.Id;
            Done = done.Id;

            dispatch = new Dispatch(Db, actors, claims, Time);
            runners = new Runners(
                Options.Create(new HatchOptions()), Options.Create(new AuthOptions { Enabled = false }), new StubRevision());

            if (standingHours is { } hours) goose.GooseStandingHours = hours;
        }

        public void UseStandingHours(int hours) => goose.GooseStandingHours = hours;

        public string KeyOf(EfHatchIssue issue) => IssueKey.Format("AER", issue.Number);

        public ReadyWork NewWork() => new(dispatch, Db, runners, claims, Time);

        public StandingStillCheck NewCheck() => new(Db, NewWork(), Options.Create(goose), Time);

        /// <summary>One sweep, the way a new job scope runs it: a fresh read of the board, and a fresh check.</summary>
        public async Task<GooseObservation> SweepAsync()
        {
            var observation = await NewCheck().RunAsync(CancellationToken.None);
            await Db.SaveChangesAsync();
            return observation;
        }

        public Task<List<EfHatchGooseFinding>> OpenFindingsAsync() =>
            Db.GooseFindings.Where(f => f.Check == "standing-still" && f.ClosedAt == null).ToListAsync();

        public async Task<EfHatchIssue> SeedAsync(string type, int statusId, long? parentId = null)
        {
            var number = nextNumber++;
            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId, Number = number, Type = type, Title = "a thing", Description = "",
                StatusId = statusId, Rank = number, ParentId = parentId, CreatedBy = "test",
                CreatedAt = Time.GetUtcNow(), UpdatedAt = Time.GetUtcNow(),
            };
            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id, Actor = "test", Kind = EfHatchIssueEvent.Created, At = Time.GetUtcNow(),
            });
            await Db.SaveChangesAsync();
            return issue;
        }

        public void AddPlaybook(int from, int to)
        {
            Db.Playbooks.Add(new EfHatchPlaybook
            {
                FromStatusId = from, ToStatusId = to, Types = "", Shape = "any", Prompt = "do the thing", Model = "sonnet",
                Effort = "high", CreatedAt = Now, UpdatedAt = Now,
            });
            Db.SaveChanges();
        }

        /// <summary>
        /// The shape HA-2636 stood in: a story running, and its task in review, clean and
        /// green, with no pull request recorded and no claim. Pre-approved unless
        /// <paramref name="approved"/> is false, when it waits for an owner or approver.
        /// </summary>
        public async Task<(EfHatchIssue Story, EfHatchIssue Task)> SeedReviewShapeAsync(bool approved = true)
        {
            var story = await SeedAsync("story", InProgress);
            var task = await SeedAsync("task", Review, parentId: story.Id);
            if (approved) task.PreApproval = PreApprovalLevels.PreApproved;

            var remote = "https://example.com/o/r";
            var (canonical, _) = RemoteIdentity.Canonical(remote);
            Db.MergeChecks.Add(new EfHatchMergeCheck
            {
                IssueId = task.Id, Remote = remote, Canonical = canonical!, Trunk = "main", TrunkSha = new string('a', 40),
                Verdict = MergeVerdicts.Clean, Branch = "aer-1-thing", BranchSha = new string('b', 40),
                CheckedAt = Now, Runner = "host:/checkout", CheckedBy = "runner",
            });
            Db.BuildChecks.Add(new EfHatchBuildCheck
            {
                IssueId = task.Id, Remote = remote, Canonical = canonical!, Branch = "aer-1-thing", Sha = new string('b', 40),
                ShaSince = Now, Verdict = BuildVerdicts.Passed, CheckedAt = Now, Runner = "host:/checkout", CheckedBy = "runner",
            });
            await Db.SaveChangesAsync();
            return (story, task);
        }

        public async Task MoveAsync(EfHatchIssue issue, int statusId)
        {
            var row = await Db.Issues.SingleAsync(i => i.Id == issue.Id);
            Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = row.Id, Actor = "test", Kind = EfHatchIssueEvent.StatusChanged, At = Time.GetUtcNow(),
            });
            row.StatusId = statusId;
            await Db.SaveChangesAsync();
        }

        public void Claim(EfHatchIssue issue, string runner, string where = "host:/one")
        {
            var row = Db.Issues.Single(i => i.Id == issue.Id);
            var now = Time.GetUtcNow();
            row.ClaimToken = Guid.NewGuid();
            row.ClaimedBy = runner;
            row.ClaimRunner = where;
            row.ClaimedAt = now;
            row.ClaimHeartbeatAt = now;
            Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id, Actor = runner, Kind = EfHatchIssueEvent.ClaimTaken, At = now,
                Payload = $"{{\"to\":\"{runner} at {where}\",\"from\":null}}",
            });
            Db.SaveChanges();
        }

        public void ReleaseClaim(EfHatchIssue issue)
        {
            var row = Db.Issues.Single(i => i.Id == issue.Id);
            row.ClaimToken = null;
            row.ClaimedBy = null;
            row.ClaimRunner = null;
            Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id, Actor = "test", Kind = EfHatchIssueEvent.ClaimReleased, At = Time.GetUtcNow(),
            });
            Db.SaveChanges();
        }

        public async Task FileBugAsync(EfHatchIssue issue)
        {
            Db.GooseFindings.Add(new EfHatchGooseFinding
            {
                Check = "some-other-check", Subject = "whatever", Token = "whatever",
                BeganAt = Now, OpenedAt = Now, Sentence = "s", BugIssueId = issue.Id,
            });
            await Db.SaveChangesAsync();
        }

        /// <summary>Makes one line of criterion 4 true of the issue.</summary>
        public async Task BeginAsync(Line line, EfHatchIssue issue)
        {
            switch (line)
            {
                case Line.Person:
                    (await Db.Issues.SingleAsync(i => i.Id == issue.Id)).Held = true;
                    await Db.SaveChangesAsync();
                    break;
                case Line.Approver:
                    (await Db.Issues.SingleAsync(i => i.Id == issue.Id)).PreApproval = PreApprovalLevels.None;
                    await Db.SaveChangesAsync();
                    break;
                case Line.FiledBug:
                    await FileBugAsync(issue);
                    break;
                case Line.LiveClaim:
                    Claim(issue, "alder");
                    break;
            }
        }

        /// <summary>Ends it.</summary>
        public async Task EndAsync(Line line, EfHatchIssue issue)
        {
            switch (line)
            {
                case Line.Person:
                    (await Db.Issues.SingleAsync(i => i.Id == issue.Id)).Held = false;
                    await Db.SaveChangesAsync();
                    break;
                case Line.Approver:
                    (await Db.Issues.SingleAsync(i => i.Id == issue.Id)).PreApproval = PreApprovalLevels.PreApproved;
                    await Db.SaveChangesAsync();
                    break;
                case Line.FiledBug:
                    Db.GooseFindings.RemoveRange(Db.GooseFindings.Where(f => f.BugIssueId == issue.Id));
                    await Db.SaveChangesAsync();
                    break;
                case Line.LiveClaim:
                    ReleaseClaim(issue);
                    break;
            }
        }
    }

    private sealed class StubRevision : IHatchRevision
    {
        public string Revision => "dev";
        public int Sequence => 0;
        public DateTimeOffset? BuiltAt => null;
        public bool IsDevelopment => true;
    }
}

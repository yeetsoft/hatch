using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="ReadyUnclaimedCheck"/> and <see cref="ReadyNoRunnerCheck"/> -
/// HA-2497's two checks, sharing one <see cref="ReadyWork"/> read. Pinned
/// here: the two checks never both open for one project, a claim restarts
/// the unclaimed clock even taken and released between sweeps, a quiet
/// runner trips the no-runner bound instead, and ready work filed from a
/// finding opens neither. The reconciler itself - when a finding opens,
/// refreshes or closes - is <c>GooseFindingsTests</c>'s.
/// </summary>
public class ReadyWorkChecksTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Names_AreReadyUnclaimedAndReadyNoRunner()
    {
        var h = new Harness();
        var (unclaimed, noRunner) = await h.SweepAsync();
        Assert.Equal("ready-unclaimed", unclaimed.Check);
        Assert.Equal("ready-no-runner", noRunner.Check);
    }

    [Fact]
    public async Task AnIdleLiveTaker_OpensReadyUnclaimed_AtTenMinutes_AndClosesOnceClaimed()
    {
        var h = new Harness();
        var step = h.SeedReadyStep();
        await h.AddLoopRunnerAsync("alder");

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(9));
        await h.SweepAsync();
        Assert.Empty(await h.Db.GooseFindings.Where(f => f.Check == "ready-unclaimed").ToListAsync());

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync(f => f.Check == "ready-unclaimed");
        Assert.Equal("AER", finding.Subject);
        Assert.Equal(h.ProjectId, finding.ProjectId);
        Assert.Equal("AER has 1 step ready and no runner has taken one", finding.Sentence);

        var evidence = GooseFindingEvidence.Read(finding.Evidence);
        Assert.Equal($"{IssueKey.Format("AER", step.Number)} (1)", evidence.Single(e => e.Label == "steps").Value);
        Assert.Equal(Now.ToString("O"), evidence.Single(e => e.Label == "alder").Value);
        Assert.Equal("none", evidence.Single(e => e.Label == "newest claim").Value);

        h.Claim(step, "alder");
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.SweepAsync();

        var closed = await h.Db.GooseFindings.SingleAsync(f => f.Check == "ready-unclaimed");
        Assert.Equal(h.Time.GetUtcNow(), closed.ClosedAt);
    }

    [Fact]
    public async Task ReadyNoRunner_NeverOpensWhileATakerIsIdle()
    {
        var h = new Harness();
        h.SeedReadyStep();
        await h.AddLoopRunnerAsync("alder");

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(20));
        await h.SweepAsync();

        Assert.Empty(await h.Db.GooseFindings.Where(f => f.Check == "ready-no-runner").ToListAsync());
    }

    [Fact]
    public async Task TheRunnerGoesQuiet_TheUnclaimedFindingNeverOpens_ButNoRunnerOpensAtFifteenMinutes_AndClosesWhenItReturns()
    {
        var h = new Harness(goneAfterSeconds: 60);
        h.SeedReadyStep();
        await h.AddLoopRunnerAsync("alder");

        await h.SweepAsync();

        // Past the gone-after horizon with no further heartbeat: alder drops
        // out of the fleet before ten minutes ever elapse.
        h.Time.Advance(TimeSpan.FromMinutes(2));
        await h.SweepAsync();
        Assert.Empty(await h.Db.GooseFindings.Where(f => f.Check == "ready-unclaimed").ToListAsync());
        Assert.Empty(await h.Db.GooseFindings.Where(f => f.Check == "ready-no-runner").ToListAsync());

        h.Time.Advance(TimeSpan.FromMinutes(15).Subtract(TimeSpan.FromSeconds(1)));
        await h.SweepAsync();
        Assert.Empty(await h.Db.GooseFindings.Where(f => f.Check == "ready-no-runner").ToListAsync());

        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync(f => f.Check == "ready-no-runner");
        Assert.Null(finding.ClosedAt);
        Assert.Equal("AER has 1 step ready and no live runner can take it", finding.Sentence);

        h.Heartbeat("alder");
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.SweepAsync();

        var closed = await h.Db.GooseFindings.SingleAsync(f => f.Check == "ready-no-runner");
        Assert.Equal(h.Time.GetUtcNow(), closed.ClosedAt);
        Assert.Empty(await h.Db.GooseFindings.Where(f => f.Check == "ready-unclaimed" && f.ClosedAt == null).ToListAsync());
    }

    [Fact]
    public async Task AClaimTakenAndReleasedBetweenSweeps_RestartsTheUnclaimedClock()
    {
        var h = new Harness();
        var step = h.SeedReadyStep();
        await h.AddLoopRunnerAsync("alder");

        await h.SweepAsync();

        h.Time.Advance(TimeSpan.FromMinutes(6));
        h.Claim(step, "someone-else");
        h.ReleaseClaim(step);
        await h.SweepAsync();

        h.Time.Advance(TimeSpan.FromMinutes(9));
        await h.SweepAsync();
        Assert.Empty(await h.Db.GooseFindings.Where(f => f.Check == "ready-unclaimed").ToListAsync());

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();
        Assert.Single(await h.Db.GooseFindings.Where(f => f.Check == "ready-unclaimed").ToListAsync());
    }

    [Fact]
    public async Task ReadyWorkFiledFromAFinding_OpensNeitherCheck()
    {
        var h = new Harness();
        var step = h.SeedReadyStep();
        await h.AddLoopRunnerAsync("alder");

        h.Db.GooseFindings.Add(new EfHatchGooseFinding
        {
            Check = "some-other-check", Subject = "whatever", Token = "whatever",
            BeganAt = Now, OpenedAt = Now, Sentence = "s", BugIssueId = step.Id,
        });
        await h.Db.SaveChangesAsync();

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(20));
        await h.SweepAsync();

        Assert.Empty(await h.Db.GooseFindings.Where(f => f.Check == "ready-unclaimed" || f.Check == "ready-no-runner").ToListAsync());
    }

    [Fact]
    public async Task ARunnerBoundToAnotherRepository_OpensReadyNoRunner_WithTheQueuesOwnReason()
    {
        var h = new Harness();
        h.SeedReadyStep();
        var repo = await h.BindRepositoryAsync("https://example.com/o/r");
        await h.AddLoopRunnerAsync("alder", remotes: "https://example.com/o/different", standing: false);

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(15));
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync(f => f.Check == "ready-no-runner");
        var evidence = GooseFindingEvidence.Read(finding.Evidence);
        Assert.Equal($"bound to {repo.Remote}, and this runner has no checkout of it", evidence.Single(e => e.Label == "alder").Value);
    }

    [Fact]
    public async Task NoLiveRunnerAtAll_SaysSo_AndListsEachKnownRunnersLastHeartbeat()
    {
        var h = new Harness(goneAfterSeconds: 60);
        h.SeedReadyStep();
        await h.AddLoopRunnerAsync("alder");

        h.Time.Advance(TimeSpan.FromMinutes(2));
        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(15));
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync(f => f.Check == "ready-no-runner");
        var evidence = GooseFindingEvidence.Read(finding.Evidence);
        Assert.Equal("no runner is live", evidence.Single(e => e.Label == "live runners").Value);
        Assert.Equal(Now.ToString("O"), evidence.Single(e => e.Label == "alder").Value);
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(5, 5)]
    [InlineData(-1, 10)]
    public async Task TheUnclaimedBound_FallsBackToTenMinutesOnlyWhenNegative(int configured, int expectedMinutes)
    {
        var h = new Harness(unclaimedMinutes: configured);
        h.SeedReadyStep();
        await h.AddLoopRunnerAsync("alder");

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(expectedMinutes).Subtract(TimeSpan.FromSeconds(1)));
        await h.SweepAsync();
        Assert.Empty(await h.Db.GooseFindings.Where(f => f.Check == "ready-unclaimed").ToListAsync());

        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.SweepAsync();
        Assert.Single(await h.Db.GooseFindings.Where(f => f.Check == "ready-unclaimed").ToListAsync());
    }

    [Fact]
    public async Task ZeroTurnsTheUnclaimedCheckOff_AndClosesAnOpenFinding()
    {
        var h = new Harness();
        h.SeedReadyStep();
        await h.AddLoopRunnerAsync("alder");

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(10));
        await h.SweepAsync();
        Assert.Single(await h.Db.GooseFindings.Where(f => f.Check == "ready-unclaimed").ToListAsync());

        h.UseUnclaimedBound(0);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync(f => f.Check == "ready-unclaimed");
        Assert.Equal(h.Time.GetUtcNow(), finding.ClosedAt);
    }

    private sealed class Harness
    {
        public HatchContext Db { get; }

        public FakeTimeProvider Time { get; } = new(Now);

        public int ProjectId { get; }

        private readonly int todoStatusId;
        private readonly StubActorDirectory actors = new();
        private readonly IssueClaims claims = TestClaims.With();
        private readonly Dispatch dispatch;
        private readonly Runners runners;
        private HatchOptions goose;
        private int nextNumber = 1;

        public Harness(int goneAfterSeconds = 3600, int? unclaimedMinutes = null, int? noRunnerMinutes = null)
        {
            Db = new(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
            Db.Projects.Add(project);
            var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
            var doing = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
            var review = new EfHatchStatus { Name = "review", SortOrder = 35 };
            var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
            Db.AddRange(todo, doing, review, done);
            Db.SaveChanges();
            ProjectId = project.Id;
            todoStatusId = todo.Id;

            Db.AddRange(
                Playbook(todo.Id, doing.Id),
                Playbook(doing.Id, review.Id));
            Db.SaveChanges();

            dispatch = new Dispatch(Db, actors, claims, Time);
            runners = new Runners(
                Options.Create(new HatchOptions { RunnerGoneAfterSeconds = goneAfterSeconds }),
                Options.Create(new AuthOptions { Enabled = false }), new StubRevision());

            goose = new HatchOptions();
            if (unclaimedMinutes is { } u) goose.GooseUnclaimedMinutes = u;
            if (noRunnerMinutes is { } n) goose.GooseNoRunnerMinutes = n;
        }

        public void UseUnclaimedBound(int minutes) => goose.GooseUnclaimedMinutes = minutes;

        private static EfHatchPlaybook Playbook(int from, int to) => new()
        {
            FromStatusId = from, ToStatusId = to, Types = "", Shape = "any", Prompt = "do the thing", Model = "sonnet",
            Effort = "high", CreatedAt = Now, UpdatedAt = Now,
        };

        public EfHatchIssue SeedReadyStep()
        {
            var number = nextNumber++;
            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId, Number = number, Type = "story", Title = "a thing", Description = "",
                StatusId = todoStatusId, Rank = number, CreatedBy = "test", CreatedAt = Time.GetUtcNow(), UpdatedAt = Time.GetUtcNow(),
            };
            Db.Issues.Add(issue);
            Db.SaveChanges();
            return issue;
        }

        public async Task AddLoopRunnerAsync(string name, string? remotes = null, bool standing = true)
        {
            Db.LegacyRunners.Add(new EfHatchRunner
            {
                Name = name, Kind = EfHatchRunner.LoopKind, State = EfHatchRunner.Running,
                FirstSeenAt = Time.GetUtcNow(), LastSeenAt = Time.GetUtcNow(), Remotes = remotes, Standing = standing,
            });
            await Db.SaveChangesAsync();
        }

        public void Heartbeat(string name)
        {
            var row = Db.LegacyRunners.Single(r => r.Name == name);
            row.LastSeenAt = Time.GetUtcNow();
            Db.SaveChanges();
        }

        /// <summary>A remote bound to the shared project - TrainReadyCheckTests' own pattern.</summary>
        public async Task<EfHatchProjectRepository> BindRepositoryAsync(string remote)
        {
            var (canonical, _) = RemoteIdentity.Canonical(remote);
            var repo = new EfHatchProjectRepository
            {
                ProjectId = ProjectId, Remote = remote, Canonical = canonical!, SortOrder = 0, CreatedAt = Now,
            };
            Db.ProjectRepositories.Add(repo);
            await Db.SaveChangesAsync();
            return repo;
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
            });
            Db.SaveChanges();
        }

        public void ReleaseClaim(EfHatchIssue issue)
        {
            var row = Db.Issues.Single(i => i.Id == issue.Id);
            row.ClaimToken = null;
            row.ClaimedBy = null;
            row.ClaimRunner = null;
            Db.SaveChanges();
        }

        /// <summary>
        /// One sweep: a fresh <see cref="ReadyWork"/> and fresh checks, the way a
        /// new job scope would resolve them, so its one cached snapshot never
        /// outlives this sweep.
        /// </summary>
        public async Task<(GooseEntry Unclaimed, GooseEntry NoRunner)> SweepAsync()
        {
            var work = new ReadyWork(dispatch, Db, runners, claims, Time);
            var options = Options.Create(goose);
            var unclaimed = new ReadyUnclaimedCheck(Db, work, options, Time);
            var noRunner = new ReadyNoRunnerCheck(Db, work, options, Time);

            var u = await unclaimed.RunAsync(CancellationToken.None);
            var n = await noRunner.RunAsync(CancellationToken.None);
            await Db.SaveChangesAsync();

            return (new GooseEntry(unclaimed.Name, u.Looked, u.Concluded, false, null),
                new GooseEntry(noRunner.Name, n.Looked, n.Concluded, false, null));
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

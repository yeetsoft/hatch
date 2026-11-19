using System.Text.Json;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="TrainReadyCheck"/> - the <c>train-ready</c> check, HA-2496's own
/// <see cref="FindingCheck"/>. Pinned here: its name, the sentence and the
/// evidence it writes, that it needs no driver, that a head filed from a
/// finding opens none, and the three-way guard on its own bound setting. The
/// reconciler itself - when a finding opens, refreshes or closes - is
/// <c>GooseFindingsTests</c>'s.
/// </summary>
public class TrainReadyCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Name_IsTrainReady()
    {
        Assert.Equal("train-ready", NewHarness().Check.Name);
    }

    [Fact]
    public async Task AHeadJudgedMerge_OpensAFindingAtTheBound_WithTheHouseSentence()
    {
        var h = NewHarness();
        var head = await h.SeedMergingHeadAsync();

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(30));
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal("train-ready", finding.Check);
        Assert.Equal("AER", finding.Subject);
        Assert.Equal(IssueKey.Format("AER", head.Number), finding.Token);
        Assert.Equal(h.ProjectId, finding.ProjectId);
        Assert.Equal($"AER's merge train is standing: AER-{head.Number} is ready to merge and has not", finding.Sentence);
    }

    [Fact]
    public async Task ItNeedsNoDriver_AReadyHeadWithNoLiveDriverStillOpens()
    {
        var h = NewHarness();
        await h.SeedMergingHeadAsync();

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(30));
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync();
        var evidence = GooseFindingEvidence.Read(finding.Evidence);
        Assert.Equal("no live runner is eligible to drive", evidence.Single(e => e.Label == "driver").Value);
    }

    [Fact]
    public async Task TheEvidence_NamesTheHeadTheDriverAndHowManyWaitBehind()
    {
        var h = NewHarness();
        await h.BindRepositoryAsync("https://example.com/o/r");
        var head = await h.SeedMergingHeadAsync(remote: "https://example.com/o/r");
        await h.SeedBehindAsync();
        await h.SeedDriverAsync("here:/one");

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(30));
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync();
        var evidence = GooseFindingEvidence.Read(finding.Evidence);
        Assert.Equal(IssueKey.Format("AER", head.Number), evidence.Single(e => e.Label == "head").Value);
        Assert.Equal(WorkKinds.Merge, evidence.Single(e => e.Label == "judged").Value);
        Assert.Equal("here:/one", evidence.Single(e => e.Label == "driver").Value);
        Assert.Equal("1", evidence.Single(e => e.Label == "behind").Value);
        Assert.Equal("never", evidence.Single(e => e.Label == "last driver event").Value);
        Assert.Equal("0", evidence.Single(e => e.Label == "pass-overs").Value);
        Assert.Equal("none", evidence.Single(e => e.Label == "passed-over runners").Value);
    }

    [Fact]
    public async Task ThePassOverEvidence_CountsDriverStalledEventsSinceTheConditionBegan_AndNamesTheRunners()
    {
        var h = NewHarness();
        var head = await h.SeedMergingHeadAsync();

        // Before the condition began - never counted.
        h.AddDriverStalledEvent(head.Id, "here:/zero", "here:/one", at: Now.AddMinutes(-5));
        await h.SweepAsync();
        var began = h.Time.GetUtcNow();

        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.AddDriverStalledEvent(head.Id, "here:/one", "here:/two");
        h.Time.Advance(TimeSpan.FromMinutes(29));
        h.AddDriverStalledEvent(head.Id, "here:/two", "here:/one");
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync();
        var evidence = GooseFindingEvidence.Read(finding.Evidence);
        Assert.Equal("2", evidence.Single(e => e.Label == "pass-overs").Value);
        Assert.Equal(["here:/one", "here:/two"], evidence.Single(e => e.Label == "passed-over runners").Value.Split(", "));
        Assert.True(began < finding.OpenedAt);
    }

    [Fact]
    public async Task APassOver_ABroughtCurrentMark_AndAChangedDriver_DoNotRestartTheClock()
    {
        var h = NewHarness();
        var head = await h.SeedMergingHeadAsync();
        await h.SeedDriverAsync("here:/one");

        await h.SweepAsync();
        var began = (await h.Db.GooseSightings.SingleAsync()).BeganAt;

        h.AddDriverStalledEvent(head.Id, "here:/one", "here:/two");
        h.Db.Issues.Single(i => i.Id == head.Id).BroughtCurrentAt = h.Time.GetUtcNow();
        await h.Db.SaveChangesAsync();
        await h.SeedDriverAsync("here:/two");

        h.Time.Advance(TimeSpan.FromMinutes(30));
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal(began, finding.BeganAt);
    }

    [Fact]
    public async Task AHeadThatChangesKind_EndsTheCondition_AndClosesTheFinding()
    {
        var h = NewHarness();
        var head = await h.SeedMergingHeadAsync();

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(30));
        await h.SweepAsync();
        Assert.Null((await h.Db.GooseFindings.SingleAsync()).ClosedAt);

        // No longer clean against the trunk - held, not a train head at all.
        var merge = await h.Db.MergeChecks.SingleAsync(m => m.IssueId == head.Id);
        merge.Verdict = MergeVerdicts.Conflicted;
        await h.Db.SaveChangesAsync();

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal(h.Time.GetUtcNow(), finding.ClosedAt);
        Assert.Empty(await h.Db.GooseSightings.ToListAsync());
    }

    [Fact]
    public async Task AHeadFiledFromAFinding_OpensNoFindingOfItsOwn()
    {
        var h = NewHarness();
        var head = await h.SeedMergingHeadAsync();

        h.Db.GooseFindings.Add(new EfHatchGooseFinding
        {
            Check = "some-other-check", Subject = "whatever", Token = "whatever",
            BeganAt = Now, OpenedAt = Now, Sentence = "s", BugIssueId = head.Id,
        });
        await h.Db.SaveChangesAsync();

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(30));
        await h.SweepAsync();

        Assert.Empty(await h.Db.GooseFindings.Where(f => f.Check == "train-ready").ToListAsync());
    }

    [Fact]
    public async Task Looked_NamesTheProjectAndHowTheHeadWasJudged()
    {
        var h = NewHarness();
        var head = await h.SeedMergingHeadAsync();

        var observation = await h.Check.RunAsync(CancellationToken.None);

        Assert.Equal($"AER: AER-{head.Number} judged merge", observation.Looked);
    }

    [Fact]
    public async Task NoProjectHasAHead_LooksAtNothingAndOpensNothing()
    {
        var h = NewHarness();

        var observation = await h.Check.RunAsync(CancellationToken.None);

        Assert.Equal("no project has a merge train head", observation.Looked);
        Assert.Empty(await h.Db.GooseFindings.ToListAsync());
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(5, 5)]
    [InlineData(-1, 30)]
    public async Task TheBound_FallsBackToThirtyMinutesOnlyWhenNegative(int configured, int expectedMinutes)
    {
        var h = NewHarness(trainReadyMinutes: configured);
        await h.SeedMergingHeadAsync();

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(expectedMinutes).Subtract(TimeSpan.FromSeconds(1)));
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
        await h.SeedMergingHeadAsync();

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(30));
        await h.SweepAsync();
        Assert.Single(await h.Db.GooseFindings.ToListAsync());

        h.Check = h.BuildCheck(0);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal(h.Time.GetUtcNow(), finding.ClosedAt);
    }

    private static Harness NewHarness(int? trainReadyMinutes = null)
    {
        var h = new Harness();
        if (trainReadyMinutes is { } minutes) h.Check = h.BuildCheck(minutes);
        return h;
    }

    private sealed class Harness
    {
        public HatchContext Db { get; }

        public FakeTimeProvider Time { get; } = new(Now);

        public TrainReadyCheck Check { get; set; }

        public int ProjectId { get; }

        public int MergesStatusId { get; }

        private readonly IssuesController issues;
        private readonly AppDbContext coreDb;
        private readonly IAuthService keys;
        private readonly Dispatch dispatch;
        private readonly Runners runners;
        private readonly IOptions<HatchOptions> options;
        private int nextRunnerSeen;

        public Harness()
        {
            Db = new(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
            Db.Projects.Add(project);
            var merges = new EfHatchStatus { Name = "merges", SortOrder = 40, IsProtected = true, MergesPullRequest = true };
            Db.Add(merges);
            Db.SaveChanges();
            ProjectId = project.Id;
            MergesStatusId = merges.Id;

            var actors = new StubActorDirectory();
            var claims = TestClaims.With();
            coreDb = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            keys = new AuthService(coreDb, Options.Create(new AuthOptions { Enabled = false }), Time, NullLogger<AuthService>.Instance);
            dispatch = new Dispatch(Db, actors, claims, Time);
            // Wide on purpose: a seeded driver must still read as live across a
            // sweep that advances the clock a full bound's worth of minutes.
            runners = new Runners(
                Options.Create(new HatchOptions { RunnerGoneAfterSeconds = 3600 }),
                Options.Create(new AuthOptions { Enabled = false }), new StubRevision());

            var caller = new StubCaller();
            issues = new IssuesController(Db, new RankService(Db), actors, claims, caller, Time, new ProjectAccess(Db, actors));

            options = Options.Create(new HatchOptions());
            Check = BuildCheck(null);
        }

        public TrainReadyCheck BuildCheck(int? trainReadyMinutes)
        {
            var opts = trainReadyMinutes is { } minutes
                ? Options.Create(new HatchOptions { GooseTrainReadyMinutes = minutes })
                : options;
            return new TrainReadyCheck(Db, dispatch, runners, keys, opts, Time);
        }

        public async Task SweepAsync()
        {
            await Check.RunAsync(CancellationToken.None);
            await Db.SaveChangesAsync();
        }

        /// <summary>A remote bound to the shared project - RunnersControllerTests' own pattern.</summary>
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

        /// <summary>
        /// A merges-column row with a clean, trunk-holding branch and a passed
        /// build - RunnersControllerTests' own <c>SeedMergingHeadAsync</c>.
        /// </summary>
        public async Task<EfHatchIssue> SeedMergingHeadAsync(string remote = "https://example.com/o/r")
        {
            var created = Value(await issues.CreateIssue(
                new IssueCreateRequest(ProjectId, "story", "a thing", null, null, null, null), default));
            var issue = await FindAsync(created.Key);
            issue.StatusId = MergesStatusId;
            issue.PullRequestUrl = $"{remote}/pull/1";
            await Db.SaveChangesAsync();

            var (canonical, _) = RemoteIdentity.Canonical(remote);
            Db.MergeChecks.Add(new EfHatchMergeCheck
            {
                IssueId = issue.Id, Remote = remote, Canonical = canonical!, Trunk = "main",
                TrunkSha = new string('a', 40), Verdict = MergeVerdicts.Clean, Branch = "aer-1-a-thing",
                BranchSha = new string('b', 40), HoldsTrunk = true, CheckedAt = Now, Runner = "host:/checkout", CheckedBy = "runner",
            });
            Db.BuildChecks.Add(new EfHatchBuildCheck
            {
                IssueId = issue.Id, Remote = remote, Canonical = canonical!, Branch = "aer-1-a-thing",
                Sha = new string('b', 40), ShaSince = Now, Verdict = BuildVerdicts.Passed,
                CheckedAt = Now, Runner = "host:/checkout", CheckedBy = "runner",
            });
            await Db.SaveChangesAsync();

            return issue;
        }

        /// <summary>A second, unrelated row in the same merges column, for the "behind" count.</summary>
        public async Task SeedBehindAsync()
        {
            var created = Value(await issues.CreateIssue(
                new IssueCreateRequest(ProjectId, "story", "another thing", null, null, null, null), default));
            var issue = await FindAsync(created.Key);
            issue.StatusId = MergesStatusId;
            await Db.SaveChangesAsync();
        }

        private async Task<EfHatchIssue> FindAsync(string key)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            return await Db.Issues.WithKey(projectKey, number).FirstAsync();
        }

        /// <summary>A live, eligible runner for the shared project, written straight to the table.</summary>
        public async Task SeedDriverAsync(string name, string remote = "git@example.com:o/r.git")
        {
            Db.LegacyRunners.Add(new EfHatchRunner
            {
                Name = name, Kind = EfHatchRunner.LoopKind, State = EfHatchRunner.Running,
                FirstSeenAt = Now.AddSeconds(nextRunnerSeen++), LastSeenAt = Time.GetUtcNow(), Remotes = remote,
            });
            await Db.SaveChangesAsync();
        }

        public void AddDriverStalledEvent(long issueId, string passedOver, string elected, DateTimeOffset? at = null)
        {
            Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issueId, Actor = "Hatch", Kind = EfHatchIssueEvent.DriverStalled,
                Payload = JsonSerializer.Serialize(new { passedOver, elected }), At = at ?? Time.GetUtcNow(),
            });
            Db.SaveChanges();
        }

        private static T Value<T>(ActionResult<T> result) =>
            result.Result is CreatedAtActionResult created ? (T)created.Value! : result.Value!;
    }

    private sealed class StubRevision : IHatchRevision
    {
        public string Revision => "dev";
        public int Sequence => 0;
        public DateTimeOffset? BuiltAt => null;
        public bool IsDevelopment => true;
    }

    private sealed class StubCaller : ICallerIdentity
    {
        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);
        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);
        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult<EfPerson?>(null);
        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult<EfApiKey?>(null);
        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult<Actor?>(null);
        public Task<bool> IsProgramAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<string> ActorNameAsync(CancellationToken ct) => Task.FromResult(CallerIdentity.Unattributed);
    }
}

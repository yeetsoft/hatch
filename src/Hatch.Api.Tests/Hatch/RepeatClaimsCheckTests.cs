using System.Text.Json;
using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="RepeatClaimsCheck"/> - the <c>claim-repeating</c> check, HA-2499.
/// Pinned here: what makes a streak and what breaks it, when a finding opens
/// and closes, the sentence and the evidence, the columns and finding-filed
/// issues it leaves alone, and the guard on its own setting. The reconciler is
/// <c>GooseFindingsTests</c>'s.
/// </summary>
public class RepeatClaimsCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Name_IsClaimRepeating()
    {
        Assert.Equal("claim-repeating", NewHarness().Check.Name);
    }

    [Fact]
    public async Task FiveClaimsInTenMinutes_EachReleasedUnstarted_OpenOneFindingAtTheFifth_AndASixthRefreshesIt()
    {
        var h = NewHarness();
        var issue = await h.SeedIssueAsync();

        for (var i = 1; i <= 4; i++)
        {
            h.Claim(issue, "here:/one", outcome: "unstarted");
            h.Time.Advance(TimeSpan.FromMinutes(2));
        }
        await h.SweepAsync();
        Assert.Empty(await h.Db.GooseFindings.ToListAsync());

        h.Claim(issue, "here:/one", outcome: "unstarted");
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal("claim-repeating", finding.Check);
        Assert.Equal("AER-1", finding.Subject);
        Assert.Equal(h.ProjectId, finding.ProjectId);
        Assert.Equal(Now, finding.BeganAt);
        Assert.Equal(h.Time.GetUtcNow(), finding.OpenedAt);
        Assert.Null(finding.ClosedAt);

        h.Time.Advance(TimeSpan.FromMinutes(2));
        h.Claim(issue, "here:/one", outcome: "unstarted");
        await h.SweepAsync();

        var refreshed = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal(finding.Id, refreshed.Id);
        Assert.Equal(finding.Token, refreshed.Token);
        Assert.Equal(finding.OpenedAt, refreshed.OpenedAt);
        Assert.Equal("here:/one has claimed AER-1 6 times and it has not moved", refreshed.Sentence);
    }

    [Fact]
    public async Task AColumnChange_ClosesTheFinding()
    {
        var h = NewHarness();
        var issue = await h.SeedIssueAsync();
        h.ClaimTimes(issue, "here:/one", 5);
        await h.SweepAsync();
        Assert.Null((await h.Db.GooseFindings.SingleAsync()).ClosedAt);

        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.AddEvent(issue, EfHatchIssueEvent.StatusChanged, new { from = "todo", to = "in progress" });
        await h.SweepAsync();

        Assert.Equal(h.Time.GetUtcNow(), (await h.Db.GooseFindings.SingleAsync()).ClosedAt);
        Assert.Empty(await h.Db.GooseSightings.ToListAsync());
    }

    [Fact]
    public async Task FiveClaimsTakenInTurnByTwoRunners_GiveNothing()
    {
        var h = NewHarness();
        var issue = await h.SeedIssueAsync();
        string[] turns = ["here:/one", "here:/two", "here:/one", "here:/two", "here:/one"];
        foreach (var runner in turns)
        {
            h.Claim(issue, runner);
            h.Time.Advance(TimeSpan.FromMinutes(1));
        }

        await h.SweepAsync();

        Assert.Empty(await h.Db.GooseFindings.ToListAsync());
    }

    [Fact]
    public async Task FiveClaims_WithANewCommitAfterEach_GiveNothing()
    {
        var h = NewHarness();
        var issue = await h.SeedIssueAsync();
        for (var i = 0; i < 5; i++)
        {
            h.Claim(issue, "here:/one");
            h.Time.Advance(TimeSpan.FromMinutes(1));
            h.AddEvent(issue, EfHatchIssueEvent.PullRequestStateChanged, new
            {
                from = i == 0 ? null : new { mergeState = "clean", reviewDecision = (string?)null, headSha = $"sha{i - 1}" },
                to = new { mergeState = "clean", reviewDecision = (string?)null, headSha = $"sha{i}" },
            });
        }

        await h.SweepAsync();

        Assert.Empty(await h.Db.GooseFindings.ToListAsync());
    }

    [Fact]
    public async Task ABuildVerdictOnANewSha_BreaksTheStreak_ButOneOnTheSameShaDoesNot()
    {
        var h = NewHarness();
        var issue = await h.SeedIssueAsync();
        h.ClaimTimes(issue, "here:/one", 2);
        h.AddEvent(issue, EfHatchIssueEvent.BuildCheckChanged, new
        {
            from = new { verdict = "pending", sha = "aaa" }, to = new { verdict = "passed", sha = "aaa" },
        });
        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.ClaimTimes(issue, "here:/one", 3);
        await h.SweepAsync();
        Assert.Single(await h.Db.GooseFindings.ToListAsync());

        // The same five, with the verdict moved to another sha, leave only three claims in the streak.
        var other = await h.SeedIssueAsync();
        h.ClaimTimes(other, "here:/one", 2);
        h.AddEvent(other, EfHatchIssueEvent.BuildCheckChanged, new
        {
            from = new { verdict = "passed", sha = "aaa" }, to = new { verdict = "passed", sha = "bbb" },
        });
        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.ClaimTimes(other, "here:/one", 3);
        await h.SweepAsync();

        Assert.Equal(["AER-1"], (await h.Db.GooseFindings.ToListAsync()).Select(f => f.Subject).ToArray());
    }

    [Fact]
    public async Task AStreakWhoseNewestClaimIsThreeHoursOld_GivesNothing()
    {
        var h = NewHarness();
        var issue = await h.SeedIssueAsync();
        h.ClaimTimes(issue, "here:/one", 5);
        h.Time.Advance(TimeSpan.FromHours(3));

        var observation = await h.Check.RunAsync(CancellationToken.None);

        Assert.Empty(await h.Db.GooseFindings.ToListAsync());
        Assert.Equal("0 issues with a claim in the last 2 hours", observation.Looked);
    }

    [Fact]
    public async Task AFindingCloses_WhenTheNewestClaimPassesTwoHours()
    {
        var h = NewHarness();
        var issue = await h.SeedIssueAsync();
        h.ClaimTimes(issue, "here:/one", 5);
        await h.SweepAsync();
        Assert.Null((await h.Db.GooseFindings.SingleAsync()).ClosedAt);

        h.Time.Advance(TimeSpan.FromHours(2));
        await h.SweepAsync();

        Assert.Equal(h.Time.GetUtcNow(), (await h.Db.GooseFindings.SingleAsync()).ClosedAt);
    }

    [Fact]
    public async Task ANewStreakAfterABreak_IsANewCondition_WithItsOwnRow()
    {
        var h = NewHarness();
        var issue = await h.SeedIssueAsync();
        h.ClaimTimes(issue, "here:/one", 5);
        await h.SweepAsync();

        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.AddEvent(issue, EfHatchIssueEvent.StatusChanged, new { from = "todo", to = "in progress" });
        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.ClaimTimes(issue, "here:/one", 5);
        await h.SweepAsync();

        var findings = await h.Db.GooseFindings.OrderBy(f => f.Id).ToListAsync();
        Assert.Equal(2, findings.Count);
        Assert.NotNull(findings[0].ClosedAt);
        Assert.Null(findings[1].ClosedAt);
        Assert.NotEqual(findings[0].Token, findings[1].Token);
    }

    [Fact]
    public async Task TwoSweepsOfAHeldOpenStreak_MakeOneRow()
    {
        var h = NewHarness();
        var issue = await h.SeedIssueAsync();
        h.ClaimTimes(issue, "here:/one", 5);

        await h.SweepAsync();
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();

        Assert.Single(await h.Db.GooseFindings.ToListAsync());
    }

    [Fact]
    public async Task TheSentenceAndEvidence_NameTheRunnerTheCountTheReleasesAndTheIssue()
    {
        var h = NewHarness();
        var issue = await h.SeedIssueAsync(stalledWhy: "the agent never started", held: true);
        h.Claim(issue, "here:/one", outcome: "unstarted");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.Claim(issue, "here:/one", outcome: "unstarted");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.Claim(issue, "here:/one", outcome: "abandoned");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.Claim(issue, "here:/one", outcome: null);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.Claim(issue, "here:/one", outcome: "unstarted");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.Claim(issue, "here:/one", outcome: "unstarted", release: false);
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal("here:/one has claimed AER-1 6 times and it has not moved", finding.Sentence);
        var evidence = GooseFindingEvidence.Read(finding.Evidence).ToDictionary(e => e.Label, e => e.Value);
        Assert.Equal("here:/one", evidence["runner"]);
        Assert.Equal("6", evidence["claims"]);
        Assert.Equal(Now.ToString("O"), evidence["first claim"]);
        Assert.Equal(Now.AddMinutes(5).ToString("O"), evidence["newest claim"]);
        Assert.Equal("unstarted 3, abandoned 1, no outcome 1", evidence["releases"]);
        Assert.Equal("todo", evidence["column"]);
        Assert.Equal("the agent never started", evidence["stall mark"]);
        Assert.Equal("held", evidence["hold"]);
    }

    [Fact]
    public async Task TheEvidence_ReadsNoneWhereNothingWasReleasedMarkedOrHeld()
    {
        var h = NewHarness();
        var issue = await h.SeedIssueAsync();
        h.ClaimTimes(issue, "here:/one", 5, release: false);

        await h.SweepAsync();

        var evidence = GooseFindingEvidence.Read((await h.Db.GooseFindings.SingleAsync()).Evidence).ToDictionary(e => e.Label, e => e.Value);
        Assert.Equal("none", evidence["releases"]);
        Assert.Equal("none", evidence["stall mark"]);
        Assert.Equal("none", evidence["hold"]);
    }

    [Fact]
    public async Task ARunnerNamedWithoutAnAt_IsShownWhole()
    {
        var h = NewHarness();
        var issue = await h.SeedIssueAsync();
        for (var i = 0; i < 5; i++) h.AddEvent(issue, EfHatchIssueEvent.ClaimTaken, new { from = (string?)null, to = "solo" });

        await h.SweepAsync();

        Assert.Equal("solo has claimed AER-1 5 times and it has not moved", (await h.Db.GooseFindings.SingleAsync()).Sentence);
    }

    [Theory]
    [InlineData("done", true, false)]
    [InlineData("later", false, true)]
    public async Task AnIssueInATerminalOrDeferredColumn_OpensNothing(string column, bool terminal, bool deferred)
    {
        var h = NewHarness();
        var status = new EfHatchStatus { Name = column, SortOrder = 90, IsTerminal = terminal, IsDeferred = deferred };
        h.Db.Add(status);
        await h.Db.SaveChangesAsync();
        var issue = await h.SeedIssueAsync(statusId: status.Id);
        h.ClaimTimes(issue, "here:/one", 5);

        var observation = await h.Check.RunAsync(CancellationToken.None);

        Assert.Empty(await h.Db.GooseFindings.ToListAsync());
        Assert.Equal("0 issues with a claim in the last 2 hours", observation.Looked);
    }

    [Fact]
    public async Task AnIssueFiledFromAFinding_OpensNothing()
    {
        var h = NewHarness();
        var issue = await h.SeedIssueAsync();
        h.ClaimTimes(issue, "here:/one", 5);
        h.Db.GooseFindings.Add(new EfHatchGooseFinding
        {
            Check = "some-other-check", Subject = "whatever", Token = "whatever",
            BeganAt = Now, OpenedAt = Now, Sentence = "s", BugIssueId = issue.Id,
        });
        await h.Db.SaveChangesAsync();

        await h.SweepAsync();

        Assert.Empty(await h.Db.GooseFindings.Where(f => f.Check == "claim-repeating").ToListAsync());
    }

    [Fact]
    public async Task Looked_SaysHowManyIssuesHadAClaimInTheLastTwoHours()
    {
        var h = NewHarness();
        var one = await h.SeedIssueAsync();
        var two = await h.SeedIssueAsync();
        h.ClaimTimes(one, "here:/one", 2);
        h.Claim(two, "here:/two");

        var observation = await h.Check.RunAsync(CancellationToken.None);

        Assert.Equal("2 issues with a claim in the last 2 hours", observation.Looked);
    }

    [Theory]
    [InlineData(3, 3, 1)]
    [InlineData(3, 2, 0)]
    [InlineData(-1, 5, 1)]
    [InlineData(-1, 4, 0)]
    public async Task TheThreshold_FallsBackToFiveOnlyWhenNegative(int configured, int claims, int expectedFindings)
    {
        var h = NewHarness(configured);
        var issue = await h.SeedIssueAsync();
        h.ClaimTimes(issue, "here:/one", claims);

        await h.SweepAsync();

        Assert.Equal(expectedFindings, await h.Db.GooseFindings.CountAsync());
    }

    [Fact]
    public async Task ZeroTurnsTheCheckOff_AndClosesAnOpenFinding()
    {
        var h = NewHarness();
        var issue = await h.SeedIssueAsync();
        h.ClaimTimes(issue, "here:/one", 5);
        await h.SweepAsync();
        Assert.Single(await h.Db.GooseFindings.ToListAsync());

        h.Check = h.BuildCheck(0);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.SweepAsync();

        Assert.Equal(h.Time.GetUtcNow(), (await h.Db.GooseFindings.SingleAsync()).ClosedAt);
    }

    [Fact]
    public void Streak_StopsAtTheFirstMovement_AndCountsReleasesOnlyInsideItsSpan()
    {
        long id = 0;
        RepeatClaimsCheck.Streak.Event E(string kind, object payload, int minute) =>
            new(++id, kind, JsonSerializer.Serialize(payload), Now.AddMinutes(minute));

        var oldestFirst = new[]
        {
            E(EfHatchIssueEvent.ClaimReleased, new { outcome = "worked" }, 0),
            E(EfHatchIssueEvent.StatusChanged, new { from = "a", to = "b" }, 1),
            E(EfHatchIssueEvent.ClaimReleased, new { outcome = "dropped" }, 2),
            E(EfHatchIssueEvent.ClaimTaken, new { from = (string?)null, to = "k at here:/one" }, 3),
            E(EfHatchIssueEvent.ClaimReleased, new { outcome = "unstarted" }, 4),
            E(EfHatchIssueEvent.ClaimTaken, new { from = (string?)null, to = "k at here:/one" }, 5),
            E(EfHatchIssueEvent.ClaimReleased, new { outcome = "unstarted" }, 6),
        };

        var streak = RepeatClaimsCheck.Streak.Read(oldestFirst.Reverse())!;

        Assert.Equal("here:/one", streak.Runner);
        Assert.Equal(2, streak.Claims);
        Assert.Equal(Now.AddMinutes(3), streak.OldestAt);
        Assert.Equal(Now.AddMinutes(5), streak.NewestAt);
        Assert.Equal(2, streak.Releases["unstarted"]);
        Assert.False(streak.Releases.ContainsKey("dropped"));
    }

    [Fact]
    public void Streak_IsNone_WhenTheTicketMovedAfterTheNewestClaim()
    {
        var events = new RepeatClaimsCheck.Streak.Event[]
        {
            new(2, EfHatchIssueEvent.StatusChanged, "{}", Now.AddMinutes(2)),
            new(1, EfHatchIssueEvent.ClaimTaken, """{"to":"k at here:/one"}""", Now),
        };

        Assert.Null(RepeatClaimsCheck.Streak.Read(events));
    }

    private static Harness NewHarness(int? repeatClaims = null)
    {
        var h = new Harness();
        if (repeatClaims is { } n) h.Check = h.BuildCheck(n);
        return h;
    }

    private sealed class Harness
    {
        public HatchContext Db { get; } = new(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        public FakeTimeProvider Time { get; } = new(Now);

        public RepeatClaimsCheck Check { get; set; }

        public int ProjectId { get; }

        private readonly int todoId;
        private int nextNumber = 1;

        public Harness()
        {
            var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
            var todo = new EfHatchStatus { Name = "todo", SortOrder = 10 };
            Db.Projects.Add(project);
            Db.Add(todo);
            Db.SaveChanges();
            ProjectId = project.Id;
            todoId = todo.Id;
            Check = BuildCheck(null);
        }

        public RepeatClaimsCheck BuildCheck(int? repeatClaims) => new(
            Db,
            Options.Create(repeatClaims is { } n ? new HatchOptions { GooseRepeatClaims = n } : new HatchOptions()),
            Time);

        public async Task SweepAsync()
        {
            await Check.RunAsync(CancellationToken.None);
            await Db.SaveChangesAsync();
        }

        public async Task<EfHatchIssue> SeedIssueAsync(int? statusId = null, string? stalledWhy = null, bool held = false)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId, Number = nextNumber++, Type = "story", Title = "a thing",
                StatusId = statusId ?? todoId, Rank = nextNumber, CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
                StalledWhy = stalledWhy, Held = held,
            };
            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        /// <summary>A claim at the clock's present and, unless <paramref name="release"/> is false, its release at the same instant - which sorts after it.</summary>
        public void Claim(EfHatchIssue issue, string runner, string? outcome = null, bool release = true)
        {
            var holder = $"someone at {runner}";
            AddEvent(issue, EfHatchIssueEvent.ClaimTaken, new { from = (string?)null, to = holder });
            if (!release) return;

            var payload = new Dictionary<string, object?> { ["from"] = holder, ["to"] = null };
            if (outcome is not null) payload["outcome"] = outcome;
            AddEvent(issue, EfHatchIssueEvent.ClaimReleased, payload);
        }

        /// <summary>Claims ten seconds apart, each released with no outcome.</summary>
        public void ClaimTimes(EfHatchIssue issue, string runner, int count, bool release = true)
        {
            for (var i = 0; i < count; i++)
            {
                Claim(issue, runner, release: release);
                Time.Advance(TimeSpan.FromSeconds(10));
            }
        }

        public void AddEvent(EfHatchIssue issue, string kind, object payload)
        {
            Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id, Actor = "Hatch", Kind = kind, Payload = JsonSerializer.Serialize(payload), At = Time.GetUtcNow(),
            });
            Db.SaveChanges();
        }
    }
}

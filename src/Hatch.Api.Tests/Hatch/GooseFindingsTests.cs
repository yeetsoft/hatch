using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="FindingCheck"/> - the reconciler every check that opens a stored
/// finding shares, so none of them can disagree about when a clock starts, when
/// a row opens, or when it closes. The pins are the ones HA-2496's acceptance
/// draws: at most one finding open per check and subject, a clock that starts
/// on first sight and survives everything but a changed token, a finding that
/// only ever opens at the bound, and a sweep that throws touching neither
/// table.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class GooseFindingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AConditionBelowTheBound_OpensNoFindingButStartsASighting()
    {
        var h = NewHarness();
        h.Check.Candidates = [new FindingCandidate("AER", "AER-1", null)];

        await h.SweepAsync();

        Assert.Empty(await h.Db.GooseFindings.ToListAsync());
        var sighting = Assert.Single(await h.Db.GooseSightings.ToListAsync());
        Assert.Equal("AER", sighting.Subject);
        Assert.Equal("AER-1", sighting.Token);
        Assert.Equal(Now, sighting.BeganAt);
        Assert.Equal(0, h.Check.DescribeCalls);
    }

    [Fact]
    public async Task AConditionThatReachesTheBound_OpensExactlyOneFinding()
    {
        var h = NewHarness();
        h.Check.Candidates = [new FindingCandidate("AER", "AER-1", 7)];

        await h.SweepAsync();
        h.Time.Advance(h.Check.Bound);
        await h.SweepAsync();

        var finding = Assert.Single(await h.Db.GooseFindings.ToListAsync());
        Assert.Equal("train-ready-stub", finding.Check);
        Assert.Equal("AER", finding.Subject);
        Assert.Equal("AER-1", finding.Token);
        Assert.Equal(7, finding.ProjectId);
        Assert.Equal(Now, finding.BeganAt);
        Assert.Equal(Now + h.Check.Bound, finding.OpenedAt);
        Assert.Null(finding.ClosedAt);
        Assert.Equal(1, h.Check.DescribeCalls);
    }

    [Fact]
    public async Task LaterSweeps_RefreshSentenceAndEvidence_ButNeverTheTwoStartTimes()
    {
        var h = NewHarness();
        h.Check.Candidates = [new FindingCandidate("AER", "AER-1", null)];
        h.Check.Sentence = "first";

        await h.SweepAsync();
        h.Time.Advance(h.Check.Bound);
        await h.SweepAsync();

        var opened = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal(Now, opened.BeganAt);
        Assert.Equal(Now + h.Check.Bound, opened.OpenedAt);

        h.Check.Sentence = "second";
        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.SweepAsync();

        var refreshed = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal("second", refreshed.Sentence);
        Assert.Equal(Now, refreshed.BeganAt);
        Assert.Equal(Now + h.Check.Bound, refreshed.OpenedAt);
        Assert.Null(refreshed.ClosedAt);
    }

    [Fact]
    public async Task ASweepThatNoLongerSeesTheCondition_ClosesTheFinding()
    {
        var h = NewHarness();
        h.Check.Candidates = [new FindingCandidate("AER", "AER-1", null)];

        await h.SweepAsync();
        h.Time.Advance(h.Check.Bound);
        await h.SweepAsync();
        Assert.Null((await h.Db.GooseFindings.SingleAsync()).ClosedAt);

        h.Check.Candidates = [];
        await h.SweepAsync();

        var finding = await h.Db.GooseFindings.SingleAsync();
        Assert.Equal(h.Time.GetUtcNow(), finding.ClosedAt);
        Assert.Empty(await h.Db.GooseSightings.ToListAsync());
    }

    [Fact]
    public async Task AChangedToken_ClosesTheOldFindingAndStartsTheNewClockAtItsOwnFirstSighting()
    {
        var h = NewHarness();
        h.Check.Candidates = [new FindingCandidate("AER", "AER-1", null)];

        await h.SweepAsync();
        h.Time.Advance(h.Check.Bound);
        await h.SweepAsync();
        var openedAt = h.Time.GetUtcNow();

        h.Check.Candidates = [new FindingCandidate("AER", "AER-2", null)];
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();

        var old = await h.Db.GooseFindings.SingleAsync(f => f.Token == "AER-1");
        Assert.Equal(openedAt + TimeSpan.FromMinutes(1), old.ClosedAt);

        var sighting = await h.Db.GooseSightings.SingleAsync();
        Assert.Equal("AER-2", sighting.Token);
        Assert.Equal(openedAt + TimeSpan.FromMinutes(1), sighting.BeganAt);

        // Below the bound again: the new token's clock just started.
        Assert.Empty(await h.Db.GooseFindings.Where(f => f.ClosedAt == null).ToListAsync());
    }

    [Fact]
    public async Task ACheckThatThrowsReadingCandidates_OpensNothingAndClosesNothing()
    {
        var h = NewHarness();
        h.Check.Candidates = [new FindingCandidate("AER", "AER-1", null)];
        await h.SweepAsync();
        h.Time.Advance(h.Check.Bound);
        await h.SweepAsync();
        Assert.Single(await h.Db.GooseFindings.ToListAsync());

        h.Check.Throws = new InvalidOperationException("the board was unreadable");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Check.RunAsync(CancellationToken.None));

        // Untouched: still the one open finding from before, same sentence.
        var finding = await h.Db.GooseFindings.SingleAsync();
        Assert.Null(finding.ClosedAt);
        Assert.Equal(1, await h.Db.GooseFindings.CountAsync());
        Assert.Equal(1, await h.Db.GooseSightings.CountAsync());
    }

    [Fact]
    public async Task ACheckThatThrowsDescribing_OpensNothingForThatSweep()
    {
        var h = NewHarness();
        h.Check.Candidates = [new FindingCandidate("AER", "AER-1", null)];
        h.Check.DescribeThrows = new InvalidOperationException("could not describe it");

        await h.SweepAsync();
        h.Time.Advance(h.Check.Bound);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Check.RunAsync(CancellationToken.None));

        Assert.Empty(await h.Db.GooseFindings.ToListAsync());

        // The sighting from the first, successful sweep is untouched - the
        // throw came from describing, after the sighting half had already
        // been read, so it was never asked to change.
        var sighting = await h.Db.GooseSightings.SingleAsync();
        Assert.Equal(Now, sighting.BeganAt);
    }

    [Fact]
    public async Task FiledFromAFindingAsync_IsTrueOnlyForTheBugItsOwnFindingNames()
    {
        var h = NewHarness();
        h.Db.Issues.Add(new EfHatchIssue
        {
            ProjectId = h.ProjectId, Number = 1, Type = "bug", Title = "a bug", Description = "",
            StatusId = h.StatusId, Rank = 1, CreatedAt = Now, UpdatedAt = Now, CreatedBy = "test",
        });
        await h.Db.SaveChangesAsync();
        var bug = await h.Db.Issues.SingleAsync();

        Assert.False(await GooseFindings.FiledFromAFindingAsync(h.Db, bug.Id, CancellationToken.None));

        h.Db.GooseFindings.Add(new EfHatchGooseFinding
        {
            Check = "train-ready-stub", Subject = "AER", Token = "AER-1",
            BeganAt = Now, OpenedAt = Now, Sentence = "s", BugIssueId = bug.Id,
        });
        await h.Db.SaveChangesAsync();

        Assert.True(await GooseFindings.FiledFromAFindingAsync(h.Db, bug.Id, CancellationToken.None));
        Assert.False(await GooseFindings.FiledFromAFindingAsync(h.Db, bug.Id + 1, CancellationToken.None));
    }

    [SkippableFact]
    public async Task AtMostOneFindingIsOpenForACheckAndSubject_TheDatabaseEnforcesIt()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the filtered unique index is enforced by Postgres, which " +
            "EF's in-memory provider does not model at all. Run `make test-api-db`.");

        var options = new DbContextOptionsBuilder<HatchContext>().UseNpgsql(await HatchDatabase.PrepareAsync()).Options;
        await using var db = new HatchContext(options);

        db.GooseFindings.Add(new EfHatchGooseFinding
        {
            Check = "train-ready", Subject = "AER", Token = "AER-1", BeganAt = Now, OpenedAt = Now, Sentence = "a",
        });
        await db.SaveChangesAsync();

        db.GooseFindings.Add(new EfHatchGooseFinding
        {
            Check = "train-ready", Subject = "AER", Token = "AER-2", BeganAt = Now, OpenedAt = Now, Sentence = "b",
        });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task AClosedFinding_NeverBlocksTheNextFromOpening()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - see the test above.");

        var options = new DbContextOptionsBuilder<HatchContext>().UseNpgsql(await HatchDatabase.PrepareAsync()).Options;
        await using var db = new HatchContext(options);

        db.GooseFindings.Add(new EfHatchGooseFinding
        {
            Check = "train-ready", Subject = "AER", Token = "AER-1", BeganAt = Now, OpenedAt = Now, ClosedAt = Now, Sentence = "a",
        });
        await db.SaveChangesAsync();

        db.GooseFindings.Add(new EfHatchGooseFinding
        {
            Check = "train-ready", Subject = "AER", Token = "AER-2", BeganAt = Now, OpenedAt = Now, Sentence = "b",
        });
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.GooseFindings.CountAsync());
    }

    private static Harness NewHarness() => new();

    private sealed class Harness
    {
        public HatchContext Db { get; } = new(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        public FakeTimeProvider Time { get; } = new(Now);

        public StubFindingCheck Check { get; }

        public int ProjectId { get; }

        public int StatusId { get; }

        public Harness()
        {
            Check = new StubFindingCheck(Db, Time);

            var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
            var status = new EfHatchStatus { Name = "todo", SortOrder = 10 };
            Db.Projects.Add(project);
            Db.Add(status);
            Db.SaveChanges();
            ProjectId = project.Id;
            StatusId = status.Id;
        }

        /// <summary>One sweep, as <see cref="GooseJob"/> drives a check: run it, then save - <see cref="FindingCheck.RunAsync"/> never saves on its own.</summary>
        public async Task SweepAsync()
        {
            await Check.RunAsync(CancellationToken.None);
            await Db.SaveChangesAsync();
        }
    }

    /// <summary>A <see cref="FindingCheck"/> whose candidates and descriptions a test sets directly, to pin the reconciler alone.</summary>
    private sealed class StubFindingCheck(HatchContext db, TimeProvider time) : FindingCheck(db, time)
    {
        public override string Name => "train-ready-stub";

        public string Looked { get; set; } = "";

        public TimeSpan Bound { get; set; } = TimeSpan.FromMinutes(10);

        public IReadOnlyList<FindingCandidate> Candidates { get; set; } = [];

        public string Sentence { get; set; } = "standing";

        public Exception? Throws { get; set; }

        public Exception? DescribeThrows { get; set; }

        public int DescribeCalls { get; private set; }

        protected override Task<(string Looked, TimeSpan Bound, IReadOnlyList<FindingCandidate> Candidates)> ReadAsync(
            DateTimeOffset now, CancellationToken ct) =>
            Throws is { } e ? throw e : Task.FromResult((Looked, Bound, Candidates));

        protected override Task<(string Sentence, IReadOnlyList<GooseFindingEvidence> Evidence)> DescribeAsync(
            FindingCandidate candidate, DateTimeOffset began, CancellationToken ct)
        {
            DescribeCalls++;
            if (DescribeThrows is { } e) throw e;
            return Task.FromResult((Sentence, (IReadOnlyList<GooseFindingEvidence>)[new GooseFindingEvidence("token", candidate.Token)]));
        }
    }
}

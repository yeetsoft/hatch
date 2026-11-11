using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The pure fold alone: sessions and candidates in, rows and buckets out, with
/// no database and no clock - pinned the way <see cref="ChangelogReportTests"/>
/// pins <see cref="ChangelogReport"/>.
/// </summary>
public class ConsumptionReportTests
{
    private static readonly DateTimeOffset Hour0 = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);

    private static ConsumptionReport.Candidate Candidate(
        long id, long? parentId, string key, string type = "epic", string projectKey = "AER", int statusId = 1) =>
        new(id, parentId, key, key, type, projectKey, statusId);

    private static ConsumptionReport.Session Session(
        long issueId, DateTimeOffset endedAt, long tokens = 100, decimal? costUsd = 1m, bool isError = false, long durationMs = 1_000) =>
        new(issueId, endedAt, isError, tokens, 0, 0, 0, costUsd, durationMs);

    private static WorkLogWindow WindowOf(DateTimeOffset from, long buckets, TimeSpan length) => new(from, buckets, length);

    [Fact]
    public void ASubEpicsSessions_LandInItsOwnRowAndItsParents_AndTheTotalOnce()
    {
        var parentEpic = Candidate(1, null, "AER-1");
        var subEpic = Candidate(2, 1, "AER-2");
        var candidates = new[] { parentEpic, subEpic };
        var parentOf = new Dictionary<long, long?> { [1] = null, [2] = 1 };
        var sessions = new[] { Session(2, Hour0.AddMinutes(30), tokens: 1_000, costUsd: 5m) };
        var window = WindowOf(Hour0, 1, TimeSpan.FromHours(1));

        var result = ConsumptionReport.Fold(sessions, candidates, parentOf, window, ConsumptionReport.ConsumptionSort.Activity);

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, r => Assert.Equal(1_000, r.Totals.TotalTokens));
        Assert.Equal("AER-1", Assert.Single(result.Rows, r => r.Key == "AER-2").ParentKey);
        Assert.Null(Assert.Single(result.Rows, r => r.Key == "AER-1").ParentKey);

        // Counted once across the whole filter and once in its bucket, even
        // though it credited two rows.
        Assert.Equal(1, result.Totals.Sessions);
        Assert.Equal(1_000, result.Totals.TotalTokens);
        Assert.Equal(1, Assert.Single(result.Buckets).Totals.Sessions);
    }

    [Fact]
    public void ASessionBeneathNoCandidate_IsDroppedFromRowsTotalsAndBuckets()
    {
        var elsewhere = Candidate(1, null, "AER-1");
        var parentOf = new Dictionary<long, long?> { [1] = null, [99] = null };
        var sessions = new[] { Session(99, Hour0.AddMinutes(10), tokens: 50_000, costUsd: 10m) };
        var window = WindowOf(Hour0, 1, TimeSpan.FromHours(1));

        var result = ConsumptionReport.Fold(sessions, [elsewhere], parentOf, window, ConsumptionReport.ConsumptionSort.Activity);

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.Totals.Sessions);
        Assert.Equal(0, result.Totals.TotalTokens);
        Assert.Equal(0, Assert.Single(result.Buckets).Totals.Sessions);
    }

    [Fact]
    public void CostSort_PutsEveryAllUnpricedRowLast_AndBreaksTiesOnKey()
    {
        var a = Candidate(1, null, "AER-1");
        var b = Candidate(2, null, "AER-2");
        var c = Candidate(3, null, "AER-3");
        var candidates = new[] { a, b, c };
        var parentOf = new Dictionary<long, long?> { [1] = null, [2] = null, [3] = null };
        var sessions = new[]
        {
            Session(1, Hour0, costUsd: 5m),
            Session(2, Hour0, costUsd: null),
            Session(3, Hour0, costUsd: 5m),
        };
        var window = WindowOf(Hour0, 1, TimeSpan.FromHours(1));

        var result = ConsumptionReport.Fold(sessions, candidates, parentOf, window, ConsumptionReport.ConsumptionSort.Cost);

        // AER-1 and AER-3 tie on cost, so key breaks the tie; AER-2 has no
        // priced session at all and sorts last whatever its cost reads as.
        Assert.Equal(["AER-1", "AER-3", "AER-2"], result.Rows.Select(r => r.Key));
    }

    [Fact]
    public void ActivitySort_OrdersByLastSessionAtDescending_AndBreaksTiesOnKey()
    {
        var a = Candidate(1, null, "AER-1");
        var b = Candidate(2, null, "AER-2");
        var candidates = new[] { a, b };
        var parentOf = new Dictionary<long, long?> { [1] = null, [2] = null };
        var sessions = new[]
        {
            Session(1, Hour0.AddMinutes(10)),
            Session(2, Hour0.AddMinutes(40)),
        };
        var window = WindowOf(Hour0, 1, TimeSpan.FromHours(1));

        var result = ConsumptionReport.Fold(sessions, candidates, parentOf, window, ConsumptionReport.ConsumptionSort.Activity);

        Assert.Equal(["AER-2", "AER-1"], result.Rows.Select(r => r.Key));
    }

    [Theory]
    [InlineData("epic")]
    [InlineData("reflection")]
    [InlineData("story")]
    [InlineData("task")]
    [InlineData("bug")]
    public void ARowsType_IsCarriedThroughFromItsCandidate(string type)
    {
        var candidate = Candidate(1, null, "AER-1", type: type);
        var parentOf = new Dictionary<long, long?> { [1] = null };
        var sessions = new[] { Session(1, Hour0) };
        var window = WindowOf(Hour0, 1, TimeSpan.FromHours(1));

        var result = ConsumptionReport.Fold(sessions, [candidate], parentOf, window, ConsumptionReport.ConsumptionSort.Activity);

        Assert.Equal(type, Assert.Single(result.Rows).Type);
    }

    [Fact]
    public void ACandidatesParent_ThatIsNotItselfACandidate_LeavesParentKeyNull()
    {
        // AER-1 is a story filed under AER-0's epic, but AER-0 was filtered out
        // of the candidate set (wrong type, wrong status, wrong project...).
        var candidate = Candidate(1, parentId: 0, "AER-1", type: "story");
        var parentOf = new Dictionary<long, long?> { [0] = null, [1] = 0 };
        var sessions = new[] { Session(1, Hour0) };
        var window = WindowOf(Hour0, 1, TimeSpan.FromHours(1));

        var result = ConsumptionReport.Fold(sessions, [candidate], parentOf, window, ConsumptionReport.ConsumptionSort.Activity);

        Assert.Null(Assert.Single(result.Rows).ParentKey);
    }

    [Fact]
    public void ACycleInParentOf_TerminatesRatherThanSpinning()
    {
        var a = Candidate(1, 2, "AER-1");
        var b = Candidate(2, 1, "AER-2");
        var parentOf = new Dictionary<long, long?> { [1] = 2, [2] = 1 };
        var sessions = new[] { Session(1, Hour0) };
        var window = WindowOf(Hour0, 1, TimeSpan.FromHours(1));

        var result = ConsumptionReport.Fold(sessions, [a, b], parentOf, window, ConsumptionReport.ConsumptionSort.Activity);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(1, result.Totals.Sessions);
    }
}

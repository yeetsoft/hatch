using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The leaderboard's sums and ranking, worked out by Postgres, against the same
/// read folded in C# over one seeded log.
///
/// <para>The folded reads (<see cref="WorkLogRollup.SessionsFoldedAsync"/> and
/// <see cref="WorkLogRollup.SeriesFoldedAsync"/>) are the oracle: the answer the
/// endpoints gave before the database did the adding. Every field of the two
/// answers is compared, row by row and in order, so a sum that is off by a
/// place of cost, a bucket edge that lands a session one bucket over, a tie
/// that breaks the other way or a path that sorts under another collation fails
/// here and nowhere else.</para>
///
/// <para>The seed is small and built to be awkward: sessions on every bucket
/// edge, one a microsecond either side of one, one at the far end of the range
/// (which is excluded), equal tokens and cost and end time on different ids, an
/// unpriced one, an errored one, a session with no request count and none with
/// a peak context, outcomes of two kinds and none, and reads whose paths
/// differ only in case.</para>
///
/// <para>These run against a real Postgres, and skip without one: the in-memory
/// provider has no <c>jsonb_array_elements</c>, no collation and no
/// <c>numeric</c>. Run <c>make test-api-db</c>.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class WorkLogRollupDatabaseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Micro = TimeSpan.FromTicks(10);

    private static readonly WorkLogSort[] Sorts = Enum.GetValues<WorkLogSort>();

    /// <summary>The seeded filters, by what they are called in a failure.</summary>
    private sealed record Filter(string Name, bool Ancestor, int[]? Projects, string? Outcome);

    private static Filter[] Filters(Seed s) =>
    [
        new("no filter", false, null, null),
        new("outcome ok", false, null, "ok"),
        new("outcome none", false, null, WorkLogRollup.NoOutcome),
        new("project A", false, [s.ProjectA], null),
        new("ancestor epic", true, null, null),
        new("epic in project B", true, [s.ProjectB], null),
        new("epic, outcome dropped", true, null, "dropped"),
    ];

    [SkippableFact]
    public async Task Sessions_AgreeWithTheFold_ForEverySortFilterAndLimit()
    {
        await using var s = await SeedAsync();

        var from = T0;
        var to = T0 + TimeSpan.FromHours(6);
        var checkedAny = 0;

        foreach (var sort in Sorts)
        foreach (var filter in Filters(s))
        foreach (var limit in new[] { 3, 100 })
        // Without a top, and with one: the same table either way, and the top
        // ranked by tokens in every sort, ties and all.
        foreach (var top in new int?[] { null, 4 })
        {
            var ancestor = filter.Ancestor ? s.Epic : (long?)null;
            var label = $"{sort} / {filter.Name} / limit {limit} / top {top}";

            var folded = await WorkLogRollup.SessionsFoldedAsync(
                s.Db, from, to, ancestor, filter.Projects, sort, limit, filter.Outcome, top, default);
            var summed = await WorkLogRollup.SessionsAsync(
                s.Db, from, to, ancestor, filter.Projects, sort, limit, filter.Outcome, top, default);

            AssertSameSessions(folded, summed, label);
            checkedAny++;
        }

        Assert.Equal(Sorts.Length * Filters(s).Length * 2 * 2, checkedAny);
    }

    [SkippableFact]
    public async Task Sessions_AgreeWithTheFold_OverARangeWiderThanTheSeed()
    {
        await using var s = await SeedAsync();

        // Every session, the one on the far end of the hourly window included.
        var from = T0 - TimeSpan.FromDays(1);
        var to = T0 + TimeSpan.FromDays(5);

        foreach (var sort in Sorts)
        {
            var folded = await WorkLogRollup.SessionsFoldedAsync(
                s.Db, from, to, null, null, sort, 20, null, 5, default);
            var summed = await WorkLogRollup.SessionsAsync(
                s.Db, from, to, null, null, sort, 20, null, 5, default);

            AssertSameSessions(folded, summed, $"{sort} / wide");
        }
    }

    [SkippableFact]
    public async Task Series_AgreeWithTheFold_ForEveryFilter_HourlyAndDaily()
    {
        await using var s = await SeedAsync();

        var windows = new[]
        {
            (Name: "hourly", Bucket: WorkLogBucketSize.Hour, Window: WorkLogRollup.Align(
                T0, T0 + TimeSpan.FromHours(6), WorkLogBucketSize.Hour, 0)),
            // A half-hour offset puts the day grid half an hour off midnight, so
            // an edge falls between two of the hourly ones.
            (Name: "daily, half-hour offset", Bucket: WorkLogBucketSize.Day, Window: WorkLogRollup.Align(
                T0 - TimeSpan.FromHours(1), T0 + TimeSpan.FromDays(3), WorkLogBucketSize.Day, 30)),
        };

        foreach (var (name, bucket, window) in windows)
        foreach (var filter in Filters(s))
        {
            var ancestor = filter.Ancestor ? s.Epic : (long?)null;
            var label = $"{name} / {filter.Name}";

            var folded = await WorkLogRollup.SeriesFoldedAsync(
                s.Db, window, bucket, ancestor, filter.Projects, filter.Outcome, default);
            var summed = await WorkLogRollup.SeriesAsync(
                s.Db, window, bucket, ancestor, filter.Projects, filter.Outcome, default);

            Assert.Equal(folded.From, summed.From);
            Assert.Equal(folded.To, summed.To);
            Assert.Equal(folded.Bucket, summed.Bucket);
            Assert.True(folded.Totals == summed.Totals, $"{label}: totals {folded.Totals} then {summed.Totals}");
            Assert.Equal(folded.FirstSessionAt, summed.FirstSessionAt);
            Assert.Equal(folded.LastSessionAt, summed.LastSessionAt);
            Assert.Equal(folded.Buckets.Count, summed.Buckets.Count);

            for (var i = 0; i < folded.Buckets.Count; i++)
            {
                Assert.Equal(folded.Buckets[i].Start, summed.Buckets[i].Start);
                Assert.Equal(folded.Buckets[i].End, summed.Buckets[i].End);
                Assert.True(
                    folded.Buckets[i].Totals == summed.Buckets[i].Totals,
                    $"{label}, bucket {i}: {folded.Buckets[i].Totals} then {summed.Buckets[i].Totals}");
            }

            // The buckets add to the headline, by construction.
            Assert.Equal(summed.Totals.Sessions, summed.Buckets.Sum(b => b.Totals.Sessions));
            Assert.Equal(summed.Totals.CostUsd, summed.Buckets.Sum(b => b.Totals.CostUsd));
        }
    }

    [SkippableFact]
    public async Task ASessionOnABucketEdge_LandsInTheBucketItStartsAndNotTheOneBefore()
    {
        await using var s = await SeedAsync();

        var window = WorkLogRollup.Align(T0, T0 + TimeSpan.FromHours(6), WorkLogBucketSize.Hour, 0);
        var history = await WorkLogRollup.SeriesAsync(s.Db, window, WorkLogBucketSize.Hour, null, null, null, default);

        // T0 itself and a microsecond before T0+1h are in the first; T0+1h
        // exactly, a microsecond after it and half past are in the second.
        Assert.Equal(2, history.Buckets[0].Totals.Sessions);
        Assert.Equal(3, history.Buckets[1].Totals.Sessions);

        // The one at the far end of the range is outside it.
        Assert.DoesNotContain(history.Buckets, b => b.End > T0 + TimeSpan.FromHours(6));
        Assert.Equal(6, history.Buckets.Count);
    }

    [SkippableFact]
    public async Task TheSumsAreExact_CostToEightPlaces_UnpricedApart_ErroredCounted()
    {
        await using var s = await SeedAsync();

        var all = await WorkLogRollup.SessionsAsync(
            s.Db, T0 - TimeSpan.FromDays(1), T0 + TimeSpan.FromDays(5), null, null, WorkLogSort.Tokens, 1, null, null, default);

        // Added here from the rows themselves, not by either implementation.
        var rows = await s.Db.WorkLog.AsNoTracking().ToListAsync();
        Assert.Equal(rows.Sum(r => r.CostUsd ?? 0m), all.Totals.CostUsd);
        Assert.Equal(rows.Count(r => r.CostUsd is null), all.Totals.UnpricedSessions);
        Assert.Equal(rows.Count(r => r.IsError), all.Totals.Errors);
        Assert.Equal(rows.Count, all.Totals.Sessions);
        Assert.Equal(rows.Sum(r => r.DurationMs), all.Totals.DurationMs);
        Assert.True(all.Totals.UnpricedSessions > 0 && all.Totals.Errors > 0);
        Assert.NotEqual(Math.Round(all.Totals.CostUsd, 2), all.Totals.CostUsd);
    }

    [SkippableFact]
    public async Task TopReads_AreRankedOrdinally_CutAtTheCap_AndSkipAColumnThatIsNotAList()
    {
        await using var s = await SeedAsync();

        var all = await WorkLogRollup.SessionsAsync(
            s.Db, T0 - TimeSpan.FromDays(1), T0 + TimeSpan.FromDays(5), null, null, WorkLogSort.Tokens, 1, null, null, default);

        Assert.Equal(WorkLogRollup.MaxTopReads, all.TopReads.Count);

        // Equal counts are broken by path as bytes: B before a before b.
        var heaviest = all.TopReads.Take(3).Select(r => r.Path).ToList();
        Assert.Equal(["B", "a", "b"], heaviest);

        // The bare object adds nothing.
        Assert.DoesNotContain(all.TopReads, r => r.Path == "bare");
    }

    // ---- The comparison ----

    private static void AssertSameSessions(WorkLogSessionsDto folded, WorkLogSessionsDto summed, string label)
    {
        Assert.True(folded.From == summed.From && folded.To == summed.To && folded.Sort == summed.Sort, label);
        Assert.True(folded.Totals == summed.Totals, $"{label}: totals {folded.Totals} then {summed.Totals}");
        Assert.True(folded.FirstSessionAt == summed.FirstSessionAt, $"{label}: first");
        Assert.True(folded.LastSessionAt == summed.LastSessionAt, $"{label}: last");

        Assert.True(
            folded.Sessions.Select(r => r.Id).SequenceEqual(summed.Sessions.Select(r => r.Id)),
            $"{label}: rows {string.Join(",", folded.Sessions.Select(r => r.Id))} then {string.Join(",", summed.Sessions.Select(r => r.Id))}");

        for (var i = 0; i < folded.Sessions.Count; i++)
            Assert.True(folded.Sessions[i] == summed.Sessions[i], $"{label}, row {i}: {folded.Sessions[i]} then {summed.Sessions[i]}");

        Assert.Equal(folded.Top is null, summed.Top is null);
        if (folded.Top is not null && summed.Top is not null)
        {
            Assert.True(
                folded.Top.Select(r => r.Id).SequenceEqual(summed.Top.Select(r => r.Id)),
                $"{label}: top {string.Join(",", folded.Top.Select(r => r.Id))} then {string.Join(",", summed.Top.Select(r => r.Id))}");

            for (var i = 0; i < folded.Top.Count; i++)
                Assert.True(folded.Top[i] == summed.Top[i], $"{label}, top row {i}: {folded.Top[i]} then {summed.Top[i]}");
        }

        Assert.True(
            folded.TopReads.SequenceEqual(summed.TopReads),
            $"{label}: reads {string.Join(",", folded.TopReads.Select(r => $"{r.Path}={r.Count}"))} then " +
            string.Join(",", summed.TopReads.Select(r => $"{r.Path}={r.Count}")));
    }

    // ---- The seed ----

    private sealed record Seed(HatchContext Db, long Epic, int ProjectA, int ProjectB) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static async Task<Seed> SeedAsync()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the sums come from a real Postgres. Run `make test-api-db`, " +
            "or point the variable at a scratch database that may be dropped.");

        var connectionString = await HatchDatabase.PrepareAsync();
        var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var a = new EfHatchProject { Key = "AER", Name = "Alpha", CreatedAt = T0 };
        var b = new EfHatchProject { Key = "BEE", Name = "Beta", CreatedAt = T0 };
        var status = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        db.AddRange(a, b, status);
        await db.SaveChangesAsync();

        EfHatchIssue Issue(EfHatchProject p, int n, string type, EfHatchIssue? parent = null) => new()
        {
            ProjectId = p.Id, Number = n, Type = type, Title = $"{type} {n}", StatusId = status.Id,
            ParentId = parent?.Id, Rank = 1024L * n, CreatedBy = "bo", CreatedAt = T0, UpdatedAt = T0,
        };

        var epic = Issue(a, 1, "epic");
        var loose = Issue(a, 2, "task");
        var other = Issue(b, 1, "task");
        db.Issues.AddRange(epic, loose, other);
        await db.SaveChangesAsync();

        var childOne = Issue(a, 3, "story", epic);
        var childTwo = Issue(a, 4, "story", epic);
        var childOfB = Issue(b, 2, "task", epic);
        db.Issues.AddRange(childOne, childTwo, childOfB);
        await db.SaveChangesAsync();

        var n = 0;
        EfHatchWorkLogEntry Session(
            EfHatchIssue issue, TimeSpan after, long input = 1000, long output = 200, decimal? cost = 0.5m,
            int? requests = 10, long? peak = 5000, bool error = false, string? outcome = null, string? reads = null,
            long cacheCreation = 0, long cacheRead = 0, long duration = 60_000) => new()
        {
            IssueId = issue.Id, SessionId = $"s{++n}",
            StartedAt = T0 + after - TimeSpan.FromMinutes(10), EndedAt = T0 + after, DurationMs = duration,
            Title = $"session {n}", IsError = error, Turns = 3, CostUsd = cost, Requests = requests,
            PeakContextTokens = peak, Outcome = outcome, Reads = reads,
            InputTokens = input, OutputTokens = output, CacheCreationTokens = cacheCreation, CacheReadTokens = cacheRead,
            CreatedAt = T0,
        };

        var hour = TimeSpan.FromHours(1);

        db.WorkLog.AddRange(
            // On the first edge, and on the second, and a microsecond either side of it.
            Session(epic, TimeSpan.Zero, cost: 0.00000001m, reads: WorkLogRollup.WriteReads(
                [new("B", 3), new("a", 3), new("b", 3), new("docs/hatch.md", 1)])),
            Session(childOne, hour - Micro, cost: 0.12345679m, requests: null, outcome: "ok"),
            Session(childOne, hour, cost: 0.12345679m, peak: null, outcome: "ok", error: true),
            Session(childTwo, hour + Micro, cost: null, outcome: "dropped", reads: WorkLogRollup.WriteReads([new("c", 2)])),
            Session(childOfB, hour + hour / 2, cost: 1.99999999m, requests: 0, peak: 0),

            // Two sessions with everything equal but their ids, ending at one instant.
            Session(loose, 2 * hour, input: 500, output: 500, cost: 0.25m, requests: 7, peak: 777),
            Session(loose, 2 * hour, input: 500, output: 500, cost: 0.25m, requests: 7, peak: 777),
            Session(other, 2 * hour, input: 700, output: 300, cost: null, requests: null, peak: null, outcome: "ok"),

            // Cache tokens are part of the headline and are not nothing.
            Session(epic, 3 * hour + hour / 2, input: 0, output: 0, cost: 3m, cacheCreation: 4000, cacheRead: 90_000),
            Session(childTwo, 4 * hour - Micro, cost: 0.33333333m, error: true, outcome: "dropped"),

            // Read lists: a bare object stored where a list should be adds nothing,
            // and two long lists of equal counts are cut by path.
            Session(epic, 4 * hour + hour / 3, reads: "{\"path\":\"bare\",\"count\":99}"),
            Session(epic, 5 * hour, reads: WorkLogRollup.WriteReads(
                Enumerable.Range(0, WorkLogRollup.MaxTopReads).Select(i => new WorkLogReadDto($"p{i:00}", 1)).ToList())),
            Session(epic, 5 * hour + hour / 2, reads: WorkLogRollup.WriteReads(
                Enumerable.Range(0, WorkLogRollup.MaxTopReads).Select(i => new WorkLogReadDto($"q{i:00}", 1)).ToList())),

            // On the far edge, which the hourly window leaves out, and either
            // side of the half-hour day edge and of the next.
            Session(epic, 6 * hour, cost: 0.75m),
            Session(childOne, 23 * hour + hour / 2 - Micro, cost: 0.1m),
            Session(childOne, 23 * hour + hour / 2, cost: 0.2m),
            Session(childTwo, 47 * hour + hour / 2 - Micro, cost: 0.3m),
            Session(childTwo, 47 * hour + hour / 2, cost: 0.4m),
            Session(other, 71 * hour + hour / 2, cost: 0.5m),

            // Before everything.
            Session(epic, -hour, cost: 0.9m));

        await db.SaveChangesAsync();

        return new Seed(db, epic.Id, a.Id, b.Id);
    }
}

using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The health poll's trail reads (<see cref="BoardHealthTrail"/>), shown on
/// Postgres to return the rows the filter they replaced returned (HA-2783), and
/// to be answered from the (<c>IssueId</c>, <c>Kind</c>, <c>At</c>) index with no
/// sort of the whole set. It points at <c>HATCH_TEST_DATABASE_URL</c>, which is
/// emptied by each test, so it must never name a database anybody uses.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class BoardHealthTrailTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Since = Now.AddDays(-1);

    private static readonly string[] ClaimKinds = [EfHatchIssueEvent.ClaimTaken, EfHatchIssueEvent.ClaimReleased];

    private sealed record Trail(string ConnectionString, Dictionary<string, long> Issues, int ProjectA);

    private static HatchContext Open(string connectionString) =>
        new(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

    /// <summary>
    /// Six issues over two projects, each a different shape of trail around
    /// <see cref="Since"/>:
    /// <c>a</c> has every kind before and inside; <c>b</c> took a claim and
    /// released it before the window; <c>c</c> has nothing before; <c>d</c>'s
    /// only earlier event is of another kind; <c>e</c> (in the second project)
    /// has pre-approval changes and pull request records; <c>f</c> has two
    /// claim events written out of time order before the window.
    /// </summary>
    private static async Task<Trail> SeedAsync(int noise = 0)
    {
        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = Open(connectionString);

        var p1 = new EfHatchProject { Key = "TRA", Name = "Trail", CreatedAt = Now.AddDays(-120) };
        var p2 = new EfHatchProject { Key = "TRB", Name = "Trail two", CreatedAt = Now.AddDays(-120) };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsWip = true, IsImplementation = true };
        db.AddRange(p1, p2, doing);
        await db.SaveChangesAsync();

        var issues = new Dictionary<string, EfHatchIssue>();
        var n = 0;
        foreach (var (name, project) in new[] { ("a", p1), ("b", p1), ("c", p1), ("d", p2), ("e", p2), ("f", p1) })
        {
            issues[name] = new EfHatchIssue
            {
                ProjectId = project.Id, Number = ++n, Type = "task", Title = name, StatusId = doing.Id,
                Rank = 1024L * n, CreatedBy = "bo", CreatedAt = Now.AddDays(-110), UpdatedAt = Now,
            };
        }

        db.Issues.AddRange(issues.Values);
        await db.SaveChangesAsync();

        EfHatchIssueEvent Ev(string issue, string kind, DateTimeOffset at) => new()
        {
            IssueId = issues[issue].Id, Actor = "bo", Kind = kind, At = at,
            Payload = "{\"from\":\"none\",\"to\":\"express\",\"runner\":\"runner-1\"}",
        };

        // Written one at a time, in this order, so ids follow it.
        var rows = new List<EfHatchIssueEvent>
        {
            Ev("a", EfHatchIssueEvent.StatusChanged, Since.AddDays(-9)),
            Ev("a", EfHatchIssueEvent.StatusChanged, Since.AddDays(-3)),
            Ev("a", EfHatchIssueEvent.ClaimTaken, Since.AddDays(-5)),
            Ev("a", EfHatchIssueEvent.ClaimReleased, Since.AddDays(-4)),
            Ev("a", EfHatchIssueEvent.ClaimTaken, Since.AddDays(-2)),
            Ev("a", EfHatchIssueEvent.PreApprovalChanged, Since.AddDays(-6)),
            Ev("a", EfHatchIssueEvent.PullRequestChanged, Since.AddDays(-6)),
            Ev("a", EfHatchIssueEvent.Commented, Since.AddHours(-1)),
            Ev("a", EfHatchIssueEvent.StatusChanged, Since.AddHours(2)),
            Ev("a", EfHatchIssueEvent.ClaimReleased, Since.AddHours(3)),
            Ev("a", EfHatchIssueEvent.PreApprovalChanged, Since.AddHours(3)),
            Ev("a", EfHatchIssueEvent.PullRequestChanged, Since.AddHours(3)),
            Ev("b", EfHatchIssueEvent.ClaimTaken, Since.AddDays(-4)),
            Ev("b", EfHatchIssueEvent.ClaimReleased, Since.AddDays(-3)),
            Ev("c", EfHatchIssueEvent.StatusChanged, Since.AddHours(1)),
            Ev("c", EfHatchIssueEvent.ClaimTaken, Since.AddHours(1)),
            Ev("d", EfHatchIssueEvent.StatusChanged, Since.AddDays(-2)),
            Ev("d", EfHatchIssueEvent.Commented, Since.AddDays(-2)),
            Ev("d", EfHatchIssueEvent.ClaimTaken, Since.AddHours(5)),
            Ev("e", EfHatchIssueEvent.PreApprovalChanged, Since.AddDays(-8)),
            Ev("e", EfHatchIssueEvent.PreApprovalChanged, Since.AddDays(-7)),
            Ev("e", EfHatchIssueEvent.PullRequestChanged, Since.AddDays(-7)),
            Ev("e", EfHatchIssueEvent.PreApprovalChanged, Since.AddHours(9)),
            // f: the later event is written first, so it has the lower id.
            Ev("f", EfHatchIssueEvent.ClaimTaken, Since.AddHours(-22)),
            Ev("f", EfHatchIssueEvent.ClaimReleased, Since.AddHours(-23)),
            Ev("f", EfHatchIssueEvent.StatusChanged, Since.AddHours(4)),
        };
        foreach (var row in rows)
        {
            db.IssueEvents.Add(row);
            await db.SaveChangesAsync();
        }

        // Noise: events of every kind spread over the long past, on every issue,
        // for a planner that has something to choose between.
        string[] kinds =
        [
            EfHatchIssueEvent.StatusChanged, EfHatchIssueEvent.Commented, EfHatchIssueEvent.ClaimTaken,
            EfHatchIssueEvent.ClaimReleased, EfHatchIssueEvent.PreApprovalChanged, EfHatchIssueEvent.PullRequestChanged,
        ];
        var ids = issues.Values.Select(i => i.Id).ToList();
        for (var i = 0; i < noise; i++)
            db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = ids[i % ids.Count], Actor = "bo", Kind = kinds[i % kinds.Length],
                At = Since.AddDays(-10).AddMinutes(-i * 15), Payload = "{\"from\":\"none\",\"to\":\"express\"}",
            });

        // Runner states: x has rows either side of the window, y only before, z only inside, w none.
        foreach (var (runner, at) in new[]
                 {
                     ("x", Since.AddDays(-5)), ("x", Since.AddDays(-2)), ("x", Since.AddHours(1)), ("x", Since.AddHours(2)),
                     ("y", Since.AddDays(-4)), ("y", Since.AddDays(-3)),
                     ("z", Since.AddHours(3)),
                 })
            db.RunnerStates.Add(new EfHatchRunnerState { Runner = runner, At = at, State = RunnerStateRule.Running });

        await db.SaveChangesAsync();
        await db.Database.OpenConnectionAsync();
        await using var vacuum = db.Database.GetDbConnection().CreateCommand();
        vacuum.CommandText = "VACUUM (ANALYZE)";
        await vacuum.ExecuteNonQueryAsync();

        return new Trail(connectionString, issues.ToDictionary(i => i.Key, i => i.Value.Id), p1.Id);
    }

    /// <summary>The filter <see cref="BoardHealthTrail.ReadAsync"/> replaced, kept here to compare against: the newest earlier is the highest id.</summary>
    private static async Task<List<long>> TodaysFilterAsync(
        HatchContext db, string[] kinds, HashSet<string>? scope, List<long>? issueIds, string? olderOnlyIf)
    {
        var events = db.IssueEvents.AsNoTracking()
            .Where(e => kinds.Contains(e.Kind))
            .Where(e => e.At >= Since
                || ((olderOnlyIf == null || e.Kind == olderOnlyIf)
                    && e.Id == db.IssueEvents
                        .Where(x => x.IssueId == e.IssueId && kinds.Contains(x.Kind) && x.At < Since)
                        .Max(x => (long?)x.Id)));

        if (scope is not null)
        {
            var keys = scope.ToList();
            events = events.Where(e => keys.Contains(e.Issue!.Project!.Key));
        }

        if (issueIds is not null) events = events.Where(e => issueIds.Contains(e.IssueId));

        return (await events.Select(e => e.Id).ToListAsync()).Order().ToList();
    }

    private static async Task AssertSameAsFilterAsync(
        string connectionString, string[] kinds, HashSet<string>? scope = null, List<long>? issueIds = null,
        string? olderOnlyIf = null, ICollection<long>? except = null)
    {
        await using var db = Open(connectionString);
        var expected = await TodaysFilterAsync(db, kinds, scope, issueIds, olderOnlyIf);
        if (except is not null) expected = expected.Where(id => !except.Contains(id)).ToList();
        var actual = (await BoardHealthTrail.ReadAsync(db, kinds, Since, scope, issueIds, default, olderOnlyIf))
            .Select(e => e.Id).Order().ToList();
        if (except is not null) actual = actual.Where(id => !except.Contains(id)).ToList();
        Assert.Equal(expected, actual);
    }

    /// <summary>The ids of the out-of-order pair on issue f, where the two rules differ: the filter takes the higher id, the read the later time.</summary>
    private static async Task<(long Later, long Earlier)> OutOfOrderPairAsync(string connectionString, long issueId)
    {
        await using var db = Open(connectionString);
        var pair = await db.IssueEvents.Where(e => e.IssueId == issueId && e.At < Since).OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, pair.Count);
        Assert.True(pair[0].At > pair[1].At, "the seed's pair is out of time order");
        return (pair[0].Id, pair[1].Id);
    }

    public static TheoryData<string> Reads => new() { "moves", "claims", "levels", "records" };

    [SkippableTheory]
    [MemberData(nameof(Reads))]
    public async Task EachRead_ReturnsTheRowsTheFilterDid(string read)
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the rows come from a real Postgres.");
        var trail = await SeedAsync();
        var f = await OutOfOrderPairAsync(trail.ConnectionString, trail.Issues["f"]);

        switch (read)
        {
            case "moves":
                await AssertSameAsFilterAsync(trail.ConnectionString, [EfHatchIssueEvent.StatusChanged]);
                break;
            case "claims":
                // Issue f is where the rules differ (see the pinning test), so it is left out of both sides.
                await AssertSameAsFilterAsync(
                    trail.ConnectionString, ClaimKinds, olderOnlyIf: EfHatchIssueEvent.ClaimTaken,
                    except: [f.Later, f.Earlier]);
                await AssertSameAsFilterAsync(trail.ConnectionString, ClaimKinds, except: [f.Later, f.Earlier]);
                break;
            case "levels":
                await AssertSameAsFilterAsync(trail.ConnectionString, [EfHatchIssueEvent.PreApprovalChanged]);
                break;
            default:
                await AssertSameAsFilterAsync(trail.ConnectionString, [EfHatchIssueEvent.PullRequestChanged]);
                break;
        }
    }

    [SkippableFact]
    public async Task AClaimTakenAndReleasedBeforeTheWindow_IsNotReadAsOpen()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the rows come from a real Postgres.");
        var trail = await SeedAsync();
        await using var db = Open(trail.ConnectionString);

        var read = await BoardHealthTrail.ReadAsync(
            db, ClaimKinds, Since, null, null, default, olderOnlyIf: EfHatchIssueEvent.ClaimTaken);

        // b took and released before the window: nothing of b's is read.
        Assert.DoesNotContain(read, e => e.IssueId == trail.Issues["b"]);
        // a's newest earlier is its second take, still open at the window's start.
        var a = read.Where(e => e.IssueId == trail.Issues["a"] && e.At < Since).ToList();
        Assert.Single(a);
        Assert.Equal(EfHatchIssueEvent.ClaimTaken, a[0].Kind);
    }

    [SkippableFact]
    public async Task AProjectScope_ReturnsTheRowsTheFilterDid()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the rows come from a real Postgres.");
        var trail = await SeedAsync();

        await AssertSameAsFilterAsync(trail.ConnectionString, [EfHatchIssueEvent.PreApprovalChanged], scope: ["TRB"]);
        await AssertSameAsFilterAsync(trail.ConnectionString, [EfHatchIssueEvent.StatusChanged], scope: ["TRA"]);
        await AssertSameAsFilterAsync(trail.ConnectionString, [EfHatchIssueEvent.PullRequestChanged], scope: ["TRA", "TRB"]);
    }

    [SkippableFact]
    public async Task ACallerWhoSeesOneProject_IsReadTheSameRowsTheFilterGave()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the rows come from a real Postgres.");
        var trail = await SeedAsync();
        var options = new DbContextOptionsBuilder<HatchContext>().UseNpgsql(trail.ConnectionString).Options;
        await using var db = ScopeSeed.Request(options, ScopeSeed.Only(trail.ProjectA));

        // Issue f is where the two rules differ (see the pinning test), so it is left out of both sides.
        var f = trail.Issues["f"];
        var fRows = await db.IssueEvents.Where(e => e.IssueId == f).Select(e => e.Id).ToListAsync();

        foreach (var kinds in new[] { ClaimKinds, [EfHatchIssueEvent.PreApprovalChanged], [EfHatchIssueEvent.PullRequestChanged] })
        {
            var expected = (await TodaysFilterAsync(db, kinds, null, null, null)).Except(fRows).Order().ToList();
            var read = await BoardHealthTrail.ReadAsync(db, kinds, Since, null, null, default);
            var actual = read.Select(e => e.Id).Except(fRows).Order().ToList();

            Assert.Equal(expected, actual);
            // d and e are in the project this caller cannot see.
            Assert.DoesNotContain(read, e => e.IssueId == trail.Issues["d"] || e.IssueId == trail.Issues["e"]);
        }
    }

    [SkippableFact]
    public async Task AnIssueIdList_ReturnsTheRowsTheFilterDid()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the rows come from a real Postgres.");
        var trail = await SeedAsync();

        // c has nothing before the window, d's only earlier event is of another kind, e is in the other project.
        var ids = new List<long> { trail.Issues["a"], trail.Issues["c"], trail.Issues["d"], trail.Issues["e"] };
        await AssertSameAsFilterAsync(trail.ConnectionString, [EfHatchIssueEvent.StatusChanged], issueIds: ids);
        await AssertSameAsFilterAsync(trail.ConnectionString, [EfHatchIssueEvent.PreApprovalChanged], issueIds: ids);
        await AssertSameAsFilterAsync(
            trail.ConnectionString, [EfHatchIssueEvent.PreApprovalChanged], scope: ["TRB"], issueIds: [trail.Issues["e"]]);

        await using var db = Open(trail.ConnectionString);
        Assert.Empty(await BoardHealthTrail.ReadAsync(db, ClaimKinds, Since, null, [], default));
    }

    [SkippableFact]
    public async Task AnIssueWithNothingEarlier_AndAnIssueWhoseEarlierEventIsAnotherKind_AddNothingBeforeTheWindow()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the rows come from a real Postgres.");
        var trail = await SeedAsync();
        await using var db = Open(trail.ConnectionString);

        var read = await BoardHealthTrail.ReadAsync(db, ClaimKinds, Since, null, null, default);

        foreach (var issue in new[] { "c", "d" })
            Assert.DoesNotContain(read, e => e.IssueId == trail.Issues[issue] && e.At < Since);
        Assert.Contains(read, e => e.IssueId == trail.Issues["c"]);
        Assert.Contains(read, e => e.IssueId == trail.Issues["d"]);
    }

    [SkippableFact]
    public async Task TheNewestEarlierEvent_IsTheLatestByTimeThenId_NotTheHighestId()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the rows come from a real Postgres.");
        var trail = await SeedAsync();
        var (later, earlier) = await OutOfOrderPairAsync(trail.ConnectionString, trail.Issues["f"]);
        Assert.True(later < earlier);

        await using var db = Open(trail.ConnectionString);

        // The filter this replaced took the higher id: the event that happened first.
        var before = await TodaysFilterAsync(db, ClaimKinds, null, [trail.Issues["f"]], null);
        Assert.Contains(earlier, before);

        var read = await BoardHealthTrail.ReadAsync(db, ClaimKinds, Since, null, [trail.Issues["f"]], default);
        Assert.Equal([later], read.Select(e => e.Id).ToList());
    }

    [SkippableFact]
    public async Task TwoEventsAtTheSameInstant_ReadTheHigherId()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the rows come from a real Postgres.");
        var trail = await SeedAsync();
        long first, second;
        await using (var db = Open(trail.ConnectionString))
        {
            var at = Since.AddHours(-30);
            var one = new EfHatchIssueEvent { IssueId = trail.Issues["c"], Actor = "bo", Kind = EfHatchIssueEvent.ClaimTaken, At = at };
            var two = new EfHatchIssueEvent { IssueId = trail.Issues["c"], Actor = "bo", Kind = EfHatchIssueEvent.ClaimTaken, At = at };
            db.IssueEvents.Add(one);
            await db.SaveChangesAsync();
            db.IssueEvents.Add(two);
            await db.SaveChangesAsync();
            (first, second) = (one.Id, two.Id);
        }

        await using var read = Open(trail.ConnectionString);
        var earlier = (await BoardHealthTrail.ReadAsync(read, ClaimKinds, Since, null, [trail.Issues["c"]], default))
            .Where(e => e.At < Since).ToList();
        Assert.True(first < second);
        Assert.Equal([second], earlier.Select(e => e.Id).ToList());
    }

    [SkippableFact]
    public async Task RunnerStates_AreTheRowsTheFilterReturned_InTimeOrder()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the rows come from a real Postgres.");
        var trail = await SeedAsync();
        await using var db = Open(trail.ConnectionString);

        List<string>[] nameSets = [["x", "y", "z", "w"], ["x"], ["y", "z"], ["w"], []];
        foreach (var names in nameSets)
        {
            var expected = await db.RunnerStates.AsNoTracking()
                .Where(s => names.Contains(s.Runner))
                .Where(s => s.At >= Since
                    || s.Id == db.RunnerStates
                        .Where(x => x.Runner == s.Runner && x.At < Since)
                        .OrderByDescending(x => x.At).ThenByDescending(x => x.Id)
                        .Select(x => (long?)x.Id)
                        .FirstOrDefault())
                .OrderBy(s => s.At).ThenBy(s => s.Id)
                .Select(s => s.Id)
                .ToListAsync();

            var actual = (await BoardHealthTrail.RunnerStatesAsync(db, names, Since, default)).Select(s => s.Id).ToList();
            Assert.Equal(expected, actual);
        }
    }

    [SkippableFact]
    public async Task TheEarlierEvents_AreReadFromTheIssueKindIndex_WithoutSortingTheSet()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the plan comes from a real Postgres.");
        var trail = await SeedAsync(noise: 12000);

        var sent = await PlanProbe.CaptureAllAsync(
            trail.ConnectionString,
            async db => await BoardHealthTrail.ReadAsync(
                db, ClaimKinds, Since, null, null, default, olderOnlyIf: EfHatchIssueEvent.ClaimTaken));

        // The window read, a probe per kind from Issues, and the read of the rows at the instants they found.
        var probes = sent.Where(s => s.Sql.Contains("max(")).ToList();
        Assert.True(ClaimKinds.Length == probes.Count, string.Join("\n----\n", sent.Select(s => s.Sql)));

        foreach (var probe in probes)
        {
            var plan = await PlanProbe.ExplainAsync(trail.ConnectionString, probe);
            Assert.True(plan.Contains("IX_IssueEvents_IssueId_Kind_At"), plan);
            Assert.DoesNotContain("IssueEvents", PlanProbe.SeqScans(plan));
            Assert.DoesNotContain("Sort", PlanProbe.NodeTypes(plan));
        }
    }
}

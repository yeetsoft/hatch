using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Two people press <em>File a bug</em> on one finding at the same moment, and
/// one bug is filed: the second call waits on the finding's advisory lock, reads
/// the bug the first stored, and answers it (HA-2516).
///
/// <para><b>These run against a real Postgres, and skip without one.</b> The
/// guarantee is a Postgres advisory lock, which EF's in-memory provider has no
/// way to take. Point <c>HATCH_TEST_DATABASE_URL</c> at a scratch database, or
/// run <c>make test-api-db</c>.</para>
///
/// <para>Unlike <see cref="MergedConcurrencyTests"/>, there is no save barrier:
/// the call that loses the race waits on the lock and never reaches a save, so a
/// barrier on saves would only time out. Both calls are released from one
/// signal instead, each on a context of its own.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class GooseFindingBugConcurrencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task TwoCallsOnOneFinding_FileOneBug_AndBothAnswerIt()
    {
        await using var h = await NewAsync(findings: 1);

        var answers = await h.PressTogetherAsync(h.FindingIds[0], h.FindingIds[0]);

        Assert.All(answers, a => Assert.NotNull(a.Value));
        Assert.Equal(answers[0].Value!.Key, answers[1].Value!.Key);

        await using var db = h.NewContext();
        var bug = Assert.Single(await db.Issues.AsNoTracking().Where(i => i.Type == "bug").ToListAsync());
        Assert.Equal(bug.Id, (await db.GooseFindings.AsNoTracking().SingleAsync()).BugIssueId);
        Assert.Single(await db.IssueEvents.Where(e => e.IssueId == bug.Id && e.Kind == EfHatchIssueEvent.Created).ToListAsync());
    }

    [SkippableFact]
    public async Task ManyFindingsEachPressedTwiceAtOnce_FileOneBugEach_UnderNumberCollisions()
    {
        await using var h = await NewAsync(findings: 5);

        var ids = h.FindingIds.SelectMany(id => new[] { id, id }).ToArray();
        var answers = await h.PressTogetherAsync(ids);

        Assert.All(answers, a => Assert.NotNull(a.Value));

        await using var db = h.NewContext();
        var findings = await db.GooseFindings.AsNoTracking().Include(f => f.BugIssue).ToListAsync();
        Assert.Equal(5, await db.Issues.CountAsync(i => i.Type == "bug"));
        Assert.All(findings, f => Assert.NotNull(f.BugIssueId));
        Assert.Equal(5, findings.Select(f => f.BugIssueId).Distinct().Count());

        for (var i = 0; i < answers.Length; i += 2)
            Assert.Equal(answers[i].Value!.Key, answers[i + 1].Value!.Key);
    }

    private static async Task<Harness> NewAsync(int findings)
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the guard is a Postgres advisory lock, which EF's in-memory " +
            "provider cannot take. Run `make test-api-db`, or point the variable at a scratch database.");

        var connectionString = await HatchDatabase.PrepareAsync();

        await using var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.AddRange(project, new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        await db.SaveChangesAsync();

        var rows = Enumerable.Range(1, findings).Select(n => new EfHatchGooseFinding
        {
            Check = "ready-unclaimed", Subject = $"AER{n}", Token = $"AER{n}", ProjectId = project.Id,
            BeganAt = Now.AddHours(-1), OpenedAt = Now.AddMinutes(-30), Sentence = "Ready work is waiting.",
            Evidence = GooseFindingEvidence.Write([new("ready", "3 issues")]),
        }).ToList();
        db.GooseFindings.AddRange(rows);
        await db.SaveChangesAsync();

        return new Harness(connectionString, rows.Select(f => f.Id).ToArray());
    }

    private sealed class Harness(string connectionString, long[] findingIds) : IAsyncDisposable
    {
        private readonly List<HatchContext> contexts = [];

        public long[] FindingIds { get; } = findingIds;

        /// <summary>A context of its own on the shared database, for a test's own reads.</summary>
        public HatchContext NewContext() => new(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        /// <summary>
        /// One press per id, each through a controller of its own over a context
        /// of its own, all held at one signal and let go together.
        /// </summary>
        public async Task<ActionResult<IssueDto>[]> PressTogetherAsync(params long[] ids)
        {
            var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var presses = ids.Select(id =>
            {
                var controller = NewController();
                return Task.Run(async () =>
                {
                    await go.Task;
                    return await controller.PostBug(id, null, default);
                });
            }).ToArray();

            go.SetResult();
            return await Task.WhenAll(presses);
        }

        private GooseFindingBugController NewController()
        {
            var db = NewContext();
            contexts.Add(db);

            var caller = new MergeCheckControllerTests.StubCallerIdentity
            {
                Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now },
            };

            return new GooseFindingBugController(
                db, new RankService(db), new StubActorDirectory(), TestClaims.With(), caller,
                new FakeTimeProvider(Now), Options.Create(new HatchOptions()))
            {
                ControllerContext = new ControllerContext(),
            };
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var db in contexts) await db.DisposeAsync();
        }
    }
}

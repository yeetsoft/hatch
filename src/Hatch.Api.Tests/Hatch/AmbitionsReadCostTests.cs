using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The Ambitions read sends the same statements for two ambitions as for twenty
/// (HA-2843): what each one's stage needs - the columns, the playbooks, the
/// queue's family gate and the budgets - is read once for the whole list.
/// Counted on Postgres, the way <see cref="BoardHealthReadCostTests"/> counts
/// the health poll.
///
/// <para>Both seeds hold an ambition in the implementation column with a budget,
/// an epic and a finished pass, so every read that happens only for such a row
/// happens at both sizes. The twenty are spread over every column.</para>
///
/// <para>It points at <c>HATCH_TEST_DATABASE_URL</c>, which is emptied on every
/// seed, so it must never name a database anybody uses.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class AmbitionsReadCostTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task TheRead_SendsTheSameStatements_ForTwoAmbitionsAsForTwenty()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the counts come from a real Postgres. Run `make test-api-db`, " +
            "or point the variable at a scratch database that may be dropped.");

        var two = await MeasureAsync(2);
        var twenty = await MeasureAsync(20);

        Assert.True(
            two.Count == twenty.Count,
            $"2 ambitions sent {two.Count} statements and 20 sent {twenty.Count}:{Environment.NewLine}{Listed(two)}{Environment.NewLine}{Listed(twenty)}");
    }

    private static string Listed(IReadOnlyList<ReadCounter.Statement> sent) =>
        $"{sent.Count}: {string.Join(", ", sent.Select(s => s.Table))}";

    private static async Task<IReadOnlyList<ReadCounter.Statement>> MeasureAsync(int ambitions)
    {
        var connectionString = await HatchDatabase.PrepareAsync();
        await SeedAsync(connectionString, ambitions);

        var measured = await ReadCounter.MeasureAsync(connectionString, async db =>
        {
            var result = await new AmbitionsController(
                    db, new StubActorDirectory(), new MeScopeTests.StubCaller(null, null, null), new FakeTimeProvider(Now))
                .GetAmbitions(null, default);

            var rows = result.Value ?? throw new InvalidOperationException("the read was refused");
            Assert.Equal(ambitions, rows.Length);
            // The reads that happen only for a row in the implementation column ran.
            Assert.Contains(rows, r => r.Stage == AmbitionStage.BudgetSpent);
        });

        return measured.Statements;
    }

    /// <summary>
    /// The ambitions, dealt round the columns starting at the implementation one.
    /// Each has a capability that holds, an epic that serves it and a work log
    /// row; those in the implementation column have a budget their work has spent
    /// and a pass behind them.
    /// </summary>
    private static async Task SeedAsync(string connectionString, int ambitions)
    {
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsImplementation = true };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var review = new EfHatchStatus { Name = "review", SortOrder = 35 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        var shelved = new EfHatchStatus { Name = "shelved", SortOrder = 50, IsDeferred = true };
        db.AddRange(project, doing, todo, review, done, shelved);
        await db.SaveChangesAsync();

        EfHatchStatus[] columns = [doing, todo, review, done, shelved];
        var number = 0;

        EfHatchIssue Issue(string type, int statusId, long? parentId = null, decimal? budget = null) => new()
        {
            ProjectId = project.Id, Number = ++number, Type = type, Title = $"{type} {number}", Description = "",
            StatusId = statusId, Rank = number, ParentId = parentId, BudgetUsd = budget,
            CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
        };

        for (var n = 0; n < ambitions; n++)
        {
            var column = columns[n % columns.Length];
            var ambition = Issue("ambition", column.Id, budget: column == doing ? 1m : null);
            db.Issues.Add(ambition);
            await db.SaveChangesAsync();

            var epic = Issue("epic", todo.Id, ambition.Id);
            var capability = new EfHatchCapability
            {
                IssueId = ambition.Id, Position = 1, Statement = "it works", HowToTell = "look",
                CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
            };
            db.AddRange(epic, capability);
            await db.SaveChangesAsync();

            db.CapabilityLinks.Add(new EfHatchCapabilityLink { CapabilityId = capability.Id, IssueId = epic.Id, Actor = "operator", At = Now });
            db.CapabilityVerdicts.Add(new EfHatchCapabilityVerdict
            {
                CapabilityId = capability.Id, Verdict = EfHatchCapabilityVerdict.Holds, LookedAt = "the page",
                Sha = "abc1234", Author = "runner", CreatedAt = Now,
            });
            db.WorkLog.Add(new EfHatchWorkLogEntry
            {
                IssueId = ambition.Id, SessionId = $"s-{n}", IssueType = "ambition", Outcome = ClaimOutcomes.Worked,
                PlaybookFromStatusId = doing.Id, PlaybookToStatusId = doing.Id,
                StartedAt = Now.AddHours(-1), EndedAt = Now.AddHours(-1), DurationMs = 0, CreatedAt = Now, CostUsd = 2m,
            });
            await db.SaveChangesAsync();
        }
    }
}

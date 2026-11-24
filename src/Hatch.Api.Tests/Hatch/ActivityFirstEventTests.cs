using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The activity report clamps its start to the first event the caller can see. That read was
/// <c>MIN(At)</c>, and through the query filter's join to Issues it read the whole table
/// (HA-2817). <see cref="ReportsController.FirstEventAtAsync"/> reads the oldest event off the
/// index instead. This holds it to the answer the old read gave, for every scope a caller can
/// have: everything, one project, both, none, and a board with no events at all.
///
/// <para>Postgres, because the point is the query the database runs, and the query filter's
/// scope reaches it as a parameter EF's in-memory provider does not treat the same way. Skipped
/// without <c>HATCH_TEST_DATABASE_URL</c>; <c>make test-api-db</c> sets it.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ActivityFirstEventTests
{
    private static readonly DateTimeOffset Base = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task TheFirstEvent_IsWhatMinOfAtAnswered_ForEveryScope()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the read goes through a real Postgres. " +
            "Run `make test-api-db`, or point the variable at a scratch database that may be dropped.");

        var connectionString = await HatchDatabase.PrepareAsync();
        var options = new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options;

        // A board with no events: nothing to clamp to, under any scope.
        await using (var empty = new HatchContext(options))
        {
            Assert.Null(await Old(empty));
            Assert.Null(await ReportsController.FirstEventAtAsync(empty, default));
        }

        var (a, b) = await SeedAsync(options);

        var scopes = new (string Name, VisibleProjects? Scope)[]
        {
            ("everything", null),
            ("everything, resolved", VisibleProjects.Everything),
            ("project A", ScopeSeed.Only(a)),
            ("project B", ScopeSeed.Only(b)),
            ("both projects", ScopeSeed.Only(a, b)),
            ("nothing", VisibleProjects.Nothing),
        };

        var failures = new List<string>();
        var seen = new Dictionary<string, DateTimeOffset?>();

        foreach (var (name, scope) in scopes)
        {
            await using var db = scope is null
                ? new HatchContext(options)
                : ScopeSeed.Request(options, scope);

            var old = await Old(db);
            var now = await ReportsController.FirstEventAtAsync(db, default);
            seen[name] = now;
            if (old != now) failures.Add($"{name}: MIN(At) answered {old:O}, the first event is {now:O}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        // The answers are not all one instant, or the comparison above proves nothing: A's first
        // event is older than B's, so a caller who sees only B must not be clamped to A's.
        Assert.Equal(Base.AddDays(1), seen["everything"]);
        Assert.Equal(Base.AddDays(1), seen["project A"]);
        Assert.Equal(Base.AddDays(3), seen["project B"]);
        Assert.Null(seen["nothing"]);
    }

    /// <summary>What the report read before: the smallest time among the events the caller sees.</summary>
    private static Task<DateTimeOffset?> Old(HatchContext db) =>
        db.IssueEvents.AsNoTracking().MinAsync(e => (DateTimeOffset?)e.At);

    private static async Task<(int A, int B)> SeedAsync(DbContextOptions<HatchContext> options)
    {
        await using var db = new HatchContext(options);

        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var projectA = new EfHatchProject { Key = "AA", Name = "A", CreatedAt = Base };
        var projectB = new EfHatchProject { Key = "BB", Name = "B", CreatedAt = Base };
        db.AddRange(todo, projectA, projectB);
        await db.SaveChangesAsync();

        EfHatchIssue Issue(EfHatchProject project, int number) => new()
        {
            ProjectId = project.Id, Number = number, Type = "task", Title = "task", Description = "",
            StatusId = todo.Id, Rank = number, CreatedBy = "ann", CreatedAt = Base, UpdatedAt = Base,
        };

        var (issueA, issueB) = (Issue(projectA, 1), Issue(projectB, 1));
        db.AddRange(issueA, issueB);
        await db.SaveChangesAsync();

        // A's oldest is a day in, with a second event at the same instant, and B's is two days
        // later - so the oldest event on the board belongs to the project a B-only caller cannot see.
        db.IssueEvents.AddRange(
            Event(issueA, 1), Event(issueA, 1), Event(issueA, 5), Event(issueA, 9),
            Event(issueB, 3), Event(issueB, 4), Event(issueB, 20));
        await db.SaveChangesAsync();

        return (projectA.Id, projectB.Id);
    }

    private static EfHatchIssueEvent Event(EfHatchIssue issue, int day) => new()
    {
        IssueId = issue.Id, Actor = "bo", Kind = EfHatchIssueEvent.Created, At = Base.AddDays(day),
    };
}

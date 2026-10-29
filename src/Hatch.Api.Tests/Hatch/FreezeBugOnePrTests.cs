using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// A bug can now carry the one-pull-request setting a story does. One already
/// split into tasks was planned on separate branches, so the migration gives it
/// an explicit <c>false</c> - a project whose default is on must not move that
/// in-flight work onto one branch. Everything else stays null and follows the
/// project, a story included.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class FreezeBugOnePrTests
{
    private static readonly DateTimeOffset Seeded = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Up_touches_only_a_bug_with_no_setting_that_has_a_child_and_writes_no_event()
    {
        var sql = Sql(new Probe().Forward);

        Assert.Contains("\"OnePullRequest\" = false", sql);
        Assert.Contains("\"Type\" = 'bug'", sql);
        Assert.Contains("\"OnePullRequest\" IS NULL", sql);
        Assert.Contains("c.\"ParentId\" = \"Issues\".\"Id\"", sql);
        Assert.DoesNotContain("IssueEvents", sql);
        Assert.DoesNotContain("UpdatedAt", sql);
    }

    [SkippableFact]
    public async Task A_bug_with_a_child_gets_false_and_nothing_else_moves()
    {
        await using var db = await OpenAsync();
        var withChild = await AddAsync(db, "bug");
        await AddAsync(db, "task", parent: withChild);
        var childless = await AddAsync(db, "bug");
        var story = await AddAsync(db, "story");
        await AddAsync(db, "task", parent: story);
        var decided = await AddAsync(db, "bug", onePr: true);
        await AddAsync(db, "task", parent: decided);

        await RunAsync(db, new Probe().Forward);

        Assert.False((await ReadAsync(db, withChild)).OnePullRequest);
        Assert.Null((await ReadAsync(db, childless)).OnePullRequest);
        Assert.Null((await ReadAsync(db, story)).OnePullRequest);
        Assert.True((await ReadAsync(db, decided)).OnePullRequest);
        Assert.Equal(Seeded, (await ReadAsync(db, withChild)).UpdatedAt);
        Assert.Empty(await db.Set<EfHatchIssueEvent>().ToListAsync());
    }

    [SkippableFact]
    public async Task Running_Up_twice_leaves_the_state_running_it_once_did()
    {
        await using var db = await OpenAsync();
        var withChild = await AddAsync(db, "bug");
        await AddAsync(db, "task", parent: withChild);
        var childless = await AddAsync(db, "bug");

        await RunAsync(db, new Probe().Forward);
        await RunAsync(db, new Probe().Forward);

        Assert.False((await ReadAsync(db, withChild)).OnePullRequest);
        Assert.Null((await ReadAsync(db, childless)).OnePullRequest);
    }

    [SkippableFact]
    public async Task Down_after_Up_sets_the_frozen_bug_back_to_null_and_leaves_a_story_alone()
    {
        await using var db = await OpenAsync();
        var withChild = await AddAsync(db, "bug");
        await AddAsync(db, "task", parent: withChild);
        var story = await AddAsync(db, "story", onePr: false);
        await AddAsync(db, "task", parent: story);

        await RunAsync(db, new Probe().Forward);
        await RunAsync(db, new Probe().Backward);

        Assert.Null((await ReadAsync(db, withChild)).OnePullRequest);
        Assert.False((await ReadAsync(db, story)).OnePullRequest);
    }

    private static int _number;

    private static async Task<HatchContext> OpenAsync()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        return new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);
    }

    /// <summary>An issue placed straight onto the table - how it got there is not under test.</summary>
    private static async Task<long> AddAsync(HatchContext db, string type, long? parent = null, bool? onePr = null)
    {
        var project = await db.Set<EfHatchProject>().FirstOrDefaultAsync();
        if (project is null)
        {
            project = new EfHatchProject { Key = "FRZ", Name = "Freeze", CreatedAt = Seeded };
            db.Add(project);
        }

        var status = await db.Set<EfHatchStatus>().FirstOrDefaultAsync();
        if (status is null)
        {
            status = new EfHatchStatus { Name = "Inbox", SortOrder = 1 };
            db.Add(status);
        }

        await db.SaveChangesAsync();

        var issue = new EfHatchIssue
        {
            ProjectId = project.Id, Number = Interlocked.Increment(ref _number), Type = type, Title = type,
            StatusId = status.Id, ParentId = parent, OnePullRequest = onePr, Rank = 1024,
            CreatedBy = "operator", CreatedAt = Seeded, UpdatedAt = Seeded,
        };
        db.Add(issue);
        await db.SaveChangesAsync();
        return issue.Id;
    }

    private static async Task RunAsync(HatchContext db, IReadOnlyList<MigrationOperation> operations)
    {
        foreach (var operation in operations.OfType<SqlOperation>())
            await db.Database.ExecuteSqlRawAsync(operation.Sql);
    }

    private static async Task<EfHatchIssue> ReadAsync(HatchContext db, long id)
    {
        db.ChangeTracker.Clear();
        return await db.Set<EfHatchIssue>().SingleAsync(i => i.Id == id);
    }

    private static string Sql(IReadOnlyList<MigrationOperation> operations) =>
        string.Join("\0", operations.OfType<SqlOperation>().Select(o => o.Sql));

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class Probe : FreezeBugOnePr
    {
        public IReadOnlyList<MigrationOperation> Forward => Operations(Up);

        public IReadOnlyList<MigrationOperation> Backward => Operations(Down);

        private static IReadOnlyList<MigrationOperation> Operations(Action<MigrationBuilder> step)
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            step(builder);
            return builder.Operations;
        }
    }
}

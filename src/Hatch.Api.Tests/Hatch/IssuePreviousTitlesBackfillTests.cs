using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The backfill statement <see cref="AddIssuePreviousTitles"/> runs, against real
/// rows - the same shape <see cref="IssueClosedAtBackfillTests"/> uses: this lifts
/// the one SQL operation out of the migration and runs that exact text, so an
/// edit to the migration that changes what the statement does breaks this test
/// instead of drifting beside a copy of it.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class IssuePreviousTitlesBackfillTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task ANeverRetitledIssue_IsLeftAtTheDefaultEmptyString()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync(1, "the gasket");

        await h.BackfillAsync();

        Assert.Equal("", await h.PreviousTitlesAsync(issue));
    }

    [SkippableFact]
    public async Task OneRetitle_LeavesTheOldTitleAlone()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync(1, "the sprocket");
        await h.RetitledAsync(issue, from: "the gasket", at: T0.AddDays(1));

        await h.BackfillAsync();

        Assert.Equal("the gasket", await h.PreviousTitlesAsync(issue));
    }

    [SkippableFact]
    public async Task ARoundTrip_LeavesOnlyTheTitleInBetween()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync(1, "A");
        await h.RetitledAsync(issue, from: "A", at: T0.AddDays(1)); // A -> B
        await h.RetitledAsync(issue, from: "B", at: T0.AddDays(2)); // B -> A

        await h.BackfillAsync();

        Assert.Equal("B", await h.PreviousTitlesAsync(issue));
    }

    [SkippableFact]
    public async Task FourTitles_KeepTheTwoNotCurrent_OldestReplacedFirst()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync(1, "C");
        await h.RetitledAsync(issue, from: "A", at: T0.AddDays(1)); // A -> B
        await h.RetitledAsync(issue, from: "B", at: T0.AddDays(2)); // B -> A
        await h.RetitledAsync(issue, from: "A", at: T0.AddDays(3)); // A -> C

        await h.BackfillAsync();

        Assert.Equal("B\nA", await h.PreviousTitlesAsync(issue));
    }

    [SkippableFact]
    public async Task TwoIssues_AreBackfilledFromTheirOwnEventsOnly()
    {
        await using var h = await NewAsync();
        var a = await h.FileAsync(1, "a-now");
        var b = await h.FileAsync(2, "b-now");
        await h.RetitledAsync(a, from: "a-was", at: T0.AddDays(1));
        await h.RetitledAsync(b, from: "b-was", at: T0.AddDays(1));

        await h.BackfillAsync();

        Assert.Equal("a-was", await h.PreviousTitlesAsync(a));
        Assert.Equal("b-was", await h.PreviousTitlesAsync(b));
    }

    [SkippableFact]
    public async Task ALineBreakInAnOldTitle_IsFoldedToASpace()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync(1, "something else");
        await h.RetitledAsync(issue, from: "the gasket\r\nand the sprocket", at: T0.AddDays(1));

        await h.BackfillAsync();

        Assert.Equal("the gasket and the sprocket", await h.PreviousTitlesAsync(issue));
    }

    private static async Task<Harness> NewAsync()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var project = new EfHatchProject { Key = "PT", Name = "Previous Titles", CreatedAt = T0 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 10 };
        db.AddRange(project, todo);
        await db.SaveChangesAsync();

        return new Harness { Db = db, ProjectId = project.Id, StatusId = todo.Id };
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required HatchContext Db { get; init; }
        public required int ProjectId { get; init; }
        public required int StatusId { get; init; }

        public async Task<EfHatchIssue> FileAsync(int number, string title)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId,
                Number = number,
                Type = "task",
                Title = title,
                StatusId = StatusId,
                Rank = number * 1024,
                CreatedBy = "operator",
                CreatedAt = T0,
                UpdatedAt = T0,
            };
            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        public async Task RetitledAsync(EfHatchIssue issue, string from, DateTimeOffset at)
        {
            Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id,
                Actor = "operator",
                Kind = EfHatchIssueEvent.Retitled,
                Payload = System.Text.Json.JsonSerializer.Serialize(new { from, to = issue.Title }),
                At = at,
            });
            await Db.SaveChangesAsync();
        }

        public async Task BackfillAsync() => await Db.Database.ExecuteSqlRawAsync(BackfillSql);

        public async Task<string> PreviousTitlesAsync(EfHatchIssue issue)
        {
            Db.ChangeTracker.Clear();
            return (await Db.Issues.AsNoTracking().SingleAsync(i => i.Id == issue.Id)).PreviousTitles;
        }

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();

        private static string BackfillSql =>
            new AddIssuePreviousTitlesProbe().Forward.OfType<SqlOperation>().Single().Sql;

        private sealed class AddIssuePreviousTitlesProbe : AddIssuePreviousTitles
        {
            public IReadOnlyList<MigrationOperation> Forward
            {
                get
                {
                    var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
                    Up(builder);
                    return builder.Operations;
                }
            }
        }
    }
}

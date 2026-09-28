using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The stock board's two WIP columns are flagged once, by name, and only where
/// they are neither deferred nor terminal - and no limit row is written.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class StatusWipTests
{
    [Fact]
    public void It_flags_only_In_Progress_and_In_Review_and_only_where_neither_deferred_nor_terminal()
    {
        var sql = Sql(Probe.Forward);

        Assert.Contains("\"IsWip\" = true", sql);
        Assert.Contains("\"Name\" IN ('In Progress', 'In Review')", sql);
        Assert.Contains("NOT \"IsDeferred\"", sql);
        Assert.Contains("NOT \"IsTerminal\"", sql);
    }

    [Fact]
    public void It_writes_no_limit_row()
    {
        var sql = Sql(Probe.Forward);

        Assert.DoesNotContain("INSERT INTO hatch.\"WipLimits\"", sql);
    }

    [Fact]
    public void Down_drops_the_column_the_flag_lived_on()
    {
        var down = Probe.Backward;

        Assert.Contains(down, o => o is DropColumnOperation { Name: "IsWip", Table: "Statuses" });
        Assert.Contains(down, o => o is DropTableOperation { Name: "WipLimits" });
    }

    [SkippableFact]
    public async Task On_a_real_database_the_stock_columns_are_flagged_and_a_renamed_or_deferred_one_is_not()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var inbox = new EfHatchStatus { Name = "Inbox", SortOrder = 10 };
        var doing = new EfHatchStatus { Name = "In Progress", SortOrder = 20 };
        var review = new EfHatchStatus { Name = "In Review", SortOrder = 30, IsDeferred = true };
        var done = new EfHatchStatus { Name = "Done", SortOrder = 40, IsTerminal = true };
        db.AddRange(inbox, doing, review, done);
        await db.SaveChangesAsync();

        foreach (var sql in Sql(Probe.Forward).Split("\u0000"))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        Assert.True((await db.Statuses.SingleAsync(s => s.Id == doing.Id)).IsWip);
        Assert.False((await db.Statuses.SingleAsync(s => s.Id == review.Id)).IsWip);
        Assert.Empty(await db.Set<EfHatchWipLimit>().ToListAsync());
    }

    [SkippableFact]
    public async Task On_a_real_database_a_board_where_neither_name_survives_seeds_nothing()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var todo = new EfHatchStatus { Name = "Todo", SortOrder = 10 };
        var doing = new EfHatchStatus { Name = "Doing", SortOrder = 20 };
        var shipped = new EfHatchStatus { Name = "Shipped", SortOrder = 30, IsTerminal = true };
        db.AddRange(todo, doing, shipped);
        await db.SaveChangesAsync();

        foreach (var sql in Sql(Probe.Forward).Split("\u0000"))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        Assert.DoesNotContain(await db.Statuses.ToListAsync(), s => s.IsWip);
    }

    private static string Sql(IReadOnlyList<MigrationOperation> operations) =>
        string.Join("\u0000", operations.OfType<SqlOperation>().Select(o => o.Sql));

    private static StatusWipProbe Probe => new();

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class StatusWipProbe : StatusWip
    {
        public IReadOnlyList<MigrationOperation> Forward => Operations(Up);

        public IReadOnlyList<MigrationOperation> Backward => Operations(Down);

        private IReadOnlyList<MigrationOperation> Operations(Action<MigrationBuilder> step)
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            step(builder);
            return builder.Operations;
        }
    }
}

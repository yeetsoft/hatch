using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The stock board's Backlog column is flagged once, by name, and only where
/// it is neither deferred nor terminal - a board that has renamed it is left
/// alone.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ParentPullsTests
{
    [Fact]
    public void It_flags_only_Backlog_and_only_where_neither_deferred_nor_terminal()
    {
        var sql = Sql(Probe.Forward);

        Assert.Contains("\"ParentPulls\" = true", sql);
        Assert.Contains("\"Name\" = 'Backlog'", sql);
        Assert.Contains("NOT \"IsDeferred\"", sql);
        Assert.Contains("NOT \"IsTerminal\"", sql);
    }

    [Fact]
    public void Down_drops_the_column_the_flag_lived_on()
    {
        var down = Probe.Backward;

        Assert.Contains(down, o => o is DropColumnOperation { Name: "ParentPulls", Table: "Statuses" });
    }

    [SkippableFact]
    public async Task On_a_real_database_Backlog_is_flagged_and_a_differently_named_column_is_not()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var backlog = new EfHatchStatus { Name = "Backlog", SortOrder = 10 };
        var doing = new EfHatchStatus { Name = "In Progress", SortOrder = 20 };
        db.AddRange(backlog, doing);
        await db.SaveChangesAsync();

        foreach (var sql in Sql(Probe.Forward).Split("\u0000"))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        Assert.True((await db.Statuses.SingleAsync(s => s.Id == backlog.Id)).ParentPulls);
        Assert.False((await db.Statuses.SingleAsync(s => s.Id == doing.Id)).ParentPulls);
    }

    [SkippableFact]
    public async Task On_a_real_database_a_board_where_no_column_is_named_Backlog_seeds_nothing()
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
        Assert.DoesNotContain(await db.Statuses.ToListAsync(), s => s.ParentPulls);
    }

    private static string Sql(IReadOnlyList<MigrationOperation> operations) =>
        string.Join("\u0000", operations.OfType<SqlOperation>().Select(o => o.Sql));

    private static ParentPullsProbe Probe => new();

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class ParentPullsProbe : ParentPulls
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

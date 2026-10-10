using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The stock implementation row gets a starting brief limit; every other row is
/// left at <c>null</c>, and an edited copy of the stock row is left alone too.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class SeedImplementationPlaybookBriefLimitTests
{
    [Fact]
    public void The_stock_constant_is_the_live_implementation_prompt_byte_for_byte()
    {
        Assert.Equal(ShrinkStockPlaybooks.ImplementationReplaced, SeedImplementationPlaybookBriefLimit.Stock);
    }

    [Fact]
    public void The_guard_is_the_whole_stock_prompt_and_nothing_looser()
    {
        var sql = Sql(new Probe().Forward);

        Assert.Contains($"$prompt${SeedImplementationPlaybookBriefLimit.Stock}$prompt$", sql);
        Assert.Contains("SET \"BriefLimit\" = 8000", sql);

        // Prompt alone: no status, Types or Shape condition, and no LIKE loosening the match.
        Assert.DoesNotContain("FromStatusId", sql);
        Assert.DoesNotContain("ToStatusId", sql);
        Assert.DoesNotContain("\"Types\"", sql);
        Assert.DoesNotContain("\"Shape\"", sql);
        Assert.DoesNotContain("LIKE", sql);
    }

    [Fact]
    public void Down_restores_null_only_while_the_row_still_reads_the_stock_prompt_and_brief_limit_8000()
    {
        var sql = Sql(new Probe().Backward);

        Assert.Contains("SET \"BriefLimit\" = NULL", sql);
        Assert.Contains($"WHERE \"Prompt\" = $prompt${SeedImplementationPlaybookBriefLimit.Stock}$prompt$", sql);
        Assert.Contains("AND \"BriefLimit\" = 8000", sql);
    }

    [SkippableFact]
    public async Task The_stock_row_becomes_8000_an_edited_row_is_left_at_null_and_it_is_idempotent()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var todo = new EfHatchStatus { Name = "Todo", SortOrder = 10 };
        var doing = new EfHatchStatus { Name = "Doing", SortOrder = 20 };
        db.AddRange(todo, doing);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var stock = new EfHatchPlaybook
        {
            FromStatusId = todo.Id, ToStatusId = doing.Id, Types = "", Shape = "any",
            Prompt = SeedImplementationPlaybookBriefLimit.Stock, Model = "sonnet", Effort = "high",
            CreatedAt = now, UpdatedAt = now,
        };
        var edited = new EfHatchPlaybook
        {
            FromStatusId = todo.Id, ToStatusId = doing.Id, Types = "", Shape = "leaf",
            Prompt = SeedImplementationPlaybookBriefLimit.Stock + "\n- Also do this.", Model = "sonnet", Effort = "high",
            CreatedAt = now, UpdatedAt = now,
        };
        db.AddRange(stock, edited);
        await db.SaveChangesAsync();

        async Task RunForwardAsync()
        {
            foreach (var sql in Sql(new Probe().Forward).Split('\0'))
                await db.Database.ExecuteSqlRawAsync(sql);
        }

        await RunForwardAsync();

        db.ChangeTracker.Clear();
        Assert.Equal(8000, (await db.Set<EfHatchPlaybook>().FindAsync(stock.Id))!.BriefLimit);
        Assert.Null((await db.Set<EfHatchPlaybook>().FindAsync(edited.Id))!.BriefLimit);

        // Run again: the guard leaves it alone.
        await RunForwardAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(8000, (await db.Set<EfHatchPlaybook>().FindAsync(stock.Id))!.BriefLimit);

        foreach (var sql in Sql(new Probe().Backward).Split('\0'))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        Assert.Null((await db.Set<EfHatchPlaybook>().FindAsync(stock.Id))!.BriefLimit);
        Assert.Null((await db.Set<EfHatchPlaybook>().FindAsync(edited.Id))!.BriefLimit);
    }

    [SkippableFact]
    public async Task A_brief_limit_a_person_has_since_changed_is_left_alone_by_down()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var todo = new EfHatchStatus { Name = "Todo", SortOrder = 10 };
        var doing = new EfHatchStatus { Name = "Doing", SortOrder = 20 };
        db.AddRange(todo, doing);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var stock = new EfHatchPlaybook
        {
            FromStatusId = todo.Id, ToStatusId = doing.Id, Types = "", Shape = "any",
            Prompt = SeedImplementationPlaybookBriefLimit.Stock, Model = "sonnet", Effort = "high", BriefLimit = 20000,
            CreatedAt = now, UpdatedAt = now,
        };
        db.Add(stock);
        await db.SaveChangesAsync();

        foreach (var sql in Sql(new Probe().Backward).Split('\0'))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        Assert.Equal(20000, (await db.Set<EfHatchPlaybook>().FindAsync(stock.Id))!.BriefLimit);
    }

    /// <summary>The SQL an operation list carries, joined with a NUL where there is more than one.</summary>
    private static string Sql(IReadOnlyList<MigrationOperation> operations) =>
        string.Join('\0', operations.OfType<SqlOperation>().Select(o => o.Sql));

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class Probe : SeedImplementationPlaybookBriefLimit
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

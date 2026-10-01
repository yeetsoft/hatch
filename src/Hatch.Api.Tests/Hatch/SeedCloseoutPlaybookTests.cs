using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The stock implementation row narrows to leaf, and a parent row carrying the
/// closeout prompt is seeded beside it - once, and only where nobody has
/// already formed an opinion.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class SeedCloseoutPlaybookTests
{
    /// <summary>
    /// This test's own copy of the seeded text, on purpose. A migration is
    /// frozen text: an edit to it that forgets the row's guard would fail a
    /// rollback, and an edit here that forgets the migration fails this
    /// instead.
    /// </summary>
    private const string Expected = """
        Every child below is closed. This increment checks that what was done is what
        this issue asked for, and writes no implementation code.

        - Read the issue's own acceptance criteria, then each child - its description,
          its comments, and the pull request recorded on it (hatch show <key>, and
          hatch api GET /api/hatch/issues/<key> for the rest).
        - Check the criteria one at a time, naming the child and the commit or pull
          request that met each. A criterion that cannot be verified is one to say so
          about, not one to quietly count.
        - A gap becomes a new child task under this issue, filed with parentKey, not a
          fix made here - which puts the issue back to having an open child, so the
          next pass pulls the new task and the ceremony runs again when it lands.
          docs/hatch-planning.md has the shape.
        - Every criterion met: comment what landed, where to look and what was
          checked, and move the issue on. Only the operator decides that something
          shipped.
        """;

    [Fact]
    public void The_migration_seeds_the_text_this_test_holds()
    {
        Assert.Equal(Expected, SeedCloseoutPlaybook.Closeout);
        Assert.Contains($"$prompt${Expected}$prompt$", Sql(Probe.Forward)[1]);
    }

    [Fact]
    public void The_prompt_says_what_this_increment_checks_and_writes_no_code()
    {
        var prompt = SeedCloseoutPlaybook.Closeout;

        Assert.Contains("Every child below is closed", prompt);
        Assert.Contains("writes no implementation code", prompt);
        Assert.Contains("A gap becomes a new child task under this issue", prompt);
        Assert.Contains("Only the operator decides that something\n  shipped.", prompt.ReplaceLineEndings("\n"));
        Assert.True(prompt.Length <= EfHatchPlaybook.MaxPromptLength);
    }

    [Fact]
    public void It_narrows_the_stock_row_to_leaf_and_does_not_touch_the_epic_specific_one()
    {
        var sql = Sql(Probe.Forward)[0];

        Assert.Contains("SET \"Shape\" = 'leaf'", sql);
        Assert.Contains("AND p.\"Types\" = '' AND p.\"Shape\" = 'any'", sql);
        Assert.DoesNotContain("'epic'", sql);
    }

    [Fact]
    public void It_inserts_the_parent_row_once_and_only_where_none_exists()
    {
        var sql = Sql(Probe.Forward)[1];

        Assert.Contains("'', 'parent', $prompt$", sql);
        Assert.Contains("'sonnet', 'medium'", sql);
        Assert.Contains("WHERE NOT EXISTS", sql);
        Assert.Contains("p.\"FromStatusId\" = i.\"Id\" AND p.\"ToStatusId\" = r.\"Id\"", sql);
    }

    /// <summary>
    /// The review and implementation columns measured the way
    /// <c>Columns.AwaitingReview</c> and <c>Columns.Implementation</c> measure
    /// them: the column before the first terminal one, and the one before that.
    /// </summary>
    [Fact]
    public void It_measures_review_and_implementation_and_does_not_name_either()
    {
        foreach (var sql in Sql(Probe.Forward))
        {
            Assert.Contains("WHERE NOT \"IsDeferred\"", sql);
            Assert.Contains("MIN(n) FROM board WHERE \"IsTerminal\") - 1", sql);
            Assert.Contains("b.n = r.n - 1", sql);
            Assert.DoesNotContain("'review'", sql);
            Assert.DoesNotContain("'in progress'", sql);
        }
    }

    [Fact]
    public void Down_restores_the_leaf_only_while_the_closeout_row_it_wrote_still_stands()
    {
        var sql = Sql(Probe.Backward)[0];

        Assert.Contains("SET \"Shape\" = 'any'", sql);
        Assert.Contains("AND p.\"Types\" = '' AND p.\"Shape\" = 'leaf'", sql);
        Assert.Contains("EXISTS", sql);
        Assert.Contains($"c.\"Prompt\" = $prompt${Expected}$prompt$", sql);
    }

    [Fact]
    public void Down_removes_only_a_row_still_saying_the_stock_text()
    {
        var sql = Sql(Probe.Backward)[1];

        Assert.Contains("p.\"Types\" = '' AND p.\"Shape\" = 'parent'", sql);
        Assert.Contains($"p.\"Prompt\" = $prompt${Expected}$prompt$", sql);
    }

    [SkippableFact]
    public async Task On_a_real_database_the_stock_row_narrows_the_closeout_row_is_seeded_once_and_down_restores_both()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        // A board whose review column is not called review, with a siding
        // sorted between it and done - same shape SeedConflictPlaybookTests
        // uses.
        var todo = new EfHatchStatus { Name = "Todo", SortOrder = 10 };
        var doing = new EfHatchStatus { Name = "Doing", SortOrder = 20 };
        var waiting = new EfHatchStatus { Name = "Waiting on Nathan", SortOrder = 30 };
        var shelf = new EfHatchStatus { Name = "Shelf", SortOrder = 25, IsDeferred = true };
        var done = new EfHatchStatus { Name = "Shipped", SortOrder = 40, IsTerminal = true };
        db.AddRange(todo, doing, waiting, shelf, done);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var stock = new EfHatchPlaybook
        {
            FromStatusId = doing.Id, ToStatusId = waiting.Id, Types = "", Shape = "any",
            Prompt = "You are implementing a ticket...", Model = "sonnet", Effort = "high",
            CreatedAt = now, UpdatedAt = now,
        };
        var epic = new EfHatchPlaybook
        {
            FromStatusId = doing.Id, ToStatusId = waiting.Id, Types = "epic", Shape = "any",
            Prompt = "You are deciding whether an epic is finished...", Model = "sonnet", Effort = "medium",
            CreatedAt = now, UpdatedAt = now,
        };
        db.AddRange(stock, epic);
        await db.SaveChangesAsync();

        async Task RunForwardAsync()
        {
            foreach (var sql in Sql(Probe.Forward))
                await db.Database.ExecuteSqlRawAsync(sql);
        }

        await RunForwardAsync();

        db.ChangeTracker.Clear();
        Assert.Equal("leaf", (await db.Set<EfHatchPlaybook>().FindAsync(stock.Id))!.Shape);
        Assert.Equal("any", (await db.Set<EfHatchPlaybook>().FindAsync(epic.Id))!.Shape);

        var closeout = await db.Set<EfHatchPlaybook>().SingleAsync(
            p => p.FromStatusId == doing.Id && p.ToStatusId == waiting.Id && p.Shape == "parent");
        Assert.Equal(Expected, closeout.Prompt);
        Assert.Equal("", closeout.Types);
        Assert.Equal("sonnet", closeout.Model);
        Assert.Equal("medium", closeout.Effort);

        // Run again: the guards leave everything alone.
        await RunForwardAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Set<EfHatchPlaybook>().CountAsync(p => p.Shape == "parent"));
        Assert.Equal("leaf", (await db.Set<EfHatchPlaybook>().FindAsync(stock.Id))!.Shape);

        foreach (var sql in Sql(Probe.Backward))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        Assert.Equal("any", (await db.Set<EfHatchPlaybook>().FindAsync(stock.Id))!.Shape);
        Assert.Equal("any", (await db.Set<EfHatchPlaybook>().FindAsync(epic.Id))!.Shape);
        Assert.Equal(0, await db.Set<EfHatchPlaybook>().CountAsync(p => p.Shape == "parent"));
    }

    [SkippableFact]
    public async Task On_a_board_with_no_terminal_column_nothing_is_written()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var todo = new EfHatchStatus { Name = "Todo", SortOrder = 10 };
        var doing = new EfHatchStatus { Name = "Doing", SortOrder = 20 };
        var waiting = new EfHatchStatus { Name = "Waiting on Nathan", SortOrder = 30 };
        db.AddRange(todo, doing, waiting);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var stock = new EfHatchPlaybook
        {
            FromStatusId = doing.Id, ToStatusId = waiting.Id, Types = "", Shape = "any",
            Prompt = "You are implementing a ticket...", Model = "sonnet", Effort = "high",
            CreatedAt = now, UpdatedAt = now,
        };
        db.Add(stock);
        await db.SaveChangesAsync();

        foreach (var sql in Sql(Probe.Forward))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        Assert.Equal("any", (await db.Set<EfHatchPlaybook>().FindAsync(stock.Id))!.Shape);
        Assert.Equal(0, await db.Set<EfHatchPlaybook>().CountAsync(p => p.Shape == "parent"));
    }

    private static IReadOnlyList<string> Sql(IReadOnlyList<MigrationOperation> operations) =>
        operations.OfType<SqlOperation>().Select(o => o.Sql).ToList();

    private static SeedCloseoutPlaybookProbe Probe => new();

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class SeedCloseoutPlaybookProbe : SeedCloseoutPlaybook
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

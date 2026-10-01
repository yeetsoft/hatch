using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The epic's implementation-to-review row starts checking the trunk against
/// the epic's own acceptance criteria instead of only that every child closed -
/// reworded where a row still says the seeded text, seeded where nothing covers
/// an epic on the transition at all.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class VerifyEpicPlaybookTests
{
    /// <summary>
    /// This test's own copy of the verification text, on purpose. A migration
    /// is frozen text: an edit to it that forgets the row's guard would fail a
    /// rollback, and an edit here that forgets the migration fails this
    /// instead.
    /// </summary>
    private const string Expected = """
        You are verifying an epic: checking what its description promised against what the
        trunk now does. Every story filed under it has shipped or been shelved, and this is
        the last look before the operator decides whether to close it. You do not close it.

        - Read the epic, its children and what has already happened to it - its comments
          and its events. A previous run may have been aborted partway; continue it rather
          than starting again.
        - Check each acceptance criterion in the epic's description against the trunk as
          origin has it, one at a time. Read the repository; where a criterion is testable,
          build and run the tests with the repository's own commands. A criterion you could
          not check is one to say out loud you could not check, not one to count as holding.
        - Comment the verdict on the epic: each criterion, whether it holds, and the
          evidence - a path, a test, a command and what it printed.
        - A criterion that a child still open, or a child in a deferred column, was filed
          for is not a gap. Name that child rather than filing its work a second time:
          shelving work is the operator's call, and it has been made.
        - If every criterion holds, or the only ones that do not were shelved with a
          deferred child, move the epic to the column named at the end of this prompt, and
          say in the verdict which criteria were shelved.
        - Otherwise file each gap under the epic - one story or bug per gap, with parentKey
          set to the epic, saying what is wrong or missing, with acceptance criteria
          somebody else could check - and leave the epic in the column it is in. The verdict
          names each issue you filed and the criterion it answers. A gap you cannot turn
          into a ticket is a question: ask it, and stop.
        - Never move the epic, or anything under it, into a terminal column. Only the
          operator decides that something shipped.

        Write no code, make no commit and push nothing. If the branch section names a
        branch to cut, you need none: nothing you do changes the tree.
        """;

    [Fact]
    public void The_migration_holds_the_text_this_test_holds()
    {
        Assert.Equal(Expected, VerifyEpicPlaybook.Stock);
        Assert.Contains($"$new${Expected}$new$", Sql(Probe.Forward)[0]);
        Assert.Contains($"$prompt${Expected}$prompt$", Sql(Probe.Forward)[1]);
    }

    /// <summary>Pins the byte-for-byte guard to the text the seed actually wrote.</summary>
    [Fact]
    public void Seeded_is_what_the_original_seed_wrote()
    {
        var seedSql = new PlaybooksProbe().Forward.OfType<SqlOperation>().Select(o => o.Sql);

        Assert.Contains(seedSql, sql => sql.Contains("$prompt$" + VerifyEpicPlaybook.Seeded + "$prompt$", StringComparison.Ordinal));
    }

    [Fact]
    public void The_text_checks_against_the_trunk_comments_a_verdict_files_gaps_and_asks()
    {
        var flat = Flat(Expected);

        Assert.Contains("checking what its description promised against what the trunk now does", flat);
        Assert.Contains("Check each acceptance criterion in the epic's description against the trunk", flat);
        Assert.Contains("Comment the verdict on the epic: each criterion, whether it holds, and the evidence", flat);
        Assert.Contains("file each gap under the epic", flat);
        Assert.Contains("one story or bug per gap, with parentKey", flat);
        Assert.Contains("A gap you cannot turn into a ticket is a question: ask it, and stop.", flat);
    }

    [Fact]
    public void The_text_never_closes_the_epic_except_to_forbid_it()
    {
        var flat = Flat(Expected);

        Assert.Contains("You do not close it.", flat);
        Assert.Contains("Never move the epic, or anything under it, into a terminal column.", flat);
        Assert.Contains("Only the operator decides that something shipped.", flat);
        Assert.DoesNotContain("close the epic", flat.Replace("You do not close it.", ""));
    }

    [Fact]
    public void The_text_writes_no_code_pushes_nothing_names_no_column_and_fits_the_column_limit()
    {
        var flat = Flat(Expected);

        Assert.Contains("Write no code, make no commit and push nothing.", flat);
        Assert.DoesNotContain("In Progress", Expected);
        Assert.DoesNotContain("In Review", Expected);
        Assert.DoesNotContain("'review'", Expected);
        Assert.True(Expected.Length <= EfHatchPlaybook.MaxPromptLength);
    }

    [Fact]
    public void The_reword_guard_is_the_whole_seeded_prompt_and_nothing_looser()
    {
        var sql = Sql(Probe.Forward)[0];

        Assert.Contains($"$new${VerifyEpicPlaybook.Stock}$new$", sql);
        Assert.Contains($"\"Prompt\" = $old${VerifyEpicPlaybook.Seeded}$old$", sql);
        Assert.DoesNotContain("LIKE", sql);
    }

    [Fact]
    public void The_reword_only_lifts_model_and_effort_off_sonnet_and_medium()
    {
        var sql = Sql(Probe.Forward)[0];

        Assert.Contains("CASE WHEN \"Model\" = 'sonnet' AND \"Effort\" = 'medium' THEN 'opus' ELSE \"Model\" END", sql);
        Assert.Contains("CASE WHEN \"Model\" = 'sonnet' AND \"Effort\" = 'medium' THEN 'high' ELSE \"Effort\" END", sql);
    }

    /// <summary>
    /// The review and implementation columns measured the way
    /// <c>Columns.AwaitingReview</c> and <c>Columns.Implementation</c> measure
    /// them, and named nowhere in the insert.
    /// </summary>
    [Fact]
    public void The_insert_measures_the_columns_and_names_neither()
    {
        var sql = Sql(Probe.Forward)[1];

        Assert.Contains("WHERE NOT \"IsDeferred\"", sql);
        Assert.Contains("MIN(n) FROM board WHERE \"IsTerminal\") - 1", sql);
        Assert.Contains("b.n = r.n - 1", sql);
        Assert.DoesNotContain("'In Progress'", sql);
        Assert.DoesNotContain("'In Review'", sql);
        Assert.DoesNotContain("'review'", sql);
    }

    [Fact]
    public void The_insert_covers_empty_or_epic_types_leaves_shape_to_its_default_and_writes_opus_and_high()
    {
        var sql = Sql(Probe.Forward)[1];

        Assert.Contains(
            "(\"FromStatusId\", \"ToStatusId\", \"Types\", \"Prompt\", \"Model\", \"Effort\", \"CreatedAt\", \"UpdatedAt\")",
            sql);
        Assert.Contains("'epic', $prompt$", sql);
        Assert.Contains("'opus', 'high'", sql);
        Assert.Contains("p.\"Types\" = '' OR 'epic' = ANY(string_to_array(p.\"Types\", ','))", sql);
        Assert.Contains("p.\"Shape\" = 'any' OR p.\"Shape\" = 'parent'", sql);
    }

    [Fact]
    public void Down_is_one_statement_that_touches_only_a_row_still_reading_the_verification_text()
    {
        var sql = Sql(Probe.Backward);
        Assert.Single(sql);

        Assert.Contains($"SET \"Prompt\" = $old${VerifyEpicPlaybook.Seeded}$old$", sql[0]);
        Assert.Contains($"\"Prompt\" = $new${VerifyEpicPlaybook.Stock}$new$", sql[0]);
        Assert.Contains("CASE WHEN \"Model\" = 'opus' AND \"Effort\" = 'high' THEN 'sonnet' ELSE \"Model\" END", sql[0]);
        Assert.Contains("CASE WHEN \"Model\" = 'opus' AND \"Effort\" = 'high' THEN 'medium' ELSE \"Effort\" END", sql[0]);
    }

    [SkippableFact]
    public async Task On_a_real_database_an_unedited_row_is_reworded_and_lifted_an_edited_one_is_untouched_and_down_restores_the_unedited()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var todo = new EfHatchStatus { Name = "Todo", SortOrder = 10 };
        var doing = new EfHatchStatus { Name = "Doing", SortOrder = 20 };
        var waiting = new EfHatchStatus { Name = "Waiting on Nathan", SortOrder = 30 };
        var shelf = new EfHatchStatus { Name = "Shelf", SortOrder = 25, IsDeferred = true };
        var done = new EfHatchStatus { Name = "Shipped", SortOrder = 40, IsTerminal = true };
        db.AddRange(todo, doing, waiting, shelf, done);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var unedited = new EfHatchPlaybook
        {
            FromStatusId = doing.Id, ToStatusId = waiting.Id, Types = "epic", Shape = "any",
            Prompt = VerifyEpicPlaybook.Seeded, Model = "sonnet", Effort = "medium",
            CreatedAt = now, UpdatedAt = now,
        };
        var edited = new EfHatchPlaybook
        {
            FromStatusId = doing.Id, ToStatusId = waiting.Id, Types = "story", Shape = "any",
            Prompt = VerifyEpicPlaybook.Seeded + "\n- Also do this.", Model = "sonnet", Effort = "medium",
            CreatedAt = now, UpdatedAt = now,
        };
        db.AddRange(unedited, edited);
        await db.SaveChangesAsync();

        async Task RunForwardAsync()
        {
            foreach (var sql in Sql(Probe.Forward))
                await db.Database.ExecuteSqlRawAsync(sql);
        }

        await RunForwardAsync();

        db.ChangeTracker.Clear();
        var reworded = (await db.Set<EfHatchPlaybook>().FindAsync(unedited.Id))!;
        Assert.Equal(VerifyEpicPlaybook.Stock, reworded.Prompt);
        Assert.Equal("opus", reworded.Model);
        Assert.Equal("high", reworded.Effort);
        Assert.Equal(VerifyEpicPlaybook.Seeded + "\n- Also do this.", (await db.Set<EfHatchPlaybook>().FindAsync(edited.Id))!.Prompt);

        // Run again: the guards leave everything alone, and no row is inserted -
        // the reworded row already covers an epic on this transition.
        await RunForwardAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Set<EfHatchPlaybook>().CountAsync(p => p.FromStatusId == doing.Id && p.ToStatusId == waiting.Id));

        foreach (var sql in Sql(Probe.Backward))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        var restored = (await db.Set<EfHatchPlaybook>().FindAsync(unedited.Id))!;
        Assert.Equal(VerifyEpicPlaybook.Seeded, restored.Prompt);
        Assert.Equal("sonnet", restored.Model);
        Assert.Equal("medium", restored.Effort);
        Assert.Equal(VerifyEpicPlaybook.Seeded + "\n- Also do this.", (await db.Set<EfHatchPlaybook>().FindAsync(edited.Id))!.Prompt);
    }

    [SkippableFact]
    public async Task On_a_real_database_a_retuned_row_is_reworded_but_keeps_its_own_model_and_effort()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var doing = new EfHatchStatus { Name = "Doing", SortOrder = 10 };
        var waiting = new EfHatchStatus { Name = "Waiting on Nathan", SortOrder = 20 };
        var done = new EfHatchStatus { Name = "Shipped", SortOrder = 30, IsTerminal = true };
        db.AddRange(doing, waiting, done);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var retuned = new EfHatchPlaybook
        {
            FromStatusId = doing.Id, ToStatusId = waiting.Id, Types = "epic", Shape = "any",
            Prompt = VerifyEpicPlaybook.Seeded, Model = "haiku", Effort = "low",
            CreatedAt = now, UpdatedAt = now,
        };
        db.Add(retuned);
        await db.SaveChangesAsync();

        foreach (var sql in Sql(Probe.Forward))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        var reworded = (await db.Set<EfHatchPlaybook>().FindAsync(retuned.Id))!;
        Assert.Equal(VerifyEpicPlaybook.Stock, reworded.Prompt);
        Assert.Equal("haiku", reworded.Model);
        Assert.Equal("low", reworded.Effort);

        foreach (var sql in Sql(Probe.Backward))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        var restored = (await db.Set<EfHatchPlaybook>().FindAsync(retuned.Id))!;
        Assert.Equal(VerifyEpicPlaybook.Seeded, restored.Prompt);
        Assert.Equal("haiku", restored.Model);
        Assert.Equal("low", restored.Effort);
    }

    [SkippableFact]
    public async Task On_a_real_database_a_board_with_no_covering_row_gets_the_stock_row_once_and_down_puts_the_seeded_text_back()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var doing = new EfHatchStatus { Name = "Doing", SortOrder = 10 };
        var waiting = new EfHatchStatus { Name = "Waiting on Nathan", SortOrder = 20 };
        var done = new EfHatchStatus { Name = "Shipped", SortOrder = 30, IsTerminal = true };
        db.AddRange(doing, waiting, done);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.Set<EfHatchPlaybook>().CountAsync());

        async Task RunForwardAsync()
        {
            foreach (var sql in Sql(Probe.Forward))
                await db.Database.ExecuteSqlRawAsync(sql);
        }

        await RunForwardAsync();

        db.ChangeTracker.Clear();
        var seeded = await db.Set<EfHatchPlaybook>().SingleAsync(
            p => p.FromStatusId == doing.Id && p.ToStatusId == waiting.Id);
        Assert.Equal(VerifyEpicPlaybook.Stock, seeded.Prompt);
        Assert.Equal("epic", seeded.Types);
        Assert.Equal("any", seeded.Shape);
        Assert.Equal("opus", seeded.Model);
        Assert.Equal("high", seeded.Effort);

        // Run again: it already covers an epic, so nothing more is inserted.
        await RunForwardAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Set<EfHatchPlaybook>().CountAsync());

        // Down cannot tell this row apart from one it reworded - it puts the
        // seeded text back rather than deleting the row.
        foreach (var sql in Sql(Probe.Backward))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Set<EfHatchPlaybook>().CountAsync());
        var restored = await db.Set<EfHatchPlaybook>().SingleAsync();
        Assert.Equal(VerifyEpicPlaybook.Seeded, restored.Prompt);
        Assert.Equal("sonnet", restored.Model);
        Assert.Equal("medium", restored.Effort);
    }

    [SkippableFact]
    public async Task On_a_real_database_a_leaf_scoped_every_type_row_never_covers_an_epic_so_the_stock_row_is_still_seeded()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var doing = new EfHatchStatus { Name = "Doing", SortOrder = 10 };
        var waiting = new EfHatchStatus { Name = "Waiting on Nathan", SortOrder = 20 };
        var done = new EfHatchStatus { Name = "Shipped", SortOrder = 30, IsTerminal = true };
        db.AddRange(doing, waiting, done);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var leaf = new EfHatchPlaybook
        {
            FromStatusId = doing.Id, ToStatusId = waiting.Id, Types = "", Shape = "leaf",
            Prompt = "You are implementing a ticket...", Model = "sonnet", Effort = "high",
            CreatedAt = now, UpdatedAt = now,
        };
        db.Add(leaf);
        await db.SaveChangesAsync();

        foreach (var sql in Sql(Probe.Forward))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        var seeded = await db.Set<EfHatchPlaybook>().SingleAsync(p => p.Shape != "leaf");
        Assert.Equal(VerifyEpicPlaybook.Stock, seeded.Prompt);
        Assert.Equal("epic", seeded.Types);
        Assert.Equal(2, await db.Set<EfHatchPlaybook>().CountAsync());
    }

    [SkippableFact]
    public async Task On_a_real_database_an_any_or_parent_scoped_every_type_row_already_covers_epics_so_nothing_is_inserted()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var doing = new EfHatchStatus { Name = "Doing", SortOrder = 10 };
        var waiting = new EfHatchStatus { Name = "Waiting on Nathan", SortOrder = 20 };
        var done = new EfHatchStatus { Name = "Shipped", SortOrder = 30, IsTerminal = true };
        db.AddRange(doing, waiting, done);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var parent = new EfHatchPlaybook
        {
            FromStatusId = doing.Id, ToStatusId = waiting.Id, Types = "", Shape = "parent",
            Prompt = "You are closing out...", Model = "sonnet", Effort = "medium",
            CreatedAt = now, UpdatedAt = now,
        };
        db.Add(parent);
        await db.SaveChangesAsync();

        foreach (var sql in Sql(Probe.Forward))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Set<EfHatchPlaybook>().CountAsync());
        Assert.Equal("You are closing out...", (await db.Set<EfHatchPlaybook>().FindAsync(parent.Id))!.Prompt);
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

        foreach (var sql in Sql(Probe.Forward))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.Set<EfHatchPlaybook>().CountAsync());
    }

    /// <summary>The prompt as one line, so a phrase is found wherever the text wraps it.</summary>
    private static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

    private static IReadOnlyList<string> Sql(IReadOnlyList<MigrationOperation> operations) =>
        operations.OfType<SqlOperation>().Select(o => o.Sql).ToList();

    private static VerifyEpicPlaybookProbe Probe => new();

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class VerifyEpicPlaybookProbe : VerifyEpicPlaybook
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

    /// <summary>The original seed's operations, without a database to run them against - so <see cref="VerifyEpicPlaybook.Seeded"/> can be pinned to what it actually wrote.</summary>
    private sealed class PlaybooksProbe : Playbooks
    {
        public IReadOnlyList<MigrationOperation> Forward => Operations(Up);

        private IReadOnlyList<MigrationOperation> Operations(Action<MigrationBuilder> step)
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            step(builder);
            return builder.Operations;
        }
    }
}

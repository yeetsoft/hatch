using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The stock row that shapes an ambition is seeded once, flagged, from
/// Breakdown to Backlog, where no flagged row already covers an ambition, and
/// takes nothing back that somebody has written. The reflection row and the
/// epic rows on the same edge do not stand in its way, and a board without
/// either column gets nothing.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class SeedAmbitionShapingPlaybookTests
{
    /// <summary>
    /// This test's own copy of the stock text, on purpose. A migration is frozen
    /// text: an edit to it that forgets the row's guard would fail a rollback,
    /// and an edit here that forgets the migration fails this instead.
    /// </summary>
    private const string Expected = """
        You are shaping a draft ambition. The prompt gives you what the person wrote: their words, the
        dreams they drew on and any files. You judge and you write. You never edit the tree, and your
        tools refuse if you try.

        - Read the draft's comments and events first. A previous run may have been aborted partway;
          continue it rather than starting again. Keep what the description already says in the
          ambition's shape, and add only what is missing.
        - Work from what the prompt's own section holds: the person's words, the dreams and the files.
        - Give the draft a title of a handful of words.
        - Write the description in this shape and no other: a summary of at most three short paragraphs
          before any heading; then `## What this is for`; then `## Capabilities`, a table with a number,
          what is true when it is done, and how to tell; then `## Principles`; then `## Out of scope`.
        - A capability says what is true, never what work to do. Its how-to-tell line is something a
          later session could check on the trunk. Each line is one line of at most 300 characters.
        - Add each capability as a row, in the table's order, with
          `hatch capability add <key> "<statement>" --how "<how to tell>"`.
        - Where the brief does not settle something that is the person's to choose, ask with `hatch ask`,
          one call per question, the choices named and one recommended. Do not guess, and do not fill a
          gap with a capability nobody asked for.
        - Record the dreams you drew on with `hatch api PUT /api/issues/<key>/dreams`.
        - List no epics and no work, and file nothing under the draft. Move it only to the column named
          at the end of this prompt, and never start it: starting an ambition is a person's.
        - Name no board, repository, domain or person: nothing said here may be true of only the
          installation this happens to run on.
        """;

    private const string ExpectedDeniedTools =
        """["Edit","Write","NotebookEdit","Bash(git add *)","Bash(git commit *)","Bash(git push *)","Bash(git checkout *)","Bash(git switch *)","Bash(git reset *)","Bash(git restore *)","Bash(git stash *)","Bash(git merge *)","Bash(git rebase *)","Bash(git cherry-pick *)","Bash(git clean *)"]""";

    private static readonly string[] ExpectedRules =
    [
        "Edit", "Write", "NotebookEdit",
        "Bash(git add *)", "Bash(git commit *)", "Bash(git push *)", "Bash(git checkout *)",
        "Bash(git switch *)", "Bash(git reset *)", "Bash(git restore *)", "Bash(git stash *)",
        "Bash(git merge *)", "Bash(git rebase *)", "Bash(git cherry-pick *)", "Bash(git clean *)",
    ];

    [Fact]
    public void The_migration_seeds_the_text_this_test_holds()
    {
        Assert.Equal(Expected, SeedAmbitionShapingPlaybook.Stock);
        Assert.Contains($"$prompt${Expected}$prompt$", Sql(Probe.Forward));
        Assert.True(Expected.Length <= EfHatchPlaybook.MaxPromptLength);
    }

    [Fact]
    public void The_stock_text_names_no_ticket_board_or_person()
    {
        Assert.DoesNotMatch(@"\b[A-Z]{2,}-\d+\b", Expected);
        Assert.DoesNotContain("http", Expected);
    }

    [Fact]
    public void The_stock_text_says_the_things_in_order()
    {
        var lines = new[]
        {
            "You are shaping a draft ambition.",
            "Read the draft's comments and events first.",
            "Keep what the description already says in the",
            "Work from what the prompt's own section holds",
            "Give the draft a title of a handful of words.",
            "a summary of at most three short paragraphs",
            "`## What this is for`",
            "`## Capabilities`",
            "`## Principles`",
            "`## Out of scope`",
            "A capability says what is true, never what work to do.",
            "at most 300 characters",
            "`hatch capability add <key> \"<statement>\" --how \"<how to tell>\"`",
            "ask with `hatch ask`",
            "`hatch api PUT /api/issues/<key>/dreams`",
            "List no epics and no work, and file nothing under the draft.",
            "never start it: starting an ambition is a person's.",
            "Name no board, repository, domain or person:",
        };

        var last = -1;
        foreach (var line in lines)
        {
            var at = Expected.IndexOf(line, StringComparison.Ordinal);
            Assert.True(at >= 0, $"missing: {line}");
            Assert.True(at > last, $"out of order: {line}");
            last = at;
        }
    }

    [Fact]
    public void It_seeds_the_default_prompt_and_leaves_the_operators_prompt_null()
    {
        var sql = Sql(Probe.Forward);

        Assert.Contains("\"Prompt\", \"DefaultPrompt\"", sql);
        Assert.Contains($"'any', NULL, $prompt${Expected}$prompt$", sql);
    }

    [Fact]
    public void It_seeds_a_flagged_ambition_row_with_fable_and_max_from_breakdown_to_backlog_by_name()
    {
        var sql = Sql(Probe.Forward);

        Assert.Contains("f.\"Id\", t.\"Id\", 'ambition', 'any'", sql);
        Assert.Contains("'fable', 'max'", sql);
        Assert.Contains("\"DeniedTools\", \"ShapesAmbition\", \"CreatedAt\"", sql);
        Assert.Contains("::jsonb,\n       true, now(), now()", sql.Replace("\r", ""));
        Assert.Contains("f.\"Name\" = 'Breakdown' AND t.\"Name\" = 'Backlog'", sql);
    }

    [Fact]
    public void It_denies_the_tools_that_edit_the_tree_and_the_git_commands_that_move_it()
    {
        Assert.Contains($"'{ExpectedDeniedTools}'::jsonb", Sql(Probe.Forward));

        var rules = EfHatchPlaybook.ReadDeniedTools(ExpectedDeniedTools);
        Assert.Equal(ExpectedRules, rules);
        Assert.True(rules!.Count <= EfHatchPlaybook.MaxDeniedTools);
        Assert.All(rules, rule => Assert.True(rule.Length <= EfHatchPlaybook.MaxToolRuleLength));
    }

    [Fact]
    public void The_denied_tools_are_the_ambition_pass_rows_so_the_two_lists_cannot_drift()
    {
        var pass = string.Join("\n", new SeedAmbitionPassPlaybookProbe().Forward.OfType<SqlOperation>().Select(o => o.Sql));

        Assert.Contains($"'{ExpectedDeniedTools}'::jsonb", pass);
        Assert.Contains($"'{ExpectedDeniedTools}'::jsonb", Sql(Probe.Forward));
    }

    [Fact]
    public void It_seeds_only_where_no_flagged_row_already_covers_an_ambition()
    {
        var sql = Sql(Probe.Forward);

        Assert.Contains("AND NOT EXISTS", sql);
        Assert.Contains("AND p.\"ShapesAmbition\"\n", sql.Replace("\r", ""));
        Assert.Contains("p.\"FromStatusId\" = f.\"Id\" AND p.\"ToStatusId\" = t.\"Id\"", sql);
        Assert.Contains("(p.\"Types\" = '' OR 'ambition' = ANY(string_to_array(p.\"Types\", ',')))", sql);
    }

    [Fact]
    public void Down_removes_only_a_row_still_saying_the_stock_text_with_no_prompt_of_its_own()
    {
        var sql = Sql(Probe.Backward);

        Assert.Contains("f.\"Name\" = 'Breakdown' AND t.\"Name\" = 'Backlog'", sql);
        Assert.Contains("p.\"FromStatusId\" = f.\"Id\" AND p.\"ToStatusId\" = t.\"Id\"", sql);
        Assert.Contains("p.\"Types\" = 'ambition'", sql);
        Assert.Contains("p.\"ShapesAmbition\"", sql);
        Assert.Contains("p.\"Prompt\" IS NULL", sql);
        Assert.Contains($"p.\"DefaultPrompt\" = $prompt${Expected}$prompt$", sql);
    }

    [SkippableFact]
    public async Task On_a_real_database_a_shaping_row_is_seeded_once_and_an_edited_one_is_left_alone()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var (breakdown, backlog) = await AddColumnsAsync(db);

        await RunAsync(db, Probe.Forward);

        db.ChangeTracker.Clear();
        var seeded = await db.Set<EfHatchPlaybook>().SingleAsync();
        Assert.Equal(breakdown.Id, seeded.FromStatusId);
        Assert.Equal(backlog.Id, seeded.ToStatusId);
        Assert.Equal("ambition", seeded.Types);
        Assert.Equal("any", seeded.Shape);
        Assert.Null(seeded.Prompt);
        Assert.Equal(Expected, seeded.DefaultPrompt);
        Assert.Equal("fable", seeded.Model);
        Assert.Equal("max", seeded.Effort);
        Assert.False(seeded.IsCheck);
        Assert.False(seeded.AnswersQuestions);
        Assert.False(seeded.WorksOutChanges);
        Assert.True(seeded.ShapesAmbition);
        Assert.Equal(ExpectedRules, EfHatchPlaybook.ReadDeniedTools(seeded.DeniedTools));

        // Run again: the guard leaves it alone - no duplicate.
        await RunAsync(db, Probe.Forward);
        Assert.Equal(1, await db.Set<EfHatchPlaybook>().CountAsync());

        // Down leaves a row with a prompt of its own.
        db.ChangeTracker.Clear();
        var edited = await db.Set<EfHatchPlaybook>().SingleAsync();
        edited.Prompt = "Our own shaping wording.";
        await db.SaveChangesAsync();
        await RunAsync(db, Probe.Backward);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Set<EfHatchPlaybook>().CountAsync());

        // And one whose stock text has been edited.
        var reworded = await db.Set<EfHatchPlaybook>().SingleAsync();
        reworded.Prompt = null;
        reworded.DefaultPrompt = "Edited.";
        await db.SaveChangesAsync();
        await RunAsync(db, Probe.Backward);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Set<EfHatchPlaybook>().CountAsync());

        // But removes one that is untouched.
        var restored = await db.Set<EfHatchPlaybook>().SingleAsync();
        restored.DefaultPrompt = Expected;
        await db.SaveChangesAsync();
        await RunAsync(db, Probe.Backward);
        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.Set<EfHatchPlaybook>().CountAsync());
    }

    [SkippableTheory]
    [InlineData("")]
    [InlineData("ambition")]
    public async Task On_a_real_database_a_flagged_row_already_covering_an_ambition_means_nothing_is_seeded(string types)
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var (breakdown, backlog) = await AddColumnsAsync(db);
        db.Add(Row(breakdown, backlog, types, shapesAmbition: true));
        await db.SaveChangesAsync();

        await RunAsync(db, Probe.Forward);

        db.ChangeTracker.Clear();
        var only = await db.Set<EfHatchPlaybook>().SingleAsync();
        Assert.Equal(types, only.Types);
        Assert.Equal("Our own wording.", only.Prompt);
    }

    [SkippableFact]
    public async Task On_a_real_database_the_reflection_row_on_the_same_edge_does_not_block_the_seed()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var (breakdown, backlog) = await AddColumnsAsync(db);
        db.Add(Row(breakdown, backlog, "reflection", shapesAmbition: false));
        await db.SaveChangesAsync();

        await RunAsync(db, Probe.Forward);

        db.ChangeTracker.Clear();
        var rows = await db.Set<EfHatchPlaybook>().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, p => !p.ShapesAmbition && p.Types == "reflection" && p.Prompt == "Our own wording.");
        Assert.Single(rows, p => p.ShapesAmbition && p.DefaultPrompt == Expected);
    }

    [SkippableFact]
    public async Task On_a_real_database_an_unflagged_row_covering_an_ambition_on_that_edge_does_not_block_the_seed()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var (breakdown, backlog) = await AddColumnsAsync(db);
        db.Add(Row(breakdown, backlog, "epic", shapesAmbition: false));
        db.Add(Row(breakdown, backlog, "", shapesAmbition: false, shape: "leaf"));
        await db.SaveChangesAsync();

        await RunAsync(db, Probe.Forward);

        db.ChangeTracker.Clear();
        var rows = await db.Set<EfHatchPlaybook>().ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Single(rows, p => p.ShapesAmbition && p.DefaultPrompt == Expected);
    }

    [SkippableFact]
    public async Task On_a_real_database_a_board_missing_either_column_gets_nothing()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        db.Add(new EfHatchStatus { Name = "Breakdown", SortOrder = 20 });
        await db.SaveChangesAsync();
        await RunAsync(db, Probe.Forward);
        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.Set<EfHatchPlaybook>().CountAsync());

        db.RemoveRange(await db.Set<EfHatchStatus>().ToListAsync());
        db.Add(new EfHatchStatus { Name = "Backlog", SortOrder = 30 });
        await db.SaveChangesAsync();
        await RunAsync(db, Probe.Forward);
        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.Set<EfHatchPlaybook>().CountAsync());
    }

    private static async Task<(EfHatchStatus Breakdown, EfHatchStatus Backlog)> AddColumnsAsync(HatchContext db)
    {
        var breakdown = new EfHatchStatus { Name = "Breakdown", SortOrder = 20 };
        var backlog = new EfHatchStatus { Name = "Backlog", SortOrder = 30 };
        db.AddRange(breakdown, backlog);
        await db.SaveChangesAsync();
        return (breakdown, backlog);
    }

    private static EfHatchPlaybook Row(
        EfHatchStatus from, EfHatchStatus to, string types, bool shapesAmbition, string shape = EfHatchPlaybook.AnyShape) => new()
    {
        FromStatusId = from.Id,
        ToStatusId = to.Id,
        Types = types,
        Shape = shape,
        Prompt = "Our own wording.",
        Model = "opus",
        Effort = "high",
        ShapesAmbition = shapesAmbition,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static async Task RunAsync(HatchContext db, IReadOnlyList<MigrationOperation> operations)
    {
        foreach (var sql in Sql(operations).Split("\0"))
            await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static string Sql(IReadOnlyList<MigrationOperation> operations) =>
        string.Join("\0", operations.OfType<SqlOperation>().Select(o => o.Sql));

    private static SeedAmbitionShapingPlaybookProbe Probe => new();

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class SeedAmbitionShapingPlaybookProbe : SeedAmbitionShapingPlaybook
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

    private sealed class SeedAmbitionPassPlaybookProbe : SeedAmbitionPassPlaybook
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

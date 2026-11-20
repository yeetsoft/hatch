using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The stock question-answering row is seeded once, flagged, from the
/// implementation column to itself, where no flagged row already covers an
/// ambition, and takes nothing back that somebody has written. The ambition
/// pass row on the same edge does not stand in its way.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class SeedQuestionAnsweringPlaybookTests
{
    /// <summary>
    /// This test's own copy of the stock text, on purpose. A migration is frozen
    /// text: an edit to it that forgets the row's guard would fail a rollback,
    /// and an edit here that forgets the migration fails this instead.
    /// </summary>
    private const string Expected = """
        You are answering one question asked on an issue under an ambition. The prompt names
        the question. You judge and you answer. You never edit the tree, and your tools refuse
        if you try.

        - Read the ambition's description, its answered questions with
          `hatch api GET /api/issues/<key>/questions?open=false`, the issue the question is on,
          and the repository at the trunk.
        - Answer only when one of those settles the question. A guess is not an answer. A
          choice that is the operator's stays the operator's.
        - Say where you found it: the file, the docs page, the principle, or the earlier answer.
        - The first line of the answer is the decision, in an option's label where one fits. The
          lines after it say where it was found.
        - If none of them settles the question, leave it open. Comment on its issue that the
          context does not settle it, and say what you read. Then stop.
        - Never answer a question at `hold`.
        - Move nothing. Never change an issue's column or its dates.
        - Name no board, repository, domain or person: nothing said here may be true of only
          the installation this happens to run on.
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
        Assert.Equal(Expected, SeedQuestionAnsweringPlaybook.Stock);
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
    public void It_seeds_the_default_prompt_and_leaves_the_operators_prompt_null()
    {
        var sql = Sql(Probe.Forward);

        Assert.Contains("\"Prompt\", \"DefaultPrompt\"", sql);
        Assert.Contains($"'any', NULL, $prompt${Expected}$prompt$", sql);
    }

    [Fact]
    public void It_seeds_an_ambition_row_with_fable_and_max_on_the_implementation_column_by_name()
    {
        var sql = Sql(Probe.Forward);

        Assert.Contains("s.\"Id\", s.\"Id\", 'ambition', 'any'", sql);
        Assert.Contains("'fable', 'max'", sql);
        Assert.Contains("\"DeniedTools\", \"AnswersQuestions\", \"CreatedAt\"", sql);
        Assert.Contains("::jsonb,\n       true, now(), now()", sql.Replace("\r", ""));
        Assert.Contains("s.\"Name\" = 'In Progress'", sql);
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
    public void The_denied_tools_are_the_reflection_rows_so_the_two_lists_cannot_drift()
    {
        var reflection = string.Join("\n", new SeedReflectionPlaybookProbe().Forward.OfType<SqlOperation>().Select(o => o.Sql));

        Assert.Contains($"'{ExpectedDeniedTools}'::jsonb", reflection);
        Assert.Contains($"'{ExpectedDeniedTools}'::jsonb", Sql(Probe.Forward));
    }

    [Fact]
    public void It_seeds_only_where_no_row_already_covers_an_ambition()
    {
        var sql = Sql(Probe.Forward);

        Assert.Contains("AND NOT EXISTS", sql);
        Assert.Contains("AND p.\"AnswersQuestions\"\n", sql.Replace("\r", ""));
        Assert.Contains("p.\"FromStatusId\" = s.\"Id\" AND p.\"ToStatusId\" = s.\"Id\"", sql);
        Assert.Contains("(p.\"Types\" = '' OR 'ambition' = ANY(string_to_array(p.\"Types\", ',')))", sql);
    }

    [Fact]
    public void Down_removes_only_a_row_still_saying_the_stock_text_with_no_prompt_of_its_own()
    {
        var sql = Sql(Probe.Backward);

        Assert.Contains("s.\"Name\" = 'In Progress'", sql);
        Assert.Contains("p.\"FromStatusId\" = s.\"Id\" AND p.\"ToStatusId\" = s.\"Id\"", sql);
        Assert.Contains("p.\"Types\" = 'ambition'", sql);
        Assert.Contains("p.\"AnswersQuestions\"", sql);
        Assert.Contains("p.\"Prompt\" IS NULL", sql);
        Assert.Contains($"p.\"DefaultPrompt\" = $prompt${Expected}$prompt$", sql);
    }

    [SkippableFact]
    public async Task On_a_real_database_a_question_answering_row_is_seeded_once_and_an_edited_one_is_left_alone()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var inProgress = new EfHatchStatus { Name = "In Progress", SortOrder = 30 };
        db.Add(inProgress);
        await db.SaveChangesAsync();

        await RunAsync(db, Probe.Forward);

        db.ChangeTracker.Clear();
        var seeded = await db.Set<EfHatchPlaybook>().SingleAsync();
        Assert.Equal(inProgress.Id, seeded.FromStatusId);
        Assert.Equal(inProgress.Id, seeded.ToStatusId);
        Assert.Equal("ambition", seeded.Types);
        Assert.Equal("any", seeded.Shape);
        Assert.Null(seeded.Prompt);
        Assert.Equal(Expected, seeded.DefaultPrompt);
        Assert.Equal("fable", seeded.Model);
        Assert.Equal("max", seeded.Effort);
        Assert.False(seeded.IsCheck);
        Assert.True(seeded.AnswersQuestions);
        Assert.Equal(ExpectedRules, EfHatchPlaybook.ReadDeniedTools(seeded.DeniedTools));

        // Run again: the guard leaves it alone - no duplicate.
        await RunAsync(db, Probe.Forward);
        Assert.Equal(1, await db.Set<EfHatchPlaybook>().CountAsync());

        // Down leaves a row with a prompt of its own.
        db.ChangeTracker.Clear();
        var edited = await db.Set<EfHatchPlaybook>().SingleAsync();
        edited.Prompt = "Our own answering wording.";
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

        var inProgress = new EfHatchStatus { Name = "In Progress", SortOrder = 30 };
        db.Add(inProgress);
        await db.SaveChangesAsync();
        db.Add(Row(inProgress, types));
        await db.SaveChangesAsync();

        await RunAsync(db, Probe.Forward);

        db.ChangeTracker.Clear();
        var only = await db.Set<EfHatchPlaybook>().SingleAsync();
        Assert.Equal(types, only.Types);
        Assert.Equal("Our own wording.", only.Prompt);
    }

    [SkippableFact]
    public async Task On_a_real_database_the_ambition_pass_row_on_the_same_edge_does_not_block_the_seed()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var inProgress = new EfHatchStatus { Name = "In Progress", SortOrder = 30 };
        db.Add(inProgress);
        await db.SaveChangesAsync();
        db.Add(Row(inProgress, "ambition", answersQuestions: false));
        await db.SaveChangesAsync();

        await RunAsync(db, Probe.Forward);

        db.ChangeTracker.Clear();
        var rows = await db.Set<EfHatchPlaybook>().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, p => !p.AnswersQuestions && p.Prompt == "Our own wording.");
        Assert.Single(rows, p => p.AnswersQuestions && p.DefaultPrompt == Expected);
    }

    [SkippableFact]
    public async Task On_a_real_database_a_row_for_another_type_on_that_edge_does_not_block_the_seed()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var inProgress = new EfHatchStatus { Name = "In Progress", SortOrder = 30 };
        db.Add(inProgress);
        await db.SaveChangesAsync();
        db.Add(Row(inProgress, "story"));
        await db.SaveChangesAsync();

        await RunAsync(db, Probe.Forward);

        db.ChangeTracker.Clear();
        var rows = await db.Set<EfHatchPlaybook>().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, p => p.Types == "story" && p.Prompt == "Our own wording.");
        Assert.Single(rows, p => p.Types == "ambition" && p.DefaultPrompt == Expected);
    }

    private static EfHatchPlaybook Row(EfHatchStatus status, string types, bool answersQuestions = true) => new()
    {
        FromStatusId = status.Id,
        ToStatusId = status.Id,
        Types = types,
        Shape = EfHatchPlaybook.AnyShape,
        Prompt = "Our own wording.",
        Model = "opus",
        Effort = "high",
        AnswersQuestions = answersQuestions,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static async Task RunAsync(HatchContext db, IReadOnlyList<MigrationOperation> operations)
    {
        foreach (var sql in Sql(operations).Split("\u0000"))
            await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static string Sql(IReadOnlyList<MigrationOperation> operations) =>
        string.Join("\u0000", operations.OfType<SqlOperation>().Select(o => o.Sql));

    private static SeedQuestionAnsweringPlaybookProbe Probe => new();

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class SeedQuestionAnsweringPlaybookProbe : SeedQuestionAnsweringPlaybook
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

    private sealed class SeedReflectionPlaybookProbe : SeedReflectionPlaybook
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

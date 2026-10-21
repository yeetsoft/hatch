using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The stock reflection playbook is seeded once, on Breakdown to Backlog, where
/// nothing already covers a reflection, and takes nothing back that somebody
/// has written.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class SeedReflectionPlaybookTests
{
    /// <summary>
    /// This test's own copy of the stock text, on purpose. A migration is frozen
    /// text: an edit to it that forgets the row's guard would fail a rollback,
    /// and an edit here that forgets the migration fails this instead.
    /// </summary>
    private const string Expected = """
        You are reflecting on how this board has been run, not on any one ticket on it.

        - Read this ticket's own comments and events first. A previous run may have been
          aborted partway; continue it rather than starting again, and if it ran to
          completion and only the status was left behind, say so and move it on.
        - Read the window this ticket's own brief names - the thirty days before it was
          filed, where it names none. Read it through the folds, not the rows: spend by
          playbook, flow by column, the timeline, the queue, utilization, the activity
          trail, and which playbooks spoke in it.
        - Rewrite this ticket's description as the report, in this shape and no other:
          Window; What ran and what it cost; Where the time went; Findings, each with
          its evidence; Recommendations, each either filed as an issue under this
          reflection or written as an instruction for a person; Hypotheses, each with a
          metric, a baseline, the change expected and a date to read it after; What
          reflection could not see.
        - File each recommendation that is a change to make as an issue under this
          reflection, in the project it reflects on - an epic, a story, a task or a bug,
          whichever its size calls for. Propose code changes only where that project's
          own repository is this one, told by the tree this session stands in rather
          than by name; where it is not, write the finding as an instruction for a
          person instead. File each hypothesis the same way, with a readyAt set to its
          read-after date, so it waits until then.
        - Where the project reflected on binds no repository at all, say so under "What
          reflection could not see" as an instrumentation request, rather than filing
          work against code nobody can reach.
        - Name no board, repository, domain or person: nothing said here may be true of
          only the installation this happens to run on.
        - You judge and you file; you never edit the tree. Move this ticket to the
          column named at the end of this prompt once the report is written and its
          issues are filed.
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
        Assert.Equal(Expected, SeedReflectionPlaybook.Stock);
        Assert.Contains($"$prompt${Expected}$prompt$", Sql(Probe.Forward));
        Assert.True(Expected.Length <= EfHatchPlaybook.MaxPromptLength);
    }

    [Fact]
    public void It_seeds_the_default_prompt_and_leaves_the_operators_prompt_null()
    {
        var sql = Sql(Probe.Forward);

        Assert.Contains("\"Prompt\", \"DefaultPrompt\"", sql);
        Assert.Contains($"'any', NULL, $prompt${Expected}$prompt$", sql);
    }

    [Fact]
    public void It_seeds_a_reflection_row_with_fable_and_max_on_breakdown_to_backlog_by_name()
    {
        var sql = Sql(Probe.Forward);

        Assert.Contains("'reflection', 'any'", sql);
        Assert.Contains("'fable', 'max'", sql);
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
    public void It_seeds_only_where_no_row_already_covers_a_reflection()
    {
        var sql = Sql(Probe.Forward);

        Assert.Contains("AND NOT EXISTS", sql);
        Assert.Contains("p.\"FromStatusId\" = f.\"Id\" AND p.\"ToStatusId\" = t.\"Id\"", sql);
        Assert.Contains("(p.\"Types\" = '' OR 'reflection' = ANY(string_to_array(p.\"Types\", ',')))", sql);
    }

    [Fact]
    public void Down_removes_only_a_row_still_saying_the_stock_text()
    {
        var sql = Sql(Probe.Backward);

        Assert.Contains("f.\"Name\" = 'Breakdown' AND t.\"Name\" = 'Backlog'", sql);
        Assert.Contains("p.\"Types\" = 'reflection'", sql);
        Assert.Contains($"p.\"DefaultPrompt\" = $prompt${Expected}$prompt$", sql);
    }

    [SkippableFact]
    public async Task On_a_real_database_a_reflection_playbook_is_seeded_once_and_an_edited_or_deleted_one_is_left_alone()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var breakdown = new EfHatchStatus { Name = "Breakdown", SortOrder = 20 };
        var backlog = new EfHatchStatus { Name = "Backlog", SortOrder = 30 };
        db.AddRange(breakdown, backlog);
        await db.SaveChangesAsync();

        await RunAsync(db, Probe.Forward);

        db.ChangeTracker.Clear();
        var seeded = await db.Set<EfHatchPlaybook>().SingleAsync(p => p.FromStatusId == breakdown.Id && p.ToStatusId == backlog.Id);
        Assert.Equal(Expected, seeded.DefaultPrompt);
        Assert.Null(seeded.Prompt);
        Assert.Equal("reflection", seeded.Types);
        Assert.Equal("any", seeded.Shape);
        Assert.Equal("fable", seeded.Model);
        Assert.Equal("max", seeded.Effort);
        Assert.False(seeded.IsCheck);
        Assert.Equal(ExpectedRules, EfHatchPlaybook.ReadDeniedTools(seeded.DeniedTools));

        // Run again: the guard leaves it alone - no duplicate.
        await RunAsync(db, Probe.Forward);
        Assert.Equal(1, await db.Set<EfHatchPlaybook>().CountAsync());

        // An operator's own wording is not overwritten by a re-run.
        db.ChangeTracker.Clear();
        var edited = await db.Set<EfHatchPlaybook>().SingleAsync();
        edited.Prompt = "Our own reflection wording.";
        await db.SaveChangesAsync();
        await RunAsync(db, Probe.Forward);
        db.ChangeTracker.Clear();
        var after = await db.Set<EfHatchPlaybook>().SingleAsync();
        Assert.Equal("Our own reflection wording.", after.Prompt);
        Assert.Equal(Expected, after.DefaultPrompt);

        // Down leaves a row whose stock text has been edited: that is
        // somebody's wording.
        after.DefaultPrompt = "Edited.";
        await db.SaveChangesAsync();
        await RunAsync(db, Probe.Backward);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Set<EfHatchPlaybook>().CountAsync());

        // And removes one that still says it.
        var restored = await db.Set<EfHatchPlaybook>().SingleAsync();
        restored.DefaultPrompt = Expected;
        await db.SaveChangesAsync();
        await RunAsync(db, Probe.Backward);
        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.Set<EfHatchPlaybook>().CountAsync());

        // A migration never replays once EF has recorded it, so a row an
        // operator deletes by hand is simply gone: this guard is about the
        // first run, not a memory of it.
    }

    [SkippableFact]
    public async Task On_a_real_database_a_row_already_covering_every_type_means_nothing_is_seeded()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var breakdown = new EfHatchStatus { Name = "Breakdown", SortOrder = 20 };
        var backlog = new EfHatchStatus { Name = "Backlog", SortOrder = 30 };
        db.AddRange(breakdown, backlog);
        await db.SaveChangesAsync();
        db.Add(new EfHatchPlaybook
        {
            FromStatusId = breakdown.Id,
            ToStatusId = backlog.Id,
            Types = "",
            Shape = EfHatchPlaybook.AnyShape,
            Prompt = "Break it down.",
            Model = "opus",
            Effort = "high",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await RunAsync(db, Probe.Forward);

        db.ChangeTracker.Clear();
        var only = await db.Set<EfHatchPlaybook>().SingleAsync();
        Assert.Equal("", only.Types);
        Assert.Equal("Break it down.", only.Prompt);
    }

    private static async Task RunAsync(HatchContext db, IReadOnlyList<MigrationOperation> operations)
    {
        foreach (var sql in Sql(operations).Split("\u0000"))
            await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static string Sql(IReadOnlyList<MigrationOperation> operations) =>
        string.Join("\u0000", operations.OfType<SqlOperation>().Select(o => o.Sql));

    private static SeedReflectionPlaybookProbe Probe => new();

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class SeedReflectionPlaybookProbe : SeedReflectionPlaybook
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

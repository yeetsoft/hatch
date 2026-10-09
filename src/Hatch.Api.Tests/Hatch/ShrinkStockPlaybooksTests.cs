using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The story-breakdown and analysis playbooks shrink further, and the
/// implementation playbook learns to verify a ticket whose tasks are all
/// terminal rather than implement it again - each only where nobody has
/// edited the row.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ShrinkStockPlaybooksTests
{
    [Fact]
    public void The_story_criteria_bullet_is_capped_at_five_with_a_retype_to_epic_clause()
    {
        var flat = Flat(ShrinkStockPlaybooks.StoryReplaced);

        Assert.Contains("at most five", flat);
        Assert.Contains("retype it", flat);
        Assert.Contains("onto the tasks, not into the description", flat);
        Assert.True(ShrinkStockPlaybooks.StoryReplaced.Length <= EfHatchPlaybook.MaxPromptLength);
    }

    [Fact]
    public void The_analysis_plan_bullet_splits_into_one_seam_to_a_task_and_a_brief_bullet()
    {
        var flat = Flat(ShrinkStockPlaybooks.AnalysisReplaced);

        Assert.Contains("one seam to a task", flat);
        Assert.Contains("the column this ticket is leaving", flat);
        Assert.Contains("a brief", flat);
        Assert.True(ShrinkStockPlaybooks.AnalysisReplaced.Length <= EfHatchPlaybook.MaxPromptLength);
    }

    [Fact]
    public void The_implementation_prompt_gains_a_verify_or_implement_bullet()
    {
        var flat = Flat(ShrinkStockPlaybooks.ImplementationReplaced);

        Assert.Contains("already done, task by task", flat);
        Assert.Contains("Do not implement it again", flat);
        Assert.True(ShrinkStockPlaybooks.ImplementationReplaced.Length <= EfHatchPlaybook.MaxPromptLength);
    }

    /// <summary>The prompt as one line, so a phrase is found wherever the text wraps it.</summary>
    private static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

    [Fact]
    public void Each_replaced_text_is_not_the_old_one()
    {
        Assert.NotEqual(ShrinkStockPlaybooks.StoryStock, ShrinkStockPlaybooks.StoryReplaced);
        Assert.NotEqual(ShrinkStockPlaybooks.AnalysisStock, ShrinkStockPlaybooks.AnalysisReplaced);
        Assert.NotEqual(ShrinkStockPlaybooks.ImplementationStock, ShrinkStockPlaybooks.ImplementationReplaced);
    }

    [Fact]
    public void The_guard_is_the_whole_old_prompt_on_each_row_and_nothing_looser()
    {
        var sql = Sql(new Probe().Forward);

        Assert.Contains($"$old${ShrinkStockPlaybooks.StoryStock}$old$", sql);
        Assert.Contains($"$new${ShrinkStockPlaybooks.StoryReplaced}$new$", sql);
        Assert.Contains($"$old${ShrinkStockPlaybooks.AnalysisStock}$old$", sql);
        Assert.Contains($"$new${ShrinkStockPlaybooks.AnalysisReplaced}$new$", sql);
        Assert.Contains($"$old${ShrinkStockPlaybooks.ImplementationStock}$old$", sql);
        Assert.Contains($"$new${ShrinkStockPlaybooks.ImplementationReplaced}$new$", sql);

        // Prompt alone: no status, Types or Shape condition, and no LIKE loosening the match.
        Assert.DoesNotContain("FromStatusId", sql);
        Assert.DoesNotContain("ToStatusId", sql);
        Assert.DoesNotContain("\"Types\"", sql);
        Assert.DoesNotContain("LIKE", sql);
    }

    [Fact]
    public void Down_restores_only_a_row_still_saying_the_new_text()
    {
        var sql = Sql(new Probe().Backward);

        Assert.Contains($"SET \"Prompt\" = $old${ShrinkStockPlaybooks.StoryStock}$old$", sql);
        Assert.Contains($"WHERE \"Prompt\" = $new${ShrinkStockPlaybooks.StoryReplaced}$new$", sql);
        Assert.Contains($"SET \"Prompt\" = $old${ShrinkStockPlaybooks.AnalysisStock}$old$", sql);
        Assert.Contains($"WHERE \"Prompt\" = $new${ShrinkStockPlaybooks.AnalysisReplaced}$new$", sql);
        Assert.Contains($"SET \"Prompt\" = $old${ShrinkStockPlaybooks.ImplementationStock}$old$", sql);
        Assert.Contains($"WHERE \"Prompt\" = $new${ShrinkStockPlaybooks.ImplementationReplaced}$new$", sql);
    }

    [SkippableFact]
    public async Task An_unedited_row_is_reworded_regardless_of_narrowed_types_an_edited_one_is_left_alone_and_down_restores_only_the_unedited()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var todo = new EfHatchStatus { Name = "To Do", SortOrder = 20 };
        var inProgress = new EfHatchStatus { Name = "In Progress", SortOrder = 30 };
        db.AddRange(todo, inProgress);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        EfHatchPlaybook Row(string types, string prompt, string shape = "any") => new()
        {
            FromStatusId = todo.Id, ToStatusId = inProgress.Id, Types = types, Shape = shape, Prompt = prompt,
            Model = "sonnet", Effort = "high", CreatedAt = now, UpdatedAt = now,
        };

        // Unedited, with its Types narrowed the way the live board already has it for
        // each row: scoping is not an edit of the text. The edited sibling is a
        // distinct row - the unique index on (FromStatusId, ToStatusId, Types, Shape)
        // would not allow two rows to share every one of those columns - so it is
        // given a different Shape where Types alone does not already set it apart.
        var storyUnedited = Row("story,bug", ShrinkStockPlaybooks.StoryStock);
        var storyEdited = Row("story,bug", ShrinkStockPlaybooks.StoryStock + "\n- Also do this.", shape: "leaf");
        var analysisUnedited = Row("", ShrinkStockPlaybooks.AnalysisStock);
        var analysisEdited = Row("", ShrinkStockPlaybooks.AnalysisStock + "\n- Also do this.", shape: "leaf");
        var implementationUnedited = Row("story,task,bug", ShrinkStockPlaybooks.ImplementationStock);
        var implementationEdited = Row("story,task,bug", ShrinkStockPlaybooks.ImplementationStock + "\n- Also do this.", shape: "leaf");
        db.AddRange(storyUnedited, storyEdited, analysisUnedited, analysisEdited, implementationUnedited, implementationEdited);
        await db.SaveChangesAsync();

        foreach (var sql in Sql(new Probe().Forward).Split("\0"))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        Assert.Equal(ShrinkStockPlaybooks.StoryReplaced, (await db.Set<EfHatchPlaybook>().FindAsync(storyUnedited.Id))!.Prompt);
        Assert.Equal(ShrinkStockPlaybooks.StoryStock + "\n- Also do this.", (await db.Set<EfHatchPlaybook>().FindAsync(storyEdited.Id))!.Prompt);
        Assert.Equal(ShrinkStockPlaybooks.AnalysisReplaced, (await db.Set<EfHatchPlaybook>().FindAsync(analysisUnedited.Id))!.Prompt);
        Assert.Equal(ShrinkStockPlaybooks.AnalysisStock + "\n- Also do this.", (await db.Set<EfHatchPlaybook>().FindAsync(analysisEdited.Id))!.Prompt);
        Assert.Equal(ShrinkStockPlaybooks.ImplementationReplaced, (await db.Set<EfHatchPlaybook>().FindAsync(implementationUnedited.Id))!.Prompt);
        Assert.Equal(ShrinkStockPlaybooks.ImplementationStock + "\n- Also do this.", (await db.Set<EfHatchPlaybook>().FindAsync(implementationEdited.Id))!.Prompt);

        foreach (var sql in Sql(new Probe().Backward).Split("\0"))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        Assert.Equal(ShrinkStockPlaybooks.StoryStock, (await db.Set<EfHatchPlaybook>().FindAsync(storyUnedited.Id))!.Prompt);
        Assert.Equal(ShrinkStockPlaybooks.StoryStock + "\n- Also do this.", (await db.Set<EfHatchPlaybook>().FindAsync(storyEdited.Id))!.Prompt);
        Assert.Equal(ShrinkStockPlaybooks.AnalysisStock, (await db.Set<EfHatchPlaybook>().FindAsync(analysisUnedited.Id))!.Prompt);
        Assert.Equal(ShrinkStockPlaybooks.AnalysisStock + "\n- Also do this.", (await db.Set<EfHatchPlaybook>().FindAsync(analysisEdited.Id))!.Prompt);
        Assert.Equal(ShrinkStockPlaybooks.ImplementationStock, (await db.Set<EfHatchPlaybook>().FindAsync(implementationUnedited.Id))!.Prompt);
        Assert.Equal(ShrinkStockPlaybooks.ImplementationStock + "\n- Also do this.", (await db.Set<EfHatchPlaybook>().FindAsync(implementationEdited.Id))!.Prompt);
    }

    /// <summary>The SQL an operation list carries, joined with a NUL where there is more than one.</summary>
    private static string Sql(IReadOnlyList<MigrationOperation> operations) =>
        string.Join("\0", operations.OfType<SqlOperation>().Select(o => o.Sql));

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class Probe : ShrinkStockPlaybooks
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

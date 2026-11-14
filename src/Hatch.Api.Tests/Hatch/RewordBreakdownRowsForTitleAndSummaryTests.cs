using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The three breakdown rows - epic, story and task - each gain a title-and-
/// summary rule in the place their existing bullet already lived. Like
/// <c>RewordFinalAnalysisPlaybook</c> and <c>RewordEpicBreakdownForDreams</c>
/// each row guards and writes <c>DefaultPrompt</c> alone - no row's own
/// <c>Prompt</c> is read or written, and <c>UpdatedAt</c> never moves.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class RewordBreakdownRowsForTitleAndSummaryTests
{
    [Fact]
    public void The_epic_stock_text_is_the_live_dream_board_and_prefix_rewritten_text_not_the_seed_constant()
    {
        Assert.NotEqual(SeedPlaybookDefaultPrompt.EpicBreakdown, RewordBreakdownRowsForTitleAndSummary.EpicStock);
        Assert.Equal(SeedPlaybookDefaultPrompt.StoryBreakdown, RewordBreakdownRowsForTitleAndSummary.StoryStock);
        Assert.Equal(SeedPlaybookDefaultPrompt.TaskBreakdown, RewordBreakdownRowsForTitleAndSummary.TaskStock);
    }

    [Fact]
    public void The_epic_bullet_is_reworded_for_title_and_summary_and_nothing_else_changes()
    {
        Assert.NotEqual(RewordBreakdownRowsForTitleAndSummary.EpicStock, RewordBreakdownRowsForTitleAndSummary.EpicReplaced);
        Assert.Contains(RewordBreakdownRowsForTitleAndSummary.OldEpicBullet, RewordBreakdownRowsForTitleAndSummary.EpicStock);
        Assert.DoesNotContain(RewordBreakdownRowsForTitleAndSummary.OldEpicBullet, RewordBreakdownRowsForTitleAndSummary.EpicReplaced);
        Assert.Contains(RewordBreakdownRowsForTitleAndSummary.NewEpicBullet, RewordBreakdownRowsForTitleAndSummary.EpicReplaced);
        Assert.Contains("a title of a handful of words", RewordBreakdownRowsForTitleAndSummary.EpicReplaced);
        Assert.Contains("issues.md", RewordBreakdownRowsForTitleAndSummary.EpicReplaced);
        Assert.True(RewordBreakdownRowsForTitleAndSummary.EpicReplaced.Length <= EfHatchPlaybook.MaxPromptLength);

        Assert.Equal(
            RewordBreakdownRowsForTitleAndSummary.EpicStock.Replace(RewordBreakdownRowsForTitleAndSummary.OldEpicBullet, "", StringComparison.Ordinal),
            RewordBreakdownRowsForTitleAndSummary.EpicReplaced.Replace(RewordBreakdownRowsForTitleAndSummary.NewEpicBullet, "", StringComparison.Ordinal));
    }

    [Fact]
    public void The_story_bullet_is_reworded_for_title_and_summary_and_nothing_else_changes()
    {
        Assert.NotEqual(RewordBreakdownRowsForTitleAndSummary.StoryStock, RewordBreakdownRowsForTitleAndSummary.StoryReplaced);
        Assert.Contains(RewordBreakdownRowsForTitleAndSummary.OldStoryBullet, RewordBreakdownRowsForTitleAndSummary.StoryStock);
        Assert.DoesNotContain(RewordBreakdownRowsForTitleAndSummary.OldStoryBullet, RewordBreakdownRowsForTitleAndSummary.StoryReplaced);
        Assert.Contains(RewordBreakdownRowsForTitleAndSummary.NewStoryBullet, RewordBreakdownRowsForTitleAndSummary.StoryReplaced);
        Assert.Contains("the first line of", RewordBreakdownRowsForTitleAndSummary.StoryReplaced);
        Assert.Contains("issues.md", RewordBreakdownRowsForTitleAndSummary.StoryReplaced);
        Assert.True(RewordBreakdownRowsForTitleAndSummary.StoryReplaced.Length <= EfHatchPlaybook.MaxPromptLength);

        Assert.Equal(
            RewordBreakdownRowsForTitleAndSummary.StoryStock.Replace(RewordBreakdownRowsForTitleAndSummary.OldStoryBullet, "", StringComparison.Ordinal),
            RewordBreakdownRowsForTitleAndSummary.StoryReplaced.Replace(RewordBreakdownRowsForTitleAndSummary.NewStoryBullet, "", StringComparison.Ordinal));
    }

    [Fact]
    public void The_task_bullet_is_reworded_for_title_and_summary_and_nothing_else_changes()
    {
        Assert.NotEqual(RewordBreakdownRowsForTitleAndSummary.TaskStock, RewordBreakdownRowsForTitleAndSummary.TaskReplaced);
        Assert.Contains(RewordBreakdownRowsForTitleAndSummary.OldTaskBullet, RewordBreakdownRowsForTitleAndSummary.TaskStock);
        Assert.DoesNotContain(RewordBreakdownRowsForTitleAndSummary.OldTaskBullet, RewordBreakdownRowsForTitleAndSummary.TaskReplaced);
        Assert.Contains(RewordBreakdownRowsForTitleAndSummary.NewTaskBullet, RewordBreakdownRowsForTitleAndSummary.TaskReplaced);
        Assert.Contains("names the change in one short line", RewordBreakdownRowsForTitleAndSummary.TaskReplaced);
        Assert.Contains("issues.md", RewordBreakdownRowsForTitleAndSummary.TaskReplaced);
        Assert.True(RewordBreakdownRowsForTitleAndSummary.TaskReplaced.Length <= EfHatchPlaybook.MaxPromptLength);

        Assert.Equal(
            RewordBreakdownRowsForTitleAndSummary.TaskStock.Replace(RewordBreakdownRowsForTitleAndSummary.OldTaskBullet, "", StringComparison.Ordinal),
            RewordBreakdownRowsForTitleAndSummary.TaskReplaced.Replace(RewordBreakdownRowsForTitleAndSummary.NewTaskBullet, "", StringComparison.Ordinal));
    }

    [Fact]
    public void Up_guards_and_writes_DefaultPrompt_alone_on_all_three_rows()
    {
        var sql = Sql(new Probe().Forward);

        Assert.Contains($"WHERE \"DefaultPrompt\" = $old${RewordBreakdownRowsForTitleAndSummary.EpicStock}$old$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $new${RewordBreakdownRowsForTitleAndSummary.EpicReplaced}$new$", sql);
        Assert.Contains($"WHERE \"DefaultPrompt\" = $old${RewordBreakdownRowsForTitleAndSummary.StoryStock}$old$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $new${RewordBreakdownRowsForTitleAndSummary.StoryReplaced}$new$", sql);
        Assert.Contains($"WHERE \"DefaultPrompt\" = $old${RewordBreakdownRowsForTitleAndSummary.TaskStock}$old$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $new${RewordBreakdownRowsForTitleAndSummary.TaskReplaced}$new$", sql);

        Assert.DoesNotContain("\"Prompt\"", sql);
        Assert.DoesNotContain("UpdatedAt", sql);
        Assert.DoesNotContain("FromStatusId", sql);
        Assert.DoesNotContain("ToStatusId", sql);
        Assert.DoesNotContain("\"Types\"", sql);
        Assert.DoesNotContain("\"Shape\"", sql);
    }

    [Fact]
    public void Down_is_Up_mirrored_on_all_three_rows()
    {
        var sql = Sql(new Probe().Backward);

        Assert.Contains($"WHERE \"DefaultPrompt\" = $new${RewordBreakdownRowsForTitleAndSummary.EpicReplaced}$new$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $old${RewordBreakdownRowsForTitleAndSummary.EpicStock}$old$", sql);
        Assert.Contains($"WHERE \"DefaultPrompt\" = $new${RewordBreakdownRowsForTitleAndSummary.StoryReplaced}$new$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $old${RewordBreakdownRowsForTitleAndSummary.StoryStock}$old$", sql);
        Assert.Contains($"WHERE \"DefaultPrompt\" = $new${RewordBreakdownRowsForTitleAndSummary.TaskReplaced}$new$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $old${RewordBreakdownRowsForTitleAndSummary.TaskStock}$old$", sql);

        Assert.DoesNotContain("\"Prompt\"", sql);
        Assert.DoesNotContain("UpdatedAt", sql);
    }

    private static readonly DateTimeOffset Seeded = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string EditedPrompt = "Do it your own way.";

    private const string UnrelatedDefault = "Something else entirely.";

    private static int _slot;

    private static (string Stock, string Replaced) RowTexts(string row) => row switch
    {
        "epic" => (RewordBreakdownRowsForTitleAndSummary.EpicStock, RewordBreakdownRowsForTitleAndSummary.EpicReplaced),
        "story" => (RewordBreakdownRowsForTitleAndSummary.StoryStock, RewordBreakdownRowsForTitleAndSummary.StoryReplaced),
        "task" => (RewordBreakdownRowsForTitleAndSummary.TaskStock, RewordBreakdownRowsForTitleAndSummary.TaskReplaced),
        _ => throw new ArgumentOutOfRangeException(nameof(row)),
    };

    [SkippableTheory]
    [InlineData("epic")]
    [InlineData("story")]
    [InlineData("task")]
    public async Task An_unedited_row_learns_the_new_default_and_keeps_Prompt_and_UpdatedAt_untouched(string row)
    {
        var (stock, replaced) = RowTexts(row);
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: null, defaultPrompt: stock);

        await RunAsync(db, new Probe().Forward);

        var saved = await ReadAsync(db, id);
        Assert.Null(saved.Prompt);
        Assert.Equal(replaced, saved.DefaultPrompt);
        Assert.Equal(Seeded, saved.UpdatedAt);
    }

    [SkippableTheory]
    [InlineData("epic")]
    [InlineData("story")]
    [InlineData("task")]
    public async Task A_row_with_its_own_Prompt_keeps_it_byte_for_byte_and_still_learns_the_new_default(string row)
    {
        var (stock, replaced) = RowTexts(row);
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: EditedPrompt, defaultPrompt: stock);

        await RunAsync(db, new Probe().Forward);

        var saved = await ReadAsync(db, id);
        Assert.Equal(EditedPrompt, saved.Prompt);
        Assert.Equal(replaced, saved.DefaultPrompt);
        Assert.Equal(Seeded, saved.UpdatedAt);
    }

    [SkippableFact]
    public async Task A_row_whose_DefaultPrompt_does_not_match_any_stock_text_is_left_alone()
    {
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: null, defaultPrompt: UnrelatedDefault);

        await RunAsync(db, new Probe().Forward);

        var saved = await ReadAsync(db, id);
        Assert.Equal(UnrelatedDefault, saved.DefaultPrompt);
        Assert.Equal(Seeded, saved.UpdatedAt);
    }

    private static async Task<HatchContext> OpenAsync()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        return new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);
    }

    /// <summary>
    /// A playbook in a slot of its own. The unique index is on the from, to, types and
    /// shape, so each row gets a status of its own to move to.
    /// </summary>
    private static async Task<int> AddRowAsync(HatchContext db, string? prompt, string? defaultPrompt)
    {
        var slot = Interlocked.Increment(ref _slot);
        var from = await db.Set<EfHatchStatus>().FirstOrDefaultAsync(s => s.Name == "From");
        if (from is null)
        {
            from = new EfHatchStatus { Name = "From", SortOrder = 1 };
            db.Add(from);
        }

        var to = new EfHatchStatus { Name = $"Slot {slot}", SortOrder = 100 + slot };
        db.Add(to);
        await db.SaveChangesAsync();

        var row = new EfHatchPlaybook
        {
            FromStatusId = from.Id, ToStatusId = to.Id,
            Types = "", Shape = "leaf", Prompt = prompt, DefaultPrompt = defaultPrompt,
            Model = "sonnet", Effort = "high", CreatedAt = Seeded, UpdatedAt = Seeded,
        };
        db.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private static async Task RunAsync(HatchContext db, IReadOnlyList<MigrationOperation> operations)
    {
        foreach (var operation in operations.OfType<SqlOperation>())
            // The epic row's SQL carries a JSON literal, and ExecuteSqlRawAsync reads
            // braces as format placeholders; escape them so the text reaches the
            // database as written.
            await db.Database.ExecuteSqlRawAsync(operation.Sql.Replace("{", "{{").Replace("}", "}}"));
    }

    private static async Task<EfHatchPlaybook> ReadAsync(HatchContext db, int id)
    {
        db.ChangeTracker.Clear();
        return (await db.Set<EfHatchPlaybook>().FindAsync(id))!;
    }

    private static string Sql(IReadOnlyList<MigrationOperation> operations) =>
        string.Join("\0", operations.OfType<SqlOperation>().Select(o => o.Sql));

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class Probe : RewordBreakdownRowsForTitleAndSummary
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

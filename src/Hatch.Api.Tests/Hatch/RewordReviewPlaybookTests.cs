using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The stock review playbook now covers a failing build as well as a conflict -
/// and only where nobody has edited it.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class RewordReviewPlaybookTests
{
    [Fact]
    public void The_new_text_covers_both_a_conflict_and_a_failing_build()
    {
        var text = RewordReviewPlaybook.Stock;
        var flat = Flat(text);

        Assert.Contains("conflict section", flat);
        Assert.Contains("failing build section", flat);
        Assert.Contains("log excerpt", flat);
        Assert.Contains("some tests only run against a database", flat);
        Assert.Contains("flaky", flat);
        Assert.Contains("nothing else", flat);
        Assert.Contains("Build and test", flat);
        Assert.Contains("Commit and push to the same branch", flat);
        Assert.Contains("Never rebase, never force-push", flat);
        Assert.Contains("Leave the ticket in the column it is in.", flat);
        Assert.True(text.Length <= EfHatchPlaybook.MaxPromptLength);
    }

    /// <summary>A build dispatch has no merge in progress, so the text must not say there is one for every dispatch.</summary>
    [Fact]
    public void The_new_text_does_not_say_the_merge_is_in_progress_unconditionally()
    {
        var flat = Flat(RewordReviewPlaybook.Stock);

        Assert.DoesNotContain("started merging the trunk into it", flat);
        Assert.Contains("If there is a conflict section, the merge with the trunk is in progress", flat);
        Assert.Contains("the branch section says what state the tree is in", flat);
    }

    [Fact]
    public void The_new_text_is_not_the_old_one()
    {
        Assert.NotEqual(SeedConflictPlaybook.Stock, RewordReviewPlaybook.Stock);
    }

    [Fact]
    public void The_guard_is_the_whole_old_prompt_on_a_review_row_and_nothing_looser()
    {
        var sql = Sql(new Probe().Forward);

        Assert.Contains($"$new${RewordReviewPlaybook.Stock}$new$", sql);
        Assert.Contains($"\"FromStatusId\" = \"ToStatusId\" AND \"Prompt\" = $old${SeedConflictPlaybook.Stock}$old$", sql);
        Assert.DoesNotContain("LIKE", sql);
    }

    [Fact]
    public void Down_restores_only_a_row_still_saying_the_new_text()
    {
        var sql = Sql(new Probe().Backward);

        Assert.Contains($"SET \"Prompt\" = $old${SeedConflictPlaybook.Stock}$old$", sql);
        Assert.Contains($"\"FromStatusId\" = \"ToStatusId\" AND \"Prompt\" = $new${RewordReviewPlaybook.Stock}$new$", sql);
    }

    [SkippableFact]
    public async Task An_unedited_row_is_reworded_an_edited_one_is_left_alone_and_down_restores_only_the_unedited()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var progress = new EfHatchStatus { Name = "In Progress", SortOrder = 50 };
        var review = new EfHatchStatus { Name = "In Review", SortOrder = 60 };
        var done = new EfHatchStatus { Name = "Done", SortOrder = 70, IsTerminal = true };
        db.AddRange(progress, review, done);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        EfHatchPlaybook Row(int from, int to, string types, string prompt) => new()
        {
            FromStatusId = from, ToStatusId = to, Types = types, Shape = "any", Prompt = prompt,
            Model = "sonnet", Effort = "high", CreatedAt = now, UpdatedAt = now,
        };

        // Unedited, with its types narrowed: scoping is not an edit of the text.
        var unedited = Row(review.Id, review.Id, "story,task", SeedConflictPlaybook.Stock);
        var edited = Row(review.Id, review.Id, "epic", SeedConflictPlaybook.Stock + "\n- Also do this.");

        // The same words on a row that is not a review row are somebody's own.
        var elsewhere = Row(progress.Id, review.Id, "bug", SeedConflictPlaybook.Stock);
        db.AddRange(unedited, edited, elsewhere);
        await db.SaveChangesAsync();

        foreach (var sql in Sql(new Probe().Forward).Split("\u0000"))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        Assert.Equal(RewordReviewPlaybook.Stock, (await db.Set<EfHatchPlaybook>().FindAsync(unedited.Id))!.Prompt);
        Assert.Equal(SeedConflictPlaybook.Stock + "\n- Also do this.", (await db.Set<EfHatchPlaybook>().FindAsync(edited.Id))!.Prompt);
        Assert.Equal(SeedConflictPlaybook.Stock, (await db.Set<EfHatchPlaybook>().FindAsync(elsewhere.Id))!.Prompt);

        foreach (var sql in Sql(new Probe().Backward).Split("\u0000"))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        Assert.Equal(SeedConflictPlaybook.Stock, (await db.Set<EfHatchPlaybook>().FindAsync(unedited.Id))!.Prompt);
        Assert.Equal(SeedConflictPlaybook.Stock + "\n- Also do this.", (await db.Set<EfHatchPlaybook>().FindAsync(edited.Id))!.Prompt);
    }

    /// <summary>The prompt as one line, so a phrase is found wherever the text wraps it.</summary>
    private static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

    /// <summary>The SQL an operation list carries, joined with a NUL where there is more than one.</summary>
    private static string Sql(IReadOnlyList<MigrationOperation> operations) =>
        string.Join("\u0000", operations.OfType<SqlOperation>().Select(o => o.Sql));

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class Probe : RewordReviewPlaybook
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

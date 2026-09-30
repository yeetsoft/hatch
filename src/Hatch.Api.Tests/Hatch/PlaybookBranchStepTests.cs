using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The stock implementation playbook stops telling a session to cut its own
/// branch - and only where nobody has edited it.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class PlaybookBranchStepTests
{
    [Fact]
    public void The_old_bullet_is_in_the_stock_prompt_exactly_once_and_the_new_one_is_not()
    {
        Assert.Equal(1, Count(PlaybookBranchStep.Stock, PlaybookBranchStep.OldBullet));
        Assert.Equal(0, Count(PlaybookBranchStep.Stock, PlaybookBranchStep.NewBullet));
    }

    [Fact]
    public void Replacing_it_changes_that_bullet_and_nothing_else()
    {
        var replaced = PlaybookBranchStep.Replaced;

        Assert.NotEqual(PlaybookBranchStep.Stock, replaced);
        Assert.Equal(0, Count(replaced, PlaybookBranchStep.OldBullet));
        Assert.Equal(1, Count(replaced, PlaybookBranchStep.NewBullet));
        Assert.Equal(PlaybookBranchStep.Stock, replaced.Replace(PlaybookBranchStep.NewBullet, PlaybookBranchStep.OldBullet));
    }

    [Fact]
    public void The_new_bullet_defers_to_the_prompts_branch_section_and_no_longer_says_to_cut_from_main()
    {
        Assert.Contains("\"The branch\" section", PlaybookBranchStep.NewBullet);
        Assert.DoesNotContain("origin/main", PlaybookBranchStep.NewBullet);
    }

    [Fact]
    public void The_replaced_prompt_still_fits_the_column()
    {
        Assert.True(PlaybookBranchStep.Replaced.Length <= EfHatchPlaybook.MaxPromptLength);
    }

    [Fact]
    public void The_guard_is_the_whole_prompt_and_only_the_prompt()
    {
        var sql = Sql(new PlaybookBranchStepProbe().Forward);

        Assert.Contains($"WHERE \"Prompt\" = $old${PlaybookBranchStep.Stock}$old$", sql);
        Assert.DoesNotContain("\"Types\"", sql);
        Assert.DoesNotContain("LIKE", sql);
    }

    [SkippableFact]
    public async Task An_unedited_row_is_replaced_even_with_its_types_narrowed_and_an_edited_one_is_left_alone()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var from = new EfHatchStatus { Name = "In Progress", SortOrder = 50 };
        var to = new EfHatchStatus { Name = "In Review", SortOrder = 60 };
        db.AddRange(from, to);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        EfHatchPlaybook Row(string types, string prompt) => new()
        {
            FromStatusId = from.Id, ToStatusId = to.Id, Types = types, Shape = "any", Prompt = prompt,
            Model = "sonnet", Effort = "high", CreatedAt = now, UpdatedAt = now,
        };

        var narrowed = Row("story,task,bug", PlaybookBranchStep.Stock);
        var edited = Row("epic", PlaybookBranchStep.Stock + "\n- Also do this.");
        db.AddRange(narrowed, edited);
        await db.SaveChangesAsync();

        foreach (var sql in Sql(new PlaybookBranchStepProbe().Forward).Split("\u0000"))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        Assert.Equal(PlaybookBranchStep.Replaced, (await db.Set<EfHatchPlaybook>().FindAsync(narrowed.Id))!.Prompt);
        Assert.Equal(PlaybookBranchStep.Stock + "\n- Also do this.", (await db.Set<EfHatchPlaybook>().FindAsync(edited.Id))!.Prompt);
    }

    private static int Count(string text, string of) =>
        (text.Length - text.Replace(of, "", StringComparison.Ordinal).Length) / of.Length;

    /// <summary>The SQL an operation list carries, joined with a NUL where there is more than one.</summary>
    private static string Sql(IReadOnlyList<MigrationOperation> operations) =>
        string.Join("\u0000", operations.OfType<SqlOperation>().Select(o => o.Sql));

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class PlaybookBranchStepProbe : PlaybookBranchStep
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

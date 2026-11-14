using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The four later rows - final analysis, implementation, closeout and epic
/// verification - each gain a plain-opening-sentence rule in the place their
/// existing bullet already lived. Like <c>RewordBreakdownRowsForTitleAndSummary</c>
/// each row guards and writes <c>DefaultPrompt</c> alone - no row's own
/// <c>Prompt</c> is read or written, and <c>UpdatedAt</c> never moves.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class RewordLaterRowsForTheSummaryTests
{
    [Fact]
    public void The_stock_texts_are_the_live_text_for_three_rows_and_the_seed_constant_for_verification()
    {
        Assert.NotEqual(SeedPlaybookDefaultPrompt.Analysis, RewordLaterRowsForTheSummary.AnalysisStock);
        Assert.NotEqual(SeedPlaybookDefaultPrompt.Implementation, RewordLaterRowsForTheSummary.ImplementationStock);
        Assert.NotEqual(SeedPlaybookDefaultPrompt.Closeout, RewordLaterRowsForTheSummary.CloseoutStock);
        Assert.Equal(SeedPlaybookDefaultPrompt.EpicVerification, RewordLaterRowsForTheSummary.VerificationStock);

        // Final analysis reads what its own reword left, and closeout is the seed
        // with the API prefix rewritten.
        Assert.Equal(RewordFinalAnalysisPlaybook.Replaced, RewordLaterRowsForTheSummary.AnalysisStock);
        Assert.Equal(
            SeedPlaybookDefaultPrompt.Closeout.Replace("/api/hatch/", "/api/", StringComparison.Ordinal),
            RewordLaterRowsForTheSummary.CloseoutStock);
    }

    [Fact]
    public void The_analysis_bullet_is_reworded_and_nothing_else_changes()
    {
        Assert.NotEqual(RewordLaterRowsForTheSummary.AnalysisStock, RewordLaterRowsForTheSummary.AnalysisReplaced);
        Assert.Contains(RewordLaterRowsForTheSummary.OldAnalysisBullet, RewordLaterRowsForTheSummary.AnalysisStock);
        Assert.Contains(RewordLaterRowsForTheSummary.NewAnalysisBullet, RewordLaterRowsForTheSummary.AnalysisReplaced);
        Assert.Contains("The summary at the top stays", RewordLaterRowsForTheSummary.AnalysisReplaced);
        Assert.True(RewordLaterRowsForTheSummary.AnalysisReplaced.Length <= EfHatchPlaybook.MaxPromptLength);

        Assert.Equal(
            RewordLaterRowsForTheSummary.AnalysisStock.Replace(RewordLaterRowsForTheSummary.OldAnalysisBullet, "", StringComparison.Ordinal),
            RewordLaterRowsForTheSummary.AnalysisReplaced.Replace(RewordLaterRowsForTheSummary.NewAnalysisBullet, "", StringComparison.Ordinal));
    }

    [Fact]
    public void The_implementation_bullet_is_reworded_and_nothing_else_changes()
    {
        Assert.NotEqual(RewordLaterRowsForTheSummary.ImplementationStock, RewordLaterRowsForTheSummary.ImplementationReplaced);
        Assert.Contains(RewordLaterRowsForTheSummary.OldImplementationBullet, RewordLaterRowsForTheSummary.ImplementationStock);
        Assert.DoesNotContain(RewordLaterRowsForTheSummary.OldImplementationBullet, RewordLaterRowsForTheSummary.ImplementationReplaced);
        Assert.Contains(RewordLaterRowsForTheSummary.NewImplementationBullet, RewordLaterRowsForTheSummary.ImplementationReplaced);
        Assert.Contains("opening with one sentence saying", RewordLaterRowsForTheSummary.ImplementationReplaced);
        Assert.True(RewordLaterRowsForTheSummary.ImplementationReplaced.Length <= EfHatchPlaybook.MaxPromptLength);

        Assert.Equal(
            RewordLaterRowsForTheSummary.ImplementationStock.Replace(RewordLaterRowsForTheSummary.OldImplementationBullet, "", StringComparison.Ordinal),
            RewordLaterRowsForTheSummary.ImplementationReplaced.Replace(RewordLaterRowsForTheSummary.NewImplementationBullet, "", StringComparison.Ordinal));
    }

    [Fact]
    public void The_closeout_bullet_is_reworded_and_nothing_else_changes()
    {
        Assert.NotEqual(RewordLaterRowsForTheSummary.CloseoutStock, RewordLaterRowsForTheSummary.CloseoutReplaced);
        Assert.Contains(RewordLaterRowsForTheSummary.OldCloseoutBullet, RewordLaterRowsForTheSummary.CloseoutStock);
        Assert.DoesNotContain(RewordLaterRowsForTheSummary.OldCloseoutBullet, RewordLaterRowsForTheSummary.CloseoutReplaced);
        Assert.Contains(RewordLaterRowsForTheSummary.NewCloseoutBullet, RewordLaterRowsForTheSummary.CloseoutReplaced);
        Assert.Contains("saying whether the ticket does what it asked", RewordLaterRowsForTheSummary.CloseoutReplaced);
        Assert.True(RewordLaterRowsForTheSummary.CloseoutReplaced.Length <= EfHatchPlaybook.MaxPromptLength);

        Assert.Equal(
            RewordLaterRowsForTheSummary.CloseoutStock.Replace(RewordLaterRowsForTheSummary.OldCloseoutBullet, "", StringComparison.Ordinal),
            RewordLaterRowsForTheSummary.CloseoutReplaced.Replace(RewordLaterRowsForTheSummary.NewCloseoutBullet, "", StringComparison.Ordinal));
    }

    [Fact]
    public void The_verification_bullet_is_reworded_and_nothing_else_changes()
    {
        Assert.NotEqual(RewordLaterRowsForTheSummary.VerificationStock, RewordLaterRowsForTheSummary.VerificationReplaced);
        Assert.Contains(RewordLaterRowsForTheSummary.OldVerificationBullet, RewordLaterRowsForTheSummary.VerificationStock);
        Assert.DoesNotContain(RewordLaterRowsForTheSummary.OldVerificationBullet, RewordLaterRowsForTheSummary.VerificationReplaced);
        Assert.Contains(RewordLaterRowsForTheSummary.NewVerificationBullet, RewordLaterRowsForTheSummary.VerificationReplaced);
        Assert.Contains("whether the epic does what it asked", RewordLaterRowsForTheSummary.VerificationReplaced);
        Assert.True(RewordLaterRowsForTheSummary.VerificationReplaced.Length <= EfHatchPlaybook.MaxPromptLength);

        Assert.Equal(
            RewordLaterRowsForTheSummary.VerificationStock.Replace(RewordLaterRowsForTheSummary.OldVerificationBullet, "", StringComparison.Ordinal),
            RewordLaterRowsForTheSummary.VerificationReplaced.Replace(RewordLaterRowsForTheSummary.NewVerificationBullet, "", StringComparison.Ordinal));
    }

    [Fact]
    public void Up_guards_and_writes_DefaultPrompt_alone_on_all_four_rows()
    {
        var sql = Sql(new Probe().Forward);

        Assert.Contains($"WHERE \"DefaultPrompt\" = $old${RewordLaterRowsForTheSummary.AnalysisStock}$old$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $new${RewordLaterRowsForTheSummary.AnalysisReplaced}$new$", sql);
        Assert.Contains($"WHERE \"DefaultPrompt\" = $old${RewordLaterRowsForTheSummary.ImplementationStock}$old$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $new${RewordLaterRowsForTheSummary.ImplementationReplaced}$new$", sql);
        Assert.Contains($"WHERE \"DefaultPrompt\" = $old${RewordLaterRowsForTheSummary.CloseoutStock}$old$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $new${RewordLaterRowsForTheSummary.CloseoutReplaced}$new$", sql);
        Assert.Contains($"WHERE \"DefaultPrompt\" = $old${RewordLaterRowsForTheSummary.VerificationStock}$old$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $new${RewordLaterRowsForTheSummary.VerificationReplaced}$new$", sql);

        Assert.DoesNotContain("\"Prompt\"", sql);
        Assert.DoesNotContain("UpdatedAt", sql);
        Assert.DoesNotContain("FromStatusId", sql);
        Assert.DoesNotContain("ToStatusId", sql);
        Assert.DoesNotContain("\"Types\"", sql);
        Assert.DoesNotContain("\"Shape\"", sql);
    }

    [Fact]
    public void Down_is_Up_mirrored_on_all_four_rows()
    {
        var sql = Sql(new Probe().Backward);

        Assert.Contains($"WHERE \"DefaultPrompt\" = $new${RewordLaterRowsForTheSummary.AnalysisReplaced}$new$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $old${RewordLaterRowsForTheSummary.AnalysisStock}$old$", sql);
        Assert.Contains($"WHERE \"DefaultPrompt\" = $new${RewordLaterRowsForTheSummary.ImplementationReplaced}$new$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $old${RewordLaterRowsForTheSummary.ImplementationStock}$old$", sql);
        Assert.Contains($"WHERE \"DefaultPrompt\" = $new${RewordLaterRowsForTheSummary.CloseoutReplaced}$new$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $old${RewordLaterRowsForTheSummary.CloseoutStock}$old$", sql);
        Assert.Contains($"WHERE \"DefaultPrompt\" = $new${RewordLaterRowsForTheSummary.VerificationReplaced}$new$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $old${RewordLaterRowsForTheSummary.VerificationStock}$old$", sql);

        Assert.DoesNotContain("\"Prompt\"", sql);
        Assert.DoesNotContain("UpdatedAt", sql);
    }

    private static readonly DateTimeOffset Seeded = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string EditedPrompt = "Do it your own way.";

    private const string UnrelatedDefault = "Something else entirely.";

    private static int _slot;

    private static (string Stock, string Replaced) RowTexts(string row) => row switch
    {
        "analysis" => (RewordLaterRowsForTheSummary.AnalysisStock, RewordLaterRowsForTheSummary.AnalysisReplaced),
        "implementation" => (RewordLaterRowsForTheSummary.ImplementationStock, RewordLaterRowsForTheSummary.ImplementationReplaced),
        "closeout" => (RewordLaterRowsForTheSummary.CloseoutStock, RewordLaterRowsForTheSummary.CloseoutReplaced),
        "verification" => (RewordLaterRowsForTheSummary.VerificationStock, RewordLaterRowsForTheSummary.VerificationReplaced),
        _ => throw new ArgumentOutOfRangeException(nameof(row)),
    };

    [SkippableTheory]
    [InlineData("analysis")]
    [InlineData("implementation")]
    [InlineData("closeout")]
    [InlineData("verification")]
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
    [InlineData("analysis")]
    [InlineData("implementation")]
    [InlineData("closeout")]
    [InlineData("verification")]
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

    [SkippableTheory]
    [InlineData("analysis")]
    [InlineData("implementation")]
    [InlineData("closeout")]
    [InlineData("verification")]
    public async Task Running_Up_twice_changes_nothing_the_second_time_and_Down_restores_the_stock_text(string row)
    {
        var (stock, replaced) = RowTexts(row);
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: null, defaultPrompt: stock);

        await RunAsync(db, new Probe().Forward);
        await RunAsync(db, new Probe().Forward);
        Assert.Equal(replaced, (await ReadAsync(db, id)).DefaultPrompt);

        await RunAsync(db, new Probe().Backward);
        var saved = await ReadAsync(db, id);
        Assert.Equal(stock, saved.DefaultPrompt);
        Assert.Null(saved.Prompt);
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
            // The stock text may carry braces, and ExecuteSqlRawAsync reads them as
            // format placeholders; escape them so the text reaches the database as
            // written.
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
    private sealed class Probe : RewordLaterRowsForTheSummary
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

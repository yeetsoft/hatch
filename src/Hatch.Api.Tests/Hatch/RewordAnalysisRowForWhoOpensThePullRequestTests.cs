using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The final-analysis row says the last task opens the one pull request only when
/// the ticket's tasks share a branch, and that otherwise each task opens and records
/// its own. Like <c>RewordLaterRowsForTheSummary</c> the migration guards and writes
/// <c>DefaultPrompt</c> alone - no row's own <c>Prompt</c> is read or written, and
/// <c>UpdatedAt</c> never moves.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class RewordAnalysisRowForWhoOpensThePullRequestTests
{
    private static string Stock => RewordAnalysisRowForWhoOpensThePullRequest.AnalysisStock;

    private static string Replaced => RewordAnalysisRowForWhoOpensThePullRequest.AnalysisReplaced;

    [Fact]
    public void The_stock_text_is_what_the_earlier_reword_left()
    {
        Assert.Equal(RewordLaterRowsForTheSummary.AnalysisReplaced, Stock);
    }

    [Fact]
    public void The_bullet_is_reworded_and_nothing_else_changes()
    {
        Assert.NotEqual(Stock, Replaced);
        Assert.Contains(RewordAnalysisRowForWhoOpensThePullRequest.OldBullet, Stock);
        Assert.DoesNotContain(RewordAnalysisRowForWhoOpensThePullRequest.OldBullet, Replaced);
        Assert.Contains(RewordAnalysisRowForWhoOpensThePullRequest.NewBullet, Replaced);
        Assert.True(Replaced.Length <= EfHatchPlaybook.MaxPromptLength);

        Assert.Equal(
            Stock.Replace(RewordAnalysisRowForWhoOpensThePullRequest.OldBullet, "", StringComparison.Ordinal),
            Replaced.Replace(RewordAnalysisRowForWhoOpensThePullRequest.NewBullet, "", StringComparison.Ordinal));
    }

    [Fact]
    public void The_new_text_names_who_opens_the_pull_request_and_the_unconditional_sentence_is_gone()
    {
        var flat = Flatten(Replaced);

        Assert.Contains("Tests, docs and style-guide updates go in the task they describe.", flat);
        Assert.Contains("When this ticket's tasks share one branch (`hatch one-pr`), the last task opens the one pull request;", flat);
        Assert.Contains("otherwise each task opens its own pull request and records it on itself with `hatch pr`.", flat);
        Assert.Contains("and the last task opens the pull request", Flatten(Stock));
        Assert.DoesNotContain("and the last task opens the pull request", flat);
    }

    [Fact]
    public void Up_guards_and_writes_DefaultPrompt_alone()
    {
        var sql = Sql(new Probe().Forward);

        Assert.Contains($"WHERE \"DefaultPrompt\" = $old${Stock}$old$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $new${Replaced}$new$", sql);
        Assert.DoesNotContain("\"Prompt\"", sql);
        Assert.DoesNotContain("UpdatedAt", sql);
    }

    [Fact]
    public void Down_is_Up_mirrored()
    {
        var sql = Sql(new Probe().Backward);

        Assert.Contains($"WHERE \"DefaultPrompt\" = $new${Replaced}$new$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $old${Stock}$old$", sql);
        Assert.DoesNotContain("\"Prompt\"", sql);
        Assert.DoesNotContain("UpdatedAt", sql);
    }

    private static readonly DateTimeOffset Seeded = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string EditedPrompt = "Do it your own way.";

    private const string UnrelatedDefault = "Something else entirely.";

    private static int _slot;

    [SkippableFact]
    public async Task An_unedited_row_learns_the_new_default_and_keeps_Prompt_and_UpdatedAt_untouched()
    {
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: null, defaultPrompt: Stock);

        await RunAsync(db, new Probe().Forward);

        var saved = await ReadAsync(db, id);
        Assert.Null(saved.Prompt);
        Assert.Equal(Replaced, saved.DefaultPrompt);
        Assert.Equal(Seeded, saved.UpdatedAt);
    }

    [SkippableFact]
    public async Task A_row_with_its_own_Prompt_keeps_it_byte_for_byte_and_still_learns_the_new_default()
    {
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: EditedPrompt, defaultPrompt: Stock);

        await RunAsync(db, new Probe().Forward);

        var saved = await ReadAsync(db, id);
        Assert.Equal(EditedPrompt, saved.Prompt);
        Assert.Equal(Replaced, saved.DefaultPrompt);
        Assert.Equal(Seeded, saved.UpdatedAt);
    }

    [SkippableFact]
    public async Task Running_Up_twice_changes_nothing_the_second_time_and_Down_restores_the_stock_text()
    {
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: null, defaultPrompt: Stock);

        await RunAsync(db, new Probe().Forward);
        await RunAsync(db, new Probe().Forward);
        Assert.Equal(Replaced, (await ReadAsync(db, id)).DefaultPrompt);

        await RunAsync(db, new Probe().Backward);
        var saved = await ReadAsync(db, id);
        Assert.Equal(Stock, saved.DefaultPrompt);
        Assert.Null(saved.Prompt);
        Assert.Equal(Seeded, saved.UpdatedAt);
    }

    [SkippableFact]
    public async Task A_row_someone_has_reworded_since_is_left_alone()
    {
        await using var db = await OpenAsync();
        var reworded = Stock.Replace("Chain with depends", "Chain with depends, if you must,", StringComparison.Ordinal);
        Assert.NotEqual(Stock, reworded);
        var rewordedId = await AddRowAsync(db, prompt: null, defaultPrompt: reworded);
        var unrelatedId = await AddRowAsync(db, prompt: null, defaultPrompt: UnrelatedDefault);

        await RunAsync(db, new Probe().Forward);

        var savedReworded = await ReadAsync(db, rewordedId);
        Assert.Equal(reworded, savedReworded.DefaultPrompt);
        Assert.Equal(Seeded, savedReworded.UpdatedAt);
        var savedUnrelated = await ReadAsync(db, unrelatedId);
        Assert.Equal(UnrelatedDefault, savedUnrelated.DefaultPrompt);
        Assert.Equal(Seeded, savedUnrelated.UpdatedAt);
    }

    private static string Flatten(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
    private sealed class Probe : RewordAnalysisRowForWhoOpensThePullRequest
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

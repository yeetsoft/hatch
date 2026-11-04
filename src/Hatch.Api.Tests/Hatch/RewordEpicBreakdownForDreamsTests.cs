using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The epic-breakdown row now consults the Dream Board and records the dreams it
/// drew on. Like <see cref="RewordReflectionPlaybook"/> this guards and writes
/// <c>DefaultPrompt</c> alone - no row's own <c>Prompt</c> is read or
/// written, and <c>UpdatedAt</c> never moves.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class RewordEpicBreakdownForDreamsTests
{
    [Fact]
    public void The_frozen_old_text_equals_the_constant_it_was_copied_from()
    {
        Assert.Equal(SeedPlaybookDefaultPrompt.EpicBreakdown, RewordEpicBreakdownForDreams.Stock);
    }

    /// <summary>The bullet the reword inserts, with its trailing newline.</summary>
    private const string Paragraph = """
        - Search the Dream Board for dreams that bear on this epic -
          `hatch api GET /api/hatch/dreams?q=<words from the title and description>`
          lists them best first, any word matching. Read the ones that do bear on it
          and fold what they ask for into the brief, quoting them. Record which you
          drew on with `hatch api PUT /api/hatch/issues/<KEY>/dreams
          '{"dreamIds":[...]}'`. An empty answer costs one call; say
          *no dream bore on this* in the description and move on.

        """;

    [Fact]
    public void The_new_text_consults_the_Dream_Board()
    {
        var replaced = RewordEpicBreakdownForDreams.Replaced;

        Assert.Contains("GET /api/hatch/dreams?q=", replaced);
        Assert.Contains("PUT /api/hatch/issues/<KEY>/dreams", replaced);
        Assert.Contains("\"dreamIds\"", replaced);
        Assert.Contains("no dream bore on this", replaced);
        Assert.True(replaced.Length <= EfHatchPlaybook.MaxPromptLength);
        Assert.NotEqual(RewordEpicBreakdownForDreams.Stock, replaced);
    }

    [Fact]
    public void The_paragraph_sits_between_reading_the_history_and_asking_the_repository()
    {
        var replaced = RewordEpicBreakdownForDreams.Replaced;

        var history = replaced.IndexOf("Read what has already happened", StringComparison.Ordinal);
        var dreams = replaced.IndexOf("Search the Dream Board", StringComparison.Ordinal);
        var repository = replaced.IndexOf("Ask the repository", StringComparison.Ordinal);

        Assert.True(history >= 0);
        Assert.True(history < dreams);
        Assert.True(dreams < repository);
    }

    [Fact]
    public void Nothing_else_moved()
    {
        Assert.Equal(1, RewordEpicBreakdownForDreams.Replaced.Split(Paragraph).Length - 1);
        Assert.Equal(RewordEpicBreakdownForDreams.Stock, RewordEpicBreakdownForDreams.Replaced.Replace(Paragraph, ""));
    }

    [Fact]
    public void Up_guards_and_writes_DefaultPrompt_alone()
    {
        var sql = Sql(new Probe().Forward);

        Assert.Contains($"WHERE \"DefaultPrompt\" = $old${RewordEpicBreakdownForDreams.Stock}$old$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $new${RewordEpicBreakdownForDreams.Replaced}$new$", sql);

        Assert.DoesNotContain("\"Prompt\"", sql);
        Assert.DoesNotContain("UpdatedAt", sql);
        Assert.DoesNotContain("FromStatusId", sql);
        Assert.DoesNotContain("ToStatusId", sql);
        Assert.DoesNotContain("\"Types\"", sql);
        Assert.DoesNotContain("\"Shape\"", sql);
    }

    [Fact]
    public void Down_is_Up_mirrored()
    {
        var sql = string.Join("\0", new Probe().Backward.OfType<SqlOperation>().Select(o => o.Sql));

        Assert.Contains($"WHERE \"DefaultPrompt\" = $new${RewordEpicBreakdownForDreams.Replaced}$new$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $old${RewordEpicBreakdownForDreams.Stock}$old$", sql);

        Assert.DoesNotContain("\"Prompt\"", sql);
        Assert.DoesNotContain("UpdatedAt", sql);
    }

    private static readonly DateTimeOffset Seeded = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string EditedPrompt = "Do it your own way.";

    private const string UnrelatedDefault = "Something else entirely.";

    private static int _slot;

    [SkippableFact]
    public async Task An_unedited_row_learns_the_new_default_and_keeps_Prompt_null()
    {
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: null, defaultPrompt: RewordEpicBreakdownForDreams.Stock);

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Null(row.Prompt);
        Assert.Equal(RewordEpicBreakdownForDreams.Replaced, row.DefaultPrompt);
        Assert.Equal(Seeded, row.UpdatedAt);
    }

    [SkippableFact]
    public async Task A_row_with_its_own_Prompt_keeps_it_byte_for_byte_and_still_learns_the_new_default()
    {
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: EditedPrompt, defaultPrompt: RewordEpicBreakdownForDreams.Stock);

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Equal(EditedPrompt, row.Prompt);
        Assert.Equal(RewordEpicBreakdownForDreams.Replaced, row.DefaultPrompt);
        Assert.Equal(Seeded, row.UpdatedAt);
    }

    [SkippableFact]
    public async Task A_row_whose_default_does_not_match_the_stock_text_is_left_alone()
    {
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: null, defaultPrompt: UnrelatedDefault);

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Equal(UnrelatedDefault, row.DefaultPrompt);
        Assert.Equal(Seeded, row.UpdatedAt);
    }

    [SkippableFact]
    public async Task Running_Up_twice_leaves_the_state_running_it_once_did()
    {
        await using var db = await OpenAsync();
        var unedited = await AddRowAsync(db, prompt: null, defaultPrompt: RewordEpicBreakdownForDreams.Stock);
        var edited = await AddRowAsync(db, prompt: EditedPrompt, defaultPrompt: RewordEpicBreakdownForDreams.Stock);
        var unrelated = await AddRowAsync(db, prompt: null, defaultPrompt: UnrelatedDefault);

        await RunAsync(db, new Probe().Forward);
        var once = await SnapshotAsync(db, unedited, edited, unrelated);
        await RunAsync(db, new Probe().Forward);
        var twice = await SnapshotAsync(db, unedited, edited, unrelated);

        Assert.Equal(once, twice);
        Assert.Equal(
            new (string? Prompt, string? DefaultPrompt)[]
            {
                (null, RewordEpicBreakdownForDreams.Replaced),
                (EditedPrompt, RewordEpicBreakdownForDreams.Replaced),
                (null, UnrelatedDefault),
            },
            once);
    }

    [SkippableFact]
    public async Task Down_after_Up_returns_every_row_to_what_it_was_without_touching_UpdatedAt()
    {
        await using var db = await OpenAsync();
        var unedited = await AddRowAsync(db, prompt: null, defaultPrompt: RewordEpicBreakdownForDreams.Stock);
        var edited = await AddRowAsync(db, prompt: EditedPrompt, defaultPrompt: RewordEpicBreakdownForDreams.Stock);

        await RunAsync(db, new Probe().Forward);
        await RunAsync(db, new Probe().Backward);

        var uneditedRow = await ReadAsync(db, unedited);
        Assert.Null(uneditedRow.Prompt);
        Assert.Equal(RewordEpicBreakdownForDreams.Stock, uneditedRow.DefaultPrompt);
        Assert.Equal(Seeded, uneditedRow.UpdatedAt);

        var editedRow = await ReadAsync(db, edited);
        Assert.Equal(EditedPrompt, editedRow.Prompt);
        Assert.Equal(RewordEpicBreakdownForDreams.Stock, editedRow.DefaultPrompt);
        Assert.Equal(Seeded, editedRow.UpdatedAt);
    }

    private static async Task<List<(string? Prompt, string? DefaultPrompt)>> SnapshotAsync(HatchContext db, params int[] ids)
    {
        var rows = new List<(string?, string?)>();
        foreach (var id in ids)
        {
            var row = await ReadAsync(db, id);
            rows.Add((row.Prompt, row.DefaultPrompt));
        }

        return rows;
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
            // The SQL carries a JSON literal, and ExecuteSqlRawAsync reads braces as format
            // placeholders; escape them so the text reaches the database as written.
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
    private sealed class Probe : RewordEpicBreakdownForDreams
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

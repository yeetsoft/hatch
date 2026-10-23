using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The reflection row now files a tracker-instrumentation gap as an issue in the
/// project the board flags as the tracker's own, and leaves it as prose where
/// none is flagged. Like <see cref="RewordReflectionPlaybook"/> this guards and
/// writes <c>DefaultPrompt</c> alone - no row's own <c>Prompt</c> is read or
/// written, and <c>UpdatedAt</c> never moves.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ReflectionFilesTrackerGapsTests
{
    [Fact]
    public void The_frozen_old_text_equals_the_constant_it_was_copied_from()
    {
        Assert.Equal(RewordReflectionPlaybook.Replaced, ReflectionFilesTrackerGaps.Stock);
    }

    [Fact]
    public void The_new_text_files_tracker_gaps_in_the_flagged_project()
    {
        var replaced = ReflectionFilesTrackerGaps.Replaced;
        // The text is wrapped, so a phrase is read across a line break as one space.
        var flat = string.Join(' ', replaced.Split('\n').Select(l => l.Trim()));

        Assert.Contains("GET /api/hatch/projects", replaced);
        Assert.Contains("\"bindsTracker\": true", replaced);
        Assert.Contains("if the board has one", flat);
        Assert.Contains("leave it in the report as prose", replaced);

        // The old no-repository meaning is kept.
        Assert.Contains("instrumentation request", replaced);
        Assert.Contains("rather than filing", replaced);

        Assert.True(replaced.Length <= EfHatchPlaybook.MaxPromptLength);
        Assert.NotEqual(ReflectionFilesTrackerGaps.Stock, replaced);

        // Every other bullet is still there.
        Assert.Contains("## Hypothesis", replaced);
        Assert.Contains("Hypotheses read", replaced);
        Assert.Contains("revert or amend", replaced);
    }

    [Fact]
    public void The_new_text_names_no_board_repository_domain_or_person()
    {
        var replaced = ReflectionFilesTrackerGaps.Replaced;

        Assert.DoesNotContain("http", replaced);
        Assert.DoesNotContain(".com", replaced);
        Assert.DoesNotContain("@", replaced);
    }

    [Fact]
    public void Up_guards_and_writes_DefaultPrompt_alone()
    {
        var sql = Sql(new Probe().Forward);

        Assert.Contains($"WHERE \"DefaultPrompt\" = $old${ReflectionFilesTrackerGaps.Stock}$old$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $new${ReflectionFilesTrackerGaps.Replaced}$new$", sql);

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

        Assert.Contains($"WHERE \"DefaultPrompt\" = $new${ReflectionFilesTrackerGaps.Replaced}$new$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $old${ReflectionFilesTrackerGaps.Stock}$old$", sql);

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
        var id = await AddRowAsync(db, prompt: null, defaultPrompt: ReflectionFilesTrackerGaps.Stock);

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Null(row.Prompt);
        Assert.Equal(ReflectionFilesTrackerGaps.Replaced, row.DefaultPrompt);
        Assert.Equal(Seeded, row.UpdatedAt);
    }

    [SkippableFact]
    public async Task A_row_with_its_own_Prompt_keeps_it_byte_for_byte_and_still_learns_the_new_default()
    {
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: EditedPrompt, defaultPrompt: ReflectionFilesTrackerGaps.Stock);

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Equal(EditedPrompt, row.Prompt);
        Assert.Equal(ReflectionFilesTrackerGaps.Replaced, row.DefaultPrompt);
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
    public async Task A_row_still_on_the_pre_hypothesis_text_is_left_alone()
    {
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: null, defaultPrompt: RewordReflectionPlaybook.Stock);

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Equal(RewordReflectionPlaybook.Stock, row.DefaultPrompt);
        Assert.Equal(Seeded, row.UpdatedAt);
    }

    [SkippableFact]
    public async Task Running_Up_twice_leaves_the_state_running_it_once_did()
    {
        await using var db = await OpenAsync();
        var unedited = await AddRowAsync(db, prompt: null, defaultPrompt: ReflectionFilesTrackerGaps.Stock);
        var edited = await AddRowAsync(db, prompt: EditedPrompt, defaultPrompt: ReflectionFilesTrackerGaps.Stock);
        var unrelated = await AddRowAsync(db, prompt: null, defaultPrompt: UnrelatedDefault);

        await RunAsync(db, new Probe().Forward);
        var once = await SnapshotAsync(db, unedited, edited, unrelated);
        await RunAsync(db, new Probe().Forward);
        var twice = await SnapshotAsync(db, unedited, edited, unrelated);

        Assert.Equal(once, twice);
        Assert.Equal(
            new (string? Prompt, string? DefaultPrompt)[]
            {
                (null, ReflectionFilesTrackerGaps.Replaced),
                (EditedPrompt, ReflectionFilesTrackerGaps.Replaced),
                (null, UnrelatedDefault),
            },
            once);
    }

    [SkippableFact]
    public async Task Down_after_Up_returns_every_row_to_what_it_was_without_touching_UpdatedAt()
    {
        await using var db = await OpenAsync();
        var unedited = await AddRowAsync(db, prompt: null, defaultPrompt: ReflectionFilesTrackerGaps.Stock);
        var edited = await AddRowAsync(db, prompt: EditedPrompt, defaultPrompt: ReflectionFilesTrackerGaps.Stock);

        await RunAsync(db, new Probe().Forward);
        await RunAsync(db, new Probe().Backward);

        var uneditedRow = await ReadAsync(db, unedited);
        Assert.Null(uneditedRow.Prompt);
        Assert.Equal(ReflectionFilesTrackerGaps.Stock, uneditedRow.DefaultPrompt);
        Assert.Equal(Seeded, uneditedRow.UpdatedAt);

        var editedRow = await ReadAsync(db, edited);
        Assert.Equal(EditedPrompt, editedRow.Prompt);
        Assert.Equal(ReflectionFilesTrackerGaps.Stock, editedRow.DefaultPrompt);
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
            await db.Database.ExecuteSqlRawAsync(operation.Sql);
    }

    private static async Task<EfHatchPlaybook> ReadAsync(HatchContext db, int id)
    {
        db.ChangeTracker.Clear();
        return (await db.Set<EfHatchPlaybook>().FindAsync(id))!;
    }

    private static string Sql(IReadOnlyList<MigrationOperation> operations) =>
        string.Join("\0", operations.OfType<SqlOperation>().Select(o => o.Sql));

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class Probe : ReflectionFilesTrackerGaps
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

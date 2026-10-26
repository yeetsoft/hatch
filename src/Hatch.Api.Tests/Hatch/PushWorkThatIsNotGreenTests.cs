using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The implementation row's green-before-pushed bullet now pushes work that
/// is not green instead of stopping with it on one machine. Like the reword
/// before it, it guards and writes <c>DefaultPrompt</c> alone - no row's own
/// <c>Prompt</c> is read or written, and <c>UpdatedAt</c> never moves.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class PushWorkThatIsNotGreenTests
{
    [Fact]
    public void The_frozen_old_text_equals_the_constant_it_was_copied_from()
    {
        Assert.Equal(RewordImplementationPlaybook.Replaced, PushWorkThatIsNotGreen.Stock);
        Assert.Equal(RewordImplementationPlaybook.NewGreenBullet, PushWorkThatIsNotGreen.OldGreenBullet);
    }

    [Fact]
    public void The_new_bullet_names_both_cases_and_pushes_in_either()
    {
        var bullet = PushWorkThatIsNotGreen.NewGreenBullet;
        var unrunnable = bullet.IndexOf("checks cannot run here", StringComparison.Ordinal);
        var red = bullet.IndexOf("red and you cannot fix it", StringComparison.Ordinal);

        Assert.True(unrunnable >= 0, "the bullet no longer covers checks that cannot run here");
        Assert.True(red > unrunnable, "the bullet no longer covers work that is red and cannot be fixed");

        Assert.Contains("push", bullet[unrunnable..red]);
        Assert.Contains("push it", bullet[red..]);
        Assert.DoesNotContain("do not push", bullet);
    }

    [Fact]
    public void The_new_text_differs_only_in_the_green_before_pushed_bullet()
    {
        Assert.NotEqual(PushWorkThatIsNotGreen.Stock, PushWorkThatIsNotGreen.Replaced);
        Assert.Contains(PushWorkThatIsNotGreen.OldGreenBullet, PushWorkThatIsNotGreen.Stock);
        Assert.DoesNotContain(PushWorkThatIsNotGreen.OldGreenBullet, PushWorkThatIsNotGreen.Replaced);
        Assert.Contains(PushWorkThatIsNotGreen.NewGreenBullet, PushWorkThatIsNotGreen.Replaced);

        // Every other line is byte-identical: removing both bullets from each
        // text leaves the same string.
        Assert.Equal(
            PushWorkThatIsNotGreen.Stock.Replace(PushWorkThatIsNotGreen.OldGreenBullet, "", StringComparison.Ordinal),
            PushWorkThatIsNotGreen.Replaced.Replace(PushWorkThatIsNotGreen.NewGreenBullet, "", StringComparison.Ordinal));
    }

    [Fact]
    public void Up_guards_and_writes_DefaultPrompt_alone()
    {
        var sql = Sql(new Probe().Forward);

        Assert.Contains($"WHERE \"DefaultPrompt\" = $old${PushWorkThatIsNotGreen.Stock}$old$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $new${PushWorkThatIsNotGreen.Replaced}$new$", sql);

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

        Assert.Contains($"WHERE \"DefaultPrompt\" = $new${PushWorkThatIsNotGreen.Replaced}$new$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $old${PushWorkThatIsNotGreen.Stock}$old$", sql);

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
        var id = await AddRowAsync(db, prompt: null, defaultPrompt: PushWorkThatIsNotGreen.Stock);

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Null(row.Prompt);
        Assert.Equal(PushWorkThatIsNotGreen.Replaced, row.DefaultPrompt);
        Assert.Equal(Seeded, row.UpdatedAt);
    }

    [SkippableFact]
    public async Task A_row_with_its_own_Prompt_keeps_it_byte_for_byte_and_still_learns_the_new_default()
    {
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: EditedPrompt, defaultPrompt: PushWorkThatIsNotGreen.Stock);

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Equal(EditedPrompt, row.Prompt);
        Assert.Equal(PushWorkThatIsNotGreen.Replaced, row.DefaultPrompt);
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
        var unedited = await AddRowAsync(db, prompt: null, defaultPrompt: PushWorkThatIsNotGreen.Stock);
        var edited = await AddRowAsync(db, prompt: EditedPrompt, defaultPrompt: PushWorkThatIsNotGreen.Stock);
        var unrelated = await AddRowAsync(db, prompt: null, defaultPrompt: UnrelatedDefault);

        await RunAsync(db, new Probe().Forward);
        var once = await SnapshotAsync(db, unedited, edited, unrelated);
        await RunAsync(db, new Probe().Forward);
        var twice = await SnapshotAsync(db, unedited, edited, unrelated);

        Assert.Equal(once, twice);
        Assert.Equal(
            new (string? Prompt, string? DefaultPrompt)[]
            {
                (null, PushWorkThatIsNotGreen.Replaced),
                (EditedPrompt, PushWorkThatIsNotGreen.Replaced),
                (null, UnrelatedDefault),
            },
            once);
    }

    [SkippableFact]
    public async Task Down_after_Up_returns_every_row_to_what_it_was_without_touching_UpdatedAt()
    {
        await using var db = await OpenAsync();
        var unedited = await AddRowAsync(db, prompt: null, defaultPrompt: PushWorkThatIsNotGreen.Stock);
        var edited = await AddRowAsync(db, prompt: EditedPrompt, defaultPrompt: PushWorkThatIsNotGreen.Stock);

        await RunAsync(db, new Probe().Forward);
        await RunAsync(db, new Probe().Backward);

        var uneditedRow = await ReadAsync(db, unedited);
        Assert.Null(uneditedRow.Prompt);
        Assert.Equal(PushWorkThatIsNotGreen.Stock, uneditedRow.DefaultPrompt);
        Assert.Equal(Seeded, uneditedRow.UpdatedAt);

        var editedRow = await ReadAsync(db, edited);
        Assert.Equal(EditedPrompt, editedRow.Prompt);
        Assert.Equal(PushWorkThatIsNotGreen.Stock, editedRow.DefaultPrompt);
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
    private sealed class Probe : PushWorkThatIsNotGreen
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

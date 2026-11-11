using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The reflection row now files by <c>filedFromKey</c>, writes a hypothesis
/// that measures itself, declares no code, and ends on the review column
/// rather than Backlog. Like <see cref="RewordReflectionPlaybookTests"/> the
/// reword half guards and writes <c>DefaultPrompt</c> alone; the retarget
/// half is tested against real rows the way
/// <see cref="MoveReflectionChildrenToFiledFromTests"/> runs its one SQL
/// operation straight.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ReflectionFilesByLinkEndsInReviewTests
{
    [Fact]
    public void The_frozen_old_text_equals_the_constant_it_was_copied_from()
    {
        Assert.Equal(ReflectionFilesTrackerGaps.Replaced, ReflectionFilesByLinkEndsInReview.Stock);
    }

    [Fact]
    public void The_new_text_files_by_link_and_declares_no_code()
    {
        var replaced = ReflectionFilesByLinkEndsInReview.Replaced;

        Assert.Contains("filedFromKey", replaced);
        Assert.Contains("## How to read it", replaced);
        Assert.Contains("hatch depends", replaced);
        Assert.Contains("declare no code", replaced);
        Assert.DoesNotContain("under this reflection", replaced);
        Assert.DoesNotContain("left for a person to close", replaced);
        Assert.DoesNotContain("parentKey", replaced);
        Assert.True(replaced.Length <= EfHatchPlaybook.MaxPromptLength);
        Assert.NotEqual(ReflectionFilesByLinkEndsInReview.Stock, replaced);
    }

    [Fact]
    public void Ups_reword_half_guards_and_writes_DefaultPrompt_alone()
    {
        var sql = Reword(new Probe().Forward);

        Assert.Contains($"WHERE \"DefaultPrompt\" = $old${ReflectionFilesByLinkEndsInReview.Stock}$old$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $new${ReflectionFilesByLinkEndsInReview.Replaced}$new$", sql);

        Assert.DoesNotContain("\"Prompt\"", sql);
        Assert.DoesNotContain("UpdatedAt", sql);
        Assert.DoesNotContain("ToStatusId", sql);
        Assert.DoesNotContain("\"Types\"", sql);
        Assert.DoesNotContain("\"Shape\"", sql);
    }

    [Fact]
    public void Downs_reword_half_is_ups_mirrored()
    {
        var sql = Reword(new Probe().Backward);

        Assert.Contains($"WHERE \"DefaultPrompt\" = $new${ReflectionFilesByLinkEndsInReview.Replaced}$new$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $old${ReflectionFilesByLinkEndsInReview.Stock}$old$", sql);

        Assert.DoesNotContain("\"Prompt\"", sql);
        Assert.DoesNotContain("UpdatedAt", sql);
    }

    [Fact]
    public void The_retarget_half_is_routing_alone_and_names_no_board_repository_or_person()
    {
        var sql = Retarget(new Probe().Forward);

        Assert.Contains("f.\"Name\" = 'Breakdown'", sql);
        Assert.Contains("t.\"Name\" = 'Backlog'", sql);
        Assert.Contains("p.\"Types\" = 'reflection'", sql);
        Assert.Contains("\"IsTerminal\"", sql);
        Assert.DoesNotContain("DefaultPrompt", sql);
        Assert.DoesNotContain("\"Prompt\"", sql);
    }

    private static readonly DateTimeOffset Seeded = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string EditedPrompt = "Do it your own way.";

    private const string UnrelatedDefault = "Something else entirely.";

    [SkippableFact]
    public async Task An_unedited_row_learns_the_new_default_and_moves_to_the_review_column()
    {
        await using var db = await OpenAsync();
        var board = await SeedBoardAsync(db);
        var id = await AddRowAsync(db, board, prompt: null, defaultPrompt: ReflectionFilesByLinkEndsInReview.Stock, toId: board.Backlog);

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Null(row.Prompt);
        Assert.Equal(ReflectionFilesByLinkEndsInReview.Replaced, row.DefaultPrompt);
        Assert.Equal(board.Review, row.ToStatusId);
        Assert.Equal(Seeded, row.UpdatedAt);
    }

    [SkippableFact]
    public async Task A_row_with_its_own_Prompt_keeps_it_and_still_moves_to_the_review_column()
    {
        await using var db = await OpenAsync();
        var board = await SeedBoardAsync(db);
        var id = await AddRowAsync(db, board, prompt: EditedPrompt, defaultPrompt: ReflectionFilesByLinkEndsInReview.Stock, toId: board.Backlog);

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Equal(EditedPrompt, row.Prompt);
        Assert.Equal(ReflectionFilesByLinkEndsInReview.Replaced, row.DefaultPrompt);
        Assert.Equal(board.Review, row.ToStatusId);
        Assert.Equal(Seeded, row.UpdatedAt);
    }

    [SkippableFact]
    public async Task An_unrelated_row_off_the_Breakdown_to_Backlog_route_is_left_on_both()
    {
        await using var db = await OpenAsync();
        var board = await SeedBoardAsync(db);
        var id = await AddRowAsync(db, board, prompt: null, defaultPrompt: UnrelatedDefault, fromId: board.Review, toId: board.Review, types: "");

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Equal(UnrelatedDefault, row.DefaultPrompt);
        Assert.Equal(board.Review, row.ToStatusId);
        Assert.Equal(Seeded, row.UpdatedAt);
    }

    [SkippableFact]
    public async Task A_row_already_retargeted_off_Backlog_still_gets_reworded_but_not_re_retargeted()
    {
        await using var db = await OpenAsync();
        var board = await SeedBoardAsync(db);
        var id = await AddRowAsync(db, board, prompt: null, defaultPrompt: ReflectionFilesByLinkEndsInReview.Stock, toId: board.Review);

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Equal(ReflectionFilesByLinkEndsInReview.Replaced, row.DefaultPrompt);
        Assert.Equal(board.Review, row.ToStatusId);
    }

    [SkippableFact]
    public async Task Running_Up_twice_leaves_the_state_running_it_once_did()
    {
        await using var db = await OpenAsync();
        var board = await SeedBoardAsync(db);
        var id = await AddRowAsync(db, board, prompt: null, defaultPrompt: ReflectionFilesByLinkEndsInReview.Stock, toId: board.Backlog);

        await RunAsync(db, new Probe().Forward);
        var once = await SnapshotAsync(db, id);
        await RunAsync(db, new Probe().Forward);
        var twice = await SnapshotAsync(db, id);

        Assert.Equal(once, twice);
        Assert.Equal((ReflectionFilesByLinkEndsInReview.Replaced, board.Review), once);
    }

    [SkippableFact]
    public async Task Down_after_Up_returns_an_unedited_row_to_what_it_was_and_leaves_an_edited_one_alone()
    {
        await using var db = await OpenAsync();
        var board = await SeedBoardAsync(db);
        var unedited = await AddRowAsync(db, board, prompt: null, defaultPrompt: ReflectionFilesByLinkEndsInReview.Stock, toId: board.Backlog);
        var edited = await AddRowAsync(db, board, prompt: null, defaultPrompt: UnrelatedDefault, fromId: board.Review, toId: board.Review, types: "");

        await RunAsync(db, new Probe().Forward);
        await RunAsync(db, new Probe().Backward);

        var uneditedRow = await ReadAsync(db, unedited);
        Assert.Equal(ReflectionFilesByLinkEndsInReview.Stock, uneditedRow.DefaultPrompt);
        Assert.Equal(board.Backlog, uneditedRow.ToStatusId);
        Assert.Equal(Seeded, uneditedRow.UpdatedAt);

        var editedRow = await ReadAsync(db, edited);
        Assert.Equal(UnrelatedDefault, editedRow.DefaultPrompt);
        Assert.Equal(board.Review, editedRow.ToStatusId);
        Assert.Equal(Seeded, editedRow.UpdatedAt);
    }

    private sealed record Board(int Breakdown, int Backlog, int Review, int Done);

    /// <summary>
    /// Breakdown, Backlog, a non-deferred non-terminal review column and a
    /// terminal Done, in that order - the shape the retarget's own SQL finds
    /// the review column against.
    /// </summary>
    private static async Task<Board> SeedBoardAsync(HatchContext db)
    {
        var breakdown = new EfHatchStatus { Name = "Breakdown", SortOrder = 10 };
        var backlog = new EfHatchStatus { Name = "Backlog", SortOrder = 20 };
        var review = new EfHatchStatus { Name = "In Review", SortOrder = 30 };
        var done = new EfHatchStatus { Name = "Done", SortOrder = 40, IsTerminal = true };
        db.AddRange(breakdown, backlog, review, done);
        await db.SaveChangesAsync();
        return new Board(breakdown.Id, backlog.Id, review.Id, done.Id);
    }

    private static int _slot;

    /// <summary>
    /// A playbook in a slot of its own. The unique index is on from, to,
    /// types, shape and project, so each row not meant to collide gets a
    /// shape of its own.
    /// </summary>
    private static async Task<int> AddRowAsync(
        HatchContext db, Board board, string? prompt, string? defaultPrompt,
        int? fromId = null, int? toId = null, string types = "reflection")
    {
        var slot = Interlocked.Increment(ref _slot);
        var row = new EfHatchPlaybook
        {
            FromStatusId = fromId ?? board.Breakdown,
            ToStatusId = toId ?? board.Backlog,
            Types = types, Shape = $"leaf{slot}"[..Math.Min(10, $"leaf{slot}".Length)],
            Prompt = prompt, DefaultPrompt = defaultPrompt,
            Model = "fable", Effort = "max", CreatedAt = Seeded, UpdatedAt = Seeded,
        };
        db.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private static async Task<(string? DefaultPrompt, int ToStatusId)> SnapshotAsync(HatchContext db, int id)
    {
        var row = await ReadAsync(db, id);
        return (row.DefaultPrompt, row.ToStatusId);
    }

    private static async Task<HatchContext> OpenAsync()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        return new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);
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

    private static string Reword(IReadOnlyList<MigrationOperation> operations) =>
        operations.OfType<SqlOperation>().First().Sql;

    private static string Retarget(IReadOnlyList<MigrationOperation> operations) =>
        operations.OfType<SqlOperation>().Last().Sql;

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class Probe : ReflectionFilesByLinkEndsInReview
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

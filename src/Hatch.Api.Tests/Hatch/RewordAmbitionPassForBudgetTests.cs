using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The ambition-pass row now tells the pass to read the budget before it
/// carves, and once it is spent to carve nothing and ask one question.
/// Like <see cref="RewordEpicBreakdownForDreamsTests"/> this guards and
/// writes <c>DefaultPrompt</c> alone - no row's own <c>Prompt</c> is read or
/// written, and <c>UpdatedAt</c> never moves.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class RewordAmbitionPassForBudgetTests
{
    /// <summary>A frozen copy of the new text, held here and not read off the migration, so a change to one alone fails this test.</summary>
    private const string NewText = """
        You are running a pass on an ambition. A pass chooses the next piece of work from
        what the trunk holds today. You judge and you file. You never edit the tree, and
        your tools refuse if you try.

        - Read the ambition, every earlier pass comment on it, its answered questions, and
          each epic under it with that epic's own verdict. If an earlier run of this pass
          was aborted partway, continue it rather than starting again.
        - Decide what holds by reading the trunk, not the tickets. Read the ambition's
          capabilities with `hatch api GET /api/issues/<key>/capabilities`. Check each one
          against the trunk by its how-to-tell line. Call it holds, does not hold or cannot
          tell. A closed epic is not evidence. The trunk is.
        - Record each verdict with `hatch capability judge`. Name what you looked at (the
          file, test, route or docs page) and the sha of the trunk.
        - Read the budget before you carve, with `hatch api GET /api/issues/<key>/budget`.
          If `Spent` is true, carve nothing: the server refuses an epic under the ambition
          while its budget is spent. Ask one question on the ambition with `hatch ask`,
          naming the budget and the amount spent, with two choices: raise the budget, or
          stop here. A person who raises it sets the higher amount on the ambition; that is
          what lets a later pass carve. Then comment the pass. An ambition with no budget
          is never spent.
        - If an answered question on the ambition already says to stop at this amount, do
          not ask again. Carve nothing, still record your verdicts, and comment the pass.
        - Work out how many epics you may carve: the ambition's Epics at once (its
          `wipLimit`; where none is set it reads as one), less the epics under it that are
          still open. An open epic is one in a column that is neither done nor deferred. If
          that is zero or less, carve nothing.
        - Choose what to carve, in this order. First, the thinnest piece that makes one
          more capability hold on the trunk by itself; an epic that only prepares for a
          later one is the wrong cut. Second, what settles the most doubt; if it is unclear
          whether something can work at all, find out now. Third, what the operator gets
          the most from.
        - Two epics carved in one pass must not need each other. If a story in one would
          wait on a story in the other, carve one now and leave the other for the next
          pass.
        - An epic may need a decision that is the operator's. Ask it on that epic with
          `hatch ask`, the choices named and one recommended. Ask nothing else on the ambition.
          Never leave every open epic waiting on the operator: if carving would do that,
          carve one that needs no decision instead.
        - File each carved epic under the ambition with `hatch api POST /api/issues`: type
          `epic`, `parentKey` the ambition's key. Write it for one breakdown session: a
          short summary, then `## What this is for`, `## What is already there`,
          `## What done looks like` as numbered checks, `## Decisions taken`, and three
          to six suggested stories. Say which capabilities it serves, by number.
        - Link each epic to what it serves with `hatch serves`. Move it to the breakdown
          column at once, after any question is asked on it, and confirm with `hatch show`
          that it stands there.
        - Comment the pass on the ambition. Open with `Pass <n>` (one more than the earlier
          pass comments) and the outcome in one sentence. Then a table with a verdict for
          every capability and what you looked at; what you carved and why now; what you
          left and why; and one short paragraph on what looks likely next. That paragraph
          is a guess for a reader, never a list of tickets.
        - Leave the ambition where it stands. If every capability holds, carve nothing: the
          server refuses an epic under an ambition whose every capability's newest verdict is
          holds, so record your verdicts first. Comment on the ambition that nothing was
          carved and why. Only a person closes an ambition.
        - An idea that falls outside the capabilities goes to the Dream Board with
          `hatch dream`, not onto the board.
        - Write what you file in the voice the project's docs ask of a ticket: short
          sentences, no coined names, a title of a handful of words.
        - Name no board, repository, domain or person: nothing said here may be true of
          only the installation this happens to run on.
        """;

    [Fact]
    public void The_frozen_old_text_equals_the_constant_it_was_copied_from()
    {
        Assert.Equal(RewordAmbitionPassForCompletion.Replaced, RewordAmbitionPassForBudget.Stock);
    }

    [Fact]
    public void The_new_text_is_held_here_byte_for_byte()
    {
        Assert.Equal(NewText, RewordAmbitionPassForBudget.Replaced);
    }

    [Fact]
    public void The_new_text_reads_the_budget_and_asks_once_and_fits_the_column()
    {
        var replaced = RewordAmbitionPassForBudget.Replaced;
        var flat = System.Text.RegularExpressions.Regex.Replace(replaced, @"\s+", " ");

        Assert.Contains("hatch api GET /api/issues/<key>/budget", flat);
        Assert.Contains("stop here", flat);
        Assert.Contains("do not ask again", flat);
        Assert.DoesNotContain("Never ask it on the ambition", flat);
        Assert.Contains("Ask nothing else on the ambition", flat);
        Assert.True(replaced.Length <= EfHatchPlaybook.MaxPromptLength);
        Assert.NotEqual(RewordAmbitionPassForBudget.Stock, replaced);
    }

    [Fact]
    public void Up_guards_and_writes_DefaultPrompt_alone()
    {
        var sql = Sql(new Probe().Forward);

        Assert.Contains($"WHERE \"DefaultPrompt\" = $old${RewordAmbitionPassForBudget.Stock}$old$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $new${RewordAmbitionPassForBudget.Replaced}$new$", sql);

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

        Assert.Contains($"WHERE \"DefaultPrompt\" = $new${RewordAmbitionPassForBudget.Replaced}$new$", sql);
        Assert.Contains($"SET \"DefaultPrompt\" = $old${RewordAmbitionPassForBudget.Stock}$old$", sql);

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
        var id = await AddRowAsync(db, prompt: null, defaultPrompt: RewordAmbitionPassForBudget.Stock);

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Null(row.Prompt);
        Assert.Equal(RewordAmbitionPassForBudget.Replaced, row.DefaultPrompt);
        Assert.Equal(Seeded, row.UpdatedAt);
    }

    [SkippableFact]
    public async Task A_row_with_its_own_Prompt_keeps_it_byte_for_byte_and_still_learns_the_new_default()
    {
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: EditedPrompt, defaultPrompt: RewordAmbitionPassForBudget.Stock);

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Equal(EditedPrompt, row.Prompt);
        Assert.Equal(RewordAmbitionPassForBudget.Replaced, row.DefaultPrompt);
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
        var unedited = await AddRowAsync(db, prompt: null, defaultPrompt: RewordAmbitionPassForBudget.Stock);
        var edited = await AddRowAsync(db, prompt: EditedPrompt, defaultPrompt: RewordAmbitionPassForBudget.Stock);
        var unrelated = await AddRowAsync(db, prompt: null, defaultPrompt: UnrelatedDefault);

        await RunAsync(db, new Probe().Forward);
        var once = await SnapshotAsync(db, unedited, edited, unrelated);
        await RunAsync(db, new Probe().Forward);
        var twice = await SnapshotAsync(db, unedited, edited, unrelated);

        Assert.Equal(once, twice);
        Assert.Equal(
            new (string? Prompt, string? DefaultPrompt)[]
            {
                (null, RewordAmbitionPassForBudget.Replaced),
                (EditedPrompt, RewordAmbitionPassForBudget.Replaced),
                (null, UnrelatedDefault),
            },
            once);
    }

    [SkippableFact]
    public async Task Down_after_Up_returns_every_row_to_what_it_was_without_touching_UpdatedAt()
    {
        await using var db = await OpenAsync();
        var unedited = await AddRowAsync(db, prompt: null, defaultPrompt: RewordAmbitionPassForBudget.Stock);
        var edited = await AddRowAsync(db, prompt: EditedPrompt, defaultPrompt: RewordAmbitionPassForBudget.Stock);

        await RunAsync(db, new Probe().Forward);
        await RunAsync(db, new Probe().Backward);

        var uneditedRow = await ReadAsync(db, unedited);
        Assert.Null(uneditedRow.Prompt);
        Assert.Equal(RewordAmbitionPassForBudget.Stock, uneditedRow.DefaultPrompt);
        Assert.Equal(Seeded, uneditedRow.UpdatedAt);

        var editedRow = await ReadAsync(db, edited);
        Assert.Equal(EditedPrompt, editedRow.Prompt);
        Assert.Equal(RewordAmbitionPassForBudget.Stock, editedRow.DefaultPrompt);
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
    private sealed class Probe : RewordAmbitionPassForBudget
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

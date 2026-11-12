using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The stored playbooks name <c>/api/</c> rather than <c>/api/hatch/</c>, and
/// <c>/api/hatch-revision</c> stays as it was. Unlike the rewording migrations
/// this one is semantically neutral, so an edited <c>Prompt</c> is rewritten
/// along with <c>DefaultPrompt</c>.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class RewriteApiPrefixInPlaybooksTests
{
    private const string Old =
        "hatch api GET /api/hatch/issues/<key>, then /api/hatch/plan and /api/hatch-revision, twice: /api/hatch/dreams";

    private const string New =
        "hatch api GET /api/issues/<key>, then /api/plan and /api/hatch-revision, twice: /api/dreams";

    private static readonly DateTimeOffset Seeded = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static int _slot;

    [Fact]
    public void Up_replaces_the_prefix_in_both_columns_and_nothing_else()
    {
        var sql = string.Join("\0", new Probe().Forward.OfType<SqlOperation>().Select(o => o.Sql));

        Assert.Contains("\"Prompt\" = REPLACE(\"Prompt\", '/api/hatch/', '/api/')", sql);
        Assert.Contains("\"DefaultPrompt\" = REPLACE(\"DefaultPrompt\", '/api/hatch/', '/api/')", sql);
        Assert.DoesNotContain("UpdatedAt", sql);
        Assert.DoesNotContain("WHERE", sql);
    }

    [Fact]
    public void Down_does_nothing()
    {
        Assert.Empty(new Probe().Backward);
    }

    [SkippableFact]
    public async Task Both_columns_learn_the_new_prefix_and_hatch_revision_is_left_alone()
    {
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: Old, defaultPrompt: Old);

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Equal(New, row.Prompt);
        Assert.Equal(New, row.DefaultPrompt);
        Assert.Equal(Seeded, row.UpdatedAt);
    }

    [SkippableFact]
    public async Task A_null_Prompt_stays_null()
    {
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: null, defaultPrompt: Old);

        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Null(row.Prompt);
        Assert.Equal(New, row.DefaultPrompt);
    }

    [SkippableFact]
    public async Task Running_Up_twice_leaves_the_state_running_it_once_did()
    {
        await using var db = await OpenAsync();
        var id = await AddRowAsync(db, prompt: Old, defaultPrompt: Old);

        await RunAsync(db, new Probe().Forward);
        await RunAsync(db, new Probe().Forward);

        var row = await ReadAsync(db, id);
        Assert.Equal(New, row.Prompt);
        Assert.Equal(New, row.DefaultPrompt);
    }

    private static async Task<HatchContext> OpenAsync()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        return new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);
    }

    /// <summary>A playbook in a slot of its own: the unique index is on from, to, types and shape.</summary>
    private static async Task<int> AddRowAsync(HatchContext db, string? prompt, string? defaultPrompt)
    {
        var slot = Interlocked.Increment(ref _slot);
        var from = await db.Set<EfHatchStatus>().FirstOrDefaultAsync(s => s.Name == "From");
        if (from is null)
        {
            from = new EfHatchStatus { Name = "From", SortOrder = 1 };
            db.Add(from);
        }

        var to = new EfHatchStatus { Name = $"Rewrite slot {slot}", SortOrder = 200 + slot };
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
            await db.Database.ExecuteSqlRawAsync(operation.Sql.Replace("{", "{{").Replace("}", "}}"));
    }

    private static async Task<EfHatchPlaybook> ReadAsync(HatchContext db, int id)
    {
        db.ChangeTracker.Clear();
        return (await db.Set<EfHatchPlaybook>().FindAsync(id))!;
    }

    private sealed class Probe : RewriteApiPrefixInPlaybooks
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

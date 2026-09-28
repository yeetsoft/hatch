using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The stock conflict playbook is seeded once, where nobody has one, and takes
/// nothing back that somebody has written.
/// </summary>
public class SeedConflictPlaybookTests
{
    /// <summary>
    /// This test's own copy of the stock text, on purpose. A migration is frozen
    /// text: an edit to it that forgets the row's guard would fail a rollback,
    /// and an edit here that forgets the migration fails this instead.
    /// </summary>
    private const string Expected = """
        You are resolving merge conflicts on a branch that is already up for review.
        The trunk moved after its pull request opened, and the two no longer merge. The
        runner has checked the branch out and started merging the trunk into it: the
        merge is in progress, and the conflict section below names the files, the
        branch and both shas.

        - Resolve each conflicted file so that both sides' intent survives. The trunk's
          side is merged and reviewed work; the branch's side is what the pull request
          is for. Where the two cannot both stand, say why on the ticket and stop.
        - Fix the conflict and whatever the merge broke - a build error, a failing test
          - and nothing else. This is not the increment to improve the change.
        - Build and test with the repository's own commands until they are green.
        - Commit the merge and push it to the same branch. Never rebase, never
          force-push, and never push to the trunk.
        - Comment the new sha on the ticket.
        - Leave the ticket in the column it is in.
        """;

    [Fact]
    public void The_migration_seeds_the_text_this_test_holds()
    {
        Assert.Equal(Expected, SeedConflictPlaybook.Stock);
        Assert.Contains($"$prompt${Expected}$prompt$", Sql(Probe.Forward));
    }

    [Fact]
    public void The_prompt_tells_the_session_what_to_do_and_what_never_to_do()
    {
        var prompt = SeedConflictPlaybook.Stock;

        Assert.Contains("Build and test", prompt);
        Assert.Contains("Commit the merge and push it to the same branch", prompt);
        Assert.Contains("Never rebase, never\n  force-push", prompt.ReplaceLineEndings("\n"));
        Assert.True(prompt.Length <= EfHatchPlaybook.MaxPromptLength);
    }

    [Fact]
    public void It_seeds_every_type_with_sonnet_and_high_and_only_where_no_row_exists()
    {
        var sql = Sql(Probe.Forward);

        Assert.Contains("'', $prompt$", sql);
        Assert.Contains("'sonnet', 'high'", sql);
        Assert.Contains("WHERE NOT EXISTS", sql);
        Assert.Contains("p.\"FromStatusId\" = r.\"Id\" AND p.\"ToStatusId\" = r.\"Id\"", sql);
    }

    /// <summary>
    /// The review column measured the way <c>Columns.AwaitingReview</c> does:
    /// the column before the first terminal one, among the ones the board draws.
    /// </summary>
    [Fact]
    public void It_measures_the_review_column_and_does_not_name_it()
    {
        var sql = Sql(Probe.Forward);

        Assert.Contains("WHERE NOT \"IsDeferred\"", sql);
        Assert.Contains("MIN(n) FROM board WHERE \"IsTerminal\") - 1", sql);
        Assert.DoesNotContain("'review'", sql);
    }

    [Fact]
    public void Down_removes_only_a_row_still_saying_the_stock_text()
    {
        var sql = Sql(Probe.Backward);

        Assert.Contains("\"FromStatusId\" = \"ToStatusId\"", sql);
        Assert.Contains($"\"Prompt\" = $prompt${Expected}$prompt$", sql);
    }

    [SkippableFact]
    public async Task On_a_real_database_a_conflict_playbook_is_seeded_once_and_a_deleted_one_is_not_put_back()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        // A board whose review column is not called review, with a siding
        // sorted between it and done.
        var doing = new EfHatchStatus { Name = "Doing", SortOrder = 10 };
        var waiting = new EfHatchStatus { Name = "Waiting on Nathan", SortOrder = 20 };
        var shelf = new EfHatchStatus { Name = "Shelf", SortOrder = 25, IsDeferred = true };
        var done = new EfHatchStatus { Name = "Shipped", SortOrder = 30, IsTerminal = true };
        db.AddRange(doing, waiting, shelf, done);
        await db.SaveChangesAsync();

        foreach (var sql in Sql(Probe.Forward).Split("\u0000"))
            await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        var seeded = await db.Set<EfHatchPlaybook>().SingleAsync(p => p.FromStatusId == waiting.Id && p.ToStatusId == waiting.Id);
        Assert.Equal(Expected, seeded.Prompt);
        Assert.Equal("", seeded.Types);
        Assert.Equal("sonnet", seeded.Model);
        Assert.Equal("high", seeded.Effort);

        // Run again: the guard leaves it alone.
        foreach (var sql in Sql(Probe.Forward).Split("\u0000"))
            await db.Database.ExecuteSqlRawAsync(sql);
        Assert.Equal(1, await db.Set<EfHatchPlaybook>().CountAsync(p => p.FromStatusId == p.ToStatusId));

        // An operator deleted it: nothing puts it back that the operator did not.
        db.Remove(await db.Set<EfHatchPlaybook>().SingleAsync(p => p.FromStatusId == p.ToStatusId));
        await db.SaveChangesAsync();
        Assert.Equal(0, await db.Set<EfHatchPlaybook>().CountAsync(p => p.FromStatusId == p.ToStatusId));
    }

    private static string Sql(IReadOnlyList<MigrationOperation> operations) =>
        string.Join("\u0000", operations.OfType<SqlOperation>().Select(o => o.Sql));

    private static SeedConflictPlaybookProbe Probe => new();

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class SeedConflictPlaybookProbe : SeedConflictPlaybook
    {
        public IReadOnlyList<MigrationOperation> Forward => Operations(Up);

        public IReadOnlyList<MigrationOperation> Backward => Operations(Down);

        private IReadOnlyList<MigrationOperation> Operations(Action<MigrationBuilder> step)
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            step(builder);
            return builder.Operations;
        }
    }
}

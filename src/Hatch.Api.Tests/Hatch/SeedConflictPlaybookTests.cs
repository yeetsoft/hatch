using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The stock conflict playbook: seeded on the review column, for every type,
/// only where none exists - and taken back out on a rollback only while it
/// still says what the migration wrote.
/// </summary>
public class SeedConflictPlaybookTests
{
    /// <summary>
    /// The stock prompt, held here a second time on purpose. A migration is
    /// frozen text, so the words in it are not something to import: an edit to
    /// one copy that forgets the other has to fail this test rather than a
    /// rollback on somebody's install.
    /// </summary>
    private const string Stock = """
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
    public void The_migration_writes_the_stock_prompt_verbatim_in_both_directions()
    {
        var up = Sql(Probe.Operations(forward: true));
        var down = Sql(Probe.Operations(forward: false));

        Assert.Contains($"$prompt${Stock}$prompt$, 'sonnet', 'high'", up);
        Assert.Contains($"\"Prompt\" = $prompt${Stock}$prompt$", down);
    }

    [Fact]
    public void The_stock_prompt_fits_the_column_and_forbids_what_it_should()
    {
        Assert.True(Stock.Length <= EfHatchPlaybook.MaxPromptLength);
        Assert.Contains("Never rebase, never", Stock);
        Assert.Contains("force-push, and never push to the trunk", Stock);
    }

    [Fact]
    public void The_seed_is_for_every_type_and_guarded_by_an_existing_row_of_any_type()
    {
        var up = Sql(Probe.Operations(forward: true));

        Assert.Contains("'', $prompt$", up);
        Assert.Contains("WHERE p.\"FromStatusId\" = r.\"Id\" AND p.\"ToStatusId\" = r.\"Id\"", up);
        Assert.DoesNotContain("p.\"Types\"", up);
    }

    [SkippableFact]
    public async Task The_review_column_is_measured_and_a_row_already_there_is_left_alone()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        // A siding between the lanes, and a second terminal column: review is
        // the non-deferred column immediately left of the *first* terminal one.
        var backlog = new EfHatchStatus { Name = "Backlog", SortOrder = 10 };
        var doing = new EfHatchStatus { Name = "Doing", SortOrder = 20 };
        var awaiting = new EfHatchStatus { Name = "Awaiting", SortOrder = 30 };
        var parked = new EfHatchStatus { Name = "Parked", SortOrder = 35, IsDeferred = true };
        var done = new EfHatchStatus { Name = "Done", SortOrder = 40, IsTerminal = true };
        var dropped = new EfHatchStatus { Name = "Dropped", SortOrder = 50, IsTerminal = true };
        db.AddRange(backlog, doing, awaiting, parked, done, dropped);
        await db.SaveChangesAsync();

        await Run(db, forward: true);
        db.ChangeTracker.Clear();

        var seeded = await db.Set<EfHatchPlaybook>().SingleAsync();
        Assert.Equal(awaiting.Id, seeded.FromStatusId);
        Assert.Equal(awaiting.Id, seeded.ToStatusId);
        Assert.Equal("", seeded.Types);
        Assert.Equal(Stock, seeded.Prompt);
        Assert.Equal("sonnet", seeded.Model);
        Assert.Equal("high", seeded.Effort);

        // Run again: a row is there, so nothing is added.
        await Run(db, forward: true);
        db.ChangeTracker.Clear();
        Assert.Single(await db.Set<EfHatchPlaybook>().ToListAsync());

        // An edited row is the operator's, and a rollback leaves it.
        var row = await db.Set<EfHatchPlaybook>().SingleAsync();
        row.Prompt = Stock + "\n- Also this.";
        await db.SaveChangesAsync();
        await Run(db, forward: false);
        db.ChangeTracker.Clear();
        Assert.Single(await db.Set<EfHatchPlaybook>().ToListAsync());

        // An unedited one goes.
        row = await db.Set<EfHatchPlaybook>().SingleAsync();
        row.Prompt = Stock;
        await db.SaveChangesAsync();
        await Run(db, forward: false);
        db.ChangeTracker.Clear();
        Assert.Empty(await db.Set<EfHatchPlaybook>().ToListAsync());
    }

    private static async Task Run(HatchContext db, bool forward)
    {
        foreach (var sql in Probe.Operations(forward).OfType<SqlOperation>().Select(o => o.Sql))
            await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static string Sql(IReadOnlyList<MigrationOperation> operations) =>
        string.Join("\u0000", operations.OfType<SqlOperation>().Select(o => o.Sql));

    /// <summary>The migration's operations, without a database to run them against.</summary>
    private sealed class Probe : SeedConflictPlaybook
    {
        public static IReadOnlyList<MigrationOperation> Operations(bool forward)
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            var probe = new Probe();
            if (forward) probe.Up(builder);
            else probe.Down(builder);
            return builder.Operations;
        }
    }
}

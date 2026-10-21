using Hatch.Api.Modules.Hatch;
using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// No seeding migration turns a playbook into a check or denies it a tool, bar
/// the one that means to: <see cref="SeedReflectionPlaybook"/> denies its row the
/// tools that edit the tree, so a reflection judges and files rather than
/// changes. The review row still says what <see cref="RewordReviewPlaybook"/>
/// left it. <c>IsCheck</c> and <c>DeniedTools</c> are columns whose defaults are
/// the stock behaviour; a seed that named <c>IsCheck</c> would make a fresh
/// install differ from one that was never seeded, and so would one that named
/// <c>DeniedTools</c> on any row but the reflection row.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class NoSeededPlaybookIsACheckTests
{
    /// <summary>
    /// Runs under a plain <c>make test-api</c>: it reads the SQL each seeding
    /// migration emits and needs no database.
    /// </summary>
    [Fact]
    public void No_seeding_migration_names_IsCheck_and_only_the_reflection_one_names_DeniedTools()
    {
        var sql = string.Join("\n", Migrations.SelectMany(m => m.Forward));

        Assert.NotEmpty(sql);
        Assert.DoesNotContain("IsCheck", sql);

        var denying = Migrations.Where(m => m.Forward.Any(s => s.Contains("DeniedTools"))).ToList();
        var only = Assert.Single(denying);
        Assert.Equal(nameof(SeedReflectionPlaybook), only.Name);
        Assert.Contains($"'{ExpectedDeniedTools}'::jsonb", string.Join("\n", only.Forward));
    }

    [SkippableFact]
    public async Task On_a_real_database_every_seeded_row_but_the_reflection_one_keeps_the_column_defaults_and_the_review_row_is_the_reworded_one()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");

        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        // Stock names, which Playbooks joins on. The review status is added
        // here because the migration's own insert of it would trip the NOT NULL
        // bools EnsureCreated builds without defaults; with the row present its
        // guard skips it. Breakdown and Backlog are what SeedReflectionPlaybook
        // finds its transition by; without them it inserts nothing.
        db.AddRange(
            new EfHatchStatus { Name = "Breakdown", SortOrder = 5 },
            new EfHatchStatus { Name = "Backlog", SortOrder = 6 },
            new EfHatchStatus { Name = "inbox", SortOrder = 10 },
            new EfHatchStatus { Name = "todo", SortOrder = 20 },
            new EfHatchStatus { Name = "in progress", SortOrder = 30 },
            new EfHatchStatus { Name = "review", SortOrder = 40 },
            new EfHatchStatus { Name = "done", SortOrder = 50, IsTerminal = true });
        await db.SaveChangesAsync();

        // RetireEpicStartPlaybook is left out on purpose: whether the epic-start
        // row goes depends on IsWip, which is that migration's own concern.
        foreach (var migration in Migrations)
            foreach (var sql in migration.Forward)
                await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        var rows = await db.Set<EfHatchPlaybook>().ToListAsync();

        // All over an empty list passes without checking anything.
        Assert.NotEmpty(rows);
        Assert.All(rows, p =>
        {
            Assert.False(p.IsCheck);
            if (p.Types != "reflection")
                Assert.Null(p.DeniedTools);
        });

        var reflection = rows.Single(p => p.Types == "reflection");
        Assert.Equal(ExpectedRules, EfHatchPlaybook.ReadDeniedTools(reflection.DeniedTools));

        var review = rows.Single(p => p.FromStatusId == p.ToStatusId);
        Assert.Equal(RewordReviewPlaybook.Stock, review.Prompt);
    }

    /// <summary>
    /// Every migration that writes a playbook row, in migration order. Only the
    /// <see cref="SqlOperation"/>s: the table itself already exists by the time
    /// they run.
    /// </summary>
    private static IEnumerable<(string Name, IReadOnlyList<string> Forward)> Migrations =>
    [
        (nameof(Playbooks), Sql(new PlaybooksProbe().Forward)),
        (nameof(SeedConflictPlaybook), Sql(new SeedConflictPlaybookProbe().Forward)),
        (nameof(RewordReviewPlaybook), Sql(new RewordReviewPlaybookProbe().Forward)),
        (nameof(SeedCloseoutPlaybook), Sql(new SeedCloseoutPlaybookProbe().Forward)),
        (nameof(VerifyEpicPlaybook), Sql(new VerifyEpicPlaybookProbe().Forward)),
        (nameof(SeedReflectionPlaybook), Sql(new SeedReflectionPlaybookProbe().Forward)),
    ];

    // Held here rather than read off the migration, so an edit to the migration
    // alone fails a test; SeedReflectionPlaybookTests holds the same list.
    private const string ExpectedDeniedTools =
        """["Edit","Write","NotebookEdit","Bash(git add *)","Bash(git commit *)","Bash(git push *)","Bash(git checkout *)","Bash(git switch *)","Bash(git reset *)","Bash(git restore *)","Bash(git stash *)","Bash(git merge *)","Bash(git rebase *)","Bash(git cherry-pick *)","Bash(git clean *)"]""";

    private static readonly string[] ExpectedRules =
    [
        "Edit", "Write", "NotebookEdit",
        "Bash(git add *)", "Bash(git commit *)", "Bash(git push *)", "Bash(git checkout *)",
        "Bash(git switch *)", "Bash(git reset *)", "Bash(git restore *)", "Bash(git stash *)",
        "Bash(git merge *)", "Bash(git rebase *)", "Bash(git cherry-pick *)", "Bash(git clean *)",
    ];

    private static IReadOnlyList<string> Sql(IReadOnlyList<MigrationOperation> operations) =>
        operations.OfType<SqlOperation>().Select(o => o.Sql).ToList();

    private static IReadOnlyList<MigrationOperation> Operations(Action<MigrationBuilder> step)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        step(builder);
        return builder.Operations;
    }

    private sealed class PlaybooksProbe : Playbooks
    {
        public IReadOnlyList<MigrationOperation> Forward => Operations(Up);
    }

    private sealed class SeedConflictPlaybookProbe : SeedConflictPlaybook
    {
        public IReadOnlyList<MigrationOperation> Forward => Operations(Up);
    }

    private sealed class RewordReviewPlaybookProbe : RewordReviewPlaybook
    {
        public IReadOnlyList<MigrationOperation> Forward => Operations(Up);
    }

    private sealed class SeedCloseoutPlaybookProbe : SeedCloseoutPlaybook
    {
        public IReadOnlyList<MigrationOperation> Forward => Operations(Up);
    }

    private sealed class VerifyEpicPlaybookProbe : VerifyEpicPlaybook
    {
        public IReadOnlyList<MigrationOperation> Forward => Operations(Up);
    }

    private sealed class SeedReflectionPlaybookProbe : SeedReflectionPlaybook
    {
        public IReadOnlyList<MigrationOperation> Forward => Operations(Up);
    }
}

using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// A health poll sends the same statements whatever the board holds in review (HA-2790). The
/// per-card reads of the attention count - which playbook a card meets on its way into the merge
/// column, and which dependencies the live runners lack - are read once for the whole column, so
/// one card costs what twenty do. Measured on Postgres, at both of the cost seed's history sizes,
/// the way <see cref="DispatchReadCostTests"/> measures a dispatch read.
///
/// <para>It also holds what the poll no longer reads: the family gate is asked for its structure
/// alone, so no statement is sent to <c>ChangelogTasks</c>, and none to the work log for the
/// settled passes. The runners' lanes read the failed sessions off the work log, bounded by the
/// window and by the partial index on <c>IsError</c> (HA-2782); that one statement is allowed, and
/// is told apart by its text.</para>
///
/// <para>The review column is made for the test: a merge column after review that merges pull
/// requests and is protected, a playbook from review to it that is a check, and both projects
/// letting a passing check approve the merge. Cards in review alternate. A task meets the check
/// playbook and is left out of the count, which is the per-card playbook read. A bug does not, so
/// it is pre-approved with a clean merge check that declares one dependency, and the judge asks
/// the runners what they lack, which is the per-card fleet read.</para>
///
/// <para>It points at <c>HATCH_TEST_DATABASE_URL</c>, which is emptied on every seed, so it must
/// never name a database anybody uses.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class BoardHealthReadCostTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly int[] InReview = [1, 20];

    private sealed record Poll(string Size, int Cards, string Scope, int Statements, int IssueLoads, IReadOnlyList<ReadCounter.Statement> Sent);

    [SkippableFact]
    public async Task APoll_SendsTheSameStatements_WithOneCardInReviewAsWithTwenty()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the counts come from a real Postgres. Run `make test-api-db`, " +
            "or point the variable at a scratch database that may be dropped.");

        var polls = new List<Poll>();
        foreach (var size in new[] { ReadCostSeed.Small, ReadCostSeed.Large })
        {
            foreach (var cards in InReview)
            {
                var (connectionString, _) = await ReadCostSeed.SeedAsync(
                    size, Now, ReadCostSeed.Issues, async (db, board, issues) => await ExtendAsync(db, board, issues, cards));

                await using var setup = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);
                var statuses = await setup.Statuses.AsNoTracking().ToListAsync();

                foreach (var (scope, keys) in new (string, List<string>?)[] { ("unscoped", null), ("scoped", [ReadCostSeed.KeyA]) })
                {
                    var m = await ReadCounter.MeasureAsync(connectionString, async db =>
                    {
                        var result = await ReportReadCostTests.Harness.On(db, statuses).Health.GetBoardHealth("24h", keys, default);
                        Assert.True(result.Value is not null, "the poll was refused - a refusal costs nothing and would be written down as a cheap read");
                    });

                    polls.Add(new Poll(size.Name, cards, scope, m.Cost.Statements, m.Cost.IssueLoads, m.Statements));
                }
            }
        }

        var failures = new List<string>();

        // 1. The per-card reads ran at all, or equal counts are of a read that stopped early.
        foreach (var p in polls)
        {
            if (!p.Sent.Any(s => s.Table == "Playbooks"))
                failures.Add($"{Name(p)}: no statement read Playbooks, so the card's playbook was never looked up");
            if (p.Sent.Count(s => s.Table == "LegacyRunners") < 2)
                failures.Add($"{Name(p)}: the runners were read {p.Sent.Count(s => s.Table == "LegacyRunners")} time(s); the lanes read them once, and the fleet read should be a second");
        }

        // 2. The same cost whatever is in review, and at either size of history.
        foreach (var scope in new[] { "unscoped", "scoped" })
        {
            var these = polls.Where(p => p.Scope == scope).ToList();
            var first = these[0];
            foreach (var p in these.Skip(1))
            {
                if (p.Statements != first.Statements)
                    failures.Add($"{scope}: {Name(p)} sent {p.Statements} statements, {Name(first)} sent {first.Statements}");
                if (p.IssueLoads != first.IssueLoads)
                    failures.Add($"{scope}: {Name(p)} loaded Issues {p.IssueLoads} times, {Name(first)} loaded it {first.IssueLoads}");
            }
        }

        // 3. Issues is read end to end once: the priority tree's own load. The other loads are the
        //    trail's probes, which read an id and a time per issue and not the issue.
        foreach (var p in polls)
        {
            var treeLoads = p.Sent.Count(s => s.Table == "Issues" && s.Sql.Contains("\"RunnerTags\""));
            if (treeLoads != 1)
                failures.Add($"{Name(p)}: {treeLoads} statements loaded the whole issue row, not one");
        }

        // 4. Nothing the family gate reads for its changelog tasks or its settled passes.
        foreach (var p in polls)
        {
            foreach (var s in p.Sent.Where(s => s.Table == "ChangelogTasks"))
                failures.Add($"{Name(p)}: a statement began on ChangelogTasks: {Trim(s.Sql)}");
            foreach (var s in p.Sent.Where(s => s.Table == "WorkLogEntries" && !s.Sql.Contains("\"IsError\"")))
                failures.Add($"{Name(p)}: a statement began on WorkLogEntries and is not the lanes' failed-session read: {Trim(s.Sql)}");
        }

        if (failures.Count > 0)
            Assert.Fail(
                string.Join(Environment.NewLine, failures) + Environment.NewLine + Environment.NewLine
                + string.Join(Environment.NewLine, polls.Select(p => $"{Name(p)} ({p.Scope}): {p.Statements} statements, {p.IssueLoads} issue loads")));
    }

    private static string Name(Poll p) => $"{p.Cards} in review at the {p.Size} size";

    private static string Trim(string sql) => sql.ReplaceLineEndings(" ")[..Math.Min(sql.Length, 160)];

    /// <summary>
    /// The review column the test needs, and <paramref name="cards"/> cards standing in it: every
    /// card the seed left in review goes back to doing first, so only these are there. Even places
    /// are pre-approved bugs with a clean merge check; odd places are tasks.
    /// </summary>
    private static async Task ExtendAsync(HatchContext db, ReadCostSeed.Board board, IReadOnlyList<EfHatchIssue> issues, int cards)
    {
        var merge = new EfHatchStatus { Name = "merge", SortOrder = 38, IsProtected = true, MergesPullRequest = true };
        db.Statuses.Add(merge);

        foreach (var project in await db.Projects.ToListAsync()) project.CheckApprovesMerge = true;
        await db.SaveChangesAsync();

        db.Playbooks.Add(new EfHatchPlaybook
        {
            FromStatusId = board.Review, ToStatusId = merge.Id, Types = "task", Shape = "any", Prompt = "check it", Model = "sonnet",
            Effort = "high", IsCheck = true, DeniedTools = EfHatchPlaybook.WriteDeniedTools(null), CreatedAt = Now, UpdatedAt = Now,
        });

        foreach (var issue in issues.Where(i => i.StatusId == board.Review)) issue.StatusId = board.Doing;

        var bugs = issues.Where(i => i.Type == "bug").ToList();
        var tasks = issues.Where(i => i.Type == "task" && i.ParentId is not null).ToList();
        var made = 0;
        for (var n = 0; n < cards; n++)
        {
            var issue = n % 2 == 0 ? bugs[n / 2] : tasks[n / 2];
            issue.StatusId = board.Review;
            issue.PullRequestUrl = $"https://forge.example/owner/repo/pull/{n + 1}";
            made++;

            if (n % 2 != 0) continue;

            issue.PreApproval = PreApprovalLevels.PreApproved;
            db.MergeChecks.Add(new EfHatchMergeCheck
            {
                IssueId = issue.Id, Remote = "forge.example/owner/repo", Canonical = "forge.example/owner/repo", Trunk = "main",
                TrunkSha = new string('1', 40), Verdict = MergeVerdicts.Clean, Branch = "ha-1-thing", BranchSha = new string('2', 40),
                DependencyFilePath = ".hatch/dependencies.json", DependencyFileContent = "{\"dependencies\":[\"postgres\"]}",
                CheckedAt = Now, Runner = "box:/work/repo", CheckedBy = "runner",
            });
        }

        Assert.Equal(cards, made);
        await db.SaveChangesAsync();
    }
}

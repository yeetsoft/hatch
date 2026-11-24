using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The range reads behind the reports and the health poll, each shown to be
/// answered from an index (HA-2782). The statement EF really sent is run under
/// <c>EXPLAIN</c> on a session that has sequential scans turned off; the plan
/// must name the index and must not read the table end to end. It points at
/// <c>HATCH_TEST_DATABASE_URL</c>, which is emptied by each test, so it must
/// never name a database anybody uses.
/// </summary>
/// <remarks>
/// A test proves the index is usable, no more. A read that did not use its
/// index would have the index removed, not the test weakened.
/// </remarks>
[Collection(HatchDatabaseCollection.Name)]
public class ReportRangeIndexPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Since = Now.AddDays(-7);

    /// <summary>
    /// A board with thousands of rows that match none of the reads and a few
    /// that match each - events of many kinds over 120 days, sessions over the
    /// same, a few failed, questions with and without answers - then
    /// <c>VACUUM (ANALYZE)</c>, so the planner has the statistics it would have
    /// on a real one.
    /// </summary>
    private static async Task<string> SeedAsync()
    {
        var connectionString = await HatchDatabase.PrepareAsync();
        await using var db = new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var project = new EfHatchProject { Key = "RNG", Name = "Range", CreatedAt = Now.AddDays(-120) };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsWip = true, IsImplementation = true };
        db.AddRange(project, doing);
        await db.SaveChangesAsync();

        var issues = Enumerable.Range(1, 40).Select(n => new EfHatchIssue
        {
            ProjectId = project.Id, Number = n, Type = "task", Title = $"issue {n}", StatusId = doing.Id,
            Rank = 1024L * n, CreatedBy = "bo", CreatedAt = Now.AddDays(-110), UpdatedAt = Now,
        }).ToList();
        db.Issues.AddRange(issues);
        await db.SaveChangesAsync();

        string[] kinds =
        [
            EfHatchIssueEvent.StatusChanged, EfHatchIssueEvent.Commented, EfHatchIssueEvent.ClaimTaken,
            EfHatchIssueEvent.ClaimReleased, EfHatchIssueEvent.Stalled, EfHatchIssueEvent.Answered,
            EfHatchIssueEvent.Commented, EfHatchIssueEvent.Commented,
        ];
        for (var n = 0; n < 8000; n++)
        {
            var kind = kinds[n % kinds.Length];
            db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issues[n % issues.Count].Id, Actor = "bo", Kind = kind,
                Payload = kind == EfHatchIssueEvent.Answered && n % 16 == 5 ? "{\"questionId\":1,\"lapsed\":true}" : null,
                At = Now.AddMinutes(-n * 20),
            });
        }

        // Every fortieth session failed.
        for (var n = 0; n < 4000; n++)
        {
            db.WorkLog.Add(new EfHatchWorkLogEntry
            {
                IssueId = issues[n % issues.Count].Id, SessionId = $"s{n}",
                StartedAt = Now.AddMinutes(-n * 40), EndedAt = Now.AddMinutes(-n * 40 + 2), DurationMs = 120_000,
                Title = $"session {n}", Turns = 3, InputTokens = 10, OutputTokens = 2, CreatedAt = Now.AddMinutes(-n * 40 + 2),
                IssueType = "task", Outcome = ClaimOutcomes.Worked, IsError = n % 40 == 0,
                PlaybookFromStatusId = doing.Id, PlaybookToStatusId = doing.Id,
            });
        }

        // One comment in fifty is a question; every other question has an answer.
        var thread = new List<EfHatchComment>();
        for (var n = 0; n < 3000; n++)
        {
            thread.Add(new EfHatchComment
            {
                IssueId = issues[n % issues.Count].Id, Author = "bo", Body = $"comment {n}",
                Kind = n % 50 == 0 ? EfHatchComment.Question : EfHatchComment.Note, CreatedAt = Now.AddMinutes(-n * 60),
            });
        }

        db.Comments.AddRange(thread);
        await db.SaveChangesAsync();

        var answers = thread.Where(c => c.Kind == EfHatchComment.Question).Where((_, i) => i % 2 == 0)
            .Select(q => new EfHatchComment
            {
                IssueId = q.IssueId, Author = "me", Body = "answer", Kind = EfHatchComment.Note,
                AnswersId = q.Id, CreatedAt = q.CreatedAt.AddMinutes(30),
            })
            .ToList();
        db.Comments.AddRange(answers);
        await db.SaveChangesAsync();

        await db.Database.OpenConnectionAsync();
        await using var vacuum = db.Database.GetDbConnection().CreateCommand();
        vacuum.CommandText = "VACUUM (ANALYZE)";
        await vacuum.ExecuteNonQueryAsync();

        return connectionString;
    }

    /// <summary>
    /// The plan names <paramref name="index"/> when one is given, and reads
    /// <paramref name="table"/> by no sequential scan - the second is the claim
    /// the story makes, and the first says which index carries it.
    /// </summary>
    private static async Task<string> AssertIndexedAsync(string connectionString, PlanProbe.Sent sent, string table, string? index)
    {
        var plan = await PlanProbe.ExplainAsync(connectionString, sent);
        if (index is not null) Assert.Contains(index, plan);
        Assert.DoesNotContain(table, PlanProbe.SeqScans(plan));
        return plan;
    }

    [SkippableFact]
    public async Task EventsOfOneKind_FromATime_AreReadFromTheKindIndex()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the plan comes from a real Postgres.");
        var connectionString = await SeedAsync();

        // BoardHealthController.ReadSinceAsync, which is private: the same
        // predicate and projection.
        var sent = await PlanProbe.CaptureAsync(
            connectionString,
            db => db.IssueEvents.AsNoTracking()
                .Where(e => e.Kind == EfHatchIssueEvent.Stalled && e.At >= Since)
                .Select(e => new { e.IssueId, e.Id, e.Kind, e.At, e.Actor, e.Payload })
                .ToListAsync(),
            sql => sql.Contains("IssueEvents"));

        await AssertIndexedAsync(connectionString, sent, "IssueEvents", "IX_IssueEvents_Kind_At");
    }

    [SkippableFact]
    public async Task LapsedAnswers_Questions_AndAnswers_AreReadFromIndexes()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the plan comes from a real Postgres.");
        var connectionString = await SeedAsync();

        var sent = await PlanProbe.CaptureAllAsync(
            connectionString, db => QuestionTrail.ReadAsync(db, Since, Now, default));

        var events = sent.Single(c => c.Sql.Contains("\"IssueEvents\""));
        await AssertIndexedAsync(connectionString, events, "IssueEvents", "IX_IssueEvents_Kind_At");

        var questions = sent.Single(c => c.Sql.Contains("\"Comments\"") && c.Sql.Contains("EXISTS"));
        await AssertIndexedAsync(connectionString, questions, "Comments", "IX_Comments_Questions");

        var answers = sent.Single(c => c.Sql.Contains("\"Comments\"") && !c.Sql.Contains("EXISTS"));
        await AssertIndexedAsync(connectionString, answers, "Comments", "IX_Comments_Answers");
    }

    [SkippableFact]
    public async Task EventsOfSeveralKinds_FromATime_AreReadFromTheKindIndex()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the plan comes from a real Postgres.");
        var connectionString = await SeedAsync();

        string[] kinds = [EfHatchIssueEvent.ClaimTaken, EfHatchIssueEvent.ClaimReleased, EfHatchIssueEvent.ClaimCleared, EfHatchIssueEvent.ClaimLapsed];
        var sent = await PlanProbe.CaptureAsync(
            connectionString,
            db => db.IssueEvents.AsNoTracking()
                .Where(e => kinds.Contains(e.Kind) && e.At >= Since)
                .Select(e => new { e.IssueId, e.Id, e.Kind, e.At, e.Actor, e.Payload })
                .ToListAsync(),
            sql => sql.Contains("IssueEvents"));

        await AssertIndexedAsync(connectionString, sent, "IssueEvents", "IX_IssueEvents_Kind_At");
    }

    [SkippableFact]
    public async Task TheNewestEarlierEventOfAKind_PerIssue_IsReadFromTheThreeColumnIndex()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the plan comes from a real Postgres.");
        var connectionString = await SeedAsync();

        // The read HA-2783 will make; written here because it has not landed.
        var sent = await PlanProbe.CaptureAsync(
            connectionString,
            db => db.Issues.AsNoTracking()
                .Select(i => db.IssueEvents
                    .Where(x => x.IssueId == i.Id && x.Kind == EfHatchIssueEvent.ClaimTaken && x.At < Since)
                    .OrderByDescending(x => x.At)
                    .Select(x => (long?)x.Id)
                    .FirstOrDefault())
                .ToListAsync(),
            sql => sql.Contains("IssueEvents"));

        await AssertIndexedAsync(connectionString, sent, "IssueEvents", "IX_IssueEvents_IssueId_Kind_At");
    }

    [SkippableFact]
    public async Task SessionsInARange_AreReadFromTheEndTimeIndex()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the plan comes from a real Postgres.");
        var connectionString = await SeedAsync();

        var sent = await PlanProbe.CaptureAllAsync(
            connectionString,
            async db =>
            {
                var window = WorkLogRollup.Align(Since, Now, WorkLogBucketSize.Day, 0);
                await WorkLogRollup.SeriesAsync(db, window, WorkLogBucketSize.Day, null, null, null, default);
                await WorkLogRollup.SessionsAsync(db, Since, Now, null, null, WorkLogSort.Ended, 20, null, 5, default);
                await WorkLogRollup.ByPlaybookAsync(db, Since, Now, null, null, null, default);
            });

        var ranged = sent.Where(c => c.Sql.Contains("\"WorkLogEntries\"") && c.Sql.Contains("\"EndedAt\" >=")).ToList();
        Assert.NotEmpty(ranged);

        var plans = new List<string>();
        foreach (var statement in ranged)
            plans.Add(await AssertIndexedAsync(connectionString, statement, "WorkLogEntries", null));

        // Answered from the new index, not the (IssueId, EndedAt) one, which
        // cannot serve a range across issues.
        Assert.Contains(plans, p => p.Contains("IX_WorkLogEntries_EndedAt"));
    }

    [SkippableFact]
    public async Task TheFirstAndLastEndTime_AreOneRowOffEitherEndOfTheIndex()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the plan comes from a real Postgres.");
        var connectionString = await SeedAsync();

        var sent = await PlanProbe.CaptureAllAsync(
            connectionString,
            db => WorkLogRollup.SessionsAsync(db, Since, Now, null, null, WorkLogSort.Ended, 20, null, null, default));

        var bounds = sent.Where(c => c.Sql.Contains("LIMIT") && c.Sql.Contains("ORDER BY") && c.Sql.Contains("\"EndedAt\"")
            && !c.Sql.Contains("\"EndedAt\" >=")).ToList();
        Assert.Equal(2, bounds.Count);
        Assert.Contains(bounds, c => c.Sql.Contains("DESC"));
        Assert.Contains(bounds, c => !c.Sql.Contains("DESC"));

        foreach (var bound in bounds)
        {
            var plan = await AssertIndexedAsync(connectionString, bound, "WorkLogEntries", "IX_WorkLogEntries_EndedAt");
            Assert.DoesNotContain("Sort", PlanProbe.NodeTypes(plan));
        }
    }

    [SkippableFact]
    public async Task FailedSessions_FromATime_AreReadFromTheFailedIndex()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - the plan comes from a real Postgres.");
        var connectionString = await SeedAsync();

        // BoardHealthController.LanesAsync's read, which is private: the same
        // predicate and projection.
        var sent = await PlanProbe.CaptureAsync(
            connectionString,
            db => db.WorkLog.AsNoTracking()
                .Where(w => w.IsError && w.CreatedAt >= Since)
                .Select(w => new { w.IssueId, w.CreatedAt })
                .ToListAsync(),
            sql => sql.Contains("WorkLogEntries"));

        await AssertIndexedAsync(connectionString, sent, "WorkLogEntries", "IX_WorkLogEntries_FailedSessions");
    }
}

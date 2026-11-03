using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The half of <c>GET /api/hatch/reports/flow</c> that reads an event's
/// <c>jsonb</c> payload - the lapse prefilter - which EF's in-memory provider
/// runs as a plain C# <c>Contains</c> and Postgres refuses outright
/// (<c>operator does not exist: jsonb ~~ unknown</c>). The rest of the report
/// is in <see cref="ReportsControllerFlowTests"/>.
///
/// <para><b>These run against a real Postgres, and skip without one.</b> Point
/// <c>HATCH_TEST_DATABASE_URL</c> at a scratch database, or run
/// <c>make test-api-db</c>, which does it for you.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ReportsControllerFlowDatabaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task ALapsedQuestion_IsCountedApartFromAPersonsAnswer_OnPostgres()
    {
        await using var h = await NewAsync();
        var issue = await AddIssueAsync(h, Now.AddDays(-5));
        var at = Now.AddDays(-3);

        var lapsed = await AskAsync(h, issue.Id, at);
        await AnswerAsync(h, lapsed, "Hatch", at.AddSeconds(300), lapsed: true);
        var answered = await AskAsync(h, issue.Id, at);
        await AnswerAsync(h, answered, "bo", at.AddSeconds(3600));

        var result = await h.Reports().GetFlow(null, null, null, null, default);

        var flow = result.Value ?? throw new InvalidOperationException("expected a value");
        Assert.Equal(2, flow.Window.QuestionsAsked);
        Assert.Equal(1, flow.Window.QuestionsAnswered);
        Assert.Equal(1, flow.Window.QuestionsLapsed);
        Assert.Equal(3600, flow.Window.MedianAnswerWaitSeconds);
        Assert.Equal(3600, flow.Window.LongestAnswerWaitSeconds);
    }

    private static async Task<EfHatchIssue> AddIssueAsync(Harness h, DateTimeOffset createdAt)
    {
        var db = h.Connect();
        var issue = new EfHatchIssue
        {
            ProjectId = h.ProjectId,
            Number = await db.Issues.CountAsync(i => i.ProjectId == h.ProjectId) + 1,
            Type = "task", Title = "task", Description = "", StatusId = h.DoingStatusId, Rank = 1024,
            CreatedBy = "ann", CreatedAt = createdAt, UpdatedAt = createdAt,
        };
        db.Issues.Add(issue);
        await db.SaveChangesAsync();
        return issue;
    }

    private static async Task<EfHatchComment> AskAsync(Harness h, long issueId, DateTimeOffset at)
    {
        var db = h.Connect();
        var asked = new EfHatchComment
        {
            IssueId = issueId, Author = "ann", Body = "which way?", Kind = EfHatchComment.Question, CreatedAt = at,
        };
        db.Comments.Add(asked);
        await db.SaveChangesAsync();
        return asked;
    }

    private static async Task AnswerAsync(Harness h, EfHatchComment asked, string author, DateTimeOffset at, bool lapsed = false)
    {
        var db = h.Connect();
        db.Comments.Add(new EfHatchComment
        {
            IssueId = asked.IssueId, Author = author, Body = "that way", Kind = EfHatchComment.Answer,
            AnswersId = asked.Id, CreatedAt = at,
        });

        if (lapsed)
            db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = asked.IssueId, Actor = author, Kind = EfHatchIssueEvent.Answered, At = at,
                Payload = $"{{\"questionId\":{asked.Id},\"lapsed\":true}}",
            });

        await db.SaveChangesAsync();
    }

    // ---- Harness ----

    private static async Task<Harness> NewAsync()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - an event's payload is a jsonb column, which EF's in-memory " +
            "provider reads as a string. Run `make test-api-db`, or point the variable at a scratch database.");

        var connectionString = await HatchDatabase.PrepareAsync();

        await using var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(project, doing, done);
        await db.SaveChangesAsync();

        return new Harness { ConnectionString = connectionString, ProjectId = project.Id, DoingStatusId = doing.Id };
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly List<HatchContext> open = [];

        public required string ConnectionString { get; init; }
        public required int ProjectId { get; init; }
        public required int DoingStatusId { get; init; }

        /// <summary>A connection of its own, disposed with the harness.</summary>
        public HatchContext Connect()
        {
            var db = new HatchContext(
                new DbContextOptionsBuilder<HatchContext>().UseNpgsql(ConnectionString).Options);

            open.Add(db);
            return db;
        }

        public ReportsController Reports() =>
            new(Connect(), new StubActorDirectory(), TestClaims.With(), new FakeTimeProvider(Now));

        public async ValueTask DisposeAsync()
        {
            foreach (var db in open) await db.DisposeAsync();
        }
    }
}

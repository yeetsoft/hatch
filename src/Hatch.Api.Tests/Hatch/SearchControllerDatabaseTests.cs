using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What a word matches, how a match ranks and why it was found - the half of
/// the search read that lives in Postgres's full-text machinery.
///
/// <para><b>These run against a real Postgres, and skip without one.</b> A
/// <c>SearchVector</c> is a generated column and <c>@@</c> is Postgres's own
/// operator; EF's in-memory provider has neither, so everything that does not
/// need them is in <see cref="SearchControllerTests"/>. Point
/// <c>HATCH_TEST_DATABASE_URL</c> at a scratch database, or run
/// <c>make test-api-db</c>, which does it for you.</para>
///
/// <para>The words used survive the <c>english</c> configuration unchanged and
/// are not stop words, so a bare word is a prefix that finds itself.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class SearchControllerDatabaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    // ---- Ranking ----

    [SkippableFact]
    public async Task ATitleMatch_OutranksADescriptionMatch_WhateverOrderTheyWereFiledIn()
    {
        await using var h = await NewAsync();

        // Filed first, so insertion order - or the newest-first tie-break, which
        // would put the later one ahead - cannot be what orders them.
        var inDescription = await h.FileAsync("something else", "the gasket");
        var inTitle = await h.FileAsync("gasket");

        var result = await h.SearchAsync("gasket");

        Assert.Equal(2, result.Total);
        Assert.Equal([inTitle.Key, inDescription.Key], result.Hits.Select(x => x.Card.Key));
    }

    [SkippableFact]
    public async Task AClosedIssue_OutranksAnOpenOneAmongEquals_EvenFiledFirst()
    {
        await using var h = await NewAsync();

        // Filed first, so the later Id - which the final tie-break favors - would
        // put the open one ahead if closed-before-open did not win first.
        var closed = await h.FileAsync("gasket", closedAt: Now);
        var open = await h.FileAsync("gasket");

        var result = await h.SearchAsync("gasket");

        Assert.Equal(2, result.Total);
        Assert.Equal([closed.Key, open.Key], result.Hits.Select(x => x.Card.Key));
    }

    [SkippableFact]
    public async Task AWordMatch_CardCarriesTheIssuesOwnCreatedBy()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync("gasket", createdBy: "rall-runner");

        var result = await h.SearchAsync("gasket");

        Assert.Equal("rall-runner", Assert.Single(result.Hits).Card.CreatedBy);
    }

    // ---- Why it was found ----

    [SkippableFact]
    public async Task ACommentAlone_FindsTheIssue_AndSaysComment()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync("something else");
        await h.AddCommentAsync(issue.Id, "swap the sprocket before lunch");

        var result = await h.SearchAsync("sprocket");

        var hit = Assert.Single(result.Hits);
        Assert.Equal(issue.Key, hit.Card.Key);
        Assert.Equal(["comment"], hit.Matched);
    }

    [SkippableFact]
    public async Task AWorkLogSummaryAlone_FindsTheIssue_AndSaysWorkLog()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync("something else");
        await h.AddWorkLogAsync(issue.Id, title: "tidied up", summary: "replaced the manifold");

        var result = await h.SearchAsync("manifold");

        var hit = Assert.Single(result.Hits);
        Assert.Equal(issue.Key, hit.Card.Key);
        Assert.Equal(["work-log"], hit.Matched);
    }

    [SkippableFact]
    public async Task AWorkLogTitleAlone_FindsTheIssue_AndSaysWorkLog()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync("something else");
        await h.AddWorkLogAsync(issue.Id, title: "replaced the manifold", summary: "tidied up");

        var result = await h.SearchAsync("manifold");

        var hit = Assert.Single(result.Hits);
        Assert.Equal(issue.Key, hit.Card.Key);
        Assert.Equal(["work-log"], hit.Matched);
    }

    [SkippableFact]
    public async Task TwoMatchingComments_CountTheIssueOnce()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync("something else");
        await h.AddCommentAsync(issue.Id, "the gasket again");
        await h.AddCommentAsync(issue.Id, "still the gasket");

        var result = await h.SearchAsync("gasket");

        Assert.Equal(1, result.Total);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(issue.Key, hit.Card.Key);
        Assert.Equal(["comment"], hit.Matched);
    }

    [SkippableFact]
    public async Task Matched_FollowsTheDocumentedOrder_NotTheOrderThingsWereWritten()
    {
        await using var h = await NewAsync();

        // The previous title is set after the work log and the comment, so
        // insertion order would say work-log, comment, previous-title; the
        // report goes title, previous-title, description, comment, work-log.
        var issue = await h.FileAsync("the old gasket", "something else");
        await h.AddWorkLogAsync(issue.Id, title: "tidied up", summary: "the gasket held");
        await h.AddCommentAsync(issue.Id, "the gasket again");
        await h.RetitleAsync(issue.Id, "gasket");

        var result = await h.SearchAsync("gasket");

        var hit = Assert.Single(result.Hits);
        Assert.Equal(["title", "previous-title", "comment", "work-log"], hit.Matched);
    }

    [SkippableFact]
    public async Task ATitleAndAComment_AreReportedTitleFirst()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync("gasket");
        await h.AddCommentAsync(issue.Id, "the gasket again");

        var result = await h.SearchAsync("gasket");

        var hit = Assert.Single(result.Hits);
        Assert.Equal(["title", "comment"], hit.Matched);
    }

    // ---- Previous titles ----

    [SkippableFact]
    public async Task APreviousTitleAlone_FindsTheIssue_AndSaysPreviousTitle()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync("the gasket");
        await h.RetitleAsync(issue.Id, "something else");

        var result = await h.SearchAsync("gasket");

        var hit = Assert.Single(result.Hits);
        Assert.Equal(issue.Key, hit.Card.Key);
        Assert.Equal(["previous-title"], hit.Matched);
    }

    [SkippableFact]
    public async Task ATitleAndAPreviousTitleAndAComment_AreReportedInOrder()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync("the gasket");
        await h.RetitleAsync(issue.Id, "gasket");
        await h.AddCommentAsync(issue.Id, "the gasket again");

        var result = await h.SearchAsync("gasket");

        var hit = Assert.Single(result.Hits);
        Assert.Equal(["title", "previous-title", "comment"], hit.Matched);
    }

    [SkippableFact]
    public async Task WordsSplitBetweenTheCurrentAndAPreviousTitle_FindTheIssue_ButNameNeitherPart()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync("sprocket");
        await h.RetitleAsync(issue.Id, "gasket");

        var result = await h.SearchAsync("gasket sprocket");

        Assert.Equal(1, result.Total);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(issue.Key, hit.Card.Key);
        Assert.DoesNotContain("title", hit.Matched);
        Assert.DoesNotContain("previous-title", hit.Matched);
    }

    [SkippableFact]
    public async Task APreviousTitlesList_IsNewestFirstOnTheHit()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync("A");
        await h.RetitleAsync(issue.Id, "B");
        await h.RetitleAsync(issue.Id, "A");
        await h.RetitleAsync(issue.Id, "C");

        var result = await h.SearchAsync("C");

        var hit = Assert.Single(result.Hits);
        Assert.Equal(["A", "B"], hit.PreviousTitles);
    }

    [SkippableFact]
    public async Task AnIssueFoundByItsTitleAlone_HasAnEmptyPreviousTitlesList()
    {
        await using var h = await NewAsync();
        await h.FileAsync("gasket");

        var result = await h.SearchAsync("gasket");

        Assert.Empty(Assert.Single(result.Hits).PreviousTitles);
    }

    [SkippableFact]
    public async Task ANeverRetitledIssue_IsFoundAndRankedExactlyAsBefore()
    {
        await using var h = await NewAsync();
        var issue = await h.FileAsync("gasket");

        var result = await h.SearchAsync("gasket");

        var hit = Assert.Single(result.Hits);
        Assert.Equal(issue.Key, hit.Card.Key);
        Assert.Equal(["title"], hit.Matched);
        Assert.Empty(hit.PreviousTitles);
    }

    [SkippableFact]
    public async Task ACurrentTitleMatch_OutranksAPreviousTitleMatch_FiledAfterIt()
    {
        await using var h = await NewAsync();

        // The weaker match (previous title, weight B) filed first, so the
        // newest-first tie-break - which would put it ahead - cannot be what
        // orders them.
        var inPreviousTitle = await h.FileAsync("gasket");
        await h.RetitleAsync(inPreviousTitle.Id, "something else");
        var inTitle = await h.FileAsync("gasket");

        var result = await h.SearchAsync("gasket");

        Assert.Equal(2, result.Total);
        Assert.Equal([inTitle.Key, inPreviousTitle.Key], result.Hits.Select(x => x.Card.Key));
    }

    [SkippableFact]
    public async Task ACurrentTitleMatch_OutranksAPreviousTitleMatch_FiledBeforeIt()
    {
        await using var h = await NewAsync();

        // The stronger match filed first this time, so neither filing order
        // can be what hides a wrong sort.
        var inTitle = await h.FileAsync("gasket");
        var inPreviousTitle = await h.FileAsync("gasket");
        await h.RetitleAsync(inPreviousTitle.Id, "something else");

        var result = await h.SearchAsync("gasket");

        Assert.Equal(2, result.Total);
        Assert.Equal([inTitle.Key, inPreviousTitle.Key], result.Hits.Select(x => x.Card.Key));
    }

    [SkippableFact]
    public async Task APreviousTitleMatch_OutranksACommentMatch_FiledAfterIt()
    {
        await using var h = await NewAsync();

        // The weaker match (a comment, with no weight in the issue's own
        // vector) filed first.
        var inComment = await h.FileAsync("something else");
        await h.AddCommentAsync(inComment.Id, "the gasket again");
        var inPreviousTitle = await h.FileAsync("gasket");
        await h.RetitleAsync(inPreviousTitle.Id, "something else");

        var result = await h.SearchAsync("gasket");

        Assert.Equal(2, result.Total);
        Assert.Equal([inPreviousTitle.Key, inComment.Key], result.Hits.Select(x => x.Card.Key));
    }

    [SkippableFact]
    public async Task APreviousTitleMatch_OutranksACommentMatch_FiledBeforeIt()
    {
        await using var h = await NewAsync();

        var inPreviousTitle = await h.FileAsync("gasket");
        await h.RetitleAsync(inPreviousTitle.Id, "something else");
        var inComment = await h.FileAsync("something else");
        await h.AddCommentAsync(inComment.Id, "the gasket again");

        var result = await h.SearchAsync("gasket");

        Assert.Equal(2, result.Total);
        Assert.Equal([inPreviousTitle.Key, inComment.Key], result.Hits.Select(x => x.Card.Key));
    }

    // ---- Narrowing ----

    [SkippableFact]
    public async Task AStatusId_NarrowsToThatColumn_AndADoneIssueIsNotFoldedAway()
    {
        await using var h = await NewAsync();
        var finished = await h.FileAsync("gasket", statusId: h.DoneStatusId);

        var inDone = await h.SearchAsync("gasket", h.DoneStatusId);
        var inTodo = await h.SearchAsync("gasket", h.StatusId);
        var anywhere = await h.SearchAsync("gasket");

        Assert.Equal([finished.Key], inDone.Hits.Select(x => x.Card.Key));
        Assert.Empty(inTodo.Hits);
        Assert.Equal(0, inTodo.Total);
        Assert.Equal([finished.Key], anywhere.Hits.Select(x => x.Card.Key));
    }

    // ---- Harness ----

    private static async Task<Harness> NewAsync()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - a search vector is a Postgres generated column and a match is its " +
            "own operator, neither of which EF's in-memory provider has. Run `make test-api-db`, or point the " +
            "variable at a scratch database.");

        var connectionString = await HatchDatabase.PrepareAsync();

        await using var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 90, IsTerminal = true };
        db.AddRange(project, todo, done);
        await db.SaveChangesAsync();

        return new Harness
        {
            ConnectionString = connectionString,
            ProjectId = project.Id,
            StatusId = todo.Id,
            DoneStatusId = done.Id,
        };
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly List<HatchContext> open = [];
        private int next = 1;

        public required string ConnectionString { get; init; }
        public required int ProjectId { get; init; }
        public required int StatusId { get; init; }
        public required int DoneStatusId { get; init; }

        /// <summary>A connection of its own, disposed with the harness.</summary>
        public HatchContext Connect()
        {
            var db = new HatchContext(
                new DbContextOptionsBuilder<HatchContext>().UseNpgsql(ConnectionString).Options);

            open.Add(db);
            return db;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var db in open) await db.DisposeAsync();
        }

        /// <summary>An issue, placed directly - the create path is not under test here.</summary>
        public async Task<(long Id, string Key)> FileAsync(
            string title, string description = "", int? statusId = null, DateTimeOffset? closedAt = null,
            string createdBy = "operator")
        {
            var number = next++;
            var db = Connect();

            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId,
                Number = number,
                Type = "story",
                Title = title,
                Description = description,
                StatusId = statusId ?? StatusId,
                Rank = 1024 * number,
                CreatedBy = createdBy,
                CreatedAt = Now,
                UpdatedAt = Now,
                ClosedAt = closedAt,
            };

            db.Issues.Add(issue);
            await db.SaveChangesAsync();
            return (issue.Id, IssueKey.Format("AER", number));
        }

        /// <summary>
        /// Exercises the write path's own entity method, the same way
        /// <c>IssuesController</c>'s patch does, rather than poking
        /// <c>PreviousTitles</c> directly.
        /// </summary>
        public async Task RetitleAsync(long issueId, string to)
        {
            var db = Connect();
            var issue = await db.Issues.SingleAsync(i => i.Id == issueId);
            issue.Retitle(to);
            await db.SaveChangesAsync();
        }

        public async Task AddCommentAsync(long issueId, string body, string kind = EfHatchComment.Note)
        {
            var db = Connect();
            db.Comments.Add(new EfHatchComment
            {
                IssueId = issueId,
                Author = "operator",
                Body = body,
                Kind = kind,
                CreatedAt = Now,
            });

            await db.SaveChangesAsync();
        }

        public async Task AddWorkLogAsync(long issueId, string title, string summary)
        {
            var db = Connect();
            db.WorkLog.Add(new EfHatchWorkLogEntry
            {
                IssueId = issueId,
                SessionId = Guid.NewGuid().ToString("N"),
                StartedAt = Now.AddSeconds(-1),
                EndedAt = Now,
                DurationMs = 1_000,
                Title = title,
                Summary = summary,
                CreatedAt = Now,
            });

            await db.SaveChangesAsync();
        }

        /// <summary>
        /// A search through a context of its own, so nothing it returns was read
        /// through a tracker that already held the rows.
        /// </summary>
        public async Task<SearchDto> SearchAsync(string? q, int? statusId = null)
        {
            var search = new SearchController(
                Connect(), new StubActorDirectory(), TestClaims.With(), new FakeTimeProvider(Now));

            return Value(await search.GetSearch(q, null, null, statusId, null, null, null, null, 50, 0, default));
        }
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException("expected a value, got a refusal");
}

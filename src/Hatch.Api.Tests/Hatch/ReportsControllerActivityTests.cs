using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <c>GET /api/hatch/reports/activity</c>: one actor's events loaded, folded by
/// <see cref="ActivityReport"/> and answered as an <see cref="ActivityReportDto"/>,
/// wired against the in-memory database.
/// </summary>
public class ReportsControllerActivityTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required ReportsController Reports { get; init; }
        public required int ProjectId { get; init; }
        public required int StatusId { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required StubAuthService Keys { get; init; }
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var status = new EfHatchStatus { Name = "backlog", SortOrder = 10 };
        db.Add(project);
        db.Add(status);
        await db.SaveChangesAsync();

        var actors = new StubActorDirectory();
        var keys = new StubAuthService();

        return new Harness
        {
            Db = db,
            Actors = actors,
            Keys = keys,
            Reports = new ReportsController(
                db, actors, TestClaims.With(), new FakeTimeProvider(Now), keys),
            ProjectId = project.Id,
            StatusId = status.Id,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");

    private static string Refusal<T>(ActionResult<T> result) =>
        Assert.IsType<string>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);

    private static async Task<EfHatchIssue> AddIssueAsync(Harness h, int? projectId = null)
    {
        var project = projectId ?? h.ProjectId;
        var issue = new EfHatchIssue
        {
            ProjectId = project,
            Number = await h.Db.Issues.CountAsync(i => i.ProjectId == project) + 1,
            Type = "task", Title = "task", Description = "", StatusId = h.StatusId, Rank = 1024,
            CreatedBy = "ann", CreatedAt = Now.AddDays(-30), UpdatedAt = Now.AddDays(-30),
        };
        h.Db.Issues.Add(issue);
        await h.Db.SaveChangesAsync();
        return issue;
    }

    private static async Task<int> AddProjectAsync(Harness h, string key)
    {
        var project = new EfHatchProject { Key = key, Name = key, CreatedAt = Now };
        h.Db.Projects.Add(project);
        await h.Db.SaveChangesAsync();
        return project.Id;
    }

    private static async Task EventAsync(
        Harness h, long issueId, string actor, DateTimeOffset at, string kind = "retitled", string? payload = null)
    {
        h.Db.IssueEvents.Add(new EfHatchIssueEvent
        {
            IssueId = issueId, Actor = actor, Kind = kind, Payload = payload, At = at,
        });
        await h.Db.SaveChangesAsync();
    }

    /// <summary>A person with one key, the key's events typed under its own name.</summary>
    private static Guid APersonWithAKey(Harness h, string person, string key)
    {
        var owner = h.Actors.AddPerson(person);
        h.Keys.Keys.Add(new EfApiKey
        {
            Name = key, Prefix = "hatch_ak_xxxx", Hash = [1], CreatedAt = Now, OwnerPersonId = owner.Id,
        });
        return owner.Id;
    }

    private static async Task SessionAsync(Harness h, long issueId, DateTimeOffset from, DateTimeOffset to)
    {
        h.Db.WorkLog.Add(new EfHatchWorkLogEntry
        {
            IssueId = issueId, SessionId = Guid.NewGuid().ToString(), StartedAt = from, EndedAt = to, DurationMs = 1, CreatedAt = to,
        });
        await h.Db.SaveChangesAsync();
    }

    // ---- Refusals ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankActor_IsRefused_NamingIt(string? actor)
    {
        var h = await NewAsync();

        var result = await h.Reports.GetActivityReport(actor, null, null, null, ct: default);

        Assert.Equal("an actor is required", Refusal(result));
    }

    [Theory]
    [InlineData("yesterday-ish", null)]
    [InlineData(null, "not a date")]
    public async Task AnUnparseableBound_IsRefused_NamingIt(string? from, string? to)
    {
        var h = await NewAsync();

        var result = await h.Reports.GetActivityReport("ann", from, to, null, ct: default);

        Assert.Equal(RangeBounds.NotAnInstant(from ?? to), Refusal(result));
    }

    [Fact]
    public async Task AnInvertedRange_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Reports.GetActivityReport(
            "ann", Now.ToString("o"), Now.AddDays(-1).ToString("o"), null, ct: default);

        Assert.Equal("a range ends before it begins", Refusal(result));
    }

    [Fact]
    public async Task AProjectIdNamingNoProject_IsRefused_NamingIt()
    {
        var h = await NewAsync();

        var result = await h.Reports.GetActivityReport("ann", null, null, [h.ProjectId, 9999], ct: default);

        Assert.Equal("there is no project 9999", Refusal(result));
    }

    [Theory]
    [InlineData(-1441)]
    [InlineData(1441)]
    public async Task AnOffsetOutOfRange_IsRefused_NamingIt(int offsetMinutes)
    {
        var h = await NewAsync();

        var result = await h.Reports.GetActivityReport(
            "ann", null, null, null, offsetMinutes: offsetMinutes, ct: default);

        Assert.Equal(
            $"an offset from UTC is minutes between -1440 and 1440 - not {offsetMinutes}", Refusal(result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public async Task AGapOutOfRange_IsRefused_NamingIt(int gap)
    {
        var h = await NewAsync();

        var result = await h.Reports.GetActivityReport("ann", null, null, null, gap: gap, ct: default);

        Assert.Equal($"a gap is between 1 and 1440 minutes - not {gap}", Refusal(result));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(241)]
    public async Task ALeadOutOfRange_IsRefused_NamingIt(int lead)
    {
        var h = await NewAsync();

        var result = await h.Reports.GetActivityReport("ann", null, null, null, lead: lead, ct: default);

        Assert.Equal($"a lead is between 0 and 240 minutes - not {lead}", Refusal(result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task AWindowOutOfRange_IsRefused_NamingIt(int window)
    {
        var h = await NewAsync();

        var result = await h.Reports.GetActivityReport("ann", null, null, null, window: window, ct: default);

        Assert.Equal($"a window is between 1 and 90 days - not {window}", Refusal(result));
    }

    [Fact]
    public async Task TooManyBuckets_IsRefused_NamingTheCap()
    {
        var h = await NewAsync();

        var result = await h.Reports.GetActivityReport(
            "ann", "2020-01-01T00:00:00Z", "2026-01-31T00:00:00Z", null, ct: default);

        Assert.Contains($"more than the {WorkLogController.MaxBuckets} this answers in", Refusal(result));
    }

    // ---- The answer ----

    [Fact]
    public async Task AnActorNoRowAnswersTo_AnswersEmpty()
    {
        var h = await NewAsync();

        var dto = Value(await h.Reports.GetActivityReport("gone", null, null, null, ct: default));

        Assert.Equal("gone", dto.Actor);
        Assert.Equal(0, dto.Total);
        Assert.Empty(dto.Kinds);
        Assert.Empty(dto.Issues);
        Assert.Empty(dto.Events);
        Assert.False(dto.Truncated);
    }

    // ---- Include my keys ----

    [Fact]
    public async Task IncludeKeys_CountsOnlyWhatAHandTypedUnderThePersonsKey_MarkedViaKey()
    {
        var h = await NewAsync();
        APersonWithAKey(h, "ann", "ann-laptop");
        var issue = await AddIssueAsync(h);
        var other = await AddIssueAsync(h);
        var day = Now.AddDays(-2);

        await EventAsync(h, issue.Id, "ann", day);
        // The loop's night: a claim, work inside it, a session recorded after.
        await EventAsync(h, issue.Id, "ann-laptop", day.AddHours(1), "claim_taken");
        await EventAsync(h, issue.Id, "ann-laptop", day.AddHours(2), "status_changed");
        await EventAsync(h, issue.Id, "ann-laptop", day.AddHours(3), "claim_released");
        await SessionAsync(h, issue.Id, day.AddHours(1), day.AddHours(3));
        // A hand: after the release, outside the session.
        await EventAsync(h, issue.Id, "ann-laptop", day.AddHours(5), "commented");
        // A hand on an issue the loop never touched.
        await EventAsync(h, other.Id, "ann-laptop", day.AddHours(6), "dependency_added");
        // Somebody else's key.
        await EventAsync(h, other.Id, "bob-laptop", day.AddHours(7), "commented");

        var without = Value(await h.Reports.GetActivityReport("ann", null, null, null, ct: default));
        var with = Value(await h.Reports.GetActivityReport("ann", null, null, null, includeKeys: true, ct: default));

        Assert.Equal(1, without.Total);
        Assert.All(without.Events, e => Assert.Null(e.ViaKey));

        Assert.Equal(3, with.Total);
        Assert.Equal(2, with.Events.Count(e => e.ViaKey == "ann-laptop"));
        Assert.All(with.Events, e => Assert.Equal("ann", e.Actor));
        Assert.Equal(2, with.Issues.Count);
        Assert.NotEmpty(with.Sessions);
        Assert.Equal(3, with.Series.Sum(p => p.Events));
    }

    [Fact]
    public async Task IncludeKeys_WhenTheActorIsAKey_ChangesNothing()
    {
        var h = await NewAsync();
        APersonWithAKey(h, "ann", "ann-laptop");
        var issue = await AddIssueAsync(h);
        await EventAsync(h, issue.Id, "ann-laptop", Now.AddDays(-1), "commented");

        var dto = Value(await h.Reports.GetActivityReport(
            "ann-laptop", null, null, null, includeKeys: true, ct: default));

        Assert.Equal(1, dto.Total);
        Assert.All(dto.Events, e => Assert.Null(e.ViaKey));
    }

    [Fact]
    public async Task IncludeKeys_StillCapsTheRows_AndSaysTruncated()
    {
        var h = await NewAsync();
        APersonWithAKey(h, "ann", "ann-laptop");
        var issue = await AddIssueAsync(h);
        for (var i = 0; i < ActivityController.MaxEvents + 1; i++)
            h.Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id, Actor = "ann-laptop", Kind = "commented", At = Now.AddDays(-2).AddSeconds(i),
            });
        await h.Db.SaveChangesAsync();

        var dto = Value(await h.Reports.GetActivityReport("ann", null, null, null, includeKeys: true, ct: default));

        Assert.True(dto.Truncated);
        Assert.Equal(ActivityController.MaxEvents, dto.Events.Count);
        Assert.Equal(ActivityController.MaxEvents + 1, dto.Total);
    }

    [Fact]
    public async Task WithNeitherBound_TheRangeIsTheFortnightEndingNow()
    {
        var h = await NewAsync();
        var issue = await AddIssueAsync(h);
        await EventAsync(h, issue.Id, "ann", Now.AddDays(-20));

        var dto = Value(await h.Reports.GetActivityReport("ann", null, null, null, ct: default));

        Assert.Equal(Now, dto.To);
        Assert.Equal(Now.AddDays(-14), dto.From);
    }

    [Fact]
    public async Task AFromBeforeTheFirstEvent_IsClampedForwardToIt()
    {
        var h = await NewAsync();
        var issue = await AddIssueAsync(h);
        var first = Now.AddDays(-5);
        await EventAsync(h, issue.Id, "bob", first);
        await EventAsync(h, issue.Id, "ann", Now.AddDays(-2));

        var dto = Value(await h.Reports.GetActivityReport(
            "ann", Now.AddDays(-30).ToString("o"), null, null, ct: default));

        Assert.Equal(first, dto.From);
        Assert.Equal(1, dto.Total);
    }

    [Fact]
    public async Task OtherActorsAndOtherInstants_AreLeftOut_AndTheActorIsMatchedExactly()
    {
        var h = await NewAsync();
        var issue = await AddIssueAsync(h);
        await EventAsync(h, issue.Id, "ann", Now.AddDays(-1));
        await EventAsync(h, issue.Id, "bob", Now.AddDays(-1));
        await EventAsync(h, issue.Id, "ann ", Now.AddDays(-1));
        await EventAsync(h, issue.Id, "ann", Now.AddDays(-20));
        await EventAsync(h, issue.Id, "ann", Now);

        var dto = Value(await h.Reports.GetActivityReport("ann", null, null, null, ct: default));

        Assert.Equal(1, dto.Total);
        Assert.All(dto.Events, e => Assert.Equal("ann", e.Actor));
    }

    [Fact]
    public async Task ProjectId_NarrowsToEventsOnThatProjectsIssues()
    {
        var h = await NewAsync();
        var other = await AddProjectAsync(h, "OTH");
        var mine = await AddIssueAsync(h);
        var theirs = await AddIssueAsync(h, other);
        await EventAsync(h, mine.Id, "ann", Now.AddDays(-1));
        await EventAsync(h, theirs.Id, "ann", Now.AddDays(-2));

        var all = Value(await h.Reports.GetActivityReport("ann", null, null, null, ct: default));
        var narrowed = Value(await h.Reports.GetActivityReport("ann", null, null, [other], ct: default));

        Assert.Equal(2, all.Total);
        Assert.Equal(1, narrowed.Total);
        var issue = Assert.Single(narrowed.Issues);
        Assert.Equal("OTH-1", issue.Issue.Key);
    }

    [Fact]
    public async Task AboveTheCap_TheRowsAreCut_AndTheCountsAreNot()
    {
        var h = await NewAsync();
        var issue = await AddIssueAsync(h);
        for (var i = 0; i < ActivityController.MaxEvents + 1; i++)
            h.Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id, Actor = "ann", Kind = "retitled", At = Now.AddMinutes(-i - 1),
            });
        await h.Db.SaveChangesAsync();

        var dto = Value(await h.Reports.GetActivityReport("ann", null, null, null, ct: default));

        Assert.Equal(ActivityController.MaxEvents, dto.Events.Count);
        Assert.True(dto.Truncated);
        Assert.Equal(ActivityController.MaxEvents + 1, dto.Total);
        Assert.Equal(ActivityController.MaxEvents + 1, Assert.Single(dto.Kinds).Count);
        Assert.Equal(ActivityController.MaxEvents + 1, Assert.Single(dto.Issues).Count);
        Assert.Equal(dto.Events.OrderByDescending(e => e.At).Select(e => e.Id), dto.Events.Select(e => e.Id));
    }

    [Fact]
    public async Task ListedRows_CarryTheirPayload_AndIssuesCarryTheirCard()
    {
        var h = await NewAsync();
        var issue = await AddIssueAsync(h);
        await EventAsync(h, issue.Id, "ann", Now.AddDays(-1), "retitled", "{\"from\":\"a\",\"to\":\"b\"}");

        var dto = Value(await h.Reports.GetActivityReport("ann", null, null, null, ct: default));

        var row = Assert.Single(dto.Events);
        Assert.Equal("AER-1", row.IssueKey);
        Assert.Equal("b", row.Payload!.Value.GetProperty("to").GetString());

        var entry = Assert.Single(dto.Issues);
        Assert.Equal("AER-1", entry.Issue.Key);
        Assert.Equal(1, entry.Count);
        Assert.Equal(Now.AddDays(-1), entry.First);
        Assert.Equal(Now.AddDays(-1), entry.Last);
    }

    [Fact]
    public async Task TheSeriesAndTheSessions_NameTheirIssueAndCarryTheEstimate()
    {
        var h = await NewAsync();
        var issue = await AddIssueAsync(h);
        await EventAsync(h, issue.Id, "ann", Now.AddDays(-1));
        await EventAsync(h, issue.Id, "ann", Now.AddDays(-1).AddMinutes(10));

        var dto = Value(await h.Reports.GetActivityReport(
            "ann", null, null, null, gap: 30, lead: 15, window: 3, ct: default));

        Assert.Equal("hour", dto.Bucket);
        Assert.Equal(0, dto.OffsetMinutes);
        Assert.NotEmpty(dto.Series);
        Assert.All(dto.Series, p => Assert.True(p.RollingEvents >= 0));

        var session = Assert.Single(dto.Sessions);
        Assert.Equal(2, session.Events);
        Assert.Equal(["AER-1"], session.IssueKeys);
        Assert.Equal(10 + 15, session.EstimatedMinutes);

        Assert.Equal(1, dto.Estimate.Sessions);
        Assert.Equal(25, dto.Estimate.Minutes);
        Assert.Equal(30, dto.Estimate.GapMinutes);
        Assert.Equal(15, dto.Estimate.LeadMinutes);
        Assert.Equal(3, dto.Estimate.WindowDays);
    }
}

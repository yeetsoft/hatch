using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// Everything <see cref="DreamsController"/> does on the paths EF's in-memory
/// provider can run: adding, listing, editing and deleting a dream, and every
/// refusal in a sentence. Ranking a word and matching it needs a real
/// Postgres, and is proved in <see cref="DreamsControllerDatabaseTests"/>.
/// </summary>
public class DreamsControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    // ---- Adding ----

    [Fact]
    public async Task Add_Returns201WithThePostedBody_TheCallersNameAsAuthor_AndMatchingTimestamps()
    {
        var h = await NewAsync();

        var dream = await h.AddAsync("say this out loud");

        Assert.Equal("say this out loud", dream.Body);
        Assert.Equal("a person", dream.Author);
        Assert.Equal(Now, dream.CreatedAt);
        Assert.Equal(dream.CreatedAt, dream.UpdatedAt);
    }

    [Fact]
    public async Task Add_OmittedSourceAndProjectId_ComeBackNull()
    {
        var h = await NewAsync();

        var dream = await h.AddAsync("a loose thought");

        Assert.Null(dream.Source);
        Assert.Null(dream.ProjectId);
        Assert.Null(dream.ProjectKey);
    }

    [Fact]
    public async Task Add_GivenSourceAndProjectId_RoundTrip_AndProjectKeyMatchesTheSeededProject()
    {
        var h = await NewAsync();

        var dream = await h.AddAsync("leaning somewhere", source: "a meeting", projectId: h.ProjectAId);

        Assert.Equal("a meeting", dream.Source);
        Assert.Equal(h.ProjectAId, dream.ProjectId);
        Assert.Equal(h.ProjectAKey, dream.ProjectKey);
    }

    [Fact]
    public async Task Add_ANewDream_HasAnEmptyWovenInto()
    {
        var h = await NewAsync();

        var dream = await h.AddAsync("nothing has drawn on this yet");

        Assert.Empty(dream.WovenInto);
    }

    // ---- Add refusals ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Add_ABlankBody_Is400(string blank)
    {
        var h = await NewAsync();

        var result = await h.Controller.AddDream(new DreamCreateRequest(blank), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("a dream needs some words", bad.Value);
    }

    [Fact]
    public async Task Add_ABodyOverTheCap_Is400_AndOneOfExactlyTheCapSucceeds()
    {
        var h = await NewAsync();

        var over = await h.Controller.AddDream(new DreamCreateRequest(new string('x', EfHatchComment.MaxBodyLength + 1)), default);

        var bad = Assert.IsType<BadRequestObjectResult>(over.Result);
        Assert.Equal($"a dream is at most {EfHatchComment.MaxBodyLength} characters", bad.Value);

        var dream = await h.AddAsync(new string('x', EfHatchComment.MaxBodyLength));
        Assert.Equal(EfHatchComment.MaxBodyLength, dream.Body.Length);
    }

    [Fact]
    public async Task Add_ASourceOf501Characters_Is400()
    {
        var h = await NewAsync();

        var result = await h.Controller.AddDream(new DreamCreateRequest("a thought", new string('x', 501)), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("a dream's source is at most 500 characters", bad.Value);
    }

    [Fact]
    public async Task Add_AnUnknownProjectId_Is404()
    {
        var h = await NewAsync();

        var result = await h.Controller.AddDream(new DreamCreateRequest("a thought", null, h.ProjectAId + 1000), default);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal($"there is no project {h.ProjectAId + 1000}", notFound.Value);
    }

    // ---- Listing: no words ----

    [Fact]
    public async Task List_NoQ_ListsNewestFirst()
    {
        var h = await NewAsync();
        var first = await h.AddAsync("first");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        var second = await h.AddAsync("second");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        var third = await h.AddAsync("third");

        var page = await h.GetAsync();

        Assert.Equal([third.Id, second.Id, first.Id], page.Dreams.Select(d => d.Id));
    }

    [Fact]
    public async Task List_ProjectIdNarrows_AndALooseDreamIsAbsentFromEveryNarrowedList_ButPresentUnnarrowed()
    {
        var h = await NewAsync();
        var leaning = await h.AddAsync("leans toward the project", projectId: h.ProjectAId);
        var loose = await h.AddAsync("floats free");

        var narrowed = await h.GetAsync(projectId: [h.ProjectAId]);
        var unnarrowed = await h.GetAsync();

        Assert.Equal([leaning.Id], narrowed.Dreams.Select(d => d.Id));
        Assert.DoesNotContain(loose.Id, narrowed.Dreams.Select(d => d.Id));
        Assert.Equal(2, unnarrowed.Total);
        Assert.Contains(loose.Id, unnarrowed.Dreams.Select(d => d.Id));
    }

    [Fact]
    public async Task List_TwoProjectIds_AnswerEitherProjects()
    {
        var h = await NewAsync();
        var leaningA = await h.AddAsync("leans toward A", projectId: h.ProjectAId);
        var leaningB = await h.AddAsync("leans toward B", projectId: h.ProjectBId);
        var loose = await h.AddAsync("floats free");

        var narrowed = await h.GetAsync(projectId: [h.ProjectAId, h.ProjectBId]);

        Assert.Equal(
            new[] { leaningA.Id, leaningB.Id },
            narrowed.Dreams.Select(d => d.Id).OrderBy(id => id));
        Assert.DoesNotContain(loose.Id, narrowed.Dreams.Select(d => d.Id));
    }

    [Fact]
    public async Task List_ARepeatedProjectId_CountsOnce_SameAsOnce()
    {
        var h = await NewAsync();
        var leaning = await h.AddAsync("leans toward the project", projectId: h.ProjectAId);

        var narrowed = await h.GetAsync(projectId: [h.ProjectAId, h.ProjectAId]);

        Assert.Equal([leaning.Id], narrowed.Dreams.Select(d => d.Id));
    }

    [Fact]
    public async Task List_AnUnknownProjectIdMixedWithAKnownOne_AnswersTheKnownOnesRatherThanRefusing()
    {
        var h = await NewAsync();
        var leaning = await h.AddAsync("leans toward the project", projectId: h.ProjectAId);

        var narrowed = await h.GetAsync(projectId: [h.ProjectAId, 999_999]);

        Assert.Equal([leaning.Id], narrowed.Dreams.Select(d => d.Id));
    }

    [Fact]
    public async Task List_AnUnknownProjectIdAlone_AnswersNoRowsRatherThanARefusal()
    {
        var h = await NewAsync();
        await h.AddAsync("leans toward the project", projectId: h.ProjectAId);

        var narrowed = await h.GetAsync(projectId: [999_999]);

        Assert.Empty(narrowed.Dreams);
    }

    [Fact]
    public async Task List_IncludeNoProject_WithProjectIds_AnswersThoseProjectsAndEveryLooseDream()
    {
        var h = await NewAsync();
        var other = new EfHatchProject { Key = "ZZZ", Name = "Other", CreatedAt = Now };
        h.Db.Add(other);
        await h.Db.SaveChangesAsync();
        var leaningA = await h.AddAsync("leans toward A", projectId: h.ProjectAId);
        var leaningB = await h.AddAsync("leans toward B", projectId: h.ProjectBId);
        var leaningOther = await h.AddAsync("leans toward another", projectId: other.Id);
        var loose = await h.AddAsync("floats free");

        var scoped = await h.GetAsync(projectId: [h.ProjectAId, h.ProjectBId], includeNoProject: true);

        Assert.Equal(
            new[] { leaningA.Id, leaningB.Id, loose.Id }.OrderBy(id => id),
            scoped.Dreams.Select(d => d.Id).OrderBy(id => id));
        Assert.DoesNotContain(leaningOther.Id, scoped.Dreams.Select(d => d.Id));
        Assert.Equal(3, scoped.Total);
    }

    [Fact]
    public async Task List_IncludeNoProject_WithNoProjectId_ChangesNothing()
    {
        var h = await NewAsync();
        var leaningA = await h.AddAsync("leans toward A", projectId: h.ProjectAId);
        await h.AddAsync("leans toward B", projectId: h.ProjectBId);
        await h.AddAsync("floats free");

        var plain = await h.GetAsync();
        var included = await h.GetAsync(includeNoProject: true);
        var notIncluded = await h.GetAsync(projectId: [h.ProjectAId], includeNoProject: false);

        Assert.Equal(plain.Dreams.Select(d => d.Id), included.Dreams.Select(d => d.Id));
        Assert.Equal(3, included.Total);
        Assert.Equal([leaningA.Id], notIncluded.Dreams.Select(d => d.Id));
    }

    [Fact]
    public async Task List_IncludeNoProject_WithAnUnknownProjectIdAlone_AnswersOnlyTheLooseDreams()
    {
        var h = await NewAsync();
        await h.AddAsync("leans toward the project", projectId: h.ProjectAId);
        var loose = await h.AddAsync("floats free");

        var scoped = await h.GetAsync(projectId: [999_999], includeNoProject: true);

        Assert.Equal([loose.Id], scoped.Dreams.Select(d => d.Id));
    }

    [Fact]
    public async Task List_IncludeNoProject_PagesAcrossTheScopeAndTheLooseDreams()
    {
        var h = await NewAsync();
        await h.AddAsync("leans toward A", projectId: h.ProjectAId);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.AddAsync("leans toward B", projectId: h.ProjectBId);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.AddAsync("floats free");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        List<int> scope = [h.ProjectAId, h.ProjectBId];

        var whole = (await h.GetAsync(projectId: scope, includeNoProject: true)).Dreams.Select(d => d.Id).ToList();

        var first = await h.GetAsync(projectId: scope, limit: 1, offset: 0, includeNoProject: true);
        var second = await h.GetAsync(projectId: scope, limit: 1, offset: 1, includeNoProject: true);
        var third = await h.GetAsync(projectId: scope, limit: 1, offset: 2, includeNoProject: true);

        Assert.All([first, second, third], page => Assert.Equal(3, page.Total));
        Assert.Equal(whole, first.Dreams.Concat(second.Dreams).Concat(third.Dreams).Select(d => d.Id));
    }

    // ---- Listing: loose and issueKey ----

    [Fact]
    public async Task List_LooseTrue_NarrowsToDreamsWithNoThread_AndADreamWithOneIsAbsent()
    {
        var h = await NewAsync();
        var threaded = await h.AddAsync("an issue drew on this");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        var unthreaded = await h.AddAsync("nothing has");
        await h.SeedThreadAsync(threaded.Id, "AER-1", Now);

        var loose = await h.GetAsync(loose: true);
        var all = await h.GetAsync();

        Assert.Equal([unthreaded.Id], loose.Dreams.Select(d => d.Id));
        Assert.Equal(1, loose.Total);
        Assert.Equal([unthreaded.Id, threaded.Id], all.Dreams.Select(d => d.Id));
        Assert.Equal(2, all.Total);
    }

    [Fact]
    public async Task List_IssueKeyNarrowsToTheDreamsThatIssueDrewOn_AndAnUnknownIssueKeyIsEmptyNotAnError()
    {
        var h = await NewAsync();
        var drawnOn = await h.AddAsync("this one was drawn on");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.AddAsync("this one was not");
        await h.SeedThreadAsync(drawnOn.Id, "AER-1", Now);

        var narrowed = await h.GetAsync(issueKey: "AER-1");
        var unknown = await h.GetAsync(issueKey: "AER-404");

        Assert.Equal([drawnOn.Id], narrowed.Dreams.Select(d => d.Id));
        Assert.Equal(1, narrowed.Total);
        Assert.Empty(unknown.Dreams);
        Assert.Equal(0, unknown.Total);
    }

    [Fact]
    public async Task List_AMalformedIssueKey_Is400()
    {
        var h = await NewAsync();

        var result = await h.Controller.GetDreams(null, null, 50, 0, "nonsense", false, false, default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("\"nonsense\" is not an issue key", bad.Value);
    }

    [Fact]
    public async Task List_LooseAndIssueKey_CombineWithQAndProjectIdByAnd()
    {
        var h = await NewAsync();
        var leaningA = await h.AddAsync("leans to A", projectId: h.ProjectAId);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.AddAsync("leans to B", projectId: h.ProjectBId);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        var threadedToB = await h.AddAsync("threaded, but leans to B", projectId: h.ProjectBId);
        await h.SeedThreadAsync(threadedToB.Id, "AER-1", Now);

        var looseInA = await h.GetAsync(loose: true, projectId: [h.ProjectAId]);
        var threadedInA = await h.GetAsync(issueKey: "AER-1", projectId: [h.ProjectAId]);
        var contradiction = await h.GetAsync(loose: true, issueKey: "AER-1");

        Assert.Equal([leaningA.Id], looseInA.Dreams.Select(d => d.Id));
        Assert.Empty(threadedInA.Dreams);
        Assert.Equal(0, threadedInA.Total);
        Assert.Empty(contradiction.Dreams);
        Assert.Equal(0, contradiction.Total);
    }

    // ---- Listing: the page ----

    [Fact]
    public async Task List_TotalMatchesTheUnpagedCount_AndLimitOffsetPageCorrectly()
    {
        var h = await NewAsync();
        for (var n = 0; n < 5; n++)
        {
            await h.AddAsync($"dream {n}");
            h.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var whole = (await h.GetAsync()).Dreams.Select(d => d.Id).ToList();

        var first = await h.GetAsync(limit: 2, offset: 0);
        var second = await h.GetAsync(limit: 2, offset: 2);
        var third = await h.GetAsync(limit: 2, offset: 4);
        var beyond = await h.GetAsync(limit: 2, offset: 6);

        Assert.All([first, second, third, beyond], page => Assert.Equal(5, page.Total));
        Assert.Equal(whole, first.Dreams.Concat(second.Dreams).Concat(third.Dreams).Select(d => d.Id));
        Assert.Single(third.Dreams);
        Assert.Empty(beyond.Dreams);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task List_ALimitOutsideOneTo200_Is400(int limit)
    {
        var h = await NewAsync();

        var result = await h.Controller.GetDreams(null, null, limit, 0, default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal($"a limit is between 1 and {DreamsController.MaxLimit} - not {limit}", bad.Value);
    }

    [Fact]
    public async Task List_ANegativeOffset_Is400()
    {
        var h = await NewAsync();

        var result = await h.Controller.GetDreams(null, null, 50, -1, default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("an offset is not negative - not -1", bad.Value);
    }

    // ---- Listing: a q that reduces to no terms ----

    [Theory]
    [InlineData("")]
    [InlineData("!!! ()")]
    public async Task List_AQThatReducesToNoTerms_BehavesExactlyLikeNoQ(string q)
    {
        var h = await NewAsync();
        await h.AddAsync("one");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.AddAsync("two");

        var without = await h.GetAsync();
        var with = await h.GetAsync(q: q);

        Assert.Equal(without.Total, with.Total);
        Assert.Equal(without.Dreams.Select(d => d.Id), with.Dreams.Select(d => d.Id));
    }

    // ---- Editing ----

    [Fact]
    public async Task Edit_SettingOnlyBody_LeavesSourceAndProjectIdAsTheyWere()
    {
        var h = await NewAsync();
        var dream = await h.AddAsync("the old words", source: "a meeting", projectId: h.ProjectAId);

        var edited = await h.PatchAsync(dream.Id, """{"body":"the new words"}""");

        Assert.Equal("the new words", edited.Body);
        Assert.Equal("a meeting", edited.Source);
        Assert.Equal(h.ProjectAId, edited.ProjectId);
        Assert.Equal(h.ProjectAKey, edited.ProjectKey);
    }

    [Fact]
    public async Task Edit_SourceOfEmptyString_ClearsIt()
    {
        var h = await NewAsync();
        var dream = await h.AddAsync("a thought", source: "a meeting");

        var edited = await h.PatchAsync(dream.Id, """{"source":""}""");

        Assert.Null(edited.Source);
    }

    [Fact]
    public async Task Edit_ProjectIdOfNull_ClearsTheLean()
    {
        var h = await NewAsync();
        var dream = await h.AddAsync("a thought", projectId: h.ProjectAId);

        var edited = await h.PatchAsync(dream.Id, """{"projectId":null}""");

        Assert.Null(edited.ProjectId);
        Assert.Null(edited.ProjectKey);
    }

    [Fact]
    public async Task Edit_ANumericProjectId_MovesTheLean()
    {
        var h = await NewAsync();
        var dream = await h.AddAsync("a thought", projectId: h.ProjectAId);

        var edited = await h.PatchAsync(dream.Id, $$"""{"projectId":{{h.ProjectBId}}}""");

        Assert.Equal(h.ProjectBId, edited.ProjectId);
        Assert.Equal(h.ProjectBKey, edited.ProjectKey);
    }

    [Fact]
    public async Task Edit_AnUnknownProjectId_Is404()
    {
        var h = await NewAsync();
        var dream = await h.AddAsync("a thought");
        var unknown = h.ProjectAId + 1000;

        var result = await h.Controller.EditDream(dream.Id, Json($$"""{"projectId":{{unknown}}}"""), default);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal($"there is no project {unknown}", notFound.Value);
    }

    [Fact]
    public async Task Edit_NoRecognisedKeys_WritesNothing()
    {
        var h = await NewAsync();
        var dream = await h.AddAsync("a thought", source: "a meeting", projectId: h.ProjectAId);

        h.Time.Advance(TimeSpan.FromHours(1));
        var edited = await h.PatchAsync(dream.Id, """{"nonsense":"value"}""");

        Assert.Equal(dream.UpdatedAt, edited.UpdatedAt);
        Assert.Equal(dream.Body, edited.Body);
        Assert.Equal(dream.Source, edited.Source);
        Assert.Equal(dream.ProjectId, edited.ProjectId);
    }

    [Fact]
    public async Task Edit_ValuesIdenticalToWhatIsStored_WritesNothing()
    {
        var h = await NewAsync();
        var dream = await h.AddAsync("a thought", source: "a meeting", projectId: h.ProjectAId);

        h.Time.Advance(TimeSpan.FromHours(1));
        var edited = await h.PatchAsync(
            dream.Id, $$"""{"body":"a thought","source":"a meeting","projectId":{{h.ProjectAId}}}""");

        Assert.Equal(dream.UpdatedAt, edited.UpdatedAt);
        Assert.Equal(dream.Body, edited.Body);
        Assert.Equal(dream.Source, edited.Source);
        Assert.Equal(dream.ProjectId, edited.ProjectId);
    }

    [Fact]
    public async Task Edit_AnUnknownId_IsBareNotFound()
    {
        var h = await NewAsync();

        var result = await h.Controller.EditDream(999_999, Json("""{"body":"x"}"""), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ---- WovenInto ----

    [Fact]
    public async Task List_ADreamWithASeededThread_ReadsItBackInWovenInto_InKeyOrder()
    {
        var h = await NewAsync();
        var woven = await h.AddAsync("many issues drew on this");
        h.Time.Advance(TimeSpan.FromMinutes(1));
        var untouched = await h.AddAsync("none did");
        await h.SeedThreadAsync(woven.Id, "OPS-1", Now);
        await h.SeedThreadAsync(woven.Id, "AER-10", Now.AddMinutes(1));
        await h.SeedThreadAsync(woven.Id, "AER-2", Now.AddMinutes(2));

        var page = await h.GetAsync();

        Assert.Equal(["AER-2", "AER-10", "OPS-1"], page.Dreams.Single(d => d.Id == woven.Id).WovenInto);
        Assert.Empty(page.Dreams.Single(d => d.Id == untouched.Id).WovenInto);
    }

    [Fact]
    public async Task Edit_ADreamWithASeededThread_StillShowsWovenInto_AfterAnUnrelatedEdit()
    {
        var h = await NewAsync();
        var dream = await h.AddAsync("the old words");
        await h.SeedThreadAsync(dream.Id, "AER-1", Now);

        var edited = await h.PatchAsync(dream.Id, """{"body":"changed"}""");

        Assert.Equal("changed", edited.Body);
        Assert.Equal(["AER-1"], edited.WovenInto);
    }

    // ---- Deleting ----

    [Fact]
    public async Task Delete_RemovesTheRow_AndASecondGetNoLongerListsIt()
    {
        var h = await NewAsync();
        var dream = await h.AddAsync("a thought");

        var result = await h.Controller.DeleteDream(dream.Id, default);

        Assert.IsType<NoContentResult>(result);
        var remaining = await h.GetAsync();
        Assert.DoesNotContain(dream.Id, remaining.Dreams.Select(d => d.Id));
    }

    [Fact]
    public async Task Delete_AnUnknownId_IsBareNotFound()
    {
        var h = await NewAsync();

        var result = await h.Controller.DeleteDream(999_999, default);

        Assert.IsType<NotFoundResult>(result);
    }

    // ---- Harness ----

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required DreamsController Controller { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectAId { get; init; }
        public required string ProjectAKey { get; init; }
        public required int ProjectBId { get; init; }
        public required string ProjectBKey { get; init; }
        public required int StatusId { get; init; }

        public async Task<DreamDto> AddAsync(string body, string? source = null, int? projectId = null) =>
            Created(await Controller.AddDream(new DreamCreateRequest(body, source, projectId), default));

        public async Task<DreamsPageDto> GetAsync(
            string? q = null, List<int>? projectId = null, int limit = 50, int offset = 0, string? issueKey = null, bool loose = false,
            bool includeNoProject = false) =>
            Value(await Controller.GetDreams(q, projectId, limit, offset, issueKey, loose, includeNoProject, default));

        public async Task<DreamDto> PatchAsync(long id, string json) =>
            Value(await Controller.EditDream(id, Json(json), default));

        /// <summary>This file has no issue controller, so an issue is seeded straight onto the context.</summary>
        public async Task<EfHatchIssue> SeedIssueAsync(int projectId, int number)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = projectId, Number = number,
                Type = "task", Title = "t", Description = "", StatusId = StatusId, Rank = 1024,
                CreatedBy = "ann", CreatedAt = Now, UpdatedAt = Now,
            };
            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        /// <summary>
        /// Straight onto the table - a seed for the read tests, not a route under test. The issue is
        /// seeded first when absent: a thread read back needs a real issue row to name.
        /// </summary>
        public async Task SeedThreadAsync(long dreamId, string issueKey, DateTimeOffset at)
        {
            IssueKey.TryParse(issueKey, out var projectKey, out var number);
            var issue = await Db.Issues.WithKey(projectKey, number).FirstOrDefaultAsync()
                ?? await SeedIssueAsync(await Db.Projects.Where(p => p.Key == projectKey).Select(p => p.Id).FirstAsync(), number);
            Db.DreamThreads.Add(new EfHatchDreamThread { DreamId = dreamId, IssueId = issue.Id, Actor = "ann", At = at });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var projectA = new EfHatchProject { Key = "AER", Name = "Aerie", CreatedAt = Now };
        var projectB = new EfHatchProject { Key = "OPS", Name = "Ops", CreatedAt = Now };
        var status = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        db.AddRange(projectA, projectB, status);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "a person", CreatedAt = Now, UpdatedAt = Now } };

        return new Harness
        {
            Db = db,
            Controller = new DreamsController(db, caller, time),
            Time = time,
            ProjectAId = projectA.Id,
            ProjectAKey = projectA.Key,
            ProjectBId = projectB.Id,
            ProjectBKey = projectB.Key,
            StatusId = status.Id,
        };
    }

    private sealed class StubCallerIdentity : ICallerIdentity
    {
        public EfPerson? Person { get; set; }

        public EfApiKey? Key { get; set; }

        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult(Person?.Id);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(Person);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult(Key);

        public Actor? Local { get; set; }

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult(Local);

        public Task<bool> IsProgramAsync(CancellationToken ct) =>
            Task.FromResult(Key is not null || Local is { Kind: ActorKind.Key });

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? Local?.Name ?? CallerIdentity.Unattributed);
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {Reason(result.Result)}");

    private static string Reason(IActionResult? result) => result switch
    {
        ObjectResult o => o.Value?.ToString() ?? $"{o.StatusCode}",
        StatusCodeResult s => s.StatusCode.ToString(),
        null => "no result",
        _ => result.GetType().Name,
    };
}
